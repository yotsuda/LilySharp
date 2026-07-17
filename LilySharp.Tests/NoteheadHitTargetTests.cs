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

using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Interactive (preview) SVG lays a tight, transparent hit rectangle — the size
/// of the notehead ink — over each head so only the head is clickable, not the
/// whole glyph em-box. Static SVG output carries no such rects.
/// </summary>
[Trait("Category", "Unit")]
public class NoteheadHitTargetTests
{
    private const string Doc = """
        part m { clef treble section A { c'4 d' } }
        form main { A }
        score main "s" { staff m }
        """;

    private static string Render(SvgRenderOptions options)
        => SvgGenerator.Generate(SyntaxTree.Parse(Doc), options);

    [Fact]
    public void PreviewEmitsTightNoteheadHitRects()
    {
        var svg = Render(SvgRenderOptions.Preview());

        // A quarter head's ink box (Emmentaler black notehead) is ~1.30 × 1.09
        // staff-spaces — matching GetNoteheadBBox, far tighter than the font-size
        // 4 em-box the <text> would otherwise hit.
        var m = Regex.Match(svg,
            "<rect class=\"nh-hit\" x=\"[\\d.]+\" y=\"[\\d.]+\" width=\"([\\d.]+)\" height=\"([\\d.]+)\" fill=\"none\" pointer-events=\"all\" data-pos=\"\\d+\"/>");
        Assert.True(m.Success, "expected a transparent nh-hit rect per notehead");

        var bbox = GlyphMetrics.GetNoteheadBBox(4);
        Assert.Equal(GlyphMetrics.GetNoteheadAdvance(4), double.Parse(m.Groups[1].Value), 1);
        Assert.Equal(bbox.Height, double.Parse(m.Groups[2].Value), 1);

        // The head glyph itself is made non-interactive so the rect owns the click.
        Assert.Contains("<text class=\"music\" pointer-events=\"none\"", svg);
    }

    // A barline can collapse several written bars: the renderer puts the CLICK target on
    // data-pos and the extra HIGHLIGHT offsets on data-alt. These helpers read hit rects
    // (one per drawn barline) tolerant of attribute order.
    private static IEnumerable<string> HitRects(string svg) =>
        Regex.Matches(svg, "<rect class=\"nh-hit\"[^>]*/>").Cast<Match>().Select(m => m.Value);

    private static int[] ClickTargets(string svg) => HitRects(svg)
        .Select(r => int.Parse(Regex.Match(r, "data-pos=\"(\\d+)\"").Groups[1].Value)).ToArray();

    // Number of drawn barlines a caret on `pos` highlights (its data-pos, or a data-alt member).
    private static int HighlightTargets(string svg, int pos) => HitRects(svg).Count(r =>
        Regex.Match(r, "data-pos=\"(\\d+)\"").Groups[1].Value == pos.ToString()
        || Regex.Match(r, "data-alt=\"([^\"]*)\"").Groups[1].Value.Split(' ').Contains(pos.ToString()));

    [Fact]
    public void PreviewBarlinesAreClickable()
    {
        // Every drawn barline gets a widened transparent hit rect (the ink alone is
        // ~0.2 ss — too thin to click) carrying a source offset.
        var svg = SvgGenerator.Generate(SyntaxTree.Parse("""
            part m { clef treble section A { c'1 | d'1 } }
            form main { A }
            score main "s" { staff m }
            """), SvgRenderOptions.Preview());
        Assert.NotEmpty(ClickTargets(svg));
    }

    [Fact]
    public void BarlineDataPosPointsAtTheBarlineInk_NotTheSpaceBeforeIt()
    {
        // A click jumps the editor to the hit rect's data-pos; it must be the '|'
        // character's offset, not the whitespace in front of it.
        const string src = "part m { clef treble section A { c'1 | d'1 } }\n"
                         + "form main { A }\nscore main \"s\" { staff m }";
        int barPos = src.IndexOf('|', src.IndexOf("c'1")); // the mid-measure '|'
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(src), SvgRenderOptions.Preview());
        Assert.Contains(barPos, ClickTargets(svg));
    }

    [Fact]
    public void OuterBarline_IsTheClickTarget_PhraseBarStillHighlights()
    {
        // `phrase x { c1 | }` used as `x | x`: the drawn bar collapses the phrase's `|`
        // and the section `|`. A CLICK jumps to the section bar (the outer edit point);
        // a caret on EITHER the section `|` OR the phrase `|` highlights it.
        const string src = "phrase x { c1 | }\n"
                         + "part m { clef treble section A { x | x } }\n"
                         + "form main { A }\nscore main \"s\" { staff m }";
        int sectionBar = src.IndexOf('|', src.IndexOf("{ x")); // between the two x
        int phraseBar = src.IndexOf('|');                       // the phrase's own |
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(src), SvgRenderOptions.Preview());
        Assert.Contains(sectionBar, ClickTargets(svg));         // click -> section
        Assert.Equal(1, HighlightTargets(svg, sectionBar));     // section caret lights it
        Assert.True(HighlightTargets(svg, phraseBar) >= 1);     // phrase caret lights it too
    }

    [Fact]
    public void MergedRepeat_EveryContributingBarHighlights_ClickGoesToSection()
    {
        // `phrase x { |: c1 | d1 :| }` used as `x | x :|: x` collapses, at each inner
        // boundary, the phrase `:|`, the section `|`/`:|:`, and the next phrase `|:` into
        // ONE `:|:`. A caret on the phrase `|:` or `:|` lights all three call sites; a
        // caret on a section bar lights its boundary; a click on a merged bar jumps to
        // the section bar there.
        const string src = "phrase x { |: c1 | d1 :| }\n"
                         + "part m { clef treble section A { x | x :|: x } }\n"
                         + "form main { A }\nscore main \"s\" { staff m }";
        int repeatStart = src.IndexOf("|:");
        int repeatEnd = src.IndexOf(":|");
        int sectionPlain = src.IndexOf('|', src.IndexOf("{ x")); // the `|` between x1 and x2
        int sectionBoth = src.IndexOf(":|:");                    // the `:|:` between x2 and x3
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(src), SvgRenderOptions.Preview());
        Assert.Equal(3, HighlightTargets(svg, repeatStart));     // |: at all 3 sites
        Assert.Equal(3, HighlightTargets(svg, repeatEnd));       // :| at all 3 sites
        Assert.True(HighlightTargets(svg, sectionPlain) >= 1);   // section | lights its bar
        Assert.True(HighlightTargets(svg, sectionBoth) >= 1);    // section :|: lights its bar
        // Click on the merged bars jumps to the section bars.
        Assert.Contains(sectionPlain, ClickTargets(svg));
        Assert.Contains(sectionBoth, ClickTargets(svg));
    }

    [Fact]
    public void PhraseTrailingRepeatEnd_HighlightsEveryCallSite()
    {
        // In `section A { x | x | x }` all three drawn `:|` light from the phrase's `:|`
        // offset (it is a highlight alias on the merged bars, the click target on the last).
        const string src = "phrase x { |: c1 | c1 :| }\n"
                         + "part m { clef treble section A { x | x | x } }\n"
                         + "form main { A }\nscore main \"s\" { staff m }";
        int repeatEnd = src.IndexOf(":|");
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(src), SvgRenderOptions.Preview());
        Assert.Equal(3, HighlightTargets(svg, repeatEnd));
    }

    [Fact]
    public void RepeatStartBarlineCarriesItsOwnOffset()
    {
        // A `|:` opens the next measure; the drawn start barline must highlight from the
        // `|:` offset, not the previous close SourceStart otherwise holds.
        const string src = "phrase x { |: c1 :| }\n"
                         + "part m { clef treble section A { x } }\n"
                         + "form main { A }\nscore main \"s\" { staff m }";
        int repeatStart = src.IndexOf("|:");
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(src), SvgRenderOptions.Preview());
        Assert.True(HighlightTargets(svg, repeatStart) >= 1);
    }

    [Fact]
    public void StaticSvgHasNoHitRectsAndNoPointerEvents()
    {
        var svg = Render(SvgRenderOptions.Default);
        Assert.DoesNotContain("nh-hit", svg);
        Assert.DoesNotContain("pointer-events", svg);
    }

    [Fact]
    public void PreviewAccidentalIsHighlightableButNotClickable()
    {
        // A note's accidental shares the note's data-pos (for highlight) but must
        // NOT be a click target — otherwise the note's clickable area spills left
        // onto the loose accidental glyph box. It is emitted pointer-events="none"
        // (keeps data-pos), so only the notehead's nh-hit rect owns the click.
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(
            "part m { clef treble section A { cis'4 } }\nform main { A }\nscore \"s\" { staff m }"),
            SvgRenderOptions.Preview());

        // The accidental is a non-clickable music glyph that still carries data-pos.
        Assert.Matches(
            "<text class=\"music\" pointer-events=\"none\"[^>]*data-pos=\"\\d+\">",
            svg);
        // And a notehead hit rect still exists as the note's click target.
        Assert.Contains("class=\"nh-hit\"", svg);
    }
}
