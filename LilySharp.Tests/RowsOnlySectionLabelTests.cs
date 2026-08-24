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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A staffless lead sheet sets its <c>form</c> section names ON the chord line, level with
/// the symbols, and the symbols keep out of the label's frame.
/// </summary>
/// <remarks>
/// ⚠️ THIS IS A DECIDED DIVERGENCE FROM LilyPond, NOT A PORT (owner's decision, 2026-08-24).
/// LilyPond puts the mark ABOVE the row's symbols: probes/mark-chord-row.ly reads the row's
/// ink top plus <c>outside-staff-padding</c> 0.460000 on book MKT (SectionLabel), MKS
/// (RehearsalMark) and MKV (the same book with taller symbols) — one number for two grobs and
/// two symbol heights, so it is the padding and not an accident of the ink. Lily# is asked for
/// the printed-chart convention instead. See
/// <c>MusicMarkEngraver.StafflessAnchorRefpointBelowTop</c>.
/// <para>
/// ★ THE TWO CONTROLS ARE THE POINT (HANDOFF 5.0-1, and bone 2 of session 243: an arm that
/// cannot be made red is not a control). Both were RED while this was being written:
/// <list type="bullet">
/// <item>the LYRICS-ONLY arm printed its <c>Main</c> box straight through the first syllable
/// when the rule fired on any staffless sheet rather than on one whose anchor row carries
/// CHORD symbols;</item>
/// <item>the X arm — <c>A2</c> printed through <c>Dmaj7</c> on the owner's own book — was red
/// with only the Y half of the change in, which is HANDOFF 5.3's "placement and reservation
/// are one claim" caught in the act.</item>
/// </list>
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class RowsOnlySectionLabelTests
{
    /// <summary>The size <c>SharedRenderer.DrawSingleMusicMark</c> sets a section label at.</summary>
    private const double SectionLabelFontSize = 2.2;

    private static string RowsOnly(bool withChords) => $$"""
        octave absolute
        key c major

        part melody {
          section A { c'4 d' e' f' | g' a' b' c'' | }
          section B { c'4 d' e' f' | g' a' b' c'' | }
        }
        {{(withChords ? """
        chords harm {
          section A { C | G | }
          section B { C | G | }
        }
        """ : "")}}
        lyrics verse {
          section A { one two three four | five six sev- en | }
          section B { one two three four | five six sev- en | }
        }

        form main { A B }

        score main {{{(withChords ? "\n          chords harm as names" : "")}}
          lyrics verse sings melody
        }
        """;

    /// <summary>With a chord row leading a staffless sheet, every label sits ON the chord line.</summary>
    [Fact]
    public void SectionLabel_OnAStafflessChordSheet_SharesTheChordBaseline()
    {
        var g = RenderedGeometry.Render(RowsOnly(withChords: true));
        var labels = g.MusicMarkLabels;
        Assert.Equal(2, labels.Count);          // one per section, one per system
        foreach (var label in labels)
        {
            // The chord symbols of the label's own system: the ones nearest it vertically.
            double nearest = g.ChordSymbols
                .Select(c => c.Y)
                .OrderBy(y => System.Math.Abs(y - label.Y))
                .First();
            Assert.Equal(nearest, label.Y, 9);
        }
    }

    /// <summary>...and no symbol stands inside the label's frame.</summary>
    [Fact]
    public void SectionLabel_OnAStafflessChordSheet_IsClearOfEverySymbolInX()
    {
        var g = RenderedGeometry.Render(RowsOnly(withChords: true));
        foreach (var label in g.MusicMarkLabels)
        {
            // DrawnText.X is the box CENTRE for a mark (TextAnchor.Middle) and the ink LEFT
            // for a chord symbol (TextAnchor.Start) — the two conventions the renderer draws
            // them with.
            double half = TextFontMetrics.Advance(
                label.Text, SectionLabelFontSize, sans: false, FontStyle.Bold) / 2
                + MusicMarkEngraver.LabelBoxPadding;
            double boxLeft = label.X - half;
            double boxRight = label.X + half;
            foreach (var chord in g.ChordSymbols.Where(c => System.Math.Abs(c.Y - label.Y) < 1e-9))
            {
                double inkRight = chord.X + TextFontMetrics.Advance(
                    chord.Text, chord.FontSize, sans: true, FontStyle.Regular);
                Assert.True(chord.X >= boxRight || inkRight <= boxLeft,
                    $"'{label.Text}' box [{boxLeft:F6}, {boxRight:F6}] overlaps "
                    + $"'{chord.Text}' ink [{chord.X:F6}, {inkRight:F6}] on the shared line.");
            }
        }
    }

    /// <summary>
    /// CONTROL — a staffless sheet with NO chord row keeps its label ABOVE the row.
    /// </summary>
    /// <remarks>
    /// The decision is "level with the CHORD NAMES", and a lyrics-only sheet has none. It is
    /// also the arm with no repair available: nothing would move out of the label's way, so a
    /// label on that line prints through the words. It did, before the gate.
    /// </remarks>
    [Fact]
    public void SectionLabel_OnAStafflessLyricsSheet_StaysAboveTheRow()
    {
        var g = RenderedGeometry.Render(RowsOnly(withChords: false));
        Assert.Empty(g.ChordSymbols);
        var label = Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<DrawnText>>(
            g.MusicMarkLabels)[0];
        // Device Y grows DOWNWARD, so "above" is a smaller Y — and by a real margin, not a
        // rounding: the label must clear the syllables it would otherwise overprint.
        double firstSyllable = g.LyricSyllables.Where(s => s.Y > label.Y).Min(s => s.Y);
        Assert.True(firstSyllable - label.Y > 1.0,
            $"the label sits {firstSyllable - label.Y:F6} above the first syllable it "
            + "overlaps; on a sheet with no chord row it must keep its own band.");
    }

    /// <summary>
    /// CONTROL — the same book WITH a staff is untouched: the label keeps its band above the
    /// staff, which the owner confirmed is already right (2026-08-24).
    /// </summary>
    [Fact]
    public void SectionLabel_WithAStaff_KeepsItsBandAboveTheChordRow()
    {
        const string withStaff = """
            octave absolute
            key c major

            part melody {
              section A { c'4 d' e' f' | g' a' b' c'' | }
              section B { c'4 d' e' f' | g' a' b' c'' | }
            }
            chords harm {
              section A { C | G | }
              section B { C | G | }
            }

            form main { A B }

            score main {
              chords harm
              staff melody
            }
            """;
        var g = RenderedGeometry.Render(withStaff);
        var label = g.MusicMarkLabels[0];
        double nearestChord = g.ChordSymbols
            .Select(c => c.Y)
            .OrderBy(y => System.Math.Abs(y - label.Y))
            .First();
        Assert.True(nearestChord - label.Y > 1.0,
            $"with a staff present the label must stay {nearestChord - label.Y:F6} > 1.0 above "
            + "the chord line — the staffless convention may not reach this book.");
    }
}
