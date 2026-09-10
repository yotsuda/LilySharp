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

// The editor-free half of the Explorer batch export. Run with `npm test`.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { EXPORT_FORMATS, duplicateStems, lysTargets, samePath, summarizeExport } from '../src/exportBatchCore';

describe('the core is editor-free', () => {
    it('never imports vscode', () => {
        const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'exportBatchCore.ts'), 'utf8');
        assert.doesNotMatch(source, /from 'vscode'/);
    });
});

describe('the formats the submenu offers', () => {
    it('are declared as commands in package.json, each exactly once, in the menu', () => {
        const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const declared = new Set<string>(manifest.contributes.commands.map((c: { command: string }) => c.command));
        const inMenu: string[] = manifest.contributes.menus['lilysharp.export'].map((m: { command: string }) => m.command);
        for (const format of EXPORT_FORMATS) {
            assert.ok(declared.has(format.command), `${format.command} is not a declared command`);
            assert.equal(inMenu.filter(c => c === format.command).length, 1, `${format.command} in the submenu`);
        }
        assert.equal(inMenu.length, EXPORT_FORMATS.length, 'the submenu holds nothing else');
        assert.equal(new Set(EXPORT_FORMATS.map(f => f.id)).size, EXPORT_FORMATS.length, 'format ids are unique');
    });

    it('hang the submenu on .lys files in the Explorer', () => {
        const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const entry = manifest.contributes.menus['explorer/context']
            .find((m: { submenu?: string }) => m.submenu === 'lilysharp.export');
        assert.ok(entry, 'the explorer context menu carries the export submenu');
        assert.equal(entry.when, 'resourceExtname == .lys');
        assert.ok(manifest.contributes.submenus.some((s: { id: string }) => s.id === 'lilysharp.export'));
    });
});

describe('the targets of a selection', () => {
    it('are the .lys files, in order, whatever else was selected', () => {
        assert.deepEqual(
            lysTargets(['C:\\s\\a.lys', 'C:\\s\\README.md', 'C:\\s\\folder', 'C:\\s\\b.LYS', 'C:\\s\\c.lys.bak']),
            ['C:\\s\\a.lys', 'C:\\s\\b.LYS']);
    });

    it('name a file once even when it is both clicked and selected', () => {
        assert.deepEqual(lysTargets(['C:\\s\\a.lys', 'C:\\s\\A.lys', 'C:\\s\\a.lys'], true), ['C:\\s\\a.lys']);
        assert.deepEqual(lysTargets(['/s/a.lys', '/s/A.lys'], false), ['/s/a.lys', '/s/A.lys']);
    });

    it('are empty for an empty or score-less selection', () => {
        assert.deepEqual(lysTargets([]), []);
        assert.deepEqual(lysTargets(['C:\\s\\notes.txt']), []);
    });
});

describe('name collisions in one output folder', () => {
    // The stem is path.basename's, which splits on the HOST's separator only: a
    // backslash is a legal file-name character on Linux, so these paths are joined
    // for the host — CI's ubuntu leg read 'C:\a\song.lys' as one stem, no duplicate.
    const p = (...parts: string[]) => path.join(...parts);

    it('are the stems two or more targets share', () => {
        const dups = duplicateStems([p('a', 'song.lys'), p('b', 'song.lys'), p('a', 'other.lys')], true);
        assert.deepEqual([...dups.entries()], [['song', [p('a', 'song.lys'), p('b', 'song.lys')]]]);
    });

    it('fold letter case the way the destination does', () => {
        assert.equal(duplicateStems([p('a', 'Song.lys'), p('b', 'song.lys')], true).size, 1);
        assert.equal(duplicateStems([p('a', 'Song.lys'), p('b', 'song.lys')], false).size, 0);
    });

    it('are none for distinct stems', () => {
        assert.equal(duplicateStems([p('a', 'x.lys'), p('a', 'y.lys')]).size, 0);
    });
});

describe('the completion line', () => {
    const tally = { total: 3, attempted: 3, failed: 0, outputs: 5, overwritten: 0, cancelled: false };

    it('counts the outputs, not the inputs', () => {
        assert.equal(summarizeExport(tally, 'PDF', 'C:\\out'), 'Lily#: 5 PDF files written to C:\\out.');
        assert.equal(summarizeExport({ ...tally, outputs: 1 }, 'SVG', 'C:\\out'), 'Lily#: 1 SVG file written to C:\\out.');
    });

    it('names failures, overwrites and a cancellation', () => {
        assert.equal(
            summarizeExport({ ...tally, failed: 1, overwritten: 2, attempted: 2, cancelled: true }, 'PDF', 'C:\\out'),
            'Lily#: 5 PDF files written to C:\\out; 1 of 2 score files failed; '
            + '2 overwritten by a later file with the same name; stopped after 2 of 3.');
    });
});

describe('samePath', () => {
    it('folds case only where the file system does', () => {
        assert.ok(samePath('C:\\A.lys', 'c:\\a.lys', true));
        assert.ok(!samePath('/A.lys', '/a.lys', false));
    });
});
