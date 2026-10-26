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
using LilySharp.Core.Editing;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What a composed piece leaves flat, for the AI that wrote it to revise (owner's report
/// 2026-10-26: a "dramatic string quartet" of four piano staves, whole notes and scales).
/// </summary>
[Trait("Category", "Unit")]
public class CompositionChecksTests
{
    // The owner's quartet as it came back, cut to its shape: no instruments, one grandStaff,
    // whole notes, every part in the same rhythm.
    private const string Flat = """
        time 4/4
        octave absolute
        part violin1 { clef treble }
        part violin2 { clef treble }
        part viola { clef alto }
        part cello { clef bass }
        section A {
          violin1 { c''1 | g'1 | a'1 | e'1 | f'1 | g'1 | d'1 | e'1 | c''1 | g'1 | a'1 | e'1 | f'1 | g'1 | d'1 | c'1 | }
          violin2 { e'1 | d'1 | f'1 | c'1 | d'1 | e'1 | b1 | c'1 | e'1 | d'1 | f'1 | c'1 | d'1 | e'1 | b1 | e'1 | }
          viola { g1 | g1 | a1 | g1 | a1 | b1 | g1 | g1 | g1 | g1 | a1 | g1 | a1 | b1 | g1 | g1 | }
          cello { c1 | g,1 | f,1 | c1 | d1 | c1 | g,1 | c1 | a,1 | g,1 | f,1 | c1 | d1 | c1 | g,1 | c1 | }
        }
        form { A }
        score { grandStaff { staff violin1  staff violin2  staff viola  staff cello } }
        """;

    [Fact]
    public void TheFlatQuartet_IsToldEachWayItIsFlat()
    {
        var findings = CompositionChecks.Of(Flat);
        Assert.Contains(findings, f => f.Contains("one grandStaff") && f.Contains("staffGroup"));
        Assert.Contains(findings, f => f.StartsWith("No instrument for violin1, violin2, viola, cello"));
        Assert.Contains(findings, f => f.StartsWith("violin1: 100% of its notes are whole notes"));
        Assert.Contains(findings, f => f.Contains("every part moves in the same rhythm"));
    }

    [Fact]
    public void TheGrammarsQuartetExample_IsNotFlat()
    {
        // The example the grammar gives for "compose a piece" (GRAMMAR_FOR_LLM.md, Common requests).
        var grammar = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "GRAMMAR_FOR_LLM.md"));
        int at = grammar.IndexOf("title \"Little Quartet\"", System.StringComparison.Ordinal);
        Assert.True(at > 0);
        string example = grammar[at..grammar.IndexOf("```", at, System.StringComparison.Ordinal)];
        Assert.Empty(CompositionChecks.Of(example));
    }

    [Fact]
    public void APianosTwoHands_NeedNoInstrument()
    {
        const string piano = """
            title "Minuet"
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            section A { rh { c'4 d' e' f' | } lh { c2 g | } }
            form { A }
            score { grandStaff { staff rh  staff lh } }
            """;
        Assert.Empty(CompositionChecks.Of(piano));
    }

    [Fact]
    public void APieceWithNoTitleOrAPlaceholder_IsToldToTitleIt()
    {
        // Owner's request 2026-10-27: a composed piece always has a fitting title.
        Assert.Contains(CompositionChecks.Of(Flat), f => f.StartsWith("The piece has no title"));
        string placeholder = "title \"New Piece\"\n" + Flat;
        Assert.Contains(CompositionChecks.Of(placeholder), f => f.StartsWith("\"New Piece\" is a placeholder"));
        Assert.DoesNotContain(CompositionChecks.Of("title \"Autumn Elegy\"\n" + Flat), f => f.Contains("title"));
        Assert.Equal("黄昏のワルツ", CompositionChecks.TitleOf("// a comment\ntitle \"黄昏のワルツ\"\ntime 3/4\n"));
        Assert.Null(CompositionChecks.TitleOf("title \"\"\n"));
        Assert.True(CompositionChecks.IsPlaceholderTitle("Untitled"));
        Assert.False(CompositionChecks.IsPlaceholderTitle("Song of the Sea"));
    }
}
