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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>check voiceLeading s a t b</c> (docs/voice-leading-design.md, owner's decisions 2026-10-28):
/// the check pragma, and what it finds — parallel fifths and octaves (LYS3002), crossed voices
/// (LYS3003), voices too far apart (LYS3004), all information — and that it changes nothing else.
/// </summary>
[Trait("Category", "Unit")]
public class VoiceLeadingTests
{
    /// <summary>A four-part book in absolute octaves, the voices' music given bar by bar.</summary>
    private static string Book(string s, string a, string t, string b, string check = "check voiceLeading s a t b") => $"""
        time 4/4
        octave absolute
        part s {"{"} instrument soprano {"}"}
        part a {"{"} instrument alto {"}"}
        part t {"{"} instrument tenor {"}"}
        part b {"{"} instrument voice-bass {"}"}
        {check}
        section A {"{"}
          s {"{"} {s} {"}"}
          a {"{"} {a} {"}"}
          t {"{"} {t} {"}"}
          b {"{"} {b} {"}"}
        {"}"}
        form {"{"} A {"}"}
        score {"{"} choirStaff {"{"} staff s  staff a  staff t  staff b {"}"} {"}"}
        """;

    private static List<Diagnostic> All(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private static List<Diagnostic> Found(string source, string code)
        => [.. All(source).Where(d => d.Code == code)];

    // Quiet voices, worked out pair by pair (MIDI): s 64 62 60 62 64, a 60 59 55 59 60, t 55 55 52 55 55,
    // b 48 43 48 43 48 — no perfect interval followed by the same one with both voices moving,
    // no crossing, every neighbour within its spacing.
    private const string QuietS = "e'4 d' c' d' | e'1 |", QuietA = "c'4 b g b | c'1 |";
    private const string QuietT = "g4 g e g | g1 |", QuietB = "c4 g, c g, | c1 |";

    [Fact]
    public void ParallelFifths_InSimilarMotion_AreSaid_AtTheUpperVoicesNote()
    {
        // s over b: G4/C3 (a twelfth) to A4/D3 (a twelfth).
        string src = Book("e'4 f' g' a' | g'1 |", QuietA, QuietT, "c4 d c d | c1 |");
        var found = Found(src, DiagnosticCodes.ParallelPerfectIntervals);
        Assert.Contains(found, d => d.Message.Contains("parallel fifths: s and b") && d.Severity == DiagnosticSeverity.Info);
        var first = found.OrderBy(d => d.Span.Start).First(d => d.Message.Contains("s and b"));
        Assert.Equal("a'", src.Substring(first.Span.Start, first.Span.Length));
    }

    [Fact]
    public void ContraryOctaves_AreParallelsToo()
    {
        // s over b: C5/C4 (an octave) to D5/D3 (two octaves) — s up, b down.
        string src = Book("c''4 d'' e'' f'' | e''1 |", QuietA, QuietT, "c'4 d e d | c1 |");
        Assert.Contains(Found(src, DiagnosticCodes.ParallelPerfectIntervals), d => d.Message.Contains("parallel octaves: s and b"));
    }

    [Fact]
    public void AHeldOrRepeatedVoice_IsNoParallel()
    {
        // b repeats C3 while s moves G4 → A4: fifth to sixth anyway, and the fifths G4/C3 → G4/C3 are one held sound.
        string src = Book("g'4 g' g' g' | g'1 |", "e'4 e' e' e' | e'1 |", "c'4 c' c' c' | c'1 |", "c4 c c c | c1 |");
        Assert.Empty(Found(src, DiagnosticCodes.ParallelPerfectIntervals));
    }

    [Fact]
    public void ARestBreaksTheLine()
    {
        // s: G4, rest, A4 over b: C3, D3, D3 — the fifths are not neighbours across the rest.
        string src = Book("g'4 r a' a' | a'1 |", QuietA, QuietT, "c4 d d d | d1 |");
        Assert.DoesNotContain(Found(src, DiagnosticCodes.ParallelPerfectIntervals), d => d.Message.Contains("s and b"));
    }

    [Fact]
    public void WithoutTheCheckLine_NothingIsSaid()
    {
        // Parallel fifths are ordinary in a pop arrangement: only a file that asks hears of them.
        string src = Book("e'4 f' g' a' | g'1 |", QuietA, QuietT, "c4 d c d | c1 |", check: "");
        Assert.DoesNotContain(All(src), d => d.Code is "LYS3002" or "LYS3003" or "LYS3004");
    }

    [Fact]
    public void ACrossedVoice_IsSaid()
    {
        // a rises to G4 over s's F4 on beat 3.
        string src = Book("e'4 f' f' e' | e'1 |", "c'4 d' g' c' | c'1 |", QuietT, QuietB);
        var found = Assert.Single(Found(src, DiagnosticCodes.VoiceCrossing));
        Assert.Contains("a (G4) is above s (F4)", found.Message);
    }

    [Fact]
    public void Spacing_AnOctaveBetweenUpperVoices_ATwelfthOverTheBass()
    {
        // s E5 over a C4: a tenth — too far. t G3 over b C2: a twelfth — allowed; over B1 past it.
        string src = Book("e''4 e' e' e' | e'1 |", QuietA, "g4 g g g | g1 |", "c,4 c b,, c | c1 |");
        var found = Found(src, DiagnosticCodes.VoiceSpacing);
        Assert.Contains(found, d => d.Message.StartsWith("s (E5) and a (C4) are more than an octave apart"));
        Assert.Contains(found, d => d.Message.StartsWith("t (G3) and b (B1) are more than a twelfth apart"));
        Assert.DoesNotContain(found, d => d.Message.Contains("(C2)"));
    }

    [Theory]
    [InlineData("check voiceLeding s a t b", "no check Lily# has")]
    [InlineData("check voiceLeading s a x b", "No part is named 'x'")]
    [InlineData("check voiceLeading s a s b", "named twice")]
    [InlineData("check voiceLeading s", "at least two parts")]
    [InlineData("check", "names the check to turn on")]
    public void ALineLilySharpCannotCarryOut_IsAnError(string line, string says)
    {
        var errors = All(Book(QuietS, QuietA, QuietT, QuietB, check: line)).Where(d => d.Code == DiagnosticCodes.CheckPragma).ToList();
        Assert.Contains(errors, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains(says));
    }

    [Fact]
    public void TheLineEndsAtItsLineEnd_AndCheckStaysAPartName()
    {
        // A top-level `clef` on the next line is not read as a part of the check.
        string src = Book(QuietS, QuietA, QuietT, QuietB, check: "check voiceLeading s a t b\nclef treble");
        Assert.DoesNotContain(All(src), d => d.Severity == DiagnosticSeverity.Error);
        // `check` is no keyword: a part may be named so.
        string named = "time 4/4\npart check { clef treble }\nsection A { check { c'1 | } }\nform { A }\nscore { staff check }\n";
        Assert.DoesNotContain(All(named), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TheCheckLine_ChangesNothingThatIsDrawn()
    {
        string with = Book(QuietS, QuietA, QuietT, QuietB);
        string without = Book(QuietS, QuietA, QuietT, QuietB, check: "");
        // The source positions behind the notes move by the line's length; nothing drawn does.
        static string Drawn(string src) => System.Text.RegularExpressions.Regex.Replace(
            SvgGenerator.Generate(SyntaxTree.Parse(src)), @" data-pos=""[^""]*""", "");
        Assert.Equal(Drawn(without), Drawn(with));
    }

    [Fact]
    public void RenamingAPart_ReachesTheCheckLine()
    {
        var tree = SyntaxTree.Parse(Book(QuietS, QuietA, QuietT, QuietB));
        var tokens = LilySharp.Core.Editing.PartReferenceFinder.Occurrences(tree.GetRoot(), "a");
        int line = tree.Text.IndexOf("check voiceLeading", System.StringComparison.Ordinal);
        Assert.Contains(tokens, t => t.Span.Start > line && t.Span.Start < tree.Text.IndexOf('\n', line));
    }

    // ---- stage 3: overlap and hidden fifths and octaves (§3) ----

    [Fact]
    public void AVoiceMovingPastItsNeighboursLastNote_Overlaps()
    {
        // s E4 → G4; a C4 → F4: above s's E4 just before, below its G4 now — not crossed.
        string src = Book("e'4 g' g' d' | e'1 |", "c'4 f' c' b | c'1 |", QuietT, QuietB);
        var overlap = Assert.Single(Found(src, DiagnosticCodes.VoiceOverlap));
        Assert.Contains("a moves to F4, above s's E4 just before", overlap.Message);
        Assert.Empty(Found(src, DiagnosticCodes.VoiceCrossing));
    }

    [Fact]
    public void OuterVoicesLeapingTheSameWayIntoAnOctave_AreHiddenOctaves()
    {
        // s E4 → C5 (a leap), b G2 → C3 (the same way): into two octaves.
        string leap = Book("e'4 c'' b' c'' | c''1 |", QuietA, QuietT, "g,4 c g, c | c1 |");
        Assert.Contains(Found(leap, DiagnosticCodes.HiddenPerfectInterval), d => d.Message.StartsWith("hidden octave: s leaps and b"));
        // The same arrival with the top voice stepping (B3 → C4) is the textbook's allowed case.
        string step = Book("b4 c' b c' | c'1 |", "g4 g g g | g1 |", "d4 e d e | e1 |", "g,4 c g, c | c1 |");
        Assert.Empty(Found(step, DiagnosticCodes.HiddenPerfectInterval));
    }

    // ---- stage 2: two voices on a staff (§1 ⒝ ⒞) ----

    /// <summary>A hymn on two staves, two voices each: <c>check voiceLeading upper lower</c>.</summary>
    private static string Hymn(string upper, string lower) => $"""
        time 4/4
        octave absolute
        part upper {"{"} clef treble {"}"}
        part lower {"{"} clef bass {"}"}
        check voiceLeading upper lower
        section A {"{"}
          upper {"{"} {upper} {"}"}
          lower {"{"} {lower} {"}"}
        {"}"}
        form {"{"} A {"}"}
        score {"{"} grandStaff {"{"} staff upper  staff lower {"}"} {"}"}
        """;

    private static List<string> Parallels(string src)
        => [.. Found(src, DiagnosticCodes.ParallelPerfectIntervals).OrderBy(d => d.Span.Start).Select(d => d.Message[(d.Message.IndexOf(" move") )..])];

    [Fact]
    public void TheThreeWaysToWriteFourVoices_FindTheSameParallels()
    {
        // The same music: s e f g a | g, a c c e f | e, t g a c c | c, b c F2 c d | c.
        string onePerPart = Book("e'4 f' g' a' | g'1 |", "c'4 c' e' f' | e'1 |", "g4 a c' c' | c'1 |", "c4 f, c d | c1 |");
        string branches = Hymn("voice { e'4 f' g' a' | g'1 | } { c'4 c' e' f' | e'1 | }", "voice { g4 a c' c' | c'1 | } { c4 f, c d | c1 | }");
        string chords = Hymn("<c' e'>4 <c' f'> <e' g'> <f' a'> | <e' g'>1 |", "<c g>4 <f, a> <c c'> <d c'> | <c c'>1 |");
        var expected = Parallels(onePerPart);
        Assert.Equal(2, expected.Count);
        Assert.Equal(expected, Parallels(branches));
        Assert.Equal(expected, Parallels(chords));
        Assert.Contains(Found(branches, DiagnosticCodes.ParallelPerfectIntervals), d => d.Message.StartsWith("parallel fifths: upper/1 and lower/2"));
        // A chord's squiggle is the whole chord.
        var onChord = Found(chords, DiagnosticCodes.ParallelPerfectIntervals).OrderBy(d => d.Span.Start).First();
        Assert.Equal("<f' a'>", chords.Substring(onChord.Span.Start, onChord.Span.Length));
    }

    [Fact]
    public void ANamedBranch_IsCalledByItsName()
    {
        string src = Hymn("voice sop { e'4 f' g' a' | g'1 | } alt { c'4 c' e' f' | e'1 | }",
                          "voice ten { g4 a c' c' | c'1 | } bas { c4 f, c d | c1 | }");
        Assert.Contains(Found(src, DiagnosticCodes.ParallelPerfectIntervals), d => d.Message.StartsWith("parallel fifths: sop and bas"));
    }

    [Fact]
    public void AUnisonWrittenOnce_MovesAsOneLine_NotAsParallels()
    {
        // s and a sing E4 then F4 together, written as one note each; the bass moves under them.
        string src = Hymn("<g' c''>4 e' f' <g' b'> | <g' c''>1 |", "<e c>4 <g c> <a d> <g g,> | <e c>1 |");
        Assert.DoesNotContain(Found(src, DiagnosticCodes.ParallelPerfectIntervals), d => d.Message.Contains("upper/1 and upper/2"));
    }

    [Fact]
    public void TheSecondBranchAboveTheFirst_IsACrossing()
    {
        string src = Hymn("voice { e'4 d' c' d' | e'1 | } { c'4 f' g c' | c'1 | }", "voice { g4 g e g | g1 | } { c4 g, c g, | c1 | }");
        var crossing = Assert.Single(Found(src, DiagnosticCodes.VoiceCrossing));
        Assert.Contains("upper/2 (F4) is above upper/1 (D4)", crossing.Message);
    }

    [Fact]
    public void ARareDivisi_KeepsAPartOneVoice()
    {
        // One chord in the soprano's eight onsets: still one voice (its top note), so four voices.
        string src = Book("e'4 d' c' d' | <c' e'>1 |", QuietA, QuietT, QuietB);
        Assert.DoesNotContain(All(src), d => d.Message.Contains("s/1") || d.Message.Contains("s/2"));
    }

    [Fact]
    public void QuietVoices_SayNothing()
    {
        Assert.DoesNotContain(All(Book(QuietS, QuietA, QuietT, QuietB)), d => d.Code.StartsWith("LYS300"));
    }
}
