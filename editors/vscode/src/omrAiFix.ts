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
// is listed; Accept marks a row, Apply writes the accepted rows as ONE WorkspaceEdit (one
// Ctrl+Z), and the marks go with the lines they were on. omrAiFixCore.ts is the editor-free
// half; the scan's image goes to the model at M2 (here it is shown to the person only).

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { execFile } from 'child_process';
import type { LanguageClient } from 'vscode-languageclient/node';
import { AiTransformDeps, loadGrammar } from './aiTransform';
import { ChatClient, ChatMessage, resolveChatClient } from './modelClient';
import {
    BarCheck, BarNeighbours, Diag, FixTarget, FixableTodo, GateVerdict, Proposal, Rgba, acceptableByAll, anchorOf, averageOf,
    batchesOf, checkedByDefault, cropRect, decodeImage, diagnosticsOn, estimateOf, fixTargets, gateOf, headOf, labelOf,
    neighboursOf, parseProposal, readerBarOf, repairPromptOf, scanCropPng, spliceBar, systemPromptOf, targetsInLines, taskPromptOf,
    verifyVerdict, withSample, withVerify,
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
    readonly seconds: number;
    state: 'pending' | 'accepted' | 'rejected' | 'applied' | 'skipped';
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
        { dispose: () => { panel?.dispose(); lock?.dispose(); output?.dispose(); } });
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
                    run.rows.push({ ...row, seconds });
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
        return 'no answer (the reply was not the JSON asked for)';
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
            log(`  ${labelOf(target)}: the model failed: ${err?.message ?? err}`);
            return { index, target, proposal: null, state: 'pending' };
        }
        const proposal = parseProposal(raw, target.written);
        if (!proposal) {
            // Not the JSON asked for: once more, with the contract restated.
            if (attempt < MAX_REPAIRS) {
                messages.push({ role: 'assistant', content: raw });
                messages.push({ role: 'user', content: 'Answer with the one JSON object only: {"bar": …, "reason": …, "confidence": …}.' });
                continue;
            }
            return { index, target, proposal: null, state: 'pending' };
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
        panel.onDidDispose(() => { panel = undefined; shownRun = undefined; });
        panel.webview.onDidReceiveMessage((m: { action?: string; index?: number }) => void onPanelMessage(deps, m));
    } else {
        panel.webview.options = { ...panel.webview.options, localResourceRoots: roots };
    }
    panel.webview.html = panelHtml(deps, panel.webview, run);
    panel.reveal(vscode.ViewColumn.Beside, true);
}

async function onPanelMessage(deps: OmrAiFixDeps, m: { action?: string; index?: number }): Promise<void> {
    const run = shownRun;
    if (!run || !panel) {
        return;
    }
    const row = Number.isInteger(m.index) ? run.rows.find(r => r.index === m.index) : undefined;
    switch (m.action) {
        case 'accept':
            if (row && row.proposal?.bar && row.state !== 'applied') { row.state = row.state === 'accepted' ? 'pending' : 'accepted'; }
            break;
        case 'reject':
            if (row && row.state !== 'applied') { row.state = row.state === 'rejected' ? 'pending' : 'rejected'; }
            break;
        case 'acceptAll':
            for (const r of run.rows) {
                if (r.proposal?.bar && r.gate && acceptableByAll(r.gate, r.proposal.confidence) && r.state === 'pending') {
                    r.state = 'accepted';
                }
            }
            break;
        case 'open':
            if (row) { await openAt(run, row); }
            return;
        case 'apply':
            await applyAccepted(run);
            break;
        case 'close':
            panel.dispose();
            return;
        default:
            return;
    }
    panel.webview.html = panelHtml(deps, panel.webview, run);
}

/** The editor at the row's line, as it is now (anchorOf follows a moved line). */
async function openAt(run: FixRun, row: FixRow): Promise<void> {
    const doc = await vscode.workspace.openTextDocument(run.uri);
    const at = anchorOf(doc.getText(), row.target);
    const pos = doc.positionAt(at >= 0 ? at : Math.min(row.target.start, doc.getText().length));
    const editor = await vscode.window.showTextDocument(doc, { viewColumn: vscode.ViewColumn.One, preserveFocus: false });
    editor.selection = new vscode.Selection(pos, pos);
    editor.revealRange(new vscode.Range(pos, pos), vscode.TextEditorRevealType.InCenterIfOutsideViewport);
}

/** The accepted rows, as ONE WorkspaceEdit (one Ctrl+Z): each at its line as it is now; a line
 *  that moved is followed, one that is gone or doubled is skipped and said so. The @todo marks
 *  go with the lines (a proposal carries none). */
async function applyAccepted(run: FixRun): Promise<void> {
    const accepted = run.rows.filter(r => r.state === 'accepted' && r.proposal?.bar);
    if (accepted.length === 0) {
        vscode.window.showInformationMessage('Lily#: no row is accepted. Accept the proposals to write, then Apply.');
        return;
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
        edit.replace(run.uri, new vscode.Range(doc.positionAt(at), doc.positionAt(at + r.target.written.length)), r.proposal!.bar!);
        written.push(r);
    }
    const ok = written.length > 0 && await vscode.workspace.applyEdit(edit);
    if (ok) {
        for (const r of written) { r.state = 'applied'; }
        const proposals = run.rows.filter(r => r.proposal?.bar).length;
        log(`Fix OMR marks: applied ${written.length} ${written.length === 1 ? 'bar' : 'bars'}`
            + ` (${run.rows.filter(r => r.state === 'applied').length} of ${proposals} proposals taken so far, scan ${run.images ? 'on' : 'off'})`
            + (skipped.length ? `; ${skipped.length} skipped (the line moved and could not be found)` : ''));
        vscode.window.showInformationMessage(`Lily#: wrote ${written.length} ${written.length === 1 ? 'bar' : 'bars'} (Ctrl+Z undoes them all)`
            + (skipped.length ? `; ${skipped.length} skipped — ${skipped.map(r => labelOf(r.target)).join(', ')} could not be found.` : '.'));
    } else if (written.length > 0) {
        vscode.window.showErrorMessage('Lily#: the edit could not be applied.');
    } else {
        for (const r of skipped) { r.state = 'skipped'; }
        vscode.window.showWarningMessage(`Lily#: none of the accepted lines could be found as written — the file changed too much. Run the fix again.`);
    }
}

// ---------------------------------------------------------------------------------------------
// The panel's page
// ---------------------------------------------------------------------------------------------

function esc(s: string): string {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
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
function scoreCell(id: string, sym: NonNullable<ReturnType<typeof symbolOf>>, lo: number, hi: number, define: boolean): string {
    return `<svg class="pic score" data-sym="${id}" data-lo="${lo}" data-hi="${hi}" viewBox="${esc(sym.viewBox)}" preserveAspectRatio="xMidYMid meet">`
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
        const page = t.crop ? pageUris.get(t.crop.page) : undefined;
        const crop = t.crop && page ? cropRect(t.crop.box, page) : undefined;
        const scan = crop && page
            ? `<svg class="pic" viewBox="${crop.x} ${crop.y} ${crop.width} ${crop.height}" preserveAspectRatio="xMidYMid meet">`
              + `<image href="${page.uri}" width="${page.width}" height="${page.height}"/></svg>`
            : `<div class="none">no scan</div>`;
        const now = before
            ? scoreCell('before', before, t.start, t.end, false)
            : `<div class="none">not rendered</div>`;
        const after = r.after?.Svg ? symbolOf(r.after.Svg) : undefined;
        let proposal: string;
        if (!r.proposal) {
            proposal = `<div class="none">no answer</div>`;
        } else if (!r.proposal.bar) {
            proposal = `<div class="none">no proposal</div>`;
        } else if (after) {
            proposal = scoreCell(`after-${r.index}`, after, t.start, t.start + r.proposal.bar.length, true);
        } else {
            proposal = `<div class="none">${r.gate && !(r.gate.compile && r.gate.length) ? 'did not pass' : 'not rendered'}</div>`;
        }
        const gates = r.gate
            ? `<span class="${r.gate.compile ? 'ok' : 'bad'}">compile ${r.gate.compile ? '✓' : '✗'}</span> `
              + `<span class="${r.gate.length ? 'ok' : 'bad'}">length ${r.gate.length ? '✓' : '✗'}</span> `
              + (r.gate.verify === undefined ? `<span class="dim">verify —</span>`
                  : `<span class="${r.gate.verify ? 'ok' : 'bad'}">verify ${r.gate.verify ? '✓' : '✗'}</span>`)
            : `<span class="dim">—</span>`;
        const failed = r.gate && !(r.gate.compile && r.gate.length);
        const reason = !r.proposal ? 'The reply was not the JSON asked for.'
            : !r.proposal.bar ? (failed ? `No proposal passed: ${r.gate!.problems.join(' / ') || r.proposal.reason}` : r.proposal.reason || 'The model gave no reason.')
            : r.gate?.verify === false ? `${r.proposal.reason ? `${r.proposal.reason} — ` : ''}${r.gate.problems.filter(p => p.startsWith('the scan says')).join(' / ')}`
            : r.proposal.reason || '';
        const sure = r.proposal?.confidence === null || r.proposal?.confidence === undefined ? '' : ` · confidence ${r.proposal.confidence.toFixed(2)}`;
        const can = !!(r.proposal?.bar) && r.state !== 'applied' && r.state !== 'skipped';
        const buttons = r.state === 'applied' ? `<span class="ok">written</span>`
            : r.state === 'skipped' ? `<span class="bad">skipped</span>`
            : `<button data-a="accept" data-i="${r.index}" class="${r.state === 'accepted' ? 'on' : ''}" ${can ? '' : 'disabled'}>Accept</button>`
              + `<button data-a="reject" data-i="${r.index}" class="${r.state === 'rejected' ? 'on' : ''}">Reject</button>`;
        return `<tr class="row ${r.state}" data-i="${r.index}">
  <td class="who"><div class="label">${esc(labelOf(t))}</div><div class="memo">${esc(t.memos.join('; '))}</div>
    <div class="memo dim">line ${t.line + 1}${t.confidence === undefined ? '' : ` · reader ${t.confidence.toFixed(2)}`} · ${r.seconds.toFixed(1)} s</div>
    <button data-a="open" data-i="${r.index}" class="link">Open in editor</button></td>
  <td class="cell">${scan}</td>
  <td class="cell">${now}<pre class="src">${esc(t.written)}</pre></td>
  <td class="cell">${proposal}${r.proposal?.bar ? `<pre class="src">${esc(r.proposal.bar)}</pre>` : ''}</td>
  <td class="why">${esc(reason)}${sure}<div class="gates">${gates}</div></td>
  <td class="act">${buttons}</td>
</tr>`;
    }).join('\n');

    const accepted = run.rows.filter(r => r.state === 'accepted').length;
    const acceptable = run.rows.filter(r => r.state === 'pending' && r.proposal?.bar && r.gate && acceptableByAll(r.gate, r.proposal.confidence)).length;
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
tr.accepted { background: color-mix(in srgb, var(--vscode-testing-iconPassed, #3c3) 12%, transparent); }
tr.rejected { opacity:.55; }
tr.applied { opacity:.7; }
.who { width:150px; }
.label { font-weight:600; }
.memo { font-size:12px; opacity:.85; }
.dim { opacity:.6; }
.cell { width:24%; }
.pic { display:block; width:100%; height:120px; background:#fff; border:1px solid var(--vscode-panel-border); }
.none { height:120px; display:flex; align-items:center; justify-content:center; opacity:.6; border:1px dashed var(--vscode-panel-border); }
.src { margin:4px 0 0; font-family: var(--vscode-editor-font-family, monospace); font-size:11px; white-space:pre-wrap; opacity:.9; }
.why { font-size:12px; min-width:140px; }
.gates { margin-top:6px; }
.ok { color: var(--vscode-testing-iconPassed, #3c3); }
.bad { color: var(--vscode-testing-iconFailed, #e33); }
.act { white-space:nowrap; width:140px; }
button { padding:4px 10px; font-size:12px; border:1px solid var(--vscode-button-border, transparent); border-radius:4px; cursor:pointer; margin:0 2px 4px 0;
         background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
button.primary, button.on { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
button:disabled { opacity:.4; cursor:default; }
button.link { background:none; border:none; padding:0; color: var(--vscode-textLink-foreground); font-size:12px; }
.footer { display:flex; gap:8px; padding:10px 12px; border-top:1px solid var(--vscode-panel-border); flex-shrink:0; align-items:center; }
.store { position:absolute; width:0; height:0; overflow:hidden; }
</style>
</head>
<body>
${before ? `<svg class="store" aria-hidden="true"><symbol id="before" viewBox="${esc(before.viewBox)}">${before.inner}</symbol></svg>` : ''}
<div class="header">
  <div><div class="title">OMR fixes — ${esc(name)}</div>
  <div class="cap">${run.rows.length} ${run.rows.length === 1 ? 'bar' : 'bars'} · ${run.rows.filter(r => r.proposal?.bar).length} with a proposal · nothing is written until Apply</div></div>
  <div class="spacer"></div>
  <button id="acceptAll" ${acceptable ? '' : 'disabled'} title="Accept every row whose gates pass and whose confidence is 0.5 or more">Accept all (${acceptable})</button>
</div>
<div class="main">
<table>
<thead><tr><th>Bar</th><th>Scan</th><th>Now</th><th>Proposal</th><th>Why · gates</th><th></th></tr></thead>
<tbody>
${rows}
</tbody>
</table>
</div>
<div class="footer">
  <button class="primary" id="apply" ${accepted ? '' : 'disabled'}>Apply ${accepted} accepted (one undo)</button>
  <div class="spacer"></div>
  <button id="close">Close</button>
</div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  document.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b) return;
    if (b.id === 'acceptAll') vscode.postMessage({ action: 'acceptAll' });
    else if (b.id === 'apply') vscode.postMessage({ action: 'apply' });
    else if (b.id === 'close') vscode.postMessage({ action: 'close' });
    else if (b.dataset.a) vscode.postMessage({ action: b.dataset.a, index: Number(b.dataset.i) });
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
  const boxesOf = {};
  for (const svg of document.querySelectorAll('svg.score')) {
    const id = svg.dataset.sym;
    boxesOf[id] = boxesOf[id] || Array.from(document.querySelectorAll('#' + id + ' rect.bar-staff'));
    cropTo(svg, boxesOf[id], +svg.dataset.lo, +svg.dataset.hi);
  }
</script>
</body>
</html>`;
}
