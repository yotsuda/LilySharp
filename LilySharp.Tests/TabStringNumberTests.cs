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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Bass tab support: the <c>\N</c> string-number annotation forces the fret's
/// string, bass tunings sound 8vb, and the note carries a clef-independent MIDI
/// pitch for fret calculation. LILYPOND-REF: lily/tab-note-heads-engraver.cc.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TabStringNumberTests
{
    private static NoteItem FirstNote(string body)
    {
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n" + body + "\n  }\n}\n" +
                  "structure { Main }\nscore \"x\" { staff { bl } }\n";
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src));
        return score.Voice.Measures[0].Items.OfType<NoteItem>().First();
    }

    [Fact]
    public void StringNumberAnnotation_IsParsedOntoTheNote()
    {
        // The \4 must reach the note (it used to be silently dropped).
        var note = FirstNote("a4\\4 b c d |");
        Assert.Equal(4, note.StringNumber);
    }

    [Fact]
    public void NoteCarriesClefIndependentMidi()
    {
        // The first 'a' resolves to A3 (MIDI 57); the note carries that absolute
        // pitch (NOT the clef-relative StaffPosition the tab must avoid for pitch).
        var note = FirstNote("a4 b c d |");
        Assert.Equal(57, note.Midi);
        Assert.Null(note.StringNumber);
    }

    [Theory]
    [InlineData(TuningType.Bass, -12)]
    [InlineData(TuningType.Bass5, -12)]
    [InlineData(TuningType.Bass6, -12)]
    [InlineData(TuningType.Guitar, 0)]
    [InlineData(TuningType.Ukulele, 0)]
    public void BassTuningsSound8vb(TuningType tuning, int expectedShift)
    {
        Assert.Equal(expectedShift, Tunings.OctaveShift(tuning));
    }

    [Fact]
    public void Bass6Tuning_IsSixStringsLowBHighC()
    {
        Assert.Equal(6, Tunings.GetStringCount(TuningType.Bass6));
        // B0 E1 A1 D2 G2 C3
        Assert.Equal(new[] { 23, 28, 33, 38, 43, 48 }, Tunings.GetTuning(TuningType.Bass6));
    }

    [Fact]
    public void CalculateFret_HonoursPreferredString()
    {
        // A1 (33) forced onto string 4 (E1=28) of a 4-string bass = fret 5,
        // even though the open A string (string 3) would be fret 0.
        var (str, fret) = Tunings.CalculateFret(33, Tunings.Bass, preferredString: 4);
        Assert.Equal(4, str);
        Assert.Equal(5, fret);
    }
}
