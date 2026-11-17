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
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Bars with a bar line left out (LYS2017: `e'1 e'`, every note kept, in bars of the meter — owner's
/// decision 2026-10-14) are as many bars in the sound's bar lines as the page draws: the metronome
/// accents each, and a recording lined up bar by bar (timeWarp, which counts the accents) maps each
/// to one of its bars. Owner's report 2026-11-17 (My Life, a bass line typed by hand): with the
/// two bars one in the bar lines, the metronome doubled its tempo there — eight beats laid over
/// one bar of the recording.
/// </summary>
[Trait("Category", "Unit")]
public class MissingBarLineBeatsTests
{
    private static string Book(string bass) => $$"""
        time 4/4
        part bass { instrument bass }
        section D { chords harmony { E7 | E7 | A7 | A | } bass { {{bass}} } }
        section C { chords harmony { D | D | } }
        form { D C }
        score { chords harmony  tab bass }
        """;

    [Theory]
    [InlineData("e1 | e1 | e'1 | e'1 |")]
    [InlineData("e1 | e1 | e'1 e' |")]
    [InlineData("e1 | e1 | e'1 e'")]
    public void TwoBarsWithTheirBarLineLeftOut_AreTwoBars_InTheBeats(string bass)
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(Book(bass)));
        int bar = 4 * midi.TicksPerQuarterNote;
        var accents = MetronomeBeats.Of(midi, midi.TicksPerQuarterNote, out _).Where(b => b.Accent).Select(b => b.Tick).ToArray();
        Assert.True(Enumerable.Range(0, 6).Select(i => i * bar).SequenceEqual(accents),
            "accents " + string.Join(",", accents) + " | bar ticks " + string.Join(",", midi.BarTicks)
            + " | section starts " + string.Join(",", midi.SectionStarts.Select(s => s.Key + ":" + s.Value)));
    }
}
