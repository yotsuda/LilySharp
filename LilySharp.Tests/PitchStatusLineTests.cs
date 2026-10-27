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

using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The caret's status line on a <c>pitch</c> (owner's request 2026-10-28: "pitch concert" reads as
/// unclear — the word stays, and the status bar says what it means where the caret rests).
/// </summary>
[Trait("Category", "Unit")]
public class PitchStatusLineTests
{
    private const string Text = """
        pitch concert
        time 4/4
        part tp { instrument trumpet pitch written }
        part bass { instrument bass }
        section A { tp { c'1 | } bass { e,,1 | } }
        form { A }
        score { staff tp  staff bass }
        score trumpet pitch transposed { staff tp as pitch concert }
        """;

    private static string? At(string marker, int into = 1)
        => NoteStepper.Describe(Text, SyntaxTree.Parse(Text), Text.IndexOf(marker, System.StringComparison.Ordinal) + into);

    [Fact]
    public void TheFilesPitch_SaysHowTheNotesAreTyped()
    {
        var said = At("pitch concert\n");
        Assert.NotNull(said);
        Assert.StartsWith("pitch concert: the notes are written as they SOUND (concert pitch)", said);
    }

    [Fact]
    public void APartsPitch_SaysItIsThatPartsAlone()
    {
        var said = At("pitch written }", 8);
        Assert.NotNull(said);
        Assert.StartsWith("pitch written: this part's notes are written as each player READS them", said);
    }

    [Fact]
    public void AScoresAndAStaffsPitch_SayHowThePagePrints()
    {
        Assert.StartsWith("pitch transposed: this page prints each transposing part as its player reads it",
            At("pitch transposed", 8));
        Assert.StartsWith("pitch concert: this page prints every part as it sounds", At("as pitch concert", 10));
    }
}
