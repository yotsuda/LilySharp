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
/// The parts a score does not sound ride in <c>lilysharp/playback</c>'s list after the score's
/// tracks, named in <c>Extra</c>, so the preview's practice menu can turn one on (owner's
/// request 2026-11-08: a part not in the score, off by default, switchable there). Each is on a
/// channel the score's tracks leave free. The score's own list is unchanged before them.
/// </summary>
public class ExtraPartsPlaybackTests
{
    private const string Doc = """
        part vn { midiInstrument "violin" }
        part pno
        part kit { clef percussion }
        section A {
          vn { c''4 d e f | }
          pno { e'4 f g a | }
          kit { bd4 bd sn bd | }
        }
        form { A }
        score { staff vn  staff pno  staff kit }
        score solo { staff vn }
        """;

    private static PlaybackResponse Playback(string? renderName)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///extra.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = Doc, Version = 1, LanguageId = "lilysharp" },
        });
        return server.GetPlayback(new PlaybackParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, RenderName = renderName });
    }

    [Fact]
    public void AScoreThatSoundsEveryPart_HasNoExtra()
    {
        var pb = Playback(null);
        Assert.Null(pb.Error);
        Assert.Empty(pb.Extra!);
        Assert.Equal(12, pb.Notes!.Length);
    }

    [Fact]
    public void ThePartsAScoreLeavesOut_FollowItsTracks_OnFreeChannels()
    {
        var all = Playback(null);
        var solo = Playback("solo");
        Assert.Null(solo.Error);
        Assert.Equal(new[] { "pno", "kit" }, solo.Extra);

        // The score's own list comes first and is what it was.
        int own = solo.Parts!.Length - solo.Extra!.Length;
        var ownNotes = solo.Notes!.Where(n => n.N < own).ToArray();
        Assert.Equal(4, ownNotes.Length);
        Assert.All(ownNotes, n => Assert.Equal(all.Notes!.First(a => a.S == n.S).P, n.P));

        // Then the others, each on its appended track: the piano on a channel the violin does
        // not use, the kit on the drum channel.
        int pnoTrack = Array.IndexOf(solo.Parts, "pno"), kitTrack = Array.IndexOf(solo.Parts, "kit");
        Assert.True(pnoTrack >= own && kitTrack >= own);
        var pno = solo.Notes!.Where(n => n.N == pnoTrack).ToArray();
        var kit = solo.Notes!.Where(n => n.N == kitTrack).ToArray();
        Assert.Equal(4, pno.Length);
        Assert.Equal(4, kit.Length);
        Assert.All(kit, n => Assert.Equal(9, n.C));
        Assert.DoesNotContain(pno[0].C, ownNotes.Select(n => n.C));
        Assert.NotEqual(9, pno[0].C);
        // In the same time: the piano's first note where the violin's is.
        Assert.Equal(ownNotes.Min(n => n.T), pno.Min(n => n.T));
    }
}
