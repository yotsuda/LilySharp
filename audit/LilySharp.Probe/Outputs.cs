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
/// ⚠️ WHAT IS NOT A DEFECT, measured rather than assumed, and left in the output for the
/// reader to subtract: a tie's second note is engraved and does NOT re-articulate in MIDI
/// (`silent-head`), and a chord sounds under the CHORD's position while the trace spells
/// each member at its own (so chord positions are compared as events, never as pitches).
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
        int PitchDiffers,     // single note whose trace spelling is not what MIDI plays
        string PitchSample,   // first such disagreement, for the reader
        int XmlPitches,       // sounding pitches in the MusicXML (multiset size)
        int MidiPitches,      // ... and in the MIDI, on the books where that is comparable
        int XmlDiffers,       // entries by which the two multisets differ
        string XmlSample,
        bool XmlComparable,   // no source position sounds twice => no unfolding to correct for
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
            "book,positions,silentHeads,soundingRests,midiOnly,graces,pitchDiffers,xmlPitches,midiPitches,xmlDiffers,xmlComparable,note"
        }.Concat(rows.Select(r =>
            $"{r.Name},{r.Positions},{r.SilentHeads},{r.SoundingRests},{r.MidiOnly},{r.Graces},{r.PitchDiffers}," +
            $"{r.XmlPitches},{r.MidiPitches},{r.XmlDiffers},{r.XmlComparable},{r.Note.Replace(',', ';')}")));

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
        Report("MUSICXML vs MIDI — different sounding notes, on books with no unfolding",
            rows.Where(r => r.XmlComparable && r.XmlDiffers > 0)
                .OrderByDescending(r => r.XmlDiffers),
            r => $"{r.XmlDiffers,5} differ (xml {r.XmlPitches} midi {r.MidiPitches}) {r.XmlSample}");

        Console.WriteLine($"grace positions subtracted (no page offset to join to): "
            + $"{rows.Sum(r => r.Graces)} in {rows.Count(r => r.Graces > 0)} books");
        int comparable = rows.Count(r => r.XmlComparable && r.Note.Length == 0);
        Console.WriteLine($"MusicXML/MIDI reach: {comparable} of {read} books sound no position twice"
            + " (the rest expand a repeat or a phrase, which only the MIDI unfolds)");
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
        Reading Fail(string why) => new(name, 0, 0, 0, 0, 0, 0, "", 0, 0, 0, "", false, why);
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
            int maxOrdinal = 0;
            var midi = new MidiExporter().Export(tree);
            foreach (var t in midi.Tracks)
                foreach (var n in t.Notes)
                {
                    maxOrdinal = Math.Max(maxOrdinal, n.SourceOrdinal);
                    if (n.Channel == 9)
                    {
                        if (n.SourcePos >= 0) percussion.Add(n.SourcePos);
                        continue;
                    }
                    midiKeys.Add(n.Pitch);
                    if (n.SourcePos < 0) continue;
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
                foreach (var m in part.Measures)
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
                        xmlKeys.Add(RelativeOctave.StepToMidi(st, (int)Math.Round(n.Alter ?? 0), oct));
                    }

            // --- the differences ----------------------------------------------------------
            int silent = 0, soundingRests = 0, midiOnly = 0, graces = 0, pitchDiffers = 0;
            string sample = "";
            foreach (var (pos, count) in heads)
            {
                if (count > 0 && !sounded.ContainsKey(pos) && !percussion.Contains(pos)) silent++;
                if (chords.Contains(pos) || percussion.Contains(pos)) continue;
                if (sounded.TryGetValue(pos, out var played) && trace.TryGetValue(pos, out var spelt))
                {
                    int want = PitchToMidi(spelt);
                    if (want >= 0 && !played.Contains(want))
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

            return new Reading(name, heads.Count, silent, soundingRests, midiOnly, graces,
                pitchDiffers, sample, xs.Count, ms.Count, xmlDiffers, xmlSample,
                maxOrdinal == 0, "");
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
