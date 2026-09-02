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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>pageBreak</c> (LilyPond's <c>\pageBreak</c>) forces a page break after the measure it
/// closes — and the system break that comes with it — and <c>noPageBreak</c>
/// (<c>\noPageBreak</c>) forbids one; the pair beside <c>break</c> / <c>noBreak</c>, written
/// where those are written. Owner's request, 2026-09-02.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/music-functions-init.ly:1411-1418 pageBreak — line-break-permission
/// 'force AND page-break-permission 'force; :1255-1259 noPageBreak — page 'forbid alone.
/// The model already carried <c>Measure.PageBreakPermission</c> and the breaker already
/// read <c>SystemDetails.PagePermission</c>; what was missing was every step between: the
/// keyword, the builder, the system's permission, and the route into the breaker for a
/// book that would otherwise fit one page.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PageBreakTests
{
    private static Measure[] Collect(string body)
    {
        string src = "time 4/4\nkey c major\npart m { section A { " + body + " } }\n"
                   + "form main { A }\nscore main { staff m }";
        return new MeasureCollector().Collect(SyntaxTree.Parse(src), "m").Voice.Measures.ToArray();
    }

    [Fact]
    public void PageBreak_ForcesThePageAndTheLine_AfterThePrecedingMeasure()
    {
        var m = Collect("c4 d e f | pageBreak g a b c |");
        Assert.Equal(BreakPermission.Force, m[0].PageBreakPermission);
        Assert.Equal(BreakPermission.Force, m[0].LineBreakPermission);
        Assert.Equal(BreakPermission.Force, m[0].EffectivePagePermission);
        Assert.Equal(BreakPermission.Allow, m[1].PageBreakPermission);
    }

    [Fact]
    public void MidMeasurePageBreak_AppliesToThatMeasure()
    {
        var m = Collect("c4 d pageBreak e f | g a b c |");
        Assert.Equal(BreakPermission.Force, m[0].PageBreakPermission);
        Assert.True(m[0].HasBreakAfter);
    }

    [Fact]
    public void NoPageBreak_ForbidsThePage_AndLeavesTheLineAlone()
    {
        var m = Collect("c4 d e f | noPageBreak g a b c |");
        Assert.Equal(BreakPermission.Forbid, m[0].PageBreakPermission);
        Assert.Equal(BreakPermission.Allow, m[0].LineBreakPermission);
        Assert.Equal(BreakPermission.Forbid, m[0].EffectivePagePermission);
    }

    [Fact]
    public void Break_LeavesThePagePermissionAlone()
    {
        // The positive control for the page half: the line pair never touches it.
        var m = Collect("c4 d e f | break g a b c | noBreak d e f g |");
        Assert.All(m, x => Assert.Equal(BreakPermission.Allow, x.PageBreakPermission));
    }

    [Fact]
    public void FormPageBreak_FlagsTheSectionJustPlayed()
    {
        var source = """
            time 4/4
            key c major
            part m { clef treble
              section A { c'4 d' e' f' | }
              section B { g'4 a' b' c'' | }
              section C { c'4 d' e' f' | }
            }
            form main { A pageBreak B noPageBreak C }
            score main { staff m }
            """;
        var m = new MeasureCollector().Collect(SyntaxTree.Parse(source), "m").Voice.Measures;
        Assert.Equal(3, m.Length);
        Assert.Equal(BreakPermission.Force, m[0].PageBreakPermission);
        Assert.Equal(BreakPermission.Force, m[0].LineBreakPermission);
        Assert.Equal(BreakPermission.Forbid, m[1].PageBreakPermission);
        Assert.Equal(BreakPermission.Allow, m[2].PageBreakPermission);
    }

    private static ScoreLayout LayoutOf(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        return new LayoutEngine().Layout(score);
    }

    private const string TwoShortSections = """
        time 4/4
        key c major
        part m { clef treble
          section A { c'4 d' e' f' | g'4 a' b' c'' | }
          section B { c''4 b' a' g' | f'4 e' d' c' | }
        }
        form main { A {{JOIN}} B }
        score main { staff m }
        """;

    /// <summary>
    /// A book that fits one page is put on two by <c>pageBreak</c>: the single-page stack
    /// cannot honour a forced page break, so the book goes to the breaker whether or not
    /// it would have fit — and the breaker puts the break exactly there.
    /// </summary>
    [Fact]
    public void PageBreak_BreaksAPageThatWouldHaveFitOne()
    {
        var without = LayoutOf(TwoShortSections.Replace("{{JOIN}}", ""));
        var with = LayoutOf(TwoShortSections.Replace("{{JOIN}}", "pageBreak"));
        Assert.Single(without.Pages);
        Assert.Equal(2, with.Pages.Length);
        // Page 1 ends with the measure the directive follows (A's second bar), and page 2
        // opens with B — the system break the page break implies.
        Assert.Equal(1, with.Pages[0].Systems[^1].Measures[^1].MeasureIndex);
        Assert.Equal(2, with.Pages[1].Systems[0].Measures[0].MeasureIndex);
    }

    [Fact]
    public void NoPageBreak_DoesNotMoveAOnePageBook()
    {
        var with = LayoutOf(TwoShortSections.Replace("{{JOIN}}", "noPageBreak"));
        Assert.Single(with.Pages);
    }

    [Fact]
    public void TheTwin_WritesLilyPondsOwnCommands()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse("""
            time 4/4
            key c major
            part m { clef treble
              section A { c'4 d' e' f' | pageBreak g'4 a' b' c'' | noPageBreak c''4 b' a' g' | }
            }
            form main { A }
            score main { staff m }
            """));
        Assert.Contains("\\pageBreak", ly);
        Assert.Contains("\\noPageBreak", ly);
    }

    [Fact]
    public void TheCompletions_OfferThePairWhereBreakIsOffered()
    {
        var music = LilySharpLanguageServer.GetMusicCompletions("", 0).Items.Select(i => i.Label).ToList();
        Assert.Contains("pageBreak", music);
        Assert.Contains("noPageBreak", music);

        const string doc = "part m { section A { c4 d e f | } }\nform main { A }";
        var form = LilySharpLanguageServer.GetFormCompletions(doc).Items.Select(i => i.Label).ToList();
        Assert.Contains("pageBreak", form);
        Assert.Contains("noPageBreak", form);
    }

    [Fact]
    public void TheLilyPondSpelling_IsPointedAtTheLilySharpOne()
    {
        var tree = SyntaxTree.Parse("part m { section A { c4 d e f | \\pageBreak g4 a b c | } }\nform main { A }\nscore main { staff m }");
        // The general "no leading backslash" hint, naming the bare word — the same hint
        // `\tempo` gets, because the spelling IS LilyPond's minus the backslash.
        Assert.Contains(tree.Diagnostics, d => d.Message.Contains("write 'pageBreak"));
    }
}
