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
using System.Xml.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A mid-music <c>clef</c> reopens the relative octave frame in that clef's own register —
/// asked of ALL FOUR outputs at once.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ ONE CASE ASKS EVERY OUTPUT, on purpose. This defect survived because each output had
/// its own nets and they were all green about their own answer: the page printed C3 D3 and
/// the MIDI, the MusicXML and the twin all played C5 D5, for as long as anyone had looked.
/// A per-output case would go green again the moment three of them agreed with each other.
/// </para>
/// <para>
/// THE RULE, decided 2026-08-17 (HANDOFF §3): Lily# ties the relative anchor to the clef, and
/// a part header's clef already does it (`part m { clef bass }` reads bare `c` as C3), so a
/// mid-music clef does the same — one word, one meaning. ⚠️ THIS DIVERGES FROM LILYPOND,
/// whose <c>\relative</c> never looks at a clef; the twin therefore cannot write the source's
/// own marks and writes corrected ones instead (`c,` becomes `c,,,`), the way it already does
/// for `transpose`. Verified against LilyPond 2.26.0 by dumping NoteHead pitches from the
/// generated twin: 13 heads, C5 D5 E5 F5 G5 A5 / C3 D3 E3 F3 G3 A3 / C6 — the page's answer.
/// </para>
/// <para>
/// ⚠️ AN UNCHANGED CLEF CHANGES NOTHING, and the last case holds that end: LilyPond makes a
/// Clef grob only when glyph/position/transposition differ, so a redundant `clef treble` must
/// not move the frame either. A rule that reset on every `clef` token would pass everything
/// above it.
/// </para>
/// </remarks>
public class ClefReanchorsFrameTests
{
    // ⚠️ THE FIRST BAR IS NOT DECORATION. It puts the running frame up at F5 so the clef's
    // answer and the frame's answer are two octaves apart. Written without it — `g4 a clef
    // bass c,4` — BOTH rules give C3 and the pair proves nothing (measured while writing
    // this file: the first draft's book and its control returned the same four pitches).
    private const string Book = """
        part m { clef treble }
        section A { m { c'4 d e f | g4 a  clef bass  c,4 d | } }
        form main { ~A }
        score main { staff m }
        """;

    // The same music with the clef change removed: `c,` then reads from the running frame,
    // which is what "the clef did nothing" looks like. Every case below is a pair against it.
    private const string NoClef = """
        part m { clef treble }
        section A { m { c'4 d e f | g4 a  c,4 d | } }
        form main { ~A }
        score main { staff m }
        """;

    private static int[] PagePitches(string src)
        => (ResolvedPitches.ForFile(SyntaxTree.Parse(src)) ?? [])
            .Select(p => RelativeOctave.StepToMidi(
                "CDEFGAB".IndexOf(p.Pitch[0]),
                p.Pitch.Skip(1).TakeWhile(c => c is '#' or 'b' or 'x')
                    .Sum(c => c == '#' ? 1 : c == 'x' ? 2 : -1),
                int.Parse(new string(p.Pitch.SkipWhile(c => !char.IsDigit(c)).ToArray()))))
            .ToArray();

    private static int[] MidiPitches(string src)
        => new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks
            .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).Select(n => n.Pitch).ToArray();

    private static int[] XmlPitches(string src)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().Descendants("pitch")
            .Select(p => RelativeOctave.StepToMidi(
                "CDEFGAB".IndexOf(p.Element("step")!.Value[0]),
                (int)(double.Parse(p.Element("alter")?.Value ?? "0")),
                int.Parse(p.Element("octave")!.Value)))
            .ToArray();

    private static string Twin(string src) => new LilyPondExporter().Export(SyntaxTree.Parse(src));

    [Fact]
    public void ThePage_ReadsTheNotesAfterTheClefInItsRegister()
    {
        // … G5 A5 then C3 D3 — the `,` is one octave below the BASS anchor, not below the a.
        Assert.Equal(new[] { 72, 74, 76, 77, 79, 81, 48, 50 }, PagePitches(Book));
        // The control: with no clef the same two letters stay beside the a, two octaves up.
        Assert.Equal(new[] { 72, 74, 76, 77, 79, 81, 72, 74 }, PagePitches(NoClef));
    }

    [Fact]
    public void TheMidi_SoundsWhatThePagePrints()
    {
        Assert.Equal(PagePitches(Book), MidiPitches(Book));
        Assert.Equal(PagePitches(NoClef), MidiPitches(NoClef));
    }

    [Fact]
    public void TheMusicXml_WritesWhatThePagePrints()
    {
        Assert.Equal(PagePitches(Book), XmlPitches(Book));
        Assert.Equal(PagePitches(NoClef), XmlPitches(NoClef));
    }

    [Fact]
    public void TheTwin_WritesCorrectedMarks_BecauseLilyPondsRelativeIgnoresClefs()
    {
        // LilyPond will read `c,,,` from its own last note (a) and land on C3, which is what
        // the page prints. Verified by running this twin through LilyPond 2.26.0.
        Assert.Contains("\\clef \"bass\" c,,,4", Twin(Book));
        // And the control still writes the source's own single comma, so the correction is
        // the clef's doing and not something the twin does to every `,`.
        Assert.Contains("c,4", Twin(NoClef));
        Assert.DoesNotContain("c,,,4", Twin(NoClef));
    }

    [Fact]
    public void ARedundantClef_MovesNothing_InAnyOutput()
    {
        // `clef treble` inside a treble part engraves no grob and must not reset the frame.
        const string redundant = """
            part m { clef treble }
            section A { m { c'4 d e f | g4 a  clef treble  c,4 d | } }
            form main { ~A }
            score main { staff m }
            """;
        Assert.Equal(PagePitches(NoClef), PagePitches(redundant));
        Assert.Equal(PagePitches(NoClef), MidiPitches(redundant));
        Assert.Equal(PagePitches(NoClef), XmlPitches(redundant));
    }

    [Fact]
    public void ACueClef_ReopensTheFrameAtBothEdges()
    {
        // `audit/lp-regression/lys/cue-clef-manually`'s shape: the cue body reads in the cue
        // clef, and the music after the region is back in the staff's own.
        const string cue = """
            part m { clef treble }
            section A { m { c4 c c c | voice { cue bass { c4 c c c } } { R1 } | c4 c c c | } }
            form main { ~A }
            score main { staff m }
            """;
        // The control is the same region with no clef on it, where nothing moves.
        const string plainCue = """
            part m { clef treble }
            section A { m { c4 c c c | voice { cue { c4 c c c } } { R1 } | c4 c c c | } }
            form main { ~A }
            score main { staff m }
            """;
        Assert.All(PagePitches(plainCue), p => Assert.Equal(60, p));

        // The four cue notes drop to C3 — the bass clef opened the frame at octave 3 — and
        // the four after the region are back at C4, which is the CLOSING edge doing its half.
        var page = PagePitches(cue);
        Assert.Equal(
            new[] { 60, 60, 60, 60, 48, 48, 48, 48, 60, 60, 60, 60 }, page);
        Assert.Equal(page, MidiPitches(cue));
        Assert.Equal(page, XmlPitches(cue));
    }
}
