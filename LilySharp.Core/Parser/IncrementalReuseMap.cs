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

    /// <summary>Whether any diagnostic belongs to the item spanning
    /// <paramref name="start"/>..<paramref name="end"/> — such an item must be re-parsed,
    /// so its diagnostics are produced again rather than lost with the reuse.</summary>
    /// <remarks>
    /// ⚠️ A ZERO-WIDTH span needs its own arm and did not have one until 2026-08-16. The
    /// overlap test is strict on both sides, so a diagnostic with <c>Start == End</c> sitting
    /// exactly ON a boundary satisfies neither half — and an empty span is what
    /// <c>Expect</c> produces when the token it wanted is missing, which at end of file puts
    /// it exactly at the last item's end. The item was then REUSED and its diagnostic
    /// vanished from the incremental parse: <c>WithChange_RandomizedEdits_MatchFullParse</c>
    /// caught it as 17 diagnostics against 16, the missing one being
    /// "Expected integer measure-count after '*', found 'EndOfFile'". The bug is as old as
    /// the test above it; it surfaced when unplaceable tokens became members (LYS0030) and
    /// moved where item boundaries fall. Touching either end counts, which can exclude the
    /// neighbouring item too — the safe direction, since the cost is one item re-parsed.
    /// </remarks>
    private static bool HasDiagnosticIn(
        IReadOnlyList<Diagnostic> diagnostics, int start, int end)
    {
        foreach (var d in diagnostics)
        {
            if (d.Span.Start < end && d.Span.End > start)
                return true;
            if (d.Span.Start == d.Span.End && d.Span.Start >= start && d.Span.Start <= end)
                return true;
        }
        return false;
    }
}
