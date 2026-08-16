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
using System.Reflection;
using System.Text.RegularExpressions;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A diagnostic code names ONE diagnostic.
/// </summary>
/// <remarks>
/// <para>
/// LYS0014 was held by two of them at once — <c>KeyModeAssumedMajor</c> and
/// <c>UnexpectedCharacter</c> — for as long as it took someone to read the file twice. It
/// happened because the second one was appended under the <c>LYS7xxx</c> heading, where the
/// next free number in the <c>LYS0xxx</c> band is nowhere in view. Nothing failed: both
/// diagnostics still fired, both still carried a code, and every caller names the SYMBOL, so
/// the only visible symptom was two unrelated messages a user could not tell apart in a
/// Problems list or a filter.
/// </para>
/// <para>
/// So this is the net rather than the tidy-up: the layout that caused it can come back, but
/// a second holder of a number cannot. Codes are RETIRED, never reused (LYS0013 was the
/// removed <c>version</c> directive), so this asserts uniqueness only — it must not
/// require the numbers to be dense.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class DiagnosticCodeTests
{
    private static (string Name, string Code)[] AllCodes() =>
        typeof(DiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!))
            .ToArray();

    [Fact]
    public void EveryDiagnosticCodeIsHeldByExactlyOneDiagnostic()
    {
        var codes = AllCodes();
        Assert.NotEmpty(codes);

        var shared = codes
            .GroupBy(c => c.Code, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} is held by {string.Join(" and ", g.Select(c => c.Name))}")
            .ToList();

        Assert.True(shared.Count == 0,
            "a diagnostic code must name one diagnostic; retire numbers, never share them:\n  "
            + string.Join("\n  ", shared));
    }

    [Fact]
    public void EveryDiagnosticCodeIsWellFormed()
    {
        foreach (var (name, code) in AllCodes())
        {
            Assert.True(
                code.Length == 7 && code.StartsWith("LYS", StringComparison.Ordinal)
                    && code[3..].All(char.IsAsciiDigit),
                $"{name} = \"{code}\" is not a LYSnnnn code.");
        }
    }

    /// <summary>
    /// A declared code that nothing emits is a rule the language does not have. LYS1015
    /// <c>MultipleFormDeclarations</c> was one — it says a file may hold one form, which
    /// stopped being true (<c>test/multi-movement.lys</c> declares three) — and it sat
    /// undetected because nothing looks at the SET of codes; every existing net looks at one
    /// code at a time, and a code nobody names is a code nobody's test mentions either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NAMED LIST rather than a count, for the reason recorded on
    /// <c>LpReferenceCitationTests</c>'s ratchet: with a count, an old entry can stay open
    /// while a new one is added and the total never moves. Give a code an emitter and its
    /// name must leave this list, or the test fails — that is the ratchet.
    /// </para>
    /// <para>
    /// MEASURED 2026-08-16 over LilySharp.Core / .Lsp / .Cli (352 files): 96 codes declared,
    /// 89 named by a caller, 7 named by nobody — and none of the 7 was emitted through its
    /// string literal either (0 occurrences of "LYS0001", "LYS0005", … outside the
    /// declaration). HANDOFF §2F carried this as "LYS1015 has no emitter"; that was the
    /// count of a targeted grep, not of the family. All seven were retired (user decision,
    /// same day), which is why the list below is empty rather than seven names long.
    /// </para>
    /// <para>
    /// ⚠️ TESTS ARE NOT SCANNED on purpose: a code a test names but no validator raises still
    /// has no emitter, and counting the test would hide exactly the case this exists to find.
    /// </para>
    /// </remarks>
    [Fact]
    public void CodesThatNothingEmits_DoNotGrow()
    {
        // Empty, and meant to stay empty. The seven that were here on 2026-08-16 were
        // retired the same day (their numbers are listed on DiagnosticCodes itself);
        // retiring one means DELETING the constant, because codes are retired and never
        // reused — see this class's remarks. Adding a name here is admitting a rule the
        // language does not have, so it wants a reason beside it.
        string[] known = [];

        var emitted = EmittedCodeNames();

        // §5.4's empty-set trap, both halves: if the scan found no sources, or matched no
        // callers, "nothing is unemitted" would be vacuously true and this test would guard
        // an empty comparison for as long as the layout stayed broken.
        Assert.True(AllCodes().Length >= 60, $"only {AllCodes().Length} codes were reflected.");
        Assert.True(emitted.Count >= 60, $"only {emitted.Count} codes were seen being named.");

        var unemitted = AllCodes().Select(c => c.Name).Where(n => !emitted.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(known.OrderBy(n => n, StringComparer.Ordinal).ToArray(), unemitted);
    }

    /// <summary>
    /// The names of <see cref="DiagnosticCodes"/> members that the PRODUCT sources name —
    /// read from source, because the fact wanted is "does any code path reach this", which
    /// reflection over a compiled assembly cannot answer for a <c>const string</c> (the
    /// compiler inlines it and the field vanishes from the call sites).
    /// </summary>
    private static HashSet<string> EmittedCodeNames()
    {
        var root = RepoRoot();
        var sources = new[] { "LilySharp.Core", "LilySharp.Lsp", "LilySharp.Cli" }
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        // The declaring file names every code once, by definition.
                        && Path.GetFileName(f) != "Diagnostic.cs");

        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in sources)
            foreach (var line in File.ReadAllLines(file))
                foreach (Match m in Regex.Matches(StripComment(line), @"DiagnosticCodes\.(\w+)"))
                    named.Add(m.Groups[1].Value);
        return named;
    }

    /// <summary>
    /// The code part of a line. A remark that MENTIONS a code (this file is full of them)
    /// must not count as raising it — that error runs the wrong way for a ratchet, marking a
    /// dead code alive and hiding exactly what the scan is for.
    /// </summary>
    /// <remarks>
    /// A <c>//</c> inside a string literal truncates the line early, which can only LOSE a
    /// reference — and a lost reference fails this test loudly rather than passing it
    /// quietly, so the imprecision falls on the safe side. Block comments are not handled;
    /// none of these sources puts code after one on the same line.
    /// </remarks>
    private static string StripComment(string line)
    {
        int slashes = line.IndexOf("//", StringComparison.Ordinal);
        return slashes < 0 ? line : line[..slashes];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "LilySharp.Core")))
            dir = dir.Parent;
        return dir?.FullName
               ?? throw new InvalidOperationException("could not find the repository root.");
    }
}
