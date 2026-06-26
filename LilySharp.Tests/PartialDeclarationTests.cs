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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// 'partial N' declares an anacrusis (pickup): the current measure auto-completes
/// after 1/N of music, the meter resumes afterwards, and the pickup is checked
/// strictly against its declared length. LILYPOND-REF:
/// ly/music-functions-init.ly:1670-1678 \partial.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PartialDeclarationTests
{
    // ---- Parsing -------------------------------------------------------------

    [Fact]
    public void ParsesPartialAsQuarter()
    {
        var tree = SyntaxTree.Parse("{ partial 4 c4 d e f }");
        var partial = tree.GetRoot().DescendantNodes().OfType<PartialDeclarationSyntax>().Single();
        Assert.Equal(new Fraction(1, 4), partial.ToFraction());
    }

    [Fact]
    public void ParsesDottedPartial()
    {
        // 'partial 2.' = dotted half = 3/4.
        var tree = SyntaxTree.Parse("{ partial 2. c2 d4 }");
        var partial = tree.GetRoot().DescendantNodes().OfType<PartialDeclarationSyntax>().Single();
        Assert.Equal(new Fraction(3, 4), partial.ToFraction());
    }

    [Fact]
    public void PartialDoesNotProduceParseErrors()
    {
        var tree = SyntaxTree.Parse("{ time 4/4 partial 8 g8 c'4 d e f }");
        Assert.Empty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // ---- Collector (auto-close) ----------------------------------------------

    private static System.Collections.Immutable.ImmutableArray<Measure> CollectMeasures(string body)
    {
        var src = "section Main {\n  m {\n" + body + "\n  }\n}\nstructure { Main }\nscore \"x\" { staff { m } }\n";
        var tree = SyntaxTree.Parse(src);
        return new MeasureCollector().Collect(tree).Voice.Measures;
    }

    private static int NoteEvents(Measure m) =>
        m.Items.Count(it => it is NoteItem or RestItem or ChordItem);

    [Fact]
    public void PickupAutoClosesWithoutWrittenBarline()
    {
        // No '|' after the pickup g4: it must close on its own at 1/4, leaving
        // a one-note measure 0 and a full four-note measure 1.
        var measures = CollectMeasures("time 4/4 partial 4 g4 c'4 d e f | g2 c'2 |");
        Assert.Equal(3, measures.Length);
        Assert.Equal(1, NoteEvents(measures[0])); // pickup
        Assert.Equal(4, NoteEvents(measures[1])); // first full bar
        Assert.Equal(2, NoteEvents(measures[2]));
    }

    [Fact]
    public void MeterResumesAfterPickup()
    {
        // After the pickup closes, the 4/4 meter resumes: 8 eighths fill one bar.
        var measures = CollectMeasures("time 4/4 partial 8 g8 c'8 d e f g a b c' |");
        Assert.Equal(2, measures.Length);
        Assert.Equal(1, NoteEvents(measures[0])); // eighth-note pickup
        Assert.Equal(8, NoteEvents(measures[1])); // full 4/4 bar of eighths
    }

    [Fact]
    public void PickupMeasure_IsFlaggedIsPickup()
    {
        // The pickup is marked so bar numbering can treat it as bar 0; the full
        // measures that follow are NOT pickups.
        var measures = CollectMeasures("time 4/4 partial 4 g4 | c4 d e f | g1 |");
        Assert.True(measures[0].IsPickup);
        Assert.False(measures[1].IsPickup);
        Assert.False(measures[2].IsPickup);
    }

    [Fact]
    public void WithoutPartial_NoMeasureIsPickup()
    {
        var measures = CollectMeasures("time 4/4 c4 d e f | g1 |");
        Assert.All(measures, m => Assert.False(m.IsPickup));
    }

    // ---- Validator (strict pickup) -------------------------------------------

    private static bool Warns(string source, string code)
    {
        var tree = SyntaxTree.Parse(source);
        var validator = new MeasureValidator();
        validator.Validate(tree);
        return validator.Diagnostics.Any(d => d.Code == code);
    }

    [Fact]
    public void ExactPickup_DoesNotWarn()
    {
        Assert.False(Warns("{ time 4/4 partial 4 g4 | c4 d e f | }", DiagnosticCodes.MeasureIncomplete));
        Assert.False(Warns("{ time 4/4 partial 4 g4 | c4 d e f | }", DiagnosticCodes.MeasureOverflow));
    }

    [Fact]
    public void OverfullPickup_WarnsOverflow()
    {
        // Two quarters where only one was declared.
        Assert.True(Warns("{ time 4/4 partial 4 g4 a4 | c4 d e f | }", DiagnosticCodes.MeasureOverflow));
    }

    [Fact]
    public void UnderfullPickup_WarnsIncomplete()
    {
        // 'partial 2' declares a half-note pickup, but only an eighth is written.
        // Unlike a bare anacrusis, a declared partial is strictly checked.
        Assert.True(Warns("{ time 4/4 partial 2 g8 | c4 d e f | }", DiagnosticCodes.MeasureIncomplete));
    }

    [Fact]
    public void BarePickup_WithoutPartial_StillExempt()
    {
        // Regression: a short FIRST measure WITHOUT 'partial' keeps its
        // benefit-of-the-doubt anacrusis exemption (behavior unchanged).
        Assert.False(Warns("{ time 4/4 g4 | c4 d e f | }", DiagnosticCodes.MeasureIncomplete));
    }
}
