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
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// "Join Sections": two or more sections declared one after another become ONE section — the
/// first's name, each part's, chord row's and lyrics track's music run on bar after bar — and every
/// form that played them in a row plays the one (owner's request 2026-11-17: a chart a
/// transcription cut into many short sections, made flat).
/// </summary>
/// <remarks>
/// <para>
/// The inverse of <see cref="SectionSplitter"/>, with the same care: a section boundary RESETS what
/// a section starts from (MeasureCollector.ProcessSectionPrologue), and joined, the later sections
/// would carry on from the earlier one instead. So at each join the reset is written out: the
/// meter and key the later section opened in (its header's, else the score's) where they differ
/// from what runs on; a first note with no length gets the 4 a section opens with; and the relative
/// frame — the later section's first pitch is re-marked to sound where it did (from the MIDI
/// before and after, note by note). A part a middle section does not write is given that
/// section's bars of rest (<c>R*N |</c>).
/// </para>
/// <para>
/// Refused, with the reason, rather than guessed: a section written by part, a later section's
/// header with anything but <c>time</c> / <c>key</c> (a <c>partial</c>, a <c>tempo</c>), a cell
/// ending inside a bar, an inline clef / meter / key change before a join, a chord row or words
/// missing from a section before one that has them, a form that plays a later section other than
/// right after the first, or another reference to a later section. And the rewrite is checked
/// before it is offered: it parses, no error is new, and under every form every part sounds as it
/// did (pitch, time, length) and the words fall on the same notes.
/// </para>
/// </remarks>
public static class SectionJoiner
{
    /// <summary>The rewritten text and a one-line account, or the reason nothing was done.</summary>
    public sealed record Result(string? NewText, string? Plan, string? Error);

    private static Result Fail(string error) => new(null, null, error);

    /// <summary>The top-level sections that may be joined with the one declared after them: the
    /// names, in declaration order (the menu offers each run that starts at the caret's).</summary>
    public static IReadOnlyList<string> TopLevelSections(SyntaxTree tree)
        => [.. tree.GetRoot().ChildNodes().OfType<SectionDeclarationSyntax>().Select(s => s.SectionName)];

    /// <summary>The sections a caret or selection [<paramref name="start"/>, <paramref name="end"/>]
    /// picks for joining: every top-level section it touches — and, touching one, that one and the
    /// next. Null when it touches none, or touches one with none after it.</summary>
    public static IReadOnlyList<string>? SectionsAt(SyntaxTree tree, int start, int end)
    {
        var all = tree.GetRoot().ChildNodes().OfType<SectionDeclarationSyntax>().ToList();
        var touched = all.Select((s, i) => (s, i)).Where(x => InkStart(x.s) <= end && start <= InkEnd(x.s)).Select(x => x.i).ToList();
        if (touched.Count == 0)
            return null;
        if (touched.Count == 1)
            return touched[0] + 1 < all.Count ? [all[touched[0]].SectionName, all[touched[0] + 1].SectionName] : null;
        return [.. all.Skip(touched[0]).Take(touched[^1] - touched[0] + 1).Select(s => s.SectionName)];
    }

    /// <summary>The menu's words for joining <paramref name="names"/>.</summary>
    public static string Title(IReadOnlyList<string> names)
        => names.Count == 2 ? $"Join section {names[0]} with {names[1]}" : $"Join sections {names[0]}–{names[^1]} into {names[0]}";

    /// <summary>Joins the sections <paramref name="names"/> (two or more, declared one after
    /// another in this order) into the first.</summary>
    public static Result Join(string source, IReadOnlyList<string> names)
    {
        if (names.Count < 2)
            return Fail("Join needs two or more sections.");
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors — no changes made.");
        var root = tree.GetRoot();
        var all = root.ChildNodes().OfType<SectionDeclarationSyntax>().ToList();
        int first = all.FindIndex(s => s.SectionName == names[0]);
        if (first < 0)
            return Fail($"There is no section {names[0]} written as a section of the score.");
        if (first + names.Count > all.Count || Enumerable.Range(0, names.Count).Any(i => all[first + i].SectionName != names[i]))
            return Fail($"{string.Join(", ", names)} are not declared one after another.");
        var secs = all.Skip(first).Take(names.Count).ToList();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            return Fail("A section is named twice.");

        // Written by part (part X { section A { … } }, chords h { section A { … } }): not this rewrite's.
        foreach (var n in root.DescendantNodes<SectionDeclarationSyntax>())
            if (n.Parent is not CompilationUnitSyntax && names.Contains(n.SectionName))
                return Fail($"Section {n.SectionName} is written inside a part or a track — join works on sections written as `section {n.SectionName} {{ part {{ … }} }}`.");

        // ---- each section: its header and its cells ------------------------------------
        var model = new List<(SectionDeclarationSyntax Node, List<SyntaxNode> Header, List<(string Key, string Label, SyntaxNode Cell)> Cells)>();
        foreach (var s in secs)
        {
            var header = new List<SyntaxNode>();
            var cells = new List<(string, string, SyntaxNode)>();
            foreach (var item in BodyItems(s))
            {
                switch (item)
                {
                    case SyntaxTokenNode:
                        continue;
                    case PartBlockSyntax p:
                        cells.Add(("part " + p.Name, p.Name, p)); break;
                    case ChordPartBlockSyntax { PartName: { } row } c:
                        cells.Add(("chords " + row, "chords " + row, c)); break;
                    case LyricsBlockSyntax l:
                        string label = Regex.Replace(source[InkStart(l)..OpenBraceStart(l)].Trim(), @"\s+", " ");
                        cells.Add(("lyrics " + label, label, l)); break;
                    case TimeSignatureSyntax or KeySignatureSyntax:
                        header.Add(item); break;
                    default:
                        if (s != secs[0])
                            return Fail($"Section {s.SectionName} writes `{Short(source, item)}` in its header — only `time` and `key` can be carried into a joined section. Move it into the music first.");
                        header.Add(item); break;
                }
            }
            model.Add((s, header, cells));
        }

        // ---- the forms: each play of the first is a play of them all, in a row ----------
        var formEdits = new List<Edit>();
        int formRefs = 0;
        var laterNames = names.Skip(1).ToHashSet(StringComparer.Ordinal);
        foreach (var form in root.DescendantNodes<FormDeclarationSyntax>())
        {
            foreach (var parent in new SyntaxNode[] { form }.Concat(form.DescendantNodes()).Where(n => n.ChildNodes().Any(IsReference)).ToList())
            {
                var kids = Enumerable.Range(0, parent.SlotCount).Select(parent.GetChild)
                    .Where(c => c != null && c.Width > 0).Select(c => c!).ToList();
                for (int i = 0; i < kids.Count; i++)
                {
                    if (!IsReference(kids[i]))
                        continue;
                    string name = RefName(kids[i]);
                    if (laterNames.Contains(name))
                    {
                        bool follows = i > 0 && Enumerable.Range(1, names.Count - 1).Any(k => i - k >= 0 && k < names.Count
                            && IsReference(kids[i - k]) && RefName(kids[i - k]) == names[0]
                            && Enumerable.Range(1, k).All(j => IsReference(kids[i - k + j]) && RefName(kids[i - k + j]) == names[j]));
                        if (!follows)
                            return Fail($"A form plays {name} without {string.Join(" ", names.TakeWhile(x => x != name))} right before it ({Where(source, kids[i])}) — the sections can be joined only where they always play in a row.");
                        continue;
                    }
                    if (name != names[0])
                        continue;
                    for (int k = 1; k < names.Count; k++)
                    {
                        if (i + k >= kids.Count || !IsReference(kids[i + k]) || RefName(kids[i + k]) != names[k])
                            return Fail($"A form plays {names[0]} without {names[k]} after it ({Where(source, kids[i])}) — the sections can be joined only where they always play in a row.");
                        if (!PlainReference(source, kids[i + k]))
                            return Fail($"A form plays {names[k]} as `{Short(source, kids[i + k])}` — a label or octave marks on it would be lost. Write it plain first.");
                        formEdits.Add(new Edit(InkEnd(kids[i + k - 1]), InkEnd(kids[i + k]), ""));
                        formRefs++;
                    }
                }
            }
        }
        // Any other mention of a later section (a score's, a break's): not ours to rewrite.
        foreach (var later in laterNames)
        {
            var refs = SectionReferenceFinder.Occurrences(root, later);
            int expected = 1 + formEdits.Count(e => source[e.Start..e.End].Contains(later, StringComparison.Ordinal));
            if (refs.Count > expected)
                return Fail($"Section {later} is named elsewhere too ({Where(source, refs.First(r => r.Parent is not SectionDeclarationSyntax && !formEdits.Any(e => r.Span.Start >= e.Start && r.Span.Start < e.End)))}) — join works on sections only the forms play.");
        }

        // ---- what runs on at each join, and what the later section opened with ------------
        string? scoreTime = root.ChildNodes().OfType<TimeSignatureSyntax>().Select(t => Short(source, t)).LastOrDefault();
        string? scoreKey = root.ChildNodes().OfType<KeySignatureSyntax>().Select(k => Short(source, k)).LastOrDefault();
        string? HeaderOf<T>(int i) where T : SyntaxNode => model[i].Header.OfType<T>().Select(h => Short(source, h)).LastOrDefault();
        var canonical = Svg.Collector.SectionBarCounts.BuildSemanticIndex(root).Canonical;

        // Inline meter / key / clef inside an earlier section's music: what runs on would be its.
        for (int i = 0; i < model.Count - 1; i++)
            foreach (var (_, label, cell) in model[i].Cells)
                if (cell.DescendantNodes().FirstOrDefault(n => n is TimeSignatureSyntax or KeySignatureSyntax or ClefDeclarationSyntax) is { } inline)
                    return Fail($"{label} in section {model[i].Node.SectionName} changes `{Short(source, inline)}` inside its music — what runs on into {model[i + 1].Node.SectionName} would be that. Move it into the section's header, or join from there.");

        // ---- the joined cells -----------------------------------------------------------
        var keys = new List<(string Key, string Label, SyntaxNode Head)>();
        foreach (var (_, _, cells) in model)
            foreach (var (key, label, cell) in cells)
                if (!keys.Any(k => k.Key == key))
                    keys.Add((key, label, cell));
        var joined = new List<(string Head, List<string> Pieces, string Label, bool IsMusic)>();
        // (the first pitch of each later piece of music, to re-mark: its cell's key, the section, its offset in the source)
        var openers = new List<(string Key, int PieceIndex, int SourcePos, int OffsetInPiece)>();
        foreach (var (key, label, head) in keys)
        {
            bool music = head is PartBlockSyntax;
            var pieces = new List<string>();
            int lastWith = model.FindLastIndex(m => m.Cells.Any(c => c.Key == key));
            for (int i = 0; i <= lastWith; i++)
            {
                var cell = model[i].Cells.FirstOrDefault(c => c.Key == key).Cell;
                string prefix = "";
                if (i > 0 && music)
                {
                    // The meter and key the section opened in, where they are not what runs on.
                    string? openedTime = HeaderOf<TimeSignatureSyntax>(i) ?? scoreTime;
                    string? runTime = Enumerable.Range(0, i).Reverse().Select(HeaderOf<TimeSignatureSyntax>).FirstOrDefault(t => t != null) ?? scoreTime;
                    if (openedTime != runTime)
                        prefix += (openedTime ?? "time 4/4") + " ";
                    string? openedKey = HeaderOf<KeySignatureSyntax>(i) ?? scoreKey;
                    string? runKey = Enumerable.Range(0, i).Reverse().Select(HeaderOf<KeySignatureSyntax>).FirstOrDefault(k => k != null) ?? scoreKey;
                    if (openedKey != runKey)
                        prefix += (openedKey ?? "key c major") + " ";
                }
                if (cell == null)
                {
                    if (head is not PartBlockSyntax)
                        return Fail($"{label} is not written in section {model[i].Node.SectionName}, but it is in a later one — the words or chords after it would move. Write that section's bars for it first.");
                    int bars = canonical.GetValueOrDefault(model[i].Node.SectionName);
                    if (bars <= 0)
                        return Fail($"Section {model[i].Node.SectionName}'s length could not be read.");
                    pieces.Add(prefix + $"R*{bars} |");
                    continue;
                }
                var (from, to) = BodyRange(cell);
                string text = source[from..to].Trim();
                int contentStart = from + (source[from..to].Length - source[from..to].TrimStart().Length);
                if (i < lastWith && head is not LyricsBlockSyntax && !EndsOnABarLine(text))
                    return Fail($"{label} in section {model[i].Node.SectionName} ends inside a bar (no `|` after its last note) — close the bar first.");
                if (i > 0 && music)
                {
                    // A first note with no length took the 4 a section opens with.
                    if (FirstTimedLeaf(cell) is { } leaf && LengthGap(leaf) is int gap && gap > contentStart)
                        text = source[contentStart..gap] + "4" + source[gap..to].TrimEnd();
                    // (the pitch stands before any length written for it: the "4" leaves its offset alone)
                    if (cell.DescendantNodes<PitchSyntax>().FirstOrDefault() is { } pitch && pitch.Span.Start >= contentStart)
                        openers.Add((key, pieces.Count, pitch.Span.Start, prefix.Length + pitch.Span.Start - contentStart));
                }
                pieces.Add(prefix + text);
            }
            joined.Add((source[InkStart(head)..OpenBraceEnd(head)], pieces, label, music));
        }

        // ---- the text ----------------------------------------------------------------------
        var a = secs[0];
        var firstCell = model[0].Cells.Count > 0 ? model[0].Cells[0].Cell : null;
        string indent = firstCell != null ? IndentOf(source, InkStart(firstCell)) : "  ";
        var sb = new StringBuilder();
        // The first section's head and header, as written, up to its first cell.
        int headEnd = firstCell != null ? InkStart(firstCell) : BodyRange(a).End;
        sb.Append(source, InkStart(a), headEnd - InkStart(a));
        var pieceStarts = new Dictionary<(string Key, int Piece), int>();   // offset of each piece in sb
        // A section written on one line stays on one line where its joined cells are short.
        bool sectionOnOneLine = !source[InkStart(a)..InkEnd(a)].Contains('\n')
            && joined.All(j => j.Pieces.All(p => !p.Contains('\n') && !p.Contains("//", StringComparison.Ordinal)))
            && joined.Sum(j => j.Head.Length + j.Pieces.Sum(p => p.Length + 1) + 2) < 140;
        for (int c = 0; c < joined.Count; c++)
        {
            var (head, pieces, _, _) = joined[c];
            if (c > 0)
                sb.Append(sectionOnOneLine ? " " : "\n" + indent);
            bool oneLine = pieces.All(p => !p.Contains('\n') && !p.Contains("//", StringComparison.Ordinal)) && pieces.Sum(p => p.Length) < 100;
            sb.Append(head);
            for (int p = 0; p < pieces.Count; p++)
            {
                sb.Append(oneLine ? " " : "\n" + indent + "  ");
                pieceStarts[(keys[c].Key, p)] = sb.Length;
                sb.Append(pieces[p]);
            }
            sb.Append(oneLine ? " }" : "\n" + indent + "}");
        }
        sb.Append(sectionOnOneLine ? " }" : "\n" + IndentOf(source, InkStart(a)) + "}");
        string section = sb.ToString();

        var edits = new List<Edit> { new(InkStart(a), InkEnd(a), section) };
        for (int i = 1; i < secs.Count; i++)
        {
            // With the blank lines before it: what stood between the sections goes with them.
            int start = InkStart(secs[i]);
            while (start > 0 && source[start - 1] is ' ' or '\t' or '\r' or '\n') start--;
            edits.Add(new Edit(start, InkEnd(secs[i]), ""));
        }
        edits.AddRange(formEdits);
        string candidate = Apply(source, edits);

        // ---- the relative frame: each later piece's first pitch, where it sounded ---------------
        int sectionAt = InkStart(a);
        var pinned = new Dictionary<int, int>();   // candidate offset of an opener → its source offset
        // (the joined section stands where the first did, and nothing before it moved)
        foreach (var (key, piece, pos, offsetInPiece) in openers)
            pinned[sectionAt + pieceStarts[(key, piece)] + offsetInPiece] = pos;
        candidate = Repin(source, candidate, pinned);

        // ---- the check ------------------------------------------------------------------------
        if (Verify(tree, candidate) is { } problem)
            return Fail($"Joining {string.Join(", ", names)} would change the piece, so nothing was done:\n{problem}");
        return new Result(candidate,
            $"Joined {string.Join(" + ", names)} into {names[0]}" + (formRefs > 0 ? $"; the forms play {names[0]} where they played them in a row." : "."),
            null);
    }

    // --- the relative frame -----------------------------------------------------------------

    /// <summary>Each pinned pitch (candidate offset → source offset) re-marked by the octaves its
    /// note now sounds away from where it sounded — the rest of its run follows it in the frame.</summary>
    internal static string Repin(string source, string candidate, Dictionary<int, int> pinned)
    {
        if (pinned.Count == 0)
            return candidate;
        var before = PitchBySource(SyntaxTree.Parse(source));
        // One join at a time, first to last: re-marking one moves the frame of all that follows it,
        // the next join's first pitch included (MEASURED: My Life's 14 sections at once, fixed in one
        // round, left a later join an octave off).
        for (int round = 0; round <= pinned.Count; round++)
        {
            var tree = SyntaxTree.Parse(candidate);
            var now = PitchBySource(tree);
            var edits = new List<Edit>();
            foreach (var (at, was) in pinned.OrderBy(p => p.Key))
            {
                if (edits.Count > 0)
                    break;
                if (!before.TryGetValue(was, out int pitchWas) || !now.TryGetValue(at, out int pitchNow) || pitchWas == pitchNow)
                    continue;
                int octaves = (pitchWas - pitchNow) / 12;
                if (octaves == 0 || (pitchWas - pitchNow) % 12 != 0)
                    continue;
                if (tree.GetRoot().DescendantNodes<PitchSyntax>().FirstOrDefault(p => p.Span.Start == at) is not { } pitch)
                    continue;
                string text = candidate.Substring(pitch.Span.Start, pitch.Span.Length);
                var m = Regex.Match(text, @"^([a-g][a-z]*)([',]*)(.*)$", RegexOptions.Singleline);
                if (!m.Success)
                    continue;
                int marks = m.Groups[2].Value.Count(ch => ch == '\'') - m.Groups[2].Value.Count(ch => ch == ',') + octaves;
                string newMarks = marks > 0 ? new string('\'', marks) : new string(',', -marks);
                edits.Add(new Edit(pitch.Span.Start, pitch.Span.Start + pitch.Span.Length, m.Groups[1].Value + newMarks + m.Groups[3].Value));
            }
            if (edits.Count == 0)
                break;
            // Re-marking changes lengths after it: carry the pins along.
            var moved = new Dictionary<int, int>();
            foreach (var (at, was) in pinned)
                moved[at + edits.Where(e => e.Start < at).Sum(e => e.Text.Length - (e.End - e.Start))] = was;
            candidate = Apply(candidate, edits);
            pinned = moved;
        }
        return candidate;
    }

    /// <summary>The first sounding pitch of each written note, by its source offset (every form's plays agree on a
    /// note's first pitch under the default form).</summary>
    internal static Dictionary<int, int> PitchBySource(SyntaxTree tree)
    {
        var result = new Dictionary<int, int>();
        foreach (var n in new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick))
        {
            int at = n.MemberPos >= 0 ? n.MemberPos : n.SourcePos;
            if (at >= 0)
                result.TryAdd(at, n.Pitch);
        }
        return result;
    }

    // --- the check ------------------------------------------------------------------------------

    internal static string? Verify(SyntaxTree original, string candidate)
    {
        var after = SyntaxTree.Parse(candidate);
        if (after.HasErrors)
            return "• it would not parse.";
        var problems = new List<string>();
        var oldErrors = Diagnostics(original).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code + d.Message).ToList();
        foreach (var d in Diagnostics(after).Where(d => d.Severity == DiagnosticSeverity.Error))
            if (!oldErrors.Remove(d.Code + d.Message))
                problems.Add($"• a new error: {d.Code} {d.Message}");
        var formsBefore = original.GetRoot().DescendantNodes<FormDeclarationSyntax>().Where(f => !f.IsReference).ToList();
        var formsAfter = after.GetRoot().DescendantNodes<FormDeclarationSyntax>().Where(f => !f.IsReference).ToList();
        if (formsBefore.Count != formsAfter.Count)
            return "• the forms changed in number.";
        for (int f = 0; f < Math.Max(1, formsBefore.Count); f++)
        {
            var fb = formsBefore.Count > 0 ? formsBefore[f] : null;
            var fa = formsAfter.Count > 0 ? formsAfter[f] : null;
            string label = fb == null || fb.NameText.Length == 0 ? "" : $" (form {fb.NameText})";
            var sb = Sound(original, fb);
            var sa = Sound(after, fa);
            foreach (var part in sb.Keys.Union(sa.Keys).OrderBy(p => p, StringComparer.Ordinal))
            {
                var x = sb.GetValueOrDefault(part) ?? [];
                var y = sa.GetValueOrDefault(part) ?? [];
                int n = Math.Min(x.Count, y.Count), k = 0;
                while (k < n && x[k] == y[k]) k++;
                if (k < n)
                    problems.Add($"• {part}{label}: the note at tick {x[k].Start} (key {x[k].Key}, {x[k].Length} ticks) would become tick {y[k].Start} (key {y[k].Key}, {y[k].Length} ticks).");
                else if (x.Count != y.Count)
                    problems.Add($"• {part}{label}: {x.Count} notes would become {y.Count}.");
            }
            var lb = Lyrics(original, fb);
            var la = Lyrics(after, fa);
            foreach (var track in lb.Keys.Union(la.Keys))
                if (!(lb.GetValueOrDefault(track) ?? "").Equals(la.GetValueOrDefault(track) ?? "", StringComparison.Ordinal))
                    problems.Add($"• the words of {track}{label} would fall on other notes.");
        }
        return problems.Count == 0 ? null : string.Join("\n", problems.Take(6)) + (problems.Count > 6 ? $"\n• … and {problems.Count - 6} more" : "");
    }

    private static List<Diagnostic> Diagnostics(SyntaxTree tree)
    {
        var list = tree.Diagnostics.ToList();
        try { list.AddRange(SemanticValidation.Run(tree)); }
        catch { /* a validator that throws is compared as silent on both sides */ }
        return list;
    }

    private static Dictionary<string, List<(int Start, int Key, int Length, int Bend)>> Sound(SyntaxTree tree, FormDeclarationSyntax? form)
    {
        var result = new Dictionary<string, List<(int, int, int, int)>>(StringComparer.Ordinal);
        foreach (var n in new MidiExporter { Form = form }.Export(tree).Tracks.SelectMany(t => t.Notes))
        {
            string part = n.Part ?? "(no part)";
            if (!result.TryGetValue(part, out var list))
                result[part] = list = [];
            list.Add((n.StartTick, n.Pitch, n.DurationTicks, n.QuarterBend));
        }
        foreach (var list in result.Values)
            list.Sort();
        return result;
    }

    private static Dictionary<string, string> Lyrics(SyntaxTree tree, FormDeclarationSyntax? form)
        => new MidiExporter { Form = form }.Export(tree).Tracks.Where(t => t.Lyrics.Count > 0)
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => string.Join("|", g.SelectMany(t => t.Lyrics).OrderBy(l => l.Tick).Select(l => $"{l.Tick}:{l.Text}")),
                StringComparer.Ordinal);

    // --- helpers --------------------------------------------------------------------------------

    internal static bool IsReference(SyntaxNode n) => n.Kind is SyntaxKind.SectionReference or SyntaxKind.SilentSectionReference;

    internal static string RefName(SyntaxNode n)
        => n is SectionReferenceSyntax r ? r.SectionName
            : n.DescendantNodes().OfType<SyntaxTokenNode>().FirstOrDefault(t => t.Kind == SyntaxKind.Identifier)?.Text ?? "";

    /// <summary>A reference with nothing but its name (and a <c>~</c>): no label, no octave marks.</summary>
    internal static bool PlainReference(string source, SyntaxNode n)
        => Regex.IsMatch(Short(source, n), @"^~?[A-Za-z_][A-Za-z0-9_\-]*$");

    internal static string Short(string source, SyntaxNode n)
    {
        string t = Regex.Replace(source[InkStart(n)..InkEnd(n)], @"\s+", " ");
        return t.Length > 60 ? t[..57] + "…" : t;
    }

    internal static string Where(string source, SyntaxNode n) => $"line {source[..InkStart(n)].Count(c => c == '\n') + 1}";

    private static bool EndsOnABarLine(string text)
    {
        string t = Regex.Replace(text, @"//[^\n]*", "");
        t = Regex.Replace(t, @"\b(break|pageBreak|noBreak)\b", "").TrimEnd();
        return t.EndsWith('|');
    }

    /// <summary>The first note, rest or chord of a cell's music (a bare multi-bar <c>R</c> moves no running length and is not one).</summary>
    internal static SyntaxNode? FirstTimedLeaf(SyntaxNode cell)
        => cell.DescendantNodes().FirstOrDefault(n => n is NoteSyntax or ChordSyntax || (n is RestSyntax r && !r.ToString().TrimStart().StartsWith('R')));

    /// <summary>Where a length would be written on a leaf that has none, or null when it has one.</summary>
    internal static int? LengthGap(SyntaxNode leaf)
    {
        switch (leaf)
        {
            case NoteSyntax note when note.Duration == null:
                return note.Pitch.Span.Start + note.Pitch.Span.Length;
            case RestSyntax rest when rest.Duration == null:
                return InkEnd(rest.GetChild(0)!);
            case ChordSyntax chord when chord.Duration == null:
                // After the '>' and any octave marks on it.
                int end = -1;
                for (int i = 0; i < chord.SlotCount; i++)
                    if (chord.GetChild(i) is SyntaxTokenNode t && t.Width > 0)
                    {
                        if (t.Text == ">") end = t.Span.Start + t.Span.Length;
                        else if (end >= 0 && t.Text is "'" or ",") end = t.Span.Start + t.Span.Length;
                        else if (end >= 0) break;
                    }
                return end >= 0 ? end : null;
            default:
                return null;
        }
    }

    internal static string IndentOf(string source, int at)
    {
        int lineStart = source.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        int i = lineStart;
        while (i < at && source[i] is ' ' or '\t') i++;
        return source[lineStart..i];
    }

    internal static IEnumerable<SyntaxNode> BodyItems(SyntaxNode body)
    {
        bool inBody = false;
        for (int i = 0; i < body.SlotCount; i++)
        {
            var child = body.GetChild(i);
            if (child == null)
                continue;
            if (child is SyntaxTokenNode { Kind: SyntaxKind.OpenBrace } && !inBody) { inBody = true; continue; }
            if (child is SyntaxTokenNode { Kind: SyntaxKind.CloseBrace } && inBody) break;
            if (inBody)
                yield return child;
        }
    }

    /// <summary>[just after a cell's opening brace, its closing brace's start): the first '{' of the
    /// cell's text and its last token, a '}' (a part's block holds its braces in a child node).</summary>
    internal static (int Start, int End) BodyRange(SyntaxNode body)
        => (OpenBraceEnd(body), LastToken(body) is { Kind: SyntaxKind.CloseBrace } close ? close.Span.Start : -1);

    internal static SyntaxTokenNode? OpenBrace(SyntaxNode body)
        => body.DescendantNodes().OfType<SyntaxTokenNode>().FirstOrDefault(t => t.Kind == SyntaxKind.OpenBrace && t.Width > 0);

    internal static int OpenBraceEnd(SyntaxNode body) => OpenBrace(body) is { } open ? open.Span.Start + open.Span.Length : -1;

    internal static int OpenBraceStart(SyntaxNode body) => OpenBrace(body)?.Span.Start ?? -1;

    internal static SyntaxTokenNode? LastToken(SyntaxNode node)
    {
        if (node is SyntaxTokenNode t)
            return t.Width > 0 ? t : null;
        for (int i = node.SlotCount - 1; i >= 0; i--)
            if (node.GetChild(i) is { } child && LastToken(child) is { } last)
                return last;
        return null;
    }

    internal static SyntaxTokenNode? FirstToken(SyntaxNode node)
    {
        if (node is SyntaxTokenNode t)
            return t.Width > 0 ? t : null;
        for (int i = 0; i < node.SlotCount; i++)
            if (node.GetChild(i) is { } child && FirstToken(child) is { } first)
                return first;
        return null;
    }

    internal static int InkStart(SyntaxNode node) => FirstToken(node)?.Span.Start ?? node.Span.Start;

    internal static int InkEnd(SyntaxNode node) => LastToken(node) is { } t ? t.Span.Start + t.Span.Length : node.Span.Start + node.Span.Length;

    internal readonly record struct Edit(int Start, int End, string Text);

    internal static string Apply(string source, List<Edit> edits)
    {
        var sb = new StringBuilder(source.Length + 256);
        int at = 0;
        foreach (var e in edits.Select((e, i) => (e, i)).OrderBy(x => x.e.Start).ThenBy(x => x.i).Select(x => x.e))
        {
            if (e.Start < at)
                continue;
            sb.Append(source, at, e.Start - at).Append(e.Text);
            at = e.End;
        }
        sb.Append(source, at, source.Length - at);
        return sb.ToString();
    }
}
