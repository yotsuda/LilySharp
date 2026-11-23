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
using System.Text.Json;
using LilySharp.Core.Audio;
using LilySharp.Core.Midi;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The pitch moves written on a sung note — <c>@scoop</c> <c>@plop</c> into its head, <c>@fall</c>
/// <c>@doit</c> <c>@glissando</c> <c>@slide</c> out of its end — sung by the voice as a bend
/// (Cantoria's request 2, 2026-11-22, a trial: <see cref="SungVoices.Bend"/>): points of seconds from
/// the note's start and cents, the score's <c>"bend"</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SungBendTests
{
    // At 120 a quarter is half a second.
    private static string Book(string music, string words = "ら ら ら ら |") => $$"""
        tempo 4 = 120
        time 4/4
        part melody { clef treble  instrument soprano }
        section A {
          melody { {{music}} }
          lyrics verse { {{words}} }
        }
        singer sings verse on melody
        form { A }
        score { staff melody  lyrics verse on melody }
        """;

    private static SungVoice Voice(string music, string words = "ら ら ら ら |")
    {
        var tree = SyntaxTree.Parse(Book(music, words));
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = RenderSpecParser.FindAll(tree).First();
        var midi = new MidiExporter { Form = score.Form, Score = score }.Export(tree);
        return Assert.Single(SungVoices.Read(midi, MidiClock.SecondsOf(midi)));
    }

    private static (double, double)[]? Points(SungNote note) => note.Bend?.Select(p => (p.At, p.Cents)).ToArray();

    [Fact]
    public void AScoop_RisesIntoTheHead_AndAPlop_FallsIntoIt()
    {
        var notes = Voice("c'4@scoop d@plop e f |").Notes;
        Assert.Equal(new[] { (0.0, -200.0), (0.15, 0.0) }, Points(notes[0]));
        Assert.Equal(new[] { (0.0, 200.0), (0.15, 0.0) }, Points(notes[1]));
        Assert.Null(notes[2].Bend);
        Assert.Null(notes[3].Bend);
    }

    [Fact]
    public void AScoopOnAShortNote_TakesAtMostAThirdOfIt()
        => Assert.Equal(new[] { (0.0, -200.0), (0.0375, 0.0) }, Points(Voice("c'16@scoop d e f g4 a b |", "ら ら ら ら ら ら ら |").Notes[0]));

    [Fact]
    public void AFall_DropsFourSemitones_OverTheNotesLastStretch_AndADoit_Rises()
    {
        var notes = Voice("c'2@fall d8@doit e8 f4 |").Notes;
        Assert.Equal(new[] { (0.65, 0.0), (1.0, -400.0) }, Points(notes[0]));     // a second: its last 0.35 s
        Assert.Equal(new[] { (0.15, 0.0), (0.25, 400.0) }, Points(notes[1]));     // an eighth: its last 40 %
    }

    [Fact]
    public void AGlissando_GoesToTheNextSungNotesPitch_OverTheSecondHalf()
    {
        var notes = Voice("c'4@glissando e@slide c a'@glissando |").Notes;
        Assert.Equal(new[] { (0.25, 0.0), (0.5, 400.0) }, Points(notes[0]));
        Assert.Equal(new[] { (0.25, 0.0), (0.5, -400.0) }, Points(notes[1]));
        Assert.Null(notes[3].Bend);         // the last note: no next note to go to
    }

    [Fact]
    public void AGlissandoBeforeARest_OrABreath_SlidesNowhere()
    {
        var notes = Voice("c'4@glissando r4 e4@glissando@breath g |", "ら ら ら |").Notes;
        Assert.All(notes, n => Assert.Null(n.Bend));
    }

    [Fact]
    public void AScoopAndAFallOnOneNote_AreBoth_AndAFallBeatsASlide()
    {
        var notes = Voice("c'2@scoop@fall d8@glissando@fall e8@fall@glissando f4 |").Notes;
        Assert.Equal(new[] { (0.0, -200.0), (0.15, 0.0), (0.65, 0.0), (1.0, -400.0) }, Points(notes[0]));
        Assert.Equal(new[] { (0.15, 0.0), (0.25, -400.0) }, Points(notes[1]));     // in either order
        Assert.Equal(new[] { (0.15, 0.0), (0.25, -400.0) }, Points(notes[2]));
    }

    [Fact]
    public void TheJson_CarriesTheBend_NoPointPastTheNote()
    {
        var voice = Voice("c'4@scoop d e f@fall |");
        using var json = JsonDocument.Parse(SungScoreJson.Write(voice, 48000));
        var notes = json.RootElement.GetProperty("notes").EnumerateArray().ToList();
        var first = notes[0].GetProperty("bend").EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList();
        Assert.Equal(new List<(double, double)> { (0, -200), (0.15, 0) }, first);
        var last = notes[3].GetProperty("bend").EnumerateArray().Select(p => p[0].GetDouble()).ToList();
        Assert.Equal(notes[3].GetProperty("duration").GetDouble(), last[^1]);
        Assert.False(notes[1].TryGetProperty("bend", out _));
    }

    [Fact]
    public void TheJsonsLastPoint_NeverStandsPastTheNote_AtAnyTempo()
    {
        // The score's duration is its rounded end less its rounded start, the bend's last point the
        // note's own length rounded: the two may differ by a millionth, and Cantoria refuses a point
        // past the note. Every tempo from 40 to 200, sixteenths and dotted notes, each with a fall.
        int checkedPoints = 0;
        for (int tempo = 40; tempo <= 200; tempo += 3)
        {
            var tree = SyntaxTree.Parse(Book("c'16@fall d8.@fall e16@fall f8@fall g16@fall a4@fall b8@fall |", "ら ら ら ら ら ら ら |")
                .Replace("tempo 4 = 120", $"tempo 4 = {tempo}"));
            var score = RenderSpecParser.FindAll(tree).First();
            var midi = new MidiExporter { Form = score.Form, Score = score }.Export(tree);
            var voice = Assert.Single(SungVoices.Read(midi, MidiClock.SecondsOf(midi)));
            using var json = JsonDocument.Parse(SungScoreJson.Write(voice, 48000));
            foreach (var note in json.RootElement.GetProperty("notes").EnumerateArray())
            {
                double duration = note.GetProperty("duration").GetDouble();
                foreach (var point in note.GetProperty("bend").EnumerateArray())
                {
                    Assert.True(point[0].GetDouble() <= duration, $"at {tempo}: a point at {point[0].GetDouble()} past a note of {duration}");
                    checkedPoints++;
                }
            }
        }
        Assert.Equal(54 * 7 * 2, checkedPoints);
    }
    [Fact]
    public void AnUnmarkedSong_HasNoBend()
        => Assert.All(Voice("c'4 d e f |").Notes, n => Assert.Null(n.Bend));
}
