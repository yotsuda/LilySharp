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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// A semantic validator: runs over a parsed tree and exposes the diagnostics it
/// found. Every validator has the same shape, so a single registry can drive them
/// all (previously the CLI and the LSP each hand-listed the same set — and drifted,
/// e.g. DurationValidator was registered in only one of them).
/// </summary>
public interface ISemanticValidator
{
    void Validate(SyntaxTree tree);
    IReadOnlyList<Diagnostic> Diagnostics { get; }
}

/// <summary>
/// The single source of truth for which semantic validators run, and a helper to
/// run them all. Both the CLI's <c>check</c> and the LSP's live diagnostics call
/// <see cref="Run"/> so they can never diverge.
/// </summary>
public static class SemanticValidation
{
    /// <summary>Creates one fresh instance of every semantic validator, in run order.</summary>
    public static IReadOnlyList<ISemanticValidator> CreateAll() => new ISemanticValidator[]
    {
        new SymbolReferenceValidator(),     // undefined variable / phrase / section
        new MeasureValidator(),             // measure fullness / cross-part length
        new DurationValidator(),            // invalid note values (5, 3, 6, …)
        new AnnotationNameValidator(),      // unknown @annotation names
        new StructureDeclarationValidator(),// at most one structure per scope
        new LyricSyllableValidator(),       // more syllables than notes
        new TabTieStringValidator(),        // a tie naming two tab strings
        new TabRangeValidator(),            // notes clamped outside the tab range
        new DuplicateScoreNameValidator(),  // two score blocks with the same name
        new DuplicateCellValidator(),       // a (section × part) cell filled twice
    };

    /// <summary>
    /// Runs every semantic validator and returns the combined diagnostics. Does NOT
    /// include the parser's own <c>tree.Diagnostics</c> — callers prepend those if
    /// they want them (the LSP converts the two sets separately).
    /// </summary>
    public static IReadOnlyList<Diagnostic> Run(SyntaxTree tree)
    {
        var result = new List<Diagnostic>();
        foreach (var v in CreateAll())
        {
            v.Validate(tree);
            result.AddRange(v.Diagnostics);
        }
        return result;
    }
}
