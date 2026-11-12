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

using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A harmony written OUT as a part (owner's decisions 2026-11-12, docs/lyrics-singer-design.md
/// §14): for an instrument — a second violin a third under the first — and for a singer, whose
/// lines then sing the written part; the notes are the ones a score prints for that singer.
/// </summary>
[Trait("Category", "Unit")]
public class HarmonyPartWriterTests
{
    private const string BySection = """
        octave relative
        time 4/4
        key g major
        part vn1 { instrument violin }
        part vc { instrument cello }
        section A {
          vn1 { d'4 e fis g | }
          vc { g,1 | }
        }
        section B {
          vn1 { a'4 g fis e | }
          vc { d,1 | }
        }
        form { A B }
        score { staff vn1  staff vc }
        """;

    private const string ByPart = """
        octave relative
        time 4/4
        key g major
        part vn1 { instrument violin
          section A { d'4 e fis g | }
          section B { a'4 g fis e | }
        }
        form { A B }
        score { staff vn1 }
        """;

    private static int[] PitchesOf(string source, string part)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        var spec = RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        return [.. midi.Tracks.SelectMany(t => t.Notes).Where(n => n.Part == part).OrderBy(n => n.StartTick).Select(n => n.Pitch)];
    }

    [Fact]
    public void AnInstrumentsHarmony_IsWrittenBesideThePartItFollows()
    {
        var result = HarmonyPartWriter.WritePart(BySection, "vn1", "vn2", -3, 0);
        Assert.Null(result.Error);
        string text = result.Text!;
        // The header after the first violin's, a block after its block in each section, a staff after its staff.
        Assert.Contains("part vn1 { instrument violin }\npart vn2 {\n  instrument violin\n}", text.Replace("\r\n", "\n"));
        Assert.Contains("vn2 { b4 c d e | }", text);
        Assert.Contains("vn2 { fis'4 e d c | }", text);
        Assert.Contains("score { staff vn1  staff vn2  staff vc }", text);
        // A third below, along G major: d e f# g → b c d e; a g f# e → f# e d c.
        Assert.Equal([59, 60, 62, 64, 66, 64, 62, 60], PitchesOf(text, "vn2"));
        Assert.Equal(PitchesOf(BySection, "vn1"), PitchesOf(text, "vn1"));
    }

    [Fact]
    public void InAFileWrittenByPart_ThePartHoldsItsOwnSections_AndRestsWhereItIsNotAsked()
    {
        var result = HarmonyPartWriter.WritePart(ByPart, "vn1", "vn2", 6, -1, ["B"]);
        Assert.Null(result.Error);
        string text = result.Text!.Replace("\r\n", "\n");
        Assert.Contains("part vn2 {\n  instrument violin\n  section A { R*1 }\n  section B {", text);
        // B alone: a g f# e a sixth above (f# e d c) and an octave down.
        Assert.Equal([66, 64, 62, 60], PitchesOf(text, "vn2"));
    }

    [Theory]
    [InlineData("nobody", "vn2", 3, 0, "has no part 'nobody'")]
    [InlineData("vn1", "vc", 3, 0, "is in the score already")]
    [InlineData("vn1", "vn2", 0, 0, "Say how far")]
    [InlineData("vn1", "vn2", 1, 0, "Say how far")]
    [InlineData("vn1", "2nd", 3, 0, "needs a name")]
    public void WhatCannotBeWritten_IsSaid_AndNothingIsChanged(string from, string name, int interval, int octaves, string says)
    {
        var result = HarmonyPartWriter.WritePart(BySection, from, name, interval, octaves);
        Assert.Null(result.Text);
        Assert.Contains(says, result.Error);
    }

    private const string Song = """
        octave relative
        time 4/4
        key c major
        language english
        part lead { instrument alto  lyrics words }
        section Verse {
          lead { e'4 e d c | }
          lyrics words { one two three four | }
        }
        section Chorus {
          lead { g'4 g a g | }
          lyrics words { la la la la | }
        }
        form { Verse Chorus }
        singer alice female sings on lead in Verse
        singer carol female "x/y" sings on lead up 3 in Chorus with breathiness 2
        score { staff lead  lyrics words  staff carol  lyrics carol }
        score plain { staff lead  lyrics words }
        """;

    [Fact]
    public void ASingersNotes_AreWrittenOut_AndTheSingerSingsTheWrittenPart()
    {
        Assert.True(HarmonyPartWriter.CanWriteSinger(SyntaxTree.Parse(Song).GetRoot(), "carol"));
        var result = HarmonyPartWriter.WriteSinger(Song, "carol");
        Assert.Null(result.Error);
        string text = result.Text!.Replace("\r\n", "\n");

        Assert.Contains("part carol \"Carol\" {\n  instrument alto\n  lyrics carolWords\n}", text);
        Assert.Contains("  carol { b'4 b c b | }\n  lyrics carolWords { la la la la | }", text);
        Assert.Contains("  carol { R*1 }", text);
        // One line, on the written part, its voice and settings kept; its place is in the notes now.
        Assert.Contains("singer carol female \"x/y\" sings on carol with breathiness 2\n", text);
        Assert.DoesNotContain("up 3", text);
        // The score that printed the singer keeps her staff where it stood — now the written part —
        // and its row of her words names the track they are now; the other takes the staff and its words.
        Assert.Contains("score { staff lead  lyrics words  staff carol  lyrics carolWords }", text);
        Assert.Contains("score plain { staff lead  lyrics words  staff carol  lyrics carolWords }", text);

        // Nothing is left to make, and nothing more to write out.
        var tree = SyntaxTree.Parse(text);
        Assert.False(tree.HasErrors);
        Assert.False(HarmonyPartWriter.CanWriteSinger(tree.GetRoot(), "carol"));
        Assert.Same(text, SingerParts.Expand(text, tree, out var said));
        Assert.Empty(said);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        Assert.Equal([71, 71, 72, 71], PitchesOf(text, "carol"));
    }
}