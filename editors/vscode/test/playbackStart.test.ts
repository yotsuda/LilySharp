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
import { SOUNDING_IDLE_MS, soundingOf, soundingWait, startSecondsAt } from '../src/playbackStart';
import * as fs from 'node:fs';
import * as path from 'node:path';

// The header's notes are an export of the whole score: asked once the text has rested, and for the
// score's own notes alone (review of session 1291).
describe('when the sounding notes are asked for', () => {
    it('asks at once of a text never edited or one that has rested', () => {
        assert.equal(soundingWait(undefined, 5000), 0);
        assert.equal(soundingWait(1000, 1000 + SOUNDING_IDLE_MS), 0);
        assert.equal(soundingWait(1000, 9000), 0);
    });

    it('waits out the rest of the idle time after an edit', () => {
        assert.equal(soundingWait(1000, 1000), SOUNDING_IDLE_MS);
        assert.equal(soundingWait(1000, 1100), SOUNDING_IDLE_MS - 100);
    });

    it('is what the preview does: waits, asks for the sounding notes only, and sends the render first', () => {
        const extension = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(extension, /soundingWait\(lastEditAt\.get\(uri\), Date\.now\(\)\)/);
        assert.match(extension, /'lilysharp\/playback', \{ textDocument: \{ uri \}, renderName, soundingOnly: true \}/);
        // The @todo marks are asked for after the render request has left.
        const svg = extension.indexOf("client.sendRequest<SvgResponse>('lilysharp/svg'");
        const todos = extension.indexOf('void todoMarksOf(document).then(');
        assert.ok(svg > 0 && todos > svg);
    });
});

// The header's position while nothing plays (owner's request 2026-11-16): where Play would start.
describe('where Play would start, in the score\'s seconds', () => {
    // A bar written at 10..30 played twice (a repeat), then a bar at 40..50; a mark at 35 sounds nothing.
    const notes = [
        { T: 0, D: 1, S: 10 }, { T: 1, D: 1, S: 20 }, { T: 2, D: 1, S: 30 },
        { T: 3, D: 1, S: 10 }, { T: 4, D: 1, S: 20 }, { T: 5, D: 1, S: 30 },
        { T: 6, D: 1, S: 40 }, { T: 7, D: 1, S: 50 },
    ];

    it('takes the note the caret is on at its first onset', () => {
        assert.equal(startSecondsAt(notes, 20, true), 1);
        assert.equal(startSecondsAt(notes, 50, true), 7);
    });

    it('takes the first note written at or after a place that sounds nothing', () => {
        assert.equal(startSecondsAt(notes, 35, true), 6);     // a mark: the next note
        assert.equal(startSecondsAt(notes, 15, false), 1);    // between two notes: the one after
        assert.equal(startSecondsAt(notes, 10, false), 0);
    });

    it('is the top with no place, and with nothing after it', () => {
        assert.equal(startSecondsAt(notes, -1, false), 0);
        assert.equal(startSecondsAt(notes, 99, false), 0);
        assert.equal(startSecondsAt([], 10, true), 0);
    });

    it('leaves out the parts the score does not sound', () => {
        const parts = ['melody', 'extra'];
        const all = [{ T: 0, D: 4, N: 0 }, { T: 0, D: 9, N: 1 }];
        assert.deepEqual(soundingOf(all, parts, ['extra']), [all[0]]);
        assert.deepEqual(soundingOf(all, parts, null), all);
    });
});
