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
/// (<see cref="OrAnnotation"/>; Dettato request 17, owner's decisions 2026-11-17): the
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

    private static StepResult? OrRotateStep(string text, SyntaxTree tree, int caret, int dir,
        Func<string, SyntaxTree>? expand, bool includeStretch)
    {
        ChordEntrySyntax? entry = null;
        foreach (var e in tree.GetNodes<ChordEntrySyntax>())
            if (e.SourceStart <= caret && caret <= e.SourceStart + e.Width)
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
        int timbre = 0, program = NoProgram;
        bool strum = false, apart = false;
        try
        {
            string newText = Apply(text, edits);
            var newTree = SyntaxTree.Parse(newText);
            var sounding = expand?.Invoke(newText) ?? newTree;
            if (FindTarget(newTree, TargetKind.Voicing, entry.SourceStart) is { } moved)
            {
                var voice = Sound(moved, sounding, includeStretch);
                (pitches, timbre, program, strum, apart) = ([.. voice.Pitches.Take(MaxAuditionPitches)], voice.Timbre, voice.Program, voice.Strum, voice.Apart);
            }
        }
        catch (Exception)
        {
            // A text the rotation breaks still turns; it only stays silent.
        }
        return new StepResult(false, [.. edits], message, pitches, timbre, program, null, strum, strum && apart);
    }
}
