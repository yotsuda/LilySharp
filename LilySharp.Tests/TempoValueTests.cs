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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The tempo value run, read once (docs/VALUE_SITE_AUDIT.md §1.1 A3, HANDOFF's retired ▶ ⒯ (value-site typing, NOT §2 F ⒯)⑴ ⒝).
///
/// Six state machines used to walk this one run — five on the syntax node and a sixth,
/// with its own regex, inside <c>MeasureCollector.CollectTempo</c>. Collapsing them is
/// only safe if the survivor answers what all six answered, so the table below is not a
/// sample of interesting cases: it is one row per RULE any of them had, including the
/// awkward ones (last integer, not first; the '=' with nothing before it; the number
/// after a feel word; a leading feel word that is not a marking).
///
/// ⚠️ These pin what a .lys file MEANS. A row that looks wrong is not a bug to fix here
/// — changing one changes existing scores, and belongs in its own commit.
/// </summary>
[Trait("Category", "Unit")]
public class TempoValueTests
{
    private static TempoValue Read(string source) =>
        SyntaxTree.Parse(source).GetRoot().DescendantNodes()
            .OfType<TempoDeclarationSyntax>().Single().Value;

    [Theory]
    // --- the plain forms ---
    // A lone number is the bpm, NOT a beat unit: reading 140 as a beat unit printed a
    // 140th-note metronome glyph once already, which is why BeatUnit is null with no '='.
    [InlineData("tempo 120", null, null, 0, 120, 0)]
    [InlineData("tempo \"Allegro\"", "Allegro", null, 0, null, 0)]
    [InlineData("tempo \"Allegro\" 120", "Allegro", null, 0, 120, 0)]

    // --- beat unit = bpm ---
    // Bpm is the LAST integer, not the first, because the beat unit comes first here.
    [InlineData("tempo \"Grave\" 4 = 54", "Grave", 4, 0, 54, 0)]
    [InlineData("tempo \"Lively\" 4. = 116", "Lively", 4, 1, 116, 0)]
    [InlineData("tempo 4.. = 116", null, 4, 2, 116, 0)]
    // A bare word is a marking only in the FIRST position, so nothing later is swallowed.
    [InlineData("tempo Comodo 4 = 84", "Comodo", 4, 0, 84, 0)]

    // --- feel words ---
    // A bare feel word swings the eighths; the number after it is the subdivision and
    // must not be read as the tempo.
    [InlineData("tempo 120 swing", null, null, 0, 120, 8)]
    [InlineData("tempo 120 shuffle", null, null, 0, 120, 8)]
    [InlineData("tempo 120 swing 16", null, null, 0, 120, 16)]
    [InlineData("tempo 4 = 120 swing 16", null, 4, 0, 120, 16)]
    // A LEADING feel word is a feel word, not a marking.
    [InlineData("tempo swing", null, null, 0, null, 8)]
    // 'swing 0' named a subdivision and named zero. It is not the bare-word case, and
    // the reading this replaced answered 0 — kept, so the collapse changed nothing.
    [InlineData("tempo 120 swing 0", null, null, 0, 120, 0)]

    // --- the degenerate ones the old machines still had answers for ---
    // '=' with nothing before it is a quarter.
    [InlineData("tempo = 120", null, 4, 0, 120, 0)]
    // '=' with nothing AFTER it: the 4 is still the last integer, so it is also the bpm.
    [InlineData("tempo 4 =", null, 4, 0, 4, 0)]
    public void TheRunReadsAsItAlwaysDid(
        string source, string? marking, int? beatUnit, int beatDots, int? bpm, int swing)
    {
        Assert.Equal(
            new TempoValue(marking, beatUnit, beatDots, bpm, swing),
            Read(source));
    }

    [Fact]
    public void ThePropertiesAgreeWithTheValueTheyDelegateTo()
    {
        // Marking / BeatUnit / BeatDots / Bpm / SwingSubdivision are the published
        // surface (the exporters and the LSP read them); Value is the one reading
        // underneath. If they ever disagree, the six machines are back.
        var tempo = SyntaxTree.Parse("tempo \"Lively\" 4. = 116 swing 16")
            .GetRoot().DescendantNodes().OfType<TempoDeclarationSyntax>().Single();
        var value = tempo.Value;

        Assert.Equal(value.Marking, tempo.Marking);
        Assert.Equal(value.BeatUnit, tempo.BeatUnit);
        Assert.Equal(value.BeatDots, tempo.BeatDots);
        Assert.Equal(value.Bpm, tempo.Bpm);
        Assert.Equal(value.SwingSubdivision, tempo.SwingSubdivision);
    }

    // ---------- the sixth reader, and the disagreement it hid ----------

    private static MultiStaffScore Collect(string body)
    {
        var src =
            body + "\n" +
            "part m { clef treble }\n" +
            "section A { m { c'4 d' e' f' | } }\n" +
            "form main { A }\n" +
            "score main \"s\" { staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Fact]
    public void TheCollectorReadsTheBeatUnitThroughTheSameValueAsEveryoneElse()
    {
        var score = Collect("tempo \"Lively\" 4. = 116");

        Assert.Equal(116, score.Tempo);
        Assert.Equal(4, score.TempoBeatUnit);
        Assert.Equal(1, score.TempoDots);
    }

    [Fact]
    public void AWrittenBeatUnitReplacesTheStandingOneEvenWhenItIsTheDefault()
    {
        // ⚠️ BEHAVIOUR CHANGE, recorded on purpose. The collector's own walk needed a
        // NUMBER token immediately before the '=' and matched nothing here, so the 8
        // from the first tempo stayed standing. TempoValue.BeatUnit says an '=' with
        // no unit before it is a quarter — the rule the syntax node always published —
        // and the collector now agrees with it instead of with itself.
        var score = Collect("tempo 8 = 120\ntempo \"x\" = 90");

        Assert.Equal(90, score.Tempo);
        Assert.Equal(4, score.TempoBeatUnit);
    }

    [Fact]
    public void ATempoWithNoEqualsLeavesTheStandingBeatUnitAlone()
    {
        // The other half of that rule, and the reason BeatUnit is nullable: `tempo 100`
        // changes the speed, not the unit the metronome mark is drawn with.
        var score = Collect("tempo 8 = 120\ntempo 100");

        Assert.Equal(100, score.Tempo);
        Assert.Equal(8, score.TempoBeatUnit);
    }

    // ---------- decimals ----------

    [Fact]
    public void ADecimalInATempoIsRefusedWithoutLosingTheRestOfTheDeclaration()
    {
        // Before the lexer had a decimal literal this read its beat unit as a 5
        // (`4`, `.`, `5` were three tokens and the last integer before '=' won).
        // Taking the token INTO the run and refusing it there keeps the '=' and the
        // 116 inside the tempo, so the message is about the tempo and the source
        // still round-trips.
        var tree = SyntaxTree.Parse("tempo 4.5 = 116");

        var error = Assert.Single(tree.Diagnostics.Where(
            d => d.Code == DiagnosticCodes.FractionalTempoValue));
        Assert.Contains("4.5", error.Message);
        Assert.Equal("tempo 4.5 = 116", tree.ToFullString());

        // The refused token contributes nothing to the reading.
        var value = Read("tempo 4.5 = 116");
        Assert.Equal(4, value.BeatUnit);   // the '=' with no unit before it
        Assert.Equal(116, value.Bpm);
    }
}
