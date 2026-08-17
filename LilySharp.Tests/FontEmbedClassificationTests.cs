// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Licensed under the GNU General Public License v3 or later.

using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class FontEmbedClassificationTests
{
    // The pure fsType-and-name decision, exercised without touching any installed font.
    [Theory]
    [InlineData(0x0002, "Noto Serif CJK JP", FontEmbedInfo.FontEmbedClass.Forbidden)] // restricted bit wins
    [InlineData(0x0000, "Noto Serif CJK JP", FontEmbedInfo.FontEmbedClass.Free)]      // libre, fsType clear
    [InlineData(0x0008, "meiryo", FontEmbedInfo.FontEmbedClass.Gray)]                 // editable but unknown license
    [InlineData(0x0004, "Liberation Serif", FontEmbedInfo.FontEmbedClass.Free)]       // preview bit only, libre
    [InlineData(0x0000, "Yu Mincho", FontEmbedInfo.FontEmbedClass.Gray)]              // clear fsType, unknown license
    public void ClassifyFsTypeAndName_DecidesByRestrictedBitThenFamily(
        ushort fsType, string family, FontEmbedInfo.FontEmbedClass expected)
    {
        Assert.Equal(expected, FontEmbedInfo.ClassifyFsTypeAndName(fsType, family));
    }

    [Theory]
    [InlineData("Noto Serif CJK JP", true)]
    [InlineData("IPAexMincho", true)]
    [InlineData("BIZ UDMincho", true)]
    [InlineData("meiryo", false)]
    [InlineData("Yu Mincho", false)]
    [InlineData("MS Mincho", false)]
    public void IsKnownLibreFamily_MatchesRedistributableMarkers(string family, bool expected)
    {
        Assert.Equal(expected, FontEmbedInfo.IsKnownLibreFamily(family));
    }

    // A name that is (essentially) certain not to resolve to a real installed face, so
    // this stays environment-independent: Skia hands back a default face whose family
    // name will not match, which the classifier reports as NotFound.
    private const string BogusFont = "ZzUnlikelyFont98765";

    private static string Doc(string fontLine) => $$"""
        {{fontLine}}
        time 4/4
        part m { clef treble section A { c4 d e f | } }
        form main { A }
        score main { staff m }
        """;

    [Fact]
    public void EmbeddedMissingFont_WarnsNotFound()
    {
        var v = new FontEmbedWarningValidator();
        v.Validate(SyntaxTree.Parse(Doc($"font \"{BogusFont}\" embedded")));
        Assert.Contains(v.Diagnostics, d => d.Code == DiagnosticCodes.FontNotFound);
    }

    [Fact]
    public void NonEmbeddedMissingFont_WarnsNotFoundToo()
    {
        // ⚠️ THIS CASE ASSERTED THE OPPOSITE until 2026-08-18, and the rule it froze was
        // the defect: the same fact, found by the same code, was reported through the
        // `embedded` spelling and accepted in silence through the plain one. A misspelt
        // face is not less wrong for being un-embedded — it just fails later, on the
        // page, in a substitute face. (Decided by the user; HANDOFF §2F.)
        var v = new FontEmbedWarningValidator();
        v.Validate(SyntaxTree.Parse(Doc($"font \"{BogusFont}\"")));
        Assert.Contains(v.Diagnostics, d => d.Code == DiagnosticCodes.FontNotFound);
    }

    [Fact]
    public void NonEmbeddedFont_IsStillNotLicenceChecked()
    {
        // The LICENCE half of this validator is still gated on `embedded`, and stays so:
        // nothing is embedded, so no licence can be breached. Only the "is this face here
        // at all" question outlives the flag.
        var v = new FontEmbedWarningValidator();
        v.Validate(SyntaxTree.Parse(Doc($"font \"{BogusFont}\"")));
        Assert.DoesNotContain(v.Diagnostics, d =>
            d.Code == DiagnosticCodes.FontEmbedForbidden ||
            d.Code == DiagnosticCodes.FontEmbedLicenseUnclear);
    }
}
