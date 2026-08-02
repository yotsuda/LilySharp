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
    /// <see cref="S"/>'s music shortened to one page — the Lily# half of books SCF and SCC in
    /// probes/system-clef-floor.ly, the pair that puts the system-to-system spring ON its
    /// SKYLINE FLOOR with the line-start CLEF as the ink that binds it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:625-632 <c>append_system</c> —
    /// <c>up_skyline.distance (bottom_skyline_, skyline-horizontal-padding) + padding</c> is a
    /// FLOOR under the inter-system spring, through <c>ensure_min_distance</c>. Lily# computes
    /// the same quantity at <c>LayoutEngine.cs:965</c> (and again in <c>PageLayouter</c>), and
    /// before this pair NOTHING in the corpus reached it with the clef binding, because on
    /// shipping paper it cannot: 3.776 + 3.540 + padding 1 = 8.316 is under
    /// <c>system-system-spacing</c>'s basic-distance of 12, so the spring reads its ideal and
    /// the skyline term leaves no trace. That is precisely what
    /// <c>system.clef-bounded-distance</c> records — 12.000000, exact, and blind to all of
    /// this. Two staves do not help either:
    /// LILYPOND-REF: lily/page-layout-problem.cc:1080-1127 <c>build_system_skyline</c> raises the up skyline to the
    /// system's FIRST spaceable staff and the down skyline to its LAST, so a system's own
    /// height is not in the distance and a plain two-staff score gets the same 8.316.
    /// <para>
    /// So the floor is made to bind by taking the spring's IDEAL away rather than by adding
    /// ink: <c>basic-distance</c> and <c>minimum-distance</c> both go to 0 and the padding
    /// stays LilyPond's shipping 1, which makes the reading <c>distance + 1</c> and nothing
    /// else. Adding ink instead was rejected for the reason probe S's header gives: whatever
    /// is deep enough to beat 12 is then what the entry measures, and the clef's own profile
    /// goes back to being invisible.
    /// </para>
    /// <para>
    /// ★ MEASURED 2026-07-28, and it FALSIFIED the prediction that opened this pair. The
    /// prediction was that the clef's OUTLINE would read below its BOX here, by the same
    /// 0.105961 skyline-binding.ly found between a G clef and a G clef (the outline's deepest
    /// point is at x = 1.84 and its highest at x = 2.228, so two of them facing each other
    /// touch in between). LilyPond prints <b>8.316000</b> — the box sum exactly. The reason is
    /// the horizon padding, which that earlier probe did not have —
    /// LILYPOND-REF: lily/skyline.cc:557-615 <c>Skyline::padded</c> extends every building by its <c>horizon_padding</c>
    /// by a FLAT plateau of <c>horizon_padding</c> and only then a 45° slope, so at 1.0 the
    /// clef's peak is flat from x ≈ 1.23 to x ≈ 3.23 and covers the deep point at 1.84
    /// outright. ⇒ At the SYSTEM level a box and an outline give the same answer, and this
    /// entry is the guard that says so: a Lily# whose horizontal padding is a bare 45° shoulder
    /// rather than plateau-then-slope reads LESS here, and the residual is that defect and not
    /// the clef's.
    /// </para>
    /// <para>
    /// ⚠️ <c>indent = 0</c> is load bearing on both sides, and not for trap 3's usual reason:
    /// <c>append_system</c> SHIFTS each system's skylines by its own indent (:600-601), so at
    /// LilyPond's default 15mm the first system's clef would stand about 8.5 staff spaces right
    /// of every other system's and the first gap would pair a clef against NOTES.
    /// <c>LayoutOptions.Default</c> already indents by 0; the .ly says so explicitly.
    /// </para>
    /// LilyPond twin: <c>\new Staff { \repeat unfold 24 { a'4 a' a' a' } }</c>.
    /// </remarks>
    private static string ClefFloorScore(string name) => $$"""
        octave absolute
        time 4/4
        key c major

        part melody

        section Main {
          melody { {{string.Concat(Enumerable.Repeat("a4 a a a | ", 24)).Trim()}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff melody
        }
        """;

    /// <summary>The book measured on the floor — the mirror of book SCF.</summary>
    private static readonly string SCF = ClefFloorScore("SCF");

    /// <summary>The same music on shipping spacing — the mirror of book SCC.</summary>
    private static readonly string SCC = ClefFloorScore("SCC");

    /// <summary>
    /// Ragged-bottom paper with the inter-system spring's IDEAL AND MINIMUM taken away, so the
    /// only thing holding two systems apart is the skyline floor — the paper of book SCF.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than derived from <c>RaggedBottomPaper</c> because static field
    /// initializers run in declaration order and that one is declared far below this point.
    /// <para>
    /// ⚠️ The music is short and the page keeps slack on purpose: a FULL ragged page compresses
    /// anyway (HANDOFF 5.0 trap 7) and would read a force instead of the floor. At force 0 a
    /// spring whose minimum exceeds its ideal returns exactly that minimum
    /// (LILYPOND-REF: lily/spring.cc:219-237 <c>Spring::length</c>, whose <c>inverse_compress_strength</c> the blocking force bypasses).
    /// </para>
    /// </remarks>
    private static readonly LayoutOptions ClefFloorPaper =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with { RaggedBottom = true },
            VerticalSpacing = VerticalSpacingParameters.Default with
            {
                SystemSystem = VerticalSpacingParameters.Default.SystemSystem with
                {
                    BasicDistance = 0,
                    MinimumDistance = 0,
                },
            },
        };

    /// <summary>
    /// The same paper with <c>system-system-spacing</c> left alone — the fork's other side,
    /// where the spring reads its ideal and says nothing about any skyline. The paper of SCC.
    /// </summary>
    private static readonly LayoutOptions ClefFloorControlPaper =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with { RaggedBottom = true },
        };

    /// <summary>
    /// TWO STAVES over several pages — the mirror of books JSS (justified) and JSSC
    /// (ragged-bottom control). The two entries built from it measure the staff spring
    /// LilyPond puts INSIDE a system and Lily# does not have.
    /// </summary>
    /// <remarks>
    /// <c>Page_layout_problem::append_system</c> pushes one spring per spaceable staff into
    /// the SAME chain as the system-to-system springs
    /// (LILYPOND-REF: lily/page-layout-problem.cc:651-720): ideal =
    /// <c>staff-staff-spacing</c>'s basic-distance 9, inverse stretch strength = its
    /// stretchability 5 (scm/define-grobs.scm:3352-3355). So a page stretched to force f
    /// moves the staves of a system apart by 5f while it moves the systems apart by 60f.
    /// Lily# solves one spring per SYSTEM boundary and draws every system at the score-wide
    /// <c>MultiStaffLayouter.CalculateSystemHeight</c>, i.e. at the <c>Align_interface</c>
    /// minimum, at every force.
    /// <para>
    /// The music is shaped so the staff spring does NOT sit on its floor: the treble's
    /// <c>c</c> (LilyPond <c>c'</c>) hangs 3.545 below its middle line and the bass staff's
    /// deepest up-stem reaches 3.0 above its own, so <c>ensure_min_distance</c> asks for
    /// 3.545 + 3.0 + 1 = 7.545, under the basic-distance 9 the spring stretches from. Book
    /// P's shape is the trap this avoids: its 9.595 floor blocks the spring until f &gt;
    /// 0.119, and pages like this solve to f ≈ 0.099.
    /// </para>
    /// <para>
    /// Both books carry the identical music and bar count and differ in ragged-bottom alone.
    /// ⚠️ <see cref="SixSystemsPerPage"/> is load bearing — left to choose, both engravers
    /// fill the page and COMPRESS it, which is the other regime entirely (see the .ly twin's
    /// header for the measurement that showed it).
    /// </para>
    /// </remarks>
    /// <param name="declareRemoveEmpty">Adds <c>removeEmpty all</c> to the upper part and
    /// nothing else. No staff in this music is ever empty, so the declaration cannot fire —
    /// see <see cref="HKCD"/> for the pair that rests on it. Defaulted, so the four books
    /// above are spelled exactly as before.</param>
    private static string TwoStaffPageScore(string name, bool declareRemoveEmpty = false) => $$"""
        octave absolute
        time 4/4
        key c major

        part rh { clef treble{{(declareRemoveEmpty ? " removeEmpty all" : "")}} }
        part lh { clef bass }

        section Main {
          rh { {{string.Concat(Enumerable.Repeat("c4 d e f | ", 120)).Trim()}} }
          lh { {{string.Concat(Enumerable.Repeat("c,4 d, e, f, | ", 120)).Trim()}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    /// <summary>The justified twin — the mirror of book JSS.</summary>
    private static readonly string JSS = TwoStaffPageScore("JSS");

    /// <summary>The ragged-bottom control — the mirror of book JSSC.</summary>
    /// <remarks>Same music as <see cref="JSS"/>, built by the same call, so the two cannot
    /// drift apart the way a hand-copied pair can (HANDOFF 5.0: both sides of a pair must be
    /// the same music, and it was not, twice).</remarks>
    private static readonly string JSSC = TwoStaffPageScore("JSSC");

    /// <summary>The COMPRESSED twin — the mirror of book JSK.</summary>
    /// <remarks>
    /// The same music again, on a page packed with eight systems instead of six, which is
    /// what the breaker picks for this score unaided — and it squeezes them. Compression
    /// runs on a DIFFERENT strength from stretching: <c>alter_spring_from_spacing_spec</c>
    /// sets minimum-distance 7 and then <c>set_default_strength</c>, so the inverse compress
    /// strength is <c>ideal - minimum</c> = 2 (spring.cc:205-211), and the
    /// <c>ensure_min_distance</c> that follows raises the FLOOR without recomputing the
    /// strength. Its system-spring counterpart is 12 - 8 = 4, where stretching used 60.
    /// <para>
    /// ⚠️ There is no ragged control for this one: ragged-bottom suppresses stretching only,
    /// so a full ragged page compresses just the same. The control is
    /// <c>page.natural.staff-staff-inside</c> — same music, same bar count, a page with
    /// slack — which is exact on both sides at 9.
    /// </para>
    /// </remarks>
    private static readonly string JSK = TwoStaffPageScore("JSK");

    /// <summary>
    /// At most six systems per page, so the page keeps slack to distribute and its springs
    /// STRETCH.
    /// </summary>
    /// <remarks>
    /// <c>max-systems-per-page</c> is a <c>\paper</c> variable in LilyPond, so it is set here
    /// for the same reason <see cref="TightPaper"/>'s paper height is — see
    /// <see cref="RenderedGeometry.Render"/>.
    /// <para>
    /// ⚠️ A CAP, not <c>SystemsPerPage = 6</c>, which was tried first and cannot work: this
    /// music is 17 systems, so the last page holds 5, and <c>PageBreaker</c> drops every
    /// candidate page whose count is not exactly 6 (PageBreaker.cs:523). With no feasible
    /// paging left it fell back to a single content-sized page carrying all 17 systems — the
    /// probe measured its own fallback rather than a stretched page, and only the staff COUNT
    /// entry noticed. LilyPond takes the exact form (it re-breaks the lines into 18 systems
    /// and pages them 6/6/6, its page breaker choosing the line breaking); under the cap both
    /// sides break into 17 and page 1 holds six systems on both.
    /// </para>
    /// </remarks>
    private static readonly LayoutOptions SixSystemsPerPage =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with { MaxSystemsPerPage = 6 },
        };

    /// <summary>
    /// Eight systems to a page — enough of this music that the page must SQUEEZE them.
    /// </summary>
    /// <remarks>
    /// Written as a cap for the same reason <see cref="SixSystemsPerPage"/> is, and it is
    /// what the breaker chooses here unaided on both sides; pinning it keeps the entry a
    /// measurement of the spring rather than of the page breaker.
    /// </remarks>
    private static readonly LayoutOptions EightSystemsPerPage =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with { MaxSystemsPerPage = 8 },
        };

    /// <summary>
    /// One staff with a lyric row under it, over enough music to fill several pages — the
    /// mirror of books LYRS (justified) and LYRC (ragged-bottom).
    /// </summary>
    /// <remarks>
    /// ⚠️ THE MELODY IS HIGH AND THE SYLLABLE HAS NO ASCENDER, and both are load bearing:
    /// each engraver's placement is a max of a basic distance and an INK floor, and a reading
    /// that lands on either floor is a measurement of the font, whose lyric faces differ by
    /// about 27% (HANDOFF 5.3). Measured, both ways round: with the melody on c' LilyPond's
    /// floor is 5.865115 and beats its basic-distance 5.5; with the syllable <c>la</c> Lily#
    /// reads 4.662000, its own glyph-height floor (<c>l</c> is an ascender), rather than its
    /// own basic distance. Either one makes the residual carry two models at once and name no
    /// defect. High melody plus <c>no</c> puts both sides on their basic distances.
    /// <para>
    /// Four syllables to the bar against four quarter notes, which is where the two engravers
    /// agree by construction: Lily# spreads a row's syllables evenly across the bar while
    /// LilyPond reads their written durations (the same reason the staff-less pair uses two
    /// equal syllables per bar).
    /// </para>
    /// <para>
    /// 120 bars, and that is load bearing rather than generous: the first draft had 40 and
    /// LilyPond re-broke it onto a SINGLE page, where <c>ragged-last-bottom</c> (default true)
    /// left it unstretched — so the "stretched" book was measuring a ragged one, with nothing
    /// to say so. The count entry is the guard that would now catch it.
    /// </para>
    /// </remarks>
    /// <param name="scoreBody">
    /// How the lyric line is SPELLED — the one thing the three books built from this differ
    /// in. <c>staff melody with lyrics words</c> is note-bound; a bare <c>lyrics words</c>
    /// under the staff is an independent ROW, which Lily# lays out through a different model
    /// entirely. LilyPond has one model for both, which is what makes the third book a
    /// LilyPond-side identity (see the <c>lyrics.row.*</c> entry).
    /// </param>
    private static string LyricRowPageScore(string name, string scoreBody) => $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section Main {
          melody { {{string.Concat(Enumerable.Repeat("g'4 a' g' a' | ", 120)).Trim()}} }
          lyrics words { {{string.Concat(Enumerable.Repeat("no no no no | ", 120)).Trim()}} }
        }

        form main { ~Main }

        score main "{{name}}" {
        {{scoreBody}}
        }
        """;

    /// <summary>The justified twin — the mirror of book LYRS.</summary>
    private static readonly string LYRS =
        LyricRowPageScore("LYRS", "  staff melody with lyrics words");

    /// <summary>The ragged-bottom control — the mirror of book LYRC.</summary>
    /// <remarks>Same music, built by the same call (HANDOFF 5.0).</remarks>
    private static readonly string LYRC =
        LyricRowPageScore("LYRC", "  staff melody with lyrics words");

    /// <summary>
    /// The same music and the same paper as <see cref="LYRC"/>, with the lyric line spelled
    /// as an independent ROW instead of note-bound — the mirror of book LYRR.
    /// </summary>
    /// <remarks>
    /// The spelling is the ONLY difference, and it is the whole point: LilyPond reads both
    /// books identically (a Lyrics context is a Lyrics context; association changes which
    /// column a syllable stands on, not what the vertical spacing spec is), so its side of
    /// this comparison is an identity and any difference Lily# shows between the two is its
    /// own. Carried as <c>lyrics.row.staff-to-lyric</c> since 2026-07-27, when the band model
    /// this book was built to measure was retired and the distance stopped being a decision;
    /// <see cref="LpGeometryLedgerTests.LyricRowIsSpacedLikeTheLyricsContextItIs"/> asserts
    /// the identity itself, which no single entry can.
    /// </remarks>
    internal static readonly string LYRR =
        LyricRowPageScore("LYRR", "  staff melody\n  lyrics words");

    /// <summary>
    /// The same music and paper again with a SECOND verse — the mirror of book LYRV.
    /// </summary>
    /// <remarks>
    /// Two <c>with lyrics</c> clauses is how Lily# stacks verses (docs/GRAMMAR.md), and it
    /// is a different quantity from everything above: LilyPond spaces a second loose line
    /// from the first by <c>nonstaff-nonstaff-spacing</c>, not by the
    /// <c>nonstaff-relatedstaff-spacing</c> that holds the first under its staff
    /// (page-layout-problem.cc:1315-1332).
    /// <para>
    /// ⚠️ NOT a control for the staff-to-lyrics distance, even though it is the same music:
    /// two lyric lines no longer fit in the 12.000000 the system spring keeps, so LilyPond
    /// SOLVES THE LOOSE CHAIN AT A NEGATIVE FORCE and the first line drops to its ink floor
    /// (3.737890 on this book's inner systems, against 5.500000 on LYRC). The verse step is
    /// readable there anyway because it is rigid — see the ledger entry.
    /// </para>
    /// </remarks>
    private static string LyricVersePageScore(string name) => $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section Main {
          melody { {{string.Concat(Enumerable.Repeat("g'4 a' g' a' | ", 120)).Trim()}} }
          lyrics one { {{string.Concat(Enumerable.Repeat("no no no no | ", 120)).Trim()}} }
          lyrics two { {{string.Concat(Enumerable.Repeat("no no no no | ", 120)).Trim()}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff melody with lyrics one with lyrics two
        }
        """;

    /// <summary>The two-verse book — the mirror of book LYRV.</summary>
    private static readonly string LYRV = LyricVersePageScore("LYRV");

    /// <summary>
    /// <see cref="LYRV"/>'s music with the two verses spelled as ONE independent ROW — the
    /// mirror of book LYRRV.
    /// </summary>
    /// <remarks>
    /// The same 960 syllables over the same 120 bars on the same paper; only the CONTAINER
    /// differs, and that is the measurement. A row auto-wraps a block longer than the section
    /// into stacked verses (<c>LyricsCollector.CollectRow</c>), so 240 written bars against
    /// 120 of music is two verses standing on the same columns LilyPond's two Lyrics contexts
    /// stand on.
    /// <para>
    /// ⚠️ WHY THIS BOOK AND NOT <see cref="LYRR"/>, which asks the same question: LYRR asks it
    /// where nothing binds. With ONE loose line the springs on its non-own side carry
    /// LARGE_STRETCH/HUGE_STRETCH (page-layout-problem.cc:1257-1338), so the line sits at its
    /// ideal whatever the page does, and Lily# reads LYRC and LYRR identically on every
    /// quantity except the decided band distance — the pair measures the DECISION and nothing
    /// else. LYRV's regime is the one where the loose chain is critically compressed (sum of
    /// minimums = the 12.000000 system gap, bisected in the .ly probe's header), so there
    /// every term is load bearing. This is that regime with the row model in it.
    /// </para>
    /// </remarks>
    private static string LyricRowVersePageScore(string name) => $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section Main {
          melody { {{string.Concat(Enumerable.Repeat("g'4 a' g' a' | ", 120)).Trim()}} }
          lyrics words { {{string.Concat(Enumerable.Repeat("no no no no | ", 240)).Trim()}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff melody
          lyrics words
        }
        """;

    /// <summary>The two-verse ROW book — the mirror of book LYRRV.</summary>
    internal static readonly string LYRRV = LyricRowVersePageScore("LYRRV");

    /// <summary>The paper books LYRC/LYRR/LYRV are measured on.</summary>
    internal static LayoutOptions LyricRowOptions => FourSystemsPerPageRagged;

    /// <summary>The note-bound spelling of the same music — book LYRC.</summary>
    internal static string LyricNoteBoundScore => LYRC;

    /// <summary>The note-bound TWO-VERSE spelling — book LYRV, <see cref="LYRRV"/>'s twin.
    /// LilyPond reads the two identically, so any difference Lily# shows is its own.</summary>
    internal static string LyricVerseScore => LYRV;

    /// <summary>
    /// A notation staff over a LOWER staff that is either a TAB staff or an ordinary one —
    /// the Lily# half of books TABS / NST.
    /// </summary>
    /// <remarks>
    /// The two differ only in <paramref name="render"/>, which is the measurement: LilyPond
    /// reads BOTH at 9.000000 refpoint to refpoint (measured 2026-07-28), so its side is a
    /// control and any difference Lily# shows between them is its own.
    /// <para>
    /// ⚠️ THE MUSIC IS THE SAME AND THE OCTAVES ARE THE PROBE'S, not the .ly's: Lily# writes
    /// one octave lower than LilyPond (LilyPond <c>g'</c> is Lily# <c>g</c>), so the .ly's
    /// <c>g'4 a'</c> over <c>g4 a</c> is <c>g4 a</c> over <c>g,4 a,</c> here.
    /// </para>
    /// </remarks>
    private static string TabPairScore(string name, string render) => $$"""
        octave absolute
        time 4/4
        key c major

        part upper { clef treble }
        part lower { clef treble_8 tuning guitar }

        section A {
          upper { {{string.Concat(Enumerable.Repeat("g4 a g a | ", 8)).Trim()}} }
          lower { {{string.Concat(Enumerable.Repeat("g,4 a, g, a, | ", 8)).Trim()}} }
        }

        form main { A }

        score main "{{name}}" {
          staff upper
          {{render}} lower
        }
        """;

    /// <summary>The tab half — book TABS.</summary>
    internal static readonly string TabPairScoreTab = TabPairScore("TABS", "tab");

    /// <summary>The notation control — book NST.</summary>
    internal static readonly string TabPairScoreNotation = TabPairScore("NST", "staff");

    /// <summary>
    /// The SAME part twice, the upper one rendered either as an OSSIA or as an ordinary
    /// staff — the Lily# half of books OSSU / OSSUN.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pair differs in ONE WORD, and that word is the measurement. LilyPond reads both
    /// arrangements at 9.000000 refpoint to refpoint (measured 2026-07-28,
    /// page-vertical.ly OSSU/OSSUN): it scales the STAFF and not the DISTANCE, so its side
    /// is an identity and whatever difference Lily# shows between the two halves is its own.
    /// </para>
    /// <para>
    /// ⚠️ THE OSSIA IS WRITTEN FIRST so that it sits ABOVE, which is the arrangement both
    /// LilyPond books use (<c>alignAboveContext</c>) and the only one Lily#'s <c>ossia</c>
    /// has. <see cref="RenderSpec"/> would hoist it there anyway, but writing it in place
    /// keeps the two halves textually identical apart from the render word.
    /// </para>
    /// <para>
    /// ⚠️ THE OCTAVES ARE THE PROBE'S: LilyPond <c>g'</c> is Lily# <c>g</c>, so the .ly's
    /// <c>g'4 a'</c> is <c>g4 a</c> here. Both staves carry the same pitches for the same
    /// reason the .ly does — the music is kept inside the staves so that the FLOOR does not
    /// bind and both readings are of the SPEC.
    /// </para>
    /// </remarks>
    /// <param name="bars">How much music, which is what puts the pair in one regime or the
    /// other: eight bars is one system on a page with slack (OSSU/OSSUN, every spring at its
    /// ideal), 120 is more systems than the page holds (OSSK/OSSKN, every spring solved).
    /// Defaulted, so the two books at rest are spelled exactly as they were.</param>
    private static string OssiaPairScore(string name, string render, int bars = 8) => $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section A {
          melody { {{string.Concat(Enumerable.Repeat("g4 a g a | ", bars)).Trim()}} }
          upper  { {{string.Concat(Enumerable.Repeat("g4 a g a | ", bars)).Trim()}} }
        }

        form main { ~A }

        score main "{{name}}" {
          {{render}} upper
          staff melody
        }
        """;

    /// <summary>The ossia half — book OSSU.</summary>
    internal static readonly string OssiaPairScoreOssia = OssiaPairScore("OSSU", "ossia");

    /// <summary>The full-size control — book OSSUN.</summary>
    internal static readonly string OssiaPairScoreNotation = OssiaPairScore("OSSUN", "staff");

    /// <summary>
    /// The same pair over enough music to fill pages — the Lily# half of books OSSK / OSSKN,
    /// which ask whether an ossia's distance is a SPRING at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OssiaPairScoreOssia"/> reads its distance at REST, where a rigid pair and a
    /// solved one are the same number, so it cannot see that
    /// <c>MultiStaffLayouter.StaffSprings</c> skips an ossia pair outright. On a page that
    /// must squeeze, LilyPond compresses the ossia's distance like any other staff pair
    /// (measured 2026-07-28: OSSK 8.787816 against its own ideal 9, one force per page with
    /// its system springs), because an ossia is a <c>\new Staff</c> and therefore a SPACEABLE
    /// staff — its VerticalAxisGroup prints <c>aff=()</c> in the same dump.
    /// </para>
    /// <para>
    /// ⚠️ TWO DEFECTS SHOW HERE, not one, and they are separate quantities. The pair is rigid
    /// in Lily# (the skip), AND when it is spaced at all it is spaced by the GROUPED spec:
    /// <c>MultiStaffLayouter</c> substitutes <c>sp.StaffStaff</c> whenever either side is an
    /// ossia (:130-131, :222-223) where LilyPond falls through to the VerticalAxisGroup's own
    /// <c>default-staff-staff-spacing</c> (<c>axis-group-interface.cc:1007-1027</c>). Both
    /// specs declare basic-distance 9, so at rest they agree to the digit; their MINIMA are 7
    /// and 8, so a compressed page separates them.
    /// </para>
    /// </remarks>
    internal static readonly string OssiaCompressedScoreOssia =
        OssiaPairScore("OSSK", "ossia", 120);

    /// <summary>The full-size control of the compressed pair — book OSSKN.</summary>
    internal static readonly string OssiaCompressedScoreNotation =
        OssiaPairScore("OSSKN", "staff", 120);

    /// <summary>
    /// The SAME guitar part alone on a page of its own, as either a tab staff or an ordinary
    /// one — the Lily# half of books TABL / NTL.
    /// </summary>
    /// <remarks>
    /// TABS/NST measure a distance INSIDE one system; this pair measures the PAGE's own
    /// anchors against the same staff — where the first refpoint lands below the paper edge,
    /// and how far apart consecutive systems sit. One staff and many systems, so every
    /// distance read from it is the page's.
    /// <para>
    /// ⚠️ THE SECTION REFERENCE IS SILENT (<c>~Main</c>). A printed rehearsal mark is ~3.86 ss
    /// of ink landing exactly where the first-refpoint reading looks; that is what made the
    /// first draft of probe <see cref="V"/> read 14.350551 against LilyPond's 11.690551.
    /// </para>
    /// <para>
    /// ⚠️ Octaves are the probe's, as everywhere here: LilyPond <c>g</c> is Lily# <c>g,</c>.
    /// The part is spelled exactly as <see cref="TabPairScore"/>'s lower one, so the notation
    /// control draws the same ledger lines below the staff that the .ly's clef-less
    /// <c>\new Staff</c> does.
    /// </para>
    /// </remarks>
    private static string TabPageScore(string name, string render) => $$"""
        octave absolute
        time 4/4
        key c major

        part gtr { clef treble_8 tuning guitar }

        section Main {
          gtr { {{string.Concat(Enumerable.Repeat("g,4 a, g, a, | ", 24)).Trim()}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          {{render}} gtr
        }
        """;

    /// <summary>The tab half — book TABL.</summary>
    private static readonly string TABL = TabPageScore("TABL", "tab");

    /// <summary>The notation control — book NTL.</summary>
    private static readonly string NTL = TabPageScore("NTL", "staff");

    private static string CoexistScore(string name, string scoreBlock) => $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section Main {
          melody { {{string.Concat(Enumerable.Repeat("g'4 a' g' a' | ", 40)).Trim()}} }
          lyrics one { {{string.Concat(Enumerable.Repeat("no no no no | ", 40)).Trim()}} }
          lyrics two { {{string.Concat(Enumerable.Repeat("no no no no | ", 40)).Trim()}} }
        }

        form main { ~Main }

        score main "{{name}}" {
        {{scoreBlock}}
        }
        """;

    /// <summary>
    /// A note-bound line and an independent ROW under the SAME staff — two Lyrics contexts
    /// where only the second one's association differs.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ARRANGEMENT THAT SPLITS LILY#'S MODEL IN TWO. Lily# makes a row a staff GROUP of
    /// its own, so the staff carrying the note-bound line stops being in the last group and
    /// its syllables move to the inter-group chain while the row is solved below the system —
    /// two chains, one room. LilyPond has one run for both (page-layout-problem.cc:919-925).
    /// <see cref="NoteBoundVersesScore"/> is the same music with the second line note-bound
    /// as well, which is what makes LilyPond's side of the comparison an identity.
    /// </remarks>
    internal static readonly string RowUnderNoteBoundScore = CoexistScore(
        "coexist-row", "  staff melody with lyrics one\n  lyrics two");

    /// <summary>The same music with BOTH lines note-bound — the twin of
    /// <see cref="RowUnderNoteBoundScore"/>.</summary>
    internal static readonly string NoteBoundVersesScore = CoexistScore(
        "coexist-bound", "  staff melody with lyrics one with lyrics two");

    /// <summary>Four systems to a page, so page 1 keeps real slack and STRETCHES.</summary>
    private static readonly LayoutOptions FourSystemsPerPage =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with { MaxSystemsPerPage = 4 },
        };

    /// <summary>The same paper with vertical justification off — the control's regime.</summary>
    private static readonly LayoutOptions FourSystemsPerPageRagged =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with
            {
                MaxSystemsPerPage = 4,
                RaggedBottom = true,
            },
        };

    /// <summary>The same paper with vertical justification off — the control's regime.</summary>
    private static readonly LayoutOptions SixSystemsPerPageRagged =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with
            {
                MaxSystemsPerPage = 6,
                RaggedBottom = true,
            },
        };

    /// <summary>
    /// The same note-bound lyric line under a TWO-staff system — the mirror of book LYRM.
    /// </summary>
    /// <remarks>
    /// A Lyrics line has staff-affinity UP, so <c>nonstaff-relatedstaff-spacing</c> runs from
    /// the staff DIRECTLY ABOVE it — the system's LAST spaceable staff, which
    /// page-layout-problem.cc:943-944 records as <c>last_spaceable_line</c>. Putting another
    /// staff ABOVE that one cannot change the distance: it is the same spring between the
    /// same two VerticalAxisGroups on the same music. So LilyPond's side of the
    /// LYRC/LYRM comparison is an IDENTITY, and whatever Lily# reads differently is its own.
    /// <para>
    /// ⚠️ Lily# anchors a note-bound block <c>staffBottom</c> below the SYSTEM ORIGIN — the
    /// TOP staff's top line — and lets the skyline drop push it clear of whatever is beneath.
    /// On a one-staff system that IS the last staff, which is why
    /// <c>lyrics.natural.staff-to-lyric</c> has been exact for sessions. Here they are a
    /// whole staff apart and the basic-distance 5.5 stops binding at all.
    /// </para>
    /// <para>
    /// The bottom staff's melody stays inside the staff so its own ink cannot bind (the
    /// alignment minimum is 3.737890 against a basic-distance of 5.5), and the top staff
    /// carries LYRC's high melody so the system's up-ink is unchanged from it.
    /// ⚠️ Lily# <c>g</c> is LilyPond <c>g'</c> (HANDOFF 5.5).
    /// </para>
    /// </remarks>
    private static string LyricTwoStaffPageScore(string name, bool secondVerse)
    {
        string syllables = string.Concat(Enumerable.Repeat("no no no no | ", 120)).Trim();
        return $$"""
            octave absolute
            time 4/4
            key c major

            part upper { clef treble }
            part melody { clef treble }

            section Main {
              upper { {{string.Concat(Enumerable.Repeat("g'4 a' g' a' | ", 120)).Trim()}} }
              melody { {{string.Concat(Enumerable.Repeat("g4 a g a | ", 120)).Trim()}} }
              lyrics one { {{syllables}} }{{(secondVerse ? $"\n  lyrics two {{ {syllables} }}" : "")}}
            }

            form main { ~Main }

            score main "{{name}}" {
              staff upper
              staff melody with lyrics one{{(secondVerse ? " with lyrics two" : "")}}
            }
            """;
    }

    /// <summary>The one-verse book — the mirror of book LYRM.</summary>
    private static readonly string LYRM = LyricTwoStaffPageScore("LYRM", secondVerse: false);

    /// <summary>
    /// LYRM WITH A CHORD ROW — the mirror of book LYRMC, and the book that says the
    /// remaining force-0 decline is NOT a guard that can be narrowed.
    /// </summary>
    /// <remarks>
    /// LYRCH's row sits ABOVE the anchor staff and so outside the span the room covers,
    /// which is why narrowing <c>ComputeBetweenStavesEnd</c> closed it. Here the lyrics hang
    /// under the system's LAST staff, so the room runs to the NEXT SYSTEM's first staff —
    /// and that system's chord row is INSIDE it. LilyPond puts the row into the very chain
    /// the lyrics are distributed by (page-layout-problem.cc:948-990).
    /// <para>
    /// ★ MEASURED, AND IT DECIDES THE FORK: LilyPond reads 4.608814 where LYRM reads
    /// 5.500000, with <c>system-to-system</c> 12.000000 in BOTH. The room did not grow for
    /// the row; the row is squeezed into it alongside the lyrics, and the lyric line is
    /// pulled CLOSER to its staff because one more spring in a fixed room compresses the
    /// solve. ⇒ THE ROW AND THE LYRICS SHARE ONE ROOM. Lily# gives the row a band of its own
    /// (HANDOFF 3), and a band cannot be squeezed by somebody else's chain — so
    /// <c>BuildLooseChainEnds</c>' decline is honest here, and closing it means MOVING THAT
    /// DECISION rather than narrowing a guard.
    /// ⚠️ THIS PARAGRAPH ONCE ENDED "a judgement call, not a port" AND THAT WAS WRONG
    /// (corrected 2026-07-27). The policy is literal porting and does not bend for a Lily#
    /// model: page-layout-problem.cc:948-990 pushes every non-spaceable staff onto
    /// <c>loose_lines</c> and closes the run on the next spaceable one, so the row IS in the
    /// Lyrics' chain. "Lily# models it as a band" is the thing to change, not a reason to
    /// stop — and because the port moves where the row SITS, the two are one island.
    /// </para>
    /// <para>
    /// ★ THE CHAIN IS DECOMPOSED TERM BY TERM in the probe header and in this point's
    /// ledger <c>why</c>: room 12.000000 = s0 4.608814 (nonstaff-relatedstaff, off its floor)
    /// + s1 0.837966 (= 1 + f) + s2 2.973743 (at its minimum) + s3 3.579477 (at its
    /// minimum), f = -0.162033841. ⚠️ s3's spring is the ChordNames' OWN
    /// <c>nonstaff-relatedstaff-spacing</c>, which declares only <c>(padding . 0.5)</c>
    /// (ly/engraver-init.ly:722) — its ideal 1.0 is the caller's <c>Spring (1.0, 0.0)</c>,
    /// NOT the Lyrics' 5.5. A port that reuses the Lyrics' spec builds a different spring
    /// under the same property name.
    /// </para>
    /// <para>
    /// ⚠️ ONLY THE FIRST OF LilyPond's two readings moves; the last system on a page runs its
    /// chain to the page edge with no row between, and reads LYRM's 5.500001. So the defect
    /// is per-chain, not per-score — which is the other half of what
    /// <c>BuildLooseChainEnds</c>' own remark suspected when it called the whole-score
    /// bail-out coarser than it needs to be.
    /// </para>
    /// </remarks>
    private static string LyricTwoStaffChordRowScore(string name)
    {
        string syllables = string.Concat(Enumerable.Repeat("no no no no | ", 120)).Trim();
        return $$"""
            octave absolute
            time 4/4
            key c major

            part upper { clef treble }
            part melody { clef treble }

            section Main {
              upper { {{string.Concat(Enumerable.Repeat("g'4 a' g' a' | ", 120)).Trim()}} }
              melody { {{string.Concat(Enumerable.Repeat("g4 a g a | ", 120)).Trim()}} }
              lyrics one { {{syllables}} }
              chords prog { {{string.Concat(Enumerable.Repeat("c1 | ", 120)).Trim()}} }
            }

            form main { ~Main }

            score main "{{name}}" {
              chords prog
              staff upper
              staff melody with lyrics one
            }
            """;
    }

    /// <inheritdoc cref="LyricTwoStaffChordRowScore"/>
    private static readonly string LYRMC = LyricTwoStaffChordRowScore("LYRMC");

    /// <summary>
    /// The same two-staff system with a SECOND verse — the mirror of book LYRMV.
    /// </summary>
    /// <remarks>
    /// Book LYRV's loose chain with a staff added ABOVE the one the lyrics hang from, and
    /// LilyPond's side is an IDENTITY WITH LYRV in the SOURCE rather than by observation:
    /// <c>distribute_loose_lines</c> is handed <c>last_spaceable_line_translation</c> and
    /// <c>-solution_[spring_idx]</c> (page-layout-problem.cc:936-939) — the previous
    /// spaceable staff's position on the page and this one's, both out of the page's own
    /// spring chain, neither knowing which system it belongs to. An added staff joins THAT
    /// chain, between two positions the loose chain never reads.
    /// <para>
    /// MEASURED, and every reading is LYRV's or LYRM's to six digits: the system gap
    /// 12.000000, the loose chain {3.737890, 2.800000, 5.500001}, the inside-system
    /// distance 9.000000, four systems (eight staves) on page 1.
    /// </para>
    /// <para>
    /// ⚠️ THE PARAGRAPH THAT STOOD HERE IS STALE AND HAS BEEN CORRECTED. It said
    /// <c>LayoutEngine.BuildLooseChainEnds</c> returns null for the whole score as soon as a
    /// system holds more than one staff, so every chain on this book ran at force 0. That was
    /// true when the entry was opened and stopped being true in <c>90e47848</c>, which moved
    /// the room to the refpoint frame: a multi-staff system's chain is now solved like a
    /// one-staff system's. What is left is <b>+0.271310</b> and it is the two lyric faces
    /// rather than a mechanism — see the ledger's <c>why</c>, and ⚠️ do not drive it to zero,
    /// since landing on LilyPond's 3.737890 would mean a font quantity had been fitted.
    /// </para>
    /// </remarks>
    private static readonly string LYRMV = LyricTwoStaffPageScore("LYRMV", secondVerse: true);

    /// <summary>
    /// The lyric block BETWEEN two staves of one system — the mirror of books LYRB/LYRBV.
    /// </summary>
    /// <remarks>
    /// Every other two-staff lyric book here puts the melody on the LOWER staff, so its block
    /// hangs from the system's last staff and closes on the NEXT SYSTEM's first one. This one
    /// swaps them, which is the branch <see cref="LyricEngraver.DistributeLooseLines"/> names
    /// as still running at force 0: the chain closes on a staff of the SAME system through
    /// <c>nonstaff-unrelatedstaff-spacing</c> + LARGE_STRETCH (page-layout-problem.cc:1299-1312)
    /// and there is no null line (:923-925).
    /// <para>
    /// MEASURED, and TWO PREDICTIONS OUT OF FIVE MISSED — see the probe header, which records
    /// both and why they are the useful half. The closing minimum is a SKYLINE distance and
    /// neither end of it is what the arithmetic assumed: the next staff's up-skyline is its
    /// CLEF and its stems (up-extent 3.800000), not its top line, and the down-skyline that
    /// meets it is the accumulated one — every staff and verse ABOVE, raised by the distances
    /// already fixed — so it is a different number with one verse (4.972149) and with two
    /// (4.535174). LilyPond: one verse 4.027851 into a room of 9.000000, two verses
    /// 3.737890 + 2.800000 into 11.073064.
    /// </para>
    /// <para>
    /// ⚠️ BOTH STAVES CARRY g'/a', which is not the same choice as LYRM/LYRMV. There the
    /// upper staff was made high to hold the SYSTEM's up-ink constant against the one-staff
    /// books; here the quantity under test is the gap BELOW the lyrics, so the staff that
    /// must stay plain is the lower one. ⚠️ Lily# <c>g</c> is LilyPond <c>g'</c> (HANDOFF 5.5).
    /// </para>
    /// </remarks>
    private static string LyricBetweenStavesPageScore(string name, bool secondVerse)
    {
        string syllables = string.Concat(Enumerable.Repeat("no no no no | ", 120)).Trim();
        string bars = string.Concat(Enumerable.Repeat("g4 a g a | ", 120)).Trim();
        return $$"""
            octave absolute
            time 4/4
            key c major

            part melody { clef treble }
            part lower { clef treble }

            section Main {
              melody { {{bars}} }
              lower { {{bars}} }
              lyrics one { {{syllables}} }{{(secondVerse ? $"\n  lyrics two {{ {syllables} }}" : "")}}
            }

            form main { ~Main }

            score main "{{name}}" {
              staff melody with lyrics one{{(secondVerse ? " with lyrics two" : "")}}
              staff lower
            }
            """;
    }

    /// <summary>The one-verse book — the mirror of book LYRB.</summary>
    private static readonly string LYRB = LyricBetweenStavesPageScore("LYRB", secondVerse: false);

    /// <summary>The two-verse book — the mirror of book LYRBV.</summary>
    private static readonly string LYRBV = LyricBetweenStavesPageScore("LYRBV", secondVerse: true);

    /// <summary>
    /// LYRB WITH A CHORD ROW ADDED — the mirror of book LYRCH, and the control for the last
    /// branch of the loose chain Lily# still lays out at force 0.
    /// </summary>
    /// <remarks>
    /// <c>LayoutEngine.BuildLooseChainEnds</c> and <c>ComputeBetweenStavesEnd</c> both return
    /// null as soon as the system carries a text ROW, so the lyric block gets
    /// room = +infinity and every spring sits at <c>max(min, ideal)</c> — 5.500000 — however
    /// tight the page is. This book is LYRB with one <c>chords</c> row added and nothing else
    /// changed.
    /// <para>
    /// ★★ LILYPOND IS THE IDENTITY HERE, MEASURED, which is the strongest form a pair can
    /// take (HANDOFF 5.0) and it arrived by accident: book LYRCH prints LYRB's numbers to six
    /// digits on EVERY spacing quantity in the dump — the chain 4.027851, the inside distance
    /// 9.000000, system-to-system 12.000000, four systems on page 1 — and differs only in
    /// where the first ink starts, which is the chord row's own glyphs. So LilyPond's
    /// difference is ZERO and whatever Lily# reads differently is the defect outright, with
    /// no font quantity and no page-breaking term in it.
    /// </para>
    /// <para>
    /// ⚠️ THE FALSIFIER DID NOT FIRE. It was 5.500000 on LilyPond's side, which would have
    /// meant LilyPond leaves the chain at force 0 too and Lily#'s branch is right by
    /// accident; the item would then close by deletion rather than by a port. It reads
    /// 4.027851, so the branch is a defect.
    /// </para>
    /// <para>
    /// ⚠️ ITS SIBLING LYROS (an OSSIA instead of a chord row) IS MEASURED IN THE PROBE BUT NOT
    /// WIRED HERE. Same chain reading, 4.027851, but its inside distance is 18.000000 rather
    /// than 9.000000 — <c>staff-refpoint-extent</c> spans every SPACEABLE staff, and an ossia
    /// is a real <c>\new Staff</c> to LilyPond, so there are three of them. ⇒ AN OSSIA IS
    /// SPACEABLE IN LILYPOND AND IS NOT IN LILY#, a second and different defect that has to
    /// be decided before its points mean anything. The probe header carries the numbers.
    /// </para>
    /// </remarks>
    private static string LyricChordRowPageScore(string name)
    {
        string syllables = string.Concat(Enumerable.Repeat("no no no no | ", 120)).Trim();
        string bars = string.Concat(Enumerable.Repeat("g4 a g a | ", 120)).Trim();
        string chords = string.Concat(Enumerable.Repeat("c1 | ", 120)).Trim();
        return $$"""
            octave absolute
            time 4/4
            key c major

            part melody { clef treble }
            part lower { clef treble }

            section Main {
              melody { {{bars}} }
              lower { {{bars}} }
              lyrics one { {{syllables}} }
              chords prog { {{chords}} }
            }

            form main { ~Main }

            score main "{{name}}" {
              chords prog
              staff melody with lyrics one
              staff lower
            }
            """;
    }

    /// <inheritdoc cref="LyricChordRowPageScore"/>
    private static readonly string LYRCH = LyricChordRowPageScore("LYRCH");

    /// <summary>
    /// LYRB WITH AN OSSIA ADDED — the mirror of book LYROS, and the other half of the
    /// force-0 branch: <c>ComputeBetweenStavesEnd</c> declined an ossia for the same reason
    /// it declined a text row.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONLY THE CHAIN READING IS WIRED. LilyPond's inside-system distance on this book is
    /// 18.000000, not LYRB's 9.000000, because <c>staff-refpoint-extent</c> spans every
    /// SPACEABLE staff (lily/system.cc:705-717) and an ossia is a real <c>\new Staff</c> to
    /// LilyPond — there are three. ⇒ AN OSSIA IS SPACEABLE THERE AND IS NOT HERE
    /// (<c>MultiStaffLayouter.StaffSprings</c> skips it), a second and different defect, so
    /// the inside reading is not like-for-like and is deliberately not a point.
    /// <para>
    /// The chain reading IS like-for-like: LilyPond solves it to 4.027851, LYRB's own number
    /// to six digits, exactly as it does with a chord row. The ossia sits ABOVE the anchor
    /// staff, so it is not in the span between the anchor and the staff below it.
    /// </para>
    /// </remarks>
    private static string LyricOssiaPageScore(string name)
    {
        string syllables = string.Concat(Enumerable.Repeat("no no no no | ", 120)).Trim();
        string bars = string.Concat(Enumerable.Repeat("g4 a g a | ", 120)).Trim();
        return $$"""
            octave absolute
            time 4/4
            key c major

            part melody { clef treble }
            part ossia_mel { clef treble }
            part lower { clef treble }

            section Main {
              melody { {{bars}} }
              ossia_mel { {{bars}} }
              lower { {{bars}} }
              lyrics one { {{syllables}} }
            }

            form main { ~Main }

            score main "{{name}}" {
              staff melody with lyrics one
              ossia ossia_mel
              staff lower
            }
            """;
    }

    /// <inheritdoc cref="LyricOssiaPageScore"/>
    private static readonly string LYROS = LyricOssiaPageScore("LYROS");

    /// <summary>The between-staves books, for the mechanism assertion beside the ledger.</summary>
    internal static string BetweenStavesOneVerseScore => LYRB;

    /// <inheritdoc cref="BetweenStavesOneVerseScore"/>
    internal static string BetweenStavesTwoVerseScore => LYRBV;

    /// <summary>
    /// LYRMV WITH THE UPPER STAFF REMOVED UNDER ONE SYSTEM — the mirror of book LYRHK, and
    /// the only book in the corpus whose staff count is not constant down the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The question is whether the room a loose chain is solved into belongs to the SYSTEM the
    /// block hangs under or to the SCORE. Every other lyric book is uniform, so reading the
    /// origin-to-last-spaceable-staff span off <c>systemsArray[0]</c> and reading it per system
    /// give the same answer on all of them; <c>c64ee958</c> went per system and reported that
    /// nothing measured the difference. This book is where the two cannot agree.
    /// </para>
    /// <para>
    /// LILYPOND'S SIDE IS AN IDENTITY WITH LYRMV, in the SOURCE: <c>distribute_loose_lines</c>
    /// is handed <c>last_spaceable_line_translation</c> and <c>-solution_[spring_idx]</c>
    /// (page-layout-problem.cc:936-939), two members of the PAGE's spring chain. A staff
    /// hara-kiri removed is not in that chain, and a surviving staff is in it whether or not
    /// the neighbouring system kept one — so no staff count can reach either end of the room.
    /// </para>
    /// <para>
    /// THE SHAPE IS FORCED WITH EXPLICIT BREAKS rather than left to the line breaker: "the
    /// upper staff rests through exactly system 0" is otherwise a bet on both engines choosing
    /// the same bar, and HANDOFF 5.0 traps 5 and 6 are both that bet being lost. Six bars to a
    /// system: system 0 one staff, systems 1..3 two, so page 1 carries SEVEN staff refpoints
    /// and the block under system 1 is the reading that separates the implementations — its
    /// anchor is that system's LAST staff, nine staff spaces below its origin, where a
    /// score-wide span would take system 0's zero and hand the chain nine spaces it has not
    /// got.
    /// </para>
    /// <para>
    /// ⚠️ Silent bars are <c>r1</c> and not <c>R1</c>: how many bars a multi-measure rest
    /// swallows is answered differently by the two engines, and while the staff is removed
    /// either way, the pair would then differ in something besides the quantity under test.
    /// ⚠️ Lily# <c>g</c> is LilyPond <c>g'</c> (HANDOFF 5.5).
    /// </para>
    /// </remarks>
    private static readonly string LYRHK = BuildHaraKiriLyricScore();

    /// <summary>
    /// HARA-KIRI INSIDE A GROUPER — the mirror of book LYRHKG, and the book that stops
    /// LYRHK's defect being closed with a literal.
    /// </summary>
    /// <remarks>
    /// A grand staff of A (always playing) and B (removeEmpty, silent through system 0) over
    /// a bare staff C carrying the melody and two verses. Both 9 and 10.5 must appear, and
    /// WHICH APPEARS WHERE depends on which staves are still alive: LilyPond reads the
    /// spacing property off the staff above the gap (page-layout-problem.cc:1280-1281) and
    /// then asks whether that staff still has a LIVE spaceable member below it inside its
    /// grouper (axis-group-interface.cc:1008-1027). Killing B therefore PROMOTES A to last
    /// live member and changes the spec of the gap that survives — 10.5 under system 0
    /// against 9 then 10.5 under the others. A fix that writes 9 where
    /// <c>LayoutEngine</c> writes 10.5 passes LYRHK, whose staves are both bare, and fails
    /// here.
    /// <para>⚠️ Lily# <c>g</c> is LilyPond <c>g'</c> (HANDOFF 5.5).</para>
    /// </remarks>
    private static readonly string LYRHKG = BuildHaraKiriGrouperScore();

    /// <summary>
    /// THE DECLARATION ON ITS OWN — the mirrors of books LYRHKD and LYRHKN, which are the
    /// same music differing ONLY in whether the upper staff declares <c>removeEmpty</c>.
    /// </summary>
    /// <remarks>
    /// No staff is ever empty in either, so the declaration cannot fire and LILYPOND'S TWO
    /// READINGS ARE IDENTICAL BY CONSTRUCTION: its hara-kiri is a suicide followed by a
    /// live-filter (page-layout-problem.cc:1366-1370, align-interface.cc:90), and a grob
    /// that never dies leaves no trace. Whatever Lily# reads differently between them is
    /// entirely its own, and needs no force arithmetic to interpret.
    /// <para>
    /// ⚠️ WHY LYRHK CANNOT SERVE. Lily# branches on the DECLARATION (<c>hasHaraKiri</c>, six
    /// sites in LayoutEngine), not on anything having been hidden, and two of those sites
    /// pick a different formula: the per-system height (:198-202), which LYRHK sees, and the
    /// page's staff springs, emptied and rebuilt per system WITHOUT SKYLINES (:128-131),
    /// which it cannot — a spring's minimum comes from those skylines and only binds on a
    /// COMPRESSED page, while every hara-kiri book so far is ragged. Hence the justified
    /// paper packed 8 systems to a page, the regime book JSK measures.
    /// </para>
    /// <para>
    /// ⚠️ HANDOFF 5.0 trap 7: confirm from the dump that the page really compressed — the
    /// inside distance must come out BELOW the ideal 9.000000 — before reading anything.
    /// </para>
    /// </remarks>
    private static readonly string LYRHKD = BuildHaraKiriPlainScore(declareRemoveEmpty: true);

    /// <summary>The control of the pair above: the same music without the declaration.</summary>
    private static readonly string LYRHKN = BuildHaraKiriPlainScore(declareRemoveEmpty: false);

    /// <summary>Three bars to a system, twenty systems — the shape books LYRHK, LYRHKG,
    /// LYRHKD and LYRHKN all share. Breaks go BETWEEN systems, so there are nineteen of
    /// them and no trailing one.</summary>
    private static (string Upper, string Melody, string Syllables) HaraKiriParts(
        int silentSystems)
    {
        const int barsPerSystem = 3;
        const int systems = 20;
        string rest = string.Concat(Enumerable.Repeat("r1 | ", barsPerSystem));
        string play = string.Concat(Enumerable.Repeat("g'4 a' g' a' | ", barsPerSystem));
        string upper = string.Join("break ",
            Enumerable.Range(0, systems).Select(s => s < silentSystems ? rest : play));
        return (upper,
            string.Concat(Enumerable.Repeat("g4 a g a | ", barsPerSystem * systems)),
            string.Concat(Enumerable.Repeat("no no no no | ", barsPerSystem * systems)));
    }

    /// <summary>Builds <see cref="LYRHKG"/>.</summary>
    private static string BuildHaraKiriGrouperScore()
    {
        var (upper, melody, syllables) = HaraKiriParts(silentSystems: 0);
        // The inner staff is silent through system 0 and takes no `break` of its own — the
        // staff above it carries them, since a break belongs to the score and not to a staff.
        string inner = string.Concat(Enumerable.Repeat("r1 | ", 3))
            + string.Concat(Enumerable.Repeat("g'4 a' g' a' | ", 57));
        return $$"""
            octave absolute
            time 4/4
            key c major

            part top { clef treble }
            part inner { clef treble removeEmpty all }
            part melody { clef treble }

            section Main {
              top { {{upper.Trim()}} }
              inner { {{inner.Trim()}} }
              melody { {{melody.Trim()}} }
              lyrics one { {{syllables.Trim()}} }
              lyrics two { {{syllables.Trim()}} }
            }

            form main { ~Main }

            score main "LYRHKG" {
              grandStaff { staff top staff inner }
              staff melody with lyrics one with lyrics two
            }
            """;
    }

    /// <summary>Builds <see cref="LYRHKD"/> / <see cref="LYRHKN"/> — identical but for the
    /// declaration, which never fires.</summary>
    private static string BuildHaraKiriPlainScore(bool declareRemoveEmpty)
    {
        var (upper, melody, syllables) = HaraKiriParts(silentSystems: 0);
        return $$"""
            octave absolute
            time 4/4
            key c major

            part upper { clef treble{{(declareRemoveEmpty ? " removeEmpty all" : "")}} }
            part melody { clef treble }

            section Main {
              upper { {{upper.Trim()}} }
              melody { {{melody.Trim()}} }
              lyrics one { {{syllables.Trim()}} }
              lyrics two { {{syllables.Trim()}} }
            }

            form main { ~Main }

            score main "{{(declareRemoveEmpty ? "LYRHKD" : "LYRHKN")}}" {
              staff upper
              staff melody with lyrics one with lyrics two
            }
            """;
    }

    /// <summary>
    /// HARA-KIRI WHERE THE INK BETWEEN TWO SURVIVING STAVES BEATS THE SPEC — the mirrors of
    /// books HKW and HKWN, and the regime commit 41f9749d moved a snapshot in without being
    /// able to name a ledger key for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every hara-kiri book above keeps its staves' ink inside their own lines, so all of
    /// them sit on StaffGrouper's basic-distance and would still read 9.000000 with the
    /// skylines unplugged — which is what the second placement walk did until 41f9749d.
    /// This pair is book P's arithmetic under a <c>removeEmpty</c> declaration:
    /// <c>d,</c> (LilyPond <c>d</c>) hangs 6 staff spaces below the treble staff's middle
    /// line and its head reaches 0.545 further, while the same written pitch is the bass
    /// staff's own middle line, so 6.545 + 2.05 + 1 = 9.595 beats basic-distance 9
    /// (align-interface.cc:228-238). WHOLE notes, so no stem enters the gap
    /// (stem.cc, <c>Stem::is_normal_stem</c>) and the binding ink is the notehead and the
    /// staff line — both already exact at 9.595000 in
    /// <c>staff.staff.{upper-note-to-lower-lines,lower-note-to-upper-lines}</c>, so a
    /// divergence here can only be about hara-kiri and not about the ink.
    /// </para>
    /// <para>
    /// ★ LILYPOND'S TALL-INK GAP IS THE SAME IN BOTH BOOKS, by construction rather than by
    /// measurement: hara-kiri is a suicide followed by a live-filter
    /// (page-layout-problem.cc:1366-1370, align-interface.cc:90), and the surviving system's
    /// Align_interface then runs the ordinary max() over the staves it still holds. What
    /// another system did with its own staves reaches neither term. So any difference Lily#
    /// shows between the two is entirely its own — and ragged-bottom keeps a page force out
    /// of the number as well.
    /// </para>
    /// <para>
    /// ⚠️ THE CONTROL CARRIES ITS OWN REGIME ASSERTION (HANDOFF 5.0 trap 7). HKWN's system 0
    /// keeps the silent staff, whose whole rest hangs from the fourth line and protrudes
    /// nowhere, so that ONE gap is spec-bound at 9.000000 while its neighbours are ink-bound
    /// at 9.595000 — out of one book, one paper and one solve. A probe that has quietly
    /// stopped consulting the skyline would have to report 9.000000 twice.
    /// </para>
    /// <para>
    /// ⚠️ A GRAND STAFF AND NOT A PIANO STAFF, which is not a spelling preference and cost the
    /// first run: <c>PianoStaff</c> \consists <c>Keep_alive_together_engraver</c> and its
    /// staves "are only removed together, never separately" (ly/engraver-init.ly:535-544), so
    /// written that way NOTHING is removed and the two books print identical pages. Lily#'s
    /// <c>grandStaff</c> removes members separately, which is what fixture test/hara-kiri
    /// rests on, so <c>GrandStaff</c> is also what makes the two sides the same music.
    /// ⚠️ Lily# <c>d,</c> is LilyPond <c>d</c> (HANDOFF 5.5).
    /// </para>
    /// </remarks>
    private static readonly string HKW = BuildHaraKiriWideInkScore(declareRemoveEmpty: true);

    /// <summary>The control of the pair above: the same music without the declaration, so its
    /// system 0 keeps a resting staff instead of losing it.</summary>
    private static readonly string HKWN = BuildHaraKiriWideInkScore(declareRemoveEmpty: false);

    /// <summary>
    /// THE DECLARATION ON ITS OWN, ON A COMPRESSED PAGE AND WITHOUT LYRICS — the mirrors of
    /// books HKCD and HKCN, and the net for the one stage of the hara-kiri island that had no
    /// ledger key: b415dd16, which gave the hara-kiri staff springs their skylines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="JSK"/>'s book with <c>removeEmpty</c> added to the upper part and nothing
    /// else changed — same builder, so the two cannot drift apart — and NO STAFF IS EVER
    /// EMPTY, so LilyPond's two readings are identical by construction and any difference
    /// Lily# shows between them is entirely its own.
    /// </para>
    /// <para>
    /// ⚠️ WHY NOT <see cref="LYRHKD"/>/<see cref="LYRHKN"/> AGAIN. Those ask the same question
    /// and can only carry their COUNTS: with two verses hanging under the lower staff the two
    /// engines do not fit the same number of systems on the page (LilyPond 8, Lily# 7), so a
    /// gap on one page and the same-named gap on the other are not the same quantity. Without
    /// the lyrics the shape is JSK's, where both engines already agree exactly at 16 staves
    /// and 8.651797, so here the DISTANCES can be carried.
    /// </para>
    /// <para>
    /// ⚠️ AND WHY THE INK IS LOW, unlike <see cref="HKW"/>. Compression drives a spring onto
    /// its MINIMUM, which is the alignment distance; with HKW's tall ink the floor would be
    /// 9.595000 and the page would sit on it and measure nothing (HANDOFF 5.0). JSK's music
    /// leaves the minimum at 7.545, well below the 8.651797 the page solves for, so the
    /// spring is genuinely between its floor and its ideal — the only regime in which the
    /// spring itself can be read.
    /// </para>
    /// </remarks>
    private static readonly string HKCD = TwoStaffPageScore("HKCD", declareRemoveEmpty: true);

    /// <summary>The control of the pair above: JSK's music unchanged.</summary>
    private static readonly string HKCN = TwoStaffPageScore("HKCN");

    /// <summary>Builds <see cref="HKW"/> / <see cref="HKWN"/> — three bars to a system,
    /// twenty systems, the upper staff silent through the first of them and hanging low
    /// under all the others.</summary>
    private static string BuildHaraKiriWideInkScore(bool declareRemoveEmpty)
    {
        const int barsPerSystem = 3;
        const int systems = 20;
        string rest = string.Concat(Enumerable.Repeat("r1 | ", barsPerSystem));
        string play = string.Concat(Enumerable.Repeat("d,1 | ", barsPerSystem));
        // Breaks go BETWEEN systems, so there are nineteen of them and no trailing one, and
        // they are carried by the upper staff alone — a break belongs to the score, not to a
        // staff. Explicit, so nothing here rests on the two line breakers agreeing about how
        // much music reaches a line (HANDOFF 5.0).
        string upper = string.Join("break ",
            Enumerable.Range(0, systems).Select(s => s == 0 ? rest : play));
        return $$"""
            octave absolute
            time 4/4
            key c major

            part rh { clef treble{{(declareRemoveEmpty ? " removeEmpty all" : "")}} }
            part lh { clef bass }

            section Main {
              rh { {{upper.Trim()}} }
              lh { {{string.Concat(Enumerable.Repeat("d,1 | ", barsPerSystem * systems)).Trim()}} }
            }

            form main { ~Main }

            score main "{{(declareRemoveEmpty ? "HKW" : "HKWN")}}" {
              grandStaff {
                staff rh
                staff lh
              }
            }
            """;
    }

    /// <summary>The justified paper books LYRHKD/LYRHKN engrave onto — eight systems to a
    /// page, so the page COMPRESSES and the springs' minima bind (book JSK's regime).</summary>
    private static readonly LayoutOptions EightSystemsPerPageJustified =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with { MaxSystemsPerPage = 8 },
        };

    /// <summary>Builds <see cref="LYRHK"/> — three bars to a system, twenty systems, the
    /// upper staff silent through the first of them.</summary>
    private static string BuildHaraKiriLyricScore()
    {
        var (upper, melody, syllables) = HaraKiriParts(silentSystems: 1);
        return $$"""
            octave absolute
            time 4/4
            key c major

            part upper { clef treble removeEmpty all }
            part melody { clef treble }

            section Main {
              upper { {{upper.Trim()}} }
              melody { {{melody.Trim()}} }
              lyrics one { {{syllables.Trim()}} }
              lyrics two { {{syllables.Trim()}} }
            }

            form main { ~Main }

            score main "LYRHK" {
              staff upper
              staff melody with lyrics one with lyrics two
            }
            """;
    }

    /// <summary>
    /// WHERE A BAR NUMBER SITS — the mirrors of books BNL and BNH. The two differ ONLY in
    /// the melody's octave, and LilyPond reads them identically.
    /// </summary>
    /// <remarks>
    /// A BarNumber stands at the LINE START, left of the clef, and LilyPond places
    /// outside-staff grobs against an X-AWARE skyline
    /// (lily/axis-group-interface.cc:359-474), so notes that begin after the clef cannot
    /// reach it. MEASURED (audit/lp-geometry/probes/page-vertical.ly, books BNL/BNH):
    /// both print the same three readings, 3.074440 / 3.050000 / 3.076208, with the
    /// variation being which digits the number contains and nothing else — while BNH's
    /// first ink starts 1.200000 higher up the page. So LilyPond's side of this pair is an
    /// IDENTITY, and whatever Lily# reads differently between the two spellings is its own.
    /// <para>
    /// ⚠️ THIS IS NOT A COSMETIC QUANTITY. The bar number is inside its staff's
    /// VerticalAxisGroup skyline, so it IS the ink a system reserves above its own
    /// reference point — LilyPond's <c>min_offsets[0]</c> (align-interface.cc:215-220).
    /// That term closes the loose-line chain of the system before it
    /// (page-layout-problem.cc:931-932) and floors the system-to-system spring (:625-629).
    /// The pair was opened because porting <c>distribute_loose_lines</c> made a two-verse
    /// lyric block refuse to compress into the 12.000000 LilyPond keeps, and the excess
    /// turned out not to be the lyrics.
    /// </para>
    /// <para>
    /// ⚠️ Lily# <c>a</c> is LilyPond <c>a'</c>, so the high book is <c>a''</c> here against
    /// <c>a'''</c> there (HANDOFF 5.5).
    /// </para>
    /// </remarks>
    private static string BarNumberScore(string name, string octave) => $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section Main {
          melody { {{string.Concat(Enumerable.Repeat($"a{octave}4 b{octave} a{octave} b{octave} | ", 48)).Trim()}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff melody
        }
        """;

    /// <summary>The melody inside the staff — the mirror of book BNL.</summary>
    private static readonly string BNL = BarNumberScore("BNL", "");

    /// <summary>The same music two octaves up — the mirror of book BNH.</summary>
    private static readonly string BNH = BarNumberScore("BNH", "''");

    /// <summary>
    /// WHERE A TEXT SCRIPT SITS, per string — the mirrors of textscript-ink.ly's books
    /// TXD / TXP / TXS / TXL. Lily#'s <c>_"text"</c> engraves at the end of the section
    /// just played and stacks above the staff, which is LilyPond's
    /// <c>^\markup \italic "text"</c> (TextScript, priority 450) over the same flat staff.
    /// </summary>
    /// <remarks>
    /// The pair that measures OutsideStaffStacker's letter-class constants
    /// (<c>TextAscentEm 0.75</c> / <c>TextDescentEm 0.25</c> — "no single LP grob source",
    /// its own comment says) against what LilyPond does, which is to read the string's own
    /// ink. MEASURED (audit/lp-geometry/probes/textscript-ink.ly, 2026-07-29):
    /// <list type="bullet">
    /// <item>TXD "dolce" baseline 2.550000 over the staff refpoint, six-digit round: staff
    /// ink 2.050000 + staff-padding 0.5 applied to the REFPOINT
    /// (lily/side-position-interface.cc:401-453 aligned_side).</item>
    /// <item>TXP "poco" baseline 2.954430 = ink bottom pinned at 2.510000 (staff ink
    /// 2.050000 + outside-staff-padding 0.46,
    /// lily/axis-group-interface.cc:45-50 get_default_outside_staff_padding) plus the
    /// p's own descent 0.444430 — so LilyPond's baseline RIDES THE DESCENDER and the two
    /// books differ by it, while a flat-fraction stacker reads them identical.</item>
    /// <item>TXL "poco" over "mum": step 1.938448 = inkTop(mum) 1.034035 + 0.46 +
    /// descent(poco) 0.444430 to 1.6e-5 — box arithmetic holds because "mum"'s x-height
    /// top is flat under wherever the descender falls.</item>
    /// <item>TXS "poco" over "dolce": step 2.104975, 0.420895 BELOW the box arithmetic
    /// 2.525870 — outline against outline, pointwise
    /// (lily/axis-group-interface.cc:739-806 add_grobs_of_one_priority, :648
    /// avoid_outside_staff_collisions): the descender falls over d-o-l's bowls, not
    /// the ascender. An interval stacker cannot represent this; the entry names it.</item>
    /// </list>
    /// <para>
    /// ⚠️ Every section is referenced as <c>~Name</c>: a section LABEL is serif at the very
    /// size custom text draws at, and <see cref="RenderedGeometry.CustomTexts"/> tells the
    /// runs apart by size alone.
    /// </para>
    /// <para>
    /// ⚠️ The music is c' (LilyPond c'') — a DOWN stem, so nothing but the staff's own top
    /// line stands under the text and the support is flat at every X. An UP-stemmed pitch
    /// here would turn the entries into measurements of a stem tip.
    /// </para>
    /// </remarks>
    private static string TextScriptScore(string name, string texts) => $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section A {
          melody { c'4 c' c' c' | c'4 c' c' c' | }
        }
        section B {
          melody { c'4 c' c' c' | c'1 | }
        }

        form main { ~A {{texts}} ~B }

        score main "{{name}}" {
          staff melody
        }
        """;

    /// <summary>No descender — the baseline sits on the staff-padding refpoint floor.</summary>
    private static readonly string TXD = TextScriptScore("TXD", "_\"dolce\"");

    /// <summary>The descender — the baseline rides the p's own ink.</summary>
    private static readonly string TXP = TextScriptScore("TXP", "_\"poco\"");

    /// <summary>Two scripts whose extremes do NOT align — the outline (pointwise) step.</summary>
    private static readonly string TXS = TextScriptScore("TXS", "_\"dolce\" _\"poco\"");

    /// <summary>Two scripts with a flat lower profile — the box-arithmetic step.</summary>
    private static readonly string TXL = TextScriptScore("TXL", "_\"mum\" _\"poco\"");

    /// <summary>
    /// THE NUMBER OF A FULLY BEAMED TUPLET as staff-to-staff binding ink — the mirrors of
    /// tuplet-number-beamed.ly's books TNB / TNC.
    /// </summary>
    /// <remarks>
    /// A fully beamed tuplet prints NO bracket but its NUMBER still prints, and MEASURED
    /// (audit/lp-geometry/probes/tuplet-number-beamed.ly, 2026-07-29) LilyPond puts that
    /// number's CENTRE at the INVISIBLE bracket's position — the beam's lower edge plus
    /// TupletBracket padding 1.1, six-digit clean in two different musics — NOT riding on
    /// the beam. The number is ordinary staff-skyline ink, so with the staff-staff ideal
    /// and minimum taken away (the system-clef-floor recipe applied to
    /// default-staff-staff-spacing) the gap reads it directly:
    /// <list type="bullet">
    /// <item>TNB 8.017717 = number ink bottom 4.967717 (beam edge 3.240 + 1.100 + half
    /// ink 0.627717) + lower staff line 2.05 + padding 1 — the number binds.</item>
    /// <item>TNC 6.590000 = the upper staff's CLEF down-reach 3.540 + 2.05 + 1 — in the
    /// control the beam (3.240) loses to the clef by 0.3, so TNC doubles as a clef-vs-
    /// staff-line silhouette net on a pairing no other point exercises.</item>
    /// </list>
    /// <para>
    /// ⚠️ The first cut of the probe used treble clefs on BOTH staves and read the
    /// identical clef-against-clef 8.210039 (= skyline-binding.ly's 7.210039 + 1) in both
    /// books — the deepest ink is what an entry measures, so the lower staff is BASS here
    /// and the triplet sits at b'/c'' (HANDOFF 5.0, "probe が何を測っているか").
    /// </para>
    /// <para>
    /// Lily#'s SkylineBuilder.AddTupletBracketsToSkyline SKIPS the whole tuplet when
    /// !ShowBracket ("its number rides the beam"), so its TNB gap should read like TNC's
    /// — that identity is the defect this pair opens.
    /// </para>
    /// </remarks>
    private static string BeamedTupletScore(string name, string upperBars) => $$"""
        octave absolute
        time 4/4
        key c major

        part upper { clef treble }
        part lower { clef bass }

        section Main {
          upper { {{upperBars}} }
          lower { d,1 | d,1 | d,1 | }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff upper
          staff lower
        }
        """;

    /// <summary>
    /// THE OTTAVA BRACKET LINE over the staff — the mirrors of ottava-floor.ly's books
    /// OTF / OTC, the first points that reach OttavaBracket's staff-padding floor.
    /// </summary>
    /// <remarks>
    /// OttavaBracket declares <c>staff-padding 2.0</c> and <c>padding 0.5</c>
    /// (scm/define-grobs.scm), consumed by side-position-interface.cc:401-453
    /// aligned_side. MEASURED (audit/lp-geometry/probes/ottava-floor.ly, 2026-07-29):
    /// <list type="bullet">
    /// <item>OTF (drawn third-space c'' under the bracket): line at 4.050000 above the
    /// refpoint, SIX-DIGIT ROUND = staff ink 2.05 + staff-padding 2.0 — the FLOOR, the
    /// same shape TextScript's 2.550000 took.</item>
    /// <item>OTC (same music, drawn c''' on two ledger lines): 5.777520 = column top
    /// 4.485489 + padding 0.5 + the label's half-ink 0.792031 (aligned_side clears the
    /// grob's EDGE, and the hook takes no Y-extent — ottava-bracket.cc erases the
    /// bracket's own box, only the centred text ink counts).</item>
    /// </list>
    /// The written pitch is one octave above the drawn head (the ottava transposes the
    /// notation): Lily# <c>c''</c> = LilyPond <c>c'''</c> = drawn <c>c''</c>.
    /// </remarks>
    private static string OttavaScore(string name, string bars) => $$"""
        octave absolute
        time 4/4
        key c major

        part mel { clef treble }

        section Main {
          mel { {{bars}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff mel
        }
        """;

    /// <summary>Floor regime: drawn third-space heads, every support term far below 4.05.</summary>
    private static readonly string OTF = OttavaScore("OTF",
        "c''4@ottava c'' c'' c'' | c'4@loco c' c' c' |");

    /// <summary>Support regime: the same music two octaves up, the column decides.</summary>
    private static readonly string OTC = OttavaScore("OTC",
        "c'''4@ottava c''' c''' c''' | c''4@loco c'' c'' c'' |");

    /// <summary>
    /// OTC moved to the LOWER staff of a two-staff system — the ottava's face of the guard
    /// three movers carry (<c>PlaceOttavas</c>: <c>if (StaffIndex != 0) continue</c>).
    /// OTC and not OTF because OTC is the regime whose answer COMES FROM THE PASS:
    /// <c>OttavaBracketEngraver</c> computes only the staff-padding floor (4.05) and the whole
    /// support side is the collision pass's work, so skipping the pass is visible here and
    /// free in OTF. LilyPond reads 5.777519990798647 — OTC's number to FIFTEEN digits, its
    /// pass being per-VerticalAxisGroup.
    /// </summary>
    private static readonly string OTL = $$"""
        octave absolute
        time 4/4
        key c major

        part upper { clef treble }
        part lower { clef treble }

        section Main {
          upper { b4 b b b | b b b b | }
          lower { c'''4@ottava c''' c''' c''' | c''4@loco c'' c'' c'' | }
        }

        form main { ~Main }

        score main "OTL" {
          staff upper
          staff lower
        }
        """;

    /// <summary>
    /// THE TRILL / TEXT SPANNER LINE over the staff — the mirrors of spanner-floors.ly's
    /// books TRF / TRC / TSF / TSC, the first points that reach TrillSpanner's and
    /// TextSpanner's quiet-support heights.
    /// </summary>
    /// <remarks>
    /// TrillSpanner declares <c>staff-padding 1.0</c> and <c>padding 0.5</c>; TextSpanner
    /// declares <c>staff-padding 0.8</c> and no vertical padding (side-position's default
    /// 0.0). MEASURED (audit/lp-geometry/probes/spanner-floors.ly, 2026-07-29):
    /// <list type="bullet">
    /// <item>TRF (drawn third-space c'' under the trill): line at 3.550000 = staff ink
    /// 2.05 + padding 0.5 + the grob's downward reach 1.0 (the tr glyph's
    /// <c>stencil-offset (0 . -1)</c>). NOT the naked staff-padding floor: for a
    /// deep-reaching grob, staff-padding's operative effect is <c>include_staff</c>
    /// (side-position-interface.cc:219-222, :323-330 set_minimum_height) — the STAFF
    /// EXTENT ENTERS THE SUPPORT and the grob pays its own padding over it; the :433-453
    /// refpoint floor is subsumed whenever the facing reach exceeds
    /// staff-padding − padding.</item>
    /// <item>TRC (same music two octaves up, drawn c'''' on ledgers): 9.545000 = head BOX
    /// top (7.5 + LILC 0.545) + 0.5 + 1.0 — the same formula on the column support.</item>
    /// <item>TSF: 2.850000 = staff ink 2.05 + staff-padding 0.8 — the NAKED floor, round:
    /// with padding 0 and a facing reach of only 0.05 (dash half-thickness; "rit." has no
    /// descender), the floor is what stands.</item>
    /// <item>TSC: 8.555000 = head box top 8.045 + outside-staff-padding 0.46 + 0.05 —
    /// the avoid_outside_staff_collisions pass, which outbids aligned_side's padding-0
    /// support hug.</item>
    /// </list>
    /// The trill's LINE and Lily#'s drawn tr-glyph Y coincide (DrawTrillSpanners anchors
    /// both at YUp); in LilyPond the glyph hangs 1 below the line — the LINE is the
    /// shared anchor, see RenderedGeometry.TrillLineAboveStaff.
    /// </remarks>
    private static string SpannerFloorScore(string name, string bars) => $$"""
        octave absolute
        time 4/4
        key c major

        part mel { clef treble }

        section Main {
          mel { {{bars}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff mel
        }
        """;

    /// <summary>Quiet-support regime: drawn third-space heads, the staff-in-support
    /// height decides (Lily# <c>c'</c> = LilyPond <c>c''</c>).</summary>
    private static readonly string TRF = SpannerFloorScore("TRF",
        "c'4@startTrillSpan c' c' c'@stopTrillSpan | c4 c c c |");

    /// <summary>Support regime: two octaves up, the ledger column decides.</summary>
    private static readonly string TRC = SpannerFloorScore("TRC",
        "c'''4@startTrillSpan c''' c''' c'''@stopTrillSpan | c''4 c'' c'' c'' |");

    /// <summary>Quiet-support regime for the rit. text spanner (Lily#'s @rit spans to
    /// the end of the following measure, like the .ly twin's \stopTextSpan).</summary>
    private static readonly string TSF = SpannerFloorScore("TSF",
        "c'4@rit c' c' c' | c'4 c' c' c' |");

    /// <summary>Support regime: two octaves up, the ledger column decides.</summary>
    private static readonly string TSC = SpannerFloorScore("TSC",
        "c'''4@rit c''' c''' c''' | c'''4 c''' c''' c''' |");

    /// <summary>
    /// THE TRILL LINE over a REAL stemmed column — the mirrors of
    /// trill-stem-support.ly's books TLS / TLB / TLW, the points that gate the LAST
    /// raw-3.5 read (<c>NoteColumnLayout.RawSupportEdgeUp</c>, the trill's support
    /// since the dynamics left it in session 37).
    /// </summary>
    /// <remarks>
    /// MEASURED (audit/lp-geometry/probes/trill-stem-support.ly, 2026-07-30; every
    /// prediction landed on its primary branch, six-digit). The trill's facing DOWN
    /// profile is FLAT — LilyPond wraps the left-bound text in its own "straight line
    /// as the vertical skyline" device (define-grobs.scm:4054-4068) — so the support
    /// reading is the SCALAR max of the spanned columns' up edges: a scalar CAN serve
    /// the trill, unlike the dynamics' pointwise support. Lily#'s scalar is just the
    /// wrong one (the raw 3.5).
    /// <list type="bullet">
    /// <item>TLS (voice-one c at drawn position +8, forced-up = FULLY SHORTENED
    /// quarter): line at 8.000000 = drawn tip 6.5 (dumped Stem ext upper 6.500000;
    /// stem.cc:519-555 shortens whenever dir*hp[dir] &gt;= 0 — full 2.0 half-spaces at
    /// which_step 9) + trill padding 0.5 + glyph reach 1.0. The 0.46-pass candidate
    /// (7.96) lost: aligned_side's support wins.</item>
    /// <item>TLB (the same columns as forced-up beamed PAIRS): 8.240000 = the quanted
    /// beam's outer face 6.74 (the drawn Stem ends AT the face: Stem ext upper ==
    /// Beam ext upper, 6.740000) + 0.5 + 1.0 — aligned_side again, where the
    /// dynamics' DSB was won by the 0.46 pass. The beamed quant sits 0.24 ABOVE the
    /// unbeamed shortened tip.</item>
    /// <item>TLW (the control: whole notes; the Stem grobs dump EMPTY extents):
    /// 6.045000 = head box top 4.545 (4.0 + half-ink 0.545) + 0.5 + 1.0.
    /// TLS − TLW = 1.955 isolates the stem term with the trill chain cancelled.</item>
    /// </list>
    /// Voice two is spacer rests only: it makes both engines run their two-voice stem
    /// forcing (a per-note <c>@stemUp</c> cannot serve — Lily#'s BEAM direction
    /// ignores the per-note override while the voice forcing steers it,
    /// <c>BeamDetector.DefaultBeamStemUp</c>) and contributes no ink anywhere.
    /// LilyPond twin (TLS): <c>&lt;&lt; { \voiceOne c'''4\startTrillSpan c''' c'''
    /// c'''\stopTrillSpan } \\ { s1 } &gt;&gt;</c>.
    /// <para>
    /// PORTED (same day): <c>NoteColumnLayout.SupportEdgeUp</c>'s stem branch converts
    /// <c>OutwardTipDeviceY</c> (one house, two frames) and the trill engraver hands
    /// <c>ColumnUpEdge</c> the beam-member lookup. TLS and TLB landed 0 NINE-DIGIT
    /// EXACT, TLW unmoved; the corpus stayed byte-identical as a RESULT (no fixture
    /// spans a trill over a shortened or beamed same-direction stem column).
    /// </para>
    /// </remarks>
    private static string TrillStemSupportScore(
        string name, string voiceOne, string voiceTwo) => $$"""
        octave absolute
        time 4/4
        key c major

        part mel { clef treble }

        section Main {
          mel { voice { {{voiceOne}} } { {{voiceTwo}} } }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff mel
        }
        """;

    /// <summary>The fully shortened forced-up quarter: LP reads the drawn tip 6.5.</summary>
    private static readonly string TLS = TrillStemSupportScore("TLS",
        "c''4@startTrillSpan c'' c'' c''@stopTrillSpan |", "s1 |");

    /// <summary>The forced-up beamed pairs: LP reads the quanted face 6.74.</summary>
    private static readonly string TLB = TrillStemSupportScore("TLB",
        "c''8[@startTrillSpan c''] c''[ c''] c''[ c''] c''[ c''@stopTrillSpan] |", "s1 |");

    /// <summary>The whole-note control: no stem ink; the head chain isolator.</summary>
    private static readonly string TLW = TrillStemSupportScore("TLW",
        "c''1@startTrillSpan | c'1@stopTrillSpan |", "s1 | s1 |");

    /// <summary>Round 2 (the X pair): the tall shortened column FIRST, under the tr
    /// glyph — both X hypotheses read 8.000000 here.</summary>
    private static readonly string TXG = TrillStemSupportScore("TXG",
        "c''4@startTrillSpan c c c@stopTrillSpan |", "s1 |");

    /// <summary>Round 2 (the X pair): the tall column LAST, under the WAVE. LilyPond
    /// measured 4.720721 = the stop column's LEDGER ink top 4.100000 + outside-staff-padding
    /// 0.460000 + the wave outline's local value 0.160721 there — so aligned_side gives the
    /// QUIET height and the extra comes from the COLLISION pass. (Round 2 read this chain as
    /// ledger 4.05 + the trill's own 0.5 + a wave reach 0.170721; session 39 dumped the
    /// grobs' skylines and found the sum right and all three terms wrong — see the ledger
    /// why.) Lily#'s pre-port scalar max read 8.0, the X-blindness this entry gated.</summary>
    private static readonly string TXW = TrillStemSupportScore("TXW",
        "c4@startTrillSpan c c c''@stopTrillSpan |", "s1 |");

    /// <summary>
    /// Round 3, the single-staff base of the lower-staff pair: voice one carries the trill
    /// over LOW columns (its own support is low, so aligned_side alone reads the quiet 3.55)
    /// and VOICE TWO puts a tall drawn-position-8 column under the span. The trill's
    /// side-support is per-voice (Trill_spanner_engraver lives in the Voice context,
    /// engraver-init.ly:376), so the other voice's ink is invisible to aligned_side and
    /// visible only to the collision pass — the one WIDE obstacle a priority-50 grob can
    /// have. MEASURED (LilyPond): 6.005000 = that column's ink top 4.545000 + outside-staff
    /// padding 0.460000 + the trill's stencil-offset reach 1.000000, because voice two's
    /// FIRST column lies under the "tr" GLYPH, whose profile is the flat plateau at
    /// line − 1.0 (define-grobs.scm:4054-4068) — a binding that is flat over 1.25 of X and
    /// therefore immune to a tenth of spacing, unlike TXW's 0.277 of slack.
    /// </summary>
    private static readonly string TXV = TrillStemSupportScore("TXV",
        "c4@startTrillSpan c c c@stopTrillSpan |", "c''4 c'' c'' c'' |");

    /// <summary>
    /// TXV moved to the LOWER staff of a two-staff system — the trill's face of the guard
    /// (<c>PlaceTrills</c>: <c>if (StaffIndex != 0) continue</c>). LilyPond reads TXV's
    /// 6.005000 to fifteen digits; Lily#'s guard holds the trill out of the pass entirely.
    /// The upper staff is deliberately QUIET: its ink is what the per-system tracker would
    /// wrongly let this trill clear.
    /// <para>
    /// Tagged TVL, not TXL: the textscript-ink pair already owns that tag (book TXL, "mum"
    /// over "poco"), and a book tag is what the ledger's <c>score</c> field names.
    /// </para>
    /// </summary>
    private static readonly string TVL = $$"""
        octave absolute
        time 4/4
        key c major

        part upper { clef treble }
        part lower { clef treble }

        section Main {
          upper { b4 b b b | }
          lower { voice { c4@startTrillSpan c c c@stopTrillSpan | } { c''4 c'' c'' c'' | } }
        }

        form main { ~Main }

        score main "TVL" {
          staff upper
          staff lower
        }
        """;

    /// <summary>Round 2: SLOPED forced-up beamed pairs, high member first — the
    /// support is the high member's drawn stem end (the face at ITS X, not the beam
    /// envelope's corner).</summary>
    private static readonly string TSB = TrillStemSupportScore("TSB",
        "c''8[@startTrillSpan a'] c''[ a'] c''[ a'] c''[ a'@stopTrillSpan] |", "s1 |");

    /// <summary>Round 2: a fermata on the first note of a trill over natural-down
    /// columns (single voice — voice-1 forcing would flip the fermata below). LilyPond's
    /// fermata carries outside-staff-priority 75 &gt; trill 50, so the TRILL stays at
    /// its quiet 3.55 and the fermata clears IT.</summary>
    private static readonly string TSP = $$"""
        octave absolute
        time 4/4
        key c major

        part mel { clef treble }

        section Main {
          mel { c'4@fermata@startTrillSpan c' c' c'@stopTrillSpan | c4 c c c | }
        }

        form main { ~Main }

        score main "TSP" {
          staff mel
        }
        """;

    /// <summary>
    /// WHERE A FERMATA'S INK SITS — the mirrors of script-priority.ly's books SPQ / SPH /
    /// SPS / SPD, the observers for making a declared-priority Script a MOVER of the
    /// outside-staff pass (the port gated by <c>trill.fermata-priority</c>, which watches
    /// the TRILL and cannot see the fermata itself).
    /// </summary>
    /// <remarks>
    /// A fermata is the ONLY script family that declares outside-staff-priority (75, seven
    /// entries in scm/script.scm), so it is the only one that moves in the pass — and the
    /// pass pays outside-staff-padding 0.46 against skylines that START from the inside-staff
    /// ones (axis-group-interface.cc:914-950): the staff symbol, the heads, the stems, the
    /// ledgers. That is why joining the pass moves EVERY fermata, not only one over a trill.
    /// <list type="bullet">
    /// <item>SPQ (a middle-line head, natural down stem — the regime almost every corpus
    /// fermata is in): ink bottom 2.511000 = staff ink 2.050000 + 0.460000 + the
    /// outline/LILC sliver 0.001. The engraver's own 0.40 and the staff-padding floor 0.25
    /// are both dominated.</item>
    /// <item>SPH (a high head, drawn position 8, two ledgers): 5.006000 = head ink top
    /// 4.545000 + 0.460000 + the sliver. The ledgers (ink top 4.100000) lie under the arch
    /// and cannot bind.</item>
    /// <item>SPS (a FORCED-UP stem under the fermata): 3.734333 = the drawn tip 3.333333
    /// (the middle line's 10/3) + the script's own padding 0.400000 + the sliver, with NO
    /// collision move: <c>add-stem-support</c> flattens the stem's skyline to its tip across
    /// all X for aligned_side (side-position-interface.cc:302-305), but the pass sees the
    /// thin stem again and the fermata's ARCH straddles it. The pointwise claim in person —
    /// a flat-box mover would be pushed to tip + 0.46 here.</item>
    /// <item>SPD (the down mirror of SPQ, stem forced up so it is on the far side): ink top
    /// -2.511000 — the pass runs on the down side too.</item>
    /// </list>
    /// </remarks>
    private static string FermataScore(string name, string bars, string voiceTwo = "") => $$"""
        octave absolute
        time 4/4
        key c major

        part mel { clef treble }

        section Main {
          mel { {{(voiceTwo.Length == 0 ? bars : $"voice {{ {bars} }} {{ {voiceTwo} }}")}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff mel
        }
        """;

    /// <summary>The corpus-wide regime: everything low, so the STAFF decides.</summary>
    private static readonly string SPQ = FermataScore("SPQ", "b4@fermata b b b |");

    /// <summary>A high head (drawn position 8, two ledgers): the HEAD decides.</summary>
    private static readonly string SPH = FermataScore("SPH", "c''4@fermata c'' c'' c'' |");

    /// <summary>A forced-up stem under the fermata — the pointwise falsifier. Voice two is
    /// spacer rests: it makes both engines run their two-voice stem forcing and contributes
    /// no ink (HANDOFF §5.0 trap 5).</summary>
    private static readonly string SPS = FermataScore("SPS", "b4@fermata b b b |", "s1 |");

    /// <summary>The down mirror of SPQ: fermata forced below, stem forced up.</summary>
    private static readonly string SPD = FermataScore("SPD", "b4@fermata.down b b b |", "s1 |");

    /// <summary>A FLAT under the fermata (high head): the accidental's tall ascender turns
    /// out to sit LEFT of the script's own extent, so the HEAD still binds.</summary>
    private static readonly string SPA = FermataScore("SPA", "bes'4@fermata bes' bes' bes' |");

    /// <summary>
    /// SPQ moved to the LOWER staff of a two-staff system — the book for the guard the port
    /// had to keep (<c>PlaceArticulations</c>: <c>if (StaffIndex != 0) continue</c>).
    /// LilyPond reads 2.511000, IDENTICAL to SPQ, because its pass runs per staff; Lily#'s
    /// ABOVE tracker is per SYSTEM, so without the guard the fermata flies over the top
    /// staff and with it the fermata never enters the pass at all.
    /// </summary>
    private static readonly string SPL = $$"""
        octave absolute
        time 4/4
        key c major

        part upper { clef treble }
        part lower { clef bass }

        section Main {
          upper { b4 b b b | }
          lower { d,4@fermata d, d, d, | }
        }

        form main { ~Main }

        score main "SPL" {
          staff upper
          staff lower
        }
        """;

    /// <summary>
    /// The figured-bass arrangements — the mirrors of figured-bass-placement.ly's books
    /// FBLA / FBLB / FBLC, the corpus's FIRST figured-bass points.
    /// </summary>
    /// <remarks>
    /// The figure-bearing part is identical in all three: bass clef, forced-down-stem C2
    /// half notes (LilyPond <c>c,</c>, drawn position −8 = two ledger lines below the staff),
    /// so the staff's ink reaches well past its own edge and neither engine can sit on a
    /// floor. FBB and FBC are the SAME SCORE with the figures moved from one staff to the
    /// other — the companion carries the identical deep-ink music, which is what a
    /// system-wide reading would wrongly pick up.
    /// ⚠️ <c>@stemDown</c> is a per-note override and works here because the notes are
    /// UNBEAMED; a beam ignores per-note direction (HANDOFF §1 session-37 note 5), which is
    /// why the fermata books force stems with a voice split instead.
    /// ⚠️ The staff names are suppressed (<c>staff ~fig</c>) because the LilyPond books carry
    /// no instrument name either. It ALSO used to be load-bearing for the harness: Lily# drew
    /// an instrument name in the same serif face and the same em as a bass figure, so a named
    /// staff put a decoy into <see cref="RenderedGeometry.BassFigures"/>. Since the face port
    /// (2026-07-30) a figure is a music GLYPH and a name is text, so the decoy is gone —
    /// the suppression stays for the first reason.
    /// <para>
    /// The three differ ONLY in where the figure-bearing staff sits, which is the entire
    /// claim: LilyPond reads the same 8.124795235605315 in all three
    /// (audit/lp-geometry/probes/figured-bass-placement.ly, measured 2026-07-30), because
    /// both of its devices resolve against the figures' OWN staff.
    /// </para>
    /// </remarks>
    /// <summary>The figure-bearing staff ALONE — the mirror of book FBLA.</summary>
    private static readonly string FBA = """
        octave absolute
        time 4/4
        key c major

        part fig { clef bass }

        section Main {
          fig { c,,2@stemDown@fig(5 3) c,,@stemDown@fig(6) | c,,2@stemDown@fig(7) c,,@stemDown@fig(6 4) | }
        }

        form main { ~Main }

        score main "FBA" {
          staff ~fig
        }
        """;

    /// <summary>
    /// THE QUIET CONTROL — the mirror of book FBLQ. The same figures under a staff whose
    /// column reaches nowhere (middle-line d, stems forced UP), so the STAFF's own ink is the
    /// deepest thing there is and whatever floors the row is what reads.
    /// </summary>
    /// <remarks>
    /// This is the pair-mate the placement port needs, the figured-bass form of the trill
    /// island's TRF/TRC: with a deep column the support decides, and with a quiet one the
    /// staff does. MEASURED (LilyPond): 3.674795235605315 = staff ink 2.05 + padding 0.5
    /// (the figure's INK TOP at 2.550000 below the centre line) + the digit's own
    /// 1.124795235605315 — the staff enters the SUPPORT because staff-padding is declared,
    /// and is not a separate refpoint floor.
    /// </remarks>
    private static readonly string FBQ = """
        octave absolute
        time 4/4
        key c major

        part fig { clef bass }

        section Main {
          fig { d,2@stemUp@fig(5 3) d,@stemUp@fig(6) | d,2@stemUp@fig(7) d,@stemUp@fig(6 4) | }
        }

        form main { ~Main }

        score main "FBQ" {
          staff ~fig
        }
        """;

    /// <summary>The figure-bearing staff as the UPPER of two identical staves — the mirror of
    /// book FBLB. THE ARRANGEMENT NO COMMITTED FIXTURE HAS.</summary>
    private static readonly string FBB = """
        octave absolute
        time 4/4
        key c major

        part fig { clef bass }
        part comp { clef bass }

        section Main {
          fig { c,,2@stemDown@fig(5 3) c,,@stemDown@fig(6) | c,,2@stemDown@fig(7) c,,@stemDown@fig(6 4) | }
          comp { c,,2@stemDown c,,@stemDown | c,,2@stemDown c,,@stemDown | }
        }

        form main { ~Main }

        score main "FBB" {
          staff ~fig
          staff ~comp
        }
        """;

    /// <summary>The same two staves with the figures on the LOWER one — the mirror of book
    /// FBLC, and the shape of the committed fixture test/figbass-chordname-lower-staff.
    /// </summary>
    private static readonly string FBC = """
        octave absolute
        time 4/4
        key c major

        part comp { clef bass }
        part fig { clef bass }

        section Main {
          comp { c,,2@stemDown c,,@stemDown | c,,2@stemDown c,,@stemDown | }
          fig { c,,2@stemDown@fig(5 3) c,,@stemDown@fig(6) | c,,2@stemDown@fig(7) c,,@stemDown@fig(6 4) | }
        }

        form main { ~Main }

        score main "FBC" {
          staff ~comp
          staff ~fig
        }
        """;

    /// <summary>
    /// Builds the figured-bass PAGE books — the mirrors of figured-bass-page.ly's FBPQ / FBPD
    /// / FBPN, which read what a figure row does to the foot of a page rather than where it
    /// sits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One builder for all three so they cannot drift: the control is the quiet book with
    /// <paramref name="figures"/> false and nothing else changed, which is what makes
    /// LilyPond's difference between them the row's own contribution (HANDOFF §5.0 — a pair
    /// whose halves are hand-copied has drifted apart twice).
    /// </para>
    /// <para>
    /// 100 bars, because the reading needs a page that is FULL (so the foot spring is on its
    /// floor rather than taking the page's force) and NOT THE LAST (so nothing suppresses the
    /// justification). Fourteen systems at this width: twelve on page 1, two on page 2 —
    /// asserted by the three <c>systems-on-first-page</c> entries, since every distance here
    /// is read from "the last staff of page 1".
    /// </para>
    /// <para>
    /// ⚠️ THE FIGURES ARE ON EVERY BAR, not just the last system's. The two engines need not
    /// break lines identically, and a row parked at the end alone would land on different
    /// systems on the two sides.
    /// </para>
    /// <para>
    /// ⚠️ The stem direction is forced per note for the reason the placement books force it:
    /// the quiet texture's middle-line d would default DOWN and reach below the staff, which
    /// is the one thing that texture must not do.
    /// </para>
    /// </remarks>
    private static string FiguredPageScore(string tag, string note, string stem, bool figures)
    {
        string fig = figures ? "@fig(5 3)" : "";
        string bar = $"{note}4@{stem}{fig} {note}@{stem} {note}@{stem} {note}@{stem} | ";
        return $$"""
            octave absolute
            time 4/4
            key c major

            part fig { clef bass }

            section Main {
              fig { {{string.Concat(Enumerable.Repeat(bar, 100)).Trim()}} }
            }

            form main { ~Main }

            score main "{{tag}}" {
              staff ~fig
            }
            """;
    }

    /// <summary>The QUIET page book — the mirror of FBPQ. The row is the deepest ink the
    /// music has, and the smallest row there can be.</summary>
    private static readonly string FBPQ = FiguredPageScore("FBPQ", "d,", "stemUp", figures: true);

    /// <summary>The DEEP page book — the mirror of FBPD. Two ledger lines below the staff with
    /// stems forced down, so the column pushes the row far under it.</summary>
    private static readonly string FBPD = FiguredPageScore("FBPD", "c,,", "stemDown", figures: true);

    /// <summary>The CONTROL — the mirror of FBPN: FBPQ without the figures.</summary>
    private static readonly string FBPN = FiguredPageScore("FBPN", "d,", "stemUp", figures: false);

    /// <summary>
    /// The same page books pointed at a BELOW-STAFF ANNOTATION instead of a figure row — the
    /// mirrors of dynamic-page.ly's DYPQ / DYPD / DYPH.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same builder shape, same paper, same two textures as
    /// <see cref="FiguredPageScore"/>, so a difference between the two probes is the
    /// ANNOTATION and not the music. The control is not repeated: FBPN is this exact music
    /// with nothing hanging below it.
    /// </para>
    /// <para>
    /// ⚠️ THE HAIRPIN SPELLING IS NOT LILYPOND'S, and the probe's header says so at length:
    /// LilyPond ends a hairpin with <c>\!</c> and Lily# has no terminator (its grammar ends
    /// one at the next dynamic — docs/GRAMMAR.md:544), so with no dynamic anywhere the two
    /// sides put a hairpin under every bar but end them one note apart. Measured before the
    /// book was written: three per-bar <c>@cresc</c> marks draw two hairpins, each spanning
    /// into the next one's start. The reading is the ink's DEPTH under a uniform texture, so
    /// it cannot depend on where a hairpin stops — but this book must not be reused for an X
    /// or a broken-spanner reading.
    /// </para>
    /// </remarks>
    private static string AnnotatedPageScore(string tag, string note, string stem, string mark)
    {
        string bar = $"{note}4@{stem}{mark} {note}@{stem} {note}@{stem} {note}@{stem} | ";
        return $$"""
            octave absolute
            time 4/4
            key c major

            part ann { clef bass }

            section Main {
              ann { {{string.Concat(Enumerable.Repeat(bar, 100)).Trim()}} }
            }

            form main { ~Main }

            score main "{{tag}}" {
              staff ~ann
            }
            """;
    }

    /// <summary>A dynamic under the QUIET texture — the mirror of DYPQ, and the book in which
    /// the estimate's 2.0 can bind.</summary>
    private static readonly string DYPQ = AnnotatedPageScore("DYPQ", "d,", "stemUp", "@f");

    /// <summary>The same dynamic under the DEEP texture — the mirror of DYPD.</summary>
    private static readonly string DYPD = AnnotatedPageScore("DYPD", "c,,", "stemDown", "@f");

    /// <summary>A HAIRPIN and no dynamic text at all — the mirror of DYPH, and the only state
    /// in which the estimate's 1.5 branch runs (it is gated on there being no dynamics).</summary>
    private static readonly string DYPH = AnnotatedPageScore("DYPH", "d,", "stemUp", "@cresc");

    /// <summary>
    /// Twelve systems to a page, the paper figured-bass-page.ly's three books set — enough of
    /// this one-staff music that the page is full and its foot spring sits on its floor.
    /// </summary>
    /// <remarks>
    /// <c>max-systems-per-page</c> is a <c>\paper</c> variable in LilyPond, so it is set here
    /// for the same reason <see cref="SixSystemsPerPage"/>'s cap is — see
    /// <see cref="RenderedGeometry.Render"/>.
    /// </remarks>
    private static readonly LayoutOptions TwelveSystemsPerPage =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with { MaxSystemsPerPage = 12 },
        };

    /// <summary>
    /// THE METRONOME MARK's baseline over the staff — the mirrors of tempo-mark.ly's
    /// books TMQ / TMT, the tempo island's first points (USER DIRECTIVE 2026-07-29:
    /// tempo does not mimic LilyPond; fix tempo first).
    /// </summary>
    /// <remarks>
    /// MetronomeMark declares padding 0.8 and NO staff-padding, its supports are the
    /// STAVES themselves (metronome-engraver.cc:136-139 side-support-elements =
    /// stavesFound), priority 1300, horizontal 0.2, skylines from the stencil.
    /// MEASURED (audit/lp-geometry/probes/tempo-mark.ly, 2026-07-29):
    /// <list type="bullet">
    /// <item>TMQ (quiet): baseline at 2.883010 = staff ink 2.05 + padding 0.8 + the
    /// mark's own stencil bottom 0.033010 (aligned_side lands the stencil's bottom at
    /// 2.85; the baseline rides its own overshoot above that).</item>
    /// <item>TMT (a trill under the mark): 5.110000 six-digit round = quiet trill line
    /// 3.55 + tr glyph top 1.1 + outside-staff 0.46 — the priority-1300 pass clears the
    /// priority-50 trill, whose own line stays at the ledger 3.550000.</item>
    /// <item>X (ledger tempo.x.mark-to-time-signature, opened with the port):
    /// mark ink-left == time-signature ink-left, difference exactly 0
    /// (self-alignment-X LEFT on the break-aligned time signature).</item>
    /// </list>
    /// The baseline compared is the "= N" equation text's: LilyPond's markup puts the
    /// digits AND the \smaller note's bottom on it; Lily#'s DrawSingleMusicMark draws
    /// the equation at the mark's anchor Y the same way.
    /// </remarks>
    private static string TempoScore(string name, string bars) => $$"""
        octave absolute
        tempo 120
        time 4/4
        key c major

        part mel { clef treble }

        section Main {
          mel { {{bars}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff mel
        }
        """;

    /// <summary>Quiet regime: nothing else above the staff, the staff support decides.</summary>
    private static readonly string TMQ = TempoScore("TMQ",
        "c'4 c' c' c' | c4 c c c |");

    /// <summary>Stacking regime: a trill under the mark; the mark must clear it.</summary>
    private static readonly string TMT = TempoScore("TMT",
        "c'4@startTrillSpan c' c' c'@stopTrillSpan | c4 c c c |");

    /// <summary>Beat-long beamed triplets — number only, no bracket.</summary>
    private static readonly string TNB = BeamedTupletScore("TNB",
        "tuplet 3/2 { b8 c' b } tuplet 3/2 { b8 c' b } tuplet 3/2 { b8 c' b } tuplet 3/2 { b8 c' b } | "
        + "tuplet 3/2 { b8 c' b } tuplet 3/2 { b8 c' b } tuplet 3/2 { b8 c' b } tuplet 3/2 { b8 c' b } | b1 |");

    /// <summary>The same heads as plain beamed eighths — no tuplet at all.</summary>
    private static readonly string TNC = BeamedTupletScore("TNC",
        "b8 c' b c' b c' b c' | b8 c' b c' b c' b c' | b1 |");

    /// <summary>
    /// THE DRAWN BRACKET's ENCOMPASS POINTS — the mirrors of
    /// tuplet-bracket-encompass.ly's books TPB / TPC / TPS (HANDOFF ▶ⓐ's observation
    /// surface: a partial beam with the bracket shown, spanning a beam).
    /// </summary>
    /// <remarks>
    /// LilyPond has no beamed-specific tuplet formula: the bracket's encompass points are
    /// the note columns' REAL extents — a beamed stem ends at the quanted beam face, an
    /// unbeamed one at its real (possibly shortened) tip — and the line sits one
    /// TupletBracket padding 1.1 beyond the extreme, the number riding its midpoint.
    /// MEASURED (audit/lp-geometry/probes/tuplet-bracket-encompass.ly, 2026-07-29), every
    /// book one system / two staves, outer pitches equal so the bracket is FLAT:
    /// <list type="bullet">
    /// <item>TPB (partial beam c''[ b'] c'', bracket SHOWN) 8.013028 = number ink bottom
    /// 4.963028 (line centre 4.335312 = the quanted beam face AT the outer stem + 1.100;
    /// the beam's bbox corner 3.24 differs by half-stem × slope ≈ 0.0047) + 2.05 + 1.</item>
    /// <item>TPC (the same 2-note beams, no tuplet) 6.590000 — clef-bound like TNC, the
    /// identity control: the bare beam loses to the clef 3.540 by 0.3.</item>
    /// <item>TPS (quarter triplets, no beam) 8.111050 = (10/3 + 1.1 = 4.433333 NINE-DIGIT
    /// + half ink 0.627717) + 3.05 — the middle-line quarter's SHORTENED stem
    /// (stem.cc's dir*hp[dir] >= 0 includes the middle line) is the encompass.</item>
    /// </list>
    /// <para>
    /// Lily#'s drawn-bracket branch (CalculateSlope/OutwardTip) builds its encompass from
    /// the raw DefaultStemLength 3.5 — it sees neither the beam model nor the stem
    /// shortening — so TPB and TPS should read NINE-DIGIT IDENTICAL (both flat brackets
    /// over a raw-3.5 b' extreme). LilyPond separates the two books; the identity, not
    /// the residual, is the defect (HANDOFF 5.3 「同じであってはならない数が同じ」).
    /// </para>
    /// </remarks>
    private static readonly string TPB = BeamedTupletScore("TPB",
        "tuplet 3/2 { c'8[ b] c' } tuplet 3/2 { c'8[ b] c' } tuplet 3/2 { c'8[ b] c' } tuplet 3/2 { c'8[ b] c' } | "
        + "tuplet 3/2 { c'8[ b] c' } tuplet 3/2 { c'8[ b] c' } tuplet 3/2 { c'8[ b] c' } tuplet 3/2 { c'8[ b] c' } | b1 |");

    /// <summary>The same 2-note beams with no tuplet at all — the identity control.</summary>
    private static readonly string TPC = BeamedTupletScore("TPC",
        "c'8[ b] c'[ b] c'[ b] c'[ b] | c'8[ b] c'[ b] c'[ b] c'[ b] | b1 |");

    /// <summary>Quarter triplets, no beam — the shortened-stem half of the claim.</summary>
    private static readonly string TPS = BeamedTupletScore("TPS",
        "tuplet 3/2 { b4 c' b } tuplet 3/2 { b4 c' b } | tuplet 3/2 { b4 c' b } tuplet 3/2 { b4 c' b } | b1 |");

    /// <summary>
    /// A DYNAMIC under a forced-down column — the mirrors of dynamic-support.ly's books
    /// DSQ / DSW / DSB, the points that gate the LAST raw-3.5 read
    /// (<c>NoteColumnLayout.RawSupportEdgeUp</c>, session 35's model table).
    /// </summary>
    /// <remarks>
    /// MEASURED (audit/lp-geometry/probes/dynamic-support.ly, 2026-07-29). The dumped
    /// DSQ stem tip sits at −6.500000 (the forced quarter takes the full stem-shorten
    /// 1.0), yet the spanner's near edge sits at head ink − padding 0.6, and the
    /// whole/black head-ink difference 0.022285 between DSW and DSQ propagates 1:1 into
    /// the gap; DSB's spanner edge = beam face −6.74 − 0.46 (the outside-staff pass).
    /// <para>
    /// ⚠️ MECHANISM CORRECTED in session 36 (books DMF/DMW): session 35 read the DSQ
    /// landing as "the Stem is not a side-position support". The source says otherwise
    /// — dynamic-align-engraver.cc:108-117 acknowledges heads AND stems into support_
    /// (:222-223 add_support), grob.cc:81-85 gives the Stem default vertical-skylines
    /// from extents, and side-position-interface.cc:353-358 takes a POINTWISE distance
    /// against my_dim = the DynamicText's real outline (define-grobs.scm:1412-1413,
    /// :1446). In DSQ the stem tip IS in the support skyline at its own thin X and the
    /// f's low left outline tucks beside it, so the head wins; in DMF (\fff, wide) the
    /// same computation lands ON the stem (tip − 0.6 − the fff outline's local drop
    /// 0.055330; DMF − DMW = 1.923617). One computation, two landings — which is why a
    /// scalar support edge cannot reproduce both DSQ and DMF whatever value it takes.
    /// </para>
    /// <para>
    /// PORTED (session 37): <c>DynamicEngraver.ColumnSupportSkylines</c> is the pointwise
    /// side-position support (heads + real thin stems as extent boxes at their own X +
    /// staff extent minimum, distance against the label's composed feta outline —
    /// <c>DynamicOutline</c>), and the below-side outside-staff pass runs over the
    /// staff's real down profile (0.46, pointwise; seed in
    /// <c>SkylineBuilder.AddDynamicsToSkyline</c>, draw in the below stacker, one
    /// spelling). DSB landed on the face-sliver family; DSQ/DMF carry the named Pango
    /// X-extent centering residual (~+0.0015/+0.0018 — see their whys).
    /// </para>
    /// LilyPond twin (DSQ): <c>&lt;&lt; { \voiceOne b'1 } \\ { \voiceTwo a4\f r4 r2 }
    /// &gt;&gt;</c> over <c>\clef bass d1</c>, default-staff-staff-spacing zeroed.
    /// </remarks>
    private static string DynamicSupportScore(string name, string upperVoiceTwo) => $$"""
        octave absolute
        time 4/4
        key c major

        part upper { clef treble }
        part lower { clef bass }

        section Main {
          upper { voice { b1 } { {{upperVoiceTwo}} } | }
          lower { d,1 | }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff upper
          staff lower
        }
        """;

    /// <summary>The shortened forced-down quarter: LP reads the HEAD, not the stem.</summary>
    private static readonly string DSQ = DynamicSupportScore("DSQ", "a,4@f r4 r2");

    /// <summary>The whole-note control: no stem exists; the dynamic-ink dowry isolator.</summary>
    private static readonly string DSW = DynamicSupportScore("DSW", "a,1@f");

    /// <summary>The beamed pair: the beam pushes via the outside-staff pass (0.46).</summary>
    private static readonly string DSB = DynamicSupportScore("DSB", "a,8[@f a,8] r4 r2");

    /// <summary>The mechanism book (session 36): \fff is wide enough that LilyPond's
    /// pointwise support distance lands on the STEM tip at the stem's own X.</summary>
    private static readonly string DMF = DynamicSupportScore("DMF", "a,4@fff r4 r2");

    /// <summary>DMF's control: \fff on a whole note — the head chain, exactly DSW.</summary>
    private static readonly string DMW = DynamicSupportScore("DMW", "a,1@fff");

    /// <summary>
    /// The system-clef-floor recipe applied to the STAFF-STAFF spring: ideal and minimum
    /// taken away (padding stays 1), so the gap IS the two staves' skyline distance + 1.
    /// Both the grouped and the ungrouped spelling are zeroed so the reading cannot
    /// depend on which spec Lily# routes two plain staves through; the LilyPond books
    /// zero default-staff-staff-spacing, the one two ungrouped staves read there.
    /// </summary>
    private static readonly LayoutOptions ZeroStaffStaffPaper =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with { RaggedBottom = true },
            StaffSpacing = StaffSpacingParameters.Default with
            {
                StaffStaff = StaffSpacingParameters.Default.StaffStaff with
                {
                    BasicDistance = 0,
                    MinimumDistance = 0,
                },
                DefaultStaffStaff = StaffSpacingParameters.Default.DefaultStaffStaff with
                {
                    BasicDistance = 0,
                    MinimumDistance = 0,
                },
            },
        };

    /// <summary>The default page with vertical justification off, as books BNL/BNH set.</summary>
    private static readonly LayoutOptions RaggedBottomPaper =
        LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with { RaggedBottom = true },
        };

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
          rh { voice { b1 } { a,1@f } | }
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
          lh { voice { tuplet 3/2 { a1 a1 a1 } } { d,1 d,1 } | }
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
          rh { voice { b1 b1 } { tuplet 3/2 { d,1 d,1 d,1 } } | }
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
          rh { voice { b1 } { g,8 g, g, g, g, g, g, g, } | }
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
    /// A beam whose left end runs over a printed ACCIDENTAL — the first book that reads the
    /// beam QUANTER's own answer rather than the room a beam takes on a page.
    /// </summary>
    /// <remarks>
    /// In bass clef, key A major, only <c>dis</c> prints a sharp (D sharp is not in the key),
    /// and it stands on the middle line right under the beam's left end — under the
    /// SIXTEENTH's beamlet stub, which is the second beam line. So the reading binds against
    /// the stack's inner beam over a stub's own horizontal extent, which is exactly what
    /// <c>add_collision</c> walks the beam segments for.
    /// <para>
    /// LilyPond twin (audit/lp-geometry/probes/beam-over-accidental.ly, score A):
    /// <c>\new Staff { \clef bass \key a \major r2 r4 gis,16 dis8 fis16 }</c> — its
    /// <c>Beam.positions</c> is <c>(2.81 . 4.5)</c>. Score B of the same probe is the CONTROL
    /// with the sharp spelled away (<c>d</c> for <c>dis</c>): <c>(2.0 . 3.5)</c>, so the
    /// accidental is worth +0.81 / +1.0 of beam height and the point is not sitting on a
    /// floor.
    /// </para>
    /// ⚠️ Lily#'s <c>octave absolute</c> sits an octave above LilyPond's, hence the extra
    /// commas here.
    /// </remarks>
    private static readonly string BQA = """
        octave absolute
        time 4/4
        key a major

        part m { clef bass }

        section Main { m { r2 r4 gis,,16 dis,8 fis,16 | } }

        form main { ~Main }

        score main "BQA" { staff m }
        """;

    /// <summary>
    /// A beam running over a note SUSTAINED IN ANOTHER VOICE — the cross-voice half of the
    /// covered-grob supply, and the source of test/multivoice-beam-collision.
    /// </summary>
    /// <remarks>
    /// Voice one beams eight eighths with stems up; voice two holds a whole note a sixth
    /// above them, whose head reaches under the FIRST beam group only. So the two groups in
    /// one book are a PAIR: the first is the covered reading, the second the free one, and a
    /// defect in the supply moves them by different amounts while a paper or spacing mismatch
    /// moves both.
    /// <para>
    /// LilyPond twin (audit/lp-geometry/probes/beam-over-other-voice.ly, score A):
    /// <c>\new Staff &lt;&lt; \new Voice { \voiceOne c''8 c'' c'' c'' c'' c'' c'' c'' }
    /// \new Voice { \voiceTwo a''1 } &gt;&gt;</c> — <c>Beam.positions</c> <c>(4.19 . 4.19)</c>
    /// then <c>(2.81 . 2.81)</c>. Score B is the CONTROL, the same eighths alone: both groups
    /// at <c>(2.81 . 2.81)</c>.
    /// </para>
    /// </remarks>
    private static readonly string BQV = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m {
            voice { c'8 c' c' c' c' c' c' c' }
            { a'1 }
          }
        }

        form main { ~Main }

        score main "BQV" { staff m }
        """;

    /// <summary>
    /// A beamed measure that shares a part with a <c>voice { } { }</c> span ONE BAR
    /// LATER — the reach of the span, measured where the span is not.
    /// </summary>
    /// <remarks>
    /// LilyPond's <c>\\</c> wraps each sublist in its own Voice context with
    /// <c>make-voice-props-set</c> at its head (scm/music-functions.scm:1042-1057
    /// voicify-sublist), so <c>\voiceOne</c> dies with the span and this measure keeps the
    /// pitch-derived direction: the run sits at staff positions +5..+12, well above the
    /// middle line, so its stems point DOWN and the beam lands near the centre.
    /// <para>
    /// Its IDENTITY PAIR is <see cref="BVSC"/>: the same measure with no span anywhere in
    /// the part. LilyPond prints the two the same, so any difference is Lily# alone.
    /// </para>
    /// <para>
    /// LilyPond twin (audit/lp-geometry/probes/beam-voice-span-scope.ly, score A):
    /// <c>\fixed c' { g'8 a' b' c'' d'' e'' fis'' g'' | &lt;&lt; { b2 a } \\ { d2 e } &gt;&gt; }</c>
    /// — 4/4 beams the eighths in two groups of four, <c>Beam.positions</c>
    /// <c>(-0.19 . 0.0)</c> then <c>(0.0 . 0.0)</c>. Score B prints both identically.
    /// </para>
    /// <para>
    /// ⚠️ Until 2026-08-01 Lily# asked <c>Voices.Length &gt; 1</c> — a PART-wide question —
    /// so one span anywhere pinned every bar of voice 1 stem-up and this beam came out on
    /// the wrong side of its heads while the control stayed put.
    /// </para>
    /// </remarks>
    private static readonly string BVS = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section A { m { g'8 a' b' c'' d'' e'' fis'' g'' | } }
        section B { m { voice { b2 a } { d2 e } } }

        form main { ~A ~B }

        score main "BVS" { staff m }
        """;

    /// <summary>The control for <see cref="BVS"/>: the same beamed measure, no span.</summary>
    private static readonly string BVSC = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section A { m { g'8 a' b' c'' d'' e'' fis'' g'' | } }

        form main { ~A }

        score main "BVSC" { staff m }
        """;

    // --- the one regime where the quanter's stems do NOT all point the same way
    // (audit/lp-geometry/probes/beam-knee.ly).
    //
    // Lily#'s quanter takes each stem's x from the NOTE COLUMN
    // (BeamScoringProblem.cs:187) rather than from the stem. A stem sits at the column plus
    // an attachment offset that depends on its DIRECTION — a notehead's width for an up
    // stem, about zero for a down one — so with every member pointing the same way the
    // offset is a constant that cancels out of the span, the slope and the least squares
    // alike. Under a KNEE it alternates, and nothing cancels. These are the points that can
    // see that.

    /// <summary>
    /// A four-stem KNEE: C4 against C6, alternating, so both engravers knee it unasked.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (probe beam-knee.ly, score A): <c>c'8 c''' c' c''' r2</c> —
    /// <c>Beam.positions</c> <c>(-0.19 . 0.19)</c>, stem directions <c>(1 -1 1 -1)</c>.
    /// <para>
    /// Its IDENTITY PAIR is in the same probe (score B): the same music with
    /// <c>Beam.auto-knee-gap = #100</c>, which forbids the knee, answers <c>(-5.5 . -5.5)</c>
    /// with every stem down. So the reading is the knee's and not a floor's. ⚠️ That pair has
    /// no Lily#-side twin — there is no syntax for auto-knee-gap — which is why it is
    /// recorded here rather than as a point.
    /// </para>
    /// Both ends, because the beam is sloped and a defect in the stems' x is a defect in the
    /// SLOPE: a reading at one end alone could be right by accident.
    /// </remarks>
    private static readonly string BQK = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c8 c'' c c'' r2 | }
        }

        form main { ~Main }

        score main "BQK" { staff m }
        """;

    /// <summary>
    /// A THREE-stem knee, whose middle stem is the one whose x offset differs from both ends'.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score C): <c>c'8 c''' c' r4 r2</c> — <c>(0.19 . 0.19)</c>, directions
    /// <c>(1 -1 1)</c>. Not a repetition of <see cref="BQK"/>: with four stems the two
    /// mis-offset members sit symmetrically about the middle and a slope error can cancel in
    /// the least squares, and with three it cannot.
    /// </remarks>
    private static readonly string BQK3 = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c8 c'' c r4 r2 | }
        }

        form main { ~Main }

        score main "BQK3" { staff m }
        """;

    /// <summary>
    /// The CONTROL for both: the same rhythm with no leap, so no knee and every stem up.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score D): <c>c'8 e' c' e' r2</c> — <c>(1.0 . 1.0)</c>. This is the
    /// regime where the column-versus-stem offset provably cancels, so it must be exact
    /// whatever the knee points say — and if it ever moves, the port broke the ordinary case.
    /// </remarks>
    private static readonly string BQKC = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c8 e c e r2 | }
        }

        form main { ~Main }

        score main "BQKC" { staff m }
        """;

    // --- the other regime every beam point above is blind to: a group whose members do NOT
    // all carry the same NUMBER of beam lines (audit/lp-geometry/probes/beam-mixed-count.ly).
    //
    // LilyPond reckons a beamed stem's ideal length from a beam count that is not the stem's
    // own: lily/stem.cc:1158 asks Beam::get_direction_beam_count (lily/beam.cc:1517-1532),
    // which is the MAXIMUM multiplicity over every stem pointing that way. Its own source says
    // why, at stem.cc:1196-1202 — "\score { \relative c'' { a8[ a32] } } must be horizontal".
    // MEASURED: in that book both stems answer stem-info (-0.62 -0.11) despite carrying one
    // beam and three, and the beam comes out (-0.81 . -0.81).

    /// <summary>
    /// The corpus regime: the mixedBeams bar of test/beaming, whose three beams each mix an
    /// eighth with two sixteenths.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (probe beam-mixed-count.ly, score A) — MEASURED, THREE beams of three
    /// stems each, every stem UP:
    /// <list type="bullet">
    /// <item>c-d-e rising: <c>(0.19 . 0.81)</c>, stem-info <c>(0.26 0.76 1.26)</c></item>
    /// <item>f-g-a rising: <c>(2.19 . 2.81)</c>, stem-info <c>(1.76 2.26 2.76)</c></item>
    /// <item>g-f-e falling: <c>(2.19 . 1.81)</c>, stem-info <c>(2.26 1.76 1.26)</c></item>
    /// </list>
    /// ⚠️ The ideal Ys step by exactly 0.5 per note, i.e. by the PITCH alone: LilyPond gives
    /// all three stems the same ideal LENGTH although their own counts are 1, 2 and 2. That is
    /// the maximum-count rule, visible in the corpus.
    /// <para>
    /// The two rising beams are where a round trip of the fixture found Lily# at 0.19/1.00 and
    /// 2.00/2.81 — one END each, so the height is right and the SLOPE is not. The falling beam
    /// is their control: same counts, same shape mirrored, and already exact. A port that
    /// moves it has broken what was right.
    /// </para>
    /// </remarks>
    private static readonly string BMC = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c8 d16 e f8 g16 a g8 f16 e d4 | }
        }

        form main { ~Main }

        score main "BMC" { staff m }
        """;

    /// <summary>
    /// LilyPond's own named case: the smallest group whose members' counts differ at all.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score B): <c>a''8[ a''32] r16. r4 r2</c> — <c>(-0.81 . -0.81)</c>, both
    /// stems DOWN, and both stem-infos <c>(-0.62 -0.11)</c>. Flat, which is the whole point of
    /// the comment at stem.cc:1196-1202 that put the maximum there.
    /// <para>
    /// Both ends, because flatness is the claim: a reading at one end alone cannot see a slope.
    /// </para>
    /// </remarks>
    private static readonly string BMCX = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { a'8[ a'32] r16. r4 r2 | }
        }

        form main { ~Main }

        score main "BMCX" { staff m }
        """;

    /// <summary>
    /// The IDENTITY PAIR for <see cref="BMCX"/>: the same two stems with their counts made
    /// equal.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score C): <c>a''8[ a''8] r4 r2</c> — <c>(0.0 . 0.0)</c>, both stems DOWN,
    /// both stem-infos <c>(-0.02 0.76)</c>. Flat too, for a reason that has nothing to do with
    /// a maximum: there is no maximum to take.
    /// <para>
    /// ⚠️ The pair is identical in SLOPE, not in height — three beam lines need more room than
    /// one, so LilyPond puts B lower. What LilyPond answers identically is <c>right - left</c>,
    /// which is 0 in both, so whatever Lily# puts between the two slopes is the defect and
    /// nothing else (HANDOFF 5.0).
    /// </para>
    /// </remarks>
    private static readonly string BMCC = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { a'8[ a'8] r4 r2 | }
        }

        form main { ~Main }

        score main "BMCC" { staff m }
        """;

    /// <summary>
    /// The second defect the mixed-count pair exposed: an 8-32-8 group clear of the staff,
    /// every stem in its natural direction.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score D): <c>c''8[ e''32 g''8] r8 r16. r2</c> — <c>(-3.0 . -1.81)</c>,
    /// every stem DOWN, stem-info ideals <c>-2.86 / -1.86 / -0.86</c>, a clean 1.0 step per
    /// third with one ideal LENGTH of 3.36 (beamed-lengths[count 3] 3.6 minus half the beam
    /// thickness) across counts 1, 3, 1.
    /// <para>
    /// This is bar 1 of test/beamlet-peaks, which the maximum-count port closed exactly. It is
    /// the CONTROL: it must stay exact while <see cref="BMCF"/> is chased.
    /// </para>
    /// </remarks>
    private static readonly string BMCU = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c'8[ e'32 g'8] r8 r16. r2 | }
        }

        form main { ~Main }

        score main "BMCU" { staff m }
        """;

    /// <summary>
    /// …and the same rhythm a third lower, where the first stem is FORCED against its natural
    /// direction — bar 2 of test/beamlet-peaks, which the port did not move at all.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score E): <c>a'8[ c''32 e''8] r8 r16. r2</c> — <c>(-4.0 . -2.81)</c>,
    /// every stem DOWN, stem-info ideals <c>-3.776667 / -2.776667 / -1.776667</c>.
    /// <para>
    /// ⚠️ MEASURED, and it is NOT a translation of <see cref="BMCU"/>: a third down is 1.0
    /// staff space, but the ideals moved by 0.916667 — 1.0 minus <c>1/12</c>. That 1/12 is the
    /// beam's <c>shorten</c> (lily/beam.cc:1061-1091 calc_stem_shorten, applied at
    /// lily/stem.cc:1245): <c>beamed-stem-shorten[count 3]</c> is 0.25 and exactly ONE of the
    /// three stems is forced against its natural direction (a' sits below the middle line and
    /// would stem up), so the forced fraction is 1/3 and 0.25 × 1/3 = 0.083333. In D no stem
    /// is forced and <c>shorten</c> is 0. The two books differ in that term and in nothing
    /// else — LilyPond gives them the same slope, dy 1.19 both.
    /// </para>
    /// Both ends of each, because what is being separated is height from slope.
    /// </remarks>
    private static readonly string BMCF = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { a8[ c'32 e'8] r8 r16. r2 | }
        }

        form main { ~Main }

        score main "BMCF" { staff m }
        """;

    /// <summary>
    /// A GRACE beam: two sixteenth graces before the main note — bar 1 of test/grace-notes.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score G of probes/beam-grace.ly):
    /// <c>\grace { d'16 e' } f'4 g'2 r4</c> — <c>(0.142 . 0.5)</c>, both stems UP, stem-info
    /// ideals <c>0.108 / 0.608</c> and floors <c>-0.66 / -0.16</c>, with
    /// <c>beam-thickness 0.384</c> and <c>length-fraction 0.8</c> (ly/grace-init.ly), against
    /// the full-size 0.48 and unset that <see cref="BGRC"/> reads.
    /// <para>
    /// This is the one divergence a twin sweep of the whole fixture corpus left standing
    /// outside a known gate (2026-08-01): 31 fixtures export a twin that draws a beam, and
    /// every beam reading in them matches LilyPond except the three books that carry a GRACE
    /// beam — test/grace-notes, test/grace-lower-staff, showcase/02-ornaments — which all
    /// miss by the same two numbers.
    /// </para>
    /// </remarks>
    private static readonly string BGR = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { d16 e } f4 g2 r4 | }
        }

        form main { ~Main }

        score main "BGR" { staff m }
        """;

    /// <summary>
    /// …the same two pitches as ORDINARY sixteenths — the size the quanter is already exact at.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score H): <c>d'16 e' r8 g'2 r4</c> — <c>(0.81 . 1.0)</c>, stem-info
    /// ideals <c>0.76 / 1.26</c>, floors <c>-0.2 / 0.3</c>, <c>beam-thickness 0.48</c>.
    /// <para>
    /// The CONTROL and the whole point of the pair: the two books differ by the grace scaling
    /// and by nothing else, so whatever Lily# puts between them is the defect's entire size.
    /// It must stay exact while <see cref="BGR"/> is chased.
    /// </para>
    /// </remarks>
    private static readonly string BGRC = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { d16 e r8 g2 r4 | }
        }

        form main { ~Main }

        score main "BGRC" { staff m }
        """;

    /// <summary>
    /// …and the same grace beam a third up: LilyPond TRANSLATES it, it does not re-quant.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score I): <c>\grace { f'16 g' } a'4 g'2 r4</c> — <c>(1.142 . 1.5)</c>,
    /// i.e. <see cref="BGR"/> plus exactly 1.0 staff space with the slope kept.
    /// <para>
    /// Lily# already reproduces that offset (test/grace-notes and showcase/02-ornaments are
    /// 1.0 apart on both sides), so this reading says the defect is ONE term and not a
    /// pitch-dependent family — and a fix that moves the offset has broken something else.
    /// </para>
    /// </remarks>
    private static readonly string BGRT = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { f16 g } a4 g2 r4 | }
        }

        form main { ~Main }

        score main "BGRT" { staff m }
        """;

    /// <summary>
    /// …and the register BGR / BGRT never reach: a grace sitting HIGH in the staff, whose
    /// beam therefore clears the top line instead of lying across it.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score J): <c>\grace { c''16 d'' } e''4 g''2 r4</c> — <c>(2.858 . 3.142)</c>,
    /// stem-info ideals <c>2.608 / 3.108</c>.
    /// <para>
    /// LilyPond gives every OTHER grace in this probe the same slope, dy 0.358 — G at
    /// (0.142 . 0.5), I at (1.142 . 1.5), K at (2.142 . 2.5) — and gives this one dy 0.284
    /// from the same ideal spacing of 0.5. The family is a translation until the beam leaves
    /// the staff, and then it is not. Nothing about the grace scaling changes between them:
    /// same size, same durations, same interval, only the register.
    /// </para>
    /// <para>
    /// ⚠️ ★★★ THIS BOOK SPENT ONE SESSION SPELLED <c>grace { c' d' }</c>, WITHOUT THE 16 —
    /// and a bare grace note is an EIGHTH in Lily# (Svg/Collector/MeasureCollector.cs
    /// graceDefaultDuration), so it was a ONE-beam book measured against a TWO-beam twin.
    /// It came out exact anyway, by luck, and its neighbour <see cref="BGRK"/> did not, which
    /// is where "the regime boundary is one step low" came from. With the durations matched
    /// it is the other way round again: K is exact and THIS one misses, by +0.642 / +0.716 —
    /// which is what the fixture test/grace-lower-staff had been saying all along, and what
    /// this point's own ledger entry quoted while recording residual 0.
    /// </para>
    /// </remarks>
    private static readonly string BGRJ = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { c'16 d' } e'4 g2 r4 | }
        }

        form main { ~Main }

        score main "BGRJ" { staff m }
        """;

    /// <summary>
    /// …and the bracket that locates where <see cref="BGRJ"/>'s regime begins: the same grace
    /// just BELOW the middle line, where the beam clears the staff but only barely.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score K): <c>\grace { a'16 b' } c''4 g'2 r4</c> — <c>(2.142 . 2.5)</c>,
    /// dy 0.358, the ordinary grace family's slope. Lily# answers the same to nine places.
    /// <para>
    /// ⚠️ This book was also spelled without the 16 for a session — see <see cref="BGRJ"/>.
    /// As a ONE-beam book it answered (1.858 . 2.142) against the twin's two-beam
    /// (2.142 . 2.5), which read as a divergence and is not one: the beam count changes the
    /// quant grid's bounds and every stem ideal. Matched, it is exact.
    /// </para>
    /// <para>
    /// ★ It stays a control worth keeping: it is the register where the forbidden-quant term
    /// fires on BOTH engines (LilyPond charges <c>Fl 0.82</c> here) and they still agree, so
    /// a fix that moves this point has changed the term rather than its scale.
    /// </para>
    /// </remarks>
    private static readonly string BGRK = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { a16 b } c'4 g2 r4 | }
        }

        form main { ~Main }

        score main "BGRK" { staff m }
        """;

    // ---- the X FRAME the grace beam is quanted in: how wide is a grace COLUMN? ------------
    //
    // beam.quant.grace.* left a SYMMETRIC residual — the height exact, the slope short — and a
    // slope-only residual cannot come from a length or a thickness. It has to be the span the
    // quanter fits over, i.e. the distance between the grace columns, which Lily# SPELT THREE
    // SEPARATE TIMES (all three gone since 2026-08-01 — SpacingRules.GraceColumns is the one
    // house now; kept here because it is why these points exist) and LilyPond does not spell
    // at all:
    //   GraceNoteEngraver.xs                        i * (1.2 + 0.3) * 0.65 = 0.975 per column
    //   SpacingRules.CalculateGraceGroupSpringWidth 1.28 per note + a 0.4 junction rod
    //   GraceNoteEngraver.GetGraceGroupWidth(int)   1.2*0.65 per note + 0.3*0.65 per gap + 0.4
    // (the 0.65 in two of them was the grace scale's own approximation, since derived —
    //  GraceNoteItem.ScaleFactor = magstep(-3); five spellings were counted in the end.)
    // against LilyPond's 1.417939 for the corpus texture. ⚠️ 1.417939 IS NOT THE ANSWER: it is
    // two SIXTEENTH graces, one texture, and this ledger has been burned by writing exactly
    // such a number into a constant before (the figured-bass 1.5). LilyPond computes it, from
    // a spring and a floor, and audit/lp-geometry/probes/grace-column-width.ly measures the
    // law rather than the number. Every book below is one term of it:
    //
    //   gap = max (ideal, min_dist + 0.3)
    //     ideal    = (1.6 + log2 (dt / dt_min)) * 0.8 - 0.8 + left_head_end
    //                lily/spacing-basic.cc:163-180 (the grace branch takes its options from
    //                the GraceSpacing grob), lily/spacing-options.cc:71-107 get_duration_space,
    //                scm/define-grobs.scm:1721-1725 GraceSpacing (shortest-duration-space 1.6,
    //                spacing-increment 0.8), lily/note-spacing.cc:42-115 get_spacing (the
    //                `- increment + left_head_end` rewrite), and dt_min from
    //                scm/output-lib.scm:1403-1422 grace-spacing::calc-shortest-duration — the
    //                MINIMUM gap of the run's OWN columns, which is what makes the run
    //                scale-free in duration and puts the ratio at >= 1 by construction.
    //     min_dist = the two columns' facing separation skylines, each grob's box widened by
    //                its extra-spacing-width (lily/separation-item.cc:120-190, default
    //                -0.1 . 0.1; Accidental declares -0.2 . 0.0 at scm/define-grobs.scm:40)
    //     + 0.3    = merge_springs' `avg_distance = max (min_distance + 0.3, avg_distance)`
    //                (lily/spring.cc:103-129), which runs even for a SINGLE spring
    //                (lily/spacing-spanner.cc:392-393)
    //
    // MEASURED, and every term of it read from a line above rather than fitted:
    //   grace head_end 0.917939 (a font metric at magstep(-3), NOT 0.7071 * the full-size
    //   1.3042 = 0.922205 — Emmentaler is optically sized), so the default-parameter ideal is
    //   1.6*0.8 - 0.8 + 0.917939 = 1.397939 and the floor is 0.917939 + 0.1 + 0.1 + 0.3 =
    //   1.417939. THE FLOOR IS WHAT THE CORPUS TEXTURE READS — a port that implements only
    //   the spring lands 0.02 short and the beam points stay open.
    //
    // ⚠️ The three books that pin the spring's own parameters (GraceSpacing's
    // shortest-duration-space at 1.0 and 3.0, spacing-increment at 1.6) are in the .ly probe
    // and NOT here: Lily# has no way to spell that override, so they are law, not ledger.
    // They are what says the formula is the formula — GCWS3 lands on 2.517939 and GCWI on
    // 1.877939 to nine places, and GCWS1, whose ideal would be 0.917939, does not move off
    // 1.417939 at all, which is how the floor was found.

    /// <summary>
    /// THREE sixteenth graces. The gap is a spring per column, so a third grace must not
    /// change what the first two are worth.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score GCW3): <c>\grace { d'16 e' f' } g'4 g'2 r4</c> — every gap
    /// 1.417939, the same as <see cref="BGR"/>'s. A reserved GROUP WIDTH divided among the
    /// graces would shrink here; Lily#'s <c>CalculateGraceGroupSpringWidth</c> sums a spring
    /// per note and then adds one junction rod, which is a group width in spring clothing.
    /// </remarks>
    private static readonly string GCW3 = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { d16 e f } g4 g2 r4 | }
        }

        form main { ~Main }

        score main "GCW3" { staff m }
        """;

    /// <summary>…and FOUR, whose last gap is the one that reaches the main note.</summary>
    /// <remarks>
    /// LilyPond twin (score GCW4): <c>\grace { d'16 e' f' g' } a'4 g'2 r4</c> — four gaps,
    /// all 1.417939, INCLUDING the last one. The grace → main gap is an ordinary grace spring
    /// (<c>delta_t.grace_part_</c> is non-zero, lily/spacing-basic.cc:163), not a junction of
    /// its own, so the reading that would expose an invented junction padding is this one.
    /// </remarks>
    private static readonly string GCW4 = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { d16 e f g } a4 g2 r4 | }
        }

        form main { ~Main }

        score main "GCW4" { staff m }
        """;

    /// <summary>Two EIGHTH graces — the same gap, because the run normalises by its own min.</summary>
    /// <remarks>
    /// LilyPond twin (score GCW2E): <c>\grace { d'8 e' } f'4 g'2 r4</c> — 1.417939, identical
    /// to the sixteenths. <c>grace-spacing::calc-shortest-duration</c> takes the minimum gap of
    /// THIS run's columns, so a run of equal graces always has ratio 1 whatever the note value.
    /// A width that scaled with the duration would show up here and nowhere else.
    /// </remarks>
    private static readonly string GCW2E = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { d8 e } f4 g2 r4 | }
        }

        form main { ~Main }

        score main "GCW2E" { staff m }
        """;

    /// <summary>…and two THIRTY-SECOND graces, the same claim from the other side.</summary>
    /// <remarks>
    /// LilyPond twin (score GCW2T): <c>\grace { d'32 e' } f'4 g'2 r4</c> — 1.417939 again.
    /// Two books rather than one because a single duration cannot tell "normalised by the run"
    /// from "normalised by the sixteenth".
    /// </remarks>
    private static readonly string GCW2T = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { d32 e } f4 g2 r4 | }
        }

        form main { ~Main }

        score main "GCW2T" { staff m }
        """;

    /// <summary>
    /// ONE grace, which draws a FLAG — so the floor, not the spring, decides.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score GCW1): <c>\grace { d'16 } f'4 g'2 r4</c> — 1.938627, where the
    /// beamed pairs read 1.417939. The ideal has not moved (ratio is still 1); what moved is
    /// the left column's separation skyline, which the flag pushes from 1.017939 to 1.538627,
    /// and 1.538627 + 0.1 + 0.3 = 1.938627 to nine places.
    /// <para>
    /// This is the book that proves the floor is a SKYLINE and not <c>head_end + 0.5</c>:
    /// the same arithmetic with a different ink gives a different answer.
    /// </para>
    /// </remarks>
    private static readonly string GCW1 = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { d16 } f4 g2 r4 | }
        }

        form main { ~Main }

        score main "GCW1" { staff m }
        """;

    /// <summary>
    /// THREE MUSICA FICTA accidentals — a grob that states <c>font-size −2</c> and therefore
    /// reads another Emmentaler design.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (probes/editorial-accidental.ly): the same three notes under
    /// <c>suggestAccidentals = ##t</c>, dumping each AccidentalSuggestion's extent and its
    /// origin against its note head. All three heads are black quarters, so the reading is
    /// a pure X quantity and the pitches do not enter it.
    /// </remarks>
    private static readonly string EDA = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { fis4@editorial bes@editorial c@editorial c | }
        }

        form main { ~Main }

        score main "EDA" { staff m }
        """;

    /// <summary>A sixteenth then an EIGHTH grace: the run splits, by log2 of the ratio.</summary>
    /// <remarks>
    /// LilyPond twin (score GCWM): <c>\grace { d'16 e'8 } f'4 g'2 r4</c> — 1.417939 then
    /// 2.197939. <c>dt_min</c> is 1/16, so the eighth's gap is
    /// <c>(1.6 + log2 2) * 0.8 - 0.8 + 0.917939 = 2.197939</c>, exactly 0.8 wider, and it is
    /// above the floor so it reads the IDEAL directly. This is the only pair of books that
    /// separates the two halves of the formula: one gap on the floor, one on the spring.
    /// </remarks>
    private static readonly string GCWM = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { d16 e8 } f4 g2 r4 | }
        }

        form main { ~Main }

        score main "GCWM" { staff m }
        """;

    /// <summary>…and the same two durations the other way round.</summary>
    /// <remarks>
    /// LilyPond twin (score GCWN): <c>\grace { d'8 e'16 } f'4 g'2 r4</c> — 2.197939 then
    /// 1.417939, i.e. <see cref="GCWM"/> mirrored. The wide gap follows the LONG note wherever
    /// it sits, which is what says the term is the gap's own <c>delta_t</c> and not the run's
    /// first duration or its total.
    /// </remarks>
    private static readonly string GCWN = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { d8 e16 } f4 g2 r4 | }
        }

        form main { ~Main }

        score main "GCWN" { staff m }
        """;

    /// <summary>An ACCIDENTAL on the second grace — the floor's other half.</summary>
    /// <remarks>
    /// LilyPond twin (score GCWA): <c>\grace { d'16 eis' } f'4 g'2 r4</c> — 2.560895. The
    /// spring's ideal has not moved; the RIGHT column's leftward reach has, from 0.1 to
    /// 1.242957, because the accidental sits at <c>-1.042957</c> in its column and Accidental
    /// declares <c>extra-spacing-width (-0.2 . 0.0)</c> (scm/define-grobs.scm:40). Then
    /// <c>1.017939 + 1.242957 + 0.3 = 2.560895</c>, nine places.
    /// <para>
    /// <see cref="GCW1"/> widens the left column and this widens the right one; between them
    /// the floor is pinned as a two-sided skyline computation, which is what a port has to
    /// implement rather than a constant.
    /// </para>
    /// </remarks>
    private static readonly string GCWA = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { grace { d16 eis } f4 g2 r4 | }
        }

        form main { ~Main }

        score main "GCWA" { staff m }
        """;

    /// <summary>
    /// A FLAGGED DOWN-STEM note whose neighbour is LOW enough to tuck under the flag.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score FSF8): <c>\time 4/4 c''8 dis'4 r4 r8</c> — 3.181800, against
    /// <see cref="FSFH8"/>'s 3.354200 with the SAME accidental one octave up.
    /// <para>
    /// ⚠️ THREE THINGS HAD TO BE TRUE AT ONCE and each one killed an earlier draft
    /// (audit/lp-geometry/probes/flagged-stem-reach.ly has the null results):
    /// the note must be FLAGGED (two eighths are one beat, LilyPond beams them and the Flag
    /// grob suicides), the stem must be DOWN (an up stem's flag stands beside the head, where
    /// it needs no help to be seen — 2.167400), and the neighbour must sit in the FLAG's Y
    /// BAND (a down stem stands at the head's LEFT edge, so at the head's own height the flag
    /// is inside the head's shadow and the column reaches 1.404200 whatever the flag does).
    /// </para>
    /// <para>
    /// The gap is FLOOR-bound, and the pair proves it rather than assuming it: the duration
    /// space is a function of the durations alone (lily/spacing-options.cc:71-107), so a
    /// spring-bound gap could not move when only a PITCH moves. It moves by 0.172400, which
    /// puts the flag's reach at 1.404200 − 0.172400 = 1.231800.
    /// </para>
    /// <para>
    /// WHAT IT IS FOR: Lily# reserves this flag at the head's left edge and DRAWS it at the
    /// stem, half a stem thickness (0.065) to the right — ItemSkylineFactory says so in a
    /// comment and leaves it alone for want of an observer. This is the observer.
    /// </para>
    /// <para>
    /// ⚠️⚠️ THE TWO SIDES DO NOT SPELL AN OCTAVE THE SAME WAY, and all three books here were
    /// written with LilyPond's spelling on BOTH sides until 2026-08-02 (session 71) — so they
    /// were not the same music, and <c>flag.up.reach</c> recorded a −1.613200 that was an
    /// OCTAVE and not a defect: Lily#'s column had a DOWN stem where LilyPond's had an UP one.
    /// MEASURED in this test project, <c>clef treble</c> and no instrument: Lily#'s absolute
    /// <c>c</c> is staff position −6, i.e. C4, so Lily# <c>c</c> IS LilyPond <c>c'</c> and
    /// every book here is spelled ONE APOSTROPHE LOWER than its twin.
    /// <c>lysc ly</c> already bridges this — it emits <c>\fixed c' { … }</c> — which is why
    /// the standing rule is to GENERATE twins rather than write them, and why the probes that
    /// were generated have it right (grace-column-width.ly's GCWA: LilyPond <c>f'4</c>
    /// against Lily# <c>f4</c>). Before trusting a residual here, check that the two
    /// spellings differ by exactly one apostrophe.
    /// </para>
    /// </remarks>
    private static readonly string FSF8 = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c'8 dis4 r4 r8 | }
        }

        form main { ~Main }

        score main "FSF8" { staff m }
        """;

    /// <summary>The same book with the accidental an octave UP, where it faces the notehead
    /// instead of the flag — the control that holds everything but the pitch.</summary>
    private static readonly string FSFH8 = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c'8 dis'4 r4 r8 | }
        }

        form main { ~Main }

        score main "FSFH8" { staff m }
        """;

    /// <summary>An UP stem's flag, which stands beside the head and sets the column's reach on
    /// its own (2.167400) — no draw-versus-reserve split there, so this one says whether the
    /// ordinary flagged case was already right.</summary>
    private static readonly string FSFU8 = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { d8 fis'4 r4 r8 | }
        }

        form main { ~Main }

        score main "FSFU8" { staff m }
        """;

    /// <summary>
    /// TWO QUARTERS, the right one carrying a sharp — a column pair whose gap is decided by
    /// the FLOOR and by nothing else, with no flag anywhere in the book.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (probe column-floor.ly, score XQS): <c>\time 4/4 c''4 dis''4 r2</c> —
    /// 3.354200 = 1.404200 (the head's ink plus its own 0.1 extra-spacing-width) + 1.650000
    /// (the sharp's ink 1.450000 plus the Accidental grob's declared 0.2 on the left) + 0.3.
    /// <para>
    /// WHAT IT IS FOR: the three points of <see cref="FSF8"/>'s island all carried the same
    /// +0.100000 once the flag term was closed, and this book — which has no flag — carries
    /// it too. MeasureLayouter raised the spring's minimum to the ROD (the skyline distance
    /// plus the spacing spanner's 0.1) and only then applied merge_springs' headroom, so the
    /// floor read skyline + 0.4. LilyPond keeps the two apart: the spring's minimum is the
    /// padding-free skyline distance (lily/note-spacing.cc:78-83) and the rod stands beside
    /// it (lily/separation-item.cc:47-68), 0.2 below what the headroom answers, so it cannot
    /// bind at force &gt;= 0.
    /// </para>
    /// <para>
    /// ⚠️ NO ACCIDENTAL-FREE BOOK CAN TAKE THIS POINT'S PLACE, and column-floor.ly measures
    /// why rather than asserting it: the duration ideal bottoms out at 2.504200 (a 32nd pair
    /// and a 64th pair both report it) while a head-to-head floor is only 1.804200, so an
    /// accidental-free pair is spring-bound at every duration.
    /// </para>
    /// </remarks>
    private static readonly string CFQS = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c'4 dis'4 r2 | }
        }

        form main { ~Main }

        score main "XQS" { staff m }
        """;

    /// <summary>The same book with the accidental taken away, which makes it SPRING-bound —
    /// a null result kept as a point, because it is what says the duration side of the spring
    /// was never the thing that moved (it was exact before the floor was fixed and after).
    /// </summary>
    private static readonly string CFQN = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c'4 d'4 r2 | }
        }

        form main { ~Main }

        score main "XQN" { staff m }
        """;

    /// <summary>The same floor with a WIDER accidental: the residual under the defect was the
    /// same 0.100000 here as at <see cref="CFQS"/>'s 0.468000 narrower floor, which is what
    /// said the term was a constant and not a glyph width or a padding of the accidental's own.
    /// </summary>
    private static readonly string CFQD = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c'4 deses'4 r2 | }
        }

        form main { ~Main }

        score main "XQD" { staff m }
        """;

    /// <summary>…and the same wide accidental with the left column FLAGGED at an eighth. It
    /// answers <see cref="CFQD"/> to six places in LilyPond, which is the arithmetic statement
    /// that the flag contributes nothing to this gap — a down stem stands at the head's LEFT
    /// edge, so its flag hangs inside the head's shadow.</summary>
    private static readonly string CFFD = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c'8 deses'4 r4 r8 | }
        }

        form main { ~Main }

        score main "XFD" { staff m }
        """;

    /// <summary>
    /// A cue REGION, whose columns read the cue head's own metric.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (probe cue-span.ly, score A-HIGH):
    /// <c>\time 4/4 g''4 g'' \new CueVoice { g''4 g'' }</c> — column steps
    /// 3.002245 / 2.898045 / 2.513394, against <see cref="CUEC"/>'s three identical
    /// 3.002245. The cue→cue step is the direct reading: LilyPond refines a note spring's
    /// ideal by the LEFT column's head width (lily/note-spacing.cc:77), and
    /// <c>3.002245 − (1.304200 − 0.815348908) = 2.513394</c> to nine places, so this step IS
    /// the cue head's width with the full-size one subtracted.
    /// </remarks>
    private static readonly string CUE1 = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { g'4 g' cue { g'4 g' } | }
        }

        form main { ~Main }

        score main "CUE1" { staff m }
        """;

    /// <summary>The same four quarters with no cue anywhere — the one-variable control, and
    /// the dowry: if it does not open EXACT, the cue books are not measuring the cue.</summary>
    private static readonly string CUEC = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { g'4 g' g'4 g' | }
        }

        form main { ~Main }

        score main "CUEC" { staff m }
        """;

    /// <summary>
    /// A cue ACCIDENTAL, read inside its own column where no spring can hide it.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (probe cue-span.ly, score A-ACCX): <c>\time 4/4 c''4 \new CueVoice
    /// { fis''4 } r2</c> — the cue sharp's extent in its column is
    /// <c>(−1.042956577 . −0.350000)</c>, so the anchor distance is 1.042956577.
    /// <para>
    /// ⚠️ TWO FACTS IN ONE NUMBER, and they pull opposite ways — the same shape the grace
    /// accidental turned out to have. The GLYPH is the thirteen design (0.692956577, not
    /// 1.100000 × any scale of the twenty), while the PADDING does not shrink at all: the
    /// right edge is exactly −0.350000, the full-size <c>right-padding</c> + <c>padding</c>
    /// that <c>lily/accidental-placement.cc:391-416 position_apes</c> reads raw.
    /// A single scalar cannot produce both, which is why this point exists.
    /// </para>
    /// <para>
    /// The book carries ONE accidental on purpose: the reading takes the first, and a
    /// full-size accidental earlier in the bar would be the one measured.
    /// </para>
    /// </remarks>
    private static readonly string CUEA = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c'4 cue { fis'4 } r2 | }
        }

        form main { ~Main }

        score main "CUEA" { staff m }
        """;

    /// <summary>
    /// A GRACE inside a cue, where the two font sizes COMPOUND.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (probe cue-span.ly, score C-GRACE): <c>\time 4/4 c''2 \new CueVoice
    /// { \grace { d''16 } e''2 }</c>. MEASURED: the grace head inside the cue answers
    /// <c>font-size −7.0</c> — the CueVoice context's −4 plus the grace's own −3 — and its
    /// box is 0.574399405 against a full-size black head's 1.304200. The grace→main step
    /// inside the region is 1.377510498.
    /// <para>
    /// ⚠️ 0.574399405 / 1.304200 = 0.440421, and <c>magstep(−7) = 0.445449</c>. Not equal,
    /// for the third time in this corpus: the design the size selects is not the twenty
    /// scaled. WHICH design it is has NOT been measured — the number is the target, and
    /// naming the design is the port's job.
    /// </para>
    /// </remarks>
    private static readonly string CUEG = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c'2 cue { grace { d'16 } e'2 } | }
        }

        form main { ~Main }

        score main "CUEG" { staff m }
        """;

    /// <summary>
    /// A note BEFORE the grace, so the approach spring is visible — with its control in the
    /// same book.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score GCWP): <c>\time 4/4 c'4 \grace { d'16 e' } f'4 g'2</c>. The
    /// c' → grace gap is 2.401796 and the f' → g' gap in the same book is 3.002245, and
    /// <c>3.002245 * 0.8 = 2.401796</c> to fifteen places: the approach is an ORDINARY note
    /// spring (lily/spacing-basic.cc:148-162 takes the main-part branch, because the LEFT
    /// column has no grace part) multiplied by lily/spacing-spanner.cc:396-403's 0.8.
    /// <para>
    /// The control has to live in THIS book. A grace changes the score's
    /// common-shortest-duration, so `c'4 f'4 g'2` on its own measures a different spring —
    /// the trap HANDOFF 5.0 calls "the pair's two sides were not the same music", in its
    /// spacing form. Both left heads here are black quarter heads and neither pair reaches
    /// <c>same_direction_correction</c> (adjacent staff positions give delta 1, and the rule
    /// wants delta &gt; 1, lily/note-spacing.cc:162-197), so the two springs differ by the 0.8
    /// and by nothing else.
    /// </para>
    /// </remarks>
    private static readonly string GCWP = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c4 grace { d16 e } f4 g2 | }
        }

        form main { ~Main }

        score main "GCWP" { staff m }
        """;

    /// <summary>
    /// A beam over CHORDS — measure 2 of test/dense-chromatic, which a handoff had recorded
    /// as a stem-direction divergence.
    /// </summary>
    /// <remarks>
    /// Every other beam point in this file is a beam over single note heads. A chord is a
    /// different input to the same quanter: the stem attaches at one extreme head and its
    /// length is reckoned from the other (lily/stem.cc:103-112 Stem::head_positions,
    /// lily/stem.cc:114-122 Stem::chord_start_y).
    /// <para>
    /// LilyPond twin (audit/lp-geometry/probes/beam-chord.ly, score A) — MEASURED, all four
    /// stems DOWN at heads (1 3 5), (2 4 6), (3 5 7), (3 5 7), and
    /// <c>Beam.positions</c> <c>(-1.81 . -1.19)</c>.
    /// </para>
    /// ⚠️ The handoff's claim — "the chords' stem direction is the opposite of LilyPond's,
    /// LP up and Lily# down" — is FALSE, and this point exists to keep it dead. What made it
    /// possible: the fixture's first bar holds twelve sixteenths in 4/4, and Lily#'s <c>|</c>
    /// is a bar line while LilyPond's bare <c>|</c> is only a bar CHECK that does not reset
    /// the measure position. Transcribed with the <c>|</c> kept, LilyPond beams the four
    /// chords as TWO groups starting three quarters into a bar — a different piece of music,
    /// whose beams say nothing about Lily#'s.
    /// </remarks>
    private static readonly string BQC = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { <cis' e' gis'>8 <d' f' aes'> <ees' ges' bes'> <e' g' b'> r2 | }
        }

        form main { ~Main }

        score main "BQC" { staff m }
        """;

    /// <summary>
    /// The CONTROL for <see cref="BQC"/>: the same rhythm with only each chord's BOTTOM
    /// note.
    /// </summary>
    /// <remarks>
    /// The stems point down, so the head at the stem's far end is the bottom one and this is
    /// what the beam would do with the upper chord notes taken away. LilyPond answers the
    /// IDENTICAL <c>(-1.81 . -1.19)</c> (score B of the same probe), which is the useful
    /// shape: LilyPond is an identity across the pair, so any difference Lily# shows between
    /// these two readings is a defect by definition, with no LilyPond-side quantity to
    /// explain it away.
    /// </remarks>
    private static readonly string BQCC = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { cis'8 d' ees' e' r2 | }
        }

        form main { ~Main }

        score main "BQCC" { staff m }
        """;

    /// <summary>
    /// The chord beam that <see cref="BQC"/> and <see cref="BQCC"/> cannot see: one where the
    /// chord's arithmetic MEAN and its beam-side HEAD give different answers.
    /// </summary>
    /// <remarks>
    /// LilyPond reads exactly one head of a chord — the extreme in the stem's own direction
    /// (lily/stem.cc:1214-1215 <c>head_positions (me)[my_dir]</c>, and lily/stem.cc:114-122
    /// <c>chord_start_y</c> via <c>last_head</c>, which is the same head). Lily# read the mean
    /// of the heads, which is not a head and which no LilyPond expression computes.
    /// <para>
    /// BQC/BQCC could not catch it: their beams sit near the middle line, where
    /// lily/stem.cc:1239 clamps the ideal Y to 0 whichever head it came from, so both readings
    /// land on the same quant. Here the chord spans the staff (heads -3, -1, +3, stems UP) and
    /// nothing clamps: the floor decides alone, at <c>1.5 + 2.24 = 3.74</c> from the real head
    /// against <c>0 + 2.24 = 2.24</c> from the mean. LilyPond's own quant is the first one
    /// above 3.74.
    /// </para>
    /// LilyPond twin (audit/lp-geometry/probes/beam-chord.ly, score D) — MEASURED, heads
    /// <c>(-3 -1 3)</c> and <c>Beam.positions</c> <c>(3.81 . 2.19)</c>. Forced toward the
    /// mean's answer with <c>inspect-quants</c>, LilyPond's <c>debug-beam-scoring</c> card
    /// reads <c>L 942.03</c> against <c>L 5.91</c> for its own.
    /// <para>
    /// This is the notation staff of <c>test/tab-beam-slope</c>, and it only became readable
    /// once the exporter stopped spelling that chord's octaves differently from the page.
    /// </para>
    /// </remarks>
    private static readonly string BQCD = """
        octave absolute
        time 4/4
        key c major

        part m { clef bass }

        section Main {
          m { <a,, c, g,>8 a,, a,, a,, e,, f,, g,, a,, | }
        }

        form main { ~Main }

        score main "BQCD" { staff m }
        """;

    /// <summary>
    /// The CONTROL for <see cref="BQCD"/>: the same bar with the chord replaced by its
    /// BEAM-SIDE head alone.
    /// </summary>
    /// <remarks>
    /// <c>calc_stem_info</c> reads that one head and no other, so LilyPond must answer the
    /// identical pair — measured (score E), it does. That identity is what makes the pair
    /// worth keeping: LilyPond is flat across it, so any difference Lily# shows between the
    /// chord reading and this one is a defect by definition. Under the mean the pair was
    /// broken by a full staff space; the beam-side head restores it.
    /// </remarks>
    private static readonly string BQCDC = """
        octave absolute
        time 4/4
        key c major

        part m { clef bass }

        section Main {
          m { g,8 a,, a,, a,, e,, f,, g,, a,, | }
        }

        form main { ~Main }

        score main "BQCDC" { staff m }
        """;

    /// <summary>
    /// A TAB beam — the first ledger entry on the tab quanter at all, and the regime every
    /// other beam point misses because a tab staff is a different staff, not a different beam.
    /// </summary>
    /// <remarks>
    /// LilyPond runs tab beams through the same quanter; what changes is the STAFF, and
    /// everything the quanter reads is in that staff's own spaces. A TabStaff's space is 1.5,
    /// so LilyPond re-tunes exactly two beam constants for it and leaves every length alone
    /// (ly/engraver-init.ly:1234-1246 — beam-thickness, length-fraction, staff-symbol-staff-space):
    /// <c>beam-thickness 0.32</c> (= 0.48/1.5, the absolute thickness kept) and
    /// <c>length-fraction 0.62</c>. The quanter then divides both thicknesses by the staff
    /// space again (lily/beam-quanting.cc:232-234 beam_thickness_ and line_thickness_), which
    /// puts the sit/hang quants at 0.12667 and the beam translation at 0.480667.
    /// <para>
    /// The music is <c>test/tab-string-pinned</c>'s, tab staff only, and every note names its
    /// STRING. That is what makes it comparable at all: the two engines' string allocators do
    /// not agree, and a beam sits on the string — a book that leaves the choice open compares
    /// two different fingerings.
    /// </para>
    /// LilyPond twin (audit/lp-geometry/probes/beam-tab.ly) — MEASURED, four groups:
    /// <c>(-3.873333 . -2.126667)</c>, <c>(-2.126667 . -3.873333)</c>, <c>(1.5 . 1.5)</c>,
    /// <c>(-1.5 . -1.5)</c>, in the tab staff's own spaces.
    /// <para>
    /// ⚠️ UNIT: read with <c>TabBeamPositionAboveStaffMiddle</c>, which answers in the TAB
    /// staff's own spaces, so the ledger holds LilyPond's numbers unconverted.
    /// <c>BeamPositionAboveStaffMiddle</c> would answer in DRAWN spaces (a tab space is 1.5 of
    /// them) AND refuse the page for not being a whole number of five-line staves; recording
    /// its answer against LilyPond's would be the em mistake in another costume.
    /// </para>
    /// </remarks>
    private static readonly string BQT = """
        octave absolute
        time 4/4
        key c major

        part bl { clef bass tuning bass }

        section Main {
          bl { a,,8\4 d,\3 g,\2 c\1 c\1 g,\2 d,\3 a,,\4 | a,,8\4 a,,\4 a,,\4 a,,\4 c\1 c\1 c\1 c\1 | }
        }

        form main { ~Main }

        score main "BQT" { tab bl }
        """;

    /// <summary>
    /// A beam quanted against the STEM of a note in another voice — the half of the
    /// covered-grob supply that is not a box at all.
    /// </summary>
    /// <remarks>
    /// LilyPond books, per covered grob, a SECOND collision made from that grob's
    /// <c>stem</c> object: an interval starting at <c>Stem::chord_start_y</c> and running to
    /// INFINITY in the stem's direction (lily/beam-quanting.cc:401-418), weighted by
    /// <c>STEM_COLLISION_FACTOR</c>. Here voice one's two eighths fill beat one with stems
    /// up, and voice two's sixteenths hang a fifth above them with stems DOWN reaching into
    /// the beam's path: the covered head's own box leaves the beam alone (it clears by 0.76,
    /// past the 0.35 padding), so nothing but that interval can lift the beam.
    /// <para>
    /// LilyPond twin (audit/lp-geometry/probes/beam-over-stem.ly, score qA):
    /// <c>&lt;&lt; { b'8 b' s2. } \\ { s16 d'''16 d''' d''' s2. } &gt;&gt;</c> — the measured
    /// beam's <c>Beam.positions</c> is <c>(5.81 . 5.81)</c>. That probe's IDENTITY pair (the
    /// same run with <c>Beam.details.stem-collision-factor</c> at 0) answers
    /// <c>(3.0 . 3.0)</c>, so the whole 2.81 is this one supply.
    /// </para>
    /// ⚠️ Eighths for the measured beam and sixteenths for the covering voice ON PURPOSE:
    /// a sixteenth group draws two full-width beam lines and
    /// <see cref="RenderedGeometry.BeamPositionAboveStaffMiddle"/> names a group's line by x
    /// containment, which cannot tell two equally wide lines apart. Beat one, and the rest of
    /// the bar a skip, so exactly one beam of two eighths is drawn and there is nothing to
    /// name it against. (⚠️ The reason written here used to be "Lily# ends an eighth beam at
    /// every quarter where LilyPond ends it at the half-measure". That divergence is closed —
    /// both end it at the half measure now — and this book never depended on it: what ends
    /// the beam here is the skip, in both engines.)
    /// </remarks>
    private static readonly string BQM = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m {
            voice { b8 b s2. }
            { s16 d''16 d'' d'' s2. }
          }
        }

        form main { ~Main }

        score main "BQM" { staff m }
        """;

    /// <summary>
    /// <see cref="BQM"/> with the covering note UNBEAMED — the same lift through a different
    /// door.
    /// </summary>
    /// <remarks>
    /// The weight is 1.0 rather than 0.1 when the covered grob's stem carries no beam of its
    /// own (lily/beam-quanting.cc:415-416), and such a stem is also a covered grob in its OWN
    /// right — <c>Beam_collision_engraver</c> drops only BEAMED stems
    /// (lily/beam-collision-engraver.cc:179-181). So its drawn box, head to tip, already
    /// spans the beam's path. This book and <see cref="BQM"/> are therefore a FORK, not a
    /// repetition: BQM can only be explained by the infinite interval, this one can also be
    /// explained by booking the Stem's box, and both must land.
    /// <para>
    /// LilyPond twin (score qB of the same probe):
    /// <c>&lt;&lt; { b'8 b' s2. } \\ { s16 d'''4 s8. s2 } &gt;&gt;</c> — <c>(5.81 . 5.81)</c>,
    /// the same seat BQM takes. ⚠️ No override reaches the 1.0, so this book has no
    /// LilyPond-side identity twin; <see cref="BQF"/> is its control instead.
    /// </para>
    /// </remarks>
    private static readonly string BQU = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m {
            voice { b8 b s2. }
            { s16 d''4 s8. s2 }
          }
        }

        form main { ~Main }

        score main "BQU" { staff m }
        """;

    /// <summary>
    /// The CONTROL for <see cref="BQM"/> and <see cref="BQU"/>: the same beam with nobody
    /// overhead.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score qC of the same probe):
    /// <c>&lt;&lt; { b'8 b' s2. } \\ { s1 } &gt;&gt;</c> — <c>(3.0 . 3.0)</c>. This is what says
    /// the other two are not sitting on a floor: 5.81 is 2.81 of real lift, and a paper or
    /// spacing mismatch would move this reading too. The second voice is kept (as a bar of
    /// skips) rather than deleted, because a lone voice would let Lily# pick the stem
    /// direction from the pitch and b sits ON the middle line.
    /// </remarks>
    private static readonly string BQF = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m {
            voice { b8 b s2. }
            { s1 }
          }
        }

        form main { ~Main }

        score main "BQF" { staff m }
        """;

    // ---------------------------------------------------------------------------------
    // Which side does an interior stem's BEAMLET point? (audit/lp-geometry/probes/
    // beam-beamlet.ly). Six books, one per branch of the rule, all with MANUAL brackets so
    // the grouping is not also under test, all starting on beat one, all filling the bar
    // exactly so the beat arithmetic in the rule is the intended one.
    //
    // Lily# gives an interior member left = min(count, prev) and right = min(count, next)
    // (BeamDetector.CreateBeamGroup). LilyPond starts a stem with its OWN count on BOTH
    // sides (lily/beaming-pattern.cc:50-62) and then, for interior stems only, picks ONE
    // side to keep and chips the other (:121-183). The two agree whenever a stem's count
    // exceeds only one neighbour's; when it exceeds BOTH, min() deletes the stub on both
    // sides and the stem draws nothing.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Counts <c>1 2 1</c> — the reported defect. The sixteenth between two eighths draws no
    /// stub at all in Lily#, where LilyPond gives it a full second beam on one side.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score A): <c>c'8[ c'16 c'8] r8. r2</c> — stem 1's <c>beaming</c> is
    /// <c>((0) 1 0)</c>, i.e. one beam on the left and TWO on the right.
    /// <para>
    /// This is the branch that costs the most to reach: the neighbours' counts are equal, so
    /// the rule falls past the <c>right_count &gt; left_count</c> test; the sixteenth starts
    /// neither on a beat nor ends on the next one, so it falls past the moment test too; and
    /// the answer comes from <c>rhythmic_importance_</c>, which is a whole second pass
    /// (:292-404). The simplest case anybody would report is the one that needs all of it.
    /// </para>
    /// </remarks>
    private static readonly string BLA = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c8[ c16 c8] r8. r2 | }
        }

        form main { ~Main }

        score main "BLA" { staff m }
        """;

    /// <summary>
    /// Counts <c>2 1 2</c> — the interior stem exceeds NEITHER neighbour, so nothing is
    /// chipped and both engines must answer one beam each side.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score B): <c>c'16[ c'8 c'16] r4 r2</c> — <c>((0) 0)</c>.
    /// This is the control that says the port did not start chipping stems it should leave
    /// alone: a rule that always kept its own count on one side would answer 1 . 1 here too,
    /// but only because the eighth HAS one beam — see <see cref="BLC"/> and
    /// <see cref="BLD"/>, where keeping the own count is visible.
    /// </remarks>
    private static readonly string BLB = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c16[ c8 c16] r4 r2 | }
        }

        form main { ~Main }

        score main "BLB" { staff m }
        """;

    /// <summary>
    /// Counts <c>2 2 1</c> — the interior stem exceeds exactly ONE neighbour, the right one,
    /// so the flag points LEFT and the RIGHT side is chipped.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score C): <c>c'16[ c'16 c'8] r4 r2</c> — <c>((1 0) 0)</c>, two beams
    /// left and one right. <c>min()</c> lands on the same pair here, which is why this book
    /// and <see cref="BLD"/> are the pair that says WHY the two rules usually agree rather
    /// than merely that they do: with one neighbour lower, the side min() deletes is the same
    /// side LilyPond chips.
    /// </remarks>
    private static readonly string BLC = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c16[ c16 c8] r4 r2 | }
        }

        form main { ~Main }

        score main "BLC" { staff m }
        """;

    /// <summary>
    /// Counts <c>1 2 2</c> — <see cref="BLC"/> mirrored: the flag points RIGHT and the LEFT
    /// side is chipped.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score D): <c>c'8[ c'16 c'16] r4 r2</c> — <c>((0) 1 0)</c>. Both
    /// orientations, because the rule's first branch is an inequality between the two
    /// neighbours and a port that got its sense backwards would still pass <see cref="BLC"/>.
    /// </remarks>
    private static readonly string BLD = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c8[ c16 c16] r4 r2 | }
        }

        form main { ~Main }

        score main "BLD" { staff m }
        """;

    /// <summary>
    /// Counts <c>2 1 1 2</c> — the only book with TWO interior stems, and neither is touched.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score E): <c>c'16[ c'8 c'8 c'16] r8 r2</c> — <c>((0) 0)</c> for both
    /// interior stems. The chip loop and the correction pass at :161-167 both run over a
    /// four-element pattern here, so this is what says a port did not make the counts depend
    /// on the group's LENGTH.
    /// ⚠️ The LilyPond probe's header claims this book gives the correction pass "something to
    /// correct". It does not: both interior stems come out CENTER, and the pass only ever
    /// rewrites a CENTER that stands next to a LEFT or a RIGHT.
    /// </remarks>
    private static readonly string BLE = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c16[ c8 c8 c16] r8 r2 | }
        }

        form main { ~Main }

        score main "BLE" { staff m }
        """;

    /// <summary>
    /// Counts <c>1 3 1</c> — <see cref="BLA"/> with the gap two beams wide, so the chip is
    /// bigger than one and the kept side carries THREE beams.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score F): <c>c'8[ c'32 c'8] r8 r16. r2</c> — <c>((0) 2 1 0)</c>, one
    /// beam left and three right. This is what separates "the stub is one line" from the
    /// actual rule: the opposite side is reduced by <c>max(count − neighbour, 1)</c>
    /// (:179-181), which is 2 here, and the kept side is not reduced at all.
    /// </remarks>
    private static readonly string BLF = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c8[ c32 c8] r8 r16. r2 | }
        }

        form main { ~Main }

        score main "BLF" { staff m }
        """;

    // ---------------------------------------------------------------------------------
    // WHERE DOES AN AUTOMATIC BEAM END? (audit/lp-geometry/probes/beam-grouping.ly)
    //
    // LilyPond ends a beam at the ends of the beats its meter declares: beatBase (one over
    // the denominator) times beatStructure, which is (1 1 1 …) for most meters, (3 3 …) when
    // the numerator is over three and divisible by three, and an explicit uneven list for the
    // three whose table entry overrides that — 4/8 as 2+2, 5/8 as 3+2, 8/8 as 3+3+2
    // (scm/time-signature-settings.scm:125-171 default-time-signature-settings). Over that
    // sit beamExceptions, which beam eighths BEYOND the beat: 4/4 by the half measure, 3/4
    // and 2/8 and 3/8 by the whole one.
    //
    // Lily# had a second, flatter spelling (BeamDetector's beatLength): the dotted quarter
    // for compound meters, one over the denominator otherwise. It agrees with LilyPond for
    // 4/4, 3/4, 2/4, 6/8, 9/8 and 12/8 and disagrees for every other x/8 meter — and since a
    // grid of one eighth per group leaves each group holding a single note, the disagreement
    // is not a differently-placed beam but NO BEAM AT ALL.
    //
    // Every book is plain eighths filling one bar with no manual bracket, so the grouping is
    // the only thing under test. The controls carry the same weight as the rest: a change to
    // the grid that moves 4/4 or 6/8 has broken what LilyPond already agreed with.
    // ---------------------------------------------------------------------------------

    /// <summary>Builds a one-bar book of <paramref name="count"/> eighths in a meter.</summary>
    private static string EighthsIn(string name, int beats, int beatType, int count) => $$"""
        octave absolute
        time {{beats}}/{{beatType}}
        key c major

        part m { clef treble }

        section Main {
          m { {{string.Join(" ", Enumerable.Repeat("c8", count))}} | }
        }

        form main { ~Main }

        score main "{{name}}" { staff m }
        """;

    /// <summary>4/8 — beatStructure (2 2). LilyPond twin: score M48, two beams of two.</summary>
    private static readonly string BG48 = EighthsIn("BG48", 4, 8, 4);

    /// <summary>5/8 — beatStructure (3 2). LilyPond twin: score M58, beams of three and two.</summary>
    private static readonly string BG58 = EighthsIn("BG58", 5, 8, 5);

    /// <summary>8/8 — beatStructure (3 3 2). LilyPond twin: score M88, three beams.</summary>
    private static readonly string BG88 = EighthsIn("BG88", 8, 8, 8);

    /// <summary>
    /// 2/8 — the whole measure, and NOT from the beat structure.
    /// </summary>
    /// <remarks>
    /// Its structure is the default (1 1), which would break the beam between the two
    /// eighths; what joins them is the beamException <c>(end (1/8 . (2)))</c>
    /// (scm/time-signature-settings.scm:81). So this book and <see cref="BG38"/> separate the
    /// two mechanisms: a port that fixes only the beat structure leaves these two unbeamed.
    /// LilyPond twin: score M28, one beam of two.
    /// </remarks>
    private static readonly string BG28 = EighthsIn("BG28", 2, 8, 2);

    /// <summary>3/8 — one beam of three, likewise from a beamException, not the beats.</summary>
    /// <remarks>LilyPond twin: score M38 (scm/time-signature-settings.scm:104).</remarks>
    private static readonly string BG38 = EighthsIn("BG38", 3, 8, 3);

    /// <summary>CONTROL 4/4 — two beams of four, from the half-measure beamException.</summary>
    private static readonly string BG44 = EighthsIn("BG44", 4, 4, 8);

    /// <summary>CONTROL 3/4 — one beam of six, from the whole-measure beamException.</summary>
    private static readonly string BG34 = EighthsIn("BG34", 3, 4, 6);

    /// <summary>CONTROL 2/4 — two beams of two: no exception, so the bare quarter beat.</summary>
    private static readonly string BG24 = EighthsIn("BG24", 2, 4, 4);

    /// <summary>CONTROL 6/8 — two beams of three, the compound default (3 3).</summary>
    private static readonly string BG68 = EighthsIn("BG68", 6, 8, 6);

    /// <summary>CONTROL 9/8 — three beams of three, the compound default (3 3 3).</summary>
    private static readonly string BG98 = EighthsIn("BG98", 9, 8, 9);

    /// <summary>
    /// CONTROL: sixteenths in 4/4 — four beams of four.
    /// </summary>
    /// <remarks>
    /// The one control that is NOT pure eighths, so the 1/8 entry cannot apply: the lookup
    /// takes `larger-setting` to 4/4's 1/12 entry, whose groups of three twelfths are the
    /// quarter — not the half measure. (⚠️ NOT "the bare beat structure answers", which is
    /// only true because the two agree in 4/4; see BG64S, where they do not.) Without it, a port could
    /// satisfy every book above by beaming eighths per measure and nothing would notice.
    /// LilyPond twin: score C44S.
    /// </remarks>
    private static readonly string BG44S = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c16 c c c c c c c c c c c c c c c | }
        }

        form main { ~Main }

        score main "BG44S" { staff m }
        """;

    /// <summary>
    /// A REST inside what the eighth-note exception would otherwise beam as one group.
    /// </summary>
    /// <remarks>
    /// 3/4 beams eighths by the WHOLE measure, and this bar's six slots hold two runs of
    /// eighths either side of a rest. LilyPond (score M34R) draws TWO beams, of two and
    /// three: a rest ends a beam whatever the exception says.
    /// <para>
    /// ⚠️ 3/4 is the only meter where the question can be put to eighths at all — 4/4's
    /// exception group is a half measure, four eighths, which cannot hold two runs of two AND
    /// a rest. That is why nothing noticed: Lily# merges consecutive pure-eighth groups
    /// inside the exception's length without asking whether they are ADJACENT IN TIME, and
    /// the only meter that can show it is 3/4.
    /// </para>
    /// </remarks>
    private static readonly string BG34R = """
        octave absolute
        time 3/4
        key c major

        part m { clef treble }

        section Main {
          m { c8 c r8 c c c | }
        }

        form main { ~Main }

        score main "BG34R" { staff m }
        """;

    /// <summary>
    /// Three eighths inside ONE exception group, split unevenly by the beats underneath.
    /// </summary>
    /// <remarks>
    /// The half measure from 1/2 holds eighths at 1/2, 5/8 and 3/4, and 4/4's beat boundary
    /// at 3/4 falls between the second and the third. LilyPond (score M44B) beams all three:
    /// the exception is what ends the beam, and it ends it at the bar.
    /// <para>
    /// ⚠️ This is the point that watches the OTHER half of the grid port. Lily#'s first pass
    /// groups by the beat and throws away any group holding a single note, so the eighth at
    /// 3/4 is gone before the exception pass can claim it — and once the beat grid becomes
    /// LilyPond's, every meter whose exception spans several beats (2/8 and 3/8 among them)
    /// depends on those one-note groups surviving to be merged.
    /// </para>
    /// </remarks>
    private static readonly string BG44B = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c2 c8 c c r8 | }
        }

        form main { ~Main }

        score main "BG44B" { staff m }
        """;

    // --- the beamExceptions that are NOT keyed on an eighth. 3/4 and 4/4 each carry a second
    // entry at 1/12 (scm/time-signature-settings.scm:100 and :121) whose own comment says what
    // it is for: "Anything shorter by beat … we set triplets back to every beat". A tuplet's
    // notes are looked up by their ACTUAL length, so an eighth inside a 3/2 tuplet is a
    // twelfth. What the entry asks for — threes of a twelfth — IS the quarter, the same as the
    // bare beat structure; all it really does is keep the EIGHTH entry away from triplets.
    //
    // ⚠️ Which means the entry is only observable through what it PREVENTS, and Lily#, which
    // ends a beam at every tuplet boundary, already prevents it. The books below were opened
    // to close that gap and found it shut — except for the control, which is where the real
    // divergence turned out to be. See beam.grouping.sixteenth-triplets.groups.

    /// <summary>3/4 filled with eighth triplets. LilyPond twin: score T34, three beams of three.</summary>
    private static readonly string BGT34 = """
        octave absolute
        time 3/4
        key c major

        part m { clef treble }

        section Main {
          m { tuplet 3/2 { c8 c c } tuplet 3/2 { c8 c c } tuplet 3/2 { c8 c c } | }
        }

        form main { ~Main }

        score main "BGT34" { staff m }
        """;

    /// <summary>4/4 filled with eighth triplets. LilyPond twin: score T44, four beams of three.</summary>
    private static readonly string BGT44 = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { tuplet 3/2 { c8 c c } tuplet 3/2 { c8 c c } tuplet 3/2 { c8 c c } tuplet 3/2 { c8 c c } | }
        }

        form main { ~Main }

        score main "BGT44" { staff m }
        """;

    /// <summary>
    /// An eighth triplet followed by plain eighths in 3/4 — two grouping rules in one bar.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score T34M): beams of THREE and FOUR. The triplet's run has a shortest
    /// duration of a twelfth, so the 1/12 entry ends its beam at the quarter; the plain
    /// eighths that follow have a shortest of an eighth, so the 1/8 entry carries them to the
    /// bar line. The break between them is not "a tuplet edge" — it is two different entries
    /// answering for two different runs, which score T44S shows by NOT breaking at one.
    /// </remarks>
    private static readonly string BGT34M = """
        octave absolute
        time 3/4
        key c major

        part m { clef treble }

        section Main {
          m { tuplet 3/2 { c8 c c } c8 c c c | }
        }

        form main { ~Main }

        score main "BGT34M" { staff m }
        """;

    /// <summary>The same mixture in 4/4. LilyPond twin: score T44M, beams of 3, 3 and 4.</summary>
    private static readonly string BGT44M = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { tuplet 3/2 { c8 c c } tuplet 3/2 { c8 c c } c8 c c c | }
        }

        form main { ~Main }

        score main "BGT44M" { staff m }
        """;

    /// <summary>
    /// SIXTEENTH triplets — a twenty-fourth each, for which NO exception entry exists.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score T44S): FOUR beams of SIX. With no entry keyed on a twenty-fourth,
    /// the lookup takes the smallest key that is at least the type
    /// (scm/auto-beam.scm:48-49 larger-setting) to the 1/12 entry, whose
    /// groups of three twelfths are the quarter — so the run groups by the quarter, which means
    /// LilyPond beams
    /// straight ACROSS the boundary between two tuplets, joining two triplets of sixteenths
    /// into one beam.
    /// <para>
    /// ⚠️ This was written as the control for the 1/12 books and is where the divergence
    /// turned out to live: Lily# used to end a beam at every tuplet boundary unconditionally
    /// (BeamDetector's tupletBoundaries), and drew eight beams of three. LilyPond has no such
    /// rule — what breaks the beam in the 1/12 books is the exception LOOKUP changing between
    /// two runs, not the tuplet's edge — and the guard went with the one-pass port, which is
    /// what this book now holds down.
    /// </para>
    /// </remarks>
    private static readonly string BGT44S = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m {
            tuplet 3/2 { c16 c c } tuplet 3/2 { c16 c c }
            tuplet 3/2 { c16 c c } tuplet 3/2 { c16 c c }
            tuplet 3/2 { c16 c c } tuplet 3/2 { c16 c c }
            tuplet 3/2 { c16 c c } tuplet 3/2 { c16 c c } |
          }
        }

        form main { ~Main }

        score main "BGT44S" { staff m }
        """;

    // --- the beamExceptions keyed on 1/16 and 1/32, which no book above reaches.
    //
    // These are the entries that make the LOOKUP decide where a beam ends rather than the beat
    // structure, and they are what a two-pass grouper cannot express: they ask for groups
    // FINER than the beat, and Lily#'s second pass can only merge groups COARSER. Four books
    // where the two answers differ, four controls where they do not — and the controls are
    // what say the rule is the smallest key that is AT LEAST the type
    // (scm/auto-beam.scm:48-49 larger-setting) and not "these meters group by the quarter".
    // LILYPOND-REF: scm/time-signature-settings.scm:69-173 default-time-signature-settings —
    //   the 1/16 and 1/32 entries: 2/2 at :74, 3/2 at :90, 4/2 at :112, 6/4 at :133, and the
    //   1/32 controls 9/4 at :144 and 12/4 at :155.

    /// <summary>Builds a one-bar book of <paramref name="count"/> notes of one value in a meter.</summary>
    private static string NotesIn(string name, int beats, int beatType, int noteValue, int count) => $$"""
        octave absolute
        time {{beats}}/{{beatType}}
        key c major

        part m { clef treble }

        section Main {
          m { c{{noteValue}} {{string.Join(" ", Enumerable.Repeat("c", count - 1))}} | }
        }

        form main { ~Main }

        score main "{{name}}" { staff m }
        """;

    /// <summary>
    /// 6/4 sixteenths — SIX beams of four, from <c>(1/16 . (4 4 4 4 4 4))</c>.
    /// </summary>
    /// <remarks>
    /// LilyPond twin: score S64. The entry is an exact match for the type, so its groups of
    /// four sixteenths — a QUARTER each — answer, while 6/4's beat structure is the compound
    /// default (3 3) and would beam by the dotted HALF. This is the plainest book in the
    /// family: no tuplet, no rest, no manual bracket, and the two engines differ by a factor
    /// of three in group length.
    /// </remarks>
    private static readonly string BG64S = NotesIn("BG64S", 6, 4, 16, 24);

    /// <summary>
    /// 4/2 sixteenths — EIGHT beams of four, from <c>(1/16 . (4 4 4 4 4 4 4 4))</c>.
    /// </summary>
    /// <remarks>
    /// LilyPond twin: score S42. The same shape as <see cref="BG64S"/> against a different
    /// beat: 4/2's structure is (1 1 1 1) of half notes, so the entry's quarter is FOUR times
    /// finer. Both books are needed — a port that read the entry but kept applying it at the
    /// beat would satisfy neither, and one that hard-coded "the quarter" would satisfy both
    /// and then fail the controls below.
    /// </remarks>
    private static readonly string BG42S = NotesIn("BG42S", 4, 2, 16, 32);

    /// <summary>
    /// 2/2 thirty-seconds — FOUR beams of eight, from <c>(1/32 . (8 8 8 8))</c>.
    /// </summary>
    /// <remarks>
    /// LilyPond twin: score T22. ⚠️ Its SIXTEENTHS are exact today and must stay so: a
    /// sixteenth is LARGER than a thirty-second, so <c>larger-setting</c> finds nothing and
    /// the beat structure answers — which is what Lily# already draws. The entry moves the
    /// thirty-seconds only.
    /// </remarks>
    private static readonly string BG22T = NotesIn("BG22T", 2, 2, 32, 32);

    /// <summary>3/2 thirty-seconds — SIX beams of eight, from <c>(1/32 . (8 8 8 8 8 8))</c>.</summary>
    /// <remarks>LilyPond twin: score T32. See <see cref="BG22T"/>.</remarks>
    private static readonly string BG32T = NotesIn("BG32T", 3, 2, 32, 48);

    /// <summary>
    /// CONTROL 9/4 sixteenths — THREE beams of twelve, the dotted-half beat.
    /// </summary>
    /// <remarks>
    /// LilyPond twin: score S94. 9/4 carries an exception too, but keyed on 1/32, and
    /// <c>larger-setting</c> takes the smallest key that is at least the type — a sixteenth is
    /// larger than a thirty-second, so NOTHING is found and the beat structure (3 3 3) of
    /// quarters answers. This is the control that separates "the lookup decides" from "these
    /// meters group by the quarter": a port that applied every 1/16-and-finer entry to every
    /// short note would beam this bar in nine and be wrong.
    /// </remarks>
    private static readonly string BG94S = NotesIn("BG94S", 9, 4, 16, 36);

    /// <summary>CONTROL 12/4 sixteenths — FOUR beams of twelve. See <see cref="BG94S"/>.</summary>
    /// <remarks>LilyPond twin: score S124.</remarks>
    private static readonly string BG124S = NotesIn("BG124S", 12, 4, 16, 48);

    /// <summary>
    /// CONTROL 3/4 thirty-seconds — THREE beams of eight, the quarter.
    /// </summary>
    /// <remarks>
    /// LilyPond twin: score T34X. The mirror of <see cref="BG94S"/>: here the lookup DOES find
    /// an entry — a thirty-second is smaller than a twelfth, so <c>larger-setting</c> takes
    /// 3/4's 1/12 entry — and the groups it asks for, three twelfths, are the quarter, which is
    /// what the beat structure would have said anyway. Exact before the port and after it, for
    /// two different reasons.
    /// </remarks>
    private static readonly string BG34T = NotesIn("BG34T", 3, 4, 32, 24);

    /// <summary>CONTROL 4/4 thirty-seconds — FOUR beams of eight. See <see cref="BG34T"/>.</summary>
    /// <remarks>LilyPond twin: score T44X, via 4/4's own 1/12 entry (3 3 3 3).</remarks>
    private static readonly string BG44T = NotesIn("BG44T", 4, 4, 32, 32);

    /// <summary>
    /// A triplet that starts OFF the beat, with a plain eighth either side of it.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score TOFF): <c>c'8 \tuplet 3/2 { c'8 c' c' } c'8 c'2</c> — ONE beam of
    /// FIVE. LilyPond's end test is exact membership of the measure position in the ending
    /// moments, and none of this run's positions is one: the triplet's notes fall at 1/8, 5/24
    /// and 7/24, never on the quarter its 1/12 entry ends groups at, and the eighth after them
    /// at 3/8 is not an ending moment either. So the beam runs from the first eighth to the
    /// last, straight through both edges of the tuplet.
    /// <para>
    /// ⚠️ This is the only book that ever observed Lily#'s SECOND tuplet guard
    /// (tupletInteriors, which suppressed the beat flush inside a tuplet) — a triplet that
    /// starts ON a beat is already inside one beat and nothing would cut it. Before the
    /// one-pass port Lily# drew one beam of three: tupletBoundaries cut at both edges and the
    /// two lone eighths were dropped as groups of one. The group COUNT is 1 either way, so
    /// only the first-group reading could see it — which is why both are here.
    /// </para>
    /// </remarks>
    private static readonly string BGTOFF = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main {
          m { c8 tuplet 3/2 { c8 c c } c8 c2 | }
        }

        form main { ~Main }

        score main "BGTOFF" { staff m }
        """;

    /// <summary>
    /// CONTROL: three eighths starting at the HALF MEASURE of 3/4 — one beam, not three flags.
    /// </summary>
    /// <remarks>
    /// LilyPond twin (score H34): <c>c'4. c'8 c' c'</c> — ONE beam of three. The START rule
    /// (scm/auto-beam.scm:66-79, the start? branch of default-auto-beam-check) forbids a beam
    /// from starting at the half measure of a 3/N meter, so as not to suggest a 6/N beat
    /// structure — but only when <c>beamHalfMeasure</c> is false, and the default is TRUE
    /// (ly/engraver-init.ly:880, beside autoBeamCheck = #default-auto-beam-check at :882),
    /// so the first branch is
    /// always taken and a beam may always start. This book is what makes it safe to write that
    /// rule down literally: it proves the default answer is "yes" in the one meter and at the
    /// one position where the rule has anything to say.
    /// </remarks>
    private static readonly string BGH34 = """
        octave absolute
        time 3/4
        key c major

        part m { clef treble }

        section Main {
          m { c4. c8 c c | }
        }

        form main { ~Main }

        score main "BGH34" { staff m }
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
          lh { voice { f8 f f f f f f f } { d,1 } | }
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
            { {{string.Concat(Enumerable.Repeat("b1 b1 b1 | ", 6)).Trim()}} }
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
            { {{string.Concat(Enumerable.Repeat("a1 tuplet 3/2 { g,,1 g,,1 g,,1 } | ", 6)).Trim()}} }
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
            { {{string.Concat(Enumerable.Repeat("a1 g,,8 g,, g,, g,, g,, g,, g,, g,, a1 | ", 6)).Trim()}} }
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
            { {{string.Concat(Enumerable.Repeat("b1 b1 b1 | ", 6)).Trim()}} }
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

    // --- the chord symbol's WIDTH, where it becomes geometry (chord-symbol-width.ly) ---
    // Every other chord point is an anchor (or a difference of anchors) in which the
    // symbol's own width cancels — deliberately, since the two engravers' text faces
    // differ. These three read the gap between ADJACENT symbols of the SAME text: both
    // engravers anchor a chord symbol at its ink LEFT, so the right symbol's width
    // cancels and only the LEFT one's priced width survives (where the rod binds).

    /// <summary>A staff-less chords row of the given progression, verbatim.</summary>
    private static string ChordsRowScore(string music, string name) => $$"""
        octave absolute
        time 4/4
        key c major

        section Main {
          chords prog { {{music}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          chords prog
        }
        """;

    /// <summary>
    /// Four adjacent "Am" quarters — the BINDING regime: w + 1.1 (4.3 + 1.1 on the Lily#
    /// side, 3.926480 + 1.1 on LilyPond's) exceeds the quarter-note spring (3.398045,
    /// score CWC), so the gap sits on the rod and reads the priced symbol width itself.
    /// "Am" because the bold-vs-regular advance difference lives almost entirely in the
    /// lowercase m (Heros bold "m" is 7.4% wider); Helvetica-family "C" has the SAME
    /// advance in both weights and would show only the em error.
    /// </summary>
    /// <remarks>LilyPond twin: probe score CWA in chord-symbol-width.ly.</remarks>
    private static readonly string LCWA = ChordsRowScore("a4:m a:m a:m a:m | a1:m |", "LCWA");

    /// <summary>
    /// The same shape on "C" quarters — the SLACK control: w + 1.1 = 2.977882 is under
    /// the quarter spring, so the gap reads the duration SPRING with no text metric in
    /// it. Guards the fork: a width claim on CWA is only meaningful while this is exact.
    /// </summary>
    /// <remarks>LilyPond twin: probe score CWC.</remarks>
    private static readonly string LCWC = ChordsRowScore("c4 c c c | c1 |", "LCWC");

    /// <summary>
    /// "C" halves — the second slack control, one duration step out, so a spring defect
    /// that happened to be exact at quarters would still be caught.
    /// </summary>
    /// <remarks>LilyPond twin: probe score CWH.</remarks>
    private static readonly string LCWH = ChordsRowScore("c2 c | c1 |", "LCWH");

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

        // --- the STAFF spring inside a system, on a page that STRETCHES (books JSS/JSSC) ---
        // The pair asks one question: does the distance between two staves of a system come
        // out of the page's spring chain (LilyPond) or is it a fixed height the page cannot
        // touch (Lily#)? The control is the SAME music with ragged-bottom on, so LilyPond's
        // own difference between the two entries IS the stretch, and Lily#'s is zero.
        // The third entry reads the system spring on the same page, because the slack has to
        // go somewhere: with no staff springs to share it with, Lily#'s system gaps must open
        // WIDER than LilyPond's. A port that adds the staff spring has to close both.
        new("page.natural.staff-staff-inside", JSSC, g => g.StaffGapAt(0), SixSystemsPerPageRagged),
        new("page.stretched.staff-staff-inside", JSS, g => g.StaffGapAt(0), SixSystemsPerPage),
        new("system.stretched-distance.two-staff", JSS, g => g.StaffGapAt(1), SixSystemsPerPage),

        // The structural guard the three above cannot give: they read gaps by INDEX, and an
        // index means the staff it is supposed to mean only while the page holds the staves
        // this probe assumes. Six two-staff systems = twelve staves.
        new("page.stretched.two-staff.staves-on-first-page", JSS, g => g.StavesOnPage(0), SixSystemsPerPage),

        // The THIRD regime of the same spring (book JSK): squeezed. Compression runs on a
        // different strength from stretching — ideal - minimum-distance, 2 for the staff
        // spring and 4 for the system one, against 5 and 60 — so a port that has the spring
        // but takes its strengths from the wrong place is green on the stretched pair and
        // wrong here. The natural entry above is the control for both directions.
        new("page.compressed.staff-staff-inside", JSK, g => g.StaffGapAt(0), EightSystemsPerPage),
        new("system.compressed-distance.two-staff", JSK, g => g.StaffGapAt(1), EightSystemsPerPage),
        new("page.compressed.two-staff.staves-on-first-page", JSK, g => g.StavesOnPage(0), EightSystemsPerPage),

        // WHAT UNIT A STAFF-TO-STAFF DISTANCE IS IN when one staff is a TAB staff, opened as
        // a pair 2026-07-28 (books TABS / NST). LilyPond reads BOTH at 9.000000 — the same
        // staff-staff-spacing basic-distance a notation pair gets — because Align_interface
        // works between VerticalAxisGroup REFERENCE POINTS (a staff's middle line, six-line or
        // five-line) and nothing in TabStaff overrides the spec. Its side is therefore a
        // CONTROL and whatever Lily# shows between the two spellings is entirely Lily#'s.
        //
        // ⚠️ THE FLOOR DOES NOT BIND ON EITHER SIDE, which is what makes the reading the
        // SPEC's: both books came back at exactly 9.000000 rather than at their ink. The
        // pitches are kept inside both staves for that reason, and a reading ABOVE 9.000000
        // on the LilyPond side would mean the pair had stopped measuring the unit.
        //
        // ⚠️ THE CONTROL IS CARRIED RATHER THAN ASSUMED. NST is the same music with the lower
        // staff spelled as an ordinary Staff; it exists so that "the tab entry is off" cannot
        // be confused with "this whole arrangement is off".
        // ⚠️ READ WITH THE LINE COUNTS GIVEN: a six-string tab staff draws six lines 1.5
        // apart, so StaffGapAt's five-line grouping cannot see it and says so rather than
        // returning a plausible number.
        new("staff.tab-pair.staff-staff-inside", TabPairScoreTab,
            g => g.StaffRefpointGap(5, 6)),
        new("staff.notation-pair.staff-staff-inside", TabPairScoreNotation,
            g => g.StaffRefpointGap(5, 5)),

        // THE SAME QUESTION FOR THE OTHER SHRUNK STAFF (books OSSU / OSSUN): what a
        // staff-to-staff distance is in when the upper staff is an OSSIA. A tab staff is
        // taller than four staff spaces; an ossia is SMALLER, and by a different mechanism —
        // fontSize -3 with StaffSymbol.staff-space at magstep(-3) — so it asks the unit
        // question from the other side. LilyPond answers it the same way it answered TABS:
        // 9.000000, and OSSD (the small staff BELOW) makes three arrangements at one number.
        // The distance does not know what it is spacing.
        //
        // WHY THE PAIR EXISTS AT ALL. Lily# multiplies the whole inter-group gap by
        // OssiaScaleFactor, which is its own — MultiStaffLayouter says so in its XML doc —
        // and this is the reading that gives that invention a number. The two halves differ
        // in ONE WORD and LilyPond reads them as an IDENTITY (OSSU ≡ OSSUN to the digit), so
        // the Lily# difference IS the invention's size, with no font quantity in it.
        //
        // ⚠️ THE FLOOR DOES NOT BIND ON EITHER SIDE. The control lands on exactly 9.000000
        // rather than on its ink, and the ossia half lands BELOW the basic-distance — which
        // is only possible because something scaled it, and is the shape the port has to undo.
        //
        // ⚠️ READ WITH THE SCALE GIVEN, for the reason the tab pair is read with the line
        // counts given: an ossia is drawn inside a uniform scale group, so its staff lines
        // are 0.070710 thick and StaffLineYs — which keys on an exact 0.1 to keep ledger
        // lines out — does not select them at all. Inferring the scale from the drawing would
        // be a helper that guesses (HANDOFF 5.4); ScaledStaffRefpointGap asserts it instead.
        new("staff.ossia-pair.staff-staff-inside", OssiaPairScoreOssia,
            g => g.ScaledStaffRefpointGap(5, EngravingDefaults.OssiaScale, 5)),
        new("staff.ossia-control.staff-staff-inside", OssiaPairScoreNotation,
            g => g.StaffRefpointGap(5, 5)),

        // THE SAME PAIR ON A PAGE THAT MUST SQUEEZE (books OSSK / OSSKN), which is the only
        // regime in which an ossia's distance being a SPRING and its being RIGID are two
        // different numbers. The pair above reads both at 9.000000 and cannot tell them apart;
        // MultiStaffLayouter.StaffSprings skips an ossia pair outright, and perturbing that
        // skip on 2026-07-28 moved nothing in the corpus for exactly this reason — every ossia
        // book in it is one content-sized page at force 0 (HANDOFF 5.3).
        //
        // LilyPond's side, measured: an ossia is a `\new Staff`, so its VerticalAxisGroup is
        // SPACEABLE (it prints aff=() in the same dump) and its distance is solved like any
        // other staff pair's — OSSK page 1 reads 8.787816 against the ideal 9, and the page's
        // own system springs confirm it is one force rather than two quantities:
        // (9 - 8.787816) / 1 == (12 - 11.151264) / 4 == 0.212184 to six digits.
        //
        // ⚠️ THE STAFF SPRING'S COMPRESS STRENGTH IS 1 HERE AND 2 IN JSK, and the difference is
        // the SPEC rather than the regime. JSK's staves are in a PianoStaff, so
        // Axis_group_interface::calc_maybe_pure_staff_staff_spacing finds a staff-grouper and
        // returns StaffGrouper.staff-staff-spacing (minimum 7); these have no grouper, so the
        // same function falls through to the VerticalAxisGroup's own default-staff-staff-spacing
        // (minimum 8) — axis-group-interface.cc:1007-1027, define-grobs.scm:3352-3355 and
        // :4237-4239. ⇒ TWO Lily# defects live in these four numbers, not one: the pair is
        // rigid, AND MultiStaffLayouter hands an ossia pair sp.StaffStaff — the GROUPED spec —
        // at :130-131 and :222-223, where StaffSpacingParameters.DefaultStaffStaff is the
        // ported one. Both specs declare basic-distance 9, so the pair at rest cannot see it.
        //
        // ⚠️ PAGE 1 ONLY, on both halves. The LAST page of each book prints the PREVIOUS page's
        // force rather than one of its own (OSSKN's page 3 holds ONE system over 144 units of
        // slack and still reads page 2's 8.728721719 to nine digits), so a reading taken there
        // would be pinning an artefact rather than a spring.
        //
        // ⚠️ ONE INSTRUMENT FOR BOTH HALVES: the control is read by ScaledPairGapAt with scale
        // 1.0 rather than by StaffGapAt. The books differ in one word and the readings must
        // too, or the instrument can move the pair's difference by itself.
        new("staff.ossia-pair.compressed.staff-staff-inside", OssiaCompressedScoreOssia,
            g => g.ScaledPairGapAt(0, 5, EngravingDefaults.OssiaScale, 5), EightSystemsPerPage),
        new("system.ossia-pair.compressed-distance", OssiaCompressedScoreOssia,
            g => g.ScaledPairGapAt(1, 5, EngravingDefaults.OssiaScale, 5), EightSystemsPerPage),
        new("page.ossia-pair.compressed.staves-on-first-page", OssiaCompressedScoreOssia,
            g => g.ScaledPairStavesOnPage(5, EngravingDefaults.OssiaScale, 5), EightSystemsPerPage),
        new("staff.ossia-control.compressed.staff-staff-inside", OssiaCompressedScoreNotation,
            g => g.ScaledPairGapAt(0, 5, 1.0, 5), EightSystemsPerPage),
        new("system.ossia-control.compressed-distance", OssiaCompressedScoreNotation,
            g => g.ScaledPairGapAt(1, 5, 1.0, 5), EightSystemsPerPage),
        new("page.ossia-control.compressed.staves-on-first-page", OssiaCompressedScoreNotation,
            g => g.ScaledPairStavesOnPage(5, 1.0, 5), EightSystemsPerPage),

        // BOTH ENDS OF THE SAME CHAIN, carried for the reason JSK's foot reading is (HANDOFF
        // 5.3): every gap on a page is a spring's length at that page's force, a force is the
        // slack over the chain's total strength, and so a fixed term that is wrong at either
        // END shows up in every gap at once, each scaled by its own spring, with nothing in the
        // gaps to attribute it to. ⚠️ THIS PAIR NEEDED THEM ON ARRIVAL rather than later: the
        // control's two gaps are ONE force in Lily# (its staff and system deficits stand at
        // exactly 1 : 4, nine digits) and one force in LilyPond too — different forces, same
        // law — so the control's divergence is already known NOT to be a spring and to be a
        // fixed term. These two readings are what say which end holds it.
        new("page.ossia-pair.compressed.first-staff-refpoint", OssiaCompressedScoreOssia,
            g => g.ScaledPairFirstStaffRefpoint(5, EngravingDefaults.OssiaScale, 5),
            EightSystemsPerPage),
        new("page.ossia-pair.compressed.last-staff-to-foot", OssiaCompressedScoreOssia,
            g => g.ScaledPairLastStaffToFoot(5, EngravingDefaults.OssiaScale, 5),
            EightSystemsPerPage),
        new("page.ossia-control.compressed.first-staff-refpoint", OssiaCompressedScoreNotation,
            g => g.ScaledPairFirstStaffRefpoint(5, 1.0, 5), EightSystemsPerPage),
        new("page.ossia-control.compressed.last-staff-to-foot", OssiaCompressedScoreNotation,
            g => g.ScaledPairLastStaffToFoot(5, 1.0, 5), EightSystemsPerPage),

        // ...and the SAME question one frame out (books TABL / NTL): where a tab staff sits on
        // the PAGE. The pair above reads a distance INSIDE one system, which Align_interface
        // decides; these read the page's own anchors against a staff that is not four staff
        // spaces tall — top-system-spacing to the first refpoint, system-system-spacing between
        // consecutive ones. One staff and many systems, so nothing here is Align_interface's.
        //
        // WHY IT IS A DIFFERENT QUANTITY. A six-string tab staff's lines span 7.500000, so its
        // refpoint — staff position 0, the middle of the span — sits 3.750000 below its top
        // line where an ordinary staff's sits 2.000000 below. Every LilyPond page anchor is
        // written against that refpoint; Lily# converts between it and its own system-origin
        // frame with the NOMINAL half staff (LayoutUtilities.CalculateFirstSystemY subtracts
        // `_options.StaffHeight / 2.0`, which is 2.000000 for every staff there is), so on a
        // tab staff the conversion is 1.750000 short and no entry in this corpus reads a page
        // anchor over a staff of any other height.
        //
        // ⚠️ NEITHER FLOOR BINDS, which is what makes both readings the SPEC's rather than the
        // ink's: a tab system carries 3.800000 of ink above its refpoint (its own top line —
        // measured, and the falsifier of the .ly header is that this is not larger, i.e. that
        // no fret digit and no TAB clef reaches past the outermost string line), and
        // 3.800000 + padding 1 loses to top-system-spacing's 6, while 3.8 + 3.8 + 1 loses to
        // system-system-spacing's 12.
        //
        // ⚠️ THE CONTROL IS CARRIED RATHER THAN ASSUMED, and it is NOT book L: L is the same
        // two quantities but different music on different paper. NTL is this same part on this
        // same paper with the staff spelled as notation, so the pair differs in one word.
        // ⚠️ AND THE COUNTS COME WITH THEM (HANDOFF 5.0, trap 8): both distance readings would
        // return a plausible number off a page that holds a different number of systems.
        new("page.tab-only.first-staff-refpoint", TABL,
            g => g.FirstStaffRefpointOfLineCount(6)),
        new("system.tab-only.natural-distance", TABL,
            g => g.StaffGapOfLineCount(6)),
        new("page.tab-only.staves-on-first-page", TABL,
            g => g.StaffRefpointsOfLineCount(6).Count),
        new("page.tab-control.first-staff-refpoint", NTL, g => g.FirstStaffRefpoint()),
        new("system.tab-control.natural-distance", NTL, g => g.StaffGap()),
        new("page.tab-control.staves-on-first-page", NTL, g => g.StavesOnPage()),

        // THE BOTTOM OF THE CHAIN, in both regimes. Every entry above reads a GAP — a
        // spring's length at the page's force — and a force is the page's slack over the
        // chain's total strength, so a fixed term that is wrong at the foot shows up in all
        // of them at once, each scaled by its own spring. That is not a hypothetical: the
        // four entries above stood at four different residuals for a session and were ONE
        // fault, and naming it took dividing each residual by its own strength and then
        // solving the chain from both regimes at once, because the corpus had no reading of
        // the term itself. These two are that reading.
        //
        // LilyPond puts the SAME 10.023885 here in both regimes, which is the other thing
        // worth pinning: the last spring is ideal 1 with stretchability 30, and
        // ensure_min_distance raises its FLOOR to padding 1 + the last staff's own ink
        // 3.333333 (page-layout-problem.cc:538-545) without touching that strength — so it
        // blocks at f = 0.111111 and neither of these pages ever reaches it (stretched
        // f = 0.099092, compressed f = -0.174101). Rigid on both sides, for a reason that
        // stops being true if a page ever stretches harder.
        new("page.stretched.last-staff-to-foot", JSS, g => g.LastStaffRefpointToFoot(), SixSystemsPerPage),
        new("page.compressed.last-staff-to-foot", JSK, g => g.LastStaffRefpointToFoot(), EightSystemsPerPage),

        // --- where a LOOSE LINE sits (books LYRS/LYRC) ---
        // A lyric row is not spaceable, so it is left out of the page's chain and placed by
        // a second spacer afterwards (distribute_loose_lines, :1025-1054). The pair asks
        // whether that pass moves it: same music, same paper, one page stretched to gaps of
        // ~43.8 and one ragged at 12. LilyPond's answer is 5.500000 on BOTH — the springs to
        // a loose line's non-own side carry LARGE_STRETCH/HUGE_STRETCH by design
        // (:1257-1338), so the row keeps to its own staff while the page opens around it.
        // Two entries rather than one because that regime-independence is the finding; a
        // single entry could not distinguish "does not move" from "was never stretched".
        new("lyrics.natural.staff-to-lyric", LYRC, g => g.FirstStaffToLyricBaseline(), FourSystemsPerPageRagged),
        new("lyrics.stretched.staff-to-lyric", LYRS, g => g.FirstStaffToLyricBaseline(), FourSystemsPerPage),

        // The step to a SECOND verse (book LYRV), which comes from a different LilyPond spec
        // than everything above it: with two loose lines, get_spacing_spec takes its
        // loose-loose branch and returns the UPPER line's nonstaff-nonstaff-spacing
        // (:1315-1332). Its basic-distance is 0, which makes the spring rigid in stretch
        // (set_default_strength, spring.cc:213-216), and its minimum-distance 2.8 stops it
        // compressing — so the step is the one reading on that book that a compressed loose
        // chain cannot distort.
        new("lyrics.verse-step", LYRV, g => g.LyricVerseStep(), FourSystemsPerPageRagged),

        // The SYSTEM gap on that same page, which is the precondition the step above hides.
        // LilyPond's system-system-spacing does NOT widen for loose lines — measured, the
        // gap is 12.000000 with one lyric line and 12.000000 with two — so a second verse
        // does not push the systems apart, it gets squeezed into the room that already
        // exists. Lily# grows the system's extent instead, so its chain always has room and
        // can never compress. Font-free on both sides: 12.000000 is a basic-distance.
        // ⚠️ StaffGapAt(0), not StaffGap(), SINCE 2026-07-28, and the reason is a finding and
        // not a harness workaround: Lily#'s gaps here stopped being uniform (12.167914,
        // 12.143468, 12.167914) when the bar number began reserving its OWN numeral's ink.
        // A round digit overshoots its baseline and a "1" does not, so the reservation is
        // per-numeral — which is LilyPond's shape too (its dump gives ink tops of 4.305433
        // and 4.303666 on the same book). LilyPond's gaps stay uniform only because none of
        // that binds there: its gap sits on the spring's ideal 12.000000, and Lily#'s is
        // floored by the reservation. So the non-uniformity IS the residual this entry
        // records, seen from another side, and index 0 names the wider of the two.
        new("lyrics.two-verse.system-gap", LYRV, g => g.StaffGapAt(0), FourSystemsPerPageRagged),

        // ★ THE ROW'S OWN DISTANCE FROM ITS STAFF, a ledger point since 2026-07-27, when it
        // stopped being a decision. Lily# used to place an independent lyrics row as a
        // staff-like band 9.600000 down (HANDOFF 3, a decided divergence deliberately kept out
        // of this ledger); the band was retired and the row now takes LilyPond's
        // nonstaff-relatedstaff-spacing off its own ink. Book LYRR is book LYRC with the
        // Lyrics context unassociated, and LilyPond reads the two IDENTICALLY — so this entry
        // and lyrics.natural.staff-to-lyric are one LilyPond number reached two ways, and both
        // being exact is that identity reproduced. LyricRowIsSpacedLikeTheLyricsContextItIs
        // asserts the identity itself, which no single entry can.
        new("lyrics.row.staff-to-lyric", LYRR, g => g.FirstStaffToLyricBaseline(), FourSystemsPerPageRagged),

        // ...and the same line in the regime where the chain BINDS, which is book LYRRV —
        // LYRV with `\lyricsto` struck from both Lyrics contexts, whose dump is LINE FOR LINE
        // IDENTICAL to LYRV's (measured 2026-07-27, all 59 lines).
        //
        // ⚠️ WHY THIS BOOK IS NEEDED WHEN LYRR ABOVE IS EXACT. LYRR asks the question where
        // nothing binds: with one loose line the springs on its non-own side carry
        // LARGE/HUGE_STRETCH (page-layout-problem.cc:1257-1338), so the line sits at its ideal
        // whatever the page does and 5.500000 is a basic-distance on both sides. LYRV's regime
        // is the compressed one — the sum of the chain's minimums equals the 12.000000 gap,
        // bisected in the .ly header — and THERE the row model had somewhere to be wrong.
        // HANDOFF 5.2.1 (4): exact can mean "that regime does not move", and LYRR is exactly
        // that.
        //
        // ★ EXACT SINCE 2026-07-28, when the row became an element of the loose chain. ⚠️ THE
        // ENTRY IS NOT THE TEST OF THAT PORT: a literal 2.8 would pass it, which is what its
        // `why` spent a session warning about. What holds the port is
        // LyricRowIsSolvedLikeTheLyricsContextsItIs — Lily# reads LYRRV and LYRV digit for
        // digit on every system, which is LilyPond's own identity between the two spellings
        // reproduced, and is font-free where this entry is not.
        new("lyrics.row.two-verse.verse-step", LYRRV, g => g.LyricVerseStep(), FourSystemsPerPageRagged),

        // ⚠️ THE SYSTEM GAP ON THIS BOOK WAS A LEDGER ENTRY FOR EXACTLY ONE COMMIT, and what
        // it was for is worth keeping even though the number is not. It read 12.000000 —
        // EXACT, matching LilyPond — on a page whose second verse was drawn 0.800000 BELOW the
        // next system's staff refpoint, because the row's ink reached no figure the
        // inter-system spring reads. That is HANDOFF 5.2.1 (4) at its sharpest: the quantity
        // was not right, it was BLIND. It reads 14.098000 now, and the excess is the row not
        // being in the chain: LilyPond squeezes both verses into the 12.000000 it already has,
        // Lily# places them at their ideals and pushes the systems apart to fit. That is the
        // same shape lyrics.two-verse.system-gap carried at +4.060000 before ITS chain landed,
        // and it closes the same way — so it is not carried here, where it would read as a
        // quantity of its own rather than as the one thing lyrics.row.two-verse.verse-step
        // already says. What IS asserted is the invariant that must hold either way, in
        // LyricRowReservesItsInkAgainstTheNextSystem.

        // ...and the regime guard the verse step needs (HANDOFF 5.0, trap 8): it is read by
        // index off page 1, and an index means the system it is supposed to mean only while
        // that page holds the four systems LilyPond puts there.
        new("lyrics.row.two-verse.staves-on-first-page", LYRRV, g => g.StavesOnPage(0), FourSystemsPerPageRagged),

        // The guard the two above cannot give, and it is not decorative: with 40 bars instead
        // of 120 LilyPond re-broke the music onto one page, ragged-last-bottom left it
        // unstretched, and the "stretched" entry would have read a ragged page and agreed.
        new("lyrics.stretched.systems-on-first-page", LYRS, g => g.SystemsOnPage(0), FourSystemsPerPage),

        // The same line under a TWO-staff system (book LYRM), which is the identity twin of
        // lyrics.natural.staff-to-lyric above: LilyPond spaces a Lyrics line from the staff
        // it has affinity to — the system's LAST — so a staff added ABOVE that one cannot
        // move it, and both entries carry the same 5.5. Lily# anchors the block below the
        // system ORIGIN instead, so the two staves' worth of distance between the two anchors
        // is exactly what this reads. ⚠️ Index 1, the first system's BOTTOM staff.
        new("lyrics.two-staff.staff-to-lyric", LYRM,
            g => g.LyricBaselineBelowStaff(1), FourSystemsPerPageRagged),

        // The SAME BOOK WITH A SECOND VERSE (LYRMV), which is the one question LYRM cannot
        // ask: a single loose line has nothing to be squeezed against, so it reads the same
        // whether the chain is solved or left at force 0. LYRC/LYRV differ by exactly that.
        // LilyPond's side is an identity with LYRV in the SOURCE — distribute_loose_lines
        // takes the previous spaceable staff's page position and this one's (:936-939), so
        // a staff added above joins the PAGE's chain and nothing else — and it measures out
        // that way: 12.000000, {3.737890, 2.800000, 5.500001}, 9.000000, LYRV's and LYRM's
        // numbers throughout.
        //
        // The gap entry is the measurement; the other two are what keep it honest. The
        // inside-system distance is the falsifier for "the block's ink is in the two staves'
        // own spring" — if that were so, the chain would not be what places it at all — and
        // the count is HANDOFF 5.0 trap 8, since a gap named by INDEX means the gap it is
        // supposed to mean only while the page holds the staves this probe assumes.
        new("lyrics.two-staff.two-verse.system-gap", LYRMV,
            g => g.StaffGapAt(1), FourSystemsPerPageRagged),
        new("lyrics.two-staff.two-verse.staff-staff-inside", LYRMV,
            g => g.StaffGapAt(0), FourSystemsPerPageRagged),
        new("lyrics.two-staff.two-verse.staves-on-first-page", LYRMV,
            g => g.StavesOnPage(0), FourSystemsPerPageRagged),
        new("lyrics.two-staff.two-verse.staff-to-lyric", LYRMV,
            g => g.LyricBaselineBelowStaff(1), FourSystemsPerPageRagged),
        new("lyrics.two-staff.staff-staff-inside", LYRM,
            g => g.StaffGapAt(0), FourSystemsPerPageRagged),

        // THE SAME TWO STAVES WITH THE MELODY ON TOP (books LYRB/LYRBV), which puts the lyric
        // block BETWEEN them — the last branch of the loose chain still laid out at force 0.
        // The room is the same refpoint-to-refpoint span every other block is solved into
        // (page-layout-problem.cc:936-939); what changes is the minimum that CLOSES it, which
        // is `min_offsets[k-1] - min_offsets[k]` with no null line (:923-925) and a spring
        // taking the line's own nonstaff-unrelatedstaff-spacing plus LARGE_STRETCH
        // (:1299-1312) instead of the system boundary's HUGE_STRETCH null.
        //
        // ONE VERSE AND TWO, because the pair is what separates a solved chain from a force-0
        // one: with one verse the staff spring's ideal 9.000000 is above the block's floor and
        // the chain is compressed to 4.027851, with two the floor rises past it and the whole
        // chain sits on its minimums (3.737890 + 2.800000) in a room of 11.073064. A port that
        // never solves reads its ideal 5.500000 on BOTH and is wrong by different amounts, so
        // neither book alone could say whether the mechanism or a constant was missing.
        //
        // The inside-system distance is carried for each because it is the ROOM: the block is
        // inside the staff spring's floor (:699-704), so a port that grows the reservation
        // without solving the chain moves this and nothing else would notice. The counts are
        // HANDOFF 5.0 trap 8 — every reading here is by index.
        new("lyrics.between-staves.staff-to-lyric", LYRB,
            g => g.LyricBaselineBelowStaff(0), FourSystemsPerPageRagged),
        new("lyrics.between-staves.staff-staff-inside", LYRB,
            g => g.StaffGapAt(0), FourSystemsPerPageRagged),
        new("lyrics.between-staves.staves-on-first-page", LYRB,
            g => g.StavesOnPage(0), FourSystemsPerPageRagged),

        // THE SAME BOOK WITH A CHORD ROW ADDED, which is the branch that still runs at force
        // 0 — BuildLooseChainEnds and ComputeBetweenStavesEnd both decline a system carrying
        // a text ROW, so the block gets room = +infinity and sits at its ideal 5.500000.
        // ★ LilyPond is the IDENTITY on all three: the row changes nothing it measures, so
        // each residual is Lily#'s defect outright. See LyricChordRowPageScore.
        new("lyrics.chord-row.staff-to-lyric", LYRCH,
            g => g.LyricBaselineBelowStaff(0), FourSystemsPerPageRagged),
        new("lyrics.chord-row.staff-staff-inside", LYRCH,
            g => g.StaffGapAt(0), FourSystemsPerPageRagged),
        new("lyrics.chord-row.staves-on-first-page", LYRCH,
            g => g.StavesOnPage(0), FourSystemsPerPageRagged),

        // THE ROW INSIDE THE ROOM, which is the case narrowing a guard cannot reach: the
        // lyrics hang under the system's LAST staff, so their room runs to the next system
        // and that system's chord row is in it. LilyPond squeezes both into one 12.000000.
        // See LyricTwoStaffChordRowScore for what the pair decides.
        // ⚠️ INDEX 1, the first system's BOTTOM staff — the one the block hangs from, as in
        // lyrics.two-staff.staff-to-lyric. Index 0 reads the upper staff and comes out one
        // whole staff-staff distance too big (14.500000 against 5.500000, measured).
        new("lyrics.chord-row.between-systems.staff-to-lyric", LYRMC,
            g => g.LyricBaselineBelowStaff(1), FourSystemsPerPageRagged),
        // ⚠️ StaffGapAt(1), not StaffGap(): a two-staff book's gaps alternate 9 inside a
        // system and 12 between two, so the uniform reading throws. Index 1 is the
        // between-systems one, the same index lyrics.two-staff.two-verse.system-gap takes.
        new("lyrics.chord-row.between-systems.system-gap", LYRMC,
            g => g.StaffGapAt(1), FourSystemsPerPageRagged),
        // ...and the count, because both readings above are BY INDEX (HANDOFF 5.0 trap 8).
        new("lyrics.chord-row.between-systems.staves-on-first-page", LYRMC,
            g => g.StavesOnPage(0), FourSystemsPerPageRagged),

        // THE OSSIA HALF, chain reading only — the inside distance is not like-for-like
        // (LilyPond has three spaceable staves there and Lily# has two). See
        // LyricOssiaPageScore.
        new("lyrics.ossia.staff-to-lyric", LYROS,
            g => g.LyricBaselineBelowStaff(0), FourSystemsPerPageRagged),
        new("lyrics.between-staves.two-verse.staff-to-lyric", LYRBV,
            g => g.LyricBaselineBelowStaff(0), FourSystemsPerPageRagged),
        new("lyrics.between-staves.two-verse.verse-step", LYRBV,
            g => g.LyricVerseStep(), FourSystemsPerPageRagged),
        new("lyrics.between-staves.two-verse.staff-staff-inside", LYRBV,
            g => g.StaffGapAt(0), FourSystemsPerPageRagged),
        new("lyrics.between-staves.two-verse.staves-on-first-page", LYRBV,
            g => g.StavesOnPage(0), FourSystemsPerPageRagged),

        // THE SAME BOOK WITH ONE SYSTEM'S UPPER STAFF REMOVED (LYRHK) — the only book here
        // whose staff count varies down the page, and therefore the only one that can tell a
        // per-system origin-to-last-staff span from a score-wide one. Both readings below are
        // the SAME LilyPond number, and that is the finding: the room distribute_loose_lines
        // solves into comes out of the page's spring chain (:936-939), which no staff count
        // reaches. Two entries rather than one because a single entry could not distinguish
        // "read from the right system" from "every system read alike".
        //
        // ⚠️ Staff index 0 is system 0's ONLY staff and index 2 is system 1's BOTTOM staff —
        // page 1 runs 1 + 2 + 2 + 2. The count entry is what makes those indices mean what
        // they say (HANDOFF 5.0 trap 8), and the inside-system distance is the second half of
        // the same guard: it is 9.000000 only while index 1 and index 2 are two staves of ONE
        // system, so a removal that failed to happen — or happened to the wrong staff — turns
        // it into a system gap and the entry goes red instead of the measurement going quiet.
        new("lyrics.hara-kiri.hidden-system.staff-to-lyric", LYRHK,
            g => g.LyricBaselineBelowStaff(0), FourSystemsPerPageRagged),
        new("lyrics.hara-kiri.shown-system.staff-to-lyric", LYRHK,
            g => g.LyricBaselineBelowStaff(2), FourSystemsPerPageRagged),
        new("lyrics.hara-kiri.staff-staff-inside", LYRHK,
            g => g.StaffGapAt(1), FourSystemsPerPageRagged),
        new("lyrics.hara-kiri.staves-on-first-page", LYRHK,
            g => g.StavesOnPage(0), FourSystemsPerPageRagged),

        // HARA-KIRI INSIDE A GROUPER (LYRHKG). Both 9 and 10.5 must appear, and which
        // appears where turns on which staves are LIVE: killing the grand staff's lower
        // member promotes the upper one to last live member of the grouper, so the gap that
        // survives changes spec. This is what stops LYRHK's +1.500000 being closed by
        // writing 9 where LayoutEngine writes 10.5 — that passes LYRHK, whose staves are
        // both bare, and fails the first entry here.
        // ⚠️ Page 1 is 2 + 3 + 3 + 3 staves, so index 0 is system 0's TOP staff (its grand
        // staff's only survivor), index 1 its melody staff, and indices 2..4 are system 1's
        // three. The count entry is what keeps those indices honest (HANDOFF 5.0 trap 8).
        new("lyrics.hara-kiri.grouper.promoted-gap", LYRHKG,
            g => g.StaffGapAt(0), FourSystemsPerPageRagged),
        new("lyrics.hara-kiri.grouper.inside-grouper", LYRHKG,
            g => g.StaffGapAt(2), FourSystemsPerPageRagged),
        new("lyrics.hara-kiri.grouper.staff-to-lyric", LYRHKG,
            g => g.LyricBaselineBelowStaff(1), FourSystemsPerPageRagged),
        new("lyrics.hara-kiri.grouper.staves-on-first-page", LYRHKG,
            g => g.StavesOnPage(0), FourSystemsPerPageRagged),

        // THE DECLARATION ON ITS OWN (LYRHKD/LYRHKN) — the same music differing only in
        // whether the upper staff declares removeEmpty, which never fires because no staff
        // is ever empty. LILYPOND'S TWO READINGS ARE IDENTICAL BY CONSTRUCTION: its
        // hara-kiri is a suicide followed by a live-filter (page-layout-problem.cc:1366-1370,
        // align-interface.cc:90), so a grob that never dies leaves no trace. The two entries
        // of each pair therefore carry the SAME LilyPond number, and the finding is whether
        // Lily#'s two differ — it branches on the declaration, not on anything having been
        // hidden.
        //
        // ⚠️ COMPRESSED PAPER ON PURPOSE. The branch LYRHK cannot see is the page's staff
        // springs, which under hara-kiri are emptied and rebuilt per system WITHOUT
        // SKYLINES; a spring's minimum comes from those skylines and only binds when the
        // page is squeezed. Eight systems to a justified page is book JSK's regime.
        // HANDOFF 5.0 trap 7: the inside distance must read BELOW the ideal 9.000000, or the
        // page did not compress and these entries measure nothing.
        // ⚠️ ONLY THE COUNTS ARE CARRIED, and the reason is a measurement: LilyPond fits 8
        // systems on this page and COMPRESSES to do it (inside 8.429724, below the ideal 9),
        // while Lily# fits 6 and STRETCHES (9.647977). A gap on a 6-system stretched page and
        // the same-named gap on an 8-system compressed one are not the same quantity, so the
        // distance entries would have measured the page count. They wait for a paper both
        // engines fill alike. The counts ARE comparable, and they are what caught it.
        new("lyrics.hara-kiri.declared-only.staves-on-first-page", LYRHKD,
            g => g.StavesOnPage(0), EightSystemsPerPageJustified),
        new("lyrics.hara-kiri.undeclared.staves-on-first-page", LYRHKN,
            g => g.StavesOnPage(0), EightSystemsPerPageJustified),

        // HARA-KIRI WITH THE INK BETWEEN THE STAVES BEATING THE SPEC (HKW/HKWN) — the regime
        // 41f9749d moved test/hara-kiri's snapshot in (9.000000 -> 9.500000 on one system)
        // and could not name an entry for, because every hara-kiri book above sits on
        // basic-distance and reads 9.000000 whether or not a skyline was ever consulted.
        // Book P's arithmetic under a removeEmpty declaration: 6.545 + 2.05 + 1 = 9.595.
        //
        // The two tall-ink entries carry the SAME LilyPond number, and that is the finding
        // rather than the value — a suicide plus a live-filter leaves the surviving system's
        // Align_interface running its ordinary max(), so no other system's staff count can
        // reach it. The spec-bound entry is the regime assertion the other two need (HANDOFF
        // 5.0 trap 7): it comes out of the SAME book and solve as its 9.595000 neighbours, so
        // a reading that has stopped seeing the ink cannot stay green by sitting on the floor
        // — it would have to report 9.000000 in both places.
        //
        // ⚠️ Page 1 is 1 + 2 + 2 + 2 staves in HKW and 2 + 2 + 2 + 2 in HKWN, so gap 0 in HKW
        // is a SYSTEM gap (the lone survivor to the next system's first staff) while gap 0 in
        // HKWN is system 0's inside distance. The count entries are what make those indices
        // mean what they say (HANDOFF 5.0 trap 8), and the lone-staff gap is the reading that
        // would go red if Lily# left a removed staff's room behind.
        new("hara-kiri.wide-ink.staff-staff-inside", HKW,
            g => g.StaffGapAt(1), FourSystemsPerPageRagged),
        new("hara-kiri.wide-ink.lone-staff-to-next-system", HKW,
            g => g.StaffGapAt(0), FourSystemsPerPageRagged),
        new("hara-kiri.wide-ink.staves-on-first-page", HKW,
            g => g.StavesOnPage(0), FourSystemsPerPageRagged),
        new("hara-kiri.undeclared.wide-ink.staff-staff-inside", HKWN,
            g => g.StaffGapAt(2), FourSystemsPerPageRagged),
        new("hara-kiri.undeclared.spec-bound.staff-staff-inside", HKWN,
            g => g.StaffGapAt(0), FourSystemsPerPageRagged),
        new("hara-kiri.undeclared.staves-on-first-page", HKWN,
            g => g.StavesOnPage(0), FourSystemsPerPageRagged),

        // THE DECLARATION ON ITS OWN, COMPRESSED AND WITHOUT LYRICS (HKCD/HKCN) — JSK's book
        // plus removeEmpty on the upper part, nothing else changed, and no staff ever empty.
        // This is the net for b415dd16, the one stage of the island with no ledger key: before
        // it, a declared score rebuilt its staff springs WITHOUT skylines, so the floor fell
        // back to the drawn distance and the page could not squeeze (9.000000 declared against
        // 8.651797 undeclared, on this music). LYRHKD/LYRHKN were built to be that key and
        // could not: their lyrics make the two engines fill the page differently, so only the
        // counts were comparable. Without lyrics the shape is JSK's, where both already agree.
        //
        // ⚠️ THE INK IS DELIBERATELY LOW, unlike HKW above. Compression drives a spring onto
        // its minimum, and the minimum is the alignment distance -- with tall ink the floor
        // would be 9.595000 and the page would sit on it and measure nothing. JSK's music
        // leaves it at 7.545, below the 8.651797 solved for, so the spring is between its
        // floor and its ideal. The regime assertion is that 8.651797 < the ideal 9.000000
        // (HANDOFF 5.0 trap 7); the system gap is the second half of the same force, so the
        // two distances of the declared book cross-check each other:
        // (9 - inside) / 2 == (12 - system) / 4.
        new("hara-kiri.compressed.declared.staff-staff-inside", HKCD,
            g => g.StaffGapAt(0), EightSystemsPerPageJustified),
        new("hara-kiri.compressed.declared.system-gap", HKCD,
            g => g.StaffGapAt(1), EightSystemsPerPageJustified),
        new("hara-kiri.compressed.declared.staves-on-first-page", HKCD,
            g => g.StavesOnPage(0), EightSystemsPerPageJustified),
        new("hara-kiri.compressed.undeclared.staff-staff-inside", HKCN,
            g => g.StaffGapAt(0), EightSystemsPerPageJustified),
        new("hara-kiri.compressed.undeclared.staves-on-first-page", HKCN,
            g => g.StavesOnPage(0), EightSystemsPerPageJustified),

        // --- where a BAR NUMBER sits (books BNL/BNH) ---
        // The ink a system reserves ABOVE its own reference point, which is what closes the
        // loose-line chain of the system before it and floors the system-to-system spring.
        // Two entries with the SAME LilyPond value because the finding is the invariance:
        // LilyPond places the number against an X-aware skyline, at the line start where only
        // the clef stands, so a melody two octaves higher does not move it by a bit. A single
        // entry could not tell "placed correctly" from "placed above whatever is tallest".
        new("barnumber.low-melody.staff-to-baseline", BNL,
            g => g.FirstBarNumberBaselineAboveStaff(), RaggedBottomPaper),
        new("barnumber.high-melody.staff-to-baseline", BNH,
            g => g.FirstBarNumberBaselineAboveStaff(), RaggedBottomPaper),

        // --- where a TEXT SCRIPT sits, per string (books TXD/TXP/TXS/TXL) ---
        // The first ledger points that reach OutsideStaffStacker's letter-class constants
        // (TextAscentEm/TextDescentEm). Four entries, three claims: the single-text pair
        // (TXD/TXP) reads whether the baseline rides the string's own DESCENDER — LilyPond's
        // two readings differ by exactly the p's ink, a flat-fraction stacker reads them
        // identical. The stacked pair reads the grob-vs-grob step: TXL in the regime where
        // box arithmetic equals LilyPond's skyline (flat lower profile), TXS in the regime
        // where it cannot (outline pointwise) — so TXL is what the ink port must close and
        // TXS is the named remainder that stays open until the stacker itself holds
        // outlines. See TextScriptScore for the measured mechanism and the LP refs.
        new("textscript.no-descender.staff-to-baseline", TXD,
            g => g.SoleCustomTextBaselineAboveStaff(), RaggedBottomPaper),
        new("textscript.descender.staff-to-baseline", TXP,
            g => g.SoleCustomTextBaselineAboveStaff(), RaggedBottomPaper),
        // The X half of the same pair: the script's PEN ORIGIN sits ON the anchor note
        // column's origin (X-offset 0 — self/parent-alignment-X both #f). Two strings,
        // one claim: the pen cannot ride the first glyph's side bearing (d vs p), which
        // is what pins "pen origin", not "ink left". Notehead #4 is m2's first note.
        new("textscript.x.pen-to-notehead-left", TXD,
            g => g.SoleCustomTextPenToNotehead(4), RaggedBottomPaper),
        new("textscript.x.pen-to-notehead-left.descender", TXP,
            g => g.SoleCustomTextPenToNotehead(4), RaggedBottomPaper),
        new("textscript.stacked.box-step", TXL,
            g => g.CustomTextBaselineStep(), RaggedBottomPaper),
        new("textscript.stacked.outline-step", TXS,
            g => g.CustomTextBaselineStep(), RaggedBottomPaper),

        // --- the FULLY BEAMED tuplet's number as staff-staff binding ink (TNB/TNC) ---
        // The first points that reach SkylineBuilder's !ShowBracket skip. See
        // BeamedTupletScore for the measured decomposition; the control doubles as a
        // clef-down-vs-staff-line silhouette reading.
        new("staff.staff.beamed-tuplet-number", TNB, g => g.StaffGap(), ZeroStaffStaffPaper),
        new("staff.staff.beamed-tuplet-control", TNC, g => g.StaffGap(), ZeroStaffStaffPaper),

        // --- the DRAWN bracket's encompass points (TPB/TPC/TPS) ---
        // HANDOFF ▶ⓐ's observation surface: with the bracket shown, LilyPond's encompass
        // is the REAL column extent (quanted beam face / shortened stem) while Lily#'s
        // CalculateSlope fallback reads the raw DefaultStemLength 3.5. See
        // BeamedTupletScore's TPB remark for the measured decomposition; TPB and TPS
        // reading identically in Lily# is the structural half of the defect.
        new("staff.staff.tuplet-bracket-partial-beam", TPB, g => g.StaffGap(), ZeroStaffStaffPaper),
        new("staff.staff.tuplet-bracket-partial-beam-control", TPC, g => g.StaffGap(), ZeroStaffStaffPaper),
        new("staff.staff.tuplet-bracket-shortened-stem", TPS, g => g.StaffGap(), ZeroStaffStaffPaper),

        // --- the DYNAMIC's support (DSQ/DSW/DSB + mechanism pair DMF/DMW) ---
        // The points that gate the last raw-3.5 read (NoteColumnLayout.RawSupportEdgeUp).
        // MEASURED (mechanism corrected session 36): the support is POINTWISE — heads
        // AND real stems, distance against the dynamic's own outline — so the head wins
        // under \f (DSQ) and the stem binds under the wide \fff (DMF); beams reach the
        // dynamic only through the outside-staff pass (0.46, pointwise, DSB). See
        // DynamicSupportScore's remark for the decomposition.
        new("staff.staff.dynamic-head-support", DSQ, g => g.StaffGap(), ZeroStaffStaffPaper),
        new("staff.staff.dynamic-head-support-control", DSW, g => g.StaffGap(), ZeroStaffStaffPaper),
        new("staff.staff.dynamic-beam-avoid", DSB, g => g.StaffGap(), ZeroStaffStaffPaper),
        new("staff.staff.dynamic-stem-binding", DMF, g => g.StaffGap(), ZeroStaffStaffPaper),
        new("staff.staff.dynamic-stem-binding-control", DMW, g => g.StaffGap(), ZeroStaffStaffPaper),

        // --- where the OTTAVA BRACKET's LINE sits (books OTF/OTC) ---
        // The first points that reach OttavaBracket's staff-padding floor (2.0) — the
        // largest of the four floors named unported by the TextScript port. See
        // OttavaScore for the measured decomposition; the control doubles as a
        // support-arithmetic reading (padding 0.5 + the label's half-ink to the EDGE).
        new("ottava.floor.staff-to-line", OTF, g => g.OttavaLineAboveStaff(), RaggedBottomPaper),
        new("ottava.support.staff-to-line", OTC, g => g.OttavaLineAboveStaff(), RaggedBottomPaper),
        // Round 3 (book OTC again) — the LABEL, which every entry above reads THROUGH
        // without ever reading it. Session 73 named OTC's +0.027480 by dumping the two
        // profiles instead of subtracting totals, and the mover's half of it turned out to
        // be the label's own outline; these two open what that outline is made of, because
        // the port has to move the DRAWING with the reservation or the island splits into
        // "what is measured" and "what is drawn" (HANDOFF 5.0, the state the grace island
        // exists to prevent).
        //   * ink-height: LilyPond's label rides the undeclared text font size — 2.2, the
        //     em the TextScript pair measured — and its grob extent is the centred ink, so
        //     LilyPond reads 2 × 0.7920313638041338 = 1.5840627276082676. PREDICTION for
        //     Lily# (before its run, sign certain NEGATIVE): DrawOttavaBrackets draws at
        //     FontSize × 0.45 = 1.8, giving 1.296000112 — residual −0.288062616. This is
        //     the em mislabel's FOURTH instance (lyrics 3.2, chords 2.6, TextScript 2.4).
        //     ⚠️ After the port it lands at 1.584000136, NOT at zero: −0.0000626 is the
        //     face/flattening noise the other ink readings carry (textscript box-step sits
        //     at −4.8e-5). Anything closer to zero than that is a fit, not a port.
        //   * line-to-ink-centre: 0 BY CONSTRUCTION for LilyPond (ottava-bracket.cc centres
        //     the text on the line). PREDICTION for Lily# (sign certain POSITIVE): the draw
        //     puts the BASELINE on the line, so the centre sits (Top+Bottom)/2 above it =
        //     +0.621000054 at the current 1.8, and would be +0.759000066 at 2.2 — i.e. the
        //     two entries are NOT independent and the size must land first. FALSIFIER: a
        //     Lily# reading near 0 means the draw already centres and this whole reading of
        //     DrawOttavaBrackets is wrong — find that before touching the draw.
        new("ottava.label.ink-height", OTC, g => g.OttavaLabelInkHeight(), RaggedBottomPaper),
        new("ottava.label.line-to-ink-centre", OTC,
            g => g.OttavaLabelInkCentreToLine(), RaggedBottomPaper),
        // Round 4 — the STRING control. The SAME Lily# book, against a LilyPond book that
        // was asked the question with LILY#'S label spelling
        // (`ottavationMarkups = #ottavation-simple-ordinals`, book OTS). LilyPond 2.26's
        // default is `ottavation-numbers`, i.e. the single digit "8", so every ottava entry
        // above compares a 1.212-wide label against Lily#'s 3.845-wide one — and the
        // arithmetic is POINTWISE, so which glyph hangs over the first notehead decides.
        // This pair is what tells an arithmetic residual from a spelling one.
        new("ottava.string-control.staff-to-line", OTC,
            g => g.OttavaLineAboveStaff(), RaggedBottomPaper),
        // Round 2 (book OTL) — the ottava's face of the guard three above-staff movers
        // carry. LilyPond repeats OTC's number on the lower staff to fifteen digits (its
        // pass is per-VerticalAxisGroup); Lily#'s guard skips the pass, which for THIS grob
        // is the whole support side, so the bracket falls back to the bare floor.
        new("ottava.lower-staff.staff-to-line", OTL,
            g => g.OttavaLineAboveStaff(staff: 1), RaggedBottomPaper),

        // --- where the TRILL / TEXT SPANNER LINE sits (books TRF/TRC/TSF/TSC) ---
        // The last two of the four floors the TextScript port left named unported. See
        // SpannerFloorScore for the measured decomposition: the trill's quiet height is
        // include_staff + its own padding 0.5 (NOT the naked floor); the text spanner's
        // IS the naked floor. The controls double as support-arithmetic readings.
        new("trill.quiet.staff-to-line", TRF, g => g.TrillLineAboveStaff(), RaggedBottomPaper),
        new("trill.support.staff-to-line", TRC, g => g.TrillLineAboveStaff(), RaggedBottomPaper),
        new("textspanner.floor.staff-to-line", TSF,
            g => g.TextSpannerLineAboveStaff(), RaggedBottomPaper),
        new("textspanner.support.staff-to-line", TSC,
            g => g.TextSpannerLineAboveStaff(), RaggedBottomPaper),

        // --- the TRILL LINE over a REAL stemmed column (books TLS/TLB/TLW) ---
        // The points that gated the LAST raw-3.5 read out (now
        // NoteColumnLayout.SupportEdgeUp -> DynamicEngraver.ColumnUpEdge -> the trill
        // engraver's aligned_side; ported same-day, TLS/TLB nine-digit exact). See
        // TrillStemSupportScore for the measured decomposition: the trill's facing
        // profile is FLAT, so a scalar support edge CAN serve it — its value is the
        // DRAWN tip (shortened / beam-quanted), which the pre-port raw 3.5 was blind
        // to (TLS ≡ TLB nine-digit was the blindness falsifier, and it fired).
        new("trill.shortened-stem.staff-to-line", TLS,
            g => g.TrillLineAboveStaff(), RaggedBottomPaper),
        new("trill.beam-face.staff-to-line", TLB,
            g => g.TrillLineAboveStaff(), RaggedBottomPaper),
        new("trill.stemless-control.staff-to-line", TLW,
            g => g.TrillLineAboveStaff(), RaggedBottomPaper),
        // Round 2 — the audit's three unmeasured regimes. TXG/TXW measured that
        // aligned_side is POINTWISE for the trill too (my_dim = glyph plateau −1.0 /
        // wave ink ±0.17, X-overlap gated, LEDGER ink in the supports): the scalar max
        // matches LilyPond only when the binding support sits under the glyph. TSB:
        // the sloped support is the high member's stem END (its own X), not the beam
        // envelope corner. TSP: the fermata (priority 75) clears the TRILL, which
        // stays at quiet — Lily# seeds scripts immovable, the inverted order.
        new("trill.x.glyph-zone.staff-to-line", TXG,
            g => g.TrillLineAboveStaff(), RaggedBottomPaper),
        new("trill.x.wave-zone.staff-to-line", TXW,
            g => g.TrillLineAboveStaff(), RaggedBottomPaper),
        new("trill.beam-face-sloped.staff-to-line", TSB,
            g => g.TrillLineAboveStaff(), RaggedBottomPaper),
        new("trill.fermata-priority.staff-to-line", TSP,
            g => g.TrillLineAboveStaff(), RaggedBottomPaper),
        // Round 3 (books TXV/TXL) — the trill's face of the same guard, as an IDENTITY pair:
        // the obstacle is a tall column in the OTHER VOICE, which the per-voice side-support
        // cannot see and the collision pass can, and it lands under the "tr" glyph's flat
        // plateau so the binding survives a texture edit (TXW's does not — 0.277 of slack in
        // X, and the two-staff spacing alone was enough to leave its regime).
        new("trill.other-voice.staff-to-line", TXV,
            g => g.TrillLineAboveStaff(), RaggedBottomPaper),
        new("trill.lower-staff.staff-to-line", TVL,
            g => g.TrillLineAboveStaff(staff: 1), RaggedBottomPaper),

        // --- where a FERMATA's own ink lands (books SPQ/SPH/SPS/SPD) ---
        // The observers TSP does not have: it watches the trill, and the port it gates moves
        // the FERMATA — every fermata in the corpus, because the collision pass pays 0.46
        // against the staff's own ink. See FermataScore for the measured decompositions; SPS
        // is the one that says the mover's profile must be the glyph's real outline (a flat
        // box cannot straddle a thin stem inside the arch).
        new("script.quiet.staff-to-ink-bottom", SPQ,
            g => g.FermataInkEdgeAboveStaff(), RaggedBottomPaper),
        new("script.high-head.staff-to-ink-bottom", SPH,
            g => g.FermataInkEdgeAboveStaff(), RaggedBottomPaper),
        new("script.stem-support.staff-to-ink-bottom", SPS,
            g => g.FermataInkEdgeAboveStaff(), RaggedBottomPaper),
        new("script.below.staff-to-ink-top", SPD,
            g => g.FermataInkEdgeAboveStaff(above: false), RaggedBottomPaper),
        new("script.accidental.staff-to-ink-bottom", SPA,
            g => g.FermataInkEdgeAboveStaff(), RaggedBottomPaper),
        // The guard's own price: the same quiet regime on the LOWER staff of two. LilyPond
        // reads SPQ's number because its pass is per-staff; Lily#'s above tracker is per
        // SYSTEM, so the fermata is held out of the pass entirely (PlaceArticulations'
        // StaffIndex != 0, the same line PlaceTrills and PlaceOttavas carry). This entry
        // closes when the above pass gets per-(system, staff) trackers.
        new("script.lower-staff.staff-to-ink-bottom", SPL,
            g => g.FermataInkEdgeAboveStaff(staff: 1), RaggedBottomPaper),

        // --- where FIGURED BASS lands, and WHICH STAFF decides (books FBLA/FBLB/FBLC) ---
        // The corpus's first figured-bass points. The same music and the same figures in
        // three arrangements; LilyPond reads ONE number for all three, so the LP side is an
        // IDENTITY and whatever spread Lily# shows IS the defect (HANDOFF §5.0). The middle
        // one is the arrangement no committed fixture has — figures under the UPPER staff of
        // two — and it is the one Lily#'s single per-system drop throws below the whole
        // system, the same shape as the session-40 lower-staff fermata.
        new("figbass.alone.staff-to-baseline", FBA,
            g => g.FigureBaselineBelowStaff(staffIndex: 0, staffCount: 1), RaggedBottomPaper),
        // The QUIET half of the placement pair: the staff decides, not the column. Without
        // it a port would have to guess how side-position's staff-padding is spelled.
        new("figbass.quiet.staff-to-baseline", FBQ,
            g => g.FigureBaselineBelowStaff(staffIndex: 0, staffCount: 1), RaggedBottomPaper),
        new("figbass.upper-staff.staff-to-baseline", FBB,
            g => g.FigureBaselineBelowStaff(staffIndex: 0, staffCount: 2), RaggedBottomPaper),
        new("figbass.lower-staff.staff-to-baseline", FBC,
            g => g.FigureBaselineBelowStaff(staffIndex: 1, staffCount: 2), RaggedBottomPaper),
        // The OTHER half of the two-staff claim, and a different quantity: how much room the
        // system leaves BETWEEN the staves. LilyPond's own pair is not an identity here — the
        // figure row lives between the staves in FBB and below both in FBC, and the two gaps
        // differ by exactly the row's height. So these two say whether the row is RESERVED,
        // where the baseline points say where it is PLACED; a system that puts figures in the
        // right place while reserving nothing prints them through the staff below.
        new("figbass.upper-staff.staff-gap", FBB, g => g.StaffGap(), RaggedBottomPaper),
        new("figbass.lower-staff.staff-gap", FBC, g => g.StaffGap(), RaggedBottomPaper),

        // --- what a figure row costs THE PAGE (books FBPQ / FBPD / FBPN) ---
        // The island's third spelling of the row depth is LayoutEngine.EstimateLooseLineExtents'
        // `2.0 + n * 1.5`, a LilyPond-less formula the session-45 port could not fold in because
        // nothing watched it: its effect is a page height, and every figured-bass point so far
        // measures a placement or a staff gap. These read the foot of the page's spring chain
        // (last-bottom-spacing, page-layout-problem.cc:538-545), the one term in which the ink
        // hanging BELOW the last staff stands alone — the same quantity page.{stretched,
        // compressed}.last-staff-to-foot read on JSS/JSK, here on a music that hangs a figure
        // row there.
        //
        // THE PAIR IS A FORK: the invention is a constant in the texture (5.000000 for two
        // rows), so the QUIET book's real ink loses to it and the DEEP book's beats it. One
        // prices the over-reservation; the other says whether the real row is in the system's
        // silhouette at all, which decides whether the port is a deletion or a merge.
        // The CONTROL is the quiet music with the figures taken out — LilyPond's difference
        // between the two IS the row's contribution, and on this side it is the net: no
        // figures means none of the five figured-bass guards is even entered.
        new("figbass.page.quiet.last-staff-to-foot", FBPQ,
            g => g.LastStaffRefpointToFoot(), TwelveSystemsPerPage),
        new("figbass.page.deep.last-staff-to-foot", FBPD,
            g => g.LastStaffRefpointToFoot(), TwelveSystemsPerPage),
        new("figbass.page.control.last-staff-to-foot", FBPN,
            g => g.LastStaffRefpointToFoot(), TwelveSystemsPerPage),
        // Every reading above is taken from THE LAST STAFF OF PAGE 1, which means that staff
        // only while the page holds the systems the probe assumes (HANDOFF §5.0 trap 8) — and
        // the count carries the regime too: a page that is not full opens the foot spring, and
        // a page 1 that is also the LAST page is not justified at all.
        new("figbass.page.quiet.systems-on-first-page", FBPQ,
            g => g.SystemsOnPage(0), TwelveSystemsPerPage),
        new("figbass.page.deep.systems-on-first-page", FBPD,
            g => g.SystemsOnPage(0), TwelveSystemsPerPage),
        new("figbass.page.control.systems-on-first-page", FBPN,
            g => g.SystemsOnPage(0), TwelveSystemsPerPage),

        // --- the same reading for the DYNAMIC family (books DYPQ / DYPD / DYPH) ---
        // EstimateLooseLineExtents' next two branches: 2.0 for a dynamic and 1.5 for a
        // hairpin, both citing DynamicLineSpanner's outside-staff-priority 250, which is a
        // PRIORITY and not either number. One family in LilyPond's model — a hairpin and a
        // dynamic text hang off the same DynamicLineSpanner. The control is
        // figbass.page.control.last-staff-to-foot: this exact paper and this exact quiet
        // music with nothing hanging below it, so it is referenced rather than duplicated.
        new("dynamic.page.quiet.last-staff-to-foot", DYPQ,
            g => g.LastStaffRefpointToFoot(), TwelveSystemsPerPage),
        new("dynamic.page.deep.last-staff-to-foot", DYPD,
            g => g.LastStaffRefpointToFoot(), TwelveSystemsPerPage),
        new("hairpin.page.quiet.last-staff-to-foot", DYPH,
            g => g.LastStaffRefpointToFoot(), TwelveSystemsPerPage),
        new("dynamic.page.quiet.systems-on-first-page", DYPQ,
            g => g.SystemsOnPage(0), TwelveSystemsPerPage),
        new("dynamic.page.deep.systems-on-first-page", DYPD,
            g => g.SystemsOnPage(0), TwelveSystemsPerPage),
        new("hairpin.page.quiet.systems-on-first-page", DYPH,
            g => g.SystemsOnPage(0), TwelveSystemsPerPage),

        // --- where the METRONOME MARK's baseline sits (books TMQ/TMT) ---
        // The tempo island's first points (user-directed, fix tempo first). See
        // TempoScore for the measured decomposition; the TMT control doubles as the
        // priority-1300-over-trill stacking reading — the trill-spanner bar-1
        // "120"-crosses-tr defect is exactly TMT reading like TMQ.
        new("tempo.quiet.staff-to-baseline", TMQ,
            g => g.TempoEquationBaselineAboveStaff(), RaggedBottomPaper),
        new("tempo.trill-cleared.staff-to-baseline", TMT,
            g => g.TempoEquationBaselineAboveStaff(), RaggedBottomPaper),
        // The mark's ink left ON the meter's ink left (self-alignment-X LEFT on the
        // break-aligned time signature; the probe header measured the difference
        // exactly 0). Opened with the port, as the session-32 plan said.
        new("tempo.x.mark-to-time-signature", TMQ,
            g => g.TempoMarkToTimeSignatureLeft(), RaggedBottomPaper),

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

        // --- the same spring SITTING ON ITS SKYLINE FLOOR (books SCF / SCC) ---
        // The entry above reads the spring's IDEAL, because on shipping paper the clef's
        // 3.776 + 3.540 + 1 never reaches basic-distance 12. These read the FLOOR instead:
        // the same music with the ideal and the minimum taken away, so what is left is
        // ensure_min_distance's own argument. See ClefFloorScore for the arithmetic and for
        // what LilyPond answered.
        //
        // A FORK, not one number (HANDOFF 5.0): SCF and SCC differ in two paper values and
        // nothing else, so LilyPond's difference between them IS the mechanism. If they read
        // alike, the paper edit did nothing and SCF is not in the regime it claims.
        // The refpoint and the system count ride along for the two reasons the corpus has
        // been bitten by (README "both sides", HANDOFF 5.0 trap 8): top-system-spacing is
        // untouched, so a moved refpoint means the edit reached further than intended, and a
        // gap read off a page with the wrong number of systems on it is not this gap.
        new("system.clef-floor.floor-bound-distance", SCF, g => g.StaffGap(), ClefFloorPaper),
        new("system.clef-floor.ideal-bound-distance", SCC, g => g.StaffGap(),
            ClefFloorControlPaper),
        new("page.clef-floor.first-staff-refpoint", SCF, g => g.FirstStaffRefpoint(),
            ClefFloorPaper),
        new("page.clef-floor.systems-on-first-page", SCF, g => g.SystemsOnPage(0),
            ClefFloorPaper),

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

        // --- the beam QUANTER itself (Beam.positions), which every point above reads only
        // through the room a beam takes on a page. These are the covered-grob points: a beam
        // is quanted against the INK of the grobs it runs over (lily/beam-quanting.cc:377-392
        // init_instance_variables, :186-209 add_collision), and until 2026-07-31 Lily# fed it
        // one nominal POINT per note column, in a frame a notehead width away from the beam
        // it was compared with. Both ends of the sloped book, because positions is a pair.
        new("beam.quant.over-accidental.left", BQA, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.over-accidental.right", BQA, g => g.BeamPositionAboveStaffMiddle(0, true)),
        // The cross-voice pair in one book: group one is covered by the other voice's head,
        // group two is free. Flat beams, so one end each says everything.
        new("beam.quant.over-other-voice.covered", BQV, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.over-other-voice.free", BQV, g => g.BeamPositionAboveStaffMiddle(1, false)),
        // The other half of that supply, which no point above reaches: a covered grob is
        // booked TWICE, once as its own box and once as its STEM — an interval from
        // chord_start_y to infinity in the stem's direction (:401-418). BQM's covered head
        // clears the beam by 0.76, past the 0.35 padding, so only that interval can explain
        // the lift; BQU is the same lift with an unbeamed stem, which is also reachable by
        // booking the Stem grob's own box. BQF is the control both are measured against.
        // Beam 0 in each book: the measured beam starts at the bar's first column.
        new("beam.quant.over-stem.beamed", BQM, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.over-stem.unbeamed", BQU, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.over-stem.free", BQF, g => g.BeamPositionAboveStaffMiddle(0, false)),
        // A beam over CHORDS, which no point above reaches, and the only ledger entry whose
        // job is to keep a FALSIFIED claim dead (see BQC). Both ends because the beam is
        // sloped; the single-note control must print the same pair, because LilyPond does.
        new("beam.quant.chord.left", BQC, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.chord.right", BQC, g => g.BeamPositionAboveStaffMiddle(0, true)),
        new("beam.quant.chord.single-note-control", BQCC, g => g.BeamPositionAboveStaffMiddle(0, false)),
        // …and the chord beam those three cannot see, because near the middle line the ideal-Y
        // clamp hides which head the length was reckoned from. Both ends, plus the control that
        // keeps LilyPond's identity honest: LilyPond reads ONE head of a chord and this pair
        // differs only in whether the other two heads are written. See BQCD/BQCDC.
        new("beam.quant.chord.spanning.left", BQCD, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.chord.spanning.right", BQCD, g => g.BeamPositionAboveStaffMiddle(0, true)),
        new("beam.quant.chord.spanning.beam-side-head-control", BQCDC, g => g.BeamPositionAboveStaffMiddle(0, false)),
        // The TAB quanter, which no point above reaches: same scorer, a different STAFF.
        // Both ends of a rising group (its slope is the string contour), the same group
        // mirrored (falling), and the two flat single-string groups — one stems-up on the
        // lowest string, one stems-down on the highest — because a flat tab beam is where
        // the staff's own line grid decides and the sloped ones are where its translation
        // does. ⚠️ Read in the TAB staff's own spaces — see the unit note on BQT.
        new("beam.quant.tab.rising.left", BQT, g => g.TabBeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.tab.rising.right", BQT, g => g.TabBeamPositionAboveStaffMiddle(0, true)),
        new("beam.quant.tab.falling.left", BQT, g => g.TabBeamPositionAboveStaffMiddle(1, false)),
        new("beam.quant.tab.flat-lowest-string", BQT, g => g.TabBeamPositionAboveStaffMiddle(2, false)),
        new("beam.quant.tab.flat-highest-string", BQT, g => g.TabBeamPositionAboveStaffMiddle(3, false)),
        // The REACH of a voice { } span, measured one bar outside it, against the same bar
        // with no span in the part at all. LilyPond prints the pair identically because
        // \voiceOne dies with the span; a part-wide reading moves only the first.
        new("beam.voice-span.outside-span", BVS, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.voice-span.no-span-control", BVSC, g => g.BeamPositionAboveStaffMiddle(0, false)),
        // …and the KNEE, the one regime where the members' stems point different ways and the
        // quanter's column-for-stem x therefore cannot cancel.
        new("beam.quant.knee.left", BQK, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.knee.right", BQK, g => g.BeamPositionAboveStaffMiddle(0, true)),
        new("beam.quant.knee.three-stem", BQK3, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.knee.no-leap-control", BQKC, g => g.BeamPositionAboveStaffMiddle(0, false)),
        // …and the regime none of the above can see, because every one of them beams members
        // that carry the SAME number of beam lines: a group where the count CHANGES along the
        // beam. LilyPond takes each stem's ideal length from the beam's MAXIMUM count for that
        // direction (lily/stem.cc:1158 -> lily/beam.cc:1517-1532), not from the stem's own.
        // Both ends of every reading, because what diverges is the SLOPE.
        new("beam.quant.mixed-count.rising-first.left", BMC, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.mixed-count.rising-first.right", BMC, g => g.BeamPositionAboveStaffMiddle(0, true)),
        new("beam.quant.mixed-count.rising-second.left", BMC, g => g.BeamPositionAboveStaffMiddle(1, false)),
        new("beam.quant.mixed-count.rising-second.right", BMC, g => g.BeamPositionAboveStaffMiddle(1, true)),
        new("beam.quant.mixed-count.falling-control.left", BMC, g => g.BeamPositionAboveStaffMiddle(2, false)),
        new("beam.quant.mixed-count.falling-control.right", BMC, g => g.BeamPositionAboveStaffMiddle(2, true)),
        new("beam.quant.mixed-count.max-count.left", BMCX, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.mixed-count.max-count.right", BMCX, g => g.BeamPositionAboveStaffMiddle(0, true)),
        new("beam.quant.mixed-count.uniform-control.left", BMCC, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.mixed-count.uniform-control.right", BMCC, g => g.BeamPositionAboveStaffMiddle(0, true)),
        // …and the pair the maximum-count port left behind: the SAME 8-32-8 rhythm twice, a
        // third apart, one of which the port closed exactly and the other of which it did not
        // touch. LilyPond gives them the same slope and a height one third apart, minus the
        // 1/12 that one forced stem buys (lily/beam.cc:1061-1091 -> lily/stem.cc:1245).
        new("beam.quant.mixed-count.peak-32.unforced.left", BMCU, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.mixed-count.peak-32.unforced.right", BMCU, g => g.BeamPositionAboveStaffMiddle(0, true)),
        new("beam.quant.mixed-count.peak-32.forced-stem.left", BMCF, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.mixed-count.peak-32.forced-stem.right", BMCF, g => g.BeamPositionAboveStaffMiddle(0, true)),

        // …and the regime every point above misses because they are all full size: a GRACE
        // beam, where LilyPond runs the same quanter with three scaled inputs (ly/grace-init.ly
        // — fontSize -3, Beam.beam-thickness 0.384, Beam.length-fraction 0.8, Stem's to match)
        // and Lily# runs a SECOND implementation that does not quant at all
        // (SharedRenderer.GraceNotes.DrawGraceStemsAndBeam, whose own comment says
        // "equal-length stems, so the beam runs parallel to the head contour"). Both ends of
        // all three, because what diverges is height AND slope.
        new("beam.quant.grace.left", BGR, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.grace.right", BGR, g => g.BeamPositionAboveStaffMiddle(0, true)),
        new("beam.quant.grace.full-size-control.left", BGRC, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.grace.full-size-control.right", BGRC, g => g.BeamPositionAboveStaffMiddle(0, true)),
        new("beam.quant.grace.third-up.left", BGRT, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.grace.third-up.right", BGRT, g => g.BeamPositionAboveStaffMiddle(0, true)),

        // …and the FRAME those six are projected through, which nothing above can see. The
        // quanter answers a beam line; what gets drawn is that line evaluated at two x's, and
        // both of them are one number — half a stem thickness (lily/beam.cc:631). LilyPond
        // spends the SAME 0.065 on a grace as on a full-size beam, because Stem.thickness is
        // in line-thickness units and a fontSize does not reach it
        // (audit/lp-geometry/probes/grace-stem-frame.ly: thick=1.3 and a drawn stem extent
        // 0.130000 wide in BOTH books; beam-grace.ly: the beam ends 0.065 outside each stem
        // in BOTH). ★ THE PAIR IS AN IDENTITY ON LILYPOND'S SIDE — the same number four
        // times — so whatever Lily# puts between the two books is the defect's whole size.
        // Both ends of the overhang: one stepped end and one true one is a shape no single
        // reading can tell from a beam that is merely too short.
        new("grace.stem.thickness", BGR, g => g.BeamGroupStemThickness(0)),
        new("grace.stem.thickness.full-size-control", BGRC, g => g.BeamGroupStemThickness(0)),
        new("grace.beam.overhang.left", BGR, g => g.BeamOverhangPastOuterStem(0, false)),
        new("grace.beam.overhang.right", BGR, g => g.BeamOverhangPastOuterStem(0, true)),
        new("grace.beam.overhang.full-size-control.left", BGRC,
            g => g.BeamOverhangPastOuterStem(0, false)),
        new("grace.beam.overhang.full-size-control.right", BGRC,
            g => g.BeamOverhangPastOuterStem(0, true)),

        // …and the register the six above never leave. Every grace point so far puts the
        // beam INSIDE the staff; these two walk up out of it, where LilyPond's forbidden-quant
        // term stops finding a staff line in the beam's gap. J is the divergence (Lily# draws
        // (3.50 . 3.858) against LilyPond's (2.858 . 3.142) — higher AND steeper); K is the
        // bracket just below it, where BOTH engines pay the term and agree to nine places.
        new("beam.quant.grace.above-staff.left", BGRJ, g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.grace.above-staff.right", BGRJ, g => g.BeamPositionAboveStaffMiddle(0, true)),
        new("beam.quant.grace.near-middle-bracket.left", BGRK,
            g => g.BeamPositionAboveStaffMiddle(0, false)),
        new("beam.quant.grace.near-middle-bracket.right", BGRK,
            g => g.BeamPositionAboveStaffMiddle(0, true)),

        // …and the two numbers the four quant points above are SCORED with but cannot see.
        // A beam's height says nothing about how thick its lines are drawn or how far apart
        // they sit, and LilyPond spends both inside score_forbidden_quants: the gap a staff
        // line may not fall into is built out of beam_thickness_ and beam_translation_
        // (lily/beam-quanting.cc:1287-1294). ★ LILYPOND'S SIDE IS TWO DECLARED NUMBERS, not
        // a scale: beam-thickness is 0.384 for a grace and 0.48 full size (ly/grace-init.ly,
        // scm/define-grobs.scm), and the translation follows from them at lily/beam.cc:142-144.
        // Reading them here separates a wrong CONSTANT from a wrong quant, which the position
        // points alone conflate.
        new("grace.beam.thickness", BGR, g => g.BeamLineThickness(0)),
        new("grace.beam.thickness.full-size-control", BGRC, g => g.BeamLineThickness(0)),
        new("grace.beam.stack-gap", BGR, g => g.BeamStackGap(0)),
        new("grace.beam.stack-gap.full-size-control", BGRC, g => g.BeamStackGap(0)),

        // …and the X FRAME those six read through. Until now the grace column's width was
        // measured only INDIRECTLY, by the slope it leaves in the beam; these read it. Head
        // anchor to head anchor, which is the column step in both engines (see
        // RenderedGeometry.NoteheadAnchorStep). The whole law is in the block above GCW3.
        new("grace.column.two-sixteenths.step", BGR, g => g.NoteheadAnchorStep(0)),
        new("grace.column.two-sixteenths.to-main", BGR, g => g.NoteheadAnchorStep(1)),
        new("grace.column.three-sixteenths.step", GCW3, g => g.NoteheadAnchorStep(0)),
        new("grace.column.four-sixteenths.step", GCW4, g => g.NoteheadAnchorStep(0)),
        new("grace.column.four-sixteenths.to-main", GCW4, g => g.NoteheadAnchorStep(3)),
        new("grace.column.two-eighths.step", GCW2E, g => g.NoteheadAnchorStep(0)),
        new("grace.column.two-thirtyseconds.step", GCW2T, g => g.NoteheadAnchorStep(0)),
        new("grace.column.single.to-main", GCW1, g => g.NoteheadAnchorStep(0)),
        new("grace.column.mixed-short-long.step", GCWM, g => g.NoteheadAnchorStep(0)),
        new("grace.column.mixed-short-long.to-main", GCWM, g => g.NoteheadAnchorStep(1)),
        new("grace.column.mixed-long-short.step", GCWN, g => g.NoteheadAnchorStep(0)),
        new("grace.column.mixed-long-short.to-main", GCWN, g => g.NoteheadAnchorStep(1)),
        new("grace.column.accidental.step", GCWA, g => g.NoteheadAnchorStep(0)),
        new("grace.column.approach", GCWP, g => g.NoteheadAnchorStep(0)),
        new("grace.column.approach.main-control", GCWP, g => g.NoteheadAnchorStep(3)),

        // A FLAGGED DOWN-STEM column's reach, which needs a neighbour in the FLAG's own Y band
        // to be visible at all. The pair moves ONE thing — the neighbour's pitch — and the
        // control is what says the reading is the flag's and not the accidental's.
        new("flag.down.reach.low-neighbour", FSF8, g => g.NoteheadAnchorStep(0)),
        new("flag.down.reach.high-neighbour-control", FSFH8, g => g.NoteheadAnchorStep(0)),
        new("flag.up.reach", FSFU8, g => g.NoteheadAnchorStep(0)),

        // …and what the three of them turned out to be measuring, which was not a flag. A
        // column pair's FLOOR is the skyline distance + 0.3, and the rod (that same distance
        // + 0.1) is a separate constraint that cannot reach it. These books have no flag, so
        // they state the mechanism where the island above could only carry it.
        new("column.floor.accidental", CFQS, g => g.NoteheadAnchorStep(0)),
        new("column.floor.accidental.spring-control", CFQN, g => g.NoteheadAnchorStep(0)),
        new("column.floor.accidental.wide", CFQD, g => g.NoteheadAnchorStep(0)),
        new("column.floor.accidental.wide-flagged", CFFD, g => g.NoteheadAnchorStep(0)),

        // A CUE region's own metric. EngravingDefaults.CueScale = 0.66 is declared
        // LILYSHARP-OWN and is an invented rounding; these are what it has to be replaced by.
        // The control is the dowry: it shares the book's shape and must open EXACT.
        new("cue.column.step", CUE1, g => g.NoteheadAnchorStep(2)),
        new("cue.column.main-to-cue", CUE1, g => g.NoteheadAnchorStep(1)),
        new("cue.column.control", CUEC, g => g.NoteheadAnchorStep(1)),
        new("cue.accidental.to-notehead", CUEA,
            g => g.AccidentalToNoteheadAnchor(EmmentalerGlyphs.AccidentalSharp)),
        new("cue.grace.column.to-main", CUEG, g => g.NoteheadAnchorStep(1)),

        // A MUSICA FICTA accidental, which is a grob that STATES font-size −2 — so it is
        // read and drawn out of the SIXTEEN Emmentaler design, not the twenty scaled. The
        // reading is its draw origin against its notehead's: the head's half-width minus
        // the suggestion's own, so it moves if either glyph's box moves. Three glyphs
        // because optical sizing is PER GLYPH — the sharp's box barely changes between the
        // designs (0.000049) while the flat's changes by 0.016384, and no single scale can
        // produce both. The whole law is in the header of probes/editorial-accidental.ly.
        new("editorial.accidental.sharp.head-offset", EDA,
            g => g.SuggestionToNoteheadAnchor(EmmentalerGlyphs.AccidentalSharp)),
        new("editorial.accidental.flat.head-offset", EDA,
            g => g.SuggestionToNoteheadAnchor(EmmentalerGlyphs.AccidentalFlat)),
        new("editorial.accidental.natural.head-offset", EDA,
            g => g.SuggestionToNoteheadAnchor(EmmentalerGlyphs.AccidentalNatural)),

        // --- and the OTHER thing a beam group decides, which no point above can see: how
        // many beam lines reach each stem. Every point above reads a beam's HEIGHT, which
        // the count does not move. These read the count itself, off the drawn segments
        // (RenderedGeometry.BeamletsAtStem), for the interior stems — the only ones
        // lily/beaming-pattern.cc:121-183 touches. Both sides of each, because the answer
        // is a PAIR and a rule that chips the wrong side gets one of them right by itself.
        new("beam.beamlet.peak-8-16-8.left", BLA, g => g.BeamletsAtStem(0, 1, false)),
        new("beam.beamlet.peak-8-16-8.right", BLA, g => g.BeamletsAtStem(0, 1, true)),
        new("beam.beamlet.valley-16-8-16.left", BLB, g => g.BeamletsAtStem(0, 1, false)),
        new("beam.beamlet.valley-16-8-16.right", BLB, g => g.BeamletsAtStem(0, 1, true)),
        new("beam.beamlet.step-down-16-16-8.left", BLC, g => g.BeamletsAtStem(0, 1, false)),
        new("beam.beamlet.step-down-16-16-8.right", BLC, g => g.BeamletsAtStem(0, 1, true)),
        new("beam.beamlet.step-up-8-16-16.left", BLD, g => g.BeamletsAtStem(0, 1, false)),
        new("beam.beamlet.step-up-8-16-16.right", BLD, g => g.BeamletsAtStem(0, 1, true)),
        new("beam.beamlet.plateau-16-8-8-16.first.left", BLE, g => g.BeamletsAtStem(0, 1, false)),
        new("beam.beamlet.plateau-16-8-8-16.first.right", BLE, g => g.BeamletsAtStem(0, 1, true)),
        new("beam.beamlet.plateau-16-8-8-16.second.left", BLE, g => g.BeamletsAtStem(0, 2, false)),
        new("beam.beamlet.plateau-16-8-8-16.second.right", BLE, g => g.BeamletsAtStem(0, 2, true)),
        new("beam.beamlet.peak-8-32-8.left", BLF, g => g.BeamletsAtStem(0, 1, false)),
        new("beam.beamlet.peak-8-32-8.right", BLF, g => g.BeamletsAtStem(0, 1, true)),

        // --- and the question one step above the beamlets: where does the beam END. Two
        // readings each, because the group COUNT alone cannot tell 3+2 from 2+3 — and, for
        // 8/8, a third, because count plus first cannot tell 3+3+2 from 3+2+3.
        new("beam.grouping.four-eight.groups", BG48, g => g.BeamGroupCount()),
        new("beam.grouping.four-eight.first-group", BG48, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.five-eight.groups", BG58, g => g.BeamGroupCount()),
        new("beam.grouping.five-eight.first-group", BG58, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.eight-eight.groups", BG88, g => g.BeamGroupCount()),
        new("beam.grouping.eight-eight.first-group", BG88, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.eight-eight.last-group", BG88, g => g.LastBeamGroupStemCount()),
        new("beam.grouping.two-eight.groups", BG28, g => g.BeamGroupCount()),
        new("beam.grouping.two-eight.first-group", BG28, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.three-eight.groups", BG38, g => g.BeamGroupCount()),
        new("beam.grouping.three-eight.first-group", BG38, g => g.BeamGroupStemCount(0)),
        // The controls, which LilyPond and Lily# already agree on. A port of the grid that
        // moves any of these has broken what was right.
        new("beam.grouping.common-time.groups", BG44, g => g.BeamGroupCount()),
        new("beam.grouping.common-time.first-group", BG44, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.three-four.groups", BG34, g => g.BeamGroupCount()),
        new("beam.grouping.three-four.first-group", BG34, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.two-four.groups", BG24, g => g.BeamGroupCount()),
        new("beam.grouping.two-four.first-group", BG24, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.six-eight.groups", BG68, g => g.BeamGroupCount()),
        new("beam.grouping.six-eight.first-group", BG68, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.nine-eight.groups", BG98, g => g.BeamGroupCount()),
        new("beam.grouping.nine-eight.first-group", BG98, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.common-time-sixteenths.groups", BG44S, g => g.BeamGroupCount()),
        new("beam.grouping.common-time-sixteenths.first-group", BG44S, g => g.BeamGroupStemCount(0)),
        // …and the defect the controls turned up: a rest inside an exception group.
        new("beam.grouping.rest-inside-exception.groups", BG34R, g => g.BeamGroupCount()),
        new("beam.grouping.rest-inside-exception.first-group", BG34R, g => g.BeamGroupStemCount(0)),
        // …and the mirror of it: a beat boundary INSIDE an exception group, which the
        // exception is supposed to beam straight through.
        new("beam.grouping.beat-split-inside-exception.groups", BG44B, g => g.BeamGroupCount()),
        new("beam.grouping.beat-split-inside-exception.first-group", BG44B, g => g.BeamGroupStemCount(0)),
        // …and the exceptions keyed on a TWELFTH rather than an eighth, plus the control that
        // found what those were hiding.
        new("beam.grouping.eighth-triplets-three-four.groups", BGT34, g => g.BeamGroupCount()),
        new("beam.grouping.eighth-triplets-three-four.first-group", BGT34, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.eighth-triplets-common-time.groups", BGT44, g => g.BeamGroupCount()),
        new("beam.grouping.eighth-triplets-common-time.first-group", BGT44, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.triplet-then-eighths-three-four.groups", BGT34M, g => g.BeamGroupCount()),
        new("beam.grouping.triplet-then-eighths-three-four.first-group", BGT34M, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.triplet-then-eighths-common-time.groups", BGT44M, g => g.BeamGroupCount()),
        new("beam.grouping.triplet-then-eighths-common-time.first-group", BGT44M, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.triplet-then-eighths-common-time.last-group", BGT44M, g => g.LastBeamGroupStemCount()),
        new("beam.grouping.sixteenth-triplets.groups", BGT44S, g => g.BeamGroupCount()),
        new("beam.grouping.sixteenth-triplets.first-group", BGT44S, g => g.BeamGroupStemCount(0)),
        // …and the exceptions keyed on a SIXTEENTH and a THIRTY-SECOND, which ask for groups
        // FINER than the beat — the half of the table a merge pass cannot reach.
        new("beam.grouping.six-four-sixteenths.groups", BG64S, g => g.BeamGroupCount()),
        new("beam.grouping.six-four-sixteenths.first-group", BG64S, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.four-two-sixteenths.groups", BG42S, g => g.BeamGroupCount()),
        new("beam.grouping.four-two-sixteenths.first-group", BG42S, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.two-two-thirtyseconds.groups", BG22T, g => g.BeamGroupCount()),
        new("beam.grouping.two-two-thirtyseconds.first-group", BG22T, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.three-two-thirtyseconds.groups", BG32T, g => g.BeamGroupCount()),
        new("beam.grouping.three-two-thirtyseconds.first-group", BG32T, g => g.BeamGroupStemCount(0)),
        // …and the four controls that say it is the LOOKUP that decides: two meters whose
        // entry is too fine to be found for the note in the bar, two whose entry is found and
        // asks for the beat anyway.
        new("beam.grouping.nine-four-sixteenths.groups", BG94S, g => g.BeamGroupCount()),
        new("beam.grouping.nine-four-sixteenths.first-group", BG94S, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.twelve-four-sixteenths.groups", BG124S, g => g.BeamGroupCount()),
        new("beam.grouping.twelve-four-sixteenths.first-group", BG124S, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.three-four-thirtyseconds.groups", BG34T, g => g.BeamGroupCount()),
        new("beam.grouping.three-four-thirtyseconds.first-group", BG34T, g => g.BeamGroupStemCount(0)),
        new("beam.grouping.common-time-thirtyseconds.groups", BG44T, g => g.BeamGroupCount()),
        new("beam.grouping.common-time-thirtyseconds.first-group", BG44T, g => g.BeamGroupStemCount(0)),
        // …the off-beat tuplet, the only book that observes the second tuplet guard…
        new("beam.grouping.offbeat-triplet.groups", BGTOFF, g => g.BeamGroupCount()),
        new("beam.grouping.offbeat-triplet.first-group", BGTOFF, g => g.BeamGroupStemCount(0)),
        // …and the half-measure START of a 3/N meter, whose rule is off by default.
        new("beam.grouping.half-measure-start.groups", BGH34, g => g.BeamGroupCount()),
        new("beam.grouping.half-measure-start.first-group", BGH34, g => g.BeamGroupStemCount(0)),

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
        // --- the chord symbol's WIDTH (chord-symbol-width.ly; fixtures above) ---
        // One binding gap (the width itself + 1.1) and two slack controls (the duration
        // spring, no text metric). See the fixture remarks and the probe header for the
        // fork these three decide together.
        new("chord.symbol-width.minor-pair-gap", LCWA,
            g => g.ChordSymbolAnchor(1) - g.ChordSymbolAnchor(0)),
        new("chord.symbol-width.quarter-spring-control", LCWC,
            g => g.ChordSymbolAnchor(1) - g.ChordSymbolAnchor(0)),
        new("chord.symbol-width.half-spring-control", LCWH,
            g => g.ChordSymbolAnchor(1) - g.ChordSymbolAnchor(0)),
    };

    /// <summary>The first chord symbol's anchor in a SECOND score, for the difference points
    /// above. Rendering twice is the point: the quantity is a difference between scores.</summary>
    private static double ChordAnchorOf(string source) =>
        RenderedGeometry.Render(source).FirstChordSymbolAnchor();
}
