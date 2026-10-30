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
/// Warns for a later voice of a <c>voice { } { }</c> span (or a later line of a <c>drums { }</c>
/// chart) that runs more bars than the span's first voice (LYS2019): the bars the section is
/// counted in are the first voice's, so the longer voice plays on alone after every other part has
/// stopped (owner's report 2026-10-30: a composed rock piece's feet line ran 74 bars to the hands'
/// 56, and the drums went on 18 bars after the song ended). MeasureValidator holds each bar of a
/// later voice to the meter, and no check counted its bars.
/// </summary>
internal sealed class VoiceSpanLengthValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var spans = root.DescendantNodes<ParallelExpressionSyntax>().ToList();
        if (spans.Count == 0)
            return;
        var phrases = Svg.Collector.SectionBarCounts.PhraseBodies(root);
        foreach (var span in spans)
        {
            var voices = span.Voices.ToList();
            if (voices.Count < 2)
                continue;
            int first = Bars(voices[0], phrases);
            for (int v = 1; v < voices.Count; v++)
            {
                int bars = Bars(voices[v], phrases);
                if (bars > first)
                    // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
                    _diagnostics.Warning(new TextSpan(voices[v].Span.Start, 1), DiagnosticCodes.VoiceRunsPastSpan,
                        $"this voice runs {bars} bars, {bars - first} more than the first voice of its span ({first}) - "
                        + "the voices sound together and the section is the first voice's length, so the rest plays on "
                        + "alone after the other parts stop: end each voice where the first one ends");
            }
        }
    }

    /// <summary>The bars a voice writes, its phrases played in place; a trailing empty bar (the
    /// bar line closing the voice) is not one.</summary>
    private static int Bars(SyntaxNode voice, IReadOnlyDictionary<string, SyntaxNode> phrases)
    {
        var bars = MeasureModel.Split(voice, phrases);
        int count = bars.Count;
        while (count > 0 && bars[count - 1].IsEmpty)
            count--;
        return count;
    }
}
