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
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Split Section Here (owner's request 2026-11-17): a section cut in two at a bar line, the rest a new
/// section after it, the forms playing both — and the piece sounding as it did.
/// </summary>
public class SectionCutterTests
{
    private static string Sound(string source)
        => string.Join(" ", new MidiExporter().Export(SyntaxTree.Parse(source)).Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => $"{n.Part}:{n.StartTick}:{n.Pitch}:{n.DurationTicks}"));

    private static string Cut(string source, string section, int bars, string name)
    {
        var r = SectionCutter.Cut(source, section, bars, name);
        Assert.True(r.NewText != null, r.Error);
        Assert.Equal(Sound(source), Sound(r.NewText!));
        return r.NewText!;
    }

    private const string Song = """
        time 4/4
        part bass { instrument bass }
        section A {
          chords harmony { C | F | G | C | break }
          bass { c4 e g e | f a c a | g, b d b | c1 | }
        }
        form { A ~A }
        score { chords harmony  staff bass }
        """;

    [Fact]
    public void ASection_IsCutAtTheCaretsBar_AndTheFormsPlayBoth()
    {
        var tree = SyntaxTree.Parse(Song);
        int caret = Song.IndexOf("| g, b") + 1;
        Assert.Equal(("A", 2), SectionCutter.PlaceAt(tree, caret));
        Assert.Equal("A2", SectionCutter.SuggestName(tree, "A"));
        string text = Cut(Song, "A", 2, "A2");
        Assert.True(text.Contains("form { A A2 ~A ~A2 }"), text);
        Assert.True(text.Contains("chords harmony { C | F | }"), text);
        Assert.True(text.Contains("section A2 {"), text);
        Assert.True(text.Contains("chords harmony { G | C | break }"), text);
    }

    /// <summary>The second half opens a fresh frame and the 4: its first pitch is re-marked and the
    /// running note value written where it relied on it.</summary>
    [Fact]
    public void TheFrameAndTheNoteValue_ArePutBack()
    {
        const string src = """
            time 4/4
            part a { clef treble }
            section A { a { c''8 d e f g a b c | d e f g a b c d | } }
            form { A }
            score { staff a }
            """;
        string text = Cut(src, "A", 1, "B");
        Assert.True(text.Contains("section B { a { d'''8 e f g"), text);
    }

    [Fact]
    public void AMeterChangedInTheFirstHalf_IsRestated()
    {
        const string src = """
            time 4/4
            part a { clef treble }
            section A { a { c'1 | time 3/4 d2. | e2. | } }
            form { A }
            score { staff a }
            """;
        string text = Cut(src, "A", 2, "B");
        // (the meter restated, and e re-marked: after d it was the e above, from a fresh frame the one below)
        Assert.True(text.Contains("section B { a { time 3/4 e'2. | } }"), text);
    }

    [Fact]
    public void ATakenName_AndAnInlineRepeat_AreRefused()
    {
        Assert.Contains("already", SectionCutter.Cut(Song, "A", 2, "A").Error);
        const string rep = """
            time 4/4
            part a { clef treble }
            section A { a { c'1 | |: d1 | e1 :| f1 | } }
            form { A }
            score { staff a }
            """;
        Assert.Contains("inline repeat", SectionCutter.Cut(rep, "A", 1, "B").Error);
    }

    /// <summary>A run of bar repeats cut in its middle (`d8 … | % | % | %`, one node with its bar
    /// lines inside — My Life's bass): the bars are counted inside it, and the second half opens with
    /// the repeated bar written out.</summary>
    [Fact]
    public void ARunOfBarRepeats_IsCutInside_TheBarWrittenOut()
    {
        const string src = """
            time 4/4
            part bass { instrument bass }
            section A {
              chords harmony { D | D | D | D | }
              bass { d8 d' d, d' d, d' d, d' | % | % | % | }
            }
            form { A }
            score { chords harmony  staff bass }
            """;
        string text = Cut(src, "A", 2, "B");
        Assert.True(text.Contains("bass { d8 d' d, d' d, d' d, d' | % | }"), text);
        Assert.True(Regex.IsMatch(text, @"section B \{\s*chords harmony \{ D \| D \| \}\s*bass \{ d'?8 d' d, d' d, d' d, d' \| % \| \}"), text);
    }

    /// <summary>The lightbulb offers the cut on a bar line, just after one, and on a <c>break</c> /
    /// <c>pageBreak</c> (owner's request 2026-11-17) — nowhere else inside the music.</summary>
    [Theory]
    [InlineData("| pageBreak", 0, true)]           // on the bar line after F
    [InlineData("| pageBreak", 1, true)]           // just after it
    [InlineData("pageBreak", 4, true)]             // in the middle of the page break
    [InlineData("break }", 2, false)]              // the section's last break: nothing after it to cut off
    [InlineData("g, b", 1, false)]                 // inside a bar
    public void TheLightbulb_OffersTheCut_OnABarLineOrABreak(string near, int into, bool offered)
    {
        string text = Song.Replace("C | F | G | C | break", "C | F | pageBreak G | C | break");
        int at = text.IndexOf(near, System.StringComparison.Ordinal) + into;
        Assert.True(at >= into, near);
        var server = new LilySharp.Lsp.LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///cut-here.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharp.Lsp.LilySharpLanguageServer.GetLineAndCharacter(text, at);
        var actions = server.GetCodeActions(new LilySharp.Lsp.Protocol.CodeActionParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = new LilySharp.Lsp.Protocol.Position(line, character), End = new LilySharp.Lsp.Protocol.Position(line, character) },
        }) ?? [];
        Assert.Equal(offered, actions.Any(a => a.Title.StartsWith("Split section A here", System.StringComparison.Ordinal)));
    }

    /// <summary>The preview's right-click hands a bar's first note (before it) or the end of its text
    /// with Snap (after it, on its bar line) — owner's decision 2026-11-17, the menus' reorganisation.</summary>
    [Fact]
    public void ThePreviewsPlace_CutsBeforeOrAfterTheBar()
    {
        var server = new LilySharp.Lsp.LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///cut-preview.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem { Uri = uri, Text = Song, LanguageId = "lilysharp", Version = 1 },
        });
        LilySharp.Lsp.CutSectionResponse Ask(int offset, bool snap) => server.CutSectionAsync(new LilySharp.Lsp.CutSectionParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri }, Offset = offset, Snap = snap,
        }, System.Threading.CancellationToken.None).Result;
        // Before bar 3 (the bass's g,): 2 bars before the cut.
        Assert.Equal(("A", 2), (Ask(Song.IndexOf("g, b"), false).Section, Ask(Song.IndexOf("g, b"), false).Bars));
        // After bar 3: the end of its text, snapped on past its bar line.
        int end = Song.IndexOf("d b") + "d b".Length;
        var after = Ask(end, true);
        Assert.Equal(("A", 3), (after.Section, after.Bars));
        Assert.Equal("A2", after.Suggested);
    }

    [Fact]
    public void CutThenJoined_IsTheSameMusic()
    {
        string cut = Cut(Song, "A", 3, "Z");
        var joined = SectionJoiner.Join(cut, ["A", "Z"]);
        Assert.True(joined.NewText != null, joined.Error);
        Assert.Equal(Sound(Song), Sound(joined.NewText!));
    }
}
