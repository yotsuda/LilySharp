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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The unnamed part, chords and lyrics (owner's decision 2026-10-09,
/// docs/anonymous-blocks-design.md): each answers to an internal name a writer cannot spell
/// (<see cref="SyntaxFacts.UnnamedPartName"/> and its two siblings), and a score's bare
/// <c>staff</c> / <c>tab</c> / <c>chords</c> / <c>lyrics</c> names it.
/// </summary>
[Trait("Category", "Unit")]
public class AnonymousBlockTests
{
    private static Diagnostic[] Errors(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Severity == DiagnosticSeverity.Error)];
    }

    private static string Svg(string source) => SvgGenerator.Generate(SyntaxTree.Parse(source));

    private const string OnePart = """
        part {
          clef treble
          section A { c'4 d e f | g2 g | }
        }
        chords { section A { C | G } }
        lyrics { section A { Twin- kle twin- kle | lit- tle | } }
        form { A }
        """;

    [Fact]
    public void TheUnnamedPartChordsAndLyrics_AreRenderedByTheBareKeywords()
    {
        string source = OnePart + "score {\n  chords\n  staff\n  lyrics\n}\n";
        Assert.Empty(Errors(source));
        var spec = RenderSpecParser.FindFirst(SyntaxTree.Parse(source))!;
        Assert.Contains(spec.Items, i => i is ChordRowSpec { PartName: SyntaxFacts.UnnamedChordsName });
        var staff = Assert.Single(spec.Items.OfType<SingleStaffSpec>()).Staff;
        Assert.Equal(SyntaxFacts.UnnamedPartName, staff.VoiceName);
        // The unnamed track sings the unnamed part, so it folds under its staff.
        Assert.Equal(new[] { SyntaxFacts.UnnamedLyricsName }, staff.WithLyrics);

        string svg = Svg(source);
        Assert.Contains(">lit<", svg);
        Assert.Contains(">G<", svg);
    }

    [Fact]
    public void ABareTab_RendersTheUnnamedPart()
    {
        string source = OnePart + "score { staff  tab }\n";
        Assert.Empty(Errors(source));
        var spec = RenderSpecParser.FindFirst(SyntaxTree.Parse(source))!;
        Assert.Equal(SyntaxFacts.UnnamedPartName, Assert.Single(spec.Items.OfType<TabStaffSpec>()).Staff.VoiceName);
    }

    /// <summary>No part at all: the music written straight into the section is the one
    /// part's, and a bare staff renders it — as does the unnamed lyrics track sing it.</summary>
    [Fact]
    public void WithNoPartAtAll_ABareStaffRendersTheSectionsMusic()
    {
        string source = "section A { c'4 d e f | g2 g | }\nlyrics { section A { a b c d | e f | } }\n"
            + "score { staff  lyrics }\n";
        Assert.Empty(Errors(source));
        string svg = Svg(source);
        Assert.Contains(">e<", svg);
    }

    [Fact]
    public void TheUnnamedLyrics_SingTheOnlyNamedPart()
    {
        string source = "part melody { clef treble  section A { c'4 d e f | } }\n"
            + "lyrics { section A { la la la la | } }\nscore { staff melody  lyrics }\n";
        Assert.Empty(Errors(source));
        var staff = Assert.Single(RenderSpecParser.FindFirst(SyntaxTree.Parse(source))!.Items.OfType<SingleStaffSpec>()).Staff;
        Assert.Equal(new[] { SyntaxFacts.UnnamedLyricsName }, staff.WithLyrics);
    }

    [Fact]
    public void AnUnnamedPart_MustBeTheOnlyPart()
        => Assert.Contains(Errors("part { clef treble }\npart bass { clef bass }\nsection A { c'1 | }\nscore { staff  staff bass }\n"),
            d => d.Code == DiagnosticCodes.UndefinedPart && d.Message.Contains("only part"));

    [Fact]
    public void ABareStaff_InAFileOfNamedParts_SaysWhichNamesExist()
        => Assert.Contains(Errors("part melody { clef treble  section A { c'1 | } }\nscore { staff }\n"),
            d => d.Code == DiagnosticCodes.UndefinedPart && d.Message.Contains("(melody)"));

    [Fact]
    public void ABareChordsRow_WithNoUnnamedTrack_IsUndefined()
        => Assert.Contains(Errors("part melody { clef treble  section A { c'1 | } }\nchords prog { section A { C } }\n"
            + "score { chords  staff melody }\n"),
            d => d.Code == DiagnosticCodes.UndefinedPart && d.Message.Contains("'chords'"));

    [Fact]
    public void TheUnnamedBlocks_RoundTrip()
    {
        string source = OnePart + "score {\n  chords as roman\n  staff \"Voice\"\n  lyrics\n}\n";
        var root = SyntaxTree.Parse(source).GetRoot();
        Assert.Equal(source, root.ToFullString());
        foreach (var n in root.DescendantNodes())
            Assert.Equal(n.ToFullString(), source.Substring(n.Position, n.FullWidth));
    }
}
