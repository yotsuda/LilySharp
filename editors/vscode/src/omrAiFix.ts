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
    batchesOf, checkedByDefault, cropRect, decodeImage, diagnosticsOn, estimateOf, fixTargets, gateOf, headOf, labelOf,
    neighboursOf, parseProposal, readerBarOf, repairPromptOf, resolvedLine, scanCropPng, spliceBar, systemPromptOf, targetsInLines, taskPromptOf, withTodoMark,
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

/** One bar's outcome, a row of the panel. */
interface FixRow {
    readonly index: number;
    readonly target: FixTarget;
    readonly proposal: Proposal | null;
    /** The gates' verdict on the proposal (none without one). */
    readonly gate?: GateVerdict;
    /** The score with the proposal, when there is one that renders. */
    readonly after?: SvgResponse;
    /** Why there is no answer at all (`proposal` null): the model returned nothing, failed, or
     *  replied with something other than the JSON asked for — the reply's start, so the person
     *  sees what came back. */
    readonly noAnswer?: string;
    readonly seconds: number;
    state: 'pending' | 'applied' | 'skipped';
    /** The row's check box: Accept writes the checked rows. At first, the rows every gate passed
     *  and the model is sure enough of (acceptableByAll). */
    checked?: boolean;
    /** Taken with a @todo still on it, and the memo the person typed for it (owner's request
     *  2026-10-24: a proposal that is better but not right yet). */
    keepTodo?: boolean;
    todoMemo?: string;
}

/** The run a panel shows: the document as it was, the rows so far. */
interface FixRun {
    readonly uri: vscode.Uri;
    readonly version: number;
    readonly text: string;
    readonly side?: ScanSideFile;
    readonly before: SvgResponse;
    readonly rows: FixRow[];
    /** Whether the scan goes to the model (`lilysharp.ai.omrFixImages`, and a page to crop). */
    readonly images: boolean;
    /** The pages decoded so far (null: the page could not be read), by page number. */
    readonly pages: Map<number, Rgba | null>;
    /** The reader for gate ③, when there is one that can (`verify-bars`) and the setting is on. */
    readonly verifier?: Verifier;
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
let lock: vscode.TextEditorDecorationType | undefined;

/** Registers the command. Its three doors (design §2.1) are its arguments: none from the palette
 *  and the editor's menu (the active .lys); the document's uri from the scan view's button; the
 *  uri and the offset of one mark from the quick fix (the server's "Fix this bar with AI…"). */
export function registerOmrAiFix(context: vscode.ExtensionContext, deps: OmrAiFixDeps): void {
    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.omrFixWithAi',
            (uri?: string | vscode.Uri, at?: number) => runOmrAiFix(context, deps, uri, Number.isInteger(at) ? at : undefined)),
        { dispose: () => { panel?.dispose(); lock?.dispose(); clearBarMark(); output?.dispose(); } });
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
    const sidePath = doc.uri.scheme === 'file' ? sideFileOf(doc.uri.fsPath) : undefined;
    let side: ScanSideFile | undefined, sideText: string | undefined;
    if (sidePath && fs.existsSync(sidePath)) {
        try {
            sideText = fs.readFileSync(sidePath, 'utf8');
            side = parseSideFile(sideText, sidePath);
        } catch { /* no side file: the memos still are */ }
    }
    let targets = fixTargets(text, todos.Todos ?? [], side, sideText ? markFactsOf(sideText) : undefined);
    if (targets.length === 0) {
        vscode.window.showInformationMessage(`Lily#: ${path.basename(doc.uri.fsPath)} carries no @todo mark to fix.`);
        return;
    }
    let chosen: FixTarget[] | undefined;
    if (at !== undefined) {
        // The quick fix: the one bar the mark is on — no list, no question.
        const [from, to] = lineAround(text, at);
        chosen = targets.filter(t => t.start >= from && t.end <= to);
        if (chosen.length === 0) {
            vscode.window.showInformationMessage('Lily#: no @todo mark on that line any more.');
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
    const cfg = vscode.workspace.getConfiguration('lilysharp');
    const limit = cfg.get<number>('ai.omrFixBatch', 25);
    const batches = batchesOf(chosen, limit);
    log(`Fix OMR marks: ${path.basename(doc.uri.fsPath)} — ${chosen.length} of ${targets.length} bars via ${chat.label}`
        + `, scan ${cfg.get<boolean>('ai.omrFixImages', true) && side && side.pages.length > 0 ? 'shown to the model' : cfg.get<boolean>('ai.omrFixImages', true) ? 'not available (no page beside the file)' : 'off (lilysharp.ai.omrFixImages)'}`
        + (batches.length > 1 ? `, in ${batches.length} batches of ${limit}` : ''));
    const verifier = await verifierFor(context, deps, doc, side);
    const run: FixRun = {
        uri: doc.uri, version: doc.version, text, side,
        before: await client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: text }),
        rows: [],
        images: cfg.get<boolean>('ai.omrFixImages', true) && !!side && side.pages.length > 0,
        pages: new Map(),
        ...(verifier ? { verifier } : {}),
    };

    for (let b = 0; b < batches.length; b++) {
        if (b > 0) {
            const rest = chosen.length - b * limit;
            const go = await vscode.window.showInformationMessage(
                `Lily#: ${b * limit} bars done. Continue with the next ${Math.min(limit, rest)} of ${rest}?`,
                { modal: true }, 'Continue');
            if (go !== 'Continue') {
                break;
            }
        }
        const done = await runBatch(context, deps, client, chat, editor, run, batches[b]);
        showPanel(deps, run);
        if (!done) {
            break;
        }
    }
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

/**
 * One batch, under a cancellable progress notification: a request a bar, the gates, the render.
 * Stops — and says so — when the document is edited meanwhile (the offsets would lie) or the
 * person cancels. True when every bar was done.
 */
async function runBatch(
    context: vscode.ExtensionContext, deps: OmrAiFixDeps, client: LanguageClient, chat: ChatClient,
    editor: vscode.TextEditor, run: FixRun, batch: readonly FixTarget[]): Promise<boolean> {
    const doc = editor.document;
    const grammar = await loadGrammar(deps);
    const system = systemPromptOf(grammar);
    const head = headOf(run.text);
    const baseline = (await client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: run.text })).Diagnostics ?? [];

    softLock(editor, batch);
    let complete = true;
    try {
        await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Notification, title: 'Lily#: fixing OMR marks with AI', cancellable: true },
            async (progress, token) => {
                let proposals = 0;
                for (let i = 0; i < batch.length; i++) {
                    if (token.isCancellationRequested) {
                        log('Fix OMR marks: cancelled.');
                        complete = false;
                        return;
                    }
                    if (doc.version !== run.version) {
                        log('Fix OMR marks: the document was edited; stopped (the bars done so far are shown).');
                        vscode.window.showWarningMessage('Lily#: the file changed while the AI was working — stopped. The bars done so far are shown.');
                        complete = false;
                        return;
                    }
                    const target = batch[i];
                    progress.report({ message: `${i + 1} / ${batch.length} bars, ${proposals} ${proposals === 1 ? 'proposal' : 'proposals'} — ${labelOf(target)}`,
                        increment: 100 / batch.length });
                    const started = Date.now();
                    const image = run.images ? scanOf(run, target) : undefined;
                    const row = await fixOne(client, chat, run, system, head, baseline, target, image, token, run.rows.length);
                    const seconds = (Date.now() - started) / 1000;
                    run.rows.push({ ...row, seconds,
                        checked: !!row.proposal?.bar && !!row.gate && acceptableByAll(row.gate, row.proposal.confidence) });
                    if (row.proposal?.bar) {
                        proposals++;
                    }
                    await context.globalState.update(TIMINGS_KEY, withSample(context.globalState.get<number[]>(TIMINGS_KEY, []), seconds));
                    const sent = chat.sentImages?.();
                    const picture = !run.images ? '' : !image ? ' · no scan for this bar' : sent === false ? ' · scan not taken by the model (sent as text)'
                        : ` · scan ${image.width}×${image.height}`;
                    log(`  ${labelOf(target)}: ${seconds.toFixed(1)} s${picture} — ${outcomeOf(row)}`);
                    if (run.images && image && sent === false && !saidRefused) {
                        saidRefused = true;
                        log(`  ${chat.label} takes no image: the bars go as text from here on (pick another model with "Lily#: Select AI Model…").`);
                    }
                    // The first bar came back empty: so will the rest. Stop, and say which model
                    // and where to pick another, rather than a list of empty rows.
                    if (i === 0 && row.noAnswer?.endsWith('returned nothing')) {
                        complete = false;
                        // Copilot CLI's door is all VS Code lists when Copilot is not signed in:
                        // there is no other model to pick, so say how to get one.
                        const noModel = !hasChatModel(await availableModels());
                        log(`Fix OMR marks: ${chat.label} returned nothing — stopped. `
                            + (noModel ? 'VS Code offers no chat model (GitHub Copilot is not signed in).' : 'Pick a chat model with "Lily#: Select AI Model…" → Fix OMR marks with AI.'));
                        if (noModel) {
                            void offerModelSetup();
                        } else {
                            void vscode.window.showWarningMessage(
                                `Lily#: the model ${chat.label} returned nothing, so the fix stopped. Pick another model for "Fix OMR marks with AI".`,
                                'Select AI Model…').then(c => { if (c) { void vscode.commands.executeCommand('lilysharp.selectAiModel'); } });
                        }
                        return;
                    }
                }
            });
    } finally {
        clearLock();
    }
    const done = run.rows.slice(-batch.length);
    const verified = done.filter(r => r.gate?.verify !== undefined);
    log(`Fix OMR marks: ${done.length} bars · ${done.filter(r => r.proposal?.bar).length} proposals`
        + ` · scan ${run.images ? (chat.sentImages?.() === false ? 'refused' : 'on') : 'off'}`
        + ` · verify ${run.verifier ? `${verified.filter(r => r.gate!.verify).length} ✓ ${verified.filter(r => !r.gate!.verify).length} ✗` : 'off'}`
        + ` · ${done.reduce((s, r) => s + r.seconds, 0).toFixed(0)} s`);
    return complete;
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

function outcomeOf(row: Omit<FixRow, 'seconds'>): string {
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

/** One bar: the prompt → the model → the gates, once more with the compiler's words on a fail. */
async function fixOne(
    client: LanguageClient, chat: ChatClient, run: FixRun, system: string, head: string, baseline: readonly Diag[],
    target: FixTarget, image: { data: Uint8Array; mime: string } | undefined, token: vscode.CancellationToken, index: number,
): Promise<Omit<FixRow, 'seconds'>> {
    const neighbours: BarNeighbours = neighboursOf(run.text, target, run.side);
    const messages: ChatMessage[] = [
        { role: 'system', content: system },
        {
            role: 'user',
            content: taskPromptOf({ head, target, neighbours, diagnostics: diagnosticsOn(baseline, target), image: !!image }),
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
            return { index, target, proposal: null, noAnswer: why, state: 'pending' };
        }
        if (raw.trim() === '') {
            // Nothing at all is not a malformed answer: asking again gets nothing again. A model
            // that cannot hold a plain conversation (the owner's copilotcli, 2026-10-23) does this.
            return { index, target, proposal: null, noAnswer: `${chat.label} returned nothing`, state: 'pending' };
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
            return { index, target, proposal: null, noAnswer: `not the JSON asked for: "${head}"`, state: 'pending' };
        }
        if (!proposal.bar) {
            return { index, target, proposal, state: 'pending' };
        }
        const spliced = spliceBar(run.text, target, proposal.bar);
        const check = await client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: spliced });
        let gate = gateOf(baseline, check.Diagnostics ?? [], { start: target.start, length: proposal.bar.length });
        if (gate.compile && gate.length && run.verifier) {
            // Gate ③: the scan's word on a bar that compiles and fills its meter (a few seconds).
            gate = withVerify(gate, await verifyProposal(run, target, spliced, token));
        }
        if (gate.compile && gate.length && gate.verify !== false) {
            return { index, target, proposal, gate, after: await render(spliced), state: 'pending' };
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
        return { index, target, proposal, gate, after: await render(spliced), state: 'pending' };
    }
    return { index, target, proposal, gate, state: 'pending' };
}

// ---------------------------------------------------------------------------------------------
// The soft lock (as the AI transform's)
// ---------------------------------------------------------------------------------------------

function softLock(editor: vscode.TextEditor, targets: readonly FixTarget[]): void {
    clearLock();
    lock = vscode.window.createTextEditorDecorationType({
        backgroundColor: new vscode.ThemeColor('editor.wordHighlightBackground'),
        overviewRulerColor: new vscode.ThemeColor('editorInfo.foreground'),
        overviewRulerLane: vscode.OverviewRulerLane.Full,
    });
    editor.setDecorations(lock, targets.map(t => new vscode.Range(editor.document.positionAt(t.start), editor.document.positionAt(t.end))));
}

function clearLock(): void {
    lock?.dispose();
    lock = undefined;
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
    if (!panel) {
        panel = vscode.window.createWebviewPanel('lilysharpOmrFixes', 'Lily# — OMR fixes',
            { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
            { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: roots });
        panel.onDidDispose(() => { panel = undefined; shownRun = undefined; clearBarMark(); });
        panel.webview.onDidReceiveMessage((m: PanelMessage) => void onPanelMessage(deps, m));
    } else {
        panel.webview.options = { ...panel.webview.options, localResourceRoots: roots };
    }
    panel.webview.html = panelHtml(deps, panel.webview, run);
    panel.reveal(vscode.ViewColumn.Beside, true);
}

/** What the panel's page posts. `keeps` (with accept): the checked rows taken with a @todo still
 *  on them, and their memos. */
interface PanelMessage {
    action?: string;
    index?: number;
    on?: boolean;
    text?: string;
    indices?: number[];
    keeps?: { index: number; text: string }[];
}

async function onPanelMessage(deps: OmrAiFixDeps, m: PanelMessage): Promise<void> {
    const run = shownRun;
    if (!run || !panel) {
        return;
    }
    const row = Number.isInteger(m.index) ? run.rows.find(r => r.index === m.index) : undefined;
    // A check box a row, toggled freely, writes nothing; Accept writes the checked rows at once as
    // one edit and closes the panel; Cancel closes it writing nothing (owner's request 2026-10-23:
    // an Accept a row that wrote at once could not be pressed again after Ctrl+Z).
    switch (m.action) {
        case 'check':
            // Kept here too, so a panel drawn again (the next batch) keeps the boxes as they were.
            if (row && row.state !== 'applied') { row.checked = !!m.on; }
            return;
        case 'keep':
            if (row && row.state !== 'applied') { row.keepTodo = !!m.on; row.todoMemo = m.text ?? row.todoMemo; }
            return;
        case 'accept': {
            const chosen = run.rows.filter(r => r.state !== 'applied' && (m.indices ?? []).includes(r.index));
            for (const r of run.rows) {
                r.checked = chosen.includes(r);
                const keep = (m.keeps ?? []).find(k => k.index === r.index);
                r.keepTodo = !!keep;
                if (keep) { r.todoMemo = keep.text; }
            }
            if (chosen.length === 0) {
                vscode.window.showInformationMessage('Lily#: no proposal is checked — check the bars to write, or Cancel.');
                return;
            }
            if (await applyRows(run, chosen)) {
                panel.dispose();
                return;
            }
            break;
        }
        case 'focus':
            if (row) { await showBar(run, row); }
            return;
        case 'cancel':
            log(`Fix OMR marks: cancelled in the panel — nothing written (${run.rows.filter(r => r.proposal?.bar).length} proposals).`);
            panel.dispose();
            return;
        default:
            return;
    }
    panel.webview.html = panelHtml(deps, panel.webview, run);
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
    const bar = hasProposal(r) ? r.proposal!.bar! : resolvedLine(r.target.written);
    return r.keepTodo ? withTodoMark(r.target.written, bar, r.todoMemo ?? '') ?? bar : bar;
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
async function applyRows(run: FixRun, accepted: readonly FixRow[]): Promise<boolean> {
    if (accepted.length === 0) {
        return false;
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
        if (r.keepTodo && textOf(r) === (hasProposal(r) ? r.proposal!.bar! : resolvedLine(r.target.written))) {
            log(`  ${labelOf(r.target)}: no note to keep the @todo on — written without it`);
        }
    }
    const ok = written.length > 0 && await vscode.workspace.applyEdit(edit);
    if (ok) {
        for (const r of written) { r.state = 'applied'; }
        const proposals = run.rows.filter(r => r.proposal?.bar).length;
        log(`Fix OMR marks: applied ${written.length} ${written.length === 1 ? 'bar' : 'bars'}`
            + ` (${run.rows.filter(r => r.state === 'applied').length} of ${proposals} proposals taken so far, scan ${run.images ? 'on' : 'off'})`
            + (skipped.length ? `; ${skipped.length} skipped (the line moved and could not be found)` : ''));
        for (const r of skipped) { r.state = 'skipped'; }
        if (skipped.length) {
            vscode.window.showWarningMessage(`Lily#: wrote ${written.length} ${written.length === 1 ? 'bar' : 'bars'}; `
                + `${skipped.map(r => labelOf(r.target)).join(', ')} could not be found as written — the line changed since the fix ran.`);
        } else {
            vscode.window.showInformationMessage(`Lily#: wrote ${written.length} ${written.length === 1 ? 'bar' : 'bars'} (Ctrl+Z undoes ${written.length === 1 ? 'it' : 'them all'}).`);
        }
        return true;
    } else if (written.length > 0) {
        vscode.window.showErrorMessage('Lily#: the edit could not be applied.');
    } else {
        for (const r of skipped) { r.state = 'skipped'; }
        vscode.window.showWarningMessage(`Lily#: ${skipped.map(r => labelOf(r.target)).join(', ')} could not be found as written — `
            + 'the line changed since the fix ran, so nothing was written. Run the fix again.');
    }
    return false;
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
    return `<svg class="pic score" data-sym="${id}" data-lo="${lo}" data-hi="${hi}"${marks} viewBox="${esc(sym.viewBox)}" preserveAspectRatio="xMidYMid meet">`
        + (define ? `<symbol id="${id}" viewBox="${esc(sym.viewBox)}">${sym.inner}</symbol>` : '')
        + `<use href="#${id}" x="${sym.x}" y="${sym.y}" width="${sym.w}" height="${sym.h}"/></svg>`;
}

function panelHtml(deps: OmrAiFixDeps, webview: vscode.Webview, run: FixRun): string {
    const nonce = nonceOf();
    const fontUri = webview.asWebviewUri(vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts', 'emmentaler-20.woff2'));
    const braceUri = webview.asWebviewUri(vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts', 'emmentaler-brace.woff2'));
    const before = run.before.Svg ? symbolOf(run.before.Svg) : undefined;

    const pageUris = new Map<number, { uri: string; width: number; height: number }>();
    for (const p of run.side?.pages ?? []) {
        const shown = shownImage(p);
        if (shown && fs.existsSync(shown)) {
            pageUris.set(p.page, { uri: webview.asWebviewUri(vscode.Uri.file(shown)).toString(), width: p.width, height: p.height });
        }
    }

    const rows = run.rows.map(r => {
        const t = r.target;
        // The two lines piece by piece: struck through in red what goes, in green what comes —
        // in the text under the pictures and on the drawn notes.
        const diff = wordDiff(t.written, textOf(r));
        const indent = t.start - (run.text.lastIndexOf('\n', t.start - 1) + 1);
        const page = t.crop ? pageUris.get(t.crop.page) : undefined;
        const crop = t.crop && page ? cropRect(t.crop.box, page) : undefined;
        const scan = crop && page
            ? `<svg class="pic" viewBox="${crop.x} ${crop.y} ${crop.width} ${crop.height}" preserveAspectRatio="xMidYMid meet">`
              + `<image href="${page.uri}" width="${page.width}" height="${page.height}"/></svg>`
            : `<div class="none">no scan</div>`;
        const now = before
            ? scoreCell('before', before, t.start, t.end, false, diff && { kind: 'del', ranges: rangesOf(diff.before, 'del', t.start, indent) })
            : `<div class="none">not rendered</div>`;
        const after = r.after?.Svg ? symbolOf(r.after.Svg) : undefined;
        let proposal: string;
        if (!r.proposal) {
            proposal = `<div class="none">no answer — Take removes the @todo</div>`;
        } else if (!r.proposal.bar) {
            proposal = r.proposal.unchanged
                ? `<div class="none">unchanged — Take removes the @todo</div>`
                : `<div class="none">no proposal — Take removes the @todo</div>`;
        } else if (after) {
            proposal = scoreCell(`after-${r.index}`, after, t.start, t.start + r.proposal.bar.length, true,
                diff && { kind: 'add', ranges: rangesOf(diff.after, 'add', t.start, indent) });
        } else {
            proposal = `<div class="none">${r.gate && !(r.gate.compile && r.gate.length) ? 'did not pass — Take removes the @todo' : 'not rendered'}</div>`;
        }
        const gates = r.gate
            ? `<span class="${r.gate.compile ? 'ok' : 'bad'}">compile ${r.gate.compile ? '✓' : '✗'}</span> `
              + `<span class="${r.gate.length ? 'ok' : 'bad'}">length ${r.gate.length ? '✓' : '✗'}</span> `
              + (r.gate.verify === undefined ? `<span class="dim">verify —</span>`
                  : `<span class="${r.gate.verify ? 'ok' : 'bad'}">verify ${r.gate.verify ? '✓' : '✗'}</span>`)
            : `<span class="dim">—</span>`;
        const failed = r.gate && !(r.gate.compile && r.gate.length);
        const reason = !r.proposal ? `No answer: ${r.noAnswer ?? 'the reply was not the JSON asked for'}.`
            : r.proposal.unchanged ? `The model reads the bar as right${r.proposal.reason.replace(/^unchanged( — )?/, '') ? `: ${r.proposal.reason.replace(/^unchanged( — )?/, '')}` : '.'}`
            : !r.proposal.bar ? (failed ? `No proposal passed: ${r.gate!.problems.join(' / ') || r.proposal.reason}` : r.proposal.reason || 'The model gave no reason.')
            : r.gate?.verify === false ? `${r.proposal.reason ? `${r.proposal.reason} — ` : ''}${r.gate.problems.filter(p => p.startsWith('the scan says')).join(' / ')}`
            : r.proposal.reason || '';
        const sure = r.proposal?.confidence === null || r.proposal?.confidence === undefined ? '' : ` · confidence ${r.proposal.confidence.toFixed(2)}`;
        const can = r.state !== 'applied';
        const on = can && !!r.checked;
        const buttons = r.state === 'skipped' ? `<span class="memo bad">· not written last time: the line had changed</span>` : '';
        const keep = !hasProposal(r);
        const box = can
            ? `<label class="take" title="${keep ? 'Remove this bar\'s @todo when you press Accept (the notes stay as they are)' : 'Write this proposal when you press Accept'}">`
              + `<input type="checkbox" class="take" data-i="${r.index}" ${on ? 'checked' : ''}> ${keep ? 'Take (remove @todo)' : 'Take'}</label>`
            : `<label class="take dim" title="${r.state === 'applied' ? 'Written' : 'No proposal to take'}"><input type="checkbox" disabled> ${r.state === 'applied' ? 'Written' : 'Take'}</label>`;
        // Taken but still marked (owner's request 2026-10-24): the bar is written with a @todo at
        // the same note — the reader's key kept — and the words typed here as its memo.
        const keepBox = can
            ? `<label class="take keep" title="Write the bar with a @todo still on it, at the same note — for a bar that is better but not right yet">`
              + `<input type="checkbox" class="keep" data-i="${r.index}" ${on && r.keepTodo ? 'checked' : ''}> keep @todo</label>`
              + `<input type="text" class="note" data-i="${r.index}" value="${esc(r.todoMemo ?? '')}" placeholder="what is still wrong (optional)" ${on && r.keepTodo ? '' : 'hidden'}>`
            : '';
        // Three rows a bar: a head across the whole width — the box, the bar, the reader's memo,
        // the line, the time, the way to the editor — then the model's reason and the gates,
        // across the whole width too, and under them the three pictures. A column for either
        // was a fraction of the width and wrapped its text into a dozen short lines (owner's
        // reports 2026-10-23). The pictures go the reader's, the scan, the proposal: the scan in
        // the middle sits beside both, and the proposal is the one a person compares it with
        // (owner, 2026-10-24: scan first put the proposal two pictures away from it).
        const cls = `row ${r.state}${on ? ' taken' : ''}`;
        return `<tr class="${cls} head" data-i="${r.index}">
  <td colspan="3" class="who">${box}${keepBox} <span class="label">${esc(labelOf(t))}</span>`
            + (t.memos.length ? ` <span class="memo">${esc(t.memos.join('; '))}</span>` : '')
            + ` <span class="memo dim">· line ${t.line + 1}${t.confidence === undefined ? '' : ` · reader ${t.confidence.toFixed(2)}`} · ${r.seconds.toFixed(1)} s</span></td>
</tr>
<tr class="${cls} reason" data-i="${r.index}">
  <td colspan="3" class="why"><span class="gates">${gates}</span>${reason ? ` <span>${esc(reason)}</span>` : ''}`
            + `${sure ? ` <span class="dim">${sure.replace(/^ · /, '· ')}</span>` : ''}${buttons ? ` ${buttons}` : ''}</td>
</tr>
<tr class="${cls} body" data-i="${r.index}">
  <td class="cell">${now}<pre class="src">${diff ? piecesHtml(diff.before) : esc(shortTodos(t.written))}</pre></td>
  <td class="cell">${scan}</td>
  <td class="cell">${proposal}${diff ? `<pre class="src">${piecesHtml(diff.after)}</pre>` : ''}</td>
</tr>`;
    }).join('\n');

    const takeable = run.rows.filter(r => r.state !== 'applied').length;
    const checked = run.rows.filter(r => r.state !== 'applied' && r.checked).length;
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
/* A checked bar: the light blue the scan view and the preview tint a bar with (owner's request
   2026-10-23), on both of its rows. */
tr.taken > td { background: color-mix(in srgb, #0078d7 26%, var(--vscode-editor-background)) !important; }
/* The bar the editor shows (the last row clicked): an outline round its three rows. */
tr.cur.head > td { box-shadow: inset 0 2px 0 #0078d7; }
tr.cur > td:first-child { box-shadow: inset 3px 0 0 #0078d7; }
tr.row { cursor: pointer; }
tr.applied { opacity:.6; }
label.take { font-size:12px; margin-right:8px; cursor:pointer; user-select:none; }
label.take.dim { cursor:default; }
label.take input { vertical-align:middle; margin:0 3px 0 0; cursor:inherit; }
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
${before ? `<svg class="store" aria-hidden="true"><symbol id="before" viewBox="${esc(before.viewBox)}">${before.inner}</symbol></svg>` : ''}
<div class="header">
  <div><div class="title">OMR fixes — ${esc(name)}</div>
  <div class="cap">${run.rows.length} ${run.rows.length === 1 ? 'bar' : 'bars'} · ${run.rows.filter(hasProposal).length} with a proposal · `
        + `check the ones to take — nothing is written until Accept</div></div>
  <div class="spacer"></div>
  <label class="take all" title="Check or uncheck every bar there is something to take"><input type="checkbox" id="all" ${takeable ? '' : 'disabled'} ${takeable && checked === takeable ? 'checked' : ''}> All</label>
  <span class="memo"><span id="count">${checked}</span> of ${takeable} checked</span>
</div>
<div class="main">
<table>
<thead><tr><th>Now (OMR)</th><th>Scan</th><th>Proposal</th></tr></thead>
<tbody>
${rows}
</tbody>
</table>
</div>
<div class="footer">
  <button class="primary" id="accept" ${checked ? '' : 'disabled'} title="Write the checked bars into the .lys as one edit (one Ctrl+Z) and close">Accept <span id="acceptN">${checked}</span> checked</button>
  <div class="spacer"></div>
  <button id="cancel" title="Close, writing nothing">Cancel</button>
</div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const boxes = () => Array.from(document.querySelectorAll('input.take'));
  // A box lights its bar's two rows and is told to the extension (so a panel drawn again for the
  // next batch keeps it); the counts follow.
  function setBox(box, on) {
    box.checked = on;
    for (const tr of document.querySelectorAll('tr.row[data-i="' + box.dataset.i + '"]')) tr.classList.toggle('taken', on);
    vscode.postMessage({ action: 'check', index: Number(box.dataset.i), on });
    if (!on) setKeep(box.dataset.i, false);
  }
  // "keep @todo": taken with a @todo still on the bar, its memo in the field beside it. Checking
  // it takes the bar; a bar not taken keeps nothing.
  const keepOf = i => document.querySelector('input.keep[data-i="' + i + '"]');
  const noteOf = i => document.querySelector('input.note[data-i="' + i + '"]');
  function setKeep(i, on) {
    const k = keepOf(i), n = noteOf(i);
    if (!k || (!on && !k.checked)) return;
    k.checked = on;
    n.hidden = !on;
    vscode.postMessage({ action: 'keep', index: Number(i), on, text: n.value });
  }
  // The header's All box: checked when every box is, a dash when some are (owner's request
  // 2026-10-23, in place of Check all / Uncheck all buttons).
  const all = document.getElementById('all');
  function recount() {
    const n = boxes().filter(b => b.checked).length;
    all.checked = n > 0 && n === boxes().length;
    all.indeterminate = n > 0 && n < boxes().length;
    document.getElementById('count').textContent = String(n);
    document.getElementById('acceptN').textContent = String(n);
    document.getElementById('accept').disabled = n === 0;
  }
  document.addEventListener('change', e => {
    if (e.target.matches && e.target.matches('input.take')) { setBox(e.target, e.target.checked); recount(); }
    else if (e.target.matches && e.target.matches('input.keep')) {
      const i = e.target.dataset.i, on = e.target.checked;
      if (on) { const b = boxes().find(x => x.dataset.i === i); if (b && !b.checked) setBox(b, true); }
      e.target.checked = !on;   // setKeep reads the box as it was
      setKeep(i, on);
      if (on) noteOf(i).focus();
      recount();
    }
    else if (e.target === all) { for (const x of boxes()) setBox(x, all.checked); recount(); }
  });
  document.addEventListener('input', e => {
    if (e.target.matches && e.target.matches('input.note')) vscode.postMessage({ action: 'keep', index: Number(e.target.dataset.i), on: true, text: e.target.value });
  });
  document.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b) return;
    if (b.id === 'accept') vscode.postMessage({ action: 'accept', indices: boxes().filter(x => x.checked).map(x => Number(x.dataset.i)),
      keeps: Array.from(document.querySelectorAll('input.keep')).filter(k => k.checked).map(k => ({ index: Number(k.dataset.i), text: noteOf(k.dataset.i).value })) });
    else if (b.id === 'cancel') vscode.postMessage({ action: 'cancel' });
  });
  recount();   // the All box's first state (a dash when only some start checked)
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
  function paint(id, color, ranges) {
    const spans = String(ranges || '').split(',').map(s => s.split(':').map(Number)).filter(s => s.length === 2 && !isNaN(s[0]));
    const hit = n => spans.some(([a, b]) => n >= a && n < b);
    const root = document.getElementById(id);
    if (!root || spans.length === 0) return;
    for (const el of root.querySelectorAll('[data-pos], [data-alt]')) {
      const tag = el.tagName.toLowerCase();
      if (el.classList.contains('nh-hit') || (tag === 'rect' && !el.getAttribute('stroke') && el.getAttribute('fill'))) continue;
      const pos = [parseInt(el.getAttribute('data-pos'), 10), ...String(el.getAttribute('data-alt') || '').split(/\s+/).map(Number)];
      if (!pos.some(n => !isNaN(n) && hit(n))) continue;
      if (tag === 'line' || (el.getAttribute('fill') === 'none' && el.getAttribute('stroke'))) el.setAttribute('stroke', color);
      else el.setAttribute('fill', color);
    }
  }
  const boxesOf = {};
  for (const svg of document.querySelectorAll('svg.score')) {
    const id = svg.dataset.sym;
    boxesOf[id] = boxesOf[id] || Array.from(document.querySelectorAll('#' + id + ' rect.bar-staff'));
    cropTo(svg, boxesOf[id], +svg.dataset.lo, +svg.dataset.hi);
    if (svg.dataset.paint) paint(id, svg.dataset.paint === 'del' ? '#e53935' : '#2e9e43', svg.dataset.ranges);
  }
</script>
</body>
</html>`;
}
