// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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