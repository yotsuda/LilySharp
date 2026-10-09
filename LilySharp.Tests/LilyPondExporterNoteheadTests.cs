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

using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>@notehead(style)</c> reaches the twin as LilyPond's NoteHead style, set once before the
/// note (or chord). Until 2026-10-02 every one was "dropped (out of scope)" — 8,018 of the
/// repository's twin warnings. The page draws LilyPond's own glyphs for these styles
/// (Lab sessions/p739/twin nh: the two pictures agree).
/// </summary>
[Trait("Category", "Unit")]
public class LilyPondExporterNoteheadTests
{
    [Theory]
    [InlineData("c'4@notehead(x)", "\\once \\override NoteHead.style = #'cross c'4")]
    [InlineData("c'4@notehead(cross)", "\\once \\override NoteHead.style = #'cross c'4")]
    [InlineData("c'2@notehead(diamond)", "\\once \\override NoteHead.style = #'diamond c'2")]
    [InlineData("c'4@notehead(triangle)", "\\once \\override NoteHead.style = #'triangle c'4")]
    [InlineData("<c' e'>4@notehead(slash)", "\\once \\override NoteHead.style = #'slash <c' e'>4")]
    [InlineData("c'4@notehead(xcircle)", "\\once \\override NoteHead.style = #'xcircle c'4")]
    public void TheStyle_IsSetOnceBeforeTheNote(string note, string expected)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            part m { clef treble }
            section A { m { {{note}} r2. | } }
            form { ~A }
            score { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(tree);
        Assert.Contains(expected, ly);
        Assert.DoesNotContain(exporter.Warnings, w => w.Contains("notehead"));
    }
}
