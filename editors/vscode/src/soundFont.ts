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

// The sound the preview plays with — the half that talks to VS Code (docs/playback-design.md):
// where the synth's files are, which sound font plays (the setting, the Lily# Sound Font
// extension's, the OS's bank), the fonts used before, the pickers behind `Lily#: Choose Sound
// Font…` and a WAV export with no font, and what a preview's webview may load. What is decided
// without an editor is soundFontHistoryCore.ts. It stood in extension.ts until session 1297.

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import { textFontsRoot } from './scoreFonts';
import { SynthUrls } from './previewHtml';
import { cleanHistory, forgetFont, recordFont, samePath, shownFonts } from './soundFontHistoryCore';

let outputChannel: vscode.OutputChannel;

/** Called once from activate: where a setting that names no file is said. */
export function useSoundFontLog(channel: vscode.OutputChannel): void {
    outputChannel = channel;
}

/** Where the preview's SoundFont synth lives: the bundle and the worklet processor
 *  esbuild.js writes to out/synth (docs/playback-design.md). */
function synthRoot(extensionUri: vscode.Uri): vscode.Uri {
    return vscode.Uri.joinPath(extensionUri, 'out', 'synth');
}

/** The sound font the preview plays with: `lilysharp.playback.soundFont` when it names a
 *  file on this machine (a missing one is logged and the default used); else the Lily# Sound
 *  Font extension's, which Lily# brings with it (packagedSoundFont); else the General MIDI bank
 *  the OS ships (Windows' gm.dls, macOS's DLS bank). Null when there is none — Linux with
 *  neither: the preview then plays its oscillator patches.
 *  Owner's decisions: no font in THIS package (2026-10-12), and a good one from the start in a
 *  package of its own (2026-11-11). */
function soundFontFile(extensionUri: vscode.Uri): { file: vscode.Uri; root: vscode.Uri } | null {
    const setting = vscode.workspace.getConfiguration('lilysharp').get<string>('playback.soundFont', '').trim();
    if (setting) {
        if (fs.existsSync(setting)) {
            return { file: vscode.Uri.file(setting), root: vscode.Uri.file(path.dirname(setting)) };
        }
        outputChannel.appendLine(`lilysharp.playback.soundFont: ${setting} does not exist; using the default sound`);
    }
    const fallback = defaultSoundFont(extensionUri);
    return fallback ? { file: vscode.Uri.file(fallback.path), root: vscode.Uri.file(path.dirname(fallback.path)) } : null;
}

/** What plays when the setting is empty, named as the picker shows it: the Lily# Sound Font
 *  extension's font when that is installed (packagedSoundFont), else the OS's bank.
 *  ONE answer for the player and the picker: the picker said "gm.dls" while a local build
 *  played its MuseScore_General, which then had no name in the list (owner's report
 *  2026-10-12).
 *  A developer's build used to read a font under its own media/soundfont first. That is gone
 *  (owner's decision 2026-11-11): with the Sound Font extension there, it served nothing a
 *  setting does not, and it kept a developer's machine off the road every user's takes. */
function defaultSoundFont(_extensionUri: vscode.Uri): { label: string; path: string } | null {
    return packagedSoundFont() ?? osSoundFonts()[0] ?? null;
}

/** The Lily# Sound Font extension (editors/vscode-soundfont): MuseScore_General.sf3 as its authors
 *  published it (MIT), in a package of its own. Owner's decision 2026-11-11, revising the one of
 *  2026-10-12 (no font in the VSIX; the OS's bank by default): a thin sound makes the whole
 *  product look thin, so a good one is to be there from the start — but not in THIS package,
 *  which is downloaded whole at every update: a font does not change, and in its own package it
 *  is downloaded once. */
const SOUND_FONT_EXTENSION = 'yotsuda.lilysharp-soundfont';

/** The font that extension carries, when it is installed: what plays by default then, ahead of
 *  the OS's bank. */
function packagedSoundFont(): { label: string; path: string } | null {
    const packaged = vscode.extensions.getExtension(SOUND_FONT_EXTENSION);
    if (!packaged) { return null; }
    const dir = path.join(packaged.extensionPath, 'soundfont');
    try {
        const files = fs.readdirSync(dir).filter(f => /\.(sf2|sf3|dls)$/i.test(f)).sort();
        if (files.length > 0) {
            return { label: `${files[0]} (the Lily# Sound Font extension)`, path: path.join(dir, files[0]) };
        }
    } catch { /* installed without its font: the OS's bank */ }
    return null;
}

/** The General MIDI banks an OS ships for its own synth, where present: Windows' Microsoft GS
 *  Wavetable bank and macOS's DLSMusicDevice bank (both Roland GS, DLS; small and dated, but
 *  there with no download). Offered by the picker; never copied or redistributed. */
function osSoundFonts(): { label: string; path: string }[] {
    const candidates = process.platform === 'win32'
        ? [{ label: 'Windows: gm.dls (the Microsoft GS Wavetable bank, 3 MB)',
             path: path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'drivers', 'gm.dls') }]
        : process.platform === 'darwin'
            ? [{ label: 'macOS: gs_instruments.dls (the DLSMusicDevice bank)',
                 path: '/System/Library/Components/CoreAudio.component/Contents/Resources/gs_instruments.dls' }]
            : [];
    return candidates.filter(c => fs.existsSync(c.path));
}

/** Where the sound fonts used before are kept: this computer's, so globalState rather than the
 *  settings (a path must not travel with Settings Sync). soundFontHistoryCore.ts says why. */
const SOUND_FONT_HISTORY_KEY = 'lilysharp.soundFontHistory';
/** The site's sound font guide (site/editor-body.html, "Sound fonts"): fonts to download, nearest
 *  to the default first, and how to set one up. Choose Sound Font…'s last item opens it. */
export const SOUND_FONT_GUIDE = 'https://yotsuda.github.io/LilySharp/editor.html#soundfonts';
/** Windows' and (by default) macOS's file names ignore case. */
const PATHS_IGNORE_CASE = process.platform === 'win32' || process.platform === 'darwin';

function soundFontHistory(context: vscode.ExtensionContext): string[] {
    return cleanHistory(context.globalState.get(SOUND_FONT_HISTORY_KEY), PATHS_IGNORE_CASE);
}

/** Remembers a font that is there now as the one used last (the setting written by the
 *  picker or by hand, through onDidChangeConfiguration). */
export async function rememberSoundFont(context: vscode.ExtensionContext, file: string): Promise<void> {
    if (!file || !fs.existsSync(file)) { return; }
    await context.globalState.update(SOUND_FONT_HISTORY_KEY, recordFont(soundFontHistory(context), file, PATHS_IGNORE_CASE));
}

/** The sound font a WAV, MP3 or MP4 export plays the score through: the one the preview plays
 *  (soundFontFile — the setting, else the Sound Font extension's, else the OS's bank), so the file sounds
 *  as Play does (owner's request 2026-11-07; until then the server's synth rendered it and read
 *  `.sf2` only, so the default gm.dls and a `.sf3` were refused). Only when the preview has none
 *  (Linux with no font set: it plays its oscillator patches, which no file can) does the writer
 *  pick one — a font used before (the sound font history) or a file — and it joins the history,
 *  so the next WAV export offers it first. The preview's setting is left alone. Undefined when
 *  they cancel. */
export async function wavSoundFont(context: vscode.ExtensionContext): Promise<string | undefined> {
    const plays = soundFontFile(context.extensionUri);
    if (plays) {
        return plays.file.fsPath;
    }
    const readable = (f: string) => /\.(sf2|sf3|dls)$/i.test(f) && fs.existsSync(f);
    const why = 'A WAV or MP3 export plays the score through the preview\'s sound font, and the preview has none';
    const used = soundFontHistory(context).filter(readable);
    let chosen: string | undefined;
    if (used.length > 0) {
        type Pick = vscode.QuickPickItem & { file?: string };
        const picked = await vscode.window.showQuickPick<Pick>([
            ...used.map(f => ({ label: path.basename(f), detail: f, file: f })),
            { label: 'Browse for a sound font…', detail: 'See "Sound fonts" on the Lily# site for three free ones' },
        ], { title: 'Lily#: the sound font to render the sound with', placeHolder: why });
        if (!picked) {
            return undefined;
        }
        chosen = picked.file;
    }
    if (!chosen) {
        if (used.length === 0) {
            const browse = 'Choose a Sound Font…';
            if (await vscode.window.showInformationMessage(`Lily#: ${why}.`, { modal: true,
                detail: 'The editor page of the Lily# site names three free sound fonts and how to install them.' },
                browse) !== browse) {
                return undefined;
            }
        }
        const files = await vscode.window.showOpenDialog({
            title: 'Choose a sound font for the export',
            canSelectMany: false,
            filters: { 'Sound font (.sf2, .sf3, .dls)': ['sf2', 'sf3', 'dls'] },
        });
        if (!files || files.length === 0) {
            return undefined;
        }
        chosen = files[0].fsPath;
    }
    await rememberSoundFont(context, chosen);
    return chosen;
}

/** The picker behind `Lily#: Choose Sound Font…` and the setting's link: the default, named
 *  for what plays (defaultSoundFont — the OS's bank, or a local build's own font, with the OS's
 *  bank then listed by its name), the fonts used before whose files are there now
 *  (each with a button that takes it off the list), or a file of the user's (a dialog filtered
 *  to .sf2 / .sf3 / .dls), written to the user settings as `lilysharp.playback.soundFont` — or the
 *  site's guide to fonts worth downloading (SOUND_FONT_GUIDE, the last item).
 *  The files are looked for when the picker opens, not at start-up: only the picker needs the
 *  answer, and a drive plugged in since then counts (owner's request 2026-10-12). */
export async function chooseSoundFont(context: vscode.ExtensionContext): Promise<void> {
    const config = vscode.workspace.getConfiguration('lilysharp');
    const current = config.get<string>('playback.soundFont', '').trim();
    await rememberSoundFont(context, current);   // a font set before the list existed
    type Pick = vscode.QuickPickItem & { action: 'default' | 'browse' | 'path' | 'more'; path?: string };
    const def = defaultSoundFont(context.extensionUri);
    const os = osSoundFonts()[0];
    const playsDefault = !current || !fs.existsSync(current) || (!!def && samePath(current, def.path, PATHS_IGNORE_CASE));
    const isCurrent = (f: string) => !playsDefault && samePath(f, current, PATHS_IGNORE_CASE);
    // The OS's bank by its own name when the default is something else (a local build's font).
    const osApart = os && def && !samePath(os.path, def.path, PATHS_IGNORE_CASE) ? os : undefined;
    const forget: vscode.QuickInputButton = {
        iconPath: new vscode.ThemeIcon('trash'), tooltip: 'Remove from this list (the file stays where it is)' };
    const build = (): Pick[] => [
        { label: def ? `Default — ${def.label}` : 'Default — simple tones', description: playsDefault ? '(current)' : '',
          detail: !def ? 'This system has no General MIDI bank of its own: browse for a sound font for real instruments'
                : def === os ? 'The General MIDI bank this computer already has (empties the setting)'
                : 'What plays when no font is chosen (empties the setting)',
          action: 'default' },
        ...(osApart ? [{ label: osApart.label, description: isCurrent(osApart.path) ? '(current)' : '',
                         detail: osApart.path, action: 'path' as const, path: osApart.path }] : []),
        ...shownFonts(soundFontHistory(context), f => fs.existsSync(f), PATHS_IGNORE_CASE,
                      [def?.path, os?.path].filter((p): p is string => !!p))
            .map<Pick>(f => ({
                label: path.basename(f), description: isCurrent(f) ? '(current)' : '',
                detail: f, action: 'path', path: f, buttons: [forget] })),
        { label: 'Browse for a file…', detail: 'An .sf2, .sf3 or .dls file on this machine', action: 'browse' },
        { label: 'Find More Sound Fonts…', detail: 'Free ones to download, from near the default to clearly different, and how to set one up (the Lily# site)', action: 'more' },
    ];
    const qp = vscode.window.createQuickPick<Pick>();
    qp.title = 'Lily#: the sound font the preview plays with';
    qp.placeholder = 'Each part plays the General MIDI instrument its midiInstrument or preset names';
    qp.items = build();
    const picked = await new Promise<Pick | undefined>(resolve => {
        qp.onDidAccept(() => { resolve(qp.selectedItems[0]); qp.hide(); });
        qp.onDidHide(() => resolve(undefined));
        qp.onDidTriggerItemButton(async e => {
            if (!e.item.path) { return; }
            await context.globalState.update(SOUND_FONT_HISTORY_KEY,
                forgetFont(soundFontHistory(context), e.item.path, PATHS_IGNORE_CASE));
            qp.items = build();
        });
        qp.show();
    });
    qp.dispose();
    if (!picked) { return; }
    if (picked.action === 'more') {
        // The guide (owner's request 2026-11-27): which fonts to download, and how to set one up.
        await vscode.env.openExternal(vscode.Uri.parse(SOUND_FONT_GUIDE));
        return;
    }
    let value: string | undefined;
    if (picked.action === 'default') {
        value = '';
    } else if (picked.action === 'path') {
        value = picked.path;
    } else {
        const chosen = await vscode.window.showOpenDialog({
            title: 'Choose a sound font',
            canSelectMany: false,
            filters: { 'Sound fonts': ['sf2', 'sf3', 'dls'], 'All files': ['*'] },
            defaultUri: current ? vscode.Uri.file(path.dirname(current)) : undefined,
        });
        if (!chosen || chosen.length === 0) { return; }
        value = chosen[0].fsPath;
    }
    await config.update('playback.soundFont', value, vscode.ConfigurationTarget.Global);
    vscode.window.setStatusBarMessage(
        value ? `Lily#: the preview plays ${path.basename(value)}` : 'Lily#: the preview plays the default sound', 5000);
}

/** The preview webview's resource roots: the music fonts, the text fonts, the synth
 *  and the sound font's folder. */
export function previewResourceRoots(extensionUri: vscode.Uri): vscode.Uri[] {
    const roots = [
        vscode.Uri.joinPath(extensionUri, 'media', 'fonts'),
        textFontsRoot(extensionUri),
        synthRoot(extensionUri),
    ];
    const sf = soundFontFile(extensionUri);
    if (sf) { roots.push(sf.root); }
    return roots;
}

/** The synth's files as the page's webview reaches them. */
export function synthUrls(webview: vscode.Webview, extensionUri: vscode.Uri): SynthUrls {
    const root = synthRoot(extensionUri);
    const sf = soundFontFile(extensionUri);
    return {
        script: webview.asWebviewUri(vscode.Uri.joinPath(root, 'preview-synth.js')).toString(),
        processor: webview.asWebviewUri(vscode.Uri.joinPath(root, 'spessasynth_processor.min.js')).toString(),
        soundFont: sf ? webview.asWebviewUri(sf.file).toString() : '',
        stretch: webview.asWebviewUri(vscode.Uri.joinPath(root, 'SignalsmithStretch.mjs')).toString(),
    };
}
