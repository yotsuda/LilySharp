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

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Rendering.Boxes;

/// <summary>
/// <c>lysc boxes</c>: every symbol of a score with the box its ink fills, page by page, as
/// JSON (LilySharp-Omr's proposal of 2026-10-02, P5).
/// </summary>
/// <remarks>
/// The same collection, layout and renderer as the SVG and the PNG (and the same
/// <c>--set</c> settings), so the boxes are those pictures' symbols: page coordinates in staff
/// spaces, origin top-left, Y down — an SVG unit, and <c>10 × --scale</c> PNG pixels.
/// </remarks>
public static class BoxesGenerator
{
    /// <summary>The format's version, written first: a reader checks it before trusting the
    /// fields.</summary>
    public const int FormatVersion = 1;

    /// <summary>The pages of one score (null = a file with no <c>score</c> block).</summary>
    public static IReadOnlyList<BoxPage> GeneratePages(SyntaxTree tree, RenderSpec? renderSpec,
        Semantics.PaperOverrides? settings = null, Action<string>? layoutWarning = null,
        string? fontDirectory = null)
    {
        var score = SvgGenerator.CollectScore(tree, renderSpec, settings: settings);
        var layout = new LayoutEngine(score.Paper).Layout(score);
        LayoutWarnings.Report(layout, score.Paper, layoutWarning);
        using var doc = new BoxesDocumentContext(fontDirectory ?? FontLocator.Find());
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Pages;
    }

    /// <summary>The pages as the JSON document <c>lysc boxes</c> writes.</summary>
    public static string ToJson(IReadOnlyList<BoxPage> pages)
        => JsonSerializer.Serialize(new { version = FormatVersion, unit = "staffSpace", pages }, Json);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
