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
        // A slur mark written after a BARLINE binds to no note (a mark annotates the note
        // before it), so it is dropped — and until 2026-10-12 only its partner was reported
        // ("'(' is never closed"), never the mark that went astray (grammar feedback B3).
        // LilyPond says "Unattached SlurEvent" for `g'1 |)`.
        foreach (var slur in tree.GetRoot().DescendantNodes<SlurSyntax>())
        {
            if (slur.Parent is not { } parent)
                continue;
            SyntaxNode? before = null;
            foreach (var child in parent.ChildNodes())
            {
                if (ReferenceEquals(child, slur))
                    break;
                if (child.Kind is not (SyntaxKind.Tie or SyntaxKind.Slur or SyntaxKind.BeamMarker))
                    before = child;
            }
            if (before is BarlineSyntax)
                _diagnostics.Warning(slur.Span, DiagnosticCodes.UnpairedSlur,
                    $"this '{(slur.IsOpen ? "(" : ")")}' follows a barline, so it belongs to no note and "
                    + "is dropped; a slur mark is glued to the note it starts or ends on (g1) |, not g1 |)");
        }


        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.UnpairedSlurWarnings;
        if (warnings == null)
            return;

        foreach (var w in warnings)
        {
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(w.SourcePosition, 1),
                DiagnosticCodes.UnpairedSlur,
                w.AlreadyOpen
                    ? "a slur '(' while another slur is open is ignored - slurs do not nest, "
                      + "so the ')' after it closes the first; close the first slur before "
                      + "opening another, or mark the outer phrase with @phrasingSlur ... @!phrasingSlur"
                    : w.IsOpen
                    ? "a slur '(' is never closed, so no slur is drawn; add the matching ')' "
                      + "after the last note of the phrase - note that a slur does not carry "
                      + "into another voice"
                    : "a slur ')' has no '(' open, so no slur is drawn; a slur mark goes AFTER "
                      + "the note it starts on (c4( d e) - a '(' written before a note belongs "
                      + "to no note at all)");
        }
    }
}
