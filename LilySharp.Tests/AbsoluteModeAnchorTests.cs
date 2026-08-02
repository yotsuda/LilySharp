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
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// In <c>octave absolute</c>, a bare <c>c</c> is C4 in EVERY part, and what the part SOUNDS
/// differs from that by exactly its written→sounding shift and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ NEITHER HALF OF THIS CAN BE SEEN FROM ONE SIDE, which is why the two are asserted in one
/// test over one book. Read the page alone and a bass part drawn an octave low still looks like
/// a bass part. Read the MIDI alone and it plays a plausible bass note. The defect this exists
/// for was exactly that shape: the instrument preset's anchor octave reached the ABSOLUTE base
/// (MeasureCollector folded it into one <c>octave</c> with the relative anchor), so the drawing
/// moved while the sounding shift did not. MEASURED before the fix, one <c>c4</c> per part:
/// </para>
/// <list type="table">
/// <item><term>instrument bass</term><description>drew C3, sounded C3 — the preset's −1 octave
///   silently CANCELLED the instrument's own −12, so a bass sounded what it printed.</description></item>
/// <item><term>instrument flute</term><description>drew C5, sounded C4 — a −12 on an
///   instrument that does not transpose at all.</description></item>
/// <item><term>instrument tuba</term><description>drew C2, sounded C4 — the two shifts ADDED,
///   to +24.</description></item>
/// <item><term>instrument guitar</term><description>drew C4, sounded C3 — the only correct row,
///   and correct precisely because its octave rides a <c>treble_8</c> CLEF and never went
///   through the preset anchor.</description></item>
/// </list>
/// <para>
/// ⚠️ THE TWO AXES ARE ORTHOGONAL AND MUST STAY SO. <c>octave absolute</c> / <c>relative</c>
/// decides how an octave is INFERRED; it does not decide whether the source names written or
/// sounding pitch. The source names WRITTEN pitch — as LilyPond's and MusicXML's do — and the
/// written→sounding shift is one mode-independent quantity,
/// <c>PartHeaderDefaults.SoundingShiftSemitones</c> = clef octave + transposition. Naming
/// sounding pitch instead is a defensible design, but it is a SEPARATE switch (a concert-pitch
/// toggle, which would have to serve B♭ instruments too, not only octave ones) and folding it
/// into the octave mode would make the two inexpressible apart.
/// </para>
/// </remarks>
public sealed class AbsoluteModeAnchorTests
{
    /// <summary>Treble's middle line is B4 (MIDI 71); bass's is D3 (MIDI 50).</summary>
    private const int TrebleMiddleLine = 71;
    private const int BassMiddleLine = 50;

    private static string Book(string partProperties) => $$"""
        octave absolute
        time 4/4
        key c major

        part m { {{partProperties}} }

        section A { m { c4 c4 c4 c4 | } }

        form main { A }

        score main { staff m }
        """;

    /// <summary>The MIDI pitch a staff position stands for, given the clef's middle line.</summary>
    /// <remarks>
    /// Diatonic, not chromatic: a staff position is a LETTER and the arithmetic has to go
    /// through the scale degrees or it lands a semitone out wherever a step is a half step.
    /// </remarks>
    private static int PitchAt(int middleLinePitch, int staffPosition)
    {
        int[] semitones = [0, 2, 4, 5, 7, 9, 11];
        int middleDegree = System.Array.IndexOf(semitones, middleLinePitch % 12);
        int degree = middleDegree + staffPosition;
        int octave = (middleLinePitch / 12) + (int)System.Math.Floor(degree / 7.0);
        return octave * 12 + semitones[((degree % 7) + 7) % 7];
    }

    [Theory]
    // A part that names only a clef: no transposition anywhere, so it sounds what it draws.
    [InlineData("clef treble", TrebleMiddleLine, 0)]
    [InlineData("clef bass", BassMiddleLine, 0)]
    // Presets whose ANCHOR octave is not 4. None of them may move the drawing.
    [InlineData("instrument bass", BassMiddleLine, -12)]
    [InlineData("instrument bass-guitar", BassMiddleLine, -12)]
    [InlineData("instrument tuba", BassMiddleLine, 0)]
    [InlineData("instrument flute", TrebleMiddleLine, 0)]
    // The row that was right all along: the octave rides the treble_8 clef, not the preset.
    [InlineData("instrument guitar", TrebleMiddleLine, -12)]
    public void AbsoluteC_IsWrittenC4_AndSoundsItsOwnShiftBelow(
        string partProperties, int middleLinePitch, int expectedShift)
    {
        string source = Book(partProperties);

        var page = RenderedGeometry.Render(source);
        double middleLineY = page.StaffRefpoints()[0];
        double staffPosition = (middleLineY - page.Noteheads[0].Y) * 2;

        Assert.Equal(staffPosition, System.Math.Round(staffPosition), 6);
        int written = PitchAt(middleLinePitch, (int)System.Math.Round(staffPosition));

        Assert.True(written == 60,
            $"`c` in octave absolute must be WRITTEN C4 (60) in every part, but "
            + $"part {{ {partProperties} }} drew staff position {staffPosition:F1} = MIDI "
            + $"{written}. An instrument preset's anchor octave has reached the absolute base "
            + "again — see MeasureCollector.GetPartDefaults' remarks.");

        var notes = new MidiExporter().Export(SyntaxTree.Parse(source))
            .Tracks.SelectMany(t => t.Notes).ToList();
        Assert.NotEmpty(notes);

        Assert.True(notes[0].Pitch - written == expectedShift,
            $"part {{ {partProperties} }} draws MIDI {written} and sounds {notes[0].Pitch}, a "
            + $"shift of {notes[0].Pitch - written} where its clef and transposition come to "
            + $"{expectedShift}. The page and the playback disagree about what this part is.");
    }
}
