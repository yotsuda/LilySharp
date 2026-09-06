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

using System.Diagnostics;

namespace LilySharp.Cli;

/// <summary>
/// <c>--batch &lt;list&gt;</c>: run ONE command over MANY files in ONE process.
/// </summary>
/// <remarks>
/// ★ WHY IT EXISTS — MEASURED, NOT ASSUMED (2026-09-06, session 340). A whole-corpus sweep
/// costs one <c>lysc</c> launch per (book × output × side), and the launch itself dominates:
/// on an idle machine, median of 7, a ONE-BAR file takes 1352 ms through <c>lysc svg</c>
/// (1049 ms with <c>--no-embed-font</c>) and 245 ms through <c>lysc check</c>, while a real
/// three-page book takes 2390 ms — i.e. about a second of actual work and the rest fixed.
/// A 923-book sweep over five outputs and two builds is 3,094 launches, which is over an
/// hour of pure start-up before any music is engraved.
/// <para>
/// ⚠️ READYTORUN IS NOT THIS. Publishing with <c>-p:PublishReadyToRun=true</c> takes
/// <c>svg</c> from 1352 ms to 838 ms, but it makes <c>check</c> SLOWER (245 → 363 ms), and
/// either way the cost is still paid PER FILE. Batching removes it instead of shrinking it.
/// </para>
/// <para>
/// ⚠️ THE FLAG IS TAKEN BEFORE DISPATCH, like <c>--verbose</c>, so it belongs to every
/// command at once and no per-command parser knows about it. The loop hands the SAME option
/// array to the command each time with the file's own positionals appended, so a batch means
/// exactly what the single-file runs mean — there is no second interpretation of the options
/// to keep in step.
/// </para>
/// </remarks>
internal static class Batch
{
    /// <summary>One line of the list: an input, and optionally the output it names.</summary>
    private readonly record struct Entry(string Input, string? Output);

    /// <summary>
    /// Detects and STRIPS <c>--batch &lt;list&gt;</c> from <paramref name="args"/>, returning
    /// the list path (<c>-</c> for standard input) or null.
    /// </summary>
    public static string? Take(ref string[] args)
    {
        int i = Array.IndexOf(args, "--batch");
        if (i < 0)
            return null;
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine("Error: --batch requires a list file (or - for stdin)");
            return "";   // empty: Run reports and fails, rather than silently running once
        }
        string list = args[i + 1];
        args = args.Take(i).Concat(args.Skip(i + 2)).ToArray();
        return list;
    }

    /// <summary>
    /// Runs <paramref name="command"/> once per entry of <paramref name="listPath"/>, sharing
    /// this process. Returns 0 only when every file succeeded.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE FILE'S FAILURE DOES NOT STOP THE REST. A sweep over a live corpus meets books
    /// that do not parse, and stopping at the first would make the run useless for the other
    /// nine hundred; the count of failures comes back in the exit code instead. This is the
    /// one place the batch differs from N separate runs, and it is deliberate.
    /// </remarks>
    public static int Run(string listPath, string command, string[] args,
                          Func<string, string[], int> dispatch)
    {
        if (listPath.Length == 0)
            return 1;   // Take already said why

        // ⚠️ -o NAMES ONE FILE, so it cannot name a batch of them: every book would write
        // over the last. The list's own second column is how a batch names its outputs.
        if (args.Contains("-o") || args.Contains("--output"))
        {
            Console.Error.WriteLine(
                "Error: --batch and -o/--output are mutually exclusive — one output path "
                + "cannot name many files.");
            Console.Error.WriteLine(
                "       Put the output in the list instead: '<input>\\t<output>' per line.");
            return 1;
        }

        List<Entry> entries;
        try
        {
            entries = ReadList(listPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(CliParser.Verbose ? ex.ToString() : $"Error: {ex.Message}");
            return 1;
        }

        if (entries.Count == 0)
        {
            Console.Error.WriteLine($"Error: no input files in {Describe(listPath)}");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        int failed = 0;
        for (int n = 0; n < entries.Count; n++)
        {
            var e = entries[n];
            Console.WriteLine($"[{n + 1}/{entries.Count}] {e.Input}");
            string[] one = e.Output is null
                ? [.. args, e.Input]
                : [.. args, e.Input, e.Output];
            int rc;
            try
            {
                rc = dispatch(command, one);
            }
            catch (Exception ex)
            {
                // The per-command paths catch their own exceptions, but a batch must not be
                // ended by one that gets through — the remaining files are still owed a run.
                Console.Error.WriteLine(CliParser.Verbose ? ex.ToString() : $"Error: {ex.Message}");
                rc = 1;
            }
            if (rc != 0)
                failed++;
        }

        Console.WriteLine(
            $"Batch: {entries.Count} file(s), {failed} failed, "
            + $"{sw.Elapsed.TotalSeconds:F1} s ({sw.Elapsed.TotalMilliseconds / entries.Count:F0} ms/file)");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// One entry per non-blank line: <c>input</c>, or <c>input TAB output</c>. A line whose
    /// first non-blank character is <c>#</c> is a comment.
    /// </summary>
    /// <remarks>
    /// ⚠️ TAB, NOT SPACE, is the separator, because a real corpus has spaces in its
    /// filenames — the owner's own books are called things like
    /// <c>クリスマスメドレー (UiPath).lys</c>. Splitting on whitespace would break exactly
    /// the population a batch is for.
    /// <para>
    /// ⚠️ PATHS ARE USED AS WRITTEN, resolved against the working directory like any
    /// positional argument — not against the list file's own directory. One rule for where a
    /// path points, whether it arrives on the command line or in a list.
    /// </para>
    /// </remarks>
    private static List<Entry> ReadList(string listPath)
    {
        var lines = listPath == "-"
            ? ReadAllStdin()
            : File.ReadAllLines(listPath);

        var entries = new List<Entry>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            int tab = line.IndexOf('\t');
            entries.Add(tab < 0
                ? new Entry(line, null)
                : new Entry(line[..tab].TrimEnd(), line[(tab + 1)..].Trim()));
        }
        return entries;
    }

    private static IEnumerable<string> ReadAllStdin()
    {
        string? line;
        while ((line = Console.In.ReadLine()) != null)
            yield return line;
    }

    private static string Describe(string listPath)
        => listPath == "-" ? "standard input" : listPath;
}
