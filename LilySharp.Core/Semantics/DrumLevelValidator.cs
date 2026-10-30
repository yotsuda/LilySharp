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
/// Warns for two different levels written in a drum staff's voices at one moment (LYS4033):
/// <c>@f</c> on the hands' line and <c>@p</c> on the feet's — one drummer plays one level
/// (owner's decision 2026-10-30). The same level in both lines is the drummer's one level and
/// prints once; two different ones both print, and this says which to keep.
/// </summary>
/// <remarks>
/// The moments are the collector's (<see cref="MeasureCollector.DrumLevelConflicts"/>): a chart's
/// lines, a <c>voice { } { }</c> span, and their phrases are only timed by a finished collect.
/// </remarks>
internal sealed class DrumLevelValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // Only a book with a drum part can say this: no collect is asked of any other.
        if (!tree.GetRoot().DescendantNodes<ParallelExpressionSyntax>().Any(p => p.IsDrums)
            && !System.Text.RegularExpressions.Regex.IsMatch(tree.GetRoot().ToFullString(), @"\bclef\s+percussion\b"))
            return;
        if (sharedCollect.Value is not { } collector)
            return;
        var said = new HashSet<int>();
        foreach (var (at, other) in collector.DrumLevelConflicts())
            // A level in a phrase played twice is named once.
            if (said.Add(at.SourcePosition))
                // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
                _diagnostics.Warning(new TextSpan(at.SourcePosition, 1 + at.Text.Length),
                    DiagnosticCodes.DrumLevelsConflict,
                    $"the drums play @{at.Text} here and @{other} in another line at the same moment - "
                    + "one drummer plays one level: write the same level in each line, or in one line alone");
    }
}
