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
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A bar left open at a section's end — music after the part's last bar line, as a bar being
/// written by hand is (owner's report 2026-11-17: a bass line typed under a chord chart, `d,4`
/// with no `|` yet) — is closed with silence: the page draws what the bar holds and the rest of
/// it empty, and the part is padded through the section and the ones after it. Until then the
/// open bar was never closed, the padding (which waits for a clean bar boundary) never ran, and
/// every later section's bars fell into it: the staff ended there and the chords of the rest of
/// the piece crowded over its last bars. A bar left open for the next section's pickup
/// (`partial`) is the one way a section starts inside a bar, and stays open for it.
/// Lab sessions/p1290/mylife.
/// </summary>
[Trait("Category", "Unit")]
public class OpenBarAtSectionEndTests
{
    /// <summary>Section B: 8 chord bars, the bass writing <paramref name="bass"/>; section C:
    /// 4 chord bars, no bass.</summary>
    private static string Book(string bass, string bRow = "D | C | F | G | D | C | F | G |") => $$"""
        time 4/4
        part bass { instrument bass }
        section B {
          chords harmony { {{bRow}} }
          bass { {{bass}} }
        }
        section C { chords harmony { A | A | B | B | } }
        form { B C }
        score { staff bass  chords harmony }
        """;

    private static Measure[] BassMeasures(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return score.EnumerateStaves().Single(t => t.Staff.PrimaryVoice.Name == "bass").Staff.PrimaryVoice.Measures.ToArray();
    }

    [Fact]
    public void AnOpenBar_ShortOfTheSection_IsClosed_AndThePartPaddedThroughTheRest()
    {
        var open = BassMeasures(Book("d4 r r r | c1 | f1 | g1 | d,4"));
        var closed = BassMeasures(Book("d4 r r r | c1 | f1 | g1 | d,4 |"));
        // Eight bars of B and four of C either way.
        Assert.Equal(12, open.Length);
        Assert.Equal(12, closed.Length);
        // Open, the bar is a whole bar: its quarter, then silence.
        Assert.Equal(new Fraction(1, 1), open[4].Items.Aggregate(Fraction.Zero, (s, it) => s + it.Duration));
    }

    [Fact]
    public void AnOpenLastBar_OfAFullSection_IsClosedWhereTheNextSectionBegins()
    {
        var bars = BassMeasures(Book("d4 r r r | c1 | f1 | g4", bRow: "D | C | F | G |"));
        Assert.Equal(8, bars.Length);
    }

    [Fact]
    public void ABarLeftOpenForTheNextSectionsPickup_StaysOpenForIt()
    {
        const string pickup = """
            time 4/4
            part bass { instrument bass }
            section B { chords harmony { D | C | F | G } bass { d1 | c1 | f1 | g2. } }
            section C { partial 4 chords harmony { A | A | B | } bass { a4 | a1 | b1 | } }
            form { B C }
            score { staff bass  chords harmony }
            """;
        Assert.Equal(6, BassMeasures(pickup).Length);
    }

    [Theory]
    [InlineData("d4 r r r | c1 | f1 | g1 | d,4", "D | C | F | G | D | C | F | G |", 12)]
    [InlineData("d4 r r r | c1 | f1 | g4", "D | C | F | G |", 8)]
    public void TheSound_PutsTheNextSectionsChordsOnItsOwnBars(string bass, string bRow, int bars)
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(Book(bass, bRow)));
        var chords = midi.Tracks.Single(t => t.Name == "harmony (chords)").Notes.Select(n => n.StartTick).Distinct().OrderBy(t => t).ToArray();
        int bar = 4 * midi.TicksPerQuarterNote;
        Assert.Equal(Enumerable.Range(0, bars).Select(i => i * bar), chords);
    }
}
