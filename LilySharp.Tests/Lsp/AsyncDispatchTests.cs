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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using StreamJsonRpc;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The LSP's request dispatch (2026-08-26 review, appendix F finding 1). Every request
/// handler runs OFF the RPC dispatch loop with a client-cancellable token, and
/// <c>lilysharp/svg</c> collapses a burst of queued previews to the newest request
/// (latest-wins tickets per (document, render name) slot). The end-to-end nets hold a
/// slot's render gate from the test and prove the dispatch loop keeps serving other
/// requests meanwhile — with the old synchronous handlers the svg handler would be
/// stuck ON the dispatch loop, so the poison "wrap the request handlers back to
/// synchronous calls" turns those nets red by timeout, and the poison "drop the
/// in-gate stale check" turns the supersede assertions red.
/// </summary>
public class AsyncDispatchTests
{
    /// <summary>Generous ceiling for the e2e awaits: these tests are event-ordered,
    /// not timing-based — the timeout only converts a deadlock into a red test.</summary>
    private static readonly TimeSpan Net = TimeSpan.FromSeconds(30);

    private static readonly string Doc = MusicSource.Wrap(
        "c'4 d' e' f' | g'1 |",
        """
        octave absolute
        time 4/4
        key c major
        """);

    /// <summary>Same shape as <see cref="Doc"/>, no note in common — the ordering net
    /// tells the two apart by their written pitches.</summary>
    private static readonly string Doc2 = MusicSource.Wrap(
        "d'4 e' f' g' | a'1 |",
        """
        octave absolute
        time 4/4
        key c major
        """);

    private static LilySharpLanguageServer OpenedServer(Uri uri, string? text = null)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, Text = text ?? Doc, Version = 1, LanguageId = "lilysharp",
            },
        });
        return server;
    }

    private static SvgParams Params(Uri uri) => new()
    {
        TextDocument = new TextDocumentIdentifier { Uri = uri },
    };

    // ---- latest-wins tickets, exercised in-process ----

    [Fact]
    public void Svg_StaleTicket_AnswersSupersededWithoutRendering()
    {
        var uri = new Uri("file:///supersede.lys");
        var server = OpenedServer(uri);
        var slot = server.SvgSlotFor(uri, null);
        int stale = Interlocked.Increment(ref slot.LatestTicket);
        Interlocked.Increment(ref slot.LatestTicket); // a newer request took a ticket

        var response = server.GetSvg(Params(uri), slot, stale, CancellationToken.None);

        Assert.True(response.Superseded);
        Assert.Null(response.Svg);
        Assert.Null(response.Error);
        Assert.Null(slot.Session); // the render never ran
    }

    [Fact]
    public void Svg_DirectCalls_RenderAndReuseTheSession()
    {
        var uri = new Uri("file:///direct.lys");
        var server = OpenedServer(uri);

        var first = server.GetSvg(Params(uri));
        var slot = server.SvgSlotFor(uri, null);
        Assert.False(first.Superseded);
        Assert.NotNull(first.Svg);
        Assert.Contains("<svg", first.Svg);
        Assert.NotNull(slot.Session);

        // An unchanged document through the SAME incremental session = the same picture.
        var session = slot.Session;
        var second = server.GetSvg(Params(uri));
        Assert.Equal(first.Svg, second.Svg);
        Assert.Same(session, slot.Session);
    }

    [Fact]
    public void Svg_CancelledToken_AnswersWithCancellationNotAnErrorBanner()
    {
        var uri = new Uri("file:///cancelled.lys");
        var server = OpenedServer(uri);
        var slot = server.SvgSlotFor(uri, null);
        int ticket = Interlocked.Increment(ref slot.LatestTicket);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // OperationCanceledException, NOT an SvgResponse with an Error: StreamJsonRpc
        // turns the throw into the protocol's cancel answer, while an Error would paint
        // "Failed to generate preview" for a picture nobody asked to keep.
        Assert.ThrowsAny<OperationCanceledException>(
            () => server.GetSvg(Params(uri), slot, ticket, cts.Token));
        Assert.Null(slot.Session);
    }

    // ---- end-to-end over a real StreamJsonRpc connection ----

    /// <summary>Holds a slot's render gate from a dedicated thread (Monitor is
    /// thread-affine, and the test body hops threads at every await), released on
    /// Dispose. While held, any render of that slot is queued exactly where a long
    /// render would queue it.</summary>
    private sealed class GateHold : IDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;

        public GateHold(object gate)
        {
            // Not disposed: the holder thread may still be inside Set() when Wait()
            // returns here, and a leaked event slim is nothing next to a racy Dispose.
            var held = new ManualResetEventSlim();
            _thread = new Thread(() => { lock (gate) { held.Set(); _release.Wait(); } })
            {
                IsBackground = true,
            };
            _thread.Start();
            held.Wait();
        }

        public void Dispose()
        {
            _release.Set();
            _thread.Join();
            _release.Dispose();
        }
    }

    private static (JsonRpc Client, LilySharpLanguageServer Server) Connect(
        Action<LilySharpLanguageServer>? beforeListening = null)
    {
        var (clientStream, serverStream) = Nerdbank.Streams.FullDuplexStream.CreatePair();
        var server = new LilySharpLanguageServer(serverStream, serverStream);
        beforeListening?.Invoke(server);
        _ = server.RunAsync();
        return (JsonRpc.Attach(clientStream), server);
    }

    /// <summary>Forwards everything to the real strategy and reports when any inbound
    /// request's token actually cancels. StreamJsonRpc applies $/cancelRequest in the
    /// BACKGROUND (measured: a version-request fence after Cancel() still lost the
    /// race — the gate released, the handler saw an un-cancelled token, and the render
    /// won), so the only deterministic wait is on the server-side token itself.</summary>
    private sealed class InboundCancellationObserver : ICancellationStrategy
    {
        private readonly ICancellationStrategy _inner;
        private readonly TaskCompletionSource _cancelled;

        public InboundCancellationObserver(ICancellationStrategy inner, TaskCompletionSource cancelled)
        {
            _inner = inner;
            _cancelled = cancelled;
        }

        public void CancelOutboundRequest(RequestId requestId) => _inner.CancelOutboundRequest(requestId);
        public void OutboundRequestEnded(RequestId requestId) => _inner.OutboundRequestEnded(requestId);
        public void IncomingRequestEnded(RequestId requestId) => _inner.IncomingRequestEnded(requestId);

        public void IncomingRequestStarted(RequestId requestId, CancellationTokenSource cancellationTokenSource)
        {
            // Only a request the client cancels ever fires this (the fences complete
            // uncancelled), so "any token cancelled" identifies the svg request.
            cancellationTokenSource.Token.Register(() => _cancelled.TrySetResult());
            _inner.IncomingRequestStarted(requestId, cancellationTokenSource);
        }
    }

    private static Task DidOpenAsync(JsonRpc client, string uri, string text)
        => client.NotifyWithParameterObjectAsync("textDocument/didOpen", new
        {
            textDocument = new { uri, languageId = "lilysharp", version = 1, text },
        });

    [Fact]
    public async Task BlockedRender_DoesNotStallDispatch_AndQueuedPreviewsCollapse()
    {
        var (client, server) = Connect();
        var uri = "file:///async-live.lys";
        await DidOpenAsync(client, uri, Doc);

        var slot = server.SvgSlotFor(new Uri(uri), null);
        Task<SvgResponse> first, second;
        var hold = new GateHold(slot.Gate);
        try
        {
            first = client.InvokeWithParameterObjectAsync<SvgResponse>(
                "lilysharp/svg", new { textDocument = new { uri } });

            // With the render gate held, the OLD synchronous dispatch was stuck inside
            // the svg handler here, and this next request waited forever — this await
            // is the line the sync-poison turns into a timeout.
            var symbols = await client.InvokeWithParameterObjectAsync<DocumentSymbol[]?>(
                "textDocument/documentSymbol", new { textDocument = new { uri } })
                .WaitAsync(Net);
            Assert.NotNull(symbols);
            Assert.False(first.IsCompleted);

            // A newer preview request arrives while the first still waits for the gate.
            second = client.InvokeWithParameterObjectAsync<SvgResponse>(
                "lilysharp/svg", new { textDocument = new { uri } });
            // Fence: lilysharp/version stays ON the dispatch loop, so its answer means
            // the second svg request has taken its (newer) ticket.
            await client.InvokeAsync<string>("lilysharp/version").WaitAsync(Net);
        }
        finally
        {
            hold.Dispose();
        }

        // The first request collapses without rendering; only the newest one pays.
        var superseded = await first.WaitAsync(Net);
        var rendered = await second.WaitAsync(Net);
        Assert.True(superseded.Superseded);
        Assert.Null(superseded.Svg);
        Assert.False(rendered.Superseded);
        Assert.NotNull(rendered.Svg);
        Assert.Contains("<svg", rendered.Svg);
    }

    [Fact]
    public async Task CancelRequest_AbandonsAQueuedRender()
    {
        var inboundCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var (client, server) = Connect(s =>
            s.Rpc.CancellationStrategy = new InboundCancellationObserver(
                s.Rpc.CancellationStrategy!, inboundCancelled));
        var uri = "file:///async-cancel.lys";
        await DidOpenAsync(client, uri, Doc);

        var slot = server.SvgSlotFor(new Uri(uri), null);
        Task<SvgResponse> pending;
        using var cts = new CancellationTokenSource();
        var hold = new GateHold(slot.Gate);
        try
        {
            pending = client.InvokeWithParameterObjectAsync<SvgResponse>(
                "lilysharp/svg", new { textDocument = new { uri } }, cts.Token);
            // The request is dispatched (queued at the gate) once this fence answers.
            await client.InvokeAsync<string>("lilysharp/version").WaitAsync(Net);
            cts.Cancel();
            // Release the gate only once the SERVER-SIDE token is cancelled (see
            // InboundCancellationObserver for why no protocol fence can do this).
            await inboundCancelled.Task.WaitAsync(Net);
        }
        finally
        {
            hold.Dispose();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Net));
        Assert.Null(slot.Session); // the render never ran
    }

    [Fact]
    public async Task Notifications_StayOrderedAheadOfRequests()
    {
        var (client, _) = Connect();
        var uri = "file:///async-order.lys";
        await DidOpenAsync(client, uri, Doc);

        // Full-sync replace to the no-notes-in-common text, then immediately ask for
        // the resolved facts: didChange is dispatched before the request, so the
        // answer must be about the REPLACED text.
        await client.NotifyWithParameterObjectAsync("textDocument/didChange", new
        {
            textDocument = new { uri, version = 2 },
            contentChanges = new[] { new { text = Doc2 } },
        });
        var facts = await client.InvokeWithParameterObjectAsync<FactsForRangeResponse>(
            "lilysharp/factsForRange",
            new { textDocument = new { uri }, start = 0, end = Doc2.Length })
            .WaitAsync(Net);

        Assert.Null(facts.Error);
        Assert.Contains(facts.Pitches, p => p.Written == "d'");
        Assert.DoesNotContain(facts.Pitches, p => p.Written == "c'");
    }
}
