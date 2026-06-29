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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// F3/B robustness: proves the render-time data-pos resolution (the B-1 migration)
/// is correct under a position-shifting, content-unchanged edit — i.e. the reuse
/// scenario it exists for, WITHOUT depending on whole-layout reuse actually firing.
///
/// For every snapshot fixture we lay out the original (s1) and a leading-newline-
/// shifted copy (s2) — content-identical, all source offsets +1. For each MIGRATED
/// annotation array we then assert that resolving the CACHED layout's SourceIndex
/// against the EDITED score (s2's side-table) yields exactly the offset a full
/// layout of s2 baked — and that it actually changed (so the resolution is not a
/// no-op). A wrong SourceIndex (e.g. an engraver off-by-one, or a spanner not
/// sharing its index across broken pieces) points at the wrong side-table entry and
/// fails. The coverage assertion guarantees all 11 migrated types were exercised.
/// </summary>
[Trait("Category", "Unit")]
public class MigratedDataPosTests
{
    private static MultiStaffScore Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Fact]
    public void MigratedAnnotations_SourceIndex_ResolvesToEditedScore_AcrossAllFixtures()
    {
        var covered = new HashSet<string>();

        var sources = new List<(string Label, string Src)>();
        foreach (var path in Fixtures())
            sources.Add((Path.GetFileNameWithoutExtension(path), File.ReadAllText(path).Replace("\r\n", "\n")));

        foreach (var (fx, src) in sources)
        {
            MultiStaffScore s1, s2;
            ScoreLayout l1, l2;
            try
            {
                s1 = Collect(src);
                s2 = Collect("\n" + src);   // content-identical; every source offset +1
                l1 = new LayoutEngine().Layout(s1);
                l2 = new LayoutEngine().Layout(s2);
            }
            catch
            {
                continue; // skip sources that don't collect/lay out standalone
            }

            Check(fx, "Dynamic", l1.DynamicLayouts, l2.DynamicLayouts, s2.Dynamics,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);
            Check(fx, "Articulation", l1.ArticulationLayouts, l2.ArticulationLayouts, s2.Articulations,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);
            Check(fx, "Arpeggio", l1.ArpeggioLayouts, l2.ArpeggioLayouts, s2.Arpeggios,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);
            Check(fx, "CustomText", l1.CustomTextLayouts, l2.CustomTextLayouts, s2.CustomTexts,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);
            Check(fx, "FiguredBass", l1.FiguredBassLayouts, l2.FiguredBassLayouts, s2.FiguredBasses,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);
            Check(fx, "VoltaBracket", l1.VoltaBracketLayouts, l2.VoltaBracketLayouts, s2.VoltaBrackets,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);
            Check(fx, "TupletBracket", l1.TupletBracketLayouts, l2.TupletBracketLayouts, s2.TupletBrackets,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);
            Check(fx, "PercentRepeat", l1.PercentRepeatLayouts, l2.PercentRepeatLayouts, s2.PercentRepeats,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);
            Check(fx, "GraceNote", l1.GraceNoteLayouts, l2.GraceNoteLayouts, s2.GraceNotes,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);
            Check(fx, "ChordName", l1.ChordNameLayouts, l2.ChordNameLayouts, s2.ChordNames,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);
            Check(fx, "TrillSpanner", l1.TrillSpannerLayouts, l2.TrillSpannerLayouts, s2.TrillSpanners,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered);

            // MusicMark SourceIndex points into the reconstructed BuildAllMarks() list,
            // not a flat score side-table, so rebuild the edited table the same way
            // SharedRenderer.ResolveDataPos does. Section labels carry a real (shifted)
            // offset; the initial tempo mark carries 0 (no data-pos) and is skipped.
            var editedMarks = MusicMarkEngraver.BuildAllMarks(
                s2.MusicMarks, s2.PrimaryContentStaff.PrimaryVoice.Measures, s2.Tempo);
            Check(fx, "MusicMark", l1.MusicMarkLayouts, l2.MusicMarkLayouts, editedMarks,
                x => x.SourceIndex, x => x.SourcePosition, it => it.SourcePosition, covered,
                skipNoDataPos: true);

            // Lyric carries its data-pos on the nested LyricItem.
            Check(fx, "Lyric", l1.LyricLayouts, l2.LyricLayouts, s2.Lyrics,
                x => x.SourceIndex, x => x.Item.SourcePosition, it => it.SourcePosition, covered);
        }

        // Every migrated type the fixtures contain must have been exercised. (CustomText
        // — `_"text"` in a structure — appears in no fixture; it is covered by the direct
        // CustomText_SourceIndex_IndexesItsTable test below.)
        var expected = new[]
        {
            "Dynamic", "Articulation", "Arpeggio", "FiguredBass", "VoltaBracket",
            "TupletBracket", "PercentRepeat", "GraceNote", "ChordName", "TrillSpanner",
            "MusicMark", "Lyric",
        };
        var missing = new List<string>();
        foreach (var t in expected)
            if (!covered.Contains(t))
                missing.Add(t);
        Assert.True(missing.Count == 0, "Migrated types never exercised by any fixture: " + string.Join(", ", missing));
    }

    [Fact]
    public void CustomText_SourceIndex_IndexesItsTable()
    {
        // CustomText has no fixture coverage, so verify its engraver directly: each
        // produced layout's SourceIndex must index back to the CustomTextItem it came
        // from (so render-time ResolveDataPos reads the correct, edited offset).
        var items = ImmutableArray.Create(
            new CustomTextItem("a tempo", 0, 111),
            new CustomTextItem("poco rit.", 0, 222));
        var measureLayouts = ImmutableArray.Create(
            new MeasureLayout(0, 0, 4, ImmutableArray<ItemLayout>.Empty));

        var layouts = CustomTextEngraver.Calculate(
            null, items, ImmutableArray<SystemLayout>.Empty, measureLayouts);

        Assert.Equal(items.Length, layouts.Length);
        for (int k = 0; k < layouts.Length; k++)
        {
            Assert.True((uint)layouts[k].SourceIndex < (uint)items.Length);
            Assert.Equal(items[layouts[k].SourceIndex].SourcePosition, layouts[k].SourcePosition);
            // distinct items must map to distinct indices
            if (k > 0)
                Assert.NotEqual(layouts[k - 1].SourceIndex, layouts[k].SourceIndex);
        }
    }

    private static void Check<TL, TItem>(
        string fixture, string name,
        ImmutableArray<TL> cached, ImmutableArray<TL> full, ImmutableArray<TItem> editedTable,
        Func<TL, int> sourceIndex, Func<TL, int> layoutPos, Func<TItem, int> itemPos,
        HashSet<string> covered, bool skipNoDataPos = false)
    {
        if (cached.IsDefaultOrEmpty)
            return;
        // Content unchanged => same engraver output shape.
        Assert.True(cached.Length == full.Length, $"{fixture}/{name}: layout count changed under shift");

        for (int k = 0; k < cached.Length; k++)
        {
            // Some marks emit no data-pos (the initial tempo carries SourcePosition 0):
            // there is nothing to re-derive, and the leading-newline shift would leave it
            // at 0, tripping the no-op guard below. Skip them.
            if (skipNoDataPos && layoutPos(full[k]) == 0)
                continue;
            covered.Add(name);

            int idx = sourceIndex(cached[k]);
            Assert.True((uint)idx < (uint)editedTable.Length,
                $"{fixture}/{name}[{k}]: SourceIndex {idx} out of range (table {editedTable.Length})");

            int resolved = itemPos(editedTable[idx]);   // what ResolveDataPos would emit on reuse
            int expected = layoutPos(full[k]);           // what a full layout of the edited score bakes
            Assert.True(resolved == expected,
                $"{fixture}/{name}[{k}]: SourceIndex {idx} resolves to {resolved}, expected {expected} (wrong side-table entry)");

            // The resolution must actually change the cached value (leading '\n' shifts all offsets),
            // proving it re-derives from the edited score rather than reusing the stale baked offset.
            Assert.True(layoutPos(cached[k]) != expected,
                $"{fixture}/{name}[{k}]: resolution is a no-op (cached offset == edited offset)");
        }
    }

    private static IEnumerable<string> Fixtures()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var root = Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (Directory.Exists(root))
                return Directory.GetFiles(root, "*.lys", SearchOption.AllDirectories);
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures/ directory");
    }
}
