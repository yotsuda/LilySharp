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

    /// <summary>Gets the rest glyph for a note value at a staff position.</summary>
    /// <param name="noteValue">1 = whole, 2 = half, 0 = breve, 4/8/… shorter.</param>
    /// <param name="staffPosition">Where the rest's ORIGIN was drawn, in staff
    /// positions about the middle line (LilyPond's <c>get_position</c> — the whole
    /// rest's +2 already applied, since it is the origin that hangs from that line).</param>
    /// <remarks>
    /// LILYPOND-REF: lily/rest.cc:166-227 Rest::glyph_name — "rests." + duration-log,
    /// plus an "o" suffix for the LEDGERED cut of the glyph. A breve, whole or half
    /// rest OFF a staff line carries its own ledger line inside the glyph (there is no
    /// LedgerLineSpanner for rests), so the half rest LilyPond pushes to an odd position
    /// out of the staff prints as <c>rests.1o</c>, not <c>rests.1</c>.
    /// LILYPOND-REF: lily/staff-symbol.cc:372-396 Staff_symbol::on_line — with
    /// <c>allow_ledger</c> false (that is what <c>on_staff_line</c> passes), only the
    /// REAL lines count, so every position outside the staff is off-line and ledgers.
    /// <para>⚠️ The ledger changes the INK only. LilyPond keeps it out of the X extent
    /// on purpose (rest.cc:281-289 asks for the unledgered stencil there, because the
    /// Y position that decides it is not known until after line breaking), and the Y
    /// extent it reports is the bare bar's either way (measured: an <c>rests.1o</c> at
    /// position −11 reports <c>(0 . 0.625)</c>, the same as <c>rests.1</c>). So spacing,
    /// skylines and the dot column all keep reading the unledgered box.</para>
    /// </remarks>
    public static char GetRest(int noteValue, double staffPosition)
    {
        // LILYPOND-REF: lily/rest.cc:173-174 — int (get_position (me) + offset).
        // C++ truncates toward zero; so does this cast.
        int pos = (int) staffPosition;
        return noteValue switch
        {
            0 => IsLedgered(0, pos) ? RestDoubleWholeLedgered : RestDoubleWhole,
            1 => IsLedgered(1, pos) ? RestWholeLedgered : RestWhole,
            2 => IsLedgered(2, pos) ? RestHalfLedgered : RestHalf,
            4 => RestQuarter, 8 => Rest8th,
            16 => Rest16th, 32 => Rest32nd, 64 => Rest64th, 128 => Rest128th,
            _ => RestQuarter
        };
    }

    /// <summary>
    /// Whether a rest of this note value at this staff position prints the cut of its
    /// glyph that carries a ledger line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/rest.cc:170-185 Rest::glyph_name is_ledgered — a half rest
    /// needs a ledger if it is not LYING on a staff line, a whole rest if it is not
    /// HANGING from one, a breve if neither (its own line, or the one two positions
    /// above it, being a staff line spares it).
    /// <para>The staff's <c>line-positions</c> are {−4, −2, 0, 2, 4}: the five lines of
    /// the notation staff, which is the only staff symbol Lily# engraves in positions.
    /// LILYPOND-REF: scm/define-grobs.scm StaffSymbol — line-count 5.</para>
    /// </remarks>
    private static bool IsLedgered(int noteValue, int pos) =>
        !OnStaffLine(pos)
        && !(noteValue == 0 && OnStaffLine(pos + 2));

    /// <summary>Whether a staff position is one of the five staff lines.</summary>
    /// <remarks>LILYPOND-REF: lily/staff-symbol.cc:372-382 Staff_symbol::on_line —
    /// the position equals one of <c>line-positions</c>.</remarks>
    private static bool OnStaffLine(int pos) => EngravingDefaults.OnStaffLine(pos);

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
