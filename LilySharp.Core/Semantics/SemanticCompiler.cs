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

using LilySharp.Core.Semantics.Binding;
using LilySharp.Core.Semantics.BoundTree;
using LilySharp.Core.Semantics.Symbols;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Compiles a syntax tree through the full semantic pipeline.
/// </summary>
/// <remarks>
/// Pipeline: SyntaxTree → SymbolCollector → Binder → ScoreBuilder → Score
///
/// STATUS: NOT YET WIRED INTO ANY OUTPUT PIPELINE. As of this review nothing
/// outside <c>Semantics/</c> and tests consumes this compiler or its
/// <c>BoundScore</c>. The live render path uses <c>MeasureCollector</c>
/// (Svg/Collector) instead, which re-walks the syntax tree independently, and
/// MIDI/MusicXML export walk the syntax tree directly. This is the intended
/// "Roslyn-style" semantic layer (a stated design goal) built ahead of being
/// integrated — keep, but treat its tests as not covering any shipping path
/// until a generator is switched onto it. See LILYSHARP_STANDALONE_REVIEW.md §1.
/// </remarks>
public sealed class SemanticCompiler
{
    /// <summary>
    /// Compiles a syntax tree to a Score.
    /// </summary>
    /// <param name="tree">The syntax tree to compile.</param>
    /// <returns>The compilation result containing the score and diagnostics.</returns>
    public CompilationResult Compile(SyntaxTree tree)
    {
        // Phase 1: Symbol Collection
        var collector = new SymbolCollector();
        var symbolResult = collector.Collect(tree);

        if (!symbolResult.Success)
        {
            return new CompilationResult(
                null,
                symbolResult.Diagnostics,
                symbolResult.Symbols);
        }

        // Phase 2: Binding
        var binder = new Binder();
        var boundScore = binder.Bind(tree, symbolResult.Symbols);

        if (!boundScore.Success)
        {
            return new CompilationResult(
                null,
                boundScore.Diagnostics,
                symbolResult.Symbols);
        }

        // Phase 3: Score Building
        var clef = boundScore.Metadata.Clef;
        var builder = new ScoreBuilder(clef);
        var score = builder.Build(boundScore);

        return new CompilationResult(
            score,
            boundScore.Diagnostics,
            symbolResult.Symbols);
    }

    /// <summary>
    /// Compiles source code to a Score.
    /// </summary>
    public CompilationResult Compile(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return Compile(tree);
    }
}

/// <summary>
/// Result of semantic compilation.
/// </summary>
public sealed record CompilationResult(
    Score? Score,
    System.Collections.Immutable.ImmutableArray<SemanticDiagnostic> Diagnostics,
    ImmutableSymbolTable Symbols)
{
    /// <summary>
    /// Whether compilation succeeded without errors.
    /// </summary>
    public bool Success => Score != null && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
