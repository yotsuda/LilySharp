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

// The singing voice's pure half (docs/singing-design.md §5): the server's sung voice turned
// into the score the singing engine (Cantoria) reads, the key a rendered voice is cached by,
// and the lines of `cantoria serve`'s wire. No vscode, no process here: test/singerCore.test.ts
// drives it under node.

import { createHash } from 'crypto';
import { VOICE_LEAD_S } from './webview/playbackCore';

/** One note of `lilysharp/sung`, in seconds: its syllable `L` (null on a melisma), `M` a melisma
 *  carrying the previous syllable's vowel on, `B` a breath after it, `S` the source offset. */
export interface SungNoteDto { T: number; D: number; P: number; L?: string | null; M?: boolean; B?: boolean; S?: number; V?: number | null; E?: number | null; I?: boolean; H?: boolean;
    /** A written vibrato (`@vibrato`, a trial sung by DiffSinger voices): its swing each way in cents, its swings a second. */
    Vb?: number | null; Vr?: number | null;
    /** What an `@sing(…)` writes on the note: settings held from it (`Sg`), and arrived at with it (`St`, `NAME to VALUE`). */
    Sg?: Record<string, number | string> | null; St?: Record<string, number | string> | null }

/** A part's words on its notes, as the server answers `lilysharp/sung`. */
/** `InScore`: the score shown sings this part itself; false for a voice the file's other lyrics could sing (off until the menu turns it on; session 1163). */
export interface SungVoiceDto { Part: string; Key?: string; Language: string; Notes: SungNoteDto[]; Warnings?: string[] | null; InScore?: boolean; Singer?: string | null; Gender?: string | null; Track?: string | null;
    /** The singer's own name (`singer carol female …`), or null for a line that gives none. */
    Name?: string | null;
    /** How the voice sings, as its singer line's `with …` writes it: a name its number or its word, by the names the voice's engine takes. */
    Sing?: Record<string, number | string> | null }

/** The score Cantoria reads (its README): seconds, MIDI pitch, the syllable as pronounced. */
export interface CantoriaNote { start: number; duration: number; pitch: number; lyric?: string; melisma?: boolean; breath?: boolean; level?: number; levelEnd?: number; inhale?: boolean; hyphen?: boolean; vibrato?: number; vibratoRate?: number;
    sing?: Record<string, number | string>; singTo?: Record<string, number | string> }
export interface CantoriaScore { sampleRate: number; language: string; sing?: Record<string, number | string>; notes: CantoriaNote[] }

/** The rate the voice is asked for — NEUTRINO's own, so nothing is resampled on the way. */
export const VOICE_SAMPLE_RATE = 48000;

/** The Cantoria extension, the engine's own home on the Marketplace (docs/singing-design.md §5). */
export const CANTORIA_EXTENSION_ID = 'yotsuda.cantoria';

/** A voice the Cantoria extension lists: `id` is `engine/name` (`neutrino/MERROW`), what its
 *  `sing` takes and its setting holds. */
export interface CantoriaVoice { id: string; engine: string; name: string; label?: string; language?: string; languages?: string[]; gender?: string; licence?: string }

/** Whether a voice says it sings `language`: its list of languages, else its one. */
export function singsLanguage(voice: Pick<CantoriaVoice, 'language' | 'languages'>, language: string): boolean {
    return voice.languages?.includes(language) ?? voice.language === language;
}

/** What that extension's `activate` returns, as far as Lily# reads it (its README, "For extension
 *  authors"). `status().voice` is the setting's voice as `engine/name`; `voices()` came with
 *  Cantoria 0.2 and an older extension has none. */
export interface CantoriaApi {
    readonly version: string;
    readonly apiVersion: number;
    status(): Promise<{ ready: boolean; voice: string; problems: string[] }>;
    voices?(): Promise<CantoriaVoice[]>;
    /** `options.signal` lets go of a render no longer wanted (a Cantoria that does not know it sings on). */
    /** `options.onProgress` (a Cantoria after 0.4.0; an older one never calls it) hears how far the
     *  render is: what has to be sung anew, and how much of it is done (SingProgress). */
    sing(score: CantoriaScore, options?: { voice?: string; signal?: AbortSignal; onProgress?: (progress: SingProgress) => void }): Promise<{ wav: string; duration: number; cached?: boolean }>;
    /** The settings a voice takes in a score's `sing` (a Cantoria after 0.4.0; an older one has none). */
    parameters?(voice?: string): Promise<SingParameter[]>;
    release(): void;
    /** The voice a `.lys` file's `singer` string comes to on this machine, and what to tell the
     *  writer when it was not the one asked for. Cantoria 0.4 on. */
    resolveSinger?(singer: string, options?: { language?: string; gender?: string }): Promise<{ voice: string; note?: string }>;
}

/** Whether a `singer`'s written gender lets this voice be offered: no gender written, a voice that names none, or the same one. */
export function fitsGender(voice: Pick<CantoriaVoice, 'gender'>, gender?: string | null): boolean {
    return !gender || !voice.gender || voice.gender === gender;
}

const LANGUAGE_CODES: Record<string, string> = { japanese: 'ja', english: 'en', chinese: 'zh', korean: 'ko' };

/** The answer of the chat's voice list (owner's request 2026-11-09, session 1202): the names a score can
 *  write, narrowed by provider, gender and language. As in the completion, a voice that does not state its
 *  gender stays when a gender is asked for, and the answer says so. */
export function voiceListText(voices: readonly CantoriaVoice[], filter: { provider?: string; gender?: string; language?: string }): string {
    const provider = (filter.provider ?? '').trim().toLowerCase();
    const gender = (filter.gender ?? '').trim().toLowerCase();
    const written = (filter.language ?? '').trim().toLowerCase();
    const language = LANGUAGE_CODES[written] ?? written;
    if (gender && gender !== 'male' && gender !== 'female') { return `"${filter.gender}" is no gender of a voice: ask for male or female.`; }
    if (voices.length === 0) { return 'No singing voice is available on this machine. A score can still be written without a singer.'; }
    const providers = [...new Set(voices.map(v => v.engine))].sort();
    if (provider && !providers.includes(provider)) { return `No provider named "${filter.provider}" has voices here. There are: ${providers.join(', ')}.`; }
    const found = byVoiceId(voices.filter(v => (!provider || v.engine === provider) && fitsGender(v, gender) && (!language || singsLanguage(v, language))));
    const asked = [provider, gender, language].filter(Boolean).join(', ');
    if (found.length === 0) { return `No voice matches (${asked}). ${voices.length} voices are available with no condition.`; }
    const lines = found.map(v => {
        const languages = v.languages ?? (v.language ? [v.language] : []);
        return [`${v.id}:`, v.label && v.label !== v.name ? v.label : undefined, v.gender ?? 'gender not stated',
            languages.length > 0 ? `sings ${languages.join(', ')}` : 'languages not stated', v.licence].filter(Boolean).join(' · ').replace(': · ', ': ');
    });
    return [`${found.length} voice${found.length === 1 ? '' : 's'}${asked ? ` (${asked})` : ''}:`, ...lines, '',
        'Write a name after singer, on a line of its own at the top level: singer "neutrino/MERROW" on melody. Two lines with the same words on one part sing it together. A voice sings only the languages it lists.'].join('\n');
}

/** What the editor says when a voice cannot be listed, and the buttons that go on from it. */
export interface VoiceGuide { label: string; detail: string; documentation: string; message: string; actions: readonly { label: string; command?: string; url?: string }[] }

/** Where the steps to install each engine are (site/sing-body.html, #install). */
export const VOICE_INSTALL_GUIDE = 'https://yotsuda.github.io/LilySharp/sing.html#install';
const HOW_TO_INSTALL = { label: 'How to Install a Voice', url: VOICE_INSTALL_GUIDE };
const CHOOSE_DIFFSINGER = { label: 'Choose DiffSinger Folder…', command: 'cantoria.chooseVoicesFolder' };
const VOICE_SETUP_ACTIONS: VoiceGuide['actions'] = [
    HOW_TO_INSTALL,
    { label: 'Choose NEUTRINO Folder…', command: 'cantoria.chooseNeutrinoFolder' },
    CHOOSE_DIFFSINGER,
    { label: 'Choose VOICEVOX Folder…', command: 'cantoria.chooseVoicevoxFolder' },
];
const GUIDE_LINK = `[How to install a voice](${VOICE_INSTALL_GUIDE})`;
const ENGINE_LINKS = '[NEUTRINO](https://studio-neutrino.com/), [DiffSinger](https://github.com/openvpi/DiffSinger) or [VOICEVOX](https://voicevox.hiroshiba.jp/)';
const STILL_WRITTEN = 'The score can still be written: a name here is sung once that voice is installed.';

/** What the editor says when no singing voice is installed: with the Cantoria extension, that an engine
 *  is missing; without it, that the extension is the first step. */
export function noVoiceGuide(cantoria: boolean): VoiceGuide {
    return cantoria
        ? {
            label: 'No singing voice is installed',
            detail: 'Set up a voice…',
            documentation: `Cantoria has no voice to sing with. Install ${ENGINE_LINKS}, then choose its folder: take this line for the buttons.\n\n${GUIDE_LINK}.\n\n${STILL_WRITTEN}`,
            message: 'Lily#: no singing voice is installed. Cantoria sings with NEUTRINO, DiffSinger or VOICEVOX: install one, then choose its folder.',
            actions: VOICE_SETUP_ACTIONS,
        }
        : {
            label: 'No singing voice: Cantoria is not installed',
            detail: 'Install Cantoria…',
            documentation: `Voices are sung by the Cantoria extension, with ${ENGINE_LINKS}. Take this line to install the extension, then choose an engine's folder there.\n\n${GUIDE_LINK}.\n\n${STILL_WRITTEN}`,
            message: 'Lily#: singing a score with a voice needs the Cantoria extension.',
            actions: [],
        };
}

const LANGUAGE_NAMES: Record<string, string> = { ja: 'Japanese', en: 'English', zh: 'Chinese', ko: 'Korean' };

/** What the editor says when voices are installed and none sings the words' language (owner's
 *  request 2026-11-10): what they do sing, and what would sing this one — NEUTRINO and VOICEVOX sing
 *  Japanese, a DiffSinger voice Japanese and, with that language's dsdict, English, Chinese and Korean (Cantoria's README). */
export function noLanguageGuide(language: string, voices: readonly CantoriaVoice[]): VoiceGuide {
    const name = (code: string) => LANGUAGE_NAMES[code] ?? code;
    const sung = [...new Set(voices.flatMap(v => v.languages ?? (v.language ? [v.language] : [])))].map(name);
    const installed = sung.length > 0 ? `The installed voices sing ${sung.join(', ')}.` : 'The installed voices do not say what they sing.';
    const wanted = name(language);
    const words = 'If the words are in another language, say so with `language` on the file or the lyrics track.';
    const base = { label: `No installed voice sings ${wanted}`, detail: 'What can sing it…' };
    if (language === 'ja') {
        return { ...base,
            documentation: `${installed} Install ${ENGINE_LINKS}, then choose its folder: take this line for the buttons. ${words}\n\n${GUIDE_LINK}.\n\n${STILL_WRITTEN}`,
            message: `Lily#: no installed voice sings ${wanted}. NEUTRINO, VOICEVOX and DiffSinger voices do: install one, then choose its folder.`,
            actions: VOICE_SETUP_ACTIONS };
    }
    if (language === 'en' || language === 'zh' || language === 'ko') {
        return { ...base,
            documentation: `${installed} A [DiffSinger](https://github.com/openvpi/DiffSinger) voice with a \`dsdict-${language}.yaml\` sings ${wanted}: install one, then choose its folder. ${words}\n\n${GUIDE_LINK}.\n\n${STILL_WRITTEN}`,
            message: `Lily#: no installed voice sings ${wanted}. A DiffSinger voice with a dsdict-${language}.yaml does: install one, then choose its folder.`,
            actions: [HOW_TO_INSTALL, CHOOSE_DIFFSINGER] };
    }
    return { ...base,
        detail: `Cantoria does not sing ${wanted} yet`,
        documentation: `${installed} The engines Cantoria drives do not sing ${wanted} yet, so another voice would not help. ${words}\n\nThe words are still printed.`,
        message: `Lily#: no installed voice sings ${wanted}, and the engines Cantoria drives do not sing it yet. The words are still printed.`,
        actions: [] };
}

/** What tells a voice from the others of its part: the menu's switches and the page's sounds go by it. */
export function sungKey(voice: Pick<SungVoiceDto, 'Part' | 'Key'>): string {
    return voice.Key || voice.Part;
}

/** The name a voice goes by in the practice menu (docs/lyrics-singer-design.md §7): what follows its
 *  engine (`neutrino/MERROW` is MERROW). Undefined for the part's default voice. */
function voiceMenuName(singer: string | null | undefined): string | undefined {
    if (!singer) { return undefined; }
    return singer.includes('/') ? singer.slice(singer.indexOf('/') + 1) : singer;
}

/** Whether a named singer's voice is on the staff a score prints for her: the part made of her lines
 *  (Core's LyricSinger.PartOfSinger: her name, a mark, a number). */
function onOwnStaff(voice: Pick<SungVoiceDto, 'Part' | 'Name'>): boolean {
    return !!voice.Name && voice.Part.startsWith(voice.Name + '__');
}

/** Each voice's line in the practice menu, by its key: who sings it, with the part beside a voice
 *  that sings several; the part alone for its default voice. */
export function voiceMenuLabels(voices: readonly Pick<SungVoiceDto, 'Part' | 'Key' | 'Singer' | 'Name'>[]): Record<string, string> {
    // A singer the file names goes by that name (owner's request 2026-11-12: `singer carol
    // female "…" sings …` is carol in the menu, not her voice's name); the others by the voice.
    const called = (v: Pick<SungVoiceDto, 'Singer' | 'Name'>) => v.Name || voiceMenuName(v.Singer);
    const uses = new Map<string, number>();
    for (const v of voices) {
        const name = called(v);
        if (name) { uses.set(name, (uses.get(name) ?? 0) + 1); }
    }
    return Object.fromEntries(voices.map(v => {
        const name = called(v);
        // A named singer on the staff a score prints for her (the part is called as she is) needs no part beside the name.
        return [sungKey(v), !name ? v.Part : (uses.get(name) ?? 0) > 1 && !onOwnStaff(v) ? `${name} (${v.Part})` : name];
    }));
}

/** What a voice's line says when hovered: who sings what, on which part. */
export function voiceMenuTitle(label: string, track: string | null | undefined, part: string): string {
    return !track || track === 'lyrics' ? `${label} sings ${part}` : `${label} sings ${track} on ${part}`;
}

/** What a voice's line says when hovered, of the voice itself: a named singer with the voice that
 *  sings her beside the name (`carol (MERROW) sings words on lead`), and — on the staff a score
 *  prints for her, whose part and words are made for it — only that it is her staff. */
export function voiceMenuTitleOf(voice: Pick<SungVoiceDto, 'Part' | 'Singer' | 'Name' | 'Track'>, label: string): string {
    if (!voice.Name) { return voiceMenuTitle(label, voice.Track, voice.Part); }
    const by = voiceMenuName(voice.Singer);
    const who = by ? `${label} (${by})` : label;
    // Short (owner's report 2026-11-12: the text over a voice's button was long and hard to
    // read): on the staff a score prints for her there is nothing more to say than who.
    return onOwnStaff(voice) ? who : voiceMenuTitle(who, voice.Track, voice.Part);
}

/** All the practice menu's voice lines say, for telling that an edit changed them: who sings
 *  (owner's report 2026-11-10: a `singer` line rewritten, the open menu kept the old name), which
 *  lines there are, what each sings, and whether the score sings it. */
export function voiceMenuKey(voices: readonly Pick<SungVoiceDto, 'Part' | 'Key' | 'Singer' | 'Name' | 'Track' | 'InScore'>[]): string {
    const labels = voiceMenuLabels(voices);
    return JSON.stringify(voices.map(v => [sungKey(v), labels[sungKey(v)], v.Track ?? null, v.InScore !== false]));
}

/** What the status bar says, once, of a part with words in the score and no voice asked for them. */
export function printedWordsNote(part: string): string {
    return `the words of ${part} are printed, not sung: add a line of its own, singer on ${part}`;
}

/** One choice between a `singer`'s quotes. */
export interface SingerChoice { label: string; detail: string }

/**
 * What the editor offers between a `singer`'s quotes (owner's request 2026-11-09, session 1168:
 * only the names right for the language): the installed voices that sing `language` (all, with none given). A voice that
 * does not say what it sings is left out; with `gender` (`singer male "…"`, sessions 1173 and
 * 1174), so is one that says it is the other.
 */
export function singerChoices(voices: readonly CantoriaVoice[], language: string | undefined, gender?: string | null): SingerChoice[] {
    return byVoiceId(voices.filter(v => (!language || singsLanguage(v, language)) && fitsGender(v, gender)))
        .map(v => ({ label: v.id, detail: v.label && v.label !== v.name ? v.label : v.name }));
}

/** The singer's OWN name on a singer line, where one stands (`singer alice female "…" sings`, owner's
 *  decision 2026-11-12): a word before the gender or the voice that is neither a gender nor `sings`,
 *  and the space after it. Optional, and no group of its own: the readers of a singer line take
 *  the same groups with it or without. Not `\w`: a singer may be called ようこ. */
export const SINGER_LANGUAGES = 'japanese|english|chinese|korean';

/** The language a singer line's head may say after the gender (`singer alice female japanese "…"`,
 *  owner's decisions 2026-11-12), and the space after it. Optional, no group of its own. */
export const SINGER_LANGUAGE = String.raw`(?:(?:${SINGER_LANGUAGES})(?![\w-])[ \t]*)?`;

// …the name, and a language that stands BEFORE the gender (the two words may come in either order).
export const SINGER_NAME = String.raw`(?:(?!(?:male|female|sings|${SINGER_LANGUAGES})(?![\w-]))[^\s"{},/]+[ \t]+)?(?:(?:${SINGER_LANGUAGES})(?![\w-])[ \t]+)?`;

/** Whether a score names who sings: a `singer` followed by a name or a gender, off a comment. */
export function writesSinger(text: string): boolean {
    return text.split('\n').some(line => /^[ \t]*singer\b.*\b(?:sings|on)\b/.test(line.split('//')[0]));
}

/** Whether a line's text before the caret ends with `singer ` or `singer male `: a space that opens the list of voices. */
export function afterSingerWord(before: string): boolean {
    return new RegExp(String.raw`(?<![\w-])singer[ \t]+${SINGER_NAME}(?:(?:male|female)[ \t]+)?${SINGER_LANGUAGE}$`).test(before) && !before.includes('//');
}

/** Whether the text before a line leaves it at the top level: every brace opened is closed
 *  (comments and quoted text left out of the count). */
export function atTopLevel(before: string): boolean {
    let depth = 0;
    for (const line of before.split('\n')) {
        for (const c of line.split('//')[0].replace(/"[^"]*"?/g, '')) {
            if (c === '{') { depth++; } else if (c === '}') { depth--; }
        }
    }
    return depth <= 0;
}

/**
 * Whether a voice taken from a `singer`'s list goes on to `sings` and the list of the file's
 * lyrics (owner's request 2026-11-11): on a `singer` line of the top level that says no `sings`
 * yet and has nothing after the name being written — `after` is the line from the caret, or from
 * past the closing quote when the caret is inside quotes. A `singer` in a score or a part, and a
 * line whose `sings` is there already, keep the caret in the name as before.
 */
export function singerGoesOnToSings(before: string, line: string, after: string): boolean {
    return atTopLevel(before) && /^[ \t]*singer(?![\w-])/.test(line)
        && !/(?<![\w-])(?:sings|on)(?![\w-])/.test(line.split('//')[0].replace(/"[^"]*"/g, '""'))
        && after.split('//')[0].trim() === '';
}

/** The name of a `singer "…"` the caret stands on, anywhere on the `singer`: where it is on the line. */
export function singerValueAt(line: string, character: number): { start: number; end: number; value: string } | undefined {
    for (const m of line.matchAll(new RegExp(String.raw`(?<![\w-])singer[ \t]+${SINGER_NAME}(?:(?:male|female)[ \t]+)?${SINGER_LANGUAGE}"([^"]*)"`, 'g'))) {
        if (character < m.index || character > m.index + m[0].length) { continue; }
        const end = m.index + m[0].length - 1;
        return { start: end - m[1].length, end, value: m[1] };
    }
    return undefined;
}

/** Whether the caret stands on a `singer` line's `male` / `female` (owner's request 2026-11-12): the
 *  step keys there change the word, not the voice named after it. */
export function onSingerGender(line: string, character: number): boolean {
    for (const m of line.split('//')[0].matchAll(new RegExp(String.raw`(?<![\w-])singer[ \t]+${SINGER_NAME}(male|female)(?![\w-])`, 'g'))) {
        const end = m.index + m[0].length;
        if (character >= end - m[1].length && character <= end) { return true; }
    }
    return false;
}

/** Where on the line to ask what language a `singer`'s words are in, wherever on the `singer` the caret
 *  is (owner's report 2026-11-09, session 1200: just past the closing quote the answer was none, and a voice
 *  of several languages said its first one's phrase): inside the name, or at the end of a bare gender. */
export function singerLanguageColumn(line: string, character: number): number {
    const named = singerValueAt(line, character);
    if (named) { return named.start; }
    for (const m of line.matchAll(new RegExp(String.raw`(?<![\w-])singer[ \t]+${SINGER_NAME}(?:male|female)(?![\w-])`, 'g'))) {
        if (character >= m.index && character <= m.index + m[0].length) { return m.index + m[0].length; }
    }
    return character;
}

/**
 * What the `singer` that holds `from`..`to` on this line writes, for hearing it (owner's request
 * 2026-11-09, session 1180): its name, its gender if it has no name, '' for `singer ""`. Undefined off
 * one, in a comment and inside another string.
 */
export function singerWrittenAt(line: string, from: number, to: number): string | undefined {
    for (const m of line.matchAll(new RegExp(String.raw`(?<![\w-])singer[ \t]+${SINGER_NAME}(?:(male|female)(?![\w-])(?:[ \t]+(?:${SINGER_LANGUAGES})(?![\w-]))?(?:[ \t]+"([^"]*)")?|"([^"]*)")`, 'g'))) {
        const before = line.slice(0, m.index);
        if (before.includes('//') || (before.split('"').length - 1) % 2 === 1) { continue; }
        if (from >= m.index && to <= m.index + m[0].length) { return m[2] || m[3] || m[1] || ''; }
    }
    return undefined;
}

/**
 * The voice a step key moves to (owner's requests 2026-11-09, sessions 1170, 1172, 1173 and 1188):
 * Up (`direction` 1) is the voice above in the whole list, Down the one below, from one engine into
 * the next; with `acrossEngines` (PageUp `direction` 1, upward too), the first voice of the engine above or below. Neither goes round.
 * A `singer` that names none of `voices` (a role, an empty string) enters the list at the end the key
 * moves away from. Undefined when there is nowhere else to go.
 */
export function stepVoice(voices: readonly Pick<CantoriaVoice, 'id' | 'engine'>[], current: string, direction: 1 | -1, acrossEngines: boolean): string | undefined {
    if (voices.length === 0) { return undefined; }
    const here = voices.find(v => v.id === current);
    if (acrossEngines) {
        const engines = [...new Set(voices.map(v => v.engine))];
        if (engines.length < 2) { return undefined; }
        const engine = engines[here ? engines.indexOf(here.engine) - direction : direction > 0 ? engines.length - 1 : 0];
        return engine === undefined ? undefined : voices.find(v => v.engine === engine)?.id;
    }
    if (!here) { return (direction > 0 ? voices[voices.length - 1] : voices[0]).id; }
    return voices[voices.findIndex(v => v.id === current) - direction]?.id;
}

/** Voices in the order they are offered and stepped through: by name, so by engine first. */
export function byVoiceId<T extends { id: string }>(voices: readonly T[]): T[] {
    return [...voices].sort((a, b) => a.id.localeCompare(b.id, 'en'));
}

/** What a render costs before its length counts: the bar's estimate (session 1178). */
const VOICE_RENDER_BASE_MS = 1500;
/** A first guess at the engine's speed, in milliseconds per second sung, until a render is timed. */
export const VOICE_RENDER_FIRST_RATE = 100;

/** How many seconds a voice lasts at `speed`, from its first note's start at 0. */
export function sungSeconds(dto: Pick<SungVoiceDto, 'Notes'>, speed: number): number {
    return dto.Notes.reduce((end, n) => Math.max(end, n.T + n.D), 0) / speed;
}

/** How long the engine should take over `seconds` sung, at its `rate`. */
export function voiceRenderEstimateMs(seconds: number, rate: number): number {
    return VOICE_RENDER_BASE_MS + seconds * rate;
}

/** The engine's speed after a render of `seconds` took `elapsedMs`, the earlier speed counting half. */
export function learnRenderRate(rate: number, elapsedMs: number, seconds: number): number {
    if (seconds <= 0) { return rate; }
    const measured = Math.max(0, elapsedMs - VOICE_RENDER_BASE_MS) / seconds;
    return Math.min(3000, Math.max(10, (rate + measured) / 2));
}

/** Bumped when the phrase below changes, or how it is sung (5: VOICEVOX's low notes, 2026-11-10):
 *  the samples kept on disk are named with it. */
export const VOICE_SAMPLE_VERSION = 5;
/** The same for the English phrase, on its own so that a change to it leaves the Japanese samples. */
export const VOICE_SAMPLE_EN_VERSION = 5;

/** The time each stage of hearing a voice took, for the output channel (owner's request 2026-11-09, session 1171). */
export class StageClock {
    private readonly stages: string[] = [];
    private last: number;

    constructor(readonly startedAt: number, private readonly now: () => number = Date.now) { this.last = startedAt; }

    mark(stage: string): void {
        const t = this.now();
        this.stages.push(`${stage} ${t - this.last} ms`);
        this.last = t;
    }

    toString(): string { return this.stages.join(', '); }
}

/** A sample's file name: letters of any script kept, so that two VOICEVOX voices, whose names are
 *  kana, do not come to one file. */
export function voiceSampleFileName(key: string): string {
    return `${key.replace(/[^\p{L}\p{N}_.-]+/gu, '_')}-${parseVoiceSampleKey(key).language === 'en' ? VOICE_SAMPLE_EN_VERSION : VOICE_SAMPLE_VERSION}.wav`;
}

/** The octave a voice's sample is sung in. */
export type SampleOctave = 'high' | 'low';

/** The octave to hear a voice in (owner's decision 2026-11-10): what the `singer` line says, a
 *  `singer female` line high and a `singer male` line low; on a line that says neither, the voice's
 *  own gender; on a voice that says none either, low. */
export function sampleOctave(voiceGender?: string | null, lineGender?: string | null): SampleOctave {
    return (lineGender ?? voiceGender) === 'female' ? 'high' : 'low';
}

/** What a voice's sample is kept under: the voice, its language after an `@` where that is not
 *  Japanese, and the octave after a `~`, a voice that says no gender being heard in both. */
export function voiceSampleKey(id: string, octave: SampleOctave, language?: string, sing?: Record<string, number | string>): string {
    // …and, after a `!`, how the line says the voice sings (its `with …`), the names in one
    // order: a voice is heard as its line has it (owner's request 2026-11-12: hear what
    // `transpose` does as a note or a voice is heard).
    const settings = sing ? Object.entries(sing).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0).map(([name, value]) => `${name}=${value}`).join(';') : '';
    return `${language && language !== 'ja' ? `${id}@${language}` : id}~${octave}${settings ? '!' + settings : ''}`;
}

/** The parts of a {@link voiceSampleKey}. */
export function parseVoiceSampleKey(key: string): { id: string; language?: string; octave: SampleOctave; sing?: Record<string, number | string> } {
    const bang = key.indexOf('!', key.lastIndexOf('~'));
    const plain = bang < 0 ? key : key.slice(0, bang);
    const mark = plain.lastIndexOf('~');
    const [id, language] = plain.slice(0, mark).split('@');
    const parsed: { id: string; language?: string; octave: SampleOctave; sing?: Record<string, number | string> } =
        { id, language, octave: plain.slice(mark + 1) === 'high' ? 'high' : 'low' };
    if (bang >= 0) {
        parsed.sing = Object.fromEntries(key.slice(bang + 1).split(';').filter(Boolean).map(pair => {
            const at = pair.indexOf('=');
            const value = pair.slice(at + 1);
            return [pair.slice(0, at), value !== '' && !isNaN(Number(value)) ? Number(value) : value];
        }));
    }
    return parsed;
}

/**
 * A singer line as its text reads, for hearing it: the voice as written (a name, else a gender,
 * else '' for the default voice), where on the line to ask what language its words are in, the
 * settings after `with` (a number, or a word or a string unquoted; a name written twice keeps its
 * last), and where the settings begin (-1 with none). Undefined off a singer line and in a
 * comment. The grammar's own reading is the server's; this is the line alone, for the editor.
 */
export function singerLineOf(line: string): { voice: string; askColumn: number; sing: Record<string, number | string>; withAt: number } | undefined {
    const code = line.split('//')[0];
    const head = new RegExp(String.raw`^([ \t]*singer[ \t]+${SINGER_NAME})(?:(male|female)(?![\w-])[ \t]*)?${SINGER_LANGUAGE}(?:"([^"]*)")?`).exec(code);
    // A singer line by its `on PART` or its `sings` (the part comes first since 2026-11-12, and a
    // line that names no words has no `sings`).
    if (!head || !/(?<![\w-])(?:sings|on)(?![\w-])/.test(code.replace(/"[^"]*"/g, '""'))) { return undefined; }
    const quote = code.indexOf('"', head[1].length);
    const askColumn = head[3] !== undefined && quote >= 0 ? quote + 1 : head[1].length + (head[2]?.length ?? 0);
    const sing: Record<string, number | string> = {};
    // `with` and a name after it, not the track or the part of that name (`sings with on lead`).
    const masked = code.replace(/"[^"]*"/g, m => '"' + ' '.repeat(Math.max(0, m.length - 2)) + '"');
    // The line in its order: sings, the words, on and the part, then with.
    const at = new RegExp(String.raw`^[ \t]*(?:on(?![\w-])[ \t]+(?:"[^"]*"|[^\s"]+)(?:[ \t]+sings(?![\w-])[ \t]+(?:"[^"]*"|[^\s"]+))?|sings(?![\w-])(?:[ \t]+on[ \t]+(?:"[^"]*"|[^\s"]+)|[ \t]+(?:"[^"]*"|[^\s"]+)(?:[ \t]+on[ \t]+(?:"[^"]*"|[^\s"]+))?))${SINGER_PLACE}[ \t]+with([ \t]+)(?=[A-Za-z_])`).exec(masked.slice(head[0].length));
    const withAt = at ? head[0].length + at[0].length - at[1].length - 4 : -1;
    if (at) {
        const words = code.slice(head[0].length + at[0].length).match(/"[^"]*"|[^\s"]+/g) ?? [];
        for (let i = 0; i + 1 < words.length; i += 2) {
            const value = words[i + 1];
            sing[words[i]] = value.startsWith('"') ? value.slice(1, -1) : value !== '' && !isNaN(Number(value)) ? Number(value) : value;
        }
    }
    return { voice: head[3] || head[2] || '', askColumn, sing, withAt };
}

/** The short phrase a voice is heard by when it is chosen (owner's requests 2026-11-09, session 1177,
 *  and 2026-11-10); undefined for a language it has no words for. */
export function voiceSampleScore(language: string, sung: SampleOctave = 'low'): CantoriaScore | undefined {
    const octave = sung === 'high' ? 0 : -12;
    if (language === 'en') {
        // Five syllables to the five of こんにちわ, on the same notes (owner's decision 2026-11-09, session 1201).
        return { sampleRate: VOICE_SAMPLE_RATE, language, notes: [
            { start: 0.3, duration: 0.25, pitch: 60 + octave, lyric: 'hel', hyphen: true },
            { start: 0.55, duration: 0.25, pitch: 62 + octave, lyric: 'lo' },
            { start: 0.8, duration: 0.25, pitch: 64 + octave, lyric: 'how' },
            { start: 1.05, duration: 0.25, pitch: 65 + octave, lyric: 'are' },
            { start: 1.3, duration: 0.5, pitch: 67 + octave, lyric: 'you' },
        ] };
    }
    if (language !== 'ja') { return undefined; }
    const notes: CantoriaNote[] = [
        { start: 0.3, duration: 0.25, pitch: 60, lyric: 'こ' },
        { start: 0.55, duration: 0.25, pitch: 62, lyric: 'ん' },
        { start: 0.8, duration: 0.25, pitch: 64, lyric: 'に' },
        { start: 1.05, duration: 0.25, pitch: 65, lyric: 'ち' },
        { start: 1.3, duration: 0.5, pitch: 67, lyric: 'わ' },     // as it is said: a は here would be sung ha
    ];
    return { sampleRate: VOICE_SAMPLE_RATE, language, notes: notes.map(n => ({ ...n, pitch: n.pitch + octave })) };
}

/**
 * The samples still to sing, one at a time so the engine is never held long from a score's own
 * voices: `ask` goes to the front and says when its sample is there, `warm` waits behind. A
 * sample that could not be sung is not warmed again; an `ask` tries it again.
 */
export class VoiceSampleQueue {
    private readonly waiting: string[] = [];
    private readonly callers = new Map<string, ((ok: boolean) => void)[]>();
    private readonly failed = new Set<string>();
    private running = false;
    private current: string | undefined;

    constructor(private readonly has: (id: string) => boolean, private readonly render: (id: string) => Promise<void>,
        private readonly tell?: (id: string, error: unknown) => void) {}

    warm(ids: readonly string[]): void {
        for (const id of ids) {
            if (!this.has(id) && !this.failed.has(id) && id !== this.current && !this.waiting.includes(id)) { this.waiting.push(id); }
        }
        void this.pump();
    }

    /** `supersedes`: the waiting samples this one takes the place of - they are not sung, and
     *  whoever asked for them hears false. */
    ask(id: string, supersedes?: (other: string) => boolean): Promise<boolean> {
        if (supersedes) {
            for (const other of this.waiting.filter(w => w !== id && supersedes(w))) {
                this.waiting.splice(this.waiting.indexOf(other), 1);
                for (const resolve of this.callers.get(other) ?? []) { resolve(false); }
                this.callers.delete(other);
            }
        }
        if (this.has(id)) { return Promise.resolve(true); }
        return new Promise<boolean>(resolve => {
            this.callers.set(id, [...(this.callers.get(id) ?? []), resolve]);
            if (id !== this.current) {
                const at = this.waiting.indexOf(id);
                if (at >= 0) { this.waiting.splice(at, 1); }
                this.waiting.unshift(id);
            }
            void this.pump();
        });
    }

    private async pump(): Promise<void> {
        if (this.running) { return; }
        this.running = true;
        for (let id = this.waiting.shift(); id !== undefined; id = this.waiting.shift()) {
            this.current = id;
            let ok = false;
            try {
                await this.render(id);
                ok = true;
                this.failed.delete(id);
            } catch (e) {
                this.failed.add(id);
                this.tell?.(id, e);
            }
            this.current = undefined;
            for (const resolve of this.callers.get(id) ?? []) { resolve(ok); }
            this.callers.delete(id);
        }
        this.running = false;
    }
}

/** The API from an extension's exports, or undefined when they are not that shape. */
export function cantoriaApiOf(exports: unknown): CantoriaApi | undefined {
    const e = exports as Partial<CantoriaApi> | undefined;
    return e && typeof e.sing === 'function' && typeof e.status === 'function' && e.apiVersion === 1 ? e as CantoriaApi : undefined;
}

const round = (x: number): number => Math.round(x * 1e4) / 1e4;

/**
 * The engine's score of a voice at the practice menu's `speed`: the notes' seconds divided by
 * it (a voice cannot be stretched after the fact without its pitch moving, so a speed change
 * is a render of its own), a melisma with no lyric, a breath where the server put one.
 */
export function cantoriaScore(voice: SungVoiceDto, speed: number): CantoriaScore {
    const k = 1 / (speed > 0 ? speed : 1);
    // How the voice sings (its singer line's `with …`), by the engine's own names; the names in
    // one order, so the same settings are the same key however the line wrote them.
    const sing = voice.Sing && Object.keys(voice.Sing).length > 0
        ? Object.fromEntries(Object.entries(voice.Sing).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)) : undefined;
    return {
        sampleRate: VOICE_SAMPLE_RATE,
        language: voice.Language || 'ja',
        ...(sing ? { sing } : {}),
        notes: voice.Notes.map(n => {
            // Rounded at the note's ends, not its length: a start and a duration rounded apart can
            // put the next note's start a ten-thousandth before this one's end (16ths at 132 are
            // repeating decimals), and the engine refuses overlapping notes (owner's report
            // 2026-11-08, 雪やこんこ: "note 22 at 6.364 s starts before note 21 ends (6.364 s)").
            // Moved later by VOICE_LEAD_S, the room for the first syllable's consonant before the
            // beat (the WAV's time VOICE_LEAD_S is the score's 0; docs/singing-design.md §3).
            const start = round(n.T * k + VOICE_LEAD_S), end = round((n.T + n.D) * k + VOICE_LEAD_S);
            const note: CantoriaNote = { start, duration: round(end - start), pitch: n.P };
            if (n.M) { note.melisma = true; } else if (n.L) { note.lyric = n.L; }
            if (n.B) { note.breath = true; }
            if (n.V != null) { note.level = n.V; }
            if (n.E != null) { note.levelEnd = n.E; }
            if (n.I) { note.inhale = true; }
            if (n.H) { note.hyphen = true; }
            // A vibrato's swings a second are the singer's, not the tempo's: a practice speed leaves them.
            if (n.Vb != null) { note.vibrato = n.Vb; }
            if (n.Vr != null) { note.vibratoRate = n.Vr; }
            // The settings written on the note, the names in one order (the voice's key is this text).
            const ordered = (s: Record<string, number | string>) => Object.fromEntries(Object.entries(s).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0));
            if (n.Sg && Object.keys(n.Sg).length > 0) { note.sing = ordered(n.Sg); }
            if (n.St && Object.keys(n.St).length > 0) { note.singTo = ordered(n.St); }
            return note;
        }),
    };
}

/** Milliseconds the text must rest before an edited voice is sent to the engine (owner's decision
 *  2026-11-08, session 1148): typing never asks the engine; a pause does. */
export const PREPARE_IDLE_MS = 1500;

/** How long the engine may take over one voice before the preview gives it up as `failed`
 *  (the microphone pulsed for ever over an engine that never answered; a long score on the
 *  CPU's DiffSinger takes tens of seconds, so two minutes). */
export const PREPARE_TIMEOUT_MS = 120_000;

/** When an edited voice is prepared: as the text rests (`afterEdit`, the default), or only when
 *  Play is pressed (`onPlay`: for a slow engine — the CPU's DiffSinger — where every pause would
 *  cost seconds of rendering; that Play plays the instruments). The setting `lilysharp.playback.singerPrepare`. */
export type PrepareMode = 'afterEdit' | 'onPlay';
export function prepareModeOf(setting: unknown): PrepareMode {
    return setting === 'onPlay' ? 'onPlay' : 'afterEdit';
}

/** The timers a PrepareQueue runs on — the host's, or a test's. */
export interface PrepareTimers { setTimeout(fn: () => void, ms: number): unknown; clearTimeout(handle: unknown): void }

/**
 * One job at a time per slot, the newest winning (docs/singing-design.md §5 "When the voice is
 * prepared"): a job scheduled with a delay waits that long after the LAST schedule of its slot
 * (typing resets it); one scheduled while another runs waits its turn, and only the newest one
 * waiting runs then — the engine sings each phrase at most twice over an edit, not once per
 * keystroke. `cancel` forgets a slot's waiting job (a preview turned to another document).
 * `letGo` hears of a newer job while one runs: the running one can then end early, and the newer
 * one follows as soon as it has.
 */
export class PrepareQueue<J> {
    private readonly slots = new Map<string, { timer?: unknown; waiting?: J; running: boolean; current?: J }>();

    constructor(private readonly run: (job: J) => Promise<void>,
        private readonly timers: PrepareTimers = { setTimeout: (fn, ms) => setTimeout(fn, ms), clearTimeout: h => clearTimeout(h as ReturnType<typeof setTimeout>) },
        private readonly letGo?: (running: J, newer: J) => void) {}

    /** True while a job of `slot` runs. */
    busy(slot: string): boolean { return this.slots.get(slot)?.running ?? false; }

    schedule(slot: string, job: J, delayMs: number): void {
        let s = this.slots.get(slot);
        if (!s) { this.slots.set(slot, s = { running: false }); }
        if (s.current !== undefined) { this.letGo?.(s.current, job); }
        if (s.timer !== undefined) { this.timers.clearTimeout(s.timer); s.timer = undefined; }
        s.waiting = job;
        if (delayMs > 0) { s.timer = this.timers.setTimeout(() => { s.timer = undefined; this.fire(slot); }, delayMs); }
        else { this.fire(slot); }
    }

    /** True while a newer job of `slot` waits (its timer, or its turn): a result landing now is of an older text. */
    hasWaiting(slot: string): boolean {
        const s = this.slots.get(slot);
        return !!s && s.waiting !== undefined;
    }

    cancel(slotPrefix: string): void {
        for (const [slot, s] of this.slots) {
            if (!slot.startsWith(slotPrefix)) { continue; }
            if (s.timer !== undefined) { this.timers.clearTimeout(s.timer); s.timer = undefined; }
            s.waiting = undefined;
        }
    }

    /** Forgets one slot's waiting job, this slot alone (`p1/melody`, not `p1/melody2`). */
    cancelSlot(slot: string): void {
        const s = this.slots.get(slot);
        if (!s) { return; }
        if (s.timer !== undefined) { this.timers.clearTimeout(s.timer); s.timer = undefined; }
        s.waiting = undefined;
    }

    private fire(slot: string): void {
        const s = this.slots.get(slot);
        if (!s || s.running || s.waiting === undefined) { return; }
        const job = s.waiting;
        s.waiting = undefined;
        s.running = true;
        s.current = job;
        this.run(job).catch(() => undefined).then(() => {
            s.running = false;
            s.current = undefined;
            if (s.waiting !== undefined && s.timer === undefined) { this.fire(slot); }
        });
    }
}

/** What a rendered voice is cached and named by: the voice and the score's bytes, so the same
 *  phrase at the same speed in the same voice is sung once per machine. */
export function voiceKey(score: CantoriaScore, voice: string): string {
    return createHash('sha1').update(voice).update('\n').update(JSON.stringify(score)).digest('hex');
}

/** One line of `cantoria serve`'s stdout. */
export interface ServeAnswer {
    ready?: boolean; voices?: string[];
    id?: string; wav?: string; duration?: number; sampleRate?: number; seconds?: number; error?: string;
    /** How far a request is — not an answer: the request's id is inside it. */
    progress?: { id: string } & SingProgress;
}

/** A setting a voice takes on its singer line's `with …` (Cantoria's `parameters`): its name;
 *  where it can change — `curve` at any note, `phrase` where a phrase ends, `voice` once a score;
 *  what it does; and for a number its range, the value that changes nothing, its unit. */
export interface SingParameter {
    name: string; scope: 'curve' | 'phrase' | 'voice'; what: string; min?: number; max?: number; default?: number; unit?: string;
    /** The value is a NAME, not a number (NEUTRINO's support voice); `values` lists the names that mean something. */
    word?: boolean; values?: string[];
}

/** What of its part a singer line sings, as it stands between the part and `with` (owner's
 *  decisions 2026-11-12): `up 3` / `down 3`, `octave -1`, `in Chorus Bridge` — each optional, in
 *  that order; a section is never called with. */
const SINGER_PLACE = String.raw`(?:[ \t]+(?:(?:up|down)[ \t]+\d+|octave[ \t]+[-+]?\d+|in(?:[ \t]+(?!(?:with|up|down|octave)(?![\w-]))[\w-]+)+))*`;

/**
 * Where the caret stands in a singer line's settings (owner's decisions 2026-11-12:
 * `singer … with breathiness 0.3 gender -0.2`, a name and a value each, no commas), for the
 * editor's list: `with` — the word itself may be written here (a whole line with no settings
 * yet, the caret after a space); `name` — a setting's name is expected (after `with`, or after a
 * value); `value` — the value of `of`. Undefined anywhere else, and in a comment.
 */
export function singerSettingSlot(before: string): { kind: 'with' } | { kind: 'name'; written: string[] } | { kind: 'value'; of: string } | undefined {
    if (before.includes('//')) { return undefined; }
    const line = /^[ \t]*singer(?![\w-])(.*)$/.exec(before);
    if (!line || !/(?<![\w-])(?:sings|on)(?![\w-])/.test(line[1].replace(/"[^"]*"/g, '""'))) { return undefined; }
    // The line up to `with`, quoted strings set aside so that a voice's name is no word of it.
    const rest = line[1].replace(/"[^"]*"/g, '""');
    const at = /(?<![\w.-])with[ \t]+(?!on(?![\w-]))/.exec(rest);
    const ends = /[ \t]$/.test(rest);
    if (!at) {
        // `singer "x" sings words on lead ▮`: after the words or the part, a space, nothing begun.
        // …or after what of the part it sings (`up 3`, `octave -1`, `in Chorus Bridge`), each whole.
        return ends && new RegExp(String.raw`(?<![\w-])(?:on[ \t]+[\w-]+(?:[ \t]+sings[ \t]+[\w-]+)?|sings(?:[ \t]+(?!on(?![\w-]))[\w-]+)?(?:[ \t]+on[ \t]+[\w-]+)?)${SINGER_PLACE}[ \t]+$`).test(rest)
            && !/(?<![\w-])(?:sings|on|up|down|octave|in)[ \t]+$/.test(rest)
            ? { kind: 'with' } : undefined;
    }
    const words = rest.slice(at.index + at[0].length).split(/[ \t]+/).filter(w => w.length > 0);
    const complete = ends ? words : words.slice(0, -1);
    const names = complete.filter((_, i) => i % 2 === 0);
    return complete.length % 2 === 0 ? { kind: 'name', written: names } : { kind: 'value', of: complete[complete.length - 1] };
}

/**
 * Where the caret stands inside a note's `@sing(…)` (owner's decisions 2026-11-12:
 * `c4@sing(breathiness 6 tension to -2)`), for the editor's list: `name` — a setting's name is
 * expected (after the parenthesis, or after a value), with the names already written; `value` —
 * the value of `of`, where `default` and `to` may be written. Undefined outside one, and in a comment.
 */
export function singMarkSlot(before: string): { kind: 'name'; written: string[] } | { kind: 'value'; of: string } | undefined {
    if (before.includes('//')) { return undefined; }
    const open = /@sing\(([^()]*)$/.exec(before);
    if (!open) { return undefined; }
    const inside = open[1].replace(/"[^"]*"/g, '""');
    const words = inside.split(/[ \t]+/).filter(w => w.length > 0);
    const complete = /[ \t]$/.test(inside) || inside.length === 0 ? words : words.slice(0, -1);
    // Names and values alternate; a `to` before a value belongs to the value.
    const names: string[] = [];
    let expectsName = true, last = '';
    for (const word of complete) {
        if (expectsName) { names.push(word); last = word; expectsName = false; }
        else if (word !== 'to') { expectsName = true; }
    }
    return expectsName ? { kind: 'name', written: names } : { kind: 'value', of: last };
}

/** The voices a file's singer lines name, as written: a quoted name, else the gender. */
export function singersWritten(text: string): string[] {
    const found: string[] = [];
    for (const line of text.split('\n')) {
        const m = new RegExp(String.raw`^[ \t]*singer[ \t]+${SINGER_NAME}(?:(male|female)[ \t]+)?${SINGER_LANGUAGE}(?:"([^"]*)")?[ \t]*(?:sings|on)(?![\w-])`).exec(line.split('//')[0]);
        if (m) { const who = m[2] || m[1] || ''; if (!found.includes(who)) { found.push(who); } }
    }
    return found;
}

/** A setting as a document writes it (`lilysharp/singSettings`): where its name and its value
 *  stand, the value, and whose it is — a singer line's (`Singer` as the line names its voice) or a note's. */
export interface SingSettingDto {
    Name: string; Start: number; End: number; ValueStart: number; ValueEnd: number;
    Number?: number | null; Word?: string | null; Arrives?: boolean; OnNote?: boolean; Singer?: string | null;
}

/** What is wrong with a written setting, and where: on its name, or on its value. */
export interface SingProblem { start: number; end: number; message: string }

/**
 * What the voices' engines would not sing of the settings a document writes (owner's decisions
 * 2026-11-12: a setting the voice does not take is the score's mistake, and is shown where it is
 * written — not only when the voice is sung). `takenBy` is what each voice named on a singer line
 * takes, by the name as written ('' for the default voice). A singer line's setting is its own
 * voice's; a note's is any of the file's voices' — the editor does not follow a note to its singer.
 * Checked: a name no voice takes; a number where a name is wanted and the reverse; a name that is
 * none of the setting's; a number out of its range; on a note, a setting that is one value for
 * the voice, and a glide (`to`) of one that is one value a phrase.
 */
export function singProblems(settings: readonly SingSettingDto[], takenBy: ReadonlyMap<string, readonly SingParameter[]>): SingProblem[] {
    const problems: SingProblem[] = [];
    const all = [...takenBy.values()].flat();
    const listed = (taken: readonly SingParameter[]) => taken.length === 0 ? 'takes no setting' : `takes ${[...new Set(taken.map(p => p.name))].join(', ')}`;
    for (const s of settings) {
        const taken = s.OnNote ? all : takenBy.get(s.Singer ?? '') ?? [];
        const who = s.OnNote ? (takenBy.size === 1 ? 'this file\'s voice' : 'no voice of this file') : (s.Singer ? `"${s.Singer}"` : 'the default voice');
        const p = taken.find(x => x.name === s.Name);
        if (!p) {
            problems.push({ start: s.Start, end: s.End, message: s.OnNote && takenBy.size !== 1
                ? `${who} takes the setting "${s.Name}" (they take ${[...new Set(all.map(x => x.name))].join(', ') || 'none'}): it is not sung`
                : `${who} does not take the setting "${s.Name}" (it ${listed(taken)}): it is not sung`
                    + (s.Name === 'transpose' && !s.OnNote ? '. To sing higher or lower than the part, write up 3, down 6 or octave -1 after the part, before with' : '') });
            continue;
        }
        const isDefault = s.Word === 'default';
        if (s.OnNote && p.scope === 'voice') {
            problems.push({ start: s.Start, end: s.End, message: `"${s.Name}" is one value for the voice: write it on the singer line (with ${s.Name} …), it cannot change inside the music` });
            continue;
        }
        if (s.OnNote && s.Arrives && p.scope === 'phrase') {
            problems.push({ start: s.ValueStart, end: s.ValueEnd, message: `"${s.Name}" is one value a phrase and cannot glide: "to" is taken as from this note` });
        }
        if (isDefault) { continue; }
        if (p.word) {
            if (s.Word == null) { problems.push({ start: s.ValueStart, end: s.ValueEnd, message: `"${s.Name}" takes a name, not a number` }); }
            else if (p.values && p.values.length > 0 && !p.values.some(v => v.toLowerCase() === s.Word!.toLowerCase())) {
                problems.push({ start: s.ValueStart, end: s.ValueEnd, message: `"${s.Word}" is none of the names "${s.Name}" takes: ${p.values.join(', ')}` });
            }
        } else if (s.Number == null) {
            problems.push({ start: s.ValueStart, end: s.ValueEnd, message: `"${s.Name}" takes a number${p.min !== undefined && p.max !== undefined ? `, ${p.min} to ${p.max}` : ''}, not "${s.Word}"` });
        } else if ((p.min !== undefined && s.Number < p.min) || (p.max !== undefined && s.Number > p.max)) {
            const sung = Math.min(p.max ?? s.Number, Math.max(p.min ?? s.Number, s.Number));
            problems.push({ start: s.ValueStart, end: s.ValueEnd, message: `${s.Number} is outside what "${s.Name}" takes, ${p.min} to ${p.max}${p.unit ? ' ' + p.unit : ''}: it is sung as ${sung}` });
        }
    }
    return problems;
}

/**
 * The values offered where a setting's number is written (owner's request 2026-11-12, after
 * `with transpose `): from the least to the most the voice takes, in steps — every semitone for a
 * setting counted in them, else a round step that gives about a dozen — always with the value
 * that changes nothing, which says so. A place to begin from: the step keys move it finer. None
 * for a setting that takes a name, or one that says no range.
 */
export function singValueChoices(p: SingParameter): { text: string; detail: string; isDefault: boolean }[] {
    if (p.word || p.min === undefined || p.max === undefined || !(p.max > p.min)) { return []; }
    const min = p.min, max = p.max;
    const span = max - min;
    let step = 1;
    if (p.unit !== 'semitones') {
        const raw = span / 12;
        const magnitude = Math.pow(10, Math.floor(Math.log10(raw)));
        step = [1, 2, 2.5, 5, 10].map(m => m * magnitude).find(s => s >= raw - 1e-12) ?? raw;
    }
    // Counted from the value that changes nothing, so that it is one of them.
    const origin = p.default !== undefined && p.default >= min && p.default <= max ? p.default : min;
    const decimals = (n: number) => (String(n).split('.')[1] ?? '').length;
    const places = Math.min(6, Math.max(decimals(Number(step.toPrecision(6))), decimals(origin)));
    const values = new Set<number>();
    for (let k = Math.ceil((min - origin) / step - 1e-9); origin + k * step <= max + 1e-9; k++) {
        values.add(Number((origin + k * step).toFixed(places)));
    }
    values.add(min);
    values.add(max);
    const unit = p.unit ? ' ' + p.unit : '';
    return [...values].sort((a, b) => a - b).map(v => ({
        text: String(v),
        detail: v === p.default ? `${p.name} as the voice does by itself` : `${min} to ${max}${unit}`,
        isDefault: v === p.default,
    }));
}

/** One line of the list for a setting: its name, and what it does with its range. */
export function singParameterDetail(p: SingParameter): string {
    const range = p.min !== undefined && p.max !== undefined ? `${p.min} to ${p.max}${p.unit ? ' ' + p.unit : ''}`
        : p.word ? 'a name' + (p.values && p.values.length > 0 ? `: ${p.values.slice(0, 6).join(', ')}${p.values.length > 6 ? ', …' : ''}` : '') : '';
    const where = p.scope === 'curve' ? '' : p.scope === 'phrase' ? 'one value a phrase: it changes where a phrase begins' : 'one value for the voice';
    return [p.what, range && `${range}${p.default !== undefined ? `, ${p.default} as it is` : ''}`, where].filter(Boolean).join(' — ');
}

/**
 * How far a voice is while the engine sings it, in seconds of MUSIC: what has to be sung anew —
 * the engine keeps every phrase it sang, so after an edit that is the phrases changed, not the
 * score — and how much of it is done. Told once before the first phrase and after each, so a bar
 * from 0 to `toSing` begins empty and is full when the voice is (owner's request 2026-11-11: the
 * bar was counted on the whole score, and stood a tenth full when the voice landed).
 */
export interface SingProgress { toSing: number; sung: number }

/** The answer a line holds, or null for a line that is not one (the engine's own chatter). */
export function parseServeLine(line: string): ServeAnswer | null {
    const text = line.trim();
    if (!text.startsWith('{')) { return null; }
    try {
        const parsed: unknown = JSON.parse(text);
        return parsed && typeof parsed === 'object' ? parsed as ServeAnswer : null;
    } catch {
        return null;
    }
}

/** One request of `cantoria serve`'s stdin: the score, where the WAV goes, and the engine's
 *  default voice (its `--voice`, else MERROW) — Lily# names none. */
export function serveRequestLine(id: string, score: CantoriaScore, output: string, voice?: string): string {
    return JSON.stringify(voice ? { id, score, voice, output } : { id, score, output }) + '\n';
}

/** Lets go of request `id` at its next phrase. */
export function serveCancelLine(id: string): string {
    return JSON.stringify({ command: 'cancel', id }) + '\n';
}
