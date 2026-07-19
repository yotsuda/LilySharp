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
using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Svg;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Visual + geometric pins for hara-kiri (RemoveEmpty) rendering. The M2
/// fixes (per-system staff-Y routing for annotations, per-system heights in
/// page spacing) landed without any visual fixture because RemoveEmpty is not
/// reachable from the .lys grammar — these tests build the score through the
/// API and pin the rendering as a PROGRAMMATIC snapshot, reviewable through
/// the same visual-diff harness as the .lys fixtures
/// (docs/visual-regression.md).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hara-kiri-group-spanner.cc:61-105 (per-system suicide),
/// lily/page-layout-problem.cc:1080-1118 (per-system extents).
/// </remarks>
[Trait("Category", "Visual")]
public class HaraKiriVisualTests
{
    private static NoteItem Note(int pos) => new(pos, Fraction.Quarter, 0, null, false, 0);

    private static Measure Notes(bool breakAfter = false, int basePos = 0) =>
        new(ImmutableArray.Create<MusicItem>(
                Note(basePos), Note(basePos + 1), Note(basePos + 2), Note(basePos + 1)),
            BarlineType.None, BarlineType.Single, null, 0, 0, hasBreakAfter: breakAfter);

    private static Measure Rests() =>
        new(ImmutableArray.Create<MusicItem>(
                new RestItem(Fraction.Quarter, 0, 0), new RestItem(Fraction.Quarter, 0, 0),
                new RestItem(Fraction.Quarter, 0, 0), new RestItem(Fraction.Quarter, 0, 0)),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    /// <summary>
    /// 3 systems × 2 measures on a grand staff. The RemoveEmpty bass staff is
    /// empty (hidden) in systems 1 and 3 and plays only in system 2, with an
    /// "f" on the bass staff there and a "p" on the treble staff in system 3 —
    /// the per-system visibility change plus staff-routed annotations that the
    /// M2 defects mis-rendered.
    /// </summary>
    private static (MultiStaffScore Score, ScoreLayout Layout, string Svg) Build(
        LayoutOptions? options = null)
    {
        var rh = new Staff(ClefType.Treble, ImmutableArray.Create(new Voice("rh",
            ImmutableArray.Create(
                Notes(), Notes(breakAfter: true),
                Notes(basePos: 2), Notes(breakAfter: true, basePos: 2),
                Notes(), Notes()))));
        var lh = new Staff(ClefType.Bass, ImmutableArray.Create(new Voice("lh",
            ImmutableArray.Create(
                Rests(), Rests(),
                Notes(basePos: -2), Notes(basePos: -2),
                Rests(), Rests()))),
            // RemoveFirst too (LP RemoveAllEmptyStaves): the FIRST system also
            // hides, giving the 1-staff / 2-staff / 1-staff shape the geometric
            // assertions pin. Plain RemoveEmpty keeps the first system's staves
            // (LP RemoveEmptyStaves).
            RemoveEmpty: true,
            RemoveFirst: true);

        var score = new MultiStaffScore(
            ImmutableArray.Create(new StaffGroup(StaffGroupType.GrandStaff,
                ImmutableArray.Create(rh, lh))),
            new TimeSignature(4, 4), KeySignature.CMajor,
            dynamics: ImmutableArray.Create(
                new DynamicItem(DynamicLevel.F, measureIndex: 3, itemIndex: 0,
                    sourcePosition: 0, staffIndex: 1),
                new DynamicItem(DynamicLevel.P, measureIndex: 4, itemIndex: 0,
                    sourcePosition: 0, staffIndex: 0)));

        var layout = new LayoutEngine(options).Layout(score);
        var doc = new SvgDocumentContext(new SvgDocumentOptions { EmbedFont = false });
        SharedRenderer.RenderTo(score, layout, doc);
        doc.Dispose();
        return (score, layout, doc.ToSvg().Replace("\r\n", "\n"));
    }

    [Fact]
    public void HiddenStaffSystems_AreCompact_AndAnnotationsFollowTheirStaff()
    {
        var (_, layout, _) = Build();
        var systems = layout.AllSystems;
        Assert.Equal(3, systems.Length);

        // M2 Defect B: page spacing is per system — the two-staff system
        // occupies visibly more room than the single-staff ones around it.
        // system.Y is page Y-up (W2-core): the earlier (upper) system has the LARGER
        // Y, so the space a system occupies is the previous-minus-next difference.
        double gap01 = systems[0].Y - systems[1].Y; // spans system 1 (1 staff)
        double gap12 = systems[1].Y - systems[2].Y; // spans system 2 (2 staves)
        Assert.True(gap12 > gap01 + 1.0,
            $"the two-staff system must need more room: gap01={gap01:F2}, gap12={gap12:F2}");

        // M2 Defect A: the bass-staff "f" in system 2 resolves that system's
        // OWN staff table — it must sit at/below the bass staff, well past the
        // treble staff's bottom (the defect put it at the system-0 table's Y).
        var f = layout.DynamicLayouts.Single(d => d.Text == "f");
        // Everything is page Y-up now (W2-core). The dynamic's drawn page Y-up is its
        // bass staff's middle Y-up plus its stored Y-up offset (as DrawDynamics emits).
        // Lower on the page = SMALLER Y-up, so "below" comparisons use <.
        double fAbsY = LayoutUtilities.ResolveStaffMiddleY(systems[1], f.StaffIndex, 4.0) + f.YUp;
        double trebleBottom = LayoutUtilities.FindStaffYInSystem(systems[1], 0) - 4.0;
        double bassTop = LayoutUtilities.FindStaffYInSystem(systems[1], 1);
        Assert.True(bassTop < trebleBottom,
            $"fixture must show both staves in system 2 (bassTop={bassTop:F2}, trebleBottom={trebleBottom:F2})");
        Assert.True(fAbsY < trebleBottom,
            $"the bass-staff dynamic must be below the treble staff: fY={fAbsY:F2}, trebleBottom={trebleBottom:F2}");
    }

    [Fact]
    public void Rendering_MatchesTheProgrammaticBaseline()
    {
        var (_, _, svg) = Build();
        ProgrammaticSnapshot.Assert("programmatic/hara-kiri", svg);
    }

    [Fact]
    public void PagedRendering_MatchesTheProgrammaticBaseline()
    {
        // The same score paged: exercises the per-system heights in the page
        // breaker (incl. the TopExtent continuation rod) with a hidden-staff
        // system on each page boundary.
        var (_, layout, svg) = Build(new LayoutOptions
        {
            PageHeight = 30,
            UseOptimalPageBreaking = true,
        });
        Xunit.Assert.True(layout.Pages.Length >= 2,
            $"expected a multi-page layout, got {layout.Pages.Length} page(s)");
        ProgrammaticSnapshot.Assert("programmatic/hara-kiri-paged", svg);
    }
}

/// <summary>
/// Snapshot gate for scores built through the API (features the .lys grammar
/// cannot reach yet, e.g. RemoveEmpty). Same contract as SvgSnapshotTests:
/// byte-identical against a committed baseline, LILYSHARP_UPDATE_SNAPSHOTS=1
/// or tools/Approve-Snapshots.ps1 to bless, and every difference (or newly
/// created baseline) lands in the visual-diff report for human review.
/// </summary>
internal static class ProgrammaticSnapshot
{
    private static readonly bool UpdateSnapshots =
        Environment.GetEnvironmentVariable("LILYSHARP_UPDATE_SNAPSHOTS") == "1";

    private static string SnapshotsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "LilySharp.Tests", "Snapshots");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Snapshots/");
    }

    public static void Assert(string name, string svg)
    {
        var fileName = name.Replace("/", "__").Replace("\\", "__") + ".svg";
        var path = Path.Combine(SnapshotsDir(), fileName);

        if (UpdateSnapshots || !File.Exists(path))
        {
            File.WriteAllText(path, svg);
            if (!UpdateSnapshots)
            {
                // A brand-new baseline still needs eyes: put its rendering in
                // the visual-diff report (baseline == actual, 0 diff) so the
                // reviewer can inspect the PNG before committing it.
                string visual;
                try
                {
                    visual = "Review the rendering: " + Svg.VisualDiffReport.Record(name, svg, svg);
                }
                catch (Exception ex)
                {
                    visual = $"(rendering review unavailable: {ex.GetType().Name})";
                }
                Xunit.Assert.Fail(
                    $"Programmatic snapshot baseline created: {fileName}. {visual}\n" +
                    "Inspect it, then re-run to verify against the new baseline.");
            }
            return;
        }

        var baseline = File.ReadAllText(path).Replace("\r\n", "\n");
        if (svg != baseline)
        {
            string visual;
            try
            {
                visual = "Visual diff report (open in a browser): " +
                         Svg.VisualDiffReport.Record(name, baseline, svg);
            }
            catch (Exception ex)
            {
                visual = $"Visual diff report unavailable ({ex.GetType().Name}: {ex.Message})";
            }
            Xunit.Assert.Fail(
                $"Programmatic snapshot mismatch for '{name}'.\n" + visual + "\n" +
                $"Approve THIS fixture: pwsh tools/Approve-Snapshots.ps1 -Name {name}\n" +
                "Approve ALL: set LILYSHARP_UPDATE_SNAPSHOTS=1 and re-run.\n" +
                $"Baseline: {path}");
        }
    }
}
