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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Plans a cross-edit (Δ≠0) resume of the collect walks
/// (<see cref="CollectWalkProbe"/>): given the recording of a previous collect
/// (the BASELINE) and the edited text, computes the dirty window — the old/new
/// texts' common prefix P and common suffix — and, per walk, picks BOTH ends:
/// the PREFIX side (the last checkpoint whose whole read set lies in the
/// unchanged prefix, <see cref="WalkCheckpoint.MaxSourceRead"/> ≤ P — adopted
/// state is a function of unchanged text, positions included, so no shifter)
/// and the SUFFIX side (the checkpoints the live walk may re-meet past the
/// window; the splice itself — state comparison, position shift, tail adoption
/// — runs in <c>MeasureCollector.TrySpliceSuffix</c> with
/// <see cref="CollectTailShifter"/>). The walk then runs live only across the
/// dirty window between the two.
/// </summary>
/// <remarks>
/// <para>
/// SOUNDNESS = "adopted state is a function of [0, P)". Three layers hold it:
/// (1) per-checkpoint, MaxSourceRead folds every source read the walk makes
/// (see its remarks for the fold sites); (2) the collect-level inputs that seed
/// walk entry state OUTSIDE any walk (file defaults, headers, top-level
/// overrides…) are guarded here by <see cref="WindowRespectsTopLevel"/> —
/// conservatively, any top-level node that is not a known music-body container
/// must end before P (its VALUES must be unchanged AND its burned positions
/// must not shift, hence "before P", not merely "outside the window");
/// (3) runtime validations at walk entry / restore (voice identity, cumulative
/// table counts, node-start equality) bail to a full collect via
/// <see cref="CollectResumeAbortException"/> on any structural drift the text
/// comparison could not see; (4) the parse-prefix agreement — byte equality is
/// not parse equality near the window (parser lookahead crosses it), so the
/// baseline and new TREES must structurally agree below the deepest adopted
/// read (see <see cref="ParsePrefixAgrees"/>).
/// </para>
/// <para>
/// A plan is reuse, never truth: a missed plan only costs speed, and the
/// completeness net (CollectEditResumeTests: resumed == full on synthetic
/// edits over every fixture/sample) is what holds the guard set honest.
/// </para>
/// </remarks>
internal static class CollectResumePlanner
{
    /// <summary>
    /// Builds a resume probe for collecting <paramref name="newTree"/> against the
    /// recorded baseline, or null when nothing can be safely resumed (callers then
    /// run a full collect and re-record).
    /// </summary>
    public static CollectWalkProbe? Plan(
        SyntaxTree baselineTree, SyntaxTree newTree,
        CollectWalkProbe recording, MeasureCollector source)
    {
        string oldText = baselineTree.Text;
        string newText = newTree.Text;
        // The common suffix bounds the dirty window from the right; the DELTA says
        // whether positions after the window shift. The guard distinguishes three
        // grades of "unchanged" for a baseline span:
        //   before P            → content and positions unchanged, always;
        //   in the suffix, Δ=0  → content and positions unchanged too;
        //   in the suffix, Δ≠0  → content unchanged, positions shifted — enough
        //                         only for value-only readers (form order, a
        //                         headerless score block at the file's bottom).
        var (prefix, suffixStart, delta) = ComputeWindow(oldText, newText);

        if (!WindowRespectsTopLevel(baselineTree, prefix, suffixStart, delta))
            return null;

        var resumer = CollectWalkProbe.Resumer();
        resumer.WindowPrefix = prefix;
        resumer.WindowSuffixStart = suffixStart;
        resumer.WindowDelta = delta;

        // SUFFIX-side collect-level guard: a phrase/variable body is read at its
        // CALL SITES, and a tail call site re-reads the declaration text. Reads
        // that produce positioned output are caught per-position by the shifter,
        // but a body of pure directives (`phrase p { key g }`) would feed VALUE
        // state from window text with no position to trip over — so when the
        // window touches any phrase/variable declaration, the suffix side is off
        // wholesale (the prefix side already invalidates through the folded body
        // span in MaxSourceRead). Costs reuse on in-body edits, never soundness.
        bool suffixEligible = !WindowTouchesPhraseOrVariable(
            baselineTree.GetRoot(), prefix, suffixStart);

        foreach (var (ordinal, rec) in recording.Recordings)
        {
            if (rec.IneligibleReason != null || rec.Checkpoints.Count == 0)
                continue;

            // A header span the walk read must be content- AND position-stable
            // (they burn label/data-pos positions and seed prologue state). The
            // reads are appended in walk order, so a running count of "stable so
            // far" lets each checkpoint be judged on exactly its own read prefix —
            // a shifted LATER section header never rejects an EARLIER checkpoint.
            var headerReads = rec.HeaderReads;
            int stableHeaderReads = 0;

            // MaxSourceRead is a running max, so it is non-decreasing along the
            // walk: scan forward, keep the last passing checkpoint, stop at the
            // first that reads past the prefix.
            WalkCheckpoint? best = null;
            foreach (var ck in rec.Checkpoints)
            {
                if (ck.MaxSourceRead > prefix)
                    break;
                if (ck.NodeStart > prefix)
                    continue;
                while (stableHeaderReads < ck.HeaderReadCount
                       && headerReads != null && stableHeaderReads < headerReads.Count
                       && SpanStable(headerReads[stableHeaderReads], prefix, suffixStart, delta))
                    stableHeaderReads++;
                if (stableHeaderReads < ck.HeaderReadCount)
                    break; // an unstable header read; every later checkpoint reads it too
                best = ck;
            }
            // An empty prefix adopts nothing — no prefix target, but the walk
            // may still suffix-splice (an early edit is exactly the case whose
            // reuse lives entirely on the suffix side).
            if (best is { MeasureCount: 0 })
                best = null;

            // SUFFIX candidates: recorded checkpoints the live walk can re-meet
            // past the window. A candidate's own node must stand outside the
            // window (else its shifted address is undefined), and every header
            // read at-or-after its watermark must be content-stable with a
            // well-defined shift (span wholly before the window, or wholly at/
            // after the suffix — those burned positions shift with the delta the
            // splice applies). Header reads are appended in walk order, so the
            // minimal safe watermark is one past the LAST unstable read.
            IReadOnlyList<WalkCheckpoint>? suffixCandidates = null;
            if (suffixEligible && rec.EndCheckpoint != null)
            {
                int headerFloor = 0;
                if (rec.HeaderReads is { } reads)
                {
                    for (int i = reads.Count - 1; i >= 0; i--)
                    {
                        if (!(reads[i].End <= prefix || reads[i].Start >= suffixStart))
                        {
                            headerFloor = i + 1;
                            break;
                        }
                    }
                }
                var win = new CollectTailShifter.Window(prefix, suffixStart, delta);
                List<WalkCheckpoint>? candidates = null;
                foreach (var ck in rec.Checkpoints)
                {
                    if (ck.HeaderReadCount < headerFloor)
                        continue;
                    if (!win.TryShift(ck.NodeStart, out _))
                        continue;
                    (candidates ??= new()).Add(ck);
                }
                suffixCandidates = candidates;
            }

            if (best == null && suffixCandidates == null)
                continue;

            resumer.ResumePlans[ordinal] = new VoiceResumePlan
            {
                Checkpoint = best,
                Recording = rec,
                Source = source,
                SuffixCandidates = suffixCandidates,
            };
        }
        if (resumer.ResumePlans.Count == 0)
            return null;

        // Layer 4, prefix half — the parse-prefix agreement. "Byte-identical
        // prefix" is NOT "identical prefix parse": the parser's lookahead can
        // cross the window, so a node ENDING before P may parse differently
        // when the bytes just past P changed (found by the session fuzz net: a
        // `//` comment insertion reshaped a flat list whose entries all ended
        // before P, and a resumed walk adopted measures whose notes the full
        // collect no longer produced). The adopted state is a function of the
        // baseline PARSE below the reads, so require the new tree's structure
        // to agree with the baseline's below the deepest read any prefix plan
        // adopts. Reference-reused greens short-circuit (the Edit path's
        // incremental reparse), so this is near-free per keystroke and
        // O(prefix nodes) only on a full reparse. The SUFFIX half — a
        // whole-file agreement pair — is deliberately deferred to the first
        // splice attempt (ParseAgreementsHold below).
        var prefixPlans = resumer.ResumePlans.Values.Where(p => p.Checkpoint != null).ToList();
        if (prefixPlans.Count > 0)
        {
            int deepestRead = prefixPlans.Max(p => p.Checkpoint!.MaxSourceRead);
            if (!ParsePrefixAgrees(baselineTree.GetRoot(), newTree.GetRoot(), deepestRead))
                return null;
        }
        resumer.BaselineRoot = baselineTree.GetRoot();
        resumer.NewRoot = newTree.GetRoot();

        return resumer;
    }

    /// <summary>Layer 4's SUFFIX half, verified lazily from the first splice
    /// attempt whose cheaper guards all passed (memoized on the probe): before
    /// ANY recorded tail may be adopted, the WHOLE prefix and the shifted suffix
    /// must parse-agree — tail reads can touch any prefix text (a phrase body
    /// declared above the edit, a form-reordered earlier section), not just what
    /// the prefix checkpoints read, and the suffix steps in the mirrored
    /// lookahead hole the prefix-half remarks above describe. Lazy on purpose:
    /// the whole-tree walk taxed every keystroke of a book that can never splice
    /// when it sat at plan time (perf-v2bow1k: one m=0 candidate, always
    /// declined). Typical honest failure: a deleted barline merges two measures,
    /// so an old node ending at P has no structural twin — such an edit changes
    /// downstream state anyway, so declining every splice loses nothing real.</summary>
    internal static bool ParseAgreementsHold(CollectWalkProbe probe)
    {
        // An empty window with Δ=0 means the two TEXTS are identical (the
        // baseline-restore keystroke of an alternating edit session): identical
        // text parses identically, no walk needed.
        if (probe.WindowDelta == 0 && probe.WindowPrefix >= probe.WindowSuffixStart)
            return true;
        probe.ParseAgreementsVerified ??=
            probe.BaselineRoot != null && probe.NewRoot != null
            && ParsePrefixAgrees(probe.BaselineRoot, probe.NewRoot, probe.WindowPrefix)
            && ParseSuffixAgrees(probe.BaselineRoot, probe.NewRoot,
                probe.WindowSuffixStart, probe.WindowDelta);
        return probe.ParseAgreementsVerified == true;
    }

    /// <summary>True when the dirty window intersects any phrase/variable
    /// declaration — walked over the narrow band of nodes that intersect the
    /// window only (O(depth + window nodes), never the whole tree).</summary>
    private static bool WindowTouchesPhraseOrVariable(SyntaxNode node, int prefix, int suffixStart)
    {
        if (node.FullSpan.End <= prefix || node.FullSpan.Start >= suffixStart)
            return false;
        if (node is PhraseDeclarationSyntax or VariableDeclarationSyntax)
            return true;
        foreach (var child in node.ChildNodes())
        {
            if (WindowTouchesPhraseOrVariable(child, prefix, suffixStart))
                return true;
        }
        return false;
    }

    /// <summary>The suffix mirror of <see cref="ParsePrefixAgrees"/>: structural
    /// equality of the two trees at/after the window — same kinds and widths,
    /// new starts shifted by <paramref name="delta"/> for nodes wholly at/after
    /// <paramref name="oldLimit"/> in the baseline, recursing only into subtrees
    /// that overlap [oldLimit, ∞). Token streams are a pure function of the text
    /// (byte-identical at/after the window), so node-level agreement pins the
    /// whole suffix parse.</summary>
    private static bool ParseSuffixAgrees(SyntaxNode a, SyntaxNode b, int oldLimit, int delta)
    {
        if (a.Kind != b.Kind)
            return false;
        bool aAbove = a.FullSpan.Start >= oldLimit;
        bool bAbove = b.FullSpan.Start >= oldLimit + delta;
        if (aAbove != bAbove)
            return false;
        if (aAbove)
        {
            if (b.FullSpan.Start != a.FullSpan.Start + delta
                || b.FullSpan.End - b.FullSpan.Start != a.FullSpan.End - a.FullSpan.Start)
                return false;
            // Same green shifted by exactly the delta = identical subtree
            // (greens are position-free); the Edit path's reuse map hits this.
            if (ReferenceEquals(a.Green, b.Green))
                return true;
        }

        // Children in position order; compare the sub-forests that END after
        // the limit (the old side) / the shifted limit (the new side). A
        // straddling node recurses without a start check — its below-window
        // part is the prefix agreement's business, not this one's.
        using IEnumerator<SyntaxNode> ea = a.ChildNodes().GetEnumerator();
        using IEnumerator<SyntaxNode> eb = b.ChildNodes().GetEnumerator();
        SyntaxNode? ca = NextOverlapping(ea, oldLimit);
        SyntaxNode? cb = NextOverlapping(eb, oldLimit + delta);
        while (true)
        {
            if ((ca == null) != (cb == null))
                return false;
            if (ca == null)
                return true;
            if (!ParseSuffixAgrees(ca, cb!, oldLimit, delta))
                return false;
            ca = NextOverlapping(ea, oldLimit);
            cb = NextOverlapping(eb, oldLimit + delta);
        }

        static SyntaxNode? NextOverlapping(IEnumerator<SyntaxNode> e, int limit)
        {
            while (e.MoveNext())
            {
                if (e.Current.FullSpan.End > limit)
                    return e.Current;
            }
            return null;
        }
    }

    /// <summary>Structural equality of the two trees below <paramref name="limit"/>:
    /// same kinds, same starts, same widths for nodes wholly below it, recursing
    /// only into subtrees that overlap [0, limit). Token streams are a pure
    /// function of the text (byte-identical below the window), so node-level
    /// agreement pins the whole prefix parse.</summary>
    private static bool ParsePrefixAgrees(SyntaxNode a, SyntaxNode b, int limit)
    {
        if (a.Kind != b.Kind || a.FullSpan.Start != b.FullSpan.Start)
            return false;
        // Same green at the same position = identical subtree (greens are
        // position-free); this is what the Edit path's reuse map hits.
        if (ReferenceEquals(a.Green, b.Green))
            return true;
        bool aBelow = a.FullSpan.End <= limit;
        bool bBelow = b.FullSpan.End <= limit;
        if (aBelow != bBelow || (aBelow && a.FullSpan.End != b.FullSpan.End))
            return false;

        using var ea = a.ChildNodes().GetEnumerator();
        using var eb = b.ChildNodes().GetEnumerator();
        while (true)
        {
            // Children are in position order, so the first child at/past the
            // limit ends the comparison on that side.
            SyntaxNode? ca = ea.MoveNext() && ea.Current.FullSpan.Start < limit ? ea.Current : null;
            SyntaxNode? cb = eb.MoveNext() && eb.Current.FullSpan.Start < limit ? eb.Current : null;
            if ((ca == null) != (cb == null))
                return false;
            if (ca == null)
                return true;
            if (!ParsePrefixAgrees(ca, cb!, limit))
                return false;
        }
    }

    /// <summary>
    /// The single edit window between two texts: the common-prefix length P, the
    /// old-text offset where the common suffix starts, and the position delta for
    /// offsets at/after it. THE one spelling of this arithmetic — the collect splice
    /// (this planner / <see cref="CollectTailShifter"/>) and the render fragment
    /// replay (<c>SvgSystemFragmentCache</c>) both shift burned positions by it.
    /// </summary>
    internal static (int Prefix, int SuffixStart, int Delta) ComputeWindow(
        string oldText, string newText)
    {
        int prefix = CommonPrefix(oldText, newText);
        int suffixStart = oldText.Length - CommonSuffix(oldText, newText, prefix);
        int delta = newText.Length - oldText.Length;
        return (prefix, suffixStart, delta);
    }

    private static int CommonPrefix(string a, string b)
    {
        int n = System.Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < n && a[i] == b[i])
            i++;
        return i;
    }

    /// <summary>Length of the common suffix, clamped so it never overlaps the
    /// common prefix (an insertion inside a run of equal characters would
    /// otherwise double-count the run).</summary>
    private static int CommonSuffix(string a, string b, int prefix)
    {
        int n = System.Math.Min(a.Length, b.Length) - prefix;
        int i = 0;
        while (i < n && a[a.Length - 1 - i] == b[b.Length - 1 - i])
            i++;
        return i;
    }

    /// <summary>Content AND positions unchanged: wholly before the window, or —
    /// when the edit preserves length (Δ=0) — wholly after it. Header reads and
    /// state feeders take this test (they burn source positions into checkpointed
    /// state); value-only readers take the weaker suffix test at their call site.</summary>
    private static bool SpanStable(TextSpan span, int prefix, int suffixStart, int delta)
        => span.End <= prefix || (delta == 0 && span.Start >= suffixStart);

    /// <summary>
    /// The collect-level guard (layer 2 of the soundness argument above), over the
    /// BASELINE tree's top-level items:
    /// <list type="bullet">
    /// <item>Music-body containers (section/part/phrase/variable/chords/lyrics
    /// declarations) need no top-level constraint: their MUSIC reads fold into
    /// MaxSourceRead, and their header reads (section names/directives, part
    /// config) are recorded per walk and validated per checkpoint (HeaderReads) —
    /// a renamed phrase/variable invalidates through its folded BODY span.</item>
    /// <item>A form/structure declaration orders the section visits and a render
    /// (score) block seeds the collector's spec: value-only readers, so textually
    /// unchanged suffices even when positions shift (a bottom-of-file block below
    /// the edit stays usable) — EXCEPT a render block restating `title`/`composer`
    /// (HeaderOverrides bakes those token positions into MetadataState), which
    /// must be fully stable.</item>
    /// <item>Everything else (file-default key/time/clef/tempo/partial, title,
    /// font, drummap, top-level overrides, error-recovery leftovers — the
    /// conservative default for kinds this list has never seen) feeds value state
    /// AND source positions into the checkpoints, so it must be fully stable.</item>
    /// </list>
    /// </summary>
    private static bool WindowRespectsTopLevel(
        SyntaxTree baselineTree, int prefix, int suffixStart, int delta)
    {
        // Content unchanged; positions may have shifted (Δ≠0 suffix).
        bool ValueStable(TextSpan span)
            => span.End <= prefix || span.Start >= suffixStart;

        foreach (var node in baselineTree.GetRoot().ChildNodes())
        {
            switch (node)
            {
                case SectionDeclarationSyntax or PartDeclarationSyntax
                    or PhraseDeclarationSyntax or VariableDeclarationSyntax
                    or ChordPartBlockSyntax or LyricsBlockSyntax:
                    continue;

                // The end-of-file token closes every compilation unit (its full
                // span is the file-tail trivia); trivia feeds no collector state.
                case SyntaxTokenNode { Kind: SyntaxKind.EndOfFile }:
                    continue;

                case FormDeclarationSyntax when ValueStable(node.FullSpan):
                    continue;

                case RenderDeclarationSyntax render
                    when render.DescendantNodes().OfType<MetadataDeclarationSyntax>().Any()
                        ? SpanStable(render.FullSpan, prefix, suffixStart, delta)
                        : ValueStable(render.FullSpan):
                    continue;

                default:
                    if (!SpanStable(node.FullSpan, prefix, suffixStart, delta))
                        return false;
                    continue;
            }
        }
        return true;
    }
}
