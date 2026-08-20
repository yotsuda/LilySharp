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
        // The verse-skyline memo is SHARED between the two annotation passes: the first
        // render's preliminary pass computes every system (misses) and the final pass
        // serves them (hits) — the pass-sharing half of the win, asserted.
        var (vHits0, vMisses0) = cache.VerseSkylines.Stats;
        Assert.True(vMisses0 >= 2 && vHits0 >= vMisses0,
            $"first render: verse memo hits {vHits0} / misses {vMisses0} — the final pass should serve the preliminary's entries");

        // Re-pitch one note in ONE measure: the content key moves for that measure's
        // neighbourhood only, so every other system's band must be a HIT.
        string edited = ReplaceFirst(Sung, "c4 d e f | c4 d e f | c4 d e f | }", "c4 d e f | c4 d e f | c4 d a f | }");
        Assert.Equal(Full(edited), Norm(session.RenderIncremental(SyntaxTree.Parse(edited))));

        var (hits1, misses1) = cache.LyricBandStats;
        Assert.True(hits1 - hits0 >= misses0 - 2,
            $"keystroke served {hits1 - hits0} bands of {misses0} systems (misses {misses1 - misses0})");
        // ...and the verse memo re-fed only the edited system (a miss in the preliminary
        // pass; the final pass hits the freshly stored entry), serving everything else.
        var (vHits1, vMisses1) = cache.VerseSkylines.Stats;
        Assert.True(vMisses1 - vMisses0 <= 2,
            $"the note keystroke recomputed {vMisses1 - vMisses0} systems' verse skylines — expected at most the edited one per pass");
        Assert.True(vHits1 - vHits0 >= 2 * (vMisses0 - 2),
            $"keystroke served {vHits1 - vHits0} verse-skyline entries of ~{2 * vMisses0} lookups");
    }

    [Fact]
    public void LyricEdit_RecomputesTheBand_AndMatchesFull()
    {
        var session = new IncrementalCompiler(SyntaxTree.Parse(Sung), Opt);
        session.RenderIncremental(SyntaxTree.Parse(Sung));
        var cache = session.SystemCache;
        Assert.NotNull(cache);
        var (_, misses0) = cache!.LyricBandStats;
        var (_, vMisses0) = cache.VerseSkylines.Stats;

        // Change one syllable's TEXT (wider ink): the edited system's band must
        // recompute — the syllables reach the key through the side-table buckets —
        // and the output must equal a full recompile (a stale band would misplace
        // the floor under the edited line).
        string edited = ReplaceFirst(Sung, "ma me mi mo | }", "ma me mi mooooo | }");
        Assert.Equal(Full(edited), Norm(session.RenderIncremental(SyntaxTree.Parse(edited))));

        var (_, misses1) = cache.LyricBandStats;
        Assert.True(misses1 > misses0,
            $"the lyric edit recomputed no band (misses {misses0} -> {misses1})");
        // The syllable edit must also re-feed the edited system's verse skylines — the
        // content key folds the lyrics, so the measures memo hands out NEW instances
        // there and the reference key declines (a stale profile would space the verse
        // against the old text's ink).
        var (_, vMisses1) = cache.VerseSkylines.Stats;
        Assert.True(vMisses1 > vMisses0,
            $"the lyric edit recomputed no verse skyline (misses {vMisses0} -> {vMisses1})");
    }
}
