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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The concert-pitch convention (<see cref="ConcertPitch"/>): <c>pitch concert</c> at the
/// top level says the letters are what SOUNDS, so a transposing part is printed the way its
/// player reads it — pitches AND key signature; <c>score … pitch concert</c> says that score
/// prints every part at what it sounds. Four consumers read the one shift — the page, MIDI,
/// MusicXML, the LilyPond twin — and every table here carries a part that does not
/// transpose as its CONTROL, because the defect this design guards against is the shift
/// arriving twice (once as the page's transpose, once as the sounding shift) or reaching a
/// part it does not belong to.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConcertPitchTests
{
    // ⚠️ `octave absolute` on purpose, as InstrumentTranspositionMidiTests: a preset also
    // moves the RELATIVE anchor, and that would mix a second variable into a table about
    // the transposition. Absolute pins the written note at C5 for every row.
    private static string Book(string top, string header, string scoreOpts = "") => $$"""
        time 4/4
        key c major
        octave absolute
        {{top}}
        part x { {{header}} }
        part ctl
        section A { x { c'1 | } ctl { c'1 | } }
        form main { A }
        score main{{scoreOpts}} { staff x staff ctl }
        """;

    private static SyntaxTree Parse(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        return tree;
    }

    /// <summary>The page's answer for the two parts: (x's pitch, x's key sharps, ctl's pitch,
    /// ctl's key sharps), collected exactly as the render path collects.</summary>
    private static (string XPitch, int XSharps, string CtlPitch, int CtlSharps) Page(string source)
    {
        var tree = Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);

        var collector = SemanticValidation.TryCollect(tree, spec);
        Assert.NotNull(collector);
        var pitches = collector!.PitchTrace.OrderBy(e => e.Position).Select(e => e.Pitch).ToList();
        Assert.Equal(2, pitches.Count);

        var score = SvgGenerator.CollectScore(tree, spec);
        var staves = score.EnumerateStaves().Select(s => s.Staff).ToList();
        Assert.Equal(2, staves.Count);
        int Sharps(int i) => (staves[i].PerStaffKeySignature ?? score.KeySignature).Sharps;

        return (pitches[0], Sharps(0), pitches[1], Sharps(1));
    }

    // ───────────────────────────── the page ─────────────────────────────

    /// <summary>
    /// The four cells of ConcertPitch's table, on an E♭ alto saxophone (T = −9): the two
    /// spellings compose to one page shift, and the two that cancel print what the letters
    /// say. The control part never moves.
    /// </summary>
    [Theory]
    [InlineData("", "", "C5", 0)]                                  // written file, written score: the default
    [InlineData("pitch written", "", "C5", 0)]                     // …spelled out
    [InlineData("", " pitch concert", "Eb4", -3)]                  // a written part shown at what it sounds
    [InlineData("pitch concert", "", "A5", 3)]                     // a concert-pitch file printed for the player
    [InlineData("pitch concert", " pitch written", "A5", 3)]       // …spelled out
    [InlineData("pitch concert", " pitch concert", "C5", 0)]       // both: nothing moves
    public void TheTwoSpellingsComposeToOnePageShift(
        string top, string scoreOpts, string expectedPitch, int expectedSharps)
    {
        var (x, xSharps, ctl, ctlSharps) = Page(Book(top, "instrument alto-sax", scoreOpts));

        Assert.Equal(expectedPitch, x);
        Assert.Equal(expectedSharps, xSharps);
        Assert.Equal("C5", ctl);
        Assert.Equal(0, ctlSharps);
    }

    /// <summary>
    /// A concert-pitch file, one preset per row: each chromatic transposer prints its
    /// written pitch in its written key, and the octave-only ones (bass, piccolo, an explicit
    /// <c>transposition 8vb</c>) keep their notation — a concert score does not un-transpose
    /// an octave (PartHeaderDefaults.ConcertShiftSemitones). The controls are the presets
    /// that never transposed.
    /// </summary>
    [Theory]
    [InlineData("instrument clarinet", "D5", 2)]        // in B♭: up a major 2nd, D major
    [InlineData("instrument trumpet", "D5", 2)]
    [InlineData("instrument clarinet-a", "Eb5", -3)]    // in A: up a minor 3rd, E♭ major (not D♯)
    [InlineData("instrument horn", "G5", 1)]            // in F: up a 5th, G major
    [InlineData("instrument alto-sax", "A5", 3)]        // in E♭: up a major 6th, A major
    [InlineData("instrument tenor-sax", "D6", 2)]       // in B♭, an octave lower: up a major 9th
    [InlineData("instrument baritone-sax", "A6", 3)]    // in E♭, an octave lower: up a 13th
    [InlineData("instrument trumpet-c", "C5", 0)]       // control: the C trumpet
    [InlineData("instrument flute", "C5", 0)]           // control
    [InlineData("", "C5", 0)]                           // control: no instrument at all
    [InlineData("instrument bass", "C5", 0)]            // octave-only: notation kept
    [InlineData("instrument piccolo", "C5", 0)]         // octave-only
    [InlineData("transposition 8vb", "C5", 0)]          // octave-only, spelled by hand
    public void AConcertPitchFile_PrintsEachInstrumentInItsWrittenKey(
        string header, string expectedPitch, int expectedSharps)
    {
        var (x, xSharps, ctl, ctlSharps) = Page(Book("pitch concert", header));

        Assert.Equal(expectedPitch, x);
        Assert.Equal(expectedSharps, xSharps);
        Assert.Equal("C5", ctl);
        Assert.Equal(0, ctlSharps);
    }

    /// <summary>
    /// The instrument shift composes with a hand-written <c>transpose</c>, in both of its
    /// houses, the way two LilyPond <c>\transpose</c> wrappers compose: a concert-pitch
    /// alto saxophone part of a piece printed a step up is a major 6th plus a major 2nd.
    /// </summary>
    [Theory]
    [InlineData("pitch concert", "instrument alto-sax transpose d", "", "B5", 5)]   // on the part
    [InlineData("pitch concert\ntranspose d", "instrument alto-sax", "", "B5", 5)]  // the file default
    [InlineData("pitch concert", "instrument alto-sax", " transpose d", "B5", 5)]   // on the score
    public void TheInstrumentShiftComposesWithAWrittenTranspose(
        string top, string header, string scoreOpts, string expectedPitch, int expectedSharps)
    {
        var (x, xSharps, _, _) = Page(Book(top, header, scoreOpts));

        Assert.Equal(expectedPitch, x);
        Assert.Equal(expectedSharps, xSharps);
    }

    // ───────────────────────────── MIDI ─────────────────────────────

    private static int FirstMidiPitch(string top, string header, string scoreOpts = "")
    {
        var tree = Parse(
            $"time 4/4\nkey c major\noctave absolute\n{top}\n"
            + $"part x {{ {header} section A {{ c'1 | }} }}\n"
            + $"form main {{ A }}\nscore main{scoreOpts} {{ staff x }}");
        var midi = new MidiExporter().Export(tree);
        return midi.Tracks[1].Notes[0].Pitch;
    }

    /// <summary>
    /// ⚠️ THE cancellation, exactly once: a concert-pitch file's alto saxophone PRINTS <c>a'</c>
    /// (+9, through the transpose channel) and SOUNDS <c>c'</c> (−9, the preset's sounding
    /// shift), so the .mid plays the letter that was written. Passing the −9 a second time —
    /// the design ConcertPitch's remarks warn against — would play E♭4 = 63 here, the
    /// written-pitch answer, and the written-pitch row is in the table so the two cannot be
    /// confused. The octave transposers keep their notation and therefore their playback.
    /// </summary>
    [Theory]
    [InlineData("pitch concert", "instrument alto-sax", 72)]   // prints A5, sounds C5
    [InlineData("pitch concert", "instrument clarinet", 72)]   // prints D5, sounds C5
    [InlineData("pitch concert", "instrument tenor-sax", 72)]  // prints D6, sounds C5
    [InlineData("pitch concert", "instrument flute", 72)]      // control: no shift either way
    [InlineData("pitch concert", "", 72)]                      // control
    [InlineData("pitch concert", "instrument bass", 60)]       // octave-only: prints C5, sounds C4 as ever
    [InlineData("", "instrument alto-sax", 63)]                // written pitch: prints C5, sounds E♭4
    public void AConcertPitchFile_SoundsTheLetterThatWasWritten(string top, string header, int expected)
        => Assert.Equal(expected, FirstMidiPitch(top, header));

    /// <summary>
    /// A score's <c>pitch concert</c> is how the PAGE prints; the .mid sounds the instrument
    /// whatever the score asks, as it ignores the score's <c>transpose</c>.
    /// </summary>
    [Fact]
    public void AScoresPitchMode_DoesNotReachTheMidi()
        => Assert.Equal(63, FirstMidiPitch("", "instrument alto-sax", " pitch concert"));

    /// <summary>
    /// ⚠️ Found while wiring the shift: a part-major book's <c>transpose</c> reached the page
    /// and not the .mid — PlayInPart armed the instrument's sounding shift and nothing else,
    /// while the section-major arm (the <c>x { … }</c> block) composed the transpose in.
    /// Both arms now ask one function. The section-major row is the control that was
    /// already right.
    /// </summary>
    [Fact]
    public void APartMajorBooksTranspose_ReachesTheMidiAsTheSectionMajorOneDoes()
    {
        Assert.Equal(74, FirstMidiPitch("", "transpose d")); // part-major: C5 written, D5 played

        var sectionMajor = Parse(Book("", "transpose d"));
        Assert.Equal(74, new MidiExporter().Export(sectionMajor).Tracks[1].Notes[0].Pitch);
    }

    // ───────────────────────────── MusicXML ─────────────────────────────

    /// <summary>
    /// MusicXML's <c>&lt;pitch&gt;</c> is the WRITTEN pitch and <c>&lt;transpose&gt;</c> is
    /// the distance to what sounds. A concert-pitch file therefore exports the transposed
    /// pitch and the transposed key — and the SAME <c>&lt;transpose&gt;</c> as the
    /// written-pitch file, because the instrument has not changed, only the spelling of the
    /// source. The element's meaning is what HANDOFF §2 F ⒴ asked to be measured again.
    /// </summary>
    [Theory]
    [InlineData("", "C5", 0, -9)]
    [InlineData("pitch concert", "A5", 3, -9)]
    public void TheMusicXml_WritesTheWrittenPitchAndTheSameTranspose(
        string top, string expectedPitch, int expectedFifths, int expectedTranspose)
    {
        var doc = new MusicXmlExporter().Export(Parse(Book(top, "instrument alto-sax")));

        var x = doc.Parts.Single(p => p.Name == "x");
        var attrs = x.Measures[0].Attributes;
        Assert.NotNull(attrs);
        Assert.Equal(expectedFifths, attrs!.KeyFifths);
        Assert.Equal(expectedTranspose, attrs.TransposeSemitones);
        var first = x.Measures.SelectMany(m => m.Notes).First(n => n.Step != null);
        Assert.Equal(expectedPitch, first.Step + first.Octave!.Value.ToString());

        // The control part is untouched by the file's convention.
        var ctl = doc.Parts.Single(p => p.Name == "ctl");
        var ctlFirst = ctl.Measures.SelectMany(m => m.Notes).First(n => n.Step != null);
        Assert.Equal("C5", ctlFirst.Step + ctlFirst.Octave!.Value.ToString());
        Assert.Equal(0, ctl.Measures[0].Attributes?.KeyFifths ?? 0);
    }

    // ───────────────────────────── the LilyPond twin ─────────────────────────────

    /// <summary>
    /// The twin writes the shift as the <c>\transpose</c> wrapper a hand-written
    /// <c>transpose</c> gets — LilyPond's own spelling of "print this part transposed", with
    /// the target the instrument's interval names — and nothing at all when the two
    /// spellings cancel. The control part's variable never takes a wrapper.
    /// </summary>
    [Theory]
    [InlineData("pitch concert", "", "x = \\transpose c a \\fixed c'")]
    [InlineData("", " pitch concert", "x = \\transpose c ees, \\fixed c'")]
    [InlineData("pitch concert", " pitch concert", "x = \\fixed c'")]
    [InlineData("", "", "x = \\fixed c'")]
    public void TheTwin_WrapsThePartInTheInstrumentsTranspose(string top, string scoreOpts, string expected)
    {
        var ly = new LilyPondExporter().Export(Parse(Book(top, "instrument alto-sax", scoreOpts)));

        Assert.Contains(expected, ly);
        Assert.Contains("ctl = \\fixed c'", ly);
    }

    // ───────────────────────────── the spelling ─────────────────────────────

    [Theory]
    [InlineData("pitch concert")]
    [InlineData("pitch written")]
    public void BothModes_Parse(string directive)
        => Parse(Book(directive, "instrument alto-sax"));

    [Fact]
    public void TheModesList_IsTheParsersVocabulary()
        => Assert.Equal(new[] { "written", "concert" }, ConcertPitch.Modes);

    /// <summary>A third word is refused where it stands and kept, so the text round-trips
    /// and nothing after it moves — the recovery every header property follows.</summary>
    [Fact]
    public void AThirdWord_IsRefusedAtTheWord()
    {
        string source = Book("pitch banana", "instrument alto-sax");
        var tree = SyntaxTree.Parse(source);

        var diag = Assert.Single(tree.Diagnostics);
        Assert.Contains("'banana' is not a pitch mode", diag.Message);
        Assert.Contains("written or concert", diag.Message);
        Assert.Equal(source, tree.GetRoot().ToFullString());
        // …and a refused word reads as the default: the file is written-pitch.
        Assert.False(ConcertPitch.FileIsConcert(tree.GetRoot()));
    }

    [Fact]
    public void AMissingMode_IsReported()
    {
        var tree = SyntaxTree.Parse("pitch\n" + Book("", "instrument alto-sax"));

        Assert.Contains(tree.Diagnostics, d => d.Message.Contains("Expected pitch mode"));
    }

    // ───────────────────────────── the part header ─────────────────────────────

    /// <summary>
    /// A part header's own <c>pitch</c> wins over the file's, the rule <c>transpose</c> and
    /// <c>octave</c> follow — and without a chromatically transposing instrument the word
    /// changes nothing wherever it stands (the owner's reading, 2026-09-03: "instrument を
    /// 指定しない場合、pitch は noop").
    /// </summary>
    [Theory]
    [InlineData("", "instrument alto-sax pitch concert", "A5", 3)]               // own, no file default
    [InlineData("pitch written", "instrument alto-sax pitch concert", "A5", 3)]  // own beats the file
    [InlineData("pitch concert", "instrument alto-sax pitch written", "A5", 3, "C5", 0)]
    [InlineData("pitch concert", "instrument alto-sax pitch concert", "A5", 3)]  // both agree
    [InlineData("", "pitch concert", "C5", 0)]                                   // no instrument: a no-op
    [InlineData("", "instrument flute pitch concert", "C5", 0)]                  // a non-transposer: a no-op
    [InlineData("", "instrument bass pitch concert", "C5", 0)]                   // octave-only: a no-op
    public void APartsOwnPitch_WinsOverTheFiles(
        string top, string header, string expectedPitch, int expectedSharps,
        string? overridePitch = null, int? overrideSharps = null)
    {
        var (x, xSharps, ctl, ctlSharps) = Page(Book(top, header));

        Assert.Equal(overridePitch ?? expectedPitch, x);
        Assert.Equal(overrideSharps ?? expectedSharps, xSharps);
        Assert.Equal("C5", ctl);
        Assert.Equal(0, ctlSharps);
    }

    /// <summary>
    /// The case the part header exists for: one book, a saxophone copied from its transposed
    /// part-sheet and a clarinet copied from the concert-pitch score. The file says concert,
    /// the saxophone says written, and each prints and PLAYS as its source meant.
    /// </summary>
    [Fact]
    public void TwoConventions_ShareOneBook()
    {
        var tree = Parse("""
            time 4/4
            key c major
            octave absolute
            pitch concert
            part sax { instrument alto-sax pitch written }
            part cl { instrument clarinet }
            section A { sax { c'1 | } cl { c'1 | } }
            form main { A }
            score main { staff sax staff cl }
            """);

        var (sax, saxSharps, cl, clSharps) = Page(tree.Text);
        Assert.Equal(("C5", 0), (sax, saxSharps));   // printed as copied
        Assert.Equal(("D5", 2), (cl, clSharps));     // printed for the player

        var midi = new MidiExporter().Export(tree);
        var played = midi.Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch).OrderBy(p => p).ToList();
        Assert.Equal(new[] { 63, 72 }, played);      // the saxophone sounds E♭4, the clarinet C5
    }

    [Fact]
    public void APartsPitch_ReachesTheMidi()
        => Assert.Equal(72, FirstMidiPitch("", "instrument alto-sax pitch concert"));

    /// <summary>The header refuses a third word (SymbolCaseValidator, the rule every other
    /// header symbol obeys) and a second <c>pitch</c> (DuplicatePartPropertyValidator).</summary>
    [Theory]
    [InlineData("instrument alto-sax pitch banana", "pitch")]
    [InlineData("instrument alto-sax pitch Concert", "pitch")]
    [InlineData("instrument alto-sax pitch concert pitch written", "set twice")]
    public void APartHeader_RefusesWhatItCannotRead(string header, string expectedInMessage)
    {
        // Both refusals are the SEMANTIC pass's (the parser's generic header path takes any
        // word), so they are read from SemanticValidation rather than the tree.
        var diagnostics = SemanticValidation.Run(SyntaxTree.Parse(Book("", header)));

        Assert.Contains(diagnostics, d => d.Message.Contains(expectedInMessage));
    }

    /// <summary>The two score options are written in either order, and each reader finds
    /// its own — the transpose getter picks by NAME now that a second property can stand
    /// before the brace.</summary>
    [Theory]
    [InlineData(" pitch concert transpose d")]
    [InlineData(" transpose d pitch concert")]
    public void TheScoreOptions_TakeEitherOrder(string scoreOpts)
    {
        var tree = Parse(Book("", "instrument alto-sax", scoreOpts));
        var render = tree.GetRoot().DescendantNodes().OfType<RenderDeclarationSyntax>().Single();

        Assert.Equal((1, 0, 0), PartTranspose.ReadProperty(render.Transpose!));
        Assert.True(ConcertPitch.ScoreIsConcert(render));
        Assert.True(RenderSpecParser.FindFirst(tree)!.ScoreConcert);
        Assert.False(ConcertPitch.FileIsConcert(tree.GetRoot()), "a score's option is not the file's");
    }

    /// <summary>The interval spelling behind every row above: the semitone count is
    /// preserved exactly, and the class is spelled the way the instrument's key is named.</summary>
    [Theory]
    [InlineData(9, 5, 0, 0)]     // c→a
    [InlineData(-9, 2, -1, -1)]  // c→ees,
    [InlineData(2, 1, 0, 0)]     // c→d
    [InlineData(-2, 6, -1, -1)]  // c→bes,
    [InlineData(3, 2, -1, 0)]    // c→ees
    [InlineData(14, 1, 0, 1)]    // c→d'
    [InlineData(-21, 2, -1, -2)] // c→ees,,
    [InlineData(0, 0, 0, 0)]
    public void IntervalFromSemitones_RoundTripsThroughIntervalSemitones(int semitones, int step, int alt, int oct)
    {
        var interval = PitchTransposer.IntervalFromSemitones(semitones);

        Assert.Equal((step, alt, oct), interval);
        Assert.Equal(semitones, PitchTransposer.IntervalSemitones(interval.step, interval.alt, interval.oct));
    }

    // ───────────────────────────── the incremental compiler ─────────────────────────────

    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static string Full(string text) =>
        SvgGenerator.Generate(SyntaxTree.Parse(text), Opt).Replace("\r\n", "\n");

    /// <summary>
    /// Toggling either spelling is an edit like any other to the incremental session: the
    /// file directive is a top-level item the resume planner requires to be stable, and the
    /// score option changes the render block's text. Both must land on the full compile's
    /// bytes — the first with a transposing part whose every note moves.
    /// </summary>
    [Theory]
    [InlineData("pitch written", "pitch concert")]
    [InlineData("pitch concert", "pitch written")]
    public void TogglingTheFileDirective_StaysEqualToFull(string before, string after)
    {
        var tree = SyntaxTree.Parse(Book(before, "instrument alto-sax"));
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        int at = tree.Text.IndexOf(before, System.StringComparison.Ordinal);
        tree = tree.WithChange(new TextChange(new TextSpan(at, before.Length), after));

        Assert.Equal(Full(tree.Text), session.RenderIncremental(tree).Replace("\r\n", "\n"));
    }

    [Fact]
    public void AddingTheScoreOption_StaysEqualToFull()
    {
        var tree = SyntaxTree.Parse(Book("", "instrument alto-sax"));
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        int at = tree.Text.IndexOf("score main", System.StringComparison.Ordinal) + "score main".Length;
        tree = tree.WithChange(new TextChange(new TextSpan(at, 0), " pitch concert"));

        Assert.Equal(Full(tree.Text), session.RenderIncremental(tree).Replace("\r\n", "\n"));
    }
}
