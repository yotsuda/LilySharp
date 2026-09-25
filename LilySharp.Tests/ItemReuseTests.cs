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
/// The two places a keystroke used to copy items whose content it did not change (session 601:
/// 61% of the final score's items were a new object with the old content): the suffix splice's
/// position shift, and the tab staff's string resolution. Each answers with the SAME instance
/// when nothing it writes would differ, and with a new one when something does.
/// </summary>
public class ItemReuseTests
{
    private const string Book = """
        octave absolute
        part bl { clef bass tuning bass }
        section A { bl { c4 d e f | g4 a b c' | } }
        form main { A }
        score main "x" { staff bl tab bl }
        """;

    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static Voice NotationVoice(MultiStaffScore score) =>
        score.EnumerateStaves().First(s => !s.Staff.IsTab).Staff.PrimaryVoice;

    [Fact]
    public void Shift_NothingMoved_HandsBackTheSameMeasureAndItems()
    {
        var m = NotationVoice(Collect(Book)).Measures[1];
        // Every position sits before the window: nothing is re-homed.
        var unmoved = CollectTailShifter.ShiftMeasure(m, new CollectTailShifter.Window(100_000, 100_000, 7));
        Assert.Same(m, unmoved);

        // Every position sits after it: each one moves, so each is a new object.
        var moved = CollectTailShifter.ShiftMeasure(m, new CollectTailShifter.Window(1, 1, 7))!;
        Assert.NotSame(m, moved);
        for (int i = 0; i < m.Items.Length; i++)
        {
            Assert.NotSame(m.Items[i], moved.Items[i]);
            Assert.Equal(m.Items[i].SourcePosition + 7, moved.Items[i].SourcePosition);
        }
    }

    [Fact]
    public void TabStrings_TheSameNoteOnTheSameString_IsTheSameCopy_UntilItsStampsChange()
    {
        var input = NotationVoice(Collect(Book));
        var first = new TabResolver().ResolveTabStrings(input, TuningType.Bass, ClefType.Bass);
        var second = new TabResolver().ResolveTabStrings(input, TuningType.Bass, ClefType.Bass);

        var a = first.Measures[0].Items[0] as NoteItem;
        var b = second.Measures[0].Items[0] as NoteItem;
        Assert.NotNull(a);
        Assert.NotNull(a!.StringNumber);
        Assert.NotSame(input.Measures[0].Items[0], a);   // it IS a copy of the input...
        Assert.Same(a, b);                               // ...and the same copy both times

        // The one mutation on the model: a re-bake stamps the adopted input in place. The
        // copy made before carries the old stamps, so it must not be handed out again.
        var inputNote = (NoteItem)input.Measures[0].Items[0];
        inputNote.StampBeam(stemUp: !(inputNote.StemUpOverride ?? false), beamId: 4242, pureTip: null);
        var third = new TabResolver().ResolveTabStrings(input, TuningType.Bass, ClefType.Bass);
        var c = (NoteItem)third.Measures[0].Items[0];
        Assert.NotSame(a, c);
        Assert.Equal(4242, c.BeamId);
        Assert.Equal(a.StringNumber, c.StringNumber);
    }
}
