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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A part header's <c>key</c> is THAT part's home key, in MusicXML as on the page: each of its
/// sections opens in it, a section's own key stands for that section, and a part that writes
/// none opens in the file's. Until 2026-11-15 the exporter read a part header's key as the
/// FILE's, so the last part to write one gave every part its key (found by a probe of
/// <c>key none</c>: <c>key f major</c> beside <c>part a { key d major }</c> exported both parts
/// in D major).
/// </summary>
public class MusicXmlPartHomeKeyTests
{
    private const string Music = "c'4 d' e' f' | g'1 |";

    private static string Book(string top, string partA, string partB, string sectionB = "")
        => $"octave absolute\ntime 4/4\n{top}\npart a {{ clef treble {partA} }}\npart b {{ clef treble {partB} }}\n"
            + $"section A {{ a {{ {Music} }} b {{ {Music} }} }}\n"
            + $"section B {{ {sectionB} a {{ {Music} }} b {{ {Music} }} }}\n"
            + $"section C {{ a {{ {Music} }} b {{ {Music} }} }}\n"
            + "form { A B C }\nscore { staff a staff b }\n";

    /// <summary>Each part's key statements in order: the fifths, or "none" for a
    /// <c>&lt;key&gt;</c> that states nothing.</summary>
    private static string[] Keys(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var xml = new MusicXmlExporter().Export(tree).ToXml();
        return [.. xml.Descendants("part").Select(part => string.Join(" ",
            part.Descendants("key").Select(k => k.Element("fifths")?.Value ?? "none")))];
    }

    [Fact]
    public void APartsHeaderKey_IsItsOwnHome_AndTheOtherPartKeepsTheFiles()
    {
        // The page: a opens in D major, b in F major; section B's G major stands for both;
        // section C goes back to each part's home.
        Assert.Equal(["2 1 2", "-1 1 -1"], Keys(Book("key f major", "key d major", "", "key g major")));
        // No section states a key: each part states its home once.
        Assert.Equal(["2", "-1"], Keys(Book("key f major", "key d major", "")));
    }

    [Theory]
    // The later part's key is not the earlier part's.
    [InlineData("key f major", "", "key d major", "-1", "2")]
    // Each part its own.
    [InlineData("key f major", "key d major", "key ees major", "2", "-3")]
    // A file that writes no key is in C major for the part that writes none.
    [InlineData("", "key d major", "", "2", "0")]
    // `key none` in one part is that part's alone.
    [InlineData("key f major", "key none", "", "none", "-1")]
    [InlineData("key f major", "", "key none", "-1", "none")]
    // …and a part's key beside a file with none.
    [InlineData("key none", "key d major", "", "2", "none")]
    public void EachPart_OpensInItsOwnHomeKey(string top, string partA, string partB, string a, string b)
        => Assert.Equal([a, b], Keys(Book(top, partA, partB)));

    /// <summary>A key in the MUSIC a part writes inside its own braces is that music's — a
    /// change where it stands — and not the part's home.</summary>
    [Fact]
    public void AKeyInAPartsOwnMusic_IsNotItsHome()
    {
        const string ByPart = "octave absolute\ntime 4/4\nkey f major\n"
            + "part a { clef treble section A { c'1 | } section B { key d major c'1 | } section C { c'1 | } }\n"
            + "part b { clef treble section A { c'1 | } section B { c'1 | } section C { c'1 | } }\n"
            + "form { A B C }\nscore { staff a staff b }\n";
        Assert.Equal(["-1 2 -1", "-1"], Keys(ByPart));
    }

    [Fact]
    public void TheFilesKey_IsTheFilesWhereverItIsWritten()
    {
        // A top-level key written after the parts is still the file's, not shadowed by a header's.
        string book = Book("", "key d major", "") + "key f major\n";
        Assert.Equal(["2", "-1"], Keys(book));
        // Of two top-level keys the last is the file's, as it was.
        Assert.Equal(["2", "-1"], Keys(Book("key g major\nkey f major", "key d major", "")));
        // With no part header key the document is what it was: one key for both parts.
        Assert.Equal(["-1 1 -1", "-1 1 -1"], Keys(Book("key f major", "", "", "key g major")));
    }
}
