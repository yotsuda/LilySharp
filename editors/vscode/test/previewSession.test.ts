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

import { describe, it, mock } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { PreviewSession, Cancellable } from '../src/previewSession';

/** A request that says whether it was cancelled and let go. */
function request(): Cancellable & { cancelled: number; disposed: number } {
    return { cancelled: 0, disposed: 0, cancel() { this.cancelled++; }, dispose() { this.disposed++; } };
}

// What the host keeps of one preview panel (review of session 1291, F2): one object in the place
// of seven maps keyed by the document, which the three places that touch them all kept apart.
describe('a preview panel\'s session', () => {
    it('orders the answers: a request that left later makes the earlier one\'s answer stale', () => {
        const session = new PreviewSession();
        const first = request(), second = request();
        const a = session.beginRequest(first);
        assert.equal(session.isLatest(a), true);
        const b = session.beginRequest(second);
        assert.equal(session.isLatest(a), false);
        assert.equal(session.isLatest(b), true);
        // …and the earlier one is cancelled on its way, once.
        assert.deepEqual([first.cancelled, first.disposed], [1, 1]);
        assert.deepEqual([second.cancelled, second.disposed], [0, 0]);
    });

    it('waits for the page once: one that has loaded is not waited for', async () => {
        const session = new PreviewSession();
        assert.equal(session.pageHasLoaded, false);
        session.lastPostedSvg = 'x';
        session.shownPagesVersion = 4;
        session.pageLoaded();
        await session.loaded;
        assert.equal(session.pageHasLoaded, true);
        // A page that (re)loaded is blank: the next answer is posted, with every page.
        assert.equal(session.lastPostedSvg, undefined);
        assert.equal(session.shownPagesVersion, undefined);
    });

    it('refreshes after the delay, starting over at each change, but no later than the max-wait', () => {
        mock.timers.enable({ apis: ['setTimeout'] });
        try {
            const session = new PreviewSession();
            let runs = 0;
            const run = () => { runs++; };
            // A held key: a change every 30 ms, the delay 100, the max-wait 120.
            session.refreshAfter(100, 120, 1000, run);
            mock.timers.tick(30);
            session.refreshAfter(100, 120, 1030, run);
            mock.timers.tick(30);
            session.refreshAfter(100, 120, 1060, run);
            mock.timers.tick(30);
            session.refreshAfter(100, 120, 1090, run);   // 30 ms are left of the 120
            mock.timers.tick(29);
            assert.equal(runs, 0);
            mock.timers.tick(1);
            assert.equal(runs, 1);
            // The wait is over: the next change waits the whole delay again.
            session.refreshAfter(100, 120, 1200, run);
            mock.timers.tick(99);
            assert.equal(runs, 1);
            mock.timers.tick(1);
            assert.equal(runs, 2);
        } finally {
            mock.timers.reset();
        }
    });

    it('turned to another file: nothing on its way is the new file\'s, and the page holds neither picture', () => {
        mock.timers.enable({ apis: ['setTimeout'] });
        try {
            const session = new PreviewSession();
            let runs = 0;
            const sent = request();
            const stamp = session.beginRequest(sent);
            session.lastPostedSvg = 'x';
            session.shownPagesVersion = 4;
            session.refreshAfter(100, 120, 1000, () => { runs++; });
            mock.timers.tick(90);

            session.turned(false);
            mock.timers.tick(500);
            assert.equal(runs, 0, 'the refresh that waited held the other document');
            assert.equal(sent.cancelled, 1);
            assert.equal(session.isLatest(stamp), false, 'its answer is the other document\'s');
            assert.equal(session.lastPostedSvg, undefined);
            assert.equal(session.shownPagesVersion, undefined);

            // When its wait began went with it (the seven maps kept it: the next change after a
            // turn was drawn at once, its max-wait long over).
            session.refreshAfter(100, 120, 5000, () => { runs++; });
            mock.timers.tick(99);
            assert.equal(runs, 0);
            mock.timers.tick(1);
            assert.equal(runs, 1);
        } finally {
            mock.timers.reset();
        }
    });

    it('an untitled score saved: the page still shows the picture, but not a version the server knows', () => {
        const session = new PreviewSession();
        const sent = request();
        const stamp = session.beginRequest(sent);
        session.lastPostedSvg = 'x';
        session.shownPagesVersion = 4;
        session.turned(true);
        assert.equal(session.lastPostedSvg, 'x');
        assert.equal(session.shownPagesVersion, undefined);
        assert.equal(sent.cancelled, 1);
        assert.equal(session.isLatest(stamp), false);
    });

    it('closed: the request is cancelled and no refresh fires', () => {
        mock.timers.enable({ apis: ['setTimeout'] });
        try {
            const session = new PreviewSession();
            let runs = 0;
            const sent = request();
            session.beginRequest(sent);
            session.refreshAfter(100, 120, 1000, () => { runs++; });
            session.dispose();
            mock.timers.tick(500);
            assert.equal(runs, 0);
            assert.deepEqual([sent.cancelled, sent.disposed], [1, 1]);
        } finally {
            mock.timers.reset();
        }
    });

    it('is what the host keeps: the three places say what happened in one call, and no map by URI is left', () => {
        const extension = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(extension, /function retargetPreview\([\s\S]{0,700}?sessionOf\(panel\)\.turned\(false\);/);
        assert.match(extension, /function migratePreviewKey\([\s\S]{0,1200}?sessionOf\(panel\)\.turned\(true\);/);
        assert.match(extension, /previewSessions\.get\(panel\)\?\.dispose\(\);\s*previewSessions\.delete\(panel\);/);
        assert.match(extension, /sessionOf\(panel\)\.pageLoaded\(\);/);
        for (const gone of ['panelReady', 'debounceTimers', 'svgRequestGeneration', 'svgRequestCancellation', 'pendingRefreshSince', 'cancelSvgRequest']) {
            assert.equal(extension.includes(gone), false, gone);
        }
        assert.equal(/\b(lastPostedSvg|shownPagesVersion)\.(get|set|delete)\(/.test(extension), false);
        // A call that waited for the server while the panel turned away does not cancel the new file's request.
        assert.match(extension, /if \(previewPanels\.get\(uri\) !== panel\) \{[\s\S]{0,200}?return;\s*\}\s*const requestCancellation = new vscode\.CancellationTokenSource\(\);\s*const generation = session\.beginRequest\(requestCancellation\);/);
    });
});
