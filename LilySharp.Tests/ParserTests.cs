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
using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class ParserTests
{
    [Fact]
    public void ParseSingleNote()
    {
        var tree = SyntaxTree.Parse("c");
        Assert.NotNull(tree.Root);
        Assert.Equal(2, tree.Root.SlotCount); // note + EOF
    }

    [Fact]
    public void ParseNoteWithDuration()
    {
        var tree = SyntaxTree.Parse("c4");
        var note = tree.Root.GetSlot(0) as NoteGreen;

        Assert.NotNull(note);
        Assert.Equal(SyntaxKind.Note, note.Kind);
    }

    [Fact]
    public void ParseNoteWithOctave()
    {
        var tree = SyntaxTree.Parse("c''");
        var note = tree.Root.GetSlot(0) as NoteGreen;

        Assert.NotNull(note);
        var pitch = note.GetSlot(0) as PitchGreen;
        Assert.NotNull(pitch);
        // pitch token + 2 apostrophes
        Assert.Equal(3, pitch.SlotCount);
    }

    [Fact]
    public void ParseNoteSequence()
    {
        var tree = SyntaxTree.Parse("c4 d e f");

        // 4 notes + EOF
        Assert.Equal(5, tree.Root.SlotCount);

        for (int i = 0; i < 4; i++)
        {
            var note = tree.Root.GetSlot(i);
            Assert.Equal(SyntaxKind.Note, note?.Kind);
        }
    }

    [Fact]
    public void ParseRest()
    {
        var tree = SyntaxTree.Parse("r4");
        var rest = tree.Root.GetSlot(0) as RestGreen;

        Assert.NotNull(rest);
        Assert.Equal(SyntaxKind.Rest, rest.Kind);
    }

    [Fact]
    public void ParseChord()
    {
        var tree = SyntaxTree.Parse("<c e g>4");
        var chord = tree.Root.GetSlot(0) as ChordGreen;

        Assert.NotNull(chord);
        Assert.Equal(SyntaxKind.Chord, chord.Kind);
    }

    [Fact]
    public void NameStartingWithDigit_ReportsOneClearError_NotACascade()
    {
        // `phrase 2foo {}` lexes the leading digit off as an IntegerLiteral glued
        // to the name — the root cause is a digit-leading name, not the four
        // unrelated errors the stray tokens used to cascade into.
        var tree = SyntaxTree.Parse("phrase 2ほえほえ {}");
        var errors = tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Single(errors);
        Assert.Equal(DiagnosticCodes.NameStartsWithDigit, errors[0].Code);
        Assert.Contains("2ほえほえ", errors[0].Message);
        // Recovery merged `2ほえほえ` into one name, so the body parsed and the
        // source round-trips verbatim.
        Assert.Equal("phrase 2ほえほえ {}", tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("phrase foo2bar {}")]   // a digit in the middle is fine
    [InlineData("phrase melody2 {}")]   // a trailing digit is fine
    [InlineData("phrase ほえ2 {}")]      // …in any script
    public void NamesContainingOrEndingWithDigits_AreAccepted(string src)
    {
        // Only a LEADING digit is rejected; the name starts with a letter, so the
        // lexer keeps it as one identifier and it parses clean.
        var tree = SyntaxTree.Parse(src);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NameStartingWithDigit_AppliesToSectionsToo()
    {
        // ExpectPartName is the shared name-parse, so the rule covers every
        // declaration position (part / section / phrase / form / …).
        var tree = SyntaxTree.Parse("section 3A { c1 }");
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.NameStartsWithDigit);
    }

    [Fact]
    public void ParseBarline()
    {
        var tree = SyntaxTree.Parse("c d | e f");

        // c, d, |, e, f, EOF
        Assert.Equal(6, tree.Root.SlotCount);

        var barline = tree.Root.GetSlot(2) as BarlineGreen;
        Assert.NotNull(barline);
        Assert.Equal(SyntaxKind.Barline, barline.Kind);
    }

    [Fact]
    public void ParseTie()
    {
        var tree = SyntaxTree.Parse("c4~ c4");

        // note, tie, note, EOF
        Assert.Equal(4, tree.Root.SlotCount);

        var tie = tree.Root.GetSlot(1) as TieGreen;
        Assert.NotNull(tie);
        Assert.Equal(SyntaxKind.Tie, tie.Kind);
    }

    [Fact]
    public void ParseSlur()
    {
        var tree = SyntaxTree.Parse("c( d e f)");

        // c, (, d, e, f, ), EOF = 7 items
        Assert.Equal(7, tree.Root.SlotCount);

        var slurOpen = tree.Root.GetSlot(1) as SlurGreen;
        Assert.NotNull(slurOpen);
        Assert.Equal(SyntaxKind.Slur, slurOpen.Kind);
    }

    [Fact]
    public void ParseMusicBlock()
    {
        var tree = SyntaxTree.Parse("{ c d e f }");
        var block = tree.Root.GetSlot(0) as MusicBlockGreen;

        Assert.NotNull(block);
        Assert.Equal(SyntaxKind.MusicBlock, block.Kind);
    }

    [Fact]
    public void ParseComplexPhrase()
    {
        var tree = SyntaxTree.Parse("{ c4 d e f | g2 g | }");
        var block = tree.Root.GetSlot(0) as MusicBlockGreen;

        Assert.NotNull(block);

        // Check it round-trips
        var reconstructed = tree.ToFullString();
        Assert.Equal("{ c4 d e f | g2 g | }", reconstructed);
    }

    [Fact]
    public void RoundTrip_PreservesText()
    {
        var original = "{ c4 d8. e16 f4 | <c e g>2. r4 | }";
        var tree = SyntaxTree.Parse(original);
        var reconstructed = tree.ToFullString();

        Assert.Equal(original, reconstructed);
    }

    // A failed Expect() inserts a ZERO-WIDTH missing token that must NOT absorb the
    // next token's leading trivia — the real token keeps it and contributes it when
    // consumed, so counting it on the synthetic token too would duplicate it and break
    // the red-green invariant root.FullWidth == text.Length (and round-tripping). Each
    // input trips one or more Expects where Current carries leading whitespace/newline
    // (e.g. "time 4" then a stray token: Expect(Slash) AND Expect(denominator) both fail
    // on the same following token).
    [Theory]
    [InlineData("time 4\n  x")]
    [InlineData("time 3\n\tq")]
    [InlineData("{ c4 } time 6\n   y")]
    // Recovery placeholders for a missing key mode / clef name / octave mode are
    // now zero-width (was "major" / "treble" / "relative"), so even these
    // truncated inputs round-trip exactly (the token to recover is at end-of-input).
    [InlineData("key c")]
    [InlineData("clef")]
    [InlineData("octave")]
    public void MalformedInput_RoundTripsExactly(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void LyricMeasureWithoutTrailingBar_RoundTripsWithoutPhantomBar()
    {
        // A VALID lyrics block whose final measure omits the trailing '|' before
        // '}' must round-trip exactly: the synthetic barline is zero-width, so it
        // emits no '|' and shifts no following node's position.
        var source = "section M { m { c4 | } lyrics { hello world } }";
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseDottedDuration()
    {
        var tree = SyntaxTree.Parse("c4. d8..");

        var note1 = tree.Root.GetSlot(0) as NoteGreen;
        var note2 = tree.Root.GetSlot(1) as NoteGreen;

        Assert.NotNull(note1);
        Assert.NotNull(note2);

        var dur1 = note1.GetSlot(1) as DurationGreen;
        var dur2 = note2.GetSlot(1) as DurationGreen;

        Assert.NotNull(dur1);
        Assert.NotNull(dur2);
        Assert.Equal(2, dur1.SlotCount); // number + 1 dot
        Assert.Equal(3, dur2.SlotCount); // number + 2 dots
    }

    [Fact]
    public void ParseAccidentals()
    {
        var tree = SyntaxTree.Parse("cis des fisis geses");

        Assert.Equal(5, tree.Root.SlotCount); // 4 notes + EOF

        // All should be Note nodes
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(SyntaxKind.Note, tree.Root.GetSlot(i)?.Kind);
        }
    }

    [Fact]
    public void ParseWithWhitespaceAndComments()
    {
        var source = @"
// A simple melody
c4 d e f  /* first measure */
| g2 g |  // second measure
";
        var tree = SyntaxTree.Parse(source);

        // Should parse successfully and round-trip
        var reconstructed = tree.ToFullString();
        Assert.Equal(source, reconstructed);
    }

    // ========== Structure Tests ==========

    [Fact]
    public void ParsePartDeclaration()
    {
        var tree = SyntaxTree.Parse(@"part Violin ""First Violin"" {
    relative c' { c d e f }
}");
        var part = tree.Root.GetSlot(0) as PartDeclarationGreen;

        Assert.NotNull(part);
        Assert.Equal(SyntaxKind.PartDeclaration, part.Kind);
    }

    [Fact]
    public void ParseStaffDeclaration()
    {
        var tree = SyntaxTree.Parse(@"part Piano {
    staff RH {
        relative c'' { c d e f }
    }
    staff LH {
        relative c { c d e f }
    }
}");
        var part = tree.Root.GetSlot(0) as PartDeclarationGreen;
        Assert.NotNull(part);
    }

    [Fact]
    public void ParseMetadata()
    {
        var tree = SyntaxTree.Parse(@"title ""Happy Birthday""
composer ""Traditional""
tempo 120
time 3/4
key g major
");
        // Should have 5 metadata declarations + EOF
        Assert.Equal(6, tree.Root.SlotCount);

        var title = tree.Root.GetSlot(0) as MetadataDeclarationGreen;
        Assert.NotNull(title);
        Assert.Equal(SyntaxKind.MetadataDeclaration, title.Kind);
    }

    [Fact]
    public void ParsePropertyAssignment()
    {
        var tree = SyntaxTree.Parse(@"part {
    clef treble
    relative c' { c d e f }
}");
        var part = tree.Root.GetSlot(0) as PartDeclarationGreen;
        Assert.NotNull(part);
    }

    [Fact]
    public void BareEqualsDeclaration_BuildsRecoveryNode()
    {
        // 'name = { … }' is rejected in favor of 'phrase name { … }', but a
        // VariableDeclaration node is still built so $name can resolve.
        var tree = SyntaxTree.Parse("theme = { c d e f }");
        var varDecl = tree.Root.GetSlot(0) as VariableDeclarationGreen;
        Assert.NotNull(varDecl);
        Assert.Equal(SyntaxKind.VariableDeclaration, varDecl.Kind);
    }

    [Fact]
    public void BareEqualsDeclaration_IsRejected()
    {
        // Lily# has no '=' assignment; declarations are 'keyword name { … }'. The
        // error is neutral — it does not presume which keyword was meant.
        var tree = SyntaxTree.Parse("theme = { c d e f }");
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.LegacyDeclarationForm);
    }

    [Fact]
    public void PhraseDeclaration_IsTheBlessedForm()
    {
        var tree = SyntaxTree.Parse("phrase theme { c d e f }");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void WrongCaseKeyMode_GivesCleanUnknownMode_NotStrayReference()
    {
        // `Major` is a wrong-case (unknown) mode. It must give a clean "unknown mode"
        // error and be consumed — NOT leak into the music as a bare-reference error.
        var tree = SyntaxTree.Parse("key c Major\npart vln { clef treble }\n" +
            "section A { vln { c4 d e f } }\nform main { A }\nscore \"s\" { staff vln }");
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.UnknownSymbolCase);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.BareReferenceRequiresDollar);
    }

    [Fact]
    public void MetadataValue_MustBeQuoted()
    {
        // title/composer values are free-text strings (like a score name), so they
        // must be quoted; a bare value is rejected.
        var bare = SyntaxTree.Parse("title Song");
        Assert.True(bare.HasErrors);
        Assert.Contains(bare.Diagnostics, d => d.Code == DiagnosticCodes.MetadataValueMustBeQuoted);

        var quoted = SyntaxTree.Parse("title \"Song\"\ncomposer \"Bach\"");
        Assert.DoesNotContain(quoted.Diagnostics, d => d.Code == DiagnosticCodes.MetadataValueMustBeQuoted);
    }

    [Fact]
    public void UnterminatedString_IsReported()
    {
        var tree = SyntaxTree.Parse("title \"oops");
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedString);
    }

    [Fact]
    public void UnterminatedBlockComment_IsReported()
    {
        var tree = SyntaxTree.Parse("/* never closed\nphrase a { c }");
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedComment);
    }

    [Fact]
    public void TerminatedStringAndComment_AreClean()
    {
        var tree = SyntaxTree.Parse("/* ok */ title \"fine\"\nphrase a { c }");
        Assert.DoesNotContain(tree.Diagnostics, d =>
            d.Code == DiagnosticCodes.UnterminatedString || d.Code == DiagnosticCodes.UnterminatedComment);
    }

    [Fact]
    public void Sharp_IsLegalInsideChordAndFigArgs()
    {
        // '#' is illegal everywhere EXCEPT inside a @chord(...) / @fig(...) argument.
        foreach (var src in new[] { "c4 @chord(C#m7) d", "c4 @fig(#6) d" })
        {
            var tree = SyntaxTree.Parse(src);
            Assert.DoesNotContain(tree.Diagnostics,
                d => d.Code == DiagnosticCodes.UnexpectedCharacter);
        }
    }

    [Fact]
    public void Sharp_StaysIllegalOutsideChordAndFigArgs()
    {
        // A stray '#' outside a chord/fig argument is still flagged.
        var tree = SyntaxTree.Parse("c4 # d");
        Assert.Contains(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.UnexpectedCharacter);
    }

    [Fact]
    public void CautionaryAccidentalReflex_PointsToCourtesy()
    {
        // 'c'?' is LilyPond's cautionary accidental — flag it with a pointer to
        // the Lily# annotation form rather than a bare "unexpected character".
        var tree = SyntaxTree.Parse("c'? d'");
        Assert.Contains(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.UnexpectedCharacter && d.Message.Contains("@courtesy"));
    }

    [Fact]
    public void BackToBackRepeatBarline_Inline_ParsesCleanly()
    {
        // ':|:' is a single back-to-back repeat token, not ':|' + a stray ':'.
        var tree = SyntaxTree.Parse("{ c4 c c c :|: d4 d d d }");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.DoesNotContain(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.UnexpectedCharacter);
    }

    [Fact]
    public void DoublePlacementSuffix_GivesClearError()
    {
        // @staccato.up.down — a second placement is a mistake; the message must
        // name the real problem, not cascade into "'down' is not a note".
        var tree = SyntaxTree.Parse("c4@staccato.up.down");
        Assert.Contains(tree.Diagnostics, d => d.Message.Contains("only one of"));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Message.Contains("not a note"));
    }

    [Fact]
    public void SinglePlacementSuffix_IsClean()
    {
        var tree = SyntaxTree.Parse("c4@staccato.up");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseVariableReference()
    {
        var tree = SyntaxTree.Parse(@"phrase theme { c d e f }
$theme");

        var varRef = tree.Root.GetSlot(1) as VariableReferenceGreen;
        Assert.NotNull(varRef);
        Assert.Equal(SyntaxKind.VariableReference, varRef.Kind);
    }

    [Fact]
    public void ParseNoteWithArticulation()
    {
        var tree = SyntaxTree.Parse("{ c4@staccato }");
        Assert.False(tree.HasErrors);

        var root = tree.GetRoot();
        Assert.NotNull(root);
    }

    [Fact]
    public void ParseNoteWithDynamic()
    {
        var tree = SyntaxTree.Parse(@"{ c4@p }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseNoteWithMultipleArticulations()
    {
        var tree = SyntaxTree.Parse(@"{ c4@staccato@accent@f }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseDynamicSequence()
    {
        var tree = SyntaxTree.Parse(@"{ c4@p d@cresc e@f }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseNoteWithTrill()
    {
        var tree = SyntaxTree.Parse("{ c4@trill }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseNoteWithMultipleOrnaments()
    {
        var tree = SyntaxTree.Parse("{ c4@trill d4@mordent e4@turn f4@prall }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseNoteWithOrnamentAndDynamic()
    {
        var tree = SyntaxTree.Parse(@"{ c4@trill@p }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseNoteWithDynamicAtPrefix()
    {
        // New @ prefix for dynamics
        var tree = SyntaxTree.Parse(@"{ c4@p d@f e@mf }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void BackslashDynamic_IsRejected_WithAtHint()
    {
        // Backslash annotations are no longer accepted: '@' is the one canonical
        // prefix, and backslash is reserved for tablature (\3 string numbers, \tuning).
        // A '\p' is flagged with a hint pointing at '@p'.
        var tree = SyntaxTree.Parse(@"{ c4\p }");
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics, d => d.Message.Contains("@p"));

        // The canonical '@' form is clean.
        Assert.False(SyntaxTree.Parse(@"{ c4@p }").HasErrors);
    }

    [Fact]
    public void ParseNoteWithArticulationAndDynamicAtPrefix()
    {
        // Articulation and dynamic both with @ prefix
        var tree = SyntaxTree.Parse(@"{ c4@staccato@p d@accent@f }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ClefNameWords_AreUsableAsIdentifiers()
    {
        // bass/treble/alto/tenor are clef-name keywords but are also natural part /
        // section / phrase names. Declarations AND every reference accept them, so a
        // 'bass' part can be declared, referenced, sectioned and structured.
        var tree = SyntaxTree.Parse(
            "part bass { clef bass }\n" +
            "phrase bass { c2 c | }\n" +
            "section bass { bass { $bass } }\n" +
            "form main { bass }\n" +
            "score main \"out\" { staff bass }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void OverrideValue_AcceptsString()
    {
        // Override values may be strings (e.g. a color), not just integers/identifiers.
        var tree = SyntaxTree.Parse("{ once override NoteHead.color = \"red\" c4 d e f }");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void OverrideProperty_AcceptsHyphenatedLilyPondName()
    {
        // LilyPond grob properties are kebab-case (force-hshift, X-offset). The lexer
        // splits on '-', so the override parser reassembles the name into ONE token.
        const string src = "{ override NoteColumn.force-hshift = 2 c4 d e f }";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var ov = tree.GetRoot().DescendantNodes<OverrideDeclarationSyntax>().Single();
        Assert.Equal("force-hshift", ov.PropertyName.Text);
        // The folded hyphen/trivia keeps the tree byte-exact (root.FullWidth == text.Length).
        Assert.Equal(src, tree.ToFullString());
    }

    [Fact]
    public void RevertProperty_AcceptsHyphenatedLilyPondName()
    {
        const string src = "{ override NoteColumn.force-hshift = 1 c4 revert NoteColumn.force-hshift d }";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var rv = tree.GetRoot().DescendantNodes<RevertDeclarationSyntax>().Single();
        Assert.Equal("force-hshift", rv.PropertyName.Text);
        Assert.Equal(src, tree.ToFullString());
    }

    [Fact]
    public void PartialWithoutDuration_GivesClearError_AndDoesNotCrashValidation()
    {
        // `partial .` (missing the note value): a clear message, not the raw
        // "Expected 'IntegerLiteral', found 'Dot'". The recovered empty-number duration
        // must not throw downstream — int.Parse("") once took the whole diagnostics pass
        // down, emptying the Problems panel.
        var tree = SyntaxTree.Parse(
            "section A { partial . }\npart melody { section A { c2 | a1 } }\nform main { A }\nscore main { staff melody }");
        Assert.Contains(tree.Diagnostics, d =>
            d.Code == DiagnosticCodes.ExpectedToken && d.Message.Contains("'partial' needs a duration"));
        // The full validator pass (what the LSP runs to fill the Problems panel) must not throw.
        var ex = Record.Exception(() => LilySharp.Core.Semantics.SemanticValidation.Run(tree).ToList());
        Assert.Null(ex);
    }

    // ========== Repeat Tests ==========

    [Fact]
    public void ParseRepeatUnfold()
    {
        var tree = SyntaxTree.Parse("repeat unfold 2 { c4 d e f }");
        Assert.False(tree.HasErrors);

        var repeat = tree.Root.GetSlot(0) as RepeatExpressionGreen;
        Assert.NotNull(repeat);
        Assert.Equal(SyntaxKind.RepeatExpression, repeat.Kind);
    }

    [Fact]
    public void RepeatVolta_IsRejected_WithSymbolicHint()
    {
        // 'repeat volta' was removed in favor of the symbolic |: … :| form.
        var tree = SyntaxTree.Parse("repeat volta 2 { c4 d e f }");
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.RepeatVoltaRemoved);
    }

    [Fact]
    public void RepeatVoltaWithAlternative_IsRejected_ButRecovers()
    {
        var tree = SyntaxTree.Parse(@"repeat volta 2 { c4 d e f } alternative { { g2 } { a2 } }");
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.RepeatVoltaRemoved);

        // Recovery still parses the full structure (no cascade), including the
        // alternative clause, so the tree round-trips faithfully.
        var repeat = tree.Root.GetSlot(0) as RepeatExpressionGreen;
        Assert.NotNull(repeat);
        Assert.NotNull(repeat.GetSlot(4) as AlternativeClauseGreen);
    }

    [Fact]
    public void ParseRepeatUnfoldRoundTrip()
    {
        var source = "repeat unfold 2 { c4 d e f | g2 g | }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void RepeatVolta_RecoveryRoundTripsFaithfully()
    {
        // Even though 'repeat volta' is rejected, recovery consumes every token so
        // the source still round-trips (full-fidelity preserved through the error).
        var source = "repeat volta 2 { c4 d e f } alternative { { g2 } { a2 } }";
        var tree = SyntaxTree.Parse(source);
        Assert.True(tree.HasErrors);
        Assert.Equal(source, tree.ToFullString());
    }

    // ========== Parallel Tests ==========

    [Fact]
    public void ParseParallelExpression()
    {
        var tree = SyntaxTree.Parse(@"voice { c2 d } voice { e2 f }");
        Assert.False(tree.HasErrors);

        var parallel = tree.Root.GetSlot(0) as ParallelExpressionGreen;
        Assert.NotNull(parallel);
        Assert.Equal(SyntaxKind.ParallelExpression, parallel.Kind);
    }

    [Fact]
    public void ParseParallelWithMusicBlocks()
    {
        var tree = SyntaxTree.Parse(@"voice { c2 d } voice { e2 f }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseParallelRoundTrip()
    {
        var source = @"voice { c2 d } voice { e2 f }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseParallelThreeVoices()
    {
        var tree = SyntaxTree.Parse(@"voice { c2 } voice { e2 } voice { g2 }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseNamedVoices_CarryTheirNames()
    {
        var tree = SyntaxTree.Parse(@"voice sop { c2 } voice alt { e2 }");
        Assert.False(tree.HasErrors);
        var parallel = tree.GetRoot().DescendantNodes()
            .OfType<ParallelExpressionSyntax>().Single();
        var names = parallel.NamedVoices.Select(v => v.Name).ToArray();
        Assert.Equal(new[] { "sop", "alt" }, names);
    }

    [Fact]
    public void ParseNamedLyrics_CarryTheirBindingName()
    {
        var tree = SyntaxTree.Parse(@"lyrics alt { la la la }");
        Assert.False(tree.HasErrors);
        var lyrics = tree.GetRoot().DescendantNodes().OfType<LyricsBlockSyntax>().Single();
        Assert.Equal("alt", lyrics.VoiceName);
    }

    [Fact]
    public void ParseUnnamedVoicesAndLyrics_HaveNoNames()
    {
        var tree = SyntaxTree.Parse(@"voice { c2 } voice { e2 }");
        Assert.False(tree.HasErrors);
        var parallel = tree.GetRoot().DescendantNodes()
            .OfType<ParallelExpressionSyntax>().Single();
        Assert.All(parallel.NamedVoices, v => Assert.Null(v.Name));
    }

    [Fact]
    public void OldAngleParallelSyntax_ReportsMigrationHint()
    {
        // The removed << … \\ … >> form is rejected with a hint pointing at voice { }.
        var tree = SyntaxTree.Parse(@"<< { c2 d } \\ { e2 f } >>");
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.ParallelSyntaxRemoved);
    }

    [Theory]
    [InlineData(@"\tempo 120")]
    [InlineData(@"\clef bass")]
    [InlineData(@"\new Staff { c4 d }")]
    [InlineData(@"\relative c' { c4 d }")]
    [InlineData(@"\addlyrics { la la }")]
    public void LilypondBackslashCommand_ReportsMigrationHint(string source)
    {
        // A LilyPond reflex — a leading backslash on a command — gets a Lily# hint.
        var tree = SyntaxTree.Parse(source);
        Assert.Contains(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.LilypondBackslashCommand);
    }

    [Fact]
    public void Backslash_OnTablature_IsNotFlaggedAsLilypondReflex()
    {
        // \tabStaff / \tuning are genuine Lily# backslash syntax, not LP reflexes.
        var tree = SyntaxTree.Parse(@"\tabStaff \tuning guitar { e4 a d' }");
        Assert.DoesNotContain(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.LilypondBackslashCommand);
    }

    [Fact]
    public void ParseNestedRepeatInPart()
    {
        // 'repeat volta' inside a section is also rejected with the symbolic hint.
        var tree = SyntaxTree.Parse(@"section Main {
    melody {
        repeat volta 2 {
            c4 d e f |
        }
        alternative {
            { g2 g | }
            { a2 a | }
        }
    }
}");
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.RepeatVoltaRemoved);
    }

    [Fact]
    public void ParseParallelInStaff()
    {
        var tree = SyntaxTree.Parse(@"section Main {
    melody {
        voice { c2 d } voice { e2 f }
    }
}");
        Assert.False(tree.HasErrors);
    }

    // ========== Key Signature ==========

    [Fact]
    public void ParseKeySignatureMajor()
    {
        var source = "key c major";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var keySig = tree.GetNodes<KeySignatureSyntax>().First();
        Assert.Equal("c", keySig.Pitch.PitchName);
        Assert.True(keySig.IsMajor);
    }

    [Fact]
    public void ParseKeySignatureMinor()
    {
        var source = "key a minor";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var keySig = tree.GetNodes<KeySignatureSyntax>().First();
        Assert.Equal("a", keySig.Pitch.PitchName);
        Assert.False(keySig.IsMajor);
    }

    [Fact]
    public void ParseKeySignatureWithAccidental()
    {
        var source = "key bes major";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var keySig = tree.GetNodes<KeySignatureSyntax>().First();
        Assert.Equal("bes", keySig.Pitch.PitchName);
    }

    // ========== Clef ==========

    [Fact]
    public void ParseClefTreble()
    {
        var source = "clef treble";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var clef = tree.GetNodes<ClefDeclarationSyntax>().First();
        Assert.Equal(SyntaxKind.TrebleKeyword, clef.ClefName.Kind);
    }

    [Fact]
    public void ParseClefBass()
    {
        var source = "clef bass";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var clef = tree.GetNodes<ClefDeclarationSyntax>().First();
        Assert.Equal(SyntaxKind.BassKeyword, clef.ClefName.Kind);
    }

    // ========== Tuplet ==========

    [Fact]
    public void ParseTupletTriplet()
    {
        var source = "tuplet 3/2 { c d e }";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var tuplet = tree.GetNodes<TupletExpressionSyntax>().First();
        Assert.Equal(3, tuplet.TupletRatio);
        Assert.Equal(2, tuplet.BaseDivision);
        Assert.Equal(3, tuplet.Body.Items.Count());
    }

    [Fact]
    public void ParseTupletQuintuplet()
    {
        var source = "tuplet 5/4 { c d e f g }";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var tuplet = tree.GetNodes<TupletExpressionSyntax>().First();
        Assert.Equal(5, tuplet.TupletRatio);
        Assert.Equal(4, tuplet.BaseDivision);
    }

    [Fact]
    public void ParseKeyClefTupletCombined()
    {
        var source = @"
clef treble
key g major
tuplet 3/2 { c d e }
c4 d e f
";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        Assert.Single(tree.GetNodes<ClefDeclarationSyntax>());
        Assert.Single(tree.GetNodes<KeySignatureSyntax>());
        Assert.Single(tree.GetNodes<TupletExpressionSyntax>());
    }

    [Fact]
    public void RoundTripKeySignature()
    {
        var source = "key fis minor";
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void RoundTripTuplet()
    {
        var source = "tuplet 3/2 { c d e }";
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.ToFullString());
    }

    // ========== Grace Notes ==========

    [Fact]
    public void ParseGraceNotes()
    {
        var source = "grace { c16 d e } c4";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var grace = tree.GetNodes<GraceExpressionSyntax>().First();
        Assert.Equal(SyntaxKind.GraceKeyword, grace.GraceKeyword.Kind);
    }

    [Fact]
    public void ParseAcciaccatura()
    {
        var source = "acciaccatura { c16 } d4";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var grace = tree.GetNodes<GraceExpressionSyntax>().First();
        Assert.True(grace.IsAcciaccatura);
    }

    [Fact]
    public void ParseAppoggiatura()
    {
        var source = "appoggiatura { c8 } d4";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var grace = tree.GetNodes<GraceExpressionSyntax>().First();
        Assert.True(grace.IsAppoggiatura);
    }

    [Fact]
    public void RoundTripGrace()
    {
        var source = "grace { c16 d e }";
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.ToFullString());
    }

    // ========== Lyrics ==========

    [Fact]
    public void ParseLyricsBlock()
    {
        var source = "lyrics { Hap -- py birth -- day }";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var lyrics = tree.GetNodes<LyricsBlockSyntax>().First();
        Assert.NotEmpty(lyrics.Syllables);
    }

    [Fact]
    public void ParseMusicWithLyrics()
    {
        var source = @"
{ c d e f g }
lyrics { do re mi fa sol }
";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        Assert.Single(tree.GetNodes<LyricsBlockSyntax>());
        Assert.Equal(5, tree.GetNodes<NoteSyntax>().Count());
    }

    // ========== Time Signature and Tempo ==========

    [Fact]
    public void ParseTimeSignature()
    {
        var source = "time 3/4";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var timeSig = tree.GetNodes<TimeSignatureSyntax>().First();
        Assert.Equal(3, timeSig.Beats);
        Assert.Equal(4, timeSig.BeatType);
    }

    [Fact]
    public void ParseTempoDeclaration_Simple()
    {
        var source = "tempo 120";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var tempo = tree.GetNodes<TempoDeclarationSyntax>().First();
        Assert.Equal(120, tempo.Bpm);
    }

    [Fact]
    public void ParseTempoDeclaration_WithBeatUnit()
    {
        var source = "tempo 4 = 120";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var tempo = tree.GetNodes<TempoDeclarationSyntax>().First();
        Assert.Equal(4, tempo.BeatUnit);
        Assert.Equal(120, tempo.Bpm);
    }

    [Fact]
    public void ParseTempoDeclaration_WithMarking()
    {
        var source = "tempo \"Allegro\" 4 = 132";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var tempo = tree.GetNodes<TempoDeclarationSyntax>().First();
        Assert.Equal("Allegro", tempo.Marking);
        Assert.Equal(4, tempo.BeatUnit);
        Assert.Equal(132, tempo.Bpm);
    }

    // ========== Pitch Properties ==========

    [Fact]
    public void PitchSyntax_BaseName()
    {
        var source = "cis";
        var tree = SyntaxTree.Parse(source);
        var pitch = tree.GetNodes<PitchSyntax>().First();

        Assert.Equal('c', pitch.BaseName);
        Assert.Equal("is", pitch.Accidental);
        Assert.Equal(1, pitch.AccidentalOffset);
    }

    [Fact]
    public void PitchSyntax_Flat()
    {
        var source = "bes";
        var tree = SyntaxTree.Parse(source);
        var pitch = tree.GetNodes<PitchSyntax>().First();

        Assert.Equal('b', pitch.BaseName);
        Assert.Equal("es", pitch.Accidental);
        Assert.Equal(-1, pitch.AccidentalOffset);
    }

    // LilyPond's Dutch contractions `es` (E flat) and `as` (A flat) are accepted
    // alongside `ees`/`aes` and normalize to the same pitch.
    [Theory]
    [InlineData("es", 'e', -1)]
    [InlineData("as", 'a', -1)]
    [InlineData("ees", 'e', -1)]
    [InlineData("aes", 'a', -1)]
    public void PitchSyntax_DutchFlatContractions(string source, char baseName, int offset)
    {
        var tree = SyntaxTree.Parse(source);
        var pitch = tree.GetNodes<PitchSyntax>().First();

        Assert.Equal(baseName, pitch.BaseName);
        Assert.Equal(offset, pitch.AccidentalOffset);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    // A bare word where a note is expected reports clearly and, for the classic
    // English-accidental slip, suggests the Dutch spelling instead of assuming a
    // phrase reference.
    [Theory]
    [InlineData("{ c4 eb d4 }", "ees")]
    [InlineData("{ c4 bb d4 }", "bes")]
    [InlineData("{ c4 gflat d4 }", "ges")]
    public void BareWord_EnglishAccidental_SuggestsDutchPitch(string source, string suggested)
    {
        var tree = SyntaxTree.Parse(source);
        var err = tree.Diagnostics.First(d => d.Code == DiagnosticCodes.BareReferenceRequiresDollar);
        Assert.Contains($"'{suggested}'", err.Message);
    }

    [Fact]
    public void BareWord_UnknownName_IsUndefinedPhrase()
    {
        // A bare identifier in music is a phrase reference (no `$` sigil); an
        // unknown one is reported as an undefined phrase by semantic validation.
        var tree = SyntaxTree.Parse("{ c4 xyz d4 }");
        var diags = tree.Diagnostics.Concat(SemanticValidation.Run(tree)).ToList();
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.UndefinedVariable && d.Message.Contains("xyz"));
    }

    // ========== Metadata Properties ==========

    [Fact]
    public void MetadataDeclaration_StringValue()
    {
        var source = "title \"My Song\"";
        var tree = SyntaxTree.Parse(source);
        var meta = tree.GetNodes<MetadataDeclarationSyntax>().First();

        Assert.Equal("title", meta.Keyword);
        Assert.Equal("My Song", meta.StringValue);
    }

    [Fact]
    public void ParseFormRepeatBlock()
    {
        var source = @"
section A {
    melody { c4 d e | }
}
form main {
    |: A :|
}
";
        var tree = SyntaxTree.Parse(source);
        var root = tree.GetRoot();

        // Find FormDeclarationSyntax
        var structure = root.DescendantNodes().OfType<FormDeclarationSyntax>().FirstOrDefault();
        Assert.NotNull(structure);

        // Find FormRepeatBlockSyntax
        var repeat = root.DescendantNodes().OfType<FormRepeatBlockSyntax>().FirstOrDefault();
        Assert.NotNull(repeat);

        // Check slots - should have |:, A (reference), and :|
        Assert.True(repeat!.SlotCount >= 3, $"Expected at least 3 slots but got {repeat.SlotCount}");
    }

    [Fact]
    public void ParseVoltaWithRange()
    {
        var source = @"
section A { melody { c4 | } }
section B { melody { d4 | } }
form main {
    |: A [1-3. B] :| x4
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseVoltaWithList()
    {
        var source = @"
section A { melody { c4 | } }
section B { melody { d4 | } }
form main {
    |: A [1,3. B] :| x4
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseTremolo()
    {
        var source = @"
section A {
    melody {
        c4:8 d4:16 e4:32 |
    }
}
form main { A }
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.Equal(source, tree.ToFullString());

        // Verify tremolo is parsed
        var noteNodes = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().ToList();
        Assert.Equal(3, noteNodes.Count);
        Assert.NotNull(noteNodes[0].Tremolo);
        Assert.Equal(":8", noteNodes[0].Tremolo!.Text);
        Assert.Equal(":16", noteNodes[1].Tremolo!.Text);
        Assert.Equal(":32", noteNodes[2].Tremolo!.Text);
    }

    [Fact]
    public void ParseTremolo_WidePowersOfTwo()
    {
        // LilyPond's tremolo_type accepts any power-of-two duration, not just
        // :8/:16/:32 — e.g. :64 (4 beams) and :128 (5 beams).
        var source = @"
section A {
    melody {
        c4:64 d4:128 |
    }
}
form main { A }
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.Equal(source, tree.ToFullString());

        var noteNodes = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().ToList();
        Assert.Equal(2, noteNodes.Count);
        Assert.Equal(":64", noteNodes[0].Tremolo!.Text);
        Assert.Equal(":128", noteNodes[1].Tremolo!.Text);
    }

    [Fact]
    public void ParseTremoloChord()
    {
        var source = @"
section A {
    melody {
        <c e g>4:16 |
    }
}
form main { A }
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var chordNodes = tree.GetRoot().DescendantNodes().OfType<ChordSyntax>().ToList();
        Assert.Single(chordNodes);
        Assert.NotNull(chordNodes[0].Tremolo);
        Assert.Equal(":16", chordNodes[0].Tremolo!.Text);
    }

    [Fact]
    public void ParseLyrics()
    {
        var source = @"
section Verse {
    melody { c4 d4 e4 f4 | g2 g2 | }
    lyrics w { き ら き ら | ひ か | }
}
form main { Verse }
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseLyricsWithHyphens()
    {
        var source = @"
section Verse {
    melody { c4 d4 e4 f4 | g2 g2 | }
    lyrics w { twi- nkle twi- nkle | li- tle | }
}
form main { Verse }
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseLyricsWithMelisma()
    {
        var source = @"
section Verse {
    melody { c4 d4 e4 f4 | g2 g2 | }
    lyrics w { Glo~ ~ ri- a | in ex- | }
}
form main { Verse }
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseMultipleVerses()
    {
        var source = @"
section Verse {
    melody { c4 d4 e4 f4 | g2 g2 | }
    lyrics w { き ら き ら | ひ か | }
    lyrics w2 { ま ば た き | し て | }
}
form main { Verse }
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ParseDollarVariableReference()
    {
        var tree = SyntaxTree.Parse(@"phrase theme { c d e f }
$theme");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var varRef = tree.Root.GetSlot(1) as VariableReferenceGreen;
        Assert.NotNull(varRef);
        Assert.Equal(SyntaxKind.VariableReference, varRef.Kind);
        // $name is the blessed form — it must not trip the bare-reference error.
        Assert.Empty(tree.Diagnostics.Where(d => d.Code == DiagnosticCodes.BareReferenceRequiresDollar));
    }

    [Fact]
    public void ParseDollarPhraseReferenceInSection()
    {
        var source = @"time 4/4
part melody
phrase intro { c4 d e f | }
section Main {
  melody { $intro }
}
form main { Main }
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.Empty(tree.Diagnostics.Where(d => d.Code == DiagnosticCodes.BareReferenceRequiresDollar));
    }

    [Fact]
    public void BareName_ReferencesPhrase_NoDollarNeeded()
    {
        // A phrase is referenced by BARE name now: 'melody { intro }' resolves the
        // `intro` phrase — no `$` sigil, no error.
        var source = @"time 4/4
part melody
phrase intro { c4 d e f | }
section Main {
  melody { intro }
}
form main { Main }
";
        var tree = SyntaxTree.Parse(source);
        var diags = tree.Diagnostics.Concat(SemanticValidation.Run(tree)).ToList();
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BareReferenceRequiresDollar);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.UndefinedVariable);
        Assert.Contains(tree.GetNodes<VariableReferenceSyntax>(), r => r.Name.Text == "intro");
    }

    [Theory]
    [InlineData("intro", 0)]
    [InlineData("intro'", 1)]
    [InlineData("intro''", 2)]
    [InlineData("intro,", -1)]
    [InlineData("intro,,", -2)]
    [InlineData("$intro", 0)]
    [InlineData("$intro'", 1)]
    [InlineData("$intro,,", -2)]
    public void PhraseReference_OctaveMarks_CountAsOffset(string reference, int expected)
    {
        // A phrase reference carries the SAME trailing octave marks as a pitch —
        // intro' lands the movable phrase an octave up, intro,, two octaves down.
        // The name stays intact; the marks contribute only to OctaveOffset.
        var source = $"phrase intro {{ c4 d e f | }}\n{{ {reference} }}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var varRef = tree.GetNodes<VariableReferenceSyntax>().Single();
        Assert.Equal("intro", varRef.Name.Text);
        Assert.Equal(expected, varRef.OctaveOffset);
    }

    [Theory]
    [InlineData("intro'")]
    [InlineData("intro,,")]
    [InlineData("$intro'")]
    public void PhraseReference_OctaveMarks_RoundTrip(string reference)
    {
        // Marks live on the green node's tokens, so ToFullString reproduces the
        // source byte-for-byte (the click-jump offsets stay exact).
        var source = $"phrase intro {{ c4 d e f | }}\n{{ {reference} }}";
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    // --- Naive grand-staff mistake: clef name used like a staff block ---

    [Fact]
    public void ClefNameAsStaff_NaiveGrandStaffAttempt_Warns()
    {
        // The dogfood mistake: `treble { … } bass { … }` treated as staves. The
        // clef keywords aren't top-level items and were silently skipped; warn.
        var tree = SyntaxTree.Parse("treble { c'1 }\nbass { c1 }");
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.ClefNameAsStaff);
    }

    [Fact]
    public void ClefNameAsStaff_CorrectGrandStaff_DoesNotWarn()
    {
        var tree = SyntaxTree.Parse(
            "part upper { clef treble }\npart lower { clef bass }\n" +
            "section Main { upper { c'1 } lower { c1 } }\nform main { Main }\n" +
            "score main \"x\" { grandStaff { staff upper staff lower } }");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.ClefNameAsStaff);
    }

    [Fact]
    public void ClefNameAsStaff_ClefDeclaration_DoesNotWarn()
    {
        // `clef treble` (with the clef keyword) is the real clef form — never flagged.
        var tree = SyntaxTree.Parse("part p { clef treble }\nsection M { p { c1 } }\nform main { M }");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.ClefNameAsStaff);
    }

    [Fact]
    public void ClefNameAsStaff_SingleStaffMusic_DoesNotWarn()
    {
        var tree = SyntaxTree.Parse("{ c d e f }");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.ClefNameAsStaff);
    }

    [Fact]
    public void ClefNameAsStaff_NoFixtureOrSampleTripsIt()
    {
        // No shipped .lys legitimately writes a bare clef name before a block, so
        // the warning must never fire on the fixtures or the samples playground.
        var offenders = new System.Collections.Generic.List<string>();
        foreach (var dir in EnumerateLysRoots())
            foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.lys", System.IO.SearchOption.AllDirectories))
            {
                var tree = SyntaxTree.Parse(System.IO.File.ReadAllText(file));
                if (tree.Diagnostics.Any(d => d.Code == DiagnosticCodes.ClefNameAsStaff))
                    offenders.Add(System.IO.Path.GetFileName(file));
            }
        Assert.True(offenders.Count == 0, "ClefNameAsStaff fired on: " + string.Join(", ", offenders));
    }

    private static System.Collections.Generic.IEnumerable<string> EnumerateLysRoots()
    {
        var dir = System.AppContext.BaseDirectory;
        while (dir != null)
        {
            var roots = new System.Collections.Generic.List<string>();
            var samples = System.IO.Path.Combine(dir, "samples");
            if (System.IO.Directory.Exists(samples)) roots.Add(samples);
            var fixtures = System.IO.Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (System.IO.Directory.Exists(fixtures)) roots.Add(fixtures);
            if (roots.Count > 0) return roots;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        return System.Array.Empty<string>();
    }
}
