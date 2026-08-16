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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

/// <summary>
/// Counts what every <c>.lys</c> ON DISK actually contains, from the COLLECTED model.
/// </summary>
/// <remarks>
/// This exists to answer "how far does a cost reach" before anyone optimises for it. The
/// question it settled first (session 190): an articulation costs ~0.15 MB of allocation per
/// keystroke, so how many real books carry enough of them to notice? Answer: of 1026 books
/// on disk, 961 carry none, 58 carry 1..49, none carry 50..499, and the 7 with 500+ are all
/// synthetic perf books.
/// <para>
/// ⚠️ IT READS THE MODEL, NOT THE SOURCE. Grepping for <c>@staccato</c> counts spellings; a
/// phrase referenced ten times contributes its scripts ten times to the engine and once to
/// the grep. The reach question is about what the engine does.
/// </para>
/// <para>
/// ⚠️ IT SCANS THE WHOLE WORKING TREE, including git-ignored directories. That is deliberate
/// — the user's own scores usually live in one — so a count from this tool is a claim about
/// THIS MACHINE, never about the notation. Say which when quoting it.
/// </para>
/// </remarks>
internal static class Census
{
    public static void Run(string root, int top)
    {
        var files = Directory.GetFiles(root, "*.lys", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                     && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !p.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();

        var rows = new List<(string Rel, int Scripts, int Fingerings, int Measures, int Kb)>();
        int threw = 0;
        foreach (var p in files)
        {
            try
            {
                // LF-canonical for the same reason Sweep is: the collected model must not
                // depend on how git materialised the file.
                var tree = SyntaxTree.Parse(File.ReadAllText(p).Replace("\r\n", "\n"));
                var score = SvgGenerator.CollectScore(tree, null);
                int measures = score.EnumerateStaves()
                    .Select(s => s.Staff.PrimaryVoice.Measures.Length).DefaultIfEmpty(0).Max();
                // Single notes only: a chord carries its fingerings on its members. The
                // column is context, not a finding — do not quote it as a total.
                int fingerings = score.EnumerateStaves()
                    .SelectMany(s => s.Staff.Voices).SelectMany(v => v.Measures)
                    .SelectMany(m => m.Items)
                    .Count(it => it is NoteItem { Fingering: not null });
                rows.Add((Path.GetRelativePath(root, p).Replace('\\', '/'),
                    score.Articulations.Length, fingerings, measures,
                    (int)(new FileInfo(p).Length / 1024)));
            }
            catch { threw++; }
        }

        Console.WriteLine($"{files.Length} .lys on disk, {rows.Count} collected, {threw} threw");
        Console.WriteLine();

        int[] cuts = { 1, 50, 200, 500, 1000, 2000, 5000 };
        Console.WriteLine("books by articulation count");
        Console.WriteLine($"  {"0",-16}{rows.Count(r => r.Scripts == 0),6}");
        for (int i = 0; i < cuts.Length; i++)
        {
            int lo = cuts[i], hi = i + 1 < cuts.Length ? cuts[i + 1] : int.MaxValue;
            Console.WriteLine($"  {(hi == int.MaxValue ? $">={lo}" : $"{lo}..{hi - 1}"),-16}"
                + $"{rows.Count(r => r.Scripts >= lo && r.Scripts < hi),6}");
        }
        Console.WriteLine();

        // Per area, because the distribution above is dominated by fixtures and synthetic
        // perf books while the question is usually about real scores.
        Console.WriteLine("worst book per area");
        foreach (var g in rows.GroupBy(r => Area(r.Rel)).OrderByDescending(g => g.Max(r => r.Scripts)))
        {
            var w = g.OrderByDescending(r => r.Scripts).First();
            Console.WriteLine($"  {g.Key,-34}{g.Count(),5} books   worst {w.Scripts,6} scripts  {w.Rel}");
        }
        if (top <= 0) return;

        Console.WriteLine();
        Console.WriteLine($"heaviest {top} books");
        Console.WriteLine("  scripts   fing   bars     KB  book");
        foreach (var r in rows.OrderByDescending(r => r.Scripts).Take(top))
            Console.WriteLine($"  {r.Scripts,7} {r.Fingerings,6} {r.Measures,6} {r.Kb,6}  {r.Rel}");
    }

    private static string Area(string rel)
    {
        if (rel.StartsWith("audit/lpreg/perf-", StringComparison.Ordinal))
            return "audit/lpreg (synthetic perf)";
        int i = rel.IndexOf('/');
        if (i < 0) return "(root)";
        int j = rel.IndexOf('/', i + 1);
        return j < 0 ? rel[..i] : rel[..j];
    }
}
