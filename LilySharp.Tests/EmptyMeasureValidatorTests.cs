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
/// An empty placeholder measure is an explicit <c>| |</c> PAIR — two written barlines
/// with no music between them, anywhere in a MUSIC section (it holds a slot so parts
/// stay aligned, renders as an empty bar, and warns until filled). A SINGLE bare
/// barline never creates one: at the section's head or tail it merely anchors the
/// boundary, between full bars it confirms the auto-filled close, and a typed barline
/// (":|", "||", "|.") is a decoration. Lyrics keep their own rule (a lone leading
/// <c>|</c> there skips a bar) — lyrics have no durations, so their barlines ARE the
/// structure.
/// </summary>
[Trait("Category", "Unit")]
public class EmptyMeasureValidatorTests
{
    private static int PlaceholderCount(string music)
        => PlaceholderCountIn($"part m {{ section A {{ {music} }} }} form main {{ A }} score main {{ staff m }}");

    // The empty-placeholder warning now comes from MeasureValidator (over the shared
    // MeasureModel), form-independently. It shares the MeasureIncomplete code with the
    // underfull warning, so filter on the "empty measure" phrasing to count placeholders.
    private static int PlaceholderCountIn(string source)
    {
        var validator = new MeasureValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics.Count(d => d.Code == DiagnosticCodes.MeasureIncomplete
            && d.Severity == DiagnosticSeverity.Warning
            && d.Message.Contains("empty measure"));
    }

    [Theory]
    [InlineData("| | c4 c g' g | a a g2")]      // leading `| |` — the explicit empty bar
    [InlineData("c4 c g' g | | a a g2")]        // `| |` gap after a full bar
    [InlineData("c4 c | | a a g2")]             // `| |` gap after an UNDERFULL bar (same result)
    [InlineData("c4 c g' g | a a g2 | |")]      // trailing `| |`
    public void BarePair_WarnsOnce(string music) => Assert.Equal(1, PlaceholderCount(music));

    [Fact]
    public void LeadingAndMiddlePairs_WarnTwice() =>
        Assert.Equal(2, PlaceholderCount("| | c4 c g' g | | a a g2"));

    [Fact]
    public void ThreeConsecutiveBars_AreTwoEmptyMeasures() =>
        Assert.Equal(2, PlaceholderCount("c4 c g' g | | | a a g2"));

    [Theory]
    [InlineData("c4 c g' g | a a g2")]          // one plain `|` delimiting two bars
    [InlineData("| c4 c g' g | a a g2")]        // leading `|` anchors the section start
    [InlineData("c4 c g' g | a a g2 |")]        // trailing `|` confirms the auto-filled last bar
    [InlineData("| c4 c g' g | a a g2 |")]      // both edges anchored — the symmetric idiom
    [InlineData("c4 c g' g | a a g2 |.")]       // typed final barline, not a gap
    [InlineData("c4 c g' g | a a g2 ||")]       // typed double barline, not a gap
    [InlineData("|")]                           // a lone bar delimits nothing: empty section
    public void NoBarePair_NoWarning(string music) => Assert.Equal(0, PlaceholderCount(music));

    [Theory]
    [InlineData("form main { A }")]   // referenced by the form
    [InlineData("form main { }")]     // NOT referenced — validated all the same
    [InlineData("")]                  // no form at all
    public void EmptyMeasure_WarnsRegardlessOfTheForm(string form)
    {
        // A section validates the moment it is written, before it is wired into a
        // form — the underfull check always did, and the empty-placeholder check now
        // matches it (it used to be silent unless the form referenced the section).
        var src = $"section A {{ melody {{ | | r1 }} }} {form} score main {{ staff melody }}";
        Assert.Equal(1, PlaceholderCountIn(src));
    }

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
    public void ExplicitEmptyBarAfterPhrase_StillWarns()
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
    public void LeadingSingleBar_CreatesNoMeasure()
    {
        // `{ | c1 | c1 | }` is exactly `{ c1 | c1 }` — two measures, edges anchored.
        var src = "part m { section A { | c1 | c1 | } } form main { A } score main { staff m }";
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(src), "m");
        Assert.Equal(2, score.Voice.Measures.Length);
        Assert.All(score.Voice.Measures, m => Assert.False(m.IsEmptyPlaceholder));
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
    public void LeadingClefBeforePhrase_InsertsNoEmptyMeasure()
    {
        // A section-head DIRECTIVE (a `clef`) has zero duration, so it does NOT fill a
        // span. When the phrase it precedes opens with a leading `|` (an anchor, not a
        // separator), that `|` must merely confirm the section-start boundary and carry
        // the clef into the FIRST real measure — not close a spurious clef-only empty bar.
        // Regression: `clef bass x | …` used to draw an empty measure before the music.
        // (bass, not treble: an UNCHANGED clef now engraves nothing at all —
        // lily/clef-engraver.cc inspect_clef_properties, clef-unchanged.ly — so the
        // directive must differ from the default to leave an item to assert on.)
        var src = "phrase x { | c d e f | c' b a g | } "
                + "part melody2 { section A { clef bass x | x | x | } } "
                + "form main { A } score main { staff melody2 }";
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(src), "melody2");

        Assert.All(score.Voice.Measures, m => Assert.False(m.IsEmptyPlaceholder));
        // The clef rides in the first content measure, whose first note is real music.
        var first = score.Voice.Measures[0];
        Assert.Contains(first.Items, i => i is LilySharp.Core.Svg.Model.ClefChangeItem);
        Assert.Contains(first.Items, i => i is LilySharp.Core.Svg.Model.NoteItem);
    }

    [Fact]
    public void LeadingClefBeforePhrase_WarnsNoEmptyMeasure()
    {
        // The validator agrees with the collector: no empty-measure warning is raised for
        // a directive that merely precedes the phrase's leading anchor barline.
        var src = "phrase x { | c d e f | c' b a g | } "
                + "part melody2 { section A { clef treble x | x | x | } } "
                + "form main { A } score main { staff melody2 }";
        Assert.Equal(0, PlaceholderCountIn(src));
    }
}
