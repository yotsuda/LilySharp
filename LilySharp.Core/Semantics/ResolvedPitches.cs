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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Every written note's resolved absolute pitch, as the file's scores render it —
/// the answer behind <c>lysc check --pitches</c>.
/// </summary>
/// <remarks>
/// <para>
/// Lives here rather than in the CLI so the property can be tested on the thing that
/// ships. RULES §5.3 判定法⑶ tells every session to run that command over a synthesized
/// book before filing anything about it, which makes this one of the few reports whose
/// wrongness costs whole sessions rather than one reading.
/// </para>
/// <para>
/// Two decisions, both measured over the 300 books under `Fixtures` and
/// `audit/lp-regression` before being written:
/// </para>
/// <para>
/// ⚠️ EVERY score, not the first. Stopping at the first loses coverage — `test/cue-notes`
/// reports 26 notes across its scores and 8 in the first alone.
/// </para>
/// <para>
/// ⚠️ Folded onto the WRITTEN position, so a note counts once however many times it is
/// drawn. Without the fold a tab book counts twice (`tab X` and `staff X` render the same
/// written note: `test/tab-percent-repeat` gives 32 written notes and 64 drawn ones), and
/// a phrase referenced from three sections counts three times
/// (`test/section-octave-reset`: 13 written, 26 drawn under the old reading).
/// </para>
/// <para>
/// ⚠️ A position that two scores read differently keeps the FIRST score's reading, and
/// says nothing about the disagreement. No book in the tree has one (measured over the
/// same 300); the only construct that can make one is a per-score <c>transpose</c>.
/// </para>
/// <para>
/// ⚠️ That construct WORKS now, so the case is writable — this paragraph used to say it
/// was "under its own suspicion" and that there was therefore nothing to report
/// truthfully, which <c>077e5c98</c> ended in the same session (a score's own
/// <c>transpose</c> was being counted as the file's default, so the declaring score got
/// it twice and its neighbour got it once unasked). Measured: two scores over one form,
/// one with <c>transpose d</c>, draw different pages (135,854 vs 135,903 bytes, distinct
/// hashes) while this report answers C4 for the written <c>c</c> and warns about nothing.
/// Whether to warn — and in what words — is the open question, not whether the situation
/// exists.
/// </para>
/// <para>
/// ⚠️ A part that NO score renders is absent from this list. The validators still check
/// it — a five-quarter bar inside one warns — so the gap is in this report, not in
/// `check`. Resolving every DECLARED part from its own clef, score or no score, is a
/// change inside the collector; filed in HANDOFF §2F.
/// </para>
/// </remarks>
public static class ResolvedPitches
{
    /// <summary>
    /// The trace in written order, or null when not one score collected (a file broken
    /// enough that the caller should report its errors instead).
    /// </summary>
    public static IReadOnlyList<MeasureCollector.PitchTraceEntry>? ForFile(SyntaxTree tree)
    {
        var specs = RenderSpecParser.FindAll(tree);
        var resolved = new SortedDictionary<int, string>();
        int collected = 0;

        // A scoreless file gets one pass with no spec — the same fall-through
        // SemanticValidation.TryCollect(tree) takes. Written as an assignment rather than
        // `specs.Count > 0 ? specs : [null]`, because a conditional takes its natural type
        // from the non-empty arm and then converts `[null]` against RenderSpec, NOT
        // RenderSpec? (CS8625). Assigning gives the collection expression the declared type
        // directly; the covariant conversion means the spec'd path enumerates the list
        // itself, with no copy.
        IEnumerable<RenderSpec?> passes = specs;
        if (specs.Count == 0)
            passes = [null];
        foreach (var spec in passes)
        {
            var collector = SemanticValidation.TryCollect(tree, spec);
            if (collector == null)
                continue;
            collected++;
            foreach (var e in collector.PitchTrace)
                resolved.TryAdd(e.Position, e.Pitch);
        }

        if (collected == 0)
            return null;

        var trace = new List<MeasureCollector.PitchTraceEntry>(resolved.Count);
        foreach (var kv in resolved)
            trace.Add(new MeasureCollector.PitchTraceEntry(kv.Key, kv.Value));
        return trace;
    }
}
