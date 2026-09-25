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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The position shifter of the suffix splice (HANDOFF's retired ▶ ⒭ (the incremental workstream, NOT §2 F ⒭) ⑵, second slice —
/// second half): re-homes a recorded collect tail's burned source positions
/// from OLD-text to NEW-text coordinates across an edit. The map is
/// PER-POSITION, not per-entry — <c>p ≤ prefix → p; p ≥ suffixStart → p+Δ;
/// inside the dirty window → undefined</c> — because one adopted entry can mix
/// provenances: a tail note cites its own (shifted) text while the phrase-
/// expanded note beside it cites an (unshifted) body declared above the edit.
/// An undefined position means the entry depends on window text the splice
/// cannot vouch for, so the whole splice is declined (never guessed at).
/// </summary>
/// <remarks>
/// The burned-position inventory is HANDOFF §1's session-145 memo ⑶, whose
/// type map is <c>MeasureContentKey</c>'s ItemExclusions/SideExclusions:
/// <c>MusicItem.SourcePosition</c> (with <c>ChordItem.Notes[].SourcePosition</c>
/// nested), the Measure quartet (SourceStart / SourceEnd / EndHighlightAliases /
/// SectionLabelPosition), and each cumulative side table's <c>SourcePosition</c>.
/// <c>HairpinItem.SourceIndex</c>-style fields are INDICES into other tables,
/// not positions, and must not be shifted. Positions the splice re-homes are
/// click-to-source data only (HANDOFF §1 145 ⑶: engravers key on measure/item
/// indices), so a shift can never change geometry — the SVG byte-identity net
/// (CollectEditResumeTests) holds the data-pos side.
/// ⚠️ DRIFT NET: CollectResumeTests.ShifterInventory_CoversEveryPositionField
/// reflects over every adopted type and fails when a position-named field this
/// class does not handle appears — extend BOTH together.
/// </remarks>
internal static class CollectTailShifter
{
    /// <summary>The dirty window in old-text coordinates; see the class remarks
    /// for the per-position map.</summary>
    internal readonly record struct Window(int Prefix, int SuffixStart, int Delta)
    {
        /// <summary>Maps an old-text position to new-text coordinates, or false
        /// when the position lies inside the dirty window. Non-positive values
        /// are sentinels (0 = none, -1 = fall-back) and pass through.</summary>
        public bool TryShift(int p, out int shifted)
        {
            // Strictly BELOW the prefix length: old positions 0..Prefix-1 hold
            // identical text; position Prefix is the first changed character,
            // so citing it is citing the window. Sentinels (0 = none, -1 =
            // fall-back) pass through the same branch.
            if (p <= 0 || p < Prefix)
            {
                shifted = p;
                return true;
            }
            if (p >= SuffixStart)
            {
                shifted = p + Delta;
                return true;
            }
            shifted = default;
            return false;
        }
    }

    public static Measure? ShiftMeasure(Measure m, in Window w)
    {
        if (!w.TryShift(m.SourceStart, out int start)
            || !w.TryShift(m.SourceEnd, out int end)
            || !w.TryShift(m.SectionLabelPosition, out int label))
            return null;

        // ⚠️ NOTHING MOVED ⇒ THE SAME INSTANCES. A Δ=0 edit, or a tail whose positions all sit
        // before the window, re-homes nothing, and the splice used to hand back a copy of every
        // measure and item anyway — MEASURED (session 601, the reader's corpus, 1,880 pitch
        // keystrokes): 61% of the final score's items were a new object with the old content,
        // the splice's copies among them. The adopted prefix already shares the recording's
        // instances (NoteItem.StampBeam's remarks: the bake clears before it stamps), so the
        // spliced tail sharing them stands on the same argument.
        bool moved = start != m.SourceStart || end != m.SourceEnd || label != m.SectionLabelPosition;

        var aliases = m.EndHighlightAliases;
        if (!aliases.IsDefaultOrEmpty)
        {
            int[]? shifted = null;
            for (int i = 0; i < aliases.Length; i++)
            {
                if (!w.TryShift(aliases[i], out int a))
                    return null;
                if (a != aliases[i])
                    (shifted ??= aliases.ToArray())[i] = a;
            }
            if (shifted != null)
            {
                aliases = ImmutableArray.Create(shifted);
                moved = true;
            }
        }

        var items = m.Items;
        if (!items.IsDefaultOrEmpty)
        {
            MusicItem[]? shifted = null;
            for (int i = 0; i < items.Length; i++)
            {
                if (ShiftItem(items[i], w) is not { } item)
                    return null;
                if (!ReferenceEquals(item, items[i]))
                    (shifted ??= items.ToArray())[i] = item;
            }
            if (shifted != null)
            {
                items = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(shifted);
                moved = true;
            }
        }

        if (!moved)
            return m;
        return m with
        {
            Items = items,
            SourceStart = start,
            SourceEnd = end,
            EndHighlightAliases = aliases,
            SectionLabelPosition = label,
        };
    }

    public static MusicItem? ShiftItem(MusicItem item, in Window w)
    {
        if (!w.TryShift(item.SourcePosition, out int pos)
            || !w.TryShift(item.TieStartSourcePosition, out int tiePos)
            || !w.TryShift(item.SlurStartSourcePosition, out int slurOpen)
            || !w.TryShift(item.SlurEndSourcePosition, out int slurClose)
            || !w.TryShift(item.PhrasingSlurStartSourcePosition, out int phrasingOpen)
            || !w.TryShift(item.PhrasingSlurEndSourcePosition, out int phrasingClose)
            || !w.TryShift(item.LaissezVibrerSourcePosition, out int lvPos)
            || !w.TryShift(item.RepeatTieSourcePosition, out int rtPos))
            return null;
        // The bow offsets ride the same map as the item's own: they are the `~`, `(`
        // and `)` written ON this item — and, for the half-ties, the `@` of the
        // `@laissezVibrer` / `@repeatTie` written on it. The -1 "nothing wrote it"
        // sentinel passes through TryShift's non-positive branch untouched.
        // Unmoved, the item is handed back as it is (see ShiftMeasure).
        if (pos != item.SourcePosition || tiePos != item.TieStartSourcePosition
            || slurOpen != item.SlurStartSourcePosition || slurClose != item.SlurEndSourcePosition
            || phrasingOpen != item.PhrasingSlurStartSourcePosition
            || phrasingClose != item.PhrasingSlurEndSourcePosition
            || lvPos != item.LaissezVibrerSourcePosition || rtPos != item.RepeatTieSourcePosition)
            item = item with
            {
                SourcePosition = pos,
                TieStartSourcePosition = tiePos,
                SlurStartSourcePosition = slurOpen,
                SlurEndSourcePosition = slurClose,
                PhrasingSlurStartSourcePosition = phrasingOpen,
                PhrasingSlurEndSourcePosition = phrasingClose,
                LaissezVibrerSourcePosition = lvPos,
                RepeatTieSourcePosition = rtPos,
            };

        // The nested positions: a chord member's own pitch token, and the `@` of a
        // member-level half-tie annotation (the same fields
        // MeasureContentKey.AddValue special-cases for the same reason).
        if (item is ChordItem chord && !chord.Notes.IsDefaultOrEmpty)
        {
            ChordNoteInfo[]? notes = null;
            for (int i = 0; i < chord.Notes.Length; i++)
            {
                var n = chord.Notes[i];
                if (!w.TryShift(n.SourcePosition, out int np)
                    || !w.TryShift(n.LaissezVibrerSourcePosition, out int nlv)
                    || !w.TryShift(n.RepeatTieSourcePosition, out int nrt))
                    return null;
                if (np != n.SourcePosition || nlv != n.LaissezVibrerSourcePosition
                    || nrt != n.RepeatTieSourcePosition)
                    (notes ??= chord.Notes.ToArray())[i] = n with
                    {
                        SourcePosition = np,
                        LaissezVibrerSourcePosition = nlv,
                        RepeatTieSourcePosition = nrt,
                    };
            }
            if (notes != null)
                item = chord with { Notes = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(notes) };
        }
        return item;
    }

    /// <summary>Shifts one cumulative side-table entry
    /// (<c>MeasureCollector.CumulativeSideTables()</c>'s element types — keep
    /// this switch in step with that list and with the drift net). Returns null
    /// when a position lies in the window; throws on an element type this
    /// switch has never seen (a new table must be wired here consciously —
    /// silently adopting it unshifted would emit stale data-pos).</summary>
    public static object? ShiftSideEntry(object entry, in Window w)
    {
        switch (entry)
        {
            case DynamicItem e:
                return w.TryShift(e.SourcePosition, out int p1) ? e with { SourcePosition = p1 } : null;
            case ArticulationItem e:
                return w.TryShift(e.SourcePosition, out int p2) ? e with { SourcePosition = p2 } : null;
            case GraceNoteItem e:
                return w.TryShift(e.SourcePosition, out int p3) ? e with { SourcePosition = p3 } : null;
            case MusicMarkItem e:
                return w.TryShift(e.SourcePosition, out int p4) ? e with { SourcePosition = p4 } : null;
            case CustomTextItem e:
                return w.TryShift(e.SourcePosition, out int p5) ? e with { SourcePosition = p5 } : null;
            case VoltaBracketItem e:
                return w.TryShift(e.SourcePosition, out int p6) ? e with { SourcePosition = p6 } : null;
            case TupletBracketItem e:
                return w.TryShift(e.SourcePosition, out int p7) ? e with { SourcePosition = p7 } : null;
            case ArpeggioItem e:
                return w.TryShift(e.SourcePosition, out int p8) ? e with { SourcePosition = p8 } : null;
            case FiguredBassItem e:
                return w.TryShift(e.SourcePosition, out int p9) ? e with { SourcePosition = p9 } : null;
            case PercentRepeatItem e:
                return w.TryShift(e.SourcePosition, out int p10) ? e with { SourcePosition = p10 } : null;
            case CrossStaffItem e:
                return w.TryShift(e.SourcePosition, out int p11) ? e with { SourcePosition = p11 } : null;
            case ChordNameItem e:
                return w.TryShift(e.SourcePosition, out int p12) ? e with { SourcePosition = p12 } : null;
            case NavigationMarkPlacementWarning e:
                return w.TryShift(e.SourcePosition, out int p13) ? e with { SourcePosition = p13 } : null;
            case TieTargetWarning e:
                return w.TryShift(e.SourcePosition, out int p14) ? e with { SourcePosition = p14 } : null;
            case UnpairedSlurWarning e:
                return w.TryShift(e.SourcePosition, out int p15) ? e with { SourcePosition = p15 } : null;
            case UnpairedBeamWarning e:
                return w.TryShift(e.SourcePosition, out int p18) ? e with { SourcePosition = p18 } : null;
            case CueSpanBoundaryWarning e:
                return w.TryShift(e.SourcePosition, out int p19) ? e with { SourcePosition = p19 } : null;
            // Position-free entries pass through by value (records/structs are
            // immutable; sharing the instance with the recording is safe).
            case GrobOverride or GrobRevert:
                return entry;
            case MeasureCollector.PitchTraceEntry e:
                return w.TryShift(e.Position, out int p16) ? e with { Position = p16 } : null;
            case ValueTuple<bool, int, int, int, int, int, int> trill:
                // _trillSpannerEvents: (isStart, measureIndex, itemIndex,
                // sourcePosition, staffIndex, voiceIndex, forcedDir).
                return w.TryShift(trill.Item4, out int p17)
                    ? trill with { Item4 = p17 }
                    : null;
            default:
                throw new InvalidOperationException(
                    $"CollectTailShifter: unshifted side-table element type {entry.GetType().Name} " +
                    "— a new cumulative side table must be wired into ShiftSideEntry (and the drift net).");
        }
    }

    /// <summary>Finds the NEW tree's node standing where <paramref name="oldNode"/>
    /// stood in the baseline — same kind, same width, start shifted by the
    /// window — by descending only the spine that contains the target position.
    /// Null when no such node exists (structure drifted; decline the splice).
    /// The suffix parse agreement (CollectResumePlanner.ParseSuffixAgrees) is
    /// what makes a found node's SUBTREE trustworthy, not just its header.</summary>
    public static SyntaxNode? ResolveShifted(SyntaxNode newRoot, SyntaxNode oldNode, in Window w)
    {
        if (!w.TryShift(oldNode.FullSpan.Start, out int start))
            return null;
        int width = oldNode.FullSpan.End - oldNode.FullSpan.Start;

        var node = newRoot;
        while (true)
        {
            if (node.Kind == oldNode.Kind
                && node.FullSpan.Start == start
                && node.FullSpan.End - node.FullSpan.Start == width)
                return node;

            SyntaxNode? next = null;
            foreach (var child in node.ChildNodes())
            {
                if (child.FullSpan.Start <= start && start < child.FullSpan.End)
                {
                    next = child;
                    break;
                }
            }
            if (next == null)
                return null;
            node = next;
        }
    }
}
