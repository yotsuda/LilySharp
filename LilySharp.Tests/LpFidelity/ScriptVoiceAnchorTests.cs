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
using LilySharp.Core.Svg;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// A script hangs off the note of the voice it was WRITTEN in. Its ItemIndex counts that
/// voice's items, so resolving it against the staff's primary voice returns whatever note
/// shares the index — a different pitch, and a different COLUMN once the rhythms differ.
/// </summary>
/// <remarks>
/// The pitch half is measured against LilyPond by the ledger pair
/// <c>script.{staccato,marcato}-below.staff-to-ink-top</c> (probes/dynamic-support.ly round
/// 3: LilyPond reads ONE number for both glyphs, and a primary-voice anchor spread them by
/// 0.9 while parking both inside the staff). Those books cannot see the X half — both voices
/// carry a whole note, so item 0 of either voice is the same column — which is what this
/// test is for: the two voices here have DIFFERENT rhythms, so voice two's item 1 and voice
/// one's item 1 are three columns apart and only the right lookup lands on the right one.
/// LILYPOND-REF: ly/engraver-init.ly:359,414-416 Script_engraver — consisted by the
///   <c>\name Voice</c> context, so the script is a grob of its own voice.
/// </remarks>
[Trait("Category", "Unit")]
public class ScriptVoiceAnchorTests
{
    // Voice one runs in eighths, voice two in halves, so the two item lists disagree from
    // index 1 on: voice two's second item is at timing 1/2 (voice one's FIFTH eighth) while
    // voice one's second item is at 1/8. The registers are kept a sixth apart so no
    // note-column collision shift moves a head sideways and blurs the reading.
    private const string TwoRhythms = """
        part m { clef treble }
        section S { m { voice { c''8 d'' e'' f'' g'' a'' b'' c''' } { g4 g2@staccato.down g4 } } }
        form main { S }
        score main "o" { staff m }
        """;

    [Fact]
    public void BelowScript_SitsOnItsOwnVoicesColumn_NotThePrimaryVoicesItemOfTheSameIndex()
    {
        var g = RenderedGeometry.Render(TwoRhythms);

        var dot = g.Glyphs.Single(x => x.Glyph == EmmentalerGlyphs.ArticStaccatoAbove);
        // Voice two's second item is the only HALF head, so it names its own column with no
        // index arithmetic. Voice one's columns are the DISTINCT black-head X's — distinct,
        // because a shared onset draws two black heads at one X and positional indexing over
        // the raw list would read the first column twice.
        var half = g.Glyphs.Single(x => x.Glyph == EmmentalerGlyphs.NoteheadHalf);
        var eighthColumns = g.Glyphs.Where(x => x.Glyph == EmmentalerGlyphs.NoteheadBlack)
            .Select(x => x.X).Distinct().OrderBy(x => x).ToList();

        // Voice one's eight eighths, four of which share an onset with a voice-two quarter.
        Assert.Equal(8, eighthColumns.Count);

        // The script is centred on its head, so its origin sits within the head's own width
        // of that head's left edge; every other column is a whole eighth away.
        Assert.True(dot.X > half.X && dot.X - half.X < 1.4,
            $"the staccato sits at x={dot.X:F6}, its own voice's half note at x={half.X:F6} — "
            + "the script did not land on the note it was written on.\n"
            + "Drawn geometry:\n" + g.Describe());

        // The falsifier, stated as its own assert: voice ONE's item 1 (the second eighth) is
        // where a primary-voice lookup would have put it, and it is nowhere near.
        double primaryVoiceItem1 = eighthColumns[1];
        Assert.True(dot.X - primaryVoiceItem1 > 2.0,
            $"the staccato sits at x={dot.X:F6}, voice one's second item at "
            + $"x={primaryVoiceItem1:F6} — that is the primary-voice anchor, not this "
            + "script's own voice.");
    }

    [Fact]
    public void AScriptInThePrimaryVoiceIsUnaffected()
    {
        // The control: the SAME texture with the script on voice one's second item. Here the
        // two lookups agree by construction, which is why the whole corpus (three fixtures
        // with both voice{} and scripts, all of them scripting the primary voice) never
        // observed the defect — and why this pair, not a corpus diff, is its net.
        var g = RenderedGeometry.Render("""
            part m { clef treble }
            section S { m { voice { c''8 d''@staccato.down e'' f'' g'' a'' b'' c''' } { g4 g2 g4 } } }
            form main { S }
            score main "o" { staff m }
            """);

        var dot = g.Glyphs.Single(x => x.Glyph == EmmentalerGlyphs.ArticStaccatoAbove);
        var eighthColumns = g.Glyphs.Where(x => x.Glyph == EmmentalerGlyphs.NoteheadBlack)
            .Select(x => x.X).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(8, eighthColumns.Count);

        Assert.True(dot.X > eighthColumns[1] && dot.X - eighthColumns[1] < 1.4,
            $"the staccato sits at x={dot.X:F6}, voice one's second eighth at "
            + $"x={eighthColumns[1]:F6}.\nDrawn geometry:\n" + g.Describe());
    }
}
