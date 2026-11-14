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
/// A file with no <c>form</c> plays its sections in declaration order, each once, every part's
/// copy together — the MIDI of the same book with <c>form { A B … }</c> written out.
/// </summary>
/// <remarks>
/// Until 2026-11-15 the MIDI played each section DECLARATION where its walk met it, so a
/// by-part book — <c>part mel { section A { … } } part bas { section A { … } }</c> — sounded
/// the second part after the first had ended (the bass's first note eight beats late; Lab
/// sessions/p1243/probe/h-plain.lys). The page, MusicXML and the twin already read the
/// declared order by name. Poison: play the declaration alone again
/// (<c>MidiExporter.ProcessNode</c>'s section arm) and the by-part rows go red; the
/// by-section row is the control — one declaration a name, the same either way.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FormlessMidiTests
{
    private static (int Pitch, int Start, int Length)[] Notes(string book)
        => new MidiExporter().Export(SyntaxTree.Parse(book)).Tracks
            .SelectMany(t => t.Notes).Where(n => n.DurationTicks > 0)
            .Select(n => (n.Pitch, n.StartTick, n.DurationTicks))
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).ToArray();

    private const string ByPart =
        "time 4/4\noctave absolute\n"
        + "part mel { section A { c''4 d'' e'' f'' | g''1 | } section B { a''1 | } }\n"
        + "part bas { clef bass section A { c1 | g1 | } section B { f1 | } }\n";

    private const string BySection =
        "time 4/4\noctave absolute\npart mel { }\npart bas { clef bass }\n"
        + "section A { mel { c''4 d'' e'' f'' | g''1 | } bas { c1 | g1 | } }\n"
        + "section B { mel { a''1 | } bas { f1 | } }\n";

    private const string Score = "score { staff mel staff bas }\n";

    [Theory]
    [InlineData(ByPart)]
    [InlineData(BySection)]
    // a part that leaves a section out
    [InlineData("time 4/4\noctave absolute\n"
        + "part mel { section A { c''1 | } section B { a''1 | } }\n"
        + "part bas { clef bass section B { f1 | } }\n")]
    public void WithNoForm_TheSectionsPlayAsTheFormOfTheirNamesWould(string book)
    {
        var formless = Notes(book + Score);
        Assert.NotEmpty(formless);
        Assert.Equal(Notes(book + "form { ~A ~B }\n" + Score), formless);
    }

    /// <summary>The independent pin (both spellings could drift together): the second part
    /// starts with the first, and section B begins where A's two bars end.</summary>
    [Fact]
    public void WithNoForm_ASecondPartStartsWithTheFirst()
    {
        var notes = Notes(ByPart + Score);
        int bar = notes.Single(n => n.Pitch == 79).Start;            // g''1, the second bar
        Assert.Equal(0, notes.Single(n => n.Pitch == 48).Start);     // the bass's c
        Assert.Equal(bar, notes.Single(n => n.Pitch == 55).Start);   // …and its g, under g''
        Assert.Equal(2 * bar, notes.Single(n => n.Pitch == 53).Start); // B's f
        Assert.Equal(2 * bar, notes.Single(n => n.Pitch == 81).Start); // B's a''
    }
}
