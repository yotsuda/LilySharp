using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

public class TablatureTests
{
    [Fact]
    public void ParseTabStaff_NoTuning()
    {
        var source = @"\tabStaff { e4 a d' }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseTabStaff_WithGuitarTuning()
    {
        var source = @"\tabStaff \tuning guitar { e4 a d' }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseTabStaff_WithBassTuning()
    {
        var source = @"\tabStaff \tuning bass { e,4 a, d g }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseTabStaff_WithBass5Tuning()
    {
        var source = @"\tabStaff \tuning bass5 { b,,4 e, a, d g }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseTabStaff_WithUkuleleTuning()
    {
        var source = @"\tabStaff \tuning ukulele { g c' e' a' }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void Tunings_CalculateFret_OpenLowE()
    {
        // E2 (40) = low E string = string 6 in guitar notation
        var (stringNum, fret) = Tunings.CalculateFret(40, Tunings.Guitar);
        Assert.Equal(6, stringNum);  // 6th string (lowest)
        Assert.Equal(0, fret);       // open string
    }

    [Fact]
    public void Tunings_CalculateFret_FrettedNote()
    {
        // G2 (43) on low E string = fret 3
        var (stringNum, fret) = Tunings.CalculateFret(43, Tunings.Guitar);
        Assert.Equal(3, fret);
    }

    [Fact]
    public void Tunings_CalculateFret_OpenHighE()
    {
        // E4 (64) = high E string = string 1 in guitar notation
        var (stringNum, fret) = Tunings.CalculateFret(64, Tunings.Guitar);
        Assert.Equal(1, stringNum);  // 1st string (highest)
        Assert.Equal(0, fret);       // open string
    }

    [Fact]
    public void Tunings_CalculateFret_PreferredString()
    {
        // A2 (45) on string 5 (A string) = fret 0
        var (stringNum, fret) = Tunings.CalculateFret(45, Tunings.Guitar, 5);
        Assert.Equal(5, stringNum);
        Assert.Equal(0, fret);
    }

    [Fact]
    public void Tunings_Bass_CalculateFret()
    {
        // E1 (28) on low E string = fret 0
        var (stringNum, fret) = Tunings.CalculateFret(28, Tunings.Bass);
        Assert.Equal(0, fret);
    }
}