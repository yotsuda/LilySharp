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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LilySharp.Core.Png;
using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using SkiaSharp;

namespace LilySharp.Core.Export;

/// <summary>One sounding note as a score video lights it: when it starts and stops (seconds from
/// the music's start), the source offset it plays under and which printed copy of that offset it
/// is — the fields of the editor's playback list (<c>PlaybackNote.T</c>, <c>D</c>, <c>S</c>,
/// <c>O</c>).</summary>
public readonly record struct VideoNote(double Start, double End, int SourcePos, int SourceOrdinal);

/// <summary>How a score video looks. By default the picture takes the page's shape and the notes
/// are lit orange.</summary>
public sealed class ScoreVideoOptions
{
    /// <summary>The picture's width in pixels, or null to take it from the page. With
    /// <see cref="Height"/> also given, the picture is that size, the page keeps its shape inside
    /// it and the rest is white.</summary>
    public int? Width { get; init; }

    /// <summary>The picture's height in pixels, or null to take it from the page.</summary>
    public int? Height { get; init; }

    /// <summary>Pictures a second.</summary>
    public int Fps { get; init; } = 30;

    /// <summary>Seconds of the first page, still, before the music starts.</summary>
    public double Lead { get; init; } = 1.5;

    /// <summary>The colour a sounding notehead is drawn in, 0xRRGGBB.</summary>
    public uint LitColor { get; init; } = 0xF57800;

    /// <summary>The resolution the pages are engraved at before they are fitted (as <c>lysc png --scale</c>).</summary>
    public float PngScale { get; init; } = 2.0f;

    /// <summary>Paper values laid over the file's (as <c>lysc png --set</c>).</summary>
    public Semantics.PaperOverrides? PaperOverrides { get; init; }

    /// <summary>The defaults.</summary>
    public static ScoreVideoOptions Default { get; } = new();
}

/// <summary>What <see cref="ScoreVideo.WriteFrames"/> wrote: the ffmpeg concat list (each
/// picture and how long it shows), how many pictures, and the seconds the list runs.</summary>
public sealed record ScoreVideoFrames(string ConcatList, int Pictures, double Seconds, IReadOnlyList<string> Warnings);

/// <summary>
/// The pictures of a score video: the engraved pages, the page the music is on shown whole, the
/// noteheads lit while they sound and nothing else marked (owner's decisions 2026-11-07, for a
/// video of an AI-composed piece). The first page shows until a note printed on the next page
/// starts; then the whole picture turns to that page.
/// </summary>
/// <remarks>
/// The editor's MP4 export and <c>tools/lys-to-mp4</c> both come here, so the two cannot drift.
/// The pages are the PNG export's and the noteheads' boxes the boxes export's — one layout on one
/// paper, so a box is where its head is drawn. A note of the playback list is found on the page by
/// its source offset: a chord's notes play under the chord's offset and each head is drawn under
/// its own pitch's, so a head answers to both; a section the form plays twice is printed twice,
/// and the note's ordinal picks the copy, counted in reading order (page, then the bars in the
/// order the page lists them). One picture is drawn per state — the page and the heads lit — and
/// shown for as many frames as the state lasts, so a long piece is a few hundred pictures, not
/// one per frame.
/// </remarks>
public static class ScoreVideo
{
    /// <summary>
    /// Draws the pictures of <paramref name="score"/> (null: a file with no <c>score</c> block) into
    /// <paramref name="directory"/> and writes there the ffmpeg concat list that shows them —
    /// <c>ffmpeg -f concat -safe 0 -i LIST</c> — for <c>Lead + musicSeconds</c> in all.
    /// </summary>
    /// <param name="tree">The score's tree, the one the notes were played from.</param>
    /// <param name="score">The score drawn.</param>
    /// <param name="notes">The notes that sound, as the editor's playback list gives them.</param>
    /// <param name="musicSeconds">How long the sound runs, from its start (the tail included).</param>
    /// <param name="directory">Where the pictures and the list go; created if missing.</param>
    /// <param name="options">The look; null for <see cref="ScoreVideoOptions.Default"/>.</param>
    public static ScoreVideoFrames WriteFrames(SyntaxTree tree, RenderSpec? score, IReadOnlyList<VideoNote> notes,
        double musicSeconds, string directory, ScoreVideoOptions? options = null)
    {
        options ??= ScoreVideoOptions.Default;
        Directory.CreateDirectory(directory);
        var warnings = new List<string>();
        var pngs = PngGenerator.GenerateScorePages(tree, score, new PngRenderOptions
        {
            Scale = options.PngScale, PaperOverrides = options.PaperOverrides, LayoutWarning = warnings.Add,
        });
        var boxes = BoxesGenerator.GeneratePages(tree, score, options.PaperOverrides);
        if (pngs.Count != boxes.Count)
            throw new InvalidOperationException($"the pages ({pngs.Count}) and their boxes ({boxes.Count}) disagree");

        // A score of several pages: the paper's shape, every page shown whole in it. A score of
        // ONE page: that page's own shape, which is cut under its music as the PNG's is — the
        // picture has no paper above and below the music to show (owner's report 2026-11-12: a
        // short song's video was the whole sheet, most of it white; until then the picture
        // took the paper's shape whatever the page was). A content-driven paper has no height
        // but the page's either.
        // ⚠️ ONLY ON THE SHEET NOBODY CHOSE. A score that states its paper keeps it, one page or
        // many: `paper yt { paperWidth 256mm paperHeight 144mm }` is a video's 16:9, written to
        // have that picture (owner's report 2026-11-10: such a score came out upright). The
        // default sheet is known by its size — a file that writes that very size is cut too.
        var paper = Svg.SvgGenerator.CollectScore(tree, score, settings: options.PaperOverrides).Paper;
        var sheet = Svg.Layout.LayoutOptions.Default;
        bool chosen = Math.Abs(paper.PageWidth - sheet.PageWidth) > 1e-6 || Math.Abs(paper.PageHeight - sheet.PageHeight) > 1e-6;
        var (w, h) = PictureSize(paper.PageWidth,
            paper.PageHeight > 0 && (pngs.Count > 1 || chosen) ? paper.PageHeight : boxes[0].Height,
            options.Width, options.Height);
        var pages = new List<(SKBitmap Picture, double Scale, int X, int Y)>();
        try
        {
            for (int p = 0; p < pngs.Count; p++)
            {
                using var raw = SKBitmap.Decode(pngs[p]);
                double pxPerSpace = raw.Width / boxes[p].Width;
                double fit = Math.Min(w / (double)raw.Width, h / (boxes[p].Height * pxPerSpace));
                float fw = (float)(raw.Width * fit), fh = (float)(raw.Height * fit);
                int x = (int)Math.Round((w - fw) / 2), y = (int)Math.Round((h - boxes[p].Height * pxPerSpace * fit) / 2);
                var picture = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
                using (var canvas = new SKCanvas(picture))
                using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
                {
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(raw, SKRect.Create(x, y, fw, fh), paint);
                }
                pages.Add((picture, pxPerSpace * fit, x, y));
            }

            var heads = HeadsByNote(tree, boxes);
            var lit = new SKColor((byte)(options.LitColor >> 16), (byte)(options.LitColor >> 8), (byte)options.LitColor);
            var shifted = notes.Select(n => n with { Start = n.Start + options.Lead, End = n.End + options.Lead }).ToList();
            double total = options.Lead + musicSeconds;
            int frames = (int)Math.Ceiling(total * options.Fps);

            // First what each frame shows, then each different picture drawn once — in parallel, as
            // the encoding is most of the time (a 100-second piece: some 230 pictures).
            var states = new Dictionary<string, (string File, int Page, List<(int, int)> On)>(StringComparer.Ordinal);
            var shown = new List<(string File, int Frames)>();
            string? last = null;
            int page = 0;
            for (int f = 0; f < frames; f++)
            {
                double t = (f + 0.5) / options.Fps;
                var sounding = shifted.Where(n => n.Start <= t && t < n.End).ToList();
                // The page of the note that started last: a note held over the turn does not hold
                // the page back.
                foreach (var n in sounding.OrderByDescending(n => n.Start))
                    if (heads.TryGetValue((n.SourcePos, n.SourceOrdinal), out var at)) { page = at[0].Page; break; }
                var on = sounding.Select(n => (n.SourcePos, n.SourceOrdinal)).Distinct().OrderBy(k => k).ToList();
                string key = page + "|" + string.Join(",", on.Select(k => k.SourcePos + ":" + k.SourceOrdinal));
                if (key == last) { shown[^1] = (shown[^1].File, shown[^1].Frames + 1); continue; }
                last = key;
                if (!states.TryGetValue(key, out var state))
                    states[key] = state = (Path.Combine(directory, $"s{states.Count:D5}.png"), page, on);
                shown.Add((state.File, 1));
            }
            System.Threading.Tasks.Parallel.ForEach(states.Values, state =>
            {
                var (picture, scale, x0, y0) = pages[state.Page];
                using var copy = picture.Copy();
                foreach (var k in state.On)
                    if (heads.TryGetValue(k, out var hs))
                        foreach (var (hp, b) in hs)
                            if (hp == state.Page)
                                Tint(copy, x0 + (int)(b[0] * scale) - 1, y0 + (int)(b[1] * scale) - 1,
                                    x0 + (int)Math.Ceiling(b[2] * scale) + 1, y0 + (int)Math.Ceiling(b[3] * scale) + 1, lit);
                using var data = copy.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(state.File, data.ToArray());
            });

            var list = new StringBuilder();
            foreach (var (file, count) in shown)
            {
                list.Append("file '").Append(file.Replace('\\', '/').Replace("'", "'\\''")).Append("'\n");
                list.Append("duration ").Append((count / (double)options.Fps).ToString("0.######", CultureInfo.InvariantCulture)).Append('\n');
            }
            // The concat demuxer takes the last entry's duration only when the file is named again.
            list.Append("file '").Append(shown[^1].File.Replace('\\', '/').Replace("'", "'\\''")).Append("'\n");
            var listPath = Path.Combine(directory, "frames.txt");
            File.WriteAllText(listPath, list.ToString());
            return new ScoreVideoFrames(listPath, states.Count, total, warnings);
        }
        finally
        {
            foreach (var p in pages) p.Picture.Dispose();
        }
    }

    /// <summary>
    /// The picture's size for a page of this shape: both sides as given when both are, else the
    /// page's shape inside 1240 × 1754 (a page taller than wide; A4 upright fills it) or
    /// 1920 × 1080 (wider than tall; 16:9 fills it, the size a video site plays best), a side
    /// given standing in for that limit (owner's report, 2026-11-10: a score on 16:9 paper made an
    /// upright video with white above and below). Even, as H.264's 4:2:0 needs.
    /// </summary>
    internal static (int Width, int Height) PictureSize(double pageWidth, double pageHeight, int? width, int? height)
    {
        if (width is int w && height is int h)
            return (w, h);
        bool wide = pageWidth > pageHeight;
        double fit = Math.Min((width ?? (wide ? 1920 : 1240)) / pageWidth, (height ?? (wide ? 1080 : 1754)) / pageHeight);
        return (Even(pageWidth * fit), Even(pageHeight * fit));

        static int Even(double pixels) => Math.Max(2, (int)Math.Round(pixels / 2) * 2);
    }

    /// <summary>Every notehead by the note that lights it — (source offset, printed copy) — with
    /// its page and box.</summary>
    private static Dictionary<(int Pos, int Ordinal), List<(int Page, double[] Box)>> HeadsByNote(
        SyntaxTree tree, IReadOnlyList<BoxPage> pages)
    {
        // The bars in reading order, to count the printed copies of one offset by.
        var bars = pages.SelectMany((p, pi) => p.Bars.Select(b => (Page: pi, b.Box))).ToList();
        int BarOf(int page, double[] box)
        {
            double cx = (box[0] + box[2]) / 2, cy = (box[1] + box[3]) / 2;
            int best = int.MaxValue;
            double nearest = double.MaxValue;
            for (int i = 0; i < bars.Count; i++)
            {
                var (bp, b) = bars[i];
                if (bp != page || cx < b[0] || cx > b[2]) continue;
                double d = cy < b[1] ? b[1] - cy : cy > b[3] ? cy - b[3] : 0;
                if (d < nearest) { nearest = d; best = i; }
            }
            return best;
        }

        var keyed = new List<(int Key, int Page, int Bar, double[] Box)>();
        for (int pi = 0; pi < pages.Count; pi++)
            foreach (var s in pages[pi].Symbols)
            {
                if (s.Kind != "notehead" || s.Pos < 0) continue;
                int bar = BarOf(pi, s.Box);
                keyed.Add((s.Pos, pi, bar, s.Box));
                int chord = ChordStart(tree, s.Pos);
                if (chord != s.Pos) keyed.Add((chord, pi, bar, s.Box));
            }

        var heads = new Dictionary<(int, int), List<(int, double[])>>();
        foreach (var group in keyed.GroupBy(k => k.Key))
        {
            var copies = group.Select(k => (k.Page, k.Bar)).Distinct().OrderBy(c => c.Page).ThenBy(c => c.Bar).ToList();
            foreach (var k in group)
            {
                var key = (k.Key, copies.IndexOf((k.Page, k.Bar)));
                if (!heads.TryGetValue(key, out var list)) heads[key] = list = new();
                list.Add((k.Page, k.Box));
            }
        }
        return heads;
    }

    /// <summary>Where the chord a pitch stands in begins (its <c>&lt;</c>, the offset its notes
    /// play under), else the pitch's own offset.</summary>
    private static int ChordStart(SyntaxTree tree, int pos)
    {
        for (var node = tree.FindNode(pos); node != null; node = node.Parent)
            if (node is ChordSyntax chord) return chord.Span.Start;
        return pos;
    }

    /// <summary>Draws the ink in the box in <paramref name="color"/>, the paper left white and the
    /// anti-aliased edge between kept in proportion.</summary>
    private static void Tint(SKBitmap picture, int x0, int y0, int x1, int y1, SKColor color)
    {
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
        x1 = Math.Min(picture.Width - 1, x1); y1 = Math.Min(picture.Height - 1, y1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = picture.GetPixel(x, y);
                double paper = (p.Red + p.Green + p.Blue) / (3.0 * 255);
                picture.SetPixel(x, y, new SKColor(
                    (byte)(paper * p.Red + (1 - paper) * color.Red),
                    (byte)(paper * p.Green + (1 - paper) * color.Green),
                    (byte)(paper * p.Blue + (1 - paper) * color.Blue)));
            }
    }
}
