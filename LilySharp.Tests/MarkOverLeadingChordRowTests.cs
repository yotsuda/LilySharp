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
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section label stacked over a chord row that LEADS a system is an element of the system
/// above's lyric chain (owner's report 2026-11-01, 帰り道の灯り: every mid-line label — Verse,
/// Chorus, Bridge, Coda — was engraved through the second verse of the system above). Two
/// halves: the chain prices the label on the row (<c>LayoutEngine.RowUpWithMarksRestingOnIt</c>),
/// and when the solve moves the row, the label moves with it
/// (<c>LayoutEngine.ApplySolvedRowPositions</c>) instead of floating where the row was.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MarkOverLeadingChordRowTests
{
    // Two verses, so the second one hangs low; B opens mid-line on the third system, whose
    // chord row (names over diagrams) leads it — Lab sessions/p1036/strokes/markclash2.lys.
    private const string Book = """
        time 4/4
        octave absolute
        layout { chordDiagrams all }
        part singer { instrument alto }
        section A {
          chords harmony { C | G | Am | F | C | G | Am | F | }
          singer { c'4 d' e' f' | g'4 a' b' c'' | c''4 b' a' g' | f'4 e' d' c' | c'4 d' e' f' | g'4 a' b' c'' | c''4 b' a' g' | f'4 e' d' c' | }
          lyrics words {
            verse 1 { a b c d | e f g h | i j k l | m n o p | a b c d | e f g h | i j k l | m n o p | }
            verse 2 { a b c d | e f g h | i j k l | m n o p | a b c d | e f g h | i j k l | m n o p | }
          }
        }
        section B {
          chords harmony { C | G | Am | F | C | G | }
          singer { c'4 d' e' f' | g'4 a' b' c'' | c''4 b' a' g' | f'4 e' d' c' | c'4 d' e' f' | g'4 a' b' c'' | }
          lyrics words { a b c d | e f g h | i j k l | m n o p | a b c d | e f g h | }
        }
        form { A A B }
        score { chords harmony  staff singer  lyrics words on singer }
        """;

    [Fact]
    public void ALabelOverALeadingChordRow_ClearsTheVersesOfTheSystemAbove_ByTheSystemPadding()
    {
        var g = RenderedGeometry.Render(Book);
        var fonts = ScoreTextMetrics.Bundled;
        var label = g.MusicMarkLabels.Single(t => t.Text == "B");
        // The frame about the drawn text: half its height each way of the box centre, which
        // the text's baseline stands under by the frame's own offset.
        var (frameBottom, frameTop) = MusicMarkEngraver.LabelFrameAboutBaseline(fonts, Core.Svg.Model.MusicMarkType.SectionLabel, "B", boxed: true);
        double boxTop = label.Y - frameTop;          // device Y grows down
        double halfW = MusicMarkEngraver.LabelBoxHalfWidth(fonts, Core.Svg.Model.MusicMarkType.SectionLabel, "B", boxed: true);

        // The syllables of the system above whose ink stands over the label's frame: the
        // lowest of them (the second verse) must stand a system padding clear of the frame.
        var over = g.LyricSyllables
            .Where(s => s.Y < label.Y && Math.Abs(s.X - label.X) < halfW + 1.5)
            .ToList();
        Assert.NotEmpty(over);
        double lowestBaseline = over.Max(s => s.Y);
        double gap = boxTop - lowestBaseline;
        Assert.True(gap >= 1.0 - 1e-6,
            $"the label's frame top stands {gap:F3} under the lowest syllable's baseline above it; the "
            + "system padding (1.0) is the least — the chain has to see the label on the row.");
        Assert.True(gap < 3.0, $"…but not {gap:F3}: the label rests on the row, not a band above it.");

        // …and the label rests ON the row it leads: just outside-staff-padding over the names.
        double nameTop = g.ChordSymbols.Where(c => c.Y > label.Y).Min(c => c.Y)
            - ChordNameEngraver.SymbolInk(fonts, "C").Top;
        double boxBottom = label.Y - frameBottom;
        Assert.InRange(nameTop - boxBottom, OutsideStaffStacker.OutsideStaffPadding - 0.05, OutsideStaffStacker.OutsideStaffPadding + 0.6);
    }
}
