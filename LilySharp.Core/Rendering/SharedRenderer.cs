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
/// measured serif text (TextFontMetrics) are implemented here as well.
///
/// The legacy <c>SvgRenderer</c> was retired and DELETED once parity was
/// reached (recoverable from git history). Beam drawing is staff- and
/// knee-aware; cross-staff beam PRODUCTION (the upstream layout emitting
/// MemberStaffIndices) is the remaining known gap.
/// </remarks>
internal static partial class SharedRenderer
{
    private const double StaffHeight = 4.0;
    // Clearance from the redrawn clef/key/time prefix to a start barline that
    // opens a line, so a `|:` at a system start doesn't overprint the clef.
    // LILYPOND-REF: scm/define-grobs.scm BarLine space-alist — clef /
    // key-signature / time-signature all reserve (extra-space . 1.15).
    private const double LineStartBarClearance = 1.15;
    // Mirror of LyricEngraver's VerseSpacing (baseline step between stacked verses).
    private const double LyricVerseSpacing = 3.2;
    // Internal so LP-fidelity readers can identify a text run by the SAME size expression
    // the draw calls use (e.g. custom text at FontSize * 0.6), instead of a second literal.
    internal const double FontSize = 4.0;
    // LILYSHARP-OWN: the swing feel-equation's small note size. The equation is Lily#'s
    // own device (no LilyPond counterpart) and its head-gap/stem/beam constants are tuned
    // to this size. The METRONOME MARK's own note is NOT this any more — it draws at
    // \smaller = magstep(-1) of the full size (MetronomeMarkGeometry.NoteSize).
    private const double SwingNoteSize = 1.6;
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
            // Internal layout/geometry is device Y-down; the single conversion to the
            // device output happens here, in the Y-flip decorator wrapping the page
            // context. Every primitive Y handed to `gc` below is page Y-up
            // (page-bottom origin) and the decorator maps it to device.
            IDrawingContext gc = new YFlipDrawingContext(doc.BeginPage(page.Width, page.Height), page.Height);
            // `font "NAME"` header directive: remap every generic text family to the
            // configured face for the header AND the body (music glyphs pass through).
            // Wrap before DrawHeader and the margin group so both are covered.
            if (!string.IsNullOrEmpty(score.TextFont))
                gc = new TextFontDrawingContext(gc, score.TextFont!);
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
                    DrawSystem(score, layout, system, resolver, beamedItems, gc, page.Height);
                // Page-level overlays that span systems. The Y-anchor map is
                // built from THIS page's systems only: system Y is page-local
                // (each page restarts at MarginTop), so every overlay whose
                // measure lives on another page must be skipped on this one —
                // drawing it here would overprint this page's music at the
                // other page's local Y. Each drawer treats a missing measure
                // key as "not on this page".
                var measureToSystemTopYUp = BuildMeasureToSystemTopYUp(page);
                var measureToSystem = BuildMeasureToSystem(page);
                var os = new OssiaShrink(ossiaStaves, measureToSystem);
                DrawTies(layout, measureToSystemTopYUp, os, gc);
                DrawSlurs(layout, measureToSystemTopYUp, os, gc);
                DrawDynamics(layout, measureToSystemTopYUp, os, gc);
                DrawArticulations(layout, measureToSystemTopYUp, os, gc);
                DrawLyrics(layout, measureToSystemTopYUp, gc);
                DrawHairpins(layout, measureToSystemTopYUp, os, gc);
                DrawOttavaBrackets(layout, measureToSystemTopYUp, os, gc);
                DrawVoltaBrackets(layout, measureToSystemTopYUp, gc);
                DrawTupletBrackets(layout, measureToSystemTopYUp, os, gc);
                DrawTrillSpanners(layout, measureToSystemTopYUp, os, gc);
                DrawGlissandos(layout, measureToSystemTopYUp, os, gc);
                DrawArpeggios(layout, measureToSystemTopYUp, os, gc);
                DrawGraceNotes(layout, measureToSystemTopYUp, os, gc, page.Height);
                DrawChordNames(layout, measureToSystemTopYUp, gc);
                DrawFiguredBass(layout, measureToSystemTopYUp, os, gc);
                DrawPercentRepeats(layout, measureToSystemTopYUp, os, gc);
                DrawBarNumbers(layout, measureToSystemTopYUp, gc);
                DrawStanzaNumbers(layout, measureToSystemTopYUp, gc);
                DrawFingerings(layout, measureToSystemTopYUp, os, gc);
                DrawMusicMarks(layout, measureToSystemTopYUp, os, gc);
                DrawCustomTexts(layout, measureToSystemTopYUp, os, gc);
                DrawTextSpanners(layout, measureToSystemTopYUp, os, gc);
                DrawPedalBrackets(layout, measureToSystemTopYUp, gc);
                DrawMultiMeasureRests(layout, measureToSystemTopYUp, gc);
                DrawTieVariants(layout, measureToSystemTopYUp, os, gc);
                DrawLyricHyphens(layout, measureToSystemTopYUp, gc);
                DrawPartCombine(layout, measureToSystemTopYUp, gc);
            }
            finally
            {
                marginScope?.Dispose();
            }
            doc.EndPage();
        }
    }

    // ---------- Header ----------

    // LILYPOND-REF: ly/titling-init.ly:75 — \huge \larger \larger \bold ≈ 3.49 ss
    // LILYPOND-REF: ly/titling-init.ly:89 — composer baseline ≈ 2.2 ss
    private const double TitleFontSize = 3.49;
    private const double ComposerFontSize = 2.2;

    private static void DrawHeader(
        MultiStaffScore score, PageLayout page, LayoutOptions options, IDrawingContext gc)
    {
        // Keep the device-Y downward accumulation, but emit each baseline in the
        // page's Y-up frame (page-bottom origin): the flipping context turns
        // page.Height − y back into the device y.
        double y = options.MarginTop;
        if (score.Title is { } title)
        {
            double centerX = page.Width / 2;
            using (SourceScope(gc, score.Header.Title))
                gc.DrawText(title, centerX, page.Height - y, TitleFontSize, "serif",
                    FontStyle.Bold, TextAnchor.Middle);
            y += TitleFontSize;
        }
        if (score.Composer is { } composer)
        {
            double rightX = page.Width - options.MarginLeft;
            using (SourceScope(gc, score.Header.Composer))
                gc.DrawText(composer, rightX, page.Height - y, ComposerFontSize, "serif",
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
        HashSet<(int Staff, int Voice, int Measure, int Item)> beamedItems, IDrawingContext gc,
        double pageHeight)
    {
        bool isFirstSystem = system.SystemIndex == 0;
        double systemStartX = system.Indent;
        // The widest clef in the system: every staff's key/time signature break-aligns
        // past this SHARED column (LP's Clef break-align spans the whole system), so a
        // grand staff's bass F clef keeps the treble staff's meter aligned with it. This
        // is the same width the layout reserved (SpacingRules.MaxClefWidth), so the drawn
        // prefatory items land exactly where the first note was spaced from (ledger defect-3).
        double maxClefWidth = SpacingRules.MaxClefWidth(score);
        // Where the Clef break-align GROUP's ink starts, relative to each clef's own origin:
        // one anchor for every staff, so a system mixing a percussion clef with a pitched one
        // places both from the group and not each from itself. See ClefGroupExtent.
        double clefGroupInkLeft = SpacingRules.ClefGroupInkLeft(score);

        // Shared time-signature column. LilyPond break-aligns the TimeSignature into ONE
        // column spanning all staves (break-alignment-interface.cc:141-142,242 — the
        // KeySignature group extent is the union across staves), so a transposed part with a
        // wider key pushes the concert staff's meter to the SAME X rather than each staff
        // spacing its meter off its own key. Computed as the MAX of each staff's own would-be
        // meter X: a score whose staves share a key is byte-identical (max == each), and only
        // genuinely differing keys re-align. Uses the KEY INK width (SpacingRules
        // .KeySignatureInkWidth — the ONE model the reservation reads too), the
        // same width the layout's break-align reservation uses, so the drawn meter and the
        // reserved column coincide — one model, not two — and both match LilyPond's key→time
        // (measured off the key's ink RIGHT edge, extra-space 1.15).
        // Shared KEY column: the KeySignature break-aligns into ONE column spanning the
        // system exactly like the clef and time columns — every staff's signature starts
        // at the widest clef's right + the Clef→Key gap, INCLUDING an ossia whose first
        // appearance draws no clef of its own (its key still sits in the system column,
        // not at some LeftEdge distance — the ledger pair line-start.ossia-key-alignment
        // measured LilyPond's OKEY anchor equal to the main staff's KEY anchor).
        // LILYPOND-REF: lily/break-alignment-interface.cc:141-142 — group extent = union
        // across staves; :242 — the next column offsets from the union RIGHT.
        double sharedKeyX;
        double sharedTimeX;
        {
            double clefRightX = systemStartX + EngravingDefaults.ClefGlyphXOffset + maxClefWidth;
            double clefToTime = clefRightX + BreakAlignSpacing.GetSpacing(
                BreakAlignSymbol.Clef, BreakAlignSymbol.TimeSignature).Value;
            sharedKeyX = clefRightX + BreakAlignSpacing.GetSpacing(
                BreakAlignSymbol.Clef, BreakAlignSymbol.KeySignature).Value;
            double keyToTime = BreakAlignSpacing.GetSpacing(
                BreakAlignSymbol.KeySignature, BreakAlignSymbol.TimeSignature).Value;
            sharedTimeX = clefToTime;
            foreach (var (_, st, _) in score.EnumerateStaves())
            {
                // The SAME staff set the reservation's WidestActiveKeyInk walks — one
                // predicate, so the column that is drawn and the column that is booked
                // cannot be built from different staves (ledger
                // line-start.time-to-first-note.tab-*).
                if (!SpacingRules.ContributesToKeyColumnWidth(st))
                    continue;
                var k = ResolveKeySignature(st, system, score);
                if (GetSystemStartKeyChange(st, system) is { } kc)
                    k = kc.NewKey;
                // "Keyed" is ink > 0 — the SAME predicate the reservation's key column
                // uses (SolveColumns skips zero-width items), so a degenerate signature
                // with glyphless Custom (e.g. `key custom` naming no pitches) neither
                // draws nor spaces a key column on either side. The width is the ENGRAVED
                // stencil's, so an ossia contributes its reduced-size signature — the same
                // union LilyPond takes (break-alignment-interface.cc:141-142).
                double keyInk = SpacingRules.EngravedKeyInkWidth(st, k);
                double tx = keyInk > 0.0
                    ? sharedKeyX + keyInk + keyToTime
                    : clefToTime;
                sharedTimeX = Math.Max(sharedTimeX, tx);
            }
        }

        // System-start delimiters (brackets / bar lines connecting staves in a group).
        DrawSystemStartDelimiters(system, gc);

        // Instrument names within the indent area (drawn before staves so glyphs
        // overlap correctly when names are wider than the indent).
        DrawInstrumentNames(score, system, systemStartX, gc);

        // Staff lines end exactly at the final barline (the last measure's right
        // edge), so the staff never overshoots a ragged system nor falls short of a
        // justified one. (system.Width is the target width, not the drawn content.)
        double staffRight = system.Measures.Length > 0
            ? system.Measures[^1].X + system.Measures[^1].Width
            : system.Width;

        // An end-of-line courtesy key signature — and the courtesy meter after it — sit ON
        // the staff after the final barline, so the staff lines extend over the reserved
        // suffix (tab staves keep the unextended width; they print no signatures).
        double notationStaffRight = staffRight;
        if (system.Measures.Length > 0)
        {
            var eolCourtesy = GetSystemEndKeyChange(score.PrimaryContentStaff, system);
            if (eolCourtesy is { } key)
                notationStaffRight += SpacingRules.KeyCourtesySuffixWidth(
                    key.PreviousKey.Sharps, key.NewKey.Sharps);
            if (GetSystemEndTimeChange(score.PrimaryContentStaff, system) is { } eolMeter)
                notationStaffRight += SpacingRules.TimeCourtesySuffixWidth(
                    eolMeter, afterCourtesyKey: eolCourtesy is not null);
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

            // Device staff-top Y for the ossia group transform below (an inherently
            // device-frame affine): FindStaffYInSystem is now page Y-up (W2-core), so
            // reflect it back to device here. The non-ossia content path uses the Y-up
            // StaffTopYUp (localStaffY) further down.
            double staffY = pageHeight - LayoutUtilities.FindStaffYInSystem(system, globalIdx);
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
                    DrawBarlines(system, staff, LayoutUtilities.StaffTopYUp(system, globalIdx), layout, gc, barHeight: h);
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
            // The flip decorator conjugates this group transform AND re-flips the
            // group's content by the page height. To keep BOTH the emitted <g>
            // transform and the content's local coordinates byte-identical to the
            // former device output, the group translate absorbs the scaled page
            // height and the content's local refpoint is pageHeight (so localStaffY −
            // offset flips back to the original local device offset). Deriving from
            // the decorator's conjugation: emitted device translate =
            // H − ty − scale·H must equal the original device staffY, giving
            // ty = H(1 − scale) − staffY.
            IDisposable? groupScope = isOssia
                ? gc.BeginGroup(new DrawingTransform(0, pageHeight * (1 - OssiaScale) - staffY, OssiaScale, OssiaScale))
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
                // Non-ossia: the staff's top-line Y-up (page-bottom origin). Ossia:
                // pageHeight — the content is inside the flip-conjugated group, so its
                // local refpoint is the page height (localStaffY − offset then flips,
                // via the decorator, back to the original local device offset).
                double localStaffY = isOssia ? pageHeight : LayoutUtilities.StaffTopYUp(system, globalIdx);

                // Tablature staves: string lines + TAB clef + fret numbers.
                if (staff.IsTab)
                {
                    // Measures this staff repeats under a % sign: hide their fret digits
                    // (the % replaces them), exactly as the notation staff hides its notes.
                    var tabPercentCovered = new HashSet<int>();
                    foreach (var prItem in score.PercentRepeats)
                        if (prItem.StaffIndex == globalIdx)
                            tabPercentCovered.Add(prItem.MeasureIndex);
                    DrawTabStaff(staff, layout, system, globalIdx, localStaffY, staffRight,
                        systemStartX,
                        clefGroupInkLeft, beamedItems, tabPercentCovered, sgc, pageHeight);
                    continue;
                }

                DrawStaffLines(localStaffY, lineEndX, sgc, lineStartX, staff.Lines);

                // System-start prefix. The clef and key signature repeat at the
                // head of EVERY system (standard notation); the key reflects any
                // mid-piece change in force at this point. The time signature is
                // printed only at the very start — a mid-piece meter change is
                // drawn as a measure item, not a system prefix.
                // LILYPOND-REF: lily/break-align-engraver.cc — Clef + KeySignature
                // are break-aligned at every line start; TimeSignature is not.
                double prefixEndX = systemStartX;
                var clef = ResolveClef(staff, system);
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
                        prefixEndX = DrawClef(clef, systemStartX, localStaffY, maxClefWidth,
                            clefGroupInkLeft, sgc);
                }
                // Break-align gaps between prefix items, from the SAME space-alists
                // CalculatePrefixWidth reserves by, so the drawn glyph lands where the spacing
                // reserved rather than tight against the clef. Each gap is measured off the
                // previous item's ink (extra-space). LILYPOND-REF Clef/KeySignature space-alist.
                bool keyDrawn = false;
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
                    // Only a non-empty signature draws, at the SHARED key column
                    // (sharedKeyX): the KeySignature break-aligns into one column spanning
                    // the system, so an ossia whose first appearance has no clef of its own
                    // still prints its key at the main staves' key X — not at some gap off
                    // a clef that is not there (ledger line-start.ossia-key-alignment).
                    // C major draws nothing and leaves the meter spaced from the clef itself.
                    // For a staff with a clef this is byte-identical to the former
                    // prefixEndX + Clef→Key walk (the clef return IS the shared clef right).
                    // "Keyed" is ink > 0 — the reservation's predicate (SolveColumns skips
                    // zero-width items), so draw and reserve agree even for a degenerate
                    // glyphless Custom signature.
                    keyDrawn = SpacingRules.KeySignatureInkWidth(activeKey) > 0.0;
                    if (keyDrawn)
                        prefixEndX = sharedKeyX;
                    using (SourceScope(sgc, keySigPos))
                        prefixEndX = DrawKeySignature(activeKey, clef, prefixEndX, localStaffY, sgc,
                            isOssia ? OssiaScale : 1.0);
                }
                if (!isOssia)
                {
                    // The meter draws at the SHARED time column (sharedTimeX), so every staff's
                    // meter aligns even when a transposed part carries a wider key signature.
                    // keyDrawn/this staff's own key no longer pick the X — the system-wide max
                    // does (LP break-alignment shares the TimeSignature column across staves).
                    if (isFirstSystem)
                    {
                        if (!score.TimeSignature.SenzaMisura)
                            using (SourceScope(sgc, score.Header.Time))
                                DrawTimeSignature(score.TimeSignature, sharedTimeX, localStaffY, sgc);
                    }
                    else if (GetSystemStartTimeChange(staff, system) is { } startTimeChange)
                    {
                        // A meter change at the line break is part of the prefix.
                        if (!startTimeChange.NewTime.SenzaMisura)
                            DrawTimeSignature(startTimeChange.NewTime, sharedTimeX, localStaffY, sgc);
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
                bool anyOverrides = !score.GrobOverrides.IsDefaultOrEmpty || !score.GrobReverts.IsDefaultOrEmpty;
                for (int vi = 0; vi < voices.Length; vi++)
                {
                    int voiceNumber = vi + 1;
                    // Scope the override resolver to THIS staff and voice: a global
                    // override (null staff) is seen by all, a staff-/voice-scoped one only
                    // where it was written. When the score has no overrides at all, reuse
                    // the shared (empty) resolver to avoid per-voice allocation.
                    var voiceResolver = anyOverrides
                        ? GrobPropertyResolver.ForStaffVoice(
                            score.GrobOverrides, score.GrobReverts, globalIdx, voiceNumber)
                        : resolver;
                    DrawStaffMeasures(voices[vi], voiceNumber, voices,
                        system, layout, globalIdx, localStaffY, clef, voiceResolver, beamedItems, sgc,
                        pageHeight, fragFrom, fragTo, percentCovered);
                }

                // Barlines (typed: single / double / final / repeat) per measure
                DrawBarlines(system, staff, localStaffY, layout, sgc,
                    fromMeasure: fragFrom, toMeasure: fragTo);

                // End-of-line courtesy group: the cancellation + new key signature when the
                // NEXT line opens with a key change, then the new METER when it opens with a
                // time change. The layouter reserved this room after the final barline.
                // LILYPOND-REF: lily/key-engraver.cc + explicitKeySignatureVisibility
                //   default all-visible — the changed signature prints on BOTH sides.
                // LILYPOND-REF: scm/define-grobs.scm:3922-3953 break-visibility — the
                //   TimeSignature grob's is all-visible, so a CHANGED meter does the same.
                //   Only a changed one: see GetSystemEndTimeChange.
                if (!isOssia && system.Measures.Length > 0)
                {
                    var lastMl = system.Measures[^1];
                    // lastMl.X + lastMl.Width is the final barline's RIGHT edge (measured:
                    // the barline rect ends there), which is what both courtesies hang off.
                    double barlineRight = lastMl.X + lastMl.Width;
                    double? meterX = null;

                    // LilyPond's END-OF-LINE break-align order
                    // (LILYPOND-REF: scm/define-grobs.scm:632-648 BreakAlignment.break-align-orders)
                    // runs staff-bar → key-cancellation → key-signature → time-signature, and
                    // every gap in it is the LEFT grob's space-alist entry — the same table the
                    // line-START prefix walks. There is ONE break-align group at each end of a
                    // line; until 2026-08-03 this end read three hand-written constants instead.
                    //
                    // ⚠️ THE TABLE IS SHARED, THE WALK IS NOT — say so rather than let
                    // "runs through BreakAlignSpacing" be read off the call. This chains each
                    // member off the PREVIOUS ONE'S DRAWN INK RIGHT EDGE plus its gap, which is
                    // LILYPOND-REF: lily/break-alignment-interface.cc:241-243 Break_alignment_interface::calc_positioning_done
                    //   — offsets[r] = extents[l][RIGHT] + distance − extents[r][LEFT], with
                    //   both extents cancelling: the same arithmetic SolveColumns does.
                    // SolveColumns is NOT called because it wants a WIDTH for each member, and
                    // the drawn key's real right edge is what extents[l][RIGHT] actually is:
                    // feeding it a modelled width would be a second spelling of that edge. The
                    // reservation (SpacingRules.KeyCourtesySuffixWidth) does model it, as an
                    // UPPER BOUND on the natural kerning, so the two sides do not agree to the
                    // digit yet.
                    //   departs from: nothing in the arithmetic; only in WHERE it lives.
                    //   goes away when: the courtesy key's reserved width becomes the drawn
                    //     width (one model, not a bound), after which this group can be handed
                    //     to SolveColumns like the line-start prefix is.
                    //   observed by: audit/lp-geometry courtesy.* — all EXACT, so the
                    //     arithmetic is pinned; nothing yet observes the reserve/draw width gap.
                    if (GetSystemEndKeyChange(staff, system) is { } eolKeyChange)
                    {
                        // Which symbol OPENS the group decides which entry the bar line's alist
                        // is keyed by — a cancellation and a signature are different break-align
                        // symbols even where BarLine happens to declare 1.0 for both.
                        bool opensWithCancellation = SpacingRules.CancellationNaturalCount(
                            eolKeyChange.PreviousKey.Sharps, eolKeyChange.NewKey.Sharps) > 0;
                        double groupLeft = barlineRight + SpacingRules.BreakAlignGap(
                            BreakAlignSymbol.StaffBar,
                            opensWithCancellation
                                ? BreakAlignSymbol.KeyCancellation
                                : BreakAlignSymbol.KeySignature);
                        // A meter after a key stands off the KEY's real right edge, which is
                        // what the draw returns — not off a width computed a second time.
                        meterX = DrawKeySignatureChange(eolKeyChange, groupLeft, localStaffY, clef, sgc)
                            + SpacingRules.BreakAlignGap(
                                BreakAlignSymbol.KeySignature, BreakAlignSymbol.TimeSignature);
                    }

                    if (GetSystemEndTimeChange(staff, system) is { } eolTimeChange)
                        using (sgc.Source(eolTimeChange.SourcePosition))
                            DrawTimeSignature(
                                eolTimeChange.NewTime,
                                // Alone, the meter takes its OWN entry off the bar line — not the
                                // key's. LilyPond's two are 0.750000 and 1.150000, so one number
                                // for both would be wrong on one side by construction.
                                meterX ?? barlineRight + SpacingRules.BreakAlignGap(
                                    BreakAlignSymbol.StaffBar, BreakAlignSymbol.TimeSignature),
                                localStaffY, sgc);
                }
            }
            finally
            {
                groupScope?.Dispose();
            }
        }

        // Beams (use system-wide coordinates; ossia beams are rare and
        // outside the Phase 2-A scope so we draw at full scale)
        DrawBeams(score, layout, system, gc, pageHeight);

        // SpanBars: connect barlines across the staves of each multi-staff group.
        DrawSpanBars(score, system, gc);
    }

    /// <summary>
    /// Draws barlines spanning the full height of each multi-staff group, so the
    /// per-staff barlines read as one continuous barline across the group.
    /// Repeat dots stay per-staff (drawn by <see cref="DrawBarlines"/>).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/span-bar-engraver.cc — SpanBar across a connected group.</remarks>
    private static void DrawSpanBars(MultiStaffScore score, SystemLayout system, IDrawingContext gc)
    {
        if (system.StaffGroups.IsDefaultOrEmpty) return;
        double systemYUp = LayoutUtilities.SystemTopYUp(system);

        for (int gi = 0; gi < system.StaffGroups.Length && gi < score.StaffGroups.Length; gi++)
        {
            // Only connected, multi-staff groups (those with a delimiter) get a span bar.
            if (system.StaffGroups[gi].GrandStaffLayout is not { } delim) continue;

            double top = systemYUp + delim.BraceTop;
            double height = delim.TotalHeight;
            if (height <= StaffHeight + 0.001) continue; // single staff — nothing to span

            // Barline types are a measure property shared by all staves in the group.
            var voice = score.StaffGroups[gi].Staves[0].PrimaryVoice;
            bool lineStart = true;
            foreach (var ml in system.Measures)
            {
                if (ml.MeasureIndex >= voice.Measures.Length) continue;
                var measure = voice.Measures[ml.MeasureIndex];
                bool atLineStart = lineStart;
                lineStart = false;

                // Line-start start barline clears the redrawn clef (see DrawBarlines).
                if (measure.StartBarline != BarlineType.None)
                    DrawBarline(measure.StartBarline,
                        atLineStart ? ml.X + LineStartBarClearance : ml.X, top, height, gc, withDots: false);

                double endX = ml.X + ml.Width;
                DrawBarline(measure.EndBarline, endX - GetVisualBarlineWidth(measure.EndBarline),
                    top, height, gc, withDots: false);
            }
        }
    }

    private static void DrawStaffLines(double staffY, double width, IDrawingContext gc, double startX = 0,
        int lines = 5)
    {
        // Reduced staves draw centered on the 5-line frame: 1 line = the
        // middle, 2 lines = the timbales pair (rows 1 and 3), 3-4 contiguous
        // centered. Geometry (positions, barlines, stems) stays 5-line.
        // LILYPOND-REF: StaffSymbol line-positions — percussion/timbales styles.
        IEnumerable<int> rows = lines switch
        {
            1 => [2],
            2 => [1, 3],
            3 => [1, 2, 3],
            4 => [0, 1, 2, 3],
            _ => [0, 1, 2, 3, 4],
        };
        foreach (int i in rows)
        {
            // staffY is the top line's Y-up; successive lines run downward (device),
            // i.e. toward smaller Y-up.
            double y = staffY - i;
            gc.DrawLine(startX, y, width, y, Color.Black, EngravingDefaults.StaffLineThickness);
        }
    }

}
