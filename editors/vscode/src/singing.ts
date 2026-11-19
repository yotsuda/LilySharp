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

// The singing voice — the half that talks to VS Code (docs/singing-design.md §5): the `singer`
// lists and what they write, the settings a voice takes and their warnings, the voice samples,
// the one engine of this host, and the voices each preview holds up to its text
// (prepareVoices). The engine and its cache are singer.ts; what is decided without an editor
// is singerCore.ts. It stood in extension.ts until session 1296; what it reads of the
// extension's own state comes through SingingDeps.

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import type { LanguageClient } from 'vscode-languageclient/node';
import { CandidateSounds, CandidateVoice, CandidateVoices, candidateSilencedTrack, candidateSings } from './aiTransformCore';
import { PracticeState, VOICE_LEAD_S, voiceEntries } from './webview/playbackCore';
import { CantoriaExtensionBackend, SingerProcess, VoiceRenderer } from './singer';
import { SINGER_NAME, SINGER_LANGUAGE, onSingerGender, singValueChoices, singerGoesOnToSings, singerLineOf, singerSettingSlot, singMarkSlot, singersWritten, singParameterDetail, singProblems, SingSettingDto, SingParameter, SingProgress, CANTORIA_EXTENSION_ID, PREPARE_IDLE_MS, EngineWatch, prepareAllowanceMs, PrepareMode, PrepareQueue, StageClock, SungVoiceDto, sungKey, VOICE_RENDER_FIRST_RATE, VoiceSampleQueue, byVoiceId, cantoriaApiOf, fitsGender, learnRenderRate, prepareModeOf, afterSingerWord, voiceMenuKey, noLanguageGuide, noVoiceGuide, printedWordsNote, singerChoices, VoiceGuide, singerLanguageColumn, singerValueAt, singsLanguage, stepVoice, sungSeconds, voiceRenderEstimateMs, voiceSampleFileName, voiceSampleKey, sampleOctave, parseVoiceSampleKey, voiceSampleScore, writesSinger, unsingableGuide, unsingableReason } from './singerCore';
import { CANTORIA_OUTDATED_CONTEXT, CantoriaVoice, cantoriaIsOutdated, firstSongFor, galleryQuery, hasNvidiaGpu, newestInGallery } from './singerCore';
import { VoiceProblems, engineFailureProblem, engineSaid, failurePlace, noVocoderGuide, singerFailureText, unsungSyllableNotice, unsungSyllableProblem, vocoderMissing, voiceWho, wordsProblems } from './singerCore';

export interface SingingDeps {
    getClient: () => LanguageClient | undefined;
    isReady: () => boolean;
    /** The extension's output channel: every `singer:` line is written there. */
    outputChannel: vscode.OutputChannel;
    /** The open previews, by their document's uri. */
    previewPanels: ReadonlyMap<string, vscode.WebviewPanel>;
    /** What each preview's practice menu is set to (its speed, the voices turned on). */
    previewPractice: { get(panel: vscode.WebviewPanel): PracticeState | undefined };
    /** The score each document's preview shows, where it is not the first. */
    selectedRenders: ReadonlyMap<string, string>;
    /** The `lilysharp` settings, as the extension keeps them for a keystroke. */
    settings: () => vscode.WorkspaceConfiguration;
}

let outputChannel: vscode.OutputChannel;
let previewPanels: SingingDeps['previewPanels'];
let previewPractice: SingingDeps['previewPractice'];
let selectedRenders: SingingDeps['selectedRenders'];
let settings: SingingDeps['settings'];
/** The server's client once it is up, else undefined. */
let server: () => LanguageClient | undefined = () => undefined;

/** Called once from activate, before anything here can be asked for. */
export function registerSinging(context: vscode.ExtensionContext, deps: SingingDeps): void {
    ({ outputChannel, previewPanels, previewPractice, selectedRenders, settings } = deps);
    server = () => { const client = deps.getClient(); return client && deps.isReady() ? client : undefined; };
    context.subscriptions.push(vscode.languages.registerCompletionItemProvider({ language: 'lilysharp' }, singerCompletion, '"', ' ', ','));
    context.subscriptions.push(vscode.languages.registerCompletionItemProvider({ language: 'lilysharp' }, singerSettingCompletion, ' '));
    context.subscriptions.push(vscode.languages.registerCompletionItemProvider({ language: 'lilysharp' }, singMarkCompletion, '(', ' '));
    // …and what a voice does not take is marked where it is written (markSingProblems).
    context.subscriptions.push(singDiagnostics,
        vscode.workspace.onDidOpenTextDocument(scheduleSingProblems),
        vscode.workspace.onDidChangeTextDocument(e => scheduleSingProblems(e.document)),
        vscode.workspace.onDidCloseTextDocument(d => singDiagnostics.delete(d.uri)),
        voiceDiagnostics,
        vscode.workspace.onDidCloseTextDocument(d => { voiceProblems.forget(d.uri.toString()); voiceDiagnostics.delete(d.uri); }),
        vscode.extensions.onDidChange(() => { void setCantoriaOutdated(); for (const d of vscode.workspace.textDocuments) { scheduleSingProblems(d); } }));
    for (const d of vscode.workspace.textDocuments) { scheduleSingProblems(d); }
    voiceSamplesDir = path.join(context.globalStorageUri.fsPath, 'voice-samples');
    context.subscriptions.push(vscode.commands.registerCommand('lilysharp.hearSinger', hearSinger));
    context.subscriptions.push(vscode.commands.registerCommand('lilysharp.singerTaken', singerTaken));
    context.subscriptions.push(vscode.commands.registerCommand('lilysharp.setUpVoices', setUpVoices));
}

// ---- The singing voice (docs/singing-design.md §5) ----

/** A file's `singer` came to another voice than it asked for: said once a session. */
const singerNotesTold = new Set<string>();
function tellSingerNote(text: string): void {
    if (singerNotesTold.has(text)) { return; }
    singerNotesTold.add(text);
    outputChannel.appendLine(`singer: ${text}`);
    vscode.window.setStatusBarMessage(`Lily#: ${text}`, 8000);
}

/**
 * At a `singer`'s name, the names right for the words' language (owner's request
 * 2026-11-09, session 1168): the server says whether the caret is in one and what the language
 * is (lilysharp/singerSlot), the Cantoria extension what is installed (singerChoices).
 * The quotes' inside is replaced whole, so a name with a slash completes as one. Before any quote
 * (`singer |`, owner's report 2026-11-09, session 1196) a name goes in with its quotes, the caret
 * between them as if it had been typed there, and `male` and `female` are offered too.
 */
const singerCompletion: vscode.CompletionItemProvider = {
    async provideCompletionItems(document, position, _token, context) {
        const client = server();
        if (!client) { return undefined; }
        if ((context.triggerCharacter === ' ' || context.triggerCharacter === ',') && !afterSingerWord(document.lineAt(position.line).text.slice(0, position.character))) { return undefined; }
        const slot = await client.sendRequest<{ InSlot: boolean; Language?: string | null; AnyLanguage?: boolean; Gender?: string | null; Quoted?: boolean }>('lilysharp/singerSlot',
            { textDocument: { uri: document.uri.toString() }, offset: document.offsetAt(position) });
        if (!slot.InSlot || !slot.Language) { return undefined; }
        const engine = await renderer();
        const api = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend.api : undefined;
        const line = document.lineAt(position.line).text;
        const open = line.lastIndexOf('"', position.character - 1);
        const close = line.indexOf('"', position.character);
        // At the top level, a voice taken goes on to `on` and the list of the file's parts
        // (owner's request 2026-11-11, and the part before the words since 2026-11-12): the name
        // is closed, ` on ` written after it and the list asked again — the server's, which
        // names the parts there, and after the part what the line may say next. The closing quote is
        // then part of what the item replaces.
        const goesOn = singerGoesOnToSings(document.getText(new vscode.Range(0, 0, position.line, 0)), line,
            line.slice(slot.Quoted && close >= 0 ? close + 1 : position.character));
        const range = slot.Quoted
            ? new vscode.Range(position.line, open + 1, position.line, close >= 0 ? close + (goesOn ? 1 : 0) : position.character)
            : new vscode.Range(position.line, position.character - (/[^\s,"]*$/.exec(line.slice(0, position.character))?.[0].length ?? 0), position.line, position.character);
        // Where the name stands once written: what hearing it asks the words' language by.
        const nameColumn = slot.Quoted ? open + 1 : range.start.character + 1;
        if (!api?.voices) {
            offerCantoria();
            return vscode.extensions.getExtension(CANTORIA_EXTENSION_ID) || singerSetting() ? undefined : [noVoiceItem(document, range, noVoiceGuide(false))];
        }
        const voices = await api.voices();
        // Words not named yet and written in several languages: every voice (docs/lyrics-singer-design.md §7).
        const language = slot.AnyLanguage ? undefined : slot.Language!;
        samples().warm(voices.filter(v => !language || singsLanguage(v, language)).map(v => voiceSampleKey(v.id, sampleOctave(v.gender, slot.Gender), slot.Language!)));
        const guide = voices.length === 0 ? noVoiceGuide(true) : !language || voices.some(v => singsLanguage(v, language)) ? undefined : noLanguageGuide(language, voices);
        if (guide) { offerVoiceSetup(guide); }
        const items = singerChoices(voices, language, slot.Gender).map((c, i) => {
            const item = new vscode.CompletionItem(c.label, vscode.CompletionItemKind.Value);
            item.detail = c.detail;
            item.range = range;
            item.filterText = c.label;
            item.sortText = String(i).padStart(3, '0');
            if (goesOn) {
                item.insertText = new vscode.SnippetString().appendText(`${slot.Quoted ? '' : '"'}${c.label}" on `).appendTabstop(0);
                item.command = { command: 'lilysharp.singerTaken', title: 'Hear this voice, and list the parts', arguments: [c.label, nameColumn] };
            } else {
                if (!slot.Quoted) { item.insertText = new vscode.SnippetString(`"${c.label}$0"`); }
                item.command = { command: 'lilysharp.hearSinger', title: 'Hear this voice', arguments: [c.label] };
            }
            return item;
        });
        if (!slot.Quoted && !slot.Gender) {
            for (const gender of ['female', 'male']) {
                const item = new vscode.CompletionItem(gender, vscode.CompletionItemKind.Keyword);
                item.detail = `any ${gender} voice, or name one`;
                item.range = range;
                item.sortText = ` ${gender}`;
                item.insertText = new vscode.SnippetString(`${gender} "$0"`);
                item.command = { command: 'editor.action.triggerSuggest', title: 'List the voices' };
                items.push(item);
            }
        }
        if (guide) { items.unshift(noVoiceItem(document, range, guide)); }
        return items;
    },
};

/**
 * The list on a singer line's settings (owner's decisions 2026-11-12: `singer … with breathiness
 * 0.3 gender -0.2`): the word `with` where the line has none yet, and after it the settings the
 * line's own voice takes, as the voice's engine names them — each with what it does and its range
 * (Cantoria's `parameters`). Nothing is listed that the voice does not take, and what the line
 * has already set is left out. A value is the writer's to type: its range is in the name's line.
 */
const singerSettingCompletion: vscode.CompletionItemProvider = {
    async provideCompletionItems(document, position) {
        const before = document.lineAt(position.line).text.slice(0, position.character);
        const slot = singerSettingSlot(before);
        if (!slot) { return undefined; }
        const engine = await renderer();
        const backend = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend : undefined;
        if (!backend) { return undefined; }
        // The line's voice: the name it writes, else the gender it says, else the default voice.
        // ⚠️ As the score's own voice is found (VoiceRenderer): by the words' language and the part's
        // gender too, or a line that names no voice lists another voice's settings than the one
        // that sings it.
        const named = new RegExp(String.raw`^[ \t]*singer[ \t]+${SINGER_NAME}(?:(male|female)[ \t]+)?${SINGER_LANGUAGE}(?:"([^"]*)")?`).exec(before);
        const lineText = document.lineAt(position.line).text;
        const asked = new vscode.Position(position.line, singerLanguageColumn(lineText, singerLineOf(lineText)?.askColumn ?? position.character));
        const client = server();
        const where = client ? await client.sendRequest<{ Language?: string | null; Gender?: string | null }>('lilysharp/singerSlot',
            { textDocument: { uri: document.uri.toString() }, offset: document.offsetAt(asked) }).then(s => s, () => undefined) : undefined;
        let taken: SingParameter[];
        let voice: string;
        try {
            voice = await backend.voiceName(named?.[2] || named?.[1] || null, where?.Language ?? undefined, where?.Gender);
            taken = await backend.parameters(voice);
        } catch (e) {
            outputChannel.appendLine(`singer: the voice's settings could not be listed — ${String(e)}`);
            return undefined;
        }
        // `neutrino/MERROW` → neutrino: the engine the settings are of.
        const engineName = voice.includes('/') ? voice.slice(0, voice.indexOf('/')) : voice;
        if (slot.kind === 'value') {
            // A setting that takes a NAME lists its names; a number, values across its range
            // to begin from (owner's request 2026-11-12, after `with transpose `).
            const of = taken.find(p => p.name === slot.of);
            if (!of) { return undefined; }
            if (of.word) {
                return (of.values ?? []).map((name, i) => {
                    const item = new vscode.CompletionItem(name, vscode.CompletionItemKind.Value);
                    item.sortText = String(i).padStart(3, '0');
                    return item;
                });
            }
            return singValueItems(of, document, position);
        }
        if (slot.kind === 'with') {
            if (taken.length === 0) { return undefined; }
            // Whose settings they are is said — by the engine, which is what decides them
            // (owner's decision 2026-11-12: the voice's whole name is more than is needed).
            const item = new vscode.CompletionItem({ label: 'with', description: engineName }, vscode.CompletionItemKind.Keyword);
            item.detail = `How the voice sings: ${taken.map(p => p.name).join(', ')}`;
            item.insertText = 'with ';
            item.sortText = '~with';     // after what the line still needs (`on`)
            item.command = { command: 'editor.action.triggerSuggest', title: 'List the voice\'s settings' };
            return [item];
        }
        return taken.filter(p => !slot.written.includes(p.name)).map((p, i) => {
            const item = new vscode.CompletionItem({ label: p.name, description: engineName }, vscode.CompletionItemKind.Property);
            item.detail = singParameterDetail(p);
            item.insertText = p.name + ' ';
            item.filterText = p.name;
            item.sortText = String(i).padStart(3, '0');
            item.command = { command: 'editor.action.triggerSuggest', title: 'List values to begin from' };
            return item;
        });
    },
};

/** The values a number setting is begun from, least to most; the one that changes nothing is
 *  where the list opens (singValueChoices). */
function singValueItems(of: SingParameter, document: vscode.TextDocument, position: vscode.Position): vscode.CompletionItem[] {
    // What is written of the value so far, its sign included: a `-` is no part of a word to the
    // editor, and an item taken after one would stand behind it (`--3`).
    const begun = /[^\s(]*$/.exec(document.lineAt(position.line).text.slice(0, position.character))?.[0].length ?? 0;
    const range = new vscode.Range(position.line, position.character - begun, position.line, position.character);
    return singValueChoices(of).map((c, i) => {
        const item = new vscode.CompletionItem(c.text, vscode.CompletionItemKind.Value);
        item.range = range;
        item.detail = c.detail;
        item.sortText = String(i).padStart(3, '0');
        item.filterText = c.text;
        item.preselect = c.isDefault;
        return item;
    });
}

/**
 * The list inside a note's `@sing(…)` (owner's decisions 2026-11-12): the settings that can change
 * inside the music — every one but those that are one value for the voice; one value a phrase
 * steps where a phrase begins, and its line says so — that any voice the file's singer lines name
 * takes, each with what it does and its range; and where a value is expected, the two words a
 * value may be: `default` (the singer line's value again) and `to` (arrive at the value that
 * follows, gliding).
 */
const singMarkCompletion: vscode.CompletionItemProvider = {
    async provideCompletionItems(document, position) {
        const slot = singMarkSlot(document.lineAt(position.line).text.slice(0, position.character));
        if (!slot) { return undefined; }
        const engine = await renderer();
        const backend = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend : undefined;
        if (slot.kind === 'value') {
            const back = new vscode.CompletionItem('default', vscode.CompletionItemKind.Keyword);
            back.detail = `${slot.of} as the singer line has it again (or as the voice does by itself)`;
            back.sortText = '~1';
            const to = new vscode.CompletionItem('to', vscode.CompletionItemKind.Keyword);
            to.detail = `Arrive at the value written after it with this note, gliding from the ${slot.of} before`;
            to.insertText = 'to ';
            to.sortText = '~2';
            // …and before the two words, values across the setting's range to begin from.
            let numbers: vscode.CompletionItem[] = [];
            if (backend) {
                const singers = singersWritten(document.getText());
                for (const who of singers.length > 0 ? singers : ['']) {
                    try {
                        const of = (await backend.parameters(await backend.voiceName(who || null))).find(p => p.name === slot.of);
                        if (of) { numbers = singValueItems(of, document, position); break; }
                    } catch { /* that voice's settings could not be listed: the two words stand alone */ }
                }
            }
            return [...numbers, back, to];
        }
        if (!backend) { return undefined; }
        const taken = new Map<string, SingParameter>();
        const singers = singersWritten(document.getText());
        for (const who of singers.length > 0 ? singers : ['']) {
            try {
                for (const p of await backend.parameters(await backend.voiceName(who || null))) {
                    if (p.scope !== 'voice' && !taken.has(p.name)) { taken.set(p.name, p); }
                }
            } catch (e) {
                outputChannel.appendLine(`singer: the settings of ${who || 'the default voice'} could not be listed — ${String(e)}`);
            }
        }
        return [...taken.values()].filter(p => !slot.written.includes(p.name)).map((p, i) => {
            const item = new vscode.CompletionItem(p.name, vscode.CompletionItemKind.Property);
            item.detail = singParameterDetail(p);
            item.insertText = p.name + ' ';
            item.sortText = String(i).padStart(3, '0');
            item.command = { command: 'editor.action.triggerSuggest', title: 'List values to begin from' };
            return item;
        });
    },
};

/**
 * A voice's settings marked where they are written (owner's decisions 2026-11-12): what the
 * document's singer lines and notes set (`lilysharp/singSettings`), against what the voices'
 * engine says each takes (Cantoria's `parameters`) — a name it does not take, a value out of its
 * range, a setting that cannot change inside the music. The server does not know the engines and
 * the engine does not know the document, so the two are joined here, a moment after the text
 * rests. Nothing is marked without a Cantoria that says what a voice takes: its silence would
 * read as "takes nothing". A document that sets nothing asks no engine.
 */
const singDiagnostics = vscode.languages.createDiagnosticCollection('Lily# singer');
const singTimers = new Map<string, ReturnType<typeof setTimeout>>();
function scheduleSingProblems(document: vscode.TextDocument): void {
    if (document.languageId !== 'lilysharp') { return; }
    const key = document.uri.toString();
    const pending = singTimers.get(key);
    if (pending) { clearTimeout(pending); }
    singTimers.set(key, setTimeout(() => { singTimers.delete(key); void markSingProblems(document); }, 700));
}
async function markSingProblems(document: vscode.TextDocument, tries = 0): Promise<void> {
    if (document.isClosed) { return; }
    // A file open before the server is up (the window's start): asked again when it is, a few times.
    const client = server();
    if (!client) {
        if (tries < 5) { setTimeout(() => void markSingProblems(document, tries + 1), 2000); }
        return;
    }
    const version = document.version;
    let written: { Settings?: SingSettingDto[] | null; Singers?: (string | null)[] | null };
    try {
        written = await client.sendRequest('lilysharp/singSettings', { textDocument: { uri: document.uri.toString() } });
    } catch {
        return;
    }
    if (document.version !== version) { return; }
    if (!written.Settings || written.Settings.length === 0) { singDiagnostics.delete(document.uri); return; }
    const engine = await renderer();
    const backend = engine?.backend instanceof CantoriaExtensionBackend && engine.backend.listsParameters ? engine.backend : undefined;
    if (!backend) { singDiagnostics.delete(document.uri); return; }
    const takenBy = new Map<string, SingParameter[]>();
    try {
        const singers = written.Singers && written.Singers.length > 0 ? written.Singers : [null];
        for (const who of singers) { takenBy.set(who ?? '', await backend.parameters(await backend.voiceName(who))); }
    } catch (e) {
        // A voice that is not installed, an engine that did not start: nothing can be said of the settings.
        outputChannel.appendLine(`singer: the voices' settings could not be checked — ${String(e)}`);
        singDiagnostics.delete(document.uri);
        return;
    }
    if (document.version !== version) { return; }
    singDiagnostics.set(document.uri, singProblems(written.Settings, takenBy).map(p => {
        const d = new vscode.Diagnostic(new vscode.Range(document.positionAt(p.start), document.positionAt(p.end)), p.message, vscode.DiagnosticSeverity.Warning);
        d.source = 'Lily# singer';
        return d;
    }));
}

/** The first line of the `singer` list when no voice is installed (owner's request 2026-11-10): it
 *  says so and inserts nothing, the text under it kept, and taking it goes on to the setup. */
function noVoiceItem(document: vscode.TextDocument, range: vscode.Range, guide: VoiceGuide): vscode.CompletionItem {
    const typed = document.getText(range);
    const item = new vscode.CompletionItem(guide.label, vscode.CompletionItemKind.Issue);
    item.detail = guide.detail;
    item.documentation = new vscode.MarkdownString(guide.documentation);
    item.range = range;
    item.filterText = typed;
    item.insertText = typed;
    item.sortText = '  ';
    item.preselect = true;
    item.command = { command: 'lilysharp.setUpVoices', title: guide.detail, arguments: [guide] };
    return item;
}

/** Where a chosen voice's sample is kept, across sessions, so a voice heard once comes at once. */
let voiceSamplesDir: string | undefined;
let voiceSampleQueue: VoiceSampleQueue | undefined;
/** The voice last asked to be heard: a sample landing after another was chosen stays silent. */
let voiceSampleWanted = '';

/** A voice's sample; one sung with a line's settings (its key has a `!`) in a folder of its own,
 *  which keeps the newest few: every value a setting is stepped through is one more. */
const TRIED_SAMPLES_KEPT = 40;
function voiceSampleFile(id: string): string {
    return id.includes('!')
        ? path.join(voiceSamplesDir ?? '', 'tried', voiceSampleFileName(id))
        : path.join(voiceSamplesDir ?? '', voiceSampleFileName(id));
}
/** Lets go of the oldest samples sung with settings, past TRIED_SAMPLES_KEPT. */
function trimTriedSamples(): void {
    const folder = path.join(voiceSamplesDir ?? '', 'tried');
    try {
        const files = fs.readdirSync(folder).map(name => ({ file: path.join(folder, name), at: fs.statSync(path.join(folder, name)).mtimeMs }))
            .sort((a, b) => b.at - a.at);
        for (const old of files.slice(TRIED_SAMPLES_KEPT)) { try { fs.unlinkSync(old.file); } catch { /* in use */ } }
    } catch { /* no such folder yet */ }
}

function samples(): VoiceSampleQueue {
    return voiceSampleQueue ??= new VoiceSampleQueue(id => fs.existsSync(voiceSampleFile(id)), singVoiceSample,
        (id, e) => {
            outputChannel.appendLine(`singer: the sample of ${id} was not sung: ${String(e)}`);
            // A DiffSinger voice taken from the list that has no vocoder: said with the way to one,
            // once a session for each such voice (owner's request 2026-11-18).
            if (vocoderMissing(e)) { offerVoiceSetup(noVocoderGuide(String(e), parseVoiceSampleKey(id).id)); }
        });
}

async function singVoiceSample(key: string): Promise<void> {
    const engine = await renderer();
    const backend = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend : undefined;
    if (!backend?.api.voices) { throw new Error('no engine that lists its voices'); }
    const { id, language: asked, octave, sing } = parseVoiceSampleKey(key);
    const voice = (await backend.api.voices()).find(v => v.id === id);
    const language = asked ?? voice?.language;
    const score = language ? voiceSampleScore(language, octave) : undefined;
    if (!score) { throw new Error(`no sample phrase for the language ${language ?? 'it does not name'}`); }
    // The phrase as the line says the voice sings (its `with …`): what a setting does is heard.
    if (sing && Object.keys(sing).length > 0) { score.sing = sing; }
    const v = await backend.sing(`sample-${key}`, score, id);
    for (const note of v.notes ?? []) { tellSingerSetting(note); }
    await fs.promises.mkdir(path.dirname(voiceSampleFile(key)), { recursive: true });
    await fs.promises.copyFile(v.wav, voiceSampleFile(key));
    if (key.includes('!')) { trimTriedSamples(); }
}

/** Lets the document's preview say a short phrase in `id` (with no preview open it is silent, as every audition is). */
async function auditionVoice(uri: string, id: string, clock: StageClock, language?: string, lineGender?: string | null,
    sing?: Record<string, number | string>): Promise<void> {
    const engine = await renderer();
    const voice = engine?.backend instanceof CantoriaExtensionBackend ? (await engine.backend.api.voices?.())?.find(v => v.id === id) : undefined;
    const key = voiceSampleKey(id, sampleOctave(voice?.gender, lineGender), language, sing);
    voiceSampleWanted = key;
    const songAtStart = voiceRenderer?.singing ? 'being sung' : 'not being sung';
    const say = (end: string) => outputChannel.appendLine(`singer: hear ${id}: ${clock}${end} (the song was ${songAtStart} at the start)`);
    if (!vscode.workspace.getConfiguration('lilysharp').get<boolean>('audition.enabled', true)) { say(', auditions are off'); return; }
    if (!previewPanels.get(uri)) { say(', no preview is open'); return; }
    const onDisk = fs.existsSync(voiceSampleFile(key));
    // A sample with the line's settings takes the place of the ones of that voice still waiting:
    // stepping a value asks one a step, and only the last is wanted.
    const ready = await samples().ask(key, sing ? other => other.includes('!') && other.slice(0, other.indexOf('!')) === key.slice(0, key.indexOf('!')) : undefined);
    clock.mark(onDisk ? 'sample on disk' : 'sample sung');
    if (!ready) { say(', the sample could not be sung'); return; }
    if (voiceSampleWanted !== key) { say(', another voice was chosen meanwhile'); return; }
    const panel = previewPanels.get(uri);
    if (!panel) { say(', the preview closed meanwhile'); return; }
    const bytes = await fs.promises.readFile(voiceSampleFile(key));
    clock.mark('read');
    if (voiceSampleWanted !== key) { say(', another voice was chosen meanwhile'); return; }
    say('');
    void panel.webview.postMessage({ type: 'playVoiceSample', bytes: new Uint8Array(bytes), id, pressedAt: clock.startedAt, sentAt: Date.now() });
}

/** A name taken from the `singer` list at the top level (owner's request 2026-11-11): ` sings ` is
 *  written after it, so the list is asked again — the file's lyrics — and the voice is heard
 *  meanwhile, its words' language asked where the name stands (`nameColumn`), not at the caret. */
async function singerTaken(label: unknown, nameColumn: unknown): Promise<void> {
    void vscode.commands.executeCommand('editor.action.triggerSuggest');
    await hearSinger(label, nameColumn);
}

/** A name taken from the `singer` list: heard at once. A gender or a role is heard as the default voice it comes to. */
export async function hearSinger(label: unknown, nameColumn?: unknown): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (typeof label !== 'string' || !editor) { return; }
    const clock = new StageClock(Date.now());
    const engine = await renderer();
    if (!engine) { offerCantoria(); return; }
    clock.mark('engine');
    const voice = await engine.backend.voiceName(label);
    clock.mark('name');
    const caret = editor.selection.active;
    const lineText = editor.document.lineAt(caret.line).text;
    // The line as it stands: how it says the voice sings (its `with …`) is heard with the voice,
    // and with the caret in those settings the words' language is asked at the voice's name.
    const line = singerLineOf(lineText);
    const inSettings = line && line.withAt >= 0 && caret.character >= line.withAt;
    const asked = new vscode.Position(caret.line, singerLanguageColumn(lineText,
        typeof nameColumn === 'number' ? nameColumn : inSettings ? line.askColumn : caret.character));
    const client = server();
    const slot = client ? await client.sendRequest<{ Language?: string | null; Gender?: string | null }>('lilysharp/singerSlot',
        { textDocument: { uri: editor.document.uri.toString() }, offset: editor.document.offsetAt(asked) }).then(s => s, () => undefined) : undefined;
    await auditionVoice(editor.document.uri.toString(), voice, clock, slot?.Language ?? undefined, slot?.Gender,
        line && Object.keys(line.sing).length > 0 ? line.sing : undefined);
}

/**
 * The step keys on a `singer "…"` (owner's requests 2026-11-09, sessions 1170 and 1188): Ctrl+Shift+Up /
 * Down the voice above or below in the whole list, Ctrl+Shift+PageUp / PageDown the first voice of
 * another engine, among the voices that sing the words' language. The chosen voice says its phrase, and the score's own
 * voice follows from the edit (prepareVoices). False when the caret is not on one.
 */
export async function stepSingerAt(editor: vscode.TextEditor, direction: 1 | -1, byVoice: boolean, pressedAt: number): Promise<boolean> {
    const client = server();
    if (!client || editor.selections.length !== 1) { return false; }
    const doc = editor.document;
    const version = doc.version;
    const line = editor.selection.active.line;
    // On the line's `male` / `female` the keys change that word (the server's step).
    if (onSingerGender(doc.lineAt(line).text, editor.selection.active.character)) { return false; }
    const at = singerValueAt(doc.lineAt(line).text, editor.selection.active.character);
    if (!at) { return false; }
    const clock = new StageClock(pressedAt);
    clock.mark('key queue');
    const slot = await client.sendRequest<{ InSlot: boolean; Language?: string | null; Gender?: string | null }>('lilysharp/singerSlot',
        { textDocument: { uri: doc.uri.toString() }, offset: doc.offsetAt(new vscode.Position(line, at.start)) });
    clock.mark('server');
    if (!slot.InSlot || !slot.Language) { return false; }
    const engine = await renderer();
    clock.mark('engine');
    const api = engine?.backend instanceof CantoriaExtensionBackend ? engine.backend.api : undefined;
    if (!api?.voices) {
        offerCantoria();
        vscode.window.setStatusBarMessage('Lily#: the Cantoria extension lists the voices, and it is not there', 4000);
        return true;
    }
    const sings = (await api.voices()).filter(v => singsLanguage(v, slot.Language!));
    samples().warm(sings.map(v => voiceSampleKey(v.id, sampleOctave(v.gender, slot.Gender), slot.Language!)));
    const voices = sings.filter(v => fitsGender(v, slot.Gender));
    clock.mark('voices');
    const next = stepVoice(byVoiceId(voices), at.value, direction, !byVoice);
    if (!next) {
        vscode.window.setStatusBarMessage(byVoice ? `Lily#: ${at.value} is the end of the voices that sing ${slot.Language}`
            : `Lily#: no further provider has voices for ${slot.Language} that way`, 4000);
        return true;
    }
    if (doc.version !== version) { return true; }     // typed into meanwhile: the place is about another text
    const applied = await editor.edit(b => b.replace(new vscode.Range(line, at.start, line, at.end), next), { undoStopBefore: true, undoStopAfter: true });
    if (!applied) { return true; }
    clock.mark('edit');
    const caret = new vscode.Position(line, at.start + next.length);
    editor.selection = new vscode.Selection(caret, caret);
    const label = voices.find(v => v.id === next)?.label;
    vscode.window.setStatusBarMessage(`Lily#: singer ${next}${label ? ` (${label})` : ''}`, 4000);
    void auditionVoice(doc.uri.toString(), next, clock, slot.Language, slot.Gender);
    return true;
}

/** Whether this session has already said that Cantoria is needed. */
let cantoriaOffered = false;

/** Says, once a session, that voices need the Cantoria extension, and offers to install it (owner's
 *  request 2026-11-09, session 1205). Only where a voice was asked for: lyrics alone say nothing, since
 *  most people writing them mean them for the page. */
function offerCantoria(): void {
    if (cantoriaOffered || vscode.extensions.getExtension(CANTORIA_EXTENSION_ID)) { return; }
    cantoriaOffered = true;
    const install = 'Install Cantoria';
    void vscode.window.showInformationMessage(noVoiceGuide(false).message, install).then(async choice => {
        if (choice === install) { await installCantoria(); }
    });
}

export async function installCantoria(): Promise<void> {
    try {
        await vscode.commands.executeCommand('workbench.extensions.installExtension', CANTORIA_EXTENSION_ID);
    } catch {
        // Not reachable from here (offline, or a gallery that does not list it): show the search.
        await vscode.commands.executeCommand('workbench.extensions.search', `@id:${CANTORIA_EXTENSION_ID}`);
    }
}

/** The walkthrough's first song for the voices installed here (firstSongFor): Cantoria is asked
 *  what its voices sing; not installed, or not answering within a few seconds, the Japanese one.
 *  `whole` unsaid: the whole song where there is an NVIDIA GPU, its opening alone where not. */
export async function firstSongFile(whole: boolean = hasNvidiaGpu(process.platform, process.env, fs.existsSync)): Promise<string> {
    let voices: CantoriaVoice[] = [];
    try {
        const asked = (async () => {
            const engine = await renderer();
            return engine?.backend instanceof CantoriaExtensionBackend ? await engine.backend.api.voices?.() ?? [] : [];
        })().catch((): CantoriaVoice[] => []);      // (also when it fails after the wait is over: no one is listening then)
        voices = await Promise.race([asked, new Promise<CantoriaVoice[]>(resolve => setTimeout(() => resolve([]), 5000))]);
    } catch {
        // No engine to ask: the default.
    }
    return firstSongFor(voices, vscode.env.language, whole);
}

/** The newest Cantoria its gallery lists, as last asked (undefined: not asked, not answered, not listed). */
let cantoriaNewest: string | undefined;
/** The version {@link upgradeCantoria} installed this session: the one running still says its old version. */
let cantoriaUpgradedTo: string | undefined;

/** The gallery this VS Code installs from, as its product.json names it (the Marketplace, or
 *  another that speaks as it does), or undefined for a build with none. */
function galleryServiceUrl(): string | undefined {
    try {
        const product = JSON.parse(fs.readFileSync(path.join(vscode.env.appRoot, 'product.json'), 'utf8'));
        const url = product?.extensionsGallery?.serviceUrl;
        return typeof url === 'string' && /^https:\/\//.test(url) ? url.replace(/\/+$/, '') : undefined;
    } catch {
        return undefined;
    }
}

function setCantoriaOutdated(): Thenable<unknown> {
    const installed = vscode.extensions.getExtension(CANTORIA_EXTENSION_ID)?.packageJSON?.version;
    return vscode.commands.executeCommand('setContext', CANTORIA_OUTDATED_CONTEXT, cantoriaIsOutdated(installed, cantoriaNewest, cantoriaUpgradedTo));
}

/** Asks the gallery for Cantoria's newest version and sets {@link CANTORIA_OUTDATED_CONTEXT}: the
 *  walkthrough's first step is "Upgrade" while the one installed is older (owner's request
 *  2026-11-18). Only an installed Cantoria is asked about, and only its name is sent. No answer
 *  within `waitMs` (offline, a gallery that does not list it) leaves the step "Install", which an
 *  installed Cantoria has checked off; an answer that comes later still sets the step. */
export async function checkCantoriaUpgrade(waitMs = 2500): Promise<void> {
    const service = vscode.extensions.getExtension(CANTORIA_EXTENSION_ID) ? galleryServiceUrl() : undefined;
    if (!service) { await setCantoriaOutdated(); return; }
    const asked = (async () => {
        try {
            const answer = await fetch(`${service}/extensionquery`, {
                method: 'POST', signal: AbortSignal.timeout(15000),
                headers: { 'Content-Type': 'application/json', Accept: 'application/json;api-version=3.0-preview.1' },
                body: JSON.stringify(galleryQuery(CANTORIA_EXTENSION_ID)),
            });
            if (answer.ok) { cantoriaNewest = newestInGallery(await answer.json(), CANTORIA_EXTENSION_ID) ?? cantoriaNewest; }
        } catch {
            // Offline, or no such gallery: what was known stays.
        }
        await setCantoriaOutdated();
    })();
    await Promise.race([asked, new Promise<void>(resolve => setTimeout(resolve, waitMs))]);
}

/** The walkthrough's "Upgrade Cantoria": installs the gallery's newest over the one installed. The
 *  step goes back to "Install" (checked off), and the new version sings once the extensions are restarted. */
export async function upgradeCantoria(): Promise<void> {
    const was = vscode.extensions.getExtension(CANTORIA_EXTENSION_ID)?.packageJSON?.version;
    try {
        await vscode.commands.executeCommand('workbench.extensions.installExtension', CANTORIA_EXTENSION_ID);
    } catch {
        await vscode.commands.executeCommand('workbench.extensions.search', `@id:${CANTORIA_EXTENSION_ID}`);
        return;
    }
    cantoriaUpgradedTo = cantoriaNewest;
    await setCantoriaOutdated();
    const restart = 'Restart Extensions';
    const choice = await vscode.window.showInformationMessage(
        `Lily#: Cantoria ${cantoriaNewest ?? ''} is installed${was ? ` over ${was}` : ''}. It sings once the extensions are restarted.`.replace('  ', ' '), restart);
    if (choice === restart) { await vscode.commands.executeCommand('workbench.action.restartExtensionHost'); }
}

/** What this session has already said about voices that cannot be listed. */
const voiceSetupOffered = new Set<string>();

/** Says, once a session for each thing to say, that no voice can be listed. The `singer` list says it every time. */
function offerVoiceSetup(guide: Pick<VoiceGuide, 'message' | 'actions'>): void {
    if (voiceSetupOffered.has(guide.message)) { return; }
    voiceSetupOffered.add(guide.message);
    void showVoiceSetup(guide);
}

/** What would give a voice, as buttons on Cantoria's own commands. */
async function showVoiceSetup(guide: Pick<VoiceGuide, 'message' | 'actions'>): Promise<void> {
    const choice = await vscode.window.showInformationMessage(guide.message, ...guide.actions.map(a => a.label));
    const action = guide.actions.find(a => a.label === choice);
    if (action?.command) { await vscode.commands.executeCommand(action.command); }
    else if (action?.url) { void vscode.env.openExternal(vscode.Uri.parse(action.url)); }
}

/** The `singer` list's guide, taken: what it said, with its buttons, or else what a voice needs first. */
async function setUpVoices(guide: VoiceGuide): Promise<void> {
    if (vscode.extensions.getExtension(CANTORIA_EXTENSION_ID)) { await showVoiceSetup(guide); } else { await installCantoria(); }
}

/** `lilysharp.playback.singer`: the path of a `cantoria` program to run here (a developer's
 *  build), or '' — the usual case — for the Cantoria extension when it is installed. */
function singerSetting(): string {
    return settings().get<string>('playback.singer', '').trim();
}

/** The one engine of this host, found on the first voice asked for — the setting's program,
 *  else the Cantoria extension (CANTORIA_EXTENSION_ID, activated here; activating it starts
 *  no process) — and let go when the setting or the extensions change, and on deactivate.
 *  Undefined when there is neither: the lyrics are not sung and nothing is said. */
let voiceRenderer: VoiceRenderer | undefined;
let voiceRendererFor: string | undefined;   // the setting the renderer was made for
export async function renderer(): Promise<VoiceRenderer | undefined> {
    const exe = singerSetting();
    if (voiceRenderer && voiceRendererFor === exe) { return voiceRenderer; }
    disposeSinger();
    if (exe) {
        voiceRenderer = new VoiceRenderer(new SingerProcess(exe, line => outputChannel.appendLine(line)));
        voiceRenderer.onSinging = singingStarted;
        voiceRenderer.onProgress = singingProgress;
    } else {
        const ext = vscode.extensions.getExtension(CANTORIA_EXTENSION_ID);
        if (!ext) { return undefined; }
        try {
            const api = cantoriaApiOf(ext.isActive ? ext.exports : await ext.activate());
            if (!api) { outputChannel.appendLine(`singer: ${CANTORIA_EXTENSION_ID} is installed but its API is not the shape Lily# reads`); return undefined; }
            voiceRenderer = new VoiceRenderer(new CantoriaExtensionBackend(api, tellSingerNote));
            voiceRenderer.onSinging = singingStarted;
        voiceRenderer.onProgress = singingProgress;
        } catch (e) {
            outputChannel.appendLine(`singer: ${CANTORIA_EXTENSION_ID} did not activate: ${String(e)}`);
            return undefined;
        }
    }
    voiceRendererFor = exe;
    // The extension's voice is named with it (`engine/name`); a program's is its own default.
    const voice = voiceRenderer.backend instanceof CantoriaExtensionBackend ? await voiceRenderer.backend.voiceName().catch(() => '') : '';
    outputChannel.appendLine(`singer: ${voiceRenderer.name}${voice ? `, voice ${voice}` : ''}`);
    return voiceRenderer;
}
export function disposeSinger(): void {
    voiceRenderer?.dispose();
    voiceRenderer = undefined;
    voiceRendererFor = undefined;
    for (const panel of previewPanels.values()) {
        postedVoices.delete(panel);
        prepareQueue.cancel(`${panelId(panel)}/`);
        panel.webview.postMessage({ type: 'clearVoices' });
    }
}
/** The extension goes: the engine, and its own server with it. The previews are told nothing. */
export function deactivateSinging(): void {
    voiceRenderer?.dispose();
    voiceRenderer = undefined;
}

/** `lilysharp.playback.singerPrepare`: when a voice whose text changed goes to the engine. */
function prepareModeSetting(): PrepareMode {
    return prepareModeOf(settings().get<string>('playback.singerPrepare'));
}

/** What each preview's practice menu last listed of the voices (voiceMenuKey). */
export const menuVoices = new WeakMap<vscode.WebviewPanel, string>();
/** The voices each preview holds, part → key: a voice already there is not sent again. */
const postedVoices = new WeakMap<vscode.WebviewPanel, Map<string, string>>();
/** The document and score each preview's voices are of (`uri\nrender`): another one empties them first. */
const voiceDocs = new WeakMap<vscode.WebviewPanel, string>();
/** The previews that just turned to another document: its voices go to the engine at once, not after the rest an edit gets. */
const voiceTurned = new WeakSet<vscode.WebviewPanel>();
/** The parts each preview's score sings now (the last `voiceParts`): a voice landing for any
 *  other part is of a text that sang it and is dropped (owner's report 2026-11-09, session 1152:
 *  the lyrics row commented out, the microphone stayed — the engine's answer to the earlier text
 *  arrived after the page had emptied, and lit it again). Unknown until the first answer. */
const sungParts = new WeakMap<vscode.WebviewPanel, Set<string>>();
/** The voices of each preview's score that are NOT sung here, by key: a line nobody installed can
 *  sing (prepareVoices), a voice the engine failed (failVoice). A Play's notes leave their sections
 *  to the instrument where another line of the part sings (leftToTheInstrument; found 2026-11-13:
 *  they stayed silent). Of the preview's document and score: emptied with its voices. */
export const missingVoices = new WeakMap<vscode.WebviewPanel, Set<string>>();
/** A number per preview, for the prepare queue's slots (`<id>/<part>`). */
const panelIds = new WeakMap<vscode.WebviewPanel, number>();
let nextPanelId = 0;
function panelId(panel: vscode.WebviewPanel): number {
    let id = panelIds.get(panel);
    if (id === undefined) { panelIds.set(panel, id = ++nextPanelId); }
    return id;
}

/**
 * Keeps a preview's voices to the document and score it shows. When they change, what the page
 * holds is the last document's (kept by part name — two files with a `melody` would share a
 * buffer): the page is told to drop it and to show the microphone as PREPARING until the new
 * document's voices are named (owner's report 2026-11-08, session 1146: after the switch the
 * microphone stayed blue); the caller's prepareVoices then asks for the new voices, which go
 * to the engine at once (voiceTurned). Answers whether anything changed.
 */
export function followVoices(panel: vscode.WebviewPanel, uri: string): boolean {
    const voiceDoc = `${uri}\n${selectedRenders.get(uri) || ''}`;
    if (voiceDocs.get(panel) === voiceDoc) { return false; }
    voiceDocs.set(panel, voiceDoc);
    postedVoices.delete(panel);
    sungParts.delete(panel);
    missingVoices.delete(panel);
    voiceTurned.add(panel);
    prepareQueue.cancel(`${panelId(panel)}/`);
    toldNoEngine.delete(panel);
    void panel.webview.postMessage({ type: 'clearVoices', pending: true });
    return true;
}
/** The previews told, with no engine, that they hold no voice — and told nothing of voices since
 *  (followVoices' placeholder, an engine found). Most people have no engine and every refresh
 *  asks for the voices: until session 1296 each keystroke sent the page a `clearVoices` and wrote
 *  a line to the output channel, to say again what had not changed. */
const toldNoEngine = new WeakSet<vscode.WebviewPanel>();
/** A preview closed: nothing more is sung for it, and a voice in flight is dropped on landing. */
export function forgetVoices(panel: vscode.WebviewPanel): void {
    voiceDocs.delete(panel);
    postedVoices.delete(panel);
    sungParts.delete(panel);
    missingVoices.delete(panel);
    voiceTurned.delete(panel);
    prepareQueue.cancel(`${panelId(panel)}/`);
}
/** What was said once per preview (a warning of the server's, a voice that was not sung). */
const singerTold = new WeakMap<vscode.WebviewPanel, Set<string>>();
/** What the engine said of a singer line's settings, shown once a session each: a setting written
 *  and not sung is a mistake in the score, so it is a warning and not a line of the status bar. */
const singerSettingsTold = new Set<string>();
function tellSingerSetting(text: string): void {
    outputChannel.appendLine(`singer: ${text}`);
    if (singerSettingsTold.has(text)) { return; }
    singerSettingsTold.add(text);
    void vscode.window.showWarningMessage(`Lily#: ${text}`);
}

/** A syllable a voice has no sound for, said once a session for each (a document, a voice, a
 *  syllable): a notification with the way to the note it stands under. False when it was said
 *  before. Not by its place: the voice is asked for again at every edit, and a line typed above
 *  the note would say it again each time. */
const unsungSyllablesTold = new Set<string>();
function tellUnsungSyllable(document: vscode.TextDocument, problem: { start: number; end: number; message: string }, notice: string): boolean {
    const key = `${document.uri.toString()}\n${problem.message}`;
    if (unsungSyllablesTold.has(key)) { return false; }
    unsungSyllablesTold.add(key);
    void vscode.window.showWarningMessage(`Lily#: ${notice}`, 'Show the Note').then(async picked => {
        if (!picked) { return; }
        const editor = await vscode.window.showTextDocument(document, { preview: false });
        const range = new vscode.Range(document.positionAt(problem.start), document.positionAt(problem.end));
        editor.selection = new vscode.Selection(range.start, range.end);
        editor.revealRange(range, vscode.TextEditorRevealType.InCenter);
    });
    return true;
}

function tellSingerOnce(panel: vscode.WebviewPanel, text: string): void {
    outputChannel.appendLine(`singer: ${text}`);
    let told = singerTold.get(panel);
    if (!told) { singerTold.set(panel, told = new Set()); }
    if (told.has(text)) { return; }
    told.add(text);
    vscode.window.setStatusBarMessage(`Lily#: ${text}`, 8000);
}

/** The pane's voice badge (owner's request 2026-11-08): a voice that will not be sung, with the
 *  engine's sentence (`ready` is the page's own, when it has decoded the WAV). */
function failVoice(panel: vscode.WebviewPanel, part: string, speed: number, why: string, voice?: SungVoiceDto): void {
    forgetAskedVoice(panel, part);
    // The words themselves cannot be sung by what is installed (a language no voice sings): that
    // is the writer's to know, not a render gone wrong - said as tellUnsingable says it.
    const unsingable = unsingableReason(why);
    if (unsingable) { tellUnsingable(part, unsingable); }
    else {
        // The engine FAILED on a voice it agreed to sing (owner, 2026-11-13, kaze.lys: the alto's
        // "Failed to load phrase data" was a line of the status bar, gone in eight seconds, that
        // named no line of the file and no cause): a warning on the voice's `singer` line in the
        // Problems panel - which line, what happened in plain words, where to look when the
        // server could tell, the engine's own words last - there until the voice is sung.
        const uri = voiceDocs.get(panel)?.split('\n')[0];
        const document = uri ? vscode.workspace.textDocuments.find(d => d.uri.toString() === uri) : undefined;
        const text = document?.getText() ?? '';
        const sentence = singerFailureText(voice ? voiceWho(voice, text) : part, why, voice ? failurePlace(voice, why, text) : undefined);
        outputChannel.appendLine(`singer: ${clockNow()} ${sentence}`);
        // A syllable the voice has no sound for (owner, 2026-11-19, Moon-on-the-River.lys: the
        // engine's `the phoneme "iz" is not in …phonemes.txt` on the singer line, and eight
        // seconds of the status bar): a word of the score has to change, so it is a warning on
        // the note itself too, and a notification that goes there - not a line that passes.
        const unsung = voice ? unsungSyllableProblem(voice, why, text) : undefined;
        if (uri && document && voice) {
            voiceProblems.set(uri, 'engine', part, unsung ? [engineFailureProblem(voice, why, text), unsung] : [engineFailureProblem(voice, why, text)]);
            showVoiceProblems(uri);
        }
        // Said before (the notification is once a session): the status bar's line, as for any failure.
        const notice = voice ? unsungSyllableNotice(voice, why, text) : undefined;
        if (!(unsung && notice && document && tellUnsungSyllable(document, unsung, notice))) {
            tellSingerOnce(panel, `${part} is not sung, and its instrument plays — why is in the Problems panel`);
        }
    }
    missingVoices.get(panel)?.add(part);
    void panel.webview.postMessage({ type: 'voiceState', part, speed, state: 'failed', detail: unsingable ?? why });
}

/** What is wrong with each document's voices now (singerCore.VoiceProblems), and its warnings in
 *  the Problems panel: the engine's failure on a voice at its `singer` line, the syllables a voice
 *  will not sing at their first note. A collection of its own: the settings' warnings
 *  (singDiagnostics) are set whole for a document and would take these away. */
const voiceProblems = new VoiceProblems();
const voiceDiagnostics = vscode.languages.createDiagnosticCollection('Lily# voices');
function showVoiceProblems(uri: string): void {
    const document = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri);
    if (!document) { voiceDiagnostics.delete(vscode.Uri.parse(uri)); return; }
    const range = (start: number, end: number) => new vscode.Range(document.positionAt(start), document.positionAt(end));
    voiceDiagnostics.set(document.uri, voiceProblems.of(uri).map(p => {
        const d = new vscode.Diagnostic(range(p.start, p.end), p.message, vscode.DiagnosticSeverity.Warning);
        d.source = 'Lily# singer';
        if (p.related) {
            d.relatedInformation = [new vscode.DiagnosticRelatedInformation(
                new vscode.Location(document.uri, range(p.related.start, p.related.end)), p.related.message)];
        }
        return d;
    }));
}

/**
 * A singer line nobody installed can sing (owner's decisions 2026-11-13: a Latin alto played with
 * no voice, and only a line of the status bar - gone in eight seconds - said why): a line in the
 * output channel each time, and ONE notification a session for each reason, with the way to a
 * voice that could sing it. The reason is Cantoria's own (its plan, or what its sing refused
 * with). The part's instrument plays: the page stands it in wherever a voice is not there.
 */
export function tellUnsingable(voice: string, reason: string): void {
    outputChannel.appendLine(`singer: ${clockNow()} ${voice} is not sung: ${reason}; its part's instrument plays`);
    offerVoiceSetup(unsingableGuide(reason));
}

/** The engine's speed as last timed, and when it last answered: the bar's estimates (session 1178). */
let voiceRenderRate = VOICE_RENDER_FIRST_RATE;
let voiceLastLanded = 0;
/** Whether voiceRenderRate is a render's, not the first guess: until it is, the patience with the
 *  engine assumes a CPU (prepareAllowanceMs). */
let voiceRenderTimed = false;
/** The patience with the engine, for every voice waiting on it (singerCore.EngineWatch). */
const engineWatch = new EngineWatch();

/** How long the engine should take over this voice at `speed`, for the bar under Play. */
function voiceEstimateMs(voice: SungVoiceDto, speed: number): number {
    return voiceRenderEstimateMs(sungSeconds(voice, speed), voiceRenderRate);
}

/** Why the voices are asked for: the text was rendered (`edit`), Play was pressed, the practice speed changed. */
type PrepareReason = 'edit' | 'play' | 'speed';

/** One voice to render for one preview; `mine` is the preview's postedVoices at the time (another map means the engine or the document changed). */
interface PrepareJob { panel: vscode.WebviewPanel; voice: SungVoiceDto; speed: number; mine: Map<string, string>; key: string; letGo: AbortController }

/** The engine's work, one job at a time per preview and part, the newest text winning
 *  (singerCore.PrepareQueue). A voice of an older text being sung is let go of as soon as a newer
 *  one comes: the engine finishes the phrase it is on and moves on, keeping what it sang (owner's
 *  decision 2026-11-10). A Play over the text already being sung changes nothing. */
const prepareQueue = new PrepareQueue<PrepareJob>(job => renderVoice(job), undefined,
    (running, newer) => { if (running.key !== newer.key) { running.letGo.abort(); } });

/** The clock for the voice lines of the output, which say when and why a voice waited. */
const clockNow = () => new Date().toTimeString().slice(0, 8);

/**
 * Keeps the voices of the score `panel` shows up to its text (owner's request 2026-11-08,
 * session 1148: the microphone said ready over a voice of the old words, and pulsed while
 * nothing was being sung). Asks the server for the sung voices (`lilysharp/sung`), names their
 * parts to the page (`voiceParts`), and for each compares the key its score and the engine's
 * voice would give with the key of the voice the page holds: the same — nothing to do; another —
 * the page drops the old voice and shows the part as WAITING, and the render is queued: at once
 * after a Play, a speed change or a turn to another document, or when the engine already has
 * the voice; after PREPARE_IDLE_MS of rest when the text was edited (typing restarts the clock,
 * and the engine never works under the keystrokes); not at all, until Play, with
 * `lilysharp.playback.singerPrepare` at onPlay. Nothing is asked when the server cannot answer
 * (the text has an error: the voices the page has stay as they are, the placeholder of a turn is
 * settled), and everything is dropped when there is no engine.
 */
export async function prepareVoices(panel: vscode.WebviewPanel, uri: string, reason: PrepareReason): Promise<void> {
    // The preview may have turned since the caller looked (a render that corrected the score's
    // name, selectedRenders): the keys are made to agree here, so a drift never leaves the
    // page's placeholder unanswered (session 1161).
    followVoices(panel, uri);
    const voiceDoc = `${uri}\n${selectedRenders.get(uri) || ''}`;
    const current = () => voiceDocs.get(panel) === voiceDoc;   // false once the preview turned or closed meanwhile
    const file = path.basename(vscode.Uri.parse(uri).fsPath);
    // One line per ask in the output channel: what was asked and how it ended, so a microphone
    // that stays can be read back (owner's report 2026-11-09, session 1161).
    const log = (what: string) => outputChannel.appendLine(`singer: ${clockNow()} prepare (${reason}) ${file}: ${what}`);
    const settled = () => { if (current()) { void panel.webview.postMessage({ type: 'voiceSettled' }); } };
    const client = server();
    if (!client) { log('the server is not up'); settled(); return; }
    const engine = await renderer();
    if (!current()) { log('the preview turned meanwhile'); return; }
    if (!engine) {
        if (reason === 'play' && writesSinger(vscode.workspace.textDocuments.find(d => d.uri.toString() === uri)?.getText() ?? '')) { offerCantoria(); }
        // Said once to a page: it holds nothing until an engine is found or it turns (toldNoEngine).
        if (toldNoEngine.has(panel)) { return; }
        toldNoEngine.add(panel);
        log('no engine: the voices are dropped');
        postedVoices.delete(panel);
        void panel.webview.postMessage({ type: 'clearVoices' });
        return;
    }
    toldNoEngine.delete(panel);
    let sung: { Voices?: SungVoiceDto[] | null; PrintedWords?: string[] | null; Error?: string | null };
    try {
        sung = await client.sendRequest('lilysharp/sung', { textDocument: { uri }, renderName: selectedRenders.get(uri) || null, allLyrics: true });
    } catch (e) {
        log(`lilysharp/sung failed: ${String(e)}`);
        settled();
        return;
    }
    if (!current()) { log('the preview turned meanwhile'); return; }
    if (sung.Error || !sung.Voices) { log(`no answer (${sung.Error ?? 'no voices'}): the states stay`); settled(); return; }
    // The menu's lines follow the text: a `singer` line rewritten renames its voice there. Rarely
    // true, so the costly parts list is asked only then.
    const listed = voiceMenuKey(sung.Voices.filter(v => v.Notes.length > 0));
    if (menuVoices.has(panel) && menuVoices.get(panel) !== listed) {
        menuVoices.set(panel, listed);
        void panel.webview.postMessage({ type: 'voiceLinesChanged' });
    }
    if (reason === 'play') { for (const part of sung.PrintedWords ?? []) { tellSingerOnce(panel, printedWordsNote(part)); } }
    // Which parts sing now: the score's own, and the file's other singable lyrics the menu turned
    // on (voicesOn, session 1163). The page drops the voices and the badge's states of any other
    // (a score or a file with no lyrics empties it — owner's report 2026-11-08: the microphone
    // stayed on after the lyrics were gone).
    const voicesOn = new Set(previewPractice.get(panel)?.voicesOn ?? []);
    const active = sung.Voices.filter(v => v.Notes.length > 0 && (v.InScore !== false || voicesOn.has(sungKey(v))));
    const parts = new Set(active.map(sungKey));
    log(parts.size === 0 ? 'no sung part' : `sung: ${[...parts].join(', ')}`);
    void panel.webview.postMessage({ type: 'voiceParts', parts: [...parts] });
    sungParts.set(panel, parts);
    const speed = previewPractice.get(panel)?.speed ?? 1;
    let posted = postedVoices.get(panel);
    if (!posted) { postedVoices.set(panel, posted = new Map()); }
    const mine = posted;
    // A part sung no more (its words gone): its voice is forgotten here as the page forgets it,
    // and a job of its earlier text, waiting or singing, lands nowhere (renderVoice's wanted).
    for (const part of [...mine.keys()]) {
        if (!parts.has(part)) { mine.delete(part); prepareQueue.cancelSlot(`${panelId(panel)}/${part}`); }
    }
    const turned = voiceTurned.delete(panel);
    const mode = prepareModeSetting();
    // The Problems panel follows the text: a voice the score sings no more takes its warnings
    // away, and each voice's words are said anew (a syllable that is not kana, at its note).
    const shownText = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri)?.getText();
    let problemsChanged = voiceProblems.keep(uri, parts);
    for (const voice of active) {
        for (const warning of voice.Warnings ?? []) { tellSingerOnce(panel, `${voice.Part}: ${warning}`); }
        if (shownText !== undefined && voiceProblems.set(uri, 'words', sungKey(voice), wordsProblems(voice, shownText))) { problemsChanged = true; }
    }
    if (problemsChanged) { showVoiceProblems(uri); }
    // Who would sing, asked before anything is sung (Cantoria's plan): a line nobody installed
    // can sing never goes to the engine - it is said (tellUnsingable), shown as not sung on its
    // line of the practice menu, and its part's instrument stands in, having no voice.
    const unsingable = await engine.unsingable(active);
    if (!current()) { return; }
    // …and where another line of its part sings, its sections are the instrument's in the next
    // Play's notes (missingVoices): anew each time, a voice that failed before being asked again below.
    missingVoices.set(panel, new Set(unsingable.keys()));
    for (const voice of active) {
        const id = sungKey(voice);
        const slot = `${panelId(panel)}/${id}`;
        const nobody = unsingable.get(id);
        if (nobody !== undefined) {
            mine.delete(id);
            prepareQueue.cancelSlot(slot);
            tellUnsingable(id, nobody);
            void panel.webview.postMessage({ type: 'voiceState', part: id, speed, state: 'failed', detail: nobody });
            continue;
        }
        let key: string;
        try {
            key = await engine.key(voice, speed);
        } catch (e) {
            failVoice(panel, id, speed, engineSaid(e), voice);
            continue;
        }
        if (!current()) { return; }
        if (mine.get(id) === key) {     // the page has this text's voice; a job of an older text can go
            prepareQueue.cancelSlot(slot);
            if (voiceProblems.set(uri, 'engine', id, [])) { showVoiceProblems(uri); }     // …and a failure on another text is not this one's
            continue;
        }
        mine.delete(id);
        const rests = reason === 'edit' && !turned && !engine.has(key);
        if (rests && mode === 'onPlay') {
            void panel.webview.postMessage({ type: 'voiceState', part: id, speed, state: 'waiting', estimateMs: voiceEstimateMs(voice, speed) });
            prepareQueue.cancelSlot(slot);
            continue;
        }
        // The engine may ALREADY be singing this very text for a job that a newer prepare has
        // since replaced (the file opens, and a second prepare follows the first within a second):
        // the replaced job says nothing more to the page, and the one that waits finds the voice
        // sung and never says "singing" either - the microphone pulsed for 24 s with no bar
        // (owner's report 2026-11-13, kaze.lys). So the page is told by the KEY: here if the
        // engine is on it now, and by singingStarted when it begins.
        askVoice(panel, id, key, speed, voiceEstimateMs(voice, speed));
        void panel.webview.postMessage({ type: 'voiceState', part: id, speed, state: rests ? 'waiting' : 'preparing', estimateMs: voiceEstimateMs(voice, speed), key, singing: !rests && engine.isSinging(key) ? true : undefined });
        const busy = prepareQueue.busy(slot);
        prepareQueue.schedule(slot, { panel, voice, speed, mine, key, letGo: new AbortController() }, rests ? PREPARE_IDLE_MS : 0);
        log(`${id}: ${rests ? `waits for the text to rest ${PREPARE_IDLE_MS} ms` : 'goes now'}${busy ? ', after what is being sung lets go' : ''}`);
    }
}

/** The voice each preview's part is waiting for, by the engine's key (askVoice): who to tell when the engine begins on it. */
const askedVoices = new Map<string, { panel: vscode.WebviewPanel; part: string; speed: number; estimateMs: number }[]>();

function askVoice(panel: vscode.WebviewPanel, part: string, key: string, speed: number, estimateMs: number): void {
    forgetAskedVoice(panel, part);
    const asked = askedVoices.get(key) ?? [];
    asked.push({ panel, part, speed, estimateMs });
    askedVoices.set(key, asked);
}

/** The part's voice has landed, failed, or is asked anew: nobody waits for the old key. */
function forgetAskedVoice(panel: vscode.WebviewPanel, part: string): void {
    for (const [key, asked] of askedVoices) {
        const left = asked.filter(a => !(a.panel === panel && a.part === part));
        if (left.length === 0) { askedVoices.delete(key); } else if (left.length !== asked.length) { askedVoices.set(key, left); }
    }
}

/** The engine has begun on `key` (VoiceRenderer.onSinging): every preview waiting for that voice shows its bar. */
function singingStarted(key: string): void {
    for (const a of askedVoices.get(key) ?? []) {
        if (!(sungParts.get(a.panel)?.has(a.part) ?? true)) { continue; }
        try {
            void a.panel.webview.postMessage({ type: 'voiceState', part: a.part, speed: a.speed, state: 'preparing', estimateMs: a.estimateMs, singing: true, key });
        } catch { /* the preview is gone */ }
    }
}

/**
 * What the engine says of the voice of `key` as it sings it (VoiceRenderer.onProgress): to every
 * preview waiting for THAT voice, whichever job asked the engine (owner's decision 2026-11-13: the
 * count went to the asking job's page only, and a job replaced meanwhile kept it to itself, so
 * the bar ran on its estimate). A part that has since asked for another text waits under another
 * key and hears nothing of this one.
 */
function singingProgress(key: string, p: SingProgress): void {
    for (const a of askedVoices.get(key) ?? []) {
        if (!(sungParts.get(a.panel)?.has(a.part) ?? true)) { continue; }
        try {
            void a.panel.webview.postMessage({ type: 'voiceProgress', part: a.part, key, toSing: p.toSing, sung: p.sung, msPerSecond: voiceRenderRate });
        } catch { /* the preview is gone */ }
    }
}

/**
 * One queued voice: rendered by the engine unless rendered before, and handed to the page as WAV
 * bytes (`voice`), with `preparing` on the badge while the engine sings and `failed` when it
 * cannot, or when the engine has said nothing for longer than prepareAllowanceMs. A voice whose preview turned to
 * another document, whose part the score sings no more, or whose part has a newer text waiting
 * in the queue, is dropped on landing.
 */
async function renderVoice(job: PrepareJob): Promise<void> {
    const { panel, voice, speed, mine } = job;
    const id = sungKey(voice);
    const slot = `${panelId(panel)}/${id}`;
    const wanted = () => !job.letGo.signal.aborted && postedVoices.get(panel) === mine && !prepareQueue.hasWaiting(slot)
        && (sungParts.get(panel)?.has(id) ?? true);
    const engine = voiceRenderer;
    if (!engine || !wanted()) { return; }
    let key: string, rendered: Promise<{ wav: string; duration: number; notes?: string[] }>, cached: boolean;
    // What the engine says of this render (owner's request 2026-11-11): the seconds of music it
    // has to sing ANEW - it keeps every phrase it sang, so after an edit that is the phrases
    // changed - and how many are done. The page's bar is counted on these; `msPerSecond` is the
    // pace learnt so far, for the time before the first phrase is in. The key is the job's own,
    // which the page holds its count by.
    let told: SingProgress | undefined;
    // (The page hears it by the voice's key, from singingProgress: a job a newer prepare has
    // replaced goes on being sung for the one that waits, and the bar must still be counted.
    // Here it is kept for the pace learnt below, and for how long the engine may yet take.)
    const onProgress = (p: SingProgress) => { told = p; engineWatch.heard(); };
    try {
        outputChannel.appendLine(`singer: ${clockNow()} ${id}: to the engine`);
        ({ key, rendered, cached } = await engine.render(voice, speed, job.letGo.signal, onProgress));
    } catch (e) {
        if (wanted()) { failVoice(panel, id, speed, engineSaid(e), voice); }
        return;
    }
    // Asked again after the await: the preview may have turned to a file with no lyrics
    // meanwhile, whose empty `voiceParts` had already gone out — a `preparing` landing after it
    // kept the microphone pulsing for ever (owner's report 2026-11-09, session 1160).
    if (!wanted()) { return; }
    if (!cached) { void panel.webview.postMessage({ type: 'voiceState', part: id, speed, state: 'preparing', estimateMs: voiceEstimateMs(voice, speed), singing: true, key: job.key }); }
    const askedAt = Date.now();
    // Given up when the engine has said nothing for as long as what is left of this voice should
    // take (prepareAllowanceMs), the clock set again whenever it is heard of; and when given up,
    // the engine is told to let go of it, or it goes on singing a voice nobody waits for and every
    // voice after waits behind it (owner's report 2026-11-18).
    let gaveUp = false;
    let giveUp: (e: Error) => void = () => undefined;
    const timeout = new Promise<never>((_, reject) => { giveUp = reject; });
    const secondsLeft = () => told ? told.toSing - told.sung : sungSeconds(voice, speed);
    const unwatch = cached ? () => undefined : engineWatch.watch(() => prepareAllowanceMs(secondsLeft(), voiceRenderRate, voiceRenderTimed),
        ms => { gaveUp = true; giveUp(new Error(`the engine gave no answer in ${Math.round(ms / 1000)} s`)); });
    const singing = (async () => {
        try {
            const v = await (cached ? rendered : Promise.race([rendered, timeout]));
            unwatch();
            engineWatch.heard();
            // What the engine had to say of the score: a setting of the singer line the voice
            // does not take is not sung, and the writer is told which - once, where it is seen.
            for (const note of v.notes ?? []) { tellSingerSetting(note); }
            if (!cached) {
                // The engine sings one at a time: this voice's time began when the last one landed.
                const now = Date.now();
                // The pace is learnt on what was SUNG: the engine's own count where it gave one
                // (a render that laid kept phrases and sang two seconds says nothing of its pace
                // over the whole score, and one that sang next to nothing teaches nothing).
                const sungSecondsNow = told ? told.toSing : v.duration;
                if (!told || told.toSing >= 1) {
                    voiceRenderRate = learnRenderRate(voiceRenderRate, now - Math.max(askedAt, voiceLastLanded), sungSecondsNow, voiceRenderTimed);
                    voiceRenderTimed = true;
                }
                voiceLastLanded = now;
            }
            if (!wanted()) { return; }
            const bytes = await fs.promises.readFile(v.wav);
            if (!wanted()) { return; }
            mine.set(id, key);
            // Sung: what was said of the engine's failure on this voice is over.
            const sungUri = voiceDocs.get(panel)?.split('\n')[0];
            if (sungUri && voiceProblems.set(sungUri, 'engine', id, [])) { showVoiceProblems(sungUri); }
            // entries: where the voice may come in if its part is already playing (joinVoice).
            forgetAskedVoice(panel, id);
            void panel.webview.postMessage({ type: 'voice', part: id, key, speed, lead: VOICE_LEAD_S, bytes: new Uint8Array(bytes), entries: voiceEntries(voice.Notes) });
            outputChannel.appendLine(`singer: ${clockNow()} ${id} ${cached ? 'from the cache' : 'sung'} (${v.duration.toFixed(1)} s at speed ${speed})`);
        } catch (e) {
            if (wanted()) { failVoice(panel, id, speed, engineSaid(e), voice); }
            // Said first: a voice let go of is not `wanted`, and its failure would go unsaid.
            if (gaveUp) { job.letGo.abort(); }
        } finally {
            unwatch();
        }
    })();
    if (!cached) { vscode.window.setStatusBarMessage(`Lily#: singing ${voice.Part}…`, singing); }
    await singing;
}

/** How long a candidate's Play waits for a voice the renderer does not hold: the engine's own
 *  store answers within it (a voice sung in an earlier session), a voice to be sung does not. */
const CANDIDATE_VOICE_WAIT_MS = 600;

/**
 * The voices of a text for a candidate panel's Play (AiTransformDeps.voicesFor; owner's request
 * 2026-11-11). A voice is kept by its notes, its words and its singer, so a candidate that left a
 * sung part alone finds the voice the preview had sung — at once, with no work for the engine. A
 * part the candidate changed is sung now, at the score's own speed; its instrument stands in for
 * this Play and `onSung` says when the voice is there. The practice menu's switches are the
 * preview's and are not read: the panel plays the score as written. With no engine, no server or a
 * text that sings nothing, nothing is asked.
 */
export async function candidateVoices(text: string, uri: vscode.Uri, renderName: string | undefined, sounds: CandidateSounds,
    onSung: (voice: CandidateVoice) => void): Promise<CandidateVoices> {
    const notSung: string[] = [];
    const out: CandidateVoices = { voices: [], silenced: [], waiting: [], notSung };
    const client = server();
    if (!client || (sounds.Sung ?? []).length === 0) { return out; }
    const engine = await renderer();
    if (!engine) { return out; }
    const log = (what: string) => outputChannel.appendLine(`singer: ${clockNow()} candidate: ${what}`);
    let sung: { Voices?: SungVoiceDto[] | null; Error?: string | null };
    try {
        sung = await client.sendRequest('lilysharp/sung', { textDocument: { uri: uri.toString() }, text, renderName: renderName ?? null });
    } catch (e) {
        log(`lilysharp/sung failed: ${String(e)}`);
        return out;
    }
    if (sung.Error || !sung.Voices) { log(`no voices (${sung.Error ?? 'none'})`); return out; }
    const silenced = new Set<number>();
    // The voice as the panel takes it: its WAV, the instrument it replaces, and the notes after a
    // rest, where it may come in while its part already plays.
    const handed = async (voice: SungVoiceDto, wav: string): Promise<CandidateVoice> => ({
        part: voice.Part, bytes: new Uint8Array(await fs.promises.readFile(wav)), lead: VOICE_LEAD_S,
        track: candidateSilencedTrack(voice.Part, sounds), entries: voiceEntries(voice.Notes),
    });
    const candidates = candidateSings(sung.Voices, sounds);
    const unsingable = await engine.unsingable(candidates);
    for (const voice of candidates) {
        // Nobody installed can sing it: its instrument plays, and the writer is told as the preview tells it.
        const nobody = unsingable.get(sungKey(voice));
        if (nobody !== undefined) { tellUnsingable(sungKey(voice), nobody); notSung.push(sungKey(voice)); continue; }
        try {
            const { rendered, cached } = await engine.render(voice, 1);
            let late: ReturnType<typeof setTimeout> | undefined;
            const v = cached ? await rendered : await Promise.race([
                rendered,
                new Promise<undefined>(resolve => { late = setTimeout(() => resolve(undefined), CANDIDATE_VOICE_WAIT_MS); }),
            ]);
            if (late !== undefined) { clearTimeout(late); }
            if (!v) {
                out.waiting.push(voice.Part);
                log(`${voice.Part}: to the engine; its instrument plays this time`);
                rendered.then(async sungNow => { log(`${voice.Part}: sung (${sungNow.duration.toFixed(1)} s)`); onSung(await handed(voice, sungNow.wav)); })
                    .catch(e => log(`${voice.Part} was not sung — ${String(e)}`));
                continue;
            }
            const there = await handed(voice, v.wav);
            out.voices.push(there);
            if (there.track >= 0) { silenced.add(there.track); }
            log(`${voice.Part}: ${cached ? 'the voice the preview had' : 'from the engine\'s store'} (${v.duration.toFixed(1)} s)`);
        } catch (e) {
            log(`${voice.Part} was not sung — ${String(e)}; its instrument plays`);
        }
    }
    out.silenced = [...silenced];
    return out;
}
