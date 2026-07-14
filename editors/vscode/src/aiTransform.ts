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

// ---- Dependencies wired in from extension.ts (keeps the LSP client global there) ----
export interface AiTransformDeps {
    extensionUri: vscode.Uri;
    /** The LanguageClient, or undefined if not started. */
    getClient: () => LanguageClient | undefined;
    /** True once the client has started and is ready to serve requests. */
    isReady: () => boolean;
    /** SecretStorage for BYO-key providers (Anthropic / OpenAI). */
    secrets: vscode.SecretStorage;
    log: (msg: string) => void;
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
let softLockDecoration: vscode.TextEditorDecorationType | undefined;

/** Registers the `lilysharp.aiTransform` command. */
export function registerAiTransform(context: vscode.ExtensionContext, deps: AiTransformDeps): void {
    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.aiTransform', () => runAiTransform(deps))
    );
    context.subscriptions.push({
        dispose: () => {
            candidatePanel?.dispose();
            softLockDecoration?.dispose();
        }
    });
}

/** The whole select→prompt→validate→render→apply transaction (§3/§4). */
async function runAiTransform(deps: AiTransformDeps): Promise<void> {
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
    // single line "just works".
    let sel: vscode.Range = editor.selection;
    if (sel.isEmpty) {
        sel = doc.lineAt(editor.selection.active.line).range;
    }
    const selectedText = doc.getText(sel);
    if (selectedText.trim().length === 0) {
        vscode.window.showErrorMessage('Lily#: select some notation to transform (or place the cursor on a non-empty line).');
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

    const instruction = await vscode.window.showInputBox({
        title: 'Lily# — transform selection with AI',
        prompt: 'Describe the change (natural language)',
        placeHolder: nextPlaceholder(),
        ignoreFocusOut: true,
    });
    if (instruction === undefined || instruction.trim().length === 0) {
        return; // cancelled
    }

    // Resolve the model: the user's Copilot models (vscode.lm) or a BYO key.
    const chat = await resolveChatClient(deps.secrets, false);
    if (!chat) {
        return;
    }

    // Soft-lock the range while we work (§7): non-blocking, purely visual.
    applySoftLock(editor, sel);

    try {
        // Progress lives in the status bar (Window), NOT a Notification toast:
        // drive() awaits the user's decision on the candidate panel from inside
        // this scope, so a Notification would leave a "rendering candidate… /
        // Cancel" toast hanging over the review the whole time. The status-bar
        // spinner is unobtrusive and needs no competing Cancel button — the
        // candidate panel's Reject/Esc is the cancel affordance.
        await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Window, title: 'Lily#: AI transform', cancellable: false },
            // Use the token withProgress hands us — `vscode.CancellationToken` is a TYPE,
            // not a runtime object, so `vscode.CancellationToken.None` throws
            // "Cannot read properties of undefined (reading 'None')".
            async (progress, token) => {
                await drive(deps, client, chat, snapshot, instruction, progress, token);
            }
        );
    } catch (err: any) {
        vscode.window.showErrorMessage(`Lily#: AI transform failed: ${err?.message ?? err}`);
    } finally {
        clearSoftLock();
    }
}

/**
 * Runs the state machine to completion for one transaction. `instruction` is the
 * initial ask; iteration continues the same conversation with refinements.
 */
async function drive(
    deps: AiTransformDeps,
    client: LanguageClient,
    chat: ChatClient,
    snapshot: Snapshot,
    instruction: string,
    progress: vscode.Progress<{ message?: string }>,
    token: vscode.CancellationToken,
): Promise<void> {
    const grammar = await loadGrammar(deps);
    // Quality log (§M5) — local only, no telemetry.
    deps.log(`AI transform: "${instruction}" via ${chat.label}`);

    // Resolved facts of the selection (§5): un-blindfold the model. Best-effort.
    progress.report({ message: 'reading resolved facts…' });
    const facts = await getFacts(client, snapshot, token);

    // Baseline error count of the untouched document, so we only reject a candidate
    // that makes things WORSE (a pre-existing error elsewhere isn't the model's fault).
    const baselineErrors = await countErrors(client, snapshot.origFullText, token);

    // Render the untouched score once for the before/after comparison (§M5).
    const renderBefore = await client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: snapshot.origFullText });

    // Conversation seed.
    const messages: ChatMessage[] = [
        { role: 'system', content: systemPrompt(grammar) },
        { role: 'user', content: taskPrompt(snapshot.origSelectedText, facts, instruction) },
    ];

    let iterate = true;
    while (iterate) {
        if (token.isCancellationRequested) return;

        // ----- Generate + validate-and-self-repair (§4 AwaitingModel → Validating → Repairing) -----
        let candidate: string | null = null;
        let repairs = 0;
        for (let attempt = 0; attempt <= MAX_REPAIR_ATTEMPTS; attempt++) {
            if (token.isCancellationRequested) return;
            progress.report({ message: attempt === 0 ? 'generating…' : `repairing (${attempt}/${MAX_REPAIR_ATTEMPTS})…` });

            const raw = await chat.send(messages, token);
            const cleaned = cleanCandidate(raw);
            if (cleaned.length === 0) {
                messages.push({ role: 'assistant', content: raw });
                messages.push({ role: 'user', content:
                    'Your reply was empty. Return ONLY the replacement Lily# text, no commentary, no code fences.' });
                continue;
            }

            progress.report({ message: 'validating candidate…' });
            const reconstructed = spliceCandidate(snapshot, cleaned);
            const check = await client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: reconstructed });
            const badness = candidateBadness(check, snapshot, cleaned, baselineErrors);

            if (badness.length === 0) {
                candidate = cleaned; // valid — never showed a broken candidate
                break;
            }
            repairs = attempt + 1;

            // Self-repair: feed the diagnostics back and try again.
            messages.push({ role: 'assistant', content: cleaned });
            messages.push({ role: 'user', content:
                `That candidate does not compile. The Lily# compiler reported:\n${formatDiags(badness)}\n` +
                `Return a corrected replacement for the selection only — same output rules.` });
        }

        if (candidate === null) {
            // §4 Failed: exhausted repairs — abort WITHOUT showing broken notation.
            deps.log(`AI transform: FAILED validation after ${MAX_REPAIR_ATTEMPTS} repairs — nothing applied.`);
            vscode.window.showWarningMessage(
                'Lily#: the AI could not produce a valid transform after several tries. Nothing was changed.');
            return;
        }
        if (repairs > 0) {
            deps.log(`AI transform: candidate valid after ${repairs} self-repair round(s).`);
        }

        // ----- Render the candidate score and decide on it (§3 [7]) -----
        progress.report({ message: 'rendering candidate…' });
        const reconstructed = spliceCandidate(snapshot, candidate);
        const renderAfter = await client.sendRequest<SvgResponse>('lilysharp/renderText', { Text: reconstructed });
        // Source-offset spans of the change so the panel can highlight WHERE it
        // landed: in the "after" score the candidate occupies [start, start+len);
        // in the "before" score the original selection occupied [start, end).
        // The SVG carries data-pos (source offsets) for editor↔preview sync, so
        // the webview lights up the notes whose data-pos falls in these spans.
        const changed: ChangedSpans = {
            afterLo: snapshot.startOffset,
            afterHi: snapshot.startOffset + candidate.length,
            beforeLo: snapshot.startOffset,
            beforeHi: snapshot.endOffset,
        };
        const decision = await reviewOnScore(deps, renderBefore, renderAfter, instruction, candidate, changed);

        if (decision === 'reject') {
            deps.log('AI transform: rejected on the score.');
            return;
        }
        if (decision === 'iterate') {
            const refine = await vscode.window.showInputBox({
                title: 'Lily# — refine the transform',
                prompt: 'What should change about this candidate?',
                placeHolder: 'e.g. keep the rhythm but a third lower / less busy',
                ignoreFocusOut: true,
            });
            if (refine === undefined || refine.trim().length === 0) {
                return; // treat cancelled refine as done
            }
            deps.log(`AI transform: iterate — "${refine}"`);
            messages.push({ role: 'assistant', content: candidate });
            messages.push({ role: 'user', content:
                `Revise the previous replacement: ${refine}\nReturn ONLY the replacement Lily#, same output rules.` });
            instruction = refine; // caption reflects the latest ask
            continue; // back to generate
        }

        // ----- Accept: apply with the version/range guard (§7) -----
        deps.log('AI transform: accepted.');
        await applyCandidate(deps, snapshot, candidate);
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
        'OUTPUT CONTRACT (strict — the result is applied to the file mechanically):',
        '- Return ONLY the replacement text for the selected fragment.',
        '- No explanations, no commentary, no Markdown, no code fences.',
        '- Rewrite ONLY the selection; do not repeat the surrounding score.',
        '- Preserve the fragment\'s shape: if it spans lines, keep one statement per line and end every measure with "|".',
        '- Never emit LilyPond-only constructs (\\relative, \\new Staff, \\repeat volta, \\version, << \\\\ >>). Annotations use @name, never \\name.',
    ].join('\n');
}

function taskPrompt(selectedText: string, facts: ResolvedPitchFact[], instruction: string): string {
    const parts: string[] = [];
    parts.push('Selected fragment to transform:');
    parts.push('<selection>');
    parts.push(selectedText);
    parts.push('</selection>');
    if (facts.length > 0) {
        parts.push('');
        parts.push('Resolved absolute pitches of the selection (written -> resolved), from the compiler:');
        parts.push(facts.map(f => `  ${f.Written} -> ${f.Resolved}`).join('\n'));
    }
    parts.push('');
    parts.push(`Instruction: ${instruction}`);
    parts.push('');
    parts.push('Return the replacement Lily# for the selection only.');
    return parts.join('\n');
}

// ======================================================================
// Candidate cleaning & validation
// ======================================================================

/** Strips code fences / stray commentary the model may add despite the contract. */
export function cleanCandidate(raw: string): string {
    let t = raw.trim();
    // Whole reply wrapped in a fenced block -> take the inner content.
    const fence = t.match(/^```[a-zA-Z0-9]*\s*\n([\s\S]*?)\n?```$/);
    if (fence) {
        t = fence[1];
    } else {
        // Or just strip a leading ```lang and a trailing ``` if present unbalanced.
        t = t.replace(/^```[a-zA-Z0-9]*\s*\n?/, '').replace(/\n?```\s*$/, '');
    }
    return t.replace(/\s+$/, '').replace(/^\n+/, '');
}

/** Rebuilds the full document text with the candidate spliced into the snapshot range. */
function spliceCandidate(snapshot: Snapshot, candidate: string): string {
    return snapshot.origFullText.slice(0, snapshot.startOffset)
        + candidate
        + snapshot.origFullText.slice(snapshot.endOffset);
}

async function countErrors(client: LanguageClient, text: string, token: vscode.CancellationToken): Promise<number> {
    try {
        const check = await client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: text });
        return check.Diagnostics.filter(d => d.Severity === 'error').length;
    } catch {
        return 0;
    }
}

/**
 * Decides whether a candidate is "broken" and returns the diagnostics to feed back
 * for repair. A candidate is broken only if it INTRODUCES errors: more total errors
 * than the untouched document, or an error inside the replaced span. Pre-existing
 * errors elsewhere are not the candidate's fault, so they don't block it.
 */
function candidateBadness(
    check: CheckCandidateResponse,
    snapshot: Snapshot,
    candidate: string,
    baselineErrors: number,
): CandidateDiagnostic[] {
    const errors = check.Diagnostics.filter(d => d.Severity === 'error');
    const candStart = snapshot.startOffset;
    const candEnd = snapshot.startOffset + candidate.length;
    const inRange = errors.filter(d => d.Offset >= candStart && d.Offset < candEnd);
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

// ======================================================================
// Decide on the score (candidate preview webview)
// ======================================================================

type Decision = 'accept' | 'iterate' | 'reject';

/** Source-offset spans of the change, for highlighting in each pane. */
interface ChangedSpans {
    afterLo: number; afterHi: number;
    beforeLo: number; beforeHi: number;
}

async function reviewOnScore(
    deps: AiTransformDeps,
    renderBefore: SvgResponse,
    renderAfter: SvgResponse,
    caption: string,
    candidate: string,
    changed: ChangedSpans,
): Promise<Decision> {
    const panel = ensureCandidatePanel(deps);
    const fontUri = panel.webview.asWebviewUri(
        vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts', 'emmentaler-20.woff2'));
    const braceFontUri = panel.webview.asWebviewUri(
        vscode.Uri.joinPath(deps.extensionUri, 'media', 'fonts', 'emmentaler-brace.woff2'));

    panel.webview.html = getCandidateHtml(
        fontUri.toString(), braceFontUri.toString(), panel.webview.cspSource, getNonce(),
        renderBefore.Svg, renderAfter.Svg, renderAfter.Error, caption, candidate, changed);
    panel.reveal(vscode.ViewColumn.Beside, true);

    return await new Promise<Decision>(resolve => {
        let settled = false;
        const finish = (d: Decision) => { if (!settled) { settled = true; resolve(d); } };
        const sub = panel.webview.onDidReceiveMessage((m: any) => {
            if (m && (m.action === 'accept' || m.action === 'iterate' || m.action === 'reject')) {
                sub.dispose();
                finish(m.action);
            }
        });
        // Closing the panel counts as reject.
        const disp = panel.onDidDispose(() => { disp.dispose(); finish('reject'); });
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
        { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [fontsUri] });
    panel.onDidDispose(() => { candidatePanel = undefined; });
    candidatePanel = panel;
    return panel;
}

function getCandidateHtml(
    fontUri: string, braceFontUri: string, cspSource: string, nonce: string,
    svgBefore: string | null, svgAfter: string | null, error: string | null,
    caption: string, candidate: string, changed: ChangedSpans,
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
.spacer { flex:1; }
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
  <button class="primary" id="accept">Accept (Enter)</button>
  <button id="iterate">Iterate…</button>
  <div class="spacer"></div>
  <button id="reject">Reject (Esc)</button>
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
  function markChanged(pane, lo, hi) {
    let first = null;
    pane.querySelectorAll('[data-pos]').forEach(el => {
      const pos = parseInt(el.getAttribute('data-pos'), 10);
      if (!isNaN(pos) && pos >= lo && pos < hi) {
        el.classList.add('chg');
        if (first === null) first = el;
      }
    });
    return first;
  }
  const firstAfter = markChanged(paneAfter, CHANGED.afterLo, CHANGED.afterHi);
  const firstBefore = markChanged(paneBefore, CHANGED.beforeLo, CHANGED.beforeHi);
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
  document.getElementById('accept').addEventListener('click', () => send('accept'));
  document.getElementById('iterate').addEventListener('click', () => send('iterate'));
  document.getElementById('reject').addEventListener('click', () => send('reject'));
  window.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); send('accept'); }
    else if (e.key === 'Escape') { e.preventDefault(); send('reject'); }
    else if (e.key === 'Tab') { e.preventDefault(); show(!paneAfter.classList.contains('show')); }
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
    }
}

// ======================================================================
// Soft lock decoration (§7)
// ======================================================================

function applySoftLock(editor: vscode.TextEditor, range: vscode.Range): void {
    clearSoftLock();
    softLockDecoration = vscode.window.createTextEditorDecorationType({
        backgroundColor: new vscode.ThemeColor('editor.wordHighlightBackground'),
        isWholeLine: false,
        overviewRulerColor: new vscode.ThemeColor('editorInfo.foreground'),
        overviewRulerLane: vscode.OverviewRulerLane.Full,
    });
    editor.setDecorations(softLockDecoration, [range]);
}

function clearSoftLock(): void {
    if (softLockDecoration) {
        softLockDecoration.dispose();
        softLockDecoration = undefined;
    }
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
