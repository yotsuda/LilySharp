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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What a tuplet's body may direct: a <c>key</c> and a <c>tempo</c> stand at the member's moment, as
/// a <c>clef</c> does; a <c>time</c> and a <c>partial</c> are errors (LYS1065, owner's decision
/// 2026-11-05). And of two voices' clefs at one moment the staff draws one, the last heard.
/// </summary>
[Trait("Category", "Unit")]
public class DirectivesInATupletTests
{
    private static string Book(string music)
        => "octave absolute\ntime 4/4\npart p { clef treble }\nsection A { p { " + music + " } }\nform { A }\nscore { staff p }\n";

    private static MultiStaffScore Score(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
    }

    private static Staff Staff(string music) => Score(music).StaffGroups.Single().Staves.Single();

    private static T[] All<T>(Staff staff) where T : MusicItem
        => staff.Voices.SelectMany(v => v.Measures).SelectMany(m => m.Items).OfType<T>().ToArray();

    private static string[] Meter(string music)
        => SemanticValidation.Run(SyntaxTree.Parse(Book(music)))
            .Where(d => d.Code == DiagnosticCodes.MeterInTuplet).Select(d => d.Message).ToArray();

    [Theory]
    [InlineData("c'2 tuplet 3/2 { key g major d'8 e' f' } g'4 |")]
    [InlineData("c'2 tuplet 3/2 { d'8 key g major e' f' } g'4 |")]
    [InlineData("c'2 tuplet 3/2 { tuplet 3/2 { key g major d'16 e' f' } e'8 f' } g'4 |")]
    public void AKeyInTheBody_IsDrawn(string music)
        => Assert.Equal(1, Assert.Single(All<KeySignatureChangeItem>(Staff(music))).NewKey.Sharps);

    [Fact]
    public void AKeyInTheBody_RulesTheNotesAfterIt()
    {
        // In g major an f needs its natural, once in the bar.
        var notes = All<NoteItem>(Staff("c'2 tuplet 3/2 { key g major d'8 e' f' } f'4 |"));
        Assert.Equal("natural", notes[3].Accidental);
        Assert.Null(notes[4].Accidental);
    }

    [Fact]
    public void AKeyOpeningTheBody_StandsBeforeTheBracket()
    {
        var score = Score("c'2 tuplet 3/2 { key g major d'8 e' f' } g'4 |");
        var items = score.StaffGroups.Single().Staves.Single().Voices[0].Measures[0].Items;
        Assert.IsType<NoteItem>(items[Assert.Single(score.TupletBrackets).StartNoteIndex]);
    }

    [Fact]
    public void ATempoInTheBody_IsDrawn_AtItsMembersMoment()
    {
        var mark = Assert.Single(Score("c'2 tuplet 3/2 { d'8 tempo 4 = 90 e' f' } g'4 |").MusicMarks,
            m => m.Type == MusicMarkType.Tempo);
        Assert.Equal("90", mark.Text);
        Assert.Equal(new Fraction(1, 2) + new Fraction(1, 12), mark.AnchorTiming);
    }

    [Theory]
    [InlineData("tuplet 3/2 { time 3/4 c'8 d' e' } c'4 c' |", "time")]
    [InlineData("c'2 tuplet 3/2 { d'8 time 3/4 e' f' } g'4 |", "time")]
    [InlineData("c'2 tuplet 3/2 { tuplet 3/2 { time 3/4 d'16 e' f' } e'8 f' } g'4 |", "time")]
    [InlineData("tuplet 3/2 { partial 4 c'8 d' e' } c'1 |", "partial")]
    public void AMeterInTheBody_IsAnError_NamingItsWord(string music, string word)
        => Assert.Contains($"'{word}'", Assert.Single(Meter(music)));

    [Theory]
    [InlineData("time 3/4 tuplet 3/2 { c'8 d' e' } c'4 c' |")]
    [InlineData("partial 4 tuplet 3/2 { c'8 d' e' } | c'1 |")]
    public void AMeterBeforeTheTuplet_IsNoError(string music) => Assert.Empty(Meter(music));

    [Fact]
    public void TwoVoicesDifferentClefs_DrawTheLastHeard_AndEveryHeadStandsInIt()
    {
        var staff = Staff("voice { c'4 clef bass c' c' c' | } { c'4 clef alto c' c' c' | }");
        Assert.Equal(ClefType.Alto, Assert.Single(All<ClefChangeItem>(staff)).NewClef);
        Assert.Equal(All<NoteItem>(staff).Skip(1).Take(3).Select(n => n.StaffPosition),
            Enumerable.Repeat(0, 3));
    }

    [Fact]
    public void TwoVoicesOneClef_DrawOne()
        => Assert.Equal(ClefType.Bass, Assert.Single(All<ClefChangeItem>(
            Staff("voice { c'4 clef bass c' c' c' | } { c'4 clef bass c' c' c' | }"))).NewClef);

    [Fact]
    public void ALowerVoiceBackToTheClefInForce_DrawsNone()
        => Assert.Empty(All<ClefChangeItem>(Staff("voice { c'4 clef bass c' c' c' | } { c'4 clef treble c' c' c' | }")));

    [Fact]
    public void ThreeVoices_DrawTheLastHeard()
        => Assert.Equal(ClefType.Alto, Assert.Single(All<ClefChangeItem>(
            Staff("voice { c'2 clef bass c' | } { c'2 clef bass c' | } { c'2 clef alto c' | }"))).NewClef);

    [Fact]
    public void ClefsAtTwoMoments_AreBothDrawn()
        => Assert.Equal(new[] { ClefType.Bass, ClefType.Alto }, All<ClefChangeItem>(
            Staff("voice { c'4 clef bass c' c' c' | } { c'2 clef alto c' | }")).Select(c => c.NewClef).ToArray());

    [Fact]
    public void AWordOnlyTempoInTheBody_StandsAtItsMembersMoment()
    {
        var mark = Assert.Single(Score("c'2 tuplet 3/2 { d'8 tempo \"Lento\" e' f' } g'4 |").MusicMarks,
            m => m.Type == MusicMarkType.Tempo);
        Assert.Equal(new Fraction(1, 2) + new Fraction(1, 12), mark.AnchorTiming);
    }

    [Fact]
    public void AKeyBesideAClefThatGivesWay_IsStampedWithTheClefThatStays()
    {
        var staff = Staff("voice { c'4 clef bass key g major c' c' c' | } { c'4 clef alto c' c' c' | }");
        Assert.Equal(ClefType.Alto, Assert.Single(All<KeySignatureChangeItem>(staff)).Clef);
    }

    [Fact]
    public void AVoicesTwoClefsAtOneMoment_DrawTheLast()
        => Assert.Equal(ClefType.Alto, Assert.Single(All<ClefChangeItem>(
            Staff("voice { c'4 clef bass clef alto c' c' c' | } { c'1 | }"))).NewClef);

    [Fact]
    public void AnOctaveModeInTheBody_RulesTheNotesAfterIt()
    {
        static int[] Heads(Staff s) => All<NoteItem>(s).Select(n => n.StaffPosition).ToArray();
        var outside = Heads(Staff("c'2 octave relative d'8 e f octave absolute g'4 r8 |"));
        var inside = Heads(Staff("c'2 tuplet 3/2 { octave relative d'8 e f } octave absolute g'4 |"));
        Assert.Equal(outside, inside);
    }

    [Fact]
    public void AnOverrideInTheBody_IsCollected_AtItsMember()
    {
        var o = Assert.Single(Score("c'2 tuplet 3/2 { d'8 override NoteHead.color = red e' f' } g'4 |").GrobOverrides);
        Assert.Equal("NoteHead", o.GrobType);
    }

    [Fact]
    public void ARevertInTheBody_IsCollected()
        => Assert.Single(Score("override NoteHead.color = red c'2 tuplet 3/2 { d'8 revert NoteHead.color e' f' } g'4 |").GrobReverts);

    [Fact]
    public void AOnceInTheBody_IsCollected()
        => Assert.Single(Score("c'2 tuplet 3/2 { once override NoteHead.color = blue d'8 e' f' } g'4 |").GrobOverrides);

    [Fact]
    public void AVoicesClefThereAndBack_AtOneMoment_DrawsNone()
        => Assert.Empty(All<ClefChangeItem>(Staff("voice { c'4 clef bass clef treble c' c' c' | } { c'1 | }")));

    [Fact]
    public void TheSameMomentOfAnotherBar_IsAnotherMoment()
    {
        // Back to treble in bar 2 is a real change: the bass of bar 1 stands until then.
        var staff = Staff("voice { c'4 clef bass c' c' c' | c'1 | } { c'1 | c'4 clef treble c' c' c' | }");
        Assert.Equal(new[] { ClefType.Bass, ClefType.Treble }, All<ClefChangeItem>(staff).Select(c => c.NewClef).ToArray());
    }

    [Fact]
    public void AnotherStaffsClefBefore_IsNotThisStaffs()
    {
        // q stands in bass, so its treble is a change, whatever p stood in before its own clef.
        const string music = "voice { c'4 clef CLEF c' c' c' | } { c'1 | }";
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart p { clef treble }\npart q { clef bass }\n"
            + "section A { p { " + music.Replace("CLEF", "bass") + " } q { " + music.Replace("CLEF", "treble")
            + " } }\nform { A }\nscore { staff p staff q }\n");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var staves = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!).StaffGroups.SelectMany(g => g.Staves);
        Assert.Equal(new[] { ClefType.Bass, ClefType.Treble },
            staves.Select(s => Assert.Single(All<ClefChangeItem>(s)).NewClef).ToArray());
    }

    [Fact]
    public void AnotherStaffsClef_AtTheSameBarAndMoment_StaysDrawn()
    {
        const string music = "voice { c'4 clef bass c' c' c' | } { c'1 | }";
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart p { clef treble }\npart q { clef treble }\n"
            + "section A { p { " + music + " } q { " + music.Replace("bass", "alto") + " } }\nform { A }\nscore { staff p staff q }\n");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var staves = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!).StaffGroups.SelectMany(g => g.Staves);
        Assert.Equal(new[] { ClefType.Bass, ClefType.Alto },
            staves.Select(s => Assert.Single(All<ClefChangeItem>(s)).NewClef).ToArray());
    }
}
