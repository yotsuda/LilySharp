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

    /// <summary>The twin writes the barline instead of losing it — and says what it cannot
    /// carry.</summary>
    /// <remarks>
    /// LilyPond's <c>\bar ":|."</c> is a GLYPH; only <c>\repeat volta</c> repeats. So the
    /// twin engraves the same page and plays the music once, which is a twin that compiles
    /// and is different music. The exporter's rule for anything it drops is to warn, so it
    /// warns.
    /// </remarks>
    [Fact]
    public void AFormLevelRepeatEnd_ReachesTheTwin_WhichSaysItCannotPlayIt()
    {
        var exporter = new LilyPondExporter();
        var with = exporter.Export(SyntaxTree.Parse(Src("form main { A B :| }")));
        Assert.Contains("\\bar \":|.\"", with);
        Assert.Contains(exporter.Warnings, w => w.Contains("one-sided ':|'"));

        var plain = new LilyPondExporter();
        var without = plain.Export(SyntaxTree.Parse(Src("form main { A B }")));
        Assert.DoesNotContain("\\bar \":|.\"", without);
        Assert.DoesNotContain(plain.Warnings, w => w.Contains("one-sided ':|'"));
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
