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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section that states no <c>time</c> of its own opens at the SCORE meter — and ALL
/// FIVE readers of that one sentence have to say so.
/// </summary>
/// <remarks>
/// The rule is <c>MeasureCollector.ProcessSectionPrologue</c>'s, carried for everyone else
/// by <see cref="ScoreHomeMeter"/>. The fixture named for it,
/// <c>test/section-meter-resets-to-global</c>, cannot observe it: its section A ENDS in
/// the score meter, so reverting changes nothing there. The observable case — a section
/// that ends in a meter the score does not have — had no test at all, and measured on
/// 2026-08-31 three of the five readers were wrong:
/// <list type="bullet">
/// <item>the page and the bar-check reverted (correct);</item>
/// <item><c>lysc ly</c> restored the KEY and not the meter, handing LilyPond a 3/4 bar
/// holding four quarters;</item>
/// <item><c>lysc xml</c> restored neither — it moved the running state back and never
/// wrote it, so the document kept 3/4 and G major to the end;</item>
/// <item><c>lysc midi</c> left the conductor track in 3/4 for the rest of the piece.</item>
/// </list>
/// ⚠️ EVERY CASE HERE IS ONE BOOK READ FIVE WAYS. Split them into per-exporter files and
/// the next reader to be added is the next one to drift.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SectionBoundaryMeterRevertTests
{
    // Section A ends in 3/4 and G major; section B states neither, so it opens in the
    // score's 4/4 and C major. B's bar is four quarters — full under 4/4, overfull under
    // the meter A left behind, which is what makes the revert observable at all.
    private const string Book = """
        time 4/4
        part m {
          section A { c'4 d e f | time 3/4 key g major g a b | }
          section B { c'4 d e f | }
        }
        form main { ~A ~B }
        score main { staff m }
        """;

    private static SyntaxTree Tree() => SyntaxTree.Parse(Book);

    [Fact]
    public void ThePageDrawsTheScoreMeterAgain()
    {
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector().Collect(Tree(), "m");
        // Bar 3 is section B's: it holds four quarters and fills its meter, which it can
        // only do at 4/4.
        Assert.Equal(3, score.Voice.Measures.Length);
        Assert.Equal(4, score.Voice.Measures[2].Items.Count(i => i is Core.Svg.Model.NoteItem));
    }

    [Fact]
    public void TheBarCheckJudgesTheOpeningBarAtTheScoreMeter()
    {
        var validator = new MeasureValidator();
        validator.Validate(Tree());
        Assert.DoesNotContain(validator.Diagnostics, d =>
            d.Code == DiagnosticCodes.MeasureOverflow || d.Code == DiagnosticCodes.MeasureIncomplete);
    }

    [Fact]
    public void TheLilyPondTwinRestatesTheScoreMeter()
    {
        var ly = new LilyPondExporter().Export(Tree());
        int change = ly.IndexOf("\\time 3/4");
        int restore = ly.IndexOf("\\time 4/4", change + 1);
        Assert.True(change > 0, "the mid-section change is written");
        Assert.True(restore > change, "…and the boundary restates the score meter after it");
    }

    [Fact]
    public void TheMusicXmlWritesBothRevertsAtTheBoundary()
    {
        var doc = new MusicXmlExporter().Export(Tree());
        var measures = doc.Parts.SelectMany(p => p.Measures).ToList();
        Assert.Equal(3, measures.Count);
        // The bar the boundary opens says the meter AND the key again — restoring the
        // running state without writing it is what made this silent.
        Assert.Equal(4, measures[2].Attributes?.TimeBeats);
        Assert.Equal(4, measures[2].Attributes?.TimeBeatType);
        Assert.Equal(0, measures[2].Attributes?.KeyFifths);
    }

    [Fact]
    public void TheConductorTrackReturnsToTheScoreMeter()
    {
        var midi = new MidiExporter().Export(Tree());
        var sigs = midi.Tracks.SelectMany(t => t.TimeSignatures)
            .OrderBy(t => t.Tick).Select(t => (t.Numerator, t.Denominator)).ToList();
        Assert.Equal(new[] { (4, 4), (3, 4), (4, 4) }, sigs);
    }

    /// <summary>
    /// A section that DOES state its own meter keeps it — the revert is the else-arm, not
    /// a blanket reset, and the same registry answers both halves.
    /// </summary>
    [Fact]
    public void ASectionThatStatesItsOwnMeterKeepsIt()
    {
        var tree = SyntaxTree.Parse("""
            time 4/4
            part m
            section A { m { c'4 d e f | } }
            section B { time 3/4 m { c'4 d e | } }
            form main { ~A ~B }
            score main { staff m }
            """);
        var ly = new LilyPondExporter().Export(tree);
        Assert.Contains("\\time 3/4", ly);
        var midi = new MidiExporter().Export(tree);
        Assert.Contains(midi.Tracks.SelectMany(t => t.TimeSignatures),
            ts => ts.Numerator == 3 && ts.Denominator == 4);
    }

    /// <summary>
    /// A <c>key</c> written in the MIDDLE of a part-major section's inline music is not
    /// that section's HEADER key: it belongs to the bar it stands in.
    /// </summary>
    /// <remarks>
    /// <c>MusicXmlExporter.EmitSection</c> scanned the section's direct children for a
    /// header key, and an inline-music section's mid-music <c>key</c> IS a direct child —
    /// so the export claimed G major from bar 1 while the page turns it on at bar 2. The
    /// section-major twin below is the control: the two spellings of one book must agree.
    /// </remarks>
    [Fact]
    public void AMidMusicKeyIsNotTheSectionsHeaderKey()
    {
        static int[] Fifths(string lys) => new MusicXmlExporter().Export(SyntaxTree.Parse(lys))
            .Parts.SelectMany(p => p.Measures)
            .Select(m => m.Attributes?.KeyFifths ?? -99).ToArray();

        var partMajor = Fifths("""
            time 4/4
            part m {
              section A { c'4 d e f | key g major g a b c | }
              section B { c'4 d e f | }
            }
            form main { ~A ~B }
            score main { staff m }
            """);
        var sectionMajor = Fifths("""
            time 4/4
            part m
            section A { m { c'4 d e f | key g major g a b c | } }
            section B { m { c'4 d e f | } }
            form main { ~A ~B }
            score main { staff m }
            """);

        Assert.Equal(new[] { 0, 1, 0 }, partMajor);
        Assert.Equal(partMajor, sectionMajor);
    }
}
