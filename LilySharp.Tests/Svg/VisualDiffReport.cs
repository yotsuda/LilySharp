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
using LilySharp.Core.Png;
using SkiaSharp;

namespace LilySharp.Tests.Svg;

/// <summary>
/// The human half of the visual regression net. The byte-identical snapshot
/// gate (<see cref="SvgSnapshotTests"/>) detects EVERY output change but cannot
/// judge whether a change is an improvement — which is exactly what a
/// geometry-changing fix needs. On each snapshot mismatch this collector
/// rasterizes baseline and actual (SkiaSharp with the bundled Emmentaler, the
/// same renderer for both sides in the same process, so equal SVG means equal
/// pixels), computes a pixel diff, and regenerates a self-contained HTML report
/// with side-by-side / overlay / blink / diff-heatmap views per fixture, sorted
/// by diff magnitude. The reviewer judges in a browser, then approves
/// selectively (tools/Approve-Snapshots.ps1) or wholesale
/// (LILYSHARP_UPDATE_SNAPSHOTS=1). See docs/visual-regression.md.
/// </summary>
internal static class VisualDiffReport
{
    private sealed record Entry(
        string SampleName, string FileBase,
        long ChangedPixels, long TotalPixels,
        int BaseW, int BaseH, int ActW, int ActH,
        int BoxLeft, int BoxTop, int BoxRight, int BoxBottom)
    {
        public double Percent => TotalPixels == 0 ? 0 : 100.0 * ChangedPixels / TotalPixels;
    }

    private static readonly object Gate = new();
    private static readonly List<Entry> Entries = new();

    // Cleared once per test process: a re-run after fixing some snapshots must
    // not keep showing the fixtures that no longer differ.
    private static readonly Lazy<string> OutDir = new(() =>
    {
        var dir = Path.Combine(FindRepoRoot(), "artifacts", "visual-diff");
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    });

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "LilySharp.Tests", "Snapshots")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find the repository root");
    }

    /// <summary>
    /// Records one changed fixture (writes SVGs + rasterized PNGs + diff
    /// heatmap, refreshes report.html) and returns the report path for the
    /// test-failure message.
    /// </summary>
    public static string Record(string sampleName, string baselineSvg, string actualSvg)
    {
        lock (Gate)
        {
            var dir = OutDir.Value;
            var fileBase = sampleName.Replace("/", "__").Replace("\\", "__");

            // The exact strings the snapshot gate compared — actual.svg is what
            // Approve-Snapshots.ps1 promotes to the baseline, byte for byte.
            File.WriteAllText(Path.Combine(dir, fileBase + ".baseline.svg"), baselineSvg);
            File.WriteAllText(Path.Combine(dir, fileBase + ".actual.svg"), actualSvg);

            // Scale 2 (192 DPI): sub-pixel geometry shifts in staff-space units
            // land on visibly distinct pixels.
            var png = new PngRenderOptions { Scale = 2.0f };
            var basePng = PngGenerator.ConvertSvgToPng(baselineSvg, png);
            var actPng = PngGenerator.ConvertSvgToPng(actualSvg, png);
            File.WriteAllBytes(Path.Combine(dir, fileBase + ".baseline.png"), basePng);
            File.WriteAllBytes(Path.Combine(dir, fileBase + ".actual.png"), actPng);

            var entry = Diff(sampleName, fileBase, basePng, actPng,
                Path.Combine(dir, fileBase + ".diff.png"));

            Entries.RemoveAll(e => e.FileBase == fileBase);
            Entries.Add(entry);
            var report = Path.Combine(dir, "report.html");
            File.WriteAllText(report, BuildHtml());
            return report;
        }
    }

    // Pixel-exact comparison (both PNGs come from the same Skia in the same
    // process, so any difference is a real output change, not raster noise).
    // The heatmap shows the actual rendering faded to light gray with changed
    // pixels in red; area only one side covers (page grew/shrank) counts as
    // changed too.
    private static Entry Diff(string sampleName, string fileBase,
        byte[] basePng, byte[] actPng, string diffPath)
    {
        using var baseBmp = SKBitmap.Decode(basePng);
        using var actBmp = SKBitmap.Decode(actPng);
        var bp = baseBmp.Pixels;
        var ap = actBmp.Pixels;
        int bw = baseBmp.Width, bh = baseBmp.Height;
        int aw = actBmp.Width, ah = actBmp.Height;
        int w = Math.Max(bw, aw), h = Math.Max(bh, ah);

        var outPixels = new SKColor[w * h];
        long changed = 0;
        int left = w, top = h, right = -1, bottom = -1;
        var red = new SKColor(220, 0, 0);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool inB = x < bw && y < bh, inA = x < aw && y < ah;
                var b = inB ? bp[y * bw + x] : SKColors.White;
                var a = inA ? ap[y * aw + x] : SKColors.White;
                bool diff = (inB != inA && (inB ? b : a) != SKColors.White) || (inB && inA && b != a);
                if (diff)
                {
                    changed++;
                    outPixels[y * w + x] = red;
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
                else
                {
                    // Fade the (identical) content so the red stands out but the
                    // musical context stays readable.
                    var g = (byte)(0.299 * a.Red + 0.587 * a.Green + 0.114 * a.Blue);
                    var faded = (byte)(g * 0.35 + 255 * 0.65);
                    outPixels[y * w + x] = new SKColor(faded, faded, faded);
                }
            }
        }

        using (var diffBmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque)))
        {
            diffBmp.Pixels = outPixels;
            using var img = SKImage.FromBitmap(diffBmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(diffPath, data.ToArray());
        }

        return new Entry(sampleName, fileBase, changed, (long)w * h,
            bw, bh, aw, ah, left, top, right, bottom);
    }

    private static string BuildHtml()
    {
        var sb = new StringBuilder();
        var ordered = Entries.OrderByDescending(e => e.Percent).ToList();

        sb.Append("""
            <!doctype html>
            <html><head><meta charset="utf-8"><title>Lily# visual regression report</title>
            <style>
              body { font: 14px/1.5 system-ui, sans-serif; margin: 24px; background: #fafafa; color: #222; }
              h1 { font-size: 20px; }
              table.index { border-collapse: collapse; margin: 12px 0 28px; }
              table.index th, table.index td { border: 1px solid #ddd; padding: 4px 10px; text-align: left; }
              table.index th { background: #f0f0f0; }
              .fixture { background: #fff; border: 1px solid #ddd; border-radius: 6px; padding: 12px 16px; margin: 0 0 28px; }
              .fixture h2 { font-size: 16px; margin: 0 0 4px; }
              .stats { color: #666; font-weight: normal; font-size: 13px; margin-left: 10px; }
              .tabs button { margin: 6px 6px 10px 0; padding: 4px 12px; cursor: pointer; }
              .tabs button.active { background: #2b6cb0; color: #fff; border-color: #2b6cb0; }
              .pane { display: none; }
              .pane.shown { display: block; }
              .side { display: flex; gap: 10px; }
              .side > div { flex: 1; min-width: 0; }
              .side h4 { margin: 0 0 4px; font-size: 12px; color: #666; }
              img { width: 100%; height: auto; border: 1px solid #ccc; background: #fff; display: block; }
              .stack { position: relative; }
              .stack img.top { position: absolute; inset: 0; }
              .links { font-size: 12px; margin-top: 6px; }
              .links a { margin-right: 12px; }
              input[type=range] { width: 300px; }
              code { background: #eee; padding: 1px 5px; border-radius: 3px; }
            </style></head><body>
            """);

        sb.Append("<h1>Lily# visual regression report</h1>");
        sb.Append($"<p>Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss} — <b>{ordered.Count}</b> changed fixture(s). ");
        sb.Append("Judge each change below, then approve per fixture with " +
                  "<code>pwsh tools/Approve-Snapshots.ps1 -Name &lt;fixture&gt;</code> " +
                  "or everything with <code>LILYSHARP_UPDATE_SNAPSHOTS=1</code> + re-run.</p>");

        sb.Append("<table class=\"index\"><tr><th>fixture</th><th>diff pixels</th><th>%</th><th>size</th><th>changed region</th></tr>");
        foreach (var e in ordered)
        {
            var size = e.BaseW == e.ActW && e.BaseH == e.ActH
                ? $"{e.ActW}×{e.ActH}"
                : $"{e.BaseW}×{e.BaseH} → {e.ActW}×{e.ActH}";
            var box = e.ChangedPixels == 0 ? "—"
                : $"({e.BoxLeft},{e.BoxTop})–({e.BoxRight},{e.BoxBottom})";
            sb.Append($"<tr><td><a href=\"#{e.FileBase}\">{e.SampleName}</a></td>" +
                      $"<td>{e.ChangedPixels:N0}</td><td>{e.Percent:F3}%</td>" +
                      $"<td>{size}</td><td>{box}</td></tr>");
        }
        sb.Append("</table>");

        foreach (var e in ordered)
        {
            var f = e.FileBase;
            sb.Append($"""
                <div class="fixture" id="{f}">
                <h2>{e.SampleName}<span class="stats">{e.ChangedPixels:N0} px ({e.Percent:F3}%)</span></h2>
                <div class="tabs">
                  <button class="active" onclick="show(this,'side')">Side-by-side</button>
                  <button onclick="show(this,'overlay')">Overlay</button>
                  <button onclick="show(this,'blink')">Blink</button>
                  <button onclick="show(this,'diff')">Diff</button>
                </div>
                <div class="pane side shown">
                  <div><h4>baseline</h4><img loading="lazy" src="{f}.baseline.png"></div>
                  <div><h4>actual</h4><img loading="lazy" src="{f}.actual.png"></div>
                </div>
                <div class="pane overlay">
                  <div class="stack"><img loading="lazy" src="{f}.baseline.png"><img loading="lazy" class="top" src="{f}.actual.png" style="opacity:.5"></div>
                  baseline <input type="range" min="0" max="100" value="50"
                    oninput="this.previousElementSibling.querySelector('.top').style.opacity=this.value/100"> actual
                </div>
                <div class="pane blink">
                  <img loading="lazy" src="{f}.actual.png" data-baseline="{f}.baseline.png" data-actual="{f}.actual.png">
                  <button onclick="blink(this)">Start blink</button>
                </div>
                <div class="pane diff"><img loading="lazy" src="{f}.diff.png"></div>
                <div class="links">raw:
                  <a href="{f}.baseline.svg">baseline.svg</a><a href="{f}.actual.svg">actual.svg</a>
                  (SVG needs the Emmentaler font locally — the PNGs are the authoritative rendering)
                </div>
                </div>
                """);
        }

        sb.Append("""
            <script>
            function show(btn, kind) {
              const fx = btn.closest('.fixture');
              fx.querySelectorAll('.tabs button').forEach(b => b.classList.toggle('active', b === btn));
              fx.querySelectorAll('.pane').forEach(p => p.classList.toggle('shown', p.classList.contains(kind)));
            }
            function blink(btn) {
              const fx = btn.closest('.fixture');
              const img = fx.querySelector('.pane.blink img');
              if (fx._t) { clearInterval(fx._t); fx._t = null; btn.textContent = 'Start blink'; return; }
              let on = false;
              fx._t = setInterval(() => { on = !on; img.src = on ? img.dataset.baseline : img.dataset.actual; }, 500);
              btn.textContent = 'Stop blink';
            }
            </script></body></html>
            """);

        return sb.ToString();
    }
}
