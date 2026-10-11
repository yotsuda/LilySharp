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
        // The one message a lesson may raise is its own tasks: an `@todo("…")` on the note to
        // change, which the preview rings in red with the memo beside it (LYS4026 says the
        // same in the Problems list). Anything else is a lesson teaching a mistake.
        var lesson = LessonOf(language, label);
        var tree = SyntaxTree.Parse(lesson.Text);
        var diagnostics = tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Code != DiagnosticCodes.TodoMark).ToList();
        Assert.True(diagnostics.Count == 0,
            $"{language}/{label}:\n" + string.Join("\n", diagnostics.Select(d => $"  {d.Code}: {d.Message}")));

        var outputs = SvgGenerator.GenerateAll(tree);
        Assert.NotEmpty(outputs);
        Assert.All(outputs, o => Assert.False(string.IsNullOrWhiteSpace(o.Svg)));
        // …and the preview, which draws the memos, renders too (it did not, for a memo in
        // 3/4 or 6/8, until 2026-10-11 — TodoCalloutTests).
        Assert.False(string.IsNullOrWhiteSpace(SvgGenerator.Generate(tree, SvgRenderOptions.Preview())));
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

    [Fact]
    public void Lesson1_Step3_AnAtAfterTheFirstNote_ListsDynamicsArticulationsAndFermata()
    {
        int at = Lesson1.IndexOf("e4@todo(\"STEP 3\")", System.StringComparison.Ordinal) + "e4".Length;
        var labels = Labels(CompletionAt(Lesson1.Insert(at, "@"), at + 1));
        Assert.Contains("mf", labels);
        Assert.Contains("p", labels);
        Assert.Contains("staccato", labels);
        Assert.Contains("accent", labels);
        Assert.Contains("fermata", labels);
    }

    [Fact]
    public void Lesson1_Step4_WhereTheRingedRestWas_Cmaj7IsOffered_InCMajorAndAfterStep1sGMajor()
    {
        // Cmaj7, not C: a label nobody can take for the note c. It is the key's IV7 in
        // G major too, so step 1 having changed the key does not take it away. The writer
        // deletes `r1@todo("STEP 4")` and asks there.
        const string rest = "r1@todo(\"STEP 4\")";
        Assert.Contains(rest, Lesson1);
        string emptied = Lesson1.Replace(rest, "");
        int at = Lesson1.IndexOf(rest, System.StringComparison.Ordinal);
        Assert.Equal("<c e g b>", CompletionAt(emptied, at).Single(i => i.Label == "Cmaj7").InsertText);

        string inG = emptied.Insert(EmptyLineAfter(emptied, "STEP 1"), "key g major");
        int inGAt = at + "key g major".Length;
        Assert.Equal("<c e g b>", CompletionAt(inG, inGAt).Single(i => i.Label == "Cmaj7").InsertText);
    }

    [Fact]
    public void Lesson1_Step5_TheFormOffersTheSectionsAndTheRepeatSigns()
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
        int first = text.IndexOf("e4@todo(\"STEP 3\")", System.StringComparison.Ordinal) + "e4".Length;
        text = text.Insert(first, "@mf");
        text = text.Replace("r1@todo(\"STEP 4\")", "<c e g b>");
        Assert.Contains("form { |: ~A :| }", Lesson1);   // the sample step 5 shows
        text = text.Replace("form { ~A }", "form { |: ~A :| }");

        var tree = SyntaxTree.Parse(text);
        var diagnostics = tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Code != DiagnosticCodes.TodoMark).ToList();   // the rings may stay
        Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Lesson1_TheMessageExercise_DoesRaiseAMessage()
    {
        // "delete one `e` from `e e e2`: a squiggle appears".
        Assert.Contains("e@todo(\"MESSAGES\") e e2", Lesson1);
        var tree = SyntaxTree.Parse(Lesson1.Replace("e@todo(\"MESSAGES\") e e2", "e e2"));
        Assert.NotEmpty(tree.Diagnostics.Concat(SemanticValidation.Run(tree)).Where(d => d.Code != DiagnosticCodes.TodoMark));
    }

    private static Position PositionAtEnd(string text)
    {
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, text.Length);
        return new Position(line, character);
    }
}
