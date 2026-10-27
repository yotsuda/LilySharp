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

// The Edit Bars Beside the Scan panel's page script (omrAiFix.ts panelHtml), as the webview gets it.
// Owner's requests 2026-10-27: a line that holds two bars shows both, and every picture zooms with
// Ctrl+wheel and pans with a drag.
describe('the Edit Bars Beside the Scan page', () => {
    const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'omrAiFix.ts'), 'utf8');
    const open = src.indexOf('<script nonce="${nonce}">');
    const close = src.indexOf('</script>', open);
    // The template literal's text as the page receives it: its escapes undone, its
    // interpolations a value.
    const script = src.slice(open + '<script nonce="${nonce}">'.length, close)
        .replace(/\$\{[^{}]*\}/g, '0')
        .replace(/\\\\/g, '\\')
        .replace(/\\`/g, '`');

    it('is a script that parses', () => {
        assert.ok(open > 0 && close > open);
        assert.doesNotThrow(() => new vm.Script(script));
    });

    it('crops a picture to every bar the line meets on the system, not the first alone', () => {
        assert.match(script, /if \(!isNaN\(from\) && !isNaN\(to\) && from < hi && to >= lo\) found\.push\(r\);/);
        assert.match(script, /x0 = Math\.min\(x0, u\.x\); x1 = Math\.max\(x1, u\.x \+ u\.w\);/);
    });

    it('zooms a picture with Ctrl+wheel about the pointer and pans it with a drag that is no click', () => {
        assert.match(script, /document\.addEventListener\('wheel', e => \{\s*if \(!e\.ctrlKey\) return;\s*const svg = e\.target\.closest && e\.target\.closest\('svg\.pic'\);/);
        assert.match(script, /drag\.svg\.setAttribute\('viewBox', \[x - dx \/ drag\.s, y - dy \/ drag\.s, w, h\]\.join\(' '\)\);/);
        assert.match(script, /document\.addEventListener\('click', e => \{ if \(dragged\) \{ e\.stopPropagation\(\); e\.preventDefault\(\); dragged = false; \} \}, true\);/);
    });

    it('writes the missing bar line into the field and sends it as the edit', () => {
        assert.match(script, /const bl = e\.target\.closest && e\.target\.closest\('button\.barline'\);/);
        assert.match(script, /if \(ta\) \{ ta\.value = bl\.dataset\.line; sendEdit\(i\); \}/);
    });

    it('offers the button when the line has a bar line the compiler finds missing', () => {
        assert.match(src, /row\.barFix = await barFixOf\(run\.client, spliceBar\(run\.base\.text, target, plain\), target\.start, plain\.length\);/);
        assert.match(src, /\/bar lines\? looks\? to be missing\/\.test\(d\.Message\)/);
        assert.match(src, /'lilysharp\/missingBarLines', \{ Text: text, Start: start, End: start \+ length \}/);
        assert.match(src, /reason: lost \+ reason \+ sure \+ hint \+ barLine \+ aiNow/);
    });
});

// Owner's request 2026-10-27: the preview's context menu has no Fix OMR Marks with AI, and the
// scan view's head opens the marks' bars beside the scan instead of asking the AI.
describe('the doors to the bars of the marks', () => {
    const root = path.join(__dirname, '..', '..');
    const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
    const scan = fs.readFileSync(path.join(root, 'src', 'omrScan.ts'), 'utf8');
    const fix = fs.readFileSync(path.join(root, 'src', 'omrAiFix.ts'), 'utf8');

    it('leaves Fix OMR Marks with AI out of the preview\'s and the scan view\'s menus', () => {
        const items = pkg.contributes.menus['webview/context'] as { command: string; when: string }[];
        assert.equal(items.filter(m => m.command === 'lilysharp.omrFixWithAi').length, 0);
        assert.equal(items.filter(m => m.command === 'lilysharp.editBarsBesideScan').length, 1);
    });

    it('shows a turning ring and a pulsing word while a row asks the AI', () => {
        assert.match(fix, /const aiNow = r\.ai === 'asking' \? ` <span class="busy">— <span class="spin"><\/span>asking the AI…<\/span>/);
        assert.match(fix, /\.spin \{[^}]*animation:spin \.8s linear infinite; \}/);
        assert.match(fix, /@keyframes busy \{ 0%, 100% \{ opacity:1; \} 50% \{ opacity:\.4; \} \}/);
    });

    it('has the scan view\'s button open the marks\' bars beside the scan', () => {
        assert.match(scan, /Edit <span id="fixN">0<\/span> <span id="fixBars">Bars<\/span> Beside the Scan…<\/button>/);
        assert.match(scan, /fix\.addEventListener\('click', \(\) => vscode\.postMessage\(\{ type: 'editBars' \}\)\);/);
        assert.match(scan, /executeCommand\('lilysharp\.editBarsBesideScan', lys\.toString\(\), 'marks'\)/);
        assert.doesNotMatch(scan, /marks with AI…/);
        assert.match(fix, /let targets = marks \? marked\.filter\(t => checkedByDefault\(t, threshold\)\)/);
    });
});
