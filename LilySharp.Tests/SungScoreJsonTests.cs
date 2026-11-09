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
using System.Text.Json;
using LilySharp.Core.Audio;
using LilySharp.Core.Midi;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The score <c>lysc wav --singer</c> hands the engine (<see cref="SungScoreJson"/>): the engine
/// refuses notes that overlap, and 16ths at 132 are repeating decimals — rounded apart, a start and
/// a duration put the next note a millionth before this one's end (owner's report 2026-11-08,
/// 雪やこんこ: "note 23 at 6.591 s: starts before note 22 ends"). Rounded at the ends, touching
/// notes stay touching.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SungScoreJsonTests
{
    [Fact]
    public void TouchingNotes_StayTouching_AfterRounding()
    {
        // 雪やこんこ's melody at 132: dotted eighths and sixteenths, every note ending where the next starts.
        var tree = SyntaxTree.Parse("""
            tempo 4 = 132
            time 2/4
            key f major
            part melody { clef treble  instrument soprano }
            section A {
              melody { c''8. d16 c8. d16 | c4 a8 r | a8. bes16 a8. bes16 | a4 f8 r | a4 f8. g16 | a4 f8. g16 | a8 bes c c | g a g r | }
              lyrics verse { ゆ ~ き や | こん こ | あ ら れ や | こん こ | ふっ て も | ふっ て も | ま だ ふ り | や ま ぬ | }
            }
            form { A }
            score { staff melody  lyrics verse sings melody }
            """);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var score = RenderSpecParser.FindAll(tree).First();
        var midi = new MidiExporter { Form = score.Form, Score = score }.Export(tree);
        var voice = Assert.Single(SungVoices.Read(midi, MidiClock.SecondsOf(midi)));

        using var json = JsonDocument.Parse(SungScoreJson.Write(voice, 44100));
        var notes = json.RootElement.GetProperty("notes").EnumerateArray()
            .Select(n => (Start: n.GetProperty("start").GetDouble(), Duration: n.GetProperty("duration").GetDouble())).ToList();
        Assert.True(notes.Count > 20);
        for (int i = 1; i < notes.Count; i++)
        {
            double prevEnd = notes[i - 1].Start + notes[i - 1].Duration;
            // Cantoria's own rule: a note may not start before the one before it ends (1e-9).
            Assert.True(notes[i].Start >= prevEnd - 1e-9,
                $"note {i} at {notes[i].Start} starts before note {i - 1} ends ({prevEnd})");
        }
    }
}
