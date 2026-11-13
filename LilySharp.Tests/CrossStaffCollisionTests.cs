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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A note <c>@cross</c> moved to the other staff that runs into that staff's own notes on the
/// laid-out page is reported at the note (LYS4078; owner's decision 2026-11-13): the page draws
/// what is written, as LilyPond does, and the writer is told what the note met and the ways out.
/// </summary>
[Trait("Category", "Unit")]
public class CrossStaffCollisionTests
{
    private static string Book(string rh, string lh) => $$"""
        octave absolute
        time 4/4
        key d major
        part pr "Piano" { instrument piano-right }
        part pl { instrument piano-left }
        section A {
          pr { {{rh}} }
          pl { {{lh}} }
        }
        form { ~A }
        score { grandStaff { staff pr  staff pl } }
        """;

    /// <summary>The owner's left hand: its fis', a', fis' are drawn on the right hand's staff.</summary>
    private const string LeftHand = "d8 a d' fis'@cross a'@cross fis'@cross d' a | g,8 d g b d' b g d |";

    private static System.Collections.Generic.List<Diagnostic> Warnings(string book)
        => SemanticValidation.RunPageChecks(SyntaxTree.Parse(book))
            .Where(d => d.Code == DiagnosticCodes.CrossStaffRunsIntoOtherStaff).ToList();

    private const string OwnersRightHand = "<fis' a' fis''>4 e''8 d'' a'2 | <g' b' g''>4 fis''8 e'' b'2 |";

    [Fact]
    public void TheOwnersBar_SaysOnceThatTwoOfItsThreeMovedNotesCollide()
    {
        string book = Book(OwnersRightHand, LeftHand);

        // The first fis' stands under the right hand's eighth beam (its stem runs through it,
        // its head against that beam's stem), the a' against the right hand's half note; the
        // second fis' meets nothing. One bar of one part: one warning, at the first of them.
        var only = Assert.Single(Warnings(book));
        Assert.Equal(DiagnosticSeverity.Warning, only.Severity);
        Assert.StartsWith("2 of this bar's 3 notes drawn on the other staff ('@cross') run into that staff's own notes "
            + "(noteheads, beams, stems), fis' first - ", only.Message);
        Assert.Contains("drop '@cross'", only.Message);
        Assert.Contains("'@stemUp' / '@stemDown'", only.Message);
        Assert.Contains("change the clef", only.Message);
        // These strings reach legacy-codepage consoles.
        Assert.All(only.Message, c => Assert.InRange(c, ' ', '~'));

        // The span is the note's, annotation and all, so the editor underlines the note.
        Assert.Equal("fis'@cross", book.Substring(only.Span.Start, only.Span.Length));
        Assert.Equal(book.IndexOf("fis'@cross", System.StringComparison.Ordinal), only.Span.Start);
    }

    [Fact]
    public void ALoneCollidingNote_KeepsItsOwnSentence()
    {
        // Only the first fis' is moved: it alone meets the beam.
        var only = Assert.Single(Warnings(Book(OwnersRightHand, LeftHand.Replace("a'@cross fis'@cross", "a' fis'"))));
        Assert.StartsWith("fis' is drawn on the other staff ('@cross') and runs into a beam of that staff's own notes - ", only.Message);
    }

    [Theory]
    [InlineData(5, 5)]   // up to five bars of a part: a warning a bar
    [InlineData(6, 1)]   // more: one, at the first, counting the others
    public void APartThatCollidesBarAfterBar_IsFoldedIntoOneWarning(int bars, int warnings)
    {
        string Times(string bar) => string.Join(" ", Enumerable.Repeat(bar, bars));
        string book = Book(Times("<fis' a' fis''>4 e''8 d'' a'2 |"), Times("d8 a d' fis'@cross a'@cross fis'@cross d' a |"));
        var found = Warnings(book);
        Assert.Equal(warnings, found.Count);
        Assert.Equal(book.IndexOf("fis'@cross", System.StringComparison.Ordinal), found[0].Span.Start);
        if (warnings == 1)
            Assert.Contains("fis' first, and so do notes in 5 more bars of this part (noteheads, beams, stems) - ", found[0].Message);
        else
            Assert.DoesNotContain("more bars", found[0].Message);
    }

    [Fact]
    public void TheKeystrokePass_DoesNotLayTheScoreOut()
    {
        // SemanticValidation.Run is what the language server runs after every settled
        // keystroke: the page check is not one of its validators, and it lays nothing out.
        Assert.DoesNotContain(SemanticValidation.CreateAll(), v => v is CrossStaffCollisionValidator);
        var tree = SyntaxTree.Parse(Book(OwnersRightHand, LeftHand));
        int before = CrossStaffCollisionValidator.Layouts;
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.CrossStaffRunsIntoOtherStaff);
        Assert.Equal(before, CrossStaffCollisionValidator.Layouts);

        // The page checks lay it out once; a file with no @cross, or whose score moves no
        // note, never.
        Assert.True(SemanticValidation.HasPageChecks(tree));
        Assert.Single(SemanticValidation.RunPageChecks(tree));
        Assert.Equal(before + 1, CrossStaffCollisionValidator.Layouts);
        var plain = SyntaxTree.Parse(Book(OwnersRightHand, LeftHand.Replace("@cross", "")));
        Assert.False(SemanticValidation.HasPageChecks(plain));
        Assert.Empty(SemanticValidation.RunPageChecks(plain));
        Assert.Equal(before + 1, CrossStaffCollisionValidator.Layouts);
    }

    [Fact]
    public void ACancelledPageCheck_IsGivenUpBeforeItLaysOut()
    {
        using var cancelled = new System.Threading.CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<System.OperationCanceledException>(() =>
            SemanticValidation.RunPageChecks(SyntaxTree.Parse(Book(OwnersRightHand, LeftHand)), cancelled.Token));
    }

    [Fact]
    public void TheEditor_GetsItFromThePagePass_NotFromTheKeystrokePass()
    {
        var uri = new System.Uri("file:///crosswarn.lys");
        var server = new LilySharp.Lsp.LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem
            {
                Uri = uri, Text = Book(OwnersRightHand, LeftHand), Version = 1, LanguageId = "lilysharp",
            },
        });
        var doc = server.DocumentAt(uri)!;
        // Closed at once: the passes didOpen scheduled are cancelled, so no page pass of this
        // server lays anything out behind the other tests' backs (Layouts is the process's).
        server.DidClose(new LilySharp.Lsp.Protocol.DidCloseTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
        });
        Assert.DoesNotContain(server.DocumentDiagnostics(doc, System.Threading.CancellationToken.None, out _),
            d => d.Code == DiagnosticCodes.CrossStaffRunsIntoOtherStaff);
        var page = Assert.Single(LilySharp.Lsp.LilySharpLanguageServer.PageDiagnostics(doc, System.Threading.CancellationToken.None));
        Assert.Equal(DiagnosticCodes.CrossStaffRunsIntoOtherStaff, page.Code);
        // The idle the pass waits is longer than the keystroke pass's debounce.
        Assert.True(LilySharp.Lsp.LilySharpLanguageServer.PageChecksIdleMs >= 1000);
    }

    [Fact]
    public void WhereTheRightHandIsSilentThere_NothingIsReported()
    {
        // The same left hand under a right hand that plays its chord and then nothing.
        Assert.Empty(Warnings(Book("<fis' a' fis''>4 s2. | <g' b' g''>4 fis''8 e'' b'2 |", LeftHand)));
    }

    [Fact]
    public void ARestInTheWay_IsNamed()
    {
        // The right hand's half rest stands where the moved a' is drawn: its stem runs through it.
        var only = Assert.Single(Warnings(Book("<fis' a' fis''>4 r4 r2 | <g' b' g''>4 fis''8 e'' b'2 |", LeftHand)));
        Assert.StartsWith("a' is drawn on the other staff ('@cross') and runs into a rest", only.Message);
    }

    [Theory]
    // Clean cross-staff passages of the cross-staff tests (CrossStaffTests' song with piano,
    // CrossStaffWarningTests' pair, CrossStaffDrawingTests' books): the note moves into room.
    [InlineData("""
        time 4/4
        part voice_ { clef treble }
        part rh { clef treble }
        part lh { clef bass }
        section A {
          voice_ { c''1 | }
          rh { c''2 c''2 | }
          lh { g'4@cross e'4@cross c4 e4 | }
        }
        form { A }
        score {
          staff voice_
          grandStaff {
            staff rh
            staff lh
          }
        }
        """)]
    [InlineData("""
        octave absolute
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { e''2@cross a,,2@cross | } lh { c,1 | } }
        form { A }
        score { grandStaff { staff rh staff lh } }
        """)]
    [InlineData("""
        octave absolute
        time 4/4
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { s1 | } lh { c8 e g@cross c' r2 | } }
        form { ~A }
        score { grandStaff { staff rh staff lh } }
        """)]
    [InlineData("""
        octave absolute
        time 4/4
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { r1 | r1 | } lh { c1 | c''4@cross r2. | } }
        form { ~A }
        score { grandStaff { staff rh staff lh } }
        """)]
    public void ACleanCrossStaffPassage_IsNotReported(string book)
        => Assert.Empty(Warnings(book));

    [Fact]
    public void ARepeatedSection_SaysItOnce()
    {
        string book = Book("<fis' a' fis''>4 e''8 d'' a'2 | <g' b' g''>4 fis''8 e'' b'2 |", LeftHand)
            .Replace("form { ~A }", "form { ~A ~A }");
        Assert.Single(Warnings(book));
    }

    [Fact]
    public void TouchingHairlinesAreNotACollision_AndABeamIsReadWhereTheInkStands()
    {
        // A stem (0.13 wide) ending 0.05 from a beam's edge, and one running 0.5 into it.
        double[] beam = [0, 10, 4, 10.5];
        Assert.False(CrossStaffCollisionValidator.Meets([1, 4, 1.13, 9.95], beam));
        Assert.False(CrossStaffCollisionValidator.Meets([1, 4, 1.13, 10.05], beam));
        Assert.True(CrossStaffCollisionValidator.Meets([1, 4, 1.13, 10.5], beam));
        // Side by side: a head set against a stem counts, one 0.05 away does not.
        Assert.True(CrossStaffCollisionValidator.Meets([4.001, 10, 5.3, 11], [3.87, 8, 4, 11]));
        Assert.False(CrossStaffCollisionValidator.Meets([4.05, 10, 5.3, 11], [3.87, 8, 4, 11]));

        // A slanted beam from (0, 10) up to (8, 6), half a space thick: its BOX holds the stem
        // at x = 7 from y = 6 to 10.5, its ink only round y = 6.5.
        double[] slanted = [0, 10.25, 8, 6.25, 8, 5.75, 0, 9.75];
        Assert.False(CrossStaffCollisionValidator.MeetsQuad([7, 8, 7.13, 10], slanted));
        Assert.True(CrossStaffCollisionValidator.MeetsQuad([7, 6.5, 7.13, 10], slanted));
        Assert.False(CrossStaffCollisionValidator.MeetsQuad([9, 5, 9.13, 10], slanted));
    }
}
