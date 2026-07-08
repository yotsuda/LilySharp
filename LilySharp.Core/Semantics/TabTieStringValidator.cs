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
/// Warns when the two notes of a tie carry DIFFERENT explicit tab string numbers
/// (<c>\N</c>). A tie holds one string, so the held note cannot move to another —
/// the conflicting <c>\N</c> is almost always an authoring slip. (When only the
/// destination names a string the source silently adopts it, which is not a
/// conflict and is not reported.)
/// </summary>
/// <remarks>
/// Resolving ties across sections, repeats and bar lines is exactly what
/// <see cref="MeasureCollector"/> already does for rendering, so — like
/// <see cref="LyricSyllableValidator"/> — this validator runs the collector and
/// reads back the conflicts it recorded as a side effect.
/// </remarks>
internal sealed class TabTieStringValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(new System.Lazy<Svg.Collector.MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(System.Lazy<Svg.Collector.MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.TabTieWarnings;
        if (warnings == null)
            return;

        foreach (var w in warnings)
            _diagnostics.Warning(new TextSpan(w.SourcePosition, 1),
                DiagnosticCodes.TabTieStringConflict,
                $"tied notes name different strings ({w.PreviousString} then {w.FollowingString}); " +
                "a tie holds one string, so the first note's string is kept");
    }
}
