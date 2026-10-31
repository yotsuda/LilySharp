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
import { joinStarts, parseStarts, todoDeletions, todoStartsInBar } from '../src/todoResolveCore';

// `c4@todo d e@todo("x") f | g4 a b c |` — two marks in the first bar, none in the second.
const marks = [
    { Start: 2, End: 7, HostStart: 0 },
    { Start: 11, End: 22, HostStart: 10 },
];

describe('the preview\'s Resolve TODO doors (owner\'s request 2026-10-31)', () => {
    it('finds the marks whose host is written in the bar right-clicked', () => {
        assert.deepEqual(todoStartsInBar(marks, 0, 27), [2, 11]);
        assert.deepEqual(todoStartsInBar(marks, 10, 27), [11]);
        assert.deepEqual(todoStartsInBar(marks, 28, 40), []);
        assert.deepEqual(todoStartsInBar(marks, 5, 5), []);          // no span
    });
    it('carries the starts through the menu context as one string', () => {
        assert.equal(joinStarts([2, 11]), '2,11');
        assert.deepEqual(parseStarts('2,11'), [2, 11]);
        assert.deepEqual(parseStarts(''), []);
        assert.deepEqual(parseStarts(undefined), []);
        assert.deepEqual(parseStarts('2,x'), []);
    });
    it('deletes the marks asked for off the server\'s current list, right to left', () => {
        assert.deepEqual(todoDeletions(marks, [2, 11]), [{ start: 11, end: 22 }, { start: 2, end: 7 }]);
        assert.deepEqual(todoDeletions(marks, [11]), [{ start: 11, end: 22 }]);
        assert.deepEqual(todoDeletions(marks, [99]), []);            // moved since the click: not cut blind
        assert.deepEqual(todoDeletions(marks, null), [{ start: 11, end: 22 }, { start: 2, end: 7 }]);
    });
    it('is wired: the marks go to the page with each render, the bar\'s marks into the menu, two commands', () => {
        const root = path.join(__dirname, '..', '..');
        const ext = fs.readFileSync(path.join(root, 'src', 'extension.ts'), 'utf8');
        const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
        assert.match(ext, /type: 'todos', marks: /);
        assert.match(ext, /lysBarTodo: ctxTodoStarts\.length > 0,\s*lysTodoStarts: ctxTodoStarts\.join\(','\),\s*lysHasTodos: todoMarks\.length > 0,/);
        assert.match(ext, /registerCommand\('lilysharp\.previewResolveTodo'/);
        assert.match(ext, /registerCommand\('lilysharp\.previewResolveAllTodos'/);
        const menu = pkg.contributes.menus['webview/context'] as { command: string; when: string; group: string }[];
        const one = menu.find(m => m.command === 'lilysharp.previewResolveTodo');
        const all = menu.find(m => m.command === 'lilysharp.previewResolveAllTodos');
        assert.equal(one?.when, "webviewId == 'lilysharpPreview' && lysBarTodo");
        assert.equal(all?.when, "webviewId == 'lilysharpPreview' && !lysOnStaff && lysHasTodos");
        assert.equal(one?.group, '1_edit@6');
        assert.equal(all?.group, '1_edit@7');
        const commands = pkg.contributes.commands as { command: string; title: string }[];
        assert.equal(commands.find(c => c.command === 'lilysharp.previewResolveTodo')?.title, 'Resolve This TODO (remove @todo)');
        assert.equal(commands.find(c => c.command === 'lilysharp.previewResolveAllTodos')?.title, 'Resolve All TODOs');
        // Not in the palette: the marks the menu names are the page's.
        const palette = pkg.contributes.menus['commandPalette'] as { command: string; when: string }[];
        assert.equal(palette.find(m => m.command === 'lilysharp.previewResolveTodo')?.when, 'false');
        assert.equal(palette.find(m => m.command === 'lilysharp.previewResolveAllTodos')?.when, 'false');
    });
});
