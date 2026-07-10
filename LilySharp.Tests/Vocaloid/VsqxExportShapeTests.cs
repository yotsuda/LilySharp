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
            section A { m { c4 d e f | g1 } }
            form main { A }
            score main { staff m }
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
              m { c4 d e f | }
              lyrics { la le li lo | }
            }
            form main { A }
            score main { staff m }
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
            section A { m { c4 d e f | } }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal("vsq4", root.Name.LocalName);
        Assert.Equal(Ns, root.Name.Namespace);
        Assert.NotEmpty(root.Descendants(Ns + "note"));
    }
}
