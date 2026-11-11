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
import { PLAIN_TONES_JS } from '../src/plainTones';
import { nextVoiceEntry } from '../src/webview/playbackCore';

// An AI candidate can be heard before it is accepted (owner's request 2026-10-25): the candidate
// panel's Play asks the server for the notes of the text shown (lilysharp/playbackText) and the
// document's preview plays them, as its own Play would.
describe('an AI candidate plays before it is accepted', () => {
    const src = (f: string) => fs.readFileSync(path.join(__dirname, '..', '..', 'src', f), 'utf8');
    const transform = src('aiTransform.ts');
    const extension = src('extension.ts');

    it('has Play and Stop in the panel, Play sounding the pane shown', () => {
        assert.match(transform, /<button id="play"[^>]*>▶ Play<\/button>/);
        assert.match(transform, /<button id="stop">■ Stop<\/button>/);
        assert.match(transform, /const which = \(\) => paneAfter\.classList\.contains\('show'\) \? 'after' : 'before';/);
        // From the selected bar, if one is: its written stretch.
        assert.match(transform, /\{ action: 'play', which: which\(\), from: span\[0\], to: span\[1\] \}/);
    });

    it('asks for the notes of the candidate or the original, and plays them on the preview', () => {
        assert.match(transform, /sendRequest<[^>]*>\('lilysharp\/playbackText', \{\s*Text: which === 'after' \? play\.after : play\.before,/);
        assert.match(transform, /deps\.playOnPreview\?\.\(play\.uri, notes\)/);
        // A decision stops it.
        assert.match(transform, /if \(audible\) \{ deps\.stopOnPreview\?\.\(currentUri\(audible\.uri\)\); \}\s*stopCandidateMidi\(deps\);\s*resolve\?\.\(d\);/);
        // An untitled score saved under review: the preview it plays on went with it.
        assert.match(transform, /const play = \{ \.\.\.audible, uri: currentUri\(audible\.uri\) \};/);
    });

    it('lets the preview page play another text\'s notes from the top, as its Play does', () => {
        const start = extension.indexOf('return `<!DOCTYPE html>') + 'return '.length;
        const end = extension.indexOf('</html>`;', start) + '</html>`'.length;
        const html = new vm.Script(extension.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
        // Never round the header's loop: a candidate plays once — and without this score's
        // singing voice (docs/singing-design.md §5: voicePlan null).
        assert.match(html, /case 'playNotes':\s*loopList = null;\s*voicePlan = null;[^\n]*\n[\s\S]{0,300}?audioBlockedTold = false;\s*startPlayback\(message\.notes, 0\);/);
        assert.match(extension, /void panel\.webview\.postMessage\(\{ type: 'playNotes', notes \}\);/);
    });

    // The notes sounding light on the panel's score (owner's request 2026-10-25): the preview
    // follows the candidate's own offsets (R) and reports them; the panel lights that pane's notes.
    // The browser's title tip was too small to read (owner's report 2026-10-25): the panel shows
    // its own, in VS Code's hover font, as the preview does.
    it('shows its own tooltip in VS Code\'s hover look', () => {
        assert.match(transform, /\.tip \{[^}]*font-size: var\(--vscode-font-size, 13px\)/);
        assert.match(transform, /e\.setAttribute\('data-tip', e\.getAttribute\('title'\)\); e\.removeAttribute\('title'\);/);
    });

    // The page script's functions, taken out of the source by their braces (none of them holds a
    // `${`), and run on plain objects.
    const fn = (source: string, head: string, context: Record<string, unknown> = {}) => {
        const at = source.indexOf(head);
        assert.ok(at >= 0, `${head} is not in the page script`);
        let depth = 0, close = -1;
        for (let i = source.indexOf('{', at); i < source.length; i++) {
            if (source[i] === '{') depth++;
            else if (source[i] === '}' && --depth === 0) { close = i + 1; break; }
        }
        return { text: source.slice(at, close), run: new vm.Script('(' + source.slice(at, close) + ')').runInNewContext({ Math, ...context }) };
    };

    // Owner's request 2026-10-26: After / Before keep the view, Ctrl+wheel zooms, a drag scrolls.
    it('keeps the view when After and Before trade places', () => {
        const classes = () => ({ toggle: () => { main.scrollTop = 0; main.scrollLeft = 0; } });   // (as a browser may on a swap)
        const main = { scrollLeft: 120, scrollTop: 2400 };
        const show = fn(transform, 'function show(after)', {
            main, paneAfter: { classList: classes() }, paneBefore: { classList: classes() },
            tAfter: { classList: classes() }, tBefore: { classList: classes() },
        }).run as (after: boolean) => void;
        show(false);
        assert.deepEqual([main.scrollLeft, main.scrollTop], [120, 2400]);
        show(true);
        assert.deepEqual([main.scrollLeft, main.scrollTop], [120, 2400]);
        // …and the switch no longer jumps to the first change.
        assert.doesNotMatch(fn(transform, 'function show(after)').text, /scrollToChange/);
    });

    it('zooms by a tenth a notch, from a quarter to four times, back to the fit at 1', () => {
        const next = fn(transform, 'function nextZoom(').run as (z: number, deltaY: number) => number;
        assert.equal(next(1, -100), 1.1);
        assert.ok(Math.abs(next(1, 100) - 1 / 1.1) < 1e-9);
        assert.equal(next(4, -100), 4);
        assert.equal(next(0.25, 100), 0.25);
        assert.equal(next(1.1, 100), 1);
        assert.equal(next(1 / 1.1, -100), 1);
    });

    it('zooms about the point under the pointer', () => {
        const zoomAbout = fn(transform, 'function zoomAbout(').run as
            (s: unknown, resize: () => void, cx: number, cy: number) => void;
        const s = { scrollLeft: 100, scrollTop: 300, scrollWidth: 1000, scrollHeight: 4000 };
        // The point 200, 100 into the box is 300, 400 into the sheets; at twice the size, 600, 800.
        zoomAbout(s, () => { s.scrollWidth = 2000; s.scrollHeight = 8000; }, 200, 100);
        assert.deepEqual([s.scrollLeft + 200, s.scrollTop + 100], [600, 800]);
        // The wheel zooms only with Ctrl, and takes the browser's own zoom away then.
        assert.match(transform, /main\.addEventListener\('wheel', \(e\) => \{\s*if \(!e\.ctrlKey\) return;\s*e\.preventDefault\(\);/);
        assert.match(transform, /\}, \{ passive: false \}\);\s*window\.addEventListener\('resize'/);
    });

    // Owner's report 2026-11-11: on a lower page of a long score, Ctrl+wheel on the PREVIEW slid the
    // view back toward the pages before — the sheet grew under a scroll offset that stayed.
    it('the preview zooms about the point under the pointer too, whatever page it is on', () => {
        const zoomKeeping = fn(extension, 'function zoomKeeping(').run as
            (s: unknown, rect: () => unknown, resize: () => void, x: number, y: number) => void;
        // A sheet 1000 × 9000 (three pages), scrolled to its third page: its box starts 6200 above
        // the view, 40 from its left. The pointer at 300, 250 is on the sheet's point 260, 6450.
        const s = { scrollLeft: 0, scrollTop: 6200 };
        let size = 1;
        const rect = () => ({ left: 40 - s.scrollLeft, top: -s.scrollTop, width: 1000 * size, height: 9000 * size });
        zoomKeeping(s, rect, () => { size = 1.5; }, 300, 250);
        // At one and a half times the size that point is 390, 9675 into the sheet — still under the pointer.
        const after = rect();
        assert.ok(Math.abs((300 - after.left) - 390) < 1e-9 && Math.abs((250 - after.top) - 9675) < 1e-9);
        assert.equal(s.scrollTop, 9675 - 250);
        // Zooming back out returns to where it was.
        zoomKeeping(s, rect, () => { size = 1; }, 300, 250);
        assert.ok(Math.abs(s.scrollTop - 6200) < 1e-9 && Math.abs(s.scrollLeft) < 1e-9);
        // The wheel hands it the scroller that scrolls the sheet, the sheet's own box and the pointer.
        assert.match(extension, /zoomKeeping\(mainContent, \(\) => sheet\.getBoundingClientRect\(\), resize, e\.clientX, e\.clientY\);/);
        assert.match(extension, /installDragScroll\(mainContent, document\);/);
    });

    it('scrolls when the sheets are dragged, with the preview\'s own code', () => {
        const words = (t: string) => t.split('\n').map(l => l.trim()).filter(l => l !== '' && !l.startsWith('//')).join('\n');
        const mine = fn(transform, 'function installDragScroll(').text;
        const preview = fn(extension, 'function installDragScroll(').text;
        assert.equal(words(mine), words(preview));
        assert.match(transform, /installDragScroll\(main, document\);/);
        assert.match(transform, /\.main \{ cursor: grab; \}/);
        // A drag selected the glyphs it crossed — a purple box on every note (owner's report
        // 2026-10-26): the sheets are not selectable, the replacement text still is.
        assert.match(transform, /\.main \{ user-select: none; -webkit-user-select: none; \}/);
        assert.doesNotMatch(transform, /\.snippet[^{]*\{[^}]*user-select/);
    });

    // The replacement text opened pushed the buttons out of the panel and could not be read to
    // its end (owner's reports 2026-10-26): it keeps its height and scrolls inside.
    it('keeps the buttons in the panel when the replacement text is opened, and scrolls the text', () => {
        assert.match(transform, /\.snippet \{ margin:10px 16px 0; flex-shrink:0; \}/);
        assert.match(transform, /\.snippet pre \{[^}]*overflow:auto;[^}]*max-height:35vh;[^}]*\}/);
        assert.match(transform, /\.footer \{[^}]*flex-shrink:0; \}/);
    });

    // The whole text is shown now; opened, its box scrolls to the first change (owner's request
    // 2026-10-26) — the box alone, its marks measured from it (position: relative).
    it('opens the replacement text at its first change', () => {
        const jump = fn(transform, 'function jumpToFirstChange(').run as (pre: unknown) => void;
        const pre = { scrollTop: 0, clientHeight: 200, querySelector: () => ({ offsetTop: 1500, offsetHeight: 16 }) };
        jump(pre);
        assert.equal(pre.scrollTop, 1500 - (200 - 16) / 2);
        const top = { scrollTop: 0, clientHeight: 200, querySelector: () => ({ offsetTop: 30, offsetHeight: 16 }) };
        jump(top);
        assert.equal(top.scrollTop, 0);
        const none = { scrollTop: 7, clientHeight: 200, querySelector: () => null };
        jump(none);
        assert.equal(none.scrollTop, 7);
        assert.match(transform, /\.snippet pre \{[^}]*position:relative; \}/);
        assert.match(transform, /details\.addEventListener\('toggle', \(\) => \{ if \(details\.open\) jumpToFirstChange\(details\.querySelector\('pre'\)\); \}\);/);
        assert.match(transform, /wholeTextMarkup\(candidate\.snap\.origFullText, spliceCandidate\(candidate\.snap, candidate\.text\), changed\)/);
        // …and it is named for what it is: the file as Accept leaves it.
        assert.match(transform, /<summary title="[^"]+">Text after Accept<\/summary>/);
        assert.doesNotMatch(transform, /<summary>Replacement text<\/summary>/);
    });

    it('lights the notes sounding on the candidate panel, not on the preview', () => {
        assert.match(transform, /S: -1, R: n\.S \?\? -1 \}\)\);\s*playingPane = which;/);
        assert.match(extension, /const relay = notes\.some\(n => n\.R !== undefined\);/);
        assert.match(extension, /if \(relay\) \{\s*vscode\.postMessage\(\{ type: 'candidateSounding'/);
        // A stop clears the panel's light.
        assert.match(extension, /if \(relaying\) \{\s*relaying = false;\s*vscode\.postMessage\(\{ type: 'candidateSounding', sounding: \[\] \}\);/);
        assert.match(extension, /if \(message\.type === 'candidateSounding'\) \{\s*showCandidateSounding\(message\.sounding\);/);
        // Owner's report 2026-11-11: Play was silent and said nothing — the preview, not clicked
        // since it loaded, may not sound. The panel says so, at every silent press.
        assert.match(extension, /setStatusBarMessage\('Lily#: click the preview once to enable its sound', 8000\);[\s\S]{0,160}tellCandidateAudioBlocked\(\);/);
        assert.match(extension, /case 'playNotes':[\s\S]{0,400}audioBlockedTold = false;\s*startPlayback\(message\.notes, 0\);/);
        const transformSource = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
        assert.match(transformSource, /export function tellCandidateAudioBlocked\(\): boolean \{\s*if \(!candidatePanel\) \{ return false; \}/);
        assert.match(transformSource, /No sound yet: click the preview once/);
        // Owner's request 2026-11-11: the proposal opens over the score's preview, a tab in its
        // column, not in a third column beside it; with no preview open, beside the editor.
        assert.match(transformSource, /const column = deps\.previewColumnOf\?\.\(currentUri\(uri\)\) \?\? vscode\.ViewColumn\.Beside;/);
        assert.match(transformSource, /'Lily# — AI candidate',\s*\{ viewColumn: column, preserveFocus: true \},/);
        assert.match(transformSource, /const panel = ensureCandidatePanel\(deps, play\.uri\);/);
        assert.match(extension, /previewColumnOf: \(uri: vscode\.Uri\) => previewPanels\.get\(uri\.toString\(\)\)\?\.viewColumn,/);
        assert.match(transform, /if \(e\.data\.type === 'sounding'\) \{ lightSounding\(e\.data\.which, e\.data\.sounding \|\| \[\]\); return; \}/);
        // A chord sounds at its '<', which its heads carry as data-alt: they light too (owner's report
        // 2026-10-29: a song's right-hand chords stayed dark in the panel), the click target and the
        // fret digit's mask left out, as in the preview.
        assert.match(transform, /querySelectorAll\('\[data-pos\], \[data-alt\]'\)\.forEach\(el => \{\s*if \(el\.classList\.contains\('nh-hit'\)/);
        assert.match(transform, /if \(alt\) alt\.split\(' '\)\.forEach\(a => add\(parseInt\(a, 10\), el\)\);/);
        // Only the copy sounding lights — a phrase played often, a section a form plays twice — told
        // apart by the bar-boxes, as the preview tells them (owner's request 2026-10-29).
        assert.match(transform, /function soundingAt\(w, s\) \{\s*const els = elementsAt\(w, s\.pos\);\s*if \(!\(Number\.isInteger\(s\.occ\) && s\.occ >= 0\) \|\| els\.length < 2\) return els;\s*const copies = copiesOf\(w, els\);\s*return copies\[Math\.min\(s\.occ, copies\.length - 1\)\] \|\| els;/);
        assert.match(transform, /for \(const s of sounding\) lit\.push\(\.\.\.soundingAt\(w, s\)\);/);
        assert.match(transform, /function copiesOf\(w, els\) \{\s*const bars = Array\.from\(\(w === 'after' \? paneAfter : paneBefore\)\.querySelectorAll\('rect\.bar-box'\)\);/);
    });
});

// The panel's page script lives in a template literal (getCandidateHtml): it parses once evaluated,
// and its copiesOf splits a note's copies by the bar each stands in, in the order of the bars.
describe('the AI candidate panel\'s page script', () => {
    // The template's script blocks as written (its HTML around them nests substitutions a plain
    // evaluation cannot take), each `${…}` standing for a value.
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
    const at = transform.indexOf('return `<!DOCTYPE html>', transform.indexOf('function getCandidateHtml('));
    const end = transform.indexOf('</html>`;', at);
    const scripts = [...transform.slice(at, end).matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)]
        .map(m => m[1].replace(/\$\{[^}]*\}/g, '0'));

    it('parses', () => {
        assert.ok(scripts.length > 0);
        for (const s of scripts) assert.doesNotThrow(() => new vm.Script(s));
    });
    // Owner's decision 2026-11-11: the panel sounds its own Play — a click on it may start its
    // audio, where the preview's stayed silent until the preview itself had been clicked.
    it('plays the notes the host answers on its own synth, lights them, and stops', () => {
        const page = scripts.join('\n');
        const cut = (head: string) => {
            const at = page.indexOf(head);
            assert.ok(at >= 0, head);
            let depth = 0;
            for (let i = page.indexOf('{', at); i < page.length; i++) {
                if (page[i] === '{') depth++;
                else if (page[i] === '}' && --depth === 0) return page.slice(at, i + 1);
            }
            throw new Error(head);
        };
        const lit: { w: string; n: number }[] = [];
        const plainPlayed: { n: number; offset: number; t0: number }[] = [];
        let plainStopped = 0;
        const said: (string | undefined)[] = [];
        let tick: (() => void) | undefined, ended = false, stopped = 0;
        const played: { notes: unknown[]; offset: number; t0: number }[] = [];
        const ctx = { currentTime: 10, state: 'running', resume: () => Promise.resolve() };
        const synth = {
            state: 'ready', ready: true, stop: () => { stopped++; },
            whenReady: () => Promise.resolve(true),
            play: (notes: unknown[], offset: number, t0: number) => { played.push({ notes, offset, t0 }); return { t0, end: 2 }; },
        };
        class SoundingCursor {
            constructor(private readonly list: { t: number; d: number; s: number; o: number }[]) {}
            at(t: number) { return { changed: true, sounding: this.list.filter(n => n.t <= t && t < n.t + n.d).map(n => ({ pos: n.s, occ: n.o })) }; }
        }
        const context = vm.createContext({
            window: { LysSynth: { SoundingCursor, create: () => synth } }, SYNTH: { processor: 'p', soundFont: 's' },
            AudioContext: function () { return ctx; }, Math, decided: false,
            ownCtx: null, ownSynth: null, ownPlayer: null, ownTick: null, ownEnd: null, ownGen: 0, ownPane: 'after',
            ownVoices: [], ownPlay: null, startVoices: () => 0,
            playingOscs: [] as { stop: () => void }[],
            // The plain tones (plainTones.ts), stood in for: what they were asked to play, and a source to stop.
            startOscillators: (_ctx: unknown, list: unknown[], offset: number, t0: number) => {
                plainPlayed.push({ n: list.length, offset, t0 });
                (context.playingOscs as { stop: () => void }[]).push({ stop: () => { plainStopped++; } });
                return 2;
            },
            say: (text?: string) => { said.push(text); },
            lightSounding: (w: string, sounding: unknown[]) => { lit.push({ w, n: sounding.length }); },
            setInterval: (f: () => void) => { tick = f; return 1; }, clearInterval: () => { tick = undefined; },
            setTimeout: () => { ended = true; return 2; }, clearTimeout: () => { /* none */ },
        });
        new vm.Script([cut('function wakeAudio()'), cut('function stopOwn()'), cut('function playOwn(w, notes, sung)')].join('\n')).runInContext(context);
        const run = (code: string) => new vm.Script(code).runInContext(context);
        const notes = [{ T: 0, D: 1, R: 40, O: 0 }, { T: 1, D: 1, R: 44, O: 1 }];
        context.notes = notes;
        run("playOwn('before', notes)");
        // On its own synth, from the list's start, a moment after now; the pane it plays is the one asked.
        assert.equal(played.length, 1);
        assert.deepEqual([played[0].offset, played[0].t0], [0, 10.15]);
        assert.equal(played[0].notes, notes);
        assert.ok(ended, 'an end is set');
        ctx.currentTime = 10.15 + 1.5;
        tick!();
        assert.deepEqual(lit.at(-1), { w: 'before', n: 1 });
        // Stop: the synth stops, the timer goes, and nothing stays lit.
        run('stopOwn()');
        assert.equal(tick, undefined);
        assert.ok(stopped >= 1);
        assert.deepEqual(lit.at(-1), { w: 'before', n: 0 });
        // A font that does not come is said, and the plain tones play in its place.
        synth.ready = false;
        ctx.currentTime = 20;
        run("playOwn('after', notes)");
        assert.equal(played.length, 1);
        assert.match(String(said.at(-1)), /sound font is not ready: plain tones this time/);
        assert.deepEqual(plainPlayed, [{ n: 2, offset: 0, t0: 20.15 }]);
        tick!();
        run('stopOwn()');
        assert.equal(plainStopped, 1, 'a Stop ends the plain tones');
        // With no sound font at all (a release carries none, and Linux has none of its own) there
        // is no synth to make: the plain tones play at once, and nothing is said of it.
        context.ownSynth = null;
        context.SYNTH = null;
        run("playOwn('before', notes)");
        assert.equal(plainPlayed.length, 2);
        assert.equal(said.at(-1), '');
        run('stopOwn()');
        // The click wakes the audio before the notes come; a decision stops the Play; the host sends
        // the notes to the panel that plays itself, and keeps the MIDI output with the preview.
        assert.match(page, /document\.getElementById\('play'\)\.addEventListener\('click', \(\) => \{ wakeAudio\(\); play\(\); \}\);/);
        assert.match(page, /decided = true;\s*stopOwn\(\);/);
        assert.match(page, /if \(e\.data\.type === 'playNotes'\) \{\s*playOwn\(/);
        assert.match(page, /if \(e\.data\.type === 'playMidi'\) \{ playMidi\(/);
        assert.match(transform, /if \(synth && synth\.script\.length > 0\) \{[\s\S]{0,1400}void panel\.webview\.postMessage\(\{\s*type: 'playNotes', which, notes,/);
        // The plain tones are one text for the preview and the panel.
        assert.match(transform, /let playingOscs = \[\];\s*\$\{PLAIN_TONES_JS\}/);
        assert.match(fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8'), /\n {8}\$\{PLAIN_TONES_JS\}\r?\n/);
        const tones = PLAIN_TONES_JS;
        assert.doesNotThrow(() => new vm.Script('let playingOscs = [];\n' + tones));
        assert.match(tones, /function startOscillators\(ctx, notes, offset, t0\)/);
        // Owner's request 2026-11-11: with a MIDI output chosen, the port plays it with no preview
        // open — the host asks the server, and the panel lights the notes by the port's clock.
        assert.match(transform, /if \(synth && synth\.midiOutput\.length > 0 && deps\.midiPlay\) \{\s*stopCandidateMidi\(deps\);\s*const sent = await deps\.midiPlay\(synth\.midiOutput, notes\);\s*if \(sent\.started\) \{\s*candidateOnMidi = true;\s*void panel\.webview\.postMessage\(\{ type: 'playMidi', which, notes, t0Ms: sent\.t0Ms, end: sent\.end \}\);/);
        // …a port that is not there is said, and the panel's own synth plays instead.
        assert.match(transform, /if \(synth\.script\.length > 0\) \{\s*void panel\.webview\.postMessage\(\{ type: 'playNotes', which, notes,\s*said: `MIDI output \$\{synth\.midiOutput\}: \$\{sent\.error \?\? 'not started'\} — played here instead\.` \}\);/);
        assert.match(transform, /\} else if \(m\?\.action === 'stop' && candidateOnMidi\) \{\s*stopCandidateMidi\(deps\);/);
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /midiPlay: async \(port: string, notes: unknown\[\]\) => \{[\s\S]{0,260}'lilysharp\/midiOut\/play', \{ port, notes, offset: 0 \}\);/);
        assert.match(ext, /midiStop: \(\) => \{ void midiOutRequest\('lilysharp\/midiOut\/stop', \{\}\); \},/);
        new vm.Script(cut('function playMidi(w, notes, t0Ms, end)')).runInContext(context);
        context.Date = { now: () => 5000 };
        tick = undefined;
        run("playMidi('after', notes, 4500, 2)");
        // Half a second into the list by the port's clock: the first note sounds.
        tick!();
        assert.deepEqual(lit.at(-1), { w: 'after', n: 1 });
        assert.equal(played.length, 1, 'the panel\'s own synth is not struck');
        run('stopOwn()');
        const core = require('../src/aiTransformCore') as { playsItself: (s?: { script: string; processor: string; soundFont: string; midiOutput: string }) => boolean };
        assert.equal(core.playsItself({ script: 'a', processor: 'b', soundFont: 'c', midiOutput: '' }), true);
        assert.equal(core.playsItself({ script: 'a', processor: 'b', soundFont: 'c', midiOutput: 'Port 1' }), false);
        assert.equal(core.playsItself({ script: 'a', processor: 'b', soundFont: '', midiOutput: '' }), false);
        assert.equal(core.playsItself(undefined), false);
        const has = (require('../src/aiTransformCore') as { hasSynth: (s?: { script: string; processor: string; soundFont: string; midiOutput: string }) => boolean }).hasSynth;
        assert.equal(has({ script: 'a', processor: 'b', soundFont: 'c', midiOutput: 'Port 1' }), true);
        assert.equal(has({ script: 'a', processor: 'b', soundFont: '', midiOutput: 'Port 1' }), false);
    });
    // Owner's request 2026-11-11: the panel played a song's melody on an instrument. The voices the
    // engine has are sung in the panel, on the clock its instruments play by.
    it('sings the voices the host hands it, the sung part lit and not struck', async () => {
        const page = scripts.join('\n');
        const cut = (head: string) => {
            const at = page.indexOf(head);
            assert.ok(at >= 0, head);
            let depth = 0;
            for (let i = page.indexOf('{', at); i < page.length; i++) {
                if (page[i] === '{') depth++;
                else if (page[i] === '}' && --depth === 0) return page.slice(at, i + 1);
            }
            throw new Error(head);
        };
        const sources: { when?: number; offset?: number; stopped: boolean; gain?: number }[] = [];
        const played: { notes: { N: number }[]; t0: number }[] = [];
        const said: string[] = [];
        const silencedNotes: { T: number; N: number }[][] = [];
        let handedOut = 0;
        let litNotes = 0, decodes = 0, failDecode = false;
        const ctx = {
            currentTime: 10, state: 'running', destination: {}, resume: () => Promise.resolve(),
            // A WAV of 8 s whose first sound is 0.2 s in: 0.1 s of consonant before the beat (lead 0.3).
            decodeAudioData: () => { decodes++; return failDecode ? Promise.reject(new Error('bad')) : Promise.resolve({ duration: 8, sampleRate: 48000, getChannelData: () => new Float32Array(4) }); },
            createBufferSource: () => {
                const s: { when?: number; offset?: number; stopped: boolean; gain?: number; buffer?: unknown; connect: (g: { value: number }) => void; start: (w: number, o: number) => void; stop: () => void } = {
                    stopped: false, connect: () => { /* to its gain */ },
                    start(w: number, o: number) { this.when = w; this.offset = o; }, stop() { this.stopped = true; },
                };
                sources.push(s);
                return s;
            },
            createGain: () => ({ gain: { value: 1 }, connect: () => { /* to the output */ } }),
        };
        const synth = {
            state: 'ready', ready: true, stop: () => { /* none */ }, whenReady: () => Promise.resolve(true),
            play: (notes: { N: number }[], _offset: number, t0: number) => {
                played.push({ notes, t0 });
                return { t0, end: 2, get handedOutUntil() { return handedOut; }, silence: (list: { T: number; N: number }[]) => { silencedNotes.push(list); } };
            },
        };
        class SoundingCursor {
            constructor(readonly list: unknown[]) { litNotes = list.length; }
            at() { return { changed: false, sounding: [] }; }
        }
        const context = vm.createContext({
            window: { LysSynth: { SoundingCursor, create: () => synth, soundStart: () => 0.2, nextVoiceEntry } }, SYNTH: { processor: 'p', soundFont: 's' },
            AudioContext: function () { return ctx; }, Math, Promise, decided: false,
            ownCtx: null, ownSynth: null, ownPlayer: null, ownTick: null, ownEnd: null, ownGen: 0, ownPane: 'after', ownVoices: [], ownPlay: null,
            playingOscs: [], startOscillators: () => 2, say: (text: string) => { said.push(text); }, lightSounding: () => { /* none */ },
            setInterval: () => 1, clearInterval: () => { /* none */ }, setTimeout: () => 2, clearTimeout: () => { /* none */ },
        });
        new vm.Script([cut('function wakeAudio()'), cut('function decodeVoices(list)'), cut('function voiceSource(v, when, offset)'),
            cut('function startVoices(decoded, t0, start, pre)'), cut('function joinVoice(w, voice)'),
            cut('function stopOwn()'), cut('function playOwn(w, notes, sung)')].join('\n')).runInContext(context);
        const run = (code: string) => new vm.Script(code).runInContext(context);
        const settle = () => new Promise<void>(resolve => setImmediate(resolve));
        // Track 0 is the sung melody, track 1 the bass.
        context.notes = [{ T: 0, D: 1, R: 40, N: 0 }, { T: 0, D: 2, R: 60, N: 1 }, { T: 1, D: 1, R: 44, N: 0 }];
        context.voice = { part: 'melody', bytes: new Uint8Array(16), lead: 0.3 };
        run("playOwn('after', notes, { voices: [voice], silenced: [0], start: 0 })");
        assert.equal(played.length, 0, 'nothing sounds until the voice is decoded');
        await settle();
        // The instruments wait the consonant's 0.1 s; the bass alone is struck, and every note is lit.
        assert.equal(played.length, 1);
        assert.ok(Math.abs(played[0].t0 - 10.25) < 1e-9);
        assert.deepEqual(played[0].notes.map(n => n.N), [1]);
        assert.equal(litNotes, 3);
        // The voice begins where its sound does, so its time 0.3 (the score's 0) falls on the instruments' t0.
        assert.equal(sources.length, 1);
        assert.ok(Math.abs(sources[0].when! - 10.15) < 1e-9 && Math.abs(sources[0].offset! - 0.2) < 1e-9);
        run('stopOwn()');
        assert.ok(sources[0].stopped, 'a Stop ends the voice');
        // From inside the score (a click on a note 2 s in): no wait, and the voice is cut in there.
        ctx.currentTime = 20;
        run("playOwn('after', notes, { voices: [voice], silenced: [0], start: 2 })");
        await settle();
        assert.ok(Math.abs(played[1].t0 - 20.15) < 1e-9);
        assert.ok(Math.abs(sources[1].when! - 20.15) < 1e-9 && Math.abs(sources[1].offset! - 2.3) < 1e-9);
        // A newer Play while a voice decodes: the older one never starts.
        run('stopOwn()');
        run("playOwn('after', notes, { voices: [voice], silenced: [0], start: 0 }); stopOwn()");
        await settle();
        assert.equal(played.length, 2);
        // A WAV that will not decode: the melody's instrument plays after all.
        failDecode = true;
        run("playOwn('after', notes, { voices: [voice], silenced: [0], start: 0 })");
        await settle();
        assert.deepEqual(played[2].notes.map(n => n.N), [0, 1, 0]);
        assert.equal(sources.length, 2);
        assert.ok(decodes >= 4);
        // Owner's request 2026-11-11: a voice that is ready while the piece plays comes in after
        // the next rest. The melody plays on its instrument (nothing silenced, no voice yet)…
        failDecode = false;
        ctx.currentTime = 30;
        context.long = [{ T: 0, D: 1, R: 40, N: 0 }, { T: 0, D: 8, R: 60, N: 1 }, { T: 3, D: 1, R: 44, N: 0 }, { T: 6, D: 1, R: 48, N: 0 }];
        run("playOwn('after', long, { voices: [], silenced: [], start: 0 })");
        assert.deepEqual(played[3].notes.map(n => n.N), [0, 1, 0, 0]);
        const before = sources.length;
        // …and 1 s in its voice lands, with rests before 3 s (2 s long) and before 6 s.
        ctx.currentTime = 30.15 + 1;
        handedOut = 1.25;
        context.late = { part: 'melody', bytes: new Uint8Array(16), lead: 0.3, track: 0, entries: [{ at: 3, room: 2 }, { at: 6, room: 2 }] };
        run("joinVoice('after', late)");
        await settle();
        // The voice is cut in for the note at 3 s, its lead's 0.3 s before the beat; the melody's
        // instrument is struck no more from there, and the bass is left alone.
        assert.equal(sources.length, before + 1);
        assert.ok(Math.abs(sources.at(-1)!.when! - (30.15 + 3 - 0.3)) < 1e-9 && Math.abs(sources.at(-1)!.offset! - 3) < 1e-9);
        assert.deepEqual(silencedNotes.at(-1)!.map(n => [n.T, n.N]), [[3, 0], [6, 0]]);
        assert.match(said.at(-1)!, /^The voice of melody comes in after the next rest\.$/);
        // A voice for the other pane, or with no rest left, or after the Play has ended, waits for the next Play.
        run("joinVoice('before', late)");
        await settle();
        assert.match(said.at(-1)!, /^The voice of melody is ready: Play sings it\.$/);
        ctx.currentTime = 30.15 + 7;
        said.length = 0;
        run("joinVoice('after', late)");
        await settle();
        assert.match(said.at(-1)!, /is ready: Play sings it\.$/);
        assert.equal(sources.length, before + 1);
        run('stopOwn()');
        assert.ok(sources.at(-1)!.stopped);
        run("joinVoice('after', late)");
        await settle();
        assert.equal(sources.length, before + 1);
        // A Play from 2 s in: the entry at 3 s is 1 s away in the list's time.
        ctx.currentTime = 50;
        handedOut = 0.25;
        run("playOwn('after', long.filter(n => n.T >= 2).map(n => Object.assign({}, n, { T: n.T - 2 })), { voices: [], silenced: [], start: 2 })");
        ctx.currentTime = 50.15 + 0.3;
        run("joinVoice('after', late)");
        await settle();
        assert.ok(Math.abs(sources.at(-1)!.when! - (50.15 + 1 - 0.3)) < 1e-9 && Math.abs(sources.at(-1)!.offset! - 3) < 1e-9);
        assert.deepEqual(silencedNotes.at(-1)!.map(n => n.T), [1, 4]);
        run('stopOwn()');
        assert.match(page, /if \(e\.data\.type === 'voiceReady'\) \{ if \(e\.data\.voice\) joinVoice\(e\.data\.which === 'before' \? 'before' : 'after', e\.data\.voice\); return; \}/);
        assert.match(transform, /void panel\.webview\.postMessage\(\{ type: 'voiceReady', which, voice \}\);/);
        // The host: the voices of the text played, asked with what its playback sings and plays.
        assert.match(page, /playOwn\(e\.data\.which === 'before' \? 'before' : 'after', e\.data\.notes \|\| \[\],\s*\{ voices: e\.data\.voices \|\| \[\], silenced: e\.data\.silenced \|\| \[\], start: e\.data\.start \|\| 0, said: e\.data\.said \|\| '' \}\);/);
        assert.match(transform, /const sung = await deps\.voicesFor\?\.\(text, play\.uri, deps\.renderNameOf\?\.\(play\.uri\), sounds, voice => \{/);
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /voicesFor: candidateVoices,/);
        assert.match(ext, /sendRequest\('lilysharp\/sung', \{ textDocument: \{ uri: uri\.toString\(\) \}, text, renderName: renderName \?\? null \}\);/);
        // A voice the renderer holds is there at once; one to be sung is waited for briefly, then left to land later.
        assert.match(ext, /const \{ rendered, cached \} = await engine\.render\(voice, 1\);/);
        assert.match(ext, /out\.waiting\.push\(voice\.Part\);[\s\S]{0,160}rendered\.then\(async sungNow => \{[^\n]*onSung\(await handed\(voice, sungNow\.wav\)\); \}\)/);
        assert.match(ext, /track: candidateSilencedTrack\(voice\.Part, sounds\), entries: voiceEntries\(voice\.Notes\),/);
    });
    it('names the voices a candidate\'s Play sings and the instruments they replace', () => {
        const core = require('../src/aiTransformCore') as {
            candidateSings: (v: { Part: string; InScore?: boolean; Notes: unknown[] }[], s: object) => { Part: string }[];
            candidateSilencedTrack: (part: string, s: object) => number;
            candidateWaitingNote: (w: string[]) => string | undefined;
        };
        const sounds = { Parts: ['bass', 'melody', 'alto'], Sung: ['melody', 'alto'], Played: ['bass', 'alto'] };
        const voices = [
            { Part: 'melody', Notes: [1] }, { Part: 'alto', Notes: [1] },
            { Part: 'tenor', Notes: [1], InScore: false },      // the practice menu's, not the score's
            { Part: 'melody', Notes: [] },                       // words with no notes
            { Part: 'bass', Notes: [1] },                        // not sung by this score
        ];
        assert.deepEqual(core.candidateSings(voices, sounds).map(v => v.Part), ['melody', 'alto']);
        // A part only sung gives its instrument up; one played too sounds both ways.
        assert.equal(core.candidateSilencedTrack('melody', sounds), 1);
        assert.equal(core.candidateSilencedTrack('alto', sounds), -1);
        assert.equal(core.candidateSilencedTrack('bass', sounds), -1);
        assert.equal(core.candidateSilencedTrack('melody', {}), -1);
        assert.equal(core.candidateWaitingNote([]), undefined);
        assert.equal(core.candidateWaitingNote(['melody']), 'The voice of melody is being sung: its instrument plays until it is ready.');
        assert.match(core.candidateWaitingNote(['sop', 'alto'])!, /^The voices of sop, alto are being sung: their instruments play until they are ready\.$/);
    });
    it('splits a note\'s copies by their bars, whatever the order of the DOM', () => {
        const page = scripts.join('\n');
        const head = page.indexOf('function copiesOf(');
        let depth = 0, close = -1;
        for (let i = page.indexOf('{', head); i < page.length; i++) {
            if (page[i] === '{') depth++;
            else if (page[i] === '}' && --depth === 0) { close = i + 1; break; }
        }
        const rect = (left: number, top: number, w: number, h: number) => ({ left, top, right: left + w, bottom: top + h, width: w, height: h });
        const boxes = [rect(0, 0, 50, 4), rect(50, 0, 50, 4)];
        const pane = { querySelectorAll: () => boxes.map(b => ({ getBoundingClientRect: () => b, getBBox: () => ({ width: b.width }) })) };
        const copiesOf = new vm.Script('(' + page.slice(head, close) + ')')
            .runInNewContext({ paneAfter: pane, paneBefore: pane, Array, Map, Math, Infinity }) as (w: string, els: unknown[]) => { id: string }[][];
        const el = (x: number, y: number) => ({ id: x + ',' + y, getBoundingClientRect: () => rect(x, y, 1, 1) });
        // The second bar's copy drawn first, a head under the staff with the first bar's copy.
        const copies = copiesOf('after', [el(55, 2), el(5, 2), el(5, 6)]);
        assert.deepEqual(Array.from(copies, c => Array.from(c, e => e.id).join(' ')), ['5,2 5,6', '55,2']);
    });
});
