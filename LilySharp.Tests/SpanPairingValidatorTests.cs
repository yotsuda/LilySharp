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
/// A text-spanner mark that pairs with nothing draws NOTHING — the word goes with the line,
/// which is LilyPond's own answer (<c>suicide()</c>) and not a shortening. LYS4018 says so,
/// because before the terminator existed a bare <c>@rit</c> was given a one-measure default
/// and the reader was never told the length was the engine's guess.
/// </summary>
[Trait("Category", "Unit")]
public class SpanPairingValidatorTests
{
    private static IReadOnlyList<Diagnostic> Warnings(string music)
    {
        var source = $"octave absolute part m {{ clef treble }} "
                     + $"section A {{ m {{ {music} }} }} form main {{ A }} score main {{ staff m }}";
        var validator = new SpanPairingValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnpairedSpan
                        && d.Severity == DiagnosticSeverity.Warning)
            .ToList();
    }

    private static int WarningCount(string music) => Warnings(music).Count;

    [Theory]
    [InlineData("c'4@rit c' c' c'@!rit |")]                     // the plain pair
    [InlineData("c'4@rit c'@!rit c'@accel c'@!accel |")]        // two in a row
    [InlineData("c'4@rit c' c' c' | c'@!rit c' c' c' |")]       // across the barline
    [InlineData("c'4@accel c' c' c'@!rit |")]                   // ONE stop for the whole family
    [InlineData("c'4@textSpan(\"poco rit.\") c' c' c'@!textSpan |")]   // the general spelling
    [InlineData("c'4@textSpan(\"poco rit.\") c' c' c'@!rit |")]        // and its sugar's stop
    [InlineData("c'4 c' c' c' |")]                              // no spanner at all
    public void PairedSpanners_NoWarning(string music) =>
        Assert.Equal(0, WarningCount(music));

    [Theory]
    [InlineData("c'4@rit c' c' c' |")]                          // never closed
    [InlineData("c'4@accel c' c' c' |")]
    [InlineData("c'4@rall c' c' c' |")]
    [InlineData("c'4@textSpan(\"poco rit.\") c' c' c' |")]
    public void AnUnterminatedSpanner_IsReported(string music)
    {
        var warning = Assert.Single(Warnings(music));
        Assert.Contains("never closed", warning.Message);
    }

    [Fact]
    public void ATerminatorWithNothingOpen_IsReported()
    {
        var warning = Assert.Single(Warnings("c'4@!rit c' c' c' |"));
        Assert.Contains("closes nothing", warning.Message);
    }

    [Fact]
    public void ASecondSpannerInsideAnOpenOne_IsReported()
    {
        // The first keeps the span; the second is dropped. Only the second is complained
        // about, because the first is doing exactly what was written.
        var warning = Assert.Single(Warnings("c'4@rit c'@accel c' c'@!rit |"));
        Assert.Contains("already open", warning.Message);
    }

    /// <summary>
    /// ⚠️ THE MESSAGES ARE ASCII, like <see cref="SlurPairingValidator"/>'s: they reach
    /// legacy-codepage consoles through the CLI, where a curly quote or a dash arrives as
    /// mojibake in the middle of the sentence that is supposed to help.
    /// </summary>
    [Fact]
    public void TheMessages_AreAscii()
    {
        foreach (var music in new[]
                 {
                     "c'4@rit c' c' c' |",
                     "c'4@!rit c' c' c' |",
                     "c'4@rit c'@accel c' c'@!rit |",
                 })
        {
            var message = Assert.Single(Warnings(music)).Message;
            Assert.All(message, ch => Assert.True(ch < 128, $"non-ASCII '{ch}' in: {message}"));
        }
    }

    /// <summary>
    /// The diagnostic points at the MARK, not at the bar or the score — the reader has to
    /// land on the '@rit' that needs a partner.
    /// </summary>
    [Fact]
    public void TheWarning_PointsAtTheMark()
    {
        const string music = "c'4 c' c'@rit c' |";
        var source = $"octave absolute part m {{ clef treble }} "
                     + $"section A {{ m {{ {music} }} }} form main {{ A }} score main {{ staff m }}";
        var warning = Assert.Single(Warnings(music));
        Assert.Equal(source.IndexOf("@rit", StringComparison.Ordinal), warning.Span.Start);
    }

    /// <summary>
    /// ONE ROOT CAUSE, ONE DIAGNOSTIC. A form that repeats a section plays the same written
    /// mark twice, and the collector's mark table holds one instance per playing — but the
    /// reader forgot one terminator, not two.
    /// </summary>
    [Fact]
    public void AnUnterminatedMarkInARepeatedSection_IsReportedOnce()
    {
        var source = "octave absolute part m { clef treble } "
                     + "section A { m { c'4@rit c' c' c' | } } "
                     + "section B { m { d'4 d' d' d' | } } "
                     + "form main { A B A } score main { staff m }";
        var validator = new SpanPairingValidator();
        validator.Validate(SyntaxTree.Parse(source));

        var warnings = validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnpairedSpan)
            .ToList();

        // Two faults at ONE position, not one fault twice: playing 2 finds a span already
        // open, and playing 1's is what is left unterminated at the end.
        Assert.Equal(2, warnings.Count);
        Assert.Single(warnings.Select(w => w.Span.Start).Distinct());
    }

    /// <summary>
    /// The pairing is per VOICE, because that is the context LilyPond keeps the engraver in
    /// (ly/engraver-init.ly:375). A terminator in the other voice reaches nothing, so BOTH
    /// marks are unpaired — the same answer LilyPond gives, and two complaints rather than
    /// a silent span that crosses a boundary LilyPond will not cross.
    /// </summary>
    [Fact]
    public void ATerminatorInAnotherVoice_ClosesNothing()
    {
        var warnings = Warnings("<< { c'4@rit c' c' c' } \\\\ { e4@!rit e e e } >> |");

        Assert.Equal(2, warnings.Count);
        Assert.Single(warnings, w => w.Message.Contains("never closed"));
        Assert.Single(warnings, w => w.Message.Contains("closes nothing"));
    }

    // ---- the ottava, on the same rule ----

    [Theory]
    [InlineData("c'4@ottava c' c' c'@!ottava |")]
    [InlineData("c'4@quindicesima c' c' c'@!ottava |")]   // one stop for the family
    [InlineData("c'4@ottava(bassa) c' c' c'@!ottava |")]
    public void APairedOttava_NoWarning(string music) =>
        Assert.Equal(0, WarningCount(music));

    [Fact]
    public void AnUnterminatedOttava_IsReported()
    {
        var warning = Assert.Single(Warnings("c'4@ottava c' c' c' |"));
        Assert.Contains("never closed", warning.Message);
        // The ottava loses the TRANSPOSITION with its bracket, and the message says so —
        // that is the half a reader cannot see by looking at the page.
        Assert.Contains("not transposed", warning.Message);
    }

    [Fact]
    public void AnOttavaTerminatorWithNothingOpen_IsReported()
    {
        var warning = Assert.Single(Warnings("c'4@!ottava c' c' c' |"));
        Assert.Contains("closes nothing", warning.Message);
        // ...and it reads as English: the article belongs to the family's noun, not glued on.
        Assert.Contains("no ottava bracket is open", warning.Message);
    }

    /// <summary>
    /// ⚠️ CONSECUTIVE OTTAVAS ARE A CHANGE, NOT A NESTING, so no complaint: LilyPond finishes
    /// the open span on any ottava event and starts the new one. Only the TEXT spanner
    /// refuses a second start.
    /// </summary>
    [Fact]
    public void ConsecutiveOttavas_AreNotAFault() =>
        Assert.Equal(0, WarningCount(
            "c'4@ottava c' | c'@ottava(bassa) c' | c'@!ottava c' |"));

    // ---- the pedal: the two faults it has, and the one it does NOT ----

    [Fact]
    public void AnUnreleasedPedal_IsReported()
    {
        // MEASURED (scratch/p289/pedopen.lys): the bracket vanishes entirely. Nothing about
        // the drawing changed here — what changed is that the loss is now said.
        var warning = Assert.Single(Warnings("c'4@sustainOn c' c' c' |"));
        Assert.Contains("never closed", warning.Message);
    }

    [Fact]
    public void APedalReleaseWithNothingDown_IsReported()
    {
        var warning = Assert.Single(Warnings("c'4@sustainOff c' c' c' |"));
        Assert.Contains("closes nothing", warning.Message);
    }

    /// <summary>
    /// ⚠️ RE-PEDALLING IS NOT A FAULT, and this is the one place the families differ. A
    /// second <c>@sustainOn</c> while the pedal is down is what a pianist does and what
    /// "Ped. … Ped." means; it closes the open bracket and opens a new one. A second text
    /// spanner inside an open one is refused instead.
    /// </summary>
    [Fact]
    public void RePedalling_IsNotAFault()
    {
        Assert.Equal(0, WarningCount(
            "c'4@sustainOn c'@sustainOff@sustainOn c' c'@sustainOff |"));
        // ...and the bare re-pedal (no explicit release between) is not one either.
        Assert.Equal(0, WarningCount("c'4@sustainOn c'@sustainOn c' c'@sustainOff |"));
    }

    [Fact]
    public void EachPedalIsItsOwnSpan()
    {
        // A una corda does not release a sustain: three pedals, three spans.
        Assert.Equal(0, WarningCount(
            "c'4@sustainOn@unaCorda c' c'@treCorde c'@sustainOff |"));
        // ...so a sustain closed by the WRONG release leaves both unpaired.
        Assert.Equal(2, WarningCount("c'4@sustainOn c' c' c'@treCorde |"));
    }
}
