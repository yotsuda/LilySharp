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
import { svgPostKey, pagesSummary, previewOnEditorShown, sourceGroupToReveal, sourceGroupToOpen, newPreviewColumn, scoreGroupFor, ShownEditor, jumpOffsetFor, chordSpans, chordAtCaret, soundingRange, soundingRanges, soundingLight, auditionBarRange } from '../src/previewCore';
import * as fs from 'node:fs';
import * as path from 'node:path';

// Owner's request 2026-11-15: while the preview plays, the editor lights the text of what
// sounds — as Shosa's editor lights the word being performed. A playback note's offset is its
// node's full-span start, so the leading spaces are skipped and the word is what is lit.
describe('the bar Alt+M plays', () => {
    const text = 'chords h {\n  D | Em | break\n  G | A | break\n}\n';
    // (what is in the span besides spaces and a `break`: the bar's music)
    const bar = (caret: number) => { const [a, b] = auditionBarRange(text, caret); return text.slice(a, b).replace(/break/g, '').trim(); };
    it('is the bar the caret is in', () => {
        assert.equal(bar(text.indexOf('Em') + 1), 'Em');
        assert.equal(bar(text.indexOf('G')), 'G');
    });
    // Owner's report 2026-11-17: in a chords row Alt+M sometimes played nothing — a caret past a
    // line's last bar line took the span to the block's '}' (or to the next line's first bar).
    it('is the bar before for a caret past a line\'s last bar line', () => {
        assert.equal(bar(text.indexOf('break')), 'Em');
        assert.equal(bar(text.indexOf('| break\n}') + 2), 'A');
        assert.equal(bar(text.indexOf('A | break') + 9), 'A');   // the line's end
    });
    it('keeps an empty bar written on one line', () => {
        const empty = 'a { c1 | | d1 | }';
        const [s, e] = auditionBarRange(empty, empty.indexOf('| |') + 1);
        assert.equal(empty.slice(s, e).trim(), '');
    });
});

describe('the text of a note that sounds', () => {
    const lit = (text: string, pos: number) => { const r = soundingRange(text, pos); return r ? text.slice(r[0], r[1]) : undefined; };

    it('is the note with its octave marks, duration and dots, from past its leading spaces', () => {
        const text = "m { c'4  cis''8.@staccato d,2~ d,4 | e4) f8[ g] bes4\\3 }";
        assert.equal(lit(text, text.indexOf("c'4")), "c'4");
        assert.equal(lit(text, text.indexOf("c'4") - 1), "c'4");          // the space before it
        assert.equal(lit(text, text.indexOf('  cis')), "cis''8.");         // two spaces, then the mark is left out
        assert.equal(lit(text, text.indexOf('d,2')), 'd,2');               // the tie is not the note
        assert.equal(lit(text, text.indexOf('e4)')), 'e4');
        assert.equal(lit(text, text.indexOf('f8[')), 'f8');
        assert.equal(lit(text, text.indexOf('g]')), 'g');
        assert.equal(lit(text, text.indexOf('bes4')), 'bes4');             // the string number is not
    });

    it('is the whole chord with its duration, a broken chord too', () => {
        const text = "<c' e' g'>4. <d f a>'2@fermata << c e g >>1 |";
        assert.equal(lit(text, 0), "<c' e' g'>4.");
        assert.equal(lit(text, text.indexOf(' <d')), "<d f a>'2");
        assert.equal(lit(text, text.indexOf('<<')), '<< c e g >>1');
        assert.equal(lit('<c e g', 0), undefined);                         // not closed
        assert.equal(lit('<c e\ng>4', 0), undefined);                      // nor on its line
    });

    it('is a drum name and a chord row symbol as written', () => {
        assert.equal(lit('drums { bd4 hh sn hh | }', 8), 'bd4');
        const row = 'chords prog { Cmaj7 | F#m7/E Bb | % | }';
        assert.equal(lit(row, row.indexOf('Cmaj7')), 'Cmaj7');
        assert.equal(lit(row, row.indexOf(' F#m7')), 'F#m7/E');
        assert.equal(lit(row, row.indexOf('%')), '%');
    });

    it('is nothing past the text, at its end or on a bar line', () => {
        assert.equal(soundingRange('c4 d4', 5), undefined);
        assert.equal(soundingRange('c4 d4', 99), undefined);
        assert.equal(soundingRange('c4 d4', -1), undefined);
        assert.equal(soundingRange('c4 | d4', 2), undefined);
        assert.equal(soundingRange('c4   ', 2), undefined);
    });

    it('is each sounding note once, in the order of the text', () => {
        const text = "rh { e''4 c''4 } lh { <c e g>2 }";
        const positions = [text.indexOf('<c'), text.indexOf("e''4"), text.indexOf("e''4") - 1, 999];
        assert.deepEqual(soundingRanges(text, positions).map(([a, b]) => text.slice(a, b)), ["e''4", '<c e g>2']);
        assert.deepEqual(soundingRanges(text, []), []);
    });
});

describe('the editor lights what the preview plays', () => {
    const text = "m { c'4 d'4 r4 <e' g'>4 | }";
    const at = (word: string) => text.indexOf(word);
    const words = (ranges: [number, number][]) => ranges.map(([a, b]) => text.slice(a, b));
    const report = (session: ReturnType<typeof soundingLight>['session'], positions: number[], o: { uri?: string; version?: number; ended?: boolean; on?: boolean } = {}) =>
        soundingLight(session, o.uri ?? 'a.lys', o.version ?? 7, () => text, positions, o.ended ?? false, o.on ?? true);

    it('lights each report of a playback, and nothing for a rest', () => {
        let r = report(undefined, [at("c'4")]);
        assert.deepEqual(words(r.ranges), ["c'4"]);
        assert.deepEqual(r.session, { uri: 'a.lys', version: 7 });
        r = report(r.session, []);                       // the rest: dark, the playback goes on
        assert.deepEqual(r.ranges, []);
        assert.deepEqual(r.session, { uri: 'a.lys', version: 7 });
        r = report(r.session, [at("<e'")]);
        assert.deepEqual(words(r.ranges), ["<e' g'>4"]);
    });

    it('lights nothing once the text is edited, until the playback ends and the next starts', () => {
        const first = report(undefined, [at("c'4")]);
        // An edit: every offset after it has moved, and the notes are the old text's.
        const edited = report(first.session, [at("d'4")], { version: 8 });
        assert.deepEqual(edited.ranges, []);
        assert.deepEqual(edited.session, { uri: 'a.lys', version: 7 });
        // …a rest in between does not start it again…
        assert.deepEqual(report(report(edited.session, [], { version: 8 }).session, [at("d'4")], { version: 8 }).ranges, []);
        // …the end does: the next Play reads the text as it then is.
        const ended = report(edited.session, [], { version: 8, ended: true });
        assert.equal(ended.session, undefined);
        assert.deepEqual(words(report(ended.session, [at("d'4")], { version: 8 }).ranges), ["d'4"]);
        // A playback that opens on a rest is this text's from that first report.
        const opened = report(undefined, [], { version: 3 });
        assert.deepEqual(report(opened.session, [at("c'4")], { version: 4 }).ranges, []);
    });

    it('starts again for another file, lights nothing when switched off, and clears at the end', () => {
        const first = report(undefined, [at("c'4")]);
        const turned = report(first.session, [at("c'4")], { uri: 'b.lys', version: 1 });
        assert.deepEqual(turned.session, { uri: 'b.lys', version: 1 });
        assert.deepEqual(words(turned.ranges), ["c'4"]);
        assert.deepEqual(report(first.session, [at("c'4")], { on: false }).ranges, []);
        assert.deepEqual(report(first.session, [at("c'4")], { ended: true }), { session: undefined, ranges: [] });
    });

    // The wiring, in the source: the page reports what its follow-along lights and says when
    // the playback stops; the host lights the editors of the panel's file.
    it('is told by the page at every change of what sounds, and at the stop', () => {
        const extension = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        // The notes sounding and the rests lasting (owner's request 2026-11-17: a rest lit in the
        // text while it lasts); the score lights the notes only — the rests (E) have their own cursor.
        assert.match(extension, /sourceLit = true;\s*vscode\.postMessage\(\{ type: 'soundingSource', positions: r\.sounding\.map\(x => x\.pos\)\.concat\(rests \? rests\.sounding\.map\(x => x\.pos\) : \[\]\) \}\);\s*if \(!r\.changed\) return;\s*clearHighlights\(\);\s*const painted = highlightPositions\(r\.sounding\);/);
        assert.match(extension, /const onScore = notes\.filter\(n => !n\.E && !n\.Z\);[\s\S]{0,400}?const cursor = new Cursor\(onScore\.map/);
        assert.match(extension, /const restCursor = relay \? null : new Cursor\(notes\.filter\(n => n\.E\)\.map/);
        assert.match(extension, /if \(sourceLit\) \{\s*sourceLit = false;\s*vscode\.postMessage\(\{ type: 'soundingSource', positions: \[\], ended: true \}\);/);
        assert.match(extension, /if \(message\.type === 'soundingSource'\) \{\s*showSoundingSource\(panel, uri,/);
        // A preview closed while it played leaves nothing lit.
        assert.match(extension, /showSoundingSource\(panel, soundingLit\.get\(panel\)\?\.uri, \[\], true\);/);
        assert.match(extension, /backgroundColor: new vscode\.ThemeColor\('editor\.findMatchHighlightBackground'\)/);
        const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const settings = pkg.contributes.configuration.flatMap((c: { properties: Record<string, { default?: unknown }> }) => Object.entries(c.properties));
        assert.equal(settings.find(([k]: [string, unknown]) => k === 'lilysharp.playback.highlightSource')?.[1].default, true);
    });
});

// Owner's request 2026-10-27: Select Chord on a member right-clicked puts the caret past the
// chord's ">", and a caret there (or on its "<", or on what is glued after the ">") lights it whole.
describe('the chords of a score', () => {
    it('are each "<…>" on a line, not "<<…>>", a hairpin, a comment or a string', () => {
        const text = 'c4 <c e g>4 << { a } \\ { b } >> d\\< e\\! // <x y>\n"<no>" /* <z> */ <f a>2.@fermata |';
        assert.deepEqual(chordSpans(text).map(([a, b]) => text.slice(a, b + 1)), ['<c e g>', '<f a>']);
    });

    it('are pointed at whole from the "<", past the ">" and in what is glued after it', () => {
        const line = '    <c e g>4.@fermata d4 <f a>';
        const whole: [number, number] = [4, 10];
        assert.deepEqual(chordAtCaret(line, 4), whole);                       // |<c e g>
        assert.deepEqual(chordAtCaret(line, 11), whole);                      // <c e g>|4.
        assert.deepEqual(chordAtCaret(line, 13), whole);                      // <c e g>4.|@fermata
        assert.deepEqual(chordAtCaret(line, 21), whole);                      // …@fermata|
        assert.deepEqual(chordAtCaret(line, line.length), [25, 29]);          // <f a>| at the end
        assert.equal(chordAtCaret(line, 5), undefined);                       // <|c — a member
        assert.equal(chordAtCaret(line, 10), undefined);                      // <c e g|> — a member
        assert.equal(chordAtCaret(line, 23), undefined);                      // d4
        assert.equal(chordAtCaret('<< a >>', 7), undefined);
        assert.equal(chordAtCaret('c\\> d', 3), undefined);
    });

    it('are wired: the menu on a member, the caret past the ">", the whole chord lit there', () => {
        const fs = require('node:fs') as typeof import('node:fs');
        const path = require('node:path') as typeof import('node:path');
        const root = path.join(__dirname, '..', '..');
        const ext = fs.readFileSync(path.join(root, 'src', 'extension.ts'), 'utf8');
        const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
        assert.match(ext, /const ctxChordEnd = ctxNotePos >= 0 \? chordEndAt\(ctxNotePos\) : -1;/);
        assert.match(ext, /lysOnChord: ctxChordEnd >= 0,\s*lysChordEnd: ctxChordEnd,/);
        assert.match(ext, /const position = editor\.document\.positionAt\(m\.lysChordEnd!\);/);
        assert.match(ext, /const chord = chordAtCaret\(line, col\);\s*if \(chord\) \{/);
        assert.match(ext, /type: 'chordSpans', spans: chordSpans\(document\.getText\(\)\)/);
        const item = (pkg.contributes.menus['webview/context'] as { command: string; when: string }[])
            .find(m => m.command === 'lilysharp.previewSelectChord');
        assert.equal(item?.when, "webviewId == 'lilysharpPreview' && lysOnChord");
    });

    // Owner's report 2026-10-27: the menu's groups held one item each. Three groups: what changes
    // the music (the AI, the selection, the scan), playback, the view.
    it('groups the preview\'s menu as editing, playback and view', () => {
        const fs = require('node:fs') as typeof import('node:fs');
        const path = require('node:path') as typeof import('node:path');
        const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const menu = pkg.contributes.menus['webview/context'] as { command: string; group: string }[];
        const groups = [...new Set(menu.map(m => m.group.split('@')[0]))].sort();
        assert.deepEqual(groups, ['1_edit', '2_playback', '3_view']);
        const of = (c: string) => menu.find(m => m.command === c)?.group;
        // Owner's request 2026-10-29: the AI on the menu is the chat's; Lily#'s own Ask AI left it.
        assert.equal(of('lilysharp.previewAskAiInChat'), '1_edit@1');
        assert.ok(!menu.some(m => m.command.startsWith('lilysharp.previewAiTransform')));
        const editor = pkg.contributes.menus['editor/context'] as { command: string; group: string }[];
        assert.equal(editor.find(m => m.command === 'lilysharp.askAiInChat')?.group, '1_modification@1');
        assert.ok(!editor.some(m => m.command === 'lilysharp.aiTransform'));
        // …nor composing: a new piece is begun from File > New File… (owner's decision, the same day).
        assert.ok(!editor.some(m => m.command.startsWith('lilysharp.composeWithAi')));
        assert.equal(of('lilysharp.previewSelectChord'), '1_edit@2');
        assert.equal(of('lilysharp.editBarsBesideScan'), '1_edit@5');
        assert.equal(of('lilysharp.previewPlayFromHere'), '2_playback@1');
        assert.equal(of('lilysharp.previewFitWidth'), '3_view@1');
    });
});

describe('a score opened into a preview\'s group', () => {
    it('moves to the leftmost group with no preview', () => {
        assert.equal(scoreGroupFor(2, [{ column: 1, hasPreview: false }, { column: 2, hasPreview: true }]), 1);
        assert.equal(scoreGroupFor(3, [{ column: 1, hasPreview: true }, { column: 2, hasPreview: false },
            { column: 3, hasPreview: true }, { column: 4, hasPreview: false }]), 2);
    });

    it('moves to a new group left of the preview when every group holds one', () => {
        assert.equal(scoreGroupFor(1, [{ column: 1, hasPreview: true }]), 'left');
    });

    it('stays where it opened in a group without a preview', () => {
        assert.equal(scoreGroupFor(1, [{ column: 1, hasPreview: false }, { column: 2, hasPreview: true }]), undefined);
    });
});

describe('where a click on the preview puts the caret', () => {
    it('steps over the indentation to the note', () => {
        const text = 'part p {\n    c\'4 d\'4\n}';
        assert.equal(text[jumpOffsetFor(text, text.indexOf('    c'))], 'c');
    });

    it('steps over a title\'s opening quote', () => {
        const text = 'title "Harbour"';
        assert.equal(jumpOffsetFor(text, text.indexOf('"')), text.indexOf('H'));
    });

    it('lands after the @ of a fermata, a dynamic and an @chord, where the step keys read the mark', () => {
        // A caret just before the @ is the note's (NoteStepper.TargetAt), so a click on a
        // fermata followed by Ctrl+Shift+Up moved the pitch (user report 2026-10-17).
        const text = 'c\'4@fermata d\'4@mf e\'1@chord(Cm7)';
        for (const mark of ['@fermata', '@mf', '@chord']) {
            assert.equal(jumpOffsetFor(text, text.indexOf(mark)), text.indexOf(mark) + 1);
        }
    });

    it('stays on a note, and never crosses a line break', () => {
        const text = 'c\'4\n  d\'4';
        assert.equal(jumpOffsetFor(text, 0), 0);
        assert.equal(jumpOffsetFor(text, 3), 3);
        assert.equal(jumpOffsetFor(text, 4), 6);
    });
});

describe('the page answer summary', () => {
    it('counts the pages by what they did and the characters that travelled', () => {
        const summary = pagesSummary({
            Version: 7, BaseVersion: 5, Head: '<svg>', Tail: '</svg>',
            Window: { Prefix: 10, SuffixStart: 10, Delta: 1 },
            Items: [
                { Change: 'same' }, { Change: 'shifted' }, { Change: 'changed', Markup: '<g class="page">…</g>' },
            ],
        });
        assert.equal(summary, '3 (delta v5->v7: same 1, shifted 1, changed 1; 32 chars)');
    });

    it('says a full answer is one', () => {
        const summary = pagesSummary({
            Version: 1, BaseVersion: null, Head: '', Tail: '',
            Items: [{ Change: 'changed', Markup: 'ab' }, { Change: 'changed', Markup: 'cd' }],
        });
        assert.equal(summary, '2 (full v1: same 0, shifted 0, changed 2; 4 chars)');
    });
});

describe('the svg post key', () => {
    const svg = '<svg/>';
    const main = { Name: 'main', Type: 'score', Filename: '' };
    const sub = { Name: 'S', Type: 'score', Filename: 'S' };

    it('is the same for the same picture, banner, list and drawn score', () => {
        assert.equal(svgPostKey(svg, null, [main], ''), svgPostKey(svg, undefined, [main], ''));
    });

    it('changes when a score is added to the list although the picture did not change', () => {
        // The 2026-09-11 report: a `score { }` pasted below the drawn one left the picture
        // identical, and the skipped post left the picker without the new score.
        assert.notEqual(svgPostKey(svg, null, [main], ''), svgPostKey(svg, null, [main, sub], ''));
    });

    it('changes when the drawn score changes, when the banner changes, and when the picture changes', () => {
        assert.notEqual(svgPostKey(svg, null, [main, sub], ''), svgPostKey(svg, null, [main, sub], 'S'));
        assert.notEqual(svgPostKey(svg, null, [main], ''), svgPostKey(svg, 'Line 1: oops', [main], ''));
        assert.notEqual(svgPostKey(svg, null, [main], ''), svgPostKey('<svg><g/></svg>', null, [main], ''));
    });

    it('treats a missing list as empty', () => {
        assert.equal(svgPostKey(svg, null, null, ''), svgPostKey(svg, null, [], ''));
    });
});

describe('an editor coming to the front', () => {
    const opened: ShownEditor = {
        languageId: 'lilysharp', scheme: 'file', inDiff: false, hasOwnPreview: false,
        hasFollower: false, openOnOpen: true, firstShow: true, dismissed: false, followerLocked: false,
    };

    it('opens the second preview on a tab switch once the follower was locked to compare', () => {
        assert.equal(previewOnEditorShown({ ...opened, firstShow: false, followerLocked: true }), 'open');
        assert.equal(previewOnEditorShown({ ...opened, firstShow: false, followerLocked: true, dismissed: true }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, firstShow: false, followerLocked: true, inDiff: true }), 'none');
    });

    it('opens a preview when a .lys file is opened and none is open', () => {
        assert.equal(previewOnEditorShown(opened), 'open');
        // New Score's untitled buffer is an opening too.
        assert.equal(previewOnEditorShown({ ...opened, scheme: 'untitled' }), 'open');
    });

    it('turns the follower to it instead of opening a second preview', () => {
        assert.equal(previewOnEditorShown({ ...opened, hasFollower: true }), 'follow');
        // ...on a tab switch too, and with the opening setting off: following is the follower's own nature.
        assert.equal(previewOnEditorShown({ ...opened, hasFollower: true, firstShow: false, openOnOpen: false }), 'follow');
        assert.equal(previewOnEditorShown({ ...opened, hasFollower: true, scheme: 'untitled' }), 'follow');
    });

    it('leaves a file that has a preview of its own alone', () => {
        assert.equal(previewOnEditorShown({ ...opened, hasOwnPreview: true }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, hasOwnPreview: true, hasFollower: true }), 'none');
    });

    it('opens nothing on a tab switch, in a diff, off a file, when closed by the writer or turned off', () => {
        assert.equal(previewOnEditorShown({ ...opened, firstShow: false }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, inDiff: true }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, scheme: 'git' }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, dismissed: true }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, openOnOpen: false }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, languageId: 'markdown' }), 'none');
        assert.equal(previewOnEditorShown({ ...opened, hasFollower: true, scheme: 'git' }), 'none');
    });

    // Owner's report 2026-10-29: a score saved and closed, opened again from a link in the chat,
    // came without its preview — closed with the score, it had been taken for dismissed for good.
    it('forgets a closed preview and the first showing once the score\'s last tab is closed', () => {
        const fs = require('node:fs') as typeof import('node:fs');
        const path = require('node:path') as typeof import('node:path');
        const ext = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
        assert.match(ext, /if \(uri && !open\.has\(uri\)\) \{\s*shownDocs\.delete\(uri\);\s*dismissedPreviews\.delete\(uri\);/);
        assert.match(ext, /vscode\.window\.tabGroups\.onDidChangeTabs\(forgetClosedScores\),/);
    });
});

describe('the column a new preview opens in', () => {
    it('is the next one right of the rightmost locked preview on screen', () => {
        assert.equal(newPreviewColumn([2]), 3);
        assert.equal(newPreviewColumn([3, 2]), 4);
    });

    it('is VS Code\'s own beside with no locked preview on screen', () => {
        assert.equal(newPreviewColumn([]), undefined);
    });
});

describe('the source tab a preview brings forward', () => {
    const a = 'file:///a.lys';

    it('is its tab in another group, when that tab is behind others', () => {
        assert.equal(sourceGroupToReveal([{ column: 1, uri: a, active: false }], 2, a), 1);
    });

    it('is none when the source is already in front, not open, or only in the preview\'s own group', () => {
        assert.equal(sourceGroupToReveal([{ column: 1, uri: a, active: true }], 2, a), undefined);
        assert.equal(sourceGroupToReveal([{ column: 1, uri: 'file:///b.lys', active: false }], 2, a), undefined);
        assert.equal(sourceGroupToReveal([{ column: 2, uri: a, active: false }], 2, a), undefined);
        assert.equal(sourceGroupToReveal([{ column: 1, uri: a, active: false }, { column: 3, uri: a, active: true }], 2, a), undefined);
    });
});

// A click on a preview whose score was closed opens it again (owner's request 2026-10-14).
describe('where a click on a preview reopens its closed score', () => {
    const a = 'file:///a.lys';
    const preview = { column: 2, hasPreview: true };

    it('is the group holding a tab of it behind others, never the preview\'s', () => {
        assert.equal(sourceGroupToOpen([{ column: 3, uri: a, active: false }], [{ column: 1, hasPreview: false }, preview, { column: 3, hasPreview: false }], a), 3);
        assert.equal(sourceGroupToOpen([{ column: 2, uri: a, active: false }], [{ column: 1, hasPreview: false }, preview], a), 1);
    });
    it('is else the leftmost group with no preview', () => {
        assert.equal(sourceGroupToOpen([{ column: 3, uri: 'file:///b.lys', active: true }], [preview, { column: 3, hasPreview: false }, { column: 4, hasPreview: false }], a), 3);
    });
    it('is a new group left of the preview when every group holds one (the score\'s group went with it)', () => {
        assert.equal(sourceGroupToOpen([], [{ column: 1, hasPreview: true }], a), 'left');
    });
});
