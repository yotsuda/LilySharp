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
using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The section-name resolver that powers the editor's "rename a section"
/// refactor: it finds a section's declaration and every structure reference —
/// plain, silent (<c>~NAME</c>) and volta (<c>[1. NAME]</c>) — while leaving a
/// per-occurrence display label string untouched.
/// </summary>
[Trait("Category", "Unit")]
public class SectionReferenceFinderTests
{
    private static SyntaxNode Root(string src) => SyntaxTree.Parse(src).GetRoot();

    [Fact]
    public void FindsDeclarationAndPlainReference()
    {
        var root = Root("""
            part m {
              section A { c4 d e f | }
              section B { g4 a b c | }
            }
            form main { A B A }
            score main "s" { staff m }
            """);

        // decl + two plays of A in the structure (A B A).
        Assert.Equal(3, SectionReferenceFinder.Occurrences(root, "A").Count);
        Assert.Equal(2, SectionReferenceFinder.Occurrences(root, "B").Count);
        Assert.All(SectionReferenceFinder.Occurrences(root, "A"), t => Assert.Equal("A", t.Text));
    }

    [Fact]
    public void SilentAndVoltaReferencesAreCollected()
    {
        var root = Root("""
            part m {
              section A { c4 d e f | }
              section B { g4 a b c | }
            }
            form main { A |: B :| [1. ~A] [2. B] }
            score main "s" { staff m }
            """);

        // A: decl + plain + volta-silent [1. ~A].
        Assert.Equal(3, SectionReferenceFinder.Occurrences(root, "A").Count);
        // B: decl + plain (inside repeat) + volta [2. B].
        Assert.Equal(3, SectionReferenceFinder.Occurrences(root, "B").Count);
    }

    [Fact]
    public void DisplayLabelStringIsNotRenamed()
    {
        var src = """
            part m { section A { c4 } }
            form main { A "A (reprise)" }
            score main "s" { staff m }
            """;
        var root = Root(src);

        // decl + the single labelled reference — the "A (reprise)" string is not a
        // section token.
        Assert.Equal(2, SectionReferenceFinder.Occurrences(root, "A").Count);
        int labelStart = src.IndexOf("\"A (reprise)\"");
        Assert.Null(SectionReferenceFinder.SectionNameTokenAt(root, labelStart + 1));
    }

    [Fact]
    public void CaretOnReferenceResolvesToTheName()
    {
        var src = "part m { section Intro { c4 } }\nform main { Intro }\nscore \"s\" { staff m }";
        var root = Root(src);
        // Caret on the reference inside form main { Intro }.
        int caret = src.LastIndexOf("Intro") + 2;
        var tok = SectionReferenceFinder.SectionNameTokenAt(root, caret);
        Assert.NotNull(tok);
        Assert.Equal("Intro", tok!.Text);
    }
}
