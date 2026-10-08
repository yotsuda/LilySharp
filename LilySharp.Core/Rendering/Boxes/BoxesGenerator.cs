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
/// spaces, origin top-left, Y down — an SVG unit, and <c>10 × --scale</c> PNG pixels (times the
/// staff space's ratio to LilyPond's under <c>--set staffSpace=…</c>, <see cref="ToJson"/>).
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
        => GenerateDocument(tree, renderSpec, settings, layoutWarning, fontDirectory).Pages;

    /// <summary>The pages of one score and the music font they were drawn in.</summary>
    public static BoxDocument GenerateDocument(SyntaxTree tree, RenderSpec? renderSpec,
        Semantics.PaperOverrides? settings = null, Action<string>? layoutWarning = null,
        string? fontDirectory = null)
    {
        var score = SvgGenerator.CollectScore(tree, renderSpec, settings: settings);
        var layout = new LayoutEngine(score.Paper).Layout(score);
        LayoutWarnings.Report(layout, score.Paper, layoutWarning);
        using var doc = new BoxesDocumentContext(fontDirectory ?? FontLocator.Find());
        using (var fallbacks = MusicFallbackLog.Open())
        {
            SharedRenderer.RenderTo(score, layout, doc);
            fallbacks.Report(layoutWarning);
        }
        return new BoxDocument(doc.Pages, doc.MusicFont);
    }

    /// <summary>The document as the JSON <c>lysc boxes</c> writes — the pages and, first, the
    /// music font (<c>musicFont</c>): a reader training on several fonts' pictures splits its
    /// data by it.</summary>
    /// <param name="document">The document.</param>
    /// <param name="staffSpaceMm">As <see cref="ToJson(IReadOnlyList{BoxPage}, double)"/>.</param>
    public static string ToJson(BoxDocument document, double staffSpaceMm = LayoutOptions.DefaultStaffSpaceMm)
        => JsonSerializer.Serialize(new
        {
            version = FormatVersion, unit = "staffSpace", staffSpaceMm = Math.Round(staffSpaceMm, 6),
            musicFont = document.MusicFont, pages = document.Pages,
        }, Json);

    /// <summary>The pages as the JSON document <c>lysc boxes</c> writes.</summary>
    /// <param name="pages">The pages.</param>
    /// <param name="staffSpaceMm">The staff space on the paper (<c>--set staffSpace=…</c>), written
    /// as <c>staffSpaceMm</c>: a PNG of the same settings has <c>10 × --scale × staffSpaceMm /
    /// 1.757299</c> pixels to the unit, since the paper keeps its size.</param>
    public static string ToJson(IReadOnlyList<BoxPage> pages, double staffSpaceMm = LayoutOptions.DefaultStaffSpaceMm)
        => JsonSerializer.Serialize(new
        {
            version = FormatVersion, unit = "staffSpace", staffSpaceMm = Math.Round(staffSpaceMm, 6), pages,
        }, Json);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
