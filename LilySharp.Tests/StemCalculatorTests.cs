using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class StemCalculatorTests
{
    [Fact]
    public void StemDetails_Default_MatchesLilyPondDefineGrobs()
    {
        var d = StemDetails.Default;

        // LILYPOND-REF: define-grobs.scm:3121-3141
        Assert.Equal(new[] { 3.5, 3.5, 3.5, 4.25, 5.0, 6.0, 7.0, 8.0, 9.0 }, d.Lengths);
        Assert.Equal(new[] { 3.26, 3.5, 3.6 }, d.BeamedLengths);
        Assert.Equal(new[] { 1.83, 1.5, 1.25 }, d.BeamedMinimumFreeLengths);
        Assert.Equal(new[] { 2.0, 1.25 }, d.BeamedExtremeMinimumFreeLengths);
        Assert.Equal(new[] { 1.0, 0.5, 0.25 }, d.StemShorten);
        Assert.Equal(1.0, d.LengthFraction);
    }

    [Fact]
    public void CalculateStemEndY_QuarterNote_Uses3_5StaffSpaces()
    {
        // Quarter note stem up at middle of staff
        double stemAttachY = 4.0; // middle of 4-space staff at systemY=2
        double systemY = 0.0;

        double endY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 2, staffPosition: 0);

        // Stem should be 3.5 staff spaces long (going up = smaller Y)
        double stemLength = stemAttachY - endY;
        Assert.True(stemLength >= 3.5 - 0.01, $"Quarter stem should be >= 3.5, got {stemLength}");
    }

    [Fact]
    public void CalculateStemEndY_32ndNote_UsesLongerStem()
    {
        // 32nd note at middle of staff
        double stemAttachY = 2.0;
        double systemY = 0.0;

        double endY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 5, staffPosition: 0);

        // 32nd note should use 4.25 staff spaces (index 3 in lengths array)
        double stemLength = stemAttachY - endY;
        Assert.True(stemLength >= 4.25 - 0.5, $"32nd stem should be longer, got {stemLength}");
    }

    [Fact]
    public void CalculateStemEndY_StemExtendesToMiddleLine()
    {
        // Note below staff (staff position -4 = 2 below bottom line)
        // systemY = 0, staffMiddle at Y=2
        double stemAttachY = 4.0; // bottom of staff
        double systemY = 0.0;

        double endY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 2, staffPosition: -4);

        // Stem should reach at least the middle line (Y=2)
        Assert.True(endY <= 2.0 + 0.01, $"Stem should reach middle line Y=2, got {endY}");
    }

    [Fact]
    public void CalculateStemEndY_UnnaturalDirection_Shortened()
    {
        // Note above middle line with stem up (unnatural direction)
        double systemY = 0.0;
        double stemAttachY = 1.0; // above middle line

        double naturalEndY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 2, staffPosition: 2); // above middle, stem up = unnatural

        double normalEndY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 2, staffPosition: -2); // below middle, stem up = natural

        // Unnatural direction should have shorter stem
        double unnaturalLength = stemAttachY - naturalEndY;
        double normalLength = stemAttachY - normalEndY;
        Assert.True(unnaturalLength <= normalLength,
            $"Unnatural direction stem ({unnaturalLength}) should be <= natural ({normalLength})");
    }

    [Fact]
    public void CalculateBeamedStemInfo_Returns_ValidInfo()
    {
        var info = StemCalculator.CalculateBeamedStemInfo(
            headPosition: 0,
            stemUp: true,
            beamCount: 1);

        // Ideal Y should be positive (above staff center for stem up)
        Assert.True(info.IdealY > 0, $"Ideal Y should be > 0 for stem up, got {info.IdealY}");
        Assert.True(info.StemUp);
    }

    [Fact]
    public void CalculateBeamedStemInfo_MoreBeams_LongerStem()
    {
        var info1 = StemCalculator.CalculateBeamedStemInfo(
            headPosition: 0, stemUp: true, beamCount: 1);

        var info3 = StemCalculator.CalculateBeamedStemInfo(
            headPosition: 0, stemUp: true, beamCount: 3);

        // More beams should result in longer ideal stem
        Assert.True(info3.IdealY >= info1.IdealY,
            $"3 beams ({info3.IdealY}) should need >= length than 1 beam ({info1.IdealY})");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    [InlineData(16, 4)]
    [InlineData(32, 5)]
    public void GetDurationLog_ReturnsCorrectValues(int noteValue, int expectedLog)
    {
        Assert.Equal(expectedLog, StemCalculator.GetDurationLog(noteValue));
    }
}
