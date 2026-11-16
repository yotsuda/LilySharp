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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chord row's PICKUP bar is gridded on the beats the pickup has — the meter's last ones
/// (<see cref="ChordRhythm.BeatsOf"/>) — by every reader of the row: the page, its check
/// (LYS2009), the sound, the MusicXML and the LilyPond twin. Until 2026-11-14 the page and the
/// twin gridded the pickup on the whole meter and scaled it, and the sound and the MusicXML
/// gridded it on the whole meter and cut it (Dettato's request 3, Lab sessions/p1242/dettato):
/// three chords over three beats of pickup in 4/4 were warned off the grid and written to the
/// MusicXML a third of a bar apart, and the second of two chords over two beats neither
/// sounded nor was written.
/// </summary>
[Trait("Category", "Unit")]
public class ChordRowPickupGridTests
{
    private const int Quarter = 480;

    /// <summary>The places a score puts a chord track's row: alone (a lead sheet, no staff),
    /// above a staff, below one, and between two — the last is folded onto the staff below it,
    /// the track ON a staff.</summary>
    public enum Shape { LeadSheet, RowAboveStaff, RowBelowStaff, RowBetweenStaves }

    private static string Book(Shape shape, string partial, string pickupMusic, string pickupRow, string time = "4/4", string barMusic = "c''1",
        string barRow = "F")
    {
        bool music = shape != Shape.LeadSheet;
        // A row with a staff above it and one below is FOLDED onto the one below
        // (RenderSpecParser.FoldAdjacentRows): the track ON a staff, another walk of the collector.
        bool two = shape == Shape.RowBetweenStaves;
        string head = $"time {time}\n" + (music ? "part melody\n" : "") + (two ? "part upper\n" : "");
        string Music(string m) => (music ? $"melody {{ {m} | }} " : "") + (two ? $"upper {{ {m} | }} " : "");
        string pickup = $"section P {{ partial {partial} {Music(pickupMusic)}chords harmony {{ {pickupRow} | }} }}\n";
        string bar = $"section A {{ {Music(barMusic)}chords harmony {{ {barRow} | }} }}\n";
        string score = shape switch
        {
            Shape.LeadSheet => "score { chords harmony }",
            Shape.RowAboveStaff => "score { chords harmony  staff melody }",
            Shape.RowBetweenStaves => "score { staff upper  chords harmony  staff melody }",
            _ => "score { staff melody  chords harmony }",
        };
        return head + pickup + bar + "form { P A }\n" + score + "\n";
    }

    private static (string Text, int Measure, double Timing)[] Chords(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, src);
        // The render path's own collect: it takes the single-staff walk where the score is one.
        return LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree))
            .ChordNames.OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble())
            .Select(c => (c.ChordText, c.MeasureIndex, c.Timing.ToDouble())).ToArray();
    }

    private static Diagnostic[] GridFaults(string src)
        => SemanticValidation.Run(SyntaxTree.Parse(src)).Where(x => x.Code == DiagnosticCodes.ChordSlotMismatch).ToArray();

    // ── the grid ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void APickupsBeats_AreTheMetersLast()
    {
        static string[] Beats(int beats, int beatType, Fraction? pickup)
            => ChordRhythm.BeatsOf(beats, beatType, pickup).Select(f => $"{f.Numerator}/{f.Denominator}").ToArray();

        Assert.Equal(["1/4", "1/4", "1/4", "1/4"], Beats(4, 4, null));
        Assert.Equal(["1/4", "1/4", "1/4"], Beats(4, 4, new Fraction(3, 4)));
        // A pickup that begins inside a beat: what is left of that beat, then the whole ones.
        Assert.Equal(["1/8", "1/4"], Beats(4, 4, new Fraction(3, 8)));
        Assert.Equal(["3/8"], Beats(6, 8, new Fraction(3, 8)));
        // 5/8 is [3, 2]: its last quarter is the group of two, three eighths reach into the three.
        Assert.Equal(["1/4"], Beats(5, 8, new Fraction(2, 8)));
        Assert.Equal(["1/8", "1/4"], Beats(5, 8, new Fraction(3, 8)));
        // Not shorter than the meter's bar: no pickup.
        Assert.Equal(4, ChordRhythm.BeatsOf(4, 4, new Fraction(4, 4)).Count);
        Assert.Equal(4, ChordRhythm.BeatsOf(4, 4, new Fraction(5, 4)).Count);
    }

    [Fact]
    public void APickupsSlots_AreReadOnItsOwnBeats()
    {
        static string[]? Slots(int count, Fraction pickup, int beats = 4, int beatType = 4)
            => ChordRhythm.SlotDurations(count, beats, beatType, pickup)?.Select(f => $"{f.Numerator}/{f.Denominator}").ToArray();

        var three = new Fraction(3, 4);
        Assert.Equal(["3/4"], Slots(1, three));
        Assert.Equal(["1/4", "1/4", "1/4"], Slots(3, three));
        Assert.Equal(6, Slots(6, three)!.Length);
        // Two and four fit no beat of three, as they fit none in 3/4.
        Assert.Null(Slots(2, three));
        Assert.Null(Slots(4, three));
        Assert.Equal(["1/4", "1/4"], Slots(2, new Fraction(2, 4)));
        // The uneven pickup of 5/8: the slots are the beats, not halves of the pickup.
        Assert.Equal(["1/8", "1/4"], Slots(2, new Fraction(3, 8), 5, 8));
    }

    // ── the page and its check ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Shape.LeadSheet)]
    [InlineData(Shape.RowAboveStaff)]
    [InlineData(Shape.RowBelowStaff)]
    [InlineData(Shape.RowBetweenStaves)]
    public void ThreeChordsOverThreeBeatsOfPickup_StandOnTheBeats_AndAreNotWarned(Shape shape)
    {
        string book = Book(shape, "4*3", "c''4 d e", "C7 F G7");
        Assert.Empty(GridFaults(book));
        Assert.Equal([("C7", 0, 0.0), ("F", 0, 0.25), ("G7", 0, 0.5), ("F", 1, 0.0)], Chords(book));
    }

    [Theory]
    [InlineData(Shape.LeadSheet)]
    [InlineData(Shape.RowAboveStaff)]
    [InlineData(Shape.RowBelowStaff)]
    [InlineData(Shape.RowBetweenStaves)]
    public void TwoChordsOverTwoBeatsOfPickup_StandOnTheBeats(Shape shape)
    {
        string book = Book(shape, "2", "c''4 d", "C7 F");
        Assert.Empty(GridFaults(book));
        Assert.Equal([("C7", 0, 0.0), ("F", 0, 0.25), ("F", 1, 0.0)], Chords(book));
    }

    [Theory]
    [InlineData(Shape.LeadSheet, "C7 F", 2)]
    [InlineData(Shape.RowAboveStaff, "C7 F", 2)]
    [InlineData(Shape.RowBelowStaff, "C7 F", 2)]
    [InlineData(Shape.RowBetweenStaves, "C7 F", 2)]
    [InlineData(Shape.LeadSheet, "C7 F G7 C", 4)]
    public void SlotsThatFitNoBeatOfThePickup_AreWarned_AndThePickupIsNamed(Shape shape, string row, int slots)
    {
        string book = Book(shape, "4*3", "c''4 d e", row);
        var fault = Assert.Single(GridFaults(book));
        Assert.Equal(DiagnosticSeverity.Warning, fault.Severity);
        Assert.Contains($"{slots} chord slot(s) in a pickup of 3 beat(s) in 4/4", fault.Message);
        // …and the pickup's own length is divided equally, not the meter's bar.
        var first = Chords(book).Where(c => c.Measure == 0).Select(c => c.Timing).ToArray();
        Assert.Equal(Enumerable.Range(0, slots).Select(i => 0.75 * i / slots), first);
        // The sound and the MusicXML divide the same length (three beats, 1440 ticks).
        Assert.Equal(Enumerable.Range(0, slots).Select(i => 3 * Quarter * i / slots).Append(3 * Quarter), ChordOnsets(book));
        var written = PickupHarmonies(book, out int d);
        Assert.Equal(Enumerable.Range(0, slots).Select(i => 3 * d * i / slots), written.Select(h => h.Offset));
    }

    /// <summary>A score of ONE staff with the track named on it (<c>staff NAME with chords</c>),
    /// the staff of one voice and of two: the pickup is the staff's, however many voices
    /// sound it.</summary>
    [Theory]
    [InlineData("c''4 d e", "c''1")]
    [InlineData("voice { c''4 d e } { e'4 f g }", "voice { c''1 } { e'1 }")]
    public void ATrackOnTheOnlyStaff_ReadsThatStaffsPickup(string pickupMusic, string barMusic)
    {
        string book = $"time 4/4\npart melody\nsection P {{ partial 4*3 melody {{ {pickupMusic} | }} chords harmony {{ C7 F G7 | }} }}\n"
            + $"section A {{ melody {{ {barMusic} | }} chords harmony {{ F | }} }}\nform {{ P A }}\nscore {{ staff melody with chords harmony }}\n";
        Assert.Empty(GridFaults(book));
        Assert.Equal([("C7", 0, 0.0), ("F", 0, 0.25), ("G7", 0, 0.5), ("F", 1, 0.0)], Chords(book));
        // Two slots there fit no beat of the three, and say so of the pickup.
        Assert.Contains("pickup of 3 beat(s)", Assert.Single(GridFaults(book.Replace("C7 F G7 |", "C7 F |"))).Message);
    }

    [Fact]
    public void APickupOfAnUnevenMeter_IsGriddedOnItsOwnBeats_OnThePage()
    {
        // 5/8 beams [3, 2]; three eighths of pickup are the last eighth of the three and the two.
        // Scaled from the whole bar (until 2026-11-14) E stood at 9/40, on no eighth at all.
        string book = Book(Shape.RowAboveStaff, "8*3", "c''8 d4", "Am E", time: "5/8", barMusic: "c''4. d4");
        Assert.Empty(GridFaults(book));
        Assert.Equal([("Am", 0, 0.0), ("E", 0, 0.125), ("F", 1, 0.0)], Chords(book));
    }

    [Fact]
    public void ABarThatIsNoPickup_IsReadOnTheWholeMeter_AsBefore()
    {
        // The control: the same rows in a full bar keep the meter's grid and its warning.
        string three = "time 4/4\npart melody\nsection A { melody { c''1 | } chords harmony { C7 F G7 | } }\nform { A }\nscore { chords harmony  staff melody }\n";
        var fault = Assert.Single(GridFaults(three));
        Assert.Contains("3 chord slot(s) in a 4/4 bar", fault.Message);
        Assert.Equal([0.0, 1.0 / 3, 2.0 / 3], Chords(three).Select(c => c.Timing));
    }

    // ── the sound ───────────────────────────────────────────────────────────────────────────

    private static int[] ChordOnsets(string book)
        => new MidiExporter().Export(SyntaxTree.Parse(book)).Tracks.Where(t => t.Name == "harmony (chords)")
            .SelectMany(t => t.Notes).Where(n => n.DurationTicks > 0).Select(n => n.StartTick).Distinct().OrderBy(t => t).ToArray();

    [Theory]
    [InlineData(Shape.LeadSheet)]
    [InlineData(Shape.RowAboveStaff)]
    [InlineData(Shape.RowBelowStaff)]
    [InlineData(Shape.RowBetweenStaves)]
    public void TheSound_StrikesEveryChordOfThePickup_OnItsBeat(Shape shape)
    {
        // Two beats of pickup: C7, F a beat later, then the bar's F at the bar line.
        Assert.Equal([0, Quarter, 2 * Quarter], ChordOnsets(Book(shape, "2", "c''4 d", "C7 F")));
        // Three beats: one chord a beat.
        Assert.Equal([0, Quarter, 2 * Quarter, 3 * Quarter], ChordOnsets(Book(shape, "4*3", "c''4 d e", "C7 F G7")));
    }

    [Theory]
    [InlineData(Shape.LeadSheet)]
    [InlineData(Shape.RowAboveStaff)]
    public void OnlyThePickupBar_IsReadOnThePickupsBeats(Shape shape)
    {
        // The bar after two beats of pickup is a whole bar of the meter: four slots are its
        // four beats (on a pickup's two they would be eighths).
        string book = Book(shape, "2", "c''4 d", "C7 F", barRow: "F . . G");
        Assert.Equal([0, Quarter, 2 * Quarter, 5 * Quarter], ChordOnsets(book));
        Assert.Equal([("C7", 0, 0.0), ("F", 0, 0.25), ("F", 1, 0.0), ("G", 1, 0.75)], Chords(book));
        Assert.Empty(GridFaults(book));
    }

    [Fact]
    public void TheBarAfterThePickup_InTheSameRun_IsAWholeBarToTheSound()
    {
        // One section, its pickup and the bar after it in one run of the row: the sound reads
        // the first bar alone on the pickup's beats.
        string book = "time 4/4\npart melody\nsection A { partial 2 melody { c''4 d | c1 | } chords harmony { C7 F | F . . G | } }\n"
            + "form { A }\nscore { chords harmony  staff melody }\n";
        Assert.Equal([0, Quarter, 2 * Quarter, 5 * Quarter], ChordOnsets(book));
    }

    /// <summary>
    /// A lead sheet's bar lines are its rows' (MidiFile.BarTicks): the metronome's accents
    /// stand on them, the pickup's beats before the first unaccented. Until 2026-11-14 only a
    /// part's lane marked a bar line, so a score of rows alone had none and the metronome
    /// counted whole bars from the first beat — after three beats of pickup, every accent a beat
    /// before its bar line.
    /// </summary>
    [Fact]
    public void ALeadSheetsBarLines_AreItsRows_AndTheMetronomeAccentsThem()
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(Book(Shape.LeadSheet, "4*3", "", "C7", barRow: "F | G")));
        // The pickup's three beats, then two bars of four.
        Assert.Equal([3 * Quarter, 7 * Quarter, 11 * Quarter], midi.BarTicks.Where(t => t > 0));
        var accents = MetronomeBeats.Of(midi, Quarter, out int barEnd).Where(b => b.Accent).Select(b => b.Tick).ToArray();
        Assert.Equal([3 * Quarter, 7 * Quarter], accents);
        Assert.Equal(11 * Quarter, barEnd);

        // No pickup: the bar lines every four beats.
        var plain = new MidiExporter().Export(SyntaxTree.Parse(
            "time 4/4\nsection A { chords harmony { F | G | } }\nform { A A }\nscore { chords harmony }\n"));
        Assert.Equal([4 * Quarter, 8 * Quarter, 12 * Quarter, 16 * Quarter], plain.BarTicks.Where(t => t > 0));
    }

    [Fact]
    public void ARowsBarsBesideAStaff_AddNoBarLineOfTheirOwn()
    {
        // The control: beside a staff the bar lines are the music's, the row's the same ones.
        string book = Book(Shape.RowAboveStaff, "4*3", "c''4 d e", "C7", barRow: "F");
        var withRow = new MidiExporter().Export(SyntaxTree.Parse(book)).BarTicks.ToArray();
        var without = new MidiExporter().Export(SyntaxTree.Parse(book.Replace("score { chords harmony  staff melody }", "score { staff melody }"))).BarTicks.ToArray();
        Assert.Equal(without, withRow);
        Assert.Equal([3 * Quarter, 7 * Quarter], withRow.Where(t => t > 0));

        // …and a row's last bar the music ends inside (the next section's pickup completes it)
        // is cut to the music: its own bar line, a beat later, is no bar line of the piece.
        static int[] Bars(string score) => new MidiExporter().Export(SyntaxTree.Parse(
            "time 4/4\npart m\nsection X { m { c'4 d e | } chords h { C | } }\nsection Y { partial 4 m { f'4 | g1 | } chords h { | G | } }\n"
            + $"form {{ X Y }}\nscore {{ {score} }}\n")).BarTicks.Where(t => t > 0).ToArray();
        Assert.Equal(Bars("staff m"), Bars("chords h  staff m"));

        // The same where nothing completes the bar (the piece ends inside it), and where the
        // music's first bar is short with no `partial` said: the music's bar lines alone.
        static int[] Of(string sections, string score) => new MidiExporter().Export(SyntaxTree.Parse(
            $"time 4/4\npart m\n{sections}\nform {{ X }}\nscore {{ {score} }}\n")).BarTicks.Where(t => t > 0).ToArray();
        foreach (string sections in new[]
                 {
                     "section X { m { c'1 | c'4 d e | } chords h { C | G | } }",
                     "section X { m { g'4 | c''1 | } chords h { | C | } }",
                 })
            Assert.Equal(Of(sections, "staff m"), Of(sections, "chords h  staff m"));
    }

    /// <summary>
    /// A part written for a section's first bars only — a bass line being added by hand under a
    /// chord chart (owner's report 2026-11-16: "the play position jumps in section A") — is padded
    /// with silence to the section's bars, and the silence draws no bar line: the rows' bar lines
    /// past the music written stand, so the metronome accents every bar and a recording lined up
    /// bar by bar does not rush the bars the part leaves out. Before, the bar lines stood at 1 2
    /// only, and with the part's last bar unclosed at 1 only.
    /// </summary>
    [Theory]
    [InlineData("c'1 | c'1 |")]
    [InlineData("c'1 | c'1")]
    [InlineData("c'1 | % |")]
    public void APartWrittenForTheFirstBars_KeepsTheRowsBarLinesAfterIt(string music)
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(
            $"time 4/4\npart m\nsection A {{ chords h {{ C | C | C | C | }} m {{ {music} }} }}\nform {{ A A }}\nscore {{ chords h  staff m }}\n"));
        int bar = 4 * Quarter;
        Assert.Equal(Enumerable.Range(1, 8).Select(i => i * bar), midi.BarTicks.Where(t => t > 0));
        var accents = MetronomeBeats.Of(midi, Quarter, out _).Where(b => b.Accent).Select(b => b.Tick);
        Assert.Equal(Enumerable.Range(0, 8).Select(i => i * bar), accents);
    }

    // ── the MusicXML ────────────────────────────────────────────────────────────────────────

    private static (string Root, int Offset)[] PickupHarmonies(string book, out int divisions)
    {
        var doc = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml();
        var part = doc.Descendants("part").First(p => p.Descendants("harmony").Any());
        divisions = (int)doc.Descendants("divisions").First();
        return part.Elements("measure").First().Elements("harmony")
            .Select(h => ((string)h.Element("root")!.Element("root-step")!, (int?)h.Element("offset") ?? 0)).ToArray();
    }

    [Theory]
    [InlineData(Shape.LeadSheet)]
    [InlineData(Shape.RowAboveStaff)]
    [InlineData(Shape.RowBelowStaff)]
    [InlineData(Shape.RowBetweenStaves)]
    public void TheMusicXml_WritesEveryChordOfThePickup_AtItsBeat(Shape shape)
    {
        var two = PickupHarmonies(Book(shape, "2", "c''4 d", "C7 F"), out int d);
        Assert.Equal([("C", 0), ("F", d)], two);
        var three = PickupHarmonies(Book(shape, "4*3", "c''4 d e", "C7 F G7"), out d);
        Assert.Equal([("C", 0), ("F", d), ("G", 2 * d)], three);
    }

    // ── the LilyPond twin ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TheTwin_WritesThePickupsChords_ABeatEach()
    {
        static string Ly(string book) => new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(book)).ReplaceLineEndings("\n");
        Assert.Contains("c4:7 f4 g4:7 |", Ly(Book(Shape.RowAboveStaff, "4*3", "c''4 d e", "C7 F G7")));
        Assert.Contains("c4:7 f4 |", Ly(Book(Shape.LeadSheet, "2", "", "C7 F")));
        // The uneven pickup: until 2026-11-14 `a1*9/40:m e1*3/20`, the whole 5/8 grid scaled.
        Assert.Contains("a8:m e4 |", Ly(Book(Shape.RowAboveStaff, "8*3", "c''8 d4", "Am E", time: "5/8", barMusic: "c''4. d4")));
    }
}
