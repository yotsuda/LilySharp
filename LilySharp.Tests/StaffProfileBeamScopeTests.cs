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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The above/below outside-staff passes side-position against ONE staff's own profile
/// (<c>LayoutEngine</c>'s <c>staffProfile</c> delegate). This asserts the scope of what that
/// profile reserves: THIS system's beams for THIS staff, and no others.
/// </summary>
/// <remarks>
/// The defect this guards was invisible to the whole suite for a session. The score-wide beam
/// array holds every system's beams, each carrying the X its own system laid it out at, and
/// those ranges overlap — every system starts near x 0. Selecting on the staff alone therefore
/// seeded system 0's profile with system 1's beam ink, and the only witness was a snapshot that
/// got APPROVED, on the reading that the system silhouette (which was right) had lost its music.
/// <para>
/// LILYPOND-REF: lily/axis-group-interface.cc:914-950 <c>Axis_group_interface::skyline_spacing</c>
/// builds <c>inside_staff_skylines</c> from the elements of the ONE axis group it was called on
/// (<c>calc_skylines</c>:479-482 calls it per grob), so a System's axis groups never share ink.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class StaffProfileBeamScopeTests
{
    private static string LoadFixture(string rel)
    {
        var dir = System.AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (System.IO.Directory.Exists(candidate))
                return System.IO.File.ReadAllText(System.IO.Path.Combine(candidate,
                        rel.Replace('/', System.IO.Path.DirectorySeparatorChar) + ".lys"))
                    .Replace("\r\n", "\n");
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        throw new System.IO.DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures/");
    }

    [Fact]
    public void StaffProfileBeams_AreScopedToTheirOwnSystem()
    {
        // test/notes is the witness: two systems, and the FIRST holds no beamed note at all
        // (whole/half/quarter bars) while the second holds the eighths and sixteenths. So a
        // leak from system 1 into system 0 shows as ink where there is none to have.
        var tree = SyntaxTree.Parse(LoadFixture("test/notes"));
        var spec = RenderSpecParser.FindFirst(tree)!;
        var score = new MeasureCollector().CollectMultiStaff(tree, spec);
        var layout = new LayoutEngine().Layout(score);

        Assert.True(layout.Systems.Length >= 2,
            "the fixture must span two systems or this test is vacuous");

        // EVERY beam knows which system it is in, and the stamp agrees with the only other
        // witness of the same fact — the system whose measure range holds the beam's measure.
        // Both halves matter: the first would pass on an all-zero stamp, the second on a
        // stamp nobody set.
        foreach (var beam in layout.BeamLayouts)
        {
            var owner = layout.Systems.SingleOrDefault(
                s => s.Measures.Any(m => m.MeasureIndex == beam.Group.MeasureIndex));
            Assert.NotNull(owner);
            Assert.Equal(owner!.SystemIndex, beam.SystemIndex);
        }

        // ...and the fixture still WITNESSES the defect this guards: selecting on the staff
        // alone — the spelling that shipped in a1d22431 — really does hand system 0 beams it
        // does not own. Without this the assertions above could pass on a single-system
        // fixture, where there is nothing to leak.
        var staffOnly = layout.BeamLayouts.Count(b => b.StaffIndex == 0);
        var firstSystemOnly = layout.BeamLayouts.Count(
            b => b.StaffIndex == 0 && b.SystemIndex == 0);
        Assert.True(staffOnly > firstSystemOnly,
            $"expected the staff-only selection ({staffOnly}) to over-collect for the first "
            + $"system ({firstSystemOnly}); the fixture no longer witnesses the leak");
    }
}
