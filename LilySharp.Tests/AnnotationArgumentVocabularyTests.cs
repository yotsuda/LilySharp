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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Which <c>@name(</c> is an argument list and which is a slur opening. The
/// parser used to read EVERY identifier's '(' as an argument list, because the
/// name is resolved downstream — so <c>c4@staccato (d4 e4 f4)</c> ate the slur
/// group and three notes left the bar, reported only as "Unknown annotation
/// '@staccato(d 4 e 4 f 4)'".
/// </summary>
[Trait("Category", "Unit")]
public class AnnotationArgumentVocabularyTests
{
    private static SyntaxNode Root(string music)
        => SyntaxTree.Parse("melody { " + music + " }").GetRoot();

    /// <summary>
    /// The defect, stated as the music it loses: the notes inside the slur stay
    /// in the bar. The control is the same bar with the slur one note later —
    /// that spelling was never eaten, so it says the counter is honest.
    /// </summary>
    [Theory]
    [InlineData("c4@staccato (d4 e4 f4) |")]
    [InlineData("c4@accent d4( e4 f4) |")]      // control: already fine before
    [InlineData("c4@glissando (d4 e4 f4) |")]   // a plain feature name, not an articulation
    public void AKnownArgumentlessAnnotation_LeavesTheParenthesisToTheMusic(string music)
    {
        var root = Root(music);
        Assert.Equal(4, root.DescendantNodes().OfType<NoteSyntax>().Count());
        Assert.Empty(root.DescendantNodes().OfType<MusicMarkSyntax>());
    }

    [Theory]
    [InlineData("c4@fig(6) |", "fig.6")]
    [InlineData("c4@fig(3 5) |", "fig.3.5")]
    [InlineData("c4@finger(3) |", "finger.3")]
    [InlineData("c4@bend(5) |", "bend.5")]
    [InlineData("c4@notehead(x) |", "notehead.x")]
    [InlineData("c4@chord(c:m7) |", "chord.c.:.m7")]
    [InlineData("c4@feather(right) |", "feather.right")]
    [InlineData("c4@arpeggio(bracket) |", "arpeggio.bracket")]
    [InlineData("c4@ottava(bassa) |", "ottava.bassa")]
    public void AnArgumentTakingAnnotation_StillReadsItsArgument(string music, string markName)
    {
        var mark = Assert.Single(Root(music).DescendantNodes().OfType<MusicMarkSyntax>());
        Assert.Equal(markName, mark.MarkName);
    }

    /// <summary>
    /// An unknown name keeps reading its argument, so a typo stays ONE mistake.
    /// Handing its '(' to the slur instead would answer "@notehed" with a second,
    /// unrelated error ("Undefined variable or phrase: 'x'") and lose the
    /// suggestion, which is computed from the whole dotted name.
    /// </summary>
    [Fact]
    public void AnUnknownAnnotation_StillReadsItsArgument()
    {
        var tree = SyntaxTree.Parse("c4@notehed(x) d4 |");
        var validator = new AnnotationNameValidator();
        validator.Validate(tree);

        var warning = Assert.Single(
            validator.Diagnostics, d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Contains("Did you mean '@notehead(x)'?", warning.Message);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Message.Contains("Undefined variable or phrase"));
    }
}
