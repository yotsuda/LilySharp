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
    /// numbers (with white backgrounds occluding the string line) instead of
    /// noteheads. Rests draw nothing on a tab staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tab-note-heads-engraver.cc — fret numbers as note heads
    /// LILYPOND-REF: scm/translation-functions.scm determine-frets
    /// </remarks>
    private static void DrawTabStaff(Staff staff, SystemLayout system, int staffIndex,
        double staffY, double staffRight, double systemStartX,
        HashSet<(int Staff, int Voice, int Measure, int Item)> beamedItems, IDrawingContext gc)
    {
        var tuningType = staff.Tuning ?? TuningType.Guitar;
        int stringCount = Tunings.GetStringCount(tuningType);
        int[] tuning = Tunings.GetTuning(tuningType);
        double stringSpace = EngravingDefaults.TabStringSpace(stringCount);
        // Written→sounding recovery: treble_8 (guitar) and bass tunings are
        // notated an octave above where they sound.
        int octaveShift = Tunings.OctaveShift(tuningType, staff.TabSourceClef);

        // One staff line per string, spaced stringSpace apart. Lines start at the
        // system indent (systemStartX), like the notation staff — not the page
        // margin, or on the first (indented) system they overrun to the left.
        for (int i = 0; i < stringCount; i++)
            gc.DrawLine(systemStartX, staffY + i * stringSpace, staffRight, staffY + i * stringSpace,
                Color.Black, EngravingDefaults.StaffLineThickness);

        // Per-measure barlines at the tab staff height.
        var primaryVoice = staff.PrimaryVoice;
        double tabHeight = (stringCount - 1) * stringSpace;

        // Repeat dots straddle the staff centre, each centred in a string space:
        // ±1 line off centre when the centre falls in a space (even line gap), ±½
        // when it falls on a line — matching LilyPond's tab repeat dots.
        double dotCenter = (stringCount - 1) / 2.0;
        double dotOff = ((stringCount - 1) % 2 == 0) ? 0.5 : 1.0;
        (double, double) tabDots = ((dotCenter - dotOff) * stringSpace, (dotCenter + dotOff) * stringSpace);

        // TAB clef (clefs.tab), sized to span the actual staff height (the glyph's
        // designed span is ~5.78 font units) and centered on it.
        double tabCenterY = staffY + tabHeight / 2.0;
        gc.DrawGlyph(EmmentalerGlyphs.TabClef, systemStartX, tabCenterY,
            FontSize * tabHeight / 5.78);
        bool lineStart = true;
        foreach (var ml in system.Measures)
        {
            if (ml.MeasureIndex >= primaryVoice.Measures.Length)
                continue;
            var measure = primaryVoice.Measures[ml.MeasureIndex];
            bool atLineStart = lineStart;
            lineStart = false;
            // Line-start start barline clears the redrawn tab clef (see DrawBarlines).
            if (measure.StartBarline != BarlineType.None)
                DrawBarline(measure.StartBarline,
                    atLineStart ? ml.X + LineStartBarClearance : ml.X, staffY, tabHeight, gc, tabDots: tabDots);
            double endX = ml.X + ml.Width;
            double width = GetVisualBarlineWidth(measure.EndBarline);
            DrawBarline(measure.EndBarline, endX - width, staffY, tabHeight, gc, tabDots: tabDots);
        }

        foreach (var ml in system.Measures)
        {
            for (int vi = 0; vi < staff.Voices.Length; vi++)
            {
                var voice = staff.Voices[vi];
                if (ml.MeasureIndex < voice.Measures.Length)
                    DrawTabMeasure(voice.Measures[ml.MeasureIndex], ml, staffY,
                        tuning, stringCount, octaveShift, staff, staffIndex, vi + 1, beamedItems, gc);
            }
        }
    }

    private static void DrawTabMeasure(Measure measure, MeasureLayout ml,
        double staffY, int[] tuning, int stringCount, int octaveShift,
        Staff staff, int staffIndex, int voiceNumber,
        HashSet<(int Staff, int Voice, int Measure, int Item)> beamedItems, IDrawingContext gc)
    {
        bool useColumnTiming = !ml.Columns.IsDefaultOrEmpty && ml.Columns.Length > 0;
        var currentTiming = Fraction.Zero;
        double stringSpace = EngravingDefaults.TabStringSpace(stringCount);

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
                    // its fret number — the held string is not re-struck.
                    if (!note.IsTieTarget)
                        DrawTabNote(note.Midi, itemX, staffY,
                            tuning, note.StringNumber, octaveShift, stringSpace, note.SourcePosition, gc, note.IsDead);
                    DrawUnbeamedTabStem(note, note.BaseDuration, note.StemUp,
                        columnX, staffY, staff, isBeamed, gc);
                    break;
                case ChordItem chord:
                    DrawTabChord(chord, itemX, staffY, tuning, octaveShift, stringSpace, gc);
                    DrawUnbeamedTabStem(chord, chord.BaseDuration, chord.StemUp,
                        columnX, staffY, staff, isBeamed, gc);
                    break;
                // RestItem: nothing on a tab staff.
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
        bool isBeamed, IDrawingContext gc)
    {
        int noteValue = baseDuration.Denominator;
        if (baseDuration.Numerator != 1) noteValue = 1;
        if (noteValue < 2 || isBeamed)
            return; // whole notes have no stem; beamed notes are drawn elsewhere.

        const double stemLength = 3.0;
        double stemX = columnX + (stemUp ? EngravingDefaults.StemUpAttachX : EngravingDefaults.StemDownAttachX);
        double nearY = TabStemHeadY(item, stemUp, staffY, staff);
        double farY = nearY + (stemUp ? -stemLength : stemLength);

        if (noteValue == 2)
        {
            // Half note: a DOUBLE stem distinguishes it from a quarter on a tab
            // staff, where the fret number carries no notehead shape to read the
            // duration from. The two lines sit 0.355 staff-spaces apart, measured
            // from LilyPond's own tab output. LILYPOND-REF: ly/tablature-init.ly.
            const double halfGap = 0.355 / 2;
            gc.DrawLine(stemX - halfGap, nearY, stemX - halfGap, farY, Color.Black, EngravingDefaults.StemThickness);
            gc.DrawLine(stemX + halfGap, nearY, stemX + halfGap, farY, Color.Black, EngravingDefaults.StemThickness);
        }
        else
        {
            gc.DrawLine(stemX, nearY, stemX, farY, Color.Black, EngravingDefaults.StemThickness);
        }

        if (noteValue >= 8)
        {
            var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
            if (flag.HasValue)
                gc.DrawGlyph(flag.Value, stemX, farY, FontSize, null);
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

    /// <summary>Drawn width of a fret number at <see cref="TabFretFontSize"/>.</summary>
    private static double TabFretWidth(int fret) =>
        (fret.ToString().Length == 1 ? 0.625 : 1.0) * TabFretFontSize;

    private static void DrawTabNote(int midi,
        double x, double staffY, int[] tuning, int? stringNumber, int octaveShift,
        double stringSpace, int sourcePosition, IDrawingContext gc, bool isDead = false)
    {
        int midiPitch = midi + octaveShift;
        var (stringNum, fret) = Tunings.CalculateFret(midiPitch, tuning, stringNumber ?? 0);
        DrawTabFret(fret, stringNum, x, staffY, stringSpace, sourcePosition, gc, isDead);
    }

    /// <summary>
    /// Draws one fret number (with its string-line-occluding background) at the
    /// given string line and x. Chord notes share this after their x is shifted.
    /// </summary>
    private static void DrawTabFret(int fret, int stringNum, double x, double staffY,
        double stringSpace, int sourcePosition, IDrawingContext gc, bool isDead = false)
    {
        // String 1 (highest pitch) is the TOP tab line; string N the bottom.
        double noteY = staffY + (stringNum - 1) * stringSpace;
        // A dead (muted) note shows an "×" in place of the fret number.
        string fretText = isDead ? "×" : fret.ToString();
        double bgWidth = isDead ? 0.7 * TabFretFontSize : TabFretWidth(fret);
        double bgHeight = LilySharp.Core.Svg.Layout.TabConstants.FretDigitHeight;

        using (gc.Source(sourcePosition))
        {
            // White background occludes the string line behind the number.
            gc.DrawRectangle(x - bgWidth / 2, noteY - bgHeight / 2, bgWidth, bgHeight,
                fill: Color.White);
            // Bold so the fret numbers read clearly over the string lines.
            gc.DrawText(fretText, x, noteY + TabFretFontSize * 0.32, TabFretFontSize, "serif",
                FontStyle.Bold, TextAnchor.Middle, Color.Black);
        }
    }

    /// <summary>
    /// Draws a chord's fret numbers, shifting their x so the (now larger) digits on
    /// neighbouring strings do not overlap — the tab analogue of notehead-collision
    /// resolution. Two-note chords put the SMALLER fret on the left; three-or-more
    /// zigzag between two columns (rather than slanting) so the stack stays compact.
    /// </summary>
    private static void DrawTabChord(ChordItem chord, double itemX, double staffY,
        int[] tuning, int octaveShift, double stringSpace, IDrawingContext gc)
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

        double[] dx = AssignTabChordOffsets(notes);
        for (int i = 0; i < notes.Count; i++)
            DrawTabFret(notes[i].fret, notes[i].str, itemX + dx[i], staffY, stringSpace, chord.SourcePosition, gc);
    }

    /// <summary>
    /// Horizontal offset for each chord note (notes ordered top string → bottom) so
    /// digits on ADJACENT strings (which would overlap vertically at the larger font)
    /// are pulled apart into a left and a right column. Two overlapping notes: the
    /// smaller fret goes left. Three or more: zigzag the columns down each run of
    /// adjacent strings; a note with no adjacent neighbour stays centred.
    /// </summary>
    internal static double[] AssignTabChordOffsets(IReadOnlyList<(int str, int fret)> notes)
    {
        int n = notes.Count;
        var off = new double[n];
        if (n < 2) return off;

        // Mark notes that have an adjacent (string ±1) neighbour in the chord.
        var adjacent = new bool[n];
        for (int i = 1; i < n; i++)
            if (notes[i].str == notes[i - 1].str + 1)
                adjacent[i] = adjacent[i - 1] = true;

        // Column separation: half the widest digit + a small gap, so even two-digit
        // frets in the two columns clear each other.
        double maxWidth = notes.Max(p => TabFretWidth(p.fret));
        double delta = maxWidth / 2 + 0.1;

        if (n == 2 && adjacent[0])
        {
            bool topSmaller = notes[0].fret <= notes[1].fret;
            off[0] = topSmaller ? -delta : delta;
            off[1] = topSmaller ? delta : -delta;
            return off;
        }

        // Three or more: zigzag (left, right, left, …) within each adjacent run.
        int col = 0;
        for (int i = 0; i < n; i++)
        {
            if (!adjacent[i]) { off[i] = 0; continue; }
            col = (i > 0 && adjacent[i - 1] && notes[i].str == notes[i - 1].str + 1) ? 1 - col : 0;
            off[i] = col == 0 ? -delta : delta;
        }
        return off;
    }
}
