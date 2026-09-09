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

// The editor-free half of the Markdown lys fence. Run with `npm test`.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import {
    FenceCache, PIXELS_PER_STAFF_SPACE, fenceHtml, fenceKey, fenceLanguage, inlineSvg, isLysFence,
} from '../src/markdownFenceCore';

describe('the core is editor-free', () => {
    it('never imports vscode', () => {
        const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'markdownFenceCore.ts'), 'utf8');
        assert.doesNotMatch(source, /from 'vscode'/);
    });
});

describe('which fences are scores', () => {
    it('are ```lys and ```lily#, whatever the case and whatever follows', () => {
        assert.ok(isLysFence('lys'));
        assert.ok(isLysFence('LYS'));
        assert.ok(isLysFence('lily#'));
        assert.ok(isLysFence('Lily#'));
        assert.ok(isLysFence('  lys title="x" '));
        assert.equal(fenceLanguage(' lys  extra'), 'lys');
    });

    it('are not lilysharp, ly, or a fence with no language', () => {
        assert.ok(!isLysFence('lilysharp'));
        assert.ok(!isLysFence('ly'));
        assert.ok(!isLysFence('lilypond'));
        assert.ok(!isLysFence(''));
        assert.equal(fenceLanguage(''), '');
    });
});

describe('the cache key', () => {
    it('is the text, exactly', () => {
        assert.equal(fenceKey('c4 d e f |'), fenceKey('c4 d e f |'));
        assert.notEqual(fenceKey('c4 d e f |'), fenceKey('c4 d e g |'));
        assert.notEqual(fenceKey('c4\n'), fenceKey('c4'));
    });
});

describe('the HTML of a fence', () => {
    const code = 'part m { section A { c4 <d> } }';

    it('inlines the SVG without its XML declaration', () => {
        const svg = '<?xml version="1.0" encoding="UTF-8"?>\n<svg xmlns="http://www.w3.org/2000/svg"><style>.music{}</style><rect/></svg>';
        assert.equal(inlineSvg(svg), '<svg xmlns="http://www.w3.org/2000/svg"><style>.music{}</style><rect/></svg>');
        const html = fenceHtml({ kind: 'svg', svg }, code);
        assert.ok(html.startsWith('<div class="lys-fence"><svg'));
        assert.ok(!html.includes('<?xml'));
        assert.ok(!html.includes('&lt;d&gt;'), 'the source is not shown once the picture is');
    });

    it('sizes the picture from its viewBox, at the fence\'s pixels per staff space', () => {
        // The server's header: 10 px per staff space (477.6 × 274.5 for 47.76 × 27.45).
        const svg = '<?xml version="1.0"?>\n<svg xmlns="http://www.w3.org/2000/svg" width="477.6" height="274.5" viewBox="0 0 47.76 27.45" font-family="x, serif">\n<rect/></svg>';
        const inline = inlineSvg(svg);
        assert.ok(inline.startsWith('<svg xmlns="http://www.w3.org/2000/svg" width="286.6" height="164.7" viewBox="0 0 47.76 27.45"'), inline);
        assert.ok(inline.endsWith('<rect/></svg>'));
        assert.equal(PIXELS_PER_STAFF_SPACE, 6);
    });

    it('keeps the source in view while pending, escaped', () => {
        const html = fenceHtml({ kind: 'pending' }, code);
        assert.ok(html.includes('lys-fence-pending'));
        assert.ok(html.includes('rendering'));
        assert.ok(html.includes('&lt;d&gt;'));
        assert.ok(!html.includes('<d>'));
    });

    it('names the error and keeps the source on failure', () => {
        const html = fenceHtml({ kind: 'error', message: 'Line 1, Col 3: <bad>' }, code);
        assert.ok(html.includes('lys-fence-error'));
        assert.ok(html.includes('Line 1, Col 3: &lt;bad&gt;'));
        assert.ok(html.includes('&lt;d&gt;'));
    });
});

describe('the cache', () => {
    it('keeps the newest entries and drops the oldest past its capacity', () => {
        const cache = new FenceCache(2);
        cache.set('a', { kind: 'pending' });
        cache.set('b', { kind: 'pending' });
        cache.set('a', { kind: 'svg', svg: '<svg/>' });   // refreshed: a is the newest now
        cache.set('c', { kind: 'pending' });
        assert.equal(cache.size, 2);
        assert.equal(cache.get('b'), undefined, 'b was the oldest');
        assert.equal(cache.get('a')?.kind, 'svg');
        assert.equal(cache.get('c')?.kind, 'pending');
    });
});

describe('the manifest', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));

    it('contributes the markdown-it plugin, the preview stylesheet and the fence grammar', () => {
        // The DOTTED keys, as the markdown extension reads them
        // (contributes["markdown.markdownItPlugins"]); a nested "markdown": {…} object
        // is silently ignored — measured on 2026-09-09: the plugin was never installed.
        assert.equal(manifest.contributes['markdown.markdownItPlugins'], true);
        assert.equal(manifest.contributes.markdown, undefined, 'no nested markdown object');
        const styles: string[] = manifest.contributes['markdown.previewStyles'];
        assert.ok(styles.some(s => fs.existsSync(path.join(__dirname, '..', '..', s))), 'the stylesheet exists');
        const injection = manifest.contributes.grammars.find((g: { injectTo?: string[] }) => g.injectTo?.includes('text.html.markdown'));
        assert.ok(injection, 'a grammar is injected into markdown');
        assert.equal(injection.embeddedLanguages['meta.embedded.block.lilysharp'], 'lilysharp');
        assert.ok(fs.existsSync(path.join(__dirname, '..', '..', injection.path)));
        assert.ok(manifest.activationEvents.includes('onLanguage:markdown'));
    });

    it('injects on the same fence words the plugin renders', () => {
        const injection = manifest.contributes.grammars.find((g: { injectTo?: string[] }) => g.injectTo?.includes('text.html.markdown'));
        const grammar = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', injection.path), 'utf8'));
        const begin: string = grammar.repository['lys-code-block'].begin;
        for (const word of ['lys', 'lily#']) {
            assert.ok(begin.includes(word), `${word} in the fence grammar`);
        }
        assert.ok(!begin.includes('lilysharp'), 'the retired alias is gone from the grammar');
    });
});
