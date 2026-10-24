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

// "Lily#: Fix OMR marks with AI…" (docs/omr-ai-fix-design.md, M1): the bars an OMR reader marked
// as doubtful (@todo), each rewritten by the language model from its text, its memo, the bars
// around it and the compiler's report — never on its own: the command asks which bars, says
// what the run will cost, and shows every proposal beside the scan and the score before a
// single one is written. What passes the gates (compiles without a new error, fills its meter)
// is listed with a check box a row (the gated ones the model is sure of checked); Accept writes the checked rows as ONE WorkspaceEdit (one
// Ctrl+Z), and the marks go with the lines they were on. omrAiFixCore.ts is the editor-free
// half; the scan's image goes to the model at M2 (here it is shown to the person only).

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { execFile } from 'child_process';
import type { LanguageClient } from 'vscode-languageclient/node';
import { AiTransformDeps, loadGrammar } from './aiTransform';
import { ChatClient, ChatMessage, availableModels, hasChatModel, offerModelSetup, resolveChatClient } from './modelClient';
import {
    BarCheck, BarNeighbours, Diag, FixTarget, FixableTodo, GateVerdict, Proposal, Rgba, acceptableByAll, anchorOf, averageOf,
    checkedByDefault, cropRect, decodeImage, diagnosticsOn, estimateOf, fixTargets, gateOf, headOf, labelOf,
    neighboursOf, parseProposal, readerBarOf, repairPromptOf, resolvedLine, scanCropPng, spliceBar, systemPromptOf, targetsInLines, taskPromptOf, withTodoMark, lineTargets,
    verifyVerdict, withSample, withVerify, wordDiff, DiffPiece, rangesOf, shortTodos,
} from './omrAiFixCore';
import { OmrVersion, canVerifyBars, exitMeaning, launchOf, parseVersion, runOmr, verifyArguments } from './omrCore';
import { findOmr } from './omrReader';
import { SCAN_DEFAULT_THRESHOLD, ScanSideFile, lineAround, markFactsOf, parseSideFile, shownImage, sideFileOf } from './omrScanCore';
import { textFontFaceCss, textFontsRoot } from './scoreFonts';

export interface OmrAiFixDeps extends AiTransformDeps {
    /** The *Marks below* slider of the document's scan view, if one is open (omrScan.scanThresholdOf). */
    scanThreshold: (lys: string) => number | undefined;
    /** The lysc the reader engraves with when it verifies, if the extension carries one. */
    lysc: () => string | undefined;
    /** The bar a right-click in the preview or the scan view was on (the page's data-vscode-context,
     *  which VS Code hands the menu's command): the .lys and an offset in it; undefined when the
     *  click was on no bar or the view is gone. */
    barAtMenu: (context: MenuContext) => { uri: vscode.Uri; at: number } | undefined;
    /** The column the document's scan view is in, if one is open (omrScan.scanColumnOf). */
    scanColumn: (lys: string) => vscode.ViewColumn | undefined;
}

/** What a webview's context menu hands its command: the view's type, and what its page wrote
 *  into data-vscode-context at the right-click — the preview's offset, the scan view's bar. */
export interface MenuContext {
    readonly webview: string;
    readonly lysAt?: number;
    readonly lysBar?: number;
}

/** An offset moved past the spaces and line breaks at it: a bar's drawn position is its node's
 *  full-span start, which can be the previous line's break or the indentation. */
function pastSpace(text: string, at: number): number {
    while (at < text.length && /\s/.test(text[at])) { at++; }
    return at;
}

/** A command's first argument, read: a webview's menu context → its bar (null when it names
 *  none); anything else (a uri, nothing) → undefined, the command's other doors. */
function menuBar(deps: OmrAiFixDeps, arg: unknown): { uri: vscode.Uri; at: number } | null | undefined {
    if (typeof arg !== 'object' || arg === null || arg instanceof vscode.Uri || typeof (arg as MenuContext).webview !== 'string') {
        return undefined;
    }
    return deps.barAtMenu(arg as MenuContext) ?? null;
}

/** The reader that compares a proposal with the scan (gate ③): where it is, the lysc it draws
 *  with, and the .lys whose side file and pages it compares against. */
interface Verifier {
    readonly launch: { command: string; prefix: string[] };
    readonly lysc?: string;
    readonly lysPath: string;
}

// ---- Server DTO mirrors (LspProtocolDtos.cs) ----
interface CheckCandidateResponse { HasErrors: boolean; Diagnostics: Diag[]; }
interface SvgResponse { Svg: string | null; Error: string | null; }
interface TodosResponse { Todos?: FixableTodo[]; Version: number; Error?: string | null; }

/** One repair round after a failed gate (design §3.2 ④). */
const MAX_REPAIRS = 1;
/** Where the last bars' seconds are kept, for the estimate (globalState). */
const TIMINGS_KEY = 'lilysharp.ai.omrFixSeconds';

/** The model's answer for one bar, as the gates left it. */
interface Answer {
    readonly proposal: Proposal | null;
    /** The gates' verdict on the proposal (none without one). */
    readonly gate?: GateVerdict;
    /** The score with the proposal, when there is one that renders. */
    readonly after?: SvgResponse;
    /** Why there is no answer at all (`proposal` null): the model returned nothing, failed, or
     *  replied with something other than the JSON asked for — the reply's start, so the person
     *  sees what came back. */
    readonly noAnswer?: string;
}

/** One bar, a row of the panel: the model's answer once there is one (the fields of Answer),
 *  the person's edit and choices. */
interface FixRow {
    readonly index: number;
    readonly target: FixTarget;
    /** The text the row was opened on: its offsets, pictures, gates and questions are about it
     *  (a row stays true to it when the panel's own Accepts change the document meanwhile). */
    readonly base: RunBase;
    proposal: Proposal | null;
    gate?: GateVerdict;
    after?: SvgResponse;
    noAnswer?: string;
    seconds: number;
    /** Written by its Accept, taken off by its Discard (both leave the panel), or not found
     *  where it was written when an Accept looked for it. */
    state: 'pending' | 'applied' | 'discarded' | 'skipped';
    /** The AI is about it (owner's request 2026-10-24: asked a row at a time, answered in the
     *  background while the other rows are edited): waiting in the queue, or being asked. */
    ai?: 'queued' | 'asking';
    /** The AI has answered it once at least (its column is then "Proposal", not "Edit"). */
    asked?: boolean;
    /** Taken with a @todo still on it, and the memo the person typed for it (owner's request
     *  2026-10-24: a proposal that is better but not right yet). */
    keepTodo?: boolean;
    todoMemo?: string;
    /** The bar as the person edited it in the panel (owner's request 2026-10-24), with its own
     *  gates and picture; undefined: the model's proposal stands. */
    edited?: string;
    editGate?: GateVerdict;
    editAfter?: SvgResponse;
    /** The page's count of edits to this row: an answer to an older one is dropped. */
    editSeq?: number;
    /** The bar as it stands (its marks taken out), drawn where a row has no bar of its own to
     *  show — no proposal, or a bar opened without the AI — so it can be clicked and edited
     *  (owner's request 2026-10-24). */
    baseAfter?: SvgResponse;
}

/** A text of the document the panel's rows are opened on: its version, its picture (each row's
 *  "Now"), and the server's diagnostics on it (what a new error is measured against). */
interface RunBase {
    readonly id: number;
    readonly text: string;
    readonly version: number;
    readonly before: SvgResponse;
    readonly baseline: readonly Diag[];
}

/** The run a panel shows: the document as the panel last knew it, the rows so far. */
interface FixRun {
    readonly uri: vscode.Uri;
    /** The text new rows are opened on: the document when the panel opened, then as the panel's
     *  own Accepts left it (a new base is drawn when a row is added after one). */
    base: RunBase;
    /** The document versions the panel's own Accepts made: a later command on the file is still
     *  this panel's (owner's request 2026-10-24: Accept a row at a time). */
    readonly written: number[];
    /** The bases whose picture the page has (its symbol store). */
    readonly shownBases: Set<number>;
    readonly side?: ScanSideFile;
    readonly rows: FixRow[];
    /** Whether the scan goes to the model (`lilysharp.ai.omrFixImages`, and a page to crop). */
    readonly images: boolean;
    /** The pages decoded so far (null: the page could not be read), by page number. */
    readonly pages: Map<number, Rgba | null>;
    /** The reader for gate ③, when there is one that can (`verify-bars`) and the setting is on. */
    readonly verifier?: Verifier;
    /** The server: an edit in the panel is gated and drawn as the model's proposals were. */
    readonly client: LanguageClient;
    /** Opened by Edit Bars Beside the Scan, not by the AI command (the panel's title). */
    readonly manual?: boolean;
    /** The rows waiting for the AI, in order, and the one being asked with its cancel. */
    readonly queue: FixRow[];
    current?: { row: FixRow; cts: vscode.CancellationTokenSource };
    /** The model, the system prompt and the file's head, from the first ask on. */
    ai?: { chat: ChatClient; system: string; head: string };
    /** The queue is being worked; the panel went away (the queue stops). */
    working?: boolean;
    closed?: boolean;
}

/** The bar as it stands, marks taken out, drawn: the whole text again only when the line carries
 *  a mark (else it is the "before" picture already). */
async function baseAfterOf(run: FixRun, base: RunBase, target: FixTarget): Promise<SvgResponse> {
    const line = resolvedLine(target.written);
    return line === target.written ? base.before
        : run.client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: spliceBar(base.text, target, line) });
}

let baseIds = 0;

/** The document as it is now, drawn and diagnosed: a base for rows. */
async function baseOf(client: LanguageClient, doc: vscode.TextDocument): Promise<RunBase> {
    const text = doc.getText();
    return {
        id: ++baseIds, text, version: doc.version,
        before: await client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: text }),
        baseline: (await client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: text })).Diagnostics ?? [],
    };
}

/** `omr --version --json` once per reader path (a run asks for every bar). */
const versions = new Map<string, Promise<OmrVersion | undefined>>();

/** The verifier for a run, or undefined with the reason logged: the setting, the side file, a
 *  reader, and its `verify-bars` all have to be there. */
async function verifierFor(context: vscode.ExtensionContext, deps: OmrAiFixDeps, doc: vscode.TextDocument, side: ScanSideFile | undefined): Promise<Verifier | undefined> {
    if (!vscode.workspace.getConfiguration('lilysharp').get<boolean>('ai.omrFixVerify', true)) {
        log('  gate verify: off (lilysharp.ai.omrFixVerify)');
        return undefined;
    }
    if (!side || doc.uri.scheme !== 'file') {
        log('  gate verify: no side file beside the .lys — not run');
        return undefined;
    }
    const where = await findOmr(context);
    if (!where) {
        log('  gate verify: no OMR reader on this machine — not run');
        return undefined;
    }
    const launch = launchOf(where.path);
    if (!versions.has(where.path)) {
        versions.set(where.path, new Promise(resolve =>
            execFile(launch.command, [...launch.prefix, '--version', '--json'], { timeout: 20000, windowsHide: true },
                (err, stdout) => resolve(err ? undefined : parseVersion(stdout)))));
    }
    const v = await versions.get(where.path);
    if (!v || !canVerifyBars(v)) {
        log(`  gate verify: the reader at ${where.path}${v ? ` (${v.version})` : ''} has no 'verify-bars' — not run`);
        return undefined;
    }
    return { launch, lysc: deps.lysc(), lysPath: doc.uri.fsPath };
}

/**
 * Gate ③: the reader compares the proposal — the whole file with the bar rewritten, in a temp
 * file — with the scan, for this one bar (`omr verify x.lys --candidate tmp --bars N --no-write`),
 * and the gate reads its `bar` events (verifyVerdict). Undefined when it could not say.
 */
async function verifyProposal(run: FixRun, target: FixTarget, spliced: string, token: vscode.CancellationToken): Promise<{ ok: boolean; memos: string[] } | undefined> {
    const verifier = run.verifier;
    const bar = readerBarOf(target, run.side);
    if (!verifier || !bar) {
        return undefined;
    }
    const tmp = path.join(os.tmpdir(), `lilysharp-omr-fix-${process.pid}-${Date.now()}-${Math.floor(Math.random() * 1e6)}.lys`);
    const abort = new AbortController();
    const sub = token.onCancellationRequested(() => abort.abort());
    try {
        fs.writeFileSync(tmp, spliced, 'utf8');
        const checks: BarCheck[] = [];
        const result = await runOmr(verifier.launch,
            verifyArguments(verifier.lysPath, verifier.lysc, { candidate: tmp, bars: [bar.measure], noWrite: true }),
            e => { if (e.event === 'bar') { checks.push(e); } }, () => { }, abort.signal);
        const meaning = exitMeaning(result.exitCode);
        if (meaning !== 'ok' && meaning !== 'partial') {
            if (meaning !== 'stopped') {
                log(`  ${labelOf(target)}: verify did not run (exit ${result.exitCode}): ${result.errors.concat(result.stderrTail).slice(-2).join(' / ')}`);
            }
            return undefined;
        }
        return verifyVerdict(checks, bar);
    } catch (err) {
        log(`  ${labelOf(target)}: verify could not start: ${err}`);
        return undefined;
    } finally {
        sub.dispose();
        try { fs.unlinkSync(tmp); } catch { /* gone already */ }
    }
}

let output: vscode.OutputChannel | undefined;
let panel: vscode.WebviewPanel | undefined;
let shownRun: FixRun | undefined;

/** Registers the command. Its three doors (design §2.1) are its arguments: none from the palette
 *  and the editor's menu (the active .lys); the document's uri from the scan view's button; the
 *  uri and the offset of one mark from the quick fix (the server's "Fix this bar with AI…"). */
export function registerOmrAiFix(context: vscode.ExtensionContext, deps: OmrAiFixDeps): void {
    extensionContext = context;
    context.subscriptions.push(
        // A fourth door for both (owner's request 2026-10-24): the preview's and the scan view's
        // context menu, on the bar right-clicked.
        vscode.commands.registerCommand('lilysharp.omrFixWithAi', (uri?: string | vscode.Uri | MenuContext, at?: number) => {
            const bar = menuBar(deps, uri);
            if (bar === null) {
                vscode.window.showInformationMessage('Lily#: right-click on a bar to fix it with AI.');
                return;
            }
            return bar ? runOmrAiFix(context, deps, bar.uri, bar.at)
                : runOmrAiFix(context, deps, uri as string | vscode.Uri | undefined, Number.isInteger(at) ? at : undefined);
        }),
        vscode.commands.registerCommand('lilysharp.editBarsBesideScan', (uri?: string | vscode.Uri | MenuContext) => {
            const bar = menuBar(deps, uri);
            if (bar === null) {
                vscode.window.showInformationMessage('Lily#: right-click on a bar to edit it beside the scan.');
                return;
            }
            return bar ? runEditBars(context, deps, bar.uri, bar.at) : runEditBars(context, deps, uri as string | vscode.Uri | undefined);
        }),
        { dispose: () => { panel?.dispose(); clearBarMark(); output?.dispose(); } });
}

function log(line: string): void {
    output ??= vscode.window.createOutputChannel('Lily# AI');
    output.appendLine(`${new Date().toISOString().slice(11, 19)} ${line}`);
}

/** The document the command is about, in an editor: the one named, else the active .lys. */
async function editorFor(uriArg?: string | vscode.Uri): Promise<vscode.TextEditor | undefined> {
    if (uriArg === undefined) {
        const active = vscode.window.activeTextEditor;
        return active && active.document.languageId === 'lilysharp' ? active : undefined;
    }
    const uri = typeof uriArg === 'string' ? vscode.Uri.parse(uriArg) : uriArg;
    const shown = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === uri.toString());
    if (shown) {
        return shown;
    }
    const doc = await vscode.workspace.openTextDocument(uri);
    return vscode.window.showTextDocument(doc, { viewColumn: vscode.ViewColumn.One, preserveFocus: true });
}

/** The reader's side file beside the document, read and parsed, when there is one. */
function sideOf(doc: vscode.TextDocument): { side?: ScanSideFile; sideText?: string } {
    const sidePath = doc.uri.scheme === 'file' ? sideFileOf(doc.uri.fsPath) : undefined;
    if (sidePath && fs.existsSync(sidePath)) {
        try {
            const sideText = fs.readFileSync(sidePath, 'utf8');
            return { side: parseSideFile(sideText, sidePath), sideText };
        } catch { /* no side file: the memos still are */ }
    }
    return {};
}

/** How many bars Edit Bars Beside the Scan opens at once (each row draws the score). */
const MAX_EDIT_BARS = 30;

/**
 * Edit Bars Beside the Scan (owner's request 2026-10-24): the bars on the selected lines — the
 * caret's line when nothing is selected — in the same panel as the AI's proposals, without the
 * AI: Now, the Scan and Edit, the third the bar as it stands, to click and edit, gated and drawn
 * as it is typed; Accept writes the checked bars as one edit.
 */
async function runEditBars(context: vscode.ExtensionContext, deps: OmrAiFixDeps, uriArg?: string | vscode.Uri, at?: number): Promise<void> {
    const editor = await editorFor(uriArg);
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open a .lys, put the caret on a bar (or select bars), then run this again.');
        return;
    }
    const client = deps.getClient();
    if (!client || !deps.isReady()) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }
    const doc = editor.document;
    const text = doc.getText();
    const lines: number[] = at !== undefined ? [doc.positionAt(pastSpace(text, at)).line] : [];
    for (const s of at !== undefined ? [] : editor.selections) {
        // A selection that ends at the start of a line does not take that line.
        const last = s.end.line > s.start.line && s.end.character === 0 ? s.end.line - 1 : s.end.line;
        for (let l = s.start.line; l <= last; l++) { lines.push(l); }
    }
    const { side, sideText } = sideOf(doc);
    // A line that carries marks is the AI command's target (its memos, keys, crop and readings), so
    // Ask AI on it is told what the reader saw; any other line is a bar with no mark.
    const todos = await client.sendRequest<TodosResponse>('lilysharp/todos', { textDocument: { uri: doc.uri.toString() } });
    const marked = todos.Todos ? fixTargets(text, todos.Todos, side, sideText ? markFactsOf(sideText) : undefined) : [];
    let targets = lineTargets(text, lines, side).map(t => marked.find(m => m.line === t.line) ?? t);
    if (targets.length === 0) {
        vscode.window.showInformationMessage('Lily#: no bar on the selected lines (a line with a bar line "|", or one the scan\'s reader wrote).');
        return;
    }
    if (targets.length > MAX_EDIT_BARS) {
        vscode.window.showInformationMessage(`Lily#: ${targets.length} bars selected — opening the first ${MAX_EDIT_BARS}.`);
        targets = targets.slice(0, MAX_EDIT_BARS);
    }
    await vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: 'Lily#: drawing the bars' },
        async () => {
            const opened = await runFor(context, deps, doc, side, client, true);
            if (opened) {
                await addRows(deps, opened.run, opened.fresh, targets, async row => { row.baseAfter = await baseAfterOf(opened.run, row.base, row.target); });
            }
        });
}

/**
 * The panel's run for this document: the one it shows when it shows this file as it is now — so
 * a second command adds rows to it (owner's request 2026-10-24: a second Edit Bars Beside the
 * Scan replaced the rows being edited) — else a new one, after asking when the panel holds work
 * (a check, an edit, a question to the AI) that a new one would drop.
 */
async function runFor(context: vscode.ExtensionContext, deps: OmrAiFixDeps, doc: vscode.TextDocument, side: ScanSideFile | undefined,
    client: LanguageClient, manual: boolean): Promise<{ run: FixRun; fresh: boolean } | undefined> {
    const old = panel ? shownRun : undefined;
    // The same file, as the panel knows it — opened, or changed only by the panel's own Accepts
    // (then the rows added now are opened on the document as it is: a new base).
    if (old && old.uri.toString() === doc.uri.toString()
        && (old.base.version === doc.version || old.written.includes(doc.version))) {
        if (old.base.version !== doc.version) {
            old.base = await baseOf(client, doc);
        }
        return { run: old, fresh: false };
    }
    if (old && old.rows.some(r => (r.state === 'pending' || r.state === 'skipped') && (r.edited !== undefined || r.ai || r.keepTodo))) {
        const why = old.uri.toString() === doc.uri.toString()
            ? `${path.basename(doc.uri.fsPath)} has changed since the panel opened`
            : `the panel holds the bars of ${path.basename(old.uri.fsPath)}`;
        const go = await vscode.window.showWarningMessage(`Lily#: ${why}. Open a new panel? The edits in it are dropped.`,
            { modal: true }, 'Open a New Panel');
        if (go !== 'Open a New Panel') {
            return undefined;
        }
    }
    if (old) {
        stopQueue(old);
    }
    const cfg = vscode.workspace.getConfiguration('lilysharp');
    const verifier = await verifierFor(context, deps, doc, side);
    const run: FixRun = {
        uri: doc.uri, base: await baseOf(client, doc), written: [], shownBases: new Set(), side,
        rows: [],
        images: cfg.get<boolean>('ai.omrFixImages', true) && !!side && side.pages.length > 0,
        pages: new Map(),
        ...(verifier ? { verifier } : {}),
        client,
        manual,
        queue: [],
    };
    return { run, fresh: true };
}

/**
 * The run's rows for these bars: a bar already in the panel is its row (shown, not doubled); the
 * others are new rows, `init` filling them in, added to the panel in place — the rows there keep
 * their edits — or drawn with a new panel. In the targets' order.
 */
async function addRows(deps: OmrAiFixDeps, run: FixRun, fresh: boolean, targets: readonly FixTarget[],
    init: (row: FixRow) => Promise<void> | void): Promise<FixRow[]> {
    const rows: FixRow[] = [], added: FixRow[] = [];
    for (const target of targets) {
        const have = run.rows.find(r => r.target.line === target.line && (r.state === 'pending' || r.state === 'skipped'));
        if (have) {
            rows.push(have);
            continue;
        }
        const row: FixRow = { index: run.rows.length, target, base: run.base, proposal: null, seconds: 0, state: 'pending' };
        await init(row);
        run.rows.push(row);
        rows.push(row);
        added.push(row);
    }
    if (fresh || !panel || shownRun !== run) {
        showPanel(deps, run);
    } else {
        if (added.length > 0) {
            // The picture of a base the page has not got yet (rows opened after an Accept) goes
            // along, into its symbol store.
            const symbols = [...new Set(added.map(r => r.base))].filter(b => !run.shownBases.has(b.id)).map(b => {
                run.shownBases.add(b.id);
                return symbolHtml(b);
            }).join('');
            const pageUris = pageUrisOf(panel.webview, run);
            void panel.webview.postMessage({ type: 'rows', html: added.map(r => rowHtml(run, r, pageUris)).join('\n'), symbols, ...headerOf(run) });
        }
        panel.reveal(deps.scanColumn(run.uri.toString()) ?? panel.viewColumn, true);
        if (rows.length > 0) {
            void panel.webview.postMessage({ type: 'reveal', index: rows[0].index });
        }
    }
    return rows;
}

/** The whole command: which bars → the estimate → the run → the panel. */
async function runOmrAiFix(context: vscode.ExtensionContext, deps: OmrAiFixDeps, uriArg?: string | vscode.Uri, at?: number): Promise<void> {
    const editor = await editorFor(uriArg);
    if (!editor || editor.document.languageId !== 'lilysharp') {
        vscode.window.showErrorMessage('Lily#: open the .lys an OMR reader wrote, then fix its marks with AI.');
        return;
    }
    const client = deps.getClient();
    if (!client || !deps.isReady()) {
        vscode.window.showErrorMessage('Lily#: language server not ready.');
        return;
    }
    const doc = editor.document;
    const text = doc.getText();

    // The marks, from the server (they move with the text), and the reader's side file (their
    // confidence, their place on the scan, its readings) when there is one.
    const todos = await client.sendRequest<TodosResponse>('lilysharp/todos', { textDocument: { uri: doc.uri.toString() } });
    if (todos.Error) {
        vscode.window.showErrorMessage(`Lily#: could not list the marks: ${todos.Error}`);
        return;
    }
    const { side, sideText } = sideOf(doc);
    let targets = fixTargets(text, todos.Todos ?? [], side, sideText ? markFactsOf(sideText) : undefined);
    if (targets.length === 0) {
        vscode.window.showInformationMessage(`Lily#: ${path.basename(doc.uri.fsPath)} carries no @todo mark to fix.`);
        return;
    }
    let chosen: FixTarget[] | undefined;
    if (at !== undefined) {
        // The quick fix: the one bar the mark is on — no list, no question.
        const [from, to] = lineAround(text, pastSpace(text, at));
        chosen = targets.filter(t => t.start >= from && t.end <= to);
        if (chosen.length === 0) {
            const go = await vscode.window.showInformationMessage(
                'Lily#: that bar carries no @todo mark. Edit Bars Beside the Scan opens it without the AI.', 'Edit Bars Beside the Scan');
            if (go) {
                await runEditBars(context, deps, doc.uri, at);
            }
            return;
        }
    } else {
        // A selection narrows the marks to its lines, all of them checked.
        const selected = uriArg === undefined && !editor.selection.isEmpty;
        if (selected) {
            targets = targetsInLines(targets, editor.selection.start.line, editor.selection.end.line);
            if (targets.length === 0) {
                vscode.window.showInformationMessage('Lily#: the selection carries no @todo mark.');
                return;
            }
        }
        const threshold = deps.scanThreshold(doc.uri.toString()) ?? SCAN_DEFAULT_THRESHOLD;
        const perBar = averageOf(context.globalState.get<number[]>(TIMINGS_KEY, []));
        chosen = await pickTargets(targets, t => selected || checkedByDefault(t, threshold), perBar);
    }
    if (!chosen || chosen.length === 0) {
        return;
    }

    const chat = await resolveChatClient(false, 'omrFix');
    if (!chat) {
        return;
    }
    // Many bars: say so once, before any is asked (lilysharp.ai.omrFixBatch). The panel opens at
    // once and fills in as the answers come, so the first ones can be reviewed while the rest wait.
    const limit = vscode.workspace.getConfiguration('lilysharp').get<number>('ai.omrFixBatch', 25);
    if (chosen.length > limit) {
        const all = `All ${chosen.length}`, first = `The first ${limit}`;
        const go = await vscode.window.showInformationMessage(
            `Lily#: ${chosen.length} bars — ${estimateOf(chosen.length, averageOf(context.globalState.get<number[]>(TIMINGS_KEY, [])))}, one request a bar. Ask about all of them?`,
            { modal: true }, all, first);
        if (!go) {
            return;
        }
        if (go === first) {
            chosen = chosen.slice(0, limit);
        }
    }
    const opened = await runFor(context, deps, doc, side, client, false);
    if (!opened) {
        return;
    }
    const run = opened.run;
    run.ai ??= { chat, system: systemPromptOf(await loadGrammar(deps)), head: headOf(run.base.text) };
    log(`Fix OMR marks: ${path.basename(doc.uri.fsPath)} — ${chosen.length} of ${targets.length} bars via ${run.ai.chat.label}, ${scanSaid(run)}`);
    // New rows are made waiting, so a new panel draws them so before its page can hear a message.
    askAi(deps, run, await addRows(deps, run, opened.fresh, chosen, row => { row.ai = 'queued'; }));
}

/** What the log says of the scan going to the model. */
function scanSaid(run: FixRun): string {
    const on = vscode.workspace.getConfiguration('lilysharp').get<boolean>('ai.omrFixImages', true);
    return `scan ${run.images ? 'shown to the model' : on ? 'not available (no page beside the file)' : 'off (lilysharp.ai.omrFixImages)'}`;
}

/** The QuickPick: every bar, the least sure first, the ones below the slider checked; the title
 *  carries the estimate for what is checked (design §2.2). Undefined when dismissed. */
async function pickTargets(
    targets: readonly FixTarget[], checked: (t: FixTarget) => boolean, perBar: number | undefined): Promise<FixTarget[] | undefined> {
    type Item = vscode.QuickPickItem & { target: FixTarget };
    const items: Item[] = targets.map(t => ({
        label: labelOf(t),
        description: t.memos.join('; ') || (t.keys.length ? t.keys.join(' ') : 'a mark written by hand'),
        detail: `line ${t.line + 1}` + (t.confidence === undefined ? '' : ` · reader's confidence ${t.confidence.toFixed(2)}`),
        picked: checked(t),
        target: t,
    }));
    const pick = vscode.window.createQuickPick<Item>();
    pick.canSelectMany = true;
    pick.matchOnDescription = true;
    pick.items = items;
    pick.selectedItems = items.filter(i => i.picked);
    pick.placeholder = 'The bars an OMR reader marked, least sure first. Check the ones to rewrite with AI.';
    const retitle = () => {
        const n = pick.selectedItems.length;
        pick.title = `Lily# — fix OMR marks with AI: ${n === 0 ? 'nothing checked' : `${estimateOf(n, perBar)}, one request a bar`}`;
    };
    retitle();
    pick.onDidChangeSelection(retitle);
    return new Promise<FixTarget[] | undefined>(resolve => {
        pick.onDidAccept(() => {
            const chosen = pick.selectedItems.map(i => i.target);
            pick.hide();
            resolve(chosen);
        });
        pick.onDidHide(() => { pick.dispose(); resolve(undefined); });
        pick.show();
    });
}

// ---------------------------------------------------------------------------------------------
// The AI, a row at a time in the background (owner's request 2026-10-24)
// ---------------------------------------------------------------------------------------------

/** The extension's context, for the timings the estimate reads (registerOmrAiFix sets it). */
let extensionContext: vscode.ExtensionContext | undefined;

/** The model, the system prompt and the head for a run: asked for the first time a row is put
 *  to the AI; undefined when there is no model (the setup was offered). */
async function aiOf(deps: OmrAiFixDeps, run: FixRun): Promise<FixRun['ai']> {
    if (!run.ai) {
        const chat = await resolveChatClient(false, 'omrFix');
        if (!chat) {
            return undefined;
        }
        run.ai = { chat, system: systemPromptOf(await loadGrammar(deps)), head: headOf(run.base.text) };
        log(`Fix OMR marks: ${path.basename(run.uri.fsPath)} — asked from the panel via ${chat.label}, ${scanSaid(run)}`);
    }
    return run.ai;
}

/** Puts rows to the AI: each waits in the run's queue (its row says so, with a cancel), and the
 *  queue is worked in the background while the panel stays in the person's hands. */
function askAi(deps: OmrAiFixDeps, run: FixRun, rows: readonly FixRow[]): void {
    for (const row of rows) {
        // A row already waiting or being asked stays where it is; one marked "queued" as it was
        // made (drawn so by a new panel) joins the queue now.
        if (row.state === 'applied' || run.queue.includes(row) || run.current?.row === row) {
            continue;
        }
        row.ai = 'queued';
        run.queue.push(row);
        postRow(run, row);
    }
    void pump(deps, run);
}

/** Takes a row off the AI: out of the queue, or its question cancelled. */
function cancelAsk(run: FixRun, row: FixRow): void {
    if (run.current?.row === row) {
        run.current.cts.cancel();
        return;   // pump puts the row back as it was
    }
    const at = run.queue.indexOf(row);
    if (at >= 0) {
        run.queue.splice(at, 1);
    }
    row.ai = undefined;
    postRow(run, row);
}

/** Stops a run's AI: the queue emptied, the question being asked cancelled. */
function stopQueue(run: FixRun): void {
    run.closed = true;
    for (const row of run.queue.splice(0)) {
        row.ai = undefined;
    }
    run.current?.cts.cancel();
}

/** What a row puts to the model: its bar, and the words typed beside its @todo box as a
 *  person's note (what to look at — on a bar with no mark the only words there are). */
function promptTargetOf(row: FixRow): FixTarget {
    const memo = row.todoMemo?.trim();
    return memo ? { ...row.target, notes: [...(row.target.notes ?? []), memo] } : row.target;
}

/**
 * Works the queue, one bar at a time: the prompt (with the person's edit so far as their
 * attempt), the gates, the picture — then that row alone is put back on the page. An answer for
 * a row the person edited meanwhile leaves their edit standing ("use the AI's" brings it in). The
 * first answer coming back empty stops the queue, and says which model and where to pick another.
 */
async function pump(deps: OmrAiFixDeps, run: FixRun): Promise<void> {
    if (run.working || run.queue.length === 0) {
        return;
    }
    run.working = true;
    try {
        const ai = await aiOf(deps, run);
        if (!ai) {
            for (const row of run.queue.splice(0)) {
                row.ai = undefined;
                postRow(run, row);
            }
            return;
        }
        const answered: FixRow[] = [];
        await vscode.window.withProgress({ location: vscode.ProgressLocation.Window, title: 'Lily# AI' }, async progress => {
            while (run.queue.length > 0 && !run.closed) {
                const row = run.queue.shift()!;
                row.ai = 'asking';
                postRow(run, row);
                progress.report({ message: `${labelOf(row.target)}${run.queue.length ? ` — ${run.queue.length} waiting` : ''}` });
                const cts = new vscode.CancellationTokenSource();
                run.current = { row, cts };
                const started = Date.now();
                const image = run.images ? scanOf(run, row.target) : undefined;
                const answer = await fixOne(run.client, ai.chat, run, row.base, ai.system, ai.head, promptTargetOf(row), image,
                    cts.token, row.edited);
                const cancelled = cts.token.isCancellationRequested;
                run.current = undefined;
                cts.dispose();
                row.ai = undefined;
                if (cancelled || run.closed) {
                    postRow(run, row);
                    continue;
                }
                const seconds = (Date.now() - started) / 1000;
                Object.assign(row, { proposal: answer.proposal, gate: answer.gate, after: answer.after, noAnswer: answer.noAnswer, seconds, asked: true });
                if (!hasProposal(row) && !row.baseAfter) {
                    row.baseAfter = await baseAfterOf(run, row.base, row.target);
                }
                postRow(run, row);
                answered.push(row);
                if (extensionContext) {
                    await extensionContext.globalState.update(TIMINGS_KEY, withSample(extensionContext.globalState.get<number[]>(TIMINGS_KEY, []), seconds));
                }
                const sent = ai.chat.sentImages?.();
                const picture = !run.images ? '' : !image ? ' · no scan for this bar' : sent === false ? ' · scan not taken by the model (sent as text)'
                    : ` · scan ${image.width}×${image.height}`;
                log(`  ${labelOf(row.target)}: ${seconds.toFixed(1)} s${picture}${row.edited !== undefined ? ' · from the person\'s edit' : ''} — ${outcomeOf(answer)}`);
                if (run.images && image && sent === false && !saidRefused) {
                    saidRefused = true;
                    log(`  ${ai.chat.label} takes no image: the bars go as text from here on (pick another model with "Lily#: Select AI Model…").`);
                }
                // The first bar came back empty: so will the rest. Stop, and say which model and
                // where to pick another, rather than a list of empty rows.
                if (answered.length === 1 && answer.noAnswer?.endsWith('returned nothing')) {
                    for (const r of run.queue.splice(0)) {
                        r.ai = undefined;
                        postRow(run, r);
                    }
                    const label = ai.chat.label;
                    run.ai = undefined;   // the next ask picks a model again
                    const noModel = !hasChatModel(await availableModels());
                    log(`Fix OMR marks: ${label} returned nothing — stopped. `
                        + (noModel ? 'VS Code offers no chat model (GitHub Copilot is not signed in).' : 'Pick a chat model with "Lily#: Select AI Model…" → Fix OMR marks with AI.'));
                    if (noModel) {
                        void offerModelSetup();
                    } else {
                        void vscode.window.showWarningMessage(
                            `Lily#: the model ${label} returned nothing, so the AI stopped. Pick another model for "Fix OMR marks with AI".`,
                            'Select AI Model…').then(c => { if (c) { void vscode.commands.executeCommand('lilysharp.selectAiModel'); } });
                    }
                }
            }
        });
        if (answered.length > 0) {
            const verified = answered.filter(r => r.gate?.verify !== undefined);
            log(`Fix OMR marks: ${answered.length} bars · ${answered.filter(r => r.proposal?.bar).length} proposals · ${scanSaid(run)}`
                + ` · verify ${run.verifier ? `${verified.filter(r => r.gate!.verify).length} ✓ ${verified.filter(r => !r.gate!.verify).length} ✗` : 'off'}`
                + ` · ${answered.reduce((s, r) => s + r.seconds, 0).toFixed(0)} s`);
        }
    } finally {
        run.working = false;
    }
    // Rows asked while the last answer was being drawn.
    if (run.queue.length > 0 && !run.closed) {
        void pump(deps, run);
    }
}
let saidRefused = false;

/** The crop of the scan around a bar, as a PNG for the model (design §3.1 ④); undefined when the
 *  bar has no box, or its page cannot be read. A page is decoded once a run. */
function scanOf(run: FixRun, target: FixTarget): { data: Uint8Array; mime: string; width: number; height: number } | undefined {
    if (!target.crop || !run.side) {
        return undefined;
    }
    let page = run.pages.get(target.crop.page);
    if (page === undefined) {
        const info = run.side.pages.find(p => p.page === target.crop!.page);
        const file = info ? shownImage(info) : undefined;
        try {
            page = file && fs.existsSync(file) ? decodeImage(fs.readFileSync(file)) ?? null : null;
        } catch {
            page = null;
        }
        run.pages.set(target.crop.page, page);
        if (page === null) {
            log(`  page ${target.crop.page}: ${file ? 'cannot be decoded (PNG or JPEG only)' : 'no image to crop'} — its bars go as text`);
        }
    }
    if (!page) {
        return undefined;
    }
    const crop = scanCropPng(page, target.crop.box);
    return { data: crop.png, mime: 'image/png', width: crop.width, height: crop.height };
}

function outcomeOf(row: Answer): string {
    if (!row.proposal) {
        return `no answer (${row.noAnswer ?? 'the reply was not the JSON asked for'})`;
    }
    if (!row.proposal.bar) {
        return `no proposal (${row.proposal.reason || 'the model gave no reason'})`;
    }
    const gates = row.gate
        ? `compile ${row.gate.compile ? '✓' : '✗'} · length ${row.gate.length ? '✓' : '✗'} · verify ${row.gate.verify === undefined ? '—' : row.gate.verify ? '✓' : '✗'}`
        : 'ungated';
    return `proposal ${JSON.stringify(row.proposal.bar)} (${gates}${row.proposal.confidence === null ? '' : `, confidence ${row.proposal.confidence.toFixed(2)}`})`
        + (row.proposal.reason ? ` — ${row.proposal.reason}` : '');
}

/** One bar: the prompt → the model → the gates, once more with the compiler's words on a fail.
 *  `base`: the text the bar's row was opened on; `attempt`: the person's edit of the bar so far,
 *  shown to the model as theirs. */
async function fixOne(
    client: LanguageClient, chat: ChatClient, run: FixRun, base: RunBase, system: string, head: string,
    target: FixTarget, image: { data: Uint8Array; mime: string } | undefined, token: vscode.CancellationToken, attempt?: string,
): Promise<Answer> {
    const baseline = base.baseline;
    const neighbours: BarNeighbours = neighboursOf(base.text, target, run.side);
    const messages: ChatMessage[] = [
        { role: 'system', content: system },
        {
            role: 'user',
            content: taskPromptOf({ head, target, neighbours, diagnostics: diagnosticsOn(baseline, target), image: !!image, attempt }),
            ...(image ? { images: [image] } : {}),
        },
    ];
    let last: { proposal: Proposal; gate: GateVerdict; spliced: string } | undefined;
    const render = (spliced: string) => client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: spliced });
    for (let attempt = 0; attempt <= MAX_REPAIRS; attempt++) {
        let raw: string;
        try {
            raw = await chat.send(messages, token);
        } catch (err: any) {
            const why = `the model failed: ${err?.message ?? err}`;
            log(`  ${labelOf(target)}: ${why}`);
            return { proposal: null, noAnswer: why };
        }
        if (raw.trim() === '') {
            // Nothing at all is not a malformed answer: asking again gets nothing again. A model
            // that cannot hold a plain conversation (the owner's copilotcli, 2026-10-23) does this.
            return { proposal: null, noAnswer: `${chat.label} returned nothing` };
        }
        const proposal = parseProposal(raw, target.written);
        if (!proposal) {
            const head = raw.trim().replace(/\s+/g, ' ').slice(0, 160);
            log(`  ${labelOf(target)}: not the JSON asked for: ${head}${raw.trim().length > 160 ? '…' : ''}`);
            // Not the JSON asked for: once more, with the contract restated.
            if (attempt < MAX_REPAIRS) {
                messages.push({ role: 'assistant', content: raw });
                messages.push({ role: 'user', content: 'Answer with the one JSON object only: {"bar": …, "reason": …, "confidence": …}.' });
                continue;
            }
            return { proposal: null, noAnswer: `not the JSON asked for: "${head}"` };
        }
        if (!proposal.bar) {
            return { proposal };
        }
        const spliced = spliceBar(base.text, target, proposal.bar);
        const check = await client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: spliced });
        let gate = gateOf(baseline, check.Diagnostics ?? [], { start: target.start, length: proposal.bar.length });
        if (gate.compile && gate.length && run.verifier) {
            // Gate ③: the scan's word on a bar that compiles and fills its meter (a few seconds).
            gate = withVerify(gate, await verifyProposal(run, target, spliced, token));
        }
        if (gate.compile && gate.length && gate.verify !== false) {
            return { proposal, gate, after: await render(spliced) };
        }
        last = { proposal, gate, spliced };
        if (attempt < MAX_REPAIRS) {
            messages.push({ role: 'assistant', content: raw });
            messages.push({ role: 'user', content: repairPromptOf(gate.problems) });
        }
    }
    // Out of repairs. A proposal the scan alone contradicts is still shown — the comparison
    // reports ties and slurs too — with "verify ✗", out of Accept all's reach; one that does
    // not compile or fill its bar is a row saying what failed ("no proposal (reason)"), never
    // silently dropped.
    const { proposal, gate, spliced } = last!;
    if (gate.compile && gate.length) {
        return { proposal, gate, after: await render(spliced) };
    }
    return { proposal, gate };
}

// ---------------------------------------------------------------------------------------------
// The panel: Lily# — OMR fixes (design §2.3)
// ---------------------------------------------------------------------------------------------

function showPanel(deps: OmrAiFixDeps, run: FixRun): void {
    shownRun = run;
    const roots = [vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts'), textFontsRoot(deps.extensionUri)];
    for (const p of run.side?.pages ?? []) {
        const shown = shownImage(p);
        if (shown) { roots.push(vscode.Uri.file(path.dirname(shown))); }
    }
    // In the scan view's column when the file has one open (owner's request 2026-10-24: opened
    // from the preview it landed there, "Beside" the preview, and that was the handy place; from
    // the scan view itself "Beside" made a new column), else beside the active one.
    const column = deps.scanColumn(run.uri.toString()) ?? vscode.ViewColumn.Beside;
    if (!panel) {
        panel = vscode.window.createWebviewPanel('lilysharpOmrFixes', 'Lily# — OMR fixes',
            { viewColumn: column, preserveFocus: true },
            { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: roots });
        panel.onDidDispose(() => {
            // The AI stops with the panel: nothing is left to show its answers.
            if (shownRun) { stopQueue(shownRun); }
            panel = undefined; shownRun = undefined; clearBarMark();
        });
        panel.webview.onDidReceiveMessage((m: PanelMessage) => void onPanelMessage(deps, m));
    } else {
        panel.webview.options = { ...panel.webview.options, localResourceRoots: roots };
    }
    panel.title = run.manual ? 'Lily# — Bars beside the scan' : 'Lily# — OMR fixes';
    panel.webview.html = panelHtml(deps, panel.webview, run);
    panel.reveal(column, true);
}

/** What the panel's page posts. `keeps` (with accept): the checked rows taken with a @todo still
 *  on them, and their memos. */
interface PanelMessage {
    action?: string;
    index?: number;
    on?: boolean;
    text?: string;
    seq?: number;
    indices?: number[];
    keeps?: { index: number; text: string }[];
}

async function onPanelMessage(deps: OmrAiFixDeps, m: PanelMessage): Promise<void> {
    const run = shownRun;
    if (!run || !panel) {
        return;
    }
    const found = Number.isInteger(m.index) ? run.rows.find(r => r.index === m.index) : undefined;
    const row = found && isLive(found) ? found : undefined;
    // Each row is accepted or discarded on its own, and leaves the panel (owner's request
    // 2026-10-24: a bar the person has looked at is done with there and then); Accept All writes
    // the rows that are ready as ONE edit; Discard All closes the panel writing nothing more. The
    // panel closes by itself when its last row goes.
    switch (m.action) {
        case 'keep':
            if (row) {
                const toggled = m.on !== undefined && !!m.on !== !!row.keepTodo;
                if (m.on !== undefined) { row.keepTodo = !!m.on; }   // a memo typed sends its words alone
                row.todoMemo = m.text ?? row.todoMemo;
                if (toggled) { postRow(run, row); }   // what Accept writes, and its word, change
            }
            return;
        case 'edit':
            if (row && typeof m.text === 'string') { await editRow(run, row, m.text, m.seq ?? 0); }
            return;
        case 'acceptRow':
            if (row) {
                if (row.ai) { cancelAsk(run, row); }
                const written = await applyRows(run, [row]);
                removeRows(run, written);
                if (!written.length) { postRow(run, row); }
            }
            return;
        case 'discard':
            if (row) {
                if (row.ai) { cancelAsk(run, row); }
                row.state = 'discarded';
                removeRows(run, [row]);
            }
            return;
        case 'acceptAll': {
            const ready = run.rows.filter(r => isLive(r) && isReady(r));
            if (ready.length === 0) {
                vscode.window.showInformationMessage('Lily#: no bar is ready to write — accept or edit them one by one, or Discard All.');
                return;
            }
            const written = await applyRows(run, ready);
            removeRows(run, written);
            for (const r of ready.filter(r => !written.includes(r))) { postRow(run, r); }
            return;
        }
        case 'ready':
            // The page has its script: what the AI is doing now may have been posted before it
            // could hear (a new panel asks at once), so the rows it is about are sent again.
            for (const r of run.rows) {
                if (r.ai) { postRow(run, r); }
            }
            return;
        case 'ask':
            // The row's Ask AI (owner's request 2026-10-24): queued, answered in the background.
            if (row) { askAi(deps, run, [row]); }
            return;
        case 'unask':
            if (row) { cancelAsk(run, row); }
            return;
        case 'focus':
            if (row) { await showBar(run, row); }
            return;
        case 'cancel':
            log(`Fix OMR marks: Discard All in the panel — ${run.rows.filter(isLive).length} bars left unwritten`
                + ` (${run.rows.filter(r => r.state === 'applied').length} written before).`);
            panel.dispose();
            return;
        default:
            return;
    }
}

/** A row still in the panel: not written, not discarded. */
function isLive(r: FixRow): boolean {
    return r.state === 'pending' || r.state === 'skipped';
}

/** A row Accept All writes: the person's edit that compiles and fills its bar, or the AI's bar
 *  that passes every gate and is sure enough (acceptableByAll, what was checked at first) — not
 *  a row that would only take its marks out, nor one the AI is still about. */
function isReady(r: FixRow): boolean {
    if (r.ai) {
        return false;
    }
    if (r.edited !== undefined) {
        return !!r.editGate && r.editGate.compile && r.editGate.length;
    }
    return hasProposal(r) && !!r.gate && acceptableByAll(r.gate, r.proposal!.confidence);
}

/** Takes rows off the page (written or discarded); the panel closes when none is left. */
function removeRows(run: FixRun, rows: readonly FixRow[]): void {
    if (!panel || shownRun !== run || rows.length === 0) {
        return;
    }
    if (!run.rows.some(isLive)) {
        log(`Fix OMR marks: the panel's last bar is done — ${run.rows.filter(r => r.state === 'applied').length} written.`);
        panel.dispose();
        return;
    }
    void panel.webview.postMessage({ type: 'remove', indices: rows.map(r => r.index), ...headerOf(run) });
}

/** Whether the row has a bar of the model's to write: one that compiles and fills its meter (a
 *  proposal the scan contradicts, verify ✗, is the person's call and counts). */
function hasProposal(r: FixRow): boolean {
    return !!r.proposal?.bar && !(r.gate && !(r.gate.compile && r.gate.length));
}

/** What taking a row writes over its line: the model's bar, or — for every other row: the model
 *  read the bar as right ("unchanged"), gave no proposal, no answer, or one that did not pass —
 *  the line without its @todo marks, the notes as they are (owner's requests 2026-10-23: a bar
 *  one checks by eye is resolved too, so no row's box is disabled). */
function textOf(r: FixRow): string {
    const bar = barOf(r) ?? resolvedLine(r.target.written);
    return r.keepTodo ? withTodoMark(r.target.written, bar, r.todoMemo ?? '') ?? bar : bar;
}

/** The bar a row writes of its own: the person's edit, else the model's proposal that passed;
 *  undefined when taking the row only removes its marks. */
function barOf(r: FixRow): string | undefined {
    return r.edited !== undefined ? r.edited : hasProposal(r) ? r.proposal!.bar! : undefined;
}

/** The pending verify of each row's latest edit, cancelled by the next one. */
const editVerifies = new Map<number, vscode.CancellationTokenSource>();

/**
 * An edit of a row's bar in the panel (owner's request 2026-10-24): gated as a proposal is —
 * compile and length at once, the scan's verify after it when there is a reader that can — and
 * drawn, the row's parts sent back to the page as each answer comes. Text equal to what the row
 * would write anyway (the model's bar, or the line without its marks) is no edit.
 */
async function editRow(run: FixRun, row: FixRow, raw: string, seq: number): Promise<void> {
    row.editSeq = seq;
    editVerifies.get(row.index)?.cancel();
    editVerifies.delete(row.index);
    const text = raw.replace(/\s+/g, ' ').trim();
    const model = hasProposal(row) ? row.proposal!.bar! : resolvedLine(row.target.written).replace(/\s+/g, ' ').trim();
    if (text === '' || text === model) {
        row.edited = row.editGate = row.editAfter = undefined;
        postRow(run, row, seq);
        return;
    }
    row.edited = text;
    const spliced = spliceBar(row.base.text, row.target, text);
    const [check, after] = await Promise.all([
        run.client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: spliced }),
        run.client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: spliced }),
    ]);
    if (row.editSeq !== seq) {
        return;
    }
    const gate = gateOf(row.base.baseline, check.Diagnostics ?? [], { start: row.target.start, length: text.length });
    row.editGate = gate;
    row.editAfter = after;
    postRow(run, row, seq);
    if (gate.compile && gate.length && run.verifier) {
        const cts = new vscode.CancellationTokenSource();
        editVerifies.set(row.index, cts);
        const verdict = await verifyProposal(run, row.target, spliced, cts.token);
        if (row.editSeq !== seq || cts.token.isCancellationRequested) {
            return;
        }
        editVerifies.delete(row.index);
        row.editGate = withVerify(gate, verdict);
        postRow(run, row, seq);
    }
}

/** Sends one row's parts to the page, to replace in place (the text field keeps its focus).
 *  `seq`: the edit it answers (an answer to an older one is dropped there); `check`: the AI's
 *  answer checks the row's box. The panel's head goes along (its counts, its third column). */
function postRow(run: FixRun, row: FixRow, seq = row.editSeq ?? 0): void {
    if (!panel || shownRun !== run || !isLive(row)) {
        return;
    }
    const p = rowParts(run, row);
    void panel.webview.postMessage({ type: 'row', index: row.index, seq, gates: p.gates, reason: p.reason, proposal: p.proposal,
        now: p.nowText, after: p.afterText, del: p.del, accept: p.accept, plain: barOf(row) ?? resolvedLine(row.target.written),
        secs: secondsOf(run, row), ...headerOf(run) });
}

/** The head row's time: what the model took, once it has answered. */
function secondsOf(run: FixRun, row: FixRow): string {
    return row.asked ? ` · ${row.seconds.toFixed(1)} s` : '';
}

/** The panel's head: the line under its title, and its third column's name — Proposal once the
 *  AI has answered a row, Edit while every bar is the person's (owner's choice 2026-10-24). */
function headerOf(run: FixRun): { cap: string; third: string; ready: number } {
    const live = run.rows.filter(isLive);
    const n = live.length;
    const proposals = live.filter(hasProposal).length;
    const waiting = live.filter(r => r.ai).length;
    const asked = run.rows.some(r => r.asked || r.ai);
    const written = run.rows.filter(r => r.state === 'applied').length;
    return {
        cap: `${n} ${n === 1 ? 'bar' : 'bars'}`
            + (asked ? ` · ${proposals} with a proposal` : '')
            + (waiting ? ` · ${waiting} waiting for the AI` : '')
            + (written ? ` · ${written} written` : '')
            + ' · edit a bar (click a note or its text) or ask the AI; Accept writes a bar, Discard drops it',
        third: asked ? 'Proposal' : 'Edit',
        ready: live.filter(isReady).length,
    };
}

/** The light blue of the bar the panel is on, in the editor (owner's request 2026-10-23). */
let barMark: vscode.TextEditorDecorationType | undefined;

/** Shows the row's bar in the editor, as it is now (anchorOf follows a moved line), lit light
 *  blue, the panel keeping the focus: a click on a row or its box (owner's request 2026-10-23,
 *  in place of an "Open in editor" link). The preview follows the editor's caret on its own. */
async function showBar(run: FixRun, row: FixRow): Promise<void> {
    const doc = await vscode.workspace.openTextDocument(run.uri);
    const text = doc.getText();
    const at = anchorOf(text, row.target);
    const from = at >= 0 ? at : Math.min(row.target.start, text.length);
    const to = at >= 0 ? at + row.target.written.length : from;
    const shown = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === run.uri.toString());
    const editor = shown ?? await vscode.window.showTextDocument(doc, { viewColumn: vscode.ViewColumn.One, preserveFocus: true });
    const range = new vscode.Range(doc.positionAt(from), doc.positionAt(to));
    // The caret at the bar's start, nothing selected: a selection lit every note of the bar in
    // the preview and hid the bar's own tint (owner's report 2026-10-23).
    editor.selection = new vscode.Selection(range.start, range.start);
    editor.revealRange(range, vscode.TextEditorRevealType.InCenterIfOutsideViewport);
    barMark ??= vscode.window.createTextEditorDecorationType({
        backgroundColor: 'rgba(0, 120, 215, 0.22)',
        isWholeLine: true,
        overviewRulerColor: 'rgba(0, 120, 215, 0.8)',
        overviewRulerLane: vscode.OverviewRulerLane.Center,
    });
    for (const e of vscode.window.visibleTextEditors) { e.setDecorations(barMark, []); }
    editor.setDecorations(barMark, [range]);
}

function clearBarMark(): void {
    barMark?.dispose();
    barMark = undefined;
}

/** Writes the rows' proposals as ONE WorkspaceEdit (one Ctrl+Z): each at its line as it is now;
 *  a line that moved is followed, one that is gone or doubled is skipped and said so. The @todo
 *  marks go with the lines (a proposal carries none). */
async function applyRows(run: FixRun, accepted: readonly FixRow[]): Promise<FixRow[]> {
    if (accepted.length === 0) {
        return [];
    }
    const doc = await vscode.workspace.openTextDocument(run.uri);
    const current = doc.getText();
    const edit = new vscode.WorkspaceEdit();
    const skipped: FixRow[] = [];
    const written: FixRow[] = [];
    for (const r of accepted) {
        const at = anchorOf(current, r.target);
        if (at < 0) {
            skipped.push(r);
            continue;
        }
        edit.replace(run.uri, new vscode.Range(doc.positionAt(at), doc.positionAt(at + r.target.written.length)), textOf(r));
        written.push(r);
        if (r.keepTodo && textOf(r) === (barOf(r) ?? resolvedLine(r.target.written))) {
            log(`  ${labelOf(r.target)}: no note to keep the @todo on — written without it`);
        }
    }
    const ok = written.length > 0 && await vscode.workspace.applyEdit(edit);
    for (const r of skipped) { r.state = 'skipped'; }
    if (ok) {
        for (const r of written) { r.state = 'applied'; }
        // The version this edit made: a later command on the file still belongs to this panel.
        run.written.push((await vscode.workspace.openTextDocument(run.uri)).version);
        const what = written.length === 1 ? labelOf(written[0].target) : `${written.length} bars`;
        log(`Fix OMR marks: wrote ${what} (${run.rows.filter(r => r.state === 'applied').length} written so far, scan ${run.images ? 'on' : 'off'})`
            + (skipped.length ? `; ${skipped.length} skipped (the line changed and could not be found)` : ''));
        if (skipped.length) {
            vscode.window.showWarningMessage(`Lily#: wrote ${what}; `
                + `${skipped.map(r => labelOf(r.target)).join(', ')} could not be found as written — the line changed since the panel opened it.`);
        } else {
            vscode.window.setStatusBarMessage(`Lily#: wrote ${what} — Ctrl+Z undoes ${written.length === 1 ? 'it' : 'them'}`, 6000);
        }
        return written;
    } else if (written.length > 0) {
        vscode.window.showErrorMessage('Lily#: the edit could not be applied.');
    } else {
        vscode.window.showWarningMessage(`Lily#: ${skipped.map(r => labelOf(r.target)).join(', ')} could not be found as written — `
            + 'the line changed since the panel opened it, so nothing was written.');
    }
    return [];
}

// ---------------------------------------------------------------------------------------------
// The panel's page
// ---------------------------------------------------------------------------------------------

function esc(s: string): string {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/** A line of the word diff as HTML: removed pieces in <del>, added ones in <ins>. */
function piecesHtml(pieces: readonly DiffPiece[]): string {
    return pieces.map((p, i) => {
        const text = esc(shortTodos(p.text));
        const body = p.kind === 'del' ? `<del>${text}</del>` : p.kind === 'add' ? `<ins>${text}</ins>` : text;
        return (p.space && i > 0 ? ' ' : '') + body;
    }).join('');
}

function nonceOf(): string {
    return [...Array(24)].map(() => Math.floor(Math.random() * 36).toString(36)).join('');
}

/** The score SVG's root attributes and inner markup, so it can be put in a <symbol> and drawn
 *  cropped by many <use>s (one copy of the big "before" score, not one per row). */
function symbolOf(svg: string): { viewBox: string; x: number; y: number; w: number; h: number; inner: string } | undefined {
    const open = svg.match(/<svg\b[^>]*>/);
    const close = svg.lastIndexOf('</svg>');
    if (!open || close < 0) {
        return undefined;
    }
    const viewBox = /viewBox="([^"]+)"/.exec(open[0])?.[1] ?? '0 0 100 100';
    const [x, y, w, h] = viewBox.trim().split(/[\s,]+/).map(Number);
    return { viewBox, x, y, w, h, inner: svg.slice(open.index! + open[0].length, close) };
}

/** A score cell: the symbol `id` drawn at 1:1 in the cell's user space (the <use> as large as the
 *  symbol's viewBox), so the cell's own viewBox — set by the page script — crops it to the bar. */
function scoreCell(id: string, sym: NonNullable<ReturnType<typeof symbolOf>>, lo: number, hi: number, define: boolean,
    paint?: { kind: 'del' | 'add'; ranges: readonly [number, number][] }): string {
    // The notes to paint (the word diff's pieces, by the source offsets the drawn notes carry):
    // what the proposal takes out red in "Now", what it puts in green in "Proposal".
    const marks = paint && paint.ranges.length ? ` data-paint="${paint.kind}" data-ranges="${paint.ranges.map(r => r.join(':')).join(',')}"` : '';
    // A row's own picture (define) is drawn inline, not through a <use>: a click inside a <use>
    // is reported on the <use>, and the proposal's notes are clicked to edit them (owner's
    // request 2026-10-24). The shared "Now" stays one symbol for all the rows.
    return `<svg class="pic score${define ? ' own' : ''}" data-sym="${id}" data-lo="${lo}" data-hi="${hi}"${marks} viewBox="${esc(sym.viewBox)}" preserveAspectRatio="xMidYMid meet">`
        + (define ? `<g id="${id}">${sym.inner}</g>`
            : `<use href="#${id}" x="${sym.x}" y="${sym.y}" width="${sym.w}" height="${sym.h}"/>`) + `</svg>`;
}

/** The parts of a row that an edit changes — drawn with the page, and sent again in place
 *  (postRow) as an edit is gated and drawn. */
interface RowParts {
    gates: string;
    reason: string;
    /** The proposal's picture (a score cell, or a box saying why there is none). */
    proposal: string;
    /** The two lines piece by piece, under the pictures. */
    nowText: string;
    afterText: string;
    /** What the row takes out, to paint red in "Now" ("from:to,…"). */
    del: string;
    /** The row's Accept: its word, whether it writes anything, and whether the row is ready
     *  (what Accept All writes — the button is then the primary one). */
    accept: { word: string; can: boolean; ready: boolean };
}

function gatesHtml(g: GateVerdict | undefined): string {
    return g
        ? `<span class="${g.compile ? 'ok' : 'bad'}">compile ${g.compile ? '✓' : '✗'}</span> `
          + `<span class="${g.length ? 'ok' : 'bad'}">length ${g.length ? '✓' : '✗'}</span> `
          + (g.verify === undefined ? `<span class="dim">verify —</span>`
              : `<span class="${g.verify ? 'ok' : 'bad'}">verify ${g.verify ? '✓' : '✗'}</span>`)
        : `<span class="dim">—</span>`;
}

function rowParts(run: FixRun, r: FixRow): RowParts {
    const t = r.target;
    // The two lines piece by piece: struck through in red what goes, in green what comes —
    // in the text under the pictures and on the drawn notes.
    const diff = wordDiff(t.written, textOf(r));
    const indent = t.start - (r.base.text.lastIndexOf('\n', t.start - 1) + 1);
    const add = { kind: 'add' as const, ranges: rangesOf(diff.after, 'add', t.start, indent) };
    const del = rangesOf(diff.before, 'del', t.start, indent).map(x => x.join(':')).join(',');
    const edited = r.edited !== undefined;
    const gate = edited ? r.editGate : r.gate;
    const after = (edited ? r.editAfter : r.after)?.Svg;
    const sym = after ? symbolOf(after) : undefined;
    // A row without a bar of its own shows the bar as it stands, to click and edit.
    const base = r.baseAfter?.Svg ? symbolOf(r.baseAfter.Svg) : undefined;
    let proposal: string;
    if (edited) {
        proposal = sym ? scoreCell(`after-${r.index}`, sym, t.start, t.start + r.edited!.length, true, add)
            : `<div class="none">${gate ? 'not rendered' : 'checking…'}</div>`;
    } else if (barOf(r) === undefined && base) {
        proposal = scoreCell(`after-${r.index}`, base, t.start, t.start + resolvedLine(t.written).length, true, add);
    } else if (r.ai && !hasProposal(r)) {
        proposal = `<div class="none">${r.ai === 'asking' ? 'asking the AI…' : 'waiting for the AI…'} — or click the text below to edit it yourself</div>`;
    } else if (!r.proposal) {
        proposal = `<div class="none">no answer — Accept removes the @todo</div>`;
    } else if (!r.proposal.bar) {
        proposal = r.proposal.unchanged
            ? `<div class="none">unchanged — Accept removes the @todo</div>`
            : `<div class="none">no proposal — Accept removes the @todo</div>`;
    } else if (sym) {
        proposal = scoreCell(`after-${r.index}`, sym, t.start, t.start + r.proposal.bar.length, true, add);
    } else {
        proposal = `<div class="none">${r.gate && !(r.gate.compile && r.gate.length) ? 'did not pass — Accept removes the @todo' : 'not rendered'}</div>`;
    }
    const failed = r.gate && !(r.gate.compile && r.gate.length);
    // An edit goes back to the AI's bar when it has one — the answer that came while the person
    // was editing waits there (owner's request 2026-10-24) — else to the bar as it stands.
    const revert = hasProposal(r)
        ? `<button class="link revert" data-i="${r.index}" title="${esc(`The AI's bar: ${r.proposal!.bar!}`)}">use the AI's</button>`
        : `<button class="link revert" data-i="${r.index}" title="Back to the bar as it stands">undo my edit</button>`;
    // The AI's state, and the way to ask it: queued or being asked (with a cancel), or a button.
    const can = isLive(r);
    const aiNow = r.ai === 'asking' ? ` <span class="dim">— asking the AI…</span> <button class="link unask" data-i="${r.index}">cancel</button>`
        : r.ai === 'queued' ? ` <span class="dim">— waiting for the AI…</span> <button class="link unask" data-i="${r.index}">cancel</button>`
        : can ? ` <button class="link ask" data-i="${r.index}" title="Ask the AI to rewrite this bar from the scan — with your note and your edit, if any">${r.asked ? 'ask the AI again' : 'Ask AI'}</button>`
        : '';
    const sure = r.proposal?.confidence === null || r.proposal?.confidence === undefined || edited ? ''
        : ` <span class="dim">· confidence ${r.proposal.confidence.toFixed(2)}</span>`;
    const reason = edited
        ? esc(gate && gate.problems.length ? `Your edit: ${gate.problems.join(' / ')}` : gate ? 'Your edit.' : 'Your edit — checking…') + ` ${revert}`
        : !r.asked ? esc(r.ai ? '' : 'Click a note or the text under the picture to edit this bar.')
        : esc(!r.proposal ? `No answer: ${r.noAnswer ?? 'the reply was not the JSON asked for'}.`
            : r.proposal.unchanged ? `The model reads the bar as right${r.proposal.reason.replace(/^unchanged( — )?/, '') ? `: ${r.proposal.reason.replace(/^unchanged( — )?/, '')}` : '.'}`
            : !r.proposal.bar ? (failed ? `No proposal passed: ${r.gate!.problems.join(' / ') || r.proposal.reason}` : r.proposal.reason || 'The model gave no reason.')
            : r.gate?.verify === false ? `${r.proposal.reason ? `${r.proposal.reason} — ` : ''}${r.gate.problems.filter(p => p.startsWith('the scan says')).join(' / ')}`
            : r.proposal.reason || '');
    const hint = !edited && r.asked && !r.ai && barOf(r) === undefined && base
        ? ' <span class="dim">— click a note or the text to edit the bar yourself</span>' : '';
    const lost = r.state === 'skipped' ? '<span class="bad">Not written: the line has changed since the panel opened it.</span> ' : '';
    // Accept writes the row's bar (the person's or the AI's), else takes its marks out, else —
    // with keep/add @todo — writes the mark; a bar with none of these has nothing to write.
    const marked = resolvedLine(t.written) !== t.written;
    const own = barOf(r) !== undefined;
    return {
        gates: gatesHtml(gate), reason: lost + reason + sure + hint + aiNow, proposal, nowText: piecesHtml(diff.before), afterText: piecesHtml(diff.after), del,
        accept: { word: !own && marked && !r.keepTodo ? 'Accept (remove @todo)' : 'Accept', can: own || marked || !!r.keepTodo, ready: isReady(r) },
    };
}

/** A base's picture for the page's symbol store: every row opened on it draws its "Now" from it. */
function symbolHtml(base: RunBase): string {
    const sym = beforeOf(base);
    return sym ? `<svg class="store" aria-hidden="true"><symbol id="before-${base.id}" viewBox="${esc(sym.viewBox)}">${sym.inner}</symbol></svg>` : '';
}

/** The page images the rows crop, as the webview reaches them, by page number. */
function pageUrisOf(webview: vscode.Webview, run: FixRun): Map<number, { uri: string; width: number; height: number }> {
    const pageUris = new Map<number, { uri: string; width: number; height: number }>();
    for (const p of run.side?.pages ?? []) {
        const shown = shownImage(p);
        if (shown && fs.existsSync(shown)) {
            pageUris.set(p.page, { uri: webview.asWebviewUri(vscode.Uri.file(shown)).toString(), width: p.width, height: p.height });
        }
    }
    return pageUris;
}

/** A base's picture as a symbol, made once (every row opened on it draws its Now from it). */
const beforeSymbols = new WeakMap<RunBase, ReturnType<typeof symbolOf>>();
function beforeOf(base: RunBase): ReturnType<typeof symbolOf> {
    if (!beforeSymbols.has(base)) {
        beforeSymbols.set(base, base.before.Svg ? symbolOf(base.before.Svg) : undefined);
    }
    return beforeSymbols.get(base);
}

/** One bar's three rows of the table — drawn with the page, and added to it later (addRows). */
function rowHtml(run: FixRun, r: FixRow, pageUris: ReadonlyMap<number, { uri: string; width: number; height: number }>): string {
    const before = beforeOf(r.base);
    const t = r.target;
    const parts = rowParts(run, r);
    const page = t.crop ? pageUris.get(t.crop.page) : undefined;
    const crop = t.crop && page ? cropRect(t.crop.box, page) : undefined;
    const scan = crop && page
        ? `<svg class="pic" viewBox="${crop.x} ${crop.y} ${crop.width} ${crop.height}" preserveAspectRatio="xMidYMid meet">`
          + `<image href="${page.uri}" width="${page.width}" height="${page.height}"/></svg>`
        : `<div class="none">no scan</div>`;
    const now = before
        ? scoreCell(`before-${r.base.id}`, before, t.start, t.end, false, { kind: 'del', ranges: parts.del ? parts.del.split(',').map(s => s.split(':').map(Number) as [number, number]) : [] })
        : `<div class="none">not rendered</div>`;
    const can = isLive(r);
    // Each row is done with on its own (owner's request 2026-10-24, in place of a check box a
    // row and one Accept for the checked): Accept writes this bar now (its own Ctrl+Z) and the
    // row leaves; Discard drops the row, writing nothing. A ready row's Accept is the primary one.
    const box = can
        ? `<button class="row-accept${parts.accept.ready ? ' primary' : ''}" data-i="${r.index}" ${parts.accept.can ? '' : 'disabled'}`
          + ` title="Write this bar into the .lys now (Ctrl+Z undoes it) — without a bar of its own (the AI's or your edit), its @todo is removed and the notes stay">${parts.accept.word}</button>`
          + `<button class="row-discard" data-i="${r.index}" title="Take this bar off the panel, writing nothing">Discard</button>`
        : '';
    // Taken but still marked (owner's request 2026-10-24): the bar is written with a @todo at
    // the same note — the reader's key kept — and the words typed here as its memo. A bar that
    // carries no mark (one opened by Edit Bars Beside the Scan) gets one instead: "add", at its
    // first note (withTodoMark with no mark to follow).
    const marked = resolvedLine(t.written) !== t.written;
    const keepBox = can
        ? `<label class="take keep" title="${marked
            ? 'Write the bar with a @todo still on it, at the same note — for a bar that is better but not right yet'
            : 'Write the bar with a @todo on its first note — to come back to it'}">`
          + `<input type="checkbox" class="keep" data-i="${r.index}" ${r.keepTodo ? 'checked' : ''}> ${marked ? 'keep' : 'add'} @todo</label>`
          + `<input type="text" class="note" data-i="${r.index}" value="${esc(r.todoMemo ?? '')}" placeholder="${marked ? 'what is still wrong' : 'what to check'} (optional)" ${r.keepTodo ? '' : 'hidden'}>`
        : '';
    // Three rows a bar: a head across the whole width — the box, the bar, the reader's memo,
    // the line, the time, the way to the editor — then the model's reason and the gates,
    // across the whole width too, and under them the three pictures. A column for either
    // was a fraction of the width and wrapped its text into a dozen short lines (owner's
    // reports 2026-10-23). The pictures go the reader's, the scan, the proposal: the scan in
    // the middle sits beside both, and the proposal is the one a person compares it with
    // (owner, 2026-10-24: scan first put the proposal two pictures away from it).
    const cls = `row ${r.state}`;
    return `<tr class="${cls} head" data-i="${r.index}">
  <td colspan="3" class="who">${box}${keepBox} <span class="label">${esc(labelOf(t))}</span>`
        + (t.memos.length ? ` <span class="memo">${esc(t.memos.join('; '))}</span>` : '')
        + ` <span class="memo dim">· line ${t.line + 1}${t.confidence === undefined ? '' : ` · reader ${t.confidence.toFixed(2)}`}<span class="secs" data-i="${r.index}">${secondsOf(run, r)}</span></span></td>
</tr>
<tr class="${cls} reason" data-i="${r.index}">
  <td colspan="3" class="why"><span class="gates" data-i="${r.index}">${parts.gates}</span> <span class="reason-text" data-i="${r.index}">${parts.reason}</span></td>
</tr>
<tr class="${cls} body" data-i="${r.index}">
  <td class="cell">${now}<pre class="src now" data-i="${r.index}">${parts.nowText}</pre></td>
  <td class="cell">${scan}</td>
  <td class="cell"><div class="after-pic" data-i="${r.index}">${parts.proposal}</div>`
        // The bar to write, editable (owner's request 2026-10-24): a click opens it; the
        // picture and the gates follow the typing.
        + (can ? `<pre class="src after editable" data-i="${r.index}" title="Click to edit the bar — the picture and the gates follow">${parts.afterText}</pre>`
            + `<textarea class="edit" data-i="${r.index}" data-base="${t.start}" data-indent="${t.start - (r.base.text.lastIndexOf('\n', t.start - 1) + 1)}" spellcheck="false" hidden>${esc(barOf(r) ?? resolvedLine(t.written))}</textarea>`
            : `<pre class="src after" data-i="${r.index}">${parts.afterText}</pre>`) + `</td>
</tr>`;
}

function panelHtml(deps: OmrAiFixDeps, webview: vscode.Webview, run: FixRun): string {
    const nonce = nonceOf();
    const fontUri = webview.asWebviewUri(vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts', 'emmentaler-20.woff2'));
    const braceUri = webview.asWebviewUri(vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts', 'emmentaler-brace.woff2'));
    const pageUris = pageUrisOf(webview, run);
    const live = run.rows.filter(isLive);
    const rows = live.map(r => rowHtml(run, r, pageUris)).join('\n');
    // The pictures of the bases the rows were opened on (one, until a row is added after an Accept).
    run.shownBases.clear();
    const store = [...new Set(live.map(r => r.base))].map(b => { run.shownBases.add(b.id); return symbolHtml(b); }).join('');
    const head = headerOf(run);
    const name = path.basename(run.uri.fsPath);

    return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource}; style-src 'unsafe-inline'; font-src ${webview.cspSource}; script-src 'nonce-${nonce}';">
<style>
@font-face { font-family: 'Emmentaler'; src: url('${fontUri}') format('woff2'); }
@font-face { font-family: 'Emmentaler-Brace'; src: url('${braceUri}') format('woff2'); }
${textFontFaceCss(webview, deps.extensionUri)}
body { margin:0; padding:0; display:flex; flex-direction:column; height:100vh; overflow:hidden;
       font-family: var(--vscode-font-family, system-ui, sans-serif); font-size: var(--vscode-font-size, 13px);
       background: var(--vscode-editor-background); color: var(--vscode-foreground); }
.header { padding:8px 12px; border-bottom:1px solid var(--vscode-panel-border); flex-shrink:0; display:flex; align-items:center; gap:12px; }
.header .title { font-weight:600; }
.header .cap { opacity:.8; font-size:12px; }
.spacer { flex:1; }
.main { flex:1; overflow:auto; }
table { border-collapse:collapse; width:100%; }
th { text-align:left; font-weight:600; font-size:12px; opacity:.8; padding:6px 8px; position:sticky; top:0;
     background: var(--vscode-editor-background); border-bottom:1px solid var(--vscode-panel-border); }
td { vertical-align:top; padding:6px 8px; border-bottom:1px solid var(--vscode-panel-border); }
/* A row's Accept and Discard, first on its head line. */
.who button { padding:2px 10px; margin:0 6px 0 0; vertical-align:middle; }
/* The bar the editor shows (the last row clicked): an outline round its three rows. */
tr.cur.head > td { box-shadow: inset 0 2px 0 #0078d7; }
tr.cur > td:first-child { box-shadow: inset 3px 0 0 #0078d7; }
tr.row { cursor: pointer; }
tr.applied { opacity:.6; }
label.take { font-size:12px; margin-right:8px; cursor:pointer; user-select:none; }
label.take.dim { cursor:default; }
label.take input { vertical-align:middle; margin:0 3px 0 0; cursor:inherit; }
div.after-pic svg.own [data-pos] { cursor:pointer; }
pre.src.editable { cursor:text; outline:1px dashed transparent; outline-offset:2px; }
pre.src.editable:hover { outline-color: var(--vscode-focusBorder, #0078d7); }
textarea.edit { display:block; width:100%; box-sizing:border-box; margin:4px 0 0; resize:vertical; min-height:3.2em;
                font-family: var(--vscode-editor-font-family, monospace); font-size:11px;
                background: var(--vscode-input-background); color: var(--vscode-input-foreground);
                border:1px solid var(--vscode-focusBorder, #0078d7); }
textarea.edit[hidden] { display:none; }
input.note { font-size:12px; width:22em; margin-right:8px; padding:1px 4px; vertical-align:middle;
             background: var(--vscode-input-background); color: var(--vscode-input-foreground);
             border:1px solid var(--vscode-input-border, var(--vscode-panel-border)); }
/* The head and the reason rows span the table; the pictures' row under them closes the bar with the rule. */
tr.head td { border-bottom:none; padding:10px 8px 2px; }
tr.reason td { border-bottom:none; padding:2px 8px 4px; }
tr.body td { padding-top: 2px; }
.who { white-space:normal; }
.label { font-weight:600; margin-right:6px; }
.memo { font-size:12px; opacity:.85; }
.dim { opacity:.6; }
.cell { width:33%; }
.pic { display:block; width:100%; height:120px; background:#fff; border:1px solid var(--vscode-panel-border); }
.none { height:120px; display:flex; align-items:center; justify-content:center; opacity:.6; border:1px dashed var(--vscode-panel-border); }
.src { margin:4px 0 0; font-family: var(--vscode-editor-font-family, monospace); font-size:11px; white-space:pre-wrap; opacity:.9; }
.src del { color: var(--vscode-gitDecoration-deletedResourceForeground, #e33); text-decoration: line-through; background: rgba(229, 57, 53, .15); }
.src ins { color: var(--vscode-gitDecoration-addedResourceForeground, #3a3); text-decoration: none; background: rgba(46, 160, 67, .18); font-weight: 600; }
.why { font-size:12px; white-space:normal; }
.gates { margin-right:8px; white-space:nowrap; }
.ok { color: var(--vscode-testing-iconPassed, #3c3); }
.bad { color: var(--vscode-testing-iconFailed, #e33); }
button { padding:4px 10px; font-size:12px; border:1px solid var(--vscode-button-border, transparent); border-radius:4px; cursor:pointer; margin:0 2px 4px 0;
         background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
button.primary, button.on { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
button:disabled { opacity:.4; cursor:default; }
button.link { background:none; border:none; padding:0; margin-left:8px; color: var(--vscode-textLink-foreground); font-size:12px; }
.footer { display:flex; gap:8px; padding:10px 12px; border-top:1px solid var(--vscode-panel-border); flex-shrink:0; align-items:center; }
.store { position:absolute; width:0; height:0; overflow:hidden; }
</style>
</head>
<body>
${store}
<div class="header">
  <div><div class="title">${run.manual ? 'Bars beside the scan' : 'OMR fixes'} — ${esc(name)}</div>
  <div class="cap" id="cap">${esc(head.cap)}</div></div>
</div>
<div class="main">
<table>
<thead><tr><th>${run.manual ? 'Now' : 'Now (OMR)'}</th><th>Scan</th><th id="third">${head.third}</th></tr></thead>
<tbody>
${rows}
</tbody>
</table>
</div>
<div class="footer">
  <button class="primary" id="acceptAll" ${head.ready ? '' : 'disabled'} title="Write every ready bar — your edits that compile and fill their bar, and the AI's bars that pass every gate — as one edit (one Ctrl+Z)">Accept All (<span id="readyN">${head.ready}</span>)</button>
  <div class="spacer"></div>
  <button id="cancel" title="Discard every remaining row and close the panel — bars already accepted stay written">Discard All</button>
</div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const noteOf = i => document.querySelector('input.note[data-i="' + i + '"]');
  // "keep @todo" ("add @todo" on a bar with no mark): the row's bar is written with a @todo on it,
  // the words in the field beside it its memo — told to the extension as they change, so the
  // row's Accept writes them.
  document.addEventListener('change', e => {
    if (!(e.target.matches && e.target.matches('input.keep'))) return;
    const i = e.target.dataset.i, on = e.target.checked, n = noteOf(i);
    n.hidden = !on;
    vscode.postMessage({ action: 'keep', index: Number(i), on, text: n.value });
    if (on) n.focus();
  });
  document.addEventListener('input', e => {
    if (e.target.matches && e.target.matches('input.note')) vscode.postMessage({ action: 'keep', index: Number(e.target.dataset.i), text: e.target.value });
  });
  // A row's Accept and Discard (owner's request 2026-10-24: each bar is done with on its own), and
  // the footer's Accept All (the ready rows) and Discard All. The field being typed in has closed
  // by then (focusout sends its text first), so a row's Accept writes what was typed last.
  document.addEventListener('click', e => {
    const b = e.target.closest && e.target.closest('button');
    if (!b) return;
    if (b.classList.contains('row-accept') || b.classList.contains('row-discard')) {
      e.stopImmediatePropagation();
      vscode.postMessage({ action: b.classList.contains('row-accept') ? 'acceptRow' : 'discard', index: Number(b.dataset.i) });
    }
    else if (b.id === 'acceptAll') vscode.postMessage({ action: 'acceptAll' });
    else if (b.id === 'cancel') vscode.postMessage({ action: 'cancel' });
  });
  // A click on a bar's rows — its box included, and a box that cannot be checked — shows the bar
  // in the editor, lit light blue there (owner's request 2026-10-23, in place of a link).
  document.addEventListener('click', e => {
    const tr = e.target.closest && e.target.closest('tr.row');
    if (!tr) return;
    for (const x of document.querySelectorAll('tr.row.cur')) x.classList.remove('cur');
    for (const x of document.querySelectorAll('tr.row[data-i="' + tr.dataset.i + '"]')) x.classList.add('cur');
    vscode.postMessage({ action: 'focus', index: Number(tr.dataset.i) });
  });

  // Each score cell is cropped to its bar: the preview's rect.bar-staff boxes carry where a bar
  // is written (data-span: first note .. bar line), so the box whose stretch meets the line's
  // span is the bar; ancestors may translate and scale (the renderer's groups), which the box's
  // numbers are read through. A staff height above and below, half a bar's height at the sides.
  function userRect(el) {
    let x = +el.getAttribute('x'), y = +el.getAttribute('y'), w = +el.getAttribute('width'), h = +el.getAttribute('height');
    for (let p = el.parentElement; p && p.tagName.toLowerCase() !== 'svg' && p.tagName.toLowerCase() !== 'symbol'; p = p.parentElement) {
      const t = p.getAttribute('transform');
      if (!t) continue;
      const s = /scale\\(\\s*([-\\d.e]+)(?:[\\s,]+([-\\d.e]+))?\\s*\\)/.exec(t);
      if (s) { const sx = +s[1], sy = s[2] === undefined ? sx : +s[2]; x *= sx; y *= sy; w *= sx; h *= sy; }
      const tr = /translate\\(\\s*([-\\d.e]+)(?:[\\s,]+([-\\d.e]+))?\\s*\\)/.exec(t);
      if (tr) { x += +tr[1]; y += +(tr[2] || 0); }
    }
    return { x, y, w, h };
  }
  function cropTo(holder, boxes, lo, hi) {
    let box = null;
    for (const r of boxes) {
      const span = (r.getAttribute('data-span') || '').split(' ');
      const from = parseInt(span[0], 10), to = parseInt(span[1], 10);
      if (!isNaN(from) && !isNaN(to) && from < hi && to >= lo) { box = r; break; }
    }
    if (!box) {
      for (const r of boxes) { const s = parseInt(r.getAttribute('data-start'), 10); if (s >= lo && s < hi) { box = r; break; } }
    }
    if (!box) return;
    const u = userRect(box);
    holder.setAttribute('viewBox', (u.x - u.h * 0.5) + ' ' + (u.y - u.h) + ' ' + (u.w + u.h) + ' ' + (u.h * 3));
    holder.setAttribute('preserveAspectRatio', 'xMidYMid meet');
  }
  // Paints the drawn notes of the symbol 'id' whose source offset (data-pos, or one of data-alt)
  // falls in one of 'ranges' ("from:to,…"): the preview's own rule for what may be recolored — not
  // the transparent notehead click box (nh-hit), not a fill-only box (a tab digit's white backing);
  // a line or an outline gets the stroke, anything else the fill. The symbol is shared by every
  // <use> of it, so "Now" paints once for all the rows (each row's crop shows its own bar).
  function paint(id, color, ranges, posOnly) {
    const spans = String(ranges || '').split(',').map(s => s.split(':').map(Number)).filter(s => s.length === 2 && !isNaN(s[0]));
    const hit = n => spans.some(([a, b]) => n >= a && n < b);
    const root = document.getElementById(id);
    if (!root || spans.length === 0) return;
    for (const el of root.querySelectorAll('[data-pos], [data-alt]')) {
      const tag = el.tagName.toLowerCase();
      if (el.classList.contains('nh-hit') || (tag === 'rect' && !el.getAttribute('stroke') && el.getAttribute('fill'))) continue;
      // posOnly: the element's own offset alone — a chord's heads all carry the chord's '<' in
      // data-alt, so a range that reaches the '<' would light every head.
      const pos = [parseInt(el.getAttribute('data-pos'), 10), ...(posOnly ? [] : String(el.getAttribute('data-alt') || '').split(/\\s+/).map(Number))];
      if (!pos.some(n => !isNaN(n) && hit(n))) continue;
      // The colour it had, kept once, so an edit can take a row's paint off again (color null).
      if (!el.hasAttribute('data-was')) el.setAttribute('data-was', (el.getAttribute('fill') || '') + '|' + (el.getAttribute('stroke') || ''));
      if (color === null) {
        const was = el.getAttribute('data-was').split('|');
        if (was[0]) el.setAttribute('fill', was[0]); else el.removeAttribute('fill');
        if (was[1]) el.setAttribute('stroke', was[1]); else el.removeAttribute('stroke');
      } else if (tag === 'line' || (el.getAttribute('fill') === 'none' && el.getAttribute('stroke'))) el.setAttribute('stroke', color);
      else el.setAttribute('fill', color);
    }
  }
  const RED = '#e53935', GREEN = '#2e9e43';
  const boxesOf = {};
  function setUp(svg) {
    const id = svg.dataset.sym;
    // A row's own symbol is drawn anew by an edit: its boxes are looked up again (a base's "Now"
    // symbol, shared by its rows, never changes).
    if (!boxesOf[id] || id.indexOf('before-') !== 0) boxesOf[id] = Array.from(document.querySelectorAll('#' + id + ' rect.bar-staff'));
    cropTo(svg, boxesOf[id], +svg.dataset.lo, +svg.dataset.hi);
    if (svg.dataset.paint) paint(id, svg.dataset.paint === 'del' ? RED : GREEN, svg.dataset.ranges);
  }
  for (const svg of document.querySelectorAll('svg.score')) setUp(svg);

  // Editing the bar to write (owner's request 2026-10-24): a click on its text opens it; each
  // pause in the typing sends it, and the extension answers with the row's gates, picture, text
  // and what goes red in "Now" — put in place, the field keeping its focus. Enter closes it,
  // Escape puts back what it held when opened; an edit takes the bar.
  const q = (sel, i) => document.querySelector(sel + '[data-i="' + i + '"]');
  const seqs = {}, timers = {}, sent = {};
  function sendEdit(i) {
    clearTimeout(timers[i]);
    const ta = q('textarea.edit', i);
    if (sent[i] === ta.value) return;
    sent[i] = ta.value;
    seqs[i] = (seqs[i] || 0) + 1;
    vscode.postMessage({ action: 'edit', index: Number(i), text: ta.value, seq: seqs[i] });
  }
  function fit(ta) { ta.style.height = 'auto'; ta.style.height = (ta.scrollHeight + 2) + 'px'; }
  // While the field is open the proposal's picture drops its green and lights, in blue, the notes
  // the caret is on or the selection covers (owner's request 2026-10-24) — the field's offsets
  // are the drawn notes' source offsets from where the bar starts (data-base). A note's data-pos
  // is its node's full-span start, so the lit range reaches back over the spaces before it, and
  // for the first piece over the line's indentation (as rangesOf does).
  const BLUE = '#0078d7';
  const afterSvg = i => { const h = q('div.after-pic', i); return h && h.querySelector('svg.score'); };
  // The chord around offset c of v, when c is in one — from its '<' to the end of what is glued
  // after its '>' (duration, marks) — with the '>' itself; null otherwise. '<<' is not a chord.
  function chordAt(v, c) {
    for (let k = 0; k < v.length; k++) {
      if (v[k] !== '<') continue;
      if (v[k + 1] === '<') { k++; continue; }
      const gt = v.indexOf('>', k);
      if (gt < 0) return null;
      let end = gt + 1;
      while (end < v.length && !/\\s/.test(v[end])) end++;
      if (c >= k && c < end) return { lt: k, gt, end };
      k = end;
    }
    return null;
  }
  // What the caret or the selection lights (owner's requests 2026-10-24): a selection, every
  // note it covers; a caret on a chord's member, that head alone; on its '<', its '>' or what
  // follows the '>' (duration, @marks), the whole chord; elsewhere the piece it is in.
  function caretLight(v, s, e, base, indent) {
    if (s === e) {
      // The character the caret is on: the one after it, or before it at a space or the end.
      const c = s < v.length && !/\\s/.test(v[s]) ? s : s - 1;
      if (c < 0 || /\\s/.test(v[c] || ' ')) return null;
      const ch = chordAt(v, c);
      if (ch) {
        if (c === ch.lt || c >= ch.gt) {
          return { ranges: (base + ch.lt) + ':' + (base + ch.lt + 1) + ',' + (base + ch.gt) + ':' + (base + ch.end), posOnly: false };
        }
        let ms = c, me = c + 1;
        while (ms > ch.lt + 1 && !/\\s/.test(v[ms - 1])) ms--;
        while (me < ch.gt && !/\\s/.test(v[me])) me++;
        return { ranges: (base + ms) + ':' + (base + me), posOnly: true };
      }
      s = c; e = c + 1;
      while (s > 0 && !/\\s/.test(v[s - 1])) s--;
      while (e < v.length && !/\\s/.test(v[e])) e++;
    }
    // A note's data-pos is its node's full-span start: reach back over the spaces before the
    // piece, and for the first piece over the line's indentation.
    let b = s;
    while (b > 0 && /\\s/.test(v[b - 1])) b--;
    return { ranges: (base + b - (b === 0 ? indent : 0)) + ':' + (base + e), posOnly: false };
  }
  function lightSelection(i) {
    const ta = q('textarea.edit', i), svg = afterSvg(i);
    if (!ta || !svg) return;
    const id = svg.dataset.sym;
    if (svg.dataset.lit) paint(id, null, svg.dataset.lit, svg.dataset.litPosOnly === '1');
    svg.dataset.lit = '';
    if (ta.hidden) return;
    const lit = caretLight(ta.value, ta.selectionStart, ta.selectionEnd, +ta.dataset.base, +ta.dataset.indent);
    if (!lit) return;
    svg.dataset.lit = lit.ranges;
    svg.dataset.litPosOnly = lit.posOnly ? '1' : '';
    paint(id, BLUE, lit.ranges, lit.posOnly);
  }
  function editLook(i, editing) {
    const svg = afterSvg(i);
    if (!svg) return;
    if (editing) {
      if (svg.dataset.paint) paint(svg.dataset.sym, null, svg.dataset.ranges);
      lightSelection(i);
    } else {
      lightSelection(i);   // the field is hidden by now: this takes the blue off
      if (svg.dataset.paint) paint(svg.dataset.sym, GREEN, svg.dataset.ranges);
    }
  }
  function openEdit(i) {
    const ta = q('textarea.edit', i);
    if (!ta) return;
    q('pre.src.after', i).hidden = true;
    ta.hidden = false;
    ta.dataset.opened = ta.value;
    if (sent[i] === undefined) sent[i] = ta.value;
    fit(ta);
    ta.focus();
    editLook(i, true);
  }
  function closeEdit(i) {
    sendEdit(i);
    q('textarea.edit', i).hidden = true;
    q('pre.src.after', i).hidden = false;
    editLook(i, false);
  }
  // The caret moves by keys, the mouse and typing; each lights its notes again.
  for (const type of ['keyup', 'mouseup', 'select', 'focus']) {
    document.addEventListener(type, e => {
      if (e.target.matches && e.target.matches('textarea.edit') && !e.target.hidden) lightSelection(e.target.dataset.i);
    }, true);
  }
  document.addEventListener('selectionchange', () => {
    const a = document.activeElement;
    if (a && a.matches && a.matches('textarea.edit') && !a.hidden) lightSelection(a.dataset.i);
  });
  // A click on a note of the proposal's picture opens the field with the caret on that note's
  // text (owner's request 2026-10-24): its data-pos less where the bar starts — a chord's head
  // is its member's own offset; a node's full-span start (the spaces before it, the line's
  // indentation) steps forward to the text.
  function editAt(i, pos) {
    const ta = q('textarea.edit', i);
    if (!ta) return;
    if (ta.hidden) openEdit(i);
    const v = ta.value;
    let k = Math.max(0, Math.min(v.length, pos - +ta.dataset.base));
    while (k < v.length && /\\s/.test(v[k])) k++;
    ta.focus();
    ta.setSelectionRange(k, k);
    lightSelection(i);
  }
  document.addEventListener('click', e => {
    const note = e.target.closest && e.target.closest('div.after-pic svg.own [data-pos]');
    if (note) {
      const i = note.closest('div.after-pic').dataset.i;
      const pos = parseInt(note.getAttribute('data-pos'), 10);
      if (!isNaN(pos)) editAt(i, pos);
      return;
    }
    const pre = e.target.closest && e.target.closest('pre.src.editable');
    if (pre) { openEdit(pre.dataset.i); return; }
    const rv = e.target.closest && e.target.closest('button.revert');
    if (rv) {
      const i = rv.dataset.i;
      seqs[i] = (seqs[i] || 0) + 1;
      sent[i] = undefined;
      vscode.postMessage({ action: 'edit', index: Number(i), text: '', seq: seqs[i] });
      return;
    }
    // The row's Ask AI, and the cancel of a row waiting for it or being asked (owner's request
    // 2026-10-24): the answer comes back to this row alone, the others stay in the person's hands.
    const ask = e.target.closest && e.target.closest('button.ask, button.unask');
    if (ask) vscode.postMessage({ action: ask.classList.contains('ask') ? 'ask' : 'unask', index: Number(ask.dataset.i) });
  });
  document.addEventListener('input', e => {
    if (!(e.target.matches && e.target.matches('textarea.edit'))) return;
    const i = e.target.dataset.i;
    fit(e.target);
    clearTimeout(timers[i]);
    timers[i] = setTimeout(() => sendEdit(i), 400);
  });
  document.addEventListener('keydown', e => {
    if (!(e.target.matches && e.target.matches('textarea.edit'))) return;
    const i = e.target.dataset.i;
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); closeEdit(i); }
    else if (e.key === 'Escape') { e.preventDefault(); e.target.value = e.target.dataset.opened; closeEdit(i); }
  });
  document.addEventListener('focusout', e => {
    if (e.target.matches && e.target.matches('textarea.edit') && !e.target.hidden) closeEdit(e.target.dataset.i);
  });
  // The head: the line under the title, the third column's name (Edit until the AI answers).
  function header(m) {
    if (typeof m.cap === 'string') document.getElementById('cap').textContent = m.cap;
    if (typeof m.third === 'string') document.getElementById('third').textContent = m.third;
    if (typeof m.ready === 'number') {
      document.getElementById('readyN').textContent = String(m.ready);
      document.getElementById('acceptAll').disabled = m.ready === 0;
    }
  }
  window.addEventListener('message', e => {
    const m = e.data;
    if (!m) return;
    // Bars added to the panel (a second Edit Bars Beside the Scan, or Fix OMR Marks with AI on the
    // same file): their rows go below, the rows there keep what is being done in them.
    if (m.type === 'rows') {
      // The picture of a base these rows were opened on (after an Accept), if the page has not got it.
      if (m.symbols) document.body.insertAdjacentHTML('afterbegin', m.symbols);
      const body = document.querySelector('tbody');
      const at = body.children.length;
      body.insertAdjacentHTML('beforeend', m.html);
      for (const tr of Array.from(body.children).slice(at)) for (const svg of tr.querySelectorAll('svg.score')) setUp(svg);
      header(m);
      return;
    }
    // Rows written or discarded leave the page.
    if (m.type === 'remove') {
      for (const i of m.indices) for (const tr of document.querySelectorAll('tr.row[data-i="' + i + '"]')) tr.remove();
      header(m);
      return;
    }
    if (m.type === 'reveal') {
      const tr = document.querySelector('tr.row.head[data-i="' + m.index + '"]');
      if (tr) tr.scrollIntoView({ block: 'start', behavior: 'smooth' });
      return;
    }
    if (m.type !== 'row' || m.seq < (seqs[m.index] || 0)) return;
    const i = m.index;
    header(m);
    if (!q('span.gates', i)) return;   // a row that has left the page
    q('span.gates', i).innerHTML = m.gates;
    q('span.reason-text', i).innerHTML = m.reason;
    const secs = q('span.secs', i);
    if (secs) secs.textContent = m.secs || '';
    // The row's Accept: its word, whether it writes anything, primary when the row is ready.
    const ab = q('button.row-accept', i);
    if (ab && m.accept) { ab.textContent = m.accept.word; ab.disabled = !m.accept.can; ab.classList.toggle('primary', !!m.accept.ready); }
    const holder = q('div.after-pic', i);
    holder.innerHTML = m.proposal;
    for (const svg of holder.querySelectorAll('svg.score')) setUp(svg);
    // A picture that comes while the field is open shows the caret's notes, not the green.
    const open = q('textarea.edit', i);
    if (open && !open.hidden) editLook(i, true);
    q('pre.src.now', i).innerHTML = m.now;
    q('pre.src.after', i).innerHTML = m.after;
    const ta = q('textarea.edit', i);
    if (ta && document.activeElement !== ta) { ta.value = m.plain; sent[i] = m.plain; }
    // "Now" is one symbol for every row opened on the same text: this row's old red comes off,
    // its new red goes on.
    const nowSvg = document.querySelector('tr.body[data-i="' + i + '"] svg.score[data-sym^="before-"]');
    if (nowSvg) {
      paint(nowSvg.dataset.sym, null, nowSvg.dataset.ranges);
      nowSvg.dataset.ranges = m.del;
      nowSvg.dataset.paint = 'del';
      paint(nowSvg.dataset.sym, RED, m.del);
    }
  });
  // Everything above listens now: the extension sends again what the AI is doing.
  vscode.postMessage({ action: 'ready' });
</script>
</body>
</html>`;
}
