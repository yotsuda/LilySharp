using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// An articulation mark attached to a music item.
/// </summary>
/// <remarks>
/// LILYPOND-REF: script-engraver.cc:36-61 Script_engraver class
/// LILYPOND-REF: define-grobs.scm:2268-2310 Script grob definition
/// </remarks>
public sealed record ArticulationItem
{
    /// <summary>The articulation type.</summary>
    public ArticulationType Type { get; }

    /// <summary>The measure index where this articulation appears.</summary>
    public int MeasureIndex { get; }

    /// <summary>The item index within the measure.</summary>
    public int ItemIndex { get; }

    /// <summary>Whether this articulation should be placed above the note.</summary>
    public bool IsAbove { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }

    public ArticulationItem(ArticulationType type, int measureIndex, int itemIndex, bool isAbove, int sourcePosition)
    {
        Type = type;
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        IsAbove = isAbove;
        SourcePosition = sourcePosition;
    }

    /// <summary>
    /// Gets the Emmentaler font glyph for this articulation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: feta-scripts.mf - articulation glyph definitions
    /// Codepoints verified against emmentaler-20.woff2 cmap table.
    /// </remarks>
    public string GetGlyph() => Type switch
    {
        ArticulationType.Staccato => EmmentalerGlyphs.ArticStaccatoAbove.ToString(),
        ArticulationType.Accent => EmmentalerGlyphs.ArticAccentAbove.ToString(),
        ArticulationType.Tenuto => EmmentalerGlyphs.ArticTenutoAbove.ToString(),
        ArticulationType.Marcato => (IsAbove ? EmmentalerGlyphs.ArticMarcatoAbove : EmmentalerGlyphs.ArticMarcatoBelow).ToString(),
        ArticulationType.Fermata => (IsAbove ? EmmentalerGlyphs.FermataAbove : EmmentalerGlyphs.FermataBelow).ToString(),
        ArticulationType.Portato => (IsAbove ? EmmentalerGlyphs.ArticPortatoAbove : EmmentalerGlyphs.ArticPortatoBelow).ToString(),
        ArticulationType.Trill => EmmentalerGlyphs.OrnTrill.ToString(),
        ArticulationType.Mordent => EmmentalerGlyphs.OrnMordent.ToString(),
        ArticulationType.Prall => EmmentalerGlyphs.OrnPrall.ToString(),
        ArticulationType.Turn => EmmentalerGlyphs.OrnTurn.ToString(),
        ArticulationType.InvertedTurn => EmmentalerGlyphs.OrnReverseTurn.ToString(),
        ArticulationType.PrallTriller => EmmentalerGlyphs.OrnPrallPrall.ToString(),
        _ => ""
    };

    /// <summary>
    /// Whether this is an ornament (always placed above the note).
    /// </summary>
    public bool IsOrnament => Type is ArticulationType.Trill or ArticulationType.Mordent or
        ArticulationType.Prall or ArticulationType.Turn or ArticulationType.InvertedTurn or
        ArticulationType.PrallTriller;
}