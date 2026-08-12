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
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg;

/// <summary>
/// The F3 incremental compiler (engine slice S4b): a stateful render session
/// that reuses the previous line-break solution when an edit does not change the
/// line-break gate, so the (heavy) global Knuth-Plass DP is skipped on the
/// majority of edits — those that do not change any measure's natural width
/// (the F3 incremental design notes §4).
/// </summary>
/// <remarks>
/// <para>
/// Correctness over speed: <see cref="Edit"/> always produces SVG byte-identical
/// to a full recompile of the edited text. The skip is SOUND because the break
/// solution is a pure function of the per-measure spring vector
/// (<see cref="SystemBreaker.ComputeMultiStaffSpringData"/>) plus the line-start
/// prefix widths (which depend on key/time) plus the paper width; the session
/// caches all of those (the "gate") and only reuses the cached line sizes when
/// the gate compares equal. When it changes, it recomputes the breaks fully.
/// This is asserted by IncrementalCompilerTests via the S1 differential harness
/// (incremental == full) over both width-preserving and width-changing edits.
/// </para>
/// <para>
/// Everything DOWNSTREAM of line-breaking (system layout, paging, spanners,
/// rendering) still runs every edit — this slice elides only the break DP. Later
/// slices (S5) memoize per-measure semantics/layout to skip more.
/// </para>
/// <para>
/// Handles the first/default render block (like
/// <see cref="SvgGenerator.Generate(SyntaxTree, SvgRenderOptions, string)"/> with
/// no name). Multi-render-name selection is out of scope for this slice.
/// </para>
/// </remarks>
public sealed class IncrementalCompiler
{
    private readonly SvgRenderOptions _options;
    private SyntaxTree _tree;

    // Cached line-break gate and its solution. _lineSizes != null marks a warm
    // cache. _springs is internal MeasureSpringData (this type lives in Core).
    private MeasureSpringData[]? _springs;
    private double _firstPrefix;
    private double _contPrefix;
    private int[]? _lineSizes;
    // The score-global common shortest duration _springs was built with (⒟⁗ per-measure
    // memo): every spring's duration space is a function of it, so the memo may only
    // reuse a measure's springs while it is unchanged. Null until the first build.
    private double? _shortest;

    // F3/S5-3a: persists across edits so unchanged systems reuse their (spring)
    // measure layout. Installed only for the single-staff, override-free path
    // (see Compile); null otherwise => full layout, byte-identical.
    private SystemLayoutCache? _systemCache;

    // F3/B-2: the whole previous ScoreLayout, plus the complete per-measure content
    // key vector and the score-global layout inputs it was built from. When an edit
    // leaves ALL of these (and the line-break gate) unchanged, the layout geometry is
    // position-independent and can be reused wholesale — the renderer re-derives every
    // annotation's data-pos from the live (edited) score (SharedRenderer.ResolveDataPos),
    // so the reused layout renders byte-identical to a full recompile.
    private ScoreLayout? _cachedLayout;
    private ImmutableArray<MeasureContentKey> _contentKeys;
    private (string? Title, string? Composer, int? Tempo, int SwingSubdivision,
        string? TempoText, int TempoBeatUnit, int TempoDots) _globalKey;

    // F3/S5-4 (⒭ ⑵, second slice — prefix side): the collect walk's checkpoint
    // recording from the last FULL collect, kept as the resume baseline. An edit
    // resumes each walk from its last checkpoint that reads only the old/new
    // texts' common prefix (CollectResumePlanner), so the walk skips the measures
    // before the edit instead of re-collecting the whole book per keystroke.
    // Resumed collects do NOT re-record: the baseline stays pinned to
    // _collectBaselineTree's text until a full collect refreshes it (first
    // compile, an unplannable edit, or a mid-collect bail).
    private MeasureCollector? _collectSource;
    private CollectWalkProbe? _collectRecording;
    private SyntaxTree? _collectBaselineTree;

    /// <summary>Whether the most recent <see cref="Edit"/> reused the cached
    /// break solution (true) or recomputed it (false). For diagnostics / tests.</summary>
    public bool LastEditSkippedLineBreak { get; private set; }

    /// <summary>Whether the most recent <see cref="Edit"/> reused the ENTIRE cached
    /// ScoreLayout (skipping <see cref="LayoutEngine"/>.Layout outright). Implies
    /// <see cref="LastEditSkippedLineBreak"/>. For diagnostics / tests.</summary>
    public bool LastEditReusedLayout { get; private set; }

    /// <summary>How many collect walks the most recent compile resumed from a
    /// recorded checkpoint (0 = full collect), how many measures those resumes
    /// adopted instead of re-collecting, plus the suffix side: how many walks
    /// spliced their recorded tail and how many measures those splices adopted.
    /// For diagnostics / tests.</summary>
    internal (int Walks, int AdoptedMeasures, int SplicedWalks, int SplicedMeasures)
        LastCollectResume { get; private set; }

    /// <summary>How the most recent spring-vector build was paid for (⒟⁗): how many
    /// measures' springs were reused from the previous edit's vector via the per-measure
    /// content-key memo, and how many were recomputed. (0, 0) when the whole vector was
    /// reused by reference (content-unchanged edit) or on a full compile.
    /// For diagnostics / tests.</summary>
    internal (int Reused, int Recomputed) LastSpringMemo { get; private set; }

    /// <summary>The spring vector the most recent compile ended with — the same array the
    /// break gate and the layout consumed. For the tests that assert the per-measure memo
    /// reproduces a from-scratch build exactly.</summary>
    internal MeasureSpringData[]? SpringsForTest => _springs;

    /// <summary>The current syntax tree (after the last edit).</summary>
    public SyntaxTree Tree => _tree;

    /// <summary>Test/diagnostic access to the per-system layout cache (null until an
    /// override-free, single-voice edit first installs one; retained thereafter, even
    /// across intervening ineligible edits, though it is not consulted while ineligible).
    /// Lets tests assert that unchanged systems are reused rather than recomputed.</summary>
    internal SystemLayoutCache? SystemCache => _systemCache;

    /// <summary>Creates an incremental compiler seeded with an initial tree.</summary>
    public IncrementalCompiler(SyntaxTree tree, SvgRenderOptions? options = null)
    {
        _tree = tree;
        _options = options ?? SvgRenderOptions.Default;
    }

    /// <summary>Fully compiles the current tree and (re)establishes the cache.</summary>
    public string Render() => Compile(_tree, allowSkip: false);

    /// <summary>Applies an edit and renders incrementally (line-breaking skipped
    /// when the gate is unchanged). Result equals a full recompile of the edited text.</summary>
    public string Edit(TextChange change) => Compile(_tree.WithChange(change), allowSkip: true);

    /// <summary>Renders an ALREADY-updated tree incrementally — for a caller (the LSP
    /// preview) that maintains the tree itself (its own incremental reparse) rather than
    /// handing this session a <see cref="TextChange"/>. The first call warms the cache
    /// (full compile); later calls reuse the systems whose content is unchanged. The
    /// result is byte-identical to a full recompile of <paramref name="tree"/> — reuse is
    /// keyed on the new score's per-measure content, not on how the tree was produced.</summary>
    public string RenderIncremental(SyntaxTree tree) => Compile(tree, allowSkip: true);

    private string Compile(SyntaxTree tree, bool allowSkip)
    {
        var spec = RenderSpecParser.FindFirst(tree);
        var score = CollectWithResume(tree, spec, allowResume: allowSkip);

        // F3/S5-3: install/refresh the per-system layout cache for scores without
        // grob overrides (overrides can change spacing GLOBALLY, so a per-measure key
        // cannot localize them — those fall back to full layout, byte-identical).
        // Both single- and multi-staff benefit now that the two dominant per-system
        // phases — the spring solve AND the skyline (the larger, esp. on multi-staff)
        // — are memoized; Compute(MultiStaffScore) gives the sound all-staff key.
        bool overrideFree = score.GrobOverrides.IsDefaultOrEmpty
            && score.GrobReverts.IsDefaultOrEmpty;
        // Polyphony is no longer disqualifying. BOTH gates now see every voice:
        //   - the SPRING gate always did — ComputeMultiStaffSpringData passes
        //     `primaryMeasure` only as the anchor and builds the springs from
        //     CollectAllMeasuresAtIndex / CollectAllTimingsForMeasure, i.e. all staves
        //     AND all their voices (SystemBreaker.cs:124-127);
        //   - the CONTENT KEY now folds each staff's secondary voices too
        //     (MeasureContentKey.Compute, discriminated by voice index).
        // So an edit confined to voice 2 moves the key and correctly declines reuse,
        // while an edit that leaves every voice's content alone can reuse. Previously
        // this was gated off wholesale with !score.HasSecondaryVoices, which cost the
        // fast path on essentially all real repertoire (piano, choral, any `voice {}`).
        bool reuseEligible = overrideFree;
        var contentKeys = reuseEligible
            ? MeasureContentKey.Compute(score)
            : default;
        // The cache's entries are content-addressed and re-verified on lookup, so they
        // stay valid across a transient INELIGIBLE edit (e.g. an override typed then
        // deleted) — a later eligible edit can still reuse the unchanged systems. So we
        // RETAIN _systemCache rather than dropping it; we simply do not CONSULT it while
        // ineligible (its content keys are not refreshed this edit), by passing null to
        // the layout engine. cacheForEdit is the cache to use for THIS edit only.
        SystemLayoutCache? cacheForEdit = null;
        if (reuseEligible)
        {
            _systemCache ??= new SystemLayoutCache();
            _systemCache.SetContentKeys(contentKeys);
            cacheForEdit = _systemCache;
        }

        // ⒟⁗ (HANDOFF §1 ▶): when every per-measure content key matches the PREVIOUS edit's,
        // the spring vector is the same function of the same inputs — reuse it instead of
        // rebuilding every measure's springs on a keystroke that changed nothing (the
        // content-unchanged regime's single largest term, 336 ms of a 717 ms plain1k
        // keystroke, measured session 135/136). The implication (keys equal => springs
        // equal) is the same one whole-layout reuse below already stands on: the key folds
        // every staff's voices, side-tables and entry context, and the common shortest
        // duration is a function of those same measures.
        // ⚠️ THIS REMOVES A SAFETY NET, deliberately (the ticket requires saying so): the
        // spring rebuild-and-compare used to be an independent SECOND OPINION on the content
        // key's completeness — a spring input missing from the key would fail the gate
        // comparison and force a recompute. On a content-unchanged edit that opinion is now
        // silent — and since the ⒟⁗ second slice (the per-measure memo in the else branch
        // below) it is silent for the UNCHANGED measures of a content-changing edit too;
        // what stands guard instead is the incremental==full net
        // (IncrementalCompilerTests, incl. the beamed multi-system chained-edit net) plus
        // the memo==full deep-compare nets beside it.
        bool sameContentAsLastEdit = allowSkip
            && !contentKeys.IsDefault && !_contentKeys.IsDefault
            && contentKeys.AsSpan().SequenceEqual(_contentKeys.AsSpan());
        MeasureSpringData[] springs;
        if (sameContentAsLastEdit && _springs != null)
        {
            springs = _springs;
            LastSpringMemo = (0, 0);
        }
        else
        {
            // ⒟⁗ second slice (HANDOFF §1 ▶): on a CONTENT-CHANGING edit (regimes ⑵⑶),
            // rebuild only the measures whose content-key NEIGHBOURHOOD moved and reuse the
            // rest of the previous vector per measure. Springs[i] reads measure i (all
            // staves/voices/side-tables/entry context — folded into key i), the previous
            // measure's end bar line (SpacingRules.RunLeftBoundBarline), the next measure's
            // run membership (MmrRunMap.ForbidsBreakAfter) and the score-global shortest
            // duration — nothing else (inventoried session 150); so keys i−1..i+1 unchanged
            // (index-aligned) plus an unchanged shortest make springs[i] the same function
            // of the same inputs. Reuse is index-aligned on purpose: an inserted/deleted
            // measure shifts every later key and correctly rebuilds the tail, and the
            // index-dependent inputs (isFirstSystem == 0, startMeasureIndex) stay valid.
            double shortest = SpacingRules.CalculateCommonShortestDuration(score);
            int reusedCount = 0, recomputedCount = 0;
            Func<int, MeasureSpringData?>? memo = null;
            if (allowSkip && _springs != null && _shortest == shortest
                && !contentKeys.IsDefault && !_contentKeys.IsDefault)
            {
                var prevSprings = _springs;
                var prevKeys = _contentKeys;
                var newKeys = contentKeys;
                memo = i =>
                {
                    if (SpringReusable(i, newKeys, prevKeys))
                    {
                        reusedCount++;
                        return prevSprings[i];
                    }
                    recomputedCount++;
                    return null;
                };
            }
            springs = SystemBreaker.ComputeMultiStaffSpringData(score, shortest, memo);
            _shortest = shortest;
            LastSpringMemo = (reusedCount, recomputedCount);
        }
        // The break gate's OWN key model — the union of the signatures the staves engrave
        // (SpacingRules.WidestActiveKeyInk), which is what SystemBreaker now prices a line
        // start from. This pair is a CHANGE DETECTOR, not the gate's number (it carries no
        // indent), so what matters is that it reads the same INPUTS: reading score.KeySignature
        // here left an edit that changed only a transposed part's own signature — and so
        // changed the gate — looking unchanged to the skip.
        double maxClefWidth = SpacingRules.MaxClefWidth(score);
        double firstPrefix = SystemBreaker.GateFirstPrefixWidth(score, maxClefWidth);
        double contPrefix = SystemBreaker.GateContinuationPrefixWidth(score, maxClefWidth);

        bool skip = allowSkip
            && _lineSizes != null
            && _springs != null
            && firstPrefix == _firstPrefix
            && contPrefix == _contPrefix
            // Reference-equal when the content-unchanged reuse above fired; the
            // element-wise comparison is the real gate on every content-changing edit.
            && (ReferenceEquals(springs, _springs) || springs.AsSpan().SequenceEqual(_springs));

        // F3/B-2: whole-layout reuse. When the line-break gate is unchanged AND every
        // per-measure content key matches AND the score-global layout inputs match AND
        // the cached layout carries no UNMIGRATED data-pos annotation (ReuseSafe), the
        // geometry is provably position-independent and identical, so the entire cached
        // ScoreLayout is reused and LayoutEngine.Layout is skipped outright. The renderer
        // re-derives each migrated annotation's data-pos from the edited score, so the
        // output is byte-identical to a full recompile. Gated to the override-free path
        // (overrides spread spacing globally and are not localized by the per-measure key).
        // SwingSubdivision joins the score-global key: the synthesized tempo/swing
        // mark (MusicMarkEngraver.BuildAllMarks) is not in the side-tables the content
        // key buckets, so a swing toggle at an unchanged BPM must be caught here.
        var globalKey = (score.Title, score.Composer, score.Tempo, score.SwingSubdivision,
            score.TempoText, score.TempoBeatUnit, score.TempoDots);
        bool reuse = skip
            && reuseEligible
            && _cachedLayout != null
            && sameContentAsLastEdit
            && globalKey == _globalKey
            && ReuseSafe(_cachedLayout);

        ScoreLayout layout;
        if (reuse)
        {
            layout = _cachedLayout!;
        }
        else
        {
            // ...and the spring vector this method just built for the gate goes WITH it: when
            // the gate moved there is no cached line-size answer, so the breaker runs its DP
            // and used to rebuild the identical vector to feed it. One quantity, two places
            // (HANDOFF §2 A) — in its perf clothing.
            layout = new LayoutEngine().Layout(score, skip ? _lineSizes : null, cacheForEdit, springs);
            // Reuse the prior line sizes on a gate-skip (still the correct solution);
            // otherwise capture the fresh ones.
            if (!skip)
                _lineSizes = layout.AllSystems.Select(s => s.Measures.Length).ToArray();
        }

        _springs = springs;
        _firstPrefix = firstPrefix;
        _contPrefix = contPrefix;
        _contentKeys = contentKeys;
        _globalKey = globalKey;
        _cachedLayout = layout;
        _tree = tree;
        LastEditSkippedLineBreak = skip;
        LastEditReusedLayout = reuse;

        // Only a reused layout carries stale (pre-edit) data-pos that the renderer must
        // re-derive from the live score; a freshly laid-out layout already has it right.
        return SvgGenerator.RenderToSvg(score, layout, _options, resolveDataPos: reuse);
    }

    /// <summary>
    /// Whether measure <paramref name="i"/>'s springs from the previous edit's vector are
    /// provably identical to what a from-scratch build would produce — the ⒟⁗ per-measure
    /// memo's key test. Index-aligned: keys i−1, i and i+1 must all be unchanged.
    /// </summary>
    /// <remarks>
    /// The neighbourhood window is not caution, it is the inventory (session 150 — every
    /// read of SystemBreaker.ComputeMultiStaffSpringData's loop body traced to its fold):
    /// <list type="bullet">
    /// <item>LEFT (i−1): a multi-measure-rest run OPENING at i whose measure declares no
    /// start bar line reads the previous measure's <c>EndBarline</c> for the run rod
    /// (<see cref="SpacingRules.RunLeftBoundBarline"/>) — that field is folded into key
    /// i−1, not key i.</item>
    /// <item>RIGHT (i+1): <see cref="MmrRunMap.ForbidsBreakAfter"/> asks whether a break
    /// after i would split a run, i.e. whether i+1 belongs to the SAME run — visible in
    /// key i+1 (its content/interior-ness), not in key i. (The next measure's opening
    /// clef is NOT why: that allowance is already folded into key i.) The last-measure
    /// case pins the window's edge: i+1 must exist in both vectors or in neither.</item>
    /// <item>Everything else springs[i] reads is folded into key i itself (all
    /// staves/voices at i, side-tables, entry context, run membership/count, the boundary
    /// clef allowance), is score-global-and-folded-into-every-key (staff structure, clefs,
    /// lead-sheet-ness), or is the shortest duration, which the caller compares
    /// separately.</item>
    /// </list>
    /// </remarks>
    private static bool SpringReusable(
        int i, ImmutableArray<MeasureContentKey> newKeys, ImmutableArray<MeasureContentKey> prevKeys)
    {
        if (i >= prevKeys.Length || newKeys[i] != prevKeys[i])
            return false;
        if (i > 0 && newKeys[i - 1] != prevKeys[i - 1])
            return false;
        bool hasRight = i + 1 < newKeys.Length;
        bool hadRight = i + 1 < prevKeys.Length;
        if (hasRight != hadRight)
            return false;
        return !hasRight || newKeys[i + 1] == prevKeys[i + 1];
    }

    /// <summary>
    /// Collects <paramref name="tree"/> the way <see cref="SvgGenerator.CollectScore(SyntaxTree, RenderSpec?)"/>
    /// does, resuming the collect walks from the recorded baseline when the edit
    /// window allows it (see <see cref="CollectResumePlanner"/>). Any resume path —
    /// planned or bailed — produces a score identical to a full collect of
    /// <paramref name="tree"/>; the CollectResumeTests / CollectEditResumeTests
    /// completeness nets and this class's incremental==full net stand on that.
    /// </summary>
    private MultiStaffScore CollectWithResume(SyntaxTree tree, RenderSpec? spec, bool allowResume)
    {
        if (allowResume && _collectRecording != null && _collectSource != null
            && _collectBaselineTree != null)
        {
            var resumer = CollectResumePlanner.Plan(
                _collectBaselineTree, tree, _collectRecording, _collectSource);
            if (resumer != null)
            {
                try
                {
                    var collector = new MeasureCollector
                    {
                        ScoreTranspose = spec?.ScoreTranspose,
                        WalkProbe = resumer,
                    };
                    var resumed = SvgGenerator.CollectScore(collector, tree, spec);
                    int walks = 0, adopted = 0, splicedWalks = 0, spliced = 0;
                    foreach (var plan in resumer.ResumePlans.Values)
                    {
                        if (plan.Consumed)
                        {
                            walks++;
                            adopted += plan.Checkpoint!.MeasureCount;
                        }
                        if (plan.SplicedMeasures > 0)
                        {
                            splicedWalks++;
                            spliced += plan.SplicedMeasures;
                        }
                    }
                    LastCollectResume = (walks, adopted, splicedWalks, spliced);
                    return resumed;
                }
                catch (CollectResumeAbortException)
                {
                    // Structural drift the plan-time guards could not see. The
                    // half-collected state is discarded; fall through to the full
                    // collect below, which also re-records the baseline.
                }
            }
        }

        LastCollectResume = (0, 0, 0, 0);
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector
        {
            ScoreTranspose = spec?.ScoreTranspose,
            WalkProbe = recorder,
        };
        var score = SvgGenerator.CollectScore(source, tree, spec);
        _collectSource = source;
        _collectRecording = recorder;
        _collectBaselineTree = tree;
        return score;
    }

    // F3/B-2: a cached layout is safe to reuse only if it carries no annotation whose
    // data-pos is still BAKED into the layout (i.e. not yet migrated onto the render-time
    // SourceIndex / note-locator resolution). Such a value would go stale on reuse since a
    // content-unchanged edit shifts source offsets. Every data-pos-emitting annotation is
    // migrated and re-resolved from the live score by SharedRenderer.ResolveDataPos, so
    // there is nothing left to decline for — the guard is the migration itself.
    //
    // ⚠️ IT USED TO EXCLUDE ANY SCORE WITH A PEDAL BRACKET, under a comment asserting the
    // array was "always empty today (pedals render as text marks, never a bracket layout)".
    // That was false: Staff.PedalStyle defaults to Bracket, so every `@ped` in the corpus
    // produced one. The cost was invisible because the only thing that measures it is
    // LilySharp.Benchmarks' IncrementalSessionBenchmark, which asserts reuse fires and had
    // been throwing on its multi-staff fixture (showcase/03-piano, a piano score with
    // pedals) since before 2026-08-04 — a failing benchmark is not a failing test, and
    // nothing in the suite ran it. MEASURED after the migration: that assertion passes.
    // ★ The lesson is the comment, not the code: "always empty today" was a claim about the
    // corpus that nobody had asked the corpus about.
    private static bool ReuseSafe(ScoreLayout layout) => true;
}
