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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The TAB technique letters — <c>@tap</c> (T), <c>@hammeron</c> (H), <c>@pulloff</c> (P)
/// and <c>@pluck</c>'s finger letter — are TEXT, and the room reserved for one has to be
/// the ink that gets drawn.
/// </summary>
/// <remarks>
/// <para>
/// Reported 2026-08-28: "the T of @tap overlaps the note". It did, by 0.383 ss, and only
/// on the side where the letter lands BELOW its note. The letters fell to
/// <c>GetGlyphBBox</c>'s generic half-space fallback — a symmetric box around the anchor —
/// while the renderer anchors the letter's BASELINE there and its ink rises 1.083 ss above
/// it (TeX Gyre Schola's cap height 0.7220 em at the drawn size 1.5). A symmetric box
/// mirrors to itself, so above the note, where the ink grows AWAY, nothing showed; below,
/// it grew straight into the notehead.
/// </para>
/// <para>
/// ⚠️⚠️ NOTHING IN THE TREE OBSERVED THESE LETTERS, and that is the whole reason the defect
/// lived: LilyPond has no grob for them (a player writes them as markup), so there is no LP
/// geometry and no ledger point can exist; and on 2026-08-28 <b>not one</b> of the 572
/// tracked <c>.lys</c> books wrote <c>@tap</c>, <c>@hammeron</c>, <c>@pulloff</c> or
/// <c>@pluck</c> — no snapshot, no sweep, no fixture. This file and
/// <c>Fixtures/test/tab-technique-letters.lys</c> are that missing observer. What replaces
/// the ledger is an IDENTITY: the box the layout reserves IS the ink the renderer draws,
/// because both read <c>ArticulationEngraver.TabTechnique*</c>.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TabTechniqueLetterTests
{
    private static string Render(string source) =>
        SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    private static string Book(string note, string mark) =>
        "octave absolute\ntime 4/4\n"
        + "part mel { clef treble }\n"
        + "section Main { mel { " + note + mark + " r4 r r | } }\n"
        + "form main { ~Main }\n"
        + "score main { staff mel }\n";

    /// <summary>The black notehead's centre Y in the drawn (Y-down) frame.</summary>
    private static double NoteheadY(string svg)
    {
        var m = Regex.Match(svg, "<text[^>]*y=\"(?<y>[-0-9.]+)\"[^>]*></text>");
        Assert.True(m.Success, "the probe drew no black notehead");
        return double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>The technique letter's BASELINE Y in the drawn frame — which is what
    /// <c>DrawText</c> anchors, and the thing the old box did not know it was.</summary>
    private static double LetterBaselineY(string svg, string letter)
    {
        var m = Regex.Match(svg, "<text[^>]*y=\"(?<y>[-0-9.]+)\"[^>]*>" + letter + "</text>");
        Assert.True(m.Success, $"the probe drew no \"{letter}\"");
        return double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>The drawn letter's ink about its baseline, asked the way the DRAW asks —
    /// same face, same size, same style. Up-positive.</summary>
    private static (double Bottom, double Top) LetterInk(string letter) =>
        ScoreTextMetrics.Bundled.Ink(
            letter, ArticulationEngraver.TabTechniqueFontSize, TextRole.TabTechnique,
            ArticulationEngraver.TabTechniqueFontStyle);

    // Half the notehead's own height, the support the script is placed against.
    private const double HeadHalf = 0.55;

    [Theory]
    // Low notes take the letter BELOW — the reported side.
    [InlineData("c4", "@tap", "T")]
    [InlineData("e4", "@tap", "T")]
    [InlineData("g4", "@tap", "T")]
    [InlineData("c4", "@hammeron", "H")]
    [InlineData("c4", "@pulloff", "P")]
    // …and high ones ABOVE, where the same box is read the other way round.
    [InlineData("b4", "@tap", "T")]
    [InlineData("e'4", "@tap", "T")]
    [InlineData("g'4", "@tap", "T")]
    public void TheDrawnLetter_DoesNotTouchTheNotehead(string note, string mark, string letter)
    {
        string svg = Render(Book(note, mark));
        double head = NoteheadY(svg);
        double baseline = LetterBaselineY(svg, letter);
        var ink = LetterInk(letter);

        // Y grows DOWNWARD in the drawn frame, so the ink's top edge is baseline − Top.
        double inkTop = baseline - ink.Top;
        double inkBottom = baseline - ink.Bottom;

        double clearance = baseline > head
            ? inkTop - (head + HeadHalf)      // letter below: its TOP faces the head
            : (head - HeadHalf) - inkBottom;  // letter above: its BOTTOM faces the head

        Assert.True(clearance > 0,
            $"{note}{mark}: the drawn \"{letter}\" reaches {-clearance:F3} ss into the "
            + $"notehead (head y={head:F3}, baseline y={baseline:F3}, "
            + $"ink=[{inkTop:F3},{inkBottom:F3}])");
    }

    [Fact]
    public void TheLettersInkIsNotMirroredBySide()
    {
        // The invariant the half-space fallback could not express, and the one that made
        // the defect invisible above: every OTHER script in the engraver has an above form
        // and a below form because the GLYPH is mirrored. A letter is drawn the same way up
        // on both sides, so its reach TOWARD the note is its baseline on one side and its
        // cap height on the other — never the same number.
        var ink = ArticulationEngraver.TabTechniqueInkBox(ScoreTextMetrics.Bundled, "T");
        Assert.Equal(0.0, -ink.Bottom, 9);              // above: the baseline faces the note
        Assert.True(ink.Top > 1.0, $"cap height {ink.Top:F4} — expected about 1.083");
    }

    [Fact]
    public void ThePlacementSpendsTheLettersOwnCapHeight()
    {
        // The repair in one reading: BELOW, the baseline has to sit a full cap height
        // FURTHER from the note than it does ABOVE, because that is the ink that has to
        // fit in between. Before the repair both sides used the same 0.5 and the below
        // side was short by cap − 0.5 = 0.583.
        double capHeight = LetterInk("T").Top;
        double headAbove = NoteheadY(Render(Book("g'4", "@tap")));
        double baseAbove = LetterBaselineY(Render(Book("g'4", "@tap")), "T");
        double headBelow = NoteheadY(Render(Book("c4", "@tap")));
        double baseBelow = LetterBaselineY(Render(Book("c4", "@tap")), "T");

        double gapAbove = (headAbove - HeadHalf) - baseAbove;   // head ink top → baseline
        double gapBelow = baseBelow - (headBelow + HeadHalf);   // head ink bottom → baseline

        Assert.True(gapBelow - gapAbove > capHeight - 0.001,
            $"below spends {gapBelow:F3} and above {gapAbove:F3}; the difference "
            + $"{gapBelow - gapAbove:F3} must be at least the cap height {capHeight:F3}");
    }

    [Fact]
    public void ALetterWithADescender_ReachesFurtherWhenItIsAbove()
    {
        // The other half of "not mirrored", and the reason the box is asked per LETTER and
        // not per type: @pluck prints a lowercase finger letter, and `p` hangs below its
        // baseline. Above a note that descender is what faces the head; above `i` there is
        // nothing below the baseline at all.
        var p = ArticulationEngraver.TabTechniqueInkBox(ScoreTextMetrics.Bundled, "p");
        var i = ArticulationEngraver.TabTechniqueInkBox(ScoreTextMetrics.Bundled, "i");
        // Measured in the bundled face: "p" hangs 0.303 ss below its baseline where the
        // italic "i" only overshoots by 0.021 — an order of magnitude, not a rounding.
        Assert.True(-p.Bottom > 0.25, $"\"p\" descender {-p.Bottom:F4} — expected about 0.303");
        Assert.True(-i.Bottom < 0.05, $"\"i\" dips {-i.Bottom:F4} — expected an overshoot only");
        Assert.True(-p.Bottom > 5 * -i.Bottom, "a descender is not an italic overshoot");
        Assert.True(p.Top < i.Top, "\"p\" is an x-height letter and \"i\" carries a dot");
    }
}
