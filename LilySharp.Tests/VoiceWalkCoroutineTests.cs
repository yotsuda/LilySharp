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
using LilySharp.Core.Svg.Model;
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
        section A { m { voice { c''4 d' e' f' | g'2 g | } { c'4 d e f | g2 g | } } }
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

    /// <summary>A <c>key</c> in the second voice of a span, mid-bar; voice 1 reads it from there
    /// on. Its <c>f'</c> in the next bar is a natural in D major (LilyPond 2.26.0 twin: Lab
    /// sessions/p970/probe6/p4-key-in-voice2); its <c>f'</c> at the span's first beat, before the
    /// change, is C major's and carries nothing.</summary>
    private const string KeyInVoice2Book = """
        octave absolute
        time 4/4
        key c major
        part m { clef treble }
        section A { m { voice { f''4 g'' a'' b'' | f''4 g'' a'' b'' | } { d'4 key d major e' fis' g' | d'4 e' fis' g' | } } }
        form { ~A }
        score { staff m }
        """;

    /// <summary>Stage ⑥, the key only: the key is the staff's, so a key change in one branch of
    /// a span governs the other branches' later notes. The accidental memory stays per voice
    /// (owner, 2026-10-02).</summary>
    /// <remarks>LILYPOND-REF: lily/key-engraver.cc:141-162 Key_engraver::process_music — the
    /// engraver sits in Staff, so its keyAlterations are every voice's.</remarks>
    [Fact]
    public void KeyChangeInVoice2_GovernsVoice1FromThatMoment()
    {
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(KeyInVoice2Book));

        Assert.Equal(2, score.Voices.Length);
        var voice1 = score.Voices[0].Measures;
        var bar1F = Assert.IsType<NoteItem>(voice1[0].Items.First(i => i is NoteItem));
        var bar2F = Assert.IsType<NoteItem>(voice1[1].Items.First(i => i is NoteItem));
        Assert.Null(bar1F.Accidental);
        Assert.Equal("natural", bar2F.Accidental);
        // Voice 2's own fis after its key change is in the key: no sharp drawn.
        var voice2 = score.Voices[1].Measures;
        var fis = voice2[0].Items.OfType<NoteItem>().ElementAt(2);
        Assert.Null(fis.Accidental);
    }

    /// <summary>The <c>key</c> at the very moment of voice 1's second <c>f'</c>: LilyPond hears the
    /// key event before it judges the moment's heads, so that <c>f'</c> is a natural in D major too
    /// (LilyPond 2.26.0 twin: Lab sessions/p971/probe5/p5-key-same-moment). The span's driver
    /// resumes the branch parked on the key change first.</summary>
    private const string KeyAtTheSameMomentBook = """
        octave absolute
        time 4/4
        key c major
        part m { clef treble }
        section A { m { voice { f''4 f'' g'' a'' | f''4 g'' a'' b'' | } { d'4 key d major e' fis' g' | d'4 e' fis' g' | } } }
        form { ~A }
        score { staff m }
        """;

    [Fact]
    public void KeyChangeInVoice2_AtTheSameMoment_GovernsVoice1sHeadThere()
    {
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(KeyAtTheSameMomentBook));

        var bar1 = score.Voices[0].Measures[0].Items.OfType<NoteItem>().ToList();
        Assert.Null(bar1[0].Accidental);
        Assert.Equal("natural", bar1[1].Accidental);
        Assert.Equal("natural", Assert.IsType<NoteItem>(score.Voices[0].Measures[1].Items.First(i => i is NoteItem)).Accidental);
    }

    /// <summary>A key statement empties the accidental memory, in one voice as in LilyPond
    /// (lily/accidental-engraver.cc:108-130 Accidental_engraver::update_local_key_signature):
    /// <c>f'4 key d major f'</c> prints the natural on the second <c>f'</c>, which the bar's memory
    /// of the first (a natural, in C) used to suppress (LilyPond 2.26.0 twin: Lab
    /// sessions/p971/probe5/p6-key-resets-memory).</summary>
    [Fact]
    public void KeyChange_EmptiesTheAccidentalMemory()
    {
        const string book = """
            octave absolute
            time 4/4
            key c major
            part m { clef treble }
            section A { m { f''4 key d major f'' g'' a'' | f''4 g'' a'' b'' | } }
            form { ~A }
            score { staff m }
            """;
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(book));

        var bar1 = score.Voices[0].Measures[0].Items.OfType<NoteItem>().ToList();
        Assert.Null(bar1[0].Accidental);
        Assert.Equal("natural", bar1[1].Accidental);
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
