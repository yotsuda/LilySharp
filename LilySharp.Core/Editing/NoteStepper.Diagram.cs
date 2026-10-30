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

using System.Collections.Immutable;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

public static partial class NoteStepper
{
    /// <summary>
    /// Ctrl+Shift+Up/Down with the caret on the shape of an <c>@diagram(SHAPE)</c> over a
    /// <c>&lt;…&gt;</c> chord (owner's decision 2026-10-31, with "Add Guitar Diagram"): the
    /// other ways a hand holds the SAME notes, the one a player reaches for first
    /// (<see cref="ChordFingering.Fingerings"/>) — the notes never change, only the strings and
    /// the frets. A shape that is none of them (the notes edited since; LYS1057) steps to the
    /// first. Null when the caret is not there: the step goes on as before.
    /// </summary>
    private static StepResult? DiagramFingeringStep(string text, SyntaxTree tree, int caret, int dir)
    {
        foreach (var mark in tree.GetNodes<MusicMarkSyntax>())
        {
            if (mark.Name != "diagram" || mark.Arguments.IsEmpty
                || mark.Parent is not ChordSyntax { IsShapeChord: false } chord
                || caret < mark.Span.Start || caret > mark.Span.End)
                continue;
            int open = text.IndexOf('(', mark.Span.Start, mark.Span.Length);
            int close = open < 0 ? -1 : text.IndexOf(')', open);
            if (open < 0 || close < 0 || caret <= open || caret > close)
                return null;
            if (ShapeChordConverter.PlayedPitches(tree, chord) is not { } pitches)
                return new StepResult(false, [], "@diagram: this chord is not played in any score", [], 0);
            var options = ChordFingering.Fingerings(pitches, ShapeChordConverter.StringsOf(chord))
                .Where(f => f.Max() <= 15).ToList();
            if (options.Count == 0)
                return new StepResult(false, [], "@diagram: no hand can hold this chord (LYS1055)", [], 0);
            int at = Semantics.AnnotationValues.DiagramFrets(mark) is { } now
                ? options.FindIndex(o => o.SequenceEqual(now)) : -1;
            int next = at < 0 ? 0 : Math.Clamp(at + dir, 0, options.Count - 1);
            string shape = ChordVoicings.Spell(options[next]);
            string where = $"@diagram: fingering {next + 1} of {options.Count} ({shape})";
            ImmutableArray<int> sound = [.. pitches.Take(MaxAuditionPitches)];
            if (next == at)
                return new StepResult(false, [], where + (dir > 0 ? " - the last" : " - the first"), sound,
                    GuitarTimbre, GuitarProgram);
            return new StepResult(false, [new Edit(open + 1, close, shape)], where, sound, GuitarTimbre, GuitarProgram);
        }
        return null;
    }
}
