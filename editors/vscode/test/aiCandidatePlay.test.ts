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
        assert.match(transform, /if \(audible\) \{ deps\.stopOnPreview\?\.\(currentUri\(audible\.uri\)\); \}\s*resolve\?\.\(d\);/);
        // An untitled score saved under review: the preview it plays on went with it.
        assert.match(transform, /const play = \{ \.\.\.audible, uri: currentUri\(audible\.uri\) \};/);
    });

    it('lets the preview page play another text\'s notes from the top, as its Play does', () => {
        const start = extension.indexOf('return `<!DOCTYPE html>') + 'return '.length;
        const end = extension.indexOf('</html>`;', start) + '</html>`'.length;
        const html = new vm.Script(extension.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
        // Never round the header's loop: a candidate plays once.
        assert.match(html, /case 'playNotes':\s*loopList = null;\s*startPlayback\(message\.notes, 0\);/);
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
        assert.match(transform, /if \(e\.data\.type === 'sounding'\) \{ lightSounding\(e\.data\.which, e\.data\.sounding \|\| \[\]\); return; \}/);
    });
});
