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
/// what it recorded as a side effect, rather than re-deciding the pairing.
/// <para>
/// WHY THE LAID-OUT SCORE: the pairing is not decidable on the written text. A <c>|:</c> in
/// a section's music may be closed by a <c>:|</c> in a later section, or by one the form
/// writes (docs/inline-repeat-design.md §3.3), so only the collector's expanded stream has
/// the layers as siblings. (From 2026-08-31 to 2026-11-15 a <c>|:</c> could only be written
/// in a form, whose block the parser closes, and all that reached this check was a bare
/// <c>:|:</c> in a form body — ONE written divider meaning <c>:|</c> then <c>|:</c>, whose
/// second half nothing closes. That case is still here.)
/// </para>
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
                + "the matching ':|' in the form. (A ':|' on its own is fine: it repeats "
                + "from the beginning of the piece. And note that ':|:' is TWO barlines, "
                + "':|' then '|:', so it opens a repeat that still needs closing.)");
        }
    }
}
