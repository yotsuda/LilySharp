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

// "Write a Song over These Chords…" (docs/song-over-chords-design.md): a sung part — melody, words
// and the line that sings them — added by Copilot's chat to the score that is there. The second
// step of the way from a song to a dance (Shosa's, 2026-11-26): a score with chords and no voice,
// read from a recording or written, gets a song, the recording playing under it. What the chat is
// asked and the guide it reads are made here, apart from VS Code.

/** The name the new part takes: `vocal`, or `vocal2`… where the score has one. */
export function songPartName(text: string): string {
    return freeName(text, 'vocal', /\bpart\s+([A-Za-z_][A-Za-z0-9_]*)/g);
}

/** The name its words take: `words`, or `songWords`… where the score has a track of that name. */
export function songWordsName(text: string): string {
    return freeName(text, 'words', /\blyrics\s+([A-Za-z_][A-Za-z0-9_]*)/g, 'songWords');
}

function freeName(text: string, first: string, declared: RegExp, second?: string): string {
    const taken = new Set([...stripComments(text).matchAll(declared)].map(m => m[1]));
    if (!taken.has(first)) { return first; }
    const base = second ?? first;
    if (second && !taken.has(second)) { return second; }
    for (let n = 2; ; n++) {
        if (!taken.has(`${base}${n}`)) { return `${base}${n}`; }
    }
}

function stripComments(text: string): string {
    return text.replace(/\/\/[^\n]*/g, '');
}

/** Whether the score names chords (a `chords NAME { … }` row in a section). A score without them
 *  is asked of all the same — the chat reads the harmony its parts play — and told so. */
export function namesChords(text: string): boolean {
    return /\bchords\s+[A-Za-z_][A-Za-z0-9_]*\s*\{/.test(stripComments(text));
}

/** What the chat is handed: the score, the names to use, where the guide is, and the musician's
 *  request — or, with none, that the AI chooses. */
export function songRequest(file: string, part: string, words: string, request: string, chords: boolean): string {
    const asked = request.trim();
    return `Write a song over the ${chords ? 'chords' : 'harmony'} of the Lily# score ${file}: add a sung part to THIS file — its melody, `
        + 'its words and the line that sings them — over the music that is there, which stays as it is.\n'
        + `Name the part \`${part}\` and its words \`${words}\`.\n`
        + 'First read how: lilysharp_readGuide, topic "song".\n'
        + (asked.length > 0 ? `The request: ${asked}` : 'No request was given: choose the song yourself, in the language of the score\'s title.');
}

/** The steps of the song guide (topic "song"), before the composing guide's "A song with words". */
export const SONG_STEPS = `# Writing a song over a score's chords

You add ONE sung part to a score that is there: its melody, its words and the line that sings them. Everything else — the parts, the chords rows, the sections, the form, the tempo, the key — stays as it is. Never propose the whole file again, and never write a new file.

1. **Read the score**: lilysharp_readScore (its sections, bars, tempo, key and meter, the form's order) and lilysharp_readChords (each section's chords). A score read from a recording (its first line says \`read from … by orecchiante\`, or it has a recording) plays that recording under the song: its tempo and its bars are the recording's, and the song fits them as they are.
2. **Plan, in your reply, before any note**: which sections are sung — verses, a pre-chorus, the chorus, a bridge — and which are left to the band (an intro, a solo, an interlude, an outro). Sections may be named by letters (A, A2, B …): tell them by their chords and where the form plays them — a progression that comes back is the same kind of section, and the chorus is the one that returns with the strongest progression. Then each sung section's words in a line, and the chorus's hook (its first line, the title).
3. **The voice**: \`instrument alto\` (F3–F5) for a woman's voice and when the request names none, \`instrument baritone\` (G2–G4) for a man's; a voice the request names as named. The key is the score's: find the tune's place in the range for it, never another key.
4. **Words first, then the tune**, as *A song with words* below says: each line counted, one note a unit; the long notes and the strong beats on the chord's tones; the chorus higher than the verse, in another rhythm. Every sung section fills exactly its bars; a bar that is not sung is a whole-bar rest (\`R\`). A section the form plays more than once sings the same notes each time; its words differ as verses, in its lyrics: \`verse 1 { … } verse 2 { … }\`. How words are written — units apart, \`|\` at the bar lines as the notes have them — is the grammar's *Lyrics* (lilysharp_readGuide, topic "grammar": its contents name the part).
5. **Write it in one call of lilysharp_writePart**, nothing else of the file: \`newParts\` [{ "part": PART, "title": "Vocal", "body": "instrument alto\\nlyrics WORDS", "first": true, "singer": "female" }] and \`write\`, one item a section the song sings, each with its \`music\` and its \`lyrics\` — a section it does not sing left out (it stays silent). The singer is \`female\` or \`male\` (this machine's default voice of that gender), or a voice lilysharp_listVoices gave, in quotes (\`"neutrino/MERROW"\`), when the request names a voice or a kind of voice.
6. A call turned down changed nothing: mend what it says and call again with all of it. The musician hears the song in the panel beside the score and accepts it or not.

The composing guide's word on a song with words follows.

`;

/** The song guide: the steps, then the composing guide's "A song with words" — kept there, one
 *  text for a new song and for this one. The steps alone where the guide has no such passage. */
export function songGuide(composeGuide: string): string {
    return SONG_STEPS + (songWithWords(composeGuide) ?? '');
}

/** The composing guide's "A song with words" item, to the next item at its level; undefined when
 *  it is not there. */
export function songWithWords(composeGuide: string): string | undefined {
    const lines = composeGuide.replace(/\r\n/g, '\n').split('\n');
    const start = lines.findIndex(l => l.startsWith('- **A song with words**'));
    if (start < 0) { return undefined; }
    let end = start + 1;
    while (end < lines.length && !/^(- |#)/.test(lines[end])) { end++; }
    return lines.slice(start, end).join('\n').trimEnd() + '\n';
}
