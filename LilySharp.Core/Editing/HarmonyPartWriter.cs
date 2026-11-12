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
using System.Text.RegularExpressions;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Writes a harmony OUT, as a part of the file (owner's decisions 2026-11-12,
/// docs/lyrics-singer-design.md §14): a part's notes an interval away along the key — a second
/// violin a third under the first, a singer's harmony — become a part of their own, in the
/// file's own way of writing, to be mended by hand where a third is not the note. For a voice
/// and for an instrument alike.
/// </summary>
/// <remarks>
/// The notes are <see cref="SingerParts"/>'s: the writer asks it for the part of a singer —
/// the file's own, or one it names for the asking, who "sings" the part the interval away — and
/// takes that part's music section by section, so a harmony written out is note for note the
/// harmony a score prints. <see cref="PartWriter"/> then places it as it places any new part:
/// a block beside the part it follows in each section (or sections of its own, in a file
/// written by part), a header after that part's, a staff after it in the scores that show it.
/// </remarks>
public static class HarmonyPartWriter
{
    /// <summary>The file with the harmony written (<see cref="Text"/>), or what kept it from being.</summary>
    public sealed record Result(string? Text, string? Error);

    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private const string AskingScore = "__harmony";

    /// <summary>
    /// A new part <paramref name="name"/>: the notes of <paramref name="from"/> moved by
    /// <paramref name="interval"/> along the key (3 a third above, −6 a sixth below; 0 for none)
    /// and by <paramref name="octaves"/>, in <paramref name="sections"/> (all of them when null
    /// or empty) and at rest in the others.
    /// </summary>
    public static Result WritePart(string source, string from, string name, int interval, int octaves, IReadOnlyList<string>? sections = null)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return new(null, "Fix the errors in the score first — nothing was written.");
        var root = tree.GetRoot();
        name = (name ?? "").Trim();
        if (!Identifier.IsMatch(name))
            return new(null, $"A part needs a name of letters and digits: '{name}' is none.");
        if (!root.ChildNodes().OfType<PartDeclarationSyntax>().Any(p => p.Name.Text == from))
            return new(null, $"The score has no part '{from}'.");
        if (root.ChildNodes().OfType<PartDeclarationSyntax>().Any(p => p.Name.Text == name)
            || LyricSinger.LinesOf(root).Any(l => l.Person == name))
            return new(null, $"'{name}' is in the score already: give the new part another name.");
        if (interval is 1 or -1 || Math.Abs(interval) > 15 || Math.Abs(octaves) > 3 || (interval == 0 && octaves == 0))
            return new(null, "Say how far the new part is from the other: an interval of 2 to 15 above or below, an octave of 1 to 3 either way, or both.");
        var known = root.DescendantNodes<SectionDeclarationSyntax>().Select(s => s.SectionName).ToHashSet(StringComparer.Ordinal);
        if (sections?.FirstOrDefault(s => !known.Contains(s)) is { } unknown)
            return new(null, $"The score has no section '{unknown}'.");

        // Somebody of the new part's name "sings" the part, the interval away, in a score for the asking.
        var line = new StringBuilder("singer ").Append(name).Append(" sings on ").Append(from);
        if (interval != 0)
            line.Append(interval > 0 ? " up " : " down ").Append(Math.Abs(interval));
        if (octaves != 0)
            line.Append(" octave ").Append(octaves);
        if (sections is { Count: > 0 })
            line.Append(" in ").Append(string.Join(" ", sections));
        if (Made(source, name, line.ToString(), out string? why) is not { } made)
            return new(null, why);
        // An instrument an octave down keeps its own clef: the treble clef an octave down is a voice's.
        string header = made.Header;
        var fromPart = root.ChildNodes().OfType<PartDeclarationSyntax>().First(p => p.Name.Text == from);
        if (!Regex.IsMatch(source[fromPart.Span.Start..fromPart.Span.End], @"\bclef\s+treble_8\b"))
            header = string.Join("\n", header.Split('\n').Where(l => l.Trim() != "clef treble_8"));
        return Placed(source, name, from, title: null, header, made.Music);
    }

    /// <summary>Whether "Write NAME's notes out as a part" has something to write: lines of that
    /// name, and no part of it yet.</summary>
    public static bool CanWriteSinger(SyntaxNode root, string person)
        => LyricSinger.LinesOf(root).Any(l => l.Person == person && l.Part != null)
            && !root.ChildNodes().OfType<PartDeclarationSyntax>().Any(p => p.Name.Text == person);

    /// <summary>
    /// What the singer <paramref name="person"/> sings, written out as a part called as the
    /// singer is: the notes, the words as a track of the singer's own, the singer's lines made
    /// one line that sings that part as it stands. A score's <c>singer NAME</c> goes on
    /// printing it — now the written part.
    /// </summary>
    public static Result WriteSinger(string source, string person)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return new(null, "Fix the errors in the score first — nothing was written.");
        var root = tree.GetRoot();
        if (!CanWriteSinger(root, person))
            return new(null, $"No singer is called {person}, or {person} is written out already.");
        string from = LyricSinger.LinesOf(root).First(l => l.Person == person && l.Part != null).Part!;
        if (Made(source, person, null, out string? why) is not { } made)
            return new(null, why);

        // The words: a track of the singer's own, of a name the file has not.
        var taken = root.DescendantNodes<LyricsBlockSyntax>().Select(b => b.VoiceName)
            .Concat(root.ChildNodes().OfType<PartDeclarationSyntax>().Select(p => p.Name.Text)).ToHashSet(StringComparer.Ordinal);
        string track = person + "Words";
        for (int n = 2; taken.Contains(track); n++)
            track = person + "Words" + n;
        string header = made.Words.Count > 0 ? made.Header + "\nlyrics " + track : made.Header;

        var placed = Placed(source, person, from, char.ToUpperInvariant(person[0]) + person[1..], header, made.Music);
        if (placed.Text is not { } text)
            return placed;

        var written = SyntaxTree.Parse(text);
        var top = written.GetRoot();
        string nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var edits = new List<(int Start, int End, string Text)>();

        // ⑴ The words, where the file writes words: beside the part's block in each section,
        // or — a file written by part — as the track's own block of sections after the part.
        var byPart = new StringBuilder();
        foreach (var (section, words) in made.Words)
        {
            var block = top.ChildNodes().OfType<SectionDeclarationSyntax>().Where(s => s.SectionName == section)
                .SelectMany(s => s.ChildNodes().OfType<PartBlockSyntax>()).FirstOrDefault(b => b.Name == person);
            if (block != null)
                edits.Add((block.Span.End, block.Span.End, nl + IndentOf(text, block.Span.Start) + "lyrics " + track + " {" + words + "}"));
            else
                byPart.Append("  section ").Append(section).Append(" {").Append(words).Append('}').Append(nl);
        }
        if (byPart.Length > 0 && top.ChildNodes().OfType<PartDeclarationSyntax>().FirstOrDefault(p => p.Name.Text == person) is { } part)
            edits.Add((part.Span.End, part.Span.End, nl + "lyrics " + track + " {" + nl + byPart + "}"));

        // ⑵ The scores: one that prints the singer keeps its `singer NAME` (now the written
        // part) and wants no second staff; one that shows the staff takes the words under it.
        foreach (var score in top.ChildNodes().OfType<RenderDeclarationSyntax>())
        {
            var staves = score.DescendantNodes<StaffRenderSyntax>().Where(s => s.PartToken?.Text == person).ToList();
            var staff = staves.FirstOrDefault(s => !s.IsSinger);
            if (staff == null)
                continue;
            if (staves.Any(s => s.IsSinger))
            {
                int from0 = staff.Span.Start;
                while (from0 > 0 && char.IsWhiteSpace(text[from0 - 1]))
                    from0--;
                edits.Add((from0, staff.Span.End, ""));
            }
            else if (made.Words.Count > 0)
                edits.Add((staff.Span.End, staff.Span.End, "  lyrics " + track));
        }

        // ⑶ The singer's lines: one line, singing the written part as it stands (its words are
        // the part's own now); the first line's voice and settings are kept.
        var lines = top.DescendantNodes<SingerDeclarationSyntax>().Where(l => l.PersonToken?.Text == person).ToList();
        var head = lines.FirstOrDefault(l => l.NameToken != null || l.GenderToken != null) ?? lines[0];
        foreach (var line in lines)
        {
            if (ReferenceEquals(line, lines[0]) && head.SingsKeyword is { } sings)
            {
                string settings = head.WithKeyword is { } with ? " " + text[with.Span.Start..head.Span.End] : "";
                edits.Add((line.Span.Start, line.Span.End, text[head.Span.Start..sings.Span.End] + " on " + person + settings));
                continue;
            }
            int start = line.Span.Start, end = line.Span.End;
            if (line.Parent is CompilationUnitSyntax)
            {
                // The whole of its line, the line's end with it.
                start = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
                int next = text.IndexOf('\n', end);
                end = next < 0 ? text.Length : next + 1;
            }
            edits.Add((start, end, ""));
        }

        var result = new StringBuilder(text);
        foreach (var (start, end, replacement) in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.End))
            result.Remove(start, end - start).Insert(start, replacement);
        string final = result.ToString();
        return SyntaxTree.Parse(final).HasErrors
            ? new(null, $"What {person} sings could not be written out as a part.")
            : new(final, null);
    }

    /// <summary>The part of <paramref name="name"/> as <see cref="SingerParts"/> makes it, asked
    /// for by a score added to the text (and, for a part that is nobody's yet, a line that names
    /// who "sings" it): its header's lines, its music and its words section by section. Null,
    /// with <paramref name="error"/>, where it is not made.</summary>
    private static (string Header, List<(string Section, string Music)> Music, List<(string Section, string Words)> Words)? Made(
        string source, string name, string? line, out string? error)
    {
        error = null;
        string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string asked = source + (source.EndsWith('\n') ? "" : nl) + (line != null ? line + nl : "")
            + "score " + AskingScore + " { singer " + name + " }" + nl;
        var tree = SyntaxTree.Parse(asked);
        string text = SingerParts.Expand(asked, tree, out var said);
        if (said.Count > 0 || ReferenceEquals(text, asked))
        {
            error = said.Count > 0 ? "Nothing was written. " + said[0].Message : "Nothing was written: the notes could not be made.";
            return null;
        }
        var root = SyntaxTree.Parse(text).GetRoot();
        int from = SingerParts.MadeFrom(text);
        var part = root.ChildNodes().OfType<PartDeclarationSyntax>().FirstOrDefault(p => p.Name.Text == name && p.Span.Start >= from);
        if (part == null)
        {
            error = "Nothing was written: the notes could not be made.";
            return null;
        }
        string header = string.Join("\n", part.ChildNodes()
            .Where(n => n is not (SyntaxTokenNode or SectionDeclarationSyntax or PartLyricsDeclarationSyntax))
            .Select(n => text[n.Span.Start..n.Span.End]));
        var music = new List<(string, string)>();
        foreach (var section in part.ChildNodes().OfType<SectionDeclarationSyntax>())
            if (Inner(text, section) is { } inner)
                music.Add((section.SectionName, inner));
        var words = new List<(string, string)>();
        string track = LyricSinger.WordsOf(name);
        foreach (var block in root.ChildNodes().OfType<LyricsBlockSyntax>().Where(b => b.VoiceName == track && b.Span.Start >= from))
            foreach (var section in block.ChildNodes().OfType<SectionDeclarationSyntax>())
                if (Inner(text, section) is { } inner)
                    words.Add((section.SectionName, inner));
        return (header, music, words);
    }

    private static string? Inner(string text, SyntaxNode block)
    {
        int open = text.IndexOf('{', block.Span.Start), close = block.Span.End - 1;
        return open >= 0 && open < close && text[close] == '}' ? text[(open + 1)..close] : null;
    }

    /// <summary>The new part placed in <paramref name="source"/> as <see cref="PartWriter"/>
    /// places one: after <paramref name="after"/>, in the file and in the scores.</summary>
    private static Result Placed(string source, string name, string after, string? title, string header, List<(string Section, string Music)> music)
    {
        var placed = PartWriter.Apply(source,
            [.. music.Select(m => new PartWriter.Write(name, m.Section, m.Music))],
            [new PartWriter.NewPart(name, title, null, header, after)]);
        return placed.Text != null ? new(placed.Text, null) : new(null, "Nothing was written. " + string.Join(" ", placed.Problems));
    }

    private static string IndentOf(string text, int at)
    {
        int start = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        int end = start;
        while (end < at && text[end] is ' ' or '\t')
            end++;
        return text[start..end];
    }
}
