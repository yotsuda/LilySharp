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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chord of drums in MusicXML. Until 2026-10-11 the exporter took <c>&lt;bd hh&gt;4</c> for
/// an EMPTY chord: it wrote no note and no time, so every bar holding one came out short
/// (found with the drum-chart work, the owner asking for it fixed).
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlDrumChordTests
{
    private static MusicXmlMeasure Bar(string music) => new MusicXmlExporter().Export(SyntaxTree.Parse($$"""
        part kit { clef percussion }
        section A { kit { {{music}} } }
        form { ~A }
        score { staff kit }
        """)).Parts.Single().Measures[0];

    [Fact]
    public void AChordOfDrums_IsItsDrums_AndTakesItsTime()
    {
        var notes = Bar("<bd hh>4 <hh sn@parenthesize>4 r2 |").Notes.Where(n => !n.IsBackup).ToList();
        Assert.Equal(5, notes.Count);   // two, two, the rest
        Assert.All(notes.Take(4), n => Assert.True(n.IsUnpitched));
        Assert.Equal(new[] { false, true, false, true }, notes.Take(4).Select(n => n.IsChord));
        Assert.Equal(new[] { false, false, false, true }, notes.Take(4).Select(n => n.NoteheadParentheses));
        // The bar is whole: two quarters and the half rest.
        Assert.Equal(4 * notes[0].Duration, notes[0].Duration + notes[2].Duration + notes[4].Duration);
        // The hi-hat keeps its cross.
        Assert.Equal("x", notes[1].Notehead);
    }

    [Fact]
    public void ABareDurationRepeatingADrumChord_RepeatsItsDrums()
    {
        var notes = Bar("<bd hh>8 8 8 8 r2 |").Notes.Where(n => !n.IsBackup && !n.IsRest).ToList();
        Assert.Equal(8, notes.Count);
        Assert.All(notes, n => Assert.True(n.IsUnpitched));
        Assert.Equal(4, notes.Count(n => !n.IsChord));
    }
}
