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
    private readonly BeamDetector _beamDetector = new();
    private readonly BeamEngraver _beamEngraver = new();
    private readonly TieDetector _tieDetector = new();
    private readonly TieEngraver _tieEngraver = new();
    private readonly SlurDetector _slurDetector = new();
    private readonly SlurEngraver _slurEngraver = new();
    private readonly VoiceCollector _voiceCollector = new();
    private readonly NoteCollision _noteCollision = new();

    public LayoutEngine(LayoutOptions? options = null)
    {
        _options = options ?? LayoutOptions.Default;
    }

    /// <summary>
    /// Calculates the complete layout for a score.
    /// </summary>
    public ScoreLayout Layout(Score score)
    {
        var systems = new List<SystemLayout>();

        // LILYPOND-REF: lily/page-layout-problem.cc:434
        // Calculate actual header height based on title/composer presence
        double headerHeight = CalculateHeaderHeight(score);
        double headerBottom = _options.MarginTop + headerHeight;

        // Break measures into systems (using first voice as representative)
        var systemMeasures = BreakIntoSystems(score);

        // Pre-calculate measure layouts for first system to build skylines
        var firstSystemMeasures = systemMeasures.Count > 0 ? systemMeasures[0] : new List<Measure>();
        var firstSystemMeasureLayouts = LayoutMeasuresForSystem(firstSystemMeasures, score.KeySignature.Sharps, true, 0);

        // Build skylines for the first system
        var (systemUpSkyline, systemDownSkyline) = BuildSystemSkylines(firstSystemMeasures, firstSystemMeasureLayouts);

        // LILYPOND-REF: lily/page-layout-problem.cc:622-626
        // Calculate extent above staff top
        double systemUpExtent = systemUpSkyline.IsEmpty ? 0 : Math.Max(0, -systemUpSkyline.MaxHeight());
        
        // Calculate extent below staff bottom (staff bottom is at StaffHeight)
        // LILYPOND-REF: lily/skyline.cc:667-680 - MaxHeight for DOWN skyline returns largest Y
        double systemDownExtent = systemDownSkyline.IsEmpty ? 0 : Math.Max(0, systemDownSkyline.MaxHeight() - _options.StaffHeight);

        // LILYPOND-REF: lily/page-layout-problem.cc:477-478, 984-985
        // The staff Y is positioned to leave room for: header + system extent + padding
        double currentY = headerBottom + systemUpExtent + _options.TopSystemPadding;

        // Layout each system
        int firstMeasureIndex = 0;
        for (int sysIdx = 0; sysIdx < systemMeasures.Count; sysIdx++)
        {
            bool isFirstSystem = sysIdx == 0;
            var system = LayoutSystem(
                sysIdx,
                systemMeasures[sysIdx],
                currentY,
                score.KeySignature.Sharps,
                isFirstSystem,
                firstMeasureIndex);

            systems.Add(system);
            currentY += _options.StaffHeight + _options.SystemSpacing;
            firstMeasureIndex += systemMeasures[sysIdx].Count;
        }

        // Total height includes: systems + bottom extent for notes below staff + margin
        double totalHeight = currentY - _options.SystemSpacing + _options.MarginTop + systemDownExtent;

        // LILYPOND-REF: lily/page-spacing.cc
        // Create pages using optimal page breaking if enabled
        var systemsArray = systems.ToImmutableArray();
        ImmutableArray<PageLayout> pages;
        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
        {
            pages = CreatePagesWithOptimalBreaking(
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
        var beamLayouts = LayoutBeams(score, systemsArray);

        // Detect and layout ties
        var tieLayouts = LayoutTies(score, systemsArray);

        // Detect and layout slurs
        var slurLayouts = LayoutSlurs(score, systemsArray);

        // Calculate voice collision offsets for multi-voice scores
        var voiceOffsets = CalculateVoiceOffsets(score);

        // Calculate rest shifts to avoid beam collisions
        var restShifts = CalculateRestShifts(score, systemsArray, beamLayouts);

        // Calculate dynamic layouts
        // LILYPOND-REF: dynamic-engraver.cc - dynamic positioning
        var measureLayouts = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();
        var dynamicLayouts = DynamicEngraver.Calculate(score, score.Dynamics, systemsArray, measureLayouts);

        // Calculate articulation layouts
        // LILYPOND-REF: script-engraver.cc - articulation positioning
        var articulationLayouts = ArticulationEngraver.Calculate(score, score.Articulations, systemsArray, measureLayouts);

        return new ScoreLayout(
            pages,
            systemsArray,
            beamLayouts,
            tieLayouts,
            slurLayouts,
            dynamicLayouts,
            articulationLayouts,
            voiceOffsets,
            restShifts);
    }

    /// <summary>
    /// Creates pages using optimal page breaking algorithm.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc Page_spacer class
    /// Uses dynamic programming to find optimal page breaks.
    /// </remarks>
    private ImmutableArray<PageLayout> CreatePagesWithOptimalBreaking(
        ImmutableArray<SystemLayout> systems,
        double headerHeight,
        double systemUpExtent,
        double systemDownExtent)
    {
        if (systems.Length == 0)
        {
            return ImmutableArray<PageLayout>.Empty;
        }

        // Create SystemDetails for each system
        var systemDetails = new List<SystemDetails>();
        foreach (var system in systems)
        {
            // Calculate system height (staff + extents)
            double staffHeight = _options.StaffHeight;

            // For now, use simple estimates for extents
            // TODO: Calculate actual skyline extents per system
            double topExtent = systemUpExtent;
            double bottomExtent = systemDownExtent;

            systemDetails.Add(PageBreaker.CreateFromLayout(
                staffHeight: staffHeight,
                topExtent: topExtent,
                bottomExtent: bottomExtent,
                padding: _options.SystemSpacing * 0.5,
                springLength: _options.SystemSpacing * 0.5));
        }

        // Run page breaker
        var breaker = new PageBreaker(
            pageHeight: _options.PageHeight,
            topMargin: _options.MarginTop,
            bottomMargin: _options.MarginBottom,
            headerHeight: headerHeight);

        var breakPoints = breaker.BreakIntoPages(systemDetails);

        // Create pages from break points
        var pages = new List<PageLayout>();
        int systemStart = 0;

        for (int pageIdx = 0; pageIdx < breakPoints.Count; pageIdx++)
        {
            int systemEnd = breakPoints[pageIdx];
            bool isFirstPage = pageIdx == 0;

            // Collect systems for this page
            var pageSystems = new List<SystemLayout>();
            double currentY = _options.MarginTop + (isFirstPage ? headerHeight + systemUpExtent + _options.TopSystemPadding : _options.TopSystemPadding);

            for (int sysIdx = systemStart; sysIdx < systemEnd; sysIdx++)
            {
                // Create new SystemLayout with updated Y position
                var original = systems[sysIdx];
                var updated = original with { Y = currentY };
                pageSystems.Add(updated);
                currentY += _options.StaffHeight + _options.SystemSpacing;
            }

            pages.Add(new PageLayout(
                PageIndex: pageIdx,
                Width: _options.PageWidth,
                Height: _options.PageHeight,
                HeaderHeight: isFirstPage ? headerHeight : 0,
                Systems: pageSystems.ToImmutableArray()));

            systemStart = systemEnd;
        }

        return pages.ToImmutableArray();
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
        double headerHeight = CalculateHeaderHeight(score);

        // Layout measures with timing-based columns for multi-staff alignment
        var measureLayouts = LayoutMeasuresForMultiStaff(score, 0);

        // Calculate actual page width based on content
        double actualPageWidth = _options.PageWidth;
        if (measureLayouts.Length > 0)
        {
            var lastMeasure = measureLayouts[measureLayouts.Length - 1];
            double contentRight = lastMeasure.X + lastMeasure.Width + _options.MarginRight;
            actualPageWidth = Math.Max(_options.PageWidth, contentRight);
        }

        // Calculate total system height (all staff groups)
        double systemHeight = CalculateMultiStaffSystemHeight(score);

        // LILYPOND-REF: lily/page-layout-problem.cc:440-443
        // Initialize bottom_skyline to represent the top of the printable area
        // (below the header). This forces the first system to start below the header.
        double headerBottom = _options.MarginTop + headerHeight;
        var bottomSkyline = new VerticalSkyline(VerticalDirection.Down);
        bottomSkyline.SetMinimumHeight(headerBottom);

        // Build system skylines using relative coordinates (staff top = 0)
        var (systemUpSkyline, systemDownSkyline) = BuildSystemSkylines(score, measureLayouts);

        // LILYPOND-REF: lily/page-layout-problem.cc:622-626
        // MaxHeight() returns topmost Y in relative coords (negative for notes above staff)
        // Convert to positive extent above staff top
        double systemUpExtent = systemUpSkyline.IsEmpty ? 0 : Math.Max(0, -systemUpSkyline.MaxHeight());

        // LILYPOND-REF: lily/page-layout-problem.cc:477-478, 984-985
        // read_spacing_spec(top_system_spacing, &header_padding_, ly_symbol2scm("padding"));
        // min_dist = header_padding_ + header_height_ + staff->extent(staff, Y_AXIS)[UP];
        // The staff Y is positioned to leave room for: header + system extent + padding
        double currentY = headerBottom + systemUpExtent + _options.TopSystemPadding;

        // Layout all staff groups with the calculated Y position
        var staffGroupLayouts = LayoutStaffGroups(score, currentY);

        var system = new SystemLayout(
            SystemIndex: 0,
            Y: currentY,
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

        // For multi-staff scores, beams/ties/slurs are per-voice
        // TODO: implement proper beam/tie/slur detection for multi-staff
        var beamLayouts = ImmutableArray<BeamLayout>.Empty;
        var tieLayouts = ImmutableArray<TieLayout>.Empty;
        var slurLayouts = ImmutableArray<SlurLayout>.Empty;
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
            voiceOffsets,
            restShifts);
    }

    /// <summary>
    /// Calculates the total height of a multi-staff system.
    /// </summary>
    private double CalculateMultiStaffSystemHeight(MultiStaffScore score)
    {
        double height = 0;
        double staffHeight = _options.StaffHeight;
        double grandStaffSpacing = _options.GrandStaffSpacing; // Space between grand staff staves
        double staffGroupSpacing = _options.StaffGroupSpacing; // Space between different staff groups

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];

            if (group.IsGrandStaff)
            {
                // Grand staff: two staves with brace
                height += staffHeight * 2 + grandStaffSpacing;
            }
            else
            {
                height += staffHeight * group.StaffCount;
                if (group.StaffCount > 1)
                    height += grandStaffSpacing * (group.StaffCount - 1);
            }

            // Add spacing between staff groups (not after the last one)
            if (i < score.StaffGroups.Length - 1)
                height += staffGroupSpacing;
        }

        return height;
    }

    /// <summary>
    /// Layouts all staff groups within a system.
    /// </summary>
    private ImmutableArray<StaffGroupLayout> LayoutStaffGroups(MultiStaffScore score, double systemY)
    {
        var builder = ImmutableArray.CreateBuilder<StaffGroupLayout>();
        double currentY = 0;
        double staffHeight = _options.StaffHeight;
        double grandStaffSpacing = _options.GrandStaffSpacing;
        double staffGroupSpacing = _options.StaffGroupSpacing;
        int globalStaffIndex = 0;

        foreach (var group in score.StaffGroups)
        {
            if (group.IsGrandStaff)
            {
                var layout = LayoutGrandStaffGroup(group, currentY, staffHeight, grandStaffSpacing, globalStaffIndex);
                builder.Add(layout);
                currentY += layout.Height + staffGroupSpacing;
                globalStaffIndex += group.StaffCount;
            }
            else
            {
                var layout = LayoutSingleStaffGroup(group, currentY, staffHeight, grandStaffSpacing, globalStaffIndex);
                builder.Add(layout);
                currentY += layout.Height + staffGroupSpacing;
                globalStaffIndex += group.StaffCount;
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Layouts a grand staff group (piano/organ style with brace).
    /// </summary>
    private StaffGroupLayout LayoutGrandStaffGroup(
        StaffGroup group, double y, double staffHeight, double staffSpacing, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: staffHeight));

            if (i < group.Staves.Length - 1)
                currentY += staffHeight + staffSpacing;
        }

        double totalHeight = currentY + staffHeight - y;
        double braceX = _options.MarginLeft - 2;  // 2 staff spaces left of margin

        var grandStaffLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: braceX,
            BraceTop: y,
            BraceBottom: y + totalHeight);

        return StaffGroupLayout.CreateGrandStaff(
            staffLayouts.ToImmutable(),
            y,
            totalHeight,
            grandStaffLayout);
    }

    /// <summary>
    /// Layouts a single staff or bracket group.
    /// </summary>
    private StaffGroupLayout LayoutSingleStaffGroup(
        StaffGroup group, double y, double staffHeight, double staffSpacing, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: staffHeight));

            if (i < group.Staves.Length - 1)
                currentY += staffHeight + staffSpacing;
        }

        double totalHeight = group.StaffCount == 1
            ? staffHeight
            : currentY + staffHeight - y;

        return StaffGroupLayout.CreateSingle(
            staffLayouts[0],
            y,
            totalHeight);
    }

    /// <summary>
    /// Layouts measures for a single voice.
    /// </summary>
    private ImmutableArray<MeasureLayout> LayoutMeasuresForVoice(Voice voice, int systemIndex)
    {
        var layouts = ImmutableArray.CreateBuilder<MeasureLayout>();
        double currentX = _options.MarginLeft + SpacingRules.CalculatePrefixWidth(0, systemIndex == 0);

        for (int i = 0; i < voice.Measures.Length; i++)
        {
            var measure = voice.Measures[i];
            double measureWidth = SpacingRules.CalculateMeasureIdealWidth(measure);
            var itemLayouts = LayoutMeasureItems(measure, measureWidth);

            var measureLayout = new MeasureLayout(i, currentX, measureWidth, itemLayouts);
            layouts.Add(measureLayout);
            currentX += measureLayout.Width;
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Calculates X offsets for notes that collide in multi-voice contexts.
    /// </summary>
    private ImmutableDictionary<VoiceItemKey, double> CalculateVoiceOffsets(Score score)
    {
        if (score.Voices.Length <= 1)
            return ImmutableDictionary<VoiceItemKey, double>.Empty;

        // Collect voice columns (grouped by time position)
        var voiceColumns = _voiceCollector.Collect(score);

        if (voiceColumns.Length == 0)
            return ImmutableDictionary<VoiceItemKey, double>.Empty;

        // Calculate notehead width for offset calculation
        double noteheadWidth = EngravingDefaults.NoteheadBlackWidth;  // Already in staff spaces

        var builder = ImmutableDictionary.CreateBuilder<VoiceItemKey, double>();

        foreach (var column in voiceColumns)
        {
            // Skip single-voice columns (no collision possible)
            if (column.Entries.Length <= 1)
                continue;

            // Calculate collision offsets
            var offsets = _noteCollision.CalculateVoiceOffsets(column, noteheadWidth);

            foreach (var (voiceId, itemIndex, xOffset) in offsets)
            {
                // Only store non-zero offsets
                if (Math.Abs(xOffset) > 0.001)
                {
                    var key = new VoiceItemKey(column.MeasureIndex, voiceId, itemIndex);
                    builder[key] = xOffset;
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Detects beam groups and calculates their layouts.
    /// </summary>
    private ImmutableArray<BeamLayout> LayoutBeams(Score score, ImmutableArray<SystemLayout> systems)
    {
        // Detect beam groups
        var beamGroups = _beamDetector.DetectBeamGroups(score);

        if (beamGroups.Length == 0)
            return ImmutableArray<BeamLayout>.Empty;

        // Build a map from measure index to (system, measureLayout)
        var measureMap = new Dictionary<int, (SystemLayout system, MeasureLayout measure)>();
        foreach (var system in systems)
        {
            foreach (var measureLayout in system.Measures)
            {
                measureMap[measureLayout.MeasureIndex] = (system, measureLayout);
            }
        }

        // Calculate layout for each beam group
        var beamLayouts = new List<BeamLayout>();

        foreach (var group in beamGroups)
        {
            if (!measureMap.TryGetValue(group.MeasureIndex, out var measureInfo))
                continue;

            var (system, measureLayout) = measureInfo;

            // Get X positions for all items in this measure
            var itemXPositions = new List<double>();
            foreach (var itemLayout in measureLayout.Items)
            {
                // Absolute X position = measure X + item X offset
                itemXPositions.Add(measureLayout.X + itemLayout.X);
            }

            // Collect collision objects (items in measure that are NOT part of this beam group)
            var collisions = CollectBeamCollisions(
                score.Voice.Measures[group.MeasureIndex],
                group,
                itemXPositions);

            // Calculate beam layout
            var beamLayout = _beamEngraver.CalculateBeamLayout(
                group,
                itemXPositions,
                collisions);

            beamLayouts.Add(beamLayout);
        }

        return beamLayouts.ToImmutableArray();
    }

    /// <summary>
    /// Collects collision objects for beam scoring.
    /// These are items in the measure that are not part of the beam group
    /// but could collide with the beam.
    /// </summary>
    private List<BeamCollision> CollectBeamCollisions(
        Measure measure,
        BeamGroup group,
        IReadOnlyList<double> itemXPositions)
    {
        var collisions = new List<BeamCollision>();

        // Get the set of item indices that are part of this beam group
        var beamMemberIndices = new HashSet<int>(group.Members.Select(m => m.ItemIndex));

        // Get beam X range
        double beamLeftX = itemXPositions[group.Members[0].ItemIndex];
        double beamRightX = itemXPositions[group.Members[^1].ItemIndex];

        for (int i = 0; i < measure.Items.Length; i++)
        {
            // Skip items that are part of this beam group
            if (beamMemberIndices.Contains(i))
                continue;

            var item = measure.Items[i];
            double itemX = itemXPositions[i];

            // Skip items outside beam X range (with padding for object width)
            // Objects have width, and beam extends slightly beyond stem positions
            double xPadding = _options.CollisionXPadding; // accounts for rest/note width and stem offset
            if (itemX < beamLeftX - xPadding || itemX > beamRightX + xPadding)
                continue;

            // Get staff position range for this item
            int staffPosition;
            double halfHeight;

            switch (item)
            {
                case RestItem rest:
                    // Rests are typically centered on middle line (staff position 4)
                    // and have a vertical extent of about 2 staff spaces
                    staffPosition = (int)EngravingDefaults.RestCenterPosition;
                    halfHeight = EngravingDefaults.RestExtent;
                    break;

                case NoteItem note:
                    staffPosition = note.StaffPosition;
                    halfHeight = EngravingDefaults.NoteheadHalfHeight; // Notehead is about 1 staff space tall
                    break;

                case ChordItem chord:
                    // Use the extreme notes of the chord
                    int minPos = chord.Notes.Min(n => n.StaffPosition);
                    int maxPos = chord.Notes.Max(n => n.StaffPosition);
                    staffPosition = (minPos + maxPos) / 2;
                    halfHeight = (maxPos - minPos) / 2.0 + EngravingDefaults.NoteheadHalfHeight;
                    break;

                default:
                    continue;
            }

            collisions.Add(new BeamCollision(
                X: itemX,
                MinY: staffPosition - halfHeight,
                MaxY: staffPosition + halfHeight,
                BasePenalty: 1.0));
        }

        return collisions;
    }

    /// <summary>
    /// Calculates Y shifts for rests to avoid beam collisions.
    /// Based on Lilypond's Beam::rest_collision_callback.
    /// </summary>
    private ImmutableDictionary<RestShiftKey, double> CalculateRestShifts(
        Score score,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<BeamLayout> beamLayouts)
    {
        if (beamLayouts.Length == 0)
            return ImmutableDictionary<RestShiftKey, double>.Empty;

        var shifts = new Dictionary<RestShiftKey, double>();

        // Build measure layout map
        var measureMap = new Dictionary<int, MeasureLayout>();
        foreach (var system in systems)
        {
            foreach (var measureLayout in system.Measures)
            {
                measureMap[measureLayout.MeasureIndex] = measureLayout;
            }
        }

        // Group beam layouts by measure
        var beamsByMeasure = beamLayouts
            .GroupBy(bl => bl.Group.MeasureIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Check each measure for rest-beam collisions
        foreach (var kvp in beamsByMeasure)
        {
            int measureIndex = kvp.Key;
            var measureBeams = kvp.Value;

            if (!measureMap.TryGetValue(measureIndex, out var measureLayout))
                continue;

            var measure = score.Voice.Measures[measureIndex];

            // Get X positions
            var itemXPositions = measureLayout.Items
                .Select(item => measureLayout.X + item.X)
                .ToList();

            // Check each item in measure
            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                if (measure.Items[itemIdx] is not RestItem)
                    continue;

                double restX = itemXPositions[itemIdx];

                // Check against each beam in this measure
                foreach (var beamLayout in measureBeams)
                {
                    // Rest is in the same measure as the beam - always check for collision
                    // The sloped beam may extend beyond its stem positions

                    // Use beam Y at the nearest stem position, not at rest X
                    // (Following Lilypond: rest is associated with its stem, not an arbitrary X)
                    double beamY;
                    if (restX < beamLayout.LeftX)
                        beamY = beamLayout.LeftY;  // Rest is to the left of beam
                    else if (restX > beamLayout.RightX)
                        beamY = beamLayout.RightY; // Rest is to the right of beam
                    else
                        beamY = beamLayout.GetYAtX(restX); // Rest is under beam

                    // Direction: -1 for stems up (beam above), +1 for stems down (beam below)
                    int d = beamLayout.Group.StemUp ? -1 : 1;

                    // Beam thickness and translation (in staff positions)
                    double beamThickness = EngravingDefaults.ToStaffPositions(EngravingDefaults.BeamThickness);
                    double beamTranslation = EngravingDefaults.ToStaffPositions(EngravingDefaults.BeamTranslation);
                    int beamCount = beamLayout.Group.Members.Max(m => m.BeamCount);

                    // Height of beams from center
                    double heightOfBeams = beamThickness / 2 + (beamCount - 1) * beamTranslation;

                    // Beam edge Y (the edge facing the rest)
                    double beamEdgeY = beamY + d * heightOfBeams;

                    // Rest position: centered at staff position 4 (middle line B)
                    // Rest extent: approximately 2 staff positions in each direction
                    double restCenterY = EngravingDefaults.RestCenterPosition;
                    double restExtent = EngravingDefaults.RestExtent;
                    double restEdgeY = restCenterY - d * restExtent; // Edge facing beam

                    // Minimum distance (in staff positions)
                    double minimumDistance = EngravingDefaults.RestBeamMinDistance;

                    // Calculate shift needed
                    double gap = d * (beamEdgeY - d * minimumDistance - restEdgeY);
                    double shift = d * Math.Min(gap, 0.0);

                    if (Math.Abs(shift) > EngravingDefaults.RestShiftThreshold)
                    {
                        // Quantize to half staff spaces
                        shift = Math.Ceiling(Math.Abs(shift) * 2) / 2.0 * Math.Sign(shift);

                        var key = new RestShiftKey(measureIndex, itemIdx);
                        shifts[key] = shift;
                    }
                }
            }
        }

        return shifts.ToImmutableDictionary();
    }

    /// <summary>
    /// Breaks measures into systems.
    /// Uses the first voice as representative for measure widths.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc
    /// Uses Knuth-Plass optimal algorithm when UseOptimalLineBreaking is true,
    /// otherwise falls back to greedy first-fit algorithm.
    /// </remarks>
    private List<List<Measure>> BreakIntoSystems(Score score)
    {
        var measures = score.Voice.Measures;
        double firstPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: true);
        double continuationPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: false);

        if (_options.UseOptimalLineBreaking)
        {
            // Use Knuth-Plass optimal line breaking
            var breaker = new KnuthPlassBreaker(
                _options.ContentWidth,
                firstPrefixWidth,
                continuationPrefixWidth,
                _options.LineBreakingTolerance);

            return breaker.BreakIntoLines(measures);
        }

        // Fallback to greedy first-fit algorithm
        return BreakIntoSystemsGreedy(measures, firstPrefixWidth, continuationPrefixWidth);
    }

    /// <summary>
    /// Breaks measures into systems using a greedy first-fit algorithm.
    /// </summary>
    private List<List<Measure>> BreakIntoSystemsGreedy(
        ImmutableArray<Measure> measures,
        double firstPrefixWidth,
        double continuationPrefixWidth)
    {
        var result = new List<List<Measure>>();
        var currentSystem = new List<Measure>();

        double availableWidth = _options.ContentWidth;
        double currentWidth = firstPrefixWidth;

        foreach (var measure in measures)
        {
            double measureWidth = SpacingRules.CalculateMeasureIdealWidth(measure);

            // Check if measure fits in current system
            if (currentSystem.Count > 0 && currentWidth + measureWidth > availableWidth)
            {
                // Start new system
                result.Add(currentSystem);
                currentSystem = new List<Measure>();
                currentWidth = continuationPrefixWidth;
            }

            currentSystem.Add(measure);
            currentWidth += measureWidth;

            // Force line break if measure has break keyword
            if (measure.HasBreakAfter && currentSystem.Count > 0)
            {
                result.Add(currentSystem);
                currentSystem = new List<Measure>();
                currentWidth = continuationPrefixWidth;
            }
        }

        // Add final system
        if (currentSystem.Count > 0)
            result.Add(currentSystem);

        return result;
    }

    /// <summary>
    /// Layouts a single system with justification.
    /// </summary>
    private SystemLayout LayoutSystem(
        int systemIndex,
        List<Measure> measures,
        double y,
        int keySharps,
        bool isFirstSystem,
        int firstMeasureIndex)
    {
        double prefixWidth = SpacingRules.CalculatePrefixWidth(keySharps, isFirstSystem);
        double startX = _options.MarginLeft + prefixWidth;
        double rightEdge = _options.PageWidth - _options.MarginRight;
        double availableWidth = rightEdge - startX;

        // Collect springs and barline widths for each measure
        // LILYPOND-REF: lily/simple-spacer.cc - spring-based justification
        var measureSprings = new List<ImmutableArray<Spring>>();
        var measureBarlineWidths = new List<double>();
        double totalBarlineWidth = 0;

        foreach (var measure in measures)
        {
            var springs = SpacingRules.CreateSpringsForMeasure(measure);
            measureSprings.Add(springs);
            
            double barlineWidth = SpacingRules.GetBarlineWidth(measure.StartBarline)
                                + SpacingRules.GetBarlineWidth(measure.EndBarline);
            measureBarlineWidths.Add(barlineWidth);
            totalBarlineWidth += barlineWidth;
        }

        // Collect all springs and solve for target width
        var allSprings = measureSprings.SelectMany(s => s).ToImmutableArray();
        double springTargetWidth = availableWidth - totalBarlineWidth;
        
        double force = 0;
        if (allSprings.Length > 0)
        {
            var solver = new SpringSolver(allSprings);
            var (solvedForce, fits) = solver.Solve(springTargetWidth, _options.RaggedRight);
            force = fits ? solvedForce : 0; // Use ideal spacing if doesn't fit
        }

        // Layout measures using the solved force
        var measureLayouts = new List<MeasureLayout>();
        double currentX = startX;

        for (int i = 0; i < measures.Count; i++)
        {
            // Calculate measure width: barline widths + spring lengths at force
            double measureWidth = measureBarlineWidths[i];
            foreach (var spring in measureSprings[i])
            {
                measureWidth += spring.Length(force);
            }
            
            var itemLayouts = LayoutMeasureItems(measures[i], measureWidth);

            measureLayouts.Add(new MeasureLayout(
                firstMeasureIndex + i,
                currentX,
                measureWidth,
                itemLayouts));

            currentX += measureWidth;
        }

        return new SystemLayout(
            systemIndex,
            y,
            prefixWidth,
            measureLayouts.ToImmutableArray());
    }
    /// <summary>
    /// Pre-calculates measure layouts for skyline building (without creating full SystemLayout).
    /// </summary>
    private ImmutableArray<MeasureLayout> LayoutMeasuresForSystem(
        List<Measure> measures,
        int keySharps,
        bool isFirstSystem,
        int firstMeasureIndex)
    {
        double prefixWidth = SpacingRules.CalculatePrefixWidth(keySharps, isFirstSystem);
        double startX = _options.MarginLeft + prefixWidth;
        double rightEdge = _options.PageWidth - _options.MarginRight;
        double availableWidth = rightEdge - startX;

        // Collect springs and barline widths for each measure
        var measureSprings = new List<ImmutableArray<Spring>>();
        var measureBarlineWidths = new List<double>();
        double totalBarlineWidth = 0;

        foreach (var measure in measures)
        {
            var springs = SpacingRules.CreateSpringsForMeasure(measure);
            measureSprings.Add(springs);
            
            double barlineWidth = SpacingRules.GetBarlineWidth(measure.StartBarline)
                                + SpacingRules.GetBarlineWidth(measure.EndBarline);
            measureBarlineWidths.Add(barlineWidth);
            totalBarlineWidth += barlineWidth;
        }

        // Collect all springs and solve for target width
        var allSprings = measureSprings.SelectMany(s => s).ToImmutableArray();
        double springTargetWidth = availableWidth - totalBarlineWidth;
        
        double force = 0;
        if (allSprings.Length > 0)
        {
            var solver = new SpringSolver(allSprings);
            var (solvedForce, fits) = solver.Solve(springTargetWidth, _options.RaggedRight);
            force = fits ? solvedForce : 0;
        }

        // Layout measures using the solved force
        var measureLayouts = new List<MeasureLayout>();
        double currentX = startX;

        for (int i = 0; i < measures.Count; i++)
        {
            double measureWidth = measureBarlineWidths[i];
            foreach (var spring in measureSprings[i])
            {
                measureWidth += spring.Length(force);
            }
            
            var itemLayouts = LayoutMeasureItems(measures[i], measureWidth);

            measureLayouts.Add(new MeasureLayout(
                firstMeasureIndex + i,
                currentX,
                measureWidth,
                itemLayouts));

            currentX += measureWidth;
        }

        return measureLayouts.ToImmutableArray();
    }

    /// <summary>
    /// Layouts items within a measure using the Spring-Rod model.
    /// </summary>
    /// <remarks>
    /// The Spring-Rod model:
    /// 1. Creates springs between adjacent items (and between barlines and items)
    /// 2. Each spring has an ideal distance (based on duration) and minimum distance (to avoid collision)
    /// 3. A solver finds the force that achieves the target width while respecting constraints
    /// </remarks>
    private ImmutableArray<ItemLayout> LayoutMeasureItems(Measure measure, double totalWidth)
    {
        if (measure.Items.Length == 0)
            return ImmutableArray<ItemLayout>.Empty;

        // Calculate barline widths
        double startBarlineWidth = SpacingRules.GetBarlineWidth(measure.StartBarline);
        double endBarlineWidth = SpacingRules.GetBarlineWidth(measure.EndBarline);

        // Create springs for the measure
        var springs = SpacingRules.CreateSpringsForMeasure(measure);

        // Calculate target width for the spring chain
        // This is the distance from after start barline to before end barline
        double targetWidth = totalWidth - startBarlineWidth - endBarlineWidth;

        // Solve for the force that achieves target width
        var solver = new SpringSolver(springs);
        double force = solver.SolveForWidth(targetWidth);

        // Get positions (these are reference point positions relative to start barline)
        var positions = solver.GetPositions(force, startX: 0);

        // Convert to ItemLayout
        // positions[0] = first item position
        // positions[i + 1] = position of item i
        // positions[N] = end position (should equal targetWidth)
        var layouts = new List<ItemLayout>();

        for (int i = 0; i < measure.Items.Length; i++)
        {
            // X position relative to measure start (add startBarlineWidth)
            double x = startBarlineWidth + positions[i + 1];

            // Width is distance to next position
            double width = positions[i + 2] - positions[i + 1];

            layouts.Add(new ItemLayout(i, x, width));
        }

        return layouts.ToImmutableArray();
    }

    /// <summary>
    /// Detects ties and calculates their layouts.
    /// </summary>
    private ImmutableArray<TieLayout> LayoutTies(Score score, ImmutableArray<SystemLayout> systems)
    {
        // Detect ties in the score
        var ties = _tieDetector.DetectTies(score);

        if (ties.Length == 0)
            return ImmutableArray<TieLayout>.Empty;

        // Build a map from measure index to (system, measureLayout)
        var measureMap = new Dictionary<int, (SystemLayout system, MeasureLayout measure)>();
        foreach (var system in systems)
        {
            foreach (var measureLayout in system.Measures)
            {
                measureMap[measureLayout.MeasureIndex] = (system, measureLayout);
            }
        }

        // Calculate layout for each tie
        var tieLayouts = new List<TieLayout>();

        foreach (var tie in ties)
        {
            // Get layout info for start and end measures
            if (!measureMap.TryGetValue(tie.StartMeasureIndex, out var startInfo))
                continue;
            if (!measureMap.TryGetValue(tie.EndMeasureIndex, out var endInfo))
                continue;

            var (startSystem, startMeasure) = startInfo;
            var (endSystem, endMeasure) = endInfo;

            // Calculate X positions
            double startX = startMeasure.X;
            double endX = endMeasure.X;

            if (tie.StartItemIndex < startMeasure.Items.Length)
                startX += startMeasure.Items[tie.StartItemIndex].X;
            if (tie.EndItemIndex < endMeasure.Items.Length)
                endX += endMeasure.Items[tie.EndItemIndex].X;

            // Calculate Y position (staff middle + staff position offset)
            double staffMiddleY = startSystem.Y + _options.StaffHeight / 2;
            double y = staffMiddleY - tie.StaffPosition / 2;  // staff position → staff spaces

            // Calculate tie layout
            var tieLayout = _tieEngraver.CalculateTieLayout(
                tie,
                startX,
                y,
                endX,
                y);

            tieLayouts.Add(tieLayout);
        }

        return tieLayouts.ToImmutableArray();
    }

    /// <summary>
    /// Detects slurs and calculates their layouts.
    /// </summary>
    private ImmutableArray<SlurLayout> LayoutSlurs(Score score, ImmutableArray<SystemLayout> systems)
    {
        // Detect slurs in the score
        var slurs = _slurDetector.DetectSlurs(score);

        if (slurs.Length == 0)
            return ImmutableArray<SlurLayout>.Empty;

        // Build a map from measure index to (system, measureLayout)
        var measureMap = new Dictionary<int, (SystemLayout system, MeasureLayout measure)>();
        foreach (var system in systems)
        {
            foreach (var measureLayout in system.Measures)
            {
                measureMap[measureLayout.MeasureIndex] = (system, measureLayout);
            }
        }

        // Calculate layout for each slur
        var slurLayouts = new List<SlurLayout>();

        foreach (var slur in slurs)
        {
            // Get layout info for start and end measures
            if (!measureMap.TryGetValue(slur.StartMeasureIndex, out var startInfo))
                continue;
            if (!measureMap.TryGetValue(slur.EndMeasureIndex, out var endInfo))
                continue;

            var (startSystem, startMeasure) = startInfo;
            var (endSystem, endMeasure) = endInfo;

            // Calculate X positions
            double startX = startMeasure.X;
            double endX = endMeasure.X;

            if (slur.StartItemIndex < startMeasure.Items.Length)
                startX += startMeasure.Items[slur.StartItemIndex].X;
            if (slur.EndItemIndex < endMeasure.Items.Length)
                endX += endMeasure.Items[slur.EndItemIndex].X;

            // Calculate Y positions (staff middle + staff position offset)
            double staffMiddleY = startSystem.Y + _options.StaffHeight / 2;
            double startY = staffMiddleY - slur.StartStaffPosition / 2;  // staff position → staff spaces
            double endY = staffMiddleY - slur.EndStaffPosition / 2;

            // Calculate slur layout
            var slurLayout = _slurEngraver.CalculateSlurLayout(
                slur,
                startX,
                startY,
                endX,
                endY);

            slurLayouts.Add(slurLayout);
        }

        return slurLayouts.ToImmutableArray();
    }

    // ========== Vertical Skyline Methods ==========
    // LILYPOND-REF: lily/page-layout-problem.cc:578-647 append_system()
    // LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline()

    /// <summary>
    /// Builds vertical skylines for a multi-staff system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline()
    ///
    /// The skylines track the vertical extent of all music elements:
    /// - UP skyline: highest point at each X position (notes above staff, stems up)
    /// - DOWN skyline: lowest point at each X position (notes below staff, stems down)
    /// </remarks>
    private (VerticalSkyline Up, VerticalSkyline Down) BuildSystemSkylines(
        MultiStaffScore score,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        var upSkyline = new VerticalSkyline(VerticalDirection.Up);
        var downSkyline = new VerticalSkyline(VerticalDirection.Down);

        // All dimensions in staff spaces (coordinate system is unified)
        double staffHeight = _options.StaffHeight;
        // Relative coordinates: staff top = 0, middle = staffHeight/2
        double staffMiddleY = staffHeight / 2;
        double stemLength = 3.5; // Standard stem length in staff spaces
        double noteheadHeight = 1.0; // Approximately 1 staff space

        // Only process the first (topmost) staff for top margin calculation
        // Other staves are below the first one, so they don't affect the top margin
        var firstStaff = score.StaffGroups[0].PrimaryStaff;
        foreach (var voice in firstStaff.Voices)
        {
            for (int measureIndex = 0; measureIndex < voice.Measures.Length; measureIndex++)
            {
                if (measureIndex >= measureLayouts.Length)
                    continue;

                var measure = voice.Measures[measureIndex];
                var measureLayout = measureLayouts[measureIndex];
                for (int itemIndex = 0; itemIndex < measure.Items.Length; itemIndex++)
                {
                    if (itemIndex >= measureLayout.Items.Length)
                        continue;

                    var item = measure.Items[itemIndex];
                    var itemLayout = measureLayout.Items[itemIndex];
                    double itemX = measureLayout.X + itemLayout.X;

                    switch (item)
                    {
                        case NoteItem note:
                            AddNoteToSkylines(note, itemX, staffMiddleY,
                                stemLength, noteheadHeight, upSkyline, downSkyline);
                            break;
                        case ChordItem chord:
                            foreach (var chordNote in chord.Notes)
                            {
                                double noteY = staffMiddleY - chordNote.StaffPosition / 2.0;
                                bool stemUp = chordNote.StaffPosition < 4;
                                AddNoteBoxToSkylines(chordNote.StaffPosition, itemX, noteY,
                                    stemLength, noteheadHeight, stemUp,
                                    upSkyline, downSkyline);
                            }
                            break;
                    }
                }
            }
        }

        return (upSkyline, downSkyline);
    }
    /// <summary>
    /// Builds vertical skylines for a single-staff system.
    /// </summary>
    private (VerticalSkyline Up, VerticalSkyline Down) BuildSystemSkylines(
        List<Measure> measures,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        var upSkyline = new VerticalSkyline(VerticalDirection.Up);
        var downSkyline = new VerticalSkyline(VerticalDirection.Down);

        // All dimensions in staff spaces (coordinate system is unified)
        double staffHeight = _options.StaffHeight;
        double staffMiddleY = staffHeight / 2;
        double stemLength = 3.5; // Standard stem length in staff spaces
        double noteheadHeight = 1.0; // Approximately 1 staff space

        // Process measures in this system
        for (int measureIndex = 0; measureIndex < measures.Count; measureIndex++)
        {
            if (measureIndex >= measureLayouts.Length)
                continue;

            var measure = measures[measureIndex];
            var measureLayout = measureLayouts[measureIndex];
            for (int itemIndex = 0; itemIndex < measure.Items.Length; itemIndex++)
            {
                if (itemIndex >= measureLayout.Items.Length)
                    continue;

                var item = measure.Items[itemIndex];
                var itemLayout = measureLayout.Items[itemIndex];
                double itemX = measureLayout.X + itemLayout.X;

                switch (item)
                {
                    case NoteItem note:
                        AddNoteToSkylines(note, itemX, staffMiddleY,
                            stemLength, noteheadHeight, upSkyline, downSkyline);
                        break;
                    case ChordItem chord:
                        foreach (var chordNote in chord.Notes)
                        {
                            double noteY = staffMiddleY - chordNote.StaffPosition / 2.0;
                            bool stemUp = chordNote.StaffPosition < 4;
                            AddNoteBoxToSkylines(chordNote.StaffPosition, itemX, noteY,
                                stemLength, noteheadHeight, stemUp,
                                upSkyline, downSkyline);
                        }
                        break;
                }
            }
        }

        return (upSkyline, downSkyline);
    }

    /// <summary>
    /// Adds a note's bounding boxes to the skylines.
    /// All coordinates in staff spaces.
    /// </summary>
    private void AddNoteToSkylines(
        NoteItem note,
        double x,
        double staffMiddleY,
        double stemLength,
        double noteheadHeight,
        VerticalSkyline upSkyline,
        VerticalSkyline downSkyline)
    {
        double noteY = staffMiddleY - note.StaffPosition / 2.0;
        bool stemUp = note.StemUp;

        AddNoteBoxToSkylines(note.StaffPosition, x, noteY,
            stemLength, noteheadHeight, stemUp, upSkyline, downSkyline);
    }

    /// <summary>
    /// Adds bounding boxes for a note at the given position.
    /// All coordinates in staff spaces.
    /// </summary>
    private void AddNoteBoxToSkylines(
        int staffPosition,
        double x,
        double noteY,
        double stemLength,
        double noteheadHeight,
        bool stemUp,
        VerticalSkyline upSkyline,
        VerticalSkyline downSkyline)
    {
        double noteheadWidth = 1.18; // From GlyphMetrics (in staff spaces)
        double halfNoteheadHeight = noteheadHeight / 2;

        // Notehead bounding box
        double noteLeft = x - noteheadWidth / 2;
        double noteRight = x + noteheadWidth / 2;
        double noteTop = noteY - halfNoteheadHeight;  // Remember: Y increases downward
        double noteBottom = noteY + halfNoteheadHeight;

        // Add notehead to both skylines
        var noteheadUp = VerticalSkyline.FromBox(noteLeft, noteRight, noteBottom, noteTop, VerticalDirection.Up);
        var noteheadDown = VerticalSkyline.FromBox(noteLeft, noteRight, noteBottom, noteTop, VerticalDirection.Down);
        upSkyline.Merge(noteheadUp);
        downSkyline.Merge(noteheadDown);

        // Stem bounding box (if applicable - quarter notes and shorter)
        // For half notes and whole notes, no stem
        if (stemUp)
        {
            // Stem goes up from notehead
            double stemTop = noteY - stemLength;
            double stemBottom = noteY;
            var stemSkyline = VerticalSkyline.FromBox(noteRight - 1, noteRight + 1, stemBottom, stemTop, VerticalDirection.Up);
            upSkyline.Merge(stemSkyline);
        }
        else
        {
            // Stem goes down from notehead
            double stemTop = noteY;
            double stemBottom = noteY + stemLength;
            var stemSkyline = VerticalSkyline.FromBox(noteLeft - 1, noteLeft + 1, stemBottom, stemTop, VerticalDirection.Down);
            downSkyline.Merge(stemSkyline);
        }
    }

    /// <summary>
    /// Calculates the actual header height for single-staff scores.
    /// </summary>
    private double CalculateHeaderHeight(Score score)
    {
        return CalculateHeaderHeightCore(score.Title, score.Composer);
    }


    /// <summary>
    /// Calculates the actual header height based on title and composer presence.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:434
    /// header_height_ = head ? head->extent(Y_AXIS).length() : 0;
    /// The header height is the actual size of the title/composer stencil,
    /// not a fixed reserved space.
    /// </remarks>
    private double CalculateHeaderHeight(MultiStaffScore score)
    {
        return CalculateHeaderHeightCore(score.Title, score.Composer);
    }

    /// <summary>
    /// Core header height calculation used by both Score and MultiStaffScore.
    /// </summary>
    /// <remarks>
    /// SVG text coordinates specify the baseline, which is approximately
    /// the bottom of the text (excluding descenders). Therefore:
    /// - Title at y=MarginTop has its bottom at MarginTop
    /// - Composer follows with spacing from title baseline
    /// - headerBottom = MarginTop + (vertical extent of all header elements)
    /// </remarks>
    private double CalculateHeaderHeightCore(string? title, string? composer)
    {
        // In SVG, text y is baseline (≈ bottom of text)
        // Title is rendered at MarginTop, so title bottom ≈ MarginTop
        // Only add height for elements BELOW the title baseline
        double height = 0;

        if (title != null && composer != null)
        {
            // Composer is rendered below title with spacing
            // DrawHeader: y += 3 after title, then composer
            height = 3; // Gap between title baseline and composer baseline
        }
        else if (composer != null)
        {
            // Only composer, no extra height needed
            height = 0;
        }
        // Title only: height = 0 (title bottom = MarginTop)

        return height;
    }

    /// <summary>
    /// Layouts measures for multi-staff scores with timing-based column information.
    /// </summary>
    private ImmutableArray<MeasureLayout> LayoutMeasuresForMultiStaff(MultiStaffScore score, int systemIndex)
    {
        // Get the primary voice for base layout calculation
        var primaryVoice = score.StaffGroups[0].PrimaryStaff.PrimaryVoice;
        
        var layouts = ImmutableArray.CreateBuilder<MeasureLayout>();
        double currentX = _options.MarginLeft + SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, systemIndex == 0);

        for (int measureIndex = 0; measureIndex < primaryVoice.Measures.Length; measureIndex++)
        {
            // Collect all timings from all voices for this measure
            var allTimings = CollectAllTimingsForMeasure(score, measureIndex);
            
            // Get the primary voice's measure for base calculations
            var primaryMeasure = primaryVoice.Measures[measureIndex];
            double measureWidth = SpacingRules.CalculateMeasureIdealWidth(primaryMeasure);
            
            // Calculate item layouts for the primary voice (for backward compatibility)
            var itemLayouts = LayoutMeasureItems(primaryMeasure, measureWidth);
            
            // Calculate column layouts based on all timings
            var columnLayouts = LayoutColumnsForMeasure(primaryMeasure, measureWidth, allTimings);
            
            var measureLayout = new MeasureLayout(measureIndex, currentX, measureWidth, itemLayouts, columnLayouts);
            layouts.Add(measureLayout);
            currentX += measureLayout.Width;
        }

        return layouts.ToImmutable();
    }
    
    /// <summary>
    /// Collects all unique timings from all voices for a specific measure.
    /// </summary>
    private List<Fraction> CollectAllTimingsForMeasure(MultiStaffScore score, int measureIndex)
    {
        var timings = new HashSet<Fraction>();
        
        foreach (var staffGroup in score.StaffGroups)
        {
            foreach (var staff in staffGroup.Staves)
            {
                foreach (var voice in staff.Voices)
                {
                    if (measureIndex < voice.Measures.Length)
                    {
                        var measure = voice.Measures[measureIndex];
                        var currentTiming = Fraction.Zero;
                        
                        foreach (var item in measure.Items)
                        {
                            timings.Add(currentTiming);
                            currentTiming += item.Duration;
                        }
                    }
                }
            }
        }
        
        // Sort timings
        var sortedTimings = timings.ToList();
        sortedTimings.Sort();
        return sortedTimings;
    }
    
    /// <summary>
    /// Calculates column layouts for a measure based on collected timings.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc - Each musical moment becomes a paper column
    /// LILYPOND-REF: lily/spacing-spanner.cc - Springs connect adjacent columns
    /// 
    /// This creates springs between each timing point (column) in the measure,
    /// using the same Spring-Rod model as single-staff layout.
    /// </remarks>
    private ImmutableArray<ColumnLayout> LayoutColumnsForMeasure(Measure measure, double totalWidth, List<Fraction> timings)
    {
        if (timings.Count == 0)
            return ImmutableArray<ColumnLayout>.Empty;
        
        // Calculate barline widths
        // LILYPOND-REF: lily/spacing-basic.cc:50-52 barline dimensions
        double startBarlineWidth = SpacingRules.GetBarlineWidth(measure.StartBarline);
        double endBarlineWidth = SpacingRules.GetBarlineWidth(measure.EndBarline);
        
        // LILYPOND-REF: scm/define-grobs.scm BarLine space-alist (first-note . (fixed-space . 1.3))
        double firstNoteOffset = Math.Max(startBarlineWidth, EngravingDefaults.BarLineToFirstNoteSpace);
        
        // Calculate total duration of the measure
        var totalDuration = Fraction.Zero;
        foreach (var item in measure.Items)
        {
            totalDuration += item.Duration;
        }
        
        if (totalDuration == Fraction.Zero)
            return ImmutableArray<ColumnLayout>.Empty;
        
        // LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
        // Create springs between adjacent timing columns
        var springs = new List<Spring>();
        
        // Spring from start to first timing
        var firstDuration = timings.Count > 1 ? timings[1] - timings[0] : totalDuration;
        springs.Add(SpacingRules.CreateTimingSpring(firstDuration));
        
        // Springs between timing columns
        for (int i = 1; i < timings.Count; i++)
        {
            Fraction segmentDuration;
            if (i < timings.Count - 1)
            {
                segmentDuration = timings[i + 1] - timings[i];
            }
            else
            {
                segmentDuration = totalDuration - timings[i];
            }
            springs.Add(SpacingRules.CreateTimingSpring(segmentDuration));
        }
        
        // Spring from last timing to end
        springs.Add(SpacingRules.CreateTimingSpring(Fraction.Zero)); // End spring
        
        // Available width for columns (after first note offset and before end barline)
        double targetWidth = totalWidth - firstNoteOffset - endBarlineWidth;
        
        // LILYPOND-REF: lily/simple-spacer.cc:175-205 solve for force
        var solver = new SpringSolver(springs.ToImmutableArray());
        double force = solver.SolveForWidth(targetWidth);
        
        // Get positions from spring solver
        var positions = solver.GetPositions(force, startX: 0);
        
        // Create columns with solved positions
        var columns = ImmutableArray.CreateBuilder<ColumnLayout>();
        
        for (int i = 0; i < timings.Count; i++)
        {
            var timing = timings[i];
            double x = firstNoteOffset + positions[i + 1];
            double width = positions[i + 2] - positions[i + 1];
            
            columns.Add(new ColumnLayout(timing, x, width));
        }
        
        return columns.ToImmutable();
    }
}


