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
}
