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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>removeEmpty</c> for a tab, as for a staff (owner's request 2026-11-20: a bass tab written by
/// ear under a chord chart, <c>tab bass as removeEmpty true</c>), and the editor offering it — for
/// the tab, and for a staff whose part is named like a clef (<c>staff bass as removeEmpty ▮</c>
/// offered no value: the row reader had taken the <c>as</c> for the part's name).
/// </summary>
[Trait("Category", "Unit")]
public class TabRemoveEmptyTests
{
    private static string Book(string row) => $$"""
        time 4/4
        part bass { instrument bass5 }
        section A {
          chords prog { C | D | }
          bass { c1 | }
        }
        form { A }
        score {
          chords prog
          {{row}}
        }
        """;

    private static Diagnostic[] Errors(string row)
    {
        var tree = SyntaxTree.Parse(Book(row));
        return tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
    }

    [Theory]
    [InlineData("tab bass as removeEmpty true")]
    [InlineData("tab bass as removeEmpty all")]
    [InlineData("tab bass as numbers removeEmpty true")]
    [InlineData("tab bass as removeEmpty true numbers 8vb")]
    [InlineData("tab bass5 bass as 8vb removeEmpty false")]
    public void ATabTakesRemoveEmpty_BesideItsStyleAndOctave(string row)
        => Assert.Empty(Errors(row));

    [Fact]
    public void AnUnknownValue_IsRefused_AsAStaffs()
        => Assert.Contains(Errors("tab bass as removeEmpty TRUE"), d => d.Message.Contains("Unknown removeEmpty 'TRUE'"));

    [Theory]
    [InlineData("tab bass", false, false)]
    [InlineData("tab bass as removeEmpty true", true, false)]
    [InlineData("tab bass as numbers removeEmpty all", true, true)]
    [InlineData("tab bass as removeEmpty false", false, false)]
    public void TheTabStaff_TakesTheFlags(string row, bool removeEmpty, bool removeFirst)
    {
        var tree = SyntaxTree.Parse(Book(row));
        var spec = Core.Svg.Collector.RenderSpecParser.FindFirst(tree);
        var multi = new Core.Svg.Collector.MeasureCollector().CollectMultiStaff(tree, spec!);
        var tab = multi.StaffGroups.SelectMany(g => g.Staves).Single(s => s.IsTab);
        Assert.Equal(removeEmpty, tab.RemoveEmpty);
        Assert.Equal(removeFirst, tab.RemoveFirst);
    }

    [Fact]
    public void TheStyleAndOctave_AreNotReadOffTheRemoveEmptyPair()
    {
        var tab = SyntaxTree.Parse(Book("tab bass as removeEmpty true numbers")).GetRoot().DescendantNodes<TabRenderSyntax>().Single();
        Assert.Equal("numbers", tab.DisplayModeToken?.Text);
        Assert.Null(tab.OctaveToken);
        Assert.Equal("true", tab.RemoveEmptyToken?.Text);
    }

    [Fact]
    public void TheLilyPondTwin_WritesTheContextMod()
    {
        string ly = new Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book("tab bass as removeEmpty all")));
        Assert.Matches(@"\\new TabStaff \\with \{[^}]*\\RemoveAllEmptyStaves", ly);
    }

    // ----- the one-line staff standing in for the hidden tab (owner's request 2026-11-20) -----

    private const string Chart = """
        time 4/4
        part bass { instrument bass5 }
        section A { chords prog { C | D | C | D | } }
        section B {
          chords prog { G | C | G | C | }
          bass { g1 | c | g | c | }
        }
        form { A B }
        score {
          chords prog
          tab bass as removeEmpty true
        }
        """;

    private static Core.Svg.Model.MultiStaffScore Collect(string book)
    {
        var tree = SyntaxTree.Parse(book);
        return new Core.Svg.Collector.MeasureCollector().CollectMultiStaff(tree, Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!);
    }

    [Fact]
    public void AScoreWhoseStavesMayAllHide_GetsTheStandIn_LastAndOnlyWhereTheyAreHidden()
    {
        var score = Collect(Chart);
        var staves = score.EnumerateStaves().ToList();
        var standIn = staves[^1].Staff;
        Assert.True(standIn.StandIn);
        Assert.Equal(1, standIn.Lines);
        Assert.Equal(8, standIn.PrimaryVoice.Measures.Length);          // the music's bars, silent
        var tab = staves.Single(s => s.Staff.IsTab);
        bool Hidden(Core.Svg.Model.Staff s, int idx, int from, int to, bool first)
            => Core.Svg.Layout.HaraKiri.ShouldHideStaff(s, idx, score, from, to, first);
        // Bars 1-4 (section A, no bass) on a later system: the tab hides, the stand-in shows.
        Assert.True(Hidden(tab.Staff, tab.GlobalStaffIndex, 0, 4, false));
        Assert.False(Hidden(standIn, staves[^1].GlobalStaffIndex, 0, 4, false));
        // On the first system the tab stays (`true`, not `all`), so the stand-in hides.
        Assert.True(Hidden(standIn, staves[^1].GlobalStaffIndex, 0, 4, true));
        // Bars 5-8 (the bass plays): the tab shows, the stand-in hides.
        Assert.True(Hidden(standIn, staves[^1].GlobalStaffIndex, 4, 8, false));
    }

    [Theory]
    [InlineData("tab bass")]                                         // never hides: a line on every system already
    [InlineData("tab bass as removeEmpty true\n  staff bass")]       // one staff that never hides
    public void NoStandIn_WhereAStaffNeverHides(string rows)
        => Assert.DoesNotContain(Collect(Chart.Replace("tab bass as removeEmpty true", rows)).EnumerateStaves(), s => s.Staff.StandIn);

    [Fact]
    public void TheStandIn_IsNotWrittenToTheTwin()
        => Assert.DoesNotContain("RhythmicStaff", new Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Chart)));

    // ----- the editor -----

    private static LilySharpLanguageServer.CompletionContext Ctx(string row)
    {
        string text = Book(row);
        int at = text.IndexOf(row, System.StringComparison.Ordinal) + row.Length;
        return LilySharpLanguageServer.GetCompletionContext(text[..at], at);
    }

    [Theory]
    [InlineData("staff bass as removeEmpty ")]     // `bass` is a clef word and the part here
    [InlineData("staff bass as removeEmpty a")]
    [InlineData("staff bass as lines 1 removeEmpty ")]
    [InlineData("tab bass as removeEmpty ")]
    [InlineData("tab bass as numbers removeEmpty ")]
    public void AfterRemoveEmpty_ItsValuesAreOffered(string row)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterRemoveEmpty, Ctx(row));

    [Fact]
    public void AStaffNamedLikeAClef_GoesOnInItsSelectors()
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterStaffSelectorChain, Ctx("staff bass as removeEmpty true "));

    [Fact]
    public void ATabOffersRemoveEmpty_AfterItsAs_InItsChain_AndAfterItsPart()
    {
        Assert.Contains(LilySharpLanguageServer.GetTabDisplayModeCompletions().Items, i => i.Label == "removeEmpty");
        Assert.Contains(LilySharpLanguageServer.GetTabSelectorChainCompletions(["numbers"]).Items, i => i.Label == "removeEmpty");
        Assert.DoesNotContain(LilySharpLanguageServer.GetTabSelectorChainCompletions(["removeEmpty"]).Items, i => i.Label == "removeEmpty");
        Assert.Contains(LilySharpLanguageServer.GetTabAttachNameCompletions().Items, i => i.Label == "as removeEmpty");
        Assert.Contains(LilySharpLanguageServer.GetTabTuningRefCompletions(Book(""), "bass").Items, i => i.Label == "as removeEmpty");
        // After the pair, the chain goes on with what is not yet written.
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterTabSelectorChain, Ctx("tab bass as removeEmpty true "));
        Assert.Equal(["removeEmpty"], LilySharpLanguageServer.TabSelectorsWritten(Book("tab bass as removeEmpty true "), Book("tab bass as removeEmpty true ").IndexOf("true ", System.StringComparison.Ordinal) + 5));
    }
}
