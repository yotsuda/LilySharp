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

using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class SectionOrientedTests
{
    [Fact]
    public void ParsePhraseDeclaration()
    {
        var source = "phrase guitar_riff { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseSectionDeclaration()
    {
        var source = """
            section Intro {
                guitar { c4 d e f }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseSectionWithMultipleParts()
    {
        var source = """
            section A {
                guitar { c4 d e f }
                bass { c,4 g, c, g, }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseSectionWithKeyAndTempo()
    {
        var source = """
            section Intro {
                key c major
                tempo 120
                guitar { c4 d e f }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseStructureDeclaration()
    {
        var source = """
            section A { guitar { c4 } }
            structure {
                A
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseStructureWithNavigationMarks()
    {
        var source = """
            section A { guitar { c4 } }
            section B { guitar { d4 } }
            structure {
                A
                segno
                B
                dc al fine
                fine
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseRenderDeclaration()
    {
        var source = """
            section A { guitar { c4 } }
            structure { A }
            render full "output.svg" {
                staff { guitar }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseRenderWithTabAndStaff()
    {
        var source = """
            section A { guitar { c4 d e f } }
            structure { A }
            render guitarPart "guitar.pdf" {
                staff { guitar }
                tab guitar { guitar }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseRenderMidi()
    {
        var source = """
            section A { guitar { c4 } }
            structure { A }
            render audio "song.mid" {
                guitar channel 1 instrument 25
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseCompleteFile()
    {
        var source = """
            title "Test Song"
            tempo 120
            time 4/4
            key c major

            phrase guitar_riff { c4 d e f }

            section Intro {
                guitar { guitar_riff }
                bass { c,4 g, c, g, }
            }

            section A {
                key g major
                guitar { g4 a b c' }
                bass { g,4 d, g, d, }
            }

            structure {
                Intro
                A
                fine
            }

            render full "test.svg" {
                staff { guitar }
                tab guitar { guitar }
                staff bass { bass }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
}
