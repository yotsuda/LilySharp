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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A pickup is spent by the bar line that closes its bar — short or not — as the page, the
/// MIDI and the LilyPond twin spend it (Semantics.BarContext.SpendPartial), and it still
/// closes itself when the duration written into it reaches the declared length, as the page
/// auto-completes a bar (MeasureBuilder.AddItem). Until 2026-10-03 the MusicXML knew only the
/// second rule: a pickup shorter than declared ran on across its <c>|</c> and closed in the
/// middle of the next bar, splitting <c>d4 e f g</c> into two measures the page draws as one
/// (REFACTOR_PLAN stage C5, decision A2; no book in the corpus and no test observed it).
/// </summary>
public class MusicXmlPickupBarLineTests
{
    private static MusicXmlDocument Export(string source)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(source));

    private static List<int> NotesPerMeasure(MusicXmlDocument doc)
        => doc.Parts.Single().Measures.Select(m => m.Notes.Count).ToList();

    /// <summary>The short pickup: `partial 2` over a quarter note and a bar line. The `|`
    /// closes bar 0 with the quarter alone, and the four quarters after it are ONE bar.</summary>
    [Fact]
    public void AShortPickup_IsSpentByItsBarLine()
    {
        var doc = Export("octave absolute  time 4/4  partial 2  c'4 | d'4 e' f' g' | a'1 |");
        var measures = doc.Parts.Single().Measures;
        Assert.Equal(new[] { 1, 4, 1 }, NotesPerMeasure(doc));
        Assert.True(measures[0].Implicit);
        Assert.Equal(0, measures[0].Number);
        Assert.Equal(1, measures[1].Number);
    }

    /// <summary>The control: a pickup of the declared length splits the same way whether its
    /// bar line spends it or its duration closes it — the differential above is only a claim
    /// because this one does not move.</summary>
    [Fact]
    public void AFullPickup_SplitsTheSameUnderBothRules()
    {
        var doc = Export("octave absolute  time 4/4  partial 2  c'2 | d'4 e' f' g' | a'1 |");
        Assert.Equal(new[] { 1, 4, 1 }, NotesPerMeasure(doc));
    }

    /// <summary>The auto-close kept: a pickup with no bar line written after it closes when
    /// the written duration reaches the declared length, as the page does — `c'4 d'` is bar 0
    /// and the bar line after `g'` closes bar 1, not an empty one.</summary>
    [Fact]
    public void APickupWithNoBarLine_ClosesAtItsDeclaredLength()
    {
        var doc = Export("octave absolute  time 4/4  partial 2  c'4 d' e'4 f' g' a' | b'1 |");
        Assert.Equal(new[] { 2, 4, 1 }, NotesPerMeasure(doc));
    }

    /// <summary>A bare `R` inside a pickup bar is worth the pickup, not the meter
    /// (Semantics.BarContext.BarLength, as the page's EmitEmptyMeasure and the MIDI read it):
    /// `partial 2  R |` is a half-note rest in bar 0, and the `|` spends the pickup.</summary>
    [Fact]
    public void ABareRInAPickup_IsWorthThePickup()
    {
        var doc = Export("octave absolute  time 4/4  partial 2  R | c'4 d' e' f' |");
        var measures = doc.Parts.Single().Measures;
        Assert.Equal(new[] { 1, 4 }, NotesPerMeasure(doc));
        int divisions = measures[0].Attributes!.Divisions!.Value;
        var rest = measures[0].Notes.Single();
        Assert.True(rest.IsRest);
        Assert.Equal(2 * divisions, rest.Duration);
    }

    /// <summary>A section header's pickup goes the same way: the part's cell closes its
    /// short pickup at the `|`.</summary>
    [Fact]
    public void AShortHeaderPickup_IsSpentByItsBarLine()
    {
        var doc = Export("""
            octave absolute
            time 4/4
            section A { partial 2  melody { c'4 | d'4 e' f' g' | a'1 | } }
            form main { A }
            score main { staff melody }
            """);
        Assert.Equal(new[] { 1, 4, 1 }, NotesPerMeasure(doc));
    }
}
