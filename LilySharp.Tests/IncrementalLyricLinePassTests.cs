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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A sung block hanging off a NON-LAST staff renders the same through the incremental
/// session as through a full compile.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THIS SHAPE WAS THE HOLE IN THE INCREMENTAL==FULL NET, and the hole is what let the
/// defect ship. <c>LyricEngraver.DistributeLooseLines</c> files every verse profile under
/// the ALIGNMENT LINE its syllables stand on — <c>LineKeyOf</c>, which answers the staff
/// index for a block hanging off a non-last staff and -1 otherwise. That question is
/// decided by <c>noteBoundAnchorY</c>, which the PRELIMINARY annotation pass does not
/// have; the two passes therefore file the same syllables under different keys. The
/// verse-skyline memo was ONE store shared by both passes, so the final pass was served
/// entries it could not find and walked its chain with NO SYLLABLE INK.
/// </para>
/// <para>
/// It could only be seen through a cache, i.e. only in the EDITOR: the preview renders
/// through <c>IncrementalCompiler</c>, the only caller that passes a
/// <c>SystemLayoutCache</c>. <c>lysc</c>, every snapshot and every ledger point renders
/// through <c>SvgGenerator.Generate</c> and saw the correct picture. MEASURED on the
/// reported book (scratch/ベースタブLy/Untitled-6.lys, user report 2026-08-25): the
/// syllables sat 4.214000 higher than a full compile put them — through the staff above
/// them on its one-verse systems.
/// </para>
/// <para>
/// ⚠️ THE OTHER SHAPES WERE COVERED AND DID NOT CATCH IT. A lead sheet's block hangs off
/// the LAST staff, where <c>LineKeyOf</c> answers -1 in both passes and the shared store
/// was harmless; a grand staff with <c>\addlyrics</c> under each staff was not in the net
/// at all. What distinguishes this book is only WHICH staff the block hangs off.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class IncrementalLyricLinePassTests
{
    // A sung line between two staves: the block hangs off `melody`, which is NOT the
    // system's last spaceable staff, so LineKeyOf answers that staff's index.
    private const string BetweenStaves = """
        key c major
        part melody {
          clef treble
          section A { c4 d e f | g a b c' | break}
          section B { c'4 b a g | f e d c | }
        }
        lyrics verse {
          section A { one two three four | five six sev- en | }
          section B { eight nine ten e- | lev- en twelve x | }
        }
        form main { A B }
        score main {
          staff melody
          lyrics verse sings melody
          staff melody
        }
        """;

    // CONTROL: the same syllables under the LAST staff, where both passes agree on -1.
    // It passed throughout and is here so a future reader can see which half moved.
    private const string BelowSystem = """
        key c major
        part melody {
          clef treble
          section A { c4 d e f | g a b c' | break}
          section B { c'4 b a g | f e d c | }
        }
        lyrics verse {
          section A { one two three four | five six sev- en | }
          section B { eight nine ten e- | lev- en twelve x | }
        }
        form main { A B }
        score main {
          staff melody
          lyrics verse sings melody
        }
        """;

    [Theory]
    [InlineData(nameof(BetweenStaves))]
    [InlineData(nameof(BelowSystem))]
    public void TheSessionRendersWhatAFullCompileDoes(string which)
    {
        string src = which == nameof(BetweenStaves) ? BetweenStaves : BelowSystem;
        var options = SvgRenderOptions.Preview();
        string Full(string t) => SvgGenerator.Generate(SyntaxTree.Parse(t), options)
            .Replace("\r\n", "\n");

        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, options);

        // The FIRST render already diverged: the preliminary pass fills the store and the
        // final pass reads it, both inside one compile, so this needs no edit to fail.
        Assert.Equal(Full(src), session.RenderIncremental(tree).Replace("\r\n", "\n"));

        // ...and an edit that changes one measure's content keeps them equal, which is
        // what says the per-pass stores still reuse across keystrokes.
        string edited = src.Replace("g a b c' |", "g a b d' |");
        var editedTree = SyntaxTree.Parse(edited);
        Assert.Equal(Full(edited), session.RenderIncremental(editedTree).Replace("\r\n", "\n"));
    }

    /// <summary>
    /// …and the syllables are where the full compile puts them, said as geometry rather
    /// than as a string comparison, so a reader can see WHAT was wrong: they were drawn
    /// through the staff below them.
    /// </summary>
    [Fact]
    public void TheSyllablesAreNotDrawnThroughTheStaffBelowThem()
    {
        var options = SvgRenderOptions.Preview();
        var tree = SyntaxTree.Parse(BetweenStaves);
        string svg = new IncrementalCompiler(tree, options).RenderIncremental(tree);

        var staves = StaffLineGeometry.Staves(svg);
        Assert.True(staves.Count >= 2);
        for (int i = 0; i + 1 < staves.Count; i += 2)
        {
            var lines = StaffLineGeometry.Baselines(svg, staves[i].Bottom, staves[i + 1].Top);
            Assert.True(lines.Count > 0,
                $"system {i / 2} has no syllable between its two staves — they are drawn elsewhere");
        }
    }
}
