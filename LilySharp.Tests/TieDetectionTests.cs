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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tie binds only to the IMMEDIATELY following timed item, exactly like
/// LilyPond's Tie_engraver (lily/tie-engraver.cc): the next note of the same
/// pitch, or the matching pitch of the next chord. A rest or a different pitch
/// at the next moment means NO tie — the detector must never scan past
/// intervening notes looking for a matching pitch (that used to render a
/// spurious long tie arc over unrelated notes).
/// </summary>
[Trait("Category", "Unit")]
public class TieDetectionTests
{
    private static ImmutableArray<TieItem> Ties(string music)
    {
        string src = $$"""
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody { {{music}} } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src), "melody");
        return new TieDetector().DetectTies(score);
    }

    [Fact]
    public void Tie_ToTheImmediatelyFollowingSamePitch_IsDetected()
    {
        var ties = Ties("c4~ c4 d4 e4 |");
        var tie = Assert.Single(ties);
        Assert.Equal(0, tie.StartMeasureIndex);
        Assert.Equal(0, tie.EndMeasureIndex);
    }

    [Fact]
    public void Tie_AcrossTheBarline_IsDetected()
    {
        var ties = Ties("c4 d4 e4 c4~ | c4 d4 e4 f4 |");
        var tie = Assert.Single(ties);
        Assert.Equal(0, tie.StartMeasureIndex);
        Assert.Equal(1, tie.EndMeasureIndex);
    }

    [Fact]
    public void Tie_DoesNotScanPastADifferentPitch()
    {
        // LP: unterminated tie warning, no tie — never a long arc to the c
        // three items later.
        Assert.Empty(Ties("c4~ d4 e4 c4 |"));
    }

    [Fact]
    public void Tie_DoesNotCrossARest()
    {
        Assert.Empty(Ties("c4~ r4 c4 d4 |"));
    }

    /// <summary>A tie joins heads of the same PITCH, not the same staff position: the e of
    /// <c>&lt;c e&gt;2~</c> does not tie into the ees that stands on its line, so the chord
    /// pair carries one tie (the c's), as LilyPond makes one Tie grob (Lab sessions/p452,
    /// book TMB). Until session 797 the position was compared first and the e tied into the
    /// ees too (HANDOFF §2 E). The controls: the same chord twice ties both heads; a single
    /// head of another alteration (<c>cis~ c</c>) or spelling (<c>fis~ ges</c>) ties nothing.</summary>
    [Fact]
    public void Tie_JoinsTheSamePitch_NotTheSameStaffPosition()
    {
        var tie = Assert.Single(Ties("<c e>2~ <c ees>2 |"));
        Assert.Equal(tie.StartNote.StaffPosition, tie.EndNote.StaffPosition);
        Assert.Equal(tie.StartNote.Midi, tie.EndNote.Midi);
        Assert.Equal(2, Ties("<c e>2~ <c e>2 |").Length);
        Assert.Empty(Ties("cis4~ c4 d4 e4 |"));
        Assert.Empty(Ties("fis4~ ges4 e4 f4 |"));
        var single = Assert.Single(Ties("e4~ e4 d4 c4 |"));
        Assert.Equal(single.StartNote.Midi, single.EndNote.Midi);
    }

    [Fact]
    public void Tie_NoteIntoChord_TiesTheMatchingPitch()
    {
        // LP ties c into <c e> (matching pitch of the immediately following
        // chord).
        var ties = Ties("c4~ <c e>4 d4 e4 |");
        var tie = Assert.Single(ties);
        Assert.Equal(tie.StartNote.StaffPosition, tie.EndNote.StaffPosition);
    }

    [Fact]
    public void ChordTie_DoesNotCrossARest()
    {
        Assert.Empty(Ties("<c e>4~ r4 <c e>4 d4 |"));
    }

    [Fact]
    public void ChordTie_ToTheImmediatelyFollowingChord_TiesMatchingPitches()
    {
        var ties = Ties("<c e>4~ <c e>4 d4 e4 |");
        Assert.Equal(2, ties.Length);
        Assert.All(ties, t => Assert.Equal(0, t.EndMeasureIndex));
    }
}
