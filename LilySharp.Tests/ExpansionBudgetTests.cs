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
using System.Text;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The expansion budget — the liveness guard on the repository's one unbounded
/// blowup (2026-08-26 review, judgment table's only red row): the phrase DAG
/// doubles per level because <c>ExpandVariable</c> re-expands SIBLING
/// references (the cycle guard cannot see that), and <c>repeat unfold N</c> /
/// <c>R1*N</c> take any integer. Collect runs per keystroke, so before the
/// budget these books hung the preview. In-suite the truncation arms are pinned
/// at a SMALL cap (the collector's cap is init-settable for exactly this — a
/// real-cap truncation necessarily walks 10^6 sites, which belongs in a manual
/// probe, not in every suite run); the real cap's pass-through side is pinned
/// on a normal book.
/// </summary>
[Trait("Category", "Unit")]
public class ExpansionBudgetTests
{
    /// <summary>The 2^depth phrase DAG: p1 { c4 }, p_k { p_{k-1} p_{k-1} }.</summary>
    private static string DagBook(int depth)
    {
        var src = new StringBuilder("phrase p1 { c4 }\n");
        for (int k = 2; k <= depth; k++)
            src.AppendLine($"phrase p{k} {{ p{k - 1} p{k - 1} }}");
        src.AppendLine($"part m {{ }}\nsection A {{ m {{ p{depth} }} }}\n"
            + "form main { ~A }\nscore main { staff m }");
        return src.ToString();
    }

    [Fact]
    public void PhraseDag_TruncatesAtTheBudget_AndTerminates()
    {
        // 2^19 sites would flow from p20; a 100-site budget must stop far short
        // of that AND terminate fast (the pre-budget walk was the hang).
        var collector = new MeasureCollector { ExpansionBudgetCap = 100 };
        var score = collector.Collect(SyntaxTree.Parse(DagBook(20)), "m");

        Assert.NotNull(collector.ExpansionBudgetExceededAt);
        int notes = score.Voice.Measures.Sum(mm => mm.Items.Length);
        Assert.InRange(notes, 1, 100); // truncated, not 2^19
    }

    [Fact]
    public void PhraseDag_UnderTheBudget_IsUntouched()
    {
        // The same book two ways — a tight-but-sufficient cap and the default
        // cap — must agree note for note: the guard may only ever truncate,
        // never perturb what fits. p6 = 2^5 = 32 notes.
        var tight = new MeasureCollector { ExpansionBudgetCap = 1000 };
        var tightScore = tight.Collect(SyntaxTree.Parse(DagBook(6)), "m");
        Assert.Null(tight.ExpansionBudgetExceededAt);

        var defaultCap = new MeasureCollector();
        var defaultScore = defaultCap.Collect(SyntaxTree.Parse(DagBook(6)), "m");
        Assert.Null(defaultCap.ExpansionBudgetExceededAt);

        Assert.Equal(32, defaultScore.Voice.Measures.Sum(mm => mm.Items.Length));
        Assert.Equal(
            defaultScore.Voice.Measures.Select(mm => mm.Items.Length),
            tightScore.Voice.Measures.Select(mm => mm.Items.Length));
    }

    [Fact]
    public void RepeatUnfold_TruncatesItsPasses()
    {
        // 4 quarters per pass, cap 30: the first pass is free (it is the written
        // music), then floor(30/4) = 7 charged passes fit -> 32 notes, not 4000.
        var collector = new MeasureCollector { ExpansionBudgetCap = 30 };
        var score = collector.Collect(
            SyntaxTree.Parse("part m { }\nsection A { m { repeat unfold 1000 { c4 d e f } } }\n"
                + "form main { ~A }\nscore main { staff m }"), "m");

        Assert.NotNull(collector.ExpansionBudgetExceededAt);
        Assert.Equal(32, score.Voice.Measures.Sum(mm => mm.Items.Length));
    }

    [Fact]
    public void MultiMeasureRest_TruncatesItsInterior()
    {
        // R1*1000: the written rest is free, interior copies are charged.
        var collector = new MeasureCollector { ExpansionBudgetCap = 10 };
        var score = collector.Collect(
            SyntaxTree.Parse("part m { }\nsection A { m { R1*1000 } }\n"
                + "form main { ~A }\nscore main { staff m }"), "m");

        Assert.NotNull(collector.ExpansionBudgetExceededAt);
        Assert.Equal(11, score.Voice.Measures.Sum(mm => mm.Items.Length));
    }

    [Fact]
    public void NormalBook_NeverTouchesTheDefaultBudget()
    {
        var collector = new MeasureCollector();
        collector.Collect(SyntaxTree.Parse(
            "part m { }\nsection A { m { repeat unfold 4 { c4 d e f } | R1*3 } }\n"
            + "form main { ~A }\nscore main { staff m }"), "m");
        Assert.Null(collector.ExpansionBudgetExceededAt);
    }

    [Fact]
    public void TruncationLeavesTheMarkersBalanced()
    {
        // A truncated collect must not leak phrase state: a spent budget makes
        // ExpandVariable emit NOTHING (no reset marker without its end marker),
        // so notes written AFTER the truncated reference still collect in the
        // default frame instead of a half-open phrase transpose.
        var collector = new MeasureCollector { ExpansionBudgetCap = 20 };
        var src = new StringBuilder("phrase p1 { c4 }\n");
        for (int k = 2; k <= 12; k++)
            src.AppendLine($"phrase p{k} {{ p{k - 1} p{k - 1} }}");
        src.AppendLine("part m { }\nsection A { m { p12 g4 a b } }\n"
            + "form main { ~A }\nscore main { staff m }");
        var score = collector.Collect(SyntaxTree.Parse(src.ToString()), "m");

        Assert.NotNull(collector.ExpansionBudgetExceededAt);
        // The trailing written notes survive the truncation.
        int notes = score.Voice.Measures.Sum(mm => mm.Items.Length);
        Assert.True(notes >= 3, $"the notes after the truncated reference were lost ({notes})");
    }

    [Fact]
    public void Validator_ReportsTheTruncation_OncePerCollect()
    {
        var collector = new MeasureCollector { ExpansionBudgetCap = 30 };
        var tree = SyntaxTree.Parse("part m { }\nsection A { m { repeat unfold 1000 { c4 d e f } } }\n"
            + "form main { ~A }\nscore main { staff m }");
        collector.Collect(tree, "m");

        var validator = new ExpansionBudgetValidator();
        validator.ValidateWith(tree, new Lazy<MeasureCollector?>(() => collector));

        var d = Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.ExpansionBudgetExceeded, d.Code);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void Validator_SaysNothingOnANormalBook()
    {
        var diags = SemanticValidation.Run(SyntaxTree.Parse(
            "part m { }\nsection A { m { repeat unfold 4 { c4 d e f } } }\n"
            + "form main { ~A }\nscore main { staff m }"));
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.ExpansionBudgetExceeded);
    }
}
