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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Every drum this table carries, pinned to LilyPond's own three tables — the name, the
/// staff position and notehead (<c>drums-style</c>), and the GM key (<c>midiDrumPitches</c>).
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/drumpitch-init.ly — drumPitchNames (names and aliases),
/// midiDrumPitches (GM keys) and the drums-style alist (notehead articulation position).
/// <para>
/// ⚠️ Read from LilyPond 2.24.4 and CHECKED IN here, because a test that read the install
/// would pass or fail on whether LilyPond is on the machine. The rows below are the audit's
/// output, not hand-typed.
/// </para>
/// <para>
/// ⚠️⚠️ TAKE THE ROWS FROM <c>drums-style</c> AND NOTHING ELSE. The file holds several style
/// tables — <c>agostini-drums-style</c>, <c>weinberg-drums-style</c>, <c>congas-style</c>,
/// <c>percussion-style</c> … — and the same name sits in more than one with DIFFERENT
/// positions (hihat is 3 in drums-style and 5 in the other two). A file-wide scan reads
/// whichever table comes last: that is exactly how this audit's first run reported 22
/// position mismatches that did not exist (2026-09-13).
/// </para>
/// <para>
/// ★ What the audit found when it was reading the right table: no name here is absent from
/// LilyPond (the one that was — <c>splashhihat</c> / <c>hhs</c> — was removed the leg
/// before), and not one row disagrees. What LilyPond has and this table does not is the
/// Latin and accessory percussion — triangle, guiro, claves, maracas, cabasa, agogo,
/// bongos, congas, timbales, woodblocks, whistles, cuica, the side sticks — about thirty
/// instruments with their abbreviations, an open scope decision recorded in HANDOFF §1.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class DrumTableMatchesLilyPondTests
{
    [Theory]
    [InlineData("acousticbassdrum", -3, "()", "#f", 35)]
    [InlineData("acousticsnare", 1, "()", "#f", 38)]
    [InlineData("bassdrum", -3, "()", "#f", 36)]
    [InlineData("chinesecymbal", 5, "mensural", "#f", 52)]
    [InlineData("closedhihat", 3, "cross", "stopped", 42)]
    [InlineData("cowbell", 5, "triangle", "#f", 56)]
    [InlineData("crashcymbal", 5, "xcircle", "#f", 49)]
    [InlineData("crashcymbala", 5, "xcircle", "#f", 49)]
    [InlineData("crashcymbalb", 5, "cross", "#f", 57)]
    [InlineData("electricsnare", 1, "()", "#f", 40)]
    [InlineData("halfopenhihat", 3, "xcircle", "#f", 46)]
    [InlineData("handclap", 1, "triangle", "#f", 39)]
    [InlineData("highfloortom", -2, "()", "#f", 43)]
    [InlineData("hightom", 4, "()", "#f", 50)]
    [InlineData("hihat", 3, "cross", "#f", 42)]
    [InlineData("himidtom", 2, "()", "#f", 48)]
    [InlineData("lowfloortom", -4, "()", "#f", 41)]
    [InlineData("lowmidtom", 0, "()", "#f", 47)]
    [InlineData("lowtom", -1, "()", "#f", 45)]
    [InlineData("openhihat", 3, "cross", "open", 46)]
    [InlineData("pedalhihat", -5, "cross", "#f", 44)]
    [InlineData("ridebell", 5, "()", "#f", 53)]
    [InlineData("ridecymbal", 5, "cross", "#f", 51)]
    [InlineData("ridecymbala", 5, "cross", "#f", 51)]
    [InlineData("ridecymbalb", 5, "cross", "#f", 59)]
    [InlineData("sidestick", 1, "cross", "#f", 37)]
    [InlineData("snare", 1, "()", "#f", 38)]
    [InlineData("splashcymbal", 5, "diamond", "#f", 55)]
    [InlineData("vibraslap", 4, "diamond", "#f", 58)]
    public void EveryPortedDrumCarriesLilyPondsRow(
        string name, int position, string lpNotehead, string lpArticulation, int gmKey)
    {
        Assert.True(DrumNameRegistry.TryGet(name, out var info), name + " left the table");

        Assert.Equal(position, info.StaffPosition);
        Assert.Equal(gmKey, info.GmKey);
        Assert.Equal(lpArticulation == "#f" ? null : lpArticulation, info.Mark);

        // LilyPond's notehead words, mapped to the font's styles. ⚠️ `mensural` is the one
        // SUBSTITUTION and it is deliberate — the registry's own comment says so ("LP uses a
        // mensural head here; the closest style in the font set"), so the audit must not
        // read it as drift.
        var expected = lpNotehead switch
        {
            "()" => NoteheadStyle.Default,
            "cross" => NoteheadStyle.Cross,
            "xcircle" => NoteheadStyle.XCircle,
            "diamond" => NoteheadStyle.Diamond,
            "triangle" => NoteheadStyle.Triangle,
            "mensural" => NoteheadStyle.Cross,     // documented substitution
            _ => throw new Xunit.Sdk.XunitException("unmapped LilyPond notehead: " + lpNotehead),
        };
        Assert.Equal(expected, info.Notehead);
    }

    [Fact]
    public void TheOneDrumLilyPondKeepsElsewhere_IsOursToPlace()
    {
        // `tambourine` is not in LilyPond's drum-KIT table at all — it sits in
        // percussion-style — so its staff position here is Lily#'s own choice and the audit
        // above cannot pin it. Its GM key still comes from LilyPond's midiDrumPitches.
        Assert.True(DrumNameRegistry.TryGet("tambourine", out var info));
        Assert.Equal(54, info.GmKey);
        Assert.Equal(0, info.StaffPosition);
    }

    [Fact]
    public void TheTableCarriesTheKitAndSaysSo()
    {
        // A count, so that adding a drum is a deliberate edit here too: 30 canonical
        // instruments (the kit) and their LilyPond abbreviations.
        Assert.Equal(30, DrumNameRegistry.CanonicalEntries.Count());
        Assert.Equal(30, DrumNameRegistry.AliasEntries.Count());
    }
}
