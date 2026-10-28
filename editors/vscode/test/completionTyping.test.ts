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

// Every word a completion list offers, typed key by key through smart typing at the place
// the list offers it, must land as typed. The places and words are the language server's
// (completion-typing.json, written by LilySharp.Tests CompletionTypingAuditTests, which walks
// the same words through the server). Two of the three owner reports of 2026-10-28 were this
// half: `c'4@` + 8 rewrote the note's duration (so `@8va` never completed), and
// `paper { size a5 }` lost its 5.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { CARET, show, typeKey } from './simulate';

interface Fixture { places: Record<string, string>, words: [string, string][] }

const fixture: Fixture = JSON.parse(fs.readFileSync(
    path.join(__dirname, '..', '..', 'test', 'completion-typing.json'), 'utf8'));

describe('every completion word types as pressed (completion-typing.json)', () => {
    it('the fixture holds the walk', () => {
        assert.ok(fixture.words.length > 900, `only ${fixture.words.length} words`);
        assert.ok(fixture.words.some(([p, w]) => p === 'music-at' && w === '8va'));
        assert.ok(fixture.words.some(([p, w]) => p === 'paper>size' && w === 'a5'));
    });
    it('no key of any word is rewritten, moved or swallowed', () => {
        const changed: string[] = [];
        for (const [place, word] of fixture.words) {
            const start = fixture.places[place];
            let marked = start;
            for (const key of word) {
                marked = show(typeKey(marked, key));
            }
            if (marked !== start.replace(CARET, word + CARET)) {
                const at = start.indexOf(CARET);
                changed.push(`${place}: ${word} -> ${JSON.stringify(marked.slice(Math.max(0, at - 8), at + word.length + 8))}`);
            }
        }
        assert.deepEqual(changed, []);
    });
});
