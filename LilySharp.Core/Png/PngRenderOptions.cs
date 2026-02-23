namespace LilySharp.Core.Png;

/// <summary>
/// Options for PNG rendering.
/// </summary>
public sealed class PngRenderOptions
{
    /// <summary>
    /// Scale factor for output resolution. 1.0 = 96 DPI, 2.0 = 192 DPI, 3.0 = 288 DPI.
    /// </summary>
    public float Scale { get; init; } = 2.0f;

    /// <summary>
    /// PNG compression quality (0-100). Higher = better quality but larger file.
    /// </summary>
    public int Quality { get; init; } = 100;

    /// <summary>
    /// Optional font directory for Emmentaler font files.
    /// </summary>
    public string? FontDirectory { get; init; }

    public static PngRenderOptions Default => new();
    public static PngRenderOptions HighDpi => new() { Scale = 3.0f };
    public static PngRenderOptions LowDpi => new() { Scale = 1.0f };
}
