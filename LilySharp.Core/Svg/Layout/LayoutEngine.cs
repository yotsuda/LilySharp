using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Engine for calculating score layout.
/// </summary>
public sealed class LayoutEngine
{
    private readonly LayoutOptions _options;
    private readonly ElementCoordinator _elementCoordinator;
    private readonly SkylineBuilder _skylineBuilder;
    private readonly MeasureLayouter _measureLayouter = new();
    private readonly SystemLayouter _systemLayouter;
    private readonly PageLayouter _pageLayouter;
    private readonly SystemBreaker _systemBreaker;
    private readonly MultiStaffLayouter _multiStaffLayouter;

    public LayoutEngine(LayoutOptions? options = null)
    {
        _options = options ?? LayoutOptions.Default;
        _elementCoordinator = new ElementCoordinator(_options);
        _skylineBuilder = new SkylineBuilder(_options.StaffHeight);
        _systemLayouter = new SystemLayouter(_options, _measureLayouter);
        _pageLayouter = new PageLayouter(_options);
        _systemBreaker = new SystemBreaker(_options);
        _multiStaffLayouter = new MultiStaffLayouter(_options, _measureLayouter);
    }

    /// <summary>Calculates the complete layout for a single-staff score.</summary>
    public ScoreLayout Layout(Score score)
    {
        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);
        double headerBottom = _options.MarginTop + headerHeight;
        var systemMeasures = _systemBreaker.BreakIntoSystems(score);

        // Pre-calculate first system skylines for initial Y positioning
        var firstSystemMeasures = systemMeasures.Count > 0 ? systemMeasures[0] : new List<Measure>();
        var firstMeasureLayouts = _systemLayouter.LayoutMeasuresForSystem(firstSystemMeasures, score.KeySignature.Sharps, true, 0);
        var (firstUpSkyline, _) = _skylineBuilder.BuildSystemSkylines(firstSystemMeasures, firstMeasureLayouts);
        double currentY = LayoutUtilities.CalculateFirstSystemY(
            headerBottom, LayoutUtilities.CalculateUpExtent(firstUpSkyline), _options.TopSystemPadding);

        // Layout systems and build skylines in a single pass
        var systems = new List<SystemLayout>();
        var perSystemExtents = new List<(double upExtent, double downExtent)>();
        double maxSystemBottomY = 0;
        int firstMeasureIndex = 0;
        for (int sysIdx = 0; sysIdx < systemMeasures.Count; sysIdx++)
        {
            var system = _systemLayouter.LayoutSystem(
                sysIdx, systemMeasures[sysIdx], currentY,
                score.KeySignature.Sharps, sysIdx == 0, firstMeasureIndex,
                score.Lyrics, isLastSystem: sysIdx == systemMeasures.Count - 1);
            systems.Add(system);

            var (upSkyline, downSkyline) = _skylineBuilder.BuildSystemSkylines(systemMeasures[sysIdx], system.Measures);
            perSystemExtents.Add((
                LayoutUtilities.CalculateUpExtent(upSkyline),
                LayoutUtilities.CalculateDownExtent(downSkyline, _options.StaffHeight)));
            double bottomExtent = downSkyline.IsEmpty
                ? _options.StaffHeight : Math.Max(_options.StaffHeight, downSkyline.MaxHeight());
            maxSystemBottomY = Math.Max(maxSystemBottomY, system.Y + bottomExtent);

            currentY += _options.StaffHeight + _options.SystemSpacing;
            firstMeasureIndex += systemMeasures[sysIdx].Count;
        }

        var (pages, systemsArray) = CreatePages(
            systems.ToImmutableArray(), headerHeight, perSystemExtents,
            maxSystemBottomY + _options.MarginBottom);

        var beamLayouts = _elementCoordinator.LayoutBeams(score, systemsArray);
        var tieLayouts = _elementCoordinator.LayoutTies(score, systemsArray);
        var slurLayouts = _elementCoordinator.LayoutSlurs(score, systemsArray);
        var voiceOffsets = _elementCoordinator.CalculateVoiceOffsets(score);
        var restShifts = _elementCoordinator.CalculateRestShifts(score, systemsArray, beamLayouts);

        var annotations = CalculateAnnotationLayouts(
            score, systemsArray,
            score.Dynamics, score.Articulations, score.GraceNotes,
            score.Lyrics, score.MusicMarks, score.CustomTexts,
            score.VoltaBrackets, score.TupletBrackets, score.Voice.Measures);

        return BuildScoreLayout(pages, systemsArray, beamLayouts, tieLayouts, slurLayouts,
            annotations, voiceOffsets, restShifts);
    }

    /// <summary>Calculates the complete layout for a multi-staff score.</summary>
    public ScoreLayout Layout(MultiStaffScore score)
    {
        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);
        double headerBottom = _options.MarginTop + headerHeight;
        var systemMeasures = _systemBreaker.BreakIntoSystems(score);
        double systemHeight = _multiStaffLayouter.CalculateSystemHeight(score);
        var staffGroupLayouts = _multiStaffLayouter.LayoutStaffGroups(score, 0);

        // Pre-calculate first system skylines for initial Y positioning
        var firstSystemMeasureLayouts = systemMeasures.Count > 0
            ? _multiStaffLayouter.LayoutMeasures(score, 0, 0, systemMeasures[0].Count)
            : ImmutableArray<MeasureLayout>.Empty;
        var (firstUpSkyline, _) = _skylineBuilder.BuildSystemSkylines(score, firstSystemMeasureLayouts);
        double currentY = LayoutUtilities.CalculateFirstSystemY(
            headerBottom, LayoutUtilities.CalculateUpExtent(firstUpSkyline), _options.TopSystemPadding);

        // Layout each system with skyline extents
        var systems = new List<SystemLayout>();
        var perSystemExtents = new List<(double upExtent, double downExtent)>();
        int firstMeasureIndex = 0;
        for (int sysIdx = 0; sysIdx < systemMeasures.Count; sysIdx++)
        {
            bool isFirstSystem = sysIdx == 0;
            int measureCount = systemMeasures[sysIdx].Count;
            var measureLayouts = isFirstSystem
                ? firstSystemMeasureLayouts
                : _multiStaffLayouter.LayoutMeasures(score, sysIdx, firstMeasureIndex, measureCount,
                    sysIdx == systemMeasures.Count - 1);

            var (upSky, downSky) = _skylineBuilder.BuildSystemSkylines(score, measureLayouts);
            perSystemExtents.Add((
                LayoutUtilities.CalculateUpExtent(upSky),
                LayoutUtilities.CalculateDownExtent(downSky, systemHeight)));

            systems.Add(new SystemLayout(
                SystemIndex: sysIdx, Y: currentY, Width: _options.ContentWidth,
                PrefixWidth: SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, isFirstSystem),
                Measures: measureLayouts, StaffGroups: staffGroupLayouts));
            currentY += systemHeight + _options.SystemSpacing;
            firstMeasureIndex += measureCount;
        }

        var systemsArray = systems.ToImmutableArray();
        ImmutableArray<PageLayout> pages;
        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
        {
            pages = _pageLayouter.CreatePagesWithOptimalBreaking(
                systemsArray, headerHeight, perSystemExtents.ToImmutableArray());
        }
        else
        {
            (pages, systemsArray) = CreateMultiStaffPages(
                systems, headerHeight, perSystemExtents, systemHeight);
        }

        // Calculate beams/ties/slurs per staff
        var allBeamLayouts = new List<BeamLayout>();
        var allTieLayouts = new List<TieLayout>();
        var allSlurLayouts = new List<SlurLayout>();
        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            var staffScore = new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer);
            allBeamLayouts.AddRange(_elementCoordinator.LayoutBeams(staffScore, systemsArray, staffIndex));
            allTieLayouts.AddRange(_elementCoordinator.LayoutTies(staffScore, systemsArray, staffIndex));
            allSlurLayouts.AddRange(_elementCoordinator.LayoutSlurs(staffScore, systemsArray, staffIndex));
        }

        // Create primary staff Score for annotation engravers
        var primaryStaff = score.StaffGroups[0].PrimaryStaff;
        var primaryScore = new Score(
            primaryStaff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            ClefToString(primaryStaff.Clef));

        var annotations = CalculateAnnotationLayouts(
            primaryScore, systemsArray,
            score.Dynamics, score.Articulations, score.GraceNotes,
            score.Lyrics, score.MusicMarks, score.CustomTexts,
            score.VoltaBrackets, score.TupletBrackets, primaryStaff.PrimaryVoice.Measures);

        return BuildScoreLayout(pages, systemsArray,
            allBeamLayouts.ToImmutableArray(), allTieLayouts.ToImmutableArray(),
            allSlurLayouts.ToImmutableArray(), annotations,
            ImmutableDictionary<VoiceItemKey, double>.Empty,
            ImmutableDictionary<RestShiftKey, double>.Empty);
    }

    private (ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems) CreatePages(
        ImmutableArray<SystemLayout> systems, double headerHeight,
        List<(double upExtent, double downExtent)> perSystemExtents, double totalHeight)
    {
        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
        {
            var pages = _pageLayouter.CreatePagesWithOptimalBreaking(
                systems, headerHeight, perSystemExtents.ToImmutableArray());
            return (pages, pages.SelectMany(p => p.Systems).ToImmutableArray());
        }
        var page = new PageLayout(0, _options.PageWidth, totalHeight, headerHeight, systems);
        return (ImmutableArray.Create(page), systems);
    }

    private (ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems) CreateMultiStaffPages(
        List<SystemLayout> systems, double headerHeight,
        List<(double upExtent, double downExtent)> perSystemExtents, double systemHeight)
    {
        double headerBottom = _options.MarginTop + headerHeight;
        double skylineY = LayoutUtilities.CalculateFirstSystemY(
            headerBottom, perSystemExtents[0].upExtent, _options.TopSystemPadding);
        var updatedSystems = new List<SystemLayout>();
        for (int i = 0; i < systems.Count; i++)
        {
            updatedSystems.Add(systems[i] with { Y = skylineY });
            if (i < systems.Count - 1)
            {
                double padding = _options.SystemSpacing * 0.5;
                double minDistance = systemHeight + Math.Max(_options.SystemSpacing,
                    perSystemExtents[i].downExtent + perSystemExtents[i + 1].upExtent + padding);
                skylineY += minDistance;
            }
        }
        double totalHeight = skylineY + systemHeight + _options.MarginBottom;
        var systemsArray = updatedSystems.ToImmutableArray();
        var page = new PageLayout(0, _options.PageWidth, totalHeight, headerHeight, systemsArray);
        return (ImmutableArray.Create(page), systemsArray);
    }

    private AnnotationLayouts CalculateAnnotationLayouts(
        Score? score, ImmutableArray<SystemLayout> systems,
        ImmutableArray<DynamicItem> dynamics, ImmutableArray<ArticulationItem> articulations,
        ImmutableArray<GraceNoteItem> graceNotes, ImmutableArray<LyricItem> lyrics,
        ImmutableArray<MusicMarkItem> musicMarks, ImmutableArray<CustomTextItem> customTexts,
        ImmutableArray<VoltaBracketItem> voltaBrackets, ImmutableArray<TupletBracketItem> tupletBrackets,
        ImmutableArray<Measure> measures)
    {
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var lyricLayouts = new LyricEngraver().CalculateLayouts(lyrics, ml, _options.StaffHeight);
        return new AnnotationLayouts(
            Dynamics: score != null ? DynamicEngraver.Calculate(score, dynamics, systems, ml) : ImmutableArray<DynamicLayout>.Empty,
            Articulations: score != null ? ArticulationEngraver.Calculate(score, articulations, systems, ml) : ImmutableArray<ArticulationLayout>.Empty,
            GraceNotes: score != null ? GraceNoteEngraver.Calculate(score, graceNotes, systems, ml) : ImmutableArray<GraceNoteLayout>.Empty,
            Lyrics: lyricLayouts,
            LyricHyphens: new LyricHyphenEngraver().CalculateLayouts(lyricLayouts, systems),
            MusicMarks: MusicMarkEngraver.Calculate(score, musicMarks, systems, ml),
            CustomTexts: CustomTextEngraver.Calculate(score, customTexts, systems, ml),
            VoltaBrackets: VoltaBracketEngraver.Calculate(voltaBrackets, systems, ml),
            TupletBrackets: TupletBracketEngraver.Calculate(tupletBrackets, systems, ml, measures));
    }

    private static ScoreLayout BuildScoreLayout(
        ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems,
        ImmutableArray<BeamLayout> beams, ImmutableArray<TieLayout> ties,
        ImmutableArray<SlurLayout> slurs, AnnotationLayouts a,
        ImmutableDictionary<VoiceItemKey, double> voiceOffsets,
        ImmutableDictionary<RestShiftKey, double> restShifts)
    {
        return new ScoreLayout(pages, systems, beams, ties, slurs,
            a.Dynamics, a.Articulations, a.GraceNotes,
            a.Lyrics, a.LyricHyphens, a.MusicMarks,
            a.CustomTexts, a.VoltaBrackets, a.TupletBrackets,
            voiceOffsets, restShifts);
    }

    private static string ClefToString(ClefType clef) => clef switch
    {
        ClefType.Treble => "treble",
        ClefType.Bass => "bass",
        ClefType.Alto => "alto",
        ClefType.Tenor => "tenor",
        ClefType.Treble8Below => "treble_8",
        _ => "treble"
    };

    private sealed record AnnotationLayouts(
        ImmutableArray<DynamicLayout> Dynamics,
        ImmutableArray<ArticulationLayout> Articulations,
        ImmutableArray<GraceNoteLayout> GraceNotes,
        ImmutableArray<LyricLayout> Lyrics,
        ImmutableArray<LyricHyphenLayout> LyricHyphens,
        ImmutableArray<MusicMarkLayout> MusicMarks,
        ImmutableArray<CustomTextLayout> CustomTexts,
        ImmutableArray<VoltaBracketLayout> VoltaBrackets,
        ImmutableArray<TupletBracketLayout> TupletBrackets);
}
