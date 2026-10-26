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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LYS1053 (owner's decision 2026-10-26): a <c>chords</c> row entry that is no chord Lily#
/// reads is warned — it had printed as written and played its root alone, with nothing said,
/// where the same symbol in an <c>@chord</c> was warned (LYS1008). The printing and the playback
/// are unchanged.
/// </summary>
[Trait("Category", "Unit")]
public class ChordRowSymbolTests
{
    private static Diagnostic[] Warned(string row)
    {
        string source = "key c major\npart m { clef treble }\n"
            + $"chords prog {{ section A {{ {row} }} }}\n"
            + "section A { m { c'1 | c'1 | c'1 | c'1 | } }\nform { A }\nscore {\n  chords prog\n  staff m\n}\n";
        var tree = SyntaxTree.Parse(source);
        return [.. SemanticValidation.Run(tree).Where(d => d.Code == DiagnosticCodes.ChordSymbolNotRead)];
    }

    [Theory]
    [InlineData("C7b9 |", "plays only its root", "Did you mean '7-9'?")]
    [InlineData("C-5 |", "plays only its root", "C(b5)")]
    [InlineData("Cxyz |", "plays only its root", "'xyz' is no chord quality Lily# knows")]
    public void ARowEntryThatIsNoChord_IsWarned_SayingWhatItDoesAndTheFix(string row, string does, string fix)
    {
        var d = Assert.Single(Warned(row));
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("prints it as written", d.Message);
        Assert.Contains(does, d.Message);
        Assert.Contains(fix, d.Message);
    }

    [Theory]
    [InlineData("C | Am | Cmaj7 | G7/B |")]
    [InlineData("CΔ7 | Cø7 | C°7 | C-7 |")]
    [InlineData("C7(b9, #11) | Cm7(b5) | F#m7-5/C# | B♭7 |")]
    [InlineData("I | V7 | bVII | IIm7 |")]          // Roman degrees read against the key
    [InlineData("C . . . | r | C | G(320003) |")]   // an extension, N.C., a written shape
    public void AChordAChartWrites_IsNotWarned(string row)
        => Assert.Empty(Warned(row));
}
