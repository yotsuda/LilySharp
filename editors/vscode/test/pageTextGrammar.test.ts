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

// The page text's colours, by VS Code's own tokenizer (vscode-textmate and its Oniguruma —
// development dependencies, in no package) run over syntaxes/lilysharp.tmLanguage.json.
// LilySharp.Tests/EditorColouringTests.cs holds the grammar's word lists to the compiler's
// and says of itself that it has no TextMate engine: which rule WINS at a place, and where
// a begin/while region ends, are asked here.
//
// The fence's body is VS Code's Markdown grammar, included by its scope name. That grammar
// is not in this repository, so `text.html.markdown` is answered here by a STAND-IN written
// for these tests: a heading, strong emphasis, and a list that — as the real one's does —
// keeps every indented line and reads three backticks in it as a code block's opening, which
// is the trap the fence's `while` is there for. With LILYSHARP_VSCODE_REPO naming a checkout
// of VS Code, the last test runs the real grammar instead (measured so on 2026-11-13).

import { describe, it, before } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vsctm from 'vscode-textmate';
import * as oniguruma from 'vscode-oniguruma';

const F = '```';
const root = path.join(__dirname, '..', '..');
const grammarPath = path.join(root, 'syntaxes', 'lilysharp.tmLanguage.json');

const standInMarkdown = {
    scopeName: 'text.html.markdown',
    patterns: [{ include: '#block' }],
    repository: {
        block: { patterns: [{ include: '#heading' }, { include: '#list' }, { include: '#code' }, { include: '#inline' }] },
        heading: {
            match: '(?:^|\\G) {0,3}(#{1,6}) +(.*)$',
            captures: { 1: { name: 'punctuation.definition.heading.markdown' }, 2: { name: 'entity.name.section.markdown' } },
            name: 'markup.heading.markdown',
        },
        list: {
            begin: '(?:^|\\G) {0,3}([-*+]|\\d+[.)]) ',
            beginCaptures: { 1: { name: 'punctuation.definition.list.begin.markdown' } },
            while: '(?:^|\\G)(?: {2,4}|\\t)|^[ \\t]*$',
            name: 'markup.list.markdown',
            patterns: [{ include: '#block' }],
        },
        code: {
            begin: '(?:^|\\G)\\s*(`{3,}).*$', end: '(?:^|\\G)\\s*\\1\\s*$',
            name: 'markup.fenced_code.block.markdown',
        },
        inline: { patterns: [{ match: '\\*\\*[^*]+\\*\\*', name: 'markup.bold.markdown' }] },
    },
};

type Token = { text: string, scopes: string[] };

async function tokenizer(markdown: () => string): Promise<(lines: string[]) => Token[][]> {
    const wasm = fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm'));
    await oniguruma.loadWASM(wasm.buffer.slice(wasm.byteOffset, wasm.byteOffset + wasm.byteLength) as ArrayBuffer);
    const registry = new vsctm.Registry({
        onigLib: Promise.resolve({
            createOnigScanner: (patterns: string[]) => new oniguruma.OnigScanner(patterns),
            createOnigString: (s: string) => new oniguruma.OnigString(s),
        }),
        loadGrammar: async (scope: string) => {
            if (scope === 'source.lilysharp') {
                return vsctm.parseRawGrammar(fs.readFileSync(grammarPath, 'utf8'), grammarPath);
            }
            if (scope === 'text.html.markdown') { return vsctm.parseRawGrammar(markdown(), 'markdown.json'); }
            return null;
        },
    });
    const grammar = (await registry.loadGrammar('source.lilysharp'))!;
    return lines => {
        let stack = vsctm.INITIAL;
        return lines.map(line => {
            const r = grammar.tokenizeLine(line, stack);
            stack = r.ruleStack;
            return r.tokens.map(t => ({ text: line.slice(t.startIndex, t.endIndex), scopes: t.scopes.slice(1) }));
        });
    };
}

/** The scopes of the token that is exactly `text` on a line (the first, if several). */
function scopesOf(line: Token[], text: string): string[] {
    const t = line.find(x => x.text === text);
    assert.ok(t, 'no token ' + JSON.stringify(text) + ' in ' + JSON.stringify(line.map(x => x.text)));
    return t.scopes;
}

const lilysharp = (scopes: string[]) => scopes.filter(s => s.endsWith('.lilysharp') && s !== 'meta.fenced-text.lilysharp');
const EMBEDDED = 'meta.embedded.block.markdown';

describe('a page text\'s colours', () => {
    let tokenize: (lines: string[]) => Token[][];
    before(async () => { tokenize = await tokenizer(() => JSON.stringify(standInMarkdown)); });

    it('colours the opening line: the backticks, the tag, the words, a length with its unit', () => {
        const [open] = tokenize([F + 'md end xMargin 20mm 0.5in yMargin 2 box center', 'x', F]);
        assert.deepEqual(lilysharp(scopesOf(open, F)), ['punctuation.definition.fenced-text.begin.lilysharp']);
        assert.deepEqual(lilysharp(scopesOf(open, 'md')), ['entity.name.tag.fenced-text.lilysharp']);
        for (const word of ['end', 'xMargin', 'yMargin', 'box', 'center']) {
            assert.deepEqual(lilysharp(scopesOf(open, word)), ['keyword.other.fenced-text.lilysharp'], word);
        }
        assert.deepEqual(lilysharp(scopesOf(open, '20')), ['constant.numeric.lilysharp']);
        assert.deepEqual(lilysharp(scopesOf(open, '2')), ['constant.numeric.lilysharp']);
        assert.deepEqual(lilysharp(scopesOf(open, 'mm')), ['support.constant.paper-unit.lilysharp']);
        assert.deepEqual(lilysharp(scopesOf(open, '0.5')), ['constant.numeric.lilysharp']);
        assert.deepEqual(lilysharp(scopesOf(open, 'in')), ['support.constant.paper-unit.lilysharp']);
        assert.ok(open.every(t => !t.scopes.includes(EMBEDDED)), 'the opening line is Lily#, not the text');
    });

    it('reads markdown for md, with the tag alone on its line too', () => {
        assert.deepEqual(lilysharp(scopesOf(tokenize([F + 'markdown end', F])[0], 'markdown')), ['entity.name.tag.fenced-text.lilysharp']);
        assert.deepEqual(lilysharp(scopesOf(tokenize([F + 'md', F])[0], 'md')), ['entity.name.tag.fenced-text.lilysharp']);
    });

    it('colours a word of the opening line only as a whole word', () => {
        const [open] = tokenize([F + 'mdx boxed xMargin20mm yMargins indent', F]);
        assert.ok(open.every(t => !t.scopes.some(s => s.startsWith('keyword.') || s.startsWith('entity.'))),
            JSON.stringify(open));
    });

    it('hands the body to Markdown: a heading, strong words, a list marker', () => {
        const lines = tokenize([F + 'md', '# Notes', 'For **four voices**.', '- first time', F]);
        assert.deepEqual(scopesOf(lines[1], '#').slice(0, 2), ['meta.fenced-text.lilysharp', EMBEDDED]);
        assert.ok(scopesOf(lines[1], '#').includes('punctuation.definition.heading.markdown'));
        assert.ok(scopesOf(lines[2], '**four voices**').includes('markup.bold.markdown'));
        assert.ok(scopesOf(lines[3], '-').includes('punctuation.definition.list.begin.markdown'));
        for (const line of lines.slice(1, 4)) {
            assert.ok(line.every(t => t.scopes.includes(EMBEDDED)), JSON.stringify(line));
        }
    });

    it('colours nothing in the body as Lily#: a brace, a quote, a comment\'s slashes, a note', () => {
        const [, body] = tokenize([F + 'md', 'Play { c4 d } "softly" // not a comment, title key', F]);
        assert.deepEqual(body.flatMap(t => lilysharp(t.scopes)), []);
        assert.ok(body.every(t => t.scopes.includes(EMBEDDED)));
    });

    it('colours the closing line, and what follows it as Lily# again', () => {
        const lines = tokenize([F + 'md', 'text', F, 'title "After" // c', 'section A { p { c\'4 | } }']);
        assert.deepEqual(scopesOf(lines[2], F), ['punctuation.definition.fenced-text.end.lilysharp']);
        assert.deepEqual(scopesOf(lines[3], 'title'), ['keyword.other.lilysharp']);
        assert.deepEqual(scopesOf(lines[3], 'After'), ['string.quoted.double.lilysharp']);
        assert.deepEqual(scopesOf(lines[3], '// c'), ['comment.line.double-slash.lilysharp']);
        assert.deepEqual(scopesOf(lines[4], 'c\''), ['variable.other.pitch.lilysharp']);
        assert.deepEqual(scopesOf(lines[4], '|'), ['punctuation.separator.barline.lilysharp']);
    });

    it('closes after a list, indented in a score — where a plain begin/end never closed', () => {
        const lines = tokenize(['score parts {', '  ' + F + 'md', '  - a list', '  - item two', '  ' + F, '  staff piano', '}', 'title "x"']);
        assert.deepEqual(scopesOf(lines[0], 'score'), ['keyword.control.lilysharp']);
        assert.deepEqual(scopesOf(lines[0], '{'), ['punctuation.definition.block.lilysharp']);
        assert.ok(scopesOf(lines[2], '-').includes('punctuation.definition.list.begin.markdown'));
        assert.ok(scopesOf(lines[3], '-').includes('punctuation.definition.list.begin.markdown'));
        assert.deepEqual(scopesOf(lines[4], F), ['punctuation.definition.fenced-text.end.lilysharp']);
        assert.deepEqual(scopesOf(lines[5], 'staff'), ['keyword.control.lilysharp']);
        assert.deepEqual(scopesOf(lines[6], '}'), ['punctuation.definition.block.lilysharp']);
        assert.deepEqual(scopesOf(lines[7], 'title'), ['keyword.other.lilysharp']);
    });

    it('takes the fence\'s indentation off a line before Markdown reads it', () => {
        // Four spaces in: without that, Markdown reads every line as an indented code block.
        const lines = tokenize(['score {', '    ' + F + 'md', '    # Notes', '    ' + F, '}']);
        assert.ok(scopesOf(lines[2], '#').includes('punctuation.definition.heading.markdown'));
        assert.deepEqual(scopesOf(lines[3], F), ['punctuation.definition.fenced-text.end.lilysharp']);
    });

    it('closes a fence of four on four: three backticks inside it are the text\'s', () => {
        const lines = tokenize([F + '`md', 'a', F + 'lilysharp', 'c4 { d', F, '**b**', F + '`', 'title "x"']);
        assert.ok(scopesOf(lines[3], 'c4 { d').includes('markup.fenced_code.block.markdown'));
        assert.deepEqual(lilysharp(scopesOf(lines[3], 'c4 { d')), [], 'a code block in a text is not coloured as music');
        assert.ok(scopesOf(lines[5], '**b**').includes('markup.bold.markdown'), 'the inner block closed');
        assert.deepEqual(scopesOf(lines[6], F + '`'), ['punctuation.definition.fenced-text.end.lilysharp']);
        assert.deepEqual(scopesOf(lines[7], 'title'), ['keyword.other.lilysharp']);
    });

    it('does not begin where a text may not stand: a section, a brace in a score, a part', () => {
        for (const lines of [
            ['section A {', '  ' + F + 'md', '  piano { c4 "x" }', '  ' + F, '}'],
            ['score {', '  staffGroup {', '    ' + F + 'md', '    piano { c4 "x" }', '    ' + F, '  }', '}'],
            ['part piano {', '  section A {', '    ' + F + 'md', '    piano { c4 "x" }', '    ' + F, '  }', '}'],
            ['form {', F + 'md', 'piano { c4 "x" }', F, '}'],
        ]) {
            const out = tokenize(lines);
            const music = out[lines.findIndex(l => l.includes('c4'))];
            assert.ok(out.flat().every(t => !t.scopes.some(s => s.includes('fenced-text') || s === EMBEDDED)), lines.join('/'));
            // …and the lines keep the colours they had: the music inside is still music.
            assert.deepEqual(scopesOf(music, 'c'), ['variable.other.pitch.lilysharp']);
            assert.deepEqual(scopesOf(music, 'x'), ['string.quoted.double.lilysharp']);
        }
    });

    it('begins again after the brace that kept it out has closed', () => {
        const lines = tokenize(['section A { piano { c4 } }', 'form { A }', F + 'md end', '**x**', F]);
        assert.deepEqual(lilysharp(scopesOf(lines[2], 'end')), ['keyword.other.fenced-text.lilysharp']);
        assert.ok(scopesOf(lines[3], '**x**').includes('markup.bold.markdown'));
    });

    it('leaves a backtick that begins no line as it was', () => {
        const [line] = tokenize(['title "a ' + F + ' b" // ' + F + 'md']);
        assert.deepEqual(scopesOf(line, 'title'), ['keyword.other.lilysharp']);
        assert.ok(line.every(t => !t.scopes.some(s => s.includes('fenced-text'))));
    });

    it('runs an unclosed text to the end of the file', () => {
        const lines = tokenize([F + 'md', 'a', 'title "x"']);
        assert.ok(lines[2].every(t => t.scopes.includes(EMBEDDED)));
    });
});

describe('the embedded language', () => {
    it('is declared: the body\'s scope is the language markdown', () => {
        const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
        const grammar = pkg.contributes.grammars.find((g: { language?: string }) => g.language === 'lilysharp');
        assert.deepEqual(grammar.embeddedLanguages, { [EMBEDDED]: 'markdown' });
    });

    it('binds Enter only on a list item of a page text, with the typing aids on', () => {
        const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
        const enter = pkg.contributes.keybindings.filter((b: { key: string }) => b.key === 'enter');
        assert.equal(enter.length, 1);
        assert.equal(enter[0].command, 'lilysharp.textEnter');
        for (const guard of [/editorLangId == lilysharp/, /lilysharp\.inTextList/, /!suggestWidgetVisible/,
            /!editorReadonly/, /!editorHasSelection/, /config\.lilysharp\.typingAids\.enabled/]) {
            assert.match(enter[0].when, guard);
        }
    });
});

describe('with VS Code\'s own Markdown grammar', () => {
    const repo = process.env.LILYSHARP_VSCODE_REPO;
    const real = repo ? path.join(repo, 'extensions', 'markdown-basics', 'syntaxes', 'markdown.tmLanguage.json') : '';
    it('the same text is coloured and closes', { skip: !real || !fs.existsSync(real) }, async () => {
        const tokenize = await tokenizer(() => fs.readFileSync(real, 'utf8'));
        const lines = tokenize(['score parts {', '    ' + F + 'md box', '    # Notes {x} "q" // c', '    For **four voices**.', '',
            '    - first time *mf*', '      - nested', '    1. one', '    ' + F, '    staff piano', '}']);
        assert.ok(scopesOf(lines[2], '#').includes('punctuation.definition.heading.markdown'));
        assert.deepEqual(lines[2].flatMap(t => lilysharp(t.scopes)), []);
        assert.ok(scopesOf(lines[3], 'four voices').includes('markup.bold.markdown'));
        assert.ok(scopesOf(lines[5], '-').includes('punctuation.definition.list.begin.markdown'));
        assert.ok(scopesOf(lines[7], '1.').includes('punctuation.definition.list.begin.markdown'));
        assert.deepEqual(scopesOf(lines[8], F), ['punctuation.definition.fenced-text.end.lilysharp']);
        assert.deepEqual(scopesOf(lines[9], 'staff'), ['keyword.control.lilysharp']);
    });
});
