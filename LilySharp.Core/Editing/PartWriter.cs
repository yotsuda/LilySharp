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
using System.Text.RegularExpressions;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Writes music into a score BY PLACE — a part's bars in a section, some of them or all, and a
/// new part with its place in the scores — for the arranging chat (owner's proposal 2026-11-11:
/// a tool that replaces a named part's named section, several at once, or a range of bars, and
/// adds a part). A change addressed by text (<c>edits</c>) has to copy what it replaces, and the
/// chat read a whole score again to be sure of the copy; a place needs no copy.
/// </summary>
/// <remarks>
/// <para>
/// THE TEXT ALONE IS MADE HERE: every write is placed, or every problem is said and nothing is
/// changed (all of them at once, so that one more call mends them all). Whether the result
/// compiles, its bars add up and it warns of nothing new is the proposal's question, asked of
/// the text this returns (the extension's <c>writePart</c>).
/// </para>
/// <para>
/// A part's music in a section stands in one of two places, and both are read: a block of the
/// part's name in the section (<c>section A { gtr { … } }</c>), or a section in the part
/// (<c>part gtr { section A { … } }</c>). A part with no music in a section yet gets a block
/// where the score writes its others.
/// </para>
/// <para>
/// BARS are counted in the section as it is written: a run of notes closed by a bar line is one
/// bar, a whole-bar rest with a count (<c>R*4</c>) that many, and a phrase called between bars
/// the bars of its body. A range has to begin and end where one of these does — a phrase of two
/// bars is not cut in half — and a block that holds a repeat or an ending does not tell its bars apart here:
/// both are said, with the bars the block has.
/// </para>
/// </remarks>
public static class PartWriter
{
    /// <summary>Music for a part in a section.</summary>
    /// <param name="Part">The part's name.</param>
    /// <param name="Section">The section's name.</param>
    /// <param name="Music">The music, as it stands between the braces of the part's block.</param>
    /// <param name="Bars">The bars it replaces, counted in the section from 1 (<c>5-8</c>, <c>3</c>); null for all.</param>
    /// <param name="Lyrics">The words of the part's lyrics row in that section, or null to leave them.</param>
    public sealed record Write(string Part, string Section, string Music, string? Bars = null, string? Lyrics = null);

    /// <summary>A part the score does not have yet.</summary>
    /// <param name="Part">Its name.</param>
    /// <param name="Title">The name printed at its staff, or null.</param>
    /// <param name="Short">The short name printed from the second system on, or null.</param>
    /// <param name="Body">The lines of its block, as the grammar writes them (<c>instrument violin</c>), or null.</param>
    /// <param name="After">The part it stands after, in the file and in the scores; null for last.</param>
    public sealed record NewPart(string Part, string? Title = null, string? Short = null, string? Body = null, string? After = null);

    /// <summary>The score with every write placed (<see cref="Text"/>, null when a problem kept
    /// it from being made), what kept it, and what was written where.</summary>
    public sealed record Result(string? Text, IReadOnlyList<string> Problems, IReadOnlyList<string> Done);

    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex BarRange = new(@"^\s*(\d+)\s*(?:-\s*(\d+)\s*)?$", RegexOptions.Compiled);
    private static readonly Regex CountedRest = new(@"^R[^*|]*\*\s*(\d+)", RegexOptions.Compiled);

    /// <summary>Places <paramref name="writes"/> and <paramref name="newParts"/> in <paramref name="source"/>.</summary>
    public static Result Apply(string source, IReadOnlyList<Write> writes, IReadOnlyList<NewPart>? newParts = null)
    {
        newParts ??= [];
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return new Result(null, ["The score as it stands has errors: mend them first (lilysharp_readScore says them)."], []);
        var root = tree.GetRoot();
        string nl = source.Contains("\r\n") ? "\r\n" : "\n";
        var problems = new List<string>();
        var done = new List<string>();
        var edits = new List<(int Start, int Length, string Text, string What)>();

        var parts = root.DescendantNodes<PartDeclarationSyntax>().Where(p => !p.IsUnnamed)
            .GroupBy(p => p.Name.Text).ToDictionary(g => g.Key, g => g.First(), System.StringComparer.Ordinal);
        var topSections = root.ChildNodes().OfType<SectionDeclarationSyntax>()
            .GroupBy(s => s.SectionName).ToDictionary(g => g.Key, g => g.First(), System.StringComparer.Ordinal);
        var sectionNames = root.DescendantNodes<SectionDeclarationSyntax>().Select(s => s.SectionName).Distinct().ToList();
        var played = CompositionChecks.PlayedSections(root);
        if (writes.Count == 0 && newParts.Count == 0)
            return new Result(null, ["Nothing to write: give `write` (a part's music in a section) or `newParts`."], []);

        // ---- the new parts: declared, and placed in the scores
        var added = new Dictionary<string, NewPart>(System.StringComparer.Ordinal);
        foreach (var part in newParts)
        {
            string name = (part.Part ?? "").Trim();
            if (!Identifier.IsMatch(name))
                problems.Add($"A new part needs a name of letters and digits (`part`): '{part.Part}' is none.");
            else if (parts.ContainsKey(name) || added.ContainsKey(name))
                problems.Add($"The score has a part `{name}` already: write its music with `write`, or give the new part another name.");
            else if (part.After is { Length: > 0 } after && !parts.ContainsKey(after.Trim()))
                problems.Add($"`{name}` is to stand after `{after}`, and the score has no such part: its parts are {Names(parts.Keys)}.");
            else
                added[name] = part with { Part = name, After = part.After?.Trim() is { Length: > 0 } a ? a : null };
        }
        // A part written in sections of its own, as the score writes its parts when it has no section at the top.
        bool byPart = topSections.Count == 0;

        // ---- the writes
        var musicOfNew = new Dictionary<string, List<(string Section, string Music)>>(System.StringComparer.Ordinal);
        var seen = new HashSet<(string, string, string?)>();
        foreach (var write in writes)
        {
            string partName = (write.Part ?? "").Trim(), sectionName = (write.Section ?? "").Trim();
            string where = $"{partName}, {sectionName}" + (write.Bars is { Length: > 0 } ? $", bars {write.Bars.Trim()}" : "");
            if (!parts.ContainsKey(partName) && !added.ContainsKey(partName))
            {
                problems.Add($"{where}: the score has no part `{partName}` (its parts are {Names(parts.Keys)}). A new part is declared in `newParts`.");
                continue;
            }
            if (!sectionNames.Contains(sectionName))
            {
                problems.Add($"{where}: the score has no section `{sectionName}` (its sections are {Names(sectionNames)}). This tool writes into sections that are there.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(write.Music))
            {
                problems.Add($"{where}: no `music` to write. For bars of silence write whole-bar rests (`R | R |`).");
                continue;
            }
            if (!seen.Add((partName, sectionName, write.Bars?.Trim())))
            {
                problems.Add($"{where}: written twice in this call.");
                continue;
            }
            string music = write.Music.Trim();
            string times = played.Count(s => s == sectionName) is > 1 and var n ? $" ({sectionName} is played {n} times in the form: each time changes)" : "";

            // Where the part's music in the section stands: its block's braces, or its own section's.
            SyntaxNode? holder = null;
            if (topSections.TryGetValue(sectionName, out var top))
                holder = top.ChildNodes().OfType<PartBlockSyntax>().FirstOrDefault(b => b.Name == partName)
                    ?.ChildNodes().OfType<MusicBlockSyntax>().FirstOrDefault();
            if (holder == null && parts.TryGetValue(partName, out var declared))
                holder = declared.ChildNodes().OfType<SectionDeclarationSyntax>().FirstOrDefault(s => s.SectionName == sectionName);

            if (write.Bars is { Length: > 0 } range)
            {
                if (holder == null)
                {
                    problems.Add($"{where}: `{partName}` has no music in {sectionName} to take bars of. Leave `bars` out to write the section whole.");
                    continue;
                }
                if (BarSpan(root, holder, range, out var span, out string? why))
                {
                    edits.Add((span.Start, span.Length, Inline(music, source, span.Start, nl), where));
                    done.Add($"{where}: replaced{times}.");
                }
                else
                    problems.Add($"{where}: {why}");
            }
            else if (holder != null && Braces(holder) is var (open, close))
            {
                edits.Add((open, close - open, Body(music, IndentOf(source, holder.Span.Start), nl), where));
                done.Add($"{where}: the whole of it replaced{times}.");
            }
            else if (added.ContainsKey(partName) && byPart)
            {
                // Gathered into the new part's own block.
                if (!musicOfNew.TryGetValue(partName, out var own))
                    musicOfNew[partName] = own = [];
                own.Add((sectionName, music));
                done.Add($"{where}: written{times}.");
            }
            else if (topSections.TryGetValue(sectionName, out var section) && Braces(section) is var (_, sectionClose)
                && (!parts.TryGetValue(partName, out var itsPart) || !itsPart.ChildNodes().OfType<SectionDeclarationSyntax>().Any()))
            {
                // A block of its own in the section: after the part it follows there, else last.
                string? after = added.TryGetValue(partName, out var fresh) ? fresh.After : null;
                var blocks = section.ChildNodes().Where(c => c is PartBlockSyntax or LyricsBlockSyntax).ToList();
                int at = blocks.FindLastIndex(b => b is PartBlockSyntax p && p.Name == after);
                // …past the lyrics rows that follow that part.
                while (at >= 0 && at + 1 < blocks.Count && blocks[at + 1] is LyricsBlockSyntax)
                    at++;
                var anchor = at >= 0 ? blocks[at] : blocks.LastOrDefault();
                string indent = anchor != null ? IndentOf(source, anchor.Span.Start) : IndentOf(source, section.Span.Start) + "  ";
                int insertAt = anchor?.Span.End ?? sectionClose;
                string block = nl + indent + partName + " {" + Body(music, indent, nl) + "}";
                edits.Add((insertAt, 0, anchor != null ? block : block + nl + IndentOf(source, section.Span.Start), where));
                done.Add($"{where}: written{times}.");
            }
            else if (parts.TryGetValue(partName, out var owner) && Braces(owner) is var (_, ownerClose))
            {
                // A section of its own in the part, as the part writes its others.
                var last = owner.ChildNodes().OfType<SectionDeclarationSyntax>().LastOrDefault();
                string indent = last != null ? IndentOf(source, last.Span.Start) : IndentOf(source, owner.Span.Start) + "  ";
                edits.Add((last?.Span.End ?? ownerClose, 0, nl + indent + "section " + sectionName + " {" + Body(music, indent, nl) + "}"
                    + (last != null ? "" : nl + IndentOf(source, owner.Span.Start)), where));
                done.Add($"{where}: written{times}.");
            }
            else
            {
                problems.Add($"{where}: no place for it was found in the score. Write it with lilysharp_proposeScore's `edits`.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(write.Lyrics))
                WriteLyrics(source, root, parts, topSections, partName, sectionName, write.Lyrics, where, nl, edits, problems, done);
        }

        // ---- the new parts' text
        foreach (var part in added.Values)
        {
            var after = part.After != null ? parts[part.After] : root.DescendantNodes<PartDeclarationSyntax>().LastOrDefault();
            var text = new StringBuilder();
            text.Append("part ").Append(part.Part);
            if (part.Title is { Length: > 0 } title)
                text.Append(" \"").Append(title.Replace("\"", "'")).Append('"');
            if (part.Title is { Length: > 0 } && part.Short is { Length: > 0 } shortName)
                text.Append(" \"").Append(shortName.Replace("\"", "'")).Append('"');
            text.Append(" {").Append(nl);
            foreach (string line in (part.Body ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
                text.Append("  ").Append(line).Append(nl);
            if (musicOfNew.TryGetValue(part.Part, out var own))
                foreach (var (sectionName, music) in own)
                    text.Append("  section ").Append(sectionName).Append(" {").Append(Body(music, "  ", nl)).Append('}').Append(nl);
            text.Append('}');
            if (after != null)
                edits.Add((after.Span.End, 0, nl + text, part.Part));
            else
            {
                // No part yet: before the first section, or at the end.
                int at = root.ChildNodes().OfType<SectionDeclarationSyntax>().FirstOrDefault()?.Span.Start ?? source.Length;
                edits.Add((at, 0, at == source.Length ? nl + text + nl : text + nl + nl, part.Part));
            }

            // In every score that shows the part it follows (every score, with none named): a staff after it.
            int placed = 0;
            foreach (var score in root.DescendantNodes<RenderDeclarationSyntax>())
            {
                var rows = score.ChildNodes().Where(c => c is not SyntaxTokenNode).ToList();
                static bool Shows(SyntaxNode row, string name) => row is not LyricsRowRenderSyntax and not ChordRowRenderSyntax && RowNames(row).Contains(name);
                int at = part.After != null ? rows.FindLastIndex(r => Shows(r, part.After))
                    : rows.FindLastIndex(r => r is StaffRenderSyntax or TabRenderSyntax or GrandStaffRenderSyntax or CondensedStaffRenderSyntax or CombinedStaffRenderSyntax);
                if (at < 0)
                    continue;
                while (at + 1 < rows.Count && rows[at + 1] is LyricsRowRenderSyntax)
                    at++;
                var anchor = rows[at];
                bool ownLine = source.LastIndexOf('\n', System.Math.Max(0, anchor.Span.Start - 1)) + 1 + IndentOf(source, anchor.Span.Start).Length == anchor.Span.Start;
                edits.Add((anchor.Span.End, 0, ownLine ? nl + IndentOf(source, anchor.Span.Start) + "staff " + part.Part : "  staff " + part.Part, part.Part));
                placed++;
            }
            done.Add($"{part.Part}: a new part" + (part.After != null ? $", after {part.After}" : "")
                + (placed > 0 ? $", with a staff in {placed} score{(placed == 1 ? "" : "s")}." : ", in no score yet: place it with `staff " + part.Part + "` in a score."));
            if (!writes.Any(w => w.Part?.Trim() == part.Part))
                problems.Add($"{part.Part}: a new part with no music. Give its music in `write`, a section at a time (a section left out is silent).");
        }

        if (problems.Count > 0)
            return new Result(null, problems, []);
        // Two writes into one place cannot both stand.
        var ordered = edits.OrderBy(e => e.Start).ThenBy(e => e.Length).ToList();
        for (int i = 1; i < ordered.Count; i++)
            if (ordered[i].Start < ordered[i - 1].Start + ordered[i - 1].Length)
                return new Result(null, [$"{ordered[i - 1].What} and {ordered[i].What} write over each other: give one of them, or bars that do not meet."], []);
        var result = new StringBuilder(source);
        foreach (var (start, length, text, _) in ordered.AsEnumerable().Reverse())
            result.Remove(start, length).Insert(start, text);
        return new Result(result.ToString(), [], done);
    }

    private static IEnumerable<string> RowNames(SyntaxNode row)
        => Within(row).OfType<SyntaxTokenNode>().Where(t => t.Kind == SyntaxKind.Identifier).Select(t => t.Text);

    // Every node and token under a node.
    private static IEnumerable<SyntaxNode> Within(SyntaxNode node)
    {
        for (int i = 0; i < node.SlotCount; i++)
            if (node.GetChild(i) is { } child)
            {
                yield return child;
                foreach (var inner in Within(child))
                    yield return inner;
            }
    }

    private static string Names(IEnumerable<string> names) => string.Join(", ", names);

    // The words of the part's lyrics row in a section, replaced or written after its block.
    private static void WriteLyrics(string source, SyntaxNode root, Dictionary<string, PartDeclarationSyntax> parts,
        Dictionary<string, SectionDeclarationSyntax> topSections, string partName, string sectionName, string lyrics, string where,
        string nl, List<(int, int, string, string)> edits, List<string> problems, List<string> done)
    {
        string? track = parts.TryGetValue(partName, out var part)
            ? part.ChildNodes().FirstOrDefault(c => c.Kind == SyntaxKind.PartLyricsDeclaration)?.ChildNodes().OfType<SyntaxTokenNode>()
                .FirstOrDefault(t => t.Kind == SyntaxKind.Identifier)?.Text
            : null;
        if (track == null)
        {
            problems.Add($"{where}: `lyrics` is given, and `{partName}` names no lyrics row (`lyrics NAME` in its part block). Write the words with lilysharp_proposeScore's `edits`.");
            return;
        }
        if (!topSections.TryGetValue(sectionName, out var section))
        {
            problems.Add($"{where}: `lyrics` is given, and {sectionName} is not written at the top of the file. Write the words with lilysharp_proposeScore's `edits`.");
            return;
        }
        var row = section.ChildNodes().OfType<LyricsBlockSyntax>().FirstOrDefault(l => l.ChildNodes().OfType<SyntaxTokenNode>()
            .FirstOrDefault(t => t.Kind == SyntaxKind.Identifier)?.Text == track);
        string words = lyrics.Trim();
        if (row != null && Braces(row) is var (open, close))
            edits.Add((open, close - open, Body(words, IndentOf(source, row.Span.Start), nl), where + " (lyrics)"));
        else
        {
            var block = section.ChildNodes().OfType<PartBlockSyntax>().FirstOrDefault(b => b.Name == partName);
            if (block == null)
            {
                // The part's block is written in this same call: its words go with it there.
                problems.Add($"{where}: `lyrics` for a part with no music in {sectionName} yet. Write the part's music first, then its words.");
                return;
            }
            string indent = IndentOf(source, block.Span.Start);
            edits.Add((block.Span.End, 0, nl + indent + "lyrics " + track + " {" + Body(words, indent, nl) + "}", where + " (lyrics)"));
        }
        done.Add($"{where}: its words (`lyrics {track}`) written.");
    }

    /// <summary>The offsets just inside a node's braces, its own or its music block's; null when it has none.</summary>
    private static (int Open, int Close)? Braces(SyntaxNode node)
    {
        SyntaxTokenNode? open = null, close = null;
        for (int i = 0; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is not SyntaxTokenNode t)
                continue;
            if (t.Kind == SyntaxKind.OpenBrace && open == null)
                open = t;
            else if (t.Kind == SyntaxKind.CloseBrace)
                close = t;
        }
        return open != null && close != null ? (open.Span.End, close.Span.Start) : null;
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

    // What stands between a block's braces: on its line when it is one line, else a line each, indented.
    private static string Body(string music, string indent, string nl)
    {
        var lines = music.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count <= 1)
            return " " + (lines.Count == 1 ? lines[0] : "") + " ";
        return nl + string.Concat(lines.Select(l => indent + "  " + l + nl)) + indent;
    }

    // Music set in a line: its own lines kept, each after the first at the line's indent.
    private static string Inline(string music, string source, int at, string nl)
    {
        var lines = music.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return string.Join(nl + IndentOf(source, at), lines);
    }

    /// <summary>One stretch of a block that is whole bars: a run closed by a bar line, a counted rest's, a phrase's.</summary>
    private readonly record struct Unit(int Bars, int Start, int End, string? Phrase);

    /// <summary>The units of a block's music, or null where its bars cannot be told apart
    /// (<paramref name="why"/> says what stands in the way).</summary>
    private static List<Unit>? UnitsOf(SyntaxNode root, SyntaxNode holder, out string? why, int depth = 0)
    {
        why = null;
        var units = new List<Unit>();
        int runStart = -1, runBars = 1;
        var phrases = root.DescendantNodes<PhraseDeclarationSyntax>().GroupBy(p => p.Name.Text).ToDictionary(g => g.Key, g => g.First());
        for (int i = 0; i < holder.SlotCount; i++)
        {
            var child = holder.GetChild(i);
            if (child == null || child is SyntaxTokenNode { Kind: SyntaxKind.OpenBrace or SyntaxKind.CloseBrace or SyntaxKind.SectionKeyword or SyntaxKind.Identifier or SyntaxKind.StringLiteral })
                continue;
            if (child is BarlineSyntax)
            {
                // A repeat's bars are played more than once: which of them a number means is not told here.
                if (Within(child).OfType<SyntaxTokenNode>().Any(t => t.Kind is SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar or SyntaxKind.RepeatBothBar))
                {
                    why = "a repeat (`|:` … `:|`)";
                    return null;
                }
                if (runStart < 0)
                {
                    why = "a bar line with nothing before it";
                    return null;
                }
                units.Add(new Unit(runBars, runStart, child.Span.End, null));
                (runStart, runBars) = (-1, 1);
                continue;
            }
            if (child is VariableReferenceSyntax call && runStart < 0 && phrases.TryGetValue(call.Name.Text, out var phrase) && depth < 8)
            {
                var inner = UnitsOf(root, phrase.Body, out string? innerWhy, depth + 1);
                if (inner == null)
                {
                    why = $"the phrase `{call.Name.Text}` ({innerWhy})";
                    return null;
                }
                int bars = inner.Sum(u => u.Bars);
                if (bars > 0)
                {
                    units.Add(new Unit(bars, call.Span.Start, call.Span.End, call.Name.Text));
                    continue;
                }
            }
            if (Within(child).Any(n => n is BarlineSyntax))
            {
                why = "an ending, a `%` repeat or a group with bar lines inside it";
                return null;
            }
            if (runStart < 0)
                runStart = child.Span.Start;
            if (child is RestSyntax && CountedRest.Match(child.ToFullString().Trim()) is { Success: true } counted && int.TryParse(counted.Groups[1].Value, out int count) && count > 1)
                runBars = count;
        }
        // A last bar left open is a bar too.
        if (runStart >= 0)
        {
            int end = Braces(holder)?.Close ?? holder.Span.End;
            units.Add(new Unit(runBars, runStart, end, null));
        }
        return units;
    }

    /// <summary>The source span of bars <paramref name="range"/> of a block, where they begin and end with its units.</summary>
    private static bool BarSpan(SyntaxNode root, SyntaxNode holder, string range, out TextSpan span, out string? why)
    {
        span = default;
        var m = BarRange.Match(range);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out int first) || first < 1)
        {
            why = $"`bars` is a bar or a range of them, counted in the section from 1 (`3`, `5-8`): '{range}' is neither.";
            return false;
        }
        int last = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out int z) ? z : first;
        if (last < first)
        {
            why = $"bars {first}-{last} end before they begin.";
            return false;
        }
        var units = UnitsOf(root, holder, out string? opaque);
        if (units == null)
        {
            why = $"its bars cannot be told apart here — it holds {opaque}. Leave `bars` out and write the section whole.";
            return false;
        }
        int total = units.Sum(u => u.Bars);
        if (last > total)
        {
            why = $"it has {total} bar{(total == 1 ? "" : "s")} there.";
            return false;
        }
        int start = -1, end = -1, bar = 1;
        foreach (var unit in units)
        {
            int unitLast = bar + unit.Bars - 1;
            if (unitLast >= first && bar <= last)
            {
                if (bar < first || unitLast > last)
                {
                    why = unit.Phrase != null
                        ? $"they cut through the phrase `{unit.Phrase}`, which is bars {bar}-{unitLast} there. Take those bars whole, or write the section whole."
                        : $"they cut through a rest of {unit.Bars} bars (bars {bar}-{unitLast}). Take those bars whole, or write the section whole.";
                    return false;
                }
                if (start < 0)
                    start = unit.Start;
                end = unit.End;
            }
            bar += unit.Bars;
        }
        span = new TextSpan(start, end - start);
        why = null;
        return true;
    }
}
