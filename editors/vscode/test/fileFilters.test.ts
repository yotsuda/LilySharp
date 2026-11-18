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
import { importFilters } from '../src/fileFilters';
import { OMR_INPUT_EXTENSIONS } from '../src/omrCore';
import { DETTATO_INPUTS } from '../src/dettatoCore';

// Owner's request 2026-11-18: the scans' and the recordings' pickers offer every kind, then each
// kind alone, as Import MusicXML, MIDI or ABC… does.
describe('the import pickers\' filters', () => {
    it('offer every scan kind, then each, a kind\'s spellings under one name', () => {
        assert.deepEqual(importFilters('Scans and PDF', OMR_INPUT_EXTENSIONS), {
            'Scans and PDF': ['png', 'jpg', 'jpeg', 'bmp', 'tif', 'tiff', 'pdf'],
            PNG: ['png'], JPEG: ['jpg', 'jpeg'], BMP: ['bmp'], TIFF: ['tif', 'tiff'], PDF: ['pdf'],
        });
    });
    it('offer every recording kind, then each — what the reader says it reads, in its order', () => {
        assert.deepEqual(Object.keys(importFilters('Recordings', DETTATO_INPUTS)),
            ['Recordings', 'WAV', 'MP3', 'M4A', 'MP4', 'AAC', 'FLAC', 'WMA']);
        assert.deepEqual(importFilters('Recordings', ['WAV', 'ogg']), { Recordings: ['wav', 'ogg'], WAV: ['wav'], OGG: ['ogg'] });
    });
    it('have no second filter for a single kind', () => {
        assert.deepEqual(importFilters('PDF', ['pdf']), { PDF: ['pdf'] });
    });
    it('are what both pickers use', () => {
        const src = (f: string) => fs.readFileSync(path.join(__dirname, '..', '..', 'src', f), 'utf8');
        assert.match(src('omrImport.ts'), /filters: importFilters\('Scans and PDF', OMR_INPUT_EXTENSIONS\)/);
        assert.match(src('dettato.ts'), /filters: importFilters\('Recordings', reader\.inputs\)/);
    });
});
