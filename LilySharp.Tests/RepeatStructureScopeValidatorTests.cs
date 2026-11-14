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
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Repeat structure — <c>|:</c>, <c>:|</c>, <c>:|:</c>, a volta ending <c>[1. …]</c> and a
/// navigation mark — is refused in a <c>phrase</c> and nowhere else (LYS1034, narrowed
/// 2026-11-15; from 2026-08-31 to that day it was legal in a <c>form</c> alone).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE "MUST NOT CATCH" ROWS ARE NOT DECORATION. The section's music, the chord row and the
/// form are the three places that write a sign, and each row here was a refusal until the rule
/// narrowed — so each is the net for "the rule did narrow". Two of the three miscounts that
/// preceded the 2026-08-31 rule were a lyric verse header read as a repeat ending, so the lyric
/// rows are pinned here too rather than trusted to the node types staying distinct.
/// </para>
/// <para>
/// ⚠️ THE LAST THEORY measures the two quantities the 2026-08-31 move to the form nearly
/// dropped on the floor — a third volta ending, and an explicit <c>:|*N</c> play count
/// reaching the MIDI. The form still says both.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class RepeatStructureScopeValidatorTests
{
    private static IReadOnlyList<Diagnostic> Reports(string source)
    {
        var validator = new RepeatStructureScopeValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.RepeatStructureInPhrase
                        && d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    /// <summary>A phrase holds no sign: it is played wherever it is named, so a sign in it
    /// would stand in a different repeat each time. One row per node type the rule asks.</summary>
    [Theory]
    // a repeat barline, both sides and the back-to-back divider
    [InlineData("part m { clef treble }\nphrase T { |: c'4 d e f | }\n"
        + "section A { m { T } }\nform { ~A }\nscore { staff m }\n", 1)]
    [InlineData("part m { clef treble }\nphrase T { |: c'4 d e f :|: g4 a b c' :|*3 }\n"
        + "section A { m { T } }\nform { ~A }\nscore { staff m }\n", 3)]
    // a volta ending
    [InlineData("part m { clef treble }\nphrase T { c'4 d e f | [1. g2 g | ] }\n"
        + "section A { m { T } }\nform { ~A }\nscore { staff m }\n", 1)]
    // a navigation mark, one word and three
    [InlineData("part m { clef treble }\nphrase T { c'4 d e f | segno g2 g | ds al coda }\n"
        + "section A { m { T } }\nform { ~A }\nscore { staff m }\n", 2)]
    public void RepeatStructureInAPhrase_IsRefused(string book, int expected)
        => Assert.Equal(expected, Reports(book).Count);

    /// <summary>The message names the place that does take the sign — where the phrase is
    /// played — since "not here" alone leaves the writer where the form-only rule did.</summary>
    [Fact]
    public void TheRefusalNamesWhereTheSignGoes()
    {
        var report = Assert.Single(Reports("part m { clef treble }\nphrase T { c'4 d e f :| }\n"
            + "section A { m { T } }\nform { ~A }\nscore { staff m }\n"));
        Assert.Contains("':|'", report.Message);
        Assert.Contains("where the phrase is played", report.Message);
    }

    /// <summary>What the rule leaves alone: every place a sign IS written, and each spelling
    /// that looks like a sign and is not.</summary>
    [Theory]
    // a by-part section's music
    [InlineData("part m { clef treble\n  section A { |: c''4 d e f | :| }\n}\n"
        + "form { ~A }\nscore { staff m }\n")]
    // a by-section part block, with the phrase played INSIDE the repeat the music writes
    [InlineData("part m { clef treble }\nphrase T { c'4 d e f | }\n"
        + "section A { m { |: T :| } }\nform { ~A }\nscore { staff m }\n")]
    // a `chords` row
    [InlineData("time 4/4\nsection A { chords p { C Am |: F G7 | C :| } }\n"
        + "form { ~A }\nscore { chords p }\n")]
    // endings, the back-to-back divider and the marks, all in the music
    [InlineData("part m { clef treble }\n"
        + "section A { m { |: c''4 d e f | [1. g2 g :| [2. a2 a | ] } }\n"
        + "form { ~A }\nscore { staff m }\n")]
    [InlineData("part m { clef treble }\nsection A { m { |: c''4 d e f :|: g4 a b c' :| } }\n"
        + "form { ~A }\nscore { staff m }\n")]
    [InlineData("part m { clef treble }\nsection A { m { c''4 d e f | segno g4 a b c' | ds al fine } }\n"
        + "form { ~A }\nscore { staff m }\n")]
    // the form: repeat and both endings
    [InlineData("part m { clef treble }\nsection A { m { c''4 d e f | } }\n"
        + "section B { m { g2 g | } }\nsection C { m { a2 a | } }\n"
        + "form { |: A [1. ~B] :| [2. ~C] }\nscore { staff m }\n")]
    // a `:|` and a `:|:` standing loose in a form body DO make BarlineSyntax nodes
    [InlineData("part m { clef treble }\nsection A { m { c''4 d e f | } }\n"
        + "section B { m { g2 g | } }\nform { A :| B :|: A }\nscore { staff m }\n")]
    // the repeats that abbreviate notes, in a phrase too
    [InlineData("part m { clef treble }\nphrase T { c'4 d e f | % }\n"
        + "section A { m { c''4 d e f | c'4 d e f | g4 g g g | % T } }\n"
        + "form { ~A }\nscore { staff m }\n")]
    // a LYRIC verse header is the words for the Nth pass, not an ending
    [InlineData("octave absolute\ntime 4/4\nkey c major\n"
        + "part m { clef treble\n  section A { c''4 d' e' d' | }\n}\n"
        + "lyrics w {\n  section A {\n    verse 1 { Twin- kle twin- kle | }\n"
        + "    verse 2 { How I won- der | }\n  }\n}\n"
        + "form { ~A }\nscore { staff m  lyrics w on m }\n")]
    // a lyric row's barline is a raw token inside LyricMeasureGreen, never a BarlineSyntax —
    // so this rule cannot see it, and a lyric row plays nothing whose order could change.
    [InlineData("octave absolute\ntime 4/4\nkey c major\n"
        + "part m { clef treble\n  section A { c'4 d' e' d' | c'4 e' g'2 | }\n}\n"
        + "lyrics w { section A { Twin- kle twin- kle |: lit- tle star | } }\n"
        + "form { ~A }\nscore { staff m  lyrics w on m }\n")]
    public void SpellingsOutsideAPhrase_AreLeftAlone(string book)
        => Assert.Empty(Reports(book));

    /// <summary>
    /// The form can say what the music used to say. Both rows measure a quantity that the
    /// form dropped until 2026-08-31 and that had no other spelling left once the ban landed.
    /// </summary>
    /// <remarks>
    /// ⚠️ MEASURED AGAINST THE MUSIC SPELLING BEFORE IT WAS REMOVED, on the same music:
    /// a three-ending repeat sounded X E1 X E2 X E3 (six notes here), and `:|*3` sounded the
    /// body three times (24 notes). The form spelling gave five and sixteen — the third pass
    /// skipped its body, and the play count never left `FormWalk`.
    /// </remarks>
    [Theory]
    [InlineData("|: ~X [1. ~E1] :| [2. ~E2] :| [3. ~E3]", new[] { 72, 74, 72, 76, 72, 77 })]
    [InlineData("|: ~X :|*3", new[] { 72, 72, 72 })]
    public void AFormSaysWhatTheMusicSpellingSaid(string formBody, int[] expectedPitches)
    {
        string book = "octave absolute\ntime 4/4\npart v { }\n"
            + "section X { v { c''1 | } }\n"
            + "section E1 { v { d''1 | } }\n"
            + "section E2 { v { e''1 | } }\n"
            + "section E3 { v { f''1 | } }\n"
            + "form { " + formBody + " }\nscore { staff ~v }\n";
        var pitches = new MidiExporter().Export(SyntaxTree.Parse(book))
            .Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch).ToArray();
        Assert.Equal(expectedPitches, pitches);
    }
}
