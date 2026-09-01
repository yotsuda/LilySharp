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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Rendering;

internal static partial class SharedRenderer
{
    // ---------- Notes & rests per staff ----------

    private static void DrawStaffMeasures(
        Voice voice, int voiceNumber, ImmutableArray<Voice> staffVoices,
        SystemLayout system, ScoreLayout layout, int staffIndex,
        double staffY, ClefType clef, GrobPropertyResolver resolver,
        HashSet<(int Staff, int Voice, int Measure, int Item)> beamedItems, IDrawingContext gc,
        double pageHeight,
        int fragmentFrom = int.MinValue, int fragmentTo = int.MaxValue,
        HashSet<int>? percentCovered = null)
    {
        // staffY is the top-line Y-up; the middle line is below it (device down =
        // smaller Y-up).
        double staffMiddleY = staffY - StaffHeight / 2;

        // Ledger pre-pass: requests are collected across the whole system so
        // adjacent columns can shorten each other's ledgers
        // (ledger-line-spanner.cc), then drawn BEFORE any noteheads — ledger
        // lines sit on layer 0 with the staff lines, noteheads above them, so
        // a head paints over its own ledger (visible whenever a head is
        // recolored, e.g. an editor selection highlight).
        // LILYPOND-REF: scm/define-grobs.scm LedgerLineSpanner (layer . 0);
        // NoteHead uses the default layer 1.
        var ledgerPlan = new List<LedgerRequest>();
        foreach (var (item, ledgerMl, _, itemX, _) in EnumerateStaffItems(voice, voiceNumber, system, layout, staffIndex, fragmentFrom, fragmentTo))
        {
            // Percent-covered measures draw no notes — and no ledgers either.
            if (percentCovered != null && percentCovered.Contains(ledgerMl.MeasureIndex))
                continue;
            CollectItemLedgers(item, itemX, staffMiddleY, ledgerPlan);
        }
        DrawPlannedLedgers(ledgerPlan, gc);

        foreach (var (item, ml, itemIdx, itemX, voiceX) in EnumerateStaffItems(voice, voiceNumber, system, layout, staffIndex, fragmentFrom, fragmentTo))
        {
            // Head-wipe when this voice's notehead merges with another's.
            bool headWiped = layout.IsHeadWiped(ml.MeasureIndex, voiceNumber, itemIdx);
            // Multi-voice collision: the dot-column adjustments the collision imposed
            // on this item — a preferred direction for its dots and/or a minimum X
            // that clears the opposite voice's heads.
            // LILYPOND-REF: lily/note-collision.cc:352-397 check_meshing_chords.
            var dotAdjust = layout.GetDotAdjustment(ml.MeasureIndex, voiceNumber, itemIdx);

            // \voiceOne/\voiceTwo hold only where the voice { } span does, so this
            // is asked per measure — not once per part.
            // LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
            // ⚠️ GRACE TIME OUTRANKS THE VOICE, and it is stated rather than derived from the
            // pitch: LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings —
            // ((Voice Stem direction ,UP) (Voice Slur direction ,DOWN)), so a grace stem
            // points up whatever the note's own position would ask for. The grace side model
            // has always drawn them so; the ordinary pass has to be told.
            bool? forcedStemUp = item.GraceTime
                ? true
                : VoiceDefaults.GetDefaultStemUpAt(
                    staffVoices, voiceNumber - 1, ml.MeasureIndex);

            // LILYPOND-REF: lily/grob-property.cc — apply \override / \revert at this position.
            // Each voice/staff pass restarts at its first measure; the resolver detects the
            // rewind and replays the override timeline from the top, so a later-measure
            // override activated by the PREVIOUS pass can never leak into this pass's
            // earlier measures, and a \once pops back to the value it displaced.
            if (resolver.HasOverrides)
                resolver.AdvanceTo(ml.MeasureIndex, itemIdx);

            // A percent-covered measure shows only the % sign.
            if (percentCovered != null && percentCovered.Contains(ml.MeasureIndex)
                && item is NoteItem or ChordItem or RestItem)
                continue;

            switch (item)
            {
                case NoteItem note:
                    DrawNote(note, itemX, staffMiddleY, resolver,
                        beamedItems.Contains((staffIndex, voiceNumber - 1, ml.MeasureIndex, itemIdx)),
                        forcedStemUp, headWiped, gc, pageHeight, dotAdjust, voiceX);
                    break;
                case RestItem rest:
                    // A spacer rest ('s') reserves its column width but is never
                    // drawn. Measures inside a multi-measure-rest run get their
                    // symbol from DrawMultiMeasureRests (church rest or H-bar);
                    // drawing the per-measure whole rest too would double-print.
                    // LILYPOND-REF: lily/multi-measure-rest.cc — the MMR spanner
                    // replaces the individual rests.
                    if (!rest.IsSpacer && !IsMmrCovered(layout, ml.MeasureIndex))
                    {
                        // A rest under a beam is pushed clear of it. GetRestShift is
                        // in staff positions (up-positive); staffY is Y-up, so add
                        // half a staff-space per position to move the whole rest
                        // (glyph + dots) together. LILYPOND-REF: lily/beam.cc:1331.
                        double restShift =
                            layout.GetRestShift(ml.MeasureIndex, voiceNumber - 1, itemIdx);
                        // The position the rest's ORIGIN ends up at, which is what
                        // decides its ledger: the neutral letter DrawRest draws
                        // unshifted (a whole rest hangs from +2, everything else sits
                        // on the middle line) plus the shift the voiced position and
                        // the collisions asked for.
                        // LILYPOND-REF: lily/rest.cc:166-185 Rest::glyph_name — the
                        // ledgered cut is chosen from get_position.
                        double restPosition = restShift
                            + (GlyphMetrics.NoteValueOf(rest.BaseDuration) == 1 ? 2 : 0);
                        DrawRest(rest, itemX, staffY + restShift * 0.5,
                            layout.GetRestDotOffset(ml.MeasureIndex, voiceNumber - 1, itemIdx),
                            gc, restPosition);
                    }
                    break;
                case ChordItem chord:
                    DrawChord(chord, itemX, staffMiddleY, resolver,
                        beamedItems.Contains((staffIndex, voiceNumber - 1, ml.MeasureIndex, itemIdx)),
                        forcedStemUp, headWiped, gc, pageHeight, dotAdjust, voiceX);
                    break;
                case ClefChangeItem clefChange:
                    // A leading clef change that opens a system is already drawn as the
                    // system-start clef (ResolveClef folds it) — drawing it here too
                    // would double-print the clef.
                    if (!IsSystemStartClefChange(voice, system, ml.MeasureIndex, clefChange))
                        DrawClefChange(clefChange, itemX, staffY, gc);
                    break;
                case KeySignatureChangeItem keyChange:
                    // A change that OPENS a later system is folded into that
                    // system's prefix (new key only, like LilyPond) — drawing
                    // it here too overprinted the prefix with naturals.
                    if (!IsSystemStartKeyChange(voice, system, ml.MeasureIndex, keyChange))
                        DrawKeySignatureChange(keyChange, itemX, staffY, clef, gc);
                    break;
                // A BLANKED meter is drawn nowhere — that is what blanked means
                // (TimeSignatureChangeItem.Blanked, the port of LilyPond's
                // \override TimeSignature.stencil = ##f). Unreachable as it stands, because
                // an item is only blanked when NO staff of the score engraves a meter and
                // this walk is a notation staff's; asserted here anyway so the model reads
                // the same from whichever staff walks it, and so a future staff kind cannot
                // draw ink the spacing model has already declined to reserve.
                case TimeSignatureChangeItem { Blanked: false } timeChange:
                    DrawTimeSignatureChange(timeChange, itemX, staffY, gc);
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves each drawable item's X position for one staff pass — shared by
    /// the ledger pre-pass and the note drawing pass so both see identical
    /// positions.
    /// </summary>
    private static IEnumerable<(MusicItem Item, MeasureLayout Ml, int ItemIdx, double ItemX,
                                double VoiceX)>
        EnumerateStaffItems(Voice voice, int voiceNumber, SystemLayout system, ScoreLayout layout,
            int staffIndex,
            int fragmentFrom = int.MinValue, int fragmentTo = int.MaxValue)
    {
        foreach (var ml in system.Measures)
        {
            // Ossia fragment trim: measures outside the fragment print nothing
            // (their rests belong to a context that does not exist in LP).
            if (ml.MeasureIndex < fragmentFrom || ml.MeasureIndex > fragmentTo)
                continue;
            if (ml.MeasureIndex >= voice.Measures.Length)
                continue;

            var measure = voice.Measures[ml.MeasureIndex];
            // Multi-staff scores fill MeasureLayout.Columns with timing-based X
            // anchors; per-staff Items[i].X are not aligned to the shared column
            // grid, so beams (computed from column timings) drift away from
            // noteheads if we use Items[i].X here. BeamEngraver itself uses
            // GetXForTiming when columns exist — matching that ensures stem &
            // notehead share the same X.
            bool useColumnTiming = !ml.Columns.IsDefaultOrEmpty && ml.Columns.Length > 0;
            var currentTiming = Fraction.Zero;
            // Running X for changes that OPEN this measure (clef/key/time), so
            // several are sequenced left-to-right after the barline instead of stacked.
            double openChangeX = double.NaN;
            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                var item = measure.Items[itemIdx];

                // GRACE TIME IS DRAWN HERE, by the ordinary engravers, at the font each of
                // its grobs states. That is LilyPond's own shape: a grace body is an
                // ordinary stretch of an ordinary Voice — ly/engraver-init.ly has no
                // `\name Grace` at all, only `\consists Grace_engraver` inside `\name Voice`
                // (:368), and lily/grace-engraver.cc makes no grob, it only switches
                // properties. Every size below is asked of the GROB (GrobFontSize), because
                // scm/music-functions.scm:636-650 general-grace-settings is a per-grob table
                // and not one voice-wide number.
                // ⚠️ THE X IS THE PUBLISHED ONE. Neither ordinary source can answer for a
                // grace — see ScoreLayout.GraceColumnXs — and an address the layout did not
                // publish means "this group did not reach the layout", so the item is left
                // undrawn rather than drawn at the measure's origin.
                if (item.GraceTime)
                {
                    if (layout.GetGraceColumnX(
                            staffIndex, voiceNumber - 1, ml.MeasureIndex, itemIdx) is { } graceX)
                    {
                        yield return (item, ml, itemIdx, graceX, 0);
                    }
                    // No timing advance: grace time takes no measure time at all
                    // (MusicItem.Duration is zero in it), so the column grid is untouched.
                    continue;
                }

                // A meter change opening the first measure of a (non-first)
                // system is drawn in the system-start prefix (see DrawSystem),
                // not as a measure item — skip its in-measure copy here.
                if (item is TimeSignatureChangeItem
                    && currentTiming == Fraction.Zero
                    && system.SystemIndex > 0
                    && ml.MeasureIndex == system.Measures[0].MeasureIndex)
                {
                    continue;
                }

                double itemX;
                if (useColumnTiming)
                {
                    // Timing-aligned column path (multi-staff): the shared
                    // MeasureLayout.Items is sized for the PRIMARY voice, so a
                    // secondary voice with MORE items in this measure must not be
                    // bounded by ml.Items.Length — its X comes from the timing
                    // columns, not the primary item slots. Previously the
                    // ml.Items.Length guard below ran on this path too and
                    // silently dropped the surplus secondary-voice items (e.g.
                    // beamed notes after a rest, when the other staff held a
                    // single dotted note) — their noteheads never drew.
                    itemX = ml.X + ml.GetXForTiming(currentTiming);
                }
                else
                {
                    if (itemIdx >= ml.Items.Length) { currentTiming += item.Duration; continue; }
                    itemX = ml.X + ml.Items[itemIdx].X;
                }
                currentTiming += item.Duration;

                // Mid-measure clef/key changes share the next note's timing —
                // in the column path hang them LEFT of the column (their
                // width is reserved in the preceding spring; the item-slot
                // path already gives them their own X). The following note's
                // OWN accidental also hangs left of that column, so hang the
                // change glyph past it too — otherwise the change glyph (e.g. a
                // key-cancellation natural) overprints the note's accidental
                // (e.g. a fis sharp). LILYPOND-REF: lily/paper-column.cc —
                // non-musical columns precede the musical column of the same
                // moment, and the accidentals sit between them and the heads.
                bool isChange = item is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem;
                // The item-slot path takes this branch only for the trailing clef
                // column, whose slot X is the (zero-width) measure origin — the clef
                // belongs in the previous measure's closing gap like any other
                // before-the-bar clef (Measure.IsTrailingClefColumn).
                if ((useColumnTiming || measure.IsTrailingClefColumn) && item is ClefChangeItem
                    && currentTiming == Fraction.Zero
                    && ml.MeasureIndex > 0
                    && ml.MeasureIndex != system.Measures[0].MeasureIndex
                    && BoundaryClefX(voice, ml, measure) is { } clefX)
                {
                    // A clef change OPENING the measure is engraved BEFORE the bar line,
                    // unlike a key or time change: LilyPond's unbroken break-align order is
                    // `… clef, cue-clef, staff-bar, key-cancellation, key-signature,
                    // time-signature …` (scm/define-grobs.scm:650-664). It therefore hangs
                    // back into the previous measure's closing gap, which reserves exactly
                    // this much room (SpacingRules.BoundaryClefAllowance).
                    // Skipped at a system start, where the clef is drawn in the prefix
                    // instead (see ResolveClef) and there is no bar line to precede.
                    itemX = clefX;
                }
                else if (useColumnTiming && isChange && currentTiming == Fraction.Zero)
                {
                    // A change that OPENS the measure (a section-boundary revert, or
                    // an authored key/time at the bar) anchors just after the barline
                    // — NOT hanging left of the first note. A distant first note (a
                    // low ledger note, a wide chord column) otherwise leaves the
                    // signature floating in the middle of the bar. Several opening
                    // changes (key + time) sequence left-to-right so they don't
                    // overprint each other.
                    // Break alignment inside the boundary column: the change's ink left edge
                    // sits the bar line's own space-alist distance past the bar line's ink
                    // right edge — key-signature 1.0, time-signature 0.75, NOT one padding
                    // for both. Measured on 2.24.4 as exactly those two numbers
                    // (COORDINATE_AUDIT.md §4.7.3). Several changes then follow each other by
                    // the LEFT one's entry for the right one's break-align-symbol, which is
                    // the same rule one level down.
                    // LILYPOND-REF: scm/define-grobs.scm BarLine.space-alist, :650-664
                    //   break-align-orders; lily/break-alignment-interface.cc.
                    if (double.IsNaN(openChangeX))
                    {
                        double afterBar = measure.StartBarline != BarlineType.None
                            ? GetVisualBarlineWidth(measure.StartBarline) : 0;
                        openChangeX = ml.X + afterBar + SpacingRules.GetBarlineToItemSpace(item);
                    }
                    itemX = openChangeX;
                    openChangeX += SpacingRules.ChangeColumnGlyphAdvance(item, NextChangeIn(measure, itemIdx));
                }
                else if (useColumnTiming && isChange)
                {
                    // The change column's ORIGIN, hung back from the musical column by the
                    // same gap MeasureLayouter reserved — SpacingRules.MidMeasureChangeGaps
                    // and this call share one implementation, so the drawn glyph cannot
                    // drift from the space paid for it. Several changes at one moment sit in
                    // ONE column and are sequenced left to right from that origin; they used
                    // to be hung independently from the note column and so overprinted.
                    // LILYPOND-REF: lily/staff-spacing.cc:166-215.
                    // A LOOSE column (pruned from the springs, multi-staff polyphony) hangs
                    // by the solved-room distance the layouter stored instead — the springs
                    // reserved nothing for it (my_offset = right_point - distance_to_next).
                    // LILYPOND-REF: lily/spacing-loose-columns.cc:202-220 set_loose_columns.
                    var columnItems = ChangeColumnItems(measure, itemIdx);
                    itemX -= ml.LooseChangeHangs != null
                             && ml.LooseChangeHangs.TryGetValue(currentTiming, out var hang)
                        ? hang
                        : SpacingRules.MidMeasureChangeRightGap(columnItems);
                    itemX += SpacingRules.MidMeasureChangeOffsetWithin(columnItems, item);
                }

                // Horizontal collision offset for multi-voice columns. Yielded alongside the
                // shifted X because not everything drawn at this item rides it: the
                // ACCIDENTALS belong to the staff column, not to the shifted note column
                // (LILYPOND-REF: lily/accidental-placement.cc — the AccidentalPlacement grob
                // is not inside the note column note-collision.cc translates; MEASURED in
                // Collector.StaffAccidentalColumns's remark).
                double voiceX = layout.GetVoiceOffset(ml.MeasureIndex, voiceNumber, itemIdx);
                itemX += voiceX;
                yield return (item, ml, itemIdx, itemX, voiceX);
            }
        }
    }

    /// <summary>
    /// X at which to draw a clef change that OPENS <paramref name="measure"/>: read off
    /// the boundary column, so the drawn position and the reserved width come from one
    /// place and cannot drift. Null when the boundary has no bar line to sit before.
    /// </summary>
    /// <remarks>
    /// The boundary bar line is drawn with its RIGHT edge on the column boundary
    /// (SharedRenderer.Barlines: <c>endX - width</c> where <c>endX</c> is the previous
    /// measure's right edge), so its LEFT edge is <c>ml.X - width</c>. The column's origin
    /// sits <see cref="BoundaryColumn.BarLineLeft"/> further left, and the clef's own ink
    /// starts at its column-internal <c>Left</c>.
    /// </remarks>
    private static double? BoundaryClefX(Voice voice, MeasureLayout ml, Measure measure)
    {
        var prev = voice.Measures[ml.MeasureIndex - 1];
        if (prev.EndBarline == BarlineType.None)
            return null;

        var column = BoundaryColumn.Build(prev.EndBarline, measure.Items);
        if (column.BarLineLeft is not { } barLineLeft)
            return null;

        BoundaryColumnGrob? clef = null;
        foreach (var g in column.Grobs)
            if (g.Symbol == BreakAlignSymbol.Clef)
                clef = g;
        if (clef is not { } clefGrob)
            return null;

        double barLineLeftX = ml.X - GetVisualBarlineWidth(prev.EndBarline);
        return barLineLeftX - barLineLeft + clefGrob.Left;
    }

    /// <summary>
    /// The next change item in <paramref name="measure"/> after <paramref name="itemIdx"/>,
    /// or null when the change column ends there. Only the immediately following item can be
    /// in the same column — anything else has a duration and opens the musical one.
    /// </summary>
    private static MusicItem? NextChangeIn(Measure measure, int itemIdx)
    {
        int next = itemIdx + 1;
        if (next >= measure.Items.Length)
            return null;
        return measure.Items[next] is ClefChangeItem or KeySignatureChangeItem
                                    or TimeSignatureChangeItem
            ? measure.Items[next]
            : null;
    }

    /// <summary>
    /// The items sharing the change column at <paramref name="itemIdx"/>: the whole run of
    /// zero-duration changes it belongs to, plus the musical item that shares their moment.
    /// </summary>
    /// <remarks>
    /// SpacingRules prices the column from exactly this list — the changes give it its width
    /// and its space-alist, the musical item gives the rod its left-hand reach (a wide
    /// accidental pushes the column further left). Passing anything narrower would let the
    /// drawn position disagree with the reserved space.
    /// </remarks>
    // internal: SkylineBuilder's key-change seed reads the same column-run so its
    // hung-back x cannot drift from the drawn one.
    internal static List<MusicItem> ChangeColumnItems(Measure measure, int itemIdx)
    {
        int start = itemIdx;
        while (start > 0 && IsChangeItem(measure.Items[start - 1]))
            start--;

        var items = new List<MusicItem>();
        for (int k = start; k < measure.Items.Length; k++)
        {
            items.Add(measure.Items[k]);
            if (!IsChangeItem(measure.Items[k]))
                break;      // the musical item that closes the moment
        }
        return items;

        static bool IsChangeItem(MusicItem item) =>
            item is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem;
    }


    /// <summary>
    /// Registers the ledger requests one item (note or chord) needs. Chords
    /// contribute at most one request per outside-staff direction; the extreme
    /// head drives the ledger run (inner heads share its lines).
    /// </summary>
    private static void CollectItemLedgers(MusicItem item, double x, double staffMiddleY,
        List<LedgerRequest> ledgerPlan)
    {
        switch (item)
        {
            case NoteItem note:
            {
                int noteValue = GlyphMetrics.NoteValueOf(note.BaseDuration);
                // The head's INK width, not its advance: LilyPond's ledger takes the head's
                // grob EXTENT as both the base interval and the basis of length-fraction.
                // LILYPOND-REF: lily/ledger-line-spanner.cc:228-230 Ledger_line_spanner::print — head_extent = h->extent (common_x, X_AXIS), widened by length_fraction * head_extent.length ()
                // This is the DRAW half of the pair SkylineBuilder seeds; the two must stay
                // one spelling.
                // (Every notehead's ink Left is 0.000000, so HeadLeft = x still holds.)
                double headWidth = GlyphMetrics.GetNoteheadBBox(noteValue).Width
                                   * GrobFontSize.ScaleOf(note, SizedGrob.NoteHead);
                CollectLedgerRequest(ledgerPlan, note.StaffPosition, x, headWidth,
                    staffMiddleY, note.Accidental != null);
                break;
            }
            case ChordItem chord when chord.Notes.Length > 0:
            {
                int noteValue = GlyphMetrics.NoteValueOf(chord.BaseDuration);
                double chordScale = GrobFontSize.ScaleOf(chord, SizedGrob.NoteHead);
                // The ink width — see the note branch above.
                double headWidth = GlyphMetrics.GetNoteheadBBox(noteValue).Width * chordScale;
                // Seconds shift reversed heads sideways — the ledger run
                // follows the extreme head's real X.
                double[] offsets = ChordHeadPositioning.CalculateOffsets(
                    chord.Notes, chord.StemUp, noteValue, chordScale);
                int maxIdx = -1, minIdx = -1;
                for (int i = 0; i < chord.Notes.Length; i++)
                {
                    if (maxIdx < 0 || chord.Notes[i].StaffPosition > chord.Notes[maxIdx].StaffPosition) maxIdx = i;
                    if (minIdx < 0 || chord.Notes[i].StaffPosition < chord.Notes[minIdx].StaffPosition) minIdx = i;
                }
                if (chord.Notes[maxIdx].StaffPosition >= 5)
                    CollectLedgerRequest(ledgerPlan, chord.Notes[maxIdx].StaffPosition,
                        x + offsets[maxIdx], headWidth,
                        staffMiddleY, chord.Notes[maxIdx].Accidental != null);
                if (chord.Notes[minIdx].StaffPosition <= -5)
                    CollectLedgerRequest(ledgerPlan, chord.Notes[minIdx].StaffPosition,
                        x + offsets[minIdx], headWidth,
                        staffMiddleY, chord.Notes[minIdx].Accidental != null);
                break;
            }
        }
    }

    /// <summary>Stem start offset from the head CENTER, in the renderer's Y-up frame
    /// (the drawn start is <c>noteY − offset</c>, so NEGATIVE is above the centre).
    /// A styled head reads the font's own per-direction attachment point — the same
    /// LILC entry the stem's X comes from — because an asymmetric shape attaches each
    /// stem where its ink is: s2triangle starts an up stem 0.1262 ABOVE centre but a
    /// down stem 0.6828 BELOW it, and neither is the mirror of the other.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:934-963 internal_calc_stem_begin_position — begin =
    ///   head position + the font attachment Y (the normalisation round-trips, see
    ///   GlyphMetrics.GetNoteheadStemAttachment).
    /// LILYPOND-REF: lily/open-type-font.cc:334-369 attachment_point — DOWN is the
    ///   font's attachment-down entry, not a reflection.
    /// <para>
    /// ⚠️ The DEFAULT head keeps the older hand rule (filled heads recess the start
    /// ±<see cref="StemHeadInset"/> toward the stem's far end; open heads butt the
    /// centre) instead of the font's ±0.186200 (s2) / ±0.259000 (s1) — switching it
    /// moves every stem rect in every snapshot by 0.036 (black) / 0.259 (half) and
    /// wants its own point (ticketed 2026-08-07, session 108: default-head
    /// stem-begin regime).
    /// </para>
    /// <para>
    /// ⚠️ UNSCALED, unlike the X side (session 109 audit): the styled Y is the 20
    /// design's coordinate while <see cref="LayoutUtilities.StemAttachX(bool, int, NoteheadStyle, double)"/>
    /// takes the cue's headScale — LilyPond scales the whole attachment with the
    /// grob's font. A CUE styled head is the only reader of the difference and no
    /// book measures one; ticketed with the default-head regime above.
    /// </para>
    /// </remarks>
    private static double StemAttachYOffset(NoteheadStyle style, bool stemUp, int noteValue) => style switch
    {
        NoteheadStyle.Default when noteValue >= 4 => stemUp ? -StemHeadInset : StemHeadInset,
        NoteheadStyle.Default => 0,
        _ => -GlyphMetrics.GetNoteheadStemAttachment(style, stemUp, noteValue).Y,
    };

    /// <summary>How far a filled round head recesses the stem's start toward the far
    /// end so the join clears the head's slanted corner.</summary>
    private const double StemHeadInset = 0.15;

    /// <summary>
    /// The stem parameters this item's stem is measured and drawn with — null at the staff's
    /// own size, so the calculator keeps its own defaults.
    /// </summary>
    /// <remarks>
    /// A <c>length-fraction</c> is NOT a font-size, which is why it is asked separately from
    /// <see cref="GrobFontSize"/>: LilyPond declares both, and for a grace they disagree
    /// (0.8 against <c>magstep(-3)</c> = 0.707107) while for a cue they happen to coincide.
    /// LILYPOND-REF: lily/stem.cc:481-557 <c>Stem::internal_calc_stem_end_position</c> — the
    ///   fraction multiplies the length the duration chose, wherever it was declared:
    ///   <c>\name CueVoice</c> in ly/engraver-init.ly spells <c>#(magstep -4)</c>, and
    ///   scm/music-functions.scm:636-650 <c>general-grace-settings</c> states a flat 0.8.
    /// The GRACE arm is what HANDOFF §2 U8 ⒝2 handed the grace stem to in session 313, and the
    /// dot column has read the stem it produces since session 315 — so the flag support a grace
    /// dot is gated on now moves with the shortening, which is what LilyPond does
    /// (scratch/p315/measurements.md). See <see cref="GrobFontSize.GraceStemDetails"/>.
    /// </remarks>
    private static StemDetails? StemDetailsOf(MusicItem item)
        => item.GraceTime ? GrobFontSize.GraceStemDetails
         : item switch
           {
               NoteItem { IsCue: true } or ChordItem { IsCue: true }
                   => EngravingDefaults.CueStemDetails,
               _ => null,
           };

    /// <summary>
    /// Draws one note at <paramref name="x"/>, which already carries
    /// <paramref name="voiceX"/> — the multi-voice collision shift. Everything the note
    /// column owns rides that shift; its ACCIDENTAL does not, and subtracts it back off to
    /// reach the staff column it was packed against
    /// (<see cref="LilySharp.Core.Svg.Collector.StaffAccidentalColumns"/>).
    /// </summary>
    private static void DrawNote(NoteItem note, double x, double staffMiddleY,
        GrobPropertyResolver resolver, bool isBeamed, bool? forcedStemUp, bool headWiped,
        IDrawingContext gc, double pageHeight, DotAdjustment dotAdjust = default, double voiceX = 0)
    {
        int noteValue = GlyphMetrics.NoteValueOf(note.BaseDuration);
        double noteY = staffMiddleY + note.StaffPosition / 2.0;
        // EVERY SIZE BELOW IS ASKED OF THE GROB, not derived from one "how small is this
        // note" number — a cue states one context-wide font-size and a grace states a
        // per-grob table whose Accidental is a step below its NoteHead (GrobFontSize).
        // A cue grob's glyphs are the THIRTEEN design's own outline read at magstep(−4).
        double headScale = GrobFontSize.ScaleOf(note, SizedGrob.NoteHead);
        double noteFontSize = FontSize * headScale;
        double flagFontSize = FontSize * GrobFontSize.ScaleOf(note, SizedGrob.Flag);

        // Voice stem direction override (voice 1 up / voice 2 down); falls back
        // to the note's own position-based default in single-voice staves. A
        // per-note @stemUp/@stemDown outranks the voice default — in LilyPond
        // only the \\ sub-lists are voicified and an explicit \stemDown is a
        // later property set, so the writer's ask survives either way (must
        // match ResolveVoiceStemDirections, which skips these when baking).
        bool stemUp = note.ForcedStemUp ?? forcedStemUp ?? note.StemUp;

        // Accidental (left of notehead). Placed through the SAME single-ape skyline path the
        // spacing reservation uses (AccidentalPlacement.CalculateSinglePosition), so draw =
        // reserve: a natural clears the head by its real right skyline (0.367672), not a fixed
        // AccidentalNoteGap 0.35 (sharp/flat are 0.35 either way). Cue notes scale their
        // accidental with the head (LP CueVoice fontSize = -4 reduces the accidental grob too).
        if (note.Accidental != null)
        {
            double accScale = GrobFontSize.ScaleOf(note, SizedGrob.Accidental);
            // ⚠️ THE FONT IS THE DESIGN THE FONT-SIZE SELECTS, not the 20 shrunk. It was
            // Design20.Scaled(0.66) until 2026-08-03 — a rounded scale off the wrong table,
            // which is both halves of the same mistake: a cue states font-size −4, that asks
            // for 12.599pt, and 12.599pt lands on the THIRTEEN design, whose glyphs are drawn
            // differently and not merely smaller (Emmentaler is optically sized).
            // ⚠️ TWO FONTS, NOT ONE, because the placement clears an ACCIDENTAL against a
            // HEAD and the two grobs need not state the same size: a cue's context-wide
            // fontSize gives both −4, while general-grace-settings gives the accidental −4
            // and the head −3 (GrobFontSize / GraceNoteItem.AccidentalFontSizeStep). Null
            // for a full-size note keeps the callee on its own defaults, byte for byte.
            var accFont = GrobFontSize.IsReduced(note)
                ? GrobFontSize.FontOf(note, SizedGrob.Accidental) : null;
            var accHeadFont = GrobFontSize.IsReduced(note)
                ? GrobFontSize.FontOf(note, SizedGrob.NoteHead) : null;
            // The packed X is measured from the STAFF COLUMN, so undo the collision shift
            // this note's head took; without a packing there is no other voice on the
            // column and the two frames coincide.
            double? accInkLeft = note.AccidentalX is { } packedX
                ? x - voiceX + packedX
                : AccidentalColumn.CalculateSinglePosition(note, accFont, accHeadFont)
                    is { } al ? x + al.XOffset : null;
            if (accInkLeft is { } inkLeft)
                using (GrobFontSize.IsReduced(note)
                       ? gc.MusicFace(GrobFontSize.DesignOf(note, SizedGrob.Accidental))
                       : NullScope.Instance)
                    DrawAccidentalAtInkLeft(note.Accidental, note.IsCourtesy, inkLeft, noteY,
                        note.SourcePosition, gc, accScale);
        }

        // Notehead — skipped when this head merges with another voice's (head wipe)
        // or when NoteHead.transparent is overridden.
        // LILYPOND-REF: lily/note-collision.cc:403-406 (calc_positioning_done)
        // LILYPOND-REF: lily/grob-property.cc — NoteHead.transparent
        Color? noteheadColor = ResolveColor(resolver, "NoteHead");
        bool headTransparent = resolver.GetBool("NoteHead", "transparent") == true;
        char head = EmmentalerGlyphs.GetNotehead(note.Notehead, noteValue);
        if (!headWiped && !headTransparent)
            using (gc.Source(note.SourcePosition))
            // A cue head is drawn OUT OF ITS OWN DESIGN, paired with the CueFont the
            // reservation measured — see EngravingDefaults.CueDesignSize.
            using (GrobFontSize.IsReduced(note)
                   ? gc.MusicFace(GrobFontSize.DesignOf(note, SizedGrob.NoteHead))
                   : NullScope.Instance)
            {
                if (note.IsDead)
                    DrawDeadNotehead(x, noteY, noteheadColor, gc);
                else
                {
                    // Interactive preview gets a tight click target the size of the
                    // head ink (× the cue scale), centred on noteY — see DrawNotehead.
                    // Both axes from the ink box, as the remark above says: the width
                    // read the ADVANCE until 2026-08-05 (session 95) while the height
                    // beside it read the ink.
                    gc.DrawNotehead(head, x, noteY, noteFontSize, noteheadColor,
                        GlyphMetrics.GetNoteheadBBox(noteValue).Width * headScale,
                        GlyphMetrics.GetNoteheadBBox(noteValue).Height * headScale);
                }
            }

        // Ledger lines are drawn by the staff-measure ledger pre-pass, BEFORE
        // any noteheads (CollectItemLedgers/DrawPlannedLedgers).

        // WHAT THE DOT COLUMN STANDS BEHIND. LilyPond hands Dot_column its note column's
        // stem and flag as side-support-elements and builds a rightward skyline over them
        // (lily/dot-column.cc:100-141), so the two are collected HERE, where they are
        // computed for drawing, and read by the dot block below — one spelling of each
        // quantity, not a second one for the dots.
        // ⚠️ A BEAMED COLUMN HANDS OVER NOTHING, because its stem is drawn by DrawBeams and
        // this method never computes one. LilyPond's Dot_column would still see that stem —
        // but a stem's right edge is its head's own attachment point, so it cannot out-reach
        // the floor the skyline already stands on, and a beamed column has no flag at all.
        // (The FLAG is the support that binds, and only an unbeamed column has one.)
        //   departs from: lily/dot-column.cc:100-109 — the stem is a support whether or not a
        //     beam owns its end.
        //   goes away when: the beamed stem's geometry is available here (it is DrawBeams'
        //     today), or the dot column is built where the stem is.
        //   observed by: no observer, and none is possible while the term is dominated — it
        //     becomes visible only for a head whose attachment is left of its own ink.
        var dotSupports = new List<DotColumn.Support>();

        // Stem & flag — beamed notes are handled by DrawBeams (which draws the
        // beam-aware stem to the actual beam Y), so skip both here to avoid a
        // duplicated short stem layered under the beam stem.
        // LILYPOND-REF: lily/stem.cc — beamed stem end determined by beam layout.
        if (noteValue >= 2 && !isBeamed)
        {
            Color? stemColor = ResolveColor(resolver, "Stem");
            // A transparent stem prints NO ink but keeps its extent — spacing,
            // attachment and everything hung on the stem stay where they were
            // (\hideNotes is Stem.transparent, ly/property-init.ly).
            // LILYPOND-REF: lily/grob.cc:164-176 get_print_stencil — transparent
            //   replaces the stencil with an empty box of the same extent.
            bool stemTransparent = resolver.GetBool("Stem", "transparent") == true;
            // The up-stem attaches at the head's own right edge (attachment − thick/2), or the
            // stem floats off a reduced head; a down-stem attaches at the head's left edge.
            // LILYPOND-REF: lily/stem.cc internal_calc_stem_offset_from_head —
            // the offset comes from the head's extent, read from the head's OWN font.
            // ⚠️ THE HEAD'S OWN FONT, not the twenty's box times a magstep. Emmentaler is
            // optically sized, so a reduced head is a DIFFERENT outline and its attachment is
            // its own: MEASURED on 2.26.0 (scratch/p314/{cue,grace}dot-dump.ly, the stem's X
            // centre minus its head's left) — a CUE stem stands 0.750400 = 0.815355 (Design13's
            // black head at magstep(−4)) − 0.065, and a GRACE stem 0.852950 = 0.917940
            // (Design14's at magstep(−3)) − 0.065, where the twenty's 1.304200 scaled would
            // give 0.756500 and 0.857100. The old spelling was 0.006100 / 0.004150 out, and the
            // grace half went live when HANDOFF §2 U8 ⒝2 handed grace stems to this method —
            // the grace house, the beam quanter and the spacing chain all read the grace font
            // (LayoutUtilities.StemX's font overload), so this was also a drift between the
            // drawn stem and the one they reserve.
            double stemX = x + LayoutUtilities.StemAttachX(
                stemUp, noteValue, note.Notehead,
                GrobFontSize.IsReduced(note) ? GrobFontSize.FontOf(note, SizedGrob.NoteHead) : null);
            // Duration-dependent length + unnatural-direction shortening + the
            // extend-to-center-line rule, faithfully following LilyPond's
            // Stem::internal_calc_stem_end_position (lily/stem.cc:481).
            int durLog = StemCalculator.GetDurationLog(noteValue);
            // StemCalculator is device (Y-down); convert at its boundary. Derive its
            // device inputs from the Y-up locals in scope — device note Y = pageHeight −
            // noteY, device staff top = pageHeight − (staff middle + half the staff) —
            // then flip its device result back to page Y-up.
            double deviceNoteY = pageHeight - noteY;
            double deviceStaffTop = pageHeight - (staffMiddleY + StaffHeight / 2.0);
            // A CUE STEM IS SHORTER, and by LilyPond's own declaration rather than by the
            // head scale: EngravingDefaults.CueStemDetails carries Stem.length-fraction into
            // the same house the spacing correction and the horizontal skyline read
            // (SpacingRules.StemSpacingInfo), so the stem this draws is the stem they reserve.
            double stemEndY = pageHeight - StemCalculator.CalculateStemEndY(
                deviceNoteY, stemUp, deviceStaffTop, durLog, note.StaffPosition,
                StemDetailsOf(note));
            if (!stemTransparent)
                gc.DrawLine(stemX, noteY - StemAttachYOffset(note.Notehead, stemUp, noteValue),
                    stemX, stemEndY,
                    stemColor ?? Color.Black, EngravingDefaults.StemThickness);

            // The stem as a dot support — its own X extent's RIGHT edge, over the seven
            // positions LilyPond walks from the head it stands on. Transparency does not
            // remove it: a transparent grob keeps its extent (lily/grob.cc:164-176).
            // LILYPOND-REF: lily/dot-column.cc:103-109, in Dot_column::calc_positioning_done.
            dotSupports.Add(DotColumn.StemSupport(
                note.StaffPosition, stemUp,
                stemX - x + EngravingDefaults.StemThickness / 2));

            bool hasFlag = false;
            if (noteValue >= 8)
            {
                var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
                if (flag.HasValue)
                {
                    // The flag INHERITS its transparency from the stem it hangs on
                    // (measured: \hideNotes c'8 prints neither stem nor flag), but its
                    // grob — and so hasFlag, which places the tremolo — survives.
                    // LILYPOND-REF: scm/define-grobs.scm:1631-1632 Flag transparent = grob::inherit-parent-property
                    if (!stemTransparent)
                        // The flag hangs on the stem's RIGHT EDGE — LayoutUtilities.FlagDrawX is the
                        // one house for that term, and it is measured (ledger flag.x.*).
                        // ⚠️ THE FACE, NOT ONLY THE SIZE. Emmentaler is optically sized, so a
                        // font-size selects a DESIGN and then a magnification, and drawing a
                        // reduced flag out of the score's own design gives the twenty's
                        // outline shrunk instead of the fourteen's own — the same pairing
                        // GrobFontSize.FontOf/DesignOf exists to keep together, and the same
                        // one the notehead above already asks for.
                        using (GrobFontSize.IsReduced(note)
                               ? gc.MusicFace(GrobFontSize.DesignOf(note, SizedGrob.Flag))
                               : NullScope.Instance)
                            gc.DrawGlyph(flag.Value, LayoutUtilities.FlagDrawX(stemX), stemEndY,
                                flagFontSize, stemColor);
                    hasFlag = true;
                    // The flag as a dot support — the one support taken at its GLYPH's ink
                    // rather than at a nominal band, which is why a SHORT stem changes the
                    // dot's X and a long one does not (DotColumn's remarks carry the measured
                    // pair). Its transparency does not remove the grob's extent.
                    // ⚠️ THE STEM'S CENTRE, NOT ITS DRAWN RIGHT EDGE: Flag::width declares the
                    // stencil's extent MINUS the same half-thickness FlagDrawX adds, so the
                    // grob EXTENT the dot column reads sits back on the stem's centre
                    // (LayoutUtilities.FlagDrawX's remarks state the pair).
                    // LILYPOND-REF: lily/dot-column.cc:130-141 Dot_column::calc_positioning_done —
                    //   its loop over Stem::flag (stem).
                    var flagBox = GlyphMetrics.GetFlagBBox(
                        GrobFontSize.FontOf(note, SizedGrob.Flag), noteValue, stemUp);
                    if (flagBox != default)
                        dotSupports.Add(DotColumn.FlagSupport(
                            stemEndY - staffMiddleY + flagBox.Bottom,
                            stemEndY - staffMiddleY + flagBox.Top,
                            stemX - x + flagBox.Right));
                }
                // An acciaccatura's stroke, drawn where the flag is because in LilyPond it IS
                // the flag's — Flag.stroke-style = "grace" (MusicItem.GraceSlash). It follows
                // the flag's transparency for the same reason the flag follows the stem's.
                if (note.GraceSlash && !stemTransparent)
                    DrawGraceSlash(stemX, stemEndY,
                        GrobFontSize.ScaleOf(note, SizedGrob.Flag), gc);
            }

            if (note.HasTremolo)
                // StemTremolo declares NO transparent inheritance (unlike Flag/Beam,
                // scm/define-grobs.scm StemTremolo), so its slashes keep their ink
                // even on a transparent stem.
                DrawTremolo(stemX, stemEndY, stemUp, noteValue, note.TremoloBeams, hasFlag, gc);
        }
        else if (noteValue < 2 && !isBeamed && note.HasTremolo)
        {
            // Whole-note tremolo: there is no stem to hang the slashes on, so
            // they anchor 1.5ss beyond the head in the (would-be) stem
            // direction and centre on the head.
            // LILYPOND-REF: lily/stem-tremolo.cc:349-366 y_offset — end_y =
            //   note_head + dir * 1.5 when duration_log <= 0 (invisible stem);
            //   :243-248 untranslated_stencil aligns the stack on the flag
            //   closest to the head, further flags 0.81 apart (:115-125
            //   get_beam_translation, beamless).
            // LILYPOND-REF: scm/define-grobs.scm StemTremolo
            //   (parent-alignment-X . CENTER) — centred on the head column.
            double headCenterX = x
                + GlyphMetrics.GetNoteheadBBox(noteValue).Width / 2 * headScale;
            DrawStemlessTremolo(headCenterX, noteY, stemUp, note.TremoloBeams, gc);
        }

        // Augmentation dots: the dot column stands one dot-width right of whatever the
        // column's supports leave — the head's own ink right (per-duration: whole/half heads
        // are wider) unless a support reaches further at a row a dot is on. Successive dots
        // are spaced one dot-width apart.
        // LILYPOND-REF: scm/define-grobs.scm DotColumn —
        //   (padding . dot-column-interface::pad-by-one-dot-width)
        // LILYPOND-REF: scm/output-lib.scm ly:dots::print — stack with
        //   padding = one dot width (advance per dot = 2 dot widths)
        // ⚠️ "The head's right edge" is its INK right, and this line computed the ADVANCE
        // until 2026-08-05 (session 95). LilyPond builds the dot column's base X from the
        // head's grob EXTENT, which is the ink (1.962 / 1.3774 / 1.3042 against advances of
        // 1.960 / 1.376 / 1.304 — dumped in audit/lp-geometry/probes/dynamic-support.ly).
        // LILYPOND-REF: lily/dot-column.cc:82-84 Dot_column::calc_positioning_done — base_x.unite (Stem::first_head (parent_stems[i])->extent (commonx, X_AXIS))
        // ⚠️ THE DOT'S OWN FONT, because the dot's own font-size is what is drawn: two dots
        // are stacked one dot WIDTH apart, so measuring a full-size dot for a reduced one
        // spaces the pair for a glyph that is not there. general-grace-settings gives Dots
        // −3 and a cue's context-wide fontSize gives it −4 (GrobFontSize).
        double dotWidth = GrobFontSize.FontOf(note, SizedGrob.Dots).AugmentationDot.Width;
        // ⚠️ A GRACE'S DOT IS DRAWN HERE TOO, since session 315 — the second half of HANDOFF
        // §2 U8 ⒝2. Nothing below asks whether the note is a grace: the floor is the head's own
        // font's ink, the flag support hangs off the stem THIS METHOD JUST DREW, and both come
        // out of GrobFontSize's per-grob table. That is what makes the grace's answer LilyPond's
        // — the side model measured the flag off a RETIRED flat stem (3.5 × the font scale) and
        // so could not tell a shortened stem from an unshortened one. MEASURED on canonical
        // 2.26.0 (scratch/p315/measurements.md, six books): the dot's push is decided by the
        // DRAWN stem length, not by whether the head sits on a line — `\grace { g'8. }` lifts
        // its dot and still answers 1.226585 (its unshortened 2.80 stem holds the flag clear),
        // while `\grace { d''8. }` at the same "on a line, lifted" description answers 1.747274
        // because its stem is shortened to 2.50 and the flag comes down with it.
        if (note.Dots > 0)
        {
            // Same Dot_configuration machinery as chords. The dots' preferred
            // direction has two layers, exactly LilyPond's: \voiceOne/\voiceTwo set
            // Dots.direction on the WHOLE voice (UP for odd voices, DOWN for even —
            // so a lone down-voice dotted line-note dips below its line), and a
            // positive-shift collision overrides the down voice's dots to UP.
            // LILYPOND-REF: scm/music-functions.scm:616-631 direction-polyphonic-grobs — Dots
            // LILYPOND-REF: lily/note-collision.cc:374-397 check_meshing_chords — set_property direction
            int dotDir = dotAdjust.DirectionUp ? 1
                : forcedStemUp switch { true => 1, false => -1, null => 0 };
            int dotPos = DotConfiguration.Resolve(
                new[] { note.StaffPosition },
                dotDir != 0 ? new[] { dotDir } : null)[0];
            double dotY = staffMiddleY + dotPos / 2.0;
            // WHERE THE COLUMN STANDS IS THE PORTED RULE — a rightward skyline over the
            // column's supports, floored on the head's own ink right, plus one dot width.
            // The ROW decides whether a support is in the way at all, which is why
            // DotConfiguration ran first (DotColumn.OffsetX's parameters say so).
            // ⚠️ THIS IS NOT A NO-OP AT FULL SIZE, and the belief that it was is what kept the
            // flat rule here for so long: Emmentaler's eighth flag curls back to 3.0502 BELOW
            // the stem end, which is 0.45 ABOVE the head of a 3.5 stem, so a dot LIFTED onto
            // the next row lands in it. RE-MEASURED on LilyPond 2.26.0 with a grob dump
            // (scratch/p314/flagdot-dump.ly): g'8. 2.517400 (pushed), f'8. 1.754200 (not),
            // g'16. and f'16. 2.517400 BOTH — the sixteenth's flag reaches even an unlifted
            // dot. Lily# answered 1.754200 for three of those four. DotColumn's remarks carry
            // the full table and the correction of the claim that stood there before.
            // The observer is test/dotted-flag-dot-column; of 63 of the owner's real books two
            // moved, both onto LilyPond's number.
            // The floor is the head's ink right OUT OF THE HEAD'S OWN FONT, the pairing the
            // dot width beside it already uses — MEASURED on 2.26.0 (scratch/p314/cuedot-dump.ly):
            // a cue head inks 0.815355 where the twenty's box at magstep(−4) is 0.821497, so
            // the cue's f'8. dot stands at 1.086700 and not 1.092800.
            double dotStartX = x + DotColumn.OffsetX(
                GlyphMetrics.GetNoteheadBBox(
                    GrobFontSize.FontOf(note, SizedGrob.NoteHead), noteValue).Right,
                dotSupports, new[] { dotPos }, dotWidth);
            // A collision's dot side supports push the whole dot column right of the
            // opposite voice's heads; the minimum X is in the staff column's frame
            // (x − voiceX), settled in NoteCollision.CalculateVoiceOffsets.
            // LILYPOND-REF: lily/note-collision.cc:352-372 check_meshing_chords — add_support.
            if (dotAdjust.ColumnMinX is { } dotMinX)
                dotStartX = Math.Max(dotStartX, x - voiceX + dotMinX);
            // The DOTS' own font-size, out of the DOTS' own design — general-grace-settings
            // gives Dots the head's −3 and not the accidental's −4, which is why it is asked
            // per grob (GrobFontSize) even where the answer happens to match the head's.
            using (GrobFontSize.IsReduced(note)
                   ? gc.MusicFace(GrobFontSize.DesignOf(note, SizedGrob.Dots))
                   : NullScope.Instance)
                for (int d = 0; d < note.Dots; d++)
                    gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot,
                        dotStartX + d * 2 * dotWidth, dotY,
                        FontSize * GrobFontSize.ScaleOf(note, SizedGrob.Dots), noteheadColor);
        }
    }

    /// <summary>Draws one chord; <paramref name="voiceX"/> is the collision shift already in
    /// <paramref name="x"/>, which the accidentals subtract back off — see
    /// <see cref="DrawNote"/>.</summary>
    private static void DrawChord(ChordItem chord, double x, double staffMiddleY,
        GrobPropertyResolver resolver, bool isBeamed, bool? forcedStemUp, bool headWiped,
        IDrawingContext gc, double pageHeight, DotAdjustment dotAdjust = default, double voiceX = 0)
    {
        int noteValue = GlyphMetrics.NoteValueOf(chord.BaseDuration);
        char head = EmmentalerGlyphs.GetNotehead(chord.Notehead, noteValue);
        Color? noteheadColor = ResolveColor(resolver, "NoteHead");
        // LILYPOND-REF: lily/grob-property.cc — NoteHead.transparent
        bool headTransparent = resolver.GetBool("NoteHead", "transparent") == true;
        // Writer's @stemUp/@stemDown outranks the voice default — see DrawNote.
        bool stemUp = chord.ForcedStemUp ?? forcedStemUp ?? chord.StemUp;

        // EVERY SIZE IS ASKED OF THE GROB — see DrawNote. A cue chord takes the same
        // context-wide font-size −4 its heads do; a grace chord's accidental is a step below
        // its head, which is why the two scales are separate locals here.
        double headScale = GrobFontSize.ScaleOf(chord, SizedGrob.NoteHead);
        double accScale = GrobFontSize.ScaleOf(chord, SizedGrob.Accidental);
        double noteFontSize = FontSize * headScale;
        double flagFontSize = FontSize * GrobFontSize.ScaleOf(chord, SizedGrob.Flag);

        // Within-chord seconds/unisons: reversed heads shift to the far side
        // of the stem. LILYPOND-REF: lily/stem.cc:606-760 calc_positioning_done.
        double[] headOffsets = ChordHeadPositioning.CalculateOffsets(
            chord.Notes, stemUp, noteValue, headScale);

        // Accidentals through the full placement machinery (stagger/skylines),
        // aware of the shifted head ink — drawing each one at the same fixed
        // offset overprints them for seconds (e.g. <fis gis>).
        // LILYPOND-REF: lily/accidental-placement.cc position_apes.
        // A cue chord's accidentals shrink with its heads — LP runs the cue grobs at
        // fontSize -4 — but the PADDINGS between them and the heads do not (they are the
        // staff's, not the font's; see AccidentalPlacementParameters). The FONT is the design
        // that font-size selects, the same one the single-note path takes.
        var chordAccFont = GrobFontSize.IsReduced(chord)
            ? GrobFontSize.FontOf(chord, SizedGrob.Accidental) : null;
        var chordHeadFont = GrobFontSize.IsReduced(chord)
            ? GrobFontSize.FontOf(chord, SizedGrob.NoteHead) : null;
        // A chord sharing its column with another voice was packed against that voice's
        // accidentals too, and in the STAFF COLUMN's frame — so those X's undo the collision
        // shift, exactly as the single-note branch does.
        bool packedColumn = chord.HasPackedAccidentals;
        double accOriginX = packedColumn ? x - voiceX : x;
        var accLayouts = packedColumn
            ? chord.Notes
                .Where(n => n.Accidental is not null && n.AccidentalX is not null)
                .Select(n => new AccidentalLayout(
                    n.StaffPosition, n.Accidental!, n.AccidentalX!.Value, n.IsCourtesy))
                .ToImmutableArray()
            : AccidentalColumn.CalculatePositions(
                chord.Notes, headOffsets, chordAccFont, chordHeadFont);
        foreach (var al in accLayouts)
        {
            double ay = staffMiddleY + al.StaffPosition / 2.0;
            // Anchor the accidental to its own member's pitch offset so it
            // highlights together with that head (fall back to the chord).
            int accSource = chord.SourcePosition;
            foreach (var n in chord.Notes)
                if (n.StaffPosition == al.StaffPosition && n.SourcePosition >= 0) { accSource = n.SourcePosition; break; }
            using (GrobFontSize.IsReduced(chord)
                   ? gc.MusicFace(GrobFontSize.DesignOf(chord, SizedGrob.Accidental))
                   : NullScope.Instance)
                DrawAccidentalAtInkLeft(al.Accidental, al.IsCourtesy,
                    accOriginX + al.XOffset, ay, accSource, gc, accScale);
        }

        // topY/bottomY are the visually top/bottom heads. In the Y-up frame the top
        // head has the LARGER Y, so topY tracks the max and bottomY the min.
        double topY = double.MinValue, bottomY = double.MaxValue;
        int maxPos = int.MinValue, minPos = int.MaxValue;
        for (int i = 0; i < chord.Notes.Length; i++)
        {
            var n = chord.Notes[i];
            double y = staffMiddleY + n.StaffPosition / 2.0;
            // A drum chord mixes heads per member (bd default, hh cross).
            char memberHead = n.Notehead != NoteheadStyle.Default
                ? EmmentalerGlyphs.GetNotehead(n.Notehead, noteValue)
                : head;
            // Each head carries ITS OWN pitch source offset so the interactive
            // preview highlights/selects one chord note at a time and jumps the
            // caret to that pitch, not the chord's '<' (falls back to the chord
            // when a member has no recorded position).
            if (!headWiped && !headTransparent)
                using (gc.Source(n.SourcePosition >= 0 ? n.SourcePosition : chord.SourcePosition))
                    gc.DrawNotehead(memberHead, x + headOffsets[i], y, noteFontSize, noteheadColor,
                        GlyphMetrics.GetNoteheadAdvance(noteValue) * headScale,
                        GlyphMetrics.GetNoteheadBBox(noteValue).Height * headScale);
            if (y > topY) topY = y;
            if (y < bottomY) bottomY = y;
            if (n.StaffPosition > maxPos) maxPos = n.StaffPosition;
            if (n.StaffPosition < minPos) minPos = n.StaffPosition;
        }

        // Ledger lines are drawn by the staff-measure ledger pre-pass, BEFORE
        // any noteheads (CollectItemLedgers/DrawPlannedLedgers).

        // THE STEM'S GEOMETRY, COMPUTED ONCE. It is DRAWN below, after the dots, but the dot
        // column stands behind it (LilyPond hands the stem and the flag to Dot_column as
        // side-support-elements), so the numbers are settled here and read twice rather than
        // spelled twice — docs/RULES.md §5.2.1②. Nothing is drawn here, so the ink order is
        // exactly what it was.
        bool chordHasStem = noteValue >= 2 && chord.Notes.Length > 0 && !isBeamed;
        double stemX = 0, stemEndY = 0;
        var dotSupports = new List<DotColumn.Support>();
        if (chordHasStem)
        {
            // The stem attaches at the head's own right (up) or left (down) edge, read from
            // the HEAD'S OWN font — see DrawNote for the measured cue and grace numbers.
            stemX = x + LayoutUtilities.StemAttachX(
                stemUp, noteValue, chord.Notehead,
                GrobFontSize.IsReduced(chord) ? GrobFontSize.FontOf(chord, SizedGrob.NoteHead) : null);
            // The length is reckoned from the stem-tip-side notehead (top note for
            // stem-up, bottom for stem-down), following LilyPond's
            // Stem::internal_calc_stem_end_position (stem.cc:481).
            int stemTipPos = stemUp ? maxPos : minPos;
            int durLog = StemCalculator.GetDurationLog(noteValue);
            // StemCalculator is device; convert at its boundary. Derive device inputs
            // from the Y-up locals — device tip Y = pageHeight − (staff middle + tip
            // pos/2), device staff top = pageHeight − (staff middle + half staff) — as
            // in DrawNote, then flip its device result back to page Y-up.
            double deviceTipY = pageHeight - (staffMiddleY + stemTipPos / 2.0);
            double deviceStaffTop = pageHeight - (staffMiddleY + StaffHeight / 2.0);
            // Cue length-fraction, exactly as in DrawNote.
            stemEndY = pageHeight - StemCalculator.CalculateStemEndY(
                deviceTipY, stemUp, deviceStaffTop, durLog, stemTipPos,
                StemDetailsOf(chord));
            // The stem as a dot support: from the head it STANDS ON (the bottom head for an
            // up stem — Stem::head_positions[-dir]) seven positions along itself.
            // LILYPOND-REF: lily/dot-column.cc:103-109, in Dot_column::calc_positioning_done.
            dotSupports.Add(DotColumn.StemSupport(
                stemUp ? minPos : maxPos, stemUp,
                stemX - x + EngravingDefaults.StemThickness / 2));
            // The flag as a dot support, at its glyph's ink and on the stem's CENTRE — the
            // pair of reasons is written out in DrawNote.
            // LILYPOND-REF: lily/dot-column.cc:130-141 Dot_column::calc_positioning_done —
            //   its loop over Stem::flag (stem).
            if (noteValue >= 8)
            {
                var flagBox = GlyphMetrics.GetFlagBBox(
                    GrobFontSize.FontOf(chord, SizedGrob.Flag), noteValue, stemUp);
                if (flagBox != default)
                    dotSupports.Add(DotColumn.FlagSupport(
                        stemEndY - staffMiddleY + flagBox.Bottom,
                        stemEndY - staffMiddleY + flagBox.Top,
                        stemX - x + flagBox.Right));
            }
        }

        // Augmentation dots: one dot ROW per chord note, all in one column one dot-width
        // right of whatever the column's supports leave. Final positions come from the full
        // Dot_configuration port (badness-scored up/down displacement with
        // cascading; on-line dots forced into spaces).
        // LILYPOND-REF: scm/define-grobs.scm DotColumn padding (one dot width)
        // LILYPOND-REF: lily/dot-configuration.cc; lily/dot-column.cc:194-224.
        // The dot column clears heads reversed to the RIGHT of the stem.
        // A grace chord's dot comes through here as well — see the single-note branch for the
        // measured reason the drawn stem is what decides it.
        if (chord.Dots > 0 && chord.Notes.Length > 0)
        {
            // The head's INK right, and the DOT's own font's dot — see the single-note
            // branch for both, and for the LilyPond citation.
            double dotWidth = GrobFontSize.FontOf(chord, SizedGrob.Dots).AugmentationDot.Width;
            // Two-layer preferred direction, as in the single-note branch: the voice
            // props set Dots.direction voice-wide, a positive-shift collision
            // overrides the down voice's dots to UP.
            // LILYPOND-REF: scm/music-functions.scm:616-631 direction-polyphonic-grobs — Dots
            // LILYPOND-REF: lily/note-collision.cc:374-397 check_meshing_chords — set_property direction
            int dotDir = dotAdjust.DirectionUp ? 1
                : forcedStemUp switch { true => 1, false => -1, null => 0 };
            var resolved = DotConfiguration.Resolve(
                chord.Notes.Select(n => n.StaffPosition).ToArray(),
                dotDir != 0 ? Enumerable.Repeat(dotDir, chord.Notes.Length).ToArray() : null);
            // The ported rule, as in DrawNote — a rightward skyline over the supports,
            // floored on the head ink right. ⚠️ THE FLOOR CARRIES THE REVERSED HEADS: a
            // second inside the chord puts a head on the far side of the stem, and the dot
            // column stands right of THAT, which is what headOffsets.Max() adds here and
            // what LilyPond gets from uniting the heads' own extents.
            // LILYPOND-REF: lily/dot-column.cc:82-84 — base_x.unite (Stem::first_head (…)).
            double dotStartX = x + DotColumn.OffsetX(
                GlyphMetrics.GetNoteheadBBox(
                        GrobFontSize.FontOf(chord, SizedGrob.NoteHead), noteValue).Right
                    + Math.Max(0, headOffsets.Max()),
                dotSupports, resolved, dotWidth);
            // Collision dot side supports — same push as the single-note branch.
            // LILYPOND-REF: lily/note-collision.cc:352-372 check_meshing_chords — add_support.
            if (dotAdjust.ColumnMinX is { } dotMinX)
                dotStartX = Math.Max(dotStartX, x - voiceX + dotMinX);
            // The DOTS' design, paired with the width above — see DrawNote.
            using (GrobFontSize.IsReduced(chord)
                   ? gc.MusicFace(GrobFontSize.DesignOf(chord, SizedGrob.Dots))
                   : NullScope.Instance)
                foreach (int p in resolved)
                {
                    double dotY = staffMiddleY + p / 2.0;
                    for (int d = 0; d < chord.Dots; d++)
                        using (gc.Source(chord.SourcePosition))
                            gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot,
                                dotStartX + d * 2 * dotWidth, dotY,
                                FontSize * GrobFontSize.ScaleOf(chord, SizedGrob.Dots),
                                noteheadColor);
                }
        }

        // Skip chord stem when chord is part of a beam — DrawBeams handles it.
        // The geometry was settled above (chordHasStem / stemX / stemEndY), where the dot
        // column had to read it; this is the DRAWING.
        // LILYPOND-REF: lily/stem.cc — beamed stem end determined by beam layout.
        if (chordHasStem)
        {
            Color? stemColor = ResolveColor(resolver, "Stem");
            // Ink-only, extent kept — see the single-note branch (DrawNote).
            // LILYPOND-REF: lily/grob.cc:164-176 get_print_stencil — transparent
            //   replaces the stencil with an empty box of the same extent.
            bool stemTransparent = resolver.GetBool("Stem", "transparent") == true;
            // The stem attaches at the FAR notehead (the length was reckoned from the
            // tip-side one above).
            double stemStartY = (stemUp ? bottomY : topY)
                - StemAttachYOffset(chord.Notehead, stemUp, noteValue);
            if (!stemTransparent)
                gc.DrawLine(stemX, stemStartY, stemX, stemEndY,
                    stemColor ?? Color.Black, EngravingDefaults.StemThickness);

            // The flag is the STEM's grob, indifferent to how many heads hang on it —
            // LilyPond makes one Flag per stem and the flag reads only its stem, so a
            // chord takes exactly the single-note recipe (DrawNote). This branch was
            // MISSING outright: an unbeamed <…>8 drew a bare stem with no flag
            // (scratch/ベースタブLy/blogger2.lys, 起票 2026-08-05).
            // LILYPOND-REF: lily/stem-engraver.cc:152-160 — the Flag grob is created
            //   per Stem (and :165-172 kills it only for a BEAMED stem);
            // LILYPOND-REF: lily/flag.cc:118-165 Flag::print — the glyph is chosen by
            //   duration-log and stem direction alone.
            bool hasFlag = false;
            if (noteValue >= 8)
            {
                var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
                // The flag inherits the stem's transparency — see DrawNote.
                // LILYPOND-REF: scm/define-grobs.scm:1631-1632 Flag transparent = grob::inherit-parent-property
                if (flag.HasValue)
                {
                    if (!stemTransparent)
                        // Out of the FLAG's own design, paired with its own size — see DrawNote.
                        using (GrobFontSize.IsReduced(chord)
                               ? gc.MusicFace(GrobFontSize.DesignOf(chord, SizedGrob.Flag))
                               : NullScope.Instance)
                            gc.DrawGlyph(flag.Value, LayoutUtilities.FlagDrawX(stemX), stemEndY,
                                flagFontSize, stemColor);
                    hasFlag = true;
                }
            }
            // An acciaccatura's stroke belongs to the flag — see DrawNote / MusicItem.GraceSlash.
            if (chord.GraceSlash && !stemTransparent)
                DrawGraceSlash(stemX, stemEndY,
                    GrobFontSize.ScaleOf(chord, SizedGrob.Flag), gc);

            // The tremolo is the STEM's grob too — one StemTremolo per stem however
            // many heads hang on it, the single-note recipe exactly (DrawNote). This
            // call was MISSING outright, like the flag branch above once was: a chord
            // tremolo (`<c e g>4:16`, `\repeat tremolo 4 q16`) drew a bare stem with
            // no slashes while the single-note form drew them
            // (repeat-tremolo-chord-rep.ly, 2026-08-08).
            // LILYPOND-REF: lily/stem-engraver.cc — the StemTremolo hangs on the Stem;
            // LILYPOND-REF: lily/stem-tremolo.cc — geometry reads only the stem/flag.
            if (chord.HasTremolo)
                DrawTremolo(stemX, stemEndY, stemUp, noteValue, chord.TremoloBeams, hasFlag, gc);
        }
        else if (noteValue < 2 && !isBeamed && chord.HasTremolo && chord.Notes.Length > 0)
        {
            // Whole-note chord tremolo: no stem, so the slashes anchor off the
            // OUTERMOST head in the (would-be) stem direction — the head LilyPond's
            // whole-note branch reads — and centre on the head column, exactly the
            // single-note recipe (DrawNote).
            // LILYPOND-REF: lily/stem-tremolo.cc:349-366 y_offset — end_y =
            //   note_head + dir * 1.5 when duration_log <= 0 (invisible stem).
            double headCenterX = x + GlyphMetrics.GetNoteheadBBox(noteValue).Width / 2 * headScale;
            DrawStemlessTremolo(headCenterX, stemUp ? topY : bottomY, stemUp,
                chord.TremoloBeams, gc);
        }
    }

    /// <summary>
    /// Resolves the active color override for a grob type, or null when no
    /// override is active or the override is a no-op (black is treated as
    /// "no override" to keep drawing helpers using their default fill).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/color.scm — x11-color-list / x11-color mapping
    /// Accepts named colors and #rgb / #rrggbb hex codes.
    /// </remarks>
    private static Color? ResolveColor(GrobPropertyResolver resolver, string grobType)
    {
        if (!resolver.HasOverrides) return null;
        var s = resolver.GetString(grobType, "color");
        if (string.IsNullOrEmpty(s)) return null;
        return ColorParser.Parse(s);
    }

    // ---------- Ledger lines (ledger-line-spanner.cc port) ----------

    /// <summary>
    /// One note column's ledger needs in one vertical direction — the unit
    /// LilyPond's Ledger_line_spanner reasons about when shortening
    /// neighbouring ledgers against each other.
    /// </summary>
    private sealed class LedgerRequest
    {
        public double HeadLeft, HeadRight;
        public double LedgerLeft, LedgerRight; // clamped by the shortening pass
        public int ExtremePos;                 // signed staff position of the far head
        public double StaffMiddleY;
        public bool HasAccidental;
    }

    /// <summary>
    /// Registers a column's ledger request. Columns at the FIRST position
    /// outside the staff (|pos| == 5) carry no ledgers themselves but still
    /// participate, shortening their neighbours' ledgers.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/ledger-line-spanner.cc:223-226.</remarks>
    private static void CollectLedgerRequest(List<LedgerRequest> plan, int extremePos,
        double x, double headWidth, double staffMiddleY, bool hasAccidental)
    {
        if (Math.Abs(extremePos) < 5)
            return;

        double ext = EngravingDefaults.LedgerLengthFraction * headWidth;
        plan.Add(new LedgerRequest
        {
            HeadLeft = x,
            HeadRight = x + headWidth,
            LedgerLeft = x - ext,
            LedgerRight = x + headWidth + ext,
            ExtremePos = extremePos,
            StaffMiddleY = staffMiddleY,
            HasAccidental = hasAccidental,
        });
    }

    /// <summary>
    /// Shortens neighbouring ledger extents against each other, then draws.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ledger-line-spanner.cc:279-326 — for adjacent
    /// out-of-staff columns in the same direction, each side's ledger is
    /// clamped to the midpoint between the facing head edges; when BOTH
    /// columns are beyond the first space outside the staff (|pos| ≥ 6, i.e.
    /// both actually carry ledgers) a gap of 0.1 staff spaces is kept between
    /// them so the ledgers never read as one line.
    /// LILYPOND-REF: lily/ledger-line-spanner.cc:359-369 — ledgers of a note
    /// with an accidental are shortened on the LEFT to midway between the
    /// accidental's right edge and the head's left edge. (LilyPond limits
    /// this to the glyph's font-provided vertical shortening range; we
    /// approximate that range as ±3 staff positions around the head.)
    /// </remarks>
    private static void DrawPlannedLedgers(List<LedgerRequest> plan, IDrawingContext gc)
    {
        if (plan.Count == 0)
            return;

        const double gap = 0.1; // LedgerLineSpanner (gap . 0.1)
        const int accidentalRange = 3; // approximation of ledger_shortening_range

        foreach (var direction in new[] { 1, -1 })
        {
            var reqs = plan
                .Where(r => Math.Sign(r.ExtremePos) == direction)
                .OrderBy(r => r.HeadLeft)
                .ToList();

            for (int i = 1; i < reqs.Count; i++)
            {
                var prev = reqs[i - 1];
                var cur = reqs[i];
                double center = (prev.HeadRight + cur.HeadLeft) / 2.0;
                bool both = Math.Abs(prev.ExtremePos) >= 6 && Math.Abs(cur.ExtremePos) >= 6;
                double half = both ? gap / 2.0 : 0.0;
                prev.LedgerRight = Math.Min(prev.LedgerRight, center - half);
                cur.LedgerLeft = Math.Max(cur.LedgerLeft, center + half);
            }
        }

        double thickness = EngravingDefaults.LegerLineThickness;
        foreach (var req in plan)
        {
            int extreme = req.ExtremePos;
            int step = extreme > 0 ? 2 : -2;
            for (int pos = extreme > 0 ? 6 : -6;
                 extreme > 0 ? pos <= extreme : pos >= extreme;
                 pos += step)
            {
                double left = req.LedgerLeft;
                if (req.HasAccidental && Math.Abs(pos - extreme) <= accidentalRange)
                {
                    double accRight = req.HeadLeft - GlyphMetrics.AccidentalNoteGap;
                    left = Math.Max(left, (accRight + req.HeadLeft) / 2.0);
                }
                if (left >= req.LedgerRight)
                    continue;

                double y = req.StaffMiddleY + pos / 2.0;
                gc.DrawLine(left, y, req.LedgerRight, y, Color.Black, thickness);
            }
        }
    }

    private static void DrawLedgerLines(int staffPosition, double x, double staffMiddleY,
        IDrawingContext gc, double headWidth = EngravingDefaults.NoteheadBlackWidth,
        double unit = 1.0)
    {
        // ledger_extent = head_extent widened by length-fraction·head_width —
        // proportional to the ACTUAL head, so whole/half noteheads (wider than
        // black ones) get correspondingly longer, centered ledgers.
        // LILYPOND-REF: lily/ledger-line-spanner.cc:204-233 (length-fraction 0.25)
        // LILYPOND-REF: lily/staff-symbol.cc:337-344 (thickness 1.0·line + 0.1·space)
        double ext = EngravingDefaults.LedgerLengthFraction * headWidth;
        double thickness = EngravingDefaults.LegerLineThickness;
        double x1 = x - ext;
        double x2 = x + headWidth + ext;

        // `unit` shrinks the per-step offsets from the (already-transformed)
        // staff middle — used by ossia grace groups, whose Ys go through the
        // staff-top affine while this helper computes offsets itself.
        double YOf(int pos) => staffMiddleY
            + ((staffMiddleY + pos / 2.0) - staffMiddleY) * unit;

        // Ledger lines above staff (staff position > 4 = above top line)
        for (int pos = 6; pos <= staffPosition; pos += 2)
        {
            double y = YOf(pos);
            gc.DrawLine(x1, y, x2, y, Color.Black, thickness);
        }
        // Ledger lines below staff (staff position < -4 = below bottom line)
        for (int pos = -6; pos >= staffPosition; pos -= 2)
        {
            double y = YOf(pos);
            gc.DrawLine(x1, y, x2, y, Color.Black, thickness);
        }
    }

    private static void DrawRest(RestItem rest, double x, double staffY, int? dotOffset,
        IDrawingContext gc, double staffPosition)
    {
        int noteValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
        // staffY is the top-line Y-up; rest origins sit below it (device down =
        // smaller Y-up).
        // LILYPOND-REF: lily/rest.cc Rest::staff_position_internal — the semibreve
        // (whole) rest is raised one staff line (duration_log==0 returns pos+2)
        // relative to half-note and shorter rests.
        double y = noteValue == 1 ? staffY - 1 : staffY - 2;  // whole rests hang from 4th line
        DrawRestAtOrigin(rest, x, y, dotOffset, gc, staffPosition);
    }

    /// <summary>The rest glyph and its dots, at the ORIGIN the caller already resolved.</summary>
    /// <remarks>
    /// Split out of <see cref="DrawRest"/> in session 308 so a GRACE rest can reach it. A
    /// grace column's Y goes through the ossia affine before it is drawn
    /// (<c>OssiaShrink.YUp</c>), so that caller holds the ORIGIN rather than the top line,
    /// and handing it a top line to subtract an unshrunk 2 from would misplace the rest on an
    /// ossia staff.
    /// <para>
    /// ⚠️ A GRACE REST COMES THROUGH HERE AT <see cref="FontSize"/> — FULL SIZE — and reusing
    /// this method rather than writing a scaled copy is the point.
    /// <c>general-grace-settings</c> gives a <c>font-size</c> to Stem, Flag, NoteHead,
    /// TabNoteHead, Dots, Accidental, Script, Fingering and StringNumber, and NOT to Rest
    /// (scm/music-functions.scm:636-650, canonical v2.26.0), so a grace rest reads the
    /// STAFF's size. MEASURED side by side in one book (scratch/p308/lp2/s2_gracerestchord,
    /// <c>\grace { r16 d'16 }</c>): the rest drawn at 0.0040 and the grace head beside it at
    /// 0.0028 = magstep(−3), the rest's path data byte-identical to a main-stream rest's.
    /// </para>
    /// </remarks>
    private static void DrawRestAtOrigin(RestItem rest, double x, double y, int? dotOffset,
        IDrawingContext gc, double staffPosition)
    {
        int noteValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
        char glyph = EmmentalerGlyphs.GetRest(noteValue, staffPosition);
        using (gc.Source(rest.SourcePosition))
            gc.DrawGlyph(glyph, x, y, FontSize);

        // Augmentation dots: one dot-width right of the rest's ink, at the position the
        // dot COLUMN solved RELATIVE to the rest's origin (the dot's Y-parent is the
        // rest, so it rides every shift the rest took). Absent an entry, the solo answer
        // applies: one position up off the origin's line — one position DOWN for a
        // hanging semibreve.
        // LILYPOND-REF: lily/dot-column.cc:194-227 calc_positioning_done;
        //   :252-257 — rest dots translate by the rest extent plus one dot width;
        // LILYPOND-REF: scm/output-lib.scm:652-664 dots::calc-staff-position.
        if (rest.Dots > 0)
        {
            double dotWidth = GlyphMetrics.AugmentationDot.Width;
            double dotStartX = x + GlyphMetrics.GetRestBBox(noteValue).Right + dotWidth;
            double dotY = y
                + (dotOffset ?? LilySharp.Core.Svg.Layout.ElementCoordinator.RestDotDefaultOffset(noteValue)) * 0.5;
            for (int d = 0; d < rest.Dots; d++)
                using (gc.Source(rest.SourcePosition))
                    gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot,
                        dotStartX + d * 2 * dotWidth, dotY, FontSize);
        }
    }

}
