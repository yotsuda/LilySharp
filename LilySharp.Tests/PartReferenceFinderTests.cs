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
/// The part-name resolver that powers the editor's "rename a part" refactor:
/// it finds a part's declaration and every reference (section-body part blocks,
/// score staff/ossia/tab targets, bare-name midi renders) while ignoring tokens
/// the compiler reads as something else (clef words, display names, chord parts).
/// </summary>
[Trait("Category", "Unit")]
public class PartReferenceFinderTests
{
    private static SyntaxNode Root(string src) => SyntaxTree.Parse(src).GetRoot();

    [Fact]
    public void FindsDeclarationSectionBlockAndStaffRender()
    {
        var root = Root("""
            part rh { clef treble }
            part lh { clef bass }
            section A {
              rh { c4 d e f | }
              lh { c2 g | }
            }
            form main { A }
            score main "s" {
              grandStaff { staff rh  staff lh }
            }
            """);

        // decl + section block + grand-staff render item.
        Assert.Equal(3, PartReferenceFinder.Occurrences(root, "rh").Count);
        Assert.Equal(3, PartReferenceFinder.Occurrences(root, "lh").Count);
        Assert.All(PartReferenceFinder.Occurrences(root, "rh"), t => Assert.Equal("rh", t.Text));
    }

    [Fact]
    public void StaffClefKeywordIsNotThePartToken()
    {
        var src = """
            part lh { clef bass }
            section A { lh { c2 } }
            score main "s" { staff bass lh }
            """;
        var root = Root(src);

        // Three "lh" occurrences; the `bass` clef word before the render part is
        // never one of them.
        Assert.Equal(3, PartReferenceFinder.Occurrences(root, "lh").Count);
        Assert.Empty(PartReferenceFinder.Occurrences(root, "bass"));

        // Caret on the render's `bass` clef word resolves to no part;
        // caret on the following `lh` resolves to the part token.
        int bassInRender = src.LastIndexOf("bass");
        int lhInRender = src.LastIndexOf("lh");
        Assert.Null(PartReferenceFinder.PartNameTokenAt(root, bassInRender));
        Assert.Equal("lh", PartReferenceFinder.PartNameTokenAt(root, lhInRender)?.Text);
    }

    [Fact]
    public void WithChordsTailIsNotRenamedAsAPart()
    {
        var src = """
            part m { clef treble }
            chords ch { c1 }
            section A { m { c4 } }
            score main "s" { staff m with chords ch }
            """;
        var root = Root(src);

        // The staff's part token is `m`; the `ch` after `with chords` is a chord
        // part in a different namespace and must not be collected as a part ref.
        Assert.Equal(3, PartReferenceFinder.Occurrences(root, "m").Count);
        int chInRender = src.LastIndexOf("ch");
        Assert.Null(PartReferenceFinder.PartNameTokenAt(root, chInRender));
    }

    [Fact]
    public void TildeAndDisplayNameAreSkipped()
    {
        var root = Root("""
            section A { flute { c4 } }
            score main "s" { staff ~flute "Flöte" }
            """);

        // section block + staff render — the `~` suppressor and the "Flöte"
        // display string are not part tokens.
        var occ = PartReferenceFinder.Occurrences(root, "flute");
        Assert.Equal(2, occ.Count);
        Assert.All(occ, t => Assert.Equal("flute", t.Text));
        Assert.Empty(PartReferenceFinder.Occurrences(root, "Flöte"));
    }

    [Fact]
    public void OssiaAndTabTargetsAreCollected()
    {
        var ossia = Root("""
            section A {
              melody { c4 d e f | }
              ossia_melody { r1 | }
            }
            form main { A }
            score main "s" { staff melody  ossia ossia_melody }
            """);
        Assert.Equal(2, PartReferenceFinder.Occurrences(ossia, "ossia_melody").Count);

        var tab = Root("""
            part bl { clef treble_8 }
            section A { bl { c4 } }
            score main "s" { tab bl }
            """);
        Assert.Equal(3, PartReferenceFinder.Occurrences(tab, "bl").Count);
    }

    /// <summary>
    /// `tab NAME as numbers | full` — the tab STYLE selector, which
    /// RenderSpecParser.ParseTab strips before reading the part. Taking the last target
    /// token flat read `numbers` as the part: the score failed semantic validation with
    /// LYS1007 "Undefined part: 'numbers'" (the committed fixture test/tab-as-numbers.lys
    /// would not render through the CLI), and a rename would have rewritten the selector.
    /// </summary>
    [Fact]
    public void TabStyleSelectorIsNotThePartToken()
    {
        var src = """
            part m { clef treble }
            section A { m { c4 } }
            score main "s" { staff m  tab m as numbers }
            """;
        var root = Root(src);

        // decl + section block + staff render + tab render.
        Assert.Equal(4, PartReferenceFinder.Occurrences(root, "m").Count);
        Assert.Empty(PartReferenceFinder.Occurrences(root, "numbers"));
        Assert.Null(PartReferenceFinder.PartNameTokenAt(root, src.LastIndexOf("numbers")));
        // The tab render's own part token is the `m` before the selector.
        int mInTab = src.LastIndexOf("m as numbers");
        Assert.Equal("m", PartReferenceFinder.PartNameTokenAt(root, mInTab)?.Text);
    }

    /// <summary>
    /// Both selectors at once (`tab m as numbers with chords h as both`): the chord part
    /// `h` belongs to a different namespace and neither display word is a part.
    /// </summary>
    [Fact]
    public void TabStyleAndChordDisplaySelectorsAreBothSkipped()
    {
        var src = """
            part m { clef treble }
            chords h { c1 }
            section A { m { c4 } }
            score main "s" { tab m as numbers with chords h as both }
            """;
        var root = Root(src);

        Assert.Equal(3, PartReferenceFinder.Occurrences(root, "m").Count);
        Assert.Empty(PartReferenceFinder.Occurrences(root, "numbers"));
        Assert.Empty(PartReferenceFinder.Occurrences(root, "both"));
        Assert.Null(PartReferenceFinder.PartNameTokenAt(root, src.LastIndexOf("h ")));
    }

    /// <summary>A tuning override keeps the part as the token after it, selector or not.</summary>
    [Fact]
    public void TabTuningOverrideIsNotThePartToken()
    {
        var src = """
            part gt { clef treble_8 }
            section A { gt { c4 } }
            score main "s" { tab bass gt as full }
            """;
        var root = Root(src);

        Assert.Equal(3, PartReferenceFinder.Occurrences(root, "gt").Count);
        Assert.Empty(PartReferenceFinder.Occurrences(root, "full"));
        Assert.Null(PartReferenceFinder.PartNameTokenAt(root, src.LastIndexOf("bass")));
    }

    [Fact]
    public void CaretOnDeclarationResolvesToTheName()
    {
        var src = "part rh { clef treble }\nsection A { rh { c4 } }\nscore \"s\" { staff rh }";
        var root = Root(src);
        int caret = src.IndexOf("rh") + 1; // inside `part rh`'s name
        var tok = PartReferenceFinder.PartNameTokenAt(root, caret);
        Assert.NotNull(tok);
        Assert.Equal("rh", tok!.Text);
    }
}
