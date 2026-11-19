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
import { loopPlace } from '../src/webview/playbackCore';
import { extensionSource } from './extensionSource';

// The preview's Range panel (owner's request 2026-10-25): each part's lowest and highest note,
// asked of the server for the score shown, a click showing one on the score.
describe('the preview\'s Range panel', () => {
    const extension = extensionSource();
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
        assert.match(html, /<div id="practicePanel" class="range-panel practice-panel mixer-panel" hidden>\s*<button type="button" id="practiceClose" class="panel-close"/);
        assert.match(html, /getElementById\('practiceClose'\)\.addEventListener\('click', closePractice\);/);
        assert.match(html, /\.range-panel \.panel-close \{\s*float: right; position: sticky; top: 0;/);
        for (const m of html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)) {
            assert.doesNotThrow(() => new vm.Script(m[1]));
        }
    });

    it('asks the server for the score shown, and again after every render while open', () => {
        assert.match(extension, /'lilysharp\/ranges', \{ textDocument: \{ uri \}, renderName: selectedRenders\.get\(uri\) \|\| null \}/);
        assert.match(html, /function refreshRanges\(\) \{\s*if \(!rangePanel\.hidden\) vscode\.postMessage\(\{ type: 'requestRanges' \}\);/);
        assert.match(html, /lightCaretBar\(\);\s*(?:lap\('[^']*'\);\s*)?drawAbFlags\(\);[^\n]*\s*showBarInfo\(\);[^\n]*\s*refreshRanges\(\);/);
        assert.match(html, /case 'rangesData':\s*rangeParts = message\.parts \|\| null;/);
    });

    // Play of the bars A to B and the repeat; the length beside them (owner's requests 2026-10-25,
    // and 2026-11-17: the A-B repeat retires the selected bars — the selection places A and B).
    it('plays the bars A to B, repeats while Repeat is on, and shows the length', () => {
        assert.match(html, /pendingSpan = abOn \? \{ ab: \[abFrom, abTo\] \} : null;/);
        assert.doesNotMatch(html, /pendingSpan = aiRangeLo >= 0 \? selectedBarsSpan\(\) : null;/);
        assert.match(html, /window\.LysSynth\.playBarsWindow\(notes, span\.ab\[0\], span\.ab\[1\], true, bars\)/);
        // ▶ keeps the selection lit: only a click on the sheet itself clears it.
        assert.match(html, /\} else if \(target && mainContent\.contains\(target\)\) \{\s*\/\/ Clicked empty space/);
        // Repeat is the header's button, after Stop (owner's decision 2026-11-19): the practice
        // panel's Loop group — the A-B button, its bar numbers, the eight kept ranges — is gone, A-B
        // being set on the page (a click A, a Shift+click B, B's own bar line again: off).
        assert.match(html, /<button id="stopBtn"[^>]*>[^<]*<\/button>\s*(<!--[^>]*-->\s*)?<button id="loopBtn" class="toggle icon" type="button" aria-pressed="false" aria-label="Repeat"/);
        assert.doesNotMatch(html, /data-fold="loop"|id="abBtn"|id="abStrips"|id="abFromNum"|abSlots|selectedBarsSpan|abFirstPlace/);
        assert.match(html, /loopBtn\.setAttribute\('aria-pressed', String\(loopOn\)\);/);
        assert.doesNotMatch(html, /loopBtn\.textContent/);
        assert.match(html, /if \(abOn && line === abTo \+ 1\) \{ abOff\(\); return; \}/);
        assert.match(html, /practiceOn = speedOn \|\| practice\.click \|\| practice\.countIn;/);
        // Pause is drawn (two bars), not the thin glyph (owner's report 2026-11-19).
        assert.match(html, /<button id="pauseBtn" class="toggle icon"[^>]*><svg [^>]*fill="currentColor"[^>]*><rect [^>]*\/><rect [^>]*\/><\/svg><\/button>/);
        assert.match(html, /<input type="range" id="tempoSlider" class="fader" min="50" max="150" step="5" value="100"/);
        // The whole score round and round goes as it plays (its repeats kept): once is false.
        assert.match(html, /const list = spanOf\(playbackNotes, span, false\);/);
        // A loop's rounds laid out ahead, each whole bars long.
        // With the count-in on, a count before every round (docs/transport-mixer-design.md §1 4).
        assert.match(html, /window\.LysSynth\.loopNotes\(list, 600, loopRound, gap\)/);
        assert.match(html, /const c = loopRound > 0 \? counted\(\[\], 0, spanStart\) : null;\s*loopGap = c && c\.lead > 0 \? c\.lead : 0;/);
        // ...and the voice follows the stretch and its rounds (docs/singing-design.md §5).
        // (The recording beside a chart, on but not lined up with the score, plays alone instead.)
        assert.match(html, /if \(loopOn\) \{[^\n]*\n[^\n]*\n\s*const rounds = looped\(list\);\s*voicePlan = loopVoicePlan\(\);\s*if \(recordingAlone\(\)\) break;/);
        assert.match(html, /\s*startPlayback\(rounds, 0, false, false, loopGap, spanStart\);\s*break;/);
        // Repeat on, A-B off: the first round from the A flag, its own count-in before it (owner's
        // report 2026-11-17: it started from the top); nothing sung before A, the header at A meanwhile.
        assert.match(html, /if \(!span && startBar > 0\) \{[\s\S]{0,400}?const at = barStartIn\(list, beats, startBar\);/);
        assert.match(html, /const music = window\.LysSynth\.fromOffset\(rounds, entry\);/);
        assert.match(html, /const from = Math\.max\(listTime, voicePlan\.entry \|\| 0\);/);
        assert.match(html, /if \(lt < loopEntry\) return \{ tau: Math\.max\(0, loopEntry - countInFix\), counting: null \};/);
        assert.match(html, /function loopVoicePlan\(\) \{ return \{ pieces: spanPieces, rounds: loopRoundCount, gap: loopGap \}; \}/);
        assert.match(html, /voicePlan = \{ pieces: spanPieces, rounds: 1 \};\s*if \(recordingAlone\(\)\) break;\s*const c = counted\(list, 0, spanStart\);/);
        assert.match(html, /else startPlayback\(list, 0, false, false, 0, spanStart\);/);
        // The practice menu's count-in (owner's choice 2026-10-26): a toggle beside the metronome,
        // and a Play of the whole score or from a note counts in too; a tempo change, the loop's
        // next round and a seek do not.
        // Tempo is one line, never folded (owner's decision 2026-11-19): its slider, Metronome and
        // Count-in themselves — no copies of them for a folded head, no Reset (a double-click is 100%).
        assert.match(html, /<div class="mhead mtempo"><span class="mtitle mlabel">Tempo<\/span><input type="range" id="tempoSlider" class="fader" min="50" max="150" step="5" value="100" aria-label="Tempo"><span id="tempoValue" class="flevel">100%<\/span><button type="button" id="clickBtn" class="toggle" aria-pressed="false"[^>]*>Metronome<\/button><button type="button" id="countInBtn" class="toggle" aria-pressed="false"[^>]*>Count-in<\/button><\/div>/);
        assert.doesNotMatch(html, /data-fold="tempo"|data-mini|syncMinis|tempoReset|clickBtn\.textContent|countInBtn\.textContent/);
        assert.match(html, /resetOnDoubleClick\(tempoSlider, \(\) => \{ tempoSlider\.value = '100'; setSpeed\(100\); showPractice\(\); \}\);/);
        // (the count shows A meanwhile: loopEntry, where the music comes in)
        assert.match(html, /const c = counted\(playbackNotes, startAt, 0\);\s*if \(c\) \{ startPlayback\(c\.notes, 0, false, false, c\.lead - startAt, 0\); loopEntry = c\.lead; \}/);
        assert.match(html, /if \(c\) \{ startPlayback\(c\.notes, 0, false, false, c\.lead, spanStart\); loopEntry = c\.lead; \}/);
        // (the loop's next rounds count in when the count-in is on: their own count, loopGap)
        assert.match(html, /function playEnded\(\) \{[\s\S]{0,200}?startPlayback\(rounds, 0, false, false, loopGap, /);
        assert.match(html, /return \{ tau: Math\.max\(0, lt - countInFix\), counting: null \};/);
        assert.match(html, /practice\.countIn = !practice\.countIn;\s*if \(loopList && playingNow\(\)\) practiceChanged\(practice\.speed\);/);
        assert.match(html, /playEndTimer = setTimeout\(playEnded, /);
        assert.match(html, /function playEnded\(\) \{\s*const list = loopOn \? loopList : null;/);
        // A loop's refresh answered with an error (a part half typed) leaves the loop going and
        // the page playing (owner's report 2026-11-17): the next edit asks again.
        assert.match(html, /if \(pendingLoopRefresh\) \{\s*pendingLoopRefresh = false;\s*if \(playingNow\(\)\) break;\s*\}/);
        // Text that cannot be read plays as it last could be (owner's decision 2026-11-17), a Play
        // or an audition alike, and Play's edge says so while it sounds.
        assert.match(html, /if \(\(message\.error \|\| !message\.notes\) && rawNotes\) \{\s*message\.notes = rawNotes;/);
        assert.match(html, /markUnreadable\(!!\(message\.error \|\| !message\.notes\)\);/);
        assert.match(html, /playBtn\.classList\.toggle\('stale', playing && scoreUnreadable\);/);
        assert.match(html, /\.toolbar button#playBtn\.playing\.stale \{/);
        assert.match(html, /refreshRanges\(\);\s*vscode\.postMessage\(\{ type: 'requestLength' \}\);/);
        assert.match(extension, /if \(message\.type === 'requestLength'\)/);
    });

    // A held step key edits every ~30 ms; the preview's debounce restarted on each edit, so it
    // stood still until the key was let go (owner's report 2026-11-16). A max-wait bounds it.
    it('follows a held key: the refresh waits no longer than its max-wait after the first change', () => {
        assert.match(extension, /const PREVIEW_MAX_WAIT = \d+;/);
        // (The rule itself is PreviewSession.refreshAfter's: previewSession.test.ts.)
        assert.match(extension, /sessionOf\(panel\)\.refreshAfter\(delay, PREVIEW_MAX_WAIT, Date\.now\(\),\s*\(\) => updatePreviewContent\(event\.document, panel, context\)\);/);
    });

    // Play always from the A flag (owner's decisions 2026-11-17, docs/transport-mixer-design.md
    // §1 3): stopped, playing or paused alike; the editor's caret has no say ("why does it play
    // from here?"). Pause holds where it plays and goes on from there; Stop goes back to A.
    it('plays from the A flag, stopped, playing or paused alike — not from the editor\'s caret', () => {
        assert.match(html, /const playBtn = document\.getElementById\('playBtn'\);\s*playBtn\.disabled = false;/);
        assert.doesNotMatch(html, /getElementById\('playBtn'\)\.disabled = playing/);
        const play = html.slice(html.indexOf("getElementById('playBtn').addEventListener('click'"), html.indexOf("getElementById('stopBtn').addEventListener('click'"));
        assert.match(play, /paused = null;\s*if \(playingNow\(\)\) stopPlayback\(\);/);
        assert.match(play, /pendingStartBar = abOn \? -1 : Math\.max\(0, abFrom\);/);
        assert.match(play, /pendingSpan = abOn \? \{ ab: \[abFrom, abTo\] \} : null;/);
        assert.doesNotMatch(play, /caretStartPos\(\)/);
        // The answer starts at the A bar's first play, on the list's time.
        assert.match(html, /if \(pendingStartBar >= 0\) \{[\s\S]{0,300}startAt = barStartIn\(playbackNotes, /);
        // Stopped, the header says A's bar's time — off the notes in hand, no caret.
        const ask = html.slice(html.indexOf('function askStoppedPosition()'), html.indexOf('function stopPlayback()'));
        assert.doesNotMatch(ask, /caretStartPos/);
        assert.match(ask, /if \(rawNotes\) \{ stoppedSeconds = barStartIn\(rawNotes, rawBeats, m\);/);
    });

    it('finds where a printed bar first plays, back to its bar line', () => {
        const at = html.indexOf('function barStartIn(');
        const body = html.slice(at, html.indexOf('\n        }', at) + '\n        }'.length);
        const context = vm.createContext({});
        new vm.Script(body).runInContext(context);
        const run = (code: string) => new vm.Script(code).runInContext(context) as number;
        // Bars of 2 s; bar 1 printed once but played twice (a repeat): its first play.
        run(`var list = [{ T: 0.5, M: 0 }, { T: 2.5, M: 1 }, { T: 6.5, M: 1 }, { T: 9, M: 3 }];
             var beats = [0, 2, 4, 6, 8, 10].map(T => ({ T, A: true })).concat([{ T: 3, A: false }]).sort((a, b) => a.T - b.T);`);
        assert.equal(run('barStartIn(list, beats, 1)'), 2);
        assert.equal(run('barStartIn(list, beats, 0)'), 0);
        // A bar with nothing in it: the next bar that has something.
        assert.equal(run('barStartIn(list, beats, 2)'), 8);
        assert.equal(run('barStartIn(list, beats, 9)'), 0);
    });

    it('pauses where it plays and goes on from there; Stop goes back to A; Space plays or pauses', () => {
        assert.match(html, /id="pauseBtn"/);
        const pause = html.slice(html.indexOf("getElementById('pauseBtn').addEventListener('click'"));
        assert.match(pause, /startPlayback\(p\.notes, p\.at, false, false, p\.countFix, p\.origin\);/);
        assert.match(pause, /notes: playingNotes, at: Math\.max\(0, playOffset \+ playClock\(\)\), origin: playOrigin,/);
        assert.match(html, /function playingNow\(\) \{ return !document\.getElementById\('stopBtn'\)\.disabled && !paused; \}/);
        // A soloed line is heard though muted — its own mute, its group's All off (owner's decision
        // 2026-11-17: S is "listen to this").
        assert.match(html, /out\.muted = own\.muted\.filter\(n => !solo\.has\('part:' \+ n\)\)/);
        assert.match(html, /out\[name\] = \(soloParts \? !solo\.has\('rec:' \+ name\) : recPartsOff\.includes\(name\)\) \? 0 : levelIn\(recPartLevels, name\);/);
        assert.match(html, /return !!\(\(recOn \|\| recSoloed\(\)\) && recording && recording\.ready && recordingPlayer\(\)\);/);
        // The practice panel's folds are kept per file; a file's first opening has every group folded
        // (owner's requests 2026-11-17).
        assert.match(html, /practiceBtn\.setAttribute\('aria-expanded', 'true'\);[\s\S]{0,300}applyFolds\(\);/);
        assert.match(html, /folds\[g\.dataset\.fold\] = all \? true : !!foldsKept\[g\.dataset\.fold\];/);
        assert.match(html, /vscode\.postMessage\(\{ type: 'panelFolds', chart: foldsChart, folds: foldsKept \}\);/);
        assert.match(extension, /postIfChanged\(panel, \{ type: 'panelFolds', chart: uri, folds: panelFoldsFor\(uri\) \}\);/);
        assert.match(extension, /if \(message\.type === 'panelFolds'\) \{\s*if \(typeof message\.chart === 'string'\) \{ keepPanelFolds\(message\.chart, message\.folds\); \}/);
        // Play is lit while it plays, Pause while paused (owner's choice 2026-11-17).
        assert.match(html, /playBtn\.classList\.toggle\('playing', !!playing && !paused\);/);
        assert.match(html, /\.toolbar button#playBtn\.playing \{/);
        assert.match(html, /document\.getElementById\(playingNow\(\) \|\| paused \? 'pauseBtn' : 'playBtn'\)\.click\(\);/);
        // Rewind: the A flag to the top; playing, from there (owner's request 2026-11-17).
        assert.match(html, /getElementById\('rewindBtn'\)\.addEventListener\('click', \(\) => \{\s*setA\(0, false\);\s*if \(playingNow\(\)\) \{ document\.getElementById\('playBtn'\)\.click\(\); return; \}/);
        // The parts turned off stay in the list (Z): silent, and lighting the bar while nothing else sounds.
        assert.match(html, /practiceNotes\(timed, rawParts, playedBeats\(\), Object\.assign\(\{\}, eff\(\), \{ speed: playBasis\.speed \}\), rawExtra, true\);/);
        assert.match(html, /if \(quiet && quiet\.changed && r\.sounding\.length === 0\) \{[\s\S]{0,300}lightPlayingBar\(els\);/);
        // A click on a bar (or a note of it) takes the A flag there.
        assert.match(html, /const m = measureOfStaffBox\(bar\);\s+if \(m >= 0\) setA\(m\);/);
    });

    // Where the playback stands, just left of the length (owner's request 2026-11-16), on the
    // length's yardstick: the score's seconds at the written tempo.
    it('shows where the playback stands beside the length, in the score\'s seconds', () => {
        assert.match(html, /<span id="posInfo" class="length"[^>]*hidden><\/span><span id="posSlash" class="length" hidden>\/<\/span><span id="lengthInfo"/);
        // …and over them the bar, "12 / 118" (owner's request 2026-11-19: the practice panel's
        // "118 bars placed" line took room there): the bar lit while it plays, else the A flag's.
        // The two "/" one over the other (owner's request 2026-11-19): each its own cell of a grid.
        assert.match(html, /<span class="where"><span id="barNow" class="length"[^>]*hidden><\/span><span id="barSlash" class="length" hidden>\/<\/span><span id="barLast" class="length"[^>]*hidden><\/span><span id="posInfo"/);
        assert.match(html, /\.toolbar \.where \{ display: inline-grid; grid-template-columns: auto auto auto;/);
        assert.match(html, /\.toolbar \.where #barNow \{ grid-area: 1 \/ 1; justify-self: end; \}/);
        assert.match(html, /\.toolbar \.where #posInfo \{ grid-area: 2 \/ 1; justify-self: end; \}/);
        assert.match(html, /now\.textContent = num\(Math\.min\(m, last\)\);/);
        assert.match(html, /document\.getElementById\('posSlash'\)\.hidden = el\.hidden;/);
        assert.match(html, /if \(Number\.isInteger\(m\) && m !== litBar\) \{ litBar = m; showBarInfo\(\); \}/);
        assert.doesNotMatch(html, /bars placed'/);
        // No function of the page is declared twice: the later one silently takes the name (the
        // header's bar was first called showBar, the name of the function that scrolls to a bar —
        // nothing showed, and no test said so; owner's report 2026-11-19).
        const declared = Array.from(html.matchAll(/^\s*(?:async\s+)?function\s+([A-Za-z_]\w*)\s*\(/gm), m => m[1]);
        const twice = declared.filter((name, i) => declared.indexOf(name) !== i);
        assert.deepEqual(twice, []);
        const fn = (name: string) => {
            const at = html.indexOf('function ' + name + '(');
            return html.slice(at, html.indexOf('\n        }', at) + '\n        }'.length);
        };
        const context = vm.createContext({
            playClock: () => 3, playOffset: 1, countInFix: 0, lastSpan: undefined, spanStart: 0, loopList: null, loopRound: 0, loopGap: 0, loopEntry: 0,
            playBasis: { warp: null, speed: 0.5 }, practice: { speed: 0.5 }, recPlayer: null,
            window: { LysSynth: { unwarpTime: (_w: unknown, u: number) => u * 10, loopPlace } },
        });
        new vm.Script(fn('playbackScoreSeconds') + '\n' + fn('placeNow')).runInContext(context);
        const run = (code: string) => new vm.Script(code).runInContext(context) as number | null;
        // 4 s into a list played at half speed: 2 s of the score.
        assert.equal(run('playbackScoreSeconds()'), 2);
        // A count-in of 1 s is not the score's.
        run('countInFix = 1');
        assert.equal(run('playbackScoreSeconds()'), 1.5);
        // A Play from A (played time 2) after a count-in whose music comes in at the list's 6:
        // the count shows A, not the seconds before it; then on from A.
        run('countInFix = 4; loopEntry = 6');
        assert.equal(run('playbackScoreSeconds()'), 2 * 0.5);
        run('playClock = () => 6');
        assert.equal(run('playbackScoreSeconds()'), 3 * 0.5);
        run('countInFix = 1; loopEntry = 0; playClock = () => 3');
        // A looped stretch from played time 6, 4 s a round, 9 s in: the stretch's start plus 1 s.
        run('countInFix = 0; lastSpan = [0, 1]; spanStart = 6; loopList = []; loopRound = 4; playOffset = 0; playClock = () => 9');
        assert.equal(run('playbackScoreSeconds()'), (6 + 1) * 0.5);
        // …with a count-in of 2 s before each round (docs/transport-mixer-design.md §1 4): rounds of
        // 6 s from the list's 0, the music 2 s into each; in a count-in, the stretch's start.
        run('countInFix = 2; loopGap = 2; playClock = () => 4');
        assert.equal(run('playbackScoreSeconds()'), (6 + 2) * 0.5);
        run('playClock = () => 7');
        assert.equal(run('playbackScoreSeconds()'), 6 * 0.5);
        run('playClock = () => 9');
        assert.equal(run('playbackScoreSeconds()'), (6 + 1) * 0.5);
        run('countInFix = 0; loopGap = 0');
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

    // A system taller than the view (a big band zoomed in; owner's report 2026-11-16: the view
    // jerked up and down with the topmost sounding staff): the view keeps its place in the system
    // and takes the same place in the next, both ways in one smooth scroll.
    it('keeps the view\'s place in a system taller than the view, moving only to the next system', () => {
        assert.match(html, /if \(system && system\.height > m\.height - 2 \* margin\) \{/);
        assert.match(html, /if \(!moving\) followSystemOffset = m\.top - system\.top;/);
        assert.match(html, /followTopTarget = mainContent\.scrollTop \+ system\.top \+ followSystemOffset - m\.top;/);
        assert.match(html, /mainContent\.scrollTo\(\{ top: topTo, left: mainContent\.scrollLeft \+ dx, behavior: vertical \? 'smooth' : 'auto' \}\);/);
        assert.match(html, /followSystemDocTop = null; followSystemOffset = null; followTopTarget = null;/);
    });

    // The playing bar tinted on its staves, as a hover tints a bar (owner's request 2026-11-16).
    it('tints the playing bar on its staves alone, and clears it when the playback stops', () => {
        assert.match(html, /#svgContainer rect\.bar-staff\.caret,\s*#svgContainer rect\.bar-staff\.playing \{/);
        assert.match(html, /svgContainer\.querySelectorAll\('rect\.bar-staff:not\(\[data-row\]\)'\)/);
        assert.match(html, /if \(painted\.length > 0\) lightPlayingBar\(painted\);/);
        assert.match(html, /clearHighlights\(\);\s*lightPlayingBar\(null\);/);
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
    // The recording follows the chart's own transpose (owner's decision 2026-11-16), on top of the
    // panel's octave; the octave is kept per recording. (Owner's decisions 2026-11-19: Speed and
    // pitch is gone but for the octave, and the file's tuning is not used — no button turns the
    // recording to A = 440, docs/recording-design.md §3 2.)
    it('plays the recording moved by the chart\'s transpose, and keeps the panel\'s octave', () => {
        assert.match(html, /function recSound\(\) \{ return \{ octave: recSettings\.octave, chart: chartShift \}; \}/);
        assert.match(html, /let recSettings = \{ octave: 0 \};/);
        const glue = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'recording.ts'), 'utf8');
        assert.match(html, /<div class="prow moctrow" id="recOctRow"><span class="plbl">Octave<\/span>/);
        assert.doesNotMatch(html, /recSpeedSlider|recSemiSlider|recCentSlider|recLinkBtn|recChartRow|recReset|recSummary|id="recSettings"|<details|recTuneBtn|recTuning/);
        assert.doesNotMatch(glue, /tuning/);
        // The chart shown last decides what the page hears of a recording (owner's report 2026-11-19:
        // a chart with none, shown after one with a recording, kept that recording in its panel — its
        // "none" was posted first, the other's files still being looked at). Seen in a real VS Code:
        // five switches of five wrong before, none after.
        assert.match(glue, /const turn = \(sendTurns\.get\(panel\) \?\? 0\) \+ 1;\s*sendTurns\.set\(panel, turn\);\s*const file = /);
        assert.match(glue, /const key = [^\n]*\n\s*if \(sendTurns\.get\(panel\) !== turn\) \{\s*return;[^\n]*\n\s*\}\s*if \(!force && sentKeys\.get\(panel\) === key\)/);
        // Faders 100% stands at the right of the Octave row — beside As scored with no recording — and
        // the panel has no foot line for it (owner's request 2026-11-19).
        assert.match(html, /const row = document\.querySelector\(recording \? '#recOctRow \.tempo' : '#partsReset'\)\.closest\('\.tempo'\);\s*row\.appendChild\(document\.getElementById\('fadersReset'\)\);/);
        assert.match(html, /getElementById\('mixRecGroup'\)\.hidden = false;\s*placeFadersReset\(\);/);
        assert.match(html, /getElementById\('mixRecGroup'\)\.hidden = true;[^\n]*\s*placeFadersReset\(\);/);
        assert.doesNotMatch(html, /mfoot/);
        assert.doesNotMatch(html, /\.play\(sec, recSettings,|recPlayer\.update\(recSettings\)|recordingPlan\(recSettings/);
        assert.match(html, /case 'chartTranspose': \{\s*const n = Number\(message\.semitones\) \|\| 0;\s*if \(n !== chartShift\) \{ chartShift = n; recApply\(\); \}/);
        assert.match(extension, /panel\.webview\.postMessage\(\{ type: 'chartTranspose', semitones: response\.ChartTranspose \?\? 0 \}\);/);
        assert.match(html, /vscode\.postMessage\(\{ type: 'recordingSettings', chart: recording\.chart, name: recording\.name, settings: recSettings \}\);/);
        assert.match(html, /const kept = m\.settings && typeof m\.settings === 'object' \? Number\(m\.settings\.octave\) : 0;/);
        assert.match(glue, /void settingsStore\.update\(settingsKey\(chart, audio\), octave === 0 \? undefined : \{ octave \}\);/);
        assert.match(extension, /keepRecordingSettings\(message\.chart, message\.name, message\.settings\);/);
    });

    it('sounds the recording with the score, from the score\'s clock', () => {
        assert.doesNotMatch(html, /id="recBtn"/);
        assert.match(html, /<div class="mgroup" id="mixRecGroup"[^>]*>[\s\S]{0,300}<button type="button" id="recOnBtn"[^>]*>[\s\S]{0,300}<input type="range" id="recLevelSlider" class="fader"/);   // (the mixer's recording group head: §1 2)
        // The recording is the mixer's own group (docs/transport-mixer-design.md §1 1-2): its head holds its
        // on/off, its solo and its fader — no longer a line among the score's parts.
        assert.doesNotMatch(html, /toggle\('Recording', /);
        assert.match(html, /<div class="mgroup" id="mixRecGroup" data-fold="rec" hidden>\s*<div class="mhead"><button type="button" class="mfold mtitle" aria-expanded="true">Recording<\/button><button type="button" id="recOnBtn"[^>]*><span id="recName"><\/span><\/button><button type="button" id="recAllBtn"[^>]*>All<\/button><input type="range" id="recLevelSlider"/);
        // Each group's head has two buttons (owner's request 2026-11-18): its on/off, which keeps the
        // lines' own on/off, and All.
        assert.match(html, /<button type="button" id="scoreOnBtn"[^>]*>Play<\/button><button type="button" id="scoreAllBtn"[^>]*>All<\/button><input type="range" id="scoreLevelSlider"/);
        assert.match(html, /function flipRecording\(\) \{\s*(\/\/[^\n]*\s*)*recOn = !recOn;\s*renderRecParts\(\);/);
        assert.match(html, /const on = !recPartsOff\.includes\(name\);/);
        assert.match(html, /function recLevel\(\) \{ return solo\.size > 0 && !recSoloed\(\) \? 0 : levelIn\(recLevels, 'Recording'\); \}/);
        assert.match(html, /delete recLevels\.Recording;/);
        assert.match(html, /if \(origin !== undefined\) end = Math\.max\(end, startRecordingAlong\(t0, offset, origin, !!loopList\)\);/);
        assert.match(html, /const timed = warp \? window\.LysSynth\.warpNotes\(notes, warp\) : notes;/);
        // Only the answer to the latest request plays (owner's report 2026-11-17: A-B flags moved
        // while it played, a first answer took the span and a second played the whole score past B).
        assert.match(html, /function requestPlay\(\) \{ vscode\.postMessage\(\{ type: 'requestPlayback', seq: \+\+playSeq \}\); \}/);
        assert.doesNotMatch(html, /postMessage\(\{ type: 'requestPlayback' \}\)/);
        assert.match(html, /case 'playbackData':\s+if \(typeof message\.seq === 'number' && message\.seq !== playSeq\) \{/);
        // A flag moved while it plays goes on from where it plays when that is in the new bars
        // (owner's request 2026-11-17), from the notes in hand — no request.
        assert.match(html, /function replayIfPlaying\(\) \{\s+if \(!playingNow\(\)\) return;\s+if \(resumeHere\(\)\) return;/);
        const resume = html.slice(html.indexOf('function resumeHere()'), html.indexOf('function setAb('));
        assert.doesNotMatch(resume, /requestPlay\(/);
        // Repeat turned on or off while it plays: laid out again from here, so B does what Repeat says then.
        assert.match(html, /loopOn = !loopOn;[\s\S]{0,500}if \(!playingNow\(\) \|\| !resumeHere\(\)\) \{/);
        assert.match(resume, /startPlayback\(rounds, at === null \? 0 : loopOffset\(at, place\.counting\), false, false, loopGap, spanStart\);/);
        assert.match(resume, /startPlayback\(list, at === null \? 0 : at, false, false, 0, spanStart\);/);
        // A bar clicked while it plays, whose first note is tied over from the bar before (no
        // onset of its own in the list): from the bar's start, not on from where it was (owner's
        // report 2026-11-19, Moon-on-the-River.lys: bar 3 playing, bar 14 clicked).
        assert.match(html, /function seekToBar\(m\) \{\s+if \(playheadTimer === null \|\| !playbackNotes \|\| m < 0\) return false;/);
        assert.match(html, /const at = barStartIn\(playbackNotes, \(playedBeats\(\) \|\| \[\]\)\.map\(b => \(\{ T: b\.T \/ speed, A: b\.A \}\)\), m\);\s+seekPlay\(at\);/);
        assert.match(html, /if \(!abOn && \(seekToCopy\(pos, inBar\) \|\| seekToBar\(m\)\)\) return;/);
        // A seek while Repeat goes round stays in the loop: its rounds laid out again and entered at
        // the place, with the voices' plan - not the score once under a plan of rounds, which left
        // the voice going round alone after the instruments' one pass (owner's report 2026-11-19,
        // Spring-Breeze-Bicycle.lys).
        const seek = html.slice(html.indexOf('function seekPlay(t)'), html.indexOf('function seekToBar(m)'));
        assert.match(seek, /if \(loopOn && loopList && lastSpan !== undefined && loopRound > 0\) \{\s+const rounds = looped\(loopList\);\s+voicePlan = loopVoicePlan\(\);/);
        assert.match(seek, /startPlayback\(rounds, at === null \? 0 : loopOffset\(at, null\), false, false, loopGap, spanStart\);\s+return;/);
        assert.match(seek, /startPlayback\(playbackNotes, t, false, false, 0, 0\);/);
        assert.match(html, /if \(best === null\) return false;\s+seekPlay\(best\.T\);/);
        // Its start: the list's offset in the score's played time (count-in aside) times the speed.
        const at = html.indexOf('function startRecordingAlong(');
        const body = html.slice(at, html.indexOf('\n        }', at) + '\n        }'.length);
        const plays: unknown[] = [];
        const context = vm.createContext({
            playBasis: { warp: {}, speed: 0.5 }, countInFix: 0, loopRound: 4, loopGap: 0, lastSpan: undefined as unknown, playSpan: 0, recSettings: {}, recSound: () => ({}), practice: { speed: 0.5 },
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
        // A count-in of 1 s before each round: the recording goes on through it, as through the
        // first — the loop takes in the second before the stretch.
        run('countInFix = 1; loopGap = 1');
        run('startRecordingAlong(100, 0, 8, true)');
        assert.deepEqual(JSON.parse(JSON.stringify(plays.pop())), [3.5, { tempo: 0.5, at: 100 }, { from: 3.5, to: 6 }]);
        // …and from the score's 0, the loop begins before the recording does (silence there).
        run('countInFix = 2; loopGap = 2');
        run('startRecordingAlong(100, 0, 0, true)');
        assert.deepEqual(JSON.parse(JSON.stringify(plays.pop())), [0, { tempo: 0.5, at: 102 }, { from: -1, to: 2 }]);
        run('countInFix = 0; loopGap = 0');
        // The A-B bars once (owner's report 2026-11-17): it ends with the stretch, not at the
        // recording's end — Repeat off, the recording went on past B.
        run('lastSpan = { ab: [4, 7] }');
        assert.equal(run('startRecordingAlong(100, 0, 8, false)'), 4);
        // A bar heard with the recording (Alt+M, owner's decision 2026-11-17): it ends with the bar.
        run('lastSpan = undefined; playSpan = 1.5');
        assert.equal(run('startRecordingAlong(100, 0, 8, false)'), 1.5);
        assert.deepEqual(JSON.parse(JSON.stringify(plays.pop())), [4, { tempo: 0.5, at: 100 }, null]);
        run('playSpan = 0; lastSpan = { ab: [4, 7] }');
        run('countInFix = 2');
        assert.equal(run('startRecordingAlong(100, 0, 8, false)'), 6);
        run('countInFix = 0; lastSpan = undefined');
        plays.length = 0;
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
