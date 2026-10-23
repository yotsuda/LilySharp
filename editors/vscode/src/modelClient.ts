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

// Lily# - AI collaborative editing (docs/ai-collab-design §2)
//
// A thin abstraction over "given chat messages, return the model's text". The AI
// features use VS Code's built-in Language Model API (vscode.lm) — i.e. whatever model
// the user has through GitHub Copilot (or another extension that registers a provider).
// There is no Lily#-specific provider/model/key configuration: the model follows VS
// Code, and authentication is Copilot's, so nothing is stored in settings or SecretStorage.

import * as vscode from 'vscode';

export type ChatRole = 'system' | 'user' | 'assistant';
/** A picture a user message carries (the scan of a bar, docs/omr-ai-fix-design.md §3.1 ④). */
export interface ChatImage { readonly data: Uint8Array; readonly mime: string; }
export interface ChatMessage { role: ChatRole; content: string; images?: readonly ChatImage[]; }

export interface ChatClient {
    /** Human-readable label for logs, e.g. "copilot/claude-3.5". */
    readonly label: string;
    /** Runs the messages and returns the full response text. */
    send(messages: ChatMessage[], token: vscode.CancellationToken): Promise<string>;
    /** Whether the last `send` carried the images its messages offered: false when this VS Code
     *  has no image parts or the model refused them (the send went again as text, and every
     *  later one goes as text), undefined when none were offered. */
    sentImages?(): boolean | undefined;
}

/**
 * Resolves the chat client from VS Code's language-model API. Returns undefined when no
 * model is available (Copilot not enabled / no provider registered). When `quiet` (ghost
 * completion), never shows an error.
 */
export async function resolveChatClient(quiet: boolean, use: ModelUse = 'transform'): Promise<ChatClient | undefined> {
    const model = await selectLmModel(use);
    if (model) {
        return lmClient(model);
    }
    if (!quiet) {
        vscode.window.showErrorMessage(
            'Lily#: no language model available. Enable GitHub Copilot (or another VS Code '
            + 'language-model provider) and pick a model.');
    }
    return undefined;
}

/**
 * What the model is for. The two want opposite things: Transform Selection is one deliberate
 * request, where a strong model is worth a few seconds; Ghost Completion answers as you type,
 * and VS Code cancels an inline suggestion the moment the caret moves — the automatic pick of
 * the strongest model (claude-fable-5.1) took 1.9–3.2 s a bar and every one was cancelled
 * before it could show (owner's log, 2026-09-26). So each has its own setting and its own
 * automatic choice. Fixing OMR marks (docs/omr-ai-fix-design.md §4) is a deliberate request too,
 * and one that reads notation, so it wants the strong model as the transform does, under its own
 * setting: a person may well pick a faster model for a batch of twenty-five bars.
 */
export type ModelUse = 'transform' | 'ghost' | 'omrFix';

const SETTING: Record<ModelUse, string> = { transform: 'ai.model', ghost: 'ai.ghostModel', omrFix: 'ai.omrFixModel' };

async function selectLmModel(use: ModelUse): Promise<vscode.LanguageModelChat | undefined> {
    const models = await availableModels();
    if (models.length === 0) {
        return undefined;
    }
    // The user's pick (written by "Lily#: Select AI Model") wins, matched by id and then by
    // family, so a pick survives a model's version bump.
    const wanted = vscode.workspace.getConfiguration('lilysharp').get<string>(SETTING[use], '').trim();
    if (wanted) {
        const hit = models.find(m => m.id === wanted) ?? models.find(m => m.family === wanted);
        if (hit) {
            return hit;
        }
    }
    return autoPick(models, use);
}

/** Every chat model VS Code offers, Copilot's first. */
export async function availableModels(): Promise<vscode.LanguageModelChat[]> {
    if (!vscode.lm || typeof vscode.lm.selectChatModels !== 'function') {
        return [];
    }
    try {
        const copilot = await vscode.lm.selectChatModels({ vendor: 'copilot' });
        const all = await vscode.lm.selectChatModels();
        const rest = all.filter(m => !copilot.some(c => c.id === m.id));
        return [...copilot, ...rest];
    } catch {
        return [];
    }
}

/**
 * The model to use when the user has not picked one. It used to be `models[0]`, which on a
 * Copilot account is `gpt-4o-mini` (owner's log, 2026-09-26): the smallest model on offer,
 * asked to write a language it has never seen from a 52 KB spec. A small model's family name
 * says so (mini, nano, lite, haiku, flash), so those are passed over while anything else is
 * on offer, and the rest are ordered by how much input they take. For Ghost Completion it is
 * the other way round: the small ones are the ones that answer before VS Code gives up on
 * the suggestion. Both are guesses from the name; "Lily#: Select AI Model" is the way to be sure.
 */
export function autoPick(models: readonly vscode.LanguageModelChat[], use: ModelUse = 'transform'): vscode.LanguageModelChat | undefined {
    const small = (m: vscode.LanguageModelChat) => /mini|nano|lite|haiku|flash|small/i.test(`${m.family} ${m.id}`);
    const wantSmall = use === 'ghost';
    const pool = models.some(m => small(m) === wantSmall) ? models.filter(m => small(m) === wantSmall) : [...models];
    return pool.sort((a, b) => (b.maxInputTokens ?? 0) - (a.maxInputTokens ?? 0))[0];
}

/**
 * "Lily#: Select AI Model" — first which feature, then the models VS Code offers; the pick is
 * saved to `lilysharp.ai.model` (Transform Selection), `lilysharp.ai.ghostModel` (Ghost
 * Completion) or `lilysharp.ai.omrFixModel` (Fix OMR marks).
 */
export async function pickAiModel(): Promise<void> {
    const models = await availableModels();
    if (models.length === 0) {
        vscode.window.showErrorMessage(
            'Lily#: no language model available. Enable GitHub Copilot (or another VS Code '
            + 'language-model provider).');
        return;
    }
    const cfg = vscode.workspace.getConfiguration('lilysharp');
    const shown = (use: ModelUse) => cfg.get<string>(SETTING[use], '').trim() || `Automatic (${autoPick(models, use)?.name ?? 'none'})`;
    type UseItem = vscode.QuickPickItem & { use: ModelUse };
    const which = await vscode.window.showQuickPick<UseItem>([
        { label: 'Transform Selection with AI', description: shown('transform'), detail: 'One request at a time: a strong model is worth waiting for.', use: 'transform' },
        { label: 'Ghost Completion', description: shown('ghost'), detail: 'Answers as you type: a fast model, or the suggestion is cancelled before it shows.', use: 'ghost' },
        { label: 'Fix OMR marks with AI', description: shown('omrFix'), detail: 'One request a bar, on request only: a strong model reads the notation best; a faster one shortens a long batch.', use: 'omrFix' },
    ], { title: 'Lily# — choose the AI model for…' });
    if (!which) {
        return;
    }
    const use = which.use;
    const current = cfg.get<string>(SETTING[use], '').trim();
    const auto = autoPick(models, use);
    type Item = vscode.QuickPickItem & { value: string };
    const items: Item[] = [
        {
            label: 'Automatic',
            description: auto ? `now ${auto.name}` : undefined,
            detail: use === 'ghost'
                ? 'Lily# chooses a fast model: the small ones (mini, nano, lite, …) first.'
                : 'Lily# chooses: the largest model on offer, passing over the small ones (mini, nano, lite, …).',
            value: '',
            picked: current === '',
        },
        ...models.map(m => ({
            label: m.name,
            description: `${m.vendor}/${m.family}${m.id === current || m.family === current ? '  (current)' : ''}`,
            detail: m.maxInputTokens ? `${Math.round(m.maxInputTokens / 1000)}K input tokens` : undefined,
            value: m.id,
        })),
    ];
    const chosen = await vscode.window.showQuickPick(items, {
        title: `Lily# — AI model for ${which.label}`,
        placeHolder: current ? `current: ${current}` : 'current: Automatic',
    });
    if (!chosen) {
        return;
    }
    await cfg.update(SETTING[use], chosen.value, vscode.ConfigurationTarget.Global);
}

function lmClient(model: vscode.LanguageModelChat): ChatClient {
    // Image parts came to the API after the extension's floor (engines.vscode ^1.90): looked
    // for at run time, so an older VS Code sends the text alone. A model that takes no image
    // answers the request with an error; the request then goes again as text, and the client
    // remembers (`refused`) so a batch of bars does not pay the error every time.
    const api = vscode as unknown as { LanguageModelDataPart?: { image(data: Uint8Array, mime: string): unknown }; LanguageModelTextPart?: new (value: string) => unknown };
    const canImage = typeof api.LanguageModelDataPart?.image === 'function' && typeof api.LanguageModelTextPart === 'function';
    let refused = false;
    let last: boolean | undefined;
    const build = (messages: ChatMessage[], images: boolean) => messages.map(m => {
        // vscode.lm has only User/Assistant roles; a system message rides as the first User message.
        if (m.role === 'assistant') {
            return vscode.LanguageModelChatMessage.Assistant(m.content);
        }
        if (images && m.images && m.images.length > 0) {
            const parts = [new api.LanguageModelTextPart!(m.content), ...m.images.map(i => api.LanguageModelDataPart!.image(i.data, i.mime))];
            return vscode.LanguageModelChatMessage.User(parts as any);
        }
        return vscode.LanguageModelChatMessage.User(m.content);
    });
    const run = async (lmMessages: vscode.LanguageModelChatMessage[], token: vscode.CancellationToken) => {
        const response = await model.sendRequest(lmMessages, {}, token);
        let text = '';
        for await (const chunk of response.text) {
            text += chunk;
        }
        return text;
    };
    return {
        label: `${model.vendor}/${model.family}`,
        async send(messages, token) {
            const offered = messages.some(m => m.images && m.images.length > 0);
            const withImages = offered && canImage && !refused;
            try {
                const text = await run(build(messages, withImages), token);
                last = offered ? withImages : undefined;
                return text;
            } catch (err) {
                if (!withImages || token.isCancellationRequested) {
                    throw err;
                }
                refused = true;
                last = false;
                return await run(build(messages, false), token);
            }
        },
        sentImages: () => last,
    };
}
