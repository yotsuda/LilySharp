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

    [Fact]
    public void TypoNearKnownName_SuggestsIt()
    {
        var diags = Validate("c4@glisando d |");
        var warning = Assert.Single(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Contains("Did you mean '@glissando'?", warning.Message);
    }

    [Fact]
    public void NothingClose_NoSuggestion()
    {
        var diags = Validate("c4@frobnicate d |");
        var warning = Assert.Single(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.DoesNotContain("Did you mean", warning.Message);
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
    [InlineData("c4@ottava d@ottava(bassa) e@loco f |")]
    [InlineData("c4@ped d@ped(off) e@sost f@tre(corde) |")]
    [InlineData("c4@sost(off) d@una(corda) e f |")]
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
