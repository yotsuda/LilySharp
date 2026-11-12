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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LilySharp.Core.Music;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The part of a NAMED SINGER, made of that singer's lines (owner's decisions 2026-11-12,
/// docs/lyrics-singer-design.md §13): <c>singer carol female sings on lead up 3 in Chorus</c>
/// says what carol sings, and <c>score { staff carol  lyrics carol }</c> prints it — the lead's notes of
/// the Chorus a third above along the key, bars of rest where carol does not sing, carol's
/// words under them. The harmony's notes are the singer line's to know: nothing writes them twice.
/// </summary>
/// <remarks>
/// <para>
/// HOW: the part is WRITTEN, as Lily# text, after the file's own (the way <c>using</c> adds a
/// file: the document stays the prefix, so every position in it holds), and everything that
/// reads a score — the page, the MIDI, the MusicXML, the LilyPond twin — reads it as any other
/// part. One house makes it, so they cannot disagree. The part is called as the singer is; its
/// words are a track of their own (<see cref="LyricSinger.WordsOf"/>), the sung sections' alone.
/// </para>
/// <para>
/// THE NOTES: each section's music is the copy of the line's part's, its pitch NAMES moved
/// by the line's interval along the key each is written in (the walk says which:
/// <c>MeasureCollector.WrittenKeyObserver</c>) and its octave marks set so that every note
/// lands the interval above where the part's own note does
/// (<see cref="OctaveModeConverter.Pin"/>). By the LETTER: a note the key does not hold is
/// read as the letter it is written on and lands on the key's own note the interval away —
/// over g-sharp in a minor a third above is b. LILYSHARP-OWN, a reading.
/// </para>
/// <para>
/// NOT MADE, and said at the score's item (LYS4060): a singer nobody names, one called as a
/// part is, a line that sings one voice of a part, and a moved line whose music calls a phrase
/// (the phrase's notes are not the copy's to move).
/// </para>
/// </remarks>
public static class SingerParts
{
    /// <summary>Whether a score of <paramref name="tree"/> prints a singer: the question the
    /// houses ask on every keystroke, of the scores alone.</summary>
    public static bool Wanted(SyntaxTree tree) => Items(tree.GetRoot()).Any();

    /// <summary>The scores' items that name a singer — <c>staff carol</c>, her notes, and
    /// <c>lyrics carol</c>, her words — each with the singer's name. A file with no named singer
    /// has none, and is asked nothing more than whether it names one.</summary>
    private static IEnumerable<(SyntaxNode Node, string Person)> Items(SyntaxNode root)
    {
        if (LyricSinger.SingerNames(root).Count == 0 && LyricSinger.SingerWordNames(root).Count == 0)
            yield break;
        foreach (var score in root.ChildNodes().OfType<RenderDeclarationSyntax>())
        {
            foreach (var staff in score.DescendantNodes<StaffRenderSyntax>())
                if (staff.IsSinger)
                    yield return (staff, staff.PartToken!.Text);
            foreach (var row in score.DescendantNodes<LyricsRowRenderSyntax>())
                if (row.IsSingerWords)
                    yield return (row, row.NameToken!.Text);
        }
    }

    private sealed record Cell(int Made, int From, int Length, int Interval, int Octaves);

    /// <summary>The line that begins what <see cref="Expand"/> writes after a file's own text.</summary>
    public const string Marker = "// (written by Lily#, not the file's own text: what the singers its scores print sing)";

    /// <summary>Where the file's own text ends in <paramref name="expanded"/>: its length where
    /// nothing was written after it.</summary>
    public static int MadeFrom(string expanded)
    {
        int at = expanded.LastIndexOf(Marker, StringComparison.Ordinal);
        return at < 0 ? expanded.Length : at;
    }

    /// <summary>
    /// <paramref name="diagnostics"/> without those that stand in what was written after the
    /// file's own text: the made parts are copies, and what there is to say of a copy is said
    /// of the music it copies (a note out of its instrument's range is the singer's octave,
    /// not a slip of the writer's).
    /// </summary>
    public static IReadOnlyList<Diagnostic> OwnOf(IEnumerable<Diagnostic> diagnostics, string expanded)
    {
        int end = MadeFrom(expanded);
        return diagnostics.Where(d => d.Span.Start < end).ToList();
    }

    /// <summary>
    /// <paramref name="text"/> with the parts of the singers its scores print written after it,
    /// or <paramref name="text"/> itself where none is printed or none could be made;
    /// <paramref name="diagnostics"/> says what was not made, at the score's item.
    /// </summary>
    public static string Expand(string text, SyntaxTree tree, out IReadOnlyList<Diagnostic> diagnostics)
    {
        var said = new List<Diagnostic>();
        diagnostics = said;
        var root = tree.GetRoot();
        var items = Items(root).ToList();
        if (items.Count == 0)
            return text;
        // A file that does not parse says so where it does not; nothing is made of it, and
        // nothing more is said at each singer's staff (owner's report 2026-11-12: a singer line
        // that forgot its `sings` came with three errors at the score's items besides its own).
        if (tree.HasErrors)
            return text;

        var lines = LyricSinger.LinesOf(root);
        var parts = root.ChildNodes().OfType<PartDeclarationSyntax>().ToList();
        var bars = SectionBarCounts.CanonicalByNameSyntactic(root);
        var sections = root.DescendantNodes<SectionDeclarationSyntax>().Select(s => s.SectionName)
            .Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        string nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var made = new StringBuilder(text);
        if (text.Length > 0 && text[^1] != '\n')
            made.Append(nl);
        made.Append(Marker).Append(nl);
        var cells = new List<Cell>();
        int partsMade = 0;

        // One part for each staff asked for: a singer's, as the file's top level says what she
        // sings, and one of a score's own where that score's singer lines say it (the Japanese
        // score's alice sings the Japanese words: LyricSinger.PartOfItem names the two apart).
        foreach (var (item, name) in items.GroupBy(i => LyricSinger.PartOfSinger(i.Node, i.Person), StringComparer.Ordinal).Select(g => g.First()).ToList())
        {
            string partName = LyricSinger.PartOfSinger(item, name);
            void No(string why) => said.Add(Diagnostic.Error(item.Span, DiagnosticCodes.SingerNotPrinted, why));

            SyntaxNode? score = item;
            while (score != null && score is not RenderDeclarationSyntax)
                score = score.Parent;
            var own = LyricSinger.LinesOfSingerIn(root, name, score as RenderDeclarationSyntax);
            if (own.Count == 0)
            {
                No($"{name} sings nothing in this score: its own singer lines take the place of the file's for their part, and none of them is {name}'s.");
                continue;
            }
            // (A PART called as the singer is never comes here: `staff carol` is then that part —
            // which is what her notes written out are — and nothing is made.)
            if (own.Any(l => l.Voice != null))
            {
                No($"{name} sings one voice of a part, which is not printed as a staff of its own yet: write that voice as a part.");
                continue;
            }
            if (own[0].Part is not { } first || parts.FirstOrDefault(p => p.Name.Text == first) is not { } from)
            {
                No($"What {name} sings is not printed: the singer's line names no part the file has.");
                continue;
            }

            var music = new StringBuilder();
            var words = new StringBuilder();
            var pending = new List<(int At, Cell Cell)>();
            string? language = null;
            bool refused = false;
            foreach (string section in sections)
            {
                var line = own.FirstOrDefault(l => l.Part != null
                    && (l.Node.Sections.Count == 0 || l.Node.Sections.Any(s => s.Text == section)));
                var cell = line == null ? null : MusicOf(root, line.Part!, section);
                if (line == null || cell == null || Inner(text, cell) is not { } braces)
                {
                    // Where the singer does not sing: the section's bars, at rest.
                    if (bars.TryGetValue(section, out int count) && count > 0)
                        music.Append("  section ").Append(section).Append(" { R*").Append(count).Append(" }").Append(nl);
                    continue;
                }
                bool moves = line.Node.Interval is > 1 or < -1 || line.Node.Octaves != 0;
                if (moves && cell.DescendantNodes<VariableReferenceSyntax>().Any())
                {
                    No($"What {name} sings in {section} is not printed: the music there calls a phrase, whose notes are not moved. Write the harmony out as a part.");
                    refused = true;
                    break;
                }
                int innerStart = braces.Start, innerEnd = braces.End;
                music.Append("  section ").Append(section).Append(" {");
                pending.Add((music.Length, new Cell(0, innerStart, innerEnd - innerStart, line.Node.Interval, line.Node.Octaves)));
                music.Append(text, innerStart, innerEnd - innerStart).Append('}').Append(nl);

                if (!GeneratedLyrics.IsGenerated(line.Track) && WordsOf(root, line.Track, section) is { } block
                    && Inner(text, block.Body) is { } wordBraces)
                {
                    language ??= block.Language;
                    words.Append("  section ").Append(section).Append(" {")
                        .Append(text, wordBraces.Start, wordBraces.Length).Append('}').Append(nl);
                }
            }
            if (refused)
                continue;

            // The part: the header of the part the singer sings on (its instrument, its clef,
            // its octave), its own words named in place of that part's, called as the singer is.
            string header = string.Join(" ", from.ChildNodes()
                .Where(n => n is not (SyntaxTokenNode or SectionDeclarationSyntax or PartLyricsDeclarationSyntax))
                .Select(n => text[n.Span.Start..n.Span.End]));
            // A voice that sings its part an octave down throughout is written in the treble
            // clef an octave down (a tenor's): its notes stand where the part's do.
            var sung = own.Where(l => l.Part != null).ToList();
            var defaults = Semantics.PartHeaderDefaults.Read(from);
            if (sung.Count > 0 && sung.All(l => l.Node.Octaves == -1) && !System.Text.RegularExpressions.Regex.IsMatch(header, @"\bclef\b")
                && defaults.Clef == Svg.Model.ClefType.Treble)
                header += " clef treble_8";
            string track = LyricSinger.WordsOf(partName);
            partsMade++;
            made.Append("part ").Append(partName).Append(" \"").Append(char.ToUpperInvariant(name[0])).Append(name, 1, name.Length - 1).Append("\" { ")
                .Append(header);
            if (words.Length > 0)
                made.Append(" lyrics ").Append(track);
            made.Append(nl);
            int musicAt = made.Length;
            made.Append(music).Append('}').Append(nl);
            foreach (var (at, cell) in pending)
                cells.Add(cell with { Made = musicAt + at });
            if (words.Length > 0)
            {
                made.Append("lyrics ").Append(track);
                if (language != null)
                    made.Append(' ').Append(LyricLanguage.Keyword).Append(' ').Append(language);
                made.Append(" {").Append(nl).Append(words).Append('}').Append(nl);
            }
        }
        if (partsMade == 0)
            return text;

        string written = made.ToString();
        if (!cells.Any(c => c.Interval is > 1 or < -1 || c.Octaves != 0))
            return written;
        if (Moved(written, MadeFrom(written), cells, lines.Where(l => l.Person != null && l.Part != null).Select(l => l.Part!).Distinct().ToList()) is { } moved)
            return moved;
        foreach (var item in items)
            said.Add(Diagnostic.Error(item.Node.Span, DiagnosticCodes.SingerNotPrinted,
                "What this singer sings could not be printed: the notes of the singer's lines could not be moved. Mend the score's other errors first."));
        return text;
    }

    /// <summary>
    /// <paramref name="written"/> with the notes of its made cells moved as their lines say:
    /// the names along the key, the marks so that each lands the interval from the note it copies.
    /// Null where the file cannot be walked.
    /// </summary>
    private static string? Moved(string written, int madeFrom, List<Cell> cells, List<string> sungParts)
    {
        // Where each copied note stands is asked of the note it copies, so the parts the
        // singers sing on have to be played: a score of them alone goes after the text for the
        // asking (a singer's own sheet, `score { staff carol  lyrics carol }`, plays no part but the made one).
        var tree = SyntaxTree.Parse(written + "score __singers { " + string.Join(" ", sungParts.Select(p => "staff " + p)) + " }\n");
        if (tree.HasErrors)
            return null;
        var octaves = new Dictionary<int, int>();
        var keys = new Dictionary<int, int>();
        if (!OctaveModeConverter.Walk(tree, (pos, octave) => { octaves.TryAdd(pos, octave); return octave; },
                writtenKey: (pos, sharps) => keys.TryAdd(pos, sharps)))
            return null;

        var root = tree.GetRoot();
        bool contracted = KeyRespeller.UsesContractedFlats(root);
        var renames = new List<(int Start, int End, string Name)>();
        var landing = new List<(int At, int Octave)>();
        foreach (var pitch in root.DescendantNodes<PitchSyntax>())
        {
            int at = pitch.SourceStart;
            if (at < madeFrom || at >= written.Length || pitch.Parent is not (NoteSyntax or ChordSyntax or ArpeggioSyntax))
                continue;
            var cell = cells.FirstOrDefault(c => at >= c.Made && at < c.Made + c.Length);
            if (cell == null)
                continue;
            int copied = at - cell.Made + cell.From;
            // A note no form plays resolved nowhere: it is left as it is written.
            if (!octaves.TryGetValue(copied, out int octave) && !octaves.TryGetValue(at, out octave))
                continue;
            if (!keys.TryGetValue(copied, out int sharps) && !keys.TryGetValue(at, out sharps))
                continue;
            int step = KeySpelling.StepOf(pitch.BaseName);
            if (step < 0)
                continue;
            int steps = cell.Interval > 1 ? cell.Interval - 1 : cell.Interval < -1 ? cell.Interval + 1 : 0;
            int carry = 0;
            if (steps != 0)
            {
                int to = step + steps;
                carry = (int)Math.Floor(to / 7.0);
                int landed = to - (7 * carry);
                string name = KeyRespeller.Name(landed, KeySpelling.Alteration(landed, sharps), contracted);
                var span = pitch.PitchToken.Span;
                if (name != written[span.Start..span.End])
                    renames.Add((span.Start, span.End, name));
            }
            landing.Add((at, octave + carry + cell.Octaves));
        }

        // The names first (later ones first, so the offsets before them hold); each landing
        // is then asked where its note stands after them.
        var text = new StringBuilder(written);
        foreach (var (start, end, name) in renames.OrderByDescending(r => r.Start))
            text.Remove(start, end - start).Insert(start, name);
        renames.Sort((a, b) => a.Start.CompareTo(b.Start));
        var targets = new Dictionary<int, int>(landing.Count);
        int grown = 0, next = 0;
        foreach (var (at, octave) in landing.OrderBy(l => l.At))
        {
            while (next < renames.Count && renames[next].Start < at)
            {
                grown += renames[next].Name.Length - (renames[next].End - renames[next].Start);
                next++;
            }
            targets[at + grown] = octave;
        }
        return OctaveModeConverter.Pin(text.ToString(), targets);
    }

    /// <summary>What stands between the braces of a block that ends with its closing one (a
    /// part's block, a section, a lyrics block), or null.</summary>
    private static TextSpan? Inner(string text, SyntaxNode block)
    {
        int open = text.IndexOf('{', block.Span.Start), close = block.Span.End - 1;
        return open >= 0 && open < close && close < text.Length && text[close] == '}'
            ? new TextSpan(open + 1, close - open - 1)
            : null;
    }

    /// <summary>The music of <paramref name="part"/> in <paramref name="section"/>: the part's
    /// block in the section, or the section in the part; null where it writes none.</summary>
    private static SyntaxNode? MusicOf(SyntaxNode root, string part, string section)
    {
        foreach (var node in root.ChildNodes())
        {
            switch (node)
            {
                case SectionDeclarationSyntax s when s.SectionName == section:
                    if (s.ChildNodes().OfType<PartBlockSyntax>().FirstOrDefault(b => b.Name == part) is { } block)
                        return block;
                    break;
                case PartDeclarationSyntax p when p.Name.Text == part:
                    if (p.ChildNodes().OfType<SectionDeclarationSyntax>().FirstOrDefault(s => s.SectionName == section) is { } cell)
                        return cell;
                    break;
            }
        }
        return null;
    }

    /// <summary>The words of <paramref name="track"/> in <paramref name="section"/>: the track's
    /// block in the section, or the section in the track's block (whose body is then the
    /// section's); null where it writes none there.</summary>
    private static (SyntaxNode Body, string? Language)? WordsOf(SyntaxNode root, string track, string section)
    {
        foreach (var node in root.ChildNodes())
        {
            switch (node)
            {
                case SectionDeclarationSyntax s when s.SectionName == section:
                    if (s.ChildNodes().OfType<LyricsBlockSyntax>().FirstOrDefault(b => b.VoiceName == track) is { } block)
                        return (block, block.Language);
                    break;
                case LyricsBlockSyntax b when b.VoiceName == track:
                    if (b.ChildNodes().OfType<SectionDeclarationSyntax>().FirstOrDefault(s => s.SectionName == section) is { } cell)
                        return (cell, b.Language);
                    break;
            }
        }
        return null;
    }
}
