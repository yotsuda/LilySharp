namespace LilySharp.Core.Svg;

/// <summary>
/// Unit conversion utilities for music engraving.
/// </summary>
/// <remarks>
/// LilySharp uses three coordinate units:
/// 
/// 1. Staff Spaces (ss) - Primary internal unit, LilyPond compatible
///    - 1 staff space = distance between two staff lines
///    - Used for: layout calculations, spacing, most measurements
///    
/// 2. Staff Positions (sp) - Half staff spaces, for pitch positions
///    - 1 staff position = 0.5 staff spaces
///    - Used for: note positions, beam Y coordinates
///    - Integer values align with staff lines/spaces
///    
/// 3. Pixels (px) - Output unit for SVG rendering
///    - Converted from staff spaces at render time only
///    - 1 staff space = SpaceHeight pixels (typically 10)
///    
/// Coordinate system:
/// - Y-axis: positive = upward (like LilyPond, opposite of SVG)
/// - Staff middle line = 0 in staff positions
/// - Conversion to SVG flips Y-axis
/// </remarks>
public static class Units
{
    /// <summary>
    /// Default pixels per staff space for SVG output.
    /// </summary>
    public const double DefaultSpaceHeight = 10.0;
    
    /// <summary>
    /// Converts staff spaces to staff positions.
    /// </summary>
    public static double SpacesToPositions(double staffSpaces) => staffSpaces * 2;
    
    /// <summary>
    /// Converts staff positions to staff spaces.
    /// </summary>
    public static double PositionsToSpaces(double staffPositions) => staffPositions / 2;
    
    /// <summary>
    /// Converts staff spaces to pixels.
    /// </summary>
    public static double SpacesToPixels(double staffSpaces, double spaceHeight = DefaultSpaceHeight) 
        => staffSpaces * spaceHeight;
    
    /// <summary>
    /// Converts staff positions to pixels.
    /// </summary>
    public static double PositionsToPixels(double staffPositions, double spaceHeight = DefaultSpaceHeight) 
        => staffPositions * spaceHeight / 2;
    
    /// <summary>
    /// Converts pixels to staff spaces.
    /// </summary>
    public static double PixelsToSpaces(double pixels, double spaceHeight = DefaultSpaceHeight) 
        => pixels / spaceHeight;
    
    /// <summary>
    /// Converts pixels to staff positions.
    /// </summary>
    public static double PixelsToPositions(double pixels, double spaceHeight = DefaultSpaceHeight) 
        => pixels * 2 / spaceHeight;
    
    /// <summary>
    /// Converts a Y coordinate from staff positions to SVG pixels.
    /// SVG Y-axis is inverted (positive = downward).
    /// </summary>
    /// <param name="staffPosition">Y in staff positions (positive = up)</param>
    /// <param name="staffMiddleY">Y coordinate of staff middle line in pixels</param>
    /// <param name="spaceHeight">Pixels per staff space</param>
    public static double StaffPositionToSvgY(double staffPosition, double staffMiddleY, double spaceHeight = DefaultSpaceHeight)
        => staffMiddleY - staffPosition * spaceHeight / 2;
    
    /// <summary>
    /// Converts a Y coordinate from staff spaces to SVG pixels.
    /// SVG Y-axis is inverted (positive = downward).
    /// </summary>
    public static double StaffSpaceToSvgY(double staffSpace, double staffMiddleY, double spaceHeight = DefaultSpaceHeight)
        => staffMiddleY - staffSpace * spaceHeight;
}
