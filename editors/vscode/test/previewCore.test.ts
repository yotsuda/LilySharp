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
import { svgPostKey, pagesSummary, previewOnEditorShown, sourceGroupToReveal, sourceGroupToOpen, newPreviewColumn, scoreGroupFor, ShownEditor } from '../src/previewCore';

describe('a score opened into a preview\'s group', () => {
    it('moves to the leftmost group with no preview', () => {
        assert.equal(scoreGroupFor(2, [{ column: 1, hasPreview: false }, { column: 2, hasPreview: true }]), 1);
        assert.equal(scoreGroupFor(3, [{ column: 1, hasPreview: true }, { column: 2, hasPreview: false },
            { column: 3, hasPreview: true }, { column: 4, hasPreview: false }]), 2);
    });

    it('moves to a new group left of the preview when every group holds one', () => {
        assert.equal(scoreGroupFor(1, [{ column: 1, hasPreview: true }]), 'left');
    });

    it('stays where it opened in a group without a preview', () => {
        assert.equal(scoreGroupFor(1, [{ column: 1, hasPreview: false }, { column: 2, hasPreview: true }]), undefined);
    });
});

describe('the page answer summary', () => {
    it('counts the pages by what they did and the characters that travelled', () => {
        const summary = pagesSummary({
            Version: 7, BaseVersion: 5, Head: '<svg>', Tail: '</svg>',
            Window: { Prefix: 10, SuffixStart: 10, Delta: 1 },
            Items: [
                { Change: 'same' }, { Change: 'shifted' }, { Change: 'changed', Markup: '<g class="page">…</g>' },
            ],
        });
        assert.equal(summary, '3 (delta v5->v7: same 1, shifted 1, changed 1; 32 chars)');
    });

    it('says a full answer is one', () => {
        const summary = pagesSummary({
            Version: 1, BaseVersion: null, Head: '', Tail: '',
            Items: [{ Change: 'changed', Markup: 'ab' }, { Change: 'changed', Markup: 'cd' }],
        });
        assert.equal(summary, '2 (full v1: same 0, shifted 0, changed 2; 4 chars)');
    });
});

describe('the svg post key', () => {
    const svg = '<svg/>';
    const main = { Name: 'main', Type: 'score', Filename: '' };
    const sub = { Name: 'S', Type: 'score', Filename: 'S' };

    it('is the same for the same picture, banner, list and drawn score', () => {
        assert.equal(svgPostKey(svg, null, [main], ''), svgPostKey(svg, undefined, [main], ''));
    });

    it('changes when a score is added to the list although the picture did not change', () => {
        // The 2026-09-11 report: a `score { }` pasted below the drawn one left the picture
        // identical, and the skipped post left the picker without the new score.
        assert.notEqual(svgPostKey(svg, null, [main], ''), svgPostKey(svg, null, [main, sub], ''));
    });

    it('changes when the drawn score changes, when the banner changes, and when the picture changes', () => {
        assert.notEqual(svgPostKey(svg, null, [main, sub], ''), svgPostKey(svg, null, [main, sub], 'S'));
        assert.notEqual(svgPostKey(svg, null, [main], ''), svgPostKey(svg, 'Line 1: oops', [main], ''));
        assert.notEqual(svgPostKey(svg, null, [main], ''), svgPostKey('<svg><g/></svg>', null, [main], ''));
    });

    it('treats a missing list as empty', () => {
        assert.equal(svgPostKey(svg, null, null, ''), svgPostKey(svg, null, [], ''));
    });
});

describe('an editor coming to the front', () => {
    const opened: ShownEditor = {
        languageId: 'lilysharp', scheme: 'file', inDiff: false, hasOwnPreview: false,
        hasFollower: false, openOnOpen: true, firstShow: true, dismissed: false, followerLocked: false,
    };

    it('opens the second preview on a tab switch once the follower was locked to compare', () => {
        assert.equal(previewOnEditorShown({ ...opened, firstShow: false, followerLocked: true }), 'open');
        assert.equal(previewOnEditorShown({ ...opened, firstShow: false, followerLocked: true, dismissed: true }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, firstShow: false, followerLocked: true, inDiff: true }), 'none');
    });

    it('opens a preview when a .lys file is opened and none is open', () => {
        assert.equal(previewOnEditorShown(opened), 'open');
        // New Score's untitled buffer is an opening too.
        assert.equal(previewOnEditorShown({ ...opened, scheme: 'untitled' }), 'open');
    });

    it('turns the follower to it instead of opening a second preview', () => {
        assert.equal(previewOnEditorShown({ ...opened, hasFollower: true }), 'follow');
        // ...on a tab switch too, and with the opening setting off: following is the follower's own nature.
        assert.equal(previewOnEditorShown({ ...opened, hasFollower: true, firstShow: false, openOnOpen: false }), 'follow');
        assert.equal(previewOnEditorShown({ ...opened, hasFollower: true, scheme: 'untitled' }), 'follow');
    });

    it('leaves a file that has a preview of its own alone', () => {
        assert.equal(previewOnEditorShown({ ...opened, hasOwnPreview: true }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, hasOwnPreview: true, hasFollower: true }), 'none');
    });

    it('opens nothing on a tab switch, in a diff, off a file, when closed by the writer or turned off', () => {
        assert.equal(previewOnEditorShown({ ...opened, firstShow: false }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, inDiff: true }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, scheme: 'git' }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, dismissed: true }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, openOnOpen: false }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, languageId: 'markdown' }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, hasFollower: true, scheme: 'git' }), 'none');
    });
});

describe('the column a new preview opens in', () => {
    it('is the next one right of the rightmost locked preview on screen', () => {
        assert.equal(newPreviewColumn([2]), 3);
        assert.equal(newPreviewColumn([3, 2]), 4);
    });

    it('is VS Code\'s own beside with no locked preview on screen', () => {
        assert.equal(newPreviewColumn([]), undefined);
    });
});

describe('the source tab a preview brings forward', () => {
    const a = 'file:///a.lys';

    it('is its tab in another group, when that tab is behind others', () => {
        assert.equal(sourceGroupToReveal([{ column: 1, uri: a, active: false }], 2, a), 1);
    });

    it('is none when the source is already in front, not open, or only in the preview\'s own group', () => {
        assert.equal(sourceGroupToReveal([{ column: 1, uri: a, active: true }], 2, a), undefined);
        assert.equal(sourceGroupToReveal([{ column: 1, uri: 'file:///b.lys', active: false }], 2, a), undefined);
        assert.equal(sourceGroupToReveal([{ column: 2, uri: a, active: false }], 2, a), undefined);
        assert.equal(sourceGroupToReveal([{ column: 1, uri: a, active: false }, { column: 3, uri: a, active: true }], 2, a), undefined);
    });
});

// A click on a preview whose score was closed opens it again (owner's request 2026-10-14).
describe('where a click on a preview reopens its closed score', () => {
    const a = 'file:///a.lys';
    const preview = { column: 2, hasPreview: true };

    it('is the group holding a tab of it behind others, never the preview\'s', () => {
        assert.equal(sourceGroupToOpen([{ column: 3, uri: a, active: false }], [{ column: 1, hasPreview: false }, preview, { column: 3, hasPreview: false }], a), 3);
        assert.equal(sourceGroupToOpen([{ column: 2, uri: a, active: false }], [{ column: 1, hasPreview: false }, preview], a), 1);
    });
    it('is else the leftmost group with no preview', () => {
        assert.equal(sourceGroupToOpen([{ column: 3, uri: 'file:///b.lys', active: true }], [preview, { column: 3, hasPreview: false }, { column: 4, hasPreview: false }], a), 3);
    });
    it('is a new group left of the preview when every group holds one (the score\'s group went with it)', () => {
        assert.equal(sourceGroupToOpen([], [{ column: 1, hasPreview: true }], a), 'left');
    });
});
