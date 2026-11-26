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

// From a song to a dance (docs/song-to-dance-design.md): the walkthrough's commands, and the next
// step said when a score gets its song.

import * as path from 'path';
import * as vscode from 'vscode';
import { DANCE_GUIDE_CONTEXT, SHOSA_ADD_DANCE, SHOSA_EXTENSION_ID, danceNextActions, danceNextMessage } from './songToDanceCore';

export function registerSongToDance(context: vscode.ExtensionContext): void {
    context.subscriptions.push(
        vscode.commands.registerCommand('lilysharp.songToDance', () => openSongToDance(context)),
        vscode.commands.registerCommand('lilysharp.installShosa', () => installShosa()),
        vscode.commands.registerCommand('lilysharp.danceWithShosa', (uri?: vscode.Uri) => danceWithShosa(uri)),
    );
}

/** The walkthrough, shown once it is asked for (its `when`); where VS Code cannot open it, its page. */
async function openSongToDance(context: vscode.ExtensionContext): Promise<void> {
    await vscode.commands.executeCommand('setContext', DANCE_GUIDE_CONTEXT, true);
    try {
        await vscode.commands.executeCommand('workbench.action.openWalkthrough', `${context.extension.id}#songToDance`, false);
    } catch {
        await vscode.commands.executeCommand('markdown.showPreview', vscode.Uri.joinPath(context.extensionUri, 'walkthrough', 'song-to-dance.md'));
    }
}

async function installShosa(): Promise<void> {
    try {
        await vscode.commands.executeCommand('workbench.extensions.installExtension', SHOSA_EXTENSION_ID);
    } catch {
        // Not reachable from here (offline, or a gallery that does not list it): show the search.
        await vscode.commands.executeCommand('workbench.extensions.search', `@id:${SHOSA_EXTENSION_ID}`);
    }
}

/** Shosa's Add a Dance… on the score (the one given, else the one in the editor, else Shosa asks);
 *  Shosa not installed, it is offered first. */
async function danceWithShosa(uri?: vscode.Uri): Promise<void> {
    const song = uri instanceof vscode.Uri ? uri : undefined;
    if (!vscode.extensions.getExtension(SHOSA_EXTENSION_ID)) {
        const install = 'Install Shosa';
        const chosen = await vscode.window.showInformationMessage(
            'Lily#: dances are put on a score by Shosa, a free extension. Install it, then run Add a Dance… again.', install);
        if (chosen === install) {
            await installShosa();
        }
        return;
    }
    await vscode.commands.executeCommand(SHOSA_ADD_DANCE, song);
}

/** Said when a change gave a score its song (gaveASong): the next step is the dance. Not awaited by
 *  the caller — the message stays until it is closed. */
export async function offerDanceNext(uri: vscode.Uri): Promise<void> {
    const installed = vscode.extensions.getExtension(SHOSA_EXTENSION_ID) !== undefined;
    const actions = danceNextActions(installed);
    const chosen = await vscode.window.showInformationMessage(danceNextMessage(path.basename(uri.fsPath || uri.path), installed), ...actions);
    if (chosen === actions[0]) {
        if (installed) {
            await vscode.commands.executeCommand(SHOSA_ADD_DANCE, uri);
        } else {
            await installShosa();
        }
    } else if (chosen === actions[1]) {
        await vscode.commands.executeCommand('lilysharp.songToDance');
    }
}
