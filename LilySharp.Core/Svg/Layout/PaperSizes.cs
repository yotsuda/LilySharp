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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The paper-size names a <c>size "NAME"</c> entry accepts, with their dimensions in
/// millimetres — LilyPond's table, transcribed whole, plus one Lily#-own entry.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/paper.scm:155-266 <c>documented-paper-alist</c> — every name and
/// every dimension below is that table's, in its order (inch entries multiplied out at
/// 25.4 mm). Transcribed whole rather than curated: a subset would make "which sizes
/// exist" a judgment this file re-makes every time someone asks for one more.
/// <para>
/// ⚠️ <c>jisb5</c> (182 × 257) is LILY#-OWN, not LilyPond's: ISO 216's <c>b5</c>
/// (176 × 250) is NOT the Japanese B5 — JIS P 0138 defines its own B series — and
/// Japanese sheet music is commonly printed on JIS B5. Added 2026-08-23 (user
/// decision); LilyPond has no JIS entries to transcribe.
/// </para>
/// </remarks>
internal static class PaperSizes
{
    private const double MmPerInch = 25.4;

    /// <summary>(name, width mm, height mm) — LilyPond's table order, jisb5 appended
    /// beside its ISO neighbour.</summary>
    private static readonly (string Name, double WidthMm, double HeightMm)[] Table =
    [
        // ISO 216, A series
        ("a10", 26, 37), ("a9", 37, 52), ("a8", 52, 74), ("a7", 74, 105),
        ("a6", 105, 148), ("a5", 148, 210), ("a4", 210, 297), ("a3", 297, 420),
        ("a2", 420, 594), ("a1", 594, 841), ("a0", 841, 1189),
        // Two extended sizes as defined in DIN 476
        ("2a0", 1189, 1682), ("4a0", 1682, 2378),
        // ISO 216, B series
        ("b10", 31, 44), ("b9", 44, 62), ("b8", 62, 88), ("b7", 88, 125),
        ("b6", 125, 176), ("b5", 176, 250),
        // ⚠️ Lily#-own (see the class remark): the Japanese B5, beside the ISO one.
        ("jisb5", 182, 257),
        ("b4", 250, 353), ("b3", 353, 500),
        ("b2", 500, 707), ("b1", 707, 1000), ("b0", 1000, 1414),
        // ISO 269, C series
        ("c10", 28, 40), ("c9", 40, 57), ("c8", 57, 81), ("c7", 81, 114),
        ("c6", 114, 162), ("c5", 162, 229), ("c4", 229, 324), ("c3", 324, 458),
        ("c2", 458, 648), ("c1", 648, 917), ("c0", 917, 1297),
        // North American paper sizes
        ("junior-legal", 5.0 * MmPerInch, 8.0 * MmPerInch),
        ("legal", 8.5 * MmPerInch, 14.0 * MmPerInch),
        ("ledger", 17.0 * MmPerInch, 11.0 * MmPerInch),
        ("17x11", 17.0 * MmPerInch, 11.0 * MmPerInch),
        ("letter", 8.5 * MmPerInch, 11.0 * MmPerInch),
        ("tabloid", 11.0 * MmPerInch, 17.0 * MmPerInch),
        ("11x17", 11.0 * MmPerInch, 17.0 * MmPerInch),
        // Sizes by IEEE Printer Working Group, for children's writing
        ("government-letter", 8 * MmPerInch, 10.5 * MmPerInch),
        ("government-legal", 8.5 * MmPerInch, 13.0 * MmPerInch),
        ("philippine-legal", 8.5 * MmPerInch, 13.0 * MmPerInch),
        // ANSI sizes
        ("ansi a", 8.5 * MmPerInch, 11.0 * MmPerInch),
        ("ansi b", 11.0 * MmPerInch, 17.0 * MmPerInch),
        ("ansi c", 17.0 * MmPerInch, 22.0 * MmPerInch),
        ("ansi d", 22.0 * MmPerInch, 34.0 * MmPerInch),
        ("ansi e", 34.0 * MmPerInch, 44.0 * MmPerInch),
        ("engineering f", 28.0 * MmPerInch, 40.0 * MmPerInch),
        // North American architectural sizes
        ("arch a", 9.0 * MmPerInch, 12.0 * MmPerInch),
        ("arch b", 12.0 * MmPerInch, 18.0 * MmPerInch),
        ("arch c", 18.0 * MmPerInch, 24.0 * MmPerInch),
        ("arch d", 24.0 * MmPerInch, 36.0 * MmPerInch),
        ("arch e", 36.0 * MmPerInch, 48.0 * MmPerInch),
        ("arch e1", 30.0 * MmPerInch, 42.0 * MmPerInch),
        // Other sizes, including antique sizes still used in the United Kingdom
        ("statement", 5.5 * MmPerInch, 8.5 * MmPerInch),
        ("half letter", 5.5 * MmPerInch, 8.5 * MmPerInch),
        ("quarto", 8.0 * MmPerInch, 10.0 * MmPerInch),
        ("octavo", 6.75 * MmPerInch, 10.5 * MmPerInch),
        ("executive", 7.25 * MmPerInch, 10.5 * MmPerInch),
        ("monarch", 7.25 * MmPerInch, 10.5 * MmPerInch),
        ("foolscap", 8.27 * MmPerInch, 13.0 * MmPerInch),
        ("folio", 8.27 * MmPerInch, 13.0 * MmPerInch),
        ("super-b", 13.0 * MmPerInch, 19.0 * MmPerInch),
        ("post", 15.5 * MmPerInch, 19.5 * MmPerInch),
        ("crown", 15.0 * MmPerInch, 20.0 * MmPerInch),
        ("large post", 16.5 * MmPerInch, 21.0 * MmPerInch),
        ("demy", 17.5 * MmPerInch, 22.5 * MmPerInch),
        ("medium", 18.0 * MmPerInch, 23.0 * MmPerInch),
        ("broadsheet", 18.0 * MmPerInch, 24.0 * MmPerInch),
        ("royal", 20.0 * MmPerInch, 25.0 * MmPerInch),
        ("elephant", 23.0 * MmPerInch, 28.0 * MmPerInch),
        ("double demy", 22.5 * MmPerInch, 35.0 * MmPerInch),
        ("quad demy", 35.0 * MmPerInch, 45.0 * MmPerInch),
        ("atlas", 26.0 * MmPerInch, 34.0 * MmPerInch),
        ("imperial", 22.0 * MmPerInch, 30.0 * MmPerInch),
        ("antiquarian", 31.0 * MmPerInch, 53.0 * MmPerInch),
        // PA4-based sizes
        ("pa10", 26, 35), ("pa9", 35, 52), ("pa8", 52, 70), ("pa7", 70, 105),
        ("pa6", 105, 140), ("pa5", 140, 210), ("pa4", 210, 280), ("pa3", 280, 420),
        ("pa2", 420, 560), ("pa1", 560, 840), ("pa0", 840, 1120),
        // Additional format for use in Southeast Asia and Australia
        ("f4", 210, 330),
    ];

    /// <summary>Looks a name up, case-insensitively — the paper block's keys read the
    /// same way. LilyPond's own lookup is exact lowercase; every table name IS
    /// lowercase, so the relaxation admits spellings and never changes a meaning.</summary>
    internal static bool TryGet(string name, out double widthMm, out double heightMm)
    {
        foreach (var (candidate, w, h) in Table)
        {
            if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                widthMm = w;
                heightMm = h;
                return true;
            }
        }
        widthMm = 0;
        heightMm = 0;
        return false;
    }

    /// <summary>Every name, in table (LilyPond) order — for messages and completion.</summary>
    internal static IReadOnlyList<string> AllNames() => [.. Table.Select(t => t.Name)];
}
