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
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Everything a <c>grace { }</c> body drops is reported (LYS4020), and nothing it draws is.
/// </summary>
/// <remarks>
/// ⚠️ THE NET IS TIED TO THE INK, not to the validator's own opinion.
/// <see cref="EverythingReported_IsAbsentFromThePage"/> renders each reported spelling
/// against a control that does not write it and asserts the two pages are the same with
/// <c>data-pos</c> masked — so the warning and the silence are measured together. A
/// validator that drifted ahead of the collector (warning about something now drawn) or
/// behind it (silent about something still dropped) fails there, which is the whole reason
/// the narrowing is stated once in <see cref="GraceBodySupport"/> and read twice.
/// <para>
/// ⚠️ THIS FILE IS SUPPOSED TO GO RED WHEN THE HOLE CLOSES, and that is not a regression.
/// The day <c>@staccato</c> reaches a grace note, its row in the theory stops being
/// page-identical to the control and this test names the exact line to delete. See
/// docs/HANDOFF.md §2 U8 for the design that closes it.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class GraceBodyValidatorTests
{
    private static IReadOnlyList<Diagnostic> Warnings(string source)
    {
        var validator = new GraceBodyValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnengravedGraceContent
                        && d.Severity == DiagnosticSeverity.Warning)
            .ToList();
    }

    private static string Book(string music)
        => "part m { clef treble }\nsection A { m {\n" + music + "\n} }\n"
           + "form main { ~A }\nscore main { staff m }\n";

    /// <summary>The page with every source offset masked: two books that write the same
    /// music at different lengths differ in <c>data-pos</c> and in nothing else.</summary>
    private static string Page(string music)
        => Regex.Replace(LiveRender.Svg(Book(music)), "data-pos=\"\\d+\"", "data-pos=\"#\"");

    /// <summary>
    /// Each spelling a grace body drops, paired with the control that writes the same music
    /// without it. The warning and the missing ink are asserted together.
    /// </summary>
    [Theory]
    [InlineData("grace { d'8@staccato } c'1 | e'1 |", "grace { d'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8@text(\"hi\") } c'1 | e'1 |", "grace { d'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8@f } c'1 | e'1 |", "grace { d'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8@finger(3) } c'1 | e'1 |", "grace { d'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8@trill } c'1 | e'1 |", "grace { d'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8. } c'1 | e'1 |", "grace { d'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8( e'8) } c'1 | e'1 |", "grace { d'8 e'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8[ e'8] } c'1 | e'1 |", "grace { d'8 e'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8~ d'8 } c'1 | e'1 |", "grace { d'8 d'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8 r8 } c'1 | e'1 |", "grace { d'8 } c'1 | e'1 |")]
    [InlineData("grace { <d' f'>8 } c'1 | e'1 |", "c'1 | e'1 |")]
    [InlineData("grace { tuplet 3/2 { d'8 e'8 f'8 } } c'1 | e'1 |", "c'1 | e'1 |")]
    public void EverythingReported_IsAbsentFromThePage(string written, string control)
    {
        Assert.NotEmpty(Warnings(Book(written)));
        Assert.Equal(Page(control), Page(written));
    }

    /// <summary>
    /// The grace body that draws exactly what it says draws no warning either. This is the
    /// case that catches a validator that has quietly become "warn about every grace".
    /// </summary>
    [Theory]
    [InlineData("grace { d'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8 e'8 } c'1 | e'1 |")]
    [InlineData("acciaccatura { d'16 } c'1 | e'1 |")]
    [InlineData("appoggiatura { dis'8 } c'1 | e'1 |")]
    [InlineData("c'4@staccato d'4@text(\"hi\") e'4 f'4 |")]
    // The two a grace note carries. The string number draws nothing on a NOTATION staff
    // either way (`c'4\2` and `c'4` render byte-identical), so silence here is the same
    // answer the rest of the engine gives it, not a hole.
    [InlineData("grace { d'8@mark(\"P\") } c'1 | e'1 |")]
    [InlineData("grace { d'8\\2 } c'1 | e'1 |")]
    public void AGraceThatDrawsWhatItWrites_IsNotReported(string music)
        => Assert.Empty(Warnings(Book(music)));

    /// <summary>
    /// A body with no bare note in it draws NO grace at all, and the warning says so — the
    /// difference between "an ornament lost a dot" and "a whole ornament is missing" is the
    /// one a reader needs to hear.
    /// </summary>
    [Fact]
    public void ABodyWithNoBareNote_SaysTheWholeGraceIsGone()
    {
        Assert.Contains("NO grace note is drawn at all",
            Assert.Single(Warnings(Book("grace { <d' f'>8 } c'1 | e'1 |"))).Message);

        // A body that still holds one bare note keeps its grace, so the sentence stays off.
        Assert.DoesNotContain("NO grace note is drawn at all",
            Assert.Single(Warnings(Book("grace { d'8 <e' g'>8 } c'1 | e'1 |"))).Message);
    }

    /// <summary>
    /// The warning stands at what was written, not at the grace: a report that cannot be
    /// clicked is a report about the file rather than about the annotation.
    /// </summary>
    [Fact]
    public void TheWarningStandsAtWhatWasWritten()
    {
        string source = Book("grace { d'8@staccato } c'1 | e'1 |");
        var warning = Assert.Single(Warnings(source));
        Assert.Equal(source.IndexOf("@staccato", System.StringComparison.Ordinal),
            warning.Span.Start);
    }

    /// <summary>
    /// The rehearsal mark is NOT reported, because it is drawn. Its grob is the Score's
    /// (ly/engraver-init.ly:729,764 Mark_engraver), so it never needed the note's column —
    /// which is exactly why it is the one that works while the note-anchored families do not.
    /// </summary>
    [Fact]
    public void ARehearsalMarkOnAGraceNote_IsDrawnAndNotReported()
    {
        Assert.Empty(Warnings(Book("grace { d'8@mark(\"P\") } c'1 | e'1 |")));
        Assert.Contains(">P</text>", LiveRender.Svg(Book("grace { d'8@mark(\"P\") } c'1 | e'1 |")));

        // ...and the page really is different from the one that does not write it, so the
        // row above is not "identical to a control" by accident.
        Assert.NotEqual(Page("grace { d'8 } c'1 | e'1 |"),
                        Page("grace { d'8@mark(\"P\") } c'1 | e'1 |"));
    }

    /// <summary>
    /// One written mark is one printed mark, however many staves walk it. A part drawn on
    /// both a staff and a tab walks its grace once per staff, which is the shape that made
    /// the de-dupe real for the note-level arm (MeasureCollector.CollectArticulations).
    /// </summary>
    [Fact]
    public void AGraceMarkOnAPartDrawnTwice_IsPrintedOnce()
    {
        string source =
            "part m { clef treble }\n"
            + "section A { m { grace { d'8@mark(\"P\") } c'1 | e'1 | } }\n"
            + "form main { ~A }\nscore main { staff m tab m }\n";
        string svg = Regex.Replace(
            LiveRender.SvgFromRenderSpec(source), "data-pos=\"\\d+\"", "data-pos=\"#\"");
        Assert.Equal(1, Regex.Matches(svg, ">P</text>").Count);
    }

    /// <summary>
    /// A <c>\N</c> on a grace note picks that string on a TAB, and is not reported. It was
    /// ignored until session 298 and nothing said so — found by LYS4020 on the reader's own
    /// <c>Real Gone.lys</c>, which writes <c>grace { a,16\2 }</c> twice and drew both on
    /// whatever string the resolver picked. The three readings must differ from each other:
    /// asserting only "\2 is not auto" would pass on an engine that honoured the annotation
    /// by ignoring the number.
    /// </summary>
    [Fact]
    public void AStringNumberOnAGraceNote_PicksThatStringOnATab()
    {
        static string Tab(string grace) => Regex.Replace(
            LiveRender.SvgFromRenderSpec(
                "octave absolute\nkey a major\ntime 3/4\n"
                + "part bs { clef bass tuning bass }\n"
                + "section S { bs { " + grace + " b,8\\2 a,\\2 d, | } }\n"
                + "form main { ~S }\nscore main { tab bs }\n"),
            "data-pos=\"\\d+\"", "data-pos=\"#\"");

        string auto = Tab("grace { a,16 }");
        string s2 = Tab("grace { a,16\\2 }");
        string s3 = Tab("grace { a,16\\3 }");

        Assert.Empty(Warnings("octave absolute\npart bs { clef bass tuning bass }\n"
            + "section S { bs { grace { a,16\\2 } b,8 | } }\n"
            + "form main { ~S }\nscore main { tab bs }\n"));
        Assert.NotEqual(auto, s2);
        Assert.NotEqual(auto, s3);
        Assert.NotEqual(s2, s3);
    }
}
