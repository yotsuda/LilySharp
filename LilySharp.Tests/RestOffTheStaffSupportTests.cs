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

using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A rest the rest collision moved off the staff — a grace rest standing over its main note, a
/// voice's rest pushed clear of the other voice — is ink the marks beside the staff answer to:
/// the staff's profile carries it where it was moved to (SkylineBuilder's grace seed), and the
/// marks that side off their own columns keep their OWN padding from it
/// (DynamicEngraver.MergeColumnSupport: the dynamics' 0.6, the ottava's and the trill's 0.5).
/// LILYPOND-REF: lily/dynamic-align-engraver.cc:108-111 acknowledge_rhythmic_head;
/// LILYPOND-REF: lily/ottava-engraver.cc:139-150 acknowledge_note_column.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p1237/ott — probe.ps1 writes each one-bar book, its
/// <c>lysc ly</c> twin and both sides' Y about the staff middle; raggedRight). Until session
/// 1237 the grace rest's seed stood at the rest's unmoved position, inside the staff, so the
/// first four rows read their quiet heights (4.05, 3.55, 3.55 and 2.51) with the mark printed
/// through the rest; the rows over a voice's rests read 4.58, 5.04, -5.80 and 4.60, the
/// outside-staff pass's 0.46 where the mark's own padding belongs.
/// </remarks>
[Trait("Category", "Unit")]
public class RestOffTheStaffSupportTests
{
    private static RenderedGeometry Book(string bar) => RenderedGeometry.Render(
        "octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\nsection A { m {\n"
        + bar + "\n} }\nform { A }\nscore { staff m }\n");

    // The grace rest stands at +5 over the e'' (ink top 5.82 over the middle line).
    [Fact]
    public void AnOttavaStartedOnAGraceRest_ClearsTheRest()
        => Assert.Equal(6.9369, Book("e''4 grace { r16@8va } e''4 f''4@!8va e''4 |").OttavaLineAboveStaff(), 0.005);

    [Fact]
    public void ATrillStartedOnAGraceRest_KeepsItsOwnPaddingFromTheRest()
        => Assert.Equal(7.3200, Book("e''4 grace { r16@trillSpan } e''4 f''4@!trillSpan e''4 |").TrillLineAboveStaff(), 0.005);

    // The rest is not the trill's column here: the outside-staff pass clears it.
    [Fact]
    public void ATrillOnTheNoteAfterAGraceRest_ClearsTheRest()
        => Assert.Equal(7.1760, Book("e''4 grace { r16 } e''4@trillSpan f''4@!trillSpan e''4 |").TrillLineAboveStaff(), 0.005);

    [Fact]
    public void AFermataOnTheNoteAfterAGraceRest_ClearsTheRest()
        => Assert.Equal(6.1601, Book("e''4 grace { r16 } e''4@fermata e''4 e''4 |").FermataInkEdgeAboveStaff(), 0.005);

    // An upper voice's quarter rests stand at +2 (ink top 3.5624), a lower voice's at -3 (ink
    // bottom -4.25).
    [Fact]
    public void AnOttavaOverAVoicesRests_KeepsItsOwnPaddingFromThem()
        => Assert.Equal(4.6145, Book("voice { r4@8va r r@!8va r } { c'4 d' e' f' } |").OttavaLineAboveStaff(), 0.005);

    [Fact]
    public void ATrillOverAVoicesRests_KeepsItsOwnPaddingFromThem()
        => Assert.Equal(5.0840, Book("voice { r4@trillSpan r r@!trillSpan r } { c'4 d' e' f' } |").TrillLineAboveStaff(), 0.005);

    [Theory]
    // A level under a lower voice's rest, and over an upper voice's.
    [InlineData("voice { c''4 c'' c'' c'' } { r4@p r r r } |", -5.9306)]
    [InlineData("voice { r4 r r@mf.up r } { c'4 d' e' f' } |", 4.7413)]
    // A level on the line a hairpin over the rests draws (the DynamicLineSpanner's one offset).
    [InlineData("voice { r4@p@cresc.up r r r@f } { c'4 d' e' f' } |", 4.7151)]
    public void ADynamicOnAVoicesRest_KeepsItsOwnPaddingFromIt(string bar, double baseline)
        => Assert.Equal(baseline, Book(bar).DynamicBaselineAboveStaffLine(0, 2), 0.005);
}
