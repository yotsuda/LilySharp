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

using LilySharp.Core.Harmony;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Add Chord Track on the harmony reading (owner's decisions 2026-10-26,
/// docs/harmony-analysis-design.md): the chords the chosen score's parts play, written as the
/// file is laid out and placed in that score above its top staff, a row of its own.
/// </summary>
[Trait("Category", "Unit")]
public class ChordTrackReadingTests
{
    private const string Piano = """
        time 4/4
        key c major
        octave absolute
        part rh { clef treble }
        part lh { clef bass }
        section A {
          rh { e'2 g' | a'1 | b'2 d'' | c''1 | }
          lh { <c e g>1 | <d f a>1 | <g, b, d f>1 | <c e g>1 | }
        }
        form { A }
        score {
          grandStaff { staff rh  staff lh }
        }
        score tune {
          staff rh
        }
        """;

    private static string Added(string source, string? score = null, bool melodyOnly = false)
    {
        var result = ChordHarmonizer.AddReadChordTracks(source, score, melodyOnly);
        Assert.NotNull(result);
        var tree = SyntaxTree.Parse(result.Value.Text);
        Assert.False(tree.HasErrors, result.Value.Text);
        return result.Value.Text;
    }

    [Fact]
    public void ThePartsChords_AreRead_AndPlacedAboveTheScoresTopStaff_ARowOfItsOwn()
    {
        string text = Added(Piano);
        Assert.Contains("chords harmony { C | Dm | G7 | C | }", text);
        // A row of its own over the staff, indented as it is (owner's request 2026-10-26).
        Assert.Contains("score {\n  chords harmony\n  grandStaff {", text.Replace("\r\n", "\n"));
        Assert.DoesNotContain("as roman", text);
        // The other score is not touched.
        Assert.Contains("score tune {\n  staff rh\n}", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void OnlyTheChosenScoresParts_AreRead()
    {
        // Score `tune` shows the right hand alone: e g | a | b d | c — chosen for the melody
        // from the key's chords (the A alone an F), not the left hand's Dm.
        string text = Added(Piano, "tune");
        Assert.Contains("chords harmony { C | F | G | C | }", text);
        Assert.Contains("score tune {\n  chords harmony\n  staff rh", text.Replace("\r\n", "\n"));
        Assert.Contains("grandStaff { staff rh  staff lh }", text);
        Assert.DoesNotContain("chords harmony\n  grandStaff", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void TheMelodyOnlyChoice_LeavesTheAccompanimentUnread()
    {
        const string source = """
            time 4/4
            octave absolute
            part rh { clef treble }
            part lh { clef bass }
            section A {
              rh { g'4 b' d'' b' | }
              lh { <c e g>1 | }
            }
            form { A }
            score { staff rh  staff lh }
            """;
        Assert.DoesNotContain("chords harmony { G | }", Added(source));
        Assert.Contains("chords harmony { G | }", Added(source, melodyOnly: true));
    }

    [Fact]
    public void ALineThatSpellsAChordOutsideTheKey_GetsThatChord()
    {
        // Owner's report 2026-10-26 (Rose Garden, bars 79-80 and 83): a bass walking B♭ D F,
        // A C♯ E and F A♭ C under C major was given Dm, Am and F, the key's nearest chords.
        const string source = """
            time 4/4
            key c major
            octave absolute
            part p1 { clef bass }
            section A { p1 { d4 f a f | g, b, d b, | bes, d f d | a, des e des | f, aes, c aes, | } }
            form { A }
            score { staff p1 }
            """;
        Assert.Contains("chords harmony { Dm | G | Bb | A | Fm | }", Added(source));
    }

    [Fact]
    public void ABassLineAlone_TakesItsDownbeatAsTheRoot()
    {
        // The same report's bar 157: c e g a c is C under a bass, not the Am the A tipped it to.
        const string source = """
            time 4/4
            key c major
            octave absolute
            part p1 { clef bass }
            section A { p1 { c,4 e, g,16 a, g,8 a, c | c,4 e, a,8 g, f, e, | } }
            form { A }
            score { staff p1 }
            """;
        Assert.Contains("chords harmony { C | C | }", Added(source));
    }

    [Fact]
    public void ALineAlone_GetsTheKeysSeventhChords_WhereTheSeventhSounds()
    {
        // Owner's report 2026-10-26: the V was always a V7 and no other seventh was written. Now
        // a seventh is written where it sounds — the ii with its C, the V with its F — and not
        // where it does not; the iii where E G B are played.
        const string source = """
            time 4/4
            key c major
            octave absolute
            part p1 { clef bass }
            section A { p1 { d,4 f, a, c | g,, b,, d, f, | g,, b,, d, b,, | e,4 g, b, g, | c,4 e, g, b, | } }
            form { A }
            score { staff p1 }
            """;
        Assert.Contains("chords harmony { Dm7 | G7 | G | Em | Cmaj7 | }", Added(source));
    }

    [Fact]
    public void ABassLinesPassingAndApproachNotes_AreNotSevenths()
    {
        // Rose Garden bars 44-45: c e g a is C (the A passing over a C bass, not Am7/C), and the
        // B of c g a b stepping into the next bar's C is its approach, not a Cmaj7's seventh —
        // also where the section ends and the C is not there to see.
        const string source = """
            time 4/4
            key c major
            octave absolute
            part p1 { clef bass }
            section A { p1 { c,4 e, g, a, | c,4 g, a, b, | c1 | c,4 g, a, b, | } }
            form { A }
            score { staff p1 }
            """;
        Assert.Contains("chords harmony { C | C | C | C | }", Added(source));
    }

    [Fact]
    public void AChangeAtTheMiddleOfABar_IsTwoSlots_AOneBeatChordIsHeldThrough()
    {
        const string source = """
            time 4/4
            octave absolute
            part lh { clef bass }
            // A G on the last beat alone is not worth a change there (the change costs most off the
            // bar line and its middle: chords not read finer than they help).
            section A { lh { <a, c e>2 <d f a>2 | <c e g>2. <g, b, d>4 | } }
            form { A }
            score { staff lh }
            """;
        Assert.Contains("chords harmony { Am Dm | C | }", Added(source));
    }

    [Fact]
    public void ThreeFour_ChangesOnTheSecondBeat_OneSlotABeat()
    {
        const string source = """
            time 3/4
            octave absolute
            part lh { clef bass }
            section A { lh { <c e g>4 <g, b, d>2 | <c e g>2. | } }
            form { A }
            score { staff lh }
            """;
        Assert.Contains("chords harmony { C G . | C | }", Added(source));
    }

    [Fact]
    public void AByPartFile_GetsOneTopLevelTrack_WithItsSections()
    {
        const string source = """
            time 4/4
            octave absolute
            part lh {
              clef bass
              section A { <c e g>1 | }
              section B { <f a c'>1 | }
            }
            form { A B }
            score { staff lh }
            """;
        string text = Added(source).Replace("\r\n", "\n");
        Assert.Contains("chords harmony {\n  section A { C | }\n  section B { F | }\n}", text);
    }

    [Fact]
    public void ASecondReading_IsATrackOfItsOwn()
    {
        string once = Added(Piano);
        string twice = Added(once, "tune");
        Assert.Contains("chords harmony { C | Dm | G7 | C | }", twice);
        Assert.Contains("chords harmony2 { C | F | G | C | }", twice);
        Assert.Contains("chords harmony2\n  staff rh", twice.Replace("\r\n", "\n"));
    }

    [Fact]
    public void TheTrackIsWrittenInTheDocumentsLineEnds()
    {
        string lf = Piano.Replace("\r\n", "\n"), crlf = lf.Replace("\n", "\r\n");
        Assert.DoesNotContain("\r", Added(lf));
        Assert.DoesNotMatch("(?<!\r)\n", Added(crlf));
        // A by-part file's track spans lines of its own.
        const string byPart = "time 4/4\noctave absolute\npart lh {\n  clef bass\n  section A { <c e g>1 | }\n}\nform { A }\nscore { staff lh }\n";
        Assert.DoesNotMatch("(?<!\r)\n", Added(byPart.Replace("\n", "\r\n")));
    }

    [Fact]
    public void InAOneLineScore_TheRowStandsBesideTheStaff()
    {
        const string source = "time 4/4\noctave absolute\npart lh { clef bass }\nsection A { lh { <c e g>1 | } }\nform { A }\nscore { staff lh }\n";
        Assert.Contains("score { chords harmony  staff lh }", Added(source));
    }

    [Fact]
    public void WhereNothingSounds_NothingIsAdded()
    {
        const string source = """
            time 4/4
            section A { chords prog { C | F | } }
            form { A }
            score { chords prog }
            """;
        Assert.Null(ChordHarmonizer.AddReadChordTracks(source, null, melodyOnly: false));
    }
}
