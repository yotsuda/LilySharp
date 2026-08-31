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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Rules for calculating spacing in music notation.
/// </summary>
/// <remarks>
/// Based on Gourlay (1987): "Spacing a Line of Music"
/// The spacing is approximately logarithmic with respect to duration.
/// </remarks>
internal static partial class SpacingRules
{
    /// <summary>
    /// Calculates the ideal width for a measure (includes duration-based spacing).
    /// </summary>
    /// <remarks>
    /// The ideal width follows Lilypond's spacing algorithm where each duration
    /// gets space proportional to its length (logarithmic scaling).
    /// This is the width that produces visually pleasing spacing.
    /// </remarks>
    public static double CalculateMeasureIdealWidth(Measure measure,
                                                    double? baseShortestDuration = null)
    {
        // The trailing clef column takes no width of its own — its clef lives in the
        // PREVIOUS measure's closing gap (see Measure.IsTrailingClefColumn).
        if (measure.IsTrailingClefColumn)
            return 0;

        double width = 0;

        // Barline widths
        width += GetBarlineWidth(measure.StartBarline);
        width += GetBarlineWidth(measure.EndBarline);

        // Spring ideal distances (content area) - includes duration space
        if (measure.Items.Length > 0)
        {
            var springs = CreateSpringsForMeasure(measure, baseShortestDuration);
            foreach (var spring in springs)
            {
                width += spring.IdealDistance;
            }
        }
        else if (measure.IsEmptyPlaceholder)
        {
            width += EmptyPlaceholderContentWidth();
        }

        return width;
    }

    /// <summary>
    /// Content width of an empty placeholder measure (a <c>| |</c> pair): the space
    /// an empty full bar gets in LilyPond's multi-measure-rest spacing rod — the
    /// duration space of a nominal whole measure plus the bound padding on each
    /// side — so the empty bar reads as a MEASURE instead of collapsing into what
    /// looks like a double barline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/multi-measure-rest.cc calculate_spacing_rods — length +=
    /// get_duration_space(measure-length) + 2 * bound-padding;
    /// scm/define-grobs.scm MultiMeasureRest bound-padding = 0.5.
    /// </remarks>
    public static double EmptyPlaceholderContentWidth()
        => CalculateDurationSpace(new Fraction(1, 1)) + 1.0;

    /// <summary>
    /// LilyPond's full-measure-extra-space (NonMusicalPaperColumn default = 1.0): when a
    /// single musical column fills the whole measure, LP widens that column's spring to the
    /// following barline so a lone whole note/dotted-half doesn't sit cramped against the bar.
    /// LILYPOND-REF: lily/spacing-spanner.cc fills_measure + lily/staff-spacing.cc
    /// situational_space (ideal += full-measure-extra-space); scm/define-grobs.scm
    /// NonMusicalPaperColumn (full-measure-extra-space . 1.0).
    /// </summary>
    public const double FullMeasureExtraSpace = 1.0;

    /// <summary>
    /// Whether an item stands in a MUSICAL paper column — LilyPond's
    /// <c>Paper_column::is_musical</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc Paper_column::is_musical —
    /// <c>get_property (me, "shortest-starter-duration")</c> read as a boolean. The column
    /// is musical when something that STARTS A DURATION is engraved in it, so what matters
    /// is neither the glyph's kind nor the item's duration on its own: a note, a chord and
    /// a rest all set it, and a SKIP sets nothing because it engraves no grob.
    /// <para>
    /// Measured on 2.24.4 by perturbing <c>full-measure-extra-space</c> to 0 over
    /// <c>c'4 d' e' f' | s1 | r1 | c'4 d' e' f'</c>: the <c>r1</c> bar narrows by exactly
    /// 1.000000 and the <c>s1</c> bar does not move. Reading "musical" as "has a duration"
    /// would have got the skip wrong.
    /// </para>
    /// <para>
    /// ⚠️ GRACE TIME IS NOT A MUSICAL COLUMN HERE, for a reason of the same shape. LilyPond
    /// gives a grace run its OWN spacing machine — <c>Grace_spacing_engraver</c> builds a
    /// <c>GraceSpacing</c> grob, and the main <c>Spacing_spanner</c> prices the grace's
    /// approach out of the moment's grace part rather than as one main-grid column per grace
    /// note (LILYPOND-REF: lily/grace-spacing-engraver.cc:46 process_music;
    /// lily/spacing-basic.cc:163-180 <c>Spacing_spanner::note_spacing</c>, whose grace branch
    /// reads <c>delta_t.grace_part_</c>). Lily# keeps that machine in
    /// <c>SpacingRules.Grace</c>, so a grace column reaching THIS spring would be priced
    /// twice — once as the group's reserved approach and once as a column of its own.
    /// ⚠️ SCAFFOLDING in the same sense as the renderer's skip: HANDOFF §2 U8 ⒝2 keeps the
    /// grace spring (LilyPond has one) and ends the double count by folding the two readers.
    /// </para>
    /// </remarks>
    internal static bool IsMusicalColumn(MusicItem? item) =>
        item is not { GraceTime: true }
        && item is NoteItem or ChordItem or RestItem { IsSpacer: false };

    /// <summary>
    /// True when a single musical column fills the whole measure (whole note in 4/4,
    /// dotted half in 3/4, a lone note or rest in its bar).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:446-472 Spacing_spanner::fills_measure. The
    /// source tests column MUSICALITY and nothing else — it never asks what is drawn — so a
    /// full-measure REST earns <see cref="FullMeasureExtraSpace"/> just as a whole note
    /// does. LilyPond reaches "the measure's only musical column" the long way round, by
    /// requiring the NEXT column to be non-musical (which can only be the closing bar line)
    /// and then <c>dt &gt; measure-length / 2</c>; that second test is a pickup guard, and a
    /// sole column passes it for free. This side keeps the direct form.
    /// <para>
    /// This used to end at <c>NoteItem or ChordItem</c>, on the grounds that a full-measure
    /// rest is priced by the multi-measure-rest rod instead. That holds for <c>R1</c>, which
    /// never reaches this spring, but not for a lowercase <c>r1</c>: LilyPond spaces it as an
    /// ordinary bar, and bar line to whole rest measures 1.900000 — the same 0.9 + 1.0 it
    /// gives a whole note. See the ledger's barline.next.whole-rest.
    /// </para>
    /// </remarks>
    public static bool FillsMeasure(Measure measure)
    {
        MusicItem? sole = null;
        foreach (var item in measure.Items)
        {
            if (item.IsLoose) continue;
            if (sole != null) return false;
            sole = item;
        }
        return IsMusicalColumn(sole);
    }

    /// <summary>
    /// Calculates the width of system prefix (clef + key + optional time signature).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc
    /// LILYPOND-REF: scm/define-grobs.scm break-align-orders, Clef/KeySignature/TimeSignature space-alist
    ///
    /// Delegates to BreakAlignSpacing which implements LP's break-alignment-interface
    /// with space-alist lookups and break-align-orders for correct element ordering.
    /// Uses the treble G-clef stencil ink as default; for other clefs, use the overload with clefWidth.
    /// </remarks>
    /// <summary>
    /// The ink width of a key signature — the sum of its accidentals' advances, 0 for an
    /// empty (C major) signature. This is the ONE key-width model: the break-align
    /// reservation (<c>CalculatePrefixWidth</c>, any overload → SolvePrefixColumns, the
    /// KeySignature group extent RIGHT) and the drawn prefix (SharedRenderer) both read
    /// it, so a custom (non-traditional) signature reserves exactly what it draws — the
    /// defect the ledger pair line-start.time-to-first-note.{standard,custom}-key opened
    /// was the reservation reading <c>Sharps</c> alone and dropping <c>Custom</c>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/break-alignment-interface.cc:141-142 calc_positioning_done — the group extent
    /// is the union of the engraved signatures' stencils; LilyPond has one key model
    /// (keyAlterations), so its reservation IS its drawing.</remarks>
    public static double KeySignatureInkWidth(KeySignature key)
    {
        if (key.Custom is { } custom)
        {
            double w = 0.0;
            foreach (var (_, alter) in KeySignature.DecodeCustom(custom))
                w += GlyphMetrics.GetKeySignatureAccidentalWidth(alter);
            return w;
        }
        if (key.Sharps == 0)
            return 0.0;
        // ⚠️ WALK THE SIGNATURE, do not multiply a count by one width. Past seven fifths the
        // leading letters carry DOUBLE accidentals, which are a different glyph and a
        // different advance (a double flat is 1.45 against a flat's 0.80) — so a reservation
        // that priced `min(|sharps|, 7)` singles would under-reserve exactly the signature
        // the drawer widened. This is the same list SharedRenderer.KeySignatureGlyphs draws:
        // placement and reservation are ONE claim (HANDOFF §5.0).
        double width = 0.0;
        foreach (var (_, alter) in Music.KeySpelling.SignatureSteps(key.Sharps))
            width += GlyphMetrics.GetKeySignatureAccidentalWidth(alter);
        return width;
    }

    public static double CalculatePrefixWidth(KeySignature key, bool includeTimeSignature,
        string timeSigNumerator = "4", string timeSigDenominator = "4")
    {
        return BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.LineStartClefWidth(ClefType.Treble),
            KeySignatureInkWidth(key),
            includeTimeSignature, timeSigNumerator, timeSigDenominator);
    }

    /// <summary>
    /// Calculates the width of system prefix with explicit clef width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc
    /// Use this overload when the clef type is known for accurate spacing.
    /// </remarks>
    public static double CalculatePrefixWidth(double clefWidth, KeySignature key,
        bool includeTimeSignature, string timeSigNumerator = "4", string timeSigDenominator = "4")
    {
        return BreakAlignSpacing.CalculatePrefixWidth(
            clefWidth,
            KeySignatureInkWidth(key),
            includeTimeSignature, timeSigNumerator, timeSigDenominator);
    }

    /// <summary>
    /// Calculates the width of a system prefix from the key column's own INK width — the
    /// break-align group's right edge (<see cref="WidestActiveKeyInk"/>), which is what a
    /// multi-staff system reserves: a union across staves is a width, not one staff's key.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/break-alignment-interface.cc:141-142,242 calc_positioning_done.</remarks>
    public static double CalculatePrefixWidth(double clefWidth, double keyInkWidth,
        bool includeTimeSignature, string timeSigNumerator = "4", string timeSigDenominator = "4")
    {
        return BreakAlignSpacing.CalculatePrefixWidth(
            clefWidth, keyInkWidth, includeTimeSignature, timeSigNumerator, timeSigDenominator);
    }

    /// <summary>
    /// The widest line-start clef ink across the score's notation staves — the shared
    /// prefix column every staff's key/time signature and first note break-align past.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:132-243 calc_positioning_done — one
    /// break-align GROUP per symbol, positioned from the group's own X-extent (:140-145), so
    /// the Clef column spans the
    /// whole system and in a grand staff the wider bass F clef governs the treble staff's
    /// meter position too (both signatures stay vertically aligned). Tab (its own clef),
    /// text (lyric/chord) and ossia rows carry no shared-prefix clef and are skipped. An
    /// empty selection books NOTHING — see <see cref="ClefGroupExtent(IEnumerable{ValueTuple{double, double}})"/>.
    /// </remarks>
    public static double MaxClefWidth(MultiStaffScore score)
    {
        var (left, right) = ClefGroupExtent(score);
        return right - left;
    }

    /// <summary>
    /// The Clef break-align GROUP's LEFT ink edge, relative to each clef's grob origin —
    /// the minimum over the staves that engrave one. The group is positioned so that THIS
    /// edge lands on <see cref="EngravingDefaults.ClefGlyphXOffset"/>, and every clef in
    /// the group then keeps its own stencil offset inside it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:242 calc_positioning_done — the offset is
    /// <c>extents[LeftEdge][RIGHT] + distance - extents[next][LEFT]</c>, and :141-142 makes
    /// that <c>[LEFT]</c> the union across the system's staves. So the anchor is a property
    /// of the GROUP, not of each clef.
    /// <para>
    /// It matters only where a system's clefs have DIFFERENT stencil left edges, since the
    /// pitched clefs all start at 0. Measured on 2.26.0
    /// (audit/lp-geometry/probes/line-start-mindist.ly, scores CGP and CGT), predicted
    /// before the dump and confirmed to 6 digits:
    /// </para>
    /// <list type="bullet">
    /// <item>percussion ALONE — group left 0.67, so the grob sits at 0.13 and its ink
    /// reaches 0.8. A per-clef "put my own ink-left on 0.8" rule agrees here, which is why
    /// this looked settled.</item>
    /// <item>percussion WITH a treble staff (CGP) — group left is min(0.67, 0) = 0, so both
    /// grobs sit at 0.8 and the percussion clef's ink is at 1.470..2.800, NOT flush at 0.8.
    /// The per-clef rule drew it 0.67 too far LEFT here.</item>
    /// <item>a TAB clef (stencil left 0.2) alone (CGT) — grob at 0.6, ink 0.800..3.400;
    /// beside a notation staff (TKC) — grob at 0.8, ink 1.000..3.600.</item>
    /// </list>
    /// Tab staves ARE in the group — LilyPond's TAB clef is an ordinary Clef grob
    /// (ly/engraver-init.ly TabStaff <c>clefGlyph = "clefs.tab"</c>) — which is what makes
    /// a notation+tab score's meter and first note sit 0.235 further right than the same
    /// music without the tab staff (probes TKC 7.720000 against SKC 7.485000).
    /// <see cref="LilySharp.Core.Rendering.SharedRenderer"/>'s tab renderer draws
    /// <c>clefs.tab</c> unscaled at this
    /// same anchor, so the width booked here is the width drawn.
    /// </remarks>
    public static (double Left, double Right) ClefGroupExtent(MultiStaffScore score)
        => ClefGroupExtent(EngravedClefStencils(score));

    /// <summary>
    /// The clef stencil each staff contributes to the break-align group, as an
    /// origin-relative ink extent. Text (lyric / chord) and ossia rows engrave none.
    /// </summary>
    /// <remarks>
    /// A tab staff engraves the TAB clef, which is an ordinary Clef grob in the SAME group
    /// (LILYPOND-REF ly/engraver-init.ly TabStaff <c>clefGlyph = "clefs.tab"</c>) and is
    /// WIDER than the G clef — origin-to-ink-right 2.8 against 2.565. Its stencil also
    /// starts 0.2 right of the origin, so it moves the group's LEFT too: alone the group's
    /// left is 0.2 (probe CGT — grob at 0.6, ink 0.800..3.400), beside a notation staff it
    /// is 0 (probe TKC — grob at 0.8, tab ink 1.000..3.600).
    /// </remarks>
    private static IEnumerable<(double Left, double Right)> EngravedClefStencils(
        MultiStaffScore score)
    {
        foreach (var (_, staff, _) in score.EnumerateStaves())
        {
            if (staff.IsTextRow || staff.IsOssia)
                continue;
            yield return staff.IsTab ? TabClefStencil : ClefStencil(staff.Clef);
        }
    }

    /// <summary>
    /// The Clef break-align group's ink extent over an explicit set of clef stencils — the
    /// union, in the frame of a clef's own grob origin. An EMPTY set gives an empty extent
    /// <c>(0, 0)</c>, i.e. no clef column at all. This ONE fold serves the score walk and
    /// the tests, so a tab staff's contribution cannot be modelled twice.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:145-146,155-156 calc_positioning_done — a break-align group
    /// with no grobs is SKIPPED; it neither consumes a space-alist gap nor anchors its
    /// neighbour. So a system whose rows are all lyric / chord (ly/engraver-init.ly:632-649
    /// Lyrics, :703-725 ChordNames — neither consists a <c>Clef_engraver</c>) gets NO clef
    /// column, not a default one.
    /// <para>
    /// This used to fall back to the treble G, which booked 2.565 of ink nobody draws in
    /// front of a lead sheet. It is the same defect the ledger closed for the KEY column
    /// under <c>line-start.time-to-first-note.tab-keyed</c>, where a column was booked for
    /// staves that engrave none. MEASURED (audit/lp-geometry/probes/staffless-system.ly,
    /// scores CO/CO3/COK): LilyPond puts the first chord name of a staff-less system on
    /// 0.500000 — <c>standard_breakable_column_spacing</c>'s <c>min_dist + 0.5</c> with
    /// <c>min_dist</c> 0 — so there is no prefatory ink in front of it at all.
    /// </para>
    /// </remarks>
    public static (double Left, double Right) ClefGroupExtent(
        IEnumerable<(double Left, double Right)> stencils)
    {
        double left = double.PositiveInfinity, right = double.NegativeInfinity;
        foreach (var (l, r) in stencils)
        {
            left = Math.Min(left, l);
            right = Math.Max(right, r);
        }
        return double.IsInfinity(left) ? (0.0, 0.0) : (left, right);
    }

    /// <summary>The stencil extent a pitched clef contributes to the group.</summary>
    public static (double Left, double Right) ClefStencil(ClefType clef)
        => (GlyphMetrics.ClefInkLeft(clef), GlyphMetrics.ClefInkRight(clef));

    /// <summary>The stencil extent the TAB clef contributes to the group.</summary>
    public static (double Left, double Right) TabClefStencil
        => (GlyphMetrics.ClefTab.Left, GlyphMetrics.ClefTab.Right);

    /// <summary>The Clef break-align group's left ink edge —
    /// <see cref="ClefGroupExtent(Model.MultiStaffScore)"/>'s
    /// <c>Left</c>, which is what the drawn clef is offset by.</summary>
    public static double ClefGroupInkLeft(MultiStaffScore score) => ClefGroupExtent(score).Left;

    /// <summary>
    /// Whether this staff ENGRAVES a key signature, i.e. whether it is one of the grobs the
    /// KeySignature break-align group is the union of. The ONE staff set both the
    /// reservation (<see cref="WidestActiveKeyInk"/>) and the drawn prefix walk
    /// (SharedRenderer's shared time column) select by, so the width booked and the width
    /// drawn cannot come from different staves.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:1214 — <c>TabStaff \remove Key_engraver</c> (and
    /// :297 for DrumStaff): a tab staff has NO KeySignature grob at all, so it contributes
    /// nothing to the group extent however many accidentals its own key spells. A lyric /
    /// chord-name row is a Lyrics-like context, which never had a Key_engraver to remove.
    /// Everything else — an OSSIA included, it being an ordinary Staff that keeps its
    /// Key_engraver — is in the group; probe scores OKN/OKNF measured the ossia's key
    /// sitting in the shared column. The ledger pair
    /// line-start.time-to-first-note.{tab-concert,tab-keyed} opened on the tab half: the
    /// reservation walked EVERY staff, so a tab-only transposed part booked a 6-sharp
    /// column nobody engraves and drove the first note 7.05 ss right of the meter it is
    /// spaced from, while LilyPond's two twins are geometrically identical.
    /// </remarks>
    public static bool ContributesToKeyColumnWidth(Staff staff) =>
        !staff.IsTab && !staff.IsTextRow;

    /// <summary>
    /// Whether this staff engraves a time signature with a STENCIL — the same question
    /// <see cref="ContributesToKeyColumnWidth"/> asks for the key column, for the meter one.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:80 — <c>Staff \consists Time_signature_engraver</c>,
    /// and neither Lyrics (:632-649) nor ChordNames (:703-725) does. So a lyric / chord row
    /// has no TimeSignature grob at all and books nothing.
    /// <para>
    /// ⚠️ A TAB STAFF ANSWERS BOTH WAYS, AND THE SWITCH IS THE ONE LILY#'S TWIN ALREADY
    /// THROWS. LilyPond's TabStaff keeps the Time_signature_engraver — the grob exists and
    /// sits in the shared meter column — and only BLANKS it:
    /// ly/engraver-init.ly:1219-1220 sits five lines under that block's \remove Key_engraver
    /// and reads <c>\override TimeSignature.stencil = ##f</c> — BLANKED, not un-engraved; the
    /// matching revert is ly/property-init.ly:825-826, above tabFullNotation's no-stem-extend
    /// one. Lily#'s default <c>tab</c> IS full notation: it draws
    /// stems, flags, dots, rests, beams and tuplet brackets, and LilyPondExporter writes
    /// <c>\tabFullNotation</c> into the twin for it. Only <c>tab … as numbers</c> is the
    /// bare TabStaff. So the meter's stencil follows <see cref="Staff.TabNumbersOnly"/>.
    /// </para>
    /// <para>
    /// MEASURED (LilyPond, same music as two twins — the falsifier pair
    /// <c>TabFullNotationEngravesTheMeter</c>): with <c>\tabFullNotation</c> the tab staff
    /// draws the initial 4/4 at clef + 4.320000 and the mid-piece 2/4 in its own column;
    /// bare, it draws neither and its first fret digit sits 3.4548 further left. Lily# used
    /// to reserve that 3.4548 in BOTH modes and draw the glyph in neither.
    /// </para>
    /// <para>
    /// The key column is NOT symmetric with this: <c>\tabFullNotation</c> has no
    /// <c>\revert</c> for a Key_engraver that was <c>\remove</c>d, so a tab staff engraves no
    /// signature in either mode (<see cref="ContributesToKeyColumnWidth"/>).
    /// </para>
    /// </remarks>
    public static bool ContributesToTimeColumnWidth(Staff staff) =>
        !staff.IsTextRow && !(staff.IsTab && staff.TabNumbersOnly);

    /// <summary>
    /// Whether ANY staff in the score engraves a time signature stencil. False for a system
    /// built only of chord / lyric rows, and for one built only of <c>tab … as numbers</c>
    /// staves — which is what stops the prefix booking a meter column no row draws.
    /// </summary>
    /// <remarks>
    /// MEASURED (audit/lp-geometry/probes/staffless-system.ly, scores CO and CO3): the same
    /// chords under 4/4 and under 3/4 put their first chord name on 0.500000 in both, to 15
    /// digits — LilyPond books no meter width because no context engraves one, while Lily#
    /// booked <c>GetTimeSigWidth(beats, beatType)</c>, which differs between the two.
    /// <para>
    /// ⚠️ THIS IS THE WHOLE GATE NOW. The all-tab case used to be a SECOND spelling — a
    /// <c>!score.AllStavesTab</c> guard sat in front of every call — from the days when a
    /// tab staff never engraved a meter. That guard answered the numbers-only question with
    /// the tab question and so blanked the meter of a full-notation tab book too; the two
    /// have been folded into <see cref="ContributesToTimeColumnWidth"/>, which is also what
    /// the drawing walk asks. <c>MultiStaffScore.AllStavesTab</c> now serves the KEY column
    /// alone, where a tab staff really does answer one way in both modes.
    /// </para>
    /// </remarks>
    public static bool AnyStaffEngravesTime(MultiStaffScore score)
    {
        // LILYSHARP-OWN, a DECIDED divergence (user decision 2026-08-20, HANDOFF §3):
        // a staff-less lead sheet ENGRAVES the score meter on its grid row — the row
        // that carries the measure barlines prints it at the line-start prefix, so the
        // time column books width exactly as a staff score's would. LilyPond books no
        // meter width for a ChordNames/Lyrics-only system (measured: probe
        // staffless-system.ly, the former staffless.line-start.meter-identity point —
        // retired WITH this decision; the lead-sheet grid is Lily#'s own surface and
        // reads as a chart, where the meter belongs).
        if (score.IsLeadSheet)
            return true;
        foreach (var (_, staff, _) in score.EnumerateStaves())
            if (ContributesToTimeColumnWidth(staff))
                return true;
        return false;
    }

    /// <summary>
    /// The engraved width of <paramref name="key"/> ON <paramref name="staff"/> — its
    /// stencil's X extent, which is the quantity the break-align group is the union of.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:141-142 calc_positioning_done — the group extent unions
    /// the grobs' own extents, so a staff engraving at a reduced size contributes its
    /// SMALLER stencil. An ossia is set at magstep(-3) (NR "Ossia staves": fontSize -3 +
    /// StaffSymbol.staff-space), which scales the stencil though not the space-alist:
    /// probe OKN dumped the ossia's key ink as 1.5558 = 2.2 * magstep(-3) while its column
    /// X stayed the unscaled shared one, and probe OKM shows the same under
    /// <c>\magnifyStaff</c> (which additionally scales the alist).
    /// </remarks>
    public static double EngravedKeyInkWidth(Staff staff, KeySignature key) =>
        KeySignatureInkWidth(key) * (staff.IsOssia ? EngravingDefaults.OssiaScale : 1.0);

    /// <summary>
    /// The RIGHT edge of the KeySignature break-align group at a system starting at
    /// <paramref name="startMeasureIndex"/>, measured from the group's left — i.e. the
    /// widest engraved signature in the system. Every staff's time signature and first note
    /// break-align past it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:141-142 calc_positioning_done — a break-align group's
    /// extent is the UNION of its grobs' extents across the whole system; :242 — the next
    /// column offsets from that union's RIGHT. Every signature starts at the shared column
    /// left, so the union's right is the widest engraved ink.
    /// </para>
    /// <para>
    /// A width, not a key, because that is what LilyPond unions: it carries a CUSTOM
    /// signature (a custom key is <c>KeySignature(0, custom)</c>, so comparing
    /// <c>Sharps</c> dropped it), a transposed part's own wider signature
    /// (<see cref="Staff.PerStaffKeySignature"/>), and an ossia's reduced-size stencil,
    /// none of which a single KeySignature value can stand for. Staves that engrave no
    /// signature are skipped (<see cref="ContributesToKeyColumnWidth"/>) — an all-tab score
    /// or a tab-only transposed part books nothing, exactly as LilyPond books nothing for a
    /// TabStaff that has no Key_engraver.
    /// </para>
    /// </remarks>
    public static double WidestActiveKeyInk(MultiStaffScore score, int startMeasureIndex)
    {
        double widestInk = 0.0;
        foreach (var staffGroup in score.StaffGroups)
            foreach (var staff in staffGroup.Staves)
                widestInk = Math.Max(widestInk,
                    ActiveKeyInkForStaff(score, staff, startMeasureIndex));
        return widestInk;
    }

    /// <summary>
    /// The key-signature ink ONE staff engraves at the head of a system starting at
    /// <paramref name="startMeasureIndex"/> — 0 for a staff with no <c>Key_engraver</c>, and
    /// 0 for a C-major signature (which has no stencil, hence an empty extent).
    /// </summary>
    /// <remarks>
    /// The per-grob half of <see cref="WidestActiveKeyInk"/>, which is the union of exactly
    /// this over the system's staves — ONE model, so the group extent and any individual
    /// grob's extent cannot drift apart. The individual extent is what
    /// <c>Staff_spacing::get_spacing</c> reads as <c>last_ext</c>
    /// (<see cref="LineStartColumn.LineStartSpring"/>): the break-align COLUMN is shared, the
    /// grob in it is the staff's own.
    /// <para>
    /// The prefix scan reads <see cref="ActiveKeyTableOf"/> — one O(measures) build per
    /// collected <see cref="Voice"/> instead of an O(startMeasureIndex) rescan per
    /// (staff, system) call, which summed to O(systems × measures) per keystroke
    /// (2026-08-26 review, finding 4-1; LayoutEngine's per-system prefix widths,
    /// LineStartColumn and the draw all land here). Same walk, one home.
    /// </para>
    /// </remarks>
    public static double ActiveKeyInkForStaff(
        MultiStaffScore score, Staff staff, int startMeasureIndex)
    {
        if (!ContributesToKeyColumnWidth(staff))
            return 0.0;

        var key = staff.PerStaffKeySignature ?? score.KeySignature;
        var pv = staff.PrimaryVoice;
        if (ActiveKeyTableOf(pv)[Math.Min(startMeasureIndex, pv.Measures.Length)]
            is { } changedKey)
            key = changedKey;

        // A change that OPENS this system's first measure is engraved in the PREFIX as the
        // NEW signature (SharedRenderer.GetSystemStartKeyChange; the cancellation goes to the
        // previous line as a courtesy). The reservation must therefore be the new key's ink,
        // not the outgoing one's — booking the old key made the head reserve a width nobody
        // draws and pushed the first note by the difference (3 sharps 3.30 against 3 flats
        // 2.76 = 0.54 on the kb-A/kb-B pair, where LilyPond's two lines are identical).
        // startMeasureIndex == 0 is the first system, whose head IS the initial signature.
        if (startMeasureIndex > 0 && startMeasureIndex < pv.Measures.Length)
            foreach (var item in pv.Measures[startMeasureIndex].Items)
            {
                if (item is KeySignatureChangeItem opening) { key = opening.NewKey; break; }
                if (item.Duration > Fraction.Zero) break;
            }

        return EngravedKeyInkWidth(staff, key);
    }

    /// <summary>
    /// Per-voice active-key table: <c>tbl[i]</c> is the LAST key-signature change among
    /// measures <c>[0, i)</c> in walk order (measure order, item order within a measure),
    /// or null when none — the caller falls back to its own seed
    /// (<c>PerStaffKeySignature ?? score.KeySignature</c>), so one table serves every
    /// seed. Length <c>Measures.Length + 1</c>: the last slot answers a start index at or
    /// past the end, matching the rescan's <c>m &lt; Measures.Length</c> clamp.
    /// </summary>
    /// <remarks>
    /// Keyed by INSTANCE identity on the collected <see cref="Voice"/> (the same
    /// static-CWT shape as <see cref="VoiceCollisionShiftsOf(Staff)"/> and
    /// PageLayouter's pair-distance memo): a re-collect builds fresh Voice records, so
    /// an edited book misses and rebuilds while an unchanged one replays. This is the
    /// ONE spelling of the prefix walk — <see cref="ActiveKeyInkForStaff"/> only looks
    /// up (2026-08-26 review, finding 4-1: 関数は 1 実装のまま).
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Voice, KeySignature?[]> ActiveKeyTables = new();

    private static KeySignature?[] ActiveKeyTableOf(Voice voice)
        => ActiveKeyTables.GetValue(voice, static v =>
        {
            var tbl = new KeySignature?[v.Measures.Length + 1];
            KeySignature? last = null;
            for (int m = 0; m < v.Measures.Length; m++)
            {
                tbl[m] = last;
                foreach (var item in v.Measures[m].Items)
                    if (item is KeySignatureChangeItem kc)
                        last = kc.NewKey;
            }
            tbl[v.Measures.Length] = last;
            return tbl;
        });

    /// <summary>
    /// Width reserved at the END of a line for the courtesy cancellation + new key signature
    /// when the NEXT line opens with a key change (drawn after the line's final barline) —
    /// the widest staff's group, mirroring <see cref="WidestActiveKeyInk"/> for the
    /// line-start column: the break-align COLUMN is shared, the grob in it is each staff's
    /// own (a transposed part carries its own key, a tab staff engraves none).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/key-engraver.cc + explicitKeySignatureVisibility
    /// (default all-visible) — a changed signature prints on BOTH sides of
    /// the break: courtesy at the old line's end, real one in the new
    /// line's prefix.
    /// <para>
    /// ⚠️ ONE MODEL WITH THE DRAW. The middle of the group is
    /// <c>SharedRenderer.KeyChangeGeometry</c>'s width — the walk the drawer consumes glyph
    /// by glyph — so the reserved width IS the drawn width, custom keys and clef-dependent
    /// kerning included. Until 2026-08-19 the cancellation half was a hand-summed UPPER
    /// BOUND on the natural kerning (0.3 per pair, where the drawn walk kerns 0.3 / 0.15 / 0
    /// by vertical overlap), and the surplus had nowhere to go but after the group — ledger
    /// <c>courtesy.key.key-to-line-end</c> opened 0.150198 on exactly that slack.
    /// </para>
    /// </remarks>
    /// <param name="startMeasureIndex">The line's first measure — where the clef the draw
    /// will use is resolved (SharedRenderer.ResolveClefAt; the courtesy is drawn with the
    /// system-START clef).</param>
    /// <param name="nextMeasureIndex">The first measure of the NEXT line, whose leading
    /// key change triggers the courtesy.</param>
    /// <param name="meterFollows">
    /// True when a courtesy METER stands after this key in the same end-of-line group. Then
    /// the key is not the group's last member and does NOT pay the gap to the right edge —
    /// the meter does, out of its own alist. Getting this wrong charges 0.5 twice or not at
    /// all, and both look like a spacing bug rather than a double-count.
    /// </param>
    public static double KeyCourtesySuffixWidth(
        MultiStaffScore score, int startMeasureIndex, int nextMeasureIndex, bool meterFollows)
    {
        double widest = 0.0;
        foreach (var staffGroup in score.StaffGroups)
            foreach (var staff in staffGroup.Staves)
                widest = Math.Max(widest, KeyCourtesySuffixWidthForStaff(
                    staff, startMeasureIndex, nextMeasureIndex, meterFollows));
        return widest;
    }

    /// <summary>
    /// The per-staff half of <see cref="KeyCourtesySuffixWidth"/> — 0 for a staff that
    /// engraves no key (<see cref="ContributesToKeyColumnWidth"/>) or whose next line opens
    /// with no key change.
    /// </summary>
    private static double KeyCourtesySuffixWidthForStaff(
        Staff staff, int startMeasureIndex, int nextMeasureIndex, bool meterFollows)
    {
        if (!ContributesToKeyColumnWidth(staff))
            return 0.0;
        var voice = staff.PrimaryVoice;
        if (nextMeasureIndex >= voice.Measures.Length)
            return 0.0;
        // The staff's own leading key change — the same walk as
        // SharedRenderer.GetSystemEndKeyChange, which decides what this staff DRAWS.
        KeySignatureChangeItem? change = null;
        foreach (var item in voice.Measures[nextMeasureIndex].Items)
        {
            if (item is KeySignatureChangeItem kc) { change = kc; break; }
            if (item.Duration > Fraction.Zero) break;
        }
        if (change is null)
            return 0.0;

        var clef = Rendering.SharedRenderer.ResolveClefAt(staff, startMeasureIndex);
        var (glyphs, ink) = Rendering.SharedRenderer.KeyChangeGeometry(change, clef);
        if (glyphs.Count == 0)
            return 0.0;
        double w = KeyCourtesyOpeningGap(glyphs) + ink;
        // The group's last member owes a gap to `right-edge`, and which grob that is depends
        // on what the change prints: a signature when the new key has accidentals, otherwise
        // the bare cancellation. Both declare 0.5, so the ARITHMETIC does not care — reading
        // the entry that is actually last is what keeps that a fact rather than a coincidence.
        if (!meterFollows)
            w += BreakAlignGap(
                glyphs[^1].Kind != "natural"
                    ? BreakAlignSymbol.KeySignature
                    : BreakAlignSymbol.KeyCancellation,
                BreakAlignSymbol.RightEdge);
        return w;
    }

    /// <summary>
    /// The bar line's gap to whichever grob OPENS the end-of-line courtesy key group —
    /// LilyPond keys the left grob's alist by the RIGHT grob's break-align-symbol, so a group
    /// that opens with the cancellation and one that opens with the signature read different
    /// entries (both 1.0 as it happens, and reading the right one is what keeps that a fact
    /// rather than luck). Which grob opens is read off the DRAWN walk's first glyph, so the
    /// reservation and the draw cannot disagree about it — custom keys included, where the
    /// standard-count test (<see cref="CancellationNaturalCount"/>) does not apply.
    /// </summary>
    public static double KeyCourtesyOpeningGap(
        List<(string Kind, double Dx, int StaffPosition)> keyChangeGlyphs) =>
        BreakAlignGap(BreakAlignSymbol.StaffBar,
            keyChangeGlyphs.Count > 0 && keyChangeGlyphs[0].Kind == "natural"
                ? BreakAlignSymbol.KeyCancellation
                : BreakAlignSymbol.KeySignature);

    /// <summary>
    /// How many cancellation naturals a key change from <paramref name="prevSharps"/> to
    /// <paramref name="nextSharps"/> prints — 0 when the new signature cancels nothing.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOUSE, because the answer decides two things that must agree: how much room
    /// <see cref="KeyCourtesySuffixWidth"/> reserves, and which space-alist entry opens the
    /// group (a cancellation and a signature are different break-align symbols). It was spelled
    /// twice — here and in SharedRenderer.DrawKeySignatureChange — which is the shape §7.7 keeps
    /// naming.
    /// LILYPOND-REF: lily/key-engraver.cc — the cancellation is the previous signature's
    /// alterations that the new one no longer makes.
    /// </remarks>
    public static int CancellationNaturalCount(int prevSharps, int nextSharps)
    {
        bool needNaturals = (prevSharps != 0 && nextSharps == 0) ||
                            (prevSharps > 0 && nextSharps < 0) || (prevSharps < 0 && nextSharps > 0) ||
                            (Math.Sign(prevSharps) == Math.Sign(nextSharps)
                             && Math.Abs(nextSharps) < Math.Abs(prevSharps));
        return needNaturals
            ? Math.Abs(prevSharps) - (Math.Sign(prevSharps) == Math.Sign(nextSharps) ? Math.Abs(nextSharps) : 0)
            : 0;
    }

    /// <summary>
    /// The ink-to-ink gap between two adjacent members of a break-align group.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:180-210 Break_alignment_interface::calc_positioning_done
    ///   — the space-alist is taken off the LEFT grob and keyed by the RIGHT grob's
    ///   <c>break-align-symbol</c>.
    /// LILYPOND-REF: lily/break-alignment-interface.cc:241-243 Break_alignment_interface::calc_positioning_done
    ///   — places the next group at <c>extents[idx][RIGHT] + distance - extents[next][LEFT]</c>,
    ///   so for an <c>extra-space</c> entry BOTH extents cancel and the ink-to-ink gap IS the
    ///   distance.
    /// <para>
    /// ⚠️ That cancellation is why this returns the raw value, and why it refuses any other
    /// style: a <c>minimum-space</c> entry is <c>max(extents[l][RIGHT], distance)</c> and does
    /// NOT reduce to a gap, so a caller measuring off an ink edge would silently be wrong. Every
    /// entry the end-of-line group reads is extra-space today (BarLine :293/:296/:297,
    /// KeyCancellation :1944, KeySignature :1989); if that ever changes, this throws instead of
    /// answering, and the caller has to be rewritten rather than quietly drift.
    /// </para>
    /// </remarks>
    internal static double BreakAlignGap(BreakAlignSymbol left, BreakAlignSymbol right)
    {
        var entry = BreakAlignSpacing.GetSpacing(left, right);
        if (entry.Style != SpacingStyle.ExtraSpace)
            throw new InvalidOperationException(
                $"the {left}->{right} space-alist entry is {entry.Style}, which is not a plain "
                + "ink-to-ink gap; this caller measures off an ink edge and must be rewritten.");
        return entry.Value;
    }

    // ⚠️ THREE CONSTANTS STOOD HERE UNTIL 2026-08-03 — BarlineToCourtesyKey (1.0),
    // BarlineToCourtesyTime (0.75) and CourtesyKeyToTimeGap (1.15), the end-of-line courtesy's
    // gaps written out by hand. They are gone: the group now reads the same space-alist the
    // line-START prefix does, through BreakAlignGap above, because LilyPond has ONE break-align
    // group at each end of a line and not a solver at one end with constants at the other.
    // Deleting them moved nothing — every one was already its alist entry exactly. What DID
    // move was a FOURTH gap nobody had named: a bare 0.4 for cancellation→key, where LilyPond
    // declares 0.5. It survived the audit that checked these three because it was not a
    // constant with a name, and no ledger point reached it.
    // The full account, with both engines measured, is in audit/lp-geometry ledger entries
    // courtesy.meter.barline-to-{meter,cancellation} and courtesy.key.cancellation-to-key.

    /// <summary>
    /// Width reserved at the END of a line for the courtesy meter when the NEXT line opens
    /// with a time-signature change, given whether a courtesy KEY is already standing there.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3922-3953 TimeSignature's break-align-anchor and break-visibility — the TimeSignature
    ///   grob's is <c>all-visible</c>, so a CHANGED meter prints on both
    ///   sides of the break. See SharedRenderer.GetSystemEndTimeChange for why only a
    ///   changed one does.
    /// </remarks>
    public static double TimeCourtesySuffixWidth(
        TimeSignatureChangeItem change, bool afterCourtesyKey)
        // The meter's gap is measured off whatever stands to its LEFT in the group — the key
        // when one is there, otherwise the bar line — and those are different alist entries
        // (1.15 against 0.75), which is why one "space after the bar line" cannot cover both.
        => BreakAlignGap(
               afterCourtesyKey ? BreakAlignSymbol.KeySignature : BreakAlignSymbol.StaffBar,
               BreakAlignSymbol.TimeSignature)
           + GlyphMetrics.GetTimeSigWidth(
               change.NewTime.NumeratorText, change.NewTime.DenominatorText)
           // ⚠️ AND THE GAP TO THE EDGE ITSELF. A break-align group has a member to the RIGHT
           // of its last grob — `right-edge` — and the meter declares 0.5 for it. Without this
           // the staff line stopped at the meter's advance edge: 0.07 ss of white on the
           // owner's book, which reads as the line running into the signature.
           + BreakAlignGap(BreakAlignSymbol.TimeSignature, BreakAlignSymbol.RightEdge);

    /// <summary>
    /// Gets the width of a barline type.
    /// </summary>
    /// <remarks>
    /// A bar line reserves EXACTLY its drawn stencil, nothing more. In LilyPond the
    /// bar line column's contribution is `last_ext[RIGHT]` — the break-aligned grob's
    /// own X-extent — and every bit of breathing room to the neighbouring note comes
    /// from the space-alist entry applied on top of it (see GetBarlineToItemSpace).
    /// The former 0.61 ss of extra "clearance" folded into this reservation had no
    /// counterpart in LilyPond and double-charged that padding.
    /// LILYPOND-REF: lily/staff-spacing.cc:166-167 (`Real fixed = last_ext[RIGHT]`).
    /// </remarks>
    public static double GetBarlineWidth(BarlineType type) =>
        EngravingDefaults.BarlineDrawnWidth(type);

    private static bool HasAccidental(MusicItem? item)
    {
        return item switch
        {
            NoteItem note => note.Accidental != null,
            ChordItem chord => chord.Notes.Any(n => n.Accidental != null),
            _ => false
        };
    }

    internal static int GetDots(MusicItem item)
    {
        return item switch
        {
            NoteItem note => note.Dots,
            RestItem rest => rest.Dots,
            ChordItem chord => chord.Dots,
            _ => 0
        };
    }

    /// <summary>
    /// Gets the width of a change (mid-measure) clef glyph.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/clef.cc:29-52 calc_glyph_name — "_change" suffix glyphs are smaller variants.
    /// </remarks>
    internal static double GetClefChangeWidth(ClefType clef) => clef switch
    {
        ClefType.Bass => GlyphMetrics.FClefChangeWidth,
        ClefType.Alto or ClefType.Tenor => GlyphMetrics.CClefChangeWidth,
        _ => GlyphMetrics.GClefChangeWidth
    };

    // ========================================
    // Spring-Rod Model Support
    // ========================================
    // Uses SMuFL metrics from GlyphMetrics for accurate spacing

    /// <summary>Minimum gap between items in staff spaces.</summary>
    public static double MinItemGap => GlyphMetrics.MinItemGap;

    /// <summary>Padding between barline and first/last item in staff spaces.</summary>
    public static double BarlinePadding => GlyphMetrics.BarlinePadding;

    /// <summary>
    /// One side of LilyPond's default <c>extra-spacing-width</c>, the amount every grob
    /// widens its spacing box by unless it declares its own.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/separation-item.cc:166-167 extra-spacing-width — its default <c>Interval (-0.1, 0.1)</c>.</remarks>
    internal const double DefaultExtraSpacingWidth = 0.1;

    /// <summary>
    /// The vertical padding a MUSICAL column's skyline distance is taken with — how far
    /// apart two boxes must be in Y before they stop constraining each other horizontally.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm PaperColumn
    ///   <c>(skyline-vertical-padding . 0.08)</c>, read by lily/note-spacing.cc:79-81 off the
    ///   RIGHT column. A NonMusicalPaperColumn leaves it at 0.
    /// </remarks>
    internal const double MusicalColumnSkylineVerticalPadding = 0.08;

    /// <summary>
    /// The padding the spacing spanner adds on top of a skyline distance to make a ROD.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:315-316 generate_springs —
    ///   <c>Real padding = from_scm&lt;double&gt; (get_property (prev, "padding"), 0.1);
    ///   set_column_rods (cols, padding);</c>, spent in lily/separation-item.cc:56
    ///   (<c>dist = padding + …distance (right)</c>).
    /// </remarks>
    internal const double SeparationRodPadding = 0.1;

    /// <summary>
    /// The LEFTward <c>extra-spacing-width</c> an Accidental declares for itself — twice the
    /// default, and asymmetric: an accidental reserves 0.2 to its left and 0.0 to its right.
    /// </summary>
    /// <remarks>
    /// This is the box that enters the skyline when an accidental is the leftmost ink of a
    /// column, so it is what a bar line's minimum distance is measured against. Verified on
    /// 2.24.4 with `c'4 d' e' f' | cis'4 d' e' f'`: the dumped rod is 2.04, i.e. min_dist
    /// 1.94 + padding 0.1, and 1.94 = bar ink 0.19 + bar esw 0.1 + accidental reach 1.45 +
    /// 0.2; the accidental is then placed at min_dist + 0.3 = 2.24 from the column origin,
    /// which is exactly the measured 23.185729 - 20.945729.
    /// LILYPOND-REF: scm/define-grobs.scm:40 Accidental's extra-spacing-width
    ///   <c>(extra-spacing-width . (-0.2 . 0.0))</c>; :62 AccidentalCautionary likewise.
    /// </remarks>
    internal const double AccidentalExtraSpacingWidthLeft = 0.2;

    /// <summary>
    /// The headroom <c>merge_springs</c> leaves above a spring's minimum distance.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spring.cc:104-129 <c>merge_springs</c>, :122
    /// <c>avg_distance = std::max (min_distance + 0.3, avg_distance);</c> — "leave a little
    /// headroom above the largest minimum distance so that things don't get too cramped".
    /// <para>
    /// LilyPond writes the same 0.3 a second time, in
    /// <c>Staff_spacing::get_spacing</c> — <c>min_dist_correction = max (0, 0.3 + min_dist -
    /// fixed)</c> (lily/staff-spacing.cc:212-213, "ensure that the <q>fixed</q> distance will
    /// leave a gap of at least 0.3 ss") — and BOTH readings live on this one constant:
    /// <see cref="RightGap"/> for a mid-measure change column and
    /// <see cref="LineStartColumn.SpringWithMinimumDistanceFloor"/> for a line start. One
    /// home rather than two, because a second constant with the same value in another file is
    /// how <c>SystemSpacing * 0.5</c> survived (section 5.2.1 item 5). ⚠️ They are distinct
    /// LilyPond sites all the same: if one of them ever stops being 0.3 upstream, this has to
    /// split.
    /// </para>
    /// </remarks>
    internal const double SpringHeadroom = 0.3;

    /// <summary>
    /// The gap <c>Staff_spacing::get_spacing</c> guarantees above the minimum distance
    /// when it corrects its FIXED distance — a SECOND hard-coded 0.3, distinct from
    /// <see cref="SpringHeadroom"/>.
    /// </summary>
    /// <remarks>
    /// Both are 0.3 and both floor a distance at <c>min + 0.3</c>, but they are separate
    /// mechanisms and both fire on this spring: <c>breakable_column_spacing</c> hands the
    /// Staff_spacing wish on to <c>merge_springs</c>, so the value is corrected here and
    /// then merged there. This one moves <c>fixed</c> (and hence the inverse COMPRESS
    /// strength), <see cref="SpringHeadroom"/> moves the ideal.
    /// LILYPOND-REF: lily/staff-spacing.cc:212-215 get_spacing — "ensure that the 'fixed' distance
    ///   will leave a gap of at least 0.3 ss";
    ///   lily/spacing-spanner.cc:478-536 breakable_column_spacing.
    /// </remarks>
    internal const double StaffSpacingFixedHeadroom = 0.3;

    /// <summary>
    /// StaffSpacing's own <c>stem-spacing-correction</c> — 0.4, and deliberately NOT
    /// NoteSpacing's 0.5 (<see cref="NoteSpacingParameters.StemSpacingCorrection"/>).
    /// </summary>
    /// <remarks>
    /// Two different grobs declare a property of the same name with different values, and
    /// the bar-line → note optical correction reads StaffSpacing's.
    /// LILYPOND-REF: scm/define-grobs.scm:3369 StaffSpacing's stem-spacing-correction
    ///   <c>(stem-spacing-correction . 0.4)</c>; :2656 NoteSpacing has 0.5.
    /// </remarks>
    internal const double StaffSpacingStemCorrection = 0.4;

    /// <summary>
    /// Applies <c>merge_springs</c>' headroom: a spring never sits at its bare minimum,
    /// but at least <see cref="SpringHeadroom"/> above it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LilyPond routes EVERY spring through <c>merge_springs</c> — even the single-wish
    /// case, which is why this is not a polyphony-only concern
    /// (lily/spacing-spanner.cc:380-393, :514-517).
    /// </para>
    /// <para>
    /// This is a distinct constraint from the column ROD. The rod is
    /// <c>padding (0.1) + skyline distance</c> (lily/separation-item.cc:48-68); this floor
    /// is <c>skyline distance + 0.3</c>, where the spring's minimum is the PADDING-FREE
    /// skyline distance (lily/note-spacing.cc:78-83). The floor is therefore always the
    /// larger of the two, so at force &gt;= 0 — ragged-right, the default — it is what
    /// binds, and the rod only surfaces when the line is compressed below it. Measured on
    /// 2.24.4 with <c>c'2 c'2 \clef bass c'1</c>: ragged-right 1.877346 (= skyline
    /// 1.577346 + 0.3), justified at 40mm 1.677346 (= skyline + padding).
    /// </para>
    /// <para>
    /// Where the duration-based ideal already exceeds the floor — every ordinary
    /// note-to-note spring, where the ideal is ~3.0 against a floor of ~1.8 — this is a
    /// no-op. It bites exactly where the ideal is driven small, above all at a measure
    /// boundary carrying a clef change, whose ideal is measured to the bar line and so
    /// discounts the clef's whole width.
    /// </para>
    /// </remarks>
    internal static Spring ApplyMergeSpringsHeadroom(Spring spring)
    {
        double floor = spring.MinDistance + SpringHeadroom;
        // LILYPOND-REF: lily/spring.cc:104-129 merge_springs — the headroom moves
        // avg_distance ONLY; both strengths are then assigned from the averages
        // (:125-127), which for a single wish are that wish's own strengths
        // (avg_compress = 1 / (1 / invC) = invC). So the floor must not recompute them.
        return spring.IdealDistance >= floor
            ? spring
            : spring.WithIdealDistance(floor);
    }

    /// <summary>Gap between accidental and notehead in staff spaces.</summary>
    public static double AccidentalNoteGap => GlyphMetrics.AccidentalNoteGap;

    /// <summary>
    /// Gets the width of a mid-measure key signature change.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/key-engraver.cc — key signature width depends on accidental count.
    /// Includes cancellation naturals from previous key.
    /// </remarks>
    internal static double GetKeySignatureChangeWidth(KeySignatureChangeItem keyChange)
    {
        double width = 0;

        // Cancellation naturals (from previous key)
        int prevCount = keyChange.PreviousKey.Count;
        int newCount = keyChange.NewKey.Count;
        bool sameType = (keyChange.PreviousKey.IsSharps == keyChange.NewKey.IsSharps) ||
                        keyChange.PreviousKey.Sharps == 0 || keyChange.NewKey.Sharps == 0;

        // LILYPOND-REF: lily/key-engraver.cc:67-125 create_key — cancellation logic
        if (!sameType && prevCount > 0)
        {
            // Different type (sharps→flats or flats→sharps): cancel all previous
            width += prevCount * GlyphMetrics.KeySignatureNaturalWidth;
        }
        else if (sameType && prevCount > newCount && keyChange.PreviousKey.Sharps != 0)
        {
            // Same type but fewer: cancel the difference
            width += (prevCount - newCount) * GlyphMetrics.KeySignatureNaturalWidth;
        }

        // New key accidentals
        if (newCount > 0)
        {
            width += newCount * GlyphMetrics.GetKeySignatureAccidentalWidth(keyChange.NewKey.IsSharps);
        }

        return Math.Max(width, GlyphMetrics.KeySignatureNaturalWidth); // minimum width
    }

    /// <summary>
    /// Gets the width of a mid-measure time signature change.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/time-signature-engraver.cc — width is the wider of the
    /// numerator / denominator digit stacks.
    /// </remarks>
    internal static double GetTimeSignatureChangeWidth(TimeSignatureChangeItem timeChange) =>
        GlyphMetrics.GetTimeSigWidth(
            timeChange.NewTime.NumeratorText, timeChange.NewTime.DenominatorText);

    /// <summary>
    /// Calculates the item's LEFTward ink reach from its column, as a positive amount.
    /// This includes accidentals which are drawn to the left of the notehead.
    /// </summary>
    /// <remarks>
    /// The reference point is the COLUMN, which coincides with the note head's LEFT edge
    /// (see the note on the base extent in the body, and
    /// <see cref="CalculateNoteheadRightExtent"/> for the matching right-hand side). So a
    /// plain head reaches nothing leftward and the base extent is 0; only ink that
    /// genuinely hangs left of the head — accidentals, heads reversed left of the stem —
    /// contributes. Change items are the one branch still measured from their CENTRE
    /// (width/2); see COORDINATE_AUDIT §4.7.
    ///
    /// This summary previously read "from its reference point (notehead center)", which
    /// had been true before the base extent was converted to the left-edge basis but was
    /// stale afterwards — the frame it named was the opposite of the one it computed.
    ///
    /// LILYPOND-REF: lily/separation-item.cc:163-164 boxes — pure_y_extent over the column's x extent; the spacing box is
    /// <c>il-&gt;extent (pc, X_AXIS)</c>, i.e. taken in the PAPER COLUMN's frame.
    ///
    /// LILYPOND-REF: lily/accidental-placement.cc
    /// For chords with multiple accidentals, uses AccidentalPlacement to calculate
    /// the staggered/stacked positions, then returns the leftmost extent.
    /// </remarks>
    public static double CalculateLeftExtent(MusicItem item)
    {
        // A change grob's column origin is its ink LEFT edge — the same convention as a note
        // head's, verified against LilyPond in COORDINATE_AUDIT.md §4.7.2 — so it reaches
        // NOTHING to the left. These branches used to return `width/2 + ClefChangePadding`,
        // measuring the change from its CENTRE while every other grob was measured from its
        // left edge, and 0.5 of padding that is not a LilyPond quantity at all.
        if (IsChangeItem(item))
            return 0;

        // Get notehead metrics (note value determines which notehead glyph)
        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);

        // A notehead is drawn glyph-left-aligned at its column (the same convention the
        // rest branch below relies on, and the one LilyPond uses — a note column's
        // reference point coincides with the note head's LEFT edge: dumping
        // ly:grob-relative-coordinate for a PaperColumn and its NoteHead in 2.24.4
        // gives the same X). So a plain note reaches NOTHING to the left of its column,
        // and the base extent is 0; only ink that genuinely hangs left of the head —
        // accidentals, and heads reversed to the left of the stem — adds to it below.
        // Seeding this with the head's half-width (CenterX) treated the column as if it
        // were at the head's CENTRE, charging ~1 ss of phantom leftward reach for a
        // whole note; that is exactly the bug already called out for rests just below.
        double extent = 0;

        // A rest is drawn glyph-left-aligned at its column (DrawRest: DrawGlyph at x),
        // so its LEFTward reach from the column is the rest glyph's own left edge — NOT
        // the (wide) notehead box of the same duration. A whole-note notehead's centre
        // is ~1 ss, which pushed a lone whole rest ~1 ss right of beat 1, so `r1`
        // rendered near the measure centre instead of at its rhythmic moment. Mirror
        // CalculateNoteheadRightExtent, which already uses the rest glyph's right edge.
        // LILYPOND-REF: lily/rest.cc Rest::width — the rest stencil's own X-extent.
        if (item is RestItem)
        {
            return -GlyphMetrics.GetRestBBox(noteValue).Left;
        }

        // Handle accidentals
        if (item is ChordItem chord)
        {
            // Within-chord seconds: a head reversed to the LEFT of the stem
            // (stem down) extends the column's left ink even without
            // accidentals. LILYPOND-REF: lily/stem.cc:606-760 calc_positioning_done.
            double[] headOffsets = ChordHeadPositioning.CalculateOffsets(
                chord.Notes, chord.StemUp, noteValue);
            double minHeadOffset = headOffsets.Min();
            // The reversed head sits `minHeadOffset` (negative) from the column, so its
            // leftward reach is that offset's magnitude — measured from the column, not
            // from the head's centre (see the base-extent note above).
            if (minHeadOffset < 0)
                extent = Math.Max(extent, -minHeadOffset);

            // For chords, use AccidentalPlacement to calculate staggered positions —
            // unless the staff column packed them together with another voice's, in which
            // case that answer is already in THIS frame (the column's) and is the one drawn.
            double leftmost = 0;
            if (chord.HasPackedAccidentals)
            {
                leftmost = chord.Notes.Min(n => n.AccidentalX ?? 0);
            }
            else
            {
                var placement = new AccidentalPlacement();
                var layouts = placement.CalculatePositions(chord.Notes, headOffsets);
                if (layouts.Length > 0)
                    // XOffset is negative, representing distance to the left of notehead
                    leftmost = layouts.Min(l => l.XOffset);
            }
            // The leftmost extent is the absolute value of the offset
            if (leftmost < 0)
                extent = Math.Max(extent, -leftmost);
        }
        else if (item is NoteItem note && note.Accidental != null)
        {
            // Single note with accidental: reserve what the placement/drawing actually uses
            // (position_apes clears the note by right-padding + padding = 0.35, and a courtesy
            // adds its parenthesis ink), NOT a bare glyph width — otherwise the accidental,
            // and especially a courtesy's left parenthesis, spills left over the bar line.
            // A note sharing its column with another voice was packed against that voice's
            // accidentals too (StaffAccidentalColumns), and the packing is measured from
            // this same reference point.
            double? x = note.AccidentalX;
            if (x is null)
            {
                var placement = new AccidentalPlacement();
                x = placement.CalculateSinglePosition(note)?.XOffset;
            }
            if (x is { } offset && offset < 0)
                extent = Math.Max(extent, -offset);
        }

        return extent;
    }

    /// <summary>
    /// Gets the note value (1=whole, 2=half, 4=quarter, etc.) for a music item.
    /// </summary>
    internal static int GetNoteValue(MusicItem item)
    {
        // Clef/key/time change items have zero duration — treat as quarter note for glyph lookup
        if (item is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem)
            return 4;
        // The NOTATED value — the glyph that is DRAWN. Every consumer of this is a
        // drawn-ink reader (head/rest bboxes for extents, skylines and the left-head
        // refinement), and the scaled duration is the wrong frame for a glyph: a
        // tuplet whole (2/3 → denominator 3) priced as a BLACK head under 42 drawn
        // whole heads (books TSU/TSD, dumped 2026-08-21), and a dotted half (3/4 → 4)
        // as a black under a drawn half — MEASURED, dotted.natural.dotted-half-gap
        // −0.073200 = the half-vs-black head width, to the digit (LilyPond prices the
        // drawn stencil, note-spacing.cc:46-70 first_head). The renderer always read
        // the notated side (LayoutUtilities.GetNoteValueFromFraction over
        // BaseDuration, its 15 call sites); this was the drawn-ink walk that did not.
        return item switch
        {
            NoteItem n => LayoutUtilities.GetNoteValueFromFraction(n.BaseDuration),
            ChordItem c => LayoutUtilities.GetNoteValueFromFraction(c.BaseDuration),
            RestItem r => LayoutUtilities.GetNoteValueFromFraction(r.BaseDuration),
            _ => (int)item.Duration.Denominator,
        };
    }
}
