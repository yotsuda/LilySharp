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
/// before), and not one row disagrees. What LilyPond had and this table did not was the
/// Latin and accessory percussion; the owner asked for all of it and it landed 2026-09-13,
/// so the theories below now come in four parts, one per LilyPond table a row can come
/// from — the KIT (<c>drums-style</c>), the hi/lo PAIRS (<c>bongos-</c>, <c>congas-</c>,
/// <c>timbales-style</c>), the ACCESSORIES (<c>percussion-style</c>), and the eight
/// LilyPond places nowhere at all.
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
    public void EveryKitDrumCarriesLilyPondsDrumsStyleRow(
        string name, int position, string lpNotehead, string lpArticulation, int gmKey)
        => AssertRow(name, position, lpNotehead, lpArticulation, gmKey);

    /// <summary>
    /// The hi/lo hand drums, whose ONLY placement in LilyPond is the pair table for their
    /// own instrument — <c>bongos-style</c>, <c>congas-style</c>, <c>timbales-style</c>.
    /// That is where the ±1 comes from: a pair placed on one line would be one row.
    /// </summary>
    [Theory]
    // bongos-style
    [InlineData("mutehibongo", 1, "()", "stopped", 60)]
    [InlineData("hibongo", 1, "()", "#f", 60)]
    [InlineData("openhibongo", 1, "()", "open", 60)]
    [InlineData("mutelobongo", -1, "()", "stopped", 61)]
    [InlineData("lobongo", -1, "()", "#f", 61)]
    [InlineData("openlobongo", -1, "()", "open", 61)]
    // congas-style. ⚠️ 62 twice is LilyPond's own midiDrumPitches, not a transcription slip:
    // General MIDI has one Mute Conga and LilyPond gives it to both hands.
    [InlineData("mutehiconga", 1, "()", "stopped", 62)]
    [InlineData("muteloconga", -1, "()", "stopped", 62)]
    [InlineData("openhiconga", 1, "()", "open", 63)]
    [InlineData("hiconga", 1, "()", "#f", 63)]
    [InlineData("openloconga", -1, "()", "open", 64)]
    [InlineData("loconga", -1, "()", "#f", 64)]
    // timbales-style — and the two side sticks, which live only here.
    [InlineData("hisidestick", 1, "cross", "#f", 37)]
    [InlineData("losidestick", -1, "cross", "#f", 37)]
    [InlineData("hitimbale", 1, "()", "#f", 65)]
    [InlineData("lotimbale", -1, "()", "#f", 66)]
    public void EveryPairDrumCarriesLilyPondsPairTableRow(
        string name, int position, string lpNotehead, string lpArticulation, int gmKey)
        => AssertRow(name, position, lpNotehead, lpArticulation, gmKey);

    /// <summary>
    /// The accessories, from <c>percussion-style</c> — every row of which is position 0,
    /// because that table is for a ONE-LINE staff.
    /// </summary>
    [Theory]
    [InlineData("opentriangle", 0, "cross", "open", 81)]
    [InlineData("mutetriangle", 0, "cross", "stopped", 80)]
    // ⚠️ Plain `triangle` is GM 81, the OPEN triangle — LilyPond's midiDrumPitches says so
    // (ly:make-pitch 1 4 DOUBLE-SHARP, the same key as opentriangle). It is not a typo for 80.
    [InlineData("triangle", 0, "cross", "#f", 81)]
    [InlineData("shortguiro", 0, "()", "staccato", 73)]
    [InlineData("longguiro", 0, "()", "tenuto", 74)]
    [InlineData("guiro", 0, "()", "#f", 74)]
    [InlineData("claves", 0, "()", "#f", 75)]
    // `tambourine` is in this table too, and always was at position 0 — what the audit used
    // to call "Lily#'s own choice" turns out to be LilyPond's row exactly.
    [InlineData("tambourine", 0, "()", "#f", 54)]
    [InlineData("cabasa", 0, "cross", "#f", 69)]
    [InlineData("maracas", 0, "()", "#f", 70)]
    public void EveryAccessoryCarriesLilyPondsPercussionStyleRow(
        string name, int position, string lpNotehead, string lpArticulation, int gmKey)
        => AssertRow(name, position, lpNotehead, lpArticulation, gmKey);

    /// <summary>
    /// The eight LilyPond gives a GM key and a name but NO row in any style table. They take
    /// the middle line and the default head — LilyPond's own fallback for an unplaced drum,
    /// and the only rows in this registry a LilyPond table cannot be asked about.
    /// </summary>
    [Theory]
    [InlineData("hiagogo", 67)]
    [InlineData("loagogo", 68)]
    [InlineData("shortwhistle", 71)]
    [InlineData("longwhistle", 72)]
    [InlineData("hiwoodblock", 76)]
    [InlineData("lowoodblock", 77)]
    [InlineData("mutecuica", 78)]
    [InlineData("opencuica", 79)]
    public void TheOnesLilyPondNeverPlaces_TakeTheMiddleLine(string name, int gmKey)
    {
        Assert.True(DrumNameRegistry.TryGet(name, out var info), name + " left the table");
        Assert.Equal(0, info.StaffPosition);
        Assert.Equal(NoteheadStyle.Default, info.Notehead);
        Assert.Null(info.Mark);
        Assert.Equal(gmKey, info.GmKey);
    }

    private static void AssertRow(
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
    public void LilyPondsOneDanglingName_IsNotHere()
    {
        // ★ LilyPond's drumPitchNames has 63 canonical names and 64 aliases, and the extra
        // alias is `tt`, which points at `tamtam` — a symbol that is in no style table, in
        // no midiDrumPitches row, and not in drumPitchNames' own canonical half either.
        // Porting it would have been porting a name with no pitch, no head and no place:
        // exactly what `hhs` was before it was removed. So neither spelling is here, and
        // this test is why that stays a decision rather than an omission.
        Assert.False(DrumNameRegistry.Contains("tt"));
        Assert.False(DrumNameRegistry.Contains("tamtam"));
    }

    [Theory]
    [InlineData("tri")]      // triangle
    [InlineData("cl")]       // claves
    [InlineData("mar")]      // maracas
    [InlineData("gui")]      // guiro
    [InlineData("cab")]      // cabasa
    public void EveryNewDrumNameIsTakenOutOfThePhraseNamespace_Loudly(string word)
    {
        // ★ MEASURED 2026-09-13, because the table just grew by 33 names and 33 aliases,
        // several of them short words a writer might well have called a phrase. A drum name
        // IS claimed out of the phrase namespace — Parser.Music.cs sends a bare identifier
        // to ParseDrumNote whenever DrumNameRegistry.Contains says so, on ANY part, without
        // asking whether it is a kit — but the writer is TOLD, at the declaration, by
        // LYS1030 (Parser.Directives.RejectUnreachablePhraseName), whose set is read from
        // this registry and so widened with it. So what the new names cost is thirty-three
        // more refused phrase names, each with a message naming the reason — not a book that
        // silently turns into a drum staff.
        var tree = SyntaxTree.Parse($"phrase {word} {{ c2 c2 | }}\n");

        var error = Assert.Single(tree.Diagnostics.Where(
            d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error));
        Assert.Equal("LYS1030", error.Code);
        Assert.Contains($"'{word}' is a drum-kit name", error.Message);
    }

    [Fact]
    public void TheTableCarriesAllOfLilyPondsPercussionAndSaysSo()
    {
        // A count, so that adding a drum is a deliberate edit here too: LilyPond's 63
        // canonical instruments, and 63 of its 64 abbreviations (see the dangling `tt`).
        Assert.Equal(63, DrumNameRegistry.CanonicalEntries.Count());
        Assert.Equal(63, DrumNameRegistry.AliasEntries.Count());

        // Every alias resolves — an abbreviation for a name that is not here would be a
        // spelling the popup offers and the parser then reads as nothing.
        foreach (var (alias, full) in DrumNameRegistry.AliasEntries)
        {
            Assert.True(DrumNameRegistry.TryGet(alias, out var info), alias + " resolves to nothing");
            Assert.Equal(full, info.FullName);
        }
    }
}
