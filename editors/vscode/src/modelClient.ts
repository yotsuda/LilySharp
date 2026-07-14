// Lily# - AI collaborative editing (docs/ai-collab-design §2)
//
// A thin abstraction over "given chat messages, return the model's text". The AI
// features use VS Code's built-in Language Model API (vscode.lm) — i.e. whatever model
// the user has through GitHub Copilot (or another extension that registers a provider).
// There is no Lily#-specific provider/model/key configuration: the model follows VS
// Code, and authentication is Copilot's, so nothing is stored in settings or SecretStorage.

import * as vscode from 'vscode';

export type ChatRole = 'system' | 'user' | 'assistant';
export interface ChatMessage { role: ChatRole; content: string; }

export interface ChatClient {
    /** Human-readable label for logs, e.g. "copilot/claude-3.5". */
    readonly label: string;
    /** Runs the messages and returns the full response text. */
    send(messages: ChatMessage[], token: vscode.CancellationToken): Promise<string>;
}

/**
 * Resolves the chat client from VS Code's language-model API. Returns undefined when no
 * model is available (Copilot not enabled / no provider registered). When `quiet` (ghost
 * completion), never shows an error.
 */
export async function resolveChatClient(quiet: boolean): Promise<ChatClient | undefined> {
    const model = await selectLmModel();
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

async function selectLmModel(): Promise<vscode.LanguageModelChat | undefined> {
    if (!vscode.lm || typeof vscode.lm.selectChatModels !== 'function') {
        return undefined;
    }
    try {
        let models = await vscode.lm.selectChatModels({ vendor: 'copilot' });
        if (!models || models.length === 0) {
            models = await vscode.lm.selectChatModels();
        }
        return models && models.length > 0 ? models[0] : undefined;
    } catch {
        return undefined;
    }
}

function lmClient(model: vscode.LanguageModelChat): ChatClient {
    return {
        label: `${model.vendor}/${model.family}`,
        async send(messages, token) {
            // vscode.lm has only User/Assistant roles; a system message rides as the
            // first User message.
            const lmMessages = messages.map(m =>
                m.role === 'assistant'
                    ? vscode.LanguageModelChatMessage.Assistant(m.content)
                    : vscode.LanguageModelChatMessage.User(m.content));
            const response = await model.sendRequest(lmMessages, {}, token);
            let text = '';
            for await (const chunk of response.text) {
                text += chunk;
            }
            return text;
        },
    };
}
