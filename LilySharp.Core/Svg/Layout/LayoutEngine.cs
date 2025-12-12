using System.Collections.Immutable;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Engine for calculating score layout.
/// </summary>
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
        double currentY = _options.MarginTop;
        
        // Calculate header height
        double headerHeight = (score.Title != null || score.Composer != null) ? 50 : 0;
        currentY += headerHeight;
        
        // Break measures into systems (using first voice as representative)
        var systemMeasures = BreakIntoSystems(score);
        
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
        
        double totalHeight = currentY - _options.SystemSpacing + _options.MarginTop;
        
        // Create single page (TODO: implement page breaking for multi-page scores)
        var systemsArray = systems.ToImmutableArray();
        var page = new PageLayout(
            PageIndex: 0,
            Width: _options.PageWidth,
            Height: totalHeight,
            HeaderHeight: headerHeight,
            Systems: systemsArray);
        
        // Detect and layout beams
        var beamLayouts = LayoutBeams(score, systemsArray);
        
        // Detect and layout ties
        var tieLayouts = LayoutTies(score, systemsArray);
        
        // Detect and layout slurs
        var slurLayouts = LayoutSlurs(score, systemsArray);
        
        // Calculate voice collision offsets for multi-voice scores
        var voiceOffsets = CalculateVoiceOffsets(score);
        
        return new ScoreLayout(
            ImmutableArray.Create(page),
            systemsArray,
            beamLayouts,
            tieLayouts,
            slurLayouts,
            voiceOffsets);
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
        double noteheadWidth = SmuflDefaults.NoteheadBlackWidth * _options.StaffSpaceSize;
        
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
            
            // Calculate beam layout
            var beamLayout = _beamEngraver.CalculateBeamLayout(
                group,
                itemXPositions,
                _options.StaffSpaceSize);
            
            beamLayouts.Add(beamLayout);
        }
        
        return beamLayouts.ToImmutableArray();
    }
    
    /// <summary>
    /// Breaks measures into systems using a greedy algorithm.
    /// Uses the first voice as representative for measure widths.
    /// </summary>
    private List<List<Measure>> BreakIntoSystems(Score score)
    {
        var result = new List<List<Measure>>();
        var currentSystem = new List<Measure>();
        
        double availableWidth = _options.ContentWidth;
        double firstPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: true);
        double continuationPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: false);
        
        double currentWidth = firstPrefixWidth;
        
        // Use first voice for measure breaking (all voices should have same measure count)
        foreach (var measure in score.Voice.Measures)
        {
            double measureWidth = SpacingRules.CalculateMeasureMinWidth(measure);
            
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
        
        // Calculate minimum widths
        double totalMinWidth = 0;
        var measureMinWidths = new List<double>();
        
        foreach (var measure in measures)
        {
            double minWidth = SpacingRules.CalculateMeasureMinWidth(measure);
            measureMinWidths.Add(minWidth);
            totalMinWidth += minWidth;
        }
        
        // Calculate stretch factor for justification
        double extraSpace = availableWidth - totalMinWidth;
        double stretchPerMeasure = measures.Count > 0 ? extraSpace / measures.Count : 0;
        
        // Clamp stretch to prevent excessive stretching
        stretchPerMeasure = Math.Max(0, Math.Min(stretchPerMeasure, 50));
        
        // Layout measures
        var measureLayouts = new List<MeasureLayout>();
        double currentX = startX;
        
        for (int i = 0; i < measures.Count; i++)
        {
            double measureWidth = measureMinWidths[i] + stretchPerMeasure;
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
            double y = staffMiddleY - tie.StaffPosition * _options.SpaceHeight / 2;
            
            // Calculate tie layout
            var tieLayout = _tieEngraver.CalculateTieLayout(
                tie,
                startX,
                y,
                endX,
                y,
                _options.StaffSpaceSize);
            
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
            double startY = staffMiddleY - slur.StartStaffPosition * _options.SpaceHeight / 2;
            double endY = staffMiddleY - slur.EndStaffPosition * _options.SpaceHeight / 2;
            
            // Calculate slur layout
            var slurLayout = _slurEngraver.CalculateSlurLayout(
                slur,
                startX,
                startY,
                endX,
                endY,
                _options.StaffSpaceSize);
            
            slurLayouts.Add(slurLayout);
        }
        
        return slurLayouts.ToImmutableArray();
    }
}