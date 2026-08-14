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

namespace LilySharp.Core.Svg;

/// <summary>
/// Emmentaler font glyph code points for music notation symbols.
/// Based on LilyPond's Emmentaler font, which is dual-licensed GPL-3.0-or-later
/// or SIL OFL; Lily# redistributes it under the GPL branch (THIRD-PARTY-NOTICES.md).
/// </summary>
/// <remarks>
/// The code points themselves live in <c>EmmentalerGlyphs.Generated.cs</c>, resolved
/// from each glyph's feta NAME by audit/scripts/Extract-EmmentalerGlyphs.py. They are
/// private-use assignments and move between font builds, so a glyph is identified here
/// the way LilyPond identifies one — by name. This file holds only what a font cannot
/// answer: the note-value and style dispatch.
/// <para>Barlines are NOT glyphs in Emmentaler — they are drawn as shapes.</para>
/// <para>Clef modifiers ("8" above/below a clef) are NOT glyphs either; they are
/// rendered as italic text — see SharedRenderer.DrawClefModifier8.
/// LILYPOND-REF: scm/define-grobs.scm:944-975 (ClefModifier grob)</para>
/// </remarks>
internal static partial class EmmentalerGlyphs
{
    // === Dynamics (text-based in Emmentaler: plain ASCII, not private-use glyphs) ===
    public const char DynamicPiano = 'p';
    public const char DynamicMezzo = 'm';
    public const char DynamicForte = 'f';
    public const char DynamicRinforzando = 'r';
    public const char DynamicSforzando = 's';
    public const char DynamicZ = 'z';

    /// <summary>The glyph for a resolved accidental kind ("sharp", "flat",
    /// "doubleSharp", "doubleFlat"); anything else (incl. "natural") maps to the
    /// natural sign. Single source for the name-to-glyph switch.</summary>
    public static char AccidentalGlyph(string? kind) => kind switch
    {
        "doubleSharp" => AccidentalDoubleSharp,
        "sharp" => AccidentalSharp,
        "flat" => AccidentalFlat,
        "doubleFlat" => AccidentalDoubleFlat,
        "quarterSharp" => AccidentalQuarterSharp,
        "threeQuarterSharp" => AccidentalThreeQuarterSharp,
        "quarterFlat" => AccidentalQuarterFlat,
        "threeQuarterFlat" => AccidentalThreeQuarterFlat,
        _ => AccidentalNatural,
    };

    /// <summary>Gets the time signature digit glyph.</summary>
    public static char GetTimeSigDigit(int digit) => digit switch
    {
        0 => TimeSig0, 1 => TimeSig1, 2 => TimeSig2, 3 => TimeSig3, 4 => TimeSig4,
        5 => TimeSig5, 6 => TimeSig6, 7 => TimeSig7, 8 => TimeSig8, 9 => TimeSig9,
        _ => TimeSig0
    };

    // LILYPOND-REF: lily/rest.cc Rest::glyph_name — "rests." + duration-log.
    /// <summary>Gets the rest glyph for a given note value.</summary>
    public static char GetRest(int noteValue) => noteValue switch
    {
        0 => RestDoubleWhole,
        1 => RestWhole, 2 => RestHalf, 4 => RestQuarter, 8 => Rest8th,
        16 => Rest16th, 32 => Rest32nd, 64 => Rest64th, 128 => Rest128th,
        _ => RestQuarter
    };

    /// <summary>Notehead glyph for a style + note value; whole-note variants
    /// serve breve too (styled breves are not in the font).</summary>
    public static char GetNotehead(Model.NoteheadStyle style, int noteValue) => style switch
    {
        Model.NoteheadStyle.Cross => noteValue switch
        {
            0 or 1 => NoteheadCrossWhole, 2 => NoteheadCrossHalf, _ => NoteheadCrossBlack
        },
        Model.NoteheadStyle.Diamond => noteValue switch
        {
            0 or 1 => NoteheadDiamondWhole, 2 => NoteheadDiamondHalf, _ => NoteheadDiamondBlack
        },
        Model.NoteheadStyle.Triangle => noteValue switch
        {
            0 or 1 => NoteheadTriangleWhole, 2 => NoteheadTriangleHalf, _ => NoteheadTriangleBlack
        },
        Model.NoteheadStyle.Slash => noteValue switch
        {
            0 or 1 => NoteheadSlashWhole, 2 => NoteheadSlashHalf, _ => NoteheadSlashBlack
        },
        Model.NoteheadStyle.XCircle => NoteheadXCircle,
        _ => GetNotehead(noteValue),
    };

    // LILYPOND-REF: lily/note-head.cc internal_print — glyph = "noteheads.s" +
    // min(duration-log, 2) (so quarter and shorter all share the s2 filled head).
    /// <summary>Gets the notehead glyph for a given note value.</summary>
    public static char GetNotehead(int noteValue) => noteValue switch
    {
        0 => NoteheadDoubleWhole, 1 => NoteheadWhole, 2 => NoteheadHalf, _ => NoteheadBlack
    };

    // LILYPOND-REF: lily/flag.cc Flag::glyph_name — "flags." + (up ? 'u' : 'd') + duration-log.
    /// <summary>Gets the flag glyph for a given note value and stem direction.</summary>
    public static char? GetFlag(int noteValue, bool stemUp) => noteValue switch
    {
        8 => stemUp ? Flag8thUp : Flag8thDown,
        16 => stemUp ? Flag16thUp : Flag16thDown,
        32 => stemUp ? Flag32ndUp : Flag32ndDown,
        64 => stemUp ? Flag64thUp : Flag64thDown,
        128 => stemUp ? Flag128thUp : Flag128thDown,
        _ => null
    };
}
