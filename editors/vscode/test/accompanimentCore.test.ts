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
import { ACCOMPANIMENT_STEPS, PLAY_RECORDING_TOO, accompanimentGuide, accompanimentRequest, addedParts, declaredParts, guideItem, silencedMessage } from '../src/accompanimentCore';
import { parseRecording, serializeRecording } from '../src/recordingCore';
import { recordedExportPlan } from '../src/recordingExportCore';
import type { PlaybackBeat, PlaybackNote, PracticeState } from '../src/webview/playbackCore';

const root = path.join(__dirname, '..', '..');
const read = (...p: string[]) => fs.readFileSync(path.join(root, ...p), 'utf8');

// A score read from a recording, as Orecchiante writes it (abridged), and the same with a band added.
const chart = `// read from band.mp3 by orecchiante
title "Band"
part bass { instrument bass }
section A {
  chords prog { D | C | }
  bass { d8 d d d d d d d | c c c c c c c c | }
}
form { A }
score { chords prog  tab bass }
`;
const arranged = chart.replace('section A', 'part piano { instrument piano }\npart drums { clef percussion }\n// part guitar { }\nsection A');

// The fourth step of the way from a song to a dance (Shosa's, 2026-11-26; docs/accompaniment-design.md).
describe('Arrange an Accompaniment', () => {
    it('knows the parts a score has, and the ones an arrangement added', () => {
        assert.deepEqual(declaredParts(chart), ['bass']);
        assert.deepEqual(addedParts(chart, arranged), ['piano', 'drums'], 'not the one in a comment');
        assert.deepEqual(addedParts(chart, chart.replace('d8 d d', 'd8 a d')), [], 'notes changed, no part added');
    });

    it('asks the chat for parts in THIS file, keeping the ones it has, through the accompaniment guide', () => {
        const q = accompanimentRequest('C:\\songs\\Band.lys', ['bass'], ' a funk band ', true, true);
        assert.match(q, /^Arrange an accompaniment for the Lily# score C:\\songs\\Band\.lys: add accompanying parts to THIS file, written from its chords/);
        assert.match(q, /The parts it has stay as they are: bass\. Add only the instruments it does not have\./);
        assert.match(q, /plays in its place, on its bars/);
        assert.match(q, /lilysharp_readGuide, topic "accompaniment"/);
        assert.match(q, /The request: a funk band$/);
        const none = accompanimentRequest('C:\\x.lys', [], '', false, false);
        assert.match(none, /written from its harmony/);
        assert.doesNotMatch(none, /stay as they are|recording/);
        assert.match(none, /No request was given: a band \(piano, bass and drums\)/);
    });

    it('guides the chat by its steps and the composing guide\'s band and style', () => {
        const compose = read('..', '..', 'docs', 'COMPOSE_FOR_LLM.md');
        const band = guideItem(compose, 'A band');
        const style = guideItem(compose, 'A style named is its rhythm first');
        assert.ok(band && style, 'COMPOSE_FOR_LLM.md has both items');
        assert.match(band!, /^- \*\*A band\*\*/);
        assert.doesNotMatch(band!, /One staff only when it is asked for/, 'to the next item');
        const guide = accompanimentGuide(compose);
        assert.ok(guide.startsWith(ACCOMPANIMENT_STEPS));
        assert.ok(guide.endsWith(band! + style!));
        assert.doesNotMatch(guide, /as a NEW \.lys file/);
        assert.match(ACCOMPANIMENT_STEPS, /lilysharp_writePart/);
        assert.match(ACCOMPANIMENT_STEPS, /"part": "drums", "title": "Drums", "body": "clef percussion"/);
        // The grammar section it names is there.
        assert.match(read('..', '..', 'docs', 'GRAMMAR_FOR_LLM.md'), /^## Drums \(`clef percussion`\)/m);
        assert.equal(accompanimentGuide('# nothing\n'), ACCOMPANIMENT_STEPS);
    });

    it('is a command on a .lys, with its guide topic, and makes the recording silent when accepted', () => {
        const pkg = read('package.json');
        assert.match(pkg, /"command": "lilysharp\.arrangeAccompaniment",\s*"category": "Lily#",\s*"icon": "\$\(comment-discussion\)",\s*"title": "Arrange an Accompaniment…"/);
        const transform = read('src', 'aiTransform.ts');
        assert.match(transform, /registerCommand\('lilysharp\.arrangeAccompaniment'/);
        assert.match(transform, /if \(topic === 'accompaniment'\) \{\s*return paged\('accompaniment guide', guidePages\(accompanimentGuide\(/);
        assert.match(transform, /if \(arranging\.has\(arrangedFor\) && addedParts\(current, text\)\.length > 0\) \{\s*arranging\.delete\(arrangedFor\);\s*void silenceRecordingUnder\(currentUri\(uri\)\);/);
        assert.match(silencedMessage('Band.lys'), /^Lily#: Band\.lys has its own accompaniment now, so its recording is silent/);
        assert.equal(PLAY_RECORDING_TOO, 'Play the Recording Too');
    });
});

describe('a silent recording', () => {
    const file = JSON.stringify({ orecchiante: 2, audio: 'band.mp3', bars: [{ bar: 0, start: 1, end: 3, section: 'A' }, { bar: 1, start: 3, end: 5 }], tempoBpm: 120 });

    it('is "silent" in the file beside the chart, the rest kept', () => {
        const rec = parseRecording(file);
        assert.ok(!('error' in rec));
        assert.equal(rec.silent, undefined);
        rec.silent = true;
        const written = serializeRecording(rec);
        const again = parseRecording(written);
        assert.ok(!('error' in again));
        assert.equal(again.silent, true);
        assert.equal(JSON.parse(written).tempoBpm, 120);
        assert.equal(JSON.parse(written).bars[0].section, 'A');
        again.silent = false;
        assert.equal(JSON.parse(serializeRecording(again)).silent, undefined, 'heard again: the item goes');
    });

    it('times an export by its bars, and is not mixed in', () => {
        const notes: PlaybackNote[] = [{ T: 0, D: 0.5, P: 62 } as PlaybackNote, { T: 2, D: 0.5, P: 60 } as PlaybackNote];
        const beats: PlaybackBeat[] = [0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5].map((t, i) => ({ T: t, A: i % 4 === 0 } as PlaybackBeat));
        const practice = (silent: boolean): PracticeState => ({ speed: 1, muted: [], click: false, countIn: false,
            recording: { bars: [{ start: 1, end: 3 }, { start: 3, end: 5 }], sound: { octave: 0 }, level: silent ? 0 : 1, ...(silent ? { silent: true } : {}) } });
        const heard = recordedExportPlan(notes, null, beats, 4, practice(false));
        const silent = recordedExportPlan(notes, null, beats, 4, practice(true));
        assert.ok(heard.recording, 'heard: mixed in');
        assert.equal(silent.recording, null, 'silent: not mixed in');
        // …and the notes on the recording's seconds all the same: bar 2 starts at its second 3.
        assert.deepEqual(silent.notes.map(n => n.T), heard.notes.map(n => n.T));
        assert.equal(silent.notes[1].T - silent.notes[0].T, 2);
    });

    it('is turned on again from the mixer, and said in the export\'s message and the stems', () => {
        const page = read('src', 'previewHtml.ts');
        assert.match(page, /if \(recOn && recSilent\) \{\s*recSilent = false;\s*if \(recording\) vscode\.postMessage\(\{ type: 'recordingSilent', chart: recording\.chart, silent: false \}\);/);
        assert.match(page, /if \(!\(recordingAlong\(\) \|\| recTimesOnly\(\)\) \|\| !window\.LysSynth\.timeWarp\) return null;/);
        assert.match(read('src', 'extension.ts'), /message\.type === 'recordingSilent'[\s\S]{0,300}setRecordingSilent\(chart\.fsPath, message\.silent === true\)/);
        assert.match(read('src', 'exportPreview.ts'), /The recording is silent, so it is not in the file/);
        assert.match(read('src', 'stems.ts'), /const heard = !!\(warp && recording && !recording\.silent\);/);
    });
});
