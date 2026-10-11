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

// The preview's page script lives inside a TypeScript TEMPLATE LITERAL in
// src/extension.ts (getWebviewContent), so it is a string to tsc and esbuild:
// a syntax error in it passes both and surfaces only when the preview opens,
// as a page that stays on "Loading preview". Two spellings that are fine in a
// .js file are not fine there — a backtick in a comment ends the template, and
// a regex backslash is the template's own escape (\s becomes s, \/ becomes /,
// which ended a regex early and took the whole script down on 2026-09-04).
// This evaluates the template as TypeScript will and parses every <script>
// with V8.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vm from 'node:vm';

describe('the preview page script', () => {
    it('parses once the template literal that carries it has been evaluated', () => {
        const source = fs.readFileSync(
            path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        const open = 'return `<!DOCTYPE html>';
        const start = source.indexOf(open) + 'return '.length;
        const end = source.indexOf('</html>`;', start) + '</html>`'.length;
        assert.ok(start > 'return '.length && end > start, 'the webview template was not found');
        // The template's own substitutions (the theme, the nonce…) are not what
        // is under test; any word stands in for them.
        const template = source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X');
        const html = new vm.Script(template).runInNewContext({}) as string;
        const scripts = [...html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)];
        assert.ok(scripts.length >= 2, 'expected the page to carry its scripts');
        for (const [, body] of scripts) {
            assert.doesNotThrow(() => new vm.Script(body), 'the page script does not parse');
        }
    });
});

// The page's offsets read back after a group reconcile (offsetsDisagree): the guard that
// replaces a page whose reused groups kept a stale data-pos (owner report 2026-10-11 —
// selecting a note lit another one until the preview was reopened). The DOM is not
// available here, so the function is taken out of the evaluated page script and handed
// pages made of plain objects: what it reads is each element's attributes, in order.
describe('the preview reads its offsets back after a reconcile', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = source.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
    const head = 'function offsetsDisagree(';
    const at = html.indexOf(head);
    let depth = 0, close = -1;
    for (let i = html.indexOf('{', at); i < html.length; i++) {
        if (html[i] === '{') depth++;
        else if (html[i] === '}' && --depth === 0) { close = i + 1; break; }
    }
    const offsetsDisagree = new vm.Script('(' + html.slice(at, close) + ')').runInNewContext({}) as
        (page: unknown, markup: string) => string | null;

    type Attr = { name: string; value: string };
    const page = (...els: Attr[][]) => ({ querySelectorAll: () => els.map(attributes => ({ attributes })) });
    const pos = (v: string): Attr => ({ name: 'data-pos', value: v });
    const alt = (v: string): Attr => ({ name: 'data-alt', value: v });
    const markup = '<g><path d="M0" data-pos="12"/><rect data-pos="30" data-alt="31 40"/><text class="x">a</text></g>';

    it('finds the function in the page script', () => {
        assert.ok(at >= 0 && close > at, 'offsetsDisagree is not in the page script');
    });
    it('says nothing when every offset agrees, data-alt included, in attribute order', () => {
        assert.equal(offsetsDisagree(page([pos('12')], [pos('30'), alt('31 40')]), markup), null);
    });
    it('names the first offset a reused group kept stale', () => {
        const why = offsetsDisagree(page([pos('12')], [pos('38'), alt('31 40')]), markup);
        assert.match(why ?? '', /#1 drawn data-pos=38, markup data-pos=30/);
    });
    it('notices an element too many or too few', () => {
        assert.notEqual(offsetsDisagree(page([pos('12')]), markup), null);
        assert.notEqual(offsetsDisagree(page([pos('12')], [pos('30'), alt('31 40')], [pos('99')]), markup), null);
    });
});
