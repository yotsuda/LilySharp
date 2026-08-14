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

using System.Text;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// Offset → (line, character) is asked once per token, so it was the quadratic
/// term behind "semanticTokens takes 1.7 s on a long score". The lookup is now a
/// binary search over a cached line index; these tests pin it to the behaviour of
/// the scan-from-zero version it replaced, INCLUDING the line-ending edge cases
/// that a negative character would turn into a rejected response.
/// </summary>
[Trait("Category", "Unit")]
public class GetLineAndCharacterTests
{
    [Theory]
    [InlineData("")]
    [InlineData("one line, no break")]
    [InlineData("a\nb\nc")]                       // LF
    [InlineData("a\r\nb\r\nc")]                   // CRLF
    [InlineData("a\rb\rc")]                       // lone CR
    [InlineData("a\r\nb\nc\rd\r\n")]              // mixed, trailing break
    [InlineData("\n\n\n")]                        // empty lines
    [InlineData("\r\n\r\n")]
    [InlineData("c'4@startTrillSpan d'4 |\r\n  e'4 f'4 |\n")]
    public void AgreesWithTheScanFromZero_AtEveryOffset(string text)
    {
        for (int position = -2; position <= text.Length + 2; position++)
        {
            var fast = LilySharpLanguageServer.GetLineAndCharacter(text, position);
            var scan = LilySharpLanguageServer.GetLineAndCharacterByScan(text, position);
            Assert.True(fast == scan,
                $"offset {position} of {Format(text)}: fast {fast} != scan {scan}");
        }
    }

    [Fact]
    public void NeverReportsANegativeCharacter()
    {
        const string text = "a\r\nb\r\n";
        for (int position = 0; position <= text.Length; position++)
            Assert.True(LilySharpLanguageServer.GetLineAndCharacter(text, position).character >= 0);
    }

    /// <summary>
    /// The point of the change: cost must stop growing with the document for a
    /// single lookup. Ten thousand lookups spread over a long document are timed
    /// against the same count on a short one — the scan-from-zero version made the
    /// long document ~1000× dearer, which is what this rejects. The bound is loose
    /// (30×) so a loaded machine cannot fail it spuriously.
    /// </summary>
    [Fact]
    public void CostDoesNotGrowWithTheDocument()
    {
        static string Doc(int lines)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines; i++) sb.Append("  c'4 d'4 e'4 f'4 |\n");
            return sb.ToString();
        }

        static double Time(string text)
        {
            // Warm the line index so the measurement is the lookups, not the build.
            LilySharpLanguageServer.GetLineAndCharacter(text, 0);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10_000; i++)
                LilySharpLanguageServer.GetLineAndCharacter(text, i % text.Length);
            return sw.Elapsed.TotalMilliseconds;
        }

        var shortDoc = Doc(20);
        var longDoc = Doc(20_000);

        double small = Time(shortDoc);
        double large = Time(longDoc);

        Assert.True(large < System.Math.Max(30 * small, 50),
            $"lookups on a {longDoc.Length:N0}-char document took {large:N1} ms vs "
            + $"{small:N1} ms on a {shortDoc.Length:N0}-char one — the per-call cost is "
            + "growing with the document again.");
    }

    private static string Format(string text) =>
        "\"" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
