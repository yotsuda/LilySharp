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

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// One measurable quantity, expressed on the Lily# side. Its LilyPond counterpart lives in
/// audit/lp-geometry/lp-geometry.json under the same <see cref="Id"/>.
/// </summary>
/// <param name="Id">Ledger key. Must exist in lp-geometry.json.</param>
/// <param name="Source">The .lys probe. Kept inline so the score being measured is readable
/// next to the measurement, and so it cannot drift from a separate fixture file.</param>
/// <param name="Measure">Extracts the quantity from the rendered geometry.</param>
internal sealed record LpProbe(string Id, string Source, Func<RenderedGeometry, double> Measure);

/// <summary>
/// The Lily# half of the LP fidelity corpus.
/// </summary>
/// <remarks>
/// <para>
/// Each probe here has a twin in audit/lp-geometry/probes/*.ly written to engrave the SAME
/// music, so the two sides measure the same thing. Lily# and LilyPond spell octaves
/// differently — Lily# `c` is LilyPond `c'` — which is exactly the sort of mismatch that
/// silently invalidates a comparison, so every probe below names its LilyPond twin.
/// </para>
/// <para>
/// Probes are ONE SYSTEM long on purpose. A line break would change which bar line index a
/// measurement lands on, turning a spacing regression into a confusing index error.
/// </para>
/// </remarks>
internal static class LpGeometryProbes
{
    private const string Preamble = """
        octave absolute
        time 4/4
        key c major

        part melody

        """;

    private static string Score(string music, string name) => Preamble + $$"""
        section Main {
          melody { {{music}} }
        }

        form main { Main }

        score main "{{name}}" {
          staff melody
        }
        """;

    // LilyPond twin: c'4 d' e' f' | g'4 a' b' c''      (up stems after the bar line)
    private static readonly string A = Score("c4 d e f | g a b c' |", "A");

    // LilyPond twin: c'4 d' e' f' \clef bass g4 a b c'  (down stems, clef at the bar line)
    private static readonly string B = Score("c4 d e f | clef bass g, a, b, c |", "B");

    // LilyPond twin: c'4 d' e' f' | a''4 b'' c''' d'''  (down stems, NO clef)
    private static readonly string C = Score("c4 d e f | a' b' c'' d'' |", "C");

    // LilyPond twin: c'4 d' e' f' \clef bass c,4 d, e, f,  (up stems, clef at the bar line)
    private static readonly string D = Score("c4 d e f | clef bass c,, d,, e,, f,, |", "D");

    // LilyPond twin: c'1 | c'1
    private static readonly string E = Score("c1 | c1 |", "E");

    // LilyPond twin: r1 | r1
    private static readonly string F = Score("r1 | r1 |", "F");

    // LilyPond twin: c'2 c'2 | c'2 c'2
    private static readonly string G = Score("c2 c2 | c2 c2 |", "G");

    // LilyPond twin: c'4 d' e' f' | cis'4 d' e' f'     (accidental opening the measure)
    private static readonly string X = Score("c4 d e f | cis d e f |", "X");

    // LilyPond twin: c'4 d' e' f' \key a \major c'4 d' e' f'
    private static readonly string K = Score("c4 d e f | key a major c4 d e f |", "K");

    // LilyPond twin: c'4 d' e' f' \time 3/4 c'4 d' e'
    private static readonly string T = Score("c4 d e f | time 3/4 c4 d e |", "T");

    // --- mid-measure changes: the case COORDINATE_AUDIT 4.7 item 1 governs ---
    // These are NOT break-aligned; LilyPond gives the change its own musical column between
    // two notes, so they are measured note-to-glyph rather than from a bar line.

    // LilyPond twin: c'4 d' \clef bass e4 f4   — ONE 4/4 measure, change in the middle.
    private static readonly string MC = Score("c4 d clef bass e, f, |", "MC");

    // LilyPond twin: c'4 d' \key a \major e'4 f'4   — likewise one measure.
    private static readonly string MK = Score("c4 d key a major e f |", "MK");

    /// <summary>
    /// Every probe is two measures, so thin bar line 0 is the MID-LINE one between them —
    /// Lily# draws none at a system start. That is the bar line
    /// <c>Staff_spacing::get_spacing</c> governs; a system start is break-align spacing and
    /// a different code path entirely (BreakAlignSpacing.FirstNoteSpring), which is why
    /// every probe measures the second measure's opening rather than the first's.
    /// </summary>
    private const int MidLineBarline = 0;

    public static IReadOnlyList<LpProbe> All { get; } = new List<LpProbe>
    {
        // --- bar line -> the column after it (Staff_spacing::get_spacing) ---
        new("barline.next.up-stems", A, g => g.BarlineRightToNextGlyph(MidLineBarline)),
        new("barline.next.up-stems-after-clef", D, g => g.BarlineRightToNextGlyph(MidLineBarline)),
        new("barline.next.down-stems", C, g => g.BarlineRightToNextGlyph(MidLineBarline)),
        new("barline.next.down-stems-after-clef", B, g => g.BarlineRightToNextGlyph(MidLineBarline)),
        new("barline.next.full-measure-note", E, g => g.BarlineRightToNextGlyph(MidLineBarline)),
        new("barline.next.half-notes", G, g => g.BarlineRightToNextGlyph(MidLineBarline)),

        // The accidental is the first glyph after the bar line; the notehead is the second.
        // Recording BOTH is the point: it splits "this measure start is wrong" into the
        // bar-line side and the accidental-to-head side, which have different owners.
        new("barline.next.accidental", X, g => g.BarlineRightToNextGlyph(MidLineBarline)),
        new("barline.next.accidental-to-notehead", X, g => g.BarlineRightToNextNotehead(MidLineBarline)),

        // The first glyph after the bar line is the key/time signature; the note is found by
        // IDENTITY, not by counting past it — Lily# draws one glyph per key accidental while
        // LilyPond dumps the signature as a single grob, so glyph indices do not correspond
        // between the two sides.
        new("barline.next.key-change-glyph", K, g => g.BarlineRightToNextGlyph(MidLineBarline)),
        new("barline.next.key-change-to-notehead", K, g => g.BarlineRightToNextNotehead(MidLineBarline)),
        new("barline.next.time-change-glyph", T, g => g.BarlineRightToNextGlyph(MidLineBarline)),
        new("barline.next.time-change-to-notehead", T, g => g.BarlineRightToNextNotehead(MidLineBarline)),

        // --- mid-measure change items (COORDINATE_AUDIT 4.7 item 1) ---
        // Notehead 1 is the note BEFORE the change, notehead 2 the note after it. Measuring
        // BOTH sides of the change glyph is the point: the change's own frame shows up as
        // the two gaps trading against each other, which a single gap would hide.
        new("midmeasure.clef.prev-note-to-clef", MC,
            g => g.FirstNonNoteheadAfter(g.NoteheadAnchor(1)) - g.NoteheadAnchor(1)),
        new("midmeasure.clef.clef-to-next-note", MC,
            g => g.NoteheadAnchor(2) - g.FirstNonNoteheadAfter(g.NoteheadAnchor(1))),
        new("midmeasure.key.prev-note-to-key", MK,
            g => g.FirstNonNoteheadAfter(g.NoteheadAnchor(1)) - g.NoteheadAnchor(1)),
        new("midmeasure.key.key-to-next-note", MK,
            g => g.NoteheadAnchor(2) - g.FirstNonNoteheadAfter(g.NoteheadAnchor(1))),

        // --- the column before a bar line -> that bar line (the closing side) ---
        // Section 3.3 of the working notes: a grob's position is fixed by BOTH gaps, so a
        // corpus that only measures one side can be fully green while the other side rots.
        new("barline.prev.whole-note", E, g => g.LastGlyphToBarlineLeft(MidLineBarline)),
        new("barline.prev.whole-rest", F, g => g.LastGlyphToBarlineLeft(MidLineBarline)),
        new("barline.prev.half-note", G, g => g.LastGlyphToBarlineLeft(MidLineBarline)),
    };
}
