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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// '@stemUp' / '@stemDown' force a note's stem direction, overriding the automatic
/// (staff-position) default. They feed NoteItem.ForcedStemUp — the writer's ask, a
/// separate slot from the beam-resolved StemUpOverride — and the voice-span default
/// (v1 up, v2 down) must NOT overwrite them: LilyPond voicifies only the \\
/// sub-lists, and an explicit \stemDown inside one is a later property set, so the
/// writer's direction survives either way.
/// </summary>
[Trait("Category", "Unit")]
public class StemDirectionAnnotationTests
{
    private static List<NoteItem> Notes(string body)
    {
        var src =
            "part m { clef treble }\n" +
            $"section S {{ m {{ {body} }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return score.PrimaryContentStaff.PrimaryVoice.Measures[0].Items
            .OfType<NoteItem>().ToList();
    }

    [Fact]
    public void StemUpDown_ForcesStemDirection()
    {
        // c'' (high) auto-stems DOWN; force each way and check it sticks.
        var notes = Notes("c''4@stemUp c''4@stemDown c''4");
        Assert.Equal(3, notes.Count);
        Assert.True(notes[0].StemUp, "@stemUp should force the stem up");
        Assert.False(notes[1].StemUp, "@stemDown should force the stem down");
        // The third (plain) note keeps the automatic direction.
        Assert.Equal(notes[2].StaffPosition < 0, notes[2].StemUp);
    }

    [Fact]
    public void NoAnnotation_KeepsAutomaticDirection()
    {
        // No annotation -> StemUpOverride is null -> automatic from staff position.
        var notes = Notes("c''4 c4");
        Assert.All(notes, n => Assert.Equal(n.StaffPosition < 0, n.StemUp));
    }

    [Fact]
    public void VoiceDefault_DoesNotOverwriteTheWriterAsk()
    {
        // A voice { } span forces v1 stems UP — but the writer's per-note ask
        // survives it (dots.ly walks this: its \stemDown chord shares the measure
        // with a << \\ >> that opens on the half-bar, and the measure-granular span
        // would otherwise flip the chord's stem).
        // LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist — only
        //   the \\ sub-lists receive the voice props.
        var items = Items("voice { c4@stemDown <c e g>4@stemDown c4 } { c,4 c,4 c,4 }");
        var note = Assert.IsType<NoteItem>(items[0]);
        var chord = Assert.IsType<ChordItem>(items[1]);
        var plain = Assert.IsType<NoteItem>(items[2]);
        Assert.False(note.StemUp, "the writer's @stemDown outranks the voice-1 UP default");
        Assert.False(chord.StemUp, "a chord's @stemDown outranks the voice-1 UP default");
        Assert.True(plain.StemUp, "an unmarked note takes the voice default");
    }

    private static List<MusicItem> Items(string body)
    {
        var src =
            "part m { clef treble }\n" +
            $"section S {{ m {{ {body} }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return score.PrimaryContentStaff.PrimaryVoice.Measures[0].Items.ToList();
    }
}
