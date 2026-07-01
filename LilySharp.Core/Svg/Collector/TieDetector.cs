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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects ties between notes of the same pitch.
/// </summary>
public sealed class TieDetector
{
    public ImmutableArray<TieItem> DetectTies(Score score)
    {
        var ties = new List<TieItem>();

        // Each voice runs its own tie engraver; scan them all so a second voice's
        // ties are not lost. A single-voice score iterates once with voiceIndex 0
        // (byte-identical). LILYPOND-REF: ly/engraver-init.ly — Tie_engraver per Voice.
        for (int v = 0; v < score.Voices.Length; v++)
        {
            var measures = score.Voices[v].Measures;

            for (int measureIdx = 0; measureIdx < measures.Length; measureIdx++)
            {
                var measure = measures[measureIdx];

                for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
                {
                    var item = measure.Items[itemIdx];

                    if (item is NoteItem startNote && startNote.HasTieStart)
                    {
                        // A tie connects to the next note of the SAME pitch.
                        var endNote = NoteScan.FindNextNote(measures, measureIdx, itemIdx,
                            c => c.StaffPosition == startNote.StaffPosition);
                        if (endNote != null)
                        {
                            var (endMeasureIdx, endItemIdx, note) = endNote.Value;
                            // Polyphony fixes the tie direction by voice (upper UP,
                            // lower DOWN); a single voice curves opposite the stem.
                            // LILYPOND-REF: ly/engraver-init.ly \voiceOne/\voiceTwo
                            //   set Tie.direction = UP / DOWN.
                            bool curveUp = score.Voices.Length > 1
                                ? (v % 2 == 0)
                                : !startNote.StemUp;
                            ties.Add(new TieItem(
                                startNote, note,
                                startNote.StaffPosition,
                                curveUp,
                                measureIdx, endMeasureIdx,
                                itemIdx, endItemIdx,
                                voiceIndex: v));
                        }
                    }
                    else if (item is ChordItem startChord && startChord.HasTieStart)
                    {
                        // LILYPOND-REF: lily/tie-column.cc — tie every matching pitch
                        // between this chord and the next chord/note.
                        DetectChordTies(measures, v, measureIdx, itemIdx, startChord, ties,
                            multiVoice: score.Voices.Length > 1);
                    }
                }
            }
        }

        return ties.ToImmutableArray();
    }

    /// <summary>
    /// Emits one <see cref="TieItem"/> per pitch in <paramref name="startChord"/>
    /// that has a matching pitch in the next note/chord.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-column.cc — TieColumn for chord ties.
    /// </remarks>
    private static void DetectChordTies(
        ImmutableArray<Measure> measures, int voiceIndex, int measureIdx, int itemIdx,
        ChordItem startChord,
        List<TieItem> ties,
        bool multiVoice)
    {
        // Find the next ChordItem or NoteItem.
        for (int mi = measureIdx; mi < measures.Length; mi++)
        {
            var measure = measures[mi];
            int startII = (mi == measureIdx) ? itemIdx + 1 : 0;
            for (int ii = startII; ii < measure.Items.Length; ii++)
            {
                var item = measure.Items[ii];
                if (item is ChordItem endChord)
                {
                    // For each pitch in startChord, find a matching pitch in endChord.
                    var matched = new List<(ChordNoteInfo Start, NoteItem End)>();
                    foreach (var startPitch in startChord.Notes)
                    {
                        foreach (var endPitch in endChord.Notes)
                        {
                            if (endPitch.StaffPosition == startPitch.StaffPosition)
                            {
                                matched.Add((startPitch, SynthesizeNote(endPitch, endChord)));
                                break;
                            }
                        }
                        // Unmatched pitches are silently dropped (LP behaviour for
                        // chord ties is to require matching pitches).
                    }
                    EmitChordTies(matched, startChord, ties, measureIdx, mi, itemIdx, ii, voiceIndex, multiVoice);
                    return;
                }
                else if (item is NoteItem endNoteItem)
                {
                    // chord ~ note: tie any pitch that matches the next note.
                    var matched = new List<(ChordNoteInfo Start, NoteItem End)>();
                    foreach (var startPitch in startChord.Notes)
                    {
                        if (endNoteItem.StaffPosition == startPitch.StaffPosition)
                            matched.Add((startPitch, endNoteItem));
                    }
                    EmitChordTies(matched, startChord, ties, measureIdx, mi, itemIdx, ii, voiceIndex, multiVoice);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Emits the chord's ties with LilyPond's standard direction assignment:
    /// the bottom tie curves DOWN, the top tie UP, adjacent seconds split
    /// (lower DOWN / upper UP), and remaining inner ties follow the sign of
    /// their staff position (middle line → DOWN). A single matched tie keeps
    /// the stem-opposite default.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc
    /// set_ties_config_standard_directions.
    /// </remarks>
    private static void EmitChordTies(
        List<(ChordNoteInfo Start, NoteItem End)> matched,
        ChordItem startChord,
        List<TieItem> ties,
        int startMeasureIdx, int endMeasureIdx,
        int startItemIdx, int endItemIdx,
        int voiceIndex, bool multiVoice)
    {
        if (matched.Count == 0)
            return;

        // Sort bottom → top like LilyPond's tie configs.
        matched.Sort((a, b) => a.Start.StaffPosition.CompareTo(b.Start.StaffPosition));

        var dirs = new bool?[matched.Count]; // true = curve up
        if (multiVoice)
        {
            // Polyphony: the voice fixes EVERY tie's direction (upper voice up,
            // lower voice down), overriding the single-voice bottom-DOWN/top-UP
            // distribution so a lower voice's whole chord ties below its notes.
            // LILYPOND-REF: ly/engraver-init.ly \voiceOne/\voiceTwo Tie.direction.
            bool voiceUp = voiceIndex % 2 == 0;
            for (int i = 0; i < matched.Count; i++)
                dirs[i] = voiceUp;
        }
        else if (matched.Count == 1)
        {
            dirs[0] = !startChord.StemUp;
        }
        else
        {
            dirs[0] = false;            // front: DOWN
            dirs[^1] = true;            // back: UP

            // Seconds: adjacent ties within one staff position split outward.
            for (int i = 1; i < matched.Count; i++)
            {
                if (Math.Abs(matched[i].Start.StaffPosition
                             - matched[i - 1].Start.StaffPosition) <= 1)
                {
                    dirs[i - 1] ??= false;
                    dirs[i] ??= true;
                }
            }

            // Remaining inner ties: sign of the position (0 → DOWN).
            for (int i = 0; i < matched.Count; i++)
                dirs[i] ??= matched[i].Start.StaffPosition > 0;
        }

        for (int i = 0; i < matched.Count; i++)
        {
            var (startPitch, endNote) = matched[i];
            // Synthesize NoteItem stand-ins for TieItem (the renderer only
            // consumes StaffPosition/CurveUp from these).
            ties.Add(new TieItem(
                SynthesizeNote(startPitch, startChord), endNote,
                startPitch.StaffPosition,
                dirs[i]!.Value,
                startMeasureIdx, endMeasureIdx,
                startItemIdx, endItemIdx,
                voiceIndex: voiceIndex));
        }
    }

    private static NoteItem SynthesizeNote(ChordNoteInfo info, ChordItem chord)
    {
        return new NoteItem(
            staffPosition: info.StaffPosition,
            baseDuration: chord.BaseDuration,
            dots: chord.Dots,
            accidental: info.Accidental,
            needsLedgerLines: info.NeedsLedgerLines,
            sourcePosition: chord.SourcePosition);
    }

}