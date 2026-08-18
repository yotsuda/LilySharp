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
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;
using LilySharp.Core.Rendering;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class RehearsalMarkTests
{
    // --- MusicMarkType.Rehearsal ---

    [Fact]
    public void MusicMarkType_Rehearsal_Exists()
    {
        var type = MusicMarkType.Rehearsal;
        Assert.Equal("Rehearsal", type.ToString());
    }

    // --- the label is read from the ARGUMENT (VALUE_SITE_AUDIT §9.5.3 ⑵) ---

    private static MusicMarkSyntax Mark(string music)
        => Assert.Single(SyntaxTree.Parse("melody { " + music + " }")
            .GetRoot().DescendantNodes().OfType<MusicMarkSyntax>());

    /// <summary>
    /// The label is the annotation's argument, printed as written — case and symbols
    /// preserved, the quotes being delimiters rather than text. The name gate is
    /// case-INSENSITIVE, which is what the string form's
    /// <c>MarkName.ToLowerInvariant().StartsWith("mark.")</c> did.
    /// </summary>
    [Theory]
    [InlineData("c4@mark(\"A\") |", "A")]
    [InlineData("c4@mark(\"Solo\") |", "Solo")]
    [InlineData("c4@mark(\"abc\") |", "abc")]      // case preserved — no forced upper-casing
    [InlineData("c4@mark(\"D.S.\") |", "D.S.")]    // symbols preserved
    [InlineData("c4@mark(\"a b\") |", "a b")]      // a space INSIDE the quotes is label text
    [InlineData("c4@mark(\"\") |", "")]
    [InlineData("c4@mark(A) |", "A")]              // unquoted: LYS1009, but the same label
    [InlineData("c4@mark(1) |", "1")]
    [InlineData("c4@Mark(\"C\") |", "C")]
    [InlineData("c4@MARK(\"z\") |", "z")]
    public void ARehearsalLabel_IsItsArgument(string music, string expected)
        => Assert.Equal(expected, AnnotationValues.Rehearsal(Mark(music), out _));

    /// <summary>
    /// ⚠️ The declared behaviour change. MarkName joined the argument's tokens with '.',
    /// so a label written as more than one token printed dots nobody typed. Reading the
    /// runs prints what was written. No book writes either spelling (measured: the two
    /// <c>@mark(</c> sites in the corpus and fixtures are both <c>@mark("X")</c>).
    /// </summary>
    [Theory]
    [InlineData("c4@mark(-1) |", "-1")]     // MarkName spelled this "-.1"
    [InlineData("c4@mark(A B) |", "A B")]   // MarkName spelled this "A.B"
    public void ARehearsalLabelInSeveralRuns_PrintsWhatWasWritten(string music, string expected)
        => Assert.Equal(expected, AnnotationValues.Rehearsal(Mark(music), out _));

    /// <summary>
    /// An empty argument list is not an empty label: <c>@mark()</c> spelled MarkName as
    /// just "mark", which the old <c>Length > 5</c> test refused, and it stays refused.
    /// </summary>
    [Fact]
    public void AMarkWithNoArgumentAtAll_IsNotARehearsalMark()
        => Assert.Null(AnnotationValues.Rehearsal(Mark("c4@mark() |"), out _));

    /// <summary>
    /// Quoted-ness is answered by the same read, so the diagnostic and the label can no
    /// longer disagree about where the label starts.
    /// </summary>
    [Theory]
    [InlineData("c4@mark(\"A\") |", true)]
    [InlineData("c4@mark(\"\") |", true)]
    [InlineData("c4@mark(A) |", false)]
    [InlineData("c4@mark(1) |", false)]
    public void OnlyAQuotedLabel_SatisfiesTheDiagnostic(string music, bool expected)
    {
        AnnotationValues.Rehearsal(Mark(music), out var quoted);
        Assert.Equal(expected, quoted);
    }

    // --- ParseMarkName is now a table of NAMES only ---

    /// <summary>
    /// ★ Positive control for the split: the dotted-name table no longer answers the
    /// rehearsal mark, because a rehearsal mark is an argument and not a name. Nothing
    /// in a source file reaches it with these strings — <c>@mark.A</c> has been LYS0023
    /// ("this '.' belongs to nothing") since 2026-08-14 and builds no mark node at all.
    /// </summary>
    [Theory]
    [InlineData("mark.A")]
    [InlineData("mark.1")]
    [InlineData("MARK.Z")]
    [InlineData("mark.")]
    [InlineData("mark")]
    public void TheNameTable_DoesNotAnswerRehearsal(string name)
        => Assert.Null(MusicMarkItem.ParseMarkName(name));

    [Theory]
    [InlineData("segno", MusicMarkType.Segno)]
    [InlineData("coda", MusicMarkType.Coda)]
    [InlineData("fine", MusicMarkType.Fine)]
    [InlineData("ds.al.fine", MusicMarkType.DalSegnoAlFine)]
    [InlineData("ottava.bassa", MusicMarkType.OttavaDown)]
    public void TheNameTable_StillAnswersTheDottedNames(string name, MusicMarkType expected)
        => Assert.Equal(expected, MusicMarkItem.ParseMarkName(name));

    // --- the twin says what Lily# draws ---

    /// <summary>
    /// ⚠️ The second declared change, and the reason it is in this file rather than with
    /// the other exporter tests: the exporter's own <c>mark.</c> slice was the FOURTH
    /// copy of this label, so it moved with the island. <c>\box</c> takes ONE markup
    /// (scm/define-markup-commands.scm:1049), so the unquoted label the twin used to
    /// write boxed only the first word of <c>@mark("a b")</c> — measured on LilyPond
    /// 2.26.0: box width 1.9331 unquoted against 4.0159 quoted, the latter being what
    /// Lily# draws. Quoting changes nothing for a one-word label (<c>\box A</c> and
    /// <c>\box "A"</c> render byte-identical SVG on 2.26.0).
    /// </summary>
    [Theory]
    [InlineData("@mark(\"A\")", "\\mark \\markup { \\box \"A\" }")]
    [InlineData("@mark(\"a b\")", "\\mark \\markup { \\box \"a b\" }")]
    [InlineData("@mark(\"D.S.\")", "\\mark \\markup { \\box \"D.S.\" }")]
    public void TheTwinQuotesTheLabel(string annotation, string expected)
    {
        var tree = SyntaxTree.Parse(
            "part melody\nsection A { melody { c4" + annotation + " d4 e4 f4 | } }\n"
            + "form main { ~A }\nscore main { staff melody }\n");
        Assert.Contains(expected, new LilyPondExporter().Export(tree));
    }

    // --- MusicMarkItem constructors ---

    [Fact]
    public void MusicMarkItem_StandardConstructor_SetsDefaults()
    {
        var item = new MusicMarkItem(MusicMarkType.Fine, 3, 100);

        Assert.Equal(MusicMarkType.Fine, item.Type);
        Assert.Equal("Fine", item.Text);
        Assert.Equal(MusicMarkPosition.End, item.Position);
        Assert.Equal(MusicMarkVertical.Above, item.Vertical);
        Assert.False(item.IsSymbol);
        Assert.Equal(3, item.MeasureIndex);
        Assert.Equal(100, item.SourcePosition);
    }

    [Fact]
    public void MusicMarkItem_CustomText_ForRehearsal()
    {
        var item = new MusicMarkItem(MusicMarkType.Rehearsal, "A", 0, 42);

        Assert.Equal(MusicMarkType.Rehearsal, item.Type);
        Assert.Equal("A", item.Text);
        Assert.Equal(MusicMarkPosition.Beginning, item.Position);
        Assert.Equal(MusicMarkVertical.Above, item.Vertical);
        Assert.False(item.IsSymbol);
        Assert.Equal(0, item.MeasureIndex);
        Assert.Equal(42, item.SourcePosition);
    }

    [Fact]
    public void MusicMarkItem_Rehearsal_PositionIsBeginning()
    {
        var item = new MusicMarkItem(MusicMarkType.Rehearsal, "B", 1, 0);
        Assert.Equal(MusicMarkPosition.Beginning, item.Position);
    }

    [Fact]
    public void MusicMarkItem_Rehearsal_VerticalIsAbove()
    {
        var item = new MusicMarkItem(MusicMarkType.Rehearsal, "C", 2, 0);
        Assert.Equal(MusicMarkVertical.Above, item.Vertical);
    }

    // --- MusicMarkEngraver.Calculate for Rehearsal ---

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
    public void Calculate_RehearsalMark_ReturnsLayout()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rehearsal, "A", 0, 42));

        var result = MusicMarkEngraver.Calculate(ScoreTextMetrics.Bundled, null, marks, systems, ml);

        Assert.Single(result);
        Assert.Equal(MusicMarkType.Rehearsal, result[0].MarkType);
        Assert.Equal("A", result[0].Text);
        Assert.False(result[0].IsSymbol);
        Assert.Equal(42, result[0].SourcePosition);
    }

    [Fact]
    public void Calculate_RehearsalMark_XAtBeginning()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rehearsal, "B", 1, 0));

        var result = MusicMarkEngraver.Calculate(ScoreTextMetrics.Bundled, null, marks, systems, ml);

        Assert.Single(result);
        // Rehearsal marks are at beginning, so X should be near start of measure 1
        double measureStart = ml[1].X;
        Assert.True(result[0].X > measureStart && result[0].X < measureStart + 2.0,
            $"X ({result[0].X:F2}) should be near start of measure ({measureStart:F2})");
    }

    [Fact]
    public void Calculate_RehearsalMark_YAboveStaff()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rehearsal, "C", 0, 0));

        var result = MusicMarkEngraver.Calculate(ScoreTextMetrics.Bundled, null, marks, systems, ml);

        Assert.Single(result);
        // Y-up (frame B): above the staff top line means YUp > 2 (top line = +2).
        Assert.True(result[0].YUp > 2.0, $"YUp ({result[0].YUp:F2}) should be above the staff (> 2)");
    }

    [Fact]
    public void Calculate_MultipleRehearsalMarks()
    {
        var systems = CreateSingleSystem(3);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rehearsal, "A", 0, 10),
            new MusicMarkItem(MusicMarkType.Rehearsal, "B", 2, 20));

        var result = MusicMarkEngraver.Calculate(ScoreTextMetrics.Bundled, null, marks, systems, ml);

        Assert.Equal(2, result.Length);
        Assert.Equal("A", result[0].Text);
        Assert.Equal("B", result[1].Text);
    }

    [Fact]
    public void Calculate_MarkClearsTwoRowChord_AsBoth()
    {
        // `as both` stacks the Roman degree ABOVE the chord name, making the
        // chord band ~2.2 ss taller. A mark over that chord must clear the
        // UPPER row — before the fix it cleared only the name row and the mark
        // (tempo / section label) overprinted the degree line.
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var mark = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rehearsal, "A", 0, 0));

        // An inline top-staff chord (negative Y) at the mark's own column, so
        // its ink certainly overlaps the mark horizontally.
        double markX = MusicMarkEngraver.Calculate(ScoreTextMetrics.Bundled, null, mark, systems, ml)[0].X;
        ChordNameLayout Chord(string? above) =>
            // YUp = +3.0: the chord sits 3 ss ABOVE the system top (old device Y = -3.0).
            new ChordNameLayout(0, markX, 3.0, "Cmaj7", 0, AboveLine: above);

        double oneRowY = MusicMarkEngraver
            .Calculate(ScoreTextMetrics.Bundled, null, mark, systems, ml, chordNames: ImmutableArray.Create(Chord(null)))[0].YUp;
        double twoRowY = MusicMarkEngraver
            .Calculate(ScoreTextMetrics.Bundled, null, mark, systems, ml, chordNames: ImmutableArray.Create(Chord("Imaj7")))[0].YUp;

        // Y-up (frame B): the stacked degree row lifts the mark higher (larger YUp)
        // by the row height, so it clears the top line instead of overprinting it.
        Assert.True(twoRowY > oneRowY + 2.0,
            $"two-row mark YUp ({twoRowY:F2}) should sit well above one-row ({oneRowY:F2})");
    }

    // --- WHICH measure a mark belongs to (the COLLECTOR decides; every test above
    //     hands the engraver a MeasureIndex already decided, which is why the
    //     collector could hand it the wrong one for 200 sessions) ---

    private static System.Collections.Generic.IEnumerable<MusicMarkItem> CollectedMarks(string music)
        => new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(MusicSource.Parse(music)).MusicMarks
            .Where(m => m.Type == MusicMarkType.Rehearsal);

    /// <summary>
    /// ⚠️ A mark is written ON a note, so it belongs to the bar THAT NOTE begins in.
    /// The whole note is the case that used to fail: it fills its bar, so by the time
    /// the mark's own node was walked the builder had already crossed the barline and
    /// the mark was engraved one bar late. Every shorter duration hid the defect by
    /// leaving the builder inside the same bar.
    /// </summary>
    [Theory]
    [InlineData("c1@mark(\"Intro\") | g1 |")]        // fills the bar — the reported case
    [InlineData("c2@mark(\"Intro\") c2 | g1 |")]     // half
    [InlineData("c4@mark(\"Intro\") d4 e4 f4 | g1 |")]
    [InlineData("c4 d4@mark(\"Intro\") e4 f4 | g1 |")]   // mid-bar: still bar 0
    [InlineData("c2. c4@mark(\"Intro\") | g1 |")]        // last note OF the bar
    public void AMarkOnANote_BelongsToTheBarThatNoteBeginsIn(string music)
        => Assert.Equal(0, Assert.Single(CollectedMarks(music)).MeasureIndex);

    /// <summary>
    /// The same defect at the end of the piece did not print the mark one bar late —
    /// it printed nothing at all, because the bar it was sent to never came. The
    /// silence is the reason this is asserted on the RENDERED page and not only on
    /// the index: a mark addressed to measure N+1 is dropped downstream, so an index
    /// assertion alone would still pass a layout that quietly discards it.
    /// </summary>
    [Fact]
    public void AMarkOnTheLastNote_IsPrintedAndNotDroppedForABarThatNeverCame()
    {
        Assert.Equal(1, Assert.Single(CollectedMarks("c1 | g1@mark(\"Coda\") |")).MeasureIndex);
        Assert.Contains("Coda", LiveRender.Svg("c1 | g1@mark(\"Coda\") |"));
    }

    /// <summary>
    /// The engraved page, not the model: the reported book was <c>c1@mark("Intro") | g'1</c>
    /// and the box was drawn past the first barline. Ink left of that barline is the
    /// whole claim, and it is read off the drawn SVG.
    /// </summary>
    [Fact]
    public void AMarkOnABarFillingNote_IsDrawnBeforeTheFirstBarline()
    {
        string svg = LiveRender.Svg("c1@mark(\"Intro\") | g1 |");

        double markX = System.Text.RegularExpressions.Regex
            .Matches(svg, @"<text x=""([\d.]+)""[^>]*>Intro</text>")
            .Select(m => double.Parse(m.Groups[1].Value)).Single();
        double firstBarlineX = System.Text.RegularExpressions.Regex
            .Matches(svg, @"<rect x=""([\d.]+)""[^>]*height=""4\.00""")
            .Select(m => double.Parse(m.Groups[1].Value)).Min();

        Assert.True(markX < firstBarlineX,
            $"the mark ({markX:F2}) is drawn past the first barline ({firstBarlineX:F2}) — it is written on the note in bar 1");
    }
}

