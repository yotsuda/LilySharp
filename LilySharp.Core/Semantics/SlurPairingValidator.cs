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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns when a slur mark pairs with nothing — a <c>(</c> that is never closed, or a
/// <c>)</c> with none open. Either way <see cref="Svg.Collector.SlurDetector"/> draws no
/// slur, and until this existed nothing said so: a phrase mark could vanish from the score
/// while the file compiled clean.
/// </summary>
/// <remarks>
/// Pairing a slur across barlines, tuplets and voices is exactly what the collector's
/// render path already does, so — like <see cref="TieTargetValidator"/> — this validator
/// runs the shared collector and reads back what it recorded as a side effect
/// (<see cref="Svg.Collector.MeasureCollector.UnpairedSlurWarnings"/>), rather than
/// re-deciding the pairing and risking a warning that disagrees with what is drawn.
/// </remarks>
internal sealed class SlurPairingValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<Svg.Collector.MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<Svg.Collector.MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.UnpairedSlurWarnings;
        if (warnings == null)
            return;

        foreach (var w in warnings)
        {
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(w.SourcePosition, 1),
                DiagnosticCodes.UnpairedSlur,
                w.IsOpen
                    ? "a slur '(' is never closed, so no slur is drawn; add the matching ')' "
                      + "after the last note of the phrase - note that a slur does not carry "
                      + "into another voice"
                    : "a slur ')' has no '(' open, so no slur is drawn; a slur mark goes AFTER "
                      + "the note it starts on (c4( d e) - a '(' written before a note belongs "
                      + "to no note at all)");
        }
    }
}
