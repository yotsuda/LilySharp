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
using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>@or</c> on a chords-row symbol (Dettato request 17, owner's decisions 2026-11-17): the
/// chords it may be instead. It reads and is reported; the preview shows its first candidate as a
/// grey memo; Ctrl+Shift+Up / Down rotates the candidates through the symbol's place and sounds
/// the one that came forward; nothing on paper, nothing in the sound changes.
/// </summary>
public class OrAnnotationTests
{
    private static string Book(string row) => $$"""
        time 4/4
        part bass { instrument bass }
        section A { chords harmony { {{row}} } bass { f1 | c1 | g1 | a1 | } }
        form { A }
        score { staff bass  chords harmony }
        """;

    private static System.Collections.Generic.IReadOnlyList<Diagnostic> Check(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics.Concat(SemanticValidation.Run(tree))];
    }

    [Fact]
    public void AnOr_ReadsAndIsReported_WithItsCandidates()
    {
        var diags = Check(Book("F@or(Bm Dm) | C@or(F#m7b5 C/E) | G@or(Em)@todo(\"check\") | Am |"));
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.UnknownAnnotation || d.Code == DiagnosticCodes.ChordBlockBadMember);
        var ors = diags.Where(d => d.Code == DiagnosticCodes.OrMark).Select(d => d.Message).ToList();
        Assert.Equal(3, ors.Count);
        Assert.StartsWith("F or Bm, Dm?", ors[0]);
        Assert.StartsWith("C or F#m7b5, C/E?", ors[1]);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.TodoMark);
    }

    [Theory]
    [InlineData("F@or | C | G | Am |")]
    [InlineData("F@or(Bm xyz) | C | G | Am |")]
    public void AnOrThatDoesNotRead_IsSaidSo(string row)
        => Assert.Contains(Check(Book(row)), d => d.Code == DiagnosticCodes.OrArgument);

    [Fact]
    public void AnOrOnANote_IsIgnoredAndSaidSo()
        => Assert.Contains(Check("part a { clef treble }\nsection A { a { c'1@or(Bm) | } }\nform { A }\n"),
            d => d.Code == DiagnosticCodes.OrArgument);

    [Fact]
    public void ThePreview_ShowsTheFirstCandidate_InGrey_AndATodoBesideItStaysRed()
    {
        var tree = SyntaxTree.Parse(Book("F@or(Bm Dm) | C | G@or(Em)@todo(\"check\") | Am |"));
        var names = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).ChordNames
            .OrderBy(c => c.MeasureIndex).ToList();
        Assert.Equal(("", "or Bm?", true), (names[0].TodoKey, names[0].TodoMemo, names[0].TodoGrey));
        Assert.Null(names[1].TodoKey);
        Assert.Equal(("check  or Em?", false), (names[2].TodoMemo, names[2].TodoGrey));
    }

    [Fact]
    public void NothingChanges_OnPaperOrInTheSound()
    {
        string plain = Book("F | C | G | Am |"), marked = Book("F@or(Bm Dm) | C | G@or(Em)@todo(\"x\") | Am |");
        Assert.Equal(new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(plain)),
            new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(marked)));
        static string Notes(string s) => string.Join(" ", new MidiExporter().Export(SyntaxTree.Parse(s)).Tracks
            .SelectMany(t => t.Notes.Select(n => $"{n.StartTick}:{n.Pitch}")));
        Assert.Equal(Notes(plain), Notes(marked));
    }

    [Theory]
    [InlineData(+1, "Bm", "@or(Dm F)")]
    [InlineData(-1, "Dm", "@or(F Bm)")]
    public void TheSemitonePair_RotatesTheCandidates_AndSoundsTheOneThatCameForward(int dir, string symbol, string mark)
    {
        string text = Book("F@or(Bm Dm) | C | G | Am |");
        int caret = text.IndexOf("F@or");
        var step = NoteStepper.Step(text, SyntaxTree.Parse(text), [(caret, caret)], dir, semitone: true);
        Assert.False(step.Fallback);
        string after = ApplyAll(text, step.Edits);
        Assert.Equal(symbol + mark + " | C", after.Substring(caret, symbol.Length + mark.Length + 4));
        Assert.NotEmpty(step.Pitches);
        // Bm sounds a B minor triad's tones (B D F#), Dm a D minor one's (D F A).
        var classes = step.Pitches.Select(p => p % 12).ToHashSet();
        Assert.True(dir > 0 ? classes.SetEquals([11, 2, 6]) : classes.SetEquals([2, 5, 9]), string.Join(" ", step.Pitches));
        // …and a full turn comes back.
        string t = text;
        for (int i = 0; i < 3; i++)
        {
            var s = NoteStepper.Step(t, SyntaxTree.Parse(t), [(caret, caret)], dir, semitone: true);
            t = ApplyAll(t, s.Edits);
        }
        Assert.Equal(text, t);
    }

    [Fact]
    public void TheQuickFixes_KeepTheSymbolOrTakeACandidate_AndTheOrGoes_ATodoStays()
    {
        string text = Book("F@or(Bm Dm)@todo(\"x\") | C | G | Am |");
        var choices = NoteStepper.OrChoices(SyntaxTree.Parse(text), text.IndexOf("@or") + 2);
        Assert.Equal(new[] { "F", "Bm", "Dm" }, choices.Select(c => c.Choice));
        Assert.True(choices[0].Kept);
        Assert.Contains("{ F@todo(\"x\") | C", ApplyAll(text, choices[0].Edits));
        Assert.Contains("{ Bm@todo(\"x\") | C", ApplyAll(text, choices[1].Edits));
        Assert.Empty(NoteStepper.OrChoices(SyntaxTree.Parse(text), text.IndexOf("| C")));
    }

    [Fact]
    public void RemoveAll_TakesEveryOr_AndLeavesTheChordsAndTheTodos()
    {
        string text = Book("F@or(Bm Dm) | C@or(xyz) | G@or(Em)@todo(\"x\") | Am |");
        var marks = NoteStepper.OrMarks(SyntaxTree.Parse(text));
        Assert.Equal(3, marks.Count);
        foreach (var (s, e) in marks.OrderByDescending(m => m.Start))
            text = text[..s] + text[e..];
        Assert.Contains("{ F | C | G@todo(\"x\") | Am | }", text);
    }

    [Fact]
    public void TheRotation_SoundsTheChordAsTheRowPlaysIt()
    {
        // (owner's report 2026-11-17: the rotation sounded a guitar, the row's playback a piano, louder)
        string text = Book("F@or(Bm Dm) | C | G | Am |");
        int caret = text.IndexOf("F@or");
        var step = NoteStepper.Step(text, SyntaxTree.Parse(text), [(caret, caret)], +1, semitone: true);
        string after = ApplyAll(text, step.Edits);
        var row = new MidiExporter().Export(SyntaxTree.Parse(after)).Tracks.Single(t => t.Name == "harmony (chords)");
        var first = row.Notes.Where(n => n.StartTick == 0).ToList();
        Assert.Equal(first.Select(n => n.Pitch).OrderBy(p => p), step.Pitches);
        Assert.Equal(row.Program ?? -1, step.Program);
        Assert.Equal(first[0].Timbre, step.Timbre);
        Assert.Equal(first.Max(n => n.Velocity), step.Velocity);
        Assert.Equal("harmony (chords)", step.Part);
    }

    private static string ApplyAll(string text, System.Collections.Immutable.ImmutableArray<NoteStepper.Edit> edits)
    {
        foreach (var e in edits.OrderByDescending(e => e.Start))
            text = text[..e.Start] + e.NewText + text[e.End..];
        return text;
    }
}
