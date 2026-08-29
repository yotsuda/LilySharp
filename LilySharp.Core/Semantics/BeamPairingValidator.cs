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
/// Warns when a manual beam bracket pairs with nothing — a <c>[</c> that is never closed, or
/// a <c>]</c> with none open. <see cref="Svg.Collector.BeamDetector"/> builds no group from
/// it and the notes fall back to automatic beaming, so the score is beamed the way the file
/// did NOT ask for, and until this existed nothing said so.
/// </summary>
/// <remarks>
/// Like <see cref="SlurPairingValidator"/> and <see cref="TieTargetValidator"/>, this reads
/// back what the shared collector recorded as a side effect
/// (<see cref="Svg.Collector.MeasureCollector.UnpairedBeamWarnings"/>) rather than re-deciding
/// the pairing, so the warning can never disagree with what is drawn.
/// </remarks>
internal sealed class BeamPairingValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<Svg.Collector.MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<Svg.Collector.MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.UnpairedBeamWarnings;
        if (warnings == null)
            return;

        foreach (var w in warnings)
        {
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(w.SourcePosition, 1),
                DiagnosticCodes.UnpairedBeam,
                w.IsOpen
                    ? "a manual beam '[' is never closed, so the grouping is discarded and "
                      + "these notes are beamed automatically instead; add the matching ']' "
                      + "on the last note of the group - note that a beam does not carry into "
                      + "another voice"
                    : "a manual beam ']' has no '[' open, so the grouping is discarded and "
                      + "these notes are beamed automatically instead; a beam bracket goes "
                      + "AFTER the note it attaches to, opening on the first note of the group");
        }
    }
}
