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
    public void TheGrammarsBandExample_IsWhole()
    {
        var grammar = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "GRAMMAR_FOR_LLM.md"));
        int at = grammar.IndexOf("title \"Night Drive\"", System.StringComparison.Ordinal);
        Assert.True(at > 0);
        Assert.Empty(CompositionChecks.Of(grammar[at..grammar.IndexOf("```", at, System.StringComparison.Ordinal)]));
    }

    [Fact]
    public void AStringQuartet_IsNoBand()
    {
        // Two tempi and no drums: concert music, where a change of tempo is a plan.
        var grammar = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "GRAMMAR_FOR_LLM.md"));
        int at = grammar.IndexOf("title \"Little Quartet\"", System.StringComparison.Ordinal);
        Assert.DoesNotContain(CompositionChecks.Of(grammar[at..grammar.IndexOf("```", at, System.StringComparison.Ordinal)]), f => f.StartsWith("A band"));
    }

    [Fact]
    public void TheGrammarsJazzLeadSheet_IsWhole()
    {
        // Owner's request 2026-10-27: a jazz lead sheet — one staff, a chords row, jazz changes.
        var grammar = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "GRAMMAR_FOR_LLM.md"));
        int at = grammar.IndexOf("title \"Blue Window\"", System.StringComparison.Ordinal);
        Assert.True(at > 0);
        string sheet = grammar[at..grammar.IndexOf("```", at, System.StringComparison.Ordinal)];
        Assert.Empty(CompositionChecks.Of(sheet));
        Assert.Contains("chords changes", sheet);
    }
}
