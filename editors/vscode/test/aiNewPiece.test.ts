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
import { asksForOneStaff, carrySelection, cleanTitle, isPlaceholderTitle, isProblem, partCount, titleOf, toCandidateEdit, withTitle } from '../src/aiTransformCore';

/** The grammar and the composing guide, as the composer is sent them (owner's decision 2026-10-29:
 *  the guide is a document of its own, sent with Compose with AI alone). */
function llmDocs(): string {
    const docs = path.join(__dirname, '..', '..', '..', '..', 'docs');
    return fs.readFileSync(path.join(docs, 'GRAMMAR_FOR_LLM.md'), 'utf8') + '\n' + fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
}

// Owner's decision 2026-10-29: the composing guide is kept apart from the grammar — sent with
// Compose with AI, not with Ask AI — and bundled beside it.
describe('the composing guide', () => {
    const docs = path.join(__dirname, '..', '..', '..', '..', 'docs');
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
    // Owner's trial 2026-10-29: a quartet's three lower parts rested together on beat 4 of every bar.
    it('says a rest in one part is covered by another', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*A rest in one part is covered by another\*\*: the\s+accompanying parts do not stop together bar after bar/);
    });
    // Owner's trial 2026-10-29: a "dramatic" quartet's tremolo and sforzando were all in two bars.
    it('says the character\'s devices run through the piece, strongest at the climax', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*The devices run\s+through the piece\*\*, not spent in one place: each comes back in more than one section/);
    });
    // Owner's report 2026-10-29: a piece composed in Copilot's chat was written as absolute in a
    // relative file, and its pitches flew off — the guide, which the chat is given too, says to start absolute.
    it('says a new piece starts in absolute octaves', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*Start in absolute octaves\*\*: a new piece begins with `octave absolute` \(and `pitch concert`\)/);
    });
    // Owner's request 2026-10-29: a Compose menu that hands the request to Copilot's chat, its
    // thinking in view — the file started absolute, no Lily# check yet (the owner's choice).
    it('is handed to Copilot\'s chat by a menu of its own, the file started in absolute octaves', () => {
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"command": "lilysharp\.composeWithAiInChat",\s*"category": "Lily#",[\s\S]*?"title": "Compose with AI in Chat…"/);
        assert.match(pkg, /"command": "lilysharp\.composeWithAiInChat",\s*"group": "file@2"/);
        // Named there for Copilot, apart from Lily#'s own Compose with AI in the palette (owner's
        // request 2026-10-29).
        assert.match(pkg, /"command": "lilysharp\.composeWithAiInChat",[^}]*"shortTitle": "Lily# Score, Composed with Copilot…"/);
        assert.match(transform, /registerCommand\('lilysharp\.composeWithAiInChat', \(\) => runComposeInChat\(deps\)\)/);
        const fn = transform.slice(transform.indexOf('async function runComposeInChat'), transform.indexOf('async function runCompose('));
        // Nothing opened before the piece comes (owner's request 2026-10-29): the chat writes the
        // header the guide gives, and its proposal opens the piece.
        assert.ok(!/newPieceHeader|openTextDocument|applyEdit/.test(fn));
        const header = transform.slice(transform.indexOf('function newPieceHeader('), transform.indexOf('function newPieceSteps('));
        assert.match(header, /`\/\/ Composed in Copilot's chat, \$\{new Date\(\)\.toISOString\(\)\.slice\(0, 10\)\}`,\s*`\/\/ Asked: \$\{asked\}`,\s*`\/\/ The AI: \$\{note\}`,\s*'',\s*'octave absolute',\s*'pitch concert',/);
        assert.match(fn, /executeCommand\('workbench\.action\.chat\.open', \{ query, mode: 'agent' \}\)/);
        assert.ok(!/compositionFindings|checkCandidate/.test(fn));
        // The request alone, and where to read how to begin (owner's request 2026-10-29): the steps
        // and the starting point come from the guide tool's "start".
        assert.match(fn, /'First read how to begin and the composing guide: lilysharp_readGuide, topic "composing"\.\\n'/);
        assert.ok(!/compositionHint|newPieceSteps/.test(fn));
        // The bundled lysc, for the agent to check its piece with, and Lily#'s tool to propose it
        // through, written straight only without it (owner's requests 2026-10-29).
        const steps = transform.slice(transform.indexOf('function newPieceSteps('), transform.indexOf('async function writeWhole'));
        assert.match(steps, /check it with the Lily# compiler — \\`& "\$\{lysc\}" check <copy>\\` on a copy of your text/);
        assert.match(steps, /'Propose it, whole, with the lilysharp_proposeScore tool \(#proposeScore\), giving that path: it checks the '/);
        // Owner's request 2026-10-29: no save dialog — an unsaved score named Composed.lys (or the first
        // ComposedN.lys free), saved when the musician wants it kept.
        assert.ok(!/showSaveDialog|writeFileSync/.test(fn));
        // The name is chosen here, free on the disk and in the editor, so the chat picks no score there.
        assert.match(fn, /file = path\.join\(folder, `Composed\$\{n\}\.lys`\);/);
        assert.match(fn, /const query = `Compose a new piece of music in Lily# for \$\{file\} \(a new file: your proposal opens it in the editor\)\.\\n`/);
    });
    // Owner's request 2026-10-29: Copilot's chat proposes a score through Lily#'s tool, the musician
    // deciding on the score — compiled first, an error sent back unseen.
    it('gives Copilot\'s chat a tool to propose a score with, decided on in the candidate panel', () => {
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"name": "lilysharp_proposeScore",\s*"toolReferenceName": "proposeScore"/);
        assert.match(pkg, /"onLanguageModelTool:lilysharp_proposeScore"/);
        assert.match(pkg, /"required": \["file", "text"\]/);
        assert.match(transform, /lm\.registerTool<ProposeScoreInput>\('lilysharp_proposeScore'/);
        const fn = transform.slice(transform.indexOf('async function proposeScore'), transform.indexOf('async function runComposeInChat'));
        assert.match(fn, /if \(errors\.length > 0\) \{[\s\S]*?return `Not shown to the musician: it does not compile\./);
        assert.match(fn, /await reviewOnScore\(deps, renderBefore, renderAfter, caption, wholeTextMarkup\(current, text, changed\)/);
        assert.match(fn, /return 'Accepted: the musician accepted the proposal, and it is in the editor, not saved yet/);
        // Owner's request 2026-10-29: Accept puts it in the editor and saves nothing.
        assert.ok(!/\.save\(\)/.test(fn));
        // Owner's requests 2026-10-29: what to change is said in the chat — no Ask Again — and a new
        // piece has no Reject (rejected, it would leave an empty file); the panel stays for the next
        // proposal, and a message sent in the chat before deciding is told as such.
        assert.ok(!/showInputBox/.test(fn));
        assert.match(fn, /input\.note \?\? null, true\);/);
        // Owner's request 2026-10-29: a new piece goes straight into the file, open in the editor —
        // no panel; the music check still speaks once first.
        assert.match(fn, /if \(isEmptyScore\(current\)\) \{\s*const written = await writeWhole\(uri, exists, doc, text\);/);
        assert.ok(fn.indexOf('const music = await compositionFindings') < fn.indexOf('const written = await writeWhole'));
        // Written, not saved; an unsaved score is found by the path it is named by.
        const write = transform.slice(transform.indexOf('async function writeWhole'), transform.indexOf('function unsavedNamed'));
        assert.ok(!/\.save\(\)/.test(write));
        assert.match(write, /await vscode\.window\.showTextDocument\(target, \{ preview: false \}\);/);
        // A file not there opens as an unsaved score named by its path, as Compose with AI in Chat's.
        assert.match(write, /const target = exists && doc \? doc : await vscode\.workspace\.openTextDocument\(uri\.with\(\{ scheme: 'untitled' \}\)\);/);
        assert.ok(!/createFile/.test(write));
        // The score found as the reading tool finds it — unsaved, else open, else on the disk (owner's
        // report 2026-10-29: renamed on the disk while the chat worked, the open score was written over as new).
        assert.match(fn, /const doc = await scoreDocument\(input\.file\);\s*const uri = doc\?\.uri \?\? vscode\.Uri\.file\(input\.file\);\s*const exists = doc !== undefined;/);
        const find = transform.slice(transform.indexOf('async function scoreDocument('), transform.indexOf('interface ReadScoreInput'));
        assert.match(find, /const found = unsavedNamed\(file\) \?\? open\(file\)/);
        // …and, renamed or moved in VS Code since, where it is now: the editor it was asked about
        // (owner's request 2026-10-29: renamed while the chat worked, the change was written as new).
        assert.match(find, /const moved = currentUri\(vscode\.Uri\.file\(path\.resolve\(file\)\)\);/);
        assert.match(transform, /vscode\.workspace\.onDidRenameFiles\(e => \{\s*for \(const f of e\.files\) \{ movedUris\.set\(f\.oldUri\.toString\(\), f\.newUri\); \}/);
        assert.match(transform, /d\.uri\.scheme === 'untitled'\s*&& d\.uri\.fsPath\.toLowerCase\(\) === path\.resolve\(file\)\.toLowerCase\(\)/);
        assert.match(fn, /return 'Not decided: the musician went on in the chat before deciding, and the file is unchanged\. '/);
        assert.match(transform, /<button id="iterate"[^>]*\$\{fromChat \? ' hidden' : ''\}>Ask Again…<\/button>/);
        assert.match(fn, /token\.onCancellationRequested/);
        const build = fs.readFileSync(path.join(__dirname, '..', '..', 'esbuild.js'), 'utf8');
        assert.match(build, /'\*\*Changing or writing a \.lys file\*\*: do it through the `lilysharp_proposeScore` tool \(#proposeScore\) —',/);
    });
    // Owner's request 2026-10-29: the chat's agent reads the disk, where an unsaved score is not —
    // a tool reads the score as it stands in the editor.
    it('gives Copilot\'s chat a tool to read a score as it stands in the editor, saved or not', () => {
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"name": "lilysharp_readScore",\s*"toolReferenceName": "readScore"/);
        assert.match(pkg, /"onLanguageModelTool:lilysharp_readScore"/);
        assert.match(transform, /lm\.registerTool<ReadScoreInput>\('lilysharp_readScore'/);
        const fn = transform.slice(transform.indexOf('async function readScore'), transform.indexOf('const musicCheckedFor'));
        assert.match(fn, /const doc = await scoreDocument\(input\.file\);/);
        assert.match(fn, /return `\$\{path\.basename\(doc\.uri\.fsPath\)\} \(\$\{state\}\), \$\{doc\.lineCount\} lines, as it stands:\\n\$\{doc\.getText\(\)\}`;/);
        const build = fs.readFileSync(path.join(__dirname, '..', '..', 'esbuild.js'), 'utf8');
        assert.match(build, /'\*\*Reading a \.lys file\*\*: read it through the `lilysharp_readScore` tool \(#readScore\), not from the disk —',/);
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /`\$\{doc\.uri\.fsPath\} \(not saved — read it with #readScore\)`/);
    });
    // Owner's request 2026-10-29: the instructions files go only with a request about a .lys, so a
    // piece asked for in the chat with none open was written without them — a tool gives them.
    it('gives Copilot\'s chat the grammar and the composing guide by a tool, found by what it is for', () => {
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        // Named, described and tagged as the other two (owner's request, the same day: consistent).
        assert.match(pkg, /"name": "lilysharp_readGuide",\s*"toolReferenceName": "readGuide",\s*"displayName": "Read the Lily# guide"/);
        assert.match(pkg, /"onLanguageModelTool:lilysharp_readGuide"/);
        const tags = '"tags": ["lilysharp", "lys", "music", "score", "notation", "compose", "song"]';
        assert.equal(pkg.split(tags).length - 1, 3);
        assert.match(pkg, /"modelDescription": "Use this before composing music — a piece, a song, a score — or writing or changing Lily# \(\.lys\) text/);
        assert.match(pkg, /"enum": \["grammar", "composing", "start"\]/);
        assert.match(transform, /lm\.registerTool<ReadGuideInput>\('lilysharp_readGuide'/);
        const fn = transform.slice(transform.indexOf('async function readGuide('), transform.indexOf('function newPieceSteps('));
        // The instructions files Copilot is given with a .lys in hand, their front matter left out.
        assert.match(fn, /return built\('lilysharp\.instructions\.md'\) \?\? await loadGrammar\(deps\);/);
        // "start" is how a new piece begins alone; "composing" is that and the composing guide.
        assert.match(fn, /return topic === 'start' \? start\s*: start \+ '\\n' \+ \(built\('lilysharp-compose\.instructions\.md'\) \?\? await loadComposeGuide\(deps\)\);/);
        assert.match(fn, /\.replace\(\/\^---\\r\?\\n\[\\s\\S\]\*\?\\r\?\\n---\\r\?\\n\/, ''\)/);
        // How a new piece begins: the steps Compose with AI in Chat's request gives, and a starting point.
        assert.match(fn, /\+ newPieceSteps\(deps\.lysc\?\.\(\)\)\s*\+ `\$\{hint\}\\n`;/);
        // …beginning with the header the menu writes (owner's request 2026-10-29).
        // …and what the AI made of it, as Compose with AI's "The AI:" line (owner's request, the same day).
        assert.match(fn, /\+ newPieceHeader\('<the request>', '<the piece you composed, in a sentence or two>', '\\n'\)\.trimEnd\(\) \+ '\\n'/);
        assert.match(fn, /'lines are these, the request as the musician put it and what you made of it, each on one line, in the '/);
        // A few lines with every request, saying Lily# is here and where to read it — for a score
        // asked for with no .lys about, and aside otherwise (owner's report and decision 2026-10-29).
        const build = fs.readFileSync(path.join(__dirname, '..', '..', 'esbuild.js'), 'utf8');
        const here = build.slice(build.indexOf('const everywhere = ['), build.indexOf("'lilysharp-here.instructions.md'), everywhere)"));
        assert.match(here, /"applyTo: '\*\*'",/);
        assert.match(here, /'lilysharp_readGuide tool \(topic "composing"\), then propose it with lilysharp_proposeScore\.',/);
        assert.match(here, /'Leave this aside for anything else\.',\s*'',\s*\]\.join/);
        assert.ok(here.length < 2000, 'kept to a few lines');
        // A scan read with Lily#'s reader, and its misreadings mended beside the scan, not by the chat
        // (owner's request 2026-10-29).
        assert.match(here, /'Asked to turn images or a PDF of sheet music into a score, read them with lilysharp_readScan\.',/);
        assert.match(here, /'misreadings, open the bars named with lilysharp_fixScanBars; with none named, tell the musician to',\s*'right-click the bar on the score and choose Edit Bars Beside the Scan\.',/);
        assert.match(pkg, /"name": "lilysharp_readScan",\s*"toolReferenceName": "readScan"/);
        assert.match(pkg, /"name": "lilysharp_fixScanBars",\s*"toolReferenceName": "fixScanBars"/);
        assert.match(pkg, /"onLanguageModelTool:lilysharp_readScan",\s*"onLanguageModelTool:lilysharp_fixScanBars"/);
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /lm\.registerTool<\{ files: string\[\] \}>\('lilysharp_readScan'/);
        assert.match(ext, /const read = await importFromImage\(context, omrImportDeps, uris\[0\], uris\);/);
        const fix = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'omrAiFix.ts'), 'utf8');
        assert.match(fix, /lm\.registerTool<FixScanBarsInput>\('lilysharp_fixScanBars'/);
        // One marked bar has the AI asked at once; else the bars open to be edited.
        assert.match(fix, /if \(marked\) \{\s*void runOmrAiFix\(context, deps, doc\.uri, doc\.offsetAt\(doc\.lineAt\(chosen\[0\]\)\.range\.start\)\);\s*\} else \{\s*editor\.selections = chosen\.map/);
        assert.match(fix, /'The musician decides there, and Accept writes the bar: do not change these bars yourself\.'/);
        assert.match(pkg, /"path": "\.\/out\/lilysharp-here\.instructions\.md"/);
        const compose = transform.slice(transform.indexOf('async function runComposeInChat'), transform.indexOf('async function runCompose('));
        assert.match(compose, /lilysharp_readGuide, topic "composing"/);
    });
    // Owner's trials 2026-10-29: a chat's hymns crossed the alto under the tenor, set the alto in the
    // tenor's range, and rocked the bass between root and fifth — the guide had no hymn.
    // Owner's trials 2026-10-29: a jazz trio's drums came as a rock backbeat, its bass the chord's
    // notes in a row; the next time they swung — the guide said only "swinging on the ride".
    it('says how a jazz rhythm section plays', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /the ride's swing figure, `cymr4 cymr8 cymr cymr4 cymr8 cymr`, the hi-hat pedal on\s+2 and 4 \(`hhp`\)/);
        assert.match(guide, /never a\s+backbeat of bass drum on 1 and 3 and snare on 2 and 4 \(that is rock\)/);
        assert.match(guide, /the note on beat 4 leading a\s+step or a half step into the next root/);
        assert.match(guide, /\*\*Piano comping\*\*: rootless voicings/);
        assert.match(guide, /\*\*A blues head\*\*: a riff stated, stated again over the IV, answered/);
    });
    // Owner's trials 2026-10-29: a flute waltz's piano doubled the tune, then, asked not to, set its
    // chords in the flute's register on beats 2 and 3 over the left hand's own, and rested where the
    // flute rested.
    it('says where a piano\'s chords lie under a solo, and that a dance\'s figure is one pair', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /The piano's chords lie BELOW the solo, not in its register and never\s+over it/);
        assert.match(guide, /a waltz's bass on beat 1 and its chords on 2\s+and 3 round middle C, the two hands sharing that one figure, not each playing it/);
        assert.match(guide, /the\s+right hand's own line comes where the solo holds or rests, an answer to it/);
    });
    it('says how a hymn or a chorale for choir is written', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*A hymn or chorale for choir\*\* \(SATB, `choirStaff`\): the four voices move together/);
        assert.match(guide, /the alto ABOVE the tenor, never crossing it/);
        assert.match(guide, /not rocking between a chord's root and fifth/);
    });
    it('holds the recipe, and the grammar only points at it', () => {
        const grammar = fs.readFileSync(path.join(docs, 'GRAMMAR_FOR_LLM.md'), 'utf8');
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*"Compose a piece" \/ "write it for a string quartet, …"\*\*/);
        assert.doesNotMatch(grammar, /\*\*A piano piece\*\*|\*\*Gospel\*\*|\*\*A song with words\*\*/);
        assert.match(grammar, /the composing guide sent with\s+Compose with AI \(`docs\/COMPOSE_FOR_LLM\.md`\)/);
    });
    it('is sent with the composer\'s prompt and not with Ask AI\'s', () => {
        assert.match(transform, /\.\.\.\(guide \? \['<composing-guide>', guide, '<\/composing-guide>', ''\] : \[\]\)/);
        const ask = transform.slice(transform.indexOf('function systemPrompt(grammar'));
        assert.ok(!ask.slice(0, 3000).includes('composing-guide'));
        assert.match(transform, /await loadComposeGuide\(deps\);\s*const systemTokens = estimateTokens\(composeSystemPrompt\(grammar, chat\.modelName\)\);/);
    });
    it('is bundled beside the grammar, and given to Copilot\'s chat with it', () => {
        const build = fs.readFileSync(path.join(__dirname, '..', '..', 'esbuild.js'), 'utf8');
        assert.match(build, /fs\.copyFileSync\(compose, path\.join\(outDir, 'COMPOSE_FOR_LLM\.md'\)\);/);
        // Its own instructions file (owner's request 2026-10-29: the two together, 103.7 KB, were too
        // large for Copilot's agent to read at once).
        assert.match(build, /fs\.writeFileSync\(path\.join\(outDir, 'lilysharp-compose\.instructions\.md'\), composing\);/);
        assert.doesNotMatch(build, /fs\.readFileSync\(src, 'utf8'\),\s*'',\s*\/\/[^\n]*\n\s*fs\.readFileSync\(compose/);
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"path": "\.\/out\/lilysharp-compose\.instructions\.md"/);
        assert.match(fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8'), /\*\*No `composer` line\*\* unless the request asks for one/);
    });
});

// Owner's report 2026-10-26: "discard the score and compose a new string quartet" failed three
// times — the model answered with <edit> blocks quoting the old score, and copied none of it
// exactly. A new piece is the whole new file; and a failure says why.
describe('a new piece in place of the score', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');

    it('is offered as a third reply form, the whole new file in <file>', () => {
        assert.match(transform, /Reply in ONE of three forms:/);
        assert.match(transform, /'3\. The request discards the score, or replaces nearly all of it/);
        assert.match(transform, /reply with the whole new file between <file> and <\/file>/);
    });

    it('is suggested when an edit quotes text the file does not have', () => {
        const file = 'title "Old"\npart p { clef treble }\nsection A { p { c4 d e f | } }\n';
        const reply = '<edit>\n<old>\nsection A { p { g4 a b c | } }\n</old>\n<new>\nsection A { p { e4 | } }\n</new>\n</edit>';
        const edit = toCandidateEdit({ origFullText: file, startOffset: 0, endOffset: 0, origSelectedText: '' }, reply);
        assert.ok(edit && isProblem(edit));
        assert.match((edit as { problem: string }).problem, /reply with the whole new file between <file> and <\/file> instead/);
    });

    it('a failure names the last try\'s reason, and every turned-down try is logged', () => {
        assert.match(transform, /deps\.log\(`AI transform: attempt \$\{attempt \+ 1\}\/\$\{maxRepairs \+ 1\} turned down — \$\{reason\}`\)/);
        assert.match(transform, /could not produce a valid transform in \$\{maxRepairs \+ 1\} tries\. Nothing was changed\.`\s*\+ \(why \? ` The last try: \$\{why\}` : ''\), showLog\)/);
    });

    it('a new piece is checked as music once, and what is found goes back for a revision', () => {
        assert.match(transform, /const newPiece = compose \|\| \/<file>\/\.test\(raw\);/);
        assert.match(transform, /if \(newPiece && !musicAsked && attempt < maxRepairs\) \{\s*const music = await compositionFindings\(client, reconstructed\);/);
        assert.match(transform, /sendRequest<\{ Findings: string\[\] \}>\('lilysharp\/compositionCheck', \{ Text: text \}\)/);
        // The octave check measures against the old notes: not for a new piece.
        assert.match(transform, /if \(edit\.text !== octaveAsked && !newPiece\) \{/);
        // A revision that does not come leaves the piece as first written, with what was found.
        assert.match(transform, /if \(candidate === null && flat && !newerWarned\) \{\s*candidate = flat\.edit;/);
    });

    it('Compose with AI is a command of its own, writing into a new score', () => {
        assert.match(transform, /registerCommand\('lilysharp\.composeWithAi', \(\) => runCompose\(deps\)\)/);
        // No document while the AI works (owner's request 2026-10-27): the start is in memory, and
        // the piece opens in an editor of its own when it comes — no review panel, nothing to compare.
        assert.match(transform, /uri: vscode\.Uri\.parse\('untitled:Composed\.lys'\),/);
        assert.doesNotMatch(transform.slice(transform.indexOf('async function runCompose('), transform.indexOf('async function runAiTransform')), /openTextDocument/);
        assert.match(transform, /if \(compose\) \{[\s\S]{0,5500}?openTextDocument\(\{ language: 'lilysharp', content: `\$\{preface\}\$\{eol\}\$\{eol\}\$\{text\}` \}\);\s*await vscode\.window\.showTextDocument\(piece\);/);
        // The request and the AI's note head the piece as comments (owner's request 2026-10-27).
        assert.match(transform, /line\(`Asked: \$\{composeAsked \?\? instruction\}`\),\s*\.\.\.\(note \? \[line\(`The AI: \$\{note\}`\)\] : \[\]\),/);
        // What the notification says is the musician's request, not the instruction to the AI.
        assert.match(transform, /const said = composeAsked \?\? instruction;/);
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"command": "lilysharp\.composeWithAi",\s*"category": "Lily#",\s*"icon": "\$\(sparkle\)",\s*"title": "Compose with AI…",\s*"shortTitle": "Lily# Score, Composed with AI…"/);
        // Found where a piece is begun (owner's request 2026-10-29): File > New File… beside New
        // Score, the language server started for it when no .lys has started it yet.
        // New Score first, then Compose (owner's request 2026-10-29): by order, not by title. The
        // composer there is the chat's (owner's decision, the same day); this one is in the palette.
        assert.match(pkg, /"command": "lilysharp\.newScore",\s*"group": "file@1"\s*\},\s*\{\s*"command": "lilysharp\.composeWithAiInChat",\s*"group": "file@2"\s*\}\s*\]/);
        assert.match(transform, /async function runCompose\(deps: AiTransformDeps, asked\?: string\): Promise<void> \{[\s\S]{0,300}?await deps\.ensureReady\?\.\(\);/);
        // A broken connection offers Try Again, the request in the box to send or shorten
        // (owner's report 2026-10-29: net::ERR_HTTP2_PROTOCOL_ERROR two minutes into a piece).
        assert.match(transform, /const failed = aiFailure\('compose', String\(err\?\.message \?\? err\), Date\.now\(\) - began\);\s*if \(failed\.retry && await vscode\.window\.showErrorMessage\(failed\.text, 'Try Again'\) === 'Try Again'\) \{\s*await runCompose\(deps, description\.trim\(\)\);/);
        assert.match(transform, /value: asked,/);
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /ensureReady: ensureClientReady,/);
    });
});

// Owner's report 2026-10-27: "add guitar chords" asked again of a composed piece failed three
// times — the AI's edits quoted the piece it had written, and were looked for in the file before
// it. Asked again, the AI answers against the file as its candidate left it.
// Owner's request 2026-10-27: a composed piece opens in relative octaves, converted by the
// server, not by the AI.
describe('a composed piece opens in relative octaves', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
    it('converts the text before it opens, and keeps it as it came if it cannot', () => {
        const at = (s: string) => transform.indexOf(s);
        assert.ok(at("'lilysharp/convertOctaves', { Text: text, Absolute: false, DropPartOctaves: true, DropConcertPitch: true }") > 0);
        assert.ok(at("'lilysharp/convertOctaves', { Text: text, Absolute: false, DropPartOctaves: true, DropConcertPitch: true }") < at("const piece = await vscode.workspace.openTextDocument({ language: 'lilysharp'"));
        assert.match(transform, /if \(relative\.Success && relative\.NewText\) \{\s*text = relative\.NewText;/);
        assert.match(transform, /the piece stays in absolute octaves/);
    });
});

// Owner's report 2026-10-27: asked for guitar diagrams, the AI wrote under the guitar's range,
// and the warnings filled the candidate's head.
describe('the instruments\' ranges, and a head that holds a long list', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
    const grammar = llmDocs();
    it('tells the AI each instrument\'s range, the guitar\'s among them, in the grammar every request carries', () => {
        assert.match(grammar, /## Instrument ranges[\s\S]*keep\s+EVERY note inside the range/);
        assert.match(grammar, /\| `guitar` \| E2–E6 \| `e,` – `e'''` \|/);
    });
    // Owner's report 2026-10-27: a composed piano piece's left hand lived between E1 and G2.
    it('tells it where a piano\'s hands lie, and that the left hand\'s bare c is C3', () => {
        assert.match(grammar, /A range is how far an instrument CAN go, not where its part lies/);
        assert.match(grammar, /left hand lies mostly between C2 and C4, round C3/);
        // Owner's decision 2026-10-28: the octaves are LilyPond's in both hands (no preset moves them).
        assert.match(grammar, /Both hands write LilyPond's octaves: C3 is `c`, middle C `c'`, C5 `c''`\./);
    });
    // Owner's review 2026-10-27: an octave of empty keyboard between the hands, a left hand broken up.
    it('tells it how a piano piece fills the keyboard, and that a left hand may keep one value', () => {
        assert.match(grammar, /\*\*A piano piece\*\* is written for two hands that fill the keyboard between them/);
        assert.match(grammar, /A\s+left hand may keep one note value through a section: its figure is the rhythm;[\s\S]*?A contrasting section takes a figure of its own\./);
    });
    // Owner's request 2026-10-27: a part names its instrument and lets the preset bring its clef
    // (and, since 2026-10-28, no octave: the octaves are LilyPond's whatever the instrument).
    it('tells the AI to name the instrument and write no clef beside it, and shows it so', () => {
        assert.match(grammar, /A part of a known instrument names it with `instrument` and writes no `clef`\s+beside it\.\*\* The preset brings its clef/);
        assert.match(grammar, /it\s+does NOT move the octaves, which are LilyPond's in every part/);
        assert.match(grammar, /part leftHand  \{ instrument piano-left \}/);
        assert.match(grammar, /part bass \{ instrument bass \}\s+\/\/ added/);
        // No example writes them together (the rule's own counter-example aside).
        assert.doesNotMatch(grammar.replace(/not `\{ instrument cello clef bass/, ''), /instrument \w[\w-]* (clef|octave) |(clef \w+|octave \d) instrument /);
        // A bass is written by the pitch it sounds under `pitch concert` (owner's decision 2026-10-28).
        assert.match(grammar, /Octave-only instruments \(bass, piccolo\) are\s+typed as they SOUND under `pitch concert` too/);
    });
    it('holds the candidate\'s head to a few lines, scrolled for the rest', () => {
        assert.match(transform, /\.header \.texts \{ flex:1; min-width:0; max-height:7\.5em; overflow-y:auto;/);
        assert.match(transform, /<div class="header">\s*<div class="texts">/);
        // The request, the AI's word under it, then what is left (owner's request 2026-10-27).
        assert.match(transform, /<div class="prompt">\$\{escapeHtml\(asked\)\}<\/div>\s*\$\{note \?[^\n]*\n\s*\$\{issues \? `<div class="issues">/);
        assert.match(transform, /formatDiags\(newWarnings, reconstructed\)/);
    });
});

// Owner's report 2026-10-27: Accept said "the document was closed" — VS Code had let go of a
// document no editor showed while the AI thought.
describe('an Accept on a file no editor shows', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
    // The case of the report: an unsaved composed piece, Ctrl+I on it, Accept — the name
    // "Untitled-1" had been saved once before, and its old move pointed at a file not open.
    it('takes an open document by its own name before any move, and forgets a name given out again', () => {
        assert.match(transform, /function currentUri\(uri: vscode\.Uri\): vscode\.Uri \{\s*if \(vscode\.workspace\.textDocuments\.some\(d => d\.uri\.toString\(\) === uri\.toString\(\)\)\) \{\s*return uri;/);
        assert.match(transform, /onDidOpenTextDocument\(d => \{\s*if \(d\.uri\.scheme === 'untitled'\) \{ movedUris\.delete\(d\.uri\.toString\(\)\); \}/);
    });
    it('opens the file again rather than saying it was closed; an untitled score alone cannot be', () => {
        assert.match(transform, /if \(!doc && uri\.scheme !== 'untitled'\) \{\s*try \{\s*doc = await vscode\.workspace\.openTextDocument\(uri\);/);
    });
});

// Owner's request 2026-10-27: the preface quotes what the AI first said, not a repair's word.
describe('a composed piece\'s preface', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
    it('quotes the AI\'s first answer, not the word of a repair', () => {
        assert.match(transform, /if \(parsed !== null && !isProblem\(parsed\) && firstNote === undefined\) \{\s*firstNote = splitNote\(parsed\.reply\)\.note;/);
        assert.match(transform, /const note = round\.firstNote !== undefined \? round\.firstNote : splitNote\(candidate\.reply\)\.note;/);
    });
});

// Owner's report 2026-10-27: the same model writes a fair piece asked in a chat, a poor one asked
// from VS Code. A composer is told it is one, plans before it writes, and is shown no piece to copy;
// what it is sent and answers is logged word for word.
describe('the composer\'s prompt', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');
    it('casts the model as a composer, not a transformer of fragments, when it composes', () => {
        assert.match(transform, /content: compose \? composeSystemPrompt\(grammar, chat\.modelName\) : systemPrompt\(grammar\)/);
        assert.match(transform, /'You are a composer\. You write a complete new piece of music/);
        assert.match(transform, /none of them is a model of a piece\\'s length, form or style/);
    });
    it('has it plan the piece where the musician does not see it, then the note, then the file', () => {
        assert.match(transform, /'1\. <plan> … <\/plan> — your working notes; the musician never sees them\./);
        // Owner's decision 2026-10-29: short and shapely rather than long — one printed page unless asked.
        assert.match(transform, /LENGTH AND FORM — the sections and their bars \(about 24 to 32 bars unless the request gives a length,',\s*'\s*or what the piece needs\)/);
        // Owner's wish 2026-10-29: rules held too tight make every piece the same — habits, broken where the piece is better.
        assert.match(transform, /'The composing guide\\'s ways are a composer\\'s habits, not rules: break one where the piece is better',/);
        assert.match(llmDocs(), /\*\*These are a composer's habits, not a form to fill\*\*/);
        assert.doesNotMatch(transform, /32 to 64 bars/);
        assert.match(transform, /return 'Reply with <plan>…<\/plan>, <note>…<\/note> and the whole piece in <file>…<\/file>';/);
    });
    // Owner's decision 2026-10-28: Lily#'s octaves are LilyPond's — no anchors to name, no habit to warn of.
    // Owner's decision 2026-10-28: a composed piece is written at concert pitch, every part as it sounds.
    it('has it write at concert pitch, a bass and a trumpet by the pitches they sound', () => {
        assert.match(transform, /'- Write at CONCERT pitch: `pitch concert` at the top of the file too, and every part by the pitch it',/);
        assert.match(transform, /a bass\\'s low E string is `e,,` \(E1\)/);
        assert.doesNotMatch(transform, /give its part `octave 4`/);
    });
    it('tells it the octaves are LilyPond\'s in every part, and has the plan give each register as written', () => {
        assert.match(transform, /The octaves are LilyPond\\'s, in every part and for every instrument: `c` = C3, `c\\'` = middle C \(C4\)/);
        assert.doesNotMatch(transform, /NOT LilyPond\\'s octaves/);
        assert.match(transform, /AND as you will write/);
        assert.match(transform, /is written `d\\'` – `a\\'\\'`/);
    });
    // Owner's review 2026-10-27: the piece was tidy but not "cool" — the character is to be planned
    // as the devices that make it.
    // Owner's request 2026-10-28: the bass's `octave 4` is the AI's crutch — taken out before the piece opens.
    it('opens the piece without the parts\' octave lines, every note kept where it sounds', () => {
        assert.match(transform, /'lilysharp\/convertOctaves', \{ Text: text, Absolute: false, DropPartOctaves: true, DropConcertPitch: true \}/);
        assert.match(transform, /if \(!relative\.Success\) \{\s*deps\.log\(`AI transform: the parts' octave lines stay/);
    });
    // Owner's trial 2026-10-29: the A′ of an A–B–A′–Coda piece was A again, note for note.
    it('has a section that returns as it was named again in the form, not copied', () => {
        assert.match(transform, /a section that comes back as it was is named again in the form',\s*'\s*\(`form \{ A B A Coda \}`\), never copied\./);
        const grammarDoc = llmDocs();
        assert.match(grammarDoc, /\*\*A section that returns is named again, not copied\*\*/);
    });
    // Owner's report 2026-10-29: the connection broke during the third round, and the piece the
    // second had written went with it.
    it('shows the candidate before a revision that was cut off, saying so', () => {
        assert.match(transform, /if \(attempt > 0 && \(warned \|\| flat\)\) \{\s*cutOff = String\(err\?\.message \?\? err\);/);
        assert.match(transform, /the AI\\'s last revision was cut off \(the connection broke\), '\s*\+ 'so this is the version before it: '/);
    });
    it('has the plan name the devices that make the character asked for', () => {
        assert.match(transform, /CHARACTER — each device of the character asked for, with how it is written in this file and the bars',\s*'\s*it is in/);
        assert.match(llmDocs(), /\*\*The character asked for\*\* is heard through devices, each written into the piece where it\s+acts/);
        assert.match(transform, /every device of the character are written where',\s*'\s*the plan put them\./);
    });
    // Owner's request 2026-10-29: a poor piece's plan named its theme only in words, and no bar was
    // made of it; its phrases did not answer, it had no high point, its dynamics went against the plan.
    // Owner's trial 2026-10-29: a song's syllables matched its notes as words cut to fit them.
    it('has a song\'s words written whole first and the notes fitted to them', () => {
        const grammarDoc = llmDocs();
        assert.match(grammarDoc, /\*\*Write the words first, whole, then set them\*\*/);
        assert.match(grammarDoc, /the notes fit the words, never the words cut\s+to fit the notes/);
        assert.match(transform, /WORDS \(a song only\) — each line of the words written out WHOLE first/);
        assert.match(transform, /phrase that sings it, given that many notes \(the guide\\'s "A song with words"\)/);
    });
    // Owner's trials 2026-10-29: a pop song's tune mixed quarters, eighths and halves but never
    // crossed a beat — told now how often, and how.
    it('asks a pop song\'s sung line for a syncopation in at least half its bars', () => {
        const grammarDoc = llmDocs();
        assert.match(grammarDoc, /\*\*At least\s+half the bars of the sung line have a syncopation\*\*/);
        assert.match(grammarDoc, /`r8 g'8 b'8 d''8~ d''4 b'4 \|`/);
    });
    // Owner's trials 2026-10-29: a violin piece wrote `key b minor` over D major's sharps, printing
    // them again; a piano's left hand leapt from C2 to E4 in one broken chord.
    it('writes no key line for the relative key, and keeps a broken chord within a twelfth', () => {
        const grammarDoc = llmDocs();
        assert.match(grammarDoc, /A section in the RELATIVE key \(B\s+minor in a D major piece, A minor in C\) keeps the signature and writes no `key` line/);
        assert.match(grammarDoc, /A broken-chord figure\s+stays within an octave and a half \(a twelfth\)/);
    });
    // Owner's request 2026-10-29: the guide covers jazz, a violin, rock, pop and gospel, and how words are set.
    it('guides the other kinds of music and the setting of words, Japanese one mora a note', () => {
        const grammarDoc = llmDocs();
        for (const kind of ['**A solo instrument with piano**', '**A pop song**', '**Rock**', '**Gospel**', '**A song with words**']) {
            assert.ok(grammarDoc.includes(kind), kind);
        }
        assert.match(grammarDoc, /\*\*One sung unit a note, each written apart\*\*: the unit is the language's own beat of speech/);
        assert.match(grammarDoc, /Lily# splits nothing: text written together\s+is ONE unit on one note, with no warning\./);
        assert.match(grammarDoc, /\*\*The melody follows the words' own shape\*\*/);
        assert.match(transform, /WORDS \(a song only\) — each line of the words written out WHOLE first/);
        assert.match(transform, /gospel \(piano or organ under a voice or a choir\), a song \(a voice part/);
    });
    it('has the plan fill in every heading, the theme in notes, and the file held to the plan', () => {
        for (const heading of ['LENGTH AND FORM', 'KEY, METER, TEMPO', 'HARMONY', 'THEME', 'PHRASES', 'CLIMAX', 'PARTS', 'DYNAMICS', 'CHARACTER', 'WORDS', 'ENDING']) {
            assert.ok(transform.includes(`'   ${heading}`), heading);
        }
        assert.match(transform, /THEME — its one or two bars WRITTEN OUT here in this file\\'s notes, its rhythm with them/);
        assert.match(transform, /its cadence \(a pair answering, as the guide says\)/);
        assert.match(llmDocs(), /the second phrase of a pair starts as the first did and ends otherwise/);
        assert.match(transform, /Then hold',\s*'\s*the file to the plan:/);
        const grammarDoc = llmDocs();
        assert.match(grammarDoc, /\*\*Phrases that answer\*\*/);
        assert.match(grammarDoc, /\*\*One climax\*\*/);
        assert.match(grammarDoc, /a theme that is only words \("a rising broken chord"\) is not one yet/);
    });
    // Owner's report 2026-10-27: a piano piece labelled its staves 右手 and 左手.
    // Owner's report 2026-10-29: a composed piece's warning went back asking for "the corrected
    // edits"; the composer replied with bare <old>/<new>, read as the file, and the music check never ran.
    it('asks a composed piece\'s repair for the whole piece in <file>, not edits', () => {
        assert.match(transform, /const sameForm = compose\s*\?\s*'Reply with the whole corrected piece between <file> and <\/file>/);
        assert.match(transform, /content: compose\s*\?\s*`\$\{parsed\.problem\}\\nReply with the whole piece between <file> and <\/file>/);
    });
    // Owner's trial 2026-10-29: the music check's revision was the better piece, but three bars a beat long, and no round left.
    it('gives a composed piece one round more than an edit', () => {
        assert.match(transform, /const maxRepairs = compose \? MAX_REPAIR_ATTEMPTS \+ 1 : MAX_REPAIR_ATTEMPTS;/);
        assert.match(transform, /for \(let attempt = 0; attempt <= maxRepairs; attempt\+\+\)/);
    });
    // Owner's report 2026-10-29: a section mark the AI wrote filled green, its name hidden in a block.
    it('keeps a white box white when it paints the changes green or the notes playing orange', () => {
        assert.match(transform, /\.score \.chg \*:not\(\[fill="none"\]\):not\(\[fill="#FFFFFF"\]\):not\(\[fill="#ffffff"\]\):not\(\[fill="white"\]\) \{ fill: #2e9e43 !important; \}/);
        assert.match(transform, /\.score \.playing \*:not\(\[fill="none"\]\):not\(\[fill="#FFFFFF"\]\)/);
    });
    it('writes no composer line unless asked', () => {
        assert.match(transform, /'- Write no `composer` line unless the musician asks for one\.'/);
    });
    it('names the parts by the instrument, a solo piano none', () => {
        assert.match(transform, /A piece for piano alone names no part/);
        assert.match(transform, /Never "Right hand", "L\.H\.", 右手, 左手\./);
    });
    it('logs every message sent and every reply, the grammar left out', () => {
        assert.match(transform, /logged = logExchange\(chat, grammar, messages, logged\);\s*let raw: string;\s*try \{\s*raw = await chat\.send\(messages, token\);/);
        assert.match(transform, /throw err;\s*\}\s*logReply\(raw\);/);
        assert.match(transform, /createOutputChannel\('Lily# AI Exchange'\)/);
    });
    // Owner's report 2026-10-27: asked to fill in the composer, a model never told its name wrote
    // "OpenAI"; the composer line is still written only when asked (owner's decision).
    it('tells the composer its own name, and logs the model the request went to', () => {
        assert.match(transform, /`You are \$\{modelName\}\. If the musician asks for the composer to be written, the composer is you: `/);
        assert.doesNotMatch(transform, /otherwise write no composer line/);
        assert.match(transform, /\(to \$\{chat\.label\}\$\{chat\.modelDetail \? `: \$\{chat\.modelDetail\}` : ''\}\)/);
        const client = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'modelClient.ts'), 'utf8');
        assert.match(client, /get modelName\(\) \{ return isAutoModel\(model\) \? undefined : model\.name \|\| undefined; \}/);
        // An automatic pick VS Code keeps for itself goes to the next model at once (owner's report
        // 2026-10-28: "Model gpt-5.6-luna is only available to VS Code core" failed the compose).
        // …and so does Copilot's Auto picked by name, which handed the request to such a model
        // (owner's report 2026-10-28: Auto chosen, routed to gpt-5.6-luna, failed on every bar).
        assert.match(client, /const next = \(byName && !isAutoModel\(model\)\) \|\| token\.isCancellationRequested\s*\? undefined : await selectLmModel\(use\);/);
        assert.match(client, /const named = \/Model \(\\S\+\) is only available\/i\.exec\(refusal\)\?\.\[1\];/);
        // A refusal is remembered past the session, until VS Code's version changes, so a model
        // that cannot be used is no longer offered (owner's request 2026-10-28); Auto is not kept.
        assert.match(client, /if \(kept && kept\.version === vscode\.version\) \{\s*kept\.ids\.forEach\(id => coreOnly\.add\(id\)\);/);
        assert.match(client, /noteRefusal\(model\.id, isAutoModel\(model\)\);/);
        assert.match(client, /return \[\.\.\.copilot, \.\.\.rest\]\.filter\(m => !isCoreTaskModel\(m\) && !coreOnly\.has\(m\.id\)\);/);
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /rememberRefusalsIn\(context\.globalState\);/);
        assert.match(client, /model = next;\s*return await this\.send\(messages, token\);/);
        // Copilot's Auto says so, and signs no composer line (owner's report 2026-10-27).
        assert.match(client, /return isAutoModel\(model\) \? `\$\{model\.vendor\}\/auto`/);
        assert.match(client, /return `name "\$\{model\.name\}", vendor \$\{model\.vendor\}, id \$\{model\.id\}, family \$\{model\.family\}, version \$\{model\.version\}`/);
    });
});

describe('a revision is of the candidate', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');

    it('reads the reply against the candidate, and writes it as one change to the file', () => {
        assert.match(transform, /const parsed = toCandidateEdit\(base, raw\);/);
        assert.match(transform, /base = revisionBase\(work, candidate\);/);
        assert.match(transform, /\{ \.\.\.wholeFileEdit\(work, spliceCandidate\(parsed\.snap, parsed\.text\)\), reply: parsed\.reply \}/);
        assert.match(transform, /The file now reads as your candidate left it — copy each <old> /);
        // An ask that brings nothing goes back to the candidate, and so does the text read against.
        assert.match(transform, /prepared!\.messages\.length = askedFrom;\s*base = baseAsked;/);
    });

    it('carries the selection into the candidate\'s text', () => {
        // "abcdefghij": "cd" (2..4) became "CCCDDD" (2..8); the selection "fg" (5..7) moves by 4.
        const spans = [{ beforeLo: 2, beforeHi: 4, afterLo: 2, afterHi: 8 }];
        assert.deepEqual(carrySelection(spans, 5, 7), { start: 9, end: 11 });
        // A selection before the change stays; one ending where it starts does not take it in.
        assert.deepEqual(carrySelection(spans, 0, 2), { start: 0, end: 2 });
        // One inside the change widens to the whole of it.
        assert.deepEqual(carrySelection(spans, 3, 6), { start: 2, end: 10 });
        // An insertion at the selection's end is taken in.
        assert.deepEqual(carrySelection([{ beforeLo: 4, beforeHi: 4, afterLo: 4, afterHi: 7 }], 1, 4), { start: 1, end: 7 });
    });
});

// Owner's request 2026-10-27: a piece the AI composes always has a title that fits it.
describe('a composed piece has a title', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');

    it('is asked for, never seeded with a placeholder, and asked for alone when it does not come', () => {
        assert.match(transform, /Give the piece a title of its own that '\s*\+ 'fits its music and its mood/);
        assert.doesNotMatch(transform, /const start = 'title "New Piece"/);
        assert.match(transform, /if \(!had \|\| isPlaceholderTitle\(had\)\) \{\s*const title = await titleFor\(chat, text, composeAsked \?\? instruction\);/);
    });

    it('reads, cleans and writes a title', () => {
        assert.equal(titleOf('// note\ntitle "黄昏のワルツ"\ntime 3/4\n'), '黄昏のワルツ');
        assert.equal(titleOf('time 3/4\n'), null);
        assert.ok(isPlaceholderTitle('New Piece'));
        assert.equal(cleanTitle('Title: "Autumn Elegy"\n'), 'Autumn Elegy');
        assert.equal(cleanTitle('「黄昏のワルツ」'), '黄昏のワルツ');
        assert.equal(cleanTitle('Untitled'), null);
        assert.equal(withTitle('title "New Piece"\ntime 3/4\n', 'Autumn Elegy'), 'title "Autumn Elegy"\ntime 3/4\n');
        // No title line: after the leading comments.
        assert.equal(withTitle('// a\ntime 3/4\n', 'Autumn Elegy'), '// a\ntitle "Autumn Elegy"\ntime 3/4\n');
    });
});

// Owner's rule 2026-10-27: one staff only when it is asked for — a lead sheet, a melody alone, a
// solo; anything else (a jazz piece too: a combo) has its accompaniment.
describe('one staff only when asked for', () => {
    const transform = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'aiTransform.ts'), 'utf8');

    it('knows the requests that ask for one staff', () => {
        for (const yes of ['ジャズのリードシートを書いて', 'a jazz lead sheet in F', 'この歌詞にメロディだけ付けて', 'フルート独奏の曲', 'an unaccompanied cello piece', 'a guitar solo']) {
            assert.ok(asksForOneStaff(yes), yes);
        }
        for (const no of ['ジャズの曲を書いて', 'a dramatic string quartet', 'エレキギターとドラムの曲']) {
            assert.ok(!asksForOneStaff(no), no);
        }
    });

    it('counts the parts, and sends a composed piece of one back unless one staff was asked for', () => {
        assert.equal(partCount('part lead { clef treble }\nsection A { lead { c1 | } }\n'), 1);
        assert.equal(partCount('part rh { clef treble }\npart lh { clef bass }\n'), 2);
        assert.match(transform, /if \(compose && partCount\(reconstructed\) < 2 && !asksForOneStaff\(composeAsked \?\? instruction\)\) \{\s*music\.unshift\(/);
    });
});
