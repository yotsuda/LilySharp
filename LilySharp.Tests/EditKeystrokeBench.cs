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
using System.Diagnostics;
using System.IO;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Times a REAL EDIT keystroke — the regimes where layout actually runs (handoff regimes
/// ⑵/⑶), which is what the per-system memos price. <see cref="PreviewUpdateBench"/> times
/// regime ⑴ (unchanged tree, whole-layout reuse) where none of that work runs at all.
/// </summary>
/// <remarks>
/// ⚠️ NOT A TEST. Asserts nothing about the engine and is skipped unless
/// LILYSHARP_EDIT_BENCH names an output file. Every keystroke alternates the base text
/// with a one-note edit near the middle of the book, so each timed render is a genuine
/// one-measure change.
/// ⚠️ THE REGIME IS IN THE OUTPUT LINE, NOT IN THE INTENT: on these repeated-bar books a
/// staff-interior pitch toggle still moves the break gate — MEASURED (session 138):
/// g8→a8 in perf-plain1k changes the edited measure's spring IdealWidth by +0.25 (probably
/// the second beam's direction), and every swap tried (d8↔e8, f8→e8, a8↔b8, even reordering
/// e8 f8→f8 e8) moved exactly one spring — so regime ⑵ is NOT reachable by pitch edits
/// here and the keystroke includes the break DP (regime ⑶). A before/after comparison is
/// still clean as long as both sides report the same gateMoved/reusedLayout pair.
/// </remarks>
[Trait("Category", "Bench")]
public class EditKeystrokeBench
{
    private const int Warmups = 2;
    private const int Rounds = 14;

    public static IEnumerable<object[]> Books()
    {
        // book, token to find (from the middle), replacement — staff-interior pitches
        // (no ledger, no accidental). See the class remarks: the gate still moves.
        yield return ["perf-plain1k.lys", "g8", "a8"];
        yield return ["perf-fingbeam1k.lys", "e@finger(3)", "f@finger(3)"];
        yield return ["perf-v2bow1k.lys", "e4(", "f4("];
        // ...and a BAR INSERTED mid-book (the keystroke the user reported slow, session
        // 329): every later measure's springs and every later system's memo entry sit
        // one index further on. The spring memo follows them by KeyAlignment; the system
        // memos serve them re-stamped — but only where the later systems are still the
        // same music, and in THIS book (no `break`) the line-break DP cascades one bar
        // into every later system, so the output line's shifted count is expected to be
        // ~0 here. A pinned-break book is where the re-stamp pays; the unit is
        // IncrementalCompilerTests.LayoutMemo_*.
        yield return ["perf-plain1k.lys", "c8 d8 e8 f8 g8 a8 b8 c'8 |", "c8 d8 e8 f8 g8 a8 b8 c'8 | c4 d4 e4 f4 |"];
    }

    [Theory]
    [MemberData(nameof(Books))]
    public void TimeOneEditedKeystroke(string book, string find, string replace)
    {
        var outPath = Environment.GetEnvironmentVariable("LILYSHARP_EDIT_BENCH");
        if (string.IsNullOrEmpty(outPath))
            return;

        // ⚠️ audit\lpreg, through the repo root — see the note in PreviewUpdateBench: the old
        // absolute scratch\lpreg address made every book MISSING on a fresh clone.
        var path = Path.Combine(CollectResumeTests.FindRepoRoot(), "audit", "lpreg", book);
        if (!File.Exists(path))
        {
            File.AppendAllText(outPath, $"{book}\tMISSING\n");
            return;
        }
        var baseText = File.ReadAllText(path);
        int idx = baseText.IndexOf(find, baseText.Length / 2, StringComparison.Ordinal);
        if (idx < 0)
        {
            File.AppendAllText(outPath, $"{book}\tTOKEN NOT FOUND\n");
            return;
        }
        var edited = baseText.Remove(idx, find.Length).Insert(idx, replace);

        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(baseText), options);
        compiler.RenderIncremental(SyntaxTree.Parse(baseText)); // cold pass, not timed

        var times = new List<double>(Rounds);
        bool everBrokeGate = false, everReusedLayout = false;
        var memo = default(LilySharp.Core.Svg.Layout.SystemLayoutCache.MemoCounters);
        for (int i = 0; i < Warmups + Rounds; i++)
        {
            // Parse OUTSIDE the clock: the keystroke being priced is the compile.
            var tree = SyntaxTree.Parse(i % 2 == 0 ? edited : baseText);
            var sw = Stopwatch.StartNew();
            compiler.RenderIncremental(tree);
            sw.Stop();
            if (i >= Warmups)
                times.Add(sw.Elapsed.TotalMilliseconds);
            everBrokeGate |= !compiler.LastEditSkippedLineBreak;
            everReusedLayout |= compiler.LastEditReusedLayout;
            // The per-system memos' outcome on the EDITED keystroke (the same every round).
            if (i % 2 == 0)
                memo = compiler.LastLayoutMemo;
        }
        times.Sort();
        File.AppendAllText(outPath,
            $"{book}\tedit floor {times[0]:F1} ms\tmedian {times[times.Count / 2]:F1} ms"
            + $"\tgateMoved {everBrokeGate}\treusedLayout {everReusedLayout}"
            + $"\tlayoutMemo hit {memo.Hits} shifted {memo.ShiftedHits} miss {memo.Misses}\n");
    }
}
