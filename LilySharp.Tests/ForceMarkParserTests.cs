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
using Xunit;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// `key!` / `time!` / `clef!`: the `!` right after the keyword draws a restatement that changes
/// nothing (LILYSHARP-OWN, owner's decision 2026-10-02, HANDOFF §1.1 第737). It sits after the
/// keyword because a `!` after the value is the dashed barline, and the keyword must be
/// followed by its value, so `key !` can be nothing else.
/// </summary>
[Trait("Category", "Unit")]
public class ForceMarkParserTests
{
    private static SyntaxNode ParseOne<T>(string music) where T : SyntaxNode
    {
        string source = "part m { clef treble }\nsection A { m { " + music + " } }\n";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        Assert.Equal(source, tree.GetRoot().ToFullString());
        return tree.GetRoot().DescendantNodes().OfType<T>().Last();
    }

    [Theory]
    [InlineData("key! ees major c1 |", true)]
    [InlineData("key ! ees major c1 |", true)]
    [InlineData("key ees major c1 |", false)]
    public void Key(string music, bool forced)
    {
        var key = (KeySignatureSyntax)ParseOne<KeySignatureSyntax>(music);
        Assert.Equal(forced, key.IsForced);
        Assert.Equal("ees", key.Pitch.PitchToken.Text);
        Assert.Equal("major", key.Mode!.Text);
    }

    [Fact]
    public void ACustomKey()
    {
        var key = (KeySignatureSyntax)ParseOne<KeySignatureSyntax>("key! custom bes c1 |");
        Assert.True(key.IsForced);
        Assert.True(key.IsCustom);
    }

    [Theory]
    [InlineData("time! 3/4 c2. |", true, 3, 4)]
    [InlineData("time! 2+3/8 c4 c4 c8 |", true, 5, 8)]
    [InlineData("time 3/4 c2. |", false, 3, 4)]
    public void Time(string music, bool forced, int beats, int beatType)
    {
        var time = (TimeSignatureSyntax)ParseOne<TimeSignatureSyntax>(music);
        Assert.Equal(forced, time.IsForced);
        Assert.Equal(beats, time.Beats);
        Assert.Equal(beatType, time.BeatType);
    }

    [Theory]
    [InlineData("clef! bass c1 |", true)]
    [InlineData("clef bass c1 |", false)]
    public void Clef(string music, bool forced)
    {
        var clef = (ClefDeclarationSyntax)ParseOne<ClefDeclarationSyntax>(music);
        Assert.Equal(forced, clef.IsForced);
        Assert.Equal("bass", clef.ClefName.Text);
    }

    [Fact]
    public void ABangAfterTheValue_IsStillTheDashedBarline()
    {
        var key = (KeySignatureSyntax)ParseOne<KeySignatureSyntax>("key ees major ! c1 |");
        Assert.False(key.IsForced);
        var tree = SyntaxTree.Parse("part m { clef treble }\nsection A { m { key ees major ! c1 | } }\n");
        Assert.Contains(tree.GetRoot().DescendantNodes().OfType<BarlineSyntax>(),
            b => b.BarToken.Kind == SyntaxKind.DashedBar);
    }
}
