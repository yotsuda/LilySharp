namespace Lilysharp.Core.Svg;

/// <summary>
/// Engine for calculating page layout and spacing.
/// Implements LilyPond-style spring-based vertical spacing.
/// </summary>
public class LayoutEngine
{
    private readonly PaperSettings _paper;
    private readonly SpacingSettings _spacing;
    
    public LayoutEngine(PaperSettings? paper = null, SpacingSettings? spacing = null)
    {
        _paper = paper ?? PaperSettings.Default;
        _spacing = spacing ?? SpacingSettings.Default;
    }
    
    /// <summary>
    /// Calculates the layout for a page with the given lines.
    /// Returns the Y positions for each line in staff-spaces from the top.
    /// </summary>
    public PageLayout CalculatePageLayout(List<LineDetails> lines, int pageNumber, bool isLastPage)
    {
        var layout = new PageLayout
        {
            PageNumber = pageNumber,
            PaperSettings = _paper,
            SpacingSettings = _spacing
        };
        
        if (lines.Count == 0)
            return layout;
        
        // Calculate available height in staff-spaces
        double printableHeightMm = _paper.GetPrintableHeight();
        double printableHeightSs = printableHeightMm / PaperSettings.PointsToMm(_paper.StaffSpace);
        
        // Calculate rod height (fixed content height)
        double rodHeight = CalculateRodHeight(lines);
        
        // Calculate spring length and inverse k
        var (springLen, inverseK) = CalculateSpringParameters(lines);
        
        // Calculate force needed to fill the page (or 0 if ragged)
        double force = 0;
        bool ragged = _spacing.RaggedBottom || (isLastPage && _spacing.RaggedLastBottom);
        
        if (!ragged)
        {
            double availableStretch = printableHeightSs - rodHeight;
            if (availableStretch > 0 && inverseK > 0.1)
            {
                force = availableStretch / inverseK;
            }
        }
        
        // Calculate Y positions
        layout.Force = force;
        layout.LinePositions = CalculateLinePositions(lines, force);
        
        return layout;
    }
    
    /// <summary>
    /// Calculates the total fixed height of all lines.
    /// </summary>
    private double CalculateRodHeight(List<LineDetails> lines)
    {
        double height = 0;
        
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            
            if (i == 0)
            {
                // First line: full height plus top spacing
                var topSpec = line.IsTitle ? _spacing.TopMarkup : _spacing.TopSystem;
                height += topSpec.MinimumDistance + topSpec.Padding;
                height += line.FullHeight();
            }
            else
            {
                // Subsequent lines: just the height
                height += line.Tallness;
            }
        }
        
        // Add bottom spacing
        if (lines.Count > 0)
        {
            height += _spacing.LastBottom.MinimumDistance + _spacing.LastBottom.Padding;
        }
        
        return height;
    }
    
    /// <summary>
    /// Calculates the spring parameters for the layout.
    /// </summary>
    private (double springLen, double inverseK) CalculateSpringParameters(List<LineDetails> lines)
    {
        double springLen = 0;
        double inverseK = 0;
        
        if (lines.Count == 0)
            return (0, 0);
        
        // Top spring
        var topSpec = lines[0].IsTitle ? _spacing.TopMarkup : _spacing.TopSystem;
        springLen += topSpec.BasicDistance - topSpec.MinimumDistance;
        inverseK += topSpec.Stretchability > 0 ? topSpec.Stretchability : 1;
        
        // Springs between lines
        for (int i = 0; i < lines.Count - 1; i++)
        {
            var current = lines[i];
            var next = lines[i + 1];
            
            SpacingSpec spec = GetSpacingSpec(current, next);
            springLen += spec.BasicDistance - spec.MinimumDistance;
            inverseK += spec.Stretchability > 0 ? spec.Stretchability : 1;
        }
        
        // Bottom spring
        springLen += _spacing.LastBottom.BasicDistance - _spacing.LastBottom.MinimumDistance;
        inverseK += _spacing.LastBottom.Stretchability > 0 ? _spacing.LastBottom.Stretchability : 1;
        
        return (springLen, inverseK);
    }
    
    /// <summary>
    /// Gets the appropriate spacing specification between two lines.
    /// </summary>
    private SpacingSpec GetSpacingSpec(LineDetails current, LineDetails next)
    {
        if (current.IsTitle && next.IsTitle)
            return _spacing.MarkupMarkup;
        if (current.IsTitle)
            return _spacing.MarkupSystem;
        if (next.IsTitle)
            return _spacing.ScoreMarkup;
        return _spacing.SystemSystem;
    }
    
    /// <summary>
    /// Calculates the Y position for each line given the force.
    /// </summary>
    private List<double> CalculateLinePositions(List<LineDetails> lines, double force)
    {
        var positions = new List<double>();
        
        if (lines.Count == 0)
            return positions;
        
        double y = 0;
        
        // Top spacing
        var topSpec = lines[0].IsTitle ? _spacing.TopMarkup : _spacing.TopSystem;
        var topSpring = Spring.FromSpec(topSpec);
        y += topSpring.Length(force);
        
        // First line position
        positions.Add(y);
        y += lines[0].Tallness;
        
        // Subsequent lines
        for (int i = 1; i < lines.Count; i++)
        {
            var prev = lines[i - 1];
            var current = lines[i];
            
            SpacingSpec spec = GetSpacingSpec(prev, current);
            var spring = Spring.FromSpec(spec);
            
            // Add padding
            y += spec.Padding;
            
            // Add spring length
            y += spring.Length(force) - spec.Padding;
            
            positions.Add(y);
            y += current.Tallness;
        }
        
        return positions;
    }
    
    /// <summary>
    /// Calculates how many systems can fit on a page.
    /// </summary>
    public int EstimateSystemsPerPage(double systemHeight)
    {
        double printableHeight = _paper.GetPrintableHeight();
        double printableHeightSs = printableHeight / PaperSettings.PointsToMm(_paper.StaffSpace);
        
        // Account for top and bottom spacing
        double topSpace = _spacing.TopSystem.BasicDistance;
        double bottomSpace = _spacing.LastBottom.BasicDistance;
        double availableHeight = printableHeightSs - topSpace - bottomSpace;
        
        // Each system needs its height plus system-system spacing
        double spacePerSystem = systemHeight + _spacing.SystemSystem.BasicDistance;
        
        return Math.Max(1, (int)(availableHeight / spacePerSystem) + 1);
    }
}

/// <summary>
/// Represents the layout of a single page.
/// </summary>
public class PageLayout
{
    /// <summary>
    /// Page number (1-based).
    /// </summary>
    public int PageNumber { get; set; }
    
    /// <summary>
    /// Paper settings used for this page.
    /// </summary>
    public PaperSettings PaperSettings { get; set; } = PaperSettings.Default;
    
    /// <summary>
    /// Spacing settings used for this page.
    /// </summary>
    public SpacingSettings SpacingSettings { get; set; } = SpacingSettings.Default;
    
    /// <summary>
    /// The force applied to springs (positive = stretch, negative = compress).
    /// </summary>
    public double Force { get; set; }
    
    /// <summary>
    /// Y positions for each line in staff-spaces from the top margin.
    /// </summary>
    public List<double> LinePositions { get; set; } = new();
    
    /// <summary>
    /// Gets the left margin for this page in mm.
    /// </summary>
    public double LeftMargin => PaperSettings.GetLeftMargin(PageNumber);
    
    /// <summary>
    /// Gets the right margin for this page in mm.
    /// </summary>
    public double RightMargin => PaperSettings.GetRightMargin(PageNumber);
    
    /// <summary>
    /// Gets the line width (printable area) for this page in mm.
    /// </summary>
    public double LineWidth => PaperSettings.PaperWidth - LeftMargin - RightMargin;
    
    /// <summary>
    /// Gets the indent for the first system on this page in mm.
    /// </summary>
    public double Indent => PageNumber == 1 ? PaperSettings.Indent : PaperSettings.ShortIndent;
}