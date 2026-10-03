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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class AnnotationNameValidatorTests
{
    private static IReadOnlyList<Diagnostic> Validate(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var validator = new AnnotationNameValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    // --- Unknown names warn ---

    [Theory]
    [InlineData("c4@glisando d |", "glisando")]      // typo of glissando
    [InlineData("c4@stacato d |", "stacato")]        // typo of staccato
    [InlineData("c4@frobnicate d |", "frobnicate")]  // nothing close
    public void UnknownPlainName_Warns(string source, string name)
    {
        var diags = Validate(source);
        var warning = Assert.Single(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains($"'@{name}'", warning.Message);
    }

    // --- A chord with a good root but an unregistered quality names the REAL problem ---

    [Theory]
    [InlineData("c4@chord(C7b9) d |", "7b9", "7-9")]
    [InlineData("c4@chord(F#m7b5) d |", "m7b5", "m7-5")]
    [InlineData("c4@chord(A7#5) d |", "7#5", "7+5")]
    public void AChordQualitySpelledWithSharpOrFlat_NamesTheAlterationRule(
        string source, string quality, string suggestion)
    {
        var warning = Assert.Single(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        // The half that failed is the QUALITY. Saying "unknown annotation" sent the
        // reader to look at '@chord', which is spelled correctly.
        Assert.Contains($"'{quality}' is not a chord quality", warning.Message);
        Assert.Contains("'+' or '-'", warning.Message);
        Assert.Contains($"'{suggestion}'", warning.Message);
        Assert.DoesNotContain("Unknown annotation", warning.Message);
    }

    [Fact]
    public void AChordQualityWithNoAlternativeSpelling_StatesTheRuleAndGuessesNothing()
    {
        var warning = Assert.Single(Validate("c4@chord(Czz) d |"),
            d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Contains("'zz' is not a chord quality", warning.Message);
        Assert.DoesNotContain("Did you mean", warning.Message);
    }

    [Fact]
    public void AChordWhoseROOTDoesNotParse_IsStillAnOrdinaryUnknownAnnotation()
    {
        // No root to hang a quality on, so the quality message would be a lie; the
        // ordinary unknown-name path (and its own suggestion machinery) is right here.
        var warning = Assert.Single(Validate("c4@chord(zz) d |"),
            d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Contains("Unknown annotation", warning.Message);
    }

    [Theory]
    [InlineData("c4@chord(C7-9) d |")]
    [InlineData("c4@chord(F#m7-5) d |")]
    [InlineData("c4@chord(Cadd9) d |")]
    public void AnAlteredTensionInTheRegisteredSpelling_IsAccepted(string source)
    {
        Assert.DoesNotContain(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);
    }

    [Theory]
    [InlineData("c4@feather.up d |")]    // not a feather direction
    [InlineData("c4@trillspan(begin) d |")]
    [InlineData("c4@finger(x) d |")]      // non-numeric finger
    public void UnknownCompoundName_Warns(string source)
    {
        var diags = Validate(source);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);
    }

    /// <summary>
    /// The retired short spellings warn like any unknown name. Until 2026-09-27 this
    /// validator still listed them while the registry (pre-0.3.0) did not, so they compiled
    /// clean and drew nothing — no H or P, no twin script, no MusicXML technical.
    /// </summary>
    [Theory]
    [InlineData("c4@ho d |")]
    [InlineData("c4@po d |")]
    public void TheRetiredTabTechniqueShortSpellings_Warn(string source)
        => Assert.Single(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);

    [Theory]
    [InlineData("c4@hammerOn d |")]
    [InlineData("c4@pullOff d |")]
    public void TheTabTechniqueNames_AreKnown(string source)
        => Assert.DoesNotContain(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);

    /// <summary>
    /// Annotation names are case-sensitive (owner's decision 2026-09-27): a name written
    /// in another case is unknown, and the warning names the spelling to write.
    /// </summary>
    [Theory]
    [InlineData("c4@hammeron d |", "@hammerOn")]
    [InlineData("c4@pulloff d |", "@pullOff")]
    public void TheTabTechniqueNames_InTheWrongCase_AreUnknown_AndTheRightSpellingIsNamed(
        string source, string canonical)
    {
        var warning = Assert.Single(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Contains($"Names are case-sensitive: write '{canonical}'.", warning.Message);
    }

    /// <summary>
    /// The VALUE words are case-sensitive too (2026-09-27): the annotation is unknown and
    /// the warning names the lower-case spelling — as a value, not a name.
    /// </summary>
    [Theory]
    [InlineData("c4@notehead(TRIANGLE) d |", "@notehead(triangle)")]
    [InlineData("c4@diagram(X32010) d |", "@diagram(x32010)")]
    [InlineData("c4@bend(Full) d |", "@bend(full)")]
    [InlineData("c4@pluck(P) d |", "@pluck(p)")]
    [InlineData("c4@feather(Right) d |", "@feather(right)")]
    [InlineData("c4@arpeggio(BRACKET) d |", "@arpeggio(bracket)")]
    [InlineData("c4@figuredBass(6 S) d |", "@figuredBass(6 s)")]
    public void AValueInTheWrongCase_IsUnknown_AndTheRightSpellingIsNamed(string source, string canonical)
    {
        var warning = Assert.Single(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Contains($"Values are case-sensitive: write '{canonical}'.", warning.Message);
    }

    /// <summary>A value with no lower-case reading gets no case hint — and free text and a
    /// chord symbol, whose case is their content, are not touched at all.</summary>
    [Theory]
    [InlineData("c4@notehead(SQUARE) d |")]
    public void AValueWithNoLowerCaseReading_GetsNoCaseHint(string source)
    {
        var warning = Assert.Single(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.DoesNotContain("case-sensitive", warning.Message);
    }

    [Theory]
    [InlineData("c4@text(\"DOLCE\") d |")]
    [InlineData("c4@mark(\"A\") d |")]
    [InlineData("c4@chord(Dm) d |")]
    public void FreeTextAndChordSymbols_KeepTheirCase(string source)
        => Assert.DoesNotContain(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);

    /// <summary>
    /// <c>@rest</c> prints a note as a rest at that note's pitch, so it has a pitch to
    /// read only on a note. Anywhere else it would be dropped without a word — which is
    /// the failure this validator exists to give a voice to.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/rest-engraver.cc:62-80 process_music — the pitch of
    /// the rest EVENT is what becomes staff-position; there is no pitch on a rest or a
    /// chord to read.</remarks>
    [Theory]
    [InlineData("r4@rest d |")]        // already a rest: no pitch to sit at
    [InlineData("<c e>4@rest d |")]    // a chord has several, and LilyPond takes none
    public void RestAnnotation_OffANote_IsAnError(string source)
    {
        var diags = Validate(source);
        var error = Assert.Single(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("belongs on a note", error.Message);
    }

    [Fact]
    public void RestAnnotation_OnANote_IsAccepted()
    {
        Assert.Empty(Validate("a4@rest c |"));
    }

    [Fact]
    public void TypoNearKnownName_SuggestsIt()
    {
        var diags = Validate("c4@glisando d |");
        var warning = Assert.Single(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Contains("Did you mean '@glissando'?", warning.Message);
    }

    /// <summary>An ottava with an argument the family does not take is told the four
    /// spellings — '@ottava(8va)' got nothing and '@ottava(1)' a guess ("Did you mean
    /// '@ottava'?") until 2026-10-03 (session 762's piano book, HANDOFF ⑼ ⒠).</summary>
    [Theory]
    [InlineData("c4@ottava(8va) d e f@!ottava |")]
    [InlineData("c4@ottava(1) d e f@!ottava |")]
    [InlineData("c4@quindicesima(15ma) d e f@!ottava |")]
    public void AnOttavaWithAnArgumentItDoesNotTake_IsToldTheSpellings(string music)
    {
        var warning = Assert.Single(Validate(music), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Contains("An ottava is written '@ottava' (8va), '@ottava(bassa)' (8vb), '@quindicesima' (15ma) "
                        + "or '@quindicesima(bassa)' (15mb), and closed by '@!ottava'.", warning.Message);
        Assert.DoesNotContain("Did you mean", warning.Message);
        // The case-only slip keeps its own, more exact hint.
        Assert.Contains("write '@ottava(bassa)'",
            Assert.Single(Validate("c4@ottava(BASSA) d e f@!ottava |"), d => d.Code == DiagnosticCodes.UnknownAnnotation).Message);
    }

    /// <summary>
    /// A suggestion the reader cannot type is worse than no suggestion. A
    /// compound annotation is keyed internally as one dotted string
    /// ("notehead.x"), but the source spells the argument in parentheses — so
    /// '@notehed(x)' used to answer "did you mean '@notehead.x'?", and following
    /// that advice produced "Undefined variable or phrase: 'x'".
    /// </summary>
    [Fact]
    public void CompoundSuggestion_IsSpelledTheWayItIsTyped()
    {
        var diags = Validate("c4@notehed(x) d |");
        var warning = Assert.Single(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);

        Assert.Contains("Did you mean '@notehead(x)'?", warning.Message);
        Assert.Contains("'@notehed(x)'", warning.Message);   // and so is the name reported
        Assert.DoesNotContain("notehead.x", warning.Message);
    }

    /// <summary>
    /// ⚠️ The annotation is QUOTED from the source, not rebuilt from its internal name.
    /// The reconstruction turns every '.' into a ' ', so a written dot came back as a
    /// space: '@figuredBass(6.4)' was reported as '@figuredBass(6 4)' — and '@figuredBass(6 4)' is a VALID
    /// spelling, so the message named a working annotation as the broken one. Nothing
    /// observed this until the figured bass began refusing a written dot
    /// (VALUE_SITE_AUDIT §9.5.3 ⑴), which is what made the misreport reachable.
    /// </summary>
    [Theory]
    [InlineData("c4@figuredBass(6.4) d |", "'@figuredBass(6.4)'", "@figuredBass(6 4)")]
    [InlineData("c4@figuredBass(6.s) d |", "'@figuredBass(6.s)'", "@figuredBass(6 s)")]
    public void TheUnknownAnnotation_IsQuotedFromTheSource(
        string source, string written, string reconstruction)
    {
        var diags = Validate(source);
        var warning = Assert.Single(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);

        Assert.Contains(written, warning.Message);
        // ⚠️ And the reconstruction — a spelling that WORKS — must not be named as the
        // unknown one. It may still appear as the suggestion, so only the report is checked.
        var report = warning.Message[..warning.Message.IndexOf("— it is ignored", StringComparison.Ordinal)];
        Assert.DoesNotContain(reconstruction, report);
    }

    /// <summary>
    /// Every suggestion the validator can make must be a spelling that actually
    /// compiles on a note. This is what caught the whole dotted-name family
    /// (@ped.off, @notehead.x, @fig.6, @chord.C, @to.coda …) being unusable.
    /// </summary>
    [Fact]
    public void EverySuggestionCandidate_CompilesAsWritten()
    {
        var failures = new List<string>();
        foreach (var candidate in AnnotationNameValidator.SuggestionNames)
        {
            var spelling = AnnotationNameValidator.SourceSpelling(candidate);
            // MusicSource wraps the music in the part/section/score a real file
            // needs; a bare "c4@… d |" would fail as top-level music for every
            // candidate and prove nothing.
            var tree = MusicSource.Parse($"c4@{spelling} d |");

            var problems = tree.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)
                .ToList();

            var validator = new AnnotationNameValidator();
            validator.Validate(tree);
            problems.AddRange(validator.Diagnostics
                .Where(d => d.Code == DiagnosticCodes.UnknownAnnotation)
                .Select(d => d.Message));

            if (problems.Count > 0)
                failures.Add($"@{spelling} -> {string.Join("; ", problems)}");
        }
        Assert.True(failures.Count == 0,
            "The validator can suggest annotations that do not compile as written:\n"
            + string.Join("\n", failures));
    }

    /// <summary>
    /// A suggestion has to be close RELATIVE TO WHAT WAS WRITTEN (2026-09-28): a third of the
    /// name's length in edits, at most two, and for a one- or two-letter name only a swapped
    /// pair. A flat "within two edits" put every two-letter word near some two-letter dynamic —
    /// '@ho' answered "did you mean '@sf'?".
    /// </summary>
    [Theory]
    [InlineData("c4@fs d |", "@sf")]            // two letters: a swapped pair is a typo
    [InlineData("c4@acent d |", "@accent")]      // five letters: one edit
    [InlineData("c4@tenuot d |", "@tenuto")]     // a swap counts as one edit
    [InlineData("c4@stacatto d |", "@staccato")] // eight letters: two edits
    public void ACloseTypo_IsSuggested(string source, string suggestion)
    {
        var warning = Assert.Single(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Contains($"Did you mean '{suggestion}'?", warning.Message);
    }

    [Theory]
    [InlineData("c4@ho d |")]      // two letters, two edits from '@sf' / '@fp'
    [InlineData("c4@po d |")]
    [InlineData("c4@x d |")]
    [InlineData("c4@trl d |")]     // three letters, two edits from '@trill'
    [InlineData("c4@ped d |")]
    public void AShortNameFarFromEveryKnownOne_GetsNoSuggestion(string source)
    {
        var warning = Assert.Single(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.DoesNotContain("Did you mean", warning.Message);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(20, 2)]
    public void TheAllowedTypoDistance_GrowsWithTheName(int length, int allowed)
        => Assert.Equal(allowed, AnnotationNameValidator.AllowedTypoDistance(length));

    [Fact]
    public void NothingClose_NoSuggestion()
    {
        var diags = Validate("c4@frobnicate d |");
        var warning = Assert.Single(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.DoesNotContain("Did you mean", warning.Message);
    }

    [Fact]
    public void Harmonic_IsAKnownArticulation()
    {
        // '@harmonic' (the familiar guitar/lead-sheet term for the ○ circle) is a
        // known alias for '@flageolet' — no unknown-annotation warning. (It once
        // warned AND absurdly suggested itself; the self-suggestion guard in
        // FindSuggestion, and this registration, both close that.)
        var diags = Validate("c4@harmonic d |");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);
    }

    // --- Known names stay silent ---

    [Theory]
    // Articulations & ornaments (full words)
    [InlineData("c4@staccato d@tenuto e@accent f@marcato |")]
    [InlineData("c4@trill d@prall e@mordent f@turn |")]
    [InlineData("c4@fermata d@marcato e@tenuto f@portato |")]
    // Music marks (plain + compound)
    [InlineData("c4@segno d@coda e@fine f |")]
    [InlineData("c4@mark(\"A\") d@mark(\"12\") e f |")]
    [InlineData("c4@rit d@accel e@cresc f@dim |")]
    [InlineData("c4@ottava d@ottava(bassa) e@!ottava f |")]
    [InlineData("c4@sustain d@!sustain e@sostenuto f@treCorde |")]
    [InlineData("c4@!sostenuto d@unaCorda e f |")]
    [InlineData("c4@ds(al fine) d e f |")]
    // Feature annotations
    [InlineData("c4@glissando d e f |")]
    [InlineData("c4@startTrillSpan d e@stopTrillSpan f |")]
    [InlineData("c4@courtesy d@cue e@cross f@arpeggio |")]
    [InlineData("c4@laissezVibrer d@repeatTie e f |")]
    [InlineData("c16@feather(right) d e f g a b c' |")]
    [InlineData("c4@finger(1) d@finger(3) e f |")]
    [InlineData("c4@figuredBass(6) d@figuredBass(6 4) e f |")]
    [InlineData("c4@chord(C) d@chord(Am) e f |")]
    // Dynamics are parser-gated, never unknown
    [InlineData("c4@ff d@p e@mf f |")]
    public void KnownNames_NoWarning(string source)
    {
        var diags = Validate(source);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);
    }

    // --- Rehearsal mark labels must be quoted: @mark("A"), not @mark(A) ---

    [Theory]
    [InlineData("c4@mark(A) d |")]
    [InlineData("c4@mark(12) d |")]
    [InlineData("c4@mark(Verse) d |")]
    public void BareRehearsalMark_RequiresQuotes(string source)
    {
        var diags = Validate(source);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MarkLabelNotQuoted);
    }

    [Theory]
    [InlineData("c4@mark(\"A\") d |")]
    [InlineData("c4@mark(\"D.S.\") d |")]
    [InlineData("c4@mark(\"12\") d |")]
    public void QuotedRehearsalMark_NoQuoteError(string source)
    {
        var diags = Validate(source);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.MarkLabelNotQuoted);
    }

    // --- Sweep: every shipped sample must be free of unknown annotations.
    // This pins the validator's known-name registry to what the collector
    // actually consumes: a new annotation added to the collector but not the
    // registry makes its sample fail here. ---

    [Fact]
    public void AllSamples_HaveNoUnknownAnnotations()
    {
        // Sweep BOTH the user-facing samples/ playground and the snapshot
        // fixtures (split out to LilySharp.Tests/Fixtures), so the annotation
        // registry stays pinned for every shipped .lys regardless of location.
        var offenders = new List<string>();
        foreach (var dir in EnumerateSampleRoots())
            foreach (var file in Directory.EnumerateFiles(dir, "*.lys", SearchOption.AllDirectories))
            {
                var diags = Validate(File.ReadAllText(file));
                foreach (var d in diags.Where(d => d.Code == DiagnosticCodes.UnknownAnnotation))
                    offenders.Add($"{Path.GetFileName(file)}: {d.Message}");
            }
        Assert.True(offenders.Count == 0,
            "Unknown annotations in samples:\n" + string.Join("\n", offenders));
    }

    private static IEnumerable<string> EnumerateSampleRoots()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var roots = new List<string>();
            var samples = Path.Combine(dir, "samples");
            if (Directory.Exists(samples)) roots.Add(samples);
            var fixtures = Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (Directory.Exists(fixtures)) roots.Add(fixtures);
            if (roots.Count > 0)
                return roots;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find samples/ or LilySharp.Tests/Fixtures/ directory");
    }
}
