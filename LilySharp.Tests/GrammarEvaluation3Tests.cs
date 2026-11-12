// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What four more large scores written to try the grammar found on 2026-11-13 (a string
/// quartet, a guitar piece, a musical-theatre number, a Baroque cantata): the findings that
/// made the sound or the notes wrong, or dropped what was written without a word. Each test is
/// one finding, as it was met.
/// </summary>
public class GrammarEvaluation3Tests
{
    private static List<Diagnostic> Said(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private static LilySharp.Core.Midi.MidiFile Midi(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        return new LilySharp.Core.Midi.MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
    }

    /// <summary>The beats one part's notes start on, in order.</summary>
    private static double[] Beats(string source, string part)
    {
        var midi = Midi(source);
        return [.. midi.Tracks.SelectMany(t => t.Notes).Where(n => n.Part == part && n.DurationTicks > 0)
            .Select(n => n.StartTick / (double)midi.TicksPerQuarterNote).Distinct().Order()];
    }

    private static LilySharp.Core.Svg.Model.MultiStaffScore Page(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    /// <summary>A section ends three beats into a bar and the next one's pickup completes it: with
    /// a chords row over it the row's bar was the meter's four beats, and everything after
    /// sounded a beat late (the page draws one bar; the same file without the row played right).</summary>
    [Theory]
    [InlineData("section X { m { c'4 d' e' | }  chords h { C | } }\nsection Y { partial 4  m { f'4 | g'1 | }  chords h { . | G | } }\n")]
    [InlineData("part m {\n section X { c'4 d' e' | }\n section Y { f'4 | g'1 | } }\nsection Y { partial 4 }\n"
        + "chords h { section X { C | } section Y { . | G | } }\n")]
    public void AChordsRow_DoesNotStretchABarASectionBoundarySplits(string music)
    {
        string source = "octave absolute\npart m { clef treble }\n" + music + "form { X Y }\nscore { chords h  staff m }\n";
        Assert.Equal([0, 1, 2, 3, 4], Beats(source, "m"));
        // The row's chords stand where the bars do, and the first stops where its bar does.
        var midi = Midi(source);
        var row = midi.Tracks.SelectMany(t => t.Notes).Where(n => n.Part == "h (chords)" && n.DurationTicks > 0).ToList();
        Assert.Equal([0, 4], row.Select(n => n.StartTick / midi.TicksPerQuarterNote).Distinct().Order());
        Assert.All(row.Where(n => n.StartTick == 0), n => Assert.Equal(3 * midi.TicksPerQuarterNote, n.DurationTicks));
        // A row longer than the parts, and a section of a row alone, still take the row's bars.
        Assert.Equal([0, 1, 2, 3, 16, 20], Beats("octave absolute\npart m { clef treble }\n"
            + "section X { m { c'4 d' e' f' | }  chords h { C | F | G | } }\nsection Y { chords h { C | } }\nsection Z { m { g'1 | a'1 | } }\n"
            + "form { X Y Z }\nscore { chords h  staff m }\n", "m"));
    }

    /// <summary>The notes each voice of the first staff holds in each bar.</summary>
    private static int[][] NotesByVoiceAndBar(LilySharp.Core.Svg.Model.MultiStaffScore score)
        => [.. score.EnumerateStaves().First().Staff.Voices.Select(v => v.Measures
            .Select(m => m.Items.Count(i => i is LilySharp.Core.Svg.Model.NoteItem)).ToArray())];

    /// <summary>One part of a condensed staff wrote <c>%</c> beside the other's notes: the sign
    /// took the staff's whole bar, the other part's notes with it — its slur and its tuplet
    /// bracket drawn over an empty bar. The mark is written out there; where every part marks
    /// the bar, the sign stands.</summary>
    [Fact]
    public void ACondensedStaff_WritesOutAPercentOnlyOnePartWrites()
    {
        static string Book(string a, string b, string octave = "absolute") => "octave " + octave
            + "\npart a { instrument violin }\npart b { instrument violin }\n"
            + "section S {\n  a { " + a + " }\n  b { " + b + " }\n}\nform { ~S }\nscore { condensedStaff { a b } }\n";
        const string Tune = "c''8( d'' e'' f'') g''2 | g''8( a'' b'' c''') d'''2 | tuplet 3/2 { c'''4( b'' a'') } g''2 | c''1 |";

        string mixed = Book(Tune, "e'4@staccato e' e' e' | % | % | e'1 |");
        Assert.Empty(Said(mixed).Select(d => d.Message));
        var page = Page(mixed);
        Assert.Empty(page.PercentRepeats);
        Assert.Equal(new[] { new[] { 5, 5, 4, 1 }, new[] { 4, 4, 4, 1 } }, NotesByVoiceAndBar(page));
        // …the same page as the bars spelled out.
        Assert.Equal(NotesByVoiceAndBar(Page(Book(Tune, "e'4@staccato e' e' e' | e'4@staccato e' e' e' | e'4@staccato e' e' e' | e'1 |"))),
            NotesByVoiceAndBar(page));
        Assert.Equal(page.Articulations.Length,
            Page(Book(Tune, "e'4@staccato e' e' e' | e'4@staccato e' e' e' | e'4@staccato e' e' e' | e'1 |")).Articulations.Length);

        // Both parts repeat the bar: the sign, and no note under it.
        var both = Page(Book("c''4 d'' e'' f'' | % | % | c''1 |", "e'4 e' e' e' | % | % | e'1 |"));
        Assert.Equal(4, both.PercentRepeats.Length);
        Assert.Equal(new[] { new[] { 4, 0, 0, 1 }, new[] { 4, 0, 0, 1 } }, NotesByVoiceAndBar(both));
        // One repeats two bars more than the other: the sign where both do.
        var partly = Page(Book("c''4 d'' e'' f'' | % | c''1 | c''1 |", "e'4 e' e' e' | % | % | e'1 |"));
        Assert.Equal(new[] { new[] { 4, 0, 1, 1 }, new[] { 4, 0, 4, 1 } }, NotesByVoiceAndBar(partly));
        // A staff of its own keeps its signs, and a written-out bar is the same notes in relative octaves.
        Assert.Equal(2, Page(mixed.Replace("condensedStaff { a b }", "staff a  staff b")).PercentRepeats.Length);
        var relative = Page(Book("c''1 | c'1 | c'1 |", "e'4 g' e'' g'' | % | % |", "relative"));
        var low = relative.EnumerateStaves().First().Staff.Voices[1].Measures;
        Assert.Equal(low[0].Items.OfType<LilySharp.Core.Svg.Model.NoteItem>().Select(n => n.StaffPosition),
            low[2].Items.OfType<LilySharp.Core.Svg.Model.NoteItem>().Select(n => n.StaffPosition));
    }

    /// <summary>The conductor's meters, as (beat, beats, unit).</summary>
    private static (double At, int Beats, int Unit)[] Meters(string source)
    {
        var midi = Midi(source);
        return [.. midi.Tracks.SelectMany(t => t.TimeSignatures).OrderBy(t => t.Tick)
            .Select(t => (t.Tick / (double)midi.TicksPerQuarterNote, t.Numerator, t.Denominator))];
    }

    /// <summary>A <c>time</c> one part writes in the music is every part's from that bar of the
    /// section — but a part with an <c>R*N</c> before it took it N−1 bars late: a correct bar was
    /// reported against the old meter, and in the MIDI a bare <c>R</c> lasted the old meter's
    /// bar, every later note late and the next section's meter never stated.</summary>
    [Theory]
    [InlineData("R*2 | R |")]
    [InlineData("R | R | R |")]
    [InlineData("g'1 | % | R |")]
    public void ATimeAnotherPartWrites_ReachesAPartAfterItsMultiBarRest(string rests)
    {
        string source = "octave absolute\npart m { clef treble }\npart n { clef treble }\n"
            + "section X { m { c'1 | c'1 | time 2/4 d'2 | }  n { " + rests + " } }\n"
            + "section Y { m { e'1 | f'1 | } n { g'1 | a'1 | } }\nform { X Y }\nscore { staff m staff n }\n";
        Assert.Empty(Said(source).Select(d => d.Message));
        Assert.Equal([0, 4, 8, 10, 14], Beats(source, "m"));
        Assert.Equal([10, 14], Beats(source, "n").Where(b => b >= 8));
        Assert.Equal([(0, 4, 4), (8, 2, 4), (10, 4, 4)], Meters(source));
        // The page, which was right, and the exports: the same bars in both parts.
        var staves = Page(source).EnumerateStaves().Select(s => s.Staff.Voices[0].Measures.Length).ToArray();
        Assert.Equal([5, 5], staves);
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter { Form = spec?.Form, Score = spec }.Export(tree);
        static int[] Lengths(LilySharp.Core.MusicXml.MusicXmlPart part)
            => [.. part.Measures.Select(m => m.Notes.Where(n => !n.IsChord && !n.IsBackup).Sum(n => n.Duration))];
        Assert.Equal(Lengths(xml.Parts[0]), Lengths(xml.Parts[1]));
    }

    /// <summary>…and the validator: the bar after the change, in the part that rests before it,
    /// is the new meter's ("Measure duration 1/2 is less than time signature 5/8" was said of it);
    /// a bar that is wrong there is still reported, against the meter the page shows.</summary>
    [Fact]
    public void ATimeAnotherPartWrites_IsTheMeterTheValidatorHoldsABarAfterAMultiBarRestTo()
    {
        static string Book(string last) => "octave absolute\npart a { instrument violin }\npart b { instrument violin }\n"
            + "section S { time 5/8\n a { c''4 d''4. | R*2 | c''4 d''4. | time 2/4\n c''2 | c''2 | }\n"
            + " b { c'4 d'4. | R*2 | c'4 d'4. | " + last + " | c'2 | } }\nform { ~S }\nscore { staff a staff b }\n";
        Assert.Empty(Said(Book("c'2")).Select(d => d.Message));
        var wrong = Said(Book("c'4 d'4.")).Select(d => d.Message).ToList();
        Assert.Contains(wrong, m => m.Contains("2/4"));
        Assert.DoesNotContain(wrong, m => m.Contains("less than"));
    }

    /// <summary>A part with nothing in a section that holds a <c>time none</c> bar was padded a bar
    /// short: its unmetered padding bar was never closed, so every later bar of the part stood a
    /// bar early, its staff ended early and the words under the other staff were said to have no
    /// note. It rests as long as the others play, the unmetered bar included.</summary>
    [Theory]
    [InlineData("staff orn  staff alto  lyrics la")]
    [InlineData("ossia orn  staff alto  lyrics la")]
    public void APartAbsentFromASectionWithAnUnmeteredBar_RestsEveryBarOfIt(string placed)
    {
        string source = "octave absolute\ntime 4/4\npart orn { clef treble }\npart alto { instrument alto  lyrics la }\n"
            + "section R {\n  alto { a'4 a' a' a' | time none a'4 a' a' a' a' | time 4/4 a'4 r4 r4 r8 | }\n"
            + "  lyrics la { la la la la | la la la la la | la | }\n}\n"
            + "section A {\n  time 3/8\n  partial 8\n  orn  { s8 | | | d''16 e'' d''8 a' | | }\n"
            + "  alto { r8 | R | r4 a'8 | d''8 d'' a' | b'4. | }\n  lyrics la { | | Lae- | ta- bor in | lu. | }\n}\n"
            + "form { ~R ~A }\nscore { " + placed + " }\n";
        Assert.Empty(Said(source).Select(d => d.Message));
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var page = Page(source);
        // The same bars on both staves, the ornament's notes in the bar they were written in.
        var staves = page.EnumerateStaves().Select(s => s.Staff.Voices[0].Measures).ToArray();
        Assert.Equal(staves[1].Length, staves[0].Length);
        Assert.Equal(8, staves[0].Length);
        Assert.Equal(6, Array.FindIndex(staves[0].ToArray(), m => m.Items.Any(i => i is LilySharp.Core.Svg.Model.NoteItem)));

        // The exports rest the unmetered bar as long as it is played: five quarters.
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter { Form = spec?.Form, Score = spec }.Export(tree);
        static int[] Lengths(LilySharp.Core.MusicXml.MusicXmlPart part)
            => [.. part.Measures.Select(m => m.Notes.Where(n => !n.IsChord && !n.IsBackup).Sum(n => n.Duration))];
        Assert.Equal(Lengths(xml.Parts.First(p => p.Name.Contains("alto", StringComparison.OrdinalIgnoreCase))),
            Lengths(xml.Parts.First(p => !p.Name.Contains("alto", StringComparison.OrdinalIgnoreCase))));
        Assert.Contains("\\cadenzaOn s1*5/4", new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree));
        // …and both parts play the ornament's bar together.
        Assert.Equal(Beats(source, "orn")[0], Beats(source, "alto").Where(b => b > 15.5).First());
    }

    /// <summary>The file with its singers' parts written after it, as every output reads it.</summary>
    private static string WithSingers(string source)
        => LilySharp.Core.Editing.SingerParts.Expand(source, SyntaxTree.Parse(source), out _);

    private const string Tom = "octave absolute\nlanguage english\npart m { instrument baritone  lyrics w }\n";

    /// <summary>A named singer who sings one section rests the others — as a COUNT of whole bars:
    /// a <c>time 2/4</c> the part wrote inside such a section was not on her staff, its bar four
    /// beats long in the MIDI and the 4/4 after it never stated. She rests bar for bar as the
    /// part she sings on is barred.</summary>
    [Fact]
    public void ASingersStaff_RestsThroughATimeChangeAsItsPartDoes()
    {
        string source = WithSingers(Tom + "section X { m { R | time 2/4 R | } }\n"
            + "section Y { m { c4 d e f | g1 | } lyrics w { la la la la | la | } }\n"
            + "singer tom male on m up 3 in Y\nform { X Y }\nscore { staff m  lyrics w  staff tom }\n");
        Assert.Empty(Said(source).Select(d => d.Message));
        Assert.Equal([(0, 4, 4), (4, 2, 4), (6, 4, 4)], Meters(source));
        Assert.Equal([6, 7, 8, 9, 10], Beats(source, "m"));
        Assert.Equal(Beats(source, "m"), Midi(source).Tracks.SelectMany(t => t.Notes)
            .Where(n => n.Part != "m").Select(n => n.StartTick / 480.0).Distinct().Order());
        // Both staves show the 2/4 and the 4/4 that follows it.
        var changes = Page(source).EnumerateStaves().Select(s => s.Staff.Voices[0].Measures
            .Select(m => m.Items.Count(i => i is LilySharp.Core.Svg.Model.TimeSignatureChangeItem)).ToArray()).ToArray();
        Assert.Equal(changes[0], changes[1]);
        Assert.Equal(2, changes[1].Sum());
    }

    /// <summary>…and the short bar a section ends on, which the next one's pickup completes: her
    /// whole bar of rest there made the pickup "a short bar of its own after a whole bar"
    /// (LYS2022), which the file's own part is not. Many bars at rest are still one rest.</summary>
    [Fact]
    public void ASingersStaff_RestsAShortBarAsLongAsItsPartPlaysIt()
    {
        string source = WithSingers(Tom + "section X { m { c'1 | c'4 d' e' | } lyrics w { la | la la la | } }\n"
            + "section Y { partial 4  m { f4 | g1 | } lyrics w { la | la | } }\n"
            + "singer tom male on m up 3 in Y\nform { X Y }\nscore { staff m  lyrics w  staff tom }\n");
        Assert.Empty(Said(source).Select(d => d.Message));
        Assert.DoesNotContain(Said(source), d => d.Code == DiagnosticCodes.PickupAfterAWholeBar);
        Assert.Contains("section X { R | r2 r4 | }", source);
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(source));
        static int[] Lengths(LilySharp.Core.MusicXml.MusicXmlPart part)
            => [.. part.Measures.Select(m => m.Notes.Where(n => !n.IsChord && !n.IsBackup).Sum(n => n.Duration))];
        Assert.Equal(Lengths(xml.Parts[0]), Lengths(xml.Parts[1]));

        string longRest = WithSingers(Tom + "section X { partial 4  m { c'4 | c'1 | c'1 | c'1 | time 3/4 c'2. | c'2. | } }\n"
            + "section Y { time 3/4  m { c'2. | } lyrics w { la | } }\n"
            + "singer tom male on m up 3 in Y\nform { X Y }\nscore { staff m  lyrics w  staff tom }\n");
        Assert.Contains("section X { R | R*3 | time 3/4 R*2 | }", longRest);
        Assert.Empty(Said(longRest).Select(d => d.Message));
    }

    /// <summary><c>section A { p { … } }</c> and, further down, <c>section A { q { … } }</c>: the page
    /// walked one declaration, so q's staff was empty bars and nothing was said, while the MIDI
    /// played q. A section gathers its parts' music wherever it is declared (each cell once): the
    /// page gathers too — and the same cell written twice is still the error it was.</summary>
    [Theory]
    [InlineData("section A { p { c'1 | c'1 | } }\nsection A { q { e'4 f' g' a' | b'1 | } }\n", 2)]
    [InlineData("section A { q { e'4 f' g' a' | b'1 | } }\nsection A { key g major }\nsection A { p { c'1 | c'1 | } }\n", 2)]
    [InlineData("section A { p { c'1 | } }\nsection A { q { e'4 f' g' a' | b'1 | b'1 | } }\n", 3)]
    public void ASectionDeclaredTwice_GathersThePartsOfBothBodies(string sections, int bars)
    {
        string source = "octave absolute\npart p { clef treble }\npart q { clef treble }\n" + sections
            + "form { ~A }\nscore { staff p  staff q }\n";
        Assert.DoesNotContain(Said(source), d => d.Severity == DiagnosticSeverity.Error);
        var page = Page(source);
        var notes = page.EnumerateStaves().Select(s => s.Staff.Voices[0].Measures
            .Sum(m => m.Items.Count(i => i is LilySharp.Core.Svg.Model.NoteItem))).ToArray();
        Assert.Equal([bars == 3 ? 1 : 2, bars == 3 ? 6 : 5], notes);
        Assert.All(page.EnumerateStaves(), s => Assert.Equal(bars, s.Staff.Voices[0].Measures.Length));
        // The page holds what the MIDI plays.
        Assert.Equal(notes[1], Beats(source, "q").Length);

        Assert.Contains(Said("octave absolute\npart p { clef treble }\nsection A { p { c'1 | } }\nsection A { p { e'1 | } }\n"
            + "form { ~A }\nscore { staff p }\n"), d => d.Code == DiagnosticCodes.DuplicateCell);
    }

    private static string Continuo(string figures)
        => "octave absolute\ntime 4/4\nkey d major\npart bc { instrument cello }\nsection A { bc { e1@figuredBass(" + figures
            + ") | } }\nform { ~A }\nscore { staff bc }\n";

    /// <summary>The figures the page holds for the one note of <see cref="Continuo"/>, top to bottom.</summary>
    private static string[] Figures(string figures)
        => [.. Page(Continuo(figures)).FiguredBasses.Single().Figures.Select(f => f.DisplayText)];

    /// <summary>A sharp on a row of its own under a figure — the raised third, the commonest figure
    /// of a continuo part — was dropped without a word: <c>7 #</c> printed 7, in the MusicXML and
    /// the LilyPond twin too. It is a figure at any row, and comes back from a MusicXML file.</summary>
    [Theory]
    [InlineData("7 #", "7|♯", "<7 _+>")]
    [InlineData("5 #", "5|♯", "<5 _+>")]
    [InlineData("7 5 #", "7|5|♯", "<7 5 _+>")]
    [InlineData("#", "♯", "<_+>")]
    [InlineData("# 5", "5♯", "<5+>")]
    [InlineData("6 # 4", "6|4♯", "<6 4+>")]
    [InlineData("# #", "♯|♯", "<_+ _+>")]
    [InlineData("# _", "♯|–", null)]
    [InlineData("6 s 4", "6♯|4", "<6+ 4>")]
    public void AFiguredBassSharpOnARowOfItsOwn_IsAFigure(string written, string rows, string? lilypond)
    {
        Assert.Empty(Said(Continuo(written)).Select(d => d.Message));
        Assert.Equal(rows.Split('|'), Figures(written));
        var tree = SyntaxTree.Parse(Continuo(written));
        if (lilypond != null)
            Assert.Contains(lilypond, new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree));

        // MusicXML: a <figure> for each row, the sharp alone one with only a <prefix> — and the
        // file read back draws the same figures.
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(tree).ToXml();
        var figures = xml.Descendants("figure").ToList();
        Assert.Equal(rows.Split('|').Length, figures.Count);
        if (written == "7 #")
            Assert.Equal(["prefix"], figures[1].Elements().Select(e => e.Name.LocalName));
        if (!written.Contains('_'))
        {
            var (lys, _) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(xml.ToString());
            var back = SyntaxTree.Parse(lys);
            Assert.Equal(rows.Split('|'), SvgGenerator.CollectScore(back, RenderSpecParser.FindFirst(back)).FiguredBasses.Single().Figures.Select(f => f.DisplayText));
        }
    }

    /// <summary>A figure group Lily# does not read is ignored whole — and was answered with a guess
    /// ("Did you mean '@figuredBass(6)'?"), or, for <c>7!</c>, with a message about a span that
    /// cannot be ended. The diagnostic says what is read.</summary>
    [Theory]
    [InlineData("6+")]
    [InlineData("b7")]
    [InlineData("6 b")]
    [InlineData("6-")]
    [InlineData("6/")]
    [InlineData("7!")]
    [InlineData("n")]
    [InlineData("s 7")]
    [InlineData("5 sharp")]
    public void AnUnknownFigure_IsToldWhatFiguredBassReads(string written)
    {
        var said = Assert.Single(Said(Continuo(written)));
        Assert.Equal(DiagnosticCodes.UnknownAnnotation, said.Code);
        Assert.Contains("@figuredBass(" + written + ")", said.Message);
        Assert.Contains("'7 #'", said.Message);
        Assert.Contains("'6 s'", said.Message);
        Assert.DoesNotContain("Did you mean", said.Message);
        Assert.DoesNotContain("@!", said.Message);
        Assert.Empty(Page(Continuo(written)).FiguredBasses);
    }

    /// <summary>…and what the reference says is read, is: every spelling it lists draws.</summary>
    [Fact]
    public void TheFiguredBassVocabularyTheReferenceLists_IsRead()
    {
        string reference = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "GRAMMAR_FOR_LLM.md"));
        int at = reference.IndexOf("- Figured bass:", StringComparison.Ordinal);
        string entry = reference[at..reference.IndexOf("Nothing else is read", at, StringComparison.Ordinal)];
        var spellings = System.Text.RegularExpressions.Regex.Matches(entry, "`([0-9#_sfn ]+)`").Select(m => m.Groups[1].Value)
            .Where(s => s is not ("s" or "f" or "n")).Distinct().ToList();
        Assert.True(spellings.Count >= 15, string.Join(" · ", spellings));
        foreach (string spelling in spellings)
            Assert.Empty(Said(Continuo(spelling)).Select(d => spelling + ": " + d.Message));
    }

    /// <summary><c>@vibrato</c> is a singer's mark, performed by a singing voice: on a guitar it was
    /// accepted, drew nothing and sounded nothing, in silence. It is said — with the way to print
    /// the word — and a sung part's is left alone. <c>@slide</c>, reported beside it as unknown
    /// ground, is the glissando line under another name.</summary>
    [Theory]
    [InlineData("@vibrato")]
    [InlineData("@vibrato(40)")]
    public void VibratoOnAPartNobodySings_IsSaid(string mark)
    {
        string guitar = "octave absolute\npart g { instrument guitar }\nsection A { g { a'2" + mark + " a'4 b' | } }\n"
            + "form { ~A }\nscore { staff g  tab g }\n";
        var said = Assert.Single(Said(guitar));
        Assert.Equal(DiagnosticCodes.UnknownAnnotation, said.Code);
        Assert.Equal(DiagnosticSeverity.Warning, said.Severity);
        Assert.Contains("singer's mark", said.Message);
        Assert.Contains("@text(\"vib.\")", said.Message);
        Assert.Contains("'g'", said.Message);

        // Sung: words set to the part, or a singer line on it.
        Assert.Empty(Said("octave absolute\nlanguage english\npart v { instrument soprano  lyrics w }\n"
            + "section A { v { a'2" + mark + " a'4 b' | } lyrics w { la la la | } }\nform { ~A }\nscore { staff v  lyrics w }\n").Select(d => d.Message));
        Assert.Empty(Said("octave absolute\nlanguage english\npart v { instrument soprano  lyrics w }\n"
            + "section A { v { a'2" + mark + " a'4 b' | } lyrics w { la la la | } }\nsinger ann female on v\nform { ~A }\nscore { staff v  lyrics w }\n").Select(d => d.Message));
    }

    /// <summary><c>@slide</c> is the glissando's line (and a <c>&lt;slide&gt;</c> in MusicXML).</summary>
    [Fact]
    public void ASlide_IsTheGlissandoLine()
    {
        static string Book(string mark) => "octave absolute\npart g { instrument guitar }\nsection A { g { a'2" + mark + " b'2 | } }\n"
            + "form { ~A }\nscore { staff g }\n";
        Assert.Empty(Said(Book("@slide")).Select(d => d.Message));
        Assert.Contains("\\glissando", new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book("@slide"))));
        Assert.Equal(LpFidelity.RenderedGeometry.Render(Book("@glissando")).Lines.Count, LpFidelity.RenderedGeometry.Render(Book("@slide")).Lines.Count);
        Assert.Single(new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(Book("@slide"))).ToXml().Descendants("slide"),
            e => (string?)e.Attribute("type") == "start");
    }

    // ── The guitar score: tablature and the capo ────────────────────────────────────────────

    /// <summary>The tab staff's fret numbers as the page reads them, one array an item that
    /// prints any, each (string, fret) from the lowest string up.</summary>
    private static (int String, int Fret)[][] TabFrets(string source)
    {
        var tab = Page(source).StaffGroups.SelectMany(g => g.Staves).Single(s => s.IsTab);
        int[] tuning = LilySharp.Core.Tablature.Tunings.GetTuning(tab.Tuning!.Value);
        int shift = LilySharp.Core.Tablature.Tunings.SoundingShift(tab.TabSourceClef, tab.Transposition);
        (int, int) At(int midi, int? written) => LilySharp.Core.Tablature.Tunings.CalculateFret(midi + shift, tuning, written ?? 0);
        return [.. tab.Voices[0].Measures.SelectMany(m => m.Items).Select(i => i switch
        {
            LilySharp.Core.Svg.Model.NoteItem { TabHidden: false } n => new[] { At(n.Midi, n.StringNumber) },
            LilySharp.Core.Svg.Model.ChordItem c => [.. c.Notes.Select(n => At(n.Midi, n.StringNumber)).OrderByDescending(x => x.Item1)],
            _ => [],
        }).Where(a => a.Length > 0)];
    }

    private static string Guitar(string music, string head = "", string score = "staff g  tab g", string header = "instrument guitar")
        => "octave absolute\n" + head + "part g { " + header + " }\nsection A { g { " + music + " } }\nform { ~A }\nscore { " + score + " }\n";

    /// <summary>Under <c>capo 2</c> the tab counts its frets from the capo, as the diagram over it
    /// does: a <c>diagram(D x32010)</c> drew x32010 with x54232 under it, and an open low E printed
    /// 0 — behind the capo. The notes are written as they sound; one below the capo on every
    /// string is said, by name, and left off the tab.</summary>
    [Fact]
    public void ACapoedTab_CountsItsFretsFromTheCapo()
    {
        string capoed = Guitar("diagram(D x32010)1 | <e, b, e gis b e'>1 | fis,4 e'' r2 |", "capo 2\n");
        var frets = TabFrets(capoed);
        // The shape as written, string 5 up to string 1 (the sixth is not played).
        Assert.Equal([(5, 3), (4, 2), (3, 0), (2, 1), (1, 0)], frets[0]);
        // The E chord without its open low E, each member where it sounds: open + capo + fret.
        int[] open = [64, 59, 55, 50, 45, 40];
        Assert.Equal([47, 52, 56, 59, 64], frets[1].Select(f => open[f.String - 1] + 2 + f.Fret));
        Assert.All(frets[1], f => Assert.True(f.Fret >= 0));
        Assert.Equal(frets[1].Length, frets[1].Select(f => f.String).Distinct().Count());
        // The capo's own note is fret 0, and the top of the first string is still reached.
        Assert.Equal([(6, 0)], frets[2]);
        Assert.Equal([(1, 10)], frets[3]);
        var low = Assert.Single(Said(capoed), d => d.Code == DiagnosticCodes.TabOutOfRange);
        Assert.Contains("E2 cannot be played with the capo on fret 2", low.Message);
        Assert.Equal('<', capoed[low.Span.Start]);    // at its chord

        // Without the line the same notes are fretted from the nut, the low E open.
        var plain = TabFrets(Guitar("diagram(D x32010)1 | <e, b, e gis b e'>1 | fis,4 e'' r2 |"));
        Assert.Equal(6, plain[1].Length);
        Assert.Contains((6, 0), plain[1]);
        Assert.Equal([(6, 2)], plain[2]);

        // The other pages tell the same story: MusicXML's frets (beside its <capo>), the twin's shift.
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(capoed)).ToXml();
        Assert.Equal(["3", "2", "0", "1", "0"], xml.Descendants("fret").Take(5).Select(e => e.Value));
        Assert.Equal("2", xml.Descendants("capo").First().Value);
        Assert.Contains("\\transpose c bes, ", new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(capoed)));

        // A bass wears no capo: the band's bass tab counts from the nut as it did.
        static string Band(string head) => "octave absolute\n" + head + "part b { instrument bass }\nsection A { b { e,4 a, d g | } }\nform { ~A }\nscore { tab b }\n";
        Assert.Equal(TabFrets(Band("")), TabFrets(Band("capo 2\n")));
        Assert.DoesNotContain(Said(Band("capo 2\n")), d => d.Code == DiagnosticCodes.TabOutOfRange);
    }

    /// <summary>A member of a chord below the tab's lowest string leaves the tab, as LYS5002 says
    /// and as a single note does: it was clamped to fret 0 of the lowest string, on top of the
    /// member that string plays.</summary>
    [Fact]
    public void AChordMemberBelowTheStrings_IsLeftOffTheTab()
    {
        string source = Guitar("<d, a, d fis>1 |");
        var chord = Assert.Single(TabFrets(source));
        Assert.Equal(3, chord.Length);
        Assert.Equal(3, chord.Select(f => f.String).Distinct().Count());
        Assert.Contains(Said(source), d => d.Code == DiagnosticCodes.TabOutOfRange && d.Message.Contains("omitted from the tab"));
    }

    /// <summary><c>instrument guitar  tuning guitarDropD</c>: the open sixth string was "below the
    /// range of the Guitar". The range is the tuning's strings.</summary>
    [Fact]
    public void ARetunedPartsRange_IsItsOwnStrings()
    {
        static IEnumerable<string> Range(string header, string music)
            => Said(Guitar(music, header: header, score: "staff g")).Where(d => d.Code == DiagnosticCodes.NoteOutsideInstrumentRange).Select(d => d.Message);
        Assert.Empty(Range("instrument guitar  tuning guitarDropD", "d,4 a, d e''' |"));
        Assert.Contains("D2-E6", Assert.Single(Range("instrument guitar  tuning guitarDropD", "cis,4 a, d e''' |")));
        // The top moves with the first string: drop C is a tone lower throughout.
        Assert.Equal(2, Range("instrument guitar  tuning guitarDropC", "c,4 b,, d''' ees''' |").Count());
        Assert.Contains("C2-D6", Range("instrument guitar  tuning guitarDropC", "b,,1 |").Single());
        // The usual tuning is as it was.
        Assert.Contains("E2-E6", Assert.Single(Range("instrument guitar", "d,4 e, e''' r |")));
    }

    /// <summary>A glissando is one finger along one string: its two ends were put on neighbouring
    /// strings, the line drawn between them. A written string still decides, and notes with no
    /// slide are chosen as they were.</summary>
    [Fact]
    public void AGlissando_KeepsItsTwoNotesOnOneString()
    {
        var slid = TabFrets(Guitar("cis'4@glissando fis' d'@glissando e' |")).Select(f => f[0]).ToArray();
        Assert.Equal(slid[0].String, slid[1].String);
        Assert.Equal(slid[2].String, slid[3].String);
        Assert.Equal(5, slid[1].Fret - slid[0].Fret);
        var slide = TabFrets(Guitar("cis'4@slide fis' r2 |")).Select(f => f[0]).ToArray();
        Assert.Equal(slide[0].String, slide[1].String);
        // The same notes with no slide: the strings the planner chose before.
        var plain = TabFrets(Guitar("cis'4 fis' d' e' |")).Select(f => f[0]).ToArray();
        Assert.Equal([(2, 2), (1, 2), (2, 3), (1, 0)], plain);
        // A written \N wins over the slide.
        var written = TabFrets(Guitar("cis'4@glissando\\2 fis'\\1 r2 |")).Select(f => f[0]).ToArray();
        Assert.Equal([(2, 2), (1, 2)], written);
        // Across a rest there is no slide to keep.
        var apart = TabFrets(Guitar("cis'4@glissando r fis' r |")).Select(f => f[0]).ToArray();
        Assert.Equal((1, 2), apart[1]);
    }

    /// <summary><c>&lt;c f@notehead(diamond)&gt;</c> — a stopped note and the touched fourth over it —
    /// drew two ordinary heads; a single note's style was read.</summary>
    [Fact]
    public void ANoteheadOnAChordMember_IsThatMembersHead()
    {
        string source = "octave absolute\npart v { clef treble }\nsection A { v { <c' f'@notehead(diamond)>2 <c' f'>2@notehead(x) | } }\nform { ~A }\nscore { staff v }\n";
        Assert.Empty(Said(source).Select(d => d.Message));
        var chords = Page(source).StaffGroups.SelectMany(g => g.Staves).Single().Voices[0].Measures
            .SelectMany(m => m.Items).OfType<LilySharp.Core.Svg.Model.ChordItem>().ToArray();
        Assert.Equal([LilySharp.Core.Svg.Model.NoteheadStyle.Default, LilySharp.Core.Svg.Model.NoteheadStyle.Diamond], chords[0].Notes.Select(n => n.Notehead));
        Assert.Equal(LilySharp.Core.Svg.Model.NoteheadStyle.Default, chords[0].Notehead);
        // The chord's own mark still styles every head.
        Assert.Equal(LilySharp.Core.Svg.Model.NoteheadStyle.Cross, chords[1].Notehead);
        var notes = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().Descendants("note").ToArray();
        Assert.Equal([null, "diamond", "x", "x"], notes.Select(n => (string?)n.Element("notehead")));
        Assert.Contains("<c' \\tweak style #'diamond f'>2", new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(source)));
    }

    /// <summary><c>chordShapes { A7(b9) x02323 }</c> was refused at the parentheses (LYS9103) that
    /// a chords row and <c>chord()</c> take: the table reads the same chord names.</summary>
    [Theory]
    [InlineData("A7(b9) x02323", null)]
    [InlineData("A7(b9) x02223", "for A7(b9) lacks")]
    [InlineData("section A { A7(b9) x02323 }", null)]
    public void AShapeTable_TakesAParenthesisedAlteration(string entry, string? warned)
    {
        string source = "chordShapes { " + entry + " }\npart g { instrument guitar }\nsection A { chords h { A7(b9) | } g { a1 | } }\n"
            + "form { ~A }\nscore { chords h  staff g }\n";
        var said = Said(source);
        Assert.DoesNotContain(said, d => d.Severity == DiagnosticSeverity.Error);
        if (warned == null)
            Assert.Empty(said.Select(d => d.Message));
        else
            Assert.Contains(warned, Assert.Single(said, d => d.Code == DiagnosticCodes.ChordShapeMismatch).Message);
    }

    /// <summary><c>\7</c> on six strings passed with no word.</summary>
    [Fact]
    public void AStringTheTuningDoesNotHave_IsSaidWithItsCount()
    {
        var said = Said(Guitar("d4\\7 a\\9 d'2\\2 |")).Where(d => d.Code == DiagnosticCodes.TabStringUnplayable).ToList();
        Assert.Equal(2, said.Count);
        Assert.Contains("there is no string 7: this tab's tuning has 6 strings", said[0].Message);
        Assert.Contains("there is no string 9", said[1].Message);
        Assert.DoesNotContain(Said(Guitar("d4\\6 a\\5 d'2\\2 |")), d => d.Code == DiagnosticCodes.TabStringUnplayable);
    }

    /// <summary><c>tab g tuning guitarDropD</c> asked for a part named 'tuning'.</summary>
    [Fact]
    public void ATabItemsTuningWord_IsToldWhereTheTuningStands()
    {
        var said = Said(Guitar("d1 |", score: "staff g  tab g tuning guitarDropD")).Where(d => d.Code == DiagnosticCodes.UndefinedPart).ToList();
        Assert.Contains(said, d => d.Message.Contains("A tab item has no 'tuning' word") && d.Message.Contains("'tab guitarDropD g'"));
        Assert.Contains(said, d => d.Message.Contains("'guitarDropD' is a tuning's name"));
        Assert.DoesNotContain(said, d => d.Message.Contains("Undefined part: 'tuning'"));
        // The spelling the message names is read.
        Assert.Empty(Said(Guitar("d,1 |", score: "staff g  tab guitarDropD g")).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        // A part that IS named tuning is a part.
        Assert.DoesNotContain(Said("octave absolute\npart g { instrument guitar }\npart tuning { clef treble }\nsection A { g { d1 | } tuning { c'1 | } }\n"
            + "form { ~A }\nscore { staff g  tab g tuning }\n"), d => d.Code == DiagnosticCodes.UndefinedPart);
    }

    /// <summary>A table shape with no instrument word is tried on every fretted tuning of the file:
    /// <c>G 320003</c> "sounds F G B D, which is G7/F" because another part is in drop D, and the
    /// message did not say whose tuning that was.</summary>
    [Fact]
    public void AShapeThatFailsOnAnotherPartsTuning_NamesThePartAndTheTuning()
    {
        static string Book(string entry) => "chordShapes { " + entry + " }\npart g { instrument guitar  tuning guitarDropD }\npart h { instrument guitar }\n"
            + "section A { chords k { G | } g { g1 | } h { g1 | } }\nform { ~A }\nscore { chords k  staff h }\n";
        var warned = Assert.Single(Said(Book("G 320003")), d => d.Code == DiagnosticCodes.ChordShapeMismatch);
        Assert.Contains("Tried on guitarDropD, the tuning of part 'g'", warned.Message);
        Assert.Contains("'G guitar 320003'", warned.Message);
        // The entry the message names is the guitar's alone.
        Assert.DoesNotContain(Said(Book("G guitar 320003")), d => d.Code == DiagnosticCodes.ChordShapeMismatch);
        // A file with one tuning says nothing more than it did.
        var one = Assert.Single(Said("chordShapes { G 320002 }\npart h { instrument guitar }\nsection A { chords k { G | } h { g1 | } }\nform { ~A }\nscore { chords k  staff h }\n"),
            d => d.Code == DiagnosticCodes.ChordShapeMismatch);
        Assert.DoesNotContain("Tried on", one.Message);
    }
}
