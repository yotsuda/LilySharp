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
using LilySharp.Core.LilyPond;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An empty <c>| |</c> bar is one bar of silence (owner's decision 2026-08-28) to EVERY
/// reader of the sentence. The page and the MIDI learned it that day
/// (EmptyMeasureValidatorTests); the MusicXML exporter and the LilyPond twin had not: the
/// twin copied the bare bars, which are bar CHECKS to LilyPond, so <c>c'1 | | e'1</c> was
/// two bars there and three on the page, and <c>partial 4 | c'4 …</c> failed the bar check;
/// the MusicXML walk reused the empty measure and wrote no bar at all, and pulled the
/// <c>c'4</c> into the pickup (MEASURED 2026-09-09, scratch/p358/midi: gap.lys, mp-open,
/// mp-mid). The pairs below are the same identity the MIDI tests carry — the bare spelling
/// against the <c>s1</c> the author would type — so the four readers cannot drift apart.
/// </summary>
[Trait("Category", "Unit")]
public class EmptyBarExportTests
{
    private const string OneStaff =
        "octave absolute\ntime 4/4\npart m {{ }}\nsection A {{ m {{ {0} }} }}\n"
        + "form main {{ ~A }}\nscore main {{ staff m }}";

    private const string ThreeFour =
        "octave absolute\ntime 3/4\npart m {{ }}\nsection A {{ m {{ {0} }} }}\n"
        + "form main {{ ~A }}\nscore main {{ staff m }}";

    private const string TwoStaves =
        "octave absolute\ntime 4/4\npart up {{ clef treble }}\npart dn {{ clef bass }}\n"
        + "section A {{ up {{ {0} }} dn {{ c1 | g1 | c1 }} }}\n"
        + "form main {{ ~A }}\nscore main {{ staffGroup {{ staff up staff dn }} }}";

    private static string Twin(string source) => new LilyPondExporter().Export(SyntaxTree.Parse(source));

    private static string Xml(string source) => new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToString();

    // ===================== the twin =====================

    [Theory]
    [InlineData("c'1 | | e'1", "c'1 | s1 | e'1")]
    [InlineData("| | c'4 c' g' g' | a' a' g'2", "s1 | s1 | c'4 c' g' g' | a' a' g'2")]
    [InlineData("c'4 c' g' g' | | | a' a' g'2", "c'4 c' g' g' | s1 | s1 | a' a' g'2")]
    [InlineData("c'1 | || | e'1", "c'1 | || s1 | e'1")]           // `||` decorates; the bare `|` after it opens a bar
    [InlineData("partial 4 | c'4 c' g' g' | a'1", "partial 4 s4 | c'4 c' g' g' | a'1")]
    [InlineData("c'1 | partial 4 | c'4 c' g' g' | a'1", "c'1 | partial 4 s4 | c'4 c' g' g' | a'1")]
    [InlineData("partial 4 g'4 | | c'1", "partial 4 g'4 | s1 | c'1")]  // the pickup closed; a gap is the meter again
    public void TheTwin_WritesTheSpacerAnEmptyBarStandsFor(string bare, string spelled)
        => Assert.Equal(Twin(string.Format(OneStaff, spelled)), Twin(string.Format(OneStaff, bare)));

    [Fact]
    public void TheTwin_WritesTheMeter_NotAWholeNote()
        => Assert.Equal(
            Twin(string.Format(ThreeFour, "c'2. | s2. | e'2.")),
            Twin(string.Format(ThreeFour, "c'2. | | e'2.")));

    [Fact]
    public void TheTwin_SpacerIsSpelledAsTheAuthorWould()
    {
        // Not only equal to the spelled twin — readable as LilyPond: `s1 |`, not `s1*4/4 |`.
        Assert.Contains("s1 |", Twin(string.Format(OneStaff, "c'1 | | e'1")));
        Assert.Contains("s2. |", Twin(string.Format(ThreeFour, "c'2. | | e'2.")));
        Assert.Contains("\\partial 4 s4 |", Twin(string.Format(OneStaff, "partial 4 | c'4 c' g' g' | a'1")));
    }

    [Theory]
    [InlineData("c'1 |: c'4 d e f :|")]      // ONE bar line doing both jobs — no gap
    [InlineData("|: c'4 d e f :|")]           // a leading `|:` closes nothing
    [InlineData("c'1 | || c'4 d e f")]        // `||` decorates the boundary
    [InlineData("c'1 | |. ")]                 // …and so does the final bar line
    public void TheTwin_InventsNoSpacer(string music)
        => Assert.DoesNotContain("s1", Twin(string.Format(OneStaff, music)));

    [Fact]
    public void TheTwin_ARepeatOpenerAfterAWrittenBar_IsAnEmptyBarToo()
        // The page's LeadingBareThenRepeatOpener / RepeatOpenerAfterAWrittenBar rules.
        => Assert.Equal(
            Twin(string.Format(OneStaff, "c'1 | s1 |: c'4 d e f :|")),
            Twin(string.Format(OneStaff, "c'1 | |: c'4 d e f :|")));

    // ===================== MusicXML =====================

    [Theory]
    [InlineData("c'1 | | e'1", "c'1 | s1 | e'1")]
    [InlineData("| | c'4 c' g' g' | a' a' g'2", "s1 | s1 | c'4 c' g' g' | a' a' g'2")]
    [InlineData("partial 4 | c'4 c' g' g' | a'1", "partial 4 s4 | c'4 c' g' g' | a'1")]
    [InlineData("c'1 | partial 4 | c'4 c' g' g' | a'1", "c'1 | partial 4 s4 | c'4 c' g' g' | a'1")]
    [InlineData("partial 4 g'4 | | c'1", "partial 4 g'4 | s1 | c'1")]
    [InlineData("c'1 | |: c'4 d e f :|", "c'1 | s1 |: c'4 d e f :|")]
    public void TheMusicXml_WritesTheBarAnEmptyBarStandsFor(string bare, string spelled)
        => Assert.Equal(Xml(string.Format(OneStaff, spelled)), Xml(string.Format(OneStaff, bare)));

    [Fact]
    public void TheMusicXml_TakesTheMeter()
        => Assert.Equal(
            Xml(string.Format(ThreeFour, "c'2. | s2. | e'2.")),
            Xml(string.Format(ThreeFour, "c'2. | | e'2.")));

    [Fact]
    public void TheMusicXml_KeepsTheOtherPartsInStep()
    {
        // The defect in the shape the owner would see it: an empty bar in ONE part dropped
        // that part's measure, so its third bar stood beside the other part's second.
        var doc = new MusicXmlExporter().Export(SyntaxTree.Parse(string.Format(TwoStaves, "c'1 | | e'1")));
        Assert.All(doc.Parts, p => Assert.Equal(3, p.Measures.Count));
        Assert.Equal(
            Xml(string.Format(TwoStaves, "c'1 | s1 | e'1")),
            Xml(string.Format(TwoStaves, "c'1 | | e'1")));
    }

    [Theory]
    [InlineData("c'1 |: c'4 d e f :|", 2)]    // ONE bar line doing both jobs — no gap
    [InlineData("|: c'4 d e f :|", 1)]         // a leading `|:` closes nothing
    [InlineData("c'1 | || c'4 d e f", 2)]      // `||` decorates the boundary
    public void TheMusicXml_InventsNoBar(string music, int measures)
    {
        var doc = new MusicXmlExporter().Export(SyntaxTree.Parse(string.Format(OneStaff, music)));
        Assert.Equal(measures, doc.Parts.Single().Measures.Count);
    }
}
