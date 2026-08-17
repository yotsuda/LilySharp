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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>a4@rest</c> is a REST placed by a written pitch, and it has to be a rest in ALL FOUR
/// outputs. Two of them said otherwise until 2026-08-17.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ NEITHER HALF IS VISIBLE FROM ONE SIDE, which is why one book is asked of all four here.
/// The page has drawn rests since the spelling was added, and the twin has written
/// <c>a'4\rest</c> — so every test that looked at either was green. MusicXML wrote
/// <c>&lt;note&gt;&lt;pitch&gt;A4&lt;/pitch&gt;</c> and the MIDI played it: MEASURED on
/// <c>a'4@rest c'4 r4 g'4@rest</c>, 3 note-ons against the 1 of the same book with plain
/// rests. A player-piano roll and a score editor were reading a different piece than the page.
/// </para>
/// <para>
/// ⚠️ HANDOFF §2F had this filed as "MusicXML drops the height — a pitched rest becomes
/// <c>&lt;rest/&gt;</c>", which is the smaller and gentler half of what was happening: it was
/// not a rest that lost its position, it was not a rest. Both exporters walk the syntax tree
/// rather than the collector's items, so each needed its own reader of the spelling and
/// neither had one; there is one house for it now (Semantics.PitchedRest), which is what the
/// collector and the twin ask too.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PitchedRestOutputsTests
{
    private const string Book = """
        octave absolute
        time 4/4
        part v { clef treble }
        section S { v { a'4@rest c'4 r4 g'4@rest | } }
        form main { ~S }
        score main { staff v }
        """;

    /// <summary>The control: the same book with the two pitched rests written plainly.</summary>
    private const string Plain = """
        octave absolute
        time 4/4
        part v { clef treble }
        section S { v { r4 c'4 r4 r4 | } }
        form main { ~S }
        score main { staff v }
        """;

    [Fact]
    public void APitchedRest_IsARestInEveryOutput()
    {
        var tree = SyntaxTree.Parse(Book);

        // ⑴ the page: one head, for the one note that is a note
        Assert.Single(RenderedGeometry.Render(Book).Noteheads);

        // ⑵ the twin: LilyPond's own spelling of the same thing
        string ly = new LilyPondExporter().Export(tree);
        Assert.Contains("a'4\\rest", ly);
        Assert.Contains("g'4\\rest", ly);

        // ⑶ MusicXML: a <rest>, carrying the written pitch as its DISPLAY position
        var xml = new MusicXmlExporter().Export(tree).Parts[0].Measures[0].Notes;
        Assert.Equal(4, xml.Count);
        Assert.True(xml[0].IsRest && xml[0].RestHasDisplayPitch, "the first event is a rest");
        Assert.Equal(("A", 5), (xml[0].Step, xml[0].Octave));
        Assert.False(xml[1].IsRest, "the c'4 between them is still a note");
        Assert.True(xml[2].IsRest && !xml[2].RestHasDisplayPitch, "a plain rest displays nothing");
        Assert.True(xml[3].IsRest && xml[3].RestHasDisplayPitch, "the last event is a rest");
        Assert.Equal(("G", 5), (xml[3].Step, xml[3].Octave));

        // ⑷ MIDI: silence. One note-on, and it is the c'4.
        var midi = new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes).ToList();
        Assert.Single(midi);
    }

    /// <summary>
    /// A rest still takes its time. Asserted as an EQUALITY with the plain-rest control
    /// rather than as a tick number, because "emits nothing" and "emits nothing and swallows
    /// the beat" look identical from the note count alone — and the second would silently
    /// shorten every book that uses the spelling.
    /// </summary>
    [Fact]
    public void APitchedRest_StillTakesItsTime_LikeThePlainRestItStandsFor()
    {
        var pitched = new MidiExporter().Export(SyntaxTree.Parse(Book))
            .Tracks.SelectMany(t => t.Notes).ToList();
        var plain = new MidiExporter().Export(SyntaxTree.Parse(Plain))
            .Tracks.SelectMany(t => t.Notes).ToList();

        Assert.Single(plain);
        Assert.Equal(plain[0].StartTick, pitched[0].StartTick);
        Assert.Equal(plain[0].DurationTicks, pitched[0].DurationTicks);
        Assert.Equal(plain[0].Pitch, pitched[0].Pitch);
    }

    /// <summary>
    /// The display position is the SOUNDING one when the part transposes, because that is
    /// where the page draws the glyph: a transpose moves a pitched rest with everything else,
    /// and LilyPond's <c>\transpose</c> — which is what the twin now writes — moves the pitch
    /// inside the rest event too.
    /// </summary>
    [Fact]
    public void ATransposingPartMovesItsPitchedRest()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part v { clef treble transpose d }
            section S { v { a'4@rest r2. | } }
            form main { ~S }
            score main { staff v }
            """)).Parts[0].Measures[0].Notes;

        Assert.True(xml[0].IsRest && xml[0].RestHasDisplayPitch);
        // `a'` is A5 in absolute mode, and c→d moves it a major second: B5.
        Assert.Equal(("B", 5), (xml[0].Step, xml[0].Octave));
    }
}
