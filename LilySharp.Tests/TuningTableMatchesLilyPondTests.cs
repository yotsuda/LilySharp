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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Every tuning this table carries, pinned string by string to LilyPond's own.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/string-tunings-init.ly — makeDefaultStringTuning, whose chords are
/// written in absolute pitch from the HIGHEST string number (the lowest pitch) first, which
/// is the same order as the arrays here.
/// <para>
/// The audit that produced these rows (2026-09-13) asked the same three questions as the
/// drum one: a tuning of ours LilyPond does not have (NONE — the seven spellings are five
/// tunings, and every one is LilyPond's), a string that disagrees (NONE), and what
/// LilyPond has that we do not — about twenty-five more tunings: the guitar's drop-D,
/// drop-C, open-G, open-D, DADGAD, lute, asus4 and seven-string, the bass's drop-D, the
/// banjo's seven, mandolin, the tenor and baritone ukulele, and violin / viola / cello /
/// double bass. A scope decision, recorded in HANDOFF §1, not a defect.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TuningTableMatchesLilyPondTests
{
    [Theory]
    // name                       LilyPond's chord           its strings as MIDI
    [InlineData(TuningType.Guitar, "guitar-tuning <e, a, d g b e'>", new[] { 40, 45, 50, 55, 59, 64 })]
    [InlineData(TuningType.Bass, "bass-tuning <e,, a,, d, g,>", new[] { 28, 33, 38, 43 })]
    [InlineData(TuningType.Bass5, "bass-five-string-tuning <b,,, e,, a,, d, g,>", new[] { 23, 28, 33, 38, 43 })]
    [InlineData(TuningType.Bass6, "bass-six-string-tuning <b,,, e,, a,, d, g, c>", new[] { 23, 28, 33, 38, 43, 48 })]
    // ⚠️ Re-entrant, and LilyPond writes it that way too: the g is ABOVE the c, so the
    // array is not ascending and must not be "fixed" into ascending order.
    [InlineData(TuningType.Ukulele, "ukulele-tuning <g' c' e' a'>", new[] { 67, 60, 64, 69 })]
    public void EveryTuningCarriesLilyPondsStrings(TuningType type, string lilyPond, int[] strings)
    {
        Assert.Equal(strings, Tunings.GetTuning(type));
        Assert.Equal(strings.Length, Tunings.GetStringCount(type));
        Assert.False(string.IsNullOrEmpty(lilyPond));   // the citation travels with the row
    }

    [Fact]
    public void TheSpellingsAreLilyPondsNames_OrItsAbbreviations()
    {
        // Seven words, five tunings: `standard` and `guitar` are LilyPond's guitar-tuning,
        // `uke` is its ukulele-tuning. No spelling here names a tuning LilyPond lacks — the
        // question that found `hhs` in the drum table two legs ago.
        Assert.Equal(
            new[] { "bass", "bass5", "bass6", "guitar", "standard", "uke", "ukulele" },
            LanguageVocabulary.TuningNames.OrderBy(n => n, System.StringComparer.Ordinal).ToArray());
    }
}
