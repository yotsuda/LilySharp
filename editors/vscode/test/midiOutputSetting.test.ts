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

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vm from 'node:vm';

// The manifest's side of `lilysharp.playback.output` (docs/playback-design.md §6, MIDI out):
// the setting, the command that lists the machine's ports for it, and the link from the one
// to the other — a port's name is not something to type.
describe('the MIDI output setting', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
    const setting = manifest.contributes.configuration.properties['lilysharp.playback.output'];
    const commands = manifest.contributes.commands as { command: string; title: string }[];
    const palette = manifest.contributes.menus.commandPalette as { command: string; when?: string }[];

    it('is a machine-overridable string, empty by default (the preview\'s own synth)', () => {
        assert.equal(setting.type, 'string');
        assert.equal(setting.default, '');
        assert.equal(setting.scope, 'machine-overridable');
    });

    it('links to the picker command, which is in the palette without a language condition', () => {
        assert.match(setting.markdownDescription, /\(command:lilysharp\.chooseMidiOutput\)/);
        const command = commands.find(c => c.command === 'lilysharp.chooseMidiOutput');
        assert.ok(command, 'the command is contributed');
        assert.match(command!.title, /MIDI Output/);
        const entry = palette.find(p => p.command === 'lilysharp.chooseMidiOutput');
        assert.ok(entry, 'the command is in the palette');
        assert.equal(entry!.when, undefined);
    });
});

// The page script's side: a Play with a port named goes to the host (midiPlay) instead of
// the synth, the host's answer starts the highlight from the wall clock or, refused, plays
// the same list here; a strike goes to the host too and its release names the channel.
// The functions are lifted out of the page script as webviewScript.test.ts lifts others.
describe('the preview routes its sounds to the MIDI output', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
    const start = source.indexOf('return `<!DOCTYPE html>') + 'return '.length;
    const end = source.indexOf('</html>`;', start) + '</html>`'.length;
    const html = new vm.Script(source.slice(start, end).replace(/\$\{[^}]*\}/g, 'X')).runInNewContext({}) as string;

    it('sends a Play to the host when a port is named, and the strike and its release too', () => {
        const play = html.indexOf('function startPlayback(');
        const playBody = html.slice(play, html.indexOf('function midiStarted(', play));
        assert.match(playBody, /if \(midiOutput && !here\)/);
        assert.match(playBody, /type: 'midiPlay', port: midiOutput, notes, offset/);
        const hold = html.indexOf('function holdNotes(');
        const holdBody = html.slice(hold, html.indexOf('function auditionNote(', hold));
        assert.match(holdBody, /type: 'midiStrike', port: midiOutput/);
        assert.match(holdBody, /heldChannel = timbre === 9 \? 9 : 15/);
        const release = html.indexOf('function stopHeldNote(');
        assert.match(html.slice(release, hold), /type: 'midiRelease', channel: heldChannel, pitches: heldPitches/);
    });

    it('follows the wall clock from the server\'s zero, and plays here when the server refused', () => {
        const at = html.indexOf('function midiStarted(');
        const body = html.slice(at, html.indexOf('function followAlong(', at));
        assert.match(body, /\(Date\.now\(\) - t0Ms\) \/ 1000/);
        assert.match(body, /startPlayback\(pending\.notes, pending\.offset, false, true\)/);
        const stop = html.indexOf('function stopPlayback(');
        assert.match(html.slice(stop, html.indexOf('function startPlayback(', stop)), /type: 'midiStop'/);
    });
});
