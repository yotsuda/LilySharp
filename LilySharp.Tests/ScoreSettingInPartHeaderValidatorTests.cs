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
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>tempo</c> and <c>time</c> are score-level — every part shares one tempo and meter — so a
/// part-header attribute (<c>part melody { tempo 120 … }</c>) is rejected. Their homes are the
/// top level (opening value) and a section header (a change applying to every part); a change
/// inside the music stream is left alone.
/// </summary>
[Trait("Category", "Unit")]
public class ScoreSettingInPartHeaderValidatorTests
{
    private static int ErrCount(string src)
        => SemanticValidation.Run(SyntaxTree.Parse(src))
            .Count(d => d.Code == DiagnosticCodes.ScoreSettingInPartHeader);

    [Fact]
    public void PartHeaderTempo_Errors()
        => Assert.Equal(1, ErrCount(
            "part melody { tempo 120  clef treble  section A { c1 } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void PartHeaderTime_Errors()
        => Assert.Equal(1, ErrCount(
            "part melody { time 3/4  clef treble  section A { c2. } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void BothInOnePartHeader_ErrorEach()
        => Assert.Equal(2, ErrCount(
            "part melody { tempo 120  time 3/4  section A { c2. } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void GlobalTempoAndTime_Ok()
        => Assert.Equal(0, ErrCount(
            "time 4/4\ntempo 100\npart melody { clef treble  section A { c1 } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void SectionHeaderTempo_Ok()
        // A tempo change stated in a top-level section header applies to every part — allowed.
        => Assert.Equal(0, ErrCount(
            "tempo 100\npart melody { section A { c1 } section B { d1 } }\nsection B { tempo 132 }\n"
            + "form main { A B }\nscore main { staff melody }"));

    [Fact]
    public void MidMusicTempoInPartSection_Ok()
        // A tempo written INSIDE the music (a part's inner section) is a mid-piece change, not a
        // header attribute — not flagged.
        => Assert.Equal(0, ErrCount(
            "tempo 100\npart melody { section A { c2 tempo 132 c2 } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void PartPropertyCompletion_DoesNotOfferTempoOrTime()
    {
        var labels = LilySharpLanguageServer.GetPartPropertyCompletions().Items.Select(i => i.Label).ToArray();
        Assert.DoesNotContain("tempo", labels);
        Assert.DoesNotContain("time", labels);
        Assert.Contains("clef", labels);   // real part properties stay
    }
}
