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

    // ===== Emmentaler's character for a choice MusicGlyphs makes =====
    // The dispatch itself is font-independent and lives in MusicGlyphs (第858). These answer
    // the EMMENTALER character for it — what the layout's Emmentaler-keyed tables are looked
    // up by until each family moves onto MusicFont (docs/smufl-design.md §6 ①). A draw site
    // asks MusicFont.Current instead.

    /// <summary>Emmentaler's accidental for a resolved kind — <see cref="MusicGlyphs.Accidental"/>.</summary>
    public static char AccidentalGlyph(string? kind) => Char(MusicGlyphs.Accidental(kind));

    /// <summary>Emmentaler's time signature digit — <see cref="MusicGlyphs.TimeSigDigit(int)"/>.</summary>
    public static char GetTimeSigDigit(int digit) => Char(MusicGlyphs.TimeSigDigit(digit));

    /// <summary>Emmentaler's rest — <see cref="MusicGlyphs.Rest"/>.</summary>
    public static char GetRest(int noteValue, double staffPosition, int staffLines = 5)
        => Char(MusicGlyphs.Rest(noteValue, staffPosition, staffLines));

    /// <summary>Emmentaler's notehead for a style — <see cref="MusicGlyphs.Notehead(Model.NoteheadStyle, int)"/>.</summary>
    public static char GetNotehead(Model.NoteheadStyle style, int noteValue)
        => Char(MusicGlyphs.Notehead(style, noteValue));

    /// <summary>Emmentaler's plain notehead — <see cref="MusicGlyphs.Notehead(int)"/>.</summary>
    public static char GetNotehead(int noteValue) => Char(MusicGlyphs.Notehead(noteValue));

    /// <summary>Emmentaler's flag — <see cref="MusicGlyphs.Flag"/>.</summary>
    public static char? GetFlag(int noteValue, bool stemUp)
        => MusicGlyphs.Flag(noteValue, stemUp) is { } g ? Char(g) : null;

    private static char Char(MusicGlyph glyph) => MusicGlyphs.Of(glyph).EmmentalerCode;
}
