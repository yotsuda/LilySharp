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

using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A token a chord block cannot read — since 2026-08-23 (GRAMMAR_AUDIT 8.1) that
/// is the RETIRED lowercase entry (<c>c</c>, <c>a:m</c>), the exact inverse of the
/// mistake this net was built on — must be REPORTED and KEPT. It used to be
/// consumed by a bare Advance(), which said nothing and dropped the token's width, so
/// every source offset after the block shifted left. The visible damage was elsewhere
/// entirely: a later score's title carried a data-pos 8 short, and clicking that title
/// in the preview put the caret on the `title` keyword instead of inside its string.
/// Same failure as the part-header `key` (see PartHeaderKeyTests).
/// </summary>
[Trait("Category", "Unit")]
public class ChordBlockStrayTokenTests
{
    // Four lowercase roots, each one character plus its trailing space: the 8 characters
    // that would vanish. The `score main "another"` after it is what made it visible.
    private const string Source =
        "octave absolute\n"
        + "part melody\n"
        + "section A {\n"
        + "  melody { c'4 d' e' f' }\n"
        + "  chords prog { c | f | g | c }\n"
        + "}\n"
        + "form main { ~A }\n"
        + "score main { staff melody }\n"
        + "score main \"another\" {\n"
        + "  title \"別の楽譜\"\n"
        + "  staff melody\n"
        + "}\n";

    [Fact]
    public void StrayChordToken_RoundTripsExactly()
    {
        // ToFullString != source means tokens were dropped — the one detector for this.
        var root = SyntaxTree.Parse(Source).GetRoot();
        Assert.Equal(Source, root.ToFullString());
    }

    [Fact]
    public void StrayChordToken_IsReported_NamingTheRetiredSpelling()
    {
        var tree = SyntaxTree.Parse(Source);
        var reported = tree.Diagnostics
            .Where(d => d.Code == LilySharp.Core.Syntax.DiagnosticCodes.ChordBlockBadMember)
            .ToList();
        // One per lowercase root: c, f, g, c.
        Assert.Equal(4, reported.Count);
        Assert.All(reported, d => Assert.Contains("UPPERCASE", d.Message));
    }

    [Fact]
    public void ARetiredColonEntry_IsOneReport_NotThreePerChord()
    {
        // 'a:m' strays as three glued tokens; three errors would bury the one
        // message that matters, so the glued run reports once.
        var tree = SyntaxTree.Parse(Source.Replace("{ c | f | g | c }", "{ a:m | g2:7 | }"));
        Assert.Equal(2, tree.Diagnostics
            .Count(d => d.Code == LilySharp.Core.Syntax.DiagnosticCodes.ChordBlockBadMember));
    }

    [Fact]
    public void AScoreAfterTheChordBlock_KeepsItsTitlesTrueOffset()
    {
        // The symptom that surfaced the bug: this title's data-pos must point at its own
        // opening quote, so the editor's jump (which steps over one quote) lands INSIDE
        // the string rather than back on the `title` keyword.
        var tree = SyntaxTree.Parse(Source);
        var spec = RenderSpecParser.FindByName(tree, "another");
        Assert.NotNull(spec);
        var score = SvgGenerator.CollectScore(tree, spec);

        Assert.Equal("別の楽譜", score.Title);
        int at = score.Header.Title;
        Assert.Equal('"', Source[at]);
        Assert.Equal("別の楽譜", Source.Substring(at + 1, 4));
    }

    [Fact]
    public void UppercaseSymbols_ParseCleanly()
    {
        // The spelling the grammar defines (§ChordEntry: the symbol as it prints).
        var tree = SyntaxTree.Parse(Source.Replace("{ c | f | g | c }", "{ C | F | G | C }"));
        Assert.DoesNotContain(tree.Diagnostics,
            d => d.Code == LilySharp.Core.Syntax.DiagnosticCodes.ChordBlockBadMember);
    }
}
