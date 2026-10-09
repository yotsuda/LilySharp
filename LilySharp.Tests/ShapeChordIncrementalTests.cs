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

using System.Threading;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Stepping a <c>chord(…)</c>'s shape (Ctrl+Shift+↑) in a section the form plays SIX times
/// must redraw all six diagrams: the preview redrew the first play's and kept the other five
/// (user report 2026-09-29, <c>C:\tmp\diagram.lys</c>). The resumed render is held to the
/// fresh one byte for byte, as every resume net is (CollectEditResumeTests).
/// </summary>
public class ShapeChordIncrementalTests
{
    private const string Before = """
        layout {
        }

        part melody {
          clef treble
          instrument guitar
          section A { chord(C x35550)1@chord() | c'1 }
        }

        form { A |: A  [1. ~A ~A ] :| ~A A }

        score {
          staff melody
        }
        """;

    private static readonly SvgRenderOptions Options = new() { EmbedFont = false };

    private static LilySharp.Core.Rendering.Svg.SvgPageSet Fresh(string text)
        => new IncrementalCompiler(SyntaxTree.Parse(text), Options)
            .RenderIncrementalPages(SyntaxTree.Parse(text), CancellationToken.None);

    /// <summary>The step's own edit (a same-length shape, Δ=0), a longer shape (Δ>0, the
    /// window non-empty) and a note typed into the replayed section (an insertion: an
    /// empty window with Δ>0) — each must reach every play.</summary>
    [Theory]
    [InlineData("x35550", "x32010")]
    [InlineData("x35550", "8-10-10-9-8-8")]
    [InlineData("c'1", "c'2 d'2")]
    public void AnEditInASectionTheFormPlaysAgain_ReachesEveryPlay(string old, string @new)
    {
        string after = Before.Replace(old, @new);
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(Before), Options);
        compiler.RenderIncrementalPages(SyntaxTree.Parse(Before), CancellationToken.None);

        var resumed = compiler.RenderIncrementalPages(SyntaxTree.Parse(after), CancellationToken.None);

        Assert.Equal(Fresh(after).Pages, resumed.Pages);
    }
}
