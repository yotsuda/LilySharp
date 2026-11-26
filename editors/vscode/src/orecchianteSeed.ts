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

// "Report to Orecchiante…" (Orecchiante's request 23, the owner's wish 2026-11-23;
// orecchianteSeedCore.ts, docs/orecchiante-report-design.md): code actions (Ctrl+.) on a bass, kit
// or chords row's bar, and on a drum phrase, of a chart with its file beside it — offered only
// while `lilysharp.recordings.seedFolder` is written in settings.json by hand. Nothing in
// package.json: the commands are registered here and reached through the code actions alone.

import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import type { LanguageClient } from 'vscode-languageclient/node';
import { recordingPathFor, resolveAudio } from './recordingCore';
import {
    ORECCHIANTE_SEED_FOLDER_SETTING, SeedKind, SeedPart, SideBar, VoiceBars, caretBar, caretPhrase, groovesPlaying, itemsOf,
    orecchianteSeedDirName, orecchianteSeedOf, phraseItems, phrasesOf, seedPartOf, sideBarsOf, sideSectionLength,
} from './orecchianteSeedCore';

const REPORT_COMMAND = 'lilysharp.orecchianteReport';

/** The owner's seed folder, or undefined: the setting is written by hand alone. */
function seedFolder(): string | undefined {
    return vscode.workspace.getConfiguration('lilysharp').get<string>(ORECCHIANTE_SEED_FOLDER_SETTING, '').trim() || undefined;
}

/** What a report is about, as a code action carries it. */
interface ReportTarget {
    readonly uri: string;
    readonly kind: SeedKind;
    readonly offset: number;
}

interface VoiceBarsResponse {
    Voices?: { Section: string; Voice: string; Bars: { Start: number; End: number }[] }[];
    Version: number;
}

async function voiceBarsOf(client: LanguageClient, doc: vscode.TextDocument): Promise<VoiceBars[] | undefined> {
    const r = await client.sendRequest<VoiceBarsResponse>('lilysharp/voiceBars', { textDocument: { uri: doc.uri.toString() } });
    if (r.Version !== doc.version || !r.Voices) { return undefined; }
    return r.Voices.map(v => ({ section: v.Section, voice: v.Voice, bars: v.Bars.map(b => ({ start: b.Start, end: b.End })) }));
}

/** The file beside the chart, read: its raw items and bars, or undefined (none, or not readable). */
function sideOf(doc: vscode.TextDocument): { path: string; text: string; raw: Record<string, unknown>; bars: SideBar[] } | undefined {
    if (doc.uri.scheme !== 'file') { return undefined; }
    const p = recordingPathFor(doc.uri.fsPath);
    try {
        const open = vscode.workspace.textDocuments.find(d => d.uri.fsPath.toLowerCase() === p.toLowerCase());
        const text = open ? open.getText() : fs.readFileSync(p, 'utf8');
        const raw = JSON.parse(text) as Record<string, unknown>;
        const bars = sideBarsOf(raw);
        return bars.some(b => typeof b.section === 'string') ? { path: p, text, raw, bars } : undefined;
    } catch {
        return undefined;
    }
}

/** Offers the reports at the caret: its bar and its section's bars, or the phrase it is on. */
class ReportActions implements vscode.CodeActionProvider {
    constructor(private readonly client: () => LanguageClient | undefined) { }

    async provideCodeActions(doc: vscode.TextDocument, range: vscode.Range): Promise<vscode.CodeAction[]> {
        const client = this.client();
        if (!seedFolder() || !client || doc.languageId !== 'lilysharp' || !sideOf(doc)) { return []; }
        const offset = doc.offsetAt(range.start);
        const text = doc.getText();
        const action = (title: string, kind: SeedKind) => {
            const a = new vscode.CodeAction(title, vscode.CodeActionKind.Empty);
            a.command = { title, command: REPORT_COMMAND, arguments: [{ uri: doc.uri.toString(), kind, offset } satisfies ReportTarget] };
            return a;
        };
        const phrase = caretPhrase(text, offset);
        if (phrase) {
            return [action(`Report Phrase ${phrase} to Orecchiante…`, 'phrase')];
        }
        let voices: VoiceBars[] | undefined;
        try {
            voices = await voiceBarsOf(client, doc);
        } catch {
            return [];
        }
        const at = voices && caretBar(voices, offset);
        const part = at && seedPartOf(at.voice.voice);
        if (!at || !part) { return []; }
        return [
            action(`Report Bar ${at.barOfSection} of ${at.voice.section} (${part}) to Orecchiante…`, 'bar'),
            action(`Report Section ${at.voice.section} (${part}) as Checked to Orecchiante…`, 'section'),
        ];
    }
}

/** Writes the report: asks for a note, copies the .lys and the file beside, writes seed.json. */
async function report(client: LanguageClient | undefined, output: vscode.OutputChannel, target: ReportTarget): Promise<void> {
    const root = seedFolder();
    const doc = vscode.workspace.textDocuments.find(d => d.uri.toString() === target.uri);
    if (!root || !client || !doc) { return; }
    const side = sideOf(doc);
    if (!side) {
        void vscode.window.showErrorMessage('Lily#: the file beside the chart (with its sections) is not there or cannot be read.');
        return;
    }
    const text = doc.getText();
    const voices = await voiceBarsOf(client, doc);
    if (!voices) {
        void vscode.window.showWarningMessage('Lily#: the chart changed while it was being read — report again.');
        return;
    }
    let part: SeedPart, section: string | null = null, label: string;
    let items: ReturnType<typeof itemsOf>;
    let linedUp: boolean;
    let phraseInfo: { name: string; body: string; grooves: string[] } | undefined;
    if (target.kind === 'phrase') {
        const name = caretPhrase(text, target.offset);
        if (!name) { return; }
        const phrases = phrasesOf(text);
        part = 'kit';
        const kits = voices.filter(v => seedPartOf(v.voice) === 'kit');
        items = phraseItems(text, kits, name, side.bars);
        const sections = [...new Set(items.map(i => i.section))];
        linedUp = sections.every(s => kits.filter(v => v.section === s).every(v => v.bars.length === sideSectionLength(side.bars, s)));
        phraseInfo = { name, body: phrases.get(name) ?? '', grooves: groovesPlaying(phrases, name) };
        label = `phrase ${name} (${items.length} kit bar${items.length === 1 ? '' : 's'} play it)`;
    } else {
        const at = caretBar(voices, target.offset);
        const p = at && seedPartOf(at.voice.voice);
        if (!at || !p) { return; }
        part = p;
        section = at.voice.section;
        items = itemsOf(text, at.voice, part, side.bars, target.kind === 'bar' ? k => k === at.barOfSection : undefined);
        linedUp = at.voice.bars.length === sideSectionLength(side.bars, at.voice.section);
        label = target.kind === 'bar' ? `bar ${at.barOfSection} of ${section} (${part})` : `section ${section} (${part}), ${items.length} bars`;
    }
    if (items.length === 0) {
        void vscode.window.showWarningMessage(`Lily#: nothing to report for ${label}.`);
        return;
    }
    const unplayed = items.filter(i => i.bars.length === 0).length;
    const warn = !linedUp ? ' — the chart\'s bars no longer match the file beside it'
        : unplayed > 0 ? ` — ${unplayed} bar${unplayed === 1 ? '' : 's'} not in the file beside it` : '';
    const note = await vscode.window.showInputBox({
        title: `Report to Orecchiante — ${label}${warn}`,
        prompt: target.kind === 'section'
            ? 'Every bar of the section, changed or not, is reported as right. Anything to add? (optional)'
            : target.kind === 'phrase'
                ? 'The phrase as it is now, with the kit bars that play it. Which of them truly go as it does, or anything else? (optional)'
                : 'The bar as it is now is reported as right. What did Orecchiante get wrong? (optional)',
        placeHolder: part === 'bass' ? 'e.g. the octave of the third note' : part === 'kit' ? 'e.g. the snare is on 2 and 4' : 'e.g. Bm, not D',
    });
    if (note === undefined) { return; }
    const when = new Date();
    const first = items[0];
    const dir = path.join(path.resolve(root), orecchianteSeedDirName(when, doc.uri.fsPath, target.kind, part,
        { firstPlayed: first.bars[0], section: section ?? undefined, phrase: phraseInfo?.name }));
    const audio = typeof side.raw.audio === 'string' ? side.raw.audio : undefined;
    const record = orecchianteSeedOf(when, target.kind, part, items, note, {
        lys: doc.uri.fsPath, sideFile: side.path, recording: audio ? resolveAudio(side.path, audio) : null,
        section, linedUp, ...(phraseInfo ? { phrase: phraseInfo } : {}),
    });
    try {
        fs.mkdirSync(dir, { recursive: true });
        fs.writeFileSync(path.join(dir, record.lysCopy), text, 'utf8');
        fs.writeFileSync(path.join(dir, record.sideFileCopy), side.text, 'utf8');
        fs.writeFileSync(path.join(dir, 'seed.json'), JSON.stringify(record, null, 2) + '\n', 'utf8');
    } catch (err) {
        void vscode.window.showErrorMessage(`Lily#: could not write the report to ${dir}: ${err}`);
        return;
    }
    output.appendLine(`Report to Orecchiante: ${label} → ${dir}`);
    void vscode.window.showInformationMessage(`Lily#: ${label} reported to Orecchiante.`, 'Reveal Folder').then(go => {
        if (go) { void vscode.commands.executeCommand('revealFileInOS', vscode.Uri.file(path.join(dir, 'seed.json'))); }
    });
}

/** Called once from activate. */
export function registerOrecchianteReport(context: vscode.ExtensionContext, client: () => LanguageClient | undefined,
    output: vscode.OutputChannel): void {
    context.subscriptions.push(
        vscode.languages.registerCodeActionsProvider({ language: 'lilysharp' }, new ReportActions(client)),
        vscode.commands.registerCommand(REPORT_COMMAND, (target: ReportTarget) => report(client(), output, target)),
    );
}
