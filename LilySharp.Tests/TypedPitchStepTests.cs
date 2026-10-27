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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The step keys on a TYPED <c>pitch</c> word — <c>written</c> ⇄ <c>concert</c>, the file's line or
/// a part header's — rewrite the music it governs so the piece sounds and prints as before (owner's
/// request 2026-10-28). Down is <c>concert</c> (the completion's order: written, concert).
/// </summary>
public class TypedPitchStepTests
{
    private const string Band = """
        pitch written
        octave absolute
        time 4/4
        part pno { clef treble }
        part bs { instrument bass }
        section A { pno { c'4 e' g' c'' | } bs { c4 e g c' | } }
        form { A }
        score { staff pno staff bs }
        """;

    private static (string Text, string Message) StepAt(string text, string word, int dir, int nth = 0)
    {
        int at = -1;
        for (int i = 0; i <= nth; i++)
            at = text.IndexOf(word, at + 1, System.StringComparison.Ordinal);
        Assert.True(at >= 0, word);
        int caret = at + word.Length - 1;
        var step = NoteStepper.Step(text, SyntaxTree.Parse(text), [(caret, caret)], dir, semitone: true);
        Assert.False(step.Fallback);
        return (NoteStepper.Apply(text, step.Edits), step.Message!);
    }

    [Fact]
    public void ToConcert_TypesTheBassAsItSounds_AndThePianoStays()
    {
        var (text, said) = StepAt(Band, "pitch written", -1);
        Assert.StartsWith("pitch concert\n", text.ReplaceLineEndings("\n"));
        Assert.Contains("bs { c,4 e, g, c | }", text);       // an octave lower: as it sounds
        Assert.Contains("pno { c'4 e' g' c'' | }", text);
        Assert.Equal(OctaveModeConverter.SoundSignature(Band), OctaveModeConverter.SoundSignature(text));
        Assert.StartsWith("pitch concert: ", said);
    }

    [Fact]
    public void BackToWritten_GivesTheSameFileBack()
    {
        var (concert, _) = StepAt(Band, "pitch written", -1);
        var (written, _) = StepAt(concert, "pitch concert", +1);
        Assert.Equal(Band, written);
    }

    [Fact]
    public void AChromaticTransposerUnderTheFilesLine_KeepsItsLettersAndSaysItsOwnPitch()
    {
        string band = Band.Replace("part bs { instrument bass }", "part bs { instrument bass }\npart tp { instrument trumpet }")
            .Replace("form { A }", "section B { tp { d'1 | } }\nform { A B }");
        var (text, _) = StepAt(band, "pitch written", -1);
        Assert.Contains("part tp { instrument trumpet pitch written }", text);
        Assert.Contains("tp { d'1 | }", text);
        Assert.Equal(OctaveModeConverter.SoundSignature(band), OctaveModeConverter.SoundSignature(text));
    }

    [Fact]
    public void AChromaticTransposersOwnPitch_IsRefused_AndNothingChanges()
    {
        string band = Band.Replace("part bs { instrument bass }", "part bs { instrument bass }\npart tp { instrument trumpet pitch written }")
            .Replace("form { A }", "section B { tp { d'1 | } }\nform { A B }");
        var (text, said) = StepAt(band, "pitch written", -1, nth: 1);
        Assert.Equal(band, text);
        Assert.Contains("respelled", said);
    }

    [Fact]
    public void APartsOwnPitch_MovesThatPartAlone()
    {
        string band = Band.Replace("pitch written\n", "").Replace("part bs { instrument bass }", "part bs { instrument bass pitch written }");
        var (text, _) = StepAt(band, "pitch written", -1);
        Assert.Contains("part bs { instrument bass pitch concert }", text);
        Assert.Contains("bs { c,4 e, g, c | }", text);
        Assert.Contains("pno { c'4 e' g' c'' | }", text);
        Assert.Equal(OctaveModeConverter.SoundSignature(band), OctaveModeConverter.SoundSignature(text));
    }

    [Fact]
    public void AScoresPitch_StillSwitchesTheWordAlone()
    {
        string book = Band.Replace("score { staff pno staff bs }", "score full pitch transposed { staff pno staff bs }");
        var (text, _) = StepAt(book, "pitch transposed", -1);
        Assert.Equal(book.Replace("pitch transposed", "pitch concert"), text);
    }
}
