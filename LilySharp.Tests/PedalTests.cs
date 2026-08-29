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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class PedalTests
{
    // --- PedalType enum ---

    [Fact]
    public void PedalType_HasExpectedValues()
    {
        Assert.Equal(0, (int)PedalType.Sustain);
        Assert.Equal(1, (int)PedalType.Sostenuto);
        Assert.Equal(2, (int)PedalType.UnaCorda);
    }

    // --- MusicMarkType pedal entries ---

    // One name per pedal, opened by the name and closed by '@!' — case-insensitive like
    // every other annotation name. '@treCorde' is the one release with a name of its own,
    // kept because it is a WORD the Text style prints (session 289, user decision).
    [Theory]
    [InlineData("sustain", MusicMarkType.SustainOn)]
    [InlineData("sostenuto", MusicMarkType.SostenutoOn)]
    [InlineData("unaCorda", MusicMarkType.UnaCordaOn)]
    [InlineData("treCorde", MusicMarkType.UnaCordaOff)]
    [InlineData("SUSTAIN", MusicMarkType.SustainOn)]
    [InlineData("UNACORDA", MusicMarkType.UnaCordaOn)]
    public void ParseMarkName_PedalMarks(string name, MusicMarkType expected)
    {
        var result = MusicMarkItem.ParseMarkName(name);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>
    /// Every pedal ends with '@!' and its OWN name — a una corda does not release a sustain.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS LILYPOND'S MODEL SAID ONCE. ly/spanners-init.ly:94-101 spells six commands,
    /// but each is <c>make-span-event</c> of ONE event with a direction:
    /// <c>sustainOn = #(make-span-event 'SustainEvent START)</c>. The 'On'/'Off' suffix was
    /// how the surface spelt that direction, and this language already spells it — the '!'.
    /// </remarks>
    [Theory]
    [InlineData("sustain", MusicMarkType.SustainOff)]
    [InlineData("sostenuto", MusicMarkType.SostenutoOff)]
    [InlineData("unaCorda", MusicMarkType.UnaCordaOff)]
    public void ParseSpanEndName_PedalMarks(string name, MusicMarkType expected)
        => Assert.Equal(expected, MusicMarkItem.ParseSpanEndName(name));

    /// <summary>
    /// '@treCorde' and '@!unaCorda' are the SAME mark — two spellings, one answer, exactly as
    /// '@!rit' and '@!textSpan' are. The word is kept because the Text style prints it.
    /// </summary>
    [Fact]
    public void TreCorde_IsSugarForTheUnaCordaTerminator()
        => Assert.Equal(
            MusicMarkItem.ParseSpanEndName("unaCorda"),
            MusicMarkItem.ParseMarkName("treCorde"));

    // ONE spelling per pedal (grammar audit B-5). The short forms and the argument
    // spellings are rejected, not silently mapped: a pedal event carries only START/STOP,
    // so there was never an argument to put a state in ('@ped(off)'), and the
    // noun-continuation spellings ('@una(corda)') used the same parentheses for something
    // else entirely.
    // ⚠️ 'sustainOn' / 'sustainOff' / 'sostenutoOn' / 'sostenutoOff' JOINED THIS LIST in
    // session 289: the direction moved out of the name and into the '!', so they are two
    // names for one span and the second is gone. '@ped' was weighed again at the same time
    // and refused again — LilyPond has no \ped, all three of these are pedals so 'ped'
    // names a category where its siblings name mechanisms, and "Ped." is not printed at all
    // in the default Bracket style.
    [Theory]
    [InlineData("ped")]
    [InlineData("ped.off")]
    [InlineData("sost")]
    [InlineData("sost.off")]
    [InlineData("una.corda")]
    [InlineData("tre.corde")]
    [InlineData("sustainOn")]
    [InlineData("sustainOff")]
    [InlineData("sostenutoOn")]
    [InlineData("sostenutoOff")]
    [InlineData("sustain.off")]
    [InlineData("sostenuto.off")]
    [InlineData("trillspan.start")]
    [InlineData("trillspan.stop")]
    public void ParseMarkName_RemovedSpellings_AreUnknown(string name)
    {
        Assert.Null(MusicMarkItem.ParseMarkName(name));
    }

    // --- MusicMarkItem text for pedals ---

    [Theory]
    [InlineData(MusicMarkType.SustainOn, "Ped.")]
    [InlineData(MusicMarkType.SustainOff, "*")]
    [InlineData(MusicMarkType.SostenutoOn, "Sost. Ped.")]
    [InlineData(MusicMarkType.SostenutoOff, "*")]
    [InlineData(MusicMarkType.UnaCordaOn, "una corda")]
    [InlineData(MusicMarkType.UnaCordaOff, "tre corde")]
    public void MusicMarkItem_PedalText(MusicMarkType type, string expectedText)
    {
        var item = new MusicMarkItem(type, 0, 0);
        Assert.Equal(expectedText, item.Text);
    }

    // --- Pedal marks position and vertical ---

    [Theory]
    [InlineData(MusicMarkType.SustainOn)]
    [InlineData(MusicMarkType.SustainOff)]
    [InlineData(MusicMarkType.SostenutoOn)]
    [InlineData(MusicMarkType.SostenutoOff)]
    [InlineData(MusicMarkType.UnaCordaOn)]
    [InlineData(MusicMarkType.UnaCordaOff)]
    public void MusicMarkItem_PedalPosition_Beginning(MusicMarkType type)
    {
        var item = new MusicMarkItem(type, 0, 0);
        Assert.Equal(MusicMarkPosition.Beginning, item.Position);
    }

    [Theory]
    [InlineData(MusicMarkType.SustainOn)]
    [InlineData(MusicMarkType.SustainOff)]
    [InlineData(MusicMarkType.SostenutoOn)]
    [InlineData(MusicMarkType.SostenutoOff)]
    [InlineData(MusicMarkType.UnaCordaOn)]
    [InlineData(MusicMarkType.UnaCordaOff)]
    public void MusicMarkItem_PedalVertical_Below(MusicMarkType type)
    {
        var item = new MusicMarkItem(type, 0, 0);
        Assert.Equal(MusicMarkVertical.Below, item.Vertical);
    }

    // --- PedalBracketItem ---

    [Fact]
    public void PedalBracketItem_StoresValues()
    {
        var item = new PedalBracketItem(PedalType.Sustain, 0, 3, 42);
        Assert.Equal(PedalType.Sustain, item.Type);
        Assert.Equal(0, item.StartMeasureIndex);
        Assert.Equal(3, item.EndMeasureIndex);
        Assert.Equal(42, item.SourcePosition);
    }

    // --- DetectPedalBrackets ---

    [Fact]
    public void DetectPedalBrackets_EmptyMarks_ReturnsEmpty()
    {
        var result = PedalEngraver.DetectPedalBrackets(ImmutableArray<MusicMarkItem>.Empty);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectPedalBrackets_SustainOnOff_CreatesBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOn, 0, 10),
            new MusicMarkItem(MusicMarkType.SustainOff, 3, 20));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.Single(result);
        Assert.Equal(PedalType.Sustain, result[0].Type);
        Assert.Equal(0, result[0].StartMeasureIndex);
        Assert.Equal(3, result[0].EndMeasureIndex);
        Assert.Equal(10, result[0].SourcePosition);
    }

    [Fact]
    public void DetectPedalBrackets_SostenutoOnOff_CreatesBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SostenutoOn, 1, 10),
            new MusicMarkItem(MusicMarkType.SostenutoOff, 4, 20));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.Single(result);
        Assert.Equal(PedalType.Sostenuto, result[0].Type);
        Assert.Equal(1, result[0].StartMeasureIndex);
        Assert.Equal(4, result[0].EndMeasureIndex);
    }

    [Fact]
    public void DetectPedalBrackets_UnaCordaOnOff_CreatesBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.UnaCordaOn, 2, 10),
            new MusicMarkItem(MusicMarkType.UnaCordaOff, 5, 20));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.Single(result);
        Assert.Equal(PedalType.UnaCorda, result[0].Type);
    }

    [Fact]
    public void DetectPedalBrackets_OnWithoutOff_NoBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOn, 0, 10));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectPedalBrackets_OffWithoutOn_NoBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOff, 3, 20));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectPedalBrackets_ConsecutiveOnOn_EndsPreviousBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOn, 0, 10),
            new MusicMarkItem(MusicMarkType.SustainOn, 2, 20),
            new MusicMarkItem(MusicMarkType.SustainOff, 4, 30));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.Equal(2, result.Length);
        // First bracket: measure 0→2
        Assert.Equal(0, result[0].StartMeasureIndex);
        Assert.Equal(2, result[0].EndMeasureIndex);
        // Second bracket: measure 2→4
        Assert.Equal(2, result[1].StartMeasureIndex);
        Assert.Equal(4, result[1].EndMeasureIndex);
    }

    [Fact]
    public void DetectPedalBrackets_MultiplePedalTypes_Independent()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOn, 0, 10),
            new MusicMarkItem(MusicMarkType.UnaCordaOn, 1, 20),
            new MusicMarkItem(MusicMarkType.SustainOff, 3, 30),
            new MusicMarkItem(MusicMarkType.UnaCordaOff, 4, 40));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.Equal(2, result.Length);
        // Sustain bracket
        var sustain = result.First(b => b.Type == PedalType.Sustain);
        Assert.Equal(0, sustain.StartMeasureIndex);
        Assert.Equal(3, sustain.EndMeasureIndex);
        // Una corda bracket
        var unaCorda = result.First(b => b.Type == PedalType.UnaCorda);
        Assert.Equal(1, unaCorda.StartMeasureIndex);
        Assert.Equal(4, unaCorda.EndMeasureIndex);
    }

    // --- PedalEngraver.Calculate ---

    private static ImmutableArray<MeasureLayout> CreateMeasureLayouts(int count, double measureWidth = 20.0)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureLayout>(count);
        for (int i = 0; i < count; i++)
        {
            var items = ImmutableArray.Create(
                new ItemLayout(0, 1.0, 2.0),
                new ItemLayout(1, 5.0, 2.0));
            builder.Add(new MeasureLayout(i, i * measureWidth, measureWidth, items));
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<SystemLayout> CreateSingleSystem(int measureCount)
    {
        var measures = CreateMeasureLayouts(measureCount);
        return ImmutableArray.Create(new SystemLayout(0, 10.0, 200.0, 5.0, measures));
    }

    [Fact]
    public void Calculate_BasicBracket_ReturnsLayout()
    {
        var systems = CreateSingleSystem(4);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 2, 42));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.Single(result);
        Assert.Equal(42, result[0].SourcePosition);
    }

    [Fact]
    public void Calculate_BracketStartX_LessThanEndX()
    {
        var systems = CreateSingleSystem(4);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 2, 0));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.Single(result);
        Assert.True(result[0].StartX < result[0].EndX,
            $"StartX ({result[0].StartX:F2}) should be less than EndX ({result[0].EndX:F2})");
    }

    [Fact]
    public void Calculate_BracketY_BelowStaff()
    {
        var systems = CreateSingleSystem(4);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 2, 0));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.Single(result);
        // Staff bottom is at 4.0 staff spaces, bracket should be below
        Assert.True(result[0].Y > 4.0, $"Y ({result[0].Y:F2}) should be below staff bottom (4.0)");
    }

    [Fact]
    public void Calculate_EdgeHeight_Positive()
    {
        var systems = CreateSingleSystem(4);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 2, 0));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.Single(result);
        Assert.True(result[0].EdgeHeight > 0, "EdgeHeight should be positive");
    }

    [Fact]
    public void Calculate_EmptyBrackets_ReturnsEmpty()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();

        var result = PedalEngraver.Calculate(
            ImmutableArray<PedalBracketItem>.Empty, systems, ml);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_OutOfRangeMeasure_Skipped()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 10, 0));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_MultipleBrackets_AllReturned()
    {
        var systems = CreateSingleSystem(6);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 2, 10),
            new PedalBracketItem(PedalType.Sustain, 3, 5, 20));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.Equal(2, result.Length);
        Assert.Equal(10, result[0].SourcePosition);
        Assert.Equal(20, result[1].SourcePosition);
    }

    // --- WHICH PEDAL FAMILY LANDS NEAREST THE STAFF (session 206) ---

    /// <summary>All three pedals struck together, printed as text.</summary>
    private const string ThreePedalsAtOnce = """
        part pf {
          clef bass
          pedal text
          section A { c1@sustain@sostenuto@unaCorda | c1@!sustain@!sostenuto@treCorde }
        }

        form main { A }

        score main { staff pf }
        """;

    /// <summary>
    /// ⚠️ THREE SEPARATE GROBS, ALL AT outside-staff-priority 1000, so priority does not order
    /// them — only measurement does. Until 2026-08-18 una corda was ranked OUTERMOST, which
    /// <c>PedalFamilyRank</c>'s own comment called a guess and marked as one; LilyPond puts it
    /// NEAREST, so on a three-pedal book every family sat one row wrong.
    /// </summary>
    /// <remarks>
    /// MEASURED: audit/lp-geometry/probes/pedal-three.ly. Distance from the staff's bottom
    /// line to each row — una corda 2.777500, sostenuto 4.738700, sustain 7.181300.
    /// <para>
    /// ⚠️ THE SUSTAIN ROW IS GLYPHS, NOT TEXT, on both engines (LilyPond draws "Ped." with
    /// Emmentaler, and Lily# ported that in session 204), which is why this reads it as a
    /// MUSIC glyph while the other two are read as text. A probe that scanned text alone
    /// reported two rows and missed the third in silence.
    /// </para>
    /// <para>
    /// The ORDER is asserted, not the distances: LilyPond's rows step 1.961 then 2.443 (each
    /// row's own ink) where Lily# uses one StackGap for both, and that step model is a
    /// separate quantity with no ledger point yet.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThreePedalFamilies_StackInLilyPondsOrder()
    {
        string svg = LiveRender.SvgFromRenderSpec(ThreePedalsAtOnce);
        double staffBottom = System.Text.RegularExpressions.Regex
            .Matches(svg, @"<line x1=""0\.00"" y1=""([\d.]+)"" x2=""[\d.]+"" y2=""\1""")
            .Select(m => double.Parse(m.Groups[1].Value)).Distinct().Max();

        double RowOf(string pattern) => System.Text.RegularExpressions.Regex
            .Matches(svg, pattern)
            .Select(m => double.Parse(m.Groups[1].Value))
            .Where(y => y > staffBottom).Min() - staffBottom;

        double unaCorda = RowOf(@"<text x=""[\d.]+"" y=""([\d.]+)""[^>]*>una corda</text>");
        double sostenuto = RowOf(@"<text x=""[\d.]+"" y=""([\d.]+)""[^>]*>Sost\. Ped\.</text>");
        // The sustain word is a run of MUSIC glyphs — see the remarks.
        double sustain = RowOf(@"<text class=""music"" x=""[\d.]+"" y=""([\d.]+)""");

        Assert.True(unaCorda < sostenuto,
            $"una corda must sit NEAREST the staff: it is at {unaCorda:F2} and sostenuto at "
            + $"{sostenuto:F2}");
        Assert.True(sostenuto < sustain,
            $"sustain must sit OUTERMOST: sostenuto is at {sostenuto:F2} and sustain at "
            + $"{sustain:F2}");
    }
}
