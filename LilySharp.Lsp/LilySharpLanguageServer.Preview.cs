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
    // Custom: SVG Preview
    // ============================================================

    /// <summary>
    /// One (document, render name) selection's incremental SVG-compile state. The session
    /// reuses the systems whose content is unchanged across edits, so refreshing a large
    /// score's preview does not re-lay-out every bar — the dominant cost. A NAMED render
    /// is a session of its own (2026-08-26 review, finding 3-1 — it used to bypass the
    /// session machinery and pay a cold compile per keystroke): IncrementalCompiler
    /// renders ONE spec per session, so the selection is part of the key ("" = default).
    /// </summary>
    internal sealed class SvgSessionSlot
    {
        /// <summary>Serializes this selection's renders: <see cref="IncrementalCompiler"/>
        /// is stateful, so two renders of the same selection may not interleave. Held for
        /// the duration of one render and nothing else — different selections (other
        /// documents, other named renders) render in parallel. The old design held ONE
        /// cross-document lock around every render (2026-08-26 review, appendix F).</summary>
        public readonly object Gate = new();

        /// <summary>The selection's incremental session — null until the first render,
        /// reset to null when a render fails (the next request starts fresh).
        /// Guarded by <see cref="Gate"/>.</summary>
        public IncrementalCompiler? Session;

        /// <summary>The newest ticket issued for this selection (via Interlocked, ON the
        /// dispatch thread, so arrival order alone decides which request is newest). A
        /// render whose ticket is stale answers <see cref="SvgResponse.Superseded"/>
        /// without rendering, so a typing burst's queued previews collapse to the newest
        /// instead of running serially to thrown-away answers.</summary>
        public int LatestTicket;

        /// <summary>The cancellation of the render RUNNING under <see cref="Gate"/>, if
        /// any — cancelled ON the dispatch thread the moment a newer request for this
        /// selection takes a ticket, so the running render gives up at its next stage
        /// boundary (<c>IncrementalCompiler.RenderIncremental(tree, token)</c>) instead of
        /// finishing an answer nobody wants while the newer request waits for the gate
        /// (the `lock` term of the client's timing line). Guarded by
        /// <see cref="RunningGate"/>, never by <see cref="Gate"/> (the canceller must not
        /// wait for the render it cancels).</summary>
        public CancellationTokenSource? Running;
        public readonly object RunningGate = new();

        /// <summary>The <see cref="SvgPages.Version"/> of the page set the session's most
        /// recent render produced — the picture the session classifies the next page set
        /// against — or 0 when the most recent render was not a pages render (a one-string
        /// answer, the fallback, none yet). A request whose <see cref="SvgParams.ShownVersion"/>
        /// equals it is answered with a delta. Guarded by <see cref="Gate"/>.</summary>
        public int LastPagesVersion;

        public void CancelRunning()
        {
            lock (RunningGate)
                Running?.Cancel();
        }
    }

    /// <summary>The page-set version counter, one for the whole server: a version names one
    /// picture of one selection, so a viewer that was showing another score (another slot)
    /// can never present a version this slot's session would take for its own previous
    /// render.</summary>
    private static int s_pagesVersion;

    // All of a document's slots are dropped on close. _svgSessionLock guards the MAP
    // only — never a render (that is each slot's Gate).
    private readonly System.Collections.Generic.Dictionary<(System.Uri Uri, string RenderName), SvgSessionSlot> _svgSessions = new();
    private readonly object _svgSessionLock = new();

    /// <summary>This selection's slot (created on first use).</summary>
    internal SvgSessionSlot SvgSlotFor(System.Uri uri, string? renderName)
    {
        var key = (uri, renderName ?? string.Empty);
        lock (_svgSessionLock)
        {
            if (!_svgSessions.TryGetValue(key, out var slot))
                _svgSessions[key] = slot = new SvgSessionSlot();
            return slot;
        }
    }

    /// <summary>
    /// Custom request to generate SVG from a document.
    /// Used for real-time preview in VS Code.
    /// </summary>
    [JsonRpcMethod("lilysharp/svg", UseSingleObjectParameterDeserialization = true)]
    public Task<SvgResponse> GetSvgAsync(SvgParams @params, CancellationToken token)
    {
        // The clock starts here, ON the dispatch thread: everything after this line is
        // the server's own time, everything before it is transport or the dispatch loop.
        var receipt = SvgReceipt.Now();
        NoteSvgActivity();
        // The ticket is taken here, ON the dispatch thread, before the work races onto
        // the thread pool: arrival order alone decides which request is newest.
        var slot = SvgSlotFor(@params.TextDocument.Uri, @params.RenderName);
        int ticket = System.Threading.Interlocked.Increment(ref slot.LatestTicket);
        // ...and the render already running for an OLDER ticket is told to stop.
        slot.CancelRunning();
        return Task.Run(() => GetSvg(@params, slot, ticket, token, receipt), token);
    }

    /// <summary>Direct entry (tests, in-process callers): a fresh newest ticket, no
    /// cancellation — behaves exactly like a lone RPC request.</summary>
    public SvgResponse GetSvg(SvgParams @params)
    {
        var slot = SvgSlotFor(@params.TextDocument.Uri, @params.RenderName);
        return GetSvg(@params, slot,
            System.Threading.Interlocked.Increment(ref slot.LatestTicket), CancellationToken.None,
            SvgReceipt.Now());
    }

    /// <summary>When a lilysharp/svg request reached the dispatch thread — the origin
    /// of every duration in <see cref="SvgTiming"/>.</summary>
    internal readonly record struct SvgReceipt(long Timestamp, long UnixMs)
    {
        public static SvgReceipt Now() => new(
            System.Diagnostics.Stopwatch.GetTimestamp(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public double MsSince(long timestamp)
            => System.Diagnostics.Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
    }

    /// <summary>Duration of the most recent diagnostics run (see
    /// <see cref="SvgTiming.DiagnosticsLastMs"/>); written by PublishDiagnostics.</summary>
    private static double _lastDiagnosticsMs;

    /// <summary>Duration of the most recent didChange handler on the dispatch thread
    /// (see <see cref="SvgTiming.LastDidChangeMs"/>).</summary>
    private static double _lastDidChangeMs;

    /// <summary>Stopwatch timestamp of the most recent svg activity — a request received
    /// or a response handed back — for the diagnostics pass to yield to
    /// (<see cref="SvgQuietMs"/>). 0 until the first request.</summary>
    private static long _lastSvgActivity;

    /// <summary>How long after the last svg activity the diagnostics pass may start.</summary>
    private const int SvgQuietMs = 300;

    private static void NoteSvgActivity()
        => System.Threading.Volatile.Write(ref _lastSvgActivity, System.Diagnostics.Stopwatch.GetTimestamp());

    /// <summary>Milliseconds since the last svg activity (infinite before the first).</summary>
    private static double SvgActivityAgeMs()
    {
        long at = System.Threading.Volatile.Read(ref _lastSvgActivity);
        return at == 0 ? double.PositiveInfinity
            : System.Diagnostics.Stopwatch.GetElapsedTime(at).TotalMilliseconds;
    }

    private SvgResponse WithTiming(SvgResponse response, SvgTiming timing, SvgReceipt receipt)
    {
        timing.TotalServerMs = receipt.MsSince(receipt.Timestamp);
        timing.ServerSentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        timing.DiagnosticsLastMs = System.Threading.Volatile.Read(ref _lastDiagnosticsMs);
        timing.LastDidChangeMs = System.Threading.Volatile.Read(ref _lastDidChangeMs);
        timing.ThreadPoolThreads = System.Threading.ThreadPool.ThreadCount;
        timing.PendingWorkItems = System.Threading.ThreadPool.PendingWorkItemCount;
        NoteSvgActivity();
        response.Timing = timing;
        return response;
    }

    /// <summary>True when a newer request for the same selection has taken a ticket —
    /// this request's answer is already unwanted.</summary>
    private static bool IsStale(SvgSessionSlot slot, int ticket)
        => System.Threading.Volatile.Read(ref slot.LatestTicket) != ticket;

    // No Svg and no Error: an extension without the flag logs-and-ignores this shape,
    // one with it drops the response. The picture the preview wants is on the NEWER
    // request's answer, which is either rendering or queued behind this return.
    private static SvgResponse SupersededResponse() => new() { Superseded = true };

    /// <summary>Test entry with an explicit ticket: the clock starts at the call.</summary>
    internal SvgResponse GetSvg(SvgParams @params, SvgSessionSlot slot, int ticket, CancellationToken token)
        => GetSvg(@params, slot, ticket, token, SvgReceipt.Now());

    internal SvgResponse GetSvg(SvgParams @params, SvgSessionSlot slot, int ticket, CancellationToken token,
        SvgReceipt receipt)
    {
        var timing = new SvgTiming
        {
            ReceivedAfterMs = @params.ClientSentAt is { } sentAt ? receipt.UnixMs - sentAt : null,
            QueuedMs = receipt.MsSince(receipt.Timestamp),
        };
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
        {
            return WithTiming(new SvgResponse
            {
                Svg = null,
                Error = DocumentNotFound
            }, timing, receipt);
        }

        // Superseded already? A typing burst queues several previews; only the newest
        // is worth even the expansion below, let alone the render.
        if (IsStale(slot, ticket))
            return WithTiming(SupersededResponse(), timing, receipt);

        long expandStart = System.Diagnostics.Stopwatch.GetTimestamp();
        // Resolve `using "..."` directives so the preview shows the whole piece.
        // The main file is the prefix of the combined source, so its positions
        // (data-pos editor<->preview sync) are unchanged.
        var (tree, _) = ExpandUsings(doc, @params.TextDocument.Uri);

        // The picker's list AND the entry this request resolves to, from one walk and
        // the renderer's own rule (RenderSpecParser.ScoreIndex).
        var (renders, drawnRender) = ExtractRenderInfo(tree, @params.RenderName);
        timing.ExpandMs = receipt.MsSince(expandStart);

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

            string? svg;
            SvgPages? pages;
            long lockStart = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (slot.Gate)
            {
                timing.LockWaitMs = receipt.MsSince(lockStart);
                // The queue-collapse point: while this request waited for the running
                // render, newer ones may have arrived — every stale request answers
                // here without rendering, and only the newest pays for a render.
                if (IsStale(slot, ticket))
                    return WithTiming(SupersededResponse(), timing, receipt);
                // The client no longer wants this answer ($/cancelRequest while
                // queued): answer with the cancel error, not a render.
                token.ThrowIfCancellationRequested();

                long renderStart = System.Diagnostics.Stopwatch.GetTimestamp();
                // The render's own cancellation: the client's token (a $/cancelRequest)
                // and the slot's "a newer ticket arrived" signal, either of which stops
                // it at its next stage boundary.
                using var running = CancellationTokenSource.CreateLinkedTokenSource(token);
                lock (slot.RunningGate)
                    slot.Running = running;
                try
                {
                    (svg, pages) = RenderSvgIncremental(slot, @params, tree, renderOptions, running.Token);
                }
                finally
                {
                    lock (slot.RunningGate)
                        if (ReferenceEquals(slot.Running, running))
                            slot.Running = null;
                }
                timing.RenderMs = receipt.MsSince(renderStart);
            }

            return WithTiming(new SvgResponse
            {
                Svg = svg,
                Pages = pages,
                Error = errorText,
                Renders = renders,
                SelectedRender = drawnRender,
                ChartTranspose = ChartTransposeOf(tree, @params.RenderName),
            }, timing, receipt);
        }
        catch (OperationCanceledException)
        {
            // StreamJsonRpc answers a cancelled request with the protocol's cancel
            // error; folding it into the error banner below would paint a scary
            // "Failed to generate preview" for a picture nobody asked to keep.
            throw;
        }
        catch (Exception ex)
        {
            return WithTiming(new SvgResponse
            {
                Svg = null,
                Error = errorText == null ? ex.Message : $"{errorText}\n{ex.Message}",
                Renders = renders,
                SelectedRender = drawnRender
            }, timing, receipt);
        }
    }

    /// <summary>
    /// Renders the score the request's <see cref="SvgParams.RenderName"/> selects
    /// (null/empty = default) through the selection's persistent
    /// <see cref="IncrementalCompiler"/> session (created on first use), reusing unchanged
    /// systems. The output is byte-identical to <see cref="SvgGenerator.Generate"/> with
    /// the same name — as pages (<see cref="SvgPages"/>) when the request asked for them,
    /// else as one string. Any failure in the session path drops the (possibly corrupted)
    /// session and falls back to a full compile, as one string, so the optimization can
    /// never break the preview. The caller holds <paramref name="slot"/>'s Gate.
    /// </summary>
    private static (string? Svg, SvgPages? Pages) RenderSvgIncremental(SvgSessionSlot slot,
        SvgParams @params, SyntaxTree tree, LilySharp.Core.Svg.Renderer.SvgRenderOptions options,
        CancellationToken token)
    {
        string? renderName = @params.RenderName;
        try
        {
            slot.Session ??= new IncrementalCompiler(tree, options, renderName);
            if (!@params.PageDiff)
            {
                slot.LastPagesVersion = 0;
                return (slot.Session.RenderIncremental(tree, token), null);
            }
            var set = slot.Session.RenderIncrementalPages(tree, token);
            // The session classified the set against ITS previous pages render, which is
            // the picture of LastPagesVersion: a delta serves a client that holds exactly
            // that picture. Anyone else — a fresh viewer, a client whose response was
            // dropped, a viewer that was showing another score — gets every page.
            int previous = slot.LastPagesVersion;
            int version = System.Threading.Interlocked.Increment(ref s_pagesVersion);
            slot.LastPagesVersion = version;
            bool delta = previous != 0 && @params.ShownVersion == previous;
            return (null, PagesOf(set, version, delta ? previous : null));
        }
        catch (OperationCanceledException)
        {
            // A render given up on request leaves the session sound (its contract);
            // the newer request renders from it. Not a failure, so not the fallback.
            throw;
        }
        catch
        {
            slot.Session = null;
            slot.LastPagesVersion = 0;
            return (LilySharp.Core.Svg.SvgGenerator.Generate(tree, options, renderName), null);
        }
    }

    /// <summary>The semitones the score <paramref name="renderName"/> draws moves the music by: the
    /// file's top-level <c>transpose</c> and the score's own (<see cref="SvgResponse.ChartTranspose"/>).</summary>
    internal static int ChartTransposeOf(SyntaxTree tree, string? renderName)
    {
        int semitones = LilySharp.Core.Semantics.FileTranspose.ReadSemitones(tree.GetRoot());
        if (RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), renderName)?.ScoreTranspose is { } t)
            semitones += LilySharp.Core.Semantics.PitchTransposer.IntervalSemitones(t.step, t.alt, t.oct);
        return semitones;
    }

    /// <summary>The wire shape of a page set: with <paramref name="baseVersion"/>, a delta
    /// (markup on the changed pages only); without, every page's markup.</summary>
    private static SvgPages PagesOf(LilySharp.Core.Rendering.Svg.SvgPageSet set, int version, int? baseVersion)
    {
        var items = new SvgPageItem[set.Pages.Length];
        bool shifted = false;
        for (int i = 0; i < items.Length; i++)
        {
            var change = baseVersion == null ? LilySharp.Core.Rendering.Svg.SvgPageChange.Changed : set.Changes[i];
            shifted |= change == LilySharp.Core.Rendering.Svg.SvgPageChange.Shifted;
            items[i] = new SvgPageItem
            {
                Change = change switch
                {
                    LilySharp.Core.Rendering.Svg.SvgPageChange.Same => "same",
                    LilySharp.Core.Rendering.Svg.SvgPageChange.Shifted => "shifted",
                    _ => "changed",
                },
                Markup = change == LilySharp.Core.Rendering.Svg.SvgPageChange.Changed ? set.Pages[i] : null,
            };
        }
        return new SvgPages
        {
            Version = version,
            BaseVersion = baseVersion,
            Head = set.Head,
            Tail = set.Tail,
            Window = shifted
                ? new SvgEditWindowDto { Prefix = set.Window.Prefix, SuffixStart = set.Window.SuffixStart, Delta = set.Window.Delta }
                : null,
            Items = items,
        };
    }

    /// <summary>Drops every incremental SVG session of a closed document (one per
    /// render selection previewed in it). An in-flight render on a dropped slot
    /// finishes harmlessly on its own reference; a reopened document starts fresh.</summary>
    private void DropSvgSession(Uri uri)
    {
        lock (_svgSessionLock)
        {
            var stale = new System.Collections.Generic.List<(Uri, string)>();
            foreach (var key in _svgSessions.Keys)
                if (key.Uri == uri)
                    stale.Add(key);
            foreach (var key in stale)
                _svgSessions.Remove(key);
        }
    }

    /// <summary>
    /// The preview's score picker: one entry per <c>score</c> block (its LABEL — the score's
    /// name, or <c>(Default)</c> for the unnamed one — and the output name the client sends
    /// back to select it, empty for the unnamed one), plus the output name
    /// <paramref name="renderName"/> actually resolves to, so the response can say WHICH
    /// score was drawn.
    /// </summary>
    /// <remarks>
    /// ⚠️ The list and the resolution both come from
    /// <see cref="RenderSpecParser.ScoreIndex"/> — the renderer's own rule. This method
    /// used to compute the value itself while the renderer resolved another: a score could
    /// then be offered but never selected, and picking it silently drew the FIRST score
    /// (2026-09-06, owner report — the preview looked stuck on the default score).
    /// </remarks>
    private static (RenderInfo[] Renders, string? Drawn) ExtractRenderInfo(
        SyntaxTree tree, string? renderName = null)
    {
        var (scores, chosen) = RenderSpecParser.ScoreIndex(tree, renderName);
        var renders = new RenderInfo[scores.Count];
        for (int i = 0; i < scores.Count; i++)
        {
            renders[i] = new RenderInfo
            {
                Name = scores[i].Label,
                Type = "score",
                Filename = scores[i].Name,
            };
        }
        return (renders, chosen >= 0 ? scores[chosen].Name : null);
    }
}
