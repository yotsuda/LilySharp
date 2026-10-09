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
        form { Main }
        score { staff melody }
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

    // Two parts, one form, NO score — a fence that has not yet said what to draw.
    private const string TwoPartsNoScore = """
        time 4/4
        key c major
        part melody { clef treble }
        part bass { clef bass }
        section Main { melody { c4 d e f | } bass { c,1 | } }
        form tune { Main }
        """;

    /// <summary>
    /// A fence writes its score (owner decision 2026-09-10, replacing the implied score of
    /// 2026-09-09): with none it is refused, naming <c>score { }</c>, and draws nothing —
    /// the implied score silently dropped whatever a score names (lyrics, chord rows, tab)
    /// and was a second spelling of "what to draw with no score" beside the file road's.
    /// The same text OUTSIDE a fence keeps the file's fallback and still draws.
    /// </summary>
    [Fact]
    public void Fence_WithNoScore_IsRefused_NamingScore()
    {
        var fence = Server().RenderText(new RenderTextParams { Text = TwoPartsNoScore, Interactive = false, Fence = true });
        var plain = Server().RenderText(new RenderTextParams { Text = TwoPartsNoScore, Interactive = false });

        Assert.Null(fence.Svg);
        Assert.Contains("declares none", fence.Error);
        Assert.Contains("score {", fence.Error);
        Assert.Null(plain.Error);
        Assert.Contains("<svg", plain.Svg);
    }

    [Fact]
    public void Fence_WithOneScore_DrawsIt_WhateverTheFormCount()
    {
        // Two forms are no longer a question the fence has to answer: the score names its form.
        var text = TwoPartsNoScore + "\nform other { Main Main }\nscore tune { form tune staff melody staff bass }";

        var response = Server().RenderText(new RenderTextParams { Text = text, Interactive = false, Fence = true });

        Assert.Null(response.Error);
        Assert.Contains("<svg", response.Svg);
    }

    /// <summary>
    /// A fence holding a whole book — several scores — draws its DEFAULT score, the one the
    /// preview opens on: the unnamed one wherever it stands, else the first (owner decision
    /// 2026-10-09: an existing .lys pasted into a fence must not be an error).
    /// </summary>
    [Fact]
    public void Fence_WithSeveralScores_DrawsTheDefault()
    {
        // The unnamed score is declared LAST and is the one drawn; written last, so the music
        // above it keeps its source positions in both texts and the pictures compare whole.
        var book = TwoPartsNoScore + "\nscore tune { form tune staff melody }\nscore { form tune staff melody staff bass }";
        var alone = TwoPartsNoScore + "\nscore { form tune staff melody staff bass }";
        var named = TwoPartsNoScore + "\nscore tune { form tune staff melody }\nscore both { form tune staff melody staff bass }";

        var fence = Server().RenderText(new RenderTextParams { Text = book, Interactive = false, Fence = true });
        var expected = Server().RenderText(new RenderTextParams { Text = alone, Interactive = false, Fence = true });
        var first = Server().RenderText(new RenderTextParams { Text = named, Interactive = false, Fence = true });
        var firstAlone = Server().RenderText(new RenderTextParams
            { Text = TwoPartsNoScore + "\nscore tune { form tune staff melody }", Interactive = false, Fence = true });

        Assert.Null(fence.Error);
        Assert.Equal(MaskDataPos(expected.Svg!), MaskDataPos(fence.Svg!));
        // With no unnamed score the first one is drawn.
        Assert.Null(first.Error);
        Assert.Equal(MaskDataPos(firstAlone.Svg!), MaskDataPos(first.Svg!));
    }

    private static string MaskDataPos(string svg)
        => System.Text.RegularExpressions.Regex.Replace(svg, "data-pos=\"[^\"]*\"", "");

    [Fact]
    public void Fence_WithOneScore_DrawsIt_AsASnippet()
    {
        var text = TwoPartsNoScore + "\nscore tune { form tune staff bass }";

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
