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

/** A finding of the review's report, as far as the Problems panel reads it (docs/review-tool-design.md §2). */
interface ReviewFinding {
    kind: string;
    where?: { parts?: string[]; line?: number };
    what: string;
    why?: string;
    fine_when?: string;
    ask?: string;
}

/** One line of the Problems panel: where (0-based) and what it says. */
export interface ReviewPoint {
    line: number;
    message: string;
}

/** What the command makes of a report: whether the score compiles, and the points. */
export function reviewPointsOf(report: string, text: string): { compiles: boolean; points: ReviewPoint[] } {
    const parsed = JSON.parse(report) as { compiles?: boolean; findings?: ReviewFinding[] };
    return {
        compiles: parsed.compiles !== false,
        points: (parsed.findings ?? []).map(f => ({ line: lineOf(f, text), message: messageOf(f) })),
    };
}

/** The line the check named; else the first part's declaration; else the top of the file. Most checks read the
 *  sounding notes and know no place in the text. */
function lineOf(finding: ReviewFinding, text: string): number {
    if (finding.where?.line) {
        return finding.where.line - 1;
    }
    const part = finding.where?.parts?.[0];
    if (part) {
        const at = text.search(new RegExp(`^[ \\t]*part\\s+${part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(?![\\p{L}\\p{N}_])`, 'mu'));
        if (at >= 0) {
            return text.slice(0, at).split('\n').length - 1;
        }
    }
    return 0;
}

function messageOf(finding: ReviewFinding): string {
    return [
        `[${finding.kind}] ${finding.what}`,
        finding.why && `Why: ${finding.why}`,
        finding.fine_when && `Fine when: ${finding.fine_when}`,
        finding.ask && `Ask: ${finding.ask}`,
    ].filter(Boolean).join('\n');
}
