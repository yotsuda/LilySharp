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

using System.IO;
using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// Inside a section's MUSIC the completion offers the current key's diatonic chords —
/// by name (<c>C</c>, <c>Cmaj7</c>, <c>Dm</c>, <c>Dm7</c> …) and by degree (<c>I</c>,
/// <c>IIm7</c>, <c>V7</c> …) — and accepting one inserts the NOTE CHORD that voices it:
/// <c>C</c> → <c>&lt;c e g&gt;</c>, <c>IIm7</c> → <c>&lt;d f a c&gt;</c>.
/// </summary>
/// <remarks>
/// ⚠️ The owner reported this row as implemented once and lost (2026-09-02). Nothing had
/// asserted it: the note-chord expansion only ever fired on a TYPED symbol of two letters
/// or more, and the diatonic list lived in <c>@chord(…)</c> and <c>chords { }</c> alone.
/// <see cref="EveryOfferedChordCompilesWhereItIsInserted"/> is the net that connects the
/// list to the parser, as RomanChordCompletionTests does for the chords block.
/// </remarks>
[Trait("Category", "Unit")]
public class MusicChordCompletionTests
{
    private static CompletionItem[] Items(char tonic, int sharps, bool contracted = false)
        => LilySharpLanguageServer.GetMusicCompletions("", sharps, contracted, false, tonic).Items;

    private static string Insert(CompletionItem[] items, string label)
        => items.Single(i => i.Label == label).InsertText!;

    [Fact]
    public void CMajor_OffersTheDiatonicChordsByNameAndByDegree()
    {
        var labels = Items('c', 0).Select(i => i.Label).ToArray();
        foreach (var name in new[] { "C", "Cmaj7", "Dm", "Dm7", "Em", "Em7", "F", "Fmaj7",
                                     "G", "G7", "Am", "Am7", "Bdim", "Bm7-5", "Csus4", "Gsus2" })
            Assert.Contains(name, labels);
        foreach (var degree in new[] { "I", "Imaj7", "IIm", "IIm7", "IIIm", "IV", "V", "V7", "VIm", "VIIdim" })
            Assert.Contains(degree, labels);
    }

    [Fact]
    public void AcceptingAChord_InsertsItsNotes()
    {
        var items = Items('c', 0);
        Assert.Equal("<c e g>", Insert(items, "C"));
        Assert.Equal("<c e g b>", Insert(items, "Cmaj7"));
        Assert.Equal("<d f a>", Insert(items, "Dm"));
        Assert.Equal("<d f a c>", Insert(items, "Dm7"));
        Assert.Equal("<g b d f>", Insert(items, "G7"));
        Assert.Equal("<b d f a>", Insert(items, "Bm7-5"));
    }

    [Fact]
    public void ADegree_InsertsTheSameNotesAsTheNameItStandsFor()
    {
        var items = Items('c', 0);
        Assert.Equal("<d f a c>", Insert(items, "IIm7"));
        Assert.Equal(Insert(items, "Dm7"), Insert(items, "IIm7"));
        Assert.Equal(Insert(items, "G7"), Insert(items, "V7"));
        Assert.Equal(Insert(items, "C"), Insert(items, "I"));
        Assert.Equal(Insert(items, "Bdim"), Insert(items, "VIIdim"));
    }

    [Fact]
    public void TheChordsFollowTheKey_AndSpellItsAccidentals()
    {
        // D major: F#m is the third degree; A minor: the same seven chords as C, rotated.
        var d = Items('d', 2);
        Assert.Equal("<fis a cis>", Insert(d, "F#m"));
        Assert.Equal("<fis a cis>", Insert(d, "IIIm"));
        Assert.Equal("<a cis e g>", Insert(d, "V7"));

        var aMinor = Items('a', 0);
        Assert.Equal("<a c e>", Insert(aMinor, "Im"));
        Assert.Equal("<e g b d>", Insert(aMinor, "Vm7"));
        Assert.DoesNotContain("IIm", aMinor.Select(i => i.Label));
    }

    [Fact]
    public void FlatSpelling_FollowsTheSetting()
    {
        // E-flat major, full: ees; contracted: es (and aes → as), everything else as is.
        Assert.Equal("<ees g bes>", Insert(Items('e', -3), "Eb"));
        Assert.Equal("<es g bes>", Insert(Items('e', -3, contracted: true), "Eb"));
        Assert.Equal("<as c es g>", Insert(Items('e', -3, contracted: true), "Abmaj7"));
    }

    /// <summary>
    /// The list, as the editor shows it on Ctrl+Space (owner, 2026-09-02): the pitches in
    /// the KEY'S SCALE ORDER, then the chord names in scale order with triad, 7th, sus4,
    /// sus2 per root, then the degrees in the same shape, then everything else.
    /// </summary>
    /// <remarks>
    /// ⚠️ BOTH ORDERS ARE ASSERTED: VS Code sorts by <c>sortText</c>, and a client that
    /// ignores it takes the list as emitted — the two must agree (RomanChordCompletionTests
    /// pins the same property for the chords block).
    /// </remarks>
    [Fact]
    public void DMajor_ListsThePitchesThenTheNamesThenTheDegrees_EachInScaleOrder()
    {
        var items = Items('d', 2);
        var emitted = items.Select(i => i.Label!).ToArray();
        var sorted = items.OrderBy(i => i.SortText, System.StringComparer.Ordinal)
            .Select(i => i.Label!).ToArray();

        string[] head =
        {
            "d", "e", "fis", "g", "a", "b", "cis",
            "D", "Dmaj7", "Dsus4", "Dsus2", "Em", "Em7", "Esus4", "Esus2",
            "F#m", "F#m7", "F#sus4", "F#sus2", "G", "Gmaj7", "Gsus4", "Gsus2",
            "A", "A7", "Asus4", "Asus2", "Bm", "Bm7", "Bsus4", "Bsus2",
            "C#dim", "C#m7-5", "C#sus4", "C#sus2",
            "I", "Imaj7", "Isus4", "Isus2", "IIm", "IIm7", "IIsus4", "IIsus2",
            "IIIm", "IIIm7", "IIIsus4", "IIIsus2", "IV", "IVmaj7", "IVsus4", "IVsus2",
            "V", "V7", "Vsus4", "Vsus2", "VIm", "VIm7", "VIsus4", "VIsus2",
            "VIIdim", "VIIm7-5", "VIIsus4", "VIIsus2",
        };
        Assert.Equal(head, sorted.Take(head.Length).ToArray());
        // …the emit order is the same list over this whole block (the rests behind it
        // have always been emitted r s R and sorted R r s — not this block's concern)…
        Assert.Equal(head, emitted.Take(head.Length).ToArray());
        // …and the rests come after all of that.
        Assert.True(System.Array.IndexOf(sorted, "r") > head.Length - 1);
    }

    [Fact]
    public void NoKey_ListsThePitchesFromC()
    {
        var sorted = Items('c', 0).OrderBy(i => i.SortText, System.StringComparer.Ordinal)
            .Select(i => i.Label!).ToArray();
        Assert.Equal(new[] { "c", "d", "e", "f", "g", "a", "b", "C" }, sorted.Take(8).ToArray());
    }

    [Fact]
    public void ATypedDiatonicSymbol_IsNotOfferedTwice()
    {
        // `dm7` typed in C major: the diatonic rows (the name and its degree) already insert
        // <d f a c>, so the typed-word expansion ("dm7  →  <d f a c>") stands down; a chord
        // OUTSIDE the key still expands.
        var inKey = LilySharpLanguageServer.GetMusicCompletions("dm7", 0, false, false, 'c').Items;
        Assert.Equal(new[] { "Dm7", "IIm7" },
            inKey.Where(i => i.InsertText == "<d f a c>").Select(i => i.Label).ToArray());
        var outOfKey = LilySharpLanguageServer.GetMusicCompletions("besm7", 0, false, false, 'c').Items;
        Assert.Contains(outOfKey, i => i.InsertText == "<bes des f aes>" && i.Label!.StartsWith("besm7"));
    }

    /// <summary>
    /// Every item the list offers must parse where it would be inserted, in several keys —
    /// the net RomanChordCompletionTests carries for the chords block, here for music.
    /// </summary>
    [Theory]
    [InlineData('c', 0)]
    [InlineData('a', 0)]
    [InlineData('e', -3)]
    [InlineData('f', 3)]
    public void EveryOfferedChordCompilesWhereItIsInserted(char tonic, int sharps)
    {
        string key = (tonic, sharps) switch
        {
            ('c', 0) => "c major", ('a', 0) => "a minor", ('e', -3) => "ees major", ('f', 3) => "fis minor",
            _ => throw new System.ArgumentException("unmapped key"),
        };
        // The chord rows are plain Values whose insert is a note chord; the `<< >>` arpeggio
        // SNIPPET also starts with '<' and carries a tab stop, so it is not one of them.
        var chords = Items(tonic, sharps)
            .Where(i => i.Kind == CompletionItemKind.Value && i.InsertText is { } t && t.StartsWith('<'))
            .ToArray();
        Assert.NotEmpty(chords);
        foreach (var item in chords)
        {
            string src = $$"""
                time 4/4
                key {{key}}
                part m { clef treble }
                section A { m { {{item.InsertText}}4 {{item.InsertText}}2. | } }
                form main { A }
                score main { staff m }
                """;
            var tree = SyntaxTree.Parse(src);
            Assert.False(tree.HasErrors,
                $"'{item.Label}' → {item.InsertText} does not parse in {key}: "
                + string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        }
    }

    // ── end to end: the real Completion path resolves the key from the document ──

    private static CompletionItem[] CompletionAt(string text, int offset)
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
        return list?.Items ?? System.Array.Empty<CompletionItem>();
    }

    [Fact]
    public void InASectionsMusic_TheKeyOfTheDocumentDecidesTheChords()
    {
        const string doc = """
            time 4/4
            key d major
            part m { clef treble }
            section A {
              m {
            """;
        var items = CompletionAt(doc, doc.Length);
        Assert.Equal("<d fis a>", Insert(items, "D"));
        Assert.Equal("<fis a cis>", Insert(items, "F#m"));
        Assert.Equal("<e g b d>", Insert(items, "IIm7"));
        // …and the pitch rows are still there beside them, in the key's scale order,
        // through the real Completion path (the tonic travelled with the signature).
        var sorted = items.OrderBy(i => i.SortText, System.StringComparer.Ordinal)
            .Select(i => i.Label!).ToArray();
        Assert.Equal(new[] { "d", "e", "fis", "g", "a", "b", "cis", "D", "Dmaj7", "Dsus4", "Dsus2", "Em" },
            sorted.Take(12).ToArray());
    }

    [Fact]
    public void AMidPieceKeyChange_MovesTheChords()
    {
        // ⚠️ The caret is a bar AFTER the change, not right behind `major`: directly after
        // the mode word the context is still the key declaration (mode completion).
        const string doc = """
            time 4/4
            key c major
            part m { clef treble }
            section A {
              m { c4 d e f | key g major g4 a b c' |

            """;
        var items = CompletionAt(doc, doc.Length);
        Assert.Equal("<g b d>", Insert(items, "G"));
        Assert.Equal("<d fis a c>", Insert(items, "V7"));
    }
}
