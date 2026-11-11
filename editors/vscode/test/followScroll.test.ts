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
import * as vm from 'node:vm';
import { FOLLOW_SCROLL_CSS, FOLLOW_SCROLL_JS } from '../src/followScroll';

// Owner's request 2026-11-11: a score dragged away while it played jumped back to the sounding
// note the moment it was dropped. A scroll of the user's own that leaves the sounding notes out
// of the view ends the following; a button, the notes back in view, or the next Play restores it.
describe('whether a Play\'s view follows the notes sounding', () => {
    type Rect = { top: number; bottom: number; left: number; right: number; width: number; height: number };
    const rect = (top: number, bottom: number, left = 100, right = 120): Rect => ({ top, bottom, left, right, width: right - left, height: bottom - top });
    function page() {
        const on: Record<string, (() => void)[]> = {};
        const listen = (prefix: string) => (name: string, f: () => void) => { (on[prefix + name] ??= []).push(f); };
        const fire = (name: string) => { for (const f of on[name] ?? []) f(); };
        // The view: 100 to 500 down the window, 0 to 800 across.
        const scroller = { addEventListener: listen('s:'), getBoundingClientRect: () => rect(100, 500, 0, 800) };
        const button = { hidden: true, textContent: '', style: {} as Record<string, string>, addEventListener: listen('b:') };
        const timers: (() => void)[] = [];
        const context = vm.createContext({
            scroller, button, Math, Infinity,
            window: { addEventListener: listen('w:') },
            setTimeout: (f: () => void) => { timers.push(f); return timers.length; }, clearTimeout: () => { /* none */ },
        });
        new vm.Script(FOLLOW_SCROLL_JS + '\nvar follow = installFollowScroll(scroller, button);').runInContext(context);
        const follow = context.follow as {
            start(): void; stop(): void; sounding(els: unknown[]): void; follows(): boolean; auto(f: () => void): void;
            onResume: ((els: unknown[]) => void) | null;
        };
        const note = { r: rect(300, 320), getBoundingClientRect() { return this.r; } };
        return { follow, button, fire, note, timers };
    }

    it('is page script that parses, with no backtick and no substitution', () => {
        assert.doesNotThrow(() => new vm.Script(FOLLOW_SCROLL_JS));
        assert.ok(!FOLLOW_SCROLL_JS.includes('`') && !FOLLOW_SCROLL_JS.includes('${'));
        assert.match(FOLLOW_SCROLL_CSS, /\.follow-btn\[hidden\] \{ display: none; \}/);
    });

    it('follows from the start of a Play, and while the sounding notes stay in view', () => {
        const { follow, button, fire, note } = page();
        assert.ok(follow.follows(), 'with nothing playing the page moves the view as it always did');
        follow.start();
        follow.sounding([note]);
        assert.ok(follow.follows());
        // A scroll of the user's own that keeps the note in the view changes nothing.
        note.r = rect(450, 470);
        fire('s:scroll');
        assert.ok(follow.follows());
        assert.ok(button.hidden);
    });

    it('stops following when the user scrolls the sounding notes out of view, and shows where they are', () => {
        const { follow, button, fire, note } = page();
        follow.start();
        follow.sounding([note]);
        // Scrolled down the score: the note is now above the view.
        note.r = rect(40, 60);
        fire('s:scroll');
        assert.ok(!follow.follows());
        assert.ok(!button.hidden);
        assert.equal(button.textContent, '↑ Follow');
        // At the foot of the view, in its middle.
        assert.deepEqual([button.style.left, button.style.top], ['400px', '456px']);
        // The music goes on: the arrow follows the notes (here, a later system below the view).
        const later = { getBoundingClientRect: () => rect(700, 720) };
        follow.sounding([later]);
        assert.equal(button.textContent, '↓ Follow');
        // A rest, or a note the page does not draw: the arrow stays as it was.
        follow.sounding([]);
        assert.equal(button.textContent, '↓ Follow');
        assert.ok(!follow.follows());
        // Off to the side of a wide page.
        follow.sounding([{ getBoundingClientRect: () => rect(300, 320, 900, 920) }]);
        assert.equal(button.textContent, '→ Follow');
    });

    it('follows again by the button, by scrolling the notes back into view, and with the next Play', () => {
        const { follow, button, fire, note } = page();
        const resumed: unknown[][] = [];
        follow.onResume = els => { resumed.push(els); };
        const away = () => { note.r = rect(40, 60); fire('s:scroll'); assert.ok(!follow.follows()); };
        follow.start();
        follow.sounding([note]);
        away();
        // The button: the page is handed the sounding notes to scroll to.
        fire('b:click');
        assert.ok(follow.follows());
        assert.ok(button.hidden);
        assert.deepEqual(resumed, [[note]]);
        // Scrolled back by hand until the note is in view.
        away();
        note.r = rect(200, 220);
        fire('s:scroll');
        assert.ok(follow.follows());
        assert.ok(button.hidden);
        // The next Play.
        away();
        follow.start();
        assert.ok(follow.follows());
        assert.ok(button.hidden);
        // A Play that ends takes the button away.
        follow.sounding([note]);
        away();
        follow.stop();
        assert.ok(button.hidden);
        assert.ok(follow.follows());
    });

    it('does not take its own scroll for the user\'s, until the user\'s hand takes over', () => {
        const { follow, button, fire, note } = page();
        follow.start();
        follow.sounding([note]);
        // The page scrolls to the next system: on the way the old note passes out of view.
        follow.auto(() => { note.r = rect(40, 60); });
        fire('s:scroll');
        assert.ok(follow.follows(), 'a scroll of the page\'s own');
        fire('s:scrollend');
        // The wheel in the middle of a smooth scroll: from there the scroll is the user's.
        follow.auto(() => { /* a smooth scroll begins */ });
        fire('s:wheel');
        fire('s:scroll');
        assert.ok(!follow.follows());
        assert.ok(!button.hidden);
        // A scroll that moved nothing sends no scrollend: the timer ends it.
        fire('b:click');
        const { timers } = page();
        assert.equal(timers.length, 0);
    });

    it('never moves the view under a pointer held down on the score', () => {
        const { follow, fire, note } = page();
        follow.start();
        follow.sounding([note]);
        fire('s:pointerdown');
        assert.ok(!follow.follows(), 'a drag in progress');
        fire('w:pointerup');
        assert.ok(follow.follows(), 'dropped with the notes still in view');
        // Dragged until the notes are out of view, then dropped: the view stays where it was put.
        fire('s:pointerdown');
        note.r = rect(600, 620);
        fire('s:scroll');
        fire('w:pointerup');
        assert.ok(!follow.follows());
    });

    it('is one text for the preview and the candidate panel, each asking before it moves the view', () => {
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        const panel = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
        for (const src of [ext, panel]) {
            assert.match(src, /\$\{FOLLOW_SCROLL_JS\}/);
            assert.match(src, /\$\{FOLLOW_SCROLL_CSS\}/);
            assert.match(src, /<button id="followBtn" class="follow-btn" type="button" title="[^"]+" hidden>↓ Follow<\/button>/);
        }
        // The preview: a Play of its own (not a candidate's relayed notes) starts it, a stop ends it.
        assert.match(ext, /const follow = installFollowScroll\(mainContent, document\.getElementById\('followBtn'\)\);/);
        assert.match(ext, /if \(!relay\) follow\.start\(\);/);
        assert.match(ext, /if \(painted\.length > 0\) follow\.sounding\(painted\);\s*if \(r\.hadNew && painted\.length > 0 && follow\.follows\(\)\) follow\.auto\(\(\) => keepSoundingInView\(painted\)\);/);
        assert.match(ext, /playheadTimer = null;\s*follow\.stop\(\);/);
        assert.match(ext, /follow\.onResume = \(els\) => \{ if \(els\.length > 0\) follow\.auto\(\(\) => keepSoundingInView\(els\)\); \};/);
        // The panel.
        assert.match(panel, /const follow = installFollowScroll\(document\.querySelector\('\.main'\), document\.getElementById\('followBtn'\)\);/);
        assert.match(panel, /if \(lit\.length > 0\) follow\.sounding\(lit\);/);
        assert.match(panel, /if \(el && follow\.follows\(\)\) \{/);
        assert.match(panel, /ownPlay = null;\s*follow\.stop\(\);/);
    });
});
