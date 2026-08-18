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

using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The box a text grob reserves is the box its ink lands in — for the plain-text music
/// marks (D.S./Fine/To&#160;Coda/pedal words), for dynamic labels, and for text scripts.
/// </summary>
/// <remarks>
/// WHY THIS FILE EXISTS RATHER THAN A FIXTURE. The two overlap tests that read
/// <see cref="MusicMarkEngraver.MarkXExtent"/> compare a mark against inline chord symbols
/// above and against lyrics below, and NO book in the tracked corpus pairs a plain-text mark
/// with either: a 100-staff-space poison in that arm moved 0 of 567 books (2026-08-18). The
/// arm is live code on a page that happens not to exist yet, so the observer has to call it.
/// The inter-system mark box in <c>LayoutEngine</c> IS reached (the same poison there moved 3
/// books) — it was simply never priced from the drawn stencil.
/// <para>
/// ⚠️ WHAT THIS CANNOT SEE, stated so the next reader does not read more into a green run
/// than is here: these assert that the reservation and the draw ask for the same box, and
/// that the size and slant come from LilyPond's own declarations. They do NOT measure the
/// box against LilyPond — that is the four ledger points
/// <c>mark.jump.width.fine</c> / <c>.ds-al-coda</c> and <c>mark.pedal.width.sostenuto</c> /
/// <c>.sustain</c>, from audit/lp-geometry/probes/jump-mark-em.ly, and the last of them is
/// NOT closed: LilyPond sets the sustain word in music glyphs, not in a face.
/// <para>
/// The second repair landed in session 204 (the first, agreeing with the draw, was 203):
/// the em is LilyPond's paper text-font-size and the slant is its <c>font-shape italic</c>
/// with no series. Sixteen books moved, four of them tracked.
/// </para>
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class MarkReserveVersusDrawTests
{
    private static readonly ScoreTextMetrics Fonts = ScoreTextMetrics.Bundled;

    /// <summary>Every plain-text mark type, i.e. the arm under test.</summary>
    public static TheoryData<MusicMarkType> PlainTextMarks() => new()
    {
        MusicMarkType.Fine,
        MusicMarkType.DalSegno,
        MusicMarkType.DaCapo,
        MusicMarkType.DalSegnoAlFine,
        MusicMarkType.DalSegnoAlCoda,
        MusicMarkType.DaCapoAlFine,
        MusicMarkType.DaCapoAlCoda,
        MusicMarkType.SustainOn,
        MusicMarkType.SustainOff,
        MusicMarkType.SostenutoOn,
        MusicMarkType.UnaCordaOn,
        MusicMarkType.UnaCordaOff,
    };

    // ---- the reservation is the drawn advance, at the drawn size and style ----

    [Theory]
    [MemberData(nameof(PlainTextMarks))]
    public void MarkXExtent_SpansTheDrawnAdvance(MusicMarkType type)
    {
        var mark = new MusicMarkItem(type, measureIndex: 0, sourcePosition: 0);
        var (x0, x1) = MusicMarkEngraver.MarkXExtent(Fonts, mark, x: 0.0);

        // ⚠️ Through PlainMarkWidth, not TextFontMetrics: the family stopped being answered
        // by one call on 2026-08-18, when the sustain pedal's word became a run of MUSIC
        // glyphs. A test that spelled the text call here would assert the old mechanism.
        double drawn = MusicMarkEngraver.PlainMarkWidth(Fonts, type, mark.Text);

        Assert.Equal(drawn, x1 - x0, 9);
    }

    /// <summary>
    /// The em the whole family is set at is LILYPOND's paper text-font-size, read from the
    /// home that carries LilyPond's derivation — not a size of Lily#'s own choosing.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS REPLACES A RATCHET THE PORT RETIRED, and the swap is written down rather than
    /// done quietly (HANDOFF §7.6 ⒟ — a removal names the observer that permits it). Session
    /// 203 asserted the reservation was never NARROWER than a 2.4 Bold box, because the two
    /// strays it had just removed both reserved less than the ink, always toward a collision.
    /// Session 204 measured LilyPond: the answer is 2.2 ITALIC, which is narrower than 2.4
    /// Bold on every string in the family — so that floor now forbids the correct box. What
    /// replaces it is the claim the floor stood in for. The sizes themselves are observed by
    /// the four ledger points <c>mark.jump.width.*</c> / <c>mark.pedal.width.*</c>, against
    /// real LilyPond rather than against a previous Lily#, and two of them are exact.
    /// </remarks>
    [Fact]
    public void PlainTextMarks_AreSetAtLilyPondsOwnTextFontSize()
        => Assert.Equal(EngravingDefaults.TextScriptFontSize,
            MusicMarkEngraver.PlainTextFontSize, 9);

    /// <summary>
    /// The sustain pedal's words are upright bold and the rest of the family is plain
    /// italic — the distinction the one home exists to state once.
    /// </summary>
    /// <remarks>
    /// ⚠️ The slanted arm was <c>BoldItalic</c> until session 204 and is LilyPond's plain
    /// <c>italic</c> now (scm/define-grobs.scm:1898-1926 JumpScript declares font-shape
    /// italic and no font-series). The SUSTAIN arm did not move and is not a port: LilyPond
    /// sets "Ped." in Emmentaler's pedal glyphs, not in a text face at all
    /// (lily/sustain-pedal.cc:47-76), so there is no weight of LilyPond's to agree with.
    /// </remarks>
    [Fact]
    public void TextStyleOf_SlantsEverythingButTheSustainWords()
    {
        Assert.Equal(FontStyle.Bold, MusicMarkEngraver.TextStyleOf(MusicMarkType.SustainOn));
        Assert.Equal(FontStyle.Bold, MusicMarkEngraver.TextStyleOf(MusicMarkType.SustainOff));
        Assert.Equal(FontStyle.Italic, MusicMarkEngraver.TextStyleOf(MusicMarkType.SostenutoOn));
        Assert.Equal(FontStyle.Italic, MusicMarkEngraver.TextStyleOf(MusicMarkType.UnaCordaOn));
        Assert.Equal(FontStyle.Italic, MusicMarkEngraver.TextStyleOf(MusicMarkType.Fine));
    }

    // ---- the drawn page carries the same size and style the home names ----

    private static string Svg(string source)
        => SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    private static (double Size, FontStyle Style) DrawnTextAttributes(string svg, string content)
    {
        var m = Regex.Match(svg, $"<text[^>]*>{Regex.Escape(content)}</text>");
        Assert.True(m.Success, $"no <text> element drew \"{content}\"");
        string el = m.Value;
        double size = double.Parse(Regex.Match(el, @"font-size=""([\d.]+)""").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var style = FontStyle.Regular;
        if (el.Contains(@"font-weight=""bold""")) style |= FontStyle.Bold;
        if (el.Contains(@"font-style=""italic""")) style |= FontStyle.Italic;
        return (size, style);
    }

    /// <summary>
    /// A book whose one staff prints the pedal WORDS: the default style is a bracket, so
    /// without `pedal text` nothing in the family draws a string at all.
    /// </summary>
    // ⚠️ The part is `pno`, not `p`: `p` is the piano dynamic and a reserved word.
    private const string PedalTextBook =
        "part pno { clef treble pedal text }\n" +
        "section A { pno { %BODY% } }\n" +
        "form main { A }\n" +
        "score main \"pedal\" { staff pno }\n";

    /// <summary>
    /// The SUSTAIN pedal's word is drawn as music glyphs, not as text — the one place the
    /// family leaves the text path, and the shape LilyPond has always had.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/sustain-pedal.cc:47-76 Sustain_pedal::print pastes pedal.Ped and
    /// pedal.. edge to edge with zero padding, priced by their LILC boxes. This asserts the
    /// page carries that run and no <c>&lt;text&gt;</c> element saying "Ped." at all — the
    /// falsifier for a half-port that reserved glyphs and still drew a string. The WIDTH is
    /// pinned against real LilyPond by ledger point <c>mark.pedal.width.sustain</c>
    /// (3.472000000, exact); this pins the mechanism the ledger point reads.
    /// </remarks>
    [Fact]
    public void SustainPedalWord_IsDrawnAsMusicGlyphs()
    {
        string svg = Svg(PedalTextBook.Replace("%BODY%", "c4@sustainOn c4@sustainOff |"));

        Assert.DoesNotContain(">Ped.<", svg);
        // The renderer writes the private-use code point LITERALLY into the element body,
        // so the run is read as characters rather than as numeric entities.
        var glyphs = Regex.Matches(svg, @"<text class=""music""[^>]*>(.)</text>")
            .Select(m => m.Groups[1].Value[0])
            .Where(c => c is EmmentalerGlyphs.PedalPed or EmmentalerGlyphs.PedalDot
                          or EmmentalerGlyphs.PedalStar)
            .ToList();
        Assert.Equal(
            new[] { EmmentalerGlyphs.PedalPed, EmmentalerGlyphs.PedalDot,
                    EmmentalerGlyphs.PedalStar },
            glyphs);
    }

    /// <summary>
    /// A pedal CHANGE puts the release beside the engage on one line, and the gap it
    /// clears is measured from the word that actually follows — not from a spelled "Ped.".
    /// </summary>
    /// <remarks>
    /// ⚠️ THE OBSERVER FOR AN ARM NO BOOK REACHES. A 100-staff-space poison in the
    /// pedal-change nudge moved 0 of 1119 books on disk (2026-08-18): the corpus has no
    /// pedal change in a `pedal text` or `pedal mixed` style at all, so the sweep says
    /// nothing about it and a fixture would say nothing either (HANDOFF §5.3). This book
    /// reaches it on purpose, in the family that tells the two spellings apart: SOSTENUTO,
    /// whose engage word is "Sost. Ped." and whose release therefore has a different width
    /// to clear. The site spelled "Ped." whatever the family until 2026-08-18, and after
    /// the sustain word became a glyph run that stopped being merely the wrong string — it
    /// was the wrong MECHANISM, a text word cleared by a music-font width.
    /// <para>
    /// The arithmetic asserted is the site's own claim restated from the DRAWN page: two
    /// centred words, so the release's centre sits half of each plus the gap to the left of
    /// the engage's. The engage's half comes from the string the page actually carries.
    /// </para>
    /// </remarks>
    [Fact]
    public void PedalChange_ClearsTheWordThatFollowsIt()
    {
        string svg = Svg(PedalTextBook.Replace(
            "%BODY%", "c4@sostenutoOn c4@sostenutoOff@sostenutoOn c4@sostenutoOff |"));

        double CentreOf(string content) => double.Parse(
            Regex.Match(svg, $@"<text x=""([\d.]+)""[^>]*>{Regex.Escape(content)}</text>")
                .Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        // The engage word is drawn twice (the change re-engages); the change is the SECOND,
        // and the release beside it is the star that shares its X neighbourhood.
        var engages = Regex.Matches(svg, @"<text x=""([\d.]+)""[^>]*>Sost\. Ped\.</text>")
            .Select(m => double.Parse(m.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(v => v).ToList();
        Assert.Equal(2, engages.Count);
        double engageCentre = engages[1];
        double starCentre = CentreOf("*");

        double engageHalf = MusicMarkEngraver.PlainMarkWidth(
            Fonts, MusicMarkType.SostenutoOn, "Sost. Ped.") / 2;
        double starHalf = MusicMarkEngraver.PlainMarkWidth(
            Fonts, MusicMarkType.SostenutoOff, "*") / 2;
        const double gap = 0.4;

        // ⚠️ TWO decimals, not nine: these coordinates are read from the SVG, and
        // SvgGenerator writes every one with F2 (SvgGenerator.cs:229). The two candidate
        // answers here are 3.2 staff spaces apart, so the quantisation has no bearing on
        // what is being told apart — and saying so is cheaper than routing this one book
        // through the LpFidelity recorder, which exists for residuals that live below 0.01.
        Assert.Equal(engageCentre - engageHalf - starHalf - gap, starCentre, 2);
        // The falsifier, spelled out: the sustain word is a MUSIC-GLYPH run and is much
        // narrower than this one, so a nudge that still measured "Ped." would land the star
        // that much to the right — inside the engage word's ink.
        Assert.NotEqual(
            engageCentre
                - MusicMarkEngraver.PlainMarkWidth(Fonts, MusicMarkType.SustainOn, "Ped.") / 2
                - starHalf - gap,
            starCentre, 2);
    }

    // ⚠️ The navigation marks are BARE tokens, not '@' post-events (the '@' form is LYS1022);
    // the pedal words are '@' post-events on a staff that asks for `pedal text`. Two
    // spellings and two gates, one family downstream.
    // ⚠️ The SUSTAIN pedal is not in this theory: since 2026-08-18 its word is a glyph run,
    // and SustainPedalWord_IsDrawnAsMusicGlyphs above is what asserts it.
    [Theory]
    [InlineData("fine g1 |", "Fine", MusicMarkType.Fine)]
    [InlineData("ds al coda g1 |", "D.S. al Coda", MusicMarkType.DalSegnoAlCoda)]
    [InlineData("dc al fine g1 |", "D.C. al Fine", MusicMarkType.DaCapoAlFine)]
    [InlineData("PEDAL:c4@sostenutoOn c4@sostenutoOff |", "Sost. Ped.", MusicMarkType.SostenutoOn)]
    [InlineData("PEDAL:c4@unaCorda c4@treCorde |", "una corda", MusicMarkType.UnaCordaOn)]
    public void DrawnMark_CarriesTheOneHomesSizeAndStyle(
        string source, string content, MusicMarkType type)
    {
        if (source.StartsWith("PEDAL:"))
            source = PedalTextBook.Replace("%BODY%", source["PEDAL:".Length..]);
        var (size, style) = DrawnTextAttributes(Svg(source), content);

        Assert.Equal(MusicMarkEngraver.PlainTextFontSize, size, 2);
        Assert.Equal(MusicMarkEngraver.TextStyleOf(type), style);
    }

    /// <summary>
    /// The To-Coda prefix centres the pair on the width it is about to draw. It measured
    /// upright bold against a bold-italic draw until 2026-08-18, which put the group
    /// 0.068286614 staff spaces left of its anchor.
    /// </summary>
    [Fact]
    public void ToCodaPrefix_IsCentredOnTheStyleItDraws()
    {
        string svg = Svg("to coda g4 a b c' |");
        var (size, style) = DrawnTextAttributes(svg, "To ");
        Assert.Equal(MusicMarkEngraver.PlainTextFontSize, size, 2);
        Assert.Equal(MusicMarkEngraver.TextStyleOf(MusicMarkType.ToCoda), style);

        // The coda glyph starts exactly one measured "To " past the text's pen origin —
        // which is only true if the centring measured the face it drew.
        double textX = double.Parse(
            Regex.Match(svg, @"<text x=""([\d.]+)""[^>]*>To </text>").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var glyph = Regex.Match(svg,
            @"<text class=""music"" x=""([\d.]+)""[^>]*>" + EmmentalerGlyphs.MarkCoda + "</text>");
        Assert.True(glyph.Success, "the To-Coda pair drew no coda glyph");
        double glyphX = double.Parse(glyph.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        double prefix = Fonts.Advance("To ", MusicMarkEngraver.PlainTextFontSize,
            TextRole.Navigation, MusicMarkEngraver.TextStyleOf(MusicMarkType.ToCoda));
        // ⚠️ Tolerance 0.02, not a decimal count: the SVG prints coordinates to 2 places, so
        // a difference of two printed numbers carries two roundings. The gap this separates
        // is 0.136573228 — the Bold/BoldItalic advance difference — an order above the noise.
        Assert.InRange(glyphX - textX, prefix - 0.02, prefix + 0.02);
    }

    // ---- dynamics and text scripts: the same claim, their own homes ----

    [Theory]
    [InlineData(true, FontStyle.Italic)]
    [InlineData(false, FontStyle.BoldItalic)]
    public void DynamicLabelStyle_IsOneHome(bool expressive, FontStyle expected)
        => Assert.Equal(expected, DynamicEngraver.LabelStyle(expressive));

    /// <summary>
    /// The reserved half-width is half of the DRAWN advance. Separate from the style test
    /// above because the two fail for different reasons: that one if the home changes, this
    /// one if the reservation stops reading it.
    /// </summary>
    [Theory]
    [InlineData("pp", false)]
    [InlineData("sfz", false)]
    [InlineData("cresc.", true)]
    [InlineData("dim.", true)]
    public void DynamicLabelHalfWidth_IsHalfTheDrawnAdvance(string text, bool expressive)
    {
        double drawn = Fonts.Advance(text, 2.0, TextRole.Dynamics,
            DynamicEngraver.LabelStyle(expressive));
        Assert.Equal(drawn / 2.0, DynamicEngraver.LabelHalfWidth(Fonts, text, expressive), 9);
    }

    [Theory]
    [InlineData("c4@f |", "f", false)]
    [InlineData("c4@pp |", "pp", false)]
    [InlineData("c4@text(\"dolce\") |", "dolce", true)]
    public void DrawnDynamic_CarriesTheStyleTheReservationMeasures(
        string source, string content, bool expressive)
    {
        var (_, style) = DrawnTextAttributes(Svg(source), content);
        Assert.Equal(DynamicEngraver.LabelStyle(expressive), style);
    }

    /// <summary>
    /// A structure-level text script (<c>_"…"</c>) is drawn italic at TextScript's own em;
    /// the inter-system skyline box used to reserve it upright bold at 2.0 — a box around a
    /// string nobody draws. (The note-level <c>@text("…")</c> is a different grob: it rides
    /// the dynamics pipeline, which is why it is asserted above and not here.)
    /// </summary>
    [Fact]
    public void DrawnTextScript_IsItalicAtTextScriptFontSize()
    {
        string book =
            "part rh { clef treble }\n" +
            "section A { rh { g4 a b c' | } }\n" +
            "form main { A _\"poco a poco\" }\n" +
            "score main \"textscript\" { staff rh }\n";
        var (size, style) = DrawnTextAttributes(Svg(book), "poco a poco");
        Assert.Equal(EngravingDefaults.TextScriptFontSize, size, 2);
        Assert.Equal(FontStyle.Italic, style);
    }
}
