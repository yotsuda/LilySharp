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
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Semantics;

/// <summary>
/// LYS1055: a chord a fretted part's player cannot finger — no way to give each of its notes a
/// string of its own within one hand's reach (owner's request 2026-10-30: a composed rock piece's
/// guitar wrote <c>&lt;g b d f&gt;</c>, G2 B2 D3 F3, which no guitar can hold). Named with a voicing of
/// the same notes that can be held, as a chord diagram reads it (<c>3x3430</c>). A bowed string's
/// part (violin, viola, cello, double bass) sounds two strings at once (owner's rule, the same day):
/// a chord of three or more is warned, and two notes not on neighbouring strings within the hand's
/// reach. The voicings that can be held are asked of <see cref="Editing.ChordVoicingFinder"/>
/// (the composing chat's <c>lilysharp_chordVoicings</c>).
/// </summary>
/// <remarks>
/// <para>
/// A chord is the notes the MIDI export starts at one moment in one part, so a phrase, a repeat or
/// a second voice is heard as the player plays it, at its sounding pitch. Fingerable is
/// <see cref="Music.ChordFingering"/>'s rule, the one a bare <c>@diagram</c> draws by.
/// </para>
/// <para>
/// The voicing offered keeps the lowest note and moves the others by octaves, the fewest moves
/// first. ASCII only: the message reaches legacy-codepage consoles through the CLI.
/// </para>
/// </remarks>
internal sealed class ChordFingeringValidator : ISemanticValidator
{
    private const int Reach = Music.ChordFingering.Reach;

    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var tunings = new Dictionary<string, int[]>(System.StringComparer.Ordinal);
        var bowed = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var part in tree.GetRoot().ChildNodes().OfType<PartDeclarationSyntax>())
        {
            var header = PartHeaderDefaults.Read(part);
            if (header.FrettedTuning is { } tuning)
                tunings.TryAdd(part.Name.Text, Tunings.GetTuning(tuning));
            // A bowed string (owner's rule 2026-10-30): two strings sound at once.
            else if (header.Preset is "violin" or "viola" or "cello" or "contrabass" && tunings.TryAdd(part.Name.Text, Tunings.GetTuning(header.Tuning)))
                bowed.Add(part.Name.Text);
        }
        if (tunings.Count == 0)
            return;

        MidiFile midi;
        try
        {
            midi = new MidiExporter().Export(tree);
        }
        catch
        {
            return; // a book that cannot play surfaces its real error elsewhere
        }

        var said = new HashSet<int>();
        var chords = midi.Tracks.SelectMany(t => t.Notes)
            .Where(n => n.SourcePos >= 0 && n.Part is { } p && tunings.ContainsKey(p))
            .GroupBy(n => (n.Part!, n.StartTick));
        foreach (var chord in chords)
        {
            var pitches = chord.Select(n => n.Pitch).Distinct().OrderBy(p => p).ToArray();
            if (pitches.Length < 2)
                continue;
            int at = chord.Min(n => n.SourcePos);
            if (!said.Add(at))
                continue;
            var strings = tunings[chord.Key.Item1];
            if (bowed.Contains(chord.Key.Item1))
            {
                if (pitches.Length == 2 && DoubleStop(pitches[0], pitches[1], strings))
                    continue;
                // ASCII punctuation only: the CLI prints this to legacy-codepage consoles.
                _diagnostics.Warning(new TextSpan(at, 1), DiagnosticCodes.ChordNotFingerable,
                    $"this chord ({string.Join(" ", pitches.Select(Spell))}) cannot be bowed at once: "
                    + (pitches.Length > 2
                        ? $"a bowed string sounds two strings together, not {pitches.Length} - keep two of its notes (a double stop), or break the chord, the low notes first"
                        : $"its two notes do not lie on neighbouring strings within the hand's reach ({Editing.ChordVoicingFinder.BowedReach} semitones) - move one by an octave, or choose another pair of the chord's notes"));
                continue;
            }
            if (Fingering(pitches, strings) != null)
                continue;
            var instead = Playable(pitches, strings);
            _diagnostics.Warning(new TextSpan(at, 1), DiagnosticCodes.ChordNotFingerable,
                $"this chord ({string.Join(" ", pitches.Select(Spell))}) cannot be fingered: no string for each note within a hand's "
                + $"reach ({Reach} frets) - "
                + (instead is { } v
                    ? $"a voicing of its notes a player can hold: {string.Join(" ", v.Pitches.Select(Spell))} ({Diagram(v.Frets)}), "
                      + "the lowest note kept, the others moved by an octave"
                    : "spread it out, or leave notes out (the root and fifth below, the third above)"));
        }
        StaleDiagrams(tree, midi, tunings, bowed);
    }

    /// <summary>
    /// LYS1057: an <c>@diagram(SHAPE)</c> on a <c>&lt;…&gt;</c> chord of a fretted part whose shape does
    /// not sound the chord's notes (owner's decision 2026-10-31: "Add Guitar Diagram" writes
    /// the shape out, and an edit of the notes afterwards would leave it behind). Every note shifted
    /// alike is the same chord — a capo, a transposing part. The fix named is the chord's own shape,
    /// the one a bare <c>@diagram</c> draws.
    /// </summary>
    private void StaleDiagrams(SyntaxTree tree, MidiFile midi, Dictionary<string, int[]> tunings, HashSet<string> bowed)
    {
        var notes = midi.Tracks.SelectMany(t => t.Notes)
            .Where(n => n.SourcePos >= 0 && n.Part is { } p && tunings.ContainsKey(p) && !bowed.Contains(p))
            .ToList();
        if (notes.Count == 0)
            return;
        foreach (var chord in tree.GetNodes<ChordSyntax>())
        {
            if (chord.IsShapeChord
                || chord.Articulations.OfType<MusicMarkSyntax>().FirstOrDefault(m => m.Name == "diagram") is not { } mark
                || AnnotationValues.DiagramFrets(mark) is not { } frets)
                continue;
            var span = chord.Span;
            var played = notes.Where(n => n.SourcePos >= span.Start && n.SourcePos < span.End)
                .GroupBy(n => (n.Part!, n.StartTick)).FirstOrDefault();
            if (played == null)
                continue;
            var strings = tunings[played.Key.Item1];
            if (frets.Length != strings.Length)
                continue;   // another instrument's shape: LYS1038 says so
            var shape = frets.Select((f, s) => f < 0 ? -1 : strings[s] + f).Where(p => p >= 0).Distinct().OrderBy(p => p).ToArray();
            var pitches = played.Select(n => n.Pitch).Distinct().OrderBy(p => p).ToArray();
            if (shape.Length == pitches.Length && shape.Zip(pitches, (a, b) => b - a).Distinct().Count() == 1)
                continue;
            string own = Fingering(pitches, strings) is { } held && held.Max() <= 15
                ? $" - the chord's own shape: @diagram({Music.ChordVoicings.Spell(held)})"
                : " - and no hand can hold the chord: write the diagram you mean, or a bare @diagram";
            _diagnostics.Warning(mark.Span, DiagnosticCodes.DiagramNotTheChord,
                $"this diagram sounds {string.Join(" ", shape.Select(Spell))}, not the chord's notes "
                + $"({string.Join(" ", pitches.Select(Spell))}){own}");
        }
    }

    /// <summary>Two pitches a bowed string sounds together: on two neighbouring strings, the stopped
    /// ones within the hand's reach (<see cref="Editing.ChordVoicingFinder"/>'s rule).</summary>
    private static bool DoubleStop(int low, int high, int[] strings)
    {
        for (int s = 0; s + 1 < strings.Length; s++)
        {
            int a = low - strings[s], b = high - strings[s + 1];
            if (a >= 0 && b >= 0 && (a == 0 || b == 0 || System.Math.Abs(a - b) <= Editing.ChordVoicingFinder.BowedReach))
                return true;
        }
        return false;
    }

    /// <summary>A fret for each string (index 0 the lowest; -1 unplayed) holding every pitch, or null
    /// (<see cref="Music.ChordFingering.Fingering"/>).</summary>
    internal static int[]? Fingering(IReadOnlyList<int> pitches, int[] strings) => Music.ChordFingering.Fingering(pitches, strings);

    /// <summary>The same notes with the fewest moved by an octave, the lowest kept, that can be held.</summary>
    private static (int[] Pitches, int[] Frets)? Playable(int[] pitches, int[] strings)
    {
        int n = pitches.Length - 1;
        (int[] Pitches, int[] Frets)? best = null;
        int bestMoves = int.MaxValue;
        // Each upper note stays, or goes an octave up or down: 3^n tries (a chord has few notes).
        int tries = (int)System.Math.Pow(3, n);
        for (int code = 0; code < tries; code++)
        {
            var moved = new int[pitches.Length];
            moved[0] = pitches[0];
            int moves = 0, c = code;
            for (int i = 1; i <= n; i++, c /= 3)
            {
                int shift = (c % 3) switch { 1 => 12, 2 => -12, _ => 0 };
                moves += shift == 0 ? 0 : 1;
                moved[i] = pitches[i] + shift;
            }
            if (moves >= bestMoves || moved.Skip(1).Any(p => p <= pitches[0]) || moved.Distinct().Count() < moved.Length)
                continue;
            var sorted = moved.OrderBy(p => p).ToArray();
            if (Fingering(sorted, strings) is { } frets)
                (best, bestMoves) = ((sorted, frets), moves);
        }
        return best;
    }

    /// <summary>A diagram's fret string, lowest string first: <c>3x3430</c>.</summary>
    private static string Diagram(int[] frets)
        => string.Concat(frets.Select(f => f < 0 ? "x" : f < 10 ? f.ToString() : $"({f})"));

    private static string Spell(int midi)
        => new[] { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" }[midi % 12] + (midi / 12 - 1);
}
