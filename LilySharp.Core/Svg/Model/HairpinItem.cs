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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Direction of a hairpin (crescendo or decrescendo).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:140-142 grow_dir
/// </remarks>
public enum HairpinDirection
{
    /// <summary>Crescendo: opening wedge (grows louder).</summary>
    Crescendo,
    /// <summary>Decrescendo: closing wedge (grows softer).</summary>
    Decrescendo
}

/// <summary>
/// Represents a hairpin (crescendo/decrescendo wedge) spanning multiple notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:110-358
/// LILYPOND-REF: scm/define-grobs.scm:1641-1666 Hairpin grob
/// </remarks>
public sealed record HairpinItem(
    // Crescendo or decrescendo.
    HairpinDirection Direction,
    // Measure index of the start note.
    int StartMeasureIndex,
    // Item index within the start measure.
    int StartItemIndex,
    // Measure index of the end note.
    int EndMeasureIndex,
    // Item index within the end measure.
    int EndItemIndex,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: index of the originating cresc/decresc mark in score.MusicMarks,
    // so a reused layout re-derives data-pos from the live score. -1 = unresolved.
    int SourceIndex = -1,
    // The staff this hairpin belongs to (0 = first/only staff).
    int StaffIndex = 0
)
{
    /// <summary>The <c>@niente</c> at the hairpin's start moment (<c>c@niente@cresc</c>), or null.</summary>
    public DynamicItem? NienteAtStart { get; init; }

    /// <summary>The <c>@niente</c> that ends the hairpin (<c>… g@niente</c>), or null.</summary>
    public DynamicItem? NienteAtEnd { get; init; }

    /// <summary>
    /// The hairpin's THIN end touches a niente — a crescendo from it (dal niente) or a
    /// decrescendo to it (al niente) — so that end is drawn as a circle and the niente is not
    /// printed as a word. A niente at the THICK end is a contradiction (LYS4029): the wedge is
    /// drawn plain and the word is printed.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:153 Hairpin::print reads circled_tip (the circled-tip of scm/define-grob-properties.scm) — "Put a circle at
    ///   start/end of hairpins (al/del niente)", the end being the tip.
    /// </remarks>
    public bool CircledTip => Direction == HairpinDirection.Crescendo
        ? NienteAtStart != null
        : NienteAtEnd != null;

    /// <summary>The niente the circle stands for (and that is therefore not printed), or null.</summary>
    public DynamicItem? CircledNiente => Direction == HairpinDirection.Crescendo
        ? NienteAtStart
        : NienteAtEnd;

    /// <summary>The niente at the THICK end, or null — the one LYS4029 reports.</summary>
    public DynamicItem? NienteAtThickEnd => Direction == HairpinDirection.Crescendo
        ? NienteAtEnd
        : NienteAtStart;

    /// <summary>
    /// The circled tip is shared: a decrescendo al niente and a crescendo dal niente meet on one
    /// note (<c>… g@niente@cresc …</c>), so both tips hang on the note's centre and their two
    /// circles fall on one place.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:243-252 Hairpin::print, x_points — "Handle back-to-back hairpins with
    ///   a circle in the middle": with an adjacent hairpin on the same column and the circled
    ///   tip on this side, <c>x_points[d] = e.center () + d * (rad - thick / 2.0)</c>.
    /// </remarks>
    public bool TipAdjacent { get; init; }

    // Identity, not value equality: see ModelIdentity.
    public bool Equals(HairpinItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);
}
