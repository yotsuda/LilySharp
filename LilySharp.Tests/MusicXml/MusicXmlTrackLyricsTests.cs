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
/// A top-level lyrics track (<c>lyrics words sings vo { … }</c>, placed by the score's
/// <c>lyrics words</c>) as <c>&lt;lyric&gt;</c> on the notes the page sets it under. Until
/// 2026-09-30 only lyrics written inside a section or part block reached the file
/// (LilySharp-Omr docs/repro/musicxml-exporter-bugs.md #2).
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlTrackLyricsTests
{
    private const string Music =
        "octave absolute\ntime 4/4\npart vo { clef treble }\npart gt { clef treble }\n"
        + "section A {\n vo { c'4 d' e' f' | g'2 a' | }\n gt { c1 | c1 | }\n}\n"
        + "lyrics words sings vo { So if you lis- | ten now | }\nform { A }\n";

    private static XDocument Export(string score)
    {
        var tree = SyntaxTree.Parse(Music + score);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MusicXmlExporter().Export(tree).ToXml();
    }

    /// <summary>Each lyric of a part as "text/syllabic".</summary>
    private static string[] Lyrics(XDocument doc, int part) =>
        doc.Descendants("part").ElementAt(part).Descendants("lyric")
            .Select(l => l.Element("text")!.Value + "/" + l.Element("syllabic")!.Value)
            .ToArray();

    [Fact]
    public void APlacedTrack_IsSungOnItsPartsNotes_AHyphenRunningOverTheBar()
    {
        var doc = Export("score { staff vo\n lyrics words\n staff gt }");
        Assert.Equal(new[] { "So/single", "if/single", "you/single", "lis/begin", "ten/end", "now/single" },
            Lyrics(doc, 0));
        Assert.Empty(Lyrics(doc, 1));
        Assert.All(doc.Descendants("lyric"), l => Assert.Equal("1", l.Attribute("number")!.Value));
    }

    /// <summary>A track the score does not place is not on the page, and not in the file.</summary>
    [Fact]
    public void ATrackTheScoreDoesNotPlace_IsNotWritten()
        => Assert.Empty(Export("score { staff vo\n staff gt }").Descendants("lyric"));
}
