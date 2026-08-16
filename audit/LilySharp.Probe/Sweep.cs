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

using System.Security.Cryptography;
using System.Text;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

/// <summary>
/// Renders a corpus of <c>.lys</c> to one hash per book, so two runs of the engine can be
/// compared exactly. The instrument behind HANDOFF RULES §5.4: poison the mechanism a
/// fixture NAMES, sweep, and read which books moved.
/// </summary>
internal static class Sweep
{
    private static string OutDir(string root)
    {
        var d = Path.Combine(root, "audit", "probe-out");
        Directory.CreateDirectory(d);
        return d;
    }

    /// <param name="listFile">Optional file of repo-relative paths, one per line (e.g. from
    /// <c>git ls-files "*.lys"</c>). Null sweeps the fixture tree.</param>
    public static void Run(string root, string label, SvgRenderOptions options, string listFile)
    {
        var books = listFile != null
            ? File.ReadAllLines(listFile).Where(l => l.Trim().Length > 0)
                .Select(l => Path.Combine(root, l.Trim().Replace('/', Path.DirectorySeparatorChar)))
                .Where(File.Exists).OrderBy(p => p, StringComparer.Ordinal).ToArray()
            : Directory.GetFiles(Path.Combine(root, "LilySharp.Tests", "Fixtures"),
                "*.lys", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();

        var lines = new List<string>(books.Length);
        int ok = 0, threw = 0;
        foreach (var p in books)
        {
            string name = Path.GetRelativePath(root, p).Replace('\\', '/');
            string cell;
            try
            {
                // ⚠️ LF-CANONICAL, exactly as SvgSnapshotTests reads a fixture, and for the
                // same reason: a CRLF working tree shifts every source offset past a newline,
                // so data-pos — and therefore this hash — would depend on how git materialised
                // the file rather than on the engine. Session 190 learned this the expensive
                // way: rewriting ten fixtures with a CRLF writer made all ten "move" under a
                // poison that could not possibly have touched them.
                var source = File.ReadAllText(p).Replace("\r\n", "\n");
                var svg = SvgGenerator.Generate(SyntaxTree.Parse(source), options);
                cell = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(svg)))[..16]
                     + "," + svg.Length;
                ok++;
            }
            catch (Exception ex)
            {
                // A throw is a RESULT, not a hole: a poison that makes books throw has been
                // recorded, and cmp will show those books as moved.
                cell = "THREW:" + ex.GetType().Name + ":" + ex.Message.Replace(',', ';') + ",0";
                threw++;
            }
            lines.Add(name + "," + cell);
        }

        var outPath = Path.Combine(OutDir(root), label + ".csv");
        File.WriteAllLines(outPath, lines);
        Console.WriteLine($"sweep '{label}': {books.Length} books, {ok} rendered, {threw} threw"
            + $" -> {Path.GetRelativePath(root, outPath).Replace('\\', '/')}");
    }

    /// <summary>Reports what moved between two sweeps. Returns false when the comparison
    /// itself could not be made.</summary>
    public static bool Compare(string root, string a, string b, string[] claimants)
    {
        Dictionary<string, string> A, B;
        try { A = Load(root, a); B = Load(root, b); }
        catch (FileNotFoundException e)
        {
            Console.Error.WriteLine($"  *** no such sweep: {e.FileName}");
            return false;
        }

        // The comparison's own positive control. An empty INTERSECTION is not "nothing
        // moved", it is "nothing was compared" — and the two read identically if you only
        // print a count. Session 190 renamed the sweep's keys, forgot to re-take the
        // baseline, and got a confident "0 of 219 moved" out of a comparison of nothing.
        int compared = A.Keys.Count(B.ContainsKey);
        if (compared == 0)
        {
            Console.Error.WriteLine($"  *** '{a}' and '{b}' share NO book names — nothing was "
                + "compared. Re-take the baseline with this build of the tool.");
            return false;
        }

        var moved = A.Keys.Where(k => B.ContainsKey(k) && A[k] != B[k])
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        int onlyA = A.Keys.Except(B.Keys).Count(), onlyB = B.Keys.Except(A.Keys).Count();

        Console.WriteLine($"{a} -> {b}: {moved.Count} of {compared} books compared moved"
            + (onlyA + onlyB > 0 ? $"  (⚠️ set differs: -{onlyA} +{onlyB})" : ""));
        if (moved.Count == 0)
            Console.WriteLine("  ⚠️ NOTHING MOVED — the poison missed. This says nothing about "
                + "any fixture; it says the poison did not reach the engine.");
        foreach (var m in moved.Take(60))
            Console.WriteLine("   moved: " + m);
        if (moved.Count > 60)
            Console.WriteLine($"   ... and {moved.Count - 60} more");

        foreach (var c in claimants)
        {
            var key = A.Keys.FirstOrDefault(k =>
                k.EndsWith("/" + c + ".lys", StringComparison.Ordinal) || k == c + ".lys");
            if (key == null) { Console.WriteLine($"  CLAIMANT {c}: NOT FOUND in the sweep"); continue; }
            Console.WriteLine($"  CLAIMANT {c}: "
                + (moved.Contains(key) ? "MOVED -- it observes" : "did NOT move -- blind to THIS poison"));
        }
        return true;
    }

    private static Dictionary<string, string> Load(string root, string label)
    {
        var path = Path.Combine(OutDir(root), label + ".csv");
        if (!File.Exists(path)) throw new FileNotFoundException(null, label);
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            int i = line.IndexOf(',');
            if (i > 0) d[line[..i]] = line[(i + 1)..];
        }
        return d;
    }
}
