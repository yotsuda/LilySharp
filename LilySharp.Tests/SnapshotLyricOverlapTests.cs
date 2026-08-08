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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// No two syllables on the same lyric row of any committed snapshot overlap.
/// This is the invariant the per-measure lyric reservations (LyricSpacing) exist
/// to hold, measured on the DRAWN output — the net that catches a reservation
/// change breaking a fixture it never looked at.
/// </summary>
/// <remarks>
/// ⚠️ MEASURED TO BE LOAD-BEARING (第112セッション第5便): removing the barline
/// clearances to let lyrics pass under the bar line (lyrics-pass-under-bar.ly)
/// put real ink overlaps of 0.1–12.5 ss into 8 of the snapshot fixtures — the
/// clearance IS the cross-bar collision guard while the spring chain is
/// per-measure. LilyPond needs no such guard because its whole LINE is one
/// spacing problem with height-aware separation; see the ledger's open note.
/// Widths are the same TextFontMetrics estimate the reservations use, so the
/// invariant is exactly "the reservations kept what they promised".
/// </remarks>
[Trait("Category", "Unit")]
public class SnapshotLyricOverlapTests
{
    [Fact]
    public void NoSnapshotLyricRow_HasOverlappingSyllables()
    {
        var dir = Path.Combine(FindRoot(), "LilySharp.Tests", "Snapshots");
        var rx = new Regex(
            "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"([\\d.]+)\" text-anchor=\"middle\"[^>]*>([^<]+)</text>");
        var overlaps = new List<string>();

        foreach (var file in Directory.GetFiles(dir, "*.svg"))
        {
            var rows = new Dictionary<string, List<(double L, double R, string T)>>();
            foreach (Match m in rx.Matches(File.ReadAllText(file)))
            {
                double x = double.Parse(m.Groups[1].Value);
                double fs = double.Parse(m.Groups[3].Value);
                string t = m.Groups[4].Value;
                double w = TextFontMetrics.Serif(t, 3.2) * fs / 3.2;
                (rows.TryGetValue(m.Groups[2].Value, out var list)
                    ? list : rows[m.Groups[2].Value] = new()).Add((x - w / 2, x + w / 2, t));
            }
            foreach (var (y, list) in rows)
            {
                var s = list.OrderBy(e => e.L).ToList();
                for (int i = 1; i < s.Count; i++)
                    // 0.05 ss of slack: the estimator differs from the viewer's face
                    // by a few percent; a real collision is far past this.
                    if (s[i].L < s[i - 1].R - 0.05)
                        overlaps.Add($"{Path.GetFileName(file)} y={y}: "
                            + $"'{s[i - 1].T}' overlaps '{s[i].T}' by {s[i - 1].R - s[i].L:F2}");
            }
        }

        Assert.True(overlaps.Count == 0,
            "syllable ink overlaps in committed snapshots:\n" + string.Join("\n", overlaps));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "LilySharp.Tests")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
