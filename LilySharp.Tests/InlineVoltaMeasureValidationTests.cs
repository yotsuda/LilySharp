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
/// An inline volta ending (<c>[1. … ]</c>) is TRANSPARENT to bar accounting: its
/// barlines close the enclosing stream's bars and the running default note value
/// threads through it, exactly as the collector walks it.
/// </summary>
/// <remarks>
/// <c>MeasureCollector.ProcessMusicNode</c>'s <c>InlineVoltaSyntax</c> case renders the
/// ending's music IN PLACE in the same builder and only overlays a bracket across the
/// bars it occupies; <c>MeasureModel.Flatten</c> says the same for the cross-part pass
/// ("inline-volta interiors are ordinary written measures and flow through as
/// themselves"). <c>MeasureValidator</c> was the one pass that disagreed — it held the
/// whole ending as an opaque zero-duration item, which cost two things at once:
/// the ending's OWN bars were never checked, and the default note value did not thread
/// through, so bare notes after the <c>]</c> were priced with the duration in force
/// before the <c>[</c>. Reported 2026-08-30 on scratch/ベースタブLy/Venus.lys, whose
/// <c>[2. a,8 … ] c c c c c c c c |</c> drew three LYS2002 warnings on a page that is
/// right (the c's are eighths there, and the render engraves them as eighths).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class InlineVoltaMeasureValidationTests
{
    private static IReadOnlyList<Diagnostic> Diagnose(string music)
    {
        var tree = SyntaxTree.Parse(
            $"part mel {{\n  section A {{ {music} }}\n}}\nform main {{ A }}\nscore main {{ staff mel }}\n");
        var validator = new MeasureValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    private static IEnumerable<Diagnostic> BarFullness(string music) =>
        Diagnose(music).Where(d =>
            d.Code == DiagnosticCodes.MeasureIncomplete
            || d.Code == DiagnosticCodes.MeasureOverflow
            || d.Code == DiagnosticCodes.PickupWithoutPartial);

    private static void AssertClean(string music) => Assert.Empty(BarFullness(music));

    [Fact]
    public void BareNotesAfterAnEndingInheritTheNoteValueWrittenInsideIt() =>
        // Venus.lys's shape. The eight c's are eighths — the ending's a,8 is the last
        // written duration before them — so the bar is exactly full. Holding the ending
        // opaque kept the quarter from before the `[` and reported "duration 2".
        AssertClean("|: c'4 c' c' c' | [1. c'8 c' c' c' c' c' c' c' | ] " +
            ":| [2. c'8 c' c' c' c' c' c' c' | ] c' c' c' c' c' c' c' c' |");

    [Fact]
    public void AnEndingsOwnBarsAreChecked() =>
        // Three whole notes in one 4/4 bar, written INSIDE the ending. While the ending
        // was one opaque zero-duration item this was silent — the only bar in the book
        // nobody counted.
        Assert.Contains(BarFullness("|: c'4 c' c' c' | [1. c'1 c'1 c'1 | ] :| [2. c'4 c' c' c' | ]"),
            d => d.Code == DiagnosticCodes.MeasureOverflow);

    [Fact]
    public void AWellFormedEndingIsSilent() =>
        AssertClean("|: c'4 c' c' c' | [1. c'4 c' c' c' | c'4 c' c' c' | ] " +
            ":| [2. c'4 c' c' c' | c'4 c' c' c' | ] c'4 c' c' c' |");

    [Fact]
    public void TheNoteValueWrittenBeforeTheEndingReachesIntoIt() =>
        // The ending's bare notes are eighths, inherited across the `[` — the mirror of
        // the case above, and the reason the ending cannot be checked in a fresh frame.
        AssertClean("|: c'8 c' c' c' c' c' c' c' | [1. c' c' c' c' c' c' c' c' | ] " +
            ":| [2. c' c' c' c' c' c' c' c' | ]");
}
