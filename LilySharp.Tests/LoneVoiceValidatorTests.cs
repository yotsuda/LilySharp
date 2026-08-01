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
/// A span holding ONE unnamed <c>voice { … }</c> is completely transparent — stem forcing
/// needs a second voice — so it engraves exactly as the same music without the braces, and
/// until this warning existed nothing told the writer who meant "polyphonic from here".
/// </summary>
public sealed class LoneVoiceValidatorTests
{
    private static IReadOnlyList<Diagnostic> Diagnose(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var validator = new LoneVoiceValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    private static bool Warns(string source) =>
        Diagnose(source).Any(d => d.Code == DiagnosticCodes.LoneVoiceBlock);

    [Fact]
    public void ASingleUnnamedVoice_Warns()
        => Assert.True(Warns("time 4/4\npart mel {\n  section A { voice { c4 d e f } | }\n}\n"));

    [Fact]
    public void TwoVoices_AreThePoint_AndDoNotWarn()
        => Assert.False(Warns("time 4/4\npart mel {\n  section A { voice { c4 d e f } voice { e4 f g a } | }\n}\n"));

    [Fact]
    public void ASingleNAMEDVoice_DoesNotWarn_ItPublishesATrackForLyrics()
        // `voice sop { … }` is not transparent: the name is what a `lyrics sop { … }`
        // block binds to (MeasureCollector's named-voice map), so the block earns its keep.
        => Assert.False(Warns("time 4/4\npart mel {\n  section A { voice sop { c4 d e f } | }\n}\n"));

    [Fact]
    public void TheWarningPointsAtTheVoiceKeyword_NotTheWholeBlock()
    {
        const string src = "time 4/4\npart mel {\n  section A { voice { c4 d e f } | }\n}\n";
        var d = Assert.Single(Diagnose(src));
        Assert.Equal("voice", src.Substring(d.Span.Start, d.Span.Length));
    }

    [Fact]
    public void TheRemovedDoubleAngleSyntax_IsLeftToItsOwnError()
    {
        // `<< … >>` already reports LYS0008; a second complaint on the same text is noise.
        var diags = Diagnose("time 4/4\npart mel {\n  section A { << { c4 d e f } >> | }\n}\n");
        Assert.Empty(diags);
    }
}
