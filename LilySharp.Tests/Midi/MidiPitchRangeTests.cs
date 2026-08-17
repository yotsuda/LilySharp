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

namespace LilySharp.Tests.Midi;

/// <summary>
/// The edge of the MIDI keyboard: what a note written outside 0-127 sounds as, and that
/// the export SAYS it could not sound it where written.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ A BOOK RUNS OFF THE TOP BY ITS OWN SPELLING. `a'' a'' a''` in relative mode is not
/// three A5s — each `''` counts from the NEAREST a, so the line climbs two octaves a note.
/// Two fixtures do exactly this (`audit/lpreg/fermata-b-obs-probe`, 2 of 4 notes, and
/// `test/section-meter-resets-to-global`, 9 of 14), and until 2026-08-17 the result was a
/// run of identical 127s that no output, log or net mentioned. The page and the MusicXML
/// keep the written octave, so this is the one output that loses it — HANDOFF §2F: if you
/// drop something, say so.
/// </para>
/// <para>
/// ⚠️ THE RANGE IS APPLIED ONCE, at the note, and the second case here is why. It used to
/// be applied inside the written→MIDI conversion as well, and three callers then added a
/// chord or arpeggio octave to the CLAMPED value: the shift landed on 127 rather than on
/// the pitch, so a high chord pulled down by `,` came out an octave and more below what the
/// arithmetic says. No book in the 566 does this, which is exactly why it needed a case.
/// </para>
/// </remarks>
public class MidiPitchRangeTests
{
    private static (List<MidiNote> Notes, IReadOnlyList<string> Warnings) Export(string body)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part v { }
            section Main { v { {{body}} } }
            form main { ~Main }
            score main { staff ~v }
            """);
        var exporter = new MidiExporter();
        var file = exporter.Export(tree);
        return (file.Tracks.SelectMany(t => t.Notes).ToList(), exporter.Warnings);
    }

    [Fact]
    public void APitchAboveTheKeyboard_SoundsAtTheEdge_AndIsReported()
    {
        // c'''''''''  = octave 13 = written key 168. There is no such MIDI key.
        var (notes, warnings) = Export("c'''''''''4 c4 |");

        // It still SOUNDS — pinning is not dropping, and a silent note would be a
        // different defect wearing the same count.
        Assert.Equal(2, notes.Count);
        Assert.Equal(127, notes[0].Pitch);
        Assert.Equal(60, notes[1].Pitch);  // bare c is C4 in absolute mode

        Assert.Single(warnings);
        Assert.Contains("168", warnings[0]);   // what the source asked for
        Assert.Contains("127", warnings[0]);   // what it sounds as
    }

    [Fact]
    public void AnInRangeBook_SaysNothing()
    {
        // The control. A report that fired on every note would satisfy the case above.
        var (notes, warnings) = Export("c'4 d' e' f' |");
        Assert.Equal(4, notes.Count);
        Assert.Empty(warnings);
    }

    [Fact]
    public void EveryOutOfRangeNote_IsReportedOnce_NotJustTheFirst()
    {
        var (_, warnings) = Export("c'''''''''4 d''''''''' c' e''''''''' |");
        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void AChordsOctaveMark_MovesTheWrittenPitch_NotThePinnedOne()
    {
        // <c e>,,,, is four octaves down. Written: 168 and 172, so the chord sounds 120
        // and 124 — both inside the keyboard, so nothing is pinned and nothing is said.
        var (notes, warnings) = Export("<c''''''''' e'''''''''>,,,,1 |");

        Assert.Equal(new[] { 120, 124 }, notes.Select(n => n.Pitch));
        Assert.Empty(warnings);
    }
}
