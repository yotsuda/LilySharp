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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A repeat barline written in the <c>form</c> itself, OUTSIDE a <c>|: … :|</c> block.
/// </summary>
/// <remarks>
/// ⚠️ Until 2026-08-15 this token did not exist. <c>ParseFormItem</c> had no arm for
/// <c>RepeatEndBar</c>, so it returned null and <c>ParseList</c>'s shared
/// <c>else Advance()</c> — the same infinite-loop guard whose part-header twin was
/// LYS0025 — dropped it. Measured on <c>form main { … Solo :| }</c>: the MIDI hash, the
/// SVG hash, the MusicXML repeat count and the LilyPond twin were ALL byte-identical to
/// not writing it, and `check` reported nothing. A book in the author's own library
/// (Addicted To Love.lys) ends exactly that way.
/// <para>
/// The barline is a SCORE-level object, not a part-level one — the collector already says
/// so (<c>SynchronizeBarlines</c>: "propagates the strongest start/end barline at each
/// measure index to every voice — score-level Timing semantics"), and it is measurable:
/// writing <c>|: … :|</c> in only one part of a two-part score draws the repeat dots on
/// BOTH staves. That is what makes a form-level repeat barline well-posed at all.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FormRepeatBarlineTests
{
    private static Measure[] Measures(string src) =>
        new MeasureCollector().Collect(SyntaxTree.Parse(src), "m").Voice.Measures.ToArray();

    private const string TwoSections =
        "part m { clef treble section A { c1 } section B { d1 } }\n";

    /// <summary>A form-level <c>:|</c> is engraved, on the bar it follows.</summary>
    [Fact]
    public void AFormLevelRepeatEnd_IsEngraved()
    {
        var m = Measures(TwoSections + "form main { A B :| }\nscore main { staff m }");
        Assert.Equal(2, m.Length);
        Assert.Equal(BarlineType.RepeatEnd, m[^1].EndBarline);
    }

    /// <summary>
    /// …and without it the same score ends in a plain bar, so the assertion above is
    /// measuring the token and not the end of the piece.
    /// </summary>
    /// <remarks>
    /// ⚠️ The control is <c>Single</c>, not <c>Final</c>: this collector path (the one
    /// <see cref="BackToBackRepeatTests"/> uses too) hands back the voice's measures before
    /// the closing barline is stamped. Written down because the first draft of this pair
    /// asserted <c>Final</c> and the CONTROL is what caught it.
    /// </remarks>
    [Fact]
    public void WithoutIt_TheSameScoreEndsInAPlainBar()
    {
        var m = Measures(TwoSections + "form main { A B }\nscore main { staff m }");
        Assert.Equal(2, m.Length);
        Assert.Equal(BarlineType.Single, m[^1].EndBarline);
    }

    private static string Src(string form) => TwoSections + form + "\nscore main { staff m }";

    /// <summary>The twin plays it: the stretch before the bar becomes a <c>\repeat volta</c>
    /// body, and the score's initial-repeat-bar setting goes off, since no <c>|:</c> was
    /// written at the start.</summary>
    /// <remarks>
    /// LilyPond's <c>\bar ":|."</c> is a GLYPH; only <c>\repeat volta</c> repeats. Until
    /// 2026-09-10 the twin wrote the glyph and warned that it played the music once — a twin
    /// that compiled and was different music. Now the body is wrapped; the warning is gone
    /// with the defect, and the control (no <c>:|</c>) keeps its plain stream and
    /// <c>printInitialRepeatBar = ##t</c>.
    /// </remarks>
    [Fact]
    public void AFormLevelRepeatEnd_ReachesTheTwin_AsARepeatFromTheBeginning()
    {
        var exporter = new LilyPondExporter();
        var with = exporter.Export(SyntaxTree.Parse(Src("form main { A B :| }")));
        Assert.Equal(1, Occurrences(with, "\\repeat volta 2 {"));
        // Both sections are INSIDE the body, in order.
        int open = with.IndexOf("\\repeat volta 2 {", System.StringComparison.Ordinal);
        int close = with.IndexOf("}", with.IndexOf("d1", open, System.StringComparison.Ordinal), System.StringComparison.Ordinal);
        Assert.True(open < with.IndexOf("c1", System.StringComparison.Ordinal)
            && with.IndexOf("c1", System.StringComparison.Ordinal) < with.IndexOf("d1", System.StringComparison.Ordinal)
            && with.IndexOf("d1", System.StringComparison.Ordinal) < close, with);
        Assert.DoesNotContain("\\bar \":|.\"", with);
        Assert.Contains("printInitialRepeatBar = ##f", with);
        Assert.DoesNotContain(exporter.Warnings, w => w.Contains("one-sided ':|'"));

        var plain = new LilyPondExporter();
        var without = plain.Export(SyntaxTree.Parse(Src("form main { A B }")));
        Assert.DoesNotContain("\\repeat volta", without);
        Assert.DoesNotContain("\\bar \":|.\"", without);
        Assert.Contains("printInitialRepeatBar = ##t", without);
        Assert.DoesNotContain(plain.Warnings, w => w.Contains("one-sided ':|'"));
    }

    /// <summary>A form-level <c>:|*3</c> is the body's play count.</summary>
    [Fact]
    public void AFormLevelRepeatEnd_CarriesItsPlayCountIntoTheTwin()
    {
        var ly = new LilyPondExporter().Export(SyntaxTree.Parse(Src("form main { A B :|*3 }")));
        Assert.Equal(1, Occurrences(ly, "\\repeat volta 3 {"));
    }

    /// <summary>
    /// A form-level <c>:|:</c> is two bars — <c>:|</c> then <c>|:</c> — so <c>A :|: B :|</c> is
    /// two repeat bodies, A's and B's, and the page's three repeat bars are all in the twin.
    /// </summary>
    /// <remarks>
    /// MEASURED 2026-09-10 (scratch/ベースタブLy/pageBreak.lys, user report): the page drew
    /// <c>:|</c> after A, <c>|:</c> before B, <c>:|</c> after B; the twin wrote A bare and only
    /// <c>\repeat volta 2 { B }</c> — the divider fell into <c>FormWalk.Other</c>, reached the
    /// stream as a bar line, and the music-stream loop opened a repeat AT it, so the <c>:|</c>
    /// half was never written.
    /// </remarks>
    [Fact]
    public void ABackToBackDivider_AtFormLevel_IsTwoRepeatsInTheTwin()
    {
        var exporter = new LilyPondExporter();
        var ly = exporter.Export(SyntaxTree.Parse(Src("form main { A :|: B :| }")));
        Assert.Equal(2, Occurrences(ly, "\\repeat volta 2 {"));
        int first = ly.IndexOf("\\repeat volta 2 {", System.StringComparison.Ordinal);
        int second = ly.IndexOf("\\repeat volta 2 {", first + 1, System.StringComparison.Ordinal);
        int c1 = ly.IndexOf("c1", System.StringComparison.Ordinal);
        int d1 = ly.IndexOf("d1", System.StringComparison.Ordinal);
        Assert.True(first < c1 && c1 < second && second < d1, ly);
        Assert.DoesNotContain("\\bar \":|", ly);
        Assert.Contains("printInitialRepeatBar = ##f", ly);
        Assert.Empty(exporter.Warnings);
    }

    /// <summary>
    /// A rewind over a WRITTEN <c>|: … :|</c> nests the block inside the body and keeps the
    /// initial repeat bar: the opener at moment 0 was written, so the page draws it.
    /// </summary>
    [Fact]
    public void ARewindOverAWrittenRepeat_NestsIt_AndKeepsTheWrittenOpener()
    {
        var ly = new LilyPondExporter().Export(SyntaxTree.Parse(Src("form main { |: A :| B :| }")));
        Assert.Equal(2, Occurrences(ly, "\\repeat volta 2 {"));
        Assert.Contains("printInitialRepeatBar = ##t", ly);
    }

    /// <summary>
    /// Two rewinds nest, and the twin says what that changes: LilyPond replays the inner
    /// repeat on the outer pass where Lily# replays the written stretch once
    /// (<see cref="TwoOneSidedEnds_RewindOnceEach_AndDoNotRunAway"/>).
    /// </summary>
    [Fact]
    public void TwoRewinds_NestInTheTwin_AndSaySoInTheWarning()
    {
        var exporter = new LilyPondExporter();
        var ly = exporter.Export(SyntaxTree.Parse(Src("form main { A :| B :| }")));
        Assert.Equal(2, Occurrences(ly, "\\repeat volta 2 {"));
        Assert.Contains(exporter.Warnings, w => w.Contains("nest"));
    }

    /// <summary>
    /// The <c>:|</c> BETWEEN two endings is the repeat's own divider, not a rewind: three
    /// endings are one <c>\repeat volta 3</c> with a three-entry <c>\alternative</c>.
    /// </summary>
    /// <remarks>
    /// Found by the p364 sweep (2026-09-10): the first cut of the rewind wrapping read this
    /// divider as a rewind and wrapped three books (audit/lpreg/voltasky, Danger Zone, Disco
    /// Inferno) whole; before that cut the divider fell out of <c>EmitInlineRepeat</c> as a
    /// stray <c>\bar ":|."</c> with the third ending outside the repeat. Both were wrong
    /// against the page, which draws the divider as ending 2's close and brackets ending 3.
    /// </remarks>
    [Fact]
    public void ADividerBetweenEndings_IsTheRepeatsOwn_NotARewind()
    {
        const string src =
            "part m { clef treble section A { c1 } section B { d1 } section C { e1 } section D { f1 } }\n"
            + "form main { |: A [1. B] :| [2. C] :| [3. D] }\nscore main { staff m }";
        var exporter = new LilyPondExporter();
        var ly = exporter.Export(SyntaxTree.Parse(src));
        Assert.Equal(1, Occurrences(ly, "\\repeat volta 3 {"));
        Assert.Equal(1, Occurrences(ly, "\\alternative {"));
        Assert.DoesNotContain("\\bar \":|", ly);
        // All three endings are inside the one \alternative, in order.
        int alt = ly.IndexOf("\\alternative {", System.StringComparison.Ordinal);
        int d1 = ly.IndexOf("d1", System.StringComparison.Ordinal);
        int e1 = ly.IndexOf("e1", System.StringComparison.Ordinal);
        int f1 = ly.IndexOf("f1", System.StringComparison.Ordinal);
        Assert.True(alt < d1 && d1 < e1 && e1 < f1, ly);
        Assert.Contains("printInitialRepeatBar = ##t", ly);
        Assert.Empty(exporter.Warnings);
    }

    /// <summary>The chord row is split where its staff is: a chord track over
    /// <c>A :|: B :|</c> is two <c>\repeat</c> bodies too.</summary>
    [Fact]
    public void ABackToBackDivider_SplitsTheChordTrackTheSameWay()
    {
        const string src =
            "part m { clef treble section A { c1 } section B { d1 } }\n"
            + "chords h { section A { C } section B { G } }\n"
            + "form main { A :|: B :| }\nscore main { staff m chords h }";
        var ly = new LilyPondExporter().Export(SyntaxTree.Parse(src));
        int chordmode = ly.IndexOf("\\chordmode {", System.StringComparison.Ordinal);
        Assert.True(chordmode >= 0, ly);
        int end = ly.IndexOf("\\score", System.StringComparison.Ordinal);
        string chords = ly.Substring(chordmode, end - chordmode);
        Assert.Equal(2, Occurrences(chords, "\\repeat volta 2 {"));
    }

    /// <summary>
    /// MusicXML gets a backward repeat with no matching forward one — which is MusicXML's
    /// own spelling for "repeat from the beginning", the reading this grammar gives a
    /// one-sided <c>:|</c>. So this walk says the right thing without a Lily#-specific
    /// extension.
    /// </summary>
    [Fact]
    public void AFormLevelRepeatEnd_ReachesMusicXmlAsABackwardRepeat()
    {
        // ⚠️ Export returns the DOCUMENT MODEL, not serialized XML — the first draft of this
        // test matched on `.ToString()` and was reading a type name, so it counted 0 for
        // both sides (RULES §5.4: a checker has to be shown failing on a known input).
        var with = new MusicXmlExporter().Export(SyntaxTree.Parse(Src("form main { A B :| }")));
        var without = new MusicXmlExporter().Export(SyntaxTree.Parse(Src("form main { A B }")));
        Assert.True(with.Parts[0].Measures[^1].RepeatBackward);
        Assert.False(without.Parts[0].Measures[^1].RepeatBackward);
        // No forward repeat anywhere: backward-without-forward is the MusicXML spelling.
        Assert.DoesNotContain(with.Parts[0].Measures, m => m.RepeatForward);
    }

    /// <summary>
    /// It is PLAYED, from the beginning of the piece.
    /// </summary>
    /// <remarks>
    /// This test is the falsifier the previous便 planted as
    /// <c>AFormLevelRepeatEnd_IsNotYetPlayed</c> — it asserted that MIDI ignored the bar,
    /// so moving it is what proves the semantics landed rather than that a silent walk
    /// stayed silent.
    /// </remarks>
    [Fact]
    public void AFormLevelRepeatEnd_PlaysThePieceFromTheBeginning()
    {
        Assert.Equal(new[] { 60, 62 }, Pitches(Src("form main { A B }")));
        Assert.Equal(new[] { 60, 62, 60, 62 }, Pitches(Src("form main { A B :| }")));
    }

    /// <summary>
    /// It rewinds to the START of the piece, not to the previous section — so a repeat
    /// after three sections replays all three.
    /// </summary>
    /// <remarks>
    /// ⚠️ Written as a THREE-section score on purpose. With two sections "from the
    /// beginning" and "from the previous section boundary" happen to differ only in one
    /// section, and with one section they agree outright — a case that cannot tell two
    /// candidate rules apart is not measuring the rule.
    /// </remarks>
    [Fact]
    public void ItRewindsToTheStartOfThePiece_NotToTheLastSection()
    {
        const string three =
            "part m { clef treble section A { c1 } section B { d1 } section C { e1 } }\n";
        Assert.Equal(new[] { 60, 62, 64 },
            Pitches(three + "form main { A B C }\nscore main { staff m }"));
        Assert.Equal(new[] { 60, 62, 64, 60, 62, 64 },
            Pitches(three + "form main { A B C :| }\nscore main { staff m }"));
    }

    /// <summary>
    /// The rewind happens WHERE THE BAR IS, not at the end of the form: a <c>:|</c> after
    /// two of three sections replays those two and then plays the third.
    /// </summary>
    [Fact]
    public void ItReplaysOnlyWhatComesBeforeTheBar()
    {
        Assert.Equal(new[] { 60, 62, 60, 62, 64 },
            Pitches(
                "part m { clef treble section A { c1 } section B { d1 } section C { e1 } }\n"
                + "form main { A B :| C }\nscore main { staff m }"));
    }

    /// <summary>
    /// A form-level <c>:|:</c> is two bars to EVERY reader: the <c>:|</c> half rewinds and
    /// the <c>|:</c> half opens a block the next form-level <c>:|</c> closes — so
    /// <c>A :|: B :|</c> sounds A A B B, as the page draws it and the twin writes it.
    /// </summary>
    /// <remarks>
    /// MEASURED 2026-09-10 (scratch/ベースタブLy/pageBreak.lys): MIDI sounded A B A B and
    /// MusicXML wrote one backward repeat on the last bar — the token was
    /// <c>FormWalk.Other</c>, so both walks skipped it and rewound at the closing <c>:|</c>.
    /// The page and (from that day) the twin read A A B B. <c>FormWalk.GroupDividerRepeats</c>
    /// is where the three readers of the walk now get the page's answer.
    /// </remarks>
    [Fact]
    public void ABackToBackDivider_AtFormLevel_RewindsThenRepeats_InMidi()
    {
        Assert.Equal(new[] { 60, 60, 62, 62 }, Pitches(Src("form main { A :|: B :| }")));
        // A second divider closes the block and opens the next — it rewinds nothing.
        Assert.Equal(new[] { 60, 60, 62, 62, 64, 64 }, Pitches(
            "part m { clef treble section A { c1 } section B { d1 } section C { e1 } }\n"
            + "form main { A :|: B :|: C :| }\nscore main { staff m }"));
        // The closing bar's count is the block's.
        Assert.Equal(new[] { 60, 60, 62, 62, 62 }, Pitches(Src("form main { A :|: B :|*3 }")));
    }

    [Fact]
    public void ABackToBackDivider_AtFormLevel_IsThreeRepeatBarsInMusicXml()
    {
        var doc = new MusicXmlExporter().Export(SyntaxTree.Parse(Src("form main { A :|: B :| }")));
        var m = doc.Parts[0].Measures;
        Assert.Equal(2, m.Count);
        Assert.False(m[0].RepeatForward);
        Assert.True(m[0].RepeatBackward);   // the ':|' half, after A
        Assert.True(m[1].RepeatForward);    // the '|:' half, before B
        Assert.True(m[1].RepeatBackward);   // the closing ':|', after B
    }

    /// <summary>
    /// A <c>:|:</c> INSIDE a written block splits it into runs, each its own repeat — the
    /// page's, the twin's and MusicXML's reading; the MIDI used to play the whole body per
    /// pass (B C B C for <c>|: B :|: C :|</c>). The block's <c>:|*N</c> applies to every run,
    /// as the twin writes it on each run's close; endings belong to the run they follow.
    /// </summary>
    [Fact]
    public void ABackToBackDivider_InsideABlock_SplitsItIntoRuns_InMidi()
    {
        const string three =
            "part m { clef treble section A { c1 } section B { d1 } section C { e1 } section D { f1 } }\n";
        Assert.Equal(new[] { 60, 60, 62, 62 },
            Pitches(three + "form main { |: A :|: B :| }\nscore main { staff m }"));
        Assert.Equal(new[] { 60, 60, 60, 62, 62, 62 },
            Pitches(three + "form main { |: A :|: B :|*3 }\nscore main { staff m }"));
        Assert.Equal(new[] { 60, 60, 62, 64, 62, 65 },
            Pitches(three + "form main { |: A :|: B [1. C] :| [2. D] }\nscore main { staff m }"));
    }

    /// <summary>The reader's own shape: rewind, then a block with the divider's token as its
    /// opener and the closing bar's token as its close; endings after the close are the
    /// block's.</summary>
    [Fact]
    public void TheReader_YieldsARewindAndABlock_ForAFormLevelDivider()
    {
        var form = SyntaxTree.Parse(
                "part m { clef treble section A { c1 } section B { d1 } section C { e1 } section D { f1 } }\n"
                + "form main { A :|: B [1. C] :| [2. D] }\nscore main { staff m }")
            .GetRoot().DescendantNodes().OfType<FormDeclarationSyntax>().Single();
        var items = FormWalk.Read(form);
        Assert.Collection(items,
            i => Assert.Equal("A", Assert.IsType<FormWalk.SectionRef>(i).Name),
            i => Assert.IsType<FormWalk.LoneRepeatEnd>(i),
            i =>
            {
                var block = Assert.IsType<FormWalk.Repeat>(i);
                Assert.Null(block.Node);
                Assert.Collection(block.Children,
                    c => Assert.IsType<FormWalk.RepeatStart>(c),
                    c => Assert.Equal("B", Assert.IsType<FormWalk.SectionRef>(c).Name),
                    c => Assert.Equal("C", Assert.IsType<FormWalk.Ending>(c).Node.SectionName.Text),
                    c => Assert.IsType<FormWalk.RepeatEnd>(c),
                    c => Assert.Equal("D", Assert.IsType<FormWalk.Ending>(c).Node.SectionName.Text));
            });
    }

    /// <summary>
    /// One rewind per written <c>:|</c>. A second one-sided <c>:|</c> inside the stretch
    /// being replayed must not rewind again — that does not terminate.
    /// </summary>
    [Fact]
    public void TwoOneSidedEnds_RewindOnceEach_AndDoNotRunAway()
    {
        // A B | rewind(A B) | C | rewind(A B C)  — the second ':|' replays what is WRITTEN
        // before it, not what was PLAYED before it, so the first rewind is not replayed a
        // second time. Written-order is the reading that terminates.
        var pitches = Pitches(
            "part m { clef treble section A { c1 } section B { d1 } section C { e1 } }\n"
            + "form main { A B :| C :| }\nscore main { staff m }");
        Assert.Equal(new[] { 60, 62, /*rewind*/ 60, 62, /**/ 64, /*rewind*/ 60, 62, 64 }, pitches);
    }

    private static int[] Pitches(string src) =>
        new MidiExporter().Export(SyntaxTree.Parse(src))
            .Tracks[1].Notes.OrderBy(n => n.StartTick).Select(n => n.Pitch).ToArray();

    private static int Occurrences(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle); i >= 0; i = haystack.IndexOf(needle, i + needle.Length))
            n++;
        return n;
    }
}
