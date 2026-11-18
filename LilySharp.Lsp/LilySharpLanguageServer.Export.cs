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

using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using LilySharp.Lsp.Protocol;
using StreamJsonRpc;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LspRange = LilySharp.Lsp.Protocol.Range;
using LspDiagnosticSeverity = LilySharp.Lsp.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    /// <summary>
    /// Exports a score file in the requested format. Two callers: the preview's Export
    /// button (the open document, ONE score named by RenderName, the save dialog's path
    /// in OutputPath) and the Explorer's batch export (any <c>.lys</c>, open or not — an
    /// open one by TextDocument so its unsaved edits are what gets written, as the
    /// preview shows them, else by Path from disk — with <c>All</c> writing EVERY score
    /// into OutputDirectory under the CLI's <c>--all</c> names). Every format honours the
    /// score: SVG/PNG/PDF draw it, and MIDI (and the WAV played from it, through the
    /// <see cref="ExportParams.SoundFont"/> the editor names), MusicXML and the LilyPond twin write ITS form
    /// (the CLI's RunFormOutput — <c>lysc midi --score practice</c> writes `form practice`).
    /// Until 2026-10-03 the one-score export handed the three a null form, which is the
    /// PRIMARY one: the preview's Export button on <c>score practice { tab bass }</c>, whose
    /// form starts at section B, wrote a .mid that began with section A (owner report).
    /// </summary>
    [JsonRpcMethod("lilysharp/export", UseSingleObjectParameterDeserialization = true)]
    public Task<ExportResponse> ExportAsync(ExportParams @params, CancellationToken token)
        => OffDispatch(() => Export(@params), token);

    /// <summary>The tree an export reads — the open document's (its unsaved edits included, as the
    /// preview shows them) or a file's from the disk, its <c>using</c>s resolved — and the stem
    /// its files are named by; or why there is none (the errors of a score that does not compile).
    /// Shared by <see cref="Export"/> and <see cref="VideoFrames"/>, so a video's pictures are
    /// drawn from the very tree its notes came from.</summary>
    private string? ExportTreeOf(TextDocumentIdentifier? document, string? path, out SyntaxTree tree, out string stem)
    {
        tree = null!;
        stem = "";
        if (document != null)
        {
            var uri = document.Uri;
            var doc = _documentManager.GetDocument(uri);
            if (doc == null)
                return DocumentNotFound;
            (tree, _) = ExpandUsings(doc, uri);
            stem = Path.GetFileNameWithoutExtension(uri.IsFile ? FilePathOf(uri) : uri.ToString());
        }
        else if (!string.IsNullOrEmpty(path))
        {
            if (!File.Exists(path))
                return $"File not found: {path}";
            // Not open in the editor: read it the way the CLI does, `using` includes
            // resolved against the file's own folder.
            var text = File.ReadAllText(path);
            (tree, _) = ExpandUsings(text, SyntaxTree.Parse(text), path,
                p => File.Exists(p) ? File.ReadAllText(p) : null);
            stem = Path.GetFileNameWithoutExtension(path);
        }
        else
        {
            return "Nothing to export: no document and no path was given";
        }

        if (tree.HasErrors)
        {
            var errors = tree;
            return string.Join("\n", tree.Diagnostics
                .Where(d => d.Severity == CoreDiagnosticSeverity.Error)
                .Select(d =>
                {
                    var (line, col) = GetLineAndColumn(errors.Text, d.Span.Start);
                    // GetLineAndColumn is 0-based (LSP protocol); the preview text is read by
                    // a human, so show 1-based line/column to match the editor gutter.
                    return $"Line {line + 1}, Col {col + 1}: {d.Message}";
                }));
        }
        return null;
    }

    public ExportResponse Export(ExportParams @params)
    {
        if (ExportTreeOf(@params.TextDocument, @params.Path, out var tree, out var stem) is { } unreadable)
            return new ExportResponse { Success = false, Error = unreadable };

        var format = (@params.Format ?? "svg").ToLowerInvariant();
        string ext;
        if (format == "mp3" || format == "mp4")
        {
            // Only the editor writes an MP3 or an MP4 (it encodes the sound it renders, and the
            // video through ffmpeg; the command line has neither), so the server names the files
            // and gives the notes; a video's pictures it draws when asked (VideoFrames).
            if (!@params.PlaybackOnly)
                return new ExportResponse { Success = false, Error = $"An {format.ToUpperInvariant()} is rendered by the editor: ask with playbackOnly" };
            ext = "." + format;
        }
        else if (!LilySharp.Core.Export.ScoreExport.Extensions.TryGetValue(format, out ext!))
            return new ExportResponse { Success = false, Error = $"Unknown format: {format}" };

        // A WAV or an MP3 the editor renders (PlaybackOnly): each score's notes and file are
        // collected where the file would be written — the same scores under the same names as
        // every other format, since the choosing and the naming below are not repeated for it.
        bool editorRenders = (format == "wav" || format == "mp3" || format == "mp4") && @params.PlaybackOnly;
        var playbacks = new List<PlaybackExport>();
        LilySharp.Core.Export.ExportResult WriteOne(string path,
            (RenderDeclarationSyntax Declaration, RenderSpec Spec)? score,
            LilySharp.Core.Export.ExportOptions? options)
        {
            if (!editorRenders)
                return LilySharp.Core.Export.ScoreExport.Write(tree, format, path, score, options);
            var pb = PlaybackOf(tree, score?.Spec);
            if (pb.Error != null)
                throw new InvalidOperationException(pb.Error);
            playbacks.Add(new PlaybackExport
            {
                OutputPath = path, Notes = pb.Notes ?? [], Parts = pb.Parts ?? [], Extra = pb.Extra ?? [], Beats = pb.Beats ?? [], BarEnd = pb.BarEnd,
                Sung = pb.Sung ?? [], Played = pb.Played ?? [],
                Score = score?.Spec.Name,
            });
            return new LilySharp.Core.Export.ExportResult([path], [], []);
        }

        // A WAV needs a font it can read; refuse before a folder is made or a file begun.
        if (format == "wav" && !editorRenders)
        {
            if (string.IsNullOrEmpty(@params.SoundFont))
                return new ExportResponse { Success = false, Error = "A WAV export needs a SoundFont 2 (.sf2)" };
            if (LilySharp.Core.Audio.WavRenderer.SoundFontProblem(@params.SoundFont) is { } problem)
                return new ExportResponse { Success = false, Error = problem };
        }

        try
        {
            if (!@params.All)
            {
                // The score the preview DRAWS (Choose's policy: the name, else the first),
                // with its declaration — the one the playback request plays, too. The
                // writing is ScoreExport's, the command line's home (its remarks say why).
                var chosen = RenderSpecParser.ChooseDeclared(tree, @params.RenderName);
                var result = WriteOne(@params.OutputPath, chosen,
                    new LilySharp.Core.Export.ExportOptions { TodoMarks = @params.TodoMarks, SoundFont = @params.SoundFont });
                var written = result.Files;
                // The dialog's path names ONE file; a multi-page PNG writes several, so
                // report what was actually written so the toast isn't a lie.
                var reported = written.Count == 1
                    ? written[0]
                    : Path.Combine(Path.GetDirectoryName(@params.OutputPath) ?? "",
                        string.Join(", ", written.Select(Path.GetFileName)));
                return new ExportResponse
                {
                    Success = true,
                    OutputPath = reported,
                    OutputPaths = written.ToArray(),
                    Warnings = result.Warnings.ToArray(),
                    Playbacks = editorRenders ? playbacks.ToArray() : null,
                };
            }

            if (string.IsNullOrEmpty(@params.OutputDirectory))
                return new ExportResponse { Success = false, Error = "Exporting every score needs an output directory" };
            var dir = @params.OutputDirectory;
            Directory.CreateDirectory(dir);
            var options = new LilySharp.Core.Export.ExportOptions { SoundFont = @params.SoundFont };

            var outputs = new List<string>();
            var warnings = new List<string>();
            var scores = RenderSpecParser.FindAllDeclared(tree);
            if (scores.Count == 0)
            {
                // A file with no `score` block has exactly one thing to write (the CLI's
                // RunFormOutput says the same), under the file's own name.
                Add(WriteOne(Path.Combine(dir, stem + ext), null, options));
            }
            else if (format == "vsqx")
            {
                // A .vsqx holds one arrangement and the exporter takes no form, so the
                // first score is what the CLI writes too — say what was left out
                // ("If you drop something, say so in Warnings", HANDOFF §2F).
                Add(LilySharp.Core.Export.ScoreExport.Write(tree, format, Path.Combine(dir, stem + ext), null));
                if (scores.Count > 1)
                    warnings.Add($"{stem}: a .vsqx holds one arrangement — wrote the first score, left out "
                        + string.Join(", ", scores.Skip(1).Select(s => RenderSpecParser.LabelOf(s.Spec.Name))));
            }
            else
            {
                foreach (var score in scores)
                {
                    var spec = score.Spec;
                    var target = Path.Combine(dir, spec.ResolveOutputStem(stem) + ext);
                    if (outputs.Contains(target, StringComparer.OrdinalIgnoreCase))
                    {
                        // Two scores of one name (LYS6001) resolve to one file; the second
                        // would silently replace the first's picture.
                        warnings.Add($"{stem}: score '{RenderSpecParser.LabelOf(spec.Name)}' resolves to {Path.GetFileName(target)}, "
                            + "already written by an earlier score of this file — skipped");
                        continue;
                    }
                    Add(WriteOne(target, score, options));
                }
            }
            // The exporters' own warnings (a MIDI note beyond the keyboard, a twin's
            // approximation) travel with the batch's, prefixed by the book they belong to.
            void Add(LilySharp.Core.Export.ExportResult result)
            {
                outputs.AddRange(result.Files);
                warnings.AddRange(result.Warnings.Select(w => $"{stem}: {w}"));
            }
            return new ExportResponse
            {
                Success = true,
                OutputPath = dir,
                OutputPaths = outputs.ToArray(),
                Warnings = warnings.ToArray(),
                Playbacks = editorRenders ? playbacks.ToArray() : null,
            };
        }
        catch (Exception ex)
        {
            return new ExportResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// The pictures of an MP4 the editor writes (owner's requests 2026-11-07): the score the export
    /// named (<see cref="PlaybackExport.Score"/>), drawn by <c>LilySharp.Core.Export.ScoreVideo</c>
    /// with the notes the editor will sound — the export's own, under the practice menu's tempo,
    /// parts and count-in, which the editor applies (playbackCore.exportNotes) — into a new temp
    /// folder the editor deletes.
    /// </summary>
    [JsonRpcMethod("lilysharp/videoFrames", UseSingleObjectParameterDeserialization = true)]
    public Task<VideoFramesResponse> VideoFramesAsync(VideoFramesParams @params, CancellationToken token)
        => OffDispatch(() => VideoFrames(@params), token);

    public VideoFramesResponse VideoFrames(VideoFramesParams @params)
    {
        if (ExportTreeOf(@params.TextDocument, @params.Path, out var tree, out _) is { } problem)
            return new VideoFramesResponse { Error = problem };
        string? directory = null;
        try
        {
            // The score by its name ("" the unnamed one — Choose takes it for ""), null for a
            // file that declares none.
            var score = @params.Score == null ? null : RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), @params.Score);
            var look = LilySharp.Core.Export.ScoreVideoOptions.Default;
            directory = Path.Combine(Path.GetTempPath(), "lilysharp-video-" + Guid.NewGuid().ToString("N"));
            var frames = LilySharp.Core.Export.ScoreVideo.WriteFrames(tree, score,
                // (a rest is the editor's text's light alone, PlaybackNote.E: not lit on the score)
                @params.Notes.Where(n => n.P >= 0 && !n.E)
                    .Select(n => new LilySharp.Core.Export.VideoNote(n.T, n.T + n.D, n.S, n.O)).ToList(),
                @params.Seconds, directory, look);
            return new VideoFramesResponse
            {
                Frames = frames.ConcatList, WorkDirectory = directory, Lead = look.Lead,
                Warnings = frames.Warnings.ToArray(),
            };
        }
        catch (Exception ex)
        {
            if (directory != null && Directory.Exists(directory))
                try { Directory.Delete(directory, true); } catch (IOException) { }
            return new VideoFramesResponse { Error = ex.Message };
        }
    }
}
