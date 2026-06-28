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

using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Exporting a Lily# tree back to LilyPond (.ly) source.
/// </summary>
[Trait("Category", "Unit")]
public class LilyPondExporterTests
{
    private static string Export(string source) =>
        new LilyPondExporter().Export(SyntaxTree.Parse(source));

    private const string Source = """
        octave absolute
        title "round trip"
        tempo 120
        key d major
        part bl {
          clef bass
          tuning bass
          section Main { c,4 d, e@dead fis, | g,8\4 a, b, c~ c r2 | }
        }
        structure { ~Main }
        score { staff bl  tab bl }
        """;

    [Fact]
    public void EmitsHeaderAndDirectives()
    {
        var ly = Export(Source);
        Assert.Contains("\\version", ly);
        Assert.Contains("title = \"round trip\"", ly);
        Assert.Contains("\\tempo 4 = 120", ly);
        Assert.Contains("\\key d \\major", ly);
        Assert.Contains("\\clef bass", ly);
    }

    [Fact]
    public void EmitsNotesStringNumberTieAndDeadNote()
    {
        var ly = Export(Source);
        Assert.Contains("\\deadNote", ly);   // e@dead -> \deadNote
        Assert.Contains("\\4", ly);          // string number preserved
        Assert.Contains("~", ly);            // tie preserved
    }

    [Fact]
    public void EmitsBothStavesWithTuning()
    {
        var ly = Export(Source);
        Assert.Contains("\\new Staff", ly);
        Assert.Contains("\\new TabStaff", ly);
        Assert.Contains("bass-four-string-tuning", ly);
        Assert.Contains("\\music", ly);      // staves reference the music variable
    }

    [Fact]
    public void AbsoluteOctaveAddsOneOctaveForLilyPondAnchor()
    {
        // Lily# bare c = C4; LilyPond bare c = C3. A Lily# `c,` (C3) must export as
        // LilyPond `c` (C3) — the +1 octave remap removes the comma. A Lily# bare
        // `c` (C4) becomes LilyPond `c'`.
        Assert.Contains("c4 ", ExportNote("c,4"));     // C3 -> bare c
        Assert.Contains("c'4 ", ExportNote("c4"));     // C4 -> c'
    }

    private static string ExportNote(string note) => Export($$"""
        octave absolute
        part bl { clef bass section S { {{note}} } }
        structure { ~S }
        score { staff bl }
        """);
}
