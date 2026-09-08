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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>partial</c> (pickup) says "the bar it stands in is this long", so it is legal wherever
/// a bar is: a section directive, a section body, and — owner's decision 2026-09-08 — a part's
/// or voice's music mid-piece, written per part like a mid-music <c>time</c>. The top level of
/// a structured file and a part header hold no bar and stay errors. Bare music (no sections)
/// is exempt: a leading partial is just that note stream's pickup.
/// </summary>
[Trait("Category", "Unit")]
public class PartialScopeValidatorTests
{
    private static int ErrCount(string src)
        => SemanticValidation.Run(SyntaxTree.Parse(src))
            .Count(d => d.Code == DiagnosticCodes.PartialOutsideSection);

    // --- Allowed: a section directive (parent is the section node) ---

    [Fact]
    public void SectionMajorHeaderPartial_Ok()
        => Assert.Equal(0, ErrCount(
            "time 4/4\nsection A { partial 4  melody { g4 | c' d' e' f' | } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void StandalonePartMajorHeaderPartial_Ok()
        => Assert.Equal(0, ErrCount(
            "part melody { section A { g4 | c' d' e' f' | } }\nsection A { partial 4 }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void SingleVoiceSectionBodyPartial_Ok()
        => Assert.Equal(0, ErrCount(
            "time 4/4\nsection A { partial 4  g4 | c' d' e' f' | }\nform main { A }\nscore main { staff melody }"));

    // --- Allowed since 2026-09-08: in the music, at the head or mid-piece, per part ---

    [Fact]
    public void PartialInPartMajorCell_IsThatPartsPickup_Ok()
        // One part's cell, one part's pickup — the per-part rule; a second part that shares
        // the bar writes its own (or CrossPartMeasureValidator says the bars disagree).
        => Assert.Equal(0, ErrCount(
            "part melody { section A { partial 4  g4 | c' d' e' f' | } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void PartialInsideAPartBlock_Ok()
        => Assert.Equal(0, ErrCount(
            "section A { melody { partial 4  g4 | c' d' e' f' | } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void MidPiecePartial_ShortensTheBarItStandsIn_OnThePage()
    {
        // The census shape of the tab corpus: `| partial 2. r2. |` closes a three-beat bar
        // mid-piece, and the meter resumes after it. No diagnostic, and the page has the bar.
        const string src = "time 4/4\nsection A { melody { c'4 d' e' f' | partial 2. r2. | g'4 a' b' c'' | } }\n"
            + "form main { A }\nscore main { staff melody }";
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(src)).Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(src)),
            d => d.Code == DiagnosticCodes.MeasureIncomplete);
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src), "melody");
        Assert.Equal(3, score.Voice.Measures.Length);
        Assert.Equal(new Fraction(3, 4), score.Voice.Measures[1].TotalDuration);
        Assert.Equal(new Fraction(4, 4), score.Voice.Measures[2].TotalDuration);
    }

    [Fact]
    public void MidPiecePartial_WrittenInOnePartOnly_IsACrossPartMismatch()
        // Lily# has no shared Timing (LilyPond's \partial moves every staff's clock through
        // Score.Timing); a part that omits the pickup keeps its full bar, and the two parts'
        // bars disagree — which the cross-part check reports rather than the scope check.
        => Assert.Contains(SemanticValidation.Run(SyntaxTree.Parse(
                "time 4/4\nsection A { melody { c'4 d' e' f' | partial 4 g'4 | c'4 d' e' f' | }\n"
                + "  bass { c4 d e f | c4 d e f | c4 d e f | } }\nform main { A }\nscore main { staff melody staff bass }")),
            d => d.Code == DiagnosticCodes.MeasureDurationMismatch);

    // --- Rejected: no bar there ---

    [Fact]
    public void TopLevelPartial_InStructuredFile_Errors()
        => Assert.Equal(1, ErrCount(
            "partial 4\nsection A { melody { c4 d e f | } }\nform main { A }\nscore main { staff melody }"));

    // --- Exempt: bare music has no sections ---

    [Fact]
    public void BareMusicPartial_Ok()
        => Assert.Equal(0, ErrCount("time 4/4 partial 4 g4 | c4 d e f |"));
}
