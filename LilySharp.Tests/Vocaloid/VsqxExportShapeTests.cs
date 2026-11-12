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
using System.Xml.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Core.Vocaloid;
using Xunit;

namespace LilySharp.Tests.Vocaloid;

/// <summary>
/// Shape assertions on the exported VOCALOID (.vsqx) document — the only test
/// coverage for the VsqxExporter. Guards that a vocal line's notes, timing and
/// sung syllables actually reach the vsq4 &lt;note&gt; events (the whole point of
/// a "vocal + lyrics" export) and that the document is well-formed vsq4.
/// </summary>
public class VsqxExportShapeTests
{
    private static readonly XNamespace Ns =
        "http://www.yamaha.co.jp/vocaloid/schema/vsq4/";
    private const int Ppq = 480; // vsq4 ticks per quarter note (VsqxExporter.Resolution)

    private static XElement Export(string source) =>
        new VsqxExporter().Export(SyntaxTree.Parse(source)).Root!;

    private static List<XElement> Notes(XElement root) =>
        root.Descendants(Ns + "note").ToList();

    private static int Int(XElement note, string child) => (int)note.Element(Ns + child)!;
    private static string Lyric(XElement note) => note.Element(Ns + "y")!.Value;

    [Fact]
    public void Notes_CarryPitchTickAndDuration()
    {
        // Relative-default c d e f g stays near middle C -> C4..G4 (MIDI 60..67).
        var root = Export("""
            part m { clef treble }
            section A { m { c'4 d e f | g1 } }
            form { A }
            score { staff m }
            """);
        var notes = Notes(root);
        Assert.Equal(5, notes.Count);
        Assert.Equal(new[] { 60, 62, 64, 65, 67 }, notes.Select(n => Int(n, "n")));
        // Sequential 480-PPQ ticks: four quarters (0,480,960,1440) then a whole.
        Assert.Equal(new[] { 0, Ppq, 2 * Ppq, 3 * Ppq, 4 * Ppq }, notes.Select(n => Int(n, "t")));
        // Quarter = 480, whole = 1920.
        Assert.Equal(new[] { Ppq, Ppq, Ppq, Ppq, 4 * Ppq }, notes.Select(n => Int(n, "dur")));
    }

    [Fact]
    public void Lyrics_AttachToNotesInOrder()
    {
        // A `lyrics { }` block INSIDE the section (beside the voice) is how a
        // syllable line associates with the melody (cf. showcase/07-lead-sheet).
        var root = Export("""
            part m { clef treble }
            section A {
              m { c'4 d e f | }
              lyrics { la le li lo | }
            }
            form { A }
            score { staff m  lyrics }
            """);
        var notes = Notes(root);
        Assert.Equal(4, notes.Count);
        // Each note carries its own syllable in order -- not the "-" placeholder.
        Assert.Equal(new[] { "la", "le", "li", "lo" }, notes.Select(Lyric));
    }

    [Fact]
    public void Output_IsWellFormedVsq4()
    {
        var root = Export("""
            part m { clef treble }
            section A { m { c'4 d e f | } }
            form { A }
            score { staff m }
            """);
        Assert.Equal("vsq4", root.Name.LocalName);
        Assert.Equal(Ns, root.Name.Namespace);
        Assert.NotEmpty(root.Descendants(Ns + "note"));
    }
    /// <summary>A vocal line is one voice: the second voice of a span stood AFTER the first in
    /// the line (a held "-" a bar long) and pushed every later note a bar late; and the bar line
    /// after a span that ended on its own is the empty bar the page draws (found 2026-11-13,
    /// checking each export for that bar — the MIDI, MusicXML and LilyPond walks had it wrong).</summary>
    [Theory]
    [InlineData("voice { c'1 } { e'1 } | d'1 | g'1 |", "あ | い | う |", new[] { 0, 1920, 3840 })]
    [InlineData("voice { c'1 | } { e'1 | } | d'1 | g'1 |", "あ | | い | う |", new[] { 0, 3840, 5760 })]
    [InlineData("c'1 | | d'1 | g'1 |", "あ | | い | う |", new[] { 0, 3840, 5760 })]
    [InlineData("c'1 | voice { c'2 d'2 } { e'1 } | g'1 |", "あ | い う | え |", new[] { 0, 1920, 2880, 3840 })]
    [InlineData("c'1 | voice { c'2 } { e'2 } d'2 | g'1 |", "あ | い う | え |", new[] { 0, 1920, 2880, 3840 })]
    public void ASecondVoice_IsNotInTheVocalLine_AndTakesNoTime(string music, string words, int[] ticks)
    {
        var notes = Notes(Export("octave absolute\nlanguage japanese\ntime 4/4\npart m { lyrics w }\n"
            + $"section A {{ m {{ {music} }} lyrics w {{ {words} }} }}\nform {{ A }}\nscore {{ staff m  lyrics w }}\n"));
        Assert.Equal(ticks, notes.Select(n => Int(n, "t")));
        Assert.DoesNotContain("-", notes.Select(Lyric));
    }
    /// <summary>The line is the voice the words stand on, as the voice Cantoria sings is: words a
    /// score places on a named second voice (`lyrics w on alt`) take that voice's notes, not the
    /// first voice's (owner's decision 2026-11-13).</summary>
    [Fact]
    public void TheVocalLine_IsTheVoiceTheWordsStandOn()
    {
        var notes = Notes(Export("octave absolute\nlanguage japanese\ntime 4/4\npart m { }\n"
            + "section A { m { voice sop { c''1 | d''2 e''2 | } alt { e'4 f' g' a' | g'2 f'2 | } } lyrics w { あ い う え | お か | } }\n"
            + "form { A }\nscore { staff m  lyrics w on alt }\n"));
        Assert.Equal(new[] { 64, 65, 67, 69, 67, 65 }, notes.Select(n => Int(n, "n")));
        Assert.Equal(new[] { 0, 480, 960, 1440, 1920, 2880 }, notes.Select(n => Int(n, "t")));
        Assert.Equal(new[] { "あ", "い", "う", "え", "お", "か" }, notes.Select(Lyric));
    }
}
