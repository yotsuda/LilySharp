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

using LilySharp.Core.Midi;
using LilySharp.Core.Music;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

/// <summary>
/// Asks ONE book the same question through more than one output, and reports where the
/// answers differ. The page, the MIDI, the MusicXML and the twin are four readings of one
/// piece of music, and nothing in this repo compared them to each other.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT EXISTS. Session 194 found three defects in one sitting, and all three had the
/// same shape: one output was playing a different piece from the others, and every net that
/// looked at a SINGLE output was green. `a4@rest` drew a rest, and sounded a note in the
/// MIDI and printed <c>&lt;pitch&gt;</c> in the MusicXML. A transposing book's twin was in
/// the written key. A phrase reference inside a repeat exported an empty body. The nets in
/// the suite are per-output, so per-output is exactly what they can be wrong about
/// together; the missing instrument is the one that puts two outputs side by side.
/// </para>
/// <para>
/// ⚠️ THE JOIN IS THE SOURCE POSITION, not the order and not a count. <see
/// cref="MidiNote.SourcePos"/> carries the offset of the syntax that sounded, and every
/// <see cref="MusicItem.SourcePosition"/> carries the offset of the syntax that was
/// engraved, so the two can be compared per WRITTEN NOTE. That is what makes the reading
/// immune to the difference this repo keeps tripping over: the MIDI UNFOLDS repeats while
/// the page records structure (HANDOFF §2F), so any comparison by sequence or by total
/// count reports a difference for every book with a `|:` in it. Per position, a repeat
/// simply sounds the same position twice.
/// </para>
/// <para>
/// ⚠️ THE PAGE'S ANSWER IS THE COLLECTED ITEM, NOT `check --pitches`. The report was the
/// obvious source (HANDOFF §1 ⑺ suggested it) and it is the WRONG one for this question:
/// <c>CreatePitchedRestItem</c> resolves its pitch through the same house the notes use, so
/// the trace lists a pitched rest as though it were a note. An instrument built on the trace
/// would have called session 194's third defect a MATCH. The trace is still used, but only
/// for the pitch VALUE of a position the items already agree is a note.
/// </para>
/// <para>
/// ⚠️ TWO SCORES ARE TWO COLLECTS. Positions are unioned over every RenderSpec, the way
/// <see cref="ResolvedPitches.ForFile"/> does, because stopping at the first score drops
/// whole parts (and a part NO score renders is absent from both sides — a known hole,
/// HANDOFF §2F, and the reason `midi-only` is reported rather than assumed to be a defect).
/// </para>
/// <para>
/// ⚠️ THE MUSICXML SIDE HAS A REACH, and the reach is asked of the OUTPUTS, not of the
/// ordinal. The MusicXML carries no source offsets, so it can only be compared as a
/// multiset of sounding keys — which is sound exactly while the MIDI sounds no copy the
/// document does not write. `|: … :|` is that case: the page prints one copy, the MIDI
/// sounds two, and the document writes one plus `&lt;repeat direction="backward"/&gt;`.
/// A phrase played twice is NOT that case, however similar it looks: all three write two.
/// To rebuild the pair that decides it — the first must be in reach, the second out —
/// <code>
/// phrase P { c'4 d' e' f' | }   section A { v { P P } }        // in reach, 0 differences
/// section A { v { |: c'4 d' e' f' :| } }                       // out of reach
/// </code>
/// </para>
/// <para>
/// ⚠️ WHAT IS NOT A DEFECT, measured rather than assumed, and left in the output for the
/// reader to subtract: a tie's second note is engraved and does NOT re-articulate in MIDI
/// (`silent-head`), and a chord sounds under the CHORD's position while the trace spells
/// each member at its own (so chord positions are compared as events, never as pitches).
/// </para>
/// <para>
/// ⚠️⚠️ A TRANSPOSING PART PRINTS ONE PITCH AND SOUNDS ANOTHER, and until 2026-08-17 this
/// instrument compared the two and called every note of it a difference. `part gtr {
/// instrument guitar }` prints C4 under a `treble_8` clef and sounds C3, which is what all
/// three outputs are FOR; the probe read C4 off the page, 48 off the MIDI, and reported 24
/// of 24 positions on `test/treble8` (44 of the 46 suspect books were this). Both written
/// sides are now shifted into sounding before the comparison, and the two sides ask
/// DIFFERENT sources for the shift on purpose:
/// <list type="bullet">
/// <item>The MusicXML side reads the DOCUMENT's own <c>&lt;transpose&gt;</c>
///   (<see cref="MusicXmlAttributes.TransposeSemitones"/>) — written + transpose = sounding
///   is the spec's own equation, so this side is asked of the output, never of the source.
///   ⚠️ <c>&lt;clef-octave-change&gt;</c> is NOT added: it is notation (where the pitch is
///   drawn), and honouring both drops a guitar two octaves.</item>
/// <item>The PAGE side has no such element to read — an engraved staff carries the octave
///   clef as a glyph and the instrument's share only in its NAME — so it reads the PART
///   HEADER (<see cref="PartHeaderDefaults.SoundingShiftSemitones"/>), attributed by the
///   span of the music each part owns. ⚠️ THIS ONE IS CIRCULAR and the reader must know it:
///   the MIDI exporter resolves the same header, so a defect IN THAT READING moves both
///   sides together and stays green here. What survives is everything else — the pitch the
///   page spelt, the octave it opened in, the notes it dropped.</item>
/// </list>
/// </para>
/// </remarks>
internal static class Outputs
{
    /// <summary>One book's reading. Counts, not verdicts — the classification of a count
    /// as a defect happens in the session that reads it, never here.</summary>
    private sealed record Reading(
        string Name,
        int Positions,        // written positions the page engraves as note or chord
        int SilentHeads,      // ... that sound nothing in MIDI (ties live here)
        int SoundingRests,    // page draws a REST there, MIDI sounds a note
        int MidiOnly,         // MIDI sounds a position the page engraves nothing at
        int Graces,           // ... of which are inside a grace group: NOT comparable
        int SharedPhrases,    // engraved positions in a phrase two parts transpose differently
        int PitchDiffers,     // single note whose trace spelling is not what MIDI plays
        string PitchSample,   // first such disagreement, for the reader
        int XmlPitches,       // sounding pitches in the MusicXML (multiset size)
        int MidiPitches,      // ... and in the MIDI, on the books where that is comparable
        int XmlDiffers,       // entries by which the two multisets differ
        string XmlSample,
        bool XmlComparable,   // the MIDI sounds no copy the document does not write
        string Note);

    public static int Run(string root, string listFile, string only)
    {
        var books = listFile != null
            ? File.ReadAllLines(listFile).Where(l => l.Trim().Length > 0)
                .Select(l => Path.Combine(root, l.Trim().Replace('/', Path.DirectorySeparatorChar)))
                .Where(File.Exists).OrderBy(p => p, StringComparer.Ordinal).ToArray()
            : Directory.GetFiles(Path.Combine(root, "LilySharp.Tests", "Fixtures"),
                "*.lys", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (only != null)
            books = books.Where(p => p.Replace('\\', '/').Contains(only, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        var rows = new List<Reading>(books.Length);
        foreach (var p in books)
            rows.Add(Read(root, p));

        var outDir = Path.Combine(root, "audit", "probe-out");
        Directory.CreateDirectory(outDir);
        var csv = Path.Combine(outDir, "pitches.csv");
        File.WriteAllLines(csv, new[]
        {
            "book,positions,silentHeads,soundingRests,midiOnly,graces,sharedPhrases,pitchDiffers,xmlPitches,midiPitches,xmlDiffers,xmlComparable,pitchSample,xmlSample,note"
        }.Concat(rows.Select(r =>
            // The two samples are in the CSV and not only in the report because the report
            // prints 25 rows per heading: session 196 had to re-run the sweep on hand-made
            // sublists to see the first disagreement of the books ranked 26th and lower,
            // which is the one thing that says WHICH family a row belongs to.
            $"{r.Name},{r.Positions},{r.SilentHeads},{r.SoundingRests},{r.MidiOnly},{r.Graces}," +
            $"{r.SharedPhrases},{r.PitchDiffers}," +
            $"{r.XmlPitches},{r.MidiPitches},{r.XmlDiffers},{r.XmlComparable}," +
            $"{r.PitchSample.Replace(',', ';')},{r.XmlSample.Replace(',', ';')},{r.Note.Replace(',', ';')}")));

        int read = rows.Count(r => r.Note.Length == 0);
        Console.WriteLine($"{books.Length} books, {read} read, {rows.Count - read} could not be read");
        Console.WriteLine();

        Report("SOUNDING RESTS — the page draws a rest, the MIDI sounds a note",
            rows.Where(r => r.SoundingRests > 0).OrderByDescending(r => r.SoundingRests),
            r => $"{r.SoundingRests,5} of {r.Positions,5}");
        Report("PITCH DISAGREEMENT — one written note, two different pitches",
            rows.Where(r => r.PitchDiffers > 0).OrderByDescending(r => r.PitchDiffers),
            r => $"{r.PitchDiffers,5} of {r.Positions,5}   {r.PitchSample}");
        Report("MIDI-ONLY POSITIONS — sounded, engraved nowhere",
            rows.Where(r => r.MidiOnly > 0).OrderByDescending(r => r.MidiOnly),
            r => $"{r.MidiOnly,5} of {r.Positions,5}");
        Report("SILENT HEADS — engraved, sounds nothing (a tie's second note lives here)",
            rows.Where(r => r.SilentHeads > 0).OrderByDescending(r => r.SilentHeads),
            r => $"{r.SilentHeads,5} of {r.Positions,5}");
        Report("MUSICXML vs MIDI — different sounding notes, on books the two both write out",
            rows.Where(r => r.XmlComparable && r.XmlDiffers > 0)
                .OrderByDescending(r => r.XmlDiffers),
            r => $"{r.XmlDiffers,5} differ (xml {r.XmlPitches} midi {r.MidiPitches}) {r.XmlSample}");

        Console.WriteLine($"grace positions subtracted (no page offset to join to): "
            + $"{rows.Sum(r => r.Graces)} in {rows.Count(r => r.Graces > 0)} books");
        Console.WriteLine($"phrase positions subtracted (two parts play them at two "
            + $"transpositions): {rows.Sum(r => r.SharedPhrases)} in "
            + $"{rows.Count(r => r.SharedPhrases > 0)} books");
        int comparable = rows.Count(r => r.XmlComparable && r.Note.Length == 0);
        Console.WriteLine($"MusicXML/MIDI reach: {comparable} of {read} books sound no copy the"
            + " document does not write (the rest repeat material the page engraves once)");
        Console.WriteLine($"-> {Path.GetRelativePath(root, csv).Replace('\\', '/')}");
        return 0;
    }

    private static void Report(string title, IEnumerable<Reading> rows, Func<Reading, string> cell)
    {
        var list = rows.ToList();
        Console.WriteLine($"== {title}: {list.Count} books");
        foreach (var r in list.Take(25))
            Console.WriteLine($"   {cell(r)}  {r.Name}");
        if (list.Count > 25)
            Console.WriteLine($"   ... and {list.Count - 25} more");
        Console.WriteLine();
    }

    private static Reading Read(string root, string path)
    {
        string name = Path.GetRelativePath(root, path).Replace('\\', '/');
        Reading Fail(string why) => new(name, 0, 0, 0, 0, 0, 0, 0, "", 0, 0, 0, "", false, why);
        try
        {
            // LF-canonical for Sweep's reason: a source offset past a newline is the join
            // key here, so a CRLF working tree would move every key on both sides at once.
            var tree = SyntaxTree.Parse(File.ReadAllText(path).Replace("\r\n", "\n"));

            // --- the page, over every score ---------------------------------------------
            var heads = new Dictionary<int, int>();   // position -> sounding note heads
            var rests = new HashSet<int>();
            var chords = new HashSet<int>();          // pitch is not comparable at these
            var trace = new Dictionary<int, string>();

            // --- the written→sounding shift, per source span ------------------------------
            // ⚠️ NOT read off the collected Staff: `Staff.Transposition` is filled in for TAB
            // staves only (CreateTab takes it; Create does not), so a plain `instrument bass`
            // staff reports 0 while its MIDI plays −12, and a book with both `staff m` and
            // `tab m` answered differently depending on which staff was walked last.
            // The shift belongs to the PART, so it is taken from the part header — the same
            // reading MidiExporter takes (PartSoundingShift) — and attributed by the span of
            // the music that part owns: a `PartBlockSyntax` inside a section, or the part
            // DECLARATION itself when the section is written inside it (part-major).
            // ⚠️ Music in a bare top-level section that only a `score` assigns to a part is
            // inside NEITHER span, and gets 0 — which is what the MIDI does with it too.
            var partSpans = new List<(int Start, int End, int Shift)>();
            // Phrase bodies two parts play at two different transpositions: no single answer,
            // so their positions are subtracted from the pitch comparison and reported.
            var sharedPhrases = new List<(int Start, int End)>();
            {
                var decls = tree.GetRoot().DescendantNodes<PartDeclarationSyntax>().ToArray();
                int ShiftOf(string partName) => PartHeaderDefaults
                    .Read(Array.Find(decls, d => d.Name.Text == partName))
                    .SoundingShiftSemitones;
                foreach (var d in decls)
                    partSpans.Add((d.FullSpan.Start, d.FullSpan.Start + d.FullSpan.Length,
                        ShiftOf(d.Name.Text)));
                foreach (var pb in tree.GetRoot().DescendantNodes<PartBlockSyntax>())
                    partSpans.Add((pb.FullSpan.Start, pb.FullSpan.Start + pb.FullSpan.Length,
                        ShiftOf(pb.Name)));

                // ⚠️ A PHRASE BODY IS WRITTEN OUTSIDE EVERY PART. `phrase gtrLine { c4 … }`
                // at top level sounds inside whatever part references it, so its notes are
                // at source positions no part span contains — and `test/instrument-defaults`
                // reported all 13 notes of its guitar and tenor phrases as disagreements for
                // that reason alone. The body therefore inherits the shift of the parts that
                // REFERENCE it, propagated through nested references to a fixed point.
                // ⚠️ One phrase can be played by two parts at two transpositions, and then
                // its positions carry no single answer: those are counted and skipped rather
                // than compared against one of the two (see `sharedPhrases`).
                var phrases = new Dictionary<string, PhraseDeclarationSyntax>();
                foreach (var ph in tree.GetRoot().DescendantNodes<PhraseDeclarationSyntax>())
                    phrases[ph.Name.Text] = ph;
                var phraseShifts = new Dictionary<string, HashSet<int>>();
                void Refer(SyntaxNode scope, int shift)
                {
                    foreach (var vr in scope.DescendantNodes<VariableReferenceSyntax>())
                        if (phrases.ContainsKey(vr.Name.Text))
                        {
                            if (!phraseShifts.TryGetValue(vr.Name.Text, out var set))
                                phraseShifts[vr.Name.Text] = set = new HashSet<int>();
                            set.Add(shift);
                        }
                }
                foreach (var d in decls)
                    Refer(d, ShiftOf(d.Name.Text));
                foreach (var pb in tree.GetRoot().DescendantNodes<PartBlockSyntax>())
                    Refer(pb, ShiftOf(pb.Name));
                for (bool changed = true; changed;)
                {
                    changed = false;
                    foreach (var (pname, shifts) in phraseShifts.ToArray())
                        foreach (var vr in phrases[pname].Body.DescendantNodes<VariableReferenceSyntax>())
                            if (phrases.ContainsKey(vr.Name.Text))
                            {
                                if (!phraseShifts.TryGetValue(vr.Name.Text, out var inner))
                                    phraseShifts[vr.Name.Text] = inner = new HashSet<int>();
                                foreach (int s in shifts)
                                    changed |= inner.Add(s);
                            }
                }
                foreach (var (pname, shifts) in phraseShifts)
                {
                    var body = phrases[pname].Body;
                    if (shifts.Count == 1)
                        partSpans.Add((body.FullSpan.Start,
                            body.FullSpan.Start + body.FullSpan.Length, shifts.Single()));
                    else
                        sharedPhrases.Add((body.FullSpan.Start,
                            body.FullSpan.Start + body.FullSpan.Length));
                }

                // ⚠️ A BARE SECTION IS ATTRIBUTED BY THE SCORE, not by the music. Music
                // written in `section A { c4 … }` with no `partName { }` block around it is
                // claimed by nothing except `score main { staff bl }` — which is how the
                // page reads it, and (since 2026-08-17) the MIDI too. Without this the
                // instrument compared the page's C3 against a MIDI that correctly sounds C2
                // for a bass, and reported the fix as a difference.
                // ⚠️ Only when the score names exactly ONE part: two parts means the page
                // draws the same music in two registers and there is no single answer.
                var specs = RenderSpecParser.FindAll(tree);
                var sole = specs.Count > 0 ? specs[0].EngravedPartNames : default;
                if (specs.Count > 0 && !sole.IsDefaultOrEmpty && sole.Length == 1)
                {
                    int bareShift = ShiftOf(sole[0]);
                    foreach (var sec in tree.GetRoot().DescendantNodes<SectionDeclarationSyntax>())
                    {
                        bool hasBlock = false;
                        for (int i = 0; i < sec.SlotCount; i++)
                            if (sec.GetChild(i) is PartBlockSyntax) { hasBlock = true; break; }
                        if (!hasBlock)
                            partSpans.Add((sec.FullSpan.Start,
                                sec.FullSpan.Start + sec.FullSpan.Length, bareShift));
                    }
                }
            }
            // The SMALLEST containing span wins, so a part block written inside a part
            // declaration answers for itself rather than for its container.
            int ShiftAt(int pos)
            {
                int best = 0, width = int.MaxValue;
                foreach (var (start, end, shift) in partSpans)
                    if (pos >= start && pos < end && end - start < width)
                        (best, width) = (shift, end - start);
                return best;
            }
            bool SharedPhrase(int pos)
            {
                foreach (var (start, end) in sharedPhrases)
                    if (pos >= start && pos < end) return true;
                return false;
            }
            IEnumerable<RenderSpec> passes = RenderSpecParser.FindAll(tree);
            if (!passes.Any())
                passes = new RenderSpec[] { null };
            int collected = 0;
            foreach (var spec in passes)
            {
                MeasureCollector collector;
                MultiStaffScore score;
                try
                {
                    collector = new MeasureCollector { ScoreTranspose = spec?.ScoreTranspose };
                    score = SvgGenerator.CollectScore(collector, tree, spec);
                }
                catch { continue; }
                collected++;
                foreach (var e in collector.PitchTrace)
                    trace.TryAdd(e.Position, e.Pitch);
                foreach (var st in score.EnumerateStaves())
                    foreach (var v in st.Staff.Voices)
                        foreach (var m in v.Measures)
                            foreach (var it in m.Items)
                                switch (it)
                                {
                                    case NoteItem n:
                                        Bump(heads, n.SourcePosition, 1);
                                        break;
                                    case ChordItem c:
                                        Bump(heads, c.SourcePosition, Math.Max(1, c.Notes.Length));
                                        chords.Add(c.SourcePosition);
                                        break;
                                    case RestItem r:
                                        rests.Add(r.SourcePosition);
                                        break;
                                }
            }
            if (collected == 0)
                return Fail("no score collected");

            // --- the MIDI ----------------------------------------------------------------
            var sounded = new Dictionary<int, HashSet<int>>();  // position -> pitches
            // ⚠️ Percussion is sounded, but it is not a PITCH: a drum note's key names an
            // instrument, and the page draws it from DrumInfo.StaffPosition. Kept in its own
            // set so a drum head counts as sounded (`drum-groove` reported 24 of 24 heads
            // silent when they were merely on channel 10) without ever being compared to a
            // spelling.
            var percussion = new HashSet<int>();
            var midiKeys = new List<int>();
            // ⚠️ ONSETS, not distinct pitches: `sounded` is a SET, so it cannot tell one
            // sounding of a position from three. The reach test below needs the count.
            var onsets = new Dictionary<int, int>();
            var midi = new MidiExporter().Export(tree);
            foreach (var t in midi.Tracks)
                foreach (var n in t.Notes)
                {
                    if (n.Channel == 9)
                    {
                        if (n.SourcePos >= 0) percussion.Add(n.SourcePos);
                        continue;
                    }
                    midiKeys.Add(n.Pitch);
                    if (n.SourcePos < 0) continue;
                    Bump(onsets, n.SourcePos, 1);
                    if (!sounded.TryGetValue(n.SourcePos, out var set))
                        sounded[n.SourcePos] = set = new HashSet<int>();
                    set.Add(n.Pitch);
                }

            // ⚠️ A GRACE NOTE HAS NO PAGE POSITION TO JOIN TO. The page carries its graces on
            // the main note as GraceNoteInfo — staff position, accidental, duration and MIDI
            // key, but no source offset — so "the MIDI sounds a position the page engraves
            // nothing at" is what a correctly engraved grace looks like from here. Counted
            // and subtracted rather than dropped: an instrument that hides its blind spot
            // reports a clean sweep over the part it cannot see (RULES §5.3).
            var graceSpans = tree.GetRoot().DescendantNodes<GraceExpressionSyntax>()
                .Select(g => (g.FullSpan.Start, End: g.FullSpan.Start + g.FullSpan.Length)).ToArray();
            bool InGrace(int pos) => graceSpans.Any(s => pos >= s.Start && pos < s.End);

            // --- the MusicXML -------------------------------------------------------------
            // No source offsets here — the exporter walks syntax and writes a document, and
            // a <note> carries no back-reference. So this side is compared as a MULTISET of
            // sounding keys, and only on the books where the MIDI unfolds nothing (below):
            // both walk the same syntax and expand the same phrases, so on those books the
            // two multisets must be equal, note for note.
            var xmlKeys = new List<int>();
            var doc = new MusicXmlExporter().Export(tree);
            foreach (var part in doc.Parts)
            {
                // written + <transpose> = sounding, which is the spec's equation and the
                // whole reason the element exists. Carried forward measure by measure
                // because the exporter writes it once, in the part's opening attributes.
                int transpose = 0;
                foreach (var m in part.Measures)
                {
                    if (m.Attributes?.TransposeSemitones is { } semis) transpose = semis;
                    foreach (var n in m.Notes)
                    {
                        // ⚠️ A tie's second note is WRITTEN in MusicXML and is not a second
                        // onset in MIDI (the exporter merges it into the sustained note), so
                        // it is subtracted here rather than reported as a difference every
                        // tied book would show.
                        if (n.IsRest || n.IsUnpitched || n.IsBackup || n.TieStop || n.Step == null)
                            continue;
                        int st = "CDEFGAB".IndexOf(n.Step[0]);
                        if (st < 0 || n.Octave is not int oct) continue;
                        xmlKeys.Add(
                            RelativeOctave.StepToMidi(st, (int)Math.Round(n.Alter ?? 0), oct)
                            + transpose);
                    }
                }
            }

            // --- the differences ----------------------------------------------------------
            int silent = 0, soundingRests = 0, midiOnly = 0, graces = 0, pitchDiffers = 0;
            int shared = 0;
            string sample = "";
            foreach (var (pos, count) in heads)
            {
                if (count > 0 && !sounded.ContainsKey(pos) && !percussion.Contains(pos)) silent++;
                if (chords.Contains(pos) || percussion.Contains(pos)) continue;
                if (SharedPhrase(pos)) { shared++; continue; }
                if (sounded.TryGetValue(pos, out var played) && trace.TryGetValue(pos, out var spelt))
                {
                    // The trace spells what the page PRINTS. What sounds is that plus the
                    // part's shift (see the transposing-part remark on the class).
                    // ⚠️ The -1 that means "spelling not understood" is tested BEFORE the
                    // shift is added: a +12 part would otherwise turn it into the key 11.
                    int written = PitchToMidi(spelt);
                    int want = written + ShiftAt(pos);
                    if (written >= 0 && !played.Contains(want))
                    {
                        pitchDiffers++;
                        if (sample.Length == 0)
                            sample = $"@{pos} page {spelt}({want}) midi {string.Join('/', played)}";
                    }
                }
            }
            foreach (var pos in sounded.Keys)
            {
                if (heads.ContainsKey(pos)) continue;
                if (rests.Contains(pos)) soundingRests++;
                else if (InGrace(pos)) graces++;
                else midiOnly++;
            }

            // The two multisets, as a symmetric difference. Sorted-and-zipped rather than
            // counted: two books can carry the same NUMBER of notes and not the same notes,
            // which is exactly what an octave slip looks like (measured 2026-08-17: a
            // sub-voice's B3 C3 exported as B2 C2, same count, different piece).
            var xs = xmlKeys.OrderBy(k => k).ToList();
            var ms = midiKeys.OrderBy(k => k).ToList();
            int xmlDiffers = 0;
            string xmlSample = "";
            for (int i = 0, j = 0; i < xs.Count || j < ms.Count;)
            {
                if (j >= ms.Count || (i < xs.Count && xs[i] < ms[j])) { Record($"xml {xs[i++]}"); }
                else if (i >= xs.Count || ms[j] < xs[i]) { Record($"midi {ms[j++]}"); }
                else { i++; j++; }
            }
            void Record(string what)
            {
                xmlDiffers++;
                if (xmlSample.Length == 0) xmlSample = "only in " + what;
            }

            // ⚠️ THE REACH TEST IS "DID THE MIDI SOUND A COPY NOBODY WROTE", and it is asked
            // of the two sides themselves. It used to be `SourceOrdinal == 0 everywhere`,
            // which is a DIFFERENT question and was wrong in BOTH directions (measured
            // 2026-08-17, session 196, falsifiers rebuildable from the remarks above):
            //   * `phrase P { … }` played twice carries ordinals 0 and 1, so the old test
            //     called it out of reach — but the page prints it twice, the MIDI sounds it
            //     twice AND the MusicXML writes it twice, so the multisets are comparable
            //     and 12 of 566 books were being skipped for nothing.
            //   * `|: … :|` carries ordinal 0 on BOTH passes on purpose (MidiExporter
            //     restores the snapshot, because the second pass is the same PRINTED copy),
            //     so the old test called it in reach — and then reported the whole second
            //     pass as a difference on 10 books, when the MusicXML says the same thing
            //     with `<repeat direction="backward"/>` and any player unfolds it.
            // The honest predicate compares the two counts the two sides actually produce:
            // a position the page engraves N heads at may sound N times, never more.
            bool xmlComparable = !heads.Any(h =>
                onsets.TryGetValue(h.Key, out int played) && played > h.Value);

            return new Reading(name, heads.Count, silent, soundingRests, midiOnly, graces,
                shared, pitchDiffers, sample, xs.Count, ms.Count, xmlDiffers, xmlSample,
                xmlComparable, "");
        }
        catch (Exception ex)
        {
            return Fail(ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void Bump(Dictionary<int, int> d, int key, int by)
        => d[key] = d.TryGetValue(key, out int v) ? v + by : by;

    /// <summary>"F#5" / "Bb3" / "Cx4" (the collector's <c>FormatPitch</c>) to a MIDI key,
    /// or -1 for a spelling this does not know. Goes through the engine's own
    /// <see cref="RelativeOctave.StepToMidi"/> so the two sides cannot drift on the
    /// octave convention.</summary>
    private static int PitchToMidi(string spelt)
    {
        if (spelt.Length < 2) return -1;
        int step = "CDEFGAB".IndexOf(spelt[0]);
        if (step < 0) return -1;
        int i = 1, alter = 0;
        for (; i < spelt.Length; i++)
        {
            if (spelt[i] == '#') alter++;
            else if (spelt[i] == 'x') alter += 2;
            else if (spelt[i] == 'b') alter--;
            else break;
        }
        return int.TryParse(spelt[i..], out int octave)
            ? RelativeOctave.StepToMidi(step, alter, octave)
            : -1;
    }
}
