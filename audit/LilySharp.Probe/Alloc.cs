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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

/// <summary>
/// Prices a full render and one keystroke in ALLOCATED BYTES.
/// </summary>
/// <remarks>
/// ⚠️ ALLOCATION, NOT TIME, AND THAT IS THE WHOLE POINT. On this repo's machines a
/// same-code time control swings 16% between runs, so a keystroke A/B never settles;
/// <c>GC.GetAllocatedBytesForCurrentThread</c> gives the same book the same number to
/// within 0.1 MB per render, which is what lets a before/after be read at all. Session 190
/// established that; sessions 190 and 191 both then rebuilt the harness for it in
/// git-ignored <c>scratch\</c>, which is the rot this project exists to stop — so it lives
/// here now, next to the sweep it is usually run beside.
/// <para>
/// ⚠️ STILL NOT KEYSTROKE TIMING — that stays in LilySharp.Tests (EditKeystrokeBench,
/// PreviewUpdateBench). One quantity, one home. This measures a different quantity that had
/// none.
/// </para>
/// <para>
/// ⚠️ READ THE CONTROL BEFORE THE CLAIM. Always price a book the change cannot reach
/// (perf-plain1k for anything script- or annotation-shaped) in the same run: a change that
/// moves the control has not been measured, it has been mismeasured. And say whether the
/// change could reach that book at all — one it CAN reach is not a control.
/// </para>
/// <para>
/// ⚠️ "DETERMINISTIC" IS TRUE AFTER WARM-UP, NOT FROM THE FIRST RENDER — measured, session
/// 191: three keystrokes of perf-v2bow1k in ONE process and one build read 156.8 / 153.1 /
/// 152.9 MB, so a single measurement of the first book in a process is a few MB high (the
/// JIT of paths that book is the first to reach). perf-plain1k reads 95.1 / 95.1 either way,
/// which is what makes the spread easy to mistake for a real regression in a book that has
/// it. Hence <see cref="Repeats"/>: the FLOOR of several is reported, the same shape
/// EditKeystrokeBench uses for time.
/// </para>
/// </remarks>
internal static class Alloc
{
    private static long Bytes => GC.GetAllocatedBytesForCurrentThread();

    /// <summary>How many times each measurement is taken; the floor is reported. Three is
    /// enough — the spread is a warm-up step, not noise, so the 2nd and 3rd agree.</summary>
    private const int Repeats = 3;

    public static void Run(string root, string[] books, SvgRenderOptions options)
    {
        Console.WriteLine($"{"book",-24}{"full MB",10}{"keystroke MB",15}   edit");
        foreach (var b in books)
        {
            var path = Path.Combine(root, "audit", "lpreg", b.EndsWith(".lys") ? b : b + ".lys");
            if (!File.Exists(path)) { Console.WriteLine($"{b,-24} MISSING {path}"); continue; }
            // LF-canonical for the same reason Sweep reads that way: a CRLF working tree
            // shifts every source offset, and with it the work the render does.
            var text = File.ReadAllText(path).Replace("\r\n", "\n");

            // Full render. The first one is thrown away: the glyph-outline and text-profile
            // caches are process-wide, so a cold first render prices the font, not the book.
            SvgGenerator.Generate(SyntaxTree.Parse(text), options);
            double fullMb = double.MaxValue;
            for (int r = 0; r < Repeats; r++)
            {
                long a0 = Bytes;
                SvgGenerator.Generate(SyntaxTree.Parse(text), options);
                fullMb = Math.Min(fullMb, (Bytes - a0) / 1048576.0);
            }

            // One keystroke, in EditKeystrokeBench's shape: parse outside the measurement,
            // alternate the edited and base text so the timed render is a genuine
            // one-measure change rather than a no-op the whole-layout memo answers.
            var (edited, find) = Edit(text);
            double keyMb = double.NaN;
            if (find != null)
            {
                var compiler = new IncrementalCompiler(SyntaxTree.Parse(text), options);
                compiler.RenderIncremental(SyntaxTree.Parse(text));
                keyMb = double.MaxValue;
                for (int r = 0; r < Repeats; r++)
                {
                    compiler.RenderIncremental(SyntaxTree.Parse(text));
                    var tree = SyntaxTree.Parse(edited);
                    long k0 = Bytes;
                    compiler.RenderIncremental(tree);
                    keyMb = Math.Min(keyMb, (Bytes - k0) / 1048576.0);
                }
            }
            Console.WriteLine($"{b,-24}{fullMb,10:F1}{keyMb,15:F1}   {find ?? "(no anchor — full only)"}");
        }
    }

    /// <summary>One note re-pitched near the middle of the book. Returns the anchor it
    /// found so the output says WHICH edit was priced — two runs that picked different
    /// anchors are not comparable.</summary>
    private static (string Edited, string? Find) Edit(string text)
    {
        foreach (var (find, repl) in new[]
                 {
                     ("g8", "a8"), ("e8", "f8"), ("g4", "a4"), ("e4", "f4"),
                     ("c4", "d4"), ("f4@", "g4@"), ("a4@", "b4@"),
                 })
        {
            int i = text.IndexOf(find, text.Length / 2, StringComparison.Ordinal);
            if (i >= 0)
                return (text.Remove(i, find.Length).Insert(i, repl), find);
        }
        return (text, null);
    }
}
