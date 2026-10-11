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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The learning set: the <c>lesson-…</c> completion items, each of which puts a whole lesson
/// (a complete, commented score) into the editor (docs/learning-set-design.md).
/// </summary>
/// <remarks>
/// A lesson is a promise in two directions, and both are pinned here: the SCORE it hands over
/// compiles clean (a tutorial that teaches an error is worse than none), and what its COMMENTS
/// tell the writer to try is what the editor actually does — lesson 1 is a tour of the
/// completion list, so a change to that list that falsifies the tour has to be a red test,
/// not a beginner wondering why `instrument` is not where the lesson said.
/// </remarks>
[Trait("Category", "Unit")]
public class LessonTests
{
    public static IEnumerable<object[]> AllLessons() =>
        LilySharpLanguageServer.LessonsByLanguage
            .SelectMany(kv => kv.Value.Select(l => new object[] { kv.Key, l.Label }));

    private static LilySharpLanguageServer.Lesson LessonOf(string language, string label) =>
        LilySharpLanguageServer.LessonsByLanguage[language].Single(l => l.Label == label);

    [Fact]
    public void TheEnglishSet_IsTenLessons_NumberedInOrder()
    {
        var lessons = LilySharpLanguageServer.Lessons;
        Assert.Equal(10, lessons.Count);
        for (int i = 0; i < lessons.Count; i++)
        {
            Assert.StartsWith($"lesson-{i + 1:D2}-", lessons[i].Label);
            Assert.StartsWith($"Lesson {i + 1} of {lessons.Count} — ", lessons[i].Title);
        }
    }

    [Theory]
    [MemberData(nameof(AllLessons))]
    public void ALesson_CompilesWithNoErrorAndNoWarning_AndRenders(string language, string label)
    {
        // The messages a lesson may raise are its own tasks: an `@todo("…")` on the note to
        // change, which the preview rings in red with the memo beside it (LYS4026 says the
        // same in the Problems list) — and a task may START from a broken bar, whose squiggle
        // the task then mends (lesson 3: `a8@todo("type 4 here") g4 c` is a short bar until
        // the 4 is typed). Any other message is a lesson teaching a mistake.
        var lesson = LessonOf(language, label);
        var tree = SyntaxTree.Parse(lesson.Text);
        var diagnostics = tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Code != DiagnosticCodes.TodoMark && !OnATaskBar(lesson.Text, d.Span.Start)).ToList();
        Assert.True(diagnostics.Count == 0,
            $"{language}/{label}:\n" + string.Join("\n", diagnostics.Select(d => $"  {d.Code}: {d.Message}")));

        var outputs = SvgGenerator.GenerateAll(tree);
        Assert.NotEmpty(outputs);
        Assert.All(outputs, o => Assert.False(string.IsNullOrWhiteSpace(o.Svg)));
        // …and the preview, which draws the memos, renders too (it did not, for a memo in
        // 3/4 or 6/8, until 2026-10-11 — TodoCalloutTests).
        Assert.False(string.IsNullOrWhiteSpace(SvgGenerator.Generate(tree, SvgRenderOptions.Preview())));
    }

    /// <summary>Whether <paramref name="offset"/> lies in a bar (between two `|`) that carries
    /// an <c>@todo</c> — a bar a task starts from.</summary>
    private static bool OnATaskBar(string text, int offset)
    {
        int start = text.LastIndexOf('|', Math.Max(0, offset - 1)) + 1;
        int end = text.IndexOf('|', offset);
        if (end < 0) end = text.Length;
        return text[start..end].Contains("@todo(", StringComparison.Ordinal);
    }

    [Fact]
    public void Lesson2_ItsTwoKeyTasks_TurnItIntoTheTune()
    {
        // The digit task starts from a short bar (`d4`, typed 2 anywhere on the note), and the
        // octave task from a note written an octave high (`g2`, Ctrl+Shift+Down adds the `,`) —
        // which also lifts the note after it, the relative frame carrying the slip. Done, the
        // lesson is clean, and nothing else in it raises a message.
        string lesson = LilySharpLanguageServer.Lessons[1].Text;
        const string digit = "d4@todo(\"caret before d, type 2\")", octave = "g2@todo(\"Ctrl+Shift+Down on g\")";
        Assert.Contains(digit, lesson);
        Assert.Contains(octave, lesson);
        static List<LilySharp.Core.Syntax.Diagnostic> Messages(string text)
        {
            var tree = SyntaxTree.Parse(text);
            return tree.Diagnostics.Concat(SemanticValidation.Run(tree)).Where(d => d.Code != DiagnosticCodes.TodoMark).ToList();
        }
        Assert.Single(Messages(lesson));                                  // the short bar
        string done = lesson.Replace(digit, "d2").Replace(octave, "g,2");
        Assert.Empty(Messages(done));
    }

    [Fact]
    public void Lesson3_TheShortBarItStartsWith_IsMendedByItsTask()
    {
        // `a8@todo("type 4 here") g4 c` is 5/8 in 3/4 — the squiggle is the point; typing the
        // 4 on the a (the digit lands in the duration's slot) leaves a clean score.
        string lesson = LilySharpLanguageServer.Lessons[2].Text;
        const string task = "a8@todo(\"type 4 here\") g4 c";
        Assert.Contains(task, lesson);
        static List<LilySharp.Core.Syntax.Diagnostic> Messages(string text)
        {
            var tree = SyntaxTree.Parse(text);
            return tree.Diagnostics.Concat(SemanticValidation.Run(tree)).Where(d => d.Code != DiagnosticCodes.TodoMark).ToList();
        }
        Assert.Single(Messages(lesson));
        Assert.Empty(Messages(lesson.Replace(task, task.Replace("a8@", "a4@"))));
    }

    [Fact]
    public void Lesson9_ItsChordTask_GivesTheLeftHandAChordWithinAnOctave()
    {
        // The task's chord as its memo writes it. `<g, d' b'>` was G2 D5 B4 — a member's mark
        // moves that member only, from an anchor near the note before (owner's report
        // 2026-10-11: "the octave is unnatural"); `<g b d>,` moves the whole chord: G2 B2 D3.
        string lesson = LilySharpLanguageServer.Lessons[8].Text;
        const string task = "g,2.@todo(\"make it <g b d>,2.\")";
        Assert.Contains(task, lesson);
        var tree = SyntaxTree.Parse(lesson.Replace(task, "<g b d>,2."));
        Assert.Empty(tree.Diagnostics.Concat(SemanticValidation.Run(tree)));
        var score = SvgGenerator.CollectScore(tree, LilySharp.Core.Svg.Collector.RenderSpecParser.Choose(
            LilySharp.Core.Svg.Collector.RenderSpecParser.FindAll(tree), null));
        var lh = score.EnumerateStaves().Select(s => s.Staff).Last();
        var chord = lh.Voices[0].Measures.SelectMany(m => m.Items).OfType<LilySharp.Core.Svg.Model.ChordItem>().Last();
        var positions = chord.Notes.Select(n => n.StaffPosition).ToList();
        Assert.True(positions.Max() - positions.Min() <= 7, string.Join(" ", positions));
    }

    /// <summary>The first staff of notes of a lesson's score <paramref name="score"/> (null:
    /// the default) — the melody, or lesson 10's lead under its chord row — as each head's
    /// staff position and its printed MIDI key.</summary>
    private static (int Position, int Midi)[] FirstStaffOf(string text, string? score)
    {
        var tree = SyntaxTree.Parse(text);
        var spec = LilySharp.Core.Svg.Collector.RenderSpecParser.Choose(
            LilySharp.Core.Svg.Collector.RenderSpecParser.FindAll(tree), score);
        var (_, staff, _) = SvgGenerator.CollectScore(tree, spec).EnumerateStaves().First(s => !s.Staff.IsTextRow);
        return [.. staff.Voices[0].Measures.SelectMany(m => m.Items)
            .OfType<LilySharp.Core.Svg.Model.NoteItem>().Select(n => (n.StaffPosition, n.Midi))];
    }

    [Fact]
    public void Lesson4_ItsLowPrintout_IsTheTuneAStepDown()
    {
        // Owner's request 2026-10-11 ("レッスンに transpose がない"): the printout the comment
        // sends the writer to exists, and prints every head one staff position down (F major
        // for G) — `score low transpose bes,`.
        string text = LilySharpLanguageServer.Lessons[3].Text;
        Assert.Contains("pick `low`", text);
        var main = FirstStaffOf(text, null);
        Assert.NotEmpty(main);
        Assert.Equal(main.Select(n => n.Position - 1), FirstStaffOf(text, "low").Select(n => n.Position));
    }

    [Fact]
    public void Lesson10_TheTrumpetReadsItsKey_AndTheBandSoundsInC()
    {
        // Owner's report 2026-10-11: the full score printed the trumpet in C (`score pitch
        // concert`, the conductor's sounding score), which a trumpeter cannot play from; under
        // `pitch written` it sounded in B♭ against a band in C. Under the file's `pitch concert`
        // the letters are what sounds: the trumpet's staff prints a step up (D major) in the
        // full score and in its own printout, and sounds the letters — the same keys as the same
        // letters on an instrument that does not transpose.
        string text = LilySharpLanguageServer.Lessons[9].Text;
        Assert.Contains("pick `trumpet`", text);
        string untransposed = text.Replace("part lead { instrument trumpet }", "part lead { clef treble }");
        Assert.NotEqual(text, untransposed);
        var asWritten = FirstStaffOf(untransposed, null);
        Assert.NotEmpty(asWritten);
        foreach (var score in new[] { null, "trumpet" })
            Assert.Equal(asWritten.Select(n => n.Position + 1), FirstStaffOf(text, score).Select(n => n.Position));
        // What it SOUNDS is the .mid's (NoteItem.Midi is the printed head's): the lead is the
        // first part, so the first track with notes.
        static int[] Sounding(string book) => [.. new LilySharp.Core.Midi.MidiExporter().Export(SyntaxTree.Parse(book))
            .Tracks.First(t => t.Notes.Count > 0).Notes.Select(n => n.Pitch)];
        Assert.Equal(Sounding(untransposed), Sounding(text));
    }

    [Theory]
    [MemberData(nameof(AllLessons))]
    public void ALessonsTasks_AreShortMemosOnTheNotes(string language, string label)
    {
        // A task is read off the score, beside its red ring: a few words, or it is a
        // paragraph in the margin nobody reads (owner: keep the text as short as possible).
        foreach (Match m in Regex.Matches(LessonOf(language, label).Text, "@todo\\(\"([^\"]*)\"\\)"))
            Assert.True(m.Groups[1].Value.Length <= 28, $"{language}/{label}: memo too long: {m.Groups[1].Value}");
    }

    [Theory]
    [MemberData(nameof(AllLessons))]
    public void ALessonText_IsWrittenAsTheTemplatesAre(string language, string label)
    {
        // Written into the document verbatim (ScoreTemplateCompletionTests has the why):
        // no tab, no CR, no snippet marker, two-space indentation.
        var text = LessonOf(language, label).Text;
        Assert.DoesNotContain("\t", text);
        Assert.DoesNotContain("\r", text);
        Assert.DoesNotContain("$0", text);
        Assert.DoesNotContain("${", text);
        foreach (var line in text.Split('\n').Where(l => l.StartsWith(' ')))
            Assert.True((line.Length - line.TrimStart(' ').Length) % 2 == 0, $"{language}/{label}: {line}");
    }

    [Theory]
    [MemberData(nameof(AllLessons))]
    public void EveryLessonALessonNames_Exists(string language, string label)
    {
        // "NEXT — type `lesson` and pick `lesson-02-notes`": a label that is not in the set
        // sends the writer looking for an item that is not there.
        var labels = LilySharpLanguageServer.Lessons.Select(l => l.Label).ToHashSet();
        foreach (Match m in Regex.Matches(LessonOf(language, label).Text, @"lesson-\d\d-[a-z-]+"))
            Assert.Contains(m.Value, labels);
    }

    [Fact]
    public void ATranslation_TranslatesEnglishLessons_AndKeepsTheirLabels()
    {
        // The label is the lesson's identity in every language: the lessons name each other
        // by it, and LessonsFor swaps a translation in by it.
        var english = LilySharpLanguageServer.Lessons.Select(l => l.Label).ToHashSet();
        foreach (var (language, set) in LilySharpLanguageServer.LessonsByLanguage)
            Assert.All(set, l => Assert.True(english.Contains(l.Label), $"{language}/{l.Label} has no English original"));
    }

    [Fact]
    public void ALocaleWithNoTranslation_GetsTheEnglishSet()
    {
        Assert.Equal(
            LilySharpLanguageServer.Lessons.Select(l => l.Text),
            LilySharpLanguageServer.LessonsFor("xx-yy").Select(l => l.Text));
        Assert.Equal(
            LilySharpLanguageServer.Lessons.Select(l => l.Text),
            LilySharpLanguageServer.LessonsFor(null).Select(l => l.Text));
    }

    // ── which items the top-level list offers ──

    private static string[] LessonLabelsFor(string text) =>
        LilySharpLanguageServer.GetTopLevelCompletions(text, text.Length, PositionAtEnd(text))
            .Items.Select(i => i.Label!).Where(l => l.StartsWith("lesson-")).ToArray();

    [Fact]
    public void OnAnEmptyWord_OnlyTheFirstLessonIsOffered()
    {
        Assert.Equal([LilySharpLanguageServer.Lessons[0].Label], LessonLabelsFor(""));
        Assert.Equal([LilySharpLanguageServer.Lessons[0].Label], LessonLabelsFor("title \"x\"\n\n"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("title \"x\"\n\n", true)]
    [InlineData("l", false)]
    [InlineData("lesson", false)]
    [InlineData("part", false)]
    public void AListThatHeldLessonsBack_SaysItIsIncomplete(string text, bool incomplete)
    {
        // Ctrl+Space on an empty line offers lesson 1 alone; typing `lesson` then filters THAT
        // list in the client unless the list says it is incomplete — which is how lesson 1's
        // NEXT line found lesson 1 and nothing else (owner report 2026-10-11).
        var list = LilySharpLanguageServer.GetTopLevelCompletions(text, text.Length, PositionAtEnd(text));
        Assert.Equal(incomplete, list.IsIncomplete);
    }

    [Theory]
    [InlineData("l")]
    [InlineData("les")]
    [InlineData("lesson")]
    [InlineData("lesson-0")]
    [InlineData("lea")]
    [InlineData("Learn")]
    public void OnTheWayToLessonOrLearn_EveryLessonIsOffered(string typed)
    {
        Assert.Equal(LilySharpLanguageServer.Lessons.Select(l => l.Label), LessonLabelsFor(typed));
    }

    [Theory]
    [InlineData("t")]
    [InlineData("ly")]
    [InlineData("part")]
    public void OnAnyOtherWord_NoLessonIsOffered(string typed)
    {
        Assert.Empty(LessonLabelsFor(typed));
    }

    [Fact]
    public void ALessonItem_ReplacesTheFileTheWayATemplateDoes()
    {
        const string text = "lesson";
        var lesson = LilySharpLanguageServer.Lessons[1];
        var item = LilySharpLanguageServer.GetTopLevelCompletions(text, text.Length, PositionAtEnd(text))
            .Items.Single(i => i.Label == lesson.Label);

        Assert.Equal("lilysharp.applyScoreTemplate", item.Command!.CommandIdentifier);
        Assert.Equal(lesson.Label, item.Command.Arguments![0]);
        Assert.Equal(lesson.Text, item.Command.Arguments[1]);
        Assert.Equal("lesson", item.TextEdit!.NewText);   // accepting it changes nothing by itself
        Assert.Equal("", item.InsertText);
        Assert.StartsWith(lesson.Title, item.Detail);
    }

    // ── lesson 1 says what Ctrl+Space offers; the real completion path must agree ──

    private static CompletionItem[] CompletionAt(string text, int offset)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///lesson.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        })?.Items ?? [];
    }

    private static string Lesson1 => LilySharpLanguageServer.Lessons[0].Text;

    /// <summary>The offset of the empty line right after <paramref name="marker"/>'s line
    /// block — where a STEP comment tells the writer to press Ctrl+Space.</summary>
    private static int EmptyLineAfter(string text, string marker)
    {
        int at = text.IndexOf(marker, System.StringComparison.Ordinal);
        Assert.True(at >= 0, $"lesson 1 no longer says \"{marker}\"");
        int blank = text.IndexOf("\n\n", at, System.StringComparison.Ordinal);
        if (text.IndexOf("\n  \n", at, System.StringComparison.Ordinal) is var indented and >= 0
            && indented < blank)
            return indented + 3;
        return blank + 1;
    }

    private static string[] Labels(CompletionItem[] items) => items.Select(i => i.Label!).ToArray();

    [Fact]
    public void Lesson1_Step1_TheTopOfTheFileOffersKeyAndTempo_ThenTheTonicsAndModes()
    {
        int at = EmptyLineAfter(Lesson1, "STEP 1");
        Assert.Contains("key", Labels(CompletionAt(Lesson1, at)));
        Assert.Contains("tempo", Labels(CompletionAt(Lesson1, at)));

        string withKey = Lesson1.Insert(at, "key ");
        Assert.Contains("g", Labels(CompletionAt(withKey, at + 4)));
        string withTonic = Lesson1.Insert(at, "key g ");
        Assert.Contains("major", Labels(CompletionAt(withTonic, at + 6)));
    }

    [Fact]
    public void Lesson1_Step2_APartOffersInstrument_ThenCello_WhichBringsTheBassClef()
    {
        int at = EmptyLineAfter(Lesson1, "STEP 2");
        Assert.Contains("instrument", Labels(CompletionAt(Lesson1, at)));
        string withInstrument = Lesson1.Insert(at, "instrument ");
        Assert.Contains("cello", Labels(CompletionAt(withInstrument, at + "instrument ".Length)));
        // "The bass clef appears by itself": the part writes no clef of its own, so the
        // preset's is the one drawn.
        Assert.DoesNotContain(Lesson1[Lesson1.IndexOf("part melody", System.StringComparison.Ordinal)..
                                       Lesson1.IndexOf("section A", System.StringComparison.Ordinal)]
                                .Split('\n'), l => !l.TrimStart().StartsWith("//") && l.Contains("clef"));
        Assert.Equal(LilySharp.Core.Svg.Model.ClefType.Bass,
            LilySharp.Core.Svg.Model.InstrumentDefaults.GetDefaults("cello").Clef);
    }

    private const string Step3Mark = "@todo(\"STEP 3: delete this @todo\")";

    [Fact]
    public void Lesson1_Step3_DeletingTheFirstRing_LeavesOneMarkFewer_AndTheTuneAsItWas()
    {
        // The writer's first edit is to take a mark away: the ring on the first note goes,
        // and nothing else changes.
        Assert.StartsWith("e4" + Step3Mark, Lesson1[Lesson1.IndexOf("e4@todo", System.StringComparison.Ordinal)..]);
        static int Marks(string text)
        {
            var tree = SyntaxTree.Parse(text);
            return SemanticValidation.Run(tree).Count(d => d.Code == DiagnosticCodes.TodoMark);
        }
        Assert.Equal(Marks(Lesson1) - 1, Marks(Lesson1.Replace(Step3Mark, "")));
        Assert.True(Step3Mark.Length - "@todo(\"\")".Length <= 28);
    }

    [Fact]
    public void Lesson1_Step4_WhereTheMarkWas_AnAtListsDynamicsArticulationsAndFermata()
    {
        // The writer deletes the mark first, then types @ right after the e (the comment quotes
        // the mark too, so the one in the music is the last).
        int at = Lesson1.LastIndexOf("e@todo(\"STEP 4\")", System.StringComparison.Ordinal) + "e".Length;
        Assert.True(at > Lesson1.IndexOf(Step3Mark, System.StringComparison.Ordinal), "the rings run in step order");
        string emptied = Lesson1.Remove(at, "@todo(\"STEP 4\")".Length);
        var labels = Labels(CompletionAt(emptied.Insert(at, "@"), at + 1));
        Assert.Contains("mf", labels);
        Assert.Contains("p", labels);
        Assert.Contains("staccato", labels);
        Assert.Contains("accent", labels);
        Assert.Contains("fermata", labels);
    }

    [Fact]
    public void Lesson1_Step5_WhereTheRingedRestWas_Cmaj7IsOffered_InCMajorAndAfterStep1sGMajor()
    {
        // Cmaj7, not C: a label nobody can take for the note c. It is the key's IV7 in
        // G major too, so step 1 having changed the key does not take it away. The writer
        // deletes `r1@todo("STEP 5")` and asks there.
        const string rest = "r1@todo(\"STEP 5\")";
        Assert.Contains(rest, Lesson1);
        int at = Lesson1.LastIndexOf(rest, System.StringComparison.Ordinal);   // the comment quotes it first
        string emptied = Lesson1.Remove(at, rest.Length);
        Assert.Equal("<c e g b>", CompletionAt(emptied, at).Single(i => i.Label == "Cmaj7").InsertText);

        string inG = emptied.Insert(EmptyLineAfter(emptied, "STEP 1"), "key g major");
        int inGAt = at + "key g major".Length;
        Assert.Equal("<c e g b>", CompletionAt(inG, inGAt).Single(i => i.Label == "Cmaj7").InsertText);
    }

    [Fact]
    public void Lesson1_Step6_TheFormOffersTheSectionsAndTheRepeatSigns()
    {
        int at = Lesson1.IndexOf("form { ~A", System.StringComparison.Ordinal) + "form { ~A".Length;
        var labels = Labels(CompletionAt(Lesson1, at + 1));
        Assert.Contains("~A", labels);
        Assert.Contains("|:", labels);
        Assert.Contains(":|", labels);
    }

    [Fact]
    public void Lesson1_EveryStepDoneAsWritten_StillCompilesClean()
    {
        // The lesson is done by hand, step after step, on one file; each step's result has to
        // be a score that compiles with no message, or the next step starts from a squiggle.
        string text = Lesson1;
        text = text.Insert(EmptyLineAfter(text, "STEP 1"), "key g major");
        text = text.Insert(EmptyLineAfter(text, "STEP 2"), "instrument cello");
        text = text.Replace(Step3Mark, "");
        int step4 = text.LastIndexOf("e@todo(\"STEP 4\")", System.StringComparison.Ordinal) + 1;
        text = text.Remove(step4, "@todo(\"STEP 4\")".Length).Insert(step4, "@mf");
        int rest = text.LastIndexOf("r1@todo(\"STEP 5\")", System.StringComparison.Ordinal);
        text = text.Remove(rest, "r1@todo(\"STEP 5\")".Length).Insert(rest, "<c e g b>");
        Assert.Contains("form { |: ~A :| }", Lesson1);   // the sample step 6 shows
        text = text.Replace("form { ~A }", "form { |: ~A :| }");

        var tree = SyntaxTree.Parse(text);
        var diagnostics = tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Code != DiagnosticCodes.TodoMark).ToList();   // the rings may stay
        Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        // …and "DONE": removing every mark leaves a clean score too.
        string done = Regex.Replace(text, "@todo\\(\"[^\"]*\"\\)", "");
        var doneTree = SyntaxTree.Parse(done);
        Assert.Empty(doneTree.Diagnostics.Concat(SemanticValidation.Run(doneTree)));
    }

    private static Position PositionAtEnd(string text)
    {
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, text.Length);
        return new Position(line, character);
    }
}
