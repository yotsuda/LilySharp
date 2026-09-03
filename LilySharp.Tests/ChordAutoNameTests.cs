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
/// Bare <c>@chord</c> on a chord auto-derives the chord symbol from its notes. The
/// root is found from the PITCH CLASSES — no member is privileged for being written
/// first — and when it is not the lowest note the chord is an inversion, whose lowest
/// note prints as the slash bass. The explicit form stays <c>@chord(Cmaj7)</c>.
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

    // --- Inversions: the root comes from the pitches, not from the write order ---

    [Theory]
    // These are RELATIVE (the harness writes no `octave absolute`), so a member that
    // would fall below the one before it goes up instead: <e g c> sounds e g c', whose
    // lowest note is e. Under `octave absolute` the same spelling sounds e g c and is
    // therefore plain "C" — the name follows what SOUNDS, not what is typed.
    [InlineData("<e g c>", "C/E")]
    [InlineData("<g c e>", "C/G")]
    [InlineData("<e g c e>", "C/E")]
    public void AnInversion_TakesItsLowestNoteAsTheSlashBass(string chord, string expected)
        => Assert.Equal(expected, AutoName(chord));

    [Fact]
    public void TheBassIsTriedFirst_SoAChordThatNamesTodayKeepsItsName()
    {
        // {C,E,G,A} reads as C6 rooted on C and as Am7 rooted on A. Preferring the BASS
        // settles it, which is why making inversions nameable could not turn C6 into
        // Am7/C — the ambiguity is decided before the other roots are ever tried.
        Assert.Equal("C6", AutoName("<c e g a>"));
        Assert.Equal("Am7", AutoName("<a c e g>"));
        // And a four-pitch-class set cannot read as a three-note triad at all: C-E-G-B
        // has no Em reading to lose to.
        Assert.Equal("Cmaj7", AutoName("<c e g b>"));
    }

    [Fact]
    public void ASetNoRootCanName_IsStillLeftUnnamed()
    {
        // The reader tries every member as the root; when none of them names a
        // registered quality the chord gets no symbol, exactly as before.
        Assert.Null(AutoName("<c cis d>"));
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
