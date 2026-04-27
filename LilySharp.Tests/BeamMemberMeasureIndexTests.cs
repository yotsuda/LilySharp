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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Pins down the BeamMember.MeasureIndex / ResolveMeasureIndex semantics that
/// future cross-measure beam detection (K-1b) will key off.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc — beams may span barlines via manual marker pairs.
/// </remarks>
[Trait("Category", "Unit")]
public class BeamMemberMeasureIndexTests
{
    private static NoteItem MakeNote() =>
        new(staffPosition: 0,
            baseDuration: new Fraction(1, 8),
            dots: 0,
            accidental: null,
            needsLedgerLines: false,
            sourcePosition: 0);

    [Fact]
    public void DefaultMeasureIndex_IsSentinelMinusOne()
    {
        var member = new BeamMember(
            item: MakeNote(),
            beamCount: 1, beamCountLeft: 0, beamCountRight: 0,
            staffPosition: 0, itemIndex: 0);

        Assert.Equal(-1, member.MeasureIndex);
    }

    [Fact]
    public void ResolveMeasureIndex_WithSentinel_FallsBackToDefault()
    {
        var member = new BeamMember(
            item: MakeNote(),
            beamCount: 1, beamCountLeft: 0, beamCountRight: 0,
            staffPosition: 0, itemIndex: 0);

        Assert.Equal(7, member.ResolveMeasureIndex(defaultMeasureIndex: 7));
    }

    [Fact]
    public void ResolveMeasureIndex_WithExplicitValue_ReturnsThatValue()
    {
        var member = new BeamMember(
            item: MakeNote(),
            beamCount: 1, beamCountLeft: 0, beamCountRight: 0,
            staffPosition: 0, itemIndex: 0,
            measureIndex: 3);

        Assert.Equal(3, member.ResolveMeasureIndex(defaultMeasureIndex: 7));
    }

    [Fact]
    public void ExplicitMeasureIndex_PreservedOnRecord()
    {
        var member = new BeamMember(
            item: MakeNote(),
            beamCount: 1, beamCountLeft: 0, beamCountRight: 0,
            staffPosition: 0, itemIndex: 5,
            measureIndex: 12);

        Assert.Equal(12, member.MeasureIndex);
        Assert.Equal(5, member.ItemIndex);
    }
}
