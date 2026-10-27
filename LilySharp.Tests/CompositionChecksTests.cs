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
        Assert.Empty(CompositionChecks.Of(home).Where(f => f.StartsWith("The piece")));
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
            section A { rh { c'4 d' e' f' | } lh { c2 g | } }
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
        Assert.Contains("a bare `c` is C3", finding);
        // The same hand two octaves up (round C3) is where a left hand plays.
        string usual = Piano(Hands, RightHand, "e4 r8 b8 e'4 b4 | c4. g8 c'2 | a2 e'4 r4 | b4. fis'8 e2 |");
        Assert.DoesNotContain(CompositionChecks.Of(usual), f => f.Contains("a pianist's"));
        // Two hands braced with no instrument are a piano's too: the lower staff is the left hand
        // (no instrument, so a bare `c` is C4: the same sounds, a comma more).
        string bare = Piano("part rh { clef treble }\npart lh { clef bass }", RightHand, "e,,,4 r8 b,,,8 e,,4 b,,,4 | c,,,4. g,,,8 c,,2 | a,,,2 e,,4 r4 | b,,,4. fis,,8 e,,,2 |");
        Assert.Contains(CompositionChecks.Of(bare), f => f.StartsWith("lh:") && f.Contains("far below") && !f.Contains("bare `c`"));
    }

    // Owner's report 2026-10-27: the next piano piece labelled its staves 右手 and 左手, and its right
    // hand, planned for D5–A5, sounded D5–G6 — written by LilyPond's octaves, where `c'` is middle C.
    [Fact]
    public void ARightHandAnOctaveHigh_AndStavesNamedByHand_AreToldSo()
    {
        string named = Piano("part rh \"右手\" \"右手\" { instrument piano-right }\npart lh \"左手\" \"左手\" { instrument piano-left }",
            "d'4 f'8 a'8 d''4 a''4 | f'4 bes'8 d''8 f''2 | a'4. d''8 f''4 a''4 | g'4 bes'8 d''8 g''2 |",
            "d,2. a,4 | bes,2 f4 d4 | d,2 a,4 f4 | g,4. d8 g4 bes4 |");
        var findings = CompositionChecks.Of(named);
        var high = Assert.Single(findings, f => f.StartsWith("rh:"));
        Assert.Contains("high above a pianist's right hand", high);
        Assert.Contains("not LilyPond's `c'`", high);
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
        string apart = Piano(Hands, Bars("d'8 f' a'4 g'8 f' e'4", 8) + Bars("a'4 f' d'2", 8), Bars("d,4 a, d a,", 16));
        var findings = CompositionChecks.Of(apart);
        Assert.DoesNotContain(findings, f => f.StartsWith("lh:") && f.Contains("of its notes are"));
        Assert.Contains(findings, f => f.StartsWith("rh, lh: the hands stand far apart") && f.Contains("up to D3") && f.Contains("from D5"));
        // The same right hand an octave down, over the same figure: close enough.
        string close = Piano(Hands, Bars("d8 f a4 g8 f e4", 8) + Bars("a4 f d2", 8), Bars("d,4 a, d a,", 16));
        Assert.DoesNotContain(CompositionChecks.Of(close), f => f.Contains("far apart") || f.Contains("a pianist's"));
        // A right hand's line of one value is still flat.
        Assert.Contains(CompositionChecks.Of(Piano(Hands, Bars("d8 f a f e f g e", 16), Bars("d,2 a,", 16))),
            f => f.StartsWith("rh: 100% of its notes are eighth notes"));
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
        Assert.Empty(Flatness(ComposedPieces.NightDrive));
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

    // Owner's question 2026-10-27, "a model asked in a chat has no example — is none better?": the
    // grammar shows no whole piece to compose after, only how each thing is written.
    [Fact]
    public void TheGrammar_ShowsNoPieceToComposeAfter()
    {
        var grammar = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "GRAMMAR_FOR_LLM.md"));
        foreach (var title in new[] { "Little Quartet", "Night Drive", "Blue Window" })
            Assert.DoesNotContain($"title \"{title}\"", grammar);
        Assert.Contains("a piece runs 32 to\n  64 bars", grammar.Replace("\r\n", "\n"));
    }
}
