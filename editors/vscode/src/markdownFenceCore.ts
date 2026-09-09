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

// The editor-free half of the Markdown lys fence (markdownFence.ts holds the
// markdown-it plugin and the language-server round trip): which fences are
// scores, how a fence is keyed in the render cache, and the HTML each state of
// a fence turns into. Nothing here imports vscode, so `npm test` runs it.

import { createHash } from 'crypto';

/** The fence info words that mean "this is a Lily# score": `lys` (the file
 * extension) and `lily#` (the language's own name — the owner's choice over
 * `lilysharp`, 2026-09-09; markdown-it puts no limit on an info string's
 * characters, so the `#` is fine). */
export const FENCE_LANGUAGES: readonly string[] = ['lys', 'lily#'];

/**
 * The language word of a fence's info string — the first word, lower-cased —
 * so ```` ```lys ````, ```` ```LYS ````, ```` ```Lily# ```` and
 * ```` ```lys title="…" ```` all read as their word. Empty for a fence with no info.
 */
export function fenceLanguage(info: string): string {
    return (info.trim().split(/\s+/)[0] || '').toLowerCase();
}

export function isLysFence(info: string): boolean {
    return FENCE_LANGUAGES.includes(fenceLanguage(info));
}

/**
 * The cache key of a fence: a hash of its text. markdown-it re-renders the
 * whole document on every keystroke, so a fence that did not change must hit
 * without a round trip; one that changed by a character must miss.
 */
export function fenceKey(code: string): string {
    return createHash('sha1').update(code).digest('hex');
}

/** What the plugin knows about a fence's picture. */
export type FenceState =
    | { readonly kind: 'pending' }
    | { readonly kind: 'svg'; readonly svg: string }
    | { readonly kind: 'error'; readonly message: string };

export function escapeHtml(text: string): string {
    return text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

/**
 * The size a fence is shown at: pixels per staff space. The server writes its
 * SVG at 10 px per staff space (a 40 px staff, the .lys preview's zoomable
 * canvas); a picture in prose is placed at its natural size, the way lilypond-book
 * places its snippets, and 6 px per staff space is a 24 px staff — LilyPond's
 * 20 pt staff (5 pt per space) on a 96 dpi screen is 6.67. The stylesheet still
 * caps it at the column's width, so a wide system scales down and a short one
 * does not scale up.
 */
export const PIXELS_PER_STAFF_SPACE = 6;

/**
 * The server's SVG document, made an inline element: the XML declaration is
 * dropped (an HTML parser reads `<?xml …?>` as a bogus comment, harmless but
 * noise), the root's width and height are re-stated at the fence's size from its
 * viewBox (staff spaces), and everything else is kept as it is — its own
 * `<style>` block included, which the preview's CSP allows.
 */
export function inlineSvg(svg: string): string {
    const at = svg.indexOf('<svg');
    const doc = at >= 0 ? svg.slice(at) : svg;
    const end = doc.indexOf('>');
    if (end < 0) {
        return doc;
    }
    const root = doc.slice(0, end);
    const viewBox = /viewBox="([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+)"/.exec(root);
    if (!viewBox) {
        return doc;
    }
    const w = (parseFloat(viewBox[3]) * PIXELS_PER_STAFF_SPACE).toFixed(1);
    const h = (parseFloat(viewBox[4]) * PIXELS_PER_STAFF_SPACE).toFixed(1);
    const sized = root
        .replace(/\swidth="[\d.]+"/, ` width="${w}"`)
        .replace(/\sheight="[\d.]+"/, ` height="${h}"`);
    return sized + doc.slice(end);
}

/**
 * The HTML a fence renders to. A pending or failed fence keeps the source in
 * view (as the fenced code it was), so the document stays readable while the
 * server draws, and an error names its line in the writer's own text.
 */
export function fenceHtml(state: FenceState, code: string): string {
    const source = `<pre><code class="language-lys">${escapeHtml(code)}</code></pre>`;
    switch (state.kind) {
        case 'svg':
            return `<div class="lys-fence">${inlineSvg(state.svg)}</div>\n`;
        case 'pending':
            return `<div class="lys-fence lys-fence-pending"><div class="lys-fence-note">Lily#: rendering…</div>${source}</div>\n`;
        case 'error':
            return `<div class="lys-fence lys-fence-error"><div class="lys-fence-note">Lily#: ${escapeHtml(state.message)}</div>${source}</div>\n`;
    }
}

/**
 * A small insertion-ordered cache: a document's fences all stay hot, and the
 * oldest picture goes when a writer has moved on through many more.
 */
export class FenceCache {
    private readonly map = new Map<string, FenceState>();

    constructor(private readonly capacity = 200) { }

    get(key: string): FenceState | undefined {
        return this.map.get(key);
    }

    set(key: string, state: FenceState): void {
        this.map.delete(key);
        this.map.set(key, state);
        while (this.map.size > this.capacity) {
            const oldest = this.map.keys().next().value;
            if (oldest === undefined) {
                break;
            }
            this.map.delete(oldest);
        }
    }

    get size(): number {
        return this.map.size;
    }
}
