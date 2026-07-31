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
using System.Linq;
using System.Reflection;
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
}
