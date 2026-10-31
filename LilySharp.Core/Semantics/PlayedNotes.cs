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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The notes a file plays, each with its part, the moment it starts and where it is written —
/// what the chord checks (LYS1055, LYS1057) and the chord rewrites' "sounds the same" read.
/// </summary>
/// <remarks>
/// The MIDI export's notes, when it plays any. A file whose scores each name their own form plays
/// nothing as MIDI (owner's report 2026-10-31), and is read off the collected page instead — every
/// score, each note at its bar and its onset in the bar. Those moments are not the MIDI's ticks:
/// they order and group notes, and are never mixed with a MIDI moment.
/// </remarks>
internal static class PlayedNotes
{
    /// <summary>One played note: its part, the moment it starts (a key only for grouping and
    /// ordering), its sounding pitch and the source position it was written at.</summary>
    public readonly record struct Note(string? Part, (int Score, long Tick, Fraction Onset) At, int Pitch, int SourcePos);

    public static List<Note> Of(SyntaxTree tree)
    {
        var notes = new List<Note>();
        try
        {
            foreach (var n in new Midi.MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes))
                notes.Add(new Note(n.Part, (0, n.StartTick, Fraction.Zero), n.Pitch, n.SourcePos));
        }
        catch
        {
            // A book that cannot play surfaces its real error elsewhere.
        }
        return notes.Count > 0 ? notes : OfThePage(tree);
    }

    /// <summary>The notes the collected page draws, every score that renders.</summary>
    private static List<Note> OfThePage(SyntaxTree tree)
    {
        var notes = new List<Note>();
        var specs = Svg.Collector.RenderSpecParser.FindAll(tree);
        IEnumerable<Svg.Collector.RenderSpec?> passes = specs.Count == 0 ? [null] : specs;
        int scoreIndex = 0;
        foreach (var spec in passes)
        {
            scoreIndex++;
            Svg.Model.MultiStaffScore score;
            try { score = Svg.SvgGenerator.CollectScore(tree, spec); }
            catch { continue; }
            foreach (var (_, staff, _) in score.EnumerateStaves())
                foreach (var voice in staff.Voices)
                    for (int m = 0; m < voice.Measures.Length; m++)
                    {
                        var onset = Fraction.Zero;
                        foreach (var item in voice.Measures[m].Items)
                        {
                            var at = (scoreIndex, (long)m, onset);
                            if (item is Svg.Model.ChordItem chord && !chord.GraceTime)
                                foreach (var member in chord.Notes)
                                    notes.Add(new Note(voice.Name, at, member.Midi, chord.SourcePosition));
                            else if (item is Svg.Model.NoteItem note && !note.GraceTime && note.Midi > 0)
                                notes.Add(new Note(voice.Name, at, note.Midi, note.SourcePosition));
                            onset += item.Duration;
                        }
                    }
        }
        return notes;
    }

    /// <summary>What the file plays, as one string — equal for two files that play the same notes
    /// at the same moments (<see cref="Of"/>).</summary>
    public static string Signature(SyntaxTree tree)
        => string.Join(" ", Of(tree)
            .OrderBy(n => n.At.Score).ThenBy(n => n.At.Tick).ThenBy(n => n.At.Onset.ToDouble()).ThenBy(n => n.Pitch)
            .Select(n => $"{n.Part}:{n.At.Score}.{n.At.Tick}.{n.At.Onset}:{n.Pitch}"));
}
