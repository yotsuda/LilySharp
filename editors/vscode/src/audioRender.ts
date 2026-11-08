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

// Writes the WAVs and MP3s the server named (lilysharp/export with playbackOnly) through the
// preview's synth: spessasynth_core — the engine under spessasynth_lib, which the preview plays
// through — and the preview's sound font, a .sf2, a .sf3 or a DLS bank alike. An MP3 is that
// sound encoded by lamejs (@breezystack/lamejs, LGPL-3.0, unmodified; THIRD-PARTY-NOTICES.md).
// The scheduling, the level and the MP3 framing are audioRenderCore.ts.

import * as fs from 'fs';
import * as path from 'path';
import { SoundBankLoader, SpessaSynthProcessor, audioToWav } from 'spessasynth_core';
import { Mp3Encoder } from '@breezystack/lamejs';
import type { PlaybackBeat, PlaybackNote } from './webview/playbackCore';
import {
    MP3_KBPS, OfflineSynth, VoiceTrack, WAV_SAMPLE_RATE, decodeWav, encodeMp3, fileLevel, mixIn, renderNotes,
} from './audioRenderCore';

/** One file to write, as the server gives it (PlaybackExport): where it goes and the notes it
 *  sounds, with what the practice menu needs (the parts, the beats) and its score's name. */
export interface AudioJob {
    OutputPath: string;
    Notes: PlaybackNote[];
    Parts?: string[] | null;
    /** The parts the score does not sound: played only when the menu turned them on. */
    Extra?: string[] | null;
    /** The parts the score sings and plays (PlaybackResponse.Sung / Played). */
    Sung?: string[] | null;
    Played?: string[] | null;
    Beats?: PlaybackBeat[] | null;
    Score?: string | null;
    /** Where the music's time 0 stands in the file: a count-in's lead, else 0 (exportPlan). */
    Lead?: number;
    /** The sung voices mixed into the file (docs/singing-design.md §5), rendered by the singing
     *  engine for the practice speed; a sung-only part's notes are already out of `Notes`. */
    Voices?: VoiceTrack[] | null;
    /** The notes a video lights: the whole list, where `Notes` lost a sung part's to its voice. */
    Lit?: PlaybackNote[] | null;
}

/** The sound formats the editor renders itself. */
export type AudioFormat = 'wav' | 'mp3';

/** Lets the extension host take other work between stretches of a long render. */
const breathe = () => new Promise<void>(resolve => setImmediate(resolve));

/**
 * Renders every job through the sound font at `bankPath` and writes it as `format`. The bank is
 * read once; each job gets a synth of its own, so one score's programs and pedal do not carry
 * into the next. `progress` is told each file as it begins.
 */
export async function renderAudioFiles(jobs: readonly AudioJob[], bankPath: string, format: AudioFormat,
    progress?: (file: string, index: number) => void): Promise<void> {
    const bytes = fs.readFileSync(bankPath);
    const buffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
    for (let i = 0; i < jobs.length; i++) {
        const job = jobs[i];
        progress?.(job.OutputPath, i);
        // A bank parsed per synth: addSoundBank keeps the object, and a processor torn down
        // must not take a bank another one still plays.
        const bank = SoundBankLoader.fromArrayBuffer(buffer);
        const synth = new SpessaSynthProcessor(WAV_SAMPLE_RATE, { eventsEnabled: false });
        synth.soundBankManager.addSoundBank(bank, 'lys');
        await synth.processorInitialized;
        // The synth's defaults are the preview's (spessasynth_lib's worklet sets none of its
        // own: the same voice cap, effects on), so nothing is set here.
        let [left, right] = await renderNotes(synth as unknown as OfflineSynth, job.Notes, WAV_SAMPLE_RATE, breathe);
        synth.destroySynthProcessor();
        // The sung voices, in the instruments' place, from the music's zero (after a count-in).
        for (const voice of job.Voices ?? []) {
            [left, right] = mixIn(left, right, decodeWav(fs.readFileSync(voice.wav)), voice.at, WAV_SAMPLE_RATE);
            await breathe();
        }
        fileLevel(left, right);
        const data = format === 'mp3'
            ? await encodeMp3(new Mp3Encoder(2, WAV_SAMPLE_RATE, MP3_KBPS), left, right, breathe)
            : new Uint8Array(audioToWav([left, right], WAV_SAMPLE_RATE, { normalizeAudio: false }));
        fs.mkdirSync(path.dirname(job.OutputPath), { recursive: true });
        fs.writeFileSync(job.OutputPath, data);
    }
}
