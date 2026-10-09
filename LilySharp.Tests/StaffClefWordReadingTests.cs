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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>staff X Y</c> / <c>ossia X Y</c> where X is a clef word: a clef over part Y — unless the
/// file declares a part named X, which then wins and makes Y a part played to MIDI only
/// (2026-10-09, the reading the tab item has had since 2026-10-03). Until then
/// <c>staff bass click</c> beside <c>part bass</c> drew part click on a bass clef, dropped part
/// bass, and stopped playing click as MIDI-only, with no diagnostic.
/// </summary>
/// <remarks>
/// Poison: make <c>StaffTargets.FirstIsADeclaredPart</c> answer false ⇒ the declared-part tests
/// go red (the page draws click, references miss click, no LYS1046).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class StaffClefWordReadingTests
{
    private static string Book(string parts, string cells, string item) =>
        parts + "\nsection A { " + cells + " }\nform { A }\nscore { " + item + " }\n";

    private const string BassAndClick = "part bass { clef bass }\npart click { clef percussion }";
    private const string BassAndClickCells = "bass { c4 d e f | } click { c4 c c c | }";

    private static RenderSpec Spec(string source) => RenderSpecParser.FindFirst(SyntaxTree.Parse(source))!;

    /// <summary>The label and the <c>as</c> selectors come from the same reading as the part
    /// (session 871: <c>StaffTargets</c> draws the one boundary at the first <c>as</c>; the
    /// renderer and the reference finder kept token lists of their own until then).</summary>
    [Theory]
    [InlineData("staff ~bass click \"Bass\" as removeEmpty all lines 1", true, 1, true, true)]
    [InlineData("staff bass as lines 2", false, 2, false, false)]
    [InlineData("staff bass as removeEmpty true", false, 5, true, false)]
    [InlineData("staff bass", false, 5, false, false)]
    public void TheLabelAndTheSelectors_AreTheNodesReading(
        string item, bool suppressed, int lines, bool removeEmpty, bool removeFirst)
    {
        var tree = SyntaxTree.Parse(Book(BassAndClick, BassAndClickCells, item));
        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(RenderSpecParser.FindFirst(tree)!.Items)).Staff;

        Assert.Equal("bass", staff.VoiceName);
        Assert.Equal(suppressed, staff.NameSuppressed);
        Assert.Equal(lines, staff.Lines);
        Assert.Equal(removeEmpty, staff.RemoveEmpty);
        Assert.Equal(removeFirst, staff.RemoveFirst);
        var node = tree.GetRoot().DescendantNodes().OfType<StaffRenderSyntax>().Single();
        Assert.Equal("bass", PartReferenceFinder.StaffPartToken(node)?.Text);
    }

    [Fact]
    public void TheDisplayString_IsTheNodesReading()
    {
        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(
            Spec(Book(BassAndClick, BassAndClickCells, "staff bass \"Low\" as lines 1")).Items)).Staff;
        Assert.Equal("Low", staff.InstrumentName);
        Assert.Equal(1, staff.Lines);
    }

    [Fact]
    public void ADeclaredPartNamedForAClef_IsThePart_AndTheNextWordPlaysToMidiOnly()
    {
        var spec = Spec(Book(BassAndClick, BassAndClickCells, "staff bass click"));

        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(spec.Items)).Staff;
        Assert.Equal("bass", staff.VoiceName);
        Assert.Null(staff.WrittenClef);
        Assert.Contains("click", spec.MidiOnlyParts);
        Assert.Contains("click", spec.SoundingPartNames);
    }

    [Fact]
    public void ItWarns_SayingHowToWriteEitherMeaning()
    {
        var tree = SyntaxTree.Parse(Book(BassAndClick, BassAndClickCells, "staff bass click"));

        var w = Assert.Single(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.StaffPartNameIsAlsoAClef);
        Assert.Contains("write 'click' before the staff", w.Message, System.StringComparison.Ordinal);
        Assert.Contains("'clef bass' in part 'click'", w.Message, System.StringComparison.Ordinal);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(Book(BassAndClick, BassAndClickCells, "click staff bass")))
            .Where(d => d.Code == DiagnosticCodes.StaffPartNameIsAlsoAClef));
    }

    [Fact]
    public void TheMidiOnlyWord_IsAReferenceToItsPart()
    {
        string source = Book(BassAndClick, BassAndClickCells, "staff bass click");
        var tree = SyntaxTree.Parse(source);
        var staff = tree.GetRoot().DescendantNodes<StaffRenderSyntax>().Single();

        Assert.Equal("click", staff.MidiOnlyPartToken?.Text);
        Assert.Equal("bass", PartReferenceFinder.StaffPartToken(staff)?.Text);
    }

    [Theory]
    [InlineData("staff bass click")]   // no part named bass: the clef over part click
    [InlineData("staff bass  click")]
    public void WithNoPartOfThatName_TheClefReadingStands(string item)
    {
        var tree = SyntaxTree.Parse(Book("part click { clef treble }", "click { c'4 d' e' f' | }", item));
        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(RenderSpecParser.FindFirst(tree)!.Items)).Staff;

        Assert.Equal("click", staff.VoiceName);
        Assert.Equal(ClefType.Bass, staff.WrittenClef);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Code == DiagnosticCodes.StaffPartNameIsAlsoAClef));
    }

    [Fact]
    public void TheSameWordTwice_IsTheClefOverThatPart()
    {
        var spec = Spec(Book("part bass { clef treble }", "bass { c4 d e f | }", "staff bass bass"));
        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(spec.Items)).Staff;

        Assert.Equal("bass", staff.VoiceName);
        Assert.Equal(ClefType.Bass, staff.WrittenClef);
        Assert.Empty(spec.MidiOnlyParts);
    }

    [Fact]
    public void AnOssia_ReadsTheSameWay()
    {
        var spec = Spec(Book(BassAndClick, BassAndClickCells, "staff click  ossia bass click"));

        var ossia = Assert.IsType<OssiaStaffSpec>(spec.Items.Last());
        Assert.Equal("bass", ossia.Staff.VoiceName);
        Assert.Contains("click", spec.MidiOnlyParts);
    }
}
