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

// Owner's report 2026-10-26: the language server restarted while an AI candidate was being
// decided, and the panel's Accept / Ask Again / Reject were left greyed out for good. Whatever
// fails after a decision, the panel comes back to a candidate with its buttons armed.
describe('the AI candidate panel is never left without a decision to make', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');

    it('an Accept that throws says nothing was written and waits for Accept again', () => {
        assert.match(transform, /try \{\s*again = await accept\(candidate\);\s*\} catch \(err: any\) \{[\s\S]{0,300}?again = `Not written \(\$\{err\?\.message \?\? err\}\) — Accept to try again\.`;/);
    });

    it('a failed request on the way back to relative octaves throws to Accept, not to the absolute-octaves question', () => {
        const from = transform.slice(transform.indexOf('async function fromAbsoluteOctaves'));
        const body = from.slice(0, from.search(/\r?\n\}\r?\n/));
        assert.match(body, /const resp = await client\.sendRequest<AiOctavesResponse>\('lilysharp\/aiOctaves'/);
        assert.doesNotMatch(body, /catch \(err/);
    });

    it('an Ask Again round that fails or brings nothing goes back to the candidate shown', () => {
        assert.match(transform, /try \{\s*round = await working\(title,/);
        assert.match(transform, /\} catch \(err: any\) \{\s*if \(!shownRound \|\| !candidatePanel\) \{\s*throw err;\s*\}/);
        // The conversation as it was before the ask, the candidate decided again.
        assert.match(transform, /prepared!\.messages\.length = askedFrom;\s*round = shownRound;/);
        assert.match(transform, /askedFrom = messages\.length;\s*messages\.push\(\{ role: 'assistant', content: candidate\.reply \}\);/);
        // A message waiting means decideAgain, which re-arms the buttons ('undecided').
        assert.match(transform, /decision = again !== undefined\s*\? await decideAgain\(again\)/);
        assert.match(transform, /postMessage\(\{ type: 'undecided', text \}\)/);
    });
});
