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
        foreach (var (item, ledgerMl, _, itemX, _) in EnumerateStaffItems(voice, voiceNumber, system, layout, fragmentFrom, fragmentTo))
        {
            // Percent-covered measures draw no notes — and no ledgers either.
            if (percentCovered != null && percentCovered.Contains(ledgerMl.MeasureIndex))
                continue;
            CollectItemLedgers(item, itemX, staffMiddleY, ledgerPlan);
        }
        DrawPlannedLedgers(ledgerPlan, gc);

        foreach (var (item, ml, itemIdx, itemX, voiceX) in EnumerateStaffItems(voice, voiceNumber, system, layout, fragmentFrom, fragmentTo))
        {
            // Head-wipe when this voice's notehead merges with another's.
            bool headWiped = layout.IsHeadWiped(ml.MeasureIndex, voiceNumber, itemIdx);
            // Multi-voice collision: this down voice's on-line augmentation dot is
            // forced below the line (instead of the default up) to clear the up
            // voice's dot. ⚠️ Lily#'s rule, not LilyPond's — NoteCollision.AnalyzeCollision
            // has the account. LILYPOND-REF: lily/note-collision.cc:375-398.
            bool dotForceDown = layout.IsDotForcedDown(ml.MeasureIndex, voiceNumber, itemIdx);

            // \voiceOne/\voiceTwo hold only where the voice { } span does, so this
            // is asked per measure — not once per part.
            // LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
            bool? forcedStemUp = VoiceDefaults.GetDefaultStemUpAt(
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
                        forcedStemUp, headWiped, gc, pageHeight, dotForceDown, voiceX);
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
                        double restShiftY =
                            layout.GetRestShift(ml.MeasureIndex, voiceNumber - 1, itemIdx) * 0.5;
                        DrawRest(rest, itemX, staffY + restShiftY, gc);
                    }
                    break;
                case ChordItem chord:
                    DrawChord(chord, itemX, staffMiddleY, resolver,
                        beamedItems.Contains((staffIndex, voiceNumber - 1, ml.MeasureIndex, itemIdx)),
                        forcedStemUp, headWiped, gc, pageHeight, dotForceDown, voiceX);
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
                case TimeSignatureChangeItem timeChange:
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
                if (useColumnTiming && item is ClefChangeItem
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
                    var columnItems = ChangeColumnItems(measure, itemIdx);
                    itemX -= SpacingRules.MidMeasureChangeRightGap(columnItems);
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
    private static List<MusicItem> ChangeColumnItems(Measure measure, int itemIdx)
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
                double headWidth = GlyphMetrics.GetNoteheadBBox(noteValue).Width * (note.IsCue ? EngravingDefaults.CueScale : 1.0);
                CollectLedgerRequest(ledgerPlan, note.StaffPosition, x, headWidth,
                    staffMiddleY, note.Accidental != null);
                break;
            }
            case ChordItem chord when chord.Notes.Length > 0:
            {
                int noteValue = GlyphMetrics.NoteValueOf(chord.BaseDuration);
                double chordScale = chord.IsCue ? EngravingDefaults.CueScale : 1.0;
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

    /// <summary>Stem start offset from the head CENTER for styled noteheads:
    /// cross/slash ink only reaches the attach edge at its CORNERS, so the stem
    /// joins the corner on the stem's side (±½ss); the do-triangle's corners
    /// are both at the bottom. A FILLED round head starts the stem just INSIDE the
    /// head (toward the stem's far end): the stem is painted over the head, and on a
    /// slanted oval its straight edge would otherwise step past the head's tapering
    /// corner at the exact centre — recessing the start hides that step where the
    /// head is wider. An OPEN head (half / whole) must NOT recess: its centre is
    /// hollow, so a recessed stem would show inside the void; it butts the centre.</summary>
    /// <remarks>LILYPOND-REF: mf/feta-noteheads.mf stem_attachment per head style;
    ///   the stem overlaps the head stencil rather than butting its centre.</remarks>
    private static double StemAttachYOffset(NoteheadStyle style, bool stemUp, int noteValue) => style switch
    {
        NoteheadStyle.Cross or NoteheadStyle.Slash => stemUp ? -0.5 : 0.5,
        NoteheadStyle.Triangle => 0.5,
        _ when noteValue >= 4 => stemUp ? -StemHeadInset : StemHeadInset,
        _ => 0,
    };

    /// <summary>How far a filled round head recesses the stem's start toward the far
    /// end so the join clears the head's slanted corner.</summary>
    private const double StemHeadInset = 0.15;

    /// <summary>
    /// Draws one note at <paramref name="x"/>, which already carries
    /// <paramref name="voiceX"/> — the multi-voice collision shift. Everything the note
    /// column owns rides that shift; its ACCIDENTAL does not, and subtracts it back off to
    /// reach the staff column it was packed against
    /// (<see cref="Svg.Collector.StaffAccidentalColumns"/>).
    /// </summary>
    private static void DrawNote(NoteItem note, double x, double staffMiddleY,
        GrobPropertyResolver resolver, bool isBeamed, bool? forcedStemUp, bool headWiped,
        IDrawingContext gc, double pageHeight, bool dotForceDown = false, double voiceX = 0)
    {
        int noteValue = GlyphMetrics.NoteValueOf(note.BaseDuration);
        double noteY = staffMiddleY + note.StaffPosition / 2.0;
        // A cue grob states font-size −4, so its glyphs are the THIRTEEN design's own outline
        // read at magstep(−4) — EngravingDefaults.CueFontSizeStep / CueScale / CueDesignSize.
        double noteFontSize = note.IsCue ? FontSize * EngravingDefaults.CueScale : FontSize;

        // Voice stem direction override (voice 1 up / voice 2 down); falls back
        // to the note's own position-based default in single-voice staves.
        bool stemUp = forcedStemUp ?? note.StemUp;

        // Accidental (left of notehead). Placed through the SAME single-ape skyline path the
        // spacing reservation uses (AccidentalPlacement.CalculateSinglePosition), so draw =
        // reserve: a natural clears the head by its real right skyline (0.367672), not a fixed
        // AccidentalNoteGap 0.35 (sharp/flat are 0.35 either way). Cue notes scale their
        // accidental with the head (LP CueVoice fontSize = -4 reduces the accidental grob too).
        if (note.Accidental != null)
        {
            double accScale = note.IsCue ? EngravingDefaults.CueScale : 1.0;
            // ⚠️ THE FONT IS THE DESIGN THE FONT-SIZE SELECTS, not the 20 shrunk. It was
            // Design20.Scaled(0.66) until 2026-08-03 — a rounded scale off the wrong table,
            // which is both halves of the same mistake: a cue states font-size −4, that asks
            // for 12.599pt, and 12.599pt lands on the THIRTEEN design, whose glyphs are drawn
            // differently and not merely smaller (Emmentaler is optically sized).
            var cueFont = note.IsCue ? EngravingDefaults.CueFont : null;
            // The packed X is measured from the STAFF COLUMN, so undo the collision shift
            // this note's head took; without a packing there is no other voice on the
            // column and the two frames coincide.
            double? accInkLeft = note.AccidentalX is { } packedX
                ? x - voiceX + packedX
                : AccidentalColumn.CalculateSinglePosition(note, cueFont, cueFont)
                    is { } al ? x + al.XOffset : null;
            if (accInkLeft is { } inkLeft)
                using (note.IsCue ? gc.MusicFace(EngravingDefaults.CueDesignSize) : NullScope.Instance)
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
            using (note.IsCue ? gc.MusicFace(EngravingDefaults.CueDesignSize) : NullScope.Instance)
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
                    double headInk = note.IsCue ? EngravingDefaults.CueScale : 1.0;
                    gc.DrawNotehead(head, x, noteY, noteFontSize, noteheadColor,
                        GlyphMetrics.GetNoteheadBBox(noteValue).Width * headInk,
                        GlyphMetrics.GetNoteheadBBox(noteValue).Height * headInk);
                }
            }

        // Ledger lines are drawn by the staff-measure ledger pre-pass, BEFORE
        // any noteheads (CollectItemLedgers/DrawPlannedLedgers).

        // Stem & flag — beamed notes are handled by DrawBeams (which draws the
        // beam-aware stem to the actual beam Y), so skip both here to avoid a
        // duplicated short stem layered under the beam stem.
        // LILYPOND-REF: lily/stem.cc — beamed stem end determined by beam layout.
        if (noteValue >= 2 && !isBeamed)
        {
            Color? stemColor = ResolveColor(resolver, "Stem");
            // Cue heads are drawn at 0.66×, so the up-stem attaches at the
            // SCALED head's right edge (head width × scale − thick/2), or the
            // stem floats off the small head. Down-stems attach at the head's
            // left edge, which doesn't move with the scale.
            // LILYPOND-REF: lily/stem.cc internal_calc_stem_offset_from_head —
            // the offset comes from the (scaled) head extent.
            double headScale = note.IsCue ? EngravingDefaults.CueScale : 1.0;
            double stemX = x + LayoutUtilities.StemAttachX(stemUp, noteValue, headScale);
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
                note.IsCue ? EngravingDefaults.CueStemDetails : null);
            gc.DrawLine(stemX, noteY - StemAttachYOffset(note.Notehead, stemUp, noteValue),
                stemX, stemEndY,
                stemColor ?? Color.Black, EngravingDefaults.StemThickness);

            bool hasFlag = false;
            if (noteValue >= 8)
            {
                var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
                if (flag.HasValue)
                {
                    // The flag hangs on the stem's RIGHT EDGE — LayoutUtilities.FlagDrawX is the
                    // one house for that term, and it is measured (ledger flag.x.*).
                    gc.DrawGlyph(flag.Value, LayoutUtilities.FlagDrawX(stemX), stemEndY,
                        noteFontSize, stemColor);
                    hasFlag = true;
                }
            }

            if (note.HasTremolo)
                DrawTremolo(stemX, noteY, stemEndY, stemUp, note.TremoloBeams, hasFlag, gc);
        }

        // Augmentation dots: the dot column sits one dot-width right of the
        // head's right edge (per-duration head width — whole/half heads are
        // wider), and successive dots are spaced one dot-width apart.
        // LILYPOND-REF: scm/define-grobs.scm DotColumn —
        //   (padding . dot-column-interface::pad-by-one-dot-width)
        // LILYPOND-REF: scm/output-lib.scm ly:dots::print — stack with
        //   padding = one dot width (advance per dot = 2 dot widths)
        // ⚠️ "The head's right edge" is its INK right, and this line computed the ADVANCE
        // until 2026-08-05 (session 95). LilyPond builds the dot column's base X from the
        // head's grob EXTENT, which is the ink (1.962 / 1.3774 / 1.3042 against advances of
        // 1.960 / 1.376 / 1.304 — dumped in audit/lp-geometry/probes/dynamic-support.ly).
        // LILYPOND-REF: lily/dot-column.cc:82-84 Dot_column::calc_positioning_done — base_x.unite (Stem::first_head (parent_stems[i])->extent (commonx, X_AXIS))
        double dotWidth = GlyphMetrics.AugmentationDot.Width;
        double dotStartX = x + GlyphMetrics.GetNoteheadBBox(noteValue).Right * (note.IsCue ? EngravingDefaults.CueScale : 1.0) + dotWidth;
        if (note.Dots > 0)
        {
            // Same Dot_configuration machinery as chords (for a single dot
            // this reduces to "line notes move to the space above", unless the
            // multi-voice collision forces this down voice's dot below).
            int dotPos = DotConfiguration.Resolve(
                new[] { note.StaffPosition },
                dotForceDown ? new[] { -1 } : null)[0];
            double dotY = staffMiddleY + dotPos / 2.0;
            for (int d = 0; d < note.Dots; d++)
                gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot,
                    dotStartX + d * 2 * dotWidth, dotY, noteFontSize, noteheadColor);
        }
    }

    /// <summary>Draws one chord; <paramref name="voiceX"/> is the collision shift already in
    /// <paramref name="x"/>, which the accidentals subtract back off — see
    /// <see cref="DrawNote"/>.</summary>
    private static void DrawChord(ChordItem chord, double x, double staffMiddleY,
        GrobPropertyResolver resolver, bool isBeamed, bool? forcedStemUp, bool headWiped,
        IDrawingContext gc, double pageHeight, bool dotForceDown = false, double voiceX = 0)
    {
        int noteValue = GlyphMetrics.NoteValueOf(chord.BaseDuration);
        char head = EmmentalerGlyphs.GetNotehead(chord.Notehead, noteValue);
        Color? noteheadColor = ResolveColor(resolver, "NoteHead");
        // LILYPOND-REF: lily/grob-property.cc — NoteHead.transparent
        bool headTransparent = resolver.GetBool("NoteHead", "transparent") == true;
        bool stemUp = forcedStemUp ?? chord.StemUp;

        // Cue chords take the same font-size −4 recipe as cue notes (EngravingDefaults.Cue*).
        double headScale = chord.IsCue ? EngravingDefaults.CueScale : 1.0;
        double noteFontSize = chord.IsCue ? FontSize * EngravingDefaults.CueScale : FontSize;

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
        var cueChordFont = chord.IsCue ? EngravingDefaults.CueFont : null;
        // A chord sharing its column with another voice was packed against that voice's
        // accidentals too, and in the STAFF COLUMN's frame — so those X's undo the collision
        // shift, exactly as the single-note branch does.
        bool packedColumn = chord.Notes.Any(n => n.AccidentalX.HasValue);
        double accOriginX = packedColumn ? x - voiceX : x;
        var accLayouts = packedColumn
            ? chord.Notes
                .Where(n => n.Accidental is not null && n.AccidentalX is not null)
                .Select(n => new AccidentalLayout(
                    n.StaffPosition, n.Accidental!, n.AccidentalX!.Value, n.IsCourtesy))
                .ToImmutableArray()
            : AccidentalColumn.CalculatePositions(
                chord.Notes, headOffsets, cueChordFont, cueChordFont);
        foreach (var al in accLayouts)
        {
            double ay = staffMiddleY + al.StaffPosition / 2.0;
            // Anchor the accidental to its own member's pitch offset so it
            // highlights together with that head (fall back to the chord).
            int accSource = chord.SourcePosition;
            foreach (var n in chord.Notes)
                if (n.StaffPosition == al.StaffPosition && n.SourcePosition >= 0) { accSource = n.SourcePosition; break; }
            using (chord.IsCue ? gc.MusicFace(EngravingDefaults.CueDesignSize) : NullScope.Instance)
                DrawAccidentalAtInkLeft(al.Accidental, al.IsCourtesy,
                    accOriginX + al.XOffset, ay, accSource, gc, headScale);
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

        // Augmentation dots: one dot ROW per chord note, all in one column a
        // dot-width right of the heads. Final positions come from the full
        // Dot_configuration port (badness-scored up/down displacement with
        // cascading; on-line dots forced into spaces).
        // LILYPOND-REF: scm/define-grobs.scm DotColumn padding (one dot width)
        // LILYPOND-REF: lily/dot-configuration.cc; lily/dot-column.cc:194-224.
        // The dot column clears heads reversed to the RIGHT of the stem.
        if (chord.Dots > 0 && chord.Notes.Length > 0)
        {
            // The head's INK right — see the single-note branch for the LilyPond citation.
            double dotWidth = GlyphMetrics.AugmentationDot.Width;
            double dotStartX = x + GlyphMetrics.GetNoteheadBBox(noteValue).Right * headScale
                + Math.Max(0, headOffsets.Max()) + dotWidth;
            var resolved = DotConfiguration.Resolve(
                chord.Notes.Select(n => n.StaffPosition).ToArray(),
                dotForceDown ? Enumerable.Repeat(-1, chord.Notes.Length).ToArray() : null);
            foreach (int p in resolved)
            {
                double dotY = staffMiddleY + p / 2.0;
                for (int d = 0; d < chord.Dots; d++)
                    using (gc.Source(chord.SourcePosition))
                        gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot,
                            dotStartX + d * 2 * dotWidth, dotY, noteFontSize, noteheadColor);
            }
        }

        // Skip chord stem when chord is part of a beam — DrawBeams handles it.
        // LILYPOND-REF: lily/stem.cc — beamed stem end determined by beam layout.
        if (noteValue >= 2 && chord.Notes.Length > 0 && !isBeamed)
        {
            Color? stemColor = ResolveColor(resolver, "Stem");
            // Up-stems attach at the (cue-scaled) head's right edge; see DrawNote.
            double stemX = x + LayoutUtilities.StemAttachX(stemUp, noteValue, headScale);
            // Stem attaches at the far notehead; its length is reckoned from the
            // stem-tip-side notehead (top note for stem-up, bottom for stem-down),
            // following LilyPond's Stem::internal_calc_stem_end_position (stem.cc:481).
            double stemStartY = (stemUp ? bottomY : topY)
                - StemAttachYOffset(chord.Notehead, stemUp, noteValue);
            int stemTipPos = stemUp ? maxPos : minPos;
            int durLog = StemCalculator.GetDurationLog(noteValue);
            // StemCalculator is device; convert at its boundary. Derive device inputs
            // from the Y-up locals — device tip Y = pageHeight − (staff middle + tip
            // pos/2), device staff top = pageHeight − (staff middle + half staff) — as
            // in DrawNote, then flip its device result back to page Y-up.
            double deviceTipY = pageHeight - (staffMiddleY + stemTipPos / 2.0);
            double deviceStaffTop = pageHeight - (staffMiddleY + StaffHeight / 2.0);
            // Cue length-fraction, exactly as in DrawNote.
            double stemEndY = pageHeight - StemCalculator.CalculateStemEndY(
                deviceTipY, stemUp, deviceStaffTop, durLog, stemTipPos,
                chord.IsCue ? EngravingDefaults.CueStemDetails : null);
            gc.DrawLine(stemX, stemStartY, stemX, stemEndY,
                stemColor ?? Color.Black, EngravingDefaults.StemThickness);
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

    private static void DrawRest(RestItem rest, double x, double staffY, IDrawingContext gc)
    {
        int noteValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
        char glyph = EmmentalerGlyphs.GetRest(noteValue);
        // staffY is the top-line Y-up; rest origins sit below it (device down =
        // smaller Y-up).
        // LILYPOND-REF: lily/rest.cc Rest::staff_position_internal — the semibreve
        // (whole) rest is raised one staff line (duration_log==0 returns pos+2)
        // relative to half-note and shorter rests.
        double y = noteValue == 1 ? staffY - 1 : staffY - 2;  // whole rests hang from 4th line
        using (gc.Source(rest.SourcePosition))
            gc.DrawGlyph(glyph, x, y, FontSize);

        // Augmentation dots: one dot-width right of the rest's ink, in the
        // space above the middle line (standard rest-dot position).
        // LILYPOND-REF: lily/dot-column.cc:252-257 — rest dots translate by
        //   the rest extent plus the DotColumn padding (one dot width).
        if (rest.Dots > 0)
        {
            double dotWidth = GlyphMetrics.AugmentationDot.Width;
            double dotStartX = x + GlyphMetrics.GetRestBBox(noteValue).Right + dotWidth;
            double dotY = staffY - 2 + 0.5; // staff position +1 (3rd space)
            for (int d = 0; d < rest.Dots; d++)
                using (gc.Source(rest.SourcePosition))
                    gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot,
                        dotStartX + d * 2 * dotWidth, dotY, FontSize);
        }
    }

}
