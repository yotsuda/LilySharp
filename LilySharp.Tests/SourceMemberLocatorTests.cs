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

using System.IO;
using System.Linq;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <see cref="SourceMemberLocator"/>, the address the generated inventories print in place of
/// a line number (session 871): a comment belongs to the member it documents or stands in, a
/// line between members to the type, and an edit ABOVE a site does not move its address.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SourceMemberLocatorTests
{
    private const string Source =
        "namespace N;\n"                                  // 0
        + "/// LILYSHARP-OWN: the type's remark\n"        // 1
        + "internal static class Outer\n"                 // 2
        + "{\n"                                           // 3
        + "    /// LILYSHARP-OWN: the field's\n"          // 4
        + "    public const double Gap = 1.5;\n"          // 5
        + "\n"                                            // 6
        + "    // above the method: the method's\n"       // 7
        + "    static int Walk()\n"                       // 8
        + "    {\n"                                       // 9
        + "        // inside: still the method's\n"       // 10
        + "        return 42;\n"                          // 11
        + "    }\n"                                       // 12
        + "    private sealed class Inner { int Count => 10; }\n" // 13
        + "    // after the last member: the type's\n"    // 14
        + "}\n";                                          // 15

    [Fact]
    public void EachLine_NamesTheMemberItStandsIn()
    {
        var at = SourceMemberLocator.Of(Source);

        Assert.Equal(16, at.Length);   // as File.ReadAllLines counts: no line after the last newline
        Assert.Equal("Outer", at[1]);
        Assert.Equal("Outer.Gap", at[4]);
        Assert.Equal("Outer.Gap", at[5]);
        Assert.Equal("Outer.Walk", at[7]);
        Assert.Equal("Outer.Walk", at[10]);
        Assert.Equal("Outer.Walk", at[11]);
        Assert.Equal("Outer.Inner", at[13]);   // a line is placed by its FIRST token: the nested type
        Assert.Equal("Outer", at[14]);
    }

    [Fact]
    public void ALineAddedAbove_MovesNoAddress()
    {
        string edited = Source.Replace("    public const double Gap = 1.5;\n",
            "    public const double Gap = 1.5;\n    // a new remark\n    static void Added() { }\n");

        var before = SourceMemberLocator.Of(Source);
        var after = SourceMemberLocator.Of(edited);

        Assert.Equal(before[11], after[11 + 2]);   // `return 42;` is still Walk's
        Assert.Equal(before[14], after[14 + 2]);
    }

    [Theory]
    [InlineData("a\r\nb\r\n")]
    [InlineData("a\nb\rc")]
    [InlineData("a\n\n")]
    public void TheLineCount_IsReadAllLines(string text)
    {
        string path = Path.Combine(Path.GetTempPath(), "lilysharp-locator-" + System.Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(path, text);
        try { Assert.Equal(File.ReadAllLines(path).Length, SourceMemberLocator.Of(text).Length); }
        finally { File.Delete(path); }
    }
}
