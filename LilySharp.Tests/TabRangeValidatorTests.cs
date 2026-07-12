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
/// A note below the tab's lowest string is omitted from the tab (it can't be
/// fretted); the validator surfaces that as a warning (it would otherwise hide an
/// octave slip).
/// </summary>
[Trait("Category", "Unit")]
public sealed class TabRangeValidatorTests
{
    private static System.Collections.Generic.IReadOnlyList<Diagnostic> Run(string body)
    {
        var v = new TabRangeValidator();
        v.Validate(SyntaxTree.Parse(
            "part bl { clef bass tuning bass }\nsection A { bl { " + body + " } }\nform main { A }\nscore { tab bl }\n"));
        return v.Diagnostics;
    }

    [Fact]
    public void BelowLowestString_Warns()
    {
        // a, sounds A0 — below the bass's lowest open string (E1).
        var d = Run("a,4\\4 r2. |");
        Assert.Contains(d, x => x.Code == DiagnosticCodes.TabOutOfRange);
    }

    [Fact]
    public void InRange_NoWarning()
    {
        // a sounds A1 — open A string, playable.
        Assert.DoesNotContain(Run("a4\\4 r2. |"), x => x.Code == DiagnosticCodes.TabOutOfRange);
    }

    [Fact]
    public void NonTabScore_NoWarning()
    {
        // The check only applies to tab renders; a staff score never warns.
        var v = new TabRangeValidator();
        v.Validate(SyntaxTree.Parse(
            "part bl { clef bass }\nsection A { bl { a,,4 r2. | } }\nform main { A }\nscore { staff bl }\n"));
        Assert.Empty(v.Diagnostics);
    }
}
