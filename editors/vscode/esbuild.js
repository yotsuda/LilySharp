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
 * This file also bundles MIT-licensed dependencies; see THIRD-PARTY-NOTICES.md.
 * Source: https://github.com/yotsuda/LilySharp
 */`;

async function main() {
    copyGrammar();
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
    if (watch) {
        await ctx.watch();
        console.log('esbuild: watching...');
    } else {
        await ctx.rebuild();
        await ctx.dispose();
    }
}

main().catch((e) => {
    console.error(e);
    process.exit(1);
});
