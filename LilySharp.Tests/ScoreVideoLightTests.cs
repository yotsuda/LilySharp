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
using LilySharp.Core.Export;
using LilySharp.Core.Midi;
using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What a score video lights (owner's report 2026-11-16: a chart of chords alone made a video in
/// which nothing lit). A chord row's notes play under the chord name's offset, and the name is
/// lit by them as a head by its note — in every score, as the preview does (owner's decision
/// 2026-11-16).
/// </summary>
[Trait("Category", "Unit")]
public class ScoreVideoLightTests
{
    private static (int Lit, int Sounding, int ChordNames) Count(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        var boxes = BoxesGenerator.GeneratePages(tree, spec, null);
        var lit = ScoreVideo.HeadsByNote(tree, boxes);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        var keys = midi.Tracks.SelectMany(t => t.Notes).Where(n => n.SourcePos >= 0)
            .Select(n => (n.SourcePos, n.SourceOrdinal)).Distinct().ToList();
        int names = boxes.SelectMany(p => p.Symbols).Count(s => s.Kind == "chordName");
        return (keys.Count(lit.ContainsKey), keys.Count, names);
    }

    [Fact]
    public void AChartOfChordsAlone_LightsEachChordName()
    {
        var (lit, sounding, names) = Count("chords prog { section A { C | G7 | Am | F | } }\nform { A A }\nscore { chords prog }\n");
        Assert.True(names >= 8, $"{names} chord name(s) drawn");
        Assert.True(sounding >= 8, $"{sounding} chord(s) sounding");
        Assert.Equal(sounding, lit);
    }

    [Fact]
    public void ChordNamesOverAStaff_LightToo()
    {
        string source = "octave relative\ntime 4/4\npart m { clef treble }\n"
            + "section A { m { c'4 d e f | g1 | } chords { C | G7 | } }\nform { A }\n"
            + "score { chords  staff m }\n";
        var (lit, sounding, names) = Count(source);
        Assert.True(names >= 2, $"{names} chord name(s) drawn");
        Assert.Equal(sounding, lit);
    }
}
