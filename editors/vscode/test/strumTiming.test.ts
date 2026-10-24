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
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vm from 'node:vm';

// A tuning's strum (owner's report 2026-10-24: a single note, then a chord): the strings after
// the first were each a setTimeout in the page, which a hidden preview fires about once a
// second, so they came out together. They are timed on the audio clock now (the SoundFont
// synth's queue, the oscillators' start times) and, for the MIDI output, on the host's timers.
describe('a strum keeps its time in a hidden preview', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = source.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
    const body = (name: string, next: string) => {
        const at = html.indexOf(`function ${name}(`);
        return html.slice(at, html.indexOf(`function ${next}(`, at));
    };

    it('strikes every string at once with its gap, on no page timer but the release', () => {
        const playAt = html.indexOf('function playPitches(');
        const playBody = html.slice(playAt, html.indexOf('\n        }\n', playAt));
        assert.match(playBody, /holdNotes\(pitches, timbre \|\| 0, prog, 1e9, pitches\.length > 1 \? \(strumMs \|\| 0\) \/ 1000 : 0\)/);
        assert.equal(playBody.match(/setTimeout\(/g)?.length, 1, 'only the release watchdog');
        assert.equal(html.includes('addHeldNote'), false);
    });

    it('times the strings on the audio clock, or hands the gap to the host for the MIDI output', () => {
        const hold = body('holdNotes', 'auditionNote');
        assert.match(hold, /sf\.strike\(pitches, timbre, program, 100, strum\)/);
        assert.match(hold, /const t0 = now \+ i \* strum;/);
        assert.match(hold, /gapMs: Math\.round\(strum \* 1000\)/);
    });

    it('strums the MIDI output from the host, and a release drops the strings not struck yet', () => {
        const strike = source.slice(source.indexOf("if (message.type === 'midiStrike')"), source.indexOf("if (message.type === 'export')"));
        assert.match(strike, /midiStrumTimers = pitches\.slice\(1\)\.map\(\(p, i\) => setTimeout\(\(\) => void strike\(\[p\]\), \(i \+ 1\) \* message\.gapMs\)\)/);
        const release = strike.slice(strike.indexOf("if (message.type === 'midiRelease')"));
        assert.match(release, /for \(const t of midiStrumTimers\) \{ clearTimeout\(t\); \}/);
    });
});
