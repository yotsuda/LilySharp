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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// ⑶ beamdirs (HANDOFF §1): the per-measure beam-detection memo must be INVISIBLE in the
/// detector's output — a replayed detection equals a live one on the whole surface the
/// bake (<c>MeasureCollector.ResolveBeamStemDirections</c>) consumes. This is the
/// equivalence net at the device's own level (the incremental==full nets in
/// <c>IncrementalCompilerTests</c> hold the same claim end-to-end, through the bake and
/// the render); like <c>MusicSitesEquivalenceTests</c> it also asserts LIVENESS, because
/// a memo that never fires passes every equality net.
/// </summary>
public class BeamDetectionMemoTests
{
    /// <summary>One book exercising every per-measure detection regime: plain eighth
    /// runs, a shortest-duration recheck (sixteenth mix), a manual beam over an interior
    /// rest, tuplets (nested lookup + span pricing), a cross-measure manual beam (the
    /// regime the memo GATES OUT and detects live), a mid-piece meter change (the
    /// effective-signature chain), a beam-free measure (an empty entry is a result too)
    /// and writer-forced stems.</summary>
    private const string Book = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody {
          c8 d e f g f e d |
          c16 d e f g8 a b4 c8 d |
          c8[ r8 c8] d8 e4 f4 |
          tuplet 3/2 { g8 a b } tuplet 3/2 { c d e } d2 |
          g4 a g8[ c b c | d8 e fis g] a4 g |
          c4 d e f |
          g8@stemDown a b a g f e d |
          time 3/4 a8 b c d e f |
          b8 a g f e d |
        } }
        form main { Main }
        score main "x" { staff melody }
        """;

    /// <summary>
    /// The whole memo claim at the detector's level: detect a book once (populating the
    /// memo), then detect a POSITION-SHIFTED but content-identical copy of it through the
    /// memo's next generation, and the replayed groups must equal a memo-free live
    /// detection of that copy on the bake-visible surface. The shift (a leading newline)
    /// moves every source offset, which is exactly what a keystroke does to the measures
    /// behind the edit — a position-dependent leak into the key (a miss) or into the
    /// stored groups (a stale replay) is what this net exists to catch.
    /// </summary>
    [Fact]
    public void ReplayedDetection_EqualsLiveDetection_AcrossAPositionShift()
    {
        var (voiceA, sigA, tupletsA) = FirstStaffDetectionInput(Book);
        var (voiceB, sigB, tupletsB) = FirstStaffDetectionInput("\n" + Book);

        var memo = new BeamDetectionMemo();
        memo.BeginCollect();
        new BeamDetector().DetectBeamGroups(voiceA, sigA, tupletsA, memo: memo);
        Assert.True(memo.Misses > 0, "the first detection stored nothing — the net is vacuous");

        memo.BeginCollect(); // age the entries into the previous generation (a keystroke)
        var replayed = new BeamDetector().DetectBeamGroups(voiceB, sigB, tupletsB, memo: memo);
        var live = new BeamDetector().DetectBeamGroups(voiceB, sigB, tupletsB);

        Assert.Equal(live.Select(Surface).ToArray(), replayed.Select(Surface).ToArray());
        // Liveness: every measure but the two the cross-measure beam touches replays.
        Assert.Equal(voiceB.Measures.Length - 2, memo.Hits);
        Assert.Equal(0, memo.Misses);
    }

    /// <summary>Within one generation the memo also serves — the second detection of the
    /// SAME voice replays every untouched measure (what dedups a book of identical bars
    /// inside a single collect).</summary>
    [Fact]
    public void SameGenerationReplay_EqualsLiveDetection()
    {
        var (voice, sig, tuplets) = FirstStaffDetectionInput(Book);
        var memo = new BeamDetectionMemo();
        memo.BeginCollect();
        var first = new BeamDetector().DetectBeamGroups(voice, sig, tuplets, memo: memo);
        var replayed = new BeamDetector().DetectBeamGroups(voice, sig, tuplets, memo: memo);
        Assert.Equal(first.Select(Surface).ToArray(), replayed.Select(Surface).ToArray());
    }

    /// <summary>The memo is bypassed wholesale on the multi-voice fan's shape (a nonzero
    /// voice index / a direction-forcing callback): the stored groups carry VoiceIndex
    /// and direction decisions, so serving them there would be a false reuse. Asserted by
    /// counters: the same detector run with a fan-shaped call neither hits nor stores.</summary>
    [Fact]
    public void VoiceFanShapedCalls_BypassTheMemo()
    {
        var (voice, sig, tuplets) = FirstStaffDetectionInput(Book);
        var memo = new BeamDetectionMemo();
        memo.BeginCollect();
        new BeamDetector().DetectBeamGroups(voice, sig, tuplets, voiceIndex: 1, memo: memo);
        new BeamDetector().DetectBeamGroups(voice, sig, tuplets, forceStemUpAt: _ => true, memo: memo);
        Assert.Equal(0, memo.Hits);
        Assert.Equal(0, memo.Misses);
    }

    // ---------- helpers ----------

    private static (Voice Voice, TimeSignature Sig, System.Collections.Immutable.ImmutableArray<TupletBracketItem> Tuplets)
        FirstStaffDetectionInput(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var (_, staff, _) = score.EnumerateStaves().First();
        return (staff.PrimaryVoice, score.TimeSignature, score.TupletBrackets);
    }

    /// <summary>The bake-visible surface of one group — every field
    /// <c>ResolveBeamStemDirections</c> reads (group direction, member addressing and
    /// directions, beamlet counts, head ranges, rest stems). <c>Member.Item</c> is
    /// deliberately NOT compared: the bake addresses the live measure by
    /// <c>ItemIndex</c> and never reads the member's own item reference, which a stored
    /// group keeps from the collect that stored it.</summary>
    private static string Surface(BeamGroup g) =>
        $"{g.MeasureIndex}:{g.StartIndex}:{(g.StemUp ? 'u' : 'd')}:{g.GrowDirection}:{g.VoiceIndex}"
        + "|" + string.Join(",", g.Members.Select(m =>
            $"{m.ResolveMeasureIndex(g.MeasureIndex)}.{m.ItemIndex}"
            + $".{(m.MemberStemUp ? 'u' : 'd')}.{m.BeamCount}.{m.BeamCountLeft}.{m.BeamCountRight}"
            + $".{m.HeadPositionMin}.{m.HeadPositionMax}.{m.StaffPosition}.{m.TargetStaffIndex}"))
        + "|" + string.Join(",", g.RestStems.Select(r =>
            $"{r.ItemIndex}.{r.BeforeMember}.{r.CountLeft}.{r.CountRight}.{r.NoteValue}.{r.MeasureIndex}"));
}
