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
/// ⚠️ THE REASON IT HAD TO WORK THIS WAY IS GONE, AND THE MECHANISM IS KEPT ANYWAY
/// (2026-08-31, LYS1034). Until repeat structure became form-only, the pairing was not
/// decidable on the written text at all: a <c>|:</c> in a section's music could be closed by
/// a <c>:|</c> the form wrote, and books in the wild were spelled that way, so only the
/// collector's expanded stream had the two layers as siblings. Now a <c>|:</c> can only be
/// written in a form, where <c>ParseFormRepeatBlock</c> closes its own block — so what is
/// left to reach this check is a bare <c>:|:</c> standing in a form body, which is ONE
/// written divider meaning <c>:|</c> then <c>|:</c>, and whose second half nothing closes.
/// That is still a question about the laid-out score rather than the text, so the reading is
/// unchanged; what changed is how narrow the surviving case is.
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
