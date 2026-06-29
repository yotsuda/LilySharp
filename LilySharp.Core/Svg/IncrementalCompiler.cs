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
/// (LSP_F3_QUERY_GRAPH_DESIGN.md §4).
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

    /// <summary>Whether the most recent <see cref="Edit"/> reused the cached
    /// break solution (true) or recomputed it (false). For diagnostics / tests.</summary>
    public bool LastEditSkippedLineBreak { get; private set; }

    /// <summary>The current syntax tree (after the last edit).</summary>
    public SyntaxTree Tree => _tree;

    /// <summary>Test/diagnostic access to the per-system layout cache (null unless the
    /// single-staff, override-free path installed one). Lets tests assert that
    /// unchanged systems are reused rather than recomputed.</summary>
    internal SystemLayoutCache? SystemCache => _systemCache;

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

    private string Compile(SyntaxTree tree, bool allowSkip)
    {
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec, out var singleStaffScore);

        // F3/S5-3a: install/refresh the per-system layout cache for single-staff
        // scores without grob overrides. Grob overrides can change spacing globally
        // (so a per-measure key cannot localize them), and multi-staff systems couple
        // all staves' columns (the primary-voice keys would not capture them) — both
        // fall back to full layout (cache == null), which is byte-identical.
        if (singleStaffScore != null
            && singleStaffScore.GrobOverrides.IsDefaultOrEmpty
            && singleStaffScore.GrobReverts.IsDefaultOrEmpty)
        {
            _systemCache ??= new SystemLayoutCache();
            _systemCache.SetContentKeys(MeasureContentKey.Compute(singleStaffScore));
        }
        else
        {
            _systemCache = null;
        }

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);
        var springs = SystemBreaker.ComputeMultiStaffSpringData(score, shortest);
        double firstPrefix = SpacingRules.CalculatePrefixWidth(
            score.KeySignature.Sharps, includeTimeSignature: true,
            score.TimeSignature.Beats, score.TimeSignature.BeatType);
        double contPrefix = SpacingRules.CalculatePrefixWidth(
            score.KeySignature.Sharps, includeTimeSignature: false);

        bool skip = allowSkip
            && _lineSizes != null
            && _springs != null
            && firstPrefix == _firstPrefix
            && contPrefix == _contPrefix
            && springs.AsSpan().SequenceEqual(_springs);

        var layout = new LayoutEngine().Layout(score, skip ? _lineSizes : null, _systemCache);

        // Cache: reuse the prior line sizes on a skip (the gate is unchanged so
        // they are still the correct solution); otherwise capture the fresh ones.
        if (!skip)
            _lineSizes = layout.AllSystems.Select(s => s.Measures.Length).ToArray();
        _springs = springs;
        _firstPrefix = firstPrefix;
        _contPrefix = contPrefix;
        _tree = tree;
        LastEditSkippedLineBreak = skip;

        return SvgGenerator.RenderToSvg(score, layout, _options);
    }
}
