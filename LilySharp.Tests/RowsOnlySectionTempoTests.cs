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
using LilySharp.Core.Rendering;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A rows-only sheet (chords or lyrics, no staff) prints a section header's tempo at the
/// section's start, as a staff does (session 787). Until then the rows-only form walk
/// (<c>MeasureCollector.EnsureSectionStartsForRows</c>) had no arm for it, so a chord grid
/// drew only its HEADER tempo and <c>section Chorus { tempo 4 = 90 … }</c> was silent on the
/// page while its MIDI and its <c>.ly</c> twin carried it.
/// </summary>
/// <remarks>
/// DIFFERENTIAL against the same book WITH a staff: the staff walk's metronome marks are the
/// oracle — the same equations, in the same left-to-right order, and the mid-sheet one at
/// the same bar (between the symbols of the bar before and the bar it opens). The height of
/// the mark on a row is the ledger's business (tempo.staffless.*), not this net's.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class RowsOnlySectionTempoTests
{
    private static string Book(string render, string header = "tempo 4 = 111", string headerA = "", string headerB = "tempo 4 = 90") => $$"""
        octave absolute
        {{header}}
        time 4/4
        key c major
        part melody { clef treble }
        section A {
          {{headerA}}
          melody { c'1 | d'1 | }
          chords prog { C | G | }
          lyrics words { one two three four | five six sev- en | }
        }
        section B {
          {{headerB}}
          melody { e'1 | f'1 | }
          chords prog { Am | F | }
          lyrics words { eight nine ten e- le- ven | twelve thir- teen | }
        }
        form main { ~A ~B }
        score main { {{render}} }
        """;

    /// <summary>The metronome equations drawn ("= N"), left to right.</summary>
    private static List<DrawnText> Equations(RenderedGeometry g) =>
        g.Texts.Where(t => t.Role == TextRole.Tempo && t.Text.StartsWith("= ", StringComparison.Ordinal))
            .OrderBy(t => t.X).ToList();

    [Fact]
    public void ASectionHeadersTempo_IsPrintedOnARowsOnlySheet_WhereTheStaffPrintsIt()
    {
        var staffless = RenderedGeometry.Render(Book("chords prog  lyrics words"));
        var staffful = RenderedGeometry.Render(Book("chords prog  staff melody  lyrics words"));
        Assert.Equal(Equations(staffful).Select(t => t.Text), Equations(staffless).Select(t => t.Text));
        var eqs = Equations(staffless);
        Assert.Equal(new[] { "= 111", "= 90" }, eqs.Select(t => t.Text).ToArray());

        // The section's mark opens bar 3: its NOTEHEAD (the mark's ink left; the equation
        // trails it) stands on bar 3's chord, as LilyPond's does on the twin (both self-align
        // on the bar's musical column — Lab sessions/p787/probes/lp-st.png).
        var symbols = staffless.ChordSymbols.OrderBy(c => c.X).ToList();
        var fonts = LilySharp.Core.Rendering.ScoreTextMetrics.Bundled;
        var heads = staffless.Glyphs
            .Where(g => g.Glyph == LilySharp.Core.Svg.EmmentalerGlyphs.NoteheadBlack
                        && Math.Abs(g.FontSize - LilySharp.Core.Svg.Layout.MetronomeMarkGeometry.NoteSize(fonts)) < 1e-9)
            .OrderBy(g => g.X).ToList();
        Assert.Equal(2, heads.Count);
        Assert.True(symbols[1].X < heads[1].X && Math.Abs(heads[1].X - symbols[2].X) < 1.0,
            $"the `= 90' mark's note at {heads[1].X:F2} should stand on `Am' ({symbols[2].X:F2}), past `G' ({symbols[1].X:F2})");
    }

    [Fact]
    public void ASectionHeadersTempo_AtThePiecesOpening_IsTheOpeningTempo_OnARowsOnlySheet()
    {
        // The first section's own tempo REPLACES the file's (the staff walk's rule at the
        // piece's opening): one mark, reading the section's count.
        var staffless = RenderedGeometry.Render(Book("chords prog  lyrics words", headerA: "tempo 4 = 72", headerB: ""));
        var staffful = RenderedGeometry.Render(Book("chords prog  staff melody  lyrics words", headerA: "tempo 4 = 72", headerB: ""));
        Assert.Equal(new[] { "= 72" }, Equations(staffful).Select(t => t.Text).ToArray());
        Assert.Equal(new[] { "= 72" }, Equations(staffless).Select(t => t.Text).ToArray());
    }

    [Fact]
    public void ASectionHeadersMarking_WithoutACount_IsPrintedOnARowsOnlySheet()
    {
        var staffless = RenderedGeometry.Render(Book("chords prog", header: "", headerB: "tempo \"Slower\""));
        var marks = staffless.Texts.Where(t => t.Role == TextRole.Tempo).Select(t => t.Text).ToList();
        Assert.Contains("Slower", marks);
    }

    [Fact]
    public void ASectionPlayedTwice_PrintsItsTempoAtEachPass_OnARowsOnlySheet()
    {
        // Every pass gets its mark, as every pass of the staff walk's ProcessSection does.
        string book = Book("chords prog").Replace("form main { ~A ~B }", "form main { ~A ~B ~B }");
        string staffBook = Book("chords prog  staff melody").Replace("form main { ~A ~B }", "form main { ~A ~B ~B }");
        Assert.Equal(
            Equations(RenderedGeometry.Render(staffBook)).Select(t => t.Text),
            Equations(RenderedGeometry.Render(book)).Select(t => t.Text));
        Assert.Equal(3, Equations(RenderedGeometry.Render(book)).Count);
    }
}
