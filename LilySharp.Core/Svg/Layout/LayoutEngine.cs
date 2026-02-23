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

            currentY += _options.StaffHeight + _options.SystemSpacing;
            firstMeasureIndex += systemMeasures[sysIdx].Count;
        }

        var (pages, systemsArray) = CreatePages(
            systems.ToImmutableArray(), headerHeight, perSystemExtents, _options.StaffHeight);

        var beamLayouts = _elementCoordinator.LayoutBeams(score, systemsArray);
        var tieLayouts = _elementCoordinator.LayoutTies(score, systemsArray);
        var slurLayouts = _elementCoordinator.LayoutSlurs(score, systemsArray);
        var glissandoLayouts = _elementCoordinator.LayoutGlissandos(score, systemsArray);
        var voiceOffsets = _elementCoordinator.CalculateVoiceOffsets(score);
        var restShifts = _elementCoordinator.CalculateRestShifts(score, systemsArray, beamLayouts);

        var annotations = CalculateAnnotationLayouts(
            score, systemsArray,
            score.Dynamics, score.Articulations, score.GraceNotes,
            score.Lyrics, score.MusicMarks, score.CustomTexts,
            score.VoltaBrackets, score.TupletBrackets, score.Arpeggios,
            score.Voice.Measures, score.FiguredBasses, score.ChordNames, score.PercentRepeats,
            trillSpanners: score.TrillSpanners);

        // Calculate part combination layouts for multi-voice scores
        var partCombineLayouts = ImmutableArray<PartCombineLayout>.Empty;
        if (score.IsMultiVoice && score.Voices.Length >= 2)
        {
            var ml = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();
            var combineItems = PartCombineAnalyzer.Analyze(
                score.Voices[0], score.Voices[1], score.TimeSignature);
            partCombineLayouts = PartCombineAnalyzer.Calculate(combineItems, ml);
        }

        return BuildScoreLayout(pages, systemsArray, beamLayouts, tieLayouts, slurLayouts,
            glissandoLayouts, annotations, voiceOffsets, restShifts, partCombineLayouts);
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
        var (firstUpSkyline, _) = _skylineBuilder.BuildSystemSkylines(score, firstSystemMeasureLayouts, systemHeight);
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

            var (upSky, downSky) = _skylineBuilder.BuildSystemSkylines(score, measureLayouts, systemHeight);
            perSystemExtents.Add((
                LayoutUtilities.CalculateUpExtent(upSky),
                LayoutUtilities.CalculateDownExtent(downSky, systemHeight)));

            systems.Add(new SystemLayout(
                SystemIndex: sysIdx, Y: currentY, Width: _options.ContentWidth,
                PrefixWidth: SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, isFirstSystem,
                    score.TimeSignature.Beats, score.TimeSignature.BeatType),
                Measures: measureLayouts, StaffGroups: staffGroupLayouts));
            currentY += systemHeight + _options.SystemSpacing;
            firstMeasureIndex += measureCount;
        }

        var (pages, systemsArray) = CreatePages(
            systems.ToImmutableArray(), headerHeight, perSystemExtents, systemHeight);

        // Calculate beams/ties/slurs/glissandos per staff
        var allBeamLayouts = new List<BeamLayout>();
        var allTieLayouts = new List<TieLayout>();
        var allSlurLayouts = new List<SlurLayout>();
        var allGlissandoLayouts = new List<GlissandoLayout>();
        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            var staffScore = new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer);
            allBeamLayouts.AddRange(_elementCoordinator.LayoutBeams(staffScore, systemsArray, staffIndex));
            allTieLayouts.AddRange(_elementCoordinator.LayoutTies(staffScore, systemsArray, staffIndex));
            allSlurLayouts.AddRange(_elementCoordinator.LayoutSlurs(staffScore, systemsArray, staffIndex));
            allGlissandoLayouts.AddRange(_elementCoordinator.LayoutGlissandos(staffScore, systemsArray, staffIndex));
        }

        // Resolve cross-staff layouts per voice
        var crossStaffLayouts = ImmutableArray<CrossStaffLayout>.Empty;
        if (!score.CrossStaffItems.IsDefaultOrEmpty)
        {
            // Use primary staff index (0) as default source; in full multi-voice,
            // each voice would have its own staff index.
            int primaryStaffIdx = 0;
            int staffCount = score.StaffGroups.Sum(g => g.StaffCount);
            crossStaffLayouts = CrossStaffEngraver.Calculate(
                score.CrossStaffItems, primaryStaffIdx, staffCount);
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
            score.VoltaBrackets, score.TupletBrackets, score.Arpeggios,
            primaryStaff.PrimaryVoice.Measures, score.FiguredBasses, score.ChordNames,
            score.PercentRepeats, crossStaffLayouts,
            trillSpanners: score.TrillSpanners);

        // Calculate part combination layouts for staves with multiple voices
        var partCombineLayouts = ImmutableArray<PartCombineLayout>.Empty;
        foreach (var group in score.StaffGroups)
        {
            foreach (var staff in group.Staves)
            {
                if (staff.Voices.Length >= 2)
                {
                    var ml = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();
                    var combineItems = PartCombineAnalyzer.Analyze(
                        staff.Voices[0], staff.Voices[1], score.TimeSignature);
                    partCombineLayouts = PartCombineAnalyzer.Calculate(combineItems, ml);
                    break; // Only first multi-voice staff for now
                }
            }
            if (!partCombineLayouts.IsEmpty) break;
        }

        return BuildScoreLayout(pages, systemsArray,
            allBeamLayouts.ToImmutableArray(), allTieLayouts.ToImmutableArray(),
            allSlurLayouts.ToImmutableArray(), allGlissandoLayouts.ToImmutableArray(),
            annotations,
            ImmutableDictionary<VoiceItemKey, double>.Empty,
            ImmutableDictionary<RestShiftKey, double>.Empty,
            partCombineLayouts);
    }

    private (ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems) CreatePages(
        ImmutableArray<SystemLayout> systems, double headerHeight,
        List<(double upExtent, double downExtent)> perSystemExtents, double systemHeight)
    {
        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
        {
            var pages = _pageLayouter.CreatePagesWithOptimalBreaking(
                systems, headerHeight, perSystemExtents.ToImmutableArray());
            return (pages, pages.SelectMany(p => p.Systems).ToImmutableArray());
        }

        // Recalculate Y positions using skyline extents to avoid overlaps
        double headerBottom = _options.MarginTop + headerHeight;
        double skylineY = LayoutUtilities.CalculateFirstSystemY(
            headerBottom, perSystemExtents[0].upExtent, _options.TopSystemPadding);
        var updatedSystems = new List<SystemLayout>();
        for (int i = 0; i < systems.Length; i++)
        {
            updatedSystems.Add(systems[i] with { Y = skylineY });
            if (i < systems.Length - 1)
            {
                double padding = _options.SystemSpacing * 0.5;
                double minDistance = systemHeight + Math.Max(_options.SystemSpacing,
                    perSystemExtents[i].downExtent + perSystemExtents[i + 1].upExtent + padding);
                skylineY += minDistance;
            }
        }
        double lastDownExtent = perSystemExtents[systems.Length - 1].downExtent;
        double totalHeight = skylineY + systemHeight + lastDownExtent + _options.MarginBottom;
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
        ImmutableArray<ArpeggioItem> arpeggios, ImmutableArray<Measure> measures,
        ImmutableArray<FiguredBassItem>? figuredBasses = null,
        ImmutableArray<ChordNameItem>? chordNames = null,
        ImmutableArray<PercentRepeatItem>? percentRepeats = null,
        ImmutableArray<CrossStaffLayout>? crossStaffLayouts = null,
        ImmutableArray<TrillSpannerItem>? trillSpanners = null)
    {
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var lyricLayouts = new LyricEngraver().CalculateLayouts(lyrics, ml, _options.StaffHeight);

        // LILYPOND-REF: axis-group-interface.cc skyline_spacing
        // Outside-staff elements are placed in priority order (lower priority = closer to staff).
        // DynamicLineSpanner (250) must be calculated before TextSpanner (350)
        // so text spanners can be placed below dynamics.

        // Dynamics first (outside-staff-priority: 250)
        var dynamicLayouts = score != null ? DynamicEngraver.Calculate(score, dynamics, systems, ml) : ImmutableArray<DynamicLayout>.Empty;

        // Detect and layout hairpins from cresc/decresc marks
        var hairpinItems = HairpinEngraver.DetectHairpins(musicMarks, dynamics);
        var hairpinLayouts = HairpinEngraver.Calculate(hairpinItems, systems, ml);

        // Detect and layout text spanners from rit/accel marks (outside-staff-priority: 350)
        // Pass dynamic layouts so text spanners can stack below them
        var textSpannerItems = TextSpannerEngraver.DetectTextSpanners(musicMarks);
        var textSpannerLayouts = TextSpannerEngraver.Calculate(textSpannerItems, systems, ml, dynamicLayouts);

        // Detect and layout ottava brackets from ottava/loco marks
        var ottavaItems = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);
        var ottavaLayouts = OttavaBracketEngraver.Calculate(ottavaItems, systems, ml);

        // Layout arpeggio markings
        var arpeggioLayouts = ArpeggioEngraver.Calculate(arpeggios, systems, ml, _options.StaffHeight);

        // Detect and layout pedal brackets from sustain/sostenuto/una corda marks
        var pedalBracketItems = PedalEngraver.DetectPedalBrackets(musicMarks);
        var pedalBracketLayouts = PedalEngraver.Calculate(pedalBracketItems, systems, ml);

        // Layout figured bass
        var figuredBassLayouts = FiguredBassEngraver.Calculate(
            figuredBasses ?? ImmutableArray<FiguredBassItem>.Empty, systems, ml);

        // Layout chord names
        var chordNameLayouts = ChordNameEngraver.Calculate(
            chordNames ?? ImmutableArray<ChordNameItem>.Empty, systems, ml);

        // Layout percent repeats
        var percentRepeatLayouts = PercentRepeatEngraver.Calculate(
            percentRepeats ?? ImmutableArray<PercentRepeatItem>.Empty, systems, ml);

        // Layout trill spanners (tr + wavy line)
        // LILYPOND-REF: lily/trill-spanner-engraver.cc — trill spanner positioning
        var trillSpannerLayouts = TrillSpannerEngraver.Calculate(
            trillSpanners ?? ImmutableArray<TrillSpannerItem>.Empty, systems, ml);

        return new AnnotationLayouts(
            Dynamics: dynamicLayouts,
            Articulations: score != null ? ArticulationEngraver.Calculate(score, articulations, systems, ml) : ImmutableArray<ArticulationLayout>.Empty,
            GraceNotes: score != null ? GraceNoteEngraver.Calculate(score, graceNotes, systems, ml) : ImmutableArray<GraceNoteLayout>.Empty,
            Lyrics: lyricLayouts,
            LyricHyphens: new LyricHyphenEngraver().CalculateLayouts(lyricLayouts, systems),
            MusicMarks: MusicMarkEngraver.Calculate(score, musicMarks, systems, ml, measures),
            CustomTexts: CustomTextEngraver.Calculate(score, customTexts, systems, ml),
            VoltaBrackets: VoltaBracketEngraver.Calculate(voltaBrackets, systems, ml),
            TupletBrackets: TupletBracketEngraver.Calculate(tupletBrackets, systems, ml, measures),
            Hairpins: hairpinLayouts,
            TextSpanners: textSpannerLayouts,
            OttavaBrackets: ottavaLayouts,
            Arpeggios: arpeggioLayouts,
            PedalBrackets: pedalBracketLayouts,
            FiguredBasses: figuredBassLayouts,
            ChordNames: chordNameLayouts,
            PercentRepeats: percentRepeatLayouts,
            CrossStaffs: crossStaffLayouts ?? ImmutableArray<CrossStaffLayout>.Empty,
            TrillSpanners: trillSpannerLayouts);
    }

    private static ScoreLayout BuildScoreLayout(
        ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems,
        ImmutableArray<BeamLayout> beams, ImmutableArray<TieLayout> ties,
        ImmutableArray<SlurLayout> slurs, ImmutableArray<GlissandoLayout> glissandos,
        AnnotationLayouts a,
        ImmutableDictionary<VoiceItemKey, double> voiceOffsets,
        ImmutableDictionary<RestShiftKey, double> restShifts,
        ImmutableArray<PartCombineLayout> partCombineLayouts = default)
    {
        return new ScoreLayout(pages, systems, beams, ties, slurs,
            a.Dynamics, a.Articulations, a.GraceNotes,
            a.Lyrics, a.LyricHyphens, a.MusicMarks,
            a.CustomTexts, a.VoltaBrackets, a.TupletBrackets,
            a.Hairpins, a.TextSpanners, a.OttavaBrackets,
            glissandos, a.Arpeggios, a.PedalBrackets,
            a.FiguredBasses, a.ChordNames, a.PercentRepeats,
            a.CrossStaffs,
            partCombineLayouts.IsDefault ? ImmutableArray<PartCombineLayout>.Empty : partCombineLayouts,
            a.TrillSpanners,
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
        ImmutableArray<TupletBracketLayout> TupletBrackets,
        ImmutableArray<HairpinLayout> Hairpins,
        ImmutableArray<TextSpannerLayout> TextSpanners,
        ImmutableArray<OttavaBracketLayout> OttavaBrackets,
        ImmutableArray<ArpeggioLayout> Arpeggios,
        ImmutableArray<PedalBracketLayout> PedalBrackets,
        ImmutableArray<FiguredBassLayout> FiguredBasses,
        ImmutableArray<ChordNameLayout> ChordNames,
        ImmutableArray<PercentRepeatLayout> PercentRepeats,
        ImmutableArray<CrossStaffLayout> CrossStaffs,
        ImmutableArray<TrillSpannerLayout> TrillSpanners);
}
