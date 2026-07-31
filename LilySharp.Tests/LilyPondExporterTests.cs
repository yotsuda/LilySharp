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

using Xunit;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class LilyPondExporterTests
{
    private static string Export(string lys) =>
        new LilyPondExporter().Export(SyntaxTree.Parse(lys));

    // A part-major score with one bass section. ⚠️ NOT the shape the corpus uses (that is
    // section-major, with phrase references) — this helper's monopoly on the suite is what
    // hid two whole export gaps; see the phrase-reference tests at the bottom.
    private static string Score(string music, string headers = "octave absolute",
        string render = "staff bassline") => $$"""
        {{headers}}
        part bassline {
          clef bass
          tuning bass
          section S { {{music}} }
        }
        form main { ~S }
        score main { {{render}} }
        """;

    [Fact]
    public void AbsoluteOctave_WrapsInFixed_AndCopiesMarksVerbatim()
    {
        var ly = Export(Score("a,4 e,8 gis,8"));
        Assert.Contains("\\fixed c' {", ly);
        // The written octave marks survive untouched.
        Assert.Contains("a,4", ly);
        Assert.Contains("e,8", ly);
        Assert.Contains("gis,8", ly);
        Assert.DoesNotContain("\\relative", ly);
    }

    [Fact]
    public void RelativeIsTheDefault_WrapsInRelative()
    {
        // No `octave absolute` directive -> Lily#'s default relative mode.
        // The helper's part is `clef bass`, so the anchor is octave 3 = LilyPond's bare `c`.
        var ly = Export(Score("c d e", headers: ""));
        Assert.Contains("\\relative c {", ly);
        Assert.DoesNotContain("\\fixed", ly);
    }

    /// <summary>
    /// A relative part is anchored at ITS OWN default octave, which follows its clef.
    /// </summary>
    /// <remarks>
    /// Lily#'s relative anchor is the part's default octave (MeasureCollector →
    /// InstrumentDefaults.GetDefaultOctave), so a bass, alto or tenor part starts at
    /// octave 3 and a treble part at 4. The exporter wrote <c>\relative c'</c> for every
    /// part until 2026-08-01, which put every non-treble part's twin AN OCTAVE HIGH — a
    /// quarter of the fixture corpus, and silently, because the twin is perfectly valid
    /// LilyPond and merely plays different music.
    /// <para>
    /// ⚠️ BOTH clefs in ONE test on purpose: an anchor that is constant is wrong whichever
    /// constant it is, and only a case that must answer two different things can tell.
    /// </para>
    /// </remarks>
    [Fact]
    public void RelativeAnchor_FollowsThePartsClef()
    {
        var ly = Export("""
            part low { clef bass section S { c d e } }
            part high { clef treble section T { c d e } }
            form main { ~S ~T }
            score main { staff low staff high }
            """);
        Assert.Contains("low = \\relative c {", ly);
        Assert.Contains("high = \\relative c' {", ly);
    }

    /// <summary>An explicit <c>octave N</c> part property beats the clef's default.</summary>
    /// <remarks>
    /// The same precedence the layout applies (MeasureCollector.GetPartDefaults:
    /// <c>partOctave ?? GetDefaultOctave(clef)</c>). LilyPond writes octave 4 as
    /// <c>c'</c>, so octave 5 is <c>c''</c> and octave 2 is <c>c,</c>.
    /// </remarks>
    [Fact]
    public void RelativeAnchor_ExplicitPartOctaveBeatsTheClef()
    {
        var ly = Export("""
            part v { clef bass octave 5 section S { c d e } }
            form main { ~S }
            score main { staff v }
            """);
        Assert.Contains("v = \\relative c'' {", ly);
    }

    [Fact]
    public void Header_EmitsTitleAndComposer()
    {
        var ly = Export(Score("c4", headers: "octave absolute\ntitle \"Song\"\ncomposer \"Writer\""));
        Assert.Contains("\\header {", ly);
        Assert.Contains("title = \"Song\"", ly);
        Assert.Contains("composer = \"Writer\"", ly);
    }

    [Fact]
    public void KeyTimeTempoClef_MapToBackslashForms()
    {
        var ly = Export(Score("c4",
            headers: "octave absolute\ntempo 120\nkey g major\ntime 3/4"));
        Assert.Contains("\\tempo 4 = 120", ly);
        Assert.Contains("\\key g \\major", ly);
        Assert.Contains("\\time 3/4", ly);
        Assert.Contains("\\clef bass", ly);
    }

    [Fact]
    public void StringNumbers_ArePreserved()
    {
        var ly = Export(Score("a,4\\2 e,8\\3"));
        Assert.Contains("a,4\\2", ly);
        Assert.Contains("e,8\\3", ly);
    }

    [Fact]
    public void InlineRepeat_BecomesRepeatVolta()
    {
        var ly = Export(Score("|: c,4 d,4 :|"));
        Assert.Contains("\\repeat volta 2 {", ly);
        Assert.DoesNotContain("|:", ly);
    }

    [Fact]
    public void InlineRepeatWithEndings_BecomesAlternative()
    {
        var ly = Export(Score("|: c,4 [1. d,4 ] :| [2. e,4 ]"));
        Assert.Contains("\\repeat volta 2 {", ly);
        Assert.Contains("\\alternative {", ly);
    }

    [Fact]
    public void RepeatPercent_PassesThrough()
    {
        var ly = Export(Score("repeat percent 4 { c,4 d,4 }"));
        Assert.Contains("\\repeat percent 4 {", ly);
    }

    [Fact]
    public void Mark_BecomesBoxedRehearsalMark()
    {
        var ly = Export(Score("c,4@mark(\"Intro\") d,4"));
        Assert.Contains("\\mark \\markup { \\box Intro }", ly);
    }

    [Fact]
    public void Tuplet_MapsToBackslashTuplet()
    {
        var ly = Export(Score("tuplet 3/2 { c,8 d,8 e,8 }"));
        Assert.Contains("\\tuplet 3/2 {", ly);
    }

    [Fact]
    public void Score_EmitsStaffAndTabWithBassTuning()
    {
        var ly = Export(Score("c,4", render: "staff bassline\n  tab bassline"));
        Assert.Contains("\\new Staff { \\clef bass", ly);
        Assert.Contains("\\new TabStaff", ly);
        Assert.Contains("stringTunings = #bass-four-string-tuning", ly);
    }

    [Fact]
    public void Ties_AndBreaks_ArePreserved()
    {
        var ly = Export(Score("c,4~ c,4 break d,4"));
        Assert.Contains("~", ly);
        Assert.Contains("\\break", ly);
    }

    [Fact]
    public void EmitsVersionHeader()
    {
        var ly = Export(Score("c,4"));
        Assert.StartsWith("\\version", ly);
    }

    // ---- section-major, the ordinary spelling -------------------------------
    //
    // ⚠️ Every test above uses Score(), which puts the section INSIDE the part
    // (`part m { section S { … } }`). That is the minority spelling. The corpus — all ten
    // showcase fixtures and most of test/ — writes the section at FILE level and names the
    // part with a block inside it, and that form exported an EMPTY part variable: a valid
    // .ly that renders a blank staff, with no error. Nothing here covered it, which is
    // exactly why it survived. These are the points that say the music arrives.

    [Fact]
    public void SectionMajorScore_ExportsTheMusicAndNotAnEmptyPart()
    {
        var ly = Export("""
            octave absolute
            part m { clef treble }
            section Main { m { c'8 d' e' f' } }
            form main { Main }
            score main { staff m }
            """);
        Assert.Contains("c'8", ly);
        Assert.Contains("d'", ly);
        Assert.Contains("f'", ly);
        // The failure this guards is silent: the variable was emitted, just empty.
        Assert.DoesNotContain("\\fixed c' {\n}", ly.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SectionMajorScore_GivesEachPartItsOwnMusic()
    {
        // The block name is what routes the notes; if it were ignored, one part would
        // swallow both streams and the other would come out empty.
        var ly = Export("""
            octave absolute
            part up { clef treble }
            part down { clef bass }
            section Main {
              up { c''4 d'' }
              down { c,4 d, }
            }
            form main { Main }
            score main { staff up
              staff down }
            """);
        int upVar = ly.IndexOf("up = ", System.StringComparison.Ordinal);
        int downVar = ly.IndexOf("down = ", System.StringComparison.Ordinal);
        Assert.True(upVar >= 0 && downVar > upVar, ly);
        string upBody = ly[upVar..downVar];
        string downBody = ly[downVar..];
        Assert.Contains("c''4", upBody);
        Assert.DoesNotContain("c,4", upBody);
        Assert.Contains("c,4", downBody);
        Assert.DoesNotContain("c''4", downBody);
    }

    [Fact]
    public void SectionMajorScore_FollowsTheFormsOrderNotTheFilesOrder()
    {
        // B is declared first and referenced second: the form wins.
        var ly = Export("""
            octave absolute
            part m { clef treble }
            section B { m { g'4 } }
            section A { m { c'4 } }
            form main { A B }
            score main { staff m }
            """);
        int a = ly.IndexOf("c'4", System.StringComparison.Ordinal);
        int b = ly.IndexOf("g'4", System.StringComparison.Ordinal);
        Assert.True(a >= 0 && b >= 0, ly);
        Assert.True(a < b, "the form orders the sections, not the declarations:\n" + ly);
    }

    // ---- Phrase references -------------------------------------------------
    //
    // A section body written the ordinary way is a list of bare phrase REFERENCES, and the
    // exporter used to drop every one of them: `melody { partA partB }` produced
    // `melody = \relative c' { }` — a valid .ly that draws an empty staff, with nothing but
    // a "VariableReference not exported" warning to show for it. 52 of the corpus's 204
    // fixtures declare phrases, so the tool for building LilyPond twins could not build one
    // for any of them. These tests are written in that spelling on purpose: the suite's
    // other 13 all go through the part-major Score() helper, which is exactly how the gap
    // survived (a test file that only ever uses one helper cannot see the other spelling).

    private static string PhraseScore(string phrases, string body,
        string headers = "octave absolute") => $$"""
        {{headers}}
        part m { clef treble }
        {{phrases}}
        section Main { m { {{body}} } }
        form main { ~Main }
        score main { staff m }
        """;

    [Fact]
    public void ABarePhraseReference_ExportsItsNotes_NotAnEmptyStaff()
    {
        var ly = Export(PhraseScore(
            "phrase A { c'4 d' }\nphrase B { e'4 f' }", "A B"));

        Assert.Contains("c'4", ly);
        Assert.Contains("d'", ly);
        Assert.Contains("e'4", ly);
        Assert.Contains("f'", ly);
    }

    [Fact]
    public void EachReferenceGetsItsOwnRelativeBlock_BecauseLilySharpResetsTheFrame()
    {
        // Lily# evaluates every phrase body in the default frame (the collector's
        // RelativeResetMarker), so the second phrase must NOT continue from the first's
        // last note. LilyPond's own spelling of that is a nested \relative, whose
        // reference pitch is absolute.
        var ly = Export(PhraseScore(
            "phrase A { c d }\nphrase B { c d }", "A B", headers: ""));

        int first = ly.IndexOf("\\relative c' {", System.StringComparison.Ordinal);
        int second = ly.IndexOf("\\relative c' {", first + 1, System.StringComparison.Ordinal);
        int third = ly.IndexOf("\\relative c' {", second + 1, System.StringComparison.Ordinal);
        // the part variable's wrapper, then one per reference
        Assert.True(third > second && second > first,
            "each phrase reference needs its own frame:\n" + ly);
    }

    [Fact]
    public void AnAbsoluteOctaveFile_InlinesThePhrase_WithNoFrameToReset()
    {
        var ly = Export(PhraseScore("phrase A { c'4 }", "A"));

        Assert.Contains("c'4", ly);
        // \fixed is the file's own wrapper; the reference adds no second one, because in
        // absolute mode the body's marks already say everything.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(ly, @"\\fixed").Count);
        Assert.DoesNotContain("\\relative", ly);
    }

    [Fact]
    public void ASelfReferencingPhrase_StopsAndSaysSo_InsteadOfRecursingForever()
    {
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(SyntaxTree.Parse(
            PhraseScore("phrase A { c'4 A }", "A")));

        Assert.Contains("c'4", ly);
        Assert.Contains(exporter.Warnings, w => w.Contains("refers to itself"));
    }

    [Fact]
    public void ANoteAfterAReference_IsReported_BecauseTheTwoEnginesAnchorItDifferently()
    {
        // LILYPOND-REF: lily/relative-octave-music.cc:39-45 relative_callback — a nested
        // \relative hands the ENCLOSING frame back unchanged, while Lily# hands off the
        // phrase's anchor. The bodies agree; only a pitch AFTER the reference can differ,
        // and a twin that is silently different music is worse than no twin.
        var exporter = new LilyPondExporter();
        exporter.Export(SyntaxTree.Parse(
            PhraseScore("phrase A { c d }", "A e f", headers: "")));

        Assert.Contains(exporter.Warnings, w => w.Contains("a note follows the phrase reference"));

        // …and a body that is ALL references — how the corpus is written — says nothing.
        var quiet = new LilyPondExporter();
        quiet.Export(SyntaxTree.Parse(
            PhraseScore("phrase A { c d }\nphrase B { e f }", "A B", headers: "")));
        Assert.DoesNotContain(quiet.Warnings, w => w.Contains("a note follows the phrase reference"));
    }
}
