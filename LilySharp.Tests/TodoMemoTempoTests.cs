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

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// A memo over a bar that opens with a tempo (owner's report 2026-10-27, tempo-todo.lys: "at first
/// they do not overlap; after Ctrl+S the memo's y drops onto the tempo").
/// </summary>
[Trait("Category", "Unit")]
public class TodoMemoTempoTests(ITestOutputHelper output)
{
    private const string Text = "title \"Untitled\"\n\ntempo 100\ntime 4/4\nkey c major\n\npart melody {\n  clef treble\n  section A { c'1@todo(\"here\")| }\n}\n\nform { A }\n\nscore {\n  staff melody\n}\n";

    /// <summary>The memo's text element and the tempo's "= 100".</summary>
    private static (double MemoY, string Svg) Render()
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Text), SvgRenderOptions.Preview());
        var memo = Regex.Match(svg, "<text[^>]*\\by=\"([-0-9.]+)\"[^>]*data-todo=\"[^\"]*\"[^>]*>here<|<text[^>]*data-todo=\"[^\"]*\"[^>]*\\by=\"([-0-9.]+)\"[^>]*>here<");
        Assert.True(memo.Success, "no memo drawn");
        string y = memo.Groups[1].Success ? memo.Groups[1].Value : memo.Groups[2].Value;
        return (double.Parse(y, CultureInfo.InvariantCulture), svg);
    }

    private static double MemoYOf(string svg)
    {
        var memo = Regex.Match(svg, "<text[^>]*\\by=\"([-0-9.]+)\"[^>]*data-todo=\"[^\"]*\"[^>]*>here<|<text[^>]*data-todo=\"[^\"]*\"[^>]*\\by=\"([-0-9.]+)\"[^>]*>here<");
        Assert.True(memo.Success, "no memo drawn");
        return double.Parse(memo.Groups[1].Success ? memo.Groups[1].Value : memo.Groups[2].Value, CultureInfo.InvariantCulture);
    }

    private static string Lsp(LilySharp.Lsp.LilySharpLanguageServer server, System.Uri uri, int? shown, out int? version)
    {
        var r = server.GetSvg(new LilySharp.Lsp.SvgParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri }, PageDiff = true, ShownVersion = shown,
        });
        version = r.Pages?.Version;
        return r.Svg ?? (r.Pages is { } p ? p.Head + string.Concat(p.Items.Select(i => i.Markup ?? "")) + p.Tail : "");
    }

    [Fact]
    public void TheMemoStandsWhereItStood_AsThePreviewAsks_UntitledThenSaved()
    {
        var server = new LilySharp.Lsp.LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var untitled = new System.Uri("untitled:Untitled-1");
        var file = new System.Uri("file:///C:/tmp/tempo-todo.lys");
        void Open(System.Uri u) => server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem { Uri = u, Text = Text, Version = 1, LanguageId = "lilysharp" },
        });
        Open(untitled);
        double before = MemoYOf(Lsp(server, untitled, null, out var v1));
        double again = MemoYOf(Lsp(server, untitled, null, out _));
        Open(file);
        double saved = MemoYOf(Lsp(server, file, null, out _));
        output.WriteLine($"memo y: untitled {before} again {again} saved {saved} (v1 {v1})");
        Assert.Equal(before, again);
        Assert.Equal(before, saved);
    }

    [Fact]
    public void TheMemoStandsWhereItStood_RenderAfterRender()
    {
        var first = Render();
        var second = Render();
        var third = Render();
        output.WriteLine($"memo y: {first.MemoY} {second.MemoY} {third.MemoY}");
        Assert.Equal(first.MemoY, second.MemoY);
        Assert.Equal(first.MemoY, third.MemoY);
    }
}
