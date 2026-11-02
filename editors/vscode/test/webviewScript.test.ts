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
    // bar 1 "c''1 |" at 10..15; then a line "voice { d''2 e'' } |" from 17 to 31, whose bar 2 —
    // its first note to its bar line — is 25..31.
    const bars = [bar('10 15'), bar('25 31')];
    const svgContainer = { querySelectorAll: (sel: string) => sel.includes('.caret') ? bars.filter(b => b.cls.has('caret')) : bars };
    const light = new vm.Script('(() => { let caretAt = -1, caretToken = -1, caretLineEnd = -1; return ' + html.slice(at, close) + '; })()')
        .runInNewContext({ svgContainer, Number, parseInt, Infinity }) as (caret?: number, token?: number, lineEnd?: number) => void;
    const lit = () => bars.map(b => b.cls.has('caret'));

    it('finds the function in the page script', () => {
        assert.ok(at >= 0 && close > at, 'lightCaretBar is not in the page script');
    });
    it('lights the bar holding the caret, one at a time', () => {
        light(12, 10); assert.deepEqual(lit(), [true, false]);
        light(27, 25); assert.deepEqual(lit(), [false, true]);
    });
    it('keeps the bar just closed lit after its bar line, and none in the gap or outside', () => {
        light(16, -1); assert.deepEqual(lit(), [true, false]);
        light(5, -1); assert.deepEqual(lit(), [false, false]);
    });
    it('lights the bar a caret before its first note is on, on the same line (on "voice {")', () => {
        // A caret on "voice" (18, its token at 17) lights bar 2, whose first note is later on the line.
        light(18, 17, 31); assert.deepEqual(lit(), [false, true]);
        // The same caret with the line ending before bar 2 starts: nothing.
        light(18, 17, 22); assert.deepEqual(lit(), [false, false]);
        // A caret inside bar 1 lights bar 1 alone, not the next one on the line.
        light(12, 10, 31); assert.deepEqual(lit(), [true, false]);
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
    // The frames are rects of their own (rect.flag-frame) beside the staff's bars; the fakes
    // record them as the page would hold them.
    type Frame = { attrs: Record<string, string>; remove: () => void };
    const frames: Frame[] = [];
    const parent = { insertBefore: (f: Frame) => frames.push(f) };
    const staff = (first: number, span: string | undefined, x: number) => ({
        x,
        getAttribute: (n: string) => n === 'data-start' ? String(first) : n === 'data-span' ? span ?? null
            : n === 'x' ? String(x) : n === 'y' ? '100' : n === 'width' ? '50' : '20',
        hasAttribute: (n: string) => n === 'data-span' && span !== undefined,
        parentNode: parent, nextSibling: null,
        getScreenCTM: (): unknown => null,   // not laid out: no memo is looked for
    });
    // Bar 4 of three parts, each written on its own line: rh at 100.., lh at 200.., vc at 300..;
    // and the rh bar printed again (a section played twice), from the same place.
    const staves = [staff(100, '100 120', 0), staff(200, '200 230', 1), staff(300, '300 320', 2), staff(100, '100 120', 3)];
    const svgContainer = { querySelectorAll: (sel: string) => sel.includes('flag-frame') ? [...frames] : staves };
    const document = {
        createElementNS: () => {
            const attrs: Record<string, string> = {};
            const f: Frame & { setAttribute: (k: string, v: unknown) => void } = {
                attrs,
                setAttribute: (k, v) => { attrs[k] = String(v); },
                remove: () => { const i = frames.indexOf(f); if (i >= 0) frames.splice(i, 1); },
            };
            return f;
        },
    };
    const applyWith = new vm.Script('(marks => { let flaggedMarks = marks; return ' + html.slice(at, close) + '; })')
        .runInNewContext({ svgContainer, document, Number, parseInt }) as (marks: unknown[]) => () => void;
    const framed = () => staves.map(s => frames.some(f => f.attrs.d.startsWith('M ' + s.x + ' ')));

    it('finds the function in the page script', () => {
        assert.ok(at >= 0 && close > at, 'applyBarFlags is not in the page script');
    });
    it('frames the staff\'s bars written on the mark\'s lines — one part, two, or every one', () => {
        applyWith([{ at: 205, lines: [[200, 230]] }])();
        assert.deepEqual(framed(), [false, true, false, false]);
        // The frame is the scan's box: the bar's width, two staff spaces (half a staff) above and below.
        assert.equal(frames[0].attrs.d, 'M 1 90 H 51 M 51 90 V 130 M 1 130 H 51 M 1 90 V 130');
        assert.equal(frames[0].attrs['pointer-events'], 'none');
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
    // An @todo memo an edge of the frame would run through (owner's report and decision
    // 2026-10-27, tabi-13.lys: "枠線の y は狭めないでほしい"): the edge breaks a quarter space clear
    // of the memo's words, and the frame keeps its size.
    it('breaks its edges round the memos, and keeps its size', () => {
        const at2 = html.indexOf('function applyBarFlags(');
        const identity = { inverse: () => identity };
        const point = class { constructor(public x: number, public y: number) {} matrixTransform() { return this; } };
        const bar = { ...staff(200, '200 230', 0), getScreenCTM: () => identity };
        const box = (left: number, right: number, top: number, bottom: number) => ({ left, right, top, bottom });
        const run = (memos: unknown[]) => {
            frames.length = 0;
            new vm.Script('(marks => { let flaggedMarks = marks; return ' + html.slice(at2, close) + '; })')
                .runInNewContext({
                    svgContainer: { querySelectorAll: (sel: string) => sel.includes('flag-frame') ? [...frames] : [bar] },
                    document, Number, parseInt, Math, DOMPoint: point, memoBoxes: () => memos,
                })([{ at: 205, lines: [[200, 230]] }])();
            return frames[0].attrs.d;
        };
        const whole = 'M 0 90 H 50 M 50 90 V 130 M 0 130 H 50 M 0 90 V 130';
        assert.equal(run([]), whole);
        // Over the staff, out past the bar's right: the top edge and the right one break.
        assert.equal(run([box(10, 80, 84, 95)]), 'M 0 90 H 8.75 M 50 96.25 V 130 M 0 130 H 50 M 0 90 V 130');
        // Under it: the bottom edge and the right one.
        assert.equal(run([box(10, 80, 125, 136)]), 'M 0 90 H 50 M 50 90 V 123.75 M 0 130 H 8.75 M 0 90 V 130');
        // Within the bar's width: the top edge only, on both sides of the words.
        assert.equal(run([box(10, 40, 84, 95)]), 'M 0 90 H 8.75 M 41.25 90 H 50 M 50 90 V 130 M 0 130 H 50 M 0 90 V 130');
        assert.equal(run([box(60, 80, 84, 95)]), whole);     // beside the bar
        assert.equal(run([box(10, 80, 70, 80)]), whole);     // clear above the frame
    });
});

// Play from Here on a bar too (owner's request 2026-10-26): the menu item shows wherever a play
// can start — on a note, from it; on a bar, from its first note — and the manifest gates it on
// the key the page publishes.
describe('the preview offers Play from Here on a note and on a bar', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
    it('gates the item on lysPlayFrom, which the page publishes', () => {
        const item = manifest.contributes.menus['webview/context'].find((m: { command: string }) => m.command === 'lilysharp.previewPlayFromHere');
        assert.equal(item.when, "webviewId == 'lilysharpPreview' && lysPlayFrom");
        assert.match(source, /lysPlayFrom: ctxPlayPos >= 0,/);
        assert.match(source, /"lysPlayFrom": false/);
        assert.doesNotMatch(source, /lysNote/);
    });
    it('starts from the note right-clicked, else from the first note of the bar', () => {
        assert.match(source, /ctxPlayPos = ctxNotePos >= 0 \? ctxNotePos : ctxBarRange \? ctxBarRange\[0\] : -1;/);
        assert.match(source, /case 'ctxPlayFromHere':\s*if \(ctxPlayPos >= 0\) \{\s*pendingStartPos = ctxPlayPos;/);
    });
});

// The page script's own function, taken out of the evaluated template.
function pageFunction(name: string, context: Record<string, unknown>): unknown {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = source.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;
    const at = html.indexOf('function ' + name + '(');
    assert.ok(at >= 0, name + ' is not in the page script');
    let depth = 0, close = -1;
    for (let i = html.indexOf('{', at); i < html.length; i++) {
        if (html[i] === '{') depth++;
        else if (html[i] === '}' && --depth === 0) { close = i + 1; break; }
    }
    return new vm.Script('(' + html.slice(at, close) + ')').runInNewContext({ Math, Infinity, Array, ...context });
}

// Owner's reports 2026-10-29: in `form { A B A }` playing A lit both of its printed copies, and a
// song's playback shook the view between pages. getBBox is in each system's own frame, so copies
// on two systems at one x stood 0 apart; and a page draws a hi-hat's heads again in its overlay
// after its systems, so the DOM is not in time order. A copy is what stands in one bar's column,
// close across; the copies come in the order of the bars.
describe('the preview tells a note\'s printed copies apart by their bars', () => {
    const rect = (left: number, top: number, w: number, h: number) => ({ left, top, right: left + w, bottom: top + h, width: w, height: h });
    // Two systems, two bars each, a box over each bar's staff lines; the second system 20 below.
    const bars = [rect(0, 0, 50, 4), rect(50, 0, 50, 4), rect(0, 20, 50, 4), rect(50, 20, 50, 4)];
    // An element at (x, y) on the page; its own frame's y is the same on every system (local).
    const el = (x: number, y: number, local = y % 20) => ({
        id: x + ',' + y,
        getBBox: () => ({ x, y: local }),
        getBoundingClientRect: () => rect(x, y, 1, 1),
    });
    const clusterByGaps = pageFunction('clusterByGaps', {});
    const cluster = (boxes: ReturnType<typeof rect>[]) => pageFunction('clusterInstances', {
        svgContainer: { querySelectorAll: () => boxes.map(b => ({ getBoundingClientRect: () => b, getBBox: () => ({ width: b.width }) })) },
        clusterByGaps,
    }) as (m: unknown[]) => { id: string }[][];
    // The copies by their elements, as arrays of this realm (the function's are the vm's).
    const ids = (copies: { id: string }[][]) => Array.from(copies, c => Array.from(c, e => e.id).join(' '));

    it('splits two copies at one x on two systems, a note under the staff with its bar', () => {
        // A chord at the start of each system, its lower head under the staff lines.
        assert.deepEqual(ids(cluster(bars)([el(5, 2), el(5, 6), el(5, 22), el(5, 26)])), ['5,2 5,6', '5,22 5,26']);
    });
    it('puts the copies in the order of the bars, whatever the order of the DOM', () => {
        // A page's systems, then its overlay drawing the same heads again: four bars, four copies.
        const page = [el(5, 2), el(55, 2), el(5, 22), el(55, 22), el(6, 2), el(56, 2), el(6, 22), el(56, 22)];
        assert.deepEqual(ids(cluster(bars)(page)), ['5,2 6,2', '55,2 56,2', '5,22 6,22', '55,22 56,22']);
    });
    it('splits a phrase written twice in one bar, and keeps a mark beyond the bars with its note', () => {
        assert.deepEqual(ids(cluster(bars)([el(5, 2), el(30, 2)])), ['5,2', '30,2']);
        assert.deepEqual(ids(cluster(bars)([el(5, 2), el(120, 2), el(55, 2)])), ['5,2 120,2', '55,2']);
    });
    it('falls back to the gaps where the page draws no bars', () => {
        assert.deepEqual(ids(cluster([])([el(5, 2), el(5, 12, 12)])), ['5,2 5,12']);
        assert.deepEqual(ids(cluster([])([el(5, 2), el(5, 30, 30)])), ['5,2', '5,30']);
    });
});

// Owner's request 2026-10-29: the Range and practice panels move where they are dragged, inside
// the preview, and never so far that they cannot be taken back.
describe('the preview\'s panels are dragged, and kept where they can be taken back', () => {
    type Panel = {
        hidden: boolean; dataset: Record<string, string>; style: Record<string, string>;
        width: number; height: number; getBoundingClientRect: () => { left: number; top: number; width: number; height: number };
    };
    const panel = (left: number, top: number): Panel => {
        const p: Panel = {
            hidden: false, dataset: { moved: '1' }, style: { left: left + 'px', top: top + 'px' }, width: 300, height: 200,
            getBoundingClientRect: () => ({ left: parseFloat(p.style.left), top: parseFloat(p.style.top), width: p.width, height: p.height }),
        };
        return p;
    };
    const clamp = pageFunction('clampPanel', {
        PANEL_KEEP: 48,
        window: { innerWidth: 800, innerHeight: 600 },
        document: { querySelector: () => ({ getBoundingClientRect: () => ({ bottom: 40 }) }) },
    }) as (p: Panel) => void;
    const at = (p: Panel) => [parseFloat(p.style.left), parseFloat(p.style.top)];

    it('leaves a panel where it was put inside the preview', () => {
        const p = panel(200, 100);
        clamp(p);
        assert.deepEqual(at(p), [200, 100]);
        assert.equal(p.style.right, 'auto');
    });
    it('lets it go past an edge, cut off there, but keeps 48 px of it in sight', () => {
        const left = panel(-500, 100); clamp(left);
        assert.deepEqual(at(left), [48 - 300, 100]);
        const right = panel(900, 100); clamp(right);
        assert.deepEqual(at(right), [800 - 48, 100]);
        const down = panel(200, 900); clamp(down);
        assert.deepEqual(at(down), [200, 600 - 48]);
    });
    it('goes up behind the toolbar, keeping as much in sight below it as on the other sides', () => {
        const behind = panel(200, 10); clamp(behind);
        assert.deepEqual(at(behind), [200, Math.max(10, 40 + 48 - behind.height)]);
        const up = panel(200, -3000); clamp(up);
        assert.deepEqual(at(up), [200, 40 + 48 - up.height]);
    });
    it('stands behind the toolbar, which stays whole', () => {
        const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        const z = (rule: string) => Number(new RegExp(`${rule} \\{[^}]*?z-index: (\\d+)`).exec(source)?.[1]);
        assert.ok(z('\\.range-panel') < z('\\.toolbar'), `${z('\\.range-panel')} / ${z('\\.toolbar')}`);
    });
    it('does not touch a panel never moved, or one hidden', () => {
        const p = panel(-500, 100); p.dataset = {}; clamp(p);
        assert.deepEqual(at(p), [-500, 100]);
        const h = panel(-500, 100); h.hidden = true; clamp(h);
        assert.deepEqual(at(h), [-500, 100]);
    });
    it('draws where a dragged note will land beside its head, not on top of what the head has moved by', () => {
        const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(source, /head\.parentNode\.appendChild\(ghost\);/);
        assert.match(source, /pale\.removeAttribute\('transform'\);/);
        assert.match(source, /perPixel: 1 \/ head\.getScreenCTM\(\)\.d/);
    });
    it('finds the staff a dragged note is on, and the ledger lines it would need', () => {
        const staff5 = (top: number) => [0, 1, 2, 3, 4].map(i => ({ x1: 0, x2: 100, y: top + i }));
        const lines = [...staff5(10), ...staff5(30), { x1: 40, x2: 43, y: 9 }, { x1: 40, x2: 43, y: 8 }, { x1: 40, x2: 43, y: 7 }];
        const find = pageFunction('staffFromLines', {}) as (l: unknown[], x: number, y: number, w: number) => { top: number, bottom: number, space: number };
        const on = find(lines, 41, 12, 6);
        assert.deepEqual([on.top, on.bottom, on.space], [10, 14, 1]);
        const below = find(lines, 41, 36, 6);
        assert.deepEqual([below.top, below.bottom], [30, 34]);
        const high = find(lines, 41, 5, 6);
        assert.deepEqual([high.top, high.bottom], [10, 14], 'a ledger line joins no staff');
        assert.equal(find(lines.slice(0, 4), 41, 12, 6), null);
        const ledgers = pageFunction('ledgersFor', {}) as (s: unknown, y: number) => number[];
        assert.deepEqual(Array.from(ledgers(on, 12)), []);
        assert.deepEqual(Array.from(ledgers(on, 8.5)), [9]);
        assert.deepEqual(Array.from(ledgers(on, 8)), [9, 8]);
        assert.deepEqual(Array.from(ledgers(on, 15.5)), [15]);
        assert.deepEqual(Array.from(ledgers(null, 8)), []);
    });
    it('stands the panel pressed or opened last in front of the other, both under the toolbar', () => {
        const range = { style: {} as { zIndex?: string } };
        const practice = { style: {} as { zIndex?: string } };
        const raise = pageFunction('raisePanel', { rangePanel: range, practicePanel: practice }) as (p: unknown) => void;
        raise(range);
        assert.ok(Number(range.style.zIndex) > Number(practice.style.zIndex));
        raise(practice);
        assert.ok(Number(practice.style.zIndex) > Number(range.style.zIndex));
        assert.ok(Number(practice.style.zIndex) < 10, 'the toolbar is 10');
        const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(source, /addEventListener\('pointerdown', \(e\) => \{\s*raisePanel\(panel\);/);
        assert.match(source, /if \(rangePanel\.hidden\) return;\s*raisePanel\(rangePanel\);/);
        assert.match(source, /practicePanel\.hidden = false;\s*raisePanel\(practicePanel\);/);
    });
    it('is installed on both panels, left alone by their inputs, and kept on a resize', () => {
        const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(source, /installPanelDrag\(rangePanel\);\s*installPanelDrag\(practicePanel\);/);
        assert.match(source, /e\.target\.closest\('input, select, textarea'\)\) return;/);
        assert.match(source, /window\.addEventListener\('resize', \(\) => \{ clampPanel\(rangePanel\); clampPanel\(practicePanel\); \}\);/);
        // Opened again, a moved panel stays where it was put.
        assert.match(source, /if \(rangePanel\.dataset\.moved\) clampPanel\(rangePanel\);/);
        assert.match(source, /if \(practicePanel\.dataset\.moved\) clampPanel\(practicePanel\);/);
    });
});

// Owner's report 2026-10-29: a song's system was taller than half the view, and following one
// note at a time centred the voice, then the drums it pushed off, then the voice — the view shook.
describe('the preview keeps what sounds in view, every part together', () => {
    const rect = (top: number, bottom: number, left = 100, right = 200) => ({ top, bottom, left, right, width: right - left, height: bottom - top });
    const follow = () => {
        const scrolls: { top: number; left: number }[] = [];
        const mainContent = {
            getBoundingClientRect: () => rect(0, 600, 0, 800),
            scrollBy: (o: { top: number; left: number }) => scrolls.push(o),
        };
        const keep = pageFunction('keepSoundingInView', { mainContent }) as (els: unknown[]) => void;
        const notes = (...rs: ReturnType<typeof rect>[]) => rs.map(r => ({ getBoundingClientRect: () => r }));
        return { keep, notes, scrolls };
    };
    it('leaves alone what is all in view', () => {
        const { keep, notes, scrolls } = follow();
        keep(notes(rect(100, 110), rect(400, 410)));
        assert.equal(scrolls.length, 0);
    });
    it('centres the parts together when they fit, not one note', () => {
        const { keep, notes, scrolls } = follow();
        keep(notes(rect(500, 510), rect(800, 810)));
        assert.equal(scrolls.length, 1);
        assert.equal(scrolls[0].top, (500 + 810) / 2 - 300);
    });
    it('keeps the top of a system taller than the view, and does not move for its lower part', () => {
        const { keep, notes, scrolls } = follow();
        // The voice at 40, the drums at 900: the drums out of sight, the top in view — no move.
        keep(notes(rect(40, 50), rect(900, 910)));
        assert.equal(scrolls.length, 0);
        // The same system come up from below: its top goes to the view's top.
        keep(notes(rect(700, 710), rect(1500, 1510)));
        assert.deepEqual(scrolls.map(s => s.top), [700 - 30]);
    });
});
