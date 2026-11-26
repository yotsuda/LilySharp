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

// "Report to Orecchiante" (Orecchiante's request 23, the owner's wish 2026-11-23;
// docs/orecchiante-report-design.md): bars of a chart the owner has put right — or heard and found
// right — kept in a folder for Orecchiante to make answers from (its tools/seeds.py). The twin of
// Calamo's "Report Misread to OMR" (omrSeedCore.ts), and the owner's alone in the same way: the
// code actions show only when `lilysharp.recordings.seedFolder` is written in settings.json by
// hand — the setting is not contributed, so no one else sees it.
//
// What is reported is a bass, kit or chords row's bar ('bar'), every bar of that row in its section
// as checked ('section': the bars not changed are answers too), or a drum phrase put right with the
// kit bars that play it ('phrase': which of them truly go as it does, the owner says by reporting
// them). Everything is taken at the report: the .lys and the file beside it are copied, and each
// bar goes with its text as the .lys has it now and as Orecchiante read it (the file beside's first
// playing of that bar).
//
// No vscode here: the extension and the tests share it.

/** The setting under `lilysharp` that names the seed folder; not in package.json on purpose. */
export const ORECCHIANTE_SEED_FOLDER_SETTING = 'recordings.seedFolder';

/** One voice of a section and its bars, as lilysharp/voiceBars answers: the voice as a message
 *  names it (`part 'bass'`, `chords 'prog'`) and each bar's text span (start -1: not its own). */
export interface VoiceBars {
    readonly section: string;
    readonly voice: string;
    readonly bars: readonly { readonly start: number; readonly end: number }[];
}

/** The rows Orecchiante writes and reads back. */
export type SeedPart = 'bass' | 'kit' | 'chords';

/** The part a voice reports as: `part bass`, `part kit`, any chords row; undefined for others. */
export function seedPartOf(voice: string): SeedPart | undefined {
    const m = /^(part|chords) '(.*)'$/.exec(voice);
    if (!m) { return undefined; }
    if (m[1] === 'chords') { return 'chords'; }
    return m[2] === 'bass' ? 'bass' : m[2] === 'kit' ? 'kit' : undefined;
}

/** A bar of the file beside the chart, as far as a report reads it. */
export interface SideBar {
    readonly bar: number;
    readonly section?: string;
    readonly barOfSection?: number;
    readonly bass?: string;
    readonly drums?: Record<string, string>;
    readonly chords?: readonly { readonly symbol?: string }[];
}

/** The file beside's bars, taken from its raw items (anything not a bar left out). */
export function sideBarsOf(raw: Record<string, unknown>): SideBar[] {
    const bars = Array.isArray(raw.bars) ? raw.bars : [];
    return bars.filter((b): b is SideBar => !!b && typeof b === 'object' && typeof (b as SideBar).bar === 'number');
}

/** The bars played of bar `barOfSection` of `section`: the file beside's `bar` numbers, ascending. */
export function playedOf(side: readonly SideBar[], section: string, barOfSection: number): number[] {
    return side.filter(b => b.section === section && b.barOfSection === barOfSection).map(b => b.bar).sort((a, b) => a - b);
}

/** How many bars the file beside gives a section (its highest barOfSection), 0 when none. */
export function sideSectionLength(side: readonly SideBar[], section: string): number {
    return side.filter(b => b.section === section).reduce((n, b) => Math.max(n, b.barOfSection ?? 0), 0);
}

/** What Orecchiante read in a bar for the part: the bass's music, the drums' lines, the chords'
 *  symbols; null when the bar has none (a bar of no bass heard). */
export function readOf(bar: SideBar | undefined, part: SeedPart): string | Record<string, string> | string[] | null {
    if (!bar) { return null; }
    if (part === 'bass') { return typeof bar.bass === 'string' ? bar.bass : null; }
    if (part === 'kit') { return bar.drums && typeof bar.drums === 'object' ? { ...bar.drums } : null; }
    return Array.isArray(bar.chords) ? bar.chords.map(c => String(c?.symbol ?? '')) : null;
}

/** The phrases of the .lys written on one line each, as Orecchiante writes them
 *  (`phrase hatsA { hh8 8 4 8 8 8 8 | }`): name → body, its closing bar line taken off. */
export function phrasesOf(text: string): Map<string, string> {
    const out = new Map<string, string>();
    for (const m of text.matchAll(/^[ \t]*phrase[ \t]+([\p{L}_][\p{L}\p{N}_]*)[ \t]*\{(.*)\}[ \t]*(?:\/\/.*)?$/gmu)) {
        out.set(m[1], m[2].trim().replace(/\s*\|$/, '').trim());
    }
    return out;
}

/** The phrases a drum groove plays (`phrase beatA { drums { hatsA backA kickA } }`), else undefined. */
export function grooveOf(phrases: ReadonlyMap<string, string>, name: string): string[] | undefined {
    const body = phrases.get(name);
    const m = body ? /^drums\s*\{(.*)\}$/s.exec(body) : null;
    if (!m) { return undefined; }
    const names = m[1].trim().split(/\s+/).filter(n => n !== '');
    return names.length > 0 && names.every(n => phrases.has(n)) ? names : undefined;
}

/** A drum groove written as steps (`phrase beatA { steps { hh "x... x..." sn ".... x..." } }`,
 *  `steps 4. { … }`; Orecchiante 0.18.0, its request 26): drum → its line, as the file beside's
 *  `drums` has them; else undefined. A drum written twice keeps both (`hh`, `hh 2`). */
export function stepsOf(phrases: ReadonlyMap<string, string>, name: string): Record<string, string> | undefined {
    const body = phrases.get(name);
    const m = body ? /^steps(?:\s+[0-9.]+)?\s*\{(.*)\}$/s.exec(body) : null;
    if (!m) { return undefined; }
    const lines: Record<string, string> = {};
    const rest = m[1].replace(/([\p{L}_][\p{L}\p{N}_]*)\s*"([^"]*)"/gu, (_, drum: string, line: string) => {
        let key = drum;
        for (let n = 2; key in lines; n++) { key = `${drum} ${n}`; }
        lines[key] = line;
        return ' ';
    });
    return rest.trim() === '' && Object.keys(lines).length > 0 ? lines : undefined;
}

/** What a kit bar of groove `name` plays: the bodies of a `drums { }` groove's phrases, or a
 *  steps groove's lines; undefined when `name` is not a groove. */
export function grooveLines(phrases: ReadonlyMap<string, string>, name: string): Record<string, string> | undefined {
    const names = grooveOf(phrases, name);
    return names ? Object.fromEntries(names.map(n => [n, phrases.get(n)!])) : stepsOf(phrases, name);
}

/** The grooves that play phrase `name`: the `drums { }` grooves naming it, or itself when it is a
 *  steps groove (its own lines are what it plays). */
export function groovesPlaying(phrases: ReadonlyMap<string, string>, name: string): string[] {
    const by = [...phrases.keys()].filter(g => grooveOf(phrases, g)?.includes(name));
    return stepsOf(phrases, name) ? [name, ...by] : by;
}

/** One bar reported. */
export interface SeedItem {
    readonly section: string;
    readonly barOfSection: number;
    /** The file beside's bars that played it (the first is what the .lys was written from). */
    readonly bars: number[];
    /** The bar as the .lys has it at the report (null: its text is not its own, inside a `R1*4`). */
    readonly bar: string | null;
    /** A kit bar written as a groove (`beatA`, or `%` after one): its name and its phrases' bodies. */
    readonly groove?: string;
    readonly lines?: Record<string, string>;
    /** What Orecchiante read in the first playing (bars[0]). */
    readonly barWhenRead: string | Record<string, string> | string[] | null;
}

/** The bars of a voice, each with what it plays and what was read (a `%` names the groove of the
 *  bar it repeats). */
export function itemsOf(text: string, voice: VoiceBars, part: SeedPart, side: readonly SideBar[],
    pick: (barOfSection: number) => boolean = () => true): SeedItem[] {
    const phrases = part === 'kit' ? phrasesOf(text) : new Map<string, string>();
    const items: SeedItem[] = [];
    let groove: string | undefined;
    voice.bars.forEach((span, i) => {
        const k = i + 1;
        const bar = span.start >= 0 && span.end >= span.start ? text.slice(span.start, span.end).trim() : null;
        if (part === 'kit') {
            groove = bar === '%' ? groove : bar !== null && grooveLines(phrases, bar) ? bar : undefined;
        }
        if (!pick(k)) { return; }
        const played = playedOf(side, voice.section, k);
        const first = side.find(b => b.bar === played[0]);
        const lines = groove ? grooveLines(phrases, groove) : undefined;
        items.push({
            section: voice.section, barOfSection: k, bars: played, bar,
            ...(groove && lines ? { groove, lines } : {}),
            barWhenRead: readOf(first, part),
        });
    });
    return items;
}

/** The voice and bar a caret is in: the innermost voice whose bars span it. */
export function caretBar(voices: readonly VoiceBars[], offset: number): { voice: VoiceBars; barOfSection: number } | undefined {
    for (const voice of voices) {
        const i = voice.bars.findIndex(b => b.start >= 0 && offset >= b.start && offset <= b.end);
        if (i >= 0) { return { voice, barOfSection: i + 1 }; }
    }
    return undefined;
}

/** The phrase whose one-line definition the caret is on, when a groove plays it or it is a steps groove. */
export function caretPhrase(text: string, offset: number): string | undefined {
    const lineStart = text.lastIndexOf('\n', offset - 1) + 1;
    const lineEnd = text.indexOf('\n', offset);
    const line = text.slice(lineStart, lineEnd < 0 ? text.length : lineEnd);
    const m = /^[ \t]*phrase[ \t]+([\p{L}_][\p{L}\p{N}_]*)[ \t]*\{/u.exec(line);
    if (!m) { return undefined; }
    const phrases = phrasesOf(text);
    return groovesPlaying(phrases, m[1]).length > 0 ? m[1] : undefined;
}

/** The kit bars that play a phrase through a groove (`beatA`, or `%` after it), or as the steps groove itself. */
export function phraseItems(text: string, kits: readonly VoiceBars[], phrase: string, side: readonly SideBar[]): SeedItem[] {
    const grooves = new Set(groovesPlaying(phrasesOf(text), phrase));
    return kits.flatMap(v => itemsOf(text, v, 'kit', side).filter(it => it.groove !== undefined && grooves.has(it.groove)));
}

export type SeedKind = 'bar' | 'section' | 'phrase';

/** seed.json: what Orecchiante reads. The copies beside it are named by their fields. */
export interface OrecchianteSeed {
    readonly seed: 1;
    readonly kind: SeedKind;
    readonly reported: string;
    readonly note: string;
    readonly part: SeedPart;
    /** The section of a 'bar' or 'section' report; null for a phrase. */
    readonly section: string | null;
    /** A 'phrase' report: the phrase and its body now, and the grooves that play it. */
    readonly phrase: { readonly name: string; readonly body: string; readonly grooves: string[] } | null;
    /** Every section reported has as many bars in the .lys as the file beside gives it — false: the
     *  chart's bars were added or taken out since, and `bars` may name the wrong bars. */
    readonly linedUp: boolean;
    readonly items: SeedItem[];
    /** Where the .lys and the file beside were (never read again) and their copies' names. */
    readonly lysWas: string;
    readonly lysCopy: string;
    readonly sideFileWas: string;
    readonly sideFileCopy: string;
    /** The recording, the file beside's `audio` found from where it was (not copied). */
    readonly recording: string | null;
}

function baseName(p: string): string {
    return p.split(/[\\/]/).pop() ?? p;
}

function safe(s: string): string {
    return s.replace(/[^\p{L}\p{N}_-]+/gu, '_').replace(/^_+|_+$/g, '') || 'x';
}

/** The report's folder: when, the song, what — "20261123-153012-like-a-virgin-bar5-bass",
 *  "…-A2-kit" for a section, "…-hatsA-kit" for a phrase. */
export function orecchianteSeedDirName(when: Date, lys: string, kind: SeedKind, part: SeedPart, what: { firstPlayed?: number; section?: string; phrase?: string }): string {
    const p = (n: number) => String(n).padStart(2, '0');
    const stamp = `${when.getFullYear()}${p(when.getMonth() + 1)}${p(when.getDate())}-${p(when.getHours())}${p(when.getMinutes())}${p(when.getSeconds())}`;
    const where = kind === 'phrase' ? safe(what.phrase ?? 'phrase')
        : kind === 'section' ? safe(what.section ?? 'section')
        : what.firstPlayed !== undefined ? `bar${what.firstPlayed}` : safe(what.section ?? 'bar');
    return [stamp, safe(baseName(lys).replace(/\.lys$/i, '')), where, part].join('-');
}

/** The record. */
export function orecchianteSeedOf(when: Date, kind: SeedKind, part: SeedPart, items: SeedItem[], note: string,
    where: { lys: string; sideFile: string; recording: string | null; section: string | null; linedUp: boolean;
        phrase?: { name: string; body: string; grooves: string[] } }): OrecchianteSeed {
    return {
        seed: 1, kind, reported: when.toISOString(), note: note.trim(), part,
        section: where.section, phrase: where.phrase ?? null, linedUp: where.linedUp, items,
        lysWas: where.lys, lysCopy: baseName(where.lys),
        sideFileWas: where.sideFile, sideFileCopy: baseName(where.sideFile),
        recording: where.recording,
    };
}
