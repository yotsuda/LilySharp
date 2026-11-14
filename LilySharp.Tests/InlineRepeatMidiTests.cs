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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A repeat sign, a volta ending or a navigation mark written in a section's MUSIC is the
/// score's, and the MIDI plays it: every part follows it, whichever part writes it (a chord row
/// too), and a <c>|:</c> pairs with a <c>:|</c> wherever the form puts it — in the same
/// section, in a later one, or in the form itself (docs/inline-repeat-design.md §3.2, §3.3;
/// <see cref="PlayedTimeline"/>). MEASURED before (2026-11-14, Lab sessions/p1243/stage0.md):
/// only the part that wrote the sign repeated, a pair cut by a section boundary did not
/// repeat at all, a chord row's sign repeated nothing and no jump was followed.
/// </summary>
/// <remarks>⚠️ These books still report LYS1034 (the signs belong in the form) — the rule is
/// lifted last, when every output reads them (the design's §4). The MIDI is written all the
/// same, and that is what is read here.</remarks>
[Trait("Category", "Unit")]
public sealed class InlineRepeatMidiTests
{
    // A whole note a bar, at a pitch that names it: A c'' (72), B g'' (79), C e''' (88), D a'' (81).
    private const string A = "c''1", B = "g''1", C = "e'''1", D = "a''1";

    private static MidiFile Export(string src) => new MidiExporter().Export(SyntaxTree.Parse(src));

    /// <summary>The bars one part plays, in order, as their letters (an octave lower reads the same).</summary>
    private static string Played(string src, string part = "m") =>
        string.Concat(Export(src).Tracks.SelectMany(t => t.Notes).Where(n => n.Part == part)
            .OrderBy(n => n.StartTick)
            .Select(n => (n.Pitch % 12) switch { 0 => 'A', 7 => 'B', 4 => 'C', 9 => 'D', _ => '?' }));

    private static string OneSection(string music) => $$"""
        octave absolute
        part m { section S { {{music}} } }
        form { S }
        score { staff m }
        """;

    [Theory]
    [InlineData($"{A} |: {B} :| {C} |", "ABBC")]
    [InlineData($"{A} |: {B} :|*3 {C} |", "ABBBC")]
    [InlineData($"|: {A} | [1. {B} :| [2. {C} ] | {D} |", "ABACD")]
    [InlineData($"|: {A} | [1-2. {B} :| [3. {C} ] | {D} |", "ABABACD")]
    [InlineData($"|: {A} | [1. {B} :| [2. {C} :| [3. {D} ] |", "ABACAD")]
    [InlineData($"|: {A} :|: {B} :| {C} |", "AABBC")]
    [InlineData($"{A} | {B} :| {C} |", "ABABC")]                    // a ':|' no '|:' opened: from the beginning
    public void ARepeatWrittenInTheMusicIsPlayed(string music, string played)
        => Assert.Equal(played, Played(OneSection(music)));

    [Theory]
    [InlineData($"{A} | segno {B} | to coda {C} ds al coda | coda {D} |", "ABCBD")]
    [InlineData($"{A} | {B} fine | {C} dc al fine |", "ABCAB")]
    [InlineData($"{A} | segno {B} | {C} ds |  {D} |", "ABCBCD")]
    public void AJumpWrittenInTheMusicIsFollowed(string music, string played)
        => Assert.Equal(played, Played(OneSection(music)));

    /// <summary>The sign is the score's: the part that writes none plays the same bars.</summary>
    [Fact]
    public void EveryPartFollowsASignOnePartWrites()
    {
        string src = $$"""
            octave absolute
            part m { section S { |: {{A}} | [1. {{B}} :| [2. {{C}} ] | {{D}} | } }
            part b { section S { c1 | g1 | e1 | a1 | } }
            form { S }
            score { staff m staff b }
            """;
        Assert.Equal("ABACD", Played(src, "m"));
        Assert.Equal("ABACD", Played(src, "b"));
    }

    /// <summary>A chord row's sign too — the shape of a chart typed from paper, its repeats in
    /// the row of symbols.</summary>
    [Fact]
    public void EveryPartFollowsASignAChordRowWrites()
    {
        string src = $$"""
            octave absolute
            part m { section S { {{A}} | {{B}} | {{C}} | } }
            chords prog { section S { C |: G :| Am | } }
            form { S }
            score { chords prog staff m }
            """;
        Assert.Equal("ABBC", Played(src));
    }

    /// <summary>A chord row writes an ending and a jump as a part's music does: until
    /// 2026-11-14 its '[' was LYS1028 ("'[' is none of these") and so was 'segno'.</summary>
    [Theory]
    [InlineData("C |: G | [1. Am :| [2. F ] | C |", "ABCBDA")]
    [InlineData("C | segno G | Am to coda | F ds al coda | coda C |", "ABCDBCA")]
    public void AChordRowWritesEndingsAndJumps(string row, string played)
    {
        string src = $$"""
            octave absolute
            part m { section S { {{A}} | {{B}} | {{C}} | {{D}} | {{A}} | } }
            chords prog { section S { {{row}} } }
            form { S }
            score { chords prog staff m }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.Equal(src, tree.GetRoot().ToFullString());
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == LilySharp.Core.Syntax.DiagnosticCodes.ChordBlockBadMember);
        Assert.Equal(played, Played(src));
    }

    /// <summary>The pair is read on the sequence the form assembles (owner's decision
    /// 2026-11-14): a section need not close what it opens.</summary>
    [Theory]
    [InlineData("P Q", "ABCBCD")]        // '|:' in P, ':|' in Q
    [InlineData("P R :|", "ABCBC")]      // '|:' in P, the form's own ':|' closes it
    [InlineData("|: X :| P Q", "AAABCBCD")] // the form's repeat beside the music's
    public void APairMayBeCutByASectionBoundary(string form, string played)
    {
        string src = $$"""
            octave absolute
            part m {
              section P { {{A}} |: {{B}} | }
              section Q { {{C}} :| {{D}} | }
              section R { {{C}} | }
              section X { {{A}} | }
            }
            form { {{form}} }
            score { staff m }
            """;
        Assert.Equal(played, Played(src));
    }

    /// <summary>The bar lines are the played ones: a bar played three times is three bars.</summary>
    [Fact]
    public void TheBarLinesAreUnfoldedWithTheNotes()
    {
        var midi = Export(OneSection($"{A} |: {B} :|*3 {C} |"));
        int bar = midi.TicksPerQuarterNote * 4;
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }.Select(k => k * bar), midi.BarTicks);
    }
}
