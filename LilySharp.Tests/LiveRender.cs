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

using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Svg;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// Renders SVG through the LIVE production path
/// (MeasureCollector → LayoutEngine → SharedRenderer → SvgDocumentContext),
/// mirroring <see cref="SvgGenerator"/>. Tests should use this instead of
/// constructing the legacy <c>SvgRenderer</c>, so that assertions exercise
/// the code users actually run.
/// </summary>
public static class LiveRender
{
    /// <summary>Render a single-voice source (optionally a named voice).</summary>
    public static string Svg(string source, string? voiceName = null)
    {
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree, voiceName);
        var multi = MultiStaffScore.FromScore(score);
        var layout = new LayoutEngine().Layout(multi);
        return Render(multi, layout);
    }

    /// <summary>
    /// Render via the source's render block (multi-staff aware), exactly like
    /// <c>SvgGenerator.Generate</c> with font embedding disabled.
    /// </summary>
    public static string SvgFromRenderSpec(string source)
        => SvgGenerator.Generate(
            SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    private static string Render(MultiStaffScore score, ScoreLayout layout)
    {
        using var doc = new SvgDocumentContext(new SvgDocumentOptions { EmbedFont = false });
        SharedRenderer.RenderTo(score, layout, doc);
        doc.Dispose();
        return doc.ToSvg();
    }
}
