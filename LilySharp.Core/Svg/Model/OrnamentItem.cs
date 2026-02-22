using LilySharp.Core.Svg;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Type of ornament mark.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2175-2230 TrillSpanner grob
/// LILYPOND-REF: feta-scripts.mf ornament glyph definitions
/// </remarks>
public enum OrnamentType
{
    /// <summary>Trill (tr).</summary>
    Trill,
    /// <summary>Mordent.</summary>
    Mordent,
    /// <summary>Inverted mordent (prall).</summary>
    Prall,
    /// <summary>Turn.</summary>
    Turn,
    /// <summary>Inverted turn.</summary>
    InvertedTurn,
    /// <summary>Short trill (pralltriller).</summary>
    PrallTriller
}

/// <summary>
/// An ornament mark attached to a note.
/// </summary>
/// <remarks>
/// LILYPOND-REF: trill-spanner-engraver.cc Trill_spanner_engraver class
/// LILYPOND-REF: define-grobs.scm:2175-2230 TrillSpanner grob definition
/// </remarks>
public sealed record OrnamentItem
{
    /// <summary>The ornament type.</summary>
    public OrnamentType Type { get; }

    /// <summary>The measure index where this ornament appears.</summary>
    public int MeasureIndex { get; }

    /// <summary>The item index within the measure.</summary>
    public int ItemIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }

    public OrnamentItem(OrnamentType type, int measureIndex, int itemIndex, int sourcePosition)
    {
        Type = type;
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        SourcePosition = sourcePosition;
    }

    /// <summary>
    /// Gets the Emmentaler font glyph for this ornament.
    /// </summary>
    /// <remarks>
    /// Codepoints verified against emmentaler-20.woff2 cmap table.
    /// </remarks>
    public string GetGlyph() => Type switch
    {
        OrnamentType.Trill => EmmentalerGlyphs.OrnTrill.ToString(),
        OrnamentType.Mordent => EmmentalerGlyphs.OrnMordent.ToString(),
        OrnamentType.Prall => EmmentalerGlyphs.OrnPrall.ToString(),
        OrnamentType.Turn => EmmentalerGlyphs.OrnTurn.ToString(),
        OrnamentType.InvertedTurn => EmmentalerGlyphs.OrnReverseTurn.ToString(),
        OrnamentType.PrallTriller => EmmentalerGlyphs.OrnPrallPrall.ToString(),
        _ => ""
    };
}