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
using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The Extract-phrase refactoring: section music (whole body, or the whole
/// measures a selection touches) lifts into a top-level phrase, replaced by the
/// reference. It must be provably SEMANTICS-PRESERVING (the extractor verifies
/// the MIDI matches and refuses otherwise), compensating the phrase's fresh
/// frame with an explicit duration and corrected octave marks where needed.
/// </summary>
[Trait("Category", "Unit")]
public class PhraseExtractorTests
{
    private static (int Pitch, int Tick, int Dur)[] Midi(string src) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks
            .SelectMany(t => t.Notes)
            .Select(n => (n.Pitch, n.StartTick, n.DurationTicks)).ToArray();

    private static string ExtractOk(string src, int selStart, int selEnd, string name)
    {
        var result = PhraseExtractor.Extract(src, selStart, selEnd, name);
        Assert.True(result.NewText != null, result.Error ?? "no error");
        Assert.Equal(Midi(src), Midi(result.NewText!)); // sounds identical
        Assert.False(SyntaxTree.Parse(result.NewText!).HasErrors);
        return result.NewText!;
    }

    [Fact]
    public void WholeSection_PartMajor_ExtractsAndSoundsTheSame()
    {
        var src = """
            part melody { clef treble
              section A { c'4 d e f | g a b c' | }
            }
            form main { A }
            score main { staff melody }
            """;
        int caret = src.IndexOf("d e f");
        var newText = ExtractOk(src, caret, caret, "theme");
        Assert.Contains("phrase theme {", newText);
        Assert.Contains("section A { theme }", newText);
        // The declaration lands BEFORE the enclosing part.
        Assert.True(newText.IndexOf("phrase theme") < newText.IndexOf("part melody"));
    }

    [Fact]
    public void MeasureSelection_SnapsToBarlines_AndFixesTheOctave()
    {
        // Measure 2 (`g a b c'`) sits high because the RELATIVE chain climbed
        // there; a fresh phrase frame would drop its g near C4. The extractor
        // corrects the body's first pitch by measurement — and the first pitch
        // AFTER the reference too: the reference hands off its ANCHOR (bare g),
        // not the inline chunk's last note, so measure 3's d needs its own marks
        // to stay put.
        var src = "part m { section A { c'4 d e f | g a b c' | d4 e f g } }\n"
                + "form main { A }\nscore main { staff m }";
        int selStart = src.IndexOf("g a b");
        var newText = ExtractOk(src, selStart, selStart + 1, "climb");
        Assert.Contains("phrase climb { g''4 a b c' |}", newText);
        // Measure 1 stays put; measure 3 keeps its sound via the tail knob.
        Assert.Contains("c'4 d e f |", newText);
        Assert.Contains("d''''4 e f g", newText);
    }

    [Fact]
    public void InheritedDuration_IsStampedOntoTheFirstNote()
    {
        // Measure 2's `e f` inherit the half from `c2`; the phrase's default is
        // a quarter, so the extractor stamps the running duration explicitly.
        var src = "part m { section A { c2 d | e f | g1 } }\n"
                + "form main { A }\nscore main { staff m }";
        int selStart = src.IndexOf("e f");
        var newText = ExtractOk(src, selStart, selStart + 1, "mid");
        Assert.Contains("e2 f", newText);
    }

    [Fact]
    public void SectionMajorLayout_ExtractsFromThePartBlock()
    {
        var src = """
            section A {
              melody { c4 d e f | g2 g }
            }
            form main { A }
            score main { staff melody }
            """;
        int caret = src.IndexOf("d e f");
        var newText = ExtractOk(src, caret, caret, "line");
        Assert.Contains("phrase line {", newText);
        Assert.Contains("melody { line }", newText);
    }

    [Fact]
    public void ExtractedPhrase_CanBeReferencedByAnotherPart()
    {
        // The point of the refactoring: after extraction, another part can sing
        // the same music — here an octave up. (It used to say "a third up", written
        // the glued-interval spelling; it was removed 2026-08-28, and the octave
        // marks are what a second part reaches for now.)
        var src = "part m { section A { c'4 d e f } }\nform main { A }\nscore main { staff m }";
        int caret = src.IndexOf("d e");
        var newText = PhraseExtractor.Extract(src, caret, caret, "theme").NewText!;
        var harmonized = newText.Replace(
            "score main { staff m }",
            "part h { clef treble section A { theme' } }\nscore main { staff m staff h }");
        Assert.False(SyntaxTree.Parse(harmonized).HasErrors);
    }

    [Theory]
    [InlineData("ees")]    // reads as a pitch, not a name
    [InlineData("score")]  // keyword
    [InlineData("bd")]     // drum vocabulary
    public void UnusableName_IsRefused(string name)
    {
        var src = "part m { section A { c4 d } }\nform main { A }\nscore main { staff m }";
        var result = PhraseExtractor.Extract(src, src.IndexOf("c4"), src.IndexOf("c4"), name);
        Assert.Null(result.NewText);
    }

    [Fact]
    public void TakenName_IsRefused()
    {
        var src = "part m { section A { c4 d } }\nform main { A }\nscore main { staff m }";
        var result = PhraseExtractor.Extract(src, src.IndexOf("c4"), src.IndexOf("c4"), "m");
        Assert.Null(result.NewText);
        Assert.Contains("already declared", result.Error);
    }

    [Fact]
    public void CaretOutsideMusic_IsRefused()
    {
        var src = "part m { section A { c4 d } }\nform main { A }\nscore main { staff m }";
        var result = PhraseExtractor.Extract(src, src.IndexOf("form"), src.IndexOf("form"), "x");
        Assert.Null(result.NewText);
    }

    [Fact]
    public void SyntaxErrors_RefuseWithoutChanges()
    {
        // `d 4` stopped being a syntax error on 2026-08-19 (bare-duration repeat);
        // an unclaimed dot (LYS0023) is still one.
        var src = "part m { section A { c4 d . } }\nform main { A }\nscore main { staff m }";
        var result = PhraseExtractor.Extract(src, src.IndexOf("c4"), src.IndexOf("c4"), "x");
        Assert.Null(result.NewText);
        Assert.Contains("syntax errors", result.Error);
    }
}
