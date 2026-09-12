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
using System.IO;
using LilySharp.Lsp.Protocol;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// What completion offers DIRECTLY inside a top-level <c>chords NAME { }</c> TRACK body —
/// the level that holds <c>section NAME { … }</c> cells once the track is written in the
/// part-major form, NOT chord entries. A chord symbol written beside the cells is dropped
/// on the floor (<c>ChordNameCollector</c> reads the sections when <c>HasSections</c>), and
/// in a part-major file a flat track is LYS2011 — so offering the chord vocabulary there
/// offers a spelling that does not render.
/// </summary>
/// <remarks>
/// User report (session 374): in
/// <code>chords prog { section A { Fm } ▮ }</code>
/// the popup listed chord names and never named <c>section</c>. The track body and its
/// inner section were one context: <see cref="LilySharpLanguageServer.IsInsideChordsBlock"/>
/// claimed both, and it is consulted BEFORE the context switch. The inner section keeps the
/// chord list — the controls below are the half that must not over-suppress.
/// </remarks>
[Trait("Category", "Unit")]
public class ChordsTrackBodyCompletionTests
{
    private static string[] CompletionLabelsAt(string text, int offset)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///chords.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var list = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        });
        return list?.Items.Select(i => i.Label!).ToArray() ?? System.Array.Empty<string>();
    }

    /// <summary>The caret marker in the sources below — the reported position.</summary>
    private static (string Text, int Offset) At(string marked)
    {
        int i = marked.IndexOf('▮');
        return (marked.Remove(i, 1), i);
    }

    [Fact]
    public void TheTrackBodyAndItsCellsAreDifferentContexts()
    {
        // Structural, before any list is built: the TRACK body is its own context (the
        // chords dual of LyricsBlock); a cell — the track's inner section, or a chords block
        // inside a section — is not.
        foreach (var text in new[] { "chords prog { ", "chords prog {\n  " })
            Assert.Equal(LilySharpLanguageServer.CompletionContext.ChordsTrackBody,
                LilySharpLanguageServer.GetCompletionContext(text, text.Length));

        foreach (var text in new[] { "chords prog { section A { ", "section A { chords prog { " })
            Assert.NotEqual(LilySharpLanguageServer.CompletionContext.ChordsTrackBody,
                LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    [Fact]
    public void SectionedTrackBody_OffersSection_NeverChordNames()
    {
        // The reported document: the track is already written in the sectioned form.
        var (text, offset) = At("""
            chords prog {
              section A {
                Fm
              }
              ▮
            }
            """);
        var labels = CompletionLabelsAt(text, offset);

        // A section is what belongs here — `A` is already written, so the way in is the
        // new-section item (a positive check: the list is not silently empty).
        Assert.Contains("section", labels);

        // The chord vocabulary must not be offered at this level.
        foreach (var chord in new[] { "C", "Dm", "G7", "I", "V7" })
            Assert.DoesNotContain(chord, labels);
    }

    [Fact]
    public void SectionedTrackBody_NamesTheSectionsTheFormPlays()
    {
        var (text, offset) = At("""
            part melody { section A { c'4 d' e' f' | } section B { g'4 a' b' c'' | } }
            chords prog {
              section A { C | }
              ▮
            }
            form main { A B }
            score main { staff melody  chords prog }
            """);
        var labels = CompletionLabelsAt(text, offset);

        // B is played but not yet written in this track; A is already here.
        Assert.Contains("section B", labels);
        Assert.DoesNotContain("section A", labels);
    }

    [Fact]
    public void PartMajorFlatTrackBody_OffersSection_NeverChordNames()
    {
        // No section written yet, but the file is part-major: a flat track is LYS2011, so
        // the cells are what belongs here even before the first one exists.
        var (text, offset) = At("""
            part melody { section A { c'4 d' e' f' | } section B { g'4 a' b' c'' | } }
            chords prog { ▮ }
            form main { A B }
            score main { staff melody  chords prog }
            """);
        var labels = CompletionLabelsAt(text, offset);

        Assert.Contains("section A", labels);
        foreach (var chord in new[] { "C", "Dm", "G7" })
            Assert.DoesNotContain(chord, labels);
    }

    [Fact]
    public void AfterTheSectionKeyword_OffersTheSectionNames_NotChords()
    {
        // The other half of the same report: having typed `section`, the names of the
        // sections this track has not covered are what belongs — the chord vocabulary
        // claimed this position too.
        var (text, offset) = At("""
            part melody { section A { c'4 d' e' f' | } section B { g'4 a' b' c'' | } }
            chords prog {
              section A { C | }
              section ▮
            }
            form main { A B }
            score main { staff melody  chords prog }
            """);
        var labels = CompletionLabelsAt(text, offset);

        Assert.Contains("B", labels);       // played, not yet written here
        Assert.DoesNotContain("A", labels); // already written in this track
        Assert.DoesNotContain("Dm", labels);
    }

    [Fact]
    public void InnerSectionBody_StillOffersChords()
    {
        // Control: inside the CELL the chord vocabulary is exactly right.
        var (text, offset) = At("""
            chords prog {
              section A {
                Fm ▮
              }
            }
            """);
        var labels = CompletionLabelsAt(text, offset);

        Assert.Contains("Dm", labels);
        Assert.Contains("G7", labels);
    }

    [Fact]
    public void FlatTrackInAStructurelessFile_StillOffersChords()
    {
        // Control: a lead sheet with no sections anywhere — `chords prog { C G7 | }` is the
        // whole track, and the chord list is what the writer wants.
        var (text, offset) = At("""
            key c major
            chords prog { C G7 | ▮ }
            """);
        var labels = CompletionLabelsAt(text, offset);

        Assert.Contains("Dm", labels);
        Assert.Contains("G7", labels);
    }
}
