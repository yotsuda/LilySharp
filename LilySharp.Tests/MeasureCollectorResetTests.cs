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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A MeasureCollector may be reused for more than one Collect call (Reset runs at
/// the start of each). Reset must clear per-run state so a later call does not carry
/// the previous one's data — PitchTrace in particular used to accumulate unbounded.
/// </summary>
[Trait("Category", "Unit")]
public class MeasureCollectorResetTests
{
    [Fact]
    public void Reuse_PitchTraceReflectsOnlyLatestCollect()
    {
        var collector = new MeasureCollector();

        collector.Collect(SyntaxTree.Parse("c4 d e f"));   // 4 pitches
        Assert.Equal(4, collector.PitchTrace.Count);

        collector.Collect(SyntaxTree.Parse("g4 a"));       // 2 pitches
        // Without Reset clearing _pitchTrace this would be 6 (accumulated).
        Assert.Equal(2, collector.PitchTrace.Count);
    }

    [Fact]
    public void StickyDuration_CarriesDots_ForNotesAndRests()
    {
        // An undurated note/rest takes the WHOLE previous duration — dots included:
        // `c8. c` and `r8. r` are each two dotted eighths. Until 2026-08-07 only the
        // value stuck and the inherited dots reset to 0 (the second r8. of
        // dot-rest-beam-trigger.ly lost its dot), while the semantic walk and the
        // MIDI/MusicXML exporters already carried the dots.
        // LILYPOND-REF: lily/parser.yy:3505-3514 optional_notemode_duration — default_duration_
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("time 12/16\nc8. c r8. r |"));
        var items = score.Voices[0].Measures[0].Items;
        var notes = items.OfType<NoteItem>().ToList();
        var rests = items.OfType<RestItem>().ToList();
        Assert.Equal(2, notes.Count);
        Assert.Equal(2, rests.Count);
        Assert.All(notes, n => Assert.Equal(1, n.Dots));
        Assert.All(rests, r => Assert.Equal(1, r.Dots));
    }

    [Fact]
    public void StickyDuration_AWrittenDurationDropsTheInheritedDots()
    {
        // Writing a NEW plain duration replaces the whole default: after `c4. c8 c4`
        // a bare `c` is an undotted quarter, not a dotted anything.
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("time 4/4\nc4. c8 c4 c |"));
        var notes = score.Voices[0].Measures[0].Items.OfType<NoteItem>().ToList();
        Assert.Equal(new[] { 1, 0, 0, 0 }, notes.Select(n => n.Dots).ToArray());
    }

    [Fact]
    public void StickyDuration_AGroupInheritsTheDots()
    {
        // `c4. << c e g >>` — an equal-subdivision group without a trailing `>>N`
        // spans the inherited DOTTED quarter, so its three members subdivide 3/8
        // into plain eighths. With the dots dropped it spanned 1/4 and the members
        // came out a third of that. Found by the self-audit, not by a corpus book.
        // LILYPOND-REF: lily/parser.yy:3505-3514 optional_notemode_duration — default_duration_
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("time 12/8\nc4. << c e g >> c c |"));
        var notes = score.Voices[0].Measures[0].Items.OfType<NoteItem>().ToList();
        Assert.Equal(6, notes.Count);
        Assert.Equal(LilySharp.Core.Semantics.Fraction.FromNoteValue(8),
            notes[1].BaseDuration);   // a group member
        Assert.Equal(1, notes[4].Dots); // the bare c AFTER the group keeps the dotted default
    }
}
