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
// the .lys it wrote, with the marks it was unsure of boxed in red. A click on a box goes to the
// music and the caret lights its box: a mark by its key (lilysharp/todos, so it is found
// wherever the mark has moved), a bar by the line the reader wrote it on, carried through the
// edits since (omrScanCore.anchorsOf — the reader's bar numbers are its own, not the page's).
// omrScanCore.ts is the editor-free half; the side file x.omr.json is the reader's and is only
// read.

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import type { LanguageClient } from 'vscode-languageclient/node';
import {
    BarAnchor, ScanSideFile, ScanTarget, ServerTodo, anchorsOf, barOnLine, parseSideFile, shiftAnchors,
    shownImage, sideFileOf, todoTarget,
} from './omrScanCore';

export interface OmrScanDeps {
    readonly output: vscode.OutputChannel;
    /** The language client, started and ready (undefined when it cannot start). */
    readonly client: () => Promise<LanguageClient | undefined>;
}

interface TodosResponse { Todos?: ServerTodo[]; Version: number; Error?: string | null }

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
}

const views = new Map<string, ScanView>();

/** Whether the .lys at `uri` has a side file beside it (the command's when-clause). */
export function hasScan(uri: vscode.Uri | undefined): boolean {
    return !!uri && uri.scheme === 'file' && /\.lys$/i.test(uri.fsPath) && fs.existsSync(sideFileOf(uri.fsPath));
}

/** Wires the caret and the side file's changes to every open view. Call once from activate. */
export function registerScanFollow(context: vscode.ExtensionContext, deps: OmrScanDeps): void {
    let timer: ReturnType<typeof setTimeout> | undefined;
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
                shiftAnchors(view.anchors, e.contentChanges.map(c =>
                    ({ offset: c.rangeOffset, removed: c.rangeLength, inserted: c.text.length })));
            }
        }));
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
    const panel = vscode.window.createWebviewPanel('lilysharpScan', `Scan: ${path.basename(lys.fsPath)}`,
        { viewColumn: column ?? vscode.ViewColumn.Beside, preserveFocus: true },
        { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: rootsOf(side, sidePath) });
    const view: ScanView = { panel, lys, side, anchors: anchorsOf(side, doc.getText()) };
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
            panel.webview.options = { ...panel.webview.options, localResourceRoots: rootsOf(next, sidePath) };
            render(view);
        }
    };
    watcher.onDidChange(reload);
    watcher.onDidCreate(reload);

    panel.webview.onDidReceiveMessage(async (m: { type: string; key?: string; index?: number }) => {
        try {
            if (m.type === 'todo' && m.key) {
                await goToTodo(view, m.key, deps);
            } else if (m.type === 'bar' && Number.isInteger(m.index)) {
                await goToBar(view, m.index!);
            }
        } catch (err) {
            deps.output.appendLine(`Scan: ${err}`);
        }
    });
    panel.onDidDispose(() => {
        watcher.dispose();
        views.delete(lys.toString());
    });

    const editor = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === lys.toString());
    if (editor) {
        void follow(view, editor, deps);
    }
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
    const list = resp.Error ? [] : (resp.Todos ?? []);
    view.todos = { version: doc.version, list };
    return list;
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
    void view.panel.webview.postMessage({ type: 'light', target: target ?? null });
}

async function editorOf(view: ScanView): Promise<vscode.TextEditor> {
    const shown = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === view.lys.toString());
    const doc = shown?.document ?? await vscode.workspace.openTextDocument(view.lys);
    return vscode.window.showTextDocument(doc, { viewColumn: shown?.viewColumn ?? vscode.ViewColumn.One, preserveFocus: false });
}

async function goToTodo(view: ScanView, key: string, deps: OmrScanDeps): Promise<void> {
    const editor = await editorOf(view);
    const mark = (await todosOf(view, editor.document, deps)).find(t => t.Key === key);
    if (!mark) {
        vscode.window.showInformationMessage(`Lily#: the mark @todo(${key}) is no longer in ${path.basename(view.lys.fsPath)}.`);
        return;
    }
    select(editor, mark.HostStart);
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
            return `<section class="page missing"><p>Page ${p.page}: ${esc(path.basename(p.image))} cannot be shown here`
                + `${shown ? ' (the file is not there)' : ' (a webview draws PNG, JPEG, GIF, BMP and WebP)'}.</p></section>`;
        }
        const src = panel.webview.asWebviewUri(vscode.Uri.file(shown)).toString();
        const bars = side.measures.map((m, i) => m.page !== p.page ? '' :
            `<rect class="bar" ${rect(m.box)} data-i="${i}">`
            + `<title>bar ${m.measure}${m.part ? ` (${esc(m.part)})` : ''}</title></rect>`).join('');
        const todos = side.todos.filter(t => t.page === p.page).map(t =>
            `<rect class="todo" ${rect(t.box)} data-key="${esc(t.key)}"><title>${esc(t.memo ?? t.key)}</title></rect>`).join('');
        return `<section class="page" data-page="${p.page}">`
            + `<img src="${src}" alt="page ${p.page}">`
            + `<svg viewBox="0 0 ${p.width} ${p.height}" preserveAspectRatio="none">${bars}${todos}`
            + `<rect class="light" x="0" y="0" width="0" height="0"/></svg></section>`;
    }).join('');

    panel.webview.html = `<!DOCTYPE html>
<html lang="en"><head><meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${panel.webview.cspSource}; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  body { margin: 0; padding: 8px; background: var(--vscode-editor-background); color: var(--vscode-foreground);
         font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); }
  .note { margin: 0 0 8px; opacity: .8; }
  .page { position: relative; margin: 0 auto 12px; max-width: 100%; }
  .page img { display: block; width: 100%; height: auto; background: #fff; }
  .page svg { position: absolute; inset: 0; width: 100%; height: 100%; }
  .page.missing { padding: 12px; border: 1px dashed var(--vscode-editorWidget-border, #888); }
  rect.bar { fill: transparent; stroke: none; cursor: pointer; }
  rect.bar:hover { fill: rgba(0, 120, 215, .08); }
  rect.todo { fill: rgba(220, 0, 0, .08); stroke: #d00; stroke-width: 4; vector-effect: non-scaling-stroke; cursor: pointer; }
  rect.todo:hover { fill: rgba(220, 0, 0, .18); }
  rect.light { fill: rgba(0, 120, 215, .14); stroke: #0078d7; stroke-width: 2; vector-effect: non-scaling-stroke; pointer-events: none; }
</style></head><body>
<p class="note">${esc(path.basename(view.lys.fsPath))} — read from ${side.pages.length === 1 ? 'this page' : `these ${side.pages.length} pages`}. `
        + `Red: marked to check. A click goes to the music; the caret lights its bar here.</p>
${pages || '<p>The side file lists no page.</p>'}
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  document.addEventListener('click', e => {
    const r = e.target.closest('rect');
    if (!r) return;
    if (r.classList.contains('todo')) vscode.postMessage({ type: 'todo', key: r.dataset.key });
    else if (r.classList.contains('bar')) vscode.postMessage({ type: 'bar', index: Number(r.dataset.i) });
  });
  window.addEventListener('message', ({ data }) => {
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
    const r = light.getBoundingClientRect();
    if (r.top < 0 || r.bottom > window.innerHeight)
      window.scrollBy({ top: r.top - window.innerHeight / 3, behavior: 'smooth' });
  });
</script></body></html>`;
}
