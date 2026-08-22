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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An <c>override</c> / <c>revert</c> naming a property the engine never reads changed
/// nothing and said nothing — LYS1029 says so now. These tests state today's ACCEPTED SET,
/// so that growing <see cref="SupportedGrobOverrides"/> is a visible, deliberate act: a
/// property that starts working moves a case from the rejected list to the accepted one.
/// </summary>
[Trait("Category", "Unit")]
public class OverrideVocabularyValidatorTests
{
    private static IReadOnlyList<Diagnostic> Errors(string music)
    {
        var source = $"part m {{ section A {{ {music} }} }} form main {{ A }} score main {{ staff m }}";
        var validator = new OverrideVocabularyValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.OverridePropertyUnsupported
                        && d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    [Theory]
    // The four the engine actually reads, all with a live consumer in
    // SharedRenderer.Noteheads (transparent, and color via ResolveColor). The color rows
    // were missing until 2026-08-23 while their reader was live — a correctly coloured
    // score shipped with an LYS1029 error attached (GRAMMAR_AUDIT §4.2).
    [InlineData("override NoteHead.transparent = true c4 d e f")]
    [InlineData("override Stem.transparent = true c4 d e f")]
    [InlineData("override NoteHead.color = red c4 d e f")]
    [InlineData("override Stem.color = \"#00ff00\" c4 d e f")]
    [InlineData("override NoteHead.transparent = true c4 revert NoteHead.transparent d e f")]
    [InlineData("once override Stem.transparent = true c4 d e f")]
    [InlineData("c4 d e f")]                                   // no override at all
    public void SupportedProperties_NoError(string music) =>
        Assert.Empty(Errors(music));

    [Theory]
    // Real LP grobs and properties the engine does not read. These are the spellings that
    // used to engrave byte-for-byte identically to writing nothing, in silence.
    [InlineData("override Beam.thickness = 9 c4 d e f")]
    [InlineData("override Stem.length = 12 c4 d e f")]
    [InlineData("override Stem.direction = -1 c4 d e f")]
    // A real grob with a property that does not exist anywhere.
    [InlineData("override Stem.wibble = 1 c4 d e f")]
    // No such grob.
    [InlineData("override Wibble.wobble = 5 c4 d e f")]
    // Deliberately refused while its implementation is disabled (2026-08-23, paired with
    // adding color — GRAMMAR_AUDIT §4.3): the reader sits behind
    // ElementCoordinator.ForceHshiftEnabled = false, so with a row in the vocabulary this
    // spelling was accepted and then silently ignored — the exact no-op LYS1029 exists to
    // prevent. The row (and this case's move back to the accepted list) returns with the
    // per-voice implementation.
    [InlineData("override NoteColumn.force-hshift = 0.5 c4 d e f")]
    // Case matters, as it does for part properties (SymbolCaseValidator). A mis-cased
    // grob name is the easiest way to write a supported property and get nothing.
    [InlineData("override stem.transparent = true c4 d e f")]
    [InlineData("override NoteHead.Transparent = true c4 d e f")]
    // revert and once reach the same check — they are the same vocabulary.
    [InlineData("revert Beam.thickness c4 d e f")]
    [InlineData("once override Beam.thickness = 9 c4 d e f")]
    public void UnsupportedProperties_OneError(string music) =>
        Assert.Single(Errors(music));

    [Fact]
    public void TheMessageNamesTheSpellingAndWhatIsSupported()
    {
        var d = Assert.Single(Errors("override Beam.thickness = 9 c4 d e f"));
        Assert.Contains("Beam.thickness", d.Message);
        // "not supported in this version" rather than "unknown": the property is a real
        // LilyPond one, and the day it lands the word "unknown" would have been a lie.
        Assert.Contains("not supported in this version", d.Message);
        Assert.DoesNotContain("Unknown", d.Message);
        // The supported set travels with the complaint, the way SymbolCaseValidator's does.
        foreach (var supported in SupportedGrobOverrides.Spellings)
            Assert.Contains(supported, d.Message);
    }

    [Fact]
    public void EveryOffendingLineIsReportedSeparately()
    {
        // Two unsupported and one supported: the supported one must not be swept up.
        var errors = Errors(
            "override Beam.thickness = 9 override NoteHead.transparent = true revert Stem.length c4 d e f");
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void TheSupportedListIsExactlyWhatTheEngineReads()
    {
        // A guard on the list itself, so it cannot grow by accident: adding a row here
        // without a reader would re-open the silence this validator closed.
        Assert.Equal(
            new[] { "NoteHead.color", "NoteHead.transparent", "Stem.color", "Stem.transparent" },
            SupportedGrobOverrides.Spellings.ToArray());
    }
}
