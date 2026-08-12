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
/// A non-part member of <c>condensedStaff</c> / <c>combinedStaff</c> is reported — and
/// must also be KEPT, so its width stays in the tree. Reporting alone was not enough:
/// the recovery consumed the token with a bare Advance(), which dropped its width and
/// shifted every source offset after the block, exactly as the silent chord-block skip
/// did (see ChordBlockStrayTokenTests) and the part-header `key` before it.
/// </summary>
[Trait("Category", "Unit")]
public class CondensedStaffStrayTokenTests
{
    // `staff` is the first thing a grandStaff user tries here, and it is precisely what
    // the container rejects — so it is the recovery path that runs in practice.
    private const string Source =
        "octave absolute\n"
        + "part fl1\n"
        + "part fl2\n"
        + "section A {\n"
        + "  fl1 { c'1 }\n"
        + "  fl2 { e'1 }\n"
        + "}\n"
        + "form main { ~A }\n"
        + "score main {\n"
        + "  condensedStaff { staff fl1 staff fl2 }\n"
        + "}\n"
        + "score main \"another\" {\n"
        + "  title \"別の楽譜\"\n"
        + "  staff fl1\n"
        + "}\n";

    [Fact]
    public void StrayMember_RoundTripsExactly()
    {
        var root = SyntaxTree.Parse(Source).GetRoot();
        Assert.Equal(Source, root.ToFullString());
    }

    [Fact]
    public void StrayMember_IsStillReported()
    {
        var tree = SyntaxTree.Parse(Source);
        var reported = tree.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.CondensedStaffBadMember)
            .ToList();
        Assert.Equal(2, reported.Count);   // one per `staff`
    }

    [Fact]
    public void StrayMember_IsNotMistakenForAPartName()
    {
        // Keeping the token in the tree must not make it a member: the container names
        // fl1 and fl2, not `staff`.
        var root = SyntaxTree.Parse(Source).GetRoot();
        var condensed = root.DescendantNodes().OfType<CondensedStaffRenderSyntax>().Single();
        Assert.Equal(new[] { "fl1", "fl2" }, condensed.PartNames.ToArray());
    }

    [Fact]
    public void AScoreAfterTheBlock_KeepsItsTitlesTrueOffset()
    {
        var tree = SyntaxTree.Parse(Source);
        var spec = RenderSpecParser.FindByName(tree, "another");
        Assert.NotNull(spec);
        var score = SvgGenerator.CollectScore(tree, spec);

        Assert.Equal("別の楽譜", score.Title);
        int at = score.Header.Title;
        Assert.Equal('"', Source[at]);
        Assert.Equal("別の楽譜", Source.Substring(at + 1, 4));
    }
}
