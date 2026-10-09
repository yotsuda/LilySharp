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
using System.Xml.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section spans as many bars as its longest voice, and EVERY reader pads a shorter voice
/// up to it — the page did (MeasureCollector's section padding), the LilyPond twin, the MIDI
/// walk and the MusicXML export did not (MEASURED 2026-09-10: the twin drew melody's B under
/// bass's A, MusicXML gave P1 one measure fewer than P2, and the MIDI played B a bar early
/// when only a chord row made A two bars). All four now read one count,
/// <see cref="SectionBarCounts"/>.
/// </summary>
public class SectionVoicePaddingExportTests
{
    // ★ scratch/ベースタブLy/tooLongChords.lys: only the chord row makes A two bars.
    private const string ChordRowLonger = """
        part melody {
          section A { g2 g | }
          section B { c2 c | }
        }
        chords prog {
          section A { Dm7 | G7 }
          section B { Cmaj7 | }
        }
        form { A | B | }
        score { chords prog  staff melody }
        """;

    // scratch/p363/pm-two-parts.lys: bass makes A two bars, melody writes one.
    private const string PartLonger = """
        part melody { clef treble
          section A { g'2 g' | }
          section B { c''2 c'' | }
        }
        part bass { clef bass
          section A { c2 e | g2 g | }
          section B { c2 c | }
        }
        form { A | B | }
        score { staff melody  staff bass }
        """;

    private static string Twin(string source) => new LilyPondExporter().Export(SyntaxTree.Parse(source));

    private static MusicXmlDocument Xml(string source) => new MusicXmlExporter().Export(SyntaxTree.Parse(source));

    // The PARTS' notes: a placed chord row sounds on a track of its own since 2026-09-25
    // (ChordRowMidiTests), and what these cases measure is where the parts land.
    private static (int Tick, int Pitch)[] Notes(string source)
    {
        var file = new MidiExporter().Export(SyntaxTree.Parse(source));
        return file.Tracks.Where(t => !t.Name.EndsWith(" (chords)", System.StringComparison.Ordinal))
            .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .Select(n => (n.StartTick / file.TicksPerQuarterNote, n.Pitch)).ToArray();
    }

    // ---------------------------------------------------------------- the count itself

    [Fact]
    public void CanonicalByName_IsTheLongestVoice_PartsAndChordRowsAlike_LyricsNever()
    {
        var root = SyntaxTree.Parse("""
            part melody {
              section A { g2 g | }
              section B { c2 c | }
              section C { c2 c | }
            }
            part bass {
              section A { c2 e | g2 g | }
            }
            chords prog {
              section B { Dm7 | G7 | Am7 }
            }
            lyrics words {
              section C { one two | three four | five six | seven eight | }
            }
            section D { melody { c1 | } chords prog { C | F | G | } }
            section E { c1 | c1 | }
            form { A B C D E }
            score { chords prog  staff melody  staff bass  lyrics words sings melody }
            """).GetRoot();
        var semantic = SectionBarCounts.BuildSemanticIndex(root).Canonical;
        var syntactic = SectionBarCounts.CanonicalByNameSyntactic(root);
        foreach (var bars in new[] { semantic, syntactic })
        {
            Assert.Equal(2, bars["A"]); // bass
            Assert.Equal(3, bars["B"]); // the chord row
            Assert.Equal(1, bars["C"]); // the lyrics' four bars are verses, not a voice
            Assert.Equal(3, bars["D"]); // by-section: the named chord block
            Assert.Equal(2, bars["E"]); // single-part shorthand: the section's own music
        }
    }

    [Fact]
    public void SemanticVoices_SayWhetherTheLastBarIsOpen()
    {
        // (Names that are not pitch or dynamic letters — `part a` / `chords p` do not parse.)
        var root = SyntaxTree.Parse("""
            part melody { section A { g2 g } section B { g2 g | } }
            chords prog { section A { Dm7 | G7 } section B { Dm7 | } }
            """).GetRoot();
        var voices = SectionBarCounts.SemanticVoices(root);
        Assert.Equal(new[] { "part 'melody'", "part 'melody'", "chords 'prog'", "chords 'prog'" }, voices.Select(v => v.Label));
        Assert.Equal(new[] { 1, 1, 2, 1 }, voices.Select(v => v.Bars));
        Assert.Equal(new[] { true, false, true, false }, voices.Select(v => v.TrailingOpen));
    }

    /// <summary>
    /// The cross-part validator's by-part pass asks for the by-part voices ONLY, so that
    /// a by-section voice — most sections of most books — is not split into bars for a
    /// reader that discards the count (MEASURED, session 400: on a one-part 1000-bar book that
    /// split was 31 ms of the validator's 69). The filtered call must be exactly the unfiltered
    /// call's by-part subset, in order, on every book of the net — the same voices, the same
    /// counts — and the same again when the caller hands over its own phrase table instead of
    /// letting the count gather one.
    /// </summary>
    [Fact]
    public void GroupedByPartOnly_IsTheUnfilteredCallsGroupedByPartSubset_OnEveryNetBook()
    {
        int books = 0, groupedByPart = 0, groupedBySection = 0;
        foreach (var path in NetAndAuditBooks())
        {
            SyntaxTree tree;
            try { tree = SyntaxTree.Parse(System.IO.File.ReadAllText(path)); }
            catch { continue; }
            books++;
            var root = tree.GetRoot();
            var all = SectionBarCounts.SemanticVoices(root);
            var expected = all.Where(v => v.GroupedByPart).Select(Key).ToList();
            Assert.Equal(expected, SectionBarCounts.SemanticVoices(root, groupedByPartOnly: true).Select(Key).ToList());
            // The caller's table, gathered by the rule MeasureValidator uses.
            var bodies = new Dictionary<string, SyntaxNode>();
            foreach (var n in root.DescendantNodes())
            {
                if (n is PhraseDeclarationSyntax ph) bodies[ph.Name.Text] = ph.Body;
                else if (n is VariableDeclarationSyntax vd) bodies[vd.Name.Text] = vd.Expression;
            }
            Assert.Equal(expected, SectionBarCounts.SemanticVoices(root, bodies, groupedByPartOnly: true).Select(Key).ToList());
            groupedByPart += expected.Count;
            groupedBySection += all.Count - expected.Count;
        }
        Assert.True(books >= 50 && groupedByPart >= 50 && groupedBySection >= 50,
            $"the net must hold both layouts: books {books} groupedByPart {groupedByPart} groupedBySection {groupedBySection}");

        static string Key(SectionBarCounts.SemanticVoice v)
            => $"{v.SectionName}|{v.Label}|{v.IsChords}|{v.Bars}|{v.TrailingOpen}|{v.Container.Span.Start}";
    }

    [Fact]
    public void SemanticCount_ExpandsRestsRepeatsAndPhrases_SyntacticDoesNot()
    {
        // ★ The first cut padded the exporters by the page's syntactic count and wrote two
        // spurious `s1 |` after every `R1*4` and 48 after canon-in-d's `repeat unfold 13`
        // (scratch/p363/compare-leg3-ly.txt, 2026-09-10). The semantic count is the played
        // length, so a voice that IS four bars is not "short" of a four-bar section-mate.
        const string text = """
            phrase two { c1 | c1 | }
            part upper { section Rest { R1*4 | } section Unf { repeat unfold 2 { c1 | c1 | } } section Phr { two two } }
            part lower { section Rest { c1 | c1 | c1 | c1 | } section Unf { c1 | c1 | c1 | c1 | } section Phr { c1 | c1 | c1 | c1 | } }
            form { Rest Unf Phr }
            score { staff upper  staff lower }
            """;
        // (`section R` does not parse: R is the multi-measure rest.)
        var root = SyntaxTree.Parse(text).GetRoot();
        var index = SectionBarCounts.BuildSemanticIndex(root);
        var a = index.ByContainer.Values.Where(v => v.Label == "part 'upper'").ToDictionary(v => v.SectionName, v => v.Bars);
        Assert.Equal(4, a["Rest"]);
        Assert.Equal(4, a["Unf"]);
        Assert.Equal(4, a["Phr"]);
        var syntactic = SectionBarCounts.CanonicalByNameSyntactic(root);
        Assert.Equal(4, syntactic["Rest"]); // part lower's four literal bars carry the name either way
        // And so no export pads part upper: the twin holds no spacer at all, and the MusicXML
        // writes upper exactly as it does with no section-mate to be short of (its OWN measure
        // count — a multi-measure rest and an unfolded repeat are not twelve measures there).
        Assert.DoesNotContain("s1", Twin(text));
        var alone = Xml(text.Replace("staff lower", "")).Parts.Single(p => p.Name == "upper").Measures.Count;
        Assert.Equal(alone, Xml(text).Parts.Single(p => p.Name == "upper").Measures.Count);
    }

    /// <summary>
    /// The page's syntactic canonical count must agree with the semantic one on every book
    /// the net covers — where it does not, the page pads a short voice by too little and
    /// LYS2007's "padded to align" is false. The first run of this net (2026-09-10, before
    /// the green walk learned `R1*N`, repeat counts and phrase bodies) is the measurement
    /// the fix was sized by.
    /// </summary>
    [Fact]
    public void SyntacticCanonical_MatchesSemantic_OnEveryNetBook()
    {
        var failures = new List<string>();
        int books = 0, sections = 0;
        foreach (var path in NetAndAuditBooks())
        {
            SyntaxTree tree;
            try { tree = SyntaxTree.Parse(System.IO.File.ReadAllText(path)); }
            catch { continue; }
            books++;
            var root = tree.GetRoot();
            var semantic = SectionBarCounts.BuildSemanticIndex(root).Canonical;
            var syntactic = SectionBarCounts.CanonicalByNameSyntactic(root);
            foreach (var (name, bars) in semantic)
            {
                sections++;
                int page = syntactic.TryGetValue(name, out int s) ? s : -1;
                if (page == bars)
                    continue;
                string book = System.IO.Path.GetFileName(path);
                // An UNDERCOUNT is the safe direction (the page pads nothing wrongly; at
                // worst a short voice beside this one is not padded, as before 2026-09-10)
                // and is allowed only where it is understood: a repeat whose body writes no
                // bar line is music in one bar to the green walk, though its played length
                // may be whole bars (05-special-techniques: `repeat percent 2 { c4 e g e } |`
                // is one written bar of four beats, two bars played). An OVERCOUNT pads
                // every other voice wrongly and is never allowed.
                bool allowedUndercount = page < bars && book == "05-special-techniques.lys" && name == "Main";
                if (!allowedUndercount)
                    failures.Add($"{book} section '{name}': page {page} vs semantic {bars}");
            }
        }
        Assert.True(books >= 50 && sections >= books, $"only {books} books / {sections} sections");
        Assert.True(failures.Count == 0,
            $"{failures.Count} section(s) where the page's count differs:\n" + string.Join("\n", failures.Take(40)));
    }

    /// <summary>The fixture net plus the LilyPond-regression books under <c>audit/</c> —
    /// the probe shapes live there (a beat repeat, a grace before a volta), and the
    /// fixture net alone let the first cut of the repeat rule through.</summary>
    internal static IEnumerable<string> NetAndAuditBooks()
    {
        string? repo = null;
        foreach (var path in CollectResumeTests.NetBooks())
        {
            repo ??= RepoRootOf(path);
            yield return path;
        }
        if (repo == null)
            yield break;
        foreach (var dir in new[] { System.IO.Path.Combine(repo, "audit", "lp-regression", "lys"), System.IO.Path.Combine(repo, "audit", "lpreg") })
            if (System.IO.Directory.Exists(dir))
                foreach (var f in System.IO.Directory.EnumerateFiles(dir, "*.lys", System.IO.SearchOption.AllDirectories).OrderBy(f => f, System.StringComparer.Ordinal))
                    yield return f;
    }

    private static string? RepoRootOf(string path)
    {
        for (var dir = System.IO.Path.GetDirectoryName(path); dir != null; dir = System.IO.Path.GetDirectoryName(dir))
            if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, "audit")) && System.IO.File.Exists(System.IO.Path.Combine(dir, "LilySharp.slnx")))
                return dir;
        return null;
    }

    [Theory]
    // A beat repeat: no bar line in the body, so the whole thing is music in one bar
    // (audit/lpreg/slashprobe.lys drew 4 bars for 1 when each turn counted as a bar).
    [InlineData("v { repeat percent 2 { c16 d e f } repeat percent 2 { g8. c16 } | }", 1)]
    // Grace before an in-music volta: the grace is pending music the body's first bar
    // absorbs; the page expands the volta into its two turns (voltagrace-probe.lys draws 2).
    [InlineData("v { grace { f8 } repeat volta 2 { b1 | } }", 2)]
    // A closed body: its bars per turn, and the `|` after it confirms the close.
    [InlineData("v { repeat percent 4 { c1 | } | }", 4)]
    // An open tail carries over once, never per turn (under, not over).
    [InlineData("v { repeat percent 2 { g2 ~ g } | }", 1)]
    public void SyntacticCount_NeverExceedsThePage_OnRepeatShapes(string block, int expected)
    {
        var root = SyntaxTree.Parse($"time 4/4 part v {{ }} section Main {{ {block} }} form {{ Main }} score {{ staff v }}").GetRoot();
        Assert.Equal(expected, SectionBarCounts.CanonicalByNameSyntactic(root)["Main"]);
        // …and so the page draws exactly its own bars, padded by nothing.
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(
            $"time 4/4 part v {{ }} section Main {{ {block} }} form {{ Main }} score {{ staff v }}"), "v");
        Assert.True(score.Voice.Measures.Length >= expected, $"page {score.Voice.Measures.Length} < count {expected}");
    }

    // ---------------------------------------------------------------- the LilyPond twin

    [Fact]
    public void Twin_PadsTheShortPart_WhenAChordRowMakesTheSectionLonger()
    {
        var ly = Twin(ChordRowLonger);
        // Melody's A gets its silent second bar, so B's notes stand under B's chord. (The
        // stream breaks its line at a bar line, hence the whitespace-tolerant match.)
        Assert.Matches(@"g2 g \|\s*s1 \|", ly);
        Assert.Contains("d1:m7 |", ly);
    }

    [Fact]
    public void Twin_PadsTheShortPart_AgainstALongerPart()
    {
        var ly = Twin(PartLonger);
        Assert.Matches(@"g'2 g' \|\s*s1 \|", ly);
        Assert.DoesNotMatch(@"g2 g \|\s*s1", ly); // the long voice is untouched
    }

    [Fact]
    public void Twin_ClosesAnOpenLastBar_BeforePadding()
    {
        var ly = Twin("""
            part melody { section A { g2 g } }
            part bass { section A { c2 e | g2 g | } }
            form { A | }
            score { staff melody  staff bass }
            """);
        Assert.Matches(@"g2 g \|\s*s1 \|", ly);
    }

    [Fact]
    public void Twin_PadsAShortChordRow_WithSilentChordBars()
    {
        var ly = Twin("""
            part melody { section A { g2 g | g2 g | g2 g | } section B { c1 | } }
            chords prog { section A { Dm7 | } section B { Cmaj7 | } }
            form { A | B | }
            score { chords prog  staff melody }
            """);
        // Two silent chord bars after Dm7, so Cmaj7 stands over B's bar.
        Assert.Matches(@"d1:m7 \|\s*s1 \|\s*s1 \|\s*c1:maj7", ly);
    }

    // A part and a chord row that write NO block for B: the page, the MIDI and the MusicXML
    // pad B's two bars; the twin skipped the play whole until 2026-09-29 (HANDOFF §1.1 第662 ⑺).
    private const string NoBlockForB = """
        octave absolute
        part melody { clef treble
          section A { g'2 g' | a'1 | }
          section B { c''2 c'' | d''1 | }
          section C { e''2 e'' | }
        }
        part bass { clef bass
          section A { c2 e | g1 | }
          section C { g2 g | }
        }
        chords prog {
          section A { C | G | }
          section C { G | }
        }
        form { A B C }
        score { chords prog  staff melody  staff bass }
        """;

    /// <summary>Poisons (RULES §5.4): return from AppendSection on the missing name, as it did,
    /// and both facts go red — bass's C follows its A, the row's G follows its G.</summary>
    [Fact]
    public void Twin_PadsAPartAndAChordRow_ThatWriteNoBlockForASection()
    {
        var ly = Twin(NoBlockForB);
        // Bass: A's two bars, then B's two silent bars under B's own mark, then C.
        Assert.Matches(@"g1 \|\s*\\mark \\markup \\box ""B"" s1 \|\s*s1 \|\s*\\mark \\markup \\box ""C"" g2 g \|", ly);
        // The chord row: two silent \chordmode bars between A's g1 and C's g1.
        Assert.Matches(@"c1 \|\s*g1 \|\s*s1 \|\s*s1 \|\s*g1 \|", ly);
        // The part that writes every section is untouched: no spacer bar in melody.
        Assert.Matches(@"d''1 \|\s*\\mark \\markup \\box ""C"" e''2 e'' \|", ly);
    }

    /// <summary>The section's own header shapes the silence: its <c>time</c> is the bar's
    /// length and its <c>partial</c> the first bar's, for the voice and the row alike.</summary>
    [Fact]
    public void Twin_ASilentPlay_TakesTheSectionHeadersMeterAndPickup()
    {
        // By-section spelling: the header is the SECTION's, so every voice of B reads it.
        var ly = Twin("""
            octave absolute
            part melody { clef treble }
            part bass { clef bass }
            section A { melody { g'2 g' | } bass { c2 e | } chords prog { C | } }
            section B { time 3/4  partial 4  melody { c''4 | d''2. | } }
            section C { melody { e''2 e'' | } bass { g2 g | } chords prog { G | } }
            form { A B C }
            score { chords prog  staff melody  staff bass }
            """);
        Assert.Matches(@"c2 e \|\s*\\mark \\markup \\box ""B"" \\time 3/4 \\partial 4 s4 \|\s*s2\. \|\s*\\time 4/4 \\mark \\markup \\box ""C"" g2 g \|", ly);
        Assert.Matches(@"c1 \|\s*s4 \|\s*s2\. \|\s*g1 \|", ly);
    }

    // ---------------------------------------------------------------- MIDI

    [Fact]
    public void Midi_PlaysBAfterThePaddedBar_WhenAChordRowMakesALonger()
    {
        // Page: A = 2 bars, B starts at bar 3 = tick 8 (quarters). Before: B at tick 4.
        var notes = Notes(ChordRowLonger);
        Assert.Equal(new[] { 0, 2, 8, 10 }, notes.Select(n => n.Tick).ToArray());
    }

    [Fact]
    public void Midi_ASectionPickup_ShortensEveryPartsFirstBar()
    {
        // `partial 2`: the section is 2 + 4 + 4 = 10 quarters on the page, whichever part
        // is played first. The empty second part used to open with a FULL bar (the first
        // lane had spent the pickup) and pushed the second A to quarter 12.
        var notes = Notes("""
            time 4/4
            part melody
            part X
            section A { partial 2  melody { c'4 d' | e'2 f' | g'2 g' | }  X { | | | } }
            form { A A }
            score { staff melody }
            """);
        Assert.Equal(new[] { 0, 1, 2, 4, 6, 8, 10, 11, 12, 14, 16, 18 }, notes.Select(n => n.Tick).ToArray());
    }

    [Fact]
    public void Midi_TheLongerChordRow_SoundsOnThePagesBars()
    {
        // The row itself: Dm7 on A's bar 1, G7 on its bar 2, Cmaj7 where B starts (tick 8).
        var file = new MidiExporter().Export(SyntaxTree.Parse(ChordRowLonger));
        var onsets = file.Tracks.Single(t => t.Name == "prog (chords)").Notes
            .Select(n => n.StartTick / file.TicksPerQuarterNote).Distinct().OrderBy(t => t).ToArray();
        Assert.Equal(new[] { 0, 4, 8 }, onsets);
    }

    [Fact]
    public void Midi_PartAgainstPart_StillAlignsThroughTheLanes()
    {
        var notes = Notes(PartLonger);
        // melody: 0, 2, then B at 8, 10; bass: 0, 2, 4, 6, then B at 8, 10.
        Assert.Equal(new[] { 0, 0, 2, 2, 4, 6, 8, 8, 10, 10 }, notes.Select(n => n.Tick).ToArray());
    }

    // ---------------------------------------------------------------- MusicXML

    [Fact]
    public void Xml_EveryPartHasTheSameMeasureCount()
    {
        var doc = Xml(PartLonger);
        Assert.Equal(2, doc.Parts.Count);
        Assert.Equal(3, doc.Parts[0].Measures.Count);
        Assert.Equal(3, doc.Parts[1].Measures.Count);
        // The padding measure is a bar of silence: one whole rest.
        var padded = doc.Parts[0].Measures[1];
        Assert.Single(padded.Notes);
        Assert.True(padded.Notes[0].IsRest);
    }

    [Fact]
    public void Xml_PadsThePart_WhenAChordRowMakesTheSectionLonger()
    {
        var doc = Xml(ChordRowLonger);
        var melody = doc.Parts.Single(p => p.Measures.Count > 0);
        Assert.Equal(3, melody.Measures.Count);
        // The padding bar is a bar of silence — carrying the row's G7, the harmony the bar
        // exists for (2026-09-29: a placed row's symbols are the staff's <harmony> elements).
        var padded = melody.Measures[1];
        Assert.True(padded.Notes.Single(n => n.RawElement == null).IsRest);
        Assert.Contains("<root-step>G</root-step>", padded.Notes.Single(n => n.RawElement != null).RawElement!.ToString());
        // …and numbered in sequence: the empty measure the closing bar line opened is not counted.
        Assert.Equal(new[] { 1, 2, 3 }, melody.Measures.Select(m => m.Number));
    }

    /// <summary>The roots of a measure's harmonies, in stream order, as one string.</summary>
    private static string Roots(MusicXmlMeasure m) => string.Concat(m.Notes
        .Where(n => n.RawElement != null)
        .Select(n => n.RawElement!.Descendants("root-step").Single().Value));

    /// <summary>
    /// A part with NO block for a section gets that section's bars as silence, so every part's
    /// measure N stays the same bar. MEASURED 2026-09-29 (HANDOFF §1.1 第668's hole): bass
    /// exported B's two bars nowhere — its C followed its A, three measures against the
    /// melody's five — while the twin and the MIDI padded them.
    /// </summary>
    [Fact]
    public void Xml_PadsAPart_ThatWritesNoBlockForASection()
    {
        var doc = Xml(NoBlockForB);
        var melody = doc.Parts.Single(p => p.Name == "melody");
        var bass = doc.Parts.Single(p => p.Name == "bass");
        Assert.Equal(5, melody.Measures.Count);
        Assert.Equal(5, bass.Measures.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, bass.Measures.Select(m => m.Number));
        // B's two bars: a whole rest each, nothing else.
        foreach (var m in bass.Measures.Skip(2).Take(2))
            Assert.True(m.Notes.Single().IsRest);
        // C's two half notes follow them, not A's bars.
        Assert.Equal(2, bass.Measures[4].Notes.Count(n => !n.IsRest && n.RawElement == null));
        // The row over the melody: C and G on A's bars, G on C's, nothing on B's silent bars.
        Assert.Equal(new[] { "C", "G", "", "", "G" }, melody.Measures.Select(Roots));
        Assert.All(bass.Measures, m => Assert.Equal("", Roots(m)));
    }

    /// <summary>
    /// A placed row over a part that writes nothing in the play lands on the part's silent
    /// bars — the other half of 第668's hole: EmitPendingChordRows had no measure of that
    /// play to put B's F and G in, and dropped them.
    /// </summary>
    [Fact]
    public void Xml_ARowOverAPartSilentInThePlay_LandsOnItsSilentBars()
    {
        var doc = Xml("""
            octave absolute
            part melody { clef treble }
            section A { melody { c'1 | } chords prog { C | } }
            section B { chords prog { F | G | } }
            section C { melody { e'1 | } chords prog { C | } }
            form { A B C }
            score { chords prog  staff melody }
            """);
        var melody = doc.Parts.Single(p => p.Name == "melody");
        Assert.Equal(4, melody.Measures.Count);
        Assert.Equal(new[] { "C", "F", "G", "C" }, melody.Measures.Select(Roots));
        Assert.True(melody.Measures[1].Notes.Single(n => n.RawElement == null).IsRest);
        Assert.True(melody.Measures[2].Notes.Single(n => n.RawElement == null).IsRest);
        Assert.Equal(new[] { 1, 2, 3, 4 }, melody.Measures.Select(m => m.Number));
    }

    /// <summary>The silent play takes the section header's meter and pickup, as the twin's
    /// does (<see cref="Twin_ASilentPlay_TakesTheSectionHeadersMeterAndPickup"/>): a quarter
    /// pickup bar, implicit, then a 3/4 bar, then C back in 4/4.</summary>
    [Fact]
    public void Xml_ASilentPlay_TakesTheSectionHeadersMeterAndPickup()
    {
        var doc = Xml("""
            octave absolute
            part melody { clef treble }
            part bass { clef bass }
            section A { melody { g'2 g' | } bass { c2 e | } chords prog { C | } }
            section B { time 3/4  partial 4  melody { c''4 | d''2. | } }
            section C { melody { e''2 e'' | } bass { g2 g | } chords prog { G | } }
            form { A B C }
            score { chords prog  staff melody  staff bass }
            """);
        var bass = doc.Parts.Single(p => p.Name == "bass");
        Assert.Equal(4, bass.Measures.Count);
        int divisions = bass.Measures[0].Attributes!.Divisions!.Value;
        var pickup = bass.Measures[1];
        Assert.True(pickup.Implicit);
        Assert.Equal(3, pickup.Attributes?.TimeBeats);
        Assert.Equal(4, pickup.Attributes?.TimeBeatType);
        Assert.Equal(divisions, pickup.Notes.Single().Duration);          // a quarter's rest
        Assert.Equal(3 * divisions, bass.Measures[2].Notes.Single().Duration); // a 3/4 bar's
        Assert.Equal(4, bass.Measures[3].Attributes?.TimeBeats);
        Assert.Equal(4 * divisions, bass.Measures[3].Notes.Where(n => n.RawElement == null).Sum(n => n.Duration));
    }

    /// <summary>
    /// The written-attributes record is PER PART: a second part that writes a section with
    /// its own <c>time</c> states it too, and neither part repeats its clef at a section it
    /// did not change it in. MEASURED 2026-09-29 (Lab sessions/p684/probes/silentmeter2):
    /// one exporter-wide record made the bass's B and C carry no <c>&lt;time&gt;</c> — the
    /// melody had "already written" 3/4 and 4/4 — and made both parts' C open with a second
    /// clef, the other part's being the last one written.
    /// </summary>
    [Fact]
    public void Xml_ASecondPart_StatesTheSectionsMeterToo_AndRepeatsNoClef()
    {
        var doc = Xml("""
            octave absolute
            part melody { clef treble }
            part bass { clef bass }
            section A { melody { g'2 g' | } bass { c2 e | } chords prog { C | } }
            section B { time 3/4  partial 4  melody { c''4 | d''2. | } bass { c4 | d2. | } }
            section C { melody { e''2 e'' | } bass { g2 g | } chords prog { G | } }
            form { A B C }
            score { chords prog  staff melody  staff bass }
            """);
        foreach (var part in doc.Parts.Where(p => p.Name is "melody" or "bass"))
        {
            Assert.Equal(4, part.Measures.Count);
            Assert.Equal((3, 4), (part.Measures[1].Attributes?.TimeBeats, part.Measures[1].Attributes?.TimeBeatType));
            Assert.Null(part.Measures[2].Attributes);
            Assert.Equal((4, 4), (part.Measures[3].Attributes?.TimeBeats, part.Measures[3].Attributes?.TimeBeatType));
            // The clef is stated once, in the opening bar; no section restates it.
            Assert.All(part.Measures.Skip(1), m => Assert.Null(m.Attributes?.ClefSign));
        }
    }
}
