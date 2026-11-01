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

import { strict as assert } from 'node:assert';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { describe, it } from 'node:test';
import { reviewPointsOf } from '../src/reviewCore';

const report = (findings: unknown[], compiles = true) => JSON.stringify({ compiles, findings });
const text = 'title "T"\n\n\npart fl { instrument flute }\n  part vc { instrument cello }\n';

describe('the review in the Problems panel', () => {
    it('says the kind, the finding, why, when it is fine, and what to ask', () => {
        const { points } = reviewPointsOf(report([{ kind: 'style', what: 'One rhythm.', why: 'A line lives on it.', fine_when: 'A chorale.', ask: 'Meant?' }]), text);
        assert.equal(points[0].message, '[style] One rhythm.\nWhy: A line lives on it.\nFine when: A chorale.\nAsk: Meant?');
    });
    it('leaves out what a finding does not have', () => {
        assert.equal(reviewPointsOf(report([{ kind: 'idea', what: 'Longer.' }]), text).points[0].message, '[idea] Longer.');
    });
    it('puts a point on the line the check named, as a line of the file counts from 0', () => {
        assert.equal(reviewPointsOf(report([{ kind: 'notation', what: 'x', where: { line: 3, parts: ['vc'] } }]), text).points[0].line, 2);
    });
    it('puts a point with no line on its first part\'s declaration', () => {
        assert.equal(reviewPointsOf(report([{ kind: 'style', what: 'x', where: { parts: ['fl'] } }]), text).points[0].line, 3);
        assert.equal(reviewPointsOf(report([{ kind: 'style', what: 'x', where: { parts: ['vc'] } }]), text).points[0].line, 4);
    });
    it('does not take a longer name for the part', () => {
        const longer = 'part vcl { }\npart vc { }\n';
        assert.equal(reviewPointsOf(report([{ kind: 'style', what: 'x', where: { parts: ['vc'] } }]), longer).points[0].line, 1);
    });
    it('reads a part\'s name as a name, whatever it holds', () => {
        assert.equal(reviewPointsOf(report([{ kind: 'style', what: 'x', where: { parts: ['prog (chords)'] } }]), text).points[0].line, 0);
    });
    it('finds a part named in any script, and not by its beginning', () => {
        const named = 'part 旋律二 { }\r\npart 旋律 { }\r\n';
        assert.equal(reviewPointsOf(report([{ kind: 'style', what: 'x', where: { parts: ['旋律'] } }]), named).points[0].line, 1);
    });
    it('puts a point about the piece at the top', () => {
        assert.equal(reviewPointsOf(report([{ kind: 'idea', what: 'x', where: { parts: [] } }]), text).points[0].line, 0);
    });
    it('says when the score does not compile, which has no findings to show', () => {
        const none = reviewPointsOf(JSON.stringify({ compiles: false, errors: [] }), text);
        assert.equal(none.compiles, false);
        assert.deepEqual(none.points, []);
    });
    it('is a command of the editor, and a score\'s own', () => {
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"command": "lilysharp\.reviewScore",\s*"category": "Lily#",\s*"title": "Review Score"/);
        assert.match(pkg, /"command": "lilysharp\.reviewScore",\s*"when": "resourceLangId == lilysharp"/);
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /registerCommand\('lilysharp\.reviewScore', \(\) => reviewScoreCommand\(context\)\)/);
        assert.match(ext, /createDiagnosticCollection\('Lily# review'\)/);
    });
});
