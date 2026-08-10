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

    /// <summary>Whether the most recent <see cref="Edit"/> reused the cached
    /// break solution (true) or recomputed it (false). For diagnostics / tests.</summary>
    public bool LastEditSkippedLineBreak { get; private set; }

    /// <summary>Whether the most recent <see cref="Edit"/> reused the ENTIRE cached
    /// ScoreLayout (skipping <see cref="LayoutEngine"/>.Layout outright). Implies
    /// <see cref="LastEditSkippedLineBreak"/>. For diagnostics / tests.</summary>
    public bool LastEditReusedLayout { get; private set; }

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
        var score = SvgGenerator.CollectScore(tree, spec);

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

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);
        var springs = SystemBreaker.ComputeMultiStaffSpringData(score, shortest);
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
            && springs.AsSpan().SequenceEqual(_springs);

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
            && !_contentKeys.IsDefault
            && globalKey == _globalKey
            && contentKeys.AsSpan().SequenceEqual(_contentKeys.AsSpan())
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
