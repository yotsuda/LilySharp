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
