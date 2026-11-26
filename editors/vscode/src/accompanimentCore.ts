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

// "Arrange an Accompaniment…" (docs/accompaniment-design.md): accompanying parts written by Copilot's
// chat from a score's chords, into the score that is there, to play in place of the recording it was
// read from. The fourth step of the way from a song to a dance (Shosa's, 2026-11-26). What the chat
// is asked, the guide it reads, and what an accepted change did, are made here, apart from VS Code.

/** The parts a score declares (`part NAME`), in order; comments do not count. */
export function declaredParts(text: string): string[] {
    return [...text.replace(/\/\/[^\n]*/g, '').matchAll(/(?:^|\n)\s*part\s+([A-Za-z_][A-Za-z0-9_-]*)/g)].map(m => m[1]);
}

/** The parts `after` declares that `before` did not: what an accepted arrangement added. */
export function addedParts(before: string, after: string): string[] {
    const had = new Set(declaredParts(before));
    return declaredParts(after).filter(p => !had.has(p));
}

/** What the chat is handed: the score, the parts it has (they stay), whether a recording plays
 *  under it, where the guide is, and the musician's request — or, with none, the default band. */
export function accompanimentRequest(file: string, parts: readonly string[], request: string, chords: boolean, recording: boolean): string {
    const asked = request.trim();
    return `Arrange an accompaniment for the Lily# score ${file}: add accompanying parts to THIS file, written from its `
        + `${chords ? 'chords' : 'harmony'}, over the music that is there, which stays as it is.\n`
        + (parts.length > 0 ? `The parts it has stay as they are: ${parts.join(', ')}. Add only the instruments it does not have.\n` : '')
        + (recording ? 'The score was read from a recording: the accompaniment plays in its place, on its bars.\n' : '')
        + 'First read how: lilysharp_readGuide, topic "accompaniment".\n'
        + (asked.length > 0 ? `The request: ${asked}`
            : 'No request was given: a band (piano, bass and drums), in the style the score\'s tempo, chords and title suggest.');
}

/** The steps of the accompaniment guide (topic "accompaniment"), before the composing guide's items. */
export const ACCOMPANIMENT_STEPS = `# Arranging an accompaniment over a score's chords

You add accompanying parts to a score that is there: the instruments that play its chords. Everything else — the parts it has, the chords rows, the sections, the form, the tempo, the key — stays as it is. Never propose the whole file again, and never write a new file.

1. **Read the score**: lilysharp_readScore (its sections, bars, tempo, key and meter, the form's order) and lilysharp_readChords (each section's chords). A score read from a recording (its first line says \`read from … by orecchiante\`, or it has a recording) has the recording's tempo and bars: the accompaniment fits them as they are, and plays in the recording's place. A part the score has — a bass read from the recording, a sung part — stays as it is: never write into it.
2. **Plan, in your reply, before any note**: the style (the request's, else the one the tempo, the chords and the title suggest), the instruments (the request's, else a band: piano, bass and drums — less the ones the score has), and what each does in each section: an intro that sets the groove, a lighter verse, a fuller chorus, a bridge that changes the texture, fills into a new section, an ending on the tonic.
3. **Every section the form plays, every bar**, on that section's chords as lilysharp_readChords gives them — a chord change where the chords row has one, the same chord where it holds. An instrument that lays out has a whole-bar rest (\`R\`). Under a sung part, the accompaniment stays below the voice's notes and out of its way; it answers where the voice rests.
4. **Write it in one call of lilysharp_writePart**, nothing else of the file: \`newParts\`, one item an instrument — for the band [{ "part": "piano", "title": "Piano", "body": "instrument piano-right" }, { "part": "bass", "title": "Bass", "body": "instrument bass\\nmidiInstrument \\"electric bass (finger)\\"" }, { "part": "drums", "title": "Drums", "body": "clef percussion" }] (a name the score uses already takes a number: \`piano2\`; a piano with both hands written out is two parts, \`instrument piano-right\` and \`instrument piano-left\`) — and \`write\`, one item a part and a section, each with its \`music\`. How the drums are written is the grammar's *Drums* (lilysharp_readGuide, topic "grammar": its contents name the part).
5. A call turned down changed nothing: mend what it says and call again with all of it. The musician hears it in the panel beside the score and accepts it or not. Accepted, the recording under the score goes silent — its bars still time the music — and the musician can turn it on again in the preview's mixer.

The composing guide's word on a band and on a style follows.

`;

/** The composing guide's items an arrangement reads: how a band plays, and a style's rhythm. */
const GUIDE_ITEMS = ['A band', 'A style named is its rhythm first'];

/** The accompaniment guide: the steps, then the composing guide's items (GUIDE_ITEMS) — kept there,
 *  one text for a new piece and for this. The steps alone where the guide has none of them. */
export function accompanimentGuide(composeGuide: string): string {
    return ACCOMPANIMENT_STEPS + GUIDE_ITEMS.map(h => guideItem(composeGuide, h) ?? '').join('');
}

/** The composing guide's item headed `heading` (`- **heading**`), to the next item at its level;
 *  undefined when it is not there. */
export function guideItem(composeGuide: string, heading: string): string | undefined {
    const lines = composeGuide.replace(/\r\n/g, '\n').split('\n');
    const start = lines.findIndex(l => l.startsWith(`- **${heading}**`));
    if (start < 0) { return undefined; }
    let end = start + 1;
    while (end < lines.length && !/^(- |#)/.test(lines[end])) { end++; }
    return lines.slice(start, end).join('\n').trimEnd() + '\n';
}

/** What is said once an arrangement is accepted over a score whose recording is now silent. */
export function silencedMessage(fileName: string): string {
    return `Lily#: ${fileName} has its own accompaniment now, so its recording is silent — its bars still time the music. `
        + 'Turn it on again in the preview\'s mixer (Recording), or here.';
}

/** That message's button. */
export const PLAY_RECORDING_TOO = 'Play the Recording Too';
