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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The validators' bar model (<see cref="MeasureModel.Split"/>) expands the same phrase
/// DAG and repeats the collector expands, and spends the expansion budget in the
/// collector's own unit — a phrase entry, each site of an expanded phrase body, each
/// extra repeat pass by its body's size, each interior copy of <c>R1*N</c> — and nothing
/// for the written music of the scope (HANDOFF §2 R12 ⒠: until 2026-10-10 the model
/// capped the flattened item count, written music included, so a long scope was cut
/// here while the collector drew it whole and no LYS1033 said so). Each truncation arm
/// is pinned at a small budget beside the collector's answer at the same cap
/// (ExpansionBudgetTests' books), so the two cannot drift apart again in silence.
/// </summary>
[Trait("Category", "Unit")]
public class MeasureModelBudgetTests
{
    /// <summary>The 2^depth phrase DAG of ExpansionBudgetTests: p1 { c4 }, p_k { p_{k-1} p_{k-1} }.</summary>
    private static string DagBook(int depth)
    {
        var src = new StringBuilder("phrase p1 { c4 }\n");
        for (int k = 2; k <= depth; k++)
            src.AppendLine($"phrase p{k} {{ p{k - 1} p{k - 1} }}");
        src.AppendLine($"part m {{ }}\nsection A {{ m {{ p{depth} }} }}\n"
            + "form { ~A }\nscore { staff m }");
        return src.ToString();
    }

    private static string Book(string music)
        => $"part m {{ }}\nsection A {{ m {{ {music} }} }}\nform {{ ~A }}\nscore {{ staff m }}";

    /// <summary>The model's bars of the one part block of <paramref name="source"/> at
    /// <paramref name="budget"/>.</summary>
    private static List<MeasureModel.Bar> ModelBars(string source, int budget)
    {
        var tree = SyntaxTree.Parse(source);
        var block = tree.GetRoot().DescendantNodes().OfType<PartBlockSyntax>().First();
        return MeasureModel.Split(block, SectionBarCounts.PhraseBodies(tree.GetRoot()), expansionBudget: budget);
    }

    /// <summary>Quarters of music the model saw — every note here is a <c>c4</c>.</summary>
    private static double Quarters(IEnumerable<MeasureModel.Bar> bars)
        => bars.Sum(b => 4.0 * b.Duration.Numerator / b.Duration.Denominator);

    private static int CollectorNotes(string source, int budget)
    {
        var collector = new MeasureCollector { ExpansionBudgetCap = budget };
        var score = collector.Collect(SyntaxTree.Parse(source), "m");
        Assert.NotNull(collector.ExpansionBudgetExceededAt);
        return score.Voice.Measures.Sum(mm => mm.Items.Length);
    }

    [Fact]
    public void WrittenMusic_IsNeverTruncated()
    {
        // Twelve written bars at a budget of three sites: the budget is the EXPANSION's,
        // and the collector charges nothing for written music either.
        var bars = ModelBars(Book(string.Concat(Enumerable.Repeat("c4 d e f | ", 12))), budget: 3);
        Assert.Equal(12, bars.Count);
    }

    [Fact]
    public void PhraseDag_TruncatesWhereTheCollectorTruncates()
    {
        // p6 is 2^5 = 32 notes under 63 phrase entries; at 40 sites both walks stop in
        // the same place, so the model's quarters are the collector's notes.
        string book = DagBook(6);
        Assert.Equal(CollectorNotes(book, 40), Quarters(ModelBars(book, 40)));
        Assert.Equal(32, Quarters(ModelBars(book, 1000)));
    }

    [Fact]
    public void RepeatUnfold_TruncatesItsPasses_LikeTheCollector()
    {
        // 4 quarters per pass, cap 30: the first pass is free, seven charged passes fit
        // (ExpansionBudgetTests.RepeatUnfold_TruncatesItsPasses) — 32 notes, eight 4/4 bars.
        string book = Book("repeat unfold 1000 { c4 d e f }");
        var bars = ModelBars(book, 30);
        Assert.Equal(CollectorNotes(book, 30), Quarters(bars));
        Assert.Equal(8, bars.Count);
    }

    [Fact]
    public void MultiMeasureRest_TruncatesItsInterior_LikeTheCollector()
    {
        // R1*1000 at ten sites: the written rest is free, ten interior copies are charged
        // (ExpansionBudgetTests.MultiMeasureRest_TruncatesItsInterior) — eleven bars.
        string book = Book("R1*1000");
        Assert.Equal(CollectorNotes(book, 10), ModelBars(book, 10).Count);
    }

    [Fact]
    public void PhraseDag_Terminates_AtTheDefaultBudget()
    {
        // 2^19 notes would flow from p20; the default budget stops the walk short of them.
        var tree = SyntaxTree.Parse(DagBook(20));
        var block = tree.GetRoot().DescendantNodes().OfType<PartBlockSyntax>().First();
        var bars = MeasureModel.Split(block, SectionBarCounts.PhraseBodies(tree.GetRoot()));
        Assert.InRange(Quarters(bars), 1, MeasureCollector.DefaultExpansionBudgetCap);
    }
}
