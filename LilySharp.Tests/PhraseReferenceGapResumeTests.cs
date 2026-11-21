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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A note typed between the phrase references of a section that the form plays before another
/// section it plays again (session 1343, the audit sweep's one pre-existing divergence in the
/// net, <c>dollar-ref.lys</c>): the resumed collect is the full one.
/// </summary>
/// <remarks>
/// A reference is a value read of its own call site and its body's notes fold the body's
/// span, so the text BETWEEN references — where the note lands — was read by nothing. The
/// planner then took a prefix checkpoint in the form's third visit (section First again,
/// whose reads all stand above the edit) and adopted the edited section's recorded bars: the
/// typed `c4` came out an octave high, in the old relative frame. Each walked container is now
/// a prefix-only value read (<c>HeaderRead.PrefixOnly</c>).
/// </remarks>
public sealed class PhraseReferenceGapResumeTests
{
    private const string Book = """
        time 4/4
        key c major
        part melody
        phrase motifA { c'4 d e f | }
        phrase motifB { g4 a b c' | }
        phrase motifC { c'4 e g c' | }
        section First {
          melody { motifA motifB }
        }
        section Second {
          melody { motifC motifA }
        }
        form { First Second First }
        """;

    [Theory]
    [InlineData("melody { motifC motifA }", "melody { c4 motifC motifA }")]
    [InlineData("melody { motifC motifA }", "melody { motifC c4 motifA }")]
    [InlineData("melody { motifC motifA }", "melody { motifC motifA c4 }")]
    public void ANoteTypedAmongTheReferences_ResumesToTheFullCollect(string find, string replacement)
    {
        string oldText = Book.Replace("\r\n", "\n");
        int at = oldText.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, "edit anchor not found");
        string newText = oldText.Substring(0, at) + replacement + oldText.Substring(at + find.Length);

        var oldTree = SyntaxTree.Parse(oldText);
        var oldSpec = RenderSpecParser.FindFirst(oldTree);
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder, ScoreTranspose = oldSpec?.ScoreTranspose };
        SvgGenerator.CollectScore(source, oldTree, oldSpec);

        var newTree = oldTree.WithChange(new TextChange(new TextSpan(at, find.Length), replacement));
        Assert.Equal(newText, newTree.Text);
        var newSpec = RenderSpecParser.FindFirst(newTree);
        var fresh = SyntaxTree.Parse(newText);
        var full = SvgGenerator.CollectScore(fresh, RenderSpecParser.FindFirst(fresh));

        var resumer = CollectResumePlanner.Plan(oldTree, newTree, recorder, source);
        if (resumer == null)
            return; // a full collect
        // The planner may still pick a checkpoint inside the edited section whose address the
        // inserted note moved — the restore then aborts to a full collect, which is right.
        // What must not happen is a resume that returns another picture.
        MultiStaffScore resumed;
        try
        {
            resumed = SvgGenerator.CollectScore(
                new MeasureCollector { WalkProbe = resumer, ScoreTranspose = newSpec?.ScoreTranspose }, newTree, newSpec);
        }
        catch (CollectResumeAbortException)
        {
            return;
        }
        Assert.Null(ModelDeepDiff.FirstDifference(full, resumed, "score"));
    }
}
