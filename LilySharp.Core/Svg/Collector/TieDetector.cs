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
        var measures = score.Voice.Measures;

        for (int measureIdx = 0; measureIdx < measures.Length; measureIdx++)
        {
            var measure = measures[measureIdx];

            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                var item = measure.Items[itemIdx];

                if (item is NoteItem startNote && startNote.HasTieStart)
                {
                    var endNote = FindNextSamePitchNote(score, measureIdx, itemIdx, startNote);
                    if (endNote != null)
                    {
                        var (endMeasureIdx, endItemIdx, note) = endNote.Value;
                        bool curveUp = !startNote.StemUp;
                        ties.Add(new TieItem(
                            startNote, note,
                            startNote.StaffPosition,
                            curveUp,
                            measureIdx, endMeasureIdx,
                            itemIdx, endItemIdx));
                    }
                }
                else if (item is ChordItem startChord && startChord.HasTieStart)
                {
                    // LILYPOND-REF: lily/tie-column.cc — tie every matching pitch
                    // between this chord and the next chord/note.
                    DetectChordTies(score, measureIdx, itemIdx, startChord, ties);
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
        Score score, int measureIdx, int itemIdx,
        ChordItem startChord,
        List<TieItem> ties)
    {
        // Find the next ChordItem or NoteItem.
        var measures = score.Voice.Measures;
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
                    foreach (var startPitch in startChord.Notes)
                    {
                        bool found = false;
                        foreach (var endPitch in endChord.Notes)
                        {
                            if (endPitch.StaffPosition == startPitch.StaffPosition)
                            {
                                found = true;
                                bool curveUp = !startChord.StemUp;
                                // Synthesize NoteItem stand-ins for TieItem (the renderer
                                // only consumes StaffPosition/CurveUp from these).
                                var startNote = SynthesizeNote(startPitch, startChord);
                                var endNote = SynthesizeNote(endPitch, endChord);
                                ties.Add(new TieItem(
                                    startNote, endNote,
                                    startPitch.StaffPosition,
                                    curveUp,
                                    measureIdx, mi,
                                    itemIdx, ii));
                                break;
                            }
                        }
                        // If no match, the tie is silently dropped (LP behaviour for chord
                        // ties is to require matching pitches; mismatched ones are skipped).
                        _ = found;
                    }
                    return;
                }
                else if (item is NoteItem endNoteItem)
                {
                    // chord ~ note: tie any pitch that matches the next note.
                    foreach (var startPitch in startChord.Notes)
                    {
                        if (endNoteItem.StaffPosition == startPitch.StaffPosition)
                        {
                            bool curveUp = !startChord.StemUp;
                            var startNote = SynthesizeNote(startPitch, startChord);
                            ties.Add(new TieItem(
                                startNote, endNoteItem,
                                startPitch.StaffPosition,
                                curveUp,
                                measureIdx, mi,
                                itemIdx, ii));
                        }
                    }
                    return;
                }
            }
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

    private (int measureIdx, int itemIdx, NoteItem note)? FindNextSamePitchNote(
        Score score,
        int startMeasureIdx,
        int startItemIdx,
        NoteItem startNote)
    {
        var measures = score.Voice.Measures;

        // Search in current measure first
        var currentMeasure = measures[startMeasureIdx];
        for (int i = startItemIdx + 1; i < currentMeasure.Items.Length; i++)
        {
            if (currentMeasure.Items[i] is NoteItem candidate &&
                candidate.StaffPosition == startNote.StaffPosition)
            {
                return (startMeasureIdx, i, candidate);
            }
        }

        // Search in subsequent measures
        for (int m = startMeasureIdx + 1; m < measures.Length; m++)
        {
            var measure = measures[m];
            for (int i = 0; i < measure.Items.Length; i++)
            {
                if (measure.Items[i] is NoteItem candidate &&
                    candidate.StaffPosition == startNote.StaffPosition)
                {
                    return (m, i, candidate);
                }
            }
        }

        return null;
    }
}