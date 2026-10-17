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

// "Lily#: Show Original Scan" (LilySharp-Omr proposal B3): the pages an OMR reader read, beside
// the .lys it wrote, with the marks it was unsure of framed in red dashes. A click on a bar goes
// to the line the reader wrote it on, carried through the edits since (omrScanCore.anchorsOf —
// the reader's bar numbers are its own, not the page's); the caret lights its box: a mark by its
// key (lilysharp/todos, so it is found wherever the mark has moved), a bar by that line.
// omrScanCore.ts is the editor-free half; the side file x.omr.json is the reader's and is only
// read.

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import type { LanguageClient } from 'vscode-languageclient/node';
import {
    BarAnchor, PrintedBar, ScanSideFile, ScanTarget, ServerTodo, anchorsOf, barOnLine, flaggedMarks, hiddenTodoKeys, parseSideFile, resolvedTodoKeys, barStaff,
    SCAN_DEFAULT_THRESHOLD, pageLabel, scanBarForPrinted, scanTitle, shiftAnchors, shownImage, sideFileOf, todoStroke,
    todoTarget, lineOfScanBar, scanBarForStart,
} from './omrScanCore';

export interface OmrScanDeps {
    readonly output: vscode.OutputChannel;
    /** The language client, started and ready (undefined when it cannot start). */
    readonly client: () => Promise<LanguageClient | undefined>;
    /** Posts to the score preview of a .lys, if one is open (B4: the two scroll together). */
    readonly postToPreview: (lys: vscode.Uri, message: unknown) => void;
}

interface TodosResponse { Todos?: ServerTodo[]; Version: number; Error?: string | null }
interface PlaceAtResponse { Places?: { Measure?: number | null }[] | null; Version: number }

/** One open scan view: the .lys it follows and the side file it draws. */
interface ScanView {
    readonly panel: vscode.WebviewPanel;
    readonly lys: vscode.Uri;
    side: ScanSideFile;
    /** The side file's bars pinned in the document (the ones it gives a line). */
    anchors: BarAnchor[];
    /** The marks of the document's last version asked about. */
    todos?: { version: number; list: ServerTodo[] };
    lastTarget?: string;
    /** The bar the page prints for each pinned bar's line, for the document's version (B4). */
    printed?: { version: number; bars: PrintedBar[] };
    /** Whether the scan and the preview scroll together (the view's checkbox). */
    linked: boolean;
    /** The view's slider (*Marks below*): the preview hides what it hides. */
    threshold?: number;
}

const views = new Map<string, ScanView>();

/** Whether the .lys at `uri` has a side file beside it (the command's when-clause). */
export function hasScan(uri: vscode.Uri | undefined): boolean {
    return !!uri && uri.scheme === 'file' && /\.lys$/i.test(uri.fsPath) && fs.existsSync(sideFileOf(uri.fsPath));
}

/** Wires the caret and the side file's changes to every open view. Call once from activate. */
export function registerScanFollow(context: vscode.ExtensionContext, deps: OmrScanDeps): void {
    let timer: ReturnType<typeof setTimeout> | undefined;
    let marksTimer: ReturnType<typeof setTimeout> | undefined;
    context.subscriptions.push(
        vscode.window.onDidChangeTextEditorSelection(e => {
            const view = views.get(e.textEditor.document.uri.toString());
            if (!view) {
                return;
            }
            if (timer) { clearTimeout(timer); }
            // The caret's last stop, not every step of a held arrow key.
            timer = setTimeout(() => void follow(view, e.textEditor, deps), 120);
        }),
        vscode.workspace.onDidChangeTextDocument(e => {
            const view = views.get(e.document.uri.toString());
            if (view) {
                view.todos = undefined;
                view.printed = undefined;
                if (marksTimer) { clearTimeout(marksTimer); }
                marksTimer = setTimeout(() => void markPreview(view, deps), 400);
                shiftAnchors(view.anchors, e.contentChanges.map(c =>
                    ({ offset: c.rangeOffset, removed: c.rangeLength, inserted: c.text.length })));
            }
        }),
        // The view's context menu (menus/webview/context, as the preview's: Fit Width and Reset
        // Zoom in place of the webview's own Cut/Copy/Paste — owner's request 2026-10-17). Thin
        // relays: the zoom lives in the webview, so the command only names the action.
        vscode.commands.registerCommand('lilysharp.scanFitWidth', () => postToActiveScan('ctxFitWidth')),
        vscode.commands.registerCommand('lilysharp.scanResetZoom', () => postToActiveScan('ctxResetZoom')));
}

function postToActiveScan(type: string): void {
    for (const view of views.values()) {
        if (view.panel.active) {
            void view.panel.webview.postMessage({ type });
            return;
        }
    }
}

/** The command: the active .lys's scan, beside it. */
export async function showScan(
    context: vscode.ExtensionContext, deps: OmrScanDeps, uri?: vscode.Uri, column?: vscode.ViewColumn): Promise<void> {
    const lys = uri ?? vscode.window.activeTextEditor?.document.uri;
    if (!lys || !/\.lys$/i.test(lys.fsPath)) {
        vscode.window.showInformationMessage('Lily#: open a .lys file to show the scan it was read from.');
        return;
    }
    const existing = views.get(lys.toString());
    if (existing) {
        existing.panel.reveal(undefined, true);
        return;
    }
    const sidePath = sideFileOf(lys.fsPath);
    const side = readSide(sidePath);
    if (!side) {
        vscode.window.showInformationMessage(
            `Lily#: ${path.basename(lys.fsPath)} has no scan beside it (${path.basename(sidePath)}, which an OMR reader writes).`);
        return;
    }

    // The bars are pinned to the text as it is NOW — the reader's, when the view opens on a
    // score it has just written; a score edited before its view opened pins by moved lines.
    const doc = await vscode.workspace.openTextDocument(lys);
    const panel = vscode.window.createWebviewPanel('lilysharpScan', scanTitle(side.pages, lys.fsPath),
        { viewColumn: column ?? vscode.ViewColumn.Beside, preserveFocus: true },
        { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: rootsOf(side, sidePath) });
    const view: ScanView = { panel, lys, side, anchors: anchorsOf(side, doc.getText()), linked: true };
    views.set(lys.toString(), view);
    render(view);

    // The reader may write the side file again (a re-read, B5's verify on save).
    const watcher = vscode.workspace.createFileSystemWatcher(
        new vscode.RelativePattern(vscode.Uri.file(path.dirname(sidePath)), path.basename(sidePath)));
    const reload = () => {
        const next = readSide(sidePath);
        if (next) {
            view.side = next;
            // A side file written again is written against the .lys on disk.
            view.anchors = anchorsOf(next, fs.existsSync(lys.fsPath) ? fs.readFileSync(lys.fsPath, 'utf8') : doc.getText());
            view.lastTarget = undefined;
            view.printed = undefined;
            panel.webview.options = { ...panel.webview.options, localResourceRoots: rootsOf(next, sidePath) };
            panel.title = scanTitle(next.pages, lys.fsPath);
            render(view);
            hidePreviewTodos(view, deps);
            void markPreview(view, deps);
        }
    };
    watcher.onDidChange(reload);
    watcher.onDidCreate(reload);

    panel.webview.onDidReceiveMessage(async (m: { type: string; index?: number; on?: boolean; value?: number; top?: boolean; cut?: number }) => {
        try {
            if (m.type === 'bar' && Number.isInteger(m.index)) {
                await goToBar(view, m.index!);
            } else if (m.type === 'visibleBar' && m.top === true && view.linked) {
                // Scrolled all the way up: the preview goes all the way up too (previewShowsBar).
                deps.postToPreview(lys, { type: 'showBar', top: true });
            } else if (m.type === 'visibleBar' && Number.isInteger(m.index) && view.linked) {
                const printed = (await printedOf(view, deps)).find(b => b.index === m.index)?.printed;
                if (printed !== null && printed !== undefined) {
                    // cut: how much of the bar is above the scan's top edge; the preview cuts
                    // its copy alike.
                    deps.postToPreview(lys, { type: 'showBar', bar: printed, cut: typeof m.cut === 'number' ? m.cut : 0 });
                }
            } else if (m.type === 'hoverBar') {
                // The bar under the mouse lights on the preview too: each of its printed bars
                // whose first note is on the bar's line (lineOfScanBar).
                const doc = await vscode.workspace.openTextDocument(lys);
                const line = Number.isInteger(m.index) ? lineOfScanBar(view.side, view.anchors, doc.getText(), m.index!) : undefined;
                deps.postToPreview(lys, line ? { type: 'peerBars', from: line[0], to: line[1] } : { type: 'peerBars' });
            } else if (m.type === 'link') {
                view.linked = !!m.on;
            } else if (m.type === 'threshold' && typeof m.value === 'number') {
                view.threshold = m.value;
                hidePreviewTodos(view, deps);
                void markPreview(view, deps);   // the frames follow the marks they hold
            }
        } catch (err) {
            deps.output.appendLine(`Scan: ${err}`);
        }
    });
    panel.onDidDispose(() => {
        watcher.dispose();
        views.delete(lys.toString());
        deps.postToPreview(lys, { type: 'markBars', marks: [] });
        // With the view gone nothing filters the marks: the preview shows them all again.
        deps.postToPreview(lys, { type: 'hideTodos', keys: [] });
    });

    const editor = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === lys.toString());
    if (editor) {
        void follow(view, editor, deps);
    }
    void markPreview(view, deps);
}

function readSide(sidePath: string): ScanSideFile | undefined {
    try {
        return parseSideFile(fs.readFileSync(sidePath, 'utf8'), sidePath);
    } catch {
        return undefined;
    }
}

/** The folders the webview may load images from: those of the pages it shows. */
function rootsOf(side: ScanSideFile, sidePath: string): vscode.Uri[] {
    const dirs = new Set<string>([path.dirname(sidePath)]);
    for (const p of side.pages) {
        const shown = shownImage(p);
        if (shown) { dirs.add(path.dirname(shown)); }
    }
    return [...dirs].map(d => vscode.Uri.file(d));
}

async function todosOf(view: ScanView, doc: vscode.TextDocument, deps: OmrScanDeps): Promise<ServerTodo[]> {
    if (view.todos && view.todos.version === doc.version) {
        return view.todos.list;
    }
    const client = await deps.client();
    if (!client) {
        return [];
    }
    const resp = await client.sendRequest<TodosResponse>('lilysharp/todos', { textDocument: { uri: doc.uri.toString() } });
    if (resp.Error) {
        // Not cached: a failed answer is not "no marks" (markPreview reads the cache to tell).
        return [];
    }
    const list = resp.Todos ?? [];
    view.todos = { version: doc.version, list };
    return list;
}

/** The bar the page prints for each pinned bar's line (one lilysharp/placeAt for them all). */
async function printedOf(view: ScanView, deps: OmrScanDeps): Promise<PrintedBar[]> {
    const doc = await vscode.workspace.openTextDocument(view.lys);
    if (view.printed && view.printed.version === doc.version) {
        return view.printed.bars;
    }
    const client = await deps.client();
    if (!client) {
        return [];
    }
    const anchors = [...view.anchors];
    const resp = await client.sendRequest<PlaceAtResponse>('lilysharp/placeAt', {
        textDocument: { uri: doc.uri.toString() }, offset: 0, offsets: anchors.map(a => a.offset),
    });
    const bars = anchors.map((a, i) => ({ index: view.side.measures.indexOf(a.bar), printed: resp.Places?.[i]?.Measure }));
    view.printed = { version: doc.version, bars };
    return bars;
}

/** Frames, on the preview, what each mark covers on the scan (B4: what the reader flagged, on
 *  both sides alike): the part-bars of its box (flaggedMarks → coverageOf), each as the line it
 *  is written on (lineOfScanBar, carried through the edits) — the preview frames the staff's
 *  bars whose first note is on it; a mark the side file does not know, by where it is written. */
async function markPreview(view: ScanView, deps: OmrScanDeps): Promise<void> {
    try {
        const doc = await vscode.workspace.openTextDocument(view.lys);
        const todos = await todosOf(view, doc, deps);
        const text = doc.getText();
        const marks = flaggedMarks(todos, view.side,
            view.threshold === undefined ? [] : hiddenTodoKeys(view.side.todos, view.threshold))
            .map(m => ({
                at: m.at,
                lines: m.bars.map(i => lineOfScanBar(view.side, view.anchors, text, i)).filter((l): l is [number, number] => !!l),
            }));
        deps.postToPreview(view.lys, { type: 'markBars', marks });
        // …and the scan drops the frames of the marks the .lys no longer carries — only on a
        // real answer (todosOf caches nothing else), or every mark would look resolved.
        if (view.todos?.version === doc.version) {
            void view.panel.webview.postMessage({ type: 'resolved', keys: resolvedTodoKeys(view.side.todos, todos) });
        }
    } catch (err) {
        deps.output.appendLine(`Scan: ${err}`);
    }
}

/** The preview of `lys` scrolled to the printed bar `bar`, cut off above its top edge by `cut`
 *  of its height — or all the way up (`'top'`): the linked scan follows, to its copy of that bar
 *  cut alike, or to its own top (the title stays in view on both; owner's requests 2026-10-17:
 *  "片方を一番上にしたら相手も一番上に", "上部の見切れる位置が一致すると使いやすい"). */
export async function previewShowsBar(lys: string, bar: number | 'top', deps: OmrScanDeps, cut = 0): Promise<void> {
    const view = views.get(lys);
    if (!view || !view.linked) {
        return;
    }
    if (bar === 'top') {
        void view.panel.webview.postMessage({ type: 'showBar', top: true });
        return;
    }
    const index = scanBarForPrinted(await printedOf(view, deps), bar);
    if (index !== undefined) {
        void view.panel.webview.postMessage({ type: 'showBar', index, cut });
    }
}

/** The preview of `lys` has the mouse over the bar whose first note is written at `start` (null:
 *  over none): the scan lights the bar written on that line (scanBarForStart). */
export async function previewHoversBar(lys: string, start: number | null, deps: OmrScanDeps): Promise<void> {
    const view = views.get(lys);
    if (!view) {
        return;
    }
    let index: number | undefined;
    if (start !== null) {
        const doc = await vscode.workspace.openTextDocument(view.lys);
        index = scanBarForStart(view.side, view.anchors, doc.getText(), start);
    }
    void view.panel.webview.postMessage({ type: 'peer', index: index ?? null });
}

/** A preview of `lys` (re)loaded: it shows no marks until told again. */
export function previewReady(lys: string, deps: OmrScanDeps): void {
    const view = views.get(lys);
    if (view) {
        void markPreview(view, deps);
        hidePreviewTodos(view, deps);
    }
}

/** The preview hides the marks the view's slider hides (owner's request 2026-10-12), by key —
 *  the SVG stamps every piece of a mark with it (data-todo). Also when the side file is read
 *  again: the marks and their confidences may have changed. */
function hidePreviewTodos(view: ScanView, deps: OmrScanDeps): void {
    if (view.threshold !== undefined) {
        deps.postToPreview(view.lys, { type: 'hideTodos', keys: hiddenTodoKeys(view.side.todos, view.threshold) });
    }
}

/** Lights what the caret is on: its mark's box, else its bar's. */
async function follow(view: ScanView, editor: vscode.TextEditor, deps: OmrScanDeps): Promise<void> {
    const doc = editor.document;
    const offset = doc.offsetAt(editor.selection.active);
    const line = doc.lineAt(editor.selection.active.line);
    const target: ScanTarget | undefined = todoTarget(view.side, await todosOf(view, doc, deps), offset)
        ?? barOnLine(view.anchors, doc.offsetAt(line.range.start), doc.offsetAt(line.range.end));
    const id = target ? `${target.kind}:${target.label}:${target.page}` : '';
    if (id === view.lastTarget) {
        return;
    }
    view.lastTarget = id;
    // A bar lights its staff only, as its hover tints it; a mark its whole box.
    const lit = target && target.staff ? { ...target, box: target.staff } : target;
    void view.panel.webview.postMessage({ type: 'light', target: lit ?? null });
}

async function editorOf(view: ScanView): Promise<vscode.TextEditor> {
    const shown = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === view.lys.toString());
    const doc = shown?.document ?? await vscode.workspace.openTextDocument(view.lys);
    return vscode.window.showTextDocument(doc, { viewColumn: shown?.viewColumn ?? vscode.ViewColumn.One, preserveFocus: false });
}

/** Goes to the line a bar of the side file (`side.measures[index]`) is written on. */
async function goToBar(view: ScanView, index: number): Promise<void> {
    const bar = view.side.measures[index];
    const anchor = bar && view.anchors.find(a => a.bar === bar);
    if (!anchor) {
        vscode.window.setStatusBarMessage(`Lily#: the reader did not say where bar ${bar?.measure ?? ''} is written`, 3000);
        return;
    }
    select(await editorOf(view), anchor.offset);
}

function select(editor: vscode.TextEditor, offset: number): void {
    // A position starts at the item's leading trivia; land on its first character.
    const text = editor.document.getText();
    while (offset < text.length && /\s/.test(text[offset])) { offset++; }
    const pos = editor.document.positionAt(offset);
    editor.selection = new vscode.Selection(pos, pos);
    editor.revealRange(new vscode.Range(pos, pos), vscode.TextEditorRevealType.InCenterIfOutsideViewport);
}

function render(view: ScanView): void {
    const { panel, side } = view;
    const nonce = [...Array(16)].map(() => Math.floor(Math.random() * 36).toString(36)).join('');
    const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    const rect = (b: readonly number[]) => `x="${b[0]}" y="${b[1]}" width="${b[2] - b[0]}" height="${b[3] - b[1]}"`;

    const pages = side.pages.map(p => {
        const shown = shownImage(p);
        if (!shown || !fs.existsSync(shown)) {
            return `<section class="page missing"><p>Page ${p.page}: ${esc(pageLabel(p))} cannot be shown here`
                + `${shown ? ' (the file is not there)' : ' (a webview draws PNG, JPEG, GIF, BMP and WebP)'}.</p></section>`;
        }
        const src = panel.webview.asWebviewUri(vscode.Uri.file(shown)).toString();
        const bars = side.measures.map((m, i) => m.page !== p.page ? '' :
            `<g class="bar" data-i="${i}"><rect class="hit" ${rect(m.box)}/><rect class="tint" ${rect(barStaff(m))}/>`
            + `<title>bar ${m.measure}${m.part ? ` (${esc(m.part)})` : ''}</title></g>`).join('');
        // A dashed outline, heavier and darker the less sure the reader is (a quiet one a faint
        // hairline); the slider hides the surer. Drawn after the bars, over them, and taking no
        // pointer (the CSS), so the bar under a mark is still the thing hovered and clicked.
        const todos = side.todos.filter(t => t.page === p.page).map(t => {
            const s = todoStroke(t), sure = t.confidence === undefined ? '' : ` (${t.confidence.toFixed(2)})`;
            return `<rect class="todo${t.quiet ? ' quiet' : ''}" ${rect(t.box)} data-key="${esc(t.key)}" `
                + `data-c="${t.confidence ?? ''}" style="stroke-width: ${s.width}px; stroke-opacity: ${s.opacity}">`
                + `<title>${esc(t.memo ?? t.key)}${sure}</title></rect>`;
        }).join('');
        return `<section class="page" data-page="${p.page}" title="${esc(pageLabel(p))}">`
            + `<img src="${src}" alt="page ${p.page}">`
            + `<svg viewBox="0 0 ${p.width} ${p.height}" preserveAspectRatio="none">${bars}${todos}`
            + `<rect class="light" x="0" y="0" width="0" height="0"/></svg></section>`;
    }).join('');

    panel.webview.html = `<!DOCTYPE html>
<html lang="en"><head><meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${panel.webview.cspSource}; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  body { margin: 0; padding: 0 8px 8px; background: var(--vscode-editor-background); color: var(--vscode-foreground);
         font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); }
  /* The head — what was read, the link checkbox, the slider — stays put while the pages scroll
     under it (owner's request 2026-10-17); the script measures its bottom as the view's top edge. */
  .note { position: sticky; top: 0; z-index: 1; margin: 0 -8px 8px; padding: 8px;
          background: var(--vscode-editor-background); }
  .note > span.text { opacity: .8; }
  /* The pages are dragged to scroll, as the preview's sheet is (installDragScroll below): an
     open hand over them, a closed one while they move; a bar and a mark keep the pointer. */
  /* Ctrl+wheel zooms the pages (the script sets --zoom; 1 = as wide as the pane): a real width,
     not a transform, so the bars' and marks' geometry the script reads stays the drawn one. */
  .page { position: relative; margin: 0 auto 12px; width: calc(var(--zoom, 1) * 100%); cursor: grab; }
  .page img { display: block; width: 100%; height: auto; background: #fff; -webkit-user-drag: none; user-select: none; }
  .page svg { position: absolute; inset: 0; width: 100%; height: 100%; }
  .page.missing { padding: 12px; border: 1px dashed var(--vscode-editorWidget-border, #888); }
  /* A bar: the click takes its whole box, the tint its staff alone — as in the preview, in the
     colour the caret's bar is lit in (owner's request 2026-10-14). */
  g.bar { cursor: pointer; }
  g.bar rect { fill: transparent; stroke: none; }
  g.bar:hover rect.tint, g.bar.peer rect.tint { fill: rgba(0, 120, 215, .14); }
  /* A mark's frame: the red dashes the preview frames a flagged bar with (rect.bar-box.flagged
     in extension.ts), and no more than a frame — it takes no pointer, so the bar under it is
     hovered, tinted and clicked as any other (owner's request 2026-10-17; until then the frame
     was solid, filled red under the mouse, and its click went to the @todo). */
  rect.todo { fill: none; stroke: #e53935; stroke-dasharray: 4 3; vector-effect: non-scaling-stroke; pointer-events: none; }
  .filter { display: inline-flex; align-items: center; gap: 6px; margin-left: 12px; }
  .filter input { width: 140px; }
  rect.light { fill: rgba(0, 120, 215, .14); stroke: none; pointer-events: none; }
  body.dragging, body.dragging * { cursor: grabbing; }
  /* The zoom read-out, as the preview's. */
  .zoom-info { position: fixed; bottom: 10px; right: 10px; z-index: 2; background: rgba(0, 0, 0, .7); color: #fff;
               padding: 4px 8px; border-radius: 4px; font-family: monospace; font-size: 12px; opacity: 0; transition: opacity .3s; }
  .zoom-info.visible { opacity: 1; }
</style></head>
<!-- preventDefaultContextMenuItems drops the webview's own Cut/Copy/Paste (nothing here to cut),
     leaving what package.json contributes under menus/webview/context for 'lilysharpScan'. -->
<body data-vscode-context='{"preventDefaultContextMenuItems": true}'>
<p class="note"><span class="text">${esc(path.basename(view.lys.fsPath))} — read from ${side.pages.length === 1 ? 'this page' : `these ${side.pages.length} pages`}. `
        + `Red: marked to check — heavier where the reader is less sure. A click goes to the music; the caret lights its bar here.</span> `
        + `<label><input type="checkbox" id="link" checked> Scroll with the preview</label>`
        + `<label class="filter" title="Show the marks the reader is less sure of than this (its confidence, 0 to 1)">`
        + `Marks below <input type="range" id="thr" min="0.05" max="1" step="0.05" value="${SCAN_DEFAULT_THRESHOLD}">`
        + `<span id="thrOut"></span></label></p>
${pages || '<p>The side file lists no page.</p>'}
<div class="zoom-info" id="zoomInfo">100%</div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const link = document.getElementById('link');
  const state = vscode.getState() || {};
  if (state.linked === false) link.checked = false;
  vscode.postMessage({ type: 'link', on: link.checked });
  link.addEventListener('change', () => {
    state.linked = link.checked;
    vscode.setState(state);
    vscode.postMessage({ type: 'link', on: link.checked });
  });

  // The marks the reader is less sure of than the slider show (one with no confidence always).
  const thr = document.getElementById('thr'), thrOut = document.getElementById('thrOut');
  if (typeof state.threshold === 'number') thr.value = String(state.threshold);
  // The marks the .lys no longer carries (resolved, sent by the extension) show at no value.
  let resolved = new Set();
  function filter() {
    const v = Number(thr.value);
    let shown = 0, total = 0;
    for (const r of document.querySelectorAll('rect.todo')) {
      if (resolved.has(r.dataset.key)) { r.style.display = 'none'; continue; }
      total++;
      const on = r.dataset.c === '' || v >= 1 || Number(r.dataset.c) < v;
      r.style.display = on ? '' : 'none';
      if (on) shown++;
    }
    thrOut.textContent = v.toFixed(2) + ' (' + shown + ' / ' + total + ')';
  }
  thr.addEventListener('input', () => { state.threshold = Number(thr.value); vscode.setState(state); filter(); });
  // The preview of the .lys hides the same marks (the extension turns the value into keys).
  thr.addEventListener('input', () => vscode.postMessage({ type: 'threshold', value: Number(thr.value) }));
  vscode.postMessage({ type: 'threshold', value: Number(thr.value) });
  filter();
  document.addEventListener('click', e => {
    const r = e.target.closest('g.bar');
    if (r) vscode.postMessage({ type: 'bar', index: Number(r.dataset.i) });
  });

  // The bar under the mouse lights on the preview too, and the preview's bar under the mouse
  // lights here (peer) — the owner's request 2026-10-14. Sent when it changes only.
  let hovered = null;
  function hover(g) {
    if (g === hovered) return;
    hovered = g;
    vscode.postMessage({ type: 'hoverBar', index: g ? Number(g.dataset.i) : null });
  }
  document.addEventListener('mouseover', e => hover(e.target.closest ? e.target.closest('g.bar') : null));
  document.addEventListener('mouseout', e => { if (!e.relatedTarget) hover(null); });

  // Drag the pages to scroll them (owner's request 2026-10-17), the preview's installDragScroll
  // (extension.ts) on the document, since this view scrolls as one: a press becomes a drag only
  // once the mouse has moved SLOP pixels, so a click on a bar or a mark still goes to the music,
  // and the click a drag ends with is swallowed in the capture phase. Only the left button of a
  // mouse with no modifier; a press on the sticky head, a control or a scroll bar is left alone.
  // The pages are images, so their native drag is refused too (dragstart).
  (function installDragScroll() {
    const SLOP = 4;
    const scroller = document.scrollingElement || document.documentElement;
    let press = null, dragged = false;
    document.addEventListener('pointerdown', e => {
      dragged = false;
      press = null;
      if (e.pointerType !== 'mouse' || e.button !== 0 || e.shiftKey || e.ctrlKey || e.metaKey || e.altKey) return;
      if (e.target.closest && e.target.closest('.note, button, input, select, textarea, a')) return;
      const root = document.documentElement;
      if (e.clientX >= root.clientWidth || e.clientY >= root.clientHeight) return;
      press = { id: e.pointerId, x: e.clientX, y: e.clientY, left: scroller.scrollLeft, top: scroller.scrollTop };
    });
    document.addEventListener('pointermove', e => {
      if (press === null || e.pointerId !== press.id) return;
      const dx = e.clientX - press.x, dy = e.clientY - press.y;
      if (!dragged) {
        if (Math.abs(dx) < SLOP && Math.abs(dy) < SLOP) return;
        dragged = true;
        document.body.setPointerCapture(e.pointerId);
        document.body.classList.add('dragging');
      }
      scroller.scrollLeft = press.left - dx;
      scroller.scrollTop = press.top - dy;
    });
    const release = e => {
      if (press === null || e.pointerId !== press.id) return;
      press = null;
      document.body.classList.remove('dragging');
    };
    document.addEventListener('pointerup', release);
    document.addEventListener('pointercancel', e => { release(e); dragged = false; });
    document.addEventListener('click', e => {
      if (!dragged) return;
      dragged = false;
      e.stopImmediatePropagation();
      e.preventDefault();
    }, true);
    document.addEventListener('dragstart', e => e.preventDefault());
  })();

  // Ctrl+wheel zooms the pages, as it zooms the preview's sheet (extension.ts: the same 0.25..4
  // by 0.1; owner's request 2026-10-17). 1 is a page as wide as the pane. The zoom is a CSS
  // variable the pages' width follows (a real width, so the bar and mark geometry the script
  // reads is the drawn one); the page point under the mouse stays under it — the pages start
  // under the head, whose height does not change, so the scroll is scaled from their top and
  // left. Kept per view (state.zoom).
  const zoomInfo = document.getElementById('zoomInfo');
  let zoom = typeof state.zoom === 'number' && state.zoom >= 0.25 && state.zoom <= 4 ? state.zoom : 1;
  let zoomHide = null;
  function applyZoom(show) {
    document.documentElement.style.setProperty('--zoom', String(zoom));
    if (!show) return;
    zoomInfo.textContent = Math.round(zoom * 100) + '%';
    zoomInfo.classList.add('visible');
    clearTimeout(zoomHide);
    zoomHide = setTimeout(() => zoomInfo.classList.remove('visible'), 1500);
  }
  applyZoom(false);
  // To `next`, keeping the page point at (clientX, clientY) under that spot — the wheel's point,
  // or the top-left of the pages for the menu.
  function setZoom(next, clientX, clientY) {
    next = Math.min(4, Math.max(0.25, next));
    if (next === zoom) return;
    const first = document.querySelector('.page');
    const r = first ? first.getBoundingClientRect() : { top: 0, left: 0 };
    const top = r.top + window.scrollY, left = r.left + window.scrollX;
    const x = window.scrollX + clientX, y = window.scrollY + clientY, k = next / zoom;
    zoom = next;
    state.zoom = zoom;
    vscode.setState(state);
    applyZoom(true);
    window.scrollTo(left + (x - left) * k - clientX, top + (y - top) * k - clientY);
  }
  document.addEventListener('wheel', e => {
    if (!e.ctrlKey) return;
    e.preventDefault();
    setZoom(Math.round((zoom + (e.deltaY > 0 ? -0.1 : 0.1)) * 10) / 10, e.clientX, e.clientY);
  }, { passive: false });
  // The context menu's two (package.json menus/webview/context → omrScan.ts postToActiveScan):
  // Fit Width is 100% (a page as wide as the pane); Reset Zoom (1:1) is one pixel of the page as
  // loaded to one on the screen — the page's own width over the pane's, within the 25..400%.
  function menuZoom(type) {
    const head = document.querySelector('.note').getBoundingClientRect().bottom;
    if (type === 'ctxFitWidth') {
      setZoom(1, 0, head);
    } else {
      const img = document.querySelector('.page img'), pane = document.body.clientWidth - 16;
      if (img && img.naturalWidth > 0 && pane > 0) setZoom(img.naturalWidth / pane, 0, head);
    }
  }

  // B4: the bar at the top of the view goes to the preview as the scan scrolls; a scroll
  // the preview asked for (showBar) is not reported back.
  let quietUntil = 0, reportTimer = null;
  // The sticky head covers the top of the window: what counts as "the top" starts under it.
  const note = document.querySelector('.note');
  const topEdge = () => note.getBoundingClientRect().bottom;
  // The bar at the top edge, and how much of it is cut off above the edge (0: its top is at the
  // edge; 0.5: half out of sight; negative: it starts below the edge): the preview puts its copy
  // of the bar at the same cut, so the two panes are cut alike (owner's request 2026-10-17).
  function topBar() {
    let best = null, bestTop = Infinity, bestLeft = Infinity, cut = 0;
    const edge = topEdge();
    for (const r of document.querySelectorAll('g.bar')) {
      const b = r.getBoundingClientRect();
      if (b.bottom <= edge + 4 || b.top >= window.innerHeight || b.height === 0) continue;
      const top = Math.round(Math.max(b.top, edge));
      if (top < bestTop || (top === bestTop && b.left < bestLeft)) {
        best = r; bestTop = top; bestLeft = b.left; cut = (edge - b.top) / b.height;
      }
    }
    return best ? { bar: best, cut } : null;
  }
  // At the very top the view says so instead of naming its first bar, and the preview goes to
  // its own top — title and all — as this view does when the preview is at its top (owner's
  // request 2026-10-17: before, each put the other's first bar at the top edge, over the title).
  window.addEventListener('scroll', () => {
    if (!link.checked || Date.now() < quietUntil) return;
    clearTimeout(reportTimer);
    reportTimer = setTimeout(() => {
      if (window.scrollY <= 0) {
        vscode.postMessage({ type: 'visibleBar', top: true });
        return;
      }
      const t = topBar();
      if (t) vscode.postMessage({ type: 'visibleBar', index: Number(t.bar.dataset.i), cut: t.cut });
    }, 150);
  });

  window.addEventListener('message', ({ data }) => {
    if (data.type === 'ctxFitWidth' || data.type === 'ctxResetZoom') {
      menuZoom(data.type);
      return;
    }
    if (data.type === 'resolved') {
      resolved = new Set(data.keys || []);
      filter();
      return;
    }
    if (data.type === 'peer') {
      for (const g of document.querySelectorAll('g.bar.peer')) g.classList.remove('peer');
      const g = data.index === null ? null : document.querySelector('g.bar[data-i="' + data.index + '"]');
      if (g) g.classList.add('peer');
      return;
    }
    if (data.type === 'showBar') {
      if (data.top) {
        quietUntil = Date.now() + 400;
        window.scrollTo({ top: 0 });
        return;
      }
      const r = document.querySelector('g.bar[data-i="' + data.index + '"]');
      if (!r) return;
      quietUntil = Date.now() + 400;
      // The bar at the top edge, cut off above it as the preview's copy is (data.cut).
      const b = r.getBoundingClientRect();
      window.scrollBy({ top: b.top - topEdge() + (data.cut || 0) * b.height });
      return;
    }
    if (data.type !== 'light') return;
    for (const l of document.querySelectorAll('rect.light')) l.setAttribute('width', '0');
    const t = data.target;
    if (!t) return;
    const page = document.querySelector('.page[data-page="' + t.page + '"]');
    const light = page && page.querySelector('rect.light');
    if (!light) return;
    const [x0, y0, x1, y1] = t.box;
    light.setAttribute('x', x0); light.setAttribute('y', y0);
    light.setAttribute('width', x1 - x0); light.setAttribute('height', y1 - y0);
    // Into view only when it is not: the scan must not jump while the caret walks a line.
    const r = light.getBoundingClientRect(), edge = topEdge();
    if (r.top < edge || r.bottom > window.innerHeight)
      window.scrollBy({ top: r.top - Math.max(edge + 8, window.innerHeight / 3), behavior: 'smooth' });
  });
</script></body></html>`;
}
