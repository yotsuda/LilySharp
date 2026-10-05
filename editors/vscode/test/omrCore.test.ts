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

// The editor-free half of "Import from Image/PDF…": where the reader is, what it is asked,
// how its progress lines read, and a real run against a stand-in reader that speaks the
// agreement (LilySharp-Omr docs/repro/lilysharp-omr-distribution-2026-10-05.md §3.2).

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import {
    OMR_PATH_VARIABLE, exitMeaning, launchOf, locateOmr, omrRid, parseEvent, parseVersion,
    canVerify, readArguments, runOmr, speaksProtocol, verifyArguments,
} from '../src/omrCore';

describe('the core is editor-free', () => {
    it('imports nothing from vscode', () => {
        const src = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'omrCore.ts'), 'utf8');
        assert.doesNotMatch(src, /from 'vscode'/);
    });
});

describe('where the reader is (L1)', () => {
    it('names the build for this machine', () => {
        assert.equal(omrRid('win32', 'x64'), 'win-x64');
        assert.equal(omrRid('darwin', 'arm64'), 'osx-arm64');
        assert.equal(omrRid('linux', 'x64'), 'linux-x64');
        assert.equal(omrRid('win32', 'ia32'), undefined);
        assert.equal(omrRid('freebsd', 'x64'), undefined);
    });

    it('takes the environment variable first', () => {
        const at = locateOmr({ [OMR_PATH_VARIABLE]: ' C:\\dev\\omr.exe ' }, 'C:\\store', 'win32', 'x64', () => true, () => ['9.9.9']);
        assert.deepEqual(at, { path: 'C:\\dev\\omr.exe', source: 'environment' });
    });

    it('then the newest installed version that has this machine\'s build', () => {
        const root = path.join('S', 'omr');
        const have = new Set([root,
            path.join(root, '0.10.0', 'win-x64', 'omr.exe'),
            path.join(root, '0.9.0', 'win-x64', 'omr.exe')]);
        const at = locateOmr({}, root, 'win32', 'x64', p => have.has(p), () => ['0.9.0', '0.10.0', '0.11.0', 'tmp']);
        assert.deepEqual(at, { path: path.join(root, '0.10.0', 'win-x64', 'omr.exe'), source: 'installed' });
    });

    it('and nothing when neither is there', () => {
        assert.equal(locateOmr({}, 'S', 'win32', 'x64', () => false, () => []), undefined);
    });

    it('runs a .dll under dotnet and anything else as itself', () => {
        assert.deepEqual(launchOf('C:\\a\\Omr.dll'), { command: 'dotnet', prefix: ['C:\\a\\Omr.dll'] });
        assert.deepEqual(launchOf('/opt/omr'), { command: '/opt/omr', prefix: [] });
    });
});

describe('what the reader is asked and answers (§3.2)', () => {
    it('passes the paths as array elements, spaces and all', () => {
        assert.deepEqual(readArguments(['C:\\楽譜 1\\p1.png', 'C:\\楽譜 1\\p2.png'], 'C:\\楽譜 1', 'C:\\x\\lysc.exe'),
            ['read', 'C:\\楽譜 1\\p1.png', 'C:\\楽譜 1\\p2.png', '--out', 'C:\\楽譜 1', '--progress', 'json',
                '--lysc', 'C:\\x\\lysc.exe']);
        assert.deepEqual(readArguments(['a.pdf'], 'o'), ['read', 'a.pdf', '--out', 'o', '--progress', 'json']);
    });

    it('reads --version --json and the protocol range', () => {
        assert.deepEqual(parseVersion('{"version":"0.3.0","protocol":1,"inputs":["png","pdf"]}\n'),
            { version: '0.3.0', protocol: 1, inputs: ['png', 'pdf'] });
        assert.equal(parseVersion('omr 0.3.0'), undefined);
        assert.equal(parseVersion('{"version":"0.3.0"}'), undefined);
        assert.equal(speaksProtocol(1), true);
        assert.equal(speaksProtocol(2), false);
    });

    it('reads the five events and ignores what is not one', () => {
        assert.deepEqual(parseEvent('{"event":"start","pages":9}'), { event: 'start', pages: 9 });
        assert.deepEqual(parseEvent('{"event":"page","page":2,"of":9}\r'), { event: 'page', page: 2, of: 9 });
        assert.equal(parseEvent('{"event":"warning","message":"page 3: one system not found"}')?.event, 'warning');
        assert.equal(parseEvent('{"event":"done","lys":"x.lys","todos":12}')?.event, 'done');
        assert.equal(parseEvent('{"event":"error","code":"input","message":"bad"}')?.event, 'error');
        assert.equal(parseEvent('reading page 2'), undefined);
        assert.equal(parseEvent('{"event":"page","page":"2"}'), undefined);
        assert.equal(parseEvent('{"event":"done"}'), undefined);
        assert.equal(parseEvent('{broken'), undefined);
    });

    it('names the exit codes', () => {
        assert.deepEqual([0, 1, 2, 3, 70, null].map(exitMeaning),
            ['ok', 'partial', 'input', 'internal', 'internal', 'stopped']);
    });
});

/** A stand-in reader: writes the .lys and the progress the agreement says, from node. */
const fakeReader = `
const fs = require('fs'), path = require('path');
const args = process.argv.slice(2);
const out = args[args.indexOf('--out') + 1];
const inputs = args.slice(1, args.indexOf('--out'));
if (inputs.some(i => i.endsWith('.bad'))) { console.log(JSON.stringify({event:'error',code:'input',message:'not an image'})); process.stderr.write('cannot decode\\n'); process.exit(2); }
if (inputs.some(i => i.endsWith('.slow'))) { setTimeout(() => {}, 60000); return; }
console.log(JSON.stringify({event:'start',pages:inputs.length}));
inputs.forEach((_, i) => console.log(JSON.stringify({event:'page',page:i+1,of:inputs.length})));
process.stderr.write('log line\\n');
const lys = path.join(out, path.parse(inputs[0]).name + '.lys');
fs.writeFileSync(lys, 'part m { }\\n');
console.log('not json');
console.log(JSON.stringify({event:'done',lys,annotated:[],todos:2}));
`;

describe('checking an edited score against the scan (B5)', () => {
    it('asks `omr verify` of the .lys, with progress, and the lysc when there is one', () => {
        assert.deepEqual(verifyArguments('C:\\a b\\曲.lys', 'C:\\x\\lysc.exe'),
            ['verify', 'C:\\a b\\曲.lys', '--progress', 'json', '--lysc', 'C:\\x\\lysc.exe']);
        assert.deepEqual(verifyArguments('s.lys'), ['verify', 's.lys', '--progress', 'json']);
    });

    it('asks only a reader that says it has the command', () => {
        assert.equal(canVerify({ version: '0.3.0', protocol: 1, commands: ['read', 'verify'] }), true);
        assert.equal(canVerify({ version: '0.3.0', protocol: 1, commands: ['read'] }), false);
        assert.equal(canVerify({ version: '0.2.0', protocol: 1 }), false);   // today's reader
    });
});

describe('a run (L7)', () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'lys-omr-'));
    const script = path.join(dir, 'fake-omr.js');
    fs.writeFileSync(script, fakeReader);
    const launch = { command: process.execPath, prefix: [script] };

    it('passes the events and the log through and returns the score', async () => {
        const events: string[] = [], log: string[] = [];
        const r = await runOmr(launch, readArguments([path.join(dir, 'p 1.png'), path.join(dir, 'p2.png')], dir),
            e => events.push(e.event), l => log.push(l));
        assert.equal(r.exitCode, 0);
        assert.deepEqual(events, ['start', 'page', 'page', 'done']);
        assert.deepEqual(log, ['log line']);
        assert.equal(r.done?.lys, path.join(dir, 'p 1.lys'));
        assert.equal(r.done?.todos, 2);
        assert.ok(fs.existsSync(r.done!.lys));
    });

    it('reports an unreadable input by its exit code and its words', async () => {
        const r = await runOmr(launch, readArguments([path.join(dir, 'x.bad')], dir), () => {}, () => {});
        assert.equal(exitMeaning(r.exitCode), 'input');
        assert.deepEqual(r.errors, ['not an image']);
        assert.deepEqual(r.stderrTail, ['cannot decode']);
    });

    it('stops the process when cancelled', async () => {
        const abort = new AbortController();
        const run = runOmr(launch, readArguments([path.join(dir, 'x.slow')], dir), () => {}, () => {}, abort.signal);
        setTimeout(() => abort.abort(), 200);
        const r = await run;
        assert.equal(exitMeaning(r.exitCode), 'stopped');
    });
});
