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
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LilySharp.Core.Rendering;
using SkiaSharp;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;
using LspDiagnosticSeverity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    // ========== Completion ==========

    [JsonRpcMethod(Methods.TextDocumentCompletionName, UseSingleObjectParameterDeserialization = true)]
    public CompletionList? Completion(CompletionParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return null;

        var position = @params.Position;
        var offset = GetOffset(doc.Text, position.Line, position.Character);

        // Determine context
        var context = GetCompletionContext(doc.Text, offset);

        // The word being typed at the cursor (chord letters/digits, e.g. "cmaj7"
        // or, after a ':', the quality being completed).
        int wordStart = offset;
        while (wordStart > 0 && IsChordWordChar(doc.Text[wordStart - 1]))
            wordStart--;
        string word = doc.Text.Substring(wordStart, offset - wordStart);

        // Inside a chords { } block (or its inner sections), right after a chord's
        // ':', complete the quality tokens (m, m7, maj7, sus4, …).
        if (wordStart > 0 && doc.Text[wordStart - 1] == ':' && IsInsideChordsBlock(doc.Text, offset))
            return GetChordQualityCompletions();

        // Inside a @chord(…) argument OR a chords { } block, offer the current key's
        // diatonic chords — one format for both (insert text is the chords{} form).
        if (IsInsideChordAnnotation(doc.Text, offset) || IsInsideChordsBlock(doc.Text, offset))
            return GetDiatonicChordCompletions(doc.Text, offset);

        return context switch
        {
            CompletionContext.TopLevel => GetTopLevelCompletions(),
            // A percussion part's music block offers the drum-kit vocabulary,
            // not pitch letters (LILYPOND-REF: \drummode note names).
            CompletionContext.MusicBlock => IsInsidePercussionPartMusic(doc.Text, offset, out bool inVoice)
                ? GetDrumCompletions(inVoice)
                : GetMusicCompletions(word, CurrentKeySharps(doc.Text, offset), _flatSpellingContracted, inVoice),
            CompletionContext.FormBlock => GetFormCompletions(doc.Text),
            CompletionContext.PartBlock => GetPartPropertyCompletions(),
            CompletionContext.AfterClef => GetClefCompletions(),
            CompletionContext.AfterKey => GetKeyTonicCompletions(),
            CompletionContext.AfterKeyTonic => GetKeyModeCompletions(),
            CompletionContext.AfterOctave => GetOctaveCompletions(),
            CompletionContext.AfterTempo => GetTempoCompletions(),
            CompletionContext.AfterTime => GetTimeCompletions(),
            CompletionContext.AfterPartial => GetPartialCompletions(),
            CompletionContext.AfterTitleText => GetTitleTextCompletions(WordBeforeCursor(doc.Text, offset)),
            CompletionContext.AfterFontName => GetFontNameCompletions(),
            CompletionContext.AfterFontKeyword => GetFontQuoteInsertCompletion(),
            CompletionContext.ScoreBlock => GetScoreBlockCompletions(),
            CompletionContext.AfterStaffRef => GetDeclaredNameCompletions(doc.Text, "part", "Part"),
            CompletionContext.AfterChordsRef => GetDeclaredNameCompletions(doc.Text, "chords", "Chord part"),
            CompletionContext.AfterLyricsRef => GetDeclaredNameCompletions(doc.Text, "lyrics", "Lyrics part"),
            CompletionContext.AfterWith => GetWithCompletions(),
            CompletionContext.AfterChordAttachName => GetChordAttachNameCompletions(),
            CompletionContext.AfterChordDisplayAs => GetChordDisplayModeCompletions(),
            CompletionContext.AfterTabDisplayAs => GetTabDisplayModeCompletions(),
            CompletionContext.AfterInstrument => GetInstrumentCompletions(doc.Text, offset, position),
            CompletionContext.AfterRemoveEmpty => GetRemoveEmptyCompletions(),
            CompletionContext.AfterAt => GetArticulationCompletions(AtFollowsChord(doc.Text, offset)),
            CompletionContext.AfterArticulationPlacement => GetArticulationPlacementCompletions(doc.Text, offset, position),
            CompletionContext.AfterBackslash => GetDynamicCompletions(),
            _ => null
        };
    }

    private static bool IsChordWordChar(char c) => char.IsLetterOrDigit(c);

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>chords [name] { … }</c>
    /// block — INCLUDING a <c>section</c> nested in one (the part-major chord track).
    /// This matches the NAMED form too
    /// (<c>chords harmony {</c>, whose word before the brace is the name). Used to
    /// offer chord entries: the diatonic chords, and quality tokens after ':'.
    /// </summary>
    internal static bool IsInsideChordsBlock(string text, int offset)
    {
        // The INTRODUCING keyword of each still-open block (for `chords harmony {` and
        // `section A {` it is one word before the name, so a two-word lookback).
        var frames = ScanOpenBlocks(text, offset, ReadFrame);
        // Inside a chords block, or a section nested inside one (skip section levels).
        for (int i = frames.Count - 1; i >= 0; i--) // innermost first
        {
            var f = frames[i];
            string keyword = f.Prefix is "chords" or "lyrics" or "section" or "part" ? f.Prefix : f.Name;
            if (keyword == "chords") return true;
            if (keyword == "section") continue;
            return false;
        }
        return false;
    }

    /// <summary>
    /// The keyword introducing the innermost still-open <c>keyword { … }</c> block
    /// at <paramref name="offset"/> (e.g. "structure", "chords", "section"),
    /// or null at the top level. Used to scope completions to the block kind.
    /// </summary>
    internal static string? InnermostOpenBlock(string text, int offset)
    {
        var frames = ScanOpenBlocks(text, offset, ReadFrame);
        return frames.Count > 0 ? frames[^1].Name : null;
    }

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>part &lt;name&gt; { … }</c>
    /// body. <see cref="InnermostOpenBlock"/> cannot answer this: the word before a
    /// part's <c>{</c> is the part NAME, so the introducing <c>part</c> keyword is one
    /// word further back — which is also what tells a declaration (<c>part rh {</c>)
    /// apart from a section-body part reference (<c>rh {</c>).
    /// </summary>
    internal static bool IsInsidePartBlock(string text, int offset)
    {
        var frames = ScanOpenBlocks(text, offset, ReadFrame);
        return frames.Count > 0 && frames[^1].Prefix == "part";
    }

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>"…"</c> string literal.
    /// Strings never span lines, so an odd number of quotes between the line start
    /// and the cursor means the cursor is inside one.
    /// </summary>
    internal static bool IsInsideStringLiteral(string text, int offset)
    {
        int quotes = 0;
        for (int i = offset - 1; i >= 0 && text[i] != '\n'; i--)
            if (text[i] == '"')
                quotes++;
        return (quotes & 1) == 1;
    }

    /// <summary>
    /// When <paramref name="offset"/> sits inside a <c>"…"</c> string, the bare keyword
    /// that INTRODUCES that string — the word immediately before its opening quote
    /// (e.g. "font" in <c>font "Noto|"</c>, "title" in <c>title "My Song|"</c>). Empty
    /// when not in a string or when no bare word precedes the quote. Lets a value
    /// completion key off the directive that owns the string the caret is in, so the
    /// caret is served whether it sits just before the quote (<c>font |</c>) or already
    /// inside it (<c>font "|"</c>).
    /// </summary>
    internal static string KeywordBeforeCurrentString(string text, int offset)
    {
        if (!IsInsideStringLiteral(text, offset))
            return "";
        // Walk back to the opening quote of the current (line-bounded) string.
        int i = offset - 1;
        while (i >= 0 && text[i] != '"' && text[i] != '\n') i--;
        if (i < 0 || text[i] != '"')
            return "";
        // The bare word before that quote (WordBeforeCursor skips the whitespace).
        return WordBeforeCursor(text, i);
    }

    /// <summary>
    /// Sequential string/comment state machine for the brace scanners below: call
    /// for each index i = 0,1,2,… in order; it returns true when <c>text[i]</c> is
    /// live code, false inside a <c>"…"</c> string (line-bounded, matching
    /// <see cref="IsInsideStringLiteral"/>), a <c>//</c> line comment, or a
    /// <c>/* … */</c> block comment. Without this the scanners counted braces that
    /// sit in strings/comments (e.g. <c>title "a {b"</c>) and mis-detected context.
    /// </summary>
    private static bool IsCodeChar(string text, int i,
        ref bool inString, ref bool inLineComment, ref bool inBlockComment)
    {
        char c = text[i];
        if (inLineComment)
        {
            if (c == '\n') inLineComment = false;
            return false;
        }
        if (inBlockComment)
        {
            if (c == '/' && i > 0 && text[i - 1] == '*') inBlockComment = false;
            return false;
        }
        if (inString)
        {
            if (c == '"' || c == '\n') inString = false;
            return false;
        }
        if (c == '"') { inString = true; return false; }
        if (c == '/' && i + 1 < text.Length)
        {
            if (text[i + 1] == '/') { inLineComment = true; return false; }
            if (text[i + 1] == '*') { inBlockComment = true; return false; }
        }
        return true;
    }

    /// <summary>
    /// Precomputes, for indices <c>0..length-1</c>, whether each character is live
    /// code (not in a string/comment). Lets a BACKWARD scanner ignore braces in
    /// strings/comments, which the forward <see cref="IsCodeChar"/> state machine
    /// cannot answer out of order.
    /// </summary>
    private static bool[] CodeMask(string text, int length)
    {
        var mask = new bool[length];
        bool inString = false, inLine = false, inBlock = false;
        for (int i = 0; i < length; i++)
            mask[i] = IsCodeChar(text, i, ref inString, ref inLine, ref inBlock);
        return mask;
    }

    /// <summary>
    /// Forward-scans <paramref name="text"/> over <c>[0, offset)</c>, maintaining
    /// the stack of still-open <c>{ … }</c> blocks (braces inside strings/comments
    /// ignored via <see cref="IsCodeChar"/>). For each opening brace,
    /// <paramref name="atOpenBrace"/> computes the payload pushed onto the stack;
    /// the returned list is outermost-first, so <c>[^1]</c> is the innermost block.
    /// This is the shared skeleton of the forward block-context scanners.
    /// </summary>
    private static List<T> ScanOpenBlocks<T>(string text, int offset, Func<string, int, T> atOpenBrace)
    {
        var stack = new List<T>();
        bool inStr = false, inLine = false, inBlock = false;
        int limit = Math.Min(offset, text.Length);
        for (int i = 0; i < limit; i++)
        {
            if (!IsCodeChar(text, i, ref inStr, ref inLine, ref inBlock)) continue;
            if (text[i] == '{') stack.Add(atOpenBrace(text, i));
            else if (text[i] == '}' && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
        }
        return stack;
    }

    /// <summary>A still-open block frame: the bare word immediately before its
    /// <c>{</c> (<see cref="Name"/>) and the word before that (<see cref="Prefix"/>),
    /// so callers can tell <c>chords harmony {</c> (Prefix=chords, Name=harmony)
    /// from <c>structure {</c> (Prefix="", Name=structure).</summary>
    private readonly record struct BlockFrame(string Prefix, string Name);

    /// <summary>Reads the two bare words before <paramref name="braceIndex"/> into a
    /// <see cref="BlockFrame"/> (skipping intervening whitespace).</summary>
    private static BlockFrame ReadFrame(string text, int braceIndex)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
        int j = braceIndex - 1;
        while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
        int end1 = j + 1;
        while (j >= 0 && IsWordChar(text[j])) j--;
        string name = text.Substring(j + 1, end1 - (j + 1));   // word before '{'
        int k = j;
        while (k >= 0 && char.IsWhiteSpace(text[k])) k--;
        int end2 = k + 1;
        while (k >= 0 && IsWordChar(text[k])) k--;
        string prefix = text.Substring(k + 1, end2 - (k + 1));  // and the one before it
        return new BlockFrame(prefix, name);
    }

    /// <summary>Quality-token completions offered after a chord's ':' inside a chords block.</summary>
    private static CompletionList GetChordQualityCompletions()
    {
        var items = ChordQualityRegistry.Tokens
            .OrderBy(t => t)
            .Select(t =>
            {
                ChordQualityRegistry.TryResolve(t, out var q);
                return new CompletionItem
                {
                    Label = t,
                    Kind = CompletionItemKind.EnumMember,
                    Detail = new ChordStructure(0, 0, q).DisplayName.Length == 0
                        ? "major triad"
                        : "C" + ChordQualityRegistry.GetSuffix(q),
                };
            })
            .ToArray();
        return new CompletionList { Items = items };
    }

    /// <summary>
    /// Completions for a <c>structure { … }</c> block: everything a structure body
    /// can hold — the document's section names, the navigation marks (segno / coda /
    /// to coda / D.C. / D.S. …), repeat barlines (<c>|:</c> <c>:|</c>), volta
    /// brackets (<c>[1. …]</c>), the silent-section prefix (<c>~</c>) and custom
    /// text (<c>_"…"</c>). Deliberately offers NO note names — the structure is a
    /// playback order of sections, not music.
    /// </summary>
    internal static CompletionList GetFormCompletions(string text)
    {
        var items = new System.Collections.Generic.List<CompletionItem>();

        // Section names declared anywhere in the document (in declaration order,
        // deduplicated) — these are what a structure plays.
        var sections = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (Match m in Regex.Matches(text, @"\bsection\s+(\w+)"))
        {
            var name = m.Groups[1].Value;
            if (seen.Add(name))
                sections.Add(name);
        }
        // Plain reference, then the silent form (~Name = render, no rehearsal
        // label) — one per section, so the ~ prefix is never offered on its own.
        foreach (var name in sections)
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Reference,
                Detail = "Section",
            });
        foreach (var name in sections)
            items.Add(new CompletionItem
            {
                Label = "~" + name,
                InsertText = "~" + name,
                Kind = CompletionItemKind.Reference,
                Detail = "Silent section (renders, no rehearsal label)",
            });

        // Navigation marks placed between sections.
        var navs = new (string Label, string Detail)[]
        {
            ("segno", "Segno (jump target)"),
            ("coda", "Coda (jump target)"),
            ("to coda", "Jump to the coda"),
            ("fine", "End here"),
            ("dc", "Da Capo — repeat from the top"),
            ("ds", "Dal Segno — repeat from the segno"),
            ("dc al fine", "Da Capo al Fine"),
            ("dc al coda", "Da Capo al Coda"),
            ("ds al fine", "Dal Segno al Fine"),
            ("ds al coda", "Dal Segno al Coda"),
        };
        foreach (var (label, detail) in navs)
            items.Add(new CompletionItem
            {
                Label = label,
                Kind = CompletionItemKind.Keyword,
                Detail = detail,
            });

        // Repeat barlines, volta brackets, the silent-section prefix and custom
        // text — the remaining things a structure body can hold.
        items.Add(new CompletionItem
        {
            Label = "|:", InsertText = "|:", Kind = CompletionItemKind.Operator,
            Detail = "Repeat start",
        });
        items.Add(new CompletionItem
        {
            Label = ":|", InsertText = ":|", Kind = CompletionItemKind.Operator,
            Detail = "Repeat end (suffix x3 for a count)",
        });

        CompletionItem Snippet(string label, string insert, string detail) => new()
        {
            Label = label,
            InsertText = insert,
            InsertTextFormat = InsertTextFormat.Snippet,
            Kind = CompletionItemKind.Snippet,
            Detail = detail,
        };
        items.Add(Snippet("[1. ]", "[1. $0]", "1st ending (volta bracket)"));
        items.Add(Snippet("[2. ]", "[2. $0]", "2nd ending (volta bracket)"));
        items.Add(Snippet("[1-2. ]", "[${1:1-2}. $0]", "Multi-pass ending, e.g. [1-2. …] or [1,3. …]"));
        items.Add(Snippet("_\"\"", "_\"$0\"", "Custom text annotation"));

        return new CompletionList { Items = items.ToArray() };
    }

    internal enum CompletionContext
    {
        Unknown,
        TopLevel,
        MusicBlock,
        FormBlock,
        PartBlock,
        AfterClef,
        AfterKey,
        AfterKeyTonic,
        AfterOctave,
        AfterTempo,
        AfterTime,
        AfterPartial,
        AfterTitleText,
        AfterFontName,
        AfterFontKeyword,
        ScoreBlock,
        AfterStaffRef,
        AfterChordsRef,
        AfterLyricsRef,
        AfterWith,
        AfterChordAttachName,
        AfterChordDisplayAs,
        AfterTabDisplayAs,
        AfterInstrument,
        AfterRemoveEmpty,
        AfterAt,
        AfterBackslash,
        AfterArticulationPlacement
    }

    /// <summary>
    /// The whole word immediately before the partial word being typed at
    /// <paramref name="offset"/> (skipping the current word and any whitespace),
    /// e.g. "clef" in <c>clef tr|</c> or <c>clef |</c>. Empty if none. Hyphen counts
    /// as a word character: instrument presets are hyphenated (piano-right,
    /// 5-string-bass), and the scan must not truncate at the hyphen — in
    /// <c>instrument piano-|</c> the partial word is "piano-", and the preceding
    /// word must still come out as "instrument".
    /// </summary>
    internal static string WordBeforeCursor(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
        int i = offset;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // skip the partial word
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--; // skip whitespace
        int end = i;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the preceding word
        return text.Substring(i, end - i);
    }

    internal static CompletionContext GetCompletionContext(string text, int offset)
    {
        if (offset == 0)
            return CompletionContext.TopLevel;

        // Right after a complete articulation name or its '.', offer the .up/.down
        // placement qualifier — '@fermata|', '@fermata.|', '@fermata.d|'. Checked
        // against the char immediately before the cursor (no whitespace skip) so a
        // trailing space means the user has moved on to the next note.
        if (IsArticulationPlacementContext(text, offset))
            return CompletionContext.AfterArticulationPlacement;

        // Look back for context clues
        int i = offset - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
            i--;

        if (i >= 0)
        {
            if (text[i] == '@')
                return CompletionContext.AfterAt;
            if (text[i] == '\\')
                return CompletionContext.AfterBackslash;

            // A partial name already typed after the trigger — '@acc', '\stac' —
            // must keep offering the list (the editor then filters it to 'accent'
            // etc.). Skip the partial word back to the trigger and re-check;
            // otherwise the context was lost the moment the first letter was typed.
            int at = i;
            while (at >= 0 && (char.IsLetterOrDigit(text[at]) || text[at] == '-'))
                at--;
            if (at >= 0)
            {
                if (text[at] == '@')
                    return CompletionContext.AfterAt;
                if (text[at] == '\\')
                    return CompletionContext.AfterBackslash;
            }
        }

        // Right after the `clef` keyword (in a header or mid-music), only the clef
        // names are valid — offer those alone, not notes/keywords.
        var prevWord = WordBeforeCursor(text, offset);
        if (prevWord == "clef")
            return CompletionContext.AfterClef;

        // `key |` → tonic pitches; `key a |` → only the modes are valid
        // (major/minor/dorian/…), not lyrics/tempo/every keyword.
        if (prevWord == "key")
            return CompletionContext.AfterKey;

        // `octave |` → only its two modes (a bare number re-anchor is typed,
        // not completed).
        if (prevWord == "octave")
            return CompletionContext.AfterOctave;

        // Value positions after the metadata/meter keywords: only their own
        // value forms fit there, not the keyword list. Guarded against string
        // interiors so a title like "tempo di valse" is not hijacked.
        if (!IsInsideStringLiteral(text, offset))
        {
            switch (prevWord)
            {
                case "tempo": return CompletionContext.AfterTempo;
                case "time": return CompletionContext.AfterTime;
                case "partial": return CompletionContext.AfterPartial;
                case "title" or "composer": return CompletionContext.AfterTitleText;
                // `font |` with no quotes yet: offer the font names already wrapped in
                // "…" so Ctrl+Space here completes to `font "Family"`.
                case "font": return CompletionContext.AfterFontKeyword;
            }
        }

        // Inside a "…" string value, the directive that OWNS the string decides the
        // completion. `font "Noto|"` offers the installed, embeddable font families;
        // a `title`/`composer` string keeps its snippet (so the caret is served whether
        // it sits just before the opening quote or already inside it). Every other
        // string falls through unchanged.
        switch (KeywordBeforeCurrentString(text, offset))
        {
            case "font": return CompletionContext.AfterFontName;
            case "title" or "composer": return CompletionContext.AfterTitleText;
        }

        if (IsPitchName(prevWord) && SecondWordBeforeCursor(text, offset) == "key")
            return CompletionContext.AfterKeyTonic;

        // Right after the `instrument` part property only the known instrument presets
        // are valid — offer those alone (they set clef/octave/tuning defaults). Unlike
        // the music-jargon "clef", "instrument" is an ordinary English word, so the
        // keyword alone is not enough context: it must sit where the part property is
        // actually valid — inside a part { } body and not inside a string — or lyrics
        // like `play my instrument` and titles would have their completion hijacked.
        if (prevWord == "instrument"
            && IsInsidePartBlock(text, offset)
            && !IsInsideStringLiteral(text, offset))
            return CompletionContext.AfterInstrument;

        // Right after the `removeEmpty` part property only its values are valid
        // (true / all / false — LP RemoveEmptyStaves / RemoveAllEmptyStaves).
        if (string.Equals(prevWord, "removeEmpty", StringComparison.OrdinalIgnoreCase)
            && IsInsidePartBlock(text, offset)
            && !IsInsideStringLiteral(text, offset))
            return CompletionContext.AfterRemoveEmpty;

        // Directly inside a part { } header (and not after one of the
        // value-taking keywords above): offer the part property names — a part
        // body holds properties (and inner sections), never notes.
        if (IsInsidePartBlock(text, offset) && !IsInsideStringLiteral(text, offset))
            return CompletionContext.PartBlock;

        // Inside form <name> { … } the body is a playback order (section names and
        // navigation marks), not music — so it gets its own completions, never note
        // names. A form is always named, so the `form` keyword is the frame Prefix.
        var formFrames = ScanOpenBlocks(text, offset, ReadFrame);
        if (formFrames.Count > 0 && formFrames[^1].Prefix == "form")
            return CompletionContext.FormBlock;

        // Inside score "name" { } / grandStaff { }: the body is a render spec.
        // After its reference keywords only the declared part names fit; a
        // `with` continues into `with chords PART`.
        if (IsInsideScoreBlock(text, offset))
        {
            // `… as |` → a display selector, but which one depends on what the `as`
            // governs: `tab … as` takes numbers|full, every other form (`chords … as`,
            // `[staff|tab] … with chords … as`) takes roman|both|names.
            if (prevWord == "as")
                return AsSelectorContext(text, offset);
            switch (prevWord)
            {
                case "staff": return CompletionContext.AfterStaffRef;
                // `tab` references a part too (an optional tuning may precede the
                // name, but the part is the useful suggestion right after `tab`).
                case "tab": return CompletionContext.AfterStaffRef;
                case "chords": return CompletionContext.AfterChordsRef;
                case "lyrics": return CompletionContext.AfterLyricsRef;
                case "with": return CompletionContext.AfterWith;
            }
            // `chords NAME |` / `with chords NAME |`: after the attached chord part's
            // name, offer the `as roman|both|names` display selector (plus the normal
            // continuations, so a following render item is not blocked).
            if (SecondWordBeforeCursor(text, offset) == "chords")
                return CompletionContext.AfterChordAttachName;
            return CompletionContext.ScoreBlock;
        }

        if (i >= 0 && text[i] == '{')
            return CompletionContext.MusicBlock;

        // Check if inside braces (ignoring braces in strings/comments)
        int braceDepth = 0;
        bool inStr = false, inLine = false, inBlock = false;
        for (int j = 0; j < offset && j < text.Length; j++)
        {
            if (!IsCodeChar(text, j, ref inStr, ref inLine, ref inBlock)) continue;
            if (text[j] == '{') braceDepth++;
            else if (text[j] == '}') braceDepth--;
        }

        return braceDepth > 0 ? CompletionContext.MusicBlock : CompletionContext.TopLevel;
    }

    /// <summary>The property names a part { } header accepts (bare `name value`
    /// pairs plus inner sections), matching docs/GRAMMAR.md PartProperty.</summary>
    internal static CompletionList GetPartPropertyCompletions()
    {
        // Values = the property takes a value LIST (clef → treble…, time → 4/4…): those
        // items add a space and re-open suggestions so the list enumerates right after.
        var props = new (string Label, string Detail, bool Values)[]
        {
            ("clef", "Clef (treble/bass/alto/tenor/treble_8)", true),
            ("instrument", "Instrument preset (clef/octave/tuning defaults)", true),
            ("name", "Display name (system indent label)", false),
            ("tuning", "Tab tuning (guitar/bass/ukulele/…)", false),
            ("octave", "Octave mode or absolute base (absolute | relative | N)", true),
            ("transpose", "Transpose target pitch", false),
            ("removeEmpty", "Hara-kiri: hide this staff in rest-only systems (true | all)", true),
            ("time", "Part-local time signature", true),
            ("tempo", "Part-local tempo", true),
            ("section", "Inner section (part-major form)", false),
        };
        return new CompletionList
        {
            Items = props.Select((p, i) => new CompletionItem
            {
                Label = p.Label,
                Kind = CompletionItemKind.Property,
                Detail = p.Detail,
                InsertTextFormat = p.Values ? InsertTextFormat.Snippet : default,
                InsertText = p.Values ? $"{p.Label} $0" : null,
                Command = p.Values
                    ? new Command { Title = "Suggest value", CommandIdentifier = "editor.action.triggerSuggest" }
                    : null,
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>The values valid right after the <c>removeEmpty</c> part
    /// property. LILYPOND-REF: ly/context-mods-init.ly — RemoveEmptyStaves
    /// (keeps the first system) / RemoveAllEmptyStaves.</summary>
    internal static CompletionList GetRemoveEmptyCompletions()
    {
        var values = new (string Label, string Detail)[]
        {
            ("true", "Hide in rest-only systems; the FIRST system keeps the staff (LP RemoveEmptyStaves)"),
            ("all", "Hide in rest-only systems including the first (LP RemoveAllEmptyStaves)"),
            ("false", "Never hide (default)"),
        };
        return new CompletionList
        {
            Items = values.Select((v, i) => new CompletionItem
            {
                Label = v.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = v.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>The clef names valid right after the <c>clef</c> keyword, ordered
    /// from the highest-sounding clef to the lowest (not alphabetically).</summary>
    /// <summary>True for a bare pitch-name word (c…b with optional is/es
    /// accidental suffixes) — the tonic between `key` and its mode.</summary>
    internal static bool IsPitchName(string word)
        => System.Text.RegularExpressions.Regex.IsMatch(word, "^[a-g](is|es|isis|eses)?$");

    /// <summary>The word before <see cref="WordBeforeCursor"/> — "key" in
    /// <c>key a m|</c>, where the partial word is "m" and the previous is "a".</summary>
    internal static string SecondWordBeforeCursor(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
        int i = offset;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // skip the partial word
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the previous word
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
        int end = i;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the word before it
        return text.Substring(i, end - i);
    }

    /// <summary>
    /// Which display selector an <c>as</c> in a score block governs. <c>tab … as</c>
    /// takes <c>numbers | full</c>; every other <c>as</c> (a <c>chords</c> row, or a
    /// <c>with chords …</c> attachment on a staff or tab) takes <c>roman | both | names</c>.
    /// The word right before <c>as</c> is the target NAME in every case, so it can't
    /// disambiguate — scan the words before <c>as</c> for the nearest governing keyword.
    /// A <c>chords</c> seen first means a chord attachment (its <c>as</c> wins even on a
    /// <c>tab</c> line); a <c>tab</c> seen first with no intervening <c>chords</c> is the
    /// tab style. Anything else keeps the chord default.
    /// </summary>
    internal static CompletionContext AsSelectorContext(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
        int i = offset;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the partial display word
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the `as` itself
        while (i > 0)
        {
            while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
            int end = i;
            while (i > 0 && IsWordChar(text[i - 1])) i--;
            if (end == i) break; // a non-word char (a brace or a quoted name) — give up
            string w = text.Substring(i, end - i);
            if (w == "chords" || w == "staff") return CompletionContext.AfterChordDisplayAs;
            if (w == "tab") return CompletionContext.AfterTabDisplayAs;
        }
        return CompletionContext.AfterChordDisplayAs;
    }

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>score … { }</c> or
    /// <c>grandStaff { }</c> body. A score block usually carries a name
    /// (<c>score "sheet" {</c> / <c>score practice {</c>) between the keyword
    /// and the brace, so the innermost-block scan must skip one quoted or bare
    /// name before reading the keyword.
    /// </summary>
    internal static bool IsInsideScoreBlock(string text, int offset)
    {
        var stack = ScanOpenBlocks(text, offset, IsScoreBlockOpener);
        return stack.Count > 0 && stack[^1];
    }

    private static bool IsScoreBlockOpener(string text, int braceIndex)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
        int j = braceIndex - 1;
        while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
        // Read the word (or quoted name) directly before the brace.
        string w1;
        if (j >= 0 && text[j] == '"')
        {
            j--;
            while (j >= 0 && text[j] != '"') j--;
            j--;
            w1 = ""; // a quoted score name — the keyword sits before it
        }
        else
        {
            int end = j + 1;
            while (j >= 0 && IsWordChar(text[j])) j--;
            w1 = text.Substring(j + 1, end - (j + 1));
        }
        if (w1 == "score" || w1 == "grandStaff")
            return true;
        // Blocks that take NO name before their brace: if w1 is one of these,
        // this brace opens that block, not a score.
        if (w1 is "section" or "part" or "phrase" or "form" or "chords"
            or "lyrics" or "voice" or "tuplet" or "grace" or "acciaccatura"
            or "appoggiatura" or "repeat" or "ossia" or "tab")
            return false;
        // Otherwise w1 was a bare name or a quoted basename (w1 == ""). A score
        // header is `score <form> ["basename"] {`, so up to two tokens precede the
        // keyword; walk back over the remaining ones looking for `score`.
        for (int skip = 0; skip < 2; skip++)
        {
            while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
            if (j < 0) return false;
            if (text[j] == '"')
            {
                j--;
                while (j >= 0 && text[j] != '"') j--;
                j--;
                continue;
            }
            int e2 = j + 1;
            while (j >= 0 && IsWordChar(text[j])) j--;
            string w = text.Substring(j + 1, e2 - (j + 1));
            if (w == "score" || w == "grandStaff")
                return true;
            if (w is "section" or "part" or "phrase" or "form" or "chords"
                or "lyrics" or "voice" or "tuplet" or "grace" or "acciaccatura"
                or "appoggiatura" or "repeat" or "ossia" or "tab")
                return false;
            // else a name — keep walking back
        }
        return false;
    }

    /// <summary>After <c>title</c> / <c>composer</c>: one snippet that drops a
    /// quote pair and parks the caret inside — the text itself is typed.</summary>
    internal static CompletionList GetTitleTextCompletions(string keyword)
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem
                {
                    Label = "\"\"",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "\"$0\"",
                    Detail = keyword == "composer" ? "Quoted composer name" : "Quoted title text",
                },
            ]
        };
    }

    /// <summary>
    /// The completion list offered inside a <c>font "…"</c> string: the installed,
    /// embeddable font families. Computed once and cached — enumerating every installed
    /// family, reading each OS/2 table and probing CJK glyph coverage is not free, and
    /// the set does not change within a process.
    /// </summary>
    private static CompletionList? _fontNameCompletions;

    /// <summary>
    /// Installed font families that may be embedded into an exported PDF, annotated by
    /// license class and CJK coverage. Offered INSIDE a <c>font "…"</c> string (the bare
    /// family name is inserted). At <c>font |</c> before the quotes, see
    /// <see cref="GetFontQuoteInsertCompletion"/> instead.
    /// </summary>
    internal static CompletionList GetFontNameCompletions()
        => _fontNameCompletions ??= BuildFontNameCompletions(EnumerateInstalledEmbeddableFonts());

    /// <summary>
    /// At <c>font |</c> (the keyword typed, no quotes yet): a single item that inserts
    /// the empty pair <c>"…"</c> with the caret between them and re-triggers suggestions,
    /// so completion lands inside the string with the font-name list showing — mirroring
    /// completing the <c>font</c> keyword itself.
    /// </summary>
    internal static CompletionList GetFontQuoteInsertCompletion()
        => new()
        {
            Items =
            [
                new CompletionItem
                {
                    Label = "\"…\"",
                    FilterText = "font",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "\"$0\"",
                    Detail = "Pick an installed, embeddable font",
                    Command = new Command { Title = "Suggest font name", CommandIdentifier = "editor.action.triggerSuggest" },
                }
            ]
        };

    /// <summary>
    /// Enumerates the installed font families and, for the embeddable ones (class
    /// <see cref="FontEmbedInfo.FontEmbedClass.Free"/> or
    /// <see cref="FontEmbedInfo.FontEmbedClass.Gray"/>), yields the family, its class,
    /// and whether it covers Japanese. Every SkiaSharp call is guarded so a font that
    /// fails to load or classify is simply skipped, never thrown out of completion.
    /// </summary>
    private static IEnumerable<(string Family, FontEmbedInfo.FontEmbedClass Cls, bool Cjk)>
        EnumerateInstalledEmbeddableFonts()
    {
        var result = new List<(string, FontEmbedInfo.FontEmbedClass, bool)>();
        string[] families;
        try
        {
            families = SKFontManager.Default.FontFamilies.ToArray();
        }
        catch
        {
            return result; // no font manager — offer nothing rather than throw
        }
        foreach (var family in families)
        {
            if (string.IsNullOrWhiteSpace(family))
                continue;
            try
            {
                var cls = FontEmbedInfo.Classify(family);
                if (cls is not (FontEmbedInfo.FontEmbedClass.Free or FontEmbedInfo.FontEmbedClass.Gray))
                    continue; // not installed-and-embeddable (Forbidden / NotFound)
                // Does the family cover Japanese? Probe 'か' (Hiragana KA, U+304B) —
                // a zero glyph id means the codepoint is not covered.
                bool cjk = false;
                var tf = SKTypeface.FromFamilyName(family);
                if (tf != null)
                    cjk = tf.GetGlyph(0x304B) != 0;
                result.Add((family, cls, cjk));
            }
            catch
            {
                // A font that fails to load / probe is skipped.
            }
        }
        return result;
    }

    /// <summary>
    /// Builds the <c>font "…"</c> completion items from a classified family list.
    /// Split from the system enumeration so it is unit-testable with a synthetic list.
    /// Keeps only the embeddable classes (<see cref="FontEmbedInfo.FontEmbedClass.Free"/>
    /// / <see cref="FontEmbedInfo.FontEmbedClass.Gray"/>); each item's detail states the
    /// license class and notes CJK coverage; the sort key floats Free before Gray and,
    /// within a class, CJK-capable families first.
    /// </summary>
    internal static CompletionList BuildFontNameCompletions(
        IEnumerable<(string Family, FontEmbedInfo.FontEmbedClass Cls, bool Cjk)> fonts)
    {
        var items = new List<CompletionItem>();
        foreach (var (family, cls, cjk) in fonts)
        {
            if (cls is not (FontEmbedInfo.FontEmbedClass.Free or FontEmbedInfo.FontEmbedClass.Gray))
                continue; // Forbidden (fsType blocks embedding) / NotFound — never offered
            string detail = cls == FontEmbedInfo.FontEmbedClass.Free
                ? "embeddable (OFL/libre)"
                : "embeddable - license unverified";
            if (cjk)
                detail += " - CJK";
            items.Add(new CompletionItem
            {
                Label = family,
                Kind = CompletionItemKind.Value,
                Detail = detail,
                // Free before Gray; within a class, CJK-capable first; then by name.
                SortText = (cls == FontEmbedInfo.FontEmbedClass.Free ? "0" : "1")
                    + (cjk ? "0" : "1") + family,
            });
        }
        return new CompletionList { Items = items.ToArray() };
    }

    // The written tempo forms — a bare BPM, a marking text, a beat-unit equation, or a
    // swing feel. Completing the `tempo` keyword re-opens suggestions (Command) so these
    // forms enumerate right after it; the Insert holds each form's placeholder snippet.
    private static readonly (string Label, string Insert, string Detail)[] TempoForms =
    {
        ("120", "${1:120}", "Metronome mark: ♩ = 120"),
        ("\"Allegro\" 132", "\"${1:Allegro}\" ${2:132}", "Marking text + BPM: Allegro (♩ = 132)"),
        ("\"Grave\" 4 = 54", "\"${1:Grave}\" ${2:4} = ${3:54}", "Marking + beat unit = BPM (4. = dotted unit)"),
        ("120 swing", "${1:120} swing", "Swing feel (eighths; 'swing 16' for sixteenths)"),
    };

    /// <summary>The written tempo forms, as fill-in snippets — after <c>tempo</c>
    /// nothing else fits (a bare BPM, a marking text, a beat-unit equation, or
    /// a swing feel).</summary>
    internal static CompletionList GetTempoCompletions()
    {
        return new CompletionList
        {
            Items = TempoForms.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.Snippet,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = t.Insert,
                Detail = t.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>Common meters offered after <c>time</c>.</summary>
    internal static CompletionList GetTimeCompletions()
    {
        var meters = new (string Label, string Detail)[]
        {
            ("4/4", "Common time (engraved as C)"),
            ("3/4", "Waltz / minuet"),
            ("2/4", "March / polka"),
            ("2/2", "Cut time (engraved as ¢)"),
            ("6/8", "Compound duple (jig)"),
            ("9/8", "Compound triple (slip jig)"),
            ("12/8", "Compound quadruple (shuffle)"),
            ("3/8", "Fast triple"),
            ("5/4", "Quintuple"),
            ("7/8", "Septuple"),
        };
        return new CompletionList
        {
            Items = meters.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = t.Detail,
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>Common pickup lengths offered after <c>partial</c> (the note-
    /// duration grammar: number + optional dots).</summary>
    internal static CompletionList GetPartialCompletions()
    {
        var durations = new (string Label, string Detail)[]
        {
            ("4", "Quarter-note pickup"),
            ("8", "Eighth-note pickup"),
            ("2", "Half-note pickup"),
            ("4.", "Dotted-quarter pickup"),
            ("2.", "Dotted-half pickup (three quarters)"),
            ("8.", "Dotted-eighth pickup"),
        };
        return new CompletionList
        {
            Items = durations.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = t.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>The render-spec keywords valid inside a score / grandStaff body.</summary>
    internal static CompletionList GetScoreBlockCompletions()
    {
        // Retrigger = the item takes a part-name reference next, so re-open the
        // completion popup after inserting the keyword and list the declared parts
        // (grandStaff opens a brace block instead, so it doesn't).
        var specs = new (string Label, string Insert, string Detail, bool Retrigger)[]
        {
            ("staff", "staff $0", "A staff rendering the named part", true),
            ("tab", "tab $0", "A tablature staff for the named part", true),
            ("grandStaff", "grandStaff {\n\t$0\n}", "Braced staff group (piano)", false),
            ("chords", "chords $0", "Chord row (no staff) for the named chord part", true),
            ("lyrics", "lyrics $0", "Lyrics row (no staff) for the named lyrics part", true),
        };
        return new CompletionList
        {
            Items = specs.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = t.Insert,
                Detail = t.Detail,
                SortText = i.ToString(),
                Command = t.Retrigger
                    ? new Command { Title = "Suggest part name", CommandIdentifier = "editor.action.triggerSuggest" }
                    : null,
            }).ToArray()
        };
    }

    /// <summary>After <c>staff NAME with</c> the only continuation is <c>chords</c>.</summary>
    internal static CompletionList GetWithCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem
                {
                    Label = "chords",
                    Kind = CompletionItemKind.Keyword,
                    Detail = "Attach a chord part's symbols above this staff",
                },
            ]
        };
    }

    /// <summary>After <c>chords NAME</c> / <c>with chords NAME</c>: the chord DISPLAY
    /// selector (<c>as roman | as both | as names</c>), then the ordinary render-item
    /// continuations so a following <c>staff</c>/<c>chords</c>/… is not blocked.</summary>
    internal static CompletionList GetChordAttachNameCompletions()
    {
        var items = new System.Collections.Generic.List<CompletionItem>
        {
            AsItem("as roman", "Show chord symbols as Roman-numeral degrees (I, IIm7, V7)", "0"),
            AsItem("as both", "Show both: the Roman degree stacked above the chord name", "1"),
            AsItem("as names", "Show absolute chord names (C, Am7) — the default", "2"),
        };
        // The next render item can also start here; keep those, sorted after `as …`.
        foreach (var it in GetScoreBlockCompletions().Items)
        {
            it.SortText = "9" + (it.SortText ?? "");
            items.Add(it);
        }
        return new CompletionList { Items = items.ToArray() };

        static CompletionItem AsItem(string label, string detail, string sort) => new()
        {
            Label = label,
            Kind = CompletionItemKind.Keyword,
            InsertText = label,
            Detail = detail,
            SortText = sort,
        };
    }

    /// <summary>After <c>… as</c>: the three chord display modes.</summary>
    internal static CompletionList GetChordDisplayModeCompletions()
    {
        var modes = new (string Label, string Detail)[]
        {
            ("roman", "Roman-numeral degrees for the key (I, IIm7, V7)"),
            ("both", "The Roman degree stacked above the absolute chord name"),
            ("names", "Absolute chord names (C, Am7)"),
        };
        return new CompletionList
        {
            Items = modes.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.Keyword,
                Detail = t.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>After <c>tab … as</c>: the two tab display styles.</summary>
    internal static CompletionList GetTabDisplayModeCompletions()
    {
        var modes = new (string Label, string Detail)[]
        {
            ("numbers", "Fret digits only — no stems, dots or rests"),
            ("full", "Full tablature staff with stems, dots and rests (the default)"),
        };
        return new CompletionList
        {
            Items = modes.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.Keyword,
                Detail = t.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>Names declared as <c>KEYWORD name {</c> anywhere in the document
    /// (parts, chord parts, lyrics parts), offered where a score references them.</summary>
    internal static CompletionList GetDeclaredNameCompletions(string text, string keyword, string detail)
    {
        var names = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (Match m in Regex.Matches(text, $@"\b{keyword}\s+(\w+)\s*\{{"))
        {
            var name = m.Groups[1].Value;
            if (seen.Add(name))
                names.Add(name);
        }
        return new CompletionList
        {
            Items = names.Select((n, i) => new CompletionItem
            {
                Label = n,
                Kind = CompletionItemKind.Reference,
                Detail = detail,
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>The octave-mode words valid right after <c>octave</c>. A bare
    /// integer (<c>octave 3</c>, the part-header base re-anchor) is also legal
    /// there but is typed, not completed.</summary>
    internal static CompletionList GetOctaveCompletions()
    {
        var modes = new (string Label, string Detail)[]
        {
            ("absolute", "Absolute octaves: bare c = C4; ' / , are absolute offsets per note"),
            ("relative", "Relative octaves (default): each note nearest the previous"),
        };
        return new CompletionList
        {
            Items = modes.Select((m, i) => new CompletionItem
            {
                Label = m.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = m.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>Tonic pitches offered right after <c>key</c>, in circle-of-fifths
    /// order (sharps up, then flats down) so related keys sit together.</summary>
    internal static CompletionList GetKeyTonicCompletions()
    {
        var tonics = new (string Label, string Detail)[]
        {
            ("c", "0 ♯/♭ (major)"), ("g", "1 ♯"), ("d", "2 ♯"), ("a", "3 ♯"),
            ("e", "4 ♯"), ("b", "5 ♯"), ("fis", "6 ♯"), ("cis", "7 ♯"),
            ("f", "1 ♭"), ("bes", "2 ♭"), ("ees", "3 ♭"), ("aes", "4 ♭"),
            ("des", "5 ♭"), ("ges", "6 ♭"), ("ces", "7 ♭"),
        };
        return new CompletionList
        {
            Items = tonics.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = $"Tonic — {t.Detail} signature",
                // Insert the tonic + a space and re-open suggestions, so picking a tonic
                // lands on `key TONIC ` with the scale list ENUMERATED (nothing pre-filled).
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = $"{t.Label} $0",
                Command = new Command { Title = "Suggest scale", CommandIdentifier = "editor.action.triggerSuggest" },
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    // The key modes. Picking a tonic re-opens suggestions (Command) so these modes
    // enumerate right after `key TONIC ` — nothing is pre-filled.
    private static readonly (string Label, string Detail)[] KeyModes =
    {
        ("major", "Major (ionian)"),
        ("minor", "Natural minor (aeolian): major − 3 sharps"),
        ("ionian", "Ionian (= major)"),
        ("dorian", "Dorian: major − 2 sharps"),
        ("phrygian", "Phrygian: major − 4 sharps"),
        ("lydian", "Lydian: major + 1 sharp"),
        ("mixolydian", "Mixolydian: major − 1 sharp"),
        ("aeolian", "Aeolian (= minor)"),
        ("locrian", "Locrian: major − 5 sharps"),
    };

    /// <summary>The modes valid after <c>key TONIC</c> — nothing else fits there.</summary>
    internal static CompletionList GetKeyModeCompletions()
    {
        return new CompletionList
        {
            Items = KeyModes.Select((m, i) => new CompletionItem
            {
                Label = m.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = m.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    // The clef names, high → low pitch. Completing the `clef` keyword re-opens
    // suggestions (Command) so this list enumerates right after it.
    private static readonly (string Label, string Detail)[] Clefs =
    {
        ("treble", "Treble (G) clef"),
        ("treble_8", "Treble clef sounding an octave lower (guitar/tenor)"),
        ("alto", "Alto (C) clef"),
        ("tenor", "Tenor (C) clef"),
        ("bass", "Bass (F) clef"),
    };

    internal static CompletionList GetClefCompletions()
    {
        return new CompletionList
        {
            // SortText keeps the high→low order (VS Code otherwise sorts by label).
            Items = Clefs.Select((c, i) => new CompletionItem
            {
                Label = c.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = c.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>The instrument-preset names valid right after the <c>instrument</c>
    /// part property (they set clef/octave/tuning defaults). Sourced from
    /// <see cref="InstrumentDefaults.KnownInstruments"/> so the list never drifts from
    /// what the compiler recognizes. When the request context is supplied, each item
    /// carries a TextEdit replacing the whole hyphenated token being typed: the
    /// client's default word range stops at '-', so accepting "piano-right" after
    /// typing "piano-" would otherwise leave the prefix in place
    /// ("piano-piano-right"); the explicit range also makes the client filter
    /// against the full hyphenated prefix.</summary>
    internal static CompletionList GetInstrumentCompletions(
        string? text = null, int offset = 0, Position? position = null)
    {
        LspRange? replaceRange = null;
        if (text != null && position != null)
        {
            int start = offset;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1])
                                 || text[start - 1] == '_' || text[start - 1] == '-'))
                start--;
            replaceRange = new LspRange
            {
                Start = new Position(position.Line, position.Character - (offset - start)),
                End = position,
            };
        }

        return new CompletionList
        {
            // SortText (zero-padded) preserves the family grouping — VS Code otherwise
            // sorts by label, which would scatter e.g. "double-bass" among the woodwinds.
            Items = InstrumentDefaults.KnownInstruments.Select((name, i) => new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.EnumMember,
                Detail = "Instrument preset (clef/octave defaults)",
                SortText = i.ToString("D2"),
                TextEdit = replaceRange == null
                    ? null
                    : new TextEdit { Range = replaceRange, NewText = name },
            }).ToArray()
        };
    }

    internal static CompletionList GetTopLevelCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem { Label = "version", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "version ${1:1}", Detail = "Language version this file targets (a bare number; optional, first line)" },
                new CompletionItem { Label = "score", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "score main {\n\t$0\n}", Detail = "Score block" },
                new CompletionItem { Label = "part", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "part $1 {\n\t$0\n}", Detail = "Part declaration" },
                new CompletionItem { Label = "section", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "section $1 {\n\t$0\n}", Detail = "Section declaration" },
                new CompletionItem { Label = "phrase", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "phrase $1 {\n\t$0\n}", Detail = "Reusable phrase" },
                new CompletionItem { Label = "form", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "form main { $0 }", Detail = "Piece form (section play order)" },
                new CompletionItem { Label = "score", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "score main {\n\t$0\n}", Detail = "Printable score (visual layout)" },
                new CompletionItem { Label = "title", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "title \"$0\"", Detail = "Title metadata" },
                new CompletionItem { Label = "composer", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "composer \"$0\"", Detail = "Composer metadata" },
                new CompletionItem { Label = "font", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "font \"$0\"", Detail = "Text font; add `embedded` to subset-embed it in the exported PDF", Command = new Command { Title = "Suggest font name", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "tempo", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tempo $0", Detail = "Tempo (BPM)", Command = new Command { Title = "Suggest tempo", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "Time signature", Command = new Command { Title = "Suggest time signature", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "key $0", Detail = "Key signature", Command = new Command { Title = "Suggest key tonic", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "clef", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "clef $0", Detail = "Clef (treble/bass/alto/tenor)", Command = new Command { Title = "Suggest clef", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "override $1.$2 = $0", Detail = "Override grob property" },
                new CompletionItem { Label = "revert", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "revert $1.$0", Detail = "Revert grob property" },
                new CompletionItem { Label = "once", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "once override $1.$2 = $0", Detail = "One-time override" },
                new CompletionItem
                {
                    Label = "newscore",
                    FilterText = "newscore score template",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "// Twinkle, Twinkle, Little Star (public domain).\ntitle \"Twinkle, Twinkle, Little Star\"\ncomposer \"Jane Taylor\"\n\ntempo 100\ntime 4/4\nkey c major\n\npart melody {\n\tclef treble\n\tsection A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }\n\tsection B { g'4 g f f | e e d2 | }\n}\n\nform main { A |: B :| A \"A2\" }\n\nscore main {\n\tstaff melody\n}\n$0",
                    Detail = "Complete single-staff score (Twinkle template)",
                },
                new CompletionItem { Label = "lyrics", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "lyrics {\n\t$0\n}", Detail = "Lyrics block" },
                new CompletionItem
                {
                    Label = "grandstaff",
                    FilterText = "grandstaff piano",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "// Twinkle, Twinkle, Little Star (public domain) — piano.\ntitle \"Twinkle, Twinkle, Little Star\"\ncomposer \"Jane Taylor\"\n\ntempo 100\ntime 4/4\nkey c major\n\npart rh { clef treble }\npart lh { clef bass }\n\nsection A {\n\trh { c4 c g' g | a a g2 | f4 f e e | d d c2 | }\n\tlh { c,2 g | c2 c | f2 c | g2 c | }\n}\n\nform main { A }\n\nscore main {\n\tgrandStaff {\n\t\tstaff rh\n\t\tstaff lh\n\t}\n}\n$0",
                    Detail = "Two-staff (piano) score (grand staff template)",
                }
            ]
        };
    }

    /// <summary>
    /// Finds the key signature in force at <paramref name="offset"/> by scanning back
    /// for the nearest preceding <c>key &lt;tonic&gt; &lt;mode&gt;</c> declaration, and
    /// returns its sharp(+)/flat(-) count (0 = C major / no key found).
    /// </summary>
    private static int CurrentKeySharps(string text, int offset)
    {
        if (offset > text.Length) offset = text.Length;
        var prefix = text.Substring(0, offset);
        // tonic carries its own accidental suffix (fis, bes, …); mode is a word.
        var matches = System.Text.RegularExpressions.Regex.Matches(
            prefix, @"\bkey\s+([a-gA-G](?:is|es|isis|eses)?)\s+([A-Za-z]+)");
        if (matches.Count == 0) return 0;
        var last = matches[matches.Count - 1];
        return LilySharp.Core.Music.KeySpelling.SharpsFor(
            last.Groups[1].Value, last.Groups[2].Value) ?? 0;
    }

    /// <summary>True when <paramref name="offset"/> sits inside a <c>@chord(…)</c>
    /// argument — scan back on the current line to the nearest unclosed '(' and
    /// check its word is <c>chord</c> preceded by '@'.</summary>
    internal static bool IsInsideChordAnnotation(string text, int offset)
    {
        for (int i = Math.Min(offset, text.Length) - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c is ')' or '\n' or '\r')
                return false; // past a close paren, or off the line — not inside
            if (c != '(')
                continue;
            int e = i - 1, s = i - 1;
            while (s >= 0 && char.IsLetter(text[s])) s--;
            string name = s + 1 <= e ? text[(s + 1)..(e + 1)] : "";
            return name.Equals("chord", StringComparison.OrdinalIgnoreCase)
                && s >= 0 && text[s] == '@';
        }
        return false;
    }

    /// <summary>The seven diatonic chords of the key in force at
    /// <paramref name="offset"/> (C major when none), each a chord-name symbol like
    /// C, Dm, Em, F, G, Am, Bdim — computed from the key's tonic + signature.</summary>
    internal static CompletionList GetDiatonicChordCompletions(string text, int offset)
    {
        var prefix = text.Substring(0, Math.Min(offset, text.Length));
        var matches = Regex.Matches(
            prefix, @"\bkey\s+([a-gA-G](?:is|es|isis|eses)?)\s+([A-Za-z]+)");
        char tonic = 'c';
        int sharps = 0;
        if (matches.Count > 0)
        {
            var last = matches[^1];
            tonic = char.ToLowerInvariant(last.Groups[1].Value[0]);
            sharps = KeySpelling.SharpsFor(last.Groups[1].Value, last.Groups[2].Value) ?? 0;
        }

        // Each diatonic degree offers its triad, seventh, and suspended chords —
        // sorted in scale order (degree), then triad < 7th < sus4 < sus2 per root.
        // The label is the display symbol ("Dm7"); the inserted text is the shared
        // chords{}-style form ("d:m7") so @chord and chords{} stay in one format.
        static CompletionItem Item(string label, string insert, string detail, int degree, int rank) => new()
        {
            Label = label,
            InsertText = insert,
            Kind = CompletionItemKind.Value,
            Detail = detail,
            SortText = $"{degree:D2}{rank}",
        };

        var items = DiatonicChords.ForKey(tonic, sharps)
            .SelectMany(c => new[]
            {
                Item(c.Symbol, c.LilyRoot + c.LilyQualitySuffix, $"Diatonic triad ({c.Roman})", c.Degree, 0),
                Item(c.SeventhSymbol, $"{c.LilyRoot}:{c.SeventhQuality}", "Diatonic 7th", c.Degree, 1),
                Item(c.SusFourthSymbol, $"{c.LilyRoot}:sus4", "Suspended 4th", c.Degree, 2),
                Item(c.SusSecondSymbol, $"{c.LilyRoot}:sus2", "Suspended 2nd", c.Degree, 3),
            })
            .ToArray();
        return new CompletionList { Items = items };
    }

    /// <summary>
    /// True when the cursor sits in the MUSIC of a percussion part: ascend the
    /// unmatched-brace chain from the cursor, skip voice { } wrappers, take the
    /// first named block as the part reference, and check that part's
    /// declaration for `clef percussion`.
    /// </summary>
    internal static bool IsInsidePercussionPartMusic(string text, int offset)
        => IsInsidePercussionPartMusic(text, offset, out _);

    internal static bool IsInsidePercussionPartMusic(string text, int offset, out bool insideVoice)
    {
        insideVoice = false;
        int depth = 0;
        int end = Math.Min(offset, text.Length);
        var code = CodeMask(text, end); // ignore braces inside strings/comments
        for (int i = end - 1; i >= 0; i--)
        {
            if (!code[i]) continue;
            char ch = text[i];
            if (ch == '}') { depth++; continue; }
            if (ch != '{') continue;
            if (depth > 0) { depth--; continue; }

            // Word before this unmatched open brace
            int e = i - 1;
            while (e >= 0 && char.IsWhiteSpace(text[e])) e--;
            int s = e;
            while (s >= 0 && (char.IsLetterOrDigit(text[s]) || text[s] == '_')) s--;
            if (e < 0 || s == e) return false;
            string name = text[(s + 1)..(e + 1)];

            if (name.Equals("voice", StringComparison.OrdinalIgnoreCase))
            {
                insideVoice = true;
                continue; // ascend to the enclosing part block
            }

            // Structural keywords: this is not a part-music block.
            if (name is "section" or "score" or "form" or "part" or "phrase"
                or "grandstaff" or "lyrics" or "chords" or "repeat" or "tuplet"
                or "grace" or "acciaccatura" or "appoggiatura")
                return false;

            return PartIsPercussion(text, name);
        }
        return false;
    }

    private static bool PartIsPercussion(string text, string partName)
    {
        // Locate `part <partName> {`, then extract the BALANCED body (up to its
        // matching close brace) instead of stopping at the first `}`. The old
        // `[^}]*` regex mis-detected a percussion part whose body has a nested
        // voice/section block BEFORE its `clef percussion` (the nested `}` cut
        // the body short). Brace matching ignores `{`/`}` inside strings/comments.
        var head = System.Text.RegularExpressions.Regex.Match(text,
            @"\bpart\s+" + System.Text.RegularExpressions.Regex.Escape(partName) + @"\s*\{");
        if (!head.Success) return false;

        int open = head.Index + head.Length - 1; // index of the '{'
        var code = CodeMask(text, text.Length);
        int depth = 0, bodyEnd = text.Length;    // unclosed (mid-edit) → scan to end
        for (int i = open; i < text.Length; i++)
        {
            if (!code[i]) continue;
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) { bodyEnd = i; break; }
        }

        string body = text[(open + 1)..bodyEnd];
        return System.Text.RegularExpressions.Regex.IsMatch(body, @"\bclef\s*:?\s*percussion\b");
    }

    /// <summary>
    /// Drum-kit completions for a percussion part's music: the DrumNameRegistry
    /// vocabulary (aliases first — the idiomatic form), plus rests and the
    /// structural snippets that remain valid in drum music. No pitch letters.
    /// </summary>
    private static CompletionList GetDrumCompletions(bool insideVoice)
    {
        var items = new System.Collections.Generic.List<CompletionItem>();
        foreach (var kv in LilySharp.Core.Syntax.DrumNameRegistry.AliasEntries)
        {
            LilySharp.Core.Syntax.DrumNameRegistry.TryGet(kv.Key, out var info);
            items.Add(new CompletionItem
            {
                Label = kv.Key,
                Kind = CompletionItemKind.Value,
                Detail = $"{kv.Value} (GM {info.GmKey})",
                SortText = "0" + kv.Key,
            });
        }
        foreach (var kv in LilySharp.Core.Syntax.DrumNameRegistry.CanonicalEntries)
        {
            items.Add(new CompletionItem
            {
                Label = kv.Key,
                Kind = CompletionItemKind.Value,
                Detail = $"GM {kv.Value.GmKey}",
                SortText = "1" + kv.Key,
            });
        }
        items.AddRange(new[]
        {
            new CompletionItem { Label = "r", Kind = CompletionItemKind.Value, Detail = "Rest", SortText = "2r" },
            new CompletionItem { Label = "s", Kind = CompletionItemKind.Value, Detail = "Spacer rest (invisible)", SortText = "2s" },
            new CompletionItem { Label = "R", Kind = CompletionItemKind.Value, Detail = "Full-measure rest", SortText = "2R" },
            new CompletionItem { Label = "repeat", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "repeat percent 2 {\n\t$0\n}", Detail = "Repeat block (percent/unfold/tremolo)", SortText = "3repeat" },
            new CompletionItem { Label = "tuplet", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tuplet 3/2 { $0 }", Detail = "Tuplet (e.g., triplet)", SortText = "3tuplet" },
            new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "Change time signature", SortText = "4time", Command = new Command { Title = "Suggest time signature", CommandIdentifier = "editor.action.triggerSuggest" } },
            new CompletionItem { Label = "break", Kind = CompletionItemKind.Keyword, InsertText = "break", Detail = "Force a line/system break here", SortText = "4break" },
            new CompletionItem { Label = "nobreak", Kind = CompletionItemKind.Keyword, InsertText = "nobreak", Detail = "Forbid a line break here (LilyPond \\noBreak)", SortText = "4nobreak" },
        });
        // voice { } is only meaningful directly in the part's music —
        // NESTED voice blocks silently become parallel siblings (verified),
        // so the snippet is withheld inside a voice wrapper.
        if (!insideVoice)
            items.Add(new CompletionItem { Label = "voice", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "voice { $0 }", Detail = "Voice (hats up / kick+snare down)", SortText = "3voice" });
        return new CompletionList { IsIncomplete = false, Items = [.. items] };
    }

    internal static CompletionList GetMusicCompletions(string word, int keySharps, bool contracted = false, bool insideVoice = false)
    {
        var items = new System.Collections.Generic.List<CompletionItem>();

        // Pitches, spelled for the key in force at the cursor: in G major (one
        // sharp) the F row is offered as "fis", so accepting it writes the
        // sounding note. Filtering on the spelled form keeps the row visible
        // whether the user typed just "f" or the full "fis".
        foreach (char letter in "cdefgab")
        {
            int alt = LilySharp.Core.Music.KeySpelling.Alteration(
                LilySharp.Core.Music.KeySpelling.StepOf(letter), keySharps);
            string spelled = LilySharp.Core.Music.KeySpelling.SpellLetter(letter, keySharps);
            // lilysharp.completion.flatSpelling = "contracted": suggest the Dutch
            // contractions es/as instead of ees/aes. Only E-flat and A-flat have a
            // contraction; bes/des/ges/ces/fes have none and are left as-is.
            if (contracted)
                spelled = spelled switch { "ees" => "es", "aes" => "as", _ => spelled };
            string upper = char.ToUpperInvariant(letter).ToString();
            items.Add(new CompletionItem
            {
                Label = spelled,
                Kind = CompletionItemKind.Value,
                Detail = alt == 0
                    ? $"{upper} pitch"
                    : $"{upper}{(alt > 0 ? "♯" : "♭")} pitch (from key signature)",
                FilterText = spelled,
                InsertText = spelled,
                SortText = "0" + letter
            });
        }

        items.AddRange(new[]
        {
                // Rests
                new CompletionItem { Label = "r", Kind = CompletionItemKind.Value, Detail = "Rest", SortText = "1r" },
                new CompletionItem { Label = "s", Kind = CompletionItemKind.Value, Detail = "Spacer rest (invisible)", SortText = "1s" },
                new CompletionItem { Label = "R", Kind = CompletionItemKind.Value, Detail = "Full-measure rest", SortText = "1R" },

                // Structures
                new CompletionItem { Label = "|: :|", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "|: $0 :|", Detail = "Volta repeat (symbolic; add endings [1. …] [2. …])", SortText = "2repeat" },
                new CompletionItem { Label = "|: :| [1.][2.]", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "|: $1 [1. $2 ] :| [2. $0 ]", Detail = "Volta repeat with endings", SortText = "2repeatalt" },
                new CompletionItem { Label = "repeat", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "repeat unfold 2 {\n\t$0\n}", Detail = "Repeat block (unfold/percent/tremolo)", SortText = "2repeatkw" },
                new CompletionItem { Label = "tuplet", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tuplet 3/2 { $0 }", Detail = "Tuplet (e.g., triplet)", SortText = "2tuplet" },
                new CompletionItem { Label = "grace", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "grace { $0 }", Detail = "Grace notes", SortText = "2grace" },
                new CompletionItem { Label = "acciaccatura", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "acciaccatura { $0 }", Detail = "Slashed grace note", SortText = "2acciaccatura" },
                new CompletionItem { Label = "appoggiatura", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "appoggiatura { $0 }", Detail = "Unslashed grace note", SortText = "2appoggiatura" },
                new CompletionItem { Label = "break", Kind = CompletionItemKind.Keyword, InsertText = "break", Detail = "Force a line/system break here", SortText = "2break" },
                new CompletionItem { Label = "nobreak", Kind = CompletionItemKind.Keyword, InsertText = "nobreak", Detail = "Forbid a line break here (LilyPond \\noBreak)", SortText = "2nobreak" },

                // Mid-measure declarations
                new CompletionItem { Label = "clef", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "clef $0", Detail = "Change clef", SortText = "3clef", Command = new Command { Title = "Suggest clef", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "key $0", Detail = "Change key signature", SortText = "3key", Command = new Command { Title = "Suggest key tonic", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "Change time signature", SortText = "3time", Command = new Command { Title = "Suggest time signature", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "tempo", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tempo $0", Detail = "Change tempo (BPM)", SortText = "3tempo", Command = new Command { Title = "Suggest tempo", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "octave", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "octave $0", Detail = "Octave mode (absolute / relative)", SortText = "3octave", Command = new Command { Title = "Suggest octave mode", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "partial", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "partial $0", Detail = "Pickup: the next measure is a partial of this length", SortText = "3partial" },

                // Grob overrides
                new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "override $1.$2 = $0", Detail = "Override grob property", SortText = "4override" },
                new CompletionItem { Label = "revert", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "revert $1.$0", Detail = "Revert grob property", SortText = "4revert" },
                new CompletionItem { Label = "once", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "once override $1.$2 = $0", Detail = "One-time override", SortText = "4once" }
        });

        // Parallel voices (voice { } voice { }): only meaningful directly in the
        // part's music — nested voice blocks silently become siblings — so the
        // snippet is withheld once the cursor is already inside a voice wrapper.
        if (!insideVoice)
            items.Add(new CompletionItem { Label = "voice", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "voice { $0 }", Detail = "Parallel voice on this staff", SortText = "2voice" });

        // Chord note-expansion: a chord symbol the user is typing (cmaj7, am, g7)
        // offers to replace itself with the spelled note chord <c e g b> — the same
        // tone set that names and (later) voices the chord. The notes are bare so
        // relative mode voices them ascending. LILYPOND-REF: scm/chord-entry.scm.
        if (word.Length >= 2 && ChordStructure.TryParseSymbol(word, out var chord))
        {
            var notes = chord.ToNoteChord();
            items.Insert(0, new CompletionItem
            {
                Label = $"{word}  →  {notes}",
                Kind = CompletionItemKind.Snippet,
                FilterText = word,
                InsertText = notes,
                Detail = $"{chord.DisplayName} chord notes",
                SortText = "00chord",
            });
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>
    /// True when <paramref name="offset"/> sits right after a complete articulation
    /// name or its '.', so the '.up'/'.down' placement qualifier fits:
    /// '@fermata|', '@fermata.|', '@fermata.d|'. Only the char immediately before
    /// the cursor is inspected — a trailing space means the user moved on.
    /// </summary>
    private static bool IsArticulationPlacementContext(string text, int offset)
    {
        int i = offset - 1;
        if (i < 0) return false;

        // '@name.<partial>' — skip a partial placement word ('u', 'do', …) to the dot.
        int j = i;
        while (j >= 0 && char.IsLetter(text[j])) j--;
        if (j >= 0 && text[j] == '.')
        {
            int nameEnd = j - 1;
            int k = nameEnd;
            while (k >= 0 && (char.IsLetterOrDigit(text[k]) || text[k] == '-')) k--;
            return k >= 0 && text[k] == '@' && nameEnd > k
                && ArticulationRegistry.IsKnown(text.Substring(k + 1, nameEnd - k));
        }

        // '@name|' — cursor right after a COMPLETE articulation name (no dot, no
        // space). A partial name ('@ferm') is not IsKnown, so the '@' list keeps
        // showing until the name completes.
        if (char.IsLetter(text[i]))
        {
            int k = i;
            while (k >= 0 && (char.IsLetterOrDigit(text[k]) || text[k] == '-')) k--;
            return k >= 0 && text[k] == '@'
                && ArticulationRegistry.IsKnown(text.Substring(k + 1, i - k));
        }
        return false;
    }

    /// <summary>
    /// The '.up' / '.down' placement qualifier for an articulation. When the '.' is
    /// already typed ('@fermata.'), the bare words are offered; otherwise they carry
    /// the leading dot so '@fermata' → '@fermata.up'.
    /// </summary>
    internal static CompletionList GetArticulationPlacementCompletions(
        string text, int offset, Position? position = null)
    {
        int j = offset - 1;
        while (j >= 0 && char.IsLetter(text[j])) j--;
        bool afterDot = j >= 0 && text[j] == '.';
        string p = afterDot ? "" : ".";
        // Replace from the placement word (after the dot), or INSERT at the cursor
        // when there is no dot yet. Crucially the range must NOT reach back over the
        // 'fermata' NAME: VS Code filters the items against the text in this range —
        // 'fermata' matches neither '.up' nor '.down', so without an explicit range
        // the items are hidden and nothing appears (the '@fermata|' case).
        int replaceStart = afterDot ? j + 1 : offset;
        LspRange? range = position == null ? null : new LspRange
        {
            Start = new Position(position.Line, position.Character - (offset - replaceStart)),
            End = position,
        };

        CompletionItem Item(string word, string sort) => new()
        {
            Label = p + word,
            Kind = CompletionItemKind.EnumMember,
            Detail = word == "up"
                ? "Force this articulation ABOVE the note"
                : "Force this articulation BELOW the note",
            SortText = sort,
            FilterText = p + word,
            TextEdit = range == null ? null : new TextEdit { Range = range, NewText = p + word },
        };

        return new CompletionList
        {
            IsIncomplete = false,
            Items = new[] { Item("up", "0up"), Item("down", "1down") },
        };
    }

    /// <summary>
    /// True when the <c>@</c> being completed is attached to a chord (the nearest
    /// non-space char before it is <c>&gt;</c>). A bare <c>@chord</c> on a chord
    /// auto-derives the symbol, so it is offered WITHOUT the <c>(…)</c> the note
    /// form needs.
    /// </summary>
    private static bool AtFollowsChord(string text, int offset)
    {
        int i = offset - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
        // Skip a partial annotation word already typed after '@' (e.g. '@cho').
        if (i >= 0 && text[i] != '@')
            while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '-')) i--;
        if (i < 0 || text[i] != '@') return false;
        int j = i - 1;
        while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
        return j >= 0 && text[j] == '>';
    }

    internal static CompletionList GetArticulationCompletions(bool afterChord = false)
    {
        // Bare '@chord' on a chord auto-derives the symbol from its notes — no '(…)'.
        var chordItem = afterChord
            ? new CompletionItem
            {
                Label = "chord", Kind = CompletionItemKind.Value,
                Detail = "Auto chord name — derived from the chord's notes",
                InsertText = "chord", SortText = "8chord",
            }
            : new CompletionItem
            {
                Label = "chord", Kind = CompletionItemKind.Value,
                Detail = "Chord name — offers the current key's diatonic chords",
                InsertText = "chord($0)", InsertTextFormat = InsertTextFormat.Snippet,
                SortText = "8chord",
                Command = new Command
                {
                    Title = "Suggest chords",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            };

        return new CompletionList
        {
            Items =
            [
                // Articulations
                new CompletionItem { Label = "staccato", Kind = CompletionItemKind.Value, Detail = "Staccato articulation", SortText = "0staccato" },
                new CompletionItem { Label = "accent", Kind = CompletionItemKind.Value, Detail = "Accent", SortText = "0accent" },
                new CompletionItem { Label = "tenuto", Kind = CompletionItemKind.Value, Detail = "Tenuto", SortText = "0tenuto" },
                new CompletionItem { Label = "marcato", Kind = CompletionItemKind.Value, Detail = "Marcato", SortText = "0marcato" },
                new CompletionItem { Label = "fermata", Kind = CompletionItemKind.Value, Detail = "Fermata", SortText = "0fermata" },
                new CompletionItem { Label = "portato", Kind = CompletionItemKind.Value, Detail = "Portato (tenuto + staccato)", SortText = "0portato" },
                new CompletionItem { Label = "staccatissimo", Kind = CompletionItemKind.Value, Detail = "Staccatissimo (wedge)", SortText = "0staccatissimo" },
                new CompletionItem { Label = "upbow", Kind = CompletionItemKind.Value, Detail = "Up-bow (V, above)", SortText = "0upbow" },
                new CompletionItem { Label = "downbow", Kind = CompletionItemKind.Value, Detail = "Down-bow (frog, above)", SortText = "0downbow" },
                new CompletionItem { Label = "harmonic", Kind = CompletionItemKind.Value, Detail = "Harmonic circle ○ (a.k.a. @flageolet)", SortText = "0harmonic" },
                new CompletionItem { Label = "flageolet", Kind = CompletionItemKind.Value, Detail = "Harmonic circle ○ (a.k.a. @harmonic)", SortText = "0flageolet" },

                // Free expressive text
                new CompletionItem
                {
                    Label = "text", Kind = CompletionItemKind.Value,
                    Detail = "Free expressive text below the note (\"dolce\", \"pizz.\", …); .up for above",
                    InsertText = "text(\"$0\")", InsertTextFormat = InsertTextFormat.Snippet,
                    SortText = "0text",
                },

                // Ornaments
                new CompletionItem { Label = "trill", Kind = CompletionItemKind.Value, Detail = "Trill ornament", SortText = "1trill" },
                new CompletionItem { Label = "mordent", Kind = CompletionItemKind.Value, Detail = "Mordent ornament", SortText = "1mordent" },
                new CompletionItem { Label = "prall", Kind = CompletionItemKind.Value, Detail = "Inverted mordent (pralltriller)", SortText = "1prall" },
                new CompletionItem { Label = "turn", Kind = CompletionItemKind.Value, Detail = "Turn ornament", SortText = "1turn" },
                new CompletionItem { Label = "invertedturn", Kind = CompletionItemKind.Value, Detail = "Inverted turn", SortText = "1invertedturn" },

                // Dynamics (@ prefix style)
                new CompletionItem { Label = "p", Kind = CompletionItemKind.Value, Detail = "Piano (soft)", SortText = "2p" },
                new CompletionItem { Label = "f", Kind = CompletionItemKind.Value, Detail = "Forte (loud)", SortText = "2f" },
                new CompletionItem { Label = "pp", Kind = CompletionItemKind.Value, Detail = "Pianissimo", SortText = "2pp" },
                new CompletionItem { Label = "ff", Kind = CompletionItemKind.Value, Detail = "Fortissimo", SortText = "2ff" },
                new CompletionItem { Label = "mp", Kind = CompletionItemKind.Value, Detail = "Mezzo-piano", SortText = "2mp" },
                new CompletionItem { Label = "mf", Kind = CompletionItemKind.Value, Detail = "Mezzo-forte", SortText = "2mf" },
                new CompletionItem { Label = "sfz", Kind = CompletionItemKind.Value, Detail = "Sforzato accent dynamic", SortText = "2sfz" },
                new CompletionItem { Label = "fp", Kind = CompletionItemKind.Value, Detail = "Forte-piano accent dynamic", SortText = "2fp" },
                new CompletionItem { Label = "cresc", Kind = CompletionItemKind.Value, Detail = "Crescendo hairpin", SortText = "2cresc" },
                new CompletionItem { Label = "decresc", Kind = CompletionItemKind.Value, Detail = "Decrescendo hairpin", SortText = "2decresc" },

                // Navigation signs (segno / coda / fine / D.S. / D.C. / to coda) are
                // NOT offered here: they are standalone BARE landmarks, not note
                // modifiers ('@'), so they come from the music / form completions.
                // Rehearsal mark: @mark("A") drops a boxed label. Shown as a bare
                // "mark" (like @text), but completes straight into the quotes so the
                // caret lands where the label is typed.
                new CompletionItem { Label = "mark", Kind = CompletionItemKind.Value, InsertText = "mark(\"$0\")", InsertTextFormat = InsertTextFormat.Snippet, Detail = "Rehearsal mark (boxed label)", SortText = "3mark" },

                // Spanners and brackets
                new CompletionItem { Label = "rit", Kind = CompletionItemKind.Value, Detail = "Ritardando text spanner", SortText = "4rit" },
                new CompletionItem { Label = "accel", Kind = CompletionItemKind.Value, Detail = "Accelerando text spanner", SortText = "4accel" },
                new CompletionItem { Label = "ottava", Kind = CompletionItemKind.Value, Detail = "Ottava (8va) bracket", SortText = "4ottava" },
                new CompletionItem { Label = "loco", Kind = CompletionItemKind.Value, Detail = "End ottava bracket", SortText = "4loco" },
                new CompletionItem { Label = "startTrillSpan", Kind = CompletionItemKind.Value, Detail = "Start trill spanner", SortText = "4startTrillSpan" },
                new CompletionItem { Label = "stopTrillSpan", Kind = CompletionItemKind.Value, Detail = "Stop trill spanner", SortText = "4stopTrillSpan" },

                // Pedal markings
                new CompletionItem { Label = "ped", Kind = CompletionItemKind.Value, Detail = "Sustain pedal on", SortText = "5ped" },
                new CompletionItem { Label = "ped(off)", Kind = CompletionItemKind.Value, Detail = "Sustain pedal off", SortText = "5ped.off" },
                new CompletionItem { Label = "sost", Kind = CompletionItemKind.Value, Detail = "Sostenuto pedal on", SortText = "5sost" },
                new CompletionItem { Label = "sost(off)", Kind = CompletionItemKind.Value, Detail = "Sostenuto pedal off", SortText = "5sost.off" },
                new CompletionItem { Label = "una(corda)", Kind = CompletionItemKind.Value, Detail = "Una corda pedal on", SortText = "5una.corda" },
                new CompletionItem { Label = "tre(corde)", Kind = CompletionItemKind.Value, Detail = "Una corda pedal off", SortText = "5tre.corde" },

                // Notation marks
                new CompletionItem { Label = "glissando", Kind = CompletionItemKind.Value, Detail = "Glissando to next note", SortText = "6glissando" },
                new CompletionItem { Label = "arpeggio", Kind = CompletionItemKind.Value, Detail = "Arpeggiate chord", SortText = "6arpeggio" },
                new CompletionItem { Label = "courtesy", Kind = CompletionItemKind.Value, Detail = "Force courtesy accidental", SortText = "6courtesy" },
                new CompletionItem { Label = "editorial", Kind = CompletionItemKind.Value, Detail = "Editorial (suggestion) accidental above the note", SortText = "6editorial" },

                // Figured bass — parenthesised, figures space-separated: @fig(6 4).
                new CompletionItem { Label = "fig(6)", Kind = CompletionItemKind.Value, Detail = "Figured bass: 6", SortText = "7fig" },
                new CompletionItem { Label = "fig(6 4)", Kind = CompletionItemKind.Value, Detail = "Figured bass: 6/4", SortText = "7fig" },
                new CompletionItem { Label = "fig(5 3)", Kind = CompletionItemKind.Value, Detail = "Figured bass: 5/3", SortText = "7fig" },

                // Chord name — on a note the '(…)' form (offers the key's diatonic
                // chords); on a chord the bare auto-derive form. Built above.
                chordItem
            ]
        };
    }

    private static CompletionList GetDynamicCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem { Label = "ppp", Kind = CompletionItemKind.Value, Detail = "Pianississimo" },
                new CompletionItem { Label = "pp", Kind = CompletionItemKind.Value, Detail = "Pianissimo" },
                new CompletionItem { Label = "p", Kind = CompletionItemKind.Value, Detail = "Piano" },
                new CompletionItem { Label = "mp", Kind = CompletionItemKind.Value, Detail = "Mezzo-piano" },
                new CompletionItem { Label = "mf", Kind = CompletionItemKind.Value, Detail = "Mezzo-forte" },
                new CompletionItem { Label = "f", Kind = CompletionItemKind.Value, Detail = "Forte" },
                new CompletionItem { Label = "ff", Kind = CompletionItemKind.Value, Detail = "Fortissimo" },
                new CompletionItem { Label = "fff", Kind = CompletionItemKind.Value, Detail = "Fortississimo" },
                new CompletionItem { Label = "cresc", Kind = CompletionItemKind.Value, Detail = "Crescendo" },
                new CompletionItem { Label = "decresc", Kind = CompletionItemKind.Value, Detail = "Decrescendo" },
                new CompletionItem { Label = "dim", Kind = CompletionItemKind.Value, Detail = "Diminuendo" }
            ]
        };
    }

}
