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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns for hits in a <c>drums { }</c> chart no drummer can play at once (LYS4030): its lines
/// are written apart and played together, so two patterns can meet on a beat that asks for
/// three hands. Only the UNPLAYABLE meeting is reported (owner's rule 2026-10-11) — a hi-hat
/// and a snare on one beat is the chart working.
/// </summary>
/// <remarks>
/// The timing is the collector's (<see cref="MeasureCollector.DrumChartCollisions"/>): a line
/// is phrases, tuplets and repeats, and only a finished collect knows the phrases.
/// </remarks>
internal sealed class DrumChartValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        var charts = tree.GetRoot().DescendantNodes<ParallelExpressionSyntax>().Where(p => p.IsDrums).ToList();
        if (charts.Count == 0 || sharedCollect.Value is not { } collector)
            return;
        var said = new HashSet<(int, string)>();
        foreach (var chart in charts)
            foreach (var (at, problem) in collector.DrumChartCollisions(chart))
                // A hit in a phrase two charts share is named once.
                if (said.Add((at.SourceStart, problem)))
                    _diagnostics.Warning(new TextSpan(at.SourceStart, Math.Max(1, at.Span.Length)),
                        DiagnosticCodes.DrumsUnplayable,
                        $"no drummer can play this: {problem}. The lines of a drums {{ }} chart "
                        + "play together - move a hit, or rest one line here");
    }
}
