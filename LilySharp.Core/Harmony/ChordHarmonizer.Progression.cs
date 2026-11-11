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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Harmony;

public static partial class ChordHarmonizer
{
    private const string ChordRowPart = " (chords)";

    /// <summary>A reading that fits its notes less than this is said to fit loosely.</summary>
    private const double LooseFit = 0.5;

    /// <summary>
    /// The chord progression of a score, section by section, as a <c>chords</c> block writes it —
    /// for an AI arranging the piece to read before it writes a part (owner's request 2026-11-11:
    /// asked to revoice a guitar, the chat named each chord from the notes by itself, and the
    /// author of the tool did the same). Nothing is changed: the answer is text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THE SCORE WRITES COMES FIRST (the owner's rule, the same day): a <c>chords</c> row's
    /// entry, an <c>@chord(Dm)</c>, a <c>chord(…)</c> or <c>diagram(…)</c> item's name, and the
    /// chord a shape sounds where no name is written (<c>@chord(x32010)</c>,
    /// <c>@diagram(x32010)</c>, a bare <c>@chord</c> or <c>@diagram</c> on a chord). A section
    /// with a <c>chords</c> row is answered with the row as it is written, whether or not a score
    /// shows it. Else one that names a chord in half its bars or more is read from the names
    /// alone, each held to the next; one that names fewer keeps each name over the stretch the
    /// notes read as one chord there, and the rest is read from the notes (<see cref="HarmonyReader"/>).
    /// </para>
    /// <para>
    /// With <paramref name="parts"/>, only those parts are read — their notes and the chords
    /// written on them; a <c>chords</c> row is the score's, not a part's, and is left out then.
    /// </para>
    /// </remarks>
    /// <param name="source">The score.</param>
    /// <param name="scoreName">The score to read, as the preview picks it; null for the first.</param>
    /// <param name="parts">The parts to read, by name; null or empty for all.</param>
    public static string DescribeProgression(string source, string? scoreName = null, IReadOnlyList<string>? parts = null)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return "The score has errors: the chords are read from what it plays, so fix them first.";
        var root = tree.GetRoot();
        var declared = root.DescendantNodes<PartDeclarationSyntax>().Where(p => !p.IsStrokes).Select(p => p.Name.Text).Distinct().ToList();
        var asked = (parts ?? []).Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList();
        var unknown = asked.Where(a => !declared.Contains(a)).ToList();
        if (unknown.Count > 0)
            return $"No part named {string.Join(", ", unknown)}: the parts are {string.Join(", ", declared)}.";
        bool every = asked.Count == 0;
        var (homeTonic, homeSharps) = ReadKey(source);
        var (named, fromNotes) = WrittenChords(root, homeSharps);
        var rows = WrittenRows(root, source);

        var lines = new List<(string Section, string Bars, string How)>();
        var silent = new List<string>();
        var remarks = new List<string>();
        // The chords the parts asked write out, by the chord each is read as: what an edit of them replaces.
        var writtenOut = new List<(string Part, string Symbol, string Text, int Position)>();
        var order = new List<string>();
        foreach (var section in root.DescendantNodes<SectionDeclarationSyntax>())
        {
            string sectionName = section.Name.Text;
            if (lines.Any(l => l.Section == sectionName) || silent.Contains(sectionName))
                continue;
            if (every && rows.TryGetValue(sectionName, out var row))
            {
                lines.Add((sectionName, row.Bars, $"as written (the chords row `{row.Name}`)"));
                continue;
            }
            if (ReadSection(source, tree, scoreName, sectionName, (homeTonic, homeSharps),
                    part => every || asked.Contains(part), (named, fromNotes), (tonic, sharps) => MelodyChords.OfKey(tonic, sharps)) is not { } read)
            {
                silent.Add(sectionName);
                continue;
            }
            var (beats, symbols, isWritten, barOf, readings, dense, struck) = read;
            if (!every)
                foreach (var (part, position, symbol) in struck)
                    if (symbol != "r" && position >= 0 && position < source.Length && source[position] == '<'
                        && source.IndexOf('>', position) is > 0 and var close && !writtenOut.Any(w => w.Position == position))
                        writtenOut.Add((part, symbol, source[position..(close + 1)], position));
            ChordReading? ReadingAt(int tick) => readings.FirstOrDefault(r => r.StartTick <= tick && tick < r.EndTick);
            string how = !isWritten.Contains(true) ? "read from the notes"
                : isWritten.All(w => w) || dense && Enumerable.Range(0, beats.Count).All(i => isWritten[i] || symbols[i] == "r") ? "as written"
                : "as written where a chord is named, the rest read from the notes";
            lines.Add((sectionName, BarsOfSymbols(symbols, beats), how));
            order.Add(sectionName);

            // What the reading is less sure of, by the section's own bars.
            var alone = new SortedSet<int>();
            var loose = new SortedSet<int>();
            for (int i = 0; i < beats.Count; i++)
                if (!isWritten[i] && ReadingAt(beats[i].Tick) is { Root: >= 0 } r)
                {
                    if (r.Monophonic)
                        alone.Add(barOf[i] + 1);
                    else if (r.Fit < LooseFit)
                        loose.Add(barOf[i] + 1);
                }
            if (alone.Count > 0)
                remarks.Add($"{sectionName}, {BarList(alone)}: one note at a time sounds there, so the chord is chosen for the line, not heard.");
            if (loose.Count > 0)
                remarks.Add($"{sectionName}, {BarList(loose)}: the notes fit the chord loosely — read the bar before relying on it.");
        }
        if (lines.Count == 0)
            return every ? "Nothing in the score sounds a note to read chords from."
                : $"Nothing of {string.Join(", ", asked)} sounds a note to read chords from.";

        var text = new StringBuilder();
        string? key = null;
        try
        {
            key = CompositionChecks.PlayedKeys(root) is { } keys ? $"{CompositionChecks.TonicName(keys.Home.Tonic)} {keys.Home.Mode}" : null;
        }
        catch
        {
            // The chords are said without the key.
        }
        text.Append("The chords").Append(every ? " of the score" : $" of {string.Join(", ", asked)}")
            .Append(key != null ? $", in {key}" : "").Append(", a section a line, a bar between bar lines (`.` holds the chord before, `r` is no chord):\n");
        text.Append("chords {\n");
        foreach (var (sectionName, bars, how) in lines)
            text.Append("  section ").Append(sectionName).Append(" { ").Append(bars).Append(" }   // ").Append(how).Append('\n');
        text.Append("}\n");
        var form = CompositionChecks.PlayedSections(root);
        if (form.Count > 0)
            text.Append("Form, as played: ").Append(string.Join(" ", form)).Append('\n');
        // A row's entry may carry its shape (`G(320003)`): the symbol is what stands before it.
        var distinct = lines.SelectMany(l => l.Bars.Split([' ', '|'], System.StringSplitOptions.RemoveEmptyEntries))
            .Select(w => w.Contains('(') ? w[..w.IndexOf('(')] : w)
            .Where(w => w is not ("." or "r") && ChordStructure.TryParseChordEntry(w, out _)).Distinct().ToList();
        if (distinct.Count > 0)
            text.Append("Its chords, in the order they first come: ").Append(string.Join(" ", distinct))
                .Append(" — for a guitar's or a string player's shapes, give these to chordVoicings as `chords`.\n");
        if (silent.Count > 0)
            text.Append("Nothing ").Append(every ? "" : "of these parts ").Append("sounds in ").Append(string.Join(", ", silent)).Append(".\n");
        if (!every && rows.Count > 0)
            text.Append("The score also writes a chords row, left out here as it is no part's: ask without `parts` to read it.\n");
        // One line a chord as it is written, most places first within its name: copied into an edit's
        // `old`, kept to the part, the chords of a part are replaced without the part being read for them.
        foreach (var byPart in writtenOut.GroupBy(w => w.Part))
        {
            text.Append("As ").Append(byPart.Key).Append(" writes them (each the text of an `<…>` chord — give it as `old` to lilysharp_proposeScore's `edits`, with `part: \"")
                .Append(byPart.Key).Append("\"` and `all: true`):\n");
            foreach (var chord in byPart.GroupBy(w => (w.Symbol, w.Text)).OrderBy(g => distinct.IndexOf(g.Key.Symbol) is >= 0 and var at ? at : int.MaxValue)
                .ThenByDescending(g => g.Count()).Take(MostWrittenOut))
                text.Append("  ").Append(chord.Key.Symbol).Append(" = ").Append(chord.Key.Text)
                    .Append(chord.Count() == 1 ? " (1 place)" : $" ({chord.Count()} places)").Append('\n');
            int more = byPart.GroupBy(w => (w.Symbol, w.Text)).Count() - MostWrittenOut;
            if (more > 0)
                text.Append("  … and ").Append(more).Append(" more: read the part (lilysharp_readScore, `part`) for them.\n");
        }
        foreach (string remark in remarks)
            text.Append(remark).Append('\n');
        var ranges = PartRanges.Lines(source).Where(l => every || asked.Any(a => l.StartsWith(a + ":", System.StringComparison.Ordinal))).ToList();
        if (ranges.Count > 0)
            text.Append("Ranges, as the parts sound: ").Append(string.Join("; ", ranges)).Append(".\n");
        text.Append("The chords read from the notes are a reading, not the writer's word: a name the score writes always stands.");
        return text.ToString();
    }

    /// <summary>One section's chords, a symbol a beat: what its notes name, and the rest as read.</summary>
    /// <param name="Beats">The section's beats, a bar at each accent.</param>
    /// <param name="Symbols">The chord on each beat, <c>r</c> for none.</param>
    /// <param name="Written">Whether the beat's chord is one the score names.</param>
    /// <param name="BarOf">The bar each beat is in, from 0.</param>
    /// <param name="Readings">The chords read from the notes.</param>
    /// <param name="Dense">The score names a chord in half the section's bars or more.</param>
    /// <param name="Struck">The chords the parts strike (two notes or more written at one place), each
    /// with the part, where it is written and the chord its beat is read as.</param>
    private sealed record SectionChords(IReadOnlyList<(int Tick, bool Accent)> Beats, string[] Symbols, bool[] Written,
        int[] BarOf, IReadOnlyList<ChordReading> Readings, bool Dense,
        IReadOnlyList<(string Part, int Position, string Symbol)> Struck);

    /// <summary>The most written-out chords listed for a part.</summary>
    private const int MostWrittenOut = 24;

    /// <summary>
    /// Reads one section as the score plays it alone — the one reading behind the editor's
    /// "Add Chord Track" (<see cref="AddReadChordTracks"/>) and the arranging chat's progression
    /// (<see cref="DescribeProgression"/>; owner's decision 2026-11-11: the two share it, so that
    /// a score is not read two ways). The notes of the parts <paramref name="reads"/> takes are
    /// read beat by beat (<see cref="HarmonyReader"/>); with <paramref name="names"/>, a chord
    /// the score names on one of those notes stands: named in half the bars or more, the names
    /// alone, each held to the next; in fewer, each over the stretch the notes read as one chord
    /// where it stands. Null when nothing of those parts sounds.
    /// </summary>
    /// <param name="names">The names written on notes (<see cref="WrittenChords"/>), or null to read the notes alone.</param>
    /// <param name="melody">The chords a line alone is given, for the section's key.</param>
    private static SectionChords? ReadSection(string source, SyntaxTree tree, string? scoreName, string sectionName,
        (char Tonic, int Sharps) home, System.Func<string, bool> reads,
        (Dictionary<int, string> Named, HashSet<int> FromNotes)? names, System.Func<char, int, MelodyChords> melody)
    {
        MidiFile midi;
        try
        {
            midi = MidiOfSection(source, scoreName, sectionName);
        }
        catch
        {
            return null;
        }
        var beats = MetronomeBeats.Of(midi, midi.TicksPerQuarterNote, out int end);
        var (tonic, sharps) = SectionKey(tree, sectionName) ?? home;

        var heard = new List<SoundingNote>();
        // Each place a part writes, with the instant it first sounds and how many notes sound there then.
        var together = new Dictionary<(string Part, int Position), (int Tick, int Notes)>();
        var written = new SortedDictionary<int, string>();
        var sounding = new Dictionary<(int Tick, int Position), List<int>>();
        foreach (var track in midi.Tracks)
            foreach (var n in track.Notes)
            {
                if (n.Channel == 9)
                    continue;
                string part = n.Part ?? track.Name;
                // A row a score sounds is no part.
                if (part.EndsWith(ChordRowPart, System.StringComparison.Ordinal) || !reads(part))
                    continue;
                heard.Add(new SoundingNote(n.StartTick, System.Math.Max(1, n.DurationTicks), n.Pitch));
                var place = (part, n.SourcePos);
                together[place] = together.TryGetValue(place, out var seen) ? (seen.Tick, seen.Notes + (n.StartTick == seen.Tick ? 1 : 0)) : (n.StartTick, 1);
                if (names is not { } written1)
                    continue;
                if (written1.Named.TryGetValue(n.SourcePos, out var symbol))
                    written.TryAdd(n.StartTick, symbol);
                else if (written1.FromNotes.Contains(n.SourcePos))
                {
                    if (!sounding.TryGetValue((n.StartTick, n.SourcePos), out var pitches))
                        sounding[(n.StartTick, n.SourcePos)] = pitches = [];
                    pitches.Add(n.Pitch);
                }
            }
        foreach (var ((tick, _), pitches) in sounding)
            if (!written.ContainsKey(tick) && NameOfPitches(pitches, sharps) is { } derived)
                written[tick] = derived;
        if (beats.Count == 0 || heard.Count == 0)
            return null;

        var readings = HarmonyReader.Read(heard, beats, end, melody: melody(tonic, sharps));
        ChordReading? ReadingAt(int tick) => readings.FirstOrDefault(r => r.StartTick <= tick && tick < r.EndTick);
        int BeatOf(int tick)
        {
            int at = 0;
            for (int i = 0; i < beats.Count && beats[i].Tick <= tick; i++)
                at = i;
            return at;
        }
        var barOf = new int[beats.Count];
        for (int i = 0, bar = 0; i < beats.Count; i++)
            barOf[i] = i > 0 && beats[i].Accent ? ++bar : bar;
        int barCount = barOf[^1] + 1;

        var symbols = new string[beats.Count];
        var isWritten = new bool[beats.Count];
        for (int i = 0; i < beats.Count; i++)
            symbols[i] = ReadingAt(beats[i].Tick) is { Root: >= 0 } r ? r.Symbol(sharps) : "r";
        var onsets = written.Select(w => (Beat: BeatOf(w.Key), Symbol: w.Value)).ToList();
        bool dense = onsets.Count > 0 && onsets.Select(o => barOf[o.Beat]).Distinct().Count() * 2 >= barCount;
        for (int k = 0; k < onsets.Count; k++)
        {
            int from = onsets[k].Beat, next = k + 1 < onsets.Count ? onsets[k + 1].Beat : beats.Count;
            // Named sparsely, a name holds while the notes stay on the chord read where it stands.
            int until = dense || ReadingAt(beats[from].Tick) is not { } here ? (dense ? next : from + 1)
                : System.Math.Min(next, 1 + Enumerable.Range(from, beats.Count - from).TakeWhile(i => beats[i].Tick < here.EndTick).Last());
            for (int i = from; i < System.Math.Max(until, from + 1) && i < beats.Count; i++)
            {
                symbols[i] = onsets[k].Symbol;
                isWritten[i] = true;
            }
        }
        var struck = together.Where(p => p.Value.Notes >= 2).OrderBy(p => p.Value.Tick)
            .Select(p => (p.Key.Part, p.Key.Position, symbols[BeatOf(p.Value.Tick)])).ToList();
        return new SectionChords(beats, symbols, isWritten, barOf, readings, dense, struck);
    }

    private static string BarList(SortedSet<int> bars)
        => (bars.Count == 1 ? "bar " : "bars ") + string.Join(", ", bars.Take(8)) + (bars.Count > 8 ? ", …" : "");

    /// <summary>Each section's chords row as it is written — the first row's, where a section
    /// has more than one — its bars on one line: a row in the section, or a by-part track's
    /// inner <c>section NAME { }</c>.</summary>
    private static Dictionary<string, (string Name, string Bars)> WrittenRows(SyntaxNode root, string source)
    {
        var rows = new Dictionary<string, (string Name, string Bars)>(System.StringComparer.Ordinal);
        static string OneLine(string text) => string.Join(" ", text.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
        foreach (var block in root.DescendantNodes<ChordPartBlockSyntax>())
        {
            string name = block.PartName ?? "chords";
            var inner = block.Sections.ToList();
            if (inner.Count > 0)
            {
                foreach (var section in inner)
                {
                    string text = source[section.Span.Start..section.Span.End];
                    int open = text.IndexOf('{'), close = text.LastIndexOf('}');
                    if (open >= 0 && close > open && OneLine(text[(open + 1)..close]) is { Length: > 0 } bars)
                        rows.TryAdd(section.Name.Text, (name, bars));
                }
                continue;
            }
            for (var p = block.Parent; p != null; p = p.Parent)
                if (p is SectionDeclarationSyntax section)
                {
                    if (OneLine(source[block.OpenBrace.Span.End..block.CloseBrace.Span.Start]) is { Length: > 0 } bars)
                        rows.TryAdd(section.Name.Text, (name, bars));
                    break;
                }
        }
        return rows;
    }

    /// <summary>The chords a source names on its notes, by where each is written (the position
    /// its notes carry): the name, and the places a chord is named by the notes it stands on (a
    /// bare <c>@chord</c> or <c>@diagram</c>).</summary>
    private static (Dictionary<int, string> Named, HashSet<int> FromNotes) WrittenChords(SyntaxNode root, int sharps)
    {
        var named = new Dictionary<int, string>();
        var fromNotes = new HashSet<int>();
        void Read(ChordAnnotation words, SyntaxNode site, SyntaxNode asker)
        {
            if (words is { Symbol: { } symbol, Structure: not null })
                named[site.Span.Start] = symbol;
            else if (words.NamesFromDiagram && (ChordAnnotation.PartTuningOf(asker) ?? Tablature.Tunings.Guitar) is { } tuning
                && words.NamingShape(null, tuning) is { } shape && ChordAnnotation.NameFromFrets(shape.Frets, shape.Tuning, sharps) is { } sounded)
                named[site.Span.Start] = Printed(sounded, sharps);
            else if (words.IsBare || words.NamesFromDiagram)
                fromNotes.Add(site.Span.Start);
        }
        foreach (var item in root.DescendantNodes<ChordSyntax>())
            if (item.IsShapeChord)
                Read(ShapeChords.Words(item), item, item);
        foreach (var mark in root.DescendantNodes<MusicMarkSyntax>())
        {
            if (mark.Parent is not { } site || named.ContainsKey(site.Span.Start))
                continue;
            if (ChordAnnotation.Of(mark) is { } words)
                Read(words, site, mark);
            // A diagram is a shape with no name: the chord it sounds is the name.
            else if (string.Equals(mark.Name, "diagram", System.StringComparison.Ordinal))
                Read(ChordAnnotation.Parse([.. mark.Arguments.Select(a => a.Text).Where(ChordShapes.IsShape)]), site, mark);
        }
        return (named, fromNotes);
    }

    private static string Printed(ChordStructure chord, int sharps)
        => chord.PrintedSymbol(ChordSpelling.Canonical, sharps).Text.Replace('♭', 'b').Replace('♯', '#');

    /// <summary>The chord some sounding pitches name, three pitch classes at least; null when they name none.</summary>
    private static string? NameOfPitches(List<int> pitches, int sharps)
    {
        var classes = pitches.OrderBy(p => p).Select(p => ((p % 12) + 12) % 12).Distinct().ToList();
        if (classes.Count < 3)
            return null;
        var members = classes.Select(pc => ChordAnnotation.SpellPitchClass(pc, sharps)).ToList();
        return ChordStructure.TryRecognize(members, out var chord) && chord != null ? Printed(chord, sharps) : null;
    }

    /// <summary>A symbol a beat as a chords row's bars: a bar per accented beat (a pickup's beats
    /// one bar), each divided into as few equal slots as its changes need — one, two (on an even
    /// count of beats) or one a beat — a held chord a <c>.</c>, no chord an <c>r</c>.</summary>
    internal static string BarsOfSymbols(IReadOnlyList<string> symbols, IReadOnlyList<(int Tick, bool Accent)> beats)
    {
        var starts = new List<int>();
        for (int i = 0; i < beats.Count; i++)
            if (i == 0 || beats[i].Accent)
                starts.Add(i);
        var bars = new List<string>();
        for (int b = 0; b < starts.Count; b++)
        {
            int first = starts[b], n = (b + 1 < starts.Count ? starts[b + 1] : beats.Count) - first;
            var bar = Enumerable.Range(first, n).Select(i => symbols[i]).ToList();
            int slots = 1;
            if (bar.Distinct().Count() > 1)
                slots = n % 2 == 0 && Enumerable.Range(0, n).All(k => k % (n / 2) == 0 || bar[k] == bar[k - 1]) ? 2 : n;
            var cells = new List<string>();
            for (int s = 0; s < slots; s++)
            {
                string symbol = bar[s * (n / slots)];
                cells.Add(s > 0 && symbol == bar[(s - 1) * (n / slots)] ? "." : symbol);
            }
            bars.Add(string.Join(" ", cells));
        }
        return string.Join(" | ", bars) + " |";
    }
}
