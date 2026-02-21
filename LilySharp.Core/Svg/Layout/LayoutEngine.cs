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
/// <remarks>
/// LILYPOND-REF: lily/spacing-spanner.cc:1-565 Spacing_spanner class
/// LILYPOND-REF: lily/paper-column.cc:1-487 Paper_column class
/// </remarks>
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

    /// <summary>
    /// Calculates the complete layout for a score.
    /// </summary>
    public ScoreLayout Layout(Score score)
    {
        var systems = new List<SystemLayout>();

        // LILYPOND-REF: lily/page-layout-problem.cc:434
        // Calculate actual header height based on title/composer presence
        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);
        double headerBottom = _options.MarginTop + headerHeight;

        // Break measures into systems (using first voice as representative)
        var systemMeasures = _systemBreaker.BreakIntoSystems(score);

        // Pre-calculate measure layouts for first system to build skylines
        var firstSystemMeasures = systemMeasures.Count > 0 ? systemMeasures[0] : new List<Measure>();
        var firstSystemMeasureLayouts = _systemLayouter.LayoutMeasuresForSystem(firstSystemMeasures, score.KeySignature.Sharps, true, 0);

        // Build skylines for the first system
        var (systemUpSkyline, systemDownSkyline) = _skylineBuilder.BuildSystemSkylines(firstSystemMeasures, firstSystemMeasureLayouts);

        double systemUpExtent = LayoutUtilities.CalculateUpExtent(systemUpSkyline);
        double systemDownExtent = LayoutUtilities.CalculateDownExtent(systemDownSkyline, _options.StaffHeight);
        double currentY = LayoutUtilities.CalculateFirstSystemY(headerBottom, systemUpExtent, _options.TopSystemPadding);

        // Layout each system
        int firstMeasureIndex = 0;
        for (int sysIdx = 0; sysIdx < systemMeasures.Count; sysIdx++)
        {
            bool isFirstSystem = sysIdx == 0;
            var system = _systemLayouter.LayoutSystem(
                sysIdx,
                systemMeasures[sysIdx],
                currentY,
                score.KeySignature.Sharps,
                isFirstSystem,
                firstMeasureIndex,
                score.Lyrics,
                isLastSystem: sysIdx == systemMeasures.Count - 1);

            systems.Add(system);
            currentY += _options.StaffHeight + _options.SystemSpacing;
            firstMeasureIndex += systemMeasures[sysIdx].Count;
        }

        // LILYPOND-REF: lily/page-layout-problem.cc:596-644
        // Build skylines for each system and track the maximum extent
        // Each system's down skyline gives us the bottommost point of musical elements
        double maxSystemBottomY = 0;
        var perSystemExtents = new List<(double upExtent, double downExtent)>();
        for (int sysIdx = 0; sysIdx < systems.Count; sysIdx++)
        {
            var system = systems[sysIdx];
            var measureList = systemMeasures[sysIdx];

            // Build skylines for this system (relative to staff top = 0)
            var (upSkyline, downSkyline) = _skylineBuilder.BuildSystemSkylines(measureList, system.Measures);

            double upExt = LayoutUtilities.CalculateUpExtent(upSkyline);
            double downExt = LayoutUtilities.CalculateDownExtent(downSkyline, _options.StaffHeight);
            perSystemExtents.Add((upExt, downExt));

            // LILYPOND-REF: lily/skyline.cc:667-680 Skyline::max_height()
            // DOWN skyline's MaxHeight() returns the bottommost Y in real coordinates
            double systemBottomExtent = downSkyline.IsEmpty
                ? _options.StaffHeight
                : Math.Max(_options.StaffHeight, downSkyline.MaxHeight());

            // Convert relative extent to absolute Y position
            double absoluteBottomY = system.Y + systemBottomExtent;
            maxSystemBottomY = Math.Max(maxSystemBottomY, absoluteBottomY);
        }

        // LILYPOND-REF: lily/page-layout-problem.cc:542
        // Total height includes the bottommost element plus bottom margin
        double totalHeight = maxSystemBottomY + _options.MarginBottom;

        // LILYPOND-REF: lily/page-spacing.cc
        // Create pages using optimal page breaking if enabled
        var systemsArray = systems.ToImmutableArray();
        ImmutableArray<PageLayout> pages;
        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
        {
            pages = _pageLayouter.CreatePagesWithOptimalBreaking(
                systemsArray, headerHeight, perSystemExtents.ToImmutableArray());

            // Rebuild systemsArray from pages to use final Y positions
            systemsArray = pages.SelectMany(p => p.Systems).ToImmutableArray();
        }
        else
        {
            // Single page with content-driven height
            var page = new PageLayout(
                PageIndex: 0,
                Width: _options.PageWidth,
                Height: totalHeight,
                HeaderHeight: headerHeight,
                Systems: systemsArray);
            pages = ImmutableArray.Create(page);
        }

        // Detect and layout beams
        var beamLayouts = _elementCoordinator.LayoutBeams(score, systemsArray);

        // Detect and layout ties
        var tieLayouts = _elementCoordinator.LayoutTies(score, systemsArray);

        // Detect and layout slurs
        var slurLayouts = _elementCoordinator.LayoutSlurs(score, systemsArray);

        // Calculate voice collision offsets for multi-voice scores
        var voiceOffsets = _elementCoordinator.CalculateVoiceOffsets(score);

        // Calculate rest shifts to avoid beam collisions
        var restShifts = _elementCoordinator.CalculateRestShifts(score, systemsArray, beamLayouts);

        // Calculate dynamic layouts
        // LILYPOND-REF: dynamic-engraver.cc - dynamic positioning
        var measureLayouts = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();
        var dynamicLayouts = DynamicEngraver.Calculate(score, score.Dynamics, systemsArray, measureLayouts);

        // Calculate articulation layouts
        // LILYPOND-REF: script-engraver.cc - articulation positioning
        var articulationLayouts = ArticulationEngraver.Calculate(score, score.Articulations, systemsArray, measureLayouts);

        // Calculate grace note layouts
        // LILYPOND-REF: grace-engraver.cc - grace note positioning
        var graceNoteLayouts = GraceNoteEngraver.Calculate(score, score.GraceNotes, systemsArray, measureLayouts);

        // Calculate lyric layouts
        // LILYPOND-REF: lily/lyric-engraver.cc:60-150 process_music
        var lyricEngraver = new LyricEngraver();
        double staffBottom = _options.StaffHeight;  // Bottom of staff in staff spaces (typically 4)
        var lyricLayouts = lyricEngraver.CalculateLayouts(score.Lyrics, measureLayouts, staffBottom);

        // Calculate lyric hyphen/extender layouts
        // LILYPOND-REF: lily/lyric-hyphen.cc:1-150
        var lyricHyphenEngraver = new LyricHyphenEngraver();
        var lyricHyphenLayouts = lyricHyphenEngraver.CalculateLayouts(lyricLayouts, systemsArray);

        // Calculate music mark layouts
        // LILYPOND-REF: mark-engraver.cc - segno, coda, fine, D.S., D.C. positioning
        var musicMarkLayouts = MusicMarkEngraver.Calculate(score, score.MusicMarks, systemsArray, measureLayouts);

        // Calculate custom text layouts
        // LILYPOND-REF: lily/text-interface.cc - text rendering
        var customTextLayouts = CustomTextEngraver.Calculate(score, score.CustomTexts, systemsArray, measureLayouts);

        // Calculate volta bracket layouts
        // LILYPOND-REF: lily/volta-bracket.cc - volta bracket rendering
        var voltaBracketLayouts = VoltaBracketEngraver.Calculate(score.VoltaBrackets, systemsArray, measureLayouts);

        // Calculate tuplet bracket layouts
        // LILYPOND-REF: lily/tuplet-bracket.cc - tuplet bracket rendering
        var tupletBracketLayouts = TupletBracketEngraver.Calculate(score.TupletBrackets, systemsArray, measureLayouts, score.Voice.Measures);

        return new ScoreLayout(
            pages,
            systemsArray,
            beamLayouts,
            tieLayouts,
            slurLayouts,
            dynamicLayouts,
            articulationLayouts,
            graceNoteLayouts,
            lyricLayouts,
            lyricHyphenLayouts,
            musicMarkLayouts,
            customTextLayouts,
            voltaBracketLayouts,
            tupletBracketLayouts,
            voiceOffsets,
            restShifts);
    }


    /// <summary>
    /// Calculates the complete layout for a multi-staff score.
    /// </summary>
    public ScoreLayout Layout(MultiStaffScore score)
    {
        var systems = new List<SystemLayout>();

        // LILYPOND-REF: lily/page-layout-problem.cc:434
        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);
        double headerBottom = _options.MarginTop + headerHeight;

        // Break measures into systems using the primary voice
        var systemMeasures = _systemBreaker.BreakIntoSystems(score);

        // Calculate system height (all staff groups)
        double systemHeight = _multiStaffLayouter.CalculateSystemHeight(score);

        // Staff group layouts are the same for every system (relative positions)
        var staffGroupLayouts = _multiStaffLayouter.LayoutStaffGroups(score, 0);

        // Pre-calculate first system's measure layouts for skyline building
        var firstSystemMeasureLayouts = systemMeasures.Count > 0
            ? _multiStaffLayouter.LayoutMeasures(score, 0, 0, systemMeasures[0].Count)
            : ImmutableArray<MeasureLayout>.Empty;

        var (systemUpSkyline, systemDownSkyline) = _skylineBuilder.BuildSystemSkylines(score, firstSystemMeasureLayouts);
        double systemUpExtent = LayoutUtilities.CalculateUpExtent(systemUpSkyline);
        double currentY = LayoutUtilities.CalculateFirstSystemY(headerBottom, systemUpExtent, _options.TopSystemPadding);

        // Layout each system (draft Y positions for measure layout)
        int firstMeasureIndex = 0;
        var perSystemExtents = new List<(double upExtent, double downExtent)>();
        for (int sysIdx = 0; sysIdx < systemMeasures.Count; sysIdx++)
        {
            bool isFirstSystem = sysIdx == 0;
            bool isLastSystem = sysIdx == systemMeasures.Count - 1;
            int measureCount = systemMeasures[sysIdx].Count;

            var measureLayouts = isFirstSystem
                ? firstSystemMeasureLayouts
                : _multiStaffLayouter.LayoutMeasures(score, sysIdx, firstMeasureIndex, measureCount, isLastSystem);

            // Build per-system skyline extents
            var (upSky, downSky) = _skylineBuilder.BuildSystemSkylines(score, measureLayouts);
            double upExt = LayoutUtilities.CalculateUpExtent(upSky);
            double downExt = LayoutUtilities.CalculateDownExtent(downSky, systemHeight);
            perSystemExtents.Add((upExt, downExt));

            var system = new SystemLayout(
                SystemIndex: sysIdx,
                Y: currentY,
                Width: _options.ContentWidth,
                PrefixWidth: SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, isFirstSystem),
                Measures: measureLayouts,
                StaffGroups: staffGroupLayouts);

            systems.Add(system);
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
            // Single page with content-driven height using skyline-based spacing
            double skylineY = LayoutUtilities.CalculateFirstSystemY(headerBottom, perSystemExtents[0].upExtent, _options.TopSystemPadding);
            var updatedSystems = new List<SystemLayout>();
            for (int sysIdx = 0; sysIdx < systems.Count; sysIdx++)
            {
                updatedSystems.Add(systems[sysIdx] with { Y = skylineY });
                if (sysIdx < systems.Count - 1)
                {
                    double downExtent = perSystemExtents[sysIdx].downExtent;
                    double nextUpExtent = perSystemExtents[sysIdx + 1].upExtent;
                    double padding = _options.SystemSpacing * 0.5;
                    double minDistance = systemHeight + Math.Max(_options.SystemSpacing, downExtent + nextUpExtent + padding);
                    skylineY += minDistance;
                }
            }
            double totalHeight = skylineY + systemHeight + _options.MarginBottom;
            systemsArray = updatedSystems.ToImmutableArray();
            var page = new PageLayout(
                PageIndex: 0,
                Width: _options.PageWidth,
                Height: totalHeight,
                HeaderHeight: headerHeight,
                Systems: systemsArray);
            pages = ImmutableArray.Create(page);
        }

        // For multi-staff scores, calculate beams/ties/slurs for each staff
        var allBeamLayouts = new List<BeamLayout>();
        var allTieLayouts = new List<TieLayout>();
        var allSlurLayouts = new List<SlurLayout>();

        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            var clefString = staff.Clef switch
            {
                ClefType.Treble => "treble",
                ClefType.Bass => "bass",
                ClefType.Alto => "alto",
                ClefType.Tenor => "tenor",
                ClefType.Treble8Below => "treble_8",
                _ => "treble"
            };

            var staffScore = new Score(
                staff.PrimaryVoice,
                score.TimeSignature,
                score.KeySignature,
                clefString,
                score.Tempo,
                score.Title,
                score.Composer);

            var staffBeams = _elementCoordinator.LayoutBeams(staffScore, systemsArray, staffIndex);
            var staffTies = _elementCoordinator.LayoutTies(staffScore, systemsArray, staffIndex);
            var staffSlurs = _elementCoordinator.LayoutSlurs(staffScore, systemsArray, staffIndex);

            allBeamLayouts.AddRange(staffBeams);
            allTieLayouts.AddRange(staffTies);
            allSlurLayouts.AddRange(staffSlurs);
        }

        var beamLayouts = allBeamLayouts.ToImmutableArray();
        var tieLayouts = allTieLayouts.ToImmutableArray();
        var slurLayouts = allSlurLayouts.ToImmutableArray();
        var voiceOffsets = ImmutableDictionary<VoiceItemKey, double>.Empty;
        var restShifts = ImmutableDictionary<RestShiftKey, double>.Empty;

        // Collect all measure layouts across systems for engravers
        var allMeasureLayouts = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();

        // Calculate lyric layouts
        var lyricEngraver = new LyricEngraver();
        double staffBottom = _options.StaffHeight;
        var lyricLayouts = lyricEngraver.CalculateLayouts(score.Lyrics, allMeasureLayouts, staffBottom);

        // Calculate lyric hyphen/extender layouts
        var lyricHyphenEngraver = new LyricHyphenEngraver();
        var lyricHyphenLayouts = lyricHyphenEngraver.CalculateLayouts(lyricLayouts, systemsArray);

        // Calculate music mark layouts
        var musicMarkLayouts = MusicMarkEngraver.Calculate(null, score.MusicMarks, systemsArray, allMeasureLayouts);

        // Calculate custom text layouts
        var customTextLayouts = CustomTextEngraver.Calculate(null, score.CustomTexts, systemsArray, allMeasureLayouts);

        // Calculate volta bracket layouts
        var voltaBracketLayouts = VoltaBracketEngraver.Calculate(score.VoltaBrackets, systemsArray, allMeasureLayouts);

        // Calculate tuplet bracket layouts
        var tupletBracketLayouts = TupletBracketEngraver.Calculate(score.TupletBrackets, systemsArray, allMeasureLayouts, score.StaffGroups[0].PrimaryStaff.PrimaryVoice.Measures);

        return new ScoreLayout(
            pages,
            systemsArray,
            beamLayouts,
            tieLayouts,
            slurLayouts,
            ImmutableArray<DynamicLayout>.Empty,
            ImmutableArray<ArticulationLayout>.Empty,
            ImmutableArray<GraceNoteLayout>.Empty,
            lyricLayouts,
            lyricHyphenLayouts,
            musicMarkLayouts,
            customTextLayouts,
            voltaBracketLayouts,
            tupletBracketLayouts,
            voiceOffsets,
            restShifts);
    }
}