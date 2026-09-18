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
using LilySharp.Core.Rendering.Svg;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The page-wise <c>lilysharp/svg</c> answer (R13⒝, session 404): a client that says which
/// picture its viewer holds (<see cref="SvgParams.ShownVersion"/>) is sent only the pages
/// that changed since it, and reconstructs the rest from what it holds — a Same page as is,
/// a Shifted page with its offsets mapped through the answer's window. The net's
/// certificate is the reconstruction: applied to the previous answer's pages, it must
/// equal the full render of the edited text, byte for byte.
/// </summary>
/// <remarks>
/// Poisons: answer a delta when the client's version is not the session's previous one
/// ⇒ the stale-version net; forget to reset the slot's version on a one-string answer ⇒ the
/// no-PageDiff net's second half; number the versions per slot instead of per server ⇒ the
/// two-scores net.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SvgPageDiffTests
{
    private static LilySharpLanguageServer Opened(Uri uri, string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        return server;
    }

    private static void Edit(LilySharpLanguageServer server, Uri uri, string text, int version)
        => server.DidChange(new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = version },
            ContentChanges = [new TextDocumentContentChangeEvent { Text = text }],
        });

    private static SvgParams Ask(Uri uri, int? shown, bool pageDiff = true, string? renderName = null) => new()
    {
        TextDocument = new TextDocumentIdentifier { Uri = uri },
        RenderName = renderName,
        PageDiff = pageDiff,
        ShownVersion = shown,
    };

    /// <summary>What the preview's page does with an answer: the pages it now holds.</summary>
    private static string[] Reconstruct(string[] held, SvgPages answer)
    {
        Assert.Equal(held.Length, answer.Items.Length);
        var window = answer.Window is { } w ? new SvgEditWindow(w.Prefix, w.SuffixStart, w.Delta) : default;
        return answer.Items.Select((item, i) => item.Change switch
        {
            "same" => held[i],
            "shifted" => SvgPageSetTests.Restamp(held[i], window),
            _ => item.Markup!,
        }).ToArray();
    }

    private static string Join(SvgPages answer, string[] pages) => answer.Head + string.Concat(pages) + answer.Tail;

    private static string FullRender(string text)
    {
        var uri = new Uri("file:///full.lys");
        var response = Opened(uri, text).GetSvg(Ask(uri, null, pageDiff: false));
        Assert.NotNull(response.Svg);
        return response.Svg!;
    }

    [Fact]
    public void TheFirstAnswer_CarriesEveryPage_AndJoinsToTheOneString()
    {
        string src = SvgPageSetTests.MultiPageBook();
        var uri = new Uri("file:///pages.lys");
        var server = Opened(uri, src);

        var first = server.GetSvg(Ask(uri, null));
        Assert.Null(first.Error);
        Assert.Null(first.Svg);
        var pages = first.Pages!;
        Assert.Null(pages.BaseVersion);
        Assert.True(pages.Items.Length >= 3);
        Assert.All(pages.Items, item => { Assert.Equal("changed", item.Change); Assert.NotNull(item.Markup); });
        Assert.Equal(FullRender(src), Join(pages, pages.Items.Select(i => i.Markup!).ToArray()));
    }

    [Fact]
    public void TheSameText_IsADeltaOfSamePages_WithoutMarkup()
    {
        string src = SvgPageSetTests.MultiPageBook();
        var uri = new Uri("file:///pages.lys");
        var server = Opened(uri, src);
        var first = server.GetSvg(Ask(uri, null)).Pages!;

        var again = server.GetSvg(Ask(uri, first.Version)).Pages!;
        Assert.Equal(first.Version, again.BaseVersion);
        Assert.NotEqual(first.Version, again.Version);
        Assert.All(again.Items, item => { Assert.Equal("same", item.Change); Assert.Null(item.Markup); });
        Assert.Null(again.Window);
    }

    [Fact]
    public void AnEdit_ShipsTheChangedPageOnly_AndTheViewerReconstructsTheRest()
    {
        string src = SvgPageSetTests.MultiPageBook();
        var uri = new Uri("file:///pages.lys");
        var server = Opened(uri, src);
        var first = server.GetSvg(Ask(uri, null)).Pages!;
        var held = first.Items.Select(i => i.Markup!).ToArray();

        // A note in the fourth system: one inner page changes, the rest keep or shift.
        string edited = src;
        int at = -1;
        for (int k = 0; k < 4; k++) at = edited.IndexOf("e8 f g a b( c' d' e') |", at + 1, StringComparison.Ordinal);
        edited = edited[..at] + "e8 f gis a b( c' d' e') |" + edited[(at + "e8 f g a b( c' d' e') |".Length)..];
        Edit(server, uri, edited, 2);

        var delta = server.GetSvg(Ask(uri, first.Version)).Pages!;
        Assert.Equal(first.Version, delta.BaseVersion);
        Assert.Equal(1, delta.Items.Count(i => i.Change == "changed"));
        Assert.All(delta.Items, item => Assert.Equal(item.Change == "changed", item.Markup != null));
        Assert.Contains(delta.Items, i => i.Change == "same");
        Assert.Contains(delta.Items, i => i.Change == "shifted");
        Assert.NotNull(delta.Window);

        held = Reconstruct(held, delta);
        Assert.Equal(FullRender(edited), Join(delta, held));

        // And the next keystroke builds on THAT picture: undo the edit.
        Edit(server, uri, src, 3);
        var back = server.GetSvg(Ask(uri, delta.Version)).Pages!;
        Assert.Equal(delta.Version, back.BaseVersion);
        held = Reconstruct(held, back);
        Assert.Equal(FullRender(src), Join(back, held));
    }

    [Fact]
    public void AStaleShownVersion_GetsEveryPage()
    {
        string src = SvgPageSetTests.MultiPageBook();
        var uri = new Uri("file:///pages.lys");
        var server = Opened(uri, src);
        var first = server.GetSvg(Ask(uri, null)).Pages!;
        // The client missed an answer (a newer request superseded it on its side)…
        server.GetSvg(Ask(uri, first.Version));
        // …so what it holds is not the session's previous picture: every page, again.
        var full = server.GetSvg(Ask(uri, first.Version)).Pages!;
        Assert.Null(full.BaseVersion);
        Assert.All(full.Items, item => { Assert.Equal("changed", item.Change); Assert.NotNull(item.Markup); });
        Assert.Equal(FullRender(src), Join(full, full.Items.Select(i => i.Markup!).ToArray()));
    }

    [Fact]
    public void AClientWithoutPageDiff_GetsTheOneString_AndThePagesAfterItStartOver()
    {
        string src = SvgPageSetTests.MultiPageBook();
        var uri = new Uri("file:///pages.lys");
        var server = Opened(uri, src);
        var first = server.GetSvg(Ask(uri, null)).Pages!;

        var plain = server.GetSvg(Ask(uri, first.Version, pageDiff: false));
        Assert.NotNull(plain.Svg);
        Assert.Null(plain.Pages);
        Assert.Equal(FullRender(src), plain.Svg);

        // The one-string render is not a picture the next delta can be against.
        var next = server.GetSvg(Ask(uri, first.Version)).Pages!;
        Assert.Null(next.BaseVersion);
    }

    [Fact]
    public void AnotherScoreOfTheDocument_NeverTakesTheVersionForItsOwn()
    {
        string src = SvgPageSetTests.MultiPageBook()
            .Replace("score main \"x\" { staff melody }", "score main \"x\" { staff melody }\nscore main \"y\" { staff melody }");
        var uri = new Uri("file:///pages.lys");
        var server = Opened(uri, src);
        var x = server.GetSvg(Ask(uri, null, renderName: "x")).Pages!;
        // The picker switches to the other score: its session has never rendered, and the
        // version the viewer holds belongs to the first score's session.
        var y = server.GetSvg(Ask(uri, x.Version, renderName: "y")).Pages!;
        Assert.Null(y.BaseVersion);
        Assert.NotEqual(x.Version, y.Version);
        Assert.All(y.Items, item => Assert.NotNull(item.Markup));
    }
}
