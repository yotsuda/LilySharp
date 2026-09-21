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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A SPANNER reaches the content keys of the measures it merely crosses — asserted on the
/// KEYS, which is where the claim lives, and not on the picture, where it washes out.
/// </summary>
/// <remarks>
/// ⚠️ WHY THE KEYS AND NOT A RENDER. <c>MeasureContentKey.BucketSideTables</c> builds its
/// buckets lazily, and <c>BucketSpan</c> is the only thing that ever CREATES one — a measure a
/// bracket merely crosses has no mark, no lyric and no dynamic of its own. Session 450 poisoned
/// exactly that creation, predicted RED, and got GREEN; session 453 found out why, and it took
/// an instrument and five fixtures:
/// <list type="number">
/// <item>The creation is REACHED — 42 span folds in a suite run, 10 of them creating.</item>
/// <item><c>IncrementalCompilerTests.DeletingAPedalRelease_RedrawsTheSystemsTheBracketSpanned</c>
/// cannot see it: its book sings <c>la la la la</c> under every bar, so
/// <c>BucketSingle(score.Lyrics, …)</c> has created every bucket before the span pass runs.</item>
/// <item>On a bare book the poison DOES change the cache's mind — measured on a 24-bar book
/// whose bracket runs bar 3 to bar 22: deleting the release leaves 1 hit / 12 misses clean and
/// 5 hits / 8 misses poisoned, i.e. four systems served stale — and the SVG is byte-identical
/// anyway. A middle measure's fold is <c>(role, content-without-absolute-indices)</c>, a
/// constant, and nothing a system caches depends on a bracket that merely crosses it: the ink
/// is re-solved every pass from the live score (<c>PedalEngraver.SolveAndSeed</c>). The
/// measures whose brackets DO reach a cached value are the ones holding its ENDS — and those
/// carry a mark, so their buckets already exist.</item>
/// </list>
/// ⇒ The fold over-invalidates, soundly, and no rendered book can observe it. So the observer
/// is put where the quantity is: on the key itself.
/// </remarks>
[Trait("Category", "Unit")]
public class MeasureContentKeySpanTests
{
    private const int Bars = 24;
    private const int SustainBar = 3;     // 1-based, as the book is written
    private const int ReleaseBar = 22;

    /// <summary>The same 24 bars either with a sustain bracket over bars 3-22, or without.</summary>
    private static string Book(bool pedalled)
    {
        var music = new System.Text.StringBuilder();
        for (int bar = 1; bar <= Bars; bar++)
            music.Append(pedalled && bar == SustainBar ? "c4 d@sustain e f | "
                : pedalled && bar == ReleaseBar ? "c4 d@!sustain e f | "
                : "c4 d e f | ");
        return $$"""
            time 4/4
            part melody { clef treble }
            section Main {
              melody { {{music}} }
            }
            form main { Main }
            score main "x" { staff melody }
            """.Replace("\r\n", "\n");
    }

    private static MultiStaffScore ScoreOf(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Fact]
    public void ABracketCrossingBareMeasures_MovesTheirContentKeys()
    {
        var pedalled = MeasureContentKey.Compute(ScoreOf(Book(pedalled: true)));
        var plain = MeasureContentKey.Compute(ScoreOf(Book(pedalled: false)));
        Assert.Equal(Bars, pedalled.Length);
        Assert.Equal(Bars, plain.Length);

        // A measure the bracket merely CROSSES. It owns nothing — no mark, no lyric, no
        // dynamic — so the span pass is the only thing that can put anything in its bucket,
        // and the only thing that can create the bucket at all.
        const int crossed = 10;   // 0-based: bar 11, between the engage and the release
        Assert.NotEqual(plain[crossed], pedalled[crossed]);

        // The controls: outside the span the two books are the same book. Without these the
        // assertion above would also pass if the bracket moved EVERY key.
        foreach (int outside in new[] { 0, 1, Bars - 2, Bars - 1 })
            Assert.Equal(plain[outside], pedalled[outside]);
    }
}
