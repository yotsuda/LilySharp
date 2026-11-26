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

// MIDI out — the half that talks to VS Code (docs/playback-design.md §6): the setting that
// names the port, the requests to the server's player one after another, a port's error said
// once, and the picker behind `Lily#: Choose MIDI Output…`. It stood in extension.ts until
// session 1297; what it reads of the extension's own state comes through MidiOutDeps.

import * as vscode from 'vscode';
import type { LanguageClient } from 'vscode-languageclient/node';

export interface MidiOutDeps {
    getClient: () => LanguageClient | undefined;
    isReady: () => boolean;
    /** Starts the server if nothing has yet, and waits until it is up. */
    ensureClientReady: () => Promise<void>;
    /** The extension's output channel. */
    outputChannel: vscode.OutputChannel;
}

let outputChannel: vscode.OutputChannel;
let ensureClientReady: MidiOutDeps['ensureClientReady'];
/** The server's client once it is up, else undefined. */
let server: () => LanguageClient | undefined = () => undefined;

/** Called once from activate, before anything here can be asked for. */
export function useMidiOutHost(deps: MidiOutDeps): void {
    ({ outputChannel, ensureClientReady } = deps);
    server = () => { const client = deps.getClient(); return client && deps.isReady() ? client : undefined; };
}

/** `lilysharp.playback.MIDIOutput`: the name of the MIDI output port the sounds go to, or ''
 *  for the preview's own synth. */
export function midiOutputSetting(): string {
    return vscode.workspace.getConfiguration('lilysharp').get<string>('playback.MIDIOutput', '').trim();
}

/** The lilysharp/midiOut requests go one after another: the server dispatches requests
 *  concurrently, and a Stop must not overtake the Play it follows. No server: an error
 *  answer, as the server would give. */
let midiOutChain: Promise<unknown> = Promise.resolve();
export function midiOutRequest<T extends { Error?: string | null }>(method: string, params: unknown): Promise<T> {
    const next = midiOutChain.then(async (): Promise<T> => {
        const client = server();
        if (!client) { return { Error: 'language server not running' } as T; }
        try {
            return await client.sendRequest<T>(method, params);
        } catch (e) {
            return { Error: String(e) } as T;
        }
    });
    midiOutChain = next.catch(() => undefined);
    return next;
}

/** A port that is not there, or refused, is said once per port in the status bar and
 *  every time in the output channel; the preview then plays its own synth. */
const midiOutErrorsTold = new Set<string>();
export function reportMidiOutError(port: string, error: string): void {
    outputChannel.appendLine(`MIDI output "${port}": ${error}`);
    if (midiOutErrorsTold.has(port)) { return; }
    midiOutErrorsTold.add(port);
    vscode.window.setStatusBarMessage(`Lily#: ${error} — playing in the preview (see Lily# Extension output)`, 8000);
}

/** The picker behind `Lily#: Choose MIDI Output…` and the setting's link: the preview's
 *  own synth (empties the setting), or one of the machine's MIDI output ports as the server
 *  lists them now. Written to the user settings as `lilysharp.playback.MIDIOutput`. */
export async function chooseMidiOutput(): Promise<void> {
    const current = midiOutputSetting();
    type Pick = vscode.QuickPickItem & { name: string };
    const items: Pick[] = [{
        label: 'The preview\'s own synth', description: current ? '' : '(current)',
        detail: 'The sound font (Lily#: Choose Sound Font…) — empties the setting', name: '',
    }];
    await ensureClientReady();   // no .lys open yet: the server starts for the list
    const ports = await midiOutRequest<{ Ports?: string[]; Error?: string | null }>('lilysharp/midiOut/ports', {});
    if (ports.Error) {
        outputChannel.appendLine(`MIDI outputs: ${ports.Error}`);
        items.push({ label: 'No MIDI output could be listed', detail: ports.Error, name: current, alwaysShow: true });
    } else {
        for (const name of ports.Ports ?? []) {
            items.push({ label: name, description: name === current ? '(current)' : '',
                detail: /GS Wavetable/i.test(name) ? 'Windows\' own General MIDI synth (plays with no setup)'
                    : /loopMIDI|IAC/i.test(name) ? 'A virtual port: whatever listens on it (a DAW, a VST host) plays'
                    : undefined, name });
        }
        if (current && !(ports.Ports ?? []).includes(current)) {
            items.push({ label: current, description: '(current — not there now)', name: current });
        }
        if ((ports.Ports ?? []).length === 0) {
            items.push({ label: 'This machine lists no MIDI output', detail: 'Install a virtual port (loopMIDI on Windows; an IAC bus in Audio MIDI Setup on a Mac) or plug an interface in', name: current, alwaysShow: true });
        }
    }
    const picked = await vscode.window.showQuickPick(items, {
        title: 'Lily#: where the preview\'s sounds go',
        placeHolder: 'Each part keeps its channel and sends its General MIDI program; the drums are on channel 10',
    });
    if (!picked || picked.name === current) { return; }
    await vscode.workspace.getConfiguration('lilysharp').update('playback.MIDIOutput', picked.name, vscode.ConfigurationTarget.Global);
    midiOutErrorsTold.delete(picked.name);
    vscode.window.setStatusBarMessage(
        picked.name ? `Lily#: the preview plays through ${picked.name}` : 'Lily#: the preview plays its own synth', 5000);
}
