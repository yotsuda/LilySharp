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
/// The &lt;technical&gt; marks a note carries into the MusicXML: its string number
/// (<c>\N</c>) and its fingering (<c>@finger(N)</c>). Until 2026-09-08 the exporter
/// had no arm for either node, so NO note carried a &lt;string&gt; or a
/// &lt;fingering&gt; — while the page (2026-08-09) and the twin read the same
/// annotations. The pairing rules are the collector's: a chord's outside list pairs
/// with its members in written order (own wins, the last repeats), and a string number
/// on a <c>&lt;&lt; … &gt;&gt;</c> group is every member's that names none.
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlTechnicalTests
{
    private static XDocument Export(string music)
    {
        var tree = SyntaxTree.Parse($"section A {{ m {{ {music} }} }}\nform main {{ A }}\nscore main {{ staff m }}");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MusicXmlExporter().Export(tree).ToXml();
    }

    /// <summary>Per pitched note, in document order: the &lt;string&gt; values of its
    /// &lt;technical&gt; (null when it has none).</summary>
    private static int?[] Strings(XDocument doc) =>
        doc.Descendants("note")
            .Where(n => n.Element("pitch") != null)
            .Select(n => n.Element("notations")?.Element("technical")?.Element("string") is { } s
                ? (int?)int.Parse(s.Value)
                : null)
            .ToArray();

    private static int?[] Fingerings(XDocument doc) =>
        doc.Descendants("note")
            .Where(n => n.Element("pitch") != null)
            .Select(n => n.Element("notations")?.Element("technical")?.Element("fingering") is { } f
                ? (int?)int.Parse(f.Value)
                : null)
            .ToArray();

    [Fact]
    public void AStringNumber_IsTheNotesTechnicalString()
    {
        var doc = Export("c4\\3 d4 e4\\1");
        Assert.Equal(new int?[] { 3, null, 1 }, Strings(doc));
        // Exactly one <string> per annotated note — never a second copy.
        Assert.Equal(2, doc.Descendants("string").Count());
    }

    [Fact]
    public void ChordStringNumbers_PairWithTheMembersAsThePageDoes()
    {
        // tablature.ly's claim, the page's TabStringNumberEntryTests: <e dis'>\5\4 ==
        // <e\5 dis'\4> == <e dis'\4>\5. The last outside one repeats for the rest.
        var doc = Export("<e\\5 dis'\\4>4 <e dis'>4\\5\\4 <e dis'\\4>4\\5 <c e g>4\\6");
        Assert.Equal(new int?[] { 5, 4, 5, 4, 5, 4, 6, 6, 6 }, Strings(doc));
        // A member's own \N wins over the outside list — and consumes none of it, so
        // the next member takes the FIRST outside one (the collector's pairing index).
        Assert.Equal(new int?[] { 3, 5 }, Strings(Export("<e\\3 dis'>4\\5\\4")));
        // No outside list, no own \N: nothing is written.
        Assert.Equal(new int?[] { null, null }, Strings(Export("<e dis'>4")));
    }

    [Fact]
    public void ArpeggioStringNumbers_OwnWinsThenTheGroups()
    {
        // The collector's DoubleAngleArpeggioTests.MemberMarks_AreApplied, in the XML.
        Assert.Equal(new int?[] { 3, 2, 1 }, Strings(Export("<< c\\3 e g\\1 >>4\\2")));
        Assert.Equal(new int?[] { 3, 3, 3 }, Strings(Export("<< c e g >>4\\3")));
        // A degree member takes the group's too.
        Assert.Equal(new int?[] { 3, 3, 3 }, Strings(Export("<< c 3 5 >>4\\3")));
        // A member spelled as tied parts carries the string on every part (each is a note).
        Assert.Equal(new int?[] { 3, 3, 3 }, Strings(Export("<< c . . . . d >>4\\3")));
        // A chord member keeps to its own pairing — the group's number is not its.
        Assert.Equal(new int?[] { null, null, 3 }, Strings(Export("<< <c e> g >>4\\3")));
    }

    [Fact]
    public void AFingering_IsTheNotesTechnicalFingering()
    {
        var doc = Export("c4@finger(1) d4 <e@finger(2) g@finger(4)>4");
        Assert.Equal(new int?[] { 1, null, 2, 4 }, Fingerings(doc));
        // Both marks on one note sit in one <technical>.
        var both = Export("c4@finger(1)\\3").Descendants("technical").Single();
        Assert.Equal("1", both.Element("fingering")!.Value);
        Assert.Equal("3", both.Element("string")!.Value);
    }
}
