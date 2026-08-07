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
internal static class SpacingRules
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
    /// </remarks>
    internal static bool IsMusicalColumn(MusicItem? item) =>
        item is NoteItem or ChordItem or RestItem { IsSpacer: false };

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
    /// reservation (<see cref="CalculatePrefixWidth"/> → SolvePrefixColumns, the
    /// KeySignature group extent RIGHT) and the drawn prefix (SharedRenderer) both read
    /// it, so a custom (non-traditional) signature reserves exactly what it draws — the
    /// defect the ledger pair line-start.time-to-first-note.{standard,custom}-key opened
    /// was the reservation reading <c>Sharps</c> alone and dropping <c>Custom</c>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/break-alignment-interface.cc:141-142 — the group extent
    /// is the union of the engraved signatures' stencils; LilyPond has one key model
    /// (keyAlterations), so its reservation IS its drawing.</remarks>
    public static double KeySignatureInkWidth(KeySignature key)
    {
        if (key.Custom is { } custom)
        {
            double w = 0.0;
            foreach (var (_, alter) in KeySignature.DecodeCustom(custom))
                w += GlyphMetrics.GetKeySignatureAccidentalWidth(alter >= 0);
            return w;
        }
        if (key.Sharps == 0)
            return 0.0;
        return Math.Min(Math.Abs(key.Sharps), 7)
            * GlyphMetrics.GetKeySignatureAccidentalWidth(key.Sharps > 0);
    }

    public static double CalculatePrefixWidth(KeySignature key, bool includeTimeSignature,
        int timeSigBeats = 4, int timeSigBeatType = 4)
    {
        return BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.LineStartClefWidth(ClefType.Treble),
            KeySignatureInkWidth(key),
            includeTimeSignature, timeSigBeats, timeSigBeatType);
    }

    /// <summary>
    /// Calculates the width of system prefix with explicit clef width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc
    /// Use this overload when the clef type is known for accurate spacing.
    /// </remarks>
    public static double CalculatePrefixWidth(double clefWidth, KeySignature key,
        bool includeTimeSignature, int timeSigBeats = 4, int timeSigBeatType = 4)
    {
        return BreakAlignSpacing.CalculatePrefixWidth(
            clefWidth,
            KeySignatureInkWidth(key),
            includeTimeSignature, timeSigBeats, timeSigBeatType);
    }

    /// <summary>
    /// Calculates the width of a system prefix from the key column's own INK width — the
    /// break-align group's right edge (<see cref="WidestActiveKeyInk"/>), which is what a
    /// multi-staff system reserves: a union across staves is a width, not one staff's key.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/break-alignment-interface.cc:141-142,242.</remarks>
    public static double CalculatePrefixWidth(double clefWidth, double keyInkWidth,
        bool includeTimeSignature, int timeSigBeats = 4, int timeSigBeatType = 4)
    {
        return BreakAlignSpacing.CalculatePrefixWidth(
            clefWidth, keyInkWidth, includeTimeSignature, timeSigBeats, timeSigBeatType);
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
    /// LILYPOND-REF: lily/break-alignment-interface.cc:242 — the offset is
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
    /// <see cref="SharedRenderer"/>'s tab renderer draws <c>clefs.tab</c> unscaled at this
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
    /// LILYPOND-REF: lily/break-alignment-interface.cc:145-146,155-156 — a break-align group
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

    /// <summary>The Clef break-align group's left ink edge — <see cref="ClefGroupExtent"/>'s
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
    /// Whether this staff ENGRAVES a time signature — the same question
    /// <see cref="ContributesToKeyColumnWidth"/> asks for the key column, for the meter one.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:80 — <c>Staff \consists Time_signature_engraver</c>,
    /// and neither Lyrics (:632-649) nor ChordNames (:703-725) does. So a lyric / chord row
    /// has no TimeSignature grob at all and books nothing.
    /// <para>
    /// A TAB staff is NOT excluded here: its TimeSignature grob exists and sits in the shared
    /// meter column, it merely has no stencil (dumped as an EMPTY extent — the probe harness
    /// reports skipping <c>TKC TABTIME</c>). An all-tab score's meter is dropped by the
    /// separate <c>AllStavesTab</c> gate at the call sites, which is where that (Lily#-side)
    /// modelling of the missing stencil already lives.
    /// </para>
    /// </remarks>
    public static bool ContributesToTimeColumnWidth(Staff staff) => !staff.IsTextRow;

    /// <summary>
    /// Whether ANY staff in the score engraves a time signature. False for a system built
    /// only of chord / lyric rows, which is what stops the prefix booking a meter column no
    /// row draws.
    /// </summary>
    /// <remarks>
    /// MEASURED (audit/lp-geometry/probes/staffless-system.ly, scores CO and CO3): the same
    /// chords under 4/4 and under 3/4 put their first chord name on 0.500000 in both, to 15
    /// digits — LilyPond books no meter width because no context engraves one, while Lily#
    /// booked <c>GetTimeSigWidth(beats, beatType)</c>, which differs between the two.
    /// </remarks>
    public static bool AnyStaffEngravesTime(MultiStaffScore score)
    {
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
    /// LILYPOND-REF: lily/break-alignment-interface.cc:141-142 — the group extent unions
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
    /// LILYPOND-REF: lily/break-alignment-interface.cc:141-142 — a break-align group's
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
    /// </remarks>
    public static double ActiveKeyInkForStaff(
        MultiStaffScore score, Staff staff, int startMeasureIndex)
    {
        if (!ContributesToKeyColumnWidth(staff))
            return 0.0;

        var key = staff.PerStaffKeySignature ?? score.KeySignature;
        var pv = staff.PrimaryVoice;
        for (int m = 0; m < startMeasureIndex && m < pv.Measures.Length; m++)
            foreach (var item in pv.Measures[m].Items)
                if (item is KeySignatureChangeItem kc)
                    key = kc.NewKey;

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
    /// Width reserved at the END of a line for the courtesy cancellation +
    /// new key signature when the NEXT line opens with a key change (drawn
    /// after the line's final barline). Geometry mirrors
    /// SharedRenderer.DrawKeySignatureChange.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/key-engraver.cc + explicitKeySignatureVisibility
    /// (default all-visible) — a changed signature prints on BOTH sides of
    /// the break: courtesy at the old line's end, real one in the new
    /// line's prefix.
    /// </remarks>
    public static double KeyCourtesySuffixWidth(int prevSharps, int nextSharps)
    {
        int natCount = CancellationNaturalCount(prevSharps, nextSharps);

        // The bar line's gap to whichever grob OPENS the group — LilyPond keys the left grob's
        // alist by the RIGHT grob's break-align-symbol, so a group that opens with the
        // cancellation and one that opens with the signature read different entries (both 1.0
        // as it happens, and reading the right one is what keeps that a fact rather than luck).
        double w = BreakAlignGap(BreakAlignSymbol.StaffBar,
            natCount > 0 ? BreakAlignSymbol.KeyCancellation : BreakAlignSymbol.KeySignature);
        if (natCount > 0)
            // Upper bound of the LP natural kerning (0.3 per overlapping pair), then the
            // cancellation's own gap to the signature.
            w += natCount * GlyphMetrics.AccidentalNatural.Width
               + Math.Max(0, natCount - 1) * 0.3
               + BreakAlignGap(BreakAlignSymbol.KeyCancellation, BreakAlignSymbol.KeySignature);
        if (nextSharps != 0)
            w += Math.Abs(nextSharps) * GlyphMetrics.GetKeySignatureAccidentalWidth(nextSharps > 0) + 0.4;
        return w;
    }

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
    /// LILYPOND-REF: scm/define-grobs.scm:3922-3953 break-visibility — the TimeSignature
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
           + GlyphMetrics.GetTimeSigWidth(change.NewTime.Beats, change.NewTime.BeatType);

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
    /// LILYPOND-REF: lily/clef.cc:29-52 — "_change" suffix glyphs are smaller variants.
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
    /// <remarks>LILYPOND-REF: lily/separation-item.cc:166-167 — <c>Interval (-0.1, 0.1)</c>.</remarks>
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
    /// LILYPOND-REF: lily/spacing-spanner.cc:315-316 —
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
    /// LILYPOND-REF: scm/define-grobs.scm:40 Accidental
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
    /// LILYPOND-REF: lily/staff-spacing.cc:212-215 — "ensure that the 'fixed' distance
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
    /// LILYPOND-REF: scm/define-grobs.scm:3369 StaffSpacing
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

        // LILYPOND-REF: lily/key-engraver.cc:67-125 — cancellation logic
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
        GlyphMetrics.GetTimeSigWidth(timeChange.NewTime.Beats, timeChange.NewTime.BeatType);

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
    /// LILYPOND-REF: lily/separation-item.cc:163-164 boxes — the spacing box is
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
            // accidentals. LILYPOND-REF: lily/stem.cc:606-760.
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
        var duration = item.Duration;
        return (int)duration.Denominator;
    }
    /// <summary>
    /// Creates a spring between two music items.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:100-130 note_spacing()
    /// LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction()
    /// - ideal_distance = get_duration_space(duration)
    /// - min_distance = max(increment, skyline_collision_distance)
    /// - inverse_stretch_strength = max(0.1, ideal - min)
    /// - stem direction optical correction applied to ideal
    /// </remarks>
    public static Spring CreateSpring(MusicItem? prevItem, MusicItem? nextItem, Fraction prevDuration,
                                      NoteSpacingParameters? noteParams = null,
                                      double? baseShortestDuration = null)
    {
        var np = noteParams ?? NoteSpacingParameters.Default;

        // LILYPOND-REF: lily/spacing-basic.cc:109 note_spacing() - increment
        double defaultMin = EngravingDefaults.SpacingIncrement;

        // Skyline-based collision distance (rod)
        double skylineDistance = CalculateSkylineDistance(prevItem, nextItem, staffY: 0);

        // min_distance = max(defaultMin, skylineDistance) - ensures no collision
        double minDistance = Math.Max(defaultMin, skylineDistance);

        // LILYPOND-REF: lily/spacing-basic.cc:107 note_spacing() - duration space
        double idealDistance = CalculateDurationSpace(prevDuration,
            baseShortestDuration ?? EngravingDefaults.BaseShortestDuration);

        // --- Stem direction optical correction ---
        // LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction
        idealDistance += CalculateStemCorrection(prevItem, nextItem, np);

        // LILYPOND-REF: lily/note-spacing.cc:229-264 strict_note_spacing
        // In strict mode, enforce minimum distance = duration-based ideal distance.
        // This prevents compression below proportional spacing.
        if (np.StrictNoteSpacing)
        {
            minDistance = Math.Max(minDistance, idealDistance);
        }

        // A whole-display tremolo pair whose RIGHT half carries accidentals gets the
        // Beam's minimum-length as a spacing rod between its two columns, so the
        // gapped beam keeps printable width past the accidentals. MEASURED: the
        // accidental book's three such pairs space head-origin to head-origin at
        // exactly 6.00 (m2 27.93→33.93, m3 39.88→45.88, b''–cis''' 84.82→90.82);
        // the accidental-free pair keeps its natural 4.86.
        // LILYPOND-REF: lily/beam.cc:429-449 tremolo_springs_and_rods — gated on
        //   the accidentals and on duration_log <= 0, it calls set_spacing_rods;
        //   the distance is the Beam grob's minimum-length 6.0
        //   (scm/define-grobs.scm Beam) via lily/spanner.cc:429-473 set_spacing_rods.
        minDistance = Math.Max(minDistance, TremoloPairRod(prevItem, nextItem));

        // LILYPOND-REF: lily/spacing-basic.cc note_spacing()
        //   ret.set_inverse_stretch_strength(fraction * std::max(0.1, (len - min)));
        // where min = increment_ (NOT skyline min_distance).
        // Skyline min_distance is set later via set_min_distance() but does NOT
        // affect inverse_stretch_strength. This ensures accidentals (which increase
        // skyline min_distance) don't make springs stiffer — they stretch equally.
        double inverseStretchStrength = Math.Max(0.1, idealDistance - defaultMin);

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>The Beam grob's minimum-length (scm/define-grobs.scm Beam), the rod a
    /// whole-display tremolo pair with accidentals spans between its two columns.</summary>
    private const double TremoloPairAccidentalRod = 6.0;

    /// <summary>
    /// The tremolo pair's spacing rod between <paramref name="prev"/> and
    /// <paramref name="next"/>: the Beam's minimum-length (6.0) when they are the two
    /// halves of a whole-DISPLAY pair whose right half carries accidentals, else 0.
    /// One house for BOTH spring systems (this file's estimate and the
    /// timing-column layout), so the pair cannot drift.
    /// </summary>
    internal static double TremoloPairRod(MusicItem? prev, MusicItem? next) =>
        IsWholeTremoloPairStart(prev) && IsTremoloPairEndWithAccidentals(next)
            ? TremoloPairAccidentalRod
            : 0.0;

    /// <summary>The LEFT half of a whole-DISPLAY two-note tremolo pair (the only beam
    /// whose stems are invisible).</summary>
    private static bool IsWholeTremoloPairStart(MusicItem? item) => item switch
    {
        NoteItem { TremoloPairBeams: > 0, HasBeamStart: true } n =>
            GlyphMetrics.NoteValueOf(n.BaseDuration) <= 1,
        ChordItem { TremoloPairBeams: > 0, HasBeamStart: true } c =>
            GlyphMetrics.NoteValueOf(c.BaseDuration) <= 1,
        _ => false,
    };

    /// <summary>The RIGHT half of a two-note tremolo pair, carrying at least one
    /// accidental (LilyPond's get_accidentals reads the LAST stem's heads).</summary>
    private static bool IsTremoloPairEndWithAccidentals(MusicItem? item) => item switch
    {
        NoteItem { TremoloPairBeams: > 0, HasBeamEnd: true } n => n.Accidental != null,
        ChordItem { TremoloPairBeams: > 0, HasBeamEnd: true } c =>
            c.Notes.Any(x => x.Accidental != null),
        _ => false,
    };

    /// <summary>
    /// Calculates stem direction optical correction for spacing ([Wanske] p.138:
    /// up-stem→down-stem needs extra space, down-stem→up-stem less).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction:
    /// - opposite directions, BOTH STEMS IN ONE BEAM → the knee correction, which
    ///   REPLACES the overlap one: −note_head_width · rightDir ·
    ///   knee-spacing-correction (knee_correction, :117-137, selected at :288-293)
    /// - opposite directions otherwise → correction scales with the stems' vertical
    ///   OVERLAP: min(|overlap|/7, 1) · leftDir · stem-spacing-correction
    ///   (different_directions_correction, :140-160)
    /// - same direction → only when the head ranges do NOT overlap and the gap
    ///   exceeds one staff position: ±same-direction-correction depending on
    ///   which side is lower (same_direction_correction, :162-197); skipped
    ///   when an accidental sticks out of the right side (:305-308)
    /// Simplification vs LilyPond: the flagged-unbeamed-left gate (:264-266) is not
    /// applied. Stem directions ARE beam-resolved — the collector bakes the beam's
    /// direction, and its identity (<see cref="NoteItem.BeamId"/>), into the items.
    /// </remarks>
    internal static double CalculateStemCorrection(MusicItem? prevItem, MusicItem? nextItem,
                                                   NoteSpacingParameters noteParams)
    {
        if (StemSpacingInfo(prevItem) is not { } l || StemSpacingInfo(nextItem) is not { } r)
            return 0;

        int leftDir = l.StemUp ? 1 : -1;
        int rightDir = r.StemUp ? 1 : -1;

        if (leftDir != rightDir)
        {
            // LILYPOND-REF: note-spacing.cc:288-293 knee_correction replaces
            // different_directions_correction — inside ONE beam the knee branch takes over
            // entirely (LilyPond writes it as an if/else, not as a sum).
            if (l.BeamId is { } leftBeam && leftBeam == r.BeamId)
                return KneeCorrection(nextItem, rightDir, noteParams);

            // LILYPOND-REF: note-spacing.cc:140-160 different_directions_correction
            double lo = Math.Max(l.StemMin, r.StemMin);
            double hi = Math.Min(l.StemMax, r.StemMax);
            if (hi <= lo)
                return 0;
            // Overlap in staff positions (half-spaces); 7 is LilyPond's hardcoded scale.
            return Math.Min((hi - lo) / 7.0, 1.0) * leftDir * noteParams.StemSpacingCorrection;
        }

        // LILYPOND-REF: note-spacing.cc:305-308 — same-direction correction only
        // without accidentals sticking out of the right hand side.
        if (HasAccidental(nextItem))
            return 0;

        // LILYPOND-REF: note-spacing.cc:162-197 same_direction_correction —
        // applies only when the two head ranges are disjoint by more than one
        // staff position; sign depends on which side is lower.
        bool headsOverlap = Math.Max(l.HeadMin, r.HeadMin) <= Math.Min(l.HeadMax, r.HeadMax);
        if (headsOverlap)
            return 0;

        int lowest = l.HeadMin > r.HeadMax ? 1 : -1; // +1 = RIGHT side is lower
        double delta = lowest > 0 ? l.HeadMin - r.HeadMax : r.HeadMin - l.HeadMax;
        return delta > 1 ? -lowest * noteParams.SameDirectionCorrection : 0;
    }

    /// <summary>
    /// The optical correction for a KNEE — two columns of one beam whose stems point
    /// opposite ways. Unlike the overlap correction it does not scale with anything the
    /// two stems share: it is one note-head width, signed by the RIGHT stem's direction,
    /// so an up→down pair is pushed apart by as much as a down→up pair is pulled together.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:117-137 knee_correction —
    /// <c>-note_head_width * get_grob_direction (right_stem) * knee-spacing-correction</c>,
    /// where note_head_width is the right stem's SUPPORT HEAD extent[RIGHT] taken in the
    /// column's frame, less the stem's own thickness (:131), and the spacing increment
    /// stands in when that stem has no head at all (:120).
    /// <para>
    /// MEASURED (2.26.0, audit/lp-geometry/probes/beam-column-spacing.ly): for a black head
    /// the term is 1.304200 − 0.130000 = 1.174200, and LilyPond's kneed bar
    /// <c>c'8 c' c' c'''</c> has column gaps 2.5042 / 2.5042 / 3.6784 — the last one wide by
    /// exactly that. Perturbing knee-spacing-correction to 0 / 0.5 / 2 moves both signs of
    /// the term in proportion (2.5042 flat / ±0.5871 / +2.3484), which is what says this is
    /// the term and not the overlap branch beside it: that branch never reads this property.
    /// The down→up gap saturates at 1.8042 under a large correction because the spring's
    /// MINIMUM distance stops it — the rod, not this term.
    /// </para>
    /// </remarks>
    private static double KneeCorrection(MusicItem? rightItem, int rightDir,
                                         NoteSpacingParameters noteParams)
    {
        // LILYPOND-REF: note-spacing.cc:120 knee_correction's note_head_width seed — the
        // spacing increment (Spacing_options::increment_) when the stem carries
        // no head. Written as LilyPond writes it. Nothing head-less reaches here today
        // (StemSpacingInfo already returned null for it), but that is a property of this
        // caller, not of the rule.
        double noteHeadWidth = EngravingDefaults.SpacingIncrement;

        if (SupportHeadRightExtent(rightItem) is { } headRight)
        {
            noteHeadWidth = headRight;
            // LILYPOND-REF: note-spacing.cc:131 note_head_width -= Stem::thickness (right_stem)
            // — and the stem's thickness is a LINE thickness, not a head quantity:
            // LILYPOND-REF: lily/stem.cc:909-913 Stem::thickness = thickness · line_thickness
            // (scm/define-grobs.scm:3469 Stem (thickness . 1.3) over the 0.1 ss line).
            noteHeadWidth -= EngravingDefaults.StemThickness;
        }

        return -noteHeadWidth * rightDir * noteParams.KneeSpacingCorrection;
    }

    /// <summary>
    /// The right edge of the stem's support head, measured in its COLUMN's frame — the
    /// quantity <c>head-&gt;extent (head-&gt;get_column (), X_AXIS)[RIGHT]</c> reads. Null
    /// for an item with no head.
    /// </summary>
    /// <remarks>
    /// The support head is the one the stem starts from —
    /// LILYPOND-REF: lily/stem.cc:179-204 Stem::support_head, the head with the widest part
    /// inside the stem, which for a chord of one glyph is the first, i.e. the extreme head
    /// in the stem's direction. That head
    /// is never the displaced one: <see cref="ChordHeadPositioning"/> gives it offset 0 and
    /// walks the reversals off it, so its column-frame right edge is the head glyph's own
    /// right edge — the same <c>ell</c> that file takes from stem.cc:684.
    /// </remarks>
    private static double? SupportHeadRightExtent(MusicItem? item) => item switch
    {
        NoteItem or ChordItem => GlyphMetrics.GetNoteheadBBox(GetNoteValue(item)).Right,
        _ => null,
    };

    /// <summary>
    /// Stem-direction optical correction for the spring that runs from a note column
    /// INTO a bar line, where the bar line stands in for the right-hand stem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:281-286 stem_dir_correction — when the right
    /// column carries a bar line, LilyPond synthesises the right-hand stem from the bar:
    /// <code>
    ///   stem_dirs[RIGHT] = -stem_dirs[LEFT];
    ///   stem_posns[RIGHT] = bar_yextent;
    ///   stem_posns[RIGHT] *= 2;
    /// </code>
    /// so the directions are opposite BY CONSTRUCTION and
    /// different_directions_correction always runs, then is HALVED (:299-300).
    /// ⚠️ THE THREE ADDRESSES IN THIS BLOCK WERE ALL WRONG UNTIL 2026-08-04 (session 84),
    /// each by about thirty-eight lines: :243-248 is the accidentals check and the stemless
    /// guard, :263-264 is the large-flag gate, and :200-201 is a comment. The citation
    /// ratchet only checks that a symbol name shares the line with an address, so a stale
    /// range that names the right function survives it — the range has to be opened.
    /// LILYPOND-REF: lily/staff-spacing.cc:73 Staff_spacing::bar_y_positions — the bar's Y extent divided
    /// by the staff space, i.e. staff-spaces; the <c>*= 2</c> above converts it to staff
    /// POSITIONS (half-spaces), the unit StemSpacingInfo already reports.
    ///
    /// A plain bar line spans the staff, so on a normal five-line staff that extent is
    /// ±2 staff-spaces → ±4 staff positions. (LilyPond takes this from the bar grob and
    /// only for glyphs beginning "|" or "."; this path is the ordinary staff bar, and
    /// like the item→bar-line skyline beside it, it assumes the standard staff.)
    ///
    /// Returns 0 when the left column has no visible stem — a whole note or a rest —
    /// which is LilyPond's `if (!stem || Stem::is_invisible (stem)) return;` (:248-249)
    /// and is why `c'1 c'1` needs no correction at all.
    /// </remarks>
    internal static double CalculateStemCorrectionToBarline(
        MusicItem? prevItem, NoteSpacingParameters noteParams)
    {
        if (StemSpacingInfo(prevItem) is not { } l)
            return 0;

        // The bar line's Y extent in staff positions: the staff's own half-height.
        const double barHalfHeightPositions = 4.0;

        int leftDir = l.StemUp ? 1 : -1;
        double lo = Math.Max(l.StemMin, -barHalfHeightPositions);
        double hi = Math.Min(l.StemMax, barHalfHeightPositions);
        if (hi <= lo)
            return 0;

        double correction =
            Math.Min((hi - lo) / 7.0, 1.0) * leftDir * noteParams.StemSpacingCorrection;

        // LILYPOND-REF: note-spacing.cc:263-264 — halved when the right side is a bar.
        return correction * 0.5;
    }

    /// <summary>
    /// Merges the per-voice stem-direction spacing wishes for the column pair
    /// (<paramref name="tLeft"/> → <paramref name="tRight"/>) into a single spring.
    /// Each voice with a note/chord column at BOTH moments contributes one wish:
    /// the duration-proportional <paramref name="baseSpring"/> refined by that
    /// voice's stem-direction correction. The wishes are combined with
    /// <see cref="Spring.MergeSprings"/>, exactly as LilyPond merges the simultaneous
    /// voices' spacing wishes for a musical column pair.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:322-393 Spacing_spanner::musical_column_spacing
    ///   — collect each voice's Note_spacing wish, then <c>spring = merge_springs (springs)</c>.
    /// LILYPOND-REF: lily/spring.cc:101-131 merge_springs.
    /// For monophonic music exactly one voice contributes, so the result equals
    /// that single wish (base + its own correction) — identical to applying the
    /// correction directly, which keeps all single-voice spacing unchanged.
    /// </remarks>
    internal static Spring MergeVoiceStemWishes(
        Spring baseSpring, IReadOnlyList<Measure> voices,
        Fraction tLeft, Fraction tRight, NoteSpacingParameters noteParams)
    {
        var wishes = new List<Spring>();
        foreach (var voice in voices)
        {
            var left = NoteColumnAt(voice, tLeft);
            var right = NoteColumnAt(voice, tRight);
            if (left is null || right is null)
                continue;

            double corr = CalculateStemCorrection(left, ApproachColumn(right), noteParams);
            // LILYPOND-REF: lily/note-spacing.cc:111-113 — stem_dir_correction adjusts the
            // ideal and hands it to base.set_ideal_distance, which does not touch either
            // strength (lily/spring.cc:131-141).
            wishes.Add(corr != 0
                ? baseSpring.WithIdealDistance(
                    Math.Max(baseSpring.MinDistance, baseSpring.IdealDistance + corr))
                : baseSpring);
        }
        return wishes.Count > 0 ? Spring.MergeSprings(wishes) : baseSpring;
    }

    /// <summary>
    /// Merges the per-voice stem-direction spacing wishes for the pair
    /// (last note column at <paramref name="tLeft"/> → the bar line) into a single
    /// spring — the bar-line counterpart of <see cref="MergeVoiceStemWishes"/>.
    /// Every voice sounding a note/chord column at that moment contributes one wish:
    /// the base spring refined by that voice's correction against the bar line's
    /// virtual stem.
    /// </summary>
    /// <remarks>
    /// LilyPond runs the note → bar-line pair through the SAME per-voice merge as a
    /// note → note pair: spacing-spanner.cc:183-199 generate_pair_spacing dispatches on
    /// the LEFT column being musical, so a musical → breakable pair also goes to
    /// musical_column_spacing (:322-393), which collects one Note_spacing wish per voice
    /// and ends in <c>merge_springs</c>. The wish itself carries the bar-line branch of
    /// the stem correction (note-spacing.cc:243-264), ported as
    /// <see cref="CalculateStemCorrectionToBarline"/>.
    ///
    /// Verified on LilyPond 2.24.4, last-column → bar-line-column distance over one 4/4
    /// bar of quarters: stems up throughout 3.393249, stems down throughout 3.192257,
    /// and the two as simultaneous voices 3.292753 — exactly their average, which is
    /// what merge_springs does when the wishes share a min distance.
    ///
    /// This depends on the voice-forced stem directions being resolved into the model
    /// before spacing (MeasureCollector.ResolveVoiceStemDirections); with the
    /// pitch-derived directions it saw previously, the merge moved the spring the wrong
    /// way in polyphony.
    ///
    /// LILYPOND-REF: lily/note-spacing.cc:113 — the corrected ideal is clamped at 0.0
    /// (not at the min distance), matching the single-voice path this replaces.
    /// </remarks>
    internal static Spring MergeVoiceStemWishesToBarline(
        Spring baseSpring, IReadOnlyList<Measure> voices,
        Fraction tLeft, NoteSpacingParameters noteParams)
    {
        var wishes = new List<Spring>();
        foreach (var voice in voices)
        {
            if (NoteColumnAt(voice, tLeft) is not { } left)
                continue;

            double corr = CalculateStemCorrectionToBarline(left, noteParams);
            // LILYPOND-REF: lily/note-spacing.cc:111-113, as in MergeVoiceStemWishes.
            wishes.Add(corr != 0
                ? baseSpring.WithIdealDistance(Math.Max(0, baseSpring.IdealDistance + corr))
                : baseSpring);
        }
        return wishes.Count > 0 ? Spring.MergeSprings(wishes) : baseSpring;
    }

    /// <summary>
    /// The optical correction a DOWN stem standing just after a bar line earns, taken as
    /// the maximum over the columns at that moment.
    /// </summary>
    /// <remarks>
    /// "A stem following a bar-line creates an optical illusion similar to the one
    /// mentioned in note-spacing.cc. We correct for it here." The correction is the length
    /// of the overlap between the stem and the bar line, over 7, clamped to 1, times
    /// StaffSpacing's stem-spacing-correction — and it applies ONLY to a down stem, so an
    /// up stem after a bar line earns nothing.
    /// <para>
    /// UNITS: staff-spacing works in staff-SPACES here (it divides the bar's Y extent by
    /// the staff space, giving ±2), whereas note-spacing.cc multiplies that same extent by
    /// 2 and works in staff POSITIONS. Both then divide by 7, so the two are NOT
    /// interchangeable — see CalculateStemCorrectionToBarline, which is the positions one.
    /// StemSpacingInfo reports positions, hence the halving below.
    /// </para>
    /// <para>
    /// Verified on 2.24.4, bar-line ink right edge → next notehead ink left edge with
    /// `c'4 d' e' f'` before the bar line:
    ///   `g'4 a' b' c''`            up stems            0.900000  (correction 0)
    ///   `\clef bass c,4 d, e, f,`  up stems, clef      0.900000  (correction 0)
    ///   `a''4 b'' c''' d'''`       down, head pos 6    1.042857  (correction 0.142857)
    ///   `\clef bass g4 a b c'`     down, head pos 3    1.089365  (correction 0.189365)
    /// The last two reproduce exactly: pos 6 gives a stem spanning (-0.5, 2.813894) ss,
    /// clipped by the bar to (-0.5, 2.0), length 2.5 → 2.5/7 × 0.4 = 0.14285714; pos 3
    /// gives (-2.0, 1.313894), already inside the bar, length 3.313894 → 0.18936537.
    /// This also disproves the reading that the residual came from the CLEF: a clef with
    /// up stems earns nothing, and a down stem with no clef earns a third value.
    /// </para>
    /// LILYPOND-REF: lily/staff-spacing.cc:36-67 optical_correction, :69-93
    ///   bar_y_positions, :95-110 next_notes_correction, :206-208 (applied to BOTH
    ///   fixed and ideal).
    /// </remarks>
    internal static double BarlineToNextNotesCorrection(IReadOnlyList<MusicItem>? nextItems)
    {
        if (nextItems == null)
            return 0;
        double maxOptical = 0;
        foreach (var item in nextItems)
            maxOptical = Math.Max(maxOptical, BarlineToStemOpticalCorrection(item));
        return maxOptical;
    }

    /// <remarks>LILYPOND-REF: lily/staff-spacing.cc:43-67 Staff_spacing::optical_correction.</remarks>
    private static double BarlineToStemOpticalCorrection(MusicItem? item)
    {
        if (StemSpacingInfo(item) is not { } s)
            return 0;

        // LILYPOND-REF: lily/staff-spacing.cc:55 — `d == DOWN` only.
        if (s.StemUp)
            return 0;

        // A plain bar line spans the staff: ±2 staff-spaces, i.e. ±4 staff positions.
        // LILYPOND-REF: lily/staff-spacing.cc:78-90 bar_y_positions — only for glyphs
        //   beginning "|" or "."; an empty interval yields no correction at all.
        const double barHalfHeightPositions = 4.0;

        double lo = Math.Max(s.StemMin, -barHalfHeightPositions);
        double hi = Math.Min(s.StemMax, barHalfHeightPositions);
        if (hi <= lo)
            return 0;

        // Positions → staff-spaces, because this formula is the staff-spacing one.
        double overlapStaffSpaces = (hi - lo) / 2.0;
        return Math.Min(Math.Abs(overlapStaffSpaces / 7.0), 1.0) * StaffSpacingStemCorrection;
    }

    /// <summary>
    /// The note or chord column starting exactly at moment <paramref name="t"/> in
    /// <paramref name="measure"/>, or null if that voice rests (or has no column)
    /// there. Zero-duration change items sharing the moment are skipped.
    /// </summary>
    private static MusicItem? NoteColumnAt(Measure measure, Fraction t)
    {
        var cur = Fraction.Zero;
        foreach (var item in measure.Items)
        {
            if (cur == t && item is NoteItem or ChordItem)
                return item;
            if (cur > t)
                return null;
            cur += item.Duration;
        }
        return null;
    }

    /// <summary>
    /// Stem and head vertical ranges (staff positions, +up) used by the stem
    /// direction correction, and the identity of the beam the stem hangs from
    /// (<see cref="NoteItem.BeamId"/>; null when unbeamed). Null for stemless items
    /// (rests, whole notes) — LilyPond's <c>if (!stem || Stem::is_invisible (stem))
    /// return;</c> at note-spacing.cc:248-249.
    /// <para>
    /// The SAME range is the stem's box in the column's horizontal skyline
    /// (<see cref="ItemSkylineFactory"/>) — LilyPond reads one Y-extent for both, so
    /// this stays one house. The two are otherwise unrelated mechanisms: the optical
    /// correction moves the spring's IDEAL, the skyline sets its MINIMUM.
    /// </para>
    /// <para>
    /// ✔ A CUE ITEM GETS A CUE RANGE (session 85). It used to get a full-size one, which the
    /// ledger measured: LilyPond's cue stem is shorter — dumped, its Y-extent is
    /// (0.0 . 2.4052059400555286) against a full stem's (−1.0 . 2.3138) — so at a bar line it
    /// overlaps the bar's band by 4 staff positions where the full stem overlaps by 6, and the
    /// correction differs by exactly (6−4)/7 × 0.25 = 1/14. That 1/14 was the whole of what
    /// ledger <c>cue.barline.prev.cue-head</c> recorded beyond the metrics table's rounding.
    /// MEASURED, DRIVEN AND CONTROLLED: probe voice-boundary-spacing.ly section D.
    /// </para>
    /// <para>
    /// ✔ THE LAW IS MEASURED (probe section E), and it is NOT "multiply the range by
    /// magstep(−4)". LILYPOND-REF: ly/engraver-init.ly:436 CueVoice — <c>\override
    /// Stem.length-fraction = #(magstep -4)</c>, applied at lily/stem.cc:557
    /// <c>length *= length-fraction</c> AFTER the shortening at :541-554. Three parts, and
    /// only the first is a scale — the port spends one line on each:
    /// <list type="number">
    /// <item>the length from the head's CENTRE is <c>(7 − shorten) × magstep(−4)</c> — measured
    ///   on a middle-line note as 6.666666666666667 × magstep = 4.199736832982911, equal to the
    ///   dumped value as doubles. <see cref="EngravingDefaults.CueStemDetails"/> carries the
    ///   fraction into the ONE length house;</item>
    /// <item>the rule that a stem outside the staff reaches the middle line then FLOORS it.
    ///   It is inactive at full size and active once scaled: a scaled g′′ stem would stop at
    ///   +0.590276325367943 and LilyPond stops it at 0.
    ///   <see cref="StemCalculator.CalculateStemEndY"/> already ran that rule after the length,
    ///   so it needed nothing — but Lily#'s OWN 2.5 floor below it did (see there);</item>
    /// <item><c>stem-begin-position</c> does NOT scale — 0.3724 → 0.18958811988894286, a
    ///   ratio of 0.509098 against magstep's 0.629961. It is the design-13 glyph's own
    ///   attachment, exactly as the head WIDTH is (0.815348908 is not 1.304200 × magstep), and
    ///   <see cref="StemBeginPosition"/> asks the cue FONT for it.</item>
    /// </list>
    /// ⚠️ EIGHTHS ARE STILL OPEN: a flagged stem is lengthened to carry its flag and the flag
    /// scales by its own font size, so the two terms are not one product and the probe does not
    /// separate them (4.039985 measured against this fraction's 4.252234 — nearer than the
    /// 6.750000 a full-size stem gave, and still not the law). Measure the flag term before
    /// porting anything about eighths.
    /// ⚠️ BEAMED CUES ARE UNTOUCHED: a beamed stem ends on the quanter's beam, and
    /// <see cref="BeamScoringProblem"/> takes a <c>lengthFraction</c> that only the grace and
    /// tab callers pass. This method's <c>BeamId</c> hands beamed columns to that path.
    /// </para>
    /// </para>
    /// </summary>
    internal static (bool StemUp, double StemMin, double StemMax, double HeadMin, double HeadMax,
                    int? BeamId)?
        StemSpacingInfo(MusicItem? item)
    {
        switch (item)
        {
            case NoteItem n:
            {
                int noteValue = n.BaseDuration.Denominator;
                if (n.BaseDuration.Numerator != 1) noteValue = 1;
                if (noteValue < 2)
                    return null; // whole notes have no stem (Stem::is_invisible)
                // The stem's y-extent runs from where it MEETS THE HEAD (not the head
                // centre) to the tip; the head-side end sits a stem-attachment offset
                // off centre. LILYPOND-REF: lily/stem.cc:934-963.
                double beginPos = StemBeginPosition(n.StaffPosition, n.StemUp, noteValue, n.IsCue);
                double endPos = StemEndPosition(n.StaffPosition, n.StemUp, noteValue, n.StaffPosition, n.IsCue);
                return (n.StemUp,
                    Math.Min(beginPos, endPos), Math.Max(beginPos, endPos),
                    n.StaffPosition, n.StaffPosition, n.BeamId);
            }
            case ChordItem c when c.Notes.Length > 0:
            {
                int noteValue = c.BaseDuration.Denominator;
                if (c.BaseDuration.Numerator != 1) noteValue = 1;
                if (noteValue < 2)
                    return null;
                int minPos = c.Notes.Min(x => x.StaffPosition);
                int maxPos = c.Notes.Max(x => x.StaffPosition);
                int tipPos = c.StemUp ? maxPos : minPos;
                // Head-side end: the reference head is the one the stem starts from
                // (lowest for an up stem, highest for a down stem), offset by the
                // stem attachment. LILYPOND-REF: lily/stem.cc:934-963.
                double beginPos = StemBeginPosition(c.StemUp ? minPos : maxPos, c.StemUp, noteValue, c.IsCue);
                double endPos = StemEndPosition(tipPos, c.StemUp, noteValue, tipPos, c.IsCue);
                return (c.StemUp,
                    Math.Min(beginPos, endPos), Math.Max(beginPos, endPos),
                    minPos, maxPos, c.BeamId);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Where the stem MEETS THE HEAD, in staff positions (+up) — the head-side end of
    /// the stem's y-extent. Not the head centre: the stem attaches a fraction of the
    /// head height off centre (up-stem above, down-stem below).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:934-963 internal_calc_stem_begin_position — :950 takes the
    ///   reference head's staff position, :954-959 add
    ///   <c>head_height.linear_combination (y_attach) * 2 / ss</c>.
    /// LILYPOND-REF: lily/note-head.cc:164-196 get_stem_attachment — the OTHER half of the
    ///   round trip: LilyPond NORMALISES the font's attachment point out of the FONT's box
    ///   (<c>att = 2 (wxwy − centre) / length</c>, over
    ///   <c>b = fm-&gt;get_indexed_char_dimensions (k)</c>), and stem.cc:954-959 puts it back
    ///   through <c>head-&gt;extent (head, Y_AXIS)</c> — the GROB's extent.
    /// <para>
    /// ⚠️ THIS METHOD IS NOT A LINE-FOR-LINE PORT, and the licence for collapsing it is a
    /// MEASUREMENT rather than an algebraic identity. LilyPond's two steps read TWO DIFFERENT
    /// EXPRESSIONS for the head's box — the font's char dimensions on the way out, the grob's
    /// Y-extent on the way back — and they cancel only when those two agree. For a plain
    /// notehead they do, and that was measured, not assumed: probe
    /// notehead-stem-attachment.ly dumps the grob extent as ±0.545 against the font table's
    /// ±0.545, and the resulting stem-begin-position as the attachment point itself. So this
    /// returns the attachment point directly. ⇒ IF A HEAD EVER GETS A STENCIL EXTENT THAT IS
    /// NOT ITS FONT BOX (a styled head, a scaled stencil, a grob with a custom stencil), this
    /// collapse stops being valid and the two steps have to be spelled out. Lily# has ONE box
    /// per head today, which is why writing them out would read as a round trip to nowhere.
    /// </para>
    /// <para>
    /// ✔ MEASURED ON THE LILYPOND THIS CORPUS USES (2.26.0), probe
    /// notehead-stem-attachment.ly, session 85 — both heads, on the middle line where the head
    /// position contributes nothing, and again one space lower as the falsifier:
    /// <code>
    ///                 stem-attachment       Y-extent   stem-begin-position
    ///   black s2      0.341651376146789     ±0.545     −0.372400   (= 0.186200 × 2)
    ///   half  s1      0.475229357798165     ±0.545     −0.518000   (= 0.259000 × 2)
    ///   the same heads at staff position −2, stem down:  −0.6276 and −0.4820
    ///                                                    (= −2 + the offset, to 15 digits)
    /// </code>
    /// ⚠️ THIS METHOD HELD TWO SPELLINGS OF THAT ONE QUANTITY UNTIL THE PROBE ABOVE. It kept
    /// LilyPond's normalised attachment as a pair of constants (black 0.34147639283381404,
    /// half 0.4752405486932206) and put them back through our own box, which gave 0.372209268
    /// and 0.518012198 — near, and not the dumps. THE BOX WAS NEVER THE PROBLEM: ours is
    /// ±0.545 and so is LilyPond's. The constants were, and their own doc comment said why —
    /// they were "dumped on LilyPond 2.24.4", and 2.26.0 REBUILT Emmentaler. A number copied
    /// out of one release and read against another font is stale in a way no arithmetic can
    /// show; only asking the version in use can. Reading the font removes the vintage
    /// entirely — there is now nothing here to go stale when the bundled font moves.
    /// </para>
    /// <para>
    /// ⚠️ THE <c>dir *</c> BELOW IS A SECOND DEPARTURE, and it is measured too. LilyPond does
    /// not multiply by the direction: <c>get_stem_attachment</c> asks the font for a point PER
    /// DIRECTION (<c>fm-&gt;attachment_point (key, dir)</c>) and negates only when that lookup
    /// says <c>rotate</c> (note-head.cc:182-192), so a font is free to give a head a down
    /// attachment that is not the mirror of its up one. Lily#'s extracted table holds the UP
    /// point only, so a sign flip is all it CAN do. That it is the right answer for these two
    /// heads was measured rather than assumed — the probe reads both directions:
    /// <c>(1.0 . 0.341651376146789)</c> for the up stem against
    /// <c>(−1.0 . −0.341651376146789)</c> for the down, and the begin positions come out
    /// −2 + 0.3724 and 0 − 0.3724. ⇒ The limit is that it has been measured for
    /// <c>noteheads.s1</c> and <c>s2</c> and nothing else; a head style whose font disagrees
    /// would need the down point extracted.
    /// </para>
    /// <para>
    /// A CUE HEAD THEREFORE NEEDS NO SPECIAL CASE, only its own font: design 13's attachment
    /// is 0.150476, and at magstep(−4) that is LilyPond's 0.18958811988894286 (probe
    /// voice-boundary-spacing.ly section E (3)). Scaling design 20's 0.3724 would have given
    /// 0.234598 and been wrong — the head WIDTH is not a scale either.
    /// </para>
    /// </remarks>
    private static double StemBeginPosition(int headPosition, bool stemUp, int noteValue,
        bool isCue = false)
    {
        var font = isCue ? EngravingDefaults.CueFont : GlyphMetrics.Design20;
        int dir = stemUp ? 1 : -1;
        return headPosition
               + dir * GlyphMetrics.GetNoteheadStemAttachment(font, noteValue).Y * 2.0;
    }

    /// <summary>
    /// Unbeamed stem end in staff positions (+up), via the LilyPond stem-length
    /// rules (stem.cc internal_calc_stem_end_position).
    /// </summary>
    private static double StemEndPosition(int attachPos, bool stemUp, int noteValue,
        int staffPosition, bool isCue = false)
    {
        // StemCalculator works in the renderer's Y-down staff-space frame with
        // the staff middle at staffTopDown + 2; use middle = 0 → staffTopDown = −2.
        double attachY = -attachPos * 0.5;
        double endY = StemCalculator.CalculateStemEndY(
            attachY, stemUp, staffTopDown: -2.0,
            StemCalculator.GetDurationLog(noteValue), staffPosition,
            isCue ? EngravingDefaults.CueStemDetails : null);
        return -endY * 2.0;
    }

    /// <summary>
    /// Calculates the duration-based space using the global default base shortest duration.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-options.cc:72-107 get_duration_space()
    /// Uses EngravingDefaults.BaseShortestDuration (3/16). For score-specific spacing,
    /// use the overload that accepts a baseShortestDuration parameter from
    /// CalculateCommonShortestDuration().
    /// </remarks>
    public static double CalculateDurationSpace(Fraction duration)
    {
        return CalculateDurationSpace(duration, EngravingDefaults.BaseShortestDuration);
    }

    /// <summary>
    /// Calculates the duration-based space with a specific base shortest duration.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-options.cc:72-107 get_duration_space()
    /// LILYPOND-REF: lily/spacing-spanner.cc
    /// - ratio = duration / base_shortest_duration
    /// - if ratio less than 1: space = (shortest_duration_space + ratio - 1) * increment
    /// - if ratio >= 1: space = (shortest_duration_space + log2(ratio)) * increment
    ///
    /// The baseShortestDuration should come from CalculateCommonShortestDuration()
    /// which scans all voices to find the actual shortest note in the score.
    /// </remarks>
    public static double CalculateDurationSpace(Fraction duration, double baseShortestDuration)
    {
        double durationValue = duration.ToDouble();

        if (durationValue <= 0)
            return EngravingDefaults.SpacingIncrement;

        // Ratio of this duration to base shortest
        double ratio = durationValue / baseShortestDuration;

        // LILYPOND-REF: lily/spacing-options.cc:72-107 get_duration_space()
        double spaceFactor;
        if (ratio < 1.0)
        {
            // Linear scaling for very short notes
            spaceFactor = EngravingDefaults.ShortestDurationSpace + ratio - 1.0;
        }
        else
        {
            // Logarithmic scaling (Gourlay algorithm)
            spaceFactor = EngravingDefaults.ShortestDurationSpace + Math.Log2(ratio);
        }

        // Result in staff spaces: spaceFactor * increment
        return spaceFactor * EngravingDefaults.SpacingIncrement;
    }

    // ---------- Multi-measure rest: LilyPond's run-level spacing rod ----------

    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2375 MultiMeasureRest (space-increment . 2.0).</remarks>
    private const double MmrSpaceIncrement = 2.0;

    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2370 MultiMeasureRest (bound-padding . 0.5).</remarks>
    private const double MmrBoundPadding = 0.5;

    /// <summary>
    /// Width of the multi-measure rest symbol at zero available space — LilyPond's
    /// <c>symbol_stencil (me, 0.0)</c>, the value its spacing rod is built from.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/multi-measure-rest.cc:166-189 Multi_measure_rest::symbol_stencil
    /// LILYPOND-REF: lily/multi-measure-rest.cc:226-329 Multi_measure_rest::church_rest
    ///
    /// church_rest with <c>space == 0</c>: <c>inner_padding = (space - symbols_width) /
    /// (2*1.5 + (symbol_count-1))</c> goes negative, so the guard resets it to 1.0 (and
    /// min() against max-symbol-separation 8.0 leaves it at 1.0). The stencil is then
    /// <c>symbols_width + inner_padding * (symbol_count - 1)</c>; left_offset only
    /// translates. Verified against LP: measure-count 2 → one breve rest, 0.600.
    ///
    /// The decomposition mirrors <see cref="Rendering.SharedRenderer"/>'s church rest
    /// so rod and drawing agree. It walks maxima 8 / longa 4 / breve 2 / whole 1,
    /// which is church_rest's loop: <c>dl</c> starts at -3 and only ever increases,
    /// emitting <c>2^-dl</c> measures while the remainder still covers it. With
    /// expand-limit 10 the maxima can only appear at counts 8, 9 and 10 —
    /// 8 = maxima, 9 = maxima + whole, 10 = maxima + breve. Decomposing those into
    /// longas instead (4+4, 4+4+1, 4+4+2) spent one glyph too many and made the rod
    /// 0.4 ss too wide.
    /// </remarks>
    internal static double MmrSymbolWidth(int measureCount)
    {
        if (measureCount <= 0)
            return 0;

        if (measureCount > MultiMeasureRestEngraver.ExpandLimit)
        {
            // LILYPOND-REF: lily/multi-measure-rest.cc:194-215 big_rest (me, 0.0) —
            // the filled box collapses to zero width and only the two hair-thickness
            // end caps remain.
            return 2 * EngravingDefaults.MultiMeasureRestHairThickness;
        }

        double symbolsWidth = 0;
        int symbolCount = 0;
        int remaining = measureCount;
        foreach (var (span, width) in new[]
        {
            (8, GlyphMetrics.RestMaximaWidth),
            (4, GlyphMetrics.RestLonga.Width),
            (2, GlyphMetrics.RestDoubleWhole.Width),
            (1, GlyphMetrics.RestWhole.Width),
        })
        {
            while (remaining >= span)
            {
                symbolsWidth += width;
                symbolCount++;
                remaining -= span;
            }
        }

        // inner_padding == 1.0 at space == 0 (see remarks).
        return symbolsWidth + (symbolCount - 1);
    }

    // Staff-line Y-extent (positions -4..4 -> -2..2 ss). Every break-aligned grob
    // below carries extra-spacing-height that reaches the staff, so giving each box
    // this same Y makes them all overlap — see MmrRodMinimumDistance's remarks.
    private const double StaffYBottom = -2.0;
    private const double StaffYTop = 2.0;

    /// <summary>
    /// The bar line drawn at a multi-measure-rest run's LEFT bound. Lily# owns an internal
    /// boundary's bar line on the LEFT measure's <see cref="Measure.EndBarline"/> (the right
    /// measure's <see cref="Measure.StartBarline"/> is <see cref="BarlineType.None"/> to
    /// avoid double-drawing), so fall back to the previous measure's end when the run
    /// measure declares no start bar line of its own. This is the width LilyPond's left
    /// bounding <c>NonMusicalPaperColumn</c> reaches with (see <see cref="MmrRodMinimumDistance"/>).
    /// </summary>
    internal static BarlineType RunLeftBoundBarline(
        IReadOnlyList<Measure> measures, int runStart)
    {
        BarlineType start = measures[runStart].StartBarline;
        if (start != BarlineType.None)
            return start;
        return runStart > 0 ? measures[runStart - 1].EndBarline : BarlineType.None;
    }

    /// <summary>
    /// Room a clef change at the START of <paramref name="nextMeasure"/> needs to the
    /// LEFT of the bar line separating it from the measure before — zero when that
    /// measure opens with no clef change.
    /// </summary>
    /// <remarks>
    /// LilyPond engraves a mid-line clef change BEFORE the bar line: the unbroken
    /// break-align order is <c>… clef, cue-clef, staff-bar, key-cancellation,
    /// key-signature, time-signature …</c> (scm/define-grobs.scm:650-664). A key or time
    /// change therefore rides the spring AFTER the bar line, but a clef takes space
    /// BEFORE it — which is the preceding measure's last-item → bar line minimum.
    ///
    /// The amount is the boundary column's own geometry, so it is read off
    /// <see cref="BoundaryColumn.BarLineLeft"/> rather than recomputed: the clef's width
    /// plus its <c>Clef.space-alist (staff-bar . (extra-space . 0.7))</c>. Measured
    /// 2.84668 for a bass change clef on LilyPond 2.24.4.
    ///
    /// This is added to the EXISTING item → bar line minimum rather than replacing it.
    /// LilyPond's own minimum is <c>padding + skyline distance</c> (spacing-spanner.cc:315
    /// → separation-item.cc:48-68), which Lily# does not yet use for that pair; swapping
    /// it in moves every measure and is a separate step. Adding the clef's allowance
    /// leaves every clef-less boundary untouched.
    /// </remarks>
    internal static double BoundaryClefAllowance(BarlineType barline, Measure? nextMeasure)
        => nextMeasure == null
            ? 0
            : BoundaryColumn.Build(barline, nextMeasure.Items).BarLineLeft ?? 0;

    /// <summary>
    /// <c>Paper_column::minimum_distance</c> between the two paper columns bounding a
    /// multi-measure-rest run: a genuine <see cref="HorizontalSkyline"/> distance over
    /// the break-aligned grobs on each bounding column, so a key / time change sitting
    /// at the bound reserves its own width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc:144-164 Paper_column::minimum_distance —
    /// <c>max (0.0, skys[LEFT].distance (skys[RIGHT]))</c>, where <c>skys[LEFT]</c> is the
    /// LEFT column's RIGHT skyline and <c>skys[RIGHT]</c> the RIGHT column's LEFT skyline.
    /// Each skyline is built by lily/separation-item.cc:120-190 boxes(): every grob adds a
    /// Box whose X is <c>extent + extra-spacing-width</c> and whose Y is
    /// <c>pure_y_extent + extra-spacing-height</c> (defaults <c>(-0.1 . 0.1)</c> /
    /// <c>(0 . 0)</c>, separation-item.cc:166-169).
    ///
    /// Every break-aligned grob here carries an extra-spacing-height that INCLUDES the
    /// staff (pure-from-neighbor-interface::extra-spacing-height-including-staff,
    /// scm/define-grobs.scm) and the bar line spans the staff, so every box on one column
    /// overlaps every box on the other in Y. The distance therefore equals the horizontal
    /// reach difference; it is still expressed as boxes + a real
    /// <see cref="HorizontalSkyline.Distance"/> so the mechanism — and any future
    /// non-overlapping case — is exactly LilyPond's. For the same reason the box Y is set
    /// to the staff extent (the exact esh magnitude never changes which pairs overlap).
    ///
    /// Column-internal geometry, measured on LilyPond 2.24.4 (bar line left edge at the
    /// column origin, drawn width bw; break-alignment places changes AFTER it,
    /// lily/break-alignment-interface.cc: placed-left = prev.right + space):
    ///   KeySignature: left = bw + 1.0 (space-alist key←staff-bar 1.1, observed edge gap
    ///     1.0), extra-spacing-width (0.0 . 1.0). A `\key g \major` R1*5 run then reaches
    ///     0.19 + 1.0 + 1.1 + 1.0 = 3.29, min_dist 3.29 − (−0.1) = 3.390 — matching LilyPond,
    ///     where the old bw + 0.2 closed form returned 0.390 (the run came out ~3.0 ss narrow).
    ///   TimeSignature: left = bw + 0.75 (space-alist 1.0, observed 0.75), or, when a key
    ///     change precedes it on the same column, keysig.right + 1.15; esw (0.0 . 0.8).
    /// The bar line itself reaches bw + 0.1 (its default esw right, separation-item.cc:167).
    /// A leading key change folds any cancellation into <see cref="GetKeySignatureChangeWidth"/>,
    /// which matches LilyPond's KeyCancellation+KeySignature pair for the common cases (a
    /// pure new key, or a pure cancellation to C); a key TYPE change (flats↔sharps) at the
    /// bound is slightly under-reserved by the inter-grob gap LilyPond puts between the
    /// cancellation and the new signature — rare enough to leave documented.
    /// A leading CLEF change contributes NOTHING here, and that is LilyPond's own answer,
    /// not an omission. LilyPond orders an unbroken break-align group
    /// <c>clef, cue-clef, staff-bar, key-cancellation, key-signature, time-signature</c>
    /// (scm/define-grobs.scm:650-664), so the clef is the only one of the three that sits
    /// BEFORE the bar line. LilyPond's <c>minimum_distance</c> is measured column ORIGIN to
    /// column origin, and the origin is the leftmost break-aligned grob — so a clef moves
    /// the ORIGIN left without moving the bar line. This rod is expressed in Lily#'s frame,
    /// where the bar line sits at the origin (see the box built for it below), i.e. bar line
    /// to bar line. Measured on 2.24.4, bar line to bar line across `R1*5` is 14.133856 both
    /// with and without a leading `\clef bass` (and with a sparse or a dense preceding bar);
    /// only the column origin moves, by the clef's width + its
    /// <c>Clef.space-alist (staff-bar . (extra-space . 0.7))</c>. Adding a clef box here
    /// would therefore widen the run by ~2.847 ss that LilyPond does not spend.
    /// </remarks>
    internal static double MmrRodMinimumDistance(BarlineType leftBound, IEnumerable<MusicItem>? runStartItems)
    {
        HorizontalSkyline leftColumnRight =
            BoundaryColumn.Build(leftBound, runStartItems).RightSkylineFromBarLine();
        // The right bounding column carries only its bar line: whatever sits there, the
        // column origin coincides with the leftmost grob's left edge and that grob's
        // default extra-spacing-width left is −0.1, so the column's left reach is −0.1.
        // LILYPOND-REF: lily/separation-item.cc:167.
        HorizontalSkyline rightColumnLeft = HorizontalSkyline.FromBox(
            StaffYBottom, StaffYTop, xLeft: -0.1, xRight: 0.1, HorizontalDirection.Left);

        return Math.Max(0.0, leftColumnRight.Distance(rightColumnLeft));
    }

    /// <summary>
    /// LilyPond's minimum distance between the bar lines bounding a multi-measure
    /// rest run — the rod that replaces per-measure springs for the whole run.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/multi-measure-rest.cc:341-391
    /// Multi_measure_rest::calculate_spacing_rods, transcribed:
    /// <code>
    ///   length += full-measure-extra-space
    ///           + options.get_duration_space (mlen.main_part_)
    ///           + space-increment * log2 (measure-count);
    ///   length += 2 * bound-padding;
    ///   rod.distance_ = max (Paper_column::minimum_distance (li, ri) + length, minlen);
    /// </code>
    /// <paramref name="length"/> enters as the symbol width (set_spacing_rods passes
    /// <c>symbol_stencil (me, 0.0)</c>). MultiMeasureRest leaves <c>minimum-length</c>
    /// unset, so LilyPond's <c>minlen</c> is 0 and the max() is inert; it is kept here
    /// to match the source line for line.
    ///
    /// The <c>options.get_duration_space</c> above is NOT the score's note spacing.
    /// <c>calculate_spacing_rods</c> does <c>options.init_from_grob (me)</c> with
    /// <c>me</c> = the MULTI-MEASURE REST grob, and init_from_grob reads
    /// <c>spacing-increment</c>, <c>shortest-duration-space</c> and
    /// <c>common-shortest-duration</c> off that grob — none of which MultiMeasureRest
    /// carries. So all three fall back to init_from_grob's OWN defaults, which are not
    /// the Spacing_options constructor's 1.2 / 2.0 / (1/8) but
    /// <c>1</c>, <c>1</c> and <c>Moment (1/8, 1/16)</c>:
    ///   increment = 1, shortest-duration-space = 1, global-shortest = 1/8.
    /// The rod's duration space is therefore SCORE-INDEPENDENT — a 4/4 bar always
    /// contributes <c>(1 + log2 ((1/1) / (1/8))) * 1 = 4.0</c>, whatever the music's
    /// own shortest note is. Feeding it the score's base shortest duration (which gave
    /// 5.298 for a 4/4 bar) made every run 1.298 ss too wide.
    /// Verified on LilyPond 2.24.4: overriding SpacingSpanner's shortest-duration-space
    /// (2.0 -> 4.0) or spacing-increment (1.2 -> 2.4) moves the run width by exactly
    /// 0.000, because the rod never reads them.
    /// LILYPOND-REF: lily/spacing-options.cc:31-53 Spacing_options::init_from_grob,
    ///               lily/spacing-options.cc:72-107 get_duration_space.
    /// </remarks>
    internal static double MmrRodDistance(
        int measureCount,
        Fraction measureLength,
        double minimumDistance,
        double runBarlineWidth)
    {
        double length = MmrSymbolWidth(measureCount);
        length += FullMeasureExtraSpace
                  + MmrRodDurationSpace(measureLength)
                  + MmrSpaceIncrement * Math.Log2(measureCount);
        length += 2 * MmrBoundPadding;

        const double minlen = 0.0;
        // LilyPond's rod is the whole li->ri COLUMN distance, with the bounding bar
        // lines living INSIDE those columns (bar-line extent runs from the column
        // origin). Lily#'s layout instead prices each measure as CONTENT + its own
        // bar-line glyph widths (GetBarlineWidth(start)+(end), added by the layouter
        // and the break gate alike), so the run measure would otherwise draw its
        // bounding bar lines twice: once folded into minimum_distance, once as measure
        // width. Subtract that run bar-line width here so the rod is the run's CONTENT
        // span; the layout then re-adds the bar lines to reach LilyPond's column
        // distance. (This is exactly what the old bw+0.2 form did implicitly by feeding
        // a None start bar line — now made explicit, since minimum_distance carries the
        // real left bar line and any break-aligned change.)
        return Math.Max(minimumDistance + length - runBarlineWidth, minlen);
    }

    /// <summary>
    /// <c>get_duration_space</c> as the multi-measure-rest rod sees it: with the
    /// Spacing_options that init_from_grob leaves behind for a grob carrying no
    /// spacing properties. See the note on <see cref="MmrRodDistance"/>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/spacing-options.cc:72-107.</remarks>
    private static double MmrRodDurationSpace(Fraction measureLength)
    {
        // init_from_grob's fallbacks, NOT the Spacing_options constructor's values.
        const double increment = 1.0;
        const double shortestDurationSpace = 1.0;
        const double globalShortest = 0.125; // Moment (1/8, 1/16).main_part_

        double ratio = measureLength.ToDouble() / globalShortest;
        return ratio < 1.0
            ? (shortestDurationSpace + ratio - 1) * increment
            : (shortestDurationSpace + Math.Log2(ratio)) * increment;
    }

    /// <summary>
    /// Calculates the common shortest duration across all voices in a multi-staff score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:92-173 calc_common_shortest_duration —
    /// per MEASURE, find the shortest sounding duration; the spacing basis is the
    /// MODE of those per-measure shortests across the piece (ties prefer the
    /// shorter duration), capped at base-shortest-duration (3/16). This keeps one
    /// ornamental 32nd-note run from loosening the whole piece, and keeps
    /// long-note pieces from collapsing to minimal spacing — unlike the absolute
    /// global minimum this method used previously.
    /// </remarks>
    public static double CalculateCommonShortestDuration(Model.MultiStaffScore score)
        => CommonShortestDuration(score.AllVoices.Select(v => v.Measures),
            score.TimeSignature.MeasureDuration);

    /// <summary>
    /// Calculates the common shortest duration across all voices in a single-staff score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:92-173 calc_common_shortest_duration
    /// </remarks>
    public static double CalculateCommonShortestDuration(Model.Score score)
        => CommonShortestDuration(score.Voices.Select(v => v.Measures),
            score.TimeSignature.MeasureDuration);

    private static double CommonShortestDuration(
        IEnumerable<ImmutableArray<Model.Measure>> voiceMeasures,
        Fraction initialMeasureDuration)
    {
        var voices = voiceMeasures.ToList();
        int measureCount = voices.Count == 0 ? 0 : voices.Max(m => m.Length);

        // A full-measure rest is measured against the PREVAILING meter, so a 2/4 bar's
        // half rest is dropped from the vote just like a 4/4 bar's whole rest.
        var meters = MultiMeasureRestEngraver.PrevailingMeters(
            voices, measureCount, initialMeasureDuration);

        // Per-measure shortest across all voices, then count occurrences.
        var counts = new Dictionary<double, int>();
        for (int m = 0; m < measureCount; m++)
        {
            double shortest = double.MaxValue;
            foreach (var measures in voices)
            {
                if (m >= measures.Length)
                    continue;

                // Full-measure rests create no musical columns in LilyPond and
                // therefore never contribute to the common shortest duration.
                if (MultiMeasureRestEngraver.IsFullMeasureRest(measures[m], meters[m]))
                    continue;

                foreach (var item in measures[m].Items)
                {
                    double dur = item.Duration.ToDouble();
                    // Skip zero-duration items (grace notes, clef changes, etc.)
                    if (dur > 0 && dur < shortest)
                        shortest = dur;
                }
            }

            if (shortest < double.MaxValue)
                counts[shortest] = counts.GetValueOrDefault(shortest) + 1;
        }

        if (counts.Count == 0)
            return EngravingDefaults.BaseShortestDuration;

        // Mode; on equal counts LilyPond prefers the SHORTER duration
        // (spacing-spanner.cc:156-164 — descending scan with >=).
        double mode = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .First().Key;

        // d = min(base-shortest-duration, mode) — spacing-spanner.cc:166-171.
        return Math.Min(EngravingDefaults.BaseShortestDuration, mode);
    }

    /// <summary>
    /// Creates a spring for grace note spacing with tighter parameters.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:163-180 grace note spring
    /// LILYPOND-REF: scm/define-grobs.scm:1721 GraceSpacing
    /// Grace notes use: spacing-increment=0.8, shortest-duration-space=1.6,
    /// inverse_stretch_strength = increment / 2.0
    /// </remarks>
    public static Spring CreateGraceSpring(Fraction graceDuration,
                                            GraceSpacingParameters? graceParams = null,
                                            double? baseShortestDuration = null)
    {
        var gp = graceParams ?? GraceSpacingParameters.Default;

        double durationValue = graceDuration.ToDouble();
        if (durationValue <= 0)
            durationValue = gp.BaseShortestDuration;

        // LILYPOND-REF: lily/grace-spacing-engraver.cc — use per-group common shortest duration
        double bsd = baseShortestDuration ?? gp.BaseShortestDuration;

        // Same Gourlay formula as regular notes, but with grace parameters
        double ratio = durationValue / bsd;
        double spaceFactor = ratio < 1.0
            ? gp.ShortestDurationSpace + ratio - 1.0
            : gp.ShortestDurationSpace + Math.Log2(ratio);

        double idealDistance = spaceFactor * gp.SpacingIncrement;
        double minDistance = gp.SpacingIncrement;

        // LILYPOND-REF: spacing-basic.cc:174
        // inverse_stretch_strength = increment / 2.0 (more rigid than normal)
        double inverseStretchStrength = gp.SpacingIncrement / 2.0;

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>
    /// Calculates the common shortest duration within a grace note group.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc — common-shortest-duration per grace sequence
    /// Each grace group independently determines its base shortest duration,
    /// rather than using a global default. This ensures that a group of sixteenth
    /// grace notes spaces differently from a group of eighth grace notes.
    /// </remarks>
    public static double CalculateGraceGroupShortestDuration(
        ImmutableArray<GraceNoteInfo> notes)
    {
        double shortest = double.MaxValue;

        foreach (var note in notes)
        {
            double dur = note.BaseDuration.ToDouble();
            if (dur > 0 && dur < shortest)
                shortest = dur;
        }

        // Fall back to default grace duration (eighth note)
        return shortest < double.MaxValue
            ? shortest
            : GraceSpacingParameters.Default.BaseShortestDuration;
    }

    /// <summary>
    /// Where a grace run's columns sit: an offset per grace from the run's FIRST column,
    /// plus the distance from the LAST grace column to the main note's column.
    /// </summary>
    /// <remarks>
    /// One object because the run is one chain: the reservation, the drawn heads and the
    /// beam quanter's x frame all have to read the same numbers. Until 2026-08-01 they read
    /// four different ones — see <see cref="GraceColumns"/>.
    /// </remarks>
    internal readonly record struct GraceColumnLayout(
        ImmutableArray<double> Offsets, double ToMain)
    {
        /// <summary>First grace column → the main note's column.</summary>
        public double Span => (Offsets.IsDefaultOrEmpty ? 0 : Offsets[^1]) + ToMain;
    }

    /// <summary>
    /// A grace run's column positions, LilyPond's way: one spring per column, floored by the
    /// two columns' facing separation skylines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LilyPond has no grace-column WIDTH. Every gap of the run — including the last grace to
    /// the main note, which is not a junction of its own — is
    /// <c>max(ideal, min_dist + 0.3)</c>:
    /// </para>
    /// <list type="bullet">
    /// <item>LILYPOND-REF: lily/spacing-basic.cc:163-180 <c>Spacing_spanner::note_spacing</c> —
    ///   when <c>delta_t.grace_part_</c> is non-zero the spring's options come from the
    ///   GraceSpacing grob: <c>len = grace_opts.get_duration_space (delta_t.grace_part_)</c>.</item>
    /// <item>LILYPOND-REF: lily/spacing-options.cc:71-107 <c>Spacing_options::get_duration_space</c>
    ///   — the Gourlay formula, here with GraceSpacing's own parameters
    ///   (scm/define-grobs.scm:1721-1725: <c>shortest-duration-space</c> 1.6,
    ///   <c>spacing-increment</c> 0.8).</item>
    /// <item>LILYPOND-REF: scm/output-lib.scm:1403-1422 grace-spacing::calc-shortest-duration
    ///   — the ratio is taken against the
    ///   MINIMUM gap of the run's OWN columns, so a run of equal graces always has ratio 1
    ///   whatever the note value, and the ratio-below-1 branch is unreachable here.</item>
    /// <item>LILYPOND-REF: lily/note-spacing.cc:42-115 <c>Note_spacing::get_spacing</c> —
    ///   <c>ideal = base.ideal_distance () - increment + left_head_end</c>, where
    ///   <c>left_head_end</c> is the RIGHT edge of the left column's first note head measured
    ///   in that column, and <c>min_dist</c> is the facing skylines' distance.</item>
    /// <item>LILYPOND-REF: lily/spring.cc:103-129 <c>merge_springs</c>, :122 — the
    ///   <see cref="SpringHeadroom"/> above the minimum, applied even to a single wish
    ///   (lily/spacing-spanner.cc:392-393).</item>
    /// </list>
    /// <para>
    /// MEASURED (audit/lp-geometry/probes/grace-column-width.ly, 14 books, ledger
    /// <c>grace.column.*</c>): the corpus texture reads the FLOOR, not the spring —
    /// 1.6*0.8 - 0.8 + 0.917939 = 1.397939 against a floor of 0.917939 + 0.1 + 0.1 + 0.3 =
    /// 1.417939, and LilyPond draws 1.417939. Only a run with mixed durations gets far
    /// enough above the floor to read the ideal (2.197939 for the eighth of a run whose
    /// minimum is a sixteenth), which is why the mixed books are in the ledger.
    /// </para>
    /// </remarks>
    internal static GraceColumnLayout GraceColumns(
        ImmutableArray<GraceNoteInfo> notes, MusicItem? mainItem,
        GraceSpacingParameters? graceParams = null)
    {
        if (notes.IsDefaultOrEmpty)
            return new GraceColumnLayout(ImmutableArray<double>.Empty, 0);

        var gp = graceParams ?? GraceSpacingParameters.Default;
        double dtMin = CalculateGraceGroupShortestDuration(notes);
        // A run draws a beam only when EVERY head carries one; otherwise each head draws a
        // flag, and the flag is ink in its column's RIGHT skyline. Same gate as
        // GraceNoteEngraver.QuantGraceBeam and the renderer's DrawGraceStemsAndBeam.
        bool beamed = notes.Length >= 2
            && notes.All(n => n.BaseDuration.Denominator >= 8);

        var offsets = ImmutableArray.CreateBuilder<double>(notes.Length);
        double x = 0, toMain = 0;
        for (int i = 0; i < notes.Length; i++)
        {
            offsets.Add(x);
            double rightReach = GraceColumnRightReach(notes[i], beamed);
            double leftReach = i + 1 < notes.Length
                ? GraceColumnLeftReach(notes[i + 1])
                : MainColumnLeftReach(mainItem);
            double gap = GraceColumnGap(notes[i], dtMin, gp, rightReach + leftReach);
            if (i + 1 < notes.Length) x += gap; else toMain = gap;
        }
        return new GraceColumnLayout(offsets.ToImmutable(), toMain);
    }

    /// <summary>One gap of a grace run — the spring, floored by the skyline distance.</summary>
    private static double GraceColumnGap(GraceNoteInfo left, double dtMin,
                                         GraceSpacingParameters gp, double minDistance)
    {
        var baseSpring = CreateGraceSpring(left.BaseDuration, gp, dtMin);
        // LILYPOND-REF: lily/note-spacing.cc:77 — ideal = base.ideal - increment + left_head_end.
        double ideal = baseSpring.IdealDistance - gp.SpacingIncrement + GraceHeadEnd;
        // LILYPOND-REF: lily/note-spacing.cc:78-83 set_min_distance, then lily/spring.cc:122.
        return Math.Max(ideal, minDistance + SpringHeadroom);
    }

    /// <summary>
    /// The grace note head's right edge in its own column — LilyPond's
    /// <c>left_head_end</c> for a grace spring.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:47-70 — <c>left_head_end =
    /// g-&gt;extent (col, X_AXIS)[RIGHT]</c>, the head's right edge IN THE FONT ITS GROB READS.
    /// MEASURED: LilyPond reports 0.917939 for a grace head and 1.304200 for a full-size
    /// black one. The grace one is NOT the full-size one scaled (that is 0.922205): Emmentaler
    /// is optically sized, so a font-size −3 grob reads the FOURTEEN design's head, 1.298161
    /// in its own staff spaces, and magstep(−3) of that is 0.917939 — LilyPond's own number to
    /// six places. <see cref="GraceNoteItem.Font"/> is that font, so this reads a width and
    /// multiplies nothing.
    /// </remarks>
    private static double GraceHeadEnd => GraceNoteItem.Font.NoteheadBlack.Width;

    /// <summary>
    /// How far a grace column's ink reaches RIGHT of its origin, in the separation-skyline
    /// sense: the head (plus its flag when the run is not beamed) widened by
    /// <see cref="DefaultExtraSpacingWidth"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:120-190 Separation_item::boxes — every grob's box
    /// is widened by its own <c>extra-spacing-width</c>, defaulting to <c>(-0.1 . 0.1)</c>
    /// (:166-167). MEASURED: a beamed grace column's right skyline is 1.017939 and a flagged
    /// one's is 1.538627 (probes/grace-column-width.ly, books GCW2 and GCW1).
    /// </remarks>
    private static double GraceColumnRightReach(GraceNoteInfo note, bool beamed)
    {
        double ink = GraceHeadEnd;
        if (!beamed && note.BaseDuration.Numerator == 1 && note.BaseDuration.Denominator >= 8)
        {
            // The flag hangs off the STEM, so its reach is the stem's x plus the flag's own
            // width — both read from the grace's OWN font (see GraceHeadEnd). The stem's x is
            // the one house, LayoutUtilities.StemAttachX, which is where the drawn flag is
            // put too (SharedRenderer.DrawGraceStemsAndBeam).
            // MEASURED: 0.852939 + 0.585689 = 1.438627 is LilyPond's own reading to nine
            // places (ledger grace.column.single.to-main). It hung off the head's ADVANCE
            // until 2026-08-02, 0.063472 too far right.
            var font = GraceNoteItem.Font;
            var flag = GlyphMetrics.GetFlagBBox(font, note.BaseDuration.Denominator, stemUp: true);
            if (flag != default)
                ink = Math.Max(ink,
                    LayoutUtilities.StemAttachX(
                        up: true, GlyphMetrics.NoteValueOf(note.BaseDuration),
                        NoteheadStyle.Default, font)
                    + flag.Width);
        }
        return ink + DefaultExtraSpacingWidth;
    }

    /// <summary>
    /// How far a grace column's ink reaches LEFT of its origin: nothing but the head, unless
    /// the grace carries an accidental, which hangs left and declares a wider box.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:40 Accidental <c>(extra-spacing-width . (-0.2 . 0.0))</c>
    /// — see <see cref="AccidentalExtraSpacingWidthLeft"/>. MEASURED (book GCWA): an
    /// accidental on the SECOND grace of a pair pushes that gap from 1.417939 to 2.560895,
    /// which is 1.017939 + (1.042957 + 0.2) + 0.3.
    /// </remarks>
    private static double GraceColumnLeftReach(GraceNoteInfo note)
    {
        if (note.Accidental is not { } acc)
            return DefaultExtraSpacingWidth;
        var placement = new AccidentalPlacement();
        // Two fonts, because the two grobs carry two font-sizes: the accidental is −4 and the
        // head it clears is −3 (scm/music-functions.scm:635-648 general-grace-settings).
        var layout = placement.CalculateSinglePosition(
            note.StaffPosition, acc, isCourtesy: false,
            GraceNoteItem.AccidentalFont, GraceNoteItem.Font);
        return layout is { } al
            ? Math.Abs(al.XOffset) + AccidentalExtraSpacingWidthLeft
            : DefaultExtraSpacingWidth;
    }

    /// <summary>The same reading for the MAIN note's column, which closes the run.</summary>
    private static double MainColumnLeftReach(MusicItem? mainItem)
    {
        if (mainItem is null)
            return DefaultExtraSpacingWidth;
        double acc = CalculateLeftExtent(mainItem);
        return acc > 0 ? acc + AccidentalExtraSpacingWidthLeft : DefaultExtraSpacingWidth;
    }

    /// <summary>
    /// The distance a grace run needs in front of its main note — the first grace column to
    /// the main column.
    /// </summary>
    /// <remarks>
    /// This is <see cref="GraceColumnLayout.Span"/>, i.e. the sum of the run's own gaps and
    /// nothing else. There is no junction padding:
    /// LILYPOND-REF lily/spacing-basic.cc:163 Spacing_spanner::note_spacing
    /// takes the grace branch for the last-grace-to-main pair too, because
    /// <c>delta_t.grace_part_</c> is non-zero there. Lily# used to add 0.4 here and another
    /// 0.4 when placing the group; MEASURED (ledger grace.column.*.to-main) LilyPond adds
    /// neither.
    /// <para>
    /// A LEADING accidental is the one thing outside the chain: it hangs left of the FIRST
    /// column, so a caller reserving room in front of the main note has to add that reach on
    /// top of the span. (LilyPond does not add it either — it falls out of the approach
    /// spring's own min_dist, which this measure stands in for.)
    /// </para>
    /// </remarks>
    public static double CalculateGraceGroupSpringWidth(
        ImmutableArray<GraceNoteInfo> notes,
        GraceSpacingParameters? graceParams = null)
    {
        if (notes.IsDefaultOrEmpty)
            return 0;
        double span = GraceColumns(notes, mainItem: null, graceParams).Span;
        return span + GraceColumnLeftReach(notes[0]) - DefaultExtraSpacingWidth;
    }

    /// <summary>
    /// Adjusts a spring's MinDistance to accommodate grace notes before the next item.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs
    /// Uses spring-based grace group width when note info is available,
    /// falls back to fixed-width calculation for backward compatibility.
    /// </remarks>
    public static Spring AdjustSpringForGraceNotes(Spring spring, int graceNoteCount)
    {
        if (graceNoteCount <= 0)
            return spring;

        double graceWidth = GraceNoteEngraver.GetGraceGroupWidth(graceNoteCount);
        double newMin = Math.Max(spring.MinDistance, spring.MinDistance + graceWidth);
        double newIdeal = Math.Max(spring.IdealDistance, newMin);

        return new Spring(newIdeal, newMin, spring.InverseStretchStrength);
    }

    /// <summary>
    /// What LilyPond charges the spring that RUNS INTO a grace: it is scaled by
    /// <c>0.8</c> — LilyPond's own comment on the number is "Ugh. 0.8 is arbitrary."
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:396-403 in musical_column_spacing — applied when the RIGHT column has a
    ///   grace part and the LEFT column has none, i.e. exactly once per grace run, at its
    ///   approach. The spring itself is an ORDINARY note spring: lily/spacing-basic.cc takes
    ///   the main-part branch because the left column carries no grace.
    /// MEASURED (ledger grace.column.approach): LilyPond spaces that gap at 2.401796 where
    /// the same book's ordinary quarter gap is 3.002245, and 3.002245 × 0.8 = 2.401796 to
    /// fifteen places.
    /// </remarks>
    public const double GraceApproachScale = 0.8;

    /// <summary>
    /// Makes room for the grace notes hanging left of the next column: the approach is
    /// SHRUNK the way LilyPond shrinks it, and the run's own width is what is added.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:396-403 musical_column_spacing (the 0.8);
    ///   lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs (the run's own
    ///   springs, whose total is <see cref="CalculateGraceGroupSpringWidth"/>).
    /// <para>
    /// ⚠️ THE TWO ENGINES MAKE ROOM IN OPPOSITE DIRECTIONS, and this used to make room the
    /// other way: it added the run's width to the spring and left the approach alone, so the
    /// gap before a grace came out 0.850449 too wide (ledger grace.column.approach, open
    /// since 2026-08-01). LilyPond does not widen anything — it takes the spring it already
    /// had and shrinks it, then the grace columns live inside the run's own springs.
    /// </para>
    /// <para>
    /// Lily# draws a run as glyphs hanging off the main column rather than as columns of its
    /// own, so both halves land on ONE spring here: scale first (that is the approach), then
    /// add the run (that is what the grace columns would have spanned). The scaling is
    /// <see cref="Spring.Scale"/>, which is LilyPond's <c>Spring::operator*=</c> and so
    /// refuses to push the ideal below the rod.
    /// </para>
    /// </remarks>
    public static Spring AdjustSpringForGraceNotes(Spring spring,
        ImmutableArray<GraceNoteInfo> graceNotes,
        GraceSpacingParameters? graceParams = null,
        MusicItem? mainItem = null)
        => graceNotes.IsDefaultOrEmpty
            ? spring
            : SpringIntoGraceRun(spring,
                GraceColumns(graceNotes, mainItem, graceParams).Span,
                CalculateGraceGroupSpringWidth(graceNotes, graceParams));

    /// <summary>
    /// The spring that runs into a grace run, given how wide the run itself is: LilyPond's
    /// 0.8 on the approach, then the run.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME for the rule, because Lily# builds springs in two places and they must
    /// agree — the column system (<see cref="AdjustSpringForGraceNotes"/>) and the drawn
    /// timing-column system (MeasureLayouter). The 0.8 was added to the first alone at
    /// first and the ledger did not move a hair, because the drawn output comes from the
    /// second (HANDOFF §2 A's "two places computing one quantity", in its spring form).
    /// </remarks>
    /// <param name="graceRunSpan">
    /// The run's own ANCHOR-TO-ANCHOR width — first grace to main note. This is what the
    /// ideal grows by, because it is the distance the drawn glyphs actually occupy between
    /// two column origins.
    /// </param>
    /// <param name="graceRunClearance">
    /// The same plus whatever ink hangs LEFT of the first grace's anchor. This is what the
    /// MIN grows by. ⚠️ Putting it in the ideal instead pushes the approach out by exactly
    /// that ink (0.2 in the ledger's book): LilyPond keeps the clearance in the approach
    /// spring's own min_dist, so it binds only when the line is squeezed and never widens a
    /// comfortable line.
    /// </param>
    public static Spring SpringIntoGraceRun(
        Spring spring, double graceRunSpan, double graceRunClearance)
    {
        if (graceRunClearance <= 0)
            return spring;

        var approach = spring.Scale(GraceApproachScale);
        double newMin = approach.MinDistance + graceRunClearance;
        double newIdeal = Math.Max(approach.IdealDistance + graceRunSpan, newMin);
        return new Spring(newIdeal, newMin, approach.InverseStretchStrength);
    }

    /// <summary>
    /// The column a spring coming from the left actually ARRIVES at: the FIRST GRACE when a
    /// run leads the item, the item itself otherwise.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:396-403 musical_column_spacing — the spring
    ///   whose RIGHT column has the grace part is the one that stops at the grace, so that
    ///   column is what the pair is built from.
    /// LILYPOND-REF: lily/note-spacing.cc:162-197 same_direction_correction — the rule that
    ///   then reads the two stems, and which wants the head ranges more than one staff
    ///   position apart.
    /// LILYPOND-REF: scm/music-functions.scm:633-637 score-grace-settings —
    ///   <c>((Voice Stem direction ,UP))</c>, why the stand-in's stem is forced up.
    /// <para>
    /// LilyPond's spring stops at the grace column — the run is columns of its own, so the
    /// pair whose stems the optical correction compares is (previous note, first grace), not
    /// (previous note, main note). Lily# hangs the run off the main column and therefore has
    /// ONE spring where LilyPond has three, so the correction has to be told which column
    /// the spring's right end really is.
    /// </para>
    /// <para>
    /// MEASURED, and it is the whole of what was left of grace.column.approach: in that book
    /// the ordinary spring arrives at 3.252245 against the control's 3.002245, and c→f is
    /// three staff positions (the correction fires) where c→d is one (LilyPond's
    /// lily/note-spacing.cc:162-197 same_direction_correction wants more than one). The 0.25
    /// it added became 0.2 after the approach scaling.
    /// </para>
    /// <para>
    /// ⚠️ The stand-in's stem is forced UP, not derived from its pitch: a grace stem is up
    /// whatever the note (scm/music-functions.scm:633-637 score-grace-settings, the same
    /// rule GraceNoteEngraver draws by). Letting the pitch decide would flip the correction's
    /// sign on any grace above the middle line.
    /// </para>
    /// </remarks>
    private static MusicItem? ApproachColumn(MusicItem? item)
    {
        if (item == null)
            return null;
        var grace = GraceNotesOf(item);
        if (grace.IsDefaultOrEmpty)
            return item;

        var first = grace[0];
        return new NoteItem(
            first.StaffPosition, first.BaseDuration, 0,
            first.Accidental, first.NeedsLedger, 0)
        {
            StemUpOverride = true,
        };
    }

    /// <summary>The leading grace notes hanging left of an item's column, if any.</summary>
    private static ImmutableArray<GraceNoteInfo> GraceNotesOf(MusicItem item) => item switch
    {
        NoteItem n => n.LeadingGrace,
        ChordItem c => c.LeadingGrace,
        _ => ImmutableArray<GraceNoteInfo>.Empty
    };

    // ========================================
    // Mid-measure change items (the missing non-musical column)
    // ========================================

    /// <summary>
    /// The two gaps LilyPond puts around a MID-MEASURE clef / key / time change, plus the
    /// pair's minimum. Distances are column origin to column origin.
    /// </summary>
    /// <param name="LeftGap">Previous musical column → the change column's origin.</param>
    /// <param name="RightGap">The change column's origin → the next musical column.</param>
    /// <param name="MinDistance">Minimum for the two together (the rods, summed).</param>
    internal readonly record struct MidMeasureChangeSpacing(
        double LeftGap, double RightGap, double MinDistance)
    {
        /// <summary>Previous musical column → the next one, i.e. what one spring must span.</summary>
        public double TotalIdeal => LeftGap + RightGap;
    }

    /// <summary>
    /// The change column's own extent right — <c>last_ext[RIGHT]</c> in
    /// <c>Staff_spacing::get_spacing</c>. Zero for anything that is not a change item.
    /// </summary>
    /// <remarks>
    /// The column's origin is the glyph's INK LEFT edge (measured on 2.24.4: a mid-measure
    /// bass clef's anchor plus its ink width plus 1.0 lands exactly on the next note head),
    /// so this is simply the glyph's width.
    /// LILYPOND-REF: lily/spacing-interface.cc:217 — <c>ext = break_item->extent (col, X_AXIS)</c>.
    /// </remarks>
    private static double ChangeItemColumnWidth(MusicItem item) => item switch
    {
        ClefChangeItem cc => GetClefChangeWidth(cc.NewClef),
        KeySignatureChangeItem kc => GetKeySignatureChangeWidth(kc),
        TimeSignatureChangeItem tc => GetTimeSignatureChangeWidth(tc),
        _ => 0
    };

    private static bool IsChangeItem(MusicItem item) =>
        item is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem;

    /// <summary>
    /// Whether this item stands in the non-musical change column rather than the musical
    /// one — i.e. whether <see cref="MidMeasureChangeGaps"/> owns its spacing.
    /// </summary>
    internal static bool IsMidMeasureChangeColumn(MusicItem item) => IsChangeItem(item);

    /// <summary>
    /// The <c>space-alist</c> distance from a change item to the following note.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>Staff_spacing::get_spacing</c> looks up <c>first-note</c> and only replaces it
    /// with <c>next-note</c> when that entry EXISTS (staff-spacing.cc:147-153). Clef is the
    /// only one of the three that has a <c>next-note</c> entry, so a MID-LINE key or time
    /// change — where nothing is starting a line — is nevertheless priced by
    /// <c>first-note</c>. Counter-intuitive, and confirmed by measurement: probes MK and MC
    /// land on 2.5 and 1.0 to six digits (COORDINATE_AUDIT.md §4.7.2).
    /// <para>
    /// All three of the alist types involved (extra-space, shrink-space, semi-shrink-space)
    /// put the IDEAL at <c>last_ext[RIGHT] + distance</c>; they differ only in what becomes
    /// `fixed` and whether the spring stretches, neither of which this single-spring model
    /// carries yet. LILYPOND-REF: lily/staff-spacing.cc:174-198.
    /// </para>
    /// </remarks>
    private static double ChangeItemSpaceToNextNote(MusicItem item) =>
        ChangeItemSpaceDef(item).Distance;

    /// <summary>
    /// The whole space-alist entry a change grob offers the following note: the distance and
    /// which of <c>Staff_spacing</c>'s arms consumes it.
    /// </summary>
    /// <param name="SplitsFixed">semi-shrink-space, which puts HALF the distance into
    /// <c>fixed</c> before the ideal (staff-spacing.cc:193-198). extra-space and shrink-space
    /// leave <c>fixed</c> alone, so they differ from it under compression even though all
    /// three put the ideal at <c>last_ext[RIGHT] + distance</c>.</param>
    /// <param name="Stretchable">shrink-space and semi-shrink-space clear
    /// <c>is_stretchable</c> (:191, :197); extra-space does not.</param>
    private static (double Distance, bool SplitsFixed, bool Stretchable)
        ChangeItemSpaceDef(MusicItem item) => item switch
        {
            // (next-note . (extra-space . 1.0))            scm/define-grobs.scm:924
            ClefChangeItem => (1.0, false, true),
            // (first-note . (shrink-space . 2.5))          scm/define-grobs.scm:1947
            KeySignatureChangeItem => (2.5, false, false),
            // (first-note . (semi-shrink-space . 2.0))     scm/define-grobs.scm:3948
            TimeSignatureChangeItem => (2.0, true, false),
            _ => (0, false, true)
        };

    /// <summary>
    /// A key or time change opening a measure shares the bar line's non-musical column. This
    /// returns how far its ink right edge sits from the bar line's ink RIGHT edge — the frame
    /// <see cref="BarlineToFirstColumnSpring"/> works in — and which grob ends the column.
    /// Null when nothing break-aligned opens the measure.
    /// </summary>
    /// <remarks>
    /// Inside the column, break alignment puts each group's left edge at the previous group's
    /// ink right plus the LEFT group's space-alist entry keyed on the RIGHT group's
    /// break-align-symbol: BarLine gives key-signature 1.0 and time-signature 0.75
    /// (scm/define-grobs.scm BarLine.space-alist, transcribed in
    /// <see cref="GetBarlineToItemSpace"/>). Measured on 2.24.4: bar-line ink right to the
    /// signature's anchor is exactly 1.000000 and 0.750000 — COORDINATE_AUDIT.md §4.7.3.
    /// <para>
    /// A CLEF change opening a measure is excluded: break-align-orders engraves it BEFORE the
    /// bar line (scm/define-grobs.scm:650-664), so it is paid for by the preceding measure's
    /// closing gap via <see cref="BoundaryClefAllowance"/> and contributes nothing here.
    /// </para>
    /// <para>
    /// ⚠️ SIMPLIFICATION: LilyPond splits a key change into a KeyCancellation grob and a
    /// KeySignature grob with 0.5 between them (KeyCancellation.space-alist), where Lily#
    /// carries both in one KeySignatureChangeItem whose width already sums the naturals. The
    /// corpus does not reach that case — probe K goes from no accidentals to three, so no
    /// cancellation is engraved — and it is a separate defect from this one.
    /// </para>
    /// </remarks>
    internal static (double Prefix, MusicItem LastChange)? BoundaryChangePrefix(
        IReadOnlyList<MusicItem>? firstItems)
    {
        if (firstItems == null)
            return null;

        double prefix = 0;
        MusicItem? last = null;
        foreach (var item in firstItems)
        {
            if (item is ClefChangeItem || !IsChangeItem(item))
                continue;
            prefix += last == null
                ? GetBarlineToItemSpace(item)
                : BetweenChangeItemsSpace(last, item);
            prefix += ChangeItemColumnWidth(item);
            last = item;
        }
        return last == null ? null : (prefix, last);
    }

    /// <summary>
    /// A change grob's own <c>extra-spacing-width</c>, as (leftward, rightward) reach.
    /// </summary>
    /// <remarks>
    /// These are NOT the default <c>(-0.1 . 0.1)</c>: KeySignature and KeyCancellation
    /// declare <c>(0.0 . 1.0)</c> (scm/define-grobs.scm:1936, :1982) and TimeSignature
    /// <c>(0.0 . 0.8)</c> (:3933); Clef declares nothing and keeps the default
    /// (lily/separation-item.cc:167). The zero on the left is measurable: it is exactly why
    /// the mid-measure key and clef probes' left gaps differ by 0.05 — half of the 0.1.
    /// </remarks>
    private static (double Left, double Right) ChangeItemExtraSpacingWidth(MusicItem item) =>
        item switch
        {
            KeySignatureChangeItem => (0.0, 1.0),
            TimeSignatureChangeItem => (0.0, 0.8),
            _ => (DefaultExtraSpacingWidth, DefaultExtraSpacingWidth)
        };

    /// <summary>
    /// The gap between two change items sharing one column, from the LEFT one's space-alist
    /// keyed on the right one's <c>break-align-symbol</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:922-923 Clef (key-signature 0.82, time-signature
    /// 1.52); :1945 KeySignature (time-signature 1.15). Only these orders occur, because
    /// break-align-orders fixes the sequence clef → key-signature → time-signature
    /// (scm/define-grobs.scm:650-664).
    /// </remarks>
    private static double BetweenChangeItemsSpace(MusicItem left, MusicItem right) =>
        (left, right) switch
        {
            (ClefChangeItem, KeySignatureChangeItem) => 0.82,
            (ClefChangeItem, TimeSignatureChangeItem) => 1.52,
            (KeySignatureChangeItem, TimeSignatureChangeItem) => 1.15,
            _ => 0
        };

    /// <summary>
    /// How far the leftmost ink of a MUSICAL column reaches left of that column's origin,
    /// including the grob's own <c>extra-spacing-width</c> — the right-hand term of
    /// <c>Paper_column::minimum_distance</c>.
    /// </summary>
    internal static double MusicalColumnLeftReach(MusicItem item) =>
        CalculateLeftExtent(item)
        + (HasAccidental(item) ? AccidentalExtraSpacingWidthLeft : DefaultExtraSpacingWidth);

    /// <summary>
    /// Prices a mid-measure clef / key / time change the way LilyPond does: as its own
    /// non-musical column between two musical ones, with the two gaps around it computed by
    /// DIFFERENT formulas. Returns null when <paramref name="columnItems"/> holds no change.
    /// </summary>
    /// <param name="columnItems">Everything starting at this timing — the change items and
    /// the note(s) that share their moment.</param>
    /// <param name="prevItems">Everything at the previous column, across voices and staves;
    /// the rod takes the furthest-reaching of them, as a paper column aggregates all staves.</param>
    /// <param name="durationIdeal">The plain note-to-note ideal for this pair, i.e. what the
    /// spring would be with no change item in the way.</param>
    /// <remarks>
    /// <para>
    /// LEFT — lily/note-spacing.cc:87-108. The right column is NonMusical and, mid-measure,
    /// has no staff-bar group, so the :103-108 branch is taken: the whole change column's
    /// width is subtracted from the ideal, and the result is floored at half way between the
    /// ideal and the rod. In practice the floor is what binds; the subtraction can only win
    /// when the duration ideal exceeds <c>2 × width + rod</c>, e.g. a whole note before a
    /// clef change. Both are implemented because LilyPond implements both.
    /// </para>
    /// <para>
    /// RIGHT — lily/staff-spacing.cc:166-215. The ideal is the change column's own width plus
    /// the space-alist distance, then lifted to <c>0.3 + min_dist</c> by the :213 correction
    /// when a wide accidental on the next note would otherwise collide.
    /// </para>
    /// <para>
    /// ⚠️ NOT modelled: LilyPond has TWO springs here and Lily# still has one, so the split
    /// is exact only at force 0 (which is where the corpus measures). Under justification
    /// LilyPond stretches the two independently, and for a key or time change the right one
    /// does not stretch at all (shrink-space / semi-shrink-space set
    /// <c>is_stretchable = false</c>, staff-spacing.cc:191, :197). Fixing that needs the real
    /// second column — the same work roadmap item 3 needs at a bar line.
    /// </para>
    /// </remarks>
    internal static MidMeasureChangeSpacing? MidMeasureChangeGaps(
        IReadOnlyList<MusicItem>? columnItems, IReadOnlyList<MusicItem>? prevItems,
        double durationIdeal)
    {
        var (columnWidth, firstChange, lastChange) = MeasureChangeColumn(columnItems);
        if (firstChange == null)
            return null;

        // --- LEFT: note-spacing.cc:79-82 rod, then :105-107 ---
        // The rod is the pure skyline distance between the previous column and this one:
        // the previous item's own ink reach plus each side's extra-spacing-width.
        double prevReach = 0;
        if (prevItems != null)
            foreach (var item in prevItems)
                if (!IsChangeItem(item))
                    prevReach = Math.Max(prevReach, CalculateNoteheadRightExtent(item));
        double leftRod = prevReach
                         + DefaultExtraSpacingWidth
                         + ChangeItemExtraSpacingWidth(firstChange).Left;
        double leftGap = Math.Max(durationIdeal - columnWidth,
                                  (durationIdeal + leftRod) / 2.0);

        // --- RIGHT: staff-spacing.cc:166-215 ---
        double rightRod = RightRod(columnItems!, columnWidth, lastChange!);
        double rightGap = RightGap(columnWidth, lastChange!, rightRod);

        return new MidMeasureChangeSpacing(leftGap, rightGap, leftRod + rightRod);
    }

    /// <summary>
    /// The change column's origin → the next musical column: the SAME quantity
    /// <see cref="MidMeasureChangeGaps"/> puts in the spring, so the drawn glyph and the
    /// reserved space come from one place and cannot drift. Zero when there is no change.
    /// </summary>
    /// <remarks>
    /// This depends only on the items, never on the solved force, so the renderer may
    /// position the change column by hanging it back from the next musical column. That is
    /// also what keeps a change glyph clear of a wide accidental at any line width — the
    /// accidental enters through the rod, exactly as in LilyPond.
    /// </remarks>
    internal static double MidMeasureChangeRightGap(IReadOnlyList<MusicItem>? columnItems)
    {
        var (columnWidth, first, last) = MeasureChangeColumn(columnItems);
        if (first == null)
            return 0;
        return RightGap(columnWidth, last!, RightRod(columnItems!, columnWidth, last!));
    }

    /// <summary>
    /// How far the next change grob in the same column sits from this one's origin: this
    /// glyph's own width plus the break-align gap to <paramref name="next"/>.
    /// </summary>
    internal static double ChangeColumnGlyphAdvance(MusicItem change, MusicItem? next) =>
        ChangeItemColumnWidth(change)
        + (next != null ? BetweenChangeItemsSpace(change, next) : 0);

    /// <summary>
    /// Where <paramref name="change"/> sits inside its change column, measured from the
    /// column's origin. Zero for the first change; later ones follow their predecessors'
    /// widths and the break-align gap between them.
    /// </summary>
    internal static double MidMeasureChangeOffsetWithin(
        IReadOnlyList<MusicItem>? columnItems, MusicItem change)
    {
        if (columnItems == null)
            return 0;

        double offset = 0;
        MusicItem? previous = null;
        foreach (var item in columnItems)
        {
            if (!IsChangeItem(item))
                continue;
            if (previous != null)
                offset += BetweenChangeItemsSpace(previous, item);
            if (ReferenceEquals(item, change))
                return offset;
            offset += ChangeItemColumnWidth(item);
            previous = item;
        }
        return 0;
    }

    /// <summary>
    /// Walks a column's items and returns the change column's total extent right together
    /// with its leftmost and rightmost change grobs. Changes sharing a column are drawn side
    /// by side in break-align order (clef → key-signature → time-signature), separated by
    /// the LEFT one's space-alist entry for the right one's break-align-symbol.
    /// LILYPOND-REF: scm/define-grobs.scm:650-664 break-align-orders.
    /// </summary>
    private static (double Width, MusicItem? First, MusicItem? Last) MeasureChangeColumn(
        IReadOnlyList<MusicItem>? columnItems)
    {
        if (columnItems == null)
            return (0, null, null);

        double width = 0;
        MusicItem? first = null, last = null;
        foreach (var item in columnItems)
        {
            if (!IsChangeItem(item))
                continue;
            if (first == null)
                first = item;
            else
                width += BetweenChangeItemsSpace(last!, item);
            last = item;
            width += ChangeItemColumnWidth(item);
        }
        return (width, first, last);
    }

    /// <summary>
    /// <c>Paper_column::minimum_distance</c> from the change column to the musical one: the
    /// change column's own reach plus whatever the next column's leftmost ink hangs left.
    /// </summary>
    private static double RightRod(
        IReadOnlyList<MusicItem> columnItems, double columnWidth, MusicItem lastChange)
    {
        double reach = 0;
        foreach (var item in columnItems)
            if (!IsChangeItem(item))
                reach = Math.Max(reach, MusicalColumnLeftReach(item));
        return columnWidth + ChangeItemExtraSpacingWidth(lastChange).Right + reach;
    }

    /// <summary>
    /// <c>Staff_spacing::get_spacing</c>'s ideal for the change column → next note, with the
    /// :213 minimum-distance correction.
    /// </summary>
    /// <remarks>
    /// The space-alist consulted belongs to the RIGHTMOST break-aligned grob in the column
    /// (<c>Spacing_interface::extremal_break_aligned_grob</c> with <c>d == LEFT</c> picks the
    /// one whose right edge is largest), which under break-align-orders is the last of
    /// clef / key / time present.
    /// LILYPOND-REF: lily/staff-spacing.cc:166-175 (ideal), :213-215 (the 0.3 correction).
    /// </remarks>
    private static double RightGap(double columnWidth, MusicItem lastChange, double rightRod) =>
        Math.Max(columnWidth + ChangeItemSpaceToNextNote(lastChange),
                 SpringHeadroom + rightRod);

    /// <summary>
    /// Width that leading grace notes need in FRONT of their main note's column.
    /// Grace notes hang to the left of the note (like a mid-measure clef change),
    /// so the spring into the column reserves their group width. When several
    /// voices have grace at the same moment the groups align, so the MAX is taken.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 — grace columns precede
    ///   the main note's musical column; their span is reserved before it.
    /// The width equals <see cref="CalculateGraceGroupSpringWidth"/> (grace springs
    /// plus the grace→main rod), the same measure GraceNoteEngraver uses to PLACE
    /// the group, so reserved space and drawn space agree.
    /// </remarks>
    internal static double LeadingGracePrefixWidth(IEnumerable<MusicItem>? items,
        bool includeMainAccidental = false)
    {
        if (items == null) return 0;
        double w = 0;
        foreach (var item in items)
        {
            var grace = item switch
            {
                NoteItem n => n.LeadingGrace,
                ChordItem c => c.LeadingGrace,
                _ => ImmutableArray<GraceNoteInfo>.Empty
            };
            if (grace.IsDefaultOrEmpty)
                continue;
            double hang = CalculateGraceGroupSpringWidth(grace);
            // At a LINE START the grace hangs left of the main item's OWN left ink
            // (its accidental) with nothing before it, so the front spring must
            // reserve grace + accidental, not their max — otherwise the grace
            // overflows into the clef/key/time prefix. (Mid-line the previous note
            // already provides that room, so the accidental is left out there.)
            bool hasAccidental = item switch
            {
                NoteItem n => n.Accidental != null,
                ChordItem c => c.Notes.Any(cn => cn.Accidental != null),
                _ => false
            };
            if (includeMainAccidental && hasAccidental)
                hang += CalculateLeftExtent(item);
            w = Math.Max(w, hang);
        }
        return w;
    }

    /// <summary>
    /// The widest leading grace run's ANCHOR-TO-ANCHOR span among <paramref name="items"/> —
    /// first grace origin to main note origin, with no ink allowance.
    /// </summary>
    /// <remarks>
    /// The companion of <see cref="LeadingGracePrefixWidth"/>, which is the same runs
    /// measured WITH the leading ink. The two go to different halves of the spring — see
    /// <see cref="SpringIntoGraceRun"/> — so they are separate readings rather than one
    /// number with a fudge.
    /// </remarks>
    internal static double LeadingGraceRunSpan(IEnumerable<MusicItem>? items)
    {
        if (items == null) return 0;
        double w = 0;
        foreach (var item in items)
            w = Math.Max(w, LeadingGraceRunSpan(item));
        return w;
    }

    /// <summary>One item's leading grace run span, measured the way the run is PLACED.</summary>
    /// <remarks>
    /// ⚠️ The main item has to go in. <c>GraceColumns</c> answers a different span without
    /// it — 0.2 wider on the ledger's book, which is the first grace's own left ink — and
    /// GraceNoteEngraver places the run WITH it. Feeding the mainItem-less number to the
    /// ideal put that ink straight back into the approach the scaling had just taken out.
    /// </remarks>
    internal static double LeadingGraceRunSpan(MusicItem? item)
    {
        if (item == null) return 0;
        var grace = GraceNotesOf(item);
        return grace.IsDefaultOrEmpty ? 0 : GraceColumns(grace, item).Span;
    }

    /// <summary>
    /// The spring from a mid-line bar line to the first musical column after it —
    /// the SINGLE implementation shared by both spring systems.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A transcription of <c>Staff_spacing::get_spacing</c>. The gap after a bar line is
    /// governed by the BarLine space-alist, NOT by the first note's duration: LilyPond
    /// reaches this pair through Staff_spacing, not Note_spacing, so duration space never
    /// enters. A mid-line bar line always has <c>break_status_dir () == CENTER</c>, which
    /// selects `next-note` (semi-fixed-space 0.9) and never `first-note`; the system-start
    /// case is BreakAlignSpacing.FirstNoteSpring.
    /// </para>
    /// <para>
    /// FRAME: LilyPond measures column origin → column origin, so its <c>fixed</c> opens at
    /// <c>last_ext[RIGHT]</c> — the bar line's right edge expressed in the boundary column's
    /// frame, which is why a clef sitting before the bar line makes that term jump from 0.19
    /// to ~3.04. This spring starts AT the bar line's ink right edge, so that term is
    /// identically 0 here and every quantity below is LilyPond's minus <c>last_ext[RIGHT]</c>.
    /// Measured on 2.24.4, bar-line ink right edge → next notehead ink left edge is
    /// 0.900000 both with and without a clef change at the bar line.
    /// </para>
    /// <para>
    /// The optical correction for a DOWN stem just after the bar line is
    /// <see cref="BarlineToNextNotesCorrection"/>; the measured 2x2 that identifies it as a
    /// STEM effect rather than a clef one is recorded there.
    /// </para>
    /// <para>
    /// This lived only in MeasureLayouter, so the item system priced the same gap as
    /// a quarter note's duration space — 3.6 against the correct 0.9, ~2.7 ss too
    /// wide on every measure it estimated.
    /// </para>
    /// LILYPOND-REF: lily/staff-spacing.cc:118-221 Staff_spacing::get_spacing;
    ///   scm/define-grobs.scm:301 BarLine space-alist
    ///   (next-note . (semi-fixed-space . 0.9)).
    /// LILYPOND-REF: lily/spacing-spanner.cc:484-489 breakable_column_spacing —
    ///   full-measure-extra-space is `situational_space` on THIS spring, keyed on the
    ///   measure AFTER the bar line, so the caller decides and passes it in.
    /// </remarks>
    internal static Spring BarlineToFirstColumnSpring(
        IReadOnlyList<MusicItem>? firstItems, bool fillsMeasure)
    {
        // `last_grob` is the RIGHTMOST break-aligned grob in the boundary column, which is
        // the bar line only when nothing else opens the measure. A key or time change shares
        // that column, so IT owns the space-alist consulted here and `fixed` opens at its ink
        // right edge instead of the bar line's — COORDINATE_AUDIT.md §4.7.3.
        // LILYPOND-REF: lily/staff-spacing.cc:125-126
        //   Spacing_interface::extremal_break_aligned_grob (me, LEFT, ...).
        var boundary = BoundaryChangePrefix(firstItems);

        double distance;
        double fixedDistance;
        bool isStretchable;
        if (boundary is var (prefix, lastChange) && boundary.HasValue)
        {
            var def = ChangeItemSpaceDef(lastChange);
            distance = def.Distance;
            // fixed opens at last_ext[RIGHT] — in this spring's frame, the bar line's own
            // width is already behind us, so that is the prefix.
            // LILYPOND-REF: lily/staff-spacing.cc:166.
            fixedDistance = prefix + (def.SplitsFixed ? distance / 2 : 0);
            isStretchable = def.Stretchable;
        }
        else
        {
            distance = EngravingDefaults.BarLineToNextNoteSpace;
            // semi-fixed-space: fixed += d/2, ideal = fixed + d/2. `is_stretchable` stays
            // TRUE — only shrink-space and semi-shrink-space clear it, so the resulting
            // spring is NOT rigid. (LilySharp used to pass inverseStretchStrength 0 here on
            // the strength of a comment claiming semi-fixed was unstretchable; the source
            // says otherwise.)
            // LILYPOND-REF: lily/staff-spacing.cc:164-180.
            fixedDistance = distance / 2;
            isStretchable = true;
        }
        // Every arm involved puts the IDEAL at last_ext[RIGHT] + distance; they differ only
        // in what lands in `fixed`. LILYPOND-REF: lily/staff-spacing.cc:169-198.
        double ideal = (boundary?.Prefix ?? 0) + distance;

        // Fixed BEFORE situational_space and before the min-distance correction — the
        // order matters, both of those move `ideal` away from `fixed` without making the
        // spring any more stretchable.
        // LILYPOND-REF: lily/staff-spacing.cc:200.
        double stretchability = isStretchable ? ideal - fixedDistance : 0;

        // LILYPOND-REF: lily/staff-spacing.cc:202-204 — 'situational_space' passed by the
        //   caller could include full-measure-extra-space.
        double situationalSpace = fillsMeasure ? FullMeasureExtraSpace : 0;
        ideal += situationalSpace;

        // min_dist = Paper_column::minimum_distance — a PURE skyline distance between the
        // two columns, with no space-alist value in it. See GetBarlineToItemMinimum.
        // LILYPOND-REF: lily/staff-spacing.cc:210.
        double minDistance = 0;

        double startLeadGrace = 0;
        if (firstItems != null)
        {
            if (boundary is var (bPrefix, bLast) && boundary.HasValue)
            {
                // The boundary column reaches to the change's ink right edge plus ITS
                // extra-spacing-width (KeySignature declares (0.0 . 1.0), TimeSignature
                // (0.0 . 0.8) — not the default), and the musical column reaches back by its
                // leftmost ink plus that grob's own. This is the only term that carries an
                // opening accidental into the gap, and it is what decides probe K.
                double reach = 0;
                foreach (var item in firstItems)
                    if (!IsChangeItem(item))
                        reach = Math.Max(reach, MusicalColumnLeftReach(item));
                minDistance = bPrefix + ChangeItemExtraSpacingWidth(bLast).Right + reach;
            }
            else
            {
                // Skyline reach: bar line → first item (max across all voices). A clef change
                // opening the measure is NOT on this side of the bar line (break-align-orders
                // puts clef before staff-bar), so it raises nothing here.
                foreach (var item in firstItems)
                {
                    if (item is ClefChangeItem)
                        continue;
                    minDistance = Math.Max(minDistance,
                        CalculateSkylineDistance(null, item, staffY: 0));
                }
            }

            // Leading grace notes on the first note hang left of its column, after
            // the bar line (LilyPond gives the grace its own column between the
            // bar line and the main note).
            startLeadGrace = LeadingGracePrefixWidth(firstItems, includeMainAccidental: true);
        }

        if (startLeadGrace > 0)
        {
            // The grace is now the FIRST musical column after the bar line, so the
            // barline→grace gap uses tight GRACE spacing (spacing-increment). The
            // whole front block is rigid (grace columns don't stretch), so this branch
            // does NOT take the semi-fixed spring above.
            // LILYPOND-REF: scm/define-grobs.scm:1721 GraceSpacing
            //   (spacing-increment . 0.8) — grace columns space tighter than notes.
            // LILYPOND-REF: lily/grace-spacing-engraver.cc — barline → first grace
            //   column → … → main column.
            double graceApproach = GraceSpacingParameters.Default.SpacingIncrement;
            double front = Math.Max(Math.Max(distance, minDistance),
                                    graceApproach + startLeadGrace);
            return new Spring(front + situationalSpace, front, inverseStretchStrength: 0);
        }

        // The optical correction for a DOWN stem standing just after the bar line, applied
        // to BOTH fixed and ideal — and AFTER stretchability was taken, so it widens the
        // gap without making the spring any more stretchable.
        // LILYPOND-REF: lily/staff-spacing.cc:206-208.
        double opticalCorrection = BarlineToNextNotesCorrection(firstItems);
        fixedDistance += opticalCorrection;
        ideal += opticalCorrection;

        // "Ensure that the 'fixed' distance will leave a gap of at least 0.3 ss."
        // LILYPOND-REF: lily/staff-spacing.cc:212-215.
        double minDistanceCorrection =
            Math.Max(0.0, StaffSpacingFixedHeadroom + minDistance - fixedDistance);
        fixedDistance += minDistanceCorrection;
        ideal = Math.Max(ideal, fixedDistance);

        // LILYPOND-REF: lily/staff-spacing.cc:217-220 — the compress strength is measured
        //   against `fixed`, not against the minimum, so it is NOT the Spring 3-argument
        //   constructor's default.
        // No ApplyMergeSpringsHeadroom call follows: breakable_column_spacing does hand this
        // wish on to merge_springs, but the correction just above already guarantees
        // ideal >= fixed >= 0.3 + min_distance, so the headroom is provably a no-op here.
        return new Spring(ideal, minDistance,
                          Math.Max(0.0, stretchability),
                          Math.Max(0.0, ideal - fixedDistance));
    }

    /// <summary>
    /// The ITEM spring system's share of a mid-measure change column, or null when this pair
    /// does not touch one. Its total across the pair matches the timing-column system's
    /// single spring, which is what keeps line-break width estimates honest.
    /// </summary>
    /// <param name="spacingItems">The measure's spacing items, in order.</param>
    /// <param name="leftIndex">Index of the LEFT item of the pair being sprung.</param>
    /// <param name="durationIdeal">The pair's plain duration ideal, used only when this is
    /// the note → change-column gap.</param>
    /// <remarks>
    /// The item system gives a change item its own slot, so it already has the two springs
    /// LilyPond has and can carry the split directly, where the timing-column system has to
    /// lump both into one (a change shares the next note's timing). The three cases are the
    /// column's LEFT gap, an internal gap between two changes sharing the column, and the
    /// remainder of the RIGHT gap from the last change to the note.
    /// <para>
    /// These come back rigid. The item system feeds width ESTIMATES
    /// (<see cref="CalculateMeasureIdealWidth"/>) and the break gate, where what matters is
    /// that the ideals sum to the same total the layout will produce; modelling how the two
    /// LilyPond springs share a stretch needs the real column (roadmap item 3).
    /// </para>
    /// </remarks>
    private static Spring? ChangeColumnItemSpring(
        IReadOnlyList<MusicItem> spacingItems, int leftIndex, double durationIdeal)
    {
        var left = spacingItems[leftIndex];
        var right = spacingItems[leftIndex + 1];
        bool leftIsChange = IsChangeItem(left);
        bool rightIsChange = IsChangeItem(right);
        if (!leftIsChange && !rightIsChange)
            return null;

        // change → change: the left one's own width plus their break-align gap.
        if (leftIsChange && rightIsChange)
            return Rigid(ChangeItemColumnWidth(left) + BetweenChangeItemsSpace(left, right));

        var columnItems = ChangeColumnAt(spacingItems, leftIsChange ? leftIndex : leftIndex + 1);

        // note → the column's origin.
        if (!leftIsChange)
        {
            var gaps = MidMeasureChangeGaps(columnItems, new[] { left }, durationIdeal);
            return gaps is { } g ? Rigid(g.LeftGap) : null;
        }

        // last change → the note: what is left of the right gap once the column's own
        // glyphs are subtracted, since the right gap is measured from the column ORIGIN.
        return Rigid(MidMeasureChangeRightGap(columnItems)
                     - MidMeasureChangeOffsetWithin(columnItems, left));

        static Spring Rigid(double d) => new(Math.Max(0, d), Math.Max(0, d), 0);
    }

    /// <summary>
    /// The change column containing <paramref name="index"/>: the whole run of changes it
    /// belongs to, plus the musical item that shares their moment.
    /// </summary>
    private static List<MusicItem> ChangeColumnAt(IReadOnlyList<MusicItem> items, int index)
    {
        int start = index;
        while (start > 0 && IsChangeItem(items[start - 1]))
            start--;

        var column = new List<MusicItem>();
        for (int k = start; k < items.Count; k++)
        {
            column.Add(items[k]);
            if (!IsChangeItem(items[k]))
                break;
        }
        return column;
    }

    /// <summary>
    /// Creates a spring for a timing column based on duration.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
    /// Simplified spring creation for timing-based columns without skyline collision detection.
    /// Uses duration-based spacing for ideal distance.
    /// </remarks>
    public static Spring CreateTimingSpring(Fraction duration,
                                            double? baseShortestDuration = null,
                                            NoteSpacingParameters? noteParams = null)
    {
        // LILYPOND-REF: lily/spacing-basic.cc:109 note_spacing() - increment
        double defaultMin = EngravingDefaults.SpacingIncrement;

        // LILYPOND-REF: lily/spacing-basic.cc:107 note_spacing() - duration space
        double idealDistance = CalculateDurationSpace(duration,
            baseShortestDuration ?? EngravingDefaults.BaseShortestDuration);

        // Ensure minimum distance
        idealDistance = Math.Max(idealDistance, defaultMin);

        // min_distance for timing springs (no skyline collision)
        double minDistance = defaultMin;

        // LILYPOND-REF: lily/note-spacing.cc:229-264 strict_note_spacing
        // In strict mode, enforce minimum distance = ideal distance for proportional spacing
        var np = noteParams ?? NoteSpacingParameters.Default;
        if (np.StrictNoteSpacing)
        {
            minDistance = Math.Max(minDistance, idealDistance);
        }

        // LILYPOND-REF: lily/spacing-basic.cc:115 note_spacing() - inverse_stretch
        double inverseStretchStrength = Math.Max(0.1, idealDistance - defaultMin);

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>
    /// Creates a spring scaled by the shortest currently-playing note duration across all voices.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:107-162 Spacing_spanner::note_spacing
    /// LILYPOND-REF: lily/spacing-engraver.cc:200-253 stop_translation_timestep
    ///
    /// LP's per-column spring formula:
    ///   <c>fraction = delta_t / shortest_playing</c>
    ///   <c>len = options-&gt;get_duration_space(shortest_playing)</c>
    ///   <c>spring = Spring(fraction * len, fraction * min)</c>
    /// where <c>shortest_playing</c> is the min duration over all voices' notes that are
    /// playing at the left column of the spring (NOT just the time delta to the next column).
    /// In monophonic music <c>shortest_playing == delta_t</c> and this collapses to the
    /// existing <see cref="CreateTimingSpring(Fraction, double?, NoteSpacingParameters?)"/>;
    /// in polyphonic music it produces tighter springs when a faster voice is sounding
    /// underneath a slower voice.
    /// </remarks>
    public static Spring CreateTimingSpringMultiVoice(
        Fraction segmentDuration,
        Fraction shortestPlayingDuration,
        double? baseShortestDuration = null,
        NoteSpacingParameters? noteParams = null,
        Fraction? measureLength = null)
    {
        // ⚠️ The delta_t fallback is LILY#'S OWN. LilyPond never asks this question —
        // a column with no playing grobs is pruned before note_spacing runs — and its
        // own guard for the impossible case (lily/spacing-basic.cc:113-120) is a
        // programming_error plus a WHOLE note, not delta_t. This line used to cite
        // those very lines as if they prescribed delta_t; they do not. Lily# keeps
        // skip-only columns alive (lead-sheet slot grids), and for those delta_t
        // equals the slot's own duration, which is what the measured recipe wants.
        if (shortestPlayingDuration <= Fraction.Zero)
            shortestPlayingDuration = segmentDuration;
        if (shortestPlayingDuration <= Fraction.Zero)
            return CreateTimingSpring(segmentDuration, baseShortestDuration, noteParams);

        // LILYPOND-REF: lily/spacing-basic.cc:144 — clamp shortest_playing to the MEASURE LENGTH
        // (a multi-measure-rest guard), NOT to this segment's delta_t. Clamping to delta_t was a
        // bug: it forced fraction = delta_t / shortest_playing = 1 for every sub-beat column, so an
        // interleaved polyrhythm column (a triplet note landing between two straight eighths) took a
        // FULL note's duration_space instead of its proportional share. The proportional part below
        // (fraction * len) is exactly what keeps the other voice's eighths evenly spaced: two sub-
        // gaps of a note sum back to that note's space only when shortest_playing stays the note.
        Fraction effectivePlaying = shortestPlayingDuration;
        if (measureLength is { } mlen && mlen > Fraction.Zero && mlen < effectivePlaying)
            effectivePlaying = mlen;

        double defaultMin = EngravingDefaults.SpacingIncrement;
        double bsd = baseShortestDuration ?? EngravingDefaults.BaseShortestDuration;

        // LILYPOND-REF: lily/spacing-basic.cc:151 — len = get_duration_space(shortest_playing)
        double len = CalculateDurationSpace(effectivePlaying, bsd);
        // LILYPOND-REF: lily/spacing-basic.cc:155-156 — fraction = delta_t / shortest_playing
        double fraction = segmentDuration.ToDouble() / effectivePlaying.ToDouble();

        // LILYPOND-REF: lily/spacing-basic.cc:157 — Spring(fraction * len, fraction * min).
        // BOTH terms scale by fraction. A sub-beat interleaved column (fraction < 1 — e.g. a triplet
        // note splitting one voice's straight eighth into two sub-gaps) gets its PROPORTIONAL share,
        // not a full-notehead floor. Flooring the ideal at the whole increment (as this did before)
        // inflated the shorter half of the split gap, so the other voice's eighths spread wider on
        // exactly the beats the triplet stems land on. Genuine overlap is still blocked by the
        // skyline rod computed in CreateInterColumnSpring — the ideal need not reserve a full head.
        double idealDistance = fraction * len;
        double minDistance = fraction * defaultMin;

        var np = noteParams ?? NoteSpacingParameters.Default;
        if (np.StrictNoteSpacing)
            minDistance = Math.Max(minDistance, idealDistance);

        // LILYPOND-REF: lily/spacing-basic.cc:160-161 — inverse_stretch_strength = fraction * max(0.1, len - min)
        double inverseStretchStrength = Math.Max(0.1, fraction * Math.Max(0.1, len - defaultMin));

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>A one-item sequence, for the single-voice callers of
    /// <see cref="ApplyLeftHeadWidth"/> (which takes the simultaneous left column).</summary>
    private static IEnumerable<MusicItem> One(MusicItem item)
    {
        yield return item;
    }

    /// <summary>
    /// Refines a duration-based ideal to the LEFT note column's actual head width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:77 Note_spacing::get_spacing —
    ///   ideal = base.ideal_distance() - increment + left_head_end.
    /// The duration space assumes a generic notehead (spacing-increment). LilyPond
    /// swaps that generic width for the left column's ACTUAL head width, so a wide
    /// head (half 1.376 / whole 1.96) reserves proportionally more room than a
    /// black head (1.304). For a black head the net adjustment is
    /// 1.304 - 1.2 = +0.104 ss — the uniform gap LilyPond has over Lily#'s raw
    /// duration spacing. A rest uses its glyph's right extent instead (LilyPond's
    /// g = the rest grob): a quarter rest (~0.95) is NARROWER than the increment,
    /// so the space after a rest shrinks, matching LilyPond ("a quarter rest gets
    /// almost 0.5 ss less horizontal space than a note"). The widest such left
    /// item wins (a safe choice for simultaneous voices); non-musical items leave
    /// the ideal unchanged.
    /// </remarks>
    /// <summary>Whether this item's heads are drawn in the cue font.</summary>
    /// <remarks>
    /// ⚠️ ONE DECISION, TWO READERS (EngravingDefaults.CueDesignSize says so in writing): the
    /// box a cue column reserves has to be the box its head is drawn in. This predicate is the
    /// spacing side of the pair SharedRenderer.Noteheads reads on the drawing side.
    /// </remarks>
    private static bool IsCueItem(MusicItem item) => item switch
    {
        NoteItem n => n.IsCue,
        ChordItem c => c.IsCue,
        _ => false
    };

    /// <summary>
    /// Whether the refinement runs at all over this column pair — it does not across a VOICE
    /// boundary, where the spring keeps its raw duration ideal.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:352-358 uses a Note_spacing wish only when one of
    /// its <c>right-items</c> IS the column being spaced to, and :380-391 leaves the base spring
    /// untouched when no wish matches. A wish belongs to a VOICE, so the pair that straddles two
    /// of them is refined by neither.
    /// <para>
    /// ⚠️ THE READING IS NOT ABOUT CUES, though a cue region is the only place Lily# can spell a
    /// sequential voice change today. audit/lp-geometry/probes/voice-boundary-spacing.ly measured
    /// it with no cue in the book at all: VB-VOICE is four full-size quarters whose last two sit
    /// in a plain <c>\new Voice</c>, and its boundary loses the same 0.104200 that
    /// <c>cue.column.main-to-cue</c> records. VB-OUT is the half that says the refinement is
    /// ABSENT rather than fed a smaller number — leaving a cue, where the LEFT head is the small
    /// one, costs the same 2.898044999134612 to fifteen digits instead of 2.409193907.
    /// </para>
    /// <para>
    /// ⚠️ A NULL right side is NOT a boundary — a bar line is one column for both voices, so the
    /// left voice's wish reaches it and the refinement runs. MEASURED, because the first version
    /// of this note asserted it and cited <c>barline.prev.*</c>, which are non-cue books and
    /// observe no such thing: voice-boundary-spacing.ly VBB-CTL / VBB-CUE put the same four
    /// quarters before a plain bar line with and without the last two in a CueVoice, and the
    /// last-head-to-bar-line gap is 2.787959284848899 against 2.370536764280867. The cue book's
    /// gap IS narrower, so the refinement does run here — the direction is right.
    ///   departs from: the SIZE of it. LilyPond narrows that gap by 0.417422520568032 and this
    ///     engine narrows it by the whole head-width term — 1.304200 traded for the cue head's
    ///     box, measured 0.488853432. The port improved the reading (the gap used to be wrong by
    ///     the full 0.417) without closing it.
    ///   goes away when: a CUE ITEM CAN REPORT ITS OWN STEM RANGE. ⚠️ THE LEFTOVER IS NAMED
    ///     (2026-08-04) AND IT IS NOT THIS PREDICATE'S: it is 1/14 exactly — 0.5/7, i.e.
    ///     <see cref="NoteSpacingParameters.StemSpacingCorrection"/> over the hardcoded 7 of
    ///     different_directions_correction, halved at a bar line. The earlier "3.4e-12
    ///     resemblance, do not fit it" was an artefact of subtracting the nine-digit
    ///     0.488851092; at full precision the trade is 0.488851091996604 and the difference is
    ///     1/14 to 6.4e-16. See <see cref="CalculateStemCorrectionToBarline"/> — LilyPond's
    ///     |intersect| is 4 staff positions for the cue book and 6 for the control, because
    ///     ITS cue stem is shorter, and −(6−4)/7 × 0.25 is the whole of it. Lily#'s
    ///     <see cref="StemSpacingInfo"/> does not consult <c>IsCue</c>, so it spends −3/14 on
    ///     both. MEASURED, DRIVEN AND CONTROLLED: probe voice-boundary-spacing.ly section D.
    ///   observed by: <c>cue.barline.prev.cue-head</c>, against
    ///     <c>cue.barline.prev.full-head-control</c> (opened 2026-08-03 from the numbers above,
    ///     without re-measuring). The control is EXACT and the cue records −0.071430911, which
    ///     is the unnamed 0.071428571431968 PLUS the 0.000002340 six-digit rounding of
    ///     design-13's head that already stopped <c>cue.column.step</c> — that term arriving
    ///     here by a second, independent reading is what says this gap really is spending the
    ///     head-width term and not some other quantity that happens to sit nearby.
    /// </para>
    /// <para>
    /// ⚠️ TWO PLACES WHERE THIS PREDICATE IS NARROWER THAN LilyPond'S CONDITION, both because
    /// <see cref="NoteItem.IsCue"/> is a per-note flag and not a region identity:
    ///   departs from: :352-358 for ADJACENT cue regions. <c>cue { … } cue { … }</c> is two
    ///     CueVoice contexts in LilyPond (probe cue-span.ly C-TWO shows both are made), so their
    ///     shared edge IS a voice boundary; here both sides answer IsCue and the pair is refined.
    ///   departs from: :352-358 again when SIMULTANEOUS voices disagree about being cued — the
    ///     loop below gives up and refines, where LilyPond builds one wish per voice and lets
    ///     merge_springs (:380-393) combine whatever each of them decided.
    ///   goes away when: an item can say WHICH region it belongs to, not merely that it is cued.
    ///   observed by: NOTHING in either case. No fixture or sample writes two cue blocks back to
    ///     back or a cue against a simultaneous non-cue voice (grep, 2026-08-03).
    /// </para>
    /// </remarks>
    private static bool CrossesVoiceBoundary(
        IEnumerable<MusicItem> leftItems, IEnumerable<MusicItem>? rightItems)
    {
        if (rightItems is null)
            return false;
        bool leftCue = false, sawLeft = false;
        foreach (var l in leftItems)
        {
            if (sawLeft && IsCueItem(l) != leftCue)
                return false;   // simultaneous voices disagree — see the second departure above
            leftCue = IsCueItem(l);
            sawLeft = true;
        }
        if (!sawLeft)
            return false;
        foreach (var r in rightItems)
            if (IsCueItem(r) != leftCue)
                return true;
        return false;
    }

    internal static Spring ApplyLeftHeadWidth(
        Spring spring, IEnumerable<MusicItem> leftItems, IEnumerable<MusicItem>? rightItems = null)
    {
        if (CrossesVoiceBoundary(leftItems, rightItems))
            return spring;

        double leftHeadEnd = 0;
        bool any = false;
        foreach (var p in leftItems)
        {
            double w = p switch
            {
                // The head's INK right edge, not its advance. LilyPond reads
                // g->extent (col, X_AXIS)[RIGHT] — a stencil extent — and the two differ:
                // a whole head advances 1.960000 but its stencil reaches 1.962002. Feeding
                // the advance made every closing gap 0.002 narrow than LilyPond's, which is
                // the whole of barline.prev.whole-note's former residual.
                // ...asked of the font the head is actually DRAWN in. A cue head is not the
                // twenty design scaled — it is the THIRTEEN design read at magstep(-4)
                // (EngravingDefaults.CueFont, the same object the renderer draws with), and
                // 1.304200 * magstep(-4) = 0.821594517 is not LilyPond's 0.815348908. Reading the
                // full-size box here was the whole of cue.column.step's +0.488851092: the
                // renderer shrank the head and the spring did not hear about it.
                //   ⚠️ A REST INSIDE A CUE STILL PRICES FULL SIZE. RestItem carries no IsCue,
                //   so there is nothing to ask; LilyPond's left grob would be the cue-sized
                //   rest. No point observes it — see the branch below.
                NoteItem or ChordItem => GlyphMetrics.GetNoteheadBBox(
                    IsCueItem(p) ? EngravingDefaults.CueFont : GlyphMetrics.Design20,
                    GetNoteValue(p)).Right,
                // A rest is drawn glyph-left-aligned at its column, so its right
                // extent from the column origin is the rest stencil's right edge.
                // ⚠️ A SPACER rest engraves nothing: LilyPond's left head is a real
                // grob read off the note column (note-spacing.cc:46-70 — the "rest"
                // object or first_head), and a spacer has neither. Pricing the glyph
                // of a rest nobody draws put a phantom half-rest 1.5 into every
                // chords-row gap (chord.symbol-width.half-spring-control caught it:
                // MEASURED, probe chord-symbol-width.ly CAL2, a staff-less row's
                // columns carry NO spacing wishes at all, so LilyPond's ideal there
                // is the bare duration spring).
                RestItem r => r.IsSpacer ? double.NaN : GlyphMetrics.GetRestBBox(GetNoteValue(p)).Right,
                _ => double.NaN
            };
            if (double.IsNaN(w))
                continue;
            leftHeadEnd = Math.Max(leftHeadEnd, w);
            any = true;
        }
        if (!any)
            return spring;

        double ideal = Math.Max(EngravingDefaults.SpacingIncrement,
            spring.IdealDistance + leftHeadEnd - EngravingDefaults.SpacingIncrement);
        // LILYPOND-REF: lily/note-spacing.cc:113 base.set_ideal_distance (…) — the SETTER,
        // which leaves the duration-built compressibility alone (lily/spring.cc:131-141).
        return spring.WithIdealDistance(ideal);
    }

    /// <summary>
    /// Computes the shortest playing duration across all voices at a given musical timing,
    /// matching LP's <c>shortest-playing-duration</c> column property.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-engraver.cc:200-253 stop_translation_timestep
    /// "playing" = a note that started at or before <paramref name="timing"/> and ends strictly after it.
    /// Returns <c>Fraction.Zero</c> if no voice has a note playing at <paramref name="timing"/>.
    /// </remarks>
    public static Fraction ComputeShortestPlayingAt(Fraction timing, IEnumerable<Measure> allMeasures)
    {
        // A spacer (`s`) never outranks a REAL playing note: LilyPond's spacing engraver
        // reads playing durations off the rhythmic grobs it acknowledges, and a skip
        // engraves none — so an `s8` under a sustained c'4 must not drag shortest-playing
        // down to an eighth (LP regression beam-skip.ly: the quarter keeps its plain
        // quarter spring). But when ONLY spacers play — a skip-only stretch, or a
        // lead-sheet row whose every item is a slot spacer — the spacers' durations stay
        // the answer. ⚠️ THAT KEEP IS LILY#'S OWN, not LilyPond's: LP never faces the
        // case (a column with no playing grobs is pruned before note_spacing runs, and
        // if one slipped through, spacing-basic.cc:113-120 would programming_error and
        // charge a WHOLE note — not delta_t). Lily#'s surviving skip-only columns ride
        // the delta_t fallback in CreateTimingSpringMultiVoice, and on the slot grids
        // the rows build, a slot's duration and its delta_t are the same number — the
        // measured lead-sheet recipe (ApplyRowCommandColumnSprings) is calibrated on
        // top of exactly that, which is why the spacer durations must keep flowing here.
        Fraction shortest = Fraction.Zero;
        Fraction shortestSpacer = Fraction.Zero;
        bool found = false;
        bool foundSpacer = false;

        foreach (var m in allMeasures)
        {
            Fraction t = Fraction.Zero;
            foreach (var item in m.Items)
            {
                Fraction end = t + item.Duration;
                // The note "plays" at `timing` iff t <= timing < end.
                if (t <= timing && timing < end && item.Duration > Fraction.Zero)
                {
                    if (item is RestItem { IsSpacer: true })
                    {
                        if (!foundSpacer || item.Duration < shortestSpacer)
                        {
                            shortestSpacer = item.Duration;
                            foundSpacer = true;
                        }
                    }
                    else if (!found || item.Duration < shortest)
                    {
                        shortest = item.Duration;
                        found = true;
                    }
                }
                t = end;
            }
        }

        return found ? shortest : shortestSpacer;
    }


    /// <summary>
    /// Creates all springs for a measure.
    /// </summary>
    /// <param name="measure">The measure to create springs for</param>
    /// <param name="baseShortestDuration">Optional spacing base-shortest-duration override;
    /// null uses the score default.</param>
    /// <param name="nextMeasure">The measure FOLLOWING this one, when known — a clef change
    /// opening it is drawn before the shared bar line, so its width is charged to this
    /// measure's closing spring (<see cref="BoundaryClefAllowance"/>). Must mirror
    /// MeasureLayouter.CreateTimingSprings, which does the same on the column side.</param>
    /// <returns>Array of springs (one between each pair of adjacent reference points)</returns>
    public static ImmutableArray<Spring> CreateSpringsForMeasure(Measure measure,
                                                                 double? baseShortestDuration = null,
                                                                 Measure? nextMeasure = null)
    {
        if (measure.Items.Length == 0)
            return ImmutableArray<Spring>.Empty;

        // LILYPOND-REF: lily/spacing-spanner.cc:200-280
        // Filter out loose items (tuplet brackets, fermata marks, etc.)
        // that don't participate in horizontal spacing
        var spacingItems = new List<MusicItem>();
        foreach (var item in measure.Items)
        {
            if (!item.IsLoose)
                spacingItems.Add(item);
        }

        if (spacingItems.Count == 0)
            return ImmutableArray<Spring>.Empty;

        // NOTE: a full-measure rest gets ORDINARY springs here. LilyPond does the
        // same — a rested bar is spaced like any other bar, and the compaction of a
        // multi-measure rest comes from the run-level ROD
        // (Multi_measure_rest::calculate_spacing_rods, ported as MmrRodDistance)
        // applied across the collapsed run, NOT from shrinking each measure. The
        // earlier per-measure approximation here was wrong in BOTH directions:
        // measured against LP 2.24.4 it made an `R1*9` run ~108% too wide (the
        // approximation is linear in the count where LP's rod grows ~2·log2(count))
        // and a lowercase `r1` bar ~25% too narrow (LP spaces it as a normal bar:
        // `r1`×3 spans 31.214 ss with or without \compressMMRests, vs `R1*3` 20.810).

        var springs = new List<Spring>();

        // Spring from start barline to first item — the SAME builder the timing-column
        // system uses, so the leading grace / change-glyph / skyline reservations and
        // the BarLine space-alist value cannot drift between the two. This used to
        // price the gap as the first note's duration space (3.6 for a quarter against
        // the correct 0.9): LilyPond reaches a bar line → note pair through
        // Staff_spacing, where duration never enters.
        // A measure filled by a single note/chord gets LP's full-measure-extra-space
        // on THIS spring (barline → first column), not on the note → barline spring:
        // LP passes it as `situational_space` to Staff_spacing::get_spacing, keyed on
        // the measure that FOLLOWS the barline.
        // LILYPOND-REF: lily/spacing-spanner.cc:484-489 breakable_column_spacing.
        var firstItem = spacingItems[0];
        var firstSpring = BarlineToFirstColumnSpring(new[] { firstItem }, FillsMeasure(measure));
        springs.Add(firstSpring);

        // Springs between items (the spring into a grace-bearing note reserves its grace;
        // a pair touching a mid-measure clef/key/time change is priced by that change's
        // column, so this estimate totals what the timing-column layout will produce and
        // line breaking does not mis-measure change measures — pinned by
        // SpacingInvariantTests.BothSpringSystems_AgreeAcrossAMidMeasureChangeColumn).
        for (int i = 0; i < spacingItems.Count - 1; i++)
        {
            var prevItem = spacingItems[i];
            var nextItem = spacingItems[i + 1];
            var spring = CreateSpring(prevItem, nextItem, prevItem.Duration,
                baseShortestDuration: baseShortestDuration);
            // Swap the generic spacing-increment for the LEFT column's real head
            // width, exactly as the timing-column system does (MeasureLayouter) —
            // this is LilyPond's ideal, and leaving it out made every spring here
            // ~0.104 ss narrow for a black head.
            spring = ApplyLeftHeadWidth(spring, One(prevItem), One(nextItem));
            spring = AdjustSpringForGraceNotes(
                spring, GraceNotesOf(nextItem), graceParams: null, mainItem: nextItem);
            // A pair touching a mid-measure change column is priced by the change column,
            // not by duration — and NOT by merge_springs' headroom afterwards, which would
            // add 0.3 to a gap LilyPond has already fixed.
            if (ChangeColumnItemSpring(spacingItems, i, spring.IdealDistance) is { } changeSpring)
            {
                springs.Add(changeSpring);
                continue;
            }
            // Mirror of MeasureLayouter.CreateInterColumnSpring.
            // LILYPOND-REF: lily/spacing-spanner.cc:380-393 -> lily/spring.cc:122.
            spring = ApplyMergeSpringsHeadroom(spring);
            springs.Add(spring);
        }

        // Spring from last item to end barline. full-measure-extra-space is charged to
        // the LEADING spring above, mirroring LilyPond's attribution.
        var lastItem = spacingItems[^1];
        var lastSpring = CreateSpring(lastItem, null, lastItem.Duration,
            baseShortestDuration: baseShortestDuration);
        lastSpring = ApplyLeftHeadWidth(lastSpring, One(lastItem));

        // The bar line stands in for the right-hand stem, so LilyPond runs
        // stem_dir_correction on THIS spring too. CreateSpring's own
        // CalculateStemCorrection sees no RIGHT item here and contributes nothing,
        // so without this the two spring systems disagreed on every stemmed measure:
        // the timing-column system (MeasureLayouter.CreateLastToBarlineSpring) has
        // carried the correction since the bar-line spring was ported, this one never
        // did. A measure has one voice, so this is the single-wish case of
        // MergeVoiceStemWishesToBarline — merging one wish returns it unchanged.
        // LILYPOND-REF: lily/note-spacing.cc:111 + :243-264; :113 clamps at 0.0.
        lastSpring = lastSpring.WithIdealDistance(
            Math.Max(0, lastSpring.IdealDistance
                + CalculateStemCorrectionToBarline(lastItem, NoteSpacingParameters.Default)));

        // Mirror of MeasureLayouter.CreateLastToBarlineSpring: a clef change opening the
        // NEXT measure is drawn before this bar line, so it widens the MINIMUM here. The
        // duration-based ideal is already bar-line framed and stays put.
        double clefAllowance = BoundaryClefAllowance(measure.EndBarline, nextMeasure);
        if (clefAllowance > 0)
            // LILYPOND-REF: lily/spring.cc:143-153 set_min_distance — the minimum moves,
            // the strengths do not.
            lastSpring = lastSpring.WithMinDistance(lastSpring.MinDistance + clefAllowance);

        // ...and merge_springs' headroom then lifts the ideal off that minimum, which is
        // what places the bar line when a clef precedes it. Mirror of
        // MeasureLayouter.CreateLastToBarlineSpring — the two systems must agree
        // (SpacingInvariantTests.BothSpringSystems_AgreeOnEveryMusicalSpring).
        // LILYPOND-REF: lily/spacing-spanner.cc:380-393 -> lily/spring.cc:122;
        //   lily/note-spacing.cc:78-83 for the padding-free minimum it floors from.
        lastSpring = ApplyMergeSpringsHeadroom(lastSpring);

        springs.Add(lastSpring);

        return springs.ToImmutableArray();
    }

    /// <summary>
    /// Reserves chord symbols' real text widths on the timing columns, the
    /// way LilyPond's ChordName item joins its paper column's horizontal
    /// extent expanded by (-0.5 . 0.5) — so neighbouring symbols keep ≥1.0
    /// space and a chords-only grid gets real bar widths (sixteen R1-thin
    /// bars otherwise "fit" one line and the symbols overprint). Widths use
    /// the sans face the symbols render in.
    /// LILYPOND-REF: scm/define-grobs.scm ChordName extra-spacing-width.
    /// </summary>
    /// <remarks>
    /// The reservation is ASYMMETRIC because the symbol is: ChordName has no X-offset and no
    /// self-alignment-interface (scm/define-grobs.scm:837-855), so its ink runs <c>(0 . w)</c>
    /// from its column and the spacing extent runs <c>(-0.5 . w + 0.5)</c>. A column therefore
    /// owes 0.5 to its LEFT neighbour and <c>w + 0.5</c> to its right one, whichever side the
    /// neighbour is — not <c>w/2 + 0.5</c> to each, which is what a centred symbol would owe.
    /// </remarks>
    public static ImmutableArray<Spring> ApplyChordRowSpacing(
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> timings,
        int measureIndex,
        ImmutableArray<ChordNameItem> chordNames,
        bool includeAttached = false)
    {
        if (chordNames.IsDefaultOrEmpty || springs.Length != timings.Count + 1)
            return springs;

        var width = new double[timings.Count];
        bool any = false;
        foreach (var cn in chordNames)
        {
            if (cn.MeasureIndex != measureIndex)
                continue;
            // Row symbols always price; STAFF-ATTACHED symbols only when the
            // caller opts in (an all-rest measure has no other width source).
            if (!cn.IsChordRow && (!includeAttached || !cn.UseTiming))
                continue;
            for (int t = 0; t < timings.Count; t++)
            {
                if (timings[t] == cn.Timing)
                {
                    width[t] = Math.Max(width[t],
                        ChordNameEngraver.SymbolInkWidth(cn.ChordText));
                    any = true;
                    break;
                }
            }
        }
        if (!any)
            return springs;

        // LILYPOND-REF: scm/define-grobs.scm ChordName extra-spacing-width
        // (-0.5 . 0.5): the symbol's spacing extent is its ink (0 . w) grown by
        // 0.5 on each side, so it clears a bar line on its left by 0.5, reaches
        // w + 0.5 to its right, and two adjacent symbols keep 1.0 between them.
        const double edgeGap = 0.5;
        // LILYPOND-REF: lily/spacing-spanner.cc:315-316 generate_springs — the rod between
        // two columns is the Separation_item distance PLUS the column's `padding`, default
        // 0.1 (`set_column_rods (cols, padding)`), the same 0.1 the note-to-note rods carry.
        // MEASURED (audit/lp-geometry/probes/chord-symbol-width.ly, score CWA): two adjacent
        // "Am" quarters sit at w + 0.5 + 0.5 + 0.1 to six digits; before this term the
        // chord.symbol-width.minor-pair-gap point read exactly 0.100000 of its residual here.
        const double rodPadding = 0.1;
        var result = springs.ToBuilder();
        void Widen(int springIndex, double needed)
        {
            var s = result[springIndex];
            if (needed > s.MinDistance)
                result[springIndex] = new Spring(
                    Math.Max(s.IdealDistance, needed), needed, s.InverseStretchStrength);
        }
        // How far a column's symbol reaches on each side, extra-spacing-width included.
        // A column with no symbol reaches nowhere: LilyPond has no grob there to grow.
        double LeftReach(int t) => width[t] > 0 ? edgeGap : 0;
        double RightReach(int t) => width[t] > 0 ? width[t] + edgeGap : 0;
        // A rod exists only where the symbol contributed a box; a zero reach means no box,
        // so no padding either (the other content's rods are made elsewhere).
        double Rod(double reach) => reach > 0 ? reach + rodPadding : 0;

        // Left edge: only the -0.5 of the extent stands left of the column, never a
        // half width — the ink itself starts ON the column.
        Widen(0, Rod(LeftReach(0)));
        for (int t = 0; t < timings.Count - 1; t++)
        {
            // A STAFF-ATTACHED symbol OVERHANGS a bare-note column (LP ChordName
            // extra-spacing-width -0.5 . 0.5) rather than pushing the note right,
            // so where a symbol borders a column with no symbol, reserve nothing
            // and let the note keep its natural, even spacing. A chords ROW/grid
            // (includeAttached == false) has no notes to overhang — its symbols
            // ARE the content — so it keeps the full reservation on every cell.
            // Two adjacent symbols always price so they never overprint, and the
            // bar EDGES below price the full width so an all-rest (R1) attached
            // bar, whose only column is the rest, still clears the barlines.
            if (includeAttached && (width[t] <= 0 || width[t + 1] <= 0))
                continue;
            // The LEFT symbol's whole width lies between the two columns; the right
            // one's lies beyond them. So the gap owes (w[t] + 0.5) + 0.5, plus the
            // one rod padding — it is a per-rod term, not a per-box one.
            Widen(t + 1, Rod(RightReach(t) + LeftReach(t + 1)));
        }
        Widen(timings.Count, Rod(RightReach(timings.Count - 1)));
        return result.ToImmutable();
    }

    /// <summary>
    /// How far the chord-symbol ink on each of a measure's columns reaches RIGHT of that column
    /// — the chord side of LilyPond's keep-inside-line rod, one entry per column. Mirrors
    /// <see cref="ApplyChordRowSpacing"/>'s own <c>width</c> array exactly (same filter, same
    /// metric), so the quantity rodded is the one that method reserves.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:837-855 — ChordName declares no <c>X-offset</c> and
    /// no <c>self-alignment-interface</c> at all, so its reference point IS its ink left and
    /// the symbol stands ON its column: its extent is <c>(0 . w)</c>. There is therefore NO
    /// left reach to rod, and the right reach is the symbol's whole width.
    /// MEASURED (audit/lp-geometry/probes/staffless-system.ly): the ChordName anchor equals
    /// its column's X to 6 digits in every score of that probe (CO, COW, CL, CLW, CS), and
    /// widening the name by 13.5 ss does not move the first column by a thousandth.
    /// No padding is added — LilyPond's rod carries none (lily/simple-spacer.cc:559) — unlike
    /// <see cref="ApplyChordRowSpacing"/>'s neighbour gaps.
    /// </remarks>
    internal static double[] ChordInkRightReachPerColumn(
        IReadOnlyList<Fraction> timings,
        int measureIndex,
        ImmutableArray<ChordNameItem> chordNames,
        bool includeAttached)
    {
        var width = new double[timings.Count];
        if (chordNames.IsDefaultOrEmpty || timings.Count == 0)
            return width;

        foreach (var cn in chordNames)
        {
            if (cn.MeasureIndex != measureIndex)
                continue;
            if (!cn.IsChordRow && (!includeAttached || !cn.UseTiming))
                continue;
            for (int t = 0; t < timings.Count; t++)
                if (timings[t] == cn.Timing)
                {
                    width[t] = Math.Max(width[t],
                        ChordNameEngraver.SymbolInkWidth(cn.ChordText));
                    break;
                }
        }
        return width;
    }

    /// <summary>
    /// How far the MUSICAL ink on each timing column reaches past that column, on each side —
    /// the note half of LilyPond's <c>keep_inside_line_</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:431-432 — <c>keep_inside_line_ =
    /// col-&gt;extent (col, X_AXIS)</c>, the column's own INK. Not the spacing box: an
    /// <c>extra-spacing-width</c> is read by <c>Separation_item</c>, it is not part of a
    /// grob's X-extent, so <see cref="CalculateLeftExtent"/> and
    /// <see cref="CalculateNoteheadRightExtent"/> are used bare here where
    /// <see cref="MusicalColumnLeftReach"/> (which serves <c>Paper_column::minimum_distance</c>)
    /// adds it.
    /// <para>
    /// The column reference point coincides with a note head's LEFT edge, so a plain head
    /// reaches its full width RIGHT and nothing left; what reaches LEFT is an accidental
    /// (probe TKT read a note carrying one at 1.234272 against a plain note's 0.100000, both
    /// including the 0.1 / 0.2 <c>extra-spacing-width</c> that this function excludes).
    /// </para>
    /// <para>
    /// Every measure at the index is walked — a paper column is shared by all staves and
    /// voices — and items are matched to columns by ONSET, the same walk
    /// <see cref="ApplyTabChordSpacing"/> makes.
    /// </para>
    /// </remarks>
    internal static (double[] Left, double[] Right) MusicalInkOverhangsPerColumn(
        IReadOnlyList<Model.Measure> measures, IReadOnlyList<Fraction> timings)
    {
        var left = new double[timings.Count];
        var right = new double[timings.Count];
        foreach (var measure in measures)
        {
            var onset = Fraction.Zero;
            foreach (var item in measure.Items)
            {
                if (IsMusicalColumn(item))
                    for (int t = 0; t < timings.Count; t++)
                        if (timings[t] == onset)
                        {
                            left[t] = Math.Max(left[t], CalculateLeftExtent(item));
                            right[t] = Math.Max(right[t], CalculateNoteheadRightExtent(item));
                            break;
                        }
                onset += item.Duration;
            }
        }
        return (left, right);
    }

    /// <summary>
    /// The point a grob whose <c>parent-alignment-X</c> is CENTER aligns to on each timing
    /// column — LilyPond's <c>he.linear_combination (CENTER)</c>, i.e. the centre of the
    /// column's note-column extent, or of the placeholder when the column holds no rhythmic
    /// grob at all. One entry per column, measured from the column's own reference point.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/self-alignment-interface.cc:117-141 <c>aligned_on_parent</c> —
    /// <c>he = Paper_column::get_interface_extent (him, note-column-interface, a)</c>, and when
    /// that is empty on X it falls back to the column's <c>X-alignment-extent</c>
    /// (<see cref="EngravingDefaults.PaperColumnXAlignmentExtent"/>). The extent is unioned over
    /// EVERY note column on the paper column — a paper column is shared by all staves and
    /// voices — which is the same walk <see cref="MusicalInkOverhangsPerColumn"/> makes.
    /// <para>
    /// ⚠️ WHAT IS IN THAT EXTENT WAS MEASURED, NOT ASSUMED (audit/lp-geometry/probes/
    /// staffless-system.ly, scores LSH / LSA / LSD / LSR). A NoteColumn's X-extent is its whole
    /// axis group (define-grobs.scm NoteColumn <c>X-extent = ly:axis-group-interface::width</c>),
    /// so the question is which grobs are IN the group, and the answer is not the one a reading
    /// of "the column's ink" would give: note heads are (LSH, 0.688700 = half of a 1.377400
    /// head) and rests are (LSR, 0.750000 = half a half-rest), but an ACCIDENTAL is not (LSA,
    /// unchanged at 0.688700) and neither is a DOT (LSD, unchanged). Both predictions to the
    /// contrary were written down first and both were wrong. That is consistent with LilyPond's
    /// structure — a Dots grob hangs off its note head and the accidentals off an
    /// Accidental_placement, so neither is among the note column's <c>elements</c> — and it is
    /// why this does NOT reuse <see cref="MusicalInkOverhangsPerColumn"/>, which deliberately
    /// includes an accidental's leftward reach because the keep-inside-line rod does take it.
    /// </para>
    /// <para>
    /// A stem is in the group but never widens it: it stands at a head's own edge.
    /// </para>
    /// </remarks>
    internal static double[] ParentAlignmentCentresPerColumn(
        IReadOnlyList<Model.Measure> measures, IReadOnlyList<Fraction> timings)
    {
        var left = new double[timings.Count];
        var right = new double[timings.Count];
        var seen = new bool[timings.Count];

        foreach (var measure in measures)
        {
            var onset = Fraction.Zero;
            foreach (var item in measure.Items)
            {
                if (RhythmicHeadExtent(item) is { } ext)
                    for (int t = 0; t < timings.Count; t++)
                        if (timings[t] == onset)
                        {
                            left[t] = seen[t] ? Math.Min(left[t], ext.Left) : ext.Left;
                            right[t] = seen[t] ? Math.Max(right[t], ext.Right) : ext.Right;
                            seen[t] = true;
                            break;
                        }
                onset += item.Duration;
            }
        }

        var centres = new double[timings.Count];
        for (int t = 0; t < timings.Count; t++)
            centres[t] = seen[t]
                // The placeholder extent is (0 . 1.35), so its CENTER is half the width.
                ? (left[t] + right[t]) / 2
                : EngravingDefaults.PaperColumnXAlignmentExtentWidth / 2;
        return centres;
    }

    /// <summary>
    /// One item's contribution to the note-column extent above: the note heads it draws, or
    /// the rest, measured from the column's reference point. Null for anything that is not a
    /// rhythmic grob (a note column holds no clef or bar line).
    /// </summary>
    private static (double Left, double Right)? RhythmicHeadExtent(MusicItem? item)
    {
        if (!IsMusicalColumn(item) || item is null)
            return null;

        int noteValue = GetNoteValue(item);

        // A rest is drawn glyph-left-aligned at its column, so its own box IS its extent.
        if (item is RestItem)
        {
            var restBox = GlyphMetrics.GetRestBBox(noteValue);
            return (restBox.Left, restBox.Right);
        }

        var head = GlyphMetrics.GetNoteheadBBox(noteValue);
        if (item is ChordItem chord)
        {
            // Seconds reverse a head to the other side of the stem, so the group is wider
            // than one head on whichever side the reversal happened.
            // LILYPOND-REF: lily/stem.cc:606-760 — the same offsets CalculateLeftExtent reads.
            double[] offsets = ChordHeadPositioning.CalculateOffsets(
                chord.Notes, chord.StemUp, noteValue);
            return (offsets.Min() + head.Left, offsets.Max() + head.Right);
        }
        return (head.Left, head.Right);
    }

    /// <summary>
    /// The surviving empty COMMAND columns of a staff-less row: between two of a lead
    /// sheet's timing columns LilyPond has TWO springs, not one — musical column →
    /// (empty) command column at the next beat, then command column → that beat's
    /// musical column — and the second is the breakable dt==0 spring, a flat 0.5.
    /// This composes that pair into each inter-column spring.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:71-77 standard_breakable_column_spacing —
    /// <c>ideal = min_dist + 0.5</c> for a dt == 0 pair, and <c>min_dist</c> is 0 here
    /// because an empty command column has no box. The command columns SURVIVE only on a
    /// staff-less row: lily/spacing-determine-loose-columns.cc:82-90
    /// <c>is_loose_column</c> wants a <c>left-neighbor</c>/<c>right-neighbor</c> to
    /// attach a loose column to, those are set off NOTE columns, and a
    /// ChordNames/Lyrics-only score has none — so the empty columns are never pruned and
    /// every beat costs its duration space PLUS this 0.5. On a staff-backed score they
    /// are pruned and no such term exists, which is why this is applied on the lead-sheet
    /// path only.
    /// <para>
    /// MEASURED (audit/lp-geometry/probes/chord-symbol-width.ly, CAL2 ALLCOL dump): the
    /// system of a chords-only score holds a starter-less column 0.5 left of EVERY
    /// musical column, and each measured gap decomposes to six digits as
    /// duration-space + 0.500000 across four regimes (quarters 2.898045 + 0.5, halves
    /// 4.098045 + 0.5, eighths 2.4 + 0.5, and the mixed book's quarter 3.6 + 0.5).
    /// The last musical column's spring runs to the bar line's own command column, so
    /// the closing spring carries NO extra term (whole → bar measured 5.298045, the bare
    /// duration space).
    /// </para>
    /// <para>
    /// Composing the pair into one spring is exact, not an approximation: springs in
    /// series add their ideals, their minima and their inverse strengths, and the dt==0
    /// spring is Spring(0.5, 0) with the default strength — its inverse stretch is its
    /// own ideal (lily/spring.cc set_default_strength), 0.5.
    /// </para>
    /// </remarks>
    public static ImmutableArray<Spring> ApplyRowCommandColumnSprings(
        ImmutableArray<Spring> springs)
    {
        // The dt == 0 breakable spring of one surviving empty command column.
        const double commandIdeal = 0.5;
        if (springs.Length <= 2)
            return springs;
        var result = springs.ToBuilder();
        // Inter-column springs only: spring 0 (bar line → first column) is already the
        // breakable pair, and the last spring runs INTO a command column (the bar
        // line's), so LilyPond adds nothing there.
        for (int i = 1; i < result.Count - 1; i++)
        {
            var s = result[i];
            result[i] = new Spring(
                s.IdealDistance + commandIdeal, s.MinDistance,
                s.InverseStretchStrength + commandIdeal);
        }
        return result.ToImmutable();
    }

    /// <summary>
    /// Floors a LEAD-SHEET bar at a readable grid-cell width. Row bars carry
    /// no notation ink, so without a floor a long chart packs every bar onto
    /// one line; with it the chart wraps like a song-book grid.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: LilyPond has no such floor — a chords-only chart's bar width is
    /// whatever its duration springs add up to. Both the 10.0 and the distribution are
    /// Lily#'s.
    /// <para>
    /// ⚠️ The whole deficit goes into the LAST spring — the trailing room after the bar's
    /// final chord — and nowhere else. It used to be shared equally across every spring,
    /// and in a bar with one chord (a whole-note cell: two springs) that put half the
    /// artificial width IN FRONT of beat 1 — the symbol and its syllable sat ~3.5 ss deep
    /// into the bar while every multi-chord bar opened at ~0.6 (reported by the user on
    /// test/lead-sheet, 2026-07-29: a beat-1 note belongs by its bar line). Inner springs
    /// must not take it either: they are the bar's DURATION springs, the quantity the
    /// <c>chord.symbol-width.*spring-control</c> ledger points measure against LilyPond,
    /// and a floor share folded into them is invisible fitting. Trailing room is also
    /// where LilyPond's own duration springs put a whole note's width.
    /// </para>
    /// </remarks>
    public static ImmutableArray<Spring> EnsureLeadSheetBarWidth(ImmutableArray<Spring> springs)
    {
        const double gridBarMinWidth = 10.0;
        if (springs.Length == 0)
            return springs;
        double minSum = 0;
        foreach (var s in springs)
            minSum += s.MinDistance;
        if (minSum >= gridBarMinWidth)
            return springs;
        double extra = gridBarMinWidth - minSum;
        var result = springs.ToBuilder();
        var last = result[^1];
        result[^1] = new Spring(
            Math.Max(last.IdealDistance, last.MinDistance + extra),
            last.MinDistance + extra, last.InverseStretchStrength);
        return result.ToImmutable();
    }

    /// <summary>
    /// Reserves the horizontal room a TAB staff's fret digits need in the SHARED
    /// note columns, so adjacent digits (or a chord's zigzagged columns) do not
    /// overprint. Tab fret numbers are a Lily# enlargement of LilyPond's tiny,
    /// unspaced digits, so their width has no LilyPond analogue and is priced in
    /// here on the "digits must not overlap" principle — the same one that drives
    /// the chord zigzag. Widens each inter-column spring to hold the right extent
    /// of the left column plus the left extent of the right column.
    /// </summary>
    public static ImmutableArray<Spring> ApplyTabChordSpacing(
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> timings,
        Model.Measure tabMeasure,
        int[] tuning,
        int octaveShift)
    {
        if (springs.Length != timings.Count + 1)
            return springs;

        var left = new double[timings.Count];
        var right = new double[timings.Count];
        bool any = false;
        Fraction onset = Fraction.Zero;
        foreach (var item in tabMeasure.Items)
        {
            if (item is Model.NoteItem or Model.ChordItem)
                for (int t = 0; t < timings.Count; t++)
                    if (timings[t] == onset)
                    {
                        var (l, r) = LilySharp.Core.Rendering.SharedRenderer.TabItemHalfExtent(
                            item, tuning, octaveShift);
                        left[t] = Math.Max(left[t], l);
                        right[t] = Math.Max(right[t], r);
                        any = true;
                        break;
                    }
            onset += item.Duration;
        }
        if (!any)
            return springs;

        double tabGap = TabConstants.FretColumnGap; // clearance between adjacent digit columns
        var result = springs.ToBuilder();
        void Widen(int idx, double needed)
        {
            var s = result[idx];
            if (needed > s.MinDistance)
                result[idx] = new Spring(
                    Math.Max(s.IdealDistance, needed), needed, s.InverseStretchStrength);
        }
        Widen(0, left[0]);
        for (int t = 0; t < timings.Count - 1; t++)
            Widen(t + 1, right[t] + left[t + 1] + tabGap);
        Widen(timings.Count, right[^1]);
        return result.ToImmutable();
    }

    /// <summary>
    /// Prices the CROSS-VOICE column pairs of one staff — the pairs the per-voice rod
    /// loop in <see cref="MeasureLayouter"/> cannot see — with each item's ink taken at
    /// the X the renderer will draw it, its note-collision voice shift included. The
    /// case that found it: a dotted half in voice three (shifted right past the
    /// down-voice head) reaches its DOT into the next eighth column, which belongs to
    /// voice two alone; no single voice occupies both columns, so no floor was raised
    /// and the dot printed straight through the next head
    /// (scratch/ベースタブLy/Untitled-4.lys, 起票 2026-08-05).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:120-190 Separation_item::boxes — a staff's
    ///   separation item boxes every grob of its paper column across ALL the staff's
    ///   voices, as extents in the COLUMN's frame, i.e. with calc_positioning_done's
    ///   shifts already applied. LilyPond resolves collisions BEFORE spacing reads the
    ///   boxes; Lily# applies shifts at render time, so this pass asks the same
    ///   computation the renderer's offsets come from
    ///   (<see cref="ElementCoordinator.ComputeVoiceOffsets"/>).
    /// LILYPOND-REF: lily/note-spacing.cc:78-83 — the spring minimum is the skyline
    ///   distance over those boxes; lily/spring.cc:122 merge_springs then floors the
    ///   ideal at min + 0.3, which is why the headroom is re-applied after the raise.
    /// <para>
    /// MEASURED (2.26.0, the book above): with the dot, LilyPond's first eighth gap is
    /// 3.33 against the measure's plain 2.50; remove the dot (<c>cis2</c> for
    /// <c>cis2.</c>) and it collapses to 2.51 even though the shifted head stays. So the
    /// push is the DOT's skyline, not the head's, and a dot-blind gate cannot price it.
    /// (The absolute gap will differ from LilyPond's while the voice-three cascade shift
    /// differs — Lily# draws this cis at +1.30, LilyPond at +0.65 — a separate,
    /// pre-existing note-collision question; this pass prices the geometry Lily#
    /// actually draws.)
    /// </para>
    /// <para>
    /// ⚠️ Same-voice pairs with no shift are SKIPPED: the per-voice loop has already
    /// priced them through these same two functions, and a same-voice pair is
    /// shift-invariant anyway (both ends carry the voice's own dx, which cancels in the
    /// distance). Cross-STAFF pairs are never formed — that is the false collision the
    /// per-voice loop was narrowed to avoid (a triplet in one staff clashing with
    /// straight eighths in another, see its comment); the SAME-staff frame is LilyPond's
    /// own, one separation item per staff.
    /// </para>
    /// </remarks>
    /// <summary>
    /// One collision-offsets computation per STAFF object, not per measure:
    /// <see cref="ElementCoordinator.ComputeVoiceOffsets"/> walks every measure of
    /// every voice, and this decoration runs once per measure in BOTH the break gate
    /// and the system layout — calling it inline made the pass O(measures²) per staff
    /// (MEASURED: +26% end-to-end on a 120-bar two-voice book, 1991→2514 ms). The
    /// offsets derive purely from the staff's immutable Voices, so one computation per
    /// Staff instance is exact; a model rebuild makes new Staff objects and refills.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Model.Staff, ImmutableDictionary<VoiceItemKey, double>> s_staffVoiceOffsets = new();

    internal static ImmutableArray<Spring> ApplyCrossVoiceColumnSpacing(
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> timings,
        Model.Staff staff,
        int measureIndex)
    {
        var staffVoices = staff.Voices;
        if (staffVoices.Length < 2 || springs.Length != timings.Count + 1)
            return springs;

        var offsets = s_staffVoiceOffsets.GetValue(staff,
            s => ElementCoordinator.ComputeVoiceOffsets(s.Voices).VoiceOffsets);

        // The items STARTING at each timing column, each with the shift it is drawn at.
        // VoiceId is 1-based, matching VoiceCollector / the renderer's VoiceItemKey.
        var columns = new List<(MusicItem Item, double Shift, int Voice)>?[timings.Count];
        for (int v = 0; v < staffVoices.Length; v++)
        {
            if (measureIndex >= staffVoices[v].Measures.Length)
                continue;
            var items = staffVoices[v].Measures[measureIndex].Items;
            var onset = Fraction.Zero;
            for (int oi = 0; oi < items.Length; oi++)
            {
                var item = items[oi];
                // Only what LilyPond boxes: a note, a chord, a DRAWN rest. A spacer
                // (`s`) engraves no grob at all, so it has no separation box — pairing
                // it here floored a real note against a phantom head at the middle
                // line. Same gate as IsMusicalColumn.
                if (IsMusicalColumn(item))
                    for (int t = 0; t < timings.Count; t++)
                        if (timings[t] == onset)
                        {
                            (columns[t] ??= new()).Add((item,
                                offsets.GetValueOrDefault(
                                    new VoiceItemKey(measureIndex, v + 1, oi)),
                                v));
                            break;
                        }
                onset += item.Duration;
            }
        }

        var result = springs.ToBuilder();
        bool widened = false;
        // Each entry's facing skyline is built ONCE per pair side it joins — an entry
        // faces at most one pair as the LEFT item and one as the RIGHT, and skyline
        // construction is the expensive part of a pair (MEASURED: building them
        // per-pair was most of this pass's ~100 ms on a 120-bar two-voice book).
        // SkylineFloorPair keeps both clamps in the one home the item-pair helpers use.
        var rightSkyOf = new Dictionary<(int Col, int Entry), HorizontalSkyline>();
        var leftSkyOf = new Dictionary<(int Col, int Entry), HorizontalSkyline>();
        for (int t = 1; t < timings.Count; t++)
        {
            if (columns[t - 1] is not { } left || columns[t] is not { } right)
                continue;
            double maxSky = 0, maxRod = 0;
            for (int li = 0; li < left.Count; li++)
                for (int ri = 0; ri < right.Count; ri++)
                {
                    var l = left[li];
                    var r = right[ri];
                    if (l.Voice == r.Voice && l.Shift == 0 && r.Shift == 0)
                        continue;
                    if (!rightSkyOf.TryGetValue((t - 1, li), out var rs))
                        rightSkyOf[(t - 1, li)] = rs =
                            ItemSkylineFactory.CreateRightSkyline(l.Item, l.Shift, 0);
                    if (!leftSkyOf.TryGetValue((t, ri), out var ls))
                        leftSkyOf[(t, ri)] = ls =
                            ItemSkylineFactory.CreateLeftSkyline(r.Item, r.Shift, 0);
                    var (sky, rod) = SkylineFloorPair(rs, ls);
                    maxSky = Math.Max(maxSky, sky);
                    maxRod = Math.Max(maxRod, rod);
                }
            if (maxSky <= result[t].MinDistance && maxRod <= result[t].MinDistance)
                continue;
            // The same three steps the per-voice loop takes, in the same order: raise
            // the spring minimum, re-floor the ideal at min + 0.3 (merge_springs'
            // headroom), then the rod, which binds only under compression.
            var s = result[t].EnsureMinDistance(maxSky);
            s = ApplyMergeSpringsHeadroom(s);
            result[t] = s.EnsureMinDistance(maxRod);
            widened = true;
        }
        return widened ? result.ToImmutable() : springs;
    }

    /// <summary>
    /// Reserves the sideways reach of a wide, always-outside script (a fermata or
    /// ornament) in the shared note columns, so a fermata over one note does not
    /// crowd the next note's accidental or head. The reservation is a SKYLINE
    /// distance, so it only widens where the script's glyph and the neighbour's
    /// ink overlap VERTICALLY — a fermata high above the staff leaves a low
    /// following note's spacing untouched, exactly as LilyPond's Script grob
    /// joins the note column's horizontal skyline only at its own Y band. Scripts
    /// live in a separate collection keyed by (staff, measure, item); this aligns
    /// them to columns by onset, like <see cref="ApplyTabChordSpacing"/>. Narrow
    /// scripts contribute no box (see <see cref="ArticulationEngraver.SpacingInkBox"/>),
    /// so most articulation fixtures are left exactly as before.
    /// LILYPOND-REF: lily/separation-item.cc set_distance() — every grob in the
    ///   note column (Script included) feeds the column's horizontal skyline.
    /// </summary>
    public static ImmutableArray<Spring> ApplyArticulationSpacing(
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> timings,
        Model.Measure measure,
        ImmutableArray<ArticulationItem> articulations,
        int measureIndex,
        int staffIndex)
    {
        if (articulations.IsDefaultOrEmpty || springs.Length != timings.Count + 1)
            return springs;

        // Per column: the note/chord starting at that onset, and any wide-script
        // ink boxes it carries (skyline frame: column at X=0, middle line Y=0).
        var colItem = new MusicItem?[timings.Count];
        var colBoxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>?[timings.Count];
        bool any = false;
        Fraction onset = Fraction.Zero;
        for (int oi = 0; oi < measure.Items.Length; oi++)
        {
            var item = measure.Items[oi];
            if (item is Model.NoteItem or Model.ChordItem)
                for (int t = 0; t < timings.Count; t++)
                {
                    if (timings[t] != onset)
                        continue;
                    colItem[t] ??= item;
                    foreach (var art in articulations)
                    {
                        if (art.StaffIndex != staffIndex || art.MeasureIndex != measureIndex
                            || art.ItemIndex != oi)
                            continue;
                        if (ArticulationEngraver.SpacingInkBox(art, item, staffY: 0) is { } box)
                        {
                            (colBoxes[t] ??= new()).Add(box);
                            any = true;
                        }
                    }
                    break;
                }
            onset += item.Duration;
        }
        if (!any)
            return springs;

        // Clear the script from the neighbouring column by LilyPond's script-to-grob
        // gap (each side's extra-spacing-width), not the wider generic item gap — so a
        // fermata sits the LP distance from the next note's accidental, not further.
        double gap = ArticulationSpacing.ScriptToNeighbourGap;
        var result = springs.ToBuilder();
        void Widen(int idx, double needed)
        {
            var s = result[idx];
            if (needed > s.MinDistance)
                result[idx] = new Spring(
                    Math.Max(s.IdealDistance, needed), needed, s.InverseStretchStrength);
        }

        // The between-column spring t+1 spans colItem[t] → colItem[t+1]. A script
        // on the LEFT column reaches RIGHT into the right column's left ink; a
        // script on the RIGHT column reaches LEFT over the left column's right ink.
        for (int t = 0; t + 1 < timings.Count; t++)
        {
            var left = colItem[t];
            var right = colItem[t + 1];
            if (left is null || right is null)
                continue;
            double needed = 0;
            if (colBoxes[t] is { } lb)
            {
                double d = HorizontalSkyline.FromBoxes(lb, HorizontalDirection.Right)
                    .Distance(ItemSkylineFactory.CreateLeftSkyline(right, 0, 0));
                if (!double.IsNegativeInfinity(d))
                    needed = Math.Max(needed, d + gap);
            }
            if (colBoxes[t + 1] is { } rb)
            {
                double d = ItemSkylineFactory.CreateRightSkyline(left, 0, 0)
                    .Distance(HorizontalSkyline.FromBoxes(rb, HorizontalDirection.Left));
                if (!double.IsNegativeInfinity(d))
                    needed = Math.Max(needed, d + gap);
            }
            if (needed > 0)
                Widen(t + 1, needed);
        }
        return result.ToImmutable();
    }


    // ========================================
    // Skyline Generation
    // ========================================

    /// <summary>
    /// Calculates the minimum distance between two items using skyline collision detection.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:44-86
    /// Uses skylines to find the actual minimum distance where items don't overlap,
    /// considering the shape of noteheads and accidentals at each Y coordinate.
    /// </remarks>
    /// <summary>
    /// Gets the space from a barline to the next item, based on item type.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarLine.space-alist
    /// LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
    ///
    /// Different item types get different amounts of space after a barline:
    ///   next-note:      semi-fixed-space  0.9 (mostly fixed)
    ///   clef:           extra-space       1.0
    ///   key-signature:  extra-space       1.0
    ///   time-signature: extra-space       0.75
    /// `first-note` (semi-shrink-space 1.3) is deliberately absent: LilyPond reads it
    /// only at a system start, which is not this path — see the note on the note arm.
    ///
    /// These are IDEALS, never minimums — see <see cref="GetBarlineToItemMinimum"/> for the
    /// minimum, which used to be taken from here. The `next-note` arm reaches the spring
    /// through EngravingDefaults.BarLineToNextNoteSpace
    /// (<see cref="BarlineToFirstColumnSpring"/>); the clef / key-signature /
    /// time-signature arms belong to break-align spacing, because
    /// Staff_spacing::get_spacing consults only `first-note` and `next-note`
    /// (lily/staff-spacing.cc:147-153) and never keys on the right column's content. Those
    /// arms therefore have no production caller of their own yet — folding them into
    /// BreakAlignSpacing is part of the §3.I role-overlap cleanup in COORDINATE_AUDIT.md.
    /// </remarks>
    public static double GetBarlineToItemSpace(MusicItem? nextItem)
    {
        // LILYPOND-REF: scm/define-grobs.scm BarLine space-alist
        return nextItem switch
        {
            ClefChangeItem => 1.0,             // (clef . (extra-space . 1.0))
            KeySignatureChangeItem => 1.0,     // (key-signature . (extra-space . 1.0))
            TimeSignatureChangeItem => 0.75,   // (time-signature . (extra-space . 0.75))
            // (next-note . (semi-fixed-space . 0.9)). NOT first-note: LilyPond picks
            // `first-note` only when the bar line's break_status_dir differs from
            // CENTER, i.e. at the START OF A SYSTEM — never at an ordinary mid-line
            // bar line, which every measure start inside a system is. Measured on
            // LilyPond 2.24.4: overriding BarLine's `first-note` from 0.0 to 5.0 does
            // not move a single grob in `c'1 c'1`, because that entry is never read
            // there. The system-start case is handled separately, and correctly, by
            // BreakAlignSpacing.FirstNoteSpring (prefix -> first note).
            // LILYPOND-REF: lily/staff-spacing.cc:147-153.
            _ => 0.9
        };
    }

    /// <summary>
    /// Gets the MINIMUM distance from a bar line to the next item — the mirror of
    /// <see cref="GetItemToBarlineSpace"/>, and NOT <see cref="GetBarlineToItemSpace"/>.
    /// </summary>
    /// <remarks>
    /// LilyPond's bar line → column minimum is <c>Paper_column::minimum_distance</c>, a
    /// PURE skyline distance: the bar line reaches its ink right edge + extra-spacing-width
    /// 0.1, the next column's leftmost grob reaches its ink left edge - 0.1, so the gap
    /// beyond the item's own ink is exactly 0.1 + 0.1. No space-alist value enters.
    /// <para>
    /// LilySharp used to feed <see cref="GetBarlineToItemSpace"/>'s 0.9 in here. That is the
    /// `next-note` semi-fixed-space entry, i.e. the IDEAL, and using it as the minimum made
    /// this spring rigid at its ideal and 0.7 ss over-constrained — which in turn is why
    /// merge_springs' headroom could not be applied to it (it would have floored the ideal
    /// at 0.9 + 0.3 and fattened every measure start by 0.3). With the minimum corrected the
    /// headroom is a no-op here: 0.2 + 0.3 &lt; 0.9.
    /// </para>
    /// ⚠️ The change-item arms below kept their space-alist value because
    /// <see cref="CalculateLeftExtent"/>'s change branch used to be on the CENTRE basis. That
    /// justification is GONE — the branch now returns 0, like any other grob whose origin is
    /// its ink left edge — and these arms have not been re-derived against LilyPond since. A
    /// change item reaches them only through a path LilyPond does not have (a change sharing
    /// the LAST timing of a measure, so Lily# measures it toward the closing bar line); no
    /// fixture exercises it. Recorded in the roadmap rather than guessed at.
    /// LILYPOND-REF: lily/staff-spacing.cc:210 <c>Paper_column::minimum_distance</c>;
    ///   lily/separation-item.cc:166-167 default extra-spacing-width
    ///   <c>Interval (-0.1, 0.1)</c>; lily/note-spacing.cc:78-83 sets the spring minimum to
    ///   the padding-free skyline distance.
    /// </remarks>
    public static double GetBarlineToItemMinimum(MusicItem? nextItem)
    {
        return nextItem switch
        {
            ClefChangeItem => 1.0,
            KeySignatureChangeItem => 1.0,
            TimeSignatureChangeItem => 0.75,
            // Bar line's own extra-spacing-width (the default 0.1) plus the LEFTmost grob's.
            // That grob is an accidental whenever the column carries one, and an accidental
            // declares 0.2 rather than the default — see AccidentalExtraSpacingWidthLeft.
            // A head reversed left of the stem is still an ordinary NoteHead and keeps 0.1.
            _ => DefaultExtraSpacingWidth
                 + (HasAccidental(nextItem)
                        ? AccidentalExtraSpacingWidthLeft
                        : DefaultExtraSpacingWidth)
        };
    }

    /// <summary>
    /// Gets the space from the last item in a measure to the barline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:49-70
    /// The distance from item to barline uses BarlinePadding for normal notes,
    /// with extra space for non-musical items.
    /// </remarks>
    public static double GetItemToBarlineSpace(MusicItem? prevItem)
    {
        return prevItem switch
        {
            // ⚠️ These kept their own constant because CalculateNoteheadRightExtent's change
            // branch returned width/2 — the CENTRE basis. It now returns the glyph's full
            // width, so that justification no longer holds and these three have not been
            // re-derived. See the matching note on GetBarlineToItemMinimum: LilyPond has no
            // change-item-to-bar-line pair at all, and no fixture reaches this.
            ClefChangeItem => 1.0,
            KeySignatureChangeItem => 1.0,
            TimeSignatureChangeItem => 1.0,
            // LilyPond's item → boundary minimum is a pure skyline distance between the
            // two columns' boxes, with NO padding term: the left column reaches its ink
            // right edge + extra-spacing-width 0.1, the boundary column's leftmost grob
            // reaches its ink left edge - 0.1. So the gap beyond the item's own ink is
            // exactly 0.1 + 0.1. (The rod adds a further `padding` 0.1, but the rod is
            // not what binds at force >= 0 — see BoundaryClefAllowance / the merge_springs
            // headroom in ApplyMergeSpringsHeadroom.)
            // LILYPOND-REF: lily/separation-item.cc:166-167 default extra-spacing-width
            //   Interval (-0.1, 0.1); lily/note-spacing.cc:78-83 sets the spring minimum
            //   to the padding-free skyline distance.
            _ => 2 * DefaultExtraSpacingWidth
        };
    }

    /// <param name="prevShift">X the LEFT item is drawn at, relative to its column — a
    /// note-collision voice shift. LilyPond's separation boxes are extents in the PAPER
    /// COLUMN's frame, so a shifted voice's ink (and its dots) reaches further right; the
    /// default 0 keeps every existing caller on the unshifted frame.</param>
    /// <param name="nextShift">Same for the RIGHT item.</param>
    public static double CalculateSkylineDistance(MusicItem? prevItem, MusicItem? nextItem,
                                                   double staffY,
                                                   NoteSpacingParameters? noteParams = null,
                                                   double prevShift = 0, double nextShift = 0)
    {
        // LILYPOND-REF: scm/define-grobs.scm — skyline-horizontal-padding (LP default 0.1).
        // LilySharp historically used GlyphMetrics.MinItemGap (0.4) as the static
        // constant; the parameter override path lets callers tune it down for
        // tighter LP-style proportional spacing.
        double minItemGap = noteParams?.MinItemGap ?? MinItemGap;

        // For barline-to-item or item-to-barline, use LP space-alist based calculation
        // LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
        if (prevItem == null || nextItem == null)
        {
            if (prevItem == null && nextItem != null)
            {
                // Barline → item: the padding-free skyline minimum, NOT the space-alist
                // ideal. LILYPOND-REF: lily/staff-spacing.cc:210.
                double barlinePad = GetBarlineToItemMinimum(nextItem);
                double itemExtent = CalculateLeftExtent(nextItem);
                return barlinePad + itemExtent;
            }
            else if (prevItem != null && nextItem == null)
            {
                // Item → barline: use type-aware barline padding
                double itemExtent = CalculateNoteheadRightExtent(prevItem);
                double barlinePad = GetItemToBarlineSpace(prevItem);
                return itemExtent + barlinePad;
            }
            else
            {
                // Both null (shouldn't happen): return default
                return BarlinePadding * 2 + minItemGap;
            }
        }

        // LilyPond's spring minimum for a note-to-note pair, literally: the distance
        // between the two columns' skylines, taken with the RIGHT column's
        // skyline-vertical-padding, and clamped at 0. No gap is added here — the 0.2 that
        // separates two heads is already in the boxes, as each grob's extra-spacing-width
        // (ItemSkylineFactory). The rod adds a further `padding` on top and takes the
        // distance WITHOUT that vertical padding; that is SeparationRodDistance, not this.
        // LILYPOND-REF: lily/note-spacing.cc:78-83 —
        //   `Real distance = skys[LEFT].distance (skys[RIGHT], skyline-vertical-padding);
        //    Real min_dist = max (0.0, distance); base.set_min_distance (min_dist);`
        // ⚠️ There is no fall-back branch for skylines that do not overlap vertically:
        // LilyPond has none, and the one that used to be here (prevRight + nextLeft + gap)
        // could exceed the skyline answer it was standing in for. Nor is one needed —
        // a non-overlapping pair gives -infinity and max(0, -inf) is 0, which is LilyPond's
        // own answer through its own max.
        return SkylineFloorPair(
            ItemSkylineFactory.CreateRightSkyline(prevItem, prevShift, staffY),
            ItemSkylineFactory.CreateLeftSkyline(nextItem, nextShift, staffY)).SkyMin;
    }

    /// <summary>
    /// LilyPond's TWO constraints over one column pair, computed from skylines the
    /// caller already holds — the ONE home for both clamp shapes.
    /// <c>SkyMin</c> is the SPRING's minimum (distance with the right column's
    /// skyline-vertical-padding, clamped at 0); <c>Rod</c> is the column rod (the
    /// spanner's 0.1 padding plus the padding-free distance, clamped after adding).
    /// <see cref="CalculateSkylineDistance"/> and <see cref="SeparationRodDistance"/>
    /// are these same clamps asked of a bare item pair; a caller that prices MANY
    /// pairs (ApplyCrossVoiceColumnSpacing) builds each item's skylines once and asks
    /// this directly — same numbers, half the skyline construction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:78-83 (the spring minimum);
    /// LILYPOND-REF: lily/separation-item.cc:47-68 set_distance + lily/spacing-spanner.cc:315-316
    ///   (the rod: <c>dist = padding + …distance (right)</c>, clamped at 0 after).
    /// </remarks>
    internal static (double SkyMin, double Rod) SkylineFloorPair(
        HorizontalSkyline prevRight, HorizontalSkyline nextLeft)
        => (Math.Max(0.0, prevRight.Distance(nextLeft, MusicalColumnSkylineVerticalPadding)),
            Math.Max(0.0, SeparationRodPadding + prevRight.Distance(nextLeft, 0.0)));

    /// <summary>
    /// The ROD between two musical columns: the skyline minimum plus the spacing spanner's
    /// padding. This is the hard floor a compressed line cannot cross, and it is what the
    /// drawn gap saturates at.
    /// </summary>
    /// <remarks>
    /// LilyPond keeps the two apart, and so does this: <see cref="CalculateSkylineDistance"/>
    /// is the SPRING's min_distance (note-spacing.cc:78-83) and this is the rod, raised over
    /// the same pair by Spacing_spanner::set_column_rods.
    /// <para>
    /// ⚠️ The two differ in more than the padding, which is why this does not simply add 0.1
    /// to the other. LilyPond's rod takes the ONE-ARGUMENT <c>Skyline::distance</c> — no
    /// skyline-vertical-padding — and clamps AFTER adding the padding, so a pair whose bare
    /// distance is slightly negative still yields a rod. Reusing the spring's number would
    /// have inherited the 0.08 and clamped in the wrong order; it agrees on two same-pitch
    /// heads (where the boxes overlap in Y outright) and would drift elsewhere.
    /// </para>
    /// MEASURED (audit/lp-geometry/probes/compressed-note-spacing.ly): for two same-pitch
    /// quarters LilyPond's rod is 1.604200 = 0.1 + 1.504200, and every column in that dump
    /// carries exactly that. Spring::length saturates at min_distance (lily/spring.cc:236)
    /// and the rod is the floor under it, so the compressed plateau is this number.
    /// LILYPOND-REF: lily/separation-item.cc:47-68 Separation_item::set_distance —
    ///   <c>Real dist = padding + lines[LEFT][RIGHT].distance (right); … return
    ///   std::max (dist, 0.0);</c>
    /// LILYPOND-REF: lily/spacing-spanner.cc:315-316 — the padding passed to
    ///   set_column_rods is the last column's `padding`, defaulting to 0.1.
    /// </remarks>
    public static double SeparationRodDistance(MusicItem? prevItem, MusicItem? nextItem,
                                               double staffY,
                                               NoteSpacingParameters? noteParams = null,
                                               double prevShift = 0, double nextShift = 0)
    {
        // A boundary (bar line) pair is priced by its space-alist, not by a column rod —
        // that is CalculateSkylineDistance's own branch, and it has no separate rod.
        if (prevItem == null || nextItem == null)
            return CalculateSkylineDistance(prevItem, nextItem, staffY, noteParams);

        return SkylineFloorPair(
            ItemSkylineFactory.CreateRightSkyline(prevItem, prevShift, staffY),
            ItemSkylineFactory.CreateLeftSkyline(nextItem, nextShift, staffY)).Rod;
    }

    /// <summary>
    /// Calculates the item's RIGHTward ink reach from its column: the HEAD's right edge,
    /// which is what the bar-line pair is measured from.
    /// </summary>
    /// <remarks>
    /// ⚠️ STEMS AND FLAGS ARE OUT OF THIS ONE, AND THAT IS A FACT ABOUT THE BAR-LINE PAIR,
    /// NOT ABOUT THE COLUMN'S SKYLINE. This doc used to say "excluding stems and flags" with
    /// the separation-item citation below standing right under it, and the next reader (twice)
    /// took that as "LilyPond's column skyline has no stem in it". It has one — every Item in
    /// the column is a box (see <see cref="ItemSkylineFactory"/>), and a bass line in E-flat
    /// found the omission by running a flat through a stem. What LilyPond really reads for
    /// THIS quantity is the first head's own extent:
    /// LILYPOND-REF: lily/note-spacing.cc:46-70 Note_spacing::get_spacing —
    ///   <c>left_head_end = g->extent (col, X_AXIS)[RIGHT]</c> where <c>g</c> is the rest or
    ///   <c>Note_column::first_head</c>, and it is that end the ideal is measured from at :77.
    /// The reference point is the column, which coincides with the note head's LEFT edge —
    /// the same convention <see cref="CalculateLeftExtent"/> documents and LilyPond uses
    /// (dumping <c>ly:grob-relative-coordinate</c> for a PaperColumn and its NoteHead in
    /// 2.24.4 gives the same X). So a plain head reaches its FULL ink width to the right.
    /// LILYPOND-REF: lily/separation-item.cc:163-164 boxes — the spacing box is
    /// <c>il-&gt;extent (pc, X_AXIS)</c>, the grob's extent in its PAPER COLUMN's frame.
    /// LILYPOND-REF: lily/rest.cc Rest::width — the rest branch below uses the same frame.
    /// </remarks>
    internal static double CalculateNoteheadRightExtent(MusicItem item)
    {
        // Mirror of CalculateLeftExtent: the origin is the change glyph's ink left edge, so
        // its rightward reach is its full width — not half of it plus a padding.
        if (IsChangeItem(item))
            return ChangeItemColumnWidth(item);

        int noteValue = GetNoteValue(item);

        // A rest is drawn glyph-left-aligned at its column X (DrawRest: DrawGlyph at x),
        // so its right reach from the column origin is the rest glyph's right edge —
        // wide for a whole/half rest. Using the (smaller) notehead box here let a whole
        // rest's glyph collide with the following barline. LILYPOND-REF: lily/rest.cc
        // Rest::width / generic_extent_callback — the rest stencil's own X-extent feeds
        // the column skyline / separation.
        double extent;
        if (item is RestItem)
        {
            extent = GlyphMetrics.GetRestBBox(noteValue).Right;
        }
        else
        {
            var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
            // The column sits at the head's LEFT edge (see the remarks above and
            // CalculateLeftExtent, which returns 0 leftward for the same reason), so the
            // rightward reach is the head's own right edge — mirroring the rest branch.
            // Seeding this with `Width - CenterX` treated the column as if it were at the
            // head's CENTRE, which under-charged a black head by ~0.65 ss; paired with a
            // LEFT extent that had already been converted to the left-edge basis, the two
            // sides of the same box were being measured in different frames.
            extent = noteheadBBox.Right;
        }

        // Add dots if present
        int dots = GetDots(item);
        if (dots > 0)
        {
            var dotBBox = GlyphMetrics.AugmentationDot;
            double dotWidth = dotBBox.Width;
            double dotGap = EngravingDefaults.DotGap;
            extent += dotGap + dots * dotWidth + (dots - 1) * dotGap;
        }

        return extent;
    }
}
