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

namespace LilySharp.Core.Rendering;

/// <summary>
/// WHICH font program a string is measured from — the key
/// <see cref="TextFontMetrics"/> hangs every cache on.
/// </summary>
/// <remarks>
/// This used to be the pair <c>(bool sans, FontStyle style)</c>, five caches and a dozen
/// signatures wide, and that pair could only ever name one of the four bundled files. A
/// score that wrote <c>fonts { title "Georgia" }</c> was therefore DRAWN in Georgia and
/// RESERVED for in TeX Gyre Schola — measured 2026-08-18, a 16-character tempo mark at
/// 2.2 staff spaces lands anywhere from 2.05 short to 3.61 long depending on the face.
/// Adding <see cref="Name"/> is what lets the reservation follow the drawing.
/// <para>
/// ⚠️ THE BUNDLED CASE IS A DISTINCT VALUE, not "the name happens to be Schola".
/// <see cref="Bundled"/> means "the file this engine ships for that family", which is what
/// every one of the 518 LP-geometry ledger points was measured against and what a score
/// that asked for nothing must keep getting. A named face that HAPPENS to be one of the
/// bundled families resolves to the same file (<see cref="TextFontMetrics"/> looks in the
/// bundle before it looks at the machine), so the two agree by construction rather than by
/// coincidence — and that is exactly what makes a deterministic falsifier for the named
/// path possible on any machine.
/// </para>
/// <para>
/// ⚠️ A NAMED FACE MAY NOT EXIST HERE. Asking the machine's font manager is how LilyPond
/// resolves <c>font-name</c> too (LILYPOND-REF: lily/font-select.cc:193-217 select_font
/// hands the description to find_pango_font), so the same score CAN lay out differently on
/// two machines. That is a property of naming a face, not a defect to be papered over:
/// <see cref="TextFontMetrics.CanMeasure"/> answers whether this machine can, and the
/// caller decides what to do about it out loud. Nothing in this file silently substitutes.
/// </para>
/// </remarks>
/// <param name="Name">
/// The face a score named, or null for the bundled file of <paramref name="Sans"/>.
/// </param>
/// <param name="Sans">
/// Which bundled family this is, or falls back to: the family the layout reserved against
/// before any name was involved, kept beside the name so a face that cannot be resolved
/// has somewhere to land without a second lookup.
/// </param>
/// <param name="Style">Weight and slant.</param>
public readonly record struct TextFace(string? Name, bool Sans, FontStyle Style)
{
    /// <summary>The bundled file for a family and style — what the engine measured with
    /// before a score could name anything, and still does when it names nothing.</summary>
    public static TextFace Bundled(bool sans = false, FontStyle style = FontStyle.Regular)
        => new(null, sans, style);

    /// <summary>A face a score named, with the bundled family it falls back to.</summary>
    public static TextFace Named(string name, bool sans, FontStyle style)
        => new(name, sans, style);

    /// <summary>True when this is one of the four bundled files.</summary>
    public bool IsBundled => Name is null;

    /// <summary>The same face at another weight/slant.</summary>
    public TextFace With(FontStyle style) => this with { Style = style };
}
