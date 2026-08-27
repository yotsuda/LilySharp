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
/// ONE SPEC PER SESSION: a session renders the score its construction-time
/// <c>renderName</c> selects — the same resolution as
/// <see cref="SvgGenerator.Generate(SyntaxTree, SvgRenderOptions, string)"/>
/// (<see cref="RenderSpecParser.Choose"/>: by name / output file / stem, falling
/// back to the first block), or the first/default score when no name was given.
/// The LSP keys its sessions by (document, render name) accordingly (2026-08-26
/// review, finding 3-1 — a named preview used to bypass the session entirely).
/// Every reuse key below is computed from the COLLECTED score, so it is spec-correct
/// by construction as long as resolution keeps picking the same block; the one thing
/// no key can see — resolution itself drifting to a DIFFERENT block (one inserted
/// above, renamed into or out of the match) — is caught by the spec-identity guard
/// in <see cref="Compile"/>, which then sheds the whole session cold.
/// </para>
/// </remarks>
public sealed class IncrementalCompiler
{
    private readonly SvgRenderOptions _options;
    // Which score this session renders (null/empty = the first/default block) —
    // fixed at construction; a different selection is a different session.
    private readonly string? _renderName;
    private SyntaxTree _tree;

    // The (Name, OutputFile) sequence of the tree's parseable render blocks at the
    // last compile — the complete input of WHICH block Choose resolves, for a fixed
    // _renderName. While it is unchanged, resolution picks the same ordinal block,
    // and every cache below is keyed on that block's collected score. When it moves
    // (a render block inserted, deleted, renamed, its output file changed), the
    // resolved block may be a DIFFERENT one whose score could even collect to the
    // same content keys (identical music in another part — the keys are blind to
    // source offsets by design, so fragment data-pos would replay the other part's
    // offsets; and the collect baseline was recorded under the other spec's
    // transpose, which no walk-entry validation checks). No per-measure or global
    // key can catch that, so the guard in Compile sheds the session cold instead.
    // Over-approximate on purpose: a rename that does NOT move resolution also
    // sheds — one cold compile, never a wrong one. Null until the first compile.
    private string? _specIdentity;

    // The font plan the cached geometry below was measured with. Metrics are an input
    // to every layout stage but to none of the reuse keys, so a plan change sheds the
    // lot (see the guard in Compile). Null until the first compile.
    private Rendering.TextFontPlan? _fontPlan;

    // The paper (page dimensions) the cached geometry was laid out on — the SECOND
    // score-global input outside every reuse key, guarded the same way (see Compile).
    // Null until the first compile.
    private Layout.LayoutOptions? _paper;

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

    // ⒭ first slice: persists across edits so unchanged systems replay their
    // recorded SVG text instead of re-drawing (SvgSystemFragmentCache — key
    // inventory, data-pos re-resolution and decline classes in its remarks).
    // Retained across ineligible edits like _systemCache; BeginPass's generation
    // keeps entries from replaying across a pass that did not refresh them.
    private Rendering.Svg.SvgSystemFragmentCache? _fragments;

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

    // The override/revert collections the cached geometry was laid out with, compared
    // BY VALUE (GrobOverride/GrobRevert are records over scalars and the typed LysValue).
    // The per-measure content key buckets the overrides that carry a measure, but an
    // override does not have to (BucketSingle drops mi < 0), and its effect is global
    // anyway — so whole-vector work (the spring reuse, whole-layout reuse) additionally
    // requires these collections unchanged. Per-SYSTEM reuse stays override-free-only:
    // a per-measure key cannot LOCALIZE a global spacing change, but totality can carry
    // it — when every input is unchanged, the whole answer is (2026-08-26 review,
    // finding 3-2, first stage).
    private ImmutableArray<GrobOverride> _overrides;
    private ImmutableArray<GrobRevert> _reverts;

    // F3/S5-4 (⒭ ⑵, second slice — prefix side): the collect walk's checkpoint
    // recording from the last FULL collect, kept as the resume baseline. An edit
    // resumes each walk from its last checkpoint that reads only the old/new
    // texts' common prefix (CollectResumePlanner), so the walk skips the measures
    // before the edit instead of re-collecting the whole book per keystroke.
    // Resumed collects do NOT re-record: the baseline stays pinned to
    // _collectBaselineTree's text until a full collect refreshes it (first
    // compile, an unplannable edit, a mid-collect bail — or a re-record this
    // session SCHEDULES itself, below).
    private MeasureCollector? _collectSource;
    private CollectWalkProbe? _collectRecording;
    private SyntaxTree? _collectBaselineTree;

    // Baseline re-record heuristic (2026-08-26 review, finding 3-3). The dirty
    // window is computed against the LAST FULL COLLECT's text, so it is the UNION
    // of every edit since then: edit measure 10, then measure 900, and every later
    // keystroke's window spans 10..900 — the prefix checkpoint stops at 10, the
    // splice declines (tail overlaps the window), and the walk runs essentially
    // full per keystroke, forever, while every resume still "succeeds" — so
    // nothing ever refreshes the baseline. The fix: when a resume's ADOPTION
    // (prefix-adopted + suffix-spliced measures, over the measures the baseline
    // could offer) falls below the floor, schedule a full collect for the next
    // compile — a full collect always re-records, which resets the window to the
    // current text. Hysteresis (_rerecordArmed) bounds the cost on books whose
    // adoption is INTRINSICALLY low (e.g. a whole-book parallel span edited in
    // the primary voice adopts 0 with a fresh baseline too): one re-record is
    // spent probing; the trigger re-arms only after a resume that adopted well,
    // i.e. only when re-recording demonstrably helps this document's shape.
    // Correctness is not in play — the full collect is the reference path; this
    // trades one keystroke's record overhead against unbounded window growth.
    // The floor/threshold are latency heuristics, not measurements of anything.
    private const double RerecordAdoptionFloor = 0.5;
    private const int RerecordMinResumableMeasures = 24;
    private bool _rerecordNext;
    private bool _rerecordArmed = true;

    // ⑶ beamdirs: the per-measure beam-detection memo of the collect-phase probe
    // (ResolveBeamStemDirections), persisted across edits so a keystroke re-detects only
    // the measures the edit changed. Generation-swapped per compile (BeginCollect); the
    // bake — and with it BeamId numbering — always runs live, keeping the resolved model
    // byte-identical to a memo-free collect (BeamDetector's memo remarks).
    private readonly BeamDetectionMemo _beamMemo = new();

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

    /// <summary>Whether the most recent compile scheduled a baseline re-record
    /// (its resume adopted too little of what the baseline could offer — see the
    /// re-record heuristic's remarks), so the NEXT compile will collect fully and
    /// re-record. For diagnostics / tests.</summary>
    internal bool RerecordScheduled => _rerecordNext;

    /// <summary>How the most recent spring-vector build was paid for (⒟⁗): how many
    /// measures' springs were reused from the previous edit's vector via the per-measure
    /// content-key memo, and how many were recomputed. (0, 0) when the whole vector was
    /// reused by reference (content-unchanged edit) or on a full compile.
    /// For diagnostics / tests.</summary>
    internal (int Reused, int Recomputed) LastSpringMemo { get; private set; }

    /// <summary>How the most recent collect's beam-direction probe paid for its beam
    /// DETECTION (⑶ beamdirs): how many per-measure detections were replayed from the
    /// content-key memo, and how many ran live (and were stored). Counts include
    /// within-collect duplicate measures (a book of identical bars detects one and
    /// replays the rest even on a full compile). For diagnostics / tests.</summary>
    internal (int Reused, int Recomputed) LastBeamMemo => (_beamMemo.Hits, _beamMemo.Misses);

    /// <summary>How the most recent render's per-system SVG text was paid for (⒭):
    /// how many systems replayed their recorded fragment and how many drew live
    /// (declined systems count as drawn). (0, 0) on a fragment-ineligible pass.
    /// For diagnostics / tests.</summary>
    internal (int Replayed, int Drawn) LastRenderFragments =>
        _fragments?.LastPass ?? (0, 0);

    /// <summary>Same, for the page-level OVERLAY fragments (⒭ second slice — one unit
    /// per (drawer, page) put on the fragment mechanism; fingerings today). (0, 0) on a
    /// fragment-ineligible pass and for scores without such overlays.</summary>
    internal (int Replayed, int Drawn) LastRenderOverlays =>
        _fragments?.LastOverlayPass ?? (0, 0);

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

    /// <summary>Creates an incremental compiler seeded with an initial tree.
    /// <paramref name="renderName"/> fixes which score every compile of this session
    /// renders (null/empty = the first/default block), resolved per compile with
    /// <see cref="SvgGenerator.Generate(SyntaxTree, SvgRenderOptions, string)"/>'s
    /// own policy (<see cref="RenderSpecParser.Choose"/>).</summary>
    public IncrementalCompiler(SyntaxTree tree, SvgRenderOptions? options = null,
        string? renderName = null)
    {
        _tree = tree;
        _options = options ?? SvgRenderOptions.Default;
        _renderName = renderName;
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
        var specs = RenderSpecParser.FindAll(tree);
        var spec = RenderSpecParser.Choose(specs, _renderName);

        // Spec-identity guard (_specIdentity's remarks): while the render-block
        // sequence is unchanged, Choose picks the same block it picked last compile
        // and every cache below is keyed on that block's collected score. When it
        // moved, the session may now be rendering a DIFFERENT block — shed the
        // collect baseline here and let the font/paper guard below shed every
        // geometry cache (its `_fontPlan is null` arm; one shed list, not two).
        // The beam memo needs no shed: it is addressed by the measure's RESOLVED
        // content (transpose included) and its bake always runs live.
        string specIdentity = SpecIdentity(specs);
        if (_specIdentity != null && _specIdentity != specIdentity)
        {
            _collectSource = null;
            _collectRecording = null;
            _collectBaselineTree = null;
            _rerecordNext = false;
            _rerecordArmed = true;
            _fontPlan = null;
        }
        _specIdentity = specIdentity;

        // ⑶ beamdirs: one generation per compile — the previous compile's per-measure
        // detections serve this one; anything older ages out.
        _beamMemo.BeginCollect();
        var score = CollectWithResume(tree, spec, allowResume: allowSkip);

        // A `fonts { }` edit changes the TEXT METRICS every layout stage measures with —
        // an input that lives OUTSIDE every reuse key this session keeps: the per-measure
        // content key folds the resolved model and side-tables (never the face), and the
        // global tuple below folds title/tempo/swing. So on a font-plan change EVERY
        // geometry cache here is stale at once — the spring vector, the line sizes, the
        // per-system layouts, the whole cached ScoreLayout, and the recorded SVG fragments
        // (whose replayed text carries the old face's family attribute AND its glyph
        // geometry) — and the only sound answer is to drop them and recompile as if this
        // were the first render. MEASURED before this guard existed (session 224,
        // FontEditIncrementalTests): a serif face edit re-rendered byte-identical to the
        // OLD face's layout, with only the family attribute and data-pos re-derived live —
        // stale geometry served as a hit, exactly the shape the content key cannot see.
        // The plan is compared by value (TextFontPlan.Equals folds the signature), so a
        // trivia edit near the block does not shed the caches.
        // A `paper { }` edit is the same shape with the same answer: the page dimensions
        // are an input to line breaking, justification and paging, and they live in NONE
        // of the reuse keys (not the per-measure content key, not the global tuple, not
        // the break gate — the gate caches prefix widths and springs, neither of which
        // reads the page width). MEASURED before this guard existed (2026-08-26, temp
        // probe → PaperEditIncrementalTests): a paperWidth edit on a warm session fired
        // BOTH the gate skip and whole-layout reuse and rendered 2,376,499 bytes against
        // the full recompile's 2,372,674 — the old width's layout served as a hit.
        // LayoutOptions is a record of scalars and nested records, so the value
        // comparison is sound and two collects of the SAME paper block compare equal.
        if (_fontPlan is null || !_fontPlan.Equals(score.Fonts)
            || _paper is null || !_paper.Equals(score.Paper))
        {
            _fontPlan = score.Fonts;
            _paper = score.Paper;
            _springs = null;
            _lineSizes = null;
            _shortest = null;
            _systemCache = null;
            _cachedLayout = null;
            _contentKeys = default;
            _overrides = default;
            _reverts = default;
            _fragments = null;
        }

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
        // Content keys are computed for override books too (finding 3-2 first stage):
        // they fold every per-measure input, and the override/revert collections
        // themselves are compared by value below — which is what lets the WHOLE-vector
        // reuses (springs, whole-layout) serve a book whose one override did not move,
        // instead of that book paying the cold pipeline on every keystroke. Only the
        // per-system machinery (cacheForEdit / fragments) stays override-free-gated.
        var contentKeys = MeasureContentKey.Compute(score);
        bool overridesUnchanged = !_overrides.IsDefault && !_reverts.IsDefault
            && score.GrobOverrides.AsSpan().SequenceEqual(_overrides.AsSpan())
            && score.GrobReverts.AsSpan().SequenceEqual(_reverts.AsSpan());
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
            && overridesUnchanged
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
            // overridesUnchanged joins the memo's eligibility: an override's reach is
            // global, so springs[i] may read it without key i knowing — an unchanged
            // neighbourhood is only sufficient while the override collections stand.
            if (allowSkip && _springs != null && _shortest == shortest
                && overridesUnchanged
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
        // Whole-layout reuse no longer requires override-freedom (finding 3-2, first
        // stage): it localizes nothing, so the per-measure key's inability to localize
        // an override is irrelevant — what it needs is TOTALITY, and that is exactly
        // sameContentAsLastEdit (every per-measure key equal AND the override/revert
        // collections value-equal) plus the gate, the global tuple, and the font/paper
        // session guard above. All inputs unchanged ⇒ the same layout.
        bool reuse = skip
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
            // (HANDOFF §2 A) — in its perf clothing. The shortest duration rides along for
            // the same reason (_shortest is the value `springs` was built with: set beside
            // it in the else branch, and carried when the whole vector was reused — keys
            // equal ⇒ shortest equal, the ⒟⁗ implication above).
            layout = new LayoutEngine(score.Paper).Layout(
                score, skip ? _lineSizes : null, cacheForEdit, springs, _shortest);
            // Reuse the prior line sizes on a gate-skip (still the correct solution);
            // otherwise capture the fresh ones.
            if (!skip)
                _lineSizes = layout.AllSystems.Select(s => s.Measures.Length).ToArray();
        }

        // ⒭ per-system SVG fragment memo: replay needs the window that maps the
        // PREVIOUS render's source offsets (the entries' slot values) onto this text —
        // exactly the splice's window (CollectResumePlanner.ComputeWindow), computed
        // against the tree the previous render drew (_tree, not the collect baseline:
        // fragments are refreshed EVERY render, the collect recording is not).
        string oldText = _tree.Text;
        _springs = springs;
        _firstPrefix = firstPrefix;
        _contPrefix = contPrefix;
        _contentKeys = contentKeys;
        _overrides = score.GrobOverrides;
        _reverts = score.GrobReverts;
        _globalKey = globalKey;
        _cachedLayout = layout;
        _tree = tree;
        LastEditSkippedLineBreak = skip;
        LastEditReusedLayout = reuse;

        Rendering.Svg.SvgSystemFragmentCache? fragments = null;
        if (reuseEligible)
        {
            _fragments ??= new Rendering.Svg.SvgSystemFragmentCache();
            bool windowValid = allowSkip;
            var (prefix, suffixStart, delta) = windowValid
                ? CollectResumePlanner.ComputeWindow(oldText, tree.Text)
                : (0, 0, 0);
            _fragments.BeginPass(contentKeys, windowValid, prefix, suffixStart, delta);
            fragments = _fragments;
        }
        else
        {
            // An ineligible pass renders live; bump the generation so nothing this
            // pass did not refresh can replay on the next one (slot offsets would be
            // two texts behind the window).
            _fragments?.BeginPass(default, windowValid: false, 0, 0, 0);
        }

        // EVERY session render re-derives data-pos, not only the whole-layout-reuse one.
        // ⚠️ THIS USED TO PASS `reuse`, under "a freshly laid-out layout already has it
        // right". That was false: the layout above is not built from this score alone — it
        // is built THROUGH cacheForEdit, so LayoutEngine can splice in a system (and its
        // annotation layouts, incl. the FingScriptMemo's fingerings) computed at an EARLIER
        // edit, carrying that edit's source offsets. MeasureContentKey cannot catch it: it
        // is blind to source offsets BY DESIGN — a trivia insertion must not move content —
        // so an equal key certifies the GEOMETRY and says nothing about data-pos.
        // MEASURED (session 190): after three chained keystrokes on a fingered book the
        // carried-over system's data-pos froze and drifted further with every later
        // keystroke, while the picture stayed byte-identical
        // (ChainedKeystrokes_KeepDataPosEqualToFull_WhenSystemsAreCarriedOver).
        // LILYPOND-REF (shape, NOT a port — LP has no render session, so there is no step to
        // move): `point-and-click.cc:30-36` builds every textedit:// target by reading the
        // origin off the LIVE Stream_event at output time. LP never stores a source position
        // in a layout object, which is why it cannot have this defect. Deriving at render
        // time instead of baking is the same discipline, restored here.
        // ★ Unconditional is the point, not laziness: a per-memo "did anything carry over"
        // flag would have to be raised in every memo inside SystemLayoutCache, and the next
        // memo added would silently reopen this. Correctness here does not depend on how
        // many memos exist.
        // ⚠️ The comment it replaced claimed the resolution costs "measurable allocation on
        // annotation-heavy scores". MEASURED on the heaviest book in the corpus
        // (perf-fingbeam1k: 1000 bars, 3000 fingerings): a one-note keystroke ran
        // 75.4/78.5/80.8 ms before and 74.9/81.8/84.0 ms after — inside the run-to-run
        // spread of the unpatched build. The full path (SvgGenerator.Generate) is untouched
        // and still skips it: a layout it built has no session behind it.
        return SvgGenerator.RenderToSvg(score, layout, _options, resolveDataPos: true,
            fragments);
    }

    /// <summary>
    /// Whether measure <paramref name="i"/>'s springs from the previous edit's vector are
    /// provably identical to what a from-scratch build would produce — the ⒟⁗ per-measure
    /// memo's key test. Index-aligned: keys i−1, i and i+1 must all be unchanged.
    /// </summary>
    /// <remarks>
    /// The neighbourhood window is not caution, it is the inventory (session 150 — every
    /// read of SystemBreaker.ComputeMultiStaffSpringData's loop body traced to its fold;
    /// the cross-bar lyric reads added 2026-08-20 land inside the same window):
    /// <list type="bullet">
    /// <item>LEFT (i−1): a multi-measure-rest run OPENING at i whose measure declares no
    /// start bar line reads the previous measure's <c>EndBarline</c> for the run rod
    /// (<see cref="SpacingRules.RunLeftBoundBarline"/>) — that field is folded into key
    /// i−1, not key i. And a lyric line CONTINUING from i−1 drops measure i's leading
    /// half (LyricSpacing.ReserveLyricLine) — the neighbour's lyrics are in key i−1.</item>
    /// <item>RIGHT (i+1): <see cref="MmrRunMap.ForbidsBreakAfter"/> asks whether a break
    /// after i would split a run, i.e. whether i+1 belongs to the SAME run — visible in
    /// key i+1 (its content/interior-ness), not in key i. (The next measure's opening
    /// clef is NOT why: that allowance is already folded into key i.) The last-measure
    /// case pins the window's edge: i+1 must exist in both vectors or in neither.
    /// A lyric line continuing INTO i+1 likewise drops the trailing half and prices the
    /// line-end excess. (The cross-bar PAIR quantity is deliberately NOT stored combined:
    /// each entry carries only its own half — LyricBarPricing, read off its own springs —
    /// and the breaker joins two halves at break time, precisely so no entry reads a
    /// neighbour's springs and through them a neighbour-of-neighbour's lyrics, which no
    /// 3-key window could prove.)</item>
    /// <item>Everything else springs[i] reads is folded into key i itself (all
    /// staves/voices at i, side-tables, entry context, run membership/count, the boundary
    /// clef allowance, its lyrics), is score-global-and-folded-into-every-key (staff
    /// structure, clefs, lead-sheet-ness), or is the shortest duration, which the caller
    /// compares separately.</item>
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

    /// <summary>The tree's render-block sequence folded to what <see cref="RenderSpecParser.Choose"/>
    /// resolves from: each parseable block's Name and OutputFile, in document order
    /// (a parse-failed block is invisible to Choose and to this fold alike). Each
    /// field is length-prefixed, so the fold is injective - no field content can
    /// make two different sequences fold equal.</summary>
    private static string SpecIdentity(
        System.Collections.Generic.IReadOnlyList<RenderSpec> specs)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in specs)
            sb.Append(s.Name.Length).Append(':').Append(s.Name)
              .Append(s.OutputFile.Length).Append(':').Append(s.OutputFile);
        return sb.ToString();
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
        if (allowResume && !_rerecordNext && _collectRecording != null
            && _collectSource != null && _collectBaselineTree != null)
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
                        BeamMemo = _beamMemo,
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

                    // Re-record heuristic (remarks at the fields): judge this
                    // resume's adoption against what the baseline COULD offer —
                    // the eligible recordings' measure counts, planned or not (an
                    // unplanned-but-eligible walk is reuse the stale baseline
                    // lost too). Ineligible walks (q / bare duration / form
                    // repeat) are excluded from the denominator: no re-record
                    // brings them back, so counting them would schedule
                    // re-records that cannot help.
                    int resumable = 0;
                    foreach (var rec in _collectRecording.Recordings.Values)
                    {
                        if (rec.IneligibleReason == null && rec.Checkpoints.Count > 0)
                            resumable += rec.PreFinalizeMeasures?.Count ?? 0;
                    }
                    if (resumable >= RerecordMinResumableMeasures)
                    {
                        bool adoptedWell =
                            adopted + spliced >= resumable * RerecordAdoptionFloor;
                        if (adoptedWell)
                            _rerecordArmed = true;
                        else if (_rerecordArmed)
                        {
                            _rerecordArmed = false;
                            _rerecordNext = true;
                        }
                    }
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
        _rerecordNext = false; // this IS the re-record every schedule asks for
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector
        {
            ScoreTranspose = spec?.ScoreTranspose,
            WalkProbe = recorder,
            BeamMemo = _beamMemo,
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
    // That was false: Staff.PedalStyle defaults to Bracket, so every sustain pedal in the corpus
    // produced one. The cost was invisible because the only thing that measures it is
    // LilySharp.Benchmarks' IncrementalSessionBenchmark, which asserts reuse fires and had
    // been throwing on its multi-staff fixture (showcase/03-piano, a piano score with
    // pedals) since before 2026-08-04 — a failing benchmark is not a failing test, and
    // nothing in the suite ran it. MEASURED after the migration: that assertion passes.
    // ★ The lesson is the comment, not the code: "always empty today" was a claim about the
    // corpus that nobody had asked the corpus about.
    private static bool ReuseSafe(ScoreLayout layout) => true;
}
