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
import { REVIEW_READING, asksForOneStaff, carrySelection, cleanTitle, isPlaceholderTitle, isProblem, partCount, titleOf, toCandidateEdit, withTitle, guidePages } from '../src/aiTransformCore';

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
    // Owner's direction 2026-11-01: a song to sing and play yourself takes the alto's range (the
    // range check then holds it), a lead sheet's shape, and always the guitar's diagrams.
    it('says how a song to sing and play yourself is written', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*A song to sing and play yourself\*\* \(弾き語り;/);
        assert.match(guide, /`instrument alto` \(F3–F5\) for a woman's song and when the request names no\s+voice; `instrument baritone` \(G2–G4\) for a man's/);
        assert.match(guide, /`score \{ chords NAME plays rhythm  staff singer  lyrics words \}`/);
        // Owner's design 2026-11-01: the verses are `verse N { }` in one track, not a track a verse.
        assert.match(guide, /\*\*The verses\*\*: one lyrics track, `verse 1 \{ … \} verse 2 \{ … \}` inside it/);
        assert.match(guide, /\*\*The diagrams, always\*\*: `layout \{ chordDiagrams all  chordList true \}`/);
    });
    // Owner's review 2026-11-01: a "bossa nova" of its chords, a bass and two off-beat chords a bar, heard as no bossa nova.
    it('says a style named is its rhythm first, a bossa nova\'s two bars above all', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*A style named is its rhythm first\*\*/);
        assert.match(guide, /`e,4 r4 b,2 \| e,4 r4 e,2 \|`/);
        assert.match(guide, /1, 4, 7 of the first bar and 3, 6 of the second/);
        // …and over a chords row, as strokes (the owner's bossa.lys, 2026-11-01).
        assert.match(guide, /`strokes bossa \{ section A \{ 4\. 4\. 4 \| r4 4 r8 4\. \| %2 \| %2 \| %2 \| \} \}`/);
        // …and a guitar alone, the thumb under the fingers (the owner's page, 2026-11-01).
        assert.match(guide, /\*\*A guitar alone\*\* plays all three at once/);
        // …its tune a voice of its own (the owner's review of Ameagari-no-Cafe, 2026-11-01).
        assert.match(guide, /r4 c''4 b'8 a'4\.~ \| a'2 fis'2 \|/);
        assert.match(guide, /A dynamic goes in one voice\s+only/);
        // …and one hand plays them (the owner's question of Ameagari-no-Terrace, 2026-11-01).
        assert.match(guide, /four notes\s+struck at once at most/);
        assert.match(guide, /<g c' e'>4\. <g c' e'>4\. <g c' e'>4 \| r4 <fis c' e'>4 r8 <fis c' e'>4\. \|/);
        assert.match(guide, /a,2 a,2 \| d2 d2 \|/);
    });
    // Owner's trial 2026-10-29: a "dramatic" quartet's tremolo and sforzando were all in two bars.
    it('says the character\'s devices run through the piece, strongest at the climax', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*The devices run\s+through the piece\*\*, not spent in one place: each comes back in more than one section/);
    });
    // Owner's review 2026-10-29: a wedding waltz's A landed on the tonic at bar 4 and closed V–IV–I.
    it('says how a cadence is checked, and that the first half does not land on the tonic', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /the chord just before a closing I is V \(or V7\), with IV or ii before it — never V, then IV,\s+then I/);
        assert.match(guide, /The first half's last melody note is NOT the tonic/);
    });
    // Owner's review 2026-10-29: a wedding piece's B had a left hand of bare fifths and closed on vi.
    it('says a chord sounds its third, and a middle section ends on V', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /A chord\s+sounds its third — the accompaniment has it, or the melody does on the beat/);
        assert.match(guide, /\(the B of A–B–A, a bridge\) ends on the home key's V/);
    });
    // Owner's report 2026-10-29: a revision's 7/8 bar was written in, the hands out of step after it.
    it('turns down a proposal whose bars do not add up, every time', () => {
        assert.match(transform, /const BAR_LENGTH_CODES = new Set\(\['LYS2001', 'LYS2002', 'LYS2004', 'LYS2006', 'LYS2007', 'LYS2019'\]\);/);
        const fn = transform.slice(transform.indexOf('async function proposeScore('));
        // …the score's own as well as the proposal's (owner's decision 2026-10-30).
        const misfit = fn.indexOf('const misfit = diags.filter(d => d.Severity === \'warning\' && BAR_LENGTH_CODES.has(d.Code ?? \'\'));');
        assert.ok(misfit > 0 && misfit < fn.indexOf('musicCheckedFor.has(uri.toString())'));
        assert.match(fn, /return `Not shown to the musician: its bars do not add up/);
        // A note out of its instrument's range, the same (owner's decision 2026-10-30).
        assert.match(transform, /const OUT_OF_RANGE_CODE = 'LYS1052';/);
        const unplayable = fn.indexOf('const unplayable = diags.filter(d => d.Severity === \'warning\' && d.Code === OUT_OF_RANGE_CODE);');
        assert.ok(unplayable > misfit && unplayable < fn.indexOf('musicCheckedFor.has(uri.toString())'));
        // A syllable with no note, the same (owner's decision 2026-11-01: a chorus's last word was lost).
        assert.match(transform, /const LOST_SYLLABLE_CODE = 'LYS4001';/);
        const unsung = fn.indexOf('const unsung = diags.filter(d => d.Severity === \'warning\' && d.Code === LOST_SYLLABLE_CODE);');
        assert.ok(unsung > unplayable && unsung < fn.indexOf('musicCheckedFor.has(uri.toString())'));
        assert.match(fn, /return `Not shown to the musician: words would be lost — a syllable with no note to sing it on\./);
        // A tune held against its chords eight times or more, the same (owner's decision 2026-11-01:
        // twelve held clashes were written in "as colour").
        assert.match(fn, /const clashing = heldClashesOf\(kept\);\s*if \(clashing && clashing\.count >= CLASHES_TURNED_DOWN\) \{[\s\S]*?return `Not shown to the musician: its tune stands against its chords/);
        assert.ok(fn.indexOf('const clashing = heldClashesOf(kept);') < fn.indexOf('const brought = kept.filter('));
        // Chords no hand can hold, eight or more, the same (owner's decision 2026-11-01: a solo guitar's 14).
        assert.match(fn, /const unheld = unfingerableOf\(diags\.filter\(d => d\.Severity === 'warning'\)\);\s*if \(unheld\.length >= UNFINGERABLE_TURNED_DOWN\) \{[\s\S]*?return `Not shown to the musician: its player cannot play it/);
        assert.ok(fn.indexOf('const unheld = unfingerableOf(') < fn.indexOf('const brought = kept.filter('));
        // What the check still finds is said by Lily# itself, the chat not always telling it
        // (owner's request 2026-11-01).
        assert.match(fn, /const stillFound = \[\.\.\.kept, \.\.\.added\.map\(w => w\.Message\)\];[\s\S]*?point\(s\) from the check left in it\.`,\s*'Show Log'\)/);
        // Singers on one line, the same (owner's decision 2026-11-01).
        assert.match(fn, /const oneLine = singersInOneLineOf\(kept\);\s*if \(oneLine\.length > 0\) \{[\s\S]*?return 'Not shown to the musician: its parts are not parts/);
        assert.ok(fn.indexOf('const oneLine = singersInOneLineOf(kept);') < fn.indexOf('const brought = kept.filter('));
        assert.match(fn, /return `Not shown to the musician: its players cannot play it/);
    });
    // Owner's request 2026-10-29: the chat listed the folder in a terminal to see that a name was free.
    it('gives the chat a tool to check a new file name, and asks for nothing in a terminal', () => {
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"onLanguageModelTool:lilysharp_checkName"/);
        assert.match(pkg, /"name": "lilysharp_checkName",\s*"toolReferenceName": "checkName"/);
        assert.match(transform, /lm\.registerTool<ReadScoreInput>\('lilysharp_checkName'/);
        assert.match(transform, /const taken = async \(p: string\) => fs\.existsSync\(p\) \|\| \(await scoreDocument\(p\)\) !== undefined;/);
        assert.match(transform, /`\$\{stem\}-\$\{n\}\$\{ext\}`/);
        assert.match(transform, /check it with the lilysharp_checkName tool \(#checkName\)/);
        assert.match(transform, /nor anything else in a terminal: Lily#\\'s tools and your file tools do all a new piece needs/);
    });
    // Owner's review 2026-10-29: a wedding piece's melody dropped a ninth from its climax.
    it('says a wide leap is followed by a step back, above all from the high note', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /a leap wider than a fifth is\s+followed by a step back the other way, above all from the high note/);
    });
    // Owner's review 2026-10-29: a 6/8 wedding piece sang 8 8 4 4 in four bars, and lasted 52 seconds.
    it('says the rhythm keeps a compound meter\'s beats, and a length in minutes', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /in 6\/8 \(9\/8, 12\/8\) a beat is three eighths/);
        assert.match(guide, /a minute and a half to three\s+minutes played — a shorter bar or a quicker beat takes more bars/);
        // Owner's report 2026-10-29: told it was short, the chat slowed ♩ = 72 to 48.
        assert.match(guide, /never a slower tempo than the character asks for, to fill the\s+time/);
    });
    // Owner's report 2026-10-29: a piece composed in Copilot's chat was written as absolute in a
    // relative file, and its pitches flew off — the guide, which the chat is given too, says to start absolute.
    it('says a new piece starts in absolute octaves', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*Start in absolute octaves\*\*: a new piece begins with `octave absolute` \(and `pitch concert`\)/);
    });
    // Owner's request 2026-10-29: the chat edits in absolute octaves, as Ask AI does, and the
    // musician sees and gets the file's relative octaves.
    it('lets the chat read a relative score in absolute octaves, and brings its proposal back', () => {
        assert.match(transform, /const absolute = await chatAbsolute\(deps, text\);/);
        assert.match(transform, /'lilysharp\/aiOctaves', \{ Text: text, ToAbsolute: true \}/);
        assert.match(transform, /ABSOLUTE_LINE\.test\(text\) && !ABSOLUTE_LINE\.test\(current\) && await chatAbsolute\(deps, current\) !== undefined/);
        assert.match(transform, /'lilysharp\/aiOctaves', \{ Text: text, ToAbsolute: false, Original: current \}/);
    });
    // Owner's report 2026-10-29: a piece composed in the chat opened in absolute octaves.
    it('opens the chat\'s new piece in relative octaves, as Compose with AI\'s', () => {
        assert.match(transform, /const musicians = await asMusicianWrites\(deps, client, text\);[\s\S]*?const written = await writeWhole\(uri, exists, doc, musicians\);/);
        assert.match(transform, /text = await asMusicianWrites\(deps, client, text\);\s*const eol/);
        assert.match(transform, /'lilysharp\/convertOctaves', \{ Text: text, Absolute: false, DropPartOctaves: true, DropConcertPitch: true \}/);
    });
    // Owner's request 2026-10-29: a Compose menu that hands the request to Copilot's chat, its
    // thinking in view — the file started absolute, no Lily# check yet (the owner's choice).
    it('is handed to Copilot\'s chat by a menu of its own, the file started in absolute octaves', () => {
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        // Named for Copilot in the palette and in File > New File… alike (owner's decisions 2026-10-29
        // and 2026-11-07: the one composer).
        assert.match(pkg, /"command": "lilysharp\.composeWithAiInChat",\s*"category": "Lily#",[\s\S]*?"title": "Compose with Copilot…"/);
        assert.match(pkg, /"command": "lilysharp\.composeWithAiInChat",\s*"group": "file@2"/);
        assert.match(pkg, /"command": "lilysharp\.composeWithAiInChat",[^}]*"shortTitle": "Lily# Score, Composed with Copilot…"/);
        assert.match(transform, /registerCommand\('lilysharp\.composeWithAiInChat', \(\) => runComposeInChat\(deps\)\)/);
        const fn = transform.slice(transform.indexOf('async function runComposeInChat'), transform.indexOf('async function runAiTransform('));
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
        // Lily#'s tool to propose it through, which compiles it: no compiler run by the chat (owner's
        // request 2026-10-29: its check on a temp copy asked "Run terminal?" mid-composing) — the
        // bundled lysc named only for a file written without the tool.
        const steps = transform.slice(transform.indexOf('function newPieceSteps('), transform.indexOf('async function writeWhole'));
        assert.match(steps, /'Propose it, whole, with the lilysharp_proposeScore tool \(#proposeScore\), giving that path and `draft: true`: it compiles the '/);
        // Owner's report 2026-11-08: plan and score in one reply ran past ten minutes and were cut off —
        // the plan its own reply, the notes into the draft a part at a time.
        // Owner's request 2026-11-08: the reason and the limit said plainly.
        assert.match(steps, /'Answer each reply within five minutes, thinking included \(roughly 30,000 tokens of thinking\): a long think '/);
        // No minutes for the cut itself: seen with one model only, it may be another's (owner's note 2026-11-08).
        assert.match(steps, /'can be cut off by the connection, and the whole reply is lost\. So think out the plan, not the notes, '/);
        assert.match(steps, /'and think each section\\'s notes as you draft that section\. Write in short steps, each its own reply — the answer '/);
        // The name first, alone; each tool's answer says the step after it (owner's trials 2026-11-08).
        assert.match(steps, /'to each tool call says the step after it: first the piece\\'s title and the lilysharp_checkName call for its file, '/);
        assert.match(transform, /return `Free: \$\{path\.basename\(file\)\} is not taken in \$\{path\.dirname\(file\)\} — propose the new piece there\. ` \+ PLAN_NEXT;/);
        assert.match(transform, /const PLAN_NEXT = 'Next: the plan alone — /);
        assert.match(steps, /then the plan, and the draft begun with the lilysharp_draftScore '/);
        assert.match(steps, /a new piece needs\. If that tool is not available, write the piece into the file instead'\s*\+ \(lysc \? `, and check it with/);
        assert.match(steps, /do not run a compiler '/);
        assert.ok(!/on a copy of your text/.test(steps));
        // Owner's report 2026-11-01: the score was handed to invoke_canvas_action.
        assert.match(steps, /'Call that tool directly, by its own name, never through a canvas action or another tool\.\\n'/);
        // Owner's request 2026-10-29: no save dialog — an unsaved score, saved when the musician wants
        // it kept; and, the same day, no "Composed.lys": the folder is given, the chat names the file
        // after the piece's title.
        assert.ok(!/showSaveDialog|writeFileSync|'Composed|`Composed/.test(fn));
        assert.match(fn, /const query = `Compose a new piece of music in Lily# in the folder \$\{folder\} \(a new file: your proposal opens it in the editor\)\.\\n`/);
        const guide = transform.slice(transform.indexOf('async function readGuide('), transform.indexOf('function newPieceHeader('));
        assert.match(guide, /'Name a new file after the piece\\'s title, in letters, digits and hyphens \(a title in another script '/);
        assert.match(guide, /never a placeholder \(Composed, Untitled\), and a name no file in '\s*\+ 'that folder has yet — check it with the lilysharp_checkName tool/);
        assert.ok(!/\+ 'folder \(e\.g\. Composed\.lys\)/.test(guide));
    });
    // Owner's decision 2026-11-07: the plan Compose with AI's prompt asked for went missing from the
    // chat's composing — the guide spoke of "the plan" to a chat told to write none. The guide holds
    // its headings now, and the steps ask for it before any note.
    // Owner's trials 2026-11-07: the same guide, GPT-6 Luna kept little of it and Claude Sonnet 5.5
    // nearly all — and the chat's Auto had taken Luna for "a short request".
    it('tells the musician to pick a strong model in the chat', () => {
        const fn = transform.slice(transform.indexOf('async function runComposeInChat'), transform.indexOf('async function runAiTransform('));
        assert.match(fn, /'Pick a strong model in the chat\\'s model menu: Auto may take a light one for a short request\.'/);
    });
    it('asks the chat for the plan, its headings in the composing guide', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*Plan before the first note\.\*\* Write the plan in your reply before you propose the piece/);
        let at = 0;
        for (const heading of ['LENGTH AND FORM', 'KEY, METER, TEMPO', 'HARMONY', 'THEME', 'PHRASES', 'CLIMAX', 'PARTS', 'DYNAMICS', 'CHARACTER', 'WORDS', 'ENDING']) {
            const i = guide.indexOf(`- **${heading}**`, at);
            assert.ok(i > 0, `${heading}, in order`);
            at = i;
        }
        assert.match(guide, /THEME\*\* — its one or two bars WRITTEN OUT in this file's notes/);
        // Before "What makes it music": read on the guide's first page, with how a piece begins.
        assert.ok(guide.indexOf('**Plan before the first note.**') < guide.indexOf('What makes it music:'));
        const steps = transform.slice(transform.indexOf('function newPieceSteps('), transform.indexOf('async function writeWhole'));
        assert.match(steps, /'Before any note, write in your reply the plan the composing guide asks for — its "Plan before the first note", '/);
    });
    // Owner's goals 2026-11-07: a remembered melody, chords past a stock pattern, a countermelody of
    // its own, a key change with a story, an orchestra written as it sounds, one piece throughout.
    it('says how a tune grows, chords move, a countermelody, a key change, an orchestra and one piece', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*The tune grows from its motif\*\*: every phrase of the melody is made of the theme's motif/);
        // Owner's trial the same day: a song sang its chorus once, between its verses, and ended on a verse.
        assert.match(guide, /\*\*The chorus comes back after every verse\*\*: verse 1, chorus, verse 2, chorus — the form names\s+the chorus again each time/);
        assert.match(guide, /A song does not end on\s+a verse, nor on its hook said once in a coda/);
        // Owner's trials the same day: the motif's rhythm poured into half the bars, two models.
        assert.match(guide, /\*\*The motif's shape is kept, its rhythm is not\s+frozen\*\*: its own rhythm in one bar of two at most/);
        assert.match(guide, /\*\*Harmony that moves\*\*: past the first phrase, colour the progression/);
        // Owner's trial the same day: a flute's A♮ over the piano's F minor.
        assert.match(guide, /\*\*The\s+chord changes with the note\*\*: a melody's note from outside the key belongs to the accompaniment's\s+chord as well/);
        assert.match(guide, /A pop\s+song's loop of four chords, a blues's twelve bars and a hymn's plain triads are their own idiom/);
        assert.match(guide, /\*\*A countermelody is a second tune\*\*, not a harmony part/);
        assert.match(guide, /\*\*A key change is an event\*\*: it comes where the piece turns/);
        assert.match(guide, /\*\*An orchestra or a string ensemble\*\* writes each instrument where it sounds well/);
        assert.match(guide, /\*\*The contrast is made of the theme too\*\*/);
        // …and the plan decides each.
        assert.match(guide, /\*\*KEY, METER, TEMPO\*\* — and a key change, if any: its bar, the chord that prepares it, the way home\./);
        assert.match(guide, /where it is coloured \(a\s+secondary dominant, a borrowed chord, a deceptive cadence\) — or that the style keeps it plain\./);
        assert.match(guide, /a countermelody, if any, and the\s+bar of its own peak\./);
    });
    // Owner's request 2026-11-07: asked to compose with a score open, the chat took that score for
    // "the .lys file you were given" and proposed the new piece over it. A new piece is a new file,
    // said where the chat reads how to begin, in the tool it proposes with, and in every request.
    it('writes a new piece into a new file, never over a score that is open', () => {
        const guide = transform.slice(transform.indexOf('async function readGuide('), transform.indexOf('function newPieceHeader('));
        assert.match(guide, /'Compose the new piece in Lily# as a NEW \.lys file\. Never write it into a score that is open, attached or '/);
        assert.match(guide, /Only an empty \.lys, or a file the musician asks in so many words to write the piece into, takes it\./);
        assert.ok(!/for the \.lys file you were given/.test(guide));
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /A NEW piece goes to a new file \(name it with lilysharp_checkName\), never over a score that is open or attached/);
        const build = fs.readFileSync(path.join(__dirname, '..', '..', 'esbuild.js'), 'utf8');
        assert.match(build, /'A new piece goes into a NEW \.lys file, never over an open score unless asked for that file\.',/);
    });
    // Owner's request 2026-10-29: Copilot's chat proposes a score through Lily#'s tool, the musician
    // deciding on the score — compiled first, an error sent back unseen.
    it('gives Copilot\'s chat a tool to propose a score with, decided on in the candidate panel', () => {
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"name": "lilysharp_proposeScore",\s*"toolReferenceName": "proposeScore"/);
        assert.match(pkg, /"onLanguageModelTool:lilysharp_proposeScore"/);
        // The text, or the draft written a part at a time (owner's report 2026-11-08).
        assert.match(pkg, /"draft": \{ "type": "boolean"[^\n]*\}\s*\},\s*"required": \["file"\]/);
        assert.match(pkg, /"name": "lilysharp_draftScore",\s*"toolReferenceName": "draftScore"/);
        assert.match(pkg, /"onLanguageModelTool:lilysharp_draftScore"/);
        assert.match(transform, /lm\.registerTool<DraftInput>\('lilysharp_draftScore'/);
        assert.match(transform, /lm\.registerTool<ProposeScoreInput>\('lilysharp_proposeScore'/);
        const fn = transform.slice(transform.indexOf('async function proposeScore'), transform.indexOf('async function runComposeInChat'));
        assert.match(fn, /if \(errors\.length > 0\) \{[\s\S]*?return `Not shown to the musician: it does not compile\./);
        assert.match(fn, /await reviewOnScore\(deps, renderBefore, renderAfter, caption, wholeTextMarkup\(current, text, changed\)/);
        assert.match(fn, /return 'Accepted: the musician accepted the proposal, and it is in the editor, not saved yet/);
        // The file named by its own name, where it is now, for the chat to tell (owner's report
        // 2026-10-29: the chat called the file by the piece's title).
        assert.match(fn, /return `Written\$\{goesOn \? ' again, in place of the piece written before' : ''\}: the new piece is `\s*\+ `\$\{path\.basename\(currentUri\(uri\)\.fsPath\)\} — call the file by that name, `/);
        assert.match(fn, /`\$\{path\.basename\(currentUri\(uri\)\.fsPath\)\} \(call the file by that name; it may have been renamed\)\. Read it `/);
        // Owner's request 2026-10-29: Accept puts it in the editor and saves nothing.
        assert.ok(!/\.save\(\)/.test(fn));
        // Owner's requests 2026-10-29: what to change is said in the chat — no Ask Again — and a new
        // piece has no Reject (rejected, it would leave an empty file); the panel stays for the next
        // proposal, and a message sent in the chat before deciding is told as such.
        assert.ok(!/showInputBox/.test(fn));
        assert.match(fn, /input\.note \?\? null, true\);/);
        // Owner's request 2026-10-30: Reject closes the panel, as Ask AI's does, and the chat says what
        // the rejected proposal changed before asking what to change.
        assert.match(fn, /deps\.log\('AI proposal \(chat\): rejected\.'\);\s*candidatePanel\?\.dispose\(\);/);
        assert.match(fn, /'Tell them in a line or two what the rejected proposal changed \(and anything it left undone\), then '/);
        // Owner's request 2026-10-29: a new piece goes straight into the file, open in the editor —
        // no panel; the music check still speaks once first.
        assert.match(fn, /if \(isEmptyScore\(current\) \|\| goesOn\) \{[\s\S]*?const musicians = await asMusicianWrites\(deps, client, text\);[\s\S]*?const written = await writeWhole\(uri, exists, doc, musicians\);/);
        // Owner's request 2026-11-01: a composed piece opened, and a panel over it at once — the chat
        // had proposed its piece again, unasked. While the composing goes on (the text as the chat wrote
        // it, nothing said in the chat since, by the Copilot session log), the piece is written again.
        assert.match(fn, /const goesOn = doc !== undefined && !isEmptyScore\(current\) && await composingGoesOn\(doc, current\);/);
        assert.match(fn, /newPieces\.set\(written\.uri\.toString\(\), \{ text: written\.getText\(\), at: Date\.now\(\) \}\);/);
        const goesOn = transform.slice(transform.indexOf('async function composingGoesOn('), transform.indexOf('/** The unsaved document named by the path'));
        assert.match(goesOn, /if \(!piece \|\| piece\.text !== current\) \{\s*return false;/);
        assert.match(goesOn, /const spoken = await chatSpokenAt\(\);\s*return spoken === undefined \|\| spoken <= piece\.at;/);
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
        assert.match(fn, /return `\$\{path\.basename\(doc\.uri\.fsPath\)\} \(\$\{state\}\), \$\{doc\.lineCount\} lines, as it stands:\\n\$\{text\}\$\{said\}`;/);
        const build = fs.readFileSync(path.join(__dirname, '..', '..', 'esbuild.js'), 'utf8');
        assert.match(build, /'\*\*Reading a \.lys file\*\*: read it through the `lilysharp_readScore` tool \(#readScore\), not from the disk —',/);
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /`\$\{doc\.uri\.fsPath\} \(not saved — read it with #readScore\)`/);
    });
    // Owner's proposal 2026-10-30: the composing checks, for a person who asks for a review.
    it('gives Copilot\'s chat a tool to review a score, which writes nothing and says how to read its report', () => {
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.match(pkg, /"name": "lilysharp_reviewScore",\s*"toolReferenceName": "reviewScore"/);
        assert.match(pkg, /"onLanguageModelTool:lilysharp_reviewScore"/);
        assert.match(pkg, /"enum": \["beginner", "intermediate", "advanced"\]/);
        assert.match(pkg, /"enum": \["key", "rhythm", "form", "parts", "notation"\]/);
        assert.match(transform, /lm\.registerTool<ReviewScoreInput>\('lilysharp_reviewScore'/);
        const fn = transform.slice(transform.indexOf('async function reviewScore('), transform.indexOf('async function compositionFindings('));
        assert.match(fn, /'lilysharp\/reviewScore', \{ Text: doc\.getText\(\), Audience: input\?\.audience, Focus: input\?\.focus \}/);
        assert.match(fn, /\$\{REVIEW_READING\}/);
        assert.match(fn, /active\?\.languageId === 'lilysharp' \? active : undefined/);
        assert.doesNotMatch(fn, /\.edit\(|applyEdit|writeFile|proposeScore\(/);
        const build = fs.readFileSync(path.join(__dirname, '..', '..', 'esbuild.js'), 'utf8');
        assert.match(build, /'\*\*Reviewing a piece\*\*: asked to review a \.lys file or give feedback on it, use the `lilysharp_reviewScore` tool \(#reviewScore\)',/);
        assert.match(REVIEW_READING, /Start with what works \(`strengths`\)/);
        assert.match(REVIEW_READING, /at most five points, mistakes first/);
        assert.match(REVIEW_READING, /Check `fine_when` against the piece/);
        assert.match(REVIEW_READING, /Do not change the score/);
        assert.match(REVIEW_READING, /If `compiles` is false/);
    });
    // Owner's request 2026-10-29: the instructions files go only with a request about a .lys, so a
    // piece asked for in the chat with none open was written without them — a tool gives them.
    it('gives Copilot\'s chat the grammar and the composing guide by a tool, found by what it is for', () => {
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        // Named, described and tagged as the other two (owner's request, the same day: consistent).
        assert.match(pkg, /"name": "lilysharp_readGuide",\s*"toolReferenceName": "readGuide",\s*"displayName": "Read the Lily# guide"/);
        assert.match(pkg, /"onLanguageModelTool:lilysharp_readGuide"/);
        const tags = '"tags": ["lilysharp", "lys", "music", "score", "notation", "compose", "song"]';
        assert.equal(pkg.split(tags).length - 1, 6);   // proposeScore, draftScore, readScore, reviewScore, checkName, readGuide
        assert.match(pkg, /"modelDescription": "Use this before composing music — a piece, a song, a score — or writing or changing Lily# \(\.lys\) text/);
        assert.match(transform, /lm\.registerTool<ReadGuideInput>\('lilysharp_readGuide'/);
        const fn = transform.slice(transform.indexOf('async function readGuide('), transform.indexOf('function newPieceSteps('));
        // The instructions files Copilot is given with a .lys in hand, their front matter left out.
        assert.match(fn, /guidePages\(built\('lilysharp\.instructions\.md'\) \?\? await loadGrammar\(deps\), GUIDE_PAGE_BYTES\)/);
        // "start" is how a new piece begins alone; "composing" is that and the composing guide.
        assert.match(fn, /guidePages\(built\('lilysharp-compose\.instructions\.md'\) \?\? await loadComposeGuide\(deps\),/);
        assert.match(fn, /return topic === 'start' \? start : paged\('composing guide', await guide\(\), start \+ '\\n'\);/);
        // In parts each read whole (owner's report 2026-10-29: whole, the guide went to a temp file,
        // read back with PowerShell — "Run in terminal?").
        assert.match(pkg, /"part": \{ "type": "integer", "minimum": 1,/);
        assert.match(fn, /`Read part \$\{part \+ 1\} next: lilysharp_readGuide, topic/);
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
        // A .lys read through Lily#'s tool, not a file viewer (owner's report 2026-10-30).
        assert.match(here, /'To read a \.lys file, attached or named, use lilysharp_readScore \(search the tools for it\),',/);
        // …which says what Lily# finds in it, the spot on a long line quoted (the same day: the agent
        // counted a drum line's phrases in a terminal script).
        assert.match(here, /'Run no script or terminal on a score\.',/);
        // The voicings a player can hold, asked before a chord is written (owner's request 2026-10-30).
        // …with the part's sound: a distorted one is answered power chords first (owner's request 2026-11-01).
        assert.match(here, /ask lilysharp_chordVoicings what its player can hold, its midiInstrument as sound\.',/);
        assert.match(transform, /Sound: typeof input\.sound === 'string' \? input\.sound : null,/);
        assert.match(pkg, /"sound": \{ "type": "string", "description": "The part's midiInstrument/);
        assert.match(pkg, /"onLanguageModelTool:lilysharp_chordVoicings"/);
        assert.match(pkg, /"name": "lilysharp_chordVoicings",\s*"toolReferenceName": "chordVoicings"/);
        assert.match(pkg, /"onLanguageModelTool:lilysharp_listVoices"/);
        assert.match(pkg, /"name": "lilysharp_listVoices",\s*"toolReferenceName": "listVoices"/);
        assert.match(transform, /lm\.registerTool<ListVoicesInput>\('lilysharp_listVoices'/);
        assert.match(transform, /lm\.registerTool<ChordVoicingsInput>\('lilysharp_chordVoicings'/);
        assert.match(transform, /client\.sendRequest<\{ Text: string \}>\('lilysharp\/chordVoicings'/);
        const read = transform.slice(transform.indexOf('async function readScore('), transform.indexOf('async function checkName('));
        assert.match(read, /const said = await saidOf\(deps, text\);/);
        assert.match(read, /\+ absolute \+ said;/);
        assert.match(read, /as it stands:\\n\$\{text\}\$\{said\}`;/);
        assert.match(transform, /return diagLines\(diags, d => text \? spotOf\(d, text\) \+ rangeFixOf\(d, text\) : ''\);/);
        assert.ok(here.length < 2000, 'kept to a few lines');
        // Asked how to write Lily#, the chat teaches from the path (owner's request 2026-11-02), wherever it is asked.
        assert.match(here, /'Asked how to write Lily# or \.lys, or to be taught it, read that tool with topic "learn" and teach from it\.',/);
        assert.match(build, /'\*\*Teaching Lily#\*\*: asked how to write it, or to be taught it, read the `lilysharp_readGuide` tool, topic `learn`, and teach from that path\.',/);
        assert.match(build, /fs\.copyFileSync\(path\.resolve\(__dirname, '\.\.', '\.\.', 'docs', 'LEARN_FOR_LLM\.md'\), path\.join\(outDir, 'LEARN_FOR_LLM\.md'\)\);/);
        assert.match(pkg, /"enum": \["grammar", "composing", "start", "learn"\]/);
        assert.match(pkg, /topic \\"learn\\" gives the path for teaching Lily#/);
        const guided = transform.slice(transform.indexOf('async function readGuide('), transform.indexOf('function newPieceHeader('));
        assert.match(guided, /if \(topic === 'learn'\) \{\s*const teaching = built\('LEARN_FOR_LLM\.md'\);\s*return teaching \? paged\('teaching path', guidePages\(teaching, GUIDE_PAGE_BYTES\)\)/);
        // The path's pages, each small enough to be read whole, and none of them losing an example across a page.
        const learn = fs.readFileSync(path.join(__dirname, '..', '..', '..', '..', 'docs', 'LEARN_FOR_LLM.md'), 'utf8');
        const pages = guidePages(learn, 7000);
        assert.ok(pages.length >= 2 && pages.every((p: string) => Buffer.byteLength(p) <= 7000));
        assert.ok(pages.every((p: string) => (p.match(/```/g) ?? []).length % 2 === 0), 'no example is cut across a page');
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
        const compose = transform.slice(transform.indexOf('async function runComposeInChat'), transform.indexOf('async function runAiTransform('));
        assert.match(compose, /lilysharp_readGuide, topic "composing"/);
    });
    // Owner's trials 2026-10-29: a chat's hymns crossed the alto under the tenor, set the alto in the
    // tenor's range, and rocked the bass between root and fifth — the guide had no hymn.
    // Owner's request 2026-11-10, session 1217: who sings, for any song, and the tool that names the voices.
    it('says how to name a singer, from the voices this machine has', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /To name a voice, call `lilysharp_listVoices` first and copy a name it answers; never guess one\./);
        assert.match(guide, /narrow it with `language`[\s\S]*`gender`[\s\S]*`provider`/);
        assert.match(guide, /singer "neutrino\/MERROW" sings words on sop/);
        assert.match(guide, /Names with commas between them sing the\s+part together \(`singer "neutrino\/MERROW","diffsinger\/TIGER" sings words`\)\./);
    });
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
    // Owner's trial 2026-10-29: a beginner's right-hand piece reached A and B outside the five-finger
    // place, wrote no fingering, and leapt a seventh from `b'` to its last note `c'`.
    it('says how a piece for a beginner is written', () => {
        const guide = fs.readFileSync(path.join(docs, 'COMPOSE_FOR_LLM.md'), 'utf8');
        assert.match(guide, /\*\*A piece for a beginner\*\* \("for a beginner", "an easy exercise"/);
        assert.match(guide, /five notes under five fingers \(C to G for the right\s+hand from middle C\)/);
        assert.match(guide, /Fingering\s+on the first note and wherever the hand must think \(`c'4@finger\(1\)`\)/);
        assert.match(guide, /`b4 c'1` — the B below middle C, not `b'` an octave above/);
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
        assert.match(grammar, /the composing guide, read\s+with the `lilysharp_readGuide` tool, topic `composing` \(`docs\/COMPOSE_FOR_LLM\.md`\)/);
    });
    it('is not sent with Ask AI\'s prompt', () => {
        const ask = transform.slice(transform.indexOf('function systemPrompt(grammar'));
        assert.ok(!ask.slice(0, 3000).includes('composing-guide'));
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
        assert.match(transform, /sendRequest<\{ Findings: string\[\]; Mistakes\?: string\[\]; Ranges\?: string\[\] \}>\('lilysharp\/compositionCheck', \{ Text: text \}\)/);
        // The octave check measures against the old notes: not for a new piece.
        assert.match(transform, /if \(edit\.text !== octaveAsked && !newPiece\) \{/);
        // A revision that does not come leaves the piece as first written, with what was found.
        assert.match(transform, /if \(candidate === null && flat && !newerWarned\) \{\s*candidate = flat\.edit;/);
    });

    // Owner's decision 2026-11-07: "Compose with AI…" (Lily#'s own composer, no chat) and "Compose
    // with AI in Chat…" were hard to tell apart; the chat's alone stays, as "Compose with Copilot…".
    it('composes in Copilot\'s chat alone: no composer of Lily#\'s own', () => {
        assert.doesNotMatch(transform, /registerCommand\('lilysharp\.composeWithAi'/);
        assert.doesNotMatch(transform, /async function runCompose\(/);
        const pkg = fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8');
        assert.doesNotMatch(pkg, /"lilysharp\.composeWithAi"/);
        // Found where a piece is begun (owner's request 2026-10-29): File > New File… beside New
        // Score, New Score first — by order, not by title.
        assert.match(pkg, /"command": "lilysharp\.newScore",\s*"group": "file@1"\s*\},\s*\{\s*"command": "lilysharp\.composeWithAiInChat",\s*"group": "file@2"\s*\}\s*\]/);
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
        assert.match(transform, /if \(relative\.Success && relative\.NewText\) \{\s*return relative\.NewText;/);
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
        // Owner's decision 2026-11-02: told it was short, a carol went round its 24 bars four times,
        // and its "variations" were new tunes over new chords in the theme's rhythm.
        assert.match(grammarDoc, /Going round the form does not make a piece longer: a section plays twice at most\./);
        assert.match(grammarDoc, /\*\*A variation is of its theme\*\*: it keeps the theme's chords or the outline of its tune/);
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
