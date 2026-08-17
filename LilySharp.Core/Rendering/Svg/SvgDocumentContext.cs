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

using System.Globalization;
using System.Linq;
using System.Text;

namespace LilySharp.Core.Rendering.Svg;

/// <summary>Configuration for <see cref="SvgDocumentContext"/>.</summary>
internal sealed class SvgDocumentOptions
{
    /// <summary>Pixels per staff-space when emitting outer width/height.</summary>
    public double PixelsPerSpace { get; init; } = 10.0;

    /// <summary>If true, embed Emmentaler as base64 WOFF2; otherwise reference by name.</summary>
    public bool EmbedFont { get; init; } = true;

    /// <summary>
    /// If true, skip the <c>@font-face</c> rule entirely. Used by the VS Code
    /// preview path where the host page injects Emmentaler from a known URL.
    /// </summary>
    public bool OmitFontFace { get; init; }

    /// <summary>Optional override for the font directory.</summary>
    public string? FontDirectory { get; init; }

    /// <summary>
    /// If true, pages emit interactive click targets (tight per-notehead hit
    /// rectangles). Used by the VS Code preview; off for static export.
    /// </summary>
    public bool Interactive { get; init; }
}

/// <summary>
/// SVG output. A single-page document is emitted as one <c>&lt;svg&gt;</c>.
/// Multi-page documents are stacked vertically inside one root <c>&lt;svg&gt;</c>
/// sized to hold every page: each page's content is wrapped in a
/// <c>&lt;g transform="translate(0, y)"&gt;</c> at its cumulative Y offset, so
/// pages no longer overlap and nothing is clipped by the first page's height.
/// </summary>
internal sealed class SvgDocumentContext : IDocumentContext
{
    private readonly SvgDocumentOptions _options;
    private readonly List<(StringBuilder Content, double Width, double Height)> _pages = new();
    private StringBuilder? _currentContent;
    private double _currentWidth, _currentHeight;
    private SvgDrawingContext? _currentPage;
    private string? _result;
    private bool _disposed;

    /// <summary>
    /// The Emmentaler designs the pages actually drew glyphs from. The header is assembled
    /// AFTER every page (see <see cref="Assemble"/>), so "embed the faces this score uses"
    /// is a set the pages fill in as they go — the alternative, always writing all eight
    /// <c>@font-face</c> rules, would put ~400 KB of base64 into every SVG.
    /// </summary>
    private readonly HashSet<int> _usedDesigns = new();

    public SvgDocumentContext(SvgDocumentOptions? options = null)
    {
        _options = options ?? new SvgDocumentOptions();
    }

    /// <summary>The open page's text buffer — the append target
    /// <see cref="SvgSystemFragmentCache"/> replays into and measures captures
    /// against. Null between pages.</summary>
    internal StringBuilder? CurrentContent => _currentContent;

    /// <summary>The open page's drawing context — carries the capture hooks
    /// (<see cref="SvgDrawingContext.SourceLog"/>). Null between pages.</summary>
    internal SvgDrawingContext? CurrentPage => _currentPage;

    /// <summary>The used-design set (see <see cref="_usedDesigns"/>): a replayed
    /// fragment merges its recorded designs here, since the draw that would have
    /// recorded them does not run.</summary>
    internal HashSet<int> UsedDesigns => _usedDesigns;

    /// <inheritdoc/>
    public TextFontPlan Fonts { get; set; } = TextFontPlan.Default;

    public IDrawingContext BeginPage(double widthSpaces, double heightSpaces)
    {
        if (_currentPage != null)
            throw new InvalidOperationException("Previous page not ended.");
        // Buffer each page separately so we can size the root <svg> to all pages
        // and offset them at Dispose (we don't know the total height up front).
        _currentContent = new StringBuilder();
        _currentWidth = widthSpaces;
        _currentHeight = heightSpaces;
        _currentPage = new SvgDrawingContext(_currentContent, _options.Interactive, _usedDesigns, Fonts);
        return _currentPage;
    }

    public void EndPage()
    {
        if (_currentPage == null || _currentContent == null)
            throw new InvalidOperationException("No page to end.");
        _pages.Add((_currentContent, _currentWidth, _currentHeight));
        _currentPage = null;
        _currentContent = null;
    }

    /// <summary>Returns the assembled SVG text. Call after <see cref="Dispose"/>.</summary>
    public string ToSvg()
    {
        if (!_disposed)
            throw new InvalidOperationException("Dispose the document before reading SVG.");
        return _result ?? "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_currentPage != null) EndPage();
        _result = Assemble();
        _disposed = true;
    }

    private string Assemble()
    {
        if (_pages.Count == 0)
            return "";

        // ⚠️ THE PAGE BODIES ARE COPIED ONCE, INTO THE RESULT, and on a long score that is
        // the whole cost of this method. The bodies are already built (EndPage keeps each
        // page's builder), so appending them to ANOTHER StringBuilder and calling ToString
        // pays for the document twice: once in the second builder's chunks and once in the
        // string. MEASURED (session 191, keystroke allocation): Assemble was 7.2 MB of
        // perf-plain1k's 66.3 MB keystroke for a 3.76 MB document — two copies where one is
        // the floor this method's string return imposes. Writing straight into the result
        // pays that floor and nothing else.
        // ⚠️ EVERY PIECE MUST MATCH WHAT THE BUILDER WROTE, CHARACTER FOR CHARACTER,
        // including AppendLine's Environment.NewLine. The 566-book SVG A/B is what checks
        // that, and it is not optional for a change of this shape.
        string nl = Environment.NewLine;

        // The document as a flat list of pieces, in emission order. A piece is either a
        // literal (the header, the wrappers) or one page's body, and the list is the whole
        // spelling of the document's shape — there is no second code path to keep in step.
        var pieces = new List<Piece>(_pages.Count * 3 + 2);

        // Single page: emit exactly as a one-page document (no <g> wrapper) so the
        // output is byte-identical to the common case.
        if (_pages.Count == 1)
        {
            var (body, w, h) = _pages[0];
            var head1 = new StringBuilder(512);
            WriteHeader(head1, w, h);
            pieces.Add(Piece.Of(head1));
            pieces.Add(Piece.Of(body));
            pieces.Add(Piece.Of("</svg>" + nl));
            return Compose(pieces);
        }

        double totalHeight = 0, maxWidth = 0;
        foreach (var (_, w, h) in _pages)
        {
            totalHeight += h;
            if (w > maxWidth) maxWidth = w;
        }

        var head = new StringBuilder(512);
        WriteHeader(head, maxWidth, totalHeight);
        pieces.Add(Piece.Of(head));

        double yOffset = 0;
        foreach (var (body, _, h) in _pages)
        {
            // class/data attributes let a viewer (the VS Code preview toolbar)
            // find the page boundaries for fit-page zoom and page navigation.
            pieces.Add(Piece.Of(string.Format(CultureInfo.InvariantCulture,
                "<g class=\"page\" data-page-top=\"{0:F2}\" data-page-height=\"{1:F2}\" transform=\"translate(0, {0:F2})\">",
                yOffset, h) + nl));
            pieces.Add(Piece.Of(body));
            pieces.Add(Piece.Of("</g>" + nl));
            yOffset += h;
        }
        pieces.Add(Piece.Of("</svg>" + nl));
        return Compose(pieces);
    }

    /// <summary>One span of the finished document: a literal, or a page body still in its
    /// builder. Exactly one of the two is set.</summary>
    private readonly struct Piece
    {
        private readonly string? _text;
        private readonly StringBuilder? _body;
        private Piece(string? text, StringBuilder? body) { _text = text; _body = body; }
        public static Piece Of(string text) => new(text, null);
        public static Piece Of(StringBuilder body) => new(null, body);
        public int Length => _text?.Length ?? _body!.Length;
        public void CopyTo(Span<char> at)
        {
            if (_text != null) _text.AsSpan().CopyTo(at);
            else _body!.CopyTo(0, at, _body.Length);
        }
    }

    /// <summary>Joins the pieces, allocating exactly the result and nothing else.</summary>
    private static string Compose(List<Piece> pieces)
    {
        int total = 0;
        foreach (var p in pieces) total += p.Length;
        return string.Create(total, pieces, static (span, ps) =>
        {
            int at = 0;
            foreach (var p in ps)
            {
                p.CopyTo(span[at..]);
                at += p.Length;
            }
        });
    }

    private void WriteHeader(StringBuilder sb, double widthSpaces, double heightSpaces)
    {
        double widthPx = widthSpaces * _options.PixelsPerSpace;
        double heightPx = heightSpaces * _options.PixelsPerSpace;

        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        // The root font-family is inherited by every text: non-music text (title, lyrics,
        // fret digits, …) omits its own attribute and picks this up, while the ".music"
        // CSS class overrides it for glyphs and a custom `font "NAME"` overrides it per
        // element. Saves repeating it on hundreds of texts.
        //
        // It NAMES THE BUNDLED FACE, with the generic left as the fallback. The engine
        // reserves space with TextFontMetrics, which measures TeX Gyre Schola — LilyPond's
        // own text face by metrics — so a viewer holding that font now draws exactly what
        // was spaced for. ⚠️ The bare generic used to be a silent mismatch: measured on a
        // stock Windows box the CSS generic "serif" resolves to Segoe UI, a SANS face.
        // ⚠️ REMAINING GAP: unlike Emmentaler the text faces are NOT embedded here, so a
        // viewer without Schola still falls back. Embedding them would add ~185 KB of
        // base64 PER FACE to every SVG, which needs the used-face tracking this context
        // does not have (the header is written before any text is drawn). PNG and PDF are
        // exact today; SVG records the intent and degrades to the generic.
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0:F1}\" height=\"{1:F1}\" viewBox=\"0 0 {2:F2} {3:F2}\" font-family=\"{4}, serif\">",
            widthPx, heightPx, widthSpaces, heightSpaces, TextFontMetrics.SerifFamily));
        sb.AppendLine("<style>");
        var fontFaceRule = GetFontFaceRule();
        if (!string.IsNullOrEmpty(fontFaceRule))
            sb.AppendLine("  " + fontFaceRule);
        sb.AppendLine("  .music { font-family: 'Emmentaler', serif; }");
        sb.AppendLine("</style>");
        WritePageBackground(sb, widthSpaces, heightSpaces);
    }

    /// <summary>
    /// The page itself — an opaque rectangle behind every mark, the first element in the
    /// document.
    /// </summary>
    /// <remarks>
    /// Not decoration: it is what the OCCLUDING marks have to match. A tab fret digit and a
    /// section mark are drawn on a <c>fill="#FFFFFF"</c> box that hides the string line (or
    /// the staff) behind them, and that box is only invisible when the page under it is the
    /// same colour. Without a page rect the SVG's background is TRANSPARENT, so the two were
    /// never the same thing and only looked it — against a white viewer chrome.
    /// <para>
    /// ⚠️ THE CASE THAT PROVED IT, and the reason this is the right fix rather than "force
    /// white": editors/vscode/src/extension.ts:1498 renders the preview under
    /// <c>filter: invert(1) hue-rotate(180deg)</c> when the VS Code theme is dark. A filter
    /// applies to the whole image UNIFORMLY, so page and box invert together and stay equal —
    /// but only if the page is part of the image. Transparency is not a colour and does not
    /// invert, so the box alone went black and every fret number sat in a hole.
    /// ⇒ The invariant to keep is not "the background is white", it is
    /// <b>THE PAGE AND ITS OCCLUDERS ARE THE SAME PAINT</b>. That survives any theme, any
    /// filter, and any viewer, and it also makes the SVG agree with the PDF and PNG backends,
    /// whose pages have always been opaque.
    /// </para>
    /// </remarks>
    private static void WritePageBackground(StringBuilder sb, double widthSpaces, double heightSpaces)
        => sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "  <rect x=\"0\" y=\"0\" width=\"{0:F2}\" height=\"{1:F2}\" fill=\"{2}\"/>",
            widthSpaces, heightSpaces, Color.White.ToHex()));

    private string GetFontFaceRule()
    {
        // Preview mode: host page (VS Code webview, browser) injects the fonts.
        if (_options.OmitFontFace)
            return "";
        if (_options.EmbedFont)
        {
            // Embed BOTH the main notation font and the SEPARATE brace font
            // (Emmentaler-Brace, used for grand-staff/group braces). Without the
            // brace face the brace glyph renders blank in any viewer that lacks
            // the font installed (the bug that hid it in the VS Code preview).
            var faces = new StringBuilder();
            AppendEmbeddedFontFace(faces, "Emmentaler",
                EmmentalerFaces.Woff2File(EmmentalerFaces.DefaultDesign), "woff2");
            AppendEmbeddedFontFace(faces, "Emmentaler-Brace", "emmentaler-brace.woff", "woff");
            // …plus one face per OTHER design this score drew from. Emmentaler is optically
            // sized, so a grace is the 14 design's own outlines, not the 20's scaled down —
            // without its face the viewer would draw the small glyphs from the 20 and stop
            // matching the boxes the layout reserved (GlyphMetrics.AtFontSize).
            // Sorted so the same score always produces the same bytes.
            foreach (var design in _usedDesigns.Where(d => d != EmmentalerFaces.DefaultDesign)
                                               .OrderBy(d => d))
                AppendEmbeddedFontFace(faces, EmmentalerFaces.Family(design),
                    EmmentalerFaces.Woff2File(design), "woff2");
            if (faces.Length > 0)
                return faces.ToString().TrimEnd();
        }
        var local = new StringBuilder("@font-face { font-family: 'Emmentaler'; src: local('Emmentaler'); }");
        foreach (var design in _usedDesigns.Where(d => d != EmmentalerFaces.DefaultDesign)
                                           .OrderBy(d => d))
        {
            var family = EmmentalerFaces.Family(design);
            local.AppendLine();
            local.Append(CultureInfo.InvariantCulture,
                $"  @font-face {{ font-family: '{family}'; src: local('{family}'); }}");
        }
        return local.ToString();
    }

    private void AppendEmbeddedFontFace(StringBuilder sb, string family, string fileName, string format)
    {
        var path = ResolveFontPath(fileName);
        if (path == null || !File.Exists(path))
            return;
        var b64 = Convert.ToBase64String(File.ReadAllBytes(path));
        sb.AppendLine(
            $"@font-face {{ font-family: '{family}'; src: url('data:font/{format};base64,{b64}') format('{format}'); }}");
    }

    private string? ResolveFontPath(string fileName) =>
        FontLocator.ResolveFile(fileName, _options.FontDirectory);
}
