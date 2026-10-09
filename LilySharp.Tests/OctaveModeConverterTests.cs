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
using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Rewriting a whole file between relative and absolute octaves
/// (<see cref="OctaveModeConverter"/>): every note keeps the pitch it sounds, and the text
/// changes only in the octave directives and the marks.
/// </summary>
/// <remarks>
/// Each conversion is held to the page (<see cref="ResolvedPitches"/>) AND to the MIDI, which
/// resolves octaves with a walker of its own — the converter trusts the collector, so the MIDI
/// is the independent witness. The corpus sweep that preceded these (2026-09-26: 419 books
/// each way, MIDI byte-identical in all 838) is what the rows below are distilled from.
/// </remarks>
[Trait("Category", "Unit")]
public class OctaveModeConverterTests
{
    private const string Score = "\nform { A }\nscore { staff m }\n";

    /// <summary>Converts, and asserts the result draws and plays every note as before.</summary>
    private static string Convert(string source, OctaveMode target)
    {
        var result = OctaveModeConverter.Convert(source, target);
        Assert.True(result.NewText != null, result.Error);
        Assert.Equal(Pitches(source), Pitches(result.NewText!));
        Assert.Equal(Midi(source), Midi(result.NewText!));
        return result.NewText!;
    }

    private static string[] Pitches(string source)
        => ResolvedPitches.ForFile(SyntaxTree.Parse(source))!.Select(e => e.Pitch).ToArray();

    private static int[] Midi(string source)
        => new MidiExporter().Export(SyntaxTree.Parse(source)).Tracks
            .SelectMany(t => t.Notes).Select(n => n.Pitch).ToArray();

    [Fact]
    public void ARelativeMelody_GoesAbsolute_AndComesBackAsWritten()
    {
        var relative = "part m { }\nsection A {\n  m { c4 d e f | g a b c | c' b, a g | }\n}" + Score;
        var absolute = Convert(relative, OctaveMode.Absolute);
        Assert.Equal(
            "octave absolute\npart m { }\nsection A {\n  m { c4 d e f | g a b c' | c'' b a g | }\n}" + Score,
            absolute);
        Assert.Equal(relative, Convert(absolute, OctaveMode.Relative));
    }

    [Fact]
    public void TheDirective_GoesAfterTheHeader_AndEveryOtherOneGoes()
    {
        var src = "title \"t\"\nkey g major\ntime 3/4\npart m { }\nsection A {\n"
            + "  m { g4 a b | octave absolute c'' d'' e'' | octave relative d c b | }\n}" + Score;
        var absolute = Convert(src, OctaveMode.Absolute);
        Assert.StartsWith("title \"t\"\nkey g major\ntime 3/4\noctave absolute\npart m { }", absolute);
        Assert.Single(SyntaxTree.Parse(absolute).GetRoot().DescendantNodes<OctaveDirectiveSyntax>());

        var relative = Convert(src, OctaveMode.Relative);
        Assert.Empty(SyntaxTree.Parse(relative).GetRoot().DescendantNodes<OctaveDirectiveSyntax>());
    }

    [Fact]
    public void AFileAlreadyAbsolute_KeepsItsDirectiveInPlace()
    {
        var src = "octave absolute\npart m { }\nsection A {\n  m { c'4 e' g' c'' | }\n}" + Score;
        Assert.Equal(src, Convert(src, OctaveMode.Absolute));
    }

    /// <summary>A chord's group mark moves the frame in relative mode; absolute has no frame,
    /// so it is folded into the members rather than kept beside members that restate it.
    /// </summary>
    [Fact]
    public void AChordsGroupMark_IsFoldedIntoItsMembers_InAbsolute()
    {
        var src = "part m { }\nsection A {\n  m { <c e g>'2 c | << c e g >>,4 c <c' 3 5> e | }\n}" + Score;
        var absolute = Convert(src, OctaveMode.Absolute);
        // <c e g>' is C5 E5 G5 and leaves the frame at C5; << c e g >>, then reads C5 and
        // drops a whole octave — C4 E4 G4 — which absolute spells bare.
        Assert.Contains("<c' e' g'>2 c' | << c e g >>4 c <c' 3, 5,> e |", absolute);
        Convert(absolute, OctaveMode.Relative);
    }

    [Fact]
    public void PhrasesAndSectionReferences_KeepTheirOwnMarks()
    {
        var src = "part m { }\nphrase P { g a b c }\nsection A {\n  m { P' e f | P, c2 | }\n}\n"
            + "form { A ~A' }\nscore { staff m }\n";
        var absolute = Convert(src, OctaveMode.Absolute);
        // A phrase body opens a fresh frame at C4, so its g is G3 — g, in absolute — wherever
        // it is played; the references keep their own marks, which mean the same in both.
        Assert.Contains("phrase P { g, a, b, c }", absolute);
        Assert.Contains("m { P' e f | P, c,2 | }", absolute);
        Assert.Contains("form { A ~A' }", absolute);
        Convert(absolute, OctaveMode.Relative);
    }

    /// <summary>A preset moves the RELATIVE anchor only (InstrumentDefaults.AnchorOctave), so
    /// a bass part's bare letters change their marks both ways.</summary>
    [Fact]
    public void AnInstrumentPresetsAnchor_IsWrittenIntoTheMarks()
    {
        var src = "part m { instrument bass5 \"bass\" }\nsection A {\n  m { g4 a b c | }\n}" + Score;
        var absolute = Convert(src, OctaveMode.Absolute);
        Assert.Contains("m { g,,4 a,, b,, c, | }", absolute);
        Assert.Equal(src, Convert(absolute, OctaveMode.Relative));
    }

    [Fact]
    public void ANoteNoScorePlays_IsRefused_NamingItsSection()
    {
        var src = "part m { }\nsection A {\n  m { c4 d e f | }\n}\nsection B {\n  m { g4 a b c | }\n}" + Score;
        var result = OctaveModeConverter.Convert(src, OctaveMode.Absolute);
        Assert.Null(result.NewText);
        Assert.Contains("line 6, column 7", result.Error);
        Assert.Contains("section B", result.Error);
    }

    [Theory]
    [InlineData("part m { }\nsection A {\n  m { c4 d e f | \n}" + Score)]
    [InlineData("using \"other.lys\"\npart m { }\nsection A {\n  m { c4 d e f | }\n}" + Score)]
    public void AFileItCannotCheck_IsLeftAlone(string src)
    {
        var result = OctaveModeConverter.Convert(src, OctaveMode.Absolute);
        Assert.Null(result.NewText);
        Assert.NotNull(result.Error);
    }
}
