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

using System;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A note that OPENS an indented line is clickable at the note, not at the whitespace in
/// front of it.
/// </summary>
/// <remarks>
/// A reader clicked `ees,1` at the start of line 15 of their score and nothing lit up
/// (reported 2026-08-29, scratch/ベースタブLy/Walk.lys). Its glyph carried the offset of the
/// line break before it, and the editor resolves a click by that offset.
/// <para>
/// The cause is one property. Leading trivia is only non-empty where the lexer could not
/// hand the skipped text to the PREVIOUS token as trailing trivia, and the one thing that
/// ends a trailing run is a line break — so same-line spacing never showed the defect and
/// every note but the first of a line had the right address. <c>GreenNode.LeadingTrivia</c>
/// is virtual and only a TOKEN overrides it, so a composite node — a note, a chord, a
/// repeat — answered 0 whatever its first token carried, and <c>SyntaxNode.Span</c> was
/// computed straight from that. Both halves moved: the SVG's <c>data-pos</c> and the
/// (line, column) a diagnostic underlines.
/// </para>
/// <para>
/// ★ 597 books of the 1519 on disk moved when this closed, and every one of them differs
/// ONLY in <c>data-pos</c> — no glyph and no page moved anywhere. The 466 diagnostic lines
/// that moved differ only in their address, with no message, count or exit code changing.
/// </para>
/// </remarks>
public class LineLeadingSourcePositionTests
{
    // Two notes open a line and two do not, so a fix that shifted EVERY address by the
    // indent would fail here as loudly as no fix at all.
    private const string Indented =
        "octave absolute\n"
        + "time 4/4\n"
        + "part m { clef bass }\n"
        + "section A {\n"
        + "  m {\n"
        + "    c1 | d1 |\n"
        + "    e1 | f1 |\n"
        + "  }\n"
        + "}\n"
        + "form main { A }\n"
        + "score main { staff m }\n";

    private static string Render(string source) =>
        SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    private static int OffsetOf(string source, string token, int occurrence = 1)
    {
        int at = -1;
        for (int i = 0; i < occurrence; i++)
            at = source.IndexOf(token, at + 1, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the probe does not contain occurrence {occurrence} of \"{token}\"");
        return at;
    }

    [Theory]
    // The two that OPEN a line — the case that was broken.
    [InlineData("c1", 1)]
    [InlineData("e1", 1)]
    // …and the two that do not, which were always right and must stay right.
    [InlineData("d1", 1)]
    [InlineData("f1", 1)]
    public void EveryNoteIsTaggedAtItsOwnFirstCharacter(string token, int occurrence)
    {
        var svg = Render(Indented);
        int expected = OffsetOf(Indented, token, occurrence);

        var tagged = Regex.Matches(svg, "data-pos=\"(?<p>[0-9]+)\"")
            .Select(m => int.Parse(m.Groups["p"].Value)).ToHashSet();

        Assert.True(tagged.Contains(expected),
            $"no glyph carries offset {expected}, where \"{token}\" begins. "
            + $"Tagged offsets: {string.Join(", ", tagged.OrderBy(p => p))}");
    }

    /// <summary>
    /// The other half of the same property: a diagnostic anchored on a node that opens a
    /// line underlines the node, not the indent. The measure holds two whole notes in 4/4,
    /// and its first note stands at column 5.
    /// </summary>
    [Fact]
    public void ADiagnosticOnALineLeadingNodeNamesTheNodesColumn()
    {
        const string overfull =
            "octave absolute\n"
            + "time 4/4\n"
            + "part m { clef bass }\n"
            + "section A {\n"
            + "  m {\n"
            + "    c1 c1 |\n"
            + "  }\n"
            + "}\n"
            + "form main { A }\n"
            + "score main { staff m }\n";

        var tree = SyntaxTree.Parse(overfull);
        var validator = new LilySharp.Core.Semantics.MeasureValidator();
        validator.Validate(tree);
        var diagnostic = Assert.Single(
            validator.Diagnostics.Where(d => d.Message.Contains("exceeds time signature")));

        int column = diagnostic.Span.Start
            - (overfull.LastIndexOf('\n', diagnostic.Span.Start - 1) + 1) + 1;
        Assert.Equal(5, column);
    }
}
