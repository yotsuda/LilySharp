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
/// When the structure references a section that a part does NOT define (e.g. a
/// melody that only writes A while the bass writes A and B), that part is filled with
/// full-measure spacer rests for the missing section so every staff stays bar-aligned
/// — instead of the section collapsing to zero bars on that staff and dragging the
/// rest of the score out of alignment.
/// </summary>
[Trait("Category", "Unit")]
public class MissingSectionSpacerFillTests
{
    private static MultiStaffScore Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

    private static Voice VoiceOf(MultiStaffScore score, int staffIndex)
        => score.StaffGroups.SelectMany(g => g.Staves).ElementAt(staffIndex).Voices[0];

    // Part-major: the dogfood repro — melody defines only A; bass defines A and B.
    private const string PartMajorSource = """
        time 4/4
        part melody {
          clef treble
          section A { c4 d e f | }
        }
        part bass {
          clef bass
          section A { c2 g, | }
          section B { c4 c c c | g2 g2 | }
        }
        form main { A B A }
        score main "s" {
          staff melody
          staff bass
        }
        """;

    [Fact]
    public void PartMajor_MissingSection_KeepsStavesAligned()
    {
        var score = Collect(PartMajorSource);
        var melody = VoiceOf(score, 0);
        var bass = VoiceOf(score, 1);

        // A(1) + B(2) + A(1) = 4 bars on BOTH staves (previously melody was 2).
        Assert.Equal(4, bass.Measures.Length);
        Assert.Equal(melody.Measures.Length, bass.Measures.Length);
    }

    [Fact]
    public void PartMajor_MissingSection_FilledWithSpacersNotNotes()
    {
        var score = Collect(PartMajorSource);
        var melody = VoiceOf(score, 0);

        // Measures 1 and 2 are section B for the melody: invisible spacer rests, no notes.
        foreach (var idx in new[] { 1, 2 })
        {
            var items = melody.Measures[idx].Items;
            Assert.Contains(items, i => i is RestItem { IsSpacer: true });
            Assert.DoesNotContain(items, i => i is NoteItem);
        }

        // The bars flanking B keep the real melody.
        Assert.Contains(melody.Measures[0].Items, i => i is NoteItem);
        Assert.Contains(melody.Measures[3].Items, i => i is NoteItem);
    }

    // A part that DEFINES the section but with too FEW bars (the follow-up repro:
    // melody's B is a single `g1` while bass's B is two bars) is padded up to the
    // canonical length, not left short.
    [Fact]
    public void PartMajor_ShortSection_PaddedToCanonicalLength()
    {
        var score = Collect("""
            time 4/4
            part melody {
              clef treble
              section A { c4 d e f | }
              section B { g1 }
            }
            part bass {
              clef bass
              section A { c2 g, | }
              section B { c4 c c c | g2 g2 | }
            }
            form main { A B A }
            score main "s" {
              staff melody
              staff bass
            }
            """);

        var melody = VoiceOf(score, 0);
        var bass = VoiceOf(score, 1);

        // Both staves span A(1) + B(2) + A(1) = 4 bars.
        Assert.Equal(4, bass.Measures.Length);
        Assert.Equal(4, melody.Measures.Length);

        // Melody B: bar 1 is the real g1 note; bar 2 is the padded spacer.
        Assert.Contains(melody.Measures[1].Items, i => i is NoteItem);
        Assert.Contains(melody.Measures[2].Items, i => i is RestItem { IsSpacer: true });
        Assert.DoesNotContain(melody.Measures[2].Items, i => i is NoteItem);
    }

    [Fact]
    public void SectionMajor_MissingSection_KeepsStavesAligned()
    {
        var score = Collect("""
            time 4/4
            part melody { clef treble }
            part bass { clef bass }
            section A {
              melody { c4 d e f | }
              bass { c2 g, | }
            }
            section B {
              bass { c4 c c c | g2 g2 | }
            }
            form main { A B A }
            score main "s" {
              staff melody
              staff bass
            }
            """);

        var melody = VoiceOf(score, 0);
        var bass = VoiceOf(score, 1);

        Assert.Equal(4, bass.Measures.Length);
        Assert.Equal(4, melody.Measures.Length);
        // Section B (measures 1-2) is spacer-filled on the melody staff.
        Assert.Contains(melody.Measures[1].Items, i => i is RestItem { IsSpacer: true });
        Assert.Contains(melody.Measures[2].Items, i => i is RestItem { IsSpacer: true });
    }

    [Fact]
    public void AllPartsDefineAllSections_NoSpacersAdded()
    {
        // Guard: when nothing is missing, the fill path must not fire.
        var score = Collect("""
            time 4/4
            part melody { clef treble
              section A { c4 d e f | }
              section B { g4 g g g | }
            }
            part bass { clef bass
              section A { c2 g, | }
              section B { g2 g2 | }
            }
            form main { A B A }
            score main "s" { staff melody staff bass }
            """);

        var melody = VoiceOf(score, 0);
        Assert.Equal(3, melody.Measures.Length);
        Assert.DoesNotContain(melody.Measures.SelectMany(m => m.Items), i => i is RestItem { IsSpacer: true });
    }
}
