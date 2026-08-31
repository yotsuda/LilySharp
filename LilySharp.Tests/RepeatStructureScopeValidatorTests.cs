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
/// Repeat structure — <c>|:</c>, <c>:|</c>, <c>:|:</c> and a volta ending <c>[1. …]</c> —
/// is legal only inside a <c>form</c> (LYS1034, user decision 2026-08-31).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE TWO HALVES ARE ONE TEST. Refusing the music spelling is worth nothing on its own:
/// what makes the rule a MOVE rather than a deletion is that the form can say each thing the
/// music used to say. So the refusals and the form spellings sit in the same file, and the
/// third theory measures the two quantities the move nearly dropped on the floor — a third
/// volta ending, and an explicit <c>:|*N</c> play count reaching the MIDI.
/// </para>
/// <para>
/// ⚠️ THE "MUST NOT CATCH" ROWS ARE NOT DECORATION. Two of the three miscounts that preceded
/// this rule were a lyric verse header <c>[1. …]</c> read as a repeat ending, so the lyric
/// rows are pinned here rather than trusted to the node types staying distinct.
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
            .Where(d => d.Code == DiagnosticCodes.RepeatStructureOutsideForm
                        && d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    /// <summary>The four shapes the tree's books spread this across, plus the chord row.
    /// One predicate has to reach all of them, which is why they are listed as one theory
    /// rather than tested where each is parsed.</summary>
    [Theory]
    // inside a phrase
    [InlineData("part m { clef treble }\nphrase T { |: c'4 d e f | }\n"
        + "section A { m { T } }\nform main { ~A }\nscore main { staff m }\n", 1)]
    // inside a part-major section
    [InlineData("part m { clef treble\n  section A { |: c'4 d e f | :| }\n}\n"
        + "form main { ~A }\nscore main { staff m }\n", 2)]
    // inside a section-major part block
    [InlineData("part m { clef treble }\nsection A { m { |: c'4 d e f | :| } }\n"
        + "form main { ~A }\nscore main { staff m }\n", 2)]
    // inside a `chords` row — a repeat is a repeat wherever it is written
    [InlineData("time 4/4\nsection A { chords p { C Am |: F G7 | C :| } }\n"
        + "form main { ~A }\nscore main { chords p }\n", 2)]
    // an inline volta ending, and the back-to-back divider
    [InlineData("part m { clef treble }\n"
        + "section A { m { |: c'4 d e f | [1. g2 g | ] :| [2. a2 a | ] } }\n"
        + "form main { ~A }\nscore main { staff m }\n", 4)]
    [InlineData("part m { clef treble }\nsection A { m { |: c'4 d e f | :|: g4 a b c' | :| } }\n"
        + "form main { ~A }\nscore main { staff m }\n", 3)]
    public void RepeatStructureInMusic_IsRefused(string book, int expected)
        => Assert.Equal(expected, Reports(book).Count);

    /// <summary>What the rule must leave alone. Each row is a spelling that LOOKS like the
    /// banned one and is not.</summary>
    [Theory]
    // the legal spelling: repeat and both endings in the form
    [InlineData("part m { clef treble }\nsection A { m { c'4 d e f | } }\n"
        + "section B { m { g2 g | } }\nsection C { m { a2 a | } }\n"
        + "form main { |: A [1. ~B] :| [2. ~C] }\nscore main { staff m }\n")]
    // a `:|` and a `:|:` standing loose in a form body DO make BarlineSyntax nodes — these
    // are the rows that fail if the ancestor test is dropped.
    [InlineData("part m { clef treble }\nsection A { m { c'4 d e f | } }\n"
        + "section B { m { g2 g | } }\nform main { A :| B :|: A }\nscore main { staff m }\n")]
    // the repeats that stay in music: they abbreviate notes, they do not reorder them
    [InlineData("part m { clef treble }\n"
        + "section A { m { repeat unfold 2 { c'4 d e f | } repeat percent 2 { g4 g g g | } } }\n"
        + "form main { ~A }\nscore main { staff m }\n")]
    // a LYRIC verse header is the words for the Nth pass, not an ending
    [InlineData("octave absolute\ntime 4/4\nkey c major\n"
        + "part m { clef treble\n  section A { c'4 d' e' d' | }\n}\n"
        + "lyrics w sings m {\n  section A {\n    [1. Twin- kle twin- kle | ]\n"
        + "    [2. How I won- der | ]\n  }\n}\n"
        + "form main { ~A }\nscore main { staff m  lyrics w }\n")]
    // a lyric row's barline is a raw token inside LyricMeasureGreen, never a BarlineSyntax —
    // so this rule cannot see it, and a lyric row plays nothing whose order could change.
    [InlineData("octave absolute\ntime 4/4\nkey c major\n"
        + "part m { clef treble\n  section A { c'4 d' e' d' | c'4 e' g'2 | }\n}\n"
        + "lyrics w sings m { section A { Twin- kle twin- kle |: lit- tle star | } }\n"
        + "form main { ~A }\nscore main { staff m  lyrics w }\n")]
    public void SpellingsThatAreNotAMusicRepeat_AreLeftAlone(string book)
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
            + "section X { v { c'1 | } }\n"
            + "section E1 { v { d'1 | } }\n"
            + "section E2 { v { e'1 | } }\n"
            + "section E3 { v { f'1 | } }\n"
            + "form main { " + formBody + " }\nscore main { staff ~v }\n";
        var pitches = new MidiExporter().Export(SyntaxTree.Parse(book))
            .Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch).ToArray();
        Assert.Equal(expectedPitches, pitches);
    }
}
