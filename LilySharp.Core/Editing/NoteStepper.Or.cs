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
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The semitone pair (Ctrl+Shift+Up / Down) on a chords-row symbol that carries an <c>@or</c>
/// (<see cref="OrAnnotation"/>; Orecchiante request 17, owner's decisions 2026-11-17): the
/// candidates ROTATE through the symbol's place — Up <c>F@or(Bm Dm)</c> → <c>Bm@or(Dm F)</c>,
/// Down → <c>Dm@or(F Bm)</c> — and the chord that came forward sounds alone, so the ear compares
/// one chord with the next in a moment. On such a symbol it comes before the shape step the
/// pair otherwise takes there (owner: "the key has always done what its context asks").
/// </summary>
/// <remarks>A shape written on the symbol (<c>F(133211)@or(Bm)</c>) was that chord's and goes
/// with it, as the octave pair's walk drops it (<see cref="DiatonicChordEdit(ChordEntrySyntax, int)"/>);
/// a <c>@todo</c> beside the <c>@or</c> stays where it is.</remarks>
public static partial class NoteStepper
{
    /// <summary>
    /// The decisions an <c>@or</c> at <paramref name="offset"/> (anywhere on its mark) offers — the
    /// quick fixes that settle the chord (owner's decision 2026-11-17): keep the symbol written, or
    /// take a candidate in its place; either way the <c>@or</c> goes (a <c>@todo</c> beside it stays).
    /// Empty when there is no readable <c>@or</c> there.
    /// </summary>
    public static IReadOnlyList<(string Choice, bool Kept, ImmutableArray<Edit> Edits)> OrChoices(SyntaxTree tree, int offset)
    {
        foreach (var entry in tree.GetNodes<ChordEntrySyntax>())
            foreach (var child in entry.ChildNodes())
            {
                if (child is not MusicMarkSyntax mark || OrAnnotation.Of(mark) is not { Problem: null } or
                    || offset < mark.Span.Start || offset > mark.Span.Start + mark.Span.Length)
                    continue;
                var remove = new Edit(mark.Span.Start, mark.Span.Start + mark.Span.Length, "");
                var result = new List<(string, bool, ImmutableArray<Edit>)> { (entry.SymbolText, true, [remove]) };
                foreach (var candidate in or.Candidates)
                    result.Add((candidate, false, [new Edit(entry.SourceStart, EntryEnd(entry), candidate), remove]));
                return result;
            }
        return [];
    }

    /// <summary>Every <c>@or</c> mark of the tree, '@' to ')', in source order — what Remove All @or
    /// deletes (owner's request 2026-11-17), keeping each chord as written. A miswritten one too:
    /// it is no less an @or to be rid of.</summary>
    public static IReadOnlyList<(int Start, int End)> OrMarks(SyntaxTree tree)
        => [.. tree.GetRoot().DescendantNodes()
            .Where(n => n is MusicMarkSyntax or ArticulationSyntax && OrAnnotation.Of(n) != null)
            .Select(n => (n.Span.Start, n.Span.Start + n.Span.Length))
            .OrderBy(s => s.Item1)];

    private static StepResult? OrRotateStep(string text, SyntaxTree tree, int caret, int dir,
        Func<string, SyntaxTree>? expand, bool includeStretch)
    {
        // The entry the caret is on, by its OWN text (Span, '@or(…)' included) — not its Width, which
        // carries its trivia: a comment line before a row's first entry ran its reach over the line,
        // and that entry, with no @or, was found before the one under the caret (owner's report
        // 2026-11-17, Orecchiante's "03 Open Arms.lys": G/B@or(Bm) after such a line did not turn).
        ChordEntrySyntax? entry = null;
        foreach (var e in tree.GetNodes<ChordEntrySyntax>())
            if (e.Span.Start <= caret && caret <= e.Span.Start + e.Span.Length)
            {
                entry = e;
                break;
            }
        if (entry == null)
            return null;
        MusicMarkSyntax? mark = null;
        OrAnnotation? or = null;
        foreach (var child in entry.ChildNodes())
            if (child is MusicMarkSyntax m && OrAnnotation.Of(m) is { Problem: null } read)
            {
                (mark, or) = (m, read);
                break;
            }
        if (mark == null || or == null)
            return null;

        var ring = new List<string> { entry.SymbolText };
        ring.AddRange(or.Candidates);
        // Up brings the first candidate forward and sends the symbol to the back; Down the last.
        var turned = dir > 0 ? [.. ring.Skip(1), ring[0]] : new List<string> { ring[^1] }.Concat(ring.Take(ring.Count - 1)).ToList();
        var edits = new List<Edit>
        {
            new(entry.SourceStart, EntryEnd(entry), turned[0]),
            new(mark.Span.Start, mark.Span.Start + mark.Span.Length, "@or(" + string.Join(" ", turned.Skip(1)) + ")"),
        };
        int index = dir > 0 ? 1 : ring.Count - 1;
        string message = $"{turned[0]} ({index + 1} of {ring.Count}: {string.Join(" ", ring)})";

        ImmutableArray<int> pitches = [];
        int timbre = 0, program = NoProgram, velocity = -1;
        string? part = null;
        bool strum = false, apart = false;
        try
        {
            string newText = Apply(text, edits);
            var newTree = SyntaxTree.Parse(newText);
            var sounding = expand?.Invoke(newText) ?? newTree;
            // As the row PLAYS it (owner's report 2026-11-17: the rotation sounded a guitar, the row's
            // playback a piano, and louder): the chord's notes off the row's own track — its program,
            // timbre and velocity, and the part whose fader the preview puts on it.
            var row = RowSound(sounding, entry.SourceStart);
            if (row == null && !ReferenceEquals(sounding, newTree))
                row = RowSound(newTree, entry.SourceStart);
            if (row is { } played)
                (pitches, timbre, program, velocity, part) = played;
            else if (FindTarget(newTree, TargetKind.Voicing, entry.SourceStart) is { } moved)
            {
                var voice = Sound(moved, sounding, includeStretch);
                (pitches, timbre, program, strum, apart) = ([.. voice.Pitches.Take(MaxAuditionPitches)], voice.Timbre, voice.Program, voice.Strum, voice.Apart);
            }
        }
        catch (Exception)
        {
            // A text the rotation breaks still turns; it only stays silent.
        }
        return new StepResult(false, [.. edits], message, pitches, timbre, program, null, strum, strum && apart, velocity, part);
    }

    /// <summary>The chord a chords-row entry at <paramref name="at"/> plays in the piece — its first
    /// strike's notes on the row's track (MidiExporter.PlayChordRow) — or null when the row does not
    /// sound (no score places it) or the entry is not reached.</summary>
    private static (ImmutableArray<int> Pitches, int Timbre, int Program, int Velocity, string? Part)? RowSound(SyntaxTree tree, int at)
    {
        var midi = new Midi.MidiExporter().Export(tree);
        foreach (var track in midi.Tracks)
        {
            var notes = track.Notes.Where(n => n.SourcePos == at).ToList();
            if (notes.Count == 0)
                continue;
            int first = notes.Min(n => n.StartTick);
            var chord = notes.Where(n => n.StartTick == first).ToList();
            return ([.. chord.Select(n => n.Pitch).Distinct().OrderBy(p => p).Take(MaxAuditionPitches)],
                chord[0].Timbre, track.Program ?? NoProgram, chord.Max(n => n.Velocity), chord[0].Part);
        }
        return null;
    }
}
