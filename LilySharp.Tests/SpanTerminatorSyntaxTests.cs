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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>@!X</c> — the terminator spelling. It ends what <c>@X</c> opened, and it costs the
/// lexer nothing: '!' already lexes as <see cref="SyntaxKind.DashedBar"/>, and after an '@'
/// the stream stands at a mark NAME, where a barline cannot — so the two readings of '!'
/// never compete for the same position.
/// </summary>
[Trait("Category", "Unit")]
public class SpanTerminatorSyntaxTests
{
    /// <summary>A whole file, because a mark is a post-event: it reaches the tree only
    /// attached to a note inside a scored part.</summary>
    private static string Source(string music)
        => $"octave absolute part m {{ clef treble }} "
           + $"section A {{ m {{ {music} }} }} form main {{ A }} score main {{ staff m }}";

    private static SyntaxTree Parsed(string music) => SyntaxTree.Parse(Source(music));

    private static MusicMarkSyntax Mark(string music)
        => Assert.Single(Parsed(music).GetRoot().DescendantNodes().OfType<MusicMarkSyntax>());

    [Theory]
    [InlineData("c'4@!rit c' c' c' |")]
    [InlineData("c'4@!accel c' c' c' |")]
    [InlineData("c'4@!rall c' c' c' |")]
    [InlineData("c'4@!textSpan c' c' c' |")]
    public void ATerminator_Parses(string music)
    {
        var tree = Parsed(music);
        Assert.False(tree.HasErrors,
            string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
    }

    /// <summary>
    /// ⚠️ THE NAME IS THE SAME EITHER WAY, and this is the reason the spelling was chosen:
    /// one vocabulary, one "did you mean" list, one place a word is added. A terminator with
    /// a name of its own would need a second of each.
    /// </summary>
    [Theory]
    [InlineData("rit")]
    [InlineData("accel")]
    [InlineData("rall")]
    public void ATerminator_ReportsTheSameNameAsItsStart(string word)
    {
        // The START of a sugar word is a bare ARTICULATION (one token, no argument), so the
        // name it reports is read there; the terminator is a MusicMarkSyntax because the '!'
        // needs a slot. The two spellings must still answer with ONE word.
        var end = Mark($"c'4@!{word} c' c' c' |");
        Assert.Equal(word, end.Name);
        Assert.True(end.IsSpanEnd);

        var start = Mark("c'4@textSpan(\"x\") c' c' c' |");
        Assert.Equal("textSpan", start.Name);
        Assert.False(start.IsSpanEnd);
    }

    /// <summary>The '!' is punctuation, so it stays out of the dotted MarkName too — the
    /// string every collector still parses.</summary>
    [Fact]
    public void TheBang_IsNotPartOfTheMarkName()
    {
        var mark = Mark("c'4@!textSpan c' c' c' |");
        Assert.Equal("textSpan", mark.MarkName);
        Assert.Equal("textSpan", mark.Name);
    }

    /// <summary>...but it IS part of the source span: the token is kept on the node, so a
    /// terminator's width is its own and every later offset stays put.</summary>
    [Fact]
    public void TheBang_IsInsideTheSourceSpan()
    {
        const string music = "c'4@!rit c' c' c' |";
        var mark = Mark(music);
        Assert.Equal(Source(music).IndexOf("@!rit", System.StringComparison.Ordinal), mark.Span.Start);
        Assert.Equal("@!rit".Length, mark.Span.Length);
    }

    /// <summary>
    /// A barline written AFTER a note still lexes as one — the '!' arms are reached only in
    /// the mark-name position that follows an '@', so nothing about ordinary music moves.
    /// </summary>
    [Fact]
    public void ADashedBarline_IsStillABarline()
    {
        var tree = Parsed("c'4 c' ! c' c' |");
        Assert.False(tree.HasErrors,
            string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        Assert.Empty(tree.GetRoot().DescendantNodes().OfType<MusicMarkSyntax>());
        Assert.NotEmpty(tree.GetRoot().DescendantNodes().OfType<BarlineSyntax>());
    }

    /// <summary>
    /// ONE STOP PER FAMILY, whichever of the family's words was written: '@!quindicesima'
    /// and '@!ottava' are the same mark, as '\ottava #0' cancels whatever octavation runs,
    /// and '@!rit' and '@!textSpan' are the same mark for the same reason on their side.
    /// </summary>
    [Theory]
    [InlineData("rit", MusicMarkType.TextSpanStop)]
    [InlineData("accel", MusicMarkType.TextSpanStop)]
    [InlineData("rall", MusicMarkType.TextSpanStop)]
    [InlineData("textSpan", MusicMarkType.TextSpanStop)]
    [InlineData("ottava", MusicMarkType.OttavaStop)]
    [InlineData("ottava.bassa", MusicMarkType.OttavaStop)]
    [InlineData("quindicesima", MusicMarkType.OttavaStop)]
    [InlineData("15mb", MusicMarkType.OttavaStop)]
    public void AFamilyWithATerminator_AnswersWithItsStop(string name, MusicMarkType stop)
        => Assert.Equal(stop, MusicMarkItem.ParseSpanEndName(name));

    /// <summary>Only families that HAVE an end accept the spelling. Written on any other
    /// name, '@!X' is reported rather than quietly turned into the mark '@X' would make —
    /// which is the silent drop the annotation validator exists to prevent.</summary>
    [Theory]
    [InlineData("sustainOn")]
    [InlineData("sostenutoOn")]
    [InlineData("segno")]
    [InlineData("staccato")]
    public void AFamilyWithNoTerminator_RefusesTheSpelling(string name)
        => Assert.Null(MusicMarkItem.ParseSpanEndName(name));

    /// <summary>
    /// The sugar table is the ONE place a word becomes a printed string, and
    /// <see cref="MusicMarkItem.BuildPlain"/> is the one door to it: a text-span START built
    /// any other way has no word, because the TYPE does not carry one.
    /// </summary>
    [Theory]
    [InlineData("rit", "rit.")]
    [InlineData("accel", "accel.")]
    [InlineData("rall", "rall.")]
    public void TheSugarWords_CarryTheirPrintedText(string name, string printed)
    {
        var mark = MusicMarkItem.BuildPlain(name, isSpanEnd: false, measureIndex: 0, sourcePosition: 0);
        Assert.NotNull(mark);
        Assert.Equal(MusicMarkType.TextSpanStart, mark!.Type);
        Assert.Equal(printed, mark.Text);
    }
}
