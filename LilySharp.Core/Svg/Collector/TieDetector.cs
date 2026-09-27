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
        // Lent (see t_ties); the ToImmutableArray below copies, so the list is finished with there.
        var ties = t_ties ?? new List<TieItem>();
        t_ties = null;

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
                        NoteItem n when SamePitch(n.StaffPosition, n.Midi, startNote.StaffPosition, startNote.Midi) => n,
                        ChordItem c => MatchingChordPitch(c, startNote.StaffPosition, startNote.Midi),
                        _ => null,
                    };
                    if (note != null)
                    {
                        // Polyphony fixes the tie direction by voice — that is a grob
                        // property LilyPond really does set (\voiceOne/\voiceTwo,
                        // ly/engraver-init.ly), so it arrives at the placement as a MANUAL
                        // direction. A single voice hands over NOTHING: LilyPond's ordinary
                        // Tie carries the callback ly:tie::calc-direction, not a number, and
                        // the direction falls out of the scored search
                        // (lily/tie-formatting-problem.cc:1004-1023). See TieItem.ForcedCurveUp
                        // for what used to be here and what measured it wrong.
                        bool? forcedCurveUp = VoiceScan.ForcedCurveUpAt(score.Voices, v, measureIdx, itemIdx);
                        ties.Add(new TieItem(
                            startNote, note,
                            startNote.StaffPosition,
                            forcedCurveUp,
                            measureIdx, endMeasureIdx,
                            itemIdx, endItemIdx,
                            voiceIndex: v)
                        {
                            // The `~` the reader can point at. It rides the START item,
                            // folded there by the collect's marker run (MarkerFlags).
                            SourcePosition = startNote.TieStartSourcePosition,
                        });
                    }
                }
            }
            else if (item is ChordItem startChord && startChord.HasTieStart)
            {
                // LILYPOND-REF: lily/tie-column.cc — tie every matching pitch
                // between this chord and the next chord/note.
                DetectChordTies(measures, v, measureIdx, itemIdx, startChord, ties,
                    // Polyphonic where the chord STARTS (VoiceScan.SpanCurvesUp's remarks).
                    multiVoice: VoiceScan.ForcedCurveUpAt(score.Voices, v, measureIdx, itemIdx) is not null);
            }
        }

        var detected = ties.ToImmutableArray();
        ties.Clear();
        t_ties = ties;
        return detected;
    }

    /// <summary>
    /// The list <see cref="DetectTies"/> collects a score's ties into, lent from one the thread
    /// keeps between calls.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 475's census at HEAD, Release, the reader's corpus, eight forward
    /// keystrokes a book): 1.74 builds a keystroke at 18.32 ties (max 142), 820 B a keystroke,
    /// none reachable once the render returned — the answer is the copied array, and
    /// <see cref="DetectChordTies"/>, the one other writer, is handed the list and keeps
    /// nothing. RENTING TAKES IT OUT OF THE DRAWER (session 421's idiom), THE CLEARING IS ON
    /// GIVE (session 456): a list given back dirty would hand the next score this one's ties.
    /// A throw between the rent and the give only costs the next call a new list. WHAT IT
    /// RETAINS is one emptied list a thread at that thread's most-tied score, pinning no item.
    /// </remarks>
    [ThreadStatic]
    private static List<TieItem>? t_ties;

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
                    if (SamePitch(endPitch.StaffPosition, endPitch.Midi, startPitch.StaffPosition, startPitch.Midi))
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
                if (SamePitch(endNoteItem.StaffPosition, endNoteItem.Midi, startPitch.StaffPosition, startPitch.Midi))
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

    /// <summary>
    /// Whether two heads are the SAME PITCH for a tie: the same staff position, or — where the
    /// drawn position moved under them (an ottava bracket begins or ends between the two) —
    /// the same sounding MIDI number a whole number of octaves (7 positions) apart. The
    /// octave test keeps an enharmonic pair (fis~ges: one position apart) untied, as LilyPond
    /// does.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-engraver.cc — the tie binds heads whose PITCHES are equal
    /// (ly:pitch), not their staff positions; the ottava only moves where a head is drawn.
    /// MEASURED, Lab sessions/p655 (S1 tie-ottava, LilyPond 2.26.0): `d''~ \ottava #0 d''`
    /// draws its tie at the first head's height, ending 0.335 short of the second head, which
    /// stands an octave higher; Lily# dropped every tie whose end note carried `@ottava` or
    /// `@!ottava`. ⚠️ A clef change between the two heads moves the position by a non-octave
    /// amount and is still left untied (no corpus book writes one).
    /// </remarks>
    private static bool SamePitch(int positionA, int midiA, int positionB, int midiB)
        => positionA == positionB
           || (midiA != 0 && midiA == midiB && (positionA - positionB) % 7 == 0);

    /// <summary>The synthesized end note for a note→chord tie: the chord pitch
    /// matching <paramref name="staffPosition"/>, or null when the chord does
    /// not contain it (then there is no tie).</summary>
    private static NoteItem? MatchingChordPitch(ChordItem chord, int staffPosition, int midi)
    {
        foreach (var pitch in chord.Notes)
        {
            if (SamePitch(pitch.StaffPosition, pitch.Midi, staffPosition, midi))
                return SynthesizeNote(pitch, chord);
        }
        return null;
    }

    /// <summary>
    /// Emits the chord's ties, bottom to top, imposing a direction on NONE of them unless the
    /// music really carries one.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE STANDARD DIRECTION DISTRIBUTION USED TO BE HERE — bottom tie DOWN, top tie UP,
    /// adjacent seconds split — handed on as an IMPOSED direction. It is not one:
    /// <c>set_ties_config_standard_directions</c> only SEEDS the base configuration, and
    /// <c>generate_collision_variations</c> may then flip what it wrote
    /// (lily/tie-formatting-problem.cc:1025-1084 and :1153-1237). It could not live here while
    /// Lily# solved a column one tie at a time; now that
    /// <see cref="Layout.TieFormattingProblem"/> is handed the whole column, it lives where
    /// LilyPond puts it and this collector is left with the directions the music states.
    /// <para>
    /// What still arrives imposed is \voiceOne/\voiceTwo (ly/engraver-init.ly sets
    /// <c>Tie.direction</c>, a real grob property), which is the polyphony branch below.
    /// </para>
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

        // Polyphony: the voice fixes EVERY tie's direction (upper voice up, lower voice down),
        // so a lower voice's whole chord ties below its notes.
        // LILYPOND-REF: ly/engraver-init.ly \voiceOne/\voiceTwo Tie.direction.
        bool? forcedCurveUp = multiVoice ? voiceIndex % 2 == 0 : null;

        for (int i = 0; i < matched.Count; i++)
        {
            var (startPitch, endNote) = matched[i];
            // Synthesize NoteItem stand-ins for TieItem (the renderer only
            // consumes StaffPosition/CurveUp from these).
            ties.Add(new TieItem(
                SynthesizeNote(startPitch, startChord), endNote,
                startPitch.StaffPosition,
                forcedCurveUp,
                startMeasureIdx, endMeasureIdx,
                startItemIdx, endItemIdx,
                voiceIndex: voiceIndex)
            {
                // ONE `~` after `<c e g>~` writes every pitch's tie, so all of this
                // chord's bows cite the same character — the shape a chord's heads
                // already have with their shared data-pos.
                SourcePosition = startChord.TieStartSourcePosition,
            });
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