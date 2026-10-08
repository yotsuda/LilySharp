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

import * as vscode from 'vscode';

/**
 * The TEXT faces a score's SVG names (`font-family="TeX Gyre Schola, serif"` and
 * `"TeX Gyre Heros, sans-serif"`), declared for a webview from the copies the language
 * server ships in `server/Fonts` — the same files the layout measures every string with.
 *
 * ⚠️ Until 2026-09-26 the webviews declared only Emmentaler, so on a machine without
 * TeX Gyre installed every word of the preview fell back to the browser's own serif and
 * sans. The layout still spaced the strings by TeX Gyre's widths, so the drawn words did
 * not fill what was reserved for them: a section label's frame read too wide around its
 * narrower bold text (night-avenue.lys "Verse"/"Chorus"), and every lyric, tempo and chord
 * name was drawn in a face nobody measured.
 */
const TEXT_FACES: ReadonlyArray<{ family: string; file: string; weight: number; style: string }> = [
    { family: 'TeX Gyre Schola', file: 'texgyreschola-regular.otf', weight: 400, style: 'normal' },
    { family: 'TeX Gyre Schola', file: 'texgyreschola-bold.otf', weight: 700, style: 'normal' },
    { family: 'TeX Gyre Schola', file: 'texgyreschola-italic.otf', weight: 400, style: 'italic' },
    { family: 'TeX Gyre Schola', file: 'texgyreschola-bolditalic.otf', weight: 700, style: 'italic' },
    { family: 'TeX Gyre Heros', file: 'texgyreheros-regular.otf', weight: 400, style: 'normal' },
    { family: 'TeX Gyre Heros', file: 'texgyreheros-bold.otf', weight: 700, style: 'normal' },
    { family: 'TeX Gyre Heros', file: 'texgyreheros-italic.otf', weight: 400, style: 'italic' },
    { family: 'TeX Gyre Heros', file: 'texgyreheros-bolditalic.otf', weight: 700, style: 'italic' },
];

/** The folder the text faces load from — add it to the webview's `localResourceRoots`. */
export function textFontsRoot(extensionUri: vscode.Uri): vscode.Uri {
    return vscode.Uri.joinPath(extensionUri, 'server', 'Fonts');
}

/**
 * The SMuFL MUSIC fonts the server bundles beside Emmentaler — what a score's `.music` class
 * names when it writes `fonts { music "Bravura" }` (docs/smufl-design.md §6 ③). The preview
 * omits the SVG's own `@font-face`, so without these a Bravura score's glyphs would be drawn
 * from whatever the browser substitutes for an unknown family: private-use slots, so tofu.
 * Emmentaler itself (and its brace) is declared by each webview, as it always was. Leland
 * ships no WOFF2, so its OTF is declared as what it is.
 */
const MUSIC_FACES: ReadonlyArray<{ family: string; file: string; format: string }> = [
    { family: 'Bravura', file: 'Bravura.woff2', format: 'woff2' },
    { family: 'Petaluma', file: 'Petaluma.woff2', format: 'woff2' },
    { family: 'Leland', file: 'Leland.otf', format: 'opentype' },
];

/** `@font-face` rules for the score's text faces and the bundled SMuFL music fonts, as
 * webview URIs. */
export function textFontFaceCss(webview: vscode.Webview, extensionUri: vscode.Uri): string {
    const text = TEXT_FACES.map(f => {
        const src = webview.asWebviewUri(vscode.Uri.joinPath(textFontsRoot(extensionUri), f.file));
        return `@font-face { font-family: '${f.family}'; src: url('${src}') format('opentype'); `
            + `font-weight: ${f.weight}; font-style: ${f.style}; }`;
    });
    const music = MUSIC_FACES.map(f => {
        const src = webview.asWebviewUri(vscode.Uri.joinPath(textFontsRoot(extensionUri), f.file));
        return `@font-face { font-family: '${f.family}'; src: url('${src}') format('${f.format}'); }`;
    });
    return [...text, ...music].join('\n');
}
