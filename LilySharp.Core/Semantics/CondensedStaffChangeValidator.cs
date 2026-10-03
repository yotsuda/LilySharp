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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns at a <c>key</c>, <c>time</c> or <c>clef</c> change a condensed staff does not apply
/// (LYS4024): a later part of the staff wrote it at a moment where an earlier part already
/// wrote a DIFFERENT change of that kind, and one staff takes one — the first part's — as
/// LilyPond's Staff keeps the first key event of a timestep and says "discarding event" at
/// the other. An equal change is dropped in silence and reaches no one.
/// </summary>
/// <remarks>
/// ⚠️ THE LIST IS THE COLLECT'S, NOT THIS VALIDATOR'S. Which changes the staff drops is decided
/// where the staff is assembled (<see cref="MeasureCollector.JunkCondensedStaffDuplicateChanges"/>,
/// on the voices the render spec concatenates), so the page and this warning cannot disagree
/// about which change is missing. This only reads the list, as
/// <see cref="ShadowedRehearsalMarkValidator"/> reads its own.
/// </remarks>
internal sealed class CondensedStaffChangeValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // The fault lives on a condensed staff, which is an item of a SCORE — and the
        // owner's book keeps its condensed score third (`score main "tab2"`), so the first
        // score's collect (the shared one) sees none of it. Every score that condenses is
        // collected; the first one through the shared collect, as the other validators read it.
        var seen = new HashSet<(int, bool)>();
        bool first = true;
        foreach (var node in tree.GetRoot().ChildNodes())
        {
            if (node is not RenderDeclarationSyntax render)
                continue;
            var spec = RenderSpecParser.Parse(render);
            bool isFirst = first;
            first = spec == null && first;
            if (spec == null || !spec.HasCondensedStaff)
                continue;
            // A malformed score (null collector) surfaces its real error elsewhere.
            var collector = isFirst ? sharedCollect.Value : SemanticValidation.TryCollect(tree, spec);
            if (collector == null)
                continue;
            Report(collector, seen);
        }
    }

    private void Report(MeasureCollector collector, HashSet<(int, bool)> seen)
    {
        // One warning a written change and shape: a repeated section plays the same change at
        // every pass (a junked change can also open a disagreement — two shapes, two sentences).
        foreach (var w in collector.CondensedStaffChangeWarnings)
        {
            if (!seen.Add((w.SourcePosition, w.Junked)))
                continue;
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(w.SourcePosition, 1),
                DiagnosticCodes.CondensedStaffChangeConflict,
                w.Junked
                    ? $"this '{w.Kind}' of part '{w.PartName}' is not applied: part '{w.FirstPart}', "
                      + $"first on the same condensed staff, sets a different {w.Kind} at the same moment, "
                      + $"and one staff takes one {w.Kind} (LilyPond keeps the first as well) - write the "
                      + "same change in both parts, or give each part its own staff"
                    : $"from here parts '{w.FirstPart}' and '{w.PartName}' of one condensed staff are in "
                      + $"different keys ('{w.FirstPart}': {w.FirstKey}, '{w.PartName}': {w.PartKey}): the staff "
                      + "shows one key signature, and each part's accidentals are spelled for its own - write "
                      + "the same key in both parts (a section that states no key reverts to the file's), "
                      + "or give each part its own staff");
        }
    }
}
