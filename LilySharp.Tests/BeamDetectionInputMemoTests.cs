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
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>MultiStaffLayouter.BeamGroupsOf</c> lets the STAFF quantity and the ANNOTATION quantity
/// share one detection when — and only when — they are the same three inputs. This is the
/// instrument for that equation, and it exists because the CORPUS IS NOT ONE: session 192
/// dropped the tuplet list out of the memo key and swept all 566 books in the tree, and NOT
/// ONE MOVED. No book in the repository has two detection inputs that share a first voice and
/// a meter and differ only in their tuplets, so a wrong key there would be invisible for as
/// long as that stays true — and it would be invisible as WRONG BEAMS, not as a crash
/// (HANDOFF RULES §5.3: an all-books A/B is not the instrument for an equation).
/// </summary>
public class BeamDetectionInputMemoTests
{
    /// <summary>
    /// A book whose detection ACTUALLY DEPENDS ON THE TUPLET LIST — which took measuring to
    /// find, and is the reason this file exists in the shape it does.
    /// </summary>
    /// <remarks>
    /// ⚠️ ALMOST NOTHING DISCRIMINATES. A tuplet no longer bounds a beam here (both
    /// LILYSHARP-OWN devices that made it do so were falsified and removed — see
    /// <c>BeamDetector.DetectBeamGroups</c>), so the list reaches the answer through exactly
    /// two doors: <c>BeamingPattern</c>'s span-boundary beamlet clamp (at_span_start /
    /// at_span_stop) and the written-proportion ranking. MEASURED (session 192): of 25 hand-
    /// written shapes — triplets of sixteenths, of thirty-seconds, nested, quintuplets,
    /// sextuplets, tuplets against plain runs on either side — exactly ONE answered
    /// differently with the brackets than without. It is this one, and the difference is the
    /// thirty-second's beamlet turning round: stem 2 reads left/right 3/2 with the tuplet and
    /// 2/3 without, because the clamp will not let a beamlet stick out of its span.
    /// <para>
    /// That is also why the all-books sweep says nothing here: poisoning the tuplet half of
    /// the key moved 0 of 566 books, because no book in the tree writes this.
    /// </para>
    /// </remarks>
    private const string TupletBook = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody {
          c16 tuplet 3/2 { d16. e32 f16 } g16 |
          c8 d e f g a b c' |
        } }
        form main { Main }
        score main "x" { staff melody }
        """;

    /// <summary>One staff, two voices — the shape on which the annotation quantity (the
    /// primary voice alone) and the staff quantity (every voice, direction-forced) genuinely
    /// differ while sharing their first voice, so both land under one memo key.</summary>
    private const string TwoVoiceBook = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody {
          voice { c8 d e f g a b c' } voice { e,8 f, g, a, b, c d e }
        } }
        form main { Main }
        score main "x" { staff melody }
        """;

    /// <summary>
    /// THE FOLD ITSELF: the two callers hand in Scores that differ in everything the LAYOUT
    /// cares about — clef, tempo, title, composer — and agree on the only three things the
    /// detector reads. One detection must serve both, and it must be the RIGHT one.
    /// </summary>
    /// <remarks>
    /// Reference equality is the liveness half (HANDOFF RULES §5.4: an equality net a memo
    /// never fires under passes for the wrong reason); the comparison against a memo-free
    /// <see cref="BeamDetector"/> is the correctness half.
    /// </remarks>
    [Fact]
    public void SameThreeInputs_SpeltAsDifferentScores_ShareOneDetection()
    {
        var (voice, sig, tuplets) = DetectionInput(TupletBook);
        var layouter = NewLayouter();

        var asStaffScore = new Score(voice, sig, KeySignature.CMajor, "treble",
            tupletBrackets: tuplets);
        var asAnnotationScore = new Score(voice, sig, KeySignature.CMajor, "bass",
            tempo: 120, title: "different", composer: "different",
            tupletBrackets: tuplets);

        var first = layouter.BeamGroupsOf(asStaffScore);
        var second = layouter.BeamGroupsOf(asAnnotationScore);

        Assert.True(first.Length > 0, "the book detected no beams — the net is vacuous");
        Assert.Equal(Surface(first), Surface(second));
        // The memo actually served: the two calls returned the SAME array.
        Assert.True(first == second, "the second caller re-detected instead of sharing");
        Assert.Equal(Surface(new BeamDetector().DetectBeamGroups(asAnnotationScore)),
            Surface(second));
    }

    /// <summary>
    /// THE OTHER DIRECTION: same voice, same meter, a different TUPLET LIST — the memo must
    /// not serve one for the other. Asserted after first showing that the two inputs really
    /// do detect differently, because a case the detector answers identically would make the
    /// key look sound whatever it compared.
    /// </summary>
    [Fact]
    public void TupletListsThatDiffer_DoNotShareADetection()
    {
        var (voice, sig, tuplets) = DetectionInput(TupletBook);
        Assert.False(tuplets.IsDefaultOrEmpty, "the book carries no tuplet — the net is vacuous");

        var withTuplets = new Score(voice, sig, KeySignature.CMajor, "treble",
            tupletBrackets: tuplets);
        var withoutTuplets = new Score(voice, sig, KeySignature.CMajor, "treble",
            tupletBrackets: ImmutableArray<TupletBracketItem>.Empty);

        // The case discriminates: a memo-free detector answers the two differently.
        var liveWith = new BeamDetector().DetectBeamGroups(withTuplets);
        var liveWithout = new BeamDetector().DetectBeamGroups(withoutTuplets);
        Assert.NotEqual(Surface(liveWith), Surface(liveWithout));

        var layouter = NewLayouter();
        Assert.Equal(Surface(liveWith), Surface(layouter.BeamGroupsOf(withTuplets)));
        Assert.Equal(Surface(liveWithout), Surface(layouter.BeamGroupsOf(withoutTuplets)));
    }

    /// <summary>
    /// Same again for the VOICE SET: the staff quantity of a two-voice staff and the
    /// annotation quantity of the same staff share a first voice — the memo's key — and must
    /// still not share an answer.
    /// </summary>
    [Fact]
    public void VoiceSetsThatDiffer_DoNotShareADetection()
    {
        var tree = SyntaxTree.Parse(TwoVoiceBook);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var (_, staff, _) = score.EnumerateStaves().First();
        Assert.True(staff.Voices.Length > 1, "the book has one voice — the net is vacuous");

        var everyVoice = new Score(staff.Voices, score.TimeSignature, score.KeySignature, "treble");
        var primaryOnly = new Score(staff.PrimaryVoice, score.TimeSignature, score.KeySignature, "treble");
        Assert.True(ReferenceEquals(everyVoice.Voices[0], primaryOnly.Voices[0]),
            "the two inputs do not share a first voice — the net is not on the memo's key");

        var liveEvery = new BeamDetector().DetectBeamGroups(everyVoice);
        var livePrimary = new BeamDetector().DetectBeamGroups(primaryOnly);
        Assert.NotEqual(Surface(liveEvery), Surface(livePrimary));

        var layouter = NewLayouter();
        Assert.Equal(Surface(liveEvery), Surface(layouter.BeamGroupsOf(everyVoice)));
        Assert.Equal(Surface(livePrimary), Surface(layouter.BeamGroupsOf(primaryOnly)));
    }

    /// <summary>
    /// Two inputs under one key must not evict each other. A one-slot table would answer
    /// every one of these calls with a fresh detection while looking exactly like a memo —
    /// the failure <c>_pagingAugments</c> is on record for.
    /// </summary>
    [Fact]
    public void TwoInputsUnderOneKey_BothKeepServing()
    {
        var (voice, sig, tuplets) = DetectionInput(TupletBook);
        var withTuplets = new Score(voice, sig, KeySignature.CMajor, "treble",
            tupletBrackets: tuplets);
        var withoutTuplets = new Score(voice, sig, KeySignature.CMajor, "treble",
            tupletBrackets: ImmutableArray<TupletBracketItem>.Empty);

        var layouter = NewLayouter();
        var a1 = layouter.BeamGroupsOf(withTuplets);
        var b1 = layouter.BeamGroupsOf(withoutTuplets);
        var a2 = layouter.BeamGroupsOf(withTuplets);
        var b2 = layouter.BeamGroupsOf(withoutTuplets);

        Assert.True(a1 == a2, "the first input stopped being served after the second arrived");
        Assert.True(b1 == b2, "the second input stopped being served after the first was asked again");
    }

    // ---------- helpers ----------

    private static MultiStaffLayouter NewLayouter() =>
        new(LayoutOptions.Default, new MeasureLayouter());

    private static (Voice Voice, TimeSignature Sig, ImmutableArray<TupletBracketItem> Tuplets)
        DetectionInput(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var (_, staff, _) = score.EnumerateStaves().First();
        return (staff.PrimaryVoice, score.TimeSignature, score.TupletBrackets);
    }

    /// <summary>The whole of a detection's answer, as a string, so two answers can be
    /// compared and a difference can be read.</summary>
    private static string Surface(ImmutableArray<BeamGroup> groups) =>
        string.Join(";", groups.Select(g =>
            $"{g.MeasureIndex}:{g.StartIndex}:{(g.StemUp ? 'u' : 'd')}:{g.GrowDirection}:{g.VoiceIndex}"
            + "|" + string.Join(",", g.Members.Select(m =>
                $"{m.ResolveMeasureIndex(g.MeasureIndex)}.{m.ItemIndex}"
                + $".{(m.MemberStemUp ? 'u' : 'd')}.{m.BeamCount}.{m.BeamCountLeft}.{m.BeamCountRight}"
                + $".{m.HeadPositionMin}.{m.HeadPositionMax}.{m.StaffPosition}.{m.TargetStaffIndex}"))
            + "|" + string.Join(",", g.RestStems.Select(r =>
                $"{r.ItemIndex}.{r.BeforeMember}.{r.CountLeft}.{r.CountRight}.{r.NoteValue}.{r.MeasureIndex}"))));
}
