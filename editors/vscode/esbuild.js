// Bundles the extension client (src/extension.ts + its node_modules deps) into a
// single out/extension.js so the VSIX ships one JS file instead of ~165. The
// vscode module is provided by the host and Node built-ins stay external.
const esbuild = require('esbuild');

const production = process.argv.includes('--production');
const watch = process.argv.includes('--watch');

async function main() {
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
