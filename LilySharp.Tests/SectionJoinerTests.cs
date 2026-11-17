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
using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Join Sections (owner's request 2026-11-17): sections declared one after another, played in a row
/// by every form, become one — the first's name, each cell's music run on — and the piece sounds
/// as it did. What the boundary reset is written out at each join; what cannot be is refused.
/// </summary>
public class SectionJoinerTests
{
    private static string Sound(string source)
        => string.Join(" ", new MidiExporter().Export(SyntaxTree.Parse(source)).Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => $"{n.Part}:{n.StartTick}:{n.Pitch}:{n.DurationTicks}"));

    private static string Joined(string source, params string[] names)
    {
        var r = SectionJoiner.Join(source, names);
        Assert.True(r.NewText != null, r.Error);
        Assert.Equal(Sound(source), Sound(r.NewText!));
        return r.NewText!;
    }

    [Fact]
    public void TwoSections_BecomeOne_AndTheFormPlaysIt()
    {
        const string src = """
            time 4/4
            part bass { instrument bass }
            section A {
              chords harmony { C | F | }
              bass { c4 e g e | f a c a | }
            }
            section B {
              chords harmony { G | C | }
              bass { g, b d b | c1 | }
            }
            form { A B A }
            score { chords harmony  staff bass }
            """;
        var r = SectionJoiner.Join(src, ["A", "B"]);
        Assert.Null(r.NewText);   // A plays alone at the end: B does not always follow it
        Assert.Contains("without B after it", r.Error);

        string text = Joined(src.Replace("form { A B A }", "form { A B }"), "A", "B");
        Assert.DoesNotContain("section B", text);
        Assert.True(text.Contains("form { A }"), text);
        Assert.True(text.Contains("chords harmony { C | F | G | C | }"), text);
    }

    /// <summary>Relative octaves: the later section's first pitch opened a fresh frame; joined, it
    /// runs on from the earlier one's last pitch, so it is re-marked where it would move.</summary>
    [Fact]
    public void TheRelativeFrame_IsPutBack()
    {
        const string src = """
            time 4/4
            part a { clef treble }
            section A { a { c''4 d e f | g a b c | } }
            section B { a { c'4 d e f | } }
            form { A B }
            score { staff a }
            """;
        string text = Joined(src, "A", "B");
        // (c' after the c'' that ended A: two octaves down from the c an unmarked c would be)
        Assert.True(text.Contains("g a b c | c,,4 d e f |"), text);
    }

    [Fact]
    public void AFirstNoteWithNoLength_TakesTheFourASectionOpensWith()
    {
        const string src = """
            time 4/4
            part a { clef treble }
            section A { a { c'2 d | } }
            section B { a { e f g a | } }
            form { A B }
            score { staff a }
            """;
        string text = Joined(src, "A", "B");
        // (and re-marked: after d, an e opening a fresh frame was the one below)
        Assert.True(text.Contains("c'2 d | e,4 f g a |"), text);
    }

    [Fact]
    public void APartAMiddleSectionLacks_RestsItsBars_AndAMeterChangeIsWrittenAtTheJoin()
    {
        const string src = """
            time 4/4
            part a { clef treble }
            part b { clef bass }
            section A { a { c'1 | d | } b { c1 | c | } }
            section B { time 3/4 a { e2. | } }
            section C { a { f1 | } b { f1 | } }
            form { A B C }
            score { staff a  staff b }
            """;
        string text = Joined(src, "A", "B", "C");
        Assert.True(text.Contains("R*1 |"), text);
        Assert.True(text.Contains("time 3/4"), text);
        Assert.True(text.Contains("time 4/4"), text);   // back to the score's for C
    }

    [Fact]
    public void AnOpenBarBeforeAJoin_IsRefused()
    {
        const string src = """
            time 4/4
            part a { clef treble }
            section A { a { c'1 | d4 } }
            section B { a { e1 | } }
            form { A B }
            score { staff a }
            """;
        Assert.Contains("ends inside a bar", SectionJoiner.Join(src, ["A", "B"]).Error);
    }

    [Fact]
    public void WordsAndChordsRunOn_AndThreeSectionsJoin()
    {
        const string src = """
            time 4/4
            part v { clef treble }
            section A { chords h { C | } v { c'2 d | } lyrics w { la la | } }
            section B { chords h { F | } v { e2 f | } lyrics w { lo lo | } }
            section C { chords h { G | } v { g1 | } lyrics w { lu | } }
            form { A B C }
            score { chords h  staff v  lyrics w on v }
            """;
        string text = Joined(src, "A", "B", "C");
        Assert.True(text.Contains("chords h { C | F | G | }"), text);
        Assert.True(!text.Contains("section C"), text);
    }
}
