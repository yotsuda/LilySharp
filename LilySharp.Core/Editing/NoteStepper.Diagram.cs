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
    /// <c>&lt;…&gt;</c> chord, or over a single note it is held by (owner's decisions 2026-10-31;
    /// <c>c@diagram</c> is xxxx1x, Up xxx5xx), with "Add Guitar Diagram": the
    /// other ways a hand holds the SAME notes, the one a player reaches for first
    /// (<see cref="ChordFingering.Fingerings"/>) — the notes never change, only the strings and
    /// the frets. A shape that is none of them (the notes edited since; LYS1057) steps to the
    /// first. Null when the caret is not there: the step goes on as before.
    /// <para>
    /// A bare <c>@diagram</c> already draws the first fingering: Up writes THAT one out (the page does
    /// not change; the shape shows in the source, as "Add Guitar Diagram" writes it), and Down at the
    /// first written one takes the shape away again — back to the bare mark, whose diagram follows
    /// the notes. Down on the bare mark stays. One line: bare ⇄ first ⇄ second ⇄ … (owner's
    /// decisions 2026-10-31; Up wrote the second at first, which made the walk skip a step).
    /// </para>
    /// </summary>
    private static StepResult? DiagramFingeringStep(string text, SyntaxTree tree, int caret, int dir)
    {
        foreach (var mark in tree.GetNodes<MusicMarkSyntax>())
        {
            // The caret anywhere after the '@' — on the word or the shape.
            if (mark.Name != "diagram"
                || mark.Parent is not (ChordSyntax { IsShapeChord: false } or NoteSyntax) || mark.Parent is not { } chord
                || caret <= mark.Span.Start || caret > mark.Span.End)
                continue;
            bool bare = mark.Arguments.IsEmpty;
            int open = bare ? -1 : text.IndexOf('(', mark.Span.Start, mark.Span.Length);
            int close = open < 0 ? -1 : text.IndexOf(')', open);
            if (!bare && (open < 0 || close < 0 || caret > close))
                return null;
            if (ShapeChordConverter.PlayedPitches(tree, chord) is not { } pitches)
                return new StepResult(false, [], "@diagram: this chord is not played in any score", [], 0);
            var strings = ShapeChordConverter.StringsOf(chord);
            // On a single note, a shape that is not where that note is held — a chord's diagram
            // over a melody note — is an illustration: it walks its chord's shapes (the Voicing
            // step), not the note's positions (owner's decision 2026-10-31).
            if (chord is NoteSyntax && !bare
                && !(Semantics.AnnotationValues.DiagramFrets(mark) is { } drawn && drawn.Length == strings.Length
                     && drawn.Select((f, s) => f < 0 ? -1 : strings[s] + f).Where(p => p >= 0).Distinct().Order()
                        .SequenceEqual(pitches)))
                return null;
            var options = ChordFingering.Fingerings(pitches, strings)
                .Where(f => f.Max() <= 15).ToList();
            if (options.Count == 0)
                return new StepResult(false, [], "@diagram: no hand can hold this chord (LYS1055)", [], 0);
            ImmutableArray<int> sound = [.. pitches.Take(MaxAuditionPitches)];
            if (bare)
            {
                // The bare mark shows the first fingering: Up writes it out — the page does not
                // change, the source shows the shape — and Down stays.
                string first = ChordVoicings.Spell(options[0]);
                string firstAt = $"@diagram: fingering 1 of {options.Count} ({first})";
                if (dir < 0)
                    return new StepResult(false, [], firstAt + " - drawn from the notes", sound, GuitarTimbre, GuitarProgram);
                int after = mark.Span.Start + "@diagram".Length;
                return new StepResult(false, [new Edit(after, after, "(" + first + ")")], firstAt, sound,
                    GuitarTimbre, GuitarProgram);
            }
            int at = Semantics.AnnotationValues.DiagramFrets(mark) is { } now
                ? options.FindIndex(o => o.SequenceEqual(now)) : -1;
            // Down at the first written fingering: back to the bare mark, which shows it and
            // follows the notes.
            if (at == 0 && dir < 0)
                return new StepResult(false, [new Edit(open, close + 1, "")],
                    $"@diagram: fingering 1 of {options.Count} - the shape taken out, the diagram follows the notes",
                    sound, GuitarTimbre, GuitarProgram);
            int next = at < 0 ? 0 : Math.Clamp(at + dir, 0, options.Count - 1);
            string shape = ChordVoicings.Spell(options[next]);
            string where = $"@diagram: fingering {next + 1} of {options.Count} ({shape})";
            if (next == at)
                return new StepResult(false, [], where + " - the last", sound, GuitarTimbre, GuitarProgram);
            return new StepResult(false, [new Edit(open + 1, close, shape)], where, sound, GuitarTimbre, GuitarProgram);
        }
        return null;
    }
}
