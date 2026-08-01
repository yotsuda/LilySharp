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

    /// <summary>
    /// One staff as the page shows it: the five (or four, or six) lines that carry the
    /// position arithmetic, plus how far its LEDGER lines run out from them.
    /// </summary>
    /// <remarks>
    /// The ledger reach is not decoration — it is how a notehead outside the staff says which
    /// staff it is outside OF, and nothing else on the page says it. A ledger line is drawn on
    /// the staff's own grid, continuing outward one space at a time, so the run of them is a
    /// stair back to the staff that owns the note.
    /// </remarks>
    private readonly record struct StaffBox(
        double Top, double Bottom, double Space, double LedgerTop, double LedgerBottom)
    {
        public double Middle => (Top + Bottom) / 2;

        /// <summary>How far a Y lies outside the staff proper; 0 when inside.</summary>
        public double Distance(double y) => y < Top ? Top - y : y > Bottom ? y - Bottom : 0;

        /// <summary>The same, but counting the staff's ledger lines as part of it.</summary>
        public double LedgerDistance(double y)
            => y < LedgerTop ? LedgerTop - y : y > LedgerBottom ? y - LedgerBottom : 0;
    }

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
                var mine = quads.Where(q => BeamStaff(staves, stems, q) == si).ToList();
                if (mine.Count == 0) continue;

                foreach (var beam in PrimaryBeams(mine, stems))
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
    /// Staves found from the drawn staff lines: horizontal rules clustered by Y, a cluster of
    /// four or more evenly-spaced lines being one staff. Line COUNT is not assumed (a TAB
    /// staff has as many lines as strings) and neither is the staff space (an ossia's is
    /// scaled) — both are read back off the cluster.
    /// </summary>
    /// <remarks>
    /// ⚠️⚠️ LEDGER LINES ARE HORIZONTAL RULES TOO, and letting them into a cluster moves the
    /// staff's MIDDLE — every beam of that staff then reads off by half a space per absorbed
    /// row, uniformly, which looks exactly like a quanter that is a little low. It was doing
    /// this on most of the corpus: <c>test/beaming</c> read one ledger row and so every beam
    /// in it was +0.500; <c>test/notes</c> read four on one system and was +2.000;
    /// <c>test/beam-under-staves</c> read two and was +1.000 on top of a wrong staff.
    /// <para>
    /// ⚠️ The span test that used to keep them out was dropped when tab string lines started
    /// breaking around each fret digit (a piece is then far under any sane threshold), and the
    /// union-of-pieces that replaced it lets a whole ROW of ledger lines through: eight pieces
    /// under eight noteheads reach 20.1 of a 30.5-wide system, which clears any length bar you
    /// could set. Measured, the separation is not length or coverage but REACH — a staff line
    /// spans the system, a ledger line spans a notehead:
    /// <code>
    ///                                   pieces  covered/span   span
    ///   staff line                          1        1.000    30.4848  = the system
    ///   tab string line (broken at digits)  9        0.514    31.2045  = the system, exactly
    ///   ledger row (8 noteheads)            8        0.777    20.1432  &lt; the system
    /// </code>
    /// The tab row's coverage is the LOWEST of the three and its reach is still the system's
    /// to thirteen places, because breaking a line around a digit does not move its ends.
    /// </para>
    /// <para>
    /// So group by REACH first and cluster by Y inside each group. <c>DrawStaffLines</c> draws
    /// all of one staff's lines in one call with one lineStartX/lineEndX, so a staff's rows
    /// reach exactly as far as each other and land in one group; a ledger row's reach is
    /// whatever its noteheads happen to span, so ledger rows scatter and never gather four.
    /// ⚠️ Grouping by Y FIRST does not work: the ledger rows BETWEEN two staves chain the
    /// clusters together (17.118 → 18.468 → 19.213 → … → 23.213 in test/multistaff-tuplet-beams
    /// are each within the 2.0 gap), so the page becomes one cluster and the two staves are
    /// read as a single ten-line one with a 1.566 staff space. Reach separates them without
    /// ever asking how far apart two staves are.
    /// </para>
    /// </remarks>
    private static List<StaffBox> StavesOf(RecordingDrawingContext page)
    {
        var allRows = page.Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9)
            .GroupBy(l => Math.Round(l.Y1, 6))
            .Select(g => (Y: g.Key,
                          Reach: g.Max(l => Math.Max(l.X1, l.X2)) - g.Min(l => Math.Min(l.X1, l.X2))))
            .OrderBy(r => r.Y)
            .ToList();

        var staves = new List<StaffBox>();
        foreach (var group in allRows.Where(r => r.Reach >= MinStaffLineSpan)
                                     .GroupBy(r => Math.Round(r.Reach, 6)))
        {
            var ys = group.Select(r => r.Y).OrderBy(y => y).ToList();
            int i = 0;
            while (i < ys.Count)
            {
                int j = i + 1;
                while (j < ys.Count && ys[j] - ys[j - 1] <= 2.0) j++;
                int n = j - i;
                if (n >= 4)
                {
                    double top = ys[i], bottom = ys[j - 1], space = (bottom - top) / (n - 1);
                    staves.Add(new StaffBox(top, bottom, space,
                        LedgerRun(allRows, top, space, -1), LedgerRun(allRows, bottom, space, +1)));
                }
                i = j;
            }
        }

        // The caller indexes staves top to bottom; the reach groups came in hash order.
        staves.Sort((a, b) => a.Top.CompareTo(b.Top));
        return staves;
    }

    /// <summary>
    /// How far the ledger lines continue a staff's grid past <paramref name="from"/>: keep
    /// stepping one staff space in <paramref name="dir"/> while a horizontal rule is drawn
    /// there.
    /// </summary>
    /// <remarks>
    /// Reach is NOT tested here, and must not be — the whole point is the short rules the staff
    /// scan rejects. A ledger line is exactly one space from the last one, so a run cannot
    /// wander onto another staff's lines: it stops at the first empty position.
    /// </remarks>
    private static double LedgerRun(
        List<(double Y, double Reach)> rows, double from, double space, int dir)
    {
        double at = from;
        while (true)
        {
            double next = at + dir * space;
            if (!rows.Any(r => Math.Abs(r.Y - next) < 1e-6)) return at;
            at = next;
        }
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

            // ⚠️ Only the rules that TOUCH this stack count. The stems used to be pre-filtered
            // by staff, which needed a staff for the beam before the beam had one; the reach
            // test does the same work without the circularity — a bar line or a stem on
            // another system shares the x range but not the y.
            double below = 0, above = 0;
            foreach (var s in StemsTouching(stems,
                         (g[0].Left, g[0].Right, top, bottom)))
            {
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
    private static int NearestStaff(List<StaffBox> staves, double y)
    {
        int si = 0;
        double best = double.MaxValue;
        for (int i = 0; i < staves.Count; i++)
        {
            double d = staves[i].Distance(y);
            if (d < best) { best = d; si = i; }
        }
        return si;
    }

    /// <summary>
    /// The staff a beam belongs to: the staff of the NOTEHEADS its stems hang from.
    /// </summary>
    /// <remarks>
    /// ⚠️ A beam is NOT always near its own staff. LilyPond's Beam is an ordinary inside-staff
    /// grob that may be drawn well into the gap below (test/beam-under-staves exists for
    /// exactly that), and taking the staff it is nearest reads it off the LOWER staff's centre
    /// line — 10.100 spaces wrong in that book, which was recorded for two sessions as a
    /// layout divergence and once as a twin that held the wrong music.
    /// <para>
    /// ⚠️ "The end deepest inside a staff" is NOT the rule, and looks like it until a book
    /// where the noteheads are on LEDGER lines: in that fixture neither end of the stem is
    /// inside anything, and the beam end is the closer of the two to the staff below. The end
    /// AWAY FROM THE BEAM is the head end by construction — a stem runs from its notehead to
    /// its beam — and asking which staff THAT end is nearest costs nothing extra.
    /// </para>
    /// Falls back to nearest when nothing vertical touches the beam (a lone beamlet).
    /// </remarks>
    private static int BeamStaff(
        List<StaffBox> staves,
        List<(double X, double Top, double Bottom)> stems,
        (double Left, double Right, double LeftY, double RightY) beam)
    {
        double beamY = (beam.LeftY + beam.RightY) / 2;
        var cost = new double[staves.Count];
        int heads = 0;
        foreach (var s in StemsTouching(stems, beam))
        {
            double headEnd = Math.Abs(s.Top - beamY) >= Math.Abs(s.Bottom - beamY) ? s.Top : s.Bottom;
            for (int i = 0; i < staves.Count; i++)
                cost[i] += staves[i].LedgerDistance(headEnd);
            heads++;
        }
        if (heads == 0)
            return NearestStaff(staves, beamY);

        // ⚠️ LEDGER distance, and the TOTAL of it rather than a majority of per-stem nearest.
        // Bare proximity gets test/multistaff-tuplet-beams wrong in the other direction from
        // test/beam-under-staves: it beams `g a b c'` in the BASS, and those heads sit above
        // the bass staff, nearer the treble staff than their own. The ledger lines under them
        // are the bass staff's grid continuing upward, so counting them as part of it puts the
        // heads back where they belong. The two books are geometric mirror images and only
        // this reading gets both.
        int best = 0;
        for (int i = 1; i < staves.Count; i++)
            if (cost[i] < cost[best]) best = i;
        return best;
    }

    /// <summary>
    /// The vertical rules that could be this beam's stems: overlapping it in x AND reaching it
    /// in y.
    /// </summary>
    /// <remarks>
    /// ⚠️ The y test is what the per-staff stem filter used to do badly. Without it a bar line
    /// crossing the same x range votes for whichever staff it spans, and a stem on ANOTHER
    /// SYSTEM — same x range, different y — flips the direction test in
    /// <see cref="PrimaryBeams"/>. With it, neither can be mistaken for a stem of this beam,
    /// and no staff assignment has to be guessed first.
    /// </remarks>
    private static IEnumerable<(double X, double Top, double Bottom)> StemsTouching(
        List<(double X, double Top, double Bottom)> stems,
        (double Left, double Right, double LeftY, double RightY) beam)
    {
        double top = Math.Min(beam.LeftY, beam.RightY), bottom = Math.Max(beam.LeftY, beam.RightY);
        foreach (var s in stems)
        {
            if (s.X < beam.Left - 0.2 || s.X > beam.Right + 0.2) continue;
            if (s.Bottom < top - 0.5 || s.Top > bottom + 0.5) continue;
            yield return s;
        }
    }

    /// <summary>Vertical rules — stems, and whatever else is drawn vertically.</summary>
    private static List<(double X, double Top, double Bottom)> Stems(RecordingDrawingContext page) =>
        page.Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9 && Math.Abs(l.Y2 - l.Y1) > 0.3)
            .Select(l => (l.X1, Math.Min(l.Y1, l.Y2), Math.Max(l.Y1, l.Y2)))
            .ToList();

    private static double Snap(double v) => Math.Abs(v) < 5e-7 ? 0.0 : v;
}
