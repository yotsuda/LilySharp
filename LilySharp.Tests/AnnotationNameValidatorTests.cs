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
    /// space: '@fig(6.4)' was reported as '@fig(6 4)' — and '@fig(6 4)' is a VALID
    /// spelling, so the message named a working annotation as the broken one. Nothing
    /// observed this until the figured bass began refusing a written dot
    /// (VALUE_SITE_AUDIT §9.5.3 ⑴), which is what made the misreport reachable.
    /// </summary>
    [Theory]
    [InlineData("c4@fig(6.4) d |", "'@fig(6.4)'", "@fig(6 4)")]
    [InlineData("c4@fig(6.s) d |", "'@fig(6.s)'", "@fig(6 s)")]
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
    [InlineData("c4@fig(6) d@fig(6 4) e f |")]
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
