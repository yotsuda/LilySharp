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
        var lesson = LessonOf(language, label);
        var tree = SyntaxTree.Parse(lesson.Text);
        var diagnostics = tree.Diagnostics.Concat(SemanticValidation.Run(tree)).ToList();
        Assert.True(diagnostics.Count == 0,
            $"{language}/{label}:\n" + string.Join("\n", diagnostics.Select(d => $"  {d.Code}: {d.Message}")));

        var outputs = SvgGenerator.GenerateAll(tree);
        Assert.NotEmpty(outputs);
        Assert.All(outputs, o => Assert.False(string.IsNullOrWhiteSpace(o.Svg)));
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
    public void Lesson1_Step2_APartOffersInstrument_ThenFlute()
    {
        int at = EmptyLineAfter(Lesson1, "STEP 2");
        Assert.Contains("instrument", Labels(CompletionAt(Lesson1, at)));
        string withInstrument = Lesson1.Insert(at, "instrument ");
        Assert.Contains("flute", Labels(CompletionAt(withInstrument, at + "instrument ".Length)));
    }

    [Fact]
    public void Lesson1_Step4_AfterTheLastBarline_TheChordCWritesCEG()
    {
        int at = Lesson1.IndexOf("c1 |.", System.StringComparison.Ordinal) + "c1 |.".Length;
        var c = CompletionAt(Lesson1, at).Single(i => i.Label == "C");
        Assert.Equal("<c e g>", c.InsertText);
    }

    [Fact]
    public void Lesson1_Step3_AnAtAfterTheFirstNote_ListsDynamicsArticulationsAndFermata()
    {
        int at = Lesson1.IndexOf("e4 d c d", System.StringComparison.Ordinal) + "e4".Length;
        var labels = Labels(CompletionAt(Lesson1.Insert(at, "@"), at + 1));
        Assert.Contains("mf", labels);
        Assert.Contains("p", labels);
        Assert.Contains("staccato", labels);
        Assert.Contains("accent", labels);
        Assert.Contains("fermata", labels);
    }

    [Fact]
    public void Lesson1_Step5_TheFormOffersTheSectionsAndTheRepeatSigns()
    {
        int at = Lesson1.IndexOf("form { ~A", System.StringComparison.Ordinal) + "form { ~A".Length;
        var labels = Labels(CompletionAt(Lesson1, at + 1));
        Assert.Contains("A", labels);
        Assert.Contains("~A", labels);
        Assert.Contains("|:", labels);
    }

    private static Position PositionAtEnd(string text)
    {
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, text.Length);
        return new Position(line, character);
    }
}
