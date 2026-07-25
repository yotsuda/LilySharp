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

using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// One measurable quantity, expressed on the Lily# side. Its LilyPond counterpart lives in
/// audit/lp-geometry/lp-geometry.json under the same <see cref="Id"/>.
/// </summary>
/// <param name="Id">Ledger key. Must exist in lp-geometry.json.</param>
/// <param name="Source">The .lys probe. Kept inline so the score being measured is readable
/// next to the measurement, and so it cannot drift from a separate fixture file.</param>
/// <param name="Measure">Extracts the quantity from the rendered geometry.</param>
/// <param name="Options">Paper to engrave onto; null uses the product default. Only probes
/// that must reach a paper regime the default page never enters set this — see
/// <see cref="RenderedGeometry.Render"/> for why the paper is a harness parameter and not
/// something the .lys source can say.</param>
internal sealed record LpProbe(
    string Id, string Source, Func<RenderedGeometry, double> Measure,
    LayoutOptions? Options = null);

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
    private static string Preamble(string key) => $"""
        octave absolute
        time 4/4
        key {key}

        part melody


        """;

    private static string Score(string music, string name, string key = "c major") =>
        Preamble(key) + $$"""
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

    // LilyPond twin: \key d \major c'1  (a single note carrying a NATURAL, cancelling the key's
    // C#). Measures the single-note accidental DRAW gap: the natural's real right skyline clears
    // the head at 0.367672, not the fixed AccidentalNoteGap 0.35, so HEAD - ACC anchor is 1.034272.
    private static readonly string NAT = Score("c1 |", "NAT", "d major");

    // LilyPond twin: \key c \major ces'1  (a single note carrying a FLAT). The flat's ink starts
    // 0.12 LEFT of its origin, so the fixed-gap draw over-counted the overhang and placed it at
    // gap 0.47 instead of LilyPond's 0.35; HEAD - ACC anchor is 1.150000.
    private static readonly string FLAT = Score("ces1 |", "FLAT", "c major");

    // --- CHORD accidental stacking (Accidental_placement::calc_positioning_done) ---
    // Score X measures a SINGLE accidental; these four carry a two-note cluster (a written
    // third) whose accidental glyphs OVERLAP vertically and are forced into TWO columns, so
    // the measured quantity is the ACC-to-ACC column gap (ChordAccidentalColumnGap). A third
    // does not reverse a head across the stem, so the heads share one column and the heads
    // skyline is clean; the trailing a/b/c'' never repeat the chord's altered letters, so no
    // cancellation natural is engraved as a third accidental. The pairs are vertical MIRRORS
    // (below the middle line, stems up; above it, stems down): the accidentals sit left of the
    // note column against the heads' left skyline regardless of stem direction, so each pair
    // must print the SAME gap and a difference is a direction-dependence defect of its own —
    // the P/Q relationship.

    // LilyPond twin: c'4 d' e' f' | <dis' fis'>4 a' b' c''   (D#4/F#4, stems up)
    private static readonly string CSB = Score("c4 d e f | <dis fis>4 a b c' |", "CSB");
    // LilyPond twin: c'4 d' e' f' | <eis'' gis''>4 a' b' c''  (E#5/G#5, stems down) — mirror of CSB
    private static readonly string CSA = Score("c4 d e f | <eis' gis'>4 a b c' |", "CSA");
    // LilyPond twin: c'4 d' e' f' | <ees' ges'>4 a' b' c''    (Eb4/Gb4, stems up) — the flat-merge site
    private static readonly string CFB = Score("c4 d e f | <ees ges>4 a b c' |", "CFB");
    // LilyPond twin: c'4 d' e' f' | <des'' fes''>4 a' b' c''  (Db5/Fb5, stems down) — mirror of CFB
    private static readonly string CFA = Score("c4 d e f | <des' fes'>4 a b c' |", "CFA");

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

    // LilyPond twin: \key g \major c'4 d' \key c \major fis'4 g'4
    // The note after the change carries an accidental, so the musical column's leftmost ink
    // is that accidental rather than a note head. Two branches only this probe reaches:
    // the change column is narrow enough that Note_spacing takes its SUBTRACTION arm (MC and
    // MK take the floor), and the accidental drags the rod above the space-alist ideal so
    // Staff_spacing's :213 correction decides the right-hand gap.
    private static readonly string MKA = Score("c4 d key c major fis g |", "MKA", "g major");

    // --- the PAGE vertical (probes/page-vertical.ly) ---
    //
    // LilyPond twin: book L — \repeat unfold 24 { c'4 d' e' f' } on the SHIPPING DEFAULT
    // paper, with no \header, no title and no markup. Every one of those absences is load
    // bearing:
    //
    //   * No markup, so the first thing on the page is a SYSTEM. scm/page.scm:67-87 chooses
    //     between top-system-spacing and top-markup-spacing on paper-system-title?, and a
    //     title would silently move this measurement onto the other spring. An earlier
    //     attempt at this comparison carried a `section` mark on the Lily# side and nowhere
    //     else, which put ~3.2 ss of header into a number that was being read as margin.
    //
    //   * Short enough to fit one page, so the page is also the LAST page and no stretching
    //     happens. That is what makes the gap the spring's own natural length rather than
    //     whatever force a full page was solved for. Book J in the same .ly measures the
    //     stretched regime and is deliberately NOT mirrored here yet — mixing the two is
    //     the mistake HANDOFF 5.3 exists to prevent.
    //
    // 24 measures land on 3 systems on both sides, which is what makes the gap measurable
    // at all (two gaps, and StaffGap insists they agree).
    //
    // NOTE THE `~`. Every other probe goes through Score(), whose `form main { Main }` prints
    // the section's rehearsal MARK above the first system. That mark is ~3.86 ss of ink, it
    // lands exactly where this measurement looks, and Lily# seats the first system by
    // skyline — so the first draft of this probe read 14.350551 against LilyPond's 11.690551
    // and the mark was the whole of the difference. That is precisely the confound the .ly
    // twin's header warns about ("a `section` mark on the Lily# side with no counterpart
    // here"), met again from the other direction. `~Main` is a silent section reference
    // (Parser.Form.cs): the section still governs the music, its label is not drawn.
    //
    // Writing the music inline as `part melody { ... }` also removes the mark, and was tried
    // first — but it renders NOTHING AT ALL (no staff lines, no glyphs) while still exiting
    // 0, so "the mark is gone" and "the score is gone" look identical from the outside. Any
    // probe shape has to be checked for ink before it is trusted.
    private static readonly string V = $$"""
        octave absolute
        time 4/4
        key c major

        part melody

        section Main {
          melody { {{string.Concat(Enumerable.Repeat("c4 d e f | ", 24)).Trim()}} }
        }

        form main { ~Main }

        score main "V" {
          staff melody
        }
        """;

    /// <summary>
    /// The STRETCHED twin of <see cref="V"/> — the mirror of book J in page-vertical.ly.
    /// </summary>
    /// <remarks>
    /// Same music, 150 measures instead of 24, so the first page FILLS and its springs are
    /// solved to the breaker's force rather than sitting at their natural length. Nothing
    /// else differs, which is the point: V and W measure the same two quantities in the two
    /// regimes HANDOFF 5.3 insists on keeping apart, and a change that moves one and not the
    /// other is telling you which regime it belongs to.
    ///
    /// 150 was chosen because it is what the .ly twin uses; on LilyPond 2.26.0 it lands 13
    /// systems on page 1 and 8 on page 2, so page 1 is genuinely full (its last page is a
    /// separate, ragged-last regime and is NOT what these entries read).
    /// </remarks>
    private static readonly string W = $$"""
        octave absolute
        time 4/4
        key c major

        part melody

        section Main {
          melody { {{string.Concat(Enumerable.Repeat("c4 d e f | ", 150)).Trim()}} }
        }

        form main { ~Main }

        score main "W" {
          staff melody
        }
        """;

    /// <summary>
    /// The CLEF-bounded twin of <see cref="W"/> — the mirror of book S in page-vertical.ly.
    /// </summary>
    /// <remarks>
    /// Same shape as W, but the note is chosen so that the deepest ink on every system is
    /// the CLEF and nothing else. `a` sits one step BELOW the middle line, which is what
    /// makes its stem point UP: the head reaches 1.045 below the middle, the stem goes the
    /// other way, and the staff's own bottom line at 2.0 is all that is left under it.
    /// LilyPond's clef reaches 3.540, so it decides the extent by a wide margin.
    ///
    /// Do NOT write this on the middle line. `b` looks like the natural choice and is a
    /// trap: a note ON the middle line takes a DOWN stem, which reaches 3.5 below it and
    /// shadows the clef's 3.540 to within 0.04. Measured that way first, and the seeded
    /// prediction missed because of it.
    ///
    /// W cannot catch a missing clef: there a c' notehead reaches 3.545, five thousandths
    /// past the clef, so the number comes out right for the wrong reason. Confirmed by
    /// measuring book S on 2.26.0 three ways — these notes, notes on the middle line, and
    /// a bar of rests down to a 128th (glyph bottom 3.05 below the middle) — all giving
    /// the identical 12.255229 and 11.716074, which is only possible if a grob none of
    /// them contains is what sets them.
    /// </remarks>
    private static readonly string S = $$"""
        octave absolute
        time 4/4
        key c major

        part melody

        section Main {
          melody { {{string.Concat(Enumerable.Repeat("a4 a a a | ", 150)).Trim()}} }
        }

        form main { ~Main }

        score main "S" {
          staff melody
        }
        """;

    /// <summary>
    /// TIGHT PAPER — the mirror of book T in page-vertical.ly. The page BREAKER's own
    /// quantity: how many systems it puts on a page, and how many pages that takes.
    /// </summary>
    /// <remarks>
    /// 40 bars is six systems at this line width. The paper is shrunk (see
    /// <see cref="TightPaper"/>) because on the default page the count is not decided by
    /// capacity at all: measured on 2.26.0, raising book J's first system by up to four
    /// octaves leaves page 1 at 13 systems every time. The breaker picks the count from the
    /// force each candidate page solves to, so only a page small enough for that force to
    /// matter can see its arithmetic.
    /// </remarks>
    private static readonly string TP = $$"""
        octave absolute
        time 4/4
        key c major

        part melody

        section Main {
          melody { {{string.Concat(Enumerable.Repeat("c4 d e f | ", 40)).Trim()}} }
        }

        form main { ~Main }

        score main "TP" {
          staff melody
        }
        """;

    /// <summary>
    /// 70 staff spaces tall, everything else the product default — the paper book T engraves
    /// onto (123.0109mm at the default 20pt staff).
    /// </summary>
    /// <remarks>
    /// Chosen to sit clear of BOTH sides' boundaries, which is what makes the reading a
    /// property of the model rather than of a rounding. Measured on 2.26.0, LilyPond splits
    /// this score 5 + 1 across two pages for every height up to 75 and Lily# up to 76, so
    /// 70 is five or six staff spaces inside both plateaus.
    /// <para>
    /// 72 was tried first and rejected: after the three breaker ports, Lily# flips to one
    /// page at exactly 72, so the entry would have swung between residual -1 and 0 on
    /// changes that had nothing to do with the model.
    /// </para>
    /// <para>
    /// ⚠️ Do not raise it looking for a sharper reading. Above 75 the two sides stop
    /// measuring the same thing: at 76 and 77 LilyPond does not fit six systems onto one
    /// page, it RE-BREAKS the music into five systems and puts those on one page.
    /// LILYPOND-REF: lily/optimal-page-breaking.cc:139-173 — Optimal_page_breaking::solve
    /// sweeps sys_count downward from the line breaker's ideal, spaces every line-division
    /// configuration at each count, and keeps the global argmin of demerits, so the PAGE
    /// breaker chooses the LINE breaking. Lily# breaks lines once and pages afterwards, so
    /// no amount of spacing accuracy reaches that answer.
    /// </para>
    /// Set here rather than in the .lys source because paper-height is a LilyPond
    /// <c>\paper</c> variable, not a grob property — see
    /// <see cref="RenderedGeometry.Render"/>.
    /// </remarks>
    private static readonly LayoutOptions TightPaper =
        LayoutOptions.Default with { PageHeight = 70.0 };

    /// <summary>
    /// LilyPond's DEFAULT first-system indent, for the books that do not zero it.
    /// </summary>
    /// <remarks>
    /// Lily# indents the first system only when instrument names ask for it
    /// (LayoutEngine.CalculateIndentFromInstrumentNames returns 0 with no names), so a probe
    /// whose LilyPond book leaves `indent` alone must be told, or the two sides engrave
    /// different pages and every quantity read off them is a comparison of two layouts
    /// rather than of one.
    /// <para>
    /// ⚠️ MEASURED 2026-07-25 (jn-line-forces.ly, TPT vs TPD): forty bars at the tight
    /// paper give FIVE systems of eight bars at indent 0 and SIX cut 6,7,7,7,7,6 at the
    /// default indent. For books TSD/TSU the indent is not cosmetic at all — it is what puts
    /// their six bars on TWO systems, which is the regime those points measure, so there the
    /// LilyPond book keeps its indent and THIS is what makes the pair comparable.
    /// </para>
    /// LILYPOND-REF: ly/paper-defaults-init.ly — indent = 15\mm; the conversion is the
    ///   corpus's own output-scale (A4's 210mm is LayoutOptions.Default's 119.501575, i.e.
    ///   1.757299017 mm per staff space, the same one the 10mm margins use).
    /// </remarks>
    private static readonly LayoutOptions IndentedPaper =
        LayoutOptions.Default with { Indent = 15.0 / 1.757299017 };

    /// <summary>
    /// A line too narrow for its bar, so every note-to-note spring is driven onto its
    /// MINIMUM — the one regime in which that minimum is observable at all.
    /// </summary>
    /// <remarks>
    /// The width mirrors probe CN4 in compressed-note-spacing.ly (30mm = 17.071654 staff
    /// spaces at the corpus's output-scale), chosen INSIDE LilyPond's saturation plateau
    /// rather than at its edge: measured 2026-07-25, LilyPond's gap for this bar falls
    /// 3.048125 -> 2.037936 as the line narrows and then stops dead at 1.956300 for every
    /// width from 19.916929 down to 12.519213. Sitting at 30mm keeps the entry reading the
    /// saturated value and not a rounding at the flip point, the same discipline
    /// page.tight.* uses for its paper height.
    /// <para>
    /// ContentWidth is PageWidth minus the two margins, so the page is widened by exactly
    /// the margins to leave LilyPond's line-width behind.
    /// </para>
    /// </remarks>
    private static readonly LayoutOptions NarrowPaper =
        LayoutOptions.Default with { PageWidth = 30.0 / 1.757299017 + 2 * 8.535827 };

    /// <summary>
    /// EIGHT QUARTERS IN ONE BAR, to be squeezed. The mirror of compressed-note-spacing.ly.
    /// </summary>
    /// <remarks>
    /// One bar so no bar-line spring sits between the measured heads, and eight of them so
    /// the line has enough springs that the line start cannot absorb the compression on its
    /// own (it does take some: LilyPond's first head moves 8.489735 -> 7.489735 across the
    /// widths). Same pitch throughout, so the gap is the plain head-to-head minimum with no
    /// accidental ink in it — the quantity probe N2N reads at force 0.
    /// <para>
    /// ⚠️ <c>g</c> (LilyPond <c>g'</c>), NOT <c>c</c>. The first cut of this pair used
    /// middle C, which in a treble staff carries a LEDGER LINE — and a ledger sticks out
    /// past the head on both sides and joins the column's horizontal skyline like any other
    /// ink, so LilyPond's rod read 1.956300 instead of 1.604200 and the point was measuring
    /// the unported ledger geometry (handoff section 2E) as well as the minimum. g sits on a
    /// staff line inside the staff, stem up like c, and has no ledger.
    /// </para>
    /// </remarks>
    private static readonly string CN = """
        octave absolute
        time 8/4
        key c major

        part melody

        section Main {
          melody { g4 g g g g g g g | }
        }

        form main { Main }

        score main "CN" { staff melody }
        """;

    /// <summary>
    /// Sixteen bars of <paramref name="bar"/> cut into four systems of four by an explicit
    /// break, mirroring the LilyPond books that write
    /// <c>\repeat unfold 4 { … } \break</c> four times over.
    /// </summary>
    /// <remarks>
    /// The four inter-system bow probes (SSD/SSU/TSID/TSIU, page-vertical.ly:653-726) force
    /// that split ON PURPOSE — a bow's arc is span-dependent, so the pair only holds while
    /// both systems are spacing-identical, and LilyPond's own natural break is uneven. Their
    /// Lily# sources carried NO break and let the breaker choose, so the two sides agreed
    /// only while Lily#'s breaker happened to land on LilyPond's forced division.
    /// ⚠️ MEASURED 2026-07-25: freed of the invented OverfullPenalty, Lily# cuts this music
    /// into THREE systems (5,5,6) — and so does LilyPond when its \break is removed
    /// (jn-line-forces.ly, score SSN). Both engravers are right; the PAIR was mis-specified.
    /// </remarks>
    private static string ForcedFourBarSystems(string bar) =>
        string.Join(" break ",
            Enumerable.Repeat(string.Concat(Enumerable.Repeat(bar, 4)).Trim(), 4));

    /// <summary>
    /// TWO STAVES, one system — the mirror of book P in page-vertical.ly.
    /// </summary>
    /// <remarks>
    /// Everything above measures the PAGE. This pair measures what Align_interface decides:
    /// how far apart two staves of one system sit, which is
    /// <c>max(skyline-distance + padding, minimum-distance, basic-distance)</c> over
    /// StaffGrouper's 9 / 7 / 1 (align-interface.cc:228-238, define-grobs.scm:3352-3355).
    ///
    /// The staff LINES join that skyline like any other ink, and this probe is shaped so
    /// they are the BINDING side: <c>d,</c> (LilyPond <c>d</c>) hangs 6 staff spaces below
    /// the treble staff's middle line, its head reaching 0.545 further, while the same
    /// written pitch is the bass staff's own middle line — so at that x nothing on the
    /// lower staff rises above its top line. 6.545 + 2.05 + 1 = 9.595 beats basic-distance
    /// 9, and the 2.05 is the line's INK: half of its 0.1 thickness past the centre at 2.0.
    ///
    /// Why the shape matters: with nothing protruding, both sides of the gap are staff
    /// lines and 2.05 + 2.05 + 1 = 5.1 loses to basic-distance 9. A plain two-staff score
    /// therefore cannot see the staff symbol's extent AT ALL — measured 2026-07-22, moving
    /// it to 2.05 changed 14 multi-staff fixtures and not one ledger point, which is why
    /// the fix was reverted until this entry existed.
    ///
    /// LilyPond twin: \new PianoStaff &lt;&lt; \new Staff { \clef treble d1 }
    /// \new Staff { \clef bass d1 } &gt;&gt;.
    /// </remarks>
    private static readonly string P = """
        octave absolute
        time 4/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { d,1 | }
          lh { d,1 | }
        }

        form main { ~Main }

        score main "P" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// A WHOLE NOTE with a FORCED-DOWN stem carrying a dynamic — the mirror of book D.
    /// Measures the staff-to-staff distance again, but shaped to reach one specific site.
    /// </summary>
    /// <remarks>
    /// <c>DynamicEngraver.GetLowestExtent</c> subtracts <c>DefaultStemLength</c> from any
    /// down-stemmed note without checking its duration, so a whole note — which has no stem
    /// — reserves one. It is the same defect <c>89aaa29f</c> removed from
    /// <c>SkylineBuilder</c>, which branches on
    /// <c>GetNoteValueFromFraction(...) &gt;= 2</c> citing <c>Stem::is_normal_stem</c>. This
    /// entry is the ledger point that site never had.
    /// <para>
    /// Why two voices, and why the obvious probe cannot work: the defect fires only on a
    /// DOWN stem, which under the default direction rule means a notehead at or above the
    /// middle line — far too shallow for a dynamic beneath it to beat StaffGrouper's
    /// basic-distance of 9, so the gap would rest on that floor and measure nothing at all.
    /// Reaching past 9 needs a LOW notehead, which takes an UP stem and does not fire. The
    /// two requirements are contradictory under the default rule; a second voice
    /// (LilyPond's <c>\voiceTwo</c>) forces the direction independently of the pitch and
    /// dissolves the contradiction. Voice one holds the middle line so the staff is an
    /// ordinary two-voice texture rather than a lone forced note.
    /// </para>
    /// LilyPond twin: <c>\new PianoStaff &lt;&lt; \new Staff &lt;&lt; { \voiceOne b'1 } \\
    /// { \voiceTwo a1\f } &gt;&gt; \new Staff { \clef bass d1 } &gt;&gt;</c>.
    /// </remarks>
    private static readonly string DY = """
        octave absolute
        time 4/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { voice { b1 } voice { a,1@f } | }
          lh { d,1 | }
        }

        form main { ~Main }

        score main "DY" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// <see cref="P"/> with the protrusion on the other side — the mirror of book Q.
    /// </summary>
    /// <remarks>
    /// Not redundant with <see cref="P"/>. P binds the LOWER staff's TOP line against ink
    /// coming down; Q binds the UPPER staff's BOTTOM line against ink going up. Two edges
    /// of the staff symbol, reached through two different skylines — the place a sign or a
    /// frame goes wrong with nothing else noticing. <c>b</c> (LilyPond <c>b'</c>) is the
    /// treble staff's middle line and sits 6 spaces ABOVE the bass staff's, so the
    /// arithmetic mirrors P and both must read 9.595000. A difference between them is a
    /// defect in its own right, whatever the value.
    /// </remarks>
    private static readonly string Q = """
        octave absolute
        time 4/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { b1 | }
          lh { b1 | }
        }

        form main { ~Main }

        score main "Q" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// A TUPLET BRACKET over STEMLESS whole notes, reaching UP into the staff gap from the
    /// lower staff — the mirror of book TU. Measures the staff-to-staff distance again,
    /// shaped to reach the one site the corpus has never touched.
    /// </summary>
    /// <remarks>
    /// TWO divergences live here and they push the gap in OPPOSITE directions, so neither
    /// can be judged without the other.
    /// <para>
    /// (1) NOTHING RESERVES THE BRACKET. <c>SkylineBuilder</c> does not know the word
    /// "tuplet": neither <c>MultiStaffLayouter.BuildAllStaffSkylines</c> (the staff gap) nor
    /// <c>LayoutEngine.AugmentSkylinesForPaging</c> (the page) seeds a
    /// <c>TupletBracketLayout</c>. LilyPond's TupletBracket is an ordinary inside-staff grob
    /// of the VerticalAxisGroup — <c>scm/define-grobs.scm</c> gives it
    /// <c>vertical-skylines</c> from its stencil and, although it lists
    /// <c>outside-staff-interface</c>, it sets NO <c>outside-staff-priority</c>, so it is
    /// never pushed out and joins the staff's own skyline exactly as the clef does.
    /// Measured: Lily# draws the LOWER staff's bracket across the UPPER staff's lines.
    /// </para>
    /// <para>
    /// (2) A PHANTOM STEM. <c>TupletBracketEngraver.cs:573,585</c> adds
    /// <c>DefaultStemLength</c> 3.5 to the extreme note with no test of the duration, so a
    /// whole note gets a stem it has not got — the third instance of the defect
    /// <c>89aaa29f</c> removed from <c>SkylineBuilder</c> and <c>26afa9fe</c> from
    /// <c>DynamicEngraver</c>. LILYPOND-REF: <c>lily/stem.cc Stem::is_normal_stem</c>
    /// (duration-log &gt;= 1). Measured on 2.26.0: LilyPond puts the bracket at the
    /// notehead's INK plus <c>TupletBracket.padding</c> 1.1 and nothing else, while Lily#
    /// draws it 3.5 - 0.545 = 2.955 further out.
    /// </para>
    /// <para>
    /// Why the pitch is not free: on <c>d'</c> in the bass staff the bracket reaches 5.225
    /// above the refpoint and 5.225 + 2.05 + 1 = 8.275 LOSES to StaffGrouper's
    /// basic-distance 9 — measured, that book prints 9.000000 on BOTH sides and measures
    /// nothing. The notes are raised until the bracket beats the floor with room to spare.
    /// Why two voices: the bracket sits on its voice's stem side, so a forced voice is what
    /// makes "bracket facing the gap" and "deep enough to beat the floor" satisfiable at
    /// once — the same contradiction <see cref="DY"/> dissolves the same way. Lily# takes
    /// the side from <c>VoiceDefaults</c> only when the staff has more than one voice, so
    /// the twin must be polyphonic too.
    /// </para>
    /// LilyPond twin: <c>\new PianoStaff &lt;&lt; \new Staff { \time 8/4 b'1 b'1 }
    /// \new Staff { \clef bass \time 8/4 &lt;&lt; { \voiceOne \tuplet 3/2 { a'1 a'1 a'1 } }
    /// \\ { \voiceTwo d1 d1 } &gt;&gt; } &gt;&gt;</c>.
    /// </remarks>
    private static readonly string TU = """
        octave absolute
        time 8/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { b1 b1 | }
          lh { voice { tuplet 3/2 { a1 a1 a1 } } voice { d,1 d,1 } | }
        }

        form main { ~Main }

        score main "TU" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// <see cref="TU"/> with the bracket on the other side — the mirror of book TD.
    /// </summary>
    /// <remarks>
    /// Not redundant with <see cref="TU"/>, for the same reason Q is not redundant with P:
    /// TU binds the UPPER staff's bottom line against a bracket coming up, TD binds the
    /// LOWER staff's top line against one going down. Two edges, two skylines, and a sign
    /// error shows up in exactly one of them. <c>d,</c> (LilyPond <c>d</c>) sits 6 spaces
    /// below the treble staff's middle line — the pitch <see cref="P"/> already uses.
    /// <para>
    /// LilyPond twin: <c>\new PianoStaff &lt;&lt; \new Staff { \time 8/4 &lt;&lt;
    /// { \voiceOne b'1 b'1 } \\ { \voiceTwo \tuplet 3/2 { d1 d1 d1 } } &gt;&gt; }
    /// \new Staff { \clef bass \time 8/4 d1 d1 } &gt;&gt;</c>.
    /// </para>
    /// </remarks>
    private static readonly string TD = """
        octave absolute
        time 8/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { voice { b1 b1 } voice { tuplet 3/2 { d,1 d,1 d,1 } } | }
          lh { d,1 d,1 | }
        }

        form main { ~Main }

        score main "TD" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// A SLUR over stemless whole notes, drooping DOWN into the staff gap from the upper
    /// staff — the first ledger point ever to reach a slur. Measures the staff-to-staff
    /// distance, shaped like <see cref="P"/> and <see cref="TU"/>.
    /// </summary>
    /// <remarks>
    /// LilyPond's Slur is an ordinary inside-staff grob — measured on 2.26.0 it carries NO
    /// <c>outside-staff-priority</c>, so it joins the staff's own vertical skyline like the
    /// clef and the tuplet bracket. Lily#'s <c>SkylineBuilder</c> does not contain the word
    /// "slur"; slurs reach only <c>EnrichExtentsWithAnnotationProtrusions</c>, which feeds
    /// the scalar fallback the skyline path beats wherever a skyline exists — the same
    /// architecture that hid the tuplet bracket. So between two staves the slur should be
    /// reserved NOWHERE and the gap should rest on the notes alone.
    /// <para>
    /// The pitch is chosen so the notes lose to the floor and the slur beats it: the <c>g,</c>
    /// (LilyPond <c>g</c>, G3) noteheads reach 5.045 below the refpoint, and 5.045 + 2.05 + 1
    /// = 8.095 LOSES to StaffGrouper's basic-distance 9, so a Lily# that reserves the notes
    /// and not the slur sits on that floor at 9.000000, while LilyPond's slur droops to
    /// 6.462596 below the refpoint for a gap of 9.512596. Default slur direction, no override
    /// — a low note takes an up stem so its slur curves down, which is what LilyPond does too.
    /// </para>
    /// LilyPond twin: <c>\new PianoStaff &lt;&lt; \new Staff { \time 8/4 g1( g1) }
    /// \new Staff { \clef bass \time 8/4 d1 d1 } &gt;&gt;</c>.
    /// </remarks>
    private static readonly string SD = """
        octave absolute
        time 8/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { g,1( g,1) | }
          lh { d,1 d,1 | }
        }

        form main { ~Main }

        score main "SD" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// <see cref="SD"/> with the slur on the other side — a slur reaching UP into the gap
    /// from the lower staff, the mirror of book SU.
    /// </summary>
    /// <remarks>
    /// Not redundant with <see cref="SD"/>, for the reason Q is not redundant with P: SD
    /// binds the LOWER staff's top line against a slur coming down, SU binds the UPPER
    /// staff's bottom line against one going up. Two edges of one gap through two different
    /// skylines. <c>f</c> (LilyPond <c>f'</c>, F4) sits +9 above the bass staff's middle
    /// line, the mirror of <c>g,</c>'s -9 below the treble one, so LilyPond prints the same
    /// 9.512596 and the two must agree.
    /// <para>
    /// LilyPond twin: <c>\new PianoStaff &lt;&lt; \new Staff { \time 8/4 b'1 b'1 }
    /// \new Staff { \clef bass \time 8/4 f'1( f'1) } &gt;&gt;</c>.
    /// </para>
    /// </remarks>
    private static readonly string SU = """
        octave absolute
        time 8/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { b1 b1 | }
          lh { f1( f1) | }
        }

        form main { ~Main }

        score main "SU" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// The slur pair (<see cref="SD"/>/<see cref="SU"/>) again with a TIE — the adjacent
    /// inside-staff grob, drooping DOWN into the staff gap from the upper staff.
    /// </summary>
    /// <remarks>
    /// LilyPond's Tie, like its Slur, carries <c>vertical-skylines</c> from its stencil and
    /// sets NO <c>outside-staff-priority</c> (measured on 2.26.0), so it joins the staff's own
    /// vertical skyline and a staff below must clear its bow. Lily#'s <c>SkylineBuilder</c>
    /// seeds tuplet brackets and slurs but NOT ties, so between two staves a tie is reserved
    /// NOWHERE and the gap rests on the notes alone — the same defect the slur had before
    /// <c>d11ede43</c>, one grob over.
    /// <para>
    /// A tie is flatter than a slur (details <c>height-limit 1.0 / ratio 0.333</c> vs the
    /// slur's <c>2.0 / 0.25</c>), so the tied notes sit further out than SD/SU's g/f' to keep
    /// the bow off the basic-distance-9 floor: <c>e,</c> (LilyPond <c>e</c>, E3) is -11 below
    /// the treble middle line. LilyPond droops the tie to a gap of 9.655901202802955; a Lily#
    /// that reserves the notes and not the tie rests on the note head (E3 centre 5.5 + 0.545
    /// = 6.045 below the middle, + 3.05 = 9.095), so the predicted residual is about -0.56,
    /// the same shape as the slur's pre-seed -0.512596.
    /// </para>
    /// <para>
    /// LilyPond twin: <c>\new PianoStaff &lt;&lt; \new Staff { \time 8/4 e1~ e1 }
    /// \new Staff { \clef bass \time 8/4 d1 d1 } &gt;&gt;</c>.
    /// </para>
    /// </remarks>
    private static readonly string TID = """
        octave absolute
        time 8/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { e,1~ e,1 | }
          lh { d,1 d,1 | }
        }

        form main { ~Main }

        score main "TID" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// <see cref="TID"/> with the tie on the other side — a tie reaching UP into the gap from
    /// the lower staff, the mirror of book TIU. <c>a</c> (LilyPond <c>a'</c>, A4) sits +11
    /// above the bass staff's middle line, so LilyPond prints the same 9.655901202802955 and
    /// the two must agree (the pair's cross-check, as for SD/SU and P/Q).
    /// </summary>
    /// <remarks>
    /// LilyPond twin: <c>\new PianoStaff &lt;&lt; \new Staff { \time 8/4 b'1 b'1 }
    /// \new Staff { \clef bass \time 8/4 a'1~ a'1 } &gt;&gt;</c>.
    /// </remarks>
    private static readonly string TIU = """
        octave absolute
        time 8/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { b1 b1 | }
          lh { a1~ a1 | }
        }

        form main { ~Main }

        score main "TIU" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// A BEAM over same-pitch eighth notes, drooping DOWN into the staff gap from the upper
    /// staff — the first ledger point ever to reach a beam. Measures the staff-to-staff
    /// distance, shaped like <see cref="SD"/> and <see cref="TU"/>.
    /// </summary>
    /// <remarks>
    /// A beam is DRAWN by the quanter at whatever stem length its beat needs, but Lily#'s
    /// <c>SkylineBuilder.AddNoteBoxToSkylines</c> reserves each note's box with a FIXED stem
    /// of <c>DefaultStemLength</c> 3.5 and never consults the quanter. So a beam group of low,
    /// forced-down eighths reserves a 3.5 stem where LilyPond's quanter draws a SHORTER one —
    /// the "draws right, reserves stale" double model, the same shape the tuplet bracket had
    /// before it was seeded (there the bracket was reserved NOWHERE; here the stem is reserved
    /// too LONG).
    /// <para>
    /// The pitch is chosen so the beam binds and the noteheads alone do not: the <c>g,</c>
    /// (LilyPond <c>g</c>, G3) sits 4.5 below the treble middle line, so LilyPond's beam
    /// quantises to positions -6.81 and its outer edge reaches 6.81 + 0.24 (half of
    /// <c>Beam.thickness</c> 0.48) = 7.05 below the refpoint, for a gap of 7.05 + 2.05 + 1 =
    /// 10.100000. A Lily# that reserves the fixed 3.5 stem reads g's stem tip at 4.5 + 3.5 =
    /// 8.0 instead (gap 8.0 + 2.05 + 1 = 11.05), while the noteheads alone (5.045 below the
    /// refpoint, + 2.05 + 1 = 8.095) would LOSE to StaffGrouper's basic-distance 9. So the
    /// predicted residual is +0.95, the whole of the stem it over-reserves.
    /// </para>
    /// <para>
    /// Why two voices, and why it is load bearing: Lily# cannot force a beam group's
    /// direction from a single-note token (measured, the beam came out UP), so the beam is put
    /// in the SECOND voice, whose stems and beam Lily# forces down — the same device
    /// <see cref="DY"/> and <see cref="TD"/> use. Measured on 2.26.0, LilyPond's quant is
    /// identical under single-voice <c>\stemDown</c>, <c>\voiceTwo</c> and <c>\voiceOne</c>
    /// (all -/+6.81 to fourteen digits), so the two-voice twin faithfully mirrors the
    /// single-voice defect. Voice one holds the middle line so the staff is an ordinary
    /// two-voice texture.
    /// </para>
    /// LilyPond twin: <c>\new PianoStaff &lt;&lt; \new Staff { \time 4/4 &lt;&lt;
    /// { \voiceOne b'1 } \\ { \voiceTwo g8 g g g g g g g } &gt;&gt; }
    /// \new Staff { \clef bass \time 4/4 d1 } &gt;&gt;</c>.
    /// </remarks>
    private static readonly string BMD = """
        octave absolute
        time 4/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { voice { b1 } voice { g,8 g, g, g, g, g, g, g, } | }
          lh { d,1 | }
        }

        form main { ~Main }

        score main "BMD" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// <see cref="BMD"/> with the beam on the other side — an up-stemmed beam reaching UP into
    /// the gap from the lower staff, the mirror of book BMU.
    /// </summary>
    /// <remarks>
    /// Not redundant with <see cref="BMD"/>, for the reason Q is not redundant with P: BMD
    /// binds the lower staff's top line against a beam coming down, BMU the upper staff's
    /// bottom line against one going up. Two edges of one gap through two different skylines.
    /// <c>f</c> (LilyPond <c>f'</c>, F4) sits +9 above the bass staff's middle line, the
    /// mirror of <c>g,</c>'s -9 below the treble one, so LilyPond prints the same 10.100000
    /// and the two must agree. The beam is in the FIRST voice here, whose stems Lily# forces
    /// UP.
    /// <para>
    /// LilyPond twin: <c>\new PianoStaff &lt;&lt; \new Staff { \time 4/4 b'1 }
    /// \new Staff { \clef bass \time 4/4 &lt;&lt; { \voiceOne f'8 f' f' f' f' f' f' f' } \\
    /// { \voiceTwo d1 } &gt;&gt; } &gt;&gt;</c>.
    /// </para>
    /// </remarks>
    private static readonly string BMU = """
        octave absolute
        time 4/4
        key c major

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { b1 | }
          lh { voice { f8 f f f f f f f } voice { d,1 } | }
        }

        form main { ~Main }

        score main "BMU" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>
    /// The same tuplet bracket as <see cref="TU"/>, measured BETWEEN SYSTEMS instead of
    /// between staves — the mirror of book TSD.
    /// </summary>
    /// <remarks>
    /// TU and TD reach <c>MultiStaffLayouter.BuildAllStaffSkylines</c>. Nothing in the
    /// corpus reaches <c>LayoutEngine.AugmentSkylinesForPaging</c>, the OTHER place Lily#
    /// builds a vertical skyline, and it does not seed a <c>TupletBracketLayout</c> either.
    /// (<c>EnrichExtentsWithAnnotationProtrusions</c> does see tuplets, but it feeds the
    /// scalar fallback that the skyline path beats whenever a skyline exists, so it never
    /// decides anything.) One staff over several systems, so the same <c>StaffGap()</c>
    /// reads system-system-spacing here.
    /// <para>
    /// THE FLOOR IS MUCH HIGHER THAN BETWEEN STAVES. There the bracket has to beat
    /// StaffGrouper's basic-distance of 9; here it has to beat system-system-spacing's
    /// TWELVE. The notes sit 8 staff spaces outside the middle line so the bracket clears
    /// that with room: 8 + 0.545 + 1.1 + 0.627717 + 2.05 + 1 = 13.322717. And the notes
    /// alone must NOT bind, or the entry stops being about the bracket — 8.545 + 2.05 + 1
    /// = 11.595 is under 12, so a Lily# that reserves the notes and not the bracket sits
    /// exactly on the floor and the residual reads the whole bracket stack.
    /// </para>
    /// <para>
    /// ⚠️ EACH BAR OPENS WITH A PLAIN WHOLE NOTE and that is not decoration. Written as a
    /// bar-filling tuplet the bracket starts right after the clef, and measured that way
    /// this book read 14.785225 rather than 13.322717: at that x the other system's
    /// deepest ink is not its staff line at 2.05 but its CLEF at 3.540. Hiding the clef at
    /// line starts moved the number and nothing else did. That would have folded the
    /// clef's own LILC-versus-skyline sliver — the residual
    /// <c>system.clef-bounded-distance</c> carries — into a tuplet entry.
    /// </para>
    /// LilyPond twin: <c>\new Staff { \time 12/4 \repeat unfold 6 { &lt;&lt; { \voiceOne
    /// a'1 \tuplet 3/2 { d''''1 d''''1 d''''1 } } \\ { \voiceTwo b'1 b'1 b'1 } &gt;&gt; } }</c>.
    /// </remarks>
    private static readonly string TSU = $$"""
        octave absolute
        time 12/4
        key c major

        part melody

        section Main {
          melody {
            voice { {{string.Concat(Enumerable.Repeat("a1 tuplet 3/2 { d'''1 d'''1 d'''1 } | ", 6)).Trim()}} }
            voice { {{string.Concat(Enumerable.Repeat("b1 b1 b1 | ", 6)).Trim()}} }
          }
        }

        form main { ~Main }

        score main "TSU" {
          staff melody
        }
        """;

    /// <summary>
    /// <see cref="TSU"/> with the bracket on the other side — the mirror of book TSD.
    /// </summary>
    /// <remarks>
    /// The two are one gap seen from its two edges, and the notes are the same distance
    /// out on each side, so LilyPond prints 13.322717 for both. A difference between them
    /// is a defect in its own right — the property P/Q and TU/TD are built around.
    /// </remarks>
    private static readonly string TSD = $$"""
        octave absolute
        time 12/4
        key c major

        part melody

        section Main {
          melody {
            voice { {{string.Concat(Enumerable.Repeat("b1 b1 b1 | ", 6)).Trim()}} }
            voice { {{string.Concat(Enumerable.Repeat("a1 tuplet 3/2 { g,,1 g,,1 g,,1 } | ", 6)).Trim()}} }
          }
        }

        form main { ~Main }

        score main "TSD" {
          staff melody
        }
        """;

    /// <summary>
    /// A SLUR measured BETWEEN SYSTEMS instead of between staves — the slur's version of
    /// <see cref="TSD"/>/<see cref="TSU"/>, drooping DOWN out of one system toward the next.
    /// The first ledger point to reach <c>LayoutEngine.AugmentSkylinesForPaging</c> with a
    /// slur.
    /// </summary>
    /// <remarks>
    /// <see cref="SD"/>/<see cref="SU"/> reach <c>MultiStaffLayouter.BuildAllStaffSkylines</c>,
    /// the per-staff skyline <c>Align_interface</c> reads. Nothing in the corpus reaches
    /// <c>AugmentSkylinesForPaging</c> — the OTHER skyline, the one the PAGE spaces systems by
    /// — with a slur: it seeds tuplet brackets, figured basses and scripts but NOT slurs
    /// (nor ties), so between systems a slur is reserved NOWHERE, exactly as the tuplet
    /// bracket was before <c>075277ff</c>. One staff over several systems, so
    /// <c>StaffGapAt(1)</c> reads system-system-spacing here (an INTERIOR gap, since a
    /// span-dependent bow makes the first system's time signature and the last's final bar
    /// line space theirs a hair off; the interior systems are plain, like the probe's).
    /// <para>
    /// THE FLOOR IS TWELVE, not StaffGrouper's nine. The slur must clear it and the noteheads
    /// alone must NOT: <c>g,,</c> (LilyPond <c>g,</c>, G2) is 8 staff spaces below the middle
    /// line, so notehead-alone is 8.545 + 2.05 + 1 = 11.595, UNDER 12 — a Lily# that reserves
    /// the notes and not the slur sits on the floor and the residual reads the WHOLE slur
    /// protrusion past it. LilyPond droops the bow to 10.072501 below the refpoint, for a gap
    /// of 13.122501 that clears 12 with more than a staff space to spare.
    /// </para>
    /// <para>
    /// ⚠️ THE TWO SYSTEMS MUST BE IDENTICAL, because a slur's arc — unlike a tuplet bracket's
    /// fixed padding — depends on the horizontal SPAN, so SSD binds the top system's slur and
    /// SSU the bottom's, and any spacing difference between them makes the pair disagree. The
    /// probe forces this: <c>\break</c> for an even 4+4 split, <c>ragged-right = ##f</c> so the
    /// last system is justified too, <c>indent = 0</c> so the first system is not narrowed, and
    /// <c>\omit TimeSignature</c> so neither head carries a meter glyph the other lacks. With
    /// all four, LilyPond prints the IDENTICAL bow depth on both systems and SSD == SSU to
    /// full precision. Each bar still opens with a plain middle-line whole note, the correction
    /// <see cref="TSD"/> documents, so the bow meets the next system's staff line rather than
    /// its clef.
    /// </para>
    /// LilyPond twin: <c>\new Staff \with { \omit TimeSignature } { \time 12/4
    /// \repeat unfold 4 { b'1 g,1( g,1) } \break \repeat unfold 4 { b'1 g,1( g,1) } }</c>
    /// under <c>\paper { ragged-bottom = ##t ragged-right = ##f indent = 0 }</c>.
    /// </remarks>
    private static readonly string SSD = $$"""
        octave absolute
        time 12/4
        key c major

        part melody

        section Main {
          melody {
            {{ForcedFourBarSystems("b1 g,,1( g,,1) | ")}}
          }
        }

        form main { ~Main }

        score main "SSD" {
          staff melody
        }
        """;

    /// <summary>
    /// <see cref="SSD"/> with the slur on the other side — an up-slur reaching UP out of the
    /// lower system toward the one above it, the mirror of book SSU. <c>d'''</c> (LilyPond
    /// <c>d''''</c>, D7) sits +16 above the middle line, the exact reflection of <c>g,,</c>'s
    /// -16 below it, so LilyPond prints the same 13.122501 and the two must agree.
    /// </summary>
    /// <remarks>
    /// Not redundant with <see cref="SSD"/>, for the reason Q is not redundant with P: SSD
    /// binds the lower system's top ink against a bow coming down, SSU the upper system's
    /// bottom ink against one going up. Two edges of one gap through two different skylines.
    /// <para>
    /// LilyPond twin: <c>\new Staff \with { \omit TimeSignature } { \time 12/4
    /// \repeat unfold 4 { b'1 d''''1( d''''1) } \break \repeat unfold 4 { b'1 d''''1( d''''1) } }</c>
    /// under <c>\paper { ragged-bottom = ##t ragged-right = ##f indent = 0 }</c>.
    /// </para>
    /// </remarks>
    private static readonly string SSU = $$"""
        octave absolute
        time 12/4
        key c major

        part melody

        section Main {
          melody {
            {{ForcedFourBarSystems("b1 d'''1( d'''1) | ")}}
          }
        }

        form main { ~Main }

        score main "SSU" {
          staff melody
        }
        """;

    /// <summary>
    /// A TIE measured BETWEEN SYSTEMS instead of between staves — the tie's version of
    /// <see cref="SSD"/>/<see cref="SSU"/>, and one grob over from them. Drooping DOWN out of
    /// one system toward the next.
    /// </summary>
    /// <remarks>
    /// <see cref="TID"/>/<see cref="TIU"/> reach <c>SkylineBuilder.BuildStaffSkylines</c>, the
    /// per-staff skyline. Nothing in the corpus reaches <c>AugmentSkylinesForPaging</c> — the
    /// skyline the PAGE spaces systems by — with a tie: that pass now seeds tuplet brackets and
    /// slurs (<see cref="SSD"/>) but NOT ties, so between systems a tie is reserved NOWHERE,
    /// exactly the hole the slur had before <c>3ac143e7</c>. One staff over several systems, so
    /// <c>StaffGapAt(1)</c> reads system-system-spacing here (an INTERIOR gap, since a
    /// span-dependent bow makes the first system's time signature and the last's final bar line
    /// space theirs a hair off; the interior systems are plain).
    /// <para>
    /// THE PITCH RUNS DEEPER THAN SSD/SSU, and it is the TID design, not the SSD one. A tie is
    /// FLATTER than a slur (<c>height-limit 1.0 / ratio 0.333</c> vs the slur's <c>2.0 / 0.25</c>),
    /// so its bow protrudes far less. Rather than perch the notes UNDER the floor and read the
    /// tie's clearance past 12 (which a flat tie barely reaches), it takes TID's route: put the
    /// NOTEHEADS past the floor and read the WHOLE tie droop on top. <c>e,,</c> (LilyPond
    /// <c>e,</c>, E2) is 9 staff spaces below the middle line, so notehead-alone is
    /// 9.0 + 0.545 + 2.05 + 1 = 12.595, already ABOVE 12 — a Lily# that reserves the notes and
    /// not the tie sits on the NOTES, and the residual is exactly the tie's own droop, the same
    /// shape as <see cref="TID"/>'s -0.560901 (larger here, ~-0.9176, because the justified
    /// span widens the arc). LilyPond droops the tie to a gap of 13.512560327518213.
    /// </para>
    /// <para>
    /// LilyPond twin: <c>\new Staff \with { \omit TimeSignature } { \time 12/4
    /// \repeat unfold 4 { b'1 e,1~ e,1 } \break \repeat unfold 4 { b'1 e,1~ e,1 } }</c> under
    /// <c>\paper { ragged-bottom = ##t ragged-right = ##f indent = 0 }</c>.
    /// </para>
    /// </remarks>
    private static readonly string TSID = $$"""
        octave absolute
        time 12/4
        key c major

        part melody

        section Main {
          melody {
            {{ForcedFourBarSystems("b1 e,,1~ e,,1 | ")}}
          }
        }

        form main { ~Main }

        score main "TSID" {
          staff melody
        }
        """;

    /// <summary>
    /// <see cref="TSID"/> with the tie on the other side — an up-tie reaching UP out of the
    /// lower system toward the one above it, the mirror of book TSIU. <c>f'''</c> (LilyPond
    /// <c>f''''</c>, F7) sits +18 above the middle line, the exact reflection of <c>e,,</c>'s
    /// -18 below it, so LilyPond prints the same 13.512560327518213 and the two must agree.
    /// </summary>
    /// <remarks>
    /// Not redundant with <see cref="TSID"/>, for the reason <see cref="SSU"/> is not redundant
    /// with <see cref="SSD"/>: TSID binds the lower system's top ink against a bow coming down,
    /// TSIU the upper system's bottom ink against one going up. Two edges of one gap through two
    /// different skylines.
    /// <para>
    /// LilyPond twin: <c>\new Staff \with { \omit TimeSignature } { \time 12/4
    /// \repeat unfold 4 { b'1 f''''1~ f''''1 } \break \repeat unfold 4 { b'1 f''''1~ f''''1 } }</c>
    /// under <c>\paper { ragged-bottom = ##t ragged-right = ##f indent = 0 }</c>.
    /// </para>
    /// </remarks>
    private static readonly string TSIU = $$"""
        octave absolute
        time 12/4
        key c major

        part melody

        section Main {
          melody {
            {{ForcedFourBarSystems("b1 f'''1~ f'''1 | ")}}
          }
        }

        form main { ~Main }

        score main "TSIU" {
          staff melody
        }
        """;

    /// <summary>
    /// A BEAM measured BETWEEN SYSTEMS instead of between staves — the beam's version of
    /// <see cref="TSID"/>/<see cref="TSIU"/>, a forced-down beam drooping DOWN out of one
    /// system toward the next. The first ledger point to reach
    /// <c>LayoutEngine.AugmentSkylinesForPaging</c> with a beam.
    /// </summary>
    /// <remarks>
    /// <see cref="BMD"/>/<see cref="BMU"/> reach <c>SkylineBuilder.BuildStaffSkylines</c>,
    /// where the drawn beam is seeded (<c>AddBeamsToSkyline</c>) and the members' fixed stems
    /// suppressed. Nothing in the corpus reaches <c>AugmentSkylinesForPaging</c> — the skyline
    /// the PAGE spaces systems by — with a beam: that pass now seeds tuplet brackets, slurs
    /// and ties but NOT beams, and its base skylines come from <c>BuildSkylines</c>, where
    /// <c>AddNoteBoxToSkylines</c> reserves each beamed member's FIXED
    /// <c>DefaultStemLength</c> 3.5 stem. So between systems the last "draws right, reserves
    /// stale" double model survives. One staff over several systems, so <c>StaffGap()</c>
    /// reads system-system-spacing here (all systems identical, and a same-pitch beam's quant
    /// is span-independent, so the gaps are uniform and the TSD route — no interior-gap
    /// carve-out — applies).
    /// <para>
    /// THE FLOOR IS TWELVE and the notes sit 8 staff spaces below the middle line (<c>g,,</c>,
    /// LilyPond <c>g,</c>, G2 — the depth <see cref="TSU"/> proved): the beam must clear 12
    /// and the noteheads alone must NOT (8.545 + 2.05 + 1 = 11.595, under 12). All stems are
    /// forced (head off the middle line, direction against the default), so
    /// <c>beamed-stem-shorten</c> 1.0 applies exactly as in <see cref="BMD"/> — but at this
    /// depth the beam is far outside the staff and the quanter keeps the IDEAL shortened stem
    /// of 3.5 - 1.0 = 2.5 (BMD's 2.31 was the staff-line grid's pull). MEASURED on 2.26.0:
    /// the gap is 13.790000 = 8 + 2.5 (stem) + 0.24 (half of Beam.thickness 0.48) + 2.05
    /// (staff line ink) + 1 (padding), every term an LP constant. A Lily# that reserves the
    /// fixed 3.5 stem reads 8 + 3.5 + 2.05 + 1 = 14.55 instead — predicted residual +0.76,
    /// the stem it over-reserves past the drawn beam's outer edge (3.5 - 2.5 - 0.24).
    /// </para>
    /// <para>
    /// ⚠️ TWO VOICES, load bearing as in <see cref="BMD"/>: Lily# cannot force a beam group's
    /// direction from a single-note token, so the beam lives in the second voice, whose stems
    /// Lily# forces down. Each bar OPENS AND CLOSES with a plain whole note — the
    /// <see cref="TSU"/> correction, both ends — so the beam spans the middle third of the
    /// bar, where the other system's binding ink is its plain staff line, not its clef.
    /// </para>
    /// LilyPond twin: <c>\new Staff { \time 12/4 \repeat unfold 6 { &lt;&lt; { \voiceOne
    /// b'1 b'1 b'1 } \\ { \voiceTwo a'1 g,8 g, g, g, g, g, g, g, a'1 } &gt;&gt; } }</c>
    /// under <c>\paper { ragged-bottom = ##t }</c>.
    /// </remarks>
    private static readonly string BSD = $$"""
        octave absolute
        time 12/4
        key c major

        part melody

        section Main {
          melody {
            voice { {{string.Concat(Enumerable.Repeat("b1 b1 b1 | ", 6)).Trim()}} }
            voice { {{string.Concat(Enumerable.Repeat("a1 g,,8 g,, g,, g,, g,, g,, g,, g,, a1 | ", 6)).Trim()}} }
          }
        }

        form main { ~Main }

        score main "BSD" {
          staff melody
        }
        """;

    /// <summary>
    /// <see cref="BSD"/> with the beam on the other side — an up-stemmed beam reaching UP out
    /// of the lower system toward the one above it, the mirror of book BSU. <c>d'''</c>
    /// (LilyPond <c>d''''</c>, D7) sits +16 above the middle line, the exact reflection of
    /// <c>g,,</c>'s -16 below it, and quant, shorten and thickness are direction-symmetric,
    /// so LilyPond prints the same 13.790000 and the two must agree.
    /// </summary>
    /// <remarks>
    /// Not redundant with <see cref="BSD"/>, for the reason <see cref="TSIU"/> is not
    /// redundant with <see cref="TSID"/>: BSD binds the lower system's top ink against a beam
    /// coming down, BSU the upper system's bottom ink against one going up. Two edges of one
    /// gap through two different skylines. The beam is in the FIRST voice here, whose stems
    /// Lily# forces UP.
    /// <para>
    /// LilyPond twin: <c>\new Staff { \time 12/4 \repeat unfold 6 { &lt;&lt; { \voiceOne
    /// a'1 d''''8 d'''' d'''' d'''' d'''' d'''' d'''' d'''' a'1 } \\ { \voiceTwo b'1 b'1 b'1 }
    /// &gt;&gt; } }</c> under <c>\paper { ragged-bottom = ##t }</c>.
    /// </para>
    /// </remarks>
    private static readonly string BSU = $$"""
        octave absolute
        time 12/4
        key c major

        part melody

        section Main {
          melody {
            voice { {{string.Concat(Enumerable.Repeat("a1 d'''8 d''' d''' d''' d''' d''' d''' d''' a1 | ", 6)).Trim()}} }
            voice { {{string.Concat(Enumerable.Repeat("b1 b1 b1 | ", 6)).Trim()}} }
          }
        }

        form main { ~Main }

        score main "BSU" {
          staff melody
        }
        """;

    /// <summary>
    /// Line-start clef → first note with a CLEF-ONLY prefix, measured on an INTERIOR system.
    /// The horizontal prefix defect the page-crossing slur/tie residuals point to
    /// (<see cref="SSD"/>, <see cref="TSID"/>): under justification a clef prefix ~1.76 ss too
    /// wide compresses the notes, and its floor is here, at natural length.
    /// </summary>
    /// <remarks>
    /// An interior system carries a repeated clef but no repeated time signature, so its first
    /// note binds through Clef's <c>(first-note . minimum-fixed-space . 5.0)</c> — the one
    /// break-align spring LilyPond measures from the left item's LEFT edge with a max, so the
    /// clef width is absorbed into the 5.0. This cannot be measured on system 0 the way the
    /// rest of the X corpus is: Lily# always draws a meter glyph there, so system 0's prefix is
    /// not clef-only. Its LilyPond twin (barline-spacing.ly LSCT) omits the meter on a single
    /// system instead, verified to produce the identical clef-only spacing.
    /// <para>
    /// Whole notes near the middle line, enough bars to wrap past one system so that
    /// <c>ClefToFirstNoteOnSystem(1)</c> reads an interior one; the pitch does not matter (the
    /// spacing is clef-only), it is kept near the middle line only to keep the system band
    /// clean.
    /// </para>
    /// LilyPond twin: <c>\new Staff { \omit Staff.TimeSignature \time 4/4 c'1 c'1 }</c> — the
    /// omit-time single system that reproduces this interior clef-only prefix.
    /// </remarks>
    private static readonly string LSCT = $$"""
        octave absolute
        time 4/4
        key c major

        part melody

        section Main {
          melody {
            {{string.Concat(Enumerable.Repeat("b1 | ", 32)).Trim()}}
          }
        }

        form main { ~Main }

        score main "LSCT" {
          staff melody
        }
        """;

    /// <summary>
    /// <see cref="LSCT"/> with a BASS clef — a wider clef binding the same clef-only first-note
    /// spring. LilyPond prints the IDENTICAL distance to the treble twin (the clef width is
    /// absorbed by <c>max(width, 5.0)</c>, not added), the cross-check this pair exists for: a
    /// defect that ADDS the clef width makes the two residuals differ by the clef-width
    /// difference, while a wrong fixed constant would keep them equal — the P/Q relationship on
    /// the horizontal prefix.
    /// </summary>
    /// <remarks>
    /// LilyPond twin: <c>\new Staff { \omit Staff.TimeSignature \clef bass \time 4/4 c1 c1 }</c>.
    /// </remarks>
    private static readonly string LSCB = $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef bass }

        section Main {
          melody {
            {{string.Concat(Enumerable.Repeat("d,1 | ", 32)).Trim()}}
          }
        }

        form main { ~Main }

        score main "LSCB" {
          staff melody
        }
        """;

    // LilyPond twin: \new Staff { \time 4/4 c'1 c'1 } (barline-spacing.ly DCT). The TREBLE
    // control for defect-3: GClefWidth IS the G clef's own ink, so the clef→time distance
    // reads LilyPond's 4.085 and the residual is ~0 — confirming the clef→time mechanism is
    // right so the bass twin's residual is the width divergence alone.
    private static readonly string DCT = Score("c1 | c1 |", "DCT");

    /// <summary>
    /// The BASS half of the defect-3 pair (now CLOSED). CalculatePrefixWidth once reserved
    /// GClefWidth (the treble G's ink) for the wider F clef too, so Lily# spaced the line-start
    /// meter as if the clef were a treble G; LilyPond spaces it off the ACTUAL F-clef ink
    /// (2.683 vs 2.565), so its clef→time is 0.118 WIDER than the treble's. The fix threads the
    /// real per-clef ink (SpacingRules.MaxClefWidth / GlyphMetrics.LineStartClefWidth) so Lily#
    /// now matches LilyPond's 4.2034 exactly. The treble control (DCT) stays EQUAL to its old
    /// value — that pair (bass now DIFFERS from treble on the Lily# side, matching LilyPond) is
    /// the cross-check that defect-3 was exactly the fixed GClefWidth.
    /// </summary>
    /// <remarks>LilyPond twin: <c>\new Staff { \clef bass \time 4/4 c1 c1 }</c>.</remarks>
    private static readonly string DCB = $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef bass }

        section Main {
          melody { c,1 | c,1 | }
        }

        form main { Main }

        score main "DCB" {
          staff melody
        }
        """;

    /// <summary>
    /// The PERCUSSION half of defect-3 (the last remnant). Unlike a pitched clef, whose ink
    /// starts at the grob origin (ext-left 0), the percussion clef's ink begins 0.67 ss RIGHT
    /// of its origin: LilyPond 2.26.0 places the grob at 0.13 with ext (0.67 . 2.0), so its
    /// ink-left is 0.8 (the LeftEdge→clef offset every clef shares) and ink-right 2.13. The
    /// meter binds 1.52 off that ink right edge → TIME at 3.65, so the clef anchor → time anchor
    /// distance is 3.65 − 0.13 = 3.52. Lily# reserved GClefWidth (2.565) for the percussion clef
    /// AND drew the glyph at its origin without the 0.67 ink-left offset, so this twin read the
    /// treble 4.085 until both the ink WIDTH (1.33) and the draw origin (−0.67) were threaded.
    /// </summary>
    /// <remarks>LilyPond twin: <c>\new Staff { \clef percussion \time 4/4 c'1 c'1 }</c>.</remarks>
    private static readonly string DCP = $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef percussion }

        section Main {
          melody { c1 | c1 | }
        }

        form main { Main }

        score main "DCP" {
          staff melody
        }
        """;

    /// <summary>
    /// A SAME-STAFF KNEE between systems: each half-bar leaps about four octaves, so both
    /// engravers knee the beam without being told to (the leap is far past any auto-knee-gap).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The question is whether Lily#'s NON-seeding of a knee is observable. LilyPond leaves
    /// only CROSS-staff grobs out of the skylines (axis-group-interface.cc:850-858), so a
    /// same-staff kneed Beam and its Stems ARE in them; Lily# seeds neither
    /// (SkylineBuilder.AddBeamsToSkyline skips <c>IsKnee</c>) and leaves each member its
    /// per-note FIXED 3.5 stem instead, in the member's OWN direction.
    /// </para>
    /// <para>
    /// MEASURED, and it is why this point can exist at all: LilyPond's kneed system gap is
    /// 18.090000 where the SAME music with the knee forbidden
    /// (<c>\override Beam.auto-knee-gap = #100</c>, probe book KNEC) is 20.285000. So the knee
    /// is worth 2.195000 of vertical room and the regime is not inert. But LilyPond's kneed
    /// ink TOP sits 8.545000 above the refpoint — exactly d''''`s head (8.0 + 0.545) — so in
    /// the kneed case the beam band does not break the note heads' envelope, which is what
    /// Lily#'s inward-pointing substitute stems also stay inside.
    /// </para>
    /// <para>
    /// LilyPond twin: probe book KNE under <c>\paper { ragged-bottom = ##t }</c>, the paper
    /// BSD/BSU use.
    /// </para>
    /// </remarks>
    private static readonly string KNE = $$"""
        octave absolute
        time 12/4
        key c major

        part melody

        section Main {
          melody {
            {{string.Concat(Enumerable.Repeat("b1 g,,8 d''' g,, d''' b1 g,,8 d''' g,, d''' | ", 6)).Trim()}}
          }
        }

        form main { ~Main }

        score main "KNE" {
          staff melody
        }
        """;

    /// <summary>
    /// Cross-staff time-signature alignment. A grand staff whose staves carry DIFFERENT key
    /// signatures — the upper part is transposed (<c>transpose d</c>, so it prints D major = 2
    /// sharps) beside a concert-pitch lower staff (C major, no key). LilyPond break-aligns the
    /// TimeSignature into ONE column spanning both staves (the KeySignature group extent is the
    /// union across staves), so BOTH meters print at the same x, past the WIDEST key — the lower
    /// staff's meter is NOT tight against its clef. The twin dumps two TIME grobs at an EQUAL x
    /// (rendered: both 7.6534, spread 0); the Lily# side measures max−min of the per-staff meter x.
    /// </summary>
    /// <remarks>LilyPond twin: <c>\new PianoStaff &lt;&lt; \new Staff { \key d \major \time 4/4 d'1 d'1 }
    /// \new Staff { \clef bass \key c \major \time 4/4 c1 c1 } &gt;&gt;</c>.</remarks>
    private static readonly string TSA = $$"""
        octave absolute
        time 4/4
        key c major

        part upper { clef treble transpose d }
        part lower { clef bass }

        section Main {
          upper { c1 | c1 | }
          lower { c,1 | c,1 | }
        }

        form main { Main }

        score main "TSA" {
          grandStaff {
            staff upper
            staff lower
          }
        }
        """;

    /// <summary>
    /// Clef → time signature on a KEYED staff (D major, 2 sharps). The meter binds through the
    /// key, not the clef: KeySignature.space-alist (time-signature . (extra-space . 1.15))
    /// measured off the KEY's ink RIGHT edge, with NO extra pad. Rendered on 2.26.0: CLEF 0.8,
    /// KEY 4.185 (ext 2.2), TIME 7.535, so clef→time = 6.735. Guards the unified key-ink-measured
    /// time column against the old KeySigTrailingGap 0.4 draw-vs-reserve split.
    /// </summary>
    /// <remarks>LilyPond twin: <c>\new Staff { \key d \major \time 4/4 d'1 d'1 }</c>.</remarks>
    private static readonly string DCTK = $$"""
        octave absolute
        time 4/4
        key d major

        part melody { clef treble }

        section Main {
          melody { d1 | d1 | }
        }

        form main { Main }

        score main "DCTK" {
          staff melody
        }
        """;

    /// <summary>
    /// Time signature → first note on a keyed, metered first system — the STANDARD key
    /// control of the KCS/KCC pair. LilyPond places the head at the meter's ink RIGHT +
    /// 2.0 (TimeSignature.space-alist (first-note . (semi-shrink-space . 2.0)) at natural
    /// length): TIME 7.535, ink-right 9.235, HEAD 11.235 → 3.700. Opened as the control
    /// and immediately non-zero — the keyed+metered line-start first-note regime had no
    /// point until now.
    /// </summary>
    /// <remarks>LilyPond twin: <c>\new Staff { \key d \major \time 4/4 d'4 e' fis' g' |
    /// a'4 b' cis'' d'' }</c>.</remarks>
    private static readonly string KCS = Score("d4 e fis g | a b cis' d' |", "KCS", "d major");

    /// <summary>
    /// The SAME two sharps as a CUSTOM (non-traditional) signature. LilyPond has only one
    /// key model — keyAlterations — so its KCC dump is byte-identical to KCS; the pair's
    /// disagreement on the Lily# side isolates the custom-key reserve/draw split
    /// (WidestActiveKeySharps reads .Sharps only, a custom key is KeySignature(0, custom)).
    /// </summary>
    /// <remarks>LilyPond twin: <c>\new Staff { \set Staff.keyAlterations =
    /// #`((3 . ,SHARP) (0 . ,SHARP)) \time 4/4 d'4 e' fis' g' | a'4 b' cis'' d'' }</c>.</remarks>
    private static readonly string KCC = Score("d4 e fis g | a b cis' d' |", "KCC", "custom fis cis");

    /// <summary>
    /// The cut-common (2/2) half of the C-glyph width pair. LilyPond's default style
    /// prints ONLY 2/2 and 4/4 as glyphs (timesig.C22/C44, LILC ink 1.7 both); every
    /// other fraction is \number markup (Pango). Time → first note must equal KCS's
    /// 3.700000 exactly — the pair's cross-check that the C widths ride one path.
    /// </summary>
    /// <remarks>LilyPond twin: <c>\new Staff { \key d \major \time 2/2 d'2 e' |
    /// fis'2 g' }</c>.</remarks>
    private static readonly string KC2 = Score("d2 e | fis2 g |", "KC2", "d major");

    /// <summary>
    /// An ossia (no clef on first appearance) above a keyed main staff — the ossia's key
    /// signature must break-align into the ONE key column spanning the system, exactly
    /// like the grand-staff clef/time columns. The measured quantity is the ossia key X
    /// minus the main key X (metric-free; LilyPond prints 0). Sharps half of the pair.
    /// </summary>
    /// <remarks>LilyPond twin: probe score OKN (NR "Ossia staves" recipe — fontSize -3 +
    /// staff-space magstep(-3), firstClef ##f, no Time_signature_engraver).</remarks>
    private static readonly string OKN = $$"""
        octave absolute
        time 4/4
        key d major

        section Main {
          melody { d4 e fis g | a b cis' d' | }
          ossia_melody { d'4 e' fis' g' | r1 | }
        }

        form main { Main }

        score main "OKN" {
          staff melody
          ossia ossia_melody
        }
        """;

    /// <summary>Flats half of the ossia key-alignment pair (B-flat major). Must print the
    /// same offset as OKN — a difference is a content-dependence defect of its own.</summary>
    /// <remarks>LilyPond twin: probe score OKNF.</remarks>
    private static readonly string OKNF = $$"""
        octave absolute
        time 4/4
        key bes major

        section Main {
          melody { d4 ees f g | a bes c' d' | }
          ossia_melody { d'4 ees' f' g' | r1 | }
        }

        form main { Main }

        score main "OKNF" {
          staff melody
          ossia ossia_melody
        }
        """;

    /// <summary>
    /// A TAB staff beside a concert notation staff, both in C major — the CONTROL of the
    /// tab-key pair. LilyPond's TabStaff removes the Key_engraver (engraver-init.ly:1214),
    /// so a tab staff has no KeySignature grob and contributes nothing to the KeySignature
    /// break-align group; TKC and TKT carry the SAME notes and differ only in the tab
    /// staff's key, so LilyPond dumps them identically (verified: all 13 grobs equal).
    /// The quantity is TIME → first notehead on the notation staff = 3.300000, NOT the
    /// single-staff 3.700000: the line-start spring is merge_springs (spring.cc:104)
    /// AVERAGING one wish per staff, and the two staves wish differently because their last
    /// prefatory grob differs (meter, semi-shrink 2.0 → 8.82; TAB clef, minimum-fixed 5.0
    /// floored by the shared min_dist → 8.02; average 8.42 = ink-right 6.82 + 1.6). Two
    /// ORDINARY staves keep 3.700000 — their wishes are equal — so this control also opens
    /// the cross-staff wish-averaging regime, which Lily# does not model (it computes ONE
    /// system-wide first-note spring).
    /// </summary>
    /// <remarks>LilyPond twin: probe score TKC (<c>\new Staff { \key c \major \time 4/4
    /// c'4 d' e' f' | g'2 e' }</c> over <c>\new TabStaff { \key c \major c4 d e f |
    /// g2 e }</c>).</remarks>
    private static readonly string TKC = TabKeyScore("", "TKC");

    /// <summary>
    /// Plain NOTE-TO-NOTE spacing — the one thing this corpus has never measured.
    /// </summary>
    /// <remarks>
    /// Every other point measures FROM something: <c>barline.next.*</c> from a bar line,
    /// <c>line-start.*</c> from the prefix. Nothing measures one note to the next, so
    /// Lily#'s duration space has only ever been checked through those. Two measures, so
    /// the score is one system and stays ragged (LilyPond's own rule that a score whose
    /// only line would be stretched keeps its natural width,
    /// constrained-breaking.cc:142-148, which Lily# ports) — the springs sit at force 0 and
    /// the reading is the IDEAL, paper-independent like its neighbours.
    /// <para>
    /// Mixed durations on ONE pitch: the quarter gap and the eighth gap are the pair. One
    /// pitch keeps the columns' skylines a plain reach difference and earns no
    /// stem-direction correction, so what is left is the duration space alone. LilyPond's
    /// two readings differ by exactly the spacing-increment 1.2 (3.704200 against
    /// 2.504200), which is the pair's cross-check: a defect in the increment moves them
    /// apart, one in the base moves them together.
    /// </para>
    /// </remarks>
    /// <remarks>LilyPond twin: probe score NN.</remarks>
    private static readonly string NN = """
        octave absolute
        time 4/4

        part melody

        section Main {
          melody { c4 c8 c c4 c8 c | c4 c8 c c4 c8 c | }
        }

        form main { Main }

        score main "NN" { staff melody }
        """;

    /// <summary>
    /// The same note-to-note question in the regime <see cref="NN"/> cannot reach: a score
    /// whose shortest note is a QUARTER, with a HALF note gap and a REST as a spacing
    /// target.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THE SHORTEST MATTERS. <c>Spacing_spanner::find_shortest</c> does not use the
    /// score's shortest note: it takes the most common per-measure shortest and AVERAGES it
    /// with <c>base-shortest-duration</c> (1/8). In NN the most common shortest IS 1/8, so
    /// the average is 1/8 again and the averaging does nothing — the whole corpus has only
    /// ever measured the case where it is invisible. Here the most common shortest is 1/4,
    /// so <c>global_shortest</c> is (1/4 + 1/8)/2 = 3/16, a quarter's ratio is 4/3 rather
    /// than 2, and the same quarter gap reads 3.002245 against NN's 3.704200.
    /// </para>
    /// <para>
    /// The half gap is NOT that plus the spacing-increment 1.2: a half notehead is wider
    /// than a black one and <c>Note_spacing</c> adds the LEFT head's width, so the
    /// difference carries 0.073200 of glyph metric that no other note-to-note point reads.
    /// The rest is the third hole — every existing rest point measures a rest against a BAR
    /// LINE, never a note against a rest.
    /// </para>
    /// <para>
    /// Bar 3 is there so the most common per-measure shortest is unambiguously the quarter,
    /// and so nothing read here touches the FINAL bar line, whose column LilyPond places by
    /// its ink RIGHT edge rather than its left.
    /// </para>
    /// </remarks>
    /// <remarks>LilyPond twin: probe score HR in barline-spacing.ly.</remarks>
    private static readonly string HR = """
        octave absolute
        time 4/4

        part melody

        section Main {
          melody { c2 c2 | c4 c4 r2 | c4 c4 c4 c4 | }
        }

        form main { Main }

        score main "HR" { staff melody }
        """;

    /// <summary>
    /// A JUSTIFIED line with mixed durations — the first probe here whose value depends on
    /// a spring's MINIMUM rather than only on its ideal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other probe is ragged-right on purpose, which sits each spring at force 0 and
    /// makes the measurement paper-independent — and is exactly why the note-to-note
    /// minimum has never been measurable here. On a justified line the slack is shared out
    /// in proportion to <c>inverse_stretch_strength = fraction * max (0.1, ideal - min)</c>
    /// (lily/spacing-basic.cc), so the minimum decides where each note lands.
    /// </para>
    /// <para>
    /// Three things this had to get right, each learned by getting it wrong:
    /// the durations must MIX (a line of identical springs divides its width evenly
    /// whatever their stretch strengths are — eight equal quarters read the ragged natural
    /// 3.002245 and saw nothing); it must be SEVERAL systems (a single line is also the
    /// LAST line and stays ragged, and forcing it is not comparable either, because Lily#
    /// ports LilyPond's own rule that a score whose only line would be stretched stays
    /// ragged, constrained-breaking.cc:142-148); and the paper must MATCH, since unlike
    /// every other point here this one is paper-dependent.
    /// </para>
    /// <para>
    /// ⚠️ So this probe engraves on the DEFAULT page and must not pass an Options override:
    /// LilyPond's A4 with 10mm margins measures 102.429921 to the final bar line's ink
    /// right, and <see cref="LayoutOptions.Default"/> describes the same page
    /// (119.501575 less two 8.535827 = 102.429925). It also puts the line BREAKER into the
    /// comparison, so the pair below is read on the FIRST system, where the head indices
    /// are unambiguous, and a disagreement about how many bars fit shows up as a large
    /// residual rather than a plausible small one.
    /// </para>
    /// <remarks>LilyPond twin: probe score JN in line-start-mindist.ly, whose first system
    /// holds 5 bars with its first head at 8.585000.</remarks>
    /// </remarks>
    private static readonly string JN = """
        octave absolute
        time 4/4

        part melody

        section Main {
          melody {
            c4 c8 c c4 c8 c | c4 c8 c c4 c8 c | c4 c8 c c4 c8 c | c4 c8 c c4 c8 c |
            c4 c8 c c4 c8 c | c4 c8 c c4 c8 c | c4 c8 c c4 c8 c | c4 c8 c c4 c8 c |
            c4 c8 c c4 c8 c | c4 c8 c c4 c8 c | c4 c8 c c4 c8 c | c4 c8 c c4 c8 c |
            c4 c8 c c4 c8 c | c4 c8 c c4 c8 c | c4 c8 c c4 c8 c | c4 c8 c c4 c8 c |
          }
        }

        form main { Main }

        score main "JN" { staff melody }
        """;

    /// <summary>
    /// The line start of a COMPRESSED line — the one regime every other
    /// <c>line-start.*</c> point is blind to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rest of this corpus is read at force 0, where a line-start spring sits on its
    /// space-alist ideal and neither its FIXED distance nor its compressibility can be seen.
    /// This score's eight bars overflow the 102.429921 line — LilyPond and Lily# reach the
    /// same natural geometry, column for column
    /// (audit/lp-geometry/probes/ties-slurs-breaks-ragged.ly) — so LilyPond solves a NEGATIVE
    /// force and every spring gives ground by <c>|force| × inverse_compress_strength</c>.
    /// </para>
    /// <para>
    /// For the line-start spring that strength is <c>ideal - fixed</c>, and <c>fixed</c> is
    /// floored at <c>0.3 + min_dist</c> (lily/staff-spacing.cc:210-220) — the port
    /// <see cref="LineStartColumn.SpringWithMinimumDistanceFloor"/> makes. Without the floor
    /// the metered line start is 1.0 compressible; with it, 0.8. So this point moves when
    /// that floor is wired and NOTHING at force 0 does, which is what makes it the point that
    /// justifies the snapshot rebase (handoff section 5.2.1 item 3).
    /// </para>
    /// <para>
    /// It is the music of <c>Fixtures/test/ties-slurs.lys</c>, the fixture whose snapshot
    /// moves, deliberately: the pair is the snapshot's own regime rather than a proxy for it.
    /// Engraved on the default page, since this is the rare point that depends on the paper.
    /// </para>
    /// <para>
    /// ⚠️ WITHOUT the fixture's <c>tempo 120</c>, on both sides. A MetronomeMark draws a
    /// NOTEHEAD glyph ("♩ = 120") and <see cref="RenderedGeometry.TimeSignatureToFirstNotehead"/>
    /// finds that one first — it read 2.438400 before this was caught. The mark is
    /// outside-staff on both sides and does not enter the horizontal skylines, so dropping it
    /// leaves the quantity alone; keeping it would have measured the tempo mark.
    /// </para>
    /// </remarks>
    /// <remarks>LilyPond twin: probe score TSJ in ties-slurs-breaks.ly, which dumps
    /// <c>TimeSignature=4.885000..6.585000</c> and <c>head=8.579465</c> on a single
    /// justified system.</remarks>
    private static readonly string TSJ = """
        time 4/4
        key c major

        part melody

        // ⚠️ THE PHRASES ARE LOAD-BEARING, and flattening them into one melody block is what
        // made this probe measure DIFFERENT MUSIC from its LilyPond twin until 2026-07-25.
        // Lily# resets the relative frame at every phrase reference (RelativeResetMarker), so
        // in the fixture `slurs` opens on the c nearest C4 = c'. Inlined, the relative chain
        // runs on from the `a` that ends `ties`, and the nearest c to a' is c'' — an octave
        // up, which flips the stems of bars 4 and 5 down and cost 0.314107 ss of natural
        // width that was very nearly diagnosed as a Lily# spacing defect.
        phrase ties {
          c4~ c4 d2 |
          d2 e2~ | e4 f g a |
        }

        phrase slurs {
          c4( d e f) |
          g4( f e d) c2 r2 |
        }

        phrase tieDirection {
          c4~ c4 r2 |
          b'4~ b4 r2 |
        }

        section Main {
          melody { ties slurs tieDirection }
        }

        form main { Main }

        score main "TSJ" { staff melody }
        """;

    /// <summary>
    /// A tab staff ALONE, six strings — the defect half of the tab string-spacing pair.
    /// LilyPond's TabStaff sets <c>StaffSymbol.staff-space = 1.5</c> for every string
    /// count (ly/engraver-init.ly), so its six-string staff spans (6-1) × 1.5 = 7.500000
    /// line centre to line centre.
    /// </summary>
    /// <remarks>LilyPond twin: probe score CGT in line-start-mindist.ly, which dumps
    /// <c>space=1.500000 lines=6 staffY=-7.600000..0.000000</c> — the 7.6 being the 7.5
    /// span widened by half a line thickness at each edge.</remarks>
    private static readonly string TAB6 = """
        part gtr { instrument guitar section A { c4 d e f | } }
        form main { A }
        score main "TAB6" { tab gtr }
        """;

    /// <summary>
    /// The CONTROL: a FOUR-string tab staff, where Lily#'s per-string-count taper already
    /// happens to sit on LilyPond's 1.5, so the span is (4-1) × 1.5 = 4.500000 on both
    /// sides. The pair's point is that LilyPond's two readings differ ONLY by the string
    /// count while Lily#'s differ by the string count AND by a spacing that shrinks with
    /// it, so a control that is exact while the six-string half is 1.0 short isolates the
    /// taper rather than the tab staff as a whole.
    /// </summary>
    /// <remarks>LilyPond twin: probe score CG4 in line-start-mindist.ly
    /// (<c>space=1.500000 lines=4 staffY=-5.192000..-0.592000</c>, a 4.6 extent over a
    /// 4.5 span).</remarks>
    private static readonly string TAB4 = """
        part bs { instrument bass section A { c4 d e f | } }
        form main { A }
        score main "TAB4" { tab bs }
        """;

    /// <summary>
    /// The defect half of the tab-key pair: the SAME score with the tab staff in F# major
    /// (6 sharps). Nothing engraves that key — a tab staff prints none — yet the
    /// reservation once walked EVERY staff (tab, text row and ossia included) while the
    /// drawing walk skipped tab/text/ossia, so it booked a 6-sharp key column and shoved
    /// the first note that far right of the meter it is spaced from. Both walks now select
    /// the staves that ENGRAVE a signature
    /// (<see cref="Svg.Layout.SpacingRules.ContributesToKeyColumnWidth"/>) and union their
    /// engraved widths (<see cref="Svg.Layout.SpacingRules.WidestActiveKeyInk"/>).
    /// LilyPond's TKT dump is identical to TKC's, so the whole disagreement between the two
    /// Lily# readings was the reservation's staff set.
    /// </summary>
    /// <remarks>LilyPond twin: probe score TKT (the TabStaff's <c>\key fis \major</c>).</remarks>
    private static readonly string TKT = TabKeyScore("key fis major  ", "TKT");

    /// <summary>
    /// A notation staff over a tab staff of the SAME music, the tab part optionally opening
    /// with its own key. Both staves play the same pitches (a key never transposes), so the
    /// two scores this builds differ in nothing an engraver draws.
    /// </summary>
    private static string TabKeyScore(string tabKey, string name) => $$"""
        octave absolute
        time 4/4
        key c major

        part gt { clef treble }
        part pn { clef treble }

        section Main {
          gt { {{tabKey}}c,4 d, e, f, | g,2 e, }
          pn { c4 d e f | g2 e }
        }

        form main { Main }

        score main "{{name}}" {
          staff pn
          tab gt
        }
        """;

    // --- a system with NO STAFF: chords only (probes/staffless-system.ly) ---
    // All four scores below carry the SAME progression, LilyPond's
    // `\chordmode { c2 a:m | f2 g:7 | c1 }`, so the first symbol is "C" in every one of them
    // and its width cancels out of every difference these points measure. That matters here
    // more than usual: Lily# records a chord symbol's CENTRE (TextAnchor.Middle) while
    // LilyPond's ChordName reference point is its ink LEFT, so a RAW anchor could not be
    // compared at all — only a difference of two anchors read the same way on each side.
    //
    // ⚠️ Each uses `form main { ~Main }`, the SILENT section reference. A plain `{ Main }`
    // engraves a rehearsal mark, and on a staff-less system that box is the only other ink
    // on the row.

    /// <summary>
    /// Chords ONLY, 4/4 — the baseline every staff-less point subtracts. LilyPond puts the
    /// first symbol on 0.500000, which is <c>standard_breakable_column_spacing</c>'s
    /// <c>min_dist + 0.5</c> with <c>min_dist</c> 0: a ChordNames context engraves no clef,
    /// no key and no meter (ly/engraver-init.ly:703-725), so nothing prefatory stands in
    /// front of it.
    /// </summary>
    /// <remarks>LilyPond twin: probe score CO in staffless-system.ly.</remarks>
    private static readonly string SCO = StafflessChords("time 4/4", "key c major", "SCO");

    /// <summary>
    /// The METER identity twin: the same chords under 3/4. A ChordNames context has no
    /// Time_signature_engraver, so LilyPond draws no meter either way and CO3's first symbol
    /// lands on CO's to 15 digits — the LilyPond side of this pair is an IDENTITY, which
    /// makes any Lily# difference the size of a Lily# defect by construction. Lily# used to
    /// book <c>GetTimeSigWidth(beats, beatType)</c> here, which is NOT the same for 4/4 and
    /// 3/4; <see cref="Svg.Layout.SpacingRules.AnyStaffEngravesTime"/> now asks whether any
    /// row engraves a meter at all.
    /// </summary>
    /// <remarks>LilyPond twin: probe score CO3.</remarks>
    private static readonly string SCO3 = StafflessChords("time 3/4", "key c major", "SCO3");

    /// <summary>
    /// The KEY identity twin: the same chords in E major (4 sharps). ChordNames has no
    /// Key_engraver either, so LilyPond is again unmoved. This half was already closed —
    /// <see cref="Svg.Layout.SpacingRules.ContributesToKeyColumnWidth"/> excludes a text row —
    /// so it is a control that the key column really is shut, and a guard that it stays so.
    /// </summary>
    /// <remarks>LilyPond twin: probe score COK.</remarks>
    private static readonly string SCOK = StafflessChords("time 4/4", "key e major", "SCOK");

    /// <summary>
    /// The same chords OVER AN ORDINARY STAFF. Here LilyPond DOES engrave a clef and a meter,
    /// so its first symbol sits at 8.585000 — the same line-start number probes SKC and JN
    /// already pin and Lily# already reproduces. CS − CO = 8.085000 is therefore "what a
    /// staff earns", the frame-free quantity, and the staff-ful side being known-good is what
    /// makes the difference read the staff-LESS side.
    /// </summary>
    /// <remarks>LilyPond twin: probe score CS (<c>&lt;&lt; \new ChordNames … \new Staff … &gt;&gt;</c>).
    /// The staff plays <c>c'2 a' | f'2 g' | c'1</c>, i.e. Lily#'s <c>c2 a | f2 g | c1</c>.</remarks>
    private static readonly string SCS = """
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section Main {
          melody { c2 a | f2 g | c1 | }
          chords prog { c2 a:m | f2 g:7 | c1 | }
        }

        form main { ~Main }

        score main "SCS" {
          staff melody with chords prog
        }
        """;

    /// <summary>
    /// Chords AND lyrics with a NARROW first syllable — the regime where LilyPond's own offset
    /// is too small to reach past the line-start spring, so its column stands on 0.500000 with
    /// no text metric in it at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes a lead-sheet pair possible. LilyPond stands the column at
    /// <c>w/2 - 0.675</c> (the syllable is centred on the PaperColumn placeholder
    /// <c>X-alignment-extent = (0 . 1.35)</c>, define-grobs.scm:2749-2750) and Lily# at
    /// <c>w/2</c>, but the two do not measure the same <c>w</c> — Lily# draws lyrics in its own
    /// serif at 3.2 ss and is ~27% wider here ("Twin" 7.587200 against LilyPond's 5.975079).
    /// A point on the wide-syllable CL would mix the missing 0.675 with that metric difference.
    /// </para>
    /// <para>
    /// A narrow syllable separates them because the rod is a MINIMUM: under 2.35 ss LilyPond's
    /// reach falls below the 0.5 the line-start spring already gives, the rod goes slack, and
    /// the column sits exactly where the chords-only line does. MEASURED: CLI's syllable is
    /// 0.990156 wide and sits 0.179922 RIGHT of its column, CLA's is 1.365732 and reaches
    /// 0.007866 left — a real reach that does not bind — and both columns dump 0.500000.
    /// Lily#'s reach is <c>w/2</c> with no placeholder, so it clears 0.5 for anything over
    /// 1.0 ss ("I" 1.302400, "a" 1.779200) and the column leaves the floor.
    /// </para>
    /// <para>
    /// The lyric line is minimal and rhythmically identical on both sides: two half-note
    /// syllables per bar against the two half-note chords, because Lily# spreads a row's
    /// syllables evenly across the bar while LilyPond reads their written durations, and two
    /// equal syllables is where the two agree exactly.
    /// </para>
    /// </remarks>
    private static string StafflessChordsAndLyrics(string firstSyllable, string name) => $$"""
        octave absolute
        time 4/4
        key c major

        section Main {
          chords prog { c2 a:m | f2 g:7 | c1 | }
          lyrics words { {{firstSyllable}} no | oh no | yes | }
        }

        form main { ~Main }

        score main "{{name}}" {
          chords prog
          lyrics words
        }
        """;

    /// <remarks>LilyPond twin: probe score CLI (first syllable <c>I</c>).</remarks>
    private static readonly string SCLI = StafflessChordsAndLyrics("I", "SCLI");

    /// <remarks>LilyPond twin: probe score CLA (first syllable <c>a</c>, one letter wider).</remarks>
    private static readonly string SCLA = StafflessChordsAndLyrics("a", "SCLA");

    /// <summary>
    /// A plain vocal line — one staff, one syllable per note — which is the branch of
    /// <c>aligned_on_parent</c> that is normally taken: the parent column HAS note heads, so
    /// <c>he</c> is their combined extent and the placeholder never comes into it.
    /// </summary>
    /// <remarks>
    /// LilyPond twin: probe score LSH. MEASURED there: the note head is 1.377400 wide and the
    /// syllable's ink centre sits 0.688700 right of its column — half a note head, NOT the
    /// 0.675000 the staff-less scores give. The two numbers being different is what proves the
    /// note-column branch is live rather than dead code for lyrics.
    /// </remarks>
    private static readonly string SLSH = """
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section Main {
          melody { c2 a | f2 g | c1 | }
          lyrics words { I no | oh no | yes | }
        }

        form main { ~Main }

        score main "SLSH" {
          staff melody with lyrics words
        }
        """;

    /// <summary>A staff-less score of one chord progression, under a given meter and key.</summary>
    private static string StafflessChords(string time, string key, string name) => $$"""
        octave absolute
        {{time}}
        {{key}}

        section Main {
          chords prog { c2 a:m | f2 g:7 | c1 | }
        }

        form main { ~Main }

        score main "{{name}}" {
          chords prog
        }
        """;

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
        // The rest twin of the line above. Probe F measured only its CLOSING side, and
        // full-measure-extra-space lives on the OPENING one, so the corpus could not see
        // whether Lily# spends it on a rest column at all. LilyPond does — 1.900000 for
        // both, the same 0.9 + 1.0.
        new("barline.next.whole-rest", F, g => g.BarlineRightToNextGlyph(MidLineBarline)),
        new("barline.next.half-notes", G, g => g.BarlineRightToNextGlyph(MidLineBarline)),

        // The accidental is the first glyph after the bar line; the notehead is the second.
        // Recording BOTH is the point: it splits "this measure start is wrong" into the
        // bar-line side and the accidental-to-head side, which have different owners.
        new("barline.next.accidental", X, g => g.BarlineRightToNextGlyph(MidLineBarline)),
        new("barline.next.accidental-to-notehead", X, g => g.BarlineRightToNextNotehead(MidLineBarline)),
        // The single-note accidental DRAW gap. A natural clears its head at 0.367672 (its real
        // right skyline, not 0.35); a flat's ink starts 0.12 left of its origin, so the fixed-gap
        // draw over-placed it. Sharps (Left 0, box) are unaffected. See NAT / FLAT.
        new("accidental.single-natural-to-notehead", NAT, g => g.NaturalToNoteheadAnchor()),
        new("accidental.single-flat-to-notehead", FLAT, g => g.FlatToNoteheadAnchor()),

        // The first points to reach Accidental_placement's CHORD stacking (two accidentals
        // forced into two columns). Each alteration is a mirror pair (below/above the middle
        // line): the column gap is direction-independent, so the two must agree and a
        // difference is a defect on its own. See probes CSB/CSA, CFB/CFA and
        // ChordAccidentalColumnGap.
        new("chord.accidental.sharp-column-gap-below", CSB, g => g.ChordAccidentalColumnGap(MidLineBarline)),
        new("chord.accidental.sharp-column-gap-above", CSA, g => g.ChordAccidentalColumnGap(MidLineBarline)),
        new("chord.accidental.flat-column-gap-below", CFB, g => g.ChordAccidentalColumnGap(MidLineBarline)),
        new("chord.accidental.flat-column-gap-above", CFA, g => g.ChordAccidentalColumnGap(MidLineBarline)),

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

        // The change glyph here is the CANCELLATION natural. LilyPond engraves it as a
        // KeyCancellation grob and leaves the KeySignature itself empty (C major has no
        // accidentals), which is why the .ly probe dumps both and drops the empty one.
        new("midmeasure.key-cancel.prev-note-to-key", MKA,
            g => g.FirstNonNoteheadAfter(g.NoteheadAnchor(1)) - g.NoteheadAnchor(1)),
        new("midmeasure.key-cancel.key-to-next-note", MKA,
            g => g.NoteheadAnchor(2) - g.FirstNonNoteheadAfter(g.NoteheadAnchor(1))),

        // --- the column before a bar line -> that bar line (the closing side) ---
        // Section 3.3 of the working notes: a grob's position is fixed by BOTH gaps, so a
        // corpus that only measures one side can be fully green while the other side rots.
        new("barline.prev.whole-note", E, g => g.LastGlyphToBarlineLeft(MidLineBarline)),
        new("barline.prev.whole-rest", F, g => g.LastGlyphToBarlineLeft(MidLineBarline)),
        new("barline.prev.half-note", G, g => g.LastGlyphToBarlineLeft(MidLineBarline)),

        // --- the PAGE vertical ---
        // The first Y entries in a corpus that was X-only. The paper pair are constants and
        // will read exact until someone edits them, which is the point: they are the two
        // numbers that were wrong for the whole life of the project (A4 in PostScript points
        // instead of LilyPond's TeX points) and nothing would have caught it.
        // Two entries were drafted here and dropped, both for reasons worth keeping:
        //
        //   page.top-margin  — nothing DRAWS the top margin, so the only way to measure it
        //     from recorded output is first-refpoint minus an assumed 6.0. That hardcodes
        //     the very spacing the entry below exists to check, and would read exact
        //     whenever that one did.
        //
        //   page.height      — Lily# sizes a SINGLE page to its content and only switches to
        //     the paper once the content overflows (LayoutEngine.cs:606-611, a deliberate
        //     choice), while LilyPond always engraves onto the paper. On this probe that is
        //     a residual of -109.468268: real, understood, and not going to close. Carrying
        //     it would have taken total |residual| from 0.022412 to ~109.5 and made the
        //     headline number meaningless. It is recorded in HANDOFF instead. (The
        //     paginated path does use the paper height exactly — test/multi-page-vertical
        //     renders 3 x 169.009370.)
        new("page.width", V, g => g.PageWidth()),

        // These two are the live ones. The refpoint is where top-system-spacing puts the
        // first staff, and the gap is system-system-spacing's basic-distance — LilyPond
        // reads 11.690551 (= top-margin + 6.000000) and 12.000000 on this exact score.
        new("page.first-staff-refpoint", V, g => g.FirstStaffRefpoint()),
        new("system.natural-distance", V, g => g.StaffGap()),

        // The same two quantities in the STRETCHED regime (book J / probe W). LilyPond
        // solves one chain per page -- top-system-spacing, one spring per system pair, and
        // last-bottom-spacing -- against page_height_, so on a full page EVERY spring in
        // that chain carries the same force. These two entries read the two ends of it:
        // the top spring (6.000000 natural -> 6.025482 stretched) and a system spring
        // (12.000000 -> 12.254816). A port that stretches only the middle of the chain can
        // match neither, which is why both are here rather than just the gap.
        new("page.stretched.first-staff-refpoint", W, g => g.FirstStaffRefpoint()),
        new("system.stretched-distance", W, g => g.StaffGap()),

        // HOW MANY systems the breaker put there — the one quantity on this page that the
        // two entries above cannot see. They read a page that already holds N systems; both
        // stay green when N is wrong, because a differently-filled page still solves its own
        // chain to a uniform gap. The corpus has been blind to the page breaker for its whole
        // life, and the committed fixtures agree with it only by coincidence.
        new("page.stretched.systems-on-first-page", W, g => g.SystemsOnPage(0)),
        new("page.stretched.page-count", W, g => g.PageCount),

        // The same two counts on TIGHT paper, where the breaker's force actually decides
        // them. These are the entries that bind — see probe T and book T.
        new("page.tight.page-count", TP, g => g.PageCount, TightPaper),
        new("page.tight.systems-on-first-page", TP, g => g.SystemsOnPage(0), TightPaper),

        // The same two quantities again, on music that stays inside the staff so that the
        // CLEF is the extreme ink rather than a notehead. See the remarks on probe S: W
        // reads correct even when the clef is missing from the skyline entirely, because
        // its lowest notehead happens to reach 0.005 further down than the clef does.
        new("page.clef.first-staff-refpoint", S, g => g.FirstStaffRefpoint()),
        new("system.clef-bounded-distance", S, g => g.StaffGap()),

        // --- the STAFF-TO-STAFF distance inside one system (Align_interface) ---
        // One system with two staves, so the same StaffGap() that reads system-to-system
        // above reads staff-to-staff here: it returns one refpoint per STAFF, and there is
        // exactly one gap either way. The two entries are the two sides of the same staff
        // symbol -- see the remarks on P and Q for why one of them is not enough.
        new("staff.staff.upper-note-to-lower-lines", P, g => g.StaffGap()),
        new("staff.staff.lower-note-to-upper-lines", Q, g => g.StaffGap()),

        // The same gap again, shaped so a DYNAMIC under a stemless whole note is what binds
        // it — the first ledger point that reaches DynamicEngraver. See probe DY.
        new("staff.staff.dynamic-under-whole-note", DY, g => g.StaffGap()),

        // ...and again, shaped so a TUPLET BRACKET over stemless whole notes is what binds
        // it — the first ledger points that reach TupletBracketEngraver. Both sides, because
        // the two carry OPPOSITE-signed divergences (nothing reserves the bracket; the
        // bracket that is drawn sits a phantom stem too far out) and a single side cannot
        // separate them. See probes TU and TD.
        new("staff.staff.tuplet-bracket-up", TU, g => g.StaffGap()),
        new("staff.staff.tuplet-bracket-down", TD, g => g.StaffGap()),

        // ...and again, shaped so a SLUR over stemless whole notes is what binds it -- the
        // first ledger points that reach a slur. Both sides, because SD binds the lower
        // staff's top line against a slur coming down and SU the upper staff's bottom line
        // against one going up, and a difference between them is a defect on its own. See
        // probes SD and SU.
        new("staff.staff.slur-under-notes", SD, g => g.StaffGap()),
        new("staff.staff.slur-over-notes", SU, g => g.StaffGap()),

        // The slur pair once more with a TIE -- the adjacent inside-staff grob, which Lily#
        // seeds NOWHERE in its skyline (SkylineBuilder has tuplets and slurs, not ties). Both
        // sides because TID binds the lower staff's top line against a tie coming down and TIU
        // the upper staff's bottom line against one going up. See probes TID and TIU.
        new("staff.staff.tie-under-notes", TID, g => g.StaffGap()),
        new("staff.staff.tie-over-notes", TIU, g => g.StaffGap()),

        // ...and again, shaped so a BEAM over forced-down eighth notes is what binds it -- the
        // first ledger points that reach a beam. The beam is DRAWN by the quanter but Lily#'s
        // SkylineBuilder reserves each note's box with a FIXED 3.5 stem and ignores the
        // quanter, so it over-reserves the shortened same-pitch beam. Both sides because BMD
        // binds the lower staff's top line against a beam coming down and BMU the upper
        // staff's bottom line against one going up. See probes BMD and BMU.
        new("staff.staff.beam-under-notes", BMD, g => g.StaffGap()),
        new("staff.staff.beam-over-notes", BMU, g => g.StaffGap()),

        // The same bracket again, one staff over several systems, so StaffGap() reads
        // system-system-spacing instead of Align_interface. TU/TD reach
        // MultiStaffLayouter.BuildAllStaffSkylines; these are the only points that reach
        // LayoutEngine.AugmentSkylinesForPaging. See probes TSU and TSD.
        // ⚠️ IndentedPaper, not the default: books TSU/TSD keep LilyPond's 15mm indent
        // (page-vertical.ly:412-446) and it is not decoration — measured 2026-07-25, at
        // indent 0 LilyPond fits those six bars on ONE system and the inter-system gap these
        // two points read stops existing. See IndentedPaper.
        new("system.tuplet-bracket-up", TSU, g => g.StaffGap(), IndentedPaper),
        new("system.tuplet-bracket-down", TSD, g => g.StaffGap(), IndentedPaper),

        // The slur once more, one staff over two systems, so StaffGap() reads
        // system-system-spacing instead of Align_interface. SD/SU reach
        // MultiStaffLayouter.BuildAllStaffSkylines; these are the only slur points that reach
        // LayoutEngine.AugmentSkylinesForPaging, which seeds tuplet brackets but not slurs.
        // Both sides because SSD binds the lower system's top against a bow coming down and SSU
        // the upper system's bottom against one going up. See probes SSD and SSU.
        // An INTERIOR gap (index 1, between two plain middle systems): a slur's arc is
        // span-dependent, so the first system's time signature and the last system's final
        // bar line space their bows a hair differently, and StaffGap() would refuse a
        // non-uniform page. See StaffGapAt.
        new("system.slur-under-notes", SSD, g => g.StaffGapAt(1)),
        new("system.slur-over-notes", SSU, g => g.StaffGapAt(1)),

        // The tie one grob over from the slur, one staff over two systems: TID/TIU reach
        // SkylineBuilder.BuildStaffSkylines; these are the only tie points that reach
        // LayoutEngine.AugmentSkylinesForPaging, which now seeds tuplet brackets and slurs but
        // still not ties. Both sides because TSID binds the lower system's top against a bow
        // coming down and TSIU the upper system's bottom against one going up. See probes TSID
        // and TSIU. Same INTERIOR gap (index 1) as the slur pair, for the same span-dependence.
        new("system.tie-under-notes", TSID, g => g.StaffGapAt(1)),
        new("system.tie-over-notes", TSIU, g => g.StaffGapAt(1)),

        // The beam between systems: BMD/BMU reach SkylineBuilder.BuildStaffSkylines, where
        // the drawn beam is seeded and the members' fixed stems suppressed; these are the
        // only beam points that reach LayoutEngine.AugmentSkylinesForPaging, whose base
        // skylines (BuildSkylines) still reserve every beamed member's FIXED 3.5 stem — the
        // last "draws right, reserves stale" double model. Both sides because BSD binds the
        // lower system's top against a beam coming down and BSU the upper system's bottom
        // against one going up. A same-pitch beam's quant is span-independent, so the plain
        // first gap serves (the TSD route, not the slur pair's interior carve-out). See
        // probes BSD and BSU.
        new("system.beam-under-notes", BSD, g => g.StaffGap()),
        new("system.beam-over-notes", BSU, g => g.StaffGap()),
        // …and the beam regime those two do NOT reach: a KNEE, whose Beam and Stems LilyPond
        // keeps in the skylines (only CROSS-staff grobs are left out) and Lily# seeds not at
        // all, substituting each member's fixed 3.5 stem. See KNE.
        new("system.knee-beam-notes", KNE, g => g.StaffGap()),

        // --- line-start clef -> first note (BreakAlignSpacing.FirstNoteSpring, the
        // minimum-fixed-space branch of Staff_spacing::get_spacing). Measured on an INTERIOR
        // system, whose prefix is clef-only. A treble and a WIDER bass clef must print the
        // IDENTICAL LilyPond distance (the clef width is absorbed by max(width, 5.0), not
        // added), so a defect that adds the clef width makes the pair DISAGREE — the P/Q
        // cross-check applied to the horizontal prefix. This is where the sub-question-1
        // residual (system.slur-*, system.tie-*) lives, at natural length and at its source.
        new("line-start.clef-to-first-note.treble", LSCT, g => g.ClefToFirstNoteOnSystem(1)),
        new("line-start.clef-to-first-note.bass", LSCB, g => g.ClefToFirstNoteOnSystem(1)),

        // --- defect-3: the line-start prefix reserves a fixed GClefWidth for every clef, so a
        // wider clef's meter is placed as if the clef were a treble G. Measured clef anchor ->
        // time-signature anchor with a 4/4 meter present (the clef->time gap rides on the clef
        // ink width). Treble is the control (GClefWidth == the G clef ink, residual ~0); bass
        // exposes the defect (F clef ink 2.683 vs GClefWidth 2.565). LilyPond spaces off the
        // ACTUAL clef ink so its pair DIFFERS by the ink-width difference; Lily# prints them
        // EQUAL -- the cross-check.
        new("line-start.clef-to-time.treble", DCT, g => g.ClefToTimeSignatureOnFirstSystem()),
        new("line-start.clef-to-time.bass", DCB, g => g.ClefToTimeSignatureOnFirstSystem()),
        // The last defect-3 remnant: the percussion clef's ink starts 0.67 ss right of its
        // origin, so its clef->time is 3.52 (not the treble/bass shape). See DCP.
        new("line-start.clef-to-time.percussion", DCP, g => g.ClefToTimeSignatureOnFirstSystem()),
        // The TAB half of the same pair, against line-start.clef-to-time.treble as CONTROL:
        // LilyPond's TAB clef is an ordinary Clef grob in the shared break-align group and
        // is WIDER than the G (origin-to-ink-right 2.8 vs 2.565), so a tab staff under the
        // notation staff pushes the meter column 0.235 right — 4.320000 against the
        // control's 4.085000. Lily# printed them EQUAL while LilyPond differs, the
        // cross-check that the defect was MaxClefWidth's staff set.
        new("line-start.clef-to-time.tab", TKC, g => g.ClefToTimeSignatureOnFirstSystem()),
        // Tab VERTICAL geometry, which the corpus had no point for at all. LilyPond gives
        // every TabStaff staff-space 1.5 whatever its string count; Lily# tapers it, so the
        // six-string half is short while the four-string CONTROL lands on 1.5 by accident.
        // The note-to-note MINIMUM, reachable only on a justified line (see JN). The pair
        // is the two durations on that line: a defect in the shared minimum moves them by
        // DIFFERENT amounts, while a paper or line-breaking mismatch moves both together.
        // Plain note-to-note, ragged, which nothing else in this corpus reads.
        new("note-to-note.quarter", NN, g => g.NoteheadAnchor(1) - g.NoteheadAnchor(0)),
        new("note-to-note.eighth", NN, g => g.NoteheadAnchor(2) - g.NoteheadAnchor(1)),
        // The three holes NN cannot reach, all found by decomposing test/ties-slurs column
        // by column against LilyPond: a score whose most common shortest is a QUARTER (so
        // find_shortest's averaging with base-shortest-duration is observable at all), a
        // HALF gap (which carries the half notehead's extra 0.0732 of ink), and a REST as
        // the right-hand column. note-to-note.quarter-shortest is the PAIR against
        // note-to-note.quarter: the same quarter gap, 3.002245 against 3.704200, differing
        // only by global_shortest.
        new("note-to-note.half", HR, g => g.NoteheadAnchor(1) - g.NoteheadAnchor(0)),
        new("note-to-note.quarter-shortest", HR, g => g.NoteheadAnchor(3) - g.NoteheadAnchor(2)),
        new("note-to-rest.half-rest", HR,
            g => g.FirstGlyphAfter(g.NoteheadAnchor(3)).X - g.NoteheadAnchor(3)),
        // …and the closing side of the same measure, against barline.prev.whole-rest as
        // the control: the corpus has only ever measured a WHOLE rest into a bar line.
        new("barline.prev.half-rest", HR, g => g.LastGlyphToBarlineLeft(1)),
        // ACROSS the bar line — the same two notes' worth of duration space plus whatever
        // the bar line itself costs. The two gaps above are exact, so this isolates the
        // bar line's contribution to a line's total, which is what the breaker sums.
        new("note-to-note.across-barline", NN,
            g => g.NoteheadAnchor(6) - g.NoteheadAnchor(5)),
        // …and FIRST, how many notes the breaker put on that system. A justified point is
        // only meaningful once both engravers agree on that, so it is a checked point
        // rather than an assumption — and being a count it stays out of the ss total.
        new("justified.first-system.heads", JN,
            g => g.NoteheadAnchorsOnSystem(0).Count),
        // …and NOW the gaps themselves, which that count was the precondition for. They were
        // held back while the two engravers put different music on this system (4 bars
        // against LilyPond's 5), because recording them then would have pinned a
        // line-breaking disagreement to note spacing — the shape section 5.2 forbids. With
        // the count exact both sides share the same five bars and the gaps mean what they
        // say.
        //
        // ⚠️ They were opened expecting to be the ledger keys for removing
        // GlyphMetrics.MinItemGap 0.4, and they are NOT — both opened exact (+5.3e-8 and
        // -4.7e-7). LilyPond's note_spacing sets inverse_stretch_strength from
        // `len - increment_`, NOT from the skyline minimum (lily/spacing-basic.cc), so a
        // STRETCHED line never consults that minimum and the knob is invisible here. It
        // binds only where a spring is compressed onto its minimum, so the key that removal
        // needs is a COMPRESSED note-to-note point the corpus still lacks. What this pair
        // does pin is justification itself, which the ragged corpus cannot see at all.
        new("justified.note-to-note.quarter", JN,
            g => g.NoteheadAnchorsOnSystem(0)[1] - g.NoteheadAnchorsOnSystem(0)[0]),
        new("justified.note-to-note.eighth", JN,
            g => g.NoteheadAnchorsOnSystem(0)[2] - g.NoteheadAnchorsOnSystem(0)[1]),
        // …and the regime the two above turned out NOT to reach: a line squeezed until every
        // note-to-note spring sits on its MINIMUM. This is the only place a spring minimum is
        // observable, so it is the ledger key for GlyphMetrics.MinItemGap 0.4 — the knob
        // LilyPond does not have. See probe CN and compressed-note-spacing.ly.
        new("compressed.note-to-note.quarter", CN,
            g => g.NoteheadAnchor(1) - g.NoteheadAnchor(0), NarrowPaper),
        new("tab.staff.line-span.six-string", TAB6, g => g.StaffLineSpan()),
        new("tab.staff.line-span.four-string", TAB4, g => g.StaffLineSpan()),
        new("line-start.time-signature-cross-staff-alignment", TSA, g => g.TimeSignatureAlignmentSpread()),
        // …and the ABSOLUTE distance the spread cannot see. The alignment point is an identity
        // (both meters at one x), so it holds however wide the shared key column is — including
        // not at all. This one SPANS that column: clef to meter across a key only ONE staff
        // engraves, so it is the corpus's only guard that the line start is booked from the
        // union of the staves' OWN signatures (SpacingRules.WidestActiveKeyInk /
        // ActiveKeyInkForStaff) rather than from the score-level key, which here is C major and
        // books nothing. Pointing that reservation at score.KeySignature moves this by
        // 2.650000 — the key's 2.2 plus the Clef->Key and Key->Time gaps that only open when a
        // signature is engraved.
        new("line-start.clef-to-time.mixed-key-grand-staff", TSA,
            g => g.ClefToTimeSignatureOnFirstSystem()),
        new("line-start.clef-to-time.keyed", DCTK, g => g.ClefToTimeSignatureOnFirstSystem()),
        // The break-align draw-walk regimes (reserve/draw split): KCS is the standard-key
        // CONTROL, KCC the same two sharps declared `key custom` — the reservation reads
        // KeySignature.Sharps only, so only KCC loses its key column. The OKN/OKNF pair
        // measures the ossia key against the system-wide key column (metric-free, LP = 0).
        new("line-start.time-to-first-note.standard-key", KCS, g => g.TimeSignatureToFirstNotehead()),
        new("line-start.time-to-first-note.custom-key", KCC, g => g.TimeSignatureToFirstNotehead()),
        new("line-start.time-to-first-note.cut-common", KC2, g => g.TimeSignatureToFirstNotehead()),
        new("line-start.ossia-key-alignment.sharps", OKN, g => g.OssiaKeyAlignmentOffset()),
        new("line-start.ossia-key-alignment.flats", OKNF, g => g.OssiaKeyAlignmentOffset()),
        // WHICH staves the key column is made of. A tab staff engraves no key signature
        // (LilyPond removes its Key_engraver), so the pair's LilyPond side is an IDENTITY —
        // TKC and TKT differ only in a key nothing reads. Lily#'s reservation walks every
        // staff and its drawing walk skips tab/text/ossia, so only TKT loses.
        // The same quantity on a COMPRESSED line, which is where the line-start spring's
        // FIXED distance and its 0.3 + min_dist floor become observable at all. See TSJ.
        new("compressed.line-start.time-to-first-note", TSJ,
            g => g.TimeSignatureToFirstNotehead()),
        new("line-start.time-to-first-note.tab-concert", TKC, g => g.TimeSignatureToFirstNotehead()),
        new("line-start.time-to-first-note.tab-keyed", TKT, g => g.TimeSignatureToFirstNotehead()),
        // --- a system with NO STAFF (staffless-system.ly) ---
        // Every one of these is a DIFFERENCE of two chord-symbol anchors, because the two
        // engravers do not agree on what a chord symbol's anchor IS (Lily# the ink centre,
        // LilyPond the ink left) and the same progression opens all four scores, so the
        // convention and the symbol's width cancel. The second render is taken here rather
        // than inside RenderedGeometry: a ledger point is a quantity, and this one is
        // genuinely a quantity over two scores.
        new("staffless.line-start.chords-vs-staff", SCS,
            g => g.FirstChordSymbolAnchor() - ChordAnchorOf(SCO)),
        new("staffless.line-start.meter-identity", SCO3,
            g => g.FirstChordSymbolAnchor() - ChordAnchorOf(SCO)),
        new("staffless.line-start.key-identity", SCOK,
            g => g.FirstChordSymbolAnchor() - ChordAnchorOf(SCO)),
        // The one point that needs a single render: over a staff, LilyPond's ChordName and
        // the note head it stands over share an anchor exactly (CS dumps both at 8.585000),
        // because ChordName declares no X-offset and no self-alignment-interface at all
        // (scm/define-grobs.scm:837-855). LilyPond's value is 0 by construction, so this
        // measures Lily#'s centring of the symbol and nothing else.
        new("staffless.chord-symbol-over-notehead", SCS,
            g => g.FirstChordSymbolAnchor() - g.NoteheadAnchor(0)),
        // --- the lead sheet: two IDENTITIES on LilyPond's side (staffless-system.ly CLI/CLA) ---
        // Both measure the first column through the chord symbol standing on it, which is the
        // column exactly (ChordName has no X-offset — scm/define-grobs.scm:837-855), and both
        // are 0 on LilyPond's side by the rod being SLACK: a syllable under 2.35 ss reaches
        // less than the line-start spring's own 0.5, so the column never moves. Any Lily#
        // difference is therefore a Lily# defect in Lily#'s own units, with no LilyPond text
        // metric in it — which is the whole reason the narrow regime was measured.
        new("staffless.lead-sheet.narrow-syllable-floor", SCLI,
            g => g.FirstChordSymbolAnchor() - ChordAnchorOf(SCO)),
        new("staffless.lead-sheet.narrow-syllable-width-blind", SCLA,
            g => g.FirstChordSymbolAnchor() - ChordAnchorOf(SCLI)),
        // --- where the syllable itself sits: both branches of aligned_on_parent ---
        // The quantity is the syllable's ink CENTRE minus its column, in which the syllable's
        // own width cancels (see RenderedGeometry.FirstSyllableInkCentre) — the only lyric
        // quantity the two engravers can be compared on directly, since their lyric faces
        // differ. The column is read through the chord symbol standing on it (staff-less) or
        // the note head (staff-ful); both ARE their column.
        new("lyric.syllable-centre.placeholder-column", SCLI,
            g => g.FirstSyllableInkCentre() - g.FirstChordSymbolAnchor()),
        new("lyric.syllable-centre.note-column", SLSH,
            g => g.FirstSyllableInkCentre() - g.NoteheadAnchor(0)),
    };

    /// <summary>The first chord symbol's anchor in a SECOND score, for the difference points
    /// above. Rendering twice is the point: the quantity is a difference between scores.</summary>
    private static double ChordAnchorOf(string source) =>
        RenderedGeometry.Render(source).FirstChordSymbolAnchor();
}
