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

using LilySharp.Core.Syntax;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Parser;

/// <summary>
/// Maps NEW-text positions to reusable green nodes from a previous parse —
/// the lookup half of the incremental reparse (the parser side adopts the
/// node and skips its tokens).
/// </summary>
/// <remarks>
/// A top-level item of the old tree is reusable iff:
/// <list type="bullet">
/// <item>its old span lies entirely OUTSIDE the damaged region (the edit
/// span widened by one character on each side, so a token whose text merely
/// touches the edit boundary is never reused), and</item>
/// <item>no diagnostic of the old tree touches its span — items with
/// diagnostics are re-parsed so the new parse regenerates their diagnostics
/// at the correct (possibly shifted) positions. This also sidesteps the
/// synthetic-missing-token width skew that error nodes can carry.</item>
/// </list>
/// Items before the damage keep their position; items after it shift by the
/// edit's length delta. Reuse is opportunistic: if the parser never lands
/// exactly on a mapped position, it simply parses normally.
/// <para>
/// ⚠️ THIS IS ON THE KEYSTROKE PATH — <c>DocumentManager</c> routes one edit per
/// keystroke through <c>SyntaxTree.WithChange</c>, so whatever happens here is paid per
/// character typed.
/// </para>
/// <para>
/// ⚠️ And its arithmetic is only as good as the tree's widths. <c>oldPos</c> accumulates
/// <c>item.FullWidth</c>, so a token the parser DROPPED — one no item rule could place,
/// which is what happened before LYS0030 (2026-08-16) — left every later item's computed
/// start short by that much, the keys stopped matching the parser's real
/// <c>_textPosition</c>, and the lookup simply missed. Measured on <c>perf-plain1k</c>
/// split into two sections with the caret in the second: one unplaceable token in the
/// FIRST section made the old build reuse <b>4.00</b> members per keystroke against
/// <b>5.67</b> for the identical book without it — and BOTH had zero diagnostics, so
/// nothing but the width arithmetic can account for the difference. The silence was a
/// keystroke-path cost as well as a correctness one, and no one had measured it.
/// </para>
/// </remarks>
internal sealed class IncrementalReuseMap
{
    private readonly Dictionary<int, GreenNode> _byNewPosition;

    private IncrementalReuseMap(Dictionary<int, GreenNode> byNewPosition)
    {
        _byNewPosition = byNewPosition;
    }

    public bool TryGet(int newPosition, out GreenNode node)
        => _byNewPosition.TryGetValue(newPosition, out node!);

    /// <summary>
    /// Builds the reuse map from the old tree and the damaged region
    /// (in OLD-text coordinates) plus the length delta of the edit.
    /// Returns null when nothing is reusable.
    /// </summary>
    public static IncrementalReuseMap? Create(
        SyntaxTree oldTree, int damageStart, int damageOldEnd, int delta)
    {
        var root = oldTree.Root;
        var diagnostics = oldTree.Diagnostics;

        // Widen the damage by one character on each side: an edit at a token
        // boundary can change how the neighbouring token lexes.
        damageStart = Math.Max(0, damageStart - 1);
        damageOldEnd = damageOldEnd + 1;

        var map = new Dictionary<int, GreenNode>();
        int oldPos = 0;

        // The item immediately BEFORE the damage is never reused: a top-level
        // production may greedily consume optional continuations from the
        // following tokens, and for that one item the following text changed.
        // Every other item is followed by unchanged text, so a fresh parse of
        // its (identical) tokens is guaranteed to produce the same node.
        int lastBeforeDamageKey = -1;

        // CompilationUnitGreen slots = [members..., endOfFile]; walk the members.
        for (int i = 0; i < root.SlotCount - 1; i++)
        {
            var item = root.GetSlot(i);
            if (item == null)
                continue;

            int itemStart = oldPos;
            int itemEnd = oldPos + item.FullWidth;
            oldPos = itemEnd;

            bool beforeDamage = itemEnd <= damageStart;
            bool afterDamage = itemStart >= damageOldEnd;
            if (!beforeDamage && !afterDamage)
                continue; // overlaps the edit — must re-parse

            if (HasDiagnosticIn(diagnostics, itemStart, itemEnd))
                continue; // re-parse so diagnostics are regenerated in place

            int newPosition = beforeDamage ? itemStart : itemStart + delta;
            if (newPosition < 0)
                continue;

            map[newPosition] = item;
            if (beforeDamage)
                lastBeforeDamageKey = Math.Max(lastBeforeDamageKey, newPosition);
        }

        if (lastBeforeDamageKey >= 0)
            map.Remove(lastBeforeDamageKey);

        return map.Count > 0 ? new IncrementalReuseMap(map) : null;
    }

    /// <summary>Whether any diagnostic TOUCHES the item spanning
    /// <paramref name="start"/>..<paramref name="end"/> — such an item must be re-parsed,
    /// so its diagnostics are produced again rather than lost with the reuse.</summary>
    /// <remarks>
    /// ⚠️ TOUCHING counts, not just overlapping, and each generation of this method learned
    /// that on a different width. A diagnostic's span and the span of the item whose parse
    /// PRODUCED it are different things: <c>Expect</c> reports at the FOUND token, which when
    /// an item's parse fails at its end is the first token of the NEXT item — entirely
    /// outside the producer's span. Reuse the producer and the diagnostic vanishes from the
    /// incremental parse while the tree shape stays identical (adoption emits nothing, and
    /// the neighbour's own re-parse reads that token as its ordinary first token).
    /// (1) Zero width (2026-08-16): Expect's missing token at end of file put an empty span
    /// exactly ON the last item's boundary; the then-strict overlap satisfied neither half.
    /// <c>WithChange_RandomizedEdits_MatchFullParse</c> caught it as 17 diagnostics against
    /// 16, the missing one "Expected integer measure-count after '*', found 'EndOfFile'".
    /// (2) Full width (2026-08-22): the same Expect failing mid-file spans the next item's
    /// first token — Start == the producer's end, so the strict test again said no, the
    /// producer was reused, and LYS0002 alone disappeared. Caught by the same fuzz once its
    /// Source lost the '$' sigils (session 228), shrunk to a 37-edit chain
    /// (scratch/p228/shrink), pinned by
    /// <c>WithChange_DiagnosticAtItemBoundary_SurvivesDistantEdit</c>.
    /// The class remark's bullet always said "touches"; the code now finally agrees. Cost:
    /// at most one extra neighbour re-parsed per boundary-touching diagnostic — and NONE on
    /// diagnostic-free text, which is why Probe's <c>reuse</c> meter must read unchanged on
    /// the perf books for any edit to this test.
    /// </remarks>
    private static bool HasDiagnosticIn(
        IReadOnlyList<Diagnostic> diagnostics, int start, int end)
    {
        foreach (var d in diagnostics)
        {
            if (d.Span.Start <= end && d.Span.End >= start)
                return true;
        }
        return false;
    }
}
