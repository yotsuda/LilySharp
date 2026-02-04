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
    /// Gets the SMuFL codepoint for this articulation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: feta-scripts.mf - articulation glyph definitions
    /// SMuFL codepoints: U+E4A0-U+E4BF articulation marks
    /// </remarks>
    public string GetGlyph() => Type switch
    {
        // Articulations
        ArticulationType.Staccato => "\uE4A2",   // articStaccatoAbove/Below
        ArticulationType.Accent => "\uE4A0",     // articAccentAbove/Below
        ArticulationType.Tenuto => "\uE4A4",     // articTenutoAbove/Below
        ArticulationType.Marcato => "\uE4AC",    // articMarcatoAbove/Below
        ArticulationType.Fermata => "\uE4C0",    // fermataAbove
        ArticulationType.Portato => "\uE4B2",    // articTenutoStaccatoAbove/Below
        // Ornaments (SMuFL U+E560-U+E56F)
        ArticulationType.Trill => "\uE566",          // ornamentTrill
        ArticulationType.Mordent => "\uE56C",        // ornamentMordent
        ArticulationType.Prall => "\uE56D",          // ornamentMordentInverted
        ArticulationType.Turn => "\uE567",           // ornamentTurn
        ArticulationType.InvertedTurn => "\uE568",   // ornamentTurnInverted
        ArticulationType.PrallTriller => "\uE56B",   // ornamentShortTrill
        _ => ""
    };

    /// <summary>
    /// Whether this is an ornament (always placed above the note).
    /// </summary>
    public bool IsOrnament => Type is ArticulationType.Trill or ArticulationType.Mordent or
        ArticulationType.Prall or ArticulationType.Turn or ArticulationType.InvertedTurn or
        ArticulationType.PrallTriller;
}