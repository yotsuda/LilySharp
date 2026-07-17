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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Bare <c>@chord</c> on a chord auto-derives the chord symbol from its notes:
/// the root is the first member, the remaining members' pitch classes give the
/// quality. The explicit form stays <c>@chord(c:maj7)</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChordAutoNameTests
{
    private static string? AutoName(string chord, string key = "c major")
    {
        var src = $"time 4/4\nkey {key}\n{chord}@chord";
        var names = new MeasureCollector().Collect(SyntaxTree.Parse(src)).ChordNames;
        return names.FirstOrDefault()?.ChordText;
    }

    [Theory]
    [InlineData("<c e g>", "C")]
    [InlineData("<d f a>", "Dm")]
    [InlineData("<b d f>", "Bdim")]
    [InlineData("<c e gis>", "Caug")]
    [InlineData("<c e g b>", "Cmaj7")]
    [InlineData("<g b d f>", "G7")]
    [InlineData("<d f a c>", "Dm7")]
    [InlineData("<c e g a>", "C6")]
    [InlineData("<a c e>", "Am")]
    [InlineData("<c d g>", "Csus2")]
    [InlineData("<c f g>", "Csus4")]
    public void BareChord_AutoNamesFromNotes(string chord, string expected)
        => Assert.Equal(expected, AutoName(chord));

    [Fact]
    public void OrderIndependent_SameName()
    {
        // The chord is order-independent, so its derived name is too.
        Assert.Equal("C", AutoName("<c e g>"));
        Assert.Equal("C", AutoName("<c g e>"));
    }

    [Theory]
    [InlineData("<c 3 5>", "C")]      // degree chord
    [InlineData("<d 3 5 7>", "Dm7")]  // degrees in C major
    [InlineData("<1 3 5>", "C")]      // omitted root = the key's tonic triad
    public void BareChord_NamesDegreeChords(string chord, string expected)
        => Assert.Equal(expected, AutoName(chord));

    [Fact]
    public void DegreeChord_NameFollowsTheKey()
    {
        // <d 3 5 7> is Dm7 in C (F natural) but D7 in G (F♯).
        Assert.Equal("Dm7", AutoName("<d 3 5 7>", key: "c major"));
        Assert.Equal("D7", AutoName("<d 3 5 7>", key: "g major"));
    }

    [Theory]
    [InlineData("<< c e g >>", "C")]        // pitches
    [InlineData("<< c g e >>", "C")]        // order-independent members
    [InlineData("<< d f a c >>", "Dm7")]
    [InlineData("<< c 3 5 >>", "C")]        // root + degrees
    [InlineData("<< 1 3 5 >>", "C")]        // degree-opened: anchored on the tonic
    [InlineData("<< 8 5 3 1 >>", "C")]      // a descending figure names the same
    [InlineData("<< 2 4 6 >>", "Dm")]       // named from its first degree, like <2 4 6>
    [InlineData("<< <c e> g >>", "C")]      // a nested chord contributes its pitches
    public void BareChord_NamesArpeggios(string arpeggio, string expected)
        => Assert.Equal(expected, AutoName(arpeggio));

    [Fact]
    public void ArpeggioName_FollowsTheKey()
    {
        // << d 3 5 7 >> is Dm7 in C (F natural) but D7 in G (F♯), like the chord.
        Assert.Equal("Dm7", AutoName("<< d 3 5 7 >>", key: "c major"));
        Assert.Equal("D7", AutoName("<< d 3 5 7 >>", key: "g major"));
    }

    [Fact]
    public void UnrecognizedArpeggio_ProducesNoSymbol_AndWarns()
    {
        Assert.Null(AutoName("<< c cis d >>")); // a cluster matches no quality
        // The pure named-pitch case is key-independent, so LYS1020 warns statically.
        Assert.Contains(
            LilySharp.Core.Semantics.SemanticValidation.Run(
                SyntaxTree.Parse("{ << c cis d >>@chord }")),
            d => d.Code == LilySharp.Core.Syntax.DiagnosticCodes.ChordNotRecognized);
    }

    [Fact]
    public void UnrecognizedChord_ProducesNoSymbol()
    {
        // A cluster matches no registered quality → no symbol.
        Assert.Null(AutoName("<c des d>"));
    }

    private static bool NotRecognizedDiagnosed(string chord)
    {
        var tree = SyntaxTree.Parse($"time 4/4\nkey c major\n{chord}@chord");
        return LilySharp.Core.Semantics.SemanticValidation.Run(tree)
            .Any(d => d.Code == DiagnosticCodes.ChordNotRecognized);
    }

    [Theory]
    [InlineData("<c des d>")] // a cluster
    [InlineData("<c g>")]     // a rootless-5th dyad has no 3rd to name a quality
    public void UnrecognizedChord_IsDiagnosed(string chord)
        => Assert.True(NotRecognizedDiagnosed(chord));

    [Theory]
    [InlineData("<c e g>")]   // a real chord
    [InlineData("<d f a c>")]
    [InlineData("<c e>")]     // root+3rd dyad names its quality (C)
    [InlineData("<c 3 5>")]   // a degree chord — left to the collector, never warned
    public void RecognizedOrDegreeChord_IsNotDiagnosed(string chord)
        => Assert.False(NotRecognizedDiagnosed(chord));

    [Fact]
    public void BareChordOnSingleNote_DerivesNothing()
    {
        // @chord on a note is the explicit-entry completion state (@chord(...)),
        // not an auto-derivation.
        Assert.Null(AutoName("c4"));
    }
}
