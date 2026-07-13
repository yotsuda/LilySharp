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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// In a part-major file (parts nest their own sections) a top-level lyrics TRACK must
/// mirror that shape — <c>lyrics { section A { … } }</c>. A flat top-level track is
/// rejected; a sectioned track, an inline note-bound block, and any section-major or
/// structureless file are left alone.
/// </summary>
[Trait("Category", "Unit")]
public class LyricTrackSectionValidatorTests
{
    private static bool NeedsSections(string source)
    {
        var validator = new LyricTrackSectionValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics.Any(d => d.Code == DiagnosticCodes.LyricTrackNeedsSections);
    }

    private const string PartMajorParts = """
        time 4/4
        key c major
        part melody { clef treble
          section A { c'4 d' e' f' | }
          section B { g'4 a' b' c'' | }
        }
        """;

    [Fact]
    public void PartMajor_FlatTopLevelTrack_Errors()
    {
        Assert.True(NeedsSections(PartMajorParts + """
            lyrics words { Do re mi fa | sol la ti do | }
            form main { A B }
            score main { staff melody  lyrics words }
            """));
    }

    [Fact]
    public void PartMajor_SectionedTrack_IsClean()
    {
        Assert.False(NeedsSections(PartMajorParts + """
            lyrics words { section A { Do re mi fa | } section B { sol la ti do | } }
            form main { A B }
            score main { staff melody  lyrics words }
            """));
    }

    [Fact]
    public void SectionMajor_InlineFlatLyrics_IsClean()
    {
        // Section-major (top-level `section` holds the parts): a flat lyrics block in a
        // section is the norm, not a top-level track — never flagged.
        Assert.False(NeedsSections("""
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c'4 d' e' f' | } lyrics { Do re mi fa | } }
            form main { A }
            score main { staff melody }
            """));
    }

    [Fact]
    public void Structureless_FlatLyrics_IsClean()
    {
        // No parts-with-sections: layout is not part-major, so flat lyrics are fine.
        Assert.False(NeedsSections("time 4/4\n{ c4 d e f }\nlyrics { one two three four }\n"));
    }
}
