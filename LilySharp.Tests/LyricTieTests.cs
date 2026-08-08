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

using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Collector;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tildes INSIDE a lyric syllable become tie symbols (lyric-tie.ly's claim):
/// Lily# rewrites them to the undertie ‿ (U+203F) at collection, and the bundled
/// serif face really has that glyph — an arc BELOW the baseline with a real
/// advance, so the tie is drawn, not tofu'd or silently zero-width.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-markup-commands.scm:4719-4773 tied-lyric — LilyPond
/// itself equates the tilde with U+203F in its as-string form, and composes the
/// drawn form from feta ties.lyric.default/.short glyphs between half
/// word-spaces (the composition is NOT ported — see the ledger's open note).
/// </remarks>
[Trait("Category", "Unit")]
public class LyricTieTests
{
    [Fact]
    public void InteriorTilde_BecomesTheUndertie()
    {
        Assert.Equal("wa‿o‿a", LyricSyllableReader.DisplaySyllable("wa~o~a"));
    }

    [Fact]
    public void BundledSerifFace_HasTheUndertieGlyph_AnArcBelowTheBaseline()
    {
        var ink = TextFontMetrics.Ink("‿", 3.2, sans: false, FontStyle.Regular);
        double advance = TextFontMetrics.Serif("‿", 3.2);

        // MEASURED (TeX Gyre Schola regular at 3.2 ss): ink bottom −0.6464,
        // top −0.2112, advance 1.6047. The exact digits are the face's own; what
        // the claim needs is REAL ink, wholly below the baseline, with an advance.
        Assert.True(advance > 0, "the undertie must take horizontal room");
        Assert.True(ink.Top < 0 && ink.Bottom < ink.Top,
            $"the undertie must be an arc below the baseline (got bottom {ink.Bottom:F4}, top {ink.Top:F4})");
    }
}
