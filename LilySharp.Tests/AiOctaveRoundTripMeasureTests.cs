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
using System.IO;
using System.Linq;
using LilySharp.Core.Editing;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The AI transform's octave round trip over a folder of books
/// (<c>LILYSHARP_OCTAVE_CORPUS</c>; report to <c>LILYSHARP_OCTAVE_REPORT</c>, else the temp
/// folder): how many go absolute, and whether each comes back as it was when the AI changes
/// nothing — a book that does not would show the AI's every edit with changes it never made.
/// </summary>
[Trait("Category", "Measure")]
public sealed class AiOctaveRoundTripMeasureTests
{
    [Fact]
    public void Measure()
    {
        var folder = Environment.GetEnvironmentVariable("LILYSHARP_OCTAVE_CORPUS");
        if (string.IsNullOrEmpty(folder)) return;
        var report = Environment.GetEnvironmentVariable("LILYSHARP_OCTAVE_REPORT")
            ?? Path.Combine(Path.GetTempPath(), "ai-octave-roundtrip.txt");

        var lines = new List<string>();
        int books = 0, refused = 0, same = 0, differ = 0, lost = 0;
        var reasons = new Dictionary<string, int>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.lys", SearchOption.AllDirectories).Order())
        {
            books++;
            var source = File.ReadAllText(file);
            var abs = OctaveModeConverter.ToAbsoluteForAi(source);
            if (abs.NewText == null)
            {
                refused++;
                var why = abs.Error ?? "?";
                why = why.Contains(" at line") ? why[..why.IndexOf(" at line")] : why;
                reasons[why] = reasons.GetValueOrDefault(why) + 1;
                continue;
            }
            var back = OctaveModeConverter.FromAbsoluteForAi(abs.NewText);
            if (back.NewText == null)
            {
                lost++;
                lines.Add($"LOST {file}: {back.Error}");
                continue;
            }
            if (back.NewText == source) { same++; continue; }
            differ++;
            var a = source.Split('\n');
            var b = back.NewText.Split('\n');
            int i = 0;
            while (i < Math.Min(a.Length, b.Length) && a[i] == b[i]) i++;
            lines.Add($"DIFF {file} line {i + 1}:\n  was: {(i < a.Length ? a[i].TrimEnd() : "")}\n  now: {(i < b.Length ? b[i].TrimEnd() : "")}");
        }
        lines.Insert(0, $"books {books}: refused {refused}, back as they were {same}, back changed {differ}, not back {lost}");
        lines.InsertRange(1, reasons.OrderByDescending(r => r.Value).Select(r => $"  refused {r.Value}: {r.Key}"));
        File.WriteAllLines(report, lines);
    }
}
