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
using System.Text.RegularExpressions;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A half-tie cites the <c>@</c> that wrote it. Laissez-vibrer and repeat ties are the
/// THIRD bow family: a tie is written <c>~</c> and a slur <c>( )</c>, but these are drawn
/// by an ANNOTATION, so the rule the reader set for the other two — a bow names the
/// characters that wrote it — resolves to the annotation's <c>@</c>.
/// </summary>
/// <remarks>
/// Until 2026-08-30 no half-tie carried a source offset at all: of the 56 bow-shaped
/// <c>&lt;path&gt;</c> elements in the tracked snapshots, closing HANDOFF §2 U10 gave 55 an
/// address and left exactly one — the l.v. of <c>test/lv-meterchange</c> — unaddressed,
/// because <c>DrawTieVariants</c> was the one bow drawer outside every
/// <c>IDrawingContext.Source</c> scope. That count is what opened §2 U11.
/// <para>
/// ⚠️ THE OFFSET IS THE ANNOTATION'S, NOT THE NOTE'S, and that is the whole test.
/// <c>TieVariantLayout</c> already carried a <c>SourcePosition</c> before this — but it
/// held the HOST NOTE's address, so simply wrapping the drawer would have passed every
/// "the bow has an address" assertion while lighting the note head, which is the side the
/// reader's slur decision rejected. Every case below therefore pins the <c>@</c>'s index
/// AND asserts the note's index is not cited.
/// </para>
/// <para>
/// ⚠️ NO <c>data-alt</c> HERE, unlike a slur's: a slur is written at two places and needs
/// an alias, but a half-tie is written at exactly one, so the primary address is the whole
/// answer and these renders need not be interactive.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class SemiTieSourcePositionTests
{
    private static string Book(string music) =>
        $"part bass\nsection A {{ bass {{ {music} }} }}\nform main {{ ~A }}\n"
        + "score main { staff bass }\n";

    private static string Render(string book) => LilySharp.Core.Svg.SvgGenerator.Generate(
        SyntaxTree.Parse(book),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>The offset of <paramref name="needle"/>'s <paramref name="nth"/>
    /// occurrence INSIDE the music, never in the surrounding book — the wrapper carries a
    /// <c>~</c> of its own (the phrase reference <c>~A</c>).</summary>
    private static int OffsetInMusic(string music, string needle, int nth = 0)
    {
        string book = Book(music);
        int bodyStart = book.IndexOf(music, System.StringComparison.Ordinal);
        Assert.True(bodyStart > 0, "the wrapper must contain the music verbatim");
        int at = -1;
        for (int i = 0; i <= nth; i++)
            at = music.IndexOf(needle, at + 1, System.StringComparison.Ordinal);
        Assert.True(at >= 0, $"the music must contain '{needle}' #{nth}");
        return bodyStart + at;
    }

    /// <summary>The primary address of every <c>&lt;path&gt;</c> that has one. In these
    /// books the half-ties are the only paths, so the list IS the bows.</summary>
    private static System.Collections.Generic.List<int> BowAddresses(string svg)
        => Regex.Matches(svg, "<path[^>]*>")
            .Select(m => Regex.Match(m.Value, "data-pos=\"([0-9]+)\""))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();

    [Theory]
    [InlineData("c4@laissezVibrer d4 e4 f4 |", "@laissezVibrer")]
    [InlineData("c4@repeatTie d4 e4 f4 |", "@repeatTie")]
    // The forced side is a qualifier ON the annotation, so the address does not move.
    [InlineData("c4@laissezVibrer.up d4 e4 f4 |", "@laissezVibrer")]
    public void AHalfTie_CitesTheAtSignThatWroteIt(string music, string annotation)
    {
        int at = OffsetInMusic(music, annotation);
        int note = OffsetInMusic(music, "c4");
        Assert.NotEqual(note, at);      // the two addresses must be distinguishable

        var bows = BowAddresses(Render(Book(music)));
        Assert.Equal(new[] { at }, bows);
        Assert.DoesNotContain(note, bows);
    }

    /// <summary>
    /// A chord-level annotation half-ties EVERY member from ONE <c>@</c>
    /// (LP: <c>acknowledge_note_head</c> uses the heard event for all heads), so the three
    /// drawn bows share one address — the same shape a chord's <c>~</c> ties have.
    /// </summary>
    [Fact]
    public void AChordLevelAnnotation_GivesEveryHeadTheSameOneAddress()
    {
        const string music = "<c e g>4@laissezVibrer c4 c4 c4 |";
        int at = OffsetInMusic(music, "@laissezVibrer");
        int chord = OffsetInMusic(music, "<");

        var bows = BowAddresses(Render(Book(music)));
        Assert.Equal(new[] { at, at, at }, bows);
        Assert.DoesNotContain(chord, bows);
    }

    /// <summary>
    /// A member-level annotation half-ties just its own head and cites its own <c>@</c> —
    /// not the chord's <c>&lt;</c>, and not the member's PITCH token, which is the address
    /// the head itself carries. This is the case a per-item lookup cannot express, and the
    /// reason <c>SharedRenderer.ResolveSemiTies</c> pairs bows to ties by order.
    /// </summary>
    [Fact]
    public void AMemberLevelAnnotation_CitesItsOwnAtSign_NotTheChordNorThePitch()
    {
        const string music = "<c@laissezVibrer e g>4 c4 c4 c4 |";
        int at = OffsetInMusic(music, "@laissezVibrer");
        int chord = OffsetInMusic(music, "<");
        int pitch = chord + 1;          // the member's own `c`

        var bows = BowAddresses(Render(Book(music)));
        Assert.Equal(new[] { at }, bows);
        Assert.DoesNotContain(chord, bows);
        Assert.DoesNotContain(pitch, bows);
    }

    /// <summary>
    /// When BOTH levels are written, the chord's event wins and every bow names ITS
    /// <c>@</c> — the same precedence the forced curve side already uses (the engraver
    /// reads the heard event before the articulation). Without this the two offsets could
    /// be swapped and each single-level case above would still pass.
    /// </summary>
    [Fact]
    public void AChordLevelAnnotation_WinsOverAMemberLevelOne()
    {
        const string music = "<c@laissezVibrer e g>4@laissezVibrer c4 c4 c4 |";
        int member = OffsetInMusic(music, "@laissezVibrer", 0);
        int chordWide = OffsetInMusic(music, "@laissezVibrer", 1);
        Assert.True(member < chordWide);

        var bows = BowAddresses(Render(Book(music)));
        Assert.Equal(new[] { chordWide, chordWide, chordWide }, bows);
        Assert.DoesNotContain(member, bows);
    }

    /// <summary>
    /// The control the cases above need: the same books with the annotation removed draw
    /// no addressed path at all. Without it, "the half-tie has an address" could be
    /// satisfied by stamping every path in the document.
    /// </summary>
    [Fact]
    public void WithoutTheAnnotation_NoPathCarriesAnAddress()
    {
        Assert.Empty(BowAddresses(Render(Book("c4 d4 e4 f4 |"))));
        Assert.Empty(BowAddresses(Render(Book("<c e g>4 c4 c4 c4 |"))));
    }
}
