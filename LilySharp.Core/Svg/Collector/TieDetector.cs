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
internal sealed class TieDetector
{
    public ImmutableArray<TieItem> DetectTies(Score score)
    {
        var ties = new List<TieItem>();

        // Each voice runs its own tie engraver; VoiceScan walks them all so a
        // second voice's ties are not lost. LILYPOND-REF: ly/engraver-init.ly.
        foreach (var (v, measures, measureIdx, itemIdx, item) in VoiceScan.WalkVoiceItems(score))
        {
            if (item is NoteItem startNote && startNote.HasTieStart)
            {
                // A tie binds only to the IMMEDIATELY following timed item:
                // the next note of the same pitch, or the matching pitch
                // inside the next chord. A rest or a different pitch there
                // means NO tie — LilyPond reports an unterminated tie and
                // never scans past intervening notes looking for a match.
                // LILYPOND-REF: lily/tie-engraver.cc stop_translation_timestep.
                var next = FindNextTimedItem(measures, measureIdx, itemIdx);
                if (next != null)
                {
                    var (endMeasureIdx, endItemIdx, endItem) = next.Value;
                    NoteItem? note = endItem switch
                    {
                        NoteItem n when n.StaffPosition == startNote.StaffPosition => n,
                        ChordItem c => MatchingChordPitch(c, startNote.StaffPosition),
                        _ => null,
                    };
                    if (note != null)
                    {
                        // Polyphony fixes the tie direction by voice; a single
                        // voice curves opposite the stem.
                        bool curveUp = VoiceScan.SpanCurvesUp(score.Voices.Length, v, !startNote.StemUp);
                        ties.Add(new TieItem(
                            startNote, note,
                            startNote.StaffPosition,
                            curveUp,
                            measureIdx, endMeasureIdx,
                            itemIdx, endItemIdx,
                            voiceIndex: v));
                    }
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
        // Like the single-note path: the ties bind only to the IMMEDIATELY
        // following timed item. A rest there means no ties (LilyPond reports an
        // unterminated tie rather than tying across the rest).
        var next = FindNextTimedItem(measures, measureIdx, itemIdx);
        if (next == null)
            return;
        var (mi, ii, item) = next.Value;

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
        }
        // RestItem: no tie.
    }

    /// <summary>
    /// The immediately following TIMED item (note / chord / rest) after
    /// (<paramref name="measureIdx"/>, <paramref name="itemIdx"/>), or null.
    /// Non-timed items (clef/key/time changes, marks) are transparent — a tie
    /// legitimately crosses those — but notes, chords and rests all occupy the
    /// next musical moment and therefore terminate the search.
    /// </summary>
    private static (int MeasureIdx, int ItemIdx, MusicItem Item)? FindNextTimedItem(
        ImmutableArray<Measure> measures, int measureIdx, int itemIdx)
        => NoteScan.FindNext(measures, measureIdx, itemIdx,
            x => x is NoteItem or ChordItem or RestItem);

    /// <summary>The synthesized end note for a note→chord tie: the chord pitch
    /// matching <paramref name="staffPosition"/>, or null when the chord does
    /// not contain it (then there is no tie).</summary>
    private static NoteItem? MatchingChordPitch(ChordItem chord, int staffPosition)
    {
        foreach (var pitch in chord.Notes)
        {
            if (pitch.StaffPosition == staffPosition)
                return SynthesizeNote(pitch, chord);
        }
        return null;
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