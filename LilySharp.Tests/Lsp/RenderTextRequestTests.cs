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

using System.IO;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The <c>lilysharp/renderText</c> request's two pictures (HANDOFF §2F F-mdfence): the
/// AI panel's interactive one (the default) and the Markdown fence's static one. Both
/// leave the fonts to the host page.
/// </summary>
public class RenderTextRequestTests
{
    private static LilySharpLanguageServer Server() => new(Stream.Null, Stream.Null);

    private const string Doc = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody { c4 d e f | g1 | } }
        form main { Main }
        score main { staff melody }
        """;

    [Fact]
    public void ByDefault_TheDrawingIsInteractive()
    {
        var response = Server().RenderText(new RenderTextParams { Text = Doc });

        Assert.Null(response.Error);
        Assert.Contains("nh-hit", response.Svg);
        Assert.DoesNotContain("@font-face", response.Svg);
    }

    [Fact]
    public void Static_HasTheSameLayout_AndNoClickTargets()
    {
        var interactive = Server().RenderText(new RenderTextParams { Text = Doc });
        var response = Server().RenderText(new RenderTextParams { Text = Doc, Interactive = false });

        Assert.Null(response.Error);
        Assert.StartsWith("<?xml", response.Svg);
        Assert.Contains("<svg", response.Svg);
        Assert.DoesNotContain("nh-hit", response.Svg);
        // data-pos (the source position of every glyph) is part of every SVG the
        // engine writes, export included — it is an annotation, not a click target.
        Assert.DoesNotContain("@font-face", response.Svg);
        // The page is the same size either way: the hit-rects are overlays, not layout.
        Assert.Equal(ViewBox(interactive.Svg!), ViewBox(response.Svg!));
    }

    // Two parts, one form, NO score — what a Markdown fence is expected to write.
    private const string TwoPartsNoScore = """
        time 4/4
        key c major
        part melody { clef treble }
        part bass { clef bass }
        section Main { melody { c4 d e f | } bass { c,1 | } }
        form tune { Main }
        """;

    [Fact]
    public void Fence_WithNoScore_DrawsEveryPart_AsTheExplicitScoreWould()
    {
        var implied = Server().RenderText(new RenderTextParams { Text = TwoPartsNoScore, Interactive = false, Fence = true });
        var explicitScore = Server().RenderText(new RenderTextParams
        {
            Text = TwoPartsNoScore + "\nscore tune { staff melody staff bass }",
            Interactive = false, Fence = true,
        });

        Assert.Null(implied.Error);
        Assert.Null(explicitScore.Error);
        Assert.Equal(explicitScore.Svg, implied.Svg);
        // Two staves, not the first part alone (what the same text draws OUTSIDE a fence).
        var plain = Server().RenderText(new RenderTextParams { Text = TwoPartsNoScore, Interactive = false });
        Assert.NotEqual(plain.Svg, implied.Svg);
    }

    [Fact]
    public void Fence_WithNoScore_AndNoForm_StillDraws()
    {
        var text = TwoPartsNoScore.Replace("form tune { Main }", "");

        var response = Server().RenderText(new RenderTextParams { Text = text, Interactive = false, Fence = true });

        Assert.Null(response.Error);
        Assert.Contains("<svg", response.Svg);
    }

    [Fact]
    public void Fence_WithNoScore_AndTwoForms_IsRefused()
    {
        var text = TwoPartsNoScore + "\nform other { Main Main }";

        var response = Server().RenderText(new RenderTextParams { Text = text, Interactive = false, Fence = true });

        Assert.Null(response.Svg);
        Assert.Contains("tune, other", response.Error);
    }

    [Fact]
    public void Fence_WithTwoScores_IsRefused()
    {
        var text = TwoPartsNoScore + "\nscore tune { staff melody }\nscore tune \"both\" { staff melody staff bass }";

        var response = Server().RenderText(new RenderTextParams { Text = text, Interactive = false, Fence = true });

        Assert.Null(response.Svg);
        Assert.Contains("declares 2", response.Error);
    }

    [Fact]
    public void Fence_WithOneScore_DrawsIt_AsASnippet()
    {
        var text = TwoPartsNoScore + "\nscore tune { staff bass }";

        var fence = Server().RenderText(new RenderTextParams { Text = text, Interactive = false, Fence = true });
        var plain = Server().RenderText(new RenderTextParams { Text = text, Interactive = false });

        Assert.Null(fence.Error);
        Assert.NotEqual(plain.Svg, fence.Svg);
        // The fence's page is cropped to its one system; the plain drawing is the paper.
        Assert.True(ViewBoxWidth(fence.Svg!) < ViewBoxWidth(plain.Svg!),
            $"fence {ViewBox(fence.Svg!)} against paper {ViewBox(plain.Svg!)}");
        Assert.StartsWith("viewBox=\"0 0 119.50", ViewBox(plain.Svg!));
    }

    private static double ViewBoxWidth(string svg)
    {
        var parts = ViewBox(svg)["viewBox=\"".Length..].Split(' ');
        return double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ViewBox(string svg)
    {
        int at = svg.IndexOf("viewBox=\"", System.StringComparison.Ordinal);
        Assert.True(at >= 0, "no viewBox");
        int end = svg.IndexOf('"', at + 9);
        return svg.Substring(at, end - at);
    }
}
