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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A dynamic marking attached to a music item.
/// </summary>
/// <remarks>
/// LILYPOND-REF: dynamic-engraver.cc:36-61 Dynamic_engraver class
/// LILYPOND-REF: define-grobs.scm:1298-1327 DynamicText grob definition
/// </remarks>
public sealed record DynamicItem
{
    /// <summary>The dynamic level (ppp to fff).</summary>
    public DynamicLevel Level { get; }

    /// <summary>The text representation of this dynamic.</summary>
    public string Text { get; }

    /// <summary>The measure index where this dynamic appears.</summary>
    public int MeasureIndex { get; }

    /// <summary>The item index within the measure.</summary>
    public int ItemIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }

    /// <summary>
    /// Global staff index (see <c>MultiStaffScore.EnumerateStaves</c>) this
    /// dynamic belongs to. Score-level dynamics carry no staff in the model, so
    /// the collector stamps it; layout uses it to position the dynamic under its
    /// OWN staff (not always the first) and to clear its own voices' stems.
    /// </summary>
    public int StaffIndex { get; }

    /// <summary>Forced ABOVE the staff (from <c>@f.up</c>); default is below.</summary>
    public bool IsAbove { get; init; }

    /// <summary>
    /// True for free expressive text (<c>@text("dolce")</c>): rides the whole
    /// DynamicText pipeline (placement, stacking, per-staff routing, ossia
    /// shrink) but is NOT a dynamic level — hairpins do not terminate on it,
    /// MIDI ignores it, and it prints in plain italic instead of bold.
    /// LILYPOND-REF: scm/define-grobs.scm TextScript (direction DOWN default).
    /// </summary>
    public bool IsExpressiveText { get; init; }

    /// <summary>Creates a dynamic marking of the given level.</summary>
    public DynamicItem(DynamicLevel level, int measureIndex, int itemIndex,
        int sourcePosition, int staffIndex = 0)
    {
        Level = level;
        Text = GetDynamicText(level);
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        SourcePosition = sourcePosition;
        StaffIndex = staffIndex;
    }

    /// <summary>Creates a free expressive-text item (<c>@text("…")</c>).</summary>
    public DynamicItem(string text, int measureIndex, int itemIndex,
        int sourcePosition, int staffIndex = 0)
    {
        Level = DynamicLevel.None;
        Text = text;
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        SourcePosition = sourcePosition;
        StaffIndex = staffIndex;
        IsExpressiveText = true;
    }

    private static string GetDynamicText(DynamicLevel level) => level switch
    {
        DynamicLevel.PPPPP => "ppppp",
        DynamicLevel.PPPP => "pppp",
        DynamicLevel.PPP => "ppp",
        DynamicLevel.PP => "pp",
        DynamicLevel.P => "p",
        DynamicLevel.MP => "mp",
        DynamicLevel.MF => "mf",
        DynamicLevel.FP => "fp",
        DynamicLevel.F => "f",
        DynamicLevel.SF => "sf",
        DynamicLevel.FF => "ff",
        DynamicLevel.SFZ => "sfz",
        DynamicLevel.RF => "rf",
        DynamicLevel.RFZ => "rfz",
        DynamicLevel.FZ => "fz",
        DynamicLevel.SFFZ => "sffz",
        DynamicLevel.FFF => "fff",
        DynamicLevel.FFFF => "ffff",
        DynamicLevel.FFFFF => "fffff",
        _ => ""
    };
}
