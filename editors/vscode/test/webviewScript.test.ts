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
