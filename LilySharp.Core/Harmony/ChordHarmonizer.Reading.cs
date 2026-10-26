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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Harmony;

public static partial class ChordHarmonizer
{
    /// <summary>
    /// Adds a chords track read from the music of one score (owner's decisions 2026-10-26,
    /// docs/harmony-analysis-design.md): the chords the score's parts sound together, beat by
    /// beat (<see cref="HarmonyReader"/>), a line alone given the chord it spells (<see cref="MelodyChords"/>) — or,
    /// with <paramref name="melodyOnly"/>, every bar harmonized from the score's top staff alone.
    /// <paramref name="scoreName"/> picks the score as the preview does
    /// (<see cref="RenderSpecParser.ChooseDeclared"/>); only its parts are read, so a score made
    /// for it chooses what the chords are read from. The track is written where the old command
    /// wrote it (a by-part file: one top-level track; by section: a block in each section) and
    /// placed in that score above its top staff, a row of its own (the names; `as roman` written
    /// after it shows the degrees — owner's decision 2026-10-26: one row, not both).
    /// <paramref name="richness"/> is how rich the chords chosen for a melody are. Null when nothing sounds.
    /// </summary>
    public static (string Text, string? Info)? AddReadChordTracks(string source, string? scoreName, bool melodyOnly,
        ChordRichness richness = ChordRichness.Standard)
    {
        var tree = SyntaxTree.Parse(source);
        var root = tree.GetRoot();
        var declared = RenderSpecParser.ChooseDeclared(tree, scoreName);
        var spec = declared?.Spec;
        string? topPart = declared?.Declaration.DescendantNodes<StaffRenderSyntax>().FirstOrDefault()?.PartToken?.Text
            ?? DetectVoice(root);
        string name = FreeTrackName(root);
        var (homeTonic, homeSharps) = ReadKey(source);

        // Each section once, in the order written, read as that score plays it alone.
        var bars = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var order = new List<string>();
        var unwritten = new List<string>();
        foreach (var section in root.DescendantNodes<SectionDeclarationSyntax>())
        {
            string sectionName = section.Name.Text;
            if (bars.ContainsKey(sectionName) || unwritten.Contains(sectionName))
                continue;
            var midi = MidiOfSection(source, scoreName, sectionName);
            var beats = MetronomeBeats.Of(midi, midi.TicksPerQuarterNote, out int end);
            var notes = melodyOnly ? NotesOfPart(midi, topPart) : HarmonyReader.NotesOf(midi, chordRows: false);
            if (notes.Count == 0 || beats.Count == 0)
            {
                unwritten.Add(sectionName);
                continue;
            }
            var (tonic, sharps) = SectionKey(tree, sectionName) ?? (homeTonic, homeSharps);
            var readings = HarmonyReader.Read(notes, beats, end,
                melody: MelodyChords.OfKey(tonic, sharps, everywhere: melodyOnly, richness));
            bars[sectionName] = BarsOf(readings, beats, sharps);
            order.Add(sectionName);
        }
        if (bars.Count == 0)
            return null;

        // Every insert at its offset in the source as written, applied last first, in its line ends.
        string nl = source.Contains("\r\n") ? "\r\n" : "\n";
        var inserts = new List<(int At, string Text)>();
        var homeless = new List<string>();
        if (PartSectionRegrouper.Detect(root) == Grouping.ByPart)
        {
            // One top-level track, its sections in the order written, after the top staff's part.
            var parts = root.DescendantNodes<PartDeclarationSyntax>().ToList();
            var after = parts.FirstOrDefault(p => p.Name.Text == topPart) ?? parts.LastOrDefault();
            if (after == null)
                return null;
            var sb = new StringBuilder(nl + nl + "chords ").Append(name).Append(" {" + nl);
            foreach (var sectionName in order)
                sb.Append("  section ").Append(sectionName).Append(" { ").Append(bars[sectionName]).Append(" }" + nl);
            sb.Append('}');
            inserts.Add((after.Span.End, sb.ToString()));
        }
        else
        {
            // A block in each section, after the top staff's part block (else the section's last).
            foreach (var section in root.DescendantNodes<SectionDeclarationSyntax>())
            {
                if (!bars.TryGetValue(section.Name.Text, out var row))
                    continue;
                var blocks = section.DescendantNodes<PartBlockSyntax>().ToList();
                var after = blocks.FirstOrDefault(b => b.PartName.Text == topPart) ?? blocks.LastOrDefault();
                if (after == null)
                {
                    homeless.Add(section.Name.Text);
                    continue;
                }
                inserts.Add((after.Span.End, nl + IndentOf(source, after.Span.Start) + "chords " + name + " { " + row + " }"));
            }
            if (inserts.Count == 0)
                return null;
        }

        var said = new List<string>();
        if (unwritten.Count > 0)
            said.Add("Nothing of this score sounds in " + string.Join(", ", unwritten) + ".");
        if (homeless.Count > 0)
            said.Add("No part block to write the chords beside in " + string.Join(", ", homeless.Distinct()) + ".");
        if (declared == null)
            said.Add("Place `chords " + name + "` in a score to show it.");
        else if (RowAt(declared.Value.Declaration) is int at)
            inserts.Add((at, RowText(source, at, name, nl)));

        var text = new StringBuilder(source);
        foreach (var (at, insert) in inserts.OrderByDescending(i => i.At))
            text.Insert(at, insert);
        return (text.ToString(), said.Count > 0 ? string.Join(" ", said) : null);
    }

    // The spaces a line starts with, up to the offset.
    private static string IndentOf(string source, int offset)
    {
        int start = source.LastIndexOf('\n', System.Math.Max(0, offset - 1)) + 1;
        int end = start;
        while (end < offset && source[end] is ' ' or '\t')
            end++;
        return source[start..end];
    }

    // One section as the score plays it alone. The exporter plays a form of the tree it exports
    // (it meets the form on its walk), so the section's form is written into a copy of the source.
    private const string ReadingForm = "zzHarmonyReading";

    private static MidiFile MidiOfSection(string source, string? scoreName, string sectionName)
    {
        var tree = SyntaxTree.Parse(source + "\nform " + ReadingForm + " { " + sectionName + " }\n");
        var form = tree.GetRoot().ChildNodes().OfType<FormDeclarationSyntax>().Last(f => f.NameText == ReadingForm);
        var spec = RenderSpecParser.ChooseDeclared(tree, scoreName)?.Spec;
        return new MidiExporter { Form = form, Score = spec }.Export(tree);
    }

    // `harmony`, or `harmony2`, `harmony3`… when a track of that name is written already — a
    // second reading (another score's parts, the melody alone) is a track of its own.
    private static string FreeTrackName(SyntaxNode root)
    {
        var taken = root.DescendantNodes<ChordPartBlockSyntax>().Select(c => c.PartName).ToHashSet();
        string name = "harmony";
        for (int n = 2; taken.Contains(name); n++)
            name = "harmony" + n;
        return name;
    }

    // The row placed before the top staff: on a line of its own, indented as the staff is, when
    // the staff begins its line (owner's request 2026-10-26); beside it in a one-line score.
    private static string RowText(string source, int at, string name, string nl)
    {
        string indent = IndentOf(source, at);
        int lineStart = source.LastIndexOf('\n', System.Math.Max(0, at - 1)) + 1;
        return lineStart + indent.Length == at
            ? "chords " + name + nl + indent
            : "chords " + name + "  ";
    }

    // Where the track's row goes in the score that was read: before its top staff. Null for a
    // score with no staff.
    private static int? RowAt(RenderDeclarationSyntax declaration)
        => declaration.ChildNodes().FirstOrDefault(n =>
            n is StaffRenderSyntax or TabRenderSyntax
            || n.DescendantNodes<StaffRenderSyntax>().Any() || n.DescendantNodes<TabRenderSyntax>().Any())
            ?.Span.Start;

    // The notes one part plays (the melody-only reading: the score's top staff).
    private static List<SoundingNote> NotesOfPart(MidiFile midi, string? part)
    {
        var notes = new List<SoundingNote>();
        if (part == null)
            return notes;
        foreach (var track in midi.Tracks)
            foreach (var n in track.Notes)
                if (n.Channel != 9 && (n.Part ?? track.Name) == part)
                    notes.Add(new SoundingNote(n.StartTick, System.Math.Max(1, n.DurationTicks), n.Pitch));
        return notes;
    }

    // The key a section writes first (in any of its part blocks), else none.
    private static (char Tonic, int Sharps)? SectionKey(SyntaxTree tree, string sectionName)
    {
        foreach (var section in tree.GetRoot().DescendantNodes<SectionDeclarationSyntax>())
        {
            if (section.Name.Text != sectionName)
                continue;
            var m = KeyDeclRegex().Match(section.ToFullString());
            if (m.Success)
                return (char.ToLowerInvariant(m.Groups[1].Value[0]),
                    KeySpelling.SharpsFor(m.Groups[1].Value, m.Groups[2].Value) ?? 0);
        }
        return null;
    }

    /// <summary>
    /// The readings as a chords row's bars: a bar per accented beat (a pickup's beats one bar),
    /// each divided into as few equal slots as its changes need — one, two (on an even count of
    /// beats) or one a beat — a held chord a <c>.</c>, no chord an <c>r</c>.
    /// </summary>
    internal static string BarsOf(IReadOnlyList<ChordReading> readings, IReadOnlyList<(int Tick, bool Accent)> beats, int sharps)
    {
        string At(int tick)
        {
            foreach (var r in readings)
                if (r.StartTick <= tick && tick < r.EndTick)
                    return r.Symbol(sharps);
            return "r";
        }
        var starts = new List<int>();
        for (int i = 0; i < beats.Count; i++)
            if (i == 0 || beats[i].Accent)
                starts.Add(i);
        var bars = new List<string>();
        for (int b = 0; b < starts.Count; b++)
        {
            int first = starts[b], n = (b + 1 < starts.Count ? starts[b + 1] : beats.Count) - first;
            var symbols = Enumerable.Range(first, n).Select(i => At(beats[i].Tick)).ToList();
            int slots = 1;
            if (symbols.Distinct().Count() > 1)
                slots = n % 2 == 0 && Enumerable.Range(0, n).All(k => k % (n / 2) == 0 || symbols[k] == symbols[k - 1]) ? 2 : n;
            var cells = new List<string>();
            for (int s = 0; s < slots; s++)
            {
                string symbol = symbols[s * (n / slots)];
                cells.Add(s > 0 && symbol == symbols[(s - 1) * (n / slots)] ? "." : symbol);
            }
            bars.Add(string.Join(" ", cells));
        }
        return string.Join(" | ", bars) + " |";
    }
}
