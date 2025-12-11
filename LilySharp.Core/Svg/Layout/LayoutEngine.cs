using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Engine for calculating score layout.
/// </summary>
public sealed class LayoutEngine
{
    private readonly LayoutOptions _options;
    
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
        
        // Break measures into systems
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
        
        return new ScoreLayout(
            _options.PageWidth,
            totalHeight,
            headerHeight,
            systems.ToImmutableArray());
    }
    
    /// <summary>
    /// Breaks measures into systems using a greedy algorithm.
    /// </summary>
    private List<List<Measure>> BreakIntoSystems(Score score)
    {
        var result = new List<List<Measure>>();
        var currentSystem = new List<Measure>();
        
        double availableWidth = _options.ContentWidth;
        double firstPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: true);
        double continuationPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: false);
        
        double currentWidth = firstPrefixWidth;
        
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
    /// Layouts items within a measure with proportional spacing.
    /// </summary>
    private ImmutableArray<ItemLayout> LayoutMeasureItems(Measure measure, double totalWidth)
    {
        if (measure.Items.Length == 0)
            return ImmutableArray<ItemLayout>.Empty;
        
        // Calculate barline widths
        double startBarlineWidth = SpacingRules.GetBarlineWidth(measure.StartBarline);
        double endBarlineWidth = SpacingRules.GetBarlineWidth(measure.EndBarline);
        
        // Available width for items
        double contentWidth = totalWidth - startBarlineWidth - endBarlineWidth;
        
        // Calculate minimum widths and stretch weights
        double totalMinWidth = 0;
        double totalStretchWeight = 0;
        var itemInfo = new List<(double minWidth, double stretchWeight)>();
        
        foreach (var item in measure.Items)
        {
            double minWidth = SpacingRules.CalculateItemWidth(item);
            double stretchWeight = SpacingRules.CalculateStretchWeight(item);
            
            itemInfo.Add((minWidth, stretchWeight));
            totalMinWidth += minWidth;
            totalStretchWeight += stretchWeight;
        }
        
        // Calculate extra space and distribute proportionally
        double extraSpace = Math.Max(0, contentWidth - totalMinWidth);
        
        // Layout items
        var layouts = new List<ItemLayout>();
        double currentX = startBarlineWidth;
        
        for (int i = 0; i < measure.Items.Length; i++)
        {
            double baseWidth = itemInfo[i].minWidth;
            double stretch = totalStretchWeight > 0 
                ? extraSpace * (itemInfo[i].stretchWeight / totalStretchWeight)
                : extraSpace / measure.Items.Length;
            
            double itemWidth = baseWidth + stretch;
            
            layouts.Add(new ItemLayout(i, currentX, itemWidth));
            currentX += itemWidth;
        }
        
        return layouts.ToImmutableArray();
    }
}