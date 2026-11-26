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
import { namesChords, songGuide, songPartName, songRequest, songWithWords, songWordsName, SONG_STEPS } from '../src/songOverChordsCore';

const root = path.join(__dirname, '..', '..');
const read = (...p: string[]) => fs.readFileSync(path.join(root, ...p), 'utf8');

// A score read from a recording, as Orecchiante writes it (We're An American Band.lys, abridged).
const band = `// read from band.mp3 by orecchiante
title "Band"
part bass { instrument bass }
section A {
  chords prog { D | C | }
  bass { d8 d d d d d d d | c c c c c c c c | }
}
form { A }
score {
  chords prog
  tab bass
}
`;

describe('Write a Song over These Chords', () => {
    it('names the new part and its words with names the score does not use', () => {
        assert.equal(songPartName(band), 'vocal');
        assert.equal(songWordsName(band), 'words');
        assert.equal(songPartName(band + 'part vocal { instrument alto }\npart vocal2 { }\n'), 'vocal3');
        assert.equal(songWordsName(band + 'lyrics words { a | }\n'), 'songWords');
        assert.equal(songWordsName(band + 'lyrics words { a | }\nlyrics songWords { b | }\n'), 'songWords2');
        // A name in a comment is no part.
        assert.equal(songPartName(band + '// part vocal { }\n'), 'vocal');
    });

    it('tells a score with chords rows from one without', () => {
        assert.equal(namesChords(band), true);
        assert.equal(namesChords('part p { }\nsection A { p { c1 | } }\n// chords x { C | }\n'), false);
    });

    it('asks the chat for a part in THIS file, by its names, through the song guide', () => {
        const q = songRequest('C:\\songs\\Band.lys', 'vocal', 'words', ' a Japanese song about summer ', true);
        assert.match(q, /^Write a song over the chords of the Lily# score C:\\songs\\Band\.lys: add a sung part to THIS file/);
        assert.match(q, /Name the part `vocal` and its words `words`\./);
        assert.match(q, /lilysharp_readGuide, topic "song"/);
        assert.match(q, /The request: a Japanese song about summer$/);
        // Nothing asked: the AI chooses; a score with no chords rows is asked of its harmony.
        const none = songRequest('C:\\x.lys', 'vocal', 'words', '  ', false);
        assert.match(none, /over the harmony of/);
        assert.match(none, /No request was given: choose the song yourself/);
    });

    it('guides the chat by the steps and the composing guide\'s word on a song with words — never how a new piece begins', () => {
        const compose = read('..', '..', 'docs', 'COMPOSE_FOR_LLM.md');
        const words = songWithWords(compose);
        assert.ok(words, 'COMPOSE_FOR_LLM.md has its "A song with words" item');
        assert.match(words!, /^- \*\*A song with words\*\*/);
        assert.match(words!, /\*\*Who sings\*\*/);
        // To the next item: the lead sheet's is not in it.
        assert.doesNotMatch(words!, /A song to sing and play yourself/);
        const guide = songGuide(compose);
        assert.ok(guide.startsWith(SONG_STEPS));
        assert.ok(guide.endsWith(words!));
        assert.doesNotMatch(guide, /as a NEW \.lys file/);
        assert.match(SONG_STEPS, /lilysharp_writePart/);
        assert.match(SONG_STEPS, /"first": true, "singer": "female"/);
        // A guide without the item: the steps alone.
        assert.equal(songGuide('# nothing here\n'), SONG_STEPS);
    });

    it('is a command on a .lys, and the tools take what it asks for', () => {
        const pkg = read('package.json');
        assert.match(pkg, /"command": "lilysharp\.writeSongOverChords",\s*"category": "Lily#",\s*"icon": "\$\(comment-discussion\)",\s*"title": "Write a Song over These Chords…"/);
        assert.match(pkg, /"enum": \["grammar", "composing", "start", "learn", "song", "accompaniment"\]/);
        assert.match(pkg, /"first": \{ "type": "boolean"/);
        assert.match(pkg, /"singer": \{ "type": "string"/);
        const transform = read('src', 'aiTransform.ts');
        assert.match(transform, /registerCommand\('lilysharp\.writeSongOverChords'/);
        assert.match(transform, /First: p\?\.first === true, Singer: typeof p\?\.singer === 'string' \? p\.singer : undefined/);
        assert.match(transform, /if \(topic === 'song'\) \{\s*return paged\('song guide', guidePages\(songGuide\(/);
    });
});
