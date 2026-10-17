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

// The preview's page script lives inside a TypeScript TEMPLATE LITERAL in
// src/extension.ts (getWebviewContent), so it is a string to tsc and esbuild:
// a syntax error in it passes both and surfaces only when the preview opens,
// as a page that stays on "Loading preview". Two spellings that are fine in a
// .js file are not fine there — a backtick in a comment ends the template, and
// a regex backslash is the template's own escape (\s becomes s, \/ becomes /,
// which ended a regex early and took the whole script down on 2026-09-04).
// This evaluates the template as TypeScript will and parses every <script>
// with V8.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vm from 'node:vm';

describe('the preview page script', () => {
    it('parses once the template literal that carries it has been evaluated', () => {
        const source = fs.readFileSync(
            path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        const open = 'return `<!DOCTYPE html>';
        const start = source.indexOf(open) + 'return '.length;
        const end = source.indexOf('</html>`;', start) + '</html>`'.length;
        assert.ok(start > 'return '.length && end > start, 'the webview template was not found');
        // The template's own substitutions (the theme, the nonce…) are not what
        // is under test; any word stands in for them.
        const template = source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X');
        const html = new vm.Script(template).runInNewContext({}) as string;
        const scripts = [...html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)];
        assert.ok(scripts.length >= 2, 'expected the page to carry its scripts');
        for (const [, body] of scripts) {
            assert.doesNotThrow(() => new vm.Script(body), 'the page script does not parse');
        }
    });
});

// The page's offsets read back after a group reconcile (offsetsDisagree): the guard that
// replaces a page whose reused groups kept a stale data-pos (owner report 2026-10-11 —
// selecting a note lit another one until the preview was reopened). The DOM is not
// available here, so the function is taken out of the evaluated page script and handed
// pages made of plain objects: what it reads is each element's attributes, in order.
describe('the preview reads its offsets back after a reconcile', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = source.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
    const head = 'function offsetsDisagree(';
    const at = html.indexOf(head);
    let depth = 0, close = -1;
    for (let i = html.indexOf('{', at); i < html.length; i++) {
        if (html[i] === '{') depth++;
        else if (html[i] === '}' && --depth === 0) { close = i + 1; break; }
    }
    // The selector of the offset-carrying elements, as the page script spells it.
    const SOURCE_SELECTOR = /const SOURCE_SELECTOR = '([^']*)'/.exec(html)?.[1];
    const offsetsDisagree = new vm.Script('(' + html.slice(at, close) + ')').runInNewContext({ SOURCE_SELECTOR }) as
        (page: unknown, markup: string) => string | null;

    type Attr = { name: string; value: string };
    const page = (...els: Attr[][]) => ({ querySelectorAll: () => els.map(attributes => ({ attributes })) });
    const pos = (v: string): Attr => ({ name: 'data-pos', value: v });
    const alt = (v: string): Attr => ({ name: 'data-alt', value: v });
    const markup = '<g><path d="M0" data-pos="12"/><rect data-pos="30" data-alt="31 40"/><text class="x">a</text></g>';

    it('finds the function in the page script', () => {
        assert.ok(at >= 0 && close > at, 'offsetsDisagree is not in the page script');
    });
    it('says nothing when every offset agrees, data-alt included, in attribute order', () => {
        assert.equal(offsetsDisagree(page([pos('12')], [pos('30'), alt('31 40')]), markup), null);
    });
    it('names the first offset a reused group kept stale', () => {
        const why = offsetsDisagree(page([pos('12')], [pos('38'), alt('31 40')]), markup);
        assert.match(why ?? '', /#1 drawn data-pos=38, markup data-pos=30/);
    });
    it('reads a bar\'s written place (data-start) as an offset too', () => {
        const start = (v: string): Attr => ({ name: 'data-start', value: v });
        const withBar = markup + '<rect class="bar-staff" data-start="50"/>';
        assert.equal(offsetsDisagree(page([pos('12')], [pos('30'), alt('31 40')], [start('50')]), withBar), null);
        assert.match(offsetsDisagree(page([pos('12')], [pos('30'), alt('31 40')], [start('47')]), withBar) ?? '',
            /data-start=47, markup data-start=50/);
    });
    it('notices an element too many or too few', () => {
        assert.notEqual(offsetsDisagree(page([pos('12')]), markup), null);
        assert.notEqual(offsetsDisagree(page([pos('12')], [pos('30'), alt('31 40')], [pos('99')]), markup), null);
    });
});

// Dragging the sheet scrolls it (installDragScroll, owner request 2026-10-12) without taking
// the click that jumps to the editor: the function is taken out of the page script and handed
// a scroller and a document made of plain objects that record their listeners.
describe('the preview scrolls when the sheet is dragged', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = source.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
    const head = 'function installDragScroll(';
    const at = html.indexOf(head);
    let depth = 0, close = -1;
    for (let i = html.indexOf('{', at); i < html.length; i++) {
        if (html[i] === '{') depth++;
        else if (html[i] === '}' && --depth === 0) { close = i + 1; break; }
    }
    const install = new vm.Script('(' + html.slice(at, close) + ')').runInNewContext({ Math }) as
        (scroller: unknown, doc: unknown) => void;

    type Handler = (e: Record<string, unknown>) => void;
    function sheet() {
        const on: Record<string, Handler[]> = {}, onDoc: Record<string, Handler[]> = {};
        const classes = new Set<string>();
        const scroller = {
            scrollLeft: 100, scrollTop: 500, clientLeft: 0, clientTop: 0, clientWidth: 800, clientHeight: 600,
            getBoundingClientRect: () => ({ left: 0, top: 40 }),
            setPointerCapture: () => { },
            classList: { add: (c: string) => classes.add(c), remove: (c: string) => classes.delete(c) },
            addEventListener: (t: string, h: Handler) => (on[t] ??= []).push(h),
        };
        const doc = { addEventListener: (t: string, h: Handler) => (onDoc[t] ??= []).push(h) };
        install(scroller, doc);
        const note = { closest: () => null };
        const fire = (t: string, e: Record<string, unknown>) =>
            (on[t] ?? []).forEach(h => h({ pointerId: 1, pointerType: 'mouse', button: 0, target: note, ...e }));
        // What the page's own click handler would get: true when the click reaches it.
        const click = () => {
            let reached = true;
            const e = { stopImmediatePropagation: () => { reached = false; }, preventDefault: () => { } };
            (onDoc['click'] ?? []).forEach(h => h(e));
            return reached;
        };
        return { scroller, classes, fire, click };
    }

    it('finds the function in the page script', () => {
        assert.ok(at >= 0 && close > at, 'installDragScroll is not in the page script');
    });
    it('scrolls with the mouse, and the click that ends the drag does not jump', () => {
        const s = sheet();
        s.fire('pointerdown', { clientX: 300, clientY: 300 });
        s.fire('pointermove', { clientX: 260, clientY: 180 });
        assert.deepEqual([s.scroller.scrollLeft, s.scroller.scrollTop], [140, 620]);
        assert.ok(s.classes.has('dragging'));
        s.fire('pointerup', { clientX: 260, clientY: 180 });
        assert.ok(!s.classes.has('dragging'));
        assert.equal(s.click(), false);
        // …and the next plain click on a note jumps as before.
        s.fire('pointerdown', { clientX: 300, clientY: 300 });
        s.fire('pointerup', { clientX: 300, clientY: 300 });
        assert.equal(s.click(), true);
    });
    it('keeps a click that wobbles less than the slop a click', () => {
        const s = sheet();
        s.fire('pointerdown', { clientX: 300, clientY: 300 });
        s.fire('pointermove', { clientX: 302, clientY: 297 });
        s.fire('pointerup', { clientX: 302, clientY: 297 });
        assert.deepEqual([s.scroller.scrollLeft, s.scroller.scrollTop], [100, 500]);
        assert.equal(s.click(), true);
    });
    it('leaves shift-click, other buttons, touch, the scroll bar and controls alone', () => {
        for (const e of [{ shiftKey: true }, { button: 2 }, { pointerType: 'touch' },
            { clientX: 805 }, { target: { closest: () => ({}) } }]) {
            const s = sheet();
            s.fire('pointerdown', { clientX: 300, clientY: 300, ...e });
            s.fire('pointermove', { clientX: 200, clientY: 100 });
            s.fire('pointerup', {});
            assert.deepEqual([s.scroller.scrollLeft, s.scroller.scrollTop], [100, 500], JSON.stringify(e));
            assert.equal(s.click(), true, JSON.stringify(e));
        }
    });
});

// The scan view's slider hides the same marks in the preview (owner's request 2026-10-12):
// hiddenTodoCss turns the keys into one stylesheet. Lifted out of the page script like the
// functions above.
describe('the preview hides the marks the scan view hides', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = source.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
    const at = html.indexOf('function hiddenTodoCss(');
    let depth = 0, close = -1;
    for (let i = html.indexOf('{', at); i < html.length; i++) {
        if (html[i] === '{') depth++;
        else if (html[i] === '}' && --depth === 0) { close = i + 1; break; }
    }
    const css = new vm.Script('(' + html.slice(at, close) + ')').runInNewContext({ String }) as (keys: string[]) => string;

    it('hides every piece of a hidden key but the music it rings', () => {
        const rule = css(['b2', 'b11-p1-n1']);
        for (const sel of ['path', 'ellipse', 'rect', 'line']) {
            assert.ok(rule.includes(`#svgContainer ${sel}[data-todo="b2"]`), sel);
        }
        assert.ok(rule.includes('#svgContainer text[data-todo="b11-p1-n1"]:not(.music)'));
        assert.ok(!/text\[data-todo="b2"\][,{ ]/.test(rule), 'the ringed glyph would vanish');
        assert.match(rule, /\{ display: none; \}$/);
    });
    it('writes nothing for no key, and cannot be broken out of by a quote', () => {
        assert.equal(css([]), '');
        assert.ok(!css(['a"] body {x']).includes('"]  body'));
        assert.equal(css(['a"b']).match(/"/g)!.length % 2, 0);
    });
});

// A click on a bar goes to where it is written (barStaffAt, owner request 2026-10-14): the
// server's rect.bar-staff boxes take no pointer events, so the page finds the one under the
// click by its geometry. Lifted out of the page script like the functions above and handed a
// container of plain boxes.
describe('the preview finds the bar a click lands on', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = source.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
    const at = html.indexOf('function barStaffAt(');
    let depth = 0, close = -1;
    for (let i = html.indexOf('{', at); i < html.length; i++) {
        if (html[i] === '{') depth++;
        else if (html[i] === '}' && --depth === 0) { close = i + 1; break; }
    }
    // Two bars of two staves, 40 px tall, 100 px apart; the next system 400 px down.
    const box = (name: string, left: number, top: number) =>
        ({ name, getBoundingClientRect: () => ({ left, right: left + 200, top, bottom: top + 40, height: 40 }) });
    const boxes = [box('1 upper', 0, 100), box('1 lower', 0, 200), box('2 upper', 200, 100), box('2 lower', 200, 200),
        box('3 upper', 0, 600)];
    const svgContainer = { querySelectorAll: (sel: string) => sel === 'rect.bar-staff' ? boxes : [] };
    const barStaffAt = new vm.Script('(' + html.slice(at, close) + ')').runInNewContext({ svgContainer, Infinity }) as
        (x: number, y: number) => { name: string } | null;

    it('finds the function in the page script', () => {
        assert.ok(at >= 0 && close > at, 'barStaffAt is not in the page script');
    });
    it('takes the bar and the staff under the click', () => {
        assert.equal(barStaffAt(50, 120)?.name, '1 upper');
        assert.equal(barStaffAt(250, 230)?.name, '2 lower');
    });
    it('takes the nearest staff of the bar beside a staff (a lyric line, the gap between staves)', () => {
        assert.equal(barStaffAt(50, 150)?.name, '1 upper');
        assert.equal(barStaffAt(250, 180)?.name, '2 lower');
        assert.equal(barStaffAt(50, 300)?.name, '1 lower');
    });
    it('takes nothing more than two staff heights from every staff, or beside every bar', () => {
        assert.equal(barStaffAt(50, 400), null);
        assert.equal(barStaffAt(450, 120), null);
    });
});

// The bar the editor's caret is in stays lit (lightCaretBar, owner's request 2026-10-14): a bar
// whose written stretch (data-span) holds the caret, or just after its closing bar line, or the
// start of the token under the caret. Lifted out of the page script like the functions above.
describe('the preview lights the bar the caret is in', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = source.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
    const at = html.indexOf('function lightCaretBar(');
    let depth = 0, close = -1;
    for (let i = html.indexOf('{', at); i < html.length; i++) {
        if (html[i] === '{') depth++;
        else if (html[i] === '}' && --depth === 0) { close = i + 1; break; }
    }
    const bar = (span: string) => {
        const cls = new Set<string>();
        return { cls, getAttribute: () => span, classList: { add: (c: string) => cls.add(c), remove: (c: string) => cls.delete(c) } };
    };
    // bar 1 "c''1 |" at 10..15, bar 2 "d''2 e'' |" at 17..26.
    const bars = [bar('10 15'), bar('17 26')];
    const svgContainer = { querySelectorAll: (sel: string) => sel.includes('.caret') ? bars.filter(b => b.cls.has('caret')) : bars };
    const light = new vm.Script('(() => { let caretAt = -1, caretToken = -1; return ' + html.slice(at, close) + '; })()')
        .runInNewContext({ svgContainer, Number, parseInt }) as (caret?: number, token?: number) => void;
    const lit = () => bars.map(b => b.cls.has('caret'));

    it('finds the function in the page script', () => {
        assert.ok(at >= 0 && close > at, 'lightCaretBar is not in the page script');
    });
    it('lights the bar holding the caret, one at a time', () => {
        light(12, 10); assert.deepEqual(lit(), [true, false]);
        light(20, 20); assert.deepEqual(lit(), [false, true]);
    });
    it('keeps the bar just closed lit after its bar line, and none in the gap or outside', () => {
        light(16, -1); assert.deepEqual(lit(), [true, false]);
        light(5, -1); assert.deepEqual(lit(), [false, false]);
    });
    it('lights again what it lit when called with no caret (after a render)', () => {
        light(12, 10);
        for (const b of bars) b.cls.clear();
        light(); assert.deepEqual(lit(), [true, false]);
    });
});

// The frames the preview draws for an OMR reader's marks (applyBarFlags): what each mark covers
// on the scan, part for part (owner's request 2026-10-17) — the staff's bars whose first note is
// on one of the mark's lines, or, for a mark the scan does not know, the one whose written
// stretch holds where it is written.
describe('the preview frames what the scan boxes', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = source.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
    const at = html.indexOf('function applyBarFlags(');
    let depth = 0, close = -1;
    for (let i = html.indexOf('{', at); i < html.length; i++) {
        if (html[i] === '{') depth++;
        else if (html[i] === '}' && --depth === 0) { close = i + 1; break; }
    }
    const staff = (first: number, span?: string) => {
        const cls = new Set<string>();
        return {
            cls,
            getAttribute: (n: string) => n === 'data-start' ? String(first) : span ?? null,
            hasAttribute: (n: string) => n === 'data-span' && span !== undefined,
            classList: { add: (c: string) => cls.add(c), remove: (c: string) => cls.delete(c) },
        };
    };
    // Bar 4 of three parts, each written on its own line: rh at 100.., lh at 200.., vc at 300..;
    // and the rh bar printed again (a section played twice), from the same place.
    const staves = [staff(100, '100 120'), staff(200, '200 230'), staff(300, '300 320'), staff(100, '100 120')];
    const svgContainer = { querySelectorAll: (sel: string) => sel.includes('.flagged') ? staves.filter(s => s.cls.has('flagged')) : staves };
    const applyWith = new vm.Script('(marks => { let flaggedMarks = marks; return ' + html.slice(at, close) + '; })')
        .runInNewContext({ svgContainer, Number, parseInt }) as (marks: unknown[]) => () => void;
    const framed = () => staves.map(s => s.cls.has('flagged'));

    it('finds the function in the page script', () => {
        assert.ok(at >= 0 && close > at, 'applyBarFlags is not in the page script');
    });
    it('frames the staff\'s bars written on the mark\'s lines — one part, two, or every one', () => {
        applyWith([{ at: 205, lines: [[200, 230]] }])();
        assert.deepEqual(framed(), [false, true, false, false]);
        applyWith([{ at: 101, lines: [[100, 120], [200, 230]] }])();      // a length mark: rh and lh are off
        assert.deepEqual(framed(), [true, true, false, true]);             // …and the rh copy too
        applyWith([{ at: 101, lines: [[100, 120], [200, 230], [300, 320]] }])();
        assert.deepEqual(framed(), [true, true, true, true]);
    });
    it('frames the one bar holding a mark the scan does not know, and clears what it framed before', () => {
        applyWith([{ at: 305, lines: [] }])();
        assert.deepEqual(framed(), [false, false, true, false]);
        applyWith([{ at: 305 }])();
        assert.deepEqual(framed(), [false, false, true, false]);
        applyWith([])();
        assert.deepEqual(framed(), [false, false, false, false]);
    });
});
