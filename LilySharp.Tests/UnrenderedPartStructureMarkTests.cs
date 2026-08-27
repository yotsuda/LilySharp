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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Navigation marks are score-level (every part shares the bar grid), so a segno written in the
/// piano part must still print on a chords-only / lyrics-only chart that omits the piano staff.
/// </summary>
[Trait("Category", "Unit")]
public class UnrenderedPartStructureMarkTests
{
    // Segno lives in part piano; the score draws only the chord + lyric rows.
    private const string Source = """
        time 4/4
        part piano { clef treble  section Main { segno c4 d e f | g a b c } }
        chords prog { section Main { C | G } }
        lyrics words { section Main { Twin- kle lit- tle | star how I } }
        form main { Main }
        score main { chords prog  lyrics words }
        """;

    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

    /// <summary>Through <see cref="SvgGenerator.CollectScore(SyntaxTree, RenderSpec?)"/> — the
    /// render path's own road choice, which sends a lone plain staff through the SINGLE-staff
    /// wrap (`collector.Collect`) rather than CollectMultiStaff. The single-staff tests below
    /// must ride this road: it is the one that never harvested.</summary>
    private static MultiStaffScore CollectViaRenderPath(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        Assert.False(spec!.IsMultiStaff, "these tests pin the SINGLE-staff wrap road");
        return SvgGenerator.CollectScore(tree, spec);
    }

    [Fact]
    public void SegnoInOmittedPart_SurfacesOnChordsLyricsScore()
    {
        var marks = Collect(Source).MusicMarks;
        // Exactly one segno — harvested from piano, not duplicated.
        Assert.Equal(1, marks.Count(m => m.Type == MusicMarkType.Segno));
    }

    [Fact]
    public void RenderingThePartItself_StillHasExactlyOneSegno()
    {
        // When the carrying part IS drawn the harvest must not double it (the dedup guard).
        var src = Source.Replace("score main { chords prog  lyrics words }",
            "score main { chords prog  staff piano }");
        Assert.Equal(1, Collect(src).MusicMarks.Count(m => m.Type == MusicMarkType.Segno));
    }

    [Fact]
    public void NoNavigationAnywhere_NoStructureMarks()
    {
        var src = Source.Replace("segno c4 d e f", "c4 d e f");
        Assert.DoesNotContain(Collect(src).MusicMarks, m => m.Type == MusicMarkType.Segno);
    }

    [Fact]
    public void RepeatBarlinesInOmittedPart_ProjectOntoTheChordRow()
    {
        // The |: :| spans section A's two bars in the piano part; the chords-only score must draw
        // the repeat over its own section-A measures even though the piano staff is not in it.
        var src = """
            time 4/4
            part piano { clef treble  section A { |: c4 d e f | g a b c :| } section B { c1 | g1 } }
            chords prog { section A { C | G } section B { C | G } }
            form main { A B }
            score main { chords prog }
            """;
        var measures = Collect(src).StaffGroups
            .SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures;
        Assert.Equal(BarlineType.RepeatStart, measures[0].StartBarline);   // section A, bar 0
        Assert.Equal(BarlineType.RepeatEnd, measures[1].EndBarline);       // section A, bar 1
        Assert.DoesNotContain(measures.Skip(2), m =>                       // section B: no repeat
            m.StartBarline == BarlineType.RepeatStart || m.EndBarline == BarlineType.RepeatEnd);
    }

    // ── The SECTION-major spelling of the same books (music in `section A { piano { … } }`
    // rather than `part piano { section A { … } }`). The two spellings are one meaning
    // (GRAMMAR §7: "the binding is where it is written"), but the harvest gate used to read
    // only the part DECLARATION's subtree, so every section-major book below silently
    // dropped its omitted part's structure (2026-08-27, measured: the two spellings of one
    // book rendered different pages). Each twin pins the gate's section-major arm.

    [Fact]
    public void SegnoInOmittedPart_SectionMajorSpelling_SurfacesToo()
    {
        var src = """
            time 4/4
            part piano { clef treble }
            chords prog { section Main { C | G } }
            lyrics words { section Main { Twin- kle lit- tle | star how I } }
            section Main { piano { segno c4 d e f | g a b c } }
            form main { Main }
            score main { chords prog  lyrics words }
            """;
        Assert.Equal(1, Collect(src).MusicMarks.Count(m => m.Type == MusicMarkType.Segno));
    }

    [Fact]
    public void RepeatBarlinesInOmittedPart_SectionMajorSpelling_ProjectOntoTheChordRow()
    {
        var src = """
            time 4/4
            part piano { clef treble }
            chords prog { section A { C | G } section B { C | G } }
            section A { piano { |: c4 d e f | g a b c :| } }
            section B { piano { c1 | g1 } }
            form main { A B }
            score main { chords prog }
            """;
        var measures = Collect(src).StaffGroups
            .SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures;
        Assert.Equal(BarlineType.RepeatStart, measures[0].StartBarline);   // section A, bar 0
        Assert.Equal(BarlineType.RepeatEnd, measures[1].EndBarline);       // section A, bar 1
        Assert.DoesNotContain(measures.Skip(2), m =>                       // section B: no repeat
            m.StartBarline == BarlineType.RepeatStart || m.EndBarline == BarlineType.RepeatEnd);
    }

    [Fact]
    public void TheTwoSpellingsOfOneBook_CollectTheSameBarlinesAndMarks()
    {
        // One meaning, two spellings: the collected barline grid and mark list must agree.
        var partMajor = """
            time 4/4
            part piano { clef treble  section A { |: segno c4 d e f | g a b c :| } }
            chords prog { section A { C | G } }
            form main { A }
            score main { chords prog }
            """;
        var sectionMajor = """
            time 4/4
            part piano { clef treble }
            chords prog { section A { C | G } }
            section A { piano { |: segno c4 d e f | g a b c :| } }
            form main { A }
            score main { chords prog }
            """;
        var pm = Collect(partMajor);
        var sm = Collect(sectionMajor);
        var pmBars = pm.StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices)
            .First().Measures.Select(m => (m.StartBarline, m.EndBarline));
        var smBars = sm.StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices)
            .First().Measures.Select(m => (m.StartBarline, m.EndBarline));
        Assert.Equal(pmBars, smBars);
        Assert.Equal(pm.MusicMarks.Select(m => (m.Type, m.MeasureIndex)),
            sm.MusicMarks.Select(m => (m.Type, m.MeasureIndex)));
    }

    [Fact]
    public void VoltaBracketsInOmittedPart_SectionMajorSpelling_ProjectToo()
    {
        var src = """
            time 4/4
            part piano { clef treble }
            chords prog { section A { C | G | A } }
            section A { piano { |: c4 d e f | [1. g2 g | ] :| [2. a2 a | ] } }
            form main { A }
            score main { chords prog }
            """;
        var voltas = Collect(src).VoltaBrackets;
        Assert.Contains(voltas, v => v.VoltaText.Contains('1'));
        Assert.Contains(voltas, v => v.VoltaText.Contains('2'));
    }

    // ── Structure carried only by a REFERENCED PHRASE (`hook` where `phrase hook
    // { |: … :| }`). The harvest's nested collect expands references and carries the
    // structure correctly (measured 2026-08-27 by forcing the gate open with a direct
    // mark), but the gate read only written syntax, so exactly these books dropped
    // their repeats. These pin the gate's phrase arm.

    [Fact]
    public void RepeatOnlyInsideAReferencedPhrase_StillProjects()
    {
        var src = """
            time 4/4
            phrase hook { |: g8 g a4 a8 a a4 | g2 f :| }
            part piano { clef treble }
            chords prog { section A { C | G } }
            section A { piano { hook } }
            form main { A }
            score main { chords prog }
            """;
        var measures = Collect(src).StaffGroups
            .SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures;
        Assert.Equal(BarlineType.RepeatStart, measures[0].StartBarline);
        Assert.Equal(BarlineType.RepeatEnd, measures[1].EndBarline);
    }

    [Fact]
    public void RepeatInsideANestedPhraseReference_StillProjects()
    {
        // hook -> core: the gate must walk references transitively, the way the
        // harvest's expansion does.
        var src = """
            time 4/4
            phrase core { |: g8 g a4 a8 a a4 | g2 f :| }
            phrase hook { core }
            part piano { clef treble  section A { hook } }
            chords prog { section A { C | G } }
            form main { A }
            score main { chords prog }
            """;
        var measures = Collect(src).StaffGroups
            .SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures;
        Assert.Equal(BarlineType.RepeatStart, measures[0].StartBarline);
        Assert.Equal(BarlineType.RepeatEnd, measures[1].EndBarline);
    }

    [Fact]
    public void SegnoOnlyInsideAReferencedPhrase_Surfaces()
    {
        var src = """
            time 4/4
            phrase hook { segno g8 g a4 a8 a a4 | g2 f }
            part piano { clef treble  section A { hook } }
            chords prog { section A { C | G } }
            form main { A }
            score main { chords prog }
            """;
        Assert.Equal(1, Collect(src).MusicMarks.Count(m => m.Type == MusicMarkType.Segno));
    }

    [Fact]
    public void PhraseWithoutStructure_ContributesNothing()
    {
        // The gate may open on the reference, but an unstructured phrase must leave
        // the score exactly as it was: no marks, no repeat barlines.
        var src = """
            time 4/4
            phrase hook { g8 g a4 a8 a a4 | g2 f }
            part piano { clef treble  section A { hook } }
            chords prog { section A { C | G } }
            form main { A }
            score main { chords prog }
            """;
        var score = Collect(src);
        Assert.DoesNotContain(score.MusicMarks, m => m.Type == MusicMarkType.Segno);
        Assert.DoesNotContain(
            score.StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures,
            m => m.StartBarline == BarlineType.RepeatStart || m.EndBarline == BarlineType.RepeatEnd);
    }

    // ── The SINGLE-staff road (`score main { staff sax }` — one plain staff goes through
    // the SvgGenerator wrap, not CollectMultiStaff). That road never called the harvest at
    // all, in EITHER spelling: extracting one part from a band book dropped every repeat and
    // navigation mark the other parts wrote (2026-08-27, measured: the extracted page was
    // byte-identical with and without the omitted part's |: :|). These pin the wiring.

    [Fact]
    public void SingleStaffScore_HarvestsTheOmittedPartsRepeats()
    {
        var src = """
            time 4/4
            part sax { }
            part piano { clef treble  section A { |: c4 d e f | g a b c :| } }
            section A { sax { c4 d e f | g4 f e d | } }
            form main { A }
            score main { staff sax }
            """;
        var measures = CollectViaRenderPath(src).StaffGroups
            .SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures;
        Assert.Equal(BarlineType.RepeatStart, measures[0].StartBarline);
        Assert.Equal(BarlineType.RepeatEnd, measures[1].EndBarline);
    }

    [Fact]
    public void SingleStaffScore_SectionMajorSpelling_HarvestsToo()
    {
        var src = """
            time 4/4
            part sax { }
            part piano { clef treble }
            section A {
              sax { c4 d e f | g4 f e d | }
              piano { |: c4 d e f | g a b c :| }
            }
            form main { A }
            score main { staff sax }
            """;
        var measures = CollectViaRenderPath(src).StaffGroups
            .SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures;
        Assert.Equal(BarlineType.RepeatStart, measures[0].StartBarline);
        Assert.Equal(BarlineType.RepeatEnd, measures[1].EndBarline);
    }

    [Fact]
    public void SingleStaffScore_SegnoInOmittedPart_Surfaces()
    {
        var src = """
            time 4/4
            part sax { }
            part piano { clef treble  section A { segno c4 d e f | g a b c } }
            section A { sax { c4 d e f | g4 f e d | } }
            form main { A }
            score main { staff sax }
            """;
        Assert.Equal(1, CollectViaRenderPath(src).MusicMarks.Count(m => m.Type == MusicMarkType.Segno));
    }

    [Fact]
    public void SingleStaffScore_MultiVoiceStaff_StillHarvests()
    {
        // A second voice sends the wrap down its OTHER road (BuildMultiVoiceScore); the
        // harvest sits before the branch, so the repeats must land there too.
        var src = """
            time 4/4
            part sax { }
            part piano { clef treble  section A { |: c4 d e f | g a b c :| } }
            section A { sax { voice { c4 d e f | g4 f e d | } { c,4 d e f | g,4 f e d | } } }
            form main { A }
            score main { staff sax }
            """;
        var measures = CollectViaRenderPath(src).StaffGroups
            .SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures;
        Assert.Equal(BarlineType.RepeatStart, measures[0].StartBarline);
        Assert.Equal(BarlineType.RepeatEnd, measures[1].EndBarline);
    }

    [Fact]
    public void VoltaBracketsInOmittedPart_ProjectOntoTheChordsOnlyScore()
    {
        // The piano part's |: … [1. …] :| [2. …] alternative endings must bracket the chords-only
        // score's matching bars even though the piano staff is not drawn.
        var src = """
            time 4/4
            part piano { clef treble  section A { |: c4 d e f | [1. g2 g | ] :| [2. a2 a | ] } }
            chords prog { section A { C | G | A } }
            form main { A }
            score main { chords prog }
            """;
        var voltas = Collect(src).VoltaBrackets;
        Assert.Contains(voltas, v => v.VoltaText.Contains('1'));
        Assert.Contains(voltas, v => v.VoltaText.Contains('2'));
    }
}
