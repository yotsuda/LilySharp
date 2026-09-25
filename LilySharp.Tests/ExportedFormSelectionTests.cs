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
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A book of several movements, and the three one-arrangement formats that have to be told
/// which one: MIDI, MusicXML and the LilyPond twin all write ONE <c>form</c> per file.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE SECOND MOVEMENT WAS UNREACHABLE UNTIL 2026-08-17. The page has had
/// <c>--score</c> / <c>--all</c> / <c>--combined</c> since long before; these three took no
/// selector at all, so `lysc midi` on the tree's one three-movement book wrote the first
/// form's 40 notes and said nothing about the other two (decided, HANDOFF §3: warn, and add
/// the two doors).
/// </para>
/// <para>
/// ⚠️ THE FALSIFIER IS "THE OTHER MOVEMENT'S NOTES", not "some output appeared". A selector
/// that is read and then ignored produces a perfectly good file of the WRONG music, which is
/// what every one of these formats did already — so each case here names the pitches (or the
/// measure count) that only its own movement has.
/// </para>
/// </remarks>
public class ExportedFormSelectionTests
{
    // Two movements that cannot be mistaken for each other: four Cs against three Gs.
    // ⚠️ Written WITHOUT octave marks on purpose. `c'4 c' c' c'` is not four C5s — in
    // relative mode each `'` counts from the nearest c and the line climbs an octave a note
    // (HANDOFF §2F ⒡, where two fixtures do it by accident). A control has to be the pitch
    // it looks like.
    private const string TwoMovements = """
        part m { clef treble }
        section First  { m { c4 c c c | } }
        section Second { m { g2 g | g1 | } }
        form main   { First }
        form encore { Second }
        score main "first" { staff m }
        score encore "second" { staff m }
        """;

    private static FormDeclarationSyntax FormNamed(SyntaxTree tree, string name)
        => ScoreForms.All(tree.GetRoot()).Single(f => f.NameText == name);

    [Fact]
    public void Midi_PlaysThePrimaryFormByDefaultAndTheNamedOneWhenAsked()
    {
        var tree = SyntaxTree.Parse(TwoMovements);

        var first = new MidiExporter().Export(tree)
            .Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch).ToArray();
        var second = new MidiExporter { Form = FormNamed(tree, "encore") }.Export(tree)
            .Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch).ToArray();

        // Four C4s against three G3s: a bare `g` opening a section reads from the part's
        // anchor (C4) and the NEAREST g is the one below it, which is the language's rule
        // and not a slip in the fixture.
        Assert.Equal(new[] { 60, 60, 60, 60 }, first);
        Assert.Equal(new[] { 55, 55, 55 }, second);
    }

    [Fact]
    public void MusicXml_WritesThePrimaryFormByDefaultAndTheNamedOneWhenAsked()
    {
        var tree = SyntaxTree.Parse(TwoMovements);

        static string[] Steps(MusicXmlExporter exporter, SyntaxTree t)
            => exporter.Export(t).Parts
                .SelectMany(p => p.Measures).SelectMany(m => m.Notes)
                .Where(n => n.Step != null).Select(n => n.Step!).ToArray();

        Assert.Equal(new[] { "C", "C", "C", "C" }, Steps(new MusicXmlExporter(), tree));
        Assert.Equal(
            new[] { "G", "G", "G" },
            Steps(new MusicXmlExporter { Form = FormNamed(tree, "encore") }, tree));
    }

    [Fact]
    public void LilyPondTwin_WritesThePrimaryFormByDefaultAndTheNamedOneWhenAsked()
    {
        var tree = SyntaxTree.Parse(TwoMovements);

        string first = new LilyPondExporter().Export(tree);
        string second = new LilyPondExporter { Form = FormNamed(tree, "encore") }.Export(tree);

        // The twin spells pitches, so the movements are told apart by what they contain and
        // by what they must NOT: an unfiltered export would carry both.
        Assert.Contains("c4 c c c", first);
        Assert.DoesNotContain("g2 g", first);
        Assert.Contains("g2 g", second);
        Assert.DoesNotContain("c4 c c c", second);
    }

    /// <summary>
    /// Two scores on ONE form — the tab corpus's shape, <c>score main</c> beside
    /// <c>score main "tab"</c> — are told apart by their staves, and the twin engraves the
    /// score it is handed.
    /// </summary>
    /// <remarks>
    /// ⚠️ The form cannot tell them apart, and until 2026-09-25 the twin read the file's first
    /// <c>score</c> whatever it was asked for: <c>lysc ly --all</c> wrote the staff-only
    /// twin under the tab score's name too (HANDOFF §2 T7). The falsifier is the OTHER
    /// score's staff: the tab twin must have no <c>\new Staff</c>, the default no TabStaff.
    /// </remarks>
    [Fact]
    public void LilyPondTwin_EngravesTheStavesOfTheScoreItIsHanded()
    {
        var tree = SyntaxTree.Parse("""
            part b { clef bass tuning bass }
            section A { b { e,4 a, d g | } }
            form main { A }
            score main { staff b }
            score main "tab" { tab b }
            """);
        var tab = Core.Svg.Collector.RenderSpecParser.FindDeclaredByName(tree, "tab")!.Value;

        string staffTwin = new LilyPondExporter().Export(tree);
        string tabTwin = new LilyPondExporter { Form = tab.Spec.Form, Score = tab.Declaration }.Export(tree);

        Assert.Contains(@"\new Staff", staffTwin);
        Assert.DoesNotContain("TabStaff", staffTwin);
        Assert.Contains("TabStaff", tabTwin);
        Assert.DoesNotContain(@"\new Staff", tabTwin);
    }

    /// <summary>A score handed without a form writes its OWN form, not the primary one.</summary>
    [Fact]
    public void LilyPondTwin_AScoreHandedAloneWritesItsOwnForm()
    {
        var tree = SyntaxTree.Parse(TwoMovements);
        var encore = Core.Svg.Collector.RenderSpecParser.FindDeclaredByName(tree, "second")!.Value;

        string twin = new LilyPondExporter { Score = encore.Declaration }.Export(tree);

        Assert.Contains("g2 g", twin);
        Assert.DoesNotContain("c4 c c c", twin);
    }

    /// <summary>
    /// The default is one reading for all three, so a file whose only form is not named
    /// <c>main</c> is still the one they write.
    /// </summary>
    /// <remarks>
    /// ⚠️ The three used to spell this themselves and had already drifted: MIDI and MusicXML
    /// compared the name ordinally, the twin case-insensitively (<see cref="ScoreForms"/>).
    /// </remarks>
    [Fact]
    public void ASoleFormIsThePrimaryOneWhateverItIsCalled()
    {
        var tree = SyntaxTree.Parse("""
            part m { clef treble }
            section Only { m { e4 | } }
            form finale { Only }
            score finale { staff m }
            """);

        Assert.Equal("finale", ScoreForms.Primary(tree.GetRoot())!.NameText);
        Assert.Equal(
            new[] { 64 },
            new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes)
                .Select(n => n.Pitch).ToArray());
    }
}
