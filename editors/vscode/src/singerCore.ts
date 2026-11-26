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
import { TimeWarp, warpTime } from './recordingCore';

/** One note of `lilysharp/sung`, in seconds: its syllable `L` (null on a melisma), `M` a melisma
 *  carrying the previous syllable's vowel on, `B` a breath after it, `S` the source offset. */
export interface SungNoteDto { T: number; D: number; P: number; L?: string | null; M?: boolean; B?: boolean; S?: number; V?: number | null; E?: number | null; I?: boolean; H?: boolean;
    /** A written vibrato (`@vibrato`, a trial sung by DiffSinger voices): its swing each way in cents, its swings a second. */
    Vb?: number | null; Vr?: number | null;
    /** What an `@sing(…)` writes on the note: settings held from it (`Sg`), and arrived at with it (`St`, `NAME to VALUE`). */
    Sg?: Record<string, number | string> | null; St?: Record<string, number | string> | null;
    /** The voice's symbols the syllable is sung by, space-separated (`병[/pp.y.eo.NG/]`; Cantoria's request 1). */
    Ph?: string | null;
    /** What a scoop, a fall or a slide makes of the pitch: [seconds from the note's start, cents] (a trial; Cantoria's request 2). */
    Bd?: number[][] | null }

/** A part's words on its notes, as the server answers `lilysharp/sung`. */
/** `InScore`: the score shown sings this part itself; false for a voice the file's other lyrics could sing (off until the menu turns it on; session 1163). */
export interface SungVoiceDto { Part: string; Key?: string; Language: string; Notes: SungNoteDto[]; Warnings?: string[] | null; InScore?: boolean; Singer?: string | null; Gender?: string | null; Track?: string | null;
    /** The singer's own name (`singer carol female …`), or null for a line that gives none. */
    Name?: string | null;
    /** How the voice sings, as its singer line's `with …` writes it: a name its number or its word, by the names the voice's engine takes. */
    Sing?: Record<string, number | string> | null;
    /** Where the voice's `singer` line is written (source offsets), or null with no line of the file's own. */
    SingerStart?: number | null; SingerEnd?: number | null;
    /** The note under the first syllable `Warnings` speaks of (a source offset), and that place in words. */
    WarningAt?: number | null; WarningPlace?: string | null }

/** The score Cantoria reads (its README): seconds, MIDI pitch, the syllable as pronounced. */
export interface CantoriaNote { start: number; duration: number; pitch: number; lyric?: string; melisma?: boolean; breath?: boolean; level?: number; levelEnd?: number; inhale?: boolean; hyphen?: boolean; vibrato?: number; vibratoRate?: number; phonemes?: string; bend?: number[][];
    sing?: Record<string, number | string>; singTo?: Record<string, number | string> }
export interface CantoriaScore { sampleRate: number; language: string; sing?: Record<string, number | string>; notes: CantoriaNote[] }

/** The rate the voice is asked for — NEUTRINO's own, so nothing is resampled on the way. */
export const VOICE_SAMPLE_RATE = 48000;

/** The Cantoria extension, the engine's own home on the Marketplace (docs/singing-design.md §5). */
export const CANTORIA_EXTENSION_ID = 'yotsuda.cantoria';

/** True while the Cantoria installed here is older than the newest its gallery lists: the
 *  walkthrough's first step is then "Upgrade" where it is "Install" (owner's request 2026-11-18). */
export const CANTORIA_OUTDATED_CONTEXT = 'lilysharp.cantoriaOutdated';

/** What a gallery (the Marketplace, or one that speaks as it does) is asked for an extension's
 *  versions: the extension by its name (filter 7), with its versions (1) and their properties (16),
 *  which say whether a version is a pre-release. */
export function galleryQuery(id: string): { filters: { criteria: { filterType: number; value: string }[] }[]; flags: number } {
    return { filters: [{ criteria: [{ filterType: 7, value: id }] }], flags: 17 };
}

/** The newest released version of `id` in a gallery's answer to {@link galleryQuery}, or undefined
 *  when the answer does not list it (not published there, or not an answer at all). A pre-release
 *  is not counted: VS Code does not update to one unasked. */
export function newestInGallery(answer: unknown, id: string): string | undefined {
    type Listed = { publisher?: { publisherName?: string }; extensionName?: string; versions?: { version?: string; properties?: { key?: string; value?: string }[] }[] };
    const results = (answer as { results?: { extensions?: Listed[] }[] } | null)?.results;
    if (!Array.isArray(results)) { return undefined; }
    let newest: string | undefined;
    for (const e of results.flatMap(r => Array.isArray(r?.extensions) ? r.extensions : [])) {
        if (`${e.publisher?.publisherName}.${e.extensionName}`.toLowerCase() !== id.toLowerCase() || !Array.isArray(e.versions)) { continue; }
        for (const v of e.versions) {
            if (typeof v.version !== 'string' || !/^\d+(\.\d+)*$/.test(v.version)) { continue; }
            if (v.properties?.some(p => p.key === 'Microsoft.VisualStudio.Code.PreRelease' && p.value === 'true')) { continue; }
            if (newest === undefined || olderVersion(newest, v.version)) { newest = v.version; }
        }
    }
    return newest;
}

/** Whether "a.b.c" is a version before "x.y.z", by its numbers. */
function olderVersion(a: string, b: string): boolean {
    const pa = a.split('.').map(Number), pb = b.split('.').map(Number);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
        const d = (pa[i] ?? 0) - (pb[i] ?? 0);
        if (d !== 0) { return d < 0; }
    }
    return false;
}

/** Whether to offer Cantoria's upgrade: it is installed, the gallery's newest is known and is
 *  newer, and that newest was not installed this session already (`upgradedTo`: the extension
 *  running goes on saying its old version until the extensions are restarted). */
export function cantoriaIsOutdated(installed: string | undefined, newest: string | undefined, upgradedTo?: string): boolean {
    if (!installed || !newest || !/^\d+(\.\d+)*$/.test(installed)) { return false; }
    return olderVersion(installed, newest) && upgradedTo !== newest;
}

/** A voice the Cantoria extension lists: `id` is `engine/name` (`neutrino/MERROW`), what its
 *  `sing` takes and its setting holds. */
export interface CantoriaVoice { id: string; engine: string; name: string; label?: string; language?: string; languages?: string[]; gender?: string; licence?: string }

/** The walkthrough's first song in each language Cantoria sings, by the language's tag: the
 *  opening of a song in the public domain (ふるさと, Twinkle Twinkle Little Star, 送别, 아리랑),
 *  files of the extension's `walkthrough` folder. */
export const FIRST_SONGS: Readonly<Record<string, string>> = { ja: 'first-song-ja.lys', en: 'first-song-en.lys', zh: 'first-song-zh.lys', ko: 'first-song-ko.lys' };

/** The first song to open: one the voices installed can sing (owner's request 2026-11-18 — the
 *  song was Japanese whatever the voice, and a voice that sings no Japanese played it on the
 *  instrument). The editor's own language first, where a voice sings it; else the first of
 *  Japanese, English, Chinese and Korean that one does. With no voice that says what it sings
 *  (none installed yet, or an engine not running), the Japanese one: every engine the steps set
 *  up sings Japanese.
 *
 *  `whole`: the song to its end, or — false — its opening alone (`first-song-ja-short.lys`), for
 *  a computer where a voice sings slowly (the owner, the same day): half a minute of song is
 *  minutes of waiting with no GPU, and a first song should be heard soon. */
export function firstSongFor(voices: readonly Pick<CantoriaVoice, 'language' | 'languages'>[], uiLanguage: string, whole = true): string {
    const ui = uiLanguage.toLowerCase().split('-')[0];
    const order = [...new Set([ui, 'ja', 'en', 'zh', 'ko'])].filter(l => l in FIRST_SONGS);
    const song = FIRST_SONGS[order.find(l => voices.some(v => singsLanguage(v, l))) ?? 'ja'];
    return whole ? song : song.replace(/\.lys$/, '-short.lys');
}

/** Whether this computer has an NVIDIA GPU with its driver, by the program the driver installs
 *  (`nvidia-smi`): the GPU NEUTRINO sings on, and a fast one for a DiffSinger voice. Looked for
 *  where it is put, not guessed from a name; no Mac has one. Another make's GPU sings a DiffSinger
 *  voice fast too and is not seen here: such a computer is offered the short song first, and the
 *  whole one is a click away. */
export function hasNvidiaGpu(platform: string, env: Readonly<Record<string, string | undefined>>, exists: (p: string) => boolean): boolean {
    if (platform === 'win32') {
        const root = env.SystemRoot ?? env.SYSTEMROOT ?? env.windir ?? 'C:\\Windows';
        return exists(`${root}\\System32\\nvidia-smi.exe`);
    }
    return platform === 'linux' && ['/usr/bin/nvidia-smi', '/usr/local/bin/nvidia-smi'].some(exists);
}

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
    /** Who would sing what a score asks for, without singing (Cantoria's design §4.3, from
     *  2026-11-13; an older extension has none): for each voice asked, the installed voice, or
     *  `voice: null` and the `reason` in words for whoever wrote the score. */
    plan?(input: { asked: PlanAsk[] }): Promise<SingerPlanAnswer>;
}

/** One voice asked of `plan`: by `voice` (an `engine/name`), or by `gender` alone, in a `language`;
 *  `part` and `key` are the caller's own and come back. */
export interface PlanAsk { part?: string; key?: string; voice?: string; gender?: string; language?: string }

/** What `plan` answers: `ready` when each voice asked has a singer. */
export interface SingerPlanAnswer {
    ready: boolean;
    singers: { part?: string; key?: string; voice: string | null; note?: string; reason?: string | null }[];
}

/** What a score's voices ask Cantoria's `plan` for, one entry a voice (Core's
 *  Audio.SingerPlan.AskedOf is the same list for `lysc check --singer`): the voice its singer line
 *  names, else the gender's default, in the language of its words. */
export function planAsked(voices: readonly Pick<SungVoiceDto, 'Part' | 'Key' | 'Singer' | 'Gender' | 'Language'>[]): PlanAsk[] {
    return voices.map(v => {
        const ask: PlanAsk = { part: v.Part, key: sungKey(v) };
        if (v.Singer && v.Singer !== 'male' && v.Singer !== 'female') { ask.voice = v.Singer; }
        if (v.Gender) { ask.gender = v.Gender; }
        ask.language = v.Language;
        return ask;
    });
}

/** The voices of `voices` that `plan` says nobody installed can sing, by key, each with the reason
 *  (owner's decision 2026-11-13: said to the writer, and the part's instrument plays). The answer
 *  is read in the order asked; one that is not for these voices says nothing. */
export function unsingableIn(answer: SingerPlanAnswer | undefined, voices: readonly Pick<SungVoiceDto, 'Part' | 'Key'>[]): Map<string, string> {
    const out = new Map<string, string>();
    const singers = answer?.singers;
    if (!Array.isArray(singers) || singers.length !== voices.length) { return out; }
    singers.forEach((s, i) => {
        if (s && s.voice === null) { out.set(sungKey(voices[i]), (s.reason ?? '').trim() || 'no installed voice sings it'); }
    });
    return out;
}

/** With a Cantoria that has no `plan`: why the voice a singer came to (`resolveSinger`) cannot sing
 *  words in `language` — it says what it sings, and this is not among it — or undefined. In the
 *  words Cantoria's own `sing` refuses with. (An older Cantoria sang such words as silence, and the
 *  silent voice took the instrument's place: owner's report 2026-11-13, a Latin alto.) */
export function languageRefusal(voiceId: string, language: string, voices: readonly CantoriaVoice[]): string | undefined {
    const voice = voices.find(v => v.id === voiceId);
    if (!voice) { return undefined; }
    const sings = voice.languages ?? (voice.language ? [voice.language] : []);
    return sings.length > 0 && !sings.includes(language) ? `${voiceId} sings ${sings.join(', ')}, and these words are ${language}` : undefined;
}

/** Whether what an engine threw says the WORDS cannot be sung by what is installed — a language no
 *  voice sings — and not that a render went wrong: its sentence then, without the error's wrapping. */
export function unsingableReason(error: unknown): string | undefined {
    const text = (error instanceof Error ? error.message : String(error)).replace(/^(Error: )+/, '').trim();
    return /\bsings\b.*\band these words are\b/i.test(text) || /^no installed voice\b/i.test(text) || vocoderMissing(text) ? text.replace(/\.$/, '') : undefined;
}

/** Whether what Cantoria said of a voice is that a DiffSinger voice has no vocoder to sing with
 *  (owner's request 2026-11-18): the voice bundles none and no vocoder folder is chosen, or the
 *  folder chosen holds none. Cantoria's two sentences (its DiffSingerOptions.Validate), from its
 *  plan or from what its sing refused with. A setting gives the voice, not another voice. */
export function vocoderMissing(reason: unknown): boolean {
    return /\bbundles no vocoder\b|\bno vocoder\.yaml under\b/i.test(reason instanceof Error ? reason.message : String(reason));
}

/** What the editor says of a DiffSinger voice that has no vocoder, with the way to one: the steps
 *  (the page's DiffSinger part) and Cantoria's command that chooses the folder. `voice` where it is
 *  known (a name taken from the `singer` list), so that each such voice is said once a session. */
export function noVocoderGuide(reason: string, voice?: string): Pick<VoiceGuide, 'message' | 'actions'> {
    const who = voice ? `the voice ${voice}` : 'a DiffSinger voice';
    const why = /\bno vocoder\.yaml under\b/i.test(reason)
        ? 'the vocoder folder chosen holds no vocoder.yaml'
        : 'it has no vocoder of its own (a dsvocoder folder) and no vocoder folder is chosen';
    return {
        message: `Lily#: ${who} cannot sing yet: ${why}. Download the vocoder the voice's readme names, then choose its folder.`,
        actions: [HOW_TO_INSTALL, CHOOSE_VOCODER],
    };
}

/** What the editor says, once a session for each reason, of a singer line nobody installed can
 *  sing, with the way to a voice that could. */
export function unsingableGuide(reason: string): Pick<VoiceGuide, 'message' | 'actions'> {
    if (vocoderMissing(reason)) { return noVocoderGuide(reason); }
    return { message: `Lily#: a singer line is not sung: ${reason.trim().replace(/\.$/, '')}. The part's instrument plays instead.`, actions: [HOW_TO_INSTALL] };
}

/**
 * The engine messages Lily# can put a plain cause to (owner, 2026-11-13, kaze.lys: the alto's
 * "Failed to load phrase data" was shown as it came, for eight seconds, in the status bar).
 * ⚠️ THE SAME TABLE AS Core's Audio.SingerFailure, which says these sentences for `lysc wav
 * --singer`: a cause added here is added there. Kept small - a message that is not here is shown
 * as the engine said it.
 */
const SINGER_FAILURE_CAUSES: readonly (readonly [RegExp, string])[] = [
    [/Failed to load phrase data/i,
        'NEUTRINO could not make a phrase out of the notes it was given (seen when a phrase begins on a held note with no syllable, or a syllable is not one it has a sound for)'],
    [/did not start|did not become ready|before it was ready/i, "the engine's program did not start"],
    [/did not finish within|gave no answer in/i, 'the engine took too long and was given up'],
    [/did not answer at/i, 'the engine is not running'],
    // Cantoria's DiffSingerSinger.NoSoundFor (owner's report 2026-11-19, Moon-on-the-River.lys:
    // TIGER's dictionary spells 思 "s iz" and its pitch model has no "iz" - the warning named a
    // phoneme and a file of the voice, and no word of the song). $1 is the syllable.
    // FIRST the syllable whose phonemes the file wrote (`병[/pp.yeo/]`, session 1375): the voice
    // lacks a symbol the writer chose, and other words are not the remedy. $2 is the symbol.
    [/has no sound for "([^"]+)" \(note \d+[^)]*\): its phonemes are written "[^"]*", and .*?has no phoneme "([^"]+)"/,
        'the phonemes written on the syllable "$1" are not all the voice\'s own: it has no "$2" - write its own symbols in the [/…/] there, or give the singer line another voice'],
    [/has no sound for "([^"]+)" \(note (\d+)/,
        'the voice has no sound for the syllable "$1" (its models were not taught a phoneme the syllable needs): write other words there, or give the singer line another voice'],
];

/** What Cantoria says of a syllable a voice has no sound for: the syllable, and its note among
 *  the voice's notes, from 0. Core's SingerFailure.UnsungPattern. */
const UNSUNG_SYLLABLE = /has no sound for "([^"]+)" \(note (\d+)/;

/**
 * The note under the syllable the engine says the voice has no sound for: the syllable and the
 * note's source offset. Undefined for another message, for a note the voice does not have, and
 * where that note's syllable is not the one named (the engine was sent other notes than these).
 * Core's SingerFailure.UnsungAt.
 */
export function unsungSyllable(voice: Pick<SungVoiceDto, 'Notes'>, engineMessage: string): { syllable: string; at: number } | undefined {
    const m = UNSUNG_SYLLABLE.exec(engineMessage);
    if (!m) { return undefined; }
    const note = voice.Notes[Number(m[2])];
    return note && note.L === m[1] && typeof note.S === 'number' && note.S >= 0 ? { syllable: m[1], at: note.S } : undefined;
}

/** Where to look first when the engine failed on a voice: the line of the note under a syllable
 *  the voice has no sound for, else where the server found the first syllable that is not kana. */
export function failurePlace(voice: Pick<SungVoiceDto, 'Notes' | 'WarningPlace'>, engineMessage: string, text: string): string | null | undefined {
    const unsung = unsungSyllable(voice, engineMessage);
    return unsung && unsung.at < text.length ? `line ${text.slice(0, unsung.at).split('\n').length}` : voice.WarningPlace;
}

/** What an engine threw, as it said it: without the error's wrapping. */
export function engineSaid(error: unknown): string {
    return (error instanceof Error ? error.message : String(error)).replace(/^(Error: )+/, '').trim();
}

/** The plain cause of an engine's message, or undefined for one Lily# does not know. A `$1` in a
 *  cause is what its pattern caught (the syllable). */
export function singerFailureCause(engineMessage: string): string | undefined {
    for (const [pattern, cause] of SINGER_FAILURE_CAUSES) {
        const m = pattern.exec(engineMessage);
        if (m) { return cause.split('$1').join(m[1] ?? '').split('$2').join(m[2] ?? ''); }
    }
    return undefined;
}

/** The sentence said when the engine FAILS on a voice it agreed to sing: who (the voice, with its
 *  singer line where known), why in plain words when the message is known, where to look first,
 *  and the engine's own message verbatim at the end, for searching. Core's SingerFailure.Sentence. */
export function singerFailureText(who: string, engineMessage: string, place?: string | null): string {
    const said = engineSaid(engineMessage) || 'nothing';
    const look = place ? ` Look first at ${place}.` : '';
    const cause = singerFailureCause(said);
    return cause
        ? `${who} is not sung: ${cause}. Its instrument plays instead.${look} The engine said: ${said}`
        : `${who} is not sung; its instrument plays instead.${look} The engine said: ${said}`;
}

/** Where a voice's `singer` line is and what it says, for a message about it: `alt (singer female
 *  "neutrino/REINA" on alt, line 54)`; the voice's key alone where no line of the file asks for it. */
export function voiceWho(voice: Pick<SungVoiceDto, 'Part' | 'Key' | 'SingerStart' | 'SingerEnd'>, text: string): string {
    const key = sungKey(voice);
    const start = voice.SingerStart, end = voice.SingerEnd;
    if (typeof start !== 'number' || typeof end !== 'number' || start < 0 || end <= start || end > text.length) { return key; }
    const line = text.slice(0, start).split('\n').length;
    return `${key} (${text.slice(start, end).trim().replace(/\s+/g, ' ')}, line ${line})`;
}

/** Something wrong with a voice, at a place of the file: source offsets, and where else to look. */
export interface VoiceProblem { start: number; end: number; message: string; related?: { start: number; end: number; message: string } }

/** The end of the word that begins at `at` (a note, a chord): where the next blank is. */
function wordEnd(text: string, at: number): number {
    const rest = text.slice(at).search(/\s/);
    return rest < 0 ? text.length : Math.max(at + 1, at + rest);
}

/**
 * The warning for a voice the engine failed on, on its `singer` line - the natural home in the
 * Problems panel, and a click from the place that asks for the voice (owner, 2026-11-13). With no
 * line of the file's own (a voice asked for by the part's instrument alone) it stands on the
 * voice's first note. The first syllable that is not kana, where the server found one, is the
 * place to look first, and a related place of the warning.
 */
export function engineFailureProblem(voice: Pick<SungVoiceDto, 'Part' | 'Key' | 'SingerStart' | 'SingerEnd' | 'WarningAt' | 'WarningPlace' | 'Notes'>,
    engineMessage: string, text: string): VoiceProblem {
    const onLine = typeof voice.SingerStart === 'number' && typeof voice.SingerEnd === 'number' && voice.SingerEnd > voice.SingerStart;
    const first = voice.Notes.find(n => typeof n.S === 'number' && n.S >= 0)?.S ?? 0;
    const start = onLine ? voice.SingerStart! : first;
    const end = onLine ? voice.SingerEnd! : wordEnd(text, first);
    const problem: VoiceProblem = { start, end, message: singerFailureText(voiceWho(voice, text), engineMessage, failurePlace(voice, engineMessage, text)) };
    const unsung = unsungSyllable(voice, engineMessage);
    if (unsung) {
        problem.related = { start: unsung.at, end: wordEnd(text, unsung.at), message: `the syllable "${unsung.syllable}" stands under this note` };
    } else if (typeof voice.WarningAt === 'number' && voice.WarningAt >= 0) {
        problem.related = { start: voice.WarningAt, end: wordEnd(text, voice.WarningAt), message: 'the first syllable that is not kana stands under this note' };
    }
    return problem;
}

/**
 * A second warning for a syllable the voice has no sound for, ON THE NOTE it stands under (owner,
 * 2026-11-19: "a clearer error in VS Code" - the warning on the `singer` line is sixty lines from
 * the word that has to change): what cannot be sung, by whom, and the two ways out. None for any
 * other failure of the engine.
 */
/** The symbol the voice lacks among the phonemes the file wrote on the syllable (`병[/pp.yeo/]`,
 *  session 1375), or undefined when the engine spoke of the syllable's own reading. */
export function missingWrittenPhoneme(engineMessage: string): string | undefined {
    return /: its phonemes are written "[^"]*", and .*?has no phoneme "([^"]+)"/.exec(engineMessage)?.[1];
}

export function unsungSyllableProblem(voice: Pick<SungVoiceDto, 'Part' | 'Key' | 'Singer' | 'Notes'>, engineMessage: string, text: string): VoiceProblem | undefined {
    const unsung = unsungSyllable(voice, engineMessage);
    if (!unsung || unsung.at >= text.length) { return undefined; }
    const who = voice.Singer && voice.Singer.includes('/') ? voice.Singer : 'the voice';
    const symbol = missingWrittenPhoneme(engineMessage);
    return {
        start: unsung.at, end: wordEnd(text, unsung.at),
        message: symbol !== undefined
            ? `${who} has no "${symbol}" among the phonemes written on the syllable "${unsung.syllable}" under this note, so ${sungKey(voice)} is not sung at all: write its own symbols here, or give its singer line another voice.`
            : `${who} has no sound for the syllable "${unsung.syllable}" under this note, so ${sungKey(voice)} is not sung at all: write other words here, or give its singer line another voice.`,
    };
}

/**
 * The notes a voice SANG AROUND: the syllables its dictionary has no entry for, which the engine
 * leaves silent and says so with the sung voice (Cantoria's DiffSingerSinger.NotInDictionary,
 * `… does not sing "岸" (note 46 at 57.25 s), …`; owner's report 2026-11-19, Moon-on-the-River.lys:
 * TIGER's Chinese dictionary has no "an", 岸 was silent, and nothing said so). Each with its
 * syllable and its note's source offset; a note the voice does not have, or whose syllable is
 * another, is left out. Empty for any other sentence of the engine's. Core's SingerFailure.SilentAt.
 */
export function silentSyllables(voice: Pick<SungVoiceDto, 'Notes'>, engineNote: string): { syllable: string; at: number }[] {
    if (!/ does not sing "/.test(engineNote)) { return []; }
    const found: { syllable: string; at: number }[] = [];
    for (const m of engineNote.matchAll(/"([^"]+)" \(note (\d+) at/g)) {
        const note = voice.Notes[Number(m[2])];
        if (note && note.L === m[1] && typeof note.S === 'number' && note.S >= 0) { found.push({ syllable: m[1], at: note.S }); }
    }
    return found;
}

/** A warning on each note a voice leaves silent (silentSyllables): what, why, and the two ways out. */
export function silentSyllableProblems(voice: Pick<SungVoiceDto, 'Part' | 'Key' | 'Singer' | 'Notes'>, engineNote: string, text: string): VoiceProblem[] {
    const who = voice.Singer && voice.Singer.includes('/') ? voice.Singer : `the voice of ${sungKey(voice)}`;
    return silentSyllables(voice, engineNote).filter(s => s.at < text.length).map(s => ({
        start: s.at, end: wordEnd(text, s.at),
        message: `${who} is silent on this note: the syllable "${s.syllable}" under it is not in the voice's dictionary. Write other words here, or give the singer line another voice.`,
    }));
}

/** The same, as a notification says it: every such syllable with its line, the first five. */
export function silentSyllablesNotice(voice: Pick<SungVoiceDto, 'Part' | 'Key' | 'Singer' | 'Notes'>, engineNote: string, text: string): string | undefined {
    const silent = silentSyllables(voice, engineNote).filter(s => s.at < text.length);
    if (silent.length === 0) { return undefined; }
    const who = voice.Singer && voice.Singer.includes('/') ? voice.Singer : 'its voice';
    const listed = silent.slice(0, 5).map(s => `"${s.syllable}" (line ${text.slice(0, s.at).split('\n').length})`).join(', ') + (silent.length > 5 ? ', …' : '');
    return `${sungKey(voice)}: ${who} is silent on ${listed}: ${silent.length === 1 ? 'the syllable is' : 'these syllables are'} not in the voice's dictionary. `
        + 'Write other words there, or give the singer line another voice.';
}

/** The same, as a notification says it: away from the note, so with its line. */
export function unsungSyllableNotice(voice: Pick<SungVoiceDto, 'Part' | 'Key' | 'Singer' | 'Notes'>, engineMessage: string, text: string): string | undefined {
    const unsung = unsungSyllable(voice, engineMessage);
    if (!unsung || unsung.at >= text.length) { return undefined; }
    const who = voice.Singer && voice.Singer.includes('/') ? voice.Singer : 'its voice';
    const line = text.slice(0, unsung.at).split('\n').length;
    const symbol = missingWrittenPhoneme(engineMessage);
    return symbol !== undefined
        ? `${sungKey(voice)} is not sung: ${who} has no "${symbol}" among the phonemes written on the syllable "${unsung.syllable}" (line ${line}). Write its own symbols there, or give the singer line another voice.`
        : `${sungKey(voice)} is not sung: ${who} has no sound for the syllable "${unsung.syllable}" (line ${line}). Write other words there, or give the singer line another voice.`;
}

/** The warnings the server gives of a voice's words (syllables that are not kana), each on the
 *  note its first such syllable stands under; none where the server names no place. */
export function wordsProblems(voice: Pick<SungVoiceDto, 'Part' | 'Key' | 'Warnings' | 'WarningAt'>, text: string): VoiceProblem[] {
    const at = voice.WarningAt;
    if (typeof at !== 'number' || at < 0 || at >= text.length) { return []; }
    return (voice.Warnings ?? []).map(w => ({ start: at, end: wordEnd(text, at), message: `${sungKey(voice)}: ${w}` }));
}

/**
 * What is wrong with each document's voices NOW, for the Problems panel: a voice's problems of a
 * kind (`engine` - the engine failed on it; `words` - syllables it will not sing) replace the ones
 * said before, a voice sung clears its `engine` ones, and a voice the score sings no more takes
 * all of its own away. No editor in it: the host turns `of` into diagnostics.
 */
export class VoiceProblems {
    private readonly docs = new Map<string, Map<string, VoiceProblem[]>>();
    /** The problems of `kind` of the voice `key`; an empty list clears them. True when anything changed. */
    set(uri: string, kind: 'engine' | 'words' | 'silent', key: string, problems: readonly VoiceProblem[]): boolean {
        const id = `${kind}\n${key}`;
        let doc = this.docs.get(uri);
        if (problems.length === 0) {
            const had = doc?.delete(id) ?? false;
            if (doc && doc.size === 0) { this.docs.delete(uri); }
            return had;
        }
        if (!doc) { this.docs.set(uri, doc = new Map()); }
        doc.set(id, [...problems]);
        return true;
    }
    /** Only the voices of `keys` are the score's now: the others' problems go. True when any went. */
    keep(uri: string, keys: Iterable<string>): boolean {
        const doc = this.docs.get(uri);
        if (!doc) { return false; }
        const kept = new Set(keys);
        let changed = false;
        for (const id of [...doc.keys()]) {
            if (!kept.has(id.slice(id.indexOf('\n') + 1))) { doc.delete(id); changed = true; }
        }
        if (doc.size === 0) { this.docs.delete(uri); }
        return changed;
    }
    forget(uri: string): void { this.docs.delete(uri); }
    of(uri: string): VoiceProblem[] { return [...(this.docs.get(uri)?.values() ?? [])].flat(); }
}

/**
 * The notes with the sections NOBODY SINGS HERE left to the instrument (found 2026-11-13: in a
 * part with several singer lines where one sings - a Japanese verse - the chorus of a line nobody
 * installed can sing stayed silent, because the server marks the unsung sections (`U`) without
 * knowing what this machine can sing). The server says whose each section is (`W`, the keys of the
 * voices that sing there, in a part with several); a note all of whose singers are in `notSung` is
 * marked `U`, as one in a section no line sings. A part ALL of whose voices are missing is left as
 * it is: it has no voice at all, and its instrument stands in whole (the rule that was; Core's
 * SungVoices.Unsung of the voices that did sing answers the same for `lysc wav --singer`).
 * The same list when nothing changes.
 */
export function leftToTheInstrument<T extends { N?: number; U?: boolean; W?: readonly string[] | null }>(
    notes: readonly T[], notSung: Iterable<string>): T[] {
    const missing = new Set(notSung);
    const list = notes as T[];
    if (missing.size === 0) { return list; }
    // The tracks with a singer left: only there does a voice take the instrument's place.
    const sings = new Set<number | undefined>();
    for (const n of notes) {
        if (n.W && n.W.some(k => !missing.has(k))) { sings.add(n.N); }
    }
    let changed = false;
    const out = notes.map(n => {
        if (n.U || !n.W || n.W.length === 0 || !sings.has(n.N) || n.W.some(k => !missing.has(k))) { return n; }
        changed = true;
        return { ...n, U: true };
    });
    return changed ? out : list;
}

/** Whether a `singer`'s written gender lets this voice be offered: no gender written, a voice that names none, or the same one. */
export function fitsGender(voice: Pick<CantoriaVoice, 'gender'>, gender?: string | null): boolean {
    return !gender || !voice.gender || voice.gender === gender;
}

const LANGUAGE_CODES: Record<string, string> = { japanese: 'ja', english: 'en', chinese: 'zh', korean: 'ko', latin: 'la' };

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

/** The steps to install each engine: this extension's own walkthrough (package.json "singing"),
 *  opened by its command, and until 2026-11-18 a page of the site. What is linked is only an
 *  address that does not move: an engine's own site by its domain, a project by its repository
 *  (the owner, 2026-11-18: those are kinder to the reader than a name to search for; a page
 *  deep in a site is not linked). singingWalkthrough.test.ts holds the list. */
const HOW_TO_INSTALL = { label: 'How to Install a Voice', command: 'lilysharp.setUpSingingVoice' };
const CHOOSE_DIFFSINGER = { label: 'Choose DiffSinger Folder…', command: 'cantoria.chooseVoicesFolder' };
const CHOOSE_VOCODER = { label: 'Choose Vocoder Folder…', command: 'cantoria.chooseVocoderFolder' };
// The guide to installing leads (the owner asked, 2026-11-12), and the engines' folders after it
// stand in the alphabet's order (2026-11-18).
const VOICE_SETUP_ACTIONS: VoiceGuide['actions'] = [
    HOW_TO_INSTALL,
    CHOOSE_DIFFSINGER,
    { label: 'Choose NEUTRINO Folder…', command: 'cantoria.chooseNeutrinoFolder' },
    { label: 'Choose VOICEVOX Folder…', command: 'cantoria.chooseVoicevoxFolder' },
];
const GUIDE_LINK = 'The steps for each engine: **Lily#: Set Up a Singing Voice**, in the command palette';
const ENGINE_LINKS = '[DiffSinger](https://github.com/openvpi/DiffSinger), [NEUTRINO](https://studio-neutrino.com/) or [VOICEVOX](https://voicevox.hiroshiba.jp/)';
const STILL_WRITTEN = 'The score can still be written: a name here is sung once that voice is installed.';

/** What the editor says when no singing voice is installed: with the Cantoria extension, that an engine
 *  is missing; without it, that the extension is the first step. */
export function noVoiceGuide(cantoria: boolean): VoiceGuide {
    return cantoria
        ? {
            label: 'No singing voice is installed',
            detail: 'Set up a voice…',
            documentation: `Cantoria has no voice to sing with. Install ${ENGINE_LINKS}, then choose its folder: take this line for the buttons.\n\n${GUIDE_LINK}.\n\n${STILL_WRITTEN}`,
            message: 'Lily#: no singing voice is installed. Cantoria sings with DiffSinger, NEUTRINO or VOICEVOX: install one, then choose its folder.',
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

const LANGUAGE_NAMES: Record<string, string> = { ja: 'Japanese', en: 'English', zh: 'Chinese', ko: 'Korean', la: 'Latin' };

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
            message: `Lily#: no installed voice sings ${wanted}. DiffSinger, NEUTRINO and VOICEVOX voices do: install one, then choose its folder.`,
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
export const SINGER_LANGUAGES = 'japanese|english|chinese|korean|latin';

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

/** The engine's speed after a render of `seconds` took `elapsedMs`, the earlier speed counting half
 *  — but not the first guess (`timed` false), a GPU's: half of it made a CPU's NEUTRINO half as slow
 *  as it is, and the patience with it (prepareAllowanceMs) too short (2026-11-18: 412 s sung of
 *  501 allowed, on a machine also singing something else). */
export function learnRenderRate(rate: number, elapsedMs: number, seconds: number, timed = true): number {
    if (seconds <= 0) { return rate; }
    const measured = Math.max(0, elapsedMs - VOICE_RENDER_BASE_MS) / seconds;
    if (!timed) { return Math.min(30_000, Math.max(10, measured)); }
    // The ceiling well above a CPU's NEUTRINO (about 9 s a second sung, 2026-11-18): at 3 s it
    // was, the bar and the patience (prepareAllowanceMs) were a third of what such a machine takes.
    return Math.min(30_000, Math.max(10, (rate + measured) / 2));
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
 * The voice on the recording's seconds (Shosa's request 4, docs/recording-design.md §7): every
 * note's start and end moved bar by bar as the score's notes are (warpTime), so a voice sung over a
 * recording follows its tempo as it sways — at the score's 61.5 a song's voice was 1.49 s off the
 * recording by bar 39. Each end is moved on its own, so two notes that meet still meet. No warp:
 * the voice as it is.
 */
export function voiceOnWarp<V extends Pick<SungVoiceDto, 'Notes'>>(voice: V, w: TimeWarp | null | undefined): V {
    if (!w) {
        return voice;
    }
    return { ...voice, Notes: voice.Notes.map(n => {
        const T = warpTime(w, n.T);
        return { ...n, T, D: Math.max(0, warpTime(w, n.T + n.D) - T) };
    }) };
}

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
            // The voice's own symbols for the syllable (a DiffSinger voice sings them; the server puts none on a melisma).
            if (n.Ph) { note.phonemes = n.Ph; }
            // A bend's seconds are the note's: at a practice speed they stretch with it, none past its end.
            if (n.Bd && n.Bd.length > 0) { note.bend = n.Bd.map(([at, cents]) => [Math.min(round(at * k), note.duration), cents]); }
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

/** How long the engine may say nothing — no phrase sung, of this voice or another — before the
 *  preview gives the voice up as `failed` (the microphone pulsed for ever over an engine that
 *  never answered): two minutes, and what is left to sing at the engine's pace three times over;
 *  an engine not yet timed is given a CPU's pace. Two fixed minutes failed every voice on a
 *  machine with no GPU (owner's report 2026-11-18: NEUTRINO on an Intel Iris Xe sang 37 s of song
 *  in 5 min 40 s). `rate` is in milliseconds a second sung (learnRenderRate). */
export function prepareAllowanceMs(secondsLeft: number, rate: number, timed: boolean): number {
    return PREPARE_FLOOR_MS + Math.max(0, secondsLeft) * (timed ? rate : Math.max(rate, PREPARE_UNTIMED_RATE)) * 3;
}
const PREPARE_FLOOR_MS = 120_000;
/** The CPU's pace measured above (341 s for 37 s), rounded up: milliseconds a second sung. */
const PREPARE_UNTIMED_RATE = 12_000;

/**
 * The patience with the engine for the voices waiting on it (prepareAllowanceMs): each is given
 * up when the engine has said nothing for its allowance, and every clock starts again whenever the
 * engine is heard of — a phrase of any voice sung, one landing — so that a voice waiting its turn
 * behind another is not given up on while the engine works.
 */
export class EngineWatch {
    private readonly watched = new Set<() => void>();

    constructor(private readonly timers: PrepareTimers = { setTimeout: (fn, ms) => setTimeout(fn, ms), clearTimeout: h => clearTimeout(h as ReturnType<typeof setTimeout>) }) {}

    /** Watches one voice: `giveUp(ms)` once the engine has said nothing for `allowance()` ms
     *  (asked again at each start of the clock). Returns what ends the watch. */
    watch(allowance: () => number, giveUp: (ms: number) => void): () => void {
        let timer: unknown;
        const stop = () => {
            this.watched.delete(wind);
            if (timer !== undefined) { this.timers.clearTimeout(timer); timer = undefined; }
        };
        const wind = () => {
            if (timer !== undefined) { this.timers.clearTimeout(timer); }
            const ms = allowance();
            timer = this.timers.setTimeout(() => { timer = undefined; stop(); giveUp(ms); }, ms);
        };
        wind();
        this.watched.add(wind);
        return stop;
    }

    /** The engine was heard of: every voice's clock starts again. */
    heard(): void { for (const wind of [...this.watched]) { wind(); } }
}

/** When an edited voice is prepared: as the text rests (`afterEdit`, the default), or only when
 *  Play is pressed (`onPlay`: for a slow engine — the CPU's DiffSinger — where every pause would
 *  cost seconds of rendering; that Play plays the instruments). The setting `lilysharp.playback.prepareSinging`. */
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
