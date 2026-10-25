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
import type { LanguageClient } from 'vscode-languageclient/node';
import { ChatClient, ChatMessage, resolveChatClient } from './modelClient';
import {
    CandidateEdit as CoreCandidateEdit, ChangedSpan, MarkInSelection, MarkReadings, SEL_CLOSE, SEL_OPEN, cleanCandidate,
    TextChange, carryOffset, documentBudget, documentContext, estimateTokens, isProblem, marksContext, octaveOutliers,
    startTimeAt, toCandidateEdit, wholeFileEdit,
} from './aiTransformCore';
import { markFactsOf, sideFileOf } from './omrScanCore';
import { textFontFaceCss, textFontsRoot } from './scoreFonts';
export { cleanCandidate }; // aiComplete reads it from here

// ---- Dependencies wired in from extension.ts (keeps the LSP client global there) ----
export interface AiTransformDeps {
    extensionUri: vscode.Uri;
    /** The LanguageClient, or undefined if not started. */
    getClient: () => LanguageClient | undefined;
    /** True once the client has started and is ready to serve requests. */
    isReady: () => boolean;
    log: (msg: string) => void;
    /** Plays a note list (lilysharp/playback's) on the open preview of `uri` — its synth, its
     *  sound font, its MIDI output; false when no preview of it is open. */
    playOnPreview?: (uri: vscode.Uri, notes: unknown[]) => boolean;
    /** Stops what the preview of `uri` is playing. */
    stopOnPreview?: (uri: vscode.Uri) => void;
    /** The score the preview of `uri` shows (its picker), or undefined for the first. */
    renderNameOf?: (uri: vscode.Uri) => string | undefined;
}

/** What a candidate's Play sounds (owner's request 2026-10-25: hear it before accepting): the
 *  document, the text as it stands, and the text with the candidate in. */
interface Audible { uri: vscode.Uri; before: string; after: string }

// The round under review — what its panel's Play plays.
let audible: Audible | undefined;

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
            runAiTransform(deps, opts?.whole === true, opts?.selected))
    );
    context.subscriptions.push({
        dispose: () => {
            candidatePanel?.dispose();
        }
    });
}

/** The whole select→prompt→validate→render→apply transaction (§3/§4). */
async function runAiTransform(deps: AiTransformDeps, wholeScore = false, selected?: string): Promise<void> {
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
    // it used to be refused there. The preview's "Ask AI about the Whole Score…" asks for the
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
    const budget = documentBudget(chat.maxInputTokens, estimateTokens(systemPrompt(grammar)));
    const shown = documentContext(snapshot, budget, whole);
    if (!shown.complete) {
        deps.log(`AI transform: the file (~${estimateTokens(snapshot.origFullText)} tokens) is over ${chat.label}'s `
            + `budget (${budget}); it is sent in part.`);
    }

    // A selection points the AI at a place but does not fence it in (owner's requests
    // 2026-10-25): the box leads with what was selected, as "Nothing is selected" leads the
    // whole-score one, then says the whole score stays open to change and nothing lands unseen —
    // or, for a file too long for the model, what part of it the AI reads.
    const scope = shown.complete
        ? 'the whole score is open to change'
        : 'the file is too long for this model to read whole: the AI reads its head and the bars around the selection';
    const instruction = await vscode.window.showInputBox({
        title: whole ? 'Lily# — transform the whole score with AI' : 'Lily# — transform selection with AI',
        prompt: whole
            ? (shown.complete
                ? 'Nothing is selected, so the AI works on the whole file. Describe the change (natural language)'
                : 'Nothing is selected, but the file is too long for this model to read whole: the AI reads its '
                  + 'beginning. Describe the change (natural language)')
            : `${what} — a hint of where to look, but ${scope}; `
              + 'every change is shown before it is applied. Describe the change (natural language)',
        placeHolder: nextPlaceholder(),
        ignoreFocusOut: true,
    });
    if (instruction === undefined || instruction.trim().length === 0) {
        return; // cancelled
    }

    try {
        const ai = await toAbsoluteOctaves(deps, client, snapshot, whole);
        await drive(deps, client, chat, snapshot, ai, instruction, whole, grammar,
            ai.converted ? documentContext(ai.snap, budget, whole) : shown);
    } catch (err: any) {
        vscode.window.showErrorMessage(`Lily#: AI transform failed: ${err?.message ?? err}`);
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
    let resp: AiOctavesResponse | undefined;
    try {
        resp = await client.sendRequest<AiOctavesResponse>('lilysharp/aiOctaves', { Text: full, ToAbsolute: false, Original: original });
    } catch (err: any) {
        resp = { NewText: null, Changes: [], Error: String(err?.message ?? err) };
    }
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
): Promise<void> {
    const title = whole ? 'Lily#: AI transform — the whole score' : 'Lily#: AI transform';
    // What the AI reads and writes, and every candidate is spliced into; `snapshot` stays the
    // document's (its resolved facts, its @todo marks, and the edit Accept applies).
    const work = ai.snap;
    const prepared = await working(title, async (progress, token) => {
        progress.report({ message: `asking ${chat.label}…` });
        // Quality log (§M5) — local only, no telemetry.
        deps.log(`AI transform: "${instruction}" via ${chat.label}`);

        // Resolved facts of the selection (§5): un-blindfold the model. Best-effort.
        progress.report({ message: 'reading resolved facts…' });
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
        return token.isCancellationRequested ? null : { facts, marks, baseline, renderBefore };
    });
    if (!prepared) {
        return;
    }
    const { facts, marks, baseline, renderBefore } = prepared;
    const baselineErrors = count(baseline, 'error');
    const baselineWarnings = baseline.filter(d => d.Severity === 'warning' && !MACHINE_WARNING.test(d.Message)).length;

    // Conversation seed.
    const messages: ChatMessage[] = [
        { role: 'system', content: systemPrompt(grammar) },
        { role: 'user', content: taskPrompt(work, shown, whole, facts, baseline, instruction, marks, ai.converted) },
    ];

    let iterate = true;
    while (iterate) {
        // One round — generate, validate, render — under its notification; null when cancelled or
        // failed (a failure has said so itself).
        const round = await working(title, async (progress, token) => {
            if (token.isCancellationRequested) return null;

            // ----- Generate + validate-and-self-repair (§4 AwaitingModel → Validating → Repairing) -----
            let candidate: CandidateEdit | null = null;
            let repairs = 0;
            // A candidate that compiles but adds WARNINGS (a bar that no longer fills its meter
            // is a warning in Lily#, not an error) is repaired like a broken one; if the repairs
            // run out, the best such candidate is still shown, with its warnings named, rather
            // than nothing — it is valid Lily#, and the user can see on the score whether it is
            // what they asked for.
            let warned: { edit: CandidateEdit; warnings: CandidateDiagnostic[] } | null = null;
            let unchanged = false;
            let octaveAsked: string | null = null;
            let octaveNote: ResolvedPitchFact[] | null = null;
            for (let attempt = 0; attempt <= MAX_REPAIR_ATTEMPTS; attempt++) {
                if (token.isCancellationRequested) return null;
                progress.report({ message: attempt === 0 ? 'generating…' : `repairing (${attempt}/${MAX_REPAIR_ATTEMPTS})…` });

                const raw = await chat.send(messages, token);
                const edit = toCandidateEdit(work, raw);
                if (edit === null) {
                    messages.push({ role: 'assistant', content: raw });
                    messages.push({ role: 'user', content: `Your reply was empty. ${replyForm(whole)} — no commentary, no code fences.` });
                    continue;
                }
                if (isProblem(edit)) {
                    deps.log(`AI transform: edits not applied — ${edit.problem}`);
                    messages.push({ role: 'assistant', content: raw });
                    messages.push({ role: 'user', content: `${edit.problem}\nReply again, all edits — same output rules.` });
                    continue;
                }
                // A reply that changes nothing is not a transform. The owner accepted one such
                // candidate, the section exactly as it was, because nothing said it was
                // unchanged (2026-09-26) — the model had been asked for a new part it could not
                // write inside the selection.
                if (edit.text === edit.snap.origSelectedText) {
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
                    messages.push({ role: 'assistant', content: raw });
                    messages.push({ role: 'user', content:
                        'Your change added or removed an `octave absolute` line. Keep the file\'s `octave` lines exactly '
                        + 'as they are — reply again, same output rules.' });
                    continue;
                }
                unchanged = false;

                progress.report({ message: 'validating candidate…' });
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
                    let written: ResolvedPitchFact[] = [];
                    if (edit.text !== octaveAsked) {
                        for (const s of edit.spans) {
                            written = written.concat(await pitchesOf(client, reconstructed, s.afterLo, s.afterHi));
                        }
                    }
                    const outliers = octaveOutliers(facts, written);
                    if (outliers.length > 0 && attempt < MAX_REPAIR_ATTEMPTS) {
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
                const sameForm = edit.whole
                    ? 'Reply again with all the corrected edits, each <old> copied from the file as it stands'
                    : 'Return a corrected replacement for the selection (or <edit> blocks)';
                messages.push({ role: 'assistant', content: raw });
                if (badness.length > 0) {
                    messages.push({ role: 'user', content:
                        `That candidate does not compile. The Lily# compiler reported:\n${formatDiags(badness)}\n` +
                        `${sameForm} — same output rules.` });
                } else {
                    if (!warned || newWarnings.length < warned.warnings.length) {
                        warned = { edit, warnings: newWarnings };
                    }
                    messages.push({ role: 'user', content:
                        `That candidate compiles, but it adds warnings the file did not have:\n${formatDiags(newWarnings)}\n` +
                        `${sameForm} that adds no warnings — same output rules.` });
                }
            }

            let caption = instruction;
            if (candidate === null && warned) {
                candidate = warned.edit;
                caption = `${instruction} — ⚠ ${warned.warnings.length} new warning(s): `
                    + warned.warnings.map(w => w.Message).join(' / ');
                deps.log(`AI transform: shown with ${warned.warnings.length} new warning(s) after ${MAX_REPAIR_ATTEMPTS} repairs: `
                    + warned.warnings.map(w => w.Message).join(' | '));
            }

            if (candidate === null) {
                // §4 Failed: exhausted repairs — abort WITHOUT showing broken notation.
                if (unchanged) {
                    deps.log(`AI transform: the model returned the file unchanged after ${MAX_REPAIR_ATTEMPTS} retries — nothing applied.`);
                    vscode.window.showWarningMessage(
                        'Lily#: the AI returned the music unchanged, even when asked again. Nothing was changed — try wording the request differently.');
                } else {
                    deps.log(`AI transform: FAILED validation after ${MAX_REPAIR_ATTEMPTS} repairs — nothing applied.`);
                    vscode.window.showWarningMessage(
                        'Lily#: the AI could not produce a valid transform after several tries. Nothing was changed.');
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
            progress.report({ message: 'rendering candidate…' });
            const reconstructed = spliceCandidate(candidate.snap, candidate.text);
            const renderAfter = await client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: reconstructed });
            // Source-offset spans of the change so the panel can highlight WHERE it landed —
            // each edit's, in the "after" score and in the "before" one. The SVG carries
            // data-pos (source offsets) for editor↔preview sync, so the webview lights up the
            // notes whose data-pos falls in these spans.
            const changed = candidate.spans;
            return token.isCancellationRequested ? null : { candidate, caption, renderAfter, changed };
        });
        if (!round) {
            return;
        }
        // The notification has closed: the review is the user's, with nothing over it.
        const { candidate, caption, renderAfter, changed } = round;
        // The review, and Iterate's question about it: a refine closed or left empty goes back
        // to the same candidate on its score, to be decided again (owner's request 2026-10-25 —
        // it used to end the transform, dropping the candidate unapplied).
        let decision: Decision;
        let refine: string | undefined;
        for (;;) {
            decision = await reviewOnScore(deps, renderBefore, renderAfter, caption, candidate.text, changed, {
                uri: snapshot.uri,
                before: work.origFullText,
                after: spliceCandidate(candidate.snap, candidate.text),
            });
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
            messages.push({ role: 'assistant', content: candidate.reply });
            // The file has not changed: a revision is answered against it as it stands, as the
            // previous candidate was.
            messages.push({ role: 'user', content:
                `Revise the previous candidate: ${refine}\nThe file is still as it was before that candidate. `
                + `${replyForm(whole)} — same output rules.` });
            instruction = refine; // caption reflects the latest ask
            continue; // back to generate
        }

        // ----- Accept: apply with the version/range guard (§7) -----
        deps.log('AI transform: accepted.');
        if (ai.converted) {
            // Back to the file's relative octaves, applied as the span where the file changes:
            // the AI's edits, and the notes after them whose marks follow from new neighbours.
            const back = await fromAbsoluteOctaves(deps, client, spliceCandidate(candidate.snap, candidate.text), snapshot.origFullText);
            if (back === undefined) {
                deps.log('AI transform: not applied (it could not be brought back to relative octaves).');
                panelStatus('Not written — it could not be brought back to the file\'s relative octaves.');
                return;
            }
            const edit = wholeFileEdit(snapshot, back);
            await applyCandidate(deps, edit.snap, edit.text);
        } else {
            await applyCandidate(deps, candidate.snap, candidate.text);
        }
        iterate = false;
    }
}

// ======================================================================
// Prompt construction (§5)
// ======================================================================

function systemPrompt(grammar: string): string {
    return [
        'You transform fragments of a musical score written in Lily#, a text music-notation language.',
        'Lily# is NOT LilyPond; obey the grammar below exactly.',
        '',
        '<lilysharp-grammar>',
        grammar,
        '</lilysharp-grammar>',
        '',
        'OUTPUT CONTRACT (strict — the result is applied to the file mechanically). Reply in ONE of two forms:',
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
        '   insertion. Edits must not overlap. Never write the whole file out.',
        '   Do not reach for `voice { }` just to stay inside the selection when a separate part is asked for.',
        '- Either way: no explanations, no commentary, no Markdown, no code fences, no selection markers.',
        // Owner's report 2026-10-25: "add chords" came back as @chord on every note. The answers
        // to everyday requests live in the grammar's "Common requests" section.
        '- An everyday request ("add chords", "add lyrics", "add a bass line", "add tab", "repeat this",',
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
function replyForm(whole: boolean): string {
    return whole
        ? 'Reply with <edit> blocks'
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
    marks: string | null = null, absolute = false): string {
    const parts: string[] = [];
    if (absolute) {
        // toAbsoluteOctaves: the user's file is relative; it was converted for the model.
        parts.push('The file is shown in ABSOLUTE octaves (`octave absolute` at its top): every pitch says where it '
            + 'sounds — a bare pitch is in the octave of the part\'s anchor (C4 unless the part\'s `octave N` or '
            + 'instrument says otherwise), and each \' / , moves it one octave from there, never from the note '
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
    parts.push(`${replyForm(whole)}.`);
    return parts.join('\n');
}

// ======================================================================
// Candidate cleaning & validation
// ======================================================================

/** A candidate as the edit it makes (aiTransformCore.ts), against this module's snapshot. */
type CandidateEdit = CoreCandidateEdit<Snapshot>;

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

function formatDiags(diags: CandidateDiagnostic[]): string {
    return diags.map(d => `  - line ${d.Line + 1}: ${d.Message}`).join('\n');
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


async function reviewOnScore(
    deps: AiTransformDeps,
    renderBefore: SvgResponse,
    renderAfter: SvgResponse,
    caption: string,
    candidate: string,
    changed: ChangedSpan[],
    play: Audible,
): Promise<Decision> {
    const panel = ensureCandidatePanel(deps);
    audible = play;
    const fontUri = panel.webview.asWebviewUri(
        vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts', 'emmentaler-20.woff2'));
    const braceFontUri = panel.webview.asWebviewUri(
        vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts', 'emmentaler-brace.woff2'));

    panel.webview.html = getCandidateHtml(
        fontUri.toString(), braceFontUri.toString(), panel.webview.cspSource, getNonce(),
        renderBefore.Svg, renderAfter.Svg, renderAfter.Error, caption, candidate, changed,
        textFontFaceCss(panel.webview, deps.extensionUri));
    panel.reveal(vscode.ViewColumn.Beside, true);

    // Hand this round's resolver to the panel's persistent message pump. Iterate
    // reloads the webview (new html) each round; a listener registered here, right
    // after that reload, could miss the reloaded frame's messages — so the pump is
    // registered ONCE at panel creation (ensureCandidatePanel) and routes here.
    return await new Promise<Decision>(resolve => {
        pendingDecision = resolve;
    });
}

function ensureCandidatePanel(deps: AiTransformDeps): vscode.WebviewPanel {
    if (candidatePanel) {
        return candidatePanel;
    }
    const fontsUri = vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts');
    const panel = vscode.window.createWebviewPanel(
        'lilysharpAiCandidate',
        'Lily# — AI candidate',
        { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
        { enableScripts: true, retainContextWhenHidden: true,
          localResourceRoots: [fontsUri, textFontsRoot(deps.extensionUri)] });

    // One persistent message pump for the panel's whole lifetime. Registering the
    // listener once — before any html is set — guarantees it receives Accept/
    // Iterate/Reject across every review round, including after an iterate reload.
    const settle = (d: Decision) => {
        const resolve = pendingDecision;
        pendingDecision = undefined;
        // A decision ends a candidate's Play: the music changes, or the panel goes.
        if (audible) { deps.stopOnPreview?.(audible.uri); }
        resolve?.(d);
    };
    panel.webview.onDidReceiveMessage((m: any) => {
        if (m && (m.action === 'accept' || m.action === 'iterate' || m.action === 'reject')) {
            settle(m.action);
        } else if (m?.action === 'play') {
            void playCandidate(deps, panel, m.which === 'before' ? 'before' : 'after',
                typeof m.from === 'number' ? m.from : undefined);
        } else if (m?.action === 'stop' && audible) {
            deps.stopOnPreview?.(audible.uri);
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
    from?: number): Promise<void> {
    const play = audible;
    const client = deps.getClient();
    if (!play || !client) { return; }
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
        const start = from === undefined ? 0 : startTimeAt(pb.Notes as { S?: number; T?: number }[], from);
        const notes = pb.Notes
            .filter(n => ((n as { T?: number }).T ?? 0) >= start - 1e-6)
            .map(n => ({ ...n, T: ((n as { T?: number }).T ?? 0) - start, S: -1 }));
        if (!deps.playOnPreview?.(play.uri, notes)) {
            say('Open the preview of this file to hear it (the preview is the player).');
            return;
        }
        say(which === 'after' ? 'Playing the candidate in the preview…' : 'Playing the original in the preview…');
    } catch (e) {
        say(`Cannot play: ${String(e)}`);
    }
}

function getCandidateHtml(
    fontUri: string, braceFontUri: string, cspSource: string, nonce: string,
    svgBefore: string | null, svgAfter: string | null, error: string | null,
    caption: string, candidate: string, changed: ChangedSpan[], textFontCss: string,
): string {
    const afterBody = svgAfter
        ? `<div class="score">${svgAfter}</div>`
        : `<div class="err">Could not render candidate.${error ? `<pre>${escapeHtml(error)}</pre>` : ''}</div>`;
    const beforeBody = svgBefore
        ? `<div class="score">${svgBefore}</div>`
        : `<div class="err">Could not render the original.</div>`;
    return `<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; font-src ${cspSource}; script-src 'nonce-${nonce}';">
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
.header .spacer { flex:1; }
.toggle { display:flex; gap:4px; }
.toggle button { padding:3px 10px; font-size:12px; }
.toggle button.on { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
.main { flex:1; overflow:auto; background:white; padding:16px; }
.score svg { max-width:100%; height:auto; }
/* The notes the AI changed — same non-destructive glow the main preview uses
   for editor↔preview sync, so "what changed" reads at a glance. */
.chg { filter: drop-shadow(0 0 3.5px #ff6600); }
/* A note plays the pane from it when clicked. */
.score [data-pos] { cursor: pointer; }
.pane { display:none; }
.pane.show { display:block; }
.snippet { margin:10px 16px 0; }
.snippet pre { background: var(--vscode-textCodeBlock-background); padding:8px; border-radius:4px;
               font-family: var(--vscode-editor-font-family, monospace); font-size:12px; overflow:auto; margin:4px 0 0;
               white-space:pre-wrap; }
.snippet summary { font-size:12px; opacity:0.8; cursor:pointer; }
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
.playstate { align-self:center; font-size:12px; opacity:0.8; }
.playstate.busy { opacity:1; font-weight:600; }
</style>
</head>
<body>
<div class="header">
  <div>
    <div class="cap">AI candidate — decide on the score · <span style="color:#ff6600">■</span> changed</div>
    <div class="prompt">${escapeHtml(caption)}</div>
  </div>
  <div class="spacer"></div>
  <div class="toggle">
    <button id="tAfter" class="on">After</button>
    <button id="tBefore">Before</button>
  </div>
</div>
<div class="main">
  <div id="paneAfter" class="pane show">${afterBody}</div>
  <div id="paneBefore" class="pane">${beforeBody}</div>
</div>
<div class="snippet"><details><summary>Replacement text</summary><pre>${escapeHtml(candidate)}</pre></details></div>
<div class="footer">
  <button class="primary" id="accept" title="Write this candidate into the file (Ctrl+Z in the editor undoes it)">Accept (Enter)</button>
  <button id="iterate" title="Tell the AI what to change about this candidate — it answers with a new one, shown here">Ask Again…</button>
  <button id="play" title="Play the pane shown — After or Before — in the preview (Space)">▶ Play</button>
  <button id="stop">■ Stop</button>
  <span id="playState" class="playstate"></span>
  <div class="spacer"></div>
  <button id="reject" title="Drop this candidate and close the panel — the file stays as it is">Reject (Esc)</button>
</div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const send = (action) => vscode.postMessage({ action });
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

  function show(after) {
    paneAfter.classList.toggle('show', after);
    paneBefore.classList.toggle('show', !after);
    tAfter.classList.toggle('on', after);
    tBefore.classList.toggle('on', !after);
    scrollToChange(after);
  }
  tAfter.addEventListener('click', () => show(true));
  tBefore.addEventListener('click', () => show(false));
  // Default pane is "After": center its first changed note once laid out.
  scrollToChange(true);
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
  const decide = (action) => {
    if (decided) return;
    decided = true;
    for (const id of ['accept', 'iterate', 'reject']) document.getElementById(id).disabled = true;
    say(SAY[action], true);
    send(action);
  };
  document.getElementById('accept').addEventListener('click', () => decide('accept'));
  document.getElementById('iterate').addEventListener('click', () => decide('iterate'));
  document.getElementById('reject').addEventListener('click', () => decide('reject'));
  // Play sounds the pane shown, After or Before, in the preview (owner's request 2026-10-25).
  const play = () => {
    say('Starting playback…');
    vscode.postMessage({ action: 'play', which: paneAfter.classList.contains('show') ? 'after' : 'before' });
  };
  document.getElementById('play').addEventListener('click', play);
  // A click on a note (anything the score ties to the source) plays the pane from there.
  document.querySelector('.main').addEventListener('click', (e) => {
    const el = e.target && e.target.closest ? e.target.closest('[data-pos]') : null;
    if (!el) { return; }
    const from = parseInt(el.getAttribute('data-pos'), 10);
    if (isNaN(from)) { return; }
    vscode.postMessage({ action: 'play', which: paneAfter.classList.contains('show') ? 'after' : 'before', from });
  });
  document.getElementById('stop').addEventListener('click', () => { send('stop'); if (!decided) say('Stopped.'); });
  window.addEventListener('message', (e) => {
    if (!e.data) return;
    // Playback news is not shown over a decision being carried out.
    if (e.data.type === 'playState' && !decided) { say(e.data.text); }
    else if (e.data.type === 'status') { say(e.data.text, true); }
  });
  window.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); decide('accept'); }
    else if (e.key === 'Escape') { e.preventDefault(); decide('reject'); }
    else if (e.key === 'Tab') { e.preventDefault(); show(!paneAfter.classList.contains('show')); }
    else if (e.key === ' ') { e.preventDefault(); play(); }
  });
</script>
</body>
</html>`;
}

// ======================================================================
// Apply (EditApplier, §7 version/range guard)
// ======================================================================

async function applyCandidate(deps: AiTransformDeps, snapshot: Snapshot, candidate: string): Promise<void> {
    const doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === snapshot.uri.toString());
    if (!doc) {
        vscode.window.showErrorMessage('Lily#: the document was closed; nothing was applied.');
        panelStatus('Not written — the document was closed.');
        return;
    }

    let range: vscode.Range;
    if (doc.version === snapshot.version) {
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
                    panelStatus('Not written — the file changed meanwhile.');
                    return;
                }
                const clampStart = Math.min(snapshot.startOffset, current.length);
                const clampEnd = Math.min(snapshot.endOffset, current.length);
                range = new vscode.Range(doc.positionAt(clampStart), doc.positionAt(clampEnd));
            }
        }
    }

    const edit = new vscode.WorkspaceEdit();
    edit.replace(snapshot.uri, range, candidate);
    const ok = await vscode.workspace.applyEdit(edit);
    if (ok) {
        vscode.window.showInformationMessage('Lily#: applied AI transform (Ctrl+Z to undo).');
        candidatePanel?.dispose();
    } else {
        vscode.window.showErrorMessage('Lily#: the edit could not be applied.');
        panelStatus('Not written — the edit could not be applied.');
    }
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
