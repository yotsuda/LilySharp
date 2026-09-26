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
// SCRATCH INSTRUMENT — not part of the suite's claims (HANDOFF §2 S0). Dumps every bow a
// book draws — ties, slurs, phrasing slurs — in the frame the LilyPond side's bowdump.ily
// prints (Lab sessions/p647/bows): x from the staff lines' left end, y up from the staff's
// middle line, at full double precision. Opt-in through LILYSHARP_BOW_SWEEP=<list file>,
// one "<book.lys>\t<out.txt>" per line; without it the test does nothing.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

public sealed class TwinBowSweep
{
    [Fact]
    public void DumpBows()
    {
        string? list = Environment.GetEnvironmentVariable("LILYSHARP_BOW_SWEEP");
        if (string.IsNullOrEmpty(list)) return;

        foreach (string line in File.ReadAllLines(list))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            string text;
            try
            {
                text = Dump(File.ReadAllText(parts[0]));
            }
            catch (Exception ex)
            {
                text = "ERROR " + ex.Message.Replace('\n', ' ') + "\n";
            }
            File.WriteAllText(parts[1], text);
        }
    }

    private static string Dump(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine(score.Paper).Layout(score);

        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("BOWBOOK\n");
        for (int p = 0; p < doc.Pages.Count; p++)
        {
            var page = doc.Pages[p];
            var staves = TwinBeamSweep.StavesOf(page);
            for (int s = 0; s < staves.Count; s++)
            {
                var st = staves[s];
                sb.AppendFormat(ci, "BOWSTAFF {0} {1} middle={2:F6} left={3:F6} space={4:F6} lines={5}\n",
                    p + 1, s, st.Middle, st.Left, st.Space,
                    (int)Math.Round((st.Bottom - st.Top) / st.Space) + 1);
            }
            // The noteheads, so the reader can take the bows' x relative to where the heads
            // stand: a bow drawn right from a head that spacing put elsewhere is not a bow
            // defect. Emmentaler's noteheads (EmmentalerGlyphs.Notehead*, U+E0FA-U+E10B).
            // ⚠️ Not "SMuFL's U+E0A0-U+E0FF": in this font that block also holds the flags,
            // the fingering and figured-bass digits, so an eighth's flag read as a second head
            // 1.304 right of it and every tie from a flagged note was mapped one column off
            // (Lab sessions/p648: Everybody's Talkin' x0 −1.3042 × 36). The metronome mark's
            // note shares the code points and still passes (one per book, above the staff).
            var heads = new List<(double X, double Y, int Staff)>();
            foreach (var g in page.Glyphs)
            {
                if (g.Glyph < EmmentalerGlyphs.NoteheadDoubleWhole || g.Glyph > EmmentalerGlyphs.NoteheadXCircle
                    || staves.Count == 0) continue;
                int si = NearestStaff(staves, g.Y);
                heads.Add((g.X, g.Y, si));
                sb.AppendFormat(ci, "HEAD {0} {1} x={2:F6}\n", p + 1, si, g.X - staves[si].Left);
            }
            // …and a tab staff's fret numbers, by the X they are drawn at (their centre).
            foreach (var t in page.Texts)
            {
                if (t.Role != TextRole.TabFret || staves.Count == 0) continue;
                int si = NearestStaff(staves, t.Y);
                sb.AppendFormat(ci, "HEAD {0} {1} x={2:F6} anchor={3}\n", p + 1, si, t.X - staves[si].Left, t.Anchor);
            }
            foreach (var b in page.Beziers)
            {
                if (staves.Count == 0) break;
                // The staff whose LEDGER-extended grid the bow's ends are nearest: a tie under
                // a low note can sit exactly between its own staff and the next system's, and
                // only the ledger lines under the note say which one it belongs to.
                // The staff of the notehead nearest the bow's start: a slur hanging into the gap
                // under its staff can sit nearer the NEXT staff's lines than its own, but never
                // nearer another staff's heads than its own note's. The lines decide only when
                // there is no head near (a tab staff, a piece broken at the system's start).
                int si = -1;
                double bestD = 3.0;
                foreach (var h in heads)
                {
                    double dx = h.X - b.P0.X, dy = h.Y - b.P0.Y;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < bestD) { bestD = d; si = h.Staff; }
                }
                if (si < 0)
                    si = NearestStaff(staves, (b.P0.Y + b.P1.Y) / 2);
                var st = staves[si];
                string at = b.SourcePosition >= 0 && b.SourcePosition < source.Length
                    ? source.Substring(b.SourcePosition, Math.Min(16, source.Length - b.SourcePosition))
                        .Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')
                    : "";
                var c1 = b.Centreline1;
                var c2 = b.Centreline2;
                sb.AppendFormat(ci, "BOW {0} {1} src={2} cps={3:F6} {4:F6} {5:F6} {6:F6} {7:F6} {8:F6} {9:F6} {10:F6} at={11}\n",
                    p + 1, si, b.SourcePosition,
                    b.P0.X - st.Left, st.Middle - b.P0.Y,
                    c1.X - st.Left, st.Middle - c1.Y,
                    c2.X - st.Left, st.Middle - c2.Y,
                    b.P1.X - st.Left, st.Middle - b.P1.Y,
                    at);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// The staff whose LEDGER-extended grid <paramref name="y"/> is nearest: a tie under a low
    /// note can sit exactly between its own staff and the next system's, and only the ledger
    /// lines under the note say which one it belongs to.
    /// </summary>
    private static int NearestStaff(List<TwinBeamSweep.StaffBox> staves, double y)
    {
        int si = 0;
        for (int s = 1; s < staves.Count; s++)
        {
            double d = staves[s].LedgerDistance(y), best = staves[si].LedgerDistance(y);
            if (d < best - 1e-9
                || (Math.Abs(d - best) <= 1e-9 && staves[s].Distance(y) < staves[si].Distance(y)))
                si = s;
        }
        return si;
    }
}
