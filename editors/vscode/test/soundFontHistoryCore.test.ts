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

// The sound fonts Choose Sound Font… remembers: most recent first, five at most, a missing
// file hidden rather than forgotten.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import { HISTORY_MAX, cleanHistory, forgetFont, recordFont, samePath, shownFonts } from '../src/soundFontHistoryCore';

const A = 'C:\\Users\\me\\Sound fonts\\MuseScore_General.sf3';
const B = 'D:\\Fonts\\FluidR3Mono_GM.sf3';
const C = 'C:\\Users\\me\\Downloads\\GeneralUser-GS.sf2';

describe('recordFont', () => {
    it('puts the font used last first', () => {
        assert.deepEqual(recordFont([A, B], C, true), [C, A, B]);
    });
    it('moves a font already there to the front instead of listing it twice', () => {
        assert.deepEqual(recordFont([A, B, C], B, true), [B, A, C]);
    });
    it('on Windows, the same file in another case or with other slashes is the same font', () => {
        assert.deepEqual(recordFont([A, B], 'c:/users/ME/sound fonts/musescore_general.SF3', true),
            ['c:/users/ME/sound fonts/musescore_general.SF3', B]);
    });
    it('where file names keep their case, two cases are two fonts', () => {
        assert.deepEqual(recordFont(['/fonts/a.sf2'], '/fonts/A.sf2', false), ['/fonts/A.sf2', '/fonts/a.sf2']);
    });
    it(`keeps at most ${HISTORY_MAX}: the oldest falls off`, () => {
        const five = ['1.sf2', '2.sf2', '3.sf2', '4.sf2', '5.sf2'];
        assert.deepEqual(recordFont(five, '6.sf2', true), ['6.sf2', '1.sf2', '2.sf2', '3.sf2', '4.sf2']);
    });
    it('an empty path changes nothing (the default is not a file)', () => {
        assert.deepEqual(recordFont([A], '  ', true), [A]);
    });
});

describe('forgetFont', () => {
    it('removes the font from the list, in any case on Windows', () => {
        assert.deepEqual(forgetFont([A, B, C], B.toUpperCase(), true), [A, C]);
    });
});

describe('shownFonts', () => {
    it('hides a font whose file is not there now, and keeps it in the history', () => {
        const history = [A, B, C];
        const shown = shownFonts(history, f => f !== B, true);
        assert.deepEqual(shown, [A, C]);
        assert.deepEqual(history, [A, B, C]);   // a removable drive comes back
    });
    it('does not list twice a font the picker shows as the default (the OS bank)', () => {
        const gm = 'C:\\Windows\\System32\\drivers\\gm.dls';
        assert.deepEqual(shownFonts([gm, A], () => true, true, ['c:\\windows\\system32\\drivers\\GM.DLS']), [A]);
    });
});

describe('samePath', () => {
    it('ignores surrounding space and slash direction', () => {
        assert.ok(samePath(' C:\\a\\b.sf2 ', 'C:/a/b.sf2', false));
    });
});

describe('cleanHistory', () => {
    it('reads back only non-empty strings, without duplicates, at most five', () => {
        assert.deepEqual(cleanHistory([A, 3, '', A.toLowerCase(), B, null, C, '4', '5', '6'], true), [A, B, C, '4', '5']);
    });
    it('reads nothing from a state that is not a list', () => {
        assert.deepEqual(cleanHistory({ a: 1 }, true), []);
        assert.deepEqual(cleanHistory(undefined, true), []);
    });
});
