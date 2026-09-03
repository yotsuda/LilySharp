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
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The chords { } block: structured chord entry (root + quality → interval
/// set + auto-named symbol), displayed above the staff and timing-aligned.
/// LILYPOND-REF: scm/chord-entry.scm; ly/engraver-init.ly ChordNames.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChordNamesTests
{
    private const string Sharp = "♯"; // ♯
    private const string Flat = "♭";  // ♭

    // ---- Structured model: naming ------------------------------------------

    [Theory]
    [InlineData(0, 0, ChordQuality.Major, "C")]
    [InlineData(5, 0, ChordQuality.Minor, "Am")]
    [InlineData(4, 0, ChordQuality.Dominant7, "G7")]
    [InlineData(1, 0, ChordQuality.Minor7, "Dm7")]
    [InlineData(0, 0, ChordQuality.Major7, "Cmaj7")]
    [InlineData(4, 0, ChordQuality.Sus4, "Gsus4")]
    [InlineData(0, 0, ChordQuality.Diminished, "Cdim")]
    public void DisplayName_FromStructure(int step, int alter, ChordQuality q, string expected)
    {
        Assert.Equal(expected, new ChordStructure(step, alter, q).DisplayName);
    }

    [Fact]
    public void DisplayName_RendersAccidentalsAndBass()
    {
        Assert.Equal("C" + Sharp, new ChordStructure(0, 1, ChordQuality.Major).DisplayName);
        Assert.Equal("B" + Flat + "7", new ChordStructure(6, -1, ChordQuality.Dominant7).DisplayName);
        // C/G slash bass.
        Assert.Equal("C/G", new ChordStructure(0, 0, ChordQuality.Major, BassStep: 4).DisplayName);
    }

    [Theory]
    [InlineData(ChordQuality.Major, new[] { 0, 4, 7 })]
    [InlineData(ChordQuality.Minor, new[] { 0, 3, 7 })]
    [InlineData(ChordQuality.Dominant7, new[] { 0, 4, 7, 10 })]
    [InlineData(ChordQuality.Major7, new[] { 0, 4, 7, 11 })]
    [InlineData(ChordQuality.Diminished7, new[] { 0, 3, 6, 9 })]
    public void Intervals_AreTheChordTones(ChordQuality q, int[] expected)
    {
        Assert.Equal(expected, new ChordStructure(0, 0, q).Intervals);
    }

    // Fifthless voicings: a chord with the perfect 5th dropped still names as that
    // chord — <1 3 7> in C = C-E-B = Cmaj7, and the root+3rd dyad <c e>/<c ees> = C/Cm.
    [Theory]
    [InlineData(new[] { 0, 4 }, ChordQuality.Major)]
    [InlineData(new[] { 0, 3 }, ChordQuality.Minor)]
    [InlineData(new[] { 0, 4, 11 }, ChordQuality.Major7)]
    [InlineData(new[] { 0, 4, 10 }, ChordQuality.Dominant7)]
    [InlineData(new[] { 0, 3, 10 }, ChordQuality.Minor7)]
    [InlineData(new[] { 0, 3, 11 }, ChordQuality.MinorMajor7)]
    public void Recognize_FifthlessSeventhShells(int[] intervals, ChordQuality expected)
    {
        Assert.True(ChordQualityRegistry.TryRecognize(intervals, out var q));
        Assert.Equal(expected, q);
    }

    [Theory]
    [InlineData("m", ChordQuality.Minor)]
    [InlineData("min", ChordQuality.Minor)]
    [InlineData("maj7", ChordQuality.Major7)]
    [InlineData("m7", ChordQuality.Minor7)]
    [InlineData("7", ChordQuality.Dominant7)]
    [InlineData("sus4", ChordQuality.Sus4)]
    [InlineData("m7-5", ChordQuality.HalfDiminished7)] // alterations spell +/- (audit 8.1)
    [InlineData("+", ChordQuality.Augmented)]          // the jazz augmented triad, C+
    public void Registry_ResolvesQualityTokens(string token, ChordQuality expected)
    {
        Assert.True(ChordQualityRegistry.TryResolve(token, out var q));
        Assert.Equal(expected, q);
    }

    [Fact]
    public void Registry_EmptyTokenIsMajor_UnknownFails()
    {
        Assert.True(ChordQualityRegistry.TryResolve(null, out var q));
        Assert.Equal(ChordQuality.Major, q);
        Assert.False(ChordQualityRegistry.TryResolve("nonsense", out _));
    }

    // ---- LP chord-entry realization (corpus: chord-name-entry.ly) ----------

    /// <summary>
    /// Pins every quality token the registry shares with LP's chord-entry test
    /// (input/regression/chord-name-entry.ly) to the note chord LilyPond 2.26.0
    /// actually realizes (dumped via \displayLilyMusic; LP c' is spelled c here —
    /// the usual one-apostrophe shift). Letters come from the diatonic step,
    /// accidentals from the semitone, octave marks from OctaveUp.
    /// </summary>
    [Theory]
    [InlineData("", "c e g")]
    [InlineData("7", "c e g bes")]
    [InlineData("m", "c ees g")]
    [InlineData("m7", "c ees g bes")]
    [InlineData("aug", "c e gis")]
    [InlineData("maj7", "c e g b")]
    [InlineData("dim", "c ees ges")]
    [InlineData("dim7", "c ees ges beses")]
    [InlineData("sus4", "c f g")]
    [InlineData("sus2", "c d g")]
    [InlineData("6", "c e g a")]
    [InlineData("m6", "c ees g a")]
    [InlineData("7sus4", "c f g bes")]
    [InlineData("9", "c e g bes d'")]
    public void LpEntryRealization_MatchesLilyPond(string token, string expected)
        => AssertRealizes(token, expected);

    /// <summary>The tones a registered quality realizes to, spelled the way a .lys
    /// writes them. One copy, so three theories cannot drift apart.</summary>
    private static void AssertRealizes(string token, string expected)
    {
        Assert.True(ChordQualityRegistry.TryResolve(token, out var q));
        var spelled = new ChordStructure(0, 0, q).Tones.Select(t =>
            "cdefgab"[t.Step] + (t.Alter switch
            {
                2 => "isis", 1 => "is", 0 => "", -1 => "es", -2 => "eses", _ => "?",
            }) + new string('\'', t.OctaveUp));
        Assert.Equal(expected, string.Join(" ", spelled));
    }

    // The remaining entries of chord-name-entry.ly use LilyPond ENTRY SYNTAX that Lily#
    // does not have: step alterations (3-, 3+, 5+.3-) and '^' removals. They stay
    // unresolved tokens; the corpus twin hand-expands their LP realization
    // (audit/lp-regression).
    // ⚠️ 11 / 13 / m13 were listed here too, and that conflated two different things:
    // they are not LilyPond entry syntax, they are ordinary chart symbols that happen to
    // appear in the same LP file. They are registered now — see TheExtensions_Realize.
    [Theory]
    [InlineData("3-")]
    [InlineData("3+")]
    [InlineData("5+.3-")]
    [InlineData("7^5")]
    public void LpEntryForms_OutsideTheVocabulary_DoNotResolve(string token)
    {
        Assert.False(ChordQualityRegistry.TryResolve(token, out _));
    }

    // The ALTERED tensions the registry's own rule is about: the alteration is '+'/'-',
    // never '#'/'b' (those belong to the root and the bass, which is what keeps "Bb9"
    // unambiguous). Until these were registered, a chords ROW printed them through its
    // raw-suffix fallback — so they looked right and did not play — while '@chord'
    // refused them outright: one quantity, two answers.
    // ⚠️ The plain extensions 11 / 13 / m13 stay OUT, and the theory above says so.
    [Theory]
    [InlineData("7-5", "c e ges bes")]
    [InlineData("7+5", "c e gis bes")]
    [InlineData("7-9", "c e g bes des'")]
    [InlineData("7+9", "c e g bes dis'")]
    [InlineData("7+11", "c e g bes fis'")]
    [InlineData("add9", "c e g d'")]
    public void AlteredTensions_ResolveAndRealize(string token, string expected)
        => AssertRealizes(token, expected);

    // ⚠️ A DECLARED DEPARTURE FROM LILYPOND: a thirteenth does NOT carry the eleventh.
    // LilyPond's ':13' stacks every third mechanically (… d' f' a'), and that natural
    // eleventh sits a semitone from the major third — an interval no player voices and
    // no chart means. The eleventh appears only when the symbol names it ('11', 'm11').
    // Nothing measures this against LilyPond: the .ly exporter writes chord names as
    // markup and never emits a \chordmode entry, so no twin asks LP to realize one.
    [Theory]
    [InlineData("11", "c e g bes d' f'")]
    [InlineData("13", "c e g bes d' a'")]
    [InlineData("m11", "c ees g bes d' f'")]
    [InlineData("m13", "c ees g bes d' a'")]
    [InlineData("maj13", "c e g b d' a'")]
    public void TheExtensions_Realize(string token, string expected)
        => AssertRealizes(token, expected);

    // The '#'/'b' spellings a player would type stay refused — that is the invariant,
    // not an oversight. The diagnostic that redirects them lives in
    // AnnotationNameValidatorTests.
    [Theory]
    [InlineData("7b9")]
    [InlineData("7#5")]
    [InlineData("m7b5")]
    public void TheSharpFlatSpellingOfATension_StaysUnregistered(string token)
    {
        Assert.False(ChordQualityRegistry.TryResolve(token, out _));
    }

    // ---- Parsing + collection + timing alignment ---------------------------

    /// <summary>A chord ROW rides the multi-staff pipeline (RenderSpec.HasChordRow);
    /// the single-staff Collect() never sees one, so these tests collect through
    /// the score's spec the way the renderer does.</summary>
    private static Core.Svg.Model.MultiStaffScore CollectWithRow(SyntaxTree tree)
        => new MeasureCollector().CollectMultiStaff(
            tree, Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!);

    private const string LeadSheet =
        "key c major\npart m { clef treble }\n" +
        "section Main {\n  m {\n    time 4/4\n    c4 d e f | g a b c |\n  }\n" +
        "  chords prog {\n    C Am | F G7 |\n  }\n}\n" +
        "form main { Main }\nscore \"x\" { chords prog  staff m }\n";

    [Fact]
    public void ChordNames_ParseWithoutErrors()
    {
        var tree = SyntaxTree.Parse(LeadSheet);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<ChordPartBlockSyntax>().Where(b => b.PartName == "prog"));
    }

    [Fact]
    public void Collector_EmitsTimingAlignedAutoNamedChords()
    {
        var score = CollectWithRow(SyntaxTree.Parse(LeadSheet));
        var chords = score.ChordNames.OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble()).ToList();

        Assert.Equal(4, chords.Count);
        Assert.Equal(new[] { "C", "Am", "F", "G7" }, chords.Select(c => c.ChordText));
        // Mid-bar chords (Am, G7) land at timing 1/2; bar-start chords at 0.
        Assert.Equal(new[] { 0, 0, 1, 1 }, chords.Select(c => c.MeasureIndex));
        Assert.Equal(new[] { 0.0, 0.5, 0.0, 0.5 }, chords.Select(c => c.Timing.ToDouble()));
        Assert.All(chords, c => Assert.True(c.UseTiming));
        // The structure (interval set) is carried for future notes / fret diagrams.
        Assert.Equal(new[] { 0, 4, 7, 10 }, chords[3].Structure!.Intervals); // G7
    }

    // ---- Slash bass — corpus: chord-names-bass.ly

    [Fact]
    public void SlashBass_ParsesAsOneEntry_AndCrossesTheSlashGlued()
    {
        var src = "section Main {\n  m { time 4/4 c2 d2 | }\n" +
                  "  chords prog { Fmaj7/E Fmaj7/G | }\n}\n" +
                  "form main { Main }\nscore \"x\" { chords prog  staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var score = CollectWithRow(tree);
        var chords = score.ChordNames.OrderBy(c => c.Timing.ToDouble()).ToList();
        Assert.Equal(2, chords.Count);
        Assert.Equal(new[] { "Fmaj7/E", "Fmaj7/G" }, chords.Select(c => c.ChordText));
    }

    [Fact]
    public void TryParseChordEntry_AddedBassWentWithTheColonFormat()
    {
        // '/+' (LP's CHORD_BASS) retired with the ':' entry: '+' spells altered
        // tensions and the augmented triad now, and LP prints /bass and /+bass
        // identically anyway. BassIsAdded stays on the model for the MusicXML
        // importer, but no Lily# spelling sets it.
        Assert.True(ChordStructure.TryParseChordEntry("Fmaj7/E", out var inv));
        Assert.False(inv.BassIsAdded);
        Assert.Equal("Fmaj7/E", inv.DisplayName);
        Assert.False(ChordStructure.TryParseChordEntry("Fmaj7/+E", out _));
        // '/' with no pitch after it is not an entry either.
        Assert.False(ChordStructure.TryParseChordEntry("Fmaj7/", out _));
    }

    [Fact]
    public void ChordRowAboveAGroup_StandsAboveTheTopStaff()
    {
        // corpus: chord-names-in-grand-staff.ly — the name prints above the TOP
        // staff. The row is the group's leading band (staff index 0), never hung
        // between the staves. (The nameless auto-attach form is gone - LYS0032.)
        var src = "part rh { clef treble }\npart lh { clef bass }\n" +
                  "section Main {\n  rh { a4 a a a | }\n  lh { a,,4 a,, a,, a,, | }\n" +
                  "  chords prog { F | }\n}\n" +
                  "form main { Main }\n" +
                  "score \"x\" { chords prog  grandStaff { staff rh staff lh } }\n";
        var tree = SyntaxTree.Parse(src);
        var spec = Core.Svg.Collector.RenderSpecParser.FindFirst(tree);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);
        var f = Assert.Single(score.ChordNames);
        Assert.Equal("F", f.ChordText);
        Assert.Equal(0, f.StaffIndex);
    }

    [Fact]
    public void ChordRow_RestsPrintNC_SkipsDoNot()
    {
        // corpus: chord-names-rests.ly — r and R print the no-chord symbol
        // ("N.C."), s prints nothing; all three advance the row. Pre-fix, rests
        // in a chords{} block were silently dropped by stray-token recovery.
        var src = "section Main {\n  m { time 4/4 r1 | s1 | R1 | }\n" +
                  "  chords prog { r | s | R | }\n}\n" +
                  "form main { Main }\nscore \"x\" { chords prog  staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var score = CollectWithRow(tree);
        var items = score.ChordNames.OrderBy(c => c.MeasureIndex).ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, c => Assert.Equal("N.C.", c.ChordText));
        Assert.Equal(new[] { 0, 2 }, items.Select(c => c.MeasureIndex));
    }

    // ---- Note-expansion (editor completion) --------------------------------

    [Theory]
    [InlineData("cmaj7", "<c e g b>")]
    [InlineData("am", "<a c e>")]
    [InlineData("g7", "<g b d f>")]
    [InlineData("cm", "<c ees g>")]      // minor third spells e-flat, not d-sharp
    [InlineData("bes7", "<bes d f aes>")] // Bb7: B♭ D F A♭, correctly spelled
    [InlineData("dm7", "<d f a c>")]
    [InlineData("csus4", "<c f g>")]
    [InlineData("c9", "<c e g bes d>")]   // the 9th voices an octave up via relative
    public void TryParseSymbol_SpellsNoteChord(string word, string expected)
    {
        Assert.True(ChordStructure.TryParseSymbol(word, out var chord));
        Assert.Equal(expected, chord.ToNoteChord());
    }

    [Theory]
    [InlineData("c")]      // bare note — no quality, not a chord
    [InlineData("ees")]    // bare accidental note
    [InlineData("cx")]     // unknown quality token
    [InlineData("h7")]     // not a pitch letter
    public void TryParseSymbol_RejectsNonChords(string word)
    {
        Assert.False(ChordStructure.TryParseSymbol(word, out _));
    }

    [Fact]
    public void Tones_AreSpelledByDegree()
    {
        // Cm: C, E♭, G — letter steps 0/2/4 with the third flattened.
        var tones = new ChordStructure(0, 0, ChordQuality.Minor).Tones;
        Assert.Equal(new[] { 0, 2, 4 }, tones.Select(t => t.Step));
        Assert.Equal(new[] { 0, -1, 0 }, tones.Select(t => t.Alter));
    }

    [Theory]
    // A symbol that lexes as several tokens (identifier + minus + number, or a
    // '#' BadToken in the middle) must be captured WHOLE as one glued run, not
    // truncated to its first token (the "Gm7" bug, in its symbol-format form).
    [InlineData("Gm7-5", "Gm7♭5")]     // half-diminished, minus+number → resolves
    [InlineData("G7sus4", "G7sus4")]   // number + word, one identifier → resolves
    [InlineData("F#m7-5", "F♯m7♭5")]   // '#' splits the run and is re-joined
    [InlineData("Gm7-5-7", "Gm7-5-7")] // unknown extended chord → full text, not "Gm7"
    public void MultiTokenSymbol_IsCapturedWhole(string entry, string expected)
    {
        var src = "section Main {\n  m { time 4/4 c4 d e f | }\n  chords prog { " + entry + " }\n}\n" +
                  "form main { Main }\nscore \"x\" { chords prog  staff m }\n";
        var score = CollectWithRow(SyntaxTree.Parse(src));
        var chord = Assert.Single(score.ChordNames);
        Assert.Equal(expected, chord.ChordText);
    }

    [Fact]
    public void Collector_UnknownQuality_KeepsRawSuffix_NoTones()
    {
        // An extended chord not in the vocabulary still displays (root + raw token)
        // and now resolves a Roman degree from its root, but carries no interval set
        // (unknown tones → no note expansion).
        var src = "section Main {\n  m { time 4/4 c4 d e f | }\n  chords prog { Cweird9 }\n}\n" +
                  "form main { Main }\nscore main \"x\" { chords prog  staff m }\n";
        var score = CollectWithRow(SyntaxTree.Parse(src));
        var chord = Assert.Single(score.ChordNames);
        Assert.Equal("Cweird9", chord.ChordText);
        Assert.NotNull(chord.Structure);
        Assert.Empty(chord.Structure!.Intervals);                      // tones are unknown
        Assert.Equal("Iweird9", chord.Structure.ToRomanNumeral(0, 0)); // root still gives a degree
    }

    [Fact]
    public void ChordStructure_RawSuffix_RomanKeepsTypedSuffix()
    {
        // `c:M7` (jazz major-7th shorthand, not a registry token) keeps its typed
        // suffix and converts the root to a Roman degree: "CM7" → "IM7", not "CM7".
        var s = new LilySharp.Core.Music.ChordStructure(
            0, 0, LilySharp.Core.Music.ChordQuality.Major, RawSuffix: "M7");
        Assert.Equal("CM7", s.DisplayName);
        Assert.Equal("IM7", s.ToRomanNumeral(0, 0)); // C in C major → I, suffix verbatim
    }
}
