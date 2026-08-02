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
/// A <c>~Name</c> reference in a form hides the section's rehearsal LABEL. It does not
/// hide the section.
/// </summary>
/// <remarks>
/// The MIDI exporter used to match only <c>SectionReferenceSyntax</c> when walking a form,
/// so a silent reference fell through the switch and the section never played:
/// <c>form main { ~Main }</c> engraved correctly and exported ZERO notes. The engraver had
/// already been bitten by the same omission in its repeat-block walk, and says so in
/// MeasureCollector.Form.cs — "without this the section's measures were dropped entirely,
/// not just its label". The invariant these tests hold is the strong one: dropping the '~'
/// must change nothing audible at all.
/// </remarks>
public sealed class MidiSilentSectionTests
{
    private static int[] Pitches(string source) =>
        new MidiExporter().Export(SyntaxTree.Parse(source))
            .Tracks[1].Notes.OrderBy(n => n.StartTick).Select(n => n.Pitch).ToArray();

    private const string Book = "part m { section A { c4 d4 } }\nform main { ";

    [Fact]
    public void SilentSectionReference_Plays()
    {
        Assert.Equal(new[] { 60, 62 }, Pitches(Book + "~A }"));
    }

    [Fact]
    public void SilentSectionReference_SoundsExactlyLikeTheVisibleOne()
    {
        // The '~' is a LABEL switch. Anything it changes in the audio is a defect,
        // which is why this asserts against the plain form rather than against a
        // literal — a future change that moves both stays honest.
        Assert.Equal(Pitches(Book + "A }"), Pitches(Book + "~A }"));
    }

    [Fact]
    public void SilentSectionReference_InsideARepeat_PlaysEveryPass()
    {
        Assert.Equal(new[] { 60, 62, 60, 62 }, Pitches(Book + "|: ~A :| }"));
        Assert.Equal(Pitches(Book + "|: A :| }"), Pitches(Book + "|: ~A :| }"));
    }

    [Fact]
    public void SilentSectionReference_MixedWithVisibleOnes_KeepsFormOrder()
    {
        // Two sections, one hidden, so a fix that played the silent one in the wrong
        // place (or twice) would not pass by accident.
        const string two = "part m { section A { c4 } section B { e4 } }\nform main { ";
        Assert.Equal(new[] { 60, 64, 60 }, Pitches(two + "~A B A }"));
    }
}
