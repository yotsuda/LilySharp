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
using LilySharp.Core.Semantics;
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
/// at the pitch it sounds now: the file's first <c>octave</c> directive names the new mode
/// (one is added at the top when the file had none — for <see cref="OctaveMode.Relative"/>
/// only when a mode was stated somewhere, relative being the default), the switches inside
/// music go, and each written pitch's <c>'</c>/<c>,</c> marks are recomputed. Nothing else
/// in the file is touched.
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
    public static Result Convert(string source, OctaveMode target) => ConvertWithEdits(source, target).Result;

    /// <summary>One replacement of [Start, End) of the text it was made against.</summary>
    public sealed record TextChange(int Start, int End, string Text);

    /// <summary>The file in absolute octaves for the AI transform, and the changes that made it
    /// (in the source's offsets, in order) so a selection can be carried over — or why not.</summary>
    public sealed record AiAbsolute(string? NewText, string? Error, IReadOnlyList<TextChange> Changes);

    /// <summary>
    /// The AI transform's first half (owner's request 2026-10-25): a file in relative octaves
    /// rewritten into absolute ones, so the model writes each pitch where it sounds instead of
    /// computing the relative chain, the mistake it makes most. Relative is what the file's
    /// top-level <c>octave</c> lines leave in force — none, or a last one saying
    /// <c>relative</c> (the last wins, wherever it stands; owner's request the same day: a line
    /// anywhere at the top level, not only the first). Each of those lines stays where it is,
    /// saying <c>absolute</c>, and a file with none gets one, so the way back can give every
    /// line its own word again. Refused: a file already absolute (it needs nothing), and one
    /// that switches mode inside its music (the switches would not survive the trip).
    /// </summary>
    public static AiAbsolute ToAbsoluteForAi(string source)
    {
        var tree = SyntaxTree.Parse(source);
        if (!tree.HasErrors)
        {
            var directives = tree.GetRoot().DescendantNodes<OctaveDirectiveSyntax>().ToList();
            if (directives.Any(d => d.Parent is not CompilationUnitSyntax))
                return new AiAbsolute(null, "The file switches octave mode inside its music — sent as it is.", []);
            if (directives.Count > 0 && directives[^1].IsAbsolute)
                return new AiAbsolute(null, "The file is in absolute octaves already — sent as it is.", []);
        }
        var (result, edits) = ConvertWithEdits(source, OctaveMode.Absolute, keepTopLevel: true);
        return result.NewText == null
            ? new AiAbsolute(null, result.Error, [])
            : new AiAbsolute(result.NewText, null,
                edits.OrderBy(e => e.Start).ThenBy(e => e.End).Select(e => new TextChange(e.Start, e.End, e.Text)).ToList());
    }

    /// <summary>
    /// The second half: the AI's candidate, in the absolute octaves <see cref="ToAbsoluteForAi"/>
    /// made of <paramref name="original"/>, back in relative octaves, its top-level
    /// <c>octave</c> lines as the original wrote them — each one's own text again, or, when the
    /// original had none, the one added taken out. Every note sounds where the candidate put it
    /// (the conversion verifies that), and a note the AI left alone gets back the marks it had,
    /// since in relative octaves the marks follow from the pitches. Refused when the candidate
    /// no longer holds those lines, each saying <c>absolute</c> at the top level.
    /// </summary>
    public static Result FromAbsoluteForAi(string candidate, string original)
    {
        var tree = SyntaxTree.Parse(candidate);
        if (tree.HasErrors)
            return Fail("The candidate has syntax errors — no changes made.");
        var own = SyntaxTree.Parse(original).GetRoot().DescendantNodes<OctaveDirectiveSyntax>().ToList();
        var directives = tree.GetRoot().DescendantNodes<OctaveDirectiveSyntax>().ToList();
        if (directives.Count != Math.Max(1, own.Count)
            || directives.Any(d => !d.IsAbsolute || d.Parent is not CompilationUnitSyntax))
            return Fail("The candidate no longer holds the file's `octave` lines as they were given to it — no changes made.");
        var (back, _) = ConvertWithEdits(candidate, OctaveMode.Relative, keepTopLevel: true);
        if (back.NewText == null)
            return back;
        // Every top-level line now says relative, in place; give each the original's text.
        var lines = SyntaxTree.Parse(back.NewText).GetRoot().DescendantNodes<OctaveDirectiveSyntax>().ToList();
        var restore = own.Count == 0
            ? lines.Select(d => Removal(back.NewText, d.Span.Start, d.Span.End))
            : lines.Zip(own, (d, o) => new Edit(d.Span.Start, d.Span.End, original[o.Span.Start..o.Span.End]));
        return new Result(Apply(back.NewText, restore), null, back.ChangedPitches);
    }

    private static (Result Result, List<Edit> Edits) ConvertWithEdits(string source, OctaveMode target, bool keepTopLevel = false)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return No("Fix the syntax errors before converting octaves — no changes made.");
        var root = tree.GetRoot();
        if (root.DescendantNodes<UsingDirectiveSyntax>().Any())
            return No("This file includes another with 'using', whose notes are not in this "
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
            return No("The file could not be compiled — no changes made.");

        foreach (var (pos, _) in pitches)
            if (!recorded.ContainsKey(pos))
                return No($"The note at {LineCol(source, pos)}{Where(root, pos)} is never played by "
                    + "any score, so nothing says what octave it means. Play it (add it to the form "
                    + "or a score) or remove it, then convert again — no changes made.");
        foreach (var pos in recorded.Keys)
            if (!pitches.ContainsKey(pos))
                return No("A note resolved outside this file's text — no changes made.");

        // ⑵ Force the file with only its mode directives changed.
        var modeEdits = ModeEdits(source, root, target, keepTopLevel);
        var candidate = Apply(source, modeEdits);
        var candidateTree = SyntaxTree.Parse(candidate);
        if (candidateTree.HasErrors)
            return No("Changing the octave directives broke the file — no changes made.");
        var toOriginal = PositionMap(WrittenPitches(candidateTree.GetRoot()), pitches);
        if (toOriginal == null)
            return No("Changing the octave directives moved the notes — no changes made.");

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
            return No("The file could not be compiled in the other mode — no changes made.");
        if (conflict is { } c)
            return No($"The note at {LineCol(source, c)} is played in places that would need "
                + "different octave marks in the other mode — no changes made.");
        if (lost || plays.Count != recorded.Count
            || plays.Any(p => p.Value != recorded[p.Key].Count))
            return No("The other mode plays the notes in a different order — no changes made.");

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
            return No("The rewritten file would not parse — no changes made.");
        var resultToOriginal = PositionMap(WrittenPitches(resultTree.GetRoot()), pitches);
        if (resultToOriginal == null)
            return No("The rewritten file lost track of its notes — no changes made.");
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
            return No("The rewritten file could not be compiled — no changes made.");
        foreach (var (pos, wanted) in recorded)
            if (!again.TryGetValue(pos, out var got) || !got.SequenceEqual(wanted))
                return No($"The rewritten file would move the note at {LineCol(source, pos)} — "
                    + "no changes made.");

        return (new Result(result, null, changed), edits);
    }

    /// <summary>
    /// <paramref name="source"/> without its parts' <c>octave N</c>, every note re-marked to sound
    /// where it did (<see cref="Repin"/>). Unchanged (and no error) when no part writes one.
    /// </summary>
    /// <remarks>
    /// For a piece the AI composed (owner's request 2026-10-28): its bass is written under
    /// <c>octave 4</c> so the AI can name the pitches it sounds, and the line is the AI's crutch,
    /// not the musician's — the piece opens without it, every note where the AI put it.
    /// </remarks>
    public static Result WithoutPartOctaves(string source)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors — no changes made.");
        var removals = new List<Edit>();
        foreach (var part in tree.GetRoot().DescendantNodes<PartDeclarationSyntax>())
            foreach (var prop in part.Properties)
            {
                if (prop.NameToken.Text != "octave")
                    continue;
                // The property and the spaces before it (`{ instrument bass octave 4 }`).
                int start = prop.Span.Start;
                while (start > 0 && source[start - 1] is ' ' or '\t')
                    start--;
                removals.Add(new Edit(start, prop.Span.End, ""));
            }
        if (removals.Count == 0)
            return new Result(source, null, 0);
        return Repin(source, Apply(source, removals));
    }

    /// <summary>
    /// <paramref name="source"/> without its top-level <c>pitch concert</c>, each part written as
    /// its player reads it and sounding exactly as before: an octave-only transposer's notes
    /// (a bass, a piccolo) move the octave it transposes by, the rest stay. A part that transposes
    /// by another interval (a B♭ trumpet, an E♭ saxophone — its letters would have to be
    /// respelled in the player's key, which this does not do) keeps its letters and gets its own
    /// <c>pitch concert</c>; a part that already says its own <c>pitch</c> is left alone.
    /// Unchanged (no error) when the file is not at concert pitch.
    /// </summary>
    /// <remarks>
    /// For a piece the AI composed (owner's request 2026-10-28): it writes every part at the pitch
    /// it sounds under <c>pitch concert</c>, and the piece opens as a musician writes it. Besides
    /// <see cref="Repin"/>'s own check (each written pitch where it was asked to be), the result
    /// is played: the sounding notes must be the source's, note for note.
    /// </remarks>
    public static Result WithoutConcertPitch(string source)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors — no changes made.");
        var root = tree.GetRoot();
        var line = root.DescendantNodes<PropertyAssignmentSyntax>()
            .FirstOrDefault(p => ConcertPitch.TryReadFileMode(p, out bool concert) && concert);
        if (line == null)
            return new Result(source, null, 0);
        var shifts = new Dictionary<string, int>(StringComparer.Ordinal);   // part → octaves its notes move
        // A part that transposes by another interval than octaves (a B♭ trumpet) keeps its letters
        // and says so in its own header — `pitch concert`, which wins over the file's — so the
        // line can still go (owner's decision 2026-10-28: a trumpet and a bass in one piece).
        var keepConcert = new List<Edit>();
        foreach (var part in root.DescendantNodes<PartDeclarationSyntax>())
        {
            if (ConcertPitch.PartMode(part) != null)
                continue;   // says its own, which the file's line never governed
            int t = PartHeaderDefaults.Read(part).TranspositionSemitones;
            if (t % 12 != 0)
            {
                int close = part.Span.End - 1;
                keepConcert.Add(source[close] == '}'
                    ? new Edit(close, close, (source[close - 1] == ' ' ? "" : " ") + "pitch concert ")
                    : new Edit(part.Span.End, part.Span.End, " { pitch concert }"));
                continue;
            }
            shifts[part.Name.Text] = -t / 12;
        }
        var octaveShift = new Dictionary<int, int>();
        foreach (var node in root.DescendantNodes<SyntaxNode>())
            if (node is PitchSyntax or ScaleDegreeSyntax
                && BlockOf(node) is { } block
                && shifts.TryGetValue(block.PartName.Text, out int s) && s != 0)
                octaveShift[node.SourceStart] = s;

        // The line, and the line break after it when it stands alone.
        int start = line.Span.Start, end = line.Span.End;
        while (start > 0 && source[start - 1] is ' ' or '\t') start--;
        if (end < source.Length && source[end] == '\r') end++;
        if (end < source.Length && source[end] == '\n') end++;
        var result = Repin(source, Apply(source, [new Edit(start, end, ""), .. keepConcert]), octaveShift);
        if (result.NewText == null)
            return result;
        static string Sounds(string text) => string.Join(" ", new Midi.MidiExporter().Export(SyntaxTree.Parse(text)).Tracks
            .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => $"{n.StartTick}:{n.Pitch}"));
        return Sounds(result.NewText) == Sounds(source)
            ? result
            : Fail("Without pitch concert the piece would sound otherwise — the file keeps it.");
    }

    /// <summary>
    /// The migration to LilyPond's octaves (owner's decision 2026-10-28): <paramref name="source"/>,
    /// written under the old rules, re-marked so it sounds as it did. <paramref name="legacySounds"/>
    /// is what each written pitch (by source position) sounded under the old rules, in time order;
    /// <paramref name="legacySignature"/> the whole old performance (<see cref="SoundSignature"/>).
    /// Each pitch moves the whole octaves between what it sounds now and what it sounded then,
    /// through <see cref="Repin"/>; refused when a pitch would move by other than whole octaves,
    /// by different amounts in different plays, or the result does not sound note for note as
    /// before.
    /// </summary>
    /// <param name="legacyTrace">What each written pitch the page drew was DRAWN as under the old
    /// rules (the collector's pitch trace, <c>C4</c> / <c>F#5</c>, every score, in walk order) — for
    /// a pitch no performance plays (an ossia, a cue), which <paramref name="legacySounds"/> cannot
    /// place; such a pitch moves the octaves between its drawings then and now, and must be drawn
    /// as it was.</param>
    public static Result MigrateFromLegacy(string source, IReadOnlyDictionary<int, IReadOnlyList<int>> legacySounds,
        string legacySignature, IReadOnlyDictionary<int, IReadOnlyList<string>>? legacyTrace = null)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors — no changes made.");
        var drawnNow = legacyTrace == null ? null : TraceOf(tree);
        var now = new Midi.MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes)
            .Where(n => n.SourcePos >= 0).GroupBy(n => n.SourcePos)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.StartTick).Select(n => n.Pitch).ToList());
        var shift = new Dictionary<int, int>();
        foreach (var (pos, then) in legacySounds)
        {
            if (!now.TryGetValue(pos, out var sounds) || sounds.Count != then.Count)
                return Fail($"The note at {LineCol(source, pos)} plays a different number of times now — no changes made.");
            int? octaves = null;
            for (int i = 0; i < then.Count; i++)
            {
                int diff = then[i] - sounds[i];
                if (diff % 12 != 0 || (octaves is { } o && o != diff / 12))
                    return Fail($"The note at {LineCol(source, pos)} moved by other than one octave count — no changes made.");
                octaves = diff / 12;
            }
            if (octaves is { } d && d != 0)
                shift[pos] = d;
        }
        // A chord's members sound under the CHORD's position (one note each, all at the chord's
        // source position), so a member moves as its chord does.
        var played = new HashSet<int>(legacySounds.Keys);
        foreach (var chord in tree.GetRoot().DescendantNodes<ChordSyntax>())
        {
            if (!legacySounds.ContainsKey(chord.SourceStart))
                continue;
            foreach (var member in chord.DescendantNodes<PitchSyntax>().Where(p => p.Parent == chord))
            {
                played.Add(member.SourceStart);
                if (shift.TryGetValue(chord.SourceStart, out int d))
                    shift[member.SourceStart] = d;
            }
        }
        // A pitch no performance plays but the page draws (an ossia, a cue) moves as its drawing did.
        var drawnOnly = new HashSet<int>();
        if (legacyTrace != null && drawnNow != null)
            foreach (var (pos, then) in legacyTrace)
            {
                if (played.Contains(pos) || !drawnNow.TryGetValue(pos, out var drawn) || drawn.Count != then.Count)
                    continue;
                int? octaves = null;
                bool whole = true;
                for (int i = 0; i < then.Count && whole; i++)
                {
                    if (TracedSemitone(then[i]) is not int a || TracedSemitone(drawn[i]) is not int b || (a - b) % 12 != 0
                        || (octaves is { } o && o != (a - b) / 12))
                        whole = false;
                    else
                        octaves = (a - b) / 12;
                }
                if (!whole)
                    return Fail($"The note at {LineCol(source, pos)} is drawn otherwise now, by other than whole octaves — no changes made.");
                drawnOnly.Add(pos);
                if (octaves is { } d && d != 0)
                    shift[pos] = d;
            }
        // A pitch that sounds no note of its own — a tie's continuation, merged into the note it
        // continues — moves as the pitch before it does: in the old rules and the new alike it is
        // that pitch's neighbour, written with the same relation to it.
        int? last = null;
        foreach (var pos in WrittenPitches(tree.GetRoot()).Keys)
        {
            if (played.Contains(pos) || drawnOnly.Contains(pos))
                last = shift.GetValueOrDefault(pos);
            else if (last is { } l && l != 0)
                shift[pos] = l;
        }
        if (shift.Count == 0)
            return SoundSignature(source) == legacySignature
                ? new Result(source, null, 0)
                : Fail("The file sounds otherwise now, and no pitch says by how much — no changes made.");
        var result = Repin(source, source, shift);
        // The walk does not reach every pitch the performance plays (a part's own sections in a
        // file with no score): what it left is re-marked against the performance itself.
        if (result.NewText == null || SoundSignature(result.NewText) != legacySignature)
        {
            var byPerformance = RemarkByPerformance(source, result.NewText ?? source, legacySounds);
            if (byPerformance == null || SoundSignature(byPerformance) != legacySignature)
                return result.NewText == null ? result : Fail("The re-marked file does not sound as it did — no changes made.");
            result = new Result(byPerformance, null, CountChangedMarks(source, byPerformance));
        }
        // …and what no one plays is drawn as it was. Positions move with the rewrite, so the
        // drawings are paired in document order.
        if (legacyTrace != null && drawnOnly.Count > 0)
        {
            var map = PositionMap(WrittenPitches(SyntaxTree.Parse(result.NewText).GetRoot()), WrittenPitches(tree.GetRoot()));
            var after = TraceOf(SyntaxTree.Parse(result.NewText));
            if (map == null)
                return Fail("The re-marked file lost track of its notes — no changes made.");
            var back = map.ToDictionary(kv => kv.Value, kv => kv.Key);
            foreach (var pos in drawnOnly)
                if (!back.TryGetValue(pos, out int at) || !after.TryGetValue(at, out var drawn)
                    || !drawn.SequenceEqual(legacyTrace[pos]))
                    return Fail($"The note at {LineCol(source, pos)} would be drawn otherwise — no changes made.");
        }
        return result;
    }

    // A pitch at a time, in document order: the first written pitch (or chord) that sounds otherwise
    // than legacySounds says gets the marks that bring it back, chosen by trying them; in relative
    // mode that moves the pitches after it too, which the next round reads again. Null when a
    // round finds nothing that helps.
    private static string? RemarkByPerformance(string source, string start, IReadOnlyDictionary<int, IReadOnlyList<int>> legacySounds)
    {
        var original = SyntaxTree.Parse(source).GetRoot();
        var originalPitches = WrittenPitches(original);
        var originalGroups = GroupStarts(original);
        var order = legacySounds.Keys.OrderBy(p => p).ToList();

        // (first mismatched original position or int.MaxValue, notes matching there), and the
        // tree it was read from.
        (int At, int Matching, SyntaxTree Tree)? Score(string text)
        {
            var tree = SyntaxTree.Parse(text);
            if (tree.HasErrors)
                return null;
            var root = tree.GetRoot();
            var toOriginal = PositionMap(WrittenPitches(root), originalPitches);
            var groups = GroupStarts(root);
            if (toOriginal == null || groups.Count != originalGroups.Count)
                return null;
            foreach (var (a, b) in groups.Zip(originalGroups))
                toOriginal[a] = b;
            var now = new Dictionary<int, List<int>>();
            foreach (var n in new Midi.MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick))
                if (n.SourcePos >= 0 && toOriginal.TryGetValue(n.SourcePos, out int orig))
                    (now.TryGetValue(orig, out var l) ? l : now[orig] = new List<int>()).Add(n.Pitch);
            foreach (var pos in order)
            {
                var want = legacySounds[pos];
                var got = now.GetValueOrDefault(pos) ?? [];
                if (got.SequenceEqual(want))
                    continue;
                int matching = got.Count == want.Count ? got.Zip(want).Count(p => p.First == p.Second) : -1;
                return (pos, matching, tree);
            }
            return (int.MaxValue, 0, tree);
        }

        string text = start;
        var score = Score(text);
        for (int round = 0; score is { } s && s.At != int.MaxValue && round < 4 * originalPitches.Count + 8; round++)
        {
            // The pitches written at that position, in the current text: the note's, or a group's members.
            var root = s.Tree.GetRoot();
            var currentPitches = WrittenPitches(root);
            var back = PositionMap(originalPitches, currentPitches)!;
            var currentGroups = GroupStarts(root);
            var candidates = new List<int>();
            if (back.TryGetValue(s.At, out int pitchAt))
                candidates.Add(pitchAt);
            else if (originalGroups.IndexOf(s.At) is int g and >= 0)
                candidates.AddRange(root.DescendantNodes<SyntaxNode>()
                    .Where(n => n.SourceStart == currentGroups[g] && n is ChordSyntax or ArpeggioSyntax)
                    .Take(1)
                    .SelectMany(n => n.DescendantNodes<SyntaxNode>()
                        .Where(m => m is PitchSyntax or ScaleDegreeSyntax && m.Parent == n))
                    .Select(m => m.SourceStart));
            string? best = null;
            (int At, int Matching, SyntaxTree Tree)? bestScore = null;
            foreach (int at in candidates.Where(currentPitches.ContainsKey))
                foreach (int d in new[] { 1, -1, 2, -2, 3, -3 })
                {
                    var p = currentPitches[at];
                    string tried = Apply(text, [new Edit(p.MarksStart, p.MarksEnd, MarkText(p.Marks + d))]);
                    if (Score(tried) is not { } t)
                        continue;
                    bool better = t.At > s.At || (t.At == s.At && t.Matching > s.Matching);
                    if (better && (bestScore is not { } b || t.At > b.At || (t.At == b.At && t.Matching > b.Matching)))
                        (best, bestScore) = (tried, t);
                }
            if (best == null)
                return null;
            (text, score) = (best, bestScore);
        }
        return score is { At: int.MaxValue } ? text : null;
    }

    // Where each chord and arpeggio starts, in document order: a group's notes sound at its position.
    private static List<int> GroupStarts(SyntaxNode root)
        => root.DescendantNodes<SyntaxNode>().Where(n => n is ChordSyntax or ArpeggioSyntax)
            .Select(n => n.SourceStart).OrderBy(p => p).ToList();

    private static int CountChangedMarks(string before, string after)
    {
        var a = WrittenPitches(SyntaxTree.Parse(before).GetRoot()).Values;
        var b = WrittenPitches(SyntaxTree.Parse(after).GetRoot()).Values;
        return a.Zip(b).Count(p => p.First.Marks != p.Second.Marks);
    }

    // Every written pitch the page draws, by position: its display pitch in each play, every score.
    private static Dictionary<int, List<string>> TraceOf(SyntaxTree tree)
    {
        var trace = new Dictionary<int, List<string>>();
        var specs = RenderSpecParser.FindAll(tree);
        IEnumerable<RenderSpec?> passes = specs.Count == 0 ? [null] : specs;
        foreach (var spec in passes)
        {
            var collector = new MeasureCollector
            {
                ScoreTranspose = spec?.ScoreTranspose,
                ScoreConcert = spec?.ScoreConcert,
                StaffPitch = spec?.StaffPitch,
                RecordsPitchTrace = true,
            };
            SvgGenerator.CollectScore(collector, tree, spec);
            foreach (var e in collector.PitchTrace)
            {
                if (!trace.TryGetValue(e.Position, out var l))
                    trace[e.Position] = l = new List<string>();
                l.Add(e.Pitch);
            }
        }
        return trace;
    }

    // A traced pitch (`C4`, `F#5`, `Bb3`, `Cx4`, `Cbb4`) as semitones, or null.
    private static int? TracedSemitone(string pitch)
    {
        var m = System.Text.RegularExpressions.Regex.Match(pitch, "^([A-G])(bb|b|#|x)?(-?\\d+)$");
        if (!m.Success)
            return null;
        int step = "CDEFGAB".IndexOf(m.Groups[1].Value[0]);
        int alter = m.Groups[2].Value switch { "bb" => -2, "b" => -1, "#" => 1, "x" => 2, _ => 0 };
        return Semantics.RelativeOctave.StepSemitoneOf(step) + alter + 12 * (int.Parse(m.Groups[3].Value) + 1);
    }

    /// <summary>Every note the file plays, as <c>tick:pitch</c> in time order — the comparison
    /// <see cref="MigrateFromLegacy"/> makes against the old rules.</summary>
    public static string SoundSignature(string source)
        => string.Join(" ", new Midi.MidiExporter().Export(SyntaxTree.Parse(source)).Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => $"{n.StartTick}:{n.Pitch}"));

    // The part block a pitch is written in, or null (a phrase body, a part's own header music).
    private static PartBlockSyntax? BlockOf(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PartBlockSyntax block)
                return block;
        return null;
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
    /// <para>
    /// <paramref name="octaveShift"/> (original position → octaves) is for a rewrite that
    /// RESPELLS a pitch across the B/C letter boundary (<see cref="KeyRespeller"/>): the
    /// octave the walk reports is the LETTER's, so <c>b'</c> respelled <c>ces</c> sounds the
    /// same as <c>ces''</c> — its recorded octave plus one.
    /// </para>
    /// </remarks>
    internal static Result Repin(string source, string candidate, IReadOnlyDictionary<int, int>? octaveShift = null)
    {
        int Shift(int orig) => octaveShift != null && octaveShift.TryGetValue(orig, out int s) ? s : 0;
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
                int target = wanted[k] + Shift(orig);
                int marks = target - (octave - candidatePitches[pos].Marks);
                if (newMarks.TryGetValue(pos, out int earlier) && earlier != marks)
                    conflict ??= orig;
                newMarks[pos] = marks;
                return target;
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
            if (!again.TryGetValue(pos, out var got) || !got.SequenceEqual(wanted.Select(o => o + Shift(pos))))
                return Fail($"The rewrite would move the note at {LineCol(source, pos)} — "
                    + "no changes made.");
        return new Result(result, null, changed);
    }

    private static Result Fail(string error) => new(null, error, 0);
    private static (Result, List<Edit>) No(string error) => (Fail(error), []);

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
                    ScoreConcert = spec?.ScoreConcert,
                    StaffPitch = spec?.StaffPitch,
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
    /// <para><paramref name="keepTopLevel"/> (the AI transform's round trip): EVERY top-level
    /// directive stays in place naming the new mode, so the lines can be given their own words
    /// back afterwards.</para>
    /// </summary>
    private static List<Edit> ModeEdits(string source, CompilationUnitSyntax root, OctaveMode target, bool keepTopLevel = false)
    {
        var edits = new List<Edit>();
        var directives = root.DescendantNodes<OctaveDirectiveSyntax>().ToList();
        string directive = target == OctaveMode.Absolute ? "octave absolute" : "octave relative";
        bool stated = false;
        foreach (var d in directives)
        {
            if (keepTopLevel && d.Parent is CompilationUnitSyntax)
            {
                edits.Add(new Edit(d.Span.Start, d.Span.End, directive));
                stated = true;
                continue;
            }
            // The file's first directive stays and names the new mode, in either direction:
            // the line is the writer's (owner's report 2026-10-17 — Down on `octave absolute`
            // made the line vanish, when relative was written by removing it). The switches
            // inside music go, since the whole file is now one mode.
            if (!stated && d.Parent is CompilationUnitSyntax)
            {
                edits.Add(new Edit(d.Span.Start, d.Span.End, directive));
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
        // A file that stated its mode goes on stating it, at the top: absolute always (a bare
        // file is relative, the default); relative when a directive stood somewhere — a file
        // that never wrote one is left as it is.
        if (!stated && (target == OctaveMode.Absolute || directives.Count > 0))
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
            edits.Add(new Edit(at, at, indent + directive + "\n"));
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
