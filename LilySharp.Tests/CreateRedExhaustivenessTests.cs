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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// Guards the class of bug that <c>SyntaxNode.CreateRed</c> can silently hit: a
/// green kind that has a dedicated <c>*Syntax</c> red class but no matching
/// <c>case</c> in the switch falls through to <see cref="GenericSyntaxNode"/>,
/// degrading navigation/rename with no error. (The original defect: MIDI part
/// render — <c>partName instrument:N</c> — was unreachable this way.)
/// </summary>
[Trait("Category", "Unit")]
public class CreateRedExhaustivenessTests
{
    // Direct regression for the original defect: a `partName instrument:N` render
    // item must materialize as MidiPartRenderSyntax, not GenericSyntaxNode.
    [Fact]
    public void MidiPartRender_CreatesDedicatedRedNode()
    {
        var tree = SyntaxTree.Parse(
            "part melody { clef treble }\n" +
            "section Main { melody { c4 d e f | } }\n" +
            "form main { Main }\n" +
            "score main \"audio\" { melody instrument:1 }\n");

        var renders = tree.GetRoot().DescendantNodes<MidiPartRenderSyntax>().ToList();
        Assert.Single(renders);
        Assert.Equal(SyntaxKind.MidiPartRender, renders[0].Kind);
    }

    // Comprehensive structural guard: every dedicated red type XxxSyntax must
    // have a matching SyntaxKind.Xxx and an XxxGreen. An orphaned/misnamed pair
    // is a precursor to a missing CreateRed case.
    [Fact]
    public void EveryDedicatedRedType_HasMatchingKindAndGreen()
    {
        var asm = typeof(SyntaxNode).Assembly;
        var greenNames = asm.GetTypes().Select(t => t.Name).ToHashSet();
        var redTypes = DedicatedRedTypes(asm);

        Assert.NotEmpty(redTypes);
        foreach (var red in redTypes)
        {
            var kindName = red.Name[..^"Syntax".Length];
            Assert.True(Enum.TryParse<SyntaxKind>(kindName, out _),
                $"{red.Name} has no matching SyntaxKind.{kindName}");
            Assert.True(greenNames.Contains(kindName + "Green"),
                $"{red.Name} has no matching {kindName}Green");
        }
    }

    // Parse a broad document and verify no node that HAS a dedicated red type
    // fell through CreateRed to GenericSyntaxNode — the exact B1 failure mode —
    // for every kind the document exercises (incl. the MIDI part render).
    [Fact]
    public void ParsedTree_NeverMasksADedicatedRedTypeAsGeneric()
    {
        var asm = typeof(SyntaxNode).Assembly;
        var dedicatedKindNames = DedicatedRedTypes(asm)
            .Select(t => t.Name[..^"Syntax".Length]).ToHashSet();

        var source =
            "title \"Song\"\ncomposer \"Trad\"\ntempo 120\ntime 3/4\nkey g major\n" +
            "part melody { clef treble }\n" +
            "section Main { melody { c4 d( e) f ~ f | <c e g>2 g4 | tuplet 3/2 { a8 b c } grace { d16 } e4 | } " +
            "lyrics { la la la la | la la | } }\n" +
            "form main { |: Main :| }\n" +
            "score main \"out\" { staff melody }\n" +
            "score main \"audio\" { melody instrument:1 }\n";

        var tree = SyntaxTree.Parse(source);
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is GenericSyntaxNode)
                Assert.False(dedicatedKindNames.Contains(node.Kind.ToString()),
                    $"Kind {node.Kind} has a dedicated {node.Kind}Syntax red type but " +
                    $"CreateRed fell through to GenericSyntaxNode — add the missing case.");
        }
    }

    private static List<Type> DedicatedRedTypes(Assembly asm) =>
        asm.GetTypes().Where(t =>
            t.IsClass && !t.IsAbstract && t.IsSealed &&
            typeof(SyntaxNode).IsAssignableFrom(t) &&
            t.Name.EndsWith("Syntax", StringComparison.Ordinal)).ToList();
}
