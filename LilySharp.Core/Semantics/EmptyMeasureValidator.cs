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
/// Warns for an empty placeholder measure — an explicit <c>| |</c> pair with no music
/// between, anywhere (leading, mid-piece, or trailing). Such a measure holds a slot so
/// other parts stay aligned (the intended "fix the alignment first, fill the notes
/// later" workflow) but has no music, so it is shorter than the meter until filled.
/// A SINGLE bare <c>|</c> never creates one: at the section's edges it anchors the
/// boundary, between full bars it confirms the auto-filled close.
/// </summary>
/// <remarks>
/// Which bare barlines pair into an empty measure depends on Lily#'s boundary logic
/// (a barline anchoring the section start or confirming an already-full bar is silent;
/// the second of a pair opens a placeholder), which <see cref="MeasureCollector"/>
/// resolves exactly while collecting. Rather than re-derive that from the syntax tree,
/// this validator runs the collector and reads back the placeholder positions it
/// recorded (<see cref="MeasureCollector.EmptyPlaceholderWarnings"/>).
/// </remarks>
internal sealed class EmptyMeasureValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(new System.Lazy<MeasureCollector?>(() => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(System.Lazy<MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var spans = sharedCollect.Value?.EmptyPlaceholderWarnings;
        if (spans == null)
            return;

        foreach (var (start, end) in spans)
        {
            // The ordinary underfull-measure warning (LYS2001), squiggled over the
            // region BETWEEN the barlines — an empty measure is just the zero case
            // of underfull, and the warning stays the same kind as the user fills
            // it (`| |` -> `| c4 |` -> full).
            // ASCII punctuation only: this string reaches legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(start, System.Math.Max(1, end - start + 1)),
                DiagnosticCodes.MeasureIncomplete,
                "Measure duration 0 is less than the meter - an empty measure; " +
                "fill it (or keep it as a slot that aligns the parts)");
        }
    }
}
