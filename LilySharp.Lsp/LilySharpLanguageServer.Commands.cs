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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LspRange = LilySharp.Lsp.Protocol.Range;
using LspDiagnosticSeverity = LilySharp.Lsp.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    // ============================================================
    // Custom: SVG Preview
    // ============================================================

    // Per-document incremental SVG-compile sessions (default render only). Each session
    // reuses the systems whose content is unchanged across edits, so refreshing a large
    // score's preview does not re-lay-out every bar — the dominant cost. Keyed by URI,
    // dropped on close. _svgSessionLock serializes access: GetSvg can be invoked
    // concurrently and IncrementalCompiler is stateful.
    private readonly System.Collections.Generic.Dictionary<System.Uri, IncrementalCompiler> _svgSessions = new();
    private readonly object _svgSessionLock = new();

    /// <summary>
    /// Returns the server version for debugging deployment issues.
    /// </summary>
    [JsonRpcMethod("lilysharp/version")]
    public string GetVersion()
    {
        return Version;
    }

    /// <summary>
    /// Auto-harmonizes each section's melody and returns the edits that add a diatonic
    /// chords part: a <c>chords harmony { … }</c> block after every section's melody
    /// part-block (each aligned to its own section, independent of the structure
    /// block), and a <c>chords harmony</c> row placed directly above the melody's
    /// staff in the score. Powers the "Lily#: Add Chord Track" editor command.
    /// </summary>
    [JsonRpcMethod("lilysharp/addChordTrack", UseSingleObjectParameterDeserialization = true)]
    public AddChordTrackResponse AddChordTrack(SvgParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new AddChordTrackResponse { Error = "Document not found." };

        if (SyntaxTree.Parse(doc.Text).HasErrors)
            return new AddChordTrackResponse { Error = "Fix the errors in the score first." };

        var result = LilySharp.Core.Harmony.ChordHarmonizer.AddChordTracks(doc.Text);
        if (result == null)
            return new AddChordTrackResponse { Error = "No section melody found to harmonize." };

        // One full-document replace: adding a chords part can convert the layout
        // (part-major -> section-major), which reshapes the whole file.
        var (endLine, endCol) = GetLineAndColumn(doc.Text, doc.Text.Length);
        return new AddChordTrackResponse
        {
            Edits = new[]
            {
                new ChordTrackEdit
                {
                    StartLine = 0, StartChar = 0, EndLine = endLine, EndChar = endCol, NewText = result.Value.Text,
                },
            },
            Info = result.Value.Info,
        };
    }

    /// <summary>
    /// Imports a MusicXML file (<c>.xml</c> / <c>.musicxml</c> / <c>.mxl</c>) into
    /// Lily# source. A file PATH is preferred so a binary <c>.mxl</c> zip reads
    /// directly; raw <c>XmlText</c> is the fallback. Import is opinionated and
    /// non-unique: the result is a faithful STARTING POINT, and every dropped or
    /// approximated construct comes back in <c>Warnings</c> (the import report).
    /// Powers the "Lily#: Import MusicXML…" editor command.
    /// </summary>
    [JsonRpcMethod("lilysharp/importMusicXml", UseSingleObjectParameterDeserialization = true)]
    public ImportMusicXmlResponse ImportMusicXml(ImportMusicXmlParams @params)
    {
        try
        {
            var importer = new LilySharp.Core.MusicXmlImport.MusicXmlImporter();
            (string Lys, LilySharp.Core.MusicXmlImport.ImportReport Report) result;
            if (!string.IsNullOrEmpty(@params.FilePath))
            {
                if (!System.IO.File.Exists(@params.FilePath))
                    return new ImportMusicXmlResponse { Error = $"File not found: {@params.FilePath}" };
                result = importer.ImportBytes(System.IO.File.ReadAllBytes(@params.FilePath), @params.RelativeOctave);
            }
            else if (!string.IsNullOrEmpty(@params.XmlText))
            {
                result = importer.Import(@params.XmlText, @params.RelativeOctave);
            }
            else
            {
                return new ImportMusicXmlResponse { Error = "No MusicXML file path or text provided." };
            }

            return new ImportMusicXmlResponse
            {
                Lys = result.Lys,
                Warnings = result.Report.Warnings.ToArray(),
            };
        }
        catch (Exception ex)
        {
            return new ImportMusicXmlResponse { Error = ex.Message };
        }
    }

    /// <summary>
    /// Custom request to generate SVG from a document.
    /// Used for real-time preview in VS Code.
    /// </summary>
    /// <summary>
    /// The document's tree with <c>using "..."</c> directives resolved (files read
    /// relative to the document), or the plain tree when there are no includes, plus the
    /// warnings from resolving them. The document's own text stays the prefix, so its
    /// positions are preserved.
    /// </summary>
    /// <remarks>
    /// ONE house, taking its inputs rather than reading the disk itself, so the preview,
    /// the exports and the Problems panel all read the SAME expansion and cannot disagree
    /// about what the piece is. They did disagree: the panel validated the unexpanded tree
    /// while everything else expanded, so a file with includes was told its parts were
    /// undefined by the very server that was drawing them.
    /// </remarks>
    internal static (SyntaxTree Tree, IReadOnlyList<CoreDiagnostic> UsingDiagnostics) ExpandUsings(
        string text, SyntaxTree unexpanded, string basePath, Func<string, string?> readFile)
    {
        // Ask the tree we were handed, not the text: parsing it again here would be a whole
        // extra parse per keystroke, since both the preview and the Problems panel land here.
        if (!LilySharp.Core.Parser.UsingExpander.HasUsings(unexpanded))
            return (unexpanded, []);

        var expanded = LilySharp.Core.Parser.UsingExpander.Expand(text, basePath, readFile,
            out var usingDiagnostics);
        return (SyntaxTree.Parse(expanded), usingDiagnostics);
    }

    private static (SyntaxTree Tree, IReadOnlyList<CoreDiagnostic> UsingDiagnostics) ExpandUsings(
        Document doc, Uri uri)
        => ExpandUsings(doc.Text, doc.Tree, uri.IsFile ? uri.LocalPath : string.Empty,
            p => System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : null);

    /// <summary>
    /// Every diagnostic that belongs to THIS document: the include-resolution warnings and
    /// the semantic validators run over the expanded tree, keeping only what lands inside
    /// the document's own text.
    /// </summary>
    /// <remarks>
    /// The filter is the price of validating the expansion. An included file's text is
    /// appended AFTER the document's, so a diagnostic about that file's content carries an
    /// offset past the end of this document and would squiggle nothing (or the wrong
    /// thing). It is not lost work — it is another file's diagnostic, and it belongs in
    /// that file's panel, which is a separate piece of plumbing (the LSP has one document
    /// per URI and publishes per URI).
    /// ⚠️ With no includes the expansion is the identity and the filter passes everything,
    /// so the overwhelmingly common path is unchanged.
    /// </remarks>
    internal static IReadOnlyList<CoreDiagnostic> DocumentDiagnostics(
        string text, SyntaxTree unexpanded, string basePath, Func<string, string?> readFile)
    {
        var (tree, usingDiagnostics) = ExpandUsings(text, unexpanded, basePath, readFile);

        var result = new List<CoreDiagnostic>(usingDiagnostics);
        result.AddRange(SemanticValidation.Run(tree).Where(d => d.Span.Start < text.Length));
        return result;
    }

    [JsonRpcMethod("lilysharp/svg", UseSingleObjectParameterDeserialization = true)]
    public SvgResponse GetSvg(SvgParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
        {
            return new SvgResponse
            {
                Svg = null,
                Error = "Document not found"
            };
        }

        // Resolve `using "..."` directives so the preview shows the whole piece.
        // The main file is the prefix of the combined source, so its positions
        // (data-pos editor<->preview sync) are unchanged.
        var (tree, _) = ExpandUsings(doc, @params.TextDocument.Uri);

        // Extract render definitions
        var renders = ExtractRenderInfo(tree);

        // Best-effort policy: a tree with parse errors still renders — the parser's
        // recovery drops the offending tokens, so the score that DID parse is shown
        // and the error text rides along for the preview's banner. Only when the
        // render itself fails does the response carry the error alone, and the
        // viewer falls back to its last good picture (dimmed, client-side).
        string? errorText = null;
        if (tree.HasErrors)
        {
            errorText = string.Join("\n", tree.Diagnostics
                .Where(d => d.Severity == CoreDiagnosticSeverity.Error)
                .Select(d => {
                    var (line, col) = GetLineAndColumn(tree.Text, d.Span.Start);
                    // GetLineAndColumn is 0-based (LSP protocol); the preview text is read by
                    // a human, so show 1-based line/column to match the editor gutter.
                    return $"Line {line + 1}, Col {col + 1}: {d.Message}";
                }));
        }

        try
        {
            // Preview mode: @font-face is defined in HTML, not in SVG
            var renderOptions = LilySharp.Core.Svg.Renderer.SvgRenderOptions.Preview();

            // The default render (no explicit selection) goes through the per-document
            // incremental session so an edit reuses unchanged systems. A NAMED render is
            // outside the session's scope — IncrementalCompiler always renders the first
            // score — so fall back to a full compile for those.
            var svg = string.IsNullOrEmpty(@params.RenderName)
                ? RenderSvgIncremental(@params.TextDocument.Uri, tree, renderOptions)
                : LilySharp.Core.Svg.SvgGenerator.Generate(tree, renderOptions, @params.RenderName);

            return new SvgResponse
            {
                Svg = svg,
                Error = errorText,
                Renders = renders
            };
        }
        catch (Exception ex)
        {
            return new SvgResponse
            {
                Svg = null,
                Error = errorText == null ? ex.Message : $"{errorText}\n{ex.Message}",
                Renders = renders
            };
        }
    }

    /// <summary>
    /// Renders the default score of <paramref name="tree"/> through the URI's persistent
    /// <see cref="IncrementalCompiler"/> session (created on first use), reusing unchanged
    /// systems. The output is byte-identical to <see cref="SvgGenerator.Generate"/>. Any
    /// failure in the session path drops the (possibly corrupted) session and falls back
    /// to a full compile, so the optimization can never break the preview.
    /// </summary>
    private string RenderSvgIncremental(Uri uri, SyntaxTree tree,
        LilySharp.Core.Svg.Renderer.SvgRenderOptions options)
    {
        lock (_svgSessionLock)
        {
            try
            {
                if (!_svgSessions.TryGetValue(uri, out var session))
                {
                    session = new IncrementalCompiler(tree, options);
                    _svgSessions[uri] = session;
                }
                return session.RenderIncremental(tree);
            }
            catch
            {
                _svgSessions.Remove(uri);
                return LilySharp.Core.Svg.SvgGenerator.Generate(tree, options, null);
            }
        }
    }

    /// <summary>Drops the incremental SVG session for a closed document.</summary>
    private void DropSvgSession(Uri uri)
    {
        lock (_svgSessionLock)
            _svgSessions.Remove(uri);
    }

    /// <summary>
    /// Exports the current document to a file in the requested format. The
    /// extension drives this from the preview's Export button: it shows the
    /// format/save dialog and passes the chosen path here. SVG/PNG/PDF honour the
    /// selected score (RenderName); MIDI and MusicXML export the whole piece.
    /// </summary>
    [JsonRpcMethod("lilysharp/export", UseSingleObjectParameterDeserialization = true)]
    public ExportResponse Export(ExportParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new ExportResponse { Success = false, Error = "Document not found" };

        var (tree, _) = ExpandUsings(doc, @params.TextDocument.Uri);
        if (tree.HasErrors)
        {
            var errors = string.Join("\n", tree.Diagnostics
                .Where(d => d.Severity == CoreDiagnosticSeverity.Error)
                .Select(d =>
                {
                    var (line, col) = GetLineAndColumn(tree.Text, d.Span.Start);
                    // GetLineAndColumn is 0-based (LSP protocol); the preview text is read by
                    // a human, so show 1-based line/column to match the editor gutter.
                    return $"Line {line + 1}, Col {col + 1}: {d.Message}";
                }));
            return new ExportResponse { Success = false, Error = errors };
        }

        try
        {
            var format = (@params.Format ?? "svg").ToLowerInvariant();
            var outputPath = @params.OutputPath;
            var renderName = @params.RenderName;
            switch (format)
            {
                case "svg":
                    var fontDir = LilySharp.Core.Rendering.FontLocator.Find();
                    // Embed the font so the exported SVG is self-contained; fall back
                    // to a reference if the bundled font can't be located.
                    var svgOpts = fontDir != null
                        ? LilySharp.Core.Svg.Renderer.SvgRenderOptions.Export(fontDir)
                        : LilySharp.Core.Svg.Renderer.SvgRenderOptions.Default;
                    File.WriteAllText(outputPath,
                        LilySharp.Core.Svg.SvgGenerator.Generate(tree, svgOpts, renderName));
                    break;
                case "png":
                {
                    // One file per page, LilyPond naming: single page keeps the
                    // chosen name; multiple pages save as BASE-page1.png,
                    // BASE-page2.png, … (scm/ps-to-png.scm).
                    var pages = LilySharp.Core.Png.PngGenerator.GeneratePages(tree, null, renderName);
                    if (pages.Count == 1)
                    {
                        File.WriteAllBytes(outputPath, pages[0]);
                    }
                    else
                    {
                        var dir = Path.GetDirectoryName(outputPath) ?? "";
                        var baseName = Path.GetFileNameWithoutExtension(outputPath);
                        var pngExt = Path.GetExtension(outputPath);
                        var names = new List<string>(pages.Count);
                        for (int p = 0; p < pages.Count; p++)
                        {
                            var pagePath = Path.Combine(dir, $"{baseName}-page{p + 1}{pngExt}");
                            File.WriteAllBytes(pagePath, pages[p]);
                            names.Add(Path.GetFileName(pagePath));
                        }
                        // The dialog's path names ONE file; report what was
                        // actually written so the toast isn't a lie.
                        outputPath = Path.Combine(dir, string.Join(", ", names));
                    }
                    break;
                }
                case "pdf":
                    File.WriteAllBytes(outputPath,
                        LilySharp.Core.Pdf.PdfGenerator.Generate(tree, null, renderName));
                    break;
                case "midi":
                    new LilySharp.Core.Midi.MidiExporter().Export(tree).Save(outputPath);
                    break;
                case "musicxml":
                    new LilySharp.Core.MusicXml.MusicXmlExporter().ExportToFile(tree, outputPath);
                    break;
                case "vsqx":
                    new LilySharp.Core.Vocaloid.VsqxExporter().Export(tree).Save(outputPath);
                    break;
                case "ly":
                    File.WriteAllText(outputPath,
                        new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree));
                    break;
                default:
                    return new ExportResponse { Success = false, Error = $"Unknown format: {format}" };
            }
            return new ExportResponse { Success = true, OutputPath = outputPath };
        }
        catch (Exception ex)
        {
            return new ExportResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Flattens the piece to note events in seconds for the preview's WebAudio
    /// player: the same MidiExporter model as .mid export, with the tempo map
    /// applied server-side so the webview only schedules oscillators.
    /// </summary>
    [JsonRpcMethod("lilysharp/playback", UseSingleObjectParameterDeserialization = true)]
    public PlaybackResponse GetPlayback(PlaybackParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new PlaybackResponse { Error = "Document not found" };
        var (tree, _) = ExpandUsings(doc, @params.TextDocument.Uri);
        if (tree.HasErrors)
            return new PlaybackResponse { Error = "Score has errors" };
        try
        {
            var midi = new LilySharp.Core.Midi.MidiExporter().Export(tree);
            int tpq = midi.TicksPerQuarterNote;

            // Tempo map: merged from all tracks, sorted; default 120 bpm.
            var tempos = midi.Tracks.SelectMany(t => t.TempoChanges)
                .OrderBy(t => t.Tick).ToList();
            double SecondsAt(int tick)
            {
                double sec = 0;
                int prevTick = 0;
                double usPerBeat = 500000; // 120 bpm
                foreach (var tc in tempos)
                {
                    if (tc.Tick >= tick)
                        break;
                    sec += (tc.Tick - prevTick) * usPerBeat / tpq / 1e6;
                    prevTick = tc.Tick;
                    usPerBeat = tc.MicrosecondsPerBeat;
                }
                return sec + (tick - prevTick) * usPerBeat / tpq / 1e6;
            }

            var notes = midi.Tracks
                .SelectMany(t => t.Notes)
                .OrderBy(n => n.StartTick)
                .Select(n =>
                {
                    double t0 = SecondsAt(n.StartTick);
                    return new PlaybackNote
                    {
                        T = t0,
                        D = Math.Max(0.03, SecondsAt(n.StartTick + n.DurationTicks) - t0),
                        P = n.Pitch + 0.5 * n.QuarterBend,
                        V = n.Velocity,
                        S = n.SourcePos,
                        O = n.SourceOrdinal,
                        I = n.Timbre,
                    };
                })
                .ToArray();
            return new PlaybackResponse { Notes = notes };
        }
        catch (Exception ex)
        {
            return new PlaybackResponse { Error = ex.Message };
        }
    }

    /// <summary>
    /// Converts the document between the section-major and part-major authoring
    /// layouts (the editor command toggles whichever the file currently uses) and
    /// returns the rewritten source; the extension applies it as a full-document edit.
    /// </summary>
    /// <summary>
    /// The "Extract phrase" refactoring: lifts the section music at the caret (or
    /// the whole measures a selection touches) into a top-level phrase and replaces
    /// it with the reference. Verified semantics-preserving (the MIDI of the old
    /// and new documents must match) or refused with no changes — see
    /// <see cref="LilySharp.Core.Editing.PhraseExtractor"/>.
    /// </summary>
    [JsonRpcMethod("lilysharp/extractPhrase", UseSingleObjectParameterDeserialization = true)]
    public ExtractPhraseResponse ExtractPhrase(ExtractPhraseParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new ExtractPhraseResponse { Success = false, Error = "Document not found" };

        var result = LilySharp.Core.Editing.PhraseExtractor.Extract(
            doc.Text, @params.SelectionStart, @params.SelectionEnd, @params.Name);
        return result.NewText != null
            ? new ExtractPhraseResponse { Success = true, NewText = result.NewText }
            : new ExtractPhraseResponse { Success = false, Error = result.Error };
    }

    [JsonRpcMethod("lilysharp/convertLayout", UseSingleObjectParameterDeserialization = true)]
    public ConvertLayoutResponse ConvertLayout(ConvertLayoutParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new ConvertLayoutResponse { Success = false, Error = "Document not found" };

        // Refuse to convert a file with syntax errors: cell extraction needs a
        // clean, balanced tree, and the client overwrites the whole document with
        // the result — so a malformed file would be mangled. Leave it untouched.
        if (LilySharp.Core.Syntax.SyntaxTree.Parse(doc.Text).HasErrors)
            return new ConvertLayoutResponse
            {
                Success = false,
                Error = "Fix the syntax errors before converting the layout — no changes made."
            };

        var from = LilySharp.Core.Editing.PartSectionLayoutConverter.Detect(doc.Text);
        if (from == LilySharp.Core.Editing.LayoutForm.Unknown)
            return new ConvertLayoutResponse
            {
                Success = false,
                Error = "No part/section layout to convert — the file needs parts with sections."
            };

        // Chord/lyric blocks only exist in the section-major layout; converting to
        // part-major would drop them. Explain and keep the file unchanged.
        if (from == LilySharp.Core.Editing.LayoutForm.SectionMajor
            && LilySharp.Core.Editing.PartSectionLayoutConverter.HasUntransposableSectionContent(doc.Text))
            return new ConvertLayoutResponse
            {
                Success = false,
                Error = "This file has chords/lyrics blocks, which exist only in the section-major "
                    + "layout. Converting to part-major would drop them, so it was left unchanged."
            };

        // Convert self-guards: it returns null unless the result round-trips to a
        // clean parse, so this can never produce a corrupt document.
        var newText = LilySharp.Core.Editing.PartSectionLayoutConverter.Convert(doc.Text);
        if (newText == null)
            return new ConvertLayoutResponse
            {
                Success = false,
                Error = "Conversion would not produce a clean result — no changes made."
            };

        var to = from == LilySharp.Core.Editing.LayoutForm.PartMajor
            ? LilySharp.Core.Editing.LayoutForm.SectionMajor
            : LilySharp.Core.Editing.LayoutForm.PartMajor;
        return new ConvertLayoutResponse
        {
            Success = true,
            NewText = newText,
            FromLayout = from.ToString(),
            ToLayout = to.ToString(),
        };
    }

    // ============================================================
    // Custom: AI collaborative editing (docs/ai-collab-design)
    // ============================================================

    /// <summary>
    /// Validates an arbitrary candidate source string — parser diagnostics plus the
    /// full semantic validator registry (the same set <c>check</c> and the push
    /// diagnostics use, so they can't drift). The candidate is parsed in isolation
    /// and NEVER touches document state. Powers the AI transform's
    /// validate-and-self-repair loop: a candidate with new errors is repaired (its
    /// diagnostics fed back to the model) before it is ever shown to the user.
    /// </summary>
    [JsonRpcMethod("lilysharp/checkCandidate", UseSingleObjectParameterDeserialization = true)]
    public CheckCandidateResponse CheckCandidate(CheckCandidateParams @params)
    {
        var text = @params.Text ?? "";
        var tree = SyntaxTree.Parse(text);

        var diags = new List<CandidateDiagnostic>();
        void Add(LilySharp.Core.Syntax.Diagnostic d)
        {
            var (line, col) = GetLineAndColumn(text, d.Span.Start);
            diags.Add(new CandidateDiagnostic
            {
                Line = line,
                Char = col,
                Offset = d.Span.Start,
                Length = d.Span.Length,
                Severity = d.Severity switch
                {
                    CoreDiagnosticSeverity.Error => "error",
                    CoreDiagnosticSeverity.Warning => "warning",
                    CoreDiagnosticSeverity.Info => "info",
                    _ => "hint"
                },
                Message = d.Message,
                Code = d.Code,
            });
        }

        foreach (var d in tree.Diagnostics)
            Add(d);
        // Semantic validation can throw on a badly-shaped-but-parseable tree; a
        // candidate that trips it is simply "not valid", not a server error.
        try
        {
            foreach (var d in LilySharp.Core.Semantics.SemanticValidation.Run(tree))
                Add(d);
        }
        catch (Exception ex)
        {
            diags.Add(new CandidateDiagnostic { Message = $"Validation failed: {ex.Message}" });
        }

        return new CheckCandidateResponse
        {
            HasErrors = diags.Any(d => d.Severity == "error"),
            Diagnostics = diags.ToArray(),
        };
    }

    /// <summary>
    /// Renders an arbitrary candidate source string to preview SVG without touching
    /// the open document — the non-destructive, offscreen compile behind "decide on
    /// the score" (§3/§7). Same render path and Preview() options as
    /// <see cref="GetSvg"/>, but the text is supplied directly rather than read from
    /// document state.
    /// </summary>
    [JsonRpcMethod("lilysharp/renderText", UseSingleObjectParameterDeserialization = true)]
    public SvgResponse RenderText(RenderTextParams @params)
    {
        var tree = SyntaxTree.Parse(@params.Text ?? "");
        var renders = ExtractRenderInfo(tree);

        if (tree.HasErrors)
        {
            var errors = string.Join("\n", tree.Diagnostics
                .Where(d => d.Severity == CoreDiagnosticSeverity.Error)
                .Select(d =>
                {
                    var (line, col) = GetLineAndColumn(tree.Text, d.Span.Start);
                    // GetLineAndColumn is 0-based (LSP protocol); the preview text is read by
                    // a human, so show 1-based line/column to match the editor gutter.
                    return $"Line {line + 1}, Col {col + 1}: {d.Message}";
                }));
            return new SvgResponse { Svg = null, Error = errors, Renders = renders };
        }

        try
        {
            var svg = LilySharp.Core.Svg.SvgGenerator.Generate(
                tree, LilySharp.Core.Svg.Renderer.SvgRenderOptions.Preview(), @params.RenderName);
            return new SvgResponse { Svg = svg, Error = null, Renders = renders };
        }
        catch (Exception ex)
        {
            return new SvgResponse { Svg = null, Error = ex.Message, Renders = renders };
        }
    }

    /// <summary>
    /// Returns the resolved musical facts of a selection so the model sees what the
    /// compiler sees (§5): each note's written token and its resolved absolute pitch
    /// (mirrors <c>check --pitches</c>), limited to notes whose source offset falls
    /// inside the requested range. Read-only.
    /// </summary>
    [JsonRpcMethod("lilysharp/factsForRange", UseSingleObjectParameterDeserialization = true)]
    public FactsForRangeResponse FactsForRange(FactsForRangeParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new FactsForRangeResponse { Error = "Document not found" };

        var text = doc.Text;
        try
        {
            var collector = new LilySharp.Core.Svg.Collector.MeasureCollector();
            collector.Collect(doc.Tree);
            var trace = collector.PitchTrace;

            var facts = new List<ResolvedPitchFact>();
            foreach (var e in trace)
            {
                if (e.Position < @params.Start || e.Position >= @params.End)
                    continue;
                // The trace position starts at the token's leading trivia; advance
                // to the pitch so the "written" token lines up (as `check --pitches`
                // does).
                int p = e.Position;
                while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
                facts.Add(new ResolvedPitchFact
                {
                    Offset = p,
                    Written = ReadPitchTokenAt(text, p),
                    Resolved = e.Pitch,
                });
            }
            return new FactsForRangeResponse { Pitches = facts.ToArray() };
        }
        catch (Exception ex)
        {
            return new FactsForRangeResponse { Error = ex.Message };
        }
    }

    /// <summary>Reads a bare pitch token (letters plus <c>'</c>/<c>,</c> octave
    /// marks) at <paramref name="pos"/>, for the resolved-pitch facts display.</summary>
    private static string ReadPitchTokenAt(string source, int pos)
    {
        if (pos < 0 || pos >= source.Length) return "";
        int end = pos;
        while (end < source.Length &&
               (char.IsLetter(source[end]) || source[end] == '\'' || source[end] == ','))
            end++;
        return source.Substring(pos, end - pos);
    }

    /// <summary>
    /// Extract render definitions from the syntax tree.
    /// </summary>
    private RenderInfo[] ExtractRenderInfo(SyntaxTree tree)
    {
        var renders = new List<RenderInfo>();
        // Render declarations only parse at the top level (Parser.ParseTopLevelItem's
        // ScoreKeyword arm), and this runs per preview request — ChildNodes, not a
        // whole-tree DescendantNodes materialization (RenderSpecParser.FindAll's shape).
        foreach (var node in tree.GetRoot().ChildNodes())
        {
            if (node is RenderDeclarationSyntax render)
            {
                // `score <FormName> ["basename"] { ... }`.
                string basename = render.BasenameText ?? "";
                string formName = render.FormNameText;
                // Picker label / --score selector: the basename when given, else the
                // form name — so two scores on the same form still read distinctly
                // (e.g. "main" and "あいう"). FindByName matches either.
                string label = basename.Length > 0 ? basename : formName;
                // Export basename: an explicit basename wins; else the reserved form
                // `main` writes to the input file's name (empty ⇒ the previewer uses
                // the source .lys stem); any other form name becomes the file name.
                string exportName = basename.Length > 0 ? basename
                    : formName == "main" ? "" : formName;

                renders.Add(new RenderInfo
                {
                    Name = label,
                    Type = "score",
                    Filename = exportName
                });
            }
        }
        return renders.ToArray();
    }

}
