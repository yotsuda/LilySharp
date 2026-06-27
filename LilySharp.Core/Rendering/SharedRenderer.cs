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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Rendering;

/// <summary>
/// Backend-agnostic music renderer covering the basic engraving primitives:
/// staff lines, clefs, time/key signatures, noteheads, stems, beams, ledger
/// lines, augmentation dots, and rests. Drives any
/// <see cref="IDocumentContext"/> implementation; the same call produces
/// SVG via <see cref="Rendering.Svg.SvgDocumentContext"/> or PDF via
/// <see cref="Rendering.Pdf.PdfDocumentContext"/>.
/// </summary>
/// <remarks>
/// CANONICAL RENDERER. This is the live render path for SVG, PDF, and PNG
/// (<c>SvgGenerator</c>/<c>PdfGenerator</c>/<c>PngGenerator</c> all call
/// <see cref="RenderTo"/>). It already draws ties, slurs, dynamics,
/// articulations, lyrics, accidentals, hairpins, tuplets, ottava/volta
/// brackets, grace notes, multi-measure rests, and more (see
/// <see cref="RenderTo"/>).
///
/// Barline types (double / final / repeat / repeat-dots), SpanBars across
/// staff groups, multiple voices per staff (with stem-direction defaults and
/// collision offsets), tablature staves, grace slurs, ossia staves, and
/// measured serif text (SerifTextMetrics) are implemented here as well.
///
/// The legacy <c>SvgRenderer</c> was retired and DELETED once parity was
/// reached (recoverable from git history). Beam drawing is staff- and
/// knee-aware; cross-staff beam PRODUCTION (the upstream layout emitting
/// MemberStaffIndices) is the remaining known gap.
/// </remarks>
public static class SharedRenderer
{
    private const double StaffHeight = 4.0;
    private const double FontSize = 4.0;
    private const double OssiaScale = 0.65;  // LP magnifyStaff default for ossia

    public static void RenderTo(
        MultiStaffScore score, ScoreLayout layout, IDocumentContext doc)
    {
        var options = layout.Options;
        var resolver = layout.GrobPropertyResolver;
        // Items participating in a beam — DrawNote/DrawChord skip stem & flag for these,
        // because DrawBeams will draw the beam-aware stem instead. Mirrors SvgRenderer's
        // _beamedStemEndYs gating (lily/stem.cc — beamed stem end is computed by beam layout).
        var beamedItems = BuildBeamedItemsSet(layout);
        foreach (var page in layout.Pages)
        {
            var gc = doc.BeginPage(page.Width, page.Height);
            // LILYPOND-REF: lily/page-layout-problem.cc:434 — header at MarginTop;
            // SystemLayout.Y already includes MarginTop, so apply MarginLeft only.
            DrawHeader(score, page, options, gc);
            var marginScope = options.MarginLeft != 0
                ? gc.BeginGroup(DrawingTransform.Translate(options.MarginLeft, 0))
                : null;
            try
            {
                foreach (var system in page.Systems)
                    DrawSystem(score, layout, system, resolver, beamedItems, gc);
                // Page-level overlays that span systems
                var measureToSystemY = BuildMeasureToSystemY(layout);
                DrawTies(layout, gc);
                DrawSlurs(layout, gc);
                DrawDynamics(layout, measureToSystemY, gc);
                DrawArticulations(layout, measureToSystemY, gc);
                DrawLyrics(layout, measureToSystemY, gc);
                DrawHairpins(layout, measureToSystemY, gc);
                DrawOttavaBrackets(layout, measureToSystemY, gc);
                DrawVoltaBrackets(layout, measureToSystemY, gc);
                DrawTupletBrackets(layout, measureToSystemY, gc);
                DrawTrillSpanners(layout, measureToSystemY, gc);
                DrawGlissandos(layout, gc);
                DrawArpeggios(layout, gc);
                DrawGraceNotes(layout, measureToSystemY, gc);
                DrawChordNames(layout, measureToSystemY, gc);
                DrawFiguredBass(layout, measureToSystemY, gc);
                DrawPercentRepeats(layout, measureToSystemY, gc);
                DrawBarNumbers(layout, measureToSystemY, gc);
                DrawStanzaNumbers(layout, measureToSystemY, gc);
                DrawFingerings(layout, measureToSystemY, gc);
                DrawMusicMarks(layout, measureToSystemY, gc);
                DrawCustomTexts(layout, measureToSystemY, gc);
                DrawTextSpanners(layout, measureToSystemY, gc);
                DrawPedalBrackets(layout, measureToSystemY, gc);
                DrawMultiMeasureRests(layout, gc);
                DrawTieVariants(layout, measureToSystemY, gc);
                DrawLyricHyphens(layout, measureToSystemY, gc);
                DrawPartCombine(layout, measureToSystemY, gc);
            }
            finally
            {
                marginScope?.Dispose();
            }
            doc.EndPage();
        }
    }

    // ---------- Header ----------

    // LILYPOND-REF: ly/titling-init.ly:79-108 — \huge \larger \larger \bold ≈ 3.49 ss
    // LILYPOND-REF: ly/titling-init.ly:100 — composer baseline ≈ 2.2 ss
    private const double TitleFontSize = 3.49;
    private const double ComposerFontSize = 2.2;

    private static void DrawHeader(
        MultiStaffScore score, PageLayout page, LayoutOptions options, IDrawingContext gc)
    {
        double y = options.MarginTop;
        if (score.Title is { } title)
        {
            double centerX = page.Width / 2;
            using (SourceScope(gc, score.Header.Title))
                gc.DrawText(title, centerX, y, TitleFontSize, "serif",
                    FontStyle.Bold, TextAnchor.Middle);
            y += TitleFontSize;
        }
        if (score.Composer is { } composer)
        {
            double rightX = page.Width - options.MarginLeft;
            using (SourceScope(gc, score.Header.Composer))
                gc.DrawText(composer, rightX, y, ComposerFontSize, "serif",
                    FontStyle.Italic, TextAnchor.End);
        }
    }

    /// <summary>Opens a data-pos source scope for a header grob, or a no-op scope
    /// when the offset is unknown (0). Lets the title/composer/time/key carry a
    /// data-pos for click-to-jump without sprinkling null checks at each draw.</summary>
    private static IDisposable SourceScope(IDrawingContext gc, int sourcePosition)
        => sourcePosition > 0 ? gc.Source(sourcePosition) : NullScope.Instance;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    // ---------- System ----------

    private static HashSet<MusicItem> BuildBeamedItemsSet(ScoreLayout layout)
    {
        // Records use value equality; SourcePosition uniquely identifies each
        // note/chord across the score, so default equality is correct here.
        var set = new HashSet<MusicItem>();
        foreach (var beam in layout.BeamLayouts)
            foreach (var member in beam.Group.Members)
                set.Add(member.Item);
        return set;
    }

    private static void DrawSystem(
        MultiStaffScore score, ScoreLayout layout,
        SystemLayout system, GrobPropertyResolver resolver,
        HashSet<MusicItem> beamedItems, IDrawingContext gc)
    {
        bool isFirstSystem = system.SystemIndex == 0;
        double systemStartX = system.Indent;

        // System-start delimiters (brackets / bar lines connecting staves in a group).
        DrawSystemStartDelimiters(system, gc);

        // Instrument names within the indent area (drawn before staves so glyphs
        // overlap correctly when names are wider than the indent).
        DrawInstrumentNames(system, gc);

        // Staff lines end exactly at the final barline (the last measure's right
        // edge), so the staff never overshoots a ragged system nor falls short of a
        // justified one. (system.Width is the target width, not the drawn content.)
        double staffRight = system.Measures.Length > 0
            ? system.Measures[^1].X + system.Measures[^1].Width
            : system.Width;

        // Left-edge system bar + span bars through grand-staff gaps.
        DrawStaffConnectors(score, layout, system, systemStartX, gc);

        // Per-staff: staff lines + prefix glyphs + notes
        foreach (var (group, staff, globalIdx) in score.EnumerateStaves())
        {
            double staffY = LayoutUtilities.FindStaffYInSystem(system, globalIdx);
            bool isOssia = staff.IsOssia;

            IDisposable? groupScope = isOssia
                ? gc.BeginGroup(new DrawingTransform(0, staffY, OssiaScale, OssiaScale))
                : null;
            try
            {
                double localStaffY = isOssia ? 0 : staffY;

                // Tablature staves: string lines + TAB clef + fret numbers.
                if (staff.IsTab)
                {
                    DrawTabStaff(staff, system, localStaffY, staffRight, systemStartX, beamedItems, gc);
                    continue;
                }

                DrawStaffLines(localStaffY, staffRight, gc);

                // System-start prefix. The clef and key signature repeat at the
                // head of EVERY system (standard notation); the key reflects any
                // mid-piece change in force at this point. The time signature is
                // printed only at the very start — a mid-piece meter change is
                // drawn as a measure item, not a system prefix.
                // LILYPOND-REF: lily/break-align-engraver.cc — Clef + KeySignature
                // are break-aligned at every line start; TimeSignature is not.
                double prefixEndX = systemStartX;
                var clef = ResolveClef(staff, system, score);
                // Tag the clef with its declaration for click-to-jump, on the first
                // line of a single-staff score: there it IS the declared clef (later
                // lines may show a mid-piece change, which owns its own position),
                // and a multi-staff score's per-staff clefs would all wrongly point
                // at the one score-level position.
                int clefPos = isFirstSystem && score.TotalStaffCount == 1 ? score.Header.Clef : 0;
                using (SourceScope(gc, clefPos))
                    prefixEndX = DrawClef(clef, systemStartX, localStaffY, gc);
                var activeKey = ResolveKeySignature(staff, system, score);
                // Tag the key sig with its declaration on the first line only — there
                // it IS the declared key; later lines may show a mid-piece change,
                // which carries its own position via its measure item.
                using (SourceScope(gc, isFirstSystem ? score.Header.Key : 0))
                    prefixEndX = DrawKeySignature(activeKey, clef, prefixEndX, localStaffY, gc);
                if (isFirstSystem)
                {
                    using (SourceScope(gc, score.Header.Time))
                        prefixEndX = DrawTimeSignature(score.TimeSignature, prefixEndX, localStaffY, gc);
                }
                else if (GetSystemStartTimeChange(staff, system) is { } startTimeChange)
                {
                    // A meter change at the line break is part of the prefix.
                    prefixEndX = DrawTimeSignature(startTimeChange.NewTime, prefixEndX, localStaffY, gc);
                }

                // Notes per measure — render every voice (voice 1 = stems up,
                // voice 2 = stems down, with collision offsets / head wipes).
                var voices = staff.Voices;
                bool multiVoice = voices.Length > 1;
                for (int vi = 0; vi < voices.Length; vi++)
                {
                    int voiceNumber = vi + 1;
                    bool? forcedStemUp = multiVoice
                        ? VoiceDefaults.GetDefaultStemUp(voiceNumber)
                        : null;
                    DrawStaffMeasures(voices[vi], voiceNumber, forcedStemUp,
                        system, layout, localStaffY, clef, resolver, beamedItems, gc);
                }

                // Barlines (typed: single / double / final / repeat) per measure
                DrawBarlines(system, staff, localStaffY, layout, gc);
            }
            finally
            {
                groupScope?.Dispose();
            }
        }

        // Beams (use system-wide coordinates; ossia beams are rare and
        // outside the Phase 2-A scope so we draw at full scale)
        DrawBeams(score, layout, system, gc);

        // SpanBars: connect barlines across the staves of each multi-staff group.
        DrawSpanBars(score, system, gc);
    }

    /// <summary>
    /// Draws barlines spanning the full height of each multi-staff group, so the
    /// per-staff barlines read as one continuous barline across the group.
    /// Repeat dots stay per-staff (drawn by <see cref="DrawBarlines"/>).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/span-bar.cc — SpanBar across a connected group.</remarks>
    private static void DrawSpanBars(MultiStaffScore score, SystemLayout system, IDrawingContext gc)
    {
        if (system.StaffGroups.IsDefaultOrEmpty) return;

        for (int gi = 0; gi < system.StaffGroups.Length && gi < score.StaffGroups.Length; gi++)
        {
            // Only connected, multi-staff groups (those with a delimiter) get a span bar.
            if (system.StaffGroups[gi].GrandStaffLayout is not { } delim) continue;

            double top = system.Y + delim.BraceTop;
            double height = delim.BraceBottom - delim.BraceTop;
            if (height <= StaffHeight + 0.001) continue; // single staff — nothing to span

            // Barline types are a measure property shared by all staves in the group.
            var voice = score.StaffGroups[gi].Staves[0].PrimaryVoice;
            foreach (var ml in system.Measures)
            {
                if (ml.MeasureIndex >= voice.Measures.Length) continue;
                var measure = voice.Measures[ml.MeasureIndex];

                if (measure.StartBarline != BarlineType.None)
                    DrawBarline(measure.StartBarline, ml.X, top, height, gc, withDots: false);

                double endX = ml.X + ml.Width;
                DrawBarline(measure.EndBarline, endX - GetVisualBarlineWidth(measure.EndBarline),
                    top, height, gc, withDots: false);
            }
        }
    }

    private static void DrawStaffLines(double staffY, double width, IDrawingContext gc)
    {
        for (int i = 0; i < 5; i++)
        {
            double y = staffY + i;
            gc.DrawLine(0, y, width, y, Color.Black, EngravingDefaults.StaffLineThickness);
        }
    }

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
    private static void DrawTabStaff(Staff staff, SystemLayout system,
        double staffY, double staffRight, double systemStartX,
        HashSet<MusicItem> beamedItems, IDrawingContext gc)
    {
        var tuningType = staff.Tuning ?? TuningType.Guitar;
        int stringCount = Tunings.GetStringCount(tuningType);
        int[] tuning = Tunings.GetTuning(tuningType);
        // Bass guitar sounds 8vb relative to its bass-clef notation, so its tab
        // frets come from the written pitch shifted down an octave.
        int octaveShift = Tunings.OctaveShift(tuningType);

        // One staff line per string.
        for (int i = 0; i < stringCount; i++)
            gc.DrawLine(0, staffY + i, staffRight, staffY + i,
                Color.Black, EngravingDefaults.StaffLineThickness);

        // TAB clef (clefs.tab) centered on the staff. The glyph is designed
        // for 6-string staves; it overflows gracefully on fewer strings.
        double tabCenterY = staffY + (stringCount - 1) / 2.0;
        gc.DrawGlyph(EmmentalerGlyphs.TabClef, systemStartX, tabCenterY,
            FontSize * (5.0 / 5.78));

        // Per-measure barlines at the tab staff height (stringCount−1 spaces).
        var primaryVoice = staff.PrimaryVoice;
        double tabHeight = stringCount - 1;
        foreach (var ml in system.Measures)
        {
            if (ml.MeasureIndex >= primaryVoice.Measures.Length)
                continue;
            var measure = primaryVoice.Measures[ml.MeasureIndex];
            if (measure.StartBarline != BarlineType.None)
                DrawBarline(measure.StartBarline, ml.X, staffY, tabHeight, gc);
            double endX = ml.X + ml.Width;
            double width = GetVisualBarlineWidth(measure.EndBarline);
            DrawBarline(measure.EndBarline, endX - width, staffY, tabHeight, gc);
        }

        foreach (var ml in system.Measures)
        {
            foreach (var voice in staff.Voices)
            {
                if (ml.MeasureIndex < voice.Measures.Length)
                    DrawTabMeasure(voice.Measures[ml.MeasureIndex], ml, staffY,
                        tuning, stringCount, octaveShift, staff, beamedItems, gc);
            }
        }
    }

    private static void DrawTabMeasure(Measure measure, MeasureLayout ml,
        double staffY, int[] tuning, int stringCount, int octaveShift,
        Staff staff, HashSet<MusicItem> beamedItems, IDrawingContext gc)
    {
        bool useColumnTiming = !ml.Columns.IsDefaultOrEmpty && ml.Columns.Length > 0;
        var currentTiming = Fraction.Zero;

        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            double itemX = useColumnTiming
                ? ml.X + ml.GetXForTiming(currentTiming)
                : (i < ml.Items.Length ? ml.X + ml.Items[i].X : ml.X);
            currentTiming += item.Duration;

            switch (item)
            {
                case NoteItem note:
                    // A tie's destination keeps its rhythm (stem/beam) but hides
                    // its fret number — the held string is not re-struck.
                    if (!note.IsTieTarget)
                        DrawTabNote(note.Midi, itemX, staffY,
                            tuning, note.StringNumber, octaveShift, note.SourcePosition, gc);
                    DrawUnbeamedTabStem(note, note.BaseDuration, note.StemUp,
                        itemX, staffY, staff, beamedItems, gc);
                    break;
                case ChordItem chord:
                    DrawTabChord(chord, itemX, staffY, tuning, octaveShift, gc);
                    DrawUnbeamedTabStem(chord, chord.BaseDuration, chord.StemUp,
                        itemX, staffY, staff, beamedItems, gc);
                    break;
                // RestItem: nothing on a tab staff.
            }
        }
    }

    /// <summary>
    /// Stem (and flag for eighths and shorter) for a tab note that is NOT part of
    /// a beam group — beamed notes get their stem from <see cref="DrawBeams"/>.
    /// The stem rises from the fret number's centre (its string line, with a gap)
    /// in the note's notation stem direction, so it stays parallel to the stem on
    /// the companion notation staff. Whole notes carry no stem.
    /// </summary>
    private static void DrawUnbeamedTabStem(MusicItem item, Fraction baseDuration,
        bool stemUp, double itemX, double staffY, Staff staff,
        HashSet<MusicItem> beamedItems, IDrawingContext gc)
    {
        int noteValue = baseDuration.Denominator;
        if (baseDuration.Numerator != 1) noteValue = 1;
        if (noteValue < 2 || beamedItems.Contains(item))
            return; // whole notes have no stem; beamed notes are drawn elsewhere.

        const double stemLength = 3.0;
        double nearY = TabStemHeadY(item, stemUp, staffY, staff);
        double farY = nearY + (stemUp ? -stemLength : stemLength);

        if (noteValue == 2)
        {
            // Half note: a DOUBLE stem distinguishes it from a quarter on a tab
            // staff, where the fret number carries no notehead shape to read the
            // duration from. The two lines sit 0.355 staff-spaces apart, measured
            // from LilyPond's own tab output. LILYPOND-REF: ly/tablature-init.ly.
            const double halfGap = 0.355 / 2;
            gc.DrawLine(itemX - halfGap, nearY, itemX - halfGap, farY, Color.Black, EngravingDefaults.StemThickness);
            gc.DrawLine(itemX + halfGap, nearY, itemX + halfGap, farY, Color.Black, EngravingDefaults.StemThickness);
        }
        else
        {
            gc.DrawLine(itemX, nearY, itemX, farY, Color.Black, EngravingDefaults.StemThickness);
        }

        if (noteValue >= 8)
        {
            var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
            if (flag.HasValue)
                gc.DrawGlyph(flag.Value, itemX, farY, FontSize, null);
        }
    }

    // Tab fret numbers are drawn a notch larger than the historical 1.6 so they
    // read clearly; the chord-collision shifts below keep the bigger digits from
    // overlapping. Background/clearance dimensions scale with this.
    private const double TabFretFontSize = 2.0;

    /// <summary>Drawn width of a fret number at <see cref="TabFretFontSize"/>.</summary>
    private static double TabFretWidth(int fret) =>
        (fret.ToString().Length == 1 ? 0.625 : 1.0) * TabFretFontSize;

    private static void DrawTabNote(int midi,
        double x, double staffY, int[] tuning, int? stringNumber, int octaveShift,
        int sourcePosition, IDrawingContext gc)
    {
        int midiPitch = midi + octaveShift;
        var (stringNum, fret) = Tunings.CalculateFret(midiPitch, tuning, stringNumber ?? 0);
        DrawTabFret(fret, stringNum, x, staffY, sourcePosition, gc);
    }

    /// <summary>
    /// Draws one fret number (with its string-line-occluding background) at the
    /// given string line and x. Chord notes share this after their x is shifted.
    /// </summary>
    private static void DrawTabFret(int fret, int stringNum, double x, double staffY,
        int sourcePosition, IDrawingContext gc)
    {
        // String 1 (highest pitch) is the TOP tab line; string N the bottom.
        double noteY = staffY + (stringNum - 1);
        string fretText = fret.ToString();
        double bgWidth = TabFretWidth(fret);
        double bgHeight = 0.6875 * TabFretFontSize;

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
        int[] tuning, int octaveShift, IDrawingContext gc)
    {
        // Resolve (string, fret) per note and order top string (1) → bottom.
        var notes = chord.Notes
            .Select(cn =>
            {
                var (str, fret) = Tunings.CalculateFret(cn.Midi + octaveShift, tuning, cn.StringNumber ?? 0);
                return (str, fret);
            })
            .OrderBy(p => p.str)
            .ToList();

        double[] dx = AssignTabChordOffsets(notes);
        for (int i = 0; i < notes.Count; i++)
            DrawTabFret(notes[i].fret, notes[i].str, itemX + dx[i], staffY, chord.SourcePosition, gc);
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

    /// <summary>
    /// Converts a staff position and accidental back to a MIDI note number
    /// (staff position 0 = middle C = MIDI 60).
    /// </summary>
    private static int StaffPositionToMidi(int staffPosition, string? accidental)
    {
        int step = ((staffPosition % 7) + 7) % 7;
        int octave = 4 + (staffPosition - step) / 7;

        int semitone = step switch
        {
            0 => 0,  // C
            1 => 2,  // D
            2 => 4,  // E
            3 => 5,  // F
            4 => 7,  // G
            5 => 9,  // A
            6 => 11, // B
            _ => 0
        };

        int alteration = accidental switch
        {
            "sharp" => 1,
            "flat" => -1,
            "doubleSharp" => 2,
            "doubleFlat" => -2,
            _ => 0
        };

        return (octave + 1) * 12 + semitone + alteration;
    }

    // ---------- Clef ----------

    /// <summary>
    /// Resolves the active clef at the start of a system by walking previous
    /// measures' ClefChangeItems. Mirrors SvgRenderer.GetActiveClefStringForSystem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/clef-engraver.cc — clef at system start reflects last clef change.
    /// </remarks>
    private static ClefType ResolveClef(Staff staff, SystemLayout system, MultiStaffScore score)
    {
        if (system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return staff.Clef;

        var voice = staff.PrimaryVoice;
        int firstMeasureIndex = system.Measures[0].MeasureIndex;
        var activeClef = staff.Clef;

        // Apply clef changes accumulated in earlier measures.
        for (int m = 0; m < firstMeasureIndex && m < voice.Measures.Length; m++)
        {
            foreach (var item in voice.Measures[m].Items)
            {
                if (item is ClefChangeItem cc)
                    activeClef = cc.NewClef;
            }
        }

        // Leading ClefChangeItems in this system's first measure also surface as
        // system-start clefs (LP groups them with the prefix).
        if (firstMeasureIndex < voice.Measures.Length)
        {
            foreach (var item in voice.Measures[firstMeasureIndex].Items)
            {
                if (item is ClefChangeItem cc)
                    activeClef = cc.NewClef;
                else if (item.Duration > Fraction.Zero)
                    break;
            }
        }

        return activeClef;
    }

    /// <summary>
    /// Resolves the active key signature at the start of a system by walking
    /// previous measures' KeySignatureChangeItems. Mirrors ResolveClef so the
    /// key signature repeated at each system head reflects any mid-piece change,
    /// not the initial key.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/key-engraver.cc — the key signature is reprinted at
    /// every line start, showing the meter in force at that point.
    /// </remarks>
    private static KeySignature ResolveKeySignature(Staff staff, SystemLayout system, MultiStaffScore score)
    {
        // A transposed part in a multi-staff score carries its own key; concert
        // staves fall back to the score key.
        var initialKey = staff.PerStaffKeySignature ?? score.KeySignature;

        if (system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return initialKey;

        var voice = staff.PrimaryVoice;
        int firstMeasureIndex = system.Measures[0].MeasureIndex;
        var activeKey = initialKey;

        // Apply key changes accumulated in measures BEFORE this system. A change
        // that lands inside this system is drawn in place as a measure item, so
        // it is not folded into the system-start prefix here.
        for (int m = 0; m < firstMeasureIndex && m < voice.Measures.Length; m++)
        {
            foreach (var item in voice.Measures[m].Items)
            {
                if (item is KeySignatureChangeItem kc)
                    activeKey = kc.NewKey;
            }
        }

        return activeKey;
    }

    /// <summary>
    /// Returns the time-signature change that OPENS the first measure of a
    /// (non-first) system, or null. Such a change lands exactly at the line
    /// break and is drawn in the system-start prefix (clef, key, THEN time),
    /// like LilyPond — not as a measure item hanging left of the first note.
    /// </summary>
    private static TimeSignatureChangeItem? GetSystemStartTimeChange(Staff staff, SystemLayout system)
    {
        if (system.SystemIndex == 0 || system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return null;

        var voice = staff.PrimaryVoice;
        int firstMeasureIndex = system.Measures[0].MeasureIndex;
        if (firstMeasureIndex >= voice.Measures.Length)
            return null;

        foreach (var item in voice.Measures[firstMeasureIndex].Items)
        {
            if (item is TimeSignatureChangeItem tc)
                return tc;
            if (item.Duration > Fraction.Zero)
                break; // a note/rest before any change → not a measure-opening meter change
        }
        return null;
    }

    private static double DrawClef(ClefType clef, double x, double staffY, IDrawingContext gc)
    {
        char glyph = clef switch
        {
            ClefType.Bass => EmmentalerGlyphs.FClef,
            ClefType.Alto => EmmentalerGlyphs.CClef,
            ClefType.Tenor => EmmentalerGlyphs.CClef,
            _ => EmmentalerGlyphs.GClef,
        };
        // Y baseline matches LP positioning (treble: G line, bass: F line, etc.)
        double clefY = clef switch
        {
            ClefType.Bass => staffY + 1,
            ClefType.Alto => staffY + 2,
            ClefType.Tenor => staffY + 1,
            _ => staffY + 3,
        };
        gc.DrawGlyph(glyph, x + 0.3, clefY, FontSize);
        return x + 0.3 + 3.0;  // approximate clef width + padding
    }

    // ---------- Time signature ----------

    private static double DrawTimeSignature(TimeSignature ts, double x, double staffY, IDrawingContext gc)
    {
        if (ts.Beats == 4 && ts.BeatType == 4)
        {
            gc.DrawGlyph(EmmentalerGlyphs.TimeSigCommon, x, staffY + 2, FontSize);
            return x + 2.0;
        }
        if (ts.Beats == 2 && ts.BeatType == 2)
        {
            gc.DrawGlyph(EmmentalerGlyphs.TimeSigCutCommon, x, staffY + 2, FontSize);
            return x + 2.0;
        }
        // Stack numerator over denominator, each centered on the staff like
        // LilyPond: numerator centered at staff position +2 (device staffY+1),
        // denominator at -2 (device staffY+3), so the pair is symmetric about
        // the middle line. Unlike the common/cut-common glyphs (vertically
        // centred on their origin), the feta number glyphs are anchored at their
        // BASELINE (= the digit's bottom) and stand ~2 staff-spaces tall, so the
        // DrawGlyph y must be lowered by half the digit height to bring the
        // digit's CENTER onto the target line.
        // LILYPOND-REF: mf/feta-numbers.mf — time-signature numbers sit on the baseline.
        const double digitHalfHeight = 1.0; // feta number glyphs are ~2 ss tall
        var num = ts.Beats.ToString();
        var den = ts.BeatType.ToString();
        double dx = 0;
        for (int i = 0; i < Math.Max(num.Length, den.Length); i++)
        {
            if (i < num.Length)
                gc.DrawGlyph(EmmentalerGlyphs.GetTimeSigDigit(num[i] - '0'),
                    x + dx, staffY + 1 + digitHalfHeight, FontSize);
            if (i < den.Length)
                gc.DrawGlyph(EmmentalerGlyphs.GetTimeSigDigit(den[i] - '0'),
                    x + dx, staffY + 3 + digitHalfHeight, FontSize);
            dx += 1.4;
        }
        return x + dx + 0.4;
    }

    // ---------- Key signature ----------

    // LilyPond key-signature placement tables (indexed by (c0-position mod 7)).
    // LILYPOND-REF: scm/output-lib.scm key-signature-interface::alteration-position;
    // scm/define-grobs.scm sharp-positions / flat-positions.
    private static readonly int[] KeySigSharpPositions = [4, 5, 4, 2, 3, 2, 3];
    private static readonly int[] KeySigFlatPositions = [2, 3, 4, 2, 1, 2, 1];
    // Order of accidentals: sharps F C G D A E B; flats B E A D G C F.
    private static readonly int[] KeySigSharpSteps = [3, 0, 4, 1, 5, 2, 6];
    private static readonly int[] KeySigFlatSteps = [6, 2, 5, 1, 4, 0, 3];

    private static double DrawKeySignature(
        KeySignature key, ClefType clef, double x, double staffY, IDrawingContext gc)
    {
        if (key.Sharps == 0) return x;

        // c0-position: staff position of middle C for each clef (half-spaces from
        // the middle line, + = up). Replaces the old uniform integer clefShift,
        // which placed accidentals on the wrong lines for non-treble clefs.
        int c0Position = clef switch
        {
            ClefType.Bass => 6,
            ClefType.Alto => 0,
            ClefType.Tenor => 2,
            _ => -6, // treble (and treble_8)
        };

        bool isSharps = key.Sharps > 0;
        char glyph = isSharps ? EmmentalerGlyphs.AccidentalSharp : EmmentalerGlyphs.AccidentalFlat;
        int[] positions = isSharps ? KeySigSharpPositions : KeySigFlatPositions;
        int[] steps = isSharps ? KeySigSharpSteps : KeySigFlatSteps;

        int cPos = ((c0Position % 7) + 7) % 7;
        int hi = positions[cPos];
        int n = Math.Min(Math.Abs(key.Sharps), 7);

        double accidentalWidth = GlyphMetrics.GetKeySignatureAccidentalWidth(isSharps);
        for (int i = 0; i < n; i++)
        {
            int step = steps[i];
            // LilyPond: staffPosition = hi - modulo(hi - (c-pos + step), 7).
            int diff = hi - (cPos + step);
            int modDiff = ((diff % 7) + 7) % 7;
            int staffPosition = hi - modDiff;
            double y = staffY + StaffHeight / 2 - staffPosition * 0.5;
            gc.DrawGlyph(glyph, x, y, FontSize);
            x += accidentalWidth;
        }
        return x + 0.4;
    }

    // ---------- Notes & rests per staff ----------

    private static void DrawStaffMeasures(
        Voice voice, int voiceNumber, bool? forcedStemUp,
        SystemLayout system, ScoreLayout layout,
        double staffY, ClefType clef, GrobPropertyResolver resolver,
        HashSet<MusicItem> beamedItems, IDrawingContext gc)
    {
        double staffMiddleY = staffY + StaffHeight / 2;

        // Ledger pre-pass: requests are collected across the whole system so
        // adjacent columns can shorten each other's ledgers
        // (ledger-line-spanner.cc), then drawn BEFORE any noteheads — ledger
        // lines sit on layer 0 with the staff lines, noteheads above them, so
        // a head paints over its own ledger (visible whenever a head is
        // recolored, e.g. an editor selection highlight).
        // LILYPOND-REF: scm/define-grobs.scm LedgerLineSpanner (layer . 0);
        // NoteHead uses the default layer 1.
        var ledgerPlan = new List<LedgerRequest>();
        foreach (var (item, _, _, itemX) in EnumerateStaffItems(voice, voiceNumber, system, layout))
            CollectItemLedgers(item, itemX, staffMiddleY, ledgerPlan);
        DrawPlannedLedgers(ledgerPlan, gc);

        foreach (var (item, ml, itemIdx, itemX) in EnumerateStaffItems(voice, voiceNumber, system, layout))
        {
            // Head-wipe when this voice's notehead merges with another's.
            bool headWiped = layout.IsHeadWiped(ml.MeasureIndex, voiceNumber, itemIdx);

            // LILYPOND-REF: lily/grob-property.cc — apply \override / \revert at this position.
            // Multi-staff scores re-advance the resolver per staff: harmless for ordinary
            // overrides (idempotent), but \once overrides may double-apply across staves.
            if (resolver.HasOverrides)
                resolver.AdvanceTo(ml.MeasureIndex, itemIdx);

            switch (item)
            {
                case NoteItem note:
                    DrawNote(note, itemX, staffMiddleY, resolver, beamedItems.Contains(note), forcedStemUp, headWiped, gc);
                    break;
                case RestItem rest:
                    // Measures inside a multi-measure-rest run get their
                    // symbol from DrawMultiMeasureRests (church rest or
                    // H-bar); drawing the per-measure whole rest too would
                    // double-print. LILYPOND-REF: lily/multi-measure-rest.cc
                    // — the MMR spanner replaces the individual rests.
                    if (!IsMmrCovered(layout, ml.MeasureIndex))
                        DrawRest(rest, itemX, staffY, gc);
                    break;
                case ChordItem chord:
                    DrawChord(chord, itemX, staffMiddleY, resolver, beamedItems.Contains(chord), forcedStemUp, headWiped, gc);
                    break;
                case ClefChangeItem clefChange:
                    DrawClefChange(clefChange, itemX, staffY, gc);
                    break;
                case KeySignatureChangeItem keyChange:
                    DrawKeySignatureChange(keyChange, itemX, staffY, gc);
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
    private static IEnumerable<(MusicItem Item, MeasureLayout Ml, int ItemIdx, double ItemX)>
        EnumerateStaffItems(Voice voice, int voiceNumber, SystemLayout system, ScoreLayout layout)
    {
        foreach (var ml in system.Measures)
        {
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
                if (useColumnTiming)
                {
                    double nextAcc = itemIdx + 1 < measure.Items.Length
                        ? FollowingAccidentalLeftExtent(measure.Items[itemIdx + 1])
                        : 0;
                    if (item is ClefChangeItem cc)
                        itemX -= SpacingRules.GetClefChangeWidth(cc.NewClef)
                            + GlyphMetrics.ClefChangePadding + nextAcc;
                    else if (item is KeySignatureChangeItem kc)
                        itemX -= SpacingRules.GetKeySignatureChangeWidth(kc)
                            + GlyphMetrics.ClefChangePadding + nextAcc;
                    else if (item is TimeSignatureChangeItem tc)
                        itemX -= GlyphMetrics.GetTimeSigWidth(tc.NewTime.Beats, tc.NewTime.BeatType)
                            + GlyphMetrics.ClefChangePadding + nextAcc;
                }

                // Horizontal collision offset for multi-voice columns.
                itemX += layout.GetVoiceOffset(ml.MeasureIndex, voiceNumber, itemIdx);
                yield return (item, ml, itemIdx, itemX);
            }
        }
    }

    /// <summary>
    /// How far the item's accidental(s) reach to the LEFT of its notehead
    /// origin (accidental glyph width + the head gap), or 0 when it has none.
    /// Used to hang a preceding mid-measure clef/key/time change past the
    /// accidental so the two do not overprint.
    /// </summary>
    private static double FollowingAccidentalLeftExtent(MusicItem item)
    {
        static double Ext(string? acc) => acc == null
            ? 0
            : GlyphMetrics.GetAccidentalBBox(acc).Width + GlyphMetrics.AccidentalNoteGap;

        switch (item)
        {
            case NoteItem note:
                return Ext(note.Accidental);
            case ChordItem chord:
                double max = 0;
                foreach (var n in chord.Notes)
                    max = Math.Max(max, Ext(n.Accidental));
                return max;
            default:
                return 0;
        }
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
                int noteValue = note.BaseDuration.Denominator;
                if (note.BaseDuration.Numerator != 1) noteValue = 1;
                double headWidth = GlyphMetrics.GetNoteheadAdvance(noteValue) * (note.IsCue ? 0.66 : 1.0);
                CollectLedgerRequest(ledgerPlan, note.StaffPosition, x, headWidth,
                    staffMiddleY, note.Accidental != null);
                break;
            }
            case ChordItem chord when chord.Notes.Length > 0:
            {
                int noteValue = chord.BaseDuration.Denominator;
                if (chord.BaseDuration.Numerator != 1) noteValue = 1;
                double chordScale = chord.IsCue ? 0.66 : 1.0;
                double headWidth = GlyphMetrics.GetNoteheadAdvance(noteValue) * chordScale;
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

    private static void DrawNote(NoteItem note, double x, double staffMiddleY,
        GrobPropertyResolver resolver, bool isBeamed, bool? forcedStemUp, bool headWiped,
        IDrawingContext gc)
    {
        int noteValue = note.BaseDuration.Denominator;
        if (note.BaseDuration.Numerator != 1) noteValue = 1;
        double noteY = StaffFrame.PositionToDevice(note.StaffPosition, staffMiddleY);
        // Cue notes scale to ~0.66× (LP CueVoice fontSize = -4 → magstep(-4)).
        // LILYPOND-REF: ly/engraver-init.ly CueVoice — fontSize = #-4
        double noteFontSize = note.IsCue ? FontSize * 0.66 : FontSize;

        // Voice stem direction override (voice 1 up / voice 2 down); falls back
        // to the note's own position-based default in single-voice staves.
        bool stemUp = forcedStemUp ?? note.StemUp;

        // Accidental (left of notehead)
        if (note.Accidental != null)
            DrawAccidental(note.Accidental, note.IsCourtesy, x, noteY, note.SourcePosition, gc);

        // Notehead — skipped when this head merges with another voice's (head wipe)
        // or when NoteHead.transparent is overridden.
        // LILYPOND-REF: lily/note-collision.cc:381-407
        // LILYPOND-REF: lily/grob-property.cc — NoteHead.transparent
        Color? noteheadColor = ResolveColor(resolver, "NoteHead");
        bool headTransparent = resolver.GetBool("NoteHead", "transparent") == true;
        char head = EmmentalerGlyphs.GetNotehead(noteValue);
        if (!headWiped && !headTransparent)
            using (gc.Source(note.SourcePosition))
                gc.DrawGlyph(head, x, noteY, noteFontSize, noteheadColor);

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
            double headScale = note.IsCue ? 0.66 : 1.0;
            double upAttach = EngravingDefaults.NoteheadBlackWidth * headScale
                - EngravingDefaults.StemThickness / 2;
            double stemX = stemUp
                ? x + upAttach
                : x + EngravingDefaults.StemDownAttachX;
            // Duration-dependent length + unnatural-direction shortening + the
            // extend-to-center-line rule, faithfully following LilyPond's
            // Stem::internal_calc_stem_end_position (lily/stem.cc:481).
            int durLog = StemCalculator.GetDurationLog(noteValue);
            double staffTopY = staffMiddleY - StaffHeight / 2.0;
            double stemEndY = StemCalculator.CalculateStemEndY(
                noteY, stemUp, staffTopY, durLog, note.StaffPosition);
            gc.DrawLine(stemX, noteY, stemX, stemEndY,
                stemColor ?? Color.Black, EngravingDefaults.StemThickness);

            bool hasFlag = false;
            if (noteValue >= 8)
            {
                var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
                if (flag.HasValue)
                {
                    gc.DrawGlyph(flag.Value, stemX, stemEndY, noteFontSize, stemColor);
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
        double dotWidth = GlyphMetrics.AugmentationDot.Width;
        double dotStartX = x + GlyphMetrics.GetNoteheadAdvance(noteValue) * (note.IsCue ? 0.66 : 1.0) + dotWidth;
        if (note.Dots > 0)
        {
            // Same Dot_configuration machinery as chords (for a single dot
            // this reduces to "line notes move to the space above").
            int dotPos = DotConfiguration.Resolve(new[] { note.StaffPosition })[0];
            double dotY = StaffFrame.PositionToDevice(dotPos, staffMiddleY);
            for (int d = 0; d < note.Dots; d++)
                gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot,
                    dotStartX + d * 2 * dotWidth, dotY, noteFontSize, noteheadColor);
        }
    }

    private static void DrawChord(ChordItem chord, double x, double staffMiddleY,
        GrobPropertyResolver resolver, bool isBeamed, bool? forcedStemUp, bool headWiped,
        IDrawingContext gc)
    {
        int noteValue = chord.BaseDuration.Denominator;
        if (chord.BaseDuration.Numerator != 1) noteValue = 1;
        char head = EmmentalerGlyphs.GetNotehead(noteValue);
        Color? noteheadColor = ResolveColor(resolver, "NoteHead");
        // LILYPOND-REF: lily/grob-property.cc — NoteHead.transparent
        bool headTransparent = resolver.GetBool("NoteHead", "transparent") == true;
        bool stemUp = forcedStemUp ?? chord.StemUp;

        // Cue chords scale like cue notes (LP CueVoice fontSize = -4 ≈ 0.66×).
        // LILYPOND-REF: ly/engraver-init.ly CueVoice — fontSize = #-4
        double headScale = chord.IsCue ? 0.66 : 1.0;
        double noteFontSize = chord.IsCue ? FontSize * 0.66 : FontSize;

        // Within-chord seconds/unisons: reversed heads shift to the far side
        // of the stem. LILYPOND-REF: lily/stem.cc:606-760 calc_positioning_done.
        double[] headOffsets = ChordHeadPositioning.CalculateOffsets(
            chord.Notes, stemUp, noteValue, headScale);

        // Accidentals through the full placement machinery (stagger/skylines),
        // aware of the shifted head ink — drawing each one at the same fixed
        // offset overprints them for seconds (e.g. <fis gis>).
        // LILYPOND-REF: lily/accidental-placement.cc position_apes.
        var accLayouts = AccidentalColumn.CalculatePositions(chord.Notes, headOffsets);
        foreach (var al in accLayouts)
        {
            double ay = StaffFrame.PositionToDevice(al.StaffPosition, staffMiddleY);
            DrawAccidentalAtInkLeft(al.Accidental, al.IsCourtesy,
                x + al.XOffset, ay, chord.SourcePosition, gc);
        }

        double topY = double.MaxValue, bottomY = double.MinValue;
        int maxPos = int.MinValue, minPos = int.MaxValue;
        for (int i = 0; i < chord.Notes.Length; i++)
        {
            var n = chord.Notes[i];
            double y = StaffFrame.PositionToDevice(n.StaffPosition, staffMiddleY);
            if (!headWiped && !headTransparent)
                using (gc.Source(chord.SourcePosition))
                    gc.DrawGlyph(head, x + headOffsets[i], y, noteFontSize, noteheadColor);
            if (y < topY) topY = y;
            if (y > bottomY) bottomY = y;
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
            double dotWidth = GlyphMetrics.AugmentationDot.Width;
            double dotStartX = x + GlyphMetrics.GetNoteheadAdvance(noteValue) * headScale
                + Math.Max(0, headOffsets.Max()) + dotWidth;
            var resolved = DotConfiguration.Resolve(
                chord.Notes.Select(n => n.StaffPosition).ToArray());
            foreach (int p in resolved)
            {
                double dotY = StaffFrame.PositionToDevice(p, staffMiddleY);
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
            double chordUpAttach = EngravingDefaults.NoteheadBlackWidth * headScale
                - EngravingDefaults.StemThickness / 2;
            double stemX = stemUp
                ? x + chordUpAttach
                : x + EngravingDefaults.StemDownAttachX;
            // Stem attaches at the far notehead; its length is reckoned from the
            // stem-tip-side notehead (top note for stem-up, bottom for stem-down),
            // following LilyPond's Stem::internal_calc_stem_end_position (stem.cc:481).
            double stemStartY = stemUp ? bottomY : topY;
            int stemTipPos = stemUp ? maxPos : minPos;
            double stemTipNoteY = stemUp ? topY : bottomY;
            int durLog = StemCalculator.GetDurationLog(noteValue);
            double staffTopY = staffMiddleY - StaffHeight / 2.0;
            double stemEndY = StemCalculator.CalculateStemEndY(
                stemTipNoteY, stemUp, staffTopY, durLog, stemTipPos);
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
    /// LILYPOND-REF: scm/output-lib.scm — x11-color mapping
    /// Accepts named colors and #rgb / #rrggbb hex codes.
    /// </remarks>
    private static Color? ResolveColor(GrobPropertyResolver resolver, string grobType)
    {
        if (!resolver.HasOverrides) return null;
        var s = resolver.GetString(grobType, "color");
        if (string.IsNullOrEmpty(s)) return null;
        return ParseColor(s);
    }

    private static Color? ParseColor(string s)
    {
        // Hex literal: #rgb / #rrggbb
        if (s.Length >= 4 && s[0] == '#')
        {
            ReadOnlySpan<char> hex = s.AsSpan(1);
            if (hex.Length == 3 &&
                TryParseHexNibble(hex[0], out int r3) &&
                TryParseHexNibble(hex[1], out int g3) &&
                TryParseHexNibble(hex[2], out int b3))
            {
                return new Color((byte)(r3 * 17), (byte)(g3 * 17), (byte)(b3 * 17));
            }
            if (hex.Length == 6 &&
                TryParseHexByte(hex[0], hex[1], out int r6) &&
                TryParseHexByte(hex[2], hex[3], out int g6) &&
                TryParseHexByte(hex[4], hex[5], out int b6))
            {
                return new Color((byte)r6, (byte)g6, (byte)b6);
            }
            return null;
        }
        // Named color (subset of CSS / X11)
        return s.ToLowerInvariant() switch
        {
            "black" => null,           // default — let backends use their own black
            "red" => new Color(255, 0, 0),
            "green" => new Color(0, 128, 0),
            "blue" => new Color(0, 0, 255),
            "yellow" => new Color(255, 255, 0),
            "cyan" => new Color(0, 255, 255),
            "magenta" => new Color(255, 0, 255),
            "white" => new Color(255, 255, 255),
            "gray" or "grey" => new Color(128, 128, 128),
            "orange" => new Color(255, 165, 0),
            "purple" => new Color(128, 0, 128),
            "brown" => new Color(165, 42, 42),
            _ => null,
        };
    }

    private static bool TryParseHexNibble(char c, out int v)
    {
        if (c >= '0' && c <= '9') { v = c - '0'; return true; }
        if (c >= 'a' && c <= 'f') { v = 10 + c - 'a'; return true; }
        if (c >= 'A' && c <= 'F') { v = 10 + c - 'A'; return true; }
        v = 0; return false;
    }

    private static bool TryParseHexByte(char hi, char lo, out int v)
    {
        v = 0;
        if (!TryParseHexNibble(hi, out int h)) return false;
        if (!TryParseHexNibble(lo, out int l)) return false;
        v = (h << 4) | l;
        return true;
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
    /// <remarks>LILYPOND-REF: lily/ledger-line-spanner.cc:236-248.</remarks>
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
    /// LILYPOND-REF: lily/ledger-line-spanner.cc:251-296 — for adjacent
    /// out-of-staff columns in the same direction, each side's ledger is
    /// clamped to the midpoint between the facing head edges; when BOTH
    /// columns are beyond the first space outside the staff (|pos| ≥ 6, i.e.
    /// both actually carry ledgers) a gap of 0.1 staff spaces is kept between
    /// them so the ledgers never read as one line.
    /// LILYPOND-REF: lily/ledger-line-spanner.cc:330-343 — ledgers of a note
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

                double y = StaffFrame.PositionToDevice(pos, req.StaffMiddleY);
                gc.DrawLine(left, y, req.LedgerRight, y, Color.Black, thickness);
            }
        }
    }

    private static void DrawLedgerLines(int staffPosition, double x, double staffMiddleY,
        IDrawingContext gc, double headWidth = EngravingDefaults.NoteheadBlackWidth)
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

        // Ledger lines above staff (staff position > 4 = above top line)
        for (int pos = 6; pos <= staffPosition; pos += 2)
        {
            double y = StaffFrame.PositionToDevice(pos, staffMiddleY);
            gc.DrawLine(x1, y, x2, y, Color.Black, thickness);
        }
        // Ledger lines below staff (staff position < -4 = below bottom line)
        for (int pos = -6; pos >= staffPosition; pos -= 2)
        {
            double y = StaffFrame.PositionToDevice(pos, staffMiddleY);
            gc.DrawLine(x1, y, x2, y, Color.Black, thickness);
        }
    }

    private static void DrawRest(RestItem rest, double x, double staffY, IDrawingContext gc)
    {
        int noteValue = rest.BaseDuration.Denominator;
        if (rest.BaseDuration.Numerator != 1) noteValue = 1;
        char glyph = EmmentalerGlyphs.GetRest(noteValue);
        double y = noteValue == 1 ? staffY + 1 : staffY + 2;  // whole rests hang from 4th line
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
            double dotY = staffY + 2 - 0.5; // staff position +1 (3rd space)
            for (int d = 0; d < rest.Dots; d++)
                using (gc.Source(rest.SourcePosition))
                    gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot,
                        dotStartX + d * 2 * dotWidth, dotY, FontSize);
        }
    }

    // ---------- Barlines ----------

    private static void DrawBarlines(SystemLayout system, Staff staff, double staffY,
        ScoreLayout layout, IDrawingContext gc)
    {
        var voice = staff.PrimaryVoice;
        foreach (var ml in system.Measures)
        {
            if (ml.MeasureIndex >= voice.Measures.Length)
                continue;
            var measure = voice.Measures[ml.MeasureIndex];

            // Start barline (e.g. repeat-start) at the measure's left edge.
            if (measure.StartBarline != BarlineType.None)
                DrawBarline(measure.StartBarline, ml.X, staffY, StaffHeight, gc);

            // End barline drawn so its right edge sits on the column boundary
            // (matches SvgRenderer: endX - visualWidth). Normal measures carry
            // BarlineType.Single from the collector.
            //
            // Plain barlines INSIDE a multi-measure-rest run are suppressed —
            // the MMR symbol spans the whole run without internal barlines
            // (LILYPOND-REF: lily/multi-measure-rest.cc). Non-Single barlines
            // (double / final / repeat) keep their meaning and stay visible.
            if (measure.EndBarline == BarlineType.Single
                && IsMmrInnerEndBarline(layout, ml.MeasureIndex))
                continue;

            double endX = ml.X + ml.Width;
            double width = GetVisualBarlineWidth(measure.EndBarline);
            DrawBarline(measure.EndBarline, endX - width, staffY, StaffHeight, gc);
        }
    }

    /// <summary>True iff the measure lies inside a multi-measure-rest run.</summary>
    private static bool IsMmrCovered(ScoreLayout layout, int measureIndex)
    {
        if (layout.MultiMeasureRestLayouts.IsDefaultOrEmpty) return false;
        foreach (var mmr in layout.MultiMeasureRestLayouts)
        {
            if (measureIndex >= mmr.StartMeasureIndex &&
                measureIndex < mmr.StartMeasureIndex + mmr.MeasureCount)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True iff the measure's END barline is internal to a multi-measure-rest
    /// run (i.e. the run continues into the next measure).
    /// </summary>
    private static bool IsMmrInnerEndBarline(ScoreLayout layout, int measureIndex)
    {
        if (layout.MultiMeasureRestLayouts.IsDefaultOrEmpty) return false;
        foreach (var mmr in layout.MultiMeasureRestLayouts)
        {
            if (measureIndex >= mmr.StartMeasureIndex &&
                measureIndex < mmr.StartMeasureIndex + mmr.MeasureCount - 1)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Draws a barline of the given type. Mirrors <c>SvgRenderer.DrawBarline</c>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/bar-line.cc — bar-line glyph composition.</remarks>
    private static void DrawBarline(BarlineType type, double x, double staffY, double height,
        IDrawingContext gc, bool withDots = true)
    {
        if (type == BarlineType.None) return;

        double thin = EngravingDefaults.ThinBarlineThickness;
        double thick = EngravingDefaults.ThickBarlineThickness;
        double sep = EngravingDefaults.BarlineSeparation;
        double dotSep = EngravingDefaults.RepeatBarlineDotSeparation;
        double dotsOffset = EngravingDefaults.RepeatDotsOffset;

        switch (type)
        {
            case BarlineType.Single:
                gc.DrawRectangle(x, staffY, thin, height, fill: Color.Black);
                break;

            case BarlineType.Double:
                gc.DrawRectangle(x, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(x + thin + sep, staffY, thin, height, fill: Color.Black);
                break;

            case BarlineType.Final:
                gc.DrawRectangle(x, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(x + thin + sep, staffY, thick, height, fill: Color.Black);
                break;

            case BarlineType.RepeatStart:
                gc.DrawRectangle(x, staffY, thick, height, fill: Color.Black);
                gc.DrawRectangle(x + thick + sep, staffY, thin, height, fill: Color.Black);
                if (withDots) DrawRepeatDots(x + thick + sep + thin + dotSep, staffY, gc);
                break;

            case BarlineType.RepeatEnd:
                if (withDots) DrawRepeatDots(x, staffY, gc);
                double afterDots = x + dotsOffset;
                gc.DrawRectangle(afterDots, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(afterDots + thin + sep, staffY, thick, height, fill: Color.Black);
                break;

            case BarlineType.RepeatBoth:
                if (withDots) DrawRepeatDots(x, staffY, gc);
                double pos = x + dotsOffset;
                gc.DrawRectangle(pos, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(pos + thin + sep, staffY, thick, height, fill: Color.Black);
                gc.DrawRectangle(pos + thin + sep + thick + sep, staffY, thin, height, fill: Color.Black);
                if (withDots) DrawRepeatDots(pos + thin + sep + thick + sep + thin + dotSep, staffY, gc);
                break;
        }
    }

    private static void DrawRepeatDots(double x, double staffY, IDrawingContext gc)
    {
        double r = EngravingDefaults.RepeatDotRadius;
        gc.DrawCircle(x + r, staffY + EngravingDefaults.RepeatDotPosition1, r, Color.Black);
        gc.DrawCircle(x + r, staffY + EngravingDefaults.RepeatDotPosition2, r, Color.Black);
    }

    /// <summary>Total horizontal extent of a barline glyph (for right-edge alignment).</summary>
    private static double GetVisualBarlineWidth(BarlineType type)
    {
        double thin = EngravingDefaults.ThinBarlineThickness;
        double thick = EngravingDefaults.ThickBarlineThickness;
        double sep = EngravingDefaults.BarlineSeparation;
        double dotsOffset = EngravingDefaults.RepeatDotsOffset;
        return type switch
        {
            BarlineType.None => 0,
            BarlineType.Single => thin,
            BarlineType.Double => thin + sep + thin,
            BarlineType.Final => thin + sep + thick,
            BarlineType.RepeatStart => thick + sep + thin + dotsOffset,
            BarlineType.RepeatEnd => dotsOffset + thin + sep + thick,
            BarlineType.RepeatBoth => dotsOffset + thin + sep + thick + sep + thin + dotsOffset,
            _ => thin
        };
    }

    // ---------- Beams ----------

    private static void DrawBeams(MultiStaffScore score, ScoreLayout layout, SystemLayout system, IDrawingContext gc)
    {
        var staffByIndex = score.EnumerateStaves().ToDictionary(s => s.GlobalStaffIndex, s => s.Staff);
        foreach (var beam in layout.BeamLayouts)
        {
            // Only draw beams whose first measure is in this system
            bool inSystem = system.Measures.Any(m => m.MeasureIndex == beam.Group.MeasureIndex);
            if (!inSystem) continue;

            var grp = beam.Group;

            // The quanter's Y positions are staff positions relative to the
            // beam's OWN staff middle — resolve that staff in this system
            // (multi-staff scores; -1 = single staff = the system's first).
            double staffY = beam.StaffIndex >= 0
                ? LayoutUtilities.FindStaffYInSystem(system, beam.StaffIndex)
                : system.Y;
            double staffMiddleY = staffY + StaffHeight / 2;

            // Resolve each member's staff. Cross-staff beams — and the tab
            // mirror of a notation beam — route members to a staff OTHER than
            // the beam's own StaffIndex, so this must be decided per member.
            int MemberStaffIdx(int i) => (!beam.MemberStaffIndices.IsDefaultOrEmpty
                    && i < beam.MemberStaffIndices.Length && beam.MemberStaffIndices[i] >= 0)
                ? beam.MemberStaffIndices[i] : beam.StaffIndex;
            Staff? MemberStaffOf(int i) => MemberStaffIdx(i) is var si && si >= 0
                && staffByIndex.TryGetValue(si, out var s) ? s : null;

            // Per-member stem direction: kneed beams mix up- and down-stems
            // within one group (LILYPOND-REF: beam.cc:894-982 consider_auto_knees),
            // which flips the stem's notehead attachment side.
            bool MemberUp(int i) => grp.IsKnee ? grp.Members[i].MemberStemUp : grp.StemUp;

            // On a tab staff the stem rises from the CENTRE of the fret number
            // (the note column = MemberXPositions), not a notehead edge — so a
            // tab member gets no notehead attachment offset.
            double StemAttachX(int i) => MemberStaffOf(i)?.IsTab == true
                ? beam.MemberXPositions[i]
                : beam.MemberXPositions[i]
                    + (MemberUp(i) ? EngravingDefaults.StemUpAttachX : EngravingDefaults.StemDownAttachX);

            double leftBeamY = StaffFrame.PositionToDevice(beam.LeftY, staffMiddleY);
            double rightBeamY = StaffFrame.PositionToDevice(beam.RightY, staffMiddleY);

            // A tab beam's height can't come from the notation quanter — its Y is
            // in staff positions, not string lines, so mapped onto the tab staff it
            // can land right on the fret numbers and leave stub stems. Instead lay a
            // tab beam HORIZONTAL a fixed distance past the OUTERMOST digit, so each
            // stem's length is set by its string: a low string gets a long stem, a
            // high (open) string a short one — but never a stub.
            bool allTab = grp.Members.Length > 0 && Enumerable.Range(0, grp.Members.Length)
                .All(i => MemberStaffOf(i)?.IsTab == true);
            if (allTab)
            {
                const double tabBeamStem = 3.0; // shortest stem, on the outermost string
                double extreme = grp.StemUp ? double.MaxValue : double.MinValue;
                for (int i = 0; i < grp.Members.Length; i++)
                {
                    double nearY = TabStemHeadY(grp.Members[i].Item, grp.StemUp,
                        LayoutUtilities.FindStaffYInSystem(system, MemberStaffIdx(i)), MemberStaffOf(i)!);
                    extreme = grp.StemUp ? Math.Min(extreme, nearY) : Math.Max(extreme, nearY);
                }
                leftBeamY = rightBeamY = extreme + (grp.StemUp ? -tabBeamStem : tabBeamStem);
            }

            double leftStemX = StemAttachX(0);
            double rightStemX = StemAttachX(grp.Members.Length - 1);

            // Primary beam — drawn as a thick filled rectangle (sloped by polygon)
            DrawBeamSegment(leftStemX, leftBeamY, rightStemX, rightBeamY, gc);

            // Secondary beams (16th+) stack toward the noteheads of the beam's
            // overall direction.
            int maxBeamCount = grp.Members.Max(m => m.BeamCount);
            for (int level = 1; level < maxBeamCount; level++)
            {
                double offset = level * EngravingDefaults.BeamTranslation;
                if (!grp.StemUp) offset = -offset;
                double beamSpanX = rightStemX - leftStemX;

                for (int i = 0; i < grp.Members.Length - 1; i++)
                {
                    if (grp.Members[i].BeamCount > level && grp.Members[i + 1].BeamCount > level)
                    {
                        double xa = StemAttachX(i);
                        double xb = StemAttachX(i + 1);
                        double ta = beamSpanX > 0.001 ? (xa - leftStemX) / beamSpanX : 0;
                        double tb = beamSpanX > 0.001 ? (xb - leftStemX) / beamSpanX : 0;
                        double ya = leftBeamY + offset + ta * (rightBeamY - leftBeamY);
                        double yb = leftBeamY + offset + tb * (rightBeamY - leftBeamY);
                        DrawBeamSegment(xa, ya, xb, yb, gc);
                    }
                }
            }

            // Stems for beam members (replace any individual stems). For knees
            // each stem runs from its OWN notehead (attachment side per member
            // direction) to the shared beam line; for cross-staff members the
            // notehead lives in that member's staff frame.
            double slope = (rightStemX - leftStemX) > 0.001
                ? (rightBeamY - leftBeamY) / (rightStemX - leftStemX) : 0;
            for (int i = 0; i < grp.Members.Length; i++)
            {
                var member = grp.Members[i];
                bool up = MemberUp(i);
                double stemX = StemAttachX(i);
                double beamY = leftBeamY + slope * (stemX - leftStemX);

                int memberStaffIdx = MemberStaffIdx(i);
                Staff? memberStaff = MemberStaffOf(i);

                double headY;
                if (memberStaff?.IsTab == true)
                {
                    // On a tab staff the stem runs from the FRET NUMBER (at its
                    // string line), not a notehead at a staff position. Keep the
                    // stem's X aligned with the notation staff's stem; only the
                    // near end moves to the digit, with a small gap so the stem
                    // never overlaps the number.
                    headY = TabStemHeadY(member.Item, up,
                        LayoutUtilities.FindStaffYInSystem(system, memberStaffIdx), memberStaff);
                }
                else
                {
                    double memberStaffMiddleY = memberStaffIdx >= 0
                        ? LayoutUtilities.FindStaffYInSystem(system, memberStaffIdx) + StaffHeight / 2
                        : staffMiddleY;
                    headY = memberStaffMiddleY - GetMemberStaffPosition(member, up) * 0.5;
                }
                gc.DrawLine(stemX, headY, stemX, beamY,
                    Color.Black, EngravingDefaults.StemThickness);
            }
        }
    }

    /// <summary>
    /// Staff position of the stem's notehead attachment: for chords the head
    /// on the far side from the beam (stem-up beams attach at the bottom head).
    /// </summary>
    private static int GetMemberStaffPosition(BeamMember m, bool stemUp) => m.Item switch
    {
        NoteItem n => n.StaffPosition,
        ChordItem c => stemUp
            ? c.Notes.Min(x => x.StaffPosition)
            : c.Notes.Max(x => x.StaffPosition),
        _ => 0,
    };

    /// <summary>
    /// The Y where a tab-staff stem meets its fret number: the digit's string
    /// line, offset by half the digit height plus a small gap so the stem touches
    /// the number without overlapping it. The stem's X stays aligned with the
    /// notation staff's stem (handled by the caller).
    /// </summary>
    private static double TabStemHeadY(MusicItem item, bool stemUp, double tabStaffTopY, Staff staff)
    {
        var tuningType = staff.Tuning ?? TuningType.Guitar;
        int octaveShift = Tunings.OctaveShift(tuningType);
        int[] tuning = Tunings.GetTuning(tuningType);

        int midi = 0;
        int? stringNumber = null;
        switch (item)
        {
            case NoteItem n:
                midi = n.Midi; stringNumber = n.StringNumber;
                break;
            case ChordItem c when c.Notes.Length > 0:
                // On a tab the digits stack by STRING, so the stem must meet the
                // END of the stack in its direction — the TOP digit (smallest
                // string number) for an up-stem, the BOTTOM for a down-stem. Picking
                // by pitch can start the stem on a middle digit and run it THROUGH
                // the others.
                int StrOf(ChordNoteInfo x) =>
                    Tunings.CalculateFret(x.Midi + octaveShift, tuning, x.StringNumber ?? 0).stringNum;
                var head = stemUp
                    ? c.Notes.OrderBy(StrOf).First()
                    : c.Notes.OrderByDescending(StrOf).First();
                midi = head.Midi; stringNumber = head.StringNumber;
                break;
        }

        var (stringNum, _) = Tunings.CalculateFret(midi + octaveShift, tuning, stringNumber ?? 0);
        double digitY = tabStaffTopY + (stringNum - 1);
        // Half the digit height (0.6875 × font) plus a small gap, so the stem meets
        // the bigger number without overlapping it.
        double clearance = 0.6875 * TabFretFontSize / 2 + 0.3;
        return digitY + (stemUp ? -clearance : clearance);
    }

    private static void DrawBeamSegment(double x1, double y1, double x2, double y2, IDrawingContext gc)
    {
        // Sloped beam as a filled polygon would be ideal; simple thick line is a
        // good Phase 2-A approximation (LP uses precise quad polygons).
        gc.DrawLine(x1, y1, x2, y2, Color.Black, EngravingDefaults.BeamThickness);
    }

    // ---------- Accidentals ----------

    /// <summary>Chord accidental column placement (stagger/skylines).</summary>
    private static readonly AccidentalPlacement AccidentalColumn = new();

    /// <summary>
    /// Draws an accidental (with courtesy parens when set) so its ink LEFT
    /// edge lands at <paramref name="inkLeftX"/> — used for chord accidental
    /// columns whose X comes from <see cref="AccidentalPlacement"/>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/accidental-placement.cc position_apes.</remarks>
    private static void DrawAccidentalAtInkLeft(
        string accidentalKind, bool isCourtesy, double inkLeftX, double noteheadY,
        int sourcePosition, IDrawingContext gc)
    {
        char glyph = accidentalKind switch
        {
            "doubleSharp" => EmmentalerGlyphs.AccidentalDoubleSharp,
            "sharp" => EmmentalerGlyphs.AccidentalSharp,
            "flat" => EmmentalerGlyphs.AccidentalFlat,
            "doubleFlat" => EmmentalerGlyphs.AccidentalDoubleFlat,
            _ => EmmentalerGlyphs.AccidentalNatural,
        };
        var accBBox = GlyphMetrics.GetAccidentalBBox(accidentalKind);

        if (isCourtesy)
        {
            // Same paren assembly as DrawAccidental, anchored at the ink left.
            // LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize()
            var leftParen = GlyphMetrics.AccidentalLeftParen;
            var rightParen = GlyphMetrics.AccidentalRightParen;
            double accInkLeft = inkLeftX + leftParen.Width;
            using (gc.Source(sourcePosition))
            {
                gc.DrawGlyph(EmmentalerGlyphs.AccidentalLeftParen,
                    accInkLeft - leftParen.Right, noteheadY, FontSize);
                gc.DrawGlyph(glyph, accInkLeft - accBBox.Left, noteheadY, FontSize);
                gc.DrawGlyph(EmmentalerGlyphs.AccidentalRightParen,
                    accInkLeft + accBBox.Width - rightParen.Left, noteheadY, FontSize);
            }
        }
        else
        {
            using (gc.Source(sourcePosition))
                gc.DrawGlyph(glyph, inkLeftX - accBBox.Left, noteheadY, FontSize);
        }
    }

    private static void DrawAccidental(
        string accidentalKind, bool isCourtesy, double noteheadX, double noteheadY,
        int sourcePosition, IDrawingContext gc, double scale = 1.0)
    {
        char glyph = accidentalKind switch
        {
            "doubleSharp" => EmmentalerGlyphs.AccidentalDoubleSharp,
            "sharp" => EmmentalerGlyphs.AccidentalSharp,
            "flat" => EmmentalerGlyphs.AccidentalFlat,
            "doubleFlat" => EmmentalerGlyphs.AccidentalDoubleFlat,
            _ => EmmentalerGlyphs.AccidentalNatural,
        };
        // Grace-note accidentals are reduced with the grace head (font-size -3 ≈
        // 0.65); the glyph size AND all the bbox-derived offsets scale together.
        // LILYPOND-REF: scm/music-functions.scm general-grace-settings — grace grobs
        //   inherit the reduced font-size, accidentals included.
        double fs = FontSize * scale;
        var accBBox = GlyphMetrics.GetAccidentalBBox(accidentalKind);
        double accWidth = accBBox.Width * scale;
        double gap = GlyphMetrics.AccidentalNoteGap * scale;

        if (isCourtesy)
        {
            // Parens attach at the accidental's INK edges with zero padding —
            // add_at_edge juxtaposes stencil extents, so positioning must use
            // each glyph's bounding box, not its advance. The paren glyphs are
            // designed for this: leftparen draws BEHIND its origin (ink
            // [-0.60,-0.15], advance 0), so placing it "at" a position and
            // stepping by advance leaves a gap that pushes the accidental
            // against the right paren.
            // LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize()
            var leftParen = GlyphMetrics.AccidentalLeftParen;
            var rightParen = GlyphMetrics.AccidentalRightParen;
            double lpWidth = leftParen.Width * scale;
            double rpWidth = rightParen.Width * scale;
            double totalInk = lpWidth + accWidth + rpWidth;

            // Ink left edge of the whole "(♮)" assembly.
            double inkLeft = noteheadX - gap - totalInk;
            double accInkLeft = inkLeft + lpWidth;
            using (gc.Source(sourcePosition))
            {
                // Each origin is chosen so the glyph's INK lands flush.
                gc.DrawGlyph(EmmentalerGlyphs.AccidentalLeftParen,
                    accInkLeft - leftParen.Right * scale, noteheadY, fs);
                gc.DrawGlyph(glyph, accInkLeft - accBBox.Left * scale, noteheadY, fs);
                gc.DrawGlyph(EmmentalerGlyphs.AccidentalRightParen,
                    accInkLeft + accWidth - rightParen.Left * scale, noteheadY, fs);
            }
        }
        else
        {
            using (gc.Source(sourcePosition))
                gc.DrawGlyph(glyph, noteheadX - accWidth - gap, noteheadY, fs);
        }
    }

    // ---------- Ties & slurs ----------

    private static void DrawTies(ScoreLayout layout, IDrawingContext gc)
    {
        foreach (var tie in layout.TieLayouts)
            DrawCurve(
                tie.StartX, tie.StartY, tie.EndX, tie.EndY,
                tie.Control1, tie.Control2, tie.CurveUp,
                EngravingDefaults.TieMidThickness, gc);
    }

    private static void DrawSlurs(ScoreLayout layout, IDrawingContext gc)
    {
        foreach (var slur in layout.SlurLayouts)
            DrawCurve(
                slur.StartX, slur.StartY, slur.EndX, slur.EndY,
                slur.Control1, slur.Control2, slur.CurveUp,
                EngravingDefaults.SlurMidThickness, gc);
    }

    /// <summary>
    /// Draws a tapered cubic Bézier "bow" (used for both ties and slurs) by
    /// emitting an outer curve from <c>start → c1 c2 → end</c> and an inner
    /// curve back, offset toward the curve interior to create the LP-style
    /// thicker middle / pointed endpoints.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie.cc, lily/slur.cc — Bezier bow rendering
    /// </remarks>
    private static void DrawCurve(
        double startX, double startY, double endX, double endY,
        (double X, double Y) c1, (double X, double Y) c2,
        bool curveUp, double midThickness, IDrawingContext gc)
    {
        double direction = curveUp ? -1.0 : 1.0;
        var c1Back = (X: c1.X, Y: c1.Y + direction * midThickness * 0.9);
        var c2Back = (X: c2.X, Y: c2.Y + direction * midThickness * 0.9);
        gc.DrawClosedBezier(
            (startX, startY), c1, c2,
            (endX, endY), c2Back, c1Back,
            Color.Black);
    }

    // ---------- Helpers for system-Y lookup ----------

    private static Dictionary<int, double> BuildMeasureToSystemY(ScoreLayout layout)
    {
        var map = new Dictionary<int, double>();
        foreach (var system in layout.AllSystems)
            foreach (var ml in system.Measures)
                map[ml.MeasureIndex] = system.Y;
        return map;
    }

    // ---------- Dynamics ----------

    /// <summary>
    /// Draws dynamic markings ("p", "f", "mf", etc.) below the staff using
    /// serif bold-italic text (matching LP's DynamicText grob font).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1298-1327 DynamicText grob
    /// LILYPOND-REF: scm/define-grobs.scm:1311 self-alignment-X = CENTER
    /// </remarks>
    private static void DrawDynamics(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.DynamicLayouts.IsDefaultOrEmpty) return;
        double fontSize = FontSize * 0.5;
        foreach (var d in layout.DynamicLayouts)
        {
            string text = NormalizeDynamicText(d.Text);
            double y = (sysY.TryGetValue(d.MeasureIndex, out var sy) ? sy : 0) + d.Y;
            using (gc.Source(d.SourcePosition))
                gc.DrawText(text, d.X, y, fontSize, "serif",
                    FontStyle.BoldItalic, TextAnchor.Middle, Color.Black);
        }
    }

    private static string NormalizeDynamicText(string raw) => raw switch
    {
        "cresc" => "cresc.",
        "decresc" => "decresc.",
        "dim" => "dim.",
        _ => raw,
    };

    // ---------- Articulations ----------

    /// <summary>
    /// Draws articulation marks (staccato, accent, tenuto, fermata, etc.)
    /// using their precomputed Emmentaler glyphs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:2268-2310 Script grob
    /// LILYPOND-REF: lily/script-engraver.cc:92-125 acknowledge_note_head
    /// </remarks>
    private static void DrawArticulations(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.ArticulationLayouts.IsDefaultOrEmpty) return;
        foreach (var a in layout.ArticulationLayouts)
        {
            if (string.IsNullOrEmpty(a.Glyph)) continue;
            double y = (sysY.TryGetValue(a.MeasureIndex, out var sy) ? sy : 0) + a.Y;
            using (gc.Source(a.SourcePosition))
                gc.DrawGlyph(a.Glyph[0], a.X, y, FontSize * a.Scale);
        }
    }

    // ---------- Lyrics ----------

    /// <summary>
    /// Draws lyric syllables (and any hyphen / extender connectors) below
    /// the staff using serif text at the LP-style reduced size.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:32-52 LyricText grob
    /// LILYPOND-REF: scm/define-grobs.scm:3025 font-size = -1
    /// </remarks>
    private static void DrawLyrics(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.LyricLayouts.IsDefaultOrEmpty) return;
        double lyricFontSize = FontSize * 0.8;
        foreach (var l in layout.LyricLayouts)
        {
            double y = (sysY.TryGetValue(l.Item.MeasureIndex, out var sy) ? sy : 0) + l.Y;
            // Tag the syllable with its source offset (data-pos) so the preview can
            // click-to-jump and editor-highlight it like a note. SourcePosition 0
            // means "unknown" (would clash with the bar-0 section mark), so only
            // scope a real offset.
            if (l.Item.SourcePosition > 0)
                using (gc.Source(l.Item.SourcePosition))
                    gc.DrawText(l.Item.Text, l.X, y, lyricFontSize, "serif",
                        FontStyle.Regular, TextAnchor.Middle, Color.Black);
            else
                gc.DrawText(l.Item.Text, l.X, y, lyricFontSize, "serif",
                    FontStyle.Regular, TextAnchor.Middle, Color.Black);
            if (l.DrawHyphen)
                gc.DrawText("-", l.HyphenX, y, lyricFontSize, "serif",
                    FontStyle.Regular, TextAnchor.Middle, Color.Black);
            if (l.DrawExtender)
                gc.DrawLine(l.X + l.Width / 2, y - 0.2, l.ExtenderEndX, y - 0.2,
                    Color.Black, 0.1);
        }
    }

    // ---------- Hairpins (cresc / dim wedges) ----------

    /// <summary>
    /// Draws crescendo/decrescendo wedges as a pair of straight lines that
    /// converge to a point (cresc) or open from a point (dim).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:110-358 print()
    /// LILYPOND-REF: scm/define-grobs.scm:1641-1666 Hairpin grob (thickness = 1.0)
    /// </remarks>
    private static void DrawHairpins(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.HairpinLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var h in layout.HairpinLayouts)
        {
            double absY = (sysY.TryGetValue(h.StartMeasureIndex, out var sy) ? sy : 0) + h.Y;
            double leftTop = absY - h.StartOpening;
            double leftBottom = absY + h.StartOpening;
            double rightTop = absY - h.EndOpening;
            double rightBottom = absY + h.EndOpening;
            using (gc.Source(h.SourcePosition))
            {
                // Round caps so the two arms close cleanly at the wedge apex
                // (where StartOpening or EndOpening is 0): butt caps left a
                // small V-notch at the point. Matches LilyPond's blot-rounded
                // line ends. LILYPOND-REF: lily/hairpin.cc — Round_ blot.
                gc.DrawLine(h.StartX, leftTop, h.EndX, rightTop, Color.Black, thickness, cap: LineCap.Round);
                gc.DrawLine(h.StartX, leftBottom, h.EndX, rightBottom, Color.Black, thickness, cap: LineCap.Round);
            }
        }
    }

    // ---------- Ottava brackets (8va / 8vb / 15ma) ----------

    /// <summary>
    /// Draws ottava brackets: serif italic-bold text label, dashed extension
    /// line, and a vertical hook on the closing end.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm OttavaBracket grob
    /// LILYPOND-REF: lily/ottava-bracket.cc — Ottava_bracket
    /// </remarks>
    private static void DrawOttavaBrackets(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.OttavaBracketLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        double textFontSize = FontSize * 0.45;
        foreach (var b in layout.OttavaBracketLayouts)
        {
            double absY = (sysY.TryGetValue(b.StartMeasureIndex, out var sy) ? sy : 0) + b.Y;
            using (gc.Source(b.SourcePosition))
            {
                gc.DrawText(b.Text, b.StartX, absY, textFontSize, "serif",
                    FontStyle.BoldItalic, TextAnchor.Start, Color.Black);

                double textWidth = SerifTextMetrics.MeasureBold(b.Text, textFontSize);
                double lineStartX = b.StartX + textWidth + 0.5;
                if (lineStartX < b.EndX)
                {
                    double dashOn = b.DashPeriod * b.DashFraction;
                    double dashOff = b.DashPeriod * (1 - b.DashFraction);
                    gc.DrawLine(lineStartX, absY, b.EndX, absY,
                        Color.Black, thickness, (dashOn, dashOff));
                }
                if (b.EdgeHeight > 0)
                {
                    double hookDir = b.IsAbove ? 1 : -1;
                    gc.DrawLine(b.EndX, absY, b.EndX, absY + b.EdgeHeight * hookDir,
                        Color.Black, thickness);
                }
            }
        }
    }

    // ---------- Volta brackets (1./2. endings) ----------

    /// <summary>
    /// Draws volta (repeat ending) brackets: optional left hook, horizontal
    /// line, optional right hook, and the volta-number text label.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/volta-bracket.cc:1-170 Volta_bracket_interface
    /// LILYPOND-REF: scm/define-grobs.scm:4292-4317 VoltaBracket grob
    /// </remarks>
    private static void DrawVoltaBrackets(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.VoltaBracketLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.13;
        double edgeHeight = VoltaBracketEngraver.GetEdgeHeight();

        foreach (var v in layout.VoltaBracketLayouts)
        {
            double absY = (sysY.TryGetValue(v.StartMeasureIndex, out var sy) ? sy : 0) + v.Y;
            bool hasText = !string.IsNullOrEmpty(v.VoltaText);
            using (gc.Source(v.SourcePosition))
            {
                if (hasText)
                    gc.DrawLine(v.StartX, absY, v.StartX, absY + edgeHeight,
                        Color.Black, thickness);
                gc.DrawLine(v.StartX, absY, v.EndX, absY,
                    Color.Black, thickness);
                if (v.IsClosed)
                    gc.DrawLine(v.EndX, absY, v.EndX, absY + edgeHeight,
                        Color.Black, thickness);
                if (hasText)
                {
                    // Hang the number from just below the horizontal line so it sits
                    // inside the bracket instead of overlapping the line (matches the
                    // reference SvgRenderer: y = top of glyph at absY + 0.3).
                    double textY = absY + 0.3;
                    gc.DrawText(v.VoltaText, v.StartX + 0.5, textY,
                        FontSize * 0.6, "serif", FontStyle.Bold, TextAnchor.Start, Color.Black,
                        VerticalAnchor.Hanging);
                }
            }
        }
    }

    // ---------- Tuplet brackets ----------

    /// <summary>
    /// Draws tuplet brackets: hook + sloped line (split around the number) +
    /// hook + centered number text. When all members are beamed the bracket
    /// is suppressed (number-only).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:200-350 print()
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket defaults
    /// </remarks>
    private static void DrawTupletBrackets(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.TupletBracketLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.13;
        double edgeHeight = TupletBracketEngraver.GetEdgeHeight();

        foreach (var b in layout.TupletBracketLayouts)
        {
            double sy = sysY.TryGetValue(b.MeasureIndex, out var s) ? s : 0;
            double startY = sy + b.StartY;
            double endY = sy + b.EndY;
            double midX = (b.StartX + b.EndX) / 2;
            double midY = (startY + endY) / 2;
            double hookDir = b.IsStemUp ? 1 : -1;

            using (gc.Source(b.SourcePosition))
            {
                if (b.ShowBracket)
                {
                    gc.DrawLine(b.StartX, startY, b.StartX, startY + edgeHeight * hookDir,
                        Color.Black, thickness);

                    const double numberGap = 1.0;
                    double totalWidth = b.EndX - b.StartX;
                    double leftFrac = totalWidth > 0 ? (midX - numberGap - b.StartX) / totalWidth : 0.5;
                    double rightFrac = totalWidth > 0 ? (midX + numberGap - b.StartX) / totalWidth : 0.5;
                    double leftGapY = startY + (endY - startY) * leftFrac;
                    double rightGapY = startY + (endY - startY) * rightFrac;

                    gc.DrawLine(b.StartX, startY, midX - numberGap, leftGapY,
                        Color.Black, thickness);
                    gc.DrawLine(midX + numberGap, rightGapY, b.EndX, endY,
                        Color.Black, thickness);
                    gc.DrawLine(b.EndX, endY, b.EndX, endY + edgeHeight * hookDir,
                        Color.Black, thickness);
                }

                double textY = b.IsStemUp ? midY - 0.3 : midY + 0.8;
                gc.DrawText(b.NumberText, midX, textY,
                    FontSize * 0.6, "serif", FontStyle.Bold, TextAnchor.Middle, Color.Black);
            }
        }
    }

    // ---------- Trill spanners (tr + wavy line) ----------

    /// <summary>
    /// Draws trill spanners: the "tr" Emmentaler glyph followed by a wavy
    /// extension line. The wave is approximated as a polyline through
    /// peak/valley points (enough segments per cycle that it reads as smooth).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm Trill_spanner_engraver
    /// LILYPOND-REF: scm/define-grobs.scm:2228 (style . trill)
    /// </remarks>
    private static void DrawTrillSpanners(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.TrillSpannerLayouts.IsDefaultOrEmpty) return;
        const double wavePeriod = 0.8;
        const double waveAmplitude = 0.2;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var s in layout.TrillSpannerLayouts)
        {
            double absY = (sysY.TryGetValue(s.StartMeasureIndex, out var sy) ? sy : 0) + s.Y;
            using (gc.Source(s.SourcePosition))
            {
                bool isContinuation = Math.Abs(s.GlyphX - s.LineStartX) < 0.01;
                if (!isContinuation)
                    gc.DrawGlyph(EmmentalerGlyphs.OrnTrill, s.GlyphX, absY, FontSize);
                if (s.LineStartX < s.LineEndX)
                {
                    double length = s.LineEndX - s.LineStartX;
                    int halfWaves = Math.Max(1, (int)(length / (wavePeriod / 2)));
                    double seg = length / halfWaves;
                    double prevX = s.LineStartX, prevY = absY;
                    // Approximate Q-curves with 4 line segments per half-wave;
                    // visually indistinguishable at typical print sizes.
                    const int subdivisions = 4;
                    for (int i = 0; i < halfWaves; i++)
                    {
                        double startX = s.LineStartX + i * seg;
                        double sign = (i % 2 == 0) ? -1 : 1;
                        for (int j = 1; j <= subdivisions; j++)
                        {
                            double t = (double)j / subdivisions;
                            double x = startX + t * seg;
                            // Parabolic shape: y = absY + sign * amplitude * 4 t (1-t)
                            double y = absY + sign * waveAmplitude * 4 * t * (1 - t);
                            gc.DrawLine(prevX, prevY, x, y, Color.Black, thickness);
                            prevX = x; prevY = y;
                        }
                    }
                }
            }
        }
    }

    // ---------- Glissandos ----------

    /// <summary>Draws a simple straight glissando line between two notes.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm, scm/define-grobs.scm Glissando grob
    /// </remarks>
    private static void DrawGlissandos(ScoreLayout layout, IDrawingContext gc)
    {
        if (layout.GlissandoLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var g in layout.GlissandoLayouts)
        {
            using (gc.Source(g.SourcePosition))
                gc.DrawLine(g.StartX, g.StartY, g.EndX, g.EndY, Color.Black, thickness);
        }
    }

    // ---------- Arpeggios (wavy vertical line) ----------

    /// <summary>
    /// Draws arpeggio markings: a wavy vertical line on the left of a chord.
    /// Like the trill wavy line, the curve is approximated as a polyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc, scm/define-grobs.scm:201-224
    /// </remarks>
    private static void DrawArpeggios(ScoreLayout layout, IDrawingContext gc)
    {
        if (layout.ArpeggioLayouts.IsDefaultOrEmpty) return;
        const double wavePeriod = 0.8;
        const double waveAmplitude = 0.2;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var a in layout.ArpeggioLayouts)
        {
            double length = a.BottomY - a.TopY;
            if (length <= 0) continue;
            int halfWaves = Math.Max(1, (int)(length / (wavePeriod / 2)));
            double seg = length / halfWaves;
            double prevX = a.X, prevY = a.TopY;
            const int subdivisions = 4;
            using (gc.Source(a.SourcePosition))
            {
                for (int i = 0; i < halfWaves; i++)
                {
                    double startY = a.TopY + i * seg;
                    double sign = (i % 2 == 0) ? -1 : 1;
                    for (int j = 1; j <= subdivisions; j++)
                    {
                        double t = (double)j / subdivisions;
                        double y = startY + t * seg;
                        double x = a.X + sign * waveAmplitude * 4 * t * (1 - t);
                        gc.DrawLine(prevX, prevY, x, y, Color.Black, thickness);
                        prevX = x; prevY = y;
                    }
                }
            }
        }
    }

    // ---------- Grace notes ----------

    /// <summary>
    /// Draws grace-note groups: small noteheads (with optional accidentals)
    /// scaled to GraceNoteLayout.Scale (typically 0.65), placed before the
    /// main note's column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-engraver.cc:36-80 Grace_engraver
    /// LILYPOND-REF: scm/define-grobs.scm:1358-1402 GraceSpacing grob
    /// </remarks>
    private static void DrawGraceNotes(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.GraceNoteLayouts.IsDefaultOrEmpty) return;
        foreach (var g in layout.GraceNoteLayouts)
        {
            double sy = sysY.TryGetValue(g.MeasureIndex, out var s) ? s : 0;
            // StaffYOffset places the grace over its OWN staff in a multi-staff
            // score (0 for the first staff / single-staff).
            double staffMiddleY = sy + g.StaffYOffset + StaffHeight / 2;
            double scaledFontSize = FontSize * g.Scale;
            double currentX = g.X;
            double lastNoteX = g.X, lastNoteY = staffMiddleY;
            // Per-head geometry, collected so the stems/beam can be drawn once
            // the whole group's positions are known.
            var headX = new List<double>(g.Notes.Length);
            var headY = new List<double>(g.Notes.Length);
            var beamCounts = new List<int>(g.Notes.Length);
            using (gc.Source(g.SourcePosition))
            {
                foreach (var note in g.Notes)
                {
                    double y = StaffFrame.PositionToDevice(note.StaffPosition, staffMiddleY);
                    // Ledgers under the head — layer 0 with the staff lines.
                    // LILYPOND-REF: scm/define-grobs.scm LedgerLineSpanner (layer . 0)
                    if (note.NeedsLedger)
                        DrawLedgerLines(note.StaffPosition, currentX, staffMiddleY, gc,
                            EngravingDefaults.NoteheadBlackWidth * g.Scale);
                    if (note.Accidental is { } acc)
                        DrawAccidental(acc, isCourtesy: false, currentX, y,
                            g.SourcePosition, gc, g.Scale);
                    gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, currentX, y, scaledFontSize);
                    headX.Add(currentX);
                    headY.Add(y);
                    beamCounts.Add(BeamCountForDuration(note.BaseDuration.Denominator));
                    lastNoteX = currentX;
                    lastNoteY = y;
                    currentX += 1.2 * g.Scale;  // approximate advance per grace note
                }

                // Stems (forced UP) plus the connecting beam, or a flag for a lone
                // grace note. Without this the small heads float free of any stem.
                // LILYPOND-REF: scm/music-functions.scm:633-637 score-grace-settings —
                //   ((Voice Stem direction ,UP) (Voice Slur direction ,DOWN)): grace
                //   stems are forced up regardless of pitch, and the auto-slur bows down.
                DrawGraceStemsAndBeam(headX, headY, beamCounts, g.Scale,
                    g.Type == GraceNoteType.Acciaccatura, gc);

                // Grace slur from the last grace notehead to the main notehead.
                // LILYPOND-REF: ly/grace-init.ly startGraceSlur/stopGraceSlur —
                // acciaccatura and appoggiatura are auto-slurred to the main note.
                if (g.Notes.Length > 0 &&
                    g.Type is GraceNoteType.Acciaccatura or GraceNoteType.Appoggiatura)
                {
                    double mainY = StaffFrame.PositionToDevice(g.MainNoteStaffPosition, staffMiddleY);
                    DrawGraceSlur(lastNoteX, lastNoteY, g.MainNoteX, mainY, g.Scale, gc);
                }
            }
        }
    }

    /// <summary>Number of beams/flag-hooks for a duration denominator
    /// (8th=1, 16th=2, 32nd=3, …); 0 for quarter and longer.</summary>
    private static int BeamCountForDuration(int denominator)
    {
        int beams = 0;
        for (int d = denominator; d >= 8; d /= 2) beams++;
        return beams;
    }

    /// <summary>
    /// Draws the up-pointing stems for a grace group, then either a connecting
    /// beam (≥2 beamable heads) or a single flag (lone grace note). Everything is
    /// scaled by the grace scale; stems are forced UP per score-grace-settings.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:633-637 score-grace-settings — grace
    ///   stems are forced UP (so a stem-up beam stacks its secondary beams toward
    ///   the heads, i.e. downward on the page).
    /// LILYPOND-REF: lily/beam.cc secondary beams translated by beam-thickness +
    ///   gap; here BeamTranslation, scaled.
    /// Simplification: equal-length stems, so the beam runs parallel to the head
    /// contour (LilyPond solves a quanted slope). A grace group mixing
    /// beamed (≥8th) and unbeamed (≤quarter) durations falls back to per-head
    /// flags rather than a partial beam.
    /// </remarks>
    private static void DrawGraceStemsAndBeam(
        List<double> xs, List<double> ys, List<int> beamCounts, double scale,
        bool acciaccatura, IDrawingContext gc)
    {
        int n = xs.Count;
        if (n == 0) return;

        double stemThick = EngravingDefaults.StemThickness * scale;
        double stemLen = EngravingDefaults.DefaultStemLength * scale;
        // Stem-up attaches at the right edge of the (scaled) notehead.
        double upAttach = EngravingDefaults.NoteheadBlackWidth * scale - stemThick / 2;
        double StemX(int i) => xs[i] + upAttach;
        double StemEndY(int i) => ys[i] - stemLen;   // up = smaller device Y

        // Draw each stem (head up to its stem end).
        for (int i = 0; i < n; i++)
            gc.DrawLine(StemX(i), ys[i], StemX(i), StemEndY(i), Color.Black, stemThick);

        int maxBeams = 0;
        foreach (var b in beamCounts) maxBeams = Math.Max(maxBeams, b);
        if (maxBeams == 0) return;   // quarter-or-longer grace: bare stems only

        bool allBeamable = n > 1 && beamCounts.All(b => b >= 1);
        if (!allBeamable)
        {
            // Lone grace note (or a non-uniform group): flag each beamable head.
            for (int i = 0; i < n; i++)
            {
                if (beamCounts[i] == 0) continue;
                int denom = 1 << (beamCounts[i] + 2);   // beams→denominator (1→8, 2→16, …)
                var flag = EmmentalerGlyphs.GetFlag(denom, stemUp: true);
                if (flag.HasValue)
                    gc.DrawGlyph(flag.Value, StemX(i), StemEndY(i), FontSize * scale, Color.Black);
                // Acciaccatura: diagonal slash through the stem just below the flag.
                if (acciaccatura)
                    DrawGraceSlash(StemX(i), StemEndY(i), scale, gc);
            }
            return;
        }

        // Beam: primary across the whole group; secondaries stack toward the
        // heads (downward) since grace stems point up.
        double beamThick = EngravingDefaults.BeamThickness * scale;
        double beamTrans = EngravingDefaults.BeamTranslation * scale;
        gc.DrawLine(StemX(0), StemEndY(0), StemX(n - 1), StemEndY(n - 1), Color.Black, beamThick);
        for (int level = 1; level < maxBeams; level++)
        {
            double off = level * beamTrans;   // downward toward the heads
            for (int i = 0; i < n - 1; i++)
                if (beamCounts[i] > level && beamCounts[i + 1] > level)
                    gc.DrawLine(StemX(i), StemEndY(i) + off, StemX(i + 1), StemEndY(i + 1) + off,
                        Color.Black, beamThick);
        }
        // Beamed acciaccatura would carry the slash on the beam itself
        // (Beam.stencil = slashed-stencil); not yet ported — only the lone-note
        // flag dash above is. acciaccatura groups are almost always a single note.
    }

    /// <summary>
    /// Draws the acciaccatura slash: a diagonal stroke through the (up) stem just
    /// below the flag, lower-left to upper-right, with the stem top as origin.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mf/feta-flags.mf:1228-1260 "grace dash (up)" (flags.ugrace) —
    ///   stroke from z1=(-hip_width·0.72, -foot_depth·0.72) to z2=(hip_width, -flare),
    ///   pen = 1.5·stemthickness; with flare = 1 ss, foot_depth = 3 ss,
    ///   hip_width = upflag_width − hip_thickness/2,
    ///   upflag_width = .65·notehead_width + stemthickness/2,
    ///   hip_thickness = linethickness + .069 ss. All scaled by the grace scale.
    /// </remarks>
    private static void DrawGraceSlash(double stemX, double stemTopY, double scale, IDrawingContext gc)
    {
        const double hipDepthRatio = 0.72;
        const double footDepth = 3.0;   // staff spaces
        const double flare = 1.0;       // staff spaces
        double upflagWidth = 0.65 * EngravingDefaults.NoteheadBlackWidth
                           + EngravingDefaults.StemThickness / 2;
        double hipThickness = EngravingDefaults.LineThickness + 0.069;
        double hipWidth = upflagWidth - hipThickness / 2;

        // feta y is up; device y is down, so a feta y of -k is k staff-spaces below
        // the stem top (= +k in device).
        double x1 = stemX - hipWidth * hipDepthRatio * scale;
        double y1 = stemTopY + footDepth * hipDepthRatio * scale;   // lower-left
        double x2 = stemX + hipWidth * scale;
        double y2 = stemTopY + flare * scale;                       // upper-right
        gc.DrawLine(x1, y1, x2, y2, Color.Black, 1.5 * EngravingDefaults.StemThickness * scale);
    }

    /// <summary>
    /// Draws a small slur arcing below from the last grace note to the main
    /// note (grace stems point up, so the slur bows underneath).
    /// </summary>
    /// <remarks>LILYPOND-REF: ly/grace-init.ly — grace auto-slur.</remarks>
    private static void DrawGraceSlur(double graceX, double graceY,
        double mainX, double mainY, double scale, IDrawingContext gc)
    {
        double startX = graceX + GlyphMetrics.NoteheadBlack.CenterX * scale;
        double startY = graceY + 0.5;
        double endX = mainX + GlyphMetrics.NoteheadBlack.CenterX;
        double endY = mainY + 0.5;

        double dx = endX - startX;
        if (dx < 0.5) return; // degenerate

        double arcHeight = Math.Min(dx * 0.25, 1.2);
        var c1 = (X: startX + dx * 0.3, Y: startY + 0.3 * (endY - startY) + arcHeight);
        var c2 = (X: startX + dx * 0.7, Y: startY + 0.7 * (endY - startY) + arcHeight);

        DrawCurve(startX, startY, endX, endY, c1, c2,
            curveUp: false, EngravingDefaults.SlurMidThickness * scale, gc);
    }

    // ---------- Chord names ("Cm7", "B♭7") ----------

    /// <summary>
    /// Draws chord-name labels above the staff using a sans-serif bold font.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm ChordName: font-family=sans, font-size=1.5
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm — chord-name formatting
    /// </remarks>
    private static void DrawChordNames(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.ChordNameLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.65;
        foreach (var c in layout.ChordNameLayouts)
        {
            if (!sysY.TryGetValue(c.MeasureIndex, out var sy)) continue;
            using (gc.Source(c.SourcePosition))
                gc.DrawText(c.ChordText, c.X, sy + c.Y, size, "sans-serif",
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
        }
    }

    // ---------- Figured bass ----------

    /// <summary>
    /// Draws figured-bass numerals stacked vertically below the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/figured-bass-engraver.cc:200-350 print()
    /// LILYPOND-REF: scm/define-grobs.scm:362-380 BassFigure defaults
    /// </remarks>
    private static void DrawFiguredBass(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.FiguredBassLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.75;
        const double figureSpacing = 1.5;
        foreach (var fb in layout.FiguredBassLayouts)
        {
            if (!sysY.TryGetValue(fb.MeasureIndex, out var sy)) continue;
            double baseY = sy + fb.Y;
            using (gc.Source(fb.SourcePosition))
            {
                for (int i = 0; i < fb.FigureTexts.Length; i++)
                    gc.DrawText(fb.FigureTexts[i], fb.X, baseY + i * figureSpacing,
                        size, "serif", FontStyle.Regular, TextAnchor.Middle, Color.Black);
            }
        }
    }

    // ---------- Percent repeats (slash + dots) ----------

    /// <summary>
    /// Draws the percent-repeat sign (a slanted slash with two dots) inside
    /// a measure that repeats the previous one.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/percent-repeat-interface.cc — x_percent() rendering
    /// LILYPOND-REF: scm/define-grobs.scm:2520-2539 — slope=1.0, thickness=0.48
    /// </remarks>
    private static void DrawPercentRepeats(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.PercentRepeatLayouts.IsDefaultOrEmpty) return;
        const double slope = 1.0;
        const double thickness = 0.48;
        const double slashWidth = 2.0;
        const double dotOffset = 1.0;
        const double dotRadius = 0.25;
        double slashHeight = slashWidth * slope;

        foreach (var pr in layout.PercentRepeatLayouts)
        {
            if (!sysY.TryGetValue(pr.MeasureIndex, out var sy)) continue;
            double cx = pr.X;
            double cy = sy + pr.Y;
            using (gc.Source(pr.SourcePosition))
            {
                // Slash from bottom-left to top-right
                gc.DrawLine(cx - slashWidth / 2, cy + slashHeight / 2,
                    cx + slashWidth / 2, cy - slashHeight / 2,
                    Color.Black, thickness);
                gc.DrawCircle(cx + dotOffset * 0.3, cy - dotOffset, dotRadius, Color.Black);
                gc.DrawCircle(cx - dotOffset * 0.3, cy + dotOffset, dotRadius, Color.Black);
            }
        }
    }

    // ---------- Bar numbers ----------

    /// <summary>
    /// Draws the bar-number text at the start of each system (and at any
    /// requested period). Position is precomputed by BarNumberEngraver.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/bar-number-engraver.cc — Bar_number_engraver
    /// LILYPOND-REF: scm/define-grobs.scm BarNumber (font-size = -2)
    /// </remarks>
    private static void DrawBarNumbers(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.BarNumberLayouts.IsDefaultOrEmpty) return;
        // LILYPOND-REF: scm/define-grobs.scm BarNumber (font-size . -2) —
        // 2.2sp text height × magstep(-2); see BarNumberEngraver.FontSize.
        double fontSize = BarNumberEngraver.FontSize;
        // Collisions with voltas/marks are resolved by the unified
        // outside-staff stacking pass (OutsideStaffStacker.StackAboveStaff).
        foreach (var bn in layout.BarNumberLayouts)
        {
            double y = bn.Y;
            gc.DrawText(bn.Text, bn.X, y, fontSize, "serif",
                FontStyle.Bold, bn.RightAligned ? TextAnchor.End : TextAnchor.Start,
                Color.Black);
        }
    }

    // ---------- Stanza numbers ----------

    /// <summary>
    /// Draws stanza numbers ("1.", "2.") at the left of each verse line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stanza-number-engraver.cc — Stanza_number_engraver
    /// LILYPOND-REF: scm/define-grobs.scm StanzaNumber (font-size=-1, bold)
    /// </remarks>
    private static void DrawStanzaNumbers(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.StanzaNumberLayouts.IsDefaultOrEmpty) return;
        const double fontSize = 2.4;
        foreach (var sn in layout.StanzaNumberLayouts)
        {
            // sn.Y is relative to the system top (the verse's lyric baseline);
            // add the system Y like DrawLyrics, so the number sits next to its
            // verse line rather than at the page top.
            double y = (sysY.TryGetValue(sn.MeasureIndex, out var s) ? s : 0) + sn.Y;
            gc.DrawText(sn.Text, sn.X, y, fontSize, "serif",
                FontStyle.Bold, TextAnchor.Start, Color.Black);
        }
    }

    // ---------- Fingering ----------

    /// <summary>
    /// Draws fingering numerals (1-5) next to noteheads.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob
    /// LILYPOND-REF: scm/define-grobs.scm Fingering (font-size = -5 → ~0.56×)
    /// </remarks>
    private static void DrawFingerings(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.FingeringLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.56;  // magstep(-5)
        foreach (var f in layout.FingeringLayouts)
        {
            double y = (sysY.TryGetValue(f.MeasureIndex, out var sy) ? sy : 0) + f.Y;
            using (gc.Source(f.SourcePosition))
                gc.DrawText(f.Number.ToString(), f.X, y, size, "serif",
                    FontStyle.Regular, TextAnchor.Middle, Color.Black);
        }
    }

    // ---------- Music marks (segno, coda, fine, tempo, rehearsal, pedal text) ----------

    /// <summary>
    /// Draws music marks: navigation labels (Segno/Coda/Fine/D.S./D.C.),
    /// pedal text (Ped./Sost.), tempo markings (♩= NNN), rehearsal marks
    /// (boxed letters), and section labels.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/mark-engraver.cc:90-140 Mark types
    /// LILYPOND-REF: scm/define-grobs.scm:3650-3710 Segno, Coda
    /// </remarks>
    private static void DrawMusicMarks(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.MusicMarkLayouts.IsDefaultOrEmpty) return;
        foreach (var m in layout.MusicMarkLayouts)
        {
            if (IsHandledBySpannerEngraver(m.MarkType)) continue;
            double y = (sysY.TryGetValue(m.MeasureIndex, out var s) ? s : 0) + m.Y;
            using (gc.Source(m.SourcePosition))
                DrawSingleMusicMark(m, y, gc);
        }
    }

    private static void DrawSingleMusicMark(MusicMarkLayout m, double absY, IDrawingContext gc)
    {
        if (m.IsSymbol)
        {
            // Segno (U+E062) / Coda (U+E064) in this Emmentaler cmap.
            // NOTE: the SMuFL codepoints U+E047/E048 map to scripts.thumb /
            // scripts.sforzato here and previously drew the WRONG glyphs.
            char glyph = m.MarkType == MusicMarkType.Segno
                ? EmmentalerGlyphs.MarkSegno
                : EmmentalerGlyphs.MarkCoda;
            gc.DrawGlyph(glyph, m.X, absY, FontSize, Color.Black);
            return;
        }
        if (m.MarkType == MusicMarkType.Tempo)
        {
            // LILYPOND-REF: scm/define-grobs.scm:1835 MetronomeMark
            // LILYPOND-REF: lily/metronome-engraver.cc — notehead + stem + " = NNN"
            const double noteSize = 1.6;
            const double textSize = 1.8;
            gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, m.X, absY, noteSize);
            double stemX = m.X + noteSize * 0.32;
            double stemTop = absY - 3.5 * (noteSize / FontSize);
            gc.DrawLine(stemX, absY, stemX, stemTop, Color.Black, 0.10);
            gc.DrawText("= " + m.Text, m.X + noteSize * 0.5 + 0.3, absY,
                textSize, "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);
            return;
        }
        if (m.MarkType == MusicMarkType.Rehearsal || m.MarkType == MusicMarkType.SectionLabel)
        {
            double fs = m.MarkType == MusicMarkType.Rehearsal ? FontSize * 0.6 : FontSize * 0.55;
            const double pad = 0.2;
            double textWidth = SerifTextMetrics.MeasureBold(m.Text, fs);
            double boxW = textWidth + pad * 2;
            double boxH = fs + pad * 2;
            gc.DrawRectangle(m.X - boxW / 2, absY - boxH / 2, boxW, boxH,
                fill: Color.White, stroke: Color.Black, strokeWidth: 0.10);
            gc.DrawText(m.Text, m.X, absY + fs / 2 - pad, fs, "serif",
                FontStyle.Bold, TextAnchor.Middle, Color.Black);
            return;
        }
        if (IsPedalMark(m.MarkType))
        {
            bool italic = m.MarkType is MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
                or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;
            gc.DrawText(m.Text, m.X, absY, FontSize * 0.7, "serif",
                italic ? FontStyle.BoldItalic : FontStyle.Bold, TextAnchor.Middle, Color.Black);
            return;
        }
        // Default text marks (D.S./D.C./Fine/etc.)
        gc.DrawText(m.Text, m.X, absY, FontSize * 0.7, "serif",
            FontStyle.BoldItalic, TextAnchor.Middle, Color.Black);
    }

    private static bool IsHandledBySpannerEngraver(MusicMarkType type) =>
        MusicMarkItem.IsSpannerHandled(type);

    private static bool IsPedalMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
             or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;

    // ---------- Custom text annotations ----------

    /// <summary>Draws free-form text annotations (e.g. "molto rit.", "a tempo").</summary>
    /// <remarks>LILYPOND-REF: lily/text-interface.cc — text rendering</remarks>
    private static void DrawCustomTexts(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.CustomTextLayouts.IsDefaultOrEmpty) return;
        foreach (var t in layout.CustomTextLayouts)
        {
            double y = (sysY.TryGetValue(t.MeasureIndex, out var s) ? s : 0) + t.Y;
            using (gc.Source(t.SourcePosition))
                gc.DrawText(t.Text, t.X, y, FontSize * 0.6, "serif",
                    FontStyle.Italic, TextAnchor.Middle, Color.Black);
        }
    }

    // ---------- Text spanners (rit. ----, accel. ----) ----------

    /// <summary>
    /// Draws text spanners: italic label followed by an extension line (dashed
    /// or solid) to the spanner end.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/text-spanner-engraver.cc TextSpanner engraver
    /// LILYPOND-REF: scm/define-grobs.scm:3504-3535 TextSpanner grob
    /// </remarks>
    private static void DrawTextSpanners(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.TextSpannerLayouts.IsDefaultOrEmpty) return;
        double textSize = FontSize * 0.5;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var s in layout.TextSpannerLayouts)
        {
            double absY = (sysY.TryGetValue(s.StartMeasureIndex, out var y) ? y : 0) + s.Y;
            using (gc.Source(s.SourcePosition))
            {
                gc.DrawText(s.Text, s.StartX, absY, textSize, "serif",
                    FontStyle.Italic, TextAnchor.Start, Color.Black);
                if (s.Style != TextSpannerStyle.None && s.LineStartX < s.EndX)
                {
                    (double On, double Off)? dash = s.Style == TextSpannerStyle.DashedLine
                        ? (s.DashPeriod * s.DashFraction, s.DashPeriod * (1 - s.DashFraction))
                        : null;
                    gc.DrawLine(s.LineStartX, absY, s.EndX, absY,
                        Color.Black, thickness, dash);
                }
            }
        }
    }

    // ---------- Pedal brackets ----------

    /// <summary>
    /// Draws piano pedal brackets: horizontal line below staff with a
    /// vertical hook at the release point.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/pedal-bracket.cc — PianoPedalBracket grob
    /// </remarks>
    private static void DrawPedalBrackets(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.PedalBracketLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var b in layout.PedalBracketLayouts)
        {
            double absY = (sysY.TryGetValue(b.StartMeasureIndex, out var y) ? y : 0) + b.Y;
            using (gc.Source(b.SourcePosition))
            {
                gc.DrawLine(b.StartX, absY, b.EndX, absY, Color.Black, thickness);
                gc.DrawLine(b.EndX, absY - b.EdgeHeight, b.EndX, absY, Color.Black, thickness);
            }
        }
    }

    // ---------- Multi-measure rests ----------

    /// <summary>
    /// Draws multi-measure rest indicators. Short runs (≤ ExpandLimit) use the
    /// church_rest decomposition (combinations of long/breve/whole rest
    /// glyphs); longer runs use the big_rest H-bar with a bold count above.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/multi-measure-rest.cc:194-220 big_rest
    /// LILYPOND-REF: lily/multi-measure-rest.cc:225-300 church_rest
    /// </remarks>
    private static void DrawMultiMeasureRests(ScoreLayout layout, IDrawingContext gc)
    {
        if (layout.MultiMeasureRestLayouts.IsDefaultOrEmpty) return;
        foreach (var mmr in layout.MultiMeasureRestLayouts)
        {
            if (mmr.UseChurchRest)
                DrawChurchRest(mmr, gc);
            else
                DrawBigRest(mmr, gc);
        }
    }

    private static void DrawChurchRest(MultiMeasureRestLayout mmr, IDrawingContext gc)
    {
        double cx = (mmr.StartX + mmr.EndX) / 2.0;
        double cy = mmr.Y;

        // Greedy decomposition: 4 (long), 2 (breve), 1 (whole).
        var pieces = new List<(int Span, char Glyph, double Width, double Y)>();
        const double LongWidth = 2.0, BreveWidth = 1.5, WholeWidth = 1.5, Gap = 0.4;
        int remaining = mmr.MeasureCount;
        foreach (var (span, glyph, width, dy) in new[]
        {
            (4, EmmentalerGlyphs.RestLonga, LongWidth, 0.0),
            (2, EmmentalerGlyphs.RestDoubleWhole, BreveWidth, 0.0),
            (1, EmmentalerGlyphs.RestWhole, WholeWidth, -0.5),
        })
        {
            while (remaining >= span)
            {
                pieces.Add((span, glyph, width, cy + dy));
                remaining -= span;
            }
        }
        if (pieces.Count == 0) return;

        double totalWidth = pieces.Sum(p => p.Width) + Gap * (pieces.Count - 1);
        double x = cx - totalWidth / 2;
        foreach (var p in pieces)
        {
            gc.DrawGlyph(p.Glyph, x + p.Width / 2, p.Y, FontSize);
            x += p.Width + Gap;
        }
        if (mmr.MeasureCount > 1)
            gc.DrawText(mmr.MeasureCount.ToString(), cx, cy - 2.5,
                2.4, "serif", FontStyle.Bold, TextAnchor.Middle, Color.Black);
    }

    private static void DrawBigRest(MultiMeasureRestLayout mmr, IDrawingContext gc)
    {
        const double thickness = 0.5;
        const double endCapHeight = 0.8;
        const double padding = 1.0;
        const double capThickness = 0.18;

        double left = mmr.StartX + padding;
        double right = mmr.EndX - padding;
        if (right <= left) return;
        double cy = mmr.Y;

        gc.DrawRectangle(left, cy - thickness / 2, right - left, thickness, fill: Color.Black);
        gc.DrawRectangle(left - capThickness / 2, cy - endCapHeight,
            capThickness, 2 * endCapHeight, fill: Color.Black);
        gc.DrawRectangle(right - capThickness / 2, cy - endCapHeight,
            capThickness, 2 * endCapHeight, fill: Color.Black);

        double textX = (left + right) / 2;
        double textY = cy - endCapHeight - 0.5;
        gc.DrawText(mmr.MeasureCount.ToString(), textX, textY,
            2.4, "serif", FontStyle.Bold, TextAnchor.Middle, Color.Black);
    }

    // ---------- Tie variants (laissez-vibrer / repeat-tie) ----------

    /// <summary>
    /// Draws half-ties: laissez-vibrer (let-ring, pointing right out of the
    /// note) and repeat-tie (pointing left into the note from a repeat).
    /// Same Bezier-bow shape as full ties.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/laissez-vibrer-engraver.cc — LaissezVibrerTie grob
    /// LILYPOND-REF: lily/repeat-tie-engraver.cc — RepeatTie grob
    /// </remarks>
    private static void DrawTieVariants(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.TieVariantLayouts.IsDefaultOrEmpty) return;
        // Tie variants use staff-relative Y already in the layout — no system offset needed
        // (TieVariantEngraver computes absolute Y).
        foreach (var v in layout.TieVariantLayouts)
        {
            DrawCurve(v.StartX, v.Y, v.EndX, v.Y,
                v.Control1, v.Control2, v.CurveUp,
                EngravingDefaults.TieMidThickness, gc);
        }
    }

    // ---------- Lyric hyphen dashes ----------

    /// <summary>
    /// Draws explicit hyphen dashes between syllables of the same word
    /// (LyricLayout.DrawHyphen handles single-character hyphens; this draws
    /// the multi-dash sequence layouts that span wider gaps).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-hyphen.cc:60-100 LyricHyphen grob
    /// </remarks>
    private static void DrawLyricHyphens(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.LyricHyphenLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.16;
        foreach (var h in layout.LyricHyphenLayouts)
        {
            if (h.Type == LyricConnectorType.Hyphen)
            {
                foreach (var dash in h.Dashes)
                {
                    var src = layout.LyricLayouts[h.LyricIndex];
                    double sy = sysY.TryGetValue(src.Item.MeasureIndex, out var s) ? s : 0;
                    gc.DrawLine(dash.X1, sy + dash.Y, dash.X2, sy + dash.Y,
                        Color.Black, thickness);
                }
            }
            else if (h.Type == LyricConnectorType.Extender)
            {
                var src = layout.LyricLayouts[h.LyricIndex];
                double sy = sysY.TryGetValue(src.Item.MeasureIndex, out var s) ? s : 0;
                if (h.CrossesSystemBreak)
                {
                    gc.DrawLine(h.ExtenderStartX, sy + h.ExtenderY,
                        h.FirstSegmentEndX, sy + h.ExtenderY, Color.Black, 0.1);
                    gc.DrawLine(h.SecondSegmentStartX, sy + h.ExtenderY,
                        h.ExtenderEndX, sy + h.ExtenderY, Color.Black, 0.1);
                }
                else
                {
                    gc.DrawLine(h.ExtenderStartX, sy + h.ExtenderY,
                        h.ExtenderEndX, sy + h.ExtenderY, Color.Black, 0.1);
                }
            }
        }
    }

    // ---------- Part combine annotations ----------

    /// <summary>Draws part-combine text labels ("a2", "Solo", "Solo II").</summary>
    /// <remarks>LILYPOND-REF: scm/part-combiner.scm — CombineTextScript</remarks>
    private static void DrawPartCombine(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.PartCombineLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.65;
        foreach (var pc in layout.PartCombineLayouts)
        {
            double y = (sysY.TryGetValue(pc.MeasureIndex, out var s) ? s : 0) + pc.Y;
            gc.DrawText(pc.Text, pc.X, y, size, "serif",
                FontStyle.Italic, TextAnchor.Start, Color.Black);
        }
    }

    // ---------- Tremolo (stem slashes, drawn from DrawNote) ----------

    /// <summary>
    /// Draws tremolo beams across a stem: short angled slashes at the stem's
    /// midpoint. Number of slashes corresponds to the tremolo subdivision.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem-tremolo.cc:129-150 raw_stencil
    /// LILYPOND-REF: lily/stem-tremolo.cc:81-94 width depends on flag
    /// LILYPOND-REF: lily/stem-tremolo.cc:45-79 calc-slope
    /// </remarks>
    private static void DrawTremolo(
        double stemX, double stemAttachY, double stemEndY,
        bool stemUp, int beamCount, bool hasFlag, IDrawingContext gc)
    {
        if (beamCount <= 0) return;
        double beamWidth = hasFlag ? 1.0 : 1.5;
        const double beamThickness = 0.48;
        const double beamGap = 0.8;
        double slope = (!stemUp && hasFlag) ? 0.40 : 0.25;

        double stemMidY = (stemAttachY + stemEndY) / 2;
        double totalHeight = beamCount * beamThickness + (beamCount - 1) * beamGap;
        double startY = stemMidY - totalHeight / 2 + beamThickness / 2;

        for (int i = 0; i < beamCount; i++)
        {
            double y = startY + i * (beamThickness + beamGap);
            double halfW = beamWidth / 2;
            double dy = halfW * slope;
            double y1 = stemUp ? y + dy : y - dy;
            double y2 = stemUp ? y - dy : y + dy;
            gc.DrawLine(stemX - halfW, y1, stemX + halfW, y2,
                Color.Black, beamThickness);
        }
    }

    // ---------- System-start delimiters (group brackets / bar lines) ----------

    /// <summary>
    /// Draws the system-start delimiter (bracket / line-bracket / bar-line)
    /// on the left edge of each multi-staff group. Brace rendering is left
    /// to a future phase that ports BraceRenderer's path output.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter.cc:127-129 collapse_height check
    /// LILYPOND-REF: scm/define-grobs.scm SystemStartBrace/Bracket/Square/Bar
    /// </remarks>
    /// <summary>
    /// Draws the instrument name text for each staff group. When a grand-staff
    /// group has only one named staff, the name is centered vertically across
    /// the brace span; otherwise each named staff gets its own centered name.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/instrument-name-engraver.cc — InstrumentName grob
    /// LILYPOND-REF: scm/define-grobs.scm:1711-1728 InstrumentName
    ///   font: serif, padding 0.3, self-alignment-X = CENTER, self-alignment-Y = CENTER
    /// LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
    ///   nameX = MarginLeft + indent / 2 (MarginLeft applied by the page-level
    ///   translate group, so this method uses indent / 2 directly).
    /// </remarks>
    private static void DrawInstrumentNames(SystemLayout system, IDrawingContext gc)
    {
        if (system.Indent <= 0) return;
        if (system.StaffGroups.IsDefaultOrEmpty) return;

        const double NameFontScale = 0.75;
        double actualFontSize = FontSize * NameFontScale;
        double nameX = system.Indent / 2.0;

        foreach (var staffGroup in system.StaffGroups)
        {
            bool anyNamed = false;
            foreach (var sl in staffGroup.Staves)
            {
                if (!string.IsNullOrEmpty(sl.InstrumentName)) { anyNamed = true; break; }
            }
            if (!anyNamed) continue;

            // Single name spanning a delimited group: center vertically across the brace.
            if (staffGroup.HasDelimiter && staffGroup.GrandStaffLayout is { } gs)
            {
                int namedCount = 0;
                StaffLayout? onlyNamed = null;
                foreach (var sl in staffGroup.Staves)
                {
                    if (string.IsNullOrEmpty(sl.InstrumentName)) continue;
                    namedCount++;
                    onlyNamed = sl;
                    if (namedCount > 1) break;
                }
                if (namedCount == 1 && onlyNamed is { })
                {
                    double centerY = system.Y + (gs.BraceTop + gs.BraceBottom) / 2.0;
                    gc.DrawText(onlyNamed.InstrumentName!, nameX, centerY,
                        actualFontSize, "serif", FontStyle.Regular,
                        TextAnchor.Middle, fill: null,
                        verticalAnchor: VerticalAnchor.Middle);
                    continue;
                }
                // Multiple named staves fall through to per-staff rendering.
            }

            foreach (var staffLayout in staffGroup.Staves)
            {
                if (string.IsNullOrEmpty(staffLayout.InstrumentName) || staffLayout.IsHidden)
                    continue;
                double staffY = system.Y + staffLayout.Y;
                double centerY = staffY + staffLayout.Height / 2.0;
                gc.DrawText(staffLayout.InstrumentName, nameX, centerY,
                    actualFontSize, "serif", FontStyle.Regular,
                    TextAnchor.Middle, fill: null,
                    verticalAnchor: VerticalAnchor.Middle);
            }
        }
    }

    /// <summary>
    /// Joins the staves of a multi-staff system: a SystemStartBar at the left
    /// edge (always, for 2+ staves), and — within delimited groups (grand
    /// staff etc.) — every barline extended through the inter-staff gap
    /// (Span_bar), with repeat dots omitted in the gap.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter-engraver.cc — Score-level
    ///   SystemStartBar joins all staves of any multi-staff system.
    /// LILYPOND-REF: lily/span-bar-engraver.cc + ly/engraver-init.ly —
    ///   Span_bar_engraver lives in GrandStaff/PianoStaff/StaffGroup, so
    ///   ungrouped staves do NOT span their barlines.
    /// LILYPOND-REF: lily/span-bar.cc print() — the spanned segment redraws
    ///   the bar glyph without the dots.
    /// </remarks>
    private static void DrawStaffConnectors(
        MultiStaffScore score, ScoreLayout layout, SystemLayout system,
        double systemStartX, IDrawingContext gc)
    {
        if (system.StaffGroups.IsDefaultOrEmpty)
            return;

        // SystemStartBar across ALL visible staves of the system.
        var allStaves = system.StaffGroups
            .SelectMany(g => g.Staves)
            .Where(s => !s.IsHidden && !s.IsOssia)
            .OrderBy(s => s.Y)
            .ToList();
        if (allStaves.Count >= 2)
        {
            double top = system.Y + allStaves[0].Y;
            double bottom = system.Y + allStaves[^1].Y + allStaves[^1].Height;
            DrawSystemStartBarLine(systemStartX, top, bottom, gc);
        }

        // Span bars inside delimited groups. Barline types come from the
        // first voice — they are score-synchronized at collection time.
        var voice = score.StaffGroups[0].Staves[0].PrimaryVoice;
        foreach (var group in system.StaffGroups)
        {
            if (!group.HasDelimiter)
                continue;
            var staves = group.Staves
                .Where(s => !s.IsHidden && !s.IsOssia)
                .OrderBy(s => s.Y)
                .ToList();
            if (staves.Count < 2)
                continue;

            foreach (var ml in system.Measures)
            {
                if (ml.MeasureIndex >= voice.Measures.Length)
                    continue;
                var measure = voice.Measures[ml.MeasureIndex];

                bool suppressEnd = measure.EndBarline == BarlineType.Single
                    && IsMmrInnerEndBarline(layout, ml.MeasureIndex);
                double endWidth = GetVisualBarlineWidth(measure.EndBarline);

                for (int i = 0; i + 1 < staves.Count; i++)
                {
                    double gapTop = system.Y + staves[i].Y + staves[i].Height;
                    double gapBottom = system.Y + staves[i + 1].Y;
                    double gapHeight = gapBottom - gapTop;
                    if (gapHeight <= 0)
                        continue;

                    if (measure.StartBarline != BarlineType.None)
                        DrawBarline(measure.StartBarline, ml.X, gapTop, gapHeight,
                            gc, withDots: false);
                    if (!suppressEnd)
                        DrawBarline(measure.EndBarline, ml.X + ml.Width - endWidth,
                            gapTop, gapHeight, gc, withDots: false);
                }
            }
        }
    }

    private static void DrawSystemStartDelimiters(SystemLayout system, IDrawingContext gc)
    {
        if (system.StaffGroups.IsDefaultOrEmpty) return;
        foreach (var group in system.StaffGroups)
        {
            if (group.GrandStaffLayout is not { } delim) continue;
            double top = system.Y + delim.BraceTop;
            double bottom = system.Y + delim.BraceBottom;
            double height = bottom - top;
            switch (delim.DelimiterType)
            {
                case SystemStartDelimiterType.Bracket:
                    if (height >= 5)
                        DrawSystemStartBracket(delim.BraceX, top, bottom, gc);
                    break;
                case SystemStartDelimiterType.LineBracket:
                    if (height >= 5)
                        DrawSystemStartLineBracket(delim.BraceX, top, bottom, gc);
                    break;
                case SystemStartDelimiterType.BarLine:
                    DrawSystemStartBarLine(delim.BraceX, top, bottom, gc);
                    break;
                case SystemStartDelimiterType.Brace:
                    // LILYPOND-REF: scm/define-grobs.scm SystemStartBrace collapse-height = 5
                    if (height >= 5)
                        DrawSystemStartBrace(delim.BraceX, top, bottom, gc);
                    break;
            }
        }
    }

    private static void DrawSystemStartBracket(double x, double top, double bottom, IDrawingContext gc)
    {
        double thickness = 0.45;
        double serifH = 0.4, serifW = 0.6;
        gc.DrawLine(x, top, x, bottom, Color.Black, thickness);
        // Top serif (right-pointing triangle filled)
        gc.DrawClosedBezier(
            (x, top), (x + serifW, top), (x + serifW, top),
            (x + serifW * 0.3, top + serifH), (x + serifW * 0.3, top + serifH), (x + serifW * 0.3, top + serifH),
            Color.Black);
        // Bottom serif
        gc.DrawClosedBezier(
            (x, bottom), (x + serifW, bottom), (x + serifW, bottom),
            (x + serifW * 0.3, bottom - serifH), (x + serifW * 0.3, bottom - serifH), (x + serifW * 0.3, bottom - serifH),
            Color.Black);
    }

    private static void DrawSystemStartLineBracket(double x, double top, double bottom, IDrawingContext gc)
    {
        double thickness = EngravingDefaults.StaffLineThickness;
        const double hookWidth = 0.5;
        gc.DrawLine(x, top, x, bottom, Color.Black, thickness);
        gc.DrawLine(x, top, x + hookWidth, top, Color.Black, thickness);
        gc.DrawLine(x, bottom, x + hookWidth, bottom, Color.Black, thickness);
    }

    private static void DrawSystemStartBarLine(double x, double top, double bottom, IDrawingContext gc)
    {
        double thickness = EngravingDefaults.StaffLineThickness * 1.6;
        gc.DrawLine(x, top, x, bottom, Color.Black, thickness);
    }

    /// <summary>
    /// Draws the curly brace used for grand staff (piano) groups. The brace
    /// is rendered as a single Emmentaler-Brace glyph (576 sizes available
    /// at U+E000+index, larger index → taller brace). Glyph selection mirrors
    /// <see cref="Svg.Renderer.BraceRenderer"/> so SVG and PDF agree on size.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-markup-commands.scm (left-brace)
    /// </remarks>
    // ---------- Mid-measure clef change ----------

    /// <summary>
    /// Draws a mid-measure clef change at reduced size (LP _change variant glyphs).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/clef.cc:29-52 — calc_glyph_name appends "_change" suffix
    /// </remarks>
    private static void DrawClefChange(ClefChangeItem clefChange, double x, double staffY, IDrawingContext gc)
    {
        char glyph = clefChange.NewClef switch
        {
            ClefType.Bass => EmmentalerGlyphs.FClefChange,
            ClefType.Alto => EmmentalerGlyphs.CClefChange,
            ClefType.Tenor => EmmentalerGlyphs.CClefChange,
            _ => EmmentalerGlyphs.GClefChange,
        };
        double clefY = clefChange.NewClef switch
        {
            ClefType.Bass => staffY + 1,
            ClefType.Alto => staffY + 2,
            ClefType.Tenor => staffY + 1,
            _ => staffY + 3,
        };
        using (gc.Source(clefChange.SourcePosition))
            gc.DrawGlyph(glyph, x, clefY, FontSize);
    }

    // ---------- Mid-measure key signature change ----------

    /// <summary>
    /// Draws a mid-measure key signature change. Cancellation naturals are
    /// shown for accidentals removed from the previous key, followed by the
    /// new key's accidentals.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/key-engraver.cc — process_music()
    /// </remarks>
    private static void DrawKeySignatureChange(KeySignatureChangeItem change, double x, double staffY, IDrawingContext gc)
    {
        int prev = change.PreviousKey.Sharps;
        int next = change.NewKey.Sharps;
        double dx = 0;

        // Cancellation naturals when the sign flips or count shrinks.
        bool needNaturals = (prev != 0 && next == 0) ||
                            (prev > 0 && next < 0) || (prev < 0 && next > 0) ||
                            (Math.Sign(prev) == Math.Sign(next) && Math.Abs(next) < Math.Abs(prev));
        if (needNaturals)
        {
            int natCount = Math.Abs(prev) - (Math.Sign(prev) == Math.Sign(next) ? Math.Abs(next) : 0);
            int[] sharpPos = { 8, 5, 9, 6, 3, 7, 4 };
            int[] flatPos = { 4, 7, 3, 6, 2, 5, 1 };
            var positions = prev > 0 ? sharpPos : flatPos;
            int startAt = Math.Sign(prev) == Math.Sign(next) ? Math.Abs(next) : 0;
            for (int i = 0; i < natCount; i++)
            {
                int pos = positions[startAt + i];
                double y = staffY + 4 - (pos - 1) * 0.5;
                using (gc.Source(change.SourcePosition))
                    gc.DrawGlyph(EmmentalerGlyphs.AccidentalNatural, x + dx, y, FontSize);
                dx += 0.7;
            }
        }

        if (next != 0)
            DrawKeySignature(change.NewKey, ClefType.Treble, x + dx, staffY, gc);
    }

    /// <summary>
    /// Draws a mid-piece time signature change at the change point, full size
    /// (unlike clef changes, which use reduced _change glyphs).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/time-signature-engraver.cc</remarks>
    private static void DrawTimeSignatureChange(TimeSignatureChangeItem timeChange, double x, double staffY, IDrawingContext gc)
    {
        using (gc.Source(timeChange.SourcePosition))
            DrawTimeSignature(timeChange.NewTime, x, staffY, gc);
    }

    private static void DrawSystemStartBrace(double x, double top, double bottom, IDrawingContext gc)
    {
        double height = bottom - top;
        double yMid = (top + bottom) / 2;

        const int braceGlyphStart = 0xE000;
        const int braceGlyphCount = 576;
        const double minGlyphHeight = 263.0;
        const double maxGlyphHeight = 11493.0;
        const double unitsPerEm = 1000.0;
        const double scaleFactor = 0.76;

        double targetUnits = height * unitsPerEm;
        double ratio = Math.Clamp((targetUnits - minGlyphHeight) / (maxGlyphHeight - minGlyphHeight), 0, 1);
        int glyphIndex = Math.Clamp((int)(Math.Pow(ratio, 0.8) * (braceGlyphCount - 1)), 0, braceGlyphCount - 1);
        double glyphHeightUnits = minGlyphHeight + ((double)glyphIndex / (braceGlyphCount - 1)) * (maxGlyphHeight - minGlyphHeight);
        double fontSize = (height / (glyphHeightUnits / unitsPerEm)) * scaleFactor;

        char braceChar = (char)(braceGlyphStart + glyphIndex);
        gc.DrawText(braceChar.ToString(), x, yMid, fontSize, "Emmentaler-Brace",
            FontStyle.Regular, TextAnchor.End, Color.Black);
    }
}
