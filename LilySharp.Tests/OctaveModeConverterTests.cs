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
    public void ARelativeMelody_GoesAbsolute_AndComesBackStatingRelative()
    {
        var relative = "part m { }\nsection A {\n  m { c4 d e f | g a b c | c' b, a g | }\n}" + Score;
        var absolute = Convert(relative, OctaveMode.Absolute);
        Assert.Equal(
            "octave absolute\npart m { }\nsection A {\n  m { c4 d e f | g a b c' | c'' b a g | }\n}" + Score,
            absolute);
        // The file's directive stays and names the mode (owner's report 2026-10-17: the line
        // used to vanish, relative being written by removing it); a file that never wrote one
        // is left as it is.
        Assert.Equal("octave relative\n" + relative, Convert(absolute, OctaveMode.Relative));
        Assert.Equal(relative, Convert(relative, OctaveMode.Relative));
    }

    [Fact]
    public void TheDirective_GoesAfterTheHeader_AndEveryOtherOneGoes()
    {
        var src = "title \"t\"\nkey g major\ntime 3/4\npart m { }\nsection A {\n"
            + "  m { g4 a b | octave absolute c'' d'' e'' | octave relative d c b | }\n}" + Score;
        var absolute = Convert(src, OctaveMode.Absolute);
        Assert.StartsWith("title \"t\"\nkey g major\ntime 3/4\noctave absolute\npart m { }", absolute);
        Assert.Single(SyntaxTree.Parse(absolute).GetRoot().DescendantNodes<OctaveDirectiveSyntax>());

        // Relative too: the switches inside music go, and the file — which stated a mode —
        // states relative once, after the header.
        var relative = Convert(src, OctaveMode.Relative);
        Assert.StartsWith("title \"t\"\nkey g major\ntime 3/4\noctave relative\npart m { }", relative);
        Assert.Single(SyntaxTree.Parse(relative).GetRoot().DescendantNodes<OctaveDirectiveSyntax>());
    }

    [Fact]
    public void AnAbsoluteFile_GoesRelative_KeepingItsLine()
    {
        var absolute = "octave absolute\npart m { }\nsection A {\n  m { c'4 d' e' f' | }\n}" + Score;
        var relative = Convert(absolute, OctaveMode.Relative);
        Assert.Equal("octave relative\npart m { }\nsection A {\n  m { c'4 d e f | }\n}" + Score, relative);
        Assert.Equal(absolute, Convert(relative, OctaveMode.Absolute));
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
        // drops a whole octave — C4 E4 G4 — which absolute spells bare. <c' 3 5> is read as
        // its first note: C5, the degrees stacked on it (E5 G5), the frame left at C5, so
        // the e after it is E5 (session 914; the bare-letter anchor had E4 G4 and E4).
        Assert.Contains("<c' e' g'>2 c' | << c e g >>4 c <c' 3 5> e' |", absolute);
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

    /// <summary>A preset is the part's anchor in BOTH modes (InstrumentDefaults.AnchorOctave /
    /// AbsoluteBaseOctave, one chain since 2026-10-13): a bass part's G2 is `g,` in absolute,
    /// one comma under its C3 base, and converts back to the same relative text.</summary>
    [Fact]
    public void AnInstrumentPresetsAnchor_IsWrittenIntoTheMarks()
    {
        var src = "part m { instrument bass5 \"bass\" }\nsection A {\n  m { g4 a b c | }\n}" + Score;
        var absolute = Convert(src, OctaveMode.Absolute);
        Assert.Contains("m { g,4 a, b, c | }", absolute);
        Assert.Equal("octave relative\n" + src, Convert(absolute, OctaveMode.Relative));
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
