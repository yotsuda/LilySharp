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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>@shiftOff</c> keeps one note's column in its voice group's first slot, LilyPond's
/// <c>\once \shiftOff</c> (OMR REQUESTS #25, Schumann's Widmung: the third voice's g' stands
/// under the ees'' above it).
/// </summary>
[Trait("Category", "Unit")]
public class ShiftOffTests
{
    private static NoteItem Note(int staffPosition, Fraction duration, bool shiftOff = false) =>
        new(staffPosition, duration, 0, null, 0) { ShiftOff = shiftOff };

    private static double XOf(ImmutableArray<(int VoiceId, int ItemIndex, double XOffset, bool HeadTransparent, DotAdjustment Dot)> offsets, int voice) =>
        offsets.First(o => o.VoiceId == voice).XOffset;

    /// <summary>The repro's column: ees''8 up, ees''4 down, g'8 down in the third voice.</summary>
    private static ImmutableArray<(int VoiceId, int ItemIndex, double XOffset, bool HeadTransparent, DotAdjustment Dot)> Widmung(bool shiftOff) =>
        new NoteCollision().CalculateVoiceOffsets(new VoiceColumn(
            ImmutableArray.Create(
                new VoiceEntry(1, Note(3, Fraction.FromNoteValue(8)), 0, forcedStemUp: true),
                new VoiceEntry(2, Note(3, Fraction.Quarter), 0, forcedStemUp: false),
                new VoiceEntry(3, Note(-2, Fraction.FromNoteValue(8), shiftOff), 0, forcedStemUp: false)),
            measureIndex: 0));

    [Fact]
    public void TheThirdVoiceStandsAsideOfTheSecond_WithoutIt()
    {
        var offsets = Widmung(shiftOff: false);
        Assert.True(XOf(offsets, 3) < XOf(offsets, 2)); // a down voice steps aside to the left
    }

    [Fact]
    public void TheThirdVoiceStandsUnderTheSecond_WithIt()
    {
        var offsets = Widmung(shiftOff: true);
        Assert.Equal(XOf(offsets, 2), XOf(offsets, 3), 6);
    }

    [Fact]
    public void TheGroupIsOrderedByTheShift_NotByTheVoiceNumber()
    {
        // Voices 3 and 5 read 1 and 2; the fifth's @shiftOff reads 0 and puts it first.
        static ImmutableArray<(int VoiceId, int ItemIndex, double XOffset, bool HeadTransparent, DotAdjustment Dot)> Solve(bool shiftOff) =>
            new NoteCollision().CalculateVoiceOffsets(new VoiceColumn(
                ImmutableArray.Create(
                    new VoiceEntry(3, Note(0, Fraction.Quarter), 0, forcedStemUp: true),
                    new VoiceEntry(5, Note(0, Fraction.Quarter, shiftOff), 0, forcedStemUp: true)),
                measureIndex: 0));

        var plain = Solve(shiftOff: false);
        Assert.Equal(0.0, XOf(plain, 3), 6);
        Assert.True(XOf(plain, 5) > 0);

        var off = Solve(shiftOff: true);
        Assert.Equal(0.0, XOf(off, 5), 6);
        Assert.True(XOf(off, 3) > 0);
    }

    private const string Book = """
        octave absolute
        time 4/4
        part rh { clef treble }
        section A { rh { voice { c''1 } { a'1 } { f'4@shiftOff <e' g'>4@shiftOff d'2 } | } }
        form { ~A }
        score { staff rh }
        """;

    [Fact]
    public void TheAnnotationReachesTheNoteAndTheChord_AndOnlyThem()
    {
        var tree = SyntaxTree.Parse(Book);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var items = score.EnumerateStaves().Single().Staff.Voices[2].Measures
            .SelectMany(m => m.Items).ToArray();

        Assert.True(items.OfType<NoteItem>().First().ShiftOff);
        Assert.True(items.OfType<ChordItem>().Single().ShiftOff);
        Assert.False(items.OfType<NoteItem>().Last().ShiftOff);
    }

    [Fact]
    public void TheTwinWritesLilyPondsOwnCommand_ForThatNote()
    {
        var ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book));
        Assert.Equal(2, ly.Split("\\once \\shiftOff ").Length - 1);
    }
}
