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
/// A chords/lyrics track may name each section only once. A second `section B { … }`
/// (the old "stack a repeat's verses" idiom) is rejected in favour of numbered
/// verses `[1. …] [2. …]` inside the one section.
/// </summary>
[Trait("Category", "Unit")]
public class DuplicateTrackSectionValidatorTests
{
    private static bool HasDuplicate(string source)
    {
        var validator = new DuplicateTrackSectionValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics.Any(d => d.Code == DiagnosticCodes.DuplicateTrackSection);
    }

    private const string Parts = """
        time 4/4
        key c major
        part melody { clef treble
          section A { c'4 d' e' f' | }
          section B { g'4 a' b' c'' | }
        }
        """;

    [Fact]
    public void LyricsTrack_RepeatingASectionName_Errors()
    {
        Assert.True(HasDuplicate(Parts + """
            lyrics {
              section A { la la la la | }
              section B { up up up up | }
              section B { down down down down | }
            }
            form main { A |: B :| }
            score main { staff melody }
            """));
    }

    [Fact]
    public void LyricsTrack_NumberedVersesInOneSection_IsClean()
    {
        Assert.False(HasDuplicate(Parts + """
            lyrics {
              section A { la la la la | }
              section B { [1. up up up up |] [2. down down down down |] }
            }
            form main { A |: B :| }
            score main { staff melody }
            """));
    }

    [Fact]
    public void ChordsTrack_RepeatingASectionName_Errors()
    {
        Assert.True(HasDuplicate(Parts + """
            chords harmony { section A { c1 | } section A { g1 | } }
            form main { A }
            score main { staff melody  chords harmony }
            """));
    }

    [Fact]
    public void DistinctSectionNames_AreClean()
    {
        Assert.False(HasDuplicate(Parts + """
            lyrics { section A { la la la la | } section B { up up up up | } }
            form main { A B }
            score main { staff melody }
            """));
    }
}
