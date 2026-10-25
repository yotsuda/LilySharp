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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LYS1052 (owner's request 2026-10-25): a note the part's <c>instrument</c> cannot sound is
/// warned — by its SOUNDING pitch, so a transposing instrument is judged by what it plays.
/// </summary>
public class InstrumentRangeTests
{
    private static Diagnostic[] RangeWarnings(string source)
        => SemanticValidation.Run(SyntaxTree.Parse(source))
            .Where(d => d.Code == DiagnosticCodes.NoteOutsideInstrumentRange).ToArray();

    private static string Book(string header, string music) => $$"""
        octave absolute
        time 4/4
        part p { {{header}} }
        section A { p { {{music}} } }
        form { A }
        score { staff p }
        """;

    [Fact]
    public void EveryPreset_HasARange()
    {
        foreach (var preset in InstrumentDefaults.KnownInstruments)
        {
            var range = InstrumentDefaults.SoundingRange(preset);
            Assert.True(range is { } r && r.Low < r.High, preset);
        }
        Assert.Null(InstrumentDefaults.SoundingRange("kazoo"));
    }

    [Fact]
    public void ANoteBelowTheFlute_IsWarnedAtTheNote_OnceEach()
    {
        // The flute's bare c is C5 (its preset's octave); c, is C4, its lowest note, and b,, B3.
        string source = Book("instrument flute", "c,4 b,, b,, c, |");
        var warnings = RangeWarnings(source);
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, w => Assert.StartsWith("b,,", source[w.Span.Start..]));
        Assert.Contains("sounds B3, below the range of the Flute (C4-D7", warnings[0].Message);
    }

    [Fact]
    public void ATransposingInstrument_IsJudgedByWhatItSounds()
    {
        // A B♭ clarinet's written E3 sounds D3, its lowest note; its written D3 would sound C3.
        Assert.Empty(RangeWarnings(Book("instrument clarinet", "e,4 f, g, a, |")));
        var w = Assert.Single(RangeWarnings(Book("instrument clarinet", "d,4 f, g, a, |")));
        Assert.Contains("sounds C3, below", w.Message);
    }

    [Fact]
    public void APartWithNoInstrument_IsNotJudged()
    {
        Assert.Empty(RangeWarnings(Book("clef treble", "c,,,4 d e f |")));
    }

    [Fact]
    public void TheFiveStringContrabass_ReachesBelowTheFourStringOnesLowE()
    {
        // Written C2 sounds C1 (the contrabass sounds an octave below the page): below the four
        // strings' E1, within the five strings' B0.
        Assert.Single(RangeWarnings(Book("instrument contrabass", "c,4 e, g, c |")));
        Assert.Empty(RangeWarnings(Book("instrument contrabass5", "c,4 e, g, c |")));
        Assert.Equal("Contrabass", InstrumentDefaults.PrintedName("contrabass5"));
    }

    [Fact]
    public void ANoteAboveTheSoprano_IsWarned()
    {
        var w = Assert.Single(RangeWarnings(Book("instrument soprano", "c''4 d'' c'' b' |")));
        Assert.Contains("sounds D6, above the range of the Soprano (C4-C6", w.Message);
    }
}
