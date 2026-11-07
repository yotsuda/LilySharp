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

// ScoreVideo <file.lys> <workdir> [--width N] [--height N] [--fps N] [--lead S] [--length S] [--color RRGGBB]
//
// Reads what New-ScoreVideo.ps1 left in <workdir> (lysc's page PNGs and boxes JSON), asks the
// language server for the preview's playback list, and draws one picture per state: the page
// the music is on, with the noteheads that are sounding lit. Writes <workdir>/frames/*.png and
// <workdir>/concat.txt (an ffmpeg concat list with each picture's duration).

using System.Globalization;
using System.Text;
using System.Text.Json;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using SkiaSharp;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ScoreVideo <file.lys> <workdir> [--width N] [--height N] [--fps N] [--lead S] [--length S] [--color RRGGBB]");
    return 2;
}
var lysPath = Path.GetFullPath(args[0]);
var work = Path.GetFullPath(args[1]);
var opts = new Dictionary<string, string>();
for (int i = 2; i + 1 < args.Length; i += 2) opts[args[i].TrimStart('-')] = args[i + 1];
string Opt(string k, string d) => opts.TryGetValue(k, out var v) ? v : d;
double Num(string k, double d) => double.Parse(Opt(k, d.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

int W = (int)Num("width", 1240), H = (int)Num("height", 1754), FPS = (int)Num("fps", 30);
double lead = Num("lead", 1.5);
var hex = Opt("color", "F57800");
var ink = new SKColor(Convert.ToByte(hex[0..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
var name = Path.GetFileNameWithoutExtension(lysPath);
var src = File.ReadAllText(lysPath);

// ---- The playback list: what the preview plays, in seconds, each note with its source offset.
var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
var uri = new Uri(lysPath);
server.DidOpen(new DidOpenTextDocumentParams
{
    TextDocument = new TextDocumentItem { Uri = uri, Text = src, Version = 1, LanguageId = "lilysharp" },
});
var pb = server.GetPlayback(new PlaybackParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
if (pb.Error != null) { Console.Error.WriteLine("playback: " + pb.Error); return 1; }
var notes = pb.Notes!.Where(n => n.P >= 0)
    .Select(n => (t: n.T + lead, e: n.T + n.D + lead, s: n.S, o: n.O)).ToList();
double length = Num("length", pb.BarEnd + 2);
double total = lead + length;

// A chord's notes play under the offset of its '<'; each notehead is drawn under its own pitch.
int ChordStart(int p)
{
    for (int i = p - 1; i >= 0; i--)
    {
        char c = src[i];
        if (c == '<') return i;
        if (c == '>' || c == '|' || c == '{' || c == '}' || c == '\n') return p;
    }
    return p;
}

// ---- The pages, fitted into W x H without changing their shape.
var boxes = JsonDocument.Parse(File.ReadAllText(Path.Combine(work, name + ".boxes.json"))).RootElement;
var pageEls = boxes.GetProperty("pages").EnumerateArray().ToList();
var pages = new List<(SKBitmap img, double scale, int offX, int offY)>();
var bars = new List<(int page, double[] box)>();
var heads = new List<(int page, int key, double[] box)>();
for (int pi = 0; pi < pageEls.Count; pi++)
{
    var p = pageEls[pi];
    string file = Path.Combine(work, $"{name}-page{pi + 1}.png");
    if (pageEls.Count == 1 && !File.Exists(file)) file = Path.Combine(work, name + ".png");
    using var raw = SKBitmap.Decode(file);
    double pw = p.GetProperty("width").GetDouble(), ph = p.GetProperty("height").GetDouble();
    double pxPerSs = raw.Width / pw;
    int cw = raw.Width, ch = (int)Math.Round(ph * pxPerSs);        // a one-page PNG may be cut short
    double f = Math.Min(W / (double)cw, H / (double)ch);
    int fw = (int)Math.Round(cw * f), fh = (int)Math.Round(ch * f);
    int offX = (W - fw) / 2, offY = (H - fh) / 2;
    var frame = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque);
    using (var canvas = new SKCanvas(frame))
    using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
    {
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(raw, SKRect.Create(offX, offY, (float)(raw.Width * f), (float)(raw.Height * f)), paint);
    }
    pages.Add((frame, pxPerSs * f, offX, offY));

    foreach (var b in p.GetProperty("bars").EnumerateArray())
        bars.Add((pi, b.GetProperty("box").EnumerateArray().Select(e => e.GetDouble()).ToArray()));
    foreach (var s in p.GetProperty("symbols").EnumerateArray())
    {
        if (s.GetProperty("kind").GetString() != "notehead") continue;
        int pos = s.GetProperty("pos").GetInt32();
        if (pos < 0) continue;
        var box = s.GetProperty("box").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        heads.Add((pi, ChordStart(pos), box));
        if (ChordStart(pos) != pos) heads.Add((pi, pos, box));
    }
}

// The bar a notehead stands in, in reading order — to tell the printed copies of one source
// note apart (a section the form plays twice is printed twice: the playback's O says which).
int BarIndex(int page, double[] box)
{
    double cx = (box[0] + box[2]) / 2, cy = (box[1] + box[3]) / 2;
    int best = int.MaxValue; double bestD = double.MaxValue;
    for (int i = 0; i < bars.Count; i++)
    {
        var (bp, b) = bars[i];
        if (bp != page || cx < b[0] || cx > b[2]) continue;
        double d = cy < b[1] ? b[1] - cy : cy > b[3] ? cy - b[3] : 0;
        if (d < bestD) { bestD = d; best = i; }
    }
    return best;
}
var lit = new Dictionary<(int key, int ord), List<(int page, double[] box)>>();
foreach (var g in heads.GroupBy(h => h.key))
{
    var copies = g.Select(h => (h.page, bar: BarIndex(h.page, h.box))).Distinct()
        .OrderBy(c => c.page).ThenBy(c => c.bar).ToList();
    foreach (var h in g)
    {
        int ord = copies.IndexOf((h.page, BarIndex(h.page, h.box)));
        if (!lit.TryGetValue((h.key, ord), out var list)) lit[(h.key, ord)] = list = new();
        list.Add((h.page, h.box));
    }
}
int matched = notes.Count(n => lit.ContainsKey((n.s, n.o)));
Console.WriteLine($"{pages.Count} page(s), {notes.Count} notes, {matched} found on the page");

static void Tint(SKBitmap img, int x0, int y0, int x1, int y1, SKColor c)
{
    x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(img.Width - 1, x1); y1 = Math.Min(img.Height - 1, y1);
    for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            var p = img.GetPixel(x, y);
            double v = (p.Red + p.Green + p.Blue) / (3.0 * 255);      // 1 = paper, 0 = ink
            img.SetPixel(x, y, new SKColor((byte)(v * p.Red + (1 - v) * c.Red), (byte)(v * p.Green + (1 - v) * c.Green), (byte)(v * p.Blue + (1 - v) * c.Blue)));
        }
}

// ---- One picture per state; the page is the one the latest note to start is printed on.
var outDir = Path.Combine(work, "frames");
if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
Directory.CreateDirectory(outDir);
var cache = new Dictionary<string, string>();
var segs = new List<(string file, int frames)>();
int page = 0;
string? prevKey = null;
int totalFrames = (int)Math.Ceiling(total * FPS);
for (int fi = 0; fi < totalFrames; fi++)
{
    double t = (fi + 0.5) / FPS;
    var sounding = notes.Where(n => n.t <= t && t < n.e).ToList();
    var newest = sounding.OrderByDescending(n => n.t).FirstOrDefault(n => lit.ContainsKey((n.s, n.o)));
    if (newest != default) page = lit[(newest.s, newest.o)][0].page;
    var on = sounding.Select(n => (n.s, n.o)).Distinct().OrderBy(k => k).ToList();
    var key = page + "|" + string.Join(",", on);
    if (key == prevKey) { segs[^1] = (segs[^1].file, segs[^1].frames + 1); continue; }
    prevKey = key;
    if (!cache.TryGetValue(key, out var file))
    {
        var (img0, sc, ox, oy) = pages[page];
        using var img = img0.Copy();
        foreach (var k in on)
            if (lit.TryGetValue(k, out var hs))
                foreach (var (hp, b) in hs.Where(h => h.page == page))
                    Tint(img, ox + (int)(b[0] * sc) - 1, oy + (int)(b[1] * sc) - 1,
                         ox + (int)Math.Ceiling(b[2] * sc) + 1, oy + (int)Math.Ceiling(b[3] * sc) + 1, ink);
        file = Path.Combine(outDir, $"s{cache.Count:D5}.png");
        using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            File.WriteAllBytes(file, data.ToArray());
        cache[key] = file;
    }
    segs.Add((file, 1));
}
var sb = new StringBuilder();
foreach (var (file, frames) in segs)
{
    sb.Append("file '").Append(file.Replace('\\', '/')).Append("'\n");
    sb.Append("duration ").Append((frames / (double)FPS).ToString("0.######", CultureInfo.InvariantCulture)).Append('\n');
}
sb.Append("file '").Append(segs[^1].file.Replace('\\', '/')).Append("'\n");
File.WriteAllText(Path.Combine(work, "concat.txt"), sb.ToString());
Console.WriteLine($"{cache.Count} pictures, {total.ToString("0.00", CultureInfo.InvariantCulture)} s");
return 0;
