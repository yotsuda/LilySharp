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

using System.Threading.Tasks;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The voice walk as a coroutine (docs/moment-walk-design.md §3.1, stage ③): below the two
/// entry points the walk is <c>async ValueTask</c> and asks the staff's clock before every
/// moment advance. With one voice on the clock it never suspends — counted, not assumed —
/// and the entry points refuse a suspension out loud rather than block on it.
/// </summary>
[Trait("Category", "Unit")]
public class VoiceWalkCoroutineTests
{
    private const string Book = """
        part m { clef treble }
        section A { m { c'4 d'8 e' f'2 | <g' b'>4 r4 a'2 | } }
        form { ~A }
        score { staff m }
        """;

    /// <summary>Every item the walk adds goes through the clock once (the three funnels are
    /// the only moment advances), and with one voice none of those asks suspends.</summary>
    [Fact]
    public void OneVoiceOnTheClock_ReachesOncePerFunnelCall_AndNeverSuspends()
    {
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(Book));

        int items = score.Voices.Sum(v => v.Measures.Sum(m => m.Items.Length));
        Assert.True(items >= 7, $"the book has {items} items");
        // At least one ask per item that reached a measure (directives and spacers the
        // builder synthesizes are items too, so the count may run higher, never lower).
        Assert.True(collector.Clock.Reaches >= items, $"reaches {collector.Clock.Reaches} < items {items}");
        Assert.Equal(0, collector.Clock.Suspensions);
    }

    private const string TwoVoiceBook = """
        part m { clef treble }
        section A { m { voice { c'4 d' e' f' | g'2 g' | } { c4 d e f | g2 g | } } }
        form { ~A }
        score { staff m }
        """;

    /// <summary>Two voices of a span on one clock (stage ⑤a): the branches interleave by
    /// moment, so at least one of them parks — the clock's work is visible in its counters —
    /// and the span's driver hands the collect back complete.</summary>
    [Fact]
    public void TwoVoicesOnOneClock_InterleaveAndPark()
    {
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(TwoVoiceBook));

        Assert.Equal(2, score.Voices.Length);
        Assert.Equal(1, collector.Clock.Spans);
        Assert.True(collector.Clock.Suspensions > 0, "no branch ever parked: the voices did not share a clock");
        Assert.True(collector.Clock.Reaches > collector.Clock.Suspensions);
        Assert.Equal(score.Voices[0].Measures.Length, score.Voices[1].Measures.Length);
    }

    /// <summary>The synchronous entry points run the coroutine to completion; a walk that
    /// suspended on its clock with no scheduler is an error, said in words, not a blocked thread.</summary>
    [Fact]
    public void Run_RefusesAWalkThatSuspended()
    {
        var pending = new TaskCompletionSource<int>();
        var ex = Assert.Throws<InvalidOperationException>(() => MeasureCollector.RunForTests(new ValueTask<int>(pending.Task)));
        Assert.Contains("suspended on its clock", ex.Message);

        pending.SetResult(7);
        Assert.Equal(7, MeasureCollector.RunForTests(new ValueTask<int>(pending.Task)));
    }
}
