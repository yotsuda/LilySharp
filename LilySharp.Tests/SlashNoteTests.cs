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

using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The slash note (<c>/4</c>): rhythm (comping) notation — a pitchless note
/// drawn as a slash head on the MIDDLE staff line, silent in playback, with
/// ordinary duration/stem/beam behaviour. The `/` token depicts the printed
/// ink, the way `|` depicts a barline (HANDOFF §3 records the sigil-rule
/// decision).
/// LILYPOND-REF: ly/property-init.ly improvisationOn — LilyPond's spelling of
/// the same page (slash heads, no accidentals) on a written pitch; the twin
/// exports exactly that form.
/// </summary>
[Trait("Category", "Unit")]
public class SlashNoteTests
{
    private static List<MusicItem> Items(string source)
    {
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(source), null);
        return score.Voice.Measures.SelectMany(m => m.Items).ToList();
    }

    [Fact]
    public void SitsOnTheMiddleLine_WithASlashHead()
    {
        var slash = Assert.IsType<NoteItem>(Items("/4")[0]);
        Assert.Equal(0, slash.StaffPosition);
        Assert.Equal(NoteheadStyle.Slash, slash.Notehead);
        Assert.Null(slash.Accidental);
        Assert.False(slash.NeedsLedgerLines);
    }

    [Fact]
    public void CarriesDurations_LikeAnyNote()
    {
        // Written, inherited, and dotted — the ordinary carry. (BaseDuration:
        // Duration folds the dots in, so a dotted quarter reads 3/8.)
        var notes = Items("/8 / /4. /").OfType<NoteItem>().ToList();
        Assert.Equal(new[] { 8, 8, 4, 4 },
            notes.Select(n => (int)n.BaseDuration.Denominator).ToArray());
        Assert.Equal(new[] { 0, 0, 1, 1 }, notes.Select(n => n.Dots).ToArray());
    }

    [Fact]
    public void IsSilentInMidi_ButOccupiesItsTime()
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart v { }\n"
            + "section Main { v { /4 /4 c'2 | } }\nform main { Main }\nscore main { staff v }");
        var notes = new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes).ToList();
        var note = Assert.Single(notes);
        // The two silent quarters pushed the c' to beat 3.
        var control = SyntaxTree.Parse("octave absolute\ntime 4/4\npart v { }\n"
            + "section Main { v { r4 r4 c'2 | } }\nform main { Main }\nscore main { staff v }");
        var controlNote = Assert.Single(
            new MidiExporter().Export(control).Tracks.SelectMany(t => t.Notes));
        Assert.Equal(controlNote.StartTick, note.StartTick);
    }

    [Fact]
    public void TheTwinSpellsItAsImprovisation_OnTheClefsMiddleLine()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(
            "part m\nsection A { m { /4 4 c'4 d | } }\nform main { A }\nscore main { staff m }"));
        // Treble middle line is b' (relative from c': one mark up), the run
        // closes before the first pitched note, and the bare duration rides
        // inside the run.
        Assert.Contains("\\improvisationOn b'4 4", ly);
        Assert.Contains("\\improvisationOff c", ly);
    }

    [Fact]
    public void TheTwinFollowsTheClef_ForTheMiddlePitch()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(
            "part m { clef bass }\nsection A { m { /4 4 | } }\nform main { A }\nscore main { staff m }"));
        // Bass middle line is d (octave 3) — never treble's b'.
        Assert.Contains("\\improvisationOn d", ly);
        Assert.DoesNotContain("b'4", ly);
    }

    [Fact]
    public void MusicXml_WritesAnUnpitchedSlashHead()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(
            "part m\nsection A { m { /4 | } }\nform main { A }\nscore main { staff m }")).ToXml().ToString();
        Assert.Contains("<unpitched>", xml);
        Assert.Contains("slash", xml);
    }

    [Fact]
    public void RoundTrips_WithNoDiagnostics()
    {
        const string src = "part m\nsection A { m { /4 4 8 16 8 4 | } }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(src, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void OtherSlashes_KeepTheirOwnMeanings()
    {
        // `/` is claimed only in note position: the time signature's and a chord
        // entry's `/` never reach the music-item dispatch.
        var tree = SyntaxTree.Parse("time 6/8\npart m\nsection A {\n"
            + "  m { time 3/4 c4 d e | }\n  chords prog { C/G G7 | }\n}\n");
        Assert.Empty(tree.Diagnostics);
        Assert.DoesNotContain(tree.GetRoot().DescendantNodes(), n => n is SlashNoteSyntax);
    }
}
