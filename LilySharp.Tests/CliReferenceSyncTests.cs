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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>docs/CLI_REFERENCE.md</c> documents the commands and options the CLI actually has.
/// </summary>
/// <remarks>
/// ★ WRITTEN BECAUSE IT HAD ALREADY DRIFTED, and silently. Measured 2026-09-06, before this
/// guard existed: FOUR commands had no section at all (<c>vsqx</c>, <c>ly</c>,
/// <c>import</c>, <c>harmonize</c>) and six more were missing options —
/// <c>svg --all/--combined</c>, <c>png --crop</c>, <c>midi --score/--all</c>,
/// <c>xml --score/--all</c>, and no option table at all on <c>check</c> or <c>layout</c>.
/// The help text is hard-coded in fourteen raw string literals in <c>Program.cs</c> and the
/// reference is prose beside it; nothing connected them, so every command added since the
/// document was written simply failed to appear in it.
/// <para>
/// ⚠️ THE HELP IS THE SOURCE OF TRUTH, not the document: the help is what a user reads at
/// the terminal, and it cannot be wrong about the flags the parser accepts without the
/// command visibly failing. So this asks the CLI what it has and requires the document to
/// carry it — never the reverse. A document that says MORE than the help is not failed
/// here; it is free to explain.
/// </para>
/// <para>
/// ⚠️ SPAWNS THE REAL CLI as <c>dotnet lysc.dll</c>, for the reason
/// <c>CliBestEffortOutputTests</c> gives at length (never the apphost).
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public class CliReferenceSyncTests
{
    private static readonly string Muxer = Path.Combine(
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    private static string Help(params string[] args)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "lysc.dll");
        Assert.True(File.Exists(dll), $"lysc.dll not beside the tests: {dll}");
        Assert.True(File.Exists(Muxer), $"no dotnet host where the runtime says one is: {Muxer}");

        var psi = new ProcessStartInfo(Muxer)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        foreach (string a in args) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("--help");

        using var p = Process.Start(psi)!;
        string text = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        return text;
    }

    private static string Reference()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "docs", "CLI_REFERENCE.md");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("docs/CLI_REFERENCE.md not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The verbs listed under "Commands:" in the global help.</summary>
    private static List<string> Commands()
    {
        var names = new List<string>();
        bool inList = false;
        foreach (string line in Help().Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.TrimStart().StartsWith("Commands:", StringComparison.Ordinal)) { inList = true; continue; }
            if (inList && line.Length > 0 && !char.IsWhiteSpace(line[0])) break;
            var m = Regex.Match(line, @"^\s{2,}([a-z][a-z0-9-]*)\s{2,}\S");
            if (inList && m.Success) names.Add(m.Groups[1].Value);
        }
        Assert.True(names.Count >= 10, $"only {names.Count} commands parsed out of the global help");
        return names;
    }

    /// <summary>The option tokens in one command's "Options:" block.</summary>
    private static List<string> OptionsOf(string command)
    {
        var opts = new List<string>();
        bool inBlock = false;
        foreach (string line in Help(command).Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.TrimStart().StartsWith("Options:", StringComparison.Ordinal)) { inBlock = true; continue; }
            if (inBlock && line.Length > 0 && !char.IsWhiteSpace(line[0])) break;
            if (!inBlock) continue;
            // Only a line that STARTS an entry: continuation lines are prose that may
            // legitimately contain a dash.
            if (!Regex.IsMatch(line, @"^\s+-")) continue;
            foreach (Match m in Regex.Matches(line, @"(--?[A-Za-z][-A-Za-z]*)"))
                opts.Add(m.Groups[1].Value);
        }
        return opts.Distinct().ToList();
    }

    /// <summary>The `### &lt;command&gt; - …` section of the reference, or null.</summary>
    private static string? SectionFor(string doc, string command)
    {
        var m = Regex.Match(doc, $@"(?s)###\s+{Regex.Escape(command)}\s+-\s.*?(?=\n###\s|\n##\s|\z)");
        return m.Success ? m.Value : null;
    }

    /// <summary>Just the Markdown TABLE ROWS of a section — the Options table.</summary>
    /// <remarks>
    /// ⚠️ NOT THE WHOLE SECTION, and poisoning is what said so. Written first as "the token
    /// appears anywhere in the section", deleting the <c>--combined</c> row left the guard
    /// GREEN, because the flag still appeared in an example line below. A reference whose
    /// only mention of an option is an unexplained example is the drift this exists to catch,
    /// and the failure message already told the reader to add a table row — so the check has
    /// to look where the message points.
    /// </remarks>
    private static string TableOf(string section) =>
        string.Join("\n", section.Split('\n').Where(l => l.TrimStart().StartsWith('|')));

    [Fact]
    public void EveryCommandTheCliOffersHasASectionInTheReference()
    {
        string doc = Reference();
        var missing = Commands().Where(c => SectionFor(doc, c) is null).ToList();

        Assert.True(missing.Count == 0,
            $"docs/CLI_REFERENCE.md has no `### <command> - …` section for: {string.Join(", ", missing)}. "
            + "Four commands were missing when this guard was written, each because it was added "
            + "to the CLI and the reference was not touched. Add the section; the help text for "
            + "the command is the copy to work from.");
    }

    [Fact]
    public void EveryOptionTheCliOffersIsNamedInItsSection()
    {
        string doc = Reference();
        var gaps = new List<string>();

        foreach (string command in Commands())
        {
            string? section = SectionFor(doc, command);
            if (section is null) continue;   // the other test owns that failure
            string table = TableOf(section);
            var absent = OptionsOf(command).Where(o => !table.Contains(o, StringComparison.Ordinal)).ToList();
            if (absent.Count > 0)
                gaps.Add($"{command}: {string.Join(" ", absent)}");
        }

        Assert.True(gaps.Count == 0,
            "docs/CLI_REFERENCE.md does not name every option `lysc <cmd> --help` offers:\n  "
            + string.Join("\n  ", gaps)
            + "\nThe help is the source of truth — it is what a user reads at the terminal. "
            + "Add the row to that command's Options table.");
    }

    [Fact]
    public void TheGlobalOptionsAreInTheReferenceToo()
    {
        // --batch and --verbose belong to the RUN rather than to a command (they are taken
        // before dispatch), so they appear in no command's Options block and the two tests
        // above cannot see them.
        string doc = Reference();
        foreach (string flag in new[] { "--batch", "--verbose", "--version", "--help" })
            Assert.True(doc.Contains(flag, StringComparison.Ordinal),
                $"docs/CLI_REFERENCE.md never mentions the global option {flag}");
    }
}
