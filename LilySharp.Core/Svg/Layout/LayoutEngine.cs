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
        for (int sysIdx = 0; sysIdx < systems.Count; sysIdx++)
        {
            var system = systems[sysIdx];
            var measureList = systemMeasures[sysIdx];

            // Build skylines for this system (relative to staff top = 0)
            var (_, downSkyline) = _skylineBuilder.BuildSystemSkylines(measureList, system.Measures);

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
                systemsArray, headerHeight, systemUpExtent, systemDownExtent);
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
        // header_height_ = head ? head->extent(Y_AXIS).length() : 0;
        // Calculate actual header height based on title/composer presence
        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);

        // Layout measures with timing-based columns for multi-staff alignment
        var measureLayouts = _multiStaffLayouter.LayoutMeasures(score, 0);

        // Calculate actual page width based on content
        double actualPageWidth = _options.PageWidth;
        if (measureLayouts.Length > 0)
        {
            var lastMeasure = measureLayouts[measureLayouts.Length - 1];
            double contentRight = lastMeasure.X + lastMeasure.Width + _options.MarginRight;
            actualPageWidth = Math.Max(_options.PageWidth, contentRight);
        }

        // Calculate total system height (all staff groups)
        double systemHeight = _multiStaffLayouter.CalculateSystemHeight(score);

        // LILYPOND-REF: lily/page-layout-problem.cc:440-443
        // Initialize bottom_skyline to represent the top of the printable area
        // (below the header). This forces the first system to start below the header.
        double headerBottom = _options.MarginTop + headerHeight;
        var bottomSkyline = new VerticalSkyline(VerticalDirection.Down);
        bottomSkyline.SetMinimumHeight(headerBottom);

        // Build system skylines using relative coordinates (staff top = 0)
        var (systemUpSkyline, systemDownSkyline) = _skylineBuilder.BuildSystemSkylines(score, measureLayouts);

        double systemUpExtent = LayoutUtilities.CalculateUpExtent(systemUpSkyline);
        double currentY = LayoutUtilities.CalculateFirstSystemY(headerBottom, systemUpExtent, _options.TopSystemPadding);

        // Layout all staff groups with the calculated Y position
        var staffGroupLayouts = _multiStaffLayouter.LayoutStaffGroups(score, currentY);

        var system = new SystemLayout(
            SystemIndex: 0,
            Y: currentY,
            Width: _options.PageWidth - _options.MarginLeft - _options.MarginRight,
            PrefixWidth: SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, true),
            Measures: measureLayouts,
            StaffGroups: staffGroupLayouts);

        systems.Add(system);
        currentY += systemHeight + _options.SystemSpacing;

        double totalHeight = currentY - _options.SystemSpacing + _options.MarginTop;

        var systemsArray = systems.ToImmutableArray();
        var page = new PageLayout(
            PageIndex: 0,
            Width: actualPageWidth,
            Height: totalHeight,
            HeaderHeight: headerHeight,
            Systems: systemsArray);

        // For multi-staff scores, calculate beams/ties/slurs for each staff
        var allBeamLayouts = new List<BeamLayout>();
        var allTieLayouts = new List<TieLayout>();
        var allSlurLayouts = new List<SlurLayout>();

        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            // Create a temporary Score for each staff to reuse existing logic
            var clefString = staff.Clef switch
            {
                ClefType.Treble => "treble",
                ClefType.Bass => "bass",
                ClefType.Alto => "alto",
                ClefType.Tenor => "tenor",
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
            var staffTies = _elementCoordinator.LayoutTies(staffScore, systemsArray);
            var staffSlurs = _elementCoordinator.LayoutSlurs(staffScore, systemsArray);

            allBeamLayouts.AddRange(staffBeams);
            allTieLayouts.AddRange(staffTies);
            allSlurLayouts.AddRange(staffSlurs);
        }

        var beamLayouts = allBeamLayouts.ToImmutableArray();
        var tieLayouts = allTieLayouts.ToImmutableArray();
        var slurLayouts = allSlurLayouts.ToImmutableArray();
        var voiceOffsets = ImmutableDictionary<VoiceItemKey, double>.Empty;
        var restShifts = ImmutableDictionary<RestShiftKey, double>.Empty;

        return new ScoreLayout(
            ImmutableArray.Create(page),
            systemsArray,
            beamLayouts,
            tieLayouts,
            slurLayouts,
            ImmutableArray<DynamicLayout>.Empty,
            ImmutableArray<ArticulationLayout>.Empty,
            ImmutableArray<GraceNoteLayout>.Empty,
            ImmutableArray<LyricLayout>.Empty,  // TODO: Implement lyrics for multi-staff
            ImmutableArray<LyricHyphenLayout>.Empty,
            voiceOffsets,
            restShifts);
    }
}
