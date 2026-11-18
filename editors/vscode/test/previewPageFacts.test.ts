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

// What the preview's page reads off the picture after an update (review of session 1291, P6;
// measured in a real VS Code in session 1302). An update changes one page of a book; the page
// used to read EVERY page again — the offsets' index, the bars' stretches for the caret's bar,
// the bar boxes for the A-B flags — and a keystroke that made the text longer had the browser
// restyle every element after the caret, through a style rule on [data-pos]. The functions are
// cut out of the page script and run on pages made of plain objects that count their reads.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as vm from 'node:vm';
import { extensionSource } from './extensionSource';

const source = extensionSource();
const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
const end = source.indexOf('</html>`;', start) + '</html>`'.length;
const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;

/** The function of that name, as the page script spells it. */
function cut(name: string): string {
    const at = html.indexOf('function ' + name + '(');
    assert.ok(at >= 0, name + ' is not in the page script');
    let depth = 0;
    for (let i = html.indexOf('{', at); i < html.length; i++) {
        if (html[i] === '{') depth++;
        else if (html[i] === '}' && --depth === 0) return html.slice(at, i + 1);
    }
    throw new Error(name + ' does not close');
}

type El = { cls: string[]; tag: string; attrs: Record<string, string> };
const el = (attrs: Record<string, string>, cls: string[] = [], tag = 'path'): El => ({ cls, tag, attrs });
const dom = (e: El) => ({
    e,
    classList: { contains: (c: string) => e.cls.includes(c) },
    tagName: e.tag,
    getAttribute: (n: string) => e.attrs[n] ?? null,
});
type Node = ReturnType<typeof dom>;

/** A page of the picture: its elements, and how many times they were asked for. */
function page(els: El[]) {
    const nodes = els.map(dom);
    const p = { reads: 0, nodes, querySelectorAll: (_: string) => { p.reads++; return nodes; } };
    return p;
}

/** The page script's own functions over a picture of these pages. */
function harness(pages: ReturnType<typeof page>[], paged = true) {
    const svg = {
        reads: 0,
        querySelectorAll: (sel: string) => {
            if (sel === ':scope > g.page') return paged ? pages : [];
            svg.reads++;
            return pages.flatMap(p => p.nodes);
        },
    };
    const svgContainer = { querySelector: () => svg };
    const body = '(() => { let shownPages = null; let posSorted = [], posElements = new Map(), posIndexBuilt = false;'
        + ' const pageFactsOf = new WeakMap();\n'
        + ['shiftOffset', 'readPageFacts', 'pageFacts', 'shiftPageFacts', 'livePageFacts', 'invalidateSourcePositions', 'sourcePositions', 'abBars']
            .map(cut).join('\n')
        + '\nreturn { shown: v => { shownPages = v; }, invalidateSourcePositions, shiftPageFacts, livePageFacts, abBars,'
        + ' positions: () => sourcePositions(), at: n => posElements.get(n) || [] }; })()';
    const raw = new vm.Script(body).runInNewContext({ svgContainer }) as {
        shown: (v: unknown[] | null) => void; invalidateSourcePositions: () => void;
        shiftPageFacts: (p: unknown, w: { Prefix: number; SuffixStart: number; Delta: number }) => void;
        livePageFacts: () => { pos: unknown[]; spans: unknown[]; bars: { m: number; bar: number; rect: Node }[] }[];
        abBars: () => { m: number; bar: number; rect: Node }[];
        positions: () => number[]; at: (n: number) => Node[];
    };
    // (the script's arrays are another realm's: copied here, so that they compare as arrays do)
    const api = {
        shown: raw.shown, invalidateSourcePositions: raw.invalidateSourcePositions, shiftPageFacts: raw.shiftPageFacts,
        positions: () => [...raw.positions()],
        at: (n: number) => [...raw.at(n)],
        spans: (i: number) => [...raw.livePageFacts()[i].spans],
        abBars: () => [...raw.abBars()],
    };
    return { api, svg };
}

const note = (pos: number) => el({ 'data-pos': String(pos) });
const staffBar = (from: number, to: number) => el({ 'data-start': String(from), 'data-span': from + ' ' + to }, ['bar-staff'], 'rect');
const barBox = (m: number) => el({ 'data-m': String(m), 'data-bar': String(m + 1) }, ['bar-box'], 'rect');

describe('the preview reads a page of the picture once', () => {
    it('keeps a page\'s offsets while the page shows the markup they were read from', () => {
        const pages = [page([note(10), note(20), barBox(0), staffBar(10, 25)]), page([note(40), note(30), barBox(1), staffBar(30, 45)])];
        const { api } = harness(pages);
        api.shown([{}, {}]);
        assert.deepEqual(api.positions(), [10, 20, 30, 40]);
        // The next update invalidates the index; neither page changed.
        api.invalidateSourcePositions();
        assert.deepEqual(api.positions(), [10, 20, 30, 40]);
        api.spans(0);
        api.abBars();
        assert.deepEqual(pages.map(p => p.reads), [1, 1]);
    });

    it('reads a changed page again, and that page alone', () => {
        const pages = [page([note(10), note(20)]), page([note(30), note(40)])];
        const { api } = harness(pages);
        const shown: unknown[] = [{}, {}];
        api.shown(shown);
        api.positions();
        // The second page is changed: its record is a new one (applyPages, reflowPages), and so is what it draws.
        shown[1] = {};
        pages[1].nodes[0].e.attrs['data-pos'] = '33';
        api.invalidateSourcePositions();
        assert.deepEqual(api.positions(), [10, 20, 33, 40]);
        assert.deepEqual(pages.map(p => p.reads), [1, 2]);
        // The element at an offset is the page's own, in the page's order.
        assert.equal(api.at(33)[0], pages[1].nodes[0]);
        assert.deepEqual(api.at(30), []);
    });

    it('maps a shifted page through the window, and does not read it', () => {
        // Two parts, the second written after the first: page 1 holds bars of both.
        const pages = [page([note(10), note(110)]), page([note(20), note(120), staffBar(20, 28), staffBar(120, 128)])];
        const { api } = harness(pages);
        api.shown([{}, { stale: false }]);
        api.positions();
        // An edit at 105..106 made two characters longer: what is written from 106 on moves by 2.
        const w = { Prefix: 105, SuffixStart: 106, Delta: 2 };
        api.shiftPageFacts(pages[1], w);
        // (…which the page does where it has shifted the page's own attributes: applyPages)
        assert.match(html, /shownPages\[i\]\.stale = true;\s*shiftPageFacts\(livePages\[i\], ps\.Window\);/);
        api.invalidateSourcePositions();
        assert.deepEqual(api.positions(), [10, 20, 110, 122]);
        assert.deepEqual(api.spans(1).slice(1, 3), [20, 28]);
        assert.deepEqual(api.spans(1).slice(4, 6), [122, 130]);
        assert.deepEqual(pages.map(p => p.reads), [1, 1]);
        // An offset inside the window has no image: the facts are dropped, and the page is read when next asked.
        api.shiftPageFacts(pages[1], { Prefix: 15, SuffixStart: 25, Delta: 1 });
        api.invalidateSourcePositions();
        api.positions();
        assert.deepEqual(pages.map(p => p.reads), [1, 2]);
    });

    it('reads every page when there is no record of the pages shown, and a picture with no pages whole', () => {
        const pages = [page([note(10)]), page([note(30)])];
        const { api } = harness(pages);
        api.positions();
        api.invalidateSourcePositions();
        assert.deepEqual(api.positions(), [10, 30]);
        assert.deepEqual(pages.map(p => p.reads), [2, 2]);
        const flat = harness([page([note(7), note(5)])], false);
        assert.deepEqual(flat.api.positions(), [5, 7]);
        assert.equal(flat.svg.reads, 1);
    });

    it('never paints a click target or a tab digit\'s mask, and counts a bar line\'s other bars', () => {
        const hit = el({ 'data-pos': '10' }, ['nh-hit'], 'rect');
        const mask = el({ 'data-pos': '10', fill: 'white' }, [], 'rect');
        const head = note(10);
        const barLine = el({ 'data-pos': '50', 'data-alt': '60 70' }, [], 'rect');     // (no fill: real ink)
        const pages = [page([hit, mask, head, barLine])];
        const { api } = harness(pages);
        api.shown([{}]);
        assert.deepEqual(api.positions(), [10, 50, 60, 70]);
        assert.deepEqual(api.at(10).map(n => n.e), [head]);
        assert.deepEqual(api.at(60).map(n => n.e), [barLine]);
    });

    it('gives the A-B flags each measure\'s first box, off the pages', () => {
        const again = barBox(0);        // (a bar printed twice: the first box stands for it)
        const pages = [page([barBox(0), barBox(1)]), page([again, barBox(2)])];
        const { api } = harness(pages);
        api.shown([{}, {}]);
        const bars = api.abBars();
        assert.deepEqual(bars.map(b => b.m), [0, 1, 2]);
        assert.deepEqual(bars.map(b => b.bar), [1, 2, 3]);
        assert.equal(bars[0].rect, pages[0].nodes[0]);
        api.abBars();
        assert.deepEqual(pages.map(p => p.reads), [1, 1]);
    });
});

describe('the preview does not have the browser look again at what an update left alone', () => {
    it('has no style rule on [data-pos]: the pointer over a note is the mouse handler\'s class', () => {
        const styles = [...html.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].map(m => m[1].replace(/\/\*[\s\S]*?\*\//g, ''));
        assert.ok(styles.length > 0);
        for (const css of styles) assert.doesNotMatch(css, /\[data-(pos|alt|start|span)/);
        assert.match(html, /\.main-content\.over-note,\s*\.main-content\.over-bar \{\s*cursor: pointer;/);
        assert.match(html, /classList\.toggle\('over-note', !!\(t && t\.closest && t\.closest\('\[data-pos\]'\)\)\);/);
        // The closed hand is written after the pointer: a drag over a note shows it.
        assert.ok(html.indexOf('.main-content.dragging {') > html.indexOf('.main-content.over-note,'));
    });

    it('writes a shifted page\'s offsets only where they moved', () => {
        const written: string[] = [];
        const make = (attrs: Record<string, string>) => ({
            getAttribute: (n: string) => attrs[n] ?? null,
            setAttribute: (n: string, v: string) => { attrs[n] = v; written.push(n + '=' + v); },
        });
        const els = [make({ 'data-pos': '20' }), make({ 'data-pos': '120' }),
            make({ 'data-start': '20', 'data-span': '20 28' }), make({ 'data-start': '120', 'data-span': '120 128' }),
            make({ 'data-pos': '50', 'data-alt': '60 150' })];
        const shiftPage = new vm.Script('(() => { const SOURCE_SELECTOR = "x";\n' + cut('shiftOffset') + '\n' + cut('shiftPage') + '\nreturn shiftPage; })()')
            .runInNewContext({ Number, parseInt, String }) as (p: unknown, w: unknown) => boolean;
        assert.equal(shiftPage({ querySelectorAll: () => els }, { Prefix: 105, SuffixStart: 106, Delta: 2 }), true);
        assert.deepEqual(written, ['data-pos=122', 'data-start=122', 'data-span=122 130', 'data-alt=60 152']);
    });

    it('reads no bar for the scan\'s marks when there is none', () => {
        const asked: string[] = [];
        const svgContainer = { querySelectorAll: (sel: string) => { asked.push(sel); return []; } };
        (new vm.Script('(() => { let flaggedMarks = []; return ' + cut('applyBarFlags') + '; })()')
            .runInNewContext({ svgContainer }) as () => void)();
        assert.deepEqual(asked, ['.flag-frame']);
    });
});
