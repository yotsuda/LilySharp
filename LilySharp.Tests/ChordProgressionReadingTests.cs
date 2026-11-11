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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The chord progression of a score as the arranging chat reads it
/// (<see cref="ChordHarmonizer.DescribeProgression"/>; owner's request 2026-11-11): section by
/// section as a chords block, what the score names kept as written.
/// </summary>
public class ChordProgressionReadingTests
{
    private const string Song = """
        octave absolute
        key c major
        part gtr "Guitar" { instrument guitar }
        part mel "Melody" { }
        section A {
          mel { c''4 e'' g'' e'' | a'4 c'' e'' c'' | f'4 a' c'' a' | g'4 b' d'' b' | }
          gtr { <c e g c' e'>1@chord(C) | <a, e a c' e'>1@diagram(x02210) | <f a c' f'>1@chord | c1@chord(G7 320001) | }
        }
        section B {
          mel { c''4 e'' g'' e'' | a'4 c'' e'' c'' | f'4 a' c'' a' | g'4 b' d'' b' | }
          gtr { <c e g c' e'>1@chord(Cmaj7) | <a, e a c' e'>1 | <f a c' f'>1 | <g, b, d g b g'>1 | }
        }
        section C {
          mel { c''4 e'' g'' e'' | a'4 c'' e'' c'' | }
          chords prog { C | Am F(133211) | }
        }
        section D {
          mel { c''4 e'' g'' e'' | a'4 c'' e'' c'' | }
        }
        """;

    // The owner's rule: a chord the score names stands — an @chord's name, the chord an @diagram's
    // shape sounds, a bare @chord's notes, a name on a single note — and the notes do not overrule it.
    [Fact]
    public void WhatTheScoreNamesStands_ByNameByShapeAndByItsNotes()
    {
        string text = ChordHarmonizer.DescribeProgression(Song);
        Assert.Contains("  section A { C | Am | F | G7 | }   // as written\n", text);
        // G7 is named on a single C: the notes there would never read as G7.
        Assert.StartsWith("The chords of the score, in C major, a section a line", text);
        Assert.Contains("chords {\n", text);
        Assert.Contains("Form, as played: A B C D\n", text);
    }

    // Named in one bar of four, the name holds where it stands and the rest is read from the notes.
    [Fact]
    public void ASectionNamedInPlaces_KeepsTheNames_AndReadsTheRest()
    {
        string text = ChordHarmonizer.DescribeProgression(Song);
        Assert.Contains("  section B { Cmaj7 | Am | F | G | }   // as written where a chord is named, the rest read from the notes\n", text);
    }

    // A chords row is the section's harmony as its writer put it, a shape and all, shown by a score or not.
    [Fact]
    public void AChordsRowIsAnsweredAsItIsWritten_InASectionAndInAByPartTrack()
    {
        string text = ChordHarmonizer.DescribeProgression(Song);
        Assert.Contains("  section C { C | Am F(133211) | }   // as written (the chords row `prog`)\n", text);
        // The list for chordVoicings takes the symbol, not its shape.
        Assert.Contains("Its chords, in the order they first come: C Am F G7 Cmaj7 G Fmaj7 —", text);

        string byPart = ChordHarmonizer.DescribeProgression("""
            octave absolute
            part gtr "Guitar" {
              instrument guitar
              section A { <c e g c' e'>1 | <g, b, d g b g'>1 | }
              section B { <a, e a c' e'>1 | <f a c' f'>1 | }
            }
            chords prog {
              section A { C | G7 | }
            }
            """);
        Assert.Contains("  section A { C | G7 | }   // as written (the chords row `prog`)\n", byPart);
        Assert.Contains("  section B { Am | F | }   // read from the notes\n", byPart);
    }

    // A line alone has no chord to hear: one is chosen, and the answer says so.
    [Fact]
    public void AReadingOfOneLineIsSaidToBeChosen()
    {
        string text = ChordHarmonizer.DescribeProgression(Song);
        Assert.Contains("  section D { C | Fmaj7 | }   // read from the notes\n", text);
        Assert.Contains("D, bars 1, 2: one note at a time sounds there, so the chord is chosen for the line, not heard.\n", text);
        Assert.EndsWith("a name the score writes always stands.", text);
    }

    // Owner's runs 2026-11-11: with the shapes in hand, the chat still spent a minute and a half
    // matching them to the chords the part writes. Read for a part, the answer lists each chord as
    // the part writes it — the text an edit replaces — and in how many places.
    [Fact]
    public void ReadForAPart_TheChordsAreListedAsItWritesThem_WithTheirPlaces()
    {
        const string band = """
            octave absolute
            key bes major
            part gtr "Guitar" { instrument guitar }
            part keys "Keys" { instrument piano-right }
            phrase gBb { r8 <bes, f bes d' f'>8 r8 q r8 q r8 q | }
            section A {
              gtr { gBb <g, d g bes d' g'>1 | gBb <f a c' f'>2 <f a c' f'>2 | }
              keys { <bes d' f'>1 | <bes d' g'>1 | <bes d' f'>1 | <a c' f'>1 | }
            }
            section B {
              gtr { gBb <bes, f bes d' f'>1 | }
              keys { <bes d' f'>1 | <bes d' f'>1 | }
            }
            """;
        string gtr = ChordHarmonizer.DescribeProgression(band, null, ["gtr"]);
        Assert.Contains("As gtr writes them (each the text of an `<…>` chord — give it as `old` to lilysharp_proposeScore's `edits`, with `part: \"gtr\"` and `all: true`):\n"
            + "  Bb = <bes, f bes d' f'> (2 places)\n"      // the phrase's, called three times, and section B's own
            + "  Gm = <g, d g bes d' g'> (1 place)\n"
            + "  F = <f a c' f'> (2 places)\n", gtr);
        Assert.DoesNotContain("As keys writes them", gtr);
        // Each part asked has its own list; read whole, the score has none (it is every part's).
        string both = ChordHarmonizer.DescribeProgression(band, null, ["gtr", "keys"]);
        Assert.Contains("As keys writes them", both);
        Assert.Contains("  Bb = <bes d' f'> (4 places)\n", both);
        Assert.DoesNotContain("writes them", ChordHarmonizer.DescribeProgression(band));
    }

    // Owner's run 2026-11-11: a guitar revoiced to leave the root to the bass was read alone as
    // G♭dim, Gdim7 and E♭/G. A part is a view of the reading: the chord is named from every part.
    [Fact]
    public void APartThatLeavesTheRootToTheBass_IsNamedByTheBass()
    {
        const string band = """
            octave absolute
            key bes major
            part gtr "Guitar" { instrument guitar }
            part bass "Bass" { instrument bass }
            section A {
              gtr { R | <fis c' fis' a'>1 | <g bes d' g'>1 | <g bes e' bes'>1 | <f bes d' bes'>1 | }
              bass { g,,1 | d,,1 | g,,1 | c,,1 | f,,1 | }
            }
            """;
        string gtr = ChordHarmonizer.DescribeProgression(band, null, ["gtr"]);
        Assert.StartsWith("The chords where gtr plays (named from every part's notes), in B♭ major", gtr);
        // Where the guitar rests there is no chord of its own; D7 and C7 are the bass's word; and the
        // bass's F under a B♭ chord is the bass's note, not the guitar's inversion.
        Assert.Contains("  section A { r | D7 | Gm | C7 | Bb | }   // read from the notes\n", gtr);
        Assert.Contains("  D7 = <fis c' fis' a'> (1 place)\n", gtr);
        Assert.Contains("  C7 = <g bes e' bes'> (1 place)\n", gtr);
        Assert.DoesNotContain("dim", gtr);
        // Read whole, the bar the bass plays alone has its chord, and the bass its inversion.
        string whole = ChordHarmonizer.DescribeProgression(band);
        Assert.Contains("| D7 | Gm | C7 | Bb/F | }", whole);
        Assert.DoesNotContain("section A { r |", whole);
    }

    // The owner's question, the same day: without the bass, is a three-note chord to be read as a
    // seventh chord without its root? Not on the notes' word alone — every triad is one. The name
    // stands as heard; a diminished triad alone is offered its other reading.
    [Fact]
    public void WithNoRootSounding_TheNameIsAsHeard_AndADiminishedTriadIsOfferedItsDominant()
    {
        string alone = ChordHarmonizer.DescribeProgression("""
            octave absolute
            key bes major
            part gtr "Guitar" { instrument guitar }
            section A { gtr { <fis c' fis' a'>1 | <g bes d' g'>1 | <f a c' f'>1 | } }
            """);
        Assert.Contains("  section A { Gbdim | Gm | F | }   // read from the notes\n", alone);
        Assert.Contains("A, bar 1: Gbdim may be D7 with its root left out — no part sounds a D there to say so.\n", alone);
        // A major or a minor triad is not doubted, nor renamed a seventh chord.
        Assert.DoesNotContain(" may be", alone.Replace("Gbdim may be", ""));
        Assert.DoesNotContain("Gm7", alone);
    }

    // The parts named are the parts shown: the bars they play in, the names written anywhere, no row.
    [Fact]
    public void WithParts_OnlyTheirNotesAndNamesAreRead()
    {
        string gtr = ChordHarmonizer.DescribeProgression(Song, null, ["gtr"]);
        Assert.StartsWith("The chords where gtr plays (named from every part's notes), in C major", gtr);
        Assert.Contains("  section A { C | Am | F | G7 | }   // as written\n", gtr);
        Assert.DoesNotContain("section C {", gtr);
        Assert.Contains("Nothing of these parts sounds in C, D.\n", gtr);
        Assert.Contains("The score also writes a chords row, left out here as it is no part's", gtr);
        Assert.Contains("Ranges, as the parts sound: gtr: G2–G4", gtr);
        Assert.DoesNotContain("mel:", gtr);

        string mel = ChordHarmonizer.DescribeProgression(Song, null, ["mel"]);
        // A name the score writes is the writer's word on the harmony, whichever part it stands on:
        // where the melody plays, the chords are the ones named on the guitar.
        Assert.Contains("  section A { C | Am | F | G7 | }   // as written\n", mel);
        Assert.Contains("  section D { C | Fmaj7 | }   // read from the notes\n", mel);
        Assert.Contains("Ranges, as the parts sound: mel:", mel);
    }

    [Fact]
    public void WhatCannotBeReadIsSaid()
    {
        Assert.Equal("No part named nope: the parts are gtr, mel.", ChordHarmonizer.DescribeProgression(Song, null, ["nope"]));
        Assert.StartsWith("The score has errors", ChordHarmonizer.DescribeProgression("part x {"));
        Assert.Equal("Nothing in the score sounds a note to read chords from.", ChordHarmonizer.DescribeProgression("part x \"X\" { }"));
    }

    // Chords struck off the beat (a band's guitar: `r8 <…>8`) are read in the bar they sound in.
    [Fact]
    public void ChordsStruckOffTheBeatAreReadInTheirBar()
    {
        string text = ChordHarmonizer.DescribeProgression("""
            octave absolute
            key bes major
            part gtr "Guitar" { instrument guitar }
            part bass "Bass" { instrument bass }
            section A {
              gtr { r8 <bes, f bes d' f'>8 r8 q r8 q r8 q | r8 <g, d g bes d' g'>8 r8 q r8 q r8 q | }
              bass { bes,,4 bes,, bes,, bes,, | g,,4 g,, g,, g,, | }
            }
            """);
        Assert.Contains("  section A { Bb | Gm | }   // read from the notes\n", text);
        Assert.Contains("in B♭ major", text);
    }
}
