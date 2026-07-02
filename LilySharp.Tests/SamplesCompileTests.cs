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
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Every shipped sample in <c>samples/</c> must parse without errors or
/// warnings and render to SVG. This is what keeps the public samples from
/// rotting as the language moves (the previous sample folder died precisely
/// because nothing compiled it).
/// </summary>
[Trait("Category", "Unit")]
public class SamplesCompileTests
{
    public static IEnumerable<object[]> SampleFiles()
    {
        var dir = FindSamplesDir();
        foreach (var f in Directory.EnumerateFiles(dir, "*.lys"))
            yield return new object[] { Path.GetFileName(f) };
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Sample_CompilesCleanAndRenders(string fileName)
    {
        var path = Path.Combine(FindSamplesDir(), fileName);
        var source = File.ReadAllText(path);

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors,
            $"{fileName}: parse errors:\n" + string.Join("\n",
                tree.Diagnostics.Select(d => $"  {d.Code}: {d.Message}")));

        var outputs = SvgGenerator.GenerateAll(tree);
        Assert.NotEmpty(outputs);
        Assert.All(outputs, o => Assert.False(
            string.IsNullOrWhiteSpace(o.Svg), $"{fileName}: empty SVG for {o.Filename}"));
    }

    private static string FindSamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "samples")))
            dir = Path.GetDirectoryName(dir);
        Assert.False(dir == null, "samples/ directory not found above test bin");
        return Path.Combine(dir!, "samples");
    }
}
