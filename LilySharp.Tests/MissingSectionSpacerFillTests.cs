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
using LilySharp.Core.Semantics;
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

    // By-part: the dogfood repro — melody defines only A; bass defines A and B.
    private const string GroupedByPartSource = """
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
        form { A B A }
        score s {
          staff melody
          staff bass
        }
        """;

    [Fact]
    public void GroupedByPart_MissingSection_KeepsStavesAligned()
    {
        var score = Collect(GroupedByPartSource);
        var melody = VoiceOf(score, 0);
        var bass = VoiceOf(score, 1);

        // A(1) + B(2) + A(1) = 4 bars on BOTH staves (previously melody was 2).
        Assert.Equal(4, bass.Measures.Length);
        Assert.Equal(melody.Measures.Length, bass.Measures.Length);
    }

    [Fact]
    public void GroupedByPart_MissingSection_FilledWithSpacersNotNotes()
    {
        var score = Collect(GroupedByPartSource);
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
    public void GroupedByPart_ShortSection_PaddedToCanonicalLength()
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
            form { A B A }
            score s {
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
    public void GroupedBySection_MissingSection_KeepsStavesAligned()
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
            form { A B A }
            score s {
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
            form { A B A }
            score s { staff melody staff bass }
            """);

        var melody = VoiceOf(score, 0);
        Assert.Equal(3, melody.Measures.Length);
        Assert.DoesNotContain(melody.Measures.SelectMany(m => m.Items), i => i is RestItem { IsSpacer: true });
    }

    // HANDOFF §2 F-hdrsilent (session 844): a section that OPENS with its own `time` /
    // `key` puts that change item in the bar before any music, so the old "no items yet"
    // test read the bar as dirty and a part that does not write the section was not
    // padded at all — the page drew 4 bars of 6 and the later sections slid left.
    [Theory]
    [InlineData("time 3/4", "2.")]
    [InlineData("key g major", "1")]
    public void SectionHeader_MissingSection_StillFilled(string header, string bar)
    {
        var score = Collect($$"""
            octave absolute
            time 4/4
            part melody { clef treble }
            part bass { clef bass }
            section A {
              melody { c'1 | }
              bass { c1 | }
            }
            section B {
              {{header}}
              bass { g{{bar}} | g{{bar}} | }
            }
            section C {
              {{header}}
              melody { c'{{bar}} | }
              bass { g{{bar}} | }
            }
            form { A B C }
            score s { staff melody staff bass }
            """);

        var melody = VoiceOf(score, 0);
        var bass = VoiceOf(score, 1);
        // A(1) + B(2) + C(1) = 4 bars on BOTH staves (the melody had 2).
        Assert.Equal(4, bass.Measures.Length);
        Assert.Equal(4, melody.Measures.Length);
        foreach (var idx in new[] { 1, 2 })
        {
            Assert.Contains(melody.Measures[idx].Items, i => i is RestItem { IsSpacer: true });
            Assert.DoesNotContain(melody.Measures[idx].Items, i => i is NoteItem);
        }
        // C's note is in C's bar, not in B's.
        Assert.Contains(melody.Measures[3].Items, i => i is NoteItem);
    }

    // The filler bar is worth what an empty bar is worth THERE — the header's meter, and
    // its pickup first — not the score's 4/4 (a whole-note spacer stood in a 3/4 bar).
    [Fact]
    public void SectionHeader_FillerBarsTakeTheHeadersMeterAndPickup()
    {
        var score = Collect("""
            octave absolute
            time 4/4
            part melody {
              clef treble
              section A { c'1 | }
              section C { c'1 | }
            }
            part bass {
              clef bass
              section A { c1 | }
              section B { g4 | g2. | g2. | }
              section C { c1 | }
            }
            section B { time 3/4  partial 4 }
            form { A B C }
            score s { staff melody staff bass }
            """);

        var melody = VoiceOf(score, 0);
        Assert.Equal(VoiceOf(score, 1).Measures.Length, melody.Measures.Length);
        Assert.Equal(5, melody.Measures.Length);
        Fraction SpacerLength(int idx) => melody.Measures[idx].Items
            .OfType<RestItem>().Single(r => r.IsSpacer).Duration;
        Assert.Equal(Fraction.Quarter, SpacerLength(1));
        Assert.Equal(new Fraction(3, 4), SpacerLength(2));
        Assert.Equal(new Fraction(3, 4), SpacerLength(3));
        Assert.Contains(melody.Measures[4].Items, i => i is NoteItem);
    }
}
