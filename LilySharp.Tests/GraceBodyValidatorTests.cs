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
    // ⚠️ `grace { d'8. }` LEFT THIS THEORY ON 2026-08-30 (session 299), which is the way a
    // row is meant to leave it: the dot is drawn now, so the line asserting it is missing
    // went red and taking it out was part of closing the hole. See ADottedGrace_IsDrawn.
    [InlineData("grace { d'8( e'8) } c'1 | e'1 |", "grace { d'8 e'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8[ e'8] } c'1 | e'1 |", "grace { d'8 e'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8~ d'8 } c'1 | e'1 |", "grace { d'8 d'8 } c'1 | e'1 |")]
    [InlineData("grace { d'8 r8 } c'1 | e'1 |", "grace { d'8 } c'1 | e'1 |")]
    [InlineData("grace { <d' f'>8 } c'1 | e'1 |", "c'1 | e'1 |")]
    // ⚠️ `grace { tuplet 3/2 { … } }` LEFT THIS THEORY ON 2026-08-30 (session 302), and it
    // left the way a row is meant to: the tuplet is a CONTAINER, its notes are engraved now,
    // and this row went red on its own and named itself as the line to delete. What is still
    // reported is the bracket and the number — a GraceDropKind.Bracket, which this theory
    // cannot hold, because its whole shape is "reported ⇒ page-identical to a control that
    // does not write it" and a bracket drop is reported off a page that DID change. See
    // ATupletInAGraceBody_EngravesWhatItHolds for the row that replaced it.
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
    // The dot, carried since session 299. It never wanted the note's COLUMN — it hangs off
    // the grace's own head — which is why it could be closed while @staccato still cannot.
    [InlineData("grace { d'8. } c'1 | e'1 |")]
    [InlineData("grace { d'8.. e'16 } c'1 | e'1 |")]
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

    /// <summary>
    /// A dotted grace is drawn dotted — one dot glyph more than the same grace without the
    /// dot, and the dot is the ONLY difference on the page.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SECOND ASSERT IS THE ONE THAT BITES. "One more dot" alone would pass on an
    /// engine that also moved the head, the flag or the main note; masking the dots out of
    /// both pages and demanding the rest be identical says the dot is ink ADDED rather than
    /// a re-engraving. It is also what LilyPond does: on 2.26.0 the dotted book and its
    /// control differ by exactly the one added glyph and no coordinate anywhere else
    /// (scratch/p299/lp, dot.svg against nodot.svg).
    /// </remarks>
    [Fact]
    public void ADottedGrace_IsDrawn()
    {
        string dotted = Page("grace { d'8. } c'1 | e'1 |");
        string plain = Page("grace { d'8 } c'1 | e'1 |");

        Assert.Equal(CountDots(plain) + 1, CountDots(dotted));
        Assert.Equal(StripDots(plain), StripDots(dotted));
    }

    /// <summary>
    /// Two dots are two dots, and an undurated grace after a dotted one inherits the DOTS
    /// with the duration — LilyPond's <c>optional_notemode_duration</c> carries the whole
    /// duration forward, not just its value (lily/parser.yy:3510-3516).
    /// </summary>
    [Fact]
    public void GraceDots_StackAndCarryForward()
    {
        Assert.Equal(CountDots(Page("grace { d'8 } c'1 | e'1 |")) + 2,
            CountDots(Page("grace { d'8.. } c'1 | e'1 |")));
        // `e'` writes no duration, so it is a dotted eighth too: two heads, two dots.
        Assert.Equal(CountDots(Page("grace { d'8 e'8 } c'1 | e'1 |")) + 2,
            CountDots(Page("grace { d'8. e' } c'1 | e'1 |")));
    }

    /// <summary>
    /// WHERE the dot stands turns on whether its row is one the grace's FLAG occupies — the
    /// pair that measures <see cref="LilySharp.Core.Svg.Layout.DotColumn"/>'s Y gate.
    /// </summary>
    /// <remarks>
    /// MEASURED on LilyPond 2.26.0 (scratch/p299/lp): <c>grace { e'8. }</c> sits in a space,
    /// keeps its dot on its own row, and puts it 1.226600 right of the head — the head's ink
    /// right plus one grace dot. <c>grace { d'8. }</c> sits on a line, so the dot is lifted
    /// one position into the flag, and LilyPond moves it out to 1.747300 — the FLAG's right
    /// edge plus the same dot. The difference, 0.520688, is flag-right minus head-right.
    /// ⚠️ Asserting the two are DIFFERENT is the whole test: an engine with a flat
    /// "head plus a dot" rule draws them at the same offset and is wrong about one of them.
    /// </remarks>
    [Fact]
    public void AGraceDotClearsTheFlagOnlyWhenTheFlagIsOnItsRow()
    {
        // The two positions the measured books engrave: `d'` lands on staff position 2 (a
        // LINE) and `e'` on 3 (a SPACE). Both dots end up on row 3 — one lifted, one not.
        static (double X, System.Collections.Immutable.ImmutableArray<int> Positions) At(
            int staffPosition, bool beamed)
            => LilySharp.Core.Svg.Layout.GraceNoteEngraver.Dots(
                new LilySharp.Core.Svg.Model.GraceNoteInfo(
                    staffPosition, null, false, Fraction.Eighth, Dots: 1),
                beamed);

        var onLine = At(2, beamed: false);
        var inSpace = At(3, beamed: false);

        Assert.Equal(3, Assert.Single(onLine.Positions));
        Assert.Equal(3, Assert.Single(inSpace.Positions));
        // Same row, different offset — because the flag hangs lower over the head that had
        // to lift its dot to get there.
        Assert.Equal(1.7473, onLine.X, 4);
        Assert.Equal(1.2266, inSpace.X, 4);

        // A BEAMED run has no flag at all, so the lifted dot stays at the head's right.
        Assert.Equal(1.2266, At(2, beamed: true).X, 4);
    }

    /// <summary>
    /// The COLLECTOR carries the dot into the model. Asserted on the model rather than the
    /// page because that is the only thing that tells "the dot was never read" from "the dot
    /// was read and never drawn" — two poisons that otherwise turn the same two tests red.
    /// </summary>
    [Fact]
    public void TheCollectorCarriesTheDot()
    {
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(Book("grace { d'8. e' f'16 } c'1 | e'1 |")));
        var grace = Assert.Single(score.GraceNotes);
        Assert.Equal(new[] { 1, 1, 0 }, grace.Notes.Select(n => n.Dots).ToArray());
        // …and the note VALUE is untouched by it: a dotted eighth is still an eighth, or its
        // flag and its beam count would change with the dot.
        Assert.Equal(new[] { 8, 8, 16 },
            grace.Notes.Select(n => (int)n.BaseDuration.Denominator).ToArray());
    }

    /// <summary>
    /// SPACING reads the dotted MOMENT, not the note value — a dotted grace pushes the next
    /// grace column further than an undotted one of the same value.
    /// </summary>
    /// <remarks>
    /// MEASURED on LilyPond 2.26.0 (scratch/p299/lp, u_mixdot_a against u_mixdot_b):
    /// <c>grace { d'8. e'16 }</c> puts its second head 2.915900 after its first and
    /// <c>grace { d'8 e'16 }</c> puts it at 2.448000 — a difference of 0.467900. The spring
    /// law says the difference is <c>spacing-increment × log2(3/2)</c> = 0.8 × 0.584963 =
    /// 0.467970, which is LilyPond's own number to the four places it prints.
    /// ⚠️ THE DELTA, NOT THE ABSOLUTE. Lily# draws this gap 0.246 short of LilyPond in BOTH
    /// books — an older divergence on the mixed-duration grace run, which the ledger's
    /// <c>grace.column.*</c> island already owns. Asserting the absolute here would pin that
    /// residual into this test and hide it when it is repaired.
    /// </remarks>
    [Fact]
    public void GraceSpacingReadsTheDottedMoment()
    {
        static double SecondColumn(int dots)
            => LilySharp.Core.Svg.Layout.SpacingRules.GraceColumns(
                System.Collections.Immutable.ImmutableArray.Create(
                    new LilySharp.Core.Svg.Model.GraceNoteInfo(
                        2, null, false, Fraction.Eighth, Dots: dots),
                    new LilySharp.Core.Svg.Model.GraceNoteInfo(
                        3, null, false, Fraction.Sixteenth)),
                mainItem: null).Offsets[1];

        Assert.Equal(0.46797, SecondColumn(1) - SecondColumn(0), 5);
    }

    /// <summary>The augmentation dot as it reaches the page — the glyph char itself, so
    /// this counts ink rather than a spelling of the markup around it.</summary>
    private static readonly string DotGlyph =
        LilySharp.Core.Svg.EmmentalerGlyphs.AugmentationDot.ToString();

    private static int CountDots(string page) => page.Split(DotGlyph).Length - 1;

    /// <summary>The page with every dot glyph taken out, and whitespace normalised so that
    /// the hole a removed element leaves is not itself a difference.</summary>
    private static string StripDots(string page)
        => Regex.Replace(
            Regex.Replace(page, "<text[^>]*>" + Regex.Escape(DotGlyph) + "</text>", ""),
            @"\s+", " ");

    // ---------------------------------------------------------------------------------
    // The PHRASE REFERENCE, carried since session 300. It is the one element of the
    // "engraves no grace at all" family that names no grob — it names music written
    // elsewhere — so it is expanded rather than engraved, and every other container in the
    // grammar already expanded one (scratch/p194/four-containers.lys checks the four side
    // by side and grace was the only one that dropped it).
    // ---------------------------------------------------------------------------------

    /// <summary>A book in ABSOLUTE octaves, so a phrase's fresh relative frame is not a
    /// difference between the two sides of an ink comparison; the frame has a pair of its
    /// own below (<see cref="APhraseInAGraceBody_ReadsAFreshFrame"/>).</summary>
    private static string PhraseBook(string phrases, string music)
        => "octave absolute\npart m { clef treble }\n" + phrases
           + "\nsection A { m {\n" + music + "\n} }\n"
           + "form main { ~A }\nscore main { staff m }\n";

    private static string PhrasePage(string phrases, string music)
        => Regex.Replace(
            LiveRender.Svg(PhraseBook(phrases, music)), "data-pos=\"\\d+\"", "data-pos=\"#\"");

    /// <summary>
    /// <c>grace { G }</c> engraves what <c>G</c> holds — the same page, to the byte, as
    /// writing those notes in the body — and says nothing about it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE THIRD ASSERT IS THE ONE THAT KEEPS THE FIRST TWO HONEST. "Equal to the inline
    /// control" is also what a body that engraves NOTHING would satisfy if the control were
    /// wrong; demanding that the page differ from the one with no grace at all says the
    /// reference put ink on the page rather than agreeing with a second silence. That is the
    /// exact failure this row is written against: before session 300 the page WAS the one
    /// with no grace at all.
    /// </remarks>
    [Theory]
    // The plain case, and the nested one: a phrase body may reference another phrase.
    [InlineData("phrase G { d'16 e' }", "grace { G } c'1 | e'1 |", "grace { d'16 e' } c'1 | e'1 |")]
    [InlineData("phrase I { d'16 e' }\nphrase O { I f'16 }",
                "grace { O } c'1 | e'1 |", "grace { d'16 e' f'16 } c'1 | e'1 |")]
    // Mixed with bare notes on both sides of the reference.
    [InlineData("phrase G { d'16 e' }",
                "grace { c'16 G a'16 } c'1 | e'1 |", "grace { c'16 d'16 e' a'16 } c'1 | e'1 |")]
    public void APhraseReferenceInAGraceBody_EngravesWhatThePhraseHolds(
        string phrases, string written, string control)
    {
        Assert.Empty(Warnings(PhraseBook(phrases, written)));
        Assert.Equal(PhrasePage("", control), PhrasePage(phrases, written));
        Assert.NotEqual(PhrasePage("", "c'1 | e'1 |"), PhrasePage(phrases, written));
    }

    /// <summary>
    /// A phrase body is evaluated in a FRESH relative frame inside a grace, exactly as it is
    /// everywhere else: the same reference draws the same pitches whatever the grace played
    /// before it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SECOND HALF IS NOT DECORATION. "Both books agree" is also what a collector that
    /// had stopped resolving pitches at all would say, so the INLINE spelling of the same two
    /// notes is asserted to DISAGREE across the same pair — the running frame is live, and
    /// the reference is what stands outside it.
    /// </remarks>
    [Fact]
    public void APhraseInAGraceBody_ReadsAFreshFrame()
    {
        static int[] Positions(string phrases, string music)
            => new LilySharp.Core.Svg.Collector.MeasureCollector()
                .Collect(SyntaxTree.Parse(
                    "part m { clef treble }\n" + phrases
                    + "\nsection A { m {\n" + music + "\n} }\n"
                    + "form main { ~A }\nscore main { staff m }\n"))
                .GraceNotes.Single().Notes.Select(n => n.StaffPosition).ToArray();

        const string G = "phrase G { d16 e }";
        Assert.Equal(
            Positions(G, "c'2 grace { G } c'2 | e'1 |"),
            Positions(G, "c,,2 grace { G } c'2 | e'1 |"));

        Assert.NotEqual(
            Positions("", "c'2 grace { d16 e } c'2 | e'1 |"),
            Positions("", "c,,2 grace { d16 e } c'2 | e'1 |"));
    }

    /// <summary>
    /// A reference inside a grace hands the relative chain back at the phrase's ANCHOR, the
    /// same as one in the main stream — the chord rule, so a phrase's interior never leaks
    /// into the note written after the grace.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE PAIR IS THE POINT. Equality with the main-stream reference alone would also
    /// hold for an engine that had stopped moving the frame at all, so the INLINE spelling of
    /// the same two notes is asserted to leave it somewhere ELSE: <c>grace { d16 d' }</c>
    /// ends an octave up and hands THAT over, while <c>grace { G }</c> hands over G's anchor,
    /// the bare d its body opens with — a whole octave between the two answers.
    /// </remarks>
    [Fact]
    public void APhraseInAGraceBody_HandsTheChainBackAtItsAnchor()
    {
        static int FirstNote(string phrases, string music, int index)
        {
            var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
                .Collect(SyntaxTree.Parse(
                    "part m { clef treble }\n" + phrases
                    + "\nsection A { m {\n" + music + "\n} }\n"
                    + "form main { ~A }\nscore main { staff m }\n"));
            return score.Voice.Measures[0].Items
                .OfType<LilySharp.Core.Svg.Model.NoteItem>().ElementAt(index).StaffPosition;
        }

        const string G = "phrase G { d16 d' }";
        int afterGrace = FirstNote(G, "grace { G } c2 c2 | e'1 |", 0);
        int afterReference = FirstNote(G, "G c2 c2 | e'1 |", 2);
        int afterInline = FirstNote("", "grace { d16 d' } c2 c2 | e'1 |", 0);

        Assert.Equal(afterReference, afterGrace);
        Assert.NotEqual(afterInline, afterGrace);
    }

    /// <summary>
    /// The two expanders offer the SAME body: the notes <c>grace { G }</c> engraves are the
    /// notes <c>G</c> engraves in the main stream, pitch for pitch.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE DIFFERENTIAL NET UNDER A SECOND SPELLING. Phrase expansion is written
    /// twice — <c>MeasureCollector.ExpandVariable</c> for the main stream and
    /// <c>GraceBodySupport.BodyElements</c> for a grace body — and the remarks on the latter
    /// say why they cannot be folded (the collector's is an instance method the validator
    /// cannot reach, and it flattens containers, which a grace body must not). Checklist 7.7
    /// says that a pair which cannot be folded gets a net that asks BOTH the same question
    /// and compares the answers, which is what this is: it needs no hand-written expected
    /// pitches, so it survives every change to what those pitches are.
    /// </remarks>
    [Fact]
    public void APhraseInAGraceBody_OffersTheSameBodyTheMainStreamDoes()
    {
        static LilySharp.Core.Svg.Model.Score Collect(string music)
            => new LilySharp.Core.Svg.Collector.MeasureCollector()
                .Collect(SyntaxTree.Parse(PhraseBook("phrase G { d'16 e' f'16 }", music)));

        var asGrace = Assert.Single(Collect("grace { G } c'1 | e'1 |").GraceNotes)
            .Notes.Select(n => n.StaffPosition).ToArray();
        var asMusic = Collect("G c'1 | e'1 |").Voice.Measures[0].Items
            .OfType<LilySharp.Core.Svg.Model.NoteItem>()
            .Take(asGrace.Length).Select(n => n.StaffPosition).ToArray();

        Assert.Equal(asMusic, asGrace);
        // …and the phrase really has three notes, so the comparison is not two empty arrays
        // — the failure that has cost this repository two whole sessions (RULES §5.4).
        Assert.Equal(3, asGrace.Length);
    }

    /// <summary>
    /// A phrase boundary resets the GRACE GROUP's duration memory and leaves the VOICE's
    /// alone — the two are different memories, and a grace body only ever reads the first.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SECOND HALF IS WHY <c>EnterDefaultFrame</c> IS NOT CALLED HERE. That helper
    /// resets both, and resetting the voice's would let <c>grace { G }</c> change the
    /// duration of the note AFTER the grace — an effect on the host stream that the same
    /// music written inline (<c>grace { d'16 }</c>) has no way to produce. The rule is not
    /// "a grace changes nothing", it is "a phrase resets what the thing reading it reads".
    /// </remarks>
    [Fact]
    public void APhraseBoundaryResetsTheGracesDurationMemoryOnly()
    {
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(
                PhraseBook("phrase G { d' }", "c'2 grace { c'16 G } c' | e'1 |")));

        // c'16 is a sixteenth; the phrase's undurated d' takes the GROUP's default eighth
        // rather than inheriting the sixteenth across the boundary.
        Assert.Equal(new[] { 16, 8 },
            Assert.Single(score.GraceNotes).Notes
                .Select(n => (int)n.BaseDuration.Denominator).ToArray());

        // …and the half note before the grace still owns the VOICE's duration memory: the
        // undurated c' after the grace is a HALF, so the measure closes on the meter.
        // ⚠️ THE TRAILING NOTE MUST BE UNDURATED, and the first version of this line was not
        // (`c'4 c'4`): with explicit durations nothing reads the voice's memory, the poison
        // that clears it turned NOTHING red, and this assert was decoration. A reset to a
        // QUARTER is also invisible against a book whose notes are quarters — the half is
        // what makes the two answers differ.
        Assert.Equal(new Fraction(1, 1),
            score.Voice.Measures[0].Items.Aggregate(
                new Fraction(0, 1), (total, item) => total + item.Duration));
    }

    /// <summary>
    /// What a referenced phrase holds that a grace body still drops is reported at the span
    /// it was WRITTEN at — inside the phrase's declaration — and the message names the
    /// phrase, because that line has no <c>grace</c> anywhere on it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE HALF THAT WOULD ROT SILENTLY. Expanding the reference in the collector
    /// alone would leave the validator stopping at the reference: it would say nothing about
    /// the chord one level down and, worse, would go on calling a body that engraves two
    /// grace notes "no grace note at all". Both readers take the same expansion
    /// (<c>GraceBodySupport.BodyElements</c>) for that reason.
    /// </remarks>
    [Fact]
    public void ADropInsideAReferencedPhrase_IsReportedThereAndNamesThePhrase()
    {
        string source = PhraseBook("phrase C { <c' e'>16 }", "grace { C } c'1 | e'1 |");
        var warning = Assert.Single(Warnings(source));

        Assert.Equal(source.IndexOf("<c' e'>", System.StringComparison.Ordinal),
            warning.Span.Start);
        Assert.Contains("a chord inside 'grace { C }'", warning.Message);
        Assert.Contains("NO grace note is drawn at all", warning.Message);

        // A phrase that holds a bare note as well keeps its grace, so the sentence goes off
        // — the "engraves nothing" question is asked of the EXPANDED body.
        Assert.DoesNotContain("NO grace note is drawn at all",
            Assert.Single(Warnings(
                PhraseBook("phrase C { d'16 <c' e'>16 }", "grace { C } c'1 | e'1 |"))).Message);
    }

    /// <summary>
    /// However little budget the expansion is given, it hands back frames in PAIRS — one
    /// <c>PhraseEndMarker</c> for every <c>RelativeResetMarker</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE NET UNDER THE BUDGET, and it is here because the budget has no other
    /// observer: an acyclic phrase DAG doubles per level and this walk runs on the LSP's
    /// per-keystroke pass, so the charge has to be able to stop mid-body — and a stop between
    /// the two markers would leave <c>MeasureCollector</c>'s phrase-transpose stack pushed
    /// and never popped, which is a wrong OCTAVE somewhere later in the book rather than a
    /// crash. The rule that makes it safe is the one <c>ExpandVariable</c> already uses: a
    /// phrase whose ENTRY cannot be paid for emits nothing at all, balanced by omission.
    /// </remarks>
    [Fact]
    public void AGraceBodyExpansion_ClosesEveryFrameItOpens()
    {
        var tree = SyntaxTree.Parse(PhraseBook(
            "phrase I { d'16 e' }\nphrase O { I f'16 }",
            "grace { c'16 O g'16 } c'1 | e'1 |"));
        var bodies = tree.GetRoot().DescendantNodes().OfType<PhraseDeclarationSyntax>()
            .ToDictionary(p => p.Name.Text, p => (SyntaxNode)p.Body);
        var grace = tree.GetRoot().DescendantNodes()
            .OfType<GraceExpressionSyntax>().Single();

        for (int cap = 0; cap <= 10; cap++)
        {
            int left = cap;
            var elements = GraceBodySupport.BodyElements(
                grace, name => bodies.GetValueOrDefault(name), () => left-- > 0);

            Assert.Equal(
                elements.Count(e => e.Node is LilySharp.Core.Svg.Collector.RelativeResetMarker),
                elements.Count(e => e.Node is LilySharp.Core.Svg.Collector.PhraseEndMarker));
        }

        // …and with budget enough, the nested phrase really did open two frames — otherwise
        // the loop above is asserting 0 == 0 eleven times.
        int plenty = 100;
        Assert.Equal(2, GraceBodySupport
            .BodyElements(grace, name => bodies.GetValueOrDefault(name), () => plenty-- > 0)
            .Count(e => e.Node is LilySharp.Core.Svg.Collector.RelativeResetMarker));
    }

    /// <summary>
    /// A name that cannot be expanded — undeclared, or a cycle — is still reported as the
    /// reference itself, and the expansion terminates.
    /// </summary>
    /// <remarks>
    /// The cycle is <see cref="PhraseCycleValidator"/>'s to explain and an undeclared name is
    /// <c>SymbolReferenceValidator</c>'s; what this test owns is that neither turns the grace
    /// body's own report off, and that <c>phrase X { X }</c> does not walk forever.
    /// </remarks>
    [Theory]
    [InlineData("", "grace { Nope } c'1 | e'1 |")]
    [InlineData("phrase X { X }", "grace { X } c'1 | e'1 |")]
    public void AReferenceThatCannotExpand_IsStillReported(string phrases, string music)
    {
        Assert.Contains("a phrase reference",
            Assert.Single(Warnings(PhraseBook(phrases, music))).Message);
        // …and the page is the one with no grace on it, which is what the warning says.
        Assert.Equal(PhrasePage("", "c'1 | e'1 |"), PhrasePage(phrases, music));
    }

    /// <summary>
    /// <c>grace { tuplet 3/2 { … } }</c> engraves the notes the tuplet holds — the same page,
    /// to the byte, as writing them in the body without it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE THIRD ASSERT IS THE ONE THAT SAYS SOMETHING. "The page equals the control" is
    /// also what a body engraving NOTHING would satisfy if the control were wrong, and that is
    /// not hypothetical here: MEASURED before this trip started (scratch/p302/ab, both sides
    /// Release, data-pos masked) the page for this book was BYTE-IDENTICAL to the book with no
    /// grace in it at all, as were its MIDI and its MusicXML. So the row demands the page
    /// DIFFER from the no-grace one as well.
    /// <para>
    /// ⚠️ AND IT STILL WARNS, unlike the phrase-reference row. What is lost is the bracket and
    /// the number — the two grobs LilyPond adds and a grace column cannot hold — which is why
    /// this could not stay a row of <see cref="EverythingReported_IsAbsentFromThePage"/>: that
    /// theory pairs every warning with an unchanged page, and this one changes it.
    /// </para>
    /// </remarks>
    [Theory]
    // Beamed and unbeamed: on LilyPond 2.26.0 the first adds only the italic `3` and the
    // second adds the four bracket lines too (session 301, scratch/p301/lp). Lily# draws
    // neither, and the notes are the same either way — which is what is asserted.
    [InlineData("grace { tuplet 3/2 { d'16 e' f' } } c'1 | e'1 |",
                "grace { d'16 e' f' } c'1 | e'1 |")]
    [InlineData("grace { tuplet 3/2 { d'4 e' f' } } c'1 | e'1 |",
                "grace { d'4 e' f' } c'1 | e'1 |")]
    // Mixed with bare notes on both sides, and nested one level down.
    [InlineData("grace { c'16 tuplet 3/2 { d'16 e' f' } a'16 } c'1 | e'1 |",
                "grace { c'16 d'16 e' f' a'16 } c'1 | e'1 |")]
    [InlineData("grace { tuplet 3/2 { tuplet 3/2 { d'16 e' f' } } } c'1 | e'1 |",
                "grace { d'16 e' f' } c'1 | e'1 |")]
    public void ATupletInAGraceBody_EngravesWhatItHolds(string written, string control)
    {
        Assert.Equal(PhrasePage("", control), PhrasePage("", written));
        Assert.NotEqual(PhrasePage("", "c'1 | e'1 |"), PhrasePage("", written));
        Assert.NotEmpty(Warnings(PhraseBook("", written)));
    }

    /// <summary>
    /// The warning a tuplet draws names the BRACKET AND THE NUMBER, and says the notes are
    /// drawn — the difference between "an ornament lost its bracket" and "a whole ornament is
    /// missing" is the one a reader needs to hear, and it was the second sentence until this
    /// trip.
    /// </summary>
    [Fact]
    public void ATupletsWarning_NamesTheBracketAndSaysTheNotesAreDrawn()
    {
        string message = Assert.Single(
            Warnings(PhraseBook("", "grace { tuplet 3/2 { d'16 e' f' } } c'1 | e'1 |"))).Message;

        Assert.Contains("bracket", message);
        Assert.Contains("notes it holds ARE drawn", message);
        Assert.DoesNotContain("NO grace note is drawn at all", message);
    }

    /// <summary>
    /// …but when the tuplet holds nothing engravable either, the promise is withdrawn rather
    /// than left standing as a lie, and the chords inside it carry the "whole ornament is
    /// gone" half.
    /// </summary>
    [Fact]
    public void ATupletOfChords_DoesNotPromiseNotes()
    {
        var warnings = Warnings(PhraseBook("", "grace { tuplet 3/2 { <c' e'>16 <d' f'>16 } } c'1 | e'1 |"));

        Assert.DoesNotContain(warnings, w => w.Message.Contains("notes it holds ARE drawn"));
        Assert.Contains(warnings,
            w => w.Message.Contains("a chord") && w.Message.Contains("NO grace note is drawn at all"));
    }

    /// <summary>
    /// A tuplet boundary does NOT reset the grace group's duration memory, and that is the
    /// half that separates a tuplet from a phrase reference: a phrase opens a fresh frame
    /// (<see cref="APhraseBoundaryResetsTheGracesDurationMemoryOnly"/>) and a tuplet opens
    /// none — <c>tuplet 3/2 { d'16 e' f' } c'</c> gives the trailing c a sixteenth in the
    /// main stream, and a grace body is not an exception.
    /// </summary>
    [Fact]
    public void ATupletBoundaryKeepsTheDurationMemory()
    {
        static int[] Durations(string music)
            => new LilySharp.Core.Svg.Collector.MeasureCollector()
                .Collect(SyntaxTree.Parse(PhraseBook("", music)))
                .GraceNotes.Single().Notes
                .Select(n => (int)n.BaseDuration.Denominator).ToArray();

        // The undurated c' after the tuplet inherits the sixteenth written inside it…
        Assert.Equal(new[] { 16, 16, 16, 16 },
            Durations("grace { tuplet 3/2 { d'16 e' f' } c' } c'1 | e'1 |"));
        // …exactly as it does with no tuplet in the way. ⚠️ The pair is the point: the first
        // line alone also passes for a walker that reset the memory to the group's default
        // EIGHTH and then never read it, so the control fixes what "16" means here.
        Assert.Equal(Durations("grace { d'16 e' f' c' } c'1 | e'1 |"),
                     Durations("grace { tuplet 3/2 { d'16 e' f' } c' } c'1 | e'1 |"));
    }

    /// <summary>
    /// The tuplet markers are balanced at every budget, the same invariant the phrase pair
    /// carries — a stop between the two would leave a reader's tuplet stack pushed and never
    /// popped, which is a wrong LENGTH somewhere later in the piece rather than a crash.
    /// </summary>
    [Fact]
    public void ATupletExpansion_ClosesEveryBracketItOpens()
    {
        var tree = SyntaxTree.Parse(PhraseBook(
            "phrase P { tuplet 3/2 { d'16 e' f' } }",
            "grace { c'16 tuplet 5/4 { P g'16 } a'16 } c'1 | e'1 |"));
        var bodies = tree.GetRoot().DescendantNodes().OfType<PhraseDeclarationSyntax>()
            .ToDictionary(p => p.Name.Text, p => (SyntaxNode)p.Body);
        var grace = tree.GetRoot().DescendantNodes().OfType<GraceExpressionSyntax>().Single();

        List<GraceBodyElement> Expand(int cap)
        {
            int left = cap;
            return GraceBodySupport.BodyElements(
                grace, name => bodies.GetValueOrDefault(name), () => left-- > 0);
        }

        for (int cap = 0; cap <= 12; cap++)
            Assert.Equal(
                Expand(cap).Count(e => e.Node is LilySharp.Core.Svg.Collector.GraceTupletStartMarker),
                Expand(cap).Count(e => e.Node is LilySharp.Core.Svg.Collector.GraceTupletEndMarker));

        // …and with budget enough there really were two nested brackets, so the loop above is
        // not asserting 0 == 0 thirteen times.
        Assert.Equal(2, Expand(100)
            .Count(e => e.Node is LilySharp.Core.Svg.Collector.GraceTupletStartMarker));
    }
}
