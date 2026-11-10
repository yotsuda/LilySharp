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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Verses are <c>verse N { … }</c> (owner's design 2026-11-01: "the lyrics rules are hard to
/// follow; `[1.` is unclear"). Three rules: one track is one line; words with no <c>verse</c>
/// are sung every time; <c>verse N</c> is the words for the Nth time, placed by the form — the
/// number is a LABEL, never the line. (The former <c>[N. … ]</c> was read with an error until
/// 2026-11-08, when the owner dropped that reading: <c>[</c> means nothing in lyrics, and
/// <c>[1.</c> is now words like any other.)
/// (<c>LyricVerseTests</c> is the older net for a block wrapping into stacked verses.)
/// </summary>
[Trait("Category", "Unit")]
public sealed class LyricVerseBlockTests
{
    // A sung twice, written out; the verses must land at their own printing, on line 1.
    private static readonly string WrittenOut = """
        time 4/4
        octave absolute
        part melody { clef treble  section A { c'4 d' e' f' | g'4 a' b' c'' | } }
        lyrics w {
          section A {
            verse 1 { Twin- kle twin- kle | lit- tle star | }
            verse 2 { How I won- der | what you are | }
          }
        }
        form { A A "A2" }
        score { staff melody  lyrics w on melody }
        """.ReplaceLineEndings("\n");

    private const string Verse1 = "verse 1 { Twin- kle twin- kle | lit- tle star | }";

    [Fact]
    public void AVerseBlock_Parses_AndTheWordIsContextual()
    {
        var tree = SyntaxTree.Parse(WrittenOut);
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(2, tree.GetRoot().DescendantNodes().Count(n => n.Kind == SyntaxKind.LyricVerse));
        // `verse` as a syllable, and as a track's name, stay what they were.
        var plain = SyntaxTree.Parse("""
            part m { section A { c'4 d' e' f' | } }
            lyrics verse { section A { a verse 1 of | } }
            form { A }
            score { staff m  lyrics verse on m }
            """);
        Assert.Empty(plain.Diagnostics);
        Assert.Empty(plain.GetRoot().DescendantNodes().Where(n => n.Kind == SyntaxKind.LyricVerse));
    }

    [Fact]
    public void ABracket_IsNoVerse_TheBracketIsAReadingsInLyrics()
    {
        // `[` is a READING's bracket in a lyrics block since 2026-11-08 (LyricReadingTests): the
        // old `[1. … ]` is no verse — `[1.` is a reading with no word before it and `]` a stray
        // bracket, each an error on its syllable (LYS4036) — and the words print as written. (A
        // `]` AFTER the last `|` would open a measure of its own that runs on into `verse 2 {`,
        // as any stray word there does: a verse starts only at a measure's head.)
        string book = WrittenOut.Replace(Verse1, "[1. Twin- kle twin- kle | lit- tle star ] |");
        var tree = SyntaxTree.Parse(book);
        Assert.Equal(2, tree.Diagnostics.Count(d => d.Code == DiagnosticCodes.LyricReadingMalformed));
        Assert.Equal(2, tree.Diagnostics.Count());
        Assert.Single(tree.GetRoot().DescendantNodes().Where(n => n.Kind == SyntaxKind.LyricVerse));   // verse 2 alone
        var score = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "w" });
        Assert.Contains(score.Lyrics, l => l.Text == "[1." && l.MeasureIndex == 0);
        Assert.Contains(score.Lyrics, l => l.Text == "star" && l.MeasureIndex == 1);
        Assert.Contains(score.Lyrics, l => l.Text == "]");
    }

    [Fact]
    public void AWrittenOutSecondPlay_TakesTheFirstLine_LabelledWithItsNumber()
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(WrittenOut), "melody", attachedLyricParts: new[] { "w" });
        var how = score.Lyrics.Single(l => l.Text == "How");
        Assert.Equal(2, how.MeasureIndex);
        Assert.Equal(1, how.VerseNumber);     // the first line under the staff…
        Assert.Equal(2, how.StanzaLabel);     // …numbered "2."
        var twin = score.Lyrics.First(l => l.Text == "Twin");
        Assert.Equal(1, twin.VerseNumber);
        Assert.Equal(1, twin.StanzaLabel);
        // Nothing on a second line anywhere: the number never chose the line.
        Assert.All(score.Lyrics, l => Assert.Equal(1, l.VerseNumber));
    }

    [Fact]
    public void TheStanzaNumbers_StandBeforeEachPrinting_OnOneSystem()
    {
        var g = RenderedGeometry.Render(WrittenOut);
        var numbers = g.Texts.Where(t => t.Role == TextRole.Stanza).OrderBy(t => t.X).ToList();
        Assert.Equal(new[] { "1.", "2." }, numbers.Select(t => t.Text).ToArray());
        // Both on ONE baseline (one line), the "2." mid-line, just left of "How".
        Assert.Equal(numbers[0].Y, numbers[1].Y, 6);
        var how = g.LyricSyllables.Single(s => s.Text == "How");
        var star = g.LyricSyllables.Single(s => s.Text == "star");
        Assert.True(star.X < numbers[1].X && numbers[1].X < how.X && how.X - numbers[1].X < 7.0,
            $"\"2.\" at {numbers[1].X:F3} should stand between \"star\" at {star.X:F3} and \"How\" at {how.X:F3}");
    }

    /// <summary>The number printed mid-line takes room: the previous verse's last syllable
    /// and the next verse's first are rodded apart by the number and its padding (the
    /// owner's song printed "2." over the "る" of "かさねる"). A one-bar section sung twice:
    /// verse 1's last "kle" closes bar 1 and verse 2's "How" opens bar 2, a bar line apart,
    /// where only the cross-bar rod can make the room.</summary>
    [Fact]
    public void AMidLineStanzaNumber_IsGivenRoom_BeforeItsSyllable()
    {
        var g = RenderedGeometry.Render(WrittenOut
            .Replace("section A { c'4 d' e' f' | g'4 a' b' c'' | }", "section A { c'4 d' e' f' | }")
            .Replace("verse 1 { Twin- kle twin- kle | lit- tle star | }", "verse 1 { Twin- kle twin- kle | }")
            .Replace("verse 2 { How I won- der | what you are | }", "verse 2 { How I won- der | }"));
        var two = g.Texts.Single(t => t.Role == TextRole.Stanza && t.Text == "2.");
        var kle = g.LyricSyllables.Where(s => s.Text == "kle" && s.X < two.X).OrderByDescending(s => s.X).First();
        double kleRight = kle.X + TextFontMetrics.Advance(kle.Text, kle.FontSize) / 2;   // centred on its X
        Assert.True(two.X >= kleRight - 1e-6,
            $"\"2.\" at {two.X:F3} starts under \"kle\" ending at {kleRight:F3}");
        Assert.Equal(kle.Y, two.Y, 6);
    }

    /// <summary>A verse running on to the next line says its number once, where it begins, written
    /// out or stacked (owner's reports 2026-11-01, 風の道, 明日へつなぐ声).</summary>
    [Fact]
    public void AVerseSaysItsNumberOnce_WrittenOutOrStacked()
    {
        const string oneBarALine = "paper { measuresPerSystem 1 }\n";
        var once = RenderedGeometry.Render(oneBarALine + WrittenOut);
        Assert.Equal(new[] { "1.", "2." },
            once.Texts.Where(t => t.Role == TextRole.Stanza).Select(t => t.Text).OrderBy(t => t).ToArray());
        var stacked = RenderedGeometry.Render(oneBarALine + WrittenOut.Replace("form { A A \"A2\" }", "form { |: A :| }"));
        Assert.Equal(new[] { "1.", "2." },
            stacked.Texts.Where(t => t.Role == TextRole.Stanza).Select(t => t.Text).OrderBy(t => t).ToArray());
    }

    /// <summary>Words with no <c>verse</c> have no number, though they share a system with verses
    /// that stack (owner's report 2026-11-01, 明日へつなぐ声 M9: "1." before あしたへ).</summary>
    [Fact]
    public void PlainWordsBesideNumberedVerses_HaveNoNumber()
    {
        string song = """
            time 4/4
            octave absolute
            part melody { clef treble }
            section A {
              melody { c'4 d' e' f' | }
              lyrics w { verse 1 { up up up up | } verse 2 { down down down down | } }
            }
            section B {
              melody { g'4 a' b' c'' | }
              lyrics w { plain words sung here | }
            }
            form { |: A :| B }
            score { staff melody  lyrics w on melody }
            """.ReplaceLineEndings("\n");
        var numbers = RenderedGeometry.Render(song).Texts.Where(t => t.Role == TextRole.Stanza).Select(t => t.Text).OrderBy(t => t).ToArray();
        Assert.Equal(new[] { "1.", "2." }, numbers);
    }

    [Fact]
    public void UnderARepeat_TheVersesStillStack_EachWithItsNumber()
    {
        var g = RenderedGeometry.Render(WrittenOut.Replace("form { A A \"A2\" }", "form { |: A :| }"));
        var numbers = g.Texts.Where(t => t.Role == TextRole.Stanza).OrderBy(t => t.Y).ToList();
        Assert.Equal(new[] { "1.", "2." }, numbers.Select(t => t.Text).ToArray());
        Assert.True(numbers[1].Y > numbers[0].Y + 1.0, "verse 2 stacks under verse 1");
        // The line's numbers share a RIGHT edge; "1." and "2." differ in width by a digit.
        Assert.InRange(System.Math.Abs(numbers[0].X - numbers[1].X), 0, 0.1);
    }
}
