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

using System.Collections.Immutable;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a music mark (segno, coda, fine, D.S., D.C., etc.).
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: mark-engraver.cc:36-89 Mark_engraver class
/// LILYPOND-REF: define-grobs.scm:3650-3710 RehearsalMark, SegnoMark, CodaMark grobs
/// </remarks>
public readonly record struct MusicMarkLayout(
    int MeasureIndex,       // Measure containing this mark
    double X,               // Absolute X position (staff spaces from score start)
    double YUp,             // Y in the LilyPond-native Y-up frame: staff-spaces ABOVE
                            // the (top) staff middle, up-positive (frame B). The draw
                            // adds it to the staff-middle Y-up (os.StaffMiddleYUp).
    MusicMarkType MarkType, // Type of mark
    string Text,            // Display text or glyph
    bool IsSymbol,          // True if should use symbol glyph, false for text
    int SourcePosition,     // For click-to-source mapping
    int SourceIndex = -1,   // F3/B: index into BuildAllMarks() — position-independent
                            //   ref so a reused layout re-derives SourcePosition from
                            //   the live score (see SharedRenderer.ResolveDataPos).
    int SwingSubdivision = 0, // Tempo marks only: note value to swing (0/8/16) for the feel equation.
    string? TempoText = null, // Tempo marks only: bold marking text ("Grave").
    int TempoBeatUnit = 4,    // Tempo marks only: metronome beat unit.
    int TempoDots = 0,        // Tempo marks only: dots on the beat unit.
    int StaffIndex = -1       // owning staff (-1 = top staff); the draw resolves its middle
);

/// <summary>
/// Calculates positions for music marks.
/// Implements LilyPond's mark positioning algorithm with outside-staff-priority stacking.
/// </summary>
/// <remarks>
/// LILYPOND-REF: mark-engraver.cc:46-89 Mark creation
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
/// LILYPOND-REF: axis-group-interface.cc:865-984 skyline_spacing / outside-staff-priority stacking
///
/// LilyPond places marks:
/// - Above staff (direction = UP) for most marks
/// - Below staff (direction = DOWN) for expression marks (rit., accel., etc.)
/// - At beginning of measure for segno/coda
/// - At end of measure for fine/D.S./D.C.
///
/// When multiple marks appear at the same position, they are stacked using
/// outside-staff-priority: lower priority marks are placed closer to the staff,
/// higher priority marks are placed farther away.
/// </remarks>
internal static class MusicMarkEngraver
{
    // LILYPOND-REF: define-grobs.scm:3665 padding = 0.5
    private const double Padding = 0.5;

    /// <summary>
    /// WHAT a music mark's text is, typographically — the <see cref="TextRole"/> both the
    /// drawing and the reservation ask the score's <c>font</c> directive about.
    /// </summary>
    /// <remarks>
    /// ONE HOME, and it has to be: the layout reserves a mark's box and the renderer draws
    /// it, and if the two named different roles a score could bind one of them and move only
    /// half the pair. That is the same reserve-versus-draw split this engine keeps finding,
    /// arriving through a mapping rather than through a measurement.
    /// <para>
    /// ⚠️ THE STYLE IS NOT IN THIS MAPPING. Weight and slant are the engraving's decision (a
    /// sostenuto word is italic, a sustain word is not) and a <c>font</c> directive does not
    /// touch them — <c>IDrawingContext.DrawText</c> splits the two parameters for the same
    /// reason. It has its own one home beside this one, <see cref="TextStyleOf"/>: separate
    /// because a binding reaches the role and never the style, not because either may be
    /// spelled twice.
    /// </para>
    /// </remarks>
    internal static TextRole TextRoleOf(MusicMarkType type) => type switch
    {
        MusicMarkType.Rehearsal or MusicMarkType.SectionLabel => TextRole.Mark,
        MusicMarkType.Tempo => TextRole.Tempo,
        MusicMarkType.SustainOn or MusicMarkType.SustainOff
            or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
            or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff => TextRole.Pedal,
        // D.S. / D.C. / Fine / To Coda / segno / coda — the navigation family.
        _ => TextRole.Navigation,
    };

    /// <summary>
    /// The em a plain-text music mark (D.S./Fine/pedal words/…) is set at.
    /// </summary>
    /// <remarks>
    /// ONE HOME for the same reason <see cref="TextRoleOf"/> is one: the draw and every
    /// reservation must price the same stencil. LilyPond has no "estimated" width — a mark's
    /// X extent IS its markup stencil's — so a reservation at another size is not an
    /// approximation of the drawn box, it is a box around a string nobody draws.
    /// <para>
    /// ⚠️ MEASURED, 2026-08-18: this was spelled three times at three sizes. The draw and the
    /// outside-staff stacker used 2.8 (the renderer's 4.0 staff-space font size × the 0.7
    /// plain-text factor); <c>MarkXExtent</c> used 2.2, the boxed SectionLabel size next to
    /// it in the same switch; the inter-system skyline box in <c>LayoutEngine</c> used 2.4,
    /// the boxed Rehearsal size next to IT. Both strays priced the neighbouring case, and
    /// both under-reserved: "Fine" by 1.263302362 and 0.819439370 staff spaces, "D.S. al
    /// Coda" by 4.233770079 and 2.868037795 — always short, i.e. always toward a collision
    /// the overlap tests could not see.
    /// </para>
    /// </remarks>
    // LILYPOND-REF: scm/define-grobs.scm:1898-1926 JumpScript, outside-staff-priority 1350
    //   — font-shape italic, and NO font-size anywhere in the block.
    // LILYPOND-REF: scm/define-grobs.scm:3190-3208 SostenutoPedal piano-pedal-script-interface
    //   — the same declaration: font-shape italic, no font-size, no font-series.
    // LILYPOND-REF: scm/define-grobs.scm:4148-4166 UnaCordaPedal piano-pedal-script-interface
    //   — the same again.
    // A grob that names no font-size is set at the paper's own text-font-size, which is what
    // EngravingDefaults.TextScriptFontSize already carries. READ, NOT RESPELLED: one home
    // and this is the second grob family to move into it (§5.2.1⑤ — a copy is how the same
    // quantity grows a second spelling, which is the defect session 203 spent its day on).
    //
    // ⚠️ IT USED TO BE 4.0 * 0.7 = 2.8, THE RENDERER'S OWN FONT SIZE TIMES A GUESS. That was
    // tagged LILYSHARP-OWN on 2026-08-18 (session 203) with the four things the tag owes,
    // one of which was "it disappears when a session prices the mark's em against LilyPond
    // and opens a ledger point". Session 204 did: audit/lp-geometry/probes/jump-mark-em.ly
    // measures the real JumpScript at 4.506916535433071 ss for "Fine", drawn in C059-Italic
    // at a stencil em of 3.865234375 mm = 2.2 ss × 1.757299018 mm/ss — the drawing's own
    // account of its size, not an inference from a width. Four ledger points observe it now
    // (mark.jump.width.fine / .ds-al-coda, mark.pedal.width.sostenuto / .sustain).
    internal static readonly double PlainTextFontSize = EngravingDefaults.TextScriptFontSize;

    /// <summary>
    /// Weight and slant the engraving gives a plain-text mark: italic for the jump scripts
    /// and the pedals that carry a string, upright bold for the sustain pedal alone.
    /// </summary>
    /// <remarks>
    /// The style is the ENGRAVING's decision and no <c>font</c> directive may touch it (see
    /// <see cref="TextRoleOf"/>), which is exactly why it needs a home of its own rather
    /// than none: three sites spelled it and one of them — the reservation the overlap tests
    /// read — said Bold for strings drawn BoldItalic.
    /// <para>
    /// LILYPOND-REF: scm/define-grobs.scm:1898-1926 JumpScript, outside-staff-priority 1350
    /// — <c>font-shape italic</c> and no <c>font-series</c> in the block at all.
    /// LILYPOND-REF: scm/define-grobs.scm:3190-3208 SostenutoPedal piano-pedal-script-interface
    /// — the same <c>font-shape italic</c>, no series.
    /// Italic, and NOT bold.
    /// </para>
    /// <para>
    /// ⚠️ IT USED TO ANSWER <c>BoldItalic</c>, and session 203 tagged that LILYSHARP-OWN
    /// rather than porting it, because moving the slant in the same commit as the
    /// reservation would have moved the page for two reasons at once. MEASURED FIRST
    /// (session 204, audit/lp-geometry/probes/jump-mark-em.ly), per string rather than as a
    /// ratio: LilyPond's own weight costs +0.512149606 ss on "Fine", +1.058442520 on "D.S.
    /// al Coda" and +0.785296063 on "Sost. Ped." — three strings, three ratios (1.114 /
    /// 1.082 / 1.079), so the term is a face TABLE and no string's may be borrowed for
    /// another's. With this and <see cref="PlainTextFontSize"/> both read from LilyPond,
    /// <c>mark.jump.width.fine</c> and <c>mark.jump.width.ds-al-coda</c> are 0.000000000 and
    /// <c>mark.pedal.width.sostenuto</c> is one device pixel.
    /// </para>
    /// <para>
    /// ⚠️ THE SUSTAIN ARM IS NOT THE SAME QUESTION, and the same probe is why: LilyPond
    /// does not set "Ped." in a text face at all. <c>lily/sustain-pedal.cc:47-76</c>
    /// <c>Sustain_pedal::print</c> pastes the music font's <c>pedal.Ped</c> and
    /// <c>pedal..</c> glyphs edge to edge, the file's own comment saying "we have no
    /// kerning". So this arm's upright bold agrees with nothing in LilyPond rather than
    /// disagreeing with something, and it is LEFT ALONE here:
    /// <c>mark.pedal.width.sustain</c> records that the em change moves it by 1.365732283
    /// and leaves 1.478779528 standing, which is what setting the word in a text face costs
    /// at all. Closing that is a MECHANISM port (Emmentaler's pedal glyphs), not a style one.
    /// </para>
    /// </remarks>
    internal static FontStyle TextStyleOf(MusicMarkType type)
        => type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
            ? FontStyle.Bold
            : FontStyle.Italic;

    /// <summary>
    /// How wide a plain (unboxed, non-symbol) mark's word is drawn — the text families'
    /// advance, or the sustain pedal's glyph run.
    /// </summary>
    /// <remarks>
    /// ONE HOME for the same reason <see cref="PlainTextFontSize"/> is one, and it exists
    /// because the family is no longer answered by a single call: since 2026-08-18 the
    /// sustain pedal's word is a run of MUSIC glyphs and everything else is text, so a site
    /// that asks <c>TextFontMetrics.Advance</c> directly is asking about a string nobody
    /// sets. Callers: <c>MarkXExtent</c> and the pedal-change nudge below.
    /// </remarks>
    internal static double PlainMarkWidth(ScoreTextMetrics fonts, MusicMarkType type, string text)
        => IsGlyphPedal(type)
            ? SustainPedalExtent(text).Width
            : fonts.Advance(text, PlainTextFontSize, TextRoleOf(type), TextStyleOf(type));

    /// <summary>
    /// Whether this mark's word is set in the MUSIC font rather than in a text face —
    /// true for the sustain pedal alone.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3573-3591 SustainPedal, piano-pedal-interface —
    /// the one pedal grob whose stencil is <c>ly:sustain-pedal::print</c> rather than
    /// <c>ly:text-interface::print</c>.
    /// LILYPOND-REF: scm/define-grobs.scm:3190-3208 SostenutoPedal, piano-pedal-script-interface
    /// — text, which is why <see cref="TextStyleOf"/> still answers for it.
    /// LILYPOND-REF: scm/define-grobs.scm:4148-4166 UnaCordaPedal, piano-pedal-script-interface
    /// — text likewise, and this predicate answers for neither.
    /// </remarks>
    internal static bool IsGlyphPedal(MusicMarkType type)
        => type is MusicMarkType.SustainOn or MusicMarkType.SustainOff;

    /// <summary>
    /// Which ROW a pedal family occupies below the staff, nearest first — the order
    /// pedal-three.ly measured on 2.26.0 (una corda 2.7775, sostenuto 4.7387, sustain
    /// 7.1813 below the bottom line; the guess it replaced had una corda outermost).
    /// ONE HOME: the legacy row stack here and the skyline-time solver
    /// (PedalEngraver.SolveAndSeedText) order families through this same method.
    /// </summary>
    internal static int PedalFamilyRank(MusicMarkType t) => t switch
    {
        MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff => 0,
        MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff => 1,
        _ => 2,
    };

    /// <summary>One glyph of a sustain-pedal word, and its LEFT edge in staff spaces from
    /// the word's own origin.</summary>
    internal readonly record struct PedalGlyphPlacement(char Glyph, double X);

    /// <summary>
    /// The stencil LilyPond builds for a sustain-pedal word: the glyphs, the total width
    /// and the ink top. ONE HOME for the draw and every reservation, like
    /// <see cref="PlainTextFontSize"/> is for the family that IS text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: lily/sustain-pedal.cc:47-76 Sustain_pedal::print — it walks the
    /// string, takes <c>pedal.Ped</c> for a literal "Ped" and <c>pedal.&lt;char&gt;</c> for
    /// every other character, and pastes each with
    /// <c>Stencil::add_at_edge (X_AXIS, RIGHT, m, 0)</c>: zero padding, EXTENT to extent.
    /// A name the font does not have yields an empty stencil and is skipped, which is why
    /// the space in "Sost. Ped." would cost nothing there (that string is a TEXT grob and
    /// never reaches this code).
    /// </para>
    /// <para>
    /// The extents are LilyPond's own LILC boxes (lily/open-type-font.cc:372-409
    /// get_indexed_char_dimensions), which is what <c>GlyphMetrics</c> carries. MEASURED
    /// against real LilyPond (audit/lp-geometry/probes/jump-mark-em.ly book PSU,
    /// 2026-08-18): "Ped." is 3.192000000 + 0.280000000 = 3.472000000 staff spaces and "*"
    /// is 1.555600000, both to nine digits, and ledger point
    /// <c>mark.pedal.width.sustain</c> is what holds them.
    /// </para>
    /// <para>
    /// ⚠️ Lily# drew this word as an upright bold SERIF STRING until 2026-08-18. That was
    /// not a size error and porting the em did not fix it: at LilyPond's own em the string
    /// was still 1.478779528 staff spaces too wide, because a text face and a music font
    /// have no arithmetic in common. The gap is the whole reason this method exists.
    /// </para>
    /// </remarks>
    internal static (ImmutableArray<PedalGlyphPlacement> Glyphs, double Width, double Top)
        SustainPedalStencil(string text)
    {
        var glyphs = ImmutableArray.CreateBuilder<PedalGlyphPlacement>(text.Length);
        var (width, top) = WalkSustainPedal(text, glyphs);
        return (glyphs.ToImmutable(), width, top);
    }

    /// <summary>
    /// The same stencil's WIDTH and ink top, without building the glyph list.
    /// </summary>
    /// <remarks>
    /// Every reservation wants only these two numbers and the draw is the one caller that
    /// wants the glyphs, so the reservations do not pay for a list they will throw away.
    /// ⚠️ Not a second spelling of the mapping — both go through the same private walk.
    /// </remarks>
    internal static (double Width, double Top) SustainPedalExtent(string text)
        => WalkSustainPedal(text, into: null);

    /// <summary>The walk itself: LilyPond's loop, with the glyph list optional.</summary>
    private static (double Width, double Top) WalkSustainPedal(
        string text, ImmutableArray<PedalGlyphPlacement>.Builder? into)
    {
        double x = 0.0, top = 0.0;
        for (int i = 0; i < text.Length; i++)
        {
            char glyph;
            if (i + 3 <= text.Length && text.AsSpan(i, 3) is "Ped")
            {
                glyph = EmmentalerGlyphs.PedalPed;
                i += 2;   // with the loop's own i++ this is LilyPond's `i += 2`
            }
            else if (text[i] == '.') glyph = EmmentalerGlyphs.PedalDot;
            else if (text[i] == '*') glyph = EmmentalerGlyphs.PedalStar;
            // find_by_name gave an empty stencil; LilyPond skips it and so do we.
            else continue;

            var box = PedalGlyphBox(glyph);
            into?.Add(new PedalGlyphPlacement(glyph, x));
            x += box.Width;
            top = Math.Max(top, box.Top);
        }
        return (x, top);
    }

    /// <summary>
    /// The LILC box of one sustain-pedal glyph — the extent LilyPond juxtaposes it by.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SustainPedalStencil"/> so that a reader who has a DRAWN
    /// glyph rather than a string can price it: the LP-fidelity observer measures the run
    /// off the page (first glyph's origin to the last one's right edge) and needs the last
    /// glyph's own width to close it. One table, three consumers — the builder, the
    /// observer, and anything that later asks how tall the word is.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The glyph is not one of the three the
    /// sustain pedal is built from. Loud rather than a zero box: a silent 0 here would be a
    /// mark that reserves nothing.</exception>
    internal static GlyphMetrics.BBox PedalGlyphBox(char glyph) => glyph switch
    {
        EmmentalerGlyphs.PedalPed => GlyphMetrics.PedalPed,
        EmmentalerGlyphs.PedalDot => GlyphMetrics.PedalDot,
        EmmentalerGlyphs.PedalStar => GlyphMetrics.PedalStar,
        _ => throw new ArgumentOutOfRangeException(nameof(glyph), glyph,
            "not a sustain-pedal glyph"),
    };

    /// <summary>
    /// Baseline for below-staff marks (pedal text etc.): the system's last
    /// staff bottom + 1.5sp + padding. The pedal BRACKET LINE runs on this
    /// same baseline so "Ped." text, line and the release "*" align in the
    /// classic Ped.____* shape.
    /// </summary>
    public static double BelowMarkBaseline(double systemBottom)
        => systemBottom + 1.5 + Padding;

    // LILYPOND-REF: define-grobs.scm RehearsalMark padding=0.8
    private const double AboveStaffOffset = -2.0;

    // LILYPOND-REF: axis-group-interface.cc:45 default_outside_staff_padding_ = 0.46
    private const double OutsideStaffPadding = 0.46;

    // Extra drop for D.S./D.C. jump instructions so they sit clear of low notes.
    private const double JumpInstructionDrop = 1.5;

    // A below-staff mark drops this far past the lowest lyric baseline (descent +
    // gap) so "D.C." / "Fine" clear the words rather than overprinting them.
    private const double LyricClearance = 2.0;

    /// <summary>A jump-FROM instruction (D.S./D.C. family) — placed below the staff.</summary>
    private static bool IsJumpInstruction(MusicMarkType type) =>
        type is MusicMarkType.DalSegno or MusicMarkType.DaCapo
             or MusicMarkType.DalSegnoAlFine or MusicMarkType.DalSegnoAlCoda
             or MusicMarkType.DaCapoAlFine or MusicMarkType.DaCapoAlCoda;

    // Gap between stacked marks
    // LILYPOND-REF: axis-group-interface.cc:45 default_outside_staff_padding_ = 0.46
    private const double StackGap = 0.46;

    /// <summary>
    /// The mark family's horizon padding — how far its skyline foot reaches sideways
    /// (flat by this much, then a 45° flank of the same width) when it is measured
    /// against a neighbour's profile.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:2887,3055,2345 — outside-staff-horizontal-padding 0.2,
    /// declared alike on RehearsalMark, SectionLabel and MetronomeMark; CodaMark and its
    /// siblings carry the same number. The default for a grob that declares none
    /// is 0.0 (lily/axis-group-interface.cc:750-751), which is why the CHORD side of the
    /// distance is unpadded: max(0.2, 0.0) picks the mark's (:661).
    /// </remarks>
    private const double OutsideStaffHorizontalPadding = 0.2;

    /// <summary>
    /// Calculates layout for all music marks in a score, including section labels.
    /// Section labels from measures are merged with explicit music marks and
    /// stacked using outside-staff-priority when they overlap.
    /// </summary>
    /// <summary>
    /// Measure indices covered by an above-staff volta bracket, and the highest
    /// (most negative) volta Y across all brackets.
    /// </summary>
    /// <remarks>LILYPOND-REF: define-grobs.scm:4325 VoltaBracketSpanner outside-staff-priority=600</remarks>
    private static (HashSet<int> Measures, double TopYUp) BuildVoltaCoverage(
        ImmutableArray<VoltaBracketLayout> voltaBrackets)
    {
        var voltaMeasures = new HashSet<int>();
        // Highest volta position in the mark frame (Y-up above the top-staff middle).
        // vb.YUp is Y-up from the system top, so its mark-frame Y-up is 2.0 + vb.YUp.
        // Start at the top-staff top line (Y-up 2.0) and keep the largest (highest).
        double voltaTopYUp = 2.0;
        if (!voltaBrackets.IsDefaultOrEmpty)
        {
            foreach (var vb in voltaBrackets)
            {
                for (int mi = vb.StartMeasureIndex; mi <= vb.EndMeasureIndex; mi++)
                    voltaMeasures.Add(mi);
                double vbYUp = 2.0 + vb.YUp;
                if (vbYUp > voltaTopYUp)
                    voltaTopYUp = vbYUp;
            }
        }
        return (voltaMeasures, voltaTopYUp);
    }

    public static ImmutableArray<MusicMarkLayout> Calculate(
        ScoreTextMetrics fonts,
        Score? score,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        ImmutableArray<VoltaBracketLayout> voltaBrackets = default,
        ImmutableArray<ChordNameLayout> chordNames = default,
        ImmutableArray<LyricLayout> lyrics = default,
        // Optional per-mark gate: a mark for which this returns false reserves its
        // SourceIndex but draws no text (used to hide the "Ped." / "*" a bracket or
        // mixed pedal style replaces). Null keeps every mark.
        Func<MusicMarkItem, bool>? keepMarkText = null,
        // Per system, the ABSOLUTE X of the line-start TimeSignature column's ink left,
        // or NaN when that system's prefix engraves no meter — the break-align anchor a
        // measure-start metronome mark self-aligns LEFT on. Null (single-staff callers
        // without the prefix model) falls back to the first musical column.
        // LILYPOND-REF: scm/define-grobs.scm:2337,2352,2360 MetronomeMark break-align-symbols
        //   (time-signature), self-alignment-X LEFT,
        //   X-offset self-alignment-interface::self-aligned-on-breakable.
        Func<int, double>? prefixTimeSignatureX = null,
        Func<int, double>? lineStartBarlineX = null,
        // Per system, the ABSOLUTE X of the line-start break-align anchor a
        // (staff-bar key-signature clef)-aligned mark lands on — the key ink's right
        // edge, or the clef's when no key stands; NaN when the prefix has no clef
        // column. Null (single-staff callers without the prefix model) keeps the
        // line-start edge. See CalculateXPosition's Rehearsal arm for the mechanism
        // and the LILYPOND-REFs.
        Func<int, double>? prefixMarkAnchorX = null,
        // TEXT-style pedal words solved at skyline-build time: (staff, system, the
        // mark's SOURCE POSITION) -> the word's baseline, Y-up about that STAFF's middle
        // line. Null (or a null answer) keeps the legacy below-the-system stack — the
        // bracket/mixed styles, an ossia's scale, and callers without per-staff
        // skylines.
        Func<int, int, int, double?>? solvedPedalRowUp = null)
    {
        // Merge section labels and tempo marking into the mark list
        var allMarks = BuildAllMarks(musicMarks, measures, score?.Tempo, score?.SwingSubdivision ?? 0,
            score?.TempoText, score?.TempoBeatUnit ?? 4, score?.TempoDots ?? 0,
            score?.Header.Tempo ?? 0);

        if (allMarks.Length == 0)
            return ImmutableArray<MusicMarkLayout>.Empty;

        // Calculate X positions and group marks that need stacking.
        // F3/B: each entry carries its index into allMarks (SourceIndex) so the
        // emitted layout can re-derive its data-pos from the live score later,
        // even though GroupBy/OrderBy below reorders the entries.
        var markEntries = new List<(MusicMarkItem Mark, double X, int SourceIndex)>();
        for (int si = 0; si < allMarks.Length; si++)
        {
            var mark = allMarks[si];
            if (mark.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[mark.MeasureIndex];
            double x = CalculateXPosition(
                fonts, mark, measureLayout, systems, prefixTimeSignatureX, lineStartBarlineX,
                prefixMarkAnchorX, measures);
            markEntries.Add((mark, x, si));
        }

        // LILYPOND-REF: axis-group-interface.cc:865-984 skyline_spacing
        // Group by (MeasureIndex, Position) for collision stacking.
        // Marks at the same measure+position are sorted by outside-staff-priority
        // and stacked outward from the staff.

        // Build volta bracket coverage: measure indices that have a volta bracket above.
        var (voltaMeasures, voltaTopYUp) = BuildVoltaCoverage(voltaBrackets);

        // Group by measure + position + ANCHOR TIMING so only marks that share a
        // horizontal column stack vertically. Without the timing, a mid-measure
        // tempo (e.g. \tempo at beat 2) stacked onto the line-start tempo/section
        // label even though they sit at different X, stealing the bottom slot and
        // floating the opening marks far above the staff (grand-staff regression).
        var groups = markEntries
            .GroupBy(e => (e.Mark.MeasureIndex, e.Mark.Position, e.Mark.AnchorTiming))
            .ToList();

        // BELOW-staff marks (pedal text etc.) hang under the LAST staff of
        // the measure's system, not under the top staff — in a grand staff
        // the old top-staff constant dropped "Ped." between the staves,
        // straight into the rh's low ledger notes.
        // LILYPOND-REF: ly/engraver-init.ly — Piano_pedal_engraver lives in
        //   PianoStaff/GrandStaff context: pedal grobs attach below the
        //   whole staff group.
        // X coordinates are SYSTEM-LOCAL: any ink comparison (chords, lyrics)
        // must be restricted to the mark's own system, or bar-1 marks dodge
        // phantom chords from every later line.
        var measureToSystemIdx = new Dictionary<int, int>();
        for (int si2 = 0; si2 < systems.Length; si2++)
            foreach (var ml2 in systems[si2].Measures)
                measureToSystemIdx[ml2.MeasureIndex] = si2;
        bool SameSystem(int measureA, int measureB)
            => measureToSystemIdx.TryGetValue(measureA, out int a)
            && measureToSystemIdx.TryGetValue(measureB, out int b)
            && a == b;

        // How far the mark's ANCHOR STAFF's top line sits below the system top, Y-up —
        // 0 while that staff leads the system, negative once a chords or lyrics row does.
        // It is the conversion every "above the staff" comparison in this method needs and
        // none of them had: a mark's own frame is its staff's, and ChordNameLayout's is the
        // system's. The anchor is LayoutUtilities' (StaffAffinity.TopSpaceableStaff), the
        // same staff the outside-staff pass prices the mark against.
        double AnchorStaffUp(int measureIndex)
            => measureToSystemIdx.TryGetValue(measureIndex, out int sysIdx)
               && sysIdx >= 0 && sysIdx < systems.Length
                ? LayoutUtilities.StaffOffsetInSystemUp(
                    systems[sysIdx], LayoutUtilities.TopScoreGrobStaff(systems[sysIdx]))
                : 0.0;

        // How far below its band top the anchor keeps its own baseline, on a system that has
        // NO STAFF — null on every system that has one.
        //
        // ⚠️ LILYSHARP-OWN, AND IT IS A USER DECISION (2026-08-24), NOT A PORT. LilyPond puts
        // a mark on a staffless sheet ABOVE the row's symbols (probes/mark-chord-row.ly books
        // MKT/MKS/MKV all read the row's ink top + outside-staff-padding 0.460000, the same
        // number for two different grobs and two different symbol heights). Lily# is asked for
        // the LEAD-SHEET convention instead: the section letter belongs ON the chord line,
        // level with the symbols, the way a printed chart sets it. That is a divergence with a
        // decision behind it, and it is deliberately NARROW — it applies only where there is
        // no staff to hang on, because the owner confirmed the same book WITH a staff
        // (`chords / staff / lyrics`) already places its marks correctly (2026-08-24).
        // ⇒ WHEN IT WOULD GO: if the convention is ever dropped, this method loses its arm and
        // the mark returns to base + Padding; nothing else changes.
        // ⚠️ THE X HALF TRAVELS WITH IT (HANDOFF 5.3): a label sharing the symbols' line can
        // land ON one, so ChordNameEngraver reserves the label's box in the row's spacing.
        // Placing without reserving is the shape that printed a tempo through greensleeves'
        // first chord in session 243.
        double? StafflessAnchorRefpointBelowTop(int measureIndex)
        {
            if (!measureToSystemIdx.TryGetValue(measureIndex, out int sysIdx)
                || sysIdx < 0 || sysIdx >= systems.Length)
                return null;
            var system = systems[sysIdx];
            int anchor = LayoutUtilities.TopScoreGrobStaff(system);
            // ONE CONDITION, AND IT SAYS BOTH HALVES: the row these grobs hang on carries
            // CHORD-ROW symbols.
            //   ⑴ "no staff" comes free — LayoutUtilities.TopScoreGrobStaff returns the top
            //      SPACEABLE staff whenever the system has one, and an independent chords row
            //      is never spaceable, so the anchor is a chords row exactly on a staffless
            //      sheet. A separate `TopSpaceableStaff is not null' guard was here and NO
            //      TEST COULD MAKE IT RED on its own (HANDOFF bone 2: an arm that cannot go
            //      red is not a control); it is this line that both controls watch.
            //   ⑵ "there are chord names to be level with" is the decision itself. A
            //      lyrics-only sheet has none, and nothing there would move out of the label's
            //      way either — the X reservation only ever shifts chord symbols. MEASURED:
            //      without this, test/lead-sheet-lyrics printed its `Main' box straight
            //      through the first syllable.
            // ⚠️ THE ITEMS, NOT THE LAYOUTS: a ChordNameLayout carries no StaffIndex, and the
            // question is which ROW the symbols belong to. Asked of the same array the X half
            // is gated on (LayoutEngine hands it ctx.ChordNames), so the two arms cover
            // exactly the same set of books.
            var chordItems = score?.ChordNames ?? ImmutableArray<ChordNameItem>.Empty;
            if (chordItems.IsDefaultOrEmpty
                || !chordItems.Any(c => c.IsChordRow && c.StaffIndex == anchor))
                return null;
            if (system.StaffGroups.IsDefaultOrEmpty) return null;
            foreach (var g in system.StaffGroups)
                foreach (var st in g.Staves)
                    if (st.StaffIndex == anchor)
                        return st.RefpointBelowTop;
            return null;
        }

        var measureToSystemBottom = new Dictionary<int, double>();
        foreach (var system in systems)
        {
            double bottom = 4.0;
            if (!system.StaffGroups.IsDefaultOrEmpty)
            {
                foreach (var g in system.StaffGroups)
                    foreach (var st in g.Staves)
                        if (!st.IsHidden)
                            bottom = Math.Max(bottom, st.Height - st.Y);
            }
            foreach (var ml in system.Measures)
                measureToSystemBottom[ml.MeasureIndex] = bottom;
        }

        // Lowest lyric baseline per system — a below-staff mark (D.C./D.S./Fine)
        // must drop past the lyrics, which occupy the same band under the staff, or
        // it overprints them.
        var systemLyricBottomUp = new Dictionary<int, double>();
        if (!lyrics.IsDefaultOrEmpty)
            foreach (var ly in lyrics)
                // ly.YUp is Y-up from the system top; its mark-frame Y-up (from the
                // top-staff middle) is 2.0 + ly.YUp. Track the LOWEST lyric baseline =
                // the smallest mark-frame Y-up per system.
                if (measureToSystemIdx.TryGetValue(ly.Item.MeasureIndex, out int lySys)
                    && (!systemLyricBottomUp.TryGetValue(lySys, out double cur) || 2.0 + ly.YUp < cur))
                    systemLyricBottomUp[lySys] = 2.0 + ly.YUp;

        var layouts = ImmutableArray.CreateBuilder<MusicMarkLayout>();

        foreach (var group in groups)
        {
            // Separate above-staff and below-staff marks
            var aboveMarks = group
                .Where(e => e.Mark.Vertical == MusicMarkVertical.Above)
                .OrderBy(e => GetOutsideStaffPriority(e.Mark.Type))
                .ToList();

            var belowMarks = group
                .Where(e => e.Mark.Vertical == MusicMarkVertical.Below)
                .OrderBy(e => GetOutsideStaffPriority(e.Mark.Type))
                .ToList();

            // Check if any mark in this group overlaps with a volta bracket
            bool hasVoltaOverlap = aboveMarks.Any(e => voltaMeasures.Contains(e.Mark.MeasureIndex));

            // LILYPOND-REF: axis-group-interface.cc:652-681 avoid_outside_staff_collisions
            // Marks with priority > 600 (VoltaBracketSpanner) must be placed above volta.
            // Base Y for above-staff stacking: if volta present, start above volta top.
            // Base Y-up for above-staff stacking (from the top-staff middle, up+).
            // AboveStaffOffset is a device (down+) offset, so its Y-up value is 2 − it.
            double baseAboveYUp = 2.0 - AboveStaffOffset;
            // ⚠️ AND ON A STAFFLESS SHEET THAT CONSTANT IS A STAFF'S SHAPE WITH NO STAFF
            // UNDER IT -- 2.0 - AboveStaffOffset is "two and a half above the top LINE",
            // which is what clears five staff lines. A chords or lyrics ROW has none and its
            // ink stops 0.300000 above its band top, so the base sits ABOVE the row's ink and
            // the outside-staff pass -- the thing that would place the mark at the ink plus
            // padding -- never engages. MEASURED (user report 2026-08-24): the label stands
            // 0.900000 over the chord row's ink where LilyPond puts it at 0.460000 (probe
            // mark-chord-row.ly book MKT), the last 0.440000 of a gap that was 1.960000
            // before the phantom clef went (OutsideStaffStacker.SeedClefInk).
            // ⚠️ ⚠️ NOT CLOSED HERE -- BUT THE REASON IT GIVES FOR NOT CLOSING IT EXPIRED 35
            // MINUTES AFTER IT WAS WRITTEN, and the sentence stood for a session and a half.
            // It said the obvious close overshoots by 0.300000 because
            // OutsideStaffStacker.AboveTrackers folds `topUp - StaffBottom / 2.0', the NOMINAL
            // half staff, and that "StaffLayout does not carry the row's refpoint (no
            // IsTextRow, no baseline) -- a model change, not a constant". BOTH HALVES ARE NOW
            // FALSE: 8d6b7978 made that exact model change (a placed StaffLayout carries
            // RefpointBelowTop and AboveTrackers folds it) in the commit right after 8ba98bfa,
            // which is this comment's. HANDOFF 5.1: a note whose reason lives in ANOTHER
            // island turns into a lie the day that island is fixed, and the fixing commit is
            // where it must be re-read.
            // ⇒ WHAT IS ACTUALLY IN THE WAY, measured 2026-08-25 (session 251): the tracker is
            // keyed per (system, STAFF) and an independent chord row is its OWN staff index,
            // so the mark -- anchored to a staff -- never meets the row's profile whatever
            // frame that profile is folded into. On the ROWM family this shows as a mark that
            // is drawn 4.701073 above its staff in EVERY book of the sweep, unmoved to the
            // sixth digit even when the row's ink grows by 1.023251 under it. Ledger
            // lyrics.chord-row.marked.empty-row.* carries the sweep and the LilyPond side.
            // The 0.440000 measured above stands; only the diagnosis under it was wrong.
            if (hasVoltaOverlap)
            {
                // Place marks above the volta bracket with outside-staff padding.
                baseAboveYUp = voltaTopYUp + OutsideStaffPadding;
            }

            // Chord symbols are the line CLOSEST to the staff (LP: the marks'
            // outside-staff priority 1500 beats ChordNames), so a mark standing over a
            // chord symbol clears it — otherwise a section label box (or a wide tempo
            // marking like "Brightly (♩ = 108)") prints straight over the chord.
            //
            // THE PROFILE A MARK CLEARS IS THE SYMBOLS' EXTENT BOXES, NOT THEIR GLYPH
            // OUTLINES. A ChordName declares no `vertical-skylines' of its own
            // (scm/define-grobs.scm's ChordName entry has no such line), so it gets the
            // grob default — and the default is the box:
            // LILYPOND-REF: lily/grob.cc:81-85 — an unset vertical-skylines is filled with
            //   Grob::simple_vertical_skylines_from_extents_proc.
            // LILYPOND-REF: lily/stencil-integral.cc:769-792 maybe_pure_internal_simple_skylines_from_extents
            //   — one Box(X-extent,
            //   Y-extent), so the UP side is one flat roof at the symbol's ink top across
            //   its ink width. MEASURED (scratch/p271, 2.26.0, pinned faces): `A♯m' reads
            //   2.224872 FLAT across its whole span — the sharp's height over the `A' and
            //   the `m' too — where a glyph-outline profile would drop to 1.907290 over
            //   the letters. The flat roof is what makes both mark.over-chord.* books
            //   land at ink top + 0.460000 exactly under the pinned serif.
            //
            // The mark is then placed against that skyline the way the outside-staff pass
            // places every grob:
            // LILYPOND-REF: lily/axis-group-interface.cc:914-935 skyline_spacing — a
            //   ChordName declares no outside-staff-priority, so its skyline lands in
            //   inside_staff_skylines, which every outside-staff grob is measured against;
            // LILYPOND-REF: lily/axis-group-interface.cc:648-676 avoid_outside_staff_collisions
            //   (:663-664) — the push is
            //   `elt's DOWN skyline .distance(other's UP, horizon_padding) + padding',
            //   with Skyline::distance padding only the elt side (lily/skyline.cc:545-555)
            //   and Skyline::padded's flat + 45° flanks (:557-615 —
            //   VerticalSkyline.Padded is the line-for-line port).
            // The paddings are the mark family's own: outside-staff-padding is undeclared
            // on RehearsalMark/SectionLabel/MetronomeMark, so the default 0.46 applies
            // (OutsideStaffPadding); outside-staff-horizontal-padding is declared 0.2 on
            // each (OutsideStaffHorizontalPadding). ⚠️ CodaMark alone declares
            // outside-staff-padding 0.4 (scm/define-grobs.scm:1013); this arm deliberately
            // does not spell a per-type padding — no point observes a segno/coda over a
            // chord, and the split waits for one (HANDOFF §5.1's scope discipline).
            //
            // The MARK side of the distance is a flat foot at its own bottom over
            // MarkXExtent — the drawn mirror: Lily# BOXES its labels, and a box's DOWN
            // skyline is its flat bottom edge. (LilyPond's bare-text marks carry their
            // glyph outlines instead; the ledger's mark.over-chord.* residual is exactly
            // that drawn difference, the 0.400000 box term, and stays OPEN there.)
            //
            // ⚠️ PER MARK, AND IT WAS ONE SCALAR FOR THE WHOLE SCORE until session 243.
            // That could not show while only INLINE chords reached the loop -- an inline
            // chord sits over its own note, so the marks that overlapped one were the
            // marks beside it. With a leading chord ROW in the loop every system has a
            // symbol at every bar, and ONE mark that genuinely sits on one lifted EVERY
            // other mark in the book with it (MEASURED on book MKR: the section label
            // "B" really does overlap its bar's chord, and "A" four bars earlier rose
            // with it -- 7.805000 against LilyPond's 2.850000).
            //
            // The per-system band skyline; null when the system has no chord ink above
            // the anchor staff's top line. Cached — MarkCeilingUp is asked per mark.
            var chordBandUpBySystem = new Dictionary<int, VerticalSkyline?>();
            VerticalSkyline? ChordBandUp(int measureIndex)
            {
                if (!measureToSystemIdx.TryGetValue(measureIndex, out int sysIdx))
                    return null;
                if (chordBandUpBySystem.TryGetValue(sysIdx, out var cached))
                    return cached;
                VerticalSkyline? sky = null;
                foreach (var cn in chordNames)
                {
                    if (!SameSystem(cn.MeasureIndex, measureIndex))
                        continue;
                    // The chord's baseline IN THE MARK'S FRAME, whose 2.0 is the mark's
                    // ANCHOR STAFF's top line. ⚠️ ChordNameLayout.YUp is measured above
                    // the SYSTEM top, so `2.0 + cn.YUp` — what stood here — is that
                    // conversion only while the anchor staff LEADS the system.
                    double chordUp = 2.0 + (cn.YUp - AnchorStaffUp(cn.MeasureIndex));
                    // ...and only ink ABOVE that staff's top line shares the marks'
                    // band. That test used to be spelled `cn.YUp <= 0`, which says the
                    // same thing ONLY for a leading staff and otherwise discards a
                    // leading chord ROW wholesale — harmless while the mark floated
                    // above the whole band, and a collision the moment it stopped:
                    // samples/greensleeves.lys printed its tempo THROUGH the first
                    // chord symbol (session 243).
                    if (chordUp <= 2.0)
                        continue;
                    // ⚠️ cn.X IS THE SYMBOL'S LEFT EDGE, NOT ITS CENTRE — a chord name is
                    // drawn with TextAnchor.Start (SharedRenderer.DrawChordNames) — and
                    // the box is the symbol's INK, the same SymbolInkWidth/SymbolInk pair
                    // the reservation and the draw read (one house).
                    sky ??= new VerticalSkyline(VerticalDirection.Up);
                    var ink = ChordNameEngraver.SymbolInk(fonts, cn.ChordText);
                    sky.Merge(VerticalSkyline.FromBox(
                        cn.X, cn.X + ChordNameEngraver.SymbolInkWidth(fonts, cn.ChordText),
                        chordUp + ink.Bottom, chordUp + ink.Top,
                        VerticalDirection.Up));
                }
                chordBandUpBySystem[sysIdx] = sky;
                return sky;
            }

            // The Y-up floor for THIS mark's bottom (box bottom, or ink bottom for the
            // baseline-anchored tempo) — asked with its own X, not once for the whole
            // book. Negative infinity when nothing constrains it.
            double MarkCeilingUp(MusicMarkItem mark, double markX)
            {
                if (chordNames.IsDefaultOrEmpty) return double.NegativeInfinity;
                if (ChordBandUp(mark.MeasureIndex) is not { } bandUp || bandUp.IsEmpty)
                    return double.NegativeInfinity;
                var (mx0, mx1) = MarkXExtent(fonts, mark, markX);
                var foot = VerticalSkyline.FromBox(
                    mx0, mx1, 0.0, 0.0, VerticalDirection.Down);
                // LILYPOND-REF: lily/axis-group-interface.cc:663-664 avoid_outside_staff_collisions
                //   — distance of the mark's padded DOWN skyline to the chords' UP, plus
                //   outside-staff-padding.
                double dist = foot.Distance(bandUp, OutsideStaffHorizontalPadding);
                return double.IsNegativeInfinity(dist)
                    ? double.NegativeInfinity
                    : dist + OutsideStaffPadding;
            }

            // Above-staff estimate BEFORE the outside-staff pass: every mark rests at
            // its own base and marks sharing the anchor chain upward only among
            // THEMSELVES — the TEMPO does not join the chain. Its X is the break-aligned
            // meter (or the musical column), not the label's, so whether its ink and a
            // label's box actually meet is a pointwise question, and that question
            // belongs to the priority pass (tempo 1300 is placed first, the label 1450
            // clears it where — and only where — they overlap), which is exactly
            // LilyPond's shape. The old side-by-side "chart pair" device (label box with
            // the tempo re-anchored to its right on one shared line, re-aligned after
            // stacking) died here: LilyPond has no such construction, and keeping it
            // meant re-deriving by hand the clearances the stacker had already solved
            // (the label was pulled down onto the line-start clef that way).
            // ⚠️ THE GROUP KEY IS NOT THE COLUMN. Grouping by (measure, position, timing) was
            // a PROXY for "these marks share an X", and it held only while every mark of an
            // opening column was anchored alike. It stopped holding on 2026-08-18, when a
            // segno/coda that opens a line began break-aligning on the bar line drawn there
            // (CalculateXPosition) while a section label kept the left edge, as their
            // break-align-symbols say they should — so the two now genuinely stand apart and
            // stacking them made the label float: measured on the owner's book, the "E" box
            // sat 4.96 ss above its staff where an unstacked label sits at 2.28.
            // So the chain asks the X it is actually given. A mark stacks above the highest
            // ALREADY-PLACED mark of this group whose ink it overlaps, and starts at the base
            // when it overlaps none — which is what the pointwise outside-staff pass would do
            // and what the grouping comment above always meant.
            // LILYPOND-REF: lily/axis-group-interface.cc avoid_outside_staff_collisions —
            //   outside-staff grobs are skylined pointwise, so two that do not meet in X do
            //   not raise each other however close their moments are.
            var placedAbove = new List<(double X0, double X1, double TopYUp)>();
            double stackTopYUp = baseAboveYUp;
            bool chainStarted = false;
            for (int i = 0; i < aboveMarks.Count; i++)
            {
                var (mark, x, si) = aboveMarks[i];
                double halfExtent = GetMarkHalfExtent(mark.Type);
                var (chainX0, chainX1) = MarkXExtent(fonts, mark, x);
                // The highest already-placed neighbour whose ink meets this one's, or the
                // base when there is none. The reach is the mark family's own
                // outside-staff-horizontal-padding (see the constant), the same gap the
                // pointwise pass keeps.
                double restUnderUp = double.NegativeInfinity;
                foreach (var p in placedAbove)
                    if (chainX0 <= p.X1 + OutsideStaffHorizontalPadding
                        && chainX1 >= p.X0 - OutsideStaffHorizontalPadding
                        && p.TopYUp > restUnderUp)
                        restUnderUp = p.TopYUp;
                bool overlapsPlaced = !double.IsNegativeInfinity(restUnderUp);
                if (chainStarted && overlapsPlaced)
                    stackTopYUp = restUnderUp;
                else if (chainStarted)
                    chainStarted = false;   // nothing under it: this one opens its own chain

                // THIS mark's own chord ceiling — asked with its own X, not once for the
                // whole book (see MarkCeilingUp's remark).
                double markCeilingUp = MarkCeilingUp(mark, x);

                double yUp;
                if (mark.Type == MusicMarkType.Tempo)
                {
                    // The metronome mark RESTS on the staff by aligned_side — its supports
                    // are the staves themselves, so its quiet baseline is staff ink +
                    // padding 0.8 + its own ink bottom (ledger tempo.quiet.staff-to-
                    // baseline). Collisions with other outside-staff grobs are the
                    // priority-1300 pass's job (OutsideStaffStacker.PlaceMusicMarks),
                    // which only ever lifts it. Its ink is BASELINE-anchored, not a
                    // centered box, so the chord ceiling constrains its ink bottom
                    // (baseline + Ink.Bottom), not a half-extent.
                    var tInk = MetronomeMarkGeometry.Ink(fonts, mark.Text, mark.TempoText,
                        mark.TempoBeatUnit, mark.TempoDots, mark.SwingSubdivision);
                    yUp = MetronomeMarkGeometry.QuietBaselineAboveMiddle(tInk.Bottom);
                    if (!double.IsNegativeInfinity(markCeilingUp))
                        yUp = Math.Max(yUp, markCeilingUp - tInk.Bottom);
                }
                else if (!chainStarted)
                {
                    // A BOXED LABEL ON A STAFFLESS SHEET SITS ON THE ROW'S OWN LINE, not above
                    // it (see StafflessAnchorRefpointBelowTop for the decision and the LilyPond
                    // reading it departs from). The mark frame puts the anchor's band top at
                    // 2.0, so the anchor's baseline is `2.0 - refpoint below that top', and the
                    // box centre stands its own baseline offset above that.
                    double? rowRefpoint = IsBoxedLabel(mark.Type)
                        ? StafflessAnchorRefpointBelowTop(mark.MeasureIndex)
                        : null;
                    if (rowRefpoint is { } below)
                    {
                        // ⚠️ AND THE CHORD CEILING IS NOT APPLIED HERE, deliberately: it is the
                        // device that lifts a mark OFF a symbol it overlaps, and on this sheet
                        // the two are meant to share a line. The overlap is resolved in X
                        // instead (ChordNameEngraver reserves the box), so lifting as well
                        // would undo the placement the decision asks for.
                        yUp = 2.0 - below + LabelBaselineBelowCentre(mark.Type);
                    }
                    else
                    {
                        yUp = baseAboveYUp + Padding;
                        if (!double.IsNegativeInfinity(markCeilingUp))
                            yUp = Math.Max(yUp, markCeilingUp + halfExtent); // box bottom clears the chord
                    }
                    stackTopYUp = yUp + halfExtent;
                    chainStarted = true;
                }
                else
                {
                    // Subsequent marks: stack above the neighbour found above
                    yUp = stackTopYUp + StackGap + halfExtent;
                    stackTopYUp = yUp + halfExtent;
                }

                placedAbove.Add((chainX0, chainX1, yUp + halfExtent));

                layouts.Add(new MusicMarkLayout(
                    mark.MeasureIndex, x, yUp, mark.Type, mark.Text,
                    mark.IsSymbol, mark.SourcePosition, si, mark.SwingSubdivision,
                    mark.TempoText, mark.TempoBeatUnit, mark.TempoDots));
            }

            // Stack below-staff marks (lower priority = closer to staff).
            // Base = the system's LAST staff bottom + 1.5 (equals the old
            // 5.5 constant for a single 4sp staff — multi-staff changes only).
            // Below-staff baseline reflected to the mark frame (Y-up above the top-staff
            // middle). BelowMarkBaseline stays DEVICE (PedalEngraver consumes it), so
            // reflect its result to Y-up here (2 − device).
            double belowBaseUp = 2.0 - (BelowMarkBaseline(4.0) - Padding);
            if (belowMarks.Count > 0
                && measureToSystemBottom.TryGetValue(belowMarks[0].Mark.MeasureIndex, out double sysBottom))
            {
                belowBaseUp = 2.0 - (BelowMarkBaseline(sysBottom) - Padding);
            }
            // A jump/other below-staff mark must drop past the lyric line it shares
            // the band with (LyricClearance). Pedal text is EXEMPT — classic notation
            // keeps "Ped."/"*" on its staff-relative baseline, never pushed under the
            // words — so the floor lifts only the non-pedal stacking base, NOT
            // `belowBaseUp`, which the pedal branch below reads directly.
            double stackBaseUp = belowBaseUp;
            if (belowMarks.Count > 0
                && measureToSystemIdx.TryGetValue(belowMarks[0].Mark.MeasureIndex, out int belowSys)
                && systemLyricBottomUp.TryGetValue(belowSys, out double lyricYUp))
            {
                stackBaseUp = Math.Min(belowBaseUp, lyricYUp - LyricClearance);
            }
            // Pedal CHANGES put the previous release "*" and the next
            // "Ped." in the same group; classic notation writes them SIDE BY
            // SIDE on the one pedal baseline ("* Ped."), never stacked.
            // Releases sharing a group with an on-mark shift left of it.
            bool IsPedalRelease(MusicMarkType t) =>
                t is MusicMarkType.SustainOff or MusicMarkType.SostenutoOff
                  or MusicMarkType.UnaCordaOff;
            bool IsPedal(MusicMarkType t) =>
                t is MusicMarkType.SustainOn or MusicMarkType.SostenutoOn
                  or MusicMarkType.UnaCordaOn || IsPedalRelease(t);
            // ⚠️ A PEDAL FAMILY, NOT "PEDALS". Sustain, sostenuto and una corda are three
            // SEPARATE grobs in LilyPond — a PianoPedalLineSpanner each, all three declared
            // (outside-staff-priority . 1000) — so the outside-staff pass stacks them
            // instead of letting them share a baseline. Only a release and the engage that
            // follows it belong on one line, and that is the "* Ped." case below.
            // LILYPOND-REF: scm/define-grobs.scm:3211-3216 SostenutoPedalLineSpanner's outside-staff-priority
            // LILYPOND-REF: scm/define-grobs.scm:3593-3598 SustainPedalLineSpanner's outside-staff-priority
            // LILYPOND-REF: scm/define-grobs.scm:4169-4174 UnaCordaPedalLineSpanner's outside-staff-priority
            //   — all three declare 1000, so priority does not order them; being three grobs does.
            // ⚠️ MEASURED in LilyPond 2.26.0 rather than assumed, because equal priority
            //   does not say which lands nearer the staff. ALL THREE ARE NOW MEASURED, on one
            //   book that strikes them together — audit/lp-geometry/probes/pedal-three.ly,
            //   distance from the staff's bottom line to each family's row:
            //     una corda  2.777500      sostenuto  4.738700      sustain  7.181300
            //   ⇒ UNA CORDA IS NEAREST THE STAFF and sustain is outermost.
            // ⚠️ THE GUESS THIS REPLACES HAD IT AT THE OTHER END. Until 2026-08-18 una corda
            //   was ranked outermost "which is a guess and is marked as one" (it was), so on a
            //   three-pedal book every family sat one row wrong.
            // ⚠️ THE INSTRUMENT REPRODUCED A NUMBER IT DID NOT KNOW: sustain − sostenuto comes
            //   out 2.442600 against the 2.443 session 204 measured from the sustain/sostenuto
            //   PAIR alone, which is what says this reading is of the engine and not of the
            //   probe. ⚠️ And the sustain row is found as GLYPHS, not text: LilyPond draws
            //   "Ped." with Emmentaler (lily/sustain-pedal.cc), so a probe scanning <tspan>
            //   sees the other two and silently misses this one (HANDOFF §5.3).
            // ⚠️ WHAT IS NOT PORTED: the STEPS between rows. LilyPond's are 1.961 then 2.443
            //   — each row's own ink — where Lily# uses one StackGap for both (2.46 measured).
            //   Only the ORDER is fixed here; the step model is a separate quantity and has no
            //   ledger point yet.
            // The row each pedal family occupies in this group, stacked outward.
            var pedalRowYUp = new Dictionary<int, double>();
            {
                double rowYUp = belowBaseUp - Padding;
                double prevHalf = 0;
                bool firstRow = true;
                foreach (var rank in belowMarks
                    .Where(e => IsPedal(e.Mark.Type)
                                && (keepMarkText == null || keepMarkText(e.Mark)))
                    .Select(e => PedalFamilyRank(e.Mark.Type))
                    .Distinct()
                    .OrderBy(r => r))
                {
                    double half = belowMarks
                        .Where(e => IsPedal(e.Mark.Type) && PedalFamilyRank(e.Mark.Type) == rank)
                        .Max(e => GetMarkHalfExtent(e.Mark.Type));
                    if (!firstRow)
                        rowYUp -= prevHalf + StackGap + half;
                    pedalRowYUp[rank] = rowYUp;
                    prevHalf = half;
                    firstRow = false;
                }
            }

            // The side-by-side "* Ped." shift is a WITHIN-FAMILY affair: a sostenuto release
            // beside a sustain engage is two rows, not two words on one line.
            bool GroupHasPedalChange(MusicMarkType t) =>
                belowMarks.Any(e => IsPedalRelease(e.Mark.Type)
                                    && PedalFamilyRank(e.Mark.Type) == PedalFamilyRank(t))
                && belowMarks.Any(e => IsPedal(e.Mark.Type) && !IsPedalRelease(e.Mark.Type)
                                       && PedalFamilyRank(e.Mark.Type) == PedalFamilyRank(t));

            double stackBottomYUp = belowBaseUp;
            bool firstStacked = true;
            for (int i = 0; i < belowMarks.Count; i++)
            {
                var (mark, x, si) = belowMarks[i];
                // A bracket/mixed pedal style draws the "Ped." / "*" as a bracket
                // instead; skip the text layout (its SourceIndex si is already fixed).
                if (keepMarkText != null && !keepMarkText(mark))
                    continue;
                double halfExtent = GetMarkHalfExtent(mark.Type);

                double yUp;
                bool solvedPedalRow = false;
                if (IsPedal(mark.Type))
                {
                    // The row the staff's own down profile was SOLVED with, when the
                    // skyline-time pass ran for this staff (text style, full size): the
                    // same Y the lyric floor and the staff below already cleared. Y-up
                    // about the mark's OWN staff middle — the layout carries StaffIndex
                    // so the draw and the reservations read the right frame.
                    if (solvedPedalRowUp != null
                        && measureToSystemIdx.TryGetValue(mark.MeasureIndex, out int pedalSys)
                        && solvedPedalRowUp(mark.StaffIndex, pedalSys,
                               mark.SourcePosition) is { } solvedRow)
                    {
                        yUp = solvedRow;
                        solvedPedalRow = true;
                    }
                    // One baseline per pedal FAMILY (see PedalFamilyRank); the innermost is
                    // the plain pedal baseline, Padding below it = −Padding Y-up.
                    else
                    yUp = pedalRowYUp.TryGetValue(PedalFamilyRank(mark.Type), out double rowYUp)
                        ? rowYUp
                        : belowBaseUp - Padding;
                    if (GroupHasPedalChange(mark.Type) && IsPedalRelease(mark.Type))
                    {
                        // "*" just left of the new "Ped." — both centered
                        // texts, so clear half of each measured width + gap.
                        // The word that follows is the ENGAGE mark of this release's OWN
                        // family, FOUND in the group rather than spelled. It was spelled
                        // "Ped." here whatever the family until 2026-08-18 — so a sostenuto
                        // change cleared the sustain word's width — and the glyph port made
                        // that wrong in KIND rather than in degree: "Sost. Ped." is text and
                        // "Ped." is now a run of music glyphs, so the wrong string had also
                        // become the wrong mechanism. GroupHasPedalChange has already
                        // established that this engage exists, which is what makes First safe.
                        // ⚠️ Both halves go through PlainMarkWidth, not TextFontMetrics: only
                        // that home knows which of the two a given pedal's word is.
                        var follower = belowMarks.First(
                            e => IsPedal(e.Mark.Type) && !IsPedalRelease(e.Mark.Type)
                                 && PedalFamilyRank(e.Mark.Type) == PedalFamilyRank(mark.Type));
                        double pedHalf =
                            PlainMarkWidth(fonts, follower.Mark.Type, follower.Mark.Text) / 2;
                        double starHalf =
                            PlainMarkWidth(fonts, mark.Type, mark.Text) / 2;
                        x -= pedHalf + starHalf + 0.4;
                    }
                }
                else if (firstStacked)
                {
                    yUp = stackBaseUp - Padding;
                    stackBottomYUp = yUp - halfExtent;
                    firstStacked = false;
                }
                else
                {
                    yUp = stackBottomYUp - StackGap - halfExtent;
                    stackBottomYUp = yUp - halfExtent;
                }

                // Jump-from instructions (D.S./D.C.) hang a little lower than the
                // pedal baseline so they clear low notes under the staff.
                if (IsJumpInstruction(mark.Type))
                {
                    yUp -= JumpInstructionDrop;
                    stackBottomYUp = Math.Min(stackBottomYUp, yUp - halfExtent);
                }

                // Below-staff text must clear the LYRIC lines: lyrics hang under
                // the staff before any below-mark, and "D.S. al Coda" printed
                // straight through the words. Drop the mark under the deepest
                // syllable whose ink overlaps it horizontally (0.9 = descent of
                // the 3.2 ss lyric face, as in the spacing extents).
                if (!lyrics.IsDefaultOrEmpty && !IsPedal(mark.Type))
                {
                    var (mx0, mx1) = MarkXExtent(fonts, mark, x);
                    foreach (var ly in lyrics)
                    {
                        if (!SameSystem(ly.Item.MeasureIndex, mark.MeasureIndex))
                            continue;
                        double lyHalf = ly.Width / 2 + 0.3;
                        if (mx1 < ly.X - lyHalf || mx0 > ly.X + lyHalf)
                            continue;
                        // ly.YUp is Y-up from the system top; its mark-frame Y-up is
                        // 2 + ly.YUp, and the lyric BOTTOM hangs 0.9 below that baseline.
                        double lyricBottomUp = (2.0 + ly.YUp) - 0.9;
                        if (yUp + halfExtent > lyricBottomUp - OutsideStaffPadding)
                            yUp = lyricBottomUp - OutsideStaffPadding - halfExtent;
                    }
                    stackBottomYUp = Math.Min(stackBottomYUp, yUp - halfExtent);
                }

                layouts.Add(new MusicMarkLayout(
                    mark.MeasureIndex, x, yUp, mark.Type, mark.Text,
                    mark.IsSymbol, mark.SourcePosition, si, mark.SwingSubdivision,
                    mark.TempoText, mark.TempoBeatUnit, mark.TempoDots,
                    // A solved pedal row's yUp is about ITS OWN staff's middle; the
                    // legacy stack stays in the top-staff frame (StaffIndex −1).
                    StaffIndex: solvedPedalRow ? mark.StaffIndex : -1));
            }
        }

        return layouts.ToImmutable();
    }

    // (CoPlaceTempoWithLabels — the "[Chorus] ♩ = 132" chart pair that re-anchored a
    // tempo to its section label's right on one shared line — was REMOVED with the
    // tempo port: LilyPond has no such device. The label and the metronome mark each
    // break-align to their own anchor and the outside-staff pass stacks them by
    // priority (1300 first, 1450 clears it pointwise where their inks meet), which is
    // both the letter and what keeps the label's clef clearance intact without
    // re-deriving it by hand.)

    /// <summary>
    /// Pairs each boundary "To Coda" with the section label it shares a barline with
    /// and moves the sign beside it — a fixed gap to the label's LEFT, baseline tucked
    /// to the label box's bottom edge. The sign keeps its own measure: it stays
    /// logically at the end of the previous section (left of the barline), the label
    /// at the start of the next; the drawn column and the outside-staff LINE are
    /// shared. Matched by X proximity within one system, so a label across a line
    /// break is never matched.
    /// </summary>
    /// <remarks>
    /// Called by the outside-staff pass (<c>OutsideStaffStacker.PlaceMusicMarks</c>)
    /// BEFORE any mark is priced, and the pair is then placed as ONE union extent and
    /// moved together, deliberately. The post-stack device this replaced (2026-08-18 —
    /// session 227) had two structural faults its own comments named: ⑴ it moved the
    /// sign to a new X after the tracker had been asked about the old one, so nothing
    /// ever priced what stood under the DRAWN column; ⑵ it could not tell a label
    /// raised BY THE SIGN (which must drop back when the sign steps aside) from a
    /// label raised by an obstacle of its own (which must not — its "mirror case",
    /// left uncovered). Both vanish under one-union placement: the members never
    /// price each other (their inks overlap by design — the 4.0 gap is less than the
    /// two half-widths — so stacking them separately would make each a phantom
    /// obstacle for the other), and whatever really stands under EITHER drawn column
    /// raises the pair as a whole.
    /// LILYPOND-REF: scm/define-grobs.scm — VoltaBracketSpanner outside-staff-priority
    /// 600 and every member of the mark family 1350-1500 (JumpScript 1350, CodaMark/
    /// SegnoMark 1400, SectionLabel 1450, RehearsalMark 1500): a mark is ALWAYS outside
    /// the bracket, and this device must not be the thing that puts one in. (The
    /// side-by-side shared line itself is LILYSHARP-OWN: LilyPond prints a boundary
    /// "To Coda" break-visible at the previous line's END, and the owner decided the
    /// sign stays on the new line at the barline instead — HANDOFF §3.)
    /// </remarks>
    /// <param name="sameSystem">
    /// Whether two measure indices lie on the same system. The stacker's core pass
    /// sees every system's marks in one array, and a sign at a line's end must not
    /// pair with the label opening the next line — absolute X keeps adjacent measures
    /// close across the break, so the X window alone cannot tell them apart.
    /// </param>
    /// <param name="pairs">The (sign, label) index pairs this pass matched.</param>
    public static ImmutableArray<MusicMarkLayout> CoPlaceToCodaWithLabels(
        ImmutableArray<MusicMarkLayout> marks,
        Func<int, int, bool> sameSystem,
        out ImmutableArray<(int Sign, int Label)> pairs)
    {
        pairs = ImmutableArray<(int Sign, int Label)>.Empty;
        if (marks.IsDefaultOrEmpty || !marks.Any(m => m.MarkType == MusicMarkType.ToCoda))
            return marks;

        var labelIdx = new List<int>();
        for (int i = 0; i < marks.Length; i++)
            if (marks[i].MarkType is MusicMarkType.SectionLabel or MusicMarkType.Rehearsal)
                labelIdx.Add(i);
        if (labelIdx.Count == 0)
            return marks;

        var result = marks.ToBuilder();
        var found = ImmutableArray.CreateBuilder<(int Sign, int Label)>();
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].MarkType != MusicMarkType.ToCoda)
                continue;
            var tc = result[i];
            int bestJ = -1;
            double bestDx = double.MaxValue;
            foreach (int j in labelIdx)
            {
                double dx = result[j].X - tc.X;
                if (dx >= -1.0 && dx < 5.0 && dx < bestDx
                    && sameSystem(result[j].MeasureIndex, tc.MeasureIndex))
                { bestDx = dx; bestJ = j; }
            }
            if (bestJ >= 0)
            {
                // Sit the sign just to the label's left, LOW enough that its baseline
                // meets the label box's bottom edge (the box extends half its height
                // below the shared centre line). Both are at their un-stacked default
                // lines here, so this is the ordinary-book geometry already; the
                // union placement then raises the pair together over whatever really
                // stands under it, keeping this relative arrangement.
                var lab = result[bestJ];
                result[i] = tc with
                {
                    X = lab.X - ToCodaLabelGap,
                    YUp = lab.YUp - LabelBoxHalf(lab.MarkType),
                };
                found.Add((i, bestJ));
            }
        }
        pairs = found.ToImmutable();
        return result.ToImmutable();
    }

    /// <summary>
    /// Half the drawn height of a boxed label (box = font size + 2 × 0.2 pad): the
    /// co-placement sits the sign's baseline this far below the label's centre line,
    /// i.e. at the label box's bottom edge.
    /// </summary>
    private static double LabelBoxHalf(MusicMarkType labelType)
    {
        double labelFs = labelType == MusicMarkType.Rehearsal ? 4.0 * 0.6 : 4.0 * 0.55;
        return (labelFs + 2 * 0.2) / 2;
    }

    /// <summary>
    /// The two advances of a drawn boundary "To Coda" — the "To " prefix in the
    /// navigation face and the coda GLYPH (not the word "Coda") — the composition
    /// <c>SharedRenderer</c> draws, centred as a group on the mark's anchor. ONE
    /// HOME, deliberately: the union placement (<c>OutsideStaffStacker.
    /// PlaceMusicMarks</c>) prices the co-placed sign by this so the reservation
    /// cannot outreach the ink — pricing it by <c>Advance("To Coda")</c> reached
    /// ~1 staff space further left than anything drawn and made the pair clear a
    /// neighbouring label the ink never touches (session 227, measured on
    /// scratch/p206/v4.lys: the pair floated 3 ss over the line every other label
    /// shared).
    /// </summary>
    internal static (double TextW, double GlyphW) ToCodaStencilWidths(ScoreTextMetrics fonts)
    {
        double textW = fonts.Advance("To ", PlainTextFontSize, TextRole.Navigation,
            TextStyleOf(MusicMarkType.ToCoda));
        double glyphW = 4.0 * 0.8 * 0.42; // approx advance of scripts.coda at the draw's size
        return (textW, glyphW);
    }

    // Centre-to-centre gap between a boundary "To Coda" and the section label it
    // shares the rehearsal line with, so the sign sits clear to the label's left.
    private const double ToCodaLabelGap = 4.0;

    /// <summary>
    /// Builds the full, ordered mark list (explicit marks + section labels +
    /// initial tempo) that <see cref="Calculate"/> lays out. Public and pure so
    /// the renderer can reconstruct the SAME list from the live score and resolve
    /// each layout's data-pos by its <c>SourceIndex</c> (F3/B whole-layout reuse).
    /// The merge order is content-invariant, so the index is stable across edits
    /// that don't change content.
    /// </summary>
    public static ImmutableArray<MusicMarkItem> BuildAllMarks(
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<Measure> measures,
        int? tempo,
        int swingSubdivision = 0,
        string? tempoText = null,
        int tempoBeatUnit = 4,
        int tempoDots = 0,
        int tempoPosition = 0)
    {
        var allMarks = MergeSectionLabels(musicMarks, measures);
        return MergeTempoMark(allMarks, tempo, swingSubdivision, tempoText, tempoBeatUnit,
            tempoDots, tempoPosition);
    }

    /// <summary>
    /// Merges section labels from measures into the music marks list.
    /// Section labels become MusicMarkType.SectionLabel entries.
    /// </summary>
    private static ImmutableArray<MusicMarkItem> MergeSectionLabels(
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<Measure> measures)
    {
        if (measures.IsDefaultOrEmpty)
            return musicMarks.IsDefaultOrEmpty ? ImmutableArray<MusicMarkItem>.Empty : musicMarks;

        // Collect a rehearsal-box mark for every measure that carries a section
        // label. Label VISIBILITY is the author's call, not the engraver's: a
        // section shows its name by being referenced by name (`form main { Body }`)
        // and hides it with the silent reference (`~Body`). So every non-null
        // SectionLabel engraves — no auto-suppression of "single distinct section"
        // or "repeated section" boxes. That heuristic fought the author's explicit
        // `~` control and silently hid deliberately named single sections.
        var sectionLabels = new List<MusicMarkItem>();
        for (int i = 0; i < measures.Length; i++)
        {
            var measure = measures[i];
            if (measure.SectionLabel == null)
                continue;
            // Prefer the `section X` declaration offset so a click jumps there;
            // fall back to the measure's music start when it wasn't threaded.
            int pos = measure.SectionLabelPosition > 0
                ? measure.SectionLabelPosition
                : measure.SourceStart;
            sectionLabels.Add(new MusicMarkItem(
                MusicMarkType.SectionLabel, measure.SectionLabel, i, pos));
        }

        if (sectionLabels.Count == 0)
            return musicMarks.IsDefaultOrEmpty ? ImmutableArray<MusicMarkItem>.Empty : musicMarks;

        // Merge: existing marks + section labels
        var builder = ImmutableArray.CreateBuilder<MusicMarkItem>();
        if (!musicMarks.IsDefaultOrEmpty)
            builder.AddRange(musicMarks);
        builder.AddRange(sectionLabels);
        return builder.ToImmutable();
    }

    /// <summary>
    /// Adds a tempo marking to the mark list if the score has a tempo.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:2346 MetronomeMark outside-staff-priority = 1300
    /// </remarks>
    private static ImmutableArray<MusicMarkItem> MergeTempoMark(
        ImmutableArray<MusicMarkItem> marks, int? tempo, int swingSubdivision = 0,
        string? tempoText = null, int tempoBeatUnit = 4, int tempoDots = 0,
        int tempoPosition = 0)
    {
        // A textual marking without a BPM ("tempo \"Grave\"") still prints.
        if (tempo == null && tempoText == null)
            return marks;

        // The mark is SYNTHESISED from the score's metadata rather than walked off a
        // syntax node, so its source offset has to be carried in — it used to be 0,
        // which is a real offset (the file's first character), so clicking the opening
        // metronome mark in the preview jumped to the top of the file.
        var tempoMark = new MusicMarkItem(
            MusicMarkType.Tempo, tempo?.ToString() ?? "", 0, tempoPosition)
        {
            SwingSubdivision = swingSubdivision,
            TempoText = tempoText,
            TempoBeatUnit = tempoBeatUnit,
            TempoDots = tempoDots,
        };

        var builder = ImmutableArray.CreateBuilder<MusicMarkItem>();
        if (!marks.IsDefaultOrEmpty)
        {
            // The INITIAL tempo is drawn from Score.Tempo (the mark added below).
            // A top-level or part-header `tempo` ALSO gets injected into the music
            // stream as a metronome mark at the opening moment (MeasureCollector),
            // so without this filter the same starting tempo prints two or three
            // times — and the stream copies, anchored to a note column rather than
            // the line start, float at the wrong height. Drop those redundant
            // opening-moment stream tempos (MeasureIndex 0, zero elapsed time,
            // AnchorItemIndex >= 0 = stream-sourced). A genuine mid-piece change
            // (AnchorTiming numerator != 0, or a later measure) is kept; an
            // in-music tempo with no Score.Tempo never reaches this branch.
            foreach (var m in marks)
            {
                bool redundantOpeningTempo = m.Type == MusicMarkType.Tempo
                    && m.MeasureIndex == 0
                    && m.AnchorItemIndex >= 0
                    && m.AnchorTiming.Numerator == 0;
                if (!redundantOpeningTempo)
                    builder.Add(m);
            }
        }
        builder.Add(tempoMark);
        return builder.ToImmutable();
    }

    /// <summary>
    /// Gets the outside-staff-priority for a mark type.
    /// Lower values are placed closer to the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: define-grobs.scm
    /// - SectionLabel: outside-staff-priority = 1450 (:3053)
    /// - RehearsalMark: outside-staff-priority = 1500 (:2888)
    /// - SegnoMark/CodaMark: outside-staff-priority = 1400 (:3095 / :1014, "inside RehearsalMark")
    /// </remarks>
    private static int GetOutsideStaffPriority(MusicMarkType type) => type switch
    {
        // LILYPOND-REF: scm/define-grobs.scm:2346 MetronomeMark outside-staff-priority = 1300
        MusicMarkType.Tempo => 1300,
        // Segno/Coda sit CLOSEST to the staff (below SectionLabel 1450 < RehearsalMark 1500).
        MusicMarkType.Segno => 1400,
        MusicMarkType.Coda => 1400,
        MusicMarkType.SectionLabel => 1450,
        MusicMarkType.Rehearsal => 1500,
        _ => 1500
    };

    /// <summary>
    /// Gets the approximate half-height of a mark's visual extent in staff spaces.
    /// Used for collision avoidance stacking between marks.
    /// </summary>
    /// <remarks>
    /// These values match the rendering sizes in SvgRenderer:
    /// - Boxed marks (Rehearsal/SectionLabel): (fontSize + boxPadding*2) / 2
    ///   where boxPadding = 0.2 (LILYPOND-REF: define-markup-commands.scm)
    /// - Symbol marks (Segno/Coda): symbol glyph height / 2
    /// - Text marks (D.S./Fine/etc.): fontSize / 2
    /// </remarks>
    /// <summary>
    /// The mark's approximate horizontal ink span. Tempo (left-anchored)
    /// extends right from its X; End-positioned jump text extends left;
    /// boxed labels and symbols are centred. Widths use the same faces the
    /// renderer draws with.
    /// </summary>
    // internal, not private: no book in the tracked corpus reaches the plain-text arm — a
    // 100-staff-space poison in it moved 0 of 567 (2026-08-18) because nothing pairs a
    // navigation/pedal mark with the inline chord symbols or lyrics the two overlap tests
    // compare against. The observer therefore has to call it (MusicMarkSpanTests).
    internal static (double x0, double x1) MarkXExtent(ScoreTextMetrics fonts,
        MusicMarkItem mark, double x)
        => MarkXExtent(fonts, mark.Type, mark.Text, mark.TempoText, mark.TempoBeatUnit,
            mark.TempoDots, mark.SwingSubdivision, x);

    /// <summary>
    /// The same extent read off a PLACED mark rather than a collected one.
    /// </summary>
    /// <remarks>
    /// Two adapters onto one body, not two spellings. <c>LayoutEngine</c>'s inter-system
    /// silhouette walks <see cref="MusicMarkLayout"/> while the two overlap tests walk
    /// <see cref="MusicMarkItem"/>, and until 2026-08-18 that type difference was the whole
    /// reason the silhouette priced its own box — which is how three of its four arms were
    /// still spelling the neighbouring case's numbers after this method had been made the
    /// one home (§5.2.1⑤: the second spelling of a quantity is where the ports stop
    /// arriving). ⚠️ A record type is not a reason for a second model; an adapter is what a
    /// record type deserves.
    /// </remarks>
    internal static (double x0, double x1) MarkXExtent(ScoreTextMetrics fonts,
        MusicMarkLayout mark, double x)
        => MarkXExtent(fonts, mark.MarkType, mark.Text, mark.TempoText, mark.TempoBeatUnit,
            mark.TempoDots, mark.SwingSubdivision, x);

    private static (double x0, double x1) MarkXExtent(ScoreTextMetrics fonts,
        MusicMarkType type, string text, string? tempoText, int tempoBeatUnit, int tempoDots,
        int swingSubdivision, double x)
    {
        switch (type)
        {
            case MusicMarkType.Tempo:
            {
                // Left-anchored, priced by the ONE geometry home the draw uses.
                double w = MetronomeMarkGeometry.Ink(fonts, text, tempoText,
                    tempoBeatUnit, tempoDots, swingSubdivision).Width;
                return (x, x + w);
            }
            case MusicMarkType.Rehearsal:
            case MusicMarkType.SectionLabel:
            {
                double fs = type == MusicMarkType.Rehearsal ? 2.4 : 2.2;
                double half =
                    fonts.Advance(text, fs, TextRole.Mark, FontStyle.Bold) / 2 + LabelBoxPadding;
                return (x - half, x + half);
            }
            case MusicMarkType.Segno:
            case MusicMarkType.Coda:
                return (x - 1.2, x + 1.2);
            default:
            {
                // Plain text marks: the string's own advance at the size and style the draw
                // uses — see PlainTextFontSize/TextStyleOf for what this arm used to say and
                // what it cost. The SUSTAIN pedal is not one of them: its word is a run of
                // music glyphs (see SustainPedalStencil), and pricing it as text is what
                // ledger point mark.pedal.width.sustain measured at 1.478779528 too wide.
                // Both overlap tests that read this extent (against inline chord symbols,
                // against lyrics) ask whether two grobs share horizontal ink, which is a
                // question about the DRAWN box either way.
                double w = PlainMarkWidth(fonts, type, text);
                return MusicMarkItem.PositionOf(type) == MusicMarkPosition.End
                    ? (x - w, x)
                    : (x - w / 2, x + w / 2);
            }
        }
    }

    /// <summary>The two marks Lily# draws as a framed box around bold text.</summary>
    internal static bool IsBoxedLabel(MusicMarkType type)
        => type is MusicMarkType.Rehearsal or MusicMarkType.SectionLabel;

    /// <summary>
    /// Where each boxed label's frame stands horizontally, per measure — the X half of
    /// putting a label on a staffless sheet's chord line.
    /// </summary>
    /// <remarks>
    /// ★ THE SAME TWO CALLS THE FULL LAYOUT MAKES, and that is the point: the anchor
    /// (<see cref="CalculateXPosition"/>) and the box
    /// (<c>MarkXExtent</c>) are asked here exactly as they are asked there, so the interval a
    /// chord symbol is spaced against cannot drift from the one the label is drawn in. A
    /// second X model for the same box is the shape HANDOFF 5.2.1② names.
    /// <para>
    /// ⚠️ WHY IT IS A SEPARATE PASS AT ALL. The chord layouts are an INPUT to
    /// <see cref="Calculate"/> (they feed the chord ceiling), so the marks cannot be laid out
    /// first; but a mark's X depends only on its measure's break-align column and never on a
    /// chord, so asking for the X alone is well defined before the chords exist. The two
    /// passes agree by construction because they call the same function on the same
    /// arguments.
    /// </para>
    /// <para>
    /// ⚠️ ABOVE-STAFF LABELS ONLY. A below-staff mark is not one of these types, and an
    /// <c>End</c>-positioned one (fine, D.S.) is not a boxed label either.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<(int MeasureIndex, double X0, double X1)> BoxedLabelXWindows(
        ScoreTextMetrics fonts,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<Measure> measures,
        ImmutableArray<ChordNameItem> chordNames,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        Func<int, double>? prefixTimeSignatureX = null,
        Func<int, double>? lineStartBarlineX = null,
        Func<int, double>? prefixMarkAnchorX = null)
    {
        var windows = new List<(int, double, double)>();
        if (measureLayouts.IsDefaultOrEmpty)
            return windows;
        // ⚠️ THROUGH BuildAllMarks, NOT THE RAW LIST. A `form' section name is not a
        // MusicMarkItem in the score — MergeSectionLabels manufactures it from the MEASURES —
        // so reading musicMarks alone finds no label at all, which is exactly how the first
        // draft of this reservation did nothing. The tempo arguments are neutral on purpose:
        // MergeTempoMark only ever adds a Tempo, and a Tempo is not a boxed label, so the one
        // mark this list would gain is filtered out on the next line either way.
        var marks = BuildAllMarks(musicMarks, measures, tempo: null);
        // Only the systems that HAVE NO STAFF: everywhere else the label keeps its own band
        // above the row and nothing has to move for it. Asked through the same property the
        // placement asks (StaffAffinity.TopSpaceableStaff), so the two arms cannot disagree
        // about which sheets the convention covers.
        // Only the systems whose ANCHOR ROW CARRIES CHORD-ROW SYMBOLS — the same single
        // condition the placement applies, spelled the same way (see
        // StafflessAnchorRefpointBelowTop for why it says "staffless" too). The two arms have
        // to cover the same books or one of them acts alone, which is HANDOFF 5.3's shape.
        var stafflessMeasures = new HashSet<int>();
        foreach (var system in systems)
        {
            int anchor = LayoutUtilities.TopScoreGrobStaff(system);
            if (chordNames.IsDefaultOrEmpty
                || !chordNames.Any(c => c.IsChordRow && c.StaffIndex == anchor))
                continue;
            foreach (var m in system.Measures)
                stafflessMeasures.Add(m.MeasureIndex);
        }
        if (stafflessMeasures.Count == 0)
            return windows;
        foreach (var mark in marks)
        {
            if (!IsBoxedLabel(mark.Type) || mark.Vertical != MusicMarkVertical.Above)
                continue;
            if (mark.MeasureIndex < 0 || mark.MeasureIndex >= measureLayouts.Length)
                continue;
            if (!stafflessMeasures.Contains(mark.MeasureIndex))
                continue;
            double x = CalculateXPosition(
                fonts, mark, measureLayouts[mark.MeasureIndex], systems,
                prefixTimeSignatureX, lineStartBarlineX, prefixMarkAnchorX, measures);
            var (x0, x1) = MarkXExtent(fonts, mark, x);
            windows.Add((mark.MeasureIndex, x0, x1));
        }
        return windows;
    }

    /// <summary>
    /// The padding a boxed label's frame keeps around its text, per side.
    /// </summary>
    /// <remarks>
    /// ⚠️ internal because <c>SharedRenderer.DrawSingleMusicMark</c> DRAWS the frame with it
    /// while <see cref="GetMarkHalfExtent"/> and <c>MarkXExtent</c> RESERVE for it. Three
    /// spellings of one number is the shape HANDOFF 5.2.1② names; this is the home.
    /// </remarks>
    internal const double LabelBoxPadding = 0.2;

    /// <summary>
    /// How far BELOW a boxed label's centre — which is what <c>MusicMarkLayout.YUp</c>
    /// carries for one — its text baseline is drawn.
    /// </summary>
    /// <remarks>
    /// Derived from the half extent rather than restated: the box is
    /// <c>fontSize + 2 × padding</c> tall about its centre and the text sits
    /// <c>fontSize / 2 − padding</c> below it, so the offset is
    /// <c>halfExtent − 2 × padding</c> and the font size never appears twice.
    /// <c>SharedRenderer.DrawSingleMusicMark</c> draws at exactly this offset.
    /// </remarks>
    internal static double LabelBaselineBelowCentre(MusicMarkType type)
        => GetMarkHalfExtent(type) - 2 * LabelBoxPadding;

    private static double GetMarkHalfExtent(MusicMarkType type) => type switch
    {
        // Tempo is NOT priced here any more: its ink is baseline-anchored and asymmetric
        // (note top ~3.16, digit overshoot below), read from MetronomeMarkGeometry by
        // every consumer. The arm remains only for the generic fallback shape.
        MusicMarkType.Tempo => 1.8,
        MusicMarkType.Rehearsal => 1.4,       // (FontSize*0.6 + 0.2*2) / 2 = (2.4+0.4)/2
        MusicMarkType.SectionLabel => 1.3,    // (FontSize*0.55 + 0.2*2) / 2 = (2.2+0.4)/2
        MusicMarkType.Segno or MusicMarkType.Coda => 2.0,
        _ => 1.0
    };

    /// <summary>
    /// Calculates X position for a mark.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mark-engraver.cc:75-80 break-align-symbol
    /// - Beginning marks (segno, coda): align to start of measure
    /// - End marks (fine, D.S., D.C.): align to end of measure
    /// </remarks>
    /// <summary>
    /// X anchor for a mark. Marks break-align: mid-line they anchor on the
    /// measure's start barline; at a line start (no visible barline) the
    /// anchor falls back to the key signature / clef — i.e. the start of the
    /// system's prefix — NOT the first note.
    /// Boxed labels (Rehearsal / SectionLabel) align their LEFT edge on the
    /// anchor; the returned X is the box CENTER (the renderer draws
    /// middle-anchored), so half the box width is added.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm SectionLabel —
    ///   (self-alignment-X . LEFT),
    ///   X-offset self-alignment-interface::self-aligned-on-breakable.
    /// LILYPOND-REF: scm/define-grobs.scm RehearsalMark —
    ///   break-align-symbols (staff-bar key-signature clef).
    /// </remarks>
    private static double CalculateXPosition(
        ScoreTextMetrics fonts,
        MusicMarkItem mark, MeasureLayout measureLayout,
        ImmutableArray<SystemLayout> systems,
        Func<int, double>? prefixTimeSignatureX = null,
        Func<int, double>? lineStartBarlineX = null,
        Func<int, double>? prefixMarkAnchorX = null,
        ImmutableArray<Measure> measures = default)
    {
        if (mark.Position == MusicMarkPosition.End)
            return measureLayout.X + measureLayout.Width - 0.5; // Before end barline

        // A mid-measure tempo change attaches to the musical column of the note
        // that follows it (LilyPond's MetronomeMark moment), not the measure's
        // break-align prefix. Index 0 (first note) stays a measure-start tempo
        // and falls through to the break-align logic below.
        // LILYPOND-REF: metronome-engraver.cc — mark attached at its moment.
        if (mark.Type == MusicMarkType.Tempo && mark.AnchorItemIndex > 0)
        {
            // Resolve the note column the mark sits over. On a grand staff the
            // staves share timing columns, but each voice indexes its OWN notes,
            // so the authoring voice's item index would pick the wrong staff's
            // note (independent rhythms). Prefer the shared timing columns there;
            // fall back to the item index on a single staff (no columns).
            //
            // LilyPond aligns the metronome notehead with the following note's
            // head (its " = NNN" text then trails to the right of that note).
            // The timing column X already lands on the drawn note glyph; the
            // single-staff item X is the slot reference, ~0.7 ss right of the
            // glyph, so back that path off to match.
            // LILYPOND-REF verified: \tempo 4 = N mid-measure puts the mark's
            // notehead at the same X as the note that follows it.
            if (!measureLayout.Columns.IsDefaultOrEmpty)
                return measureLayout.X + measureLayout.GetXForTiming(mark.AnchorTiming);
            if (mark.AnchorItemIndex < measureLayout.Items.Length)
                return measureLayout.X + measureLayout.Items[mark.AnchorItemIndex].X - 0.70;
        }

        // A measure-start tempo self-aligns LEFT on the break-aligned TIME SIGNATURE:
        // its ink left = the meter column's ink left (measured 0.000000 in the probe,
        // tempo-mark.ly header). At a line start that column is the prefix's meter; with
        // no meter to align on, the mark sits over the first notational element of the
        // measure instead ("Gardner Read, Music Notation p.278", LilyPond's own comment).
        // ⚠️ LILYSHARP-OWN limit: a mid-line meter CHANGE is not a break-align column in
        // this model (MidMeasureChangeGaps stands in), so a tempo at such a bar takes the
        // musical-column arm where LilyPond would align it on the changed meter.
        // LILYPOND-REF: lily/metronome-engraver.cc:109-135 stop_translation_timestep —
        //   break-align parent when a support was acknowledged, currentMusicalColumn
        //   otherwise; scm/output-lib.scm:498-504 self-aligned-on-breakable.
        if (mark.Type == MusicMarkType.Tempo && mark.Position == MusicMarkPosition.Beginning)
        {
            if (prefixTimeSignatureX != null)
            {
                for (int i = 0; i < systems.Length; i++)
                {
                    var sys = systems[i];
                    if (sys.Measures.IsDefaultOrEmpty
                        || sys.Measures[0].MeasureIndex != measureLayout.MeasureIndex)
                        continue;
                    double tsX = prefixTimeSignatureX(i);
                    if (!double.IsNaN(tsX))
                        return tsX;
                    break; // line start without a meter → the musical column below
                }
            }
            if (!measureLayout.Columns.IsDefaultOrEmpty)
                return measureLayout.X + measureLayout.GetXForTiming(mark.AnchorTiming);
            if (measureLayout.Items.Length > 0)
                return measureLayout.X + measureLayout.Items[0].X - 0.70;
            return measureLayout.X;
        }

        // Pedal marks ("Ped." / "*") attach to the note they are written on
        // (like the metronome mark), so they anchor at that note's column rather
        // than the measure/line start. LILYPOND-REF: piano-pedal-engraver.cc.
        if (mark.AnchorItemIndex >= 0 &&
            mark.Type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
                or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
                or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff)
        {
            if (!measureLayout.Columns.IsDefaultOrEmpty)
                return measureLayout.X + measureLayout.GetXForTiming(mark.AnchorTiming);
            if (mark.AnchorItemIndex < measureLayout.Items.Length)
                return measureLayout.X + measureLayout.Items[mark.AnchorItemIndex].X;
        }

        if (mark.Position != MusicMarkPosition.Beginning)
            return measureLayout.X + measureLayout.Width / 2; // Center (fallback)

        // Break-align anchor: at a line start the barline is invisible, so
        // the anchor falls back to the start of the prefix (clef/key).
        double anchor = measureLayout.X;
        int lineStartSystem = -1;
        for (int i = 0; i < systems.Length; i++)
        {
            var system = systems[i];
            if (!system.Measures.IsDefaultOrEmpty
                && system.Measures[0].MeasureIndex == measureLayout.MeasureIndex)
            {
                anchor = system.Indent + 0.3;
                lineStartSystem = i;
                break;
            }
        }

        if (mark.Type is MusicMarkType.Rehearsal or MusicMarkType.SectionLabel)
        {
            // The two grobs break-align differently and the difference is the whole
            // arm: a SectionLabel declares (left-edge staff-bar) and so keeps the
            // line-start edge above, while a RehearsalMark declares
            // (staff-bar key-signature clef) — at a line start the staff-bar is
            // invisible, so its refpoint (= the box's LEFT edge, the markup stencil's
            // X origin) lands on the KEY ink's right edge, or the CLEF's when no key
            // stands. A drawn opening bar (|:) is the staff-bar made visible and wins
            // the list. MEASURED on 2.26.0 (mark-chord-row.ly MKQ/MKK): box left =
            // clef right 3.365000, = key right 6.385000, six digits on every system.
            // LILYPOND-REF: lily/break-alignment-interface.cc:299-334 find_parent —
            //   the first break-align-symbols entry with a break-visible, non-empty
            //   extent, invisible ones only as fallback.
            // LILYPOND-REF: lily/break-alignment-interface.cc:337-353 self_align_callback —
            //   the mark's refpoint lands on the parent's break-align-anchor.
            // LILYPOND-REF: scm/define-grobs.scm:905-907 Clef break-align-anchor-alignment, :1975-1977 KeySignature break-align-anchor-alignment —
            //   break-align-anchor calc-extent-aligned-anchor, anchor-alignment RIGHT.
            // MID-LINE the staff-bar is visible and wins the list: the mark's refpoint lands
            //   on the bar's calc-anchor — the centre of its strokes, dots excluded
            //   (EngravingDefaults.BarlineAnchorFromInkLeft) — and its self-alignment-X is
            //   the OPPOSITE of the bar's break-align-anchor-alignment, which a BarLine
            //   leaves at CENTER, so the box is CENTRED on that anchor.
            // LILYPOND-REF: scm/define-grobs.scm:2890-2894 RehearsalMark, self-alignment-opposite-of-anchor
            //   (its self-alignment-X, via break-alignable-interface) and its X-offset
            //   self-alignment-interface::self-aligned-on-breakable;
            // LILYPOND-REF: scm/output-lib.scm:484-488 self-alignment-opposite-of-anchor,
            //   :498-504 self-aligned-on-breakable.
            // MEASURED on 2.26.0 (audit/lp-geometry/probes/mark-mid-line.ly, five books —
            //   |, .|:, :|., :|.|:, and the owner's \alternative endings): box centre =
            //   bar anchor to six digits in every one. Until session 328 this arm put the
            //   box's LEFT edge on the measure's X, which the owner saw as the E1/E2 boxes
            //   of Lambada Complicada standing right of LilyPond's.
            // ⚠️ LILYSHARP-OWN, declared (user decision 2026-09-02, HANDOFF §3 第322): the
            //   SECTION LABEL takes the same line-start arm. LilyPond's own SectionLabel
            //   grob keeps the left edge (its list is (left-edge staff-bar)), but a Lily#
            //   `form' section name is spelled `\mark \markup \box` in the LilyPond twin
            //   (LilyPondExporter), i.e. a RehearsalMark, and the owner's books read that
            //   way: the box stands at the meter column with the tempo stacked under it.
            //   The ledger point mark.section-label.line-start.box-left-from-clef-left is
            //   therefore refereed against the RehearsalMark's number (MKQ), not MKB's.
            //   The line-start edge (Indent + 0.3) is not lost: `marks beside' (§3, not yet
            //   built) is the display option that brings it back.
            if (lineStartSystem >= 0)
            {
                if (lineStartBarlineX?.Invoke(measureLayout.MeasureIndex) is { } barX
                    && !double.IsNaN(barX))
                    anchor = barX;
                else if (prefixMarkAnchorX?.Invoke(lineStartSystem) is { } prefX
                         && !double.IsNaN(prefX))
                    anchor = prefX;
            }
            else if (!measures.IsDefaultOrEmpty
                     && MidLineBarAnchorX(measureLayout, measures) is { } barAnchor)
            {
                // Centred on the bar line's anchor: the returned X IS the box centre.
                return barAnchor;
            }
            // LEFT edge on the anchor: returned X is the box center.
            double fs = mark.Type == MusicMarkType.Rehearsal ? 4.0 * 0.6 : 4.0 * 0.55;
            double boxWidth =
                fonts.Advance(mark.Text, fs, TextRole.Mark, FontStyle.Bold) + 0.4;
            return anchor + boxWidth / 2;
        }

        // Segno/Coda glyphs have a symmetric bbox (origin = horizontal centre), so
        // returning the barline anchor centres the sign on the barline.
        if (mark.Type is MusicMarkType.Segno or MusicMarkType.Coda)
        {
            // ⚠️ ...AND AT A LINE START THE BAR LINE IS NOT ALWAYS INVISIBLE. The fallback
            // above reads "no bar line here, so align on the prefix", which is true of a
            // system that merely continues and false of one that OPENS WITH A REPEAT: the
            // `|:` is drawn, past the prefix, in the staff-bar column
            // (MultiStaffLayouter.LineStartBarGap). The owner's book put
            // this sign at the system's left edge (0.30) with the `|:` at 6.44.
            // These two grobs break-align on the STAFF BAR first —
            // LILYPOND-REF: scm/define-grobs.scm CodaMark and SegnoMark both declare
            //   (break-align-symbols . (staff-bar key-signature clef)) — where SectionLabel
            //   declares (left-edge staff-bar) and therefore keeps the edge above.
            // ⚠️ LILYSHARP-OWN, and it has to be: LilyPond never draws this sign here at all.
            // CodaMark's (break-visibility . begin-of-line-invisible) at :1007 prints a mark
            // whose moment IS a break at the END OF THE PREVIOUS LINE instead — MEASURED,
            // audit/lp-geometry/probes/coda-line-start.ly scores CB2/CB3: no CodaMark appears
            // on the new line, and one appears at the old line's end (x 34.048, breakdir -1),
            // while the SectionLabel control CB4 does appear on the new line at x 0.0. So
            // there is no LilyPond number for THIS placement, only for the rule that decides
            // it: whichever grob the sign break-aligns to. The owner chose to keep the sign
            // on the new line (2026-08-18) and align it to that bar line; porting the
            // visibility instead is the other option and is written up in the probe.
            if (lineStartBarlineX?.Invoke(measureLayout.MeasureIndex) is { } barX
                && !double.IsNaN(barX))
                return barX;
            return anchor;
        }

        return anchor + 0.5;
    }

    /// <summary>
    /// The absolute X of the <c>break-align-anchor</c> of the bar line standing at the
    /// START of <paramref name="measureLayout"/> mid-line, or null when no bar line is
    /// drawn there. The bar is the measure's own start bar line when it draws one
    /// (a <c>|:</c>, drawn from the measure's X rightwards — a plain end bar before it
    /// yields, SharedRenderer.EndBarYieldsToRepeatStart), else the previous measure's end
    /// bar line (drawn ending AT the measure's X; a <c>:|</c> before a <c>|:</c> is one
    /// <c>:|.|:</c> on the predecessor's end, the collector's folding). The anchor is
    /// then <see cref="EngravingDefaults.BarlineAnchorFromInkLeft"/> from the ink's left.
    /// </summary>
    private static double? MidLineBarAnchorX(MeasureLayout measureLayout, ImmutableArray<Measure> measures)
    {
        int idx = measureLayout.MeasureIndex;
        if (idx < 0 || idx >= measures.Length)
            return null;
        var start = measures[idx].StartBarline;
        if (start != BarlineType.None)
            return measureLayout.X + EngravingDefaults.BarlineAnchorFromInkLeft(start);
        if (idx == 0)
            return null;
        var end = measures[idx - 1].EndBarline;
        if (end == BarlineType.None)
            return null;
        return measureLayout.X - EngravingDefaults.BarlineDrawnWidth(end)
               + EngravingDefaults.BarlineAnchorFromInkLeft(end);
    }
}
