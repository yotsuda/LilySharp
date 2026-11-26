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
    public Task<CheckCandidateResponse> CheckCandidateAsync(CheckCandidateParams @params, CancellationToken token)
        => OffDispatch(() => CheckCandidate(@params), token);

    /// <summary>
    /// A stretch of a source — a line edited beside the scan — with the bar lines its
    /// missing-bar-line reports name written in (<see cref="LilySharp.Core.Semantics.MissingBarLines.WrittenIn"/>;
    /// owner's request 2026-10-27), or null when it has none. Read-only.
    /// </summary>
    [JsonRpcMethod("lilysharp/missingBarLines", UseSingleObjectParameterDeserialization = true)]
    public Task<MissingBarLinesResponse> MissingBarLinesAsync(MissingBarLinesParams @params, CancellationToken token)
        => OffDispatch(() => new MissingBarLinesResponse
        {
            Line = @params.Start >= 0 && @params.End <= (@params.Text ?? "").Length && @params.Start < @params.End
                ? LilySharp.Core.Semantics.MissingBarLines.WrittenIn(@params.Text!, @params.Start, @params.End) : null,
        }, token);

    /// <summary>
    /// What a new piece's notation leaves flat — parts with no instrument, an ensemble braced as
    /// a piano, a part of one note value, every part in the same rhythm
    /// (<see cref="LilySharp.Core.Editing.CompositionChecks"/>) — for the AI that composed it to
    /// revise once (owner's report 2026-10-26). Read-only, like <c>checkCandidate</c>.
    /// </summary>
    [JsonRpcMethod("lilysharp/compositionCheck", UseSingleObjectParameterDeserialization = true)]
    public Task<CompositionCheckResponse> CompositionCheckAsync(CheckCandidateParams @params, CancellationToken token)
        => OffDispatch(() =>
        {
            var findings = LilySharp.Core.Editing.CompositionChecks.Findings(@params.Text ?? "");
            return new CompositionCheckResponse
            {
                Findings = [.. findings.Select(f => f.Message)],
                Mistakes = [.. findings.Where(f => LilySharp.Core.Editing.ReviewNotes.All[f.Id].Kind == LilySharp.Core.Editing.ReviewKind.Mistake).Select(f => f.Message)],
                Ranges = [.. LilySharp.Core.Midi.PartRanges.Lines(@params.Text ?? "")],
            };
        }, token);

    /// <summary>
    /// The review's report on a score (<see cref="LilySharp.Core.Editing.ScoreReview"/>,
    /// docs/review-tool-design.md §2), for the AI that explains it to the person who wrote it. Read-only.
    /// </summary>
    [JsonRpcMethod("lilysharp/reviewScore", UseSingleObjectParameterDeserialization = true)]
    public Task<ReviewScoreResponse> ReviewScoreAsync(ReviewScoreParams @params, CancellationToken token)
        => OffDispatch(() => new ReviewScoreResponse
        {
            Report = LilySharp.Core.Editing.ScoreReview.Json(@params.Text ?? "", @params.Audience ?? "beginner", @params.Focus),
        }, token);

    /// <summary>
    /// The chord progression of a score, section by section as a chords block
    /// (<see cref="LilySharp.Core.Harmony.ChordHarmonizer.DescribeProgression"/>; owner's request
    /// 2026-11-11), for the AI that arranges it: what the score names kept as written, the rest
    /// read from the notes. Read-only.
    /// </summary>
    [JsonRpcMethod("lilysharp/readChords", UseSingleObjectParameterDeserialization = true)]
    public Task<ReadChordsResponse> ReadChordsAsync(ReadChordsParams @params, CancellationToken token)
        => OffDispatch(() => new ReadChordsResponse
        {
            Text = LilySharp.Core.Harmony.ChordHarmonizer.DescribeProgression(@params.Text ?? "", @params.RenderName, @params.Parts),
        }, token);

    /// <summary>
    /// Music written into a score by place — a part's section, some of its bars, a new part
    /// (<see cref="LilySharp.Core.Editing.PartWriter"/>; owner's proposal 2026-11-11) — for the
    /// arranging chat's tool. The text alone is made: the document is not touched.
    /// </summary>
    [JsonRpcMethod("lilysharp/writePart", UseSingleObjectParameterDeserialization = true)]
    public Task<WritePartResponse> WritePartAsync(WritePartParams @params, CancellationToken token)
        => OffDispatch(() =>
        {
            var result = LilySharp.Core.Editing.PartWriter.Apply(@params.Text ?? "",
                [.. (@params.Write ?? []).Select(w => new LilySharp.Core.Editing.PartWriter.Write(w.Part ?? "", w.Section ?? "", w.Music ?? "", w.Bars, w.Lyrics))],
                [.. (@params.NewParts ?? []).Select(p => new LilySharp.Core.Editing.PartWriter.NewPart(p.Part ?? "", p.Title, p.Short, p.Body, p.After, p.First, p.Singer))]);
            return new WritePartResponse { Text = result.Text, Problems = [.. result.Problems], Done = [.. result.Done] };
        }, token);

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
    public Task<SvgResponse> RenderTextAsync(RenderTextParams @params, CancellationToken token)
        => OffDispatch(() => RenderText(@params), token);

    public SvgResponse RenderText(RenderTextParams @params)
    {
        var tree = SyntaxTree.Parse(@params.Text ?? "");
        var (renders, drawn) = ExtractRenderInfo(tree, @params.RenderName);

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
            return new SvgResponse { Svg = null, Error = errors, Renders = renders, SelectedRender = drawn };
        }

        try
        {
            // Preview() for the AI panel (it clicks on notes); the Markdown fence asks for
            // the same drawing without the hit-rects — the host page supplies the fonts
            // either way (OmitFontFace).
            // A fence is a picture in a document, not a page: the snippet layout (one page
            // as tall as the music, cropped to its widest system).
            var options = @params.Interactive
                ? LilySharp.Core.Svg.Renderer.SvgRenderOptions.Preview()
                : new LilySharp.Core.Svg.Renderer.SvgRenderOptions { OmitFontFace = true, Snippet = @params.Fence };
            string svg;
            if (@params.Fence)
            {
                // A fence draws ONE picture: the book's DEFAULT score — the unnamed one, else
                // the first (RenderSpecParser.Choose, the preview's own choice with nothing
                // picked) — so an existing .lys pasted into a fence draws as it opens in the
                // editor (owner decision 2026-10-09; from 2026-09-10 a fence had to hold exactly
                // one score, and a book with `score tab { … }` beside its default was refused).
                // NONE is still refused (owner decision 2026-09-10, and LYS6014 for a file since
                // 2026-10-09): an implied score silently dropped whatever a score names —
                // lyrics and chord rows, tab, ensembles.
                if (RenderSpecParser.FindAll(tree).Count == 0)
                    return new SvgResponse
                    {
                        Svg = null,
                        Error = "A fence draws a score; this one declares none. Write score { … } to say what to draw.",
                        Renders = renders, SelectedRender = drawn,
                    };
                svg = LilySharp.Core.Svg.SvgGenerator.Generate(tree, options, @params.RenderName);
            }
            else
            {
                svg = LilySharp.Core.Svg.SvgGenerator.Generate(tree, options, @params.RenderName);
            }
            return new SvgResponse { Svg = svg, Error = null, Renders = renders, SelectedRender = drawn };
        }
        catch (Exception ex)
        {
            return new SvgResponse { Svg = null, Error = ex.Message, Renders = renders, SelectedRender = drawn };
        }
    }

    /// <summary>
    /// Returns the resolved musical facts of a selection so the model sees what the
    /// compiler sees (§5): each note's written token and its resolved absolute pitch
    /// (mirrors <c>check --pitches</c>), limited to notes whose source offset falls
    /// inside the requested range. Read-only.
    /// </summary>
    [JsonRpcMethod("lilysharp/factsForRange", UseSingleObjectParameterDeserialization = true)]
    public Task<FactsForRangeResponse> FactsForRangeAsync(FactsForRangeParams @params, CancellationToken token)
        => OffDispatch(() => FactsForRange(@params), token);

    public FactsForRangeResponse FactsForRange(FactsForRangeParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new FactsForRangeResponse { Error = DocumentNotFound };

        return ResolvePitches(doc.Text, doc.Tree, @params.Start, @params.End);
    }

    /// <summary>
    /// The resolved pitches of a range of an arbitrary source — a candidate the AI transform
    /// has not applied yet. The transform checks the OCTAVES of its candidate with it: the
    /// first answer to "harmonize a third above" put its harmony line in the wrong octave and
    /// compiled cleanly (owner report, 2026-09-26), since a relative-octave slip is not an
    /// error. Read-only; nothing is opened.
    /// </summary>
    [JsonRpcMethod("lilysharp/pitchesForText", UseSingleObjectParameterDeserialization = true)]
    public Task<FactsForRangeResponse> PitchesForTextAsync(PitchesForTextParams @params, CancellationToken token)
        => OffDispatch(() => PitchesForText(@params), token);

    public FactsForRangeResponse PitchesForText(PitchesForTextParams @params)
    {
        var text = @params.Text ?? "";
        return ResolvePitches(text, SyntaxTree.Parse(text), @params.Start, @params.End);
    }

    private static FactsForRangeResponse ResolvePitches(string text, SyntaxTree tree, int start, int end)
    {
        try
        {
            var collector = new LilySharp.Core.Svg.Collector.MeasureCollector();
            collector.Collect(tree);
            var trace = collector.PitchTrace;

            var facts = new List<ResolvedPitchFact>();
            foreach (var e in trace)
            {
                if (e.Position < start || e.Position >= end)
                    continue;
                // The trace position starts at the token's leading trivia; advance
                // to the pitch so the "written" token lines up (as `check --pitches`
                // does).
                int p = e.Position;
                while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
                facts.Add(new ResolvedPitchFact
                {
                    Offset = p,
                    Written = LilySharp.Core.Semantics.ResolvedPitches.ReadPitchToken(text, p),
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
}
