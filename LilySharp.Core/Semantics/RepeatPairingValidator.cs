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
/// Reports a <c>|:</c> that no <c>:|</c> ever closes.
/// </summary>
/// <remarks>
/// Like <see cref="SlurPairingValidator"/> this runs the shared collector and reads back
/// what it recorded as a side effect, rather than re-deciding the pairing. Here that is not
/// only a consistency preference: the pairing is NOT decidable on the written text at all.
/// A section is not a piece of music on its own — it becomes one when a <c>form</c> lays it
/// out — so a <c>|:</c> written in a section's music may be closed by a <c>:|</c> the form
/// writes, and books in the wild are spelled exactly that way. Only the collector's
/// expanded measure stream has the two layers as siblings.
/// <para>
/// An ERROR, not a warning, and the direction is deliberate: <c>error → warning → works</c>
/// is a move that breaks nobody, so the strict position is the one to take while the
/// grammar is young. Giving a one-sided <c>|:</c> a meaning later only widens what is
/// accepted; taking the spelling back after release would not be possible at all.
/// </para>
/// </remarks>
internal sealed class RepeatPairingValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<Svg.Collector.MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<Svg.Collector.MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.UnpairedRepeatWarnings;
        if (warnings == null)
            return;

        foreach (var w in warnings)
        {
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Error(new TextSpan(w.SourcePosition, 1),
                DiagnosticCodes.UnpairedRepeat,
                "a repeat '|:' is never closed, so where the repeat ends is undefined - add "
                + "the matching ':|'. It may be written in this section or in the form that "
                + "lays the sections out, whichever is where the repeat should end. (A ':|' "
                + "on its own is fine: it repeats from the beginning of the piece.)");
        }
    }
}
