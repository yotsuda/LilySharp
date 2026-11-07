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

// Bundles the extension client (src/extension.ts + its node_modules deps) into a
// single out/extension.js so the VSIX ships one JS file instead of ~165. The
// vscode module is provided by the host and Node built-ins stay external.
const esbuild = require('esbuild');
const fs = require('fs');
const path = require('path');

const production = process.argv.includes('--production');
const watch = process.argv.includes('--watch');

// The AI transform feeds docs/GRAMMAR_FOR_LLM.md (the Lily# grammar canon) to the
// language model. Copy it into out/ at build time so it ships in the VSIX and stays
// in sync with the repo — the extension reads out/GRAMMAR_FOR_LLM.md at runtime.
function copyGrammar() {
    try {
        const src = path.resolve(__dirname, '..', '..', 'docs', 'GRAMMAR_FOR_LLM.md');
        const outDir = path.resolve(__dirname, 'out');
        fs.mkdirSync(outDir, { recursive: true });
        fs.copyFileSync(src, path.join(outDir, 'GRAMMAR_FOR_LLM.md'));
        console.log('esbuild: bundled GRAMMAR_FOR_LLM.md');
        // The composing guide, sent with Compose with AI alone (owner's decision 2026-10-29: Ask AI
        // edits a score and is not sent it).
        const compose = path.resolve(__dirname, '..', '..', 'docs', 'COMPOSE_FOR_LLM.md');
        fs.copyFileSync(compose, path.join(outDir, 'COMPOSE_FOR_LLM.md'));
        console.log('esbuild: bundled COMPOSE_FOR_LLM.md');
        // The path the chat teaches Lily# from, read through lilysharp_readGuide (owner's request
        // 2026-11-02): not an instructions file, as nothing but a request to be taught needs it.
        fs.copyFileSync(path.resolve(__dirname, '..', '..', 'docs', 'LEARN_FOR_LLM.md'), path.join(outDir, 'LEARN_FOR_LLM.md'));
        console.log('esbuild: bundled LEARN_FOR_LLM.md');
        // The SAME canon, as a Copilot instructions file (package.json
        // contributes.chatInstructions). Only Lily#'s own AI features read the copy above;
        // Copilot's chat, agent and inline chat never did, so asked about a .lys file they
        // answered from LilyPond (owner report, 2026-09-26). The `applyTo` glob attaches it
        // to requests that touch a .lys file and to no others.
        const instructions = [
            '---',
            'name: Lily# grammar',
            "description: 'The Lily# (.lys) language: its syntax, the canonical form of every construct, and what is NOT LilyPond.'",
            "applyTo: '**/*.lys'",
            '---',
            '',
            // Copilot's chat proposes a score through Lily#'s tool, the musician deciding on the
            // score as Ask AI's review has them (owner's request 2026-10-29).
            '**Changing or writing a .lys file**: do it through the `lilysharp_proposeScore` tool (#proposeScore) —',
            'the file\'s absolute path (for a score not saved yet, the path it is named by) and its WHOLE new text.',
            'Lily# compiles it; a new piece (the file empty)',
            'goes straight into the file, and a change to a score is shown to the musician beside the score as',
            'it stands, to accept or reject — the result says which. Do not edit a .lys file yourself while the',
            'tool is there.',
            '**Reading a .lys file**: read it through the `lilysharp_readScore` tool (#readScore), not from the disk —',
            'it gives the text as it stands in the editor; a score not saved yet is not on the disk at all.',
            '**Reviewing a piece**: asked to review a .lys file or give feedback on it, use the `lilysharp_reviewScore` tool (#reviewScore)',
            'and explain its report in plain words.',
            '**Teaching Lily#**: asked how to write it, or to be taught it, read the `lilysharp_readGuide` tool, topic `learn`, and teach from that path.',
            '',
            fs.readFileSync(src, 'utf8'),
        ].join('\n');
        fs.writeFileSync(path.join(outDir, 'lilysharp.instructions.md'), instructions);
        console.log('esbuild: wrote lilysharp.instructions.md');
        // The composing guide, a file of its own (owner's request 2026-10-29: Copilot's agent could
        // not read the two together — 103.7 KB, "too large to read at once" — and read pieces of it
        // found by search; alone, some 13 KB, it is read whole).
        const composing = [
            '---',
            "name: Lily# composing guide",
            "description: 'How to compose a new piece in Lily# (.lys): what makes it music for its players, the kinds of music, the setting of words. Read it whole before composing.'",
            "applyTo: '**/*.lys'",
            '---',
            '',
            fs.readFileSync(compose, 'utf8'),
        ].join('\n');
        fs.writeFileSync(path.join(outDir, 'lilysharp-compose.instructions.md'), composing);
        console.log('esbuild: wrote lilysharp-compose.instructions.md');
        // A few lines with every request, so a piece asked for with no .lys in the workspace is
        // written in Lily# (owner's report 2026-10-29: in an empty folder the chat answered a hymn
        // in prose, the tools never searched for). Kept to what a request for a score needs, and
        // told to stand aside otherwise: VS Code is used for much besides music.
        // A scan read and mended in Lily#'s own panels (owner's request 2026-10-29). And (owner's reports
        // 2026-10-30) an attached .lys viewed from the disk ("Allow reading file outside of workspace?"), a
        // drum line's phrases counted in a script ("Run in terminal?").
        // A new piece in a new file (owner's request 2026-11-07: asked with a score open, the chat
        // proposed the new piece over it).
        const everywhere = [
            '---',
            'name: Lily# is here',
            "description: 'Lily#, the music notation language this editor engraves: what to use when music is to be written out as a score.'",
            "applyTo: '**'",
            '---',
            '',
            'This VS Code has Lily#, a music notation language (.lys files) it engraves, plays and checks.',
            'When the request is to compose or write out music as a score — a piece, a song, a hymn, an',
            'arrangement — and names no other format, write it in Lily#: first read how with the',
            'lilysharp_readGuide tool (topic "composing"), then propose it with lilysharp_proposeScore.',
            'A new piece goes into a NEW .lys file, never over an open score unless asked for that file.',
            'Asked how to write Lily# or .lys, or to be taught it, read that tool with topic "learn" and teach from it.',
            'Asked to turn images or a PDF of sheet music into a score, read them with lilysharp_readScan.',
            'A score read from a scan (its .omr.json beside it) is not corrected by you: asked to fix its',
            'misreadings, open the bars named with lilysharp_fixScanBars; with none named, tell the musician to',
            'right-click the bar on the score and choose Edit Bars Beside the Scan.',
            'To read a .lys file, attached or named, use lilysharp_readScore (search the tools for it),',
            'never a file viewer: it gives the score as it stands in the editor, and what Lily# says of it.',
            'Run no script or terminal on a score.',
            'Before writing a chord for a guitar, bass or string part, ask lilysharp_chordVoicings what its player can hold, its midiInstrument as sound.',
            'Leave this aside for anything else.',
            '',
        ].join('\n');
        fs.writeFileSync(path.join(outDir, 'lilysharp-here.instructions.md'), everywhere);
        console.log('esbuild: wrote lilysharp-here.instructions.md');
    } catch (e) {
        console.warn('esbuild: could not copy GRAMMAR_FOR_LLM.md (extension will use the compact fallback):', e.message);
    }
}

// out/extension.js is the only JS the VSIX ships, and a bundler drops ordinary
// comments — including the GPL notice at the top of every source file. This
// banner is what carries the notice into the artifact users receive. The `/*!`
// form also marks it as a legal comment, so minification keeps it.
const LICENSE_BANNER = `/*!
 * Lily# VS Code extension - part of Lily#, a music notation compiler.
 * Copyright (C) 2025-2026 Yoshifumi Tsuda
 *
 * This program is free software: you can redistribute it and/or modify it
 * under the terms of the GNU General Public License as published by the Free
 * Software Foundation, either version 3 of the License, or (at your option)
 * any later version. It is distributed WITHOUT ANY WARRANTY; without even the
 * implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
 * See the GNU General Public License (LICENSE) for more details.
 *
 * This file also bundles MIT- and Apache-2.0-licensed dependencies, and lamejs
 * (LGPL-3.0, LICENSE-LGPL-3.0.txt); see THIRD-PARTY-NOTICES.md.
 * Source: https://github.com/yotsuda/LilySharp
 */`;

// The preview's SoundFont synth runs IN THE WEBVIEW, which cannot require() anything: its
// half (src/webview/previewSynth.ts + spessasynth_lib) is bundled for the browser into
// out/synth/preview-synth.js, and spessasynth_lib's AudioWorklet processor — a separate
// file the page hands to audioWorklet.addModule — is copied beside it. The page loads
// both by webview URI (extension.ts, getPreviewHtml); out/ ships in the VSIX.
function copyWorkletProcessor() {
    const src = path.resolve(__dirname, 'node_modules', 'spessasynth_lib', 'dist', 'spessasynth_processor.min.js');
    const outDir = path.resolve(__dirname, 'out', 'synth');
    if (!fs.existsSync(src)) {
        throw new Error(`${src} not found - node_modules is out of date with package.json; run \`npm ci\` in ${__dirname}.`);
    }
    fs.mkdirSync(outDir, { recursive: true });
    fs.copyFileSync(src, path.join(outDir, 'spessasynth_processor.min.js'));
    console.log('esbuild: copied spessasynth_processor.min.js');
}

async function main() {
    copyGrammar();
    copyWorkletProcessor();
    const ctx = await esbuild.context({
        entryPoints: ['src/extension.ts'],
        bundle: true,
        format: 'cjs',
        platform: 'node',
        target: 'node18',
        outfile: 'out/extension.js',
        external: ['vscode'],
        minify: production,
        sourcemap: !production,
        sourcesContent: false,
        banner: { js: LICENSE_BANNER },
        legalComments: 'inline',
        logLevel: 'warning',
    });
    const synthCtx = await esbuild.context({
        entryPoints: ['src/webview/previewSynth.ts'],
        bundle: true,
        format: 'iife',
        platform: 'browser',
        target: 'es2022',
        outfile: 'out/synth/preview-synth.js',
        minify: production,
        sourcemap: !production,
        sourcesContent: false,
        banner: { js: LICENSE_BANNER },
        legalComments: 'inline',
        logLevel: 'warning',
    });
    if (watch) {
        await ctx.watch();
        await synthCtx.watch();
        console.log('esbuild: watching...');
    } else {
        await ctx.rebuild();
        await synthCtx.rebuild();
        await ctx.dispose();
        await synthCtx.dispose();
    }
}

main().catch((e) => {
    console.error(e);
    process.exit(1);
});
