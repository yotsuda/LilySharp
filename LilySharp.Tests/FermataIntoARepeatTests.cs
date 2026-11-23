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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A fermata rings past its bar (half again its value) — and that ring is not music written
/// in the bar it rings into. When that bar opens a repeat, its second pass starts with the
/// notes written there and nothing else.
/// </summary>
/// <remarks>
/// Until 2026-11-15 the played timeline took a note sounding across a stretch's start for a
/// note tied into it (`[1. d1~ :| [2. d1 ]`), so `B |: S :|` with `e''1@fermata` closing B played
/// the fermata's tail again at the head of S's second pass, and its word was sung again there
/// (owner's report: Lab sessions/p1226/md/eval3/theatre/theatre.lys line 162, "last").
/// </remarks>
public class FermataIntoARepeatTests
{
    private static MidiFile Play(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        return new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
    }

    private static string Book(string closing, string form = "form { B |: S :| }")
        => "octave absolute\ntime 4/4\npart v { instrument soprano  lyrics en }\n"
            + $"section B {{ v {{ c'2 d'2 | {closing} | }} lyrics en {{ Here at | last. | }} }}\n"
            + "section S { v { r1 | } }\n"
            + form + "\nscore { staff v  lyrics en }\n";

    [Theory]
    [InlineData("e'1@fermata")]
    [InlineData("<e' g'>1@fermata")]
    [InlineData("e'1")]
    public void AFermataClosingTheBarBeforeARepeat_IsPlayedAndSungOnce(string closing)
    {
        var midi = Play(Book(closing));
        Assert.Equal(1, midi.Tracks.Sum(t => t.Lyrics.Count(l => l.Text.StartsWith("last"))));
        // c' d' and the closing bar's notes, once: the rests of S's two passes sound nothing.
        int closingNotes = closing.StartsWith('<') ? 2 : 1;
        Assert.Equal(2 + closingNotes, midi.Tracks.Sum(t => t.Notes.Count));
        // …and the fermata holds its bar's TIME half again, its note as written (docs/expressive-tempo-design.md,
        // 2026-11-23; until then the note rang half again its value).
        var e = midi.Tracks.SelectMany(t => t.Notes).Single(n => n.Pitch == 64);
        Assert.Equal(midi.TicksPerQuarterNote * 4, e.DurationTicks);
        var tempos = midi.Tracks[0].TempoChanges;
        int under = tempos.Last(c => c.Tick <= e.StartTick).MicrosecondsPerBeat, before = tempos[0].MicrosecondsPerBeat;
        Assert.Equal(closing.Contains("fermata") ? before * 3 / 2 : before, under);
    }

    /// <summary>
    /// Within the piece a fermata's ring ends where the music goes on — the next note of any part
    /// — since the music after it is not held back (session 1363): the held note overran the next
    /// bar, struck its own pitch again while still sounding, and rang the chord over the next
    /// harmony. A note of another voice sounding across that point is no fermata's and keeps its
    /// length; a hold nothing follows still rings half again its value (above).
    /// </summary>
    [Theory]
    // (onset ticks at 480 a quarter : length)
    [InlineData("part a { clef treble }\nsection A { a { g''1@fermata | g''1 | } }", "a", "0:1920 1920:1920")]
    [InlineData("part a { clef treble }\npart b { clef bass }\nsection A { a { <e' g'>1@fermata | r1 | } b { a,1@fermata | d4 e f g | } }",
        "a", "0:1920 0:1920")]
    [InlineData("part a { clef treble }\nsection A { a { voice { c''4@fermata d''4 e''2 } { g'1 } | c''1 | } }", "a", "0:480 0:1920 480:480 960:960 1920:1920")]
    public void AFermataWithinThePiece_RingsUntilTheMusicGoesOn(string body, string part, string expected)
    {
        var midi = Play("octave absolute\ntime 4/4\n" + body + "\nform { A }\nscore { staff a" + (body.Contains("part b") ? " staff b" : "") + " }\n");
        var notes = midi.Tracks.SelectMany(t => t.Notes).Where(n => n.Part == part)
            .OrderBy(n => n.StartTick).ThenBy(n => n.DurationTicks).Select(n => $"{n.StartTick}:{n.DurationTicks}");
        Assert.Equal(expected, string.Join(" ", notes));
    }

    /// <summary>A fermata closing a repeat's body is not tied over the jump back: the body's
    /// first note, the same pitch, is struck again on the second pass.</summary>
    [Fact]
    public void AFermataClosingARepeat_IsNotTiedIntoItsFirstNote()
    {
        const string Source = "octave absolute\ntime 4/4\npart v { clef treble }\n"
            + "section A { v { |: e'2 d'2 | e'1@fermata :| } }\nform { A }\nscore { staff v }\n";
        var played = Play(Source).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).Select(n => n.Pitch).ToList();
        Assert.Equal([64, 62, 64, 64, 62, 64], played);
    }

    /// <summary>A note TIED into an ending is still played there: the tail is written music.</summary>
    [Fact]
    public void ANoteTiedIntoAnEnding_IsStillPlayedThere()
    {
        const string Source = "octave absolute\ntime 4/4\npart v { clef treble }\n"
            + "section A { v { |: c'1 | [1. d'1~ :| [2. d'1 ] } }\nform { A }\nscore { staff v }\n";
        var played = Play(Source).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick)
            .Select(n => $"{n.Pitch}@{n.StartTick / 1920}x{n.DurationTicks / 1920}").ToList();
        // c' (pass 1), d' of the first ending, c' (pass 2), d' of the second ending.
        Assert.Equal(["60@0x1", "62@1x1", "60@2x1", "62@3x1"], played);
    }
}
