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

import * as vscode from 'vscode';
import {
    Edit, FixPlan, TypePlan, afterKeystrokeEdit, composeFix, deletionPlan, insertionPlan,
    isPlannedKey, isSmartInsert, planFor, typedKeyOutcome,
} from './smartTypingCore';

// The CARRYING-OUT half of smart typing. What a keystroke does — the rules,
// numbered 1–29, and every reading of the text they rest on — lives in
// smartTypingCore.ts, which never sees an editor and is tested under node.
// This file is the VS Code side of it: it hears the keystrokes, reconstructs
// the text the core wants to decide on, and applies the plan it gets back as
// one edit with the caret placed in the same operation.

// contentChanges carry the range of a deletion but not the text that went away,
// so something has to be remembered from BEFORE the change. The only datum that
// cannot be recovered afterwards is the deleted CHARACTER itself: a one-character
// deletion at `offset` leaves old[i] == new[i] below it and old[i] == new[i-1]
// above it, so the rest of the old text is derivable. Remembering a window
// around the caret is therefore enough, and it replaces what used to be a full
// copy of the document rebuilt on every keystroke. 8 characters is slack: the
// next deletion is at the caret, ±1 for Backspace vs Delete.
const WINDOW = 8;
const windows = new Map<string, { base: number, text: string }>();

/** Remembers the text around `at` as the document stands NOW, for the next
 * change to read. Called after every change and on every caret move, so the
 * window can only be missing, never stale. */
function cacheWindow(doc: vscode.TextDocument, at: number) {
    const base = Math.max(0, at - WINDOW);
    windows.set(doc.uri.toString(), {
        base,
        text: doc.getText(new vscode.Range(doc.positionAt(base), doc.positionAt(at + WINDOW))),
    });
}

/** The character that a just-applied deletion at `offset` removed, or undefined
 * when the window does not reach it — in which case the promotion this feeds is
 * skipped rather than guessed at. */
function deletedChar(key: string, offset: number): string | undefined {
    const w = windows.get(key);
    if (!w) { return undefined; }
    const i = offset - w.base;
    return i >= 0 && i < w.text.length ? w.text[i] : undefined;
}

// True while one of our own follow-up edits is being applied. The edits are
// designed to be no-ops when re-examined (a promoted close scans back to an
// already-'<<' opener, etc.), but skipping the handler outright guarantees no
// cascade and keeps the work per keystroke minimal.
let applyingFix = false;

/**
 * Smart typing for .lys music: chord brackets that keep their two ends in
 * sync, slur, beam and tie marks that land on the note they belong to, octave
 * marks, durations, dots, string numbers and annotations that travel into
 * their slot on the note wherever on it they were typed. The rules are the
 * numbered list at the top of smartTypingCore.ts.
 *
 * Two routes lead into the core. The keys whose character is RELOCATED are
 * intercepted before VS Code inserts anything (registerSmartTypeKeys), which is
 * the only way the caret can be made not to move at all; everything else — and
 * an intercepted key whose binding did not fire — arrives through
 * onDidChangeTextDocument with the character already in the document, and is
 * put right one round trip later.
 *
 * Cost: nothing here reads the document until a change is known to be one of
 * ours, so an ordinary letter, a space, a paste and an unrelated edit all leave
 * after two comparisons. What is remembered between changes is a window around
 * the caret, not the document — see WINDOW.
 *
 * Rendering: the follow-up edit lands within the preview's debounce window and
 * resets its timer, so the preview re-renders ONCE, from the final text — the
 * user's keystroke and the auto-fix never trigger two renders (and the
 * intermediate, mismatched text is never rendered).
 *
 * Undo: every follow-up edit merges into the keystroke's undo step
 * (undoStopBefore: false), so one undo reverts both ends together; the handler
 * itself never runs on undo/redo.
 */
export function registerSmartTyping(
    context: vscode.ExtensionContext,
    log: (msg: string) => void,
) {
    for (const doc of vscode.workspace.textDocuments) {
        if (doc.languageId === 'lilysharp') { cacheWindow(doc, 0); }
    }
    context.subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(doc => {
            if (doc.languageId === 'lilysharp') { cacheWindow(doc, 0); }
        }),
        vscode.workspace.onDidCloseTextDocument(doc => windows.delete(doc.uri.toString())),
        // A caret move is the other way the window goes out of date: the next
        // deletion happens wherever the user just clicked.
        vscode.window.onDidChangeTextEditorSelection(e => {
            const doc = e.textEditor.document;
            if (doc.languageId === 'lilysharp') {
                cacheWindow(doc, doc.offsetAt(e.selections[0].active));
            }
        }),
        vscode.workspace.onDidChangeTextDocument(event => {
            if (event.document.languageId !== 'lilysharp') { return; }
            const key = event.document.uri.toString();
            // Multi-cursor / multi-range edits are out of scope (offsets shift per edit).
            const change = event.contentChanges.length === 1 ? event.contentChanges[0] : undefined;
            // The change's START offset is identical in the old and new text
            // (everything before it is untouched), so one offset serves both.
            const offset = change ? event.document.offsetAt(change.range.start) : 0;
            // Read the OLD window, then re-point it at the document as it stands
            // now — on every path, including the ones that bail out below, so the
            // next change always finds it current.
            const deleted = change && change.rangeLength === 1 && change.text === ''
                ? deletedChar(key, offset) : undefined;
            if (change) { cacheWindow(event.document, offset); }

            if (applyingFix || !change) { return; }
            // Never fight undo/redo — re-fixing would make undo circular.
            if (event.reason === vscode.TextDocumentChangeReason.Undo
                || event.reason === vscode.TextDocumentChangeReason.Redo) { return; }

            const editor = vscode.window.activeTextEditor;
            if (!editor || editor.document !== event.document) { return; }

            // Only a matched change needs the document text, so the full read
            // stays off the path a paste or an unrelated keystroke takes.
            if (change.rangeLength === 1 && change.text === '') {
                if (deleted !== '<' && deleted !== '>') { return; }
                const text = event.document.getText();
                // The pre-change text, exactly: a one-character deletion is undone
                // by putting that character back where it was.
                const oldText = text.slice(0, offset) + deleted + text.slice(offset);
                carryOut(editor, text, deletionPlan(deleted, text, oldText, offset), log);
                return;
            }
            if (change.rangeLength !== 0 || !isSmartInsert(change.text)) { return; }
            const text = event.document.getText();
            const typed = change.text;

            if (isPlannedKey(typed)) {
                // The pre-keystroke text, which is what the plan is decided on.
                const before = text.slice(0, offset) + text.slice(offset + 1);
                const plan = planFor(typed, before, offset);
                if (!plan) { return; }
                // '@' is not intercepted (rule 27): it has no layout-safe key — on
                // a JIS keyboard it is its own key, on a US one Shift+2 — so it
                // always comes through here. The '@' is a completion trigger and
                // the suggestions VS Code opened were asked for at the OLD
                // position, so once the mark has moved the popup is asked for
                // again where it is.
                const after = typed === '@'
                    ? () => { void vscode.commands.executeCommand('editor.action.triggerSuggest'); }
                    : undefined;
                applyPlanAfterKeystroke(editor, text, offset, plan, log, typed, after);
                return;
            }
            carryOut(editor, text, insertionPlan(typed, text, offset), log);
        })
    );
    registerSmartTypeKeys(context, log);
}

/**
 * Handles the keys whose character this module RELOCATES, before VS Code inserts
 * anything — the only way the caret can be made not to move at all.
 *
 * The onDidChangeTextDocument route above cannot do it. By the time a change
 * event reaches the extension host, VS Code has already inserted the character at
 * the cursor and advanced the cursor past it, in the renderer; the rewrite that
 * puts both right is a round trip behind, and that gap is visible as the cursor
 * flicking forward and back. Bound to a command instead, the key never reaches
 * the default insertion: the character is written straight into its slot and the
 * cursor is simply never asked to move.
 *
 * Everything this module does NOT relocate is delegated to the built-in `type`,
 * so ordinary typing — auto-closing pairs, suggestions, everything — is untouched
 * in every case that reaches it. That is most of them: a digit inside `time 4/4`
 * or a title string, an apostrophe anywhere but on a note.
 *
 * ⚠️ IME: keys the input method is processing arrive with keyCode 229 and VS Code
 * does not dispatch keybindings for them, so composing Japanese in a title,
 * lyric or `@text` never reaches this command — the composed text arrives through
 * the composition path instead. The bindings are further held off while the
 * suggestion widget is up, where a keystroke means "filter", not "insert".
 *
 * ⚠️ A binding that does not fire is not a failure: the change-event route is
 * still there and still relocates the character, with the flicker. That is the
 * degradation if a keyboard layout does not resolve one of these keys.
 */
function registerSmartTypeKeys(context: vscode.ExtensionContext, log: (msg: string) => void) {
    context.subscriptions.push(vscode.commands.registerCommand(
        'lilysharp.smartType', async (args?: { text?: string }) => {
            const ch = args?.text;
            const editor = vscode.window.activeTextEditor;
            const plain = () => vscode.commands.executeCommand('type', { text: ch });
            if (typeof ch !== 'string' || ch.length !== 1) { return plain(); }
            // Anything the plan cannot speak for goes to the default insertion:
            // another language, a selection to overtype, several cursors.
            if (!editor || editor.document.languageId !== 'lilysharp'
                || editor.selections.length !== 1 || !editor.selection.isEmpty) {
                return plain();
            }
            const before = editor.document.getText();
            const offset = editor.document.offsetAt(editor.selection.active);
            const plan = planFor(ch, before, offset);
            if (!plan) { return plain(); }
            applyPlanForTypedKey(editor, before, offset, plan, log, ch);
        }));
    log('smartTyping: key interception registered for \' , . \\ and 0-9');
}

/** Applies a follow-up rewrite AND places the caret in ONE operation.
 *
 * editor.edit() reports completion on the extension host's queue, which can be a
 * visible moment after the text itself lands -- long enough to watch the caret
 * sit at the old position and then jump on its own. Anything scheduled off that
 * promise inherits the wait, so the caret must not be scheduled off it: a
 * snippet's tabstop travels WITH the text, in the same edit, and there is no
 * in-between state to see.
 *
 * `edits` and `caret` are offsets into the document as it stands NOW, except
 * that `caret` is where the caret should end up in the FINISHED text; composeFix
 * turns them into the one span to replace and the tabstop's place inside it. */
function applyFixWithCaret(editor: vscode.TextEditor, text: string,
    edits: Edit[], caret: number,
    ownUndoStep = false, selectLen = 0, after?: () => void) {
    const fix = composeFix(text, edits, caret, selectLen);
    const { lo, hi, out, caretIn } = fix;
    selectLen = fix.selectLen;
    const snippet = new vscode.SnippetString();
    snippet.appendText(out.slice(0, caretIn));
    // BRACED, and appendTabstop() is not usable here: it writes a bare `$0`, and
    // the parser reads a tabstop's number greedily — so `$0` in front of the `2`
    // of `c,2` is read as tabstop 02 and eats the digit. Every caret this helper
    // places sits next to a duration or an octave mark, which is exactly where a
    // digit follows. The text either side is still escaped by appendText().
    // With `selectLen` the tabstop is a PLACEHOLDER over the next characters,
    // which the snippet leaves SELECTED — how a string number is offered for
    // retyping (rule 24a). The placeholder's text is escaped by hand, as
    // appendPlaceholder() would do; it is only ever a digit here.
    snippet.value += selectLen > 0
        ? '${0:' + out.slice(caretIn, caretIn + selectLen).replace(/[$}\\]/g, '\\$&') + '}'
        : '${0}';
    snippet.appendText(out.slice(caretIn + selectLen));
    applyingFix = true;
    // A follow-up to a keystroke merges into that keystroke's undo step. An
    // INTERCEPTED key has no keystroke edit to merge with, so it opens its own —
    // without this it would join whatever the user did before, and one undo would
    // take that away too.
    editor.insertSnippet(snippet,
        new vscode.Range(editor.document.positionAt(lo), editor.document.positionAt(hi)),
        { undoStopBefore: ownUndoStep, undoStopAfter: true })
        .then(() => { applyingFix = false; after?.(); }, () => { applyingFix = false; });
}

/** Carries out a plan on a document that ALREADY holds the keystroke — the
 * fallback route, used when the key was not intercepted. The typed character is
 * removed and the plan's span rewritten in ONE replacement (afterKeystrokeEdit).
 *
 * ⚠️ THE CARET IS STILL SEEN TO MOVE ON THIS ROUTE, once, for one extension-host
 * round trip: VS Code inserted the character at the cursor and advanced it before
 * this module was told anything, and no edit made afterwards can un-paint that.
 * Interception is what removes it — see registerSmartTypeKeys. */
function applyPlanAfterKeystroke(editor: vscode.TextEditor, text: string, offset: number,
    plan: TypePlan, log: (msg: string) => void, typed: string, after?: () => void) {
    applyFixWithCaret(editor, text, [afterKeystrokeEdit(text, offset, plan)], plan.caret,
        false, plan.select ?? 0, after);
    log(`smartTyping: ${typed} typed -> ${plan.what}`);
}

/** Carries out a plan on a document that does NOT hold the keystroke — the
 * intercepted route. Nothing was inserted at the cursor, so there is no keystroke
 * to take back out and no moment at which the caret sat anywhere else. */
function applyPlanForTypedKey(editor: vscode.TextEditor, before: string, offset: number,
    plan: TypePlan, log: (msg: string) => void, typed: string) {
    const outcome = typedKeyOutcome(before, offset, plan);
    if (outcome.kind === 'select') {
        // Nothing to write: the keystroke only moves the selection, which
        // is no edit and so opens no undo step of its own.
        const doc = editor.document;
        editor.selection = new vscode.Selection(
            doc.positionAt(outcome.caret), doc.positionAt(outcome.caret + outcome.select));
        log(`smartTyping: ${typed} typed -> ${plan.what}`);
        return;
    }
    if (outcome.kind === 'absorbed') {
        log(`smartTyping: ${typed} typed -> ${plan.what} (absorbed, nothing to change)`);
        return;
    }
    applyFixWithCaret(editor, before, outcome.edits, outcome.caret, true, outcome.select);
    log(`smartTyping: ${typed} typed -> ${plan.what}`);
}

/** Carries out a plan decided on the text WITH the keystroke in it — the chord
 * brackets, the slur and beam marks, the tie. A plan that names a caret is
 * applied as one snippet with the caret placed in the same operation; one that
 * does not is a plain edit, merged into the keystroke's undo step, and the caret
 * stays wherever VS Code left it. */
function carryOut(editor: vscode.TextEditor, text: string, plan: FixPlan | null,
    log: (msg: string) => void) {
    if (!plan) { return; }
    if (plan.caret !== undefined) {
        applyFixWithCaret(editor, text, plan.edits, plan.caret);
    } else {
        applyFix(editor, b => {
            for (const e of plan.edits) {
                if (e.ins) { b.insert(editor.document.positionAt(e.at), e.ins); }
                if (e.del) {
                    b.delete(new vscode.Range(
                        editor.document.positionAt(e.at), editor.document.positionAt(e.at + e.del)));
                }
            }
        });
    }
    log(`smartTyping: ${plan.what}`);
}

/** Applies one follow-up edit, merged into the keystroke's undo step. */
function applyFix(editor: vscode.TextEditor, build: (b: vscode.TextEditorEdit) => void) {
    applyingFix = true;
    editor.edit(build, { undoStopBefore: false, undoStopAfter: true })
        .then(() => { applyingFix = false; }, () => { applyingFix = false; });
}
