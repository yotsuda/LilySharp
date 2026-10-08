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
using LilySharp.Core.Rendering.Pdf;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Pdf;

/// <summary>
/// Unified PDF generation from syntax tree.
/// Mirrors SvgGenerator but outputs PDF bytes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/cairo.cc - Cairo-based PDF backend
/// </remarks>
public static class PdfGenerator
{
    /// <summary>
    /// Generates PDF from a syntax tree.
    /// </summary>
    /// <param name="tree">The parsed syntax tree</param>
    /// <param name="options">PDF render options (page size, staff scale, etc.)</param>
    /// <param name="renderName">Optional render name to select specific render</param>
    /// <returns>PDF document as byte array</returns>
    public static byte[] Generate(SyntaxTree tree, PdfRenderOptions? options = null, string? renderName = null)
    {
        // The score a name picks: Choose's policy (a match, else the FIRST score), the one
        // every door resolves with (PngGenerator.Generate says what FindByName did here).
        var renderSpec = RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), renderName);
        return GenerateScore(tree, renderSpec, options);
    }

    /// <summary>The PDF of one given score (null = a file with no <c>score</c> block) —
    /// <see cref="SvgGenerator.GenerateScore"/> says why a name is not always enough.</summary>
    public static byte[] GenerateScore(SyntaxTree tree, RenderSpec? renderSpec, PdfRenderOptions? options = null)
    {
        options ??= PdfRenderOptions.Default;

        // ONE collection path for every output format (see PngGenerator):
        // the hand-copied variant here silently missed score transpose and the
        // `with chords` attachment.
        MultiStaffScore multiScore = SvgGenerator.CollectScore(tree, renderSpec, settings: options.PaperOverrides);
        ScoreLayout layout = new LayoutEngine(multiScore.Paper).Layout(multiScore);
        LayoutWarnings.Report(layout, multiScore.Paper, options.LayoutWarning);

        var docOptions = new PdfDocumentOptions
        {
            // The paper keeps its size on another staff space (`--set staffSpace=…`).
            PointsPerSpace = options.StaffSpacePt * multiScore.Paper.StaffSpaceScale,
            AutoSizePages = true,
            FontDirectory = options.FontDirectory,
            // The `font` directive travels ON THE SCORE and reaches the document through
            // SharedRenderer.RenderTo (IDocumentContext.Fonts), which is also what
            // configures the resolver's embed set — so there is nothing to copy here.
        };
        using var doc = new PdfDocumentContext(docOptions);
        using (var fallbacks = MusicFallbackLog.Open())
        {
            SharedRenderer.RenderTo(multiScore, layout, doc);
            fallbacks.Report(options.LayoutWarning);
        }
        doc.Dispose();
        return doc.GetBytes();
    }
}
