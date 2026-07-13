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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns when a navigation mark (segno / coda / fine / D.S. / D.C. / to coda) inside a
/// section's music sits MID-measure instead of at a barline boundary. These are
/// standalone landmarks that a reader expects above a barline; written after a beat or
/// two of an incomplete measure they read as a mistake. Placement at the start of a
/// measure (after a barline) or right before its barline (a full measure) is fine.
/// </summary>
/// <remarks>
/// Whether a mark landed at a boundary depends on the exact measure fill, which
/// <see cref="MeasureCollector"/> already tracks. This reads back the placements it
/// recorded as a side effect (<see cref="MeasureCollector.NavigationPlacementWarnings"/>).
/// </remarks>
internal sealed class NavigationPlacementValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        // Navigation marks live in a part's music, and the exact measure fill is only
        // walked with a voice bound — the shared no-voice collect skips part-major
        // music. Collect the primary part so its placements are recorded.
        var root = tree.GetRoot();
        // The first declared part names the primary voice (present in both part-major
        // `part m { section … }` and section-major `part m { }` + `section A { m { … } }`).
        string? voice = root.DescendantNodes().OfType<PartDeclarationSyntax>().FirstOrDefault()?.Name.Text;

        Svg.Collector.MeasureCollector collector;
        try
        {
            collector = new Svg.Collector.MeasureCollector();
            collector.Collect(tree, voice);
        }
        catch
        {
            return; // a malformed score surfaces its real error elsewhere
        }

        foreach (var w in collector.NavigationPlacementWarnings)
            _diagnostics.Warning(new TextSpan(w.SourcePosition, 1), DiagnosticCodes.NavigationMarkMidMeasure,
                $"the {w.MarkText} navigation mark sits mid-measure; place it at a barline boundary.");
    }
}
