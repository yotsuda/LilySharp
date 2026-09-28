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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>The octave mode a <c>.lys</c> file is rewritten into.</summary>
public enum OctaveMode
{
    /// <summary>Each bare pitch lands nearest the previous one (the default).</summary>
    Relative,
    /// <summary><c>octave absolute</c>: a bare <c>c</c> is always C4 (or the part's base).</summary>
    Absolute,
}

/// <summary>
/// Rewrites a whole <c>.lys</c> file from one octave mode to the other, keeping every note
/// at the pitch it sounds now: the mode directives go, one file-level
/// <c>octave absolute</c> comes (for <see cref="OctaveMode.Absolute"/>), and each written
/// pitch's <c>'</c>/<c>,</c> marks are recomputed. Nothing else in the file is touched.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE WALK DECIDES, NOT A COPY OF ITS RULES. What a mark means depends on the frame the
/// notes before it leave — phrases open a fresh frame, section references shift it, a chord
/// reads it and gives it back, an arpeggio stacks on its root, degrees stack on the anchor —
/// and a converter that re-implemented those rules would be one more walker to drift from
/// the collector. So the collector is run three times through its
/// <see cref="MeasureCollector.OctaveOverride"/> hook:
/// </para>
/// <para>
/// ⑴ RECORD the original file: every octave each written pitch resolves to, in walk order
/// (a phrase played three times is three entries). ⑵ FORCE the file with only its mode
/// directives changed: each pitch is made to resolve to its recorded octave, so the frame
/// the next pitch reads is the one the converted file will give it; the mark count that
/// produces that octave is <c>recorded − (resolved − written)</c>, because every octave the
/// walk computes is the pitch's zero-mark octave plus its own marks. ⑶ VERIFY the
/// rewritten file with no forcing: every pitch must resolve exactly as in ⑴, or nothing is
/// returned.
/// </para>
/// <para>
/// Refused rather than guessed: a file with syntax errors, a file that pulls in another
/// (<c>using</c> — its pitches are not in this text), a written pitch no score plays (the
/// walk never reads it, so nothing says what it means), and a pitch whose plays would need
/// different marks.
/// </para>
/// </remarks>
public static class OctaveModeConverter
{
    /// <summary>The rewritten text, or null with <see cref="Error"/> saying why the file was
    /// left alone. <see cref="ChangedPitches"/> counts the pitches whose marks changed.</summary>
    public sealed record Result(string? NewText, string? Error, int ChangedPitches);

    /// <summary>Rewrites <paramref name="source"/> into <paramref name="target"/> mode.</summary>
    public static Result Convert(string source, OctaveMode target)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("Fix the syntax errors before converting octaves — no changes made.");
        var root = tree.GetRoot();
        if (root.DescendantNodes<UsingDirectiveSyntax>().Any())
            return Fail("This file includes another with 'using', whose notes are not in this "
                + "file — no changes made.");

        var pitches = WrittenPitches(root);

        // ⑴ Record.
        var recorded = new Dictionary<int, List<int>>();
        if (!Walk(tree, (pos, octave) =>
            {
                if (!recorded.TryGetValue(pos, out var list))
                    recorded[pos] = list = new List<int>();
                list.Add(octave);
                return octave;
            }))
            return Fail("The file could not be compiled — no changes made.");

        foreach (var (pos, _) in pitches)
            if (!recorded.ContainsKey(pos))
                return Fail($"The note at {LineCol(source, pos)}{Where(root, pos)} is never played by "
                    + "any score, so nothing says what octave it means. Play it (add it to the form "
                    + "or a score) or remove it, then convert again — no changes made.");
        foreach (var pos in recorded.Keys)
            if (!pitches.ContainsKey(pos))
                return Fail("A note resolved outside this file's text — no changes made.");

        // ⑵ Force the file with only its mode directives changed.
        var modeEdits = ModeEdits(source, root, target);
        var candidate = Apply(source, modeEdits);
        var candidateTree = SyntaxTree.Parse(candidate);
        if (candidateTree.HasErrors)
            return Fail("Changing the octave directives broke the file — no changes made.");
        var toOriginal = PositionMap(WrittenPitches(candidateTree.GetRoot()), pitches);
        if (toOriginal == null)
            return Fail("Changing the octave directives moved the notes — no changes made.");

        var newMarks = new Dictionary<int, int>();
        var plays = new Dictionary<int, int>();
        int? conflict = null;
        bool lost = false;
        if (!Walk(candidateTree, (pos, octave) =>
            {
                if (!toOriginal.TryGetValue(pos, out int orig)
                    || !recorded.TryGetValue(orig, out var wanted))
                {
                    lost = true;
                    return octave;
                }
                plays.TryGetValue(orig, out int k);
                plays[orig] = k + 1;
                if (k >= wanted.Count)
                {
                    lost = true;
                    return octave;
                }
                int marks = wanted[k] - (octave - pitches[orig].Marks);
                if (newMarks.TryGetValue(orig, out int earlier) && earlier != marks)
                    conflict ??= orig;
                newMarks[orig] = marks;
                return wanted[k];
            }))
            return Fail("The file could not be compiled in the other mode — no changes made.");
        if (conflict is { } c)
            return Fail($"The note at {LineCol(source, c)} is played in places that would need "
                + "different octave marks in the other mode — no changes made.");
        if (lost || plays.Count != recorded.Count
            || plays.Any(p => p.Value != recorded[p.Key].Count))
            return Fail("The other mode plays the notes in a different order — no changes made.");

        // The rewrite: the mode directives, and every pitch whose marks change.
        var edits = new List<Edit>(modeEdits);
        int changed = 0;
        foreach (var (pos, pitch) in pitches)
        {
            if (newMarks[pos] == pitch.Marks)
                continue;
            edits.Add(new Edit(pitch.MarksStart, pitch.MarksEnd, MarkText(newMarks[pos])));
            changed++;
        }
        var result = Apply(source, edits);

        // ⑶ Verify.
        var resultTree = SyntaxTree.Parse(result);
        if (resultTree.HasErrors)
            return Fail("The rewritten file would not parse — no changes made.");
        var resultToOriginal = PositionMap(WrittenPitches(resultTree.GetRoot()), pitches);
        if (resultToOriginal == null)
            return Fail("The rewritten file lost track of its notes — no changes made.");
        var again = new Dictionary<int, List<int>>();
        if (!Walk(resultTree, (pos, octave) =>
            {
                if (resultToOriginal.TryGetValue(pos, out int orig))
                {
                    if (!again.TryGetValue(orig, out var list))
                        again[orig] = list = new List<int>();
                    list.Add(octave);
                }
                return octave;
            }))
            return Fail("The rewritten file could not be compiled — no changes made.");
        foreach (var (pos, wanted) in recorded)
            if (!again.TryGetValue(pos, out var got) || !got.SequenceEqual(wanted))
                return Fail($"The rewritten file would move the note at {LineCol(source, pos)} — "
                    + "no changes made.");

        return new Result(result, null, changed);
    }

    /// <summary>
    /// The same pitch-preserving machinery, lent to a rewrite that is NOT a mode change:
    /// <paramref name="candidate"/> is <paramref name="source"/> with text moved around its
    /// pitches (no pitch added or removed, the order kept), and every pitch the original
    /// played is re-marked so it resolves to the octave it did — steps ⑵ and ⑶ above, with
    /// the candidate standing in for the mode-changed file.
    /// </summary>
    /// <remarks>
    /// Written for <see cref="SectionSplitter"/> (2026-09-28): cutting a section in two opens
    /// the second half in a FRESH frame, so its first pitch needs the marks that put it back
    /// where the running frame had it. A pitch the original never played (the reference
    /// part's later sections under a form that did not list them yet) has nothing recorded
    /// and is left as written; one the original played is played exactly as often, or the
    /// rewrite is refused.
    /// </remarks>
    internal static Result Repin(string source, string candidate)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors — no changes made.");
        var pitches = WrittenPitches(tree.GetRoot());
        var recorded = new Dictionary<int, List<int>>();
        if (!Walk(tree, (pos, octave) =>
            {
                if (!recorded.TryGetValue(pos, out var list))
                    recorded[pos] = list = new List<int>();
                list.Add(octave);
                return octave;
            }))
            return Fail("The file could not be compiled — no changes made.");

        var candidateTree = SyntaxTree.Parse(candidate);
        if (candidateTree.HasErrors)
            return Fail("The rewritten file would not parse — no changes made.");
        var candidatePitches = WrittenPitches(candidateTree.GetRoot());
        var toOriginal = PositionMap(candidatePitches, pitches);
        if (toOriginal == null)
            return Fail("The rewrite moved the notes — no changes made.");

        // ⑵ Force: each recorded pitch resolves where it did; the marks that make it do so
        // are read off the walk (resolved − written = what the frame contributed).
        var newMarks = new Dictionary<int, int>();   // candidate position → marks
        var plays = new Dictionary<int, int>();      // original position → plays seen
        int? conflict = null;
        if (!Walk(candidateTree, (pos, octave) =>
            {
                if (!toOriginal.TryGetValue(pos, out int orig)
                    || !recorded.TryGetValue(orig, out var wanted))
                    return octave;
                plays.TryGetValue(orig, out int k);
                plays[orig] = k + 1;
                if (k >= wanted.Count)
                    return octave;
                int marks = wanted[k] - (octave - candidatePitches[pos].Marks);
                if (newMarks.TryGetValue(pos, out int earlier) && earlier != marks)
                    conflict ??= orig;
                newMarks[pos] = marks;
                return wanted[k];
            }))
            return Fail("The rewritten file could not be compiled — no changes made.");
        if (conflict is { } c)
            return Fail($"The note at {LineCol(source, c)} is played in places that would need "
                + "different octave marks after the rewrite — no changes made.");
        if (recorded.Any(r => plays.GetValueOrDefault(r.Key) != r.Value.Count))
            return Fail("The rewrite plays the notes a different number of times — no changes made.");

        var edits = new List<Edit>();
        int changed = 0;
        foreach (var (pos, marks) in newMarks)
        {
            var pitch = candidatePitches[pos];
            if (marks == pitch.Marks)
                continue;
            edits.Add(new Edit(pitch.MarksStart, pitch.MarksEnd, MarkText(marks)));
            changed++;
        }
        var result = Apply(candidate, edits);

        // ⑶ Verify, unforced.
        var resultTree = SyntaxTree.Parse(result);
        var resultToOriginal = resultTree.HasErrors ? null
            : PositionMap(WrittenPitches(resultTree.GetRoot()), pitches);
        if (resultToOriginal == null)
            return Fail("The re-marked file lost track of its notes — no changes made.");
        var again = new Dictionary<int, List<int>>();
        if (!Walk(resultTree, (pos, octave) =>
            {
                if (resultToOriginal.TryGetValue(pos, out int orig))
                {
                    if (!again.TryGetValue(orig, out var list))
                        again[orig] = list = new List<int>();
                    list.Add(octave);
                }
                return octave;
            }))
            return Fail("The re-marked file could not be compiled — no changes made.");
        foreach (var (pos, wanted) in recorded)
            if (!again.TryGetValue(pos, out var got) || !got.SequenceEqual(wanted))
                return Fail($"The rewrite would move the note at {LineCol(source, pos)} — "
                    + "no changes made.");
        return new Result(result, null, changed);
    }

    private static Result Fail(string error) => new(null, error, 0);

    /// <summary>" (section B2 of part bass)" — the declarations around the pitch at
    /// <paramref name="pos"/>, so an unplayed note's refusal says which block is unplayed.</summary>
    private static string Where(SyntaxNode root, int pos)
    {
        var pitch = root.DescendantNodes<SyntaxNode>()
            .FirstOrDefault(n => n is PitchSyntax or ScaleDegreeSyntax && n.SourceStart == pos);
        var names = new List<string>();
        for (var p = pitch?.Parent; p != null; p = p.Parent)
        {
            switch (p)
            {
                case SectionDeclarationSyntax s: names.Add($"section {s.SectionName}"); break;
                case PhraseDeclarationSyntax ph: names.Add($"phrase {ph.Name.Text}"); break;
                case PartDeclarationSyntax pt: names.Add($"part {pt.Name.Text}"); break;
            }
        }
        return names.Count == 0 ? "" : $" ({string.Join(" of ", names)})";
    }

    // --- the walk -------------------------------------------------------------

    /// <summary>Collects every score the file renders (one spec-less pass for a scoreless
    /// file — the same passes <see cref="Semantics.ResolvedPitches"/> makes) with
    /// <paramref name="hook"/> on every written pitch. False when a score will not collect.
    /// </summary>
    private static bool Walk(SyntaxTree tree, Func<int, int, int> hook)
    {
        var specs = RenderSpecParser.FindAll(tree);
        IEnumerable<RenderSpec?> passes = specs;
        if (specs.Count == 0)
            passes = [null];
        try
        {
            foreach (var spec in passes)
            {
                var collector = new MeasureCollector
                {
                    ScoreTranspose = spec?.ScoreTranspose,
                    ScoreConcert = spec?.ScoreConcert ?? false,
                    RecordsPitchTrace = false,
                    OctaveOverride = hook,
                };
                SvgGenerator.CollectScore(collector, tree, spec);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // --- the written pitches --------------------------------------------------

    /// <summary>A pitch or scale degree as written: its marks' count, and the span they
    /// occupy (empty, just after the name, when it has none).</summary>
    private readonly record struct WrittenPitch(int Marks, int MarksStart, int MarksEnd);

    /// <summary>Every pitch and scale degree in the file, keyed by the position the walk
    /// reports it at (its first character), in document order.</summary>
    private static SortedDictionary<int, WrittenPitch> WrittenPitches(SyntaxNode root)
    {
        var map = new SortedDictionary<int, WrittenPitch>();
        foreach (var node in root.DescendantNodes<SyntaxNode>())
        {
            // Only MUSIC: a note's pitch, a chord or arpeggio member. A key signature's tonic
            // (`key g major`) or a transpose target is a pitch with no octave to resolve.
            if (node is not (PitchSyntax or ScaleDegreeSyntax)
                || node.Parent is not (NoteSyntax or ChordSyntax or ArpeggioSyntax))
                continue;
            // Slot 0 is the name (letter + accidental, or the degree number); the marks are
            // the apostrophe / comma tokens straight after it (PitchGreen, ScaleDegreeGreen).
            int marksStart = node.GetChild(0)!.Span.End, marksEnd = marksStart, marks = 0;
            for (int i = 1; i < node.SlotCount; i++)
            {
                if (node.GetChild(i) is not SyntaxTokenNode t
                    || t.Kind is not (SyntaxKind.Apostrophe or SyntaxKind.Comma))
                    break;
                marks += t.Kind == SyntaxKind.Apostrophe ? 1 : -1;
                marksEnd = t.Span.End;
            }
            map[node.SourceStart] = new WrittenPitch(marks, marksStart, marksEnd);
        }
        return map;
    }

    /// <summary>Pairs two files' pitches in document order (the rewrite adds and removes
    /// none), or null when their counts differ.</summary>
    private static Dictionary<int, int>? PositionMap(
        SortedDictionary<int, WrittenPitch> from, SortedDictionary<int, WrittenPitch> to)
    {
        if (from.Count != to.Count)
            return null;
        var map = new Dictionary<int, int>(from.Count);
        foreach (var (a, b) in from.Keys.Zip(to.Keys))
            map[a] = b;
        return map;
    }

    private static string MarkText(int marks)
        => marks >= 0 ? new string('\'', marks) : new string(',', -marks);

    // --- the mode directives --------------------------------------------------

    /// <summary>Removes every <c>octave absolute</c> / <c>octave relative</c> and, for
    /// absolute, states it once at the top level — replacing the first top-level directive
    /// when there is one, else just before the first thing that is not the file's header.
    /// </summary>
    private static List<Edit> ModeEdits(string source, CompilationUnitSyntax root, OctaveMode target)
    {
        var edits = new List<Edit>();
        var directives = root.DescendantNodes<OctaveDirectiveSyntax>().ToList();
        bool stated = false;
        foreach (var d in directives)
        {
            if (target == OctaveMode.Absolute && !stated && d.Parent is CompilationUnitSyntax)
            {
                edits.Add(new Edit(d.Span.Start, d.Span.End, "octave absolute"));
                stated = true;
                continue;
            }
            edits.Add(Removal(source, d.Span.Start, d.Span.End));
        }
        // A group's marks (`<c e g>'`, `<< c e g >>,`) move the frame in relative mode; in
        // absolute there is no frame, so they would only restate an octave the members can
        // carry themselves. Dropped here, and the forcing walk puts each member where it was.
        if (target == OctaveMode.Absolute)
            foreach (var group in root.DescendantNodes<SyntaxNode>()
                         // A chord(…) item has no marks to drop (refused, LYS0035), and the ','
                         // between its words is a separator, not an octave.
                         .Where(n => n is ChordSyntax { IsShapeChord: false } or ArpeggioSyntax))
                for (int i = 0; i < group.SlotCount; i++)
                    if (group.GetChild(i) is SyntaxTokenNode
                        {
                            Kind: SyntaxKind.Apostrophe or SyntaxKind.Comma
                        } mark)
                        edits.Add(new Edit(mark.Span.Start, mark.Span.End, ""));
        if (target == OctaveMode.Absolute && !stated)
        {
            var first = root.DescendantNodes<SyntaxNode>()
                .Where(n => n.Parent is CompilationUnitSyntax && n is not SyntaxTokenNode)
                .FirstOrDefault(n => !IsHeader(n));
            int at = first == null ? source.Length : LineStart(source, first.Span.Start);
            string indent = first == null ? "" : source[at..first.Span.Start];
            if (indent.Any(ch => !char.IsWhiteSpace(ch)))
            {
                at = first!.Span.Start;
                indent = "";
            }
            edits.Add(new Edit(at, at, indent + "octave absolute\n"));
        }
        return edits;
    }

    /// <summary>The top-level items that open a file before its music — its title block,
    /// global settings and display switches — which the new directive goes after.</summary>
    private static bool IsHeader(SyntaxNode n)
        => n is MetadataDeclarationSyntax or TempoDeclarationSyntax or PartialDeclarationSyntax
            or FontDeclarationSyntax or PaperDeclarationSyntax or LayoutDeclarationSyntax
            or ClefDeclarationSyntax or OverrideDeclarationSyntax or RevertDeclarationSyntax
            or DrummapDeclarationSyntax
        || n.GetType().Name is var name
            && (name.StartsWith("Key") || name.StartsWith("Time") || name.StartsWith("Transpose")
                || name.StartsWith("PitchMode"));

    /// <summary>Removes [start, end): the whole line when nothing else stands on it,
    /// otherwise the directive and the spaces after it.</summary>
    private static Edit Removal(string source, int start, int end)
    {
        int lineStart = LineStart(source, start);
        int after = end;
        while (after < source.Length && source[after] is ' ' or '\t')
            after++;
        bool aloneBefore = source[lineStart..start].All(ch => ch is ' ' or '\t');
        bool aloneAfter = after >= source.Length || source[after] is '\r' or '\n';
        if (aloneBefore && aloneAfter)
        {
            if (after < source.Length && source[after] == '\r') after++;
            if (after < source.Length && source[after] == '\n') after++;
            return new Edit(lineStart, after, "");
        }
        return new Edit(start, after, "");
    }

    private static int LineStart(string source, int pos)
    {
        while (pos > 0 && source[pos - 1] != '\n')
            pos--;
        return pos;
    }

    // --- text edits -----------------------------------------------------------

    private readonly record struct Edit(int Start, int End, string Text);

    private static string Apply(string source, IEnumerable<Edit> edits)
    {
        var sb = new StringBuilder(source.Length + 64);
        int at = 0;
        foreach (var e in edits.OrderBy(e => e.Start).ThenBy(e => e.End))
        {
            sb.Append(source, at, e.Start - at).Append(e.Text);
            at = e.End;
        }
        sb.Append(source, at, source.Length - at);
        return sb.ToString();
    }

    private static string LineCol(string source, int pos)
    {
        int line = 1, col = 1;
        for (int i = 0; i < pos && i < source.Length; i++)
        {
            if (source[i] == '\n') { line++; col = 1; }
            else col++;
        }
        return $"line {line}, column {col}";
    }
}
