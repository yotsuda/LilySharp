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
          violin1 { c'''1 | g''1 | a''1 | e''1 | f''1 | g''1 | d''1 | e''1 | c'''1 | g''1 | a''1 | e''1 | f''1 | g''1 | d''1 | c''1 | }
          violin2 { e''1 | d''1 | f''1 | c''1 | d''1 | e''1 | b'1 | c''1 | e''1 | d''1 | f''1 | c''1 | d''1 | e''1 | b'1 | e''1 | }
          viola { g'1 | g'1 | a'1 | g'1 | a'1 | b'1 | g'1 | g'1 | g'1 | g'1 | a'1 | g'1 | a'1 | b'1 | g'1 | g'1 | }
          cello { c'1 | g1 | f1 | c'1 | d'1 | c'1 | g1 | c'1 | a1 | g1 | f1 | c'1 | d'1 | c'1 | g1 | c'1 | }
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
        // …and leaves the voices moving together to a hymn or a chorale (owner's trial 2026-10-29).
        Assert.Contains(findings, f => f.Contains("every part moves in the same rhythm")
            && f.EndsWith("unless the voices moving together is the music's own character, as in a hymn or a chorale: then keep it."));
    }

    // The findings of a piece's form — its length, its keys, its last chord — which a short sketch
    // has by being one; the rest are what its notation leaves flat.
    private static string[] Flatness(string source) => [.. CompositionChecks.Of(source)
        .Where(f => !f.StartsWith("The piece is ") && !f.StartsWith("The piece begins ") && !f.StartsWith("The piece ends "))];

    [Fact]
    public void TheQuartetSketch_IsNotFlat()
    {
        // The grammar's example for "compose a piece" until 2026-10-27 (ComposedPieces).
        Assert.Empty(Flatness(ComposedPieces.LittleQuartet));
        // …but a sketch: 8 bars, from D minor to F major.
        var findings = CompositionChecks.Of(ComposedPieces.LittleQuartet);
        Assert.Contains(findings, f => f.StartsWith("The piece is 8 bars long"));
        Assert.Contains(findings, f => f.StartsWith("The piece begins in D minor and ends in F major"));
    }

    // Owner's report 2026-10-27: the composed piano piece was 8 bars, and its end "unnatural" — E
    // minor at its start, G major at its end.
    [Fact]
    public void APieceThatEndsAwayFromItsTonic_IsToldToCloseOnIt()
    {
        string Piece(string keys, string rh, string lh)
            => $"title \"Close\"\ntime 4/4\noctave absolute\n{keys}\npart rh {{ instrument piano-right }}\npart lh {{ instrument piano-left }}\n"
             + $"section A {{\n  rh {{ {rh} }}\n  lh {{ {lh} }}\n}}\nform {{ A }}\nscore {{ grandStaff {{ staff rh  staff lh }} }}\n";
        string Bars(string bar, int n) => string.Concat(Enumerable.Repeat(bar + " | ", n));
        // 16 bars of C major ending on the tonic: nothing to say of its form.
        string home = Piece("key c major", Bars("e'4 g' e' c'", 15) + "c'1 |", Bars("c2 g,", 15) + "c1 |");
        Assert.Empty(CompositionChecks.Of(home).Where(f => f.StartsWith("The piece") && !f.Contains("seconds played")));
        // The same, stopping over its dominant.
        string open = Piece("key c major", Bars("e'4 g' e' c'", 15) + "d'1 |", Bars("c2 g,", 15) + "g,1 |");
        Assert.Contains(CompositionChecks.Of(open), f => f.StartsWith("The piece ends over G, not its tonic C"));
    }

    [Fact]
    public void APianosTwoHands_NeedNoInstrument()
    {
        const string piano = """
            title "Minuet"
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            section A { rh { c''4@p d' e' f' | } lh { c'2@p g | } }
            form { A }
            score { grandStaff { staff rh  staff lh } }
            """;
        Assert.Empty(Flatness(piano));
    }

    // Owner's report 2026-10-27: "かっこいいピアノの曲" came back with its left hand between E1 and G2
    // (the AI wrote `e,,` as if a bare `c` were C4; in `instrument piano-left` it is C3).
    private static string Piano(string declarations, string rh, string lh)
        => $"title \"Steel\"\ntime 4/4\noctave absolute\n{declarations}\nsection A {{\n  rh {{ {rh} }}\n  lh {{ {lh} }}\n}}\nform {{ A }}\nscore {{ grandStaff {{ staff rh  staff lh }} }}\n";

    private const string Hands = "part rh { instrument piano-right }\npart lh { instrument piano-left }";
    private const string RightHand = "e'8 b e' g' a'4 g'8 e' | d'8 fis' a' fis' b'4 a'8 fis' | c'8 e' g' b' d''4 c''8 b' | a'4 g'8 fis' e'4 d'8 b |";

    [Fact]
    public void ALeftHandUnderItsRegister_IsToldToMoveUp()
    {
        // The owner's left hand, as it sounded: E1 B1 E2 B1 | C1 G1 C2 | A1 E2 | B1 F♯2 E1.
        string low = Piano(Hands, RightHand, "e,,4 r8 b,,8 e,4 b,,4 | c,,4. g,,8 c,2 | a,,2 e,4 r4 | b,,4. fis,8 e,,2 |");
        var finding = Assert.Single(CompositionChecks.Of(low), f => f.StartsWith("lh:"));
        Assert.Contains("far below a pianist's left hand", finding);
        Assert.Contains("C3 is `c`", finding);
        // The same hand two octaves up (round C3) is where a left hand plays.
        string usual = Piano(Hands, RightHand, "e4 r8 b8 e'4 b4 | c4. g8 c'2 | a2 e'4 r4 | b4. fis'8 e2 |");
        Assert.DoesNotContain(CompositionChecks.Of(usual), f => f.Contains("a pianist's"));
        // Two hands braced with no instrument are a piano's too: the lower staff is the left hand.
        // Since the owner's decision 2026-10-28 no preset moves an octave, so the same marks sound
        // the same there.
        string bare = Piano("part rh { clef treble }\npart lh { clef bass }", RightHand, "e,,4 r8 b,,8 e,4 b,,4 | c,,4. g,,8 c,2 | a,,2 e,4 r4 | b,,4. fis,8 e,,2 |");
        Assert.Contains(CompositionChecks.Of(bare), f => f.StartsWith("lh:") && f.Contains("far below") && f.Contains("C3 is `c`"));
    }

    // Owner's report 2026-10-27: the next piano piece labelled its staves 右手 and 左手, and its right
    // hand, planned for D5–A5, sounded D5–G6 — written by LilyPond's octaves, where `c'` is middle C.
    [Fact]
    public void ARightHandAnOctaveHigh_AndStavesNamedByHand_AreToldSo()
    {
        string named = Piano("part rh \"右手\" \"右手\" { instrument piano-right }\npart lh \"左手\" \"左手\" { instrument piano-left }",
            // As it sounded (D5–G6): one mark more than LilyPond's own, which Lily# now reads
            // alike (owner's decision 2026-10-28).
            "d''4 f''8 a''8 d'''4 a'''4 | f''4 bes''8 d'''8 f'''2 | a''4. d'''8 f'''4 a'''4 | g''4 bes''8 d'''8 g'''2 |",
            "d,2. a,4 | bes,2 f4 d4 | d,2 a,4 f4 | g,4. d8 g4 bes4 |");
        var findings = CompositionChecks.Of(named);
        var high = Assert.Single(findings, f => f.StartsWith("rh:"));
        Assert.Contains("high above a pianist's right hand", high);
        Assert.Contains("middle C is `c'`", high);
        Assert.Contains(findings, f => f.StartsWith("rh, lh: a staff is not labelled by the hand that plays it"));
        // "Piano" names the instrument.
        Assert.DoesNotContain(CompositionChecks.Of(named.Replace("\"右手\" \"右手\"", "\"Piano\" \"Pno.\"").Replace("\"左手\" \"左手\"", "")),
            f => f.Contains("labelled by the hand"));
    }

    // Owner's review 2026-10-27: a left hand of steady root-and-fifth quarters is a piano's figure,
    // not a flat line (told so, the AI broke it into dotted notes and rests); and that left hand
    // up to D3 under a right hand from D5 left the middle of the keyboard empty.
    [Fact]
    public void ALeftHandsFigure_IsNotFlat_ButHandsFarApartAre()
    {
        string Bars(string bar, int n) => string.Concat(Enumerable.Repeat(bar + " | ", n));
        string apart = Piano(Hands, Bars("d''8 f'' a''4 g''8 f'' e''4", 8) + Bars("a''4 f'' d''2", 8), Bars("d,4 a, d a,", 16));
        var findings = CompositionChecks.Of(apart);
        Assert.DoesNotContain(findings, f => f.StartsWith("lh:") && f.Contains("of its notes are"));
        Assert.Contains(findings, f => f.StartsWith("rh, lh: the hands stand far apart") && f.Contains("up to D3") && f.Contains("from D5"));
        // The same right hand an octave down, over the same figure: close enough.
        string close = Piano(Hands, Bars("d'8 f' a'4 g'8 f' e'4", 8) + Bars("a'4 f' d'2", 8), Bars("d,4 a, d a,", 16));
        Assert.DoesNotContain(CompositionChecks.Of(close), f => f.Contains("far apart") || f.Contains("a pianist's"));
        // A right hand's line of one value is still flat.
        Assert.Contains(CompositionChecks.Of(Piano(Hands, Bars("d'8 f' a' f' e' f' g' e'", 16), Bars("d,2 a,", 16))),
            f => f.StartsWith("rh, the melody: 100% of its notes are eighth notes"));
    }

    // Owner's review 2026-10-29: a wedding piece's broken chords ran D2 to A4 every bar, on the
    // melody's own notes and over them where it dipped.
    [Fact]
    public void ALeftHandIntoTheMelody_AndAFigureWiderThanATwelfth_AreSaid()
    {
        string Waltz(string rh, string lh) => $"title \"Hana\"\ntime 6/8\noctave absolute\n{Hands}\n"
            + $"section A {{\n  rh {{ {rh} }}\n  lh {{ {lh} }}\n}}\nform {{ A }}\nscore {{ grandStaff {{ staff rh  staff lh }} }}\n";
        string into = Waltz(
            "g'4.( b'8 a'4) | a'8( d''8 c''4 a'4) | b'4( e''8 d''8 b'4) | a'4.( d'4.) | e'4( g'8 a'8 g'4) | d''8( b'8 a'4 g'4) | a'4.( c''8 b'4) | g'2. |",
            "g,8 d g b d' b | fis,8 a d' fis' a' fis' | e,8 b e' g' b' g' | d,8 a d' fis' a' fis' | c,8 g c' e' g' e' | a,8 e a c' e' c' | d,8 a d' fis' a' fis' | g,2. |");
        var findings = CompositionChecks.Of(into);
        var meet = Assert.Single(findings, f => f.StartsWith("lh, rh: in "));
        Assert.Contains("the left hand reaches the melody", meet);
        Assert.Contains("bars 2, 3, 4", meet);
        var wide = Assert.Single(findings, f => f.StartsWith("lh: in "));
        Assert.Contains("spans more than a twelfth (F♯2 to A4 in bar 2)", wide);
        // A figure within a twelfth, under the tune (the owner's next piece): neither.
        string kept = Waltz(
            "d''4 b'8 a'4 g'8 | a'4 b'8 d''4. | e''4 d''8 b'4 a'8 | fis'4. a'4. | b'4 d''8 g''4. | e''4 d''8 c''4. | a'4 c''8 fis''4. | g''2. |",
            "g,8 d b, d g d | fis,8 a, d a, fis a, | e,8 b, g b, e b, | d,8 a, fis a, d a, | b,8 d g d b, d | c,8 g, e g, c g, | d,8 a, c a, fis a, | g,2. |");
        Assert.DoesNotContain(CompositionChecks.Of(kept), f => f.Contains("reaches the melody") || f.Contains("twelfth"));
        // The same hands beside a flute: the tune is the flute's, and the right hand accompanies it.
        string flute = into.Replace($"{Hands}\n", $"part fl {{ instrument flute }}\n{Hands}\n")
            .Replace("section A {\n", "section A {\n  fl { d''2. | d''2. | d''2. | d''2. | d''2. | d''2. | d''2. | d''2. | }\n")
            .Replace("score { grandStaff", "score { staff fl grandStaff");
        Assert.Contains(CompositionChecks.Findings(flute), f => f.Id == "piano.figure-wide");
        Assert.DoesNotContain(CompositionChecks.Findings(flute), f => f.Id == "piano.left-reaches-melody");
    }

    // Owner's decision 2026-11-02: a carol's piano wrote one @p, in its last bar, and no check spoke.
    [Fact]
    public void APianosHands_AreOnePlayer_ForALateLevel()
    {
        string Book(string introRh, string introLh, string coda, string fl = "c''1 | c''1 |") => "title \"Late\"\ntime 4/4\noctave absolute\n"
            + $"part fl {{ instrument flute }}\n{Hands}\n"
            + $"section Intro {{\n  fl {{ {fl} }}\n  rh {{ {introRh} }}\n  lh {{ {introLh} }}\n}}\n"
            + $"section Coda {{\n  fl {{ c''1 | c''1 | }}\n  rh {{ <e' g'>1{coda} | <e' g'>1 | }}\n  lh {{ c1 | c1 | }}\n}}\n"
            + "form { Intro Coda }\nscore { staff fl grandStaff { staff rh  staff lh } }\n";
        string Late(string book) => string.Join(" / ", CompositionChecks.Findings(book).Where(f => f.Id == "dynamics.late").Select(f => f.Message));

        // Neither hand has a level before the coda.
        Assert.Contains("rh, lh: no level until Coda", Late(Book("<e' g'>1 | <e' g'>1 |", "c1 | c1 |", "@p", "c''1@p | c''1 |")));
        // A level in either hand is the piano's.
        Assert.DoesNotContain("no level until", Late(Book("<e' g'>1@p | <e' g'>1 |", "c1 | c1 |", "", "c''1@p | c''1 |")));
        Assert.DoesNotContain("no level until", Late(Book("<e' g'>1 | <e' g'>1 |", "c1@p | c1 |", "", "c''1@p | c''1 |")));
        // A piano that only rests in the first section begins in the next.
        Assert.DoesNotContain("no level until", Late(Book("R | R |", "R | R |", "@p", "c''1@p | c''1 |")));
        // …and so does a flute: the rule is every part's.
        Assert.DoesNotContain("fl", Late(Book("<e' g'>1@p | <e' g'>1 |", "c1 | c1 |", "", "R | r2 r4 r4 |").Replace("fl { c''1 | c''1 | }\n  rh { <e' g'>1", "fl { c''1@mp | c''1 | }\n  rh { <e' g'>1")));
    }

    // Owner's trial 2026-10-29: a 6/8 wedding piece sang `b'8 d''4. b'8 a'8` across its beats, and
    // its 28 bars lasted under a minute.
    [Fact]
    public void ACompoundMetersTuneAcrossItsBeats_AndAPieceUnderAMinute_AreSaid()
    {
        string Piece(string time, string tempo, string rh, string lh, int times)
            => $"title \"Ribbon\"\ntime {time}\ntempo \"Andante\" {tempo}\noctave absolute\nkey g major\n{Hands}\n"
                + $"section A {{\n  rh {{ {rh} }}\n  lh {{ {lh} }}\n}}\nform {{ {string.Join(" ", Enumerable.Repeat("A", times))} }}\n"
                + "score { grandStaff { staff rh  staff lh } }\n";
        const string Lh68 = "g,8 d b d g d | fis,8 a, d fis, a, fis, | e,8 b, g b, e g | d,8 a, c fis a c | c,8 g, e g e g, | b,8 g b d' g d' | d,8 a, c fis a c | g,8 d b d g d |";
        string across = Piece("6/8", "4. = 58",
            "b'8 d''4. b'8 a'8 | g'8 a'8 b'4. d''8 | e''4. d''8 b'4 | a'4. fis'8 a'4 | b'8 d''8 b'4. a'8 | g'4. a'8 b'4 | c''8 b'8 a'8 fis'8 d'4 | g'2. |",
            Lh68, 3);
        var findings = CompositionChecks.Of(across);
        var beats = Assert.Single(findings, f => f.Contains("a note starts within a beat"));
        Assert.StartsWith("rh, the melody: in 9 of its 24 bars", beats);
        Assert.Contains("(bars 1, 2, 5, 9, 10, 13,", beats);
        Assert.Contains(findings, f => f.StartsWith("The piece is 24 bars long, about 50 seconds played")
            // More music, not a slower tempo (owner's report 2026-10-29: ♩ = 72 became 48).
            && f.Contains("never by slowing its tempo (keep the tempo its character asks for).")
            // …nor by a copy under a new name (owner's review 2026-11-01: told it was short, a piece
            // wrote its sections again with a few bars altered).
            && f.EndsWith("not a copy of an earlier section under a new name with a few bars altered.")
            // The bars it takes, at its own pace (owner's trial 2026-10-30: told "24 to 32 bars of 4/4", a quick piece stopped short).
            && f.Contains("a minute and a half to three minutes (at this tempo and meter, about 44 to 86 bars)"));
        // The tune by its beats: not said.
        string kept = Piece("6/8", "4. = 58",
            "d''4 b'8 a'4 g'8 | a'4 b'8 d''4. | e''4 d''8 b'4 a'8 | fis'4. a'4. | b'4 d''8 g''4. | e''4 d''8 c''4. | a'4 c''8 fis''4. | g''2. |",
            Lh68, 3);
        Assert.DoesNotContain(CompositionChecks.Of(kept), f => f.Contains("a note starts within a beat"));
        // 4/4 at 72, 24 bars: 80 seconds, long enough; and a syncopation in 4/4 is a song's own.
        string common = Piece("4/4", "4 = 72", "d''8 b'4 a'8 g'2 | a'4 b'4 d''2 | e''8 d''4 b'8 a'2 | fis'2 a'2 |",
            "g,8 d b d g d b d | fis,8 a, d a, fis, a, d a, | e,8 b, g b, e, b, g b, | d,8 a, fis a, d, a, fis a, |", 6);
        Assert.DoesNotContain(CompositionChecks.Of(common), f => f.Contains("seconds played") || f.Contains("a note starts within a beat"));
    }

    // Owner's trial 2026-10-30: a piece written all in phrases went from @ff to @p unsaid, its levels inside them.
    [Fact]
    public void ADropBetweenLevelsWrittenInPhrases_IsSaid()
    {
        string Piece(string soft) => "title \"Yoake\"\ntime 4/4\noctave absolute\npart m { instrument violin }\n"
            + "phrase loud { c''4@f d'' e'' f'' | }\n" + $"phrase quiet {{ c''4{soft} d'' e'' f'' | }}\n"
            + "section A {\n  m { loud }\n}\nsection B {\n  m { quiet }\n}\nform { A B }\nscore { staff m }\n";
        Assert.Contains(CompositionChecks.Of(Piece("@p")), f => f.StartsWith("m: A ends at @f and B, played after it, opens at @p"));
        Assert.DoesNotContain(CompositionChecks.Of(Piece("@p.subito")), f => f.Contains("ends at @f and B"));
    }

    // Owner's trials 2026-10-29: three wedding pieces went from B's @f straight to the returning A's @p.
    [Fact]
    public void ALoudSectionDroppingToASoftOne_WithNothingLeadingThere_IsSaid()
    {
        string Piece(string bEnd) => "title \"Hikari\"\ntime 4/4\noctave absolute\nkey f major\n" + Hands
            + "\nsection A {\n  rh { a'4@p c''8 d''8 f''2 | g'2 c''4 e''4 | }\n  lh { f,8 c a c' f, c a c' | e,8 c g c' e, c g c' | }\n}\n"
            + $"section B {{\n  rh {{ a'2@mp@cresc d''4 f''4 | {bEnd} | }}\n  lh {{ d,8 a, f a d, a, f a | c,8 g, e g c, g, e g | }}\n}}\n"
            + "form { A B A }\nscore { grandStaff { staff rh  staff lh } }\n";
        var drop = Assert.Single(CompositionChecks.Of(Piece("e''4 f''8 g''8 g''2@f")), f => f.Contains("a drop of"));
        Assert.Equal("rh: B ends at @f and A, played after it, opens at @p — a drop of 3 levels with nothing leading to it: bring "
            + "the level down over B's last bars (a @decresc to @p), or, if the sudden quiet is meant, write @p.subito.", drop);
        // Led down by a hairpin, or one level: not said.
        Assert.DoesNotContain(CompositionChecks.Of(Piece("e''4@f f''8 g''8 g''2@decresc")), f => f.Contains("a drop of"));
        Assert.DoesNotContain(CompositionChecks.Of(Piece("e''4 f''8 g''8 g''2@mp")), f => f.Contains("a drop of"));
        Assert.DoesNotContain(CompositionChecks.Of(Piece("e''4 f''8 g''8 g''2@f").Replace("a'4@p", "a'4@p.subito")), f => f.Contains("a drop of"));
    }

    // Owner's trial 2026-10-29: B in D major, then A and the coda with no key line — back in G, as
    // they are drawn and played — was told it "ends in D major", and the chat added a needless key.
    [Fact]
    public void TheKeyAPieceEndsIn_IsTheLastSectionPlayed_ItsOwnOrTheFiles()
    {
        string Piece(string form) => "title \"Ribbon\"\ntime 4/4\noctave absolute\nkey g major\n" + Hands
            + "\nsection A {\n  rh { b'4 d''4 a'4 g'4 | }\n  lh { g,2 d2 | }\n}\n"
            + "section B {\n  key d major\n  rh { fis'4 a'4 e'4 d'4 | }\n  lh { a,2 d,2 | }\n}\n"
            + "section Coda {\n  rh { g'1 | }\n  lh { g,1 | }\n}\n"
            + $"form {{ {form} }}\nscore {{ grandStaff {{ staff rh  staff lh }} }}\n";
        Assert.DoesNotContain(CompositionChecks.Of(Piece("A B A Coda")), f => f.StartsWith("The piece begins in") || f.StartsWith("The piece ends over"));
        var findings = CompositionChecks.Of(Piece("A B"));
        Assert.Contains(findings, f => f.StartsWith("The piece begins in G major and ends in D major"));
        Assert.DoesNotContain(findings, f => f.StartsWith("The piece ends over"));
    }

    [Fact]
    public void ARightHandOverItsRegister_IsToldToMoveDown()
    {
        string high = Piano(Hands, "e'''8 b'' e''' g''' a'''4 g'''8 e''' | d'''8 fis''' a''' fis''' b'''4 a'''8 fis''' |", "e2 b, | c2 g, |");
        Assert.Contains(CompositionChecks.Of(high), f => f.StartsWith("rh:") && f.Contains("high above a pianist's right hand"));
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

    // Owner's rules 2026-10-27: a band has its whole rhythm section — drums, electric bass, a
    // guitar or keyboards — whichever members were asked for, and one tempo throughout.
    private static string Band(string parts, string sections, string score, string tempo = "tempo 112")
        => $"title \"Riff\"\n{tempo}\ntime 4/4\noctave absolute\n{parts}\nsection A {{\n{sections}\n}}\nform {{ A }}\nscore {{ {score} }}\n";

    private const string Guitar = "part gtr { instrument guitar midiInstrument \"overdriven guitar\" }";
    private const string Bass = "part bass { instrument bass midiInstrument \"electric bass (finger)\" }";
    private const string Drums = "part kit { clef percussion }";
    private const string GuitarMusic = "  gtr { <e, b,>8 r <e, b,> r <e, b,>4 <c g> | <g, d>8 r <g, d> r <d a>2 | }";
    private const string BassMusic = "  bass { e8 e e e c c c c | g,8 g, g, g, d d d d | }";
    private const string DrumMusic = "  kit { voice { hh8 hh hh hh hh hh hh hh | hh8 hh hh hh hh hh hh hh | } { bd4 sn bd sn | bd4 sn bd sn | } }";

    // Owner's report 2026-10-29: a rock instrumental's guitar played power chords and arpeggios
    // through verse and chorus — a tune only in a four-bar solo.
    [Fact]
    public void ABandInstrumentalWithNoTune_IsSaid()
    {
        string Rock(string guitar) => Band(Guitar + "\n" + Bass + "\n" + Drums,
            $"  gtr {{ {guitar} }}\n  bass {{ {string.Concat(Enumerable.Repeat("e,8 e, e, e, e, e, e, e, | c8 c c c d d d d | ", 4))} }}\n"
            + $"  kit {{ voice {{ {string.Concat(Enumerable.Repeat("hh8 hh hh hh hh hh hh hh | ", 8))} }} {{ {string.Concat(Enumerable.Repeat("bd4 sn bd sn | ", 8))} }} }}",
            "staff gtr  staff bass  staff kit", "tempo 176");
        string chords = Rock(string.Concat(Enumerable.Repeat("<e, b,>8 r <e, b,> <e, b,> e, g, b, e | <c g>4 <c g>8 r <d a,>4 <d a,> | ", 4)));
        Assert.Contains(CompositionChecks.Of(chords), f => f.StartsWith("A tune is played in only 0 of the 8 bars"));
        string tune = Rock(string.Concat(Enumerable.Repeat("e'8 g' b'4 a'8 g' fis'4 | g'4. a'8 b'2 | ", 4)));
        Assert.DoesNotContain(CompositionChecks.Of(tune), f => f.StartsWith("A tune is played"));
        // A line in every bar, but a chord and six running eighths each time (owner's report
        // 2026-10-29): a tune, and held to a melody's rhythm.
        var running = CompositionChecks.Of(Rock(string.Concat(Enumerable.Repeat("<e, b,>4 e'8 fis' gis' b' a' fis' | ", 8))));
        Assert.DoesNotContain(running, f => f.StartsWith("A tune is played"));
        Assert.Contains(running, f => f.StartsWith("gtr, the melody: 85% of its notes are eighth notes"));
        Assert.Contains(running, f => f.StartsWith("gtr, the melody: 8 of its 8 bars have the same rhythm"));
        // Not let off as a hymn's even pace (owner's trial 2026-10-30: a rock guitar kept its eighths "for the drive").
        Assert.All(running.Where(f => f.StartsWith("gtr, the melody: ") && !f.Contains("shape of the bar before")), f =>
        {
            Assert.Contains("a band's drive is its drums' and bass's eighths under the tune", f);
            Assert.DoesNotContain("a hymn", f);
        });
        // Sung: the words are the tune.
        Assert.DoesNotContain(CompositionChecks.Of(chords.Replace("score {", "lyrics words sings gtr { la }\nscore {")),
            f => f.StartsWith("A tune is played"));
    }

    // Owner's report 2026-11-01: `Downloads/Redline.lys`, an overdriven guitar in `<e, e b e' b'>` (the
    // voicing tool's first E5 shape) from start to end — on a distorted sound a full chord blurs.
    [Fact]
    public void FullChordsOnADistortedSound_AreSaid()
    {
        string Rock(string guitar, string sound = "overdriven guitar") => Band(Guitar.Replace("overdriven guitar", sound) + "\n" + Bass + "\n" + Drums,
            $"  gtr {{ {guitar} }}\n  bass {{ {string.Concat(Enumerable.Repeat("e,8 e, e, e, e, e, e, e, | c8 c c c d d d d | ", 4))} }}\n"
            + $"  kit {{ voice {{ {string.Concat(Enumerable.Repeat("hh8 hh hh hh hh hh hh hh | ", 8))} }} {{ {string.Concat(Enumerable.Repeat("bd4 sn bd sn | ", 8))} }} }}",
            "staff gtr  staff bass  staff kit", "tempo 176");
        string full = string.Concat(Enumerable.Repeat("<e, e b e' b'>8 r <e, e b e' b'> <e, e b e' b'> <e, e b e' b'>4 r | <c g c' g'>4 <c g c' g'>8 r <d a d' a'>4 <d a d' a'> | ", 4));
        Assert.Contains(CompositionChecks.Of(Rock(full)), f => f.StartsWith("gtr, on a distorted sound (overdriven guitar): 32 of its 32 chords are 4 notes or more "
            + "(E2 E3 B3 E4 B4 in bar 1 first, as the form plays them) — distortion blurs a full chord into a roar: strike two or three notes"));
        // Power chords of two and three notes.
        string power = string.Concat(Enumerable.Repeat("<e, b,>8 r <e, b, e> <e, b,> <e, b,>4 r | <c g>4 <c g c'>8 r <d a>4 <d a> | ", 4));
        Assert.DoesNotContain(CompositionChecks.Of(Rock(power)), f => f.Contains("distorted sound"));
        // A clean sound rings full chords.
        Assert.DoesNotContain(CompositionChecks.Of(Rock(full, "electric guitar (clean)")), f => f.Contains("distorted sound"));
    }

    // Owner's review 2026-11-01 (a rock instrumental, Yoake-o-Kakenukete): one chord loop from the intro
    // to the outro, the chorus starting on its second chord; no hairpin anywhere and every section
    // opening .subito; and the chorus tune over the same guitar's chords told of octave slips.
    private static string Looped(string sectionC, string levels = "@f", string lastGuitar = "e'4 g' a'2")
    {
        string Bars(string bass) => string.Join(" ", bass.Split(' ').Select(b => $"{b}1 |"));
        string Section(string name, string bass, string guitar) =>
            $"section {name} {{\n  gtr {{ {string.Concat(Enumerable.Repeat(guitar + " | ", 4))} }}\n  bass {{ {Bars(bass)} }}\n"
            + "  kit { voice { hh8 hh hh hh hh hh hh hh | hh8 hh hh hh hh hh hh hh | hh8 hh hh hh hh hh hh hh | hh8 hh hh hh hh hh hh hh | } "
            + "{ bd4 sn bd sn | bd4 sn bd sn | bd4 sn bd sn | bd4 sn bd sn | } }\n}\n";
        return $"title \"Loop\"\ntempo 168\ntime 4/4\noctave absolute\nkey c minor\n{Guitar}\n{Bass}\n{Drums}\n"
            + Section("A", "c, aes,, ees, bes,,", $"e'4{levels} g' a'2") + Section("B", "aes,, ees, bes,, c,", "e'4 g' a'2")
            + Section("C", sectionC, lastGuitar) + "form { A B C }\nscore { staff gtr  staff bass  staff kit }\n";
    }

    [Fact]
    public void OneChordProgressionThroughEverySection_IsSaid()
    {
        Assert.Contains(CompositionChecks.Of(Looped("ees, bes,, c, aes,,")), f => f.StartsWith("One chord progression from start to end: the bass's roots walk C A♭ E♭ B♭ in every section (A, B, C)"));
        // A section of its own: no finding (three sections: every one must be on the loop).
        Assert.DoesNotContain(CompositionChecks.Of(Looped("f, g, c, c,")), f => f.StartsWith("One chord progression"));
    }

    [Fact]
    public void LevelsSetBySubitoAtEverySection_WithNoHairpin_AreSaid()
    {
        string set = Looped("ees, bes,, c, aes,,").Replace("gtr { e'4 g' a'2", "gtr { e'4@f.subito g' a'2").Replace("gtr { e'4@f g'", "gtr { e'4@p.subito g'");
        Assert.Contains(CompositionChecks.Of(set), f => f.StartsWith("The levels are set, not shaped: no crescendo or decrescendo anywhere, and 3 sections open on a .subito (A, B, C)"));
        // One hairpin anywhere, and the levels are shaped.
        Assert.DoesNotContain(CompositionChecks.Of(set.Replace("a'2 |  }", "a'2@cresc |  }")), f => f.StartsWith("The levels are set"));
        // Two sections .subito of three: not yet.
        Assert.DoesNotContain(CompositionChecks.Of(Looped("ees, bes,, c, aes,,").Replace("gtr { e'4@f g'", "gtr { e'4@p.subito g'")), f => f.StartsWith("The levels are set"));
    }

    // Owner's review 2026-11-01 (Kaerimichi-no-Akari, a song to sing and play yourself): a guitar staff
    // of five-note strums under the chords row, the same harmony twice.
    [Fact]
    public void AStaffStrikingTheChordsRowsChords_IsSaid()
    {
        string Song(string guitar) => "title \"Akari\"\ntime 4/4\noctave absolute\nkey d major\npart singer { instrument alto }\n"
            + "part guitar { instrument guitar }\nsection A {\n  singer { " + string.Concat(Enumerable.Repeat("d'4 fis'8 a'8 d''2 | a'4 cis''4 e''4 cis''4 | ", 4)) + "}\n"
            + "  guitar { " + string.Concat(Enumerable.Repeat(guitar + " ", 4)) + "}\n  chords harmony { D | A | D | A | D | A | D | A | }\n}\nform { A }\n"
            + "score { chords harmony  staff singer  staff guitar }\n";
        Assert.Contains(CompositionChecks.Of(Song("<d fis a d' fis'>2 <d fis a d' fis'>2 | <a cis' e' a' cis''>4 <a cis' e' a' cis''>4 <a cis' e' a' cis''>2 |")),
            f => f.StartsWith("guitar: its staff says what the chords row says — the row's chords struck as block chords in 8 of its 8 bars"));
        // A picking pattern is the player's own.
        Assert.DoesNotContain(CompositionChecks.Of(Song("d8 a d' fis' a' fis' d' a | a,8 e a cis' e' cis' a e |")), f => f.Contains("says what the chords row says"));
        // A bass note, then the chord: not block chords alone.
        Assert.DoesNotContain(CompositionChecks.Of(Song("d4 <fis a d'>4 d4 <fis a d'>4 | a,4 <cis' e' a'>4 a,4 <cis' e' a'>4 |")), f => f.Contains("says what the chords row says"));
    }

    // Owner's review 2026-11-01 (Kaerimichi-no-Akari3, a song to sing and play yourself): 48 bars sung
    // from the first note to the last with no rest, nearly every phrase's end a quarter note.
    [Fact]
    public void ASingerWithNoRestInSixteenBars_IsSaid()
    {
        string Song(string part, int bars, string bar8 = "g'4 a'4 b'4 c''4 |") => "title \"Breath\"\ntime 4/4\noctave absolute\n" + part + "\nsection A {\n  singer { "
            + string.Concat(Enumerable.Range(1, bars).Select(i => i == 8 ? bar8 + " " : "c''4 b'4 a'4 g'4 | ")) + "}\n}\nform { A }\nscore { staff singer }\n";
        Assert.Contains(CompositionChecks.Of(Song("part singer { instrument alto }", 16)),
            f => f.StartsWith("singer: sings 16 bars in a row without a rest (bars 1–16, as the form plays them)"));
        // A rest in bar 8: two runs of 7 and 8, neither long enough.
        Assert.DoesNotContain(CompositionChecks.Of(Song("part singer { instrument alto }", 16, "g'4 a'4 b'4 r4 |")), f => f.Contains("without a rest"));
        // Fifteen bars: not yet.
        Assert.DoesNotContain(CompositionChecks.Of(Song("part singer { instrument alto }", 15)), f => f.Contains("without a rest"));
        // A violin may play on; a part a lyrics track sings is a singer whatever its instrument.
        Assert.DoesNotContain(CompositionChecks.Of(Song("part singer { instrument violin }", 16)), f => f.Contains("without a rest"));
        Assert.Contains(CompositionChecks.Of(Song("part singer { instrument violin }", 16).Replace("}\n}\nform", "}\n  lyrics w sings singer { "
            + string.Concat(Enumerable.Repeat("la la la la | ", 16)) + "}\n}\nform")), f => f.Contains("without a rest"));
    }

    // Owner's review 2026-11-01 (Yoru-Ake-no-Uta): every two-bar phrase in `8 8 4 2 | 4 2 r4`, the chorus
    // opening with the verse's first phrase note for note, the top note spent in bar 3.
    private static string Hook(string verse, string chorus) => "title \"Hook\"\ntime 4/4\noctave absolute\npart singer { instrument alto }\n"
        + "section Verse {\n  singer { " + verse + "}\n  lyrics w sings singer { la la | }\n}\nsection Chorus {\n  singer { " + chorus + "}\n}\n"
        + "form { Verse Chorus Verse Chorus }\nscore { staff singer  lyrics w }\n";
    private const string P = "g'8 a'8 b'4 e''2 | d''4 c''2 r4 | ";      // the formula, topping at E5
    private const string Q = "e'8 f'8 g'4 c''2 | b'4 a'2 r4 | ";       // the formula, lower (C5)
    private const string R = "a'4. b'8 c''4 e''4 | d''2 c''2 | ";      // another rhythm, E5
    private const string X = "e'4 g'2 a'4 | g'4 a'4 b'4 r4 | ";        // another rhythm, lower

    [Fact]
    public void OneRhythmInEveryPhrase_IsSaid()
    {
        Assert.Contains(CompositionChecks.Of(Hook(Q + Q + Q + X, P + P + P + P)),
            f => f.StartsWith("singer, the melody: 7 of its 8 2-bar phrases have the same rhythm — one formula poured into every phrase"));
        // The chorus in a rhythm of its own: half the phrases, not a formula.
        Assert.DoesNotContain(CompositionChecks.Of(Hook(Q + Q + Q + X, R + R + R + R)), f => f.Contains("one formula poured"));
    }

    [Fact]
    public void AChorusOpeningWithTheVersesPhrase_IsSaid()
    {
        Assert.Contains(CompositionChecks.Of(Hook(P + Q + Q + Q, P + R + R + R)),
            f => f.StartsWith("singer, the melody: the chorus (Chorus) opens with Verse's first phrase, note for note (bars 1–2 and 9–10, as the form plays them)"));
        // One note changed: its own opening.
        Assert.DoesNotContain(CompositionChecks.Of(Hook(P + Q + Q + Q, "a'8 b'8 c''4 e''2 | d''4 c''2 r4 | " + R + R + R)), f => f.Contains("opens with Verse's first phrase"));
    }

    [Fact]
    public void ATopNoteSpentInTheFirstPhrase_OrAChorusNoHigherThanTheVerse_IsSaid()
    {
        const string P2 = "a'8 b'8 c''4 e''2 | d''4 c''2 r4 | ";
        Assert.Contains(CompositionChecks.Of(Hook(P + Q + Q + Q, P2 + R + R + R)),
            f => f.StartsWith("singer, the melody: its highest note, E5, comes in its first phrase (bar 1, as the form plays them) and is never passed"));
        // The top in the verse's third phrase, and the chorus below it.
        Assert.Contains(CompositionChecks.Of(Hook(Q + Q + P + Q, X + X + X + X)),
            f => f.StartsWith("singer, the melody: the chorus (Chorus) stays below the sections before it (its top B4, theirs E5)"));
        // Level with the verse's peak: not told (owner's decision 2026-11-01).
        Assert.DoesNotContain(CompositionChecks.Of(Hook(Q + Q + P + Q, P2 + R + R + R)), f => f.Contains("stays below"));
        // The chorus above the verse: a song that climbs.
        var climbs = CompositionChecks.Of(Hook(Q + Q + P + Q, P2 + "a'4. b'8 c''4 g''4 | f''2 e''2 | " + R + R));
        Assert.DoesNotContain(climbs, f => f.Contains("comes in its first phrase"));
        Assert.DoesNotContain(climbs, f => f.Contains("stays below"));
    }

    // Owner's review 2026-11-01 (Kaerimichi-no-Akari4): every phrase closing `… 4 r8`, the sections
    // closing on one lick, and a song of 24 bars (69 seconds) let off as a head played round.
    [Fact]
    public void PhrasesEndingTheSameWay_AreSaid_WhereThePhrasesThemselvesVary()
    {
        // Four first bars, four rhythms; the second bar `4 2 r4` every time.
        const string S1 = "g'8 a'8 b'4 e''2 | d''4 c''2 r4 | ", S2 = "e'4 g'8 a'8 b'2 | d''4 c''2 r4 | ",
            S3 = "g'4. a'8 b'4 e''4 | d''4 c''2 r4 | ", S4 = "g'8 a'8 b'8 c''8 e''2 | d''4 c''2 r4 | ";
        var same = CompositionChecks.Of(Hook(S1 + S2 + S3 + S4, S2 + S3 + S4 + S1));
        Assert.DoesNotContain(same, f => f.Contains("one formula poured"));
        Assert.Contains(same, f => f.StartsWith("singer, the melody: 8 of its 8 phrases end the same way"));
        // Half of them closing otherwise: not one gesture.
        Assert.DoesNotContain(CompositionChecks.Of(Hook(S1 + S2 + S3 + S4, R + R + R + R)), f => f.Contains("end the same way"));
    }

    [Fact]
    public void SectionsClosingOnOneFigure_AreSaid()
    {
        const string Lick = "d''8 c''4 b'8 a'8 g'4 r8 | ", Lick2 = "d''8 c''4 b'8 a'8 d''4 r8 | ", Other = "e'4 g'4 a'4 b'4 | ", Other2 = "d'4 e'4 fis'4 g'4 | ";
        string Song(string a, string b, string c, string d) => "title \"Tag\"\ntime 4/4\noctave absolute\npart singer { instrument alto }\n"
            + $"section A {{ singer {{ g'4 a'4 b'4 c''4 | {a}}} lyrics w sings singer {{ la la | }} }}\nsection B {{ singer {{ e'4 g'4 a'4 b'4 | {b}}} }}\n"
            + $"section C {{ singer {{ c''4 b'4 a'4 g'4 | {c}}} }}\nsection D {{ singer {{ a'4 b'4 c''4 d''4 | {d}}} }}\n"
            + "form { A B C D A B }\nscore { staff singer  lyrics w }\n";
        Assert.Contains(CompositionChecks.Of(Song(Lick, Lick2, Lick, Other)),
            f => f.StartsWith("singer, the melody: 3 of its 4 sections close on the same figure (D5 C5 B4 A4 in bar 2 first, as the form plays them; A, B, C)"));
        // Two of four: not most.
        Assert.DoesNotContain(CompositionChecks.Of(Song(Lick, Lick2, Other, Other2)), f => f.Contains("close on the same figure"));
    }

    // Owner's review 2026-11-01 (Kaze-no-Michi): 21 of 24 bars in one rhythm for their place in the
    // four-bar phrase, the whole phrases alike in 4 of 6 and the two-bar ones split in half.
    [Fact]
    public void FourBarPhrasesKeepingOneRhythmForEachPlace_AreSaid()
    {
        const string A = "g'8 a'8 b'4 a'4 g'4 | a'8 b'8 a'4 fis'2 | e'4 g'4 b'2 | b'8 a'8 g'2 r4 | ",
            B = "g'4 a'8 b'8 d''4 b'4 | a'8 fis'8 d''4 a'2 | g'4 a'4 g'2 | fis'8 g'8 a'2 r4 | ";
        var same = CompositionChecks.Of(Hook(A + B + A, A + B + A));
        Assert.DoesNotContain(same, f => f.Contains("one formula poured"));
        Assert.Contains(same, f => f.StartsWith("singer, the melody: its 6 four-bar phrases keep one rhythm for each bar's place in the phrase (22 of 24 bars)"));
        // Each bar's rhythm its own in three kinds of phrase: no place has one rhythm.
        const string C = "g'4. a'8 b'2 | a'2 g'4 fis'4 | e'8 g'8 b'4 a'4 g'4 | a'1 | ",
            D = "b'2 a'8 g'8 fis'4 | g'4 a'4 b'4 c''4 | d''2. r4 | c''4. b'8 a'2 | ";
        Assert.DoesNotContain(CompositionChecks.Of(Hook(A + C + D, A + C + D)), f => f.Contains("four-bar phrases keep"));
    }

    // Owner's review 2026-11-01 (Ashita-no-Mado): 3, 3, 5, 2 notes in 31 of 32 bars, the rhythms
    // changed enough to pass, and the chorus opening an octave over the verse.
    [Fact]
    public void LinesCutToOneCountOfNotes_AreSaid_ThoughTheirRhythmsDiffer()
    {
        const string A = "r8 b'8~ b'4 c''4 d''4 | c''4 a'4 f''2 | r8 g'8~ g'4 f'8 d'8 f'8 g'8 | a'4 f'2 r4 | ",
            B = "b'2 a'4 f'4 | r8 a'8~ a'4 f'4 c''4 | r8 d'8~ d'4 f'8 g'8 b'8 g'8 | f'2 b'4 r4 | ",
            C = "c''4 e''2 g'4 | g'2 b'4 e''4 | d''4 c''8 b'8 f'4 d''4 | r4 a'4 f'2 | ";
        var moulded = CompositionChecks.Of(Hook(A + B + C, C + A + B));
        Assert.Contains(moulded, f => f.StartsWith("singer, the melody: its 6 four-bar phrases keep the same number of notes in each bar's place (3, 3, 5, 2 notes, in 24 of 24 bars)"));
        // Lines of their own lengths are not told.
        const string D = "g'1 | a'2 b'2 | c''8 b'8 a'8 g'8 a'2 | b'1 | ",
            E = "e'4 g'4 a'4 b'4 | c''2. r4 | b'2 a'4 g'4 | f'8 g'8 a'8 b'8 c''4 d''4 | ";
        Assert.DoesNotContain(CompositionChecks.Of(Hook(A + D + E, D + E + A)), f => f.Contains("same number of notes"));
    }

    [Fact]
    public void AChorusOpeningWithTheVersesFirstBarAnOctaveUp_IsSaid()
    {
        const string V = "r8 b8~ b4 c'4 d'4 | c'4 a4 f'2 | ", K = "r8 b'8~ b'4 c''4 d''4 | f'4 a'4 c''2 | ";
        Assert.Contains(CompositionChecks.Of(Hook(V + Q + Q + Q, K + R + R + R)),
            f => f.StartsWith("singer, the melody: the chorus (Chorus) opens with the first bar of Verse, an octave apart or none (bars 1 and 9"));
        // Its first bar a line of its own: not told.
        Assert.DoesNotContain(CompositionChecks.Of(Hook(V + Q + Q + Q, "e''4. d''8 b'2 | f'4 a'4 c''2 | " + R + R + R)), f => f.Contains("first bar of Verse"));
    }

    // Owner's review 2026-11-01 (Kaze-no-Hikari, a school chorus): alto and men in octaves 17 times, the
    // alto's 91 moves 12 steps, and an introduction for the voices with no words.
    private static string Choir(string sop, string alt, string men, string intro = "") => "title \"Choir\"\ntime 4/4\noctave absolute\n"
        + "part sop { instrument soprano }\npart alt { instrument alto }\npart men { instrument baritone }\n" + intro
        + $"section A {{\n  sop {{ {sop} }}\n  alt {{ {alt} }}\n  men {{ {men} }}\n"
        + "  lyrics w sings sop { " + string.Concat(Enumerable.Repeat("la la la la | ", 8)) + "}\n}\n"
        + "form { " + (intro.Length > 0 ? "I " : "") + "A }\nscore { choirStaff { staff sop  staff alt  staff men }  lyrics w }\n";
    private static string Bars(string bar) => string.Concat(Enumerable.Repeat(bar + " | ", 8));
    private const string Sop = "g'4 a' b' c''";

    [Fact]
    public void SingersInOctaves_AndAnInnerVoiceOfLeaps_AreSaid()
    {
        var doubled = CompositionChecks.Of(Choir(Bars(Sop), Bars("e'4 c' g e'"), Bars("e4 c g, e")));
        Assert.Contains(doubled, f => f.StartsWith("alt and men move in octaves"));
        Assert.Contains(doubled, f => f.StartsWith("alt: only 0 of its"));
        // Lines of their own: the alto by step, the men against it.
        var lines = CompositionChecks.Of(Choir(Bars(Sop), Bars("e'4 f' g' e'"), Bars("c4 d c g,")));
        Assert.DoesNotContain(lines, f => f.Contains("move in octaves"));
        Assert.DoesNotContain(lines, f => f.StartsWith("alt: only"));
    }

    // Owner's review 2026-11-01 (Yume-no-Saki-e): the men in a tenor's range above the alto, and the
    // alto, the inner part of the stack, not the middle of it by height.
    [Fact]
    public void ALowerSingerAboveTheOneOverIt_IsSaid_AndTheStackIsTheScores()
    {
        var crossed = CompositionChecks.Of(Choir(Bars(Sop), Bars("c'4 d' e' c'"), Bars("e'4 g' e' d'")));
        Assert.Contains(crossed, f => f.StartsWith("men stands above alt at "));
        Assert.DoesNotContain(CompositionChecks.Of(Choir(Bars(Sop), Bars("e'4 f' g' e'"), Bars("c4 d c g,"))), f => f.Contains("stands above"));
        // The alto of the stack leaps though the men sound above it.
        Assert.Contains(CompositionChecks.Of(Choir(Bars(Sop), Bars("g4 e' g e'"), Bars("g'4 a' g' a'"))), f => f.StartsWith("alt: only "));
    }

    // Owner's review 2026-11-01 (Hikari-o-Tsunaide): the soprano's E4 under the alto's G♯4 on the last chord.
    [Fact]
    public void ALastChordWithItsVoicesOutOfOrder_IsSaid()
    {
        string Close(string sop, string alt) => Choir(Bars(Sop)[..^("g'4 a' b' c'' | ".Length)] + sop + " | ", Bars("e'4 f' g' e'")[..^("e'4 f' g' e' | ".Length)] + alt + " | ", Bars("c4 d c g,"));
        Assert.Contains(CompositionChecks.Of(Close("e'1", "g'1")), f => f.StartsWith("alt ends above sop on the last chord (G4 over E4)"));
        Assert.DoesNotContain(CompositionChecks.Of(Close("g'1", "e'1")), f => f.Contains("on the last chord"));
    }

    // Owner's review 2026-11-01 (Hikari-no-Michi): the piano's chords and roots from first to last, and
    // a Verse sung twice taken for the chorus over a Chorus and a FinalChorus sung once each.
    [Fact]
    public void APianoThatOnlyHolds_IsSaid()
    {
        string Song(string right, string left) => "title \"P\"\ntime 4/4\noctave absolute\npart sop { instrument soprano }\n"
            + "part rh { instrument piano-right }\npart lh { instrument piano-left }\nsection A {\n  sop { " + Bars(Sop) + "}\n  rh { " + Bars(right) + "}\n  lh { " + Bars(left) + "}\n"
            + "  lyrics w sings sop { " + string.Concat(Enumerable.Repeat("la la la la | ", 8)) + "}\n}\nform { A }\nscore { staff sop  lyrics w  grandStaff { staff rh  staff lh } }\n";
        Assert.Contains(CompositionChecks.Of(Song("<e' g' c''>2 <e' g' c''>2", "c1")), f => f.StartsWith("rh, lh: in 8 of its 8 bars the piano only holds"));
        Assert.DoesNotContain(CompositionChecks.Of(Song("e'8 g' c'' g' e' g' c'' g'", "c1")), f => f.Contains("only holds"));
    }

    // Owner's review 2026-11-01 (Ameagari-no-Cafe): a guitar's thumb under its fingers' chords was read
    // as the tune, told "98% half notes" and "a chord reaches over the tune", and a tune's note between
    // the chords told it leapt from their low strings; each answer took the piece's tune away.
    [Fact]
    public void AThumbUnderTheFingersChords_IsNotTheTune()
    {
        string Solo(string upper, string thumb) => "title \"Solo\"\ntime 4/4\noctave absolute\npart g { instrument guitar }\nsection A {\n  g { voice { "
            + Bars(upper) + "} { " + Bars(thumb) + "} }\n}\nform { A }\nscore { staff g }\n";
        var thumb = CompositionChecks.Of(Solo("<g c' e'>4. <g c' e'>4. a'4", "a,2 e,2"));
        Assert.DoesNotContain(thumb, f => f.Contains("half notes"));
        Assert.DoesNotContain(thumb, f => f.Contains("reaches over the tune"));
        Assert.DoesNotContain(thumb, f => f.Contains("leaps more than an octave"));
        // A tune's note over chords with a low bass in them is no slip either…
        Assert.DoesNotContain(CompositionChecks.Of(Solo("<d a d' f'>4. <d a d' f'>4. bes'4", "bes,2 f,2")), f => f.Contains("leaps more than an octave"));
        // …and a single note far above the chord's top is one.
        Assert.Contains(CompositionChecks.Of(Solo("<d a d'>4. <d a d'>4. a''4", "bes,2 f,2")), f => f.Contains("leaps more than an octave"));
    }

    // Owner's review 2026-11-01 (Kissaten-no-Yugure): a solo guitar's G♯4 under its own Emaj7 topped by D♯5.
    [Fact]
    public void ATuneUnderItsOwnChords_IsSaid()
    {
        string Solo(string upper) => "title \"Solo\"\ntime 4/4\noctave absolute\npart g { instrument guitar }\nsection A {\n  g { voice { "
            + Bars(upper) + "} { " + Bars("e,4 r8 <e g' b' d''>8 b,4 r8 <e g' b' d''>8") + "} }\n}\nform { A }\nscore { staff g }\n";
        Assert.Contains(CompositionChecks.Of(Solo("r4 g'8 r8 r4 f'8 r8")), f => f.StartsWith("g: in 8 of its 8 bars with a tune and chords, a chord reaches over the tune"));
        Assert.DoesNotContain(CompositionChecks.Of(Solo("r4 e''8 r8 r4 f''8 r8")), f => f.Contains("reaches over the tune"));
    }

    [Fact]
    public void ASectionNamedTheChorus_IsTheChorus_ThoughTheVerseIsSungMore()
    {
        string song = Hook(Q + Q + P + Q, X + X + X + X)
            .Replace("form { Verse Chorus Verse Chorus }", "form { Verse Chorus Verse }");
        var findings = CompositionChecks.Of(song);
        Assert.DoesNotContain(findings, f => f.Contains("the chorus (Verse)"));
        Assert.Contains(findings, f => f.Contains("the chorus (Chorus) stays below"));
        // A FinalChorus opens as the Chorus does by right.
        string last = Hook(Q + Q + Q + Q, R + R + R + R).Replace("form { Verse Chorus Verse Chorus }", "form { Verse Chorus Verse FinalChorus }")
            .Replace("form {", "section FinalChorus {\n  singer { " + R + R + R + "a'4 g'4 f'2 | e'1 | }\n}\nform {");
        Assert.DoesNotContain(CompositionChecks.Of(last), f => f.Contains("opens with"));
    }

    [Fact]
    public void VoicesSingingBarsWithNoWords_AreSaid()
    {
        const string intro = "section I {\n  sop { c'1 | d'1 | }\n  alt { c'1 | b1 | }\n  men { c1 | g,1 | }\n}\n";
        Assert.Contains(CompositionChecks.Of(Choir(Bars(Sop), Bars("e'4 f' g' e'"), Bars("c4 d c g,"), intro)),
            f => f.StartsWith("The voices sing 2 bars with no words (I)"));
        // A second verse written `verse 2 { }` has its words.
        string verses = Choir(Bars(Sop), Bars("e'4 f' g' e'"), Bars("c4 d c g,")).Replace("form { A }", "form { A A \"A2\" }")
            .Replace("lyrics w sings sop { ", "lyrics w sings sop { verse 1 { ").Replace("}\n}\nform", "} verse 2 { " + string.Concat(Enumerable.Repeat("do do do do | ", 8)) + "} }\n}\nform");
        Assert.DoesNotContain(CompositionChecks.Of(verses), f => f.Contains("with no words"));
        // Words in the soprano's track are the words all three sing.
        Assert.DoesNotContain(CompositionChecks.Of(Choir(Bars(Sop), Bars("e'4 f' g' e'"), Bars("c4 d c g,"))), f => f.Contains("with no words"));
    }

    [Fact]
    public void AChorusSungThreeTimes_TheLastAsTheFirst_IsSaid()
    {
        string twice = Hook(Q + Q + Q + X, R + R + R + R);
        Assert.DoesNotContain(CompositionChecks.Of(twice), f => f.Contains("the last as the first"));
        Assert.Contains(CompositionChecks.Of(twice.Replace("form { Verse Chorus Verse Chorus }", "form { Verse Chorus Verse Chorus Chorus }")),
            f => f.StartsWith("singer, the melody: the chorus (Chorus) is sung 3 times, the last as the first"));
    }

    [Fact]
    public void AShortSong_IsToldItsLength_ThoughItIsOnePart()
    {
        // 16 bars at ♩ = 84: 46 seconds. Sung, it is told; a one-part instrumental head is not.
        string song = Hook(Q + Q + Q + X, R + R + R + R).Replace("time 4/4", "tempo 84\ntime 4/4").Replace("form { Verse Chorus Verse Chorus }", "form { Verse Chorus }");
        Assert.Contains(CompositionChecks.Of(song), f => f.StartsWith("The piece is 16 bars long, about 46 seconds played"));
        Assert.DoesNotContain(CompositionChecks.Of(song.Replace("  lyrics w sings singer { la la | }\n", "").Replace("  lyrics w", "")),
            f => f.StartsWith("The piece is"));
    }

    // Owner's reviews 2026-11-01 (Shippu-no-Kodou, Yoake-o-Kakenukete): a chorus tune in the first voice
    // over the guitar's own power chords in the second was told "a tune in 0 of the 76 bars" and
    // "only 19% of its moves are steps" — the two voices read as one line.
    [Fact]
    public void ATuneOverItsOwnPartsChords_IsATune_AndIsReadAlone()
    {
        var two = CompositionChecks.Of(Rock3("voice { e'4 fis'8 g' a'4 g' } { <e b>8 r <e b> r <e b>8 r <e b> r }", "e,8 e, e, e, e, e, e, e,"));
        Assert.DoesNotContain(two, f => f.StartsWith("A tune is played in only"));
        Assert.DoesNotContain(two, f => f.Contains("of its moves are steps"));
        // …but one player cannot strike the riff under their own hook (owner's "入れて" 2026-11-01).
        Assert.Contains(two, f => f.StartsWith("gtr: in 24 of its 24 bars with a tune, one voice strikes the chords while another plays the tune — one player's hands cannot"));
        // A chord struck once where the tune rests is fine.
        Assert.DoesNotContain(CompositionChecks.Of(Rock3("voice { r4 fis'8 g' a'4 g' } { <e b>4 r2. }", "e,8 e, e, e, e, e, e, e,")),
            f => f.Contains("one player's hands cannot"));
        // A chord struck where the tune rests, then the tune (what the "no tune" finding asks for),
        // is a bar of the tune (owner's review 2026-11-01: `<g, d>4 b'4 d''2` was no tune bar).
        var struck = CompositionChecks.Of(Rock3("<e b>4 g'4 a'2", "e,8 e, e, e, e, e, e, e,"));
        Assert.DoesNotContain(struck, f => f.StartsWith("A tune is played in only"));
        Assert.DoesNotContain(struck, f => f.Contains("one player's hands cannot"));
        // Chords alone in both voices: still no tune.
        Assert.Contains(CompositionChecks.Of(Rock3("voice { <e' g'>4 <e' g'> <e' g'> <e' g'> } { <e b>8 r <e b> r <e b>8 r <e b> r }", "e,8 e, e, e, e, e, e, e,")),
            f => f.StartsWith("A tune is played in only"));
    }

    [Fact]
    public void ATuneOverItsOwnPartsChords_IsNotToldOfOctaveSlips()
    {
        // The chord's E♭3 on 1 and 3, the tune's G4 on 2 — one line would read a slip at every G4.
        var two = CompositionChecks.Of(Rock3("voice { r4 g' r f' } { <ees bes>4 r <ees bes> r }", "e,8 e, e, e, e, e, e, e,"));
        Assert.DoesNotContain(two, f => f.Contains("leaps more than an octave away and straight back"));
        var midi = new LilySharp.Core.Midi.MidiExporter().Export(LilySharp.Core.Syntax.SyntaxTree.Parse(Rock3("voice { r4 g' r f' } { <ees bes>4 r <ees bes> r }", "e,8 e, e, e, e, e, e, e,")));
        Assert.Contains(midi.Tracks.SelectMany(t => t.Notes), n => n.Pitch == 51 && n.Voice == 1);
        Assert.Contains(midi.Tracks.SelectMany(t => t.Notes), n => n.Pitch == 67 && n.Voice == 0);
        // One line that does slip is still told.
        Assert.Contains(CompositionChecks.Of(Rock3("e'4 g' a'2", "e,8 e, cis''8 e, e, e, e, e,")), f => f.StartsWith("bass: ") && f.Contains("leaps more than an octave away and straight back"));
    }

    // Owner's request 2026-10-30: four findings for a piece's sound — a wrong note held against the
    // harmony, low close chords and a part in the bass's register, one groove in every section, and
    // sections whose tune ends open.
    private static string Rock3(string guitar, string bass, string? lastGuitar = null, string? lastBass = null,
        (string Hands, string Feet)[]? kits = null)
    {
        kits ??= [("hh8 hh hh hh hh hh hh hh", "bd4 sn bd sn"), ("hh8 hh hh hh hh hh hh hh", "bd4 sn bd sn"), ("hh8 hh hh hh hh hh hh hh", "bd4 sn bd sn")];
        string Bars(string bar, int n) => string.Concat(Enumerable.Repeat(bar + " | ", n));
        var names = kits.Select((_, i) => ((char)('A' + i)).ToString()).ToList();
        string sections = string.Concat(kits.Select((k, i) => $"section {names[i]} {{\n  gtr {{ {Bars(guitar, 3)}{lastGuitar ?? guitar} | }}\n"
            + $"  bass {{ {Bars(bass, 3)}{lastBass ?? bass} | }}\n  kit {{ voice {{ {Bars(k.Hands, 4)} }} {{ {Bars(k.Feet, 4)} }} }}\n}}\n"));
        return $"title \"Riff\"\ntempo 168\ntime 4/4\noctave absolute\n{Guitar}\n{Bass}\n{Drums}\n" + sections
            + $"form {{ {string.Join(" ", names)} {string.Join(" ", names)} }}\nscore {{ staff gtr  staff bass  staff kit }}\n";
    }

    [Fact]
    public void AMelodyHeldASemitoneFromItsHarmony_IsSaid()
    {
        Assert.Contains(CompositionChecks.Of(Rock3("f'4 g' a'2", "fis,8 fis, fis, fis, fis, fis, fis, fis,")),
            f => f.StartsWith("gtr, the melody: ") && f.Contains("times a note held on the beat stands a semitone from the harmony")
                && f.Contains("G4 against F♯1 first"));
        Assert.DoesNotContain(CompositionChecks.Of(Rock3("f'4 g' a'2", "d,8 d, d, d, d, d, d, d,")), f => f.Contains("a semitone from the harmony"));
    }

    // Owner's trial 2026-11-07: a swinging jazz blues's blue notes and its walking bass's chromatic
    // approaches were told wrong notes. A piece written to swing speaks that language; the same notes in
    // a piece that does not swing are told as before — the swing marking is the only difference here.
    private static string InC(string book, bool swing) => book.Replace("tempo 168", swing ? "tempo 168 swing\nkey c major" : "tempo 168\nkey c major");

    [Fact]
    public void ABlueNoteHeldInASwingingPiece_IsNotAWrongNote()
    {
        // E♭ — C's minor third — held over a D bass.
        string book = Rock3("ees'4 ees' ees'2", "d,8 d, d, d, d, d, d, d,");
        Assert.Contains(CompositionChecks.Of(InC(book, swing: false)), f => f.Contains("a semitone from the harmony"));
        Assert.DoesNotContain(CompositionChecks.Of(InC(book, swing: true)), f => f.Contains("a semitone from the harmony"));
    }

    [Fact]
    public void AWalkingBasssChromaticApproach_IsNotTheHarmony_InASwingingPiece()
    {
        // The bass's A♭ on beat 4 leads by a semitone into the next bar's G; the tune's A over it is no wrong note.
        string book = Rock3("g'4 b' d'' a'", "g,4 a, b, aes,");
        Assert.Contains(CompositionChecks.Of(InC(book, swing: false)), f => f.Contains("a semitone from the harmony"));
        Assert.DoesNotContain(CompositionChecks.Of(InC(book, swing: true)), f => f.Contains("a semitone from the harmony"));
        // A bass note that stays — a half note, though it too moves on by a semitone — is the harmony, swing or not.
        Assert.Contains(CompositionChecks.Of(InC(Rock3("g'4 b' a'2", "g,4 a, aes,2"), swing: true)), f => f.Contains("a semitone from the harmony"));
    }

    [Fact]
    public void LowCloseChords_AndAPartInTheBasssRegister_AreSaid()
    {
        // The bass sounds an octave below its written note: `e` is E2, in the guitar's own register.
        var low = CompositionChecks.Of(Rock3("<e, gis, b,>2 <e, gis, b,>2", "e8 e e e e e e e"));
        Assert.Contains(low, f => f.StartsWith("gtr: ") && f.Contains("chords stack a third or a second below C3 (E2 A♭2 B2 in bar 1 first"));
        Assert.Contains(low, f => f.StartsWith("gtr: most of its time round ") && f.Contains("within an octave of the bass's"));
        var open = CompositionChecks.Of(Rock3("<e b e' gis'>2 <e b e' gis'>2", "e,8 e, e, e, e, e, e, e,"));
        Assert.DoesNotContain(open, f => f.Contains("below C3") || f.Contains("within an octave of the bass's"));
    }

    [Fact]
    public void ABandsDrumsInOneGrooveThroughEverySection_AreSaid()
    {
        Assert.Contains(CompositionChecks.Of(Rock3("e'4 g' a'2", "e,8 e, e, e, e, e, e, e,")),
            f => f.StartsWith("kit: the same groove in every section (A, B, C)"));
        // The cymbals changing with the section, the kick and the snare the same: said of four sections or more.
        const string hats = "hh8 hh hh hh hh hh hh hh", ride = "cymr8 cymr cymr cymr cymr cymr cymr cymr", beat = "bd4 sn bd sn";
        var feet = CompositionChecks.Of(Rock3("e'4 g' a'2", "e,8 e, e, e, e, e, e, e,", kits: [(hats, beat), (ride, beat), (hats, beat), (ride, beat)]));
        Assert.DoesNotContain(feet, f => f.StartsWith("kit: the same groove"));
        Assert.Contains(feet, f => f.StartsWith("kit: the kick and the snare play one beat in every section (A, B, C, D)"));
        // A half-time section changes the beat itself.
        Assert.DoesNotContain(CompositionChecks.Of(Rock3("e'4 g' a'2", "e,8 e, e, e, e, e, e, e,",
            kits: [(hats, beat), (ride, beat), (hats, "bd4 r sn r"), (ride, beat)])), f => f.StartsWith("kit: the"));
    }

    // Owner's trial 2026-10-30: a rock guitar's verse moved one "up two and back" figure along the
    // chords bar after bar. A tune whose phrase runs over bars — Ode to Joy — is not one.
    [Fact]
    public void AMelodyOfOneBarsShapeMovedBarAfterBar_IsSaid()
    {
        const string bass = "g,8 g, g, g, g, g, g, g,";
        Assert.Contains(CompositionChecks.Of(Rock3("g'8 a' bes'4 a'8 g' f'4", bass)),
            f => f.StartsWith("gtr, the melody: ") && f.Contains("bars move in the shape of the bar before (↗↗↘↘↘)"));
        string ode = "e''4 e'' f'' g'' | g'' f'' e'' d'' | c'' c'' d'' e'' | e''4. d''8 d''2 | e''4 e'' f'' g'' | g'' f'' e'' d'' | "
            + "c'' c'' d'' e'' | d''4. c''8 c''2 | d''4 d'' e'' c'' | d'' e''8 f'' e''4 c'' | d'' e''8 f'' e''4 d'' | c'' d'' g'2 |";
        string band = "title \"Ode\"\ntempo 120\ntime 4/4\noctave absolute\n" + Guitar + "\n" + Bass + "\n" + Drums + "\n"
            + $"section A {{\n  gtr {{ {ode} }}\n  bass {{ {string.Concat(Enumerable.Repeat("c,2 g, | ", 12))} }}\n"
            + $"  kit {{ voice {{ {string.Concat(Enumerable.Repeat("hh8 hh hh hh hh hh hh hh | ", 12))} }} {{ {string.Concat(Enumerable.Repeat("bd4 sn bd sn | ", 12))} }} }}\n}}\n"
            + "form { A }\nscore { staff gtr  staff bass  staff kit }\n";
        Assert.DoesNotContain(CompositionChecks.Of(band), f => f.Contains("shape of the bar before"));
    }

    // Owner's trial 2026-10-30: the bass and drums held their opening @mf through every chorus the
    // guitar played @f.
    [Fact]
    public void ABandsRhythmSectionAtOneLevelWhileTheLeadMoves_IsSaid()
    {
        string Piece(string bassB) => "title \"Riff\"\ntempo 168\ntime 4/4\noctave absolute\n" + Guitar + "\n" + Bass + "\n" + Drums + "\n"
            + "section A {\n  gtr { <e, b,>4@mf e'8 g' a'4 g' | }\n  bass { e,8@mf e, e, e, e, e, e, e, | }\n"
            + "  kit { voice { hh8@mf hh hh hh hh hh hh hh | } { bd4 sn bd sn | } }\n}\n"
            + $"section B {{\n  gtr {{ <e, b,>4@f e'8 g' a'4 g' | }}\n  bass {{ e,8{bassB} e, e, e, e, e, e, e, | }}\n"
            + "  kit { voice { hh8 hh hh hh hh hh hh hh | } { bd4 sn bd sn | } }\n}\n"
            + "form { A B }\nscore { staff gtr  staff bass  staff kit }\n";
        Assert.Contains(CompositionChecks.Of(Piece("")),
            f => f.StartsWith("bass, kit: one level, @mf, from start to end, while gtr goes from @mf to @f"));
        Assert.Contains(CompositionChecks.Of(Piece("@f")), f => f.StartsWith("kit: one level, @mf"));
    }

    // Owner's trial 2026-10-30: told it had no tune, a rock trio's guitar played single notes from start
    // to end, the harmony the bass's root alone. A riff on the root, low, is the harmony too.
    [Fact]
    public void ABandWhoseGuitarPlaysNoChords_IsSaid()
    {
        const string bass = "e,8 e, e, e, e, e, e, e,";
        Assert.Contains(CompositionChecks.Of(Rock3("e'4 g' a'2", bass)), f => f.StartsWith("Nobody plays the chords: gtr plays a tune of single notes in 24 of its 24 bars"));
        Assert.DoesNotContain(CompositionChecks.Of(Rock3("<e b>2 <e b>2", bass)), f => f.StartsWith("Nobody plays the chords"));
        Assert.DoesNotContain(CompositionChecks.Of(Rock3("e8 r e g a r b a", bass)), f => f.StartsWith("Nobody plays the chords"));
    }

    // Owner's trial 2026-10-30: a bass in E♭ minor wrote `b` and `fis` for the key's C♭ and G♭.
    [Fact]
    public void NotesOfTheKeyWrittenUnderAnotherName_AreSaid()
    {
        string Piece(string bar) => "title \"Yoake\"\ntime 4/4\noctave absolute\nkey ees minor\npart b { instrument cello }\n"
            + $"section A {{\n  b {{ {string.Concat(Enumerable.Repeat(bar + " | ", 4))} }}\n}}\nform {{ A }}\nscore {{ staff b }}\n";
        Assert.Contains(CompositionChecks.Of(Piece("b,4 fis b, ees")),
            f => f.StartsWith("b: notes of the key written under another name, in E♭ minor — `b` for its `ces` (8 times), `fis` for its `ges` (4 times)"));
        Assert.DoesNotContain(CompositionChecks.Of(Piece("ces4 ges ces ees")), f => f.Contains("under another name"));
        // A note the key does not hold — the raised leading tone — is not one.
        Assert.DoesNotContain(CompositionChecks.Of(Piece("d4 d ees ees")), f => f.Contains("under another name"));
    }

    [Fact]
    public void SectionsWhoseTuneEndsOpenAgainstTheBass_AreSaid()
    {
        Assert.Contains(CompositionChecks.Of(Rock3("e'4 g' a'2", "e,8 e, e, e, e, e, e, e,", lastGuitar: "e'4 g' d''2", lastBass: "e,1")),
            f => f.StartsWith("gtr: 3 of 3 sections end their tune on a note open against the bass (A: D5 over E1, B: D5 over E1"));
        Assert.DoesNotContain(CompositionChecks.Of(Rock3("e'4 g' a'2", "e,8 e, e, e, e, e, e, e,", lastGuitar: "e'4 g' b'2", lastBass: "e,1")),
            f => f.Contains("end their tune on a note open"));
    }

    // Owner's trial 2026-10-30: told it had no tune, a rock guitar broke each bar's chord up and down
    // after it — a line counted, an accompaniment heard.
    [Fact]
    public void AMelodyOfItsChordsBrokenUpAndDown_IsSaid()
    {
        string Rock(string guitar) => Band(Guitar + "\n" + Bass + "\n" + Drums,
            $"  gtr {{ {string.Concat(Enumerable.Repeat(guitar, 4))} }}\n  bass {{ {string.Concat(Enumerable.Repeat("a,8 a, a, a, e e g, a, | f,8 f, f, f, c c e f, | ", 4))} }}\n"
            + $"  kit {{ voice {{ {string.Concat(Enumerable.Repeat("hh8 hh hh hh hh hh hh hh | ", 8))} }} {{ {string.Concat(Enumerable.Repeat("bd4 sn bd sn | ", 8))} }} }}",
            "staff gtr  staff bass  staff kit", "tempo 176");
        Assert.Contains(CompositionChecks.Of(Rock("<a, e>4 a8 c'8 e'4 c'8 a8 | <f, c>4 f8 a8 c'4 a8 f8 | ")),
            f => f.StartsWith("gtr, the melody: only 0% of its moves are steps"));
        Assert.DoesNotContain(CompositionChecks.Of(Rock("e'8 g' b'4 a'8 g' fis'4 | g'4. a'8 b'2 | ")), f => f.Contains("of its moves are steps"));
    }

    // Owner's trial 2026-10-30: a rock trio's bass jumped E1–E2–E1–B2–E2–D3 eighth by eighth.
    [Fact]
    public void ABandBassLeapingOctaves_IsSaid()
    {
        string Rock(string bass) => Band(Guitar + "\n" + Bass + "\n" + Drums,
            $"  gtr {{ {string.Concat(Enumerable.Repeat("e'8 g' b'4 a'8 g' fis'4 | g'4. a'8 b'2 | ", 4))} }}\n  bass {{ {string.Concat(Enumerable.Repeat(bass, 8))} }}\n"
            + $"  kit {{ voice {{ {string.Concat(Enumerable.Repeat("hh8 hh hh hh hh hh hh hh | ", 8))} }} {{ {string.Concat(Enumerable.Repeat("bd4 sn bd sn | ", 8))} }} }}",
            "staff gtr  staff bass  staff kit", "tempo 176");
        Assert.Contains(CompositionChecks.Of(Rock("e,,8 e, e,, b, e, d b, e,, | ")), f => f.StartsWith("bass: 25% of its steps leap more than an octave"));
        Assert.DoesNotContain(CompositionChecks.Of(Rock("e,8 e, e, e e, e, b, e, | ")), f => f.Contains("leap more than an octave"));
    }

    // Owner's trial 2026-10-30: a rock bass ended its A and B bars `… e cis'' | b …`, a C♯4 between
    // E2 and B2 — an octave mark slipped. A pedal riff leaping one note after another is not one.
    [Fact]
    public void ANoteOutOfItsLineAndStraightBack_IsAskedAbout()
    {
        string Rock(string bass) => Band(Guitar + "\n" + Bass + "\n" + Drums,
            $"  gtr {{ {string.Concat(Enumerable.Repeat("e'8 g' b'4 a'8 g' fis'4 | g'4. a'8 b'2 | ", 4))} }}\n  bass {{ {string.Concat(Enumerable.Repeat(bass, 4))} }}\n"
            + $"  kit {{ voice {{ {string.Concat(Enumerable.Repeat("hh8 hh hh hh hh hh hh hh | ", 8))} }} {{ {string.Concat(Enumerable.Repeat("bd4 sn bd sn | ", 8))} }} }}",
            "staff gtr  staff bass  staff kit", "tempo 176");
        Assert.Contains(CompositionChecks.Of(Rock("e,8 e, b, e, e, e, b, dis, | a,8 a, e, a, a, a, e, cis' | ")),
            f => f.StartsWith("bass: 3 times one note leaps more than an octave away and straight back (bars 2, 4, 6, as the form plays them; "));
        Assert.DoesNotContain(CompositionChecks.Of(Rock("e,8 e, b, e, e, e, b, dis, | a,8 a, e, a, a, a, e, cis | ")), f => f.Contains("straight back"));
        Assert.DoesNotContain(CompositionChecks.Of(Rock("e,8 d' e, b e, d' e, b | a,8 e' a, cis' a, e' a, cis' | ")), f => f.Contains("straight back"));
        // A chord struck under the part's own tune is not one (owner's trial 2026-10-30).
        string chorus = Band(Guitar + "\n" + Bass + "\n" + Drums,
            $"  gtr {{ {string.Concat(Enumerable.Repeat("e''8 d'' <d a>2 fis''8 e'' | ", 8))} }}\n  bass {{ {string.Concat(Enumerable.Repeat("d,8 d, d, d, d, d, d, d, | ", 8))} }}\n"
            + $"  kit {{ voice {{ {string.Concat(Enumerable.Repeat("hh8 hh hh hh hh hh hh hh | ", 8))} }} {{ {string.Concat(Enumerable.Repeat("bd4 sn bd sn | ", 8))} }} }}",
            "staff gtr  staff bass  staff kit", "tempo 176");
        Assert.DoesNotContain(CompositionChecks.Of(chorus), f => f.Contains("straight back"));
        Assert.Contains(CompositionChecks.Of(chorus.Replace("<d a>2", "d2")), f => f.StartsWith("gtr: 8 times one note leaps"));
    }

    // Owner's trial 2026-10-30: a rock instrumental played Verse Chorus three times, the same each
    // time, its guitar and bass with no level before the chorus's last bar.
    [Fact]
    public void AnInstrumentalsSectionsThriceOver_AndLevelsBegunLate_AreSaid()
    {
        string Section(string name, string gtr, string bass) => $"section {name} {{\n  gtr {{ {gtr} }}\n  bass {{ {bass} }}\n"
            + "  kit { voice { hh8 hh hh hh hh hh hh hh | hh8 hh hh hh hh hh hh hh | } { bd4 sn bd sn | bd4 sn bd sn | } }\n}\n";
        string Piece(string form, string verseLevel) => $"title \"Run\"\ntempo 176\ntime 4/4\noctave absolute\n{Guitar}\n{Bass}\n{Drums}\n"
            + Section("Verse", $"<e b>8{verseLevel} r <e b> <e b> <e b> r <e b> <e b> | <c' g>8 r <c' g> <c' g> <c' g> r <c' g> <c' g> |",
                $"e,8{verseLevel} e, e, b, e, e, e, b, | c8 c c g c c c g |")
            + Section("Chorus", "b'4 b'8 a' fis'4 e' | d''4 cis''8 b' a'2@f |", "g,8 g, g, d g, g, g, d | d8 d d a d d d a@f |")
            + $"form {{ {form} }}\nscore {{ staff gtr staff bass staff kit }}\n";
        var thrice = CompositionChecks.Of(Piece("Verse Chorus Verse Chorus Verse Chorus", ""));
        Assert.Single(thrice, f => f.StartsWith("Verse (3 times), Chorus (3 times): played the same each time"));
        Assert.Contains(thrice, f => f.StartsWith("gtr, bass: no level until Chorus — "));
        var twice = CompositionChecks.Of(Piece("Verse Chorus Verse Chorus", "@mf"));
        Assert.DoesNotContain(twice, f => f.Contains("played the same each time"));
        Assert.DoesNotContain(twice, f => f.Contains("no level until"));
        // Sung, the verses change their words.
        Assert.DoesNotContain(CompositionChecks.Of(Piece("Verse Chorus Verse Chorus Verse Chorus", "").Replace("form {", "lyrics words sings gtr { la }\nform {")),
            f => f.Contains("played the same each time"));
    }

    private const string HymnVoices = "part sop { instrument soprano }\npart alt { instrument alto }\npart ten { instrument tenor }\npart bas { instrument voice-bass }";

    private static string Hymn(string sop, string alt, string ten, string bas, string score = "choirStaff { staff sop  staff alt  staff ten  staff bas }", string voices = HymnVoices)
        => $"title \"Morning\"\ntime 4/4\noctave absolute\nkey g major\n{voices}\n"
            + $"section A {{\n  sop {{ {sop} }}\n  alt {{ {alt} }}\n  ten {{ {ten} }}\n  bas {{ {bas} }}\n}}\nform {{ A }}\nscore {{ {score} }}\n";

    private static string Eight(string bar) => string.Concat(Enumerable.Repeat(bar + " | ", 8));

    // Owner's review 2026-11-02: the guide's own hymn, its upper voices in quarters together over a bass
    // in halves, heard its alto and tenor told to find "a rhythm of their own".
    [Fact]
    public void AChoirsInnerVoices_MovingWithTheTop_AreLeftAlone()
    {
        string Book(string voices) => Hymn(Eight("g'4 a' b' a'"), Eight("d'4 e' d' e'"), Eight("b4 c' d' c'"), Eight("g,2 d2"), voices: voices)
            .Replace("  alt {", $"  lyrics words sings sop {{ {string.Concat(Enumerable.Repeat("la la la la | ", 8))} }}\n  alt {{").Replace("staff bas }", "staff bas }  lyrics words");
        var hymn = CompositionChecks.Of(Book(HymnVoices));
        Assert.Contains(hymn, f => f.StartsWith("sop, the melody: 100% of its notes are quarter notes"));
        Assert.DoesNotContain(hymn, f => f.StartsWith("alt:") || f.StartsWith("ten:"));
        // Not one rhythm: "every part" stays for four parts all alike.
        Assert.DoesNotContain(hymn, f => f.Contains("every part moves") || f.Contains("all move in one rhythm"));
        // Strings in the same notes are not a choir: told as ever.
        var strings = CompositionChecks.Of(Book("part sop { instrument violin }\npart alt { instrument violin }\npart ten { instrument viola }\npart bas { instrument cello }"));
        Assert.Contains(strings, f => f.StartsWith("alt:") || f.StartsWith("ten:"));
    }

    // Owner's review 2026-11-02: a hymn with "a quiet, prayerful cadence" and no level anywhere, every note
    // at the default, was told nothing: the check read only a part silent beside a part with levels.
    [Fact]
    public void NoLevelAnywhere_IsSaid_FromTwoParts()
    {
        static bool Told(string book) => CompositionChecks.Findings(book).Any(f => f.Id == "dynamics.none");
        string duet = Hymn("g'1 |", "d'1 |", "b1 |", "g,1 |");
        Assert.True(Told(duet));
        Assert.False(Told(duet.Replace("g,1", "g,1@p")));
        // One part alone: a head or a transcription.
        Assert.False(Told("title \"One\"\ntime 4/4\noctave absolute\npart b { instrument bass }\nsection A {\n  b { c1 | }\n}\nform { A }\nscore { staff b }\n"));
        // A level in a phrase is a level.
        Assert.False(Told(duet.Replace("section A", "phrase soft { g,1@p }\nsection A").Replace("g,1 |", "soft |")));
    }

    // Owner's review 2026-11-02: an eight-bar hymn had six parallel fifths and octaves; `check voiceLeading`
    // finds them, and no piece the chat composes turns it on.
    [Fact]
    public void AChoirsParallels_AreSaid_WithNoCheckLine()
    {
        static List<CompositionFinding> Told(string book) => CompositionChecks.Findings(book).Where(f => f.Id == "singers.parallels").ToList();
        // G4/G2 to E4/E2 between soprano and bass in bar 2 (the alto and tenor hold).
        string octaves = Hymn("g'1 | e'1 | e'1 |", "d'1 | d'1 | d'1 |", "b1 | b1 | b1 |", "g,1 | e,1 | e,1 |");
        var told = Assert.Single(Told(octaves));
        Assert.Contains("sop and bas in octaves, bar 2", told.Message);
        // Owner's review 2026-11-02: told "or hold a common tone", a hymn's bass went to one whole note a bar.
        Assert.Contains("Move one of them the other way, note for note: a voice held long to get out of them is no line", told.Message);
        Assert.DoesNotContain("hold a common tone", told.Message);
        Assert.Equal([2], told.Bars);
        Assert.Equal(["sop", "alt", "ten", "bas"], told.Parts);
        // Contrary motion: nothing.
        Assert.Empty(Told(Hymn("g'1 | a'1 | a'1 |", "d'1 | d'1 | d'1 |", "b1 | b1 | b1 |", "g,1 | e,1 | e,1 |")));
        // Fifths, named as fifths: D4/G3 to C4/F3 between alto and tenor.
        Assert.Contains("alt and ten in fifths, bar 2", Assert.Single(Told(Hymn("b'1 | a'1 | a'1 |", "d'1 | c'1 | c'1 |", "g1 | f1 | f1 |", "g,1 | f,1 | f,1 |")
            .Replace("bas { g,1 | f,1 | f,1 | }", "bas { b,1 | a,1 | a,1 | }"))).Message);
        // Not in a choirStaff, or fewer than three voices: not said.
        Assert.Empty(Told(octaves.Replace("choirStaff { staff sop  staff alt  staff ten  staff bas }", "staff sop  staff alt  staff ten  staff bas")));
        Assert.Empty(Told(octaves.Replace("choirStaff {", "staffGroup {")));
        string two = "title \"Two\"\ntime 4/4\noctave absolute\npart sop { instrument soprano }\npart bas { instrument voice-bass }\n"
            + "section A {\n  sop { g'1 | e'1 | }\n  bas { g,1 | e,1 | }\n}\nform { A }\nscore { choirStaff { staff sop  staff bas } }\n";
        Assert.Empty(Told(two));
        // Strings in a choirStaff are no choir.
        Assert.Empty(Told(octaves.Replace("instrument soprano", "instrument violin").Replace("instrument alto", "instrument violin").Replace("instrument tenor", "instrument viola").Replace("instrument voice-bass", "instrument cello")));
        // A section the form plays twice is one set of parallels, as first played: bar 2, not bar 5. The
        // new one is the turn from its last bar to its first, E to G, bar 4.
        var twice = Assert.Single(Told(octaves.Replace("form { A }", "form { A A }")));
        Assert.Equal([2, 4], twice.Bars);
    }

    // The guide says what a line opening on a weak syllable does.
    [Fact]
    public void TheGuide_PutsAnIambOnAnUpbeat()
    {
        var guide = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "COMPOSE_FOR_LLM.md")).Replace("\r\n", "\n");
        Assert.Contains("a line that opens on a weak syllable (*O GOD of\n    LIGHT*) opens on an upbeat, with `partial`", guide);
        Assert.Contains("and sings the words with the others, not\n  one long note a bar under them", guide);
    }

    // Owner's review 2026-11-02: 雨の窓辺, told it was 75% quarter notes and 22% steps, kept both as the
    // rain's and came back at 75% and 17%: a tune in one value up and down its chords is no tune.
    [Fact]
    public void AMelodyInOneValueAndInLeaps_IsNoTune()
    {
        static string Piano(string rh) => $"title \"Window\"\ntime 4/4\noctave absolute\n{Hands}\n"
            + $"section A {{\n  rh {{ {rh} }}\n  lh {{ {string.Concat(Enumerable.Repeat("c1 | ", 8))} }}\n}}\nform {{ A }}\nscore {{ grandStaff {{ staff rh  staff lh }} }}\n";
        static string Bars(string bar) => string.Concat(Enumerable.Repeat(bar + " | ", 8));
        static List<string> NoTune(string book) => CompositionChecks.Findings(book).Where(f => f.Id == "melody.no-tune").Select(f => f.Message).ToList();
        Assert.StartsWith("rh, the melody: no tune — 100% of its notes are quarter notes and only 0% of its moves are steps", Assert.Single(NoTune(Piano(Bars("c''4 e'' g'' e''")))));
        // Steps in one value, or chord tones in many: one point each, not this one.
        Assert.Empty(NoTune(Piano(Bars("c''4 d'' e'' d''"))));
        Assert.Empty(NoTune(Piano(Bars("c''4. e''8 g''2"))));
        // Both are still said, and what the review calls it has its note.
        var findings = CompositionChecks.Findings(Piano(Bars("c''4 e'' g'' e''")));
        Assert.Contains(findings, f => f.Id == "rhythm.one-value");
        Assert.Contains(findings, f => f.Id == "melody.leaps");
        // A bass's line is its own: a part named as one is not told.
        string alone = "title \"Walk\"\ntime 4/4\noctave absolute\npart b { instrument bass }\n"
            + $"section A {{\n  b {{ {Bars("c4 e g e")} }}\n  lyrics words sings b {{ {string.Concat(Enumerable.Repeat("la la la la | ", 8))} }}\n}}\nform {{ A }}\nscore {{ staff b  lyrics words }}\n";
        Assert.Contains(CompositionChecks.Findings(alone), f => f.Id == "melody.leaps");
        Assert.Empty(NoTune(alone));
    }

    // Owner's review 2026-11-02: 雨音の午後's left hand alone in bar 1, the first level the right hand's
    // @pp in bar 2. Bar 1 played at the default, louder than the piece's @mf; the section had a level.
    [Fact]
    public void NotesBeforeTheFirstLevel_AreSaid()
    {
        static string Piano(string rh, string lh) => $"title \"First\"\ntime 4/4\noctave absolute\n{Hands}\n"
            + $"section A {{\n  rh {{ {rh} }}\n  lh {{ {lh} }}\n}}\nform {{ A }}\nscore {{ grandStaff {{ staff rh  staff lh }} }}\n";
        static string Flute(string fl) => $"title \"First\"\ntime 4/4\noctave absolute\npart fl {{ instrument flute }}\nsection A {{\n  fl {{ {fl} }}\n}}\nform {{ A }}\nscore {{ staff fl }}\n";
        static List<CompositionFinding> Late(string book) => CompositionChecks.Findings(book).Where(f => f.Id == "dynamics.late").ToList();

        var left = Assert.Single(Late(Piano("r1 | g'1@pp | g'1 |", "c1 | c1 | c1 |")));
        Assert.StartsWith("lh: the notes before the first level (bar 2) have none, and play between @mf and @f", left.Message);
        Assert.Equal(["lh"], left.Parts);
        Assert.Equal([1], left.Bars);
        // Within the bar: the notes before it are named, and the bars.
        Assert.StartsWith("fl: the notes before the first level (bar 2)", Assert.Single(Late(Flute("c''1 | d''2 e''2@p | c''1 |"))).Message);
        // Both hands sounding before it are both named.
        Assert.Equal(["rh", "lh"], Assert.Single(Late(Piano("g'1 | g'1@p | g'1 |", "c1 | c1 | c1 |"))).Parts.OrderByDescending(p => p).ToList());
        // Two staves of one grandStaff with no instrument are the same two hands.
        Assert.Single(Late(Piano("g'1 | g'1@p | g'1 |", "c1 | c1 | c1 |").Replace("{ instrument piano-right }", "{ clef treble }").Replace("{ instrument piano-left }", "{ clef bass }")));
        // A level on the first note, in either hand: nothing to say.
        Assert.Empty(Late(Piano("r1 | g'1@pp | g'1 |", "c1@pp | c1 | c1 |")));
        Assert.Empty(Late(Flute("c''1@p | d''1 | c''1 |")));
        // Rests before the first level: the player begins with it.
        Assert.Empty(Late(Piano("r1 | g'1@pp | g'1 |", "r1 | c1 | c1 |")));
        Assert.Empty(Late(Flute("r1 | d''1@p | c''1 |")));
        // No level at all is another finding.
        Assert.Empty(Late(Flute("c''1 | d''1 | c''1 |")));
        // A first section with none is told once, by its section.
        string twoSections = "title \"First\"\ntime 4/4\noctave absolute\npart fl { instrument flute }\nsection A {\n  fl { c''1 | d''1 | }\n}\nsection B {\n  fl { c''1@p | d''1 | }\n}\nform { A B }\nscore { staff fl }\n";
        Assert.StartsWith("fl: no level until B", Assert.Single(Late(twoSections)).Message);
    }

    // Owner's review 2026-11-02: 雨音の午後, drawn an introduction on its figure, was told of "the same
    // figure in every section" and took the figure out of its intro and coda.
    [Fact]
    public void TheOneFigureFinding_LetsAnIntroOrACodaKeepIt()
    {
        static string Book(string first) => $"title \"Figure\"\ntime 4/4\noctave absolute\n{Hands}\n"
            + string.Concat(new[] { first, "B", "C" }.Select(s => $"section {s} {{\n  rh {{ c''1@p | d''1 | e''1 | d''1 | }}\n  lh {{ c8 g c' g c g c' g | g,8 d g d g, d g d | c8 g c' g c g c' g | g,8 d g d g, d g d | }}\n}}\n"))
            + $"form {{ {first} B C }}\nscore {{ grandStaff {{ staff rh  staff lh }} }}\n";
        const string Keep = "An intro or a coda may keep the figure";
        Assert.Contains(CompositionChecks.Of(Book("Intro")), f => f.StartsWith("lh: the same figure in every section (Intro, B, C)") && f.EndsWith(Keep + ": the change belongs in the sections between them."));
        Assert.Contains(CompositionChecks.Of(Book("A")), f => f.StartsWith("lh: the same figure in every section (A, B, C)") && !f.Contains(Keep));
    }

    // Owner's review 2026-11-02: 雨音の午後's `form { ~Intro A B A2 Coda }`: the review's form began at A.
    // `~A` hides a label, and three readings of what the form plays did not see it.
    [Fact]
    public void ASectionWithItsLabelHidden_IsStillPlayed()
    {
        static string Piece(string form, string aEnd = "g''2@f") => "title \"Hidden\"\ntime 4/4\noctave absolute\nkey f major\n" + Hands
            + "\nsection A {\n  rh { a'4@p c''8 d''8 f''2 | a'2 d''4 f''4 | e''4 f''8 g''8 " + aEnd + " | }\n  lh { f,8 c a c' f, c a c' | d,8 a, f a d, a, f a | c,8 g, e g c, g, e g | }\n}\n"
            + "section B {\n  rh { a'4@p c''2. | }\n  lh { f,1 | }\n}\n"
            + $"form {{ {form} }}\nscore {{ grandStaff {{ staff rh  staff lh }} }}\n";
        Assert.Contains(CompositionChecks.Of(Piece("A B")), f => f.Contains("a drop of"));
        Assert.Contains(CompositionChecks.Of(Piece("~A ~B")), f => f.Contains("a drop of"));
        Assert.Contains("Form: A B\n", ScoreReview.Text(Piece("~A B")).Replace("\r\n", "\n"));
        // The keys it begins and ends in.
        static string Keys(string form) => "title \"Hidden\"\ntime 4/4\noctave absolute\n" + Hands
            + "\nsection A {\n  key d minor\n  rh { d'1@p | a'1 | d'1 | }\n  lh { d1 | a,1 | d1 | }\n}\n"
            + "section B {\n  key f major\n  rh { f'1 | c''1 | f'1 | }\n  lh { f1 | c1 | f1 | }\n}\n"
            + $"form {{ {form} }}\nscore {{ grandStaff {{ staff rh  staff lh }} }}\n";
        Assert.Contains(CompositionChecks.Of(Keys("~A ~B")), f => f.StartsWith("The piece begins in D minor and ends in F major"));
        // Read from the visible sections alone, `A ~B` both began and ended in D minor…
        Assert.Contains(CompositionChecks.Of(Keys("A ~B")), f => f.StartsWith("The piece begins in D minor and ends in F major"));
        // …and `~A B A`, the form going home, began in F major (2026-11-02: strings6, `~A B ~A Coda`).
        Assert.DoesNotContain(CompositionChecks.Of(Keys("~A B ~A")), f => f.StartsWith("The piece begins in"));
    }

    // Owner's review 2026-11-02: 雨粒の午後's four bars of coda took its `4. 8 8 8` under the shares of
    // both readings of the tune's rhythm. The sections that frame a piece are not its tune.
    [Fact]
    public void TheSectionsThatFrameAPiece_AreLeftOutOfTheTunesRhythm()
    {
        static string Bars(params string[] bars) => string.Concat(bars.Select(b => b + " | "));
        static string Duet(string body, string frame, string frameName) => $"title \"Frame\"\ntime 4/4\noctave absolute\nkey c major\n{Hands}\n"
            + $"section A {{\n  rh {{ {body} }}\n  lh {{ {Bars(Enumerable.Repeat("c1", body.Count(c => c == '|')).ToArray())} }}\n}}\n"
            + $"section {frameName} {{\n  rh {{ {frame} }}\n  lh {{ {Bars(Enumerable.Repeat("c1", frame.Count(c => c == '|')).ToArray())} }}\n}}\n"
            + $"form {{ A {frameName} }}\nscore {{ grandStaff {{ staff rh  staff lh }} }}\n";
        const string Plain = "g'1", Poured = "g'4 a'8 b'8 c''4 b'4";
        // 8 of the body's 16 bars in one rhythm, the rest each its own: a half, and 8 of 20 with four bars more.
        string body = Bars(Poured, "g'2 a'2", Poured, "g'4. a'8 b'2", Poured, "g'8 a'8 b'2.", Poured, "g'2. a'4",
            Poured, "g'4 a'2 b'4", Poured, "g'2 a'4 b'4", Poured, "g'4 a'4 b'2", Poured, "g'8 a'4. b'2");
        string frame = Bars(Plain, "a'2. g'4", "g'2.. a'8", Plain);
        foreach (var name in new[] { "Coda", "intro", "Outro2", "コーダ" })
            Assert.Contains(CompositionChecks.Of(Duet(body, frame, name)), f => f.StartsWith("rh, the melody: 8 of its 16 bars have the same rhythm"));
        Assert.DoesNotContain(CompositionChecks.Of(Duet(body, frame, "Fin")), f => f.Contains("of its 20 bars have the same rhythm"));
        // A frame in one rhythm is no section poured; another section is.
        string varied = Bars("g'2 a'2", "g'4. a'8 b'2", "g'8 a'8 b'2.", "g'2. a'4", "g'4 a'2 b'4", "g'2 a'4 b'4", "g'4 a'4 b'2", "g'8 a'4. b'2");
        string poured = Bars(Poured, Poured, Poured, Poured);
        Assert.DoesNotContain(CompositionChecks.Of(Duet(varied + varied, poured, "Coda")), f => f.Contains("have the same rhythm"));
        Assert.Contains(CompositionChecks.Of(Duet(varied + varied, poured, "Fin")), f => f.StartsWith("rh, the melody: in Fin 4 of its 4 bars have the same rhythm"));
        // Six four-bar phrases, 18 of 24 bars in one rhythm for their place; a seventh of the coda's takes it under.
        string[] place = ["g'4 a'4 b'4 c''4", "g'2 a'4 b'4", "g'4. a'8 b'2", "g'1"];
        const string Off = "g'8 a'8 b'8 c''8 b'2";
        string phrases = string.Concat(Enumerable.Range(0, 6).Select(i => Bars(place.Select((b, k) => k == i % 4 ? Off : b).ToArray())));
        string other = Bars("g'2. a'4", "g'2.. a'8", "g'8 a'4. b'2", "g'4 a'2 b'4");
        Assert.Contains(CompositionChecks.Of(Duet(phrases, other, "Coda")), f => f.StartsWith("rh, the melody: its 6 four-bar phrases keep one rhythm for each bar's place in the phrase (18 of 24 bars)"));
        Assert.DoesNotContain(CompositionChecks.Of(Duet(phrases, other, "Fin")), f => f.Contains("four-bar phrases keep"));
    }

    // Owner's review 2026-11-02: 雨粒の午後, 52 bars, was called 53: the last chord's fermata, longer
    // in the MIDI, ran past the closing bar line and read as a bar begun.
    [Fact]
    public void AFermataHeldPastTheClosingBarLine_IsNoBarMore()
    {
        static string Duo(string fl, string vc, string form) => "title \"Hold\"\ntime 4/4\noctave absolute\n"
            + "part fl { instrument flute }\npart vc { instrument cello }\n"
            + $"section A {{\n  fl {{ {fl} }}\n  vc {{ {vc} }}\n}}\n"
            + "section B {\n  fl { e''1 | d''1@fermata | }\n  vc { c1 | g,1@fermata | }\n}\n"
            + $"form {{ {form} }}\nscore {{ staff fl staff vc }}\n";
        string Length(string piece) => Assert.Single(CompositionChecks.Findings(piece), f => f.Id == "form.length").Message;
        // Two sections of two whole notes at ♩ = 120: 8 seconds, and the hold's second is in the seconds.
        Assert.StartsWith("The piece is 4 bars long, about 9 seconds played", Length(Duo("c''1 | d''1 |", "c1 | g,1 |", "A B")));
        // No closing bar line: the last bar is the one its note starts in.
        Assert.StartsWith("The piece is 2 bars long", Length(Duo("c''1 | d''1@fermata", "c1 | g,1@fermata", "A")));
        // A note tied into the last bar still plays it.
        Assert.StartsWith("The piece is 2 bars long", Length(Duo("c''1~ | c''1 |", "c1~ | c1 |", "A")));
        // Rests to the closing bar line: the music stopped before it.
        Assert.StartsWith("The piece is 1 bars long", Length(Duo("c''1 | R | R |", "c1 | R | R |", "A")));
        // What goes round is counted by the same bars.
        Assert.StartsWith("A (3 times), B (3 times): played the same each time, 8 of the 12 bars played repeats",
            Assert.Single(CompositionChecks.Findings(Duo("c''1 | d''1 |", "c1 | g,1 |", "A B A B A B")), f => f.Id == "form.played-three-times").Message);
    }

    // Owner's decision 2026-11-02: told it was short, a carol for flute and piano played its 24 bars
    // four times, and no check spoke: the check read a band's pieces.
    [Fact]
    public void AFormGoingRound_IsSaid_BeyondABand()
    {
        static string Duo(string form, string lyrics = "") => "title \"Round\"\ntime 4/4\noctave absolute\n"
            + "part fl { instrument flute }\npart vc { instrument cello }\n"
            + string.Concat("ABC".Select(s => $"section {s} {{\n  fl {{ c''1 | d''1 | }}\n  vc {{ c1 | g,1 | }}\n}}\n"))
            .Replace("section B {\n  fl { c''1", "section B {\n  fl { e''1").Replace("section C {\n  fl { c''1", "section C {\n  fl { g''1")
            + $"{lyrics}form {{ {form} }}\nscore {{ staff fl staff vc }}\n";
        var round = Assert.Single(CompositionChecks.Findings(Duo("A B A B A B")), f => f.Id == "form.played-three-times");
        Assert.StartsWith("A (3 times), B (3 times): played the same each time, 8 of the 12 bars played repeats", round.Message);
        // It names what goes round, not what plays twice.
        Assert.StartsWith("A (4 times): played the same each time, 8 of the 12 bars",
            Assert.Single(CompositionChecks.Findings(Duo("A B A B A A")), f => f.Id == "form.played-three-times").Message);
        // A rondo's refrain, and a return played twice, are forms.
        Assert.DoesNotContain(CompositionChecks.Findings(Duo("A B A C A")), f => f.Id == "form.played-three-times");
        Assert.DoesNotContain(CompositionChecks.Findings(Duo("A B A B")), f => f.Id == "form.played-three-times");
        // Sung, the verses change their words.
        Assert.DoesNotContain(CompositionChecks.Findings(Duo("A B A B A B", "lyrics words sings fl { la }\n")), f => f.Id == "form.played-three-times");
        Assert.DoesNotContain(CompositionChecks.Findings(Duo("A B A B A B", "lyrics { la }\n")), f => f.Id == "form.played-three-times");
        // A part alone is said too: the notes cannot tell filling the time from a song written down, and the review says when it does not apply.
        string alone = Duo("A B A B A B").Replace("part vc { instrument cello }\n", "").Replace("  vc { c1 | g,1 | }\n", "").Replace(" staff vc", "");
        Assert.Single(CompositionChecks.Findings(alone), f => f.Id == "form.played-three-times"); Assert.Contains("a song or a dance written down", ReviewNotes.All["form.played-three-times"].FineWhen);
    }

    // Owner's trial 2026-10-30: guitar and bass went from @p to @ff, the drums with no level.
    [Fact]
    public void ABandsDrumsWithNoLevel_WhileTheOthersMove_AreSaid()
    {
        string Rock(string kitLevel, string loud) => Band(Guitar + "\n" + Bass + "\n" + Drums,
            $"  gtr {{ e'4@p g' b' e'' | e'4{loud} g' b' e'' | }}\n  bass {{ e,1@p | e,1{loud} | }}\n"
            + $"  kit {{ voice {{ hh8{kitLevel} hh hh hh hh hh hh hh | hh8 hh hh hh hh hh hh hh | }} {{ bd4 sn bd sn | bd4 sn bd sn | }} }}",
            "staff gtr  staff bass  staff kit");
        Assert.Contains(CompositionChecks.Of(Rock("", "@ff")), f => f.StartsWith("kit: no level, while the others go from @p to @ff — "));
        Assert.DoesNotContain(CompositionChecks.Of(Rock("@p", "@ff")), f => f.StartsWith("kit: no level"));
        Assert.DoesNotContain(CompositionChecks.Of(Rock("", "@mp")), f => f.StartsWith("kit: no level"));
    }

    // Owner's report 2026-10-29: a rock trio's Chorus2 was its Chorus again, every part.
    [Fact]
    public void ASectionWrittenTwice_IsToldToBeNamedAgain()
    {
        const string body = "{\n  gtr { e'4 g' b' e'' | }\n  bass { e,1 | }\n}";
        string piece = $"title \"Copy\"\ntime 4/4\noctave absolute\n{Guitar}\n{Bass}\nsection Chorus {body}\nsection Bridge {{\n  gtr {{ a'1 | }}\n  bass {{ a,1 | }}\n}}\n"
            + $"section Chorus2 {body.Replace("\n  ", "\n    ")}\nform {{ Chorus Bridge Chorus2 }}\n";
        Assert.Contains(CompositionChecks.Of(piece), f => f == "Chorus2 is Chorus again, note for note: write Chorus once and name it "
            + "again in the form where it returns, not a second section with the same notes.");
        Assert.DoesNotContain(CompositionChecks.Of(piece.Replace("section Chorus2 {\n    gtr { e'4 g' b' e''", "section Chorus2 {\n    gtr { e'4 g' b' g''")),
            f => f.Contains("note for note"));
    }

    [Fact]
    public void GuitarAndDrums_AreToldTheBassIsMissing()
    {
        var findings = CompositionChecks.Of(Band(Guitar + "\n" + Drums, GuitarMusic + "\n" + DrumMusic, "staff gtr  staff kit"));
        Assert.Contains(findings, f => f.StartsWith("A band piece has an electric bass"));
        Assert.DoesNotContain(findings, f => f.StartsWith("A band piece has drums"));
    }

    [Fact]
    public void GuitarAndBass_AreToldTheDrumsAreMissing()
    {
        var findings = CompositionChecks.Of(Band(Guitar + "\n" + Bass, GuitarMusic + "\n" + BassMusic, "staff gtr  staff bass"));
        Assert.Contains(findings, f => f.StartsWith("A band piece has drums"));
        Assert.DoesNotContain(findings, f => f.StartsWith("A band piece has an electric bass"));
    }

    [Fact]
    public void ABandThatChangesTempo_IsToldToKeepOne()
    {
        string sections = GuitarMusic + "\n" + BassMusic + "\n" + DrumMusic + "\n  tempo 90";
        var full = Band(Guitar + "\n" + Bass + "\n" + Drums, GuitarMusic + "\n" + BassMusic + "\n" + DrumMusic,
            "staff gtr  staff bass  staff kit").Replace("form { A }", "section B {\n  tempo 80\n" + GuitarMusic + "\n" + BassMusic + "\n" + DrumMusic + "\n}\nform { A B }");
        Assert.Contains(CompositionChecks.Of(full), f => f.StartsWith("A band piece keeps one tempo"));
        Assert.DoesNotContain(CompositionChecks.Of(Band(Guitar + "\n" + Bass + "\n" + Drums,
            GuitarMusic + "\n" + BassMusic + "\n" + DrumMusic, "staff gtr  staff bass  staff kit")), f => f.StartsWith("A band"));
    }

    [Fact]
    public void TheBandSketch_IsWhole()
    {
        // Its rhythm section whole; chords under no tune and no words, which it is told (owner's
        // report 2026-10-29: an instrumental's tune is its lead).
        Assert.Equal(["A tune is played in only 0 of the 8 bars"],
            Flatness(ComposedPieces.NightDrive).Select(f => f[..f.IndexOf(':')]));
    }

    [Fact]
    public void AStringQuartet_IsNoBand()
    {
        // Two tempi and no drums: concert music, where a change of tempo is a plan.
        Assert.DoesNotContain(CompositionChecks.Of(ComposedPieces.LittleQuartet), f => f.StartsWith("A band"));
    }

    [Fact]
    public void TheJazzLeadSheet_IsWhole()
    {
        // Owner's request 2026-10-27: a jazz lead sheet — one staff, a chords row, jazz changes —
        // 16 bars, home on F.
        Assert.Empty(CompositionChecks.Of(ComposedPieces.BlueWindow));
        Assert.Contains("chords changes", ComposedPieces.BlueWindow);
    }

    // Owner's trials 2026-10-29: a violin's tune two thirds quarters passed the 80% an accompaniment
    // is allowed. The melody — the solo beside a piano, a sung part, a solo piano's right hand — is
    // told at 60%; the piano under the violin, at the same share, is not.
    private const string ViolinAndPiano = """
        title "Lantern"
        time 4/4
        octave absolute
        key d major
        part vn "Violin" "Vn." { instrument violin }
        part rh "Piano" "Pno." { instrument piano-right }
        part lh { instrument piano-left }
        section A {
          vn { d''4 e''4 fis''4 d''4 | a''2 fis''4 d''4 | e''4 fis''4 g''4 e''4 | d''2 a'2 | d''4 e''4 fis''4 d''4 | a''2 fis''4 d''4 | e''4 fis''4 g''4 e''4 | d''1 | }
          rh { <d' fis'>2 <d' fis'>4 <d' fis'>4 | <d' fis'>2 <d' fis'>4 <d' fis'>4 | <cis' e'>2 <cis' e'>4 <cis' e'>4 | <d' fis'>2 <cis' e'>4 <cis' e'>4 | <d' fis'>2 <d' fis'>4 <d' fis'>4 | <d' fis'>2 <d' fis'>4 <d' fis'>4 | <cis' e'>2 <cis' e'>4 <cis' e'>4 | <d' fis'>2 <d' fis'>4 <d' fis'>4 | }
          lh { d2 a,2 | d2 a,2 | a,2 e2 | d2 a,2 | d2 a,2 | d2 a,2 | a,2 e2 | d1 | }
        }
        form { ~A }
        score { staff vn  grandStaff { staff rh  staff lh } }
        """;

    /// <summary>
    /// Voices moving as one share one rhythm: the melody's findings are said of the top voice alone
    /// (owner's trial 2026-10-29: a hymn sung in every voice heard the same two findings four times
    /// over, and came back stumbling in every voice).
    /// </summary>
    [Fact]
    public void VoicesMovingAsOne_HearTheMelodysRhythmOnce_OfTheTopVoice()
    {
        static string Voice(string bar) => string.Concat(Enumerable.Repeat(bar + " | ", 8));
        string hymn = $$"""
            octave absolute
            time 4/4
            key c major
            part sop { instrument soprano }
            part alt { instrument alto }
            part ten { instrument tenor }
            part bas { instrument voice-bass }
            section V {
              sop { {{Voice("e'4@mp g'4 e'4 c'4")}} }
              alt { {{Voice("c'4@mp e'4 c'4 g4")}} }
              ten { {{Voice("g4@mp c'4 g4 e4")}} }
              bas { {{Voice("c4@mp c4 c4 c4")}} }
              lyrics words sings sop { {{string.Concat(Enumerable.Repeat("la la la la | ", 8))}} }
            }
            form { V }
            score { choirStaff {
              staff sop  lyrics words sings sop
              staff alt  lyrics words sings alt
              staff ten  lyrics words sings ten
              staff bas  lyrics words sings bas
            } }
            """;
        var findings = CompositionChecks.Of(hymn);
        var melody = findings.Where(f => f.Contains(", the melody: ")).ToList();
        Assert.NotEmpty(melody);
        Assert.All(melody, f => Assert.StartsWith("sop, the melody: ", f));
        Assert.Contains(findings, f => f.Contains("every part moves in the same rhythm"));
        // Sung by the soprano alone, the lower voices are not told of one value each either (owner's
        // trial, the same day: so told, they were pulled apart).
        string sopranoSings = hymn.Replace("  staff alt  lyrics words sings alt", "  staff alt")
            .Replace("  staff ten  lyrics words sings ten", "  staff ten").Replace("  staff bas  lyrics words sings bas", "  staff bas");
        var sung = CompositionChecks.Of(sopranoSings);
        // (Their line is another matter: this book's alto and tenor leap in every bar, and SingersLines says so.)
        Assert.DoesNotContain(sung, f => (f.StartsWith("alt") || f.StartsWith("ten") || f.StartsWith("bas")) && !f.Contains("moves are steps"));
        Assert.Contains(sung, f => f.StartsWith("sop, the melody: "));
    }

    /// <summary>Strings with drums are no band, and no string of them a solo (owner's trial
    /// 2026-10-29: a boss-battle piece for strings and drums was asked for a bass and a guitar, and
    /// its viola's figure and cello's bass were held to a melody's rhythm).</summary>
    [Fact]
    public void StringsWithDrums_AreNoBand_AndNoneOfThemASolo()
    {
        static string Bars(string bar) => string.Concat(Enumerable.Repeat(bar + " | ", 16));
        string piece = $$"""
            octave absolute
            time 4/4
            key d minor
            part vn { instrument violin }
            part va { instrument viola }
            part vc { instrument cello }
            part kit { clef percussion }
            section A {
              vn { {{Bars("d''8@f f'' a''4 g''8 f'' d''4")}} }
              va { {{Bars("d'8@f a' d' a' f' a' d' a'")}} }
              vc { {{Bars("d,4@f a, d, a,")}} }
              kit { {{Bars("bd4@f sn bd sn")}} }
            }
            form { A }
            """;
        var findings = CompositionChecks.Of(piece);
        Assert.DoesNotContain(findings, f => f.StartsWith("A band piece"));
        Assert.DoesNotContain(findings, f => f.Contains(", the melody: "));
        // With a bass guitar or keyboards beside the drums it is a band, as before.
        Assert.Contains(CompositionChecks.Of(piece.Replace("part va { instrument viola }", "part va { instrument guitar midiInstrument \"electric guitar (clean)\" }")),
            f => f.StartsWith("A band piece has an electric bass"));
    }

    [Fact]
    public void TheMelody_IsHeldToMoreRhythmThanItsAccompaniment()
    {
        var findings = CompositionChecks.Of(ViolinAndPiano);
        Assert.Contains(findings, f => f.StartsWith("vn, the melody: ") && f.Contains("a tune lives on its rhythm"));
        // …leaving an even pace that is the music's own to the composer (owner's trial 2026-10-29: a
        // hymn sent back for its soprano's quarters came again stumbling in every voice).
        Assert.All(findings.Where(f => f.Contains(", the melody: ")),
            f => Assert.EndsWith("unless an even pace is the music's own character, as in a hymn, a chorale or a march: then keep it.", f));
        Assert.DoesNotContain(findings, f => f.StartsWith("rh"));
        // Sung: the part a lyrics track sings is the melody.
        Assert.Contains(CompositionChecks.Of(ViolinAndPiano.Replace("  lh {", "  lyrics words sings rh { a a a a a | }\n  lh {")),
            f => f.StartsWith("rh, the melody: "));
    }

    // Owner's trial 2026-10-29: a pop song sang `r8 x8 x8 x8~ x4 x4` in every bar — eighths and
    // dotted quarters half and half, which the one-value count passes.
    private static string Song(string tune) => $$"""
        title "Morning"
        time 4/4
        octave absolute
        part vox "Voice" { instrument soprano }
        part rh "Piano" "Pno." { instrument piano-right }
        part lh { instrument piano-left }
        section A {
          vox { {{tune}} }
          lyrics words sings vox { a a a a | }
          rh { {{string.Concat(Enumerable.Repeat("<e' g'>2 <e' g'>4 <e' g'>4 | ", 8))}} }
          lh { {{string.Concat(Enumerable.Repeat("c4 g4 e4 g4 | ", 8))}} }
        }
        form { ~A }
        score { staff vox  lyrics words  grandStaff { staff rh  staff lh } }
        """;

    // Owner's review 2026-11-01: a rock instrumental's verse played one rhythm in all 8 of its bars,
    // its chorus two in turn, its solo free — 40% over the piece, and the count above said nothing.
    [Fact]
    public void AMelodyOfOneRhythmThroughASection_IsToldSo_SectionByName()
    {
        const string poured = "r8 a'8 c''8 e''8~ e''4 d''4 | ";
        const string varied = "c''2. r4 | e''4. d''8 c''4 a'4 | g'4 r4 g'8 a'8 b'4 | c''4 d''8 e''8 g''2 | e''8 d''8 c''4~ c''4 a'4 | c''1 | "
            + "e''4. d''8 c''4 a'4 | g'4. a'8 g'2 | c''4 d''8 e''8 g''2 | e''8 d''8 c''4~ c''4 a'4 | c''8 r8 c''8 r8 c''4 r4 | g'1 | ";
        string Book(string a, string b) => $$"""
            title "Morning"
            time 4/4
            octave absolute
            part vox "Voice" { instrument soprano }
            part rh "Piano" "Pno." { instrument piano-right }
            part lh { instrument piano-left }
            section A {
              vox { {{a}} }
              lyrics words sings vox { a a a a | }
              rh { {{string.Concat(Enumerable.Repeat("<e' g'>2 <e' g'>4 <e' g'>4 | ", 8))}} }
              lh { {{string.Concat(Enumerable.Repeat("c4 g4 e4 g4 | ", 8))}} }
            }
            section B {
              vox { {{b}} }
              lyrics words sings vox { a a a a | }
              rh { {{string.Concat(Enumerable.Repeat("<f' a'>2 <f' a'>4 <f' a'>4 | ", 12))}} }
              lh { {{string.Concat(Enumerable.Repeat("f4 c'4 a4 c'4 | ", 12))}} }
            }
            form { ~A ~B }
            score { staff vox  lyrics words  grandStaff { staff rh  staff lh } }
            """;
        // 8 of 20 bars over the piece — under the piece's half — and all 8 of A's.
        var findings = CompositionChecks.Of(Book(string.Concat(Enumerable.Repeat(poured, 8)), varied));
        Assert.DoesNotContain(findings, f => f.Contains("of its 20 bars have the same rhythm"));
        Assert.Contains(findings, f => f.StartsWith("vox, the melody: in A 8 of its 8 bars have the same rhythm"));
        // Two rhythms in turn are not one poured: 4 of 8.
        var inTurn = CompositionChecks.Of(Book(string.Concat(Enumerable.Repeat(poured + "c''2. r4 | ", 4)), varied));
        Assert.DoesNotContain(inTurn, f => f.Contains("have the same rhythm"));
    }

    // Owner's review 2026-11-01: a 72-bar instrumental's bass played one figure in section after
    // section (the guide's rule is any piece's: a contrasting section takes a figure of its own).
    [Fact]
    public void AnAccompanimentsOneFigureThroughEverySection_IsSaid()
    {
        var one = CompositionChecks.Of(Rock3("e'4 g' a'2", "e,8 e, b, e, e, b, e, b,", lastGuitar: "b'2 a'4 g'4"));
        Assert.Contains(one, f => f.StartsWith("bass: the same figure in every section (A, B, C)"));
        // The figure moved along the chords is one figure; a section with a figure of its own is not.
        string own = Rock3("e'4 g' a'2", "e,8 e, b, e, e, b, e, b,", lastGuitar: "b'2 a'4 g'4")
            .Replace("section C {\n  gtr", "section C {\n  bass { e,2 b,2 | a,2 e,2 | e,2 b,2 | a,2 e,2 | }\n  gtr");
        own = System.Text.RegularExpressions.Regex.Replace(own, @"(section C \{\n  bass \{[^\n]*\n  gtr \{[^\n]*\n)  bass \{[^\n]*\n", "$1");
        Assert.DoesNotContain(CompositionChecks.Of(own), f => f.StartsWith("bass: the same figure"));
    }

    // Owner's review 2026-11-01: a nine-section song's kick and snare were one beat in every section
    // but the bridge; the four-section rule (above) asks for every one.
    [Fact]
    public void ABandsDrumsOnOneBeatInAllSectionsButOne_OfSixOrMore_AreSaid()
    {
        const string hats = "hh8 hh hh hh hh hh hh hh", ride = "cymr8 cymr cymr cymr cymr cymr cymr cymr", beat = "bd4 sn bd sn";
        var six = CompositionChecks.Of(Rock3("e'4 g' a'2", "e,8 e, e, e, e, e, e, e,",
            kits: [(hats, beat), (ride, beat), (hats, beat), (ride, beat), (hats, "bd4 r sn r"), (ride, beat)]));
        Assert.Contains(six, f => f.StartsWith("kit: the kick and the snare play one beat in 5 of the 6 sections (all but E)"));
        // Two sections of their own are two contrasts.
        var two = CompositionChecks.Of(Rock3("e'4 g' a'2", "e,8 e, e, e, e, e, e, e,",
            kits: [(hats, beat), (ride, beat), (hats, "bd4 r sn r"), (ride, beat), (hats, "bd4 r sn r"), (ride, beat)]));
        Assert.DoesNotContain(two, f => f.StartsWith("kit: the kick and the snare"));
    }

    [Fact]
    public void AMelodyOfOneBarsRhythm_IsToldSo_AVariedOneIsNot()
    {
        var poured = Song(string.Concat(Enumerable.Repeat("r8 a'8 c''8 e''8~ e''4 d''4 | ", 8)));
        Assert.Contains(CompositionChecks.Of(poured), f => f.StartsWith("vox, the melody: 8 of its 8 bars have the same rhythm"));
        var varied = Song("r8 a'8 c''8 e''8~ e''4 d''4 | c''2. r4 | e''4. d''8 c''4 a'4 | g'1 | "
            + "r8 a'8 c''8 e''8~ e''4 d''4 | c''4 d''8 e''8 g''2 | e''8 d''8 c''4~ c''4 a'4 | c''1 | ");
        Assert.DoesNotContain(CompositionChecks.Of(varied), f => f.Contains("have the same rhythm"));
    }

    // Owner's trial 2026-10-29: a quartet's dynamics were all in the first violin. Each player reads
    // only their own staff; an @sf is not a level; a piano hand rides on the other's, a drum part on none.
    [Fact]
    public void APartWithNoDynamicsOfItsOwn_IsToldSo()
    {
        const string quartet = """
            title "Dusk"
            time 4/4
            octave absolute
            part vn1 "Violin I" { instrument violin }
            part vn2 "Violin II" { instrument violin }
            part vc "Cello" { instrument cello }
            phrase low { c4@mf g,4 c4 g,4 | }
            section A {
              vn1 { e''4@p f''4 g''4@f a''4 | }
              vn2 { c''4 d''4@sf e''4 f''4 | }
              vc { low }
            }
            form { ~A }
            score { staffGroup { staff vn1  staff vn2  staff vc } }
            """;
        var findings = CompositionChecks.Of(quartet);
        Assert.Contains(findings, f => f.StartsWith("No dynamics in vn2: each player reads only their own staff"));
        Assert.DoesNotContain(findings, f => f.StartsWith("No dynamics") && f.Contains("vc"));
        Assert.DoesNotContain(CompositionChecks.Of(quartet.Replace("d''4@sf", "d''4@mp")), f => f.StartsWith("No dynamics"));
        // None anywhere: nothing to measure the others by — the piece is not told this.
        Assert.DoesNotContain(CompositionChecks.Of(quartet.Replace("@p", "").Replace("@f", "").Replace("@mf", "")),
            f => f.StartsWith("No dynamics"));
    }

    // Owner's review 2026-10-30: a rock trio's bass reused its intro phrases under the chorus, from
    // the intro's last @ff straight back to @p, inside one section; and its drum line, re-cut, had
    // the crashes a bar before each phrase began.
    const string Trio = """
        title "Run"
        time 4/4
        octave absolute
        part gt "Guitar" { instrument guitar }
        part bs "Bass" { instrument bass }
        part kit "Drums" { clef percussion }
        phrase lo { e,4@p g, a, b, | e,4 g, a, b, | e,4 g, a, b, | e,1@ff | }
        phrase hi { e4 g a b | e4 g a b | e4 g a b | e1 | }
        phrase hats { hh8 hh hh hh hh hh hh hh | }
        phrase crash { cymc1 | }
        section A {
          gt { hi hi hi hi }
          bs { lo lo lo lo }
          kit { hats hats hats CRASHES hats hats hats hats hats hats hats hats }
        }
        form { ~A }
        score { staff gt staff bs staff kit }
        """;

    [Fact]
    public void APhrasePlayedAgain_DroppingFromItsLastLevel_IsToldSo()
    {
        var findings = CompositionChecks.Of(Trio.Replace("CRASHES", "crash"));
        Assert.Contains(findings, f => f.StartsWith("bs: lo ends at @ff and lo opens at @p — played one after the other"));
        // Brought down, or meant: said nothing.
        Assert.DoesNotContain(CompositionChecks.Of(Trio.Replace("CRASHES", "crash").Replace("e,4@p", "e,4@p.subito")),
            f => f.StartsWith("bs: lo ends"));
        Assert.DoesNotContain(CompositionChecks.Of(Trio.Replace("CRASHES", "crash").Replace("e,1@ff", "e,1@mp")),
            f => f.StartsWith("bs: lo ends"));
    }

    [Fact]
    public void CrashesMostlyOffThePhrasesFirstBars_AreToldSo()
    {
        // Phrases begin at bars 1, 5, 9, 13: crashes at 4, 8, 12 are each a bar early.
        string early = Trio.Replace("CRASHES hats hats hats hats hats hats hats hats",
            "crash hats hats hats crash hats hats hats crash hats hats hats");
        Assert.Contains(CompositionChecks.Of(early), f => f.StartsWith("kit: 4 of its 4 crashes in A come in off the bars the phrases begin on (bars 4, 8, 12")
            || f.StartsWith("kit: 3 of its 3 crashes in A come in off the bars the phrases begin on (bars 4, 8, 12"));
        // On the phrases' first bars: nothing to say.
        string onTime = Trio.Replace("hats hats hats CRASHES hats hats hats hats hats hats hats hats",
            "crash hats hats hats crash hats hats hats crash hats hats hats crash hats hats hats");
        Assert.DoesNotContain(CompositionChecks.Of(onTime), f => f.StartsWith("kit:") && f.Contains("crashes"));
    }

    // Owner's review 2026-10-30: a rock trio's last chorus was its chorus again at @ff, written out in
    // phrases of new names; and its bass ran on in eighths under the last held chord and crash.
    const string Song2 = """
        title "Spark"
        time 4/4
        octave absolute
        part gt "Guitar" { instrument guitar }
        part bs "Bass" { instrument bass }
        phrase tuneA { e'4@f g' a' b' | e''1 | }
        phrase tuneZ { e'4@ff g' a' b' | e''1 | }
        phrase lowA { e,8@f e, e, e, e, e, e, e, | e,8 e, e, e, e, e, e, e, | }
        phrase lowZ { e,8@ff e, e, e, e, e, e, e, | ENDING | }
        section A { gt { tuneA } bs { lowA } }
        section Z { gt { tuneZ } bs { lowZ } }
        form { ~A ~Z }
        score { staff gt staff bs }
        """;

    [Fact]
    public void ASectionPlayingAnEarlierOneAgain_OnlyLouder_IsToldSo()
    {
        var findings = CompositionChecks.Of(Song2.Replace("ENDING", "e,8 e, e, e, e, e, e, e,"));
        Assert.Contains(findings, f => f.StartsWith("Z plays A again, note for note in every part — only its levels and marks differ"));
        // A bar changed toward its close is music of its own.
        Assert.DoesNotContain(CompositionChecks.Of(Song2.Replace("ENDING", "e,8 e, e, e, e, e, e, e,").Replace("tuneZ { e'4@ff g' a' b' | e''1", "tuneZ { e'4@ff g' a' b' | g''1")),
            f => f.StartsWith("Z plays A again"));
    }

    [Fact]
    public void ASectionPlayingAnEarlierOneAgain_ButABar_IsToldSo()
    {
        // Eight bars a part, the last chorus's final bar its own (owner's review 2026-10-30).
        string Book(string last) => Song2.Replace("tuneA { e'4@f g' a' b' | e''1 | }", "tuneA { e'4@f g' a' b' | e''1 | e'4 g' a' b' | e''1 | e'4 g' a' b' | e''1 | e'4 g' a' b' | e''1 | }")
            .Replace("tuneZ { e'4@ff g' a' b' | e''1 | }", $"tuneZ {{ e'4@ff g' a' b' | e''1 | e'4 g' a' b' | e''1 | e'4 g' a' b' | e''1 | e'4 g' a' b' | {last} | }}")
            .Replace("lowA { e,8@f e, e, e, e, e, e, e, | e,8 e, e, e, e, e, e, e, | }", "lowA { " + string.Concat(Enumerable.Repeat("e,8 e, e, e, e, e, e, e, | ", 8)) + "}")
            .Replace("lowZ { e,8@ff e, e, e, e, e, e, e, | ENDING | }", "lowZ { " + string.Concat(Enumerable.Repeat("e,8 e, e, e, e, e, e, e, | ", 8)) + "}");
        Assert.Contains(CompositionChecks.Of(Book("e'''1")), f => f.StartsWith("Z plays A again, note for note in every part but 1 bar"));
        Assert.DoesNotContain(CompositionChecks.Of(Book("e'''1").Replace("tuneZ { e'4@ff g' a' b' | e''1", "tuneZ { g'4@ff a' b' c'' | d''1")),
            f => f.StartsWith("Z plays A again"));
    }

    // Owner's review 2026-10-30: a trio's one guitar, told it had no tune, played one in every bar;
    // told nobody played the chords, it struck them in every bar again.
    [Fact]
    public void ABandsOnlyPartOverTheBass_IsToldToShareItsChordsAndItsTune()
    {
        string chords = string.Concat(Enumerable.Repeat("<e b>8 <e b> <e b> <e b> <e b> <e b> <e b> <e b> | ", 8));
        string low = string.Concat(Enumerable.Repeat("e,8 e, e, e, e, e, e, e, | ", 8));
        string beat = string.Concat(Enumerable.Repeat("hh8 hh hh hh hh hh hh hh | ", 8));
        string book = "title \"Run\"\ntime 4/4\noctave absolute\npart gt \"Guitar\" { instrument guitar midiInstrument \"overdriven guitar\" }\n"
            + "part bs \"Bass\" { instrument bass }\npart kit \"Drums\" { clef percussion }\n"
            + $"section A {{\n  gt {{ {chords} }}\n  bs {{ {low} }}\n  kit {{ {beat} }}\n}}\nform {{ ~A }}\nscore {{ staff gt staff bs staff kit }}\n";
        Assert.Contains(CompositionChecks.Of(book), f => f.StartsWith("A tune is played in only")
            && f.Contains("gt is the band's only part over the bass, its chords and its tune both: share them out"));
    }

    // Owner's review 2026-10-30: in C minor the chat's AI wrote every B natural — the leading tone — as
    // `ces`, thinking the key made `b` a B-flat.
    [Fact]
    public void ANaturalOutOfTheKey_WrittenWithAForeignAccidental_IsToldSo()
    {
        string Book(string key, string seven) => "title \"Run\"\ntime 4/4\noctave absolute\n"
            + $"key {key}\npart vn \"Violin\" {{ instrument violin }}\npart vc \"Cello\" {{ instrument cello }}\n"
            + $"section A {{\n  vn {{ c''4@mf d'' ees'' {seven}'' | c''1 | c''4 d'' ees'' {seven}'' | c''1 | }}\n"
            + "  vc { c4@mf g, c g, | c1 | c4 g, c g, | c1 | }\n}\nform { ~A }\nscore { staffGroup { staff vn staff vc } }\n";
        Assert.Contains(CompositionChecks.Of(Book("c minor", "ces")),
            f => f.StartsWith("vn: naturals out of the key written with an accidental it does not use, in C minor — `ces` for `b` (2 times)")
                && f.Contains("B natural is `b` in any key") && f.Contains("`ces` prints a C♭"));
        Assert.DoesNotContain(CompositionChecks.Of(Book("c minor", "b")), f => f.Contains("naturals out of the key"));
        // In the key, its own name: E-flat minor's C-flat (said, if at all, by the other finding).
        Assert.DoesNotContain(CompositionChecks.Of(Book("ees minor", "ces")), f => f.Contains("naturals out of the key"));
        // A sharp in a key of sharps is the raised degree's own name.
        Assert.DoesNotContain(CompositionChecks.Of(Book("fis minor", "eis")), f => f.Contains("naturals out of the key"));
    }

    // Owner's review 2026-10-30: a rock trio's tune in halves and quarters was counted in 23 of its
    // 32 bars (bars of two notes were no tune's); its drums played one bar sixteen times running.
    static string Trio16(string gt, string kit) => "title \"Run\"\ntime 4/4\noctave absolute\n"
        + "part gt \"Guitar\" { instrument guitar midiInstrument \"overdriven guitar\" }\npart bs \"Bass\" { instrument bass }\n"
        + "part kit \"Drums\" { clef percussion }\n"
        + $"section A {{\n  gt {{ {gt} }}\n  bs {{ {string.Concat(Enumerable.Repeat("a,8@mf a, a, a, a, a, a, a, | ", 16))} }}\n"
        + $"  kit {{ {kit} }}\n}}\nform {{ ~A }}\nscore {{ staff gt staff bs staff kit }}\n";

    [Fact]
    public void ATuneInLongNotes_IsCountedAsATune()
    {
        string tune = string.Concat(Enumerable.Repeat("a'2@mf c''2 | b'4 a' g' a' | ", 8));
        string beat = string.Concat(Enumerable.Repeat("hh8@mf hh hh hh hh hh hh hh | ", 16));
        Assert.DoesNotContain(CompositionChecks.Of(Trio16(tune, beat)), f => f.StartsWith("A tune is played in only"));
        // Chords in two notes are no tune, however few.
        string chords = string.Concat(Enumerable.Repeat("<a e'>2@mf <c' g'>2 | ", 16));
        Assert.Contains(CompositionChecks.Of(Trio16(chords, beat)), f => f.StartsWith("A tune is played in only"));
    }

    [Fact]
    public void TheDrumsPlayingOneBarSixteenTimesRunning_AreToldToMarkThePhrases()
    {
        string tune = string.Concat(Enumerable.Repeat("a'2@mf c''2 | b'4 a' g' a' | ", 8));
        string bar = "<hh bd>8@mf hh <hh sn> hh <hh bd> hh <hh sn> hh | ";
        Assert.Contains(CompositionChecks.Of(Trio16(tune, string.Concat(Enumerable.Repeat(bar, 16)))),
            f => f.StartsWith("kit: the same bar 16 times running (bars 1-16, as the form plays them)"));
        string filled = string.Concat(Enumerable.Repeat(string.Concat(Enumerable.Repeat(bar, 7)) + "sn8 sn tomh tomh toml toml bd bd | ", 2));
        Assert.DoesNotContain(CompositionChecks.Of(Trio16(tune, filled)), f => f.Contains("the same bar"));
    }

    [Fact]
    public void APartRunningOnUnderTheOthersLastHeldChord_IsToldSo()
    {
        Assert.Contains(CompositionChecks.Of(Song2.Replace("ENDING", "e,8 e, e, e, e, e, e, e,")),
            f => f.StartsWith("bs runs on through the last bar (8 notes) while gt holds its last note from its first beat"));
        Assert.DoesNotContain(CompositionChecks.Of(Song2.Replace("ENDING", "e,1")), f => f.Contains("through the last bar"));
    }

    // Owner's question 2026-10-27, "a model asked in a chat has no example — is none better?": the
    // grammar shows no whole piece to compose after, only how each thing is written.
    [Fact]
    public void TheGrammar_ShowsNoPieceToComposeAfter()
    {
        var grammar = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "GRAMMAR_FOR_LLM.md"));
        foreach (var title in new[] { "Little Quartet", "Night Drive", "Blue Window" })
            Assert.DoesNotContain($"title \"{title}\"", grammar);
        // Owner's decision 2026-10-29: one printed page unless a length is asked for — in the composing
        // guide, kept apart from the grammar the same day.
        var guide = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "COMPOSE_FOR_LLM.md"));
        foreach (var title in new[] { "Little Quartet", "Night Drive", "Blue Window" })
            Assert.DoesNotContain($"title \"{title}\"", guide);
        Assert.Contains("about 24 to 32 bars\n  of 4/4 suits a piece", guide.Replace("\r\n", "\n"));
        // Owner's review 2026-11-02: 29 pieces with a piano, composed in the chat, and not one pedal.
        Assert.Contains("`@sustain` on the\n  harmony's first bass note", guide.Replace("\r\n", "\n"));
    }
}
