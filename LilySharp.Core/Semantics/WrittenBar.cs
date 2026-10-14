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

namespace LilySharp.Core.Semantics;

/// <summary>
/// What a span of music between two written bar lines is worth when it is longer than its bar:
/// the page (MeasureBuilder) and the playback (MidiExporter.AlignBarLine) read it by this one rule.
/// </summary>
/// <remarks>
/// <para>
/// THE WRITTEN BAR LINE WINS (owner's decision, 2026-10-14, Lab sessions/p904/sync): a bar with
/// too much in it is still one bar, and what starts after its end is neither drawn nor played —
/// `g4 g c,8 f1 c1 | e1` beside a part of full bars is the bar `g4 g c,8 f1`, and the `e1` stands
/// under the other part's third bar. Until then the page closed the bar where its beats ran out
/// and made the `c1` a bar of its own, so the part ran a bar longer than the others (and the
/// score, laid out at their length, lost its last bar).
/// </para>
/// <para>
/// THE ONE EXCEPTION IS A BAR LINE LEFT OUT, AND IT IS AN ERROR (LYS2017): a span within a quarter
/// of a bar of two or more whole bars keeps every note, its bars closing where their beats fill
/// (the last one short if the span is) — 15/8 in 4/4, `… cis, break fis,,8 …` in the owner's
/// 銀河鉄道999, where the `|` before the break is missing. A bar line is never made up silently.
/// </para>
/// <para>
/// LILYSHARP-OWN: in LilyPond `|` is only a bar CHECK — Timing draws the bars, a failed check
/// warns once and the music runs on across the bars it overfills, "one mistake offsets all
/// subsequent bar checks" (lily/timing-translator.cc:222-240). Lily#'s
/// written bar line IS the bar (owner's decision, 2026-10-14; semantics are musical validity's
/// to decide, RULES §5.2): what overfills it is left out, a bar line left out is an error.
/// </para>
/// </remarks>
public static class WrittenBar
{
    /// <summary>
    /// How many bar lines a written span looks to be missing — the whole bars of the meter it
    /// holds after the bar it opened, when it is within a quarter of a bar of that; 0 when it is
    /// one bar (with too much in it).
    /// </summary>
    /// <param name="span">The span's length.</param>
    /// <param name="first">The bar it opened: the meter, or a pickup.</param>
    /// <param name="meter">The meter's bar.</param>
    public static int MissingBarLines(double span, double first, double meter)
    {
        if (meter <= 0)
            return 0;
        double more = (span - first) / meter;
        int whole = (int)System.Math.Round(more, System.MidpointRounding.AwayFromZero);
        return whole >= 1 && System.Math.Abs(more - whole) <= 0.25 ? whole : 0;
    }
}
