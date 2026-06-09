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
/// MeasureValidator must scale tuplet durations by their ratio; otherwise a bar
/// containing a triplet looks short and emits a false "measure incomplete"
/// diagnostic (the LSP surfaces these to the editor).
/// </summary>
public sealed class MeasureValidatorTupletTests
{
    private static bool HasIncomplete(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var validator = new MeasureValidator();
        validator.Validate(tree);
        return validator.Diagnostics.Any(d => d.Code == DiagnosticCodes.MeasureIncomplete);
    }

    [Fact]
    public void TripletFillingTheBar_DoesNotWarnIncomplete()
    {
        // 2/4 = quarter + eighth-triplet (3 × 1/8 × 2/3 = 1/4). Exactly full.
        // Pre-fix the triplet counted as 0, so this warned as incomplete.
        Assert.False(HasIncomplete("time 2/4\n{ c4 tuplet 3/2 { c8 c c } }"));
    }

    [Fact]
    public void NestedTriplet_FillingTheBar_DoesNotWarnIncomplete()
    {
        // 1/4 bar filled by a triplet whose middle member is itself a triplet.
        Assert.False(HasIncomplete("time 1/4\n{ tuplet 3/2 { c8 tuplet 3/2 { c16 c c } c8 } }"));
    }

    [Fact]
    public void GenuinelyShortBar_StillWarns()
    {
        // Detection must still fire for a real short measure.
        Assert.True(HasIncomplete("time 2/4\n{ c4 }"));
    }
}
