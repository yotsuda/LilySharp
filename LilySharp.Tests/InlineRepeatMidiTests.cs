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

    /// <summary>The notes one part plays, each as its letter and the bars it sounds for.</summary>
    private static string Held(string src, string part = "m") =>
        string.Join(' ', Export(src).Tracks.SelectMany(t => t.Notes).Where(n => n.Part == part)
            .OrderBy(n => n.StartTick)
            .Select(n => (n.Pitch % 12) switch { 0 => "A", 7 => "B", 4 => "C", 9 => "D", _ => "?" } + n.DurationTicks / (4 * 480)));

    /// <summary>A tie is carried to the note PLAYED next, over the jump a sign in the music makes
    /// — the rule a form's repeat keeps (<c>|: A [1. B] :| [2. C]</c>; owner's decision
    /// 2026-09-28). MEASURED before (2026-11-14, Lab sessions/p1245/stage4.md): every note was cut
    /// at the jump, so the tie out of the body sounded into the first ending and struck the
    /// second again (B2 … B1 B1), and a tie out of the first ending back to the body was lost.</summary>
    [Theory]
    // Out of the body into both endings.
    [InlineData($"|: {A} | {B}~ | [1. {B} :| [2. {B} ] | {D} |", "A1 B2 A1 B2 D1")]
    // Out of the first ending, back to the body's first note; the second ending is another pitch.
    [InlineData($"|: {A} | {B} | [1. {A}~ :| [2. {C} ] | {D} |", "A1 B1 A2 B1 C1 D1")]
    // Out of the body: back to its first note on the first pass, on into the next bar on the last.
    [InlineData($"|: {B} | {A} | {B}~ :| {B} | {D} |", "B1 A1 B2 A1 B2 D1")]
    // Where the note played next is another pitch the tie ends at the jump.
    [InlineData($"|: {A} | {B}~ :| {B} | {D} |", "A1 B1 A1 B2 D1")]
    // A tie the page prints from one ending into the next joins nothing: the second is reached from the body.
    [InlineData($"|: {A} | [1. {B}~ :| [2. {B} ] | {D} |", "A1 B1 A1 B1 D1")]
    // Over a jump text.
    [InlineData($"segno {A} | {B} | {A}~ ds al fine | {A} fine |", "A1 B1 A2 B1 A2")]
    public void ATieIsCarriedToTheNotePlayedNext(string music, string held)
        => Assert.Equal(held, Held(OneSection(music)));

    /// <summary>A <c>|:</c> at the head of the music closes nothing, so the bar after it is still
    /// the section's opening bar and its declared pickup stands. MEASURED before (2026-11-14, Lab
    /// sessions/p1245/stage4.md): the pickup was forgotten behind the sign and the bar was told
    /// to declare one (LYS2006), with the header's <c>partial</c> right above it.</summary>
    [Theory]
    [InlineData("|: g'4 | c d e f :| c1 |", 0)]
    [InlineData("g'4 | c d e f | c1 |", 0)]
    [InlineData("| g'4 | c d e f | c1 |", 1)] // a plain bar line has passed the pickup, empty
    public void ARepeatSignAtTheHeadKeepsTheDeclaredPickup(string music, int shortBars)
    {
        var validator = new LilySharp.Core.Semantics.MeasureValidator();
        validator.Validate(SyntaxTree.Parse($$"""
            time 4/4
            section S { partial 4 }
            part m { section S { {{music}} } }
            form { S }
            score { staff m }
            """));
        Assert.Equal(shortBars, validator.Diagnostics.Count(d => d.Code is "LYS2001" or "LYS2006"));
    }

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

    /// <summary>A row that opens with <c>|:</c> has the bars it writes: the <c>|:</c> closes
    /// none (the page's and the MIDI's reading of a row). Until 2026-11-14 the bar COUNT read
    /// it as a bar of its own, and the section was laid out a bar longer than the music beside it.</summary>
    [Fact]
    public void ARowOpeningWithARepeatSignCountsTheBarsItWrites()
    {
        string src = $$"""
            octave absolute
            part m { section S { {{A}} | {{B}} | {{C}} | } }
            chords prog { section S { |: C | G :| Am | } }
            form { S }
            score { chords prog staff m }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.DoesNotContain(LilySharp.Core.Semantics.SemanticValidation.Run(tree),
            d => d.Message.Contains("not the same length"));
        Assert.Equal("ABABC", Played(src));
    }

    /// <summary>The page brackets a row's endings as it brackets a part's: one bracket an
    /// ending, over the bars it holds, hooked where a <c>]</c> closes it — beside a staff, and
    /// on a lead sheet of the row alone. Until 2026-11-14 a row could not write one.</summary>
    [Theory]
    [InlineData("part m { section S { c''1 | g''1 | e'''1 | a''1 | c''1 | } }\nchords prog { section S { C |: G | [1. Am :| [2. F ] | C | } }\nform { S }\nscore { chords prog staff m }")]
    [InlineData("chords prog { C |: G | [1. Am :| [2. F ] | C | }\nscore { chords prog }")]
    public void ThePageBracketsARowsEndings(string book)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\n" + book);
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .CollectMultiStaff(tree, LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!);
        Assert.Equal(new[] { (2, 2, "1.", false), (3, 3, "2.", true) },
            score.VoltaBrackets.OrderBy(v => v.StartMeasureIndex)
                .Select(v => (v.StartMeasureIndex, v.EndMeasureIndex, v.VoltaText, v.IsClosed)).ToArray());
    }

    /// <summary>A row's <c>| |:</c> is an empty bar, as its <c>| |</c> is and as a part's is
    /// (owner's decision 2026-11-14) — and the count, the page, the MIDI, the MusicXML and the
    /// twin read it by one rule (<c>ChordRowBarLines</c>). Until then five of the row's readers
    /// made two bars of <c>C | |: G</c> and three made three.</summary>
    [Fact]
    public void ARowsBarLineBeforeARepeatSignIsAnEmptyBar()
    {
        string src = $$"""
            octave absolute
            part m { section S { {{A}} | {{B}} | {{C}} | {{D}} | } }
            chords prog { section S { C | |: G :| Am | } }
            form { S }
            score { chords prog staff m }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.DoesNotContain(LilySharp.Core.Semantics.SemanticValidation.Run(tree),
            d => d.Message.Contains("not the same length"));
        Assert.Equal("ABCCD", Played(src));                 // the repeat is the THIRD bar
    }

    /// <summary>A chart with no section at all — a chords track written flat, the one shape a
    /// file without sections has — reads the same marks.</summary>
    [Fact]
    public void AFlatChordsTrackWritesEndingsToo()
    {
        const string src = """
            time 4/4
            chords prog { C |: G | [1. Am :| [2. F ] | C | }
            score { chords prog }
            """;
        // The roots' pitch classes, a bar each: C G Am G F C.
        var bars = Export(src).Tracks.SelectMany(t => t.Notes).GroupBy(n => n.StartTick).OrderBy(g => g.Key)
            .Select(g => string.Join(",", g.Select(n => n.Pitch % 12).OrderBy(p => p)));
        Assert.Equal(new[] { "0,4,7", "2,7,11", "0,4,9", "2,7,11", "0,5,9", "0,4,7" }, bars);
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

    /// <summary>The words of a section that repeats (owner's rules 2026-11-14,
    /// docs/inline-repeat-design.md §6): <c>verse N</c> is the repeat's Nth pass, a pass past the
    /// verses written sings the last one, and a section the form plays again sings the same
    /// words again. MEASURED before (Lab sessions/p1243/probe/p-lyr-inline): a repeat written
    /// in the music sang verse 1 on every pass, where the same repeat written in the form sang
    /// verse 2 on the second.</summary>
    [Theory]
    [InlineData("|: c''1 :|", "S", "one two")]                 // the repeat's Nth pass
    [InlineData("|: c''1 :|*3", "S", "one two two")]           // a pass past the verses: the last one
    [InlineData("|: c''1 :|", "S S", "one two one two")]       // played again by the form: the same words
    [InlineData("c''1 |", "|: S :|", "one two")]               // the form's repeat, as before
    [InlineData("c''1 |", "|: S :|*3", "one two two")]         // …and its pass past the verses sings the last one too
    public void AVerseIsThePassOfTheRepeat(string music, string form, string sung)
    {
        string src = $$"""
            octave absolute
            time 4/4
            part m { section S { {{music}} } }
            lyrics w { section S { verse 1 { one | } verse 2 { two | } } }
            form { {{form}} }
            score { staff m  lyrics w on m }
            """;
        Assert.Equal(sung, string.Join(" ", Export(src).Tracks.SelectMany(t => t.Lyrics).OrderBy(l => l.Tick).Select(l => l.Text)));
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
