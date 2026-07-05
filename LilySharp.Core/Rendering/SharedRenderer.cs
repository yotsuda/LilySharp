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
    // Height of the short measure-divider barlines on a lead-sheet text row
    // (no staff, so the bar is just a tick the chord row hangs on).
    private const double LeadSheetBarlineHeight = 2.0;
    // Mirror of LyricEngraver's VerseSpacing (baseline step between stacked verses).
    private const double LyricVerseSpacing = 3.2;
    private const double FontSize = 4.0;
    private const double TempoNoteSize = 1.6;  // metronome-mark notehead size (shared with the swing equation)
    private const double OssiaScale = EngravingDefaults.OssiaScale; // magstep(-3), shared with the layouter

    public static void RenderTo(
        MultiStaffScore score, ScoreLayout layout, IDocumentContext doc,
        bool resolveDataPos = false)
    {
        // F3/B: a layout freshly built from THIS score already bakes the correct
        // data-pos, so resolution is a no-op there and we skip it (it rebuilds every
        // annotation array — measurable allocation on annotation-heavy scores). Only a
        // REUSED (cached, whole-layout) layout carries stale offsets from the pre-edit
        // score; the IncrementalCompiler reuse path passes resolveDataPos=true to
        // re-derive each annotation's source offset from the live score.
        if (resolveDataPos)
            layout = ResolveDataPos(layout, score);
        var options = layout.Options;
        var resolver = layout.GrobPropertyResolver;
        // Items participating in a beam — DrawNote/DrawChord skip stem & flag for these,
        // because DrawBeams will draw the beam-aware stem instead. Mirrors SvgRenderer's
        // _beamedStemEndYs gating (lily/stem.cc — beamed stem end is computed by beam layout).
        var beamedItems = BuildBeamedItemsSet(layout);
        // Ossia staves: their annotations (dynamics / scripts) shrink with the
        // notation, like every grob the magnified staff owns in LP
        // (ly/music-functions-init.ly magnifyStaff scales fontSize).
        var ossiaStaves = new HashSet<int>();
        foreach (var (_, st, gi) in score.EnumerateStaves())
            if (st.IsOssia)
                ossiaStaves.Add(gi);
        bool firstPage = true;
        foreach (var page in layout.Pages)
        {
            var gc = doc.BeginPage(page.Width, page.Height);
            // LILYPOND-REF: lily/page-layout-problem.cc:434 — header at MarginTop;
            // SystemLayout.Y already includes MarginTop, so apply MarginLeft only.
            // The title/composer header belongs to the FIRST page only (later
            // pages reserve no header height, so re-printing it overlapped the
            // top system).
            if (firstPage)
                DrawHeader(score, page, options, gc);
            firstPage = false;
            var marginScope = options.MarginLeft != 0
                ? gc.BeginGroup(DrawingTransform.Translate(options.MarginLeft, 0))
                : null;
            try
            {
                foreach (var system in page.Systems)
                    DrawSystem(score, layout, system, resolver, beamedItems, gc);
                // Page-level overlays that span systems. The Y-anchor map is
                // built from THIS page's systems only: system Y is page-local
                // (each page restarts at MarginTop), so every overlay whose
                // measure lives on another page must be skipped on this one —
                // drawing it here would overprint this page's music at the
                // other page's local Y. Each drawer treats a missing measure
                // key as "not on this page".
                var measureToSystemY = BuildMeasureToSystemY(page);
                var measureToSystem = BuildMeasureToSystem(page);
                var os = new OssiaShrink(ossiaStaves, measureToSystem);
                DrawTies(layout, measureToSystemY, os, gc);
                DrawSlurs(layout, measureToSystemY, os, gc);
                DrawDynamics(layout, measureToSystemY, os, gc);
                DrawArticulations(layout, measureToSystemY, os, gc);
                DrawLyrics(layout, measureToSystemY, gc);
                DrawHairpins(layout, measureToSystemY, os, gc);
                DrawOttavaBrackets(layout, measureToSystemY, os, gc);
                DrawVoltaBrackets(layout, measureToSystemY, gc);
                DrawTupletBrackets(layout, measureToSystemY, os, gc);
                DrawTrillSpanners(layout, measureToSystemY, os, gc);
                DrawGlissandos(layout, measureToSystemY, os, gc);
                DrawArpeggios(layout, measureToSystemY, os, gc);
                DrawGraceNotes(layout, measureToSystemY, os, gc);
                DrawChordNames(layout, measureToSystemY, gc);
                DrawFiguredBass(layout, measureToSystemY, gc);
                DrawPercentRepeats(layout, measureToSystemY, gc);
                DrawBarNumbers(layout, measureToSystemY, gc);
                DrawStanzaNumbers(layout, measureToSystemY, gc);
                DrawFingerings(layout, measureToSystemY, os, gc);
                DrawMusicMarks(layout, measureToSystemY, gc);
                DrawCustomTexts(layout, measureToSystemY, gc);
                DrawTextSpanners(layout, measureToSystemY, os, gc);
                DrawPedalBrackets(layout, measureToSystemY, gc);
                DrawMultiMeasureRests(layout, measureToSystemY, gc);
                DrawTieVariants(layout, measureToSystemY, os, gc);
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

    // ---------- System ----------

    // F3/B: identify beamed notes by their POSITION (staff, voice, measure, item), not by the
    // MusicItem value. A value key includes SourcePosition, so on whole-layout reuse the
    // cached layout's beam members (built from the pre-edit score) no longer value-equal the
    // live score's notes (offsets shifted) — every beamed note would then be re-stemmed AND
    // beamed = double stems. The position key is offset-independent. The voice component keeps
    // a polyphonic staff's beams matched to the RIGHT voice (voice 2's eighths beam under it;
    // DetectBeamGroups now runs per voice) — without it a voice-1 beamed item would suppress a
    // voice-2 flag at the same (staff,measure,item) and vice versa.
    private static HashSet<(int Staff, int Voice, int Measure, int Item)> BuildBeamedItemsSet(ScoreLayout layout)
    {
        var set = new HashSet<(int, int, int, int)>();
        foreach (var beam in layout.BeamLayouts)
        {
            int staff = beam.StaffIndex < 0 ? 0 : beam.StaffIndex;
            foreach (var member in beam.Group.Members)
            {
                int measure = member.MeasureIndex >= 0 ? member.MeasureIndex : beam.Group.MeasureIndex;
                set.Add((staff, beam.Group.VoiceIndex, measure, member.ItemIndex));
            }
        }
        return set;
    }

    private static void DrawSystem(
        MultiStaffScore score, ScoreLayout layout,
        SystemLayout system, GrobPropertyResolver resolver,
        HashSet<(int Staff, int Voice, int Measure, int Item)> beamedItems, IDrawingContext gc)
    {
        bool isFirstSystem = system.SystemIndex == 0;
        double systemStartX = system.Indent;

        // System-start delimiters (brackets / bar lines connecting staves in a group).
        DrawSystemStartDelimiters(system, gc);

        // Instrument names within the indent area (drawn before staves so glyphs
        // overlap correctly when names are wider than the indent).
        DrawInstrumentNames(score, system, gc);

        // Staff lines end exactly at the final barline (the last measure's right
        // edge), so the staff never overshoots a ragged system nor falls short of a
        // justified one. (system.Width is the target width, not the drawn content.)
        double staffRight = system.Measures.Length > 0
            ? system.Measures[^1].X + system.Measures[^1].Width
            : system.Width;

        // An end-of-line courtesy key signature sits ON the staff after the
        // final barline — the staff lines extend over the reserved suffix
        // (tab staves keep the unextended width; they print no signatures).
        double notationStaffRight = staffRight;
        if (system.Measures.Length > 0
            && GetSystemEndKeyChange(score.PrimaryContentStaff, system) is { } eolCourtesy)
        {
            notationStaffRight += SpacingRules.KeyCourtesySuffixWidth(
                eolCourtesy.PreviousKey.Sharps, eolCourtesy.NewKey.Sharps);
        }

        // Left-edge system bar + span bars through grand-staff gaps.
        DrawStaffConnectors(score, layout, system, systemStartX, gc);

        // Lead-sheet score: every row is a text row (chords and/or lyrics, no
        // notation staff). Then there are no staff barlines, so draw the measure
        // barlines on the TOP text row — the chords/lyrics read as a measure grid
        // (chords sit between the barlines, lyrics hang below). A score with any
        // real staff keeps that staff's own barlines and leaves text rows bare.
        bool leadSheet = score.IsLeadSheet;
        // The grid goes on the top row (global index 0 is always the first staff)
        // — unless the sheet has BOTH a chord row and a lyric row: then the
        // barlines read best inside the LYRIC line (the words carry the
        // phrase; chord names flow un-fenced above), so the grid moves to the
        // first lyric row and the chord row stays bare.
        int barlineRowIdx = leadSheet ? 0 : -1;
        if (leadSheet)
        {
            var lyricRowIndices = score.Lyrics
                .Where(l => l.IsLyricsRow).Select(l => l.StaffIndex).ToHashSet();
            bool hasChordRow = score.EnumerateStaves().Any(t =>
                t.Staff.IsTextRow && !lyricRowIndices.Contains(t.GlobalStaffIndex));
            if (hasChordRow && lyricRowIndices.Count > 0)
                barlineRowIdx = lyricRowIndices.Min();
        }

        // Per-staff: staff lines + prefix glyphs + notes
        foreach (var (group, staff, globalIdx) in score.EnumerateStaves())
        {
            // Hara-kiri: a staff hidden in THIS system is absent from the
            // system's staff table, and FindStaffYInSystem would fall back to
            // the system top — drawing the hidden staff's clef/rests on top of
            // the first visible staff. Skip its content entirely.
            // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — suicided staves
            // print nothing for the system.
            if (!StaffPresentInSystem(system, globalIdx))
                continue;

            double staffY = LayoutUtilities.FindStaffYInSystem(system, globalIdx);
            bool isOssia = staff.IsOssia;

            // Independent text rows (chords / lyrics) draw no staff lines / clef /
            // notes — only their text, emitted by DrawChordNames / DrawLyrics at the row Y.
            if (staff.IsTextRow)
            {
                // Lead-sheet measure grid: the grid row's measures carry the real
                // barline types (synced from the chord/lyric source). On a LYRIC
                // row the bars run at the full staff height, like ordinary
                // barlines around the words (maintainer feedback: the short
                // ticks read as stray marks); a chords-only grid keeps the
                // short ticks the chord symbols hang on.
                if (leadSheet && globalIdx == barlineRowIdx)
                {
                    // The grid row is "a staff with the lines removed": its
                    // barlines run the ordinary staff height — extended by one
                    // verse-spacing per extra stacked verse on a lyric row —
                    // whether the grid carries words or chord symbols.
                    double h = StaffHeight + (staff.TextRowVerses - 1) * LyricVerseSpacing;
                    DrawBarlines(system, staff, staffY, layout, gc, barHeight: h);
                }
                continue;
            }

            // Ossia: the group transform shrinks the notation uniformly, and the
            // X-compensating wrapper puts every horizontal position back on the
            // score-wide spacing columns — so the ossia's notes align vertically
            // with the full-size staff it sits above, exactly like a magnified
            // staff in LP (one paper column per moment spans every staff).
            // LILYPOND-REF: lily/paper-column.cc, lily/spacing-spanner.cc;
            // ly/music-functions-init.ly magnifyStaff.
            IDisposable? groupScope = isOssia
                ? gc.BeginGroup(new DrawingTransform(0, staffY, OssiaScale, OssiaScale))
                : null;
            IDrawingContext sgc = isOssia ? new UnscaledXDrawingContext(gc, OssiaScale) : gc;

            // Ossia fragment extent: the ossia prints ONLY over the measures
            // where it has notes — LP instantiates the ossia context just for
            // the span, so staff lines and barlines exist nowhere else
            // (NR "Ossia staves"; lily/staff-symbol-engraver.cc — the
            // StaffSymbol spanner lives exactly as long as its context).
            int fragFrom = int.MinValue, fragTo = int.MaxValue;
            // Staff lines start at the system indent (instrument names sit
            // in the clean space left of it — LP: the StaffSymbol spans the
            // system, whose left edge IS the indent).
            double lineStartX = systemStartX, lineEndX = notationStaffRight;
            if (isOssia)
            {
                (fragFrom, fragTo) = OssiaFragment(staff, system);
                if (fragFrom < 0)
                {
                    groupScope?.Dispose();
                    continue; // rest-only system (hara-kiri normally hides it upstream)
                }
                foreach (var ml in system.Measures)
                {
                    if (ml.MeasureIndex == fragFrom) lineStartX = ml.X;
                    if (ml.MeasureIndex == fragTo) lineEndX = ml.X + ml.Width;
                }
            }

            try
            {
                double localStaffY = isOssia ? 0 : staffY;

                // Tablature staves: string lines + TAB clef + fret numbers.
                if (staff.IsTab)
                {
                    DrawTabStaff(staff, system, globalIdx, localStaffY, staffRight, systemStartX, beamedItems, sgc);
                    continue;
                }

                DrawStaffLines(localStaffY, lineEndX, sgc, lineStartX);

                // System-start prefix. The clef and key signature repeat at the
                // head of EVERY system (standard notation); the key reflects any
                // mid-piece change in force at this point. The time signature is
                // printed only at the very start — a mid-piece meter change is
                // drawn as a measure item, not a system prefix.
                // LILYPOND-REF: lily/break-align-engraver.cc — Clef + KeySignature
                // are break-aligned at every line start; TimeSignature is not.
                double prefixEndX = systemStartX;
                var clef = ResolveClef(staff, system, score);
                // Ossia prefix, per the LP ossia conventions (NR "Ossia staves"):
                // no time signature at all (\remove Time_signature_engraver), no
                // clef on the ossia's FIRST appearance (firstClef = ##f —
                // lily/clef-engraver.cc creates the clef only when a previous
                // clef exists or firstClef is true), and the key signature only
                // when the fragment starts at the system head (a mid-system
                // fragment opens bare).
                bool ossiaAtSystemStart = !isOssia
                    || (system.Measures.Length > 0 && fragFrom <= system.Measures[0].MeasureIndex);
                bool drawClef = !isOssia
                    || (ossiaAtSystemStart && OssiaAppearedBefore(layout, staff, system, globalIdx));
                if (drawClef)
                {
                    // Tag the clef with its declaration for click-to-jump, on the first
                    // line of a single-staff score: there it IS the declared clef (later
                    // lines may show a mid-piece change, which owns its own position),
                    // and a multi-staff score's per-staff clefs would all wrongly point
                    // at the one score-level position.
                    int clefPos = isFirstSystem && score.TotalStaffCount == 1 ? score.Header.Clef : 0;
                    using (SourceScope(sgc, clefPos))
                        prefixEndX = DrawClef(clef, systemStartX, localStaffY, sgc);
                }
                if (ossiaAtSystemStart)
                {
                    var activeKey = ResolveKeySignature(staff, system, score);
                    // Tag the key sig with its declaration on the first line only — there
                    // it IS the declared key; later lines may show a mid-piece change,
                    // which carries its own position via its measure item.
                    int keySigPos = isFirstSystem ? score.Header.Key : 0;
                    if (GetSystemStartKeyChange(staff, system) is { } startKeyChange)
                    {
                        // A key change at the line break: the new line opens
                        // with the NEW key (no crammed naturals in bar one).
                        activeKey = startKeyChange.NewKey;
                        keySigPos = startKeyChange.SourcePosition;
                    }
                    using (SourceScope(sgc, keySigPos))
                        prefixEndX = DrawKeySignature(activeKey, clef, prefixEndX, localStaffY, sgc);
                }
                if (!isOssia)
                {
                    if (isFirstSystem)
                    {
                        using (SourceScope(sgc, score.Header.Time))
                            prefixEndX = DrawTimeSignature(score.TimeSignature, prefixEndX, localStaffY, sgc);
                    }
                    else if (GetSystemStartTimeChange(staff, system) is { } startTimeChange)
                    {
                        // A meter change at the line break is part of the prefix.
                        prefixEndX = DrawTimeSignature(startTimeChange.NewTime, prefixEndX, localStaffY, sgc);
                    }
                }

                // Measures covered by a percent repeat print ONLY the sign:
                // LilyPond never typesets the repeated music (the % replaces
                // it); our unfold keeps the notes for playback, so the visual
                // pass must skip them on the repeat's own staff.
                // LILYPOND-REF: lily/percent-repeat-engraver.cc.
                var percentCovered = new HashSet<int>();
                foreach (var prItem in score.PercentRepeats)
                    if (prItem.StaffIndex == globalIdx)
                        percentCovered.Add(prItem.MeasureIndex);

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
                        system, layout, globalIdx, localStaffY, clef, resolver, beamedItems, sgc,
                        fragFrom, fragTo, percentCovered);
                }

                // Barlines (typed: single / double / final / repeat) per measure
                DrawBarlines(system, staff, localStaffY, layout, sgc,
                    fromMeasure: fragFrom, toMeasure: fragTo);

                // End-of-line courtesy cancellation + new key signature when
                // the NEXT line opens with a key change; the layouter reserved
                // this room after the final barline.
                // LILYPOND-REF: lily/key-engraver.cc +
                // explicitKeySignatureVisibility default all-visible — the
                // changed signature prints on BOTH sides of the break.
                if (!isOssia && GetSystemEndKeyChange(staff, system) is { } eolKeyChange
                    && system.Measures.Length > 0)
                {
                    var lastMl = system.Measures[^1];
                    DrawKeySignatureChange(eolKeyChange,
                        lastMl.X + lastMl.Width + 0.8, localStaffY, clef, sgc);
                }
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

    private static void DrawStaffLines(double staffY, double width, IDrawingContext gc, double startX = 0)
    {
        for (int i = 0; i < 5; i++)
        {
            double y = staffY + i;
            gc.DrawLine(startX, y, width, y, Color.Black, EngravingDefaults.StaffLineThickness);
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

        // One staff line per string, spaced stringSpace apart.
        for (int i = 0; i < stringCount; i++)
            gc.DrawLine(0, staffY + i * stringSpace, staffRight, staffY + i * stringSpace,
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
        foreach (var ml in system.Measures)
        {
            if (ml.MeasureIndex >= primaryVoice.Measures.Length)
                continue;
            var measure = primaryVoice.Measures[ml.MeasureIndex];
            if (measure.StartBarline != BarlineType.None)
                DrawBarline(measure.StartBarline, ml.X, staffY, tabHeight, gc, tabDots: tabDots);
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
            double itemX = useColumnTiming
                ? ml.X + ml.GetXForTiming(currentTiming)
                : (i < ml.Items.Length ? ml.X + ml.Items[i].X : ml.X);
            currentTiming += item.Duration;

            // Match this position against the beamed set (offset-independent),
            // keyed by voice so each voice's beams suppress only its own flags.
            bool isBeamed = beamedItems.Contains((staffIndex, voiceNumber - 1, ml.MeasureIndex, i));

            switch (item)
            {
                case NoteItem note:
                    // A tie's destination keeps its rhythm (stem/beam) but hides
                    // its fret number — the held string is not re-struck.
                    if (!note.IsTieTarget)
                        DrawTabNote(note.Midi, itemX, staffY,
                            tuning, note.StringNumber, octaveShift, stringSpace, note.SourcePosition, gc, note.IsDead);
                    DrawUnbeamedTabStem(note, note.BaseDuration, note.StemUp,
                        itemX, staffY, staff, isBeamed, gc);
                    break;
                case ChordItem chord:
                    DrawTabChord(chord, itemX, staffY, tuning, octaveShift, stringSpace, gc);
                    DrawUnbeamedTabStem(chord, chord.BaseDuration, chord.StemUp,
                        itemX, staffY, staff, isBeamed, gc);
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
        bool isBeamed, IDrawingContext gc)
    {
        int noteValue = baseDuration.Denominator;
        if (baseDuration.Numerator != 1) noteValue = 1;
        if (noteValue < 2 || isBeamed)
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

    /// <summary>
    /// Converts a staff position and accidental back to a MIDI note number
    /// (staff position 0 = middle C = MIDI 60).
    /// </summary>
    private static int StaffPositionToMidi(int staffPosition, string? accidental)
    {
        int step = ((staffPosition % 7) + 7) % 7;
        int octave = 4 + (staffPosition - step) / 7;

        int alteration = accidental switch
        {
            "sharp" => 1,
            "flat" => -1,
            "doubleSharp" => 2,
            "doubleFlat" => -2,
            _ => 0
        };

        return Semantics.RelativeOctave.StepToMidi(step, alteration, octave);
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

    /// <summary>
    /// Returns the key-signature change that OPENS the first measure of a
    /// (non-first) system, or null. Such a change lands exactly at the line
    /// break and prints as the NEW key in the system prefix; the cancellation
    /// belongs to the previous line, not to bar one of the new line.
    /// LILYPOND-REF: lily/break-align-engraver.cc — KeySignature is
    /// break-aligned at every line start.
    /// </summary>
    private static KeySignatureChangeItem? GetSystemStartKeyChange(Staff staff, SystemLayout system)
    {
        if (system.SystemIndex == 0 || system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return null;

        var voice = staff.PrimaryVoice;
        int firstMeasureIndex = system.Measures[0].MeasureIndex;
        if (firstMeasureIndex >= voice.Measures.Length)
            return null;

        foreach (var item in voice.Measures[firstMeasureIndex].Items)
        {
            if (item is KeySignatureChangeItem kc)
                return kc;
            if (item.Duration > Fraction.Zero)
                break; // a note/rest before any change → not measure-opening
        }
        return null;
    }

    /// <summary>
    /// Returns the key change that opens the measure AFTER this system's last
    /// one (i.e. the first measure of the next line), or null — the trigger
    /// for the end-of-line courtesy cancellation + signature.
    /// </summary>
    private static KeySignatureChangeItem? GetSystemEndKeyChange(Staff staff, SystemLayout system)
    {
        if (system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return null;
        var voice = staff.PrimaryVoice;
        int nextMeasureIndex = system.Measures[^1].MeasureIndex + 1;
        if (nextMeasureIndex >= voice.Measures.Length)
            return null;
        foreach (var item in voice.Measures[nextMeasureIndex].Items)
        {
            if (item is KeySignatureChangeItem kc)
                return kc;
            if (item.Duration > Fraction.Zero)
                break;
        }
        return null;
    }

    /// <summary>True when this key change is the one folded into its system's
    /// start prefix (see GetSystemStartKeyChange) — the per-measure pass must
    /// not draw it again.</summary>
    private static bool IsSystemStartKeyChange(
        Voice voice, SystemLayout system, int measureIndex, KeySignatureChangeItem kc)
    {
        if (system.SystemIndex == 0 || system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return false;
        if (measureIndex != system.Measures[0].MeasureIndex || measureIndex >= voice.Measures.Length)
            return false;
        foreach (var item in voice.Measures[measureIndex].Items)
        {
            if (ReferenceEquals(item, kc))
                return true;
            if (item.Duration > Fraction.Zero)
                return false;
        }
        return false;
    }

    private static double DrawClef(ClefType clef, double x, double staffY, IDrawingContext gc)
    {
        char glyph = clef switch
        {
            ClefType.Bass or ClefType.Bass8Below => EmmentalerGlyphs.FClef,
            ClefType.Alto or ClefType.Tenor or ClefType.Soprano
                or ClefType.MezzoSoprano or ClefType.Baritone => EmmentalerGlyphs.CClef,
            ClefType.Percussion => EmmentalerGlyphs.PercussionClef,
            _ => EmmentalerGlyphs.GClef,
        };
        // Y baseline matches LP positioning: the clef glyph anchors on the line
        // it names (G / F / C); percussion centres on the middle line.
        double clefY = clef switch
        {
            ClefType.Bass or ClefType.Bass8Below => staffY + 1,
            ClefType.Alto or ClefType.Percussion => staffY + 2,
            ClefType.Tenor => staffY + 1,
            ClefType.Soprano => staffY + 4,       // C4 on the bottom line
            ClefType.MezzoSoprano => staffY + 3,  // C4 on line 2
            ClefType.Baritone => staffY + 0,      // C4 on the top line
            _ => staffY + 3,
        };
        gc.DrawGlyph(glyph, x + 0.3, clefY, FontSize);
        if (clef is ClefType.Treble8Below or ClefType.Bass8Below)
            DrawClefModifier8(x + 0.3, staffY, change: false, gc);
        else if (clef == ClefType.Treble8Above)
            DrawClefModifier8(x + 0.3, staffY, change: false, gc, above: true);
        return x + 0.3 + 3.0;  // approximate clef width + padding
    }

    /// <summary>
    /// Draws the octavation modifier digit "8" beneath a <c>treble_8</c> clef.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:944-975 (ClefModifier grob) +
    /// scm/output-lib.scm:3972-3989 (clef-modifier::print). LilyPond draws the
    /// modifier as italic text centred on the clef (self-alignment CENTER, with a
    /// small leftward nudge from clef-alignments <c>(G . (-0.2 . 0.1))</c>) and
    /// placed below the staff (direction DOWN, staff-padding 0.7). Lily# has no
    /// glyph-metric measurement, so the horizontal centre and vertical drop are
    /// constants calibrated to LilyPond 2.24 output (staff-space pixel probe of a
    /// treble_8 render): the digit is ~2 ss tall, its top ~0.3 ss below the bottom
    /// staff line, centred ~0.1 ss left of the clef's vertical axis. The mid-music
    /// _change clef uses a smaller glyph, so the digit and its offset shrink to
    /// match (LP applies a font-size dampening for change clefs).
    /// </remarks>
    private static void DrawClefModifier8(double clefGlyphX, double staffY, bool change, IDrawingContext gc, bool above = false)
    {
        double scale = change ? 0.85 : 1.0;
        double centerX = clefGlyphX + 1.1 * scale; // under the clef's descender (slightly left of the stem)
        // Below: clears the clef's lower curl. Above (treble^8): clears the
        // G clef's upper hook symmetrically.
        double centerY = above ? staffY - 3.2 : staffY + 5.6;
        double size = FontSize * 0.80 * scale;     // digit ~2 ss tall, matching LP
        gc.DrawText("8", centerX, centerY, size, "serif",
            FontStyle.Italic, TextAnchor.Middle, Color.Black, VerticalAnchor.Middle);
    }

    // ---------- Time signature ----------

    private static double DrawTimeSignature(TimeSignature ts, double x, double staffY, IDrawingContext gc)
    {
        // Senza misura: unmeasured music prints NO signature.
        if (ts.SenzaMisura)
            return x;
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
        // Additive meters print the numerator AS WRITTEN ("3+2" over 8), the
        // rows centered on each other. LILYPOND-REF: \compoundMeter numerator.
        var num = ts.BeatsText ?? ts.Beats.ToString();
        var den = ts.BeatType.ToString();
        const double digitW = 1.4, plusW = 1.1;
        double NumWidth(string s)
        {
            double w = 0;
            foreach (var ch in s) w += ch == '+' ? plusW : digitW;
            return w;
        }
        double numWidth = NumWidth(num), denWidth = NumWidth(den);
        double total = Math.Max(numWidth, denWidth);
        double nx = x + (total - numWidth) / 2;
        foreach (var ch in num)
        {
            if (ch == '+')
            {
                gc.DrawText("+", nx + plusW / 2, staffY + 1 + digitHalfHeight - 0.55,
                    2.4, "serif", FontStyle.Bold, TextAnchor.Middle, Color.Black);
                nx += plusW;
            }
            else
            {
                gc.DrawGlyph(EmmentalerGlyphs.GetTimeSigDigit(ch - '0'),
                    nx, staffY + 1 + digitHalfHeight, FontSize);
                nx += digitW;
            }
        }
        double dnx = x + (total - denWidth) / 2;
        foreach (var ch in den)
        {
            gc.DrawGlyph(EmmentalerGlyphs.GetTimeSigDigit(ch - '0'),
                dnx, staffY + 3 + digitHalfHeight, FontSize);
            dnx += digitW;
        }
        return x + total + 0.4;
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
        // Non-traditional signature: draw the written (step, alter) pairs in
        // print order, each on the position the standard tables would give
        // that step for its sign. LILYPOND-REF: keyAlterations; MusicXML
        // non-traditional <key-step>/<key-alter> pairs.
        if (key.Custom is { } custom)
        {
            foreach (var (step, alter) in KeySignature.DecodeCustom(custom))
            {
                string kind = alter switch
                {
                    2 => "doubleSharp",
                    1 => "sharp",
                    -1 => "flat",
                    -2 => "doubleFlat",
                    _ => "natural",
                };
                int staffPosition = KeySigStaffPositionForStep(clef, alter >= 0, step);
                double y = staffY + StaffHeight / 2 - staffPosition * 0.5;
                gc.DrawGlyph(EmmentalerGlyphs.AccidentalGlyph(kind), x, y, FontSize);
                x += GlyphMetrics.GetKeySignatureAccidentalWidth(alter >= 0);
            }
            return x + 0.4;
        }

        if (key.Sharps == 0) return x;

        bool isSharps = key.Sharps > 0;
        char glyph = isSharps ? EmmentalerGlyphs.AccidentalSharp : EmmentalerGlyphs.AccidentalFlat;
        int[] positions = isSharps ? KeySigSharpPositions : KeySigFlatPositions;
        int[] steps = isSharps ? KeySigSharpSteps : KeySigFlatSteps;

        int n = Math.Min(Math.Abs(key.Sharps), 7);

        double accidentalWidth = GlyphMetrics.GetKeySignatureAccidentalWidth(isSharps);
        for (int i = 0; i < n; i++)
        {
            int staffPosition = KeySigStaffPosition(clef, isSharps, i);
            double y = staffY + StaffHeight / 2 - staffPosition * 0.5;
            gc.DrawGlyph(glyph, x, y, FontSize);
            x += accidentalWidth;
        }
        return x + 0.4;
    }

    /// <summary>
    /// Kerning between two adjacent cancellation naturals: their vertical
    /// edge intervals (previous glyph's LEFT side = its span shifted +3)
    /// overlap → 0.3; just touch → 0.15; clear → 0.
    /// LILYPOND-REF: lily/key-signature-interface.cc natural kerning.
    /// </summary>
    private static double NaturalKernPadding(int prevPos, int curPos)
    {
        double lo1 = 2 * prevPos - 3, hi1 = 2 * prevPos + 6; // prev, left side
        double lo2 = 2 * curPos - 6, hi2 = 2 * curPos + 3;   // current, right side
        double lo = Math.Max(lo1, lo2), hi = Math.Min(hi1, hi2);
        if (lo > hi) return 0;
        return hi > lo ? 0.3 : 0.15;
    }

    /// <summary>
    /// Staff position of the i-th key-signature accidental for a clef.
    /// LILYPOND-REF: scm/music-functions.scm key-signature-interface —
    /// staffPosition = hi − modulo(hi − (c0 + step), 7).
    /// </summary>
    /// <summary>Key-signature staff position for an ARBITRARY step (custom
    /// signatures) — same octave-choice tables, indexed by step instead of the
    /// standard order. LILYPOND-REF: key-signature-interface alteration-position.</summary>
    /// <summary>The (step, alter) pairs of a STANDARD key signature in print
    /// order (sharps F C G D A E B / flats B E A D G C F).</summary>
    private static List<(int Step, int Alter)> StandardKeySteps(int sharps)
    {
        var list = new List<(int, int)>();
        int n = Math.Min(Math.Abs(sharps), 7);
        int[] steps = sharps > 0 ? KeySigSharpSteps : KeySigFlatSteps;
        for (int i = 0; i < n; i++)
            list.Add((steps[i], Math.Sign(sharps)));
        return list;
    }
    private static int KeySigStaffPositionForStep(ClefType clef, bool sharpish, int step)
    {
        int c0Position = clef switch
        {
            ClefType.Bass or ClefType.Bass8Below => 6,
            ClefType.Alto or ClefType.Percussion => 0,
            ClefType.Tenor => 2,
            ClefType.Soprano => -4,
            ClefType.MezzoSoprano => -2,
            ClefType.Baritone => 4,
            _ => -6,
        };
        int cPos = ((c0Position % 7) + 7) % 7;
        int[] positions = sharpish ? KeySigSharpPositions : KeySigFlatPositions;
        int hi = positions[cPos];
        int diff = hi - (cPos + step);
        return hi - (((diff % 7) + 7) % 7);
    }

    private static int KeySigStaffPosition(ClefType clef, bool isSharps, int index)
    {
        int c0Position = clef switch
        {
            ClefType.Bass or ClefType.Bass8Below => 6,
            ClefType.Alto or ClefType.Percussion => 0,
            ClefType.Tenor => 2,
            ClefType.Soprano => -4,
            ClefType.MezzoSoprano => -2,
            ClefType.Baritone => 4,
            _ => -6, // treble (and treble_8)
        };
        int cPos = ((c0Position % 7) + 7) % 7;
        int[] positions = isSharps ? KeySigSharpPositions : KeySigFlatPositions;
        int[] steps = isSharps ? KeySigSharpSteps : KeySigFlatSteps;
        int hi = positions[cPos];
        int diff = hi - (cPos + steps[index]);
        int modDiff = ((diff % 7) + 7) % 7;
        return hi - modDiff;
    }

    // ---------- Notes & rests per staff ----------

    private static void DrawStaffMeasures(
        Voice voice, int voiceNumber, bool? forcedStemUp,
        SystemLayout system, ScoreLayout layout, int staffIndex,
        double staffY, ClefType clef, GrobPropertyResolver resolver,
        HashSet<(int Staff, int Voice, int Measure, int Item)> beamedItems, IDrawingContext gc,
        int fragmentFrom = int.MinValue, int fragmentTo = int.MaxValue,
        HashSet<int>? percentCovered = null)
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
        foreach (var (item, ledgerMl, _, itemX) in EnumerateStaffItems(voice, voiceNumber, system, layout, fragmentFrom, fragmentTo))
        {
            // Percent-covered measures draw no notes — and no ledgers either.
            if (percentCovered != null && percentCovered.Contains(ledgerMl.MeasureIndex))
                continue;
            CollectItemLedgers(item, itemX, staffMiddleY, ledgerPlan);
        }
        DrawPlannedLedgers(ledgerPlan, gc);

        foreach (var (item, ml, itemIdx, itemX) in EnumerateStaffItems(voice, voiceNumber, system, layout, fragmentFrom, fragmentTo))
        {
            // Head-wipe when this voice's notehead merges with another's.
            bool headWiped = layout.IsHeadWiped(ml.MeasureIndex, voiceNumber, itemIdx);

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
                        forcedStemUp, headWiped, gc);
                    break;
                case RestItem rest:
                    // A spacer rest ('s') reserves its column width but is never
                    // drawn. Measures inside a multi-measure-rest run get their
                    // symbol from DrawMultiMeasureRests (church rest or H-bar);
                    // drawing the per-measure whole rest too would double-print.
                    // LILYPOND-REF: lily/multi-measure-rest.cc — the MMR spanner
                    // replaces the individual rests.
                    if (!rest.IsSpacer && !IsMmrCovered(layout, ml.MeasureIndex))
                        DrawRest(rest, itemX, staffY, gc);
                    break;
                case ChordItem chord:
                    DrawChord(chord, itemX, staffMiddleY, resolver,
                        beamedItems.Contains((staffIndex, voiceNumber - 1, ml.MeasureIndex, itemIdx)),
                        forcedStemUp, headWiped, gc);
                    break;
                case ClefChangeItem clefChange:
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
    private static IEnumerable<(MusicItem Item, MeasureLayout Ml, int ItemIdx, double ItemX)>
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
                int noteValue = GlyphMetrics.NoteValueOf(note.BaseDuration);
                double headWidth = GlyphMetrics.GetNoteheadAdvance(noteValue) * (note.IsCue ? 0.66 : 1.0);
                CollectLedgerRequest(ledgerPlan, note.StaffPosition, x, headWidth,
                    staffMiddleY, note.Accidental != null);
                break;
            }
            case ChordItem chord when chord.Notes.Length > 0:
            {
                int noteValue = GlyphMetrics.NoteValueOf(chord.BaseDuration);
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

    /// <summary>Stem start offset from the head CENTER for styled noteheads:
    /// cross/slash ink only reaches the attach edge at its CORNERS, so the stem
    /// joins the corner on the stem's side (±½ss); the do-triangle's corners
    /// are both at the bottom. Round heads attach at center (0).
    /// LILYPOND-REF: mf/feta-noteheads.mf stem_attachment per head style.</summary>
    private static double StemAttachYOffset(NoteheadStyle style, bool stemUp) => style switch
    {
        NoteheadStyle.Cross or NoteheadStyle.Slash => stemUp ? -0.5 : 0.5,
        NoteheadStyle.Triangle => 0.5,
        _ => 0,
    };

    private static void DrawNote(NoteItem note, double x, double staffMiddleY,
        GrobPropertyResolver resolver, bool isBeamed, bool? forcedStemUp, bool headWiped,
        IDrawingContext gc)
    {
        int noteValue = GlyphMetrics.NoteValueOf(note.BaseDuration);
        double noteY = StaffFrame.PositionToDevice(note.StaffPosition, staffMiddleY);
        // Cue notes scale to ~0.66× (LP CueVoice fontSize = -4 → magstep(-4)).
        // LILYPOND-REF: ly/engraver-init.ly CueVoice — fontSize = #-4
        double noteFontSize = note.IsCue ? FontSize * 0.66 : FontSize;

        // Voice stem direction override (voice 1 up / voice 2 down); falls back
        // to the note's own position-based default in single-voice staves.
        bool stemUp = forcedStemUp ?? note.StemUp;

        // Accidental (left of notehead). Cue notes scale their accidental with
        // the head (LP CueVoice fontSize = -4 reduces the accidental grob too).
        if (note.Accidental != null)
            DrawAccidental(note.Accidental, note.IsCourtesy, x, noteY, note.SourcePosition, gc,
                note.IsCue ? 0.66 : 1.0);

        // Notehead — skipped when this head merges with another voice's (head wipe)
        // or when NoteHead.transparent is overridden.
        // LILYPOND-REF: lily/note-collision.cc:381-407
        // LILYPOND-REF: lily/grob-property.cc — NoteHead.transparent
        Color? noteheadColor = ResolveColor(resolver, "NoteHead");
        bool headTransparent = resolver.GetBool("NoteHead", "transparent") == true;
        char head = EmmentalerGlyphs.GetNotehead(note.Notehead, noteValue);
        if (!headWiped && !headTransparent)
            using (gc.Source(note.SourcePosition))
            {
                if (note.IsDead)
                    DrawDeadNotehead(x, noteY, noteheadColor, gc);
                else
                    gc.DrawGlyph(head, x, noteY, noteFontSize, noteheadColor);
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
            gc.DrawLine(stemX, noteY + StemAttachYOffset(note.Notehead, stemUp),
                stemX, stemEndY,
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
        int noteValue = GlyphMetrics.NoteValueOf(chord.BaseDuration);
        char head = EmmentalerGlyphs.GetNotehead(chord.Notehead, noteValue);
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
        // Cue chords scale the whole accidental column (placement widths/paddings
        // AND glyphs) by the head scale — LP runs the cue grobs at fontSize -4, so
        // the accidentals shrink and pack closer together, as a pair.
        var accLayouts = AccidentalColumn.CalculatePositions(chord.Notes, headOffsets, headScale);
        foreach (var al in accLayouts)
        {
            double ay = StaffFrame.PositionToDevice(al.StaffPosition, staffMiddleY);
            DrawAccidentalAtInkLeft(al.Accidental, al.IsCourtesy,
                x + al.XOffset, ay, chord.SourcePosition, gc, headScale);
        }

        double topY = double.MaxValue, bottomY = double.MinValue;
        int maxPos = int.MinValue, minPos = int.MaxValue;
        for (int i = 0; i < chord.Notes.Length; i++)
        {
            var n = chord.Notes[i];
            double y = StaffFrame.PositionToDevice(n.StaffPosition, staffMiddleY);
            // A drum chord mixes heads per member (bd default, hh cross).
            char memberHead = n.Notehead != NoteheadStyle.Default
                ? EmmentalerGlyphs.GetNotehead(n.Notehead, noteValue)
                : head;
            if (!headWiped && !headTransparent)
                using (gc.Source(chord.SourcePosition))
                    gc.DrawGlyph(memberHead, x + headOffsets[i], y, noteFontSize, noteheadColor);
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
            double stemStartY = (stemUp ? bottomY : topY)
                + StemAttachYOffset(chord.Notehead, stemUp);
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
            + (StaffFrame.PositionToDevice(pos, staffMiddleY) - staffMiddleY) * unit;

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
        ScoreLayout layout, IDrawingContext gc, double? barHeight = null,
        int fromMeasure = int.MinValue, int toMeasure = int.MaxValue)
    {
        // A lead-sheet text row has no staff, so its barlines are short ticks the
        // chord/lyric row hangs on; a real staff uses its full height.
        double height = barHeight ?? StaffHeight;
        var voice = staff.PrimaryVoice;
        foreach (var ml in system.Measures)
        {
            // Ossia fragment trim: no barlines where no staff exists.
            if (ml.MeasureIndex < fromMeasure || ml.MeasureIndex > toMeasure)
                continue;
            if (ml.MeasureIndex >= voice.Measures.Length)
                continue;
            var measure = voice.Measures[ml.MeasureIndex];

            // Start barline (e.g. repeat-start) at the measure's left edge.
            if (measure.StartBarline != BarlineType.None)
                DrawBarline(measure.StartBarline, ml.X, staffY, height, gc);

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
            DrawBarline(measure.EndBarline, endX - width, staffY, height, gc);
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
        IDrawingContext gc, bool withDots = true, (double Y1, double Y2)? tabDots = null)
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

            case BarlineType.Dashed:
            {
                // LILYPOND-REF: lily/bar-line.cc make_dashed_bar_line — dash
                // length tuned so segments straddle the staff lines evenly
                // (~⅔ dash, ⅓ gap per staff space).
                const double dash = 0.67, gap = 0.33;
                for (double dy = 0; dy < height; dy += dash + gap)
                    gc.DrawRectangle(x, staffY + dy, thin,
                        Math.Min(dash, height - dy), fill: Color.Black);
                break;
            }

            case BarlineType.Final:
                gc.DrawRectangle(x, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(x + thin + sep, staffY, thick, height, fill: Color.Black);
                break;

            case BarlineType.RepeatStart:
                gc.DrawRectangle(x, staffY, thick, height, fill: Color.Black);
                gc.DrawRectangle(x + thick + sep, staffY, thin, height, fill: Color.Black);
                if (withDots) DrawRepeatDots(x + thick + sep + thin + dotSep, staffY, gc, tabDots);
                break;

            case BarlineType.RepeatEnd:
                if (withDots) DrawRepeatDots(x, staffY, gc, tabDots);
                double afterDots = x + dotsOffset;
                gc.DrawRectangle(afterDots, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(afterDots + thin + sep, staffY, thick, height, fill: Color.Black);
                break;

            case BarlineType.RepeatBoth:
                if (withDots) DrawRepeatDots(x, staffY, gc, tabDots);
                double pos = x + dotsOffset;
                gc.DrawRectangle(pos, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(pos + thin + sep, staffY, thick, height, fill: Color.Black);
                gc.DrawRectangle(pos + thin + sep + thick + sep, staffY, thin, height, fill: Color.Black);
                if (withDots) DrawRepeatDots(pos + thin + sep + thick + sep + thin + dotSep, staffY, gc, tabDots);
                break;
        }
    }

    private static void DrawRepeatDots(double x, double staffY, IDrawingContext gc,
        (double Y1, double Y2)? tabDots = null)
    {
        double r = EngravingDefaults.RepeatDotRadius;
        // On a tab staff the dots straddle the centre, each centred in a string
        // space (passed in); otherwise the notation 2nd/3rd-space positions.
        double y1 = tabDots?.Y1 ?? EngravingDefaults.RepeatDotPosition1;
        double y2 = tabDots?.Y2 ?? EngravingDefaults.RepeatDotPosition2;
        gc.DrawCircle(x + r, staffY + y1, r, Color.Black);
        gc.DrawCircle(x + r, staffY + y2, r, Color.Black);
    }

    /// <summary>Total horizontal extent of a barline glyph (for right-edge alignment).</summary>
    // The drawn extent and the reserved spacing width are the same quantity;
    // both come from EngravingDefaults.BarlineDrawnWidth so they cannot drift.
    private static double GetVisualBarlineWidth(BarlineType type)
        => EngravingDefaults.BarlineDrawnWidth(type);

    // ---------- Beams ----------

    private static void DrawBeams(MultiStaffScore score, ScoreLayout layout, SystemLayout system, IDrawingContext gc)
    {
        var staffByIndex = score.EnumerateStaves().ToDictionary(s => s.GlobalStaffIndex, s => s.Staff);
        // Beams whose notes are hidden under a percent sign are hidden too.
        var percentByStaff = new HashSet<(int Staff, int Measure)>();
        foreach (var prItem in score.PercentRepeats)
            percentByStaff.Add((prItem.StaffIndex, prItem.MeasureIndex));
        foreach (var beam in layout.BeamLayouts)
        {
            // Only draw beams whose first measure is in this system
            bool inSystem = system.Measures.Any(m => m.MeasureIndex == beam.Group.MeasureIndex);
            if (!inSystem) continue;
            if (percentByStaff.Contains((Math.Max(0, beam.StaffIndex), beam.Group.MeasureIndex)))
                continue;

            var grp = beam.Group;

            // The quanter's Y positions are staff positions relative to the
            // beam's OWN staff middle — resolve that staff in this system
            // (multi-staff scores; -1 = single staff = the system's first).
            double staffY = beam.StaffIndex >= 0
                ? LayoutUtilities.FindStaffYInSystem(system, beam.StaffIndex)
                : system.Y;

            // Ossia beams get the same treatment as the ossia staff pass: a
            // uniform-scale group anchored at the staff's Y with X compensated
            // back onto the shared columns — stems, beam thickness and slope
            // all shrink with the notation (LP: the beam belongs to the
            // magnified staff's grobs). All Ys below are then staff-LOCAL.
            var beamStaff = beam.StaffIndex >= 0
                && staffByIndex.TryGetValue(beam.StaffIndex, out var bst) ? bst : null;
            bool ossiaBeam = beamStaff?.IsOssia == true;
            IDisposable? ossiaScope = null;
            IDrawingContext bgc = gc;
            if (ossiaBeam)
            {
                ossiaScope = gc.BeginGroup(new DrawingTransform(0, staffY, OssiaScale, OssiaScale));
                bgc = new UnscaledXDrawingContext(gc, OssiaScale);
                staffY = 0;
            }
            double staffMiddleY = staffY + StaffHeight / 2;
            try
            {

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
            DrawBeamSegment(leftStemX, leftBeamY, rightStemX, rightBeamY, bgc);

            // Secondary beams (16th+) stack toward the noteheads of the beam's
            // overall direction. Each level draws full segments between adjacent
            // members that both carry the beam, plus short partial beams (beamlets)
            // for members that carry it in isolation — e.g. the 16th in a
            // dotted-8th + 16th pair, whose second beam is a left-pointing stub.
            // LILYPOND-REF: lily/beam.cc Beam::print / fractional (stub) beams.
            int maxBeamCount = grp.Members.Max(m => m.BeamCount);
            for (int level = 1; level < maxBeamCount; level++)
            {
                double offset = level * EngravingDefaults.BeamTranslation;
                if (!grp.StemUp) offset = -offset;
                double beamSpanX = rightStemX - leftStemX;
                double BeamYAt(double x) => leftBeamY + offset +
                    (beamSpanX > 0.001 ? (x - leftStemX) / beamSpanX : 0) * (rightBeamY - leftBeamY);

                for (int i = 0; i < grp.Members.Length; i++)
                {
                    if (grp.Members[i].BeamCount <= level) continue;
                    bool rightFull = i < grp.Members.Length - 1 && grp.Members[i + 1].BeamCount > level;
                    bool leftFull = i > 0 && grp.Members[i - 1].BeamCount > level;

                    if (rightFull)
                    {
                        // Full segment i -> i+1 (drawn once, from its left member).
                        double xa = StemAttachX(i);
                        double xb = StemAttachX(i + 1);
                        DrawBeamSegment(xa, BeamYAt(xa), xb, BeamYAt(xb), bgc);
                    }
                    else if (!leftFull)
                    {
                        // Isolated at this level: a beamlet (fractional beam) stub.
                        // It points back toward the previous note; the first note of
                        // the group points forward instead.
                        double x0 = StemAttachX(i);
                        double x1 = x0 + (i > 0 ? -EngravingDefaults.BeamletLength : EngravingDefaults.BeamletLength);
                        DrawBeamSegment(x0, BeamYAt(x0), x1, BeamYAt(x1), bgc);
                    }
                    // else (leftFull && !rightFull): this member is the right end of a
                    // full segment already drawn from i-1; nothing more to do.
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
                    // Ossia beams never cross staves: every member sits on the
                    // ossia's own (local) frame.
                    double memberStaffMiddleY = !ossiaBeam && memberStaffIdx >= 0
                        ? LayoutUtilities.FindStaffYInSystem(system, memberStaffIdx) + StaffHeight / 2
                        : staffMiddleY;
                    headY = memberStaffMiddleY - GetMemberStaffPosition(member, up) * 0.5
                        + StemAttachYOffset(member.Item switch
                        {
                            NoteItem n => n.Notehead,
                            ChordItem ch => ch.Notehead,
                            _ => NoteheadStyle.Default,
                        }, up);
                }
                bgc.DrawLine(stemX, headY, stemX, beamY,
                    Color.Black, EngravingDefaults.StemThickness);
            }
            }
            finally
            {
                ossiaScope?.Dispose();
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
        int octaveShift = Tunings.OctaveShift(tuningType, staff.TabSourceClef);
        int[] tuning = Tunings.GetTuning(tuningType);

        int midi = 0;
        int? stringNumber = null;
        int? chordStringNum = null;
        switch (item)
        {
            case NoteItem n:
                midi = n.Midi; stringNumber = n.StringNumber;
                break;
            case ChordItem c when c.Notes.Length > 0:
                // On a tab the digits stack by STRING, so the stem must meet the
                // END of the stack in its direction — the TOP digit (smallest
                // string number) for an up-stem, the BOTTOM for a down-stem. The
                // strings come from the SAME exclusive allocation the drawn
                // chord uses, or the stem could anchor on a digit that moved.
                var chordAlloc = Tunings.CalculateChordFrets(
                    c.Notes.Select(x => (x.Midi + octaveShift, x.StringNumber)).ToList(), tuning);
                int headIdx = 0;
                for (int ci = 1; ci < chordAlloc.Length; ci++)
                {
                    if (stemUp
                        ? chordAlloc[ci].stringNum < chordAlloc[headIdx].stringNum
                        : chordAlloc[ci].stringNum > chordAlloc[headIdx].stringNum)
                        headIdx = ci;
                }
                chordStringNum = chordAlloc[headIdx].stringNum;
                break;
        }

        int stringNum = chordStringNum
            ?? Tunings.CalculateFret(midi + octaveShift, tuning, stringNumber ?? 0).stringNum;
        double stringSpace = EngravingDefaults.TabStringSpace(Tunings.GetStringCount(tuningType));
        double digitY = tabStaffTopY + (stringNum - 1) * stringSpace;
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
        int sourcePosition, IDrawingContext gc, double scale = 1.0)
    {
        char glyph = EmmentalerGlyphs.AccidentalGlyph(accidentalKind);
        var accBBox = GlyphMetrics.GetAccidentalBBox(accidentalKind);
        // Cue columns pass scale < 1: the glyph AND its bbox-derived offsets shrink
        // together, matching the (already scaled) X from AccidentalPlacement.
        double fs = FontSize * scale;

        if (isCourtesy)
        {
            // Same paren assembly as DrawAccidental, anchored at the ink left.
            // LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize()
            var leftParen = GlyphMetrics.AccidentalLeftParen;
            var rightParen = GlyphMetrics.AccidentalRightParen;
            double accInkLeft = inkLeftX + leftParen.Width * scale;
            using (gc.Source(sourcePosition))
            {
                gc.DrawGlyph(EmmentalerGlyphs.AccidentalLeftParen,
                    accInkLeft - leftParen.Right * scale, noteheadY, fs);
                gc.DrawGlyph(glyph, accInkLeft - accBBox.Left * scale, noteheadY, fs);
                gc.DrawGlyph(EmmentalerGlyphs.AccidentalRightParen,
                    accInkLeft + accBBox.Width * scale - rightParen.Left * scale, noteheadY, fs);
            }
        }
        else
        {
            using (gc.Source(sourcePosition))
                gc.DrawGlyph(glyph, inkLeftX - accBBox.Left * scale, noteheadY, fs);
        }
    }

    private static void DrawAccidental(
        string accidentalKind, bool isCourtesy, double noteheadX, double noteheadY,
        int sourcePosition, IDrawingContext gc, double scale = 1.0)
    {
        char glyph = EmmentalerGlyphs.AccidentalGlyph(accidentalKind);
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

    private static void DrawTies(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        foreach (var tie in layout.TieLayouts)
        {
            if (!sysY.ContainsKey(tie.Tie.StartMeasureIndex))
                continue; // not on this page (geometry is page-local)
            DrawBow(tie.StartX, tie.StartY, tie.EndX, tie.EndY,
                tie.Control1, tie.Control2, tie.CurveUp,
                EngravingDefaults.TieMidThickness,
                tie.StaffIndex, tie.Tie.StartMeasureIndex, os, gc);
        }
    }

    private static void DrawSlurs(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        foreach (var slur in layout.SlurLayouts)
        {
            if (!sysY.ContainsKey(slur.Slur.StartMeasureIndex))
                continue; // not on this page (geometry is page-local)
            DrawBow(slur.StartX, slur.StartY, slur.EndX, slur.EndY,
                slur.Control1, slur.Control2, slur.CurveUp,
                EngravingDefaults.SlurMidThickness,
                slur.StaffIndex, slur.Slur.StartMeasureIndex, os, gc);
        }
    }

    /// <summary>
    /// Draws a tie/slur bow, shrinking it around its OSSIA staff's frame when
    /// it belongs to one: Y contracts toward the staff top by the ossia scale
    /// and the mid-thickness scales too, while X stays on the shared spacing
    /// columns — the same affine the ossia staff pass and beam pass apply.
    /// Sound because the bow's endpoints/controls are note-anchored and note
    /// offsets from the staff frame are linear in staff spaces (LP: every grob
    /// of a magnified staff scales with it).
    /// </summary>
    private static void DrawBow(
        double startX, double startY, double endX, double endY,
        (double X, double Y) c1, (double X, double Y) c2,
        bool curveUp, double midThickness,
        int staffIndex, int startMeasureIndex,
        in OssiaShrink os, IDrawingContext gc)
    {
        startY = os.Y(startY, staffIndex, startMeasureIndex);
        endY = os.Y(endY, staffIndex, startMeasureIndex);
        c1 = (c1.X, os.Y(c1.Y, staffIndex, startMeasureIndex));
        c2 = (c2.X, os.Y(c2.Y, staffIndex, startMeasureIndex));
        midThickness = os.Size(midThickness, staffIndex);
        DrawCurve(startX, startY, endX, endY, c1, c2, curveUp, midThickness, gc);
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

    /// <summary>
    /// The contiguous measure range [From..To] of this system where the ossia
    /// has notes/chords, or (-1, -1) when it only rests here. The ossia prints
    /// nothing outside this range — in LP the ossia context exists only for
    /// its fragment (NR "Ossia staves").
    /// </summary>
    private static (int From, int To) OssiaFragment(Staff staff, SystemLayout system)
    {
        int from = -1, to = -1;
        var measures = staff.PrimaryVoice.Measures;
        foreach (var ml in system.Measures)
        {
            if (ml.MeasureIndex >= measures.Length)
                continue;
            bool hasNotes = false;
            foreach (var item in measures[ml.MeasureIndex].Items)
            {
                if (item is NoteItem or ChordItem)
                {
                    hasNotes = true;
                    break;
                }
            }
            if (!hasNotes)
                continue;
            if (from < 0) from = ml.MeasureIndex;
            to = ml.MeasureIndex;
        }
        return (from, to);
    }

    /// <summary>True when the ossia already printed a fragment in an EARLIER
    /// system: LP's ossia convention sets firstClef = ##f, and the clef
    /// engraver creates a clef only when a previous clef exists or firstClef
    /// is true (lily/clef-engraver.cc) — so the FIRST fragment opens bare and
    /// later fragments carry the clef.</summary>
    private static bool OssiaAppearedBefore(
        ScoreLayout layout, Staff staff, SystemLayout system, int staffIndex)
    {
        foreach (var sys in layout.AllSystems)
        {
            if (sys.SystemIndex >= system.SystemIndex)
                break;
            if (StaffPresentInSystem(sys, staffIndex) && OssiaFragment(staff, sys).From >= 0)
                return true;
        }
        return false;
    }

    /// <summary>True when the staff is VISIBLE in this system. A hara-kiri
    /// staff stays in the system's staff table but with IsHidden=true and its
    /// Y collapsed onto a neighbour — drawing by that Y would print the hidden
    /// staff's clef/rests on top of a visible staff. Single-staff layouts
    /// carry no table (empty StaffGroups) and are always visible.</summary>
    private static bool StaffPresentInSystem(SystemLayout system, int staffIndex)
    {
        if (system.StaffGroups.IsDefaultOrEmpty)
            return true;
        foreach (var g in system.StaffGroups)
            foreach (var s in g.Staves)
                if (s.StaffIndex == staffIndex)
                    return !s.IsHidden;
        return true;
    }

    // ---------- Helpers for system-Y lookup ----------

    // Page-scoped on purpose: the map doubles as the page-membership test for
    // every overlay drawer (missing key = the measure is on another page).
    private static Dictionary<int, double> BuildMeasureToSystemY(PageLayout page)
    {
        var map = new Dictionary<int, double>();
        foreach (var system in page.Systems)
            foreach (var ml in system.Measures)
                map[ml.MeasureIndex] = system.Y;
        return map;
    }

    // Measure → its SystemLayout, for drawers that need per-staff Y resolution
    // inside the system (the ossia bow shrink).
    private static Dictionary<int, SystemLayout> BuildMeasureToSystem(PageLayout page)
    {
        var map = new Dictionary<int, SystemLayout>();
        foreach (var system in page.Systems)
            foreach (var ml in system.Measures)
                map[ml.MeasureIndex] = system;
        return map;
    }

    /// <summary>
    /// Shrinks overlay geometry that belongs to an OSSIA staff: absolute Y
    /// contracts toward the staff's top by the ossia scale and sizes/offsets
    /// multiply by it, while X stays on the shared spacing columns — the same
    /// affine the ossia staff pass and beam pass apply (LP: every grob of a
    /// magnified staff scales with it, ly/music-functions-init.ly
    /// magnifyStaff). Identity for normal staves and for layouts without
    /// staff identity (StaffIndex &lt; 0). Sound for note-anchored overlays
    /// because their offsets from the staff frame are linear in staff spaces.
    /// </summary>
    private readonly struct OssiaShrink
    {
        private readonly HashSet<int> _ossiaStaves;
        private readonly Dictionary<int, SystemLayout> _systems;

        public OssiaShrink(HashSet<int> ossiaStaves, Dictionary<int, SystemLayout> systems)
        {
            _ossiaStaves = ossiaStaves;
            _systems = systems;
        }

        public bool Contains(int staffIndex)
            => staffIndex >= 0 && _ossiaStaves.Contains(staffIndex);

        /// <summary>Absolute-Y affine around the staff top; identity off-ossia
        /// (or when the measure has no system on this page).</summary>
        public double Y(double y, int staffIndex, int measureIndex)
        {
            if (!Contains(staffIndex) || !_systems.TryGetValue(measureIndex, out var system))
                return y;
            double top = LayoutUtilities.FindStaffYInSystem(system, staffIndex);
            return top + (y - top) * OssiaScale;
        }

        /// <summary>Scales a size/offset/amplitude; identity off-ossia.</summary>
        public double Size(double v, int staffIndex)
            => Contains(staffIndex) ? v * OssiaScale : v;
    }

    // ---------- F3/B: data-pos resolution ----------

    /// <summary>
    /// Re-derives each annotation layout's data-pos source offset from the LIVE score,
    /// via the <c>SourceIndex</c> the layout carries (an index into the matching score
    /// side-table). This keeps the cached <see cref="ScoreLayout"/> position-independent:
    /// source offsets are NOT baked into the layout geometry but resolved here at render
    /// time. For a normal render this reproduces the same value (snapshot-identical); for
    /// whole-layout reuse (a content-unchanged edit) it yields the edited score's fresh
    /// positions, so editor click-to-source stays correct.
    /// </summary>
    private static ScoreLayout ResolveDataPos(ScoreLayout layout, MultiStaffScore score)
    {
        // Note-hosted annotations (glissando, …) carry a (staff, measure, item) locator
        // instead of a side-table index; their data-pos is the HOST NOTE's source offset.
        // Build the staff -> measures map once so the resolver can re-derive it. Lazy:
        // only built when such an annotation is present.
        var noteHosts = (layout.GlissandoLayouts.IsDefaultOrEmpty
                         && layout.FingeringLayouts.IsDefaultOrEmpty)
            ? null : BuildStaffVoices(score);
        return layout with
        {
            DynamicLayouts = ResolveArr(layout.DynamicLayouts, score.Dynamics,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            ArticulationLayouts = ResolveArr(layout.ArticulationLayouts, score.Articulations,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            ArpeggioLayouts = ResolveArr(layout.ArpeggioLayouts, score.Arpeggios,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            CustomTextLayouts = ResolveArr(layout.CustomTextLayouts, score.CustomTexts,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            FiguredBassLayouts = ResolveArr(layout.FiguredBassLayouts, score.FiguredBasses,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            VoltaBracketLayouts = ResolveArr(layout.VoltaBracketLayouts, score.VoltaBrackets,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            TupletBracketLayouts = ResolveArr(layout.TupletBracketLayouts, score.TupletBrackets,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            PercentRepeatLayouts = ResolveArr(layout.PercentRepeatLayouts, score.PercentRepeats,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            GraceNoteLayouts = ResolveArr(layout.GraceNoteLayouts, score.GraceNotes,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            ChordNameLayouts = ResolveArr(layout.ChordNameLayouts, score.ChordNames,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            TrillSpannerLayouts = ResolveArr(layout.TrillSpannerLayouts, score.TrillSpanners,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            // MusicMarks (incl. section labels + tempo) aren't a flat score side-table;
            // their SourceIndex points into the reconstructed BuildAllMarks() list. Rebuild
            // it the same way Calculate does so each layout re-derives its data-pos. Tempo
            // marks carry SourcePosition 0 (no data-pos emitted), section labels resolve from
            // the (re-collected) measures, explicit marks from score.MusicMarks.
            MusicMarkLayouts = ResolveArr(layout.MusicMarkLayouts,
                MusicMarkEngraver.BuildAllMarks(score.MusicMarks,
                    score.PrimaryContentStaff.PrimaryVoice.Measures, score.Tempo,
                    score.SwingSubdivision, score.TempoText, score.TempoBeatUnit, score.TempoDots),
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            // Lyrics carry the source offset on their nested LyricItem (the renderer draws
            // data-pos from Item.SourcePosition); re-derive that from the live Lyrics table.
            LyricLayouts = ResolveArr(layout.LyricLayouts, score.Lyrics,
                static (l, it) => l with { Item = l.Item with { SourcePosition = it.SourcePosition } },
                static l => l.SourceIndex),
            // Glissando data-pos = its start note's source offset; re-read it from the live
            // score by the note locator the layout carries.
            GlissandoLayouts = ResolveNoteArr(layout.GlissandoLayouts, noteHosts,
                static l => (l.StaffIndex, l.VoiceIndex, l.MeasureIndex, l.ItemIndex),
                static (l, pos) => l with { SourcePosition = pos }),
            // Fingering data-pos = its host note/chord's source offset. Fingerings are
            // computed only for the primary voice, so they always resolve against voice 0.
            FingeringLayouts = ResolveNoteArr(layout.FingeringLayouts, noteHosts,
                static l => (l.StaffIndex, 0, l.MeasureIndex, l.ItemIndex),
                static (l, pos) => l with { SourcePosition = pos }),
            // Detected spanners (hairpin / ottava / text spanner) take their data-pos from
            // the originating cresc/ottava/rit mark in score.MusicMarks — re-derive by its index.
            HairpinLayouts = ResolveArr(layout.HairpinLayouts, score.MusicMarks,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            OttavaBracketLayouts = ResolveArr(layout.OttavaBracketLayouts, score.MusicMarks,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            TextSpannerLayouts = ResolveArr(layout.TextSpannerLayouts, score.MusicMarks,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
        };
    }

    // staff index -> that staff's VOICES, the host tables the note-locator annotations
    // resolve against. Same staff-index convention as the layout build path
    // (LayoutEngine's EnumerateStaves loop). A note-hosted layout carries the voice it
    // lives in, so a second voice's glissando resolves against its own voice's measures.
    private static System.Collections.Generic.Dictionary<int, ImmutableArray<Voice>>
        BuildStaffVoices(MultiStaffScore score)
    {
        var map = new System.Collections.Generic.Dictionary<int, ImmutableArray<Voice>>();
        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
            map[staffIndex] = staff.Voices;
        return map;
    }

    // Refreshes each note-hosted layout's data-pos from its HOST ITEM's source offset,
    // located by the (staff, measure, item) triple it carries. The host is read as the base
    // MusicItem, so it covers both a single note (glissando, melodic fingering) and a chord
    // (chord fingering) — both expose SourcePosition. A locator that doesn't resolve (out of
    // range, or staffIndex -1 from the single-staff Layout(Score) path) is left as-is — its
    // baked value is already correct for a normal full render; only whole-layout reuse needs
    // the re-derivation, and that path always carries a real staff index.
    private static ImmutableArray<T> ResolveNoteArr<T>(
        ImmutableArray<T> layouts,
        System.Collections.Generic.Dictionary<int, ImmutableArray<Voice>>? staffVoices,
        System.Func<T, (int Staff, int Voice, int Measure, int Item)> locator,
        System.Func<T, int, T> resolve)
    {
        if (layouts.IsDefaultOrEmpty || staffVoices == null)
            return layouts;
        var b = layouts.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var (s, v, m, it) = locator(b[i]);
            if (staffVoices.TryGetValue(s, out var voices)
                && (uint)v < (uint)voices.Length
                && (uint)m < (uint)voices[v].Measures.Length)
            {
                var items = voices[v].Measures[m].Items;
                if ((uint)it < (uint)items.Length)
                    b[i] = resolve(b[i], items[it].SourcePosition);
            }
        }
        return b.MoveToImmutable();
    }

    // Refreshes each layout's resolved field from the side-table item it references
    // (by SourceIndex). Out-of-range indices are left as-is (defensive).
    private static ImmutableArray<T> ResolveArr<T, TItem>(
        ImmutableArray<T> layouts, ImmutableArray<TItem> items,
        System.Func<T, TItem, T> resolve, System.Func<T, int> sourceIndex)
    {
        if (layouts.IsDefaultOrEmpty)
            return layouts;
        var b = layouts.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            int si = sourceIndex(b[i]);
            if ((uint)si < (uint)items.Length)
                b[i] = resolve(b[i], items[si]);
        }
        return b.MoveToImmutable();
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
    private static void DrawDynamics(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.DynamicLayouts.IsDefaultOrEmpty) return;
        double fontSize = FontSize * 0.5;
        foreach (var d in layout.DynamicLayouts)
        {
            string text = NormalizeDynamicText(d.Text);
            if (!sysY.TryGetValue(d.MeasureIndex, out var sy)) continue; // other page
            // A dynamic on an ossia staff shrinks with its staff's notation —
            // both the glyph and its distance from the small staff.
            double y = os.Y(sy + d.Y, d.StaffIndex, d.MeasureIndex);
            double size = os.Size(fontSize, d.StaffIndex);
            // Free expressive text (@text) prints plain italic; dynamic levels
            // keep LP's bold-italic DynamicText face.
            var style = d.IsExpressiveText ? FontStyle.Italic : FontStyle.BoldItalic;
            using (gc.Source(d.SourcePosition))
                gc.DrawText(text, d.X, y, size, "serif",
                    style, TextAnchor.Middle, Color.Black);
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
    private static void DrawArticulations(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.ArticulationLayouts.IsDefaultOrEmpty) return;
        foreach (var a in layout.ArticulationLayouts)
        {
            if (string.IsNullOrEmpty(a.Glyph)) continue;
            if (!sysY.TryGetValue(a.MeasureIndex, out var sy)) continue; // other page
            // A script on an ossia staff shrinks with its staff's notation —
            // both the glyph and its distance from the small staff.
            double y = os.Y(sy + a.Y, a.StaffIndex, a.MeasureIndex);
            double scale = os.Size(a.Scale, a.StaffIndex);
            // Bend sentinels ("bendFall"/"bendDoit"): a trailing curve, not a glyph.
            if (a.Glyph is "bendFall" or "bendDoit")
            {
                using (gc.Source(a.SourcePosition))
                    DrawBendAfter(a.X, y, fall: a.Glyph == "bendFall", gc);
                continue;
            }
            // Approach curves INTO the note from the left: scoop rises, plop falls.
            if (a.Glyph is "bendScoop" or "bendPlop")
            {
                using (gc.Source(a.SourcePosition))
                    DrawBendBefore(a.X, y, rise: a.Glyph == "bendScoop", gc);
                continue;
            }
            // Guitar bend-up sentinel ("bendUp:N", N = semitones): rising arrow
            // with the bend amount labelled above the arrowhead.
            if (a.Glyph.StartsWith("bendUp:", StringComparison.Ordinal))
            {
                int semis = int.TryParse(a.Glyph.AsSpan(7), out int bs) ? bs : 2;
                using (gc.Source(a.SourcePosition))
                    DrawGuitarBend(a.X, y, semis, gc);
                continue;
            }
            // TAB technique letters (H / P / T): small italic serif text.
            if (a.Glyph.StartsWith("tabtech:", StringComparison.Ordinal))
            {
                using (gc.Source(a.SourcePosition))
                    gc.DrawText(a.Glyph[8..], a.X, y, 1.5, "serif",
                        FontStyle.Italic, TextAnchor.Middle, Color.Black);
                continue;
            }
            // Chord diagram sentinel ("frame:x32010").
            if (a.Glyph.StartsWith("frame:", StringComparison.Ordinal))
            {
                using (gc.Source(a.SourcePosition))
                    DrawFretFrame(a.X, y, a.Glyph[6..], gc);
                continue;
            }
            // Bartók (snap) pizzicato: a circle with a stem rising from its
            // centre. LILYPOND-REF: scripts.snappizzicato.
            if (a.Glyph == "snappizz")
            {
                using (gc.Source(a.SourcePosition))
                {
                    // Ring = black disc + white core (no stroked-circle API).
                    gc.DrawCircle(a.X, y, 0.45, Color.Black);
                    gc.DrawCircle(a.X, y, 0.33, Color.White);
                    gc.DrawLine(a.X, y - 0.45, a.X, y - 1.4, Color.Black, 0.14);
                }
                continue;
            }
            using (gc.Source(a.SourcePosition))
                gc.DrawGlyph(a.Glyph[0], a.X, y, FontSize * scale);
        }
    }

    /// <summary>
    /// Draws a dead-note "×" notehead (two crossing strokes) sized like a normal
    /// black head, anchored at the head's left edge / vertical centre.
    /// LILYPOND-REF: cross notehead style for \deadNote.
    /// </summary>
    private static void DrawDeadNotehead(double x, double noteY, Color? color, IDrawingContext gc)
    {
        double w = EngravingDefaults.NoteheadBlackWidth;
        const double h = 0.55;                 // half-height of the cross
        double t = EngravingDefaults.StemThickness * 1.4;
        var c = color ?? Color.Black;
        gc.DrawLine(x, noteY - h, x + w, noteY + h, c, t, cap: LineCap.Round);
        gc.DrawLine(x, noteY + h, x + w, noteY - h, c, t, cap: LineCap.Round);
    }

    /// <summary>
    /// Draws a jazz "fall" (drops away) or "doit" (rises away) — a short curved
    /// line trailing off to the right of a note, approximated by a polyline along
    /// a quadratic Bézier. LILYPOND-REF: lily/bend-after.cc BendAfter (curved fall).
    /// </summary>
    /// <summary>
    /// Draws a jazz scoop (rises into the note) or plop (falls into it) — the
    /// mirror of <see cref="DrawBendAfter"/>: the curve starts away-and-left
    /// and arrives at the notehead nearly horizontal.
    /// </summary>
    private static void DrawBendBefore(double x1, double y1, bool rise, IDrawingContext gc)
    {
        const double len = 1.25;
        double drop = rise ? 1.7 : -1.7;          // start BELOW for a scoop
        double x0 = x1 - len, y0 = y1 + drop;
        // Control point mirrored: arrives at the note nearly horizontal.
        double cx = x1 - len * 0.62, cy = y1 + drop * 0.08;
        double px = x0, py = y0;
        const int seg = 8;
        for (int s = 1; s <= seg; s++)
        {
            double t = s / (double)seg, u = 1 - t;
            double nx = u * u * x0 + 2 * u * t * cx + t * t * x1;
            double ny = u * u * y0 + 2 * u * t * cy + t * t * y1;
            gc.DrawLine(px, py, nx, ny, Color.Black, 0.13, cap: LineCap.Round);
            px = nx; py = ny;
        }
    }

    private static void DrawBendAfter(double x0, double y0, bool fall, IDrawingContext gc)
    {
        const double len = 1.25;                 // horizontal reach
        double drop = fall ? 1.7 : -1.7;          // vertical reach (down for fall)
        // Control point: leaves the note nearly horizontal, then curves away.
        double cx = x0 + len * 0.62, cy = y0 + drop * 0.08;
        double px = x0, py = y0;
        const int seg = 8;
        for (int s = 1; s <= seg; s++)
        {
            double t = s / (double)seg, u = 1 - t;
            double nx = u * u * x0 + 2 * u * t * cx + t * t * (x0 + len);
            double ny = u * u * y0 + 2 * u * t * cy + t * t * (y0 + drop);
            gc.DrawLine(px, py, nx, ny, Color.Black, 0.13, cap: LineCap.Round);
            px = nx; py = ny;
        }
    }

    /// <summary>
    /// Draws a chord diagram (fret frame): string grid, finger dots, o/x
    /// row, and an "Nfr" label when the shape sits above the 4th fret.
    /// Spec is LOW string first ("x32010").
    /// LILYPOND-REF: LP \fret-diagram-terse / MusicXML &lt;frame&gt;.
    /// </summary>
    private static void DrawFretFrame(double cx, double bottomY, string spec, IDrawingContext gc)
    {
        int strings = spec.Length;
        const double dx = 0.55;   // string spacing
        const double dy = 0.5;    // fret spacing
        const int fretRows = 4;
        double width = (strings - 1) * dx;
        double left = cx - width / 2;
        // The anchor Y comes from the script/skyline machinery (the frame's
        // real ink box is seeded there) — the grid bottom sits ON the anchor.
        double top = bottomY - fretRows * dy; // grid top (below the o/x row)
        double bottom = top + fretRows * dy;

        // Base fret: shapes above the 4th fret shift down and get "Nfr".
        int minFret = int.MaxValue;
        foreach (var ch in spec)
            if (ch is >= '1' and <= '9')
                minFret = Math.Min(minFret, ch - '0');
        int baseFret = minFret != int.MaxValue && minFret > 4 ? minFret : 1;

        for (int s = 0; s < strings; s++)
            gc.DrawLine(left + s * dx, top, left + s * dx, bottom, Color.Black, 0.05);
        for (int f = 0; f <= fretRows; f++)
            gc.DrawLine(left, top + f * dy, left + width, top + f * dy, Color.Black,
                f == 0 && baseFret == 1 ? 0.16 : 0.05); // nut is thick at position 1

        if (baseFret > 1)
            gc.DrawText($"{baseFret}fr", left + width + 0.35, top + dy * 0.5, 1.1,
                "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);

        for (int s = 0; s < strings; s++)
        {
            char ch = spec[s];
            double sx = left + s * dx;
            if (ch == 'x')
            {
                gc.DrawLine(sx - 0.16, top - 0.5, sx + 0.16, top - 0.18, Color.Black, 0.07);
                gc.DrawLine(sx - 0.16, top - 0.18, sx + 0.16, top - 0.5, Color.Black, 0.07);
            }
            else if (ch is '0' or 'o')
            {
                gc.DrawCircle(sx, top - 0.34, 0.15, Color.Black);
                gc.DrawCircle(sx, top - 0.34, 0.09, Color.White);
            }
            else if (ch is >= '1' and <= '9')
            {
                int fret = ch - '0' - (baseFret - 1);
                if (fret is >= 1 and <= fretRows)
                    gc.DrawCircle(sx, top + (fret - 0.5) * dy, 0.17, Color.Black);
            }
        }
    }

    /// <summary>
    /// Draws a guitar bend-up: a curve leaving the note/fret nearly
    /// horizontally then rising steeply, an arrowhead at the top, and the
    /// bend amount ("½", "full", "1½", …) above the arrowhead.
    /// LILYPOND-REF: TAB bend convention (bend-alter in MusicXML terms);
    /// curve idiom follows DrawBendAfter.
    /// </summary>
    private static void DrawGuitarBend(double x0, double y0, int semitones, IDrawingContext gc)
    {
        const double len = 1.6;    // horizontal reach
        const double rise = 2.6;   // vertical reach (upward)
        double cx = x0 + len * 0.95, cy = y0 - rise * 0.1; // sharp late turn upward
        double topX = x0 + len, topY = y0 - rise;
        double px = x0, py = y0;
        const int seg = 10;
        for (int s = 1; s <= seg; s++)
        {
            double t = s / (double)seg, u = 1 - t;
            double nx = u * u * x0 + 2 * u * t * cx + t * t * topX;
            double ny = u * u * y0 + 2 * u * t * cy + t * t * topY;
            gc.DrawLine(px, py, nx, ny, Color.Black, 0.13, cap: LineCap.Round);
            px = nx; py = ny;
        }
        // Arrowhead: a V of two strokes at the curve's top (no polygon API).
        const double ah = 0.55, aw = 0.32;
        gc.DrawLine(topX - aw, topY + ah, topX, topY, Color.Black, 0.16, cap: LineCap.Round);
        gc.DrawLine(topX + aw, topY + ah, topX, topY, Color.Black, 0.16, cap: LineCap.Round);
        // Amount label in guitar convention: semitones → steps.
        string label = semitones switch
        {
            1 => "½",
            2 => "full",
            _ => (semitones % 2 == 0) ? (semitones / 2).ToString() : $"{semitones / 2}½",
        };
        gc.DrawText(label, topX, topY - 0.35, 1.6, "serif", FontStyle.Italic,
            TextAnchor.Middle, Color.Black);
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
            if (!sysY.TryGetValue(l.Item.MeasureIndex, out var sy)) continue; // other page
            double y = sy + l.Y;
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
            // Hyphen dashes / extender lines: DrawLyricHyphens (LyricHyphen
            // layouts) — the single source, matching LP's grobs.
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
    private static void DrawHairpins(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.HairpinLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var h in layout.HairpinLayouts)
        {
            if (!sysY.TryGetValue(h.StartMeasureIndex, out var sy)) continue; // other page
            double absY = os.Y(sy + h.Y, h.StaffIndex, h.StartMeasureIndex);
            double startOpening = os.Size(h.StartOpening, h.StaffIndex);
            double endOpening = os.Size(h.EndOpening, h.StaffIndex);
            double leftTop = absY - startOpening;
            double leftBottom = absY + startOpening;
            double rightTop = absY - endOpening;
            double rightBottom = absY + endOpening;
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
    private static void DrawOttavaBrackets(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.OttavaBracketLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var b in layout.OttavaBracketLayouts)
        {
            if (!sysY.TryGetValue(b.StartMeasureIndex, out var sy)) continue; // other page
            double absY = os.Y(sy + b.Y, b.StaffIndex, b.StartMeasureIndex);
            double textFontSize = os.Size(FontSize * 0.45, b.StaffIndex);
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
                    gc.DrawLine(b.EndX, absY, b.EndX,
                        absY + os.Size(b.EdgeHeight, b.StaffIndex) * hookDir,
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
            if (!sysY.TryGetValue(v.StartMeasureIndex, out var sy)) continue; // other page
            double absY = sy + v.Y;
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
    private static void DrawTupletBrackets(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TupletBracketLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.13;

        foreach (var b in layout.TupletBracketLayouts)
        {
            if (!sysY.TryGetValue(b.MeasureIndex, out var sy)) continue; // other page
            double edgeHeight = os.Size(TupletBracketEngraver.GetEdgeHeight(), b.StaffIndex);
            double startY = os.Y(sy + b.StartY, b.StaffIndex, b.MeasureIndex);
            double endY = os.Y(sy + b.EndY, b.StaffIndex, b.MeasureIndex);
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
                    os.Size(FontSize * 0.6, b.StaffIndex), "serif",
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
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
    private static void DrawTrillSpanners(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TrillSpannerLayouts.IsDefaultOrEmpty) return;
        const double wavePeriod = 0.8;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var s in layout.TrillSpannerLayouts)
        {
            if (!sysY.TryGetValue(s.StartMeasureIndex, out var sy)) continue; // other page
            double absY = os.Y(sy + s.Y, s.StaffIndex, s.StartMeasureIndex);
            double waveAmplitude = os.Size(0.2, s.StaffIndex);
            using (gc.Source(s.SourcePosition))
            {
                bool isContinuation = Math.Abs(s.GlyphX - s.LineStartX) < 0.01;
                if (!isContinuation)
                    gc.DrawGlyph(EmmentalerGlyphs.OrnTrill, s.GlyphX, absY,
                        os.Size(FontSize, s.StaffIndex));
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
    private static void DrawGlissandos(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.GlissandoLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var g in layout.GlissandoLayouts)
        {
            // MeasureIndex -1 = direct unit-test construction: no page identity,
            // draw unconditionally.
            if (g.MeasureIndex >= 0 && !sysY.ContainsKey(g.MeasureIndex))
                continue; // other page
            using (gc.Source(g.SourcePosition))
                gc.DrawLine(
                    g.StartX, os.Y(g.StartY, g.StaffIndex, g.MeasureIndex),
                    g.EndX, os.Y(g.EndY, g.StaffIndex, g.MeasureIndex),
                    Color.Black, thickness);
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
    private static void DrawArpeggios(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.ArpeggioLayouts.IsDefaultOrEmpty) return;
        const double wavePeriod = 0.8;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var a in layout.ArpeggioLayouts)
        {
            // MeasureIndex -1 = direct unit-test construction: no page identity,
            // draw unconditionally.
            if (a.MeasureIndex >= 0 && !sysY.ContainsKey(a.MeasureIndex))
                continue; // other page
            double topY = os.Y(a.TopY, a.StaffIndex, a.MeasureIndex);
            double bottomY = os.Y(a.BottomY, a.StaffIndex, a.MeasureIndex);
            double waveAmplitude = os.Size(0.2, a.StaffIndex);
            double length = bottomY - topY;
            if (length <= 0) continue;
            // Non-arpeggiate: a straight vertical bracket with end ticks —
            // the chord is NOT rolled. LILYPOND-REF: \arpeggioBracket.
            if (a.Bracket)
            {
                using (gc.Source(a.SourcePosition))
                {
                    gc.DrawLine(a.X, topY, a.X, bottomY, Color.Black, thickness * 1.6);
                    gc.DrawLine(a.X, topY, a.X + 0.7, topY, Color.Black, thickness * 1.6);
                    gc.DrawLine(a.X, bottomY, a.X + 0.7, bottomY, Color.Black, thickness * 1.6);
                }
                continue;
            }
            int halfWaves = Math.Max(1, (int)(length / (wavePeriod / 2)));
            double seg = length / halfWaves;
            double prevX = a.X, prevY = topY;
            const int subdivisions = 4;
            using (gc.Source(a.SourcePosition))
            {
                for (int i = 0; i < halfWaves; i++)
                {
                    double startY = topY + i * seg;
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
    private static void DrawGraceNotes(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.GraceNoteLayouts.IsDefaultOrEmpty) return;
        foreach (var g in layout.GraceNoteLayouts)
        {
            if (!sysY.TryGetValue(g.MeasureIndex, out var sy)) continue; // other page

            // Tab staff: grace notes are small fret numbers on the string lines,
            // not noteheads. No stems/beam/slur/ledger — tab grace is just the
            // shrunken digit before the main fret.
            if (g.Tuning is { } graceTuning)
            {
                DrawTabGraceNotes(g, sy, graceTuning, g.TabClef, gc);
                continue;
            }

            // StaffYOffset places the grace over its OWN staff in a multi-staff
            // score (0 for the first staff / single-staff).
            double staffMiddleY = sy + g.StaffYOffset + StaffHeight / 2;
            // On an ossia staff the whole group shrinks again: head Ys go
            // through the staff-top affine and the grace's own scale compounds
            // with the ossia scale (a grace on a magnified staff is scaled
            // twice in LP too — fontSize composes).
            double eff = os.Size(g.Scale, g.StaffIndex);
            double scaledFontSize = FontSize * eff;
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
                    double y = os.Y(StaffFrame.PositionToDevice(note.StaffPosition, staffMiddleY),
                        g.StaffIndex, g.MeasureIndex);
                    // Ledgers under the head — layer 0 with the staff lines.
                    // LILYPOND-REF: scm/define-grobs.scm LedgerLineSpanner (layer . 0)
                    // On an ossia the anchor is the affined middle and the
                    // per-step offsets shrink via `unit`, matching the heads.
                    if (note.NeedsLedger)
                        DrawLedgerLines(note.StaffPosition, currentX,
                            os.Y(staffMiddleY, g.StaffIndex, g.MeasureIndex), gc,
                            EngravingDefaults.NoteheadBlackWidth * eff,
                            unit: os.Size(1.0, g.StaffIndex));
                    if (note.Accidental is { } acc)
                        DrawAccidental(acc, isCourtesy: false, currentX, y,
                            g.SourcePosition, gc, eff);
                    gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, currentX, y, scaledFontSize);
                    headX.Add(currentX);
                    headY.Add(y);
                    beamCounts.Add(BeamCountForDuration(note.BaseDuration.Denominator));
                    lastNoteX = currentX;
                    lastNoteY = y;
                    currentX += 1.2 * eff;  // approximate advance per grace note
                }

                // Stems (forced UP) plus the connecting beam, or a flag for a lone
                // grace note. Without this the small heads float free of any stem.
                // LILYPOND-REF: scm/music-functions.scm:633-637 score-grace-settings —
                //   ((Voice Stem direction ,UP) (Voice Slur direction ,DOWN)): grace
                //   stems are forced up regardless of pitch, and the auto-slur bows down.
                DrawGraceStemsAndBeam(headX, headY, beamCounts, eff,
                    g.Type == GraceNoteType.Acciaccatura, gc);

                // Grace slur from the last grace notehead to the main notehead.
                // LILYPOND-REF: ly/grace-init.ly startGraceSlur/stopGraceSlur —
                // acciaccatura and appoggiatura are auto-slurred to the main note.
                if (g.Notes.Length > 0 &&
                    g.Type is GraceNoteType.Acciaccatura or GraceNoteType.Appoggiatura)
                {
                    double mainY = os.Y(
                        StaffFrame.PositionToDevice(g.MainNoteStaffPosition, staffMiddleY),
                        g.StaffIndex, g.MeasureIndex);
                    DrawGraceSlur(lastNoteX, lastNoteY, g.MainNoteX, mainY, eff, gc);
                }
            }
        }
    }

    /// <summary>
    /// Draws a grace group on a TAB staff: each grace note becomes a small fret
    /// number on its string line (resolved from the note's MIDI pitch + tuning),
    /// scaled by the grace scale. No stems, beams, slurs, or ledger lines — tab
    /// grace notes are just the shrunken digits ahead of the main fret.
    /// </summary>
    private static void DrawTabGraceNotes(GraceNoteLayout g, double sy, TuningType tuning,
        ClefType clef, IDrawingContext gc)
    {
        double tabTopY = sy + g.StaffYOffset;
        int[] tuningArray = Tunings.GetTuning(tuning);
        int octaveShift = Tunings.OctaveShift(tuning, clef);
        double stringSpace = EngravingDefaults.TabStringSpace(Tunings.GetStringCount(tuning));
        // Tab grace digits sit only slightly below the main fret size (NOT the
        // 0.65 notehead grace scale): on a tab staff the fret number IS the note,
        // so the size contrast that reads as "grace" in notation would here just
        // make the digit illegibly tiny.
        double fontSize = TabFretFontSize * TabGraceFretScale;
        double currentX = g.X;

        using (gc.Source(g.SourcePosition))
        {
            foreach (var note in g.Notes)
            {
                var (stringNum, fret) = Tunings.CalculateFret(note.Midi + octaveShift, tuningArray, 0);
                double noteY = tabTopY + (stringNum - 1) * stringSpace;
                string fretText = fret.ToString();
                double bgWidth = (fretText.Length == 1 ? 0.625 : 1.0) * fontSize;
                double bgHeight = 0.6875 * fontSize;
                // White background occludes the string line behind the digit.
                gc.DrawRectangle(currentX - bgWidth / 2, noteY - bgHeight / 2, bgWidth, bgHeight,
                    fill: Color.White);
                gc.DrawText(fretText, currentX, noteY + fontSize * 0.32, fontSize, "serif",
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
                currentX += 1.2 * g.Scale;
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
            if (!sysY.ContainsKey(bn.MeasureIndex))
                continue; // other page
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
            if (!sysY.TryGetValue(sn.MeasureIndex, out var s)) continue; // other page
            double y = s + sn.Y;
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
    private static void DrawFingerings(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.FingeringLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.56;  // magstep(-5)
        foreach (var f in layout.FingeringLayouts)
        {
            if (!sysY.TryGetValue(f.MeasureIndex, out var sy)) continue; // other page
            double y = os.Y(sy + f.Y, f.StaffIndex, f.MeasureIndex);
            using (gc.Source(f.SourcePosition))
                gc.DrawText(f.Number.ToString(), f.X, y,
                    os.Size(size, f.StaffIndex), "serif",
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
            if (!sysY.TryGetValue(m.MeasureIndex, out var s)) continue; // other page
            double y = s + m.Y;
            using (gc.Source(m.SourcePosition))
                DrawSingleMusicMark(m, y, gc);
        }
    }

    /// <summary>
    /// Draws the swing/shuffle feel equation beside a tempo mark: two beamed straight
    /// notes "=" a beamed dotted + plain note under a triplet "3". <paramref name="subdivision"/>
    /// picks the note value — 8 = eighths (single beam), 16 = sixteenths (double beam).
    /// Hand-built from the same notehead/stem/beam primitives the metronome mark uses.
    /// </summary>
    private static void DrawSwingEquation(IDrawingContext gc, double startX, double baselineY, int subdivision)
    {
        int beams = subdivision >= 16 ? 2 : 1;
        const double beamGap = 0.3;          // spacing between the two beams of a 16th
        // Sizes track the metronome mark: the same notehead size (1.6) and stem length,
        // and a beam scaled to that small note (0.48 staff-beam x 1.6/FontSize) rather
        // than the full staff-beam thickness, which read as too heavy here.
        const double ns = TempoNoteSize;
        const double headGap = 1.0;          // x between the two heads of a pair
        const double stemUp = 1.4;           // stem height (matches the metronome stem)
        const double stemDx = ns * 0.32;     // stem offset from head origin (right side)
        const double stemW = 0.09;
        const double beamW = EngravingDefaults.BeamThickness * (ns / FontSize);
        const double eqSize = 1.8;           // matches the "= NNN" text
        const double threeSize = 1.0;

        // Draws one beamed eighth pair at px; returns the x just past it.
        double DrawPair(double px, bool dotted, bool withThree)
        {
            double h1 = px;
            double h2 = px + headGap + (dotted ? ns * 0.42 : 0);
            gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, h1, baselineY, ns);
            gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, h2, baselineY, ns);
            if (dotted)
                gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot, h1 + ns * 0.6, baselineY, ns);
            double s1 = h1 + stemDx;
            double s2 = h2 + stemDx;
            double beamY = baselineY - stemUp;
            gc.DrawLine(s1, baselineY, s1, beamY, Color.Black, stemW);
            gc.DrawLine(s2, baselineY, s2, beamY, Color.Black, stemW);
            for (int b = 0; b < beams; b++)   // 1 thin beam (8th) or 2 (16th)
                gc.DrawLine(s1, beamY + b * beamGap, s2, beamY + b * beamGap, Color.Black, beamW);
            if (withThree)
            {
                // Triplet bracket "3" above the beam (as on shuffle charts).
                double midX = (s1 + s2) / 2;
                double brkY = beamY - 0.55;
                const double hook = 0.22, halfGap = 0.3;
                gc.DrawLine(s1, brkY, s1, brkY + hook, Color.Black, 0.07);
                gc.DrawLine(s1, brkY, midX - halfGap, brkY, Color.Black, 0.07);
                gc.DrawLine(midX + halfGap, brkY, s2, brkY, Color.Black, 0.07);
                gc.DrawLine(s2, brkY, s2, brkY + hook, Color.Black, 0.07);
                gc.DrawText("3", midX, brkY + 0.35, threeSize, "serif",
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
            }
            return s2 + ns * 0.35;
        }

        double x = DrawPair(startX, dotted: false, withThree: false);
        x += 0.35;
        gc.DrawText("=", x, baselineY, eqSize, "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);
        x += SerifTextMetrics.MeasureBold("=", eqSize) + 0.45;
        DrawPair(x, dotted: true, withThree: true);
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
            // LILYPOND-REF: lily/metronome-engraver.cc — notehead + stem + " = NNN";
            // a textual marking prints bold with the equation parenthesized after
            // it: Grave (♩ = 54). Text-only tempo prints just the bold marking.
            const double noteSize = TempoNoteSize;
            const double textSize = 1.8;
            double x = m.X;
            bool hasMetronome = m.Text.Length > 0;
            if (m.TempoText != null)
            {
                const double markingSize = 2.2;
                gc.DrawText(m.TempoText, x, absY, markingSize,
                    "serif", FontStyle.Bold, TextAnchor.Start, Color.Black);
                if (!hasMetronome)
                    return;
                x += SerifTextMetrics.MeasureBold(m.TempoText, markingSize) + 0.8;
                gc.DrawText("(", x, absY, textSize,
                    "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);
                x += SerifTextMetrics.Measure("(", textSize) + 0.1;
            }
            // Beat-unit note: whole (1) = stemless whole head; 2 = hollow
            // half with stem; 4+ = black head, stem, flags from the 8th up.
            char head = m.TempoBeatUnit <= 1
                ? EmmentalerGlyphs.NoteheadWhole
                : m.TempoBeatUnit == 2
                    ? EmmentalerGlyphs.NoteheadHalf
                    : EmmentalerGlyphs.NoteheadBlack;
            // The stemless whole note sits a touch higher so its center
            // lines up with the equation text the way the stemmed units do
            // (their stem carries the visual weight upward).
            double headY = m.TempoBeatUnit <= 1 ? absY - 0.5 : absY;
            gc.DrawGlyph(head, x, headY, noteSize);
            double headW = m.TempoBeatUnit <= 1 ? noteSize * 0.62 : noteSize * 0.5;
            if (m.TempoBeatUnit >= 2)
            {
                double stemX = x + noteSize * 0.32;
                double stemTop = absY - 3.5 * (noteSize / FontSize);
                gc.DrawLine(stemX, absY, stemX, stemTop, Color.Black, 0.10);
                if (m.TempoBeatUnit >= 8)
                    gc.DrawGlyph(EmmentalerGlyphs.Flag8thUp, stemX, stemTop, noteSize);
            }
            double dotX = x + headW + 0.15;
            for (int d = 0; d < m.TempoDots; d++)
            {
                // Beside the head at ITS center (ly:dots::print puts the dot
                // on the head's line) — absY-0.5 floated it above the head.
                gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot, dotX, headY, noteSize);
                dotX += 0.55;
            }
            double tempoTextX = Math.Max(x + headW + 0.3, m.TempoDots > 0 ? dotX + 0.15 : 0);
            string equation = "= " + m.Text + (m.TempoText != null ? ")" : "");
            gc.DrawText(equation, tempoTextX, absY,
                textSize, "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);
            if (m.SwingSubdivision != 0)
            {
                double textEnd = tempoTextX + SerifTextMetrics.MeasureBold(equation, textSize);
                DrawSwingEquation(gc, textEnd + 0.8, absY, m.SwingSubdivision);
            }
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
        if (m.MarkType == MusicMarkType.ToCoda)
        {
            // "To" followed by the coda SIGN (not the word "Coda"), centered as a
            // group. LILYPOND-REF: the al-coda text is set with the coda glyph.
            double ts = FontSize * 0.7;
            double gs = FontSize * 0.8;
            const string prefix = "To ";
            double textW = SerifTextMetrics.MeasureBold(prefix, ts);
            double glyphW = gs * 0.42;   // approx advance of scripts.coda
            double left = m.X - (textW + glyphW) / 2;
            gc.DrawText(prefix, left, absY, ts, "serif",
                FontStyle.BoldItalic, TextAnchor.Start, Color.Black);
            // The coda glyph's baseline sits low; lift it so its centre aligns with
            // the cap height of "To".
            gc.DrawGlyph(EmmentalerGlyphs.MarkCoda, left + textW, absY - gs * 0.30, gs, Color.Black);
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
            if (!sysY.TryGetValue(t.MeasureIndex, out var s)) continue; // other page
            double y = s + t.Y;
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
    private static void DrawTextSpanners(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TextSpannerLayouts.IsDefaultOrEmpty) return;
        double textSize = FontSize * 0.5;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var s in layout.TextSpannerLayouts)
        {
            if (!sysY.TryGetValue(s.StartMeasureIndex, out var y)) continue; // other page
            double absY = os.Y(y + s.Y, s.StaffIndex, s.StartMeasureIndex);
            using (gc.Source(s.SourcePosition))
            {
                gc.DrawText(s.Text, s.StartX, absY,
                    os.Size(textSize, s.StaffIndex), "serif",
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
            if (!sysY.TryGetValue(b.StartMeasureIndex, out var y)) continue; // other page
            double absY = y + b.Y;
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
    private static void DrawMultiMeasureRests(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.MultiMeasureRestLayouts.IsDefaultOrEmpty) return;
        foreach (var mmr in layout.MultiMeasureRestLayouts)
        {
            if (!sysY.ContainsKey(mmr.StartMeasureIndex))
                continue; // other page
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
        // Use each rest glyph's REAL ink width (from the extracted font metrics) so
        // the centred row matches LilyPond's church_rest, which sums r.extent(X) for
        // each symbol. The block longa/breve rests are only ~0.6 ss wide; the whole
        // rest is 1.5 ss. LILYPOND-REF: lily/multi-measure-rest.cc church_rest.
        var pieces = new List<(int Span, char Glyph, double Width, double Y)>();
        double LongWidth = GlyphMetrics.RestLonga.Width;
        double BreveWidth = GlyphMetrics.RestDoubleWhole.Width;
        double WholeWidth = GlyphMetrics.RestWhole.Width;
        const double Gap = 0.4;
        // Vertical placement (dy, in staff spaces below the staff middle cy — device
        // +Y is down). Each church-rest glyph sits at its own natural staff position
        // spi = Rest::staff_position_internal(me, dl, CENTER). For a normal 5-line staff
        // (line-positions {-4,-2,0,2,4}, neutral direction, default font-size 0 so the
        // dl<0 "(ss - fs)" term vanishes) that resolves to:
        //   whole (dl= 0): spi = +2  → hangs from the 4th line (one line above middle)
        //   breve (dl=-1): spi =  0  → sits on the middle line (ink fills the space above it)
        //   longa (dl=-2): spi =  0  → centred on the middle line (ink spans ±1 space)
        // dy = -0.5 * spi converts a staff position to a device offset from cy.
        // Matches LilyPond 2.24 with \compressMMRests (verified by juxtaposition).
        // LILYPOND-REF: lily/rest.cc Rest::staff_position_internal; lily/multi-measure-rest.cc church_rest.
        int remaining = mmr.MeasureCount;
        foreach (var (span, glyph, width, dy) in new[]
        {
            (4, EmmentalerGlyphs.RestLonga, LongWidth, 0.0),       // spi 0  → dy 0
            (2, EmmentalerGlyphs.RestDoubleWhole, BreveWidth, 0.0), // spi 0  → dy 0
            (1, EmmentalerGlyphs.RestWhole, WholeWidth, -1.0),      // spi +2 → dy -1.0
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
        // Centre the row of rest glyphs on cx. DrawGlyph anchors at the glyph's
        // LEFT edge (SVG text-anchor="start"; these rest glyphs have bbox Left=0),
        // so each glyph's left edge is laid at the running x — NOT at x+Width/2,
        // which would shift the ink right by half a glyph (an R1 then landed ~0.75 ss
        // right of the bar-line midpoint). The whole row spans [cx-totalWidth/2,
        // cx+totalWidth/2], centring the symbols' ink on the span midpoint.
        // LILYPOND-REF: lily/multi-measure-rest.cc church_rest — left_offset =
        // (space - symbols_width)/2, then each glyph add_at_edge LEFT (left-anchored).
        double x = cx - totalWidth / 2;
        foreach (var p in pieces)
        {
            gc.DrawGlyph(p.Glyph, x, p.Y, FontSize);
            x += p.Width + Gap;
        }
        if (mmr.MeasureCount > 1)
            DrawMmrNumber(mmr.MeasureCount, cx, cy, gc);
    }

    /// <summary>
    /// Draws a multi-measure rest's measure count above the staff.
    /// </summary>
    /// <remarks>
    /// LilyPond's MultiMeasureRestNumber uses the music-font number glyphs
    /// (font-encoding fetaText — NOT a text serif font), centred on the rest
    /// (self-alignment-X CENTER) and placed above the staff (direction UP,
    /// staff-padding 0.4). The feta digits are baseline-anchored (bottom =
    /// baseline), so the baseline sits 0.4 ss above the top staff line:
    /// cy - 2.0 (top line) - 0.4 = cy - 2.4.
    /// LILYPOND-REF: scm/define-grobs.scm MultiMeasureRestNumber.
    /// </remarks>
    private static void DrawMmrNumber(int count, double cx, double cy, IDrawingContext gc)
    {
        var digits = count.ToString();
        double totalAdvance = 0;
        foreach (var ch in digits)
            totalAdvance += GlyphMetrics.GetTimeSigDigitWidth(ch - '0');
        double x = cx - totalAdvance / 2;
        double baseline = cy - 2.4;
        foreach (var ch in digits)
        {
            gc.DrawGlyph(EmmentalerGlyphs.GetTimeSigDigit(ch - '0'), x, baseline, FontSize);
            x += GlyphMetrics.GetTimeSigDigitWidth(ch - '0');
        }
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

        DrawMmrNumber(mmr.MeasureCount, (left + right) / 2, cy, gc);
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
    private static void DrawTieVariants(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TieVariantLayouts.IsDefaultOrEmpty) return;
        // Tie variants use staff-relative Y already in the layout — no system offset needed
        // (TieVariantEngraver computes absolute Y).
        foreach (var v in layout.TieVariantLayouts)
        {
            if (!sysY.ContainsKey(v.MeasureIndex))
                continue; // other page
            DrawBow(v.StartX, v.Y, v.EndX, v.Y,
                v.Control1, v.Control2, v.CurveUp,
                EngravingDefaults.TieMidThickness,
                v.StaffIndex, v.MeasureIndex, os, gc);
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
                    if (!sysY.TryGetValue(src.Item.MeasureIndex, out var sy)) continue; // other page
                    gc.DrawLine(dash.X1, sy + dash.Y, dash.X2, sy + dash.Y,
                        Color.Black, thickness);
                }
            }
            else if (h.Type == LyricConnectorType.Extender)
            {
                var src = layout.LyricLayouts[h.LyricIndex];
                if (!sysY.TryGetValue(src.Item.MeasureIndex, out var sy)) continue; // other page
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
            if (!sysY.TryGetValue(pc.MeasureIndex, out var s)) continue; // other page
            double y = s + pc.Y;
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
    private static void DrawInstrumentNames(MultiStaffScore score, SystemLayout system, IDrawingContext gc)
    {
        if (system.Indent <= 0) return;

        const double NameFontScale = 0.75;
        double actualFontSize = FontSize * NameFontScale;
        double nameX = system.Indent / 2.0;

        // Single-staff scores carry no StaffGroup layouts — the one staff sits
        // at the system Y with the standard staff height.
        if (system.StaffGroups.IsDefaultOrEmpty)
        {
            foreach (var (_, st, _) in score.EnumerateStaves())
            {
                if (string.IsNullOrEmpty(st.InstrumentName) || st.IsTab)
                    continue;
                gc.DrawText(st.InstrumentName, nameX, system.Y + StaffHeight / 2.0,
                    actualFontSize, "serif", FontStyle.Regular,
                    TextAnchor.Middle, fill: null,
                    verticalAnchor: VerticalAnchor.Middle);
                break;
            }
            return;
        }

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

        // SystemStartBar across ALL visible staves of the system — EXCLUDING
        // independent text rows (chords / lyrics), which LilyPond's ChordNames /
        // Lyrics contexts do not connect (StaffLayout carries no kind, so resolve
        // back via EnumerateStaves).
        var textRowIndices = new HashSet<int>(
            score.EnumerateStaves().Where(t => t.Staff.IsTextRow)
                .Select(t => t.GlobalStaffIndex));
        var allStaves = system.StaffGroups
            .SelectMany(g => g.Staves)
            .Where(s => !s.IsHidden && !s.IsOssia && !textRowIndices.Contains(s.StaffIndex))
            .OrderBy(s => s.Y)
            .ToList();
        if (allStaves.Count >= 2)
        {
            double top = system.Y + allStaves[0].Y;
            double bottom = system.Y + allStaves[^1].Y + allStaves[^1].Height;
            DrawSystemStartBarLine(systemStartX, top, bottom, gc);
        }

        // Span bars inside delimited groups. Barline types come from a content
        // voice — they are score-synchronized at collection time.
        var voice = score.PrimaryContentStaff.PrimaryVoice;
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
            ClefType.Bass or ClefType.Bass8Below => EmmentalerGlyphs.FClefChange,
            ClefType.Alto or ClefType.Tenor or ClefType.Soprano
                or ClefType.MezzoSoprano or ClefType.Baritone => EmmentalerGlyphs.CClefChange,
            ClefType.Percussion => EmmentalerGlyphs.PercussionClefChange,
            _ => EmmentalerGlyphs.GClefChange,
        };
        double clefY = clefChange.NewClef switch
        {
            ClefType.Bass or ClefType.Bass8Below => staffY + 1,
            ClefType.Alto or ClefType.Percussion => staffY + 2,
            ClefType.Tenor => staffY + 1,
            ClefType.Soprano => staffY + 4,
            ClefType.MezzoSoprano => staffY + 3,
            ClefType.Baritone => staffY + 0,
            _ => staffY + 3,
        };
        using (gc.Source(clefChange.SourcePosition))
        {
            gc.DrawGlyph(glyph, x, clefY, FontSize);
            if (clefChange.NewClef is ClefType.Treble8Below or ClefType.Bass8Below)
                DrawClefModifier8(x, staffY, change: true, gc);
            else if (clefChange.NewClef == ClefType.Treble8Above)
                DrawClefModifier8(x, staffY, change: true, gc, above: true);
        }
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
    private static void DrawKeySignatureChange(KeySignatureChangeItem change, double x, double staffY,
        ClefType clef, IDrawingContext gc)
    {
        int prev = change.PreviousKey.Sharps;
        int next = change.NewKey.Sharps;
        double dx = 0;

        // A CUSTOM key on either side: cancel every step of the previous
        // signature that the new one no longer alters, then draw the new
        // signature (custom or standard) — the simple form of LilyPond's
        // per-step cancellation.
        if (change.PreviousKey.Custom != null || change.NewKey.Custom != null)
        {
            var prevSteps = change.PreviousKey.Custom is { } pc
                ? KeySignature.DecodeCustom(pc).ToList()
                : StandardKeySteps(prev);
            var newAltered = (change.NewKey.Custom is { } nc
                ? KeySignature.DecodeCustom(nc).Select(p => p.Step)
                : StandardKeySteps(next).Select(p => p.Step)).ToHashSet();
            int prevNaturalPos = int.MinValue;
            bool anyNatural = false;
            foreach (var (step, alter) in prevSteps)
            {
                if (newAltered.Contains(step)) continue;
                int staffPosition = KeySigStaffPositionForStep(clef, alter >= 0, step);
                if (anyNatural)
                    dx += GlyphMetrics.AccidentalNatural.Width
                        + NaturalKernPadding(prevNaturalPos, staffPosition);
                double ny = staffY + StaffHeight / 2 - staffPosition * 0.5;
                using (gc.Source(change.SourcePosition))
                    gc.DrawGlyph(EmmentalerGlyphs.AccidentalNatural, x + dx, ny, FontSize);
                prevNaturalPos = staffPosition;
                anyNatural = true;
            }
            if (anyNatural)
                dx += GlyphMetrics.AccidentalNatural.Width + 0.4;
            using (gc.Source(change.SourcePosition))
                DrawKeySignature(change.NewKey, clef, x + dx, staffY, gc);
            return;
        }

        // Cancellation naturals when the sign flips or count shrinks. Their
        // positions are the PREVIOUS key's accidental positions, resolved for
        // THIS staff's clef — the old treble-only table drew bass-staff
        // naturals a third off.
        // LILYPOND-REF: lily/key-engraver.cc — cancellation from key_signature;
        // scm/music-functions.scm key-signature-interface positions.
        bool needNaturals = (prev != 0 && next == 0) ||
                            (prev > 0 && next < 0) || (prev < 0 && next > 0) ||
                            (Math.Sign(prev) == Math.Sign(next) && Math.Abs(next) < Math.Abs(prev));
        if (needNaturals)
        {
            int natCount = Math.Abs(prev) - (Math.Sign(prev) == Math.Sign(next) ? Math.Abs(next) : 0);
            int startAt = Math.Sign(prev) == Math.Sign(next) ? Math.Abs(next) : 0;
            // Naturals kern by their vertical-edge intervals, like LilyPond:
            // a natural has vertical edges on BOTH sides, so neighbours whose
            // edges overlap need 0.3 clearance, corner-touching pairs 0.15,
            // vertically clear pairs none. The old flat 0.7 advance was
            // narrower than the glyph itself (0.724) and the pair overlapped.
            // LILYPOND-REF: lily/key-signature-interface.cc — ht interval
            //   [2p−6, 2p+3], left side shifted +3; padding 0.3 / 0.15.
            int prevNatPos = 0;
            for (int i = 0; i < natCount; i++)
            {
                int staffPosition = KeySigStaffPosition(clef, prev > 0, startAt + i);
                if (i > 0)
                    dx += GlyphMetrics.AccidentalNatural.Width
                        + NaturalKernPadding(prevNatPos, staffPosition);
                double y = staffY + StaffHeight / 2 - staffPosition * 0.5;
                using (gc.Source(change.SourcePosition))
                    gc.DrawGlyph(EmmentalerGlyphs.AccidentalNatural, x + dx, y, FontSize);
                prevNatPos = staffPosition;
            }
            dx += GlyphMetrics.AccidentalNatural.Width;
            // Breathing room between the cancellation and the new signature.
            // LILYPOND-REF: scm/define-grobs.scm KeyCancellation
            //   padding-pairs give it its own break-align slot.
            dx += 0.4;
        }

        if (next != 0)
            DrawKeySignature(change.NewKey, clef, x + dx, staffY, gc);
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
