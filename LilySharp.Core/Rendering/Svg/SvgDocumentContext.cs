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

using System.Collections.Immutable;
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
    private string? _result;   // the one-string document, assembled on first read
    private bool _disposed;
    /// <summary>The session's page buffers (<see cref="SvgPageBuffers"/>), or null for a
    /// caller with no session behind it — which is every one-shot render, and which is
    /// exactly today's behaviour: a fresh builder per page, dropped with the document.</summary>
    private readonly SvgPageBuffers? _buffers;
    private bool _released;

    /// <summary>
    /// The Emmentaler designs the pages actually drew glyphs from. The header is assembled
    /// AFTER every page (see <see cref="Assemble"/>), so "embed the faces this score uses"
    /// is a set the pages fill in as they go — the alternative, always writing all eight
    /// <c>@font-face</c> rules, would put ~400 KB of base64 into every SVG.
    /// </summary>
    private readonly HashSet<int> _usedDesigns = new();

    public SvgDocumentContext(SvgDocumentOptions? options = null, SvgPageBuffers? buffers = null)
    {
        _options = options ?? new SvgDocumentOptions();
        _buffers = buffers;
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

    /// <inheritdoc/>
    /// <remarks>Interactive only — the export SVG stays in page coordinates.</remarks>
    public bool SystemLocalFrames => _options.Interactive;

    public IDrawingContext BeginPage(double widthSpaces, double heightSpaces)
    {
        if (_currentPage != null)
            throw new InvalidOperationException("Previous page not ended.");
        // Buffer each page separately so we can size the root <svg> to all pages
        // and offset them at Dispose (we don't know the total height up front).
        // The session (if there is one) lends this page the buffer it drew into last
        // render — already cleared, already the size this page came out at — which is
        // what keeps a keystroke from allocating the whole document again (SvgPageBuffers).
        _currentContent = _buffers?.Take(_pages.Count) ?? new StringBuilder();
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
        if (_result != null)
            return _result;
        ThrowIfReleased();
        return _result = Assemble();
    }

    /// <summary>
    /// Hands every page's buffer back to the session pool it came from
    /// (<see cref="SvgPageBuffers"/>), and forgets the pages. THE DOCUMENT IS NOT READABLE
    /// AFTERWARDS — the buffers belong to the next render from this point on — so only the
    /// owner of the pool calls this, and only once it has taken what it wanted:
    /// <see cref="ToSvg"/>'s string (which is cached, so a later call still answers) or
    /// <see cref="ToPages"/>'s materialized set. A document with no pool is untouched.
    /// </summary>
    internal void ReleaseBuffers()
    {
        if (_buffers == null || _released)
            return;
        _released = true;
        for (int i = 0; i < _pages.Count; i++)
            _buffers.Give(i, _pages[i].Content);
        _pages.Clear();
        _currentContent = null;
        _currentPage = null;
    }

    /// <summary>The pages are gone (<see cref="ReleaseBuffers"/>) and the answer was not
    /// taken while they were here. Says so rather than returning an empty document.</summary>
    private void ThrowIfReleased()
    {
        if (_released)
            throw new InvalidOperationException(
                "The page buffers were released; read the document before releasing it.");
    }

    /// <summary>
    /// The same document as pages (<see cref="SvgPageSet"/> — its remarks say what a page
    /// is), each classified against <paramref name="previous"/>, the session's previous
    /// render, with <paramref name="window"/> the edit between the two renders' texts. A page
    /// equal to its predecessor is the predecessor's string (no copy is made of it); the
    /// others are materialized once. Call after <see cref="Dispose"/>.
    /// </summary>
    public SvgPageSet ToPages(SvgPageSet? previous, SvgEditWindow window)
    {
        if (!_disposed)
            throw new InvalidOperationException("Dispose the document before reading SVG.");
        ThrowIfReleased();
        var (head, pages, tail) = BuildPieces();
        int count = pages.Count;
        var texts = ImmutableArray.CreateBuilder<string>(count);
        var changes = ImmutableArray.CreateBuilder<SvgPageChange>(count);
        // A previous set of another page count compares nowhere: every page is Changed and
        // carries its text (the viewer replaces the whole document from them).
        bool compare = previous != null && previous.Pages.Length == count;
        bool noEdit = window.Delta == 0 && window.Prefix >= window.SuffixStart;
        for (int i = 0; i < count; i++)
        {
            var page = pages[i];
            string? was = compare ? previous!.Pages[i] : null;
            if (was != null && page.Equals(was))
            {
                texts.Add(was);
                changes.Add(SvgPageChange.Same);
                continue;
            }
            string text = page.Materialize();
            texts.Add(text);
            changes.Add(was != null && !noEdit && SvgPageSet.SameModuloWindow(was, text, window)
                ? SvgPageChange.Shifted
                : SvgPageChange.Changed);
        }
        return new SvgPageSet(head, texts.MoveToImmutable(), changes.MoveToImmutable(), tail, window);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_currentPage != null) EndPage();
        _disposed = true;
    }

    private string Assemble()
    {
        var (head, pages, tail) = BuildPieces();
        if (pages.Count == 0)
            return "";

        // ⚠️ THE PAGE BODIES ARE COPIED ONCE, INTO THE RESULT, and on a long score that is
        // the whole cost of this method. The bodies are already built (EndPage keeps each
        // page's builder), so appending them to ANOTHER StringBuilder and calling ToString
        // pays for the document twice: once in the second builder's chunks and once in the
        // string. MEASURED (session 191, keystroke allocation): Assemble was 7.2 MB of
        // perf-plain1k's 66.3 MB keystroke for a 3.76 MB document — two copies where one is
        // the floor this method's string return imposes. Writing straight into the result
        // pays that floor and nothing else.
        int total = head.Length + tail.Length;
        foreach (var p in pages) total += p.Length;
        return string.Create(total, (head, pages, tail), static (span, doc) =>
        {
            int at = 0;
            doc.head.AsSpan().CopyTo(span);
            at += doc.head.Length;
            foreach (var p in doc.pages)
            {
                p.CopyTo(span[at..]);
                at += p.Length;
            }
            doc.tail.AsSpan().CopyTo(span[at..]);
        });
    }

    /// <summary>One page of the finished document: its wrapper's opening tag (empty when the
    /// page is not wrapped), its body still in the builder, and the closing tag.</summary>
    private readonly struct PagePieces
    {
        public readonly string Open;
        public readonly StringBuilder Body;
        public readonly string Close;
        public PagePieces(string open, StringBuilder body, string close)
        {
            Open = open;
            Body = body;
            Close = close;
        }
        public int Length => Open.Length + Body.Length + Close.Length;
        public void CopyTo(Span<char> at)
        {
            Open.AsSpan().CopyTo(at);
            Body.CopyTo(0, at[Open.Length..], Body.Length);
            Close.AsSpan().CopyTo(at[(Open.Length + Body.Length)..]);
        }
        /// <summary>Whether the page's text is exactly <paramref name="text"/> — read out
        /// of the builder, without materializing the page.</summary>
        public bool Equals(string text)
            => text.Length == Length
               && text.AsSpan(0, Open.Length).SequenceEqual(Open)
               && Body.Equals(text.AsSpan(Open.Length, Body.Length))
               && text.AsSpan(Open.Length + Body.Length).SequenceEqual(Close);
        public string Materialize()
            => string.Create(Length, this, static (span, page) => page.CopyTo(span));
    }

    /// <summary>
    /// The document as its pieces, in emission order: the header, one
    /// <see cref="PagePieces"/> per page, the closing tag. THE one spelling of the
    /// document's shape — the one-string render (<see cref="Assemble"/>) and the page set
    /// (<see cref="ToPages"/>) both join exactly these.
    /// ⚠️ EVERY PIECE MUST MATCH WHAT THE BUILDER WROTE, CHARACTER FOR CHARACTER,
    /// including AppendLine's Environment.NewLine. The 566-book SVG A/B is what checks
    /// that, and it is not optional for a change of this shape.
    /// </summary>
    private (string Head, List<PagePieces> Pages, string Tail) BuildPieces()
    {
        string nl = Environment.NewLine;
        var pages = new List<PagePieces>(_pages.Count);
        if (_pages.Count == 0)
            return ("", pages, "");

        // Single page, static export: emit exactly as a one-page document (no <g> wrapper)
        // so the output is byte-identical to the common case. The INTERACTIVE document wraps
        // a single page like any other: the preview swaps, shifts and keeps pages by their
        // wrapper, and a one-page score used to have none — so every keystroke on it
        // replaced the whole picture (session 404).
        if (_pages.Count == 1 && !_options.Interactive)
        {
            var (body, w, h) = _pages[0];
            var head1 = new StringBuilder(512);
            WriteHeader(head1, w, h);
            pages.Add(new PagePieces("", body, ""));
            return (head1.ToString(), pages, "</svg>" + nl);
        }

        double totalHeight = 0, maxWidth = 0;
        foreach (var (_, w, h) in _pages)
        {
            totalHeight += h;
            if (w > maxWidth) maxWidth = w;
        }

        var head = new StringBuilder(512);
        WriteHeader(head, maxWidth, totalHeight);

        double yOffset = 0;
        foreach (var (body, _, h) in _pages)
        {
            // class/data attributes let a viewer (the VS Code preview toolbar)
            // find the page boundaries for fit-page zoom and page navigation.
            pages.Add(new PagePieces(
                string.Format(CultureInfo.InvariantCulture,
                    "<g class=\"page\" data-page-top=\"{0:F2}\" data-page-height=\"{1:F2}\" transform=\"translate(0, {0:F2})\">",
                    yOffset, h) + nl,
                body,
                "</g>" + nl));
            yOffset += h;
        }
        return (head.ToString(), pages, "</svg>" + nl);
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
