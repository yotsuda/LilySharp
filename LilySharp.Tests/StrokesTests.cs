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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>strokes NAME { … }</c> part (owner's design 2026-11-01: a song to sing and play yourself
/// whose chords sounded only on beat 1): a rhythm of slashes and no pitch, placed as a one-line
/// staff by <c>strokes NAME</c> in the score or by <c>chords NAME plays STROKES</c> under a chords
/// row, which then STRIKES the row's chords at the strokes in the MIDI. A row without
/// <c>plays</c> keeps holding each chord (a keyboard's reading, the owner's choice that day).
/// </summary>
[Trait("Category", "Unit")]
public class StrokesTests
{
    private const int Bar = 1920;

    // ⚠️ A raw string takes the FILE's line endings (CRLF here), so the "\n" the Replace calls
    // below look for would never match — normalised once (the CRLF trap of HANDOFF §1.1 第1036).
    private static readonly string Song = Raw.ReplaceLineEndings("\n");

    private const string Raw = """
        time 4/4
        octave absolute
        part singer { instrument alto }
        strokes rhythm { section A { 4 8 8 4 4 | % | } }
        chords harmony { section A { C | G | } }
        section A {
          singer { c'4 d'4 e'4 f'4 | g'1 | }
          lyrics words sings singer { a b c d | e | }
          lyrics strum sings rhythm { ka e ri mi chi | % | }
        }
        form { A }
        score {
          staff singer
          lyrics words
          chords harmony plays rhythm
          strokes rhythm
          lyrics strum
        }
        """;

    private static SyntaxTree Parse(string lys) => SyntaxTree.Parse(lys);

    private static (int Start, int Length)[] ChordStrikes(MidiFile midi, string row = "harmony")
        => midi.Tracks.Where(t => t.Name == row + " (chords)").SelectMany(t => t.Notes)
            .GroupBy(n => (n.StartTick, n.DurationTicks)).Select(g => g.Key).OrderBy(s => s.StartTick).ToArray();

    /// <summary>The bossa nova's strokes the composing guide gives (the owner's bossa.lys, 2026-11-01):
    /// the row strikes the batida, its chords on the 1st, 4th, 7th eighths and the 3rd and 6th of the
    /// next bar, <c>%2</c> going round.</summary>
    [Fact]
    public void TheGuidesBossaNovaStrokes_GoRoundInTwoBars()
    {
        const string book = """
            time 4/4
            part singer { instrument alto }
            strokes bossa { section A { 4. 4. 4 | r4 4 r8 4. | %2 | %2 | } }
            chords harmony { section A { E | A | B | E | E | A | } }
            section A { singer { e'1 | e'1 | e'1 | e'1 | e'1 | e'1 | } }
            form { A }
            score { chords harmony plays bossa  staff singer }
            """;
        var tree = Parse(book.ReplaceLineEndings("\n"));
        Assert.False(tree.HasErrors);
        int eighth = Bar / 8;
        var strikes = ChordStrikes(new MidiExporter().Export(tree)).Select(s => s.Start / eighth).Distinct().ToArray();
        int[] twoBars = [0, 3, 6, 10, 13];
        Assert.Equal(twoBars.Concat(twoBars.Select(x => x + 16)).Concat(twoBars.Select(x => x + 32)).ToArray(), strikes);
    }

    private static (int Start, int Length)[] StrikesOf(string strokes, string chords)
    {
        string book = $$"""
            time 4/4
            part singer { instrument alto }
            strokes rhythm { section A { {{strokes}} } }
            chords harmony { section A { {{chords}} } }
            section A { singer { e'1 | e'1 | } }
            form { A }
            score { chords harmony plays rhythm  staff singer }
            """;
        var tree = Parse(book.ReplaceLineEndings("\n"));
        Assert.False(tree.HasErrors);
        return ChordStrikes(new MidiExporter().Export(tree));
    }

    /// <summary>A tied stroke is one stroke (the owner's page, 2026-11-01: a batida tied over the bar
    /// line struck again where the tie landed): the chord is not struck at the tie's end, and sounds
    /// on through it. <c>~</c> stands after a stroke as a sibling, as after a note, unless a mark
    /// follows it, so both spellings are held here.</summary>
    [Theory]
    [InlineData("4 4 4 8 8~ | 4 2. |")]
    [InlineData("/4 /4 /4 /8 /8~ | /4 /2. |")]
    public void ATiedStroke_IsNotStruckAgain_AndSoundsOnThroughTheTie(string strokes)
    {
        int quarter = Bar / 4, eighth = Bar / 8;
        Assert.Equal(
            [(0, quarter), (quarter, quarter), (2 * quarter, quarter), (3 * quarter, eighth),
             (3 * quarter + eighth, eighth + quarter), (Bar + quarter, 3 * quarter)],
            StrikesOf(strokes, "C | C |"));
    }

    /// <summary>Each stroke of a chord is told from the others (the preview's loop took them for a
    /// repeat and kept the first, owner's report 2026-11-01), counted again from each symbol.</summary>
    [Fact]
    public void EachStrokeOfAChord_IsNumbered_FromEachSymbol()
    {
        const string book = """
            time 4/4
            part singer { instrument alto }
            strokes rhythm { section A { 4. 4. 4 | r4 4 r8 4. | } }
            chords harmony { section A { Am7 | D7 | } }
            section A { singer { e'1 | e'1 | } }
            form { A }
            score { chords harmony plays rhythm  staff singer }
            """;
        var notes = new MidiExporter().Export(Parse(book.ReplaceLineEndings("\n")))
            .Tracks.Single(t => t.Name == "harmony (chords)").Notes;
        var strikes = notes.GroupBy(n => n.StartTick).OrderBy(g => g.Key).Select(g => g.Select(n => n.Strike).Distinct().Single());
        Assert.Equal([0, 1, 2, 0, 1], strikes);
    }

    /// <summary>A bar of <c>.</c> is the row's silent slot (the owner's decision 2026-09-04; a
    /// <c>.</c> never crosses a bar line), so a stroke in it sounds nothing, and a tie stops at it.</summary>
    [Fact]
    public void ASilentBar_SoundsNoStroke()
    {
        int half = Bar / 2;
        Assert.Equal([(0, half), (half, half)], StrikesOf("2 2~ | 2 2 |", "C | . |"));
    }

    /// <summary>A tied stroke over a chord change sounds only the chord it was struck in, stopping
    /// where that ends; the next chord waits for its own stroke (as the guide says).</summary>
    [Fact]
    public void ATiedStroke_StopsWhereItsChordEnds()
    {
        int quarter = Bar / 4, eighth = Bar / 8;
        var last = StrikesOf("2. 8 8~ | 4 2. |", "C | G |");
        Assert.Equal([(0, 3 * quarter), (3 * quarter, eighth), (3 * quarter + eighth, eighth), (Bar + quarter, 3 * quarter)], last);
    }

    /// <summary>A rest between a tie and the next stroke breaks the tie: that stroke is struck.</summary>
    [Fact]
    public void ARestAfterATie_LeavesTheNextStrokeStruck()
    {
        int quarter = Bar / 4;
        Assert.Equal(
            [(0, quarter), (2 * quarter, 2 * quarter), (Bar, Bar)],
            StrikesOf("4~ r4 2 | 1 |", "C | C |"));
    }

    [Fact]
    public void AStrokesPart_ParsesAsAPart_ThatIsStrokes()
    {
        var tree = Parse(Song);
        var parts = tree.GetRoot().DescendantNodes<PartDeclarationSyntax>().ToList();
        Assert.True(parts.Single(p => p.Name.Text == "rhythm").IsStrokes);
        Assert.False(parts.Single(p => p.Name.Text == "singer").IsStrokes);
        Assert.Empty(tree.Diagnostics);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Owner's design 2026-11-01, the second step: "the first pitch of a strokes section is a
    /// slash", so a strokes part writes values alone — <c>4 8 8 4 4</c> — each a slash note whose
    /// <c>/</c> is unwritten (a zero-width token), in a by-part body and in a section's cell
    /// alike, with <c>/4</c> still read as itself. Elsewhere a leading bare value is still
    /// LYS0016.
    /// </summary>
    [Theory]
    [InlineData("strokes rhythm { section A { 4 8. 16 4 4 | } }\nsection A { singer { c'1 | } }")]
    [InlineData("strokes rhythm { }\nsection A { singer { c'1 | } rhythm { 4 8. 16 4 4 | } }")]
    [InlineData("section A { singer { c'1 | } rhythm { 4 8. 16 4 4 | } }\nstrokes rhythm { }")]
    [InlineData("strokes rhythm { section A { /4 8. 16 4 4 | } }\nsection A { singer { c'1 | } }")]
    public void ABareValueInAStrokesPart_IsAStroke(string body)
    {
        var tree = Parse("part singer\n" + body + "\nform { A }\nscore { staff singer  strokes rhythm }");
        Assert.Empty(tree.Diagnostics);
        Assert.Empty(SemanticValidation.Run(tree));
        var strokes = tree.GetRoot().DescendantNodes<SlashNoteSyntax>().ToList();
        Assert.Equal(5, strokes.Count);
        Assert.Empty(tree.GetRoot().DescendantNodes<BareDurationSyntax>());
        Assert.Equal([(4, 0), (8, 1), (16, 0), (4, 0), (4, 0)], strokes.Select(s => (s.Duration!.Value, s.Duration.DotCount)));
        Assert.Equal(body, tree.ToFullString().Substring("part singer\n".Length, body.Length));
        // The singer's bare value is still a repeat with nothing to repeat.
        var pitched = Parse("part singer\nsection A { singer { 4 8 8 4 4 | } }\nform { A }\nscore { staff singer }");
        Assert.Contains(SemanticValidation.Run(pitched), d => d.Code == DiagnosticCodes.DetachedDuration);
    }

    [Fact]
    public void AddingTheStrokesDeclaration_RereadsAnUnchangedCell()
    {
        // The score's own `strokes rhythm` item is no declaration: before the declaration is
        // written, the cell's values are bare durations (Parser.StrokesNamesOf reads depth 0).
        const string cell = "section A { singer { c'1 | } rhythm { 4 8 8 4 4 | } }\nform { A }\nscore { staff singer  strokes rhythm }\n";
        var before = Parse("part singer\n" + cell);
        Assert.Equal(5, before.GetRoot().DescendantNodes<BareDurationSyntax>().Count());
        // The edit touches the first line only; every other member is unchanged text, which an
        // incremental reparse would adopt as parsed — with the cell's values as bare durations.
        var after = before.WithChanges(new TextChange(new TextSpan("part singer\n".Length, 0), "strokes rhythm { }\n"));
        Assert.Equal(5, after.GetRoot().DescendantNodes<SlashNoteSyntax>().Count());
        Assert.Empty(after.GetRoot().DescendantNodes<BareDurationSyntax>());
        Assert.Equal(after.ToFullString(), SyntaxTree.Parse(after.ToFullString()).ToFullString());
    }

    [Fact]
    public void APitchInAStrokesPart_IsRefused()
    {
        var tree = Parse("""
            part singer
            strokes rhythm { section A { /4 c'8 8 4 4 | <c e g>1 | } }
            section A { singer { c'1 | c'1 | } }
            form { A }
            score { staff singer  strokes rhythm }
            """);
        var errors = SemanticValidation.Run(tree).Where(d => d.Code == DiagnosticCodes.StrokesHoldNoPitch).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains("'rhythm' is a strokes part and holds no pitches", errors[0].Message);
    }

    [Fact]
    public void AChordsRowThatPlaysStrokes_OverTheStrokesItem_FoldsIntoItsOneLineStaff()
    {
        var spec = RenderSpecParser.FindFirst(Parse(Song))!;
        // Each row is folded into the staff beside it (RenderSpecParser.FoldAdjacentRows): the
        // words under the singer, the chords row and the strum's words round the strokes' staff.
        Assert.Equal(2, spec.Items.Length);
        var staff = Assert.IsType<SingleStaffSpec>(spec.Items[1]).Staff;
        Assert.Equal("rhythm", staff.VoiceName);
        Assert.Equal(1, staff.Lines);
        Assert.True(staff.NameSuppressed);
        Assert.Equal("rhythm", staff.ChordPlays);
        Assert.Equal("harmony", staff.WithChords);
        Assert.Equal("strum", Assert.Single(staff.WithLyrics));
    }

    /// <summary>Owner's decision 2026-11-01: <c>plays</c> says how the row SOUNDS and draws
    /// nothing — the one-line staff is the score's own <c>strokes NAME</c> item.</summary>
    [Fact]
    public void PlaysAlone_DrawsNoStaff_AndStillStrikes()
    {
        string song = Song.Replace("  strokes rhythm\n", "").Replace("  lyrics strum\n", "");
        var spec = RenderSpecParser.FindFirst(Parse(song))!;
        // The row under the singer's words stays a row, with its `plays`; no staff of the
        // strokes is added.
        Assert.Equal(2, spec.Items.Length);
        Assert.Equal("singer", Assert.IsType<SingleStaffSpec>(spec.Items[0]).Staff.VoiceName);
        Assert.Equal("rhythm", Assert.IsType<ChordRowSpec>(spec.Items[1]).Plays);
        Assert.Equal(10, ChordStrikes(new MidiExporter().Export(Parse(song))).Length);
        // The twin writes every part's variable (the strokes' with \improvisationOn) but
        // places no staff for the strokes in the score.
        string ly = new LilyPondExporter().Export(Parse(song));
        Assert.DoesNotContain("line-count", ly);
        Assert.DoesNotContain("\\rhythmVar", ly.Substring(ly.IndexOf("\\score", StringComparison.Ordinal)));
        // …and the row above the singer's staff, folded into it, strikes the same.
        string above = Song.Replace("  staff singer\n  lyrics words\n  chords harmony plays rhythm\n  strokes rhythm\n  lyrics strum\n",
            "  chords harmony plays rhythm\n  staff singer\n  lyrics words\n");
        Assert.Equal(10, ChordStrikes(new MidiExporter().Export(Parse(above))).Length);
        var aboveItems = RenderSpecParser.FindFirst(Parse(above))!.Items;
        Assert.Equal("rhythm", Assert.Single(aboveItems.OfType<ChordRowSpec>()).Plays);
        Assert.DoesNotContain(aboveItems, i => i is SingleStaffSpec { Staff.VoiceName: "rhythm" });
    }

    [Fact]
    public void AStrokesItemInTheScore_IsAOneLineStaff_WithNoName()
    {
        var spec = RenderSpecParser.FindFirst(Parse("""
            part singer
            strokes rhythm { section A { /4 4 4 4 | } }
            section A { singer { c'1 | } }
            form { A }
            score { staff singer  strokes rhythm }
            """))!;
        var staff = Assert.IsType<SingleStaffSpec>(spec.Items[1]).Staff;
        Assert.Equal(1, staff.Lines);
        Assert.True(staff.NameSuppressed);
        Assert.Null(staff.ChordPlays);
    }

    [Fact]
    public void ARowThatPlaysStrokes_StrikesItsChordAtEachStroke()
    {
        var midi = new MidiExporter().Export(Parse(Song));
        Assert.Equal(new[]
        {
            (0, 480), (480, 240), (720, 240), (960, 480), (1440, 480),
            (Bar, 480), (Bar + 480, 240), (Bar + 720, 240), (Bar + 960, 480), (Bar + 1440, 480),
        }, ChordStrikes(midi));
        // The singer still sings: the by-part strokes declared above the by-section section did
        // not take its place as the section's body (MeasureCollector.Definitions, 2026-11-01).
        Assert.Equal(5, midi.Tracks.Single(t => t.Name == "singer").Notes.Count);
    }

    [Fact]
    public void ARowWithoutPlays_HoldsEachChord()
    {
        var midi = new MidiExporter().Export(Parse(Song.Replace(" plays rhythm", "")));
        Assert.Equal(new[] { (0, Bar), (Bar, Bar) }, ChordStrikes(midi));
    }

    [Fact]
    public void TheTwin_WritesTheStrokesAsAOneLineStaffOfSlashes()
    {
        string ly = new LilyPondExporter().Export(Parse(Song));
        Assert.Contains("\\override StaffSymbol.line-count = #1", ly);
        Assert.Contains("\\improvisationOn", ly);
        Assert.Contains("\\new ChordNames \\harmonyChords", ly);
    }

    [Fact]
    public void ACompositionCheck_DoesNotAskAnInstrumentOfAStrokesPart()
    {
        var findings = CompositionChecks.Of(Song.Replace("part singer { instrument alto }", "part singer { instrument alto }\npart bass { instrument bass }")
            .Replace("singer { c'4 d'4 e'4 f'4 | g'1 | }", "singer { c'4 d'4 e'4 f'4 | g'1 | } bass { c2 g | c1 | }")
            .Replace("staff singer\n", "staff singer\n  staff bass\n"));
        Assert.DoesNotContain(findings, f => f.StartsWith("No instrument for"));
    }
}
