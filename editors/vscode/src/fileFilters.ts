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

// The file picker's filters for an import (owner's request 2026-11-18): every kind the import reads
// first, then each kind alone — as Import MusicXML, MIDI or ABC… offers them.

/** The spellings of one kind, under one name. */
const FAMILIES: Record<string, string> = {
    jpg: 'JPEG', jpeg: 'JPEG',
    tif: 'TIFF', tiff: 'TIFF',
    mid: 'MIDI', midi: 'MIDI',
};

/**
 * `showOpenDialog`'s filters: `all` with every extension, then a filter per kind in the order the
 * extensions come — one name for a kind's spellings (`jpg` and `jpeg` are JPEG), the extension in
 * capitals for the rest. A single kind has no second filter.
 */
export function importFilters(all: string, extensions: readonly string[]): Record<string, string[]> {
    const kinds = new Map<string, string[]>();
    for (const raw of extensions) {
        const ext = raw.toLowerCase();
        const name = FAMILIES[ext] ?? ext.toUpperCase();
        const list = kinds.get(name) ?? [];
        if (!list.includes(ext)) {
            list.push(ext);
        }
        kinds.set(name, list);
    }
    const filters: Record<string, string[]> = { [all]: [...new Set(extensions.map(e => e.toLowerCase()))] };
    if (kinds.size > 1) {
        for (const [name, list] of kinds) {
            filters[name] = list;
        }
    }
    return filters;
}
