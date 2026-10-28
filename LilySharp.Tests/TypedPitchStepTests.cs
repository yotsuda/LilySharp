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

    // A B♭ trumpet is respelled a major second: written d' (in its D major) is concert c'. The way
    // back gives it its player's key again, and the file comes back as it was.
    private static readonly string Trumpet = Band
        .Replace("part bs { instrument bass }", "part bs { instrument bass }\npart tp { instrument trumpet key d major }")
        .Replace("form { A }", "section B { tp { d'2 fis' | e'1 | } }\nform { A B }");

    [Fact]
    public void AChromaticTransposerUnderTheFilesLine_IsRespelledAsItSounds_AndBack()
    {
        var (concert, said) = StepAt(Trumpet, "pitch written", -1);
        Assert.Contains("part tp { instrument trumpet }", concert);     // D major moved to the file's C
        Assert.Contains("tp { c'2 e' | d'1 | }", concert);
        Assert.Equal(OctaveModeConverter.SoundSignature(Trumpet), OctaveModeConverter.SoundSignature(concert));
        Assert.StartsWith("pitch concert: ", said);
        var (written, _) = StepAt(concert, "pitch concert", +1);
        Assert.Equal(Trumpet, written);
    }

    // A chord track `for` the trumpet is in its letters (owner's decision 2026-10-28): it is
    // respelled with the trumpet's notes, root and slash bass, and comes back. A track for no part
    // is sounding and stays.
    [Fact]
    public void AChordTrackForTheTransposer_IsRespelledWithIt_AndBack()
    {
        string book = Trumpet
            .Replace("section B { tp {", "chords tpc for tp { section B { E/G# | A7 | } }\nchords band { section B { C | Dm7 | } }\nsection B { tp {")
            .Replace("score { staff pno staff bs }", "score { staff pno staff bs chords tpc chords band staff tp }");
        Assert.Empty(SyntaxTree.Parse(book).Diagnostics);
        var (concert, said) = StepAt(book, "pitch written", -1);
        Assert.StartsWith("pitch concert: ", said);
        Assert.Contains("chords tpc for tp { section B { D/F# | G7 | } }", concert);
        Assert.Contains("chords band { section B { C | Dm7 | } }", concert);
        Assert.Equal(OctaveModeConverter.SoundSignature(book), OctaveModeConverter.SoundSignature(concert));
        var (written, _) = StepAt(concert, "pitch concert", +1);
        Assert.Equal(book, written);
    }

    [Fact]
    public void AnAltoSaxophone_IsRespelledAMajorSixthUp_InA()
    {
        const string book = "pitch concert\noctave absolute\ntime 4/4\npart sx { instrument alto-sax }\n"
            + "section A { sx { c'4 e' g' bes' | } }\nform { A }\nscore { staff sx }\n";
        var (text, _) = StepAt(book, "pitch concert", +1);
        Assert.Contains("part sx { instrument alto-sax key a major }", text);
        Assert.Contains("sx { a'4 cis'' e'' g'' | }", text);
        Assert.Equal(OctaveModeConverter.SoundSignature(book), OctaveModeConverter.SoundSignature(text));
    }

    [Fact]
    public void AChromaticTransposersOwnPitch_RespellsThatPartAlone()
    {
        string band = Trumpet.Replace("pitch written\n", "").Replace("key d major }", "key d major pitch written }");
        var (text, _) = StepAt(band, "pitch written", -1);
        Assert.Contains("part tp { instrument trumpet pitch concert }", text);   // C major is the file's own
        Assert.Contains("tp { c'2 e' | d'1 | }", text);
        Assert.Contains("bs { c4 e g c' | }", text);                    // not governed by the trumpet's own
        Assert.Equal(OctaveModeConverter.SoundSignature(band), OctaveModeConverter.SoundSignature(text));
    }

    [Fact]
    public void AChromaticTransposersOwnPitch_UnderASharedSectionKey_IsRefused()
    {
        string band = Trumpet.Replace("pitch written\n", "").Replace("part tp { instrument trumpet key d major }", "part tp { instrument trumpet pitch concert }")
            .Replace("section B { tp", "section B { key f major tp");
        var (text, said) = StepAt(band, "pitch concert", +1);
        Assert.Equal(band, text);
        Assert.Contains("shares the key", said);
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
