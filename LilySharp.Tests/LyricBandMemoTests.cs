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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The per-system memo of the below-system lyric reservation band
/// (<c>SystemLayoutCache.GetOrComputeLyricBand</c>): a keystroke must SERVE the band for
/// unchanged systems (the hit is asserted, not assumed — a memo that silently recomputes
/// forever is a perf regression no correctness net can see), a lyric edit must RECOMPUTE
/// it (the content key folds the syllables), and both must render byte-identical to a
/// full recompile.
/// </summary>
[Trait("Category", "Visual")]
public class LyricBandMemoTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    /// <summary>A sung book that breaks into several systems, so a keystroke has
    /// unchanged systems whose bands can be served.</summary>
    private const string Sung = """
        time 4/4
        part v { }
        lyrics w { section Main { ma me mi mo | ma me mi mo | ma me mi mo | ma me mi mo | ma me mi mo | ma me mi mo | ma me mi mo | ma me mi mo | ma me mi mo | ma me mi mo | ma me mi mo | ma me mi mo | } }
        section Main {
          v { c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | }
        }
        form main { ~Main }
        score main { staff ~v  lyrics w }
        """;

    private static string Full(string text) =>
        SvgGenerator.Generate(SyntaxTree.Parse(text), Opt).Replace("\r\n", "\n");

    private static string Norm(string svg) => svg.Replace("\r\n", "\n");

    private static string ReplaceFirst(string text, string find, string rep)
    {
        int at = text.IndexOf(find, System.StringComparison.Ordinal);
        Assert.True(at >= 0, $"snippet not found: {find}");
        return text.Remove(at, find.Length).Insert(at, rep);
    }

    [Fact]
    public void NoteKeystroke_ServesUnchangedSystemsBands_AndMatchesFull()
    {
        var session = new IncrementalCompiler(SyntaxTree.Parse(Sung), Opt);
        session.RenderIncremental(SyntaxTree.Parse(Sung));
        var cache = session.SystemCache;
        Assert.NotNull(cache);
        var (hits0, misses0) = cache!.LyricBandStats;
        Assert.True(misses0 >= 2, $"expected a multi-system book (first render missed {misses0})");
        Assert.Equal(0, hits0);

        // Re-pitch one note in ONE measure: the content key moves for that measure's
        // neighbourhood only, so every other system's band must be a HIT.
        string edited = ReplaceFirst(Sung, "c4 d e f | c4 d e f | c4 d e f | }", "c4 d e f | c4 d e f | c4 d a f | }");
        Assert.Equal(Full(edited), Norm(session.RenderIncremental(SyntaxTree.Parse(edited))));

        var (hits1, misses1) = cache.LyricBandStats;
        Assert.True(hits1 - hits0 >= misses0 - 2,
            $"keystroke served {hits1 - hits0} bands of {misses0} systems (misses {misses1 - misses0})");
    }

    [Fact]
    public void LyricEdit_RecomputesTheBand_AndMatchesFull()
    {
        var session = new IncrementalCompiler(SyntaxTree.Parse(Sung), Opt);
        session.RenderIncremental(SyntaxTree.Parse(Sung));
        var cache = session.SystemCache;
        Assert.NotNull(cache);
        var (_, misses0) = cache!.LyricBandStats;

        // Change one syllable's TEXT (wider ink): the edited system's band must
        // recompute — the syllables reach the key through the side-table buckets —
        // and the output must equal a full recompile (a stale band would misplace
        // the floor under the edited line).
        string edited = ReplaceFirst(Sung, "ma me mi mo | }", "ma me mi mooooo | }");
        Assert.Equal(Full(edited), Norm(session.RenderIncremental(SyntaxTree.Parse(edited))));

        var (_, misses1) = cache.LyricBandStats;
        Assert.True(misses1 > misses0,
            $"the lyric edit recomputed no band (misses {misses0} -> {misses1})");
    }
}
