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
