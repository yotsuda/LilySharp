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
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A hand-written slur from a plain grace to its main note — <c>grace { g16( } a8)</c> —
/// draws the bow an <c>appoggiatura</c> draws on its own, and nothing else about the grace
/// changes (session 328, owner decision: LilyPond prints it, so does Lily#).
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/grace-init.ly startGraceSlur / stopGraceSlur — an appoggiatura IS a grace
/// with a slur event on its last note and the end on the main note, so the two spellings are
/// one picture in LilyPond. The pair is asserted as PAGE EQUALITY against the keyword rather
/// than as "a path exists": the keyword's bow is the one geometry Lily# has for a grace slur
/// (SharedRenderer.DrawGraceSlur), and a second one would be the second spelling RULES §5.2.1②
/// names.
/// <para>
/// ⚠️ THE SHAPES THAT ARE NOT THIS BOW STAY REPORTED. A <c>(</c> on an earlier grace column,
/// or on a grace rest, is not the appoggiatura's slur, and the island that draws those —
/// grace marks through the ordinary Slur engraver at the grace font, HANDOFF §2 U8 ⒝2 — is
/// not this change. Each of those is asserted to warn AND to leave the page as the control
/// leaves it, the way GraceBodyValidatorTests ties every drop to the ink.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class GraceExplicitSlurTests
{
    private static string Book(string music)
        => "time 4/4\npart m { clef treble }\nsection A { m {\n" + music + "\n} }\n"
           + "form main { ~A }\nscore main { staff m }\n";

    /// <summary>The page with every source offset masked, so two books that write the same
    /// music at different lengths differ in nothing.</summary>
    private static string Page(string music)
        => Regex.Replace(LiveRender.Svg(Book(music)), "data-pos=\"\\d+\"", "data-pos=\"#\"");

    private static IReadOnlyList<Diagnostic> GraceDrops(string music)
    {
        var validator = new GraceBodyValidator();
        validator.Validate(SyntaxTree.Parse(Book(music)));
        return validator.Diagnostics.Where(d => d.Code == DiagnosticCodes.UnengravedGraceContent).ToList();
    }

    private static IReadOnlyList<Diagnostic> UnpairedSlurs(string music)
    {
        var validator = new SlurPairingValidator();
        validator.Validate(SyntaxTree.Parse(Book(music)));
        return validator.Diagnostics.Where(d => d.Code == DiagnosticCodes.UnpairedSlur).ToList();
    }

    [Fact]
    public void TheHandWrittenPair_IsTheAppoggiaturasBow_AndNothingIsReported()
    {
        const string written = "c4 grace { g16( } a8) c4 d | e1 |";
        const string keyword = "c4 appoggiatura { g16 } a8 c4 d | e1 |";
        const string bare = "c4 grace { g16 } a8 c4 d | e1 |";

        Assert.Equal(Page(keyword), Page(written));
        // The positive control: the bow is INK — the bare grace is a different page.
        Assert.NotEqual(Page(bare), Page(written));

        Assert.Empty(GraceDrops(written));
        Assert.Empty(UnpairedSlurs(written));
    }

    [Fact]
    public void AnOpenTheMainNoteDoesNotClose_IsUnpaired_AndDrawsNoBow()
    {
        const string open = "c4 grace { g16( } a8 c4 d | e1 |";
        const string bare = "c4 grace { g16 } a8 c4 d | e1 |";

        Assert.Equal(Page(bare), Page(open));
        Assert.Empty(GraceDrops(open));
        var warning = Assert.Single(UnpairedSlurs(open));
        Assert.Contains("never closed", warning.Message);
        Assert.Equal(Book(open).IndexOf("(", System.StringComparison.Ordinal), warning.Span.Start);
    }

    [Theory]
    // A `(` on the FIRST of two grace notes: not the bow to the main note.
    [InlineData("c4 grace { f16( g16 } a8) c4 d | e1 |", "c4 grace { f16 g16 } a8 c4 d | e1 |")]
    // A `(` on a grace REST: a rest draws no bow.
    [InlineData("c4 grace { g16 r16( } a8) c4 d | e1 |", "c4 grace { g16 r16 } a8 c4 d | e1 |")]
    public void AnOpenThatIsNotTheLastGraceNotes_IsStillReported_AndTheCloseIsUnpaired(
        string written, string control)
    {
        Assert.Equal(Page(control), Page(written));
        Assert.Single(GraceDrops(written));
        var warning = Assert.Single(UnpairedSlurs(written));
        Assert.Contains("has no '(' open", warning.Message);
    }

    /// <summary>The twin writes both marks, and LilyPond draws its Slur from them.</summary>
    [Fact]
    public void TheTwin_CarriesBothMarks()
    {
        var tree = SyntaxTree.Parse(Book("c4 grace { g16( } a8) c4 d | e1 |"));
        Assert.False(tree.HasErrors);
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains("\\grace { g16 ( } a8 )", Regex.Replace(ly, @"\s+", " "));
    }
}
