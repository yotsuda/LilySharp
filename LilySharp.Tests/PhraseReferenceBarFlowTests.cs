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
/// A phrase reference whose body is plain music fills the bar it stands in, as the page plays
/// it (HANDOFF §2 R12⒝, session 571). MeasureModel always expanded it; MeasureValidator read it
/// as an opaque zero-duration item, so `riff e f |` with riff = `c4 d` — a full bar on the page —
/// drew "first measure is shorter than the meter (1/2 of 4/4)".
/// </summary>
[Trait("Category", "Unit")]
public sealed class PhraseReferenceBarFlowTests
{
    private static string Book(string phrase, string music) =>
        phrase + "\npart m { section A { " + music + " } }\nform main { A }\nscore main { staff m }\n";

    // Diagnostics that stand in the SECTION (the phrase's own block is validated where it is
    // declared, and says what it says there either way).
    private static List<string> SectionDiagnostics(string phrase, string music)
    {
        string src = Book(phrase, music);
        int sectionStart = src.IndexOf("section A", System.StringComparison.Ordinal);
        return SemanticValidation.Run(SyntaxTree.Parse(src))
            .Where(d => d.Span.Start >= sectionStart)
            .Select(d => d.Code + " " + d.Message).ToList();
    }

    [Theory]
    [InlineData("riff e f | g1 |")]           // the reference opens the bar
    [InlineData("c4 d riff | riff riff |")]  // closes it, and fills one alone
    public void AReferenceFillsTheBarItStandsIn(string music) =>
        Assert.Empty(SectionDiagnostics("phrase riff { c4 d }", music));

    [Fact]
    public void ABodyBarlineClosesTheBar_AndTheTailOpensTheNext()
    {
        // riff's `|` closes c d e f; its g2 plus the section's c2 is the next full bar.
        Assert.Empty(SectionDiagnostics("phrase riff { c4 d e f | g2 }", "riff c2 | c1 |"));
    }

    [Fact]
    public void TheBodysNoteValueCarriesOn()
    {
        // The collector keeps the body's exit value (EnterDefaultFrame resets on the way IN
        // only), so the bare a b after riff are halves: 1/2 + 1 = 3/2, overfull.
        var d = SectionDiagnostics("phrase riff { c4 d e f | g2 }", "riff a b | c1 |");
        Assert.Contains(d, m => m.Contains("3/2"));
    }

    [Fact]
    public void AStructuredBodyStaysOpaque()
    {
        // A meter change in the body keeps the old reading: the reference prices nothing, so
        // the section's bar is the half its own notes make.
        var d = SectionDiagnostics("phrase riff { time 3/4 c4 d }", "riff e f | g1 |");
        Assert.Contains(d, m => m.Contains("1/2"));
    }
}
