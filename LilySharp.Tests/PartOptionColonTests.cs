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
using Xunit;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// A part option inside a section part block (<c>section X { melody transpose d { … } }</c>)
/// is written bare, matching every sibling attribute (time/tempo/clef, and the
/// top-level part-property form). ParsePartOption used to demand a colon
/// (<c>Expect(Colon)</c>), raising a spurious "Expected ':'" on the canonical
/// bare form; it now uses ConsumeRejectedColon like the others.
/// </summary>
[Trait("Category", "Unit")]
public class PartOptionColonTests
{
    [Fact]
    public void SectionPartOption_BareForm_ParsesWithoutColonError()
    {
        var tree = SyntaxTree.Parse("section Main { melody transpose d { c4 e } }\n");

        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.ExpectedToken);
        // The bare option is captured as a property assignment (transpose d).
        Assert.NotEmpty(tree.GetRoot().DescendantNodes<PropertyAssignmentSyntax>());
    }

    [Fact]
    public void SectionPartOption_ColonForm_FlaggedLegacyNotRequired()
    {
        var tree = SyntaxTree.Parse("section Main { melody transpose: d { c4 e } }\n");

        // The ':' form still parses, now flagged as rejected-legacy (like the
        // sibling attributes), NOT demanded via an "Expected ':'".
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.LegacyDeclarationForm);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.ExpectedToken);
    }
}
