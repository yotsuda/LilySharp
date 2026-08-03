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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

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

    /// <summary>
    /// <c>@arpeggio</c> is the stacked chord's wavy line, and the twin has to carry it or the
    /// two engines are not looking at the same music.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE FAILURE THIS GUARDS IS A FALSE AGREEMENT, not a crash. Until 2026-08-03 the
    /// exporter warned "articulation @arpeggio not mapped, dropped" and wrote a bare
    /// <c>&lt;c e g&gt;1</c>, so anyone comparing an arpeggio book against LilyPond was
    /// comparing a chord WITH a wavy line to a chord WITHOUT one — and the arpeggio adds ink
    /// to the LEFT of the column, so the disagreement lands in exactly the x readings such a
    /// comparison would be taken for.
    /// <para>
    /// MEASURED, not read off the source: the emitted twin was rendered by LilyPond 2.26.0 and
    /// diffed against the same twin with <c>\arpeggio</c> deleted. The difference is three
    /// <c>scripts.arpeggio</c> glyphs stacked a staff space apart at one x, and nothing else.
    /// </para>
    /// <para>
    /// ⚠️ NOT a script: <c>\arpeggio</c> is an event on the chord, so it must not pick up the
    /// <c>-</c> / <c>^</c> / <c>_</c> direction prefix every mapped articulation gets.
    /// </para>
    /// </remarks>
    [Fact]
    public void Arpeggio_BecomesTheChordsArpeggioEvent()
    {
        var ly = Export(Score("<c, e, g,>1@arpeggio"));
        Assert.Contains("\\arpeggio", ly);
        // The event hangs on the chord itself, with no script direction in front of it.
        Assert.DoesNotContain("-\\arpeggio", ly);
        Assert.DoesNotContain("^\\arpeggio", ly);
        Assert.DoesNotContain("_\\arpeggio", ly);
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

    /// <summary>
    /// The two engines' tab DEFAULTS are opposite ends of the same switch, so a bare
    /// <c>\new TabStaff</c> is the twin of <c>tab part AS NUMBERS</c>, never of a plain
    /// <c>tab part</c>.
    /// </summary>
    /// <remarks>
    /// LilyPond's TabStaff omits Stem, Beam, Flag, Dots, Rest and TupletBracket unless
    /// <c>\tabFullNotation</c> asks for them; Lily#'s plain <c>tab part</c> draws all of it.
    /// Measured against real LilyPond: the twin of <c>test/tab-beam-script</c> held TWO Beam
    /// grobs (both on the 5-line notation staff) against the page's four, and with
    /// <c>\tabFullNotation</c> it holds four — the extra two on a 4-line staff of space 1.5.
    /// Every tab book was therefore uncomparable on beams, which had been recorded as a
    /// missing tab FRAME in the sweep rather than as a twin in the wrong mode.
    /// </remarks>
    [Fact]
    public void TabTwin_AsksForFullNotation_UnlessTheScoreSaidAsNumbers()
    {
        var full = Export(Score("c,8 d, e, f,", render: "staff bassline\n  tab bassline"));
        Assert.Contains("\\tabFullNotation", full);

        var numbers = Export(Score("c,8 d, e, f,", render: "staff bassline\n  tab bassline as numbers"));
        Assert.DoesNotContain("\\tabFullNotation", numbers);
        Assert.Contains("\\new TabStaff", numbers);
    }

    // ---- the `instrument` preset (HANDOFF gate ⑹) ---------------------------
    //
    // An instrument preset is a BUNDLE — clef, relative anchor, tab tuning and sounding
    // transposition — and LilyPond has no spelling for the bundle, only for its parts. The
    // exporter used not to read it at all, so ten fixtures declaring `instrument bass` and
    // no `clef` exported a TREBLE twin against a BASS page: valid LilyPond playing other
    // music. These points say the twin now spells what the page resolved.

    /// <summary>A part whose clef comes only from its <c>instrument</c> writes that clef,
    /// and anchors its relative pitches where the preset says.</summary>
    /// <remarks>
    /// ⚠️ Two presets in ONE test on purpose: a bundle read wrong tends to be read wrong
    /// CONSTANTLY (the old behaviour was treble/c' for everything), and only a case that
    /// must answer two different things can tell the difference.
    /// </remarks>
    [Fact]
    public void InstrumentPreset_WritesItsClef_AndAnchorsItsOctave()
    {
        var ly = Export("""
            part bs { instrument bass section S { c d e } }
            part fl { instrument flute section T { c d e } }
            form main { ~S ~T }
            score main { staff bs staff fl }
            """);
        // bass = bass clef, octave 3; flute = treble clef, octave 5 (NOT the treble
        // default of 4 — the preset's own octave, which is why the bundle is read whole).
        Assert.Contains("bs = \\relative c {", ly);
        Assert.Contains("\\new Staff { \\clef bass \\bs }", ly);
        Assert.Contains("fl = \\relative c'' {", ly);
        Assert.Contains("\\new Staff { \\clef treble \\fl }", ly);
    }

    /// <summary>A hyphenated preset (<c>electric-bass</c>) is read whole.</summary>
    /// <remarks>
    /// It is word+minus+word in the green tree, so reading only the property's FIRST value
    /// token yields "electric", which no preset matches and which therefore falls silently
    /// through to treble — the same failure the collector had to fix in its own reader.
    /// </remarks>
    [Fact]
    public void InstrumentPreset_HyphenatedNameIsReadWhole()
    {
        var ly = Export("""
            part bs { instrument electric-bass section S { c d e } }
            form main { ~S }
            score main { staff bs }
            """);
        Assert.Contains("\\new Staff { \\clef bass \\bs }", ly);
        Assert.Contains("bs = \\relative c {", ly);
    }

    /// <summary>An explicit <c>clef</c> beats the preset's clef — and the preset's OCTAVE
    /// still wins, which is what the layout does.</summary>
    /// <remarks>
    /// MeasureCollector.GetPartDefaults resolves the clef first and then fills the octave in
    /// (<c>resolvedOctave ??= defaultOctave</c>), so <c>clef treble instrument bass</c> is a
    /// treble staff anchored at octave 3. Odd, but mirrored rather than approximated: an
    /// anchor an octave off is a twin that plays other pitches.
    /// </remarks>
    [Fact]
    public void ExplicitClef_BeatsThePreset_ButThePresetsOctaveStands()
    {
        var ly = Export("""
            part bs {
              clef treble
              instrument bass
              section S { c d e }
            }
            form main { ~S }
            score main { staff bs }
            """);
        Assert.Contains("\\new Staff { \\clef treble \\bs }", ly);
        Assert.Contains("bs = \\relative c {", ly);
    }

    /// <summary>
    /// A tab part with neither <c>tuning</c> nor <c>instrument</c> is a GUITAR, the way the
    /// page reads it — the exporter used to fall back to a four-string bass.
    /// </summary>
    /// <remarks>
    /// The two defaults were opposite ends of the same switch (RenderSpecParser: explicit →
    /// property → preset → guitar; this exporter: property → bass), so
    /// <c>test/tab-part-key</c> drew six tab lines on the page against four in the twin. Once
    /// the twin also started writing the tab's transposition, the same wrong default moved
    /// its PITCHES too (bass tunings carry −12), so the twin fretted other notes as well.
    /// </remarks>
    [Fact]
    public void TabTwin_DefaultsToGuitar_NotBass()
    {
        var ly = Export("""
            part gt { section S { c'4 d' e' f' } }
            form main { ~S }
            score main { staff gt  tab gt }
            """);
        Assert.Contains("stringTunings = #guitar-tuning", ly);
        Assert.DoesNotContain("bass-four-string-tuning", ly);
        Assert.DoesNotContain("\\transpose", ly);
    }

    /// <summary>An explicit <c>transposition</c> property is what the tab frets against.</summary>
    /// <remarks>
    /// RenderSpecParser.ResolvePartTransposition: the property beats the preset's default,
    /// which beats the tuning's. A guitar tuning carries none of its own, so the property is
    /// the whole shift here and the twin either writes it or frets an octave off.
    /// </remarks>
    [Fact]
    public void TabTwin_ExplicitTranspositionProperty_IsWritten()
    {
        string Source(string extra) => $$"""
            part gt {
              clef bass
              tuning guitar
            {{extra}}
              section S { c4 d e f }
            }
            form main { ~S }
            score main { staff gt  tab gt }
            """;
        Assert.DoesNotContain("\\transpose", Export(Source("")));
        Assert.Contains("\\transpose c c, ", Export(Source("  transposition 8vb")));
    }

    /// <summary>
    /// The twin's tab spells exactly what the PAGE resolved — same tuning, same written→
    /// sounding shift, same clef — for every way a part can say (or not say) it.
    /// </summary>
    /// <remarks>
    /// The two resolutions are separate code (RenderSpecParser for the page,
    /// LilyPondExporter for the twin) reading one table, and the corpus only notices they
    /// have drifted when a book happens to be comparable. This is the invariant that
    /// notices instead: it fails the moment either side changes alone.
    /// ⚠️ The cases must DISAGREE with each other — a set that all resolve to the same
    /// tuning would pass against a constant.
    /// </remarks>
    [Theory]
    [InlineData("")]                                   // nothing said → guitar, no shift
    [InlineData("  tuning bass")]                      // tuning alone carries −12
    [InlineData("  instrument bass")]                  // preset: bass clef + tuning + −12
    [InlineData("  instrument electric-bass")]
    [InlineData("  instrument guitar")]                // treble_8: the octave rides the clef
    [InlineData("  instrument ukulele")]
    [InlineData("  clef bass\n  tuning bass")]
    [InlineData("  clef treble\n  instrument bass")]   // explicit clef, preset tuning
    [InlineData("  tuning guitar\n  transposition 8vb")]
    public void TabTwin_SpellsWhatThePageResolved(string properties)
    {
        string source = $$"""
            part pt {
            {{properties}}
              section S { c4 d e f }
            }
            form main { ~S }
            score main { staff pt  tab pt }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var tab = Assert.IsType<TabStaffSpec>(spec.Items.First(i => i is TabStaffSpec));
        string ly = new LilyPondExporter().Export(tree);

        // ⑴ the tuning, by LilyPond's name for it
        string expectedTuning = tab.Tuning switch
        {
            TuningType.Bass => "bass-four-string-tuning",
            TuningType.Bass5 => "bass-five-string-tuning",
            TuningType.Bass6 => "bass-six-string-tuning",
            TuningType.Ukulele => "ukulele-tuning",
            _ => "guitar-tuning",
        };
        Assert.Contains($"stringTunings = #{expectedTuning}", ly);

        // ⑵ the written→sounding shift the frets are taken at, as \transpose marks
        int shift = Tunings.SoundingShift(tab.Staff.Clef, tab.Transposition);
        Assert.Equal(0, shift % 12);
        string expectedTranspose = shift == 0
            ? null!
            : "\\transpose c c" + new string(shift < 0 ? ',' : '\'', System.Math.Abs(shift) / 12);
        if (shift == 0)
            Assert.DoesNotContain("\\transpose", ly);
        else
            Assert.Contains(expectedTranspose + " ", ly);

        // ⑶ the clef the notation staff reads in (nothing written when the part named none
        //    and no preset implies one — LilyPond's own default is treble, and so is Lily#'s)
        bool declaresClef = properties.Contains("clef") || properties.Contains("instrument");
        if (declaresClef)
            Assert.Contains($"\\new Staff {{ \\clef {InstrumentDefaults.ClefWord(tab.Staff.Clef)} ", ly);
        else
            Assert.DoesNotContain("\\clef", ly);
    }

    /// <summary>
    /// A chord's members are re-octaved for the twin: Lily# STACKS them on the root,
    /// LilyPond CHAINS them member to member, so the source's marks are not the twin's.
    /// </summary>
    /// <remarks>
    /// Lily# places each member in the root's octave, bumped when its letter is below the
    /// root's (MeasureCollector.ItemFactory CreateChordItem, which calls the rule a
    /// deliberate divergence); LilyPond octaves each member against the one before it and
    /// takes the nearest (lily/music-sequence.cc:142-160). Written verbatim, <c>&lt;a c
    /// g&gt;</c> is A3 C4 G4 on the page and A3 C4 G3 in the twin — a different chord.
    /// Measured on <c>test/tab-beam-slope</c>, whose notation beam was the only one still
    /// differing after the <c>instrument</c> gate closed, and confirmed against the page
    /// as a PNG.
    /// <para>
    /// ⚠️ The ASCENDING chord is the control: LilyPond's nearest and Lily#'s stack agree
    /// there, so it must come through with no marks at all. Without it this point would
    /// pass against an exporter that marked every member.
    /// </para>
    /// </remarks>
    [Fact]
    public void ChordMembers_AreReOctavedForLilyPondsChain()
    {
        var ly = Export("""
            part gt {
              section S { <c e g>2 <a c g>2 | <c a f>2 <c e g>2 | }
            }
            form main { ~S }
            score main { staff gt }
            """);
        Assert.Contains("<c e g>2 <a c g'>2", ly);   // g is BELOW a, so it stacks an octave up
        Assert.Contains("<c a' f>2 <c e g>2", ly);   // a and f both sit above the root c
    }

    /// <summary>Re-octaving the members does not move what comes AFTER the chord.</summary>
    /// <remarks>
    /// LilyPond leaves an EventChord standing on its FIRST member (music-sequence.cc
    /// :213-219 <c>ret_first</c>) and Lily# on the chord's anchor — the root's bare letter —
    /// so the two agree for an ordinary chord however the other members are spelled. The
    /// following note therefore keeps the source's own marks.
    /// </remarks>
    [Fact]
    public void ChordMembers_ReOctaving_LeavesTheFollowingNoteAlone()
    {
        var ly = Export("""
            part gt {
              section S { <a c g>4 a4 e4 f4 | }
            }
            form main { ~S }
            score main { staff gt }
            """);
        Assert.Contains("<a c g'>4 a4 e4 f4", ly);
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

    // ---- Scale-degree chords ------------------------------------------------
    //
    // LilyPond has no spelling for a degree at all, so a degree member cannot be copied
    // through the way every other pitch token is: it has to be RESOLVED, against the chord's
    // root (or the key's tonic when the root is omitted) and against the running key. Until
    // 2026-08-01 they were dropped instead, which spelt `<1 3 5>` as `<>` — a zero-length
    // event, so test/chord-octave-marks failed its bar check at 1/4 and the sweep read it as
    // a book with no beams. The expected strings below are the pitches Lily# SOUNDS
    // (MeasureCollector.ItemFactory / MidiExporter agree on them note for note).

    private static string DegreeScore(string music, string key = "key c major") => $$"""
        {{key}}
        part m { clef treble }
        section S { m { {{music}} } }
        form main { S }
        score main { staff m }
        """;

    [Fact]
    public void ARootlessDegreeChord_IsSpelledOut_NotAnEmptyChord()
    {
        // <1 3 5> in C major is the tonic triad, and degree 1 IS the tonic.
        var ly = Export(DegreeScore("<1 3 5>4"));
        Assert.Contains("<c e g>4", ly);
        Assert.DoesNotContain("<>", ly);
    }

    [Fact]
    public void ADegreeTakesItsAccidentalFromTheRunningKey()
    {
        // The key gives the letter its alteration (ChordDegrees.Resolve → KeySpelling), and
        // LilyPond note names are absolute: a bare `f` under \key g \major is F NATURAL, so
        // the leading note has to be written out as fis or the twin is a different chord.
        var ly = Export(DegreeScore("<1 3 5 7>4", key: "key g major"));
        Assert.Contains("fis", ly);
    }

    [Fact]
    public void AnOmittedRootAnchorsOnTheKeysTonic_NotOnC()
    {
        // F major, degrees 2 4 6 → the ii chord g-bes-d. The `bes` is the key's, the `g'` is
        // the frame's: Lily# anchors the (unsounded) tonic f above the c the part opens on,
        // and LilyPond, reading a bare g, would put it BELOW that c.
        var ly = Export(DegreeScore("<2 4 6>4", key: "key f major"));
        Assert.Contains("<g' bes d>4", ly);
    }

    [Fact]
    public void AWrittenRootIsTheAnchor_AndTheDegreesStackOnIt()
    {
        // <d 3 5 7,> — a seventh chord on d with its seventh dropped an octave. The `,` is
        // the degree's own mark, and what it takes to spell that in LilyPond's member-to-
        // member chain is not the same mark it had in the source.
        var ly = Export(DegreeScore("<d 3 5 7,>4"));
        Assert.Contains("<d f a c,>4", ly);
    }

    [Fact]
    public void WholeChordMarks_MoveADegreeChordTogether()
    {
        var ly = Export(DegreeScore("<1 3 5>4 <1 3 5>'4 <1 3 5>,4"));
        Assert.Contains("<c e g>4 <c' e g>4 <c, e g>4", ly);
    }

    /// <summary>
    /// A degree chord can leave the two engines' octave frames apart, and the next note's
    /// marks absorb the difference rather than reporting it.
    /// </summary>
    /// <remarks>
    /// LilyPond octaves the note after a chord against the chord's FIRST MEMBER
    /// (lily/music-sequence.cc:213-219, ret_first); Lily# octaves it against the chord's
    /// ANCHOR, which for <c>&lt;1' 3 5&gt;</c> is the tonic an octave BELOW the C5 that had to
    /// be written first. Copying the source's bare <c>c</c> through would put the twin's next
    /// note an octave high — silently, which is the failure mode this exporter exists to
    /// avoid.
    /// </remarks>
    [Fact]
    public void AFrameADegreeChordMoved_IsCarried_ByTheNextNotesMarks()
    {
        var ly = Export(DegreeScore("<1' 3 5>2 c2"));
        Assert.Contains("<c' e, g>2 c,2", ly);
    }

    [Fact]
    public void InAbsoluteMode_DegreesAreWrittenAgainstTheFixedAnchor()
    {
        // No frame to chase: \fixed c' means a bare letter is the octave of middle C, so the
        // whole-chord ' is one mark on every member.
        var ly = Export("""
            octave absolute
            key c major
            part m { clef treble }
            section S { m { <1 3 5>4 <1 3 5>'4 } }
            form main { S }
            score main { staff m }
            """);
        Assert.Contains("<c e g>4 <c' e' g'>4", ly);
    }

    [Fact]
    public void ADegreeChordAfterAPhraseReference_IsReported_BecauseTheFrameIsNoLongerTracked()
    {
        // A phrase reference's nested \relative hands the enclosing frame back unchanged in
        // LilyPond and the phrase's anchor in Lily#, so past that point the anchor a degree
        // would stack on is a guess, and the guess is reported. (A voice span used to be in
        // this list; it is now compensated exactly — see EmitParallel.)
        var exporter = new LilyPondExporter();
        exporter.Export(SyntaxTree.Parse("""
            key c major
            part m { clef treble }
            phrase P { c4 d }
            section S { m { P <1 3 5>4 } }
            form main { S }
            score main { staff m }
            """));
        Assert.Contains(exporter.Warnings, w => w.Contains("degree chord follows a phrase reference"));

        // …and the ordinary book says nothing.
        var quiet = new LilyPondExporter();
        quiet.Export(SyntaxTree.Parse(DegreeScore("<1 3 5>4")));
        Assert.DoesNotContain(quiet.Warnings, w => w.Contains("degree chord"));
    }

    /// <summary>
    /// After a DOTTED duration the next event writes its value out, because the two engines
    /// carry a dot differently.
    /// </summary>
    /// <remarks>
    /// Lily# carries the note VALUE and drops the dots (MeasureCollector.ItemFactory
    /// <c>_defaultDuration = Fraction.FromNoteValue(noteValue)</c>); LilyPond carries the whole
    /// duration (lily/parser.yy default_duration_). So <c>c4. d</c> is 5/8 on the page and 6/8
    /// in the twin — and in 6/8 that twin's bar is complete, so LilyPond does not complain
    /// either. Measured: <c>c'4. d'</c> draws the same six glyphs as <c>c'4. d'4</c> and raises
    /// the same LYS2006, while <c>c'4. d'4.</c> draws seven.
    /// </remarks>
    [Fact]
    public void AnEventAfterADottedOne_WritesItsValue_BecauseLilyPondCarriesTheDot()
    {
        var ly = Export("""
            octave absolute
            time 6/8
            part m { clef treble }
            section S { m { c'4. d' | } }
            form main { S }
            score main { staff m }
            """);
        Assert.Contains("c'4. d'4 |", ly);

        // An undotted duration still carries silently — the source is copied, not re-spelled.
        var plain = Export("""
            octave absolute
            time 4/4
            part m { clef treble }
            section S { m { c'8 d' e' f' | } }
            form main { S }
            score main { staff m }
            """);
        Assert.Contains("c'8 d' e' f' |", plain);
    }

    // ---- Drum kit ------------------------------------------------------------
    //
    // A drum note is a NAME, not a pitch, and LilyPond reads those names only inside
    // \drummode. All 24 in the corpus were dropped with a warning until 2026-08-01, which
    // left test/drum-groove's twin a bar-check failure — the last hole that lost music.

    private static string DrumScore(string music) => $$"""
        part kit { clef percussion }
        section S { kit { {{music}} } }
        form main { S }
        score main { staff kit }
        """;

    [Fact]
    public void ADrumPart_IsWrittenInDrummode_OnADrumStaff()
    {
        var ly = Export(DrumScore("hh8 hh bd4 sn4"));
        // The names go through verbatim: Lily#'s vocabulary IS LilyPond's (DrumNameRegistry
        // cites ly/drumpitch-init.ly), so nothing has to be translated — only the mode and
        // the context, which is what LilyPond needs to read them at all.
        Assert.Contains("kit = \\drummode {", ly);
        Assert.Contains("hh8 hh bd4 sn4", ly);
        Assert.Contains("\\new DrumStaff { \\kit }", ly);
        Assert.DoesNotContain("\\relative", ly);
        // No second clef: DrumStaff's own is the percussion clef.
        Assert.DoesNotContain("\\clef", ly);
    }

    [Fact]
    public void ADrumChord_KeepsItsMembers()
    {
        var ly = Export(DrumScore("<bd hh>4 <sn hh>4"));
        Assert.Contains("<bd hh>4 <sn hh>4", ly);
        Assert.DoesNotContain("<>", ly);
    }

    [Fact]
    public void APartThatMixesDrumsAndPitches_IsReported_BecauseDrummodeCannotHoldBoth()
    {
        // Inside \drummode a `c` is not a pitch and outside it `hh` is not a drum, so the
        // stream cannot be spelled at all. A .ly LilyPond refuses to read would be worse
        // than the drums going missing with a name on the loss.
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(SyntaxTree.Parse(DrumScore("hh8 hh c4 d4")));
        Assert.Contains(exporter.Warnings, w => w.Contains("drum names and pitches in one stream"));
        Assert.DoesNotContain("\\drummode", ly);
        Assert.Contains("c4", ly);
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
