// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import { CantoriaApi, SungVoiceDto } from '../src/singerCore';
import { CantoriaExtensionBackend, VoiceRenderer } from '../src/singer';

// Owner's report 2026-11-13 (kaze.lys): the microphone pulsed for 24 s with no bar under Play.
// The file opened, and a second prepare followed the first within a second: the first job, whose
// render the engine was on, was replaced and said no more to the page; the one that replaced it
// waited, found the voice sung and never said "singing" either. The host now shows the bar by
// the voice's KEY - so the renderer says when the engine begins on a key, and whether it is on one.

const alto: SungVoiceDto = { Part: 'alto', Language: 'ja', Notes: [{ T: 0, D: 1, P: 60, L: 'あ' }], Singer: 'neutrino/REINA', Gender: 'female' };

describe('a renderer says which voice the engine is on', () => {
    it('when it begins on a key, once, and for as long as it sings it', async () => {
        let land: (v: { wav: string; duration: number }) => void = () => undefined;
        const api: CantoriaApi = {
            version: '0.0.0', apiVersion: 1,
            status: async () => ({ ready: true, voice: 'neutrino/MERROW', problems: [] }),
            sing: () => new Promise(resolve => { land = resolve; }),
            release: () => undefined,
        };
        const renderer = new VoiceRenderer(new CantoriaExtensionBackend(api));
        const begun: string[] = [];
        renderer.onSinging = key => begun.push(key);

        const first = await renderer.render(alto, 1);
        assert.equal(first.cached, false);
        assert.deepEqual(begun, [first.key]);
        assert.equal(renderer.isSinging(first.key), true);

        // A second ask for the same text joins the render in flight: the engine did not begin again.
        const second = await renderer.render(alto, 1);
        assert.equal(second.key, first.key);
        assert.deepEqual(begun, [first.key]);

        land({ wav: __filename, duration: 1 });
        await first.rendered;
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(renderer.isSinging(first.key), false);
        // …and sung, it is not begun on again.
        assert.equal((await renderer.render(alto, 1)).cached, true);
        assert.deepEqual(begun, [first.key]);
    });
});
