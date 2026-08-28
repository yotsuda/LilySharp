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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A WRITTEN <c>|</c> CLOSES EXACTLY ONE MEASURE, and a measure with nothing in it is an
/// empty one (owner's decision, 2026-08-28). So an empty placeholder is any span between
/// two boundaries that no music filled — a <c>| |</c> pair, and equally the <c>|</c> that
/// OPENS a section, which closes the empty span the section opened. It holds a slot so
/// parts stay aligned and renders as an empty bar; the engine FILLS it with a full-measure
/// spacer and says nothing about it.
/// <para>
/// The exceptions are the two barlines that close nothing. A TYPED barline (<c>:|</c>,
/// <c>||</c>, <c>|.</c>) on an empty span DECORATES the bar behind it. A <c>|:</c> OPENS
/// the bar in front of it, so it makes an empty measure only when a written bar opened the
/// span it leaves behind — never at a scope start. And a bare <c>|</c> landing on a
/// boundary something else just closed (an auto-fill, a phrase exit) merely CONFIRMS it.
/// </para>
/// <para>
/// ⚠️ THE FILE OUTLIVED ITS NAME. It began as the net for a WARNING (LYS2001, "duration 0"),
/// which the owner retired on 2026-08-28; what survives is the BARE-BARLINE RULE — how many
/// empty bars a spelling makes — read from the measures the collector builds, plus the
/// section at the bottom that pins what those bars are now worth.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public class EmptyMeasureValidatorTests
{
    private static int PlaceholderCount(string music)
        => PlaceholderCountIn($"part m {{ section A {{ {music} }} }} form main {{ A }} score main {{ staff m }}");

    // ⚠️ THIS COUNTED WARNINGS UNTIL 2026-08-28, when the owner asked for `| |` to stop
    // being diagnosed at all (the engine fills the bar itself — see the section at the
    // bottom of this file). Every case below is about the BARE-BARLINE RULE — how many
    // empty bars a spelling makes — so the cases and their numbers are unchanged and only
    // the thing they are read from moved: from the diagnostic to the placeholder measures
    // the collector actually builds, which is the answer the rule was always about.
    // The silence itself is asserted separately, and against EVERY code, so this helper
    // cannot go quietly blind if some other pass starts reporting the same bars.
    private static int PlaceholderCountIn(string source)
    {
        var tree = SyntaxTree.Parse(source);
        int total = 0;
        foreach (var part in tree.GetRoot().DescendantNodes()
                     .OfType<LilySharp.Core.Syntax.PartDeclarationSyntax>())
            total += new LilySharp.Core.Svg.Collector.MeasureCollector()
                .Collect(tree, part.Name.Text)
                .Voice.Measures.Count(m => m.IsEmptyPlaceholder);
        return total;
    }

    [Theory]
    [InlineData("| c4 c g' g | a a g2")]        // the `|` that OPENS the section closes an empty bar
    [InlineData("c4 c g' g | | a a g2")]        // `| |` gap after a full bar
    [InlineData("c4 c | | a a g2")]             // `| |` gap after an UNDERFULL bar (same result)
    [InlineData("c4 c g' g | a a g2 | |")]      // trailing `| |`
    [InlineData("|")]                           // a lone `|` closes the empty span it opened
    public void OneUnfilledSpan_MakesOneEmptyMeasure(string music) => Assert.Equal(1, PlaceholderCount(music));

    [Fact]
    public void LeadingAndMiddleGaps_MakeTwoEmptyMeasures() =>
        Assert.Equal(2, PlaceholderCount("| c4 c g' g | | a a g2"));

    [Fact]
    public void ThreeConsecutiveBars_AreTwoEmptyMeasures() =>
        Assert.Equal(2, PlaceholderCount("c4 c g' g | | | a a g2"));

    // ⚠️ THE AUTHOR'S OWN COUNTING, and the case that drove the 2026-08-28 decision:
    // `君の恋人になったら` is written four bars to the line throughout, and its
    // `section A { | | | | break }` came out THREE before it. Four written `|`, four bars.
    [Fact]
    public void FourBaresAreFourEmptyMeasures() => Assert.Equal(4, PlaceholderCount("| | | |"));

    [Theory]
    [InlineData("c4 c g' g | a a g2")]          // one plain `|` delimiting two bars
    [InlineData("c4 c g' g | a a g2 |")]        // trailing `|` confirms the auto-filled last bar
    [InlineData("c4 c g' g | a a g2 |.")]       // typed final barline, not a gap
    [InlineData("c4 c g' g | a a g2 ||")]       // typed double barline, not a gap
    [InlineData("|: c'4 d e f :|")]             // `|:` OPENS the first bar — it closes no span
    public void NoUnfilledSpan_MakesNone(string music) => Assert.Equal(0, PlaceholderCount(music));

    // A typed barline DECORATES the bar behind it — but with no bar behind it there is
    // nothing to decorate, and it closes the span it stands at the end of like any other
    // written bar. So `{ || }` is one empty measure ending in a double barline, which is
    // also what `{ | }` gives with a plain end. (Predicted 0 when this was written and
    // measured 1; the reading above is the code's, and it is the consistent one.)
    [Fact]
    public void TypedBarWithNothingToDecorate_ClosesItsOwnSpan()
        => Assert.Equal(1, PlaceholderCount("||"));

    // (THREE THEORIES STOOD HERE and went with the pass they guarded, 2026-08-28:
    // PartMajorTrackCells_AreNotMeasuredAsMusic, StafflessLeadSheetSection_IsNotMeasuredAsMusic
    // and AMusicSectionStillWarns_SoTheTrackExemptionIsNotABlanketOne — plus
    // EmptyMeasure_WarnsRegardlessOfTheForm above them. All four were about
    // MeasureValidator.ValidateEmptyPlaceholders: which sections its scope list REACHED, and
    // that the WARNING it raised did not depend on a form referencing them. The warning is
    // gone (the owner asked for `| |` to be written without one), and with it the pass and
    // its scope list. The worry under the track cases — that a chord or lyric TRACK's
    // barlines get read as music — is still answered, structurally and elsewhere:
    // CrossPartMeasureValidator scopes on PartBlockSyntax, so a track never reaches it at
    // all, and the collector routes a track through MeasureCollector.IsInsidePartMajorTrack.
    // Rebuilding a scope list here only to have something to assert would be keeping a net
    // for a machine that was removed.)

    [Fact]
    public void PhraseTrailingBarline_DoesNotPairWithAnOuterBarline()
    {
        // `phrase x { … | }` closes its own last bar; `x | x` then adds a separator.
        // The phrase's trailing `|` and that separator must NOT read as a `| |` empty
        // pair — a reference is ONE item, its boundary re-arms like a section start.
        var src = "phrase x { c d e f | } part melody { section A { x | x } } "
                + "form main { A } score main { staff melody }";
        Assert.Equal(0, PlaceholderCountIn(src));
    }

    [Fact]
    public void ExplicitEmptyBarAfterPhrase_IsStillAnEmptyBar()
    {
        // `x | | x` — an EXPLICIT `| |` pair after the phrase is still an empty bar
        // (the boundary re-arm absorbs ONE barline, not a written pair).
        var src = "phrase x { c d e f | } part melody { section A { x | | x } } "
                + "form main { A } score main { staff melody }";
        Assert.Equal(1, PlaceholderCountIn(src));
    }

    [Fact]
    public void EmptyMeasureInsidePhraseBody_IsPreserved()
    {
        // A `| |` pair WITHIN the phrase body is a real empty measure and still warns.
        var src = "phrase x { c d e f | | g a b c' } part melody { section A { x } } "
                + "form main { A } score main { staff melody }";
        Assert.Equal(1, PlaceholderCountIn(src));
    }

    [Fact]
    public void LeadingSingleBar_CreatesAnEmptyMeasure()
    {
        // `{ | c1 | c1 | }` is THREE measures: the leading `|` closes the empty span the
        // section opened, then two of music. The trailing `|` still closes nothing — it
        // confirms the auto-fill — which is what keeps the 497 tracked books that end a
        // block `… c1 |` at their written length.
        var src = "part m { section A { | c1 | c1 | } } form main { A } score main { staff m }";
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(src), "m");
        Assert.Equal(3, score.Voice.Measures.Length);
        Assert.True(score.Voice.Measures[0].IsEmptyPlaceholder);
        Assert.All(score.Voice.Measures.Skip(1), m => Assert.False(m.IsEmptyPlaceholder));
    }

    [Fact]
    public void PhraseBoundary_RendersTwoBars_NoEmptyPlaceholder()
    {
        // The render agrees with the validator: `phrase x { c d e f | }` used as
        // `x | x` is two content bars, with no empty placeholder between them.
        var src = "phrase x { c d e f | } part melody { section A { x | x } } "
                + "form main { A } score main { staff melody }";
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(src), "melody");
        Assert.Equal(2, score.Voice.Measures.Length);
        Assert.All(score.Voice.Measures, m => Assert.False(m.IsEmptyPlaceholder));
    }

    [Fact]
    public void LeadingClefBeforeAnEmptyBar_RidesInThatBar()
    {
        // A section-head DIRECTIVE (a `clef`) has zero duration, so it does not fill the
        // span — the `|` after it closes an EMPTY bar. ⚠️ THE DIRECTIVE GOES WITH IT: the
        // bar the author wrote it in is where it belongs, and dropping it was a real
        // defect (`c1 | time 3/4 | | c2.` never drew or applied the 3/4 before
        // 2026-08-28). Bass, not treble: an UNCHANGED clef engraves nothing at all
        // (lily/clef-engraver.cc inspect_clef_properties, clef-unchanged.ly), so the
        // directive must differ from the default to leave an item to assert on.
        var src = "phrase x { | c d e f | c' b a g | } "
                + "part melody2 { section A { clef bass x | x | x | } } "
                + "form main { A } score main { staff melody2 }";
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(src), "melody2");

        var first = score.Voice.Measures[0];
        Assert.True(first.IsEmptyPlaceholder);
        Assert.Contains(first.Items, i => i is LilySharp.Core.Svg.Model.ClefChangeItem);
        Assert.DoesNotContain(first.Items, i => i is LilySharp.Core.Svg.Model.NoteItem);
        // …and the music that follows is unaffected: each of the three `~x` contributes
        // its two written bars of music, so six content bars follow.
        Assert.Equal(6, score.Voice.Measures.Count(m => !m.IsEmptyPlaceholder));
    }

    [Fact]
    public void PhraseBodyLeadingBar_CountsLikeASectionsOwn()
    {
        // The validator agrees with the collector, and the two edges of a phrase answer
        // differently: ENTERING the body changes nothing, so its leading `|` closes an
        // empty bar exactly as a section's would (otherwise extracting a section into a
        // phrase would silently lose a bar). Each of the three `~x` brings one.
        var src = "phrase x { | c d e f | c' b a g | } "
                + "part melody2 { section A { clef treble x | x | x | } } "
                + "form main { A } score main { staff melody2 }";
        Assert.Equal(3, PlaceholderCountIn(src));
    }

    // ============ A SCORE WITH NO BARS AT ALL IS REPORTED (LYS2013) ============
    //
    // Owner's decision, 2026-08-28. `lysc check` answered "No errors found." for a file
    // whose every staff was blank and printed the blank sheet — the same silence that let
    // a mis-spelled `staff bass` print fifteen empty systems. LilyPond does not keep
    // quiet: a zero-duration score warns "skipping zero-duration score / consider adding a
    // spacer rest" and prints no page at all (MEASURED, 2.27.3).

    private static IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> Diagnose(string source)
    {
        var validator = new MeasureValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics;
    }

    private static bool WarnsBlank(string source) =>
        Diagnose(source).Any(d => d.Code == DiagnosticCodes.ScoreHasNoMusic);

    [Theory]
    [InlineData("part m { section A { } } form main { A } score main { staff m }")]
    [InlineData("part m { section A { clef bass } } form main { A } score main { staff m }")]
    [InlineData("part m { } section A { m { } } form main { A } score main { staff m }")]
    public void AScoreWithNoBarsAtAll_IsReported(string source) => Assert.True(WarnsBlank(source));

    // …and the other direction, which is the one that keeps the check honest. ⚠️ THE
    // SECOND CASE IS THE POINT OF THE WHOLE SESSION: one written `|` IS a bar now, so a
    // section spelled `{ | }` is a real empty measure and the page is not blank.
    [Theory]
    [InlineData("part m { section A { c1 } } form main { A } score main { staff m }")]
    [InlineData("part m { section A { | } } form main { A } score main { staff m }")]
    [InlineData("chords p { C | F | } form main { } score main { chords p }")]
    // ⚠️ A LYRICS-ONLY SCORE IS A REAL PAGE — the measure grid draws on the top text row.
    // Its bars parse as LyricMeasure holding barline TOKENS, invisible to the music walk
    // that counts Barline NODES, so the first cut of this check warned on both of the
    // tree's lyrics-only books (test/lead-sheet-lyrics, audit/lpreg/lytie). The sweep over
    // 899 books is what caught it; these two lines are what keep it caught.
    [InlineData("time 4/4 section M { lyrics w { Twin- kle | lit- tle | } } "
              + "form main { M } score main { lyrics w }")]
    [InlineData("time 4/4 lyrics w { section M { wa~o~a | } } "
              + "form main { ~M } score main { lyrics w }")]
    public void AScoreWithAnyBar_IsNotReported(string source) => Assert.False(WarnsBlank(source));

    [Fact]
    public void AFragmentWithNoScoreBlock_IsNotReported() =>
        // No `score` means no claim about a page — a parser fixture or an include is not
        // asking to print anything, so there is nothing to warn about.
        Assert.False(WarnsBlank("part m { section A { } } form main { A }"));

    // ===================== `| |` IS A FULL BAR OF SILENCE =====================
    //
    // Owner's decision, 2026-08-28: writing `| |` must not be diagnosed, and the engine
    // fills the bar with a full-measure SPACER of its own — the `s1` the author would
    // otherwise have had to type. Before that the bar was created with NO items and a
    // duration of ZERO, which is why the pair below is the load-bearing one: the bar
    // aligned on the PAGE (the layouter counts bars) and did not align in TIME (the MIDI
    // exporter counts durations), so a `| |` in one part silently pulled everything after
    // it a whole bar early against the other parts.

    private static (int Pitch, int Tick)[] Notes(string source) =>
        new LilySharp.Core.Midi.MidiExporter().Export(SyntaxTree.Parse(source))
            .Tracks.Skip(1).SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .Select(n => (n.Pitch, n.StartTick)).ToArray();

    private const string OneStaff =
        "octave absolute\ntime 4/4\npart m {{ }}\nsection A {{ m {{ {0} }} }}\n"
        + "form main {{ ~A }}\nscore main {{ staff m }}";

    [Theory]
    [InlineData("| | c'4 c' g' g' | a' a' g'2")]
    [InlineData("c'4 c' g' g' | | a' a' g'2")]
    [InlineData("c'4 c' g' g' | a' a' g'2 | |")]
    [InlineData("| | c'4 c' g' g' | | a' a' g'2")]
    public void EmptyMeasure_IsNotDiagnosedAtAll(string music)
    {
        // Not "no MeasureIncomplete" — NO diagnostic of any code. An empty bar the engine
        // fills itself has nothing left to report, and a second validator quietly picking
        // up the slack would be the same complaint wearing another number.
        var tree = SyntaxTree.Parse(string.Format(OneStaff, music));
        var validator = new MeasureValidator();
        validator.Validate(tree);
        Assert.Empty(validator.Diagnostics);
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void EmptyMeasure_SoundsLikeTheSpacerItStandsFor()
    {
        // THE IDENTITY THIS CHANGE IS ABOUT: `| |` and the `s1` an author would write by
        // hand are the same music. Asserted against each other rather than against literal
        // ticks, so a later change that moves both stays honest.
        Assert.Equal(
            Notes(string.Format(OneStaff, "c'1 | s1 | e'1")),
            Notes(string.Format(OneStaff, "c'1 | | e'1")));
    }

    [Fact]
    public void EmptyMeasure_TakesTheMETER_NotAWholeNote()
    {
        // "s1" is the 4/4 spelling of the idea; the bar is one MEASURE of the meter in
        // force, so in 3/4 it is worth a dotted half and the pair is written that way.
        const string ThreeFour =
            "octave absolute\ntime 3/4\npart m {{ }}\nsection A {{ m {{ {0} }} }}\n"
            + "form main {{ ~A }}\nscore main {{ staff m }}";
        Assert.Equal(
            Notes(string.Format(ThreeFour, "c'2. | s2. | e'2.")),
            Notes(string.Format(ThreeFour, "c'2. | | e'2.")));
    }

    [Fact]
    public void EmptyMeasure_KeepsTheOTHERPartsInTime_NotJustOnThePage()
    {
        // The defect this closes, in the shape the owner would hear it: an empty bar in
        // ONE part. The page always aligned (the layouter walks bars); the audio did not
        // (the exporter walks durations), so the upper part's third bar sounded on top of
        // the lower part's second.
        const string TwoStaves =
            "octave absolute\ntime 4/4\npart up {{ clef treble }}\npart dn {{ clef bass }}\n"
            + "section A {{ up {{ {0} }} dn {{ c1 | g1 | c1 }} }}\n"
            + "form main {{ ~A }}\nscore main {{ staffGroup {{ staff up staff dn }} }}";
        Assert.Equal(
            Notes(string.Format(TwoStaves, "c'1 | s1 | e'1")),
            Notes(string.Format(TwoStaves, "c'1 | | e'1")));
    }

    // ===================== `|:` PAIRS LIKE A BARE `|` =====================
    //
    // Owner's decision, 2026-08-28, reported against a `partial` pickup written
    // `c8 | /* HERE */ |: c'4 d e f :|` whose middle bar was not drawn — and which drew
    // it as soon as the `|:` was written `|`. `||`, `|.` and `:|` on an empty span
    // DECORATE the bar behind them and rightly create nothing; `|:` decorates nothing,
    // it OPENS the bar in front of it, so the span before it is an unowned gap. Sorting
    // it with the decorations made two spellings of one arrangement answer differently
    // with nothing in the language to explain why.

    [Theory]
    [InlineData("c'1 | |: c'4 d e f :|")]      // the reported shape, without the pickup
    [InlineData("c'1 |: |: c'4 d e f :|")]     // two openers running: still two written bars
    public void RepeatOpenerAfterAWrittenBar_MakesAnEmptyMeasure(string music)
        => Assert.Equal(1, PlaceholderCount(music));

    // ⚠️ AT A SCOPE START, `|` AND `|:` PART COMPANY, and this is the only place they do.
    // Both leave an empty span behind them there, but only a `|` CLOSES one — `|:` opens
    // the bar in front of it, so it has nothing to close and makes no bar. Before
    // 2026-08-28 the two agreed here because a leading `|` was an anchor; now `| |:` is
    // TWO empty bars (the `|` closes the section's opening span, the `|:` leaves the span
    // that `|` opened unowned) while `|:` alone is none.
    [Fact]
    public void LeadingBareThenRepeatOpener_MakesTwoEmptyMeasures()
        => Assert.Equal(2, PlaceholderCount("| |: c'4 d e f :|"));

    [Theory]
    [InlineData("c'1 |: c'4 d e f :|")]        // ONE barline doing both jobs — no gap
    [InlineData("|: c'4 d e f :|")]             // a leading `|:` closes nothing: no bar
    [InlineData("c'1 | || c'4 d e f")]         // `||` still DECORATES: the rule is not "any two"
    [InlineData("c'1 | |. ")]                   // …nor does the final barline
    public void RepeatOpener_DoesNotInventOne(string music)
        => Assert.Equal(0, PlaceholderCount(music));

    [Fact]
    public void RepeatOpenerGap_SoundsLikeTheSpacerItStandsFor()
    {
        // The same identity the bare pair carries, so the three spellings of the rule
        // (collector, MeasureModel, MIDI walk) cannot drift apart on `|:` either.
        Assert.Equal(
            Notes(string.Format(OneStaff, "c'1 | s1 |: c'4 d e f :|")),
            Notes(string.Format(OneStaff, "c'1 | |: c'4 d e f :|")));
    }

    [Fact]
    public void RepeatOpenerGap_IsNotDiagnosed()
    {
        var tree = SyntaxTree.Parse(string.Format(OneStaff, "c'1 | |: c'4 d e f :|"));
        var validator = new MeasureValidator();
        validator.Validate(tree);
        Assert.Empty(validator.Diagnostics);
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void RepeatOpenerGap_KeepsTheRepeatOnTheBarAfterIt()
    {
        // The gap is OUTSIDE the repeat: the `|:` still opens the bar it precedes, so the
        // empty measure is not the one that gets played twice.
        var src = "octave absolute time 4/4 part m { } "
                + "section A { m { c'1 | |: c'4 d e f :| } } "
                + "form main { ~A } score main { staff m }";
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(src), "m");
        Assert.Equal(3, score.Voice.Measures.Length);
        Assert.True(score.Voice.Measures[1].IsEmptyPlaceholder);
        Assert.NotEqual(LilySharp.Core.Svg.Model.BarlineType.RepeatStart,
            score.Voice.Measures[1].StartBarline);
        Assert.Equal(LilySharp.Core.Svg.Model.BarlineType.RepeatStart,
            score.Voice.Measures[2].StartBarline);
    }

    [Fact]
    public void EmptyMeasure_IsStillMarkedAsOne_AndNowHoldsTheMeter()
    {
        // The marker survives the fill: a consumer that wants to know the author wrote a
        // gap (rather than a bar of rests) still can. What changes is that the bar now
        // MEASURES like the bar it stands for.
        var src = "octave absolute\ntime 4/4\npart m { }\nsection A { m { c'1 | | e'1 } }\n"
                + "form main { ~A }\nscore main { staff m }";
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(src), "m");
        Assert.Equal(3, score.Voice.Measures.Length);
        var gap = score.Voice.Measures[1];
        Assert.True(gap.IsEmptyPlaceholder);
        Assert.Equal(new LilySharp.Core.Semantics.Fraction(4, 4), gap.TotalDuration);
        // …and it draws nothing: every item it holds is a spacer.
        Assert.NotEmpty(gap.Items);
        Assert.All(gap.Items, i => Assert.True(
            i is LilySharp.Core.Svg.Model.RestItem { IsSpacer: true }));
    }
}
