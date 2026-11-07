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

// ScoreVideo <file.lys> <workdir> [--width N] [--height N] [--fps N] [--lead S] [--length S]
//            [--color RRGGBB] [--scale F] [--set KEY=VALUE]...
//
// The pictures of a score video, drawn by LilySharp.Core.Export.ScoreVideo — the same code the
// editor's Export as MP4 runs — from the notes the language server's playback list gives (the
// preview's Play). Writes <workdir>/frames/*.png and <workdir>/frames/frames.txt, an ffmpeg
// concat list. New-ScoreVideo.ps1 adds the sound.

using System.Globalization;
using LilySharp.Core.Export;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ScoreVideo <file.lys> <workdir> [--width N] [--height N] [--fps N] [--lead S] [--length S] [--color RRGGBB] [--scale F] [--set KEY=VALUE]...");
    return 2;
}
var lysPath = Path.GetFullPath(args[0]);
var work = Path.GetFullPath(args[1]);
var opts = new Dictionary<string, string>();
var settings = new List<string>();
for (int i = 2; i + 1 < args.Length; i += 2)
{
    if (args[i] == "--set") settings.Add(args[i + 1]);
    else opts[args[i].TrimStart('-')] = args[i + 1];
}
string Opt(string k, string d) => opts.TryGetValue(k, out var v) ? v : d;
double Num(string k, double d) => double.Parse(Opt(k, d.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

PaperOverrides? paper = null;
if (settings.Count > 0)
{
    paper = PaperOverrides.Parse(settings, out var error);
    if (error != null) { Console.Error.WriteLine("--set: " + error); return 2; }
}

// The notes: what the preview's Play sounds, its first score.
var src = File.ReadAllText(lysPath);
var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
var uri = new Uri(lysPath);
server.DidOpen(new DidOpenTextDocumentParams
{
    TextDocument = new TextDocumentItem { Uri = uri, Text = src, Version = 1, LanguageId = "lilysharp" },
});
var pb = server.GetPlayback(new PlaybackParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
if (pb.Error != null) { Console.Error.WriteLine("playback: " + pb.Error); return 1; }
var notes = pb.Notes!.Where(n => n.P >= 0).Select(n => new VideoNote(n.T, n.T + n.D, n.S, n.O)).ToList();

// The tree the notes were played from — its `using`s resolved beside the file, as the server
// resolves them (UsingExpander), so the notes' offsets are this tree's — and its first score.
var tree = SyntaxTree.Parse(src);
if (LilySharp.Core.Parser.UsingExpander.HasUsings(tree))
    tree = SyntaxTree.Parse(LilySharp.Core.Parser.UsingExpander.Expand(src, lysPath, p => File.Exists(p) ? File.ReadAllText(p) : null));
var score = RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null);

var look = new ScoreVideoOptions
{
    Width = (int)Num("width", 1240), Height = (int)Num("height", 1754), Fps = (int)Num("fps", 30),
    Lead = Num("lead", 1.5), LitColor = Convert.ToUInt32(Opt("color", "F57800"), 16),
    PngScale = (float)Num("scale", 2.0), PaperOverrides = paper,
};
var frames = ScoreVideo.WriteFrames(tree, score, notes, Num("length", pb.BarEnd + 2), Path.Combine(work, "frames"), look);
foreach (var w in frames.Warnings) Console.WriteLine("warning: " + w);
Console.WriteLine($"{notes.Count} notes, {frames.Pictures} pictures, {frames.Seconds.ToString("0.00", CultureInfo.InvariantCulture)} s");
return 0;
