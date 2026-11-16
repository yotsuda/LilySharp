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

// The WAV, MP3 and MP4 exports with the recording beside the chart (docs/recording-design.md §7,
// owner's report 2026-11-16: the MP4 carried the score's sound alone while Play sounded the
// recording with it). The timing is the preview's practiced() (extension.ts): the score's notes
// and beats moved bar by bar onto the recording's seconds (timeWarp), the score's written tempo
// setting the recording's pace (warpFit) under the practice tempo, and the list divided by the
// rate the recording then plays at (recordingPlan) — so the recording's second 0 stands at the
// list's 0, after the count-in's lead.
//
// No vscode here: the extension and the tests share it.

import { recordingPlan, timeWarp, warpBeats, warpFit, warpNotes } from './recordingCore';
import { PlaybackBeat, PlaybackNote, PracticeState, RecordingAlong, exportPlan } from './webview/playbackCore';

/** How the recording plays in an export: the tempo its player is handed (the practice tempo times
 *  warpFit — RecordingPlayer.play's `tempo`), its panel's settings, and its fader. */
export interface RecordingPlay {
    tempo: number;
    sound: RecordingAlong['sound'];
    level: number;
}

/**
 * {@link exportPlan} with the recording: when the page sounds a recording with the score and its
 * bars line up with this score's (timeWarp), the notes on the recording's seconds at the
 * recording's rate, and how the recording is to play; otherwise exportPlan as it is and no
 * recording.
 */
export function recordedExportPlan(notes: readonly PlaybackNote[], parts: readonly string[] | null,
    beats: readonly PlaybackBeat[] | null, barEnd: number | null | undefined,
    practice: PracticeState | null | undefined, extra: readonly string[] | null = null,
): { notes: PlaybackNote[]; lead: number; recording: RecordingPlay | null } {
    const rec = practice?.recording;
    const warp = practice && rec && beats ? timeWarp(beats, barEnd ?? null, { bars: rec.bars.map(b => ({ start: b.start, end: b.end })) }) : null;
    if (!practice || !rec || !beats || !warp) {
        return { ...exportPlan(notes, parts, beats, practice, extra), recording: null };
    }
    const tempo = practice.speed * warpFit(warp);
    const speed = recordingPlan(rec.sound, tempo).rate;
    const plan = exportPlan(warpNotes(notes, warp), parts, warpBeats(beats, warp), { ...practice, speed }, extra);
    return { ...plan, recording: { tempo, sound: rec.sound, level: rec.level } };
}
