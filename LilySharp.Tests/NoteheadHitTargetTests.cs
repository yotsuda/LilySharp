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

    [Fact]
    public void PreviewBarlinesAreClickable()
    {
        // A barline carries the measure boundary's source position for
        // click-to-source and caret highlighting, plus a widened transparent hit
        // rect (the ink alone is ~0.2 ss — too thin to click). Two measures, so a
        // mid-line SINGLE barline exists (the shared Doc has only its final bar).
        var svg = SvgGenerator.Generate(SyntaxTree.Parse("""
            part m { clef treble section A { c'1 | d'1 } }
            form main { A }
            score main "s" { staff m }
            """), SvgRenderOptions.Preview());
        var hits = Regex.Matches(svg,
            "<rect class=\"nh-hit\" x=\"(-?[\\d.]+)\" y=\"[-\\d.]+\" width=\"([\\d.]+)\" height=\"[\\d.]+\" fill=\"none\" pointer-events=\"all\" data-pos=\"(\\d+)\"/>");
        // Notehead hits are ~1.3 ss wide; the barline hit is the ink + 0.8 ss.
        var barHits = hits.Cast<Match>()
            .Where(h => double.Parse(h.Groups[2].Value) < 1.2).ToList();
        Assert.NotEmpty(barHits);
        // The visible barline rect right before it shares the same data-pos.
        foreach (var h in barHits)
            Assert.Contains($"data-pos=\"{h.Groups[3].Value}\"", svg);
    }

    [Fact]
    public void BarlineDataPosPointsAtTheBarlineInk_NotTheSpaceBeforeIt()
    {
        // The caret->preview highlight matches an element whose data-pos is >= the
        // caret token's INK start; a click jumps the editor to data-pos. So a
        // barline's data-pos must be the '|' character's offset, not the whitespace
        // in front of it — otherwise the highlight guard rejects it and a click
        // lands on the space.
        const string src = "part m { clef treble section A { c'1 | d'1 } }\n"
                         + "form main { A }\nscore main \"s\" { staff m }";
        int barPos = src.IndexOf('|', src.IndexOf("c'1")); // the mid-measure '|'
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(src), SvgRenderOptions.Preview());

        var barHits = Regex.Matches(svg,
                "<rect class=\"nh-hit\" x=\"-?[\\d.]+\" y=\"[-\\d.]+\" width=\"([\\d.]+)\" height=\"[-\\d.]+\" fill=\"none\" pointer-events=\"all\" data-pos=\"(\\d+)\"/>")
            .Cast<Match>().Where(h => double.Parse(h.Groups[1].Value) < 1.2).ToList();
        Assert.NotEmpty(barHits);
        Assert.Contains(barHits, h => int.Parse(h.Groups[2].Value) == barPos);
    }

    [Fact]
    public void OuterBarlineAfterAPhraseOwnsTheBarline_NotThePhrasesTrailingBar()
    {
        // `phrase x { … | }` ends with a barline; in `section A { x | x }` the OUTER
        // `|` confirms that close. The one drawn barline there is what the author edits
        // at the section level, so its data-pos must be the SECTION `|`, so a caret on
        // it highlights (before this it kept the phrase's trailing `|`, unreachable
        // from the section).
        const string src = "phrase x { c1 | }\n"
                         + "part m { clef treble section A { x | x } }\n"
                         + "form main { A }\nscore main \"s\" { staff m }";
        int sectionBar = src.IndexOf('|', src.IndexOf("{ x")); // the `|` between the two x
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(src), SvgRenderOptions.Preview());
        var barHits = Regex.Matches(svg,
                "<rect class=\"nh-hit\" x=\"-?[\\d.]+\" y=\"[-\\d.]+\" width=\"([\\d.]+)\" height=\"[-\\d.]+\" fill=\"none\" pointer-events=\"all\" data-pos=\"(\\d+)\"/>")
            .Cast<Match>().Where(h => double.Parse(h.Groups[1].Value) < 1.2).ToList();
        Assert.Contains(barHits, h => int.Parse(h.Groups[2].Value) == sectionBar);
    }

    [Fact]
    public void PhraseTrailingRepeatEnd_HighlightsEveryCallSite_NotJustTheLast()
    {
        // A phrase's trailing `:|` is a MEANINGFUL barline (not a plain bar the section's
        // `|` can stand in for), so it keeps its own offset at every call site. In
        // `section A { x | x | x }` all three drawn `:|` share the phrase's `:|` offset,
        // so a caret on it lights all three (before this the plain-`|` retarget scattered
        // them onto the section bars, leaving only the last copy).
        const string src = "phrase x { |: c1 | c1 :| }\n"
                         + "part m { clef treble section A { x | x | x } }\n"
                         + "form main { A }\nscore main \"s\" { staff m }";
        int repeatEnd = src.IndexOf(":|");
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(src), SvgRenderOptions.Preview());
        int copies = Regex.Matches(svg,
            "<rect class=\"nh-hit\"[^>]* pointer-events=\"all\" data-pos=\"" + repeatEnd + "\"/>").Count;
        Assert.Equal(3, copies);
    }

    [Fact]
    public void RepeatStartBarlineCarriesItsOwnOffset()
    {
        // A `|:` opens the next measure; the drawn start barline must carry the `|:`
        // offset (so a caret on it highlights), not the previous measure's close, which
        // is what SourceStart otherwise holds.
        const string src = "phrase x { |: c1 :| }\n"
                         + "part m { clef treble section A { x } }\n"
                         + "form main { A }\nscore main \"s\" { staff m }";
        int repeatStart = src.IndexOf("|:");
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(src), SvgRenderOptions.Preview());
        // The `|:` hit rect carries the repeat-start offset (its ink is wider than a
        // plain bar, so match any hit — a notehead never shares this offset).
        var offsets = Regex.Matches(svg,
                "<rect class=\"nh-hit\"[^>]* pointer-events=\"all\" data-pos=\"(\\d+)\"/>")
            .Cast<Match>().Select(h => int.Parse(h.Groups[1].Value)).ToList();
        Assert.Contains(repeatStart, offsets);
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
