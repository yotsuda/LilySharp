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

/// <summary>
/// Tablature engraving for <see cref="SharedRenderer"/>: tab staff lines, fret
/// numbers drawn as note heads, and chord fret-column offsets. Split out of the
/// main SharedRenderer file as a partial class; no behavior change.
/// </summary>
internal static partial class SharedRenderer
{
    // ---------- Tablature ----------

    /// <summary>
    /// Draws a tablature staff: one line per string, the TAB clef, and fret
    /// numbers (on a page-coloured background occluding the string line) instead
    /// of noteheads. Rests draw nothing on a tab staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tab-note-heads-engraver.cc — fret numbers as note heads
    /// LILYPOND-REF: scm/translation-functions.scm determine-frets
    /// </remarks>
    private static void DrawTabStaff(Staff staff, ScoreLayout layout, SystemLayout system,
        int staffIndex,
        double staffY, double staffRight, double systemStartX, double clefGroupInkLeft,
        HashSet<(int Staff, int Voice, int Measure, int Item)> beamedItems,
        HashSet<int> percentCovered, IDrawingContext gc, double pageHeight)
    {
        var tuningType = staff.Tuning ?? TuningType.Guitar;
        int stringCount = Tunings.GetStringCount(tuningType);
        int[] tuning = Tunings.GetTuning(tuningType);
        double stringSpace = EngravingDefaults.TabStringSpace(stringCount);
        // Written→sounding recovery: the clef octave (treble_8) plus the part's
        // resolved transposition (bass = −12) — one value shared with MIDI.
        int octaveShift = Tunings.SoundingShift(staff.TabSourceClef, staff.Transposition);

        // ⚠️ THE STRING LINES ARE DRAWN AT THE END OF THIS METHOD, not here, because each
        // fret digit takes a BITE out of the line it sits on rather than being painted over.
        // Every digit this staff draws appends its span to `digitGaps`; the lines are then
        // emitted as the segments between them. Deferring is what lets the gap be the drawn
        // digit's own span instead of a second computation of it — the "one house" rule that
        // the two spellings of the stem clearance had just broken.
        var digitGaps = new List<(int StringIndex, double Left, double Right)>();

        // Per-measure barlines at the tab staff height.
        var primaryVoice = staff.PrimaryVoice;
        double tabHeight = (stringCount - 1) * stringSpace;

        // Repeat dots straddle the staff centre, each centred in a string space:
        // ±1 line off centre when the centre falls in a space (even line gap), ±½
        // when it falls on a line — matching LilyPond's tab repeat dots.
        double dotCenter = (stringCount - 1) / 2.0;
        double dotOff = ((stringCount - 1) % 2 == 0) ? 0.5 : 1.0;
        (double, double) tabDots = ((dotCenter - dotOff) * stringSpace, (dotCenter + dotOff) * stringSpace);

        // TAB clef (clefs.tab). LilyPond's is an ORDINARY Clef grob -- TabStaff only sets
        // clefGlyph to "clefs.tab" and clefPosition to 0 (LILYPOND-REF
        // ly/engraver-init.ly) -- so it is drawn at the staff's own size, NOT scaled with
        // the staff-space, and centred on the middle line. Dumped on 2.26.0
        // (audit/lp-geometry/probes/line-start-mindist.ly, score CGT): the tab staff spans
        // 7.600000 while the clef spans only 5.760000, the bare LILC height. Lily# scaled
        // it by tabHeight/5.78 so it nearly FILLED the staff, and drew it at systemStartX
        // with no LeftEdge->clef gap at all.
        // The X is the Clef break-align GROUP's anchor, the same one DrawClef uses, which
        // is what lets the TAB clef join the group in SpacingRules.ClefGroupExtent: the
        // width booked there is now the width drawn here.
        double tabCenterY = staffY - tabHeight / 2.0;
        gc.DrawGlyph(EmmentalerGlyphs.TabClef,
            systemStartX + EngravingDefaults.ClefGlyphXOffset - clefGroupInkLeft,
            tabCenterY, FontSize);
        bool lineStart = true;
        foreach (var ml in system.Measures)
        {
            if (ml.MeasureIndex >= primaryVoice.Measures.Length)
                continue;
            var measure = primaryVoice.Measures[ml.MeasureIndex];
            bool atLineStart = lineStart;
            lineStart = false;
            // Line-start start barline clears the redrawn tab clef (see DrawBarlines).
            // Both ends are clickable/highlightable, like the notation staff's.
            if (measure.StartBarline != BarlineType.None)
            {
                double sx = atLineStart ? ml.X + LineStartBarClearance : ml.X;
                using (gc.Source(measure.SourceStart))
                {
                    DrawBarline(measure.StartBarline, sx, staffY, tabHeight, gc, tabDots: tabDots);
                    gc.DrawHitRect(sx - BarlineHitPad, staffY,
                        GetVisualBarlineWidth(measure.StartBarline) + 2 * BarlineHitPad, tabHeight);
                }
            }
            double endX = ml.X + ml.Width;
            double width = GetVisualBarlineWidth(measure.EndBarline);
            if (measure.EndBarline != BarlineType.None)
            {
                using (gc.Source(measure.SourceEnd))
                {
                    DrawBarline(measure.EndBarline, endX - width, staffY, tabHeight, gc, tabDots: tabDots);
                    gc.DrawHitRect(endX - width - BarlineHitPad, staffY,
                        width + 2 * BarlineHitPad, tabHeight);
                }
            }
        }

        bool measuresLineStart = true;
        foreach (var ml in system.Measures)
        {
            bool atMeasuresLineStart = measuresLineStart;
            measuresLineStart = false;
            // A percent-repeat iteration prints ONLY the % sign — the notation staff
            // hides its notes (SharedRenderer.Noteheads), and the tab must hide its fret
            // digits the same way. Their stems/beams are already suppressed, so without
            // this the tab left bare, stemless digits UNDER the % sign.
            if (percentCovered.Contains(ml.MeasureIndex))
                continue;
            for (int vi = 0; vi < staff.Voices.Length; vi++)
            {
                var voice = staff.Voices[vi];
                if (ml.MeasureIndex < voice.Measures.Length)
                    DrawTabMeasure(voice.Measures[ml.MeasureIndex], ml, staffY,
                        tuning, stringCount, octaveShift, staff, staffIndex, vi + 1, beamedItems,
                        gc, digitGaps, pageHeight, atMeasuresLineStart);
            }
        }

        // ⚠️ GRACE DIGITS ARE DRAWN IN A LATER PASS (SharedRenderer.cs, DrawGraceNotes), so
        // their bites have to be booked here or the line would run straight through them.
        // The spans come from TabGraceDigits — the same producer that pass draws from — so
        // the hole and the glyph cannot drift apart.
        foreach (var g in layout.GraceNoteLayouts)
        {
            if (g.Tuning is not { } graceTuning || g.StaffIndex != staffIndex)
                continue;
            if (!system.Measures.Any(m => m.MeasureIndex == g.MeasureIndex))
                continue;
            foreach (var d in TabGraceDigits(g, graceTuning, g.TabClef, g.TabTransposition))
                digitGaps.Add((d.StringNum - 1, d.CenterX - d.Width / 2, d.CenterX + d.Width / 2));
        }

        // LAST, so every digit above has booked its bite out of the line it sits on.
        // One line per string, spaced stringSpace apart, starting at the system indent
        // (systemStartX) like the notation staff — not the page margin, or on the first
        // (indented) system they overrun to the left.
        for (int i = 0; i < stringCount; i++)
            DrawTabStringLine(systemStartX, staffRight, staffY - i * stringSpace, i, digitGaps, gc);
    }

    /// <summary>
    /// One tab string line, drawn as the segments BETWEEN the fret digits sitting on it.
    /// </summary>
    /// <remarks>
    /// The gaps arrive as x spans already booked by the digits that were drawn, so the hole
    /// is the drawn glyph's own extent and not a second computation of it.
    /// <para>
    /// Overlapping spans (a chord's zigzag can put two digits close on one string) merge
    /// naturally: the cursor only ever moves right, so an overlap collapses into one hole
    /// instead of emitting a negative-width segment.
    /// </para>
    /// </remarks>
    private static void DrawTabStringLine(double left, double right, double y, int stringIndex,
        List<(int StringIndex, double Left, double Right)> digitGaps, IDrawingContext gc)
    {
        double x = left;
        foreach (var gap in digitGaps
                     .Where(g => g.StringIndex == stringIndex)
                     .OrderBy(g => g.Left))
        {
            if (gap.Right <= x) continue;              // already behind the cursor
            if (gap.Left > x)
                gc.DrawLine(x, y, Math.Min(gap.Left, right), y,
                    Color.Black, EngravingDefaults.StaffLineThickness);
            x = gap.Right;
            if (x >= right) return;
        }
        if (x < right)
            gc.DrawLine(x, y, right, y, Color.Black, EngravingDefaults.StaffLineThickness);
    }

    private static void DrawTabMeasure(Measure measure, MeasureLayout ml,
        double staffY, int[] tuning, int stringCount, int octaveShift,
        Staff staff, int staffIndex, int voiceNumber,
        HashSet<(int Staff, int Voice, int Measure, int Item)> beamedItems, IDrawingContext gc,
        List<(int StringIndex, double Left, double Right)> digitGaps,
        double pageHeight, bool atLineStart = false)
    {
        // The first sounding item of a line-starting measure is where a tie can have
        // been split by the line break (a tie's two ends are always adjacent, so a
        // target any later in the line has its source on the same line).
        int firstSoundingIndex = -1;
        if (atLineStart)
            for (int j = 0; j < measure.Items.Length; j++)
                if (measure.Items[j] is NoteItem or ChordItem)
                {
                    firstSoundingIndex = j;
                    break;
                }
        bool useColumnTiming = !ml.Columns.IsDefaultOrEmpty && ml.Columns.Length > 0;
        var currentTiming = Fraction.Zero;
        double stringSpace = EngravingDefaults.TabStringSpace(stringCount);
        // A tab stem's direction comes from the STRING (the tab head), not the notated
        // pitch, so a bass run on the bottom strings points up like LilyPond.
        var dirGeom = new TabStaffGeometry(
            staff.Tuning ?? TuningType.Guitar, staffY, staff.TabSourceClef, staff.Transposition);
        // `tab … as numbers`: fret digits only — no stems, dots or rests (beams and
        // tuplet brackets are suppressed at their own draw sites). Ties still print.
        bool numbersOnly = staff.TabNumbersOnly;

        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            double columnX = useColumnTiming
                ? ml.X + ml.GetXForTiming(currentTiming)
                : (i < ml.Items.Length ? ml.X + ml.Items[i].X : ml.X);
            currentTiming += item.Duration;
            // The fret digit sits under the notehead CENTRE; the stem shares the
            // companion notation staff's stem x (notehead edge). See TabHeadCenterOffset.
            double itemX = columnX + EngravingDefaults.TabHeadCenterOffset;

            // Match this position against the beamed set (offset-independent),
            // keyed by voice so each voice's beams suppress only its own flags.
            bool isBeamed = beamedItems.Contains((staffIndex, voiceNumber - 1, ml.MeasureIndex, i));

            switch (item)
            {
                case NoteItem note:
                    // A note below the tab's lowest string can't be fretted — show
                    // nothing (no digit, no stem) rather than a wrong open string.
                    if (note.TabBelowRange)
                        break;
                    // A tie's destination keeps its rhythm (stem/beam) but hides
                    // its fret number — the held string is not re-struck. When the
                    // tie was SPLIT by a line break (the target opens the line), or
                    // the note carries \repeatTie, the fret prints in PARENTHESES
                    // instead: the reader re-entering mid-hold needs the number.
                    // LILYPOND-REF: scm/tablature.scm:186-224 tab-note-head::handle-ties
                    //   — tied + at-line-begin? or repeat-tied? → 'parenthesized #t;
                    //   plain tied → 'transparent. (The span-start branch — a slur or
                    //   glissando starting at the tie's end — is not wired here.)
                    if (!note.IsTieTarget && !note.HasRepeatTie)
                        DrawTabNote(note.Midi, itemX, staffY,
                            tuning, note.StringNumber, octaveShift, stringSpace, note.SourcePosition,
                            numbersOnly ? 0 : note.Dots, gc, digitGaps, note.IsDead);
                    else if (note.HasRepeatTie || (note.IsTieTarget && i == firstSoundingIndex))
                        DrawTabNote(note.Midi, itemX, staffY,
                            tuning, note.StringNumber, octaveShift, stringSpace, note.SourcePosition,
                            numbersOnly ? 0 : note.Dots, gc, digitGaps, note.IsDead,
                            parenthesized: true);
                    if (!numbersOnly)
                        DrawUnbeamedTabStem(note, note.BaseDuration, dirGeom.StringStemUp(dirGeom.MeanString(note)),
                            columnX, staffY, staff, isBeamed, gc, pageHeight);
                    break;
                case ChordItem chord:
                    DrawTabChord(chord, itemX, staffY, tuning, octaveShift, stringSpace, gc, digitGaps);
                    if (!numbersOnly)
                        DrawUnbeamedTabStem(chord, chord.BaseDuration, dirGeom.StringStemUp(dirGeom.MeanString(chord)),
                            columnX, staffY, staff, isBeamed, gc, pageHeight);
                    break;
                case RestItem rest when !numbersOnly:
                    // In \tabFullNotation whole and half rests ATTACH to a staff line, just
                    // like on the notation staff — a bar floating in a gap looks wrong. The
                    // central inter-line space holds both: the HALF rest SITS ON the lower
                    // central line (body above), the WHOLE rest HANGS FROM the upper one
                    // (body below). Shorter rests (glyphs that span a line) stay centred on
                    // the tab's vertical middle. `staffY + k*stringSpace` is the k-th line.
                    int restValue = LilySharp.Core.Svg.Layout.GlyphMetrics.NoteValueOf(rest.BaseDuration);
                    // staffY is Y-up; lines below it are at smaller Y-up, and DrawRest's
                    // own +1/+2 origin offsets are now −1/−2, so the synthetic origins
                    // passed here flip sign to match.
                    double lowerCentralLineY = staffY - (stringCount / 2) * stringSpace;
                    // dotOffset null: a tab rest keeps the solo default — the dot column
                    // is a notation-staff device and no tab book has bound one.
                    // Position 0 is what these three calls pass for the ledger question,
                    // and it is the true answer rather than a stand-in: the ledgered cut
                    // of a rest glyph is for one that lands OFF a staff line, and the two
                    // that could take it are seated ON a tab line right here by
                    // construction (the half on the lower central line, the whole hanging
                    // from the upper). Shorter rests never carry a ledger at any position.
                    // LILYPOND-REF: lily/rest.cc:170-185 Rest::glyph_name is_ledgered.
                    const double onATabLine = 0.0;
                    if (restValue == 2)      // half: DrawRest origin (its bottom) at staffY−2
                        DrawRest(rest, itemX, lowerCentralLineY + 2.0, null, gc, onATabLine);
                    else if (restValue == 1) // whole: DrawRest origin (its top) at staffY−1
                        DrawRest(rest, itemX, lowerCentralLineY + stringSpace + 1.0, null, gc, onATabLine);
                    else
                    {
                        var restBBox = LilySharp.Core.Svg.Layout.GlyphMetrics.GetRestBBox(restValue);
                        double tabMiddle = staffY - (stringCount - 1) * stringSpace / 2.0;
                        double restOriginY = tabMiddle - (restBBox.Top + restBBox.Bottom) / 2.0;
                        DrawRest(rest, itemX, restOriginY + 2.0, null, gc, onATabLine);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Stem (and flag for eighths and shorter) for a tab note that is NOT part of
    /// a beam group — beamed notes get their stem from <see cref="DrawBeams"/>.
    /// The stem sits at the companion notation staff's stem x (its notehead edge,
    /// via StemUpAttachX/StemDownAttachX from the note column) so the two staves'
    /// stems line up on one vertical; the fret digit, centred a
    /// <see cref="EngravingDefaults.TabHeadCenterOffset"/> to the right of the
    /// column, still catches the stem. Whole notes carry no stem.
    /// </summary>
    private static void DrawUnbeamedTabStem(MusicItem item, Fraction baseDuration,
        bool stemUp, double columnX, double staffY, Staff staff,
        bool isBeamed, IDrawingContext gc, double pageHeight)
    {
        int noteValue = baseDuration.Denominator;
        if (baseDuration.Numerator != 1) noteValue = 1;
        if (noteValue < 2 || isBeamed)
            return; // whole notes have no stem; beamed notes are drawn elsewhere.

        // The length is TabStaffGeometry's, not this method's — the articulation engraver
        // has to place scripts clear of this stem, and a second spelling here is exactly
        // what let its non-beamed branch have no stem term at all.
        double stringSpace = EngravingDefaults.TabStringSpace(
            Tunings.GetStringCount(staff.Tuning ?? TuningType.Guitar));
        double stemLength = TabConstants.UnbeamedStemLength(stringSpace);
        // The axis the digits are placed around — see TabStemX for why it is not the
        // companion notation stem's x any more.
        double stemX = TabStemX(columnX);
        // TabStemHeadY works in device coordinates; round-trip through it, then lift
        // both stem ends to the page Y-up frame.
        double nearYDev = TabStemHeadY(item, stemUp, pageHeight - staffY, staff);
        double farYDev = nearYDev + (stemUp ? -stemLength : stemLength);
        double nearY = pageHeight - nearYDev;
        double farY = pageHeight - farYDev;

        if (noteValue == 2)
        {
            // Half note: a DOUBLE stem distinguishes it from a quarter on a tab
            // staff, where the fret number carries no notehead shape to read the
            // duration from.
            // LILYPOND-REF: scm/tablature.scm:97-111
            // tabvoice::make-double-stem-width-for-half-notes — LilyPond widens the Stem's
            // X-extent to `single-stem-width + separation` and centres it, so the two lines'
            // centres end up `separation` apart; `double-stem-separation` defaults to 0.5
            // (the :107 fallback). Measured on 2.26.0: stem-line centres exactly 0.5 apart
            // (tablature-double-stem-tremolo twin, 17.44/17.94).
            const double halfGap = EngravingDefaults.TabDoubleStemSeparation / 2;
            gc.DrawLine(stemX - halfGap, nearY, stemX - halfGap, farY, Color.Black, EngravingDefaults.StemThickness);
            gc.DrawLine(stemX + halfGap, nearY, stemX + halfGap, farY, Color.Black, EngravingDefaults.StemThickness);
        }
        else
        {
            gc.DrawLine(stemX, nearY, stemX, farY, Color.Black, EngravingDefaults.StemThickness);
        }

        // The tremolo is the Stem's grob on a tab staff exactly as on a notation
        // staff — stem-tremolo.cc has no staff-kind branch, so the one DrawTremolo
        // serves both; the stack centres on the stem's X, which the double stem
        // straddles symmetrically (make-double-stem-width-for-half-notes widens
        // the extent about its centre). This call was MISSING outright: a tab
        // tremolo (`a2:32` under \tabFullNotation) drew its (double) stem with no
        // slashes — the tab twin of the chord path's once-missing call.
        // LILYPOND-REF: lily/stem-tremolo.cc — geometry reads only the stem/flag.
        int tremoloBeams = item switch
        {
            NoteItem n => n.TremoloBeams,
            ChordItem c => c.TremoloBeams,
            _ => 0,
        };
        if (tremoloBeams > 0)
            DrawTremolo(stemX, farY, stemUp, noteValue, tremoloBeams,
                hasFlag: noteValue >= 8, gc: gc);

        if (noteValue >= 8)
        {
            var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
            if (flag.HasValue)
                // Same term as a notation staff's: the Flag grob and its rule do not change
                // with the staff kind (LayoutUtilities.FlagDrawX).
                gc.DrawGlyph(flag.Value, LayoutUtilities.FlagDrawX(stemX), farY, FontSize, null);
        }
    }

    // Tab fret numbers are drawn a notch larger than the historical 1.6 so they
    // read clearly; the chord-collision shifts below keep the bigger digits from
    // overlapping. Background/clearance dimensions scale with this.
    // Single source: TabConstants (shared with the tie/grace layout so they can't desync).
    private const double TabFretFontSize = TabConstants.FretFontSize;

    /// <summary>Grace fret digits relative to the normal fret size — just slightly
    /// smaller, so the grace reads as a grace without becoming illegible.</summary>
    private const double TabGraceFretScale = TabConstants.GraceFretScale;

    /// <summary>
    /// Half-extents left and right of a tab note/chord's column X, spanning its
    /// fret digits INCLUDING the chord zigzag column offsets. The spacing engine
    /// prices this into the shared note columns so adjacent tab digits do not
    /// overprint. Tab fret numbers are a Lily# enlargement (LilyPond's are tiny
    /// and unspaced), so this width has no LilyPond analogue — it is designed on
    /// the "digits must not overlap" principle that also drives the zigzag itself.
    /// <para>
    /// ⚠️ THE GLYPH'S OWN REACH, WITH NO PADDING IN IT. Clear air between neighbouring
    /// columns belongs to the gap BETWEEN them
    /// (<see cref="LilySharp.Core.Svg.Layout.TabConstants.FretColumnGap"/>, applied in
    /// SpacingRules), not to the column's extent: an extent is also what the FIRST note of a
    /// line is placed from, and padding folded in here pushed
    /// <c>line-start.time-to-first-note.tab-*</c> off LilyPond by 0.023550 — a measured point,
    /// moved by a quantity that has nothing to do with a neighbour that is not there.
    /// </para>
    /// </summary>
    internal static (double Left, double Right) TabItemHalfExtent(
        MusicItem item, int[] tuning, int octaveShift)
    {
        switch (item)
        {
            case NoteItem n:
            {
                var (_, fret) = Tunings.CalculateFret(
                    n.Midi + octaveShift, tuning, n.StringNumber ?? 0);
                double half = TabChordColumns.FretWidth(fret) / 2;
                return (half, half);
            }
            case ChordItem c when c.Notes.Length > 0:
            {
                var notes = Tunings.CalculateChordFrets(
                        c.Notes.Select(cn => (cn.Midi + octaveShift, cn.StringNumber)).ToList(), tuning)
                    .Select(p => (str: p.stringNum, fret: p.fret))
                    .OrderBy(p => p.str)
                    .ToList();
                double[] dx = TabChordColumns.Offsets(notes);
                double left = 0, right = 0;
                for (int i = 0; i < notes.Count; i++)
                {
                    double half = TabChordColumns.FretWidth(notes[i].fret) / 2;
                    left = Math.Max(left, -dx[i] + half);
                    right = Math.Max(right, dx[i] + half);
                }
                return (left, right);
            }
            default:
                return (0, 0);
        }
    }

    private static void DrawTabNote(int midi,
        double x, double staffY, int[] tuning, int? stringNumber, int octaveShift,
        double stringSpace, int sourcePosition, int dots, IDrawingContext gc,
        List<(int StringIndex, double Left, double Right)> digitGaps, bool isDead = false,
        bool parenthesized = false)
    {
        int midiPitch = midi + octaveShift;
        var (stringNum, fret) = Tunings.CalculateFret(midiPitch, tuning, stringNumber ?? 0);
        DrawTabFret(fret, stringNum, x, staffY, stringSpace, sourcePosition, gc, digitGaps, isDead,
            parenthesized);
        double noteY = staffY - (stringNum - 1) * stringSpace;
        double digitWidth = LilySharp.Core.Svg.Layout.TabConstants.FretGlyphWidth(
            isDead ? "×" : fret.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TabFretFontSize);
        DrawTabAugmentationDots(dots, x, digitWidth, noteY, stringSpace, sourcePosition, gc);
    }

    /// <summary>
    /// Draws a note/chord-row's augmentation dots to the RIGHT of its fret digit,
    /// nudged into the space ABOVE the string line (never centred on the line, where
    /// they overprint it). The fret number sits on a line, so — exactly like a
    /// notehead on a staff line — its dot moves off the line by one position (half a
    /// staff space); LilyPond's Dot_configuration prefers the UP direction for that
    /// move, so the dot lands above the digit. The tab fret number carries no
    /// notehead, so the dot is the only dotted-value cue; the stem/flag show the base
    /// duration.
    /// LILYPOND-REF: lily/dot-configuration.cc Dot_configuration::badness — on-line
    ///   dots displaced by one position, UP preferred.
    /// </summary>
    private static void DrawTabAugmentationDots(int dots, double digitCenterX,
        double digitWidth, double noteY, double stringSpace, int sourcePosition, IDrawingContext gc)
    {
        if (dots <= 0) return;
        double dotWidth = GlyphMetrics.AugmentationDot.Width;
        double dotStartX = digitCenterX + digitWidth / 2 + dotWidth;
        // One position off the line = half a staff space; on a tab the staff space is
        // the string gap. UP is larger Y-up.
        double dotY = noteY + stringSpace / 2;
        using (gc.Source(sourcePosition))
            for (int d = 0; d < dots; d++)
                gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot,
                    dotStartX + d * 2 * dotWidth, dotY, FontSize, null);
    }

    /// <summary>
    /// Draws one fret number (with its string-line-occluding background) at the
    /// given string line and x. Chord notes share this after their x is shifted.
    /// </summary>
    private static void DrawTabFret(int fret, int stringNum, double x, double staffY,
        double stringSpace, int sourcePosition, IDrawingContext gc,
        List<(int StringIndex, double Left, double Right)> digitGaps, bool isDead = false,
        bool parenthesized = false)
    {
        // String 1 (highest pitch) is the TOP tab line; string N the bottom
        // (device down = smaller Y-up).
        double noteY = staffY - (stringNum - 1) * stringSpace;
        // A dead (muted) note shows an "×" in place of the fret number.
        string fretText = isDead ? "×" : fret.ToString();
        double bgWidth = LilySharp.Core.Svg.Layout.TabConstants.FretGlyphWidth(
            isDead ? "×" : fret.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TabFretFontSize);

        // The string line is BROKEN around the digit rather than painted over: the span is
        // booked here and DrawTabStringLines emits the segments either side of it.
        // ⚠️ THE OLD FORM WAS AN OPAQUE BOX, and it had two faults an occluder always has.
        // It depended on a COLOUR — it was the only opaque light element in the document, so
        // a viewer that themes a page by inverting it turned the box black and every fret
        // number sat in a hole (fixed once by giving the page a background to match, but only
        // ever as a match). And it was as tall as the DIGIT, which at any size past about
        // 2.08 is taller than the 1.5 string gap, so it blanked pieces of the NEIGHBOURING
        // lines too — the ceiling that stopped the digits growing. A gap has neither
        // problem: it spends no colour, and it is confined to the digit's own line.
        double gapPad = parenthesized ? TabTieParenClearance : 0;
        digitGaps.Add((stringNum - 1, x - bgWidth / 2 - gapPad, x + bgWidth / 2 + gapPad));

        using (gc.Source(sourcePosition))
        {
            // Bold so the fret numbers read clearly over the string lines. The baseline is
            // asked of the FACE so the glyph's ink lands centred on the line — a digit and a
            // dead note's × are different shapes and a shared fraction cannot centre both.
            gc.DrawText(fretText, x,
                noteY - LilySharp.Core.Svg.Layout.TabConstants.FretBaselineDrop(
                    fretText, TabFretFontSize),
                TabFretFontSize, "serif",
                FontStyle.Bold, TextAnchor.Middle, Color.Black);
            if (parenthesized)
                DrawTabFretParens(x, noteY, bgWidth, gc);
        }
    }

    // A split/repeat-tied fret's parentheses, sized to the digit box. LilyPond builds
    // them as two filled bow stencils; the bow's page-frame geometry reduces to: the
    // outer edge bulges 4/3 × width from the spine, the inner edge one half-thickness
    // less, and the control points sit 0.1 + 0.3 × angularity of the way along the
    // span — with the TabNoteHead's declared width 0.25 / half-thickness 0.075 /
    // angularity 0.4 that is a 0.3333 outer, 0.2583 inner bulge and 0.22/0.78 stops
    // (all three measured intact on the tabtie-probe twin). Each bow is stroked 0.1
    // with round caps.
    // ⚠️ MEASURED, NOT DERIVED: the spine stands one full line-width (0.1) outside
    //   the digit ink — LP's between-parens span is 1.0877 for a 0.8877 digit, a
    //   0.1000 seam per side (tabtie-probe twin). Deriving it from combine-at-edge
    //   padding 0 + the widened bow extent predicts 0.05; the digit stencil LP
    //   attaches to is evidently wider than its ink by the other half. The
    //   measurement wins; the seam rides Lily#'s own (larger) digit metrics.
    // LILYPOND-REF: scm/stencil.scm:33-114 make-bow-stencil — outer-control =
    //   4/3 · bow-height / length; inner-control = |outer| − thickness/length;
    //   left-control = 0.1 + 0.3 · angularity.
    // LILYPOND-REF: scm/stencil.scm:182-213 make-parenthesis-stencil — the bow pair
    //   over the stencil's Y extent, line-width 0.1.
    // LILYPOND-REF: scm/define-grobs.scm:3721-3735 tab-note-head::print — details
    //   cautionary-properties: (angularity . 0.4) (half-thickness . 0.075)
    //   (padding . 0) (width . 0.25).
    private const double TabTieParenWidth = 0.25;
    private const double TabTieParenHalfThickness = 0.075;
    private const double TabTieParenAngularity = 0.4;
    private const double TabTieParenLineWidth = 0.1;
    /// <summary>How far a paren's ink can reach past the digit ink on each side —
    /// the string-line gap widens by this much: the measured spine seam, the outer
    /// bulge, and the stroke's outward half.</summary>
    private const double TabTieParenClearance =
        TabTieParenLineWidth + 4.0 / 3 * TabTieParenWidth + TabTieParenLineWidth / 2;

    private static void DrawTabFretParens(double x, double noteY, double digitWidth,
        IDrawingContext gc)
    {
        double h = LilySharp.Core.Svg.Layout.TabConstants.FretDigitHeight / 2;
        double control = 0.1 + 0.3 * TabTieParenAngularity;      // 0.22
        double outer = 4.0 / 3 * TabTieParenWidth;               // 0.3333
        double inner = outer - TabTieParenHalfThickness;         // 0.2583
        double c1Y = noteY + h - control * 2 * h;
        double c2Y = noteY - h + control * 2 * h;
        foreach (int dir in stackalloc int[] { -1, 1 })
        {
            double spineX = x + dir * (digitWidth / 2 + TabTieParenLineWidth);
            gc.DrawClosedBezier(
                (spineX, noteY + h),
                (spineX + dir * outer, c1Y),
                (spineX + dir * outer, c2Y),
                (spineX, noteY - h),
                (spineX + dir * inner, c2Y),
                (spineX + dir * inner, c1Y),
                Color.Black, TabTieParenLineWidth);
        }
    }

    /// <summary>
    /// Draws a chord's fret numbers, shifting their x so the (now larger) digits on
    /// neighbouring strings do not overlap — the tab analogue of notehead-collision
    /// resolution. Two-note chords put the SMALLER fret on the left; three-or-more
    /// zigzag between two columns (rather than slanting) so the stack stays compact.
    /// </summary>
    private static void DrawTabChord(ChordItem chord, double itemX, double staffY,
        int[] tuning, int octaveShift, double stringSpace, IDrawingContext gc,
        List<(int StringIndex, double Left, double Right)> digitGaps)
    {
        // LP-style exclusive allocation: each chord note gets its OWN string
        // (assigned strings first, then highest pitch → highest free string),
        // ordered top string (1) → bottom for the offset pass.
        // LILYPOND-REF: scm/translation-functions.scm determine-frets-and-strings.
        var notes = Tunings.CalculateChordFrets(
                chord.Notes.Select(cn => (cn.Midi + octaveShift, cn.StringNumber)).ToList(),
                tuning)
            .Select(p => (str: p.stringNum, fret: p.fret))
            .OrderBy(p => p.str)
            .ToList();

        double[] dx = TabChordColumns.Offsets(notes);
        for (int i = 0; i < notes.Count; i++)
            DrawTabFret(notes[i].fret, notes[i].str, itemX + dx[i], staffY, stringSpace,
                chord.SourcePosition, gc, digitGaps);

        // Augmentation dots sit to the right of the whole chord (its rightmost digit
        // edge), one per string row — the tab analogue of the notation dot column.
        // The helper puts dots a dot-width past digitCenterX + digitWidth/2, so pass
        // itemX as the centre and 2*(rightEdge - itemX) as the width to land them
        // just past the chord's rightmost digit.
        if (chord.Dots > 0)
        {
            double rightEdge = itemX;
            for (int i = 0; i < notes.Count; i++)
                rightEdge = Math.Max(rightEdge, itemX + dx[i] + TabChordColumns.FretWidth(notes[i].fret) / 2);
            double alignWidth = 2 * (rightEdge - itemX);
            foreach (var (str, _) in notes)
                DrawTabAugmentationDots(chord.Dots, itemX, alignWidth,
                    staffY - (str - 1) * stringSpace, stringSpace, chord.SourcePosition, gc);
        }
    }

    /// <summary>
    /// The x a tab stem stands at: the AXIS the fret digits are placed around — the note
    /// column's digit centre. A chord's zigzag straddles it, exactly as a notation
    /// chord's staggered noteheads straddle their stem.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE STEM IS WHAT SAYS THE DIGITS ARE ONE EVENT. A chord's fret numbers are
    /// scattered over several string lines and, at this digit size, over two columns as
    /// well; the only mark tying them into a single sounding is the stem they share. A
    /// stem that leaves from the axis they are placed around is read as belonging to all
    /// of them. One that leaves from an edge, or from one column, is read as belonging to
    /// what it touches — and the chord stops looking simultaneous (user, 2026-08-16).
    /// <para>
    /// It used to be the companion NOTATION staff's stem x instead — the note column's
    /// notehead EDGE, half a black notehead from the digit centre, and on the left or the
    /// right according to the stem's direction. Two things were wrong with that.
    /// </para>
    /// <para>
    /// ⚠️ ⑴ IT DID NOT BUY WHAT IT WAS FOR. The reason written down for it was that the
    /// tab stem should stand on the same vertical as the notation stem in a two-staff
    /// score. That only holds when both staves choose the SAME stem direction, and a tab
    /// stem's direction comes from the strings while the notation stem's comes from the
    /// pitch. MEASURED where they differ (one voice, low note, guitar): notation stem
    /// 9.66, tab stem 8.48 — 1.18 apart, where centring would have left 0.59.
    /// USER DECISION (2026-08-16): give the alignment up. The large fret digits stay
    /// (they are what makes a tab readable on a stand), and so does the zigzag they force.
    /// </para>
    /// <para>
    /// ⚠️ ⑵ ON A CHORD IT CAME FROM A DIFFERENT DIGIT THAN THE Y DID. <c>TabStemHeadY</c>
    /// takes the y from the digit at the END of the stack in the stem's direction; the x
    /// took no digit at all. When the digits zigzag into two columns those can be
    /// different columns, and then the stem hangs off empty space. MEASURED on
    /// <c>&lt;e a d' g'&gt;</c> (guitar): digits at 5.50 / 7.41 / 5.50 / 7.41, stem x
    /// 5.87, y taken from the bottom digit — which is in the 7.41 column, 1.54 away.
    /// </para>
    /// <para>
    /// The axis is the answer to both, and it is the notation convention rather than a new
    /// idea: a second in a chord shifts one head to the other side of the stem, and the
    /// stem stays on the boundary the two columns share. MEASURED in Lily# itself
    /// (<c>&lt;c d&gt;</c>): heads at 8.59 and 9.82, stem at 9.82 — the shared edge, not
    /// either head's centre. The tab digits sit <c>±(widest digit/2 + 0.1)</c> from the
    /// axis, so the stem passes between them with that 0.1 to spare on each side.
    /// </para>
    /// <para>
    /// AND IT IS LILYPOND'S RULE, not a new idea — the attachment is written there as a
    /// literal zero: <c>Note_head::calc_tab_stem_attachment</c> answers
    /// <c>Offset (0.0, dir * 1.35)</c>, so a tab stem stands on its head's X CENTRE and
    /// only the Y depends on the direction. (Measured before that line was read, which is
    /// what sent me to it: 2.26.0 puts the stem at 9.214093543307087 = head origin 8.62 +
    /// ink centre 0.594, identical for <c>dir=1</c> and <c>dir=-1</c>.)
    /// LILYPOND-REF: lily/note-head.cc:221-234 Note_head::calc_tab_stem_attachment;
    /// LILYPOND-REF: scm/define-grobs.scm:3741 ly:note-head::calc-tab-stem-attachment
    /// — TabNoteHead reaches the callback above through that property, not a literal pair.
    /// </para>
    /// <para>
    /// ⚠️ LILYSHARP-OWN, the other half: which x IS the centre when the digits do not
    /// share one. LilyPond never faces the question — its digits are small enough to
    /// stack on a single x — while Lily# writes them large enough to read from a stand
    /// and must zigzag them into two columns (HANDOFF §3, 2026-07-24). The axis the
    /// zigzag is built around is this quantity, and it is the one LilyPond's zero means:
    /// the x the heads are placed about. It goes away if the digits ever stop needing
    /// two columns. Observed by TabStemAxisTests.
    /// </para>
    /// </remarks>
    internal static double TabStemX(double columnX) =>
        columnX + EngravingDefaults.TabHeadCenterOffset;

}
