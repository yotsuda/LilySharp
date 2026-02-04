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
    /// Gets the SMuFL codepoint for this ornament.
    /// </summary>
    /// <remarks>
    /// SMuFL ornaments: U+E560-U+E56F
    /// </remarks>
    public string GetGlyph() => Type switch
    {
        OrnamentType.Trill => "\uE566",          // ornamentTrill
        OrnamentType.Mordent => "\uE56C",        // ornamentMordent
        OrnamentType.Prall => "\uE56D",          // ornamentMordentInverted
        OrnamentType.Turn => "\uE567",           // ornamentTurn
        OrnamentType.InvertedTurn => "\uE568",   // ornamentTurnInverted
        OrnamentType.PrallTriller => "\uE56B",   // ornamentShortTrill
        _ => ""
    };
}