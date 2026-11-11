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

// Lily# - AI collaborative editing (docs/ai-collab-design)
//
// The select-and-prompt inline transform loop (M1 vertical slice):
//   selection -> prompt -> build context (selection + grammar + resolved facts)
//   -> vscode.lm -> validate-and-self-repair (checkCandidate) -> render candidate
//   score (renderText) -> decide on the score (accept / iterate / reject)
//   -> apply as one WorkspaceEdit with a version/range guard.
//
// The two Lily#-specific moves that WYSIWYG AI cannot make:
//   (a) validate-and-self-repair BEFORE showing — a broken candidate never
//       reaches the user (the fast in-process compiler checks each candidate).
//   (b) decide-on-the-score — accept/reject is judged on the rendered notation,
//       not a text diff.
//
// Everything here is non-destructive until the user accepts: the real file is
// untouched (candidates are compiled/rendered offscreen).

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import * as os from 'os';
import type { LanguageClient } from 'vscode-languageclient/node';
import { ChatClient, ChatMessage, resolveChatClient } from './modelClient';
import {
    CandidateEdit as CoreCandidateEdit, ChangedSpan, MarkInSelection, MarkReadings, SEL_CLOSE, SEL_OPEN, cleanCandidate,
    TextChange, carryOffset, documentBudget, documentContext, estimateTokens, isProblem, marksContext, octaveOutliers, splitNote,
    startTimeAt, toCandidateEdit, wholeFileEdit, carrySelection, changedWords, wholeTextMarkup, titleOf, isPlaceholderTitle,
    cleanTitle, withTitle, asksForOneStaff, partCount, rangeFixOf, aiFailure, warningTodoLines, withLyricsRows, promptFits, compositionHint, hintTonic, drawnKeyNote, copilotModelOf, withModelNamed, withLeadingComments, isEmptyScore, newPieceOverScore, guidePages, guideContents, CandidateSynth, playsItself, hasSynth, headerLanguageNote, diagLines,
    lastUserMessageAt, heldClashesOf, noTuneOf, CLASHES_TURNED_DOWN, singersInOneLineOf, unfingerableOf, UNFINGERABLE_TURNED_DOWN, REVIEW_READING,
    applyDraft, draftSummary, DraftInput, FROM_DRAFT_NOTE,
    applyScoreEdits, ScoreEdit, scorePartView, scorePages,
} from './aiTransformCore';
import { CANTORIA_EXTENSION_ID, cantoriaApiOf, voiceListText } from './singerCore';
import { withTodoMark } from './omrAiFixCore';
import { markFactsOf, sideFileOf } from './omrScanCore';
import { textFontFaceCss, textFontsRoot } from './scoreFonts';
import { PLAIN_TONES_JS } from './plainTones';
export { cleanCandidate }; // aiComplete reads it from here

// ---- Dependencies wired in from extension.ts (keeps the LSP client global there) ----
export interface AiTransformDeps {
    extensionUri: vscode.Uri;
    /** The LanguageClient, or undefined if not started. */
    getClient: () => LanguageClient | undefined;
    /** True once the client has started and is ready to serve requests. */
    isReady: () => boolean;
    /** Starts the client if nothing has yet (no .lys open — Compose with AI from File > New File…)
     *  and waits for it. */
    ensureReady?: () => Promise<void>;
    log: (msg: string) => void;
    /** Plays a note list (lilysharp/playback's) on the open preview of `uri` — its synth, its
     *  sound font, its MIDI output; false when no preview of it is open. */
    playOnPreview?: (uri: vscode.Uri, notes: unknown[]) => boolean;
    /** The editor column the preview of `uri` stands in, or undefined with no preview open. */
    previewColumnOf?: (uri: vscode.Uri) => vscode.ViewColumn | undefined;
    /** Where the synth's files are read from, for a candidate panel that plays itself. */
    synthRoots?: () => vscode.Uri[];
    /** The synth's files as `webview` reaches them (its bundle, its worklet processor, the sound
     *  font — '' for none), and the MIDI output the sounds go to ('' for the synth). */
    synthFor?: (webview: vscode.Webview) => CandidateSynth;
    /** Plays a note list from its start on the MIDI output `port` (the server's): whether it
     *  started, the wall-clock time of its zero, where it ends in seconds, and why not. */
    midiPlay?: (port: string, notes: unknown[]) => Promise<{ started: boolean; t0Ms: number; end: number; error: string | null }>;
    /** Stops what the MIDI output plays. */
    midiStop?: () => void;
    /** Stops what the preview of `uri` is playing. */
    stopOnPreview?: (uri: vscode.Uri) => void;
    /** The score the preview of `uri` shows (its picker), or undefined for the first. */
    renderNameOf?: (uri: vscode.Uri) => string | undefined;
    /** Shows the extension's log (the Output panel's Lily# Extension). */
    showLog?: () => void;
    /** The lysc beside the bundled server, if the extension carries one. */
    lysc?: () => string | undefined;
}

/** What a candidate's Play sounds (owner's request 2026-10-25: hear it before accepting): the
 *  document, the text as it stands, and the text with the candidate in. */
interface Audible { uri: vscode.Uri; before: string; after: string }

// The round under review — what its panel's Play plays.
let audible: Audible | undefined;

// What Ask AI and Compose with AI send the model and what it answers, word for word (owner's
// request 2026-10-27: a poor composed piece could not be traced — the log held one line a step).
// The grammar is the same in every system prompt and is not written out.
let exchangeChannel: vscode.OutputChannel | undefined;

/** Writes the messages from `from` on to the exchange log; returns how many are written now. */
function logExchange(chat: ChatClient, grammar: string, messages: ChatMessage[], from: number): number {
    exchangeChannel ??= vscode.window.createOutputChannel('Lily# AI Exchange');
    const stamp = new Date().toLocaleTimeString();
    for (const m of messages.slice(from)) {
        if (m.role === 'assistant') continue; // its reply, logged as it came (logReply)
        let text = m.content.split(grammar).join(`[the Lily# grammar, docs/GRAMMAR_FOR_LLM.md — ${grammar.length} characters]`);
        if (cachedComposeGuide) {
            text = text.split(cachedComposeGuide).join(`[the composing guide, docs/COMPOSE_FOR_LLM.md — ${cachedComposeGuide.length} characters]`);
        }
        // The model the request goes to, as VS Code lists it (owner's question 2026-10-27: was the
        // model the notification named the one that answered?). Which model the vendor ran is not
        // returned to an extension.
        exchangeChannel.appendLine(`===== ${stamp} ${m.role}${m.role === 'system' ? ` (to ${chat.label}${chat.modelDetail ? `: ${chat.modelDetail}` : ''})` : ''} =====`);
        exchangeChannel.appendLine(text);
    }
    return messages.length;
}

/**
 * False, having said so with the way to another model, when the instructions do not fit the model
 * (aiTransformCore.promptFits): the request is not sent (owner's request 2026-10-29).
 */
function tellsTooSmall(chat: ChatClient, systemTokens: number, what: 'compose with' | 'Ask'): boolean {
    if (promptFits(chat.maxInputTokens, systemTokens)) {
        return true;
    }
    const pick = 'Select AI Model…';
    void vscode.window.showWarningMessage(
        `Lily#: ${chat.label} reads at most ${chat.maxInputTokens!.toLocaleString('en-US')} tokens, and the instructions to `
        + `${what} AI — the Lily# grammar with them — are about ${systemTokens.toLocaleString('en-US')}: it cannot take them. `
        + 'Choose a model that reads more.', pick)
        .then(c => { if (c === pick) { void vscode.commands.executeCommand('lilysharp.selectAiModel'); } });
    return false;
}

function logReply(raw: string): void {
    exchangeChannel?.appendLine(`===== ${new Date().toLocaleTimeString()} reply =====`);
    exchangeChannel?.appendLine(raw);
}

// ---- Server DTO mirrors (see LspProtocolDtos.cs) ----
interface CandidateDiagnostic {
    Line: number; Char: number; Offset: number; Length: number;
    Severity: string; Message: string; Code: string | null;
}
interface CheckCandidateResponse { HasErrors: boolean; Diagnostics: CandidateDiagnostic[]; }
interface SvgResponse { Svg: string | null; Error: string | null; }
interface ResolvedPitchFact { Offset: number; Written: string; Resolved: string; }
interface FactsForRangeResponse { Pitches: ResolvedPitchFact[]; Error: string | null; }

// Fallback used only if the bundled grammar file is missing — the essential
// constraints so the model still produces Lily# (not LilyPond).
const GRAMMAR_FALLBACK = `Lily# is a text music-notation language. It is NOT LilyPond.
- Pitches: c d e f g a b, sharp = "is" (cis), flat = "es" (ees/es). Octave marks: ' (up), , (down). Default octave is relative to the previous note (nearest); absolute mode is enabled by "octave absolute".
- Durations follow the note: c4 = quarter, c8 = eighth, c2 = half, c1 = whole. Dotted: c4.
- Every measure ends with a bar line "|". One statement per line.
- Annotations use @name (e.g. @staccato, @accent, @f, @p, @cresc). Backslash is reserved for tablature only.
- NEVER emit LilyPond-only constructs: \\relative, \\new Staff, \\repeat volta, \\version, << ... \\\\ ... >>.
- Everything is case-sensitive.`;

const MAX_REPAIR_ATTEMPTS = 2;

// Context-independent example prompts, shown one at a time in the input box and
// rotated each invocation so the hint stays fresh (and teaches what's possible).
// Kept generic on purpose — they don't assume a particular part, key, or texture.
const PROMPT_EXAMPLES = [
    'transpose up a perfect fourth',
    'harmonize a third above',
    'add a crescendo',
    'turn it into triplets',
    'double the note durations',
    'add staccato to every note',
    'shift up an octave',
    'simplify to quarter notes',
    'reverse the order of the notes',
    'add a trill to the last note',
];
let promptExampleIndex = 0;

function nextPlaceholder(): string {
    const example = PROMPT_EXAMPLES[promptExampleIndex % PROMPT_EXAMPLES.length];
    promptExampleIndex++;
    return `e.g. ${example}`;
}

let cachedGrammar: string | undefined;
let candidatePanel: vscode.WebviewPanel | undefined;
// The resolver for the review round currently awaiting a decision. The panel's
// message pump (registered once, at creation) routes Accept/Iterate/Reject here.
let pendingDecision: ((d: Decision) => void) | undefined;

/** Registers the `lilysharp.aiTransform` command. */
export function registerAiTransform(context: vscode.ExtensionContext, deps: AiTransformDeps): void {
    context.subscriptions.push(
        // `selected` names what the preview handed over ("3 bars selected"), for the input box.
        vscode.commands.registerCommand('lilysharp.aiTransform', (opts?: { whole?: boolean; selected?: string }) =>
            runAiTransform(deps, opts?.whole === true, opts?.selected)),
        // Composing is Copilot's chat's alone (owner's decision 2026-11-07: two composers, "Compose
        // with AI…" and "Compose with AI in Chat…", were hard to tell apart; the chat's, where the
        // model is picked and its thinking seen, stays).
        vscode.commands.registerCommand('lilysharp.composeWithAiInChat', () => runComposeInChat(deps)),
    );
    registerProposeScoreTool(context, deps);
    context.subscriptions.push({
        dispose: () => {
            candidatePanel?.dispose();
        }
    });
    // Saving an untitled score while its candidate is under review closes the untitled document
    // and opens the file (owner's report 2026-10-25: Accept then found no document). They are
    // paired by recency, as the preview pairs them — content cannot, a format-on-save rewrites it.
    let closedUntitled: { uri: string; at: number } | undefined;
    const adopt = (doc: vscode.TextDocument | undefined) => {
        if (!doc || doc.uri.scheme !== 'file' || !closedUntitled || Date.now() - closedUntitled.at > 4000) { return; }
        movedUris.set(closedUntitled.uri, doc.uri);
        closedUntitled = undefined;
    };
    context.subscriptions.push(
        vscode.workspace.onDidCloseTextDocument(d => {
            if (d.uri.scheme === 'untitled') { closedUntitled = { uri: d.uri.toString(), at: Date.now() }; }
        }),
        // VS Code gives an untitled name out again ("Untitled-1"): a new document of a name once
        // saved is not the old one, and its old move is forgotten (owner's report 2026-10-27: Accept
        // on an unsaved score said "the document was closed", the name pointing at an old file).
        vscode.workspace.onDidOpenTextDocument(d => {
            if (d.uri.scheme === 'untitled') { movedUris.delete(d.uri.toString()); }
        }),
        vscode.window.onDidChangeActiveTextEditor(e => adopt(e?.document)),
        vscode.workspace.onDidSaveTextDocument(adopt),
        // A score renamed or moved in VS Code goes on as itself: the chat that asked for its change
        // under the old name has it changed where it now is (owner's request 2026-10-29: renamed while
        // the chat worked, the score was taken for gone, and the change written as a new piece).
        vscode.workspace.onDidRenameFiles(e => {
            for (const f of e.files) { movedUris.set(f.oldUri.toString(), f.newUri); }
        }),
    );
}

/** Where each saved untitled document went, and each score renamed or moved (registerAiTransform). */
const movedUris = new Map<string, vscode.Uri>();

/** The document a snapshot's URI names now: itself while it is open (an untitled score not
 *  saved), else the file an untitled one was saved as. */
function currentUri(uri: vscode.Uri): vscode.Uri {
    if (vscode.workspace.textDocuments.some(d => d.uri.toString() === uri.toString())) {
        return uri;
    }
    for (let moved = movedUris.get(uri.toString()); moved; moved = movedUris.get(uri.toString())) {
        uri = moved;
        if (vscode.workspace.textDocuments.some(d => d.uri.toString() === uri.toString())) {
            return uri;
        }
    }
    return uri;
}

/** What Copilot's chat hands the proposal tool. */
interface ProposeScoreInput {
    /** The .lys file, its absolute path. */
    file: string;
    /** The whole new text of the file — or none, with `draft`. */
    text?: string;
    /** A sentence or two to the musician about the change. */
    note?: string;
    /** Propose the piece written into the file's draft (lilysharp_draftScore), not a text given here. */
    draft?: boolean;
    /** Changes to the score as it stands, in place of its whole text: each `old`, as
     *  lilysharp_readScore shows it, becomes `new` (owner's request 2026-11-11). */
    edits?: ScoreEdit[];
    /** With `edits`: the part they keep to — its own block, its music in each section, the phrases it calls. */
    part?: string;
    /** Not the chat's to give: set by lilysharp_writePart, whose new warnings go back once, unseen. */
    sendBackWarnings?: boolean;
}

/** What Copilot's chat hands the tool that writes music by place. */
interface WritePartInput {
    /** The .lys file, its absolute path. */
    file: string;
    /** Music for a part in a section: all of it, or `bars` of it; `lyrics` its words there. */
    write?: { part?: string; section?: string; bars?: string; music?: string; lyrics?: string }[];
    /** Parts the score does not have yet; their music stands in `write`. */
    newParts?: { part?: string; title?: string; short?: string; body?: string; after?: string }[];
    /** A sentence or two to the musician about the change. */
    note?: string;
}

/** Added to what a write by place says, where it asks for the whole text again. */
const FROM_WRITE_NOTE = '\n\nThis was written by place. Where this says to call lilysharp_proposeScore again with the whole '
    + 'text, call lilysharp_writePart again instead — all of it, corrected: the one turned down changed nothing.';

/** The warnings a write by place has been sent back for, by file: the same ones again are shown. */
const writeWarnedFor = new Set<string>();

/**
 * Music written into a score by place (owner's proposal 2026-11-11; lilysharp/writePart): a
 * part's section, some of its bars, its words, a new part — nothing of the old text copied. All
 * of it is placed or every problem is said; the text then goes on as a proposal's whole text
 * does (it compiles, its bars add up, its notes can be played), and a warning it brings goes
 * back once, unseen, before the musician is shown the change with it.
 */
async function writePart(deps: AiTransformDeps, input: WritePartInput | undefined, token: vscode.CancellationToken): Promise<string> {
    const writes = Array.isArray(input?.write) ? input.write : [];
    const fresh = Array.isArray(input?.newParts) ? input.newParts : [];
    if (!input || typeof input.file !== 'string' || writes.length + fresh.length === 0) {
        return 'Nothing shown: give `file` (the .lys file\'s absolute path) and `write` (a part\'s music in a section), or `newParts`.';
    }
    await deps.ensureReady?.();
    const client = deps.getClient();
    if (!client || !deps.isReady()) {
        return 'Nothing shown: the Lily# language server is not running.';
    }
    const doc = await scoreDocument(input.file);
    if (!doc) {
        return `Nothing shown: no score ${input.file} is open in the editor or on the disk to write into. A new piece is given whole (lilysharp_proposeScore).`;
    }
    const standing = doc.getText();
    let placed: { Text?: string | null; Problems?: string[]; Done?: string[] };
    try {
        placed = await client.sendRequest('lilysharp/writePart', {
            Text: await chatAbsolute(deps, standing) ?? standing,
            Write: writes.map(w => ({ Part: w?.part, Section: w?.section, Bars: w?.bars, Music: w?.music, Lyrics: w?.lyrics })),
            NewParts: fresh.map(p => ({ Part: p?.part, Title: p?.title, Short: p?.short, Body: p?.body, After: p?.after })),
        });
    } catch (e) {
        return `Nothing shown: the Lily# language server did not answer (${e instanceof Error ? e.message : String(e)}).`;
    }
    if (typeof placed.Text !== 'string') {
        deps.log(`AI write by place (chat): turned down — ${(placed.Problems ?? []).length} problem(s).`);
        return 'Nothing shown, and nothing changed:\n' + (placed.Problems ?? ['the music could not be placed']).map(p => `- ${p}`).join('\n')
            + '\nMend these and call lilysharp_writePart again with all of it.';
    }
    deps.log(`AI write by place (chat): ${(placed.Done ?? []).join(' / ')}`);
    const said = await proposeScore(deps, { file: input.file, text: placed.Text, note: input.note, sendBackWarnings: true }, token);
    const where = (placed.Done ?? []).map(d => `- ${d}`).join('\n');
    return /^(Not shown|Nothing shown|Not written)/.test(said)
        ? said + FROM_WRITE_NOTE
        : `${said}\nWhat was written, by place:\n${where}`;
}

/** Added to what proposing by edits says, where it asks for the whole text again. */
const FROM_EDITS_NOTE = '\n\nThis was proposed as edits. Where this says to call lilysharp_proposeScore again with the whole '
    + 'text, send `edits` again instead — all of them, corrected: they apply to the score as it stands, which the '
    + 'refused proposal did not change.';

/** Each new piece's draft, by its file (draftKey): written a part at a time with lilysharp_draftScore,
 *  never shown nor compiled, until lilysharp_proposeScore proposes it with `draft: true`. Kept after,
 *  for the chat to mend and propose again. */
const drafts = new Map<string, string>();
const draftKey = (file: string) => path.resolve(file).toLowerCase();

/** One call of the draft tool: the draft as it stands after it, said to the chat, or why nothing changed. */
function draftScore(deps: AiTransformDeps, input: DraftInput | undefined): string {
    if (!input || typeof input.file !== 'string' || !path.isAbsolute(input.file)) {
        return 'Nothing drafted: give `file`, the new .lys file\'s absolute path.';
    }
    const key = draftKey(input.file);
    const result = applyDraft(drafts.get(key), input);
    if ('error' in result) {
        return result.error;
    }
    drafts.set(key, result.text);
    deps.log(`AI proposal (chat): draft of ${path.basename(input.file)} — ${result.text.length} characters.`);
    // Edits alone are the mending of a proposal sent back: its next step is proposing it again.
    const mended = Array.isArray(input.edits) && input.edits.length > 0 && !(typeof input.text === 'string' && input.text.length > 0);
    return draftSummary(path.basename(input.file), result.text, mended);
}

/**
 * The tool Copilot's chat proposes a score with (owner's request 2026-10-29: the chat's thinking in
 * view, and the decision on the score as Ask AI makes it): Lily# compiles the proposal — one that
 * does not compile goes back with its errors, never shown — then shows it beside the file as it
 * stands in the AI candidate panel, to play and to Accept, Ask Again or Reject, and tells the chat
 * what the musician decided; Accept writes the file (one Ctrl+Z). The chat is asked to propose this
 * way, not to edit a .lys itself (the instructions file, Compose with AI in Chat); it cannot be
 * made to.
 */
function registerProposeScoreTool(context: vscode.ExtensionContext, deps: AiTransformDeps): void {
    const lm = vscode.lm as unknown as { registerTool?: typeof vscode.lm.registerTool };
    if (typeof lm.registerTool !== 'function') {
        return;   // a VS Code before the tools API
    }
    context.subscriptions.push(lm.registerTool<ProposeScoreInput>('lilysharp_proposeScore', {
        prepareInvocation: options => ({
            invocationMessage: `Showing the proposed score of ${path.basename(options.input?.file ?? 'the file')}…`,
        }),
        invoke: async (options, token) => {
            const said = await proposeScore(deps, options.input, token);
            // From the draft, mended in the draft: not the whole piece written again in one reply.
            const fromDraft = options.input?.draft === true && !said.startsWith('Nothing shown');
            // …and from edits, mended as edits.
            const fromEdits = !fromDraft && typeof options.input?.text !== 'string' && Array.isArray(options.input?.edits)
                && said.startsWith('Not shown');
            return new vscode.LanguageModelToolResult([new vscode.LanguageModelTextPart(
                fromDraft ? said + FROM_DRAFT_NOTE : fromEdits ? said + FROM_EDITS_NOTE : said)]);
        },
    }));
    // A new piece written a part at a time (owner's report 2026-11-08: a choir piece written in one
    // reply ran past ten minutes and was cut off, three times over, nothing kept).
    context.subscriptions.push(lm.registerTool<DraftInput>('lilysharp_draftScore', {
        prepareInvocation: options => ({
            invocationMessage: `Writing ${path.basename(options.input?.file ?? 'the piece')}'s draft…`,
        }),
        invoke: async options => new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(draftScore(deps, options.input)),
        ]),
    }));
    context.subscriptions.push(lm.registerTool<ReadScoreInput>('lilysharp_readScore', {
        prepareInvocation: options => ({
            invocationMessage: `Reading ${path.basename(options.input?.file ?? 'the score')} as it stands in the editor…`,
        }),
        invoke: async options => new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(await readScore(deps, options.input)),
        ]),
    }));
    context.subscriptions.push(lm.registerTool<ReviewScoreInput>('lilysharp_reviewScore', {
        prepareInvocation: options => ({
            invocationMessage: `Reviewing ${options.input?.file ? path.basename(options.input.file) : 'the score'}…`,
        }),
        invoke: async options => new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(await reviewScore(deps, options.input)),
        ]),
    }));
    // Music written by place (owner's proposal 2026-11-11): a part's section, some bars, a new part.
    context.subscriptions.push(lm.registerTool<WritePartInput>('lilysharp_writePart', {
        prepareInvocation: options => ({
            invocationMessage: `Writing ${[...new Set((options.input?.write ?? []).map(w => w?.part).filter(Boolean))].join(', ') || 'a part'} into ${path.basename(options.input?.file ?? 'the score')}…`,
        }),
        invoke: async (options, token) => new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(await writePart(deps, options.input, token)),
        ]),
    }));
    // The harmony of a score, read once (owner's request 2026-11-11: asked to revoice a guitar, the chat
    // named every chord from the notes by itself).
    context.subscriptions.push(lm.registerTool<ReadChordsInput>('lilysharp_readChords', {
        prepareInvocation: options => ({
            invocationMessage: `Reading the chords of ${options.input?.file ? path.basename(options.input.file) : 'the score'}…`,
        }),
        invoke: async options => new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(await readChords(deps, options.input)),
        ]),
    }));
    // The voicings a player can hold (owner's request 2026-10-30: a composed guitar part wrote a chord
    // no hand could hold — a tool to ask before writing one, rather than a rule refusing it after).
    context.subscriptions.push(lm.registerTool<ChordVoicingsInput>('lilysharp_chordVoicings', {
        prepareInvocation: options => ({
            invocationMessage: `Finding ${Array.isArray(options.input?.chords) && options.input.chords.length > 0
                ? `${options.input.chords.length} chords'` : `${options.input?.chord ?? 'the chord'}'s`} voicings on the ${options.input?.instrument ?? 'guitar'}…`,
        }),
        invoke: async options => new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(await chordVoicings(deps, options.input)),
        ]),
    }));
    // The voices a score can name here (owner's request 2026-11-09, session 1202).
    context.subscriptions.push(lm.registerTool<ListVoicesInput>('lilysharp_listVoices', {
        prepareInvocation: () => ({ invocationMessage: 'Listing the singing voices…' }),
        invoke: async options => new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(await listVoices(options.input)),
        ]),
    }));
    context.subscriptions.push(lm.registerTool<ReadScoreInput>('lilysharp_checkName', {
        prepareInvocation: options => ({
            invocationMessage: `Checking that ${path.basename(options.input?.file ?? 'the name')} is free…`,
        }),
        invoke: async options => new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(await checkName(options.input)),
        ]),
    }));
    context.subscriptions.push(lm.registerTool<ReadGuideInput>('lilysharp_readGuide', {
        prepareInvocation: options => ({
            invocationMessage: options.input?.topic === 'composing' ? 'Reading the Lily# composing guide…'
                : options.input?.topic === 'learn' ? 'Reading how to teach Lily#…' : 'Reading the Lily# grammar…',
        }),
        invoke: async options => new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(await readGuide(deps, options.input)),
        ]),
    }));
}

/** What Copilot's chat hands the voice list. */
interface ListVoicesInput { provider?: string; gender?: string; language?: string }

/** The voices the Cantoria extension lists, narrowed as asked. */
async function listVoices(input: ListVoicesInput | undefined): Promise<string> {
    const extension = vscode.extensions.getExtension(CANTORIA_EXTENSION_ID);
    if (!extension) { return `No voice can be listed: the Cantoria extension (${CANTORIA_EXTENSION_ID}), which sings, is not installed. A score can still be written without a singer.`; }
    try {
        const api = cantoriaApiOf(extension.isActive ? extension.exports : await extension.activate());
        if (!api?.voices) { return 'No voice can be listed: the installed Cantoria extension does not list its voices.'; }
        return voiceListText(await api.voices(), input ?? {});
    } catch (e) {
        return `No voice can be listed: ${e instanceof Error ? e.message : String(e)}`;
    }
}

/** What Copilot's chat hands the guide tool. */
interface ReadGuideInput {
    /** 'grammar' — how Lily# is written; 'composing' — how to compose a new piece in it, with the
     *  composing guide; 'start' — how a new piece begins and its starting point alone; 'learn' — the
     *  path for teaching Lily# to someone asking how to write it. */
    topic?: string;
    /** Which part, from 1: a guide comes in parts small enough to be read whole (guidePages). */
    part?: number;
}

/** A guide part's most: under the 8 KB past which Copilot's agent keeps a tool's answer in a file. */
const GUIDE_PAGE_BYTES = 7000;
/** Room for the grammar's contents, before its first part. */
const GUIDE_CONTENTS_BYTES = 1200;
/** Room for how a new piece begins, before the composing guide's first part. */
const GUIDE_START_BYTES = 2500;

/**
 * The Lily# instructions files Copilot is given with a .lys in hand (esbuild.js writes them), for a
 * chat with none (owner's request 2026-10-29: a piece asked for in the chat with no .lys open was
 * written without them): the grammar, or how a new piece begins — its first lines, the steps to
 * propose it (newPieceSteps) and a starting point drawn at random — followed by the composing
 * guide; or, for a chat that has the guide already (Compose with AI in Chat's), how it begins alone.
 * The file is the bundled one the instructions are made of where the built one is not.
 */
async function readGuide(deps: AiTransformDeps, input: ReadGuideInput | undefined): Promise<string> {
    const topic = input?.topic;
    const built = (name: string) => {
        try {
            // The instructions file without its front matter (name, description, applyTo).
            return fs.readFileSync(vscode.Uri.joinPath(deps.extensionUri, 'out', name).fsPath, 'utf8')
                .replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n/, '');
        } catch {
            return undefined;
        }
    };
    const part = Math.max(1, Math.floor(Number(input?.part) || 1));
    // One part of a guide, saying which comes next (owner's report 2026-10-29: whole, it went to a
    // temp file, read back with PowerShell — "Run in terminal?" mid-compose).
    const paged = (name: string, pages: string[], before = '') => {
        if (part > pages.length) {
            return `The ${name} has ${pages.length} part(s); there is no part ${part}.`;
        }
        const body = before + pages[part - 1];
        return pages.length === 1 ? body
            : `${body}\n\n[Part ${part} of ${pages.length} of the ${name}. `
                + (part < pages.length
                    ? `Read part ${part + 1} next: lilysharp_readGuide, topic "${topic === 'start' || topic === 'composing' ? 'composing' : topic === 'learn' ? 'learn' : 'grammar'}", part ${part + 1} — every part before you ${topic === 'learn' ? 'teach' : topic === 'composing' || topic === 'start' ? 'write' : 'write a new piece; to change a score that is there, the parts its contents (part 1) name for what you write'}.]`
                    : 'The end.]');
    };
    // Owner's request 2026-11-02: asked how to write Lily#, the chat had a 100 KB grammar and a guide to composing.
    if (topic === 'learn') {
        const teaching = built('LEARN_FOR_LLM.md');
        return teaching ? paged('teaching path', guidePages(teaching, GUIDE_PAGE_BYTES)) : 'The teaching path is not in this build: read topic "grammar".';
    }
    if (topic !== 'composing' && topic !== 'start') {
        const pages = guidePages(built('lilysharp.instructions.md') ?? await loadGrammar(deps), GUIDE_PAGE_BYTES, GUIDE_PAGE_BYTES - GUIDE_CONTENTS_BYTES);
        // What each part holds, said before the first: a change to a score needs a part or two, not all.
        return paged('Lily# grammar', pages, part === 1
            ? 'The Lily# grammar, in parts (lilysharp_readGuide, topic "grammar", part N). To write a NEW piece, read them all. To change a score '
                + 'that is there, the score itself shows how it is written (lilysharp_readScore): read only the parts that hold what you do not find in it.\n'
                + guideContents(pages) + '\n\n'
            : '');
    }
    // Its first part has room for how a new piece begins, which goes before it.
    const guide = async () => guidePages(built('lilysharp-compose.instructions.md') ?? await loadComposeGuide(deps),
        GUIDE_PAGE_BYTES, GUIDE_PAGE_BYTES - GUIDE_START_BYTES);
    if (topic === 'composing' && part > 1) {
        return paged('composing guide', await guide());
    }
    // One draw a piece, whatever times the guide is read before it is proposed (owner's trial
    // 2026-10-30: read twice, the guide drew F and then E♭, and the piece came in E minor).
    const hint = drawnHint ??= compositionHint();
    drawnTonic = hintTonic(hint);
    deps.log(`AI transform (chat, guide): ${hint}`);
    // The header Compose with AI in Chat writes, the request in it the musician's (owner's request
    // 2026-10-29: a piece composed this way began at `octave absolute`, saying nothing of itself).
    // Named after its title (owner's request 2026-10-29: "e.g. Composed.lys" here was taken for the
    // name, and the musician renamed each piece by hand), and a name not taken: a score there would
    // be proposed to as a change. Always a NEW file (owner's request 2026-11-07: asked with a score
    // open, the chat took that score for "the file you were given" and proposed to overwrite it).
    const start = 'Compose the new piece in Lily# as a NEW .lys file. Never write it into a score that is open, attached or '
        + 'in front, even when the chat was given that file: that score is the musician\'s, and a new piece there would '
        + 'replace it. Only an empty .lys, or a file the musician asks in so many words to write the piece into, takes it. '
        + 'Put the new file in the folder you were given, else the folder of the .lys in front, else the workspace '
        + 'folder, its absolute path; it is not on the disk until the musician saves it. '
        + 'Name a new file after the piece\'s title, in letters, digits and hyphens (a title in another script '
        + 'romanized: 風のリボン → Kaze-no-Ribbon.lys), never a placeholder (Composed, Untitled), and a name no file in '
        + 'that folder has yet — check it with the lilysharp_checkName tool (#checkName). Its first '
        + 'lines are these, the request as the musician put it and what you made of it, each on one line, in the '
        + 'language of the request:\n'
        + newPieceHeader('<the request>', '<the piece you composed, in a sentence or two>', '\n').trimEnd() + '\n'
        + 'Read the Lily# grammar and the composing guide first (lilysharp_readGuide) unless you have them.\n'
        + newPieceSteps(deps.lysc?.())
        + `${hint}\n`;
    return topic === 'start' ? start : paged('composing guide', await guide(), start + '\n');
}

/** The first lines of a piece composed in the chat — what it is, what was asked and what the AI
 *  made of it (its "The AI:" line as Compose with AI writes it; owner's request 2026-10-29), then
 *  `octave absolute` and `pitch concert` — the header the composing guide asks for. */
function newPieceHeader(asked: string, note: string, eol: string): string {
    return [
        `// Composed in Copilot's chat, ${new Date().toISOString().slice(0, 10)}`,
        `// Asked: ${asked}`,
        `// The AI: ${note}`,
        '',
        'octave absolute',
        'pitch concert',
        '',
    ].join(eol);
}

/**
 * How a new piece is proposed, said alike by Compose with AI in Chat's request and the composing
 * guide (owner's request 2026-10-29, the two kept one): through Lily#'s tool, written straight into
 * the file only where the tool is not there; in sounding pitches. The tool compiles the piece and
 * sends back its errors and warnings, so the chat runs no compiler of its own (owner's request
 * 2026-10-29: its check on a temp copy asked "Run terminal?" in the middle of composing) — the
 * bundled one is named only for a file written without the tool.
 */
function newPieceSteps(lysc: string | undefined): string {
    // The plan before the notes (owner's decision 2026-11-07: Compose with AI's prompt asked for it,
    // the chat's composing never did, and the guide spoke of "the plan" to a chat that wrote none).
    return 'Before any note, write in your reply the plan the composing guide asks for — its "Plan before the first note", '
        + 'every heading filled in — then write the piece to it.\n'
        // Owner's report 2026-11-08: a choir piece, plan and score in one reply, ran past ten minutes and
        // was cut off three times, nothing kept. A reply is kept short: the plan in one, the notes in the
        // draft a part at a time, the proposal last.
        // Owner's trial 2026-11-08: drafted so, the choir piece's writing took two minutes — but before its
        // plan the model thought 77,000 tokens for ten minutes, the whole piece composed in its head, and
        // came within seconds of the cut. The thinking is a reply's too.
        // Owner's request 2026-11-08: said plainly — the reason, and a limit to keep to.
        + 'Answer each reply within five minutes, thinking included (roughly 30,000 tokens of thinking): a long think '
        + 'can be cut off by the connection, and the whole reply is lost. So think out the plan, not the notes, '
        + 'and think each section\'s notes as you draft that section. Write in short steps, each its own reply — the answer '
        + 'to each tool call says the step after it: first the piece\'s title and the lilysharp_checkName call for its file, '
        + 'nothing more in that reply; then the plan, and the draft begun with the lilysharp_draftScore '
        + 'tool (#draftScore) — the header and the parts; then each section (a long one in halves), '
        + 'its section notes written in your reply just before it as the composing guide says, '
        + 'then the form and the score — `text` added at the draft\'s end each time; nothing of it is shown until you propose it.\n'
        + 'Propose it, whole, with the lilysharp_proposeScore tool (#proposeScore), giving that path and `draft: true`: it compiles the '
        + 'piece, sends back its errors and warnings to fix, and writes it into the document — do not run a compiler '
        + 'yourself, nor anything else in a terminal: Lily#\'s tools and your file tools do all a new piece needs. If that tool is not available, write the piece into the file instead'
        + (lysc ? `, and check it with \`& "${lysc}" check <file>\`` : '') + '.\n'
        // Owner's report 2026-11-01: the score was handed to invoke_canvas_action, and the musician
        // asked to allow it.
        + 'Call that tool directly, by its own name, never through a canvas action or another tool.\n'
        + 'Keep `octave absolute` and `pitch concert`, and write every pitch as the pitch it sounds.\n';
}

/** Writes `text` as the whole file and opens it, not saved: the musician saves it when they want it
 *  kept (owner's requests 2026-10-29), and the chat reads it as it stands with lilysharp_readScore. A
 *  file not there opens as an unsaved score named by its path, as Compose with AI in Chat opens one.
 *  The document written, or the reason when it could not be. */
async function writeWhole(uri: vscode.Uri, exists: boolean, doc: vscode.TextDocument | undefined, text: string): Promise<vscode.TextDocument | string> {
    const target = exists && doc ? doc : await vscode.workspace.openTextDocument(uri.with({ scheme: 'untitled' }));
    const edit = new vscode.WorkspaceEdit();
    edit.replace(target.uri, new vscode.Range(target.positionAt(0), target.positionAt(target.getText().length)), text);
    if (!await vscode.workspace.applyEdit(edit)) {
        return 'the file could not be written.';
    }
    if (target.isUntitled && target.languageId !== 'lilysharp') {
        await vscode.languages.setTextDocumentLanguage(target, 'lilysharp');
    }
    await vscode.window.showTextDocument(target, { preview: false });
    return target;
}

/** Each document's new piece as the chat's proposal wrote it, and when (proposeScore): a proposal
 *  for it while the text is still that and the musician has not spoken since is the composing going
 *  on — written whole again, not put on a panel over the piece (owner's request 2026-11-01). */
const newPieces = new Map<string, { text: string; at: number }>();

/** The new piece written into `doc`, by its URI now or the one it was written as (an untitled score
 *  saved since). */
function newPieceOf(doc: vscode.TextDocument): { text: string; at: number } | undefined {
    const now = doc.uri.toString();
    const direct = newPieces.get(now);
    if (direct) { return direct; }
    for (const [key, piece] of newPieces) {
        if (currentUri(vscode.Uri.parse(key)).toString() === now) { return piece; }
    }
    return undefined;
}

/** Whether a proposal for the new piece written at `at` is the same request's going on: the musician
 *  has not touched its text, and has said nothing in the chat since — by the Copilot CLI session's
 *  log (lastUserMessageAt); in a chat that keeps no log, by the text alone. */
async function composingGoesOn(doc: vscode.TextDocument, current: string): Promise<boolean> {
    const piece = newPieceOf(doc);
    if (!piece || piece.text !== current) {
        return false;
    }
    const spoken = await chatSpokenAt();
    return spoken === undefined || spoken <= piece.at;
}

/** The unsaved document named by the path it will be saved to (Compose with AI in Chat): the one a
 *  chat's tool call means, not the disk. */
function unsavedNamed(file: string): vscode.TextDocument | undefined {
    return vscode.workspace.textDocuments.find(d => d.uri.scheme === 'untitled'
        && d.uri.fsPath.toLowerCase() === path.resolve(file).toLowerCase());
}

/** The score a chat's tool call means, as the editor holds it: an unsaved one named by the path, else
 *  the file open in the editor, else the file on the disk; undefined when there is none. The reading
 *  and the proposing tool find it alike (owner's report 2026-10-29: "make it longer" — the reading
 *  tool found the open score, the proposing one did not, and wrote the longer piece over it as new). */
async function scoreDocument(file: string): Promise<vscode.TextDocument | undefined> {
    const open = (p: string) => vscode.workspace.textDocuments.find(d => d.uri.scheme === 'file'
        && d.uri.fsPath.toLowerCase() === path.resolve(p).toLowerCase());
    const found = unsavedNamed(file) ?? open(file)
        ?? (fs.existsSync(file) ? await vscode.workspace.openTextDocument(vscode.Uri.file(file)) : undefined);
    if (found) {
        return found;
    }
    // Renamed or moved in VS Code since (onDidRenameFiles): the score where it is now, as the
    // editor holds it — the editor it was asked about.
    const moved = currentUri(vscode.Uri.file(path.resolve(file)));
    return moved.fsPath.toLowerCase() === path.resolve(file).toLowerCase() ? undefined
        : vscode.workspace.textDocuments.find(d => d.uri.toString() === moved.toString())
            ?? (moved.scheme === 'file' && fs.existsSync(moved.fsPath) ? await vscode.workspace.openTextDocument(moved) : undefined);
}

/** What Copilot's chat hands the voicing tool. */
interface ChordVoicingsInput {
    chord?: string;
    /** A progression: one voicing each, the hand moving little between them (owner's request 2026-11-11). */
    chords?: string[];
    /** Shapes without the root, which the band's bass plays. */
    omitRoot?: boolean;
    /** The lowest fret a shape may press: 5 for the shapes from the fifth position up. */
    fromFret?: number;
    instrument?: string;
    low?: string;
    high?: string;
    count?: number;
    /** The part's midiInstrument: a distorted sound is answered power chords first (owner's request 2026-11-01). */
    sound?: string;
    /** A fretted instrument's strings to play, 1 the highest ("1-4"): a band's clean guitar on the upper strings (owner's request 2026-11-10). */
    strings?: string;
}

/** A chord's voicings a player can hold, from the server (lilysharp/chordVoicings). */
async function chordVoicings(deps: AiTransformDeps, input: ChordVoicingsInput): Promise<string> {
    const chords = Array.isArray(input?.chords) ? input.chords.filter(c => typeof c === 'string' && c.trim().length > 0) : [];
    if (!input || (chords.length === 0 && (typeof input.chord !== 'string' || input.chord.trim().length === 0))) {
        return 'Nothing found: give `chord`, a chord symbol (Cm7, G7/B …), or `chords`, the symbols of a progression.';
    }
    await deps.ensureReady?.();
    const client = deps.getClient();
    if (!client || !deps.isReady()) {
        return 'Nothing found: the Lily# language server is not running.';
    }
    try {
        const answer = await client.sendRequest<{ Text: string }>('lilysharp/chordVoicings', {
            Chord: input.chord ?? null, Chords: chords.length > 0 ? chords : null,
            OmitRoot: input.omitRoot === true, FromFret: typeof input.fromFret === 'number' ? Math.floor(input.fromFret) : null,
            Instrument: input.instrument ?? 'guitar', Low: input.low ?? null, High: input.high ?? null,
            Count: typeof input.count === 'number' ? input.count : null,
            Sound: typeof input.sound === 'string' ? input.sound : null,
            Strings: typeof input.strings === 'string' ? input.strings : null,
        });
        return answer.Text;
    } catch (err) {
        return `Nothing found: ${err}`;
    }
}

/** What Copilot's chat hands the reading tool. */
interface ReadScoreInput {
    /** The .lys file, its absolute path — an unsaved one's, the path it is named by. */
    file: string;
    /** One part's share alone: its block, its music in each section, the phrases it calls (scorePartView). */
    part?: string;
    /** Which part of a long answer, from 1 (scorePages). */
    page?: number;
}

/** A reading's most in one answer: under the 8 KB past which Copilot's agent keeps a tool's answer
 *  in a file (owner's report 2026-11-11: a 12.8 KB score went there, and was read back in thirteen
 *  view and grep calls). */
const SCORE_PAGE_BYTES = 6500;

/** The score as it stands in the editor, saved or not (owner's request 2026-10-29: the chat's agent
 *  reads the disk, where an unsaved score is not and a saved one may be behind the editor). */
async function readScore(deps: AiTransformDeps, input: ReadScoreInput): Promise<string> {
    if (!input || typeof input.file !== 'string') {
        return 'Nothing read: give `file` (the .lys file\'s absolute path).';
    }
    const doc = await scoreDocument(input.file);
    if (!doc) {
        return `Nothing read: no score ${input.file} is open in the editor or on the disk.`;
    }
    const state = doc.isUntitled ? 'open in the editor, not saved' : doc.isDirty ? 'open in the editor, with changes not saved' : 'saved';
    const text = doc.getText();
    const said = await saidOf(deps, text);
    // In absolute octaves, as Ask AI sends it (owner's request 2026-10-29): proposeScore brings the
    // proposal back to the file's relative octaves. Lines stay where they were.
    const absolute = await chatAbsolute(deps, text);
    if (absolute !== undefined) {
        deps.log('AI proposal (chat): the score read in absolute octaves.');
    }
    const shown = absolute ?? text;
    const name = path.basename(doc.uri.fsPath);
    // One part's share, where one is asked for.
    const wanted = typeof input.part === 'string' && input.part.trim().length > 0 ? input.part.trim() : undefined;
    const view = wanted ? scorePartView(shown, wanted) : undefined;
    if (wanted && view === undefined) {
        const parts = [...shown.matchAll(/^\s*part\s+([A-Za-z_][\w-]*)/gm)].map(m => m[1]);
        return `Nothing read: ${name} has no part \`${wanted}\`. Its parts: ${parts.join(', ') || 'none'}. Leave \`part\` out to read the whole score.`;
    }
    const how = (absolute !== undefined
        ? ' — shown in absolute octaves (the file itself is in relative octaves; every pitch here says its own octave). '
            + 'To change it, call lilysharp_proposeScore with `edits`, each `old` copied from this text and its `new` in '
            + 'absolute octaves too (or with the whole text, keeping `octave absolute`): Lily# brings it back to the file\'s relative octaves.'
        : '. To change it, call lilysharp_proposeScore with `edits`, each `old` copied from this text (or with the whole new text).')
        + (view !== undefined
            ? ` This is part \`${wanted}\` alone: its own block, its music in each section and the phrases it calls. `
                + `With \`part: "${wanted}"\` beside \`edits\`, they change this part's text alone: the rest of the score need not be read. `
                + '`[lines a-b]` says where a block stands in the file and `…` marks lines left out; neither is in the file.'
            : '');
    const pages = scorePages(view ?? shown, SCORE_PAGE_BYTES);
    const page = Math.max(1, Math.floor(Number(input.page) || 1));
    if (page > pages.length) {
        return `Nothing read: this reading of ${name} has ${pages.length} page(s); there is no page ${page}.`;
    }
    const last = page === pages.length;
    const paging = pages.length === 1 ? ''
        : `\n\n[Page ${page} of ${pages.length}. ` + (last ? 'The end.]'
            : `Read the next with lilysharp_readScore, the same file${wanted ? ' and part' : ''}, \`page: ${page + 1}\`${view === undefined ? ' — or give `part` (a part\'s name) to read one part alone' : ''}.]`);
    return `${name} (${state}), ${doc.lineCount} lines, as it stands${how}\n${pages[page - 1]}${paging}${last ? said : ''}`;
}

/** What Lily# says of the score as it stands, after its text (owner's report 2026-10-30: asked to
 *  mend a drum line that ran past the song, the chat's agent counted its phrases in a terminal
 *  script — "Run in terminal?" — where the warning named the bar to cut it at). */
async function saidOf(deps: AiTransformDeps, text: string): Promise<string> {
    const client = deps.getClient();
    if (!client || !deps.isReady() || isEmptyScore(text)) {
        return '';
    }
    const diags = (await checkText(client, text)).filter(d => d.Severity === 'error' || (d.Severity === 'warning' && !MACHINE_WARNING.test(d.Message)));
    return diags.length === 0 ? '\n\nLily# finds nothing to warn of in it.'
        : `\n\nWhat Lily# says of it (the lines and columns are the file's):\n${formatDiags(diags, text)}`;
}

/**
 * Whether a new piece's file name is free: no file on the disk, no score open in the editor, saved
 * or not, by that name — else the first free one beside it (`Name-2.lys`, …). Owner's request
 * 2026-10-29: the chat listed the folder in a terminal to check, asking "Run in terminal?".
 */
/** The step after a new piece's name, said in the answer that names it (owner's trials 2026-11-08: told
 *  the whole way at the start, a model composed the whole piece in thought before its plan — ten minutes,
 *  and cut off; each answer now says the next step alone). */
const PLAN_NEXT = 'Next: the plan alone — the composing guide\'s headings, the piece\'s shape in short lines, no section\'s '
    + 'chords bar by bar nor its notes yet — then start the draft with lilysharp_draftScore: the header lines, the title, '
    + 'tempo, meter, key and the parts. Think of no section\'s music in this step: each section is its own step after it. '
    + 'Answer within five minutes, thinking included: a longer think is cut off by the connection and lost.';

async function checkName(input: ReadScoreInput): Promise<string> {
    if (!input || typeof input.file !== 'string' || !path.isAbsolute(input.file)) {
        return 'Nothing checked: give `file`, the new .lys file\'s absolute path.';
    }
    const file = path.resolve(input.file);
    const taken = async (p: string) => fs.existsSync(p) || (await scoreDocument(p)) !== undefined;
    if (!await taken(file)) {
        return `Free: ${path.basename(file)} is not taken in ${path.dirname(file)} — propose the new piece there. ` + PLAN_NEXT;
    }
    const ext = path.extname(file) || '.lys';
    const stem = path.basename(file, path.extname(file)).replace(/-\d+$/, '');
    for (let n = 2; n < 100; n++) {
        const free = path.join(path.dirname(file), `${stem}-${n}${ext}`);
        if (!await taken(free)) {
            return `Taken: ${path.basename(file)} is already a score in ${path.dirname(file)}. Name the new piece ${free} — `
                + 'it is free — or after another title. ' + PLAN_NEXT;
        }
    }
    return `Taken: ${path.basename(file)} and its numbered names are all in use in ${path.dirname(file)}; name the piece after another title.`;
}

/** The score in absolute octaves for the chat, or undefined when it goes as it is: a file
 *  already absolute, one the server cannot convert exactly, or no server. */
async function chatAbsolute(deps: AiTransformDeps, text: string): Promise<string | undefined> {
    const client = deps.getClient();
    if (!client || !deps.isReady() || isEmptyScore(text)) {
        return undefined;
    }
    try {
        const resp = await client.sendRequest<AiOctavesResponse>('lilysharp/aiOctaves', { Text: text, ToAbsolute: true });
        return resp.NewText != null && resp.NewText !== text ? resp.NewText : undefined;
    } catch {
        return undefined;
    }
}

/** The tonic the last composing guide drew for the chat's piece (readGuide), its key checked once. */
let drawnTonic: string | undefined;
/** The starting point drawn for the chat's next piece, kept until that piece is first checked. */
let drawnHint: string | undefined;

/** The model calling proposeScore, read from the newest Copilot CLI session's log (copilotModelOf);
 *  undefined in a chat that keeps none, as VS Code's own. */
async function chatModelName(): Promise<string | undefined> {
    const log = await chatSessionLog();
    return log === undefined ? undefined : copilotModelOf(log);
}

/** When the musician last spoke in the chat calling proposeScore, by the newest Copilot CLI session's
 *  log (lastUserMessageAt); undefined in a chat that keeps none. */
async function chatSpokenAt(): Promise<number | undefined> {
    const log = await chatSessionLog();
    return log === undefined ? undefined : lastUserMessageAt(log);
}

/** The newest Copilot CLI session's events log, when it is this chat's: one written to just now —
 *  this call was logged as it began. Undefined where there is none, as in VS Code's own chat. */
async function chatSessionLog(): Promise<string | undefined> {
    try {
        const root = path.join(os.homedir(), '.copilot', 'session-state');
        let newest: { file: string; time: number } | undefined;
        for (const dir of await fs.promises.readdir(root)) {
            const file = path.join(root, dir, 'events.jsonl');
            const time = (await fs.promises.stat(file).catch(() => undefined))?.mtimeMs;
            if (time !== undefined && (newest === undefined || time > newest.time)) { newest = { file, time }; }
        }
        return newest && Date.now() - newest.time < 60_000 ? await fs.promises.readFile(newest.file, 'utf8') : undefined;
    } catch {
        return undefined;
    }
}

/** The files whose new piece the music check has spoken on, this session: it speaks once a file. */
const musicCheckedFor = new Set<string>();

/** What the first music check found in each file's new piece, its counts left out (findingKey), and
 *  the files whose revision has been sent back for what it brought in: the second time, the last. */
const firstFindings = new Map<string, Set<string>>();
const recheckedFor = new Set<string>();
/** The files whose last revision has been sent back for a mistake it made: once, after the two above. */
const mistakeRecheckedFor = new Set<string>();
const noTuneRecheckedFor = new Set<string>();
/** The compiler's warnings each new piece has been told of, and the files sent back for a warning a revision brought. */
const firstWarnings = new Map<string, Set<string>>();
const warningRecheckedFor = new Set<string>();
const findingKey = (finding: string) => finding.replace(/\d+/g, '#');

/** The proposal shown and decided on; what is told to the chat. */
async function proposeScore(deps: AiTransformDeps, given: ProposeScoreInput, token: vscode.CancellationToken): Promise<string> {
    if (!given || typeof given.file !== 'string') {
        return 'Nothing shown: give `file` (the .lys file\'s absolute path) and `text` (its whole new text), or `draft: true`.';
    }
    // The piece written into its draft, a part at a time (lilysharp_draftScore).
    if (given.draft === true) {
        const draft = drafts.get(draftKey(given.file));
        if (draft === undefined) {
            return `Nothing shown: ${path.basename(given.file)} has no draft. Write the piece into it with lilysharp_draftScore first, `
                + 'a part at a time, then propose it with `draft: true`.';
        }
        given = { ...given, text: draft };
    }
    const edits = typeof given.text !== 'string' && Array.isArray(given.edits) && given.edits.length > 0 ? given.edits : undefined;
    if (typeof given.text !== 'string' && edits === undefined) {
        return 'Nothing shown: give `file` (the .lys file\'s absolute path) and `edits` (the changes to the score as it stands), '
            + 'or `text` (its whole new text), or `draft: true`.';
    }
    await deps.ensureReady?.();
    const client = deps.getClient();
    if (!client || !deps.isReady()) {
        return 'Nothing shown: the Lily# language server is not running. Write the file directly instead.';
    }
    // Edits change the score as the reading tool shows it — in absolute octaves where it showed
    // them — and the text they make goes on as a whole text would.
    if (edits !== undefined) {
        const standingDoc = await scoreDocument(given.file);
        if (!standingDoc) {
            return `Nothing shown: no score ${given.file} is open in the editor or on the disk for \`edits\` to change. A new piece is given whole (\`text\`, or its draft).`;
        }
        const standing = standingDoc.getText();
        const applied = applyScoreEdits(await chatAbsolute(deps, standing) ?? standing, edits,
            typeof given.part === 'string' ? given.part : undefined);
        if ('error' in applied) {
            return applied.error + ' Read the score again with lilysharp_readScore if it may have changed.';
        }
        deps.log(`AI proposal (chat): ${edits.length} edit(s), ${applied.changed} place(s) changed.`);
        given = { ...given, text: applied.text };
    }
    const input = { ...given, text: given.text as string };
    const doc = await scoreDocument(input.file);
    const uri = doc?.uri ?? vscode.Uri.file(input.file);
    const exists = doc !== undefined;
    const current = doc?.getText() ?? '';
    // A new piece goes to a new file, never over the musician's score (owner's request 2026-11-07:
    // asked with a score open, the chat proposed the new piece over it). The guide says so; this
    // holds when a model does not listen.
    if (newPieceOverScore(current, input.text)) {
        deps.log(`AI proposal (chat): turned down — a new piece sent over another score (${input.file}).`);
        return `Not shown to the musician: this is a new piece, and ${input.file} holds another score of theirs, which it `
            + 'would replace. Propose it to a NEW file in the same folder instead: name it after the piece\'s title, check '
            + 'the name with lilysharp_checkName, and call lilysharp_proposeScore again with that path and the same text.';
    }
    // The file's opening comments kept, where the proposal left them out (owner's request 2026-10-29).
    let text = withLeadingComments(current, input.text);
    // Errors go back unseen: the musician is shown only a score that compiles.
    let diags = await checkText(client, text);
    const errors = diags.filter(d => d.Severity === 'error');
    if (errors.length > 0) {
        deps.log(`AI proposal (chat): turned down — ${errors.length} error(s).`);
        return `Not shown to the musician: it does not compile.\n${formatDiags(errors, text)}\n`
            + 'Fix these and call lilysharp_proposeScore again with the whole corrected text.';
    }
    // Written in the absolute octaves readScore showed: back to the file's relative octaves before
    // the musician sees it (owner's request 2026-10-29).
    if (ABSOLUTE_LINE.test(text) && !ABSOLUTE_LINE.test(current) && await chatAbsolute(deps, current) !== undefined) {
        const resp = await client.sendRequest<AiOctavesResponse>('lilysharp/aiOctaves', { Text: text, ToAbsolute: false, Original: current });
        if (resp.NewText == null) {
            deps.log(`AI proposal (chat): could not be brought back to relative octaves — ${resp.Error}`);
            return `Not shown to the musician: written in absolute octaves, it could not be brought back to the file's `
                + `relative octaves (${resp.Error}). Mend that, or propose the whole text in the file's own relative octaves.`;
        }
        deps.log('AI proposal (chat): brought back to relative octaves.');
        text = resp.NewText;
        diags = await checkText(client, text);
    }
    // A new piece is checked as music once, as Compose with AI checks it (owner's request
    // 2026-10-29: a chat's quartet had one rhythm in 20 of its first violin's 24 bars, and half notes
    // below it throughout): the findings go back unseen, and the next proposal is shown whatever
    // it does with them — the AI may keep what it meant.
    const before = current ? await checkText(client, current) : [];
    const added = addedWarnings({ HasErrors: false, Diagnostics: diags }, [{ beforeLo: 0, beforeHi: current.length, afterLo: 0, afterHi: text.length }],
        before.filter(d => d.Severity === 'warning' && !MACHINE_WARNING.test(d.Message)).length);
    // A bar that does not add up goes back every time, as an error does (owner's report 2026-10-29: a
    // revision's 7/8 bar was written in, the hands out of step from there on; warnings go back once) —
    // the score's own as well as the proposal's (owner's decision 2026-10-30: a drum line asked to end
    // with the song was cut by 2 of its 18 bars too many, and shown, its warning fewer than before).
    const misfit = diags.filter(d => d.Severity === 'warning' && BAR_LENGTH_CODES.has(d.Code ?? ''));
    if (misfit.length > 0) {
        deps.log(`AI proposal (chat): turned down — ${misfit.length} bar(s) that do not add up.`);
        return `Not shown to the musician: its bars do not add up — the parts would not stay together.\n${formatDiags(misfit, text)}\n`
            + 'Count the beats of each bar named (the bars of each voice named), mend it, and call lilysharp_proposeScore again with the whole corrected text.';
    }
    // A note its instrument cannot sound goes back every time too, the score's own as well (owner's
    // decision 2026-10-30: a rock trio's last guitar chord kept an E♭2 below the guitar's E2, "for its
    // open sound", the warning told once and left).
    const unplayable = diags.filter(d => d.Severity === 'warning' && d.Code === OUT_OF_RANGE_CODE);
    if (unplayable.length > 0) {
        deps.log(`AI proposal (chat): turned down — ${unplayable.length} note(s) out of their instrument's range.`);
        return `Not shown to the musician: its players cannot play it — a note out of its instrument's range.\n${formatDiags(unplayable, text)}\n`
            + 'Move each note named into the range (an octave up or down, or another note of its chord), or give the part an '
            + 'instrument that has it, and call lilysharp_proposeScore again with the whole corrected text.';
    }
    // A syllable with no note goes back every time too (owner's decision 2026-11-01): the words would
    // be lost from the page.
    const unsung = diags.filter(d => d.Severity === 'warning' && d.Code === LOST_SYLLABLE_CODE);
    if (unsung.length > 0) {
        deps.log(`AI proposal (chat): turned down — ${unsung.length} syllable(s) with no note to sing.`);
        return `Not shown to the musician: words would be lost — a syllable with no note to sing it on.\n${formatDiags(unsung, text)}\n`
            + 'In each bar named, give the words as many notes as they have syllables (or as many syllables as the notes), '
            + 'and call lilysharp_proposeScore again with the whole corrected text.';
    }
    // Its compiler warnings go back with it, unseen, the same once (owner's report 2026-10-29: a
    // piece written with a warning was proposed again at once to mend it, and the musician saw the
    // half-done piece in the editor and then a panel to accept its mending).
    let ranges: string[] | undefined;
    if (isEmptyScore(current) && !musicCheckedFor.has(uri.toString())) {
        const report = await compositionReport(client, text);
        // …and its header said in the request's language, sent back with them (owner's request 2026-10-29).
        const language = headerLanguageNote(text);
        // …and its key held to the one drawn for it, the draw said to the AI alone (owner's decision 2026-10-30).
        const music = [...report.findings, language, drawnKeyNote(text, drawnTonic)].filter((m): m is string => m !== undefined);
        drawnTonic = undefined;   // one draw, one piece: not held against the next, its guide maybe read from the rules file
        drawnHint = undefined;    // …and the next piece draws its own
        ranges = report.ranges;
        musicCheckedFor.add(uri.toString());
        firstFindings.set(uri.toString(), new Set(report.findings.map(findingKey)));
        firstWarnings.set(uri.toString(), new Set(added.map(w => findingKey(w.Message))));
        if (music.length > 0 || added.length > 0) {
            deps.log(`AI proposal (chat): sent back once — ${added.length} warning(s); as music: ${music.join(' / ') || 'nothing'}`);
            // The check counts notes and does not know the music meant (owner's decision 2026-10-29: a
            // hymn's even quarters, sent back, came again stumbling): said before what it found.
            return 'Not shown to the musician yet. '
                + (added.length > 0 ? `The Lily# compiler warns:\n${formatDiags(added, text)}\nFix what is not meant. ` : '')
                + (music.length > 0
                    ? 'Lily#\'s check of a new piece found this. It counts notes and does not know '
                      + 'the kind of music you mean: take what helps the piece, and leave what does not fit it —\n'
                      + `${music.map(m => `- ${m}`).join('\n')}\n`
                    : '')
                + 'Revise the piece with these in mind and call lilysharp_proposeScore again with the whole text. '
                + 'Where something is as you meant it, keep it, and say why in the proposal\'s note: the next proposal '
                + 'is checked again only for what it brings in, and what the check still finds in it is told to the musician.'
                + rangesSaid(ranges);
        }
    }
    // A new piece goes straight into the file, not to the panel (owner's request 2026-10-29: a
    // composed piece decided on beside an empty score was the less clear way — what is to change is
    // said in the chat afterwards, a bar named there if need be). So does the piece proposed again
    // while the composing goes on — the chat revising its own piece, unasked, before the musician has
    // said a word (owner's request 2026-11-01: a composed piece opened, and a panel over it at once;
    // only the final piece is to open). A change the musician asks for in the chat is a proposal.
    const goesOn = doc !== undefined && !isEmptyScore(current) && await composingGoesOn(doc, current);
    if (isEmptyScore(current) || goesOn) {
        // Opened as a musician writes it, as Compose with AI's piece is (owner's report 2026-10-29).
        // The ranges are of the piece as the AI wrote it: they are the same notes.
        // The revision is written as it is, and what the check still finds in it said to the musician
        // (owner's decision 2026-10-30: sent back, a rock piece of 44 seconds came again at 42, the
        // length unmentioned, its missing tune made eighth-note arpeggios the check did not see).
        let kept: string[] = [];
        if (ranges === undefined) {
            const report = await compositionReport(client, text);
            ranges = report.ranges;
            kept = report.findings;
            // Chords no hand can hold, this many, are a piece its player cannot play: turned down every
            // time (owner's decision 2026-11-01: a solo guitar's 14, a tune and a thumb over chords of
            // four and five notes, written in as "hard to hold").
            const unheld = unfingerableOf(diags.filter(d => d.Severity === 'warning'));
            if (unheld.length >= UNFINGERABLE_TURNED_DOWN) {
                deps.log(`AI proposal (chat): turned down — ${unheld.length} chords no hand can hold.`);
                return `Not shown to the musician: its player cannot play it — ${unheld.length} chords no hand can hold, each counted with the notes of the other voices sounding with it.\n${formatDiags(unheld, text)}\n`
                    + 'One hand plays the tune, the chord and the bass together: strike four notes at once at most, the chord on three neighbouring strings, '
                    + 'two of them where the tune strikes with it, the hand within a few frets from chord to chord; and call lilysharp_proposeScore again with the whole text.'
                    + rangesSaid(ranges);
            }
            // A tune held against its chords this often is wrong notes, not colour: turned down every
            // time, as a bar that does not add up is (owner's decision 2026-11-01: 12 held clashes in a
            // dawn ballad were written in "as colour").
            const clashing = heldClashesOf(kept);
            if (clashing && clashing.count >= CLASHES_TURNED_DOWN) {
                deps.log(`AI proposal (chat): turned down — ${clashing.count} notes held against the harmony.`);
                return `Not shown to the musician: its tune stands against its chords — ${clashing.count} notes held on the beat a semitone `
                    + `from the harmony, which is not colour but wrong notes.\n- ${clashing.finding}\n`
                    + 'At each, take the chord\'s own tone or change the chord under it, and call lilysharp_proposeScore again with the whole text.'
                    + rangesSaid(ranges);
            }
            // Singers on one line this often are fewer parts than the page says: the same (owner's
            // decision 2026-11-01).
            const oneLine = singersInOneLineOf(kept);
            if (oneLine.length > 0) {
                deps.log(`AI proposal (chat): turned down — ${oneLine.length} pair(s) of singers on one line.`);
                return 'Not shown to the musician: its parts are not parts — singers doubling each other for a fifth or more of what they sing.\n'
                    + oneLine.map(f => `- ${f}`).join('\n') + '\n'
                    + 'Give each part a line of its own — a third or a sixth apart, one holding while the other moves — and call lilysharp_proposeScore again with the whole text.'
                    + rangesSaid(ranges);
            }
            // A melody that is still no tune goes back once more, and only that once: the AI may mean it.
            const noTune = noTuneOf(kept);
            if (noTune.length > 0 && !noTuneRecheckedFor.has(uri.toString())) {
                noTuneRecheckedFor.add(uri.toString());
                deps.log(`AI proposal (chat): revision sent back — still no tune: ${noTune.join(' / ')}`);
                return 'Not shown to the musician yet. The revision still has no tune:\n'
                    + `${noTune.map(m => `- ${m}`).join('\n')}\n`
                    + 'Write the tune: change the notes and the rhythm of the melody itself, and keep the chords and the form. If broken chords in one value are what the piece is, '
                    + 'send it as it is and say why in the proposal\'s note. Call lilysharp_proposeScore again with the whole text.'
                    + rangesSaid(ranges);
            }
            // A compiler warning the revision brought goes back once too (owner's decision 2026-11-02: a hymn's
            // alto and tenor were written with a @cresc leading to @p, LYS4035, the warning only told).
            const warned = firstWarnings.get(uri.toString()) ?? new Set<string>();
            const newWarnings = added.filter(w => !warned.has(findingKey(w.Message)));
            if (newWarnings.length > 0 && !warningRecheckedFor.has(uri.toString())) {
                warningRecheckedFor.add(uri.toString());
                firstWarnings.set(uri.toString(), new Set([...warned, ...added.map(w => findingKey(w.Message))]));
                deps.log(`AI proposal (chat): revision sent back — ${newWarnings.length} warning(s) it brought.`);
                return 'Not shown to the musician yet. The Lily# compiler warns of this in the revision, not in the piece before it:\n'
                    + `${formatDiags(newWarnings, text)}\n`
                    + 'Mend each and leave the rest as it is, and call lilysharp_proposeScore again with the whole text; if one is as you meant it, say why in the proposal\'s note.'
                    + rangesSaid(ranges);
            }
            // What the revision brought in goes back once more (owner's decision 2026-10-30: a revision's
            // bass slipped two octaves, `cis''`, and was written in, the AI never told of it); the
            // proposal after that is written.
            const first = firstFindings.get(uri.toString()) ?? new Set<string>();
            const brought = kept.filter(m => !first.has(findingKey(m)));
            if (brought.length > 0 && !recheckedFor.has(uri.toString())) {
                recheckedFor.add(uri.toString());
                firstFindings.set(uri.toString(), new Set([...first, ...kept.map(findingKey)]));
                deps.log(`AI proposal (chat): revision sent back — as music: ${brought.join(' / ')}`);
                return 'Not shown to the musician yet. Lily#\'s check finds this in the revision, not in the piece before '
                    + 'it — it counts notes and does not know the kind of music you mean: take what helps the piece, and '
                    + `leave what does not fit it —\n${brought.map(m => `- ${m}`).join('\n')}\n`
                    + 'Revise the piece with these in mind and call lilysharp_proposeScore again with the whole text. '
                    + 'The proposal after this one is written, unless it makes a mistake the piece did not have, and what the check still finds in it is told to the musician.'
                    + rangesSaid(ranges);
            }
            // A mistake the last revision made goes back too, once (owner's decision 2026-11-02: a carol's
            // theme kept to its 12/8 beats, was told "70% eighths", and came back across them in 17 of 35
            // bars, too late to be sent back). The rest of the last revision's findings are told.
            const mistakes = report.mistakes.filter(m => !first.has(findingKey(m)));
            if (mistakes.length > 0 && !mistakeRecheckedFor.has(uri.toString())) {
                mistakeRecheckedFor.add(uri.toString());
                deps.log(`AI proposal (chat): revision sent back for a mistake it made: ${mistakes.join(' / ')}`);
                return 'Not shown to the musician yet. Your last revision made this mistake, which the piece before it did not have '
                    + `— almost surely not what you meant:\n${mistakes.map(m => `- ${m}`).join('\n')}\n`
                    + 'Mend it and leave the rest as it is, and call lilysharp_proposeScore again with the whole text; if it is as you meant it, '
                    + 'send the piece as it is and say why in the proposal\'s note. The proposal after this one is written as it is, and what the check still finds in it is told to the musician.'
                    + rangesSaid(ranges);
            }
        }
        // Its header names the model that composed it (owner's request 2026-10-30).
        const model = await chatModelName();
        if (model !== undefined) { text = withModelNamed(text, model); }
        const musicians = await asMusicianWrites(deps, client, text);
        // Said as it is written (owner's report 2026-10-30: a piece left in absolute octaves was told
        // to the chat as relative).
        const octaves = ABSOLUTE_LINE.test(musicians)
            ? 'It is written in absolute octaves, as you proposed it (Lily# could not bring it into relative octaves); '
            : 'It is written in relative octaves, as a musician writes it; ';
        const written = await writeWhole(uri, exists, doc, musicians);
        if (typeof written === 'string') {
            return `Not written: ${written}`;
        }
        newPieces.set(written.uri.toString(), { text: written.getText(), at: Date.now() });
        deps.log(goesOn ? 'AI proposal (chat): the composing goes on — the new piece written into the file again, no panel.'
            : 'AI proposal (chat): a new piece — written into the file.');
        // What the check still finds, said by Lily# itself: the chat is asked to tell the musician
        // and may not (owner's request 2026-11-01: ひかりの道's last message left both points out).
        const stillFound = [...kept, ...added.map(w => w.Message)];
        if (stillFound.length > 0) {
            for (const point of stillFound) { deps.log(`  still found: ${point}`); }
            void vscode.window.showInformationMessage(
                `Lily#: ${path.basename(currentUri(uri).fsPath)} was written with ${stillFound.length} point(s) from the check left in it.`,
                'Show Log').then(pick => { if (pick === 'Show Log') { deps.showLog?.(); } });
        }
        // The piece is done: what is to change, the musician says — not proposed again unasked, which
        // would put a panel over the piece just written (owner's report 2026-10-29).
        // The file by its own name, for the chat to tell the musician (owner's report 2026-10-29: the
        // chat called Meifu-no-Ou-to-no-Kessen.lys "冥府の王との決戦.lys", the piece's title).
        return `Written${goesOn ? ' again, in place of the piece written before' : ''}: the new piece is `
            + `${path.basename(currentUri(uri).fsPath)} — call the file by that name, `
            + 'not by the piece\'s title. It is open in the editor, not saved: the musician '
            + 'saves it when they want to keep it. ' + octaves
            + 'read it with lilysharp_readScore (not from the disk), which shows it to you in absolute octaves. The piece is done: '
            + 'do not propose it again unless the musician asks for a change in the chat.'
            + (added.length ? ` It keeps these warnings, as you proposed it:\n${formatDiags(added, text)}` : '')
            + (kept.length > 0
                ? '\nLily#\'s check of the new piece still finds this in it, as written — tell the musician each point, in '
                  + 'a line in your own words, and why you left it (or that you can change it if they wish):\n'
                  + kept.map(m => `- ${m}`).join('\n')
                : '')
            + rangesSaid(ranges);
    }
    // Written by place, a warning the score did not have goes back once, unseen: mended, or sent
    // again as it is and shown with it — no flag to set, so none to set from the start (the tool's
    // own user's wish, 2026-11-11: a tool that refuses every warning is mended by changing the music
    // to please it, and one with a switch is used with the switch on).
    if (given.sendBackWarnings === true && added.length > 0) {
        const key = `${uri.toString()}|${added.map(w => findingKey(w.Message)).sort().join('|')}`;
        if (!writeWarnedFor.has(key)) {
            writeWarnedFor.add(key);
            deps.log(`AI write by place (chat): sent back once — ${added.length} new warning(s).`);
            return 'Not shown to the musician yet. The Lily# compiler warns of this in what you wrote, not in the score before it:\n'
                + `${formatDiags(added, text)}\n`
                + 'Mend what is not meant. Sent again as it is, the change is shown to the musician with these warnings: '
                + 'say then in `note` why they stand.';
        }
    }
    const snapshot: Snapshot = {
        version: doc?.version ?? 0, startOffset: 0, endOffset: current.length,
        origFullText: current, origSelectedText: current, uri,
    };
    const edit = wholeFileEdit(snapshot, text);
    const changed = changedWords(current, text, edit.spans);
    const [renderBefore, renderAfter] = await Promise.all([
        client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: current }),
        client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: text }),
    ]);
    const caption = `Proposed in Copilot's chat — ${path.basename(input.file)}`
        + (added.length ? ` — ⚠ ${added.length} new warning(s): ${added.map(w => w.Message).join(' / ')}` : '');
    // The musician went on in the chat while the panel waited (a new message stops the tool): the
    // proposal stays on the panel, undecided, and the chat's next one replaces it there.
    let wentOn = false;
    const stop = token.onCancellationRequested(() => {
        wentOn = true;
        const resolve = pendingDecision;
        pendingDecision = undefined;
        resolve?.('reject');
    });
    try {
        let again: string | undefined;
        for (;;) {
            // Ask Again is not offered: what to change is said in the chat (owner's request 2026-10-29).
            const decision = again !== undefined ? await decideAgain(again)
                : await reviewOnScore(deps, renderBefore, renderAfter, caption, wholeTextMarkup(current, text, changed), changed,
                    { uri, before: current, after: text }, input.note ?? null, true);
            again = undefined;
            if (wentOn) {
                panelStatus('Not decided — the chat went on; its next proposal opens here.');
                deps.log('AI proposal (chat): not decided — the chat went on.');
                return 'Not decided: the musician went on in the chat before deciding, and the file is unchanged. '
                    + 'Read their message; propose again with lilysharp_proposeScore if it asks for changes.';
            }
            if (decision === 'accept') {
                again = await applyCandidate(deps, snapshot, text);
                if (again !== undefined) {
                    continue;
                }
                // Not saved: Accept puts it in the editor, the musician saving when they want to
                // (owner's request 2026-10-29); the chat reads it there with lilysharp_readScore.
                deps.log('AI proposal (chat): accepted.');
                return 'Accepted: the musician accepted the proposal, and it is in the editor, not saved yet — '
                    + `${path.basename(currentUri(uri).fsPath)} (call the file by that name; it may have been renamed). Read it `
                    + 'with lilysharp_readScore, not from the disk.'
                    + (added.length ? ` It has these warnings, which they may want fixed:\n${formatDiags(added, text)}` : '')
                    + rangesSaid((await compositionReport(client, text)).ranges);
            }
            // Rejected: the panel closes, as Ask AI's Reject closes it (owner's request 2026-10-30: kept
            // open, its buttons greyed, it could only be closed, and the chat said only "tell me what to
            // change"); the chat's next proposal opens a new one. The chat says what was turned down.
            deps.log('AI proposal (chat): rejected.');
            candidatePanel?.dispose();
            vscode.window.setStatusBarMessage('Lily#: the chat\'s proposal rejected — the file is unchanged.', 4000);
            return 'Rejected: the musician rejected the proposal, and its panel is closed; the file is unchanged. '
                + 'Tell them in a line or two what the rejected proposal changed (and anything it left undone), then '
                + 'ask what they want instead. Propose again with lilysharp_proposeScore when they say.';
        }
    } finally {
        stop.dispose();
    }
}

/**
 * "Lily#: Compose with AI in Chat…" (owner's request 2026-10-29: in Copilot's chat the model's
 * thinking and its tries are seen, and a stronger model may be picked there): asks what to compose
 * and the file it is to be, and hands the request to the chat's agent, which reads how to begin —
 * the header, the steps, a starting point drawn at random — and the composing guide from
 * lilysharp_readGuide. Its proposal opens the piece in the editor, unsaved.
 */
async function runComposeInChat(deps: AiTransformDeps): Promise<void> {
    const description = await vscode.window.showInputBox({
        title: 'Lily# — Compose with Copilot',
        // The model decides the piece more than anything the guide says (owner's trials 2026-11-07:
        // the same guide, a light model kept little of it and Claude Sonnet 5.5 nearly all), and the
        // chat's Auto takes a light one for a short request.
        prompt: 'What should the AI compose? Copilot\'s chat writes it into a new, unsaved score, its thinking in view. '
            + 'Pick a strong model in the chat\'s model menu: Auto may take a light one for a short request.',
        placeHolder: 'e.g. a dramatic string quartet in D minor, about 3 minutes, slow – fast – slow',
        ignoreFocusOut: true,
    });
    if (description === undefined || description.trim().length === 0) {
        return;
    }
    // The folder it goes in, beside the active file; its name the chat gives it from the piece's
    // title, as the composing guide says (owner's request 2026-10-29: pieces saved as "Composed.lys"
    // were renamed by hand). Nothing is opened before the piece comes: the chat's proposal opens it,
    // unsaved, as it opens a piece asked for with no .lys open.
    const active = vscode.window.activeTextEditor?.document.uri;
    const folder = active?.scheme === 'file' ? path.dirname(active.fsPath)
        : vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? os.homedir();
    // The request alone, and where to read how to begin (owner's request 2026-10-29: the header, the
    // steps, a starting point drawn at random and the composing guide come from the guide tool, as
    // they do to a chat that asks with no .lys open).
    const query = `Compose a new piece of music in Lily# in the folder ${folder} (a new file: your proposal opens it in the editor).\n`
        + 'First read how to begin and the composing guide: lilysharp_readGuide, topic "composing".\n'
        + `The request: ${description.trim()}`;
    try {
        await vscode.commands.executeCommand('workbench.action.chat.open', { query, mode: 'agent' });
    } catch {
        try {
            await vscode.commands.executeCommand('workbench.action.chat.open', query);
        } catch (err) {
            void vscode.window.showErrorMessage(`Lily#: Copilot's chat could not be opened (${err}). Is GitHub Copilot Chat installed and signed in?`);
        }
    }
}

/** The whole select→prompt→validate→render→apply transaction (§3/§4). */
async function runAiTransform(deps: AiTransformDeps, wholeScore = false, selected?: string, asked?: string): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys file and select what to transform.');
        return;
    }
    const client = deps.getClient();
    if (!client || !deps.isReady()) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }

    const doc = editor.document;

    // A range is required — an empty selection uses the current line so Ctrl+I on a
    // single line "just works". On an EMPTY line with nothing selected, the whole score is the
    // range (owner's request 2026-10-24: "sometimes I want the AI to look at the whole score");
    // it used to be refused there. The preview's "Ask AI…" off the staves asks for the
    // whole score outright (wholeScore).
    let sel: vscode.Range = editor.selection;
    let whole = false;
    let what = selected ?? 'The selected text';
    if (wholeScore) {
        whole = true;
        sel = new vscode.Range(doc.positionAt(0), doc.positionAt(doc.getText().length));
    } else if (sel.isEmpty) {
        what = "The caret's line";
        sel = doc.lineAt(editor.selection.active.line).range;
        if (doc.getText(sel).trim().length === 0) {
            whole = true;
            sel = new vscode.Range(doc.positionAt(0), doc.positionAt(doc.getText().length));
        }
    }
    const selectedText = doc.getText(sel);
    if (selectedText.trim().length === 0) {
        vscode.window.showErrorMessage('Lily#: the file is empty — write some notation first.');
        return;
    }

    // --- Snapshot (§7): freeze the doc version, the range, and the original text so
    // generation runs against a stable base even if the user edits elsewhere. ---
    const snapshot = {
        version: doc.version,
        startOffset: doc.offsetAt(sel.start),
        endOffset: doc.offsetAt(sel.end),
        origFullText: doc.getText(),
        origSelectedText: selectedText,
        uri: doc.uri,
    };

    // Resolve the model first (the user's Copilot models via vscode.lm): its input limit decides
    // whether the file goes to it whole, and the input box says which.
    const chat = await resolveChatClient(false);
    if (!chat) {
        return;
    }
    const grammar = await loadGrammar(deps);
    const systemTokens = estimateTokens(systemPrompt(grammar));
    if (!tellsTooSmall(chat, systemTokens, 'Ask')) {
        return;
    }
    const budget = documentBudget(chat.maxInputTokens, systemTokens);
    const shown = documentContext(snapshot, budget, whole);
    if (!shown.complete) {
        deps.log(`AI transform: the file (~${estimateTokens(snapshot.origFullText)} tokens) is over ${chat.label}'s `
            + `budget (${budget}); it is sent in part.`);
    }

    // A selection points the AI at a place but does not fence it in (owner's requests
    // 2026-10-25): the box names it as a hint; with none it just asks — short, as a box's
    // prompt is read at a glance (owner, 2026-10-25). A file too long for the model says what
    // the AI reads of it.
    const partly = shown.complete ? '' : whole
        ? ' The file is too long for this model: the AI reads its beginning.'
        : ' The file is too long for this model: the AI reads its beginning and the bars around it.';
    const instruction = await vscode.window.showInputBox({
        title: 'Lily# — Ask AI',
        prompt: whole
            ? `What should the AI change?${partly}`
            : `${what} — a hint for the AI. What should it change?${partly}`,
        placeHolder: nextPlaceholder(),
        // Asked again after a failure: the request as it was.
        value: asked,
        ignoreFocusOut: true,
    });
    if (instruction === undefined || instruction.trim().length === 0) {
        return; // cancelled
    }

    const began = Date.now();
    try {
        const ai = await toAbsoluteOctaves(deps, client, snapshot, whole);
        await drive(deps, client, chat, snapshot, ai, instruction, whole, grammar,
            ai.converted ? documentContext(ai.snap, budget, whole) : shown);
    } catch (err: any) {
        const failed = aiFailure('ask', String(err?.message ?? err), Date.now() - began);
        if (failed.retry && await vscode.window.showErrorMessage(failed.text, 'Try Again') === 'Try Again') {
            await runAiTransform(deps, wholeScore, selected, instruction);
        } else if (!failed.retry) {
            vscode.window.showErrorMessage(failed.text);
        }
    }
}

/** The file as the AI works on it: `snap` is the snapshot carried into absolute octaves when
 *  `converted`, else the snapshot itself. */
interface AiSpace { snap: Snapshot; converted: boolean }

interface AiOctavesResponse { NewText: string | null; Changes: TextChange[]; Error: string | null }

/**
 * The file in absolute octaves for the AI (owner's request 2026-10-25): the relative chain —
 * each note nearest the one before, every section starting afresh — is the model's most common
 * slip, and in absolute octaves every pitch says where it sounds. The whole conversation, the
 * checks and the candidate panel work on that text; Accept brings the candidate back to
 * relative octaves (fromAbsoluteOctaves), each top-level `octave` line as the file wrote it. A
 * file already absolute, one that switches mode inside its music, and one the server cannot
 * convert exactly go as they are.
 */
async function toAbsoluteOctaves(deps: AiTransformDeps, client: LanguageClient, snapshot: Snapshot, whole: boolean): Promise<AiSpace> {
    let resp: AiOctavesResponse;
    try {
        resp = await client.sendRequest<AiOctavesResponse>('lilysharp/aiOctaves', { Text: snapshot.origFullText, ToAbsolute: true });
    } catch {
        return { snap: snapshot, converted: false }; // an older server: as before
    }
    if (resp.NewText == null) {
        deps.log(`AI transform: sent in the file's own octaves — ${resp.Error}`);
        return { snap: snapshot, converted: false };
    }
    const text = resp.NewText;
    const start = whole ? 0 : carryOffset(resp.Changes, snapshot.startOffset);
    const end = whole ? text.length : carryOffset(resp.Changes, snapshot.endOffset);
    deps.log(`AI transform: sent in absolute octaves (${resp.Changes.length} change(s)).`);
    return {
        snap: { ...snapshot, origFullText: text, startOffset: start, endOffset: end, origSelectedText: text.slice(start, end) },
        converted: true,
    };
}

/**
 * A composed piece as a musician writes it. The AI writes in absolute octaves; the piece opens in
 * relative ones (owner's request 2026-10-27), converted mechanically — the server compiles the
 * result and compares every note before it answers, so it sounds as the AI wrote it. Left as it
 * came if it cannot.
 * …and without the parts' `octave N` (owner's request 2026-10-28): the bass is written under
 * `octave 4` so the AI can name the pitches it sounds, and that is the AI's crutch, not the
 * musician's — taken out, every note re-marked to sound where it did. If that cannot be done, the
 * piece keeps the line.
 * …and without its `pitch concert` (owner's request 2026-10-28): the AI writes every part as it
 * sounds, and the piece opens as a musician writes it, each part as its player reads it — a bass
 * an octave up, a B♭ trumpet or an E♭ saxophone respelled in its player's key. One that shares a
 * section's key with the others keeps its letters under a `pitch concert` of its own (the key
 * cannot move for it alone).
 * Compose with AI's piece and the chat's new piece alike (owner's report 2026-10-29: a piece
 * composed in the chat opened in absolute octaves).
 */
async function asMusicianWrites(deps: AiTransformDeps, client: LanguageClient, text: string): Promise<string> {
    type Converted = { Success: boolean; NewText: string | null; Error: string | null; Notes?: string | null };
    try {
        let relative = await client.sendRequest<Converted>(
            'lilysharp/convertOctaves', { Text: text, Absolute: false, DropPartOctaves: true, DropConcertPitch: true });
        if (relative.Notes) {
            deps.log(`AI transform: ${relative.Notes}`);
        }
        if (!relative.Success) {
            deps.log(`AI transform: the parts' octave lines stay — ${relative.Error}`);
            relative = await client.sendRequest<Converted>('lilysharp/convertOctaves', { Text: text, Absolute: false });
        }
        if (relative.Success && relative.NewText) {
            return relative.NewText;
        }
        deps.log(`AI transform: the piece stays in absolute octaves — ${relative.Error}`);
    } catch (err) {
        deps.log(`AI transform: the piece stays in absolute octaves — ${err}`);
    }
    return text;
}

/** A top-level `octave absolute` line. */
const ABSOLUTE_LINE = /^[ \t]*octave[ \t]+absolute\b/m;

/** How many `octave …` lines a text holds (the server checks where they stand). */
function octaveLines(text: string): number {
    return (text.match(/^[ \t]*octave[ \t]+(absolute|relative)\b/gm) ?? []).length;
}

/**
 * The accepted candidate (the whole file in absolute octaves) back in the file's relative
 * octaves, or undefined when the user declines the fallback: a candidate the server cannot
 * bring back (it plays a note no score reaches, say) is offered in absolute octaves — the whole
 * file then says `octave absolute`.
 */
async function fromAbsoluteOctaves(deps: AiTransformDeps, client: LanguageClient, full: string, original: string): Promise<string | undefined> {
    // A request that fails — the server restarting — throws to Accept, which says nothing was
    // written and waits to be pressed again; the absolute-octaves fallback below is for a
    // candidate the server read and could not bring back.
    const resp = await client.sendRequest<AiOctavesResponse>('lilysharp/aiOctaves', { Text: full, ToAbsolute: false, Original: original });
    if (resp.NewText != null) {
        return resp.NewText;
    }
    deps.log(`AI transform: the candidate could not be brought back to relative octaves — ${resp.Error}`);
    const absolute = 'Apply in Absolute Octaves';
    const pick = await vscode.window.showWarningMessage(
        `Lily#: the AI worked in absolute octaves, and its change could not be brought back to the file's relative `
        + `octaves (${resp.Error}). Apply it with the whole file in absolute octaves?`,
        { modal: true }, absolute);
    return pick === absolute ? full : undefined;
}

/**
 * Runs `task` under a notification that names the step it is on, with a Cancel button
 * (owner's decision 2026-10-25): after Ctrl+I the screen has to answer at once, and a
 * notification is VS Code's own way to say "working". It is closed before the candidate
 * panel opens — a notification left over the review was why this used to be a status-bar
 * spinner only, which went unnoticed (owner's report 2026-10-24). An in-editor label and a
 * shaded range came between and were dropped: the AI's change need not stay in the range.
 */
function working<T>(title: string,
    task: (progress: vscode.Progress<{ message?: string }>, token: vscode.CancellationToken) => Promise<T>): Thenable<T> {
    // Use the token withProgress hands us — `vscode.CancellationToken` is a TYPE, not a runtime
    // object, so `vscode.CancellationToken.None` throws "Cannot read properties of undefined".
    return vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title, cancellable: true }, task);
}

/**
 * Runs the state machine to completion for one transaction. `instruction` is the
 * initial ask; iteration continues the same conversation with refinements. The work before each
 * candidate runs under a notification (<see cref="working"/>), closed while the candidate is
 * reviewed; its Cancel ends the transaction.
 */
async function drive(
    deps: AiTransformDeps,
    client: LanguageClient,
    chat: ChatClient,
    snapshot: Snapshot,
    ai: AiSpace,
    instruction: string,
    whole: boolean,
    grammar: string,
    shown: { text: string; complete: boolean },
    compose = false,
    /** Compose's own words for the piece: what its notification says, not the instruction to the AI. */
    composeAsked?: string,
): Promise<void> {
    // One notification per round, preparation included (two in a row read as two tasks — owner,
    // 2026-10-25): the model in its title, the request in its message, which also says what step
    // it is on.
    const title = compose ? `Lily#: Compose with AI via ${chat.label}` : `Lily#: Ask AI${whole ? ' (whole score)' : ''} via ${chat.label}`;
    // What the AI reads and writes, and every candidate is spliced into; `snapshot` stays the
    // document's (its resolved facts, its @todo marks, and the edit Accept applies).
    const work = ai.snap;
    const prepare = async (step: (s: string) => void, token: vscode.CancellationToken) => {
        // Quality log (§M5) — local only, no telemetry.
        deps.log(`AI transform: "${instruction}" via ${chat.label}`);

        // Resolved facts of the selection (§5): un-blindfold the model. Best-effort.
        step('reading the score');
        const facts = await getFacts(client, snapshot, token);
        // The @todo marks in the selection and an OMR reader's readings of them (B6).
        const marks = await getMarks(client, snapshot);

        // The compiler's verdict on the untouched document: shown to the model up front (what is
        // wrong NOW is often exactly what the instruction is about — "fix the bar lengths"), and
        // the baseline a candidate is judged against, so only what it makes WORSE counts (a
        // pre-existing problem elsewhere isn't the model's fault).
        // Both on the text the AI works on — in absolute octaves when it was converted: the
        // candidate is compared with it, line for line.
        const baseline = await checkText(client, work.origFullText);

        // Render the untouched score once for the before/after comparison (§M5).
        const renderBefore = await client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: work.origFullText });
        return {
            facts, renderBefore,
            baselineErrors: count(baseline, 'error'),
            baselineWarnings: baseline.filter(d => d.Severity === 'warning' && !MACHINE_WARNING.test(d.Message)).length,
            // Conversation seed.
            messages: [
                { role: 'system', content: compose ? composeSystemPrompt(grammar, chat.modelName) : systemPrompt(grammar) },
                { role: 'user', content: taskPrompt(work, shown, whole, facts, baseline, instruction, marks, ai.converted, compose) },
            ] as ChatMessage[],
        };
    };
    let prepared: Awaited<ReturnType<typeof prepare>> | undefined;

    // The candidate the panel shows, and where the conversation stood before it was asked about
    // again: a round that brings no new candidate — it failed, found none valid, or was cancelled
    // — goes back to it, its buttons armed again (owner's report 2026-10-26: the language server
    // restarted during Ask Again and the panel was left with every button greyed out).
    let shownRound: ReviewRound | undefined;
    let askedFrom = 0;
    // The messages already written to the exchange log (logExchange).
    let logged = 0;
    // The text the AI is answering against: the file, and once it is asked again, the candidate
    // it revises (owner's report 2026-10-27: "add guitar chords" to a composed piece failed three
    // times — its edits quoted the piece, and were looked for in the file it had not been written to).
    let base: Snapshot = work;
    let baseAsked: Snapshot = work;
    let iterate = true;
    while (iterate) {
        // One round — prepare (the first), generate, validate, render — under its notification;
        // null when cancelled or failed (a failure has said so itself).
        let failure: string | undefined;
        let round: ReviewRound | null;
        try {
            round = await working(title, async (progress, token) => {
                const said = composeAsked ?? instruction;
                const asked = said.length > 60 ? `${said.slice(0, 59)}…` : said;
                // The step first, the request last: a notification has no line breaks (VS Code makes
                // them spaces), and a long request wraps after the step instead of burying it.
                const step = (s: string) => progress.report({ message: `${s}… — “${asked}”` });
                prepared ??= await prepare(step, token);
                if (token.isCancellationRequested) return null;
                const { facts, baselineErrors, baselineWarnings, messages } = prepared;

                // ----- Generate + validate-and-self-repair (§4 AwaitingModel → Validating → Repairing) -----
                let candidate: CandidateEdit | null = null;
                // What the AI first said of its work — a composed piece's preface quotes it, not a
                // repair's word on what it fixed, which the person never saw asked (owner's request
                // 2026-10-27: "The AI: I reviewed the bar lengths …" read as if they had).
                let firstNote: string | null | undefined;
                let repairs = 0;
                // A candidate that compiles but adds WARNINGS (a bar that no longer fills its meter
                // is a warning in Lily#, not an error) is repaired like a broken one; if the repairs
                // run out, the best such candidate is still shown, with its warnings named, rather
                // than nothing — it is valid Lily#, and the user can see on the score whether it is
                // what they asked for.
                // For a composed piece it is the NEWEST such candidate (owner's request 2026-10-29: as
                // long as it renders, a later version is worth more than an earlier one without
                // warnings), its warnings marked with @todo on the score.
                let warned: { edit: CandidateEdit; warnings: CandidateDiagnostic[]; text: string; attempt: number } | null = null;
                let unchanged = false;
                let octaveAsked: string | null = null;
                let octaveNote: ResolvedPitchFact[] | null = null;
                let musicAsked = false;
                // A valid piece the music check sent back: shown if no better one comes.
                let flat: { edit: CandidateEdit; music: string[]; attempt: number } | null = null;
                // Why the last attempt was turned down, logged with each (owner's report 2026-10-26:
                // "could not produce a valid transform" said nothing of why, and the log neither).
                let lastReason = '';
                // Why the last revision did not come, when it was cut off (a broken connection).
                let cutOff: string | null = null;
                // A composed piece has one round more (owner's trial 2026-10-29: the music check's revision was
                // the better piece, but three of its bars were a beat long and no round was left to fix them).
                const maxRepairs = compose ? MAX_REPAIR_ATTEMPTS + 1 : MAX_REPAIR_ATTEMPTS;
                const turnedDown = (attempt: number, reason: string) => {
                    lastReason = reason;
                    deps.log(`AI transform: attempt ${attempt + 1}/${maxRepairs + 1} turned down — ${reason}`);
                };
                for (let attempt = 0; attempt <= maxRepairs; attempt++) {
                    if (token.isCancellationRequested) return null;
                    step(attempt === 0 ? 'generating' : `repairing (${attempt}/${maxRepairs})`);

                    logged = logExchange(chat, grammar, messages, logged);
                    let raw: string;
                    try {
                        raw = await chat.send(messages, token);
                    } catch (err: any) {
                        // A revision cut off (owner's report 2026-10-29: the connection broke two
                        // minutes into the third round, and the piece the second had written was lost
                        // with it): the best candidate so far is shown, saying so. With none, the
                        // error goes up as before.
                        if (attempt > 0 && (warned || flat)) {
                            cutOff = String(err?.message ?? err);
                            deps.log(`AI transform: the revision was cut off (${cutOff}); the candidate before it is shown.`);
                            break;
                        }
                        throw err;
                    }
                    logReply(raw);
                    // Read against the text the AI was last shown as the file: the file itself, or —
                    // asked again — its candidate (see `base`).
                    const parsed = toCandidateEdit(base, raw);
                    if (parsed !== null && !isProblem(parsed) && firstNote === undefined) {
                        firstNote = splitNote(parsed.reply).note;
                    }
                    if (parsed === null) {
                        turnedDown(attempt, `the reply was empty (${raw.length} characters)`);
                        messages.push({ role: 'assistant', content: raw });
                        messages.push({ role: 'user', content: `Your reply was empty. ${replyForm(whole, compose)} — no commentary, no code fences.` });
                        continue;
                    }
                    if (isProblem(parsed)) {
                        turnedDown(attempt, `its edits could not be applied: ${parsed.problem}`);
                        messages.push({ role: 'assistant', content: raw });
                        messages.push({ role: 'user', content: compose
                            ? `${parsed.problem}\nReply with the whole piece between <file> and </file> — same output rules.`
                            : `${parsed.problem}\nReply again, all edits — same output rules.` });
                        continue;
                    }
                    // What it makes of the candidate, as one change to the file: Accept writes the
                    // revision and the candidate it revised together.
                    let edit: CandidateEdit = base === work
                        ? parsed
                        : parsed.text === parsed.snap.origSelectedText
                            ? { ...parsed, snap: work, text: work.origSelectedText }
                            : { ...wholeFileEdit(work, spliceCandidate(parsed.snap, parsed.text)), reply: parsed.reply };
                    // A composed song's lyrics row, if the score left it out, goes in before the check
                    // (owner's report 2026-10-29): a row not shown is not counted against its notes, so
                    // the words drew nowhere and their extra syllables went unreported.
                    if (compose) {
                        const placed = withLyricsRows(spliceCandidate(edit.snap, edit.text));
                        if (placed.added.length > 0) {
                            edit = { ...wholeFileEdit(work, placed.text), reply: edit.reply };
                            deps.log(`AI transform: the score showed no row for ${placed.added.join(', ')} — added under the staff it sings.`);
                        }
                    }
                    // A reply that changes nothing is not a transform. The owner accepted one such
                    // candidate, the section exactly as it was, because nothing said it was
                    // unchanged (2026-09-26) — the model had been asked for a new part it could not
                    // write inside the selection.
                    if (edit.text === edit.snap.origSelectedText) {
                        turnedDown(attempt, 'it changed nothing');
                        unchanged = true;
                        messages.push({ role: 'assistant', content: raw });
                        messages.push({ role: 'user', content:
                            'That reply is identical to the file as it stands — it changes nothing. Carry out the '
                            + 'instruction. If it needs changes outside the selection (a new part, a score row, a new '
                            + 'declaration), reply with <edit> blocks.' });
                        continue;
                    }
                    // The way back to relative octaves gives each of the file's `octave` lines its own
                    // word again, so they must all still be there; without them every pitch would be
                    // read relative, and land elsewhere.
                    if (ai.converted && octaveLines(spliceCandidate(edit.snap, edit.text)) !== octaveLines(work.origFullText)) {
                        turnedDown(attempt, 'it added or removed an `octave` line');
                        messages.push({ role: 'assistant', content: raw });
                        messages.push({ role: 'user', content:
                            'Your change added or removed an `octave absolute` line. Keep the file\'s `octave` lines exactly '
                            + 'as they are — reply again, same output rules.' });
                        continue;
                    }
                    unchanged = false;

                    step('validating the candidate');
                    const reconstructed = spliceCandidate(edit.snap, edit.text);
                    const check = await client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: reconstructed });
                    const badness = candidateBadness(check, edit.spans, baselineErrors);
                    const newWarnings = badness.length === 0
                        ? addedWarnings(check, edit.spans, baselineWarnings)
                        : [];

                    if (badness.length === 0 && newWarnings.length === 0) {
                        // It compiles cleanly — now check where its notes actually LAND. An octave
                        // slip is valid Lily# and draws no diagnostic, so it is the compiler's
                        // resolved pitches, not its diagnostics, that show one. Asked once per
                        // candidate: returning the same candidate again is the model confirming the
                        // register is what the instruction wants ("two octaves lower").
                        // Only the notes the candidate wrote: between two edits stands music it left alone.
                        // A new piece (composed, or the whole file written anew) has no register of
                        // the old notes to keep.
                        const newPiece = compose || /<file>/.test(raw);
                        let written: ResolvedPitchFact[] = [];
                        if (edit.text !== octaveAsked && !newPiece) {
                            for (const s of edit.spans) {
                                written = written.concat(await pitchesOf(client, reconstructed, s.afterLo, s.afterHi));
                            }
                        }
                        const outliers = octaveOutliers(facts, written);
                        if (outliers.length > 0 && attempt < maxRepairs) {
                            octaveAsked = edit.text;
                            repairs = attempt + 1;
                            const range = facts.map(f => f.Resolved);
                            messages.push({ role: 'assistant', content: raw });
                            messages.push({ role: 'user', content:
                                `That candidate compiles, but check its octaves. The notes you replaced were `
                                + `${range.join(' ')}; the compiler resolves these of yours more than an octave outside `
                                + `that register:\n${outliers.map(p => `  - ${p.Written} -> ${p.Resolved}`).join('\n')}\n`
                                + (ai.converted
                                    ? 'The octaves are absolute: each \' / , counts from the part\'s anchor, not from the note before. '
                                    : 'Remember relative octaves: each note lands nearest the one before it, and the first note of a '
                                      + 'line is placed from whatever precedes it. ')
                                + 'Fix the octave marks, or, if this register is really '
                                + 'what the instruction asks for, return the same candidate again unchanged.' });
                            deps.log(`AI transform: octave check — ${outliers.map(p => `${p.Written}->${p.Resolved}`).join(' ')}`);
                            continue;
                        }
                        // A new piece is checked as music, once: parts with no instrument, an
                        // ensemble braced as a piano, one note value throughout, every part in one
                        // rhythm (owner's report 2026-10-26: a "dramatic quartet" of piano staves
                        // and whole notes). What it finds goes back for a revision of the whole piece.
                        if (newPiece && !musicAsked && attempt < maxRepairs) {
                            const music = await compositionFindings(client, reconstructed);
                            // One staff only when it was asked for (owner's rule 2026-10-27): a lead
                            // sheet, a melody alone, a solo — anything else has its accompaniment.
                            if (compose && partCount(reconstructed) < 2 && !asksForOneStaff(composeAsked ?? instruction)) {
                                music.unshift('The piece is one staff, and no lead sheet, melody alone or solo was asked for: '
                                    + 'give it its accompaniment — the parts its kind of music has (a jazz piece is a combo: '
                                    + 'piano or guitar, bass and drums under the melody instrument).');
                            }
                            if (music.length > 0) {
                                musicAsked = true;
                                flat = { edit, music, attempt };
                                repairs = attempt + 1;
                                turnedDown(attempt, `it compiles, but as music: ${music.join(' / ')}`);
                                messages.push({ role: 'assistant', content: raw });
                                messages.push({ role: 'user', content:
                                    `That piece compiles, but it falls short as music for its players:\n`
                                    + `${music.map(m => `- ${m}`).join('\n')}\n`
                                    + 'Revise the whole piece with these in mind — keep what works, make it sound like the '
                                    + 'ensemble asked for. Reply with the whole new file between <file> and </file>, same output rules.' });
                                continue;
                            }
                        }
                        candidate = edit; // valid — never showed a broken candidate
                        if (outliers.length > 0) {
                            octaveNote = outliers;
                        }
                        warned = null;
                        break;
                    }
                    repairs = attempt + 1;

                    // Self-repair: feed the diagnostics back and try again.
                    // Edits are answered against the file as it stands, not on top of the failed ones.
                    // A composed piece is answered as it was written, whole (owner's report 2026-10-29: asked
                    // for "the corrected edits, each <old> copied from the file", the composer replied with
                    // bare <old>/<new>, read as the file — twice — and the music check never ran).
                    const sameForm = compose
                        ? 'Reply with the whole corrected piece between <file> and </file>, every bar the report does not name as it was'
                        : edit.whole
                            ? 'Reply again with all the corrected edits, each <old> copied from the file as it stands'
                            : 'Return a corrected replacement for the selection (or <edit> blocks)';
                    messages.push({ role: 'assistant', content: raw });
                    turnedDown(attempt, badness.length > 0
                        ? `it does not compile (${badness.length} error(s), a ${raw.length}-character reply): ${formatDiags(badness.slice(0, 3)).replace(/\s*\n\s*/g, ' / ')}`
                        : `it adds ${newWarnings.length} warning(s): ${formatDiags(newWarnings.slice(0, 3)).replace(/\s*\n\s*/g, ' / ')}`);
                    if (badness.length > 0) {
                        messages.push({ role: 'user', content:
                            `That candidate does not compile. The Lily# compiler reported:\n${formatDiags(badness)}\n` +
                            `${sameForm} — same output rules.` });
                    } else {
                        if (compose || !warned || newWarnings.length < warned.warnings.length) {
                            warned = { edit, warnings: newWarnings, text: reconstructed, attempt };
                        }
                        messages.push({ role: 'user', content:
                            `That candidate compiles, but it adds warnings the file did not have:\n${formatDiags(newWarnings, reconstructed)}\n` +
                            `${sameForm} that adds no warnings — same output rules.` });
                    }
                }

                let caption = instruction;
                // A composed piece: the newer of the two wins, as long as it renders.
                const newerWarned = compose && warned !== null && (flat === null || warned.attempt > flat.attempt);
                if (candidate === null && flat && !newerWarned) {
                    candidate = flat.edit;
                    caption = `${instruction} — ⚠ ${flat.music.join(' / ')}`;
                    deps.log('AI transform: the revision did not come; shown as first written, with what the music check found.');
                }
                if (candidate === null && warned) {
                    candidate = warned.edit;
                    let marked = 0;
                    if (compose) {
                        // Each warning's line marked with a @todo carrying its words, a line at a time:
                        // a line that cannot take one (a lyric line) is left as it is.
                        let text = warned.text;
                        for (const m of warningTodoLines(text, warned.warnings, withTodoMark)) {
                            const lines = text.split('\n');
                            lines[m.line] = m.marked;
                            const tried = lines.join('\n');
                            const check = await client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: tried });
                            if (!check.HasErrors) {
                                text = tried;
                                marked++;
                            }
                        }
                        if (marked > 0) {
                            candidate = { ...wholeFileEdit(work, text), reply: warned.edit.reply };
                        }
                    }
                    caption = `${instruction} — ⚠ ${warned.warnings.length} new warning(s)`
                        + (marked > 0 ? `, marked @todo on ${marked} line(s)` : '') + ': '
                        + warned.warnings.map(w => w.Message).join(' / ');
                    deps.log(`AI transform: shown with ${warned.warnings.length} new warning(s) after ${maxRepairs} repairs: `
                        + warned.warnings.map(w => w.Message).join(' | '));
                }
                // Said first, so it is read first: what is shown is the version before a revision
                // that never came, not the AI's last word.
                if (candidate !== null && cutOff !== null) {
                    caption = caption.replace(' — ⚠ ', ' — ⚠ the AI\'s last revision was cut off (the connection broke), '
                        + 'so this is the version before it: ');
                }

                if (candidate === null) {
                    // §4 Failed: exhausted repairs — abort WITHOUT showing broken notation.
                    if (unchanged) {
                        deps.log(`AI transform: the model returned the file unchanged after ${maxRepairs} retries — nothing applied.`);
                        vscode.window.showWarningMessage(
                            'Lily#: the AI returned the music unchanged, even when asked again. Nothing was changed — try wording the request differently.');
                    } else {
                        deps.log(`AI transform: FAILED validation after ${maxRepairs} repairs — nothing applied.`);
                        // The last try's reason with it, and the log a click away.
                        const showLog = 'Show Log';
                        const why = lastReason.length > 240 ? `${lastReason.slice(0, 239)}…` : lastReason;
                        void vscode.window.showWarningMessage(
                            `Lily#: the AI could not produce a valid transform in ${maxRepairs + 1} tries. Nothing was changed.`
                            + (why ? ` The last try: ${why}` : ''), showLog)
                            .then(c => { if (c === showLog) { deps.showLog?.(); } });
                    }
                    return null;
                }
                if (octaveNote) {
                    caption = `${caption} — ⚠ check the octave: ${octaveNote.map(p => `${p.Written} → ${p.Resolved}`).join(', ')}`;
                }
                if (repairs > 0 && caption === instruction) {
                    deps.log(`AI transform: candidate valid after ${repairs} self-repair round(s).`);
                }
                if (candidate.whole) {
                    deps.log(`AI transform: the model edited the file in ${candidate.spans.length} place(s) `
                        + `(${candidate.spans.map(s => `${s.beforeLo}..${s.beforeHi}`).join(', ')} of the original).`);
                }

                // ----- Render the candidate score and decide on it (§3 [7]) -----
                step('rendering the candidate');
                const reconstructed = spliceCandidate(candidate.snap, candidate.text);
                const renderAfter = await client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: reconstructed });
                // Source-offset spans of the change so the panel can highlight WHERE it landed —
                // each edit's, in the "after" score and in the "before" one. The SVG carries
                // data-pos (source offsets) for editor↔preview sync, so the webview lights up the
                // notes whose data-pos falls in these spans.
                // Word by word, so a note the edit copied through unchanged is not lit.
                const changed = changedWords(candidate.snap.origFullText, reconstructed, candidate.spans);
                return token.isCancellationRequested ? null : { candidate, caption, renderAfter, changed, firstNote };
            });
        } catch (err: any) {
            if (!shownRound || !candidatePanel) {
                throw err;
            }
            failure = String(err?.message ?? err);
            deps.log(`AI transform: asking again failed — ${failure}`);
            round = null;
        }
        // Set while the panel still shows this candidate: what its footer says as it waits again.
        let again: string | undefined;
        if (!round) {
            if (!shownRound || !candidatePanel) {
                return;
            }
            // Back to the candidate shown, the conversation as it was before the ask.
            prepared!.messages.length = askedFrom;
            base = baseAsked;
            round = shownRound;
            again = failure !== undefined
                ? `Asking again failed (${failure}) — this candidate is as it was.`
                : 'No new candidate — this one is as it was.';
        }
        shownRound = round;
        // The notification has closed: the review is the user's, with nothing over it.
        const { candidate, caption, renderAfter, changed } = round;
        if (compose) {
            // A composed piece replaces nothing, so there is nothing to set it beside: it opens in
            // an editor of its own (owner's request 2026-10-27). What was asked and what the AI says
            // of it head the file as comments — a preface kept with the piece, where a notification
            // went in seconds (owner, the same day: "I want to see the AI's comment").
            // The first answer's word (none, if it said none), not a repair's.
            const note = round.firstNote !== undefined ? round.firstNote : splitNote(candidate.reply).note;
            const flatNote = caption.startsWith(instruction) ? caption.slice(instruction.length).replace(/^ — /, '') : '';
            let text = spliceCandidate(candidate.snap, candidate.text);
            // A composed piece always has a title that fits it (owner's request 2026-10-27): asked
            // for in the request and by the music check; if it still comes without one, the AI is
            // asked for the title alone, and it is written in.
            const had = titleOf(text);
            if (!had || isPlaceholderTitle(had)) {
                const title = await titleFor(chat, text, composeAsked ?? instruction);
                if (title) {
                    text = withTitle(text, title);
                    deps.log(`AI transform: the piece came untitled — titled "${title}".`);
                }
            }
            // Words written and never shown (owner's report 2026-10-29: `lyrics words sings lead` in
            // every section, no `lyrics words` row in the score — the words were not drawn): the row
            // goes under the staff the words sing.
            const placed = withLyricsRows(text);
            if (placed.added.length > 0) {
                text = placed.text;
                deps.log(`AI transform: the score showed no row for ${placed.added.join(', ')} — added under the staff it sings.`);
            }
            text = await asMusicianWrites(deps, client, text);
            const eol = text.includes('\r\n') ? '\r\n' : '\n';
            const line = (s: string) => `// ${s.replace(/\s*\r?\n\s*/g, ' ')}`;
            const preface = [
                // The model that answered, not "copilot/auto" (owner's request 2026-10-29).
                line(`Composed with AI (${chat.answeredBy?.() ?? chat.label}), ${new Date().toISOString().slice(0, 10)}`),
                line(`Asked: ${composeAsked ?? instruction}`),
                ...(note ? [line(`The AI: ${note}`)] : []),
                ...(flatNote ? [line(`Still to improve: ${flatNote}`)] : []),
            ].join(eol);
            const piece = await vscode.workspace.openTextDocument({ language: 'lilysharp', content: `${preface}${eol}${eol}${text}` });
            await vscode.window.showTextDocument(piece);
            deps.log('AI transform: composed — opened in a new editor.');
            void vscode.window.showInformationMessage(
                'Lily#: the AI composed a new score — its note is at the top. Save it to keep it; Ctrl+I asks for changes.');
            return;
        }
        const { renderBefore, messages } = prepared!;
        // The review, and Iterate's question about it: a refine closed or left empty goes back
        // to the same candidate on its score, to be decided again (owner's request 2026-10-25 —
        // it used to end the transform, dropping the candidate unapplied).
        // An Accept that wrote nothing goes back to the candidate too, its buttons armed again
        // and the reason in the footer (owner's report 2026-10-25: they stayed greyed out).
        let decision: Decision;
        let refine: string | undefined;
        for (;;) {
            decision = again !== undefined
                ? await decideAgain(again)
                // The whole text, its changes marked — the green finds them in it (owner's request
                // 2026-10-26: the replacement alone was harder to place); the panel opens it at the first.
                : await reviewOnScore(deps, renderBefore, renderAfter, caption,
                    wholeTextMarkup(candidate.snap.origFullText, spliceCandidate(candidate.snap, candidate.text), changed),
                    changed, {
                    uri: snapshot.uri,
                    before: work.origFullText,
                    after: spliceCandidate(candidate.snap, candidate.text),
                }, splitNote(candidate.reply).note);
            again = undefined;
            if (decision === 'accept') {
                // ----- Accept: apply with the version/range guard (§7) -----
                deps.log('AI transform: accepted.');
                try {
                    again = await accept(candidate);
                } catch (err: any) {
                    // The language server gone mid-write (it restarts on its own): nothing was
                    // written, and Accept tries again.
                    deps.log(`AI transform: accept failed — ${err?.message ?? err}`);
                    again = `Not written (${err?.message ?? err}) — Accept to try again.`;
                }
                if (again === undefined) {
                    return;
                }
                continue;
            }
            if (decision !== 'iterate') {
                break;
            }
            refine = await vscode.window.showInputBox({
                title: 'Lily# — ask the AI again',
                prompt: 'What should the AI change about this candidate? It answers with a new one. (Esc returns to this candidate)',
                placeHolder: 'e.g. keep the rhythm but a third lower / less busy',
                ignoreFocusOut: true,
            });
            if (refine !== undefined && refine.trim().length > 0) {
                break;
            }
            deps.log('AI transform: refine cancelled — back to the candidate.');
            again = '';
        }

        if (decision === 'reject') {
            deps.log('AI transform: rejected on the score.');
            // Reject closes the panel, so the click is seen to have done something.
            candidatePanel?.dispose();
            vscode.window.setStatusBarMessage('Lily#: AI candidate rejected — the file is unchanged.', 4000);
            return;
        }
        if (decision === 'iterate' && refine !== undefined) {
            deps.log(`AI transform: iterate — "${refine}"`);
            panelStatus(`Asking the AI again: “${refine}”…`);
            askedFrom = messages.length;
            baseAsked = base;
            messages.push({ role: 'assistant', content: candidate.reply });
            // A revision is of the candidate: the AI answers against the file as its candidate left
            // it — its <old> quotes what it wrote as readily as what it kept — and Accept writes the
            // whole of it, the candidate and the revision as one change.
            base = revisionBase(work, candidate);
            messages.push({ role: 'user', content:
                `Revise your previous candidate: ${refine}\nThe file now reads as your candidate left it — copy each <old> `
                + 'from that (your new text where you changed something, the file\'s text where you did not). '
                + `${replyForm(whole)} — same output rules.` });
            instruction = refine; // caption reflects the latest ask
            continue; // back to generate
        }
        iterate = false;
    }

    /** Writes the candidate into the file; undefined when written, else why not. */
    async function accept(candidate: CandidateEdit): Promise<string | undefined> {
        if (ai.converted) {
            // Back to the file's relative octaves, applied as the span where the file changes:
            // the AI's edits, and the notes after them whose marks follow from new neighbours.
            const back = await fromAbsoluteOctaves(deps, client, spliceCandidate(candidate.snap, candidate.text), snapshot.origFullText);
            if (back === undefined) {
                deps.log('AI transform: not applied (it could not be brought back to relative octaves).');
                return 'Not written — it could not be brought back to the file\'s relative octaves.';
            }
            const edit = wholeFileEdit(snapshot, back);
            return applyCandidate(deps, edit.snap, edit.text);
        }
        return applyCandidate(deps, candidate.snap, candidate.text);
    }
}

// ======================================================================
// Prompt construction (§5)
// ======================================================================

/**
 * The composer's system prompt (owner's report 2026-10-27: the same model asked in a chat writes a
 * fair piece, asked from VS Code a poor one). The editor's prompt casts it as a transformer of
 * fragments, under rules for the reply's form; a composer is told it is one, plans the piece where
 * the musician does not see it (<plan>, unread — a chat's model thinks before it writes; one
 * called from VS Code may not), and writes it in absolute octaves, its parts' anchors named.
 * The grammar's sketches show the notation, not a piece's length or form: the "Compose a piece"
 * recipe once carried three 8-bar pieces, and what came back was 8 bars with a tempo per section.
 */
function composeSystemPrompt(grammar: string, modelName?: string, guide: string = cachedComposeGuide ?? ''): string {
    return [
        'You are a composer. You write a complete new piece of music for the musician who asks, and you write it',
        'down in Lily#, a text music-notation language. Lily# is NOT LilyPond; obey the grammar below exactly.',
        // Who it is (owner's report 2026-10-27: asked to fill in the composer, a model never told its
        // name wrote "OpenAI"). Nothing more is said of the composer line (owner's decision, same day:
        // it is not to be filled in unasked).
        ...(modelName
            ? [`You are ${modelName}. If the musician asks for the composer to be written, the composer is you: `
                + `\`composer "${modelName}"\`.`]
            : []),
        '',
        '<lilysharp-grammar>',
        grammar,
        '</lilysharp-grammar>',
        '',
        // The composing guide, after the grammar: what makes a piece music for its players, the
        // kinds of music, the setting of words (docs/COMPOSE_FOR_LLM.md; owner's decision 2026-10-29).
        ...(guide ? ['<composing-guide>', guide, '</composing-guide>', ''] : []),
        'Compose it as music for players, as you would write it out for a performer: a form, a harmony, a theme',
        'and an ending, at the length the music needs. The grammar\'s snippets only show how a thing is written;',
        'none of them is a model of a piece\'s length, form or style.',
        // Owner's wish 2026-10-29: rules held too tight make every piece the same — compose with pleasure.
        'The composing guide\'s ways are a composer\'s habits, not rules: break one where the piece is better',
        'for it. Write a piece you would enjoy hearing, with choices of your own.',
        '',
        'REPLY in this order, and nothing else:',
        // Headings, every one filled in, and the theme in notes (owner's request 2026-10-29: the plan
        // of a poor piece named its chords bar by bar, but its theme only in words — "a rising broken
        // chord and a falling figure" — and no bar of the piece was made of one; its phrases did not
        // answer each other, its rhythm was quarters, it had no high point, its coda was planned quiet
        // and written f, and the character asked for was never planned).
        '1. <plan> … </plan> — your working notes; the musician never sees them. Fill in every heading below, in',
        '   this order, before any note is written — a heading left vague is a decision not made:',
        // The headings say what to decide; why and how are the composing guide's, said there once
        // (owner's request 2026-10-29: the prompt and the guide said the same things twice). The
        // decisions behind each, owner's trials 2026-10-29: a piece of 32 bars padded out; an
        // "Aprime" the same as A, copied; a theme planned only in words, no bar made of it; phrases
        // that did not answer; no high point; a "dramatic" quartet with none of its character
        // written; words cut to fit the notes ("ひ を も や" for 燃やす).
        // Owner's trial 2026-10-29: a quartet of four 4-bar sections, 16 bars, its dynamics changing
        // every four — about 24 to 32 bars suggested, not required (owner's wish, the same day).
        '   LENGTH AND FORM — the sections and their bars (about 24 to 32 bars unless the request gives a length,',
        '   or what the piece needs); a section that comes back as it was is named again in the form',
        '   (`form { A B A Coda }`), never copied.',
        '   KEY, METER, TEMPO.',
        '   HARMONY — every phrase\'s chords, bar by bar, and its cadence.',
        '   THEME — its one or two bars WRITTEN OUT here in this file\'s notes, its rhythm with them',
        '   (`fis\'\'2 e\'\'8 d\'\'8 | d\'\'4 b\'4 g\'\'4 |`), and the bars where it comes back.',
        '   PHRASES — for each phrase its bars, the note it starts on, its highest note and the note it ends on over',
        '   its cadence (a pair answering, as the guide says).',
        '   CLIMAX — its bar and note, and how the dynamics lead there and away.',
        '   PARTS — what each part does in each section (melody, bass line, inner parts, the accompaniment figure',
        '   and its rhythm) and its register, as sounding pitches (C4 = middle C) AND as you will write them in',
        '   this file (a right hand round C5, D4–A5, is written `d\'` – `a\'\'`).',
        '   DYNAMICS — the level each phrase starts at and the hairpins between.',
        '   CHARACTER — each device of the character asked for, with how it is written in this file and the bars',
        '   it is in (`@sf` in bar 13, a tremolo `d\'2:32` in bars 14–15, …).',
        '   WORDS (a song only) — each line of the words written out WHOLE first, its sung units counted; then the',
        '   phrase that sings it, given that many notes (the guide\'s "A song with words").',
        '   ENDING — in the home key, on a held tonic chord, at the dynamic planned for it.',
        '2. <note> … </note> — one or two sentences to the musician about the piece, warm and plain, in the language',
        '   of the request.',
        '3. <file> … </file> — the whole file, written from the plan.',
        '',
        // Lily#'s octaves are LilyPond's since 2026-10-28 (owner's decision): the per-instrument
        // anchors this listed, and the warning that a LilyPond habit wrote right hands an octave
        // high, went with the anchors themselves.
        '- Write in ABSOLUTE octaves: `octave absolute` at the top of the file, so every pitch says where it sounds.',
        '  The octaves are LilyPond\'s, in every part and for every instrument: `c` = C3, `c\'` = middle C (C4),',
        '  `c\'\'` = C5, `c,` = C2.',
        // A part is planned in the pitches it sounds, and the AI wrote it so — a bass an octave
        // under its page, a trumpet a tone (owner's trials 2026-10-28). Under `pitch concert` every
        // letter IS the sounding pitch, transposing instruments included; Lily# prints each as its
        // player reads it.
        '- Write at CONCERT pitch: `pitch concert` at the top of the file too, and every part by the pitch it',
        '  SOUNDS, whatever the instrument — a bass\'s low E string is `e,,` (E1), a guitar\'s `e,` (E2), a B♭ trumpet\'s',
        '  middle C `c\'`. Lily# prints each part where its player reads it.',
        // Owner's decision 2026-10-27, which the prompt did not say (owner's trials 2026-10-29: two
        // pieces through Copilot's Auto wrote `composer "OpenAI"` unasked).
        '- Write no `composer` line unless the musician asks for one.',
        '- Name the parts as a score prints them, by the instrument. A piece for piano alone names no part; a piano',
        '  in an ensemble is named once, on its upper part (`"Piano" "Pno."`). Never "Right hand", "L.H.", 右手, 左手.',
        '- Before you reply, go through the piece bar by bar: every bar of every part fills the meter, the parts',
        '  agree on each bar\'s harmony, each part stays in its register, and the last bar is the tonic. Then hold',
        '  the file to the plan: the theme stands in its bars as written there, each phrase starts, peaks and ends',
        '  on its planned notes, the climax, the dynamics and every device of the character are written where',
        '  the plan put them. Where they differ, change the file (or the plan) until they agree.',
        '- If the musician sends you findings about a draft, revise the whole piece with them in mind, in the same',
        '  three blocks.',
        '- Never emit LilyPond-only constructs (\\relative, \\new Staff, \\repeat volta, \\version, << \\\\ >>). Annotations use @name, never \\name.',
    ].join('\n');
}

function systemPrompt(grammar: string): string {
    return [
        'You transform fragments of a musical score written in Lily#, a text music-notation language.',
        'Lily# is NOT LilyPond; obey the grammar below exactly.',
        '',
        '<lilysharp-grammar>',
        grammar,
        '</lilysharp-grammar>',
        '',
        'OUTPUT CONTRACT (strict — the result is applied to the file mechanically). Reply in ONE of three forms:',
        '1. The change stays inside the selection: return ONLY the replacement text for the selected fragment.',
        '   Do not repeat the surrounding score.',
        '2. The change needs edits OUTSIDE the selection — a new part (its `part` line, its block in the',
        '   section, its row in the score), a new declaration, a score row — or nothing is selected: reply with',
        '   one or more edit blocks and nothing else:',
        '   <edit>',
        '   <old>',
        '   text copied EXACTLY from the file (every character, spaces included), found there exactly once',
        '   </old>',
        '   <new>',
        '   what replaces it',
        '   </new>',
        '   </edit>',
        '   Keep each <old> short — the lines that change, plus a neighbouring line when needed to make it',
        '   unique. To insert, copy a line next to the place into <old> and repeat it in <new> with the',
        '   insertion. Edits must not overlap. Never write the whole file out for a change that keeps most of it.',
        '   Do not reach for `voice { }` just to stay inside the selection when a separate part is asked for.',
        // Owner's report 2026-10-26: "discard the score and compose a string quartet" came back as
        // edits whose <old> quoted the old score inexactly, three times, and nothing was written.
        '3. The request discards the score, or replaces nearly all of it (a new piece, "start over", "compose',
        '   … from scratch"): reply with the whole new file between <file> and </file> — every line of it,',
        '   header, parts, sections, form and score included, and the file\'s `octave` lines kept as they are.',
        '   Nothing of the old file survives that you do not write out.',
        '- Either way, start with ONE short note to the musician between <note> and </note>: what you did',
        '  and why, at most two sentences, warm and plain, in the language the instruction is written in',
        '  (Japanese for a Japanese instruction). Apart from the note: no explanations, no commentary, no',
        '  Markdown, no code fences, no selection markers.',
        // Owner's report 2026-10-25: "add chords" came back as @chord on every note. The answers
        // to everyday requests live in the grammar's "Common requests" section.
        '- An everyday request ("add chords", "add lyrics", "add a second verse", "make the chords strum",',
        '  "add a bass line", "add tab", "repeat this",',
        '  "transpose", in any language — "コードをつけて" is "add chords") is answered with the construct the',
        '  grammar\'s "Common requests" section gives for it.',
        '- A reply identical to the input is wrong: carry out the instruction.',
        '- Check every octave. Octaves are relative unless the file says `octave absolute`: each note lands nearest',
        '  the note before it, so the first note of a new line or voice decides where the whole line sits. The',
        '  compiler resolves your notes and you will be shown any that land far from the selection\'s register.',
        '- Preserve the fragment\'s shape: if it spans lines, keep one statement per line and end every measure with "|".',
        '- Never emit LilyPond-only constructs (\\relative, \\new Staff, \\repeat volta, \\version, << \\\\ >>). Annotations use @name, never \\name.',
    ].join('\n');
}

const MAX_DIAGNOSTICS = 20;

/** What to reply with, for the reminders: with nothing selected, edits only. */
function replyForm(whole: boolean, compose = false): string {
    if (compose) {
        return 'Reply with <plan>…</plan>, <note>…</note> and the whole piece in <file>…</file>';
    }
    return whole
        ? 'Reply with <edit> blocks (or, when the score is replaced by a new one, the whole new file in <file>…</file>)'
        : 'Return the replacement for the selection, or <edit> blocks when the change reaches outside it';
}

/**
 * What the compiler says about the file as it stands — errors and warnings, those inside or
 * touching the selection first. The instruction is often about one of them ("fix this bar")
 * and the model could not see any.
 */
function diagnosticsContext(snapshot: Snapshot, baseline: CandidateDiagnostic[], whole: boolean): string | null {
    const shown = baseline.filter(d => d.Severity === 'error' || d.Severity === 'warning');
    if (shown.length === 0) {
        return null;
    }
    const inSel = (d: CandidateDiagnostic) => !whole
        && d.Offset < snapshot.endOffset && d.Offset + Math.max(1, d.Length) > snapshot.startOffset;
    const ordered = [...shown.filter(inSel), ...shown.filter(d => !inSel(d))];
    const lines = ordered.slice(0, MAX_DIAGNOSTICS).map(d =>
        `  - line ${d.Line + 1}${inSel(d) ? ' (in the selection)' : ''}: ${d.Severity}`
        + `${d.Code ? ` ${d.Code}` : ''}: ${d.Message}`);
    if (ordered.length > MAX_DIAGNOSTICS) {
        lines.push(`  - … and ${ordered.length - MAX_DIAGNOSTICS} more`);
    }
    return lines.join('\n');
}

/**
 * The model used to see the selected text alone — no key, no meter, no part, no clef, no bars
 * around it — and so "harmonize a third above" or "make the bar fill its meter" was answered
 * blind. It is shown the file (`shown`: whole when the model's input holds it), the selection
 * marked in it; with nothing selected, the file alone, once.
 */
function taskPrompt(snapshot: Snapshot, shown: { text: string; complete: boolean }, whole: boolean,
    facts: ResolvedPitchFact[], baseline: CandidateDiagnostic[], instruction: string,
    marks: string | null = null, absolute = false, compose = false): string {
    const parts: string[] = [];
    if (absolute) {
        // toAbsoluteOctaves: the user's file is relative; it was converted for the model.
        parts.push('The file is shown in ABSOLUTE octaves (`octave absolute` at its top): every pitch says where it '
            + 'sounds — LilyPond\'s octaves: a bare `c` is C3 and `c\'` middle C (unless the part writes `octave N`), and each \' / , moves it one octave from there, never from the note '
            + 'before. Write every pitch you add or change the same way, and keep the file\'s `octave` lines as they are.');
        parts.push('');
    }
    const partial = shown.complete ? '' : ' Parts of it are omitted, marked "…"; leave them alone.';
    if (whole) {
        parts.push(`The file being edited — nothing is selected, so the instruction is about the whole score.${partial}`);
    } else {
        parts.push(`The file being edited, with the selection marked ${SEL_OPEN} … ${SEL_CLOSE} `
            + `(read the key, meter, parts and neighbouring bars from it; the markers are not part of the file).${partial}`);
    }
    parts.push('<document>');
    parts.push(shown.text);
    parts.push('</document>');
    if (!whole) {
        parts.push('');
        parts.push('Selected fragment to transform:');
        parts.push('<selection>');
        parts.push(snapshot.origSelectedText);
        parts.push('</selection>');
    }
    const diags = diagnosticsContext(snapshot, baseline, whole);
    if (diags) {
        parts.push('');
        parts.push('What the Lily# compiler reports about the file as it stands (before your change):');
        parts.push(diags);
        parts.push('If the instruction is about one of these, fix it. Either way, add no new errors or warnings.');
    }
    if (facts.length > 0) {
        parts.push('');
        parts.push('Resolved absolute pitches of the selection (written -> resolved), from the compiler:');
        parts.push(facts.map(f => `  ${f.Written} -> ${f.Resolved}`).join('\n'));
    }
    if (marks) {
        parts.push('');
        parts.push(marks);
    }
    parts.push('');
    parts.push(`Instruction: ${instruction}`);
    parts.push('');
    parts.push(`${replyForm(whole, compose)}.`);
    return parts.join('\n');
}

// ======================================================================
// Candidate cleaning & validation
// ======================================================================

/** A candidate as the edit it makes (aiTransformCore.ts), against this module's snapshot. */
type CandidateEdit = CoreCandidateEdit<Snapshot>;

/** A title for a piece that came without one: one short request to the model that wrote it,
 *  the piece and the musician's words shown; null when it gives none usable. */
async function titleFor(chat: ChatClient, piece: string, asked: string): Promise<string | null> {
    try {
        const reply = await chat.send([{ role: 'user', content:
            `Here is a piece of music written in Lily# for this request: "${asked}".\n`
            + 'Reply with ONLY a title for it — a few words that fit its music and its mood, in the language of the '
            + 'request. No quotes, no label, no commentary.\n\n' + piece.slice(0, 8000) }],
            new vscode.CancellationTokenSource().token);
        return cleanTitle(reply);
    } catch {
        return null;
    }
}

/** What Copilot's chat hands the chords tool. */
interface ReadChordsInput {
    /** The .lys file's absolute path; none: the score open in the editor. */
    file?: string;
    /** The parts to read, by name; none: all of them, and the chords rows. */
    parts?: string[];
}

/**
 * A score's chord progression, section by section as a chords block, from the server
 * (lilysharp/readChords): what the score names kept as written, the rest read from the notes.
 * It writes nothing.
 */
async function readChords(deps: AiTransformDeps, input: ReadChordsInput | undefined): Promise<string> {
    const active = vscode.window.activeTextEditor?.document;
    const doc = input?.file ? await scoreDocument(input.file)
        : active?.languageId === 'lilysharp' ? active : undefined;
    if (!doc) {
        return input?.file ? `No chords read: no score ${input.file} is open in the editor or on the disk.`
            : 'No chords read: no score is open in the editor. Give `file` (the .lys file\'s absolute path).';
    }
    const client = deps.getClient();
    if (!client) {
        return 'No chords read: the Lily# language server is not running.';
    }
    try {
        const parts = Array.isArray(input?.parts) ? input.parts.filter(p => typeof p === 'string' && p.trim().length > 0) : [];
        // In the octaves lilysharp_readScore shows the score in: the chords it lists as written are
        // copied into an edit's `old`, which is matched against that text.
        const standing = doc.getText();
        const answer = await client.sendRequest<{ Text: string }>('lilysharp/readChords', { Text: await chatAbsolute(deps, standing) ?? standing, Parts: parts });
        return `${path.basename(doc.uri.fsPath)}, as it stands in the editor. ${answer.Text}`;
    } catch (e) {
        return `No chords read: the Lily# language server did not answer (${e instanceof Error ? e.message : String(e)}).`;
    }
}

/** What Copilot's chat hands the review tool. */
interface ReviewScoreInput {
    /** The .lys file's absolute path; none: the score open in the editor. */
    file?: string;
    audience?: string;
    focus?: string;
}

/**
 * The review's report on a score, its reading instructions in front (owner's proposal 2026-10-30,
 * docs/review-tool-design.md): the checks made for composing, for a person who asks "review this
 * piece", explained by the chat in plain words. It writes nothing.
 */
async function reviewScore(deps: AiTransformDeps, input: ReviewScoreInput | undefined): Promise<string> {
    const active = vscode.window.activeTextEditor?.document;
    const doc = input?.file ? await scoreDocument(input.file)
        : active?.languageId === 'lilysharp' ? active : undefined;
    if (!doc) {
        return input?.file ? `Nothing reviewed: no score ${input.file} is open in the editor or on the disk.`
            : 'Nothing reviewed: no score is open in the editor. Give `file` (the .lys file\'s absolute path).';
    }
    const client = deps.getClient();
    if (!client) {
        return 'Nothing reviewed: the Lily# language server is not running.';
    }
    try {
        const resp = await client.sendRequest<{ Report: string }>('lilysharp/reviewScore', { Text: doc.getText(), Audience: input?.audience, Focus: input?.focus });
        return `${REVIEW_READING}\n\nThe report on ${path.basename(doc.uri.fsPath)}, as it stands in the editor:\n${resp.Report}`;
    } catch (e) {
        return `Nothing reviewed: the Lily# language server did not answer (${e instanceof Error ? e.message : String(e)}).`;
    }
}

/** What a new piece's notation leaves flat (lilysharp/compositionCheck), one sentence each;
 *  none from a server that does not know the request. */
async function compositionFindings(client: LanguageClient, text: string): Promise<string[]> {
    return (await compositionReport(client, text)).findings;
}

/** The music check's findings, and each part's range as one line ("melody: C4–B4, most of its
 *  time E4–G4") — what the piece is, for a chat to read back what it wrote (owner's request
 *  2026-10-29: a beginner's piece meant for C4–G4 reached B4 by an octave slip). */
async function compositionReport(client: LanguageClient, text: string): Promise<{ findings: string[]; mistakes: string[]; ranges: string[] }> {
    try {
        const resp = await client.sendRequest<{ Findings: string[]; Mistakes?: string[]; Ranges?: string[] }>('lilysharp/compositionCheck', { Text: text });
        return { findings: resp?.Findings ?? [], mistakes: resp?.Mistakes ?? [], ranges: resp?.Ranges ?? [] };
    } catch {
        return { findings: [], mistakes: [], ranges: [] };
    }
}

/** The ranges as a chat is told them: a fact to check against what was meant, not a finding. */
function rangesSaid(ranges: string[]): string {
    return ranges.length === 0 ? ''
        : `\nThe parts' ranges, as written (check them against what you meant — an octave mark slips easily): ${ranges.join('; ')}.`;
}

/**
 * The file as `candidate` left it, as the snapshot a revision of it is read against: its text,
 * and the selection where it now stands — moved by the candidate's changes before it, and
 * widened to a change it falls inside — so a reply that replaces the selection replaces the same
 * music.
 */
function revisionBase(work: Snapshot, candidate: CandidateEdit): Snapshot {
    const full = spliceCandidate(candidate.snap, candidate.text);
    const { start: startOffset, end: endOffset } = carrySelection(candidate.spans, work.startOffset, work.endOffset);
    return { ...work, origFullText: full, startOffset, endOffset, origSelectedText: full.slice(startOffset, endOffset) };
}

/** Rebuilds the full document text with the candidate spliced into the snapshot range. */
function spliceCandidate(snapshot: Snapshot, candidate: string): string {
    return snapshot.origFullText.slice(0, snapshot.startOffset)
        + candidate
        + snapshot.origFullText.slice(snapshot.endOffset);
}

/** The resolved pitches of [start, end) of an unapplied candidate file (lilysharp/pitchesForText). */
async function pitchesOf(client: LanguageClient, text: string, start: number, end: number): Promise<ResolvedPitchFact[]> {
    try {
        const resp = await client.sendRequest<FactsForRangeResponse>('lilysharp/pitchesForText', { Text: text, Start: start, End: end });
        return resp.Error ? [] : (resp.Pitches ?? []);
    } catch {
        return []; // an older server without the request: no octave check, as before
    }
}

async function checkText(client: LanguageClient, text: string): Promise<CandidateDiagnostic[]> {
    try {
        const check = await client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: text });
        return check.Diagnostics ?? [];
    } catch {
        return [];
    }
}

function count(diags: CandidateDiagnostic[], severity: string): number {
    return diags.filter(d => d.Severity === severity).length;
}

/** A warning about the machine (a font the host lacks), not about the source. */
const MACHINE_WARNING = /is not installed on this system/;

/** A bar short or over its meter (LYS2001, LYS2002), the parts' bars of different lengths (LYS2004),
 *  a first bar short with no `partial` (LYS2006), a section of more bars in one part than another
 *  (LYS2007 — owner's decision 2026-10-30: a chorus's guitar played 9 bars to the bass and drums'
 *  8, and the band stopped for a bar), a span's later voice running past its first (LYS2019 — the
 *  same day: a drum part's feet line ran 18 bars past the song): turned down from the chat as an
 *  error is. */
const BAR_LENGTH_CODES = new Set(['LYS2001', 'LYS2002', 'LYS2004', 'LYS2006', 'LYS2007', 'LYS2019']);

/** A note its part's instrument cannot sound (LYS1052): turned down from the chat as a bar that
 *  does not add up is (owner's decision 2026-10-30). */
const OUT_OF_RANGE_CODE = 'LYS1052';

/** A syllable with no note to sing it on (LYS4001), left off the page: turned down from the chat
 *  every time too (owner's decision 2026-11-01: a chorus's last word, the よ of またあるくよ, was
 *  lost, the warning told once and the piece written). A word lost is never meant. */
const LOST_SYLLABLE_CODE = 'LYS4001';

/**
 * The warnings a candidate ADDS, judged the way errors are: the file with the candidate has
 * more warnings in all than the untouched file had. Returned are the ones to show the model —
 * those in the replaced span, else those at or after it. Machine warnings are not counted.
 */
function addedWarnings(
    check: CheckCandidateResponse,
    spans: ChangedSpan[],
    baselineWarnings: number,
): CandidateDiagnostic[] {
    const warnings = check.Diagnostics.filter(d => d.Severity === 'warning' && !MACHINE_WARNING.test(d.Message));
    if (warnings.length <= baselineWarnings) {
        return [];
    }
    const inRange = warnings.filter(d => inSpans(spans, d.Offset));
    const candStart = spans[0]?.afterLo ?? 0;
    const relevant = inRange.length > 0 ? inRange : warnings.filter(d => d.Offset >= candStart);
    return relevant.length > 0 ? relevant : warnings;
}

/** Whether a candidate-file offset is in one of the places the candidate wrote. */
function inSpans(spans: ChangedSpan[], offset: number): boolean {
    return spans.some(s => offset >= s.afterLo && offset < s.afterHi);
}

/**
 * Decides whether a candidate is "broken" and returns the diagnostics to feed back
 * for repair. A candidate is broken only if it INTRODUCES errors: more total errors
 * than the untouched document, or an error inside the replaced span. Pre-existing
 * errors elsewhere are not the candidate's fault, so they don't block it.
 */
function candidateBadness(
    check: CheckCandidateResponse,
    spans: ChangedSpan[],
    baselineErrors: number,
): CandidateDiagnostic[] {
    const errors = check.Diagnostics.filter(d => d.Severity === 'error');
    const candStart = spans[0]?.afterLo ?? 0;
    const inRange = errors.filter(d => inSpans(spans, d.Offset));
    const introducedMore = errors.length > baselineErrors;
    if (inRange.length === 0 && !introducedMore) {
        return [];
    }
    // Prefer the in-range diagnostics for repair feedback; if none are in-range but
    // the count went up, hand back everything at/after the splice point.
    const relevant = inRange.length > 0 ? inRange : errors.filter(d => d.Offset >= candStart);
    return relevant.length > 0 ? relevant : errors;
}

function formatDiags(diags: CandidateDiagnostic[], text?: string): string {
    return diagLines(diags, d => text ? spotOf(d, text) + rangeFixOf(d, text) : '');
}

/** Where on a long line a diagnostic stands, its text quoted (owner's report 2026-10-30: "line 159"
 *  of a drum line of 70 phrases did not say at which one bar 57 starts, and the chat's agent
 *  counted them in a terminal script). A short line is its own answer. */
function spotOf(d: CandidateDiagnostic, text: string): string {
    const start = d.Offset - d.Char;
    const end = text.indexOf('\n', start);
    const line = text.slice(start, end < 0 ? text.length : end).trimEnd();
    if (line.length <= 100 || d.Char >= line.length) {
        return '';
    }
    const at = line.slice(d.Char, d.Char + 40);
    return ` [here is column ${d.Char + 1}: \`${at}${d.Char + 40 < line.length ? '…' : ''}\`]`;
}



// ======================================================================
// Resolved facts
// ======================================================================

async function getFacts(client: LanguageClient, snapshot: Snapshot, token: vscode.CancellationToken): Promise<ResolvedPitchFact[]> {
    try {
        const resp = await client.sendRequest<FactsForRangeResponse>('lilysharp/factsForRange', {
            textDocument: { uri: snapshot.uri.toString() },
            start: snapshot.startOffset,
            end: snapshot.endOffset,
        });
        return resp.Error ? [] : (resp.Pitches ?? []);
    } catch {
        return [];
    }
}

interface TodosResponse { Todos?: { Key?: string | null; Memo?: string | null; HostStart: number; HostEnd: number }[]; Error?: string | null }

/** The selection's @todo marks (lilysharp/todos) with the readings an OMR reader's side file
 *  offers for them, as the prompt's section; null when the selection carries none. Best-effort. */
async function getMarks(client: LanguageClient, snapshot: Snapshot): Promise<string | null> {
    try {
        const resp = await client.sendRequest<TodosResponse>('lilysharp/todos', { textDocument: { uri: snapshot.uri.toString() } });
        const text = snapshot.origFullText;
        const marks: MarkInSelection[] = (resp.Todos ?? [])
            .filter(t => t.HostStart < snapshot.endOffset && t.HostEnd > snapshot.startOffset)
            .map(t => ({
                line: text.slice(0, t.HostStart).split('\n').length - 1,
                written: text.slice(t.HostStart, t.HostEnd).trim(),
                key: t.Key, memo: t.Memo,
            }));
        let readings = new Map<string, MarkReadings>();
        const side = snapshot.uri.scheme === 'file' ? sideFileOf(snapshot.uri.fsPath) : undefined;
        if (side && marks.some(m => m.key) && fs.existsSync(side)) {
            readings = markFactsOf(fs.readFileSync(side, 'utf8'));
        }
        return marksContext(marks, readings);
    } catch {
        return null;
    }
}

// ======================================================================
// Decide on the score (candidate preview webview)
// ======================================================================

type Decision = 'accept' | 'iterate' | 'reject';

/** A candidate as the panel reviews it: the edit, its caption, its score and where it changed. */
/** `firstNote`: what the AI said of its first answer of the round, before any repair. */
interface ReviewRound { candidate: CandidateEdit; caption: string; renderAfter: SvgResponse; changed: ChangedSpan[]; firstNote?: string | null }


async function reviewOnScore(
    deps: AiTransformDeps,
    renderBefore: SvgResponse,
    renderAfter: SvgResponse,
    caption: string,
    /** The candidate's whole text as HTML, its changes marked (wholeTextMarkup). */
    candidateHtml: string,
    changed: ChangedSpan[],
    play: Audible,
    note: string | null = null,
    /** Proposed by Copilot's chat (getCandidateHtml). */
    fromChat = false,
): Promise<Decision> {
    const panel = ensureCandidatePanel(deps, play.uri);
    audible = play;
    const fontUri = panel.webview.asWebviewUri(
        vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts', 'emmentaler-20.woff2'));
    const braceFontUri = panel.webview.asWebviewUri(
        vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts', 'emmentaler-brace.woff2'));

    panel.webview.html = getCandidateHtml(
        fontUri.toString(), braceFontUri.toString(), panel.webview.cspSource, getNonce(),
        renderBefore.Svg, renderAfter.Svg, renderAfter.Error, caption, candidateHtml, changed,
        textFontFaceCss(panel.webview, deps.extensionUri), note, fromChat, deps.synthFor?.(panel.webview));
    // Where it stands: Beside would be beside the panel itself once Ask Again is clicked in it,
    // a new column for every round (owner, 2026-10-25). Only a new panel goes Beside.
    panel.reveal(panel.viewColumn ?? vscode.ViewColumn.Beside, true);

    // Hand this round's resolver to the panel's persistent message pump. Iterate
    // reloads the webview (new html) each round; a listener registered here, right
    // after that reload, could miss the reloaded frame's messages — so the pump is
    // registered ONCE at panel creation (ensureCandidatePanel) and routes here.
    return await new Promise<Decision>(resolve => {
        pendingDecision = resolve;
    });
}

function ensureCandidatePanel(deps: AiTransformDeps, uri: vscode.Uri): vscode.WebviewPanel {
    if (candidatePanel) {
        return candidatePanel;
    }
    // Over the score's preview, a tab in its column, where a preview is open (owner's request
    // 2026-11-11: the proposal opened a third column beside the preview): the proposal is that score
    // as it would be, and takes its place while it is decided. The preview goes on playing under it
    // (retainContextWhenHidden). With no preview, beside the editor as before.
    const column = deps.previewColumnOf?.(currentUri(uri)) ?? vscode.ViewColumn.Beside;
    const fontsUri = vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts');
    const panel = vscode.window.createWebviewPanel(
        'lilysharpAiCandidate',
        'Lily# — AI candidate',
        { viewColumn: column, preserveFocus: true },
        { enableScripts: true, retainContextWhenHidden: true,
          localResourceRoots: [fontsUri, textFontsRoot(deps.extensionUri), ...(deps.synthRoots?.() ?? [])] });

    // One persistent message pump for the panel's whole lifetime. Registering the
    // listener once — before any html is set — guarantees it receives Accept/
    // Iterate/Reject across every review round, including after an iterate reload.
    const settle = (d: Decision) => {
        const resolve = pendingDecision;
        pendingDecision = undefined;
        // A decision ends a candidate's Play: the music changes, or the panel goes.
        if (audible) { deps.stopOnPreview?.(currentUri(audible.uri)); }
        stopCandidateMidi(deps);
        resolve?.(d);
    };
    panel.webview.onDidReceiveMessage((m: any) => {
        if (m && (m.action === 'accept' || m.action === 'iterate' || m.action === 'reject')) {
            settle(m.action);
        } else if (m?.action === 'play') {
            void playCandidate(deps, panel, m.which === 'before' ? 'before' : 'after',
                typeof m.from === 'number' ? m.from : undefined, typeof m.to === 'number' ? m.to : undefined);
        } else if (m?.action === 'stop' && candidateOnMidi) {
            stopCandidateMidi(deps);
        } else if (m?.action === 'stop' && audible) {
            deps.stopOnPreview?.(currentUri(audible.uri));
        }
    });
    // Closing the panel counts as reject for whatever round is awaiting a decision.
    panel.onDidDispose(() => {
        candidatePanel = undefined;
        settle('reject');
    });
    candidatePanel = panel;
    return panel;
}

/**
 * Plays the round's candidate (or, from the Before pane, the text as it stands) on the document's
 * preview (owner's request 2026-10-25: hear it before accepting). The preview is the player — its
 * synth, its sound font, its MIDI output — so what plays is what the score will sound like; the
 * notes carry no source positions, so the preview lights none of its own (they are another
 * text's). Without an open preview the panel says how to get one. A click on a note plays from
 * it (`from`, the note's source offset in that text — the svg's data-pos and the playback's S
 * count in the same text; owner's request 2026-10-25).
 */
async function playCandidate(deps: AiTransformDeps, panel: vscode.WebviewPanel, which: 'before' | 'after',
    from?: number, to?: number): Promise<void> {
    const client = deps.getClient();
    if (!audible || !client) { return; }
    // An untitled score saved since: its preview went with it to the file.
    const play = { ...audible, uri: currentUri(audible.uri) };
    const say = (text: string | null) => void panel.webview.postMessage({ type: 'playState', text });
    try {
        const pb = await client.sendRequest<{ Notes?: { S?: number }[] | null; Error?: string | null }>('lilysharp/playbackText', {
            Text: which === 'after' ? play.after : play.before,
            Uri: play.uri.toString(),
            RenderName: deps.renderNameOf?.(play.uri) ?? null,
        });
        if (pb.Error || !pb.Notes) {
            say(`Cannot play: ${pb.Error ?? 'no notes'}`);
            return;
        }
        const timed = pb.Notes as { S?: number; T?: number }[];
        const start = from === undefined ? 0 : to === undefined ? startTimeAt(timed, from) : startTimeAt(timed, from, to);
        // S is the preview's own offsets, so it is -1 here; R carries the candidate text's, which
        // the preview follows for this panel (showCandidateSounding).
        const notes = pb.Notes
            .filter(n => ((n as { T?: number }).T ?? 0) >= start - 1e-6)
            .map(n => ({ ...n, T: ((n as { T?: number }).T ?? 0) - start, S: -1, R: n.S ?? -1 }));
        playingPane = which;
        // The panel plays it itself where it can (owner's decision 2026-11-11: sound that works
        // over memory spared). Its Play is a click on its own page, so its audio may start; the
        // preview's may not until the preview has been clicked, and a panel over the preview hides
        // it. The MIDI output stays the preview's: a port is the server's, and needs no click.
        const synth = deps.synthFor?.(panel.webview);
        // A MIDI output chosen: the host asks the port of the server itself (owner's request
        // 2026-11-11: with no preview open the panel said to open one) and the panel follows the
        // port's clock. A port that is not there is said, and the panel's own synth plays instead.
        if (synth && synth.midiOutput.length > 0 && deps.midiPlay) {
            stopCandidateMidi(deps);
            const sent = await deps.midiPlay(synth.midiOutput, notes);
            if (sent.started) {
                candidateOnMidi = true;
                void panel.webview.postMessage({ type: 'playMidi', which, notes, t0Ms: sent.t0Ms, end: sent.end });
                return;
            }
            if (synth.script.length > 0) {
                void panel.webview.postMessage({ type: 'playNotes', which, notes,
                    said: `MIDI output ${synth.midiOutput}: ${sent.error ?? 'not started'} — played here instead.` });
                return;
            }
            say(`MIDI output ${synth.midiOutput}: ${sent.error ?? 'not started'}`);
            return;
        }
        // On its own synth where a sound font is there, and in plain tones where none is (a release
        // carries no font: the OS's bank by default, and Linux has none — owner's decision
        // 2026-11-11: there too the Play is to sound). The preview plays only for a host that
        // hands the panel no synth at all.
        if (synth && synth.script.length > 0) {
            void panel.webview.postMessage({ type: 'playNotes', which, notes });
            return;
        }
        if (!deps.playOnPreview?.(play.uri, notes)) {
            say('Open the preview of this file to hear it (the preview is the player).');
            return;
        }
        // Playing says nothing (owner's request 2026-10-25: the words moved the buttons); only
        // what keeps it from playing is said.
        say(null);
    } catch (e) {
        say(`Cannot play: ${String(e)}`);
    }
}


/** A candidate's Play is sounding on the MIDI output, asked by the host: a Stop or a decision ends it there. */
let candidateOnMidi = false;
function stopCandidateMidi(deps: AiTransformDeps): void {
    if (candidateOnMidi) {
        candidateOnMidi = false;
        deps.midiStop?.();
    }
}

/** The pane the candidate panel's Play sounds — its notes' offsets are that text's. */
let playingPane: 'before' | 'after' = 'after';

/**
 * The notes of a candidate's Play sounding now, as the preview that plays it follows them (owner's
 * request 2026-10-25: see where the music is); an empty list ends the playback's light.
 */
export function showCandidateSounding(sounding: { pos: number; occ: number }[]): void {
    void candidatePanel?.webview.postMessage({ type: 'sounding', which: playingPane, sounding });
}

/**
 * Says on the candidate panel why its Play is silent: the preview that plays it may not sound
 * until it has been clicked once (the browser's autoplay policy — a click on this panel's Play is
 * another page's). Owner's report 2026-11-11: Play did nothing and said nothing; the status bar's
 * eight seconds were not where the eyes were. False when no candidate is shown.
 */
export function tellCandidateAudioBlocked(): boolean {
    if (!candidatePanel) { return false; }
    void candidatePanel.webview.postMessage({ type: 'playState',
        text: 'No sound yet: click the preview once (it may play only after a click there), then press Play again.' });
    return true;
}

function getCandidateHtml(
    fontUri: string, braceFontUri: string, cspSource: string, nonce: string,
    svgBefore: string | null, svgAfter: string | null, error: string | null,
    caption: string, candidateHtml: string, changed: ChangedSpan[], textFontCss: string, note: string | null = null,
    /** Proposed by Copilot's chat: no Ask Again — what to change is said in the chat (owner's
     *  request 2026-10-29). */
    fromChat = false,
    /** The synth the panel plays with; none, or a MIDI output chosen: the preview plays (playsItself). */
    synth?: CandidateSynth,
): string {
    // The synth's bundle goes with the page whenever there is one (it holds the cursor the lit
    // notes follow, too); the synth itself is made only with a sound font to play.
    const own = !!synth && synth.script.length > 0;
    // What the page's synth is made with, or null: no sound font, and the plain tones play.
    const synthJson = hasSynth(synth) ? JSON.stringify({ processor: synth!.processor, soundFont: synth!.soundFont }) : 'null';
    const afterBody = svgAfter
        ? `<div class="score">${svgAfter}</div>`
        : `<div class="err">Could not render candidate.${error ? `<pre>${escapeHtml(error)}</pre>` : ''}</div>`;
    const beforeBody = svgBefore
        ? `<div class="score">${svgBefore}</div>`
        : `<div class="err">Could not render the original.</div>`;
    // The request first, the AI's word right under it, then what is left to fix (owner's request
    // 2026-10-27: under thirteen warnings the AI's word was lost at the bottom).
    const split = caption.indexOf(' — ⚠');
    const asked = split >= 0 ? caption.slice(0, split) : caption;
    const issues = split >= 0 ? caption.slice(split + ' — '.length) : '';
    return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; font-src ${cspSource}; script-src 'nonce-${nonce}'${own ? ` ${cspSource} 'wasm-unsafe-eval' blob:; connect-src ${cspSource}` : ''};">
${own ? `<script nonce="${nonce}" src="${synth!.script}"></script>` : ''}
<style>
@font-face { font-family: 'Emmentaler'; src: url('${fontUri}') format('woff2'); }
@font-face { font-family: 'Emmentaler-Brace'; src: url('${braceFontUri}') format('woff2'); }
${textFontCss}
body { margin:0; padding:0; display:flex; flex-direction:column; height:100vh; overflow:hidden;
       font-family: system-ui, sans-serif; background: var(--vscode-editor-background); color: var(--vscode-foreground); }
.header { padding:8px 12px; border-bottom:1px solid var(--vscode-panel-border); flex-shrink:0;
          display:flex; align-items:center; gap:12px; }
.header .cap { font-size:12px; opacity:0.8; }
.header .prompt { font-size:13px; font-weight:600; }
/* The AI's word on what it did (<note>, owner's request 2026-10-25). */
.header .note { font-size:12.5px; margin-top:3px; opacity:0.9; }
/* What is left to fix (the warnings the repairs could not clear), under the AI's word. */
.header .issues { font-size:12px; margin-top:3px; opacity:0.8; }
.header .spacer { flex:1; }
/* The head's words — the request, what is left to fix, the AI's note — held to a few lines and
   scrolled for the rest (owner's report 2026-10-27: a long list of the compiler's warnings
   filled the panel). The buttons stay beside them. */
.header .texts { flex:1; min-width:0; max-height:7.5em; overflow-y:auto; overflow-wrap:anywhere; padding-right:4px; }
.toggle { display:flex; gap:4px; }
.toggle button { padding:3px 10px; font-size:12px; }
.toggle button.on { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
/* Dark around the sheets and between them (the server leaves a gap between two pages), as in
   the preview. */
.main { flex:1; overflow:auto; background:#2b2b2b; padding:16px; }
/* The sheets are dragged to scroll, as the preview's (installDragScroll): an open hand over them,
   a closed one while they move (owner's request 2026-10-26). */
.main { cursor: grab; }
/* The sheets are not selectable text, as in the preview: a drag over them selected the SVG
   <text> glyphs it crossed, which Chromium paints as a purple box over every note (owner's
   report 2026-10-26). The text after Accept below stays selectable. */
.main { user-select: none; -webkit-user-select: none; }
.main.dragging, .main.dragging [data-pos] { cursor: grabbing; }
.score svg { max-width:100%; height:auto; }
/* The notes the AI changed, painted green — the green the OMR fix panel paints its proposals in
   (owner's request 2026-10-25; a glow alone left them black). */
/* A box drawn white (a section's or a rehearsal mark's frame) keeps its white: only its outline
   turns green, and its name stays readable inside it (owner's report 2026-10-29: the mark became
   a green block). */
.score .chg:not([fill="none"]):not([fill="#FFFFFF"]):not([fill="#ffffff"]):not([fill="white"]),
.score .chg *:not([fill="none"]):not([fill="#FFFFFF"]):not([fill="#ffffff"]):not([fill="white"]) { fill: #2e9e43 !important; }
.score .chg[stroke]:not([stroke="none"]), .score .chg [stroke]:not([stroke="none"]) { stroke: #2e9e43 !important; }
/* A note plays the pane from it when clicked. */
.score [data-pos] { cursor: pointer; }
.pane { display:none; }
.pane.show { display:block; }
/* The text after Accept, opened, never pushes the buttons below out of the panel (owner's report
   2026-10-26): it keeps its own height, at most about a third of the panel, and scrolls inside;
   the sheets above give up the room. */
.snippet { margin:10px 16px 0; flex-shrink:0; }
.snippet pre { background: var(--vscode-textCodeBlock-background); padding:8px; border-radius:4px;
               font-family: var(--vscode-editor-font-family, monospace); font-size:12px; overflow:auto; margin:4px 0 0;
               white-space:pre-wrap; max-height:35vh; position:relative; }
.main { min-height:80px; }
.snippet summary { font-size:12px; opacity:0.8; cursor:pointer; }
/* The panel's own tooltip, in VS Code's hover look — the browser's title tip is too small to read
   (owner's report 2026-10-25), as in the preview. */
.tip { position:fixed; z-index:100; max-width:320px; pointer-events:none; padding:4px 8px; border-radius:3px;
       font-family: var(--vscode-font-family, system-ui, sans-serif); font-size: var(--vscode-font-size, 13px); line-height:1.4;
       background: var(--vscode-editorHoverWidget-background, #f3f3f3); color: var(--vscode-editorHoverWidget-foreground, #333);
       border:1px solid var(--vscode-editorHoverWidget-border, #c8c8c8); box-shadow:0 2px 8px rgba(0,0,0,0.18); }
.tip[hidden] { display:none; }
/* Its changes, as the OMR fix panel prints them: green what the AI wrote, struck red what it dropped. */
.snippet ins { color: var(--vscode-gitDecoration-addedResourceForeground, #3a3); text-decoration: none; background: rgba(46, 160, 67, .18); font-weight: 600; }
.snippet del { color: var(--vscode-gitDecoration-deletedResourceForeground, #e33); text-decoration: line-through; background: rgba(229, 57, 53, .15); }
.err { color:#b00; padding:16px; }
.footer { display:flex; gap:8px; padding:10px 12px; border-top:1px solid var(--vscode-panel-border); flex-shrink:0; }
button { padding:6px 14px; font-size:13px; border:1px solid var(--vscode-button-border, transparent); border-radius:4px; cursor:pointer;
         background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
button.primary { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
button:hover { opacity:0.9; }
/* A press shows (owner's report 2026-10-25: a click left no trace): the button sinks while held,
   and a decision greys the decision buttons while the panel says what is happening. */
button:active:not(:disabled) { transform: translateY(1px); filter: brightness(0.8); }
button:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: 2px; }
button:disabled { opacity:0.45; cursor:default; }
.spacer { flex:1; }
/* The footer's words fill the room between the buttons and never move them: one line, cut short. */
.playstate { flex:1; min-width:0; align-self:center; font-size:12px; opacity:0.8;
             white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.playstate.busy { opacity:1; font-weight:600; }
/* The bar Play starts from (a click on a bar selects it), lit as the preview lights a selection. */
.score rect.bar-staff.selected { fill: rgba(0, 120, 215, 0.22); }
/* The notes sounding now while Play plays — orange, the preview's own playback color. */
.score .playing:not([fill="none"]):not([fill="#FFFFFF"]):not([fill="#ffffff"]):not([fill="white"]),
.score .playing *:not([fill="none"]):not([fill="#FFFFFF"]):not([fill="#ffffff"]):not([fill="white"]) { fill: #ff6600 !important; }
.score .playing[stroke]:not([stroke="none"]), .score .playing [stroke]:not([stroke="none"]) { stroke: #ff6600 !important; }
</style>
</head>
<body>
<div class="header">
  <div class="texts">
    <div class="cap">AI candidate — decide on the score · <span style="color:#2e9e43">■</span> changed</div>
    <div class="prompt">${escapeHtml(asked)}</div>
    ${note ? `<div class="note">💬 ${escapeHtml(note)}</div>` : ''}
    ${issues ? `<div class="issues">${escapeHtml(issues)}</div>` : ''}
  </div>
  <div class="toggle">
    <button id="tAfter" class="on">After</button>
    <button id="tBefore">Before</button>
  </div>
</div>
<div class="main">
  <div id="paneAfter" class="pane show">${afterBody}</div>
  <div id="paneBefore" class="pane">${beforeBody}</div>
</div>
<div class="snippet"><details><summary title="The whole file as Accept leaves it; what the AI changed is green, what it dropped struck red">Text after Accept</summary><pre>${candidateHtml}</pre></details></div>
<div class="footer">
  <button class="primary" id="accept" title="Write this candidate into the file (Ctrl+Z in the editor undoes it)">Accept (Enter)</button>
  <button id="iterate" title="Tell the AI what to change about this candidate — it answers with a new one, shown here"${fromChat ? ' hidden' : ''}>Ask Again…</button>
  <button id="play" title="Play the pane shown — After or Before — from the selected bar if one is (Space). Click a bar to select it; click a note to play from it.">▶ Play</button>
  <button id="stop">■ Stop</button>
  <span id="playState" class="playstate"></span>
  <button id="reject" title="Drop this candidate and close the panel — the file stays as it is">Reject (Esc)</button>
</div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const send = (action) => vscode.postMessage({ action });
  // The panel's own tooltip (see .tip): a title moves to data-tip when the pointer first comes
  // over it, so the browser's small one never shows.
  (() => {
    const tip = document.createElement('div');
    tip.className = 'tip';
    tip.hidden = true;
    document.body.appendChild(tip);
    let timer = null, shownFor = null;
    const tipOf = (el) => {
      for (let e = el; e && e !== document.body; e = e.parentNode) {
        if (!e.getAttribute) continue;
        if (e.hasAttribute('title')) { e.setAttribute('data-tip', e.getAttribute('title')); e.removeAttribute('title'); }
        if (e.getAttribute('data-tip')) return e;
      }
      return null;
    };
    const hide = () => { clearTimeout(timer); timer = null; tip.hidden = true; shownFor = null; };
    document.addEventListener('pointerover', (ev) => {
      const el = tipOf(ev.target);
      if (el === shownFor) return;
      hide();
      if (!el) return;
      timer = setTimeout(() => {
        tip.textContent = el.getAttribute('data-tip');
        tip.hidden = false;
        const r = el.getBoundingClientRect();
        const w = tip.offsetWidth, h = tip.offsetHeight;
        const x = Math.min(Math.max(4, r.left + r.width / 2 - w / 2), window.innerWidth - w - 4);
        let y = r.bottom + 6;
        if (y + h > window.innerHeight - 4) y = Math.max(4, r.top - h - 6);
        tip.style.left = x + 'px';
        tip.style.top = y + 'px';
        shownFor = el;
      }, 500);
    });
    document.addEventListener('pointerdown', hide, true);
    document.addEventListener('keydown', hide, true);
    document.addEventListener('scroll', hide, true);
    window.addEventListener('blur', hide);
  })();
  const paneAfter = document.getElementById('paneAfter');
  const paneBefore = document.getElementById('paneBefore');
  const tAfter = document.getElementById('tAfter');
  const tBefore = document.getElementById('tBefore');

  // Light up the notes whose source offset (data-pos) falls in the changed
  // span, and remember the first so we can scroll it into view. The renderer
  // emits data-pos for editor↔preview sync, so the same offsets the edit used
  // map straight onto SVG elements here.
  const CHANGED = ${JSON.stringify(changed)};
  // One span per place the candidate changed ([lo, hi) by side).
  function markChanged(pane, after) {
    let first = null;
    pane.querySelectorAll('[data-pos]').forEach(el => {
      const pos = parseInt(el.getAttribute('data-pos'), 10);
      if (!isNaN(pos) && CHANGED.some(s => after ? pos >= s.afterLo && pos < s.afterHi
                                                 : pos >= s.beforeLo && pos < s.beforeHi)) {
        el.classList.add('chg');
        if (first === null) first = el;
      }
    });
    return first;
  }
  const firstAfter = markChanged(paneAfter, true);
  const firstBefore = markChanged(paneBefore, false);
  function scrollToChange(after) {
    const el = after ? firstAfter : firstBefore;
    if (el && el.scrollIntoView) {
      try { el.scrollIntoView({ block: 'center', inline: 'center' }); } catch (e) {}
    }
  }

  // After and Before trade places under the same view (owner's request 2026-10-26: the switch
  // went back to the top of the first page whenever the shown side had no change to centre on):
  // the two are the same pages at the same zoom, so the scroll offsets carry over as they are.
  const main = document.querySelector('.main');
  function show(after) {
    const left = main.scrollLeft, top = main.scrollTop;
    paneAfter.classList.toggle('show', after);
    paneBefore.classList.toggle('show', !after);
    tAfter.classList.toggle('on', after);
    tBefore.classList.toggle('on', !after);
    main.scrollLeft = left;
    main.scrollTop = top;
  }
  tAfter.addEventListener('click', () => show(true));
  tBefore.addEventListener('click', () => show(false));
  // Default pane is "After": center its first changed note once laid out.
  scrollToChange(true);

  // Ctrl+wheel zooms both panes alike about the point under the pointer (owner's request
  // 2026-10-26). 1 is the fit the panel opens at — each sheet its own width, no wider than the
  // panel — and a zoom sizes every sheet from that fit, the hidden pane's too.
  let zoom = 1;
  function nextZoom(z, deltaY) {
    const n = Math.min(4, Math.max(0.25, deltaY > 0 ? z / 1.1 : z * 1.1));
    return Math.abs(n - 1) < 0.02 ? 1 : n;
  }
  function sizeSheets() {
    const room = main.clientWidth - 32;   // (.main's padding)
    document.querySelectorAll('.score svg').forEach(svg => {
      if (zoom === 1) { svg.style.width = ''; svg.style.maxWidth = ''; return; }
      const natural = svg.width && svg.width.baseVal ? svg.width.baseVal.value : room;
      svg.style.maxWidth = 'none';
      svg.style.width = (Math.min(natural, room) * zoom) + 'px';
    });
  }
  // The point at (cx, cy) in the scroller's box stays there across resize().
  function zoomAbout(scroller, resize, cx, cy) {
    const fx = (scroller.scrollLeft + cx) / scroller.scrollWidth;
    const fy = (scroller.scrollTop + cy) / scroller.scrollHeight;
    resize();
    scroller.scrollLeft = fx * scroller.scrollWidth - cx;
    scroller.scrollTop = fy * scroller.scrollHeight - cy;
  }
  main.addEventListener('wheel', (e) => {
    if (!e.ctrlKey) return;
    e.preventDefault();
    const next = nextZoom(zoom, e.deltaY);
    if (next === zoom) return;
    const r = main.getBoundingClientRect();
    zoomAbout(main, () => { zoom = next; sizeSheets(); }, e.clientX - r.left, e.clientY - r.top);
  }, { passive: false });
  window.addEventListener('resize', () => { if (zoom !== 1) sizeSheets(); });

  // Drag the sheets to scroll them — the preview's installDragScroll, word for word
  // (webviewScript.test.ts holds the two together): a press becomes a drag only past the slop,
  // so a click on a note still plays and a click on a bar still selects it.
  function installDragScroll(scroller, doc) {
    const SLOP = 4;
    let press = null;
    let dragged = false;
    scroller.addEventListener('pointerdown', (e) => {
      // Every click starts with a press, so a flag a drag left behind (its click
      // never came) cannot swallow the next real click.
      dragged = false;
      press = null;
      if (e.pointerType !== 'mouse' || e.button !== 0
        || e.shiftKey || e.ctrlKey || e.metaKey || e.altKey) return;
      const r = scroller.getBoundingClientRect();
      if (e.clientX - r.left >= scroller.clientLeft + scroller.clientWidth
        || e.clientY - r.top >= scroller.clientTop + scroller.clientHeight) return;
      if (e.target && e.target.closest && e.target.closest('button, input, select, textarea, a')) return;
      press = { id: e.pointerId, x: e.clientX, y: e.clientY, left: scroller.scrollLeft, top: scroller.scrollTop };
    });
    scroller.addEventListener('pointermove', (e) => {
      if (press === null || e.pointerId !== press.id) return;
      const dx = e.clientX - press.x, dy = e.clientY - press.y;
      if (!dragged) {
        if (Math.abs(dx) < SLOP && Math.abs(dy) < SLOP) return;
        dragged = true;
        scroller.setPointerCapture(e.pointerId);
        scroller.classList.add('dragging');
      }
      scroller.scrollLeft = press.left - dx;
      scroller.scrollTop = press.top - dy;
    });
    const release = (e) => {
      if (press === null || e.pointerId !== press.id) return;
      press = null;
      scroller.classList.remove('dragging');
    };
    scroller.addEventListener('pointerup', release);
    scroller.addEventListener('pointercancel', (e) => { release(e); dragged = false; });
    doc.addEventListener('click', (e) => {
      if (!dragged) return;
      dragged = false;
      e.stopImmediatePropagation();
      e.preventDefault();
    }, true);
  }
  installDragScroll(main, document);

  // The text after Accept is the whole file now; opened, it shows its first change in the middle
  // of its box (owner's request 2026-10-26). Only the box scrolls (the pre is the offset parent
  // of its marks), not the panel around it.
  function jumpToFirstChange(pre) {
    const first = pre.querySelector('ins, del');
    if (!first) return;
    pre.scrollTop = Math.max(0, first.offsetTop - (pre.clientHeight - first.offsetHeight) / 2);
  }
  const details = document.querySelector('.snippet details');
  details.addEventListener('toggle', () => { if (details.open) jumpToFirstChange(details.querySelector('pre')); });
  // A decision says at once that it was taken (owner's report 2026-10-25: a click left no trace):
  // the decision buttons grey out and the footer names what happens next. A new candidate, or
  // Ask Again closed unanswered, reloads the panel and brings them back.
  const playState = document.getElementById('playState');
  const say = (text, busy) => { playState.textContent = text || ''; playState.classList.toggle('busy', !!busy); };
  let decided = false;
  const SAY = {
    accept: 'Writing it into the file…',
    iterate: 'Type what to change in the box at the top of the window (Esc comes back here)…',
    reject: 'Rejected — the file stays as it is.',
  };
  // From the chat, what to change is said there (owner's request 2026-10-29): the footer says so.
  if (${fromChat}) say('To change it, write in the chat — its next proposal opens here.');
  const decide = (action) => {
    if (decided) return;
    decided = true;
    stopOwn();   // a decision ends the panel's own Play, as it ends the preview's
    for (const id of ['accept', 'iterate', 'reject']) document.getElementById(id).disabled = true;
    say(SAY[action], true);
    send(action);
  };
  document.getElementById('accept').addEventListener('click', () => decide('accept'));
  document.getElementById('iterate').addEventListener('click', () => decide('iterate'));
  document.getElementById('reject').addEventListener('click', () => decide('reject'));
  // A bar is selected by a click on it — beside its notes, since a note plays at once — and Play
  // starts there (owner's request 2026-10-25: play from the middle). One bar per pane: the two
  // panes are two texts. The bar boxes take no pointer events, so the click is placed by their
  // geometry, as in the preview.
  const which = () => paneAfter.classList.contains('show') ? 'after' : 'before';
  const selectedBar = { after: null, before: null };
  function barAt(pane, x, y) {
    for (const r of pane.querySelectorAll('rect.bar-staff[data-span]')) {
      const b = r.getBoundingClientRect();
      if (x >= b.left && x <= b.right && y >= b.top && y <= b.bottom) return r;
    }
    return null;
  }
  function selectBar(r) {
    const w = which();
    if (selectedBar[w]) selectedBar[w].classList.remove('selected');
    selectedBar[w] = r;
    if (r) r.classList.add('selected');
  }
  // Play sounds the pane shown, After or Before, in the preview (owner's request 2026-10-25),
  // from its selected bar if one is.
  const play = () => {
    const r = selectedBar[which()];
    const span = r ? r.getAttribute('data-span').split(' ').map(Number) : null;
    vscode.postMessage(span ? { action: 'play', which: which(), from: span[0], to: span[1] } : { action: 'play', which: which() });
  };
  // The panel sounds its own Play (owner's decision 2026-11-11: a Play that works over memory
  // spared). A page may start its audio only once it has been clicked, and a click here is not
  // a click on the preview, which played the notes before — silent until the preview had been
  // clicked, and hidden under this panel. So the panel has the synth and the sound font too: the
  // audio is woken in the click itself, and the notes the host answers play when they come.
  const SYNTH = ${synthJson};
  let ownCtx = null, ownSynth = null, ownPlayer = null, ownTick = null, ownEnd = null, ownGen = 0, ownPane = 'after';
  // With no sound font (SYNTH null — none set, and the OS has none), the plain tones the preview
  // falls back on: the same patches (plainTones.ts).
  let playingOscs = [];
${PLAIN_TONES_JS}
  function wakeAudio() {
    if (!ownCtx) ownCtx = new AudioContext();
    if (ownCtx.state !== 'running') ownCtx.resume().catch(() => {});
    if (SYNTH && window.LysSynth && !ownSynth) ownSynth = window.LysSynth.create(ownCtx, SYNTH.processor, SYNTH.soundFont, (status) => {
      if (status.state === 'failed') say('Sound: ' + (status.detail || 'the sound font did not load'));
    });
  }
  // On the MIDI output (the host asked the port): the notes light by the port's own clock, the
  // wall-clock time of its zero, which Date.now() counts too.
  function playMidi(w, notes, t0Ms, end) {
    stopOwn();
    if (!decided) say('');
    ownPane = w;
    if (window.LysSynth && window.LysSynth.SoundingCursor) {
      const cursor = new window.LysSynth.SoundingCursor(notes.map(n => ({ t: n.T, d: n.D, s: n.R, o: n.O || 0 })));
      ownTick = setInterval(() => {
        const r = cursor.at((Date.now() - t0Ms) / 1000);
        if (r.changed) lightSounding(w, r.sounding.map(x => ({ pos: x.pos, occ: x.occ })));
      }, 50);
    }
    ownEnd = setTimeout(stopOwn, Math.max(0, t0Ms - Date.now() + (end + 0.5) * 1000));
  }
  function stopOwn() {
    ownGen++;
    clearInterval(ownTick); clearTimeout(ownEnd);
    ownTick = ownEnd = null;
    if (ownPlayer) { ownPlayer = null; if (ownSynth) ownSynth.stop(); }
    for (const o of playingOscs) { try { o.stop(); } catch (e) { /* ended already */ } }
    playingOscs = [];
    lightSounding(ownPane, []);
  }
  function playOwn(w, notes) {
    stopOwn();
    wakeAudio();
    if (!notes || notes.length === 0) return;
    const gen = ++ownGen;
    // The notes sounding light by the audio's own clock, and the Play ends with the last of them.
    const follow = (t0, end) => {
      ownPane = w;
      if (window.LysSynth && window.LysSynth.SoundingCursor) {
        const cursor = new window.LysSynth.SoundingCursor(notes.map(n => ({ t: n.T, d: n.D, s: n.R, o: n.O || 0 })));
        ownTick = setInterval(() => {
          const r = cursor.at(ownCtx.currentTime - t0);
          if (r.changed) lightSounding(w, r.sounding.map(x => ({ pos: x.pos, occ: x.occ })));
        }, 50);
      }
      ownEnd = setTimeout(stopOwn, (end + 0.5) * 1000);
    };
    const plain = (why) => {
      if (gen !== ownGen) return;
      if (!decided) say(why);
      const t0 = ownCtx.currentTime + 0.15;
      follow(t0, startOscillators(ownCtx, notes, 0, t0));
    };
    if (!ownSynth) { plain(''); return; }
    const go = (ok) => {
      if (gen !== ownGen) return;
      if (!ok || !ownSynth.ready) { plain('The sound font is not ready: plain tones this time.'); return; }
      if (!decided) say('');
      ownPlayer = ownSynth.play(notes, 0, ownCtx.currentTime + 0.15);
      follow(ownPlayer.t0, Math.max(ownPlayer.end, notes.reduce((e, n) => Math.max(e, n.T + n.D), 0)));
    };
    if (ownSynth.state === 'loading') {
      if (!decided) say('Loading the sound font…', true);
      ownSynth.whenReady(8000).then(go);
    } else {
      go(true);
    }
  }
  document.getElementById('play').addEventListener('click', () => { wakeAudio(); play(); });
  // A click on a note (anything the score ties to the source) plays the pane from there; on a
  // bar, selects it; elsewhere, clears the selection.
  document.querySelector('.main').addEventListener('click', (e) => {
    const el = e.target && e.target.closest ? e.target.closest('[data-pos]') : null;
    const from = el ? parseInt(el.getAttribute('data-pos'), 10) : NaN;
    if (!isNaN(from)) {
      wakeAudio();
      vscode.postMessage({ action: 'play', which: which(), from });
      return;
    }
    selectBar(barAt(which() === 'after' ? paneAfter : paneBefore, e.clientX, e.clientY));
  });
  document.getElementById('stop').addEventListener('click', () => { stopOwn(); send('stop'); });
  // Play lights the notes sounding now (owner's request 2026-10-25), on the pane it plays, as the
  // preview playing them follows them; the newest stays in view.
  let lit = [];
  const byPos = { after: null, before: null };
  // Every element of an offset, as its data-pos or a data-alt member: a chord sounds at its '<',
  // which each head carries as data-alt beside its own pitch's data-pos (owner's report 2026-10-29:
  // a song's right-hand chords stayed dark here while the preview lit them). The click target and
  // the white mask behind a fret digit are left out, as the preview leaves them (sourcePositions).
  function elementsAt(w, pos) {
    if (!byPos[w]) {
      byPos[w] = new Map();
      const add = (p, el) => { if (!isNaN(p)) { if (!byPos[w].has(p)) byPos[w].set(p, []); byPos[w].get(p).push(el); } };
      (w === 'after' ? paneAfter : paneBefore).querySelectorAll('[data-pos], [data-alt]').forEach(el => {
        if (el.classList.contains('nh-hit')
            || (el.tagName.toLowerCase() === 'rect' && !el.getAttribute('stroke') && el.getAttribute('fill'))) return;
        add(parseInt(el.getAttribute('data-pos'), 10), el);
        const alt = el.getAttribute('data-alt');
        if (alt) alt.split(' ').forEach(a => add(parseInt(a, 10), el));
      });
    }
    return byPos[w].get(pos) || [];
  }
  // A note's printed copies (a phrase written once and played often, a section a form plays
  // twice), told apart as the preview tells them (its clusterInstances; owner's request 2026-10-29):
  // what stands in one bar-box's column — the nearest up or down — within 6 staff spaces across,
  // the copies in the order of the bars; one copy where the pane draws no bar-box.
  function copiesOf(w, els) {
    const bars = Array.from((w === 'after' ? paneAfter : paneBefore).querySelectorAll('rect.bar-box'));
    if (bars.length === 0) return [els];
    const boxes = bars.map(r => r.getBoundingClientRect());
    let unit = 1;
    try { const b0 = bars[0].getBBox(); if (b0.width > 0) unit = boxes[0].width / b0.width; } catch (e) { /* non-SVG */ }
    const byBar = new Map();
    const loose = [];
    let last = null;
    for (const el of els) {
      const b = el.getBoundingClientRect();
      const x = b.left + 0.5, y = (b.top + b.bottom) / 2;
      let bar = -1, nearest = Infinity;
      for (let i = 0; i < boxes.length; i++) {
        const r = boxes[i];
        if (x < r.left || x > r.right) continue;
        const d = y < r.top ? r.top - y : y > r.bottom ? y - r.bottom : 0;
        if (d < nearest) { nearest = d; bar = i; }
      }
      if (bar < 0) { if (last) last.els.push(el); else loose.push(el); continue; }
      let list = byBar.get(bar);
      if (!list) byBar.set(bar, list = []);
      let copy = list.find(c => Math.abs(c.x - b.left) <= 6 * unit);
      if (!copy) { copy = { x: b.left, els: [] }; list.push(copy); }
      copy.els.push(el);
      last = copy;
    }
    const copies = [];
    for (const bar of Array.from(byBar.keys()).sort((p, q) => p - q)) {
      for (const c of byBar.get(bar).sort((p, q) => p.x - q.x)) copies.push(c.els);
    }
    if (loose.length > 0) { if (copies.length > 0) copies[0].unshift(...loose); else copies.push(loose); }
    return copies;
  }
  // The copy sounding: the occ-th in the order of the bars (the server's printed-copy ordinal).
  function soundingAt(w, s) {
    const els = elementsAt(w, s.pos);
    if (!(Number.isInteger(s.occ) && s.occ >= 0) || els.length < 2) return els;
    const copies = copiesOf(w, els);
    return copies[Math.min(s.occ, copies.length - 1)] || els;
  }
  function lightSounding(w, sounding) {
    lit.forEach(el => el.classList.remove('playing'));
    lit = [];
    for (const s of sounding) lit.push(...soundingAt(w, s));
    lit.forEach(el => el.classList.add('playing'));
    const el = sounding.length > 0 ? soundingAt(w, sounding[0])[0] : null;
    if (el) {
      const r = el.getBoundingClientRect();
      const m = document.querySelector('.main').getBoundingClientRect();
      if (r.top < m.top + 30 || r.bottom > m.bottom - 30 || r.left < m.left + 30 || r.right > m.right - 30) {
        try { el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' }); } catch (e) {}
      }
    }
  }
  window.addEventListener('message', (e) => {
    if (!e.data) return;
    if (e.data.type === 'sounding') { lightSounding(e.data.which, e.data.sounding || []); return; }
    if (e.data.type === 'playNotes') {
      playOwn(e.data.which === 'before' ? 'before' : 'after', e.data.notes || []);
      if (e.data.said && !decided) say(e.data.said);
      return;
    }
    if (e.data.type === 'playMidi') { playMidi(e.data.which === 'before' ? 'before' : 'after', e.data.notes || [], e.data.t0Ms || Date.now(), e.data.end || 0); return; }
    // Playback news is not shown over a decision being carried out.
    if (e.data.type === 'playState' && !decided) { say(e.data.text); }
    else if (e.data.type === 'status') { say(e.data.text, true); }
    else if (e.data.type === 'undecided') {
      decided = false;
      for (const id of ['accept', 'iterate', 'reject']) document.getElementById(id).disabled = false;
      say(e.data.text, !!e.data.text);
    }
  });
  window.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); decide('accept'); }
    else if (e.key === 'Escape') { e.preventDefault(); decide('reject'); }
    else if (e.key === 'Tab') { e.preventDefault(); show(!paneAfter.classList.contains('show')); }
    else if (e.key === ' ') { e.preventDefault(); wakeAudio(); play(); }
  });
</script>
</body>
</html>`;
}

// ======================================================================
// Apply (EditApplier, §7 version/range guard)
// ======================================================================

/** Undefined when written, else what the panel says about why not. */
async function applyCandidate(deps: AiTransformDeps, snapshot: Snapshot, candidate: string): Promise<string | undefined> {
    const uri = currentUri(snapshot.uri);
    // A document no editor shows (asked from the preview, its editor tab closed) is let go by VS
    // Code after a while — the AI thinks for minutes — though the file is there as it was (owner's
    // report 2026-10-27: "the document was closed; nothing was applied"). A file is opened again;
    // the anchoring below finds the text where it was. Only an untitled score, gone with its tab,
    // cannot be.
    let doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri.toString());
    if (!doc && uri.scheme !== 'untitled') {
        try {
            doc = await vscode.workspace.openTextDocument(uri);
        } catch {
            doc = undefined;
        }
    }
    if (!doc) {
        vscode.window.showErrorMessage('Lily#: the document was closed; nothing was applied.');
        return 'Not written — the document was closed.';
    }

    let range: vscode.Range;
    // A saved untitled score is another document, its versions counted anew.
    if (doc.uri.toString() === snapshot.uri.toString() && doc.version === snapshot.version) {
        // Untouched since snapshot — the recorded offsets are exact.
        range = new vscode.Range(doc.positionAt(snapshot.startOffset), doc.positionAt(snapshot.endOffset));
    } else {
        // The document changed while the model was thinking. If the exact selected
        // text still sits at the snapshot offsets, it's safe to apply there.
        const current = doc.getText();
        const stillThere = current.slice(snapshot.startOffset, snapshot.endOffset) === snapshot.origSelectedText;
        if (stillThere) {
            range = new vscode.Range(doc.positionAt(snapshot.startOffset), doc.positionAt(snapshot.endOffset));
        } else {
            // Try to re-anchor: find the original text uniquely elsewhere.
            const first = current.indexOf(snapshot.origSelectedText);
            const unique = first >= 0 && current.indexOf(snapshot.origSelectedText, first + 1) === -1;
            if (unique) {
                range = new vscode.Range(doc.positionAt(first), doc.positionAt(first + snapshot.origSelectedText.length));
            } else {
                const choice = await vscode.window.showWarningMessage(
                    'Lily#: the document changed since this transform was generated, and the selected text moved. Apply at the original position anyway?',
                    { modal: true }, 'Apply anyway');
                if (choice !== 'Apply anyway') {
                    return 'Not written — the file changed meanwhile.';
                }
                const clampStart = Math.min(snapshot.startOffset, current.length);
                const clampEnd = Math.min(snapshot.endOffset, current.length);
                range = new vscode.Range(doc.positionAt(clampStart), doc.positionAt(clampEnd));
            }
        }
    }

    const edit = new vscode.WorkspaceEdit();
    edit.replace(uri, range, candidate);
    const ok = await vscode.workspace.applyEdit(edit);
    if (ok) {
        vscode.window.showInformationMessage('Lily#: applied AI transform (Ctrl+Z to undo).');
        candidatePanel?.dispose();
        return undefined;
    }
    vscode.window.showErrorMessage('Lily#: the edit could not be applied.');
    return 'Not written — the edit could not be applied.';
}

/**
 * Waits for another decision on the candidate the panel shows, its buttons armed again and
 * `text` in its footer — after an Accept that wrote nothing, or Ask Again left unanswered.
 */
function decideAgain(text: string): Promise<Decision> {
    if (!candidatePanel) {
        return Promise.resolve('reject');
    }
    void candidatePanel.webview.postMessage({ type: 'undecided', text });
    return new Promise<Decision>(resolve => {
        pendingDecision = resolve;
    });
}

/** What the candidate panel's footer says about a decision being carried out (or that failed). */
function panelStatus(text: string): void {
    void candidatePanel?.webview.postMessage({ type: 'status', text });
}

// ======================================================================
// Grammar loading (bundled by esbuild into out/GRAMMAR_FOR_LLM.md)
// ======================================================================

export async function loadGrammar(deps: AiTransformDeps): Promise<string> {
    if (cachedGrammar !== undefined) {
        return cachedGrammar;
    }
    const candidates = [
        vscode.Uri.joinPath(deps.extensionUri, 'out', 'GRAMMAR_FOR_LLM.md'),
        vscode.Uri.joinPath(deps.extensionUri, 'GRAMMAR_FOR_LLM.md'),
    ];
    for (const uri of candidates) {
        try {
            const buf = fs.readFileSync(uri.fsPath, 'utf8');
            if (buf && buf.trim().length > 0) {
                cachedGrammar = buf;
                return cachedGrammar;
            }
        } catch {
            // try next
        }
    }
    deps.log('AI transform: bundled grammar not found; using compact fallback.');
    cachedGrammar = GRAMMAR_FALLBACK;
    return cachedGrammar;
}

let cachedComposeGuide: string | undefined;

/**
 * The composing guide (docs/COMPOSE_FOR_LLM.md, copied to out/ at build), sent with Compose with AI
 * after the grammar (owner's decision 2026-10-29: kept apart, so Ask AI is not sent it). Empty when
 * it is missing: the grammar and the composer's prompt still carry the piece.
 */
export async function loadComposeGuide(deps: AiTransformDeps): Promise<string> {
    if (cachedComposeGuide !== undefined) {
        return cachedComposeGuide;
    }
    for (const uri of [vscode.Uri.joinPath(deps.extensionUri, 'out', 'COMPOSE_FOR_LLM.md'), vscode.Uri.joinPath(deps.extensionUri, 'COMPOSE_FOR_LLM.md')]) {
        try {
            const buf = fs.readFileSync(uri.fsPath, 'utf8');
            if (buf.trim().length > 0) {
                return (cachedComposeGuide = buf);
            }
        } catch {
            // try next
        }
    }
    deps.log('AI transform: the composing guide was not found; composing with the grammar alone.');
    return (cachedComposeGuide = '');
}

// ======================================================================
// Small helpers
// ======================================================================

interface Snapshot {
    version: number;
    startOffset: number;
    endOffset: number;
    origFullText: string;
    origSelectedText: string;
    uri: vscode.Uri;
}

function escapeHtml(s: string): string {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function getNonce(): string {
    let text = '';
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    for (let i = 0; i < 32; i++) {
        text += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    return text;
}
