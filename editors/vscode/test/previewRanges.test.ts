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

// The preview's Range panel (owner's request 2026-10-25): each part's lowest and highest note,
// asked of the server for the score shown, a click showing one on the score.
describe('the preview\'s Range panel', () => {
    const extension = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = extension.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = extension.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(extension.slice(start, end).replace(/\$\{[^}]*\}/g, '0')).runInNewContext({}) as string;

    it('has a Range button and its panel, and the page script parses', () => {
        assert.match(html, /<button id="rangeBtn" class="toggle icon" type="button" aria-pressed="false" aria-label="Range"[^>]*><svg [^>]*>/);
        assert.match(html, /rangeBtn\.setAttribute\('aria-pressed', String\(!rangePanel\.hidden\)\);/);
        // Its × at the top right, what it says under it (owner's request 2026-10-29).
        assert.match(html, /<div id="rangePanel" class="range-panel" hidden><button type="button" id="rangeClose" class="panel-close" title="Close" aria-label="Close">×<\/button><div id="rangeBody"><\/div><\/div>/);
        assert.match(html, /getElementById\('rangeClose'\)\.addEventListener\('click', \(\) => \{\s*rangePanel\.hidden = true;\s*rangeBtn\.setAttribute\('aria-pressed', 'false'\);/);
        assert.doesNotMatch(html, /rangePanel\.(textContent|appendChild)/);
        // The practice panel's × closes it.
        assert.match(html, /<div id="practicePanel" class="range-panel practice-panel" hidden>\s*<button type="button" id="practiceClose" class="panel-close"/);
        assert.match(html, /getElementById\('practiceClose'\)\.addEventListener\('click', closePractice\);/);
        assert.match(html, /\.range-panel \.panel-close \{\s*float: right; position: sticky; top: 0;/);
        for (const m of html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)) {
            assert.doesNotThrow(() => new vm.Script(m[1]));
        }
    });

    it('asks the server for the score shown, and again after every render while open', () => {
        assert.match(extension, /'lilysharp\/ranges', \{ textDocument: \{ uri \}, renderName: selectedRenders\.get\(uri\) \|\| null \}/);
        assert.match(html, /function refreshRanges\(\) \{\s*if \(!rangePanel\.hidden\) vscode\.postMessage\(\{ type: 'requestRanges' \}\);/);
        assert.match(html, /lightCaretBar\(\);\s*refreshRanges\(\);/);
        assert.match(html, /case 'rangesData':\s*rangeParts = message\.parts \|\| null;/);
    });

    // Play of the selected bars and the loop; the length beside them (owner's request 2026-10-25).
    it('plays the selected bars, loops while the loop is on, and shows the length', () => {
        // The whole bars the selection lights, not the notes at its ends.
        assert.match(html, /pendingSpan = aiRangeLo >= 0 \? selectedBarsSpan\(\) : null;/);
        assert.match(html, /function selectedBarsSpan\(\) \{[\s\S]*?rect\.bar-staff\.selected\[data-span\]/);
        // ▶ keeps the selection lit: only a click on the sheet itself clears it.
        assert.match(html, /\} else if \(target && mainContent\.contains\(target\)\) \{\s*\/\/ Clicked empty space/);
        // The loop is in the practice menu, an On/Off as the metronome's.
        assert.match(html, /<div id="practicePanel"[\s\S]*<span class="plbl">Loop<\/span><button type="button" id="loopBtn" class="toggle" aria-pressed="false"/);
        assert.match(html, /<input type="range" id="tempoSlider" min="50" max="150" step="5" value="100"/);
        assert.match(html, /const list = spanOf\(playbackNotes, span, loopOn\);/);
        // A loop's rounds laid out ahead, each whole bars long.
        assert.match(html, /window\.LysSynth\.loopNotes\(list, 600, loopRound\)/);
        // ...and the voice follows the stretch and its rounds (docs/singing-design.md §5).
        // (The recording beside a chart, on but not lined up with the score, plays alone instead.)
        assert.match(html, /const toPlay = loopOn \? looped\(list\) : list;\s*voicePlan = \{ pieces: spanPieces, rounds: loopOn \? loopRoundCount : 1 \};\s*if \(recordingAlone\(\)\) break;\s*const c = counted\(toPlay, 0, spanStart\);/);
        assert.match(html, /else startPlayback\(toPlay, 0, false, false, 0, spanStart\);/);
        // The practice menu's count-in (owner's choice 2026-10-26): a toggle beside the metronome,
        // and a Play of the whole score or from a note counts in too; a tempo change, the loop's
        // next round and a seek do not.
        assert.match(html, /<span class="plbl">Count-in<\/span><button type="button" id="countInBtn" class="toggle" aria-pressed="false"/);
        assert.match(html, /const c = counted\(playbackNotes, startAt, 0\);\s*if \(c\) startPlayback\(c\.notes, 0, false, false, c\.lead - startAt, 0\);/);
        assert.match(html, /function playEnded\(\) \{[\s\S]{0,200}?startPlayback\(looped\(list\), 0, false, false, 0, /);
        assert.match(html, /let tau = Math\.max\(0, playOffset \+ playClock\(\) - countInFix\);/);
        assert.match(html, /playEndTimer = setTimeout\(playEnded, /);
        assert.match(html, /function playEnded\(\) \{\s*const list = loopOn \? loopList : null;/);
        assert.match(html, /refreshRanges\(\);\s*vscode\.postMessage\(\{ type: 'requestLength' \}\);/);
        assert.match(extension, /if \(message\.type === 'requestLength'\)/);
    });

    // Play stays pressable while playing; pressed then, it plays again from the editor's caret
    // (owner's request 2026-11-16), as Play from Here does.
    it('plays again from the editor\'s caret when Play is pressed while playing', () => {
        assert.match(html, /document\.getElementById\('playBtn'\)\.disabled = false;/);
        assert.doesNotMatch(html, /getElementById\('playBtn'\)\.disabled = playing/);
        assert.match(html, /if \(!document\.getElementById\('stopBtn'\)\.disabled\) \{\s*const from = caretStartPos\(\);\s*stopPlayback\(\);\s*pendingStartPos = from;\s*pendingSpan = null;\s*pendingFromHere = true;/);
        assert.match(html, /case 'highlightPosition':\s*editorCaret = \{ pos: message\.position, tokenStart: message\.tokenStart, caret: message\.caret \};/);
        // The caret's note, by the highlight's rule; its own place when on no note; a selection's start.
        const at = html.indexOf('function caretStartPos(');
        const body = html.slice(at, html.indexOf('\n        }', at) + '\n        }'.length);
        const context = vm.createContext({ editorCaret: null, posSorted: [10, 20, 30], HIGHLIGHT_THRESHOLD: 50, sourcePositions() { /* */ } });
        new vm.Script(body).runInContext(context);
        const run = (code: string) => new vm.Script(code).runInContext(context) as number;
        assert.equal(run('caretStartPos()'), -1);
        assert.equal(run('editorCaret = { pos: 22, tokenStart: 20, caret: 22 }; caretStartPos()'), 20);
        assert.equal(run('editorCaret = { pos: 25, tokenStart: 24, caret: 25 }; caretStartPos()'), 25);
        assert.equal(run('editorCaret = { pos: -1, tokenStart: -1, caret: 17 }; caretStartPos()'), 17);
        assert.equal(run('editorCaret = { ranges: [[40, 50], [12, 18]], caret: 18 }; caretStartPos()'), 12);
    });

    // Where the playback stands, just left of the length (owner's request 2026-11-16), on the
    // length's yardstick: the score's seconds at the written tempo.
    it('shows where the playback stands beside the length, in the score\'s seconds', () => {
        assert.match(html, /<span id="posInfo" class="length"[^>]*hidden><\/span>\s*<span id="lengthInfo"/);
        const at = html.indexOf('function playbackScoreSeconds(');
        const body = html.slice(at, html.indexOf('\n        }', at) + '\n        }'.length);
        const context = vm.createContext({
            playClock: () => 3, playOffset: 1, countInFix: 0, lastSpan: undefined, spanStart: 0, loopList: null, loopRound: 0,
            playBasis: { warp: null, speed: 0.5 }, practice: { speed: 0.5 }, recPlayer: null,
            window: { LysSynth: { unwarpTime: (_w: unknown, u: number) => u * 10 } },
        });
        new vm.Script(body).runInContext(context);
        const run = (code: string) => new vm.Script(code).runInContext(context) as number | null;
        // 4 s into a list played at half speed: 2 s of the score.
        assert.equal(run('playbackScoreSeconds()'), 2);
        // A count-in of 1 s is not the score's.
        run('countInFix = 1');
        assert.equal(run('playbackScoreSeconds()'), 1.5);
        // A looped stretch from played time 6, 4 s a round, 9 s in: the stretch's start plus 1 s.
        run('countInFix = 0; lastSpan = [0, 1]; spanStart = 6; loopList = []; loopRound = 4; playOffset = 0; playClock = () => 9');
        assert.equal(run('playbackScoreSeconds()'), (6 + 1) * 0.5);
        // On the recording's seconds: taken back off them.
        run('lastSpan = undefined; loopList = null; playClock = () => 2; playBasis = { warp: {}, speed: 1 }');
        assert.equal(run('playbackScoreSeconds()'), 20);
        // The recording alone: its own second.
        run('playClock = null; recPlayer = { playing: true, position: () => 42 }');
        assert.equal(run('playbackScoreSeconds()'), 42);
    });

    // Right after a chart is made from its recording (Convert Recording to Lily#), its preview starts
    // with the recording on (owner's request 2026-11-16): the conversion marks the chart, the first
    // sending of the sound says turnOn, once, and the page turns the recording on.
    it('starts a chart just made from its recording with the recording on, once', () => {
        const src = (name: string) => fs.readFileSync(path.join(__dirname, '..', '..', 'src', name), 'utf8');
        assert.match(src('dettato.ts'), /markJustConverted\(chart\);\s*const doc = await vscode\.workspace\.openTextDocument\(chart\);/);
        assert.match(src('recording.ts'), /const turnOn = justConverted\.delete\(chartKey\(chart\.fsPath\)\);\s*post\(\{ \.\.\.base, bytes: new Uint8Array\(bytes\), \.\.\.\(turnOn \? \{ turnOn: true \} : \{\}\) \}\);/);
        assert.match(html, /if \(m\.turnOn\) recOn = true;/);
    });

    // An @todo mark's memo can stand over another bar than its chord name or head; counted as a
    // copy of the music it was "copy 0" and lit alone, the chord name dark (owner's report
    // 2026-11-16: a chords row's marked names did not light while playing).
    it('lights a marked chord name while it sounds, its memo not counted as a copy', () => {
        const at = html.indexOf('function highlightPositions(');
        const body = html.slice(at, html.indexOf('\n        }', at) + '\n        }'.length);
        const el = (name: string, bar: number, todo: boolean) => {
            const attrs: Record<string, string> = todo ? { 'data-todo': '' } : {};
            return {
                name, bar, tagName: 'text', parentNode: null, classList: { add() { /* */ } },
                hasAttribute: (a: string) => a in attrs, getAttribute: (a: string) => attrs[a] ?? null,
                setAttribute: (a: string, v: string) => { attrs[a] = v; },
            };
        };
        const run = (elements: ReturnType<typeof el>[], occ: number): string[] => {
            const context = vm.createContext({
                posElements: new Map([[7, elements]]), highlighted: [], sourcePositions() { /* */ },
                getHighlightColor: () => '#f60',
                // The copies, as the page's bar-boxes would tell them: one a bar, in bar order.
                clusterInstances: (m: ReturnType<typeof el>[]) => {
                    const bars = [...new Set(m.map(e => e.bar))].sort((a, b) => a - b);
                    return bars.map(b => m.filter(e => e.bar === b));
                },
            });
            new vm.Script(body).runInContext(context);
            return (new vm.Script(`highlightPositions([{ pos: 7, occ: ${occ} }]).map(e => e.name)`).runInContext(context) as string[]).slice().map(String).join('|').split('|');
        };
        // The name in bar 2, its ring with it, its memo pushed over bar 1.
        const marked = [el('Dm', 2, false), el('7', 2, false), el('ring', 2, true), el('memo', 1, true)];
        assert.deepEqual(run(marked, 0), ['Dm', '7', 'ring', 'memo']);
        // A phrase played twice (two copies of the name): the copy sounding, without the mark.
        const twice = [el('C', 1, false), el('C', 5, false), el('memo', 0, true)];
        assert.deepEqual(run(twice, 1), ['C']);
        assert.deepEqual(run([el('G', 3, false), el('ring', 3, true)], 0), ['G', 'ring']);
    });

    // The recording beside a chart sounds with the score (owner's decision 2026-11-16,
    // docs/recording-design.md §1 7): a line of the practice menu's Parts and the recording panel's
    // top line; no waveform button in the toolbar.
    it('sounds the recording with the score, from the score\'s clock', () => {
        assert.doesNotMatch(html, /id="recBtn"/);
        assert.match(html, /<div id="recPanel"[^>]*>[\s\S]{0,300}<button type="button" id="recOnBtn"[^>]*>[\s\S]{0,80}<input type="range" id="recLevelSlider" class="fader"/);
        assert.match(html, /toggle\('Recording', [\s\S]{0,400}'recording', '', recLevels, 'Recording'\);/);
        assert.match(html, /delete recLevels\.Recording;/);
        assert.match(html, /if \(origin !== undefined\) end = Math\.max\(end, startRecordingAlong\(t0, offset, origin, !!loopList\)\);/);
        assert.match(html, /const timed = warp \? window\.LysSynth\.warpNotes\(notes, warp\) : notes;/);
        // Its start: the list's offset in the score's played time (count-in aside) times the speed.
        const at = html.indexOf('function startRecordingAlong(');
        const body = html.slice(at, html.indexOf('\n        }', at) + '\n        }'.length);
        const plays: unknown[] = [];
        const context = vm.createContext({
            playBasis: { warp: {}, speed: 0.5 }, countInFix: 0, loopRound: 4, recSettings: {}, practice: { speed: 0.5 },
            recordingAlong: () => true, recLevel: () => 1,
            recPlayer: {
                duration: 60, loop: null as unknown,
                presetLoop(l: unknown) { this.loop = l; }, setLevel() { /* */ },
                play(sec: number, _s: unknown, when: unknown) { plays.push([sec, when, this.loop]); },
            },
        });
        new vm.Script(body).runInContext(context);
        const run = (code: string) => new vm.Script(code).runInContext(context);
        // The whole score from its 10th second at half speed: the recording from its 5th.
        assert.equal(run('startRecordingAlong(100, 10, 0, false)'), 120);
        assert.deepEqual(JSON.parse(JSON.stringify(plays.pop())), [5, { tempo: 0.5, at: 100 }, null]);
        // A count-in of 2 s before the recording's 0: it waits for it.
        run('countInFix = 3');
        run('startRecordingAlong(100, 1, 0, false)');
        assert.deepEqual(JSON.parse(JSON.stringify(plays.pop())), [0, { tempo: 0.5, at: 102 }, null]);
        // A stretch from played time 8, looping: the loop is its seconds.
        run('countInFix = 0');
        assert.equal(run('startRecordingAlong(100, 0, 8, true)'), 0);
        assert.deepEqual(JSON.parse(JSON.stringify(plays.pop())), [4, { tempo: 0.5, at: 100 }, { from: 4, to: 6 }]);
        // Not lined up: nothing.
        run('playBasis = { warp: null, speed: 1 }');
        assert.equal(run('startRecordingAlong(100, 0, 0, false)'), 0);
        assert.equal(plays.length, 0);
    });

    // Played on the MIDI output the page sounds nothing, and the browser throttled its timers to
    // one a second: one eighth note in four lit (owner's report 2026-10-25). The host ticks it.
    it('keeps the follow-along on the host\'s beat as well as the page\'s own timer', () => {
        assert.match(extension, /followTicker \?\?= setInterval\(\(\) => void panel\.webview\.postMessage\(\{ type: 'followTick' \}\), 50\);/);
        assert.match(html, /case 'followTick':\s*if \(followTick\) followTick\(\);/);
        assert.match(html, /followTick = tick;\s*playheadTimer = setInterval\(tick, 50\);/);
    });

    // The tessitura (owner's request 2026-10-25): named, over a chart of the time on each semitone.
    it('shows each part\'s tessitura over a chart of where its time is', () => {
        assert.match(html, /for \(const h of \['Part', 'Lowest', 'Highest', 'Tessitura'\]\)/);
        assert.match(html, /tr\.appendChild\(tessituraCell\(p\)\);/);
        assert.match(html, /const inside = m >= p\.TessituraLow && m <= p\.TessituraHigh;/);
        assert.match(html, /t\.textContent = pitchName\(m\) \+ ': ' \+ Math\.round\(v \* 100\) \+ '% of the time';/);
    });

    // The browser's title tip was too small to read (owner's report 2026-10-25): the page shows
    // its own, in VS Code's hover font, from the same titles.
    it('shows its own tooltip in VS Code\'s hover look, from the titles', () => {
        assert.match(html, /\.tip \{[\s\S]*?font-size: var\(--vscode-font-size, 13px\)/);
        assert.match(html, /e\.setAttribute\('data-tip', e\.getAttribute\('title'\)\);\s*e\.removeAttribute\('title'\);/);
        assert.match(html, /const t = !svgContainer\.contains\(e\) && e\.querySelector && e\.querySelector\(':scope > title'\);/);
    });

    it('shows a clicked end on the score and in the editor, the next place on the next click', () => {
        assert.match(html, /if \(rangeCur === key\) rangeAt\[key\] = \(\(rangeAt\[key\] \|\| 0\) \+ 1\) % n;/);
        assert.match(html, /const painted = highlightPositions\(\[pos\]\);/);
        assert.match(html, /vscode\.postMessage\(\{ type: 'jumpToPosition', position: pos \}\);\s*renderRanges\(\);/);
    });
});
