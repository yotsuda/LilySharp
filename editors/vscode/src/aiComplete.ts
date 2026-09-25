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

// Lily# - AI collaborative editing (docs/ai-collab-design §8)
//
// The second mode: ghost-text "next measure" completion while you type. As the
// cursor sits just after a barline, the model proposes the next measure; the
// candidate is compiled first (checkCandidate) and only shown if it is VALID — a
// broken completion is never offered. Tab accepts, like any inline suggestion.
//
// select+prompt (aiTransform.ts) is the deliberate transform; this is the
// write-with-the-flow completion. Opt-in and off by default (it calls the model on
// a debounce), gated by `lilysharp.ai.ghostCompletion`.

import * as vscode from 'vscode';
import type { LanguageClient } from 'vscode-languageclient/node';
import { AiTransformDeps, loadGrammar, cleanCandidate } from './aiTransform';
import { resolveChatClient } from './modelClient';

interface CandidateDiagnostic { Severity: string; Offset: number; }
interface CheckCandidateResponse { HasErrors: boolean; Diagnostics: CandidateDiagnostic[]; }

const DEBOUNCE_MS = 300;
const CONTEXT_WINDOW = 4000; // chars of preceding music sent to the model

// Cache the last computed suggestion so VS Code's repeated calls for the same
// document state don't each fire a model request.
let lastKey = '';
let lastText: string | undefined;
let inflightKey = '';
let inflightPromise: Promise<string | undefined> | undefined;

export function registerAiComplete(context: vscode.ExtensionContext, deps: AiTransformDeps): void {
    const provider: vscode.InlineCompletionItemProvider = {
        async provideInlineCompletionItems(document, position, _ctx, token) {
            const cfg = vscode.workspace.getConfiguration('lilysharp');
            if (!cfg.get<boolean>('ai.ghostCompletion', false)) {
                return undefined;
            }
            if (document.languageId !== 'lilysharp') {
                return undefined;
            }

            // Heuristic trigger: the cursor is at the end of the line and the text
            // before it closes a measure (ends with a barline). That's exactly the
            // "what comes next" moment; anywhere else would be noise.
            const line = document.lineAt(position.line);
            if (position.character !== line.text.length) {
                return undefined;
            }
            const prefix = line.text.substring(0, position.character);
            if (!/\|\s*$/.test(prefix)) {
                return undefined;
            }

            const client = deps.getClient();
            if (!client || !deps.isReady()) {
                return undefined;
            }

            const offset = document.offsetAt(position);
            const key = `${document.uri.toString()}:${document.version}:${offset}`;

            // Serve a cached suggestion for the identical state.
            if (key === lastKey) {
                return lastText ? [makeItem(lastText, position)] : undefined;
            }
            // Coalesce concurrent requests for the same state.
            if (key === inflightKey && inflightPromise) {
                const t = await inflightPromise;
                return t ? [makeItem(t, position)] : undefined;
            }

            inflightKey = key;
            inflightPromise = computeSuggestion(deps, client, document, position, offset, token);
            const text = await inflightPromise;
            if (inflightKey === key) {
                inflightKey = '';
                inflightPromise = undefined;
            }
            // Only cache a settled (non-cancelled) result.
            if (!token.isCancellationRequested) {
                lastKey = key;
                lastText = text;
            }
            return text ? [makeItem(text, position)] : undefined;
        },
    };

    context.subscriptions.push(
        vscode.languages.registerInlineCompletionItemProvider({ language: 'lilysharp' }, provider)
    );

    // Turning the setting on must not be a switch that does nothing: offer the settings it
    // depends on when it is switched on, and once at start-up if it was on already.
    void offerInlineSuggest(context, false);
    context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(e => {
        if (e.affectsConfiguration('lilysharp.ai.ghostCompletion')) {
            void offerInlineSuggest(context, true);
        }
    }));
}

const DECLINED_KEY = 'lilysharp.ghostCompletion.inlineSuggestDeclined';

/**
 * Ghost completion is an inline suggestion, and the extension's own default turns inline
 * suggestions off in Lily# files — `"[lilysharp]": { "editor.inlineSuggest.enabled": false }`,
 * because GitHub Copilot's inline completion stalled every keystroke in a score for one to
 * two seconds (see CHANGELOG). With that default in force VS Code never asks this provider,
 * so the setting was on and nothing ever appeared (owner report, 2026-09-26).
 *
 * The fix is the user's to consent to, and it is two settings, not one: inline suggestions
 * back on for Lily# files only, AND Copilot's own completions off for them, so the stall the
 * default was there for does not come back with the ghost text. Nothing is written without
 * the click. A "Not now" is remembered until the setting is switched on again.
 */
async function offerInlineSuggest(context: vscode.ExtensionContext, switchedNow: boolean): Promise<void> {
    if (!vscode.workspace.getConfiguration('lilysharp').get<boolean>('ai.ghostCompletion', false)) {
        return;
    }
    const editorCfg = vscode.workspace.getConfiguration('editor', { languageId: 'lilysharp' });
    if (editorCfg.get<boolean>('inlineSuggest.enabled', true)) {
        return; // already on for Lily# files — nothing is in the way
    }
    if (!switchedNow && context.globalState.get<boolean>(DECLINED_KEY, false)) {
        return;
    }
    const copilot = vscode.extensions.all.some(x => x.id.toLowerCase().startsWith('github.copilot'));
    const turnOn = 'Turn on for Lily# files';
    const answer = await vscode.window.showInformationMessage(
        'Lily#: Ghost Completion needs inline suggestions, which are off in Lily# files. '
        + (copilot
            ? 'Turn them on for Lily# files, and keep GitHub Copilot\'s own completions off there so typing stays fast?'
            : 'Turn them on for Lily# files?'),
        turnOn, 'Not now');
    if (answer !== turnOn) {
        await context.globalState.update(DECLINED_KEY, true);
        return;
    }
    await context.globalState.update(DECLINED_KEY, false);
    try {
        // overrideInLanguage = true: written under "[lilysharp]", nowhere else.
        await editorCfg.update('inlineSuggest.enabled', true, vscode.ConfigurationTarget.Global, true);
        if (copilot) {
            const copilotCfg = vscode.workspace.getConfiguration('github.copilot');
            const seen = copilotCfg.inspect<Record<string, boolean>>('enable');
            // The whole map is written back, the user's own entries kept, so no other
            // language's setting changes.
            const map = { ...(seen?.defaultValue ?? {}), ...(seen?.globalValue ?? {}), lilysharp: false };
            await copilotCfg.update('enable', map, vscode.ConfigurationTarget.Global);
        }
        vscode.window.showInformationMessage(copilot
            ? 'Lily#: inline suggestions are on in Lily# files, GitHub Copilot\'s are off there. Ghost Completion appears after a barline.'
            : 'Lily#: inline suggestions are on in Lily# files. Ghost Completion appears after a barline.');
    } catch (err: any) {
        vscode.window.showErrorMessage(`Lily#: could not change the settings: ${err?.message ?? err}`);
    }
}

function makeItem(text: string, position: vscode.Position): vscode.InlineCompletionItem {
    return new vscode.InlineCompletionItem(text, new vscode.Range(position, position));
}

async function computeSuggestion(
    deps: AiTransformDeps,
    client: LanguageClient,
    document: vscode.TextDocument,
    position: vscode.Position,
    offset: number,
    token: vscode.CancellationToken,
): Promise<string | undefined> {
    // Debounce: let the user keep typing before we spend a model call.
    await delay(DEBOUNCE_MS);
    if (token.isCancellationRequested) {
        return undefined;
    }

    // Quiet resolution: never prompt or pop errors mid-typing.
    const chat = await resolveChatClient(true);
    if (!chat) {
        return undefined;
    }

    const grammar = await loadGrammar(deps);
    const docText = document.getText();
    const from = Math.max(0, offset - CONTEXT_WINDOW);
    const contextText = (from > 0 ? '…' : '') + docText.substring(from, offset);

    let raw: string;
    try {
        raw = await chat.send([
            { role: 'system', content: systemPrompt(grammar) },
            { role: 'user', content:
                `Here is the music so far (the cursor is at the very end):\n<music>\n${contextText}\n</music>\n` +
                `Write ONLY the next measure.` },
        ], token);
    } catch {
        return undefined; // no consent / offline / quota — silently offer nothing
    }
    if (token.isCancellationRequested) {
        return undefined;
    }

    // First line only (one measure per line), cleaned of fences/commentary.
    let measure = cleanCandidate(raw).split('\n')[0].trim();
    if (measure.length === 0) {
        return undefined;
    }
    if (!measure.endsWith('|')) {
        measure += ' |';
    }

    // Space it off the preceding barline if the model didn't.
    const before = offset > 0 ? docText[offset - 1] : '\n';
    const insertText = (before === ' ' || before === '\t' || before === '\n') ? measure : ' ' + measure;

    // Validate-before-show: compile the doc WITH the completion spliced in and reject
    // if the completion itself introduces an error. A broken bar is never offered.
    const spliced = docText.slice(0, offset) + insertText + docText.slice(offset);
    try {
        const check = await client.sendRequest<CheckCandidateResponse>('lilysharp/checkCandidate', { Text: spliced });
        if (token.isCancellationRequested) {
            return undefined;
        }
        const end = offset + insertText.length;
        const brokeItself = check.Diagnostics.some(
            d => d.Severity === 'error' && d.Offset >= offset && d.Offset < end);
        if (brokeItself) {
            return undefined;
        }
    } catch {
        return undefined;
    }

    return insertText;
}

function systemPrompt(grammar: string): string {
    return [
        'You autocomplete a musical score written in Lily#, a text notation language. It is NOT LilyPond.',
        '',
        '<lilysharp-grammar>',
        grammar,
        '</lilysharp-grammar>',
        '',
        'Continue the music with exactly ONE next measure that follows naturally from what precedes it.',
        'OUTPUT CONTRACT (strict — inserted verbatim at the cursor):',
        '- Output ONLY the next measure: notes/rests/chords and a trailing "|".',
        '- One line. No explanation, no commentary, no Markdown, no code fences.',
        '- Do NOT repeat the existing music.',
        '- Obey the grammar (no \\relative or other LilyPond-only constructs; annotations use @name, never \\name).',
    ].join('\n');
}

function delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
}
