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
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>chords harmony silent</c> in a score: the row is printed and sounds nothing (grammar
/// evaluation 5's B8, owner's choice 2026-11-23 — the word in the score, where the other ways
/// a row is shown are said). MEASURED before (Lab sessions/p1370 s1): a band score's chords row
/// sounded beside the keyboard that played the same chords, and nothing could silence it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChordRowSilentTests
{
    private static string Book(string row) => $$"""
        octave absolute
        part keys { instrument piano-right }
        section A { keys { <c' e' g'>1 | } chords harmony { C | } chords { G | } }
        form { A }
        score { {{row}}  staff keys }
        """;

    private static int[] Played(string src) =>
        [.. new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch).OrderBy(p => p)];

    [Fact]
    public void ASilentRowSoundsNothing()
    {
        Assert.Equal(new[] { 60, 64, 67 }, Played(Book("chords harmony silent")));
        Assert.Equal(new[] { 60, 64, 67 }, Played(Book("chords silent")));      // the unnamed row
        Assert.Equal(new[] { 60, 64, 67 }, Played(Book("chords harmony as roman silent")));
    }

    [Fact]
    public void ARowThatSaysNothingStillSounds()
        => Assert.True(Played(Book("chords harmony")).Length > 3);

    [Fact]
    public void ASilentRowIsPrintedAndExported()
    {
        string src = Book("chords harmony silent");
        Assert.Empty(SyntaxTree.Parse(src).Diagnostics);
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(src)), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(">C<", SvgGenerator.Generate(SyntaxTree.Parse(src)));
        Assert.Contains("<harmony", new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().ToString());
    }

    [Fact]
    public void TheWordIsNotTakenForAPart()
    {
        var row = SyntaxTree.Parse(Book("chords silent")).GetRoot().DescendantNodes<ChordRowRenderSyntax>().Single();
        Assert.True(row.IsSilent);
        Assert.Null(row.NameToken);
        var named = SyntaxTree.Parse(Book("chords harmony silent")).GetRoot().DescendantNodes<ChordRowRenderSyntax>().Single();
        Assert.Equal("harmony", named.PartName);
    }
}
