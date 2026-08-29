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
/// Reports a score whose expansion ran past the collector's per-collect site
/// budget and was TRUNCATED there (<see cref="Svg.Collector.MeasureCollector.ExpansionBudgetCap"/>):
/// nested phrase references double per level (<c>phrase p2 { p1 p1 } …
/// p30 { p29 p29 }</c> is 2^29 sites from 30 written lines), and
/// <c>repeat unfold N</c> / <c>R1*N</c> take any integer the text holds. The
/// cut-off is what keeps the per-keystroke collect from hanging the preview;
/// this diagnostic is the other half of that repair — a silently shortened
/// picture with no arrow at the cause would be a worse defect than the hang.
/// </summary>
/// <remarks>
/// Like <see cref="RepeatPairingValidator"/> this reads back what the shared
/// collector recorded as a side effect rather than re-deciding anything: the
/// budget is charged where the expansion happens, and only the collector knows
/// which construct ran out first. One report per collect, at that construct.
/// </remarks>
internal sealed class ExpansionBudgetValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<Svg.Collector.MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<Svg.Collector.MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        if (sharedCollect.Value?.ExpansionBudgetExceededAt is not { } position)
            return;

        // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
        _diagnostics.Warning(new TextSpan(position, 1),
            DiagnosticCodes.ExpansionBudgetExceeded,
            "this score expands past the collector's site budget, so the picture is "
            + "TRUNCATED from here on. Nested phrase references multiply (each level "
            + "doubles), and 'repeat unfold N' / 'R1*N' take any count - reduce the "
            + "nesting or the counts.");
    }
}
