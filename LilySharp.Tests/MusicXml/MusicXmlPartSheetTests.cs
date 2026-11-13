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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// What a part sheet's MusicXML keeps of the score — the section labels, the tempos another
/// part's music states, the marks hung on a phrase call's <c>&lt;&gt;</c> — and the order of a
/// score's parts (grammar evaluation 4, 2026-11-13: a big-band chart's and a chamber score's
/// part sheets; Lab sessions/p1226/md/eval4/bigband r-14).
/// </summary>
public class MusicXmlPartSheetTests
{
    private const string TwoParts = """
        octave absolute
        tempo 120
        part first { clef treble }
        part second { clef treble }
        phrase lick { c''4 d'' e'' f''@fall | }
        section A {
          first  { c''1 | tempo "Slower" 4 = 80 c''1 | }
          second { <>@f@text("tutti").up lick c''1 | }
        }
        section B {
          first  { c''1 | }
          second { g''1 | }
        }
        form { A B }
        score { staff first  staff second }
        score second { staff second }
        """;

    private static XDocument Export(string source, string? score = null)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = score == null ? null : RenderSpecParser.FindAll(tree).Single(s => s.Name == score);
        return new MusicXmlExporter { Score = spec, Form = spec?.Form }.Export(tree).ToXml();
    }

    /// <summary>Each measure's tempos, as "words:count" (either may be missing).</summary>
    private static List<string> Tempos(XElement part) =>
        part.Elements("measure").Select(m => string.Join(",",
            m.Elements("direction").Where(d => d.Element("sound")?.Attribute("tempo") != null)
                .Select(d => d.Descendants("words").FirstOrDefault()?.Value + ":" + d.Element("sound")!.Attribute("tempo")!.Value)))
        .ToList();

    private static List<string> Rehearsals(XElement part) =>
        part.Elements("measure").Select(m => string.Join(",", m.Descendants("rehearsal").Select(r => r.Value))).ToList();

    [Fact]
    public void APartSheet_HasTheSectionLabels_AndTheTemposAnotherPartsMusicStates()
    {
        var sheet = Export(TwoParts, "second").Descendants("part").Single();
        // Until 2026-11-14: no label at all (they went to the book's first part), "Slower" in
        // the opening bar in place of the file's 120, and no return at B.
        Assert.Equal(new[] { "A", "", "B" }, Rehearsals(sheet));
        Assert.Equal(new[] { ":120", "Slower:80", ":120" }, Tempos(sheet));
    }

    [Fact]
    public void EveryPartOpensAtThePiecesTempo_AndTheReturnIsWrittenOnce()
    {
        var parts = Export(TwoParts).Descendants("part").ToList();
        Assert.Equal(new[] { ":120", "Slower:80", ":120" }, Tempos(parts[0]));
        // The second part opens at 120 (it opened at the 80 the first part's music had left
        // running), and repeats neither the change nor the return.
        Assert.Equal(new[] { ":120", "", "" }, Tempos(parts[1]));
    }

    [Fact]
    public void ALaterPartOfThePlay_DoesNotAnswerTheFirstPartsNewTempoAtTheSectionsHead()
    {
        var parts = Export("""
            octave absolute
            tempo 120
            part first { clef treble }
            part second { clef treble }
            section A { first { c''1 | } second { c''1 | } }
            section B { first { c''1 | tempo 4 = 80 c''1 | } second { c''1 | c''1 | } }
            section C { first { c''1 | } second { c''1 | } }
            form { A B C }
            score { staff first  staff second }
            """).Descendants("part").ToList();
        Assert.Equal(new[] { ":120", "", ":80", ":120" }, Tempos(parts[0]));
        Assert.Equal(new[] { ":120", "", "", "" }, Tempos(parts[1]));
    }

    [Fact]
    public void MarksOnAnEmptyChord_StandAtItsMoment()
    {
        var sheet = Export(TwoParts, "second").Descendants("part").Single();
        var opening = sheet.Elements("measure").First();
        // `<>@f@text("tutti").up lick`: the documented way to mark a phrase call.
        Assert.Single(opening.Descendants("dynamics"), d => d.Element("f") != null);
        Assert.Single(opening.Descendants("words"), w => w.Value == "tutti");
    }

    [Fact]
    public void TheParts_AreInTheScoresOrder()
    {
        const string book = """
            part a "Alpha" { clef treble }
            part b "Beta" { clef treble }
            part c "Gamma" { clef treble }
            section A { a { c'1 | } b { c'1 | } c { c'1 | } }
            form { A }
            score two { staff c  staff a }
            score all { staff b  staff c  staff a }
            """;
        static List<string> Names(XDocument doc) => doc.Descendants("part-name").Select(n => n.Value).ToList();
        Assert.Equal(new[] { "Gamma", "Alpha" }, Names(Export(book, "two")));
        Assert.Equal(new[] { "Beta", "Gamma", "Alpha" }, Names(Export(book, "all")));
        // No score named: the book's first score's staves from the top, then the part it does
        // not engrave (which has no label there).
        Assert.Equal(new[] { "Gamma", "Alpha", "b" }, Names(Export(book)));
    }

    [Fact]
    public void TheJazzMarks_TheBreathingSigns_TheStrokes_AndTheFeatheredBeam_AreWritten()
    {
        var doc = Export("""
            octave absolute
            part m { clef treble }
            section A {
              m {
                c''4@fall d''@doit e''@breath f''@caesura |
                c''4@upBow d''@downBow [ e''16@feather(right) f'' g'' a'' ] |
              }
            }
            form { A }
            score { staff m }
            """);
        foreach (var name in new[] { "falloff", "doit", "breath-mark", "caesura" })
            Assert.Single(doc.Descendants("articulations").Elements(name));
        Assert.Single(doc.Descendants("technical").Elements("up-bow"));
        Assert.Single(doc.Descendants("technical").Elements("down-bow"));
        var fanned = Assert.Single(doc.Descendants("beam"), b => b.Attribute("fan") != null);
        Assert.Equal(("accel", "1", "begin"), (fanned.Attribute("fan")!.Value, fanned.Attribute("number")!.Value, fanned.Value));
    }

    [Fact]
    public void TheJazzMarksAndTheBreathingSigns_ComeBackFromAFile()
    {
        var xml = Export("""
            octave absolute
            part m { clef treble }
            section A { m { c''4@fall d''@doit e''@breath f''@caesura | c''4@scoop d''@plop e'' f'' | } }
            form { A }
            score { staff m }
            """).ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        foreach (var mark in new[] { "@fall", "@doit", "@breath", "@caesura", "@scoop", "@plop" })
            Assert.Contains(mark, lys);
    }
}
