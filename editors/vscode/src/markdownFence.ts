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

// The lys fence in VS Code's built-in Markdown preview (HANDOFF §2F F-mdfence):
// ```lys … ``` in a .md renders as the score, the way a ```mermaid fence renders
// as a diagram. This is the markdown-it plugin VS Code asks for through
// `contributes.markdown.markdownItPlugins` and the `extendMarkdownIt` export.
//
// markdown-it renders SYNCHRONOUSLY and the language server draws ASYNCHRONOUSLY,
// so a fence is keyed by a hash of its text: a hit inlines the picture, a miss
// returns a placeholder (the source stays readable), asks the server for the
// static SVG (lilysharp/renderText, Interactive=false), and when the answer lands
// the preview is refreshed once (`markdown.preview.refresh`) — the second render
// hits. The preview's CSP has no connect-src, so nothing in the webview could
// fetch the picture itself; the fonts come from media/markdown-lys.css
// (`markdown.previewStyles`), as the extension's own preview supplies them.

import * as vscode from 'vscode';
import type { LanguageClient } from 'vscode-languageclient/node';
import { FenceCache, fenceHtml, fenceKey, isLysFence } from './markdownFenceCore';

export interface MarkdownFenceDeps {
    getClient: () => LanguageClient | undefined;
    isReady: () => boolean;
    /** Starts the language client if it is not running yet and resolves once it is
     * (never rejects). A .md with a lys fence is the first thing that needs it. */
    whenReady: () => Promise<void>;
    log: (msg: string) => void;
}

// The slice of markdown-it this plugin touches. The engine is VS Code's and is
// not a dependency of this extension, so the shape is stated here.
interface MdToken { info: string; content: string; }
type FenceRule = (tokens: MdToken[], idx: number, options: unknown, env: unknown, self: unknown) => string;
interface MarkdownIt {
    renderer: { rules: { fence?: FenceRule } };
    use(plugin: (md: MarkdownIt) => void): MarkdownIt;
}

interface SvgResponse { Svg: string | null; Error: string | null; }

const REFRESH_DELAY_MS = 120;

export function createMarkdownFencePlugin(deps: MarkdownFenceDeps): (md: MarkdownIt) => void {
    const cache = new FenceCache();
    let refreshTimer: NodeJS.Timeout | undefined;

    // One refresh for a burst of answers: a document with ten fences must not
    // re-render the preview ten times as the pictures come in.
    function requestRefresh() {
        if (refreshTimer) {
            clearTimeout(refreshTimer);
        }
        refreshTimer = setTimeout(() => {
            refreshTimer = undefined;
            void vscode.commands.executeCommand('markdown.preview.refresh');
        }, REFRESH_DELAY_MS);
    }

    async function render(key: string, code: string) {
        try {
            await deps.whenReady();
            const client = deps.getClient();
            if (!client || !deps.isReady()) {
                cache.set(key, { kind: 'error', message: 'language server not running — reload the window (Developer: Reload Window)' });
                return;
            }
            const response = await client.sendRequest<SvgResponse>('lilysharp/renderText', {
                text: code,
                interactive: false,
                // A fence draws one picture: no score → every part as a staff; two
                // scores or (with none) two forms → an error under the source.
                fence: true,
            });
            if (response.Svg) {
                cache.set(key, { kind: 'svg', svg: response.Svg });
            } else {
                cache.set(key, { kind: 'error', message: response.Error || 'no picture' });
            }
        } catch (err) {
            cache.set(key, { kind: 'error', message: String(err) });
        } finally {
            requestRefresh();
        }
    }

    return (md: MarkdownIt) => {
        deps.log('markdown fence: plugin installed into the Markdown engine');
        const defaultFence = md.renderer.rules.fence;
        md.renderer.rules.fence = (tokens, idx, options, env, self) => {
            const token = tokens[idx];
            if (!isLysFence(token.info)) {
                return defaultFence ? defaultFence(tokens, idx, options, env, self) : '';
            }
            const key = fenceKey(token.content);
            let state = cache.get(key);
            if (!state) {
                state = { kind: 'pending' };
                cache.set(key, state);
                deps.log(`markdown fence: rendering ${key.slice(0, 8)} (${token.content.length} chars)`);
                void render(key, token.content);
            }
            return fenceHtml(state, token.content);
        };
    };
}

/** What activate() returns so VS Code wires the plugin into its Markdown engine. */
export function markdownItExtensionApi(deps: MarkdownFenceDeps): { extendMarkdownIt(md: MarkdownIt): MarkdownIt } {
    const plugin = createMarkdownFencePlugin(deps);
    return {
        extendMarkdownIt(md: MarkdownIt): MarkdownIt {
            return md.use(plugin);
        },
    };
}
