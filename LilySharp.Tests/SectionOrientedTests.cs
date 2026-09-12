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
    public void ParseFormDeclaration()
    {
        var source = """
            section A { guitar { c4 } }
            form main {
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
            form main {
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
            form main { A }
            score main "output" {
                staff guitar
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
            form main { A }
            score main "guitar" {
                staff guitar
                tab guitar guitar
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    /// <summary>
    /// A bare part name in a score body is the MIDI-only row: that part is played and never
    /// engraved (GRAMMAR §7 <c>ScoreItem = … | PartRef</c>).
    /// </summary>
    /// <remarks>
    /// ⚠️ This test read <c>guitar octave 1 instrument 25</c> until 2026-09-12 and asserted
    /// only that it PARSED — it was the retired <c>instrument:25</c> era's own net, and the
    /// MIDI program number in it is the giveaway. Nothing ever read those options
    /// (<c>MidiExporter</c> takes both from the part's properties; six spellings exported
    /// identical notes), no <c>.lys</c> on this machine wrote one, and the grammar never
    /// listed them, so they were removed from the parser. Both halves are asserted here now:
    /// the row parses, and the retired options are refused.
    /// </remarks>
    [Fact]
    public void ParseRenderMidi()
    {
        var source = """
            section A { guitar { c4 } }
            form main { A }
            score main "song" {
                guitar
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        // ⚠️ Written out, not patched into `source` with a Replace: the raw literal carries
        // this file's CRLF, so a `\n` needle silently misses and the assertion below passes
        // on the UNCHANGED text (measured — it went green against the wrong input once).
        var retired = SyntaxTree.Parse("""
            section A { guitar { c4 } }
            form main { A }
            score main "song" {
                guitar octave 1 instrument 25
            }
            """);
        Assert.True(retired.HasErrors);
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

            form main {
                Intro
                A
                fine
            }

            score main "test" {
                staff guitar
                tab guitar guitar
                staff bass bass
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
}
