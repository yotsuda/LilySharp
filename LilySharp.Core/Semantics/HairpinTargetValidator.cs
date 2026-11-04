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
/// Warns for a hairpin whose next level goes the other way (LYS4035): <c>@decresc</c> leading to a
/// level louder than the one it starts from, <c>@cresc</c> (no other spelling) to a softer one
/// (owner's review 2026-10-30: a composed rock piece's last chorus wrote <c>@ff</c>, then
/// <c>@decresc</c>, then <c>@fff</c>).
/// </summary>
/// <remarks>
/// Read line by line as written — one music block's own marks, in order, a phrase's within the
/// phrase — so a hairpin is judged by the level written before it and the next one written after
/// it in the same line. A hairpin with no level before it in its line says nothing. A hairpin
/// its <c>@!cresc</c> has closed leads nowhere: the next level is judged only when it stands on
/// the note the hairpin ends on (until 2026-11-04 a level a phrase later was told it went the
/// wrong way).
/// </remarks>
internal sealed class HairpinTargetValidator : ISemanticValidator
{
    private static readonly string[] Order = ["pppp", "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "ffff"];

    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var marks = root.DescendantNodes<DynamicSyntax>().Cast<SyntaxNode>()
            .Concat(root.DescendantNodes<MusicMarkSyntax>()
                .Where(m => m.IsSpanEnd && SyntaxFacts.IsDynamicSpannerName(m.Name)));
        foreach (var line in marks.GroupBy(LineOf))
        {
            int level = -1;
            (DynamicSyntax Mark, int From)? open = null;
            SyntaxNode? closedOn = null;
            foreach (var node in line.OrderBy(m => m.Span.Start))
            {
                if (node is not DynamicSyntax mark)
                {
                    closedOn = node.Parent;
                    continue;
                }
                string text = mark.DynamicToken.Text;
                if (SyntaxFacts.IsDynamicSpannerName(text))
                {
                    open = level >= 0 ? (mark, level) : null;
                    closedOn = null;
                    continue;
                }
                int next = System.Array.IndexOf(Order, text);
                if (next < 0)
                    continue;
                if (open is { } hairpin && !mark.IsSubito && (closedOn == null || ReferenceEquals(closedOn, mark.Parent)))
                {
                    bool down = hairpin.Mark.DynamicToken.Text != "cresc";
                    if (down ? next > hairpin.From : next < hairpin.From)
                        // ASCII punctuation only: the CLI prints this to legacy-codepage consoles.
                        _diagnostics.Warning(hairpin.Mark.Span, DiagnosticCodes.HairpinAgainstItsLevel,
                            $"@{hairpin.Mark.DynamicToken.Text} leads to @{text}, {(down ? "louder" : "softer")} than the "
                            + $"@{Order[hairpin.From]} it starts from - a {(down ? "decrescendo goes down" : "crescendo goes up")}: "
                            + $"write @{(down ? "cresc" : "decresc")}, or the {(down ? "softer" : "louder")} level it reaches");
                }
                open = null;
                level = next;
            }
        }
    }

    /// <summary>The voice line a mark is written in. A cue is another instrument's line, inside
    /// this one's bars: its levels say nothing of this line's hairpins.</summary>
    private static SyntaxNode LineOf(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is CueExpressionSyntax)
                return p;
        return SpanNames.Line(node);
    }
}
