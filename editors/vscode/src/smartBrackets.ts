import * as vscode from 'vscode';

// The note token a typed '<' wraps: a pitch letter with its glued accidental
// letters (cis, des, bd …) and octave marks. The duration and dots are NOT part
// of a chord member — they belong AFTER the closing '>' — so they stay outside:
// typing '<' before `c4` yields `<c>4`.
const NOTE_TOKEN = /^[a-g][a-z]*[',]*/;

// The last-seen full text per document, so a DELETION knows which character
// went away (contentChanges carry only the range, not the removed text).
const lastTexts = new Map<string, string>();

// True while one of our own follow-up edits is being applied. The edits are
// designed to be no-ops when re-examined (a promoted close scans back to an
// already-'<<' opener, etc.), but skipping the handler outright guarantees no
// cascade and keeps the work per keystroke minimal.
let applyingFix = false;

/**
 * Smart angle-bracket typing for .lys music — keeps a chord's/arpeggio's two
 * ends in sync as the user edits ONE of them:
 *
 * 1. Typing '<' directly before a note wraps that one note (`c4` → `<c>4`,
 *    durations live after the '>') and puts the caret after the note so typing
 *    continues the chord. A '<' typed in whitespace keeps the plain `<>` pair.
 * 2. '<'  → '<<' : the measure's matching '>'  becomes '>>'.
 * 3. '<<' → '<'  : the measure's matching '>>' becomes '>'.
 * 4. '>'  → '>>' : the measure's matching '<'  becomes '<<'.
 * 5. '>>' → '>'  : the measure's matching '<<' becomes '<'.
 * 6. When the measure AHEAD already holds an unresolved '>', a typed '<' stays
 *    bare — the note wrap is suppressed and an auto-closed `<>` pair has its
 *    '>' removed — so the '<' pairs with the existing close.
 * 7. Typing a lone '>' with NO unresolved '<' before it in the measure
 *    auto-opens: the pitch directly before is wrapped (`c` + '>' → `<c>`),
 *    else an empty `<>` forms. With an unresolved '<' the '>' simply closes
 *    it, and nothing is added.
 *
 * Deleting a lone '<' or '>' deliberately does NOT delete its partner: an
 * orphaned end is how a chord's RANGE is changed (delete the '>', retype it
 * after more notes — rules 6/7 then pair the retyped end with the orphan
 * instead of adding brackets). Only the '<<'⇄'<' / '>>'⇄'>' promotions watch
 * deletions.
 *
 * "Matching" honors nested chord members (`<< c <e g> … >>`) by depth counting
 * and never crosses a measure boundary ('|', '{', '}'). An end that is already
 * consistent is left alone, as is anything that would make a triple bracket.
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
export function registerSmartBrackets(
    context: vscode.ExtensionContext,
    log: (msg: string) => void,
) {
    for (const doc of vscode.workspace.textDocuments) {
        if (doc.languageId === 'lilysharp') { lastTexts.set(doc.uri.toString(), doc.getText()); }
    }
    context.subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(doc => {
            if (doc.languageId === 'lilysharp') { lastTexts.set(doc.uri.toString(), doc.getText()); }
        }),
        vscode.workspace.onDidCloseTextDocument(doc => lastTexts.delete(doc.uri.toString())),
        vscode.workspace.onDidChangeTextDocument(event => {
            if (event.document.languageId !== 'lilysharp') { return; }
            const key = event.document.uri.toString();
            const oldText = lastTexts.get(key);
            const text = event.document.getText();
            lastTexts.set(key, text); // ALWAYS current, even for changes we skip
            if (applyingFix) { return; }
            // Never fight undo/redo — re-fixing would make undo circular.
            if (event.reason === vscode.TextDocumentChangeReason.Undo
                || event.reason === vscode.TextDocumentChangeReason.Redo) { return; }
            // Multi-cursor / multi-range edits are out of scope (offsets shift per edit).
            if (event.contentChanges.length !== 1) { return; }
            const change = event.contentChanges[0];

            const editor = vscode.window.activeTextEditor;
            if (!editor || editor.document !== event.document) { return; }

            // The change's START offset is identical in the old and new text
            // (everything before it is untouched), so one offset serves both.
            const offset = event.document.offsetAt(change.range.start);

            if (change.rangeLength === 0 && change.text === '<') {
                onInsertOpen(editor, text, offset, log);
            } else if (change.rangeLength === 0 && change.text === '<>') {
                onAutoClosePair(editor, text, offset, log);
            } else if (change.rangeLength === 0 && change.text === '>') {
                onInsertClose(editor, text, offset, log);
            } else if (change.rangeLength === 1 && change.text === '' && oldText) {
                const deleted = oldText[offset];
                if (deleted === '<') { onDeleteOpen(editor, text, oldText, offset, log); }
                else if (deleted === '>') { onDeleteClose(editor, text, oldText, offset, log); }
            }
        })
    );
}

/** Applies one follow-up edit, merged into the keystroke's undo step. */
function applyFix(editor: vscode.TextEditor, build: (b: vscode.TextEditorEdit) => void,
    then?: () => void) {
    applyingFix = true;
    editor.edit(build, { undoStopBefore: false, undoStopAfter: true })
        .then(ok => {
            applyingFix = false;
            if (ok && then) { then(); }
        }, () => { applyingFix = false; });
}

/** The measure bounds around `at`: (start, end) offsets between '|'/'{'/'}'. */
function measureBounds(text: string, at: number): [number, number] {
    let start = 0;
    for (let i = at - 1; i >= 0; i--) {
        const c = text[i];
        if (c === '|' || c === '{' || c === '}') { start = i + 1; break; }
    }
    let end = text.length;
    for (let i = at; i < text.length; i++) {
        const c = text[i];
        if (c === '|' || c === '{' || c === '}') { end = i; break; }
    }
    return [start, end];
}

/** True when the measure span [from, end) holds a '>' that no '<' inside the
 * span opens — an unresolved close a just-typed '<' will pair with, so no
 * automatic '>' should be added for it. */
function hasUnresolvedClose(text: string, from: number, end: number): boolean {
    let depth = 0;
    for (let i = from; i < end; i++) {
        const c = text[i];
        if (c === '<') { depth++; }
        else if (c === '>') {
            if (depth === 0) { return true; }
            depth--;
        }
    }
    return false;
}

/** The offset of the matching close ('>') for an opener whose body starts at
 * `from`, skipping nested '<…>' members by depth; -1 when the measure has none. */
function findClose(text: string, from: number, end: number): number {
    let depth = 0;
    for (let i = from; i < end; i++) {
        const c = text[i];
        if (c === '<') { depth++; }
        else if (c === '>') {
            if (depth > 0) { depth--; }
            else { return i; }
        }
    }
    return -1;
}

/** The offset of the matching opener ('<') for a close whose body ends at
 * `from` (exclusive), skipping nested members; -1 when the measure has none. */
function findOpen(text: string, from: number, start: number): number {
    let depth = 0;
    for (let i = from - 1; i >= start; i--) {
        const c = text[i];
        if (c === '>') { depth++; }
        else if (c === '<') {
            if (depth > 0) { depth--; }
            else { return i; }
        }
    }
    return -1;
}

/** '<' typed: wrap the following note, or — against an existing '<' — promote
 * the measure's matching '>' to '>>'. */
function onInsertOpen(editor: vscode.TextEditor, text: string, offset: number,
    log: (msg: string) => void) {
    const after = offset + 1;

    if (text[after] === '<' || text[offset - 1] === '<') {
        // A stray third '<' — leave it alone.
        if (text[offset - 1] === '<' && text[offset - 2] === '<') { return; }
        if (text[after] === '<' && text[after + 1] === '<') { return; }
        // Scan from past the pair, so the pair's own chars don't count as nesting.
        const bodyStart = text[after] === '<' ? after + 1 : after;
        const [, end] = measureBounds(text, bodyStart);
        const close = findClose(text, bodyStart, end);
        if (close < 0 || text[close + 1] === '>') { return; } // unclosed, or already '>>'
        applyFix(editor, b => b.insert(editor.document.positionAt(close + 1), '>'));
        log('smartBrackets: < doubled -> promoted the matching > to >>');
        return;
    }

    // An unresolved '>' later in the measure means the user is re-opening an
    // existing chord — the bare '<' pairs with it, so add nothing.
    const [, mEnd] = measureBounds(text, after);
    if (hasUnresolvedClose(text, after, mEnd)) { return; }

    // '<' typed directly before a note: wrap that ONE note.
    const m = NOTE_TOKEN.exec(text.slice(after));
    if (!m) { return; }
    const noteEnd = after + m[0].length;
    // A '>' already there means the note IS wrapped (a re-typed '<' in front
    // of `c>`); inserting another would double-close it.
    if (text[noteEnd] === '>') { return; }
    applyFix(editor, b => b.insert(editor.document.positionAt(noteEnd), '>'), () => {
        // Caret between the note and the '>': typing ' e g' continues the chord.
        const caret = editor.document.positionAt(noteEnd);
        editor.selection = new vscode.Selection(caret, caret);
    });
    log(`smartBrackets: wrapped note -> <${m[0]}>`);
}

/** VS Code auto-closed a typed '<' into '<>'. When the measure ahead already
 * holds an unresolved '>', the user is re-opening an existing chord — keep just
 * the '<' and drop the auto-inserted '>' so the old close pairs with it. */
function onAutoClosePair(editor: vscode.TextEditor, text: string, offset: number,
    log: (msg: string) => void) {
    const closeAt = offset + 1; // the auto-inserted '>'
    const [, end] = measureBounds(text, closeAt + 1);
    if (!hasUnresolvedClose(text, closeAt + 1, end)) { return; }
    applyFix(editor, b => b.delete(new vscode.Range(
        editor.document.positionAt(closeAt), editor.document.positionAt(closeAt + 1))));
    log('smartBrackets: unresolved > ahead -> dropped the auto-closed >');
}

/** One '<' of a '<<' deleted: demote the measure's matching '>>' to '>'. A
 * LONE '<' deleted is deliberately left alone — the orphaned '>' is how the
 * chord's range gets re-drawn (see the class comment). */
function onDeleteOpen(editor: vscode.TextEditor, text: string, oldText: string,
    offset: number, log: (msg: string) => void) {
    const wasPair = oldText[offset + 1] === '<' || oldText[offset - 1] === '<';
    if (!wasPair) { return; }
    // The OLD text must have held exactly '<<' here (not '<<<').
    if (oldText[offset - 2] === '<'
        || (oldText[offset - 1] === '<' && oldText[offset + 1] === '<')
        || oldText[offset + 2] === '<') { return; }
    // The remaining '<' sits at `offset` (the deletion closed the gap) or one
    // before it when the SECOND '<' of the pair was the deleted one.
    const remaining = text[offset] === '<' ? offset : offset - 1;
    if (text[remaining] !== '<') { return; }
    const [, end] = measureBounds(text, remaining + 1);
    const close = findClose(text, remaining + 1, end);
    if (close < 0 || text[close + 1] !== '>') { return; } // no close, or already single
    applyFix(editor, b => b.delete(new vscode.Range(
        editor.document.positionAt(close), editor.document.positionAt(close + 1))));
    log('smartBrackets: << reduced -> demoted the matching >> to >');
}

/** '>' typed. Against an existing '>' it promotes the measure's matching '<'
 * to '<<'. A lone '>' closing an unresolved '<' is left alone; a lone '>' with
 * nothing to close auto-opens — wrapping the pitch directly before it, or
 * forming an empty `<>`. */
function onInsertClose(editor: vscode.TextEditor, text: string, offset: number,
    log: (msg: string) => void) {
    const after = offset + 1;
    if (text[after] === '>' || text[offset - 1] === '>') {
        // A stray third '>' — leave it alone.
        if (text[offset - 1] === '>' && text[offset - 2] === '>') { return; }
        if (text[after] === '>' && text[after + 1] === '>') { return; }
        // Scan from before the pair, so the pair's own chars don't count as nesting.
        const pairStart = text[offset - 1] === '>' ? offset - 1 : offset;
        const [pStart] = measureBounds(text, pairStart);
        const open = findOpen(text, pairStart, pStart);
        if (open < 0 || text[open - 1] === '<') { return; } // unopened, or already '<<'
        applyFix(editor, b => b.insert(editor.document.positionAt(open), '<'));
        log('smartBrackets: > doubled -> promoted the matching < to <<');
        return;
    }
    // Lone '>': with an unresolved '<' before it, it IS that chord's close.
    const [start] = measureBounds(text, offset);
    if (findOpen(text, offset, start) >= 0) { return; }
    // Otherwise auto-open: wrap the pitch token ending right at the '>' —
    // `c` + '>' -> `<c>` — or, with no note there, form an empty `<>`. The
    // boundary check keeps a word's tail (`time`) from posing as a pitch.
    const m = /(?:^|[^a-z])([a-g][a-z]*[',]*)$/.exec(text.slice(start, offset));
    const openAt = m ? offset - m[1].length : offset;
    applyFix(editor, b => b.insert(editor.document.positionAt(openAt), '<'));
    log(m ? `smartBrackets: > typed -> wrapped <${m[1]}>`
          : 'smartBrackets: > typed -> auto-opened <>');
}

/** One '>' of a '>>' deleted: demote the measure's matching '<<' to '<'. A
 * LONE '>' deleted is deliberately left alone — the orphaned '<' is how the
 * chord's range gets re-drawn (see the class comment). */
function onDeleteClose(editor: vscode.TextEditor, text: string, oldText: string,
    offset: number, log: (msg: string) => void) {
    const wasPair = oldText[offset + 1] === '>' || oldText[offset - 1] === '>';
    if (!wasPair) { return; }
    // The OLD text must have held exactly '>>' here (not '>>>').
    if (oldText[offset - 2] === '>'
        || (oldText[offset - 1] === '>' && oldText[offset + 1] === '>')
        || oldText[offset + 2] === '>') { return; }
    const remaining = text[offset] === '>' ? offset : offset - 1;
    if (text[remaining] !== '>') { return; }
    const [start] = measureBounds(text, remaining);
    const open = findOpen(text, remaining, start);
    if (open < 0 || text[open - 1] !== '<') { return; } // no opener, or already single
    applyFix(editor, b => b.delete(new vscode.Range(
        editor.document.positionAt(open - 1), editor.document.positionAt(open))));
    log('smartBrackets: >> reduced -> demoted the matching << to <');
}
