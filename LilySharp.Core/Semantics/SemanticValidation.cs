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

using System;
using System.Collections.Generic;
using LilySharp.Core.Svg.Collector;
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
    /// <summary>Runs the validator over the parsed tree, collecting diagnostics.</summary>
    void Validate(SyntaxTree tree);

    /// <summary>The diagnostics found by the most recent <see cref="Validate"/> call.</summary>
    IReadOnlyList<Diagnostic> Diagnostics { get; }
}

/// <summary>
/// A validator whose diagnostics come from warnings recorded as a side effect of a
/// single-staff <see cref="MeasureCollector.Collect"/>. <see cref="SemanticValidation.Run"/>
/// supplies ONE shared, lazily-computed collector so several such validators don't
/// each re-walk the whole score.
/// </summary>
internal interface ISharedCollectValidator : ISemanticValidator
{
    /// <summary>Emit diagnostics from the shared collector (null if it failed).</summary>
    void ValidateWith(Lazy<MeasureCollector?> sharedCollect);
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
        new EmptyMeasureValidator(),        // a bare `|` gap placeholder measure
        new DurationValidator(),            // invalid note values (5, 3, 6, …)
        new AnnotationNameValidator(),      // unknown @annotation names
        new SymbolCaseValidator(),          // wrong-case / unknown header symbols
        new FormDeclarationValidator(),// at most one structure per scope
        new LyricSyllableValidator(),       // more syllables than notes
        new LyricTrackSectionValidator(),   // part-major lyrics track must use sections
        new LyricUnattachedValidator(),     // a lyrics block no score attaches / places
        new LyricPlainVerseShadowedValidator(), // a plain verse fully shadowed by [N.] verses
        new NavigationPlacementValidator(), // a nav mark placed mid-measure
        new TabTieStringValidator(),        // a tie naming two tab strings
        new TieTargetValidator(),           // a tie whose next note is not the tied pitch
        new TabRangeValidator(),            // notes clamped outside the tab range
        new DuplicateScoreNameValidator(),  // two score blocks with the same name
        new DuplicateCellValidator(),       // a (section × part) cell filled twice
        new DuplicateTrackSectionValidator(),// a chords/lyrics track names a section twice
        new RevertContextValidator(),       // a revert/once outside a music stream
        new PartialScopeValidator(),        // a partial (pickup) outside a section
        new SectionMusicNeedsPartValidator(), // loose music in a part-major top-level section
        new ScoreSettingInPartHeaderValidator(), // tempo/time as a part header property
        new DuplicateGlobalSettingValidator(), // a top-level tempo/time/key/title/… written twice
        new FontEmbedWarningValidator(),    // an embedded font with a restricted/unverified license
    };

    /// <summary>
    /// Runs every semantic validator and returns the combined diagnostics. Does NOT
    /// include the parser's own <c>tree.Diagnostics</c> — callers prepend those if
    /// they want them (the LSP converts the two sets separately).
    /// </summary>
    public static IReadOnlyList<Diagnostic> Run(SyntaxTree tree)
    {
        var result = new List<Diagnostic>();
        // One shared single-staff collect, computed at most once and reused by every
        // collector-backed validator (previously each re-ran the full collector).
        var sharedCollect = new Lazy<MeasureCollector?>(() => TryCollect(tree));
        foreach (var v in CreateAll())
        {
            if (v is ISharedCollectValidator sc)
                sc.ValidateWith(sharedCollect);
            else
                v.Validate(tree);
            result.AddRange(v.Diagnostics);
        }
        return result;
    }

    /// <summary>
    /// Runs a single-staff <see cref="MeasureCollector.Collect"/>, swallowing collector
    /// failures (a malformed score surfaces its real error through the parser / other
    /// validators). Returns null on failure.
    /// </summary>
    internal static MeasureCollector? TryCollect(SyntaxTree tree)
    {
        try
        {
            // Collect the way the render path does (honoring `staff X with lyrics NAME`),
            // so a validator reading back the collector's recorded warnings sees exactly
            // what renders. A scoreless file has no RenderSpec and falls through to the
            // single-staff collect with no attach — nothing is auto-attached.
            var renderSpec = RenderSpecParser.FindFirst(tree);
            var collector = new MeasureCollector { ScoreTranspose = renderSpec?.ScoreTranspose };
            Svg.SvgGenerator.CollectScore(collector, tree, renderSpec);
            return collector;
        }
        catch
        {
            return null;
        }
    }
}
