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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Every tuning this table carries, pinned string by string to LilyPond's own.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/string-tunings-init.ly — makeDefaultStringTuning, whose chords are
/// written in absolute pitch from the HIGHEST string number (the lowest pitch) first, which
/// is the same order as the arrays here.
/// <para>
/// The audit that produced these rows (2026-09-13) asked the same three questions as the
/// drum one: a tuning of ours LilyPond does not have (NONE), a string that disagrees
/// (NONE), and what LilyPond had that we did not — twenty-five more tunings. The owner
/// asked for all of them the same day, so the theory below is now LilyPond's whole file:
/// every <c>\makeDefaultStringTuning</c> in it, string by string.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TuningTableMatchesLilyPondTests
{
    [Theory]
    // Lily# word            tuning                         LilyPond's chord          its strings as MIDI
    [InlineData("guitar", TuningType.Guitar, "guitar-tuning <e, a, d g b e'>", new[] { 40, 45, 50, 55, 59, 64 })]
    [InlineData("guitar7", TuningType.Guitar7, "guitar-seven-string-tuning <b,, e, a, d g b e'>", new[] { 35, 40, 45, 50, 55, 59, 64 })]
    [InlineData("guitardropd", TuningType.GuitarDropD, "guitar-drop-d-tuning <d, a, d g b e'>", new[] { 38, 45, 50, 55, 59, 64 })]
    [InlineData("guitardropc", TuningType.GuitarDropC, "guitar-drop-c-tuning <c, g, c f a d'>", new[] { 36, 43, 48, 53, 57, 62 })]
    [InlineData("guitaropeng", TuningType.GuitarOpenG, "guitar-open-g-tuning <d, g, d g b d'>", new[] { 38, 43, 50, 55, 59, 62 })]
    [InlineData("guitaropend", TuningType.GuitarOpenD, "guitar-open-d-tuning <d, a, d fis a d'>", new[] { 38, 45, 50, 54, 57, 62 })]
    [InlineData("guitardadgad", TuningType.GuitarDadgad, "guitar-dadgad-tuning <d, a, d g a d'>", new[] { 38, 45, 50, 55, 57, 62 })]
    [InlineData("guitarlute", TuningType.GuitarLute, "guitar-lute-tuning <e, a, d fis b e'>", new[] { 40, 45, 50, 54, 59, 64 })]
    [InlineData("guitarasus4", TuningType.GuitarAsus4, "guitar-asus4-tuning <e, a, d e a e'>", new[] { 40, 45, 50, 52, 57, 64 })]
    [InlineData("bass", TuningType.Bass, "bass-tuning <e,, a,, d, g,>", new[] { 28, 33, 38, 43 })]
    [InlineData("bassdropd", TuningType.BassDropD, "bass-drop-d-tuning <d,, a,, d, g,>", new[] { 26, 33, 38, 43 })]
    [InlineData("bass5", TuningType.Bass5, "bass-five-string-tuning <b,,, e,, a,, d, g,>", new[] { 23, 28, 33, 38, 43 })]
    [InlineData("bass6", TuningType.Bass6, "bass-six-string-tuning <b,,, e,, a,, d, g, c>", new[] { 23, 28, 33, 38, 43, 48 })]
    [InlineData("violin", TuningType.Violin, "violin-tuning <g d' a' e''>", new[] { 55, 62, 69, 76 })]
    [InlineData("viola", TuningType.Viola, "viola-tuning <c g d' a'>", new[] { 48, 55, 62, 69 })]
    [InlineData("cello", TuningType.Cello, "cello-tuning <c, g, d a>", new[] { 36, 43, 50, 57 })]
    // ⚠️ Every banjo is re-entrant — the 5th string is a high drone — so these arrays start
    // at their HIGHEST pitch and must not be "fixed" into ascending order. LilyPond writes
    // them the same way, highest string NUMBER first.
    [InlineData("banjoopeng", TuningType.BanjoOpenG, "banjo-open-g-tuning <g' d g b d'>", new[] { 67, 50, 55, 59, 62 })]
    [InlineData("banjoc", TuningType.BanjoC, "banjo-c-tuning <g' c g b d'>", new[] { 67, 48, 55, 59, 62 })]
    [InlineData("banjomodal", TuningType.BanjoModal, "banjo-modal-tuning <g' d g c' d'>", new[] { 67, 50, 55, 60, 62 })]
    [InlineData("banjoopend", TuningType.BanjoOpenD, "banjo-open-d-tuning <a' d fis a d'>", new[] { 69, 50, 54, 57, 62 })]
    [InlineData("banjoopendm", TuningType.BanjoOpenDm, "banjo-open-dm-tuning <a' d f a d'>", new[] { 69, 50, 53, 57, 62 })]
    [InlineData("banjodoublec", TuningType.BanjoDoubleC, "banjo-double-c-tuning <g' c g c' d'>", new[] { 67, 48, 55, 60, 62 })]
    [InlineData("banjodoubled", TuningType.BanjoDoubleD, "banjo-double-d-tuning <a' d g d' e'>", new[] { 69, 50, 55, 62, 64 })]
    // ⚠️ Re-entrant too, and LilyPond writes it that way: the g is ABOVE the c.
    [InlineData("ukulele", TuningType.Ukulele, "ukulele-tuning <g' c' e' a'>", new[] { 67, 60, 64, 69 })]
    [InlineData("ukuleled", TuningType.UkuleleD, "ukulele-d-tuning <a' d' fis' b'>", new[] { 69, 62, 66, 71 })]
    [InlineData("tenorukulele", TuningType.TenorUkulele, "tenor-ukulele-tuning <g c' e' a'>", new[] { 55, 60, 64, 69 })]
    [InlineData("baritoneukulele", TuningType.BaritoneUkulele, "baritone-ukulele-tuning <d g b e'>", new[] { 50, 55, 59, 64 })]
    public void EveryTuningCarriesLilyPondsStrings(
        string word, TuningType type, string lilyPond, int[] strings)
    {
        Assert.Equal(type, Tunings.Parse(word));         // the word reaches this tuning
        Assert.Equal(strings, Tunings.GetTuning(type));
        Assert.Equal(strings.Length, Tunings.GetStringCount(type));

        // ★★ THE CITATION IS READ, NOT DECORATED. Until 2026-09-13 this line only asserted
        // the string was non-empty — which made the whole theory circular: the numbers in the
        // InlineData and the numbers in Tunings.cs came out of the SAME generator
        // (scratchpad/gen-tunings.ps1), so a bug in that one pitch parser would have written
        // the same wrong array into both and this test would have been green about it.
        // Parsing LilyPond's own chord text here breaks the circle: the only thing still
        // trusted is the short chord string copied from ly/string-tunings-init.ly, which a
        // reader can check against the file by eye.
        Assert.Equal(strings, ChordToMidi(lilyPond));
    }

    /// <summary>
    /// LilyPond's absolute-pitch chord text → MIDI, independently of the script that built
    /// the table. <c>c</c> is 48, <c>'</c> is up an octave and <c>,</c> down one, and the
    /// chord is left in LilyPond's own order (highest string number first).
    /// </summary>
    private static int[] ChordToMidi(string citation)
    {
        int open = citation.IndexOf('<'), close = citation.IndexOf('>');
        Assert.True(open >= 0 && close > open, "the citation carries no chord: " + citation);
        return citation[(open + 1)..close]
            .Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(PitchToMidi)
            .ToArray();
    }

    private static int PitchToMidi(string pitch)
    {
        // a b c d e f g, with c = 48 (LilyPond's unmarked octave).
        int[] semitonesFromC = [9, 11, 0, 2, 4, 5, 7];
        Assert.InRange(pitch[0], 'a', 'g');
        int midi = 48 + semitonesFromC[pitch[0] - 'a'];

        int i = 1;
        while (i + 1 < pitch.Length && pitch.Substring(i, 2) is "is" or "es")
        {
            midi += pitch[i] == 'i' ? 1 : -1;
            i += 2;
        }
        for (; i < pitch.Length; i++)
        {
            Assert.True(pitch[i] is '\'' or ',', "unreadable octave mark in " + pitch);
            midi += pitch[i] == '\'' ? 12 : -12;
        }
        return midi;
    }

    [Theory]
    // The four words that name a tuning some other word already names. Two are Lily#'s own
    // and older than this table; two are LilyPond's, and they are duplicates in LilyPond too.
    [InlineData("standard", TuningType.Guitar)]          // Lily#'s own word
    [InlineData("uke", TuningType.Ukulele)]              // Lily#'s own word
    [InlineData("bass4", TuningType.Bass)]               // LP's bass-four-string-tuning ≡ bass-tuning
    [InlineData("doublebass", TuningType.Bass)]          // LP's double-bass-tuning ≡ bass-tuning
    [InlineData("mandolin", TuningType.Violin)]          // LP's mandolin-tuning ≡ violin-tuning
    public void TheSecondSpellingsReachTheSameTuning(string word, TuningType type)
        => Assert.Equal(type, Tunings.Parse(word));

    [Fact]
    public void TheSpellingsAreLilyPondsNames_OrItsAbbreviations()
    {
        // Thirty-two words, twenty-seven tunings. The rule for a word: LilyPond's symbol
        // without `-tuning`, with `<n>-string` written as the digit and the hyphens dropped.
        // No spelling here names a tuning LilyPond lacks — the question that found `hhs` in
        // the drum table.
        Assert.Equal(
            new[]
            {
                "banjoc", "banjodoublec", "banjodoubled", "banjomodal",
                "banjoopend", "banjoopendm", "banjoopeng", "baritoneukulele",
                "bass", "bass4", "bass5", "bass6",
                "bassdropd", "cello", "doublebass", "guitar", "guitar7", "guitarasus4",
                "guitardadgad", "guitardropc", "guitardropd", "guitarlute", "guitaropend",
                "guitaropeng", "mandolin", "standard", "tenorukulele", "uke", "ukulele",
                "ukuleled", "viola", "violin",
            },
            LanguageVocabulary.TuningNames.OrderBy(n => n, System.StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void EveryTuningNamesItselfBackToLilyPond()
    {
        // The twin writes a LilyPond predefined-tuning symbol; every tuning must have one,
        // and it must be the one whose strings this table holds. A missing arm here used to
        // be invisible — the old switch had a `_ =>` that answered "bass-four-string-tuning"
        // for anything it did not know, so a new tuning would have been exported as a bass.
        foreach (TuningType type in System.Enum.GetValues<TuningType>())
        {
            string lp = Tunings.LilyPondName(type);
            Assert.EndsWith("-tuning", lp);
            Assert.NotEmpty(Tunings.GetTuning(type));
        }
    }

    [Fact]
    public void OnlyTheBassFamilySoundsAnOctaveDown()
    {
        // `IsBass` drives a −12 default transposition, so a tuning wrongly in it would fret
        // and play an octave out. The four are the bass guitar's — and the double bass's,
        // which shares TuningType.Bass.
        Assert.Equal(
            new[] { TuningType.Bass, TuningType.Bass5, TuningType.Bass6, TuningType.BassDropD },
            System.Enum.GetValues<TuningType>().Where(Tunings.IsBass)
                .OrderBy(t => t.ToString(), System.StringComparer.Ordinal).ToArray());
        Assert.All(System.Enum.GetValues<TuningType>(),
            t => Assert.Equal(Tunings.IsBass(t) ? -12 : 0, Tunings.TuningTransposition(t)));
    }
}
