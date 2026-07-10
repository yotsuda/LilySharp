// Lily# - AI collaborative editing (docs/ai-collab-design §2, M5 BYO-key)
//
// A thin abstraction over "given chat messages, return the model's text" so the AI
// features work with EITHER the user's GitHub Copilot models (vscode.lm) OR a
// bring-your-own key called directly (Anthropic / OpenAI). The key lives in VS Code
// SecretStorage; the provider is chosen by `lilysharp.ai.provider` (default auto:
// Copilot if available, else whichever BYO key is set).

import * as vscode from 'vscode';

export type ChatRole = 'system' | 'user' | 'assistant';
export interface ChatMessage { role: ChatRole; content: string; }

export interface ChatClient {
    /** Human-readable label for logs, e.g. "copilot/claude-3.5" or "anthropic". */
    readonly label: string;
    /** Runs the messages and returns the full response text. */
    send(messages: ChatMessage[], token: vscode.CancellationToken): Promise<string>;
}

const SECRET_KEYS: Record<string, string> = {
    anthropic: 'lilysharp.ai.anthropic.key',
    openai: 'lilysharp.ai.openai.key',
};

const DEFAULT_MODELS: Record<string, string> = {
    anthropic: 'claude-sonnet-5',
    openai: 'gpt-4.1',
};

// --------------------------------------------------------------------------
// Resolution
// --------------------------------------------------------------------------

/**
 * Resolves the chat client to use, honoring `lilysharp.ai.provider`:
 * - `copilot`  → vscode.lm only.
 * - `anthropic`/`openai` → the BYO key only.
 * - `auto` (default) → vscode.lm if it has a model, else a BYO key if one is set.
 * Returns undefined if nothing is available. When `quiet` (ghost completion), never
 * prompts or shows errors.
 */
export async function resolveChatClient(
    secrets: vscode.SecretStorage,
    quiet: boolean,
): Promise<ChatClient | undefined> {
    const cfg = vscode.workspace.getConfiguration('lilysharp');
    const provider = cfg.get<string>('ai.provider', 'auto');
    const modelOverride = cfg.get<string>('ai.model', '') || undefined;

    const tryLm = async (): Promise<ChatClient | undefined> => {
        const model = await selectLmModel();
        return model ? lmClient(model) : undefined;
    };
    const tryByo = async (name: 'anthropic' | 'openai'): Promise<ChatClient | undefined> => {
        const key = await secrets.get(SECRET_KEYS[name]);
        if (!key) {
            return undefined;
        }
        return name === 'anthropic'
            ? anthropicClient(key, modelOverride ?? DEFAULT_MODELS.anthropic)
            : openaiClient(key, modelOverride ?? DEFAULT_MODELS.openai);
    };

    let client: ChatClient | undefined;
    switch (provider) {
        case 'copilot':
            client = await tryLm();
            break;
        case 'anthropic':
            client = await tryByo('anthropic');
            break;
        case 'openai':
            client = await tryByo('openai');
            break;
        default: // auto
            client = (await tryLm()) ?? (await tryByo('anthropic')) ?? (await tryByo('openai'));
            break;
    }

    if (!client && !quiet) {
        const pick = await vscode.window.showErrorMessage(
            'Lily#: no language model available. Use GitHub Copilot, or set an API key.',
            'Set API key…');
        if (pick === 'Set API key…') {
            await vscode.commands.executeCommand('lilysharp.setAiKey');
            return resolveChatClient(secrets, quiet);
        }
    }
    return client;
}

// --------------------------------------------------------------------------
// vscode.lm (Copilot) client
// --------------------------------------------------------------------------

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

// --------------------------------------------------------------------------
// Anthropic client (direct)
// --------------------------------------------------------------------------

function anthropicClient(apiKey: string, model: string): ChatClient {
    return {
        label: `anthropic/${model}`,
        async send(messages, token) {
            // The first system message maps to Anthropic's top-level `system`; the
            // rest are user/assistant turns.
            const system = messages.filter(m => m.role === 'system').map(m => m.content).join('\n\n');
            const turns = messages.filter(m => m.role !== 'system')
                .map(m => ({ role: m.role, content: m.content }));
            const body = JSON.stringify({
                model,
                max_tokens: 1024,
                ...(system ? { system } : {}),
                messages: turns,
            });
            const data = await postJson('https://api.anthropic.com/v1/messages', {
                'content-type': 'application/json',
                'x-api-key': apiKey,
                'anthropic-version': '2023-06-01',
            }, body, token);
            // content is an array of blocks; concatenate the text ones.
            const blocks = Array.isArray(data?.content) ? data.content : [];
            return blocks.filter((b: any) => b?.type === 'text').map((b: any) => b.text).join('');
        },
    };
}

// --------------------------------------------------------------------------
// OpenAI client (direct)
// --------------------------------------------------------------------------

function openaiClient(apiKey: string, model: string): ChatClient {
    return {
        label: `openai/${model}`,
        async send(messages, token) {
            const body = JSON.stringify({
                model,
                messages: messages.map(m => ({ role: m.role, content: m.content })),
            });
            const data = await postJson('https://api.openai.com/v1/chat/completions', {
                'content-type': 'application/json',
                'authorization': `Bearer ${apiKey}`,
            }, body, token);
            return data?.choices?.[0]?.message?.content ?? '';
        },
    };
}

// --------------------------------------------------------------------------
// HTTP helper
// --------------------------------------------------------------------------

async function postJson(
    url: string, headers: Record<string, string>, body: string, token: vscode.CancellationToken,
): Promise<any> {
    const ac = new AbortController();
    const sub = token.onCancellationRequested(() => ac.abort());
    try {
        const resp = await fetch(url, { method: 'POST', headers, body, signal: ac.signal });
        if (!resp.ok) {
            const detail = await resp.text().catch(() => '');
            throw new Error(`${resp.status} ${resp.statusText}${detail ? ` — ${detail.slice(0, 300)}` : ''}`);
        }
        return await resp.json();
    } finally {
        sub.dispose();
    }
}

// --------------------------------------------------------------------------
// Key management commands
// --------------------------------------------------------------------------

export function registerKeyCommands(context: vscode.ExtensionContext): void {
    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.setAiKey', async () => {
            const provider = await vscode.window.showQuickPick(
                [
                    { label: 'Anthropic (Claude)', value: 'anthropic' },
                    { label: 'OpenAI (GPT)', value: 'openai' },
                ],
                { title: 'Lily# — set AI API key', placeHolder: 'Which provider?' });
            if (!provider) {
                return;
            }
            const key = await vscode.window.showInputBox({
                title: `Lily# — ${provider.label} API key`,
                prompt: 'Stored securely in VS Code SecretStorage (never written to settings).',
                password: true,
                ignoreFocusOut: true,
            });
            if (key === undefined || key.trim().length === 0) {
                return;
            }
            await context.secrets.store(SECRET_KEYS[provider.value], key.trim());
            vscode.window.showInformationMessage(
                `Lily#: ${provider.label} key saved. Set "lilysharp.ai.provider" to use it (auto picks it up when Copilot is absent).`);
        }),
        vscode.commands.registerCommand('lilysharp.clearAiKey', async () => {
            await context.secrets.delete(SECRET_KEYS.anthropic);
            await context.secrets.delete(SECRET_KEYS.openai);
            vscode.window.showInformationMessage('Lily#: cleared stored AI API keys.');
        })
    );
}
