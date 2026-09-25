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

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The content key's compiled fold (session 610) folds what the per-property walk it sped up
/// folds: every measure item of every staff and voice, and every side-table entry, of every
/// net book collects to the same 64-bit fold both ways. ⚠️ The net must bite: it asserts the
/// books held items of many types and entries of several tables.
/// </summary>
public class ContentKeyCompiledFoldTests
{
    [Fact]
    public void TheCompiledFold_FoldsWhatThePropertyWalkFolds()
    {
        var failures = new List<string>();
        var itemTypes = new HashSet<string>();
        var sideTypes = new HashSet<string>();
        int items = 0, sides = 0;
        foreach (var path in CollectResumeTests.NetBooks())
        {
            MultiStaffScore score;
            try
            {
                var tree = SyntaxTree.Parse(File.ReadAllText(path));
                score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
            }
            catch { continue; }
            foreach (var voice in score.AllVoices)
                foreach (var m in voice.Measures)
                    foreach (var item in m.Items)
                    {
                        items++;
                        itemTypes.Add(item.GetType().Name);
                        long fast = MeasureContentKey.HashItemContent(item);
                        long slow = MeasureContentKey.HashItemContentSlow(item);
                        if (fast != slow)
                            failures.Add($"{Path.GetFileName(path)}: {item.GetType().Name}@{item.SourcePosition} {fast:x} != {slow:x}");
                    }
            foreach (IEnumerable table in new IEnumerable[]
                     {
                         score.Dynamics, score.Articulations, score.GraceNotes, score.Lyrics, score.MusicMarks,
                         score.CustomTexts, score.TupletBrackets, score.Arpeggios, score.FiguredBasses,
                         score.ChordNames, score.PercentRepeats, score.VoltaBrackets, score.TrillSpanners,
                     })
                foreach (var entry in table)
                {
                    sides++;
                    sideTypes.Add(entry.GetType().Name);
                    long fast = MeasureContentKey.HashSideContent(entry);
                    long slow = MeasureContentKey.HashSideContentSlow(entry);
                    if (fast != slow)
                        failures.Add($"{Path.GetFileName(path)}: side {entry.GetType().Name} {fast:x} != {slow:x}");
                }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        Assert.True(items >= 5_000 && itemTypes.Count >= 6 && sides >= 500 && sideTypes.Count >= 5,
            $"the net did not bite: {items} items of {itemTypes.Count} types, {sides} entries of {sideTypes.Count} types");
    }
}
