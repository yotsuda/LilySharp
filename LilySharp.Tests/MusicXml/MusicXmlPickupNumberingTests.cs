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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The measure numbers of a part that opens with a pickup run 0, 1, 2, … across its section
/// plays. Until 2026-10-03 a later play of the part resumed the count at "bars written so
/// far + 1", which is one too many once bar 0 exists: a by-section book whose first section
/// is the pickup alone exported 0, 2, 3 (HANDOFF §1.0 ⑹, `I'm Your Man`), and one whose
/// first section holds the pickup and eight bars skipped 9 at its second section
/// (`Greensleeves`). The page numbers the same bars 0, 1, 2 … (BarNumberEngraver).
/// </summary>
public class MusicXmlPickupNumberingTests
{
    private static int[] Numbers(string source, string part = "m")
        => new MusicXmlExporter().Export(SyntaxTree.Parse(source))
            .Parts.Single(p => p.Name == part).Measures.Select(x => x.Number).ToArray();

    /// <summary>The pickup alone in the first section (the p752 book): 0, 1, 2.</summary>
    [Fact]
    public void AFirstSectionThatIsThePickupAlone_IsFollowedByBar1()
    {
        var numbers = Numbers("""
            octave absolute
            time 4/4
            part m
            section P { partial 2  m { r8 c'8 d' e' } }
            section Q { m { f'4 g' a' b' | c''1 | } }
            form { P Q }
            score { staff m }
            """);
        Assert.Equal(new[] { 0, 1, 2 }, numbers);
    }

    /// <summary>The pickup and two bars in the first section, then a second section: the
    /// second section's first bar is 3, not 4.</summary>
    [Fact]
    public void ASecondSection_ContinuesTheCountAfterAPickup()
    {
        var numbers = Numbers("""
            octave absolute
            time 4/4
            part m
            section A { partial 4  m { c'4 | d'1 | e'1 | } }
            section B { m { f'1 | g'1 | } }
            form { A B }
            score { staff m }
            """);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, numbers);
    }

    /// <summary>The control: without a pickup the count was never off — 1, 2, 3, 4.</summary>
    [Fact]
    public void ASecondSection_WithoutAPickup_CountsAsBefore()
    {
        var numbers = Numbers("""
            octave absolute
            time 4/4
            part m
            section A { m { d'1 | e'1 | } }
            section B { m { f'1 | g'1 | } }
            form { A B }
            score { staff m }
            """);
        Assert.Equal(new[] { 1, 2, 3, 4 }, numbers);
    }
}
