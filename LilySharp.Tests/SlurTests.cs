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

using Xunit;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class SlurTests
{
    private static NoteItem CreateNote(int staffPosition)
        => new(staffPosition, Fraction.Quarter, 0, null, false, 0);

    [Fact]
    public void SlurEngraver_CalculatesSlurLayout()
    {
        // Arrange: Slur from C4 to G4
        var startNote = CreateNote(0);
        var endNote = CreateNote(5);
        var slur = new SlurItem(startNote, endNote, 0, 5, curveUp: true, 0, 0, 0, 3);

        var engraver = new SlurEngraver();

        // Act
        var layout = engraver.CalculateSlurLayout(
            slur,
            startX: 50.0,
            startY: 100.0,
            endX: 200.0,
            endY: 75.0);

        // Assert
        Assert.NotNull(layout);
        Assert.True(layout.StartX < layout.EndX, "Start X should be less than End X");
        Assert.True(layout.Control1.Y < layout.StartY, "Control point should be above for curve up");
    }

    [Fact]
    public void SlurEngraver_CurveDownHasLowerControlPoints()
    {
        // Arrange
        var startNote = CreateNote(6);
        var endNote = CreateNote(4);
        var slur = new SlurItem(startNote, endNote, 6, 4, curveUp: false, 0, 0, 0, 2);

        var engraver = new SlurEngraver();

        // Act
        var layout = engraver.CalculateSlurLayout(
            slur,
            startX: 50.0,
            startY: 80.0,
            endX: 150.0,
            endY: 90.0);

        // Assert: control points should be below the line connecting start to end
        double width = layout.EndX - layout.StartX;
        double t1 = (layout.Control1.X - layout.StartX) / width;
        double linearY1 = layout.StartY + t1 * (layout.EndY - layout.StartY);
        Assert.True(layout.Control1.Y > linearY1,
            $"Control point 1 Y ({layout.Control1.Y:F2}) should be below linear interpolation ({linearY1:F2}) for curve down");
    }

    [Fact]
    public void SlurScoreParameters_HasReasonableDefaults()
    {
        var params_ = SlurScoreParameters.Default;

        Assert.True(params_.HeightLimit > 0);
        Assert.True(params_.Ratio > 0 && params_.Ratio < 1);
        Assert.True(params_.MaxSlope > 0);
        Assert.True(params_.RegionSize > 0);
    }

    [Fact]
    public void SlurItem_StoresCorrectProperties()
    {
        var startNote = CreateNote(0);
        var endNote = CreateNote(7);

        var slur = new SlurItem(
            startNote, endNote,
            startStaffPosition: 0,
            endStaffPosition: 7,
            curveUp: false,
            startMeasureIndex: 0,
            endMeasureIndex: 1,
            startItemIndex: 0,
            endItemIndex: 2);

        Assert.Equal(0, slur.StartStaffPosition);
        Assert.Equal(7, slur.EndStaffPosition);
        Assert.False(slur.CurveUp);
        Assert.Equal(0, slur.StartMeasureIndex);
        Assert.Equal(1, slur.EndMeasureIndex);
    }
}
