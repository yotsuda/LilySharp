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
using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// "Split Sections to Match a Part" (<see cref="SectionSplitter"/>): one part has cut a section
/// in two, the others still write it whole; the others are cut at the same bars, the forms play
/// the new section where they played the old one, and the rewrite is checked — every cut part
/// sounds exactly as it did — before it is returned.
/// </summary>
/// <remarks>
/// Each accepted split is held to the MIDI here as well (part by part, the parts that were cut),
/// independently of the splitter's own check; the poison rows show the two halves catch each
/// other: with the octave fix off the check refuses, and with the check off too the result is
/// wrong in exactly the way the check says.
/// </remarks>
[Trait("Category", "Unit")]
public class SectionSplitterTests
{
    /// <summary>The owner's case in miniature (2026-09-28): vn1 has split A into A + B; vn2, va
    /// and vc still write all four bars in A, and so do a chord row and a lyrics track. Relative
    /// octaves, a running eighth-note value, a meter and a key change before the cut, and a form
    /// that repeats A.</summary>
    private const string Owner = """
        key g major

        part vn1 {
          clef treble
          section A {
            g'4 a b c | time 2/2 d2 d |
          }
          section B {
            time 2/2
            e4 d c b | a1 |
          }
        }
        part vn2 {
          clef treble
          section A {
            d'4 e fis g | time 2/2 key d major a2 a8 a a a |
            b c d e fis g a b | a1 |
          }
        }
        part va {
          clef alto
          section A {
            b4 c d e | time 2/2 fis2 fis4 fis |
            g4( a) b c | d1 |
          }
        }
        part vc {
          clef bass
          octave 3
          section A {
            g1 | time 2/2 d'1 |
            R1 | g,1 |
          }
        }
        chords prog {
          section A { G | D | Em | C | }
        }
        lyrics words sings vn2 {
          section A { la la la la | la la la la la | la la la la la la la la | la | }
        }

        form main { |: A :| }
        score main { chords prog staff vn1 staff vn2 lyrics words staff va staff vc }

        """;

    private const string OwnerSplit = """
        key g major

        part vn1 {
          clef treble
          section A {
            g'4 a b c | time 2/2 d2 d |
          }
          section B {
            time 2/2
            e4 d c b | a1 |
          }
        }
        part vn2 {
          clef treble
          section A {
            d'4 e fis g | time 2/2 key d major a2 a8 a a a |
          }
          section B {
            time 2/2 key d major
            b''8 c d e fis g a b | a1 |
          }
        }
        part va {
          clef alto
          section A {
            b4 c d e | time 2/2 fis2 fis4 fis |
          }
          section B {
            time 2/2
            g'4( a) b c | d1 |
          }
        }
        part vc {
          clef bass
          octave 3
          section A {
            g1 | time 2/2 d'1 |
          }
          section B {
            time 2/2
            R1 | g1 |
          }
        }
        chords prog {
          section A { G | D | }
          section B { Em | C | }
        }
        lyrics words sings vn2 {
          section A { la la la la | la la la la la | }
          section B { la la la la la la la la | la | }
        }

        form main { |: A B :| }
        score main { chords prog staff vn1 staff vn2 lyrics words staff va staff vc }

        """;

    private static string Lf(string s) => s.ReplaceLineEndings("\n");

    /// <summary>The splitter over the text with LF line ends (the file's own ends are kept, and
    /// the expected snippets below are written with LF).</summary>
    private static SectionSplitter.Result Split(string source, string? section = null, string? reference = null,
        SectionSplitter.Options? options = null)
        => SectionSplitter.Split(Lf(source), section, reference, options ?? new SectionSplitter.Options());

    /// <summary>Each part's notes (start, key, length), sorted.</summary>
    private static Dictionary<string, List<(int, int, int)>> Notes(string source)
        => new MidiExporter().Export(SyntaxTree.Parse(source)).Tracks
            .SelectMany(t => t.Notes)
            .GroupBy(n => n.Part ?? "")
            .ToDictionary(g => g.Key, g => g.Select(n => (n.StartTick, n.Pitch, n.DurationTicks)).OrderBy(x => x).ToList());

    private static string Lyrics(string source)
        => string.Join(" ", new MidiExporter().Export(SyntaxTree.Parse(source)).Tracks
            .SelectMany(t => t.Lyrics).OrderBy(l => l.Tick).Select(l => $"{l.Tick}:{l.Text}"));

    private static List<Diagnostic> LengthWarnings(string source)
        => SemanticValidation.Run(SyntaxTree.Parse(source))
            .Where(d => d.Code == DiagnosticCodes.SectionBarCountMismatch).ToList();

    private static void AssertSoundsTheSame(string before, string after, params string[] parts)
    {
        var b = Notes(before);
        var a = Notes(after);
        foreach (var p in parts)
            Assert.Equal(b[p], a[p]);
    }

    [Fact]
    public void TheOwnersCase_CutsEveryOtherPartAtTheSameBar_AndSoundsTheSame()
    {
        Assert.Single(LengthWarnings(Owner));
        var result = Split(Owner);
        Assert.True(result.NewText != null, result.Error);
        Assert.Equal(Lf(OwnerSplit), result.NewText);

        // The witness of our own: the parts that were cut sound exactly as before, the words
        // fall on the same notes, and the warning is gone.
        AssertSoundsTheSame(Owner, result.NewText!, "vn2", "va", "vc", "prog (chords)");
        Assert.Equal(Lyrics(Owner), Lyrics(result.NewText!));
        Assert.Empty(LengthWarnings(result.NewText!));

        Assert.Equal("""
            Follow vn1: A 2 + B 2 bars.
            Split A in vn2, va and vc after bar 2 → A, B.
            Also split chords prog and lyrics words.
            Form main: A → A B.
            At the cuts: octave marks on 3 note(s); a written duration on 1 note(s); restated time 2/2 (vn2 B), key d major (vn2 B), time 2/2 (va B), time 2/2 (vc B).
            Checked: vn2, va, vc, chords prog and lyrics words sound and count exactly as before, no error is new, and the length warning on A is gone.
            Heard at other times now that the sections are no longer padded to the longest part (their notes are not changed): vn1.
            """.ReplaceLineEndings("\n"), result.Plan);
    }

    [Fact]
    public void TheOffer_IsFoundWithoutCompiling_AndNamesThePartToFollow()
    {
        var offer = Assert.Single(SectionSplitter.FindOffers(Lf(Owner)));
        Assert.Equal("A", offer.Section);
        Assert.Equal(4, offer.Length);
        Assert.False(offer.NeedsChoice);
        Assert.Equal("vn1: A 2 + B 2 bars", $"{offer.Candidates[0].Part}: {offer.Candidates[0].Describe()}");
    }

    /// <summary>A meter that changes the bar's LENGTH is carried too (and a chord row is left
    /// out: its bars follow the score meter, not a part's change — a mismatch of its own).</summary>
    [Fact]
    public void AShorterMeterInForceAtTheCut_IsRestatedInTheNewSection()
    {
        const string src = """
            part fl {
              section A { c'4 d e f | time 3/4 g2 g4 | }
              section B { time 3/4 a2. | b2. | }
            }
            part ob {
              section A { e4 f g a | time 3/4 b2 b4 | c2. | d2. | }
            }
            form main { A }
            score main { staff fl staff ob }
            """;
        var result = Split(src);
        Assert.True(result.NewText != null, result.Error);
        Assert.Contains("section A { e4 f g a | time 3/4 b2 b4 | }\n  section B { time 3/4 c'2. | d2. | }", result.NewText);
        Assert.Contains("form main { A B }", result.NewText);
        AssertSoundsTheSame(src, result.NewText!, "ob");
    }

    /// <summary>The clef reverts to the part's own at a section boundary, and the octave mode to
    /// the file's: both are restated when the cell changed them before the cut.</summary>
    [Fact]
    public void AClefAndAnOctaveModeInForceAtTheCut_AreRestated()
    {
        const string src = """
            part fl {
              section A { c''1 | d1 | }
              section B { e1 | f1 | }
            }
            part vc {
              clef treble
              section A { c'1 | clef bass octave absolute c1 | d1 | e1 | }
            }
            form main { A }
            score main { staff fl staff vc }
            """;
        var result = Split(src);
        Assert.True(result.NewText != null, result.Error);
        Assert.Contains("section B { clef bass octave absolute d1 | e1 | }", result.NewText);
        AssertSoundsTheSame(src, result.NewText!, "vc");
    }

    /// <summary>The other spelling of the tracks in a file grouped by part: a top-level
    /// <c>section A { … }</c> holding the chord row and the words beside its header. Their second
    /// half moves into a new top-level <c>section B</c>, and the header's key — which B's own
    /// declaration does not state — is restated in the part that was cut.</summary>
    [Fact]
    public void TracksInATopLevelSection_MoveIntoANewTopLevelSection()
    {
        const string src = """
            part fl {
              section A { c'4 d e f | g1 | }
              section B { a1 | b1 | }
            }
            part ob {
              section A { e'4 f g a | b1 | c1 | d1 | }
            }
            section A {
              key g major
              chords prog { G | D | Em | C | }
              lyrics words sings ob { la la la la | la | la | la | }
            }
            form main { A }
            score main { chords prog staff fl staff ob lyrics words }
            """;
        var result = Split(src);
        Assert.True(result.NewText != null, result.Error);
        Assert.Contains("section B { key g major c''1 | d1 | }", result.NewText);
        Assert.Contains("""
            section A {
              key g major
              chords prog { G | D | }
              lyrics words sings ob { la la la la | la | }
            }
            section B {
              chords prog { Em | C | }
              lyrics words sings ob { la | la | }
            }
            form main { A B }
            """.ReplaceLineEndings("\n"), result.NewText);
        AssertSoundsTheSame(src, result.NewText!, "ob", "prog (chords)");
        Assert.Equal(Lyrics(src), Lyrics(result.NewText!));
    }

    // --- choosing the part to follow ---------------------------------------------

    private const string TwoWays = """
        part fl {
          section A { c'1 | d1 | }
          section B { e1 | f1 | }
        }
        part ob {
          section A { c'1 | }
          section X { d1 | e1 | f1 | }
        }
        part cl {
          section A { c'1 | d1 | e1 | f1 | }
        }
        form main { A }
        score main { staff fl staff ob staff cl }
        """;

    [Fact]
    public void TwoPartsThatSubdivideDifferently_AskWhichToFollow()
    {
        var result = Split(TwoWays);
        Assert.Null(result.NewText);
        Assert.Null(result.Error);
        Assert.True(result.Offer!.NeedsChoice);
        Assert.Equal(["fl: A 2 + B 2 bars", "ob: A 1 + X 3 bars"],
            result.Offer.Candidates.Select(c => $"{c.Part}: {c.Describe()}"));

        var followA = Split(TwoWays, "A", "fl");
        Assert.True(followA.NewText != null, followA.Error);
        Assert.Contains("section A { c'1 | d1 | }\n  section B { e1 | f1 | }", followA.NewText);
        Assert.Contains("form main { A B }", followA.NewText);
        AssertSoundsTheSame(TwoWays, followA.NewText!, "cl");
        // ob's A stays one bar (it subdivides A its own way): said, not left to be found.
        Assert.Contains("Still not the same length: Section 'A' is not the same length everywhere it is written: "
            + "2 bar(s) in part 'fl' and part 'cl'; 1 bar(s) in part 'ob' — no part subdivides it the same way; "
            + "nothing here splits it.", followA.Plan);

        // Following ob cuts fl's A into A + X — and fl then subdivides X (X 1 + B 2), which is
        // no longer in question, so the plan goes on to cut ob's and cl's X the same way: no
        // length is left different anywhere.
        var followB = Split(TwoWays, "A", "ob");
        Assert.True(followB.NewText != null, followB.Error);
        Assert.Contains("section A { c'1 | }\n  section X { d1 | }\n  section B { e1 | f1 | }", followB.NewText);
        Assert.Contains("form main { A X B }", followB.NewText);
        Assert.Empty(LengthWarnings(followB.NewText!));
        AssertSoundsTheSame(TwoWays, followB.NewText!, "cl");
    }

    [Fact]
    public void TwoPartsThatAgree_FollowTheFirstWithoutAsking()
    {
        const string src = """
            part fl {
              section A { c'1 | }
              section B { d1 | }
            }
            part ob {
              section A { e'1 | }
              section B { f1 | }
            }
            part cl {
              section A { g'1 | a1 | }
            }
            form main { A B }
            score main { staff fl staff ob staff cl }
            """;
        var result = Split(src);
        Assert.True(result.NewText != null, result.Error);
        Assert.StartsWith("Follow fl: A 1 + B 1 bars.", result.Plan);
        Assert.Contains("section A { g'1 | }\n  section B { a'1 | }", result.NewText);
        // The form already plays B after A: left as it is.
        Assert.Contains("form main { A B }", result.NewText);
        Assert.Contains("Form main already plays A B.", result.Plan);
        AssertSoundsTheSame(src, result.NewText!, "cl");
    }

    // --- forms ---------------------------------------------------------------------

    [Fact]
    public void EveryPlayInEveryForm_TakesTheNewSection_WithItsMarksAndTilde()
    {
        const string src = """
            part fl {
              section A { c'1 | }
              section B { d1 | }
              section C { e1 | }
            }
            part ob {
              section A { g'1 | a1 | }
              section C { b1 | }
            }
            form main { A C ~A' |: A :| }
            form short { A }
            score main { staff fl staff ob }
            score short "short" { staff fl }
            """;
        var result = Split(src);
        Assert.True(result.NewText != null, result.Error);
        Assert.Contains("form main { A B C ~A' ~B' |: A B :| }", result.NewText);
        Assert.Contains("form short { A B }", result.NewText);
        Assert.Contains("Form main: A → A B (3 places).", result.Plan);
        AssertSoundsTheSame(src, result.NewText!, "ob");
    }

    // --- refusals ------------------------------------------------------------------

    private static string Refusal(string src, string? section = null, string? reference = null)
    {
        var result = Split(src, section, reference);
        Assert.Null(result.NewText);
        Assert.NotNull(result.Error);
        return result.Error!;
    }

    private const string Frame = """
        part fl {
          section A { c'1 | d1 | }
          section B { e1 | f1 | }
        }
        part ob {
          section A { MUSIC }
        }
        form FORM
        score main { staff fl staff ob }
        """;

    private static string With(string music, string form = "main { A }")
        => Frame.Replace("MUSIC", music).Replace("FORM", form);

    // A slur, phrasing slur, tie or hairpin across a cut is KEPT (the section carry rule,
    // 2026-09-28): every form plays the new section right after the old one, so the span is
    // carried into it — as long as it ends there.

    [Fact]
    public void ATieAcrossTheCut_IsKept_AndSoundsTheSame()
    {
        string src = With("g'1 | g1~ | g1 | a1 |");
        var result = Split(src);
        Assert.True(result.NewText != null, result.Error);
        Assert.Contains("g1~ | }\n  section B {", Lf(result.NewText!));
        AssertSoundsTheSame(src, result.NewText!, "ob");
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(result.NewText!)),
            d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary or DiagnosticCodes.TieTargetMismatch);
    }

    [Fact]
    public void ASlurAcrossTheCut_IsKept()
    {
        string src = With("g'1 | g1( | a1) | a1 |");
        var result = Split(src);
        Assert.True(result.NewText != null, result.Error);
        Assert.Contains("g1( | }\n  section B {", Lf(result.NewText!));
        Assert.Contains("1) |", result.NewText);
        AssertSoundsTheSame(src, result.NewText!, "ob");
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(result.NewText!)),
            d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary or DiagnosticCodes.UnpairedSlur);
    }

    [Fact]
    public void ASlurStillOpenAtTheEndOfTheNewSection_IsReported()
    {
        // Carried into B and never ended there: the carry rule would refuse it.
        var error = Refusal(With("g'1 | g1( | a1 | a1 |"));
        Assert.Contains("a slur (opened line 6) runs across the cut and is still open at the end of the new section B", error);
    }

    [Fact]
    public void AHairpinEndedInTheNewSection_IsKept()
    {
        string src = With("g'1@p | g1@cresc | a1@f | a1 |");
        var result = Split(src);
        Assert.True(result.NewText != null, result.Error);
        AssertSoundsTheSame(src, result.NewText!, "ob");
    }

    [Fact]
    public void AHairpinNothingEndsInTheNewSection_IsReported()
    {
        var error = Refusal(With("g'1@p | g1@cresc | a1 | a1 |"));
        Assert.Contains("a hairpin (@cresc line 6, with no dynamic before the cut) runs across the cut and is still open", error);
    }

    [Fact]
    public void AManualBeamAcrossTheCut_IsStillReported()
    {
        var error = Refusal(With("g'1 | g2 g4 g8[ g8 | a8] a8 a4 a2 | a1 |"));
        Assert.Contains("a manual beam (opened line 6) runs across the cut.", error);
    }

    [Fact]
    public void ACutThatFallsMidBar_IsReported()
    {
        // b's bars are longer than a's: after two of them it is a bar and a half further on.
        var error = Refusal(With("g'1. | g1. | a1 | a1 |"));
        Assert.Contains("ob, section A, after bar 2", error);
        Assert.Contains("the cut would fall mid-bar here", error);
    }

    [Fact]
    public void ACutInsideAMultiBarRest_IsReported()
    {
        var error = Refusal(With("g'1 | R1*2 | a1 |"));
        Assert.Contains("no bar line of its own ends that bar", error);
    }

    private const string EndingFrame = """
        part fl {
          section A { c'1 | d1 | }
          section B { e1 | f1 | }
          section C { g1 | }
        }
        part ob {
          section A { g'1 | g1 | a1 | a1 | }
          section C { c'1 | }
        }
        form FORM
        score main { staff fl staff ob }
        """;

    /// <summary>A section played as a repeat ending splits like any other play: the ending
    /// holds both halves ([1. A] → [1. A B]), under the one bracket. Until endings could hold
    /// several sections (2026-09-28) this was refused.</summary>
    [Theory]
    [InlineData("main { |: C [1. A] :| [2. C] }", "|: C [1. A B] :| [2. C]")]
    [InlineData("main { |: C [1. A :| [2. C] }", "|: C [1. A B :| [2. C]")]
    [InlineData("main { |: C [1. C] :| [2. A] }", "|: C [1. C] :| [2. A B]")]
    // An open-ended last ending (`-]`, owner's design 2026-09-28) holds every section before
    // its `-]` like a `]` one, so it splits the same way — the open end stays.
    [InlineData("main { |: C [1. C] :| [2. A -] }", "|: C [1. C] :| [2. A B -]")]
    public void TheSectionAsARepeatEnding_BecomesATwoSectionEnding(string form, string expected)
    {
        string src = EndingFrame.Replace("FORM", form);
        var result = Split(src);
        Assert.True(result.NewText != null, result.Error);
        Assert.Contains(expected, result.NewText);
        AssertSoundsTheSame(src, result.NewText!, "ob");
    }

    [Fact]
    public void AFileGroupedBySection_IsNotSupportedYet()
    {
        const string src = """
            part fl
            part ob
            section A {
              fl { c'1 | }
              ob { g'1 | a1 | }
            }
            form main { A }
            """;
        Assert.Contains("not supported yet for a file grouped by section", Refusal(src));
    }

    [Fact]
    public void ANewSectionOpeningWithQ_IsRefused()
    {
        var error = Refusal(With("<g' b>1 | q1 | q1 | q1 |"));
        Assert.Contains("would open with 'q'", error);
    }

    // --- a part that holds several of the reference's sections in one ------------------

    /// <summary>The owner's run in miniature (report 2026-09-28): fl writes A B C; ob writes A
    /// and a two-bar B that holds B + C; db writes everything in A, with a slur over the B/C
    /// boundary. Splitting B used to cut ob, say nothing about db, and be applied — leaving A
    /// three bars long in db. Now the plan continues into A (db must be cut to follow fl too),
    /// and db's slur — which refused the whole thing until the section carry rule — is kept,
    /// carried from B into C, whichever section the plan was started from.</summary>
    private const string Sequence = """
        part fl {
          section A { c'1 | }
          section B { d1 | }
          section C { e1 | }
        }
        part ob {
          section A { e'1 | }
          section B { f1 | g1 | }
        }
        part db {
          clef bass
          octave 3
          section A { c'1 | d1( | e1) | }
        }
        form main { A B C }
        score main { staff fl staff ob staff db }
        """;

    [Fact]
    public void SplittingB_ContinuesIntoTheLongPartsA_AndKeepsItsSlur()
    {
        var result = Split(Sequence, "B");
        Assert.True(result.NewText != null, result.Error);
        Assert.Contains("Split A in db after bars 1, 2 → A, B, C.", result.Plan);
        Assert.Contains("section B { d'1( | }\n  section C { e'1) | }", Lf(result.NewText!));
        AssertSoundsTheSame(Sequence, result.NewText!, "db");
        Assert.Empty(LengthWarnings(result.NewText!));
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(result.NewText!)),
            d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary or DiagnosticCodes.UnpairedSlur);

        // From A (following either part) it is the same file.
        foreach (var follow in new[] { "fl", "ob" })
        {
            var fromA = Split(Sequence, "A", follow);
            Assert.True(fromA.NewText != null, fromA.Error);
            Assert.Equal(result.NewText, fromA.NewText);
        }
    }

    /// <summary>The owner's file as it is now: every part but db has A B C, db has them all in A,
    /// with a TIE over the A/B cut and a SLUR over the B/C cut (bohemian-rhapsody.lys's double
    /// bass carries a slur over its C/D boundary). Both are carried into the section that follows
    /// and end there, so the split is made, and db sounds exactly as it did — the tie included,
    /// which MIDI re-attacked across a section boundary in a book of more than one part until
    /// its tie memory was kept per part.</summary>
    [Fact]
    public void TheLongPartAlone_IsSplit_WithItsTieAndSlurCarried()
    {
        string now = Lf(Sequence).Replace("section B { f1 | g1 | }", "section B { f1 | }\n  section C { g1 | }")
            .Replace("section A { c'1 | d1( | e1) | }", "section A { c'1~ | c1( | d1) | }");
        var result = Split(now, "A");
        Assert.True(result.NewText != null, result.Error);
        Assert.Contains("section A { c'1~ | }\n  section B { c'1( | }\n  section C { d'1) | }", Lf(result.NewText!));
        AssertSoundsTheSame(now, result.NewText!, "db");
        Assert.Equal(result.NewText, Split(now, "B").NewText);
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(result.NewText!)),
            d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary or DiagnosticCodes.UnpairedSlur
                or DiagnosticCodes.TieTargetMismatch);
    }

    /// <summary>The same without the slur: one plan cuts ob's B and db's A, each part still sounds
    /// as it did, and no length warning is left anywhere.</summary>
    [Fact]
    public void WithoutTheSlur_OnePlanCutsEveryLongPart()
    {
        string clean = Sequence.Replace("d1( | e1)", "d1 | e1");
        var result = Split(clean, "B");
        Assert.True(result.NewText != null, result.Error);
        Assert.Equal("""
            Follow fl: B 1 + C 1 bars.
            Split B in ob after bar 1 → B, C.
            Form main already plays B C.
            At the cuts: octave marks on 1 note(s).
            Follow fl: A 1 + B 1 + C 1 bars.
            Split A in db after bars 1, 2 → A, B, C.
            Form main already plays A B C.
            At the cuts: octave marks on 2 note(s).
            Checked: db sounds and counts exactly as before, ob keeps every note it played, no error is new, and the length warnings on B and A are gone.
            Heard at other times now that the sections are no longer padded to the longest part (their notes are not changed): fl and ob.
            """.ReplaceLineEndings("\n"), result.Plan);
        Assert.Contains("section B { f1 | }\n  section C { g'1 | }", result.NewText);
        Assert.Contains("section A { c'1 | }\n  section B { d'1 | }\n  section C { e'1 | }", result.NewText);
        // db sounds as it did. ob's B came after db's three bars of A before (the file was out of
        // step), and now after one — which the plan says.
        AssertSoundsTheSame(clean, result.NewText!, "db");
        Assert.Empty(LengthWarnings(result.NewText!));

        // From A (after choosing either part) it is the same file.
        foreach (var follow in new[] { "fl", "ob" })
        {
            var fromA = Split(clean, "A", follow);
            Assert.True(fromA.NewText != null, fromA.Error);
            Assert.Equal(result.NewText, fromA.NewText);
        }
    }

    [Fact]
    public void WithoutTheOctaveFix_TheContinuedStepIsCaughtToo()
    {
        string clean = Sequence.Replace("d1( | e1)", "d1 | e1");
        var noFix = Split(clean, "B", null, new SectionSplitter.Options(FixOctaves: false));
        Assert.Null(noFix.NewText);
        Assert.Contains("would change the music", noFix.Error);
    }

    // --- the check catches a wrong rewrite -------------------------------------------

    [Fact]
    public void WithoutTheOctaveFix_TheCheckRefuses_AndWithoutTheCheckTheResultIsWrong()
    {
        // Poison: skip the re-marking — vn2's B opens with a bare b8, which the fresh frame
        // puts two octaves below where it sounded.
        var noFix = Split(Owner, null, null, new SectionSplitter.Options(FixOctaves: false));
        Assert.Null(noFix.NewText);
        Assert.Contains("would change the music", noFix.Error);
        Assert.Contains("part vn2", noFix.Error);

        // And the check is what stood between that and the file: bypass it too and the text
        // comes back, sounding wrong (the assertion the accepted split passes goes red here).
        var unchecked_ = Split(Owner, null, null,
            new SectionSplitter.Options(FixOctaves: false, Verify: false));
        Assert.NotNull(unchecked_.NewText);
        Assert.NotEqual(Notes(Owner)["vn2"], Notes(unchecked_.NewText!)["vn2"]);
    }

    /// <summary>
    /// A new section opening on a bare <c>R</c> stamps the running duration on the first note
    /// after it: the rest lasts its bar and neither reads nor moves that duration
    /// (Music.BarRest, 2026-10-02). Stamped on the rest, <c>R8</c> would be an eighth.
    /// </summary>
    [Fact]
    public void ACutBeforeABareBarRest_StampsTheNoteAfterIt()
    {
        const string source = """
            octave absolute
            part vn1 {
              clef treble
              section A { c'1 | }
              section B { R | c'1 | }
            }
            part vn2 {
              clef treble
              section A { c'8 d' e' f' g' a' b' c'' | R | d' e' f' g' a' b' c'' d'' | }
            }
            form main { A B }
            score main { staff vn1 staff vn2 }

            """;
        var result = Split(source);
        Assert.True(result.NewText != null, result.Error);
        Assert.Contains("R | d'8 e'", result.NewText);
        AssertSoundsTheSame(source, result.NewText!, "vn2");
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(result.NewText!)));
    }
}
