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

    /// <summary>
    /// Line-start prefix-to-first-note spring (ideal, min) — see
    /// BreakAlignSpacing.FirstNoteSpring.
    /// </summary>
    public static (double Ideal, double Min) FirstNoteSpring(KeySignature key, bool includeTimeSignature, double clefWidth)
        // clefWidth MUST be the SAME width CalculatePrefixWidth reserves for this line start,
        // so the clef-only case cancels to max(width, 5.0) from the clef's left ink, per
        // LilyPond's minimum-fixed-space. Pass MaxClefWidth(score): a bass/alto/C clef reserves
        // more than the treble G, and threading a mismatched width would break that cancellation
        // (the clef-only first note would no longer land 5.0 out). See defect-3 in the ledger.
        => BreakAlignSpacing.FirstNoteSpring(
            KeySignatureInkWidth(key), includeTimeSignature, clefWidth);

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
    /// The widest line-start clef ink across the score's notation staves — the shared
    /// prefix column every staff's key/time signature and first note break-align past.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-align-interface.cc — the Clef break-align column spans the
    /// whole system, so in a grand staff the wider bass F clef governs the treble staff's
    /// meter position too (both signatures stay vertically aligned). Tab (its own clef),
    /// text (lyric/chord) and ossia rows carry no shared-prefix clef and are skipped. Empty
    /// selection falls back to the treble G width (the historical default).
    /// </remarks>
    public static double MaxClefWidth(MultiStaffScore score)
    {
        double max = 0.0;
        foreach (var (_, staff, _) in score.EnumerateStaves())
            if (!staff.IsTab && !staff.IsTextRow && !staff.IsOssia)
                max = Math.Max(max, GlyphMetrics.LineStartClefWidth(staff.Clef));
        return max > 0.0 ? max : GlyphMetrics.LineStartClefWidth(ClefType.Treble);
    }

    /// <summary>
    /// The WIDEST active key across the score's notation staves at a system starting at
    /// <paramref name="startMeasureIndex"/> — the key whose INK width the shared prefix
    /// column reserves and every staff's time signature break-aligns past. A transposed
    /// part (<see cref="Staff.PerStaffKeySignature"/>) may carry more accidentals than the
    /// primary, so its signature governs the shared time column (LP break-alignment,
    /// KeySignature group extent = union across staves). Widest by
    /// <see cref="KeySignatureInkWidth"/> — the union of engraved stencils is an ink
    /// quantity, and it is what carries a CUSTOM signature into the reservation (a custom
    /// key is <c>KeySignature(0, custom)</c>; comparing <c>Sharps</c> dropped it). All-tab
    /// scores print no key (returns C major).
    /// </summary>
    public static KeySignature WidestActiveKey(MultiStaffScore score, int startMeasureIndex)
    {
        if (score.AllStavesTab)
            return KeySignature.CMajor;
        var activeKey = score.KeySignature;
        double widestInk = -1.0;
        foreach (var staffGroup in score.StaffGroups)
            foreach (var staff in staffGroup.Staves)
            {
                var key = staff.PerStaffKeySignature ?? score.KeySignature;
                var pv = staff.PrimaryVoice;
                for (int m = 0; m < startMeasureIndex && m < pv.Measures.Length; m++)
                    foreach (var item in pv.Measures[m].Items)
                        if (item is KeySignatureChangeItem kc)
                            key = kc.NewKey;
                double ink = KeySignatureInkWidth(key);
                if (ink > widestInk)
                {
                    widestInk = ink;
                    activeKey = key;
                }
            }
        return activeKey;
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
        bool needNaturals = (prevSharps != 0 && nextSharps == 0) ||
                            (prevSharps > 0 && nextSharps < 0) || (prevSharps < 0 && nextSharps > 0) ||
                            (Math.Sign(prevSharps) == Math.Sign(nextSharps)
                             && Math.Abs(nextSharps) < Math.Abs(prevSharps));
        int natCount = needNaturals
            ? Math.Abs(prevSharps) - (Math.Sign(prevSharps) == Math.Sign(nextSharps) ? Math.Abs(nextSharps) : 0)
            : 0;

        double w = 0.8; // barline → signature gap
        if (natCount > 0)
            // Upper bound of the LP natural kerning (0.3 per overlapping pair).
            w += natCount * GlyphMetrics.AccidentalNatural.Width
               + Math.Max(0, natCount - 1) * 0.3 + 0.4;
        if (nextSharps != 0)
            w += Math.Abs(nextSharps) * GlyphMetrics.GetKeySignatureAccidentalWidth(nextSharps > 0) + 0.4;
        return w;
    }

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
        return spring.IdealDistance >= floor
            ? spring
            : new Spring(floor, spring.MinDistance, spring.InverseStretchStrength);
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

            // For chords, use AccidentalPlacement to calculate staggered positions
            var placement = new AccidentalPlacement();
            var layouts = placement.CalculatePositions(chord.Notes, headOffsets);

            if (layouts.Length > 0)
            {
                // Find the leftmost accidental position (most negative XOffset)
                // XOffset is negative, representing distance to the left of notehead
                double leftmostOffset = layouts.Min(l => l.XOffset);

                // The leftmost extent is the absolute value of the offset
                extent = Math.Max(extent, Math.Abs(leftmostOffset));
            }
        }
        else if (item is NoteItem note && note.Accidental != null)
        {
            // Single note with accidental: reserve what the placement/drawing actually uses
            // (position_apes clears the note by right-padding + padding = 0.35, and a courtesy
            // adds its parenthesis ink), NOT a bare glyph width — otherwise the accidental,
            // and especially a courtesy's left parenthesis, spills left over the bar line.
            var placement = new AccidentalPlacement();
            var layout = placement.CalculateSinglePosition(note);
            if (layout.HasValue)
                extent = Math.Max(extent, Math.Abs(layout.Value.XOffset));
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

        // LILYPOND-REF: lily/spacing-basic.cc note_spacing()
        //   ret.set_inverse_stretch_strength(fraction * std::max(0.1, (len - min)));
        // where min = increment_ (NOT skyline min_distance).
        // Skyline min_distance is set later via set_min_distance() but does NOT
        // affect inverse_stretch_strength. This ensures accidentals (which increase
        // skyline min_distance) don't make springs stiffer — they stretch equally.
        double inverseStretchStrength = Math.Max(0.1, idealDistance - defaultMin);

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>
    /// Calculates stem direction optical correction for spacing ([Wanske] p.138:
    /// up-stem→down-stem needs extra space, down-stem→up-stem less).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction:
    /// - opposite directions → correction scales with the stems' vertical
    ///   OVERLAP: min(|overlap|/7, 1) · leftDir · stem-spacing-correction
    ///   (different_directions_correction, :140-160)
    /// - same direction → only when the head ranges do NOT overlap and the gap
    ///   exceeds one staff position: ±same-direction-correction depending on
    ///   which side is lower (same_direction_correction, :162-197); skipped
    ///   when an accidental sticks out of the right side (:305-308)
    /// Simplifications vs LilyPond (beam membership is not visible at spacing
    /// time): the flagged-unbeamed-left gate (:264-266) and the knee special
    /// case (:289-292) are not applied. Stem directions ARE beam-resolved —
    /// the collector bakes the beam's direction into the items.
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
    /// Stem-direction optical correction for the spring that runs from a note column
    /// INTO a bar line, where the bar line stands in for the right-hand stem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:243-248 stem_dir_correction — when the right
    /// column carries a bar line, LilyPond synthesises the right-hand stem from the bar:
    /// <code>
    ///   stem_dirs[RIGHT] = -stem_dirs[LEFT];
    ///   stem_posns[RIGHT] = bar_yextent;
    ///   stem_posns[RIGHT] *= 2;
    /// </code>
    /// so the directions are opposite BY CONSTRUCTION and
    /// different_directions_correction always runs, then is HALVED (:263-264).
    /// LILYPOND-REF: lily/staff-spacing.cc bar_y_positions — the bar's Y extent divided
    /// by the staff space, i.e. staff-spaces; the <c>*= 2</c> above converts it to staff
    /// POSITIONS (half-spaces), the unit StemSpacingInfo already reports.
    ///
    /// A plain bar line spans the staff, so on a normal five-line staff that extent is
    /// ±2 staff-spaces → ±4 staff positions. (LilyPond takes this from the bar grob and
    /// only for glyphs beginning "|" or "."; this path is the ordinary staff bar, and
    /// like the item→bar-line skyline beside it, it assumes the standard staff.)
    ///
    /// Returns 0 when the left column has no visible stem — a whole note or a rest —
    /// which is LilyPond's `if (!stem || Stem::is_invisible (stem)) return;` (:200-201)
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
    /// <see cref="Spring.Merge"/>, exactly as LilyPond merges the simultaneous
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
        Spring? merged = null;
        foreach (var voice in voices)
        {
            var left = NoteColumnAt(voice, tLeft);
            var right = NoteColumnAt(voice, tRight);
            if (left is null || right is null)
                continue;

            double corr = CalculateStemCorrection(left, right, noteParams);
            Spring wish = corr != 0
                ? new Spring(
                    Math.Max(baseSpring.MinDistance, baseSpring.IdealDistance + corr),
                    baseSpring.MinDistance,
                    baseSpring.InverseStretchStrength)
                : baseSpring;

            merged = merged is null ? wish : Spring.Merge(merged, wish);
        }
        return merged ?? baseSpring;
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
        Spring? merged = null;
        foreach (var voice in voices)
        {
            if (NoteColumnAt(voice, tLeft) is not { } left)
                continue;

            double corr = CalculateStemCorrectionToBarline(left, noteParams);
            Spring wish = corr != 0
                ? new Spring(
                    Math.Max(0, baseSpring.IdealDistance + corr),
                    baseSpring.MinDistance,
                    baseSpring.InverseStretchStrength)
                : baseSpring;

            merged = merged is null ? wish : Spring.Merge(merged, wish);
        }
        return merged ?? baseSpring;
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
    /// direction correction. Null for stemless items (rests, whole notes).
    /// </summary>
    private static (bool StemUp, double StemMin, double StemMax, double HeadMin, double HeadMax)?
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
                double beginPos = StemBeginPosition(n.StaffPosition, n.StemUp, noteValue);
                double endPos = StemEndPosition(n.StaffPosition, n.StemUp, noteValue, n.StaffPosition);
                return (n.StemUp,
                    Math.Min(beginPos, endPos), Math.Max(beginPos, endPos),
                    n.StaffPosition, n.StaffPosition);
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
                double beginPos = StemBeginPosition(c.StemUp ? minPos : maxPos, c.StemUp, noteValue);
                double endPos = StemEndPosition(tipPos, c.StemUp, noteValue, tipPos);
                return (c.StemUp,
                    Math.Min(beginPos, endPos), Math.Max(beginPos, endPos),
                    minPos, maxPos);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Stem-attachment Y coordinate on the notehead, in LilyPond's −1..1 scale
    /// (−1 = bottom edge, +1 = top edge of the head's bounding box). Font metric of
    /// Emmentaler, dumped from <c>NoteHead.stem-attachment</c> on LilyPond 2.24.4;
    /// the value for a down stem is the negation. Black head (s2) 0.34147639,
    /// half/open head (s1) 0.47524055.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/note-head.cc:150-196 get_stem_attachment;
    /// scm/define-grobs.scm NoteHead.stem-attachment (ly:note-head::calc-stem-attachment).</remarks>
    private const double BlackHeadStemAttachY = 0.34147639283381404;
    private const double HalfHeadStemAttachY = 0.4752405486932206;

    /// <summary>
    /// Where the stem MEETS THE HEAD, in staff positions (+up) — the head-side end of
    /// the stem's y-extent. Not the head centre: the stem attaches a fraction of the
    /// head height off centre (up-stem above, down-stem below).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:934-963 internal_calc_stem_begin_position —
    ///   <c>pos = head_position + head_height.linear_combination (stem_attachment_Y) * 2 / ss</c>.
    /// The notehead's own bounding box (GlyphMetricsGenerated) supplies head_height;
    /// staff space is 1 here. Verified on LilyPond 2.24.4: a G4 (position −2) up-stem
    /// quarter begins at −1.6285 (LP dumps the Stem Y-extent as −1.627788), which makes
    /// the note → bar-line stem correction 0.20102 against LilyPond's 0.200992 — the
    /// prior head-centre approximation (−2.0) gave 0.214286.
    /// </remarks>
    private static double StemBeginPosition(int headPosition, bool stemUp, int noteValue)
    {
        double attachY = (stemUp ? 1.0 : -1.0)
            * (noteValue == 2 ? HalfHeadStemAttachY : BlackHeadStemAttachY);
        var head = GlyphMetrics.GetNoteheadBBox(noteValue);
        // Interval::linear_combination(w): w=−1 → Bottom, +1 → Top.
        double lc = head.Bottom + (attachY + 1.0) / 2.0 * (head.Top - head.Bottom);
        return headPosition + lc * 2.0; // * 2 / ss, ss = 1
    }

    /// <summary>
    /// Unbeamed stem end in staff positions (+up), via the LilyPond stem-length
    /// rules (stem.cc internal_calc_stem_end_position).
    /// </summary>
    private static double StemEndPosition(int attachPos, bool stemUp, int noteValue, int staffPosition)
    {
        // StemCalculator works in the renderer's Y-down staff-space frame with
        // the staff middle at staffTopDown + 2; use middle = 0 → staffTopDown = −2.
        double attachY = -attachPos * 0.5;
        double endY = StemCalculator.CalculateStemEndY(
            attachY, stemUp, staffTopDown: -2.0,
            StemCalculator.GetDurationLog(noteValue), staffPosition);
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
    /// Calculates the total width of a grace note group using spring-based spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs
    /// Creates individual springs for each grace note using per-group common shortest
    /// duration, then sums the minimum distances to get the rod (minimum width).
    /// The grace→main junction adds GraceToMainRod padding.
    ///
    /// This replaces the fixed-width calculation in GetGraceGroupWidth with a
    /// LP-compliant spring-based approach.
    /// </remarks>
    public static double CalculateGraceGroupSpringWidth(
        ImmutableArray<GraceNoteInfo> notes,
        GraceSpacingParameters? graceParams = null)
    {
        if (notes.IsDefaultOrEmpty)
            return 0;

        var gp = graceParams ?? GraceSpacingParameters.Default;

        // LILYPOND-REF: lily/grace-spacing-engraver.cc — per-group common shortest duration
        double bsd = CalculateGraceGroupShortestDuration(notes);

        double totalIdealDistance = 0;

        // Create a spring for each grace note and sum ideal distances
        // LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs
        // Grace columns are positioned at ideal distances (not compressed to min)
        for (int i = 0; i < notes.Length; i++)
        {
            var spring = CreateGraceSpring(notes[i].BaseDuration, gp, bsd);
            totalIdealDistance += spring.IdealDistance;
        }

        // LILYPOND-REF: lily/grace-spacing-engraver.cc:65-80
        // Add rod from grace group to main note (junction padding)
        totalIdealDistance += GraceToMainRod;

        // The leftmost grace note's accidental hangs further left of the group's
        // first head, so it sets how far the group protrudes before the main note;
        // reserve it (scaled with the grace head). Without this a grace accidental
        // (e.g. \grace { fis16 }) could overrun the barline or the previous note.
        // LILYPOND-REF: lily/accidental-placement.cc — accidentals reserve left extent.
        if (notes[0].Accidental is { } acc0)
        {
            double accW = GlyphMetrics.GetAccidentalBBox(acc0).Width
                        + GlyphMetrics.AccidentalNoteGap;
            totalIdealDistance += accW * GraceNoteItem.ScaleFactor;
        }

        return totalIdealDistance;
    }

    /// <summary>
    /// Rod distance from last grace note to the main note.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc — distance from grace column to main column
    /// </remarks>
    public const double GraceToMainRod = 0.4;

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
    /// Adjusts a spring's MinDistance using spring-based grace note width calculation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs
    /// Uses per-group common shortest duration and individual grace springs
    /// to calculate the rod (minimum distance) more accurately than fixed widths.
    /// </remarks>
    public static Spring AdjustSpringForGraceNotes(Spring spring,
        ImmutableArray<GraceNoteInfo> graceNotes,
        GraceSpacingParameters? graceParams = null)
    {
        if (graceNotes.IsDefaultOrEmpty)
            return spring;

        double graceWidth = CalculateGraceGroupSpringWidth(graceNotes, graceParams);
        double newMin = Math.Max(spring.MinDistance, spring.MinDistance + graceWidth);
        double newIdeal = Math.Max(spring.IdealDistance, newMin);

        return new Spring(newIdeal, newMin, spring.InverseStretchStrength);
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
    private static double MusicalColumnLeftReach(MusicItem item) =>
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
        // LILYPOND-REF: lily/spacing-basic.cc:113-119 — fall back to delta_t when no playing duration is known.
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
    internal static Spring ApplyLeftHeadWidth(Spring spring, IEnumerable<MusicItem> leftItems)
    {
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
                NoteItem or ChordItem => GlyphMetrics.GetNoteheadBBox(GetNoteValue(p)).Right,
                // A rest is drawn glyph-left-aligned at its column, so its right
                // extent from the column origin is the rest stencil's right edge.
                RestItem => GlyphMetrics.GetRestBBox(GetNoteValue(p)).Right,
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
        return new Spring(ideal, spring.MinDistance, spring.InverseStretchStrength);
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
        Fraction shortest = Fraction.Zero;
        bool found = false;

        foreach (var m in allMeasures)
        {
            Fraction t = Fraction.Zero;
            foreach (var item in m.Items)
            {
                Fraction end = t + item.Duration;
                // The note "plays" at `timing` iff t <= timing < end.
                if (t <= timing && timing < end && item.Duration > Fraction.Zero)
                {
                    if (!found || item.Duration < shortest)
                    {
                        shortest = item.Duration;
                        found = true;
                    }
                }
                t = end;
            }
        }

        return shortest;
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
            spring = ApplyLeftHeadWidth(spring, One(prevItem));
            spring = AdjustSpringForGraceNotes(spring, GraceNotesOf(nextItem));
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
        lastSpring = new Spring(
            Math.Max(0, lastSpring.IdealDistance
                + CalculateStemCorrectionToBarline(lastItem, NoteSpacingParameters.Default)),
            lastSpring.MinDistance,
            lastSpring.InverseStretchStrength);

        // Mirror of MeasureLayouter.CreateLastToBarlineSpring: a clef change opening the
        // NEXT measure is drawn before this bar line, so it widens the MINIMUM here. The
        // duration-based ideal is already bar-line framed and stays put.
        double clefAllowance = BoundaryClefAllowance(measure.EndBarline, nextMeasure);
        if (clefAllowance > 0)
            lastSpring = new Spring(
                lastSpring.IdealDistance,
                lastSpring.MinDistance + clefAllowance,
                lastSpring.InverseStretchStrength);

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
    public static ImmutableArray<Spring> ApplyChordRowSpacing(
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> timings,
        int measureIndex,
        ImmutableArray<ChordNameItem> chordNames,
        bool includeAttached = false)
    {
        if (chordNames.IsDefaultOrEmpty || springs.Length != timings.Count + 1)
            return springs;

        var half = new double[timings.Count];
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
                    half[t] = Math.Max(half[t],
                        Rendering.TextFontMetrics.SansBold(cn.ChordText, 2.6) / 2);
                    any = true;
                    break;
                }
            }
        }
        if (!any)
            return springs;

        // LILYPOND-REF: scm/define-grobs.scm ChordName extra-spacing-width
        // (-0.5 . 0.5): each symbol's spacing extent grows 0.5 to each side,
        // so adjacent symbols keep 1.0 and a symbol clears a barline by 0.5.
        const double chordGap = 1.0;
        const double edgeGap = 0.5;
        var result = springs.ToBuilder();
        void Widen(int springIndex, double needed)
        {
            var s = result[springIndex];
            if (needed > s.MinDistance)
                result[springIndex] = new Spring(
                    Math.Max(s.IdealDistance, needed), needed, s.InverseStretchStrength);
        }
        Widen(0, half[0] + edgeGap);
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
            if (includeAttached && (half[t] <= 0 || half[t + 1] <= 0))
                continue;
            Widen(t + 1, half[t] + half[t + 1] + chordGap);
        }
        Widen(timings.Count, half[^1] + edgeGap);
        return result.ToImmutable();
    }

    /// <summary>
    /// Floors a LEAD-SHEET bar at a readable grid-cell width. Row bars carry
    /// no notation ink, so without a floor a long chart packs every bar onto
    /// one line; with it the chart wraps like a song-book grid.
    /// </summary>
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
        double extra = (gridBarMinWidth - minSum) / springs.Length;
        var result = springs.ToBuilder();
        for (int i = 0; i < result.Count; i++)
        {
            var s = result[i];
            result[i] = new Spring(
                Math.Max(s.IdealDistance, s.MinDistance + extra),
                s.MinDistance + extra, s.InverseStretchStrength);
        }
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

        const double tabGap = 0.2; // clearance between adjacent digit columns
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

    public static double CalculateSkylineDistance(MusicItem? prevItem, MusicItem? nextItem,
                                                   double staffY,
                                                   NoteSpacingParameters? noteParams = null,
                                                   bool prevBeamed = false)
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

        // Create skylines for both items (at reference X = 0)
        var rightSkyline = ItemSkylineFactory.CreateRightSkyline(prevItem, 0, staffY, prevBeamed);
        var leftSkyline = ItemSkylineFactory.CreateLeftSkyline(nextItem, 0, staffY);

        // Calculate minimum distance using skyline collision detection
        double skylineDistance = rightSkyline.Distance(leftSkyline);

        // If skylines don't overlap vertically, fall back to simple calculation
        if (double.IsNegativeInfinity(skylineDistance))
        {
            double prevRightExtent = CalculateNoteheadRightExtent(prevItem);
            double nextLeftExtent = CalculateLeftExtent(nextItem);
            return prevRightExtent + nextLeftExtent + minItemGap;
        }

        // Add minimum gap padding
        return Math.Max(skylineDistance + minItemGap, minItemGap);
    }

    /// <summary>
    /// Calculates the item's RIGHTward ink reach from its column, excluding stems and flags.
    /// </summary>
    /// <remarks>
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
