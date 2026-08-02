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
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A meter CHANGE at a line break prints a courtesy signature at the end of the previous
/// line; the meter merely being in force does not.
/// </summary>
/// <remarks>
/// The measured halves live in the ledger (<c>courtesy.meter.barline-to-meter</c> and
/// <c>courtesy.meter.barline-to-cancellation</c>, probe courtesy-meter.ly). This file holds
/// the half the ledger is STRUCTURALLY BLIND TO: the case where nothing is drawn. A corpus of
/// distances between drawn things cannot state "and here there is no glyph" — it has nothing
/// to measure — so an implementation that printed the meter at EVERY line end would keep both
/// ledger points exact and still be badly wrong.
/// LILYPOND-REF: lily/time-signature-engraver.cc:114-118 Time_signature_engraver::process_music
///   — initialTimeSignatureVisibility (end-of-line-invisible) is stamped on the FIRST
///   signature only, guarded by <c>scm_is_null (last_spec_)</c>; every later one keeps the
///   grob's all-visible default.
/// </remarks>
public sealed class CourtesyMeterTests
{
    /// <summary>
    /// Two systems: section A fills the first, section B opens the second with whatever
    /// <paramref name="sectionBHeader"/> declares. Section A's rest is written to fill
    /// <paramref name="openingMeter"/>.
    /// </summary>
    private static string Book(string openingMeter, string restA, string sectionBHeader) => $$"""
        octave absolute
        time {{openingMeter}}
        key ees major

        part m { clef bass }

        section A { m { {{restA}} | } }
        section B { {{sectionBHeader}} m { d,2 e, | } }

        form main { A break B }

        score main { staff m }
        """;

    /// <summary>
    /// Glyphs drawn to the right of the first system's final bar line — i.e. the end-of-line
    /// courtesy group and nothing else, since the next system's glyphs start back at the left
    /// margin and so sit at much smaller x.
    /// </summary>
    private static int CourtesyGlyphCount(string source)
    {
        var g = RenderedGeometry.Render(source);
        double barRight = g.BarlineRight(0);
        return g.Glyphs.Count(x => x.X > barRight + 1e-9);
    }

    [Fact]
    public void MeterChangeAtABreak_PrintsACourtesyMeter()
    {
        // 2/4 → 4/4 across the break: exactly one glyph after the line's bar line, the C.
        Assert.Equal(1, CourtesyGlyphCount(Book("2/4", "r2", "time 4/4")));
    }

    [Fact]
    public void NoMeterChange_LeavesTheLineEndBare()
    {
        // ⚠️ THE POINT OF THIS FILE. One meter throughout, so the INITIAL signature is
        // end-of-line-invisible and nothing at all follows the bar line.
        Assert.Equal(0, CourtesyGlyphCount(Book("4/4", "r1", "")));
    }

    [Fact]
    public void KeyChangeWithoutMeterChange_PrintsTheKeyAndNoMeter()
    {
        // E-flat → A major, meter unchanged: 3 cancellation naturals + 3 sharps = 6 glyphs,
        // and NO seventh. The pairing matters — a fix that keyed the courtesy meter off "the
        // next line has a prefix" rather than off a meter CHANGE would print seven here.
        Assert.Equal(6, CourtesyGlyphCount(Book("4/4", "r1", "key a major")));
    }

    [Fact]
    public void KeyAndMeterChange_PrintsBoth()
    {
        // Cancellation + new key + the meter: the shape a real book has.
        Assert.Equal(7, CourtesyGlyphCount(Book("2/4", "r2", "time 4/4 key a major")));
    }
}
