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
using System.IO;
using System.Linq;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The <c>lilysharp/playback</c> request plays the score the preview is SHOWING: the
/// request names the picker's score and the notes are that score's parts (user report
/// 2026-09-29: with <c>score p2 { staff p2 }</c> picked, Play sounded p1 too). No
/// name, or a name no score has, is the first score — the same fallback the drawing uses.
/// </summary>
public class PlaybackRequestTests
{
    // scratch/parts.lys: p1 and p2 on one form; `main` shows both, "p2" shows p2 alone.
    private const string Doc =
        "section A {\n" +
        "  p1 { c'4 d e f | g2 g | }\n" +
        "  p2 { e f g a | b2 b }\n" +
        "}\n\nform { A }\n\n" +
        "score {\n  staff p1\n  staff p2\n}\n\n" +
        "score p2 {\n  staff p2\n}\n";

    // A violin, a part that names no sound, and a drum kit — three channels, two programs.
    private const string Sounds =
        "part vn { midiInstrument \"violin\" }\n" +
        "part pno\n" +
        "part kit { clef percussion }\n" +
        "section A {\n" +
        "  vn { c'4 d e f | }\n" +
        "  pno { e f g a | }\n" +
        "  kit { bd4 bd sn bd | }\n" +
        "}\n\nform { A }\n\n" +
        "score {\n  staff vn\n  staff pno\n  staff kit\n}\n";

    private static PlaybackResponse Playback(string? renderName) => Playback(Doc, renderName);

    private static PlaybackResponse Playback(string doc, string? renderName)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///parts.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = doc, Version = 1, LanguageId = "lilysharp" },
        });
        return server.GetPlayback(new PlaybackParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            RenderName = renderName,
        });
    }

    // p1 opens on c' (72) and p2 on e (64): the two parts never share a pitch on the first
    // beat, so the onset-0 pitches say which parts sound.
    private static int[] PitchesAtOnset0(PlaybackResponse pb)
    {
        Assert.Null(pb.Error);
        Assert.NotNull(pb.Notes);
        return pb.Notes!.Where(n => n.T == 0).Select(n => (int)n.P).OrderBy(p => p).ToArray();
    }

    [Fact]
    public void ThePickedScore_PlaysItsPartsAlone()
    {
        var pb = Playback("p2");
        Assert.Equal(6, pb.Notes!.Length);
        Assert.Equal(new[] { 64 }, PitchesAtOnset0(pb));
    }

    [Fact]
    public void NoScoreNamed_PlaysTheFirstScore()
    {
        var pb = Playback(null);
        Assert.Equal(12, pb.Notes!.Length);
        Assert.Equal(new[] { 64, 72 }, PitchesAtOnset0(pb));
    }

    [Fact]
    public void AScoreThatIsNotInTheFile_FallsBackToTheFirst_AsTheDrawingDoes()
    {
        Assert.Equal(12, Playback("gone").Notes!.Length);
    }

    /// <summary>
    /// Every note names the channel and the General MIDI program the .mid export plays it
    /// with (MidiExporter.SplitIntoPartTracks, MidiTrack.Program), so the preview's SoundFont
    /// synth sounds the instruments the export does: the violin on its own channel with its
    /// program, the part naming no sound on the next with LilyPond's default (the acoustic
    /// grand, 0), the kit on the drum channel, where the program is not read.
    /// </summary>
    [Fact]
    public void EveryNote_CarriesTheChannelAndTheProgram_TheMidWouldPlayItWith()
    {
        var pb = Playback(Sounds, null);
        Assert.Null(pb.Error);
        var programByChannel = pb.Notes!
            .GroupBy(n => n.C)
            .ToDictionary(g => g.Key, g => g.Select(n => n.G).Distinct().Single());
        Assert.Equal(new[] { 0, 1, 9 }, programByChannel.Keys.OrderBy(c => c));
        Assert.Equal(40, programByChannel[0]);   // violin: GM 41, 0-based
        Assert.Equal(0, programByChannel[1]);
        Assert.Equal(0, programByChannel[9]);
        Assert.Equal(4, pb.Notes!.Count(n => n.C == 9));
        Assert.All(pb.Notes!.Where(n => n.C == 9), n => Assert.Equal(9, n.I));
    }

    /// <summary>Every note names its track, and the response names the tracks — what the
    /// preview's part switches mute by (owner's request 2026-10-25).</summary>
    [Fact]
    public void EveryNote_NamesItsPart()
    {
        var pb = Playback(Sounds, null);
        Assert.NotNull(pb.Parts);
        var byPart = pb.Notes!.GroupBy(n => pb.Parts![n.N]).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new[] { "kit", "pno", "vn" }, byPart.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(byPart.Values, c => Assert.Equal(4, c));
        Assert.All(pb.Notes!.Where(n => pb.Parts![n.N] == "kit"), n => Assert.Equal(9, n.C));
    }

    /// <summary>The metronome's beats: a 4/4 bar of quarters at 120 is four beats half a second
    /// apart, the first accented.</summary>
    [Fact]
    public void TheBeats_AreTheMetersBeats_TheFirstOfEachBarAccented()
    {
        var pb = Playback(Sounds, null);
        Assert.NotNull(pb.Beats);
        Assert.Equal(new[] { 0.0, 0.5, 1.0, 1.5 }, pb.Beats!.Select(b => Math.Round(b.T, 3)));
        Assert.Equal(new[] { true, false, false, false }, pb.Beats!.Select(b => b.A));
    }

    /// <summary>A pickup keeps the accents on the bar lines (owner's report 2026-10-25): the
    /// meter's grid from tick 0 had accented the pickup's beat and every bar's second.</summary>
    [Fact]
    public void APickupsBeat_IsNoBarsFirst_AndTheBarsAfterItAreAccentedOnTheirBarLines()
    {
        const string doc = "time 4/4\npart m { }\nsection A {\n  partial 4\n  m { g4 | c'4 d e f | g1 | }\n}\nform { A }\nscore { staff m }\n";
        var pb = Playback(doc, null);
        Assert.Null(pb.Error);
        // At 120: the pickup's beat at 0, the bars at 0.5 and 2.5.
        Assert.Equal(new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0 }, pb.Beats!.Select(b => Math.Round(b.T, 3)));
        Assert.Equal(new[] { 0.5, 2.5 }, pb.Beats!.Where(b => b.A).Select(b => Math.Round(b.T, 3)));
    }

    [Fact]
    public void AText_PlaysAsTheDocumentWould_AndAChangedOneAsChanged()
    {
        // lilysharp/playbackText (owner's request 2026-10-25): an AI candidate is heard before it
        // is accepted — the same notes the document's own Play gives for the same text, and the
        // candidate's for the candidate.
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var asIs = server.GetPlaybackText(new PlaybackTextParams { Text = Doc, RenderName = "p2" });
        var opened = Playback("p2");
        Assert.Equal(opened.Notes!.Select(n => (n.T, n.P)), asIs.Notes!.Select(n => (n.T, n.P)));

        var changed = server.GetPlaybackText(new PlaybackTextParams { Text = Doc.Replace("e f g a | b2 b", "g a b c | d2 d") });
        Assert.Null(changed.Error);
        var opening = changed.Notes!.Where(n => n.T == 0).Select(n => (int)n.P).ToArray();
        Assert.Contains(55, opening);          // p2 now opens on g (G3, the nearest to its anchor)
        Assert.DoesNotContain(64, opening);    // not on the e it opened on
        Assert.Equal("Score has errors", server.GetPlaybackText(new PlaybackTextParams { Text = "section A { p1 { c4 d" }).Error);
    }
}
