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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The defects LilySharp-Omr reported from its generated songs (its
/// docs/repro/lilysharp-feedback-2026-10-04.md, #19 and #20): a hidden voice staff's rests
/// and lyrics row, and the second voice of a bar after a change to a longer meter.
/// </summary>
[Trait("Category", "Unit")]
public class OmrFeedbackRegressionTests
{
    private static (MultiStaffScore Score, ScoreLayout Layout) Build(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return (score, new LayoutEngine().Layout(score));
    }

    private static string Song(string voice, string lyrics) => $$"""
        octave absolute
        time 4/4
        part vo { clef treble }
        part rh { clef treble }
        part lh { clef bass }
        section A {
          vo { {{voice}} }
          {{lyrics}}
          rh { c''1 | c''1 | c''1 | c''1 | }
          lh { c1 | c1 | c1 | c1 | }
        }
        form main { ~A }
        score main {
          staff vo "Voice" as removeEmpty all
          {{(lyrics.Length > 0 ? "lyrics vowords" : "")}}
          grandStaff {
            staff rh "Piano"
            staff lh ""
          }
        }
        """;

    /// <summary>
    /// #19: a voice resting through a system its staff is hidden on draws no rest at all —
    /// the hidden staff's Y is collapsed onto the next survivor's, and the multi-measure rest
    /// used to be drawn there, over the piano's right hand.
    /// </summary>
    [Fact]
    public void AHiddenStaffsRests_AreNotDrawnOnTheStaffBelow()
    {
        var (_, layout) = Build(Song("c'1 | c'1 | break R1 | R1 |", ""));

        var second = layout.AllSystems[1];
        Assert.True(second.StaffGroups[0].Staves[0].IsHidden);
        Assert.DoesNotContain(layout.MultiMeasureRestLayouts, r => r.StartMeasureIndex >= 2);
        // The control: the same rests on a system that shows the staff are drawn.
        var (_, shown) = Build(Song("R1 | R1 | break c'1 | c'1 |", "").Replace(" as removeEmpty all", ""));
        Assert.Contains(shown.MultiMeasureRestLayouts, r => r.StartMeasureIndex < 2);
    }

    /// <summary>
    /// #19: a lyrics row under a voice that hara-kiri hid on the FIRST system still hangs
    /// from the voice on the systems that show it — not under the piano's left hand, where
    /// the row joined the below-the-last-staff family because only system 0 was asked.
    /// </summary>
    [Fact]
    public void ALyricsRow_UnderAVoiceHiddenOnTheFirstSystem_HangsFromTheVoice()
    {
        var (_, layout) = Build(Song(
            "R1 | R1 | break c'4 d'4 e'4 f'4 | g'1 |",
            "lyrics vowords sings vo { | | la la la la | la | }"));

        var second = layout.AllSystems[1];
        var staves = second.StaffGroups.SelectMany(g => g.Staves).Where(s => !s.IsHidden)
            .OrderByDescending(s => s.Y).ToList();
        // The voice on top, then the piano's two staves.
        var voice = staves.First();
        var lowest = staves.Last();
        var bars = second.Measures.Select(m => m.MeasureIndex).ToHashSet();
        var words = layout.LyricLayouts.Where(l => bars.Contains(l.Item.MeasureIndex)).ToList();
        Assert.NotEmpty(words);
        foreach (var w in words)
        {
            Assert.True(w.YUp < voice.Y, $"syllable at {w.YUp:F3} is not below the voice's top line {voice.Y:F3}");
            Assert.True(w.YUp > lowest.Y, $"syllable at {w.YUp:F3} is under the piano's lowest staff {lowest.Y:F3}");
        }
    }

    /// <summary>
    /// #20: after a change to a longer meter, the second voice of a <c>voice { } { }</c> bar
    /// measures its bar in the meter in force, not the opening's — 4/4 then 12/8 cut it at a
    /// whole note, dropped its fourth dotted quarter, and left the upper voice's notes past
    /// the old bar length unforced (stems down).
    /// </summary>
    [Fact]
    public void ASecondVoice_AfterAMidPieceMeterChange_KeepsItsWholeBar()
    {
        var (score, _) = Build("""
            octave absolute
            time 4/4
            part rh { clef treble }
            section Piece {
              rh {
                voice { c''8 d''8 e''8 f''8 g''8 a''8 b''8 a''8 } { c'2 c'2 } |
                time 12/8 voice { c''8 d''8 e''8 f''8 g''8 a''8 b''8 a''8 g''8 f''8 e''8 d''8 } { c'4. c'4. c'4. c'4. } |
              }
            }
            form main { ~Piece }
            score main { staff rh }
            """);

        var staff = score.EnumerateStaves().Single().Staff;
        var upper = staff.Voices[0].Measures[1].Items.OfType<NoteItem>().ToList();
        var lower = staff.Voices[1].Measures[1].Items.OfType<NoteItem>().ToList();
        Assert.Equal(12, upper.Count);
        Assert.Equal(4, lower.Count);
        Assert.All(upper, n => Assert.True(n.StemUp));
        Assert.All(lower, n => Assert.False(n.StemUp));
    }
}
