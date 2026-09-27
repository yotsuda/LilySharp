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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Every tie and slur of the bow situation books (LilySharp.Tests/Fixtures/bows) against its
/// LilyPond twin: each pair of bows agrees within 0.01 staff space, or is a KNOWN residual
/// whose size and reason are written in <c>Fixtures/bows/residuals.tsv</c>.
/// </summary>
/// <remarks>
/// The books are HANDOFF §2 S1's situation matrix (session 653 and after: tie positions,
/// chords, ledgers, ottava, voices, l.v., breaks; slurs over beams, stems, scripts, chords,
/// grace notes, cue notes, tabs…), which the S0–S4 sweeps closed against LilyPond 2.26.0.
/// The LilyPond side is data, <c>&lt;book&gt;.lp.txt</c>, taken by
/// <c>audit/bows/regen-lp.ps1</c> (the book's own twin through LilyPond with
/// <c>audit/bows/bowdump.ily</c>); the Lily# side is <see cref="TwinBowSweep.Dump"/>, live.
/// <para>
/// The pairing is the S0 instrument's (Lab sessions/p647/bows/compare.ps1), in the same
/// frame: bows keyed by (staff ordinal, kind) and paired in x order; each end's x is taken
/// NET of what spacing moved the head it belongs to (a tie's end: the head at or left of it
/// within a head's reach; a slur's: the column at or left of it; a repeat / l.v. tie: its one
/// head), but only where that explains a difference away; a pair whose ends agree and whose
/// span alone differs counts as agreeing (the bow's height is a function of its width).
/// </para>
/// <para>
/// A residual that grows or shrinks by more than 0.005 fails too — a better bow is news, and
/// the table must say so. <c>LILYSHARP_UPDATE_BOWS=1</c> re-takes the table (the reasons are
/// kept by key; a new row is written with the reason <c>?</c>, which
/// <see cref="EveryKnownResidual_SaysWhy"/> then refuses).
/// </para>
/// </remarks>
public sealed class BowTwinTests
{
    private const double Tol = 0.01;
    private const double Drift = 0.005;
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    private static string BowsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "LilySharp.Tests", "Fixtures", "bows");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures/bows/");
    }

    private static string TablePath => Path.Combine(BowsDir(), "residuals.tsv");

    [Fact]
    public void EveryBook_HasItsLilyPondTwin()
    {
        var books = Directory.GetFiles(BowsDir(), "*.lys");
        Assert.NotEmpty(books);
        var missing = books.Where(b => !File.Exists(Path.ChangeExtension(b, ".lp.txt"))).ToList();
        Assert.True(missing.Count == 0,
            "run audit/bows/regen-lp.ps1 for: " + string.Join(", ", missing.Select(Path.GetFileName)));
    }

    [Fact]
    public void EveryKnownResidual_SaysWhy()
    {
        var unexplained = ReadTable().Where(r => r.Value.Why is "" or "?").Select(r => r.Key).ToList();
        Assert.True(unexplained.Count == 0, "residuals without a reason: " + string.Join(", ", unexplained));
    }

    [Fact]
    public void EveryBow_MatchesItsTwin_OrIsAKnownResidual()
    {
        var table = ReadTable();
        var seen = new Dictionary<string, (string Value, string Why)>();
        var failures = new List<string>();
        foreach (var book in Directory.GetFiles(BowsDir(), "*.lys").OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(book);
            var lp = ReadLp(File.ReadAllLines(Path.ChangeExtension(book, ".lp.txt")));
            var ls = ReadLs(TwinBowSweep.Dump(File.ReadAllText(book)).Split('\n'));
            foreach (var (key, value, over) in Compare(lp, ls))
            {
                string k = name + "|" + key;
                table.TryGetValue(k, out var known);
                seen[k] = (value, known.Why ?? "?");
                if (known.Value is null)
                {
                    if (over)
                        failures.Add($"NEW {k} = {value}");
                }
                else if (!SameValue(known.Value, value))
                    failures.Add($"MOVED {k}: {known.Value} -> {value}");
            }
        }
        foreach (var k in table.Keys.Where(k => !seen.ContainsKey(k)))
            failures.Add($"GONE {k} (was {table[k].Value})");

        if (Environment.GetEnvironmentVariable("LILYSHARP_UPDATE_BOWS") == "1")
        {
            WriteTable(seen.Where(s => Over(s.Value.Value)).ToDictionary(s => s.Key, s => s.Value));
            return;
        }
        Assert.True(failures.Count == 0,
            "bows off their LilyPond twin (LILYSHARP_UPDATE_BOWS=1 re-takes Fixtures/bows/residuals.tsv):\n"
            + string.Join("\n", failures));
    }

    // A pair's value is its worst term, a count's is "lp=N ls=M", a staff count's likewise.
    private static bool Over(string value)
        => !double.TryParse(value, NumberStyles.Float, Ci, out double v) || v > Tol;

    private static bool SameValue(string known, string value)
        => double.TryParse(known, NumberStyles.Float, Ci, out double a)
           && double.TryParse(value, NumberStyles.Float, Ci, out double b)
            ? Math.Abs(a - b) <= Drift
            : known == value;

    private static Dictionary<string, (string Value, string Why)> ReadTable()
    {
        var table = new Dictionary<string, (string, string)>();
        if (!File.Exists(TablePath))
            return table;
        foreach (var line in File.ReadAllLines(TablePath))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var f = line.Split('\t');
            table[f[0] + "|" + f[1]] = (f[2], f.Length > 3 ? f[3] : "");
        }
        return table;
    }

    private static void WriteTable(Dictionary<string, (string Value, string Why)> rows)
    {
        var old = File.Exists(TablePath) ? File.ReadAllLines(TablePath).TakeWhile(l => l.StartsWith('#')).ToList() : [];
        var sb = new StringBuilder();
        foreach (var h in old)
            sb.Append(h).Append('\n');
        foreach (var (k, (value, why)) in rows.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            int bar = k.IndexOf('|');
            sb.Append(k[..bar]).Append('\t').Append(k[(bar + 1)..]).Append('\t').Append(value)
              .Append('\t').Append(why).Append('\n');
        }
        File.WriteAllText(TablePath, sb.ToString());
    }

    // ---- the S0 instrument, ported (Lab sessions/p647/bows/compare.ps1) ----

    private sealed record Bow(int Ord, char Kind, double X0, double Y0, double X1, double Y1, double X3, double Y3);

    private sealed record Side(int StaffCount, List<Bow> Bows, Dictionary<int, List<double>> Cols);

    private static List<double> Columns(IEnumerable<double> xs)
    {
        // distinct head columns: sorted x, merged within 0.3 (a chord's heads, a displaced second)
        var cols = new List<double>();
        foreach (var x in xs.OrderBy(v => v))
            if (cols.Count == 0 || x - cols[^1] > 0.3)
                cols.Add(x);
        return cols;
    }

    private static double? Nearest(List<double> cols, double x)
    {
        double? best = null;
        foreach (var c in cols)
            if (best is null || Math.Abs(c - x) < Math.Abs(best.Value - x))
                best = c;
        return best;
    }

    private static double? ColAtOrLeft(List<double> cols, double x)
    {
        double? best = null;
        foreach (var c in cols)
            if (c <= x + 0.05 && (best is null || c > best))
                best = c;
        return best;
    }

    // A tie's end lies in [head − 0.25, head + 1.6] of the head it belongs to.
    private static double? TieShift(List<double>? lpCols, List<double>? lsCols, double x, double xLs, bool left)
    {
        if (lpCols is null || lsCols is null)
            return null;
        var a = ColAtOrLeft(lpCols, left ? x - 0.3 : x + 0.3);
        var b = ColAtOrLeft(lsCols, left ? xLs - 0.3 : xLs + 0.3);
        if (a is null || b is null || Math.Abs(b.Value - a.Value) > 1.5)
            return null;
        return b - a;
    }

    // A slur's end belongs to one head: the one at or left of it (same index on both sides).
    private static double? Shift(List<double>? lpCols, List<double>? lsCols, double x, double xLs)
    {
        if (lpCols is null || lsCols is null || lpCols.Count == 0 || lsCols.Count == 0)
            return null;
        if (lpCols.Count != lsCols.Count)
        {
            double s = Nearest(lsCols, xLs)!.Value - Nearest(lpCols, x)!.Value;
            return Math.Abs(s) > 1.5 ? null : s;
        }
        int best = -1;
        for (int i = 0; i < lpCols.Count; i++)
            if (lpCols[i] <= x + 0.3)
                best = i;
        if (best < 0)
            best = 0;
        return lsCols[best] - lpCols[best];
    }

    private static double Num(string s) => double.Parse(s, Ci);

    private static char KindLp(string name) => name switch
    {
        "PhrasingSlur" => 'P',
        "Slur" => 'S',
        "LaissezVibrerTie" => 'V',
        "RepeatTie" => 'R',
        _ => 'T',
    };

    private static char KindLs(string at)
        => Regex.IsMatch(at, "^@!?phrasingSlur") ? 'P'
         : Regex.IsMatch(at, "^@laissezVibrer") ? 'V'
         : Regex.IsMatch(at, "^@repeatTie") ? 'R'
         : Regex.IsMatch(at, @"^([()]|acciaccatura|appoggiatura|grace\b|slashedGrace)") ? 'S'
         : at.StartsWith('~') ? 'T'
         : '?';

    private static Side ReadLp(IEnumerable<string> lines)
    {
        var staves = new List<(int P, int S, double Y, double Left)>();
        var bows = new List<(int P, int S, string Name, string Vy, double[] C)>();
        var heads = new List<(string K, double X)>();
        foreach (var l in lines)
        {
            Match m;
            if ((m = Regex.Match(l, @"^HEAD (\d+) (\d+) vy=(\S+) x=(\S+)")).Success)
                heads.Add(($"{m.Groups[1].Value} {m.Groups[2].Value} {Key(Num(m.Groups[3].Value))}", Num(m.Groups[4].Value)));
            else if ((m = Regex.Match(l, @"^BOWSTAFF (\d+) (\d+) y=(\S+) left=(\S+)")).Success)
                staves.Add((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), Num(m.Groups[3].Value), Num(m.Groups[4].Value)));
            else if ((m = Regex.Match(l, @"^BOW (\d+) (\d+) name=(\S+) vy=(\S+) dir=(\S+) bl=(\S+) br=(\S+) cps=(.*)$")).Success)
                bows.Add((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Value, m.Groups[4].Value,
                    m.Groups[8].Value.Trim().Split(' ').Select(Num).ToArray()));
        }
        // Staff symbols can repeat (all-elements lists them once per system, but be safe).
        var unique = staves.OrderBy(s => s.P).ThenBy(s => s.S).ThenByDescending(s => s.Y)
            .DistinctBy(s => (s.P, s.S, s.Y)).ToList();
        var ord = new Dictionary<string, int>();
        for (int i = 0; i < unique.Count; i++)
            ord[$"{unique[i].P} {unique[i].S} {Key(unique[i].Y)}"] = i;
        var outBows = new List<Bow>();
        foreach (var b in bows)
        {
            if (b.Vy == "none")
                continue;
            if (!ord.TryGetValue($"{b.P} {b.S} {Key(Num(b.Vy))}", out int o))
                continue;
            var s = unique[o];
            var c = b.C;
            outBows.Add(new Bow(o, KindLp(b.Name), c[0] - s.Left, c[1] - s.Y, c[2] - s.Left, c[3] - s.Y,
                c[6] - s.Left, c[7] - s.Y));
        }
        var cols = new Dictionary<int, List<double>>();
        foreach (var g in heads.GroupBy(h => h.K))
            if (ord.TryGetValue(g.Key, out int o))
                cols[o] = Columns(g.Select(h => h.X - unique[o].Left));
        return new Side(unique.Count, outBows, cols);
    }

    private static string Key(double y) => Math.Round(y, 4).ToString(Ci);

    private static Side ReadLs(IEnumerable<string> lines)
    {
        var staves = new List<string>();
        var bows = new List<(string Key, char Kind, double[] C)>();
        var heads = new List<(string K, double X)>();
        foreach (var l in lines)
        {
            Match m;
            if ((m = Regex.Match(l, @"^BOWSTAFF (\d+) (\d+) .*space=(\S+)")).Success)
                staves.Add($"{m.Groups[1].Value} {m.Groups[2].Value}");
            else if ((m = Regex.Match(l, @"^HEAD (\d+) (\d+) x=(\S+)")).Success)
                heads.Add(($"{m.Groups[1].Value} {m.Groups[2].Value}", Num(m.Groups[3].Value)));
            else if ((m = Regex.Match(l, @"^BOW (\d+) (\d+) src=(\S+) cps=(\S+) (\S+) (\S+) (\S+) (\S+) (\S+) (\S+) (\S+) at=(.*)$")).Success)
                bows.Add(($"{m.Groups[1].Value} {m.Groups[2].Value}", KindLs(m.Groups[12].Value),
                    Enumerable.Range(4, 8).Select(i => Num(m.Groups[i].Value)).ToArray()));
            else if (l.StartsWith("ERROR", StringComparison.Ordinal))
                throw new InvalidOperationException(l);
        }
        var ord = new Dictionary<string, int>();
        for (int i = 0; i < staves.Count; i++)
            ord[staves[i]] = i;
        // A bezier more vertical than horizontal is not a bow: Lily# draws a tab fret number's
        // parentheses (a tie carried over a line break) as closed beziers; LilyPond, as glyphs.
        var outBows = bows
            .Where(b => Math.Abs(b.C[6] - b.C[0]) > 0.3 * Math.Abs(b.C[7] - b.C[1]))
            .Select(b => new Bow(ord[b.Key], b.Kind, b.C[0], b.C[1], b.C[2], b.C[3], b.C[6], b.C[7]))
            .ToList();
        var cols = new Dictionary<int, List<double>>();
        foreach (var g in heads.GroupBy(h => h.K))
            if (ord.TryGetValue(g.Key, out int o))
                cols[o] = Columns(g.Select(h => h.X));
        return new Side(staves.Count, outBows, cols);
    }

    /// <summary>One row per pair (its worst term), per count mismatch and per staff-count
    /// mismatch; <c>Over</c> when it is not an agreement.</summary>
    private static IEnumerable<(string Key, string Value, bool Over)> Compare(Side lp, Side ls)
    {
        if (lp.StaffCount != ls.StaffCount)
        {
            yield return ("STAFFCOUNT", $"lp={lp.StaffCount} ls={ls.StaffCount}", true);
            yield break;
        }
        var keys = lp.Bows.Concat(ls.Bows).Select(b => (b.Ord, b.Kind)).Distinct()
            .OrderBy(k => k.Ord).ThenBy(k => k.Kind);
        foreach (var (o, kind) in keys)
        {
            var a = lp.Bows.Where(b => b.Ord == o && b.Kind == kind).OrderBy(b => b.X0).ThenBy(b => b.X3).ToList();
            var b = ls.Bows.Where(q => q.Ord == o && q.Kind == kind).OrderBy(q => q.X0).ThenBy(q => q.X3).ToList();
            if (a.Count != b.Count)
            {
                yield return ($"{o}|{kind}|COUNT", $"lp={a.Count} ls={b.Count}", true);
                continue;
            }
            lp.Cols.TryGetValue(o, out var lpCols);
            ls.Cols.TryGetValue(o, out var lsCols);
            for (int i = 0; i < a.Count; i++)
            {
                var p = a[i];
                var q = b[i];
                double? s0, s3;
                if (kind == 'T')
                {
                    s0 = TieShift(lpCols, lsCols, p.X0, q.X0, left: true);
                    s3 = TieShift(lpCols, lsCols, p.X3, q.X3, left: false);
                }
                else if (kind is 'R' or 'V')
                {
                    // A repeat tie hangs LEFT of the one head it belongs to, a laissez-vibrer
                    // tie right of it: both ends move with that head.
                    s0 = kind == 'R' ? Shift(lpCols, lsCols, p.X3, q.X3) : Shift(lpCols, lsCols, p.X0, q.X0);
                    s3 = s0;
                }
                else
                {
                    s0 = Shift(lpCols, lsCols, p.X0, q.X0);
                    s3 = Shift(lpCols, lsCols, p.X3, q.X3);
                }
                // The spacing correction may only EXPLAIN a difference away.
                double rx0 = q.X0 - p.X0, rx3 = q.X3 - p.X3;
                double ax0 = rx0 - (s0 ?? 0), ax3 = rx3 - (s3 ?? 0);
                double dx0 = Math.Abs(rx0) <= Math.Abs(ax0) ? rx0 : ax0;
                double dx3 = Math.Abs(rx3) <= Math.Abs(ax3) ? rx3 : ax3;
                double[] d = [dx0, q.Y0 - p.Y0, dx3, q.Y3 - p.Y3, (q.Y1 - q.Y0) - (p.Y1 - p.Y0)];
                double worst = d.Max(Math.Abs);
                // Ends in place and the height off only because the SPAN is.
                double ends = d.Take(4).Max(Math.Abs);
                double rawSpan = (q.X3 - q.X0) - (p.X3 - p.X0);
                if (ends <= Tol && Math.Abs(rawSpan) > Tol)
                    worst = 0;
                yield return ($"{o}|{kind}|{i}", worst.ToString("F4", Ci), worst > Tol);
            }
        }
    }
}
