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

using System.Text;
using System.Globalization;
using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg;

/// <summary>
/// Unified SVG generation from syntax tree.
/// Used by both CLI and VS Code preview to ensure identical rendering.
/// </summary>
public static class SvgGenerator
{
    /// <summary>
    /// Generates SVG from a syntax tree.
    /// </summary>
    /// <param name="tree">The parsed syntax tree</param>
    /// <param name="options">Render options (font embedding, etc.)</param>
    /// <param name="renderName">Optional render name to select specific render</param>
    /// <returns>SVG string</returns>
    public static string Generate(SyntaxTree tree, SvgRenderOptions? options = null, string? renderName = null)
    {
        options ??= SvgRenderOptions.Default;

        // Find render specification - by name if specified, otherwise first. If a
        // name is given but matches no score (e.g. a stale preview selection left
        // over after the score was renamed), fall back to the first score rather
        // than laying out an empty score, which would crash.
        var renderSpec = string.IsNullOrEmpty(renderName)
            ? RenderSpecParser.FindFirst(tree)
            : RenderSpecParser.FindByName(tree, renderName) ?? RenderSpecParser.FindFirst(tree);

        var (multiScore, layout) = BuildLayout(tree, renderSpec);
        return RenderToSvg(multiScore, layout, options);
    }

    /// <summary>
    /// Generates all render blocks as separate SVG files.
    /// Returns a list of (filename, svgContent) tuples.
    /// If only one render block exists, returns a single item with that filename.
    /// </summary>
    /// <param name="inputStem">
    /// The input file's stem (name without extension). When given, each score's
    /// filename is resolved against it (<c>main</c> → the stem; <c>score sub</c> →
    /// <c>stem-sub</c>; an explicit basename → verbatim). When null, the raw
    /// OutputFile is returned (empty for <c>main</c>) — the caller derives the name.
    /// </param>
    public static IReadOnlyList<(string Filename, string Svg)> GenerateAll(
        SyntaxTree tree, SvgRenderOptions? options = null, string? inputStem = null)
    {
        options ??= SvgRenderOptions.Default;
        var allSpecs = RenderSpecParser.FindAll(tree);

        if (allSpecs.Count == 0)
        {
            // No render blocks — fallback to default single-voice rendering
            var svg = Generate(tree, options);
            return [(string.Empty, svg)];
        }

        var results = new List<(string, string)>();
        foreach (var spec in allSpecs)
        {
            var (multiScore, layout) = BuildLayout(tree, spec);
            var svg = RenderToSvg(multiScore, layout, options);
            var filename = inputStem != null ? spec.ResolveOutputStem(inputStem) : spec.OutputFile;
            results.Add((filename, svg));
        }

        return results;
    }

    /// <summary>
    /// Generates a multi-movement combined SVG from all render blocks.
    /// Each render block becomes a movement with a title and forced page break.
    /// LILYPOND-REF: lily/book.cc — bookpart/movement grouping
    /// </summary>
    public static string GenerateMultiMovement(SyntaxTree tree, SvgRenderOptions? options = null)
    {
        options ??= SvgRenderOptions.Default;
        var allSpecs = RenderSpecParser.FindAll(tree);

        if (allSpecs.Count <= 1)
        {
            // Single or no render block — use standard rendering
            return Generate(tree, options);
        }

        // Render each movement independently and combine
        var movements = new List<(string Title, string SvgContent, double Width, double Height)>();

        foreach (var spec in allSpecs)
        {
            var (multiScore, layout) = BuildLayout(tree, spec);
            var svg = RenderToSvg(multiScore, layout, options);
            var title = System.IO.Path.GetFileNameWithoutExtension(spec.OutputFile);
            movements.Add((title, svg, layout.Width, layout.Height));
        }

        return CombineMovements(movements);
    }

    private static (MultiStaffScore Score, ScoreLayout Layout) BuildLayout(
        SyntaxTree tree, RenderSpec? renderSpec)
    {
        var multiScore = CollectScore(tree, renderSpec);
        return (multiScore, new LayoutEngine().Layout(multiScore));
    }

    /// <summary>
    /// Collects a tree to a <see cref="MultiStaffScore"/> exactly the way the
    /// render path does (single-staff scores are wrapped uniformly). Shared with
    /// <see cref="IncrementalCompiler"/> so its full and incremental paths match
    /// <see cref="Generate(SyntaxTree, SvgRenderOptions, string)"/> byte for byte.
    /// </summary>
    internal static MultiStaffScore CollectScore(SyntaxTree tree, RenderSpec? renderSpec)
        => CollectScore(new MeasureCollector { ScoreTranspose = renderSpec?.ScoreTranspose },
            tree, renderSpec);

    /// <summary>
    /// Collects into the GIVEN collector, so a caller that needs the collector's
    /// recorded side effects (the shared-collect validators read back
    /// <see cref="MeasureCollector.LyricWarnings"/> etc.) sees EXACTLY what the render
    /// path produced — attached lyrics/chords included.
    /// </summary>
    internal static MultiStaffScore CollectScore(MeasureCollector collector, SyntaxTree tree, RenderSpec? renderSpec)
    {
        if (renderSpec != null && renderSpec.IsMultiStaff)
            return collector.CollectMultiStaff(tree, renderSpec);

        // Single staff — wrap in MultiStaffScore so SharedRenderer has a uniform input.
        string? voiceName = renderSpec is { Items.Length: 1 } && renderSpec.Items[0] is SingleStaffSpec single
            ? single.Staff.VoiceName
            : null;
        var singleStaff = renderSpec is { Items.Length: 1 } && renderSpec.Items[0] is SingleStaffSpec s ? s.Staff : null;
        string? attachedChords = singleStaff?.WithChords;
        // `staff NAME with lyrics L …` on a single-staff score (explicit attach; no
        // implicit auto-attach — an unreferenced `lyrics {}` block is a LYS4006 error).
        IReadOnlyList<string>? attachedLyrics = singleStaff is { WithLyrics.IsDefaultOrEmpty: false }
            ? singleStaff.WithLyrics
            : null;
        var score = collector.Collect(tree, voiceName, renderSpec?.Form, attachedChords,
            singleStaff?.ChordDisplay ?? ChordDisplayMode.Names, attachedLyrics);

        // The single-staff wrap used to drop the spec's instrument name — it
        // lives on the Staff (first-line label + indent), so
        // `part { instrument … }` / `name …` / `staff melody "…"` printed
        // nothing on solo scores.
        string? instrumentName = renderSpec is { Items.Length: 1 }
            && renderSpec.Items[0] is SingleStaffSpec sss
                ? sss.Staff.InstrumentName
                : null;
        int staffLines = renderSpec is { Items.Length: 1 }
            && renderSpec.Items[0] is SingleStaffSpec sls ? sls.Staff.Lines : 5;
        return MultiStaffScore.FromScore(score, instrumentName, staffLines);
    }

    internal static string RenderToSvg(MultiStaffScore score, ScoreLayout layout,
        SvgRenderOptions options, bool resolveDataPos = false)
    {
        var docOptions = new SvgDocumentOptions
        {
            EmbedFont = options.EmbedFont,
            OmitFontFace = options.OmitFontFace,
            FontDirectory = options.FontDirectory,
            Interactive = options.Interactive,
        };
        using var doc = new SvgDocumentContext(docOptions);
        SharedRenderer.RenderTo(score, layout, doc, resolveDataPos);
        doc.Dispose();
        return doc.ToSvg();
    }

    /// <summary>
    /// Combines multiple movement SVGs into a single document.
    /// Extracts content from each SVG and places them vertically with spacing.
    /// </summary>
    private static string CombineMovements(
        List<(string Title, string SvgContent, double Width, double Height)> movements)
    {
        // Movement separator spacing (staff spaces)
        const double movementSpacing = 6.0;
        const double movementTitleFontSize = 2.0;
        const double movementTitleSpacing = 3.0;

        // Calculate total dimensions
        double maxWidth = 0;
        double totalHeight = 0;

        for (int i = 0; i < movements.Count; i++)
        {
            var m = movements[i];
            if (m.Width > maxWidth) maxWidth = m.Width;

            if (i > 0)
            {
                totalHeight += movementSpacing;
                totalHeight += movementTitleSpacing;
            }
            totalHeight += m.Height;
        }

        // Build combined SVG
        var sb = new StringBuilder();
        var px = (double v) => (v * 4).ToString("F1", CultureInfo.InvariantCulture);
        var n = (double v) => v.ToString("F2", CultureInfo.InvariantCulture);

        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{px(maxWidth)}" height="{px(totalHeight)}" viewBox="0 0 {n(maxWidth)} {n(totalHeight)}">""");

        // Common styles
        sb.AppendLine("<style>");
        sb.AppendLine("  .movement-title { font-family: serif; font-weight: bold; text-anchor: middle; }");
        sb.AppendLine("</style>");

        double yOffset = 0;

        for (int i = 0; i < movements.Count; i++)
        {
            var (title, svgContent, width, height) = movements[i];

            // Add movement title (except for first if the movement already has a header)
            if (i > 0)
            {
                yOffset += movementSpacing;

                // Draw movement title
                double titleX = maxWidth / 2;
                double titleY = yOffset + movementTitleFontSize;
                sb.AppendLine($"""  <text x="{n(titleX)}" y="{n(titleY)}" class="movement-title" font-size="{n(movementTitleFontSize)}">{EscapeXml(title)}</text>""");
                yOffset += movementTitleSpacing;
            }

            // Extract content between <svg> and </svg> tags
            var content = ExtractSvgContent(svgContent);
            if (!string.IsNullOrEmpty(content))
            {
                sb.AppendLine($"""  <g transform="translate(0, {n(yOffset)})">""");
                sb.AppendLine(content);
                sb.AppendLine("  </g>");
            }

            yOffset += height;
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Extracts the content between the opening &lt;svg&gt; tag and closing &lt;/svg&gt; tag.
    /// Also extracts and preserves style blocks.
    /// </summary>
    private static string ExtractSvgContent(string svg)
    {
        // Find end of opening <svg ...> tag
        int svgTagEnd = svg.IndexOf('>', svg.IndexOf("<svg"));
        if (svgTagEnd < 0) return string.Empty;

        // Find </svg>
        int svgCloseStart = svg.LastIndexOf("</svg>");
        if (svgCloseStart < 0) return string.Empty;

        return svg[(svgTagEnd + 1)..svgCloseStart].Trim();
    }

    private static string EscapeXml(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
