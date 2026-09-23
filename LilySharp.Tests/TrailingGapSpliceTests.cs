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
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The observer of the suffix splice's CONTENT-WINDOW gate (HANDOFF ⒮¹², session 523): a
/// recorded tail adopted from a candidate standing BEFORE the edit walks the old node stream
/// across the edit, so the gate declines it unless the edit changed no token.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS NET EXISTS: the gate came out of session 366's audit sweep (88 divergences over 258
/// books × 30 edit classes), and session 458 found that forcing it open reddened nothing — not
/// the suite, not the owner's corpus rendered forward — and filed ⒮¹². Session 523 reran the
/// sweep with the gate forced open: 0 of 259 net books diverged, but 3 edits in 2 books of the
/// owner's corpus did (tab-chord.lys twice, 想い人.lys once), every one of them a NOTE TYPED AFTER
/// A PART-MAJOR CELL'S LAST ITEM. The suite's own shape of that hole
/// (<c>CollectEditResumeTests.NoteTypedAfterABodysLastItem_IsNotSplicedAway</c>) is section-major,
/// walked through <see cref="IncrementalCompiler"/>, and stays green with the gate open — a
/// part-major cell reaches the splice through a different address. These two books are the
/// corpus's shapes, on the planner's own path, asserting BOTH that the resumed collect equals a
/// fresh one and that the gate is what declined (an equality alone would be satisfied by any
/// other guard taking over — RULES §5.4's "a net that does not redden under the poison claims
/// nothing").
/// </para>
/// </remarks>
public sealed class TrailingGapSpliceTests
{
    // tab-chord.lys's shape: two part-major cells of one whole note each, a tab staff.
    private const string PartMajorChords = """
        part melody {
          section A { c1\3@chord(Cmaj7) }
        }

        part back {
          section A { e1\3@chord(Dm7) }
        }

        form main { A }

        score main {
          staff back
          tab melody
        }
        """;

    // 想い人.lys's shape: a part-major cell whose last item closes a slur, on a bass tab.
    private const string PartMajorSlurEnd = """
        part bass { clef bass  octave 3 }

        part bass {
          section Body {
            fis,,4\3 b,,2.\4 | fis,1\3 | fis,,4( b,,2.\4)
          }
        }

        form main { ~Body }

        score main {
          staff bass
          tab bass
        }
        """;

    [Theory]
    [InlineData(PartMajorChords, @"c1\3@chord(Cmaj7) }", @"c1\3@chord(Cmaj7) c1\3@chord(Cmaj7) }")]
    [InlineData(PartMajorChords, @"e1\3@chord(Dm7) }", @"e1\3@chord(Dm7) e1\3@chord(Dm7) }")]
    [InlineData(PartMajorSlurEnd, @"b,,2.\4)", @"b,,2.\4) b,,2.\4)")]
    public void ANoteTypedAfterAPartMajorCellsLastItem_IsDeclinedByTheContentWindowGate(
        string source, string find, string replacement)
    {
        var oldText = source.Replace("\r\n", "\n");
        int at = oldText.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, "edit anchor not found");
        var newText = oldText.Substring(0, at) + replacement + oldText.Substring(at + find.Length);

        var oldTree = SyntaxTree.Parse(oldText);
        var oldSpec = RenderSpecParser.FindFirst(oldTree);
        var recorder = CollectWalkProbe.Recorder();
        var source0 = new MeasureCollector { WalkProbe = recorder, ScoreTranspose = oldSpec?.ScoreTranspose };
        SvgGenerator.CollectScore(source0, oldTree, oldSpec);

        // The LSP's tree: the old one incrementally reparsed.
        var newTree = oldTree.WithChange(new TextChange(new TextSpan(at, find.Length), replacement));
        Assert.Equal(newText, newTree.Text);
        var newSpec = RenderSpecParser.FindFirst(newTree);
        var full = SvgGenerator.CollectScore(SyntaxTree.Parse(newText), RenderSpecParser.FindFirst(SyntaxTree.Parse(newText)));

        var resumer = CollectResumePlanner.Plan(oldTree, newTree, recorder, source0);
        Assert.NotNull(resumer); // the planner offers the walk its recorded tail
        resumer!.RecordSpliceDeclines = true;
        // The edited walk declines and runs live, so its pitch trace grows by one — and the
        // NEXT walk's entry watermark then bails the whole resume to a full collect (the
        // product's fallback, correctness untouched). A bail is the right outcome here; what
        // the poison produces instead is a resume that SPLICED and returned.
        bool bailed = false;
        MultiStaffScore? resumed = null;
        try
        {
            resumed = SvgGenerator.CollectScore(
                new MeasureCollector { WalkProbe = resumer, ScoreTranspose = newSpec?.ScoreTranspose }, newTree, newSpec);
        }
        catch (CollectResumeAbortException)
        {
            bailed = true;
        }

        // ⑴ the picture: a resumed collect that returned is the full collect
        if (!bailed)
            Assert.Null(ModelDeepDiff.FirstDifference(full, resumed!, "score"));
        // ⑵ the gate: the edited walk's candidates were declined BY THE CONTENT-WINDOW GATE, and
        // nothing was spliced. A resume that spliced (or declined for another reason) is the
        // poison's shape even when ⑴ happens to hold.
        var plans = resumer.ResumePlans.Values.ToList();
        Assert.True(plans.Count >= 1, "no walk was planned");
        Assert.All(plans, p => Assert.Equal(0, p.SplicedMeasures));
        Assert.Contains(plans, p => p.SpliceDeclines != null
            && p.SpliceDeclines.Any(d => d.Why.Contains("content window", StringComparison.Ordinal)));
    }
}
