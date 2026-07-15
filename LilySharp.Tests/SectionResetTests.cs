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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section is self-contained: at its boundary the octave frame, default
/// duration, meter, key and clef all revert to the score/part level, so a
/// mid-section meter/key/clef change cannot leak into the next section (nor into
/// the same section reused elsewhere by the form). When a prior section actually
/// left a different value, the revert is drawn (a change grob) rather than
/// silently leaving the old signature/clef on the staff.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SectionResetTests
{
    private static Measure[] Collect(string source) =>
        new MeasureCollector().Collect(SyntaxTree.Parse(source), "m").Voice.Measures.ToArray();

    [Fact]
    public void MidSectionMeterChange_DoesNotLeakIntoNextSection()
    {
        // A ends in 3/4; B declares nothing, so it reverts to the score meter (4/4)
        // and redraws it. Without the reset B would inherit 3/4 and `c4 d e f`
        // would spill a bar.
        var measures = Collect("""
            time 4/4
            part m {
              section A { c1 | time 3/4 c4 d e | }
              section B { c4 d e f | }
            }
            form main { A B }
            score main { staff m }
            """);

        // A: [c1], [3/4 change + c d e]. B starts at measure index 2.
        var bReset = measures[2].Items.OfType<TimeSignatureChangeItem>().FirstOrDefault();
        Assert.NotNull(bReset);
        Assert.Equal(4, bReset!.NewTime.Beats);
        Assert.Equal(4, bReset.NewTime.BeatType);
        // c4 d e f fits one 4/4 bar, so B is exactly one measure (index 2, the last).
        Assert.Equal(3, measures.Length);
    }

    [Fact]
    public void MidSectionKeyChange_DoesNotLeakIntoNextSection()
    {
        // A modulates to G major; B declares nothing, so it reverts to the score
        // key (C major) and redraws the cancellation.
        var measures = Collect("""
            part m {
              section A { key g major c1 | }
              section B { c1 | }
            }
            form main { A B }
            score main { staff m }
            """);

        var bReset = measures[1].Items.OfType<KeySignatureChangeItem>().FirstOrDefault();
        Assert.NotNull(bReset);
        Assert.Equal(0, bReset!.NewKey.Sharps); // reverted from G major (1 sharp) to C major
    }

    [Fact]
    public void MidSectionClefChange_DoesNotLeakIntoNextSection()
    {
        // The part is bass; A opens with its own `clef treble`; B declares nothing, so it
        // reverts to the part clef (bass) and redraws it. Without the reset B would inherit
        // A's treble clef and render on the wrong staff lines.
        var measures = Collect("""
            part m {
              clef bass
              section A { clef treble c1 | }
              section B { c1 | }
            }
            form main { A B }
            score main { staff m }
            """);

        var bReset = measures[1].Items.OfType<ClefChangeItem>().FirstOrDefault();
        Assert.NotNull(bReset);
        Assert.Equal(ClefType.Bass, bReset!.NewClef); // reverted from treble back to the part clef
    }

    [Fact]
    public void NoMidSectionChange_EmitsNoResetGrob()
    {
        // Common case: nothing changed mid-section, so the boundary reset is a
        // no-op — no phantom meter/key/clef grobs at the section start.
        var measures = Collect("""
            time 4/4
            key g major
            part m {
              clef bass
              section A { c1 | }
              section B { c1 | }
            }
            form main { A B }
            score main { staff m }
            """);

        Assert.DoesNotContain(measures[1].Items, i => i is TimeSignatureChangeItem);
        Assert.DoesNotContain(measures[1].Items, i => i is KeySignatureChangeItem);
        Assert.DoesNotContain(measures[1].Items, i => i is ClefChangeItem);
    }
}
