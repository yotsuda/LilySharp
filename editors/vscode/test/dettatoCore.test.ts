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
import {
    DETTATO_INPUTS, chartForConversion, dettatoArgs, dettatoFailure, dettatoTooOld, locateDettato,
} from '../src/dettatoCore';

describe('Dettato, the recording reader', () => {
    it('is found by the variable first, then by its extension when its reader is on disk', () => {
        const ext = { path: 'C:\\x\\bin\\dettato.exe', version: '0.2.1', protocol: 1, inputs: ['WAV', 'mp3'] };
        assert.deepEqual(locateDettato({ LILYSHARP_DETTATO_PATH: ' D:\\dev\\dettato.exe ' }, ext, () => true),
            { path: 'D:\\dev\\dettato.exe', source: 'environment', inputs: DETTATO_INPUTS });
        assert.deepEqual(locateDettato({}, ext, () => true),
            { path: 'C:\\x\\bin\\dettato.exe', source: 'extension', version: '0.2.1', protocol: 1, inputs: ['wav', 'mp3'] });
        assert.equal(locateDettato({}, ext, () => false), undefined);
        assert.equal(locateDettato({}, undefined), undefined);     // not installed, or not Windows x64
        assert.equal(locateDettato({}, { version: '1' }), undefined);
    });

    it('is offered its update when older than 0.2.0 or speaking another protocol', () => {
        const at = (version?: string, protocol?: number) => ({ path: 'p', source: 'extension' as const, version, protocol, inputs: [] });
        assert.equal(dettatoTooOld(at('0.2.0', 1)), undefined);
        assert.equal(dettatoTooOld(at('0.10.0', 1)), undefined);
        assert.match(dettatoTooOld(at('0.1.0', 1))!, /older/);
        assert.match(dettatoTooOld(at('0.3.0', 2))!, /protocol 2/);
        assert.equal(dettatoTooOld({ path: 'p', source: 'environment', inputs: [] }), undefined);
    });

    it('is called with the recording and the chart, --force only when the person agreed to replace it', () => {
        assert.deepEqual(dettatoArgs('C:\\a b\\曲.m4a', 'C:\\a b\\曲.lys', false), ['C:\\a b\\曲.m4a', '-o', 'C:\\a b\\曲.lys']);
        assert.deepEqual(dettatoArgs('x.wav', 'x.lys', true), ['x.wav', '-o', 'x.lys', '--force']);
        assert.equal(chartForConversion('C:\\songs\\take 1.mp3'), 'C:\\songs\\take 1.lys');
        assert.equal(chartForConversion('/a.b/rec'), '/a.b/rec.lys');
    });

    it('says why it did not write the chart in its own words, a broken reader as broken', () => {
        assert.equal(dettatoFailure(1, 'listening to x.mp3\nthe beats\ndettato: x.mp3 is shorter than 5 seconds\n'),
            'Dettato: x.mp3 is shorter than 5 seconds');
        assert.equal(dettatoFailure(3, 'dettato: the models are missing'), 'Dettato\'s reader is broken: the models are missing');
        assert.equal(dettatoFailure(1, ''), 'Dettato: it stopped with exit code 1');
    });

    it('takes the recordings the Explorer offers it for — the same list as package.json\'s right-click', () => {
        const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const item = pkg.contributes.menus['explorer/context'].find((m: { command: string }) => m.command === 'lilysharp.convertRecording');
        assert.ok(item, 'the right-click item');
        for (const ext of DETTATO_INPUTS) {
            assert.match(item.when, new RegExp(`\\b${ext}\\b`), ext);
        }
        assert.match(item.when, /isWindows/);
        // Shown without the reader too: the command offers it (owner's decision 2026-11-15).
        assert.doesNotMatch(item.when, /dettatoAvailable/);
    });
});
