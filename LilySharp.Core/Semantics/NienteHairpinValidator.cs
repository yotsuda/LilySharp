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
/// Warns at a <c>@niente</c> at a hairpin's THICK end (<see cref="DiagnosticCodes.NienteAtThickEnd"/>,
/// LYS4029): a crescendo ending on it, a decrescendo starting from it.
/// </summary>
/// <remarks>
/// The pairing is the page's (<see cref="MeasureCollector.NienteAtThickEndHairpins"/>), for the
/// reason <see cref="SlurPairingValidator"/> gives: which hairpin a niente ends is decided by
/// the rule the page draws by, not by a second reading of the source.
/// </remarks>
internal sealed class NienteHairpinValidator : ISharedCollectValidator
{
    private readonly List<Diagnostic> _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(() => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // Cheap exit: no niente, nothing to pair — and no collect to pay for.
        if (!tree.GetRoot().DescendantNodes<DynamicSyntax>().Any(d => d.Level == DynamicLevel.Niente))
            return;
        if (sharedCollect.Value is not { } collector)
            return;
        foreach (var (_, niente, crescendo) in collector.NienteAtThickEndHairpins)
            _diagnostics.Add(Diagnostic.Warning(new TextSpan(niente, 1), DiagnosticCodes.NienteAtThickEnd,
                crescendo
                    ? "this '@niente' ends a crescendo: niente is the silence a hairpin's thin end "
                      + "touches, and a crescendo ends loud. The hairpin is drawn without a circle and "
                      + "'niente' is printed. For a crescendo FROM nothing write '@niente@cresc' at its start"
                    : "this '@niente' starts a decrescendo: niente is the silence a hairpin's thin end "
                      + "touches, and a decrescendo starts loud. The hairpin is drawn without a circle "
                      + "and 'niente' is printed. For a decrescendo TO nothing end it with '@niente'"));
    }
}
