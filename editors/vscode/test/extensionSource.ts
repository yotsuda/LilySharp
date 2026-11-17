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

import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * The extension's host (src/extension.ts) and the preview's page it writes
 * (src/previewHtml.ts), as one text. The page was a template literal inside extension.ts until
 * session 1295, and the tests that read the source — to cut a function of the page out and run
 * it, to see a button is there, to see a name is gone — read the two together: a test that says
 * a name is NOT there must look where it could be.
 */
export function extensionSource(): string {
    const src = path.join(__dirname, '..', '..', 'src');
    return fs.readFileSync(path.join(src, 'extension.ts'), 'utf8') + '\n'
        + fs.readFileSync(path.join(src, 'previewHtml.ts'), 'utf8');
}
