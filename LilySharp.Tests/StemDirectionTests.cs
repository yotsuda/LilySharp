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

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class StemDirectionTests
{
    [Theory]
    [InlineData(0, true)]   // Low note: stem up
    [InlineData(2, true)]   // Below middle: stem up
    [InlineData(3, true)]   // Just below middle: stem up
    [InlineData(4, false)]  // Middle line: stem down (convention)
    [InlineData(6, false)]  // Above middle: stem down
    [InlineData(8, false)]  // High note: stem down
    public void SingleNote_AutomaticDirection(int staffPosition, bool expectedStemUp)
    {
        bool result = StemDirection.GetStemUp(staffPosition);
        Assert.Equal(expectedStemUp, result);
    }

    [Theory]
    [InlineData(1, true)]   // Voice 1: always up
    [InlineData(2, false)]  // Voice 2: always down
    [InlineData(3, true)]   // Voice 3: up
    [InlineData(4, false)]  // Voice 4: down
    public void VoiceNumber_OverridesAutomatic(int voiceNumber, bool expectedStemUp)
    {
        // Even high notes have stems up in voice 1
        bool result = StemDirection.GetStemUp(staffPosition: 8, voiceNumber);
        Assert.Equal(expectedStemUp, result);
    }

    [Fact]
    public void Chord_DirectionBasedOnExtremeNotes()
    {
        // Chord spanning below middle
        var positions1 = new[] { 0, 2, 4 };
        Assert.True(StemDirection.GetStemUp(positions1));

        // Chord spanning above middle
        var positions2 = new[] { 4, 6, 8 };
        Assert.False(StemDirection.GetStemUp(positions2));
    }

    [Fact]
    public void Chord_BalancedAcrossMiddle_PrefersStemDown()
    {
        // Equally distant from middle: convention is stem down
        var positions = new[] { 2, 6 };  // 2 below, 2 above middle (4)
        Assert.False(StemDirection.GetStemUp(positions));
    }
}
