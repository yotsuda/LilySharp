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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The key a part's music is read in is THAT part's, in the sound as on the page: its last
/// block of the section's, else the section header's, else its own header's (its home), else
/// the file's. A degree chord (<c>&lt;d 3 5&gt;</c>) stacks in it, so the key is heard.
/// </summary>
/// <remarks>
/// Until 2026-11-15 the MIDI had ONE running key: with <c>part a { key d major }</c> every
/// part's degree chords were stacked in D major — the page drew part b's in the file's C — and a
/// key one part's music changed went on into the next part's block of the section. Found
/// measuring a leftover of session 1273 (the same leak, then in MusicXML).
/// </remarks>
public class PartHomeKeySoundTests
{
    // D with the third and the fifth of the key over it: F♯ in D major, F in C major.
    private const string Chord = "<d 3 5>1 |";
    private const int Fis = 54, F = 53;

    private static string Book(string top, string partA, string partB, string sectionA = "", string musicA = Chord, string musicB = Chord,
        string order = "ab")
    {
        string a = $"part a {{ clef treble {partA} }}\n", b = $"part b {{ clef treble {partB} }}\n";
        return $"octave absolute\ntime 4/4\n{top}\n" + (order == "ab" ? a + b : b + a)
            + $"section A {{ {sectionA} a {{ {musicA} }} b {{ {musicB} }} }}\n"
            + $"section B {{ a {{ {Chord} }} b {{ {Chord} }} }}\n"
            + "form { A B }\nscore { staff a staff b }\n";
    }

    /// <summary>The thirds each part sounds, section A then B: "a: 54 53  b: 53 53".</summary>
    private static string Thirds(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        return string.Join("  ", midi.Tracks.Where(t => t.Notes.Count > 0).Select(t => t.Name + ": "
            + string.Join(" ", t.Notes.Where(n => n.Pitch is Fis or F).OrderBy(n => n.StartTick).Select(n => n.Pitch))));
    }

    /// <summary>The same thirds as the page draws them (`lysc check --pitches`' list, which is
    /// in the order of the source: section A's a and b, then section B's).</summary>
    private static string ThirdsOnThePage(string source)
    {
        var thirds = LilySharp.Core.Semantics.ResolvedPitches.ForFile(SyntaxTree.Parse(source))!
            .Select(e => e.ToString()).Where(e => e.Contains("F#3") || e.Contains("F3"))
            .Select(e => e.Contains("F#3") ? Fis : F).ToList();
        Assert.Equal(4, thirds.Count);
        return $"a: {thirds[0]} {thirds[2]}  b: {thirds[1]} {thirds[3]}";
    }

    /// <summary>…and as MusicXML writes them: each part's F, sharp or not.</summary>
    private static string ThirdsInMusicXml(string source)
    {
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
        return string.Join("  ", xml.Descendants("part").Select((part, i) => "ab"[i] + ": " + string.Join(" ",
            part.Descendants("pitch").Where(p => p.Element("step")?.Value == "F")
                .Select(p => p.Element("alter")?.Value == "1" ? Fis : F))));
    }

    [Theory]
    // No part key: both in the file's.
    [InlineData("key c major", "", "", "a: 53 53  b: 53 53")]
    [InlineData("key d major", "", "", "a: 54 54  b: 54 54")]
    // A part's header key is its own, in every section; the other part keeps the file's.
    [InlineData("key c major", "key d major", "", "a: 54 54  b: 53 53")]
    [InlineData("key c major", "", "key d major", "a: 53 53  b: 54 54")]
    [InlineData("key d major", "key c major", "", "a: 53 53  b: 54 54")]
    [InlineData("", "key d major", "", "a: 54 54  b: 53 53")]
    [InlineData("key c major", "key d major", "key d major", "a: 54 54  b: 54 54")]
    public void APartsHeaderKey_IsHeardInThatPartAlone(string top, string partA, string partB, string heard)
    {
        string book = Book(top, partA, partB);
        Assert.Equal(heard, Thirds(book));
        Assert.Equal(heard, ThirdsOnThePage(book));
        Assert.Equal(heard, ThirdsInMusicXml(book));
    }

    [Fact]
    public void WhicheverPartIsDeclaredLast_DoesNotGiveTheOthersItsKey()
    {
        // (Until 2026-11-15 the LAST header to state a key was every part's.)
        string book = Book("key c major", "key d major", "", order: "ba");
        Assert.Equal("a: 54 54  b: 53 53", Thirds(book));
        Assert.Equal("a: 54 54  b: 53 53", ThirdsOnThePage(book.Replace("part b { clef treble  }\npart a { clef treble key d major }\n",
            "part a { clef treble key d major }\npart b { clef treble  }\n")));
    }

    [Fact]
    public void ASectionsHeaderKey_StandsForEveryPartOfIt_AndTheNextSectionIsBackHome()
    {
        // Section A in D major for both; B back in each part's home (a's C major header, b the file's F).
        string book = Book("key f major", "key c major", "", sectionA: "key d major");
        Assert.Equal("a: 54 53  b: 54 53", Thirds(book));
        Assert.Equal("a: 54 53  b: 54 53", ThirdsOnThePage(book));
    }

    /// <summary>A part's second block of a section goes on in the key its first left.</summary>
    [Fact]
    public void APartsNextBlockOfTheSection_GoesOnInItsKey()
    {
        string book = "octave absolute\ntime 4/4\nkey c major\npart a { clef treble }\npart b { clef treble }\n"
            + $"section A {{ a {{ key d major {Chord} }} b {{ {Chord} {Chord} }} a {{ {Chord} }} }}\n"
            + "form { A }\nscore { staff a staff b }\n";
        Assert.Equal("a: 54 54  b: 53 53", Thirds(book));
    }

    /// <summary>Music written inside its part (`part a { … section A { } }`) is in that part's key too.</summary>
    [Fact]
    public void MusicWrittenInsideItsPart_IsInThatPartsKey()
    {
        string book = "octave absolute\ntime 4/4\nkey c major\n"
            + $"part a {{ clef treble key d major section A {{ {Chord} }} section B {{ key c major {Chord} {Chord} }} section C {{ {Chord} }} }}\n"
            + $"part b {{ clef treble section A {{ {Chord} }} section B {{ {Chord} {Chord} }} section C {{ {Chord} }} }}\n"
            + "form { A B C }\nscore { staff a staff b }\n";
        // a: D major, then C major in B's music (twice), then home again; b: the file's throughout.
        Assert.Equal("a: 54 53 53 54  b: 53 53 53 53", Thirds(book));
    }

    /// <summary>
    /// A part header's key is the signature the part is written under, NOT a key a phrase is
    /// moved to: `riff` in `part a { key d major }` sounds as written, in the sound and in
    /// MusicXML alike (the page draws it so — Lab sessions/p1278/probe/ph.png), where a section
    /// header's `key d major` moves it up a tone for every part.
    /// </summary>
    [Fact]
    public void APartsHeaderKey_DoesNotMoveAPhrase_WhereASectionsDoes()
    {
        static string Riff(string partA, string sectionA) => "octave absolute\ntime 4/4\nkey c major\nphrase riff { c'4 d' e' f' | }\n"
            + $"part a {{ clef treble {partA} }}\npart b {{ clef treble }}\n"
            + $"section A {{ {sectionA} a {{ riff }} b {{ riff }} }}\nform {{ A }}\nscore {{ staff a staff b }}\n";
        static int[] Sounds(string source)
        {
            var tree = SyntaxTree.Parse(source);
            var spec = RenderSpecParser.FindFirst(tree);
            return [.. new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree).Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch)];
        }
        static string[] Written(string source)
            => [.. new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().Descendants("pitch")
                .Select(p => p.Element("step")!.Value + (p.Element("alter")?.Value == "1" ? "#" : ""))];

        Assert.Equal([60, 62, 64, 65, 60, 62, 64, 65], Sounds(Riff("key d major", "")));
        Assert.Equal(["C", "D", "E", "F", "C", "D", "E", "F"], Written(Riff("key d major", "")));
        Assert.Equal([62, 64, 66, 67, 62, 64, 66, 67], Sounds(Riff("", "key d major")));
        Assert.Equal(["D", "E", "F#", "G", "D", "E", "F#", "G"], Written(Riff("", "key d major")));
    }

    /// <summary>
    /// The key signature a written .mid states — it has one lane — is the PIECE's: the file's
    /// own; in a file that states none, its first part's header key. (Until 2026-11-15 the last
    /// part header's, over the file's own.)
    /// </summary>
    [Theory]
    [InlineData("part melody { key bes major section A { c'1 } }\nform { A }\nscore { staff melody }", -2)]
    [InlineData("key c major\npart melody { key bes major section A { c'1 } }\npart two { section A { e'1 } }\nform { A }\nscore { staff melody staff two }", 0)]
    [InlineData("key d major\npart melody { section A { c'1 } }\npart two { key bes major section A { e'1 } }\nform { A }\nscore { staff melody staff two }", 2)]
    [InlineData("part melody { section A { c'1 } }\npart two { key bes major section A { e'1 } }\nform { A }\nscore { staff melody staff two }", 0)]
    [InlineData("key c major\npart melody { key bes major }\nsection A { key d major melody { c'1 } }\nform { A }\nscore { staff melody }", 2)]
    public void TheWrittenFilesKeySignature_IsThePieces(string source, int fifths)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var file = MidiForFile.Shape(new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree), tree.GetRoot(), spec);
        Assert.Equal([(0, fifths)], file.Tracks[0].KeySignatures);
    }

    [Fact]
    public void AKeyOnePartsMusicChanges_IsNotTheNextPartsKey()
    {
        // a changes to D major in its music; b, played after it in the section, stays in C.
        string book = Book("key c major", "", "", musicA: "key d major " + Chord);
        Assert.Equal("a: 54 53  b: 53 53", Thirds(book));
        Assert.Equal("a: 54 53  b: 53 53", ThirdsOnThePage(book));
        // …and the other way round: b's change is not a's in the section after.
        Assert.Equal("a: 53 53  b: 54 53", Thirds(Book("key c major", "", "", musicB: "key d major " + Chord)));
    }
}
