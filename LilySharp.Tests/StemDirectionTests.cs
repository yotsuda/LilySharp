using Xunit;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

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