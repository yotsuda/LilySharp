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
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using LilySharp.Lsp.Protocol;
using StreamJsonRpc;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LilySharp.Core.Rendering;
using SkiaSharp;
using LspRange = LilySharp.Lsp.Protocol.Range;
using LspDiagnosticSeverity = LilySharp.Lsp.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    // ========== Source-generated regexes ==========
    // Built at compile time (no runtime parse/JIT), reused across every completion pass.

    [GeneratedRegex(@"\bsection\s+(\w+)")]
    private static partial Regex SectionRefRegex();

    [GeneratedRegex(@"\bsection\s+(\w+)\s*\{")]
    private static partial Regex SectionDeclRegex();

    [GeneratedRegex(@"\bkey\s+([a-gA-G](?:is|es|isis|eses)?)\s+([A-Za-z]+)")]
    private static partial Regex KeyDeclRegex();

    [GeneratedRegex(@"^[a-g](is|es|isis|eses)?$")]
    private static partial Regex BareNoteNameRegex();

    // A chord/arpeggio member pitch token — letter + glued accidental + octave
    // marks (cis''). Used by the @chord completion's auto-name check.
    [GeneratedRegex(@"^([a-g])(isis|eses|is|es)?[',]*$")]
    private static partial Regex ChordMemberPitchRegex();

    [GeneratedRegex(@"\bclef\s*:?\s*percussion\b")]
    private static partial Regex PercussionClefRegex();

    // A `KEYWORD name {` declaration: group 1 is the keyword, group 2 the name. A caller
    // matches ANY declaration and filters by keyword, so the pattern stays constant (and
    // source-generated) instead of embedding a runtime keyword.
    [GeneratedRegex(@"\b(\w+)\s+(\w+)\s*\{")]
    private static partial Regex DeclaredNameRegex();

    // A lyrics track's declaration: `lyrics NAME {` and `lyrics NAME language L {` —
    // group 1 is the track name (LyricsTrackNamesOf).
    [GeneratedRegex(@"\blyrics\s+(\w+)(?:\s+language\s+\w+)?\s*\{")]
    private static partial Regex LyricsTrackDeclarationRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();

    // `override [once] Grob.property = <partial>` at the END of a line: group 1 is the
    // property (after the last '.' before '='). The partial value carries no whitespace,
    // so the caret is at the value position whether it sits just after '= ' or mid-value.
    [GeneratedRegex(@"\boverride\b.*?\.([A-Za-z][\w-]*)\s*=\s*[^\s=]*$")]
    private static partial Regex OverrideValueRegex();

    // ========== Completion ==========

    /// <summary>LSP <c>CompletionTriggerKind.TriggerCharacter</c>.</summary>
    private const int CompletionTriggerKindTriggerCharacter = 2;

    [JsonRpcMethod(Methods.TextDocumentCompletionName, UseSingleObjectParameterDeserialization = true)]
    public Task<CompletionList?> CompletionAsync(CompletionParams @params, CancellationToken token)
        => OffDispatch(() => Completion(@params), token);

    public CompletionList? Completion(CompletionParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        return doc == null ? null : CompletionIn(doc, @params);
    }

    /// <summary>
    /// The completion of a text the client holds outside any editor — a line edited beside the
    /// scan, spliced into its file (owner's request 2026-10-27: completion in that field) — at an
    /// offset, each item made plain for a text field: the text it puts in (a snippet's
    /// placeholders as their defaults), the offsets it replaces, where the caret goes. Read-only.
    /// </summary>
    [JsonRpcMethod("lilysharp/completeText", UseSingleObjectParameterDeserialization = true)]
    public Task<CompleteTextResponse> CompleteTextAsync(CompleteTextParams @params, CancellationToken token)
        => OffDispatch(() => CompleteText(@params), token);

    internal CompleteTextResponse CompleteText(CompleteTextParams @params)
    {
        var text = @params.Text ?? "";
        int offset = Math.Clamp(@params.Offset, 0, text.Length);
        var doc = new Document(new Uri("untitled:lilysharp-complete-text"), text, SyntaxTree.Parse(text), 0);
        var starts = LineStartsOf(text);
        int line = Array.BinarySearch(starts, offset);
        if (line < 0)
            line = ~line - 1;
        var list = CompletionIn(doc, new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = doc.Uri },
            Position = new Position { Line = line, Character = offset - starts[line] },
            Context = @params.Trigger is { Length: > 0 } t
                ? new LilySharp.Lsp.Protocol.CompletionContext { TriggerKind = CompletionTriggerKindTriggerCharacter, TriggerCharacter = t }
                : new LilySharp.Lsp.Protocol.CompletionContext { TriggerKind = 1 },
        });
        // The word before the caret, as VS Code takes it when an item names no range (its default
        // word: a run of anything but punctuation and space).
        int word = offset;
        while (word > 0 && !"`~!@#$%^&*()-=+[{]}\\|;:'\",.<>/? \t\r\n".Contains(text[word - 1]))
            word--;
        var response = new CompleteTextResponse();
        foreach (var item in list?.Items ?? [])
        {
            int start = word, end = offset;
            string raw = item.InsertText ?? item.Label;
            if (item.TextEdit is { } edit)
            {
                start = DocumentManager.GetOffset(text, edit.Range.Start);
                end = DocumentManager.GetOffset(text, edit.Range.End);
                raw = edit.NewText;
            }
            var (insert, caret) = item.InsertTextFormat == InsertTextFormat.Snippet ? PlainSnippet(raw) : (raw, raw.Length);
            response.Items.Add(new CompleteTextItem
            {
                Label = item.Label, Detail = item.Detail, Kind = (int?)item.Kind,
                Filter = item.FilterText ?? item.Label, Sort = item.SortText ?? item.Label,
                Insert = insert, Start = start, End = end, Caret = caret,
                Again = item.Command?.CommandIdentifier == "editor.action.triggerSuggest",
            });
        }
        return response;
    }

    /// <summary>A snippet as plain text — each placeholder its default (a choice its first), each
    /// tab stop nothing — and where its first stop is (else <c>$0</c>, else its end).</summary>
    internal static (string Text, int Caret) PlainSnippet(string snippet)
    {
        var sb = new StringBuilder();
        var stops = new SortedDictionary<int, int>();
        void Stop(int n) { if (!stops.ContainsKey(n)) stops[n] = sb.Length; }
        void Walk(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length && s[i + 1] is '$' or '}' or '\\' or ',' or '|')
                {
                    sb.Append(s[++i]);
                }
                else if (c == '$' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
                {
                    int j = i + 1;
                    while (j < s.Length && char.IsDigit(s[j])) j++;
                    Stop(int.Parse(s[(i + 1)..j]));
                    i = j - 1;
                }
                else if (c == '$' && i + 1 < s.Length && s[i + 1] == '{')
                {
                    // ${n}, ${n:default}, ${n|a,b|} or a ${VARIABLE…}: to its matching brace.
                    int depth = 0, j = i + 1;
                    for (; j < s.Length; j++)
                    {
                        if (s[j] == '\\') { j++; continue; }
                        if (s[j] == '{') depth++;
                        else if (s[j] == '}' && --depth == 0) break;
                    }
                    string body = s[(i + 2)..Math.Min(j, s.Length)];
                    int k = 0;
                    while (k < body.Length && char.IsDigit(body[k])) k++;
                    if (k > 0)
                        Stop(int.Parse(body[..k]));
                    if (k < body.Length && body[k] == ':')
                        Walk(body[(k + 1)..]);
                    else if (k < body.Length && body[k] == '|')
                        sb.Append(body[(k + 1)..].TrimEnd('|').Split(',')[0]);
                    else if (k == 0 && body.IndexOf(':') is var colon and >= 0)
                        Walk(body[(colon + 1)..]);
                    i = j;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        Walk(snippet);
        int caret = stops.Where(p => p.Key > 0).Select(p => (int?)p.Value).FirstOrDefault()
            ?? (stops.TryGetValue(0, out int zero) ? zero : sb.Length);
        return (sb.ToString(), caret);
    }

    /// <summary>
    /// Whether <paramref name="offset"/> stands where a singer line's settings are written: after
    /// its <c>with</c>. The line up to the caret alone decides, as the extension's own reading
    /// does (singerCore.ts singerSettingSlot).
    /// </summary>
    internal static bool InSingerSettings(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        int start = offset > 0 ? text.LastIndexOf('\n', offset - 1) + 1 : 0;
        string before = text[start..offset];
        if (before.Contains("//", StringComparison.Ordinal))
            return false;
        var line = System.Text.RegularExpressions.Regex.Match(before, @"^[ \t]*singer(?![\w-])(.*)$");
        if (!line.Success)
            return false;
        // Quoted strings set aside, so that a voice's name is no word of the line.
        string rest = System.Text.RegularExpressions.Regex.Replace(line.Groups[1].Value, "\"[^\"]*\"", "\"\"");
        bool Has(string pattern) => System.Text.RegularExpressions.Regex.IsMatch(rest, pattern);
        if (!Has(@"(?<![\w-])(?:sings|on)(?![\w-])"))
            return false;
        // Only after its `with`: before it the line still takes words of this server's —
        // `on`, then `up` / `octave` / `in` (SingerPlaceItems) — and the extension's `with`
        // stands beside them.
        return Has(@"(?<![\w.-])with[ \t]+(?!on(?![\w-]))");
    }

    /// <summary>
    /// The list after a singer line's part (docs/lyrics-singer-design.md §12): the words that
    /// say what of the part the voice sings — <c>up</c> / <c>down</c>, <c>octave</c>, <c>in</c>, in
    /// that order, each once — the numbers after the first three, and after <c>in</c> the file's
    /// sections not yet named. Null anywhere else, and once the line's <c>with</c> is written.
    /// </summary>
    internal static CompletionItem[]? SingerPlaceItems(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        int start = offset > 0 ? text.LastIndexOf('\n', offset - 1) + 1 : 0;
        string before = text[start..offset];
        if (before.Contains("//", StringComparison.Ordinal))
            return null;
        var line = System.Text.RegularExpressions.Regex.Match(before, @"^[ \t]*singer(?![\w-])(.*)$");
        if (!line.Success)
            return null;
        string rest = System.Text.RegularExpressions.Regex.Replace(line.Groups[1].Value, "\"[^\"]*\"", "\"\"");
        // The part first (`singer alice on lead ▮`, owner's decision 2026-11-12), or the older
        // order (`singer alice sings words on lead ▮`): whichever clause the line writes first.
        var first = System.Text.RegularExpressions.Regex.Match(rest, @"(?<![\w-])(on|sings)(?![\w-])");
        if (!first.Success)
            return null;
        bool partFirst = first.Groups[1].Value == "on";
        var part = System.Text.RegularExpressions.Regex.Match(rest[first.Index..], partFirst
            ? @"^on[ \t]+[\w-]+"
            : @"^sings[ \t]+(?:[\w-]+[ \t]+)?on[ \t]+[\w-]+");
        if (!part.Success)
            return null;
        string tail = rest[(first.Index + part.Length)..];
        if (tail.Length == 0 || !char.IsWhiteSpace(tail[0]))
            return null;        // still in the part's name
        // …and after the part its `sings WORDS`, where the line names its words: the words'
        // list is the line's step (SingerLineStepAt), what follows them this list.
        bool mayNameWords = partFirst;
        if (partFirst && System.Text.RegularExpressions.Regex.Match(tail, @"^[ \t]+sings(?![\w-])") is { Success: true } sings)
        {
            var track = System.Text.RegularExpressions.Regex.Match(tail[sings.Length..], @"^[ \t]+[\w-]+(?=[ \t])");
            if (!track.Success)
                return null;
            tail = tail[(sings.Length + track.Length)..];
            mayNameWords = false;
        }
        var placeWords = tail.Split([(' '), ('\t')], StringSplitOptions.RemoveEmptyEntries);
        var words = placeWords;
        // The word being written is the editor's to match against the list.
        int complete = char.IsWhiteSpace(tail[^1]) ? words.Length : words.Length - 1;
        // The three clauses come in any order, each once (2026-11-12): the list holds the ones not
        // yet written — and, while a list of sections is open, the sections not yet named too.
        bool interval = false, octave = false, sections = false, listOpen = false;
        var named = new HashSet<string>(StringComparer.Ordinal);
        static bool IsPlaceWord(string w) => w is LilySharp.Core.Music.LyricSinger.UpWord or LilySharp.Core.Music.LyricSinger.DownWord
            or LilySharp.Core.Music.LyricSinger.OctaveWord or LilySharp.Core.Music.LyricSinger.InWord;
        for (int i = 0; i < complete;)
        {
            string word = words[i];
            listOpen = false;
            if (!interval && word is LilySharp.Core.Music.LyricSinger.UpWord or LilySharp.Core.Music.LyricSinger.DownWord)
            {
                if (i + 1 >= complete)
                    return Numbers(["3", "6", "4", "5", "2", "8"], word == LilySharp.Core.Music.LyricSinger.UpWord
                        ? "above the part's notes, in the key: 3 is a third" : "below the part's notes, in the key: 3 is a third");
                interval = true;
                i += 2;
            }
            else if (!octave && word == LilySharp.Core.Music.LyricSinger.OctaveWord)
            {
                if (i + 1 >= complete)
                    return Numbers(["-1", "1", "-2", "2"], "octaves above the part's notes (below with a minus)");
                octave = true;
                i += 2;
            }
            else if (!sections && word == LilySharp.Core.Music.LyricSinger.InWord)
            {
                sections = true;
                i++;
                while (i < complete && !IsPlaceWord(words[i]))
                {
                    if (words[i] == LilySharp.Core.Music.LyricSinger.WithWord)
                        return null;
                    named.Add(words[i++]);
                }
                listOpen = i >= complete;
            }
            else
            {
                return null;    // `with`, or a word that is none of these: not this list's
            }
        }
        var items = new List<CompletionItem>();
        if (listOpen)
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"(?m)^[ \t]*section[ \t]+([\w-]+)"))
            {
                string name = m.Groups[1].Value;
                if (named.Add(name))
                    items.Add(new CompletionItem
                    {
                        Label = name, Kind = CompletionItemKind.Reference,
                        Detail = "The voice sings in this section", SortText = items.Count.ToString("000"),
                    });
            }
            // No section named yet: only the sections belong here.
            if (named.Count == items.Count)
                return [.. items];
        }
        void Word(string word, string detail) => items.Add(new CompletionItem
        {
            Label = word, Kind = CompletionItemKind.Keyword, Detail = detail, InsertText = word + " ",
            SortText = items.Count.ToString("000"),
            Command = new Command { Title = "Suggest what follows", CommandIdentifier = "editor.action.triggerSuggest" },
        });
        // Right after the part: `sings`, to name the words where the part names several, or none.
        if (mayNameWords && complete == 0)
            Word("sings", "Name the words this voice sings, where the part does not say which");
        if (!interval)
        {
            Word(LilySharp.Core.Music.LyricSinger.UpWord, "Sing above the part's notes, in the key: up 3 is a third above");
            Word(LilySharp.Core.Music.LyricSinger.DownWord, "Sing below the part's notes, in the key: down 6 is a sixth below");
        }
        if (!octave)
            Word(LilySharp.Core.Music.LyricSinger.OctaveWord, "Sing octaves above the part's notes, or below: octave -1");
        if (!sections)
            Word(LilySharp.Core.Music.LyricSinger.InWord, "Sing only in the sections named: in Chorus Bridge");
        return [.. items];

        static CompletionItem[] Numbers(string[] values, string detail)
        {
            var list = new CompletionItem[values.Length];
            for (int n = 0; n < values.Length; n++)
                list[n] = new CompletionItem
                {
                    Label = values[n], Kind = CompletionItemKind.Value, Detail = detail, InsertText = values[n] + " ",
                    SortText = n.ToString("000"),
                    Command = new Command { Title = "Suggest what follows", CommandIdentifier = "editor.action.triggerSuggest" },
                };
            return list;
        }
    }

    /// <summary>Whether <paramref name="offset"/> stands inside a note's <c>@sing(…)</c>, its
    /// parenthesis not yet closed before the caret (the extension's singMarkSlot).</summary>
    internal static bool InSingMark(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        int start = offset > 0 ? text.LastIndexOf('\n', offset - 1) + 1 : 0;
        string before = text[start..offset];
        return !before.Contains("//", StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(before, @"@sing\([^()]*$");
    }

    /// <summary>The completion of <paramref name="doc"/> at the request's position — an open
    /// document's, or a text the client holds outside any editor (<c>lilysharp/completeText</c>).</summary>
    private CompletionList? CompletionIn(Document doc, CompletionParams @params)
    {
        var position = @params.Position;
        var offset = GetOffset(doc.Text, position.Line, position.Character);

        // Determine context
        var context = GetCompletionContext(doc.Text, offset);

        // A singer line past its words and its part — `singer "…" sings words on lead ▮`, and
        // on into `with breathiness 3 ▮` — is where the line's own settings are written, and
        // those are the voice's engine's to name: the extension lists them (it asks Cantoria).
        // Nothing of this server's belongs there. ⚠️ It answered the file level's 31 words, the
        // line being whole to the parser, and the one `with` stood under all of them (owner's
        // report 2026-11-12, on the first build of `with`).
        // …but what of the PART the line sings is this server's: `up 3`, `octave -1`,
        // `in Chorus Bridge` (owner's decisions 2026-11-12), each word in its order, the
        // sections by name.
        if (SingerPlaceItems(doc.Text, offset) is { } place)
            return new CompletionList { Items = place };
        // …and inside a note's `@sing(…)` the same: the music block's 92 words stood there.
        if (InSingerSettings(doc.Text, offset) || InSingMark(doc.Text, offset))
            return new CompletionList { Items = [] };

        // '.' is a trigger for ONE thing: the placement after `@fermata.` / `@todo("memo").`
        // (owner's request 2026-10-11). Everywhere else a '.' is a dotted note value, a
        // grob key's dot or a sentence's full stop, and a list popping up there would be
        // noise — so a '.'-triggered request answers nothing outside the placement.
        if (@params.Context is { TriggerKind: CompletionTriggerKindTriggerCharacter, TriggerCharacter: "." }
            && context is not (CompletionContext.AfterArticulationPlacement
                               or CompletionContext.AfterValueAnnotationPlacement))
            return null;
        // '!' likewise opens ONE list: the span ends after `@!` (owner's report 2026-10-28:
        // nothing completed there). Elsewhere a '!' is text.
        if (@params.Context is { TriggerKind: CompletionTriggerKindTriggerCharacter, TriggerCharacter: "!" }
            && context != CompletionContext.AfterAtBang)
            return null;

        // The word being typed at the cursor (chord letters/digits, e.g. "cmaj7"
        // or, after a ':', the quality being completed).
        int wordStart = offset;
        while (wordStart > 0 && IsChordWordChar(doc.Text[wordStart - 1]))
            wordStart--;
        string word = doc.Text.Substring(wordStart, offset - wordStart);

        // (The ':'-triggered quality completion went with the ':' entry format —
        // a chord is written as it prints, so the diatonic list below carries the
        // qualities inside its symbols.)

        // Inside an @name(…) argument the annotation's own vocabulary takes over,
        // so the shapes/fingers/… are a SECOND list rather than dozens of
        // '@notehead(diamond)'-style entries crowding the '@' list itself.
        if (AnnotationArgumentName(doc.Text, offset) is { } annotation
            && GetAnnotationArgumentCompletions(annotation) is { } arguments)
            return arguments;

        // Inside a @chord(…) argument OR a chords { } block, offer the current key's
        // diatonic chords — one format for both (insert text is the chords{} form).
        // ⚠️ The ROMAN degrees are offered only in the BLOCK. The annotation reads
        // TryParseChordEntry alone, so `@chord(V7)` is refused — measured, and the reason
        // this is one call with a flag rather than one list for both contexts.
        // ⚠️ The chords intercept claims the TRACK body as well as its cells, and it runs
        // before the switch — so the two contexts that outrank it there are named here.
        // A top-level `chords prog { section A { … } ▮ }` holds CELLS (ChordsTrackBody,
        // which still answers with the chord list when the track is flat), and `section ▮`
        // in one holds the section NAMES. Until 2026-09-12 both got the chord vocabulary:
        // user report, `here` in `chords prog { section A { Fm } /* here */ }`.
        if (IsInsideChordAnnotation(doc.Text, offset))
            return GetDiatonicChordCompletions(doc.Text, offset);
        if (IsInsideChordsBlock(doc.Text, offset))
        {
            // A chord's mark (owner's decision 2026-10-11, docs/todo-memo-design.md §5): after
            // `G@` the one annotation a row takes, inside `@todo(…)` the memo — no chord names
            // there — and after its `)` the placement (answered by the switch below).
            if (context == CompletionContext.AfterAt)
                return new CompletionList
                {
                    Items = [.. GetArticulationCompletions().Items.Where(i => i.Label == LilySharp.Core.Semantics.TodoAnnotation.Name)],
                };
            if (AnnotationArgumentName(doc.Text, offset) != null)
                return new CompletionList { Items = [] };
            if (context is not (CompletionContext.ChordsTrackBody or CompletionContext.AfterSection
                    or CompletionContext.AfterValueAnnotationPlacement))
                return GetDiatonicChordCompletions(doc.Text, offset, degreesToo: true);
        }

        // `tempo 100 |`: the feel words. Mid-music (`c4 tempo 96 |`) a NOTE may follow as
        // well, so there the feel words lead the music list rather than replace it.
        CompletionList? tempoFeel = null;
        if (context == CompletionContext.AfterTempoBpm && TempoBpmBeforeCaret(doc.Text, offset) is { } run)
        {
            tempoFeel = GetTempoFeelCompletions(run.Bpm, run.Touching);
            if (GetCompletionContext(doc.Text, offset, tempoFeel: false) != CompletionContext.MusicBlock)
                return tempoFeel;
            context = CompletionContext.MusicBlock;
        }
        // `tempo 100 swing |`: the swung note value, the same way.
        else if (context == CompletionContext.AfterTempoFeel && TempoFeelBeforeCaret(doc.Text, offset) is { } feel)
        {
            tempoFeel = GetTempoSubdivisionCompletions(feel.Word, feel.Touching);
            if (GetCompletionContext(doc.Text, offset, tempoFeel: false) != CompletionContext.MusicBlock)
                return tempoFeel;
            context = CompletionContext.MusicBlock;
        }

        var result = context switch
        {
            // The position goes with the text: the `template-…` items need a RANGE (not just
            // an offset) to re-type the word being completed without changing it.
            CompletionContext.TopLevel => GetTopLevelCompletions(doc.Text, offset, position, _locale),
            // A percussion part's music block offers the drum-kit vocabulary,
            // not pitch letters (LILYPOND-REF: \drummode note names).
            // The key's tonic travels with its signature: the pitch rows are spelled for the
            // signature and the diatonic chord rows are built on the tonic (`C` → `<c e g>`,
            // `IIm7` → `<d f a c>`), from the same declaration.
            // The document's phrase names ride along: a phrase reference is a music item
            // (GRAMMAR PhraseRef), and until 2026-09-10 the popup never named one.
            // A drum part's popup names the phrases too, FIRST and by kit piece — a drum part
            // is written as patterns (owner's request 2026-10-11) — and every phrase item tells
            // what it holds (PhraseSummary: its pieces and length, the grid in its documentation).
            CompletionContext.MusicBlock => IsInsidePercussionPartMusic(doc.Text, offset, out bool inVoice)
                ? WithPhraseSummaries(GetDrumCompletions(inVoice), doc.Tree, drums: true)
                : WithPhraseSummaries(GetMusicCompletions(word, CurrentKeySharps(doc.Text, offset), _flatSpellingContracted, inVoice,
                    CurrentKey(doc.Text, offset).Tonic, DeclaredNamesOf(doc.Text, "phrase"),
                    bowed: IsInsideBowedPartMusic(doc.Tree, offset)), doc.Tree, drums: false),
            // The position goes with the text here too: a repeat barline typed as far as
            // `|` or `:` is REPLACED by the item, not appended to (see GetFormCompletions).
            CompletionContext.FormBlock => GetFormCompletions(doc.Text, offset, position),
            CompletionContext.PartBlock => GetPartBlockCompletions(doc.Text, offset),
            CompletionContext.LyricsBlock => GetLyricsSectionCompletions(doc.Text, offset),
            // A top-level chords TRACK body: its cells, or — a flat lead-sheet track — the
            // chord entries (the list decides, see GetChordsTrackCompletions).
            CompletionContext.ChordsTrackBody => GetChordsTrackCompletions(doc.Text, offset),
            CompletionContext.SectionBlock => GetSectionBlockCompletions(doc.Text, offset),
            CompletionContext.AfterSection => GetMissingSectionNameCompletions(doc.Text, offset),
            // AfterClef stands in two positions and they take different vocabularies, so the
            // position has to be asked for here — GetCompletionContext deliberately does not
            // split the context itself (a `clef` is a `clef`; what differs is only the list).
            CompletionContext.AfterClef => GetClefCompletions(IsInsidePartBlock(doc.Text, offset)),
            CompletionContext.AfterKey => GetKeyTonicCompletions(),
            CompletionContext.AfterKeyTonic => GetKeyModeCompletions(),
            CompletionContext.AfterOctave => GetOctaveCompletions(),
            CompletionContext.AfterOverride => GetOverrideCompletions(),
            CompletionContext.AfterOverrideValue => GetOverrideValueCompletions(doc.Text, offset),
            CompletionContext.AfterRevert => GetRevertCompletions(),
            CompletionContext.AfterTempo => GetTempoCompletions(),
            // (AfterTempoBpm and AfterTempoFeel are answered above, before the switch.)
            CompletionContext.AfterTime => GetTimeCompletions(),
            CompletionContext.AfterPartial => GetPartialCompletions(),
            CompletionContext.AfterTitleText => GetTitleTextCompletions(WordBeforeCursor(doc.Text, offset)),
            // The key that OWNS the string decides which faces fit in it: `serif "…"` and
            // `sans "…"` name a shape, every other key may name any face.
            CompletionContext.AfterFontName =>
                GetFontNameCompletions(KeywordBeforeCurrentString(doc.Text, offset)),
            CompletionContext.AfterFontKeyword => GetFontDeclarationCompletions(),
            CompletionContext.FontBlock => GetFontBlockCompletions(),
            CompletionContext.AfterPaperKeyword => GetPaperDeclarationCompletions(),
            CompletionContext.PaperBlock => GetPaperBlockCompletions(),
            CompletionContext.PaperSpecBlock => GetPaperSpecBlockCompletions(),
            // `score { fonts |` / `score { paper |` — the declared block names.
            CompletionContext.AfterFontsBlockRef => GetDeclaredNameCompletions(doc.Text, "fonts", "Fonts block"),
            CompletionContext.AfterPaperBlockRef => GetDeclaredNameCompletions(doc.Text, "paper", "Paper block"),
            // `size |` / `size "…"` — the paper-size table, spelled for its position.
            CompletionContext.AfterPaperSizeName => GetPaperSizeNameCompletions(insideString: false),
            CompletionContext.AfterPaperSizeNameQuoted => GetPaperSizeNameCompletions(insideString: true),
            // `layout |` / inside `layout { … }` / `score { layout |` / after a layout key.
            CompletionContext.AfterLayoutKeyword => GetLayoutDeclarationCompletions(),
            CompletionContext.LayoutBlock => GetLayoutBlockCompletions(),
            CompletionContext.AfterLayoutNumberKey => new CompletionList { Items = [] },
            CompletionContext.AfterLayoutBlockRef => GetDeclaredNameCompletions(doc.Text, "layout", "Layout block"),
            CompletionContext.AfterLayoutMarks => GetMarkArrangementCompletions(),
            CompletionContext.AfterLayoutBarNumbers => GetBarNumberPolicyCompletions(),
            CompletionContext.AfterLayoutAccidentals => GetAccidentalStyleCompletions(),
            CompletionContext.AfterLayoutSectionLabels => GetSectionLabelCompletions(),
            CompletionContext.AfterLayoutSectionLabelAlign => GetSectionLabelAlignCompletions(),
            CompletionContext.AfterLayoutPartCombineText => GetPartCombineTextCompletions(),
            CompletionContext.AfterLayoutChordQualities => GetChordQualityStyleCompletions(),
            CompletionContext.AfterLayoutMinorChords => GetMinorChordCompletions(),
            CompletionContext.AfterLayoutOttavaLine => GetOttavaLineCompletions(),
            CompletionContext.AfterLayoutChordDiagrams => GetChordDiagramCompletions(),
            CompletionContext.AfterLayoutChordTuning => GetChordTuningCompletions(),
            // The ranked capo frets after the top-level `capo` (on the file's chordTuning, else the guitar).
            CompletionContext.AfterCapo => GetChordDiagramCapoCompletions(doc.Text),
            // Inside the shape table: chord names (and `section`); after `section` the sections.
            CompletionContext.ChordDiagramTable => GetChordDiagramTableCompletions(doc.Text, offset),
            CompletionContext.ChordDiagramTableSection => GetChordDiagramTableSectionCompletions(doc.Text, offset),
            CompletionContext.AfterLayoutChordNames => GetChordNameCompletions(),
            CompletionContext.AfterLayoutChordList => GetChordListCompletions(),
            CompletionContext.AfterLayoutCompressRests => GetCompressRestsCompletions(),
            // `layout { voltaBracket |` and a form ending's `]@voltaBracket(|` take the same
            // values; `]@|` the one annotation an ending takes.
            CompletionContext.AfterLayoutVoltaBracket => GetVoltaBracketCompletions(),
            CompletionContext.InVoltaBracketAnnotation => GetVoltaBracketCompletions(),
            CompletionContext.AfterEndingAt => GetEndingAnnotationCompletions(),
            // The key the caret sits after decides which values fit: a generic family takes
            // only quoted names, a role or group may also redirect to a family.
            CompletionContext.AfterFontRoleKey =>
                GetFontRoleValueCompletions(WordBeforeCursor(doc.Text, offset)),
            // `… as |` — the family to follow; `… step |` — a number, nothing to list;
            // inside an open entry — the attributes that may still follow, then the keys.
            CompletionContext.AfterFontAs => GetFontAsCompletions(),
            CompletionContext.AfterFontNumber => GetFontNumberCompletions(),
            CompletionContext.FontEntryOpen => GetFontEntryContinuationCompletions(),
            // The render items, then the declared parts as bare MIDI-only items (GRAMMAR §7:
            // "a bare part name renders that part to MIDI only").
            // A keyword typed in front of a written body writes the keyword alone.
            CompletionContext.ScoreBlock => WithSingerOnAFreshLine(KeywordOnlyBeforeAWrittenBody(
                WithMidiOnlyParts(GetScoreBlockCompletions(), doc.Text), doc.Text, offset), doc.Text, offset),
            CompletionContext.StaffGroupBlock => KeywordOnlyBeforeAWrittenBody(
                GetStaffGroupBlockCompletions(), doc.Text, offset),
            // `staff |` / `ossia |`: the parts, and the five clefs that may precede one.
            // …and the singers the file names: `staff carol` is the staff of what carol sings.
            CompletionContext.AfterStaffRef => WithSingers(GetStaffRefCompletions(doc.Text), doc, staves: true),
            // `staff CLEF |`: the parts, and — the clef word being a legal part name too —
            // the selectors.
            // The clef word itself decides whether the selectors belong beside the parts:
            // they do only where the document declares a part by that name.
            CompletionContext.AfterStaffClefRef =>
                GetStaffClefRefCompletions(doc.Text, WordBeforeCursor(doc.Text, offset)),
            // `tab |` / `tab TUNING |`: the parts, and the tunings that may precede one.
            CompletionContext.AfterTabRef => GetTabRefCompletions(doc.Text),
            // `tab TUNING |`: the parts, and — the tuning word being a legal part name too —
            // the style selector.
            CompletionContext.AfterTabTuningRef =>
                GetTabTuningRefCompletions(doc.Text, WordBeforeCursor(doc.Text, offset)),
            // condensedStaff { } / combinedStaff { }: bare part names only.
            CompletionContext.BarePartNameList => GetDeclaredNameCompletions(doc.Text, "part", "Part"),
            // `condensedStaff |` before the brace: a clef, or the brace with none.
            CompletionContext.AfterOneStaffKeyword => GetOneStaffHeadCompletions(doc.Text, offset),
            // `score NAME |`: the header options and the body's braces.
            CompletionContext.AfterScoreHeader => GetScoreHeaderCompletions(),
            // `form |` in a score: the forms this document declares — the one thing that fits.
            CompletionContext.AfterScoreForm =>
                GetDeclaredNameCompletions(doc.Text, "form", "Form this score plays"),
            // `transpose |`: the target pitches from c, up and down.
            CompletionContext.AfterTransposePitch => GetTransposePitchCompletions(),
            // `shift |`: a number of semitones is typed; nothing to list.
            CompletionContext.AfterShiftAmount => GetShiftAmountCompletions(),
            // A lyrics body: the verses (`verse N { … }`).
            CompletionContext.LyricsBody => GetLyricVerseCompletions(),
            CompletionContext.AfterChordsRef => GetDeclaredNameCompletions(doc.Text, "chords", "Chord part"),
            CompletionContext.AfterStrokesRef => GetDeclaredNameCompletions(doc.Text, "strokes", "Strokes part — drawn as a one-line staff of slash heads"),
            CompletionContext.AfterPlaysTarget => GetDeclaredNameCompletions(doc.Text, "strokes", "Strokes part the row plays — its chords struck at the strokes"),
            // …and the singers the file names: `lyrics carol` is the words carol sings.
            CompletionContext.AfterLyricsRef => WithSingers(GetLyricsRowNameCompletions(doc.Text, offset), doc, staves: false),
            CompletionContext.AfterLyricsName => GetVoiceBindingNameCompletions(doc.Text),
            CompletionContext.AfterLyricsTrackName => GetLyricsTrackNameCompletions(LyricsHeadAt(doc.Text, offset)?.Written),
            CompletionContext.AfterLanguage => GetLanguageNameCompletions(),
            // `singer ▮` / `singer alice ▮`: the voices and the genders are the extension's to list;
            // the languages a singer may say are listed here, beside them.
            CompletionContext.AfterSinger => new CompletionList { Items = [.. SingerVoiceItems(doc.Text, offset).Where(i => i.Label is not ("sings" or "on"))] },
            CompletionContext.AfterSingerVoice => new CompletionList { Items = SingerVoiceItems(doc.Text, offset) },
            // `… on lead sings ▮` (the part first): the words THAT PART names — the owner's reason
            // for the order — and, where it names none, the file's; the made words after them.
            CompletionContext.AfterSingerSings when SingerPartBefore(doc.Text, offset) is { } sungPart
                => new CompletionList { Items = SingerWordsOfPart(doc, sungPart) },
            CompletionContext.AfterSingerSings => new CompletionList { Items = [.. SingerWordsGoOnToOn([.. GetDeclaredNameCompletions(doc.Text, "lyrics", "The words this voice sings").Items, .. GeneratedLyricsItems(forSinger: true)], doc.Text, offset), RowOnItem("9")] },
            CompletionContext.AfterSingerWords => new CompletionList { Items = [RowOnItem("0")] },
            CompletionContext.AfterSingsTarget => GetVoiceBindingNameCompletions(doc.Text,
                "The part - or named voice - this lyrics track sings"),
            // After a row or a staff a score goes on with its next item, and `singer` is one —
            // the staff of what a named singer sings, or this score's own singer line — where
            // a line of its own begins (owner's report 2026-11-12: under `lyrics words` on the
            // next line the list held every item but that one).
            CompletionContext.AfterChordAttachName => WithSingerOnAFreshLine(GetChordAttachNameCompletions(), doc.Text, offset),
            CompletionContext.AfterChordsTrackName => GetChordsTrackNameCompletions(),
            CompletionContext.AfterChordForTarget => GetDeclaredNameCompletions(doc.Text, "part", "Part"),
            CompletionContext.AfterStaffAttachName => WithSingerOnAFreshLine(WithoutOssiaPitch(GetStaffAttachNameCompletions(), doc.Text, offset), doc.Text, offset),
            CompletionContext.AfterTabAttachName => WithSingerOnAFreshLine(GetTabAttachNameCompletions(), doc.Text, offset),
            CompletionContext.AfterGroupStaffAttachName => WithoutOssiaPitch(GetGroupStaffAttachNameCompletions(), doc.Text, offset),
            CompletionContext.AfterPartLyrics => new CompletionList { Items = [.. GetDeclaredNameCompletions(doc.Text, "lyrics", "The words this part sings").Items, .. GeneratedLyricsItems(forSinger: true)] },
            CompletionContext.AfterLyricsRowAttachName => WithSingerOnAFreshLine(GetLyricsRowAttachNameCompletions(LyricsHeadAt(doc.Text, offset)?.Written), doc.Text, offset),
            CompletionContext.AfterGroupLyricsRowAttachName => GetGroupLyricsRowAttachNameCompletions(LyricsHeadAt(doc.Text, offset)?.Written),
            CompletionContext.AfterStaffLinesAs => WithoutOssiaPitch(GetStaffLinesSelectorCompletions(), doc.Text, offset),
            // `as lines 1 |` — the chain may go on (bare, sharing the one `as`) or the row
            // may end; the selectors already written are dropped.
            CompletionContext.AfterStaffSelectorChain =>
                GetStaffSelectorChainCompletions(doc.Text, offset),
            CompletionContext.AfterGroupStaffSelectorChain =>
                GetGroupStaffSelectorChainCompletions(doc.Text, offset),
            CompletionContext.AfterStaffLinesValue => GetStaffLinesValueCompletions(),
            CompletionContext.AfterChordDisplayAs => GetChordDisplayModeCompletions(),
            CompletionContext.AfterTabDisplayAs => GetTabDisplayModeCompletions(),
            CompletionContext.AfterTabSelectorChain => GetTabSelectorChainCompletions(
                TabSelectorsWritten(doc.Text, offset) ?? []),
            CompletionContext.AfterInstrument => GetInstrumentCompletions(doc.Text, offset, position),
            CompletionContext.AfterMidiInstrument => GetMidiInstrumentCompletions(doc.Text, offset, position),
            CompletionContext.AfterTuning => GetTuningCompletions(),
            CompletionContext.AfterTransposition => GetTranspositionCompletions(),
            CompletionContext.AfterRemoveEmpty => GetRemoveEmptyCompletions(),
            CompletionContext.AfterPitch => GetPitchModeCompletions(),
            CompletionContext.AfterPagePitch => GetPagePitchModeCompletions(),
            CompletionContext.AfterRepeat => GetRepeatKindCompletions(),
            CompletionContext.AfterTupletRatio => GetTupletAppearanceCompletions(doc.Text, offset),
            // The bare-@chord item is offered only when the group before the '@'
            // will actually auto-name; an unrecognizable one falls back to the
            // note form — @chord() with the caret inside the parens.
            CompletionContext.AfterAt => WithSpanEndOpener(MatchAnywhere(
                GetArticulationCompletions(
                    AtFollowsChord(doc.Text, offset) && GroupBeforeAtAutoNames(doc.Text, offset)),
                PartialAnnotationName(doc.Text, offset)), PartialAnnotationName(doc.Text, offset)),
            CompletionContext.AfterCheck => GetCheckRuleCompletions(),
            CompletionContext.AfterCheckArguments => GetCheckPartCompletions(doc.Text, offset),
            CompletionContext.AfterAtBang => MatchAnywhere(
                GetSpanEndCompletions(doc.Text, offset), PartialAnnotationName(doc.Text, offset)),
            CompletionContext.AfterArticulationPlacement => PlacementAndStillMatchingNames(
                doc.Text, offset, position,
                AtFollowsChord(doc.Text, offset) && GroupBeforeAtAutoNames(doc.Text, offset)),
            // `@todo("memo")|` / `@todo("memo").|` — the argument is settled, so only the
            // placement can follow (no names to keep matching, unlike `@trill|`).
            CompletionContext.AfterValueAnnotationPlacement => GetArticulationPlacementCompletions(
                doc.Text, offset, position, ValueAnnotationBeforePlacement(doc.Text, offset)),
            // Only a tab string number follows a backslash (the dynamic names this
            // offered until 2026-09-03 were all refused by the parser).
            CompletionContext.AfterBackslash => GetStringNumberCompletions(),
            _ => null
        };
        if (tempoFeel is null || result is null)
            return result;
        return new CompletionList
        {
            IsIncomplete = result.IsIncomplete,
            Items = [.. tempoFeel.Items, .. result.Items],
        };
    }

    private static bool IsChordWordChar(char c) => char.IsLetterOrDigit(c);

}
