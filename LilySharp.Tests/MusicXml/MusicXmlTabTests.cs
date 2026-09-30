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

using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A score's <c>tab</c> staff in the MusicXML: the TAB staff (<c>&lt;clef&gt;&lt;sign&gt;TAB</c>,
/// <c>&lt;staff-details&gt;</c> with the lines and open strings) and every note's string and
/// fret as the page's tab staff prints them. Until 2026-09-30 a tab staff was not in the file
/// at all (LilySharp-Omr docs/repro/musicxml-exporter-bugs.md #1).
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlTabTests
{
    private static XDocument Export(string music, string score)
    {
        var tree = SyntaxTree.Parse(
            "octave absolute\ntime 4/4\npart gt { clef treble_8  tuning guitar }\n"
            + $"section A {{ gt {{ {music} }} }}\nform main {{ A }}\nscore main {{ {score} }}");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MusicXmlExporter().Export(tree).ToXml();
    }

    /// <summary>Each note of a staff (null = a one-staff part's notes) as "s{string}f{fret}",
    /// or "-" when it carries no fret.</summary>
    private static string[] Frets(XDocument doc, string? staff) =>
        doc.Descendants("note")
            .Where(n => n.Element("rest") == null && (string?)n.Element("staff") == staff)
            .Select(n => n.Descendants("fret").SingleOrDefault() is { } fret
                ? $"s{n.Descendants("string").Single().Value}f{fret.Value}"
                : "-")
            .ToArray();

    [Fact]
    public void ATabBesideItsStaff_IsTheSecondStaffOfThePart_WithStringsAndFrets()
    {
        var doc = Export("e,4 a, d g |", "staff gt\n tab gt");
        Assert.Single(doc.Descendants("part"));
        var attrs = doc.Descendants("attributes").First();
        Assert.Equal("2", attrs.Element("staves")!.Value);
        Assert.Equal(new[] { "1:G", "2:TAB" },
            attrs.Elements("clef").Select(c => c.Attribute("number")!.Value + ":" + c.Element("sign")!.Value));
        var details = attrs.Element("staff-details")!;
        Assert.Equal("2", details.Attribute("number")!.Value);
        Assert.Equal("6", details.Element("staff-lines")!.Value);
        // Line 1 is the lowest string, E2.
        var low = details.Elements("staff-tuning").First();
        Assert.Equal(("1", "E", "2"), (low.Attribute("line")!.Value,
            low.Element("tuning-step")!.Value, low.Element("tuning-octave")!.Value));

        // The notation staff keeps its notes without frets; the TAB staff has the same notes
        // on the open strings — a treble_8 guitar's e, a, d, g sound E2 A2 D3 G3.
        Assert.Equal(new[] { "-", "-", "-", "-" }, Frets(doc, "1"));
        Assert.Equal(new[] { "s6f0", "s5f0", "s4f0", "s3f0" }, Frets(doc, "2"));
        Assert.All(doc.Descendants("note").Where(n => (string?)n.Element("staff") == "2"),
            n => Assert.Equal("5", n.Element("voice")!.Value));
    }

    /// <summary>
    /// A slash note (<c>/4</c>) is no note on the TAB staff — the page leaves it out there,
    /// and a reader fretted the written one (MuseScore drew a "7" per slash; LilySharp-Omr
    /// lilysharp-feedback-2026-09-30.md #13): the TAB copy keeps its time as a
    /// <c>&lt;forward&gt;</c> in voice 5, and the notation staff's slash stays. Every note on
    /// either staff names its voice (#16).
    /// </summary>
    [Fact]
    public void ASlashNote_IsAGapOnTheTabStaff_AndEveryNoteNamesItsVoice()
    {
        var doc = Export("/4 4 8 8 4 | e,4 a, d g |", "staff gt\n tab gt");
        var m1 = doc.Descendants("measure").First().Elements()
            .Where(e => e.Name == "note" || e.Name == "forward")
            .Select(e => $"{e.Name.LocalName}:{(e.Element("unpitched") != null ? "slash" : "-")}:s{e.Element("staff")?.Value}v{e.Element("voice")?.Value}")
            .ToArray();
        Assert.Equal(Enumerable.Repeat("note:slash:s1v1", 5).Concat(Enumerable.Repeat("forward:-:s2v5", 5)), m1);
        Assert.Equal(new[] { "s6f0", "s5f0", "s4f0", "s3f0" }, Frets(doc, "2"));
        Assert.All(doc.Descendants("note"), n => Assert.NotNull(n.Element("voice")));
    }

    /// <summary>
    /// A beam group with slashes rejoins over the fretted notes on the TAB staff (2026-10-01):
    /// the gap used to take its <c>&lt;beam&gt;</c> with it, so a group opening with a slash began
    /// at "continue". A fretted note left alone in its group is a flag. The notation staff
    /// keeps the page's beams as they are.
    /// </summary>
    [Fact]
    public void ABeamOverSlashes_RejoinsOverTheFrettedNotesOnTheTabStaff()
    {
        static string[] Beams(XDocument doc, string? staff) => doc.Descendants("note")
            .Where(n => (string?)n.Element("staff") == staff)
            .Select(n => string.Join("+", n.Elements("beam").Select(b => b.Value)))
            .ToArray();
        const string music = "/8 8 e,8 a,8 d8 /8 g8 /8 |";
        var both = Export(music, "staff gt\n tab gt");
        Assert.Equal(new[] { "begin", "continue", "continue", "end", "begin", "continue", "continue", "end" },
            Beams(both, "1"));
        Assert.Equal(new[] { "begin", "end", "begin", "end" }, Beams(both, "2"));
        Assert.Equal(new[] { "begin", "end", "begin", "end" }, Beams(Export(music, "tab gt"), null));
        // e, alone of its group (the slash is a gap) is a flag; the rest has no beam.
        Assert.Equal(new[] { "", "" }, Beams(Export("/8 e,8 /4 r2 |", "tab gt"), null));
    }

    /// <summary>On a TAB-only staff too, a slash is a gap, not a note to fret.</summary>
    [Fact]
    public void ASlashNote_OnATabAlone_IsAGap()
    {
        var doc = Export("/2 e,2 |", "tab gt");
        Assert.Equal(new[] { "forward", "note" }, doc.Descendants("measure").First().Elements()
            .Where(e => e.Name == "note" || e.Name == "forward").Select(e => e.Name.LocalName));
        Assert.Empty(doc.Descendants("unpitched"));
    }

    /// <summary>A slash note beside a tab is no note written an octave too low: the tab leaves
    /// it out without LYS5002 (#14), which still warns about a real pitch below the strings.</summary>
    [Fact]
    public void ASlashNote_DrawsNoLys5002()
    {
        static IEnumerable<string> Lys5002(string music)
            => SemanticValidation.Run(SyntaxTree.Parse(
                    "octave absolute\ntime 4/4\npart gt { clef treble_8  tuning guitar }\n"
                    + $"section A {{ gt {{ {music} }} }}\nform main {{ A }}\nscore main {{ staff gt\n tab gt }}"))
                .Where(d => d.Code == "LYS5002").Select(d => d.Message);
        Assert.Empty(Lys5002("/4 4 8 8 4 |"));
        Assert.Single(Lys5002("/4 4 4 c,,4 |"));
    }

    [Fact]
    public void ATabAlone_IsThePartsOnlyStaff()
    {
        var doc = Export("e,4 a, d g |", "tab gt");
        var attrs = doc.Descendants("attributes").First();
        Assert.Null(attrs.Element("staves"));
        Assert.Equal(new[] { "TAB" }, attrs.Elements("clef").Select(c => c.Element("sign")!.Value));
        Assert.Null(attrs.Element("staff-details")!.Attribute("number"));
        Assert.Equal(new[] { "s6f0", "s5f0", "s4f0", "s3f0" }, Frets(doc, null));
    }

    /// <summary>The strings are the PAGE's choice: g written on the fourth string is its fifth
    /// fret, and the same pitch later in the bar stays there (TabResolver's bar-long reuse)
    /// where a string chosen afresh would be the open third.</summary>
    [Fact]
    public void TheStringsAreThePagesChoice()
        => Assert.Equal(new[] { "s4f5", "s4f5" },
            Frets(Export("g4\\4 g4 r2 |", "tab gt"), null));

    /// <summary>A grace note is fretted as the page frets it — from its own pitch and written
    /// string: the a, before the e, is the open fifth string, and a, written \6 is its
    /// fifth fret.</summary>
    [Fact]
    public void AGraceNote_IsFretted()
    {
        var doc = Export("grace { a,16 } e,4 grace { a,16\\6 } a,4 d g |", "tab gt");
        var graces = doc.Descendants("note").Where(n => n.Element("grace") != null)
            .Select(n => $"s{n.Descendants("string").Single().Value}f{n.Descendants("fret").Single().Value}");
        Assert.Equal(new[] { "s5f0", "s6f5" }, graces);
    }

    [Fact]
    public void AChordsNotesTakeOneStringEach()
    {
        var frets = Frets(Export("<e, b, e>1 |", "tab gt"), null);
        Assert.Equal(3, frets.Length);
        Assert.Equal(3, frets.Select(f => f.Split('f')[0]).Distinct().Count());
        Assert.DoesNotContain("-", frets);
    }

    /// <summary>The importer reads the TAB staff beside a notation staff as the copy it is:
    /// the part comes back once, not as a grand staff of itself.</summary>
    [Fact]
    public void Import_DropsTheTabCopy()
    {
        var xml = Export("e,4 a, d g |", "staff gt\n tab gt").ToString();
        var doc = MusicXmlReader.Read(xml, new ImportReport());
        var part = Assert.Single(doc.Parts);
        Assert.Equal(4, part.Measures.SelectMany(m => m.VoiceItems.Values).Sum(v => v.Count(i => i is ImportNote)));
    }
}
