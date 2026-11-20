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

import { describe, it } from 'node:test';
import * as fs from 'fs';
import * as path from 'path';
import * as assert from 'node:assert/strict';
import { STEMS_VERSION, floatWav, mixProblem, notesByPart, stemFileName } from '../src/stemsCore';
import { decodeWav } from '../src/audioRenderCore';
import { PlaybackNote } from '../src/webview/playbackCore';
import { extensionSource } from './extensionSource';

// docs/stems-api-design.md (owner's decisions 2026-11-20): a .lys's sound a stem at a time, for
// Shosa's mixer — a stem a part, the voices, the recording beside the chart.
const note = (N: number | undefined, T: number, P = 60): PlaybackNote => ({ T, D: 0.5, P, V: 100, S: 0, C: N ?? 9, G: 0, N });

describe('the stems', () => {
    it('take the score\'s notes apart a part at a time, the pedal with its part', () => {
        const notes = [note(0, 0), note(1, 0, 48), note(0, 0.5), note(1, 0.2, -1), note(undefined, 0, 76)];
        const by = notesByPart(notes, ['piano', 'bass', 'melody']);
        assert.deepEqual(by.map(p => p.name), ['piano', 'bass'], 'in the parts\' order; a part with nothing to play (sung only) is left out');
        assert.deepEqual(by[0].notes.map(n => n.T), [0, 0.5]);
        assert.deepEqual(by[1].notes.map(n => n.P), [48, -1], 'the bass\'s pedal with the bass');
        assert.deepEqual(notesByPart([note(0, 0, -1)], ['piano']), [], 'a pedal alone is no part to hear');
        assert.deepEqual(notesByPart(notes, null), []);
    });

    it('keep a part\'s sound under a name its notes and the sound font give', () => {
        const a = [note(0, 0), note(0, 0.5)];
        assert.match(stemFileName(a, 'gm.sf2'), /^[0-9a-f]{40}\.wav$/);
        assert.equal(stemFileName(a, 'gm.sf2'), stemFileName([note(0, 0), note(0, 0.5)], 'gm.sf2'));
        assert.notEqual(stemFileName(a, 'gm.sf2'), stemFileName([note(0, 0), note(0, 0.75)], 'gm.sf2'), 'a note moved: another sound');
        assert.notEqual(stemFileName(a, 'gm.sf2'), stemFileName(a, 'other.sf2'), 'another sound font: another sound');
    });

    it('are float WAVs: nothing clipped, nothing scaled', () => {
        const left = Float32Array.from([0, 0.5, 1.5, -2]), right = Float32Array.from([0.25, -0.25, 0, 1]);
        const wav = decodeWav(floatWav(left, right, 44100));
        assert.equal(wav.sampleRate, 44100);
        // (decodeWav gives one channel, the two averaged)
        assert.deepEqual(Array.from(wav.samples), [0.125, 0.125, 0.75, -0.5]);
    });

    it('say what is wrong with a mix handed back to be written', () => {
        const two = (n: number) => ({ left: new Float32Array(n), right: new Float32Array(n), sampleRate: 48000 });
        assert.equal(mixProblem(two(4), 'mp3'), null);
        assert.equal(mixProblem(two(4), 'wav'), null);
        assert.match(mixProblem({ ...two(4), right: new Float32Array(3) }, 'wav')!, /as long/);
        assert.match(mixProblem({ ...two(4), sampleRate: 22050 }, 'mp3')!, /32000, 44100, 48000/);
        assert.equal(mixProblem({ ...two(4), sampleRate: 22050 }, 'wav'), null);
        assert.match(mixProblem(two(4), 'flac')!, /mp3.*wav/);
        assert.match(mixProblem({ left: [0], right: [0], sampleRate: 48000 }, 'wav')!, /Float32Array/);
    });

    it('are on the extension\'s exports, with their version', () => {
        assert.equal(STEMS_VERSION, 1);
        const ext = extensionSource() + fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'stems.ts'), 'utf8');
        assert.match(ext, /\.\.\.stemsApi\(\{/);
        assert.match(ext, /stemsVersion: STEMS_VERSION,\s*renderStems:[\s\S]{0,200}onDidChangeStems: changed\.event,\s*writeAudio:/);
        // On the recording's seconds as the export with the recording is, and a stem's start where
        // singJobs moved the file for a first consonant.
        assert.match(ext, /const \[sung\] = await singJobs\(practisedJobs\(\[job\], practice\), source, practice\);/);
        assert.match(ext, /made\(\{ kind: 'part', name: part\.name, file, at: -lead \}\);/);
        assert.match(ext, /made\(\{ kind: 'voice', name: v\.name \?\? '', file: v\.wav, at: v\.at - lead \}\);/);
    });
});
