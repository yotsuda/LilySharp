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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Lsp;
using Xunit;
using CompletionContext = LilySharp.Lsp.Protocol.CompletionContext;
using CompletionItem = LilySharp.Lsp.Protocol.CompletionItem;
using CompletionParams = LilySharp.Lsp.Protocol.CompletionParams;
using DidOpenTextDocumentParams = LilySharp.Lsp.Protocol.DidOpenTextDocumentParams;
using Position = LilySharp.Lsp.Protocol.Position;
using TextDocumentIdentifier = LilySharp.Lsp.Protocol.TextDocumentIdentifier;
using TextDocumentItem = LilySharp.Lsp.Protocol.TextDocumentItem;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// Every word a completion list offers, typed one key at a time where the list offers it.
/// The vocabulary nets (<c>CompletionAuditTests</c>, <c>AtCompletionParsesTests</c>, …) ask
/// whether a word is IN the list; three owner reports of 2026-10-28 were words in the list
/// that typing never reached: <c>@8va</c> (smart typing took the 8 as the note's duration),
/// <c>@!rit</c> (the position after <c>@!</c> offered the note names) and, found by the audit
/// these nets came from, <c>paper { size a5 }</c> (the 5 read as a duration).
/// </summary>
/// <remarks>
/// The server half is here: VS Code asks again on a trigger character or while the list is
/// incomplete, and filters by the default word at the caret (language-configuration.json sets no
/// wordPattern). The smart-typing half is the extension's: this file writes the places and words
/// to <c>editors/vscode/test/completion-typing.json</c>, which <c>completionTyping.test.ts</c>
/// types through the keystroke plans; <see cref="TheTypingFixture_IsCurrent"/> keeps it in step
/// (regenerate with <c>LILYSHARP_UPDATE_DOCS=1</c>).
/// </remarks>
[Trait("Category", "Unit")]
public class CompletionTypingAuditTests
{
    // LF whatever the file's own line endings, so the places below find their text.
    private static readonly string Doc = DocText.ReplaceLineEndings("\n");

    private const string DocText = """
        title "T"
        time 4/4
        key c major
        part p { clef treble }
        part b { instrument bass }
        phrase riff { c'4 d' e' f' | }
        chords prog { section A { C1 | } }
        lyrics words { section A { la la la la } }
        section A {
          p { c'4 d' e' f' | ▣ }
          b { c4 d e f | }
        }
        form { A }
        score { staff p  staff b }
        """;

    private static string Plain => Doc.Replace("▣", "");
    private static string At(string from, string to) => Plain.Replace(from, to);

    /// <summary>The places a word is typed, ▮ at the caret, with the character that opens the
    /// list there (null: Ctrl+Space). A template not starting with '#' is music, put at ▣.</summary>
    private static readonly (string Name, string Template, string? Trigger)[] Sites =
    [
        ("music", "c'4 ▮", " "),
        ("music-at", "c'4@▮", "@"),
        ("music-at-bang", "c'4@rit d'4@!▮", "!"),
        ("music-clef", "c'4 clef ▮", " "),
        ("music-key", "c'4 key ▮", " "),
        ("music-time", "c'4 time ▮", " "),
        ("music-tempo", "c'4 tempo ▮", " "),
        ("music-repeat", "repeat ▮", " "),
        ("music-voice", "c'4 voice { c'4 } { ▮ }", " "),
        ("top", "#▮\n" + Plain, null),
        ("part-body", "#part q { ▮ }\n" + Plain, null),
        ("part-clef", "#part q { clef ▮ }\n" + Plain, " "),
        ("part-instrument", "#part q { instrument ▮ }\n" + Plain, " "),
        ("part-tuning", "#part q { tuning ▮ }\n" + Plain, " "),
        ("part-transposition", "#part q { transposition ▮ }\n" + Plain, " "),
        ("part-pitch", "#part q { pitch ▮ }\n" + Plain, " "),
        ("part-pedal", "#part q { pedal ▮ }\n" + Plain, " "),
        ("top-key", "#key ▮\n" + Plain, " "),
        ("top-time", "#time ▮\n" + Plain, " "),
        ("top-octave", "#octave ▮\n" + Plain, " "),
        ("top-pitch", "#pitch ▮\n" + Plain, " "),
        ("section-head", "#" + At("section A {\n", "section A {\n  ▮\n"), null),
        ("form", "#" + At("form { A }", "form { A ▮ }"), " "),
        ("score-body", "#" + At("score { staff p  staff b }", "score { staff p ▮ }"), " "),
        ("score-staff", "#" + At("score { staff p  staff b }", "score { staff ▮ }"), " "),
        ("score-tab", "#" + At("score { staff p  staff b }", "score { tab ▮ }"), " "),
        ("score-staff-as", "#" + At("score { staff p  staff b }", "score { staff p as ▮ }"), " "),
        ("score-tab-as", "#" + At("score { staff p  staff b }", "score { tab bass5 p as ▮ }"), " "),
        ("score-chords", "#" + At("score { staff p  staff b }", "score { chords ▮ }"), " "),
        ("layout", "#layout { ▮ }\n" + Plain, null),
        ("paper", "#paper { ▮ }\n" + Plain, null),
        ("fonts", "#fonts { ▮ }\n" + Plain, null),
        ("chords-body", "#" + At("chords prog { section A { C1 | } }", "chords prog { section A { C1 | ▮ } }"), " "),
        ("chords-for", "#" + At("chords prog {", "chords prog for ▮ {"), " "),
        ("lyrics-sings", "#" + At("score { staff p  staff b }", "score { staff p  lyrics words sings ▮ }"), " "),
        ("check", "#check ▮\n" + Plain, " "),
        ("check-parts", "#check voiceLeading ▮\n" + Plain, " "),
    ];

    /// <summary>The places whose words take a value or an argument after them: each word, and a
    /// space (or for an annotation a '('), is another place.</summary>
    private static readonly HashSet<string> Deepened =
        ["music", "music-at", "top", "part-body", "layout", "paper", "fonts", "section-head", "score-body"];

    // VS Code's default word definition.
    private static readonly Regex DefaultWord = new(@"(-?\d*\.\d\w*)|([^`~!@#$%^&*()\-=+\[{\]}\\|;:'"",.<>/?\s]+)");

    private static string WordAtCaret(string text, int caret)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        string line = text[lineStart..caret];
        foreach (Match m in DefaultWord.Matches(line))
            if (m.Index + m.Length == line.Length)
                return m.Value;
        return "";
    }

    /// <summary>VS Code's filter, near enough: the word's characters in order, the first at the
    /// start of the filter text or of a part of it.</summary>
    private static bool Matches(string word, string filter)
    {
        if (word.Length == 0) return true;
        string f = filter.ToLowerInvariant(), w = word.ToLowerInvariant();
        for (int s = 0; s < f.Length; s++)
        {
            bool start = s == 0 || !char.IsLetterOrDigit(filter[s - 1]) || char.IsUpper(filter[s]);
            if (!start || f[s] != w[0]) continue;
            int j = 1;
            for (int k = s + 1; k < f.Length && j < w.Length; k++)
                if (f[k] == w[j]) j++;
            if (j == w.Length) return true;
        }
        return false;
    }

    /// <summary>The word an item starts with, as the writer types it — or null for a row that is
    /// not typed as its label (a placeholder such as <c>N</c>, filtered on the digits it stands for).</summary>
    private static string? LeadWord(CompletionItem i)
    {
        var m = Regex.Match(i.Label ?? "", @"^[A-Za-z0-9][A-Za-z0-9_^\-]*");
        return m.Success ? m.Value : null;
    }

    private static string? TypedWord(CompletionItem i)
    {
        var m = Regex.Match(i.Label ?? "", @"^[A-Za-z0-9][A-Za-z0-9_^\-]*");
        if (!m.Success) return null;
        if (!string.IsNullOrEmpty(i.FilterText) && !i.FilterText.Contains(m.Value, StringComparison.OrdinalIgnoreCase))
            return null;
        return m.Value;
    }

    private sealed class Probe
    {
        private readonly LilySharpLanguageServer _server = new(Stream.Null, Stream.Null);
        private int _n;

        public (CompletionItem[] Items, bool Incomplete)? Ask(string text, int caret, string? trigger)
        {
            var uri = new Uri($"file:///typing{++_n}.lys");
            _server.DidOpen(new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
            });
            var (line, ch) = LilySharpLanguageServer.GetLineAndCharacter(text, caret);
            var list = _server.Completion(new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(line, ch),
                Context = trigger == null
                    ? new CompletionContext { TriggerKind = 1 }
                    : new CompletionContext { TriggerKind = 2, TriggerCharacter = trigger },
            });
            return list == null ? null : (list.Items, list.IsIncomplete);
        }
    }

    private sealed record Walk(List<(string Name, string Marked)> Places, List<(string Place, string Word)> Words, List<string> Lost);

    private static Walk WalkAll()
    {
        var probe = new Probe();
        var walk = new Walk([], [], []);
        var queue = new Queue<(string Name, string Marked, string? Trigger, int Depth)>(
            Sites.Select(s => (s.Name, s.Template.StartsWith('#') ? s.Template[1..] : Doc.Replace("▣", s.Template), s.Trigger, 0)));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var (name, marked, trigger, depth) = queue.Dequeue();
            int caret = marked.IndexOf('▮');
            string text = marked.Remove(caret, 1);
            var first = probe.Ask(text, caret, trigger) ?? probe.Ask(text, caret, null);
            if (first is not { } list || list.Items.Length == 0)
            {
                if (depth == 0) walk.Lost.Add($"{name}: no list where it should open");
                continue;
            }
            // A derived place answering a list already walked (the music list again) adds nothing.
            if (!seen.Add(string.Join("|", list.Items.Select(i => i.Label).OrderBy(l => l, StringComparer.Ordinal))) && depth > 0)
                continue;
            walk.Places.Add((name, marked));
            var words = list.Items.Select(TypedWord).Where(w => w != null).Select(w => w!).Distinct().ToList();
            if (depth == 0 && Deepened.Contains(name))
                foreach (var w in words.Where(w => w.Length > 1))
                {
                    queue.Enqueue(($"{name}>{w}", marked.Replace("▮", w + " ▮"), " ", 1));
                    if (name == "music-at")
                        queue.Enqueue(($"{name}>{w}(", marked.Replace("▮", w + "(▮)"), null, 1));
                }
            foreach (var word in words)
            {
                walk.Words.Add((name, word));
                var current = list;
                for (int k = 1; k <= word.Length; k++)
                {
                    string typed = text.Insert(caret, word[..k]);
                    int at = caret + k;
                    char c = word[k - 1];
                    bool isTrigger = c is '\\' or '@' or ' ' or '.' or '!';
                    if (isTrigger || current.Incomplete)
                    {
                        if (probe.Ask(typed, at, isTrigger ? c.ToString() : null) is not { } again)
                        {
                            walk.Lost.Add($"{name}: {word} at '{word[..k]}' — the server answers nothing");
                            break;
                        }
                        current = again;
                    }
                    var item = current.Items.FirstOrDefault(i => LeadWord(i) == word);
                    if (item == null)
                    {
                        walk.Lost.Add($"{name}: {word} at '{word[..k]}' — not in the list");
                        break;
                    }
                    if (!Matches(WordAtCaret(typed, at), string.IsNullOrEmpty(item.FilterText) ? item.Label! : item.FilterText))
                    {
                        walk.Lost.Add($"{name}: {word} at '{word[..k]}' — the editor's filter drops it");
                        break;
                    }
                }
            }
        }
        return walk;
    }

    private static readonly Lazy<Walk> All = new(WalkAll);

    [Fact]
    public void EveryOfferedWord_StaysInTheList_KeyByKey()
    {
        Assert.True(All.Value.Words.Count > 900, $"only {All.Value.Words.Count} words walked — a place stopped answering");
        Assert.True(All.Value.Lost.Count == 0, string.Join("\n", All.Value.Lost));
    }

    [Fact]
    public void TheSpanEndsAndThePaperSizes_AreAmongTheWalkedWords()
    {
        // The three reports, so the walk cannot drift away from them.
        Assert.Contains(("music-at", "8va"), All.Value.Words);
        Assert.Contains(("music-at-bang", "rit"), All.Value.Words);
        Assert.Contains(All.Value.Words, w => w.Place == "paper>size" && w.Word == "a5");
    }

    private static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s,
        new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private const string FixtureRelative = "editors/vscode/test/completion-typing.json";

    private static string Fixture()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"note\": \"Generated by LilySharp.Tests CompletionTypingAuditTests (LILYSHARP_UPDATE_DOCS=1); do not edit.\",\n  \"places\": {\n");
        var places = All.Value.Places;
        for (int i = 0; i < places.Count; i++)
            sb.Append("    ").Append(Json(places[i].Name)).Append(": ")
              .Append(Json(places[i].Marked.Replace("▮", "‸")))
              .Append(i + 1 < places.Count ? ",\n" : "\n");
        sb.Append("  },\n  \"words\": [\n");
        var words = All.Value.Words;
        for (int i = 0; i < words.Count; i++)
            sb.Append("    [").Append(Json(words[i].Place)).Append(", ")
              .Append(Json(words[i].Word)).Append(i + 1 < words.Count ? "],\n" : "]\n");
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    [Fact]
    public void TheTypingFixture_IsCurrent()
    {
        var path = Path.Combine(CollectResumeTests.FindRepoRoot(), FixtureRelative.Replace('/', Path.DirectorySeparatorChar));
        string want = Fixture();
        string now = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
        if (now != want && Environment.GetEnvironmentVariable("LILYSHARP_UPDATE_DOCS") == "1")
        {
            File.WriteAllText(path, want, new UTF8Encoding(false));
            return;
        }
        Assert.True(now == want, $"{FixtureRelative} is stale: regenerate it with LILYSHARP_UPDATE_DOCS=1 (the completion lists changed)");
    }
}
