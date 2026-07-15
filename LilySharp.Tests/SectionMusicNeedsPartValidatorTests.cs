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
/// In a PART-MAJOR file (parts carry their own sections), a top-level <c>section</c> is a
/// section header: it holds directives and the parts' cells, never loose music. Bare notes
/// there belong to no part and are flagged. A single-part file that writes its lone part's
/// setup and music apart (<c>part bl { clef bass } section A { c d e }</c>) is NOT part-major —
/// its section music binds to the one part and both renders and validates cleanly.
/// </summary>
[Trait("Category", "Unit")]
public class SectionMusicNeedsPartValidatorTests
{
    private static int ErrCount(string src)
        => SemanticValidation.Run(SyntaxTree.Parse(src))
            .Count(d => d.Code == DiagnosticCodes.SectionMusicNeedsPart);

    [Fact]
    public void PartMajorLooseSectionMusic_Errors()
        // melody carries section A (part-major); the stray top-level `section A { g4 … }`
        // is loose music that belongs to no part.
        => Assert.Equal(1, ErrCount(
            "part melody { section A { c4 d e f } }\nsection A { g4 a b c }\n"
            + "form main { A }\nscore main { staff melody }"));

    [Fact]
    public void SinglePartSectionMusic_Ok()
        // The bend/dead-note shape: one part's setup, its music in a top-level section.
        // Not part-major (the part has no inner section) → the loose music is that part's.
        => Assert.Equal(0, ErrCount(
            "part bl { clef bass }\nsection A { c4 d e f }\n"
            + "form main { A }\nscore main { staff bl }"));

    [Fact]
    public void DirectiveOnlyTopLevelSection_Ok()
        // A standalone part-major header (`section A { partial 4 }`) carries only a directive,
        // no music — allowed.
        => Assert.Equal(0, ErrCount(
            "section A { partial 4 }\npart melody { section A { g4 | c1 } }\n"
            + "part bass { section A { g4 | c1 } }\nform main { A }\n"
            + "score main { staff melody staff bass }"));

    [Fact]
    public void SectionMajorSection_Ok()
        // Section-major: a top-level section legitimately holds the parts' cells.
        => Assert.Equal(0, ErrCount(
            "part melody { clef treble }\npart bass { clef bass }\n"
            + "section A { melody { c4 d e f } bass { c2 g } }\nform main { A }\n"
            + "score main { grandStaff { staff melody staff bass } }"));

    [Fact]
    public void SinglePartLooseSectionMusic_Renders()
    {
        // Regression: loose section music in a single-part file used to be dropped (or
        // mis-parsed) and render nothing. It must now reach the lone part's staff as notes.
        var tree = SyntaxTree.Parse(
            "part bl { clef bass }\nsection A { c4 d e f }\n"
            + "form main { A }\nscore main { staff bl }");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);
        var voice = score.StaffGroups.SelectMany(g => g.Staves).First().Voices[0];
        Assert.Equal(4, voice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().Count());
    }
}
