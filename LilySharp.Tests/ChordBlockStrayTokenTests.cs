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
/// A token a chord block cannot read — a chord symbol written the way it PRINTS
/// (<c>C</c>) where a root is lowercase — must be REPORTED and KEPT. It used to be
/// consumed by a bare Advance(), which said nothing and dropped the token's width, so
/// every source offset after the block shifted left. The visible damage was elsewhere
/// entirely: a later score's title carried a data-pos 8 short, and clicking that title
/// in the preview put the caret on the `title` keyword instead of inside its string.
/// Same failure as the part-header `key` (see PartHeaderKeyTests).
/// </summary>
[Trait("Category", "Unit")]
public class ChordBlockStrayTokenTests
{
    // Four uppercase roots, each one character plus its trailing space: the 8 characters
    // that used to vanish. The `score main "another"` after it is what made it visible.
    private const string Source =
        "octave absolute\n"
        + "part melody\n"
        + "section A {\n"
        + "  melody { c'4 d' e' f' }\n"
        + "  chords prog { C | F | G | C }\n"
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
    public void StrayChordToken_IsReported_NamingTheLowercaseSpelling()
    {
        var tree = SyntaxTree.Parse(Source);
        var reported = tree.Diagnostics
            .Where(d => d.Code == LilySharp.Core.Syntax.DiagnosticCodes.ChordBlockBadMember)
            .ToList();
        // One per uppercase root: C, F, G, C.
        Assert.Equal(4, reported.Count);
        Assert.All(reported, d => Assert.Contains("LOWERCASE", d.Message));
        Assert.Contains(reported, d => d.Message.Contains("'c' is what prints as 'C'"));
        Assert.Contains(reported, d => d.Message.Contains("'f' is what prints as 'F'"));
        Assert.Contains(reported, d => d.Message.Contains("'g' is what prints as 'G'"));
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
    public void LowercaseRoots_ParseCleanly()
    {
        // The spelling the grammar defines (§ChordEntry: `c` = C) stays silent.
        var tree = SyntaxTree.Parse(Source.Replace("{ C | F | G | C }", "{ c | f | g | c }"));
        Assert.DoesNotContain(tree.Diagnostics,
            d => d.Code == LilySharp.Core.Syntax.DiagnosticCodes.ChordBlockBadMember);
    }
}
