namespace LilySharp.Core.Pdf.Renderer;

/// <summary>
/// Options for PDF rendering.
/// </summary>
public sealed class PdfRenderOptions
{
    /// <summary>Page size in points (1 point = 1/72 inch).</summary>
    public double PageWidthPt { get; init; } = 595.28; // A4

    /// <summary>Page height in points.</summary>
    public double PageHeightPt { get; init; } = 841.89; // A4

    /// <summary>Staff space size in points. Controls the overall scale.</summary>
    public double StaffSpacePt { get; init; } = 6.0; // ~1.7mm, standard for engraving

    /// <summary>Whether to embed the music font in the PDF.</summary>
    public bool EmbedFont { get; init; } = true;

    /// <summary>Directory containing font files (OTF/TTF).</summary>
    public string? FontDirectory { get; init; }

    /// <summary>Default options (A4, standard staff size).</summary>
    public static PdfRenderOptions Default => new();

    /// <summary>Letter size variant.</summary>
    public static PdfRenderOptions Letter => new()
    {
        PageWidthPt = 612,   // 8.5"
        PageHeightPt = 792   // 11"
    };
}
