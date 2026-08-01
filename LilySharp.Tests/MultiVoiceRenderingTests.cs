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

using System.Collections.Immutable;
using System.Linq;
using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Tests;

/// <summary>
/// Tests for multi-voice rendering functionality.
/// </summary>
[Trait("Category", "Integration")]
public class MultiVoiceRenderingTests
{
    [Fact]
    public void Score_SingleVoice_BackwardCompatible()
    {
        var note = new NoteItem(4, Fraction.Quarter, 0, null, false, 0);
        var measure = new Measure(
            ImmutableArray.Create<MusicItem>(note),
            BarlineType.None, BarlineType.Single, null, 0, 10);
        var voice = new Voice("test", ImmutableArray.Create(measure));

        var score = new Score(voice, new TimeSignature(4, 4), new KeySignature(0), "treble");

        // Should have one voice
        Assert.Single(score.Voices);
        Assert.Same(voice, score.Voice);
        Assert.False(score.IsMultiVoice);
    }

    [Fact]
    public void Score_MultipleVoices_Supported()
    {
        var note1 = new NoteItem(8, Fraction.Quarter, 0, null, false, 0);  // High note
        var note2 = new NoteItem(0, Fraction.Quarter, 0, null, false, 10); // Low note

        var measure1 = new Measure(
            ImmutableArray.Create<MusicItem>(note1),
            BarlineType.None, BarlineType.Single, null, 0, 5);
        var measure2 = new Measure(
            ImmutableArray.Create<MusicItem>(note2),
            BarlineType.None, BarlineType.Single, null, 0, 5);

        var voice1 = new Voice("upper", ImmutableArray.Create(measure1));
        var voice2 = new Voice("lower", ImmutableArray.Create(measure2));

        var score = new Score(
            ImmutableArray.Create(voice1, voice2),
            new TimeSignature(4, 4),
            new KeySignature(0),
            "treble");

        Assert.Equal(2, score.Voices.Length);
        Assert.Same(voice1, score.Voice);  // First voice is primary
        Assert.True(score.IsMultiVoice);
    }

    [Fact]
    public void VoiceCollector_MultiVoice_AppliesStemDirections()
    {
        var note1 = new NoteItem(8, Fraction.Quarter, 0, null, false, 0);
        var note2 = new NoteItem(0, Fraction.Quarter, 0, null, false, 10);

        var measure1 = new Measure(
            ImmutableArray.Create<MusicItem>(note1),
            BarlineType.None, BarlineType.Single, null, 0, 5);
        var measure2 = new Measure(
            ImmutableArray.Create<MusicItem>(note2),
            BarlineType.None, BarlineType.Single, null, 0, 5);

        var voice1 = new Voice("upper", ImmutableArray.Create(measure1));
        var voice2 = new Voice("lower", ImmutableArray.Create(measure2));

        var collector = new VoiceCollector();
        var columns = collector.Collect(ImmutableArray.Create(voice1, voice2));

        Assert.Single(columns);
        Assert.Equal(2, columns[0].Entries.Length);

        // Voice 1 stems up, Voice 2 stems down
        Assert.True(columns[0].Entries[0].ForcedStemUp);
        Assert.False(columns[0].Entries[1].ForcedStemUp);
    }

    // ---- The relative-octave frame of a voice span --------------------------
    //
    // A voice span is SIMULTANEOUS music, so its branches do not chain into one another
    // and none of them moves the frame: every branch reads from the frame the span opened
    // in, and so does the music after it. That is the rule Lily# already applies inside a
    // chord (every member stacks on the root, so `<c e g>` == `<c g e>` — CreateChordItem),
    // and it is what makes a voice span order-independent in PITCH. ⚠️ Not in STEMS: voice 1
    // is forced up and voice 2 down wherever they sound together, so swapping the branches
    // still changes the page.
    //
    // Before 2026-08-01 none of the three walkers agreed: the page restarted voices 2..N at
    // the PART's default octave and let voice 1 leak its last pitch out of the span, the MIDI
    // kept the running frame throughout, and the .ly twin chained the branches like chord
    // members. `g4 g4 g4 g4 | voice { c'1 } voice { d1 }` put that d at D4, D3 and D5.

    /// <summary>Staff positions of one voice track (0 = the primary stream).</summary>
    private static ImmutableArray<int> StaffPositions(string music, int voiceIndex)
        => VoiceTracks(music)[voiceIndex];

    /// <summary>Staff positions of every voice, flattened — for order-independence, where
    /// which branch a note landed in is exactly what must not matter.</summary>
    private static ImmutableArray<int> StaffPositions(string music)
        => VoiceTracks(music).SelectMany(v => v).ToImmutableArray();

    private static List<ImmutableArray<int>> VoiceTracks(string music)
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse($$"""
            time 4/4
            key c major
            part m { clef treble }
            section S { m { {{music}} } }
            form main { S }
            score main { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return score.StaffGroups
            .SelectMany(g => g.Staves)
            .SelectMany(s => s.Voices)
            .Select(v => v.Measures
                .SelectMany(m => m.Items)
                .OfType<NoteItem>()
                .Select(n => n.StaffPosition)
                .ToImmutableArray())
            .ToList();
    }

    [Fact]
    public void AVoiceSpan_ReadsEveryBranchFromTheFrameItOpenedIn_SoTheBranchesAreOrderIndependent()
    {
        // The same two branches written the other way round sound the same notes: neither
        // branch is read against the other's last pitch.
        var ab = StaffPositions("g'4 g g g | voice { c1 } voice { e1 } |");
        var ba = StaffPositions("g'4 g g g | voice { e1 } voice { c1 } |");
        Assert.Equal(ab.Sort(), ba.Sort());
    }

    [Fact]
    public void AVoiceSpan_DoesNotMoveTheFrame_SoTheMusicAfterItIsUnaffected()
    {
        // The note after the span is relative to the note BEFORE it, whatever the branches
        // did in between: DELETING the span does not move the music that follows it.
        // (A plain `c1` in its place WOULD move it — that note is sequential music and
        // advances the frame like any other. Only the span is transparent.)
        var withSpan = StaffPositions("g'4 g g g | voice { c1 } voice { e1 } | a4 a a a |", 0);
        var withoutSpan = StaffPositions("g'4 g g g | a4 a a a |", 0);

        Assert.Equal(withoutSpan.TakeLast(4), withSpan.TakeLast(4));
    }

    [Fact]
    public void NoteCollision_NoOverlap_NoShift()
    {
        var collision = new NoteCollision();

        // Notes far apart (staff positions 0 and 8)
        var upPositions = new[] { 8 };
        var downPositions = new[] { 0 };

        var result = collision.AnalyzeCollision(upPositions, downPositions, 4, 4, 0, 0);

        Assert.Equal(CollisionType.None, result.Type);
        Assert.Equal(0, result.UpStemXOffset);
        Assert.Equal(0, result.DownStemXOffset);
    }

    [Fact]
    public void NoteCollision_Adjacent_Shifts()
    {
        var collision = new NoteCollision();

        // Adjacent notes (staff positions 4 and 5)
        var upPositions = new[] { 5 };
        var downPositions = new[] { 4 };

        var result = collision.AnalyzeCollision(upPositions, downPositions, 4, 4, 0, 0);

        // Should detect close collision and shift
        Assert.NotEqual(CollisionType.None, result.Type);
    }
}
