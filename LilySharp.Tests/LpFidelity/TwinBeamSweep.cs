// SCRATCH INSTRUMENT — not part of the suite's claims. Dumps every fixture's drawn beam
// positions in LilyPond's Beam.positions vocabulary so the corpus can be diffed against the
// .ly twins in one pass. Opt-in through LILYSHARP_BEAM_SWEEP=<csv path>; without it the test
// does nothing.

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

public sealed class TwinBeamSweep
{
    /// <summary>A horizontal rule must reach at least this far to count as a staff line.</summary>
    private const double MinStaffLineSpan = 10.0;

    [Fact]
    public void DumpBeamPositions()
    {
        string? csv = Environment.GetEnvironmentVariable("LILYSHARP_BEAM_SWEEP");
        if (string.IsNullOrEmpty(csv)) return;

        string root = FixtureRoot();
        var sb = new StringBuilder();
        sb.Append("fixture,page,staff,xleft,posLeft,posRight,staffSpace\n");

        foreach (string dir in new[] { "test", "showcase" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root, dir), "*.lys").OrderBy(f => f))
            {
                string name = dir + "__" + Path.GetFileNameWithoutExtension(file);
                try
                {
                    foreach (string line in BeamsOf(File.ReadAllText(file)))
                        sb.Append(name).Append(',').Append(line).Append('\n');
                }
                catch (Exception ex)
                {
                    sb.Append(name).Append(",ERROR,,,,,\"")
                      .Append(ex.Message.Replace("\"", "'").Replace("\n", " ")).Append("\"\n");
                }
            }
        }

        File.WriteAllText(csv, sb.ToString());
    }

    private static string FixtureRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Fixtures")))
            d = d.Parent;
        if (d == null) throw new InvalidOperationException("Fixtures directory not found");
        return Path.Combine(d.FullName, "Fixtures");
    }

    private static IEnumerable<string> BeamsOf(string source)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            throw new InvalidOperationException("does not parse");

        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(score);

        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);

        var rows = new List<string>();
        for (int p = 0; p < doc.Pages.Count; p++)
        {
            var page = doc.Pages[p];
            var staves = StavesOf(page);
            if (staves.Count == 0) continue;

            // ⚠️ Grouped PER STAFF, never per page. Two systems occupy the same x range, so
            // a page-wide grouping lets a beam on one line be swallowed by a beam group on
            // another — which drops readings AND corrupts the surviving group's stack.
            var stems = Stems(page);
            var quads = page.Quads
                .Select(q => (Left: Math.Min(q.X0, q.X3), Right: Math.Max(q.X1, q.X2),
                              LeftY: (q.Y0 + q.Y3) / 2, RightY: (q.Y1 + q.Y2) / 2))
                .ToList();

            for (int si = 0; si < staves.Count; si++)
            {
                var st = staves[si];
                var mine = quads.Where(q => NearestStaff(staves, (q.LeftY + q.RightY) / 2) == si)
                                .ToList();
                if (mine.Count == 0) continue;

                // Same reason as the quads: a stem on another system shares this x range,
                // and one of those reaching past the stack flips the direction test.
                var myStems = stems
                    .Where(s => NearestStaff(staves, (s.Top + s.Bottom) / 2) == si)
                    .ToList();

                foreach (var beam in PrimaryBeams(mine, myStems))
                {
                    rows.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2:F6},{3:F6},{4:F6},{5:F6}",
                        p, si, beam.Left,
                        Snap((st.Middle - beam.LeftY) / st.Space),
                        Snap((st.Middle - beam.RightY) / st.Space),
                        st.Space));
                }
            }
        }
        return rows;
    }

    /// <summary>
    /// Staves found from the drawn staff lines: long horizontal rules clustered by Y, a
    /// cluster of four or more evenly-spaced lines being one staff. Line COUNT is not
    /// assumed (a TAB staff has as many lines as strings) and neither is the staff space
    /// (an ossia's is scaled) — both are read back off the cluster.
    /// </summary>
    private static List<(double Top, double Bottom, double Middle, double Space)> StavesOf(
        RecordingDrawingContext page)
    {
        var ys = page.Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && Math.Abs(l.X2 - l.X1) >= MinStaffLineSpan)
            .Select(l => Math.Round(l.Y1, 6))
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        var staves = new List<(double, double, double, double)>();
        int i = 0;
        while (i < ys.Count)
        {
            int j = i + 1;
            while (j < ys.Count && ys[j] - ys[j - 1] <= 2.0) j++;
            int n = j - i;
            if (n >= 4)
            {
                double top = ys[i], bottom = ys[j - 1];
                staves.Add((top, bottom, (top + bottom) / 2, (bottom - top) / (n - 1)));
            }
            i = j;
        }
        return staves;
    }

    /// <summary>
    /// One reading per beam GROUP: the group's outermost beam line, which is what
    /// LilyPond's <c>positions</c> describes — every further beam of a stack is drawn at
    /// <c>positions + beam_dy × rank</c> TOWARD the noteheads (lily/beam.cc:810-814
    /// Beam::print), so a 16th group's rank-0 line is the one FARTHEST from them.
    /// </summary>
    /// <remarks>
    /// ⚠️ Taking "the widest quad at the group's left" is not enough, and reading the
    /// inner line instead of the outer one shifts every multi-line group by exactly one
    /// beam translation (0.81 on this corpus) — a divergence that looks like a quanter
    /// defect and is not. The stems say which side the noteheads are on: whichever way a
    /// stem runs past the stack is the way the music lies.
    /// </remarks>
    private static List<(double Left, double Right, double LeftY, double RightY)> PrimaryBeams(
        List<(double Left, double Right, double LeftY, double RightY)> staffQuads,
        List<(double X, double Top, double Bottom)> stems)
    {
        var quads = staffQuads
            .OrderBy(q => q.Left).ThenByDescending(q => q.Right - q.Left)
            .ToList();

        // Group the quads: a stub, and every further beam line of a stack, spans no wider
        // than the group's widest quad.
        var groups = new List<List<(double Left, double Right, double LeftY, double RightY)>>();
        foreach (var q in quads)
        {
            var owner = groups.FirstOrDefault(g =>
                q.Left >= g[0].Left - 1e-9 && q.Right <= g[0].Right + 1e-9);
            if (owner != null) owner.Add(q);
            else groups.Add(new List<(double, double, double, double)> { q });
        }

        var primaries = new List<(double Left, double Right, double LeftY, double RightY)>();
        foreach (var g in groups)
        {
            // Only the lines that span the whole group are candidates; stubs are not.
            var full = g.Where(q => q.Right - q.Left >= (g[0].Right - g[0].Left) - 1e-9).ToList();
            double top = full.Min(q => Math.Min(q.LeftY, q.RightY));
            double bottom = full.Max(q => Math.Max(q.LeftY, q.RightY));

            double below = 0, above = 0;
            foreach (var s in stems)
            {
                if (s.X < g[0].Left - 0.2 || s.X > g[0].Right + 0.2) continue;
                below = Math.Max(below, s.Bottom - bottom);
                above = Math.Max(above, top - s.Top);
            }

            // Stems running DOWN from the stack put the noteheads below it, so the
            // outermost line is the topmost.
            primaries.Add(below > above
                ? full.OrderBy(q => q.LeftY + q.RightY).First()
                : full.OrderByDescending(q => q.LeftY + q.RightY).First());
        }
        return primaries;
    }

    /// <summary>Which staff a Y belongs to: the one whose line span it is nearest.</summary>
    private static int NearestStaff(
        List<(double Top, double Bottom, double Middle, double Space)> staves, double y)
    {
        int si = 0;
        double best = double.MaxValue;
        for (int i = 0; i < staves.Count; i++)
        {
            double d = y < staves[i].Top ? staves[i].Top - y
                     : y > staves[i].Bottom ? y - staves[i].Bottom : 0;
            if (d < best) { best = d; si = i; }
        }
        return si;
    }

    /// <summary>Vertical rules — stems, and whatever else is drawn vertically.</summary>
    private static List<(double X, double Top, double Bottom)> Stems(RecordingDrawingContext page) =>
        page.Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9 && Math.Abs(l.Y2 - l.Y1) > 0.3)
            .Select(l => (l.X1, Math.Min(l.Y1, l.Y2), Math.Max(l.Y1, l.Y2)))
            .ToList();

    private static double Snap(double v) => Math.Abs(v) < 5e-7 ? 0.0 : v;
}
