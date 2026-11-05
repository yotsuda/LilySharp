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

    private static NoteItem[] Bar0Notes(string music, string phrases = "")
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart p { clef treble }\n" + phrases
            + "\nsection A { p { " + music + " } }\nform { A }\nscore { staff p }\n");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return score.StaffGroups.Single().Staves.Single().Voices[0].Measures[0].Items.OfType<NoteItem>().ToArray();
    }

    private static readonly string[] Quiet = [DiagnosticCodes.MeasureOverflow, DiagnosticCodes.MeasureIncomplete, DiagnosticCodes.PickupWithoutPartial,
        DiagnosticCodes.MissingBarLine, DiagnosticCodes.TupletNotANoteValue];

    private static TupletBracketItem[] Brackets(string music) => Score(music).TupletBrackets.ToArray();

    [Theory]
    [InlineData("tuplet 3/2 { tuplet 3/2 { c'8 d' e' } } c'2 |", 1)]
    [InlineData("tuplet 3/2 { tuplet 3/2 { tuplet 3/2 { c'8 d' e' } } } c'2 |", 1)]
    [InlineData("tuplet 3/2 { << c' 3 5 >>4 } c'2 |", 1)]
    [InlineData("tuplet 3/2 { tuplet 5/4 { c'16 d' e' f' g' } } c'2 |", 2)]
    [InlineData("tuplet 3/2 { tuplet 3/4 { c'8 d' e' } } c'2 |", 2)]
    [InlineData("tuplet 3/2 { tuplet 5/2 { c'8 d' e' f' g' } } c'2 |", 2)]
    [InlineData("tuplet 3/2 { tuplet 3/2 { c'8 d' e' } f'8 g' } c'2 |", 2)]
    [InlineData("tuplet 3/2 { f'8 g' tuplet 3/2 { c'8 d' e' } } c'2 |", 2)]
    [InlineData("tuplet 3/2 { c'8 d' e' } tuplet 3/2 { c'8 d' e' } c'2 |", 2)]
    public void ATupletOfTheSameRatioOverTheSameNotes_IsOne(string music, int brackets)
        => Assert.Equal(brackets, Brackets(music).Length);

    [Fact]
    public void TheOneThatStays_IsTheOuter()
    {
        var b = Assert.Single(Brackets("tuplet 3/2 bare { tuplet 3/2 { c'8 d' e' } } c'2 |"));
        Assert.Equal(0, b.NestingDepth);
        Assert.True(b.Bare);
    }

    [Fact]
    public void TheSameTupletAtTheSameIndices_InAnotherVoiceOrStaffOrBar_IsAnother()
    {
        Assert.Equal(2, Brackets("voice { tuplet 3/2 { c'8 d' e' } c'2. | } { tuplet 3/2 { c'8 d' e' } c'2. | }").Length);
        Assert.Equal(2, Brackets("tuplet 3/2 { c'8 d' e' } c'2. | tuplet 3/2 { c'8 d' e' } c'2. |").Length);
        const string music = "tuplet 3/2 { c'8 d' e' } c'2. |";
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart p { clef treble }\npart q { clef treble }\n"
            + "section A { p { " + music + " } q { " + music + " } }\nform { A }\nscore { staff p staff q }\n");
        Assert.Equal(2, new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!).TupletBrackets.Length);
    }

    private static string[] BarReports(string music, string phrases)
        // The bar after: a short LAST bar is reported by no one.
        => SemanticValidation.Run(SyntaxTree.Parse("octave absolute\ntime 4/4\npart p { clef treble }\n" + phrases
                + "\nsection A { p { " + music + " c'1 | } }\nform { A }\nscore { staff p }\n"))
            .Where(d => Quiet.Contains(d.Code)).Select(d => d.Code).ToArray();

    [Theory]
    [InlineData("tuplet 3/2 { x }")]
    [InlineData("tuplet 3/2 { cue { d'8 e'8 f'8 } }")]
    [InlineData("tuplet 3/2 { << d' e' f' >>4. }")]
    public void WhatFollowsAGroup_StandsAfterItsScaledLength(string tuplet)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart p { clef treble }\n" + X
            + "\nsection A { p { c'2 " + tuplet + " tempo 4 = 90 g'4 | } }\nform { A }\nscore { staff p }\n");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var mark = Assert.Single(new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!).MusicMarks,
            m => m.Type == MusicMarkType.Tempo);
        Assert.Equal(new Fraction(3, 4), mark.AnchorTiming);
    }

    [Fact]
    public void TheNotesAfterAPhrase_AreNotTransposedWithIt()
    {
        var tree = SyntaxTree.Parse("octave absolute\nkey c major\ntime 4/4\npart p { clef treble }\n" + X
            + "\nsection A { p { key d major c'2 tuplet 3/2 { x e'8 f'8 g'8 } | c'1 | } }\nform { A }\nscore { staff p }\n");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var heads = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!).StaffGroups.Single().Staves.Single()
            .Voices[0].Measures[0].Items.OfType<NoteItem>().Select(n => n.StaffPosition).ToArray();
        // e', f', g' as written: the last three heads rise by steps from e' (-4 against c' at -6).
        Assert.Equal(new[] { -4, -3, -2 }, heads.Skip(heads.Length - 3).ToArray());
    }

    [Fact]
    public void APhraseNamingItselfInATuplet_EndsBothWalks()
    {
        const string book = "octave absolute\ntime 4/4\npart p { clef treble }\nphrase z { c'8 tuplet 3/2 { z } }\n"
            + "section A { p { z | } }\nform { A }\nscore { staff p }\n";
        var tree = SyntaxTree.Parse(book);
        new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.NotEmpty(SemanticValidation.Run(tree));
    }

    [Fact]
    public void AGroupsChordName_StandsAtItsMomentInTheBody()
    {
        var name = Assert.Single(Score("c'2 tuplet 3/2 { d'8 << e' g' b' >>4@chord(C) } g'4 |").ChordNames);
        Assert.Equal(new Fraction(1, 2) + new Fraction(1, 12), name.Timing);
    }

    [Fact]
    public void AGroupsBracket_IsNestedInTheTuplets()
    {
        var depths = Score("c'2 tuplet 3/2 { << d' 3 5 >>4 e'8 } g'4 |").TupletBrackets.Select(b => b.NestingDepth).OrderBy(d => d).ToArray();
        Assert.Equal(new[] { 0, 1 }, depths);
    }

    [Fact]
    public void ATupletInACueInTheBody_IsNestedOnce()
    {
        var depths = Score("c'2 tuplet 3/2 { cue { tuplet 3/2 { d'16 e' f' } e'8 f' } } g'4 |").TupletBrackets.Select(b => b.NestingDepth).OrderBy(d => d).ToArray();
        Assert.Equal(new[] { 0, 1 }, depths);
    }

    [Fact]
    public void ATieAfterAGroupInTheBody_HangsOnItsLastMember()
        => Assert.True(Bar0Notes("c'2 tuplet 3/2 { << d' f' >>4~ f'8 } g'4 |")[2].HasTieStart);

    [Fact]
    public void AGroupInTheBody_WaitsForAClefAtItsMoment()
    {
        var staff = Staff("voice { tuplet 3/2 { << c' e' g' >>4 c'8 } c'2. | } { clef bass c'1 | }");
        Assert.Equal(6, staff.Voices[0].Measures[0].Items.OfType<NoteItem>().First().StaffPosition);
    }

    [Fact]
    public void APhraseReadInABody_IsInTheWalksReadExtent()
    {
        // Editing the phrase, below the music that reads it, must not adopt that music's old walk.
        var oldText = "octave absolute\ntime 4/4\npart p { clef treble }\nsection A { p { tuplet 3/2 { x } c'2. | c'1 | c'1 | c'1 | } }\n"
            + X + "\nform { A }\nscore { staff p }\n";
        var newText = oldText.Replace("d'8 e'8 f'8", "g'8 e'8 f'8");
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder };
        var oldTree = SyntaxTree.Parse(oldText);
        SvgGenerator.CollectScore(source, oldTree, RenderSpecParser.FindFirst(oldTree));
        var newTree = SyntaxTree.Parse(newText);
        var newSpec = RenderSpecParser.FindFirst(newTree);
        var fullNew = SvgGenerator.CollectScore(newTree, newSpec);
        var resumer = CollectResumePlanner.Plan(oldTree, newTree, recorder, source);
        if (resumer == null)
            return;
        MultiStaffScore resumed;
        try
        {
            resumed = SvgGenerator.CollectScore(new MeasureCollector { WalkProbe = resumer }, newTree, newSpec);
        }
        catch (CollectResumeAbortException)
        {
            return;
        }
        var diff = ModelDeepDiff.FirstDifference(fullNew, resumed, "score");
        Assert.True(diff == null, $"resumed differs from full: {diff}");
    }

    private const string X = "phrase x { d'8 e'8 f'8 }";

    [Theory]
    [InlineData("tuplet 3/2 { x }")]
    [InlineData("tuplet 3/2 { cue { d'8 e'8 f'8 } }")]
    public void AGroupInTheBody_IsDrawn_AtTheTupletsScale(string tuplet)
    {
        var notes = Bar0Notes("c'2 " + tuplet + " g'4 |", X);
        Assert.Equal(5, notes.Length);
        Assert.Equal(new Fraction(1, 12), notes[1].Duration);
        Assert.Equal(new Fraction(1, 4), notes[4].Duration);
    }

    [Fact]
    public void ABrokenChordInTheBody_CompoundsItsSubdivisionWithTheTuplets()
    {
        var notes = Bar0Notes("c'2 tuplet 3/2 { << d' 3 5 >>4 } g'4 |");
        Assert.Equal(5, notes.Length);
        Assert.Equal(new Fraction(1, 18), notes[1].Duration);
    }

    [Fact]
    public void ABrokenChordInTheBody_LeavesTheTimeToTheTuplet_AndItsWrittenLengthCounts()
    {
        var staff = Staff("c'2 tuplet 3/2 { << d' 3 5 >>4 e'8 } g'4 |");
        Assert.Single(staff.Voices[0].Measures);
    }

    [Fact]
    public void ThePhrasesOctaveMarks_AndTheFrameAfterIt_AreThoseOfThePhraseOutsideATuplet()
    {
        static int[] Heads(NoteItem[] n) => n.Select(x => x.StaffPosition).ToArray();
        const string phrases = "octave relative\nphrase y { d'8 e f }";
        Assert.Equal(Heads(Bar0Notes("y' g'8 c'4 c'4 |", phrases)).Take(3), Heads(Bar0Notes("tuplet 3/2 { y' } c'2 g'4 |", phrases)).Take(3));
    }

    [Fact]
    public void APhraseThatNamesItself_InATuplet_EndsTheWalk()
    {
        var tree = SyntaxTree.Parse(Book("c'1 |") + "phrase z { c'8 tuplet 3/2 { z } }\n");
        new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
    }

    [Theory]
    [InlineData("c'2 tuplet 3/2 { x } g'4 |", X)]
    [InlineData("c'2 tuplet 3/2 { w } g'4 |", "phrase x { d'8 e'8 f'8 }\nphrase w { x }")]
    [InlineData("c'2 tuplet 3/2 { cue { d'8 e'8 f'8 } } g'4 |", "")]
    [InlineData("c'2 tuplet 3/2 { << d' e' f' >>4. } g'4 |", "")]
    public void APhraseInTheBody_PaysItsLengthInTheBarCount(string music, string phrases)
        => Assert.Empty(BarReports(music, phrases));

    [Fact]
    public void APhraseInTheBody_IsJudgedByItsLength()
        => Assert.Contains(DiagnosticCodes.TupletNotANoteValue,
            BarReports("c'2 tuplet 3/2 { x } g'4 |", "phrase x { d'8 e'8 }"));

    [Fact]
    public void ACueInTheBody_IsACueRegion()
        => Assert.All(Bar0Notes("c'2 tuplet 3/2 { cue { d'8 e'8 f'8 } } g'4 |").Skip(1).Take(3), n => Assert.True(n.IsCue));

    private static int VoiceErrors(string music, string phrases = "")
        => SemanticValidation.Run(SyntaxTree.Parse("octave absolute\ntime 4/4\npart p { clef treble }\n" + phrases
                + "\nsection A { p { " + music + " } }\nform { A }\nscore { staff p }\n"))
            .Count(d => d.Code == DiagnosticCodes.VoiceInTuplet);

    [Theory]
    [InlineData("c'2 tuplet 3/2 { voice { d'8 e' f' } { g'8 a' b' } } g'4 |", "")]
    [InlineData("c'2 tuplet 3/2 { cue { voice { d'8 e' f' } { g'8 a' b' } } } g'4 |", "")]
    [InlineData("c'2 tuplet 3/2 { y } g'4 |", "phrase y { voice { d'8 e' f' } { g'8 a' b' } }")]
    [InlineData("c'2 tuplet 3/2 { w } g'4 |", "phrase y { voice { d'8 e' f' } { g'8 a' b' } }\nphrase w { y }")]
    [InlineData("c'2 tuplet 3/2 { y y } g'4 |", "phrase y { voice { d'8 e' f' } { g'8 a' b' } }")]
    public void AVoiceInTheBody_IsAnError(string music, string phrases)
        => Assert.True(VoiceErrors(music, phrases) >= 1);

    [Theory]
    [InlineData("c'2 voice { tuplet 3/2 { d'8 e' f' } g'4 } { tuplet 3/2 { g'8 a' b' } g'4 } |", "")]
    [InlineData("c'2 voice { d'4 g'4 } { g'4 g'4 } |", "")]
    [InlineData("c'2 tuplet 3/2 { x } g'4 |", X)]
    [InlineData("y tuplet 3/2 { c'8 d' e' } c'4 |", "phrase y { voice { c'2 } { e'2 } }")]
    public void ATupletInsideEachVoice_IsNoError(string music, string phrases)
        => Assert.Equal(0, VoiceErrors(music, phrases));

    [Fact]
    public void AReferenceInANestedTuplet_IsReportedOnce()
        => Assert.Equal(1, VoiceErrors("c'2 tuplet 3/2 { tuplet 3/2 { y } } g'4 |", "phrase y { voice { d'8 e' f' } { g'8 a' b' } }"));

    [Fact]
    public void APhraseOfVoicesNamingItselfInATuplet_EndsTheCheck()
        => Assert.True(VoiceErrors("z |", "phrase z { voice { c'4 } { e'4 } tuplet 3/2 { z } }") >= 1);

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
