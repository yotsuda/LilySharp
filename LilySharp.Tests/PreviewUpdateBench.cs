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
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Times the PREVIEW path — <see cref="IncrementalCompiler.RenderIncremental"/>, the entry the
/// LSP preview calls on every keystroke — rather than a whole <c>lysc svg</c> run.
/// </summary>
/// <remarks>
/// ⚠️ NOT A TEST. It asserts nothing and is skipped unless <c>LILYSHARP_PREVIEW_BENCH</c> names
/// an output file, because a timing loop in the suite is a flaky test waiting to happen.
/// Run it in a worktree of the baseline commit and in the working tree, then compare the files:
///   $env:LILYSHARP_PREVIEW_BENCH = "…\preview-base.txt"
///   dotnet test … --filter FullyQualifiedName~PreviewUpdateBench
/// <para>
/// The loop re-renders an UNCHANGED tree. That is the keystroke that reuses the most — a full
/// cache hit — and so the one where the per-edit fixed costs (collect the score, fold every
/// item into a content key, price the break gate) are the whole of the time. An edit that
/// actually changes a measure pays those PLUS a relayout, which dilutes exactly the thing being
/// measured here, so the unchanged tree is the sharper probe, not the more flattering one.
/// </para>
/// </remarks>
[Trait("Category", "Bench")]
public class PreviewUpdateBench
{
    private const int Warmups = 2;
    private const int Rounds = 7;

    public static IEnumerable<object[]> Books()
    {
        yield return ["perf-plain1k.lys"];
        yield return ["perf-comb300.lys"];
        // ...and the book that is HEAVY on the side session 132's fingering island made
        // heavy: 3996 fingerings over 1000 bars, each on a note that also carries two
        // scripts and a bow. plain1k is the same measurement with ZERO fingerings, which is
        // where the island's per-layout OVERHEAD shows with nothing to hide behind.
        yield return ["perf-fingstack1k.lys"];
        // ...and the PAIR that prices session 133's avoid-slur #'inside port, which made the
        // slur pass buy a script walk of its own. inside1k is a slur over four STACCATOS in
        // every bar of 1000 — every bow scored against four extra boxes, and every staff's
        // scripts placed once WITHOUT slurs before the bows are chosen, once per system.
        // script1k is the same music with ACCENTS: avoid-slur #'around, so it runs the gate
        // and stops. The difference between the two is the whole of what the port costs.
        yield return ["perf-slurinside1k.lys"];
        yield return ["perf-slurscript1k.lys"];
        // ...and the pair that prices session 133's add-stem-support port, which made the
        // fingering island ask a beam map and a column reach per note. fingbeam1k is a
        // three-fingered chord BEAMED four times a bar over 1000 bars — the gate open on
        // every digit. fingstack1k above is its control: the same fingering load on QUARTERS,
        // where nothing beams, the beam list is empty and the island pays nothing at all.
        yield return ["perf-fingbeam1k.lys"];
        // ...and the pair that prices session 133's avoid-slur #'around port. fingslur300 runs
        // the new work: every digit asks which slur covers it and then walks 64 samples of
        // that bow, 1200 times. fingbeam1k above is the control for the OTHER half of the same
        // commit — fingerings with NO scripts, the book whose early-out was removed, so it
        // pays the script walk's prologue and none of the slur work.
        // ⚠️ 300 BARS, NOT 1000, AND THAT IS A MEASUREMENT: the 1000-bar version of this book
        // takes MINUTES per render — 2000 scored bows — and a fixture nobody will run twice
        // prices nothing. perf-slur300 is sized the same way for the same reason.
        yield return ["perf-fingslur300.lys"];
        // ...and the SAME 300 bars of slurs with no fingerings at all. It is here because
        // fingslur300 turned out to cost SECONDS per keystroke on both sides of the port, and
        // a number that big has to be attributed before anyone reads it as the port's: this
        // book is what says whether the cost is the bows or the digits.
        yield return ["perf-slur300.lys"];
        // ...and the FIGURED BASS, which had no book here at all until 2026-08-11 — so when
        // session 134 changed both halves of that grob (the draw gained a face scope and lost
        // its centring; the metric gained a per-character design lookup where it had been a
        // static field read) the side it made heavy could not be priced. 100 bars x 2 columns
        // of two-row figures.
        // ⚠️ 100 BARS, AND THAT IS A MEASUREMENT: this book does not scale. 100 bars render in
        // 3.1 s, 300 in 16.0 s — 3x the music for 5.2x the time — and 1000 bars exhausts
        // memory outright. A 9-render bench round on the 300-bar version costs two minutes,
        // and a fixture nobody will run twice prices nothing (the same argument slur300
        // carries). The blow-up itself is a separate, PRE-EXISTING defect, measured against
        // 41882db9 and unchanged by that session: see HANDOFF §1's next hand.
        yield return ["perf-figbass100.lys"];
    }

    [Theory]
    [MemberData(nameof(Books))]
    public void TimeOneKeystroke(string book)
    {
        var outPath = Environment.GetEnvironmentVariable("LILYSHARP_PREVIEW_BENCH");
        if (string.IsNullOrEmpty(outPath))
            return;

        var path = Path.Combine(@"C:\MyProj\LilySharp\scratch\lpreg", book);
        if (!File.Exists(path))
        {
            File.AppendAllText(outPath, $"{book}\tMISSING\n");
            return;
        }

        var tree = SyntaxTree.Parse(File.ReadAllText(path));
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new IncrementalCompiler(tree, options);

        for (int i = 0; i < Warmups; i++)
            compiler.RenderIncremental(tree);

        var times = new List<double>(Rounds);
        string? svg = null;
        for (int i = 0; i < Rounds; i++)
        {
            var sw = Stopwatch.StartNew();
            svg = compiler.RenderIncremental(tree);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        // …and the ONE stage this session's field reaches, on its own. A whole keystroke is
        // collect + key + gate + (maybe) layout, so a few per cent of it is inside this
        // measurement's noise; the content key is where the new property is folded, and it is
        // the only place a score with no combiner pays anything at all.
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        for (int i = 0; i < Warmups; i++)
            MeasureContentKey.Compute(score);
        var keyTimes = new List<double>(Rounds);
        for (int i = 0; i < Rounds; i++)
        {
            var sw = Stopwatch.StartNew();
            // 100 folds per round: one fold of a 1000-bar score is ~7 ms and the machine's
            // per-sample spread is wider than the whole effect being looked for, so the
            // averaging has to happen inside the clock, not across rounds.
            for (int k = 0; k < 100; k++)
                MeasureContentKey.Compute(score);
            sw.Stop();
            keyTimes.Add(sw.Elapsed.TotalMilliseconds / 100.0);
        }
        keyTimes.Sort();

        times.Sort();
        // The hash of the rendered SVG goes in the line too: a timing comparison between two
        // builds means nothing unless both are drawing the same picture.
        long h = 17;
        foreach (char c in svg ?? "")
            h = h * 31 + c;
        File.AppendAllText(outPath,
            $"{book}\tkeystroke floor {times[0]:F1} ms\tmedian {times[times.Count / 2]:F1} ms"
            + $"\tcontentkey floor {keyTimes[0]:F3} ms\tmedian {keyTimes[keyTimes.Count / 2]:F3} ms"
            + $"\tsvg {h:X16}\n");
    }
}
