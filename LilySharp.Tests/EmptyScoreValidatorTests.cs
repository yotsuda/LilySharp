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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>score</c> with no render item engraves a page with no music. That used to pass
/// validation silently, so the only signal was a blank preview — indistinguishable from
/// a layout failure. LYS6002 names it.
/// </summary>
[Trait("Category", "Unit")]
public class EmptyScoreValidatorTests
{
    private const string Preamble =
        "part melody { clef treble }\nsection A { melody { c4 d e f | } }\nform main { A }\n";

    private static Diagnostic[] Validate(string src)
        => SemanticValidation.Run(SyntaxTree.Parse(src))
            .Where(d => d.Code == DiagnosticCodes.EmptyScore).ToArray();

    [Fact]
    public void EmptyScoreBody_IsAnError()
    {
        var diags = Validate(Preamble + "score main {\n}\n");
        var d = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("main", d.Message);
        Assert.Contains("staff melody", d.Message);   // points at the fix
    }

    /// <summary>
    /// The squiggle must sit on the score's OWN braces. Anchoring it to the `score`
    /// keyword put the red line next to whatever declaration preceded it — reported
    /// from the editor as "the line is inside the form's braces".
    /// </summary>
    [Fact]
    public void TheErrorMarksTheScoresOwnBraces()
    {
        const string src = "part m { clef treble }\nsection A { m { c4 } }\n"
            + "form main { A }\nscore main {\n}\n";
        var d = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(src))
            .Where(x => x.Code == DiagnosticCodes.EmptyScore));

        Assert.Equal(src.LastIndexOf('{'), d.Span.Start);
        Assert.Equal(src.LastIndexOf('}') + 1, d.Span.End);
        // …and therefore never overlaps the form declaration above it.
        Assert.True(d.Span.Start > src.IndexOf("form main", System.StringComparison.Ordinal)
            + "form main { A }".Length);
    }

    [Fact]
    public void ScoreWithAStaff_IsClean()
        => Assert.Empty(Validate(Preamble + "score main { staff melody }\n"));

    [Fact]
    public void ScoreWithAGrandStaff_IsClean()
        => Assert.Empty(Validate(
            "part rh { clef treble }\npart lh { clef bass }\n"
            + "section A { rh { c4 d e f | } lh { c4 d e f | } }\nform main { A }\n"
            + "score main { grandStaff { staff rh staff lh } }\n"));

    /// <summary>A score body carrying ONLY the per-score transpose is still empty —
    /// the property is not a render item.</summary>
    [Fact]
    public void ScoreWithOnlyATranspose_IsAnError()
        => Assert.Single(Validate(Preamble + "score main transpose bes {\n}\n"));

    /// <summary>Each empty score is reported on its own, so a file with two of them
    /// does not hide one behind the other.</summary>
    [Fact]
    public void EveryEmptyScoreIsReported()
        => Assert.Equal(2, Validate(
            Preamble + "score main {\n}\nscore other \"o\" {\n}\nform other { A }\n").Length);

    /// <summary>The staff names an undefined part: that is a DIFFERENT defect, already
    /// reported elsewhere. The score is not empty, so this validator stays quiet — it
    /// must not pile a second error onto the same line.</summary>
    [Fact]
    public void ScoreWhoseStaffNamesAnUnknownPart_IsNotCalledEmpty()
        => Assert.Empty(Validate(Preamble + "score main { staff nosuchpart }\n"));
}
