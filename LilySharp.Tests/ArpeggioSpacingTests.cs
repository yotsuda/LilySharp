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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The arpeggio as a part of its COLUMN's reach: it stands <c>padding</c> left of the chord's
/// leftmost support — a head, a reversed head or an accidental — and the column's leftward
/// reach, which the bar line → column minimum and the keep-inside-line rod read, ends at the
/// wiggle's own left edge.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/paper-column.cc:145-164 Paper_column::minimum_distance — the right
///   column's conditional skyline (the arpeggio's box) is merged before the distance is taken;
/// LILYPOND-REF: lily/accidental-engraver.cc:298-307 make_standard_accidental — every
///   accidental joins the support of the arpeggio, "so it is put left of the accidentals";
/// LILYPOND-REF: scm/define-grobs.scm:205-227 Arpeggio (padding . 0.5), no extra-spacing-width.
/// The regime is held by ledger points arpeggio.x.barline-to-wiggle (probe ABL) and
/// arpeggio.x.right-edge-to-accidental (AAC); these assert the RULE by perturbation — the
/// arpeggio on and off the same chord — so a literal that happened to fit one book fails.
/// </remarks>
[Trait("Category", "Unit")]
public class ArpeggioSpacingTests
{
    // c' e' g' in treble: positions -6, -4, -2 — no seconds, so no reversed head, and every
    // head below the middle line, so the stem points up and its ink stays on the right.
    private static ChordItem Triad(bool arpeggio, string? lowestAccidental = null)
        => new(ImmutableArray.Create(
                new ChordNoteInfo(-6, lowestAccidental, false),
                new ChordNoteInfo(-4, null, false),
                new ChordNoteInfo(-2, null, false)),
            Fraction.Quarter, 0, 0, hasArpeggio: arpeggio);

    [Fact]
    public void ThePlainTriadReachesNothingToTheLeft()
    {
        Assert.Equal(0.0, SpacingRules.CalculateLeftExtent(Triad(arpeggio: false)), 9);
        Assert.Equal(0.0, SpacingRules.ChordSupportLeftReach(Triad(arpeggio: false)), 9);
    }

    [Fact]
    public void TheWiggleIsTheColumnsLeftmostInk_PaddingPlusItsOwnWidth()
    {
        double plain = SpacingRules.CalculateLeftExtent(Triad(arpeggio: false));
        double rolled = SpacingRules.CalculateLeftExtent(Triad(arpeggio: true));
        Assert.Equal(ArpeggioEngraver.Padding + ArpeggioEngraver.WiggleWidth, rolled - plain, 9);
        // The wiggle is not a support: the support's reach is unchanged by it.
        Assert.Equal(0.0, SpacingRules.ChordSupportLeftReach(Triad(arpeggio: true)), 9);
    }

    [Fact]
    public void TheBracketReachesItsOwnWidthInstead()
    {
        double plain = SpacingRules.CalculateLeftExtent(Triad(arpeggio: false));
        double bracketed = SpacingRules.CalculateLeftExtent(
            Triad(arpeggio: false) with { HasArpeggioBracket = true });
        Assert.Equal(ArpeggioEngraver.Padding + ArpeggioEngraver.BracketWidth, bracketed - plain, 9);
    }

    [Fact]
    public void TheWiggleStandsPaddingLeftOfTheAccidental_NotOfTheHead()
    {
        var sharp = Triad(arpeggio: false, lowestAccidental: "sharp");
        double support = SpacingRules.ChordSupportLeftReach(sharp);
        // The sharp reaches further left than any head does.
        Assert.True(support > 1.0, $"the sharp's reach is {support:F6}");
        double rolled = SpacingRules.CalculateLeftExtent(Triad(arpeggio: true, "sharp"));
        Assert.Equal(support + ArpeggioEngraver.Padding + ArpeggioEngraver.WiggleWidth, rolled, 9);
    }

    [Fact]
    public void TheLeftmostGrobsExtraSpacingWidthIsTheWiggles_NotTheAccidentals()
    {
        var sharp = Triad(arpeggio: false, lowestAccidental: "sharp");
        var rolledSharp = Triad(arpeggio: true, lowestAccidental: "sharp");
        Assert.Equal(SpacingRules.AccidentalExtraSpacingWidthLeft,
            SpacingRules.MusicalColumnLeftReach(sharp) - SpacingRules.CalculateLeftExtent(sharp), 9);
        // The arpeggio declares no extra-spacing-width, so the default 0.1 — and it is the
        // leftmost grob, so the accidental's 0.2 no longer enters.
        Assert.Equal(SpacingRules.DefaultExtraSpacingWidth,
            SpacingRules.MusicalColumnLeftReach(rolledSharp)
                - SpacingRules.CalculateLeftExtent(rolledSharp), 9);
        Assert.Equal(SpacingRules.DefaultExtraSpacingWidth + SpacingRules.DefaultExtraSpacingWidth,
            SpacingRules.GetBarlineToItemMinimum(rolledSharp), 9);
    }

    [Fact]
    public void ArpeggioEngraverClearsTheAccidental()
    {
        // One measure, the rolled sharp chord at item 0, x = 3.0 in a measure at 10.0.
        var chord = Triad(arpeggio: true, lowestAccidental: "sharp");
        var measure = new Measure(ImmutableArray.Create<MusicItem>(chord),
            BarlineType.None, BarlineType.Single, null, 0, 0);
        var ml = ImmutableArray.Create(new MeasureLayout(0, 10.0, 20.0,
            ImmutableArray.Create(new ItemLayout(0, 3.0, 2.0))));
        var systems = ImmutableArray.Create(new SystemLayout(0, 10.0, 200.0, 5.0, ml));
        var arpeggios = ImmutableArray.Create(new ArpeggioItem(0, 0, -6, -2, 0));

        var layout = Assert.Single(ArpeggioEngraver.Calculate(
            arpeggios, systems, ImmutableArray.Create(measure)));

        double support = SpacingRules.ChordSupportLeftReach(chord);
        double expected = 13.0 - support - ArpeggioEngraver.Padding - ArpeggioEngraver.WiggleWidth;
        Assert.Equal(expected, layout.X, 9);
        // ...and the wiggle's right edge is `padding` off the accidental's ink left.
        Assert.Equal(ArpeggioEngraver.Padding,
            (13.0 - support) - (layout.X + ArpeggioEngraver.WiggleWidth), 9);
    }
}
