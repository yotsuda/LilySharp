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
/// A top-level single-value global (tempo / time / key / title / composer / font) is
/// last-wins; writing it more than once warns on every earlier (overwritten) one.
/// </summary>
[Trait("Category", "Unit")]
public class DuplicateGlobalSettingValidatorTests
{
    private static int WarnCount(string src)
        => SemanticValidation.Run(SyntaxTree.Parse(src))
            .Count(d => d.Code == DiagnosticCodes.DuplicateGlobalSetting);

    private const string Tail = "part m { section A { c4 d e f | } }\nform main { A }\nscore main { staff m }";

    [Fact]
    public void RepeatedTopLevelTempo_WarnsOnEachButTheLast()
        => Assert.Equal(2, WarnCount("tempo 100\ntempo 120\ntempo 140\n" + Tail)); // 3 tempos → 2 overwritten

    [Fact]
    public void RepeatedTitle_Warns()
        => Assert.Equal(1, WarnCount("title \"A\"\ntitle \"B\"\n" + Tail));

    [Fact]
    public void RepeatedComposer_Warns()
        => Assert.Equal(1, WarnCount("composer \"A\"\ncomposer \"B\"\n" + Tail));

    [Fact]
    public void RepeatedTopLevelOctave_Warns()
        => Assert.Equal(1, WarnCount("octave absolute\noctave relative\n" + Tail));

    [Fact]
    public void SingleGlobalPlusMidSectionChange_DoesNotWarn()
        // One top-level tempo + a mid-section tempo change is a legitimate change, not a duplicate.
        => Assert.Equal(0, WarnCount("tempo 100\npart m { section A { c4 d | tempo 140 e f | } }\nform main { A }\nscore main { staff m }"));

    [Fact]
    public void DistinctGlobals_DoNotWarn()
        => Assert.Equal(0, WarnCount("tempo 100\ntime 4/4\nkey g major\ntitle \"X\"\ncomposer \"Y\"\n" + Tail));

    [Fact]
    public void GlobalKeyPlusPartHeaderKey_DoesNotWarn()
        // The part-header key is a per-part default, not a second global one: `melody2`
        // (no key of its own) still inherits the global `key c major`, so it is NOT
        // overwritten. (Regression: the part key used to group with the global.)
        => Assert.Equal(0, WarnCount(
            "key c major\npart melody { key bes major section A { c1 } }\n"
            + "part melody2 { section A { e1 } }\n"
            + "form main { A }\nscore main { staff melody staff melody2 }"));

    [Fact]
    public void TwoPartsEachWithOwnHeaderKey_DoNotWarn()
        => Assert.Equal(0, WarnCount(
            "part melody { key bes major section A { c1 } }\n"
            + "part melody2 { key d major section A { e1 } }\n"
            + "form main { A }\nscore main { staff melody staff melody2 }"));

    [Fact]
    public void TwoTopLevelKeys_StillWarn()
        // The real duplicate the validator exists for must still fire.
        => Assert.Equal(1, WarnCount("key c major\nkey d major\n" + Tail));

    [Fact]
    public void GlobalKeyPlusPartHeaderKey_IsCompletelyClean()
    {
        // End-to-end guard for the reported symptom: the file key + one part's own
        // header key is a fully VALID piece — no parse diagnostics, no semantic
        // errors, and no overwrite warning of any kind.
        var tree = SyntaxTree.Parse(
            "key c major\npart melody { key bes major section A { c1 } }\n"
            + "part melody2 { section A { e1 } }\n"
            + "form main { A }\nscore main { staff melody staff melody2 }");
        Assert.Empty(tree.Diagnostics);
        var semantic = SemanticValidation.Run(tree);
        Assert.DoesNotContain(semantic, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(semantic, d => d.Code == DiagnosticCodes.DuplicateGlobalSetting);
    }
}
