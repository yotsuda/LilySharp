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
/// A chord member's own string number and script reach the twin on that member, as a note's
/// do (<c>&lt;a,\2 d&gt;</c>, LilyPond's chord-scripts.ly <c>&lt;c-. e-.&gt;</c>). Until
/// 2026-10-02 both were "dropped (out of scope)" — three of the owner's tab books wrote them.
/// </summary>
[Trait("Category", "Unit")]
public class LilyPondExporterChordMemberTests
{
    [Theory]
    [InlineData("<a,\\2 d>4", "<a,\\2 d>4")]
    [InlineData("<c'@staccato e'@staccato>4", "<c'-\\staccato e'-\\staccato>4")]
    [InlineData("<c'@marcato.down e'@marcato.up>4", "<c'_\\marcato e'^\\marcato>4")]
    public void AMembersPostEvent_IsWrittenOnTheMember(string chord, string expected)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            part m { clef treble }
            section A { m { {{chord}} r2. | } }
            form main { ~A }
            score main { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(tree);
        Assert.Contains(expected, ly);
        Assert.DoesNotContain(exporter.Warnings, w => w.Contains("chord member"));
    }
}
