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

using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A part header's key (<c>part p { key bes major … }</c>, outside its sections) is the key
/// the part OPENS in and the key a section that states none RETURNS to — on the page
/// (MeasureCollector.GetPartDefaults) and in the LilyPond twin (HANDOFF §2 R12⒞, session 570).
/// </summary>
/// <remarks>
/// Before: <see cref="ScoreHomeKey"/> read the part header's key as the SCORE's home key, and
/// the twin never wrote it at the part's head — it opened in C and restored B♭ at section C
/// only because the misread home happened to be B♭. The phrase auto-transpose ambient stays
/// at the file's home on both sides (the page's ResetAmbientTonicToHome reads the file-level
/// key), so the part header's key is display only.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PartHeaderKeyTwinTests
{
    private const string Book = """
        time 4/4
        part m {
          key bes major
          section A { c'4 d e f | }
          section B { key d major g4 a b c | }
          section C { c4 d e f | }
        }
        part n {
          section A { c'4 d e f | }
          section B { key d major g4 a b c | }
          section C { c4 d e f | }
        }
        form main { A B C }
        score main { staff m  staff n }
        """;

    private static string Twin() => new LilyPondExporter().Export(SyntaxTree.Parse(Book));

    // The part's music variable, from its assignment to the next top-level assignment.
    private static string PartVariable(string ly, string part)
    {
        var m = Regex.Match(ly, "(?ms)^" + part + "\\w* = .*?(?=^\\S+ = |\\z)");
        Assert.True(m.Success, "no variable for part " + part + " in:\n" + ly);
        return m.Value;
    }

    private static string[] Keys(string music) =>
        [.. Regex.Matches(music, @"\\key \w+ \\\w+").Select(k => k.Value)];

    [Fact]
    public void ThePartOpensInItsHeaderKey_AndSectionCReturnsToIt()
    {
        string m = PartVariable(Twin(), "m");
        Assert.Equal(["\\key bes \\major", "\\key d \\major", "\\key bes \\major"], Keys(m));
        Assert.True(m.IndexOf("\\key bes \\major") < m.IndexOf("c4"), m);
    }

    [Fact]
    public void APartWithoutAHeaderKey_ReturnsToTheFilesKey()
    {
        string n = PartVariable(Twin(), "n");
        Assert.Equal(["\\key d \\major", "\\key c \\major"], Keys(n));
    }

    [Fact]
    public void TheScoreHomeKeyIsTheFilesNotThePartHeaders()
    {
        var root = SyntaxTree.Parse(Book).GetRoot();
        Assert.Null(ScoreHomeKey.Declaration(root));
        Assert.Equal(KeyTonic.CMajor, ScoreHomeKey.Read(root));
    }
}
