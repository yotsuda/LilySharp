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

    [Theory]
    // sFamilyClass, the authority: its HIGH byte is the class id. 1-5 and 7 are the serif
    // families (oldstyle, transitional, modern, Clarendon, slab, freeform); 6 is reserved.
    [InlineData(1, 0, FontEmbedInfo.FaceShape.Serif)]
    [InlineData(2, 0, FontEmbedInfo.FaceShape.Serif)]
    [InlineData(5, 0, FontEmbedInfo.FaceShape.Serif)]
    [InlineData(7, 0, FontEmbedInfo.FaceShape.Serif)]
    [InlineData(8, 0, FontEmbedInfo.FaceShape.Sans)]
    // Ornamental / script / symbolic: neither shape, and not prose.
    [InlineData(9, 0, FontEmbedInfo.FaceShape.Decorative)]
    [InlineData(10, 0, FontEmbedInfo.FaceShape.Decorative)]
    [InlineData(12, 0, FontEmbedInfo.FaceShape.Decorative)]
    // Silent sFamilyClass: PANOSE bSerifStyle answers instead — 2-10 serif, 11-13 sans.
    [InlineData(0, 3, FontEmbedInfo.FaceShape.Serif)]
    [InlineData(0, 10, FontEmbedInfo.FaceShape.Serif)]
    [InlineData(0, 11, FontEmbedInfo.FaceShape.Sans)]
    [InlineData(0, 13, FontEmbedInfo.FaceShape.Sans)]
    // Both silent — a real answer, not an error. Measured 2026-08-18: 16 of this machine's
    // 232 families land here, among them SimSun (a CJK serif) and all of Sitka. That is why
    // the completion keeps them in a tail instead of hiding them.
    [InlineData(0, 0, FontEmbedInfo.FaceShape.Unknown)]
    [InlineData(0, 1, FontEmbedInfo.FaceShape.Unknown)]
    [InlineData(6, 0, FontEmbedInfo.FaceShape.Unknown)]
    // The authority wins where PANOSE disagrees: sFamilyClass's meaning is fixed by the
    // spec, PANOSE's ten bytes are a vendor's summary.
    [InlineData(8, 3, FontEmbedInfo.FaceShape.Sans)]
    [InlineData(2, 11, FontEmbedInfo.FaceShape.Serif)]
    public void ShapeIsReadFromTheFontsOwnClassification(
        byte familyClass, byte panoseSerifStyle, FontEmbedInfo.FaceShape expected)
        => Assert.Equal(expected, FontEmbedInfo.ShapeFromOs2(familyClass, panoseSerifStyle));

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
        v.Validate(SyntaxTree.Parse(Doc($"fonts {{ serif \"{BogusFont}\"  embedded }}")));
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
        v.Validate(SyntaxTree.Parse(Doc($"fonts {{ serif \"{BogusFont}\" }}")));
        Assert.Contains(v.Diagnostics, d => d.Code == DiagnosticCodes.FontNotFound);
    }

    [Fact]
    public void NonEmbeddedFont_IsStillNotLicenceChecked()
    {
        // The LICENCE half of this validator is still gated on `embedded`, and stays so:
        // nothing is embedded, so no licence can be breached. Only the "is this face here
        // at all" question outlives the flag.
        var v = new FontEmbedWarningValidator();
        v.Validate(SyntaxTree.Parse(Doc($"fonts {{ serif \"{BogusFont}\" }}")));
        Assert.DoesNotContain(v.Diagnostics, d =>
            d.Code == DiagnosticCodes.FontEmbedForbidden ||
            d.Code == DiagnosticCodes.FontEmbedLicenseUnclear);
    }
}
