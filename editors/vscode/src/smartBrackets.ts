import * as vscode from 'vscode';

// The note token a typed '<' wraps: a pitch letter with its glued accidental
// letters (cis, des, bd …) and octave marks. The duration and dots are NOT part
// of a chord member — they belong AFTER the closing '>' — so they stay outside:
// typing '<' before `c4` yields `<c>4`.
const NOTE_TOKEN = /^[a-g][a-z]*[',]*/;

// A complete note EVENT as written: a pitch (letter + accidental spelling +
// octave marks) or a rest, plus the duration and dots glued to it. Anchored, and
// every caller checks that a word character does NOT follow, so `clef`, `bass`
// and `rightHand` can never pose as notes.
const NOTE_EVENT = /^(?:[a-g](?:isis|eses|is|es)?[',]*|[rsR])(?:[0-9]+\.*)?/;

// Characters that sit BETWEEN note events — barlines and repeat marks, beam and
// volta brackets, ties, other slur marks, and the bare numbers of a volta or a
// `:|*N` repeat count. A slur spans all of them.
const BETWEEN_EVENTS = /[|:.*[\]~()\-0-9]/;

// The pitch letters an octave mark attaches to: the note name with its glued
// accidental spelling. The marks go after these and BEFORE the duration.
const PITCH_LETTERS = /^[a-g](?:isis|eses|is|es)?/;

// Every DurationBase there is (GRAMMAR.md §Duration). Tested as a PREFIX, never
// as a member: '3' is not a duration but it is the first keystroke of '32', and
// '12' is the first two of '128'. Dots and a ':' tremolo are separate slots.
const DURATIONS = ['1', '2', '4', '8', '16', '32', '64', '128'];
const isDurationPrefix = (digits: string) => DURATIONS.some(d => d.startsWith(digits));

// A mid-music command, which changes context between two notes without being one
// — a slur spans it, so the scan for the following note skips the whole command
// (its operands included, or `key g major` would offer 'g' as the note).
const MID_MUSIC_COMMAND =
    /^(?:clef\s+[A-Za-z_][A-Za-z0-9_]*|key\s+[a-g](?:isis|eses|is|es)?\s+[A-Za-z]+|time\s+[0-9]+\s*\/\s*[0-9]+|partial\s+[0-9]+\.*|octave\s+[A-Za-z0-9]+|break)(?![A-Za-z0-9_])/;

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

// The insertions this module reacts to — the pairs VS Code auto-closes included.
// Checked BEFORE the document text is read, so an ordinary letter, a space or a
// paste never pays for a full getText().
const SMART_INSERTS = new Set(['<', '<>', '>', '(', '()', ')', "'", ',', '~', '[', '[]', ']']);

// The durations that carry a flag, and so can be beamed. A note that spells no
// duration inherits the running one — `c8 d e f` beams all four and only the
// first says '8' — so a beam run can only be read with the measure's running
// value carried along.
const BEAMABLE = new Set([8, 16, 32, 64, 128]);
const isSmartInsert = (t: string) => SMART_INSERTS.has(t) || /^[0-9]$/.test(t);

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
 * Smart slur typing, on the same principle — a typed '(' reaches for what the
 * slur will actually cover instead of closing on itself:
 *
 * 8. Typing '(' after a note puts the ')' after the FOLLOWING note event
 *    (`c4( d e` → `c4( d) e`), so the shortest real slur is one keystroke and
 *    widening it means dragging one ')' forward. Which note the slur STARTS on
 *    is read off the caret: a '(' glued to the note AHEAD of it points at that
 *    note and moves just past it (`c |d e` → `c d( e)`), while one glued to the
 *    note behind — or spaced from both — stays put (`c| d e` → `c( d) e`). The
 *    pair always covers two notes; the caret picks which two.
 * 9. When an unresolved ')' already lies ahead, the '(' stays bare — it pairs
 *    with that existing end (an auto-closed `()` has its ')' removed), which is
 *    how a slur's range is WIDENED: retype the '(' earlier.
 * 10. With no note event ahead — end of the block, or anything the scan cannot
 *    positively identify as a note — the plain `()` pair is left as typed.
 * 11. Typing ')' mirrors it: the '(' goes after the note BEFORE the one the ')'
 *    closes on (`c4 d` + ')' → `c4( d)`), so the shortest automatic slur spans
 *    two notes whichever end is typed first.
 * 12. An unresolved '(' before the typed ')' means it simply closes that, and
 *    nothing is added — which is also what keeps `@finger(3)` intact.
 * 13. When a slur already ends on that preceding note, the typed ')' EXTENDS it
 *    by one note (`c4( d) e` + ')' → `c4( d e)`) instead of opening a second
 *    one. A slur that ended further back is left alone and a new slur starts,
 *    so both readings stay reachable: ')' extends, '(' begins.
 *
 * 19. A tie is the same mark-after-its-note shape with no second note to find,
 *    so '~' typed anywhere ON a note moves to that note's end (`|c2` + '~' →
 *    `c2~`). Which note it ties TO is whatever follows, and whether the pitch
 *    repeats is the compiler's business (LYS4007), not the editor's.
 *
 * 20. A manual beam opens the same way and closes on MUSIC rather than on a
 *    count: '[' typed on a note runs to the last note in the SAME MEASURE that
 *    can still be beamed (`c8|` + '[' → `c8[ d e f]`), because a beam cannot
 *    cross a barline and cannot hold a quarter, a longer note or a rest. The
 *    run is read with the running duration carried along, so the `d e f` of
 *    `c8 d e f` count as eighths even though only the first says so. A rest is
 *    SPANNED (`c8[ r8 d]`) but never ends a beam.
 * 21. ']' mirrors it backwards: it reaches for the first note of the beamable
 *    run that ends where it was typed, and puts the '[' there.
 * 22. A note that cannot carry a beam, or one with nothing beamable beside it,
 *    is left as typed — as is a '[' that is not on a note, which is what an
 *    inline volta's `[1.` is.
 *
 * Smart octave marks, on the same reading of the caret:
 *
 * 14. "'" or ',' typed at either END of a note (`|c4`, `c4|`) moves into the
 *    slot the pitch actually takes it in — between the pitch letters and the
 *    duration (`c'4`), or after a chord's '>'. Typed where it already belongs
 *    it is simply left alone.
 * 15. Typed against the OPPOSITE mark it CANCELS one of them ("'" undoes a ',',
 *    ',' undoes a "'"), so an overshot octave is walked back with the same key
 *    that caused it.
 *
 * Smart durations, read the same way:
 *
 * 16. A digit typed anywhere on a note moves into the duration slot, which sits
 *    AFTER the octave marks (`|c` and `c,|` alike → `c,4`).
 * 17. Digits already there are EXTENDED while the result is still a duration in
 *    the making — `c1` + '6' → `c16` from ANY caret position on the note, since
 *    `c61` is not a duration and `c16` is. The test is a PREFIX test against
 *    1/2/4/8/16/32/64/128: '3' is no duration but it is the first keystroke of
 *    '32', so it has to be accepted to be extended.
 * 18. When the digits cannot become one they are REPLACED (`c4` + '2' → `c2`:
 *    neither `c42` nor `c24` exists). Retyping the SAME digit (`c1` + '1') is
 *    that case with nothing to change, so only the caret moves — to just after
 *    the digits, where a following '6' or '28' continues it. A digit that starts
 *    no duration at all (5, 7, 9, 0) is left exactly as typed.
 *
 * A caret INSIDE a chord is pointing at a member (`<c, e g>`, `<c 3 5>` — the
 * spaced digits are scale degrees), and one past the note's core is in its
 * annotations (`@fig(6,4)`); both are left as typed.
 *
 * The search for both the following note and the unresolved ')' is bounded by
 * the innermost `{ … }` block, so an auto-placed ')' never leaves the part (or
 * voice) the '(' was typed in, let alone its section. Unlike chords, a slur DOES
 * cross barlines, so measure bounds are not used. A '(' glued to a name is an
 * annotation's argument list (`@finger(3)`) or a phrase reference's interval
 * (`Melody'(3)`), never a slur, and is left alone — as is one inside a string or
 * a comment.
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
export function registerSmartBrackets(
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
                if (deleted === '<') { onDeleteOpen(editor, text, oldText, offset, log); }
                else { onDeleteClose(editor, text, oldText, offset, log); }
                return;
            }
            if (change.rangeLength !== 0 || !isSmartInsert(change.text)) { return; }
            const text = event.document.getText();

            if (change.text === '<') {
                onInsertOpen(editor, text, offset, log);
            } else if (change.text === '<>') {
                onAutoClosePair(editor, text, offset, log);
            } else if (change.text === '>') {
                onInsertClose(editor, text, offset, log);
            } else if (change.text === '(') {
                onInsertSlurOpen(editor, text, offset, false, log);
            } else if (change.text === '()') {
                onInsertSlurOpen(editor, text, offset, true, log);
            } else if (change.text === ')') {
                onInsertSlurClose(editor, text, offset, log);
            } else if (change.text === '~') {
                onInsertTie(editor, text, offset, log);
            } else if (change.text === '[') {
                onInsertBeamOpen(editor, text, offset, false, log);
            } else if (change.text === '[]') {
                onInsertBeamOpen(editor, text, offset, true, log);
            } else if (change.text === ']') {
                onInsertBeamClose(editor, text, offset, log);
            } else if (change.text === "'" || change.text === ',') {
                onInsertOctaveMark(editor, text, offset, change.text, log);
            } else if (/^[0-9]$/.test(change.text)) {
                onInsertDuration(editor, text, offset, change.text, log);
            }
        })
    );
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
 * that `caret` is where the caret should end up in the FINISHED text. Every
 * position below `lo` is untouched by definition, so the two agree there and
 * the tabstop can be placed by simple subtraction. */
function applyFixWithCaret(editor: vscode.TextEditor, text: string,
    edits: { at: number, del?: number, ins?: string }[], caret: number) {
    let lo = Infinity;
    let hi = -Infinity;
    for (const e of edits) {
        lo = Math.min(lo, e.at);
        hi = Math.max(hi, e.at + (e.del ?? 0));
    }
    // The span [lo, hi) rebuilt with every edit applied — one replacement, so
    // the edits can never half-land or overlap. Where a delete and an insert
    // share an offset — which is every keystroke replaced in place — the INSERT
    // goes first: taking the deletion first would step the cursor past the text
    // the insert then copies again, emitting it twice.
    const ordered = [...edits].sort((a, b) =>
        a.at - b.at || (a.ins ? 0 : 1) - (b.ins ? 0 : 1));
    let out = '';
    let i = lo;
    for (const e of ordered) {
        out += text.slice(i, e.at);
        if (e.ins) { out += e.ins; }
        i = Math.max(i, e.at + (e.del ?? 0));
    }
    out += text.slice(i, hi);

    const caretIn = Math.max(0, Math.min(out.length, caret - lo));
    const snippet = new vscode.SnippetString();
    snippet.appendText(out.slice(0, caretIn));
    // BRACED, and appendTabstop() is not usable here: it writes a bare `$0`, and
    // the parser reads a tabstop's number greedily — so `$0` in front of the `2`
    // of `c,2` is read as tabstop 02 and eats the digit. Every caret this helper
    // places sits next to a duration or an octave mark, which is exactly where a
    // digit follows. The text either side is still escaped by appendText().
    snippet.value += '${0}';
    snippet.appendText(out.slice(caretIn));
    applyingFix = true;
    editor.insertSnippet(snippet,
        new vscode.Range(editor.document.positionAt(lo), editor.document.positionAt(hi)),
        { undoStopBefore: false, undoStopAfter: true })
        .then(() => { applyingFix = false; }, () => { applyingFix = false; });
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

/** The innermost `{ … }` block around `at`, as (start, end) offsets. A slur
 * closes inside the part/voice block it opened in — which is nested in the
 * section, so the bound is at least as tight as "the same section" everywhere. */
function blockBounds(text: string, at: number): [number, number] {
    let start = 0;
    for (let i = at - 1, depth = 0; i >= 0; i--) {
        const c = text[i];
        if (c === '}') { depth++; }
        else if (c === '{') {
            if (depth === 0) { start = i + 1; break; }
            depth--;
        }
    }
    let end = text.length;
    for (let i = at, depth = 0; i < text.length; i++) {
        const c = text[i];
        if (c === '{') { depth++; }
        else if (c === '}') {
            if (depth === 0) { end = i; break; }
            depth--;
        }
    }
    return [start, end];
}

/** True when [from, end) holds a ')' that no '(' inside the span opens — the
 * existing slur end a just-typed '(' pairs with. An annotation's `(args)` is
 * balanced, so it neither hides nor fakes one. */
function hasUnresolvedSlurClose(text: string, from: number, end: number): boolean {
    let depth = 0;
    for (let i = from; i < end; i++) {
        const c = text[i];
        if (c === '(') { depth++; }
        else if (c === ')') {
            if (depth === 0) { return true; }
            depth--;
        }
    }
    return false;
}

/** Past the `@annotation`s glued to the note ending at `i` — name, any
 * `(args)`, and a `.up` / `.down` placement suffix — so a ')' lands after the
 * whole note item rather than between the note and its own markings. */
function skipAnnotations(text: string, i: number, end: number): number {
    while (text[i] === '@') {
        let k = i + 1;
        while (k < end && /[A-Za-z0-9_-]/.test(text[k])) { k++; }
        if (text[k] === '(') {
            let depth = 1;
            for (k++; k < end && depth > 0; k++) {
                if (text[k] === '(') { depth++; }
                else if (text[k] === ')') { depth--; }
            }
        }
        const placement = /^\.(?:up|down)/.exec(text.slice(k, end));
        i = placement ? k + placement[0].length : k;
    }
    return i;
}

/** One step of a music walk: a note EVENT with its offsets, or a BARRIER —
 * anything the scan cannot positively identify as a note (a keyword, a phrase
 * reference, a nested block, an unclosed chord). Trivia a slur spans — barlines,
 * beams, ties, other slur marks, comments, mid-music commands — is not reported. */
type MusicEvent = { note: true, start: number, end: number } | { note: false };

/** Past a nested `{ … }` (a tuplet's, grace's or repeat's body), which the walk
 * treats as one opaque item rather than descending into it. */
function skipBlock(text: string, i: number, end: number): number {
    for (let depth = 0; i < end; i++) {
        if (text[i] === '{') { depth++; }
        else if (text[i] === '}') {
            depth--;
            if (depth === 0) { return i + 1; }
        }
    }
    return end;
}

/** Walks the music in [from, end), yielding each note event — a pitch/rest or a
 * chord/arpeggio with its duration, trailing octave marks and annotations — and
 * a barrier for everything else. Both slur ends read this one walk, so they
 * agree on what a "note" is and on what they refuse to reach across. */
function* musicEvents(text: string, from: number, end: number): Generator<MusicEvent> {
    let i = from;
    while (i < end) {
        const c = text[i];
        if (/\s/.test(c) || BETWEEN_EVENTS.test(c)) { i++; continue; }
        if (c === '/' && text[i + 1] === '/') {
            while (i < end && text[i] !== '\n') { i++; }
            continue;
        }
        if (c === '/' && text[i + 1] === '*') {
            for (i += 2; i < end && !(text[i] === '*' && text[i + 1] === '/'); i++) { /* skip */ }
            i += 2;
            continue;
        }
        if (c === '<') {
            // A chord `<c e g>4` or an arpeggio `<< c e g >>2`; members may nest.
            const arpeggio = text[i + 1] === '<';
            let j = i + (arpeggio ? 2 : 1);
            for (let depth = 0; j < end; j++) {
                if (text[j] === '<') { depth++; }
                else if (text[j] === '>') {
                    if (depth === 0) { break; }
                    depth--;
                }
            }
            if (j >= end) { yield { note: false }; return; } // unclosed
            j++;                                                        // past '>'
            if (arpeggio && text[j] === '>') { j++; }
            while (j < end && /[',]/.test(text[j])) { j++; }             // octave marks
            while (j < end && /[0-9.]/.test(text[j])) { j++; }           // duration + dots
            const chordEnd = skipAnnotations(text, j, end);
            yield { note: true, start: i, end: chordEnd };
            i = chordEnd;
            continue;
        }
        const rest = text.slice(i, end);
        const command = MID_MUSIC_COMMAND.exec(rest);
        if (command) { i += command[0].length; continue; }
        const m = NOTE_EVENT.exec(rest);
        const after = m ? i + m[0].length : -1;
        // A longer word merely STARTS like a note: `clef`, `bass`, `rightHand`.
        if (m && !(after < end && /[A-Za-z0-9'_,]/.test(text[after]))) {
            const noteEnd = skipAnnotations(text, after, end);
            yield { note: true, start: i, end: noteEnd };
            i = noteEnd;
            continue;
        }
        yield { note: false };
        if (c === '{') { i = skipBlock(text, i, end); }
        else {
            const word = /^[A-Za-z_][A-Za-z0-9_]*/.exec(rest);
            i += word ? word[0].length : 1;
        }
    }
}

/** The first note event in [from, end), or null when a barrier or the block end
 * comes first — so an automatic slur mark is only ever placed on something the
 * walk positively identified. */
function firstNoteEvent(text: string, from: number, end: number)
    : { start: number, end: number } | null {
    for (const event of musicEvents(text, from, end)) {
        return event.note ? { start: event.start, end: event.end } : null;
    }
    return null;
}

/** The offset just after the note event BEFORE the one a ')' typed at `at`
 * closes on — where that ')' wants its '('. Mirrors nextNoteEnd, so the shortest
 * automatic slur spans two notes whichever end is typed first. A barrier resets
 * the pair, keeping the '(' on this side of anything unrecognized. -1 = none. */
function precedingNoteEnd(text: string, start: number, at: number): number {
    let beforeLast = -1;
    let last = -1;
    for (const event of musicEvents(text, start, at)) {
        if (!event.note) { beforeLast = -1; last = -1; continue; }
        beforeLast = last;
        last = event.end;
    }
    return beforeLast;
}

/** True when [start, at) holds a '(' that no ')' inside the span closes — the
 * slur the typed ')' is there to close. An annotation's still-open `(args)`
 * counts too, which is exactly right: that ')' closes the arguments. */
function hasUnresolvedSlurOpen(text: string, start: number, at: number): boolean {
    let depth = 0;
    for (let i = at - 1; i >= start; i--) {
        const c = text[i];
        if (c === ')') { depth++; }
        else if (c === '(') {
            if (depth === 0) { return true; }
            depth--;
        }
    }
    return false;
}

/** True when `offset` sits inside a string literal or a comment, where a '(' is
 * plain text. Strings are single-line; block comments are not. */
function inStringOrComment(text: string, offset: number): boolean {
    const blockOpen = text.lastIndexOf('/*', offset);
    if (blockOpen >= 0 && text.lastIndexOf('*/', offset) < blockOpen) { return true; }
    let inString = false;
    for (let i = text.lastIndexOf('\n', offset - 1) + 1; i < offset; i++) {
        if (text[i] === '"') { inString = !inString; }
        else if (!inString && text[i] === '/' && text[i + 1] === '/') { return true; }
    }
    return inString;
}

/** True when a '(' typed at `offset` opens a SLUR. A '(' GLUED to a name is an
 * annotation's argument list (`@finger(3)`) or a phrase reference's diatonic
 * interval (`Melody'(3)`); one after whitespace, a barline, a note or a chord's
 * '>' is a slur — the same adjacency rule the parser uses. */
function isSlurOpen(text: string, offset: number): boolean {
    const prev = text[offset - 1];
    if (prev === undefined || /\s/.test(prev) || prev === '|' || prev === '>') { return true; }
    let j = offset - 1;
    while (j >= 0 && /[A-Za-z0-9'.,_-]/.test(text[j])) { j--; }
    if (text[j] === '@') { return false; }  // @annotation(args)
    if (text[j] === '>') { return true; }   // a chord's duration: <c e>4(
    // The glued token must be a note in FULL — `c4`, `ees'2.`, `r8` — so a
    // phrase name that merely opens like one (`bass'`) is not mistaken for one.
    const token = text.slice(j + 1, offset);
    const m = NOTE_EVENT.exec(token);
    return m !== null && m[0].length === token.length;
}

/** The note a typed '(' attaches to: the one the caret is ON, wherever in it the
 * caret sits — at its end, in its middle or at its start are all the same note.
 * 'member' when the caret is inside a chord's brackets, where it is pointing at
 * a member and no slur mark can go; null when it is between events. */
function slurAnchorAt(text: string, offset: number)
    : { start: number, end: number } | 'member' | null {
    const [blockStart, blockEnd] = blockBounds(text, offset);
    for (const event of musicEvents(text, blockStart, blockEnd)) {
        if (!event.note || event.end < offset) { continue; }
        if (event.start > offset) { return null; } // between events
        if (text[event.start] === '<') {
            const slots = noteSlots(text, event.start, event.end);
            if (slots && offset > event.start && offset < slots.marksEnd) { return 'member'; }
        }
        return { start: event.start, end: event.end };
    }
    return null;
}

/** '(' typed in music: the slur's ')' belongs after the note the slur COVERS,
 * not at the caret. Places it after the following note event, drops it entirely
 * when an unresolved ')' already lies ahead, and leaves the plain `()` pair
 * alone when there is no note left in the block. `autoClosed` = VS Code already
 * inserted the ')' at the caret (it does so before whitespace and EOL). */
function onInsertSlurOpen(editor: vscode.TextEditor, text: string, offset: number,
    autoClosed: boolean, log: (msg: string) => void) {
    if (inStringOrComment(text, offset) || !isSlurOpen(text, offset)) { return; }
    // Decided on the text WITHOUT the keystroke, like the octave marks and the
    // durations: a paren parked mid-note splits the very token that has to be
    // read (`c|2` + '(' reads as `c`, `(`, `2` and hides the note `c2`).
    const typedLen = autoClosed ? 2 : 1;
    const before = text.slice(0, offset) + text.slice(offset + typedLen);
    const [, end] = blockBounds(before, offset);

    // WHICH note the slur starts on is read off the caret: it is the note the
    // caret is ON — at its end, in its middle, or at its start, all the same
    // note — and the '(' is written after that note, because that is where the
    // parser reads a slur start from. So `c| d e` and `c|2 d` and `c |d e` all
    // start the slur on the note the caret was in, and the paren travels to that
    // note's end by itself.
    const anchor = slurAnchorAt(before, offset);
    if (anchor === 'member') { return; } // pointing inside a chord
    let openAt = anchor ? anchor.end : offset;
    let closeOn = firstNoteEvent(before, openAt, end);
    if (!closeOn) {
        // Nothing after the anchor to cover. When the caret was pointing AHEAD
        // at that note, the nearest legal two-note slur is the one anchored on
        // the note BEFORE the caret, so the paren stays where it was typed.
        if (!anchor || offset !== anchor.start) { return; }
        openAt = offset;
        closeOn = firstNoteEvent(before, offset, end);
        if (!closeOn) { return; } // nothing to slur to — keep the pair as typed
    }

    // An unresolved ')' ahead: the '(' pairs with THAT, so no close is added —
    // it still moves onto the note the caret pointed at.
    const paired = hasUnresolvedSlurClose(before, openAt, end);
    // A slur already STARTING where this one would close is extended backwards
    // instead: its open gives way to this one (`e| c4( d)` → `e( c4 d)`).
    let existingOpen = closeOn.end;
    while (before[existingOpen] === ' ' || before[existingOpen] === '\t') { existingOpen++; }
    const extend = !paired && before[existingOpen] === '('
        && hasUnresolvedSlurClose(before, existingOpen + 1, end);

    // Offsets computed on `before` are placed in the document as it stands, which
    // still holds the keystroke. The caret goes right after the '(' — in the same
    // edit, so it is never seen at the position the key was pressed.
    const insertAt = (p: number) => (p <= offset ? p : p + typedLen);
    const charAt = (p: number) => (p < offset ? p : p + typedLen);
    const edits: { at: number, del?: number, ins?: string }[] = [
        { at: offset, del: typedLen },
        { at: insertAt(openAt), ins: '(' },
    ];
    if (extend) { edits.push({ at: charAt(existingOpen), del: 1 }); }
    else if (!paired) { edits.push({ at: insertAt(closeOn.end), ins: ')' }); }
    applyFixWithCaret(editor, text, edits, openAt + 1);

    log(`smartBrackets: ( typed -> ${openAt === offset ? '' : 'moved to the end of its note, '}`
        + (extend ? 'extended the slur starting there'
            : paired ? 'paired with the unresolved ) ahead'
                : ') placed after the following note'));
}

/** The note events of the measure around `at`, each with the duration it
 * actually sounds — the running value carried from the measure's start, because
 * only the first note of `c8 d e f` spells the eighth out. Bounded by the
 * MEASURE and not by the block, which is what separates a beam from a slur: a
 * beam cannot cross a barline. A barrier ends what can be read. */
function measureEvents(text: string, at: number)
    : { start: number, end: number, duration: number, rest: boolean }[] {
    const [mStart, mEnd] = measureBounds(text, at);
    const events: { start: number, end: number, duration: number, rest: boolean }[] = [];
    let running = 0;
    for (const event of musicEvents(text, mStart, mEnd)) {
        if (!event.note) {
            if (events.length > 0) { break; }
            continue;
        }
        const slots = noteSlots(text, event.start, event.end);
        if (!slots) { break; }
        const digits = text.slice(slots.marksEnd, slots.digitsEnd);
        running = digits ? parseInt(digits, 10) : running;
        events.push({
            start: event.start, end: event.end,
            duration: running, rest: slots.octave === null,
        });
    }
    return events;
}

/** Walks the beamable run from `i` in `step` direction and returns the offset
 * just after the LAST NOTE it reaches, or -1 when fewer than two notes line up.
 * A rest is spanned — `c8[ r8 d8]` is a beam over a rest — but never ends one,
 * so a trailing rest is not what the bracket lands after. A duration that
 * carries no flag ends the run, rest or not. */
function beamRun(events: { end: number, duration: number, rest: boolean }[],
    i: number, step: 1 | -1): number {
    if (i < 0 || !events[i] || events[i].rest || !BEAMABLE.has(events[i].duration)) { return -1; }
    let notes = 0;
    let lastNoteEnd = -1;
    for (let k = i; k >= 0 && k < events.length; k += step) {
        if (!BEAMABLE.has(events[k].duration)) { break; }
        if (!events[k].rest) {
            notes++;
            lastNoteEnd = events[k].end;
        }
    }
    return notes >= 2 ? lastNoteEnd : -1;
}

/** True when [from, end) holds a ']' that no '[' inside the span opens — an
 * existing beam end a just-typed '[' pairs with. */
function hasUnresolvedBeamClose(text: string, from: number, end: number): boolean {
    let depth = 0;
    for (let i = from; i < end; i++) {
        if (text[i] === '[') { depth++; }
        else if (text[i] === ']') {
            if (depth === 0) { return true; }
            depth--;
        }
    }
    return false;
}

/** True when [start, at) holds a '[' that no ']' inside the span closes — the
 * beam a just-typed ']' is there to close. */
function hasUnresolvedBeamOpen(text: string, start: number, at: number): boolean {
    let depth = 0;
    for (let i = at - 1; i >= start; i--) {
        if (text[i] === ']') { depth++; }
        else if (text[i] === '[') {
            if (depth === 0) { return true; }
            depth--;
        }
    }
    return false;
}

/** '[' typed in music: a manual beam opens after the note it starts on, exactly
 * as a slur and a tie do, and closes on the last note that can still be beamed
 * in the same measure (`c8|` + '[' → `c8[ d e f]`). A note that cannot carry a
 * beam, or one with nothing beamable behind it, is left as typed — as is a '['
 * that is not on a note at all, which is what an inline volta's `[1.` is. */
function onInsertBeamOpen(editor: vscode.TextEditor, text: string, offset: number,
    autoClosed: boolean, log: (msg: string) => void) {
    if (inStringOrComment(text, offset)) { return; }
    const typedLen = autoClosed ? 2 : 1;
    const before = text.slice(0, offset) + text.slice(offset + typedLen);
    const anchor = slurAnchorAt(before, offset);
    if (!anchor || anchor === 'member') { return; }
    const events = measureEvents(before, anchor.start);
    const runEnd = beamRun(events, events.findIndex(e => e.start === anchor.start), 1);
    if (runEnd < 0) { return; } // not beamable, or nothing to group with

    const [, mEnd] = measureBounds(before, anchor.end);
    const paired = hasUnresolvedBeamClose(before, anchor.end, mEnd);
    const insertAt = (p: number) => (p <= offset ? p : p + typedLen);
    const edits: { at: number, del?: number, ins?: string }[] = [
        { at: offset, del: typedLen },
        { at: insertAt(anchor.end), ins: '[' },
    ];
    if (!paired) { edits.push({ at: insertAt(runEnd), ins: ']' }); }
    applyFixWithCaret(editor, text, edits, anchor.end + 1);
    log(`smartBrackets: [ typed -> beam ${paired ? 'opened against the ] ahead' : 'closed on its run'}`);
}

/** ']' typed in music: the end of a beam is written after its note like every
 * other mark here, so one typed inside a note moves to that note's end — and it
 * reaches BACK for the note the beam started on, the mirror of what '[' does
 * forwards: the first note of the beamable run that ends here. An unresolved '['
 * already in the measure means the ']' simply closes that, and only moves. */
function onInsertBeamClose(editor: vscode.TextEditor, text: string, offset: number,
    log: (msg: string) => void) {
    if (inStringOrComment(text, offset)) { return; }
    const before = text.slice(0, offset) + text.slice(offset + 1);
    const anchor = slurAnchorAt(before, offset);
    if (!anchor || anchor === 'member') { return; }

    const [mStart] = measureBounds(before, anchor.start);
    const events = measureEvents(before, anchor.start);
    const runStart = hasUnresolvedBeamOpen(before, mStart, anchor.start)
        ? -1 // already open — this ']' is its close
        : beamRun(events, events.findIndex(e => e.start === anchor.start), -1);
    if (runStart < 0 && anchor.end === offset) { return; } // nothing to do at all

    const insertAt = (p: number) => (p <= offset ? p : p + 1);
    const edits: { at: number, del?: number, ins?: string }[] = [
        { at: offset, del: 1 },
        { at: insertAt(anchor.end), ins: ']' },
    ];
    if (runStart >= 0) { edits.push({ at: insertAt(runStart), ins: '[' }); }
    // Past the ']' — which the '[' inserted before it has pushed along by one.
    applyFixWithCaret(editor, text, edits, anchor.end + (runStart >= 0 ? 2 : 1));
    log(`smartBrackets: ] typed -> ${runStart >= 0 ? '[ placed on its run' : 'moved to the end of its note'}`);
}

/** '~' typed in music: a tie is written after the note it starts from, exactly
 * as a slur's '(' is (`c4~ | c4`), so one typed anywhere ON a note moves to that
 * note's end. Unlike a slur it is a single mark — it needs no second note here,
 * because the note it ties TO is whatever follows, and the compiler is the one
 * that checks the pitch repeats (LYS4007). */
function onInsertTie(editor: vscode.TextEditor, text: string, offset: number,
    log: (msg: string) => void) {
    if (inStringOrComment(text, offset)) { return; }
    const before = text.slice(0, offset) + text.slice(offset + 1);
    const anchor = slurAnchorAt(before, offset);
    if (!anchor || anchor === 'member') { return; } // between events, or a chord member
    if (anchor.end === offset) { return; } // already where it belongs
    applyFixWithCaret(editor, text, [
        { at: offset, del: 1 },
        { at: anchor.end <= offset ? anchor.end : anchor.end + 1, ins: '~' },
    ], anchor.end + 1);
    log('smartBrackets: ~ typed -> moved to the end of its note');
}

/** ')' typed in music: the slur's '(' belongs after the note BEFORE the one the
 * ')' closes on (`c4 d` + ')' → `c4( d)`), the mirror of a typed '(' reaching
 * forward. An unresolved '(' before it — a slur the user already opened, or an
 * annotation's argument list — means the ')' just closes that, so nothing is
 * added; nor is anything added with no preceding note to open on. */
function onInsertSlurClose(editor: vscode.TextEditor, text: string, offset: number,
    log: (msg: string) => void) {
    if (inStringOrComment(text, offset)) { return; }
    const [start] = blockBounds(text, offset);
    if (hasUnresolvedSlurOpen(text, start, offset)) { return; }
    const anchor = precedingNoteEnd(text, start, offset);
    if (anchor < 0) { return; } // nothing to open on — leave the ')' as typed
    // A slur ALREADY ends where the '(' would go: the user is EXTENDING that
    // slur over one more note, so its old close gives way to the typed one
    // (`c4( d) e` + ')' → `c4( d e)`). Opening a second slur there instead
    // would nest an empty one inside the first.
    let old = anchor;
    while (text[old] === ' ' || text[old] === '\t') { old++; }
    if (text[old] === ')' && hasUnresolvedSlurOpen(text, start, old)) {
        applyFix(editor, b => b.delete(new vscode.Range(
            editor.document.positionAt(old), editor.document.positionAt(old + 1))));
        log('smartBrackets: ) typed -> extended the slur ending on the previous note');
        return;
    }
    // The insert lands BEFORE the caret, which VS Code shifts along with it.
    applyFix(editor, b => b.insert(editor.document.positionAt(anchor), '('));
    log('smartBrackets: ) typed -> ( placed after the preceding note');
}

/** The written slots of the note event at [start, end): where its octave marks
 * go (null for a rest, which takes none), where the marks already there END —
 * which is also where the duration starts — where its digits end, and where its
 * CORE does (pitch, marks, duration and dots, but NOT its annotations). null
 * when the event is not something these slots apply to. */
interface NoteSlots { octave: number | null, marksEnd: number, digitsEnd: number, coreEnd: number }

function noteSlots(text: string, start: number, end: number): NoteSlots | null {
    let octave: number | null = null;
    let marksEnd: number;
    if (text[start] === '<') {
        const arpeggio = text[start + 1] === '<';
        let j = start + (arpeggio ? 2 : 1);
        for (let depth = 0; j < end; j++) {
            if (text[j] === '<') { depth++; }
            else if (text[j] === '>') {
                if (depth === 0) { break; }
                depth--;
            }
        }
        if (j >= end) { return null; }
        j++;
        if (arpeggio && text[j] === '>') { j++; }
        octave = marksEnd = j;
    } else {
        const m = PITCH_LETTERS.exec(text.slice(start, end));
        if (m) { octave = marksEnd = start + m[0].length; }
        else if ('rsR'.includes(text[start])) { marksEnd = start + 1; } // a rest
        else { return null; }
    }
    while (marksEnd < end && (text[marksEnd] === "'" || text[marksEnd] === ',')) { marksEnd++; }
    let digitsEnd = marksEnd;
    while (digitsEnd < end && /[0-9]/.test(text[digitsEnd])) { digitsEnd++; }
    let coreEnd = digitsEnd;
    while (coreEnd < end && text[coreEnd] === '.') { coreEnd++; }
    return { octave, marksEnd, digitsEnd, coreEnd };
}

/** The note event the caret at `offset` is ON, with its slots — or null when the
 * caret is between events, on a barrier, or past a note's core (inside its
 * annotations, where a ',' or a digit means something else entirely). Anywhere
 * inside a plain note counts; a chord counts only at its ENDS, because its
 * interior belongs to the members (`<c, e g>`, `<c 3 5>`) and what is typed
 * there is already in the right place. */
function noteAtCaret(text: string, offset: number)
    : { start: number, slots: NoteSlots } | null {
    const [blockStart, blockEnd] = blockBounds(text, offset);
    for (const event of musicEvents(text, blockStart, blockEnd)) {
        if (!event.note || event.end < offset) { continue; }
        if (event.start > offset) { return null; } // the caret is between events
        const slots = noteSlots(text, event.start, event.end);
        if (!slots) { return null; }
        const onIt = text[event.start] === '<'
            ? offset === event.start
                || (offset >= slots.marksEnd && offset <= slots.coreEnd)
            : offset >= event.start && offset <= slots.coreEnd;
        return onIt ? { start: event.start, slots } : null;
    }
    return null;
}

/** "'" or ',' typed in music. An octave mark belongs between the pitch and the
 * duration, so one typed at either END of a note (`|c4`, `c4|`) moves into that
 * slot rather than splitting the token. Typed against the OPPOSITE mark it
 * CANCELS one instead — "'" undoes a ',' and vice versa — so an overshot octave
 * is walked back with the key that caused it, no selecting or backspacing. */
function onInsertOctaveMark(editor: vscode.TextEditor, text: string, offset: number,
    mark: string, log: (msg: string) => void) {
    if (inStringOrComment(text, offset)) { return; }
    // Everything is decided on the text WITHOUT the keystroke: a mark parked at
    // the caret would otherwise split the very note token being read.
    const before = text.slice(0, offset) + text.slice(offset + 1);
    const found = noteAtCaret(before, offset);
    if (!found || found.slots.octave === null) { return; } // a rest takes none
    const { octave, marksEnd } = found.slots;

    // Offsets are read off `before`; the document still holds the keystroke, so
    // everything from it on has shifted by one.
    const shifted = (p: number) => (p < offset ? p : p + 1);

    const opposite = mark === "'" ? ',' : "'";
    const cancel = before.lastIndexOf(opposite, marksEnd - 1);
    if (cancel >= octave) {
        // The caret lands at the END of what is left of the mark run — where the
        // cancelled mark was, and where the next one would go. Placed in the same
        // edit rather than left to the editor's own shifting, so cancelling from
        // the middle of a note (`c'|2` + ',' → `c|2`) puts it in the same place
        // as cancelling from the end.
        applyFixWithCaret(editor, text, [
            { at: offset, del: 1 },
            { at: shifted(cancel), del: 1 },
        ], marksEnd - 1);
        log(`smartBrackets: ${mark} typed -> cancelled one ${opposite}`);
        return;
    }
    if (marksEnd === offset) { return; } // already in the slot
    // The caret follows the mark it typed — in the same edit, so it is never
    // seen at the key's position. Leaving it behind would also make the NEXT
    // mark travel all over again instead of simply stacking.
    applyFixWithCaret(editor, text, [
        { at: offset, del: 1 },
        { at: marksEnd <= offset ? marksEnd : marksEnd + 1, ins: mark },
    ], marksEnd + 1);
    log(`smartBrackets: ${mark} typed -> moved into the octave slot`);
}

/** A digit typed in music: a duration belongs AFTER the octave marks (`c,4`),
 * so one typed anywhere else on the note moves there — `|c` and `c,|` alike.
 * Digits already in the slot are EXTENDED while the result is still a duration
 * in the making (`c1` + '6' → `c16`, from any caret position on the note) and
 * REPLACED when it cannot become one (`c4` + '2' → `c2`, since neither `c42`
 * nor `c24` exists). A digit that starts no duration at all — 5, 7, 9, 0 — is
 * left exactly as typed. */
function onInsertDuration(editor: vscode.TextEditor, text: string, offset: number,
    digit: string, log: (msg: string) => void) {
    if (inStringOrComment(text, offset)) { return; }
    const before = text.slice(0, offset) + text.slice(offset + 1);
    const found = noteAtCaret(before, offset);
    if (!found) { return; }
    const { marksEnd, digitsEnd } = found.slots;
    const digits = before.slice(marksEnd, digitsEnd);

    const extend = isDurationPrefix(digits + digit);
    if (extend && digitsEnd === offset) { return; }      // already in place
    if (!extend && !isDurationPrefix(digit)) { return; } // not a duration at all
    const written = extend ? digits + digit : digit;

    // One replace over everything affected — the digit run AND the keystroke,
    // which may sit inside it. `lo` is at or before the keystroke, so it is the
    // same offset in both texts; `hi` is at or after it, so it shifts by one.
    const lo = Math.min(marksEnd, offset);
    const hi = Math.max(digitsEnd, offset);
    const replacement = before.slice(lo, marksEnd) + written + before.slice(digitsEnd, hi);
    // Caret right after the digits, in the same edit, so the next keystroke
    // keeps building: `c1` then '6' lands on `c16`, not `c61`.
    applyFixWithCaret(editor, text, [{ at: lo, del: hi + 1 - lo, ins: replacement }],
        marksEnd + written.length);
    // `written === digits` is the same duration retyped (`c1` + '1'): the text
    // does not move, only the caret — that '1' is the first of a `16` or a `128`.
    const action = extend ? 'extended' : written === digits ? 'restarted' : 'replaced';
    log(`smartBrackets: ${digit} typed -> duration ${action} (${written})`);
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
