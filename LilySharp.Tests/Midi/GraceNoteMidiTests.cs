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

using System;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Midi;

/// <summary>
/// MIDI grace-note export (regression guard for Semantics M5). Grace export is
/// not covered by the SVG snapshot suite, so these pin: grace CHORD members are
/// emitted (were silently dropped by OfType&lt;NoteSyntax&gt;), the grace sounding
/// duration is 9/40 of the WRITTEN duration (was hard-coded 1/32 — LILYPOND-REF:
/// ly/articulate.ly ac:defaultGraceFactor = 9/40), and grace time is still stolen
/// from the following note so the downbeat stays on the metric grid.
/// </summary>
public class GraceNoteMidiTests
{
    private static List<MidiNote> ExportNotes(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var file = new MidiExporter().Export(tree);
        return file.Tracks.SelectMany(t => t.Notes).ToList();
    }

    [Fact]
    public void GraceChord_EmitsAllMembers()
    {
        var notes = ExportNotes("""
            octave absolute
            part m { clef treble }
            section A { m { grace { <c' e'>16 } d'4 | } }
            form main { A }
            score main { staff m }
            """);
        // Before the fix the grace chord was dropped whole (OfType<NoteSyntax>),
        // leaving only the main d'. Now both chord members sound as grace notes.
        Assert.Equal(3, notes.Count);
        var grace = notes.Where(n => n.StartTick == 0).ToList();
        Assert.Equal(2, grace.Count);                                  // both members
        Assert.Equal(2, grace.Select(n => n.Pitch).Distinct().Count()); // distinct pitches
        Assert.All(grace, g => Assert.Equal(grace[0].DurationTicks, g.DurationTicks));
        var main = Assert.Single(notes.Where(n => n.StartTick > 0));
        Assert.Equal(grace[0].DurationTicks, main.StartTick);          // main starts after the grace
    }

    [Fact]
    public void GraceNote_SoundingDuration_Is9_40thOfWritten()
    {
        // LILYPOND-REF: ly/articulate.ly ac:defaultGraceFactor = 9/40 — a grace
        // note sounds for 9/40 of its NOTATED duration in LP's built-in MIDI.
        int GraceDur(string dur) => ExportNotes($$"""
            octave absolute
            part m { clef treble }
            section A { m { grace { c'{{dur}} } d'4 | } }
            form main { A }
            score main { staff m }
            """).OrderBy(n => n.StartTick).First().DurationTicks;
        int NoteDur(string dur) => ExportNotes($$"""
            octave absolute
            part m { clef treble }
            section A { m { c'{{dur}} } }
            form main { A }
            score main { staff m }
            """).First().DurationTicks;

        // Each grace item's sounding time is 9/40 of its written value (checked
        // independently for a 16th and a 32nd; the exact 2x ratio between them is
        // not asserted because integer tick rounding of 9/40 breaks it — 480 PPQ:
        // 16th -> round(9/40*120)=27, 32nd -> round(9/40*60)=14, and 2*14 != 27).
        int g16 = GraceDur("16"), g32 = GraceDur("32");
        Assert.Equal(9, (int)Math.Round(g16 * 40.0 / NoteDur("16")));      // factor == 9/40
        Assert.Equal(9, (int)Math.Round(g32 * 40.0 / NoteDur("32")));

        // An unwritten grace duration is an EIGHTH — the LAYOUT's rule, read from
        // MeasureCollector.CollectGraceNotes (graceDefaultDuration = Fraction.Eighth).
        // ⚠️ It used to be a 1/32 here and a 1/8 on the page: one spelling with two
        // answers, which is what this line now guards against coming back.
        int gDefault = GraceDur(""), g8 = GraceDur("8");
        Assert.Equal(g8, gDefault);
    }

    [Fact]
    public void GraceNote_ThreadsWrittenDurationToLaterGraceItems()
    {
        // grace { d16 e }: e inherits the 16th (duration threads within the group),
        // it is NOT reset to the 1/32 default — so both sound the same length.
        var notes = ExportNotes("""
            octave absolute
            part m { clef treble }
            section A { m { grace { d'16 e' } f'4 | } }
            form main { A }
            score main { staff m }
            """).OrderBy(n => n.StartTick).ToList();
        Assert.Equal(3, notes.Count);
        Assert.Equal(notes[0].DurationTicks, notes[1].DurationTicks); // e' == d' (both 16th → same 9/40 length)
    }

    [Fact]
    public void Grace_StealsTimeFromFollowingNote_KeepingGrid()
    {
        var notes = ExportNotes("""
            octave absolute
            part m { clef treble }
            section A { m { grace { c'16 } d'4 | } }
            form main { A }
            score main { staff m }
            """).OrderBy(n => n.StartTick).ToList();
        int quarter = ExportNotes("""
            octave absolute
            part m { clef treble }
            section A { m { d'4 } }
            form main { A }
            score main { staff m }
            """).First().DurationTicks;

        Assert.Equal(2, notes.Count);
        var grace = notes[0];
        var main = notes[1];
        Assert.Equal(0, grace.StartTick);
        Assert.Equal(grace.DurationTicks, main.StartTick);          // main begins where the grace ends
        Assert.Equal(quarter, main.StartTick + main.DurationTicks); // pair fills d's original quarter slot
    }

    [Fact]
    public void GraceNote_AdvancesRelativeOctaveForFollowingNote()
    {
        // Default (relative) mode: the note AFTER a grace group resolves its octave
        // relative to the grace's LAST pitch — the same result as if the grace pitch
        // were a plain note. Pins the collector/exporter octave-threading seam through
        // grace, which was previously untested. LILYPOND-REF: grace threads relative octave.
        int DAfter(string body) => ExportNotes($$"""
            part m { clef treble }
            section A { m { {{body}} } }
            form main { A }
            score main { staff m }
            """).OrderByDescending(n => n.StartTick).First().Pitch; // the trailing d

        // `grace { g'16 }` and a plain `g'16` before the d reference the same pitch,
        // so d must land on the same octave in both.
        Assert.Equal(DAfter("c'4 g'16 d16"), DAfter("c'4 grace { g'16 } d16"));
    }
}
