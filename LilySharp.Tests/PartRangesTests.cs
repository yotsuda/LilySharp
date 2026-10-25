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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Each part's lowest and highest note, and where each is written — the preview's Range panel
/// (owner's request 2026-10-25: check the parts' ranges; find a singer's top note at once).
/// </summary>
public class PartRangesTests
{
    private const string Source = """
        octave absolute
        time 4/4
        part sop { clef treble }
        part alt { clef treble }
        section A {
          sop { c4 g' e' g' | bes1 | }
          alt { a,4 c <c e>2 | f,1 | }
        }
        form { A }
        """;

    private static PartRange Part(string name)
        => PartRanges.Of(new MidiExporter().Export(SyntaxTree.Parse(Source)), Source).Single(p => p.Name == name);

    private static string WrittenAt(int pos) => Source[pos..].TrimStart().Split(' ', '|')[0];

    [Fact]
    public void NamesEachPartsLowestAndHighestNote()
    {
        var sop = Part("sop");
        Assert.Equal(("C4", "G5"), (sop.Low.Name, sop.High.Name));
        Assert.Equal((60.0, 79.0), (sop.Low.Pitch, sop.High.Pitch));
    }

    [Fact]
    public void FindsEveryPlaceTheTopNoteIsWritten_InTheOrderItSounds()
    {
        var high = Part("sop").High;
        Assert.Equal(2, high.Positions.Count);
        Assert.All(high.Positions, p => Assert.Equal("g'", WrittenAt(p)));
        Assert.True(high.Positions[0] < high.Positions[1]);
    }

    [Fact]
    public void ReadsAChordsTopMember_AndSpellsAFlatAsWritten()
    {
        var alt = Part("alt");
        Assert.Equal(("F3", "E4"), (alt.Low.Name, alt.High.Name));
        Assert.StartsWith("<c e>", Source[alt.High.Positions[0]..].TrimStart());

        const string flat = "octave absolute\ntime 4/4\npart p { clef treble }\nsection A { p { c4 bes ces' bes | } }\nform { A }";
        var p = PartRanges.Of(new MidiExporter().Export(SyntaxTree.Parse(flat)), flat).Single();
        Assert.Equal(("C4", "C♭5"), (p.Low.Name, p.High.Name));
    }
}
