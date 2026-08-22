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

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();

    // `override [once] Grob.property = <partial>` at the END of a line: group 1 is the
    // property (after the last '.' before '='). The partial value carries no whitespace,
    // so the caret is at the value position whether it sits just after '= ' or mid-value.
    [GeneratedRegex(@"\boverride\b.*?\.([A-Za-z][\w-]*)\s*=\s*[^\s=]*$")]
    private static partial Regex OverrideValueRegex();

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
        if (IsInsideChordAnnotation(doc.Text, offset) || IsInsideChordsBlock(doc.Text, offset))
            return GetDiatonicChordCompletions(doc.Text, offset);

        return context switch
        {
            CompletionContext.TopLevel => GetTopLevelCompletions(doc.Text, offset),
            // A percussion part's music block offers the drum-kit vocabulary,
            // not pitch letters (LILYPOND-REF: \drummode note names).
            CompletionContext.MusicBlock => IsInsidePercussionPartMusic(doc.Text, offset, out bool inVoice)
                ? GetDrumCompletions(inVoice)
                : GetMusicCompletions(word, CurrentKeySharps(doc.Text, offset), _flatSpellingContracted, inVoice),
            CompletionContext.FormBlock => GetFormCompletions(doc.Text),
            CompletionContext.PartBlock => GetPartBlockCompletions(doc.Text, offset),
            CompletionContext.LyricsBlock => GetLyricsSectionCompletions(doc.Text, offset),
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
            // The key the caret sits after decides which values fit: a generic family takes
            // only quoted names, a role or group may also redirect to a family.
            CompletionContext.AfterFontRoleKey =>
                GetFontRoleValueCompletions(WordBeforeCursor(doc.Text, offset)),
            CompletionContext.ScoreBlock => GetScoreBlockCompletions(),
            CompletionContext.StaffGroupBlock => GetStaffGroupBlockCompletions(),
            CompletionContext.AfterStaffRef => GetDeclaredNameCompletions(doc.Text, "part", "Part"),
            CompletionContext.AfterChordsRef => GetDeclaredNameCompletions(doc.Text, "chords", "Chord part"),
            CompletionContext.AfterLyricsRef => GetDeclaredNameCompletions(doc.Text, "lyrics", "Lyrics part"),
            CompletionContext.AfterLyricsName => GetVoiceBindingNameCompletions(doc.Text),
            CompletionContext.AfterLyricsTrackName => GetLyricsTrackNameCompletions(),
            CompletionContext.AfterSingsTarget => GetVoiceBindingNameCompletions(doc.Text,
                "The part - or named voice - this lyrics track sings"),
            CompletionContext.AfterChordAttachName => GetChordAttachNameCompletions(),
            CompletionContext.AfterStaffAttachName => GetStaffAttachNameCompletions(),
            CompletionContext.AfterGroupStaffAttachName => GetGroupStaffAttachNameCompletions(),
            CompletionContext.AfterLyricsRowAttachName => GetLyricsRowAttachNameCompletions(),
            CompletionContext.AfterGroupLyricsRowAttachName => GetGroupLyricsRowAttachNameCompletions(),
            CompletionContext.AfterStaffLinesAs => GetStaffLinesSelectorCompletions(),
            CompletionContext.AfterStaffLinesValue => GetStaffLinesValueCompletions(),
            CompletionContext.AfterChordDisplayAs => GetChordDisplayModeCompletions(),
            CompletionContext.AfterTabDisplayAs => GetTabDisplayModeCompletions(),
            CompletionContext.AfterInstrument => GetInstrumentCompletions(doc.Text, offset, position),
            CompletionContext.AfterRemoveEmpty => GetRemoveEmptyCompletions(),
            // The bare-@chord item is offered only when the group before the '@'
            // will actually auto-name; an unrecognizable one falls back to the
            // note form — @chord() with the caret inside the parens.
            CompletionContext.AfterAt => MatchAnywhere(
                GetArticulationCompletions(
                    AtFollowsChord(doc.Text, offset) && GroupBeforeAtAutoNames(doc.Text, offset)),
                PartialAnnotationName(doc.Text, offset)),
            CompletionContext.AfterArticulationPlacement => PlacementAndStillMatchingNames(
                doc.Text, offset, position,
                AtFollowsChord(doc.Text, offset) && GroupBeforeAtAutoNames(doc.Text, offset)),
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
    /// True when <paramref name="offset"/> sits inside a <c>fonts { … }</c> block.
    /// </summary>
    /// <remarks>
    /// The block is UNNAMED, so its introducing keyword is the frame's
    /// <see cref="BlockFrame.Name"/> — the word immediately before the <c>{</c>. A font
    /// block never nests, so the innermost frame is the whole test.
    /// </remarks>
    internal static bool IsInsideFontBlock(string text, int offset)
    {
        // Unnamed: the word before `{` is `fonts` (the frame's Name). NAMED —
        // `fonts house {` — the word before `{` is the NAME and `fonts` is one
        // word further back (the frame's Prefix), the same shape as a part block.
        var frames = ScanOpenBlocks(text, offset, ReadFrame);
        return frames.Count > 0
            && (frames[^1].Name == "fonts" || frames[^1].Prefix == "fonts");
    }

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>paper { … }</c> block;
    /// <paramref name="inSpacingBlock"/> is true when it sits one level deeper, in a
    /// nested spacing block (<c>systemSystemSpacing { | }</c>) — the innermost frame is
    /// then the spacing key and <c>paper</c> is the frame outside it.
    /// </summary>
    internal static bool IsInsidePaperBlock(string text, int offset, out bool inSpacingBlock)
    {
        // A frame is a paper block when `paper` is the word before its `{` (unnamed)
        // or one word further back (named — `paper wide {`, the part-block shape).
        static bool IsPaperFrame(BlockFrame f) => f.Name == "paper" || f.Prefix == "paper";

        inSpacingBlock = false;
        var frames = ScanOpenBlocks(text, offset, ReadFrame);
        if (frames.Count == 0)
            return false;
        if (IsPaperFrame(frames[^1]))
            return true;
        if (frames.Count >= 2 && IsPaperFrame(frames[^2]))
        {
            inSpacingBlock = true;
            return true;
        }
        return false;
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
    /// True when <paramref name="offset"/> sits directly inside a container that holds
    /// part-major inner sections — a <c>part</c> or <c>lyrics</c> block. (A chords track
    /// has the same shape, but its body completes the chord vocabulary, intercepted
    /// earlier.) Used to offer the document's not-yet-used section names after
    /// <c>section</c>.
    /// </summary>
    internal static bool IsInsideSectionContainer(string text, int offset)
    {
        var frames = ScanOpenBlocks(text, offset, ReadFrame);
        if (frames.Count == 0)
            return false;
        var f = frames[^1];
        // The introducing keyword is the Prefix for a NAMED block (`lyrics words {`) but
        // the Name for an UNNAMED one (`lyrics {`, whose name is optional): pick whichever
        // holds the keyword, as IsInsideChordsBlock does.
        string keyword = f.Prefix is "part" or "lyrics" or "chords" or "section" ? f.Prefix : f.Name;
        return keyword is "part" or "lyrics";
    }

    /// <summary>The keyword introducing a block frame: the <see cref="BlockFrame.Prefix"/> for
    /// a NAMED block (<c>lyrics words {</c> → "lyrics"), else the <see cref="BlockFrame.Name"/>
    /// for an unnamed one (<c>lyrics {</c> → "lyrics").</summary>
    private static string FrameKeyword(BlockFrame f)
        => f.Prefix is "part" or "lyrics" or "chords" or "section" ? f.Prefix : f.Name;

    /// <summary>
    /// True when <paramref name="offset"/> sits DIRECTLY inside a top-level
    /// <c>lyrics [name] { }</c> track — the innermost open block is that lyrics block AND it
    /// is not nested in a <c>section</c> / <c>part</c> (a note-bound section cell like
    /// <c>section A { melody {} lyrics {} }</c> holds syllables, not <c>section</c> entries).
    /// A top-level track has no enclosing block, so the lyrics frame is the ONLY open one.
    /// </summary>
    internal static bool IsInsideTopLevelLyricsBlock(string text, int offset)
    {
        var frames = ScanOpenBlocks(text, offset, ReadFrame);
        return frames.Count == 1 && FrameKeyword(frames[0]) == "lyrics";
    }

    /// <summary>
    /// True when <paramref name="offset"/> sits DIRECTLY inside a top-level
    /// <c>section [name] { }</c> — the innermost (and only) open block is that section. A
    /// section nested in a <c>part</c> is a part-major cell (its body is that part's music,
    /// not part blocks), so it is excluded.
    /// </summary>
    internal static bool IsInsideTopLevelSectionBody(string text, int offset)
    {
        var frames = ScanOpenBlocks(text, offset, ReadFrame);
        return frames.Count == 1 && FrameKeyword(frames[0]) == "section";
    }

    /// <summary>True when the document declares at least one <c>part NAME { … }</c>.</summary>
    private static bool HasDeclaredParts(string text)
    {
        foreach (Match m in DeclaredNameRegex().Matches(text))
            if (m.Groups[1].Value == "part") return true;
        return false;
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
    /// (e.g. "serif" in <c>fonts { serif "Noto|" }</c>, "title" in <c>title "My Song|"</c>). Empty
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
        // Skip an optional quoted display name sitting right before the brace
        // (`part melody "Violin I" {`). Without this the closing quote is read as the
        // (empty) NAME word, so `part … "…" {` is not recognized as a part block and
        // its body falls through to the music completions (note names, `break`).
        if (j >= 0 && text[j] == '"')
        {
            j--;                                    // past the closing quote
            while (j >= 0 && text[j] != '"') j--;   // back to the opening quote
            j--;                                    // past the opening quote
            while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
        }
        int end1 = j + 1;
        while (j >= 0 && IsWordChar(text[j])) j--;
        string name = text.Substring(j + 1, end1 - (j + 1));   // word before '{' (or before the display name)
        int k = j;
        while (k >= 0 && char.IsWhiteSpace(text[k])) k--;
        int end2 = k + 1;
        while (k >= 0 && IsWordChar(text[k])) k--;
        string prefix = text.Substring(k + 1, end2 - (k + 1));  // and the one before it
        return new BlockFrame(prefix, name);
    }

    // (GetChordQualityCompletions retired with the ':' entry format, 2026-08-23:
    // a chord is written as it prints, so there is no ':' to complete after.)

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
        foreach (Match m in SectionRefRegex().Matches(text))
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
        LyricsBlock,
        SectionBlock,
        AfterSection,
        AfterClef,
        AfterKey,
        AfterKeyTonic,
        AfterOctave,
        AfterOverride,
        AfterOverrideValue,
        AfterRevert,
        AfterTempo,
        AfterTime,
        AfterPartial,
        AfterTitleText,
        AfterFontName,
        AfterFontKeyword,
        FontBlock,
        AfterFontRoleKey,
        AfterPaperKeyword,
        PaperBlock,
        PaperSpecBlock,
        AfterFontsBlockRef,
        AfterPaperBlockRef,
        ScoreBlock,
        StaffGroupBlock,
        AfterStaffRef,
        AfterChordsRef,
        AfterLyricsRef,
        AfterLyricsName,
        AfterLyricsTrackName,
        AfterSingsTarget,
        AfterChordAttachName,
        AfterStaffAttachName,
        AfterGroupStaffAttachName,
        AfterLyricsRowAttachName,
        AfterGroupLyricsRowAttachName,
        AfterStaffLinesAs,
        AfterStaffLinesValue,
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
        // ⚠️ NOT inside a part header. There `octave` is a different production that takes a
        // number, and since 2026-08-19 SymbolCaseValidator refuses the two mode words in that
        // position — so offering them there is offering an error. They were legal-looking for
        // a long time because GRAMMAR.md listed them as part-property alternatives; a part
        // header ignored them outright (measured, MIDI byte-identical to no octave at all).
        if (prevWord == "octave" && !IsInsidePartBlock(text, offset))
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
                // `fonts |` with no block yet: offer the block forms — except inside a
                // score, where the item is a REFERENCE and the declared names fit.
                case "fonts":
                    return IsInsideScoreBlock(text, offset)
                        ? CompletionContext.AfterFontsBlockRef
                        : CompletionContext.AfterFontKeyword;
                // `paper |` with no block yet: same motion.
                case "paper":
                    return IsInsideScoreBlock(text, offset)
                        ? CompletionContext.AfterPaperBlockRef
                        : CompletionContext.AfterPaperKeyword;
                // `override |` (and `once override |`, whose previous word is also
                // `override`): offer the grob properties that actually affect the
                // rendered output as `Grob.property = value` fill-ins.
                case "override": return CompletionContext.AfterOverride;
                // `revert |`: the same targets, without a value (revert Grob.property).
                case "revert": return CompletionContext.AfterRevert;
            }

            // `override [once] Grob.property = |` → the values that fit the property
            // (colours for .color, true/false for .transparent). prevWord here is empty
            // (the char before the caret is '='), so this is a separate check.
            if (OverrideValueProperty(text, offset) is not null)
                return CompletionContext.AfterOverrideValue;
        }

        // Inside `fonts { … }` the body binds text ROLES to faces, so it is intercepted
        // before every fallthrough below.
        //
        // ⚠️ Without this the block reached the MusicBlock fallthrough at the end of this
        // method — the '{' is a brace like any other — and the popup offered PITCHES AND
        // ARTICULATIONS at every caret a writer reaches while filling one in. Measured
        // 2026-08-18, all twelve carets from `fonts {` to `fonts { serif "Georgia"  sans "|`.
        // The one-liner had two dedicated contexts and the block had none, which is most of
        // why the block read as the harder form to write.
        if (IsInsideFontBlock(text, offset))
        {
            // A quoted value: the same 188-face list the one-liner's string offers. The
            // owning keyword here is the ROLE KEY (`serif`), not `font`, so the switch
            // below could never have reached it.
            if (IsInsideStringLiteral(text, offset))
                return CompletionContext.AfterFontName;
            // `fonts { serif |` — a bound key takes quoted faces, and a role or group may
            // also be redirected to a generic family (`chordName serif`).
            if (TextRoles.TryParseKey(prevWord, out _, out _, out _))
                return CompletionContext.AfterFontRoleKey;
            // Anywhere else in the block a KEY is what belongs.
            return CompletionContext.FontBlock;
        }

        // Inside `paper { … }` a KEY is what belongs (a value is a number, which no list
        // serves); inside a nested spacing block, its four sub-keys. Intercepted before
        // the fallthroughs for the reason the fonts block is: without this the popup
        // offers pitches and articulations at every caret inside the block.
        if (IsInsidePaperBlock(text, offset, out bool inSpacingBlock))
            return inSpacingBlock ? CompletionContext.PaperSpecBlock : CompletionContext.PaperBlock;

        // Inside a "…" string value, the directive that OWNS the string decides the
        // completion. A `title`/`composer` string keeps its snippet (so the caret is served
        // whether it sits just before the opening quote or already inside it). Every other
        // string falls through unchanged.
        //
        // ⚠️ `font "Noto|"` used to be served here with the face list. It is not any more:
        // a bare string after `font` is the REMOVED one-liner (LYS8007), and completing a
        // face into it would help the writer finish a spelling the parser refuses. The face
        // list now has exactly one home — inside `fonts { … }`, handled above.
        switch (KeywordBeforeCurrentString(text, offset))
        {
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

        // Right after `section `: offer the section names known to the piece but not yet
        // declared in this scope, not the property list. Two scopes carry a fill-in:
        // inside a part { } / lyrics { } container (part-major inner section) and at the
        // top level (section-major declaration, filled from the form's references).
        // `section` inside a music body is not a declaration site, so it is skipped.
        if (prevWord == "section"
            && !IsInsideStringLiteral(text, offset)
            && (IsInsideSectionContainer(text, offset) || InnermostOpenBlock(text, offset) == null))
            return CompletionContext.AfterSection;

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
        // After its reference keywords only the declared part names fit.
        if (IsInsideScoreBlock(text, offset))
        {
            // `… as |` → a display selector, but which one depends on what the `as`
            // governs: `tab … as` takes numbers|full, `chords … as` takes
            // roman|both|names.
            if (prevWord == "as")
                return AsSelectorContext(text, offset);
            switch (prevWord)
            {
                case "staff": return CompletionContext.AfterStaffRef;
                // `tab` references a part too (an optional tuning may precede the
                // name, but the part is the useful suggestion right after `tab`).
                case "tab": return CompletionContext.AfterStaffRef;
                // `ossia NAME` references a part directly, like `staff`.
                case "ossia": return CompletionContext.AfterStaffRef;
                case "chords": return CompletionContext.AfterChordsRef;
                case "lyrics": return CompletionContext.AfterLyricsRef;
                // `lyrics NAME sings ▮` on a score row — the binding target, the
                // same list the definition's `sings` offers. Guarded by the word
                // two back being `lyrics`, so a part that happens to be named
                // sings placed as a bare MIDI item never reaches it.
                case "sings":
                    if (ThirdWordBeforeCursor(text, offset) == "lyrics")
                        return CompletionContext.AfterSingsTarget;
                    break;
                case "lines":
                    // `staff m as lines |` — the selector's value slot. A
                    // `lines` that no `as` governs falls through to the
                    // general score list.
                    if (SecondWordBeforeCursor(text, offset) == "as")
                        return CompletionContext.AfterStaffLinesValue;
                    break;
            }
            // What a staff GROUP's body accepts is narrower than the score's, and the
            // parser says so with its own diagnostics — so the popup must not offer the
            // wider list inside one:
            //   condensedStaff { combinedStaff … } → LYS6004 "cannot contain"
            //   grandStaff     { combinedStaff … } → LYS6011 "cannot contain"
            var openBlocks = ScanOpenBlocks(text, offset, ReadFrame);
            if (openBlocks.Count > 0)
            {
                string block = openBlocks[^1].Name;
                // condensedStaff / combinedStaff take BARE PART NAMES only.
                if (IsBarePartNameGroup(block))
                    return CompletionContext.AfterStaffRef;
                // grandStaff / staffGroup / choirStaff take `staff` items and
                // `lyrics NAME` verse rows (ParseGrandStaffRender; else LYS6011).
                if (IsStaffGroupKeyword(block))
                {
                    // `staff NAME ▮` inside the group: a member takes the
                    // `as lines N` selector too, so offer it beside the group's
                    // own NARROW continuations (never the score-wide list —
                    // a chords row in here is LYS6011).
                    if (SecondWordBeforeCursor(text, offset) == "staff")
                        return CompletionContext.AfterGroupStaffAttachName;
                    // `lyrics NAME ▮` inside the group: a verse row states its
                    // binding here too (`sings PART`), then the group's own
                    // narrow continuations.
                    if (SecondWordBeforeCursor(text, offset) == "lyrics")
                        return CompletionContext.AfterGroupLyricsRowAttachName;
                    return CompletionContext.StaffGroupBlock;
                }
            }
            // `chords NAME |`: after the chord row's name, offer the
            // `as roman|both|names` display selector (plus the normal
            // continuations, so a following render item is not blocked).
            if (SecondWordBeforeCursor(text, offset) == "chords")
                return CompletionContext.AfterChordAttachName;
            // `lyrics NAME |`: after the row's track name, offer the `sings`
            // binding (plus the normal continuations) — the row states the same
            // binding the definition does.
            if (SecondWordBeforeCursor(text, offset) == "lyrics")
                return CompletionContext.AfterLyricsRowAttachName;
            // `staff NAME |` / `ossia NAME |`: after the part name, offer the
            // `as lines N` selector (plus the normal continuations), the same
            // shape as the chords row's `as` above.
            if (SecondWordBeforeCursor(text, offset) is "staff" or "ossia")
                return CompletionContext.AfterStaffAttachName;
            return CompletionContext.ScoreBlock;
        }

        // `lyrics ▮` at a DEFINITION site (outside a score block): the optional
        // voice-binding name aligns the track to a voice/part (`lyrics sop { … }`), so
        // offer the declared voice/part names — not the note stream. (A score-block
        // `lyrics NAME` is a row reference, handled as AfterLyricsRef above.)
        if (prevWord == "lyrics" && !IsInsideStringLiteral(text, offset))
            return CompletionContext.AfterLyricsName;

        // `lyrics NAME ▮` at a DEFINITION site: the binding keyword — `lyrics ja
        // sings vocal { }` states at the DEFINITION which melody the track sings
        // (user decision 2026-08-19; a score only PLACES the row). A `{` between
        // the words empties the scanned word, so a track BODY never triggers it;
        // a score-block `lyrics` row was answered inside the score branch above.
        if (SecondWordBeforeCursor(text, offset) == "lyrics"
            && !IsInsideStringLiteral(text, offset))
            return CompletionContext.AfterLyricsTrackName;

        // `lyrics NAME sings ▮` — the binding target. Guarded by the word TWO
        // back being `lyrics`, so an English lyric syllable "sings" inside a
        // body (`{ he sings ▮ }`) never reaches it.
        if (prevWord == "sings"
            && ThirdWordBeforeCursor(text, offset) == "lyrics"
            && !IsInsideStringLiteral(text, offset))
            return CompletionContext.AfterSingsTarget;

        // Directly inside a top-level `lyrics [name] { }` track (NOT a note-bound section
        // cell like `section A { melody {} lyrics {} }`, and NOT an inner section's syllable
        // body): the body holds `section NAME { syllables }`, so offer the document's section
        // names as `section NAME { }` scaffolds instead of note names.
        if (IsInsideTopLevelLyricsBlock(text, offset) && !IsInsideStringLiteral(text, offset))
            return CompletionContext.LyricsBlock;

        // Directly inside a top-level `section { }` in a doc WITH parts: the body holds PART
        // BLOCKS (`melody { … }`), not notes — so offer the declared parts as cell scaffolds,
        // not the pitch letters. (A section in a NO-parts doc is a single voice and keeps its
        // note completions; a section nested in a part is a part-major cell, likewise music.)
        if (IsInsideTopLevelSectionBody(text, offset) && HasDeclaredParts(text)
            && !IsInsideStringLiteral(text, offset))
            return CompletionContext.SectionBlock;

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

    // Most-reached-for first, which is not alphabetical; SortText preserves it. A property the
    // compiler grows and this array has not heard of sorts to the end rather than vanishing.
    private static readonly string[] PartPropertyOrder =
    {
        "clef", "instrument", "tuning", "octave", "transpose",
        "transposition", "pedal", "removeEmpty",
    };

    // Prose per property, and whether the editor has a VALUE list to enumerate for it.
    // ⚠️ Values must be true only where a value context actually exists — AfterClef,
    // AfterInstrument, AfterRemoveEmpty. It is false for `octave` because the part-header
    // `octave` takes a NUMBER (the AfterOctave context is gated to the top-level directive),
    // and false for `tuning`/`pedal`/`transposition`, which have no value context at
    // all. Setting it re-opens suggestions onto whatever list is general to the position,
    // which is worse than not offering to help.
    // ⚠️ Where a description NAMES a vocabulary it is joined from the compiler's list, not
    // typed out. A description is the one place a wrong word costs nothing to write and is
    // never noticed — `octave` advertised `absolute | relative` here for as long as those words
    // did nothing, and went on advertising them for a day after they became an ERROR.
    // A count is a copy of a list too, so `clef` states its two sizes from the two lists.
    //
    // ★★★ THE RULE these strings obey, and which CompletionVocabularyTests enforces:
    // ANYTHING IN PARENTHESES OR BACKTICKS IS SOMETHING THE WRITER MAY TYPE, and is compiled
    // to prove it. Ordinary prose is free — which is why `octave` mentions absolute/relative in
    // running text and NOT in the parentheses where it used to offer them. The rule is worth
    // the small awkwardness because it is the exact shape the old description broke.
    private static readonly System.Collections.Generic.Dictionary<string, (string Detail, bool Values)>
        PartPropertyDetails = new()
        {
            ["clef"] = ($"Clef — {LanguageVocabulary.PartClefNames.Count} names in a part header, "
                        + $"{LanguageVocabulary.ClefNames.Count} inside music", true),
            ["instrument"] = ("Instrument preset — sets the clef, octave and tuning defaults", true),
            ["tuning"] = ($"Tab tuning ({string.Join("/", LanguageVocabulary.TuningNames)})", false),
            ["octave"] = ("Base octave for this part — a whole number, e.g. `octave 3`. "
                          + "The words absolute and relative belong to the TOP-LEVEL octave "
                          + "directive and a part header refuses them", false),
            ["transpose"] = ("Transpose target pitch, e.g. `transpose d`, `transpose bes,`", false),
            ["transposition"] = ($"Sounding-octave marker ({string.Join("/", LanguageVocabulary.TranspositionMarkers)})", false),
            ["pedal"] = ($"Piano pedal style ({string.Join("/", LanguageVocabulary.PedalStyles)})", false),
            ["removeEmpty"] = ("Hara-kiri: hide this staff in rest-only systems "
                               + $"({string.Join(" | ", LanguageVocabulary.RemoveEmptyValues)})", true),
        };

    /// <summary>The property names a part { } header accepts (bare `name value`
    /// pairs plus inner sections), matching docs/GRAMMAR.md PartProperty.</summary>
    internal static CompletionList GetPartPropertyCompletions()
    {
        // ⚠️ The NAMES come from the compiler (LanguageVocabulary), not from this table. Until
        // 2026-08-19 they came from the table and it had gone wrong in both directions at once:
        // it listed six of the nine properties — `transposition`, `lines` and `pedal` were
        // simply absent, so the editor denied that three properties of the language existed —
        // and it described `octave` as taking `absolute | relative`, two words a part header has
        // never read and has REFUSED since the day before (measured: `part p { octave relative }`
        // is now an error). The list below supplies PROSE for a name; it cannot add or withhold
        // one. A property the compiler grows and this table has not been told about is still
        // offered, without a description.
        //
        // Values = the property takes a value LIST the editor can enumerate — so it is true only
        // where a value context actually exists (AfterClef, AfterInstrument, AfterRemoveEmpty).
        // ⚠️ Setting it for a property with no such context re-opens suggestions onto whatever
        // the general list is, which is worse than not offering to help.
        var props = new System.Collections.Generic.List<(string Label, string? Detail, bool Values)>();
        foreach (string name in LanguageVocabulary.PartPropertiesTakingAValuePair
                     .OrderBy(n => System.Array.IndexOf(PartPropertyOrder, n) is var i && i >= 0
                         ? i : int.MaxValue))
        {
            var d = PartPropertyDetails.TryGetValue(name, out var found) ? found : (null, false);
            props.Add((name, d.Item1, d.Item2));
        }

        // `time` / `tempo` are NOT part properties — they are score-level (every part shares
        // one meter and tempo). They belong at the top level or in a section header, so they
        // are offered there, not here (LYS1026 rejects them in a part header).
        // `section` is not a property at all: it is the OTHER thing a part body holds.
        props.Add(("section", "Inner section (part-major form)", true));
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

    // Prose per value, in the order a reader wants them (the two hiding modes, then the
    // default). As with ClefDetails, membership of this table decides NOTHING — the words
    // come from the compiler.
    private static readonly string[] RemoveEmptyOrder = { "true", "all", "false" };

    private static readonly System.Collections.Generic.Dictionary<string, string> RemoveEmptyDetails = new()
    {
        ["true"] = "Hide in rest-only systems; the FIRST system keeps the staff (LP RemoveEmptyStaves)",
        ["all"] = "Hide in rest-only systems including the first (LP RemoveAllEmptyStaves)",
        ["false"] = "Never hide (default)",
    };

    /// <summary>
    /// The values valid right after the <c>removeEmpty</c> part property, READ FROM THE
    /// COMPILER. LILYPOND-REF: ly/context-mods-init.ly — RemoveEmptyStaves (keeps the first
    /// system) / RemoveAllEmptyStaves.
    /// </summary>
    /// <remarks>
    /// ⚠️ This held its own copy of the three words until 2026-08-19, and the day before that
    /// the compiler began REFUSING anything outside them. A private list was harmless while a
    /// stray word merely fell back to <c>false</c>; the moment the value is enforced, the same
    /// list one word out of date makes the editor propose text the compiler rejects. Nothing
    /// had gone wrong yet — the fix is to the shape, not to a symptom.
    /// ⚠️ The test that guarded this held its own fourth copy of the same three words, so it
    /// would have gone green through the drift it existed to catch.
    /// </remarks>
    internal static CompletionList GetRemoveEmptyCompletions()
    {
        var ordered = LanguageVocabulary.RemoveEmptyValues.OrderBy(
            n => System.Array.IndexOf(RemoveEmptyOrder, n) is var i && i >= 0 ? i : int.MaxValue);

        return new CompletionList
        {
            Items = ordered.Select((name, i) => new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.EnumMember,
                Detail = RemoveEmptyDetails.TryGetValue(name, out var d) ? d : null,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>The clef names valid right after the <c>clef</c> keyword, ordered
    /// from the highest-sounding clef to the lowest (not alphabetically).</summary>
    /// <summary>True for a bare pitch-name word (c…b with optional is/es
    /// accidental suffixes) — the tonic between `key` and its mode.</summary>
    internal static bool IsPitchName(string word)
        => BareNoteNameRegex().IsMatch(word);

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

    /// <summary>The word before <see cref="SecondWordBeforeCursor"/> — "lyrics" in
    /// <c>lyrics ja sings vo|</c>. Like its sibling, any non-word, non-space
    /// character (a brace, a quote) empties the scanned word.</summary>
    internal static string ThirdWordBeforeCursor(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
        int i = offset;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // skip the partial word
        for (int back = 0; back < 2; back++)
        {
            while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
            while (i > 0 && IsWordChar(text[i - 1])) i--;    // one full word back
        }
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
        int end = i;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the third word back
        return text.Substring(i, end - i);
    }

    /// <summary>
    /// Which display selector an <c>as</c> in a score block governs. <c>tab … as</c>
    /// takes <c>numbers | full</c>; a <c>chords</c> row's <c>as</c> takes
    /// <c>roman | both | names</c>. The word right before <c>as</c> is the target NAME
    /// in every case, so it can't disambiguate — scan the words before <c>as</c> for
    /// the nearest governing keyword. A <c>chords</c> seen first is the chord display;
    /// a <c>tab</c> seen first is the tab style; a <c>staff</c> or <c>ossia</c> seen
    /// first is the staff-line selector (<c>as lines N</c>). Anything else keeps the
    /// chord default.
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
            if (w == "chords") return CompletionContext.AfterChordDisplayAs;
            if (w == "tab") return CompletionContext.AfterTabDisplayAs;
            // `staff m as |` / `ossia m as |` — the one selector a staff takes
            // is the line count (`as lines N`).
            if (w == "staff" || w == "ossia") return CompletionContext.AfterStaffLinesAs;
        }
        return CompletionContext.AfterChordDisplayAs;
    }

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>score … { }</c> body or
    /// one of the staff GROUPS nested in it. A score block usually carries a name
    /// (<c>score "sheet" {</c> / <c>score practice {</c>) between the keyword
    /// and the brace, so the innermost-block scan must skip one quoted or bare
    /// name before reading the keyword.
    /// </summary>
    internal static bool IsInsideScoreBlock(string text, int offset)
    {
        var stack = ScanOpenBlocks(text, offset, IsScoreBlockOpener);
        return stack.Count > 0 && stack[^1];
    }

    /// <summary>The staff-group keywords whose <c>{ }</c> body is part of the render
    /// spec: they open a block directly inside a score.</summary>
    /// <remarks>
    /// ⚠️ The first three take <c>staff</c> ITEMS; <c>condensedStaff</c> and
    /// <c>combinedStaff</c> take BARE PART NAMES (a staff or a group inside one is a
    /// parse error — see ParseBarePartNameMembers), which is why the context split in
    /// DetermineContext hands those two the part names instead of the render keywords.
    /// </remarks>
    private static bool IsStaffGroupKeyword(string w) =>
        w is "grandStaff" or "staffGroup" or "choirStaff"
          or "condensedStaff" or "combinedStaff";

    /// <summary>The two groups whose body is a list of bare part names.</summary>
    internal static bool IsBarePartNameGroup(string w) =>
        w is "condensedStaff" or "combinedStaff";

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
        if (w1 == "score" || IsStaffGroupKeyword(w1))
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
            if (w == "score" || IsStaffGroupKeyword(w))
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
    /// The faces a binding may name: the two this engine BUNDLES, then the installed
    /// families that may be embedded into an exported PDF, annotated by license class and
    /// CJK coverage. Offered inside a <c>fonts { … "…" }</c> string.
    /// </summary>
    /// <param name="ownerKey">
    /// The key the string belongs to. <c>serif</c> and <c>sans</c> ask for a SHAPE, so the
    /// list is narrowed to it; every other key (a role or a group) may legitimately name any
    /// face, and gets the whole list.
    /// </param>
    internal static CompletionList GetFontNameCompletions(string ownerKey = "")
        => ownerKey switch
        {
            "serif" => _serifFaceCompletions ??= FacesOfShape(FontEmbedInfo.FaceShape.Serif),
            "sans" => _sansFaceCompletions ??= FacesOfShape(FontEmbedInfo.FaceShape.Sans),
            _ => _fontNameCompletions ??= new CompletionList
            {
                Items = [.. BundledFaceCompletions(),
                         .. BuildFontNameCompletions(EnumerateInstalledEmbeddableFonts()).Items],
            },
        };

    private static CompletionList? _serifFaceCompletions;
    private static CompletionList? _sansFaceCompletions;

    /// <summary>
    /// The faces that draw letters of one shape: the bundled face for that family, then the
    /// installed families the font itself classifies that way, then — in a marked tail — the
    /// ones that classify as nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ THE UNCLASSIFIED ARE KEPT, DELIBERATELY. <see cref="FontEmbedInfo.ShapeOf"/> reads
    /// the font's own OS/2 classification, and a font is free to fill in neither field:
    /// measured 2026-08-18 over 232 installed families, 16 answered nothing — among them
    /// SimSun, a CJK SERIF, and the whole Sitka family. Hiding them would make a real and
    /// wanted face unreachable from the binding that wants it, which is worse than a longer
    /// list; they sort last and say why.
    /// </para>
    /// <para>
    /// Ornamental, script and symbolic families ARE dropped here. They are neither shape,
    /// and a <c>serif</c>/<c>sans</c> binding is a statement about the document's prose. A
    /// score that wants a script face for one role still names it under that role's key,
    /// where the whole list is offered.
    /// </para>
    /// </remarks>
    private static CompletionList FacesOfShape(FontEmbedInfo.FaceShape want)
    {
        var items = new List<CompletionItem>
        {
            want == FontEmbedInfo.FaceShape.Sans
                ? BundledFace(TextFontMetrics.SansFamily, "sans")
                : BundledFace(TextFontMetrics.SerifFamily, "serif"),
        };

        foreach (var item in BuildFontNameCompletions(EnumerateInstalledEmbeddableFonts()).Items)
        {
            var shape = FontEmbedInfo.ShapeOf(item.Label!);
            if (shape == want)
                items.Add(Retiered(item, "0", item.Detail));
            else if (shape == FontEmbedInfo.FaceShape.Unknown)
                items.Add(Retiered(item, "9", item.Detail + " - unclassified, may not be "
                                                          + (want == FontEmbedInfo.FaceShape.Sans ? "sans" : "serif")));
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>The same item, moved into a tier. Sorting is by the WHOLE key, so the tier
    /// goes in front of the sort text the licence/CJK ranking already built.</summary>
    private static CompletionItem Retiered(CompletionItem item, string tier, string? detail) => new()
    {
        Label = item.Label,
        Kind = item.Kind,
        Detail = detail,
        SortText = tier + item.SortText,
    };

    /// <summary>The two faces this engine ships, which no enumeration of INSTALLED families
    /// can contain.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ They were the only faces missing from this list, and they are the only two present
    /// on every machine by construction. Skia enumerates installed families, a bundled face
    /// is shipped rather than installed, and <see cref="BuildFontNameCompletions"/> drops
    /// anything that classifies as <c>NotFound</c> — so the popup offered every face except
    /// the two the completion itself pre-fills a block with.
    /// </para>
    /// <para>
    /// ⚠️ This is the THIRD consumer of one question — "is this face available?". The
    /// metrics path answers it correctly (bundle before machine), the missing-face warning
    /// answered it wrongly until f7e18024, and this list answered it wrongly until now. One
    /// question, three readers, fixed one at a time because nothing looked at the set.
    /// </para>
    /// <para>
    /// They sort ahead of the installed families ("!" precedes every digit ordinally): a
    /// bundled face is the one choice that cannot make the page depend on the machine.
    /// </para>
    /// </remarks>
    private static CompletionItem[] BundledFaceCompletions() =>
    [
        BundledFace(TextFontMetrics.SerifFamily, "serif"),
        BundledFace(TextFontMetrics.SansFamily, "sans"),
    ];

    private static CompletionItem BundledFace(string family, string role) => new()
    {
        Label = family,
        Kind = CompletionItemKind.Value,
        Detail = $"bundled with Lily# - the default {role} face, present on every machine",
        SortText = "!" + role,
    };

    /// <summary>
    /// The body a <c>font</c> declaration is completed with, as an LSP snippet: the two
    /// generic families, each pre-filled with the face that role already uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ THE DEFAULTS ARE THE FACES THE DOCUMENT IS ALREADY IN, so accepting the completion
    /// and changing nothing does not move the page. Measured 2026-08-18: a book with
    /// <c>fonts { serif "TeX Gyre Schola"  sans "TeX Gyre Heros" }</c> and the same book with
    /// no <c>font</c> at all have IDENTICAL geometry — every coordinate, every extent — and
    /// differ only in carrying the <c>font-family</c> attribute explicitly, which the
    /// default omits because the document root already names it. Controls: swapping the two
    /// families, or binding <c>serif</c> alone, does move the page, so the comparison sees a
    /// real binding.
    /// </para>
    /// <para>
    /// ⚠️ The names come from <see cref="TextFontMetrics.SerifFamily"/> and
    /// <see cref="TextFontMetrics.SansFamily"/> rather than being typed here. They are one
    /// quantity, and the editor spelling it a second time is how a popup starts offering a
    /// face the engine stopped bundling.
    /// </para>
    /// <para>
    /// ⚠️ TWO placeholders, not one mirrored placeholder. An earlier draft wrote
    /// <c>${1:face}</c> into both so a single face could be typed once — but the two
    /// families have DIFFERENT defaults, and a mirror cannot carry two. A writer who wants
    /// one face everywhere types it in the first field and tabs to the second; a writer who
    /// wants to change only the prose face edits the first and leaves the second alone,
    /// which the mirror made impossible.
    /// </para>
    /// </remarks>
    private static string FontBlockSnippet(string tail)
        => "{\n  serif \"${1:" + TextFontMetrics.SerifFamily + "}\""
         + "\n  sans  \"${2:" + TextFontMetrics.SansFamily + "}\"" + tail + "\n}";

    /// <summary>
    /// At <c>font |</c> (the keyword typed, nothing after it): the block forms. There is no
    /// quoted item — the one-line <c>font "NAME"</c> was removed 2026-08-18, and an editor
    /// must not complete toward a spelling the parser refuses.
    /// </summary>
    internal static CompletionList GetFontDeclarationCompletions()
        => new()
        {
            Items =
            [
                new CompletionItem
                {
                    Label = "{ … }",
                    FilterText = "fonts",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = FontBlockSnippet("$0"),
                    Preselect = true,
                    SortText = "0",
                    Detail = "Bind the whole document's text (pre-filled with the faces in use)",
                },
                // An empty block, for a writer who wants to bind roles rather than the
                // document — the caret lands where a key goes and the key list opens.
                new CompletionItem
                {
                    Label = "{ }",
                    FilterText = "fonts",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "{\n  $0\n}",
                    SortText = "1",
                    Detail = "Bind faces per text role",
                    Command = new Command { Title = "Suggest role key", CommandIdentifier = "editor.action.triggerSuggest" },
                },
            ]
        };

    /// <summary>
    /// The keys a <c>fonts { }</c> body binds: the two generic families, the six role
    /// groups, and every individual role.
    /// </summary>
    /// <remarks>
    /// ⚠️ The vocabulary is read from <see cref="TextRoles.AllKeySpellings"/> — the ONE home
    /// the reader validates against — and never listed here. A hand-copied key list is the
    /// shape of rot this repo has met repeatedly, most recently in the score-item lists.
    /// <para>
    /// Each key inserts <c>key "…"</c> with the caret inside the quotes and re-triggers
    /// suggestions, so the face list appears without a second keystroke — the same motion
    /// the <c>font</c> keyword itself has.
    /// </para>
    /// </remarks>
    private static CompletionList? _fontBlockCompletions;

    internal static CompletionList GetFontBlockCompletions()
        => _fontBlockCompletions ??= new CompletionList
        {
            Items =
            [
                .. TextRoles.AllKeySpellings().Select(key => new CompletionItem
                {
                    Label = key,
                    Kind = CompletionItemKind.Property,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = key + " \"$0\"",
                    Detail = FontKeyDetail(key),
                    Command = new Command
                    {
                        Title = "Suggest font name",
                        CommandIdentifier = "editor.action.triggerSuggest",
                    },
                }),
                // `embedded` is an entry of the block too, not a key — it subsets every
                // named face into an exported PDF.
                new CompletionItem
                {
                    Label = "embedded",
                    Kind = CompletionItemKind.Keyword,
                    Detail = "Subset every named face into the exported PDF",
                },
            ],
        };

    /// <summary>
    /// At <c>paper |</c> (the keyword typed, nothing after it): the block forms, the
    /// same motion as <c>fonts</c>.
    /// </summary>
    /// <remarks>
    /// ★ THE PRE-FILLED VALUES ARE THE DEFAULTS (a4, 210mm x 297mm), so accepting the
    /// completion and changing nothing does not move the page — the reader's conversion
    /// rounds exactly the way the defaults were computed, and PaperBlockTests pins the
    /// equality.
    /// </remarks>
    internal static CompletionList GetPaperDeclarationCompletions()
        => new()
        {
            Items =
            [
                new CompletionItem
                {
                    Label = "{ … }",
                    FilterText = "paper",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "{\n  paperWidth ${1:210mm}\n  paperHeight ${2:297mm}$0\n}",
                    Preselect = true,
                    SortText = "0",
                    Detail = "Set the page's dimensions (pre-filled with the a4 defaults)",
                },
                new CompletionItem
                {
                    Label = "{ }",
                    FilterText = "paper",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "{\n  $0\n}",
                    SortText = "1",
                    Detail = "Set page dimensions key by key",
                    Command = new Command { Title = "Suggest paper key", CommandIdentifier = "editor.action.triggerSuggest" },
                },
            ]
        };

    /// <summary>
    /// The keys a <c>paper { }</c> body takes: the scalar lengths, the raggedRight
    /// flag, and the nested spacing blocks.
    /// </summary>
    /// <remarks>
    /// ⚠️ The vocabulary is read from <see cref="LanguageVocabulary.PaperScalarKeys"/> /
    /// <see cref="LanguageVocabulary.PaperSpacingKeys"/> — the reader's own table,
    /// published — and never listed here, for the reason the font key list is not.
    /// </remarks>
    private static CompletionList? _paperBlockCompletions;

    internal static CompletionList GetPaperBlockCompletions()
        => _paperBlockCompletions ??= new CompletionList
        {
            Items =
            [
                .. LanguageVocabulary.PaperScalarKeys.Select(key => new CompletionItem
                {
                    Label = key,
                    Kind = CompletionItemKind.Property,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = key + " $0",
                    Detail = PaperKeyDetail(key),
                }),
                new CompletionItem
                {
                    Label = "raggedRight",
                    Kind = CompletionItemKind.Keyword,
                    Detail = "Do not justify lines; measures sit at their ideal width",
                },
                .. LanguageVocabulary.PaperSpacingKeys.Select(key => new CompletionItem
                {
                    Label = key,
                    Kind = CompletionItemKind.Property,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = key + " { $0 }",
                    Detail = PaperKeyDetail(key),
                    Command = new Command
                    {
                        Title = "Suggest spacing sub-key",
                        CommandIdentifier = "editor.action.triggerSuggest",
                    },
                }),
            ],
        };

    /// <summary>The four lines of a nested spacing block.</summary>
    private static CompletionList? _paperSpecBlockCompletions;

    internal static CompletionList GetPaperSpecBlockCompletions()
        => _paperSpecBlockCompletions ??= new CompletionList
        {
            Items =
            [
                .. LanguageVocabulary.PaperSpacingSubKeys.Select(key => new CompletionItem
                {
                    Label = key,
                    Kind = CompletionItemKind.Property,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = key + " $0",
                    Detail = key switch
                    {
                        "basicDistance" => "Ideal distance between the pair (staff spaces)",
                        "minimumDistance" => "Absolute floor, whatever the skylines say",
                        "padding" => "Safety margin beyond the skyline distance",
                        "stretchability" => "Spring flexibility (unitless); larger stretches more",
                        _ => "Spacing sub-key",
                    },
                }),
            ],
        };

    /// <summary>One line of help per paper key — what the key reaches, and its default.</summary>
    private static string PaperKeyDetail(string key) => key switch
    {
        "paperWidth" => "Page width (default 210mm, a4). Bare numbers are staff spaces",
        "paperHeight" => "Page height (default 297mm, a4); 0 for one content-driven page",
        "leftMargin" => "Left margin (default 15mm)",
        "rightMargin" => "Right margin (default 15mm)",
        "topMargin" => "Top margin (default 10mm)",
        "bottomMargin" => "Bottom margin (default 10mm)",
        "indent" => "First system's indent (default 0 = from instrument names)",
        "shortIndent" => "Later systems' indent (default 0)",
        "topSystemPadding" => "Padding between the title and the first system",
        "spacingIncrement" => "Horizontal note-spacing unit (default 1.2 staff spaces)",
        "systemSystemSpacing" => "Between two consecutive systems",
        "scoreSystemSpacing" => "After a score boundary, before the next system",
        "markupSystemSpacing" => "After a title or markup, before the next system",
        "scoreMarkupSpacing" => "After a system, before the next title or markup",
        "markupMarkupSpacing" => "Between consecutive titles or markups",
        "topSystemSpacing" => "From the page top to the first system",
        "lastBottomSpacing" => "From the last element to the page bottom",
        "staffStaffSpacing" => "Between two staves of a group",
        "staffGroupStaffSpacing" => "Between a group's staff and the next group's",
        "defaultStaffStaffSpacing" => "Between ungrouped staves",
        "nonStaffRelatedStaffSpacing" => "A lyrics/chord row and the staff it belongs to",
        "nonStaffUnrelatedStaffSpacing" => "A lyrics/chord row and an unrelated staff",
        "nonStaffNonStaffSpacing" => "Between two lyrics/chord rows",
        _ => "Paper key",
    };

    /// <summary>One line of help per key, so the popup says what the key REACHES rather
    /// than only repeating its spelling.</summary>
    private static string FontKeyDetail(string key) => key switch
    {
        "serif" => "Generic family: everything except chord symbols falls back here",
        "sans" => "Generic family: chord symbols fall back here",
        "header" => "Group: title, composer, instrument names",
        "lyrics" => "Group: lyric syllables and stanza numbers",
        "chords" => "Group: chord symbols, diagrams, figured bass",
        "marks" => "Group: tempo, rehearsal marks, pedal, navigation, free text, dynamics",
        "numbers" => "Group: bar numbers, fingerings, tuplet / volta / ottava labels",
        "notation" => "Group: text that is really notation — the treble_8 digit, a "
                      + "compound meter's +, tab fret numbers. Reached ONLY when named",
        _ => "Text role",
    };

    /// <summary>
    /// After a key (<c>fonts { lyricText |</c>): the values THAT KEY takes.
    /// </summary>
    /// <param name="key">The key the caret sits after. A generic family narrows the list.</param>
    /// <remarks>
    /// <para>
    /// A role or a group takes a quoted face, or a generic family to FOLLOW instead
    /// (<c>chordName serif</c>). A GENERIC FAMILY takes only quoted names: pointing
    /// <c>serif</c> at <c>sans</c> is a re-classification and no role reads it, which
    /// <c>FontPlanReader</c> refuses with LYS8006 — "a generic family takes quoted face
    /// names, not another family".
    /// </para>
    /// <para>
    /// ⚠️ The list was flat until 2026-08-18 and offered the redirect after EVERY key, so
    /// at <c>fonts { serif |</c> the popup proposed exactly the two words the reader was
    /// about to refuse. The reader's own message even says the offer must not be made
    /// there — it "must not offer the family form the other keys accept" — and the editor
    /// made it anyway, because the value list did not know which key it was answering for.
    /// </para>
    /// <para>
    /// The quoted item comes first and is preselected, so the common motion (name a face)
    /// stays one keystroke; the redirect is a deliberate second choice.
    /// </para>
    /// </remarks>
    private static CompletionList? _fontValuesForRole;
    private static CompletionList? _fontValuesForFamily;

    internal static CompletionList GetFontRoleValueCompletions(string key = "")
    {
        var quoted = new CompletionItem
        {
            Label = "\"…\"",
            Kind = CompletionItemKind.Snippet,
            InsertTextFormat = InsertTextFormat.Snippet,
            InsertText = "\"$0\"",
            Preselect = true,
            SortText = "0",
            Detail = "Pick a bundled or installed, embeddable font",
            Command = new Command
            {
                Title = "Suggest font name",
                CommandIdentifier = "editor.action.triggerSuggest",
            },
        };

        // Is the key a generic family? Asked of TextRoles, the one home that decides it, so
        // a family added there needs no second edit here.
        bool isFamily = TextRoles.TryParseKey(key, out _, out _, out var family) && family != null;
        if (isFamily)
            return _fontValuesForFamily ??= new CompletionList { Items = [quoted] };

        return _fontValuesForRole ??= new CompletionList
        {
            Items =
            [
                quoted,
                new CompletionItem
                {
                    Label = "serif",
                    Kind = CompletionItemKind.Value,
                    SortText = "1",
                    Detail = "Follow whatever the serif family is bound to",
                },
                new CompletionItem
                {
                    Label = "sans",
                    Kind = CompletionItemKind.Value,
                    SortText = "1",
                    Detail = "Follow whatever the sans family is bound to",
                },
            ],
        };
    }

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
            // A family the bundle shadows is never offered off the machine: the engine
            // measures and draws these names from the bundled files no matter what is
            // installed (TextFontMetrics consults the bundle before the machine), so the
            // installed row would advertise a face the engine will silently not use — and
            // on a machine that installs TeX Gyre the same name appeared twice, with the
            // system row carrying the classification and the sort.
            if (TextFontMetrics.IsBundledFamilyName(family))
                continue;
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
        // completion popup after inserting the keyword and list the declared parts.
        // A keyword that opens a BRACE body does not retrigger — the caret lands
        // inside the block, where the next completion request answers on its own.
        // ⚠️ Every keyword ParseRenderItem accepts belongs here. The four staff
        // GROUPS were missing, so the one list the writer sees inside `score { }`
        // did not mention the constructs the parser has always taken.
        var specs = new (string Label, string Insert, string Detail, bool Retrigger)[]
        {
            ("staff", "staff $0", "A staff rendering the named part", true),
            ("grandStaff", "grandStaff {\n\t$0\n}", "Braced staff group (piano)", false),
            ("staffGroup", "staffGroup {\n\t$0\n}", "Bracketed staff group (orchestral family)", false),
            ("choirStaff", "choirStaff {\n\t$0\n}", "Choir staff group (vocal ensemble)", false),
            ("condensedStaff", "condensedStaff {\n\t$0\n}",
                "One staff carrying several parts as voices — bare part names inside", false),
            ("combinedStaff", "combinedStaff {\n\t$0\n}",
                "Two parts merged onto one staff, a2 where they agree — bare part names inside", false),
            ("tab", "tab $0", "A tablature staff for the named part", true),
            ("ossia", "ossia $0", "An ossia staff (small alternative reading) for the named part", true),
            ("chords", "chords $0", "Chord row (no staff) for the named chord part", true),
            ("lyrics", "lyrics $0", "Lyrics row (no staff) for the named lyrics part", true),
            ("title", "title \"$0\"", "This score's own title, overriding the file's", false),
            ("composer", "composer \"$0\"", "This score's own composer, overriding the file's", false),
            ("fonts", "fonts $0", "This score's faces: reference a named top-level fonts block", true),
            ("paper", "paper $0", "This score's page: reference a named top-level paper block", true),
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

    /// <summary>
    /// Inside <c>grandStaff</c> / <c>staffGroup</c> / <c>choirStaff</c>: the body is a
    /// run of <c>staff</c> items with <c>lyrics NAME</c> rows between them (a bound row
    /// is the staff above's verse — LYS6012 refuses any other), so that is the whole
    /// list. Anything else is LYS6011.
    /// </summary>
    internal static CompletionList GetStaffGroupBlockCompletions() => new()
    {
        Items =
        [
            new CompletionItem
            {
                Label = "staff",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "staff $0",
                Detail = "A staff of this group, rendering the named part",
                SortText = "0",
                Command = new Command
                {
                    Title = "Suggest part name",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
            new CompletionItem
            {
                Label = "lyrics",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "lyrics $0",
                Detail = "A verse row under the staff above (the track must sing that staff's part)",
                SortText = "1",
                Command = new Command
                {
                    Title = "Suggest lyrics name",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
        ]
    };

    /// <summary>After <c>staff NAME</c> / <c>ossia NAME</c>: the <c>as lines N</c>
    /// staff-line selector, then the ordinary render-item continuations so a
    /// following staff/chords/lyrics is not blocked — the same shape as
    /// <see cref="GetChordAttachNameCompletions"/>. The count moved OFF the part
    /// header (2026-08-19): it is a property of THIS rendering, so the same part
    /// can print five-lined in the full score and one-lined in a lead sheet.
    /// </summary>
    internal static CompletionList GetStaffAttachNameCompletions()
    {
        var items = new System.Collections.Generic.List<CompletionItem>
        {
            new CompletionItem
            {
                Label = "as lines",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "as lines $0",
                Detail = "Staff-line count for this staff - 1 is a one-line rhythm staff",
                SortText = "0",
                Command = new Command
                {
                    Title = "Suggest line count",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
        };
        // The next render item can also start here; keep those, sorted after.
        foreach (var it in GetScoreBlockCompletions().Items)
        {
            it.SortText = "9" + (it.SortText ?? "");
            items.Add(it);
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>After <c>staff NAME</c> INSIDE a staff group: the
    /// <c>as lines N</c> selector, then the group's own narrow continuations
    /// (<c>staff</c> / <c>lyrics</c> — a group refuses the wider score list,
    /// LYS6011), the group-body sibling of
    /// <see cref="GetStaffAttachNameCompletions"/>.</summary>
    internal static CompletionList GetGroupStaffAttachNameCompletions()
    {
        var items = new System.Collections.Generic.List<CompletionItem>
        {
            new CompletionItem
            {
                Label = "as lines",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "as lines $0",
                Detail = "Staff-line count for this staff - 1 is a one-line rhythm staff",
                SortText = "0",
                Command = new Command
                {
                    Title = "Suggest line count",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
        };
        foreach (var it in GetStaffGroupBlockCompletions().Items)
        {
            it.SortText = "9" + (it.SortText ?? "");
            items.Add(it);
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>The <c>sings</c> keyword item a lyrics ROW offers after its track
    /// name — the row spelling of the binding the definition states
    /// (<c>lyrics verse sings melody</c>). Shared by the score-body and
    /// group-body row contexts.</summary>
    private static CompletionItem RowSingsItem() => new()
    {
        Label = "sings",
        Kind = CompletionItemKind.Keyword,
        InsertTextFormat = InsertTextFormat.Snippet,
        InsertText = "sings $0",
        Detail = "Bind this track to the part it sings - the same binding the definition states",
        SortText = "0",
        Command = new Command
        {
            Title = "Suggest part name",
            CommandIdentifier = "editor.action.triggerSuggest",
        },
    };

    /// <summary>After <c>lyrics NAME</c> on a SCORE row: the <c>sings</c>
    /// binding, then the score's normal continuations — the lyrics-row sibling
    /// of <see cref="GetStaffAttachNameCompletions"/>.</summary>
    internal static CompletionList GetLyricsRowAttachNameCompletions()
    {
        var items = new System.Collections.Generic.List<CompletionItem> { RowSingsItem() };
        foreach (var it in GetScoreBlockCompletions().Items)
        {
            it.SortText = "9" + (it.SortText ?? "");
            items.Add(it);
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>After <c>lyrics NAME</c> INSIDE a staff group: the <c>sings</c>
    /// binding, then the group's own narrow continuations (never the score-wide
    /// list — LYS6011), the group-body sibling of
    /// <see cref="GetLyricsRowAttachNameCompletions"/>.</summary>
    internal static CompletionList GetGroupLyricsRowAttachNameCompletions()
    {
        var items = new System.Collections.Generic.List<CompletionItem> { RowSingsItem() };
        foreach (var it in GetStaffGroupBlockCompletions().Items)
        {
            it.SortText = "9" + (it.SortText ?? "");
            items.Add(it);
        }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>After <c>staff NAME as</c> / <c>ossia NAME as</c>: the one
    /// selector a staff takes — <c>lines</c>. The value is enumerated by the
    /// retrigger (<see cref="GetStaffLinesValueCompletions"/>).</summary>
    internal static CompletionList GetStaffLinesSelectorCompletions() => new()
    {
        Items =
        [
            new CompletionItem
            {
                Label = "lines",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "lines $0",
                Detail = "Staff-line count for this staff - 1 is a one-line rhythm staff",
                SortText = "0",
                Command = new Command
                {
                    Title = "Suggest line count",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
        ]
    };

    /// <summary>The staff-line counts, offered in the value slot of
    /// <c>as lines</c>. The range is the compiler's
    /// (<see cref="LanguageVocabulary.MinStaffLines"/>), never restated.</summary>
    internal static CompletionList GetStaffLinesValueCompletions() => new()
    {
        Items = System.Linq.Enumerable.Range(
                LanguageVocabulary.MinStaffLines,
                LanguageVocabulary.MaxStaffLines - LanguageVocabulary.MinStaffLines + 1)
            .Select(n => new CompletionItem
            {
                Label = n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Kind = CompletionItemKind.Value,
                InsertText = n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Detail = n == 1 ? "A one-line rhythm or percussion staff"
                    : n == LanguageVocabulary.MaxStaffLines ? $"{n} lines - the default"
                    : $"{n} lines",
                SortText = n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }).ToArray()
    };

    /// <summary>After <c>chords NAME</c>: the chord DISPLAY
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
        foreach (Match m in DeclaredNameRegex().Matches(text))
        {
            if (m.Groups[1].Value != keyword) continue;
            var name = m.Groups[2].Value;
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

    /// <summary>After <c>lyrics NAME</c> at a definition site: the binding
    /// keyword. <c>lyrics ja sings vocal { }</c> states at the DEFINITION which
    /// melody the track sings (a property of the track NAME, stated once; later
    /// same-name blocks may repeat it identically or omit it). A score then only
    /// PLACES the row — under the staff it sings, it is that staff's verse;
    /// anywhere else, words-only at the melody's rhythm.</summary>
    internal static CompletionList GetLyricsTrackNameCompletions() => new()
    {
        Items =
        [
            new CompletionItem
            {
                Label = "sings",
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = "sings $0",
                Detail = "Bind this track to the part it sings - a row under that staff is its verse",
                SortText = "0",
                Command = new Command
                {
                    Title = "Suggest part name",
                    CommandIdentifier = "editor.action.triggerSuggest",
                },
            },
        ]
    };

    /// <summary>
    /// The names a <c>lyrics</c> track's optional voice-binding name can align to — the
    /// declared parts (<c>part NAME { … }</c>, the usual target) and any explicitly named
    /// voices (<c>voice NAME { … }</c>) — deduplicated, parts first.
    /// </summary>
    internal static CompletionList GetVoiceBindingNameCompletions(string text,
        string detail = "Voice / part to align the lyrics to")
    {
        var items = new System.Collections.Generic.List<CompletionItem>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var keyword in new[] { "part", "voice" })
            foreach (Match m in DeclaredNameRegex().Matches(text))
            {
                if (m.Groups[1].Value != keyword) continue;
                var name = m.Groups[2].Value;
                if (seen.Add(name))
                    items.Add(new CompletionItem
                    {
                        Label = name,
                        Kind = CompletionItemKind.Reference,
                        Detail = detail,
                        SortText = seen.Count.ToString("D2"),
                    });
            }
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>
    /// After <c>section </c>, the section names known to the piece but not yet declared
    /// in this scope — so a section can be filled in with what is still missing. In a
    /// <c>part { }</c> / <c>lyrics { }</c> container the missing set is measured against
    /// the sections already in that container (part-major: <c>bass</c> already has
    /// <c>A</c>, so only <c>B</c> / <c>C</c> are offered); at the top level (section-major)
    /// it is measured against every declared section, so what remains is the sections the
    /// <c>form { }</c> references but that have not been written yet. The universe is every
    /// section NAME the document mentions — declarations AND form references (incl.
    /// <c>~silent</c> and volta alternatives). Picking one drops in the <c>{ }</c> body
    /// with the caret inside, unless a <c>{</c> already follows (then just the name is
    /// inserted). A brand-new name is typed freely; the list never blocks it.
    /// </summary>
    internal static CompletionList GetMissingSectionNameCompletions(string text, int offset)
    {
        // Every section NAME the document mentions — declaration names PLUS form
        // references (a section the piece plays but you may not have written yet) — in
        // document order, deduplicated. The parser resolves references robustly (bare,
        // ~silent, and [1. NAME] volta alternatives), which a text scan cannot.
        var known = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var root = SyntaxTree.Parse(text).GetRoot();
        foreach (var tok in SectionReferenceFinder.AllSectionNameTokens(root))
            if (seen.Add(tok.Text))
                known.Add(tok.Text);

        // What already fills this scope: inside a container, that container's sections;
        // at the top level, every declared section (so only form-only names remain).
        var here = IsInsideSectionContainer(text, offset)
            ? SectionsDeclaredInCurrentBlock(text, offset)
            : AllDeclaredSections(text, offset);

        // Completing the name opens the `{ }` body with the caret inside — UNLESS a `{`
        // already follows (the user is naming an existing braced section), in which case
        // just the name is inserted so no second body appears.
        bool hasBrace = SectionNameIsFollowedByBrace(text, offset);
        return new CompletionList
        {
            Items = known.Where(n => !here.Contains(n)).Select((n, i) => new CompletionItem
            {
                Label = n,
                Kind = CompletionItemKind.Reference,
                Detail = "Section not yet declared here",
                InsertTextFormat = hasBrace ? default : InsertTextFormat.Snippet,
                InsertText = hasBrace ? n : n + " {\n\t$0\n}",
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>
    /// Completions offered DIRECTLY inside a top-level <c>lyrics [name] { }</c> track: the
    /// document's section names not yet present in this track, each scaffolding a full
    /// <c>section NAME { … }</c> entry (a section-major lyrics track holds
    /// <c>section NAME { syllables }</c>). Unlike <see cref="GetMissingSectionNameCompletions"/>
    /// — offered AFTER the user types <c>section</c> — this fires before it, so the insert
    /// carries the <c>section</c> keyword. The grammar still allows a bare syllable stream
    /// here; this list is opt-in (Ctrl+Space) and never blocks typing lyrics.
    /// </summary>
    internal static CompletionList GetLyricsSectionCompletions(string text, int offset)
        => new() { Items = SectionScaffoldItems(text, offset, "Lyrics for this section").ToArray() };

    /// <summary>
    /// Section-name scaffold items — label <c>section NAME</c>, insert <c>section NAME { }</c>
    /// with the caret in the body — for the document's sections not yet present in the block
    /// at <paramref name="offset"/>. Shared by a top-level <c>lyrics { }</c> track and a
    /// <c>part { }</c> body, which both hold <c>section NAME { … }</c> entries. The
    /// <c>section</c> keyword is part of the LABEL (so the picker reads as <c>section A</c>,
    /// matching what is inserted), and sorts after a part's property list.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<CompletionItem> SectionScaffoldItems(
        string text, int offset, string detail, string nest = "\t", bool includeNewSection = true)
    {
        var known = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var root = SyntaxTree.Parse(text).GetRoot();
        foreach (var tok in SectionReferenceFinder.AllSectionNameTokens(root))
            if (seen.Add(tok.Text))
                known.Add(tok.Text);

        // Sections already written in THIS block are dropped, so the list is what is still
        // missing (the same measure as the after-`section` completion).
        var here = SectionsDeclaredInCurrentBlock(text, offset);
        bool hasBrace = SectionNameIsFollowedByBrace(text, offset);

        // Indent so the section never lands inline: on a fresh (whitespace-only) line the
        // plain snippet inherits that line's indent; when the caret sits after content (e.g.
        // right after `part melody {`), force the section onto its OWN new line one level
        // deeper (VS Code prepends the current line's indent to every following snippet line).
        bool freshLine = LineIsBlankBefore(text, WordStartBefore(text, offset));
        // head = the `section NAME` (or `section $1` for a new name); the tail is the body.
        // `nest` is one indent level for the enclosing block ("\t" inside part/lyrics; "" at
        // the top level, where a section sits at column 0).
        string Body(string head) => hasBrace ? head
            : freshLine ? head + " {\n\t$0\n}"
            : "\n" + nest + head + " {\n" + nest + "\t$0\n" + nest + "}";

        var items = known.Where(n => !here.Contains(n)).Select((n, i) => new CompletionItem
        {
            Label = "section " + n,
            Kind = CompletionItemKind.Reference,
            Detail = detail,
            InsertTextFormat = InsertTextFormat.Snippet,
            InsertText = Body("section " + n),
            SortText = "z" + i.ToString("D2"), // after a part { } body's properties (00..09)
        }).ToList();

        // A brand-NEW section: `section {}` with the caret first BETWEEN `section` and `{`
        // (the $1 name stop), then Tab drops into the body ($0). Offered even when every
        // known section is already present, so a fresh name is always one pick away. Excluded
        // where another `section` entry already exists (the top-level keyword list has one).
        if (includeNewSection)
            items.Add(new CompletionItem
            {
                Label = "section",
                Kind = CompletionItemKind.Keyword,
                Detail = "New section",
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = Body("section $1"),
                SortText = "zzz", // after the named scaffolds
            });
        return items;
    }

    /// <summary>The index where the identifier word at <paramref name="offset"/> begins
    /// (letters/digits), so indentation is judged from the line content BEFORE the partial
    /// word the completion will replace.</summary>
    private static int WordStartBefore(string text, int offset)
    {
        int i = offset;
        while (i > 0 && char.IsLetterOrDigit(text[i - 1])) i--;
        return i;
    }

    /// <summary>True when everything from the start of the line to <paramref name="pos"/> is
    /// whitespace — i.e. the caret is on its own (already-indented) line.</summary>
    private static bool LineIsBlankBefore(string text, int pos)
    {
        for (int i = pos - 1; i >= 0 && text[i] != '\n'; i--)
            if (!char.IsWhiteSpace(text[i])) return false;
        return true;
    }

    /// <summary>A <c>part { }</c> body's completions: its property names PLUS the document's
    /// section names as <c>section NAME { }</c> scaffolds — a part-major part holds properties
    /// AND inner sections. The bare <c>section</c> property is dropped: the scaffolds (and the
    /// "New section" item) are the one-step way in, so it would be a redundant second entry.</summary>
    internal static CompletionList GetPartBlockCompletions(string text, int offset)
    {
        var items = GetPartPropertyCompletions().Items
            .Where(p => p.Label != "section")
            .ToList();
        items.AddRange(SectionScaffoldItems(text, offset, "Section"));
        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>Completions offered DIRECTLY inside a top-level <c>section { }</c> in a doc
    /// WITH parts: the declared part names as <c>NAME { }</c> cell scaffolds. A section-major
    /// section's body holds part blocks (<c>melody { … }</c>), not notes — so this replaces
    /// the pitch-letter list there. A section sits at column 0, so a cell nests one level in.</summary>
    internal static CompletionList GetSectionBlockCompletions(string text, int offset)
    {
        // In a PART-MAJOR file the music lives in `part X { section A { … } }`, so a top-level
        // `section A { }` is a standalone HEADER: it carries section-wide directives (a pickup,
        // key, time, tempo, a section-scoped grob override) that apply to every part of the
        // section — never part cells. Offer those directives, not part names.
        if (LilySharp.Core.Editing.PartSectionLayoutConverter.Detect(SyntaxTree.Parse(text).GetRoot())
            == LilySharp.Core.Editing.LayoutForm.PartMajor)
            return new CompletionList { Items = SectionHeaderDirectiveItems() };

        // Section-major (or a parts file not yet committed to a layout): the section body holds
        // one music cell per part. Offer the declared part names as `NAME { }` cell scaffolds.
        var parts = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in DeclaredNameRegex().Matches(text))
            if (m.Groups[1].Value == "part" && seen.Add(m.Groups[2].Value))
                parts.Add(m.Groups[2].Value);

        bool freshLine = LineIsBlankBefore(text, WordStartBefore(text, offset));
        string Body(string head) => freshLine ? head + " {\n\t$0\n}" : "\n\t" + head + " {\n\t\t$0\n\t}";
        return new CompletionList
        {
            Items = parts.Select((n, i) => new CompletionItem
            {
                Label = n,
                Kind = CompletionItemKind.Reference,
                Detail = "Part cell — this section's music for " + n,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = Body(n),
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>The directives a top-level section HEADER may carry in a part-major file: a
    /// pickup and the section-wide key / time / tempo, plus a section-scoped grob override.
    /// They apply to every part of the section; clef is deliberately absent (it is per-part).</summary>
    private static CompletionItem[] SectionHeaderDirectiveItems() => new[]
    {
        new CompletionItem { Label = "partial", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "partial $0", Detail = "Pickup — shorten this section's first bar (applies to every part)" },
        new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "key $0", Detail = "This section's key signature", Command = new Command { Title = "Suggest key tonic", CommandIdentifier = "editor.action.triggerSuggest" } },
        new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "This section's time signature", Command = new Command { Title = "Suggest time signature", CommandIdentifier = "editor.action.triggerSuggest" } },
        new CompletionItem { Label = "tempo", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tempo $0", Detail = "This section's tempo (BPM)" },
        new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "override $0", Detail = "Grob override — a default for this section on every staff", Command = new Command { Title = "Suggest grob property", CommandIdentifier = "editor.action.triggerSuggest" } },
    };

    /// <summary>
    /// The section names already declared in the <c>part { }</c> / <c>lyrics { }</c>
    /// block that encloses <paramref name="offset"/> — EXCLUDING the (possibly
    /// incomplete) <c>section</c> declaration at the cursor itself, so the name being
    /// typed is never filtered out of <see cref="GetMissingSectionNameCompletions"/>.
    /// </summary>
    private static System.Collections.Generic.HashSet<string> SectionsDeclaredInCurrentBlock(string text, int offset)
    {
        var declared = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var mask = CodeMask(text, text.Length);

        // The innermost still-open '{' at the cursor is the enclosing container body.
        var stack = new System.Collections.Generic.List<int>();
        int limit = Math.Min(offset, text.Length);
        for (int i = 0; i < limit; i++)
        {
            if (!mask[i]) continue;
            if (text[i] == '{') stack.Add(i);
            else if (text[i] == '}' && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
        }
        if (stack.Count == 0)
        {
            // Top level: the enclosing "block" is the whole file, so its own sections are
            // those declared at brace depth 0. A `section` nested in a part / lyrics track is
            // that container's cell, NOT a top-level section, so it must not count — else a
            // part-major `section B` would be treated as already present and never offered for
            // pulling up to the top level.
            int curTop = SectionKeywordStartBeforeCursor(text, offset);
            int d = 0;
            for (int i = 0; i < text.Length;)
            {
                if (!mask[i]) { i++; continue; }
                char c = text[i];
                if (c == '{') { d++; i++; continue; }
                if (c == '}') { if (d > 0) d--; i++; continue; }
                if (char.IsLetter(c) || c == '_')
                {
                    int s = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    if (d == 0 && s != curTop && text[s..i] == "section")
                    {
                        int j = i;
                        while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                        int ns = j;
                        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                        if (j > ns) declared.Add(text[ns..j]);
                    }
                    continue;
                }
                i++;
            }
            return declared;
        }
        int open = stack[^1];

        // Its matching '}' (or end of document if the container is still unclosed).
        int depth = 0, close = text.Length;
        for (int i = open; i < text.Length; i++)
        {
            if (!mask[i]) continue;
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) { close = i; break; }
        }

        // The cursor's own `section` keyword start (skip the partial name + whitespace,
        // then the preceding word), excluded so an in-place edit of `section B {` still
        // offers B.
        int curKw = SectionKeywordStartBeforeCursor(text, offset);

        foreach (Match m in SectionDeclRegex().Matches(text[open..close]))
        {
            if (open + m.Index == curKw) continue;
            declared.Add(m.Groups[1].Value);
        }
        return declared;
    }

    /// <summary>
    /// True when the section declaration at the cursor ALREADY has an open body — the
    /// next non-whitespace character after the (possibly partial) name is <c>{</c>. Then
    /// completing the name must not add a second <c>{ }</c>; it inserts just the name.
    /// </summary>
    private static bool SectionNameIsFollowedByBrace(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
        int i = Math.Clamp(offset, 0, text.Length);
        while (i < text.Length && IsWordChar(text[i])) i++;        // rest of the partial name
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++; // whitespace to the brace
        return i < text.Length && text[i] == '{';
    }

    /// <summary>
    /// Every section declared anywhere in the document (section-major top-level or
    /// part-major inner), EXCLUDING the declaration at the cursor itself. At the top
    /// level this is the scope a new <c>section</c> joins, so subtracting it from the
    /// known universe leaves the form-referenced sections not yet written.
    /// </summary>
    private static System.Collections.Generic.HashSet<string> AllDeclaredSections(string text, int offset)
    {
        var declared = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        int curKw = SectionKeywordStartBeforeCursor(text, offset);
        foreach (Match m in SectionDeclRegex().Matches(text))
        {
            if (m.Index == curKw) continue;
            declared.Add(m.Groups[1].Value);
        }
        return declared;
    }

    /// <summary>Start index of the bare word two tokens before <paramref name="offset"/>
    /// (skip the partial word being typed, the whitespace, then the preceding word) —
    /// the <c>section</c> keyword in the after-<c>section</c> completion context.</summary>
    private static int SectionKeywordStartBeforeCursor(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
        int i = Math.Min(offset, text.Length);
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the partial name
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--; // whitespace
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the `section` keyword
        return i;
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

    /// <summary>
    /// The grob-property targets the renderer actually CONSUMES: colouring and hiding
    /// note heads / stems — the same four rows as <c>SupportedGrobOverrides</c>, which
    /// LYS1029 enforces. Anything else parses and stores but is refused, so it is
    /// deliberately NOT offered — that would mislead (NoteColumn.force-hshift left this
    /// list 2026-08-23 together with its vocabulary row: its reader is disabled, see
    /// ElementCoordinator.ForceHshiftEnabled). Shared by
    /// <see cref="GetOverrideCompletions"/> (which appends <c>= value</c>) and
    /// <see cref="GetRevertCompletions"/> (which does not).
    /// </summary>
    private static readonly (string Grob, string Property, string Kind, string Detail)[] RenderedGrobProperties =
    {
        ("NoteHead", "color", "color", "Colour the note heads"),
        ("Stem", "color", "color", "Colour the stems"),
        ("NoteHead", "transparent", "bool", "Show or hide the note head"),
        ("Stem", "transparent", "bool", "Show or hide the stem"),
    };

    /// <summary>
    /// The grob-property overrides offered right after <c>override</c> (and
    /// <c>once override</c>). Each inserts <c>Grob.property = </c> and — for a property
    /// with an enumerable value (a colour, or true/false) — re-opens the suggest popup so
    /// the value list appears next, exactly like <c>key</c>/<c>clef</c>. No value is
    /// pre-filled. See <see cref="RenderedGrobProperties"/> for why the set is limited.
    /// </summary>
    internal static CompletionList GetOverrideCompletions()
    {
        return new CompletionList
        {
            Items = RenderedGrobProperties.Select((o, i) => new CompletionItem
            {
                Label = $"{o.Grob}.{o.Property}",
                Kind = CompletionItemKind.Property,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = $"{o.Grob}.{o.Property} = ",
                Detail = o.Detail,
                SortText = i.ToString(),
                // Colour / true-false enumerate, so they retrigger; a numeric kind (none
                // today — force-hshift was the one) would not.
                Command = o.Kind is "color" or "bool"
                    ? new Command { Title = "Suggest value", CommandIdentifier = "editor.action.triggerSuggest" }
                    : null,
            }).ToArray()
        };
    }

    /// <summary>
    /// The value forms offered after <c>override Grob.property = </c>, keyed by the
    /// property at the cursor: named colours for <c>color</c>, <c>true</c>/<c>false</c>
    /// for <c>transparent</c>. A numeric property (<c>force-hshift</c>) has no enumerable
    /// value, so nothing is offered (the user types the number).
    /// </summary>
    internal static CompletionList GetOverrideValueCompletions(string text, int offset)
        => OverrideValueProperty(text, offset) switch
        {
            "color" => GetColorCompletions(),
            "transparent" => GetBooleanCompletions(),
            _ => new CompletionList { Items = System.Array.Empty<CompletionItem>() },
        };

    /// <summary>The named colours <see cref="LilySharp.Core.Rendering.ColorParser"/>
    /// understands (a hex <c>#RRGGBB</c> is also valid, but typed, not listed).</summary>
    internal static CompletionList GetColorCompletions()
    {
        var colors = new[] { "red", "green", "blue", "orange", "purple", "brown",
            "yellow", "cyan", "magenta", "gray", "black", "white" };
        return new CompletionList
        {
            Items = colors.Select((c, i) => new CompletionItem
            {
                Label = c,
                Kind = CompletionItemKind.Color,
                InsertText = c,
                Detail = "Named colour",
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>The two boolean values, for <c>transparent</c> (hide / show).</summary>
    internal static CompletionList GetBooleanCompletions()
    {
        var vals = new (string Label, string Detail)[]
        {
            ("true", "Hide the grob"),
            ("false", "Show the grob (default)"),
        };
        return new CompletionList
        {
            Items = vals.Select((v, i) => new CompletionItem
            {
                Label = v.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = v.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>
    /// When the cursor sits at the VALUE of a grob override on the current line
    /// (<c>override [once] Grob.property = |</c>, optionally with a partial value already
    /// typed), returns the property name (e.g. "color"); otherwise null. Line-scoped and
    /// gated to an <c>override</c> statement so a stray <c>x = </c> elsewhere is unaffected.
    /// </summary>
    internal static string? OverrideValueProperty(string text, int offset)
    {
        int lineStart = Math.Min(offset, text.Length);
        while (lineStart > 0 && text[lineStart - 1] != '\n') lineStart--;
        var line = text.Substring(lineStart, Math.Min(offset, text.Length) - lineStart);
        var m = OverrideValueRegex().Match(line);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// The grob properties offered right after <c>revert</c> — the SAME targets as
    /// <see cref="GetOverrideCompletions"/> but WITHOUT a value, since <c>revert</c> takes
    /// just <c>Grob.property</c> (it undoes a prior override, restoring the default).
    /// </summary>
    internal static CompletionList GetRevertCompletions()
    {
        return new CompletionList
        {
            Items = RenderedGrobProperties.Select((o, i) => new CompletionItem
            {
                Label = $"{o.Grob}.{o.Property}",
                Kind = CompletionItemKind.Property,
                InsertText = $"{o.Grob}.{o.Property}",
                Detail = $"Restore {o.Grob}.{o.Property} to its default",
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

    // What each clef IS. Prose only — WHICH of these may be offered is decided by the
    // compiler's own vocabularies (LanguageVocabulary), never by this table's membership,
    // because that is precisely what drifted: the table held the five a music block takes
    // and was offered in the part header too, where eleven are legal.
    // The order is high → low sounding pitch, which is the order a reader expects and is
    // not alphabetical; SortText below preserves it against the client's own sorting.
    private static readonly string[] ClefOrder =
    {
        "treble", "treble^8", "treble_8", "soprano", "mezzosoprano",
        "alto", "tenor", "baritone", "bass", "bass_8", "percussion",
    };

    private static readonly System.Collections.Generic.Dictionary<string, string> ClefDetails = new()
    {
        ["treble"] = "Treble (G) clef",
        ["treble^8"] = "Treble clef sounding an octave higher",
        ["treble_8"] = "Treble clef sounding an octave lower (guitar/tenor)",
        ["soprano"] = "Soprano (C) clef",
        ["mezzosoprano"] = "Mezzo-soprano (C) clef",
        ["alto"] = "Alto (C) clef",
        ["tenor"] = "Tenor (C) clef",
        ["baritone"] = "Baritone (C) clef",
        ["bass"] = "Bass (F) clef",
        ["bass_8"] = "Bass clef sounding an octave lower",
        ["percussion"] = "Percussion clef (unpitched staff)",
    };

    /// <summary>
    /// The clef names legal at the caret. ONE production standing in two positions: a part
    /// header takes eleven, a <c>clef</c> directive inside music (and <c>staff</c>/<c>ossia</c>
    /// in a score) takes five.
    /// </summary>
    /// <param name="inPartHeader">
    /// True for the wider position. ⚠️ This argument is the whole fix: until 2026-08-19 there
    /// was no argument, and the five-name list was offered in BOTH positions — so an editor
    /// that never once suggested an illegal clef still hid six legal ones from every part
    /// header in the language. Measured the same day: all eleven compile in a header, and the
    /// six outside <c>ClefNames</c> are refused in music with "Expected clef name".
    /// </param>
    internal static CompletionList GetClefCompletions(bool inPartHeader = false)
    {
        var legal = inPartHeader
            ? LanguageVocabulary.PartClefNames
            : LanguageVocabulary.ClefNames;

        // Ordered by ClefOrder, then anything the compiler grew that this file has not been
        // told how to describe — such a word is still OFFERED (the compiler accepts it), just
        // without prose. Dropping it is what turned a missing description into a missing clef.
        var ordered = legal.OrderBy(
            n => System.Array.IndexOf(ClefOrder, n) is var i && i >= 0 ? i : int.MaxValue);

        return new CompletionList
        {
            // SortText keeps the high→low order (VS Code otherwise sorts by label).
            Items = ordered.Select((name, i) => new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.EnumMember,
                Detail = ClefDetails.TryGetValue(name, out var d) ? d : null,
                SortText = i.ToString("D2"),
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

    internal static CompletionList GetTopLevelCompletions(string? text = null, int offset = 0)
    {
        var items = new System.Collections.Generic.List<CompletionItem>
        {
                new CompletionItem { Label = "part", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "part $1 {\n\t$0\n}", Detail = "Part declaration" },
                new CompletionItem { Label = "section", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "section $1 {\n\t$0\n}", Detail = "Section declaration" },
                new CompletionItem { Label = "phrase", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "phrase $1 {\n\t$0\n}", Detail = "Reusable phrase" },
                new CompletionItem { Label = "form", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "form main { $0 }", Detail = "Piece form (section play order)" },
                new CompletionItem { Label = "score", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "score main {\n\t$0\n}", Detail = "Printable score (visual layout)" },
                new CompletionItem { Label = "title", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "title \"$0\"", Detail = "Title metadata" },
                new CompletionItem { Label = "composer", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "composer \"$0\"", Detail = "Composer metadata" },
                // ⚠️ This inserted `font "$0"` until 2026-08-18 — the removed one-liner —
                // so completing the KEYWORD typed a diagnostic (LYS8007). It is the path a
                // writer actually takes, and it survived the removal because the removal
                // fixed the other three font contexts and not this one.
                // ⚠️ The body comes from FontBlockSnippet, the ONE home: written out here as
                // well, the two spellings drift and the keyword path is the one nobody looks
                // at — which is exactly how it came to be wrong in the first place.
                new CompletionItem { Label = "fonts", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "fonts " + FontBlockSnippet("$0"), Detail = "Text faces per role, pre-filled with the faces in use; add `embedded` to subset-embed them in the exported PDF" },
                // ⚠️ Pre-filled with the DEFAULTS (a4), the fonts snippet's rule: accepting
                // the completion and changing nothing does not move the page.
                new CompletionItem { Label = "paper", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "paper {\n\tpaperWidth ${1:210mm}\n\tpaperHeight ${2:297mm}$0\n}", Detail = "Page dimensions (paper size, margins, spacing), pre-filled with the a4 defaults" },
                new CompletionItem { Label = "tempo", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tempo $0", Detail = "Tempo (BPM)", Command = new Command { Title = "Suggest tempo", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "Time signature", Command = new Command { Title = "Suggest time signature", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "key $0", Detail = "Key signature", Command = new Command { Title = "Suggest key tonic", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "octave", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "octave $0", Detail = "Octave mode: absolute | relative (default)", Command = new Command { Title = "Suggest octave mode", CommandIdentifier = "editor.action.triggerSuggest" } },
                // `override` is a valid global default; `revert` / `once` are NOT offered at
                // the top level — they only work in a music stream (LYS1023 otherwise).
                // `partial` is likewise NOT offered here — a pickup belongs to a section, not
                // the piece (LYS1024); it appears in the section-level list instead.
                new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "override $0", Detail = "Override grob property (global default)", Command = new Command { Title = "Suggest grob property", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem
                {
                    Label = "template-twinkle",
                    FilterText = "template scoretemplate score twinkle new",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "// Twinkle, Twinkle, Little Star (public domain).\ntitle \"Twinkle, Twinkle, Little Star\"\ncomposer \"Jane Taylor\"\n\ntempo 100\ntime 4/4\nkey c major\n\npart melody {\n\tclef treble\n\tsection A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }\n\tsection B { g'4 g f f | e e d2 | }\n}\n\n// The track sings its melody; the score places its row under the staff.\nlyrics verse sings melody {\n\tsection A { Twin- kle twin- kle | lit- tle star | How I won- der | what you are | }\n\tsection B {\n\t\t[~1. Up a- bove the | world so high |]\n\t\t[~2. Like a dia- mond | in the sky |]\n\t}\n}\n\nform main { A |: B :| A \"A2\" }\n\nscore main {\n\tstaff melody\n\tlyrics verse\n}\n$0",
                    Detail = "Score template — single-staff + lyrics (Twinkle, Twinkle, Little Star)",
                },
                new CompletionItem
                {
                    Label = "template-twinkle-piano",
                    FilterText = "template scoretemplate score twinkle piano",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "// Twinkle, Twinkle, Little Star (public domain) — piano.\ntitle \"Twinkle, Twinkle, Little Star\"\ncomposer \"Jane Taylor\"\n\ntempo 100\ntime 4/4\nkey c major\n\npart rh { clef treble }\npart lh { clef bass }\n\nsection A {\n\trh { c4 c g' g | a a g2 | f4 f e e | d d c2 | }\n\tlh { c2 g | c2 c | f2 c | g2 c | }\n}\n\nform main { A }\n\nscore main {\n\tgrandStaff {\n\t\tstaff rh\n\t\tstaff lh\n\t}\n}\n$0",
                    Detail = "Score template — piano / grand staff (Twinkle, Twinkle, Little Star)",
                },
                new CompletionItem { Label = "lyrics", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "lyrics ${1:verse} sings ${2:part} {\n\t$0\n}", Detail = "Named lyrics track (sings its melody; a score places it with a `lyrics NAME` row)" },
        };

        // Drop the singleton globals (metadata + piece-wide defaults) already written at the
        // top level, so `title` / `composer` / `time` / `key` / … are not re-offered once the
        // file has them. `override` (many grobs), `part`, `section`, `score`, … stay.
        if (text != null)
            items.RemoveAll(it => GlobalSingletonKeywords.Contains(it.Label!)
                               && ExistsAtGlobalScope(text, it.Label!));

        // Offer the document's known section names — from the part cells and the form — as
        // section-major fill-ins, so a section can be pulled up to the top level. Sections
        // ALREADY declared at the top level are dropped by SectionScaffoldItems (its
        // `SectionsDeclaredInCurrentBlock` returns the depth-0 sections here), so writing
        // `section A {}` still leaves `section B` on offer. Top-level sections sit at column 0
        // (nest = ""); the new-section item is skipped (the top-level `section` keyword covers
        // a fresh name).
        if (text != null)
            items.AddRange(SectionScaffoldItems(text, offset, "Section", nest: "", includeNewSection: false));

        return new CompletionList { Items = items.ToArray() };
    }

    /// <summary>Top-level keywords that may appear only ONCE at the global scope — metadata
    /// (title/composer/font/paper) and the piece-wide defaults (time/key/tempo/octave).
    /// Completion drops them once present; duplicable keywords are NOT listed here.</summary>
    private static readonly System.Collections.Generic.HashSet<string> GlobalSingletonKeywords =
        new(StringComparer.Ordinal) { "title", "composer", "fonts", "paper", "tempo", "time", "key", "octave" };

    /// <summary>True when <paramref name="keyword"/> appears as a whole word at the GLOBAL
    /// scope (brace depth 0) in live code — not inside a block, a string, or a comment.</summary>
    private static bool ExistsAtGlobalScope(string text, string keyword)
    {
        var mask = CodeMask(text, text.Length);
        int depth = 0;
        for (int i = 0; i < text.Length;)
        {
            if (!mask[i]) { i++; continue; }            // inside a string / comment
            char c = text[i];
            if (c == '{') { depth++; i++; continue; }
            if (c == '}') { if (depth > 0) depth--; i++; continue; }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                if (depth == 0 && text[start..i] == keyword)
                    return true;
                continue;                                // i already past the word
            }
            i++;
        }
        return false;
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
        var matches = KeyDeclRegex().Matches(prefix);
        if (matches.Count == 0) return 0;
        var last = matches[matches.Count - 1];
        return LilySharp.Core.Music.KeySpelling.SharpsFor(
            last.Groups[1].Value, last.Groups[2].Value) ?? 0;
    }

    /// <summary>
    /// The annotation whose argument list the caret sits in — "chord" for
    /// <c>@chord(|)</c>, "notehead" for <c>@notehead(|)</c> — or null when the
    /// caret is not inside one. Scans back on the current line to the nearest
    /// unclosed '(' and reads the word in front of it.
    /// </summary>
    internal static string? AnnotationArgumentName(string text, int offset)
    {
        for (int i = Math.Min(offset, text.Length) - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c is ')' or '\n' or '\r')
                return null; // past a close paren, or off the line — not inside
            if (c != '(')
                continue;
            int e = i - 1, s = i - 1;
            while (s >= 0 && char.IsLetter(text[s])) s--;
            string name = s + 1 <= e ? text[(s + 1)..(e + 1)] : "";
            return name.Length > 0 && s >= 0 && text[s] == '@' ? name : null;
        }
        return null;
    }

    /// <summary>True when <paramref name="offset"/> sits inside a <c>@chord(…)</c>
    /// argument.</summary>
    internal static bool IsInsideChordAnnotation(string text, int offset) =>
        string.Equals(AnnotationArgumentName(text, offset), "chord", StringComparison.OrdinalIgnoreCase);

    /// <summary>The seven diatonic chords of the key in force at
    /// <paramref name="offset"/> (C major when none), each a chord-name symbol like
    /// C, Dm, Em, F, G, Am, Bdim — computed from the key's tonic + signature.</summary>
    internal static CompletionList GetDiatonicChordCompletions(string text, int offset)
    {
        var prefix = text.Substring(0, Math.Min(offset, text.Length));
        var matches = KeyDeclRegex().Matches(prefix);
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
        // The SYMBOL is both the label and the insert (GRAMMAR_AUDIT 8.1: the
        // entry is the printed form — "Dm7" — for @chord and chords{} alike).
        static CompletionItem Item(string symbol, string detail, int degree, int rank) => new()
        {
            Label = symbol,
            InsertText = symbol,
            Kind = CompletionItemKind.Value,
            Detail = detail,
            SortText = $"{degree:D2}{rank}",
        };

        var items = DiatonicChords.ForKey(tonic, sharps)
            .SelectMany(c => new[]
            {
                Item(c.Symbol, $"Diatonic triad ({c.Roman})", c.Degree, 0),
                Item(c.SeventhSymbol, "Diatonic 7th", c.Degree, 1),
                Item(c.SusFourthSymbol, "Suspended 4th", c.Degree, 2),
                Item(c.SusSecondSymbol, "Suspended 2nd", c.Degree, 3),
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
        int open = -1;
        foreach (Match h in DeclaredNameRegex().Matches(text))
            if (h.Groups[1].Value == "part" && h.Groups[2].Value == partName)
            { open = h.Index + h.Length - 1; break; } // index of the '{'
        if (open < 0) return false;
        var code = CodeMask(text, text.Length);
        int depth = 0, bodyEnd = text.Length;    // unclosed (mid-edit) → scan to end
        for (int i = open; i < text.Length; i++)
        {
            if (!code[i]) continue;
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) { bodyEnd = i; break; }
        }

        string body = text[(open + 1)..bodyEnd];
        return PercussionClefRegex().IsMatch(body);
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
                new CompletionItem { Label = "<< >>", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "<< $0 >>", Detail = "Arpeggio: sequential notes, octaves stacked above the first (like a chord). Add a duration after >> for an auto-tuplet.", SortText = "2arpeggio" },
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
                new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "override $0", Detail = "Override grob property", SortText = "4override", Command = new Command { Title = "Suggest grob property", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "revert", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "revert $0", Detail = "Revert grob property", SortText = "4revert", Command = new Command { Title = "Suggest grob property", CommandIdentifier = "editor.action.triggerSuggest" } },
                new CompletionItem { Label = "once override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "once override $0", Detail = "One-time override", SortText = "4once", Command = new Command { Title = "Suggest grob property", CommandIdentifier = "editor.action.triggerSuggest" } }
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
    /// <summary>
    /// True when the caret has already passed a placement dot — <c>@trill.</c>,
    /// <c>@trill.d</c>. There the annotation name is settled and only up/down can
    /// follow; before the dot (<c>@trill</c>) it is merely one possible
    /// continuation among the names still matching.
    /// </summary>
    private static bool AfterPlacementDot(string text, int offset)
    {
        int j = Math.Min(offset, text.Length) - 1;
        while (j >= 0 && char.IsLetter(text[j])) j--;
        return j >= 0 && text[j] == '.';
    }

    /// <summary>
    /// What to offer when the typed text happens to BE a complete annotation
    /// name: the placement qualifiers AND every name the text still matches.
    /// </summary>
    /// <remarks>
    /// Typing '@trill' used to replace the whole list with '.up'/'.down', so a
    /// search that read "tril → 4 names" turned into "trill → 2 placements" and
    /// then back into "trills → 2 names". A name being complete does not mean the
    /// user has finished typing it: 'trill' is also a prefix of nothing, but a
    /// substring of pralltriller, startTrillSpan and stopTrillSpan.
    ///
    /// The two kinds coexist because they edit different ranges: a placement item
    /// carries an explicit empty TextEdit range at the caret (it appends '.up'),
    /// while a name item has none and so replaces the typed word. That also keeps
    /// both visible in the editor's own filtering — an empty range matches any
    /// query.
    /// </remarks>
    internal static CompletionList PlacementAndStillMatchingNames(
        string text, int offset, Position? position, bool afterChord)
    {
        var placement = GetArticulationPlacementCompletions(text, offset, position);
        if (AfterPlacementDot(text, offset))
            return placement;

        var names = MatchAnywhere(
            GetArticulationCompletions(afterChord), PartialAnnotationName(text, offset));

        return new CompletionList
        {
            IsIncomplete = true,
            Items = [.. placement.Items, .. names.Items],
        };
    }

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
            // '!' sorts before every digit, so when these are merged with the
            // annotation names (groups "0".."8") the placement stays on top —
            // it is the more specific continuation of what was just typed.
            Items = new[] { Item("up", "!0up"), Item("down", "!1down") },
        };
    }

    /// <summary>
    /// True when the <c>@</c> being completed is attached to a chord or a
    /// <c>&lt;&lt; &gt;&gt;</c> arpeggio (the nearest char before it, past any
    /// duration/tremolo like <c>&gt;4</c> / <c>&gt;4:8</c>, is <c>&gt;</c>). A bare
    /// <c>@chord</c> there auto-derives the symbol, so it is offered WITHOUT the
    /// <c>(…)</c> the note form needs.
    /// </summary>
    internal static bool AtFollowsChord(string text, int offset)
        => ChordCloseBeforeAt(text, offset) >= 0;

    /// <summary>The offset of the closing <c>&gt;</c> of the chord/arpeggio the
    /// <c>@</c> at/behind <paramref name="offset"/> attaches to, or -1.</summary>
    private static int ChordCloseBeforeAt(string text, int offset)
    {
        int i = offset - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
        // Skip a partial annotation word already typed after '@' (e.g. '@cho').
        if (i >= 0 && text[i] != '@')
            while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '-')) i--;
        if (i < 0 || text[i] != '@') return -1;
        int j = i - 1;
        // The duration (and a :N tremolo) sit between the '>' and the '@'.
        while (j >= 0 && (char.IsWhiteSpace(text[j]) || char.IsDigit(text[j])
            || text[j] == '.' || text[j] == ':')) j--;
        return j >= 0 && text[j] == '>' ? j : -1;
    }

    /// <summary>
    /// Whether the chord/arpeggio before the <c>@</c> will auto-name under a bare
    /// <c>@chord</c>. The same stance as AnnotationNameValidator.CanNameChord: only
    /// pure named-pitch members are checked (key-independent); degrees or anything
    /// this textual scan can't read are assumed nameable (the collector's call).
    /// </summary>
    internal static bool GroupBeforeAtAutoNames(string text, int offset)
    {
        int j = ChordCloseBeforeAt(text, offset);
        if (j < 0) return true;
        // The group body: between this '>' (an arpeggio's '>>' starts one char
        // earlier) and its matching '<', bounded by the measure.
        int close = j > 0 && text[j - 1] == '>' ? j - 1 : j;
        int open = -1, depth = 0;
        for (int k = close - 1; k >= 0; k--)
        {
            char c = text[k];
            if (c == '|' || c == '{' || c == '}') break;
            if (c == '>') depth++;
            else if (c == '<')
            {
                if (depth > 0) depth--;
                else { open = k; break; }
            }
        }
        if (open < 0) return true;

        // Tokenize the members; nested chord brackets dissolve (their pitches
        // count like any member's).
        var body = text.Substring(open + 1, close - open - 1).Replace('<', ' ').Replace('>', ' ');
        int rootStep = -1, rootAlter = 0;
        var pcs = new System.Collections.Generic.List<int>();
        foreach (var token in body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var m = ChordMemberPitchRegex().Match(token);
            if (m.Success)
            {
                int step = LilySharp.Core.Semantics.RelativeOctave.StepIndex(m.Groups[1].Value[0]);
                int alter = m.Groups[2].Value switch
                { "is" => 1, "isis" => 2, "es" => -1, "eses" => -2, _ => 0 };
                if (rootStep < 0) { rootStep = step; rootAlter = alter; }
                pcs.Add(LilySharp.Core.Semantics.RelativeOctave.StepSemitoneOf(step) + alter);
                continue;
            }
            if (token is "r" or "s" or "R") continue;              // a rest is a gap
            return true; // a degree (key-dependent) or unreadable — don't second-guess
        }
        if (rootStep < 0) return true; // nothing to derive from
        return LilySharp.Core.Music.ChordStructure.TryRecognize(rootStep, rootAlter, pcs, out _);
    }

    /// <summary>
    /// Words a user is likely to TYPE that do not appear in the annotation's own
    /// name. Everything else is derived from the name itself (see
    /// <see cref="WithSearchTerms"/>) — this table is only for the cases where no
    /// slicing of the name can produce the word.
    /// </summary>
    private static readonly Dictionary<string, string> ExtraSearchTerms = new(StringComparer.Ordinal)
    {
        // The pedals are named after the EVENT (LilyPond's names); a user reaches
        // for the printed marking or the instrument's word for it.
        ["sustainOn"] = "pedal ped",
        ["sustainOff"] = "pedal ped",
        ["sostenutoOn"] = "pedal sost",
        ["sostenutoOff"] = "pedal sost",
        ["unaCorda"] = "pedal soft",
        ["treCorde"] = "pedal soft release",
        // All-lowercase names cannot be split into words, so the part a user is
        // most likely to type has to be listed.
        ["shortfermata"] = "fermata short",
        ["longfermata"] = "fermata long",
        ["invertedturn"] = "turn inverted",
        ["pralltriller"] = "trill prall",
        ["staccatissimo"] = "staccato wedge",
        ["upbow"] = "bow up",
        ["downbow"] = "bow down",
        ["flageolet"] = "harmonic circle",
        ["harmonic"] = "flageolet circle",
        ["notehead"] = "head shape",
        ["fig"] = "figured bass continuo",
        ["snapPizz"] = "bartok pizzicato",
        ["dead"] = "mute muted",
        ["laissezVibrer"] = "lv tie",
        ["repeatTie"] = "tie",
        ["glissando"] = "gliss slide",
        ["mark"] = "rehearsal",
        ["text"] = "dolce expressive",
        ["ottava"] = "8va octave",
        ["quindicesima"] = "15ma octave",
        ["loco"] = "octave end",
    };

    /// <summary>
    /// The annotation name typed so far after the '@' — "ill" for
    /// <c>c4@ill|</c>, "" right after the trigger. Letters and digits only, so a
    /// preceding note or duration is not swept in.
    /// </summary>
    internal static string PartialAnnotationName(string text, int offset)
    {
        int end = Math.Min(offset, text.Length);
        int start = end;
        while (start > 0 && char.IsLetterOrDigit(text[start - 1]))
            start--;
        return start > 0 && text[start - 1] == '@' ? text[start..end] : "";
    }

    /// <summary>
    /// Incremental search over the annotation list: an item survives if the typed
    /// text appears ANYWHERE in its name or search terms, so "ill" finds
    /// startTrillSpan and "corda" finds unaCorda.
    /// </summary>
    /// <remarks>
    /// The editor cannot do this. Its suggest widget matches at word starts, and
    /// there is no "match anywhere" switch, so a mid-word query would drop the
    /// item however the server labelled it. Hence the server filters, and each
    /// surviving item carries FilterText = the query itself so the client's own
    /// matcher keeps everything returned; the list is marked incomplete so the
    /// editor asks again on the next keystroke instead of re-filtering a cached
    /// list. SortText still decides the order.
    ///
    /// The cost is the widget's matched-character highlight: with FilterText set
    /// to the query it underlines the label's first characters rather than the
    /// part that actually matched.
    /// </remarks>
    internal static CompletionList MatchAnywhere(CompletionList list, string query)
    {
        // Incomplete even with nothing typed yet: otherwise the editor caches this
        // list and filters it ITSELF as the next characters arrive, which is
        // exactly the word-start matching being replaced here — '@' then "ill"
        // would silently drop everything.
        if (string.IsNullOrEmpty(query))
            return new CompletionList { IsIncomplete = true, Items = list.Items };

        var kept = new List<CompletionItem>();
        foreach (var item in list.Items)
        {
            var haystack = string.IsNullOrEmpty(item.FilterText) ? item.Label : item.FilterText;
            if (haystack.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                item.FilterText = query;
                kept.Add(item);
            }
        }

        return new CompletionList { IsIncomplete = true, Items = [.. kept] };
    }

    /// <summary>
    /// Widens what <see cref="MatchAnywhere"/> searches: the label plus the words
    /// from <see cref="ExtraSearchTerms"/>. Nothing is derived from the label
    /// itself — a substring search already reaches every part of it ("ill" finds
    /// startTrillSpan), so only words that are NOT in the name need listing.
    /// Applied to the whole list rather than to chosen items: no annotation is a
    /// special case.
    /// </summary>
    private static CompletionList WithSearchTerms(CompletionList list)
    {
        foreach (var item in list.Items)
        {
            if (ExtraSearchTerms.TryGetValue(item.Label, out var extra))
                item.FilterText = item.Label + " " + extra;
        }
        return list;
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

        return WithSearchTerms(new CompletionList
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
                new CompletionItem { Label = "shortfermata", Kind = CompletionItemKind.Value, Detail = "Short fermata (angular)", SortText = "0shortfermata" },
                new CompletionItem { Label = "longfermata", Kind = CompletionItemKind.Value, Detail = "Long fermata (square)", SortText = "0longfermata" },
                new CompletionItem { Label = "breath", Kind = CompletionItemKind.Value, Detail = "Breath mark after the note", SortText = "0breath" },
                new CompletionItem { Label = "caesura", Kind = CompletionItemKind.Value, Detail = "Caesura (railroad tracks) after the note", SortText = "0caesura" },
                new CompletionItem { Label = "stopped", Kind = CompletionItemKind.Value, Detail = "Stopped note + (brass hand-stop / left-hand pizz.)", SortText = "0stopped" },
                new CompletionItem { Label = "thumb", Kind = CompletionItemKind.Value, Detail = "Thumb position (cello)", SortText = "0thumb" },
                new CompletionItem { Label = "heel", Kind = CompletionItemKind.Value, Detail = "Organ pedal: heel", SortText = "0heel" },
                new CompletionItem { Label = "toe", Kind = CompletionItemKind.Value, Detail = "Organ pedal: toe", SortText = "0toe" },
                new CompletionItem { Label = "scoop", Kind = CompletionItemKind.Value, Detail = "Scoop (jazz articulation into the note)", SortText = "0scoop" },
                new CompletionItem { Label = "plop", Kind = CompletionItemKind.Value, Detail = "Plop (jazz articulation into the note)", SortText = "0plop" },
                new CompletionItem { Label = "fall", Kind = CompletionItemKind.Value, Detail = "Fall (jazz articulation off the note)", SortText = "0fall" },
                new CompletionItem { Label = "doit", Kind = CompletionItemKind.Value, Detail = "Doit (jazz articulation off the note)", SortText = "0doit" },

                // Fretted-instrument techniques. Each has a short spelling that
                // reads better mid-passage, so both are offered.
                new CompletionItem { Label = "hammerOn", Kind = CompletionItemKind.Value, Detail = "Hammer-on (a.k.a. @ho)", SortText = "0hammerOn" },
                new CompletionItem { Label = "ho", Kind = CompletionItemKind.Value, Detail = "Hammer-on, short spelling of @hammerOn", SortText = "0ho" },
                new CompletionItem { Label = "pullOff", Kind = CompletionItemKind.Value, Detail = "Pull-off (a.k.a. @po)", SortText = "0pullOff" },
                new CompletionItem { Label = "po", Kind = CompletionItemKind.Value, Detail = "Pull-off, short spelling of @pullOff", SortText = "0po" },
                new CompletionItem { Label = "tap", Kind = CompletionItemKind.Value, Detail = "Tapped note", SortText = "0tap" },
                new CompletionItem { Label = "snapPizz", Kind = CompletionItemKind.Value, Detail = "Snap (Bartók) pizzicato", SortText = "0snapPizz" },
                new CompletionItem { Label = "slide", Kind = CompletionItemKind.Value, Detail = "Slide to the next note", SortText = "0slide" },
                new CompletionItem { Label = "dead", Kind = CompletionItemKind.Value, Detail = "Dead (muted) note — × notehead", SortText = "0dead" },

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
                new CompletionItem { Label = "pralltriller", Kind = CompletionItemKind.Value, Detail = "Prall-triller (trill with prall)", SortText = "1pralltriller" },

                // Dynamics (@ prefix style)
                new CompletionItem { Label = "p", Kind = CompletionItemKind.Value, Detail = "Piano (soft)", SortText = "2p" },
                new CompletionItem { Label = "f", Kind = CompletionItemKind.Value, Detail = "Forte (loud)", SortText = "2f" },
                new CompletionItem { Label = "pp", Kind = CompletionItemKind.Value, Detail = "Pianissimo", SortText = "2pp" },
                new CompletionItem { Label = "ff", Kind = CompletionItemKind.Value, Detail = "Fortissimo", SortText = "2ff" },
                new CompletionItem { Label = "mp", Kind = CompletionItemKind.Value, Detail = "Mezzo-piano", SortText = "2mp" },
                new CompletionItem { Label = "mf", Kind = CompletionItemKind.Value, Detail = "Mezzo-forte", SortText = "2mf" },
                new CompletionItem { Label = "ppp", Kind = CompletionItemKind.Value, Detail = "Pianississimo", SortText = "2ppp" },
                new CompletionItem { Label = "pppp", Kind = CompletionItemKind.Value, Detail = "Pianissississimo", SortText = "2pppp" },
                new CompletionItem { Label = "ppppp", Kind = CompletionItemKind.Value, Detail = "Five-p pianissimo", SortText = "2ppppp" },
                new CompletionItem { Label = "fff", Kind = CompletionItemKind.Value, Detail = "Fortississimo", SortText = "2fff" },
                new CompletionItem { Label = "ffff", Kind = CompletionItemKind.Value, Detail = "Fortissississimo", SortText = "2ffff" },
                new CompletionItem { Label = "fffff", Kind = CompletionItemKind.Value, Detail = "Five-f fortissimo", SortText = "2fffff" },
                new CompletionItem { Label = "sfz", Kind = CompletionItemKind.Value, Detail = "Sforzato accent dynamic", SortText = "2sfz" },
                new CompletionItem { Label = "sf", Kind = CompletionItemKind.Value, Detail = "Sforzando accent dynamic", SortText = "2sf" },
                new CompletionItem { Label = "sffz", Kind = CompletionItemKind.Value, Detail = "Heaviest sforzato accent dynamic", SortText = "2sffz" },
                new CompletionItem { Label = "fz", Kind = CompletionItemKind.Value, Detail = "Forzando accent dynamic", SortText = "2fz" },
                new CompletionItem { Label = "rf", Kind = CompletionItemKind.Value, Detail = "Rinforzando accent dynamic", SortText = "2rf" },
                new CompletionItem { Label = "rfz", Kind = CompletionItemKind.Value, Detail = "Rinforzando accent dynamic (rfz)", SortText = "2rfz" },
                new CompletionItem { Label = "fp", Kind = CompletionItemKind.Value, Detail = "Forte-piano accent dynamic", SortText = "2fp" },
                new CompletionItem { Label = "cresc", Kind = CompletionItemKind.Value, Detail = "Crescendo hairpin", SortText = "2cresc" },
                new CompletionItem { Label = "decresc", Kind = CompletionItemKind.Value, Detail = "Decrescendo hairpin", SortText = "2decresc" },
                new CompletionItem { Label = "dim", Kind = CompletionItemKind.Value, Detail = "Diminuendo", SortText = "2dim" },

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
                new CompletionItem { Label = "ottava", Kind = CompletionItemKind.Value, Detail = "Ottava bracket up (8va)", SortText = "4ottava" },
                new CompletionItem { Label = "ottava(bassa)", Kind = CompletionItemKind.Value, Detail = "Ottava bracket down (8vb)", SortText = "4ottava.bassa" },
                new CompletionItem { Label = "quindicesima", Kind = CompletionItemKind.Value, Detail = "Quindicesima bracket up (15ma)", SortText = "4quindicesima" },
                new CompletionItem { Label = "quindicesima(bassa)", Kind = CompletionItemKind.Value, Detail = "Quindicesima bracket down (15mb)", SortText = "4quindicesima.bassa" },
                new CompletionItem { Label = "loco", Kind = CompletionItemKind.Value, Detail = "End octave bracket", SortText = "4loco" },
                // One word per end, as in LilyPond. (The '@trillSpan(start)'
                // spelling was a second way to say the same thing; it is gone.)
                new CompletionItem { Label = "startTrillSpan", Kind = CompletionItemKind.Value, Detail = "Start trill spanner", SortText = "4startTrillSpan" },
                new CompletionItem { Label = "stopTrillSpan", Kind = CompletionItemKind.Value, Detail = "Stop trill spanner", SortText = "4stopTrillSpan" },
                ArgumentStub("feather", "Feathered beam — offers right (accel.), left (rit.)", "4feather"),

                // Pedal markings
                // LilyPond's own names (ly/spanners-init.ly). One word each: the
                // pedal event carries only START/STOP, so there is no argument.
                new CompletionItem { Label = "sustainOn", Kind = CompletionItemKind.Value, Detail = "Sustain pedal down (Ped.)", SortText = "5sustainOn" },
                new CompletionItem { Label = "sustainOff", Kind = CompletionItemKind.Value, Detail = "Sustain pedal up (*)", SortText = "5sustainOff" },
                new CompletionItem { Label = "sostenutoOn", Kind = CompletionItemKind.Value, Detail = "Sostenuto pedal down (Sost. Ped.)", SortText = "5sostenutoOn" },
                new CompletionItem { Label = "sostenutoOff", Kind = CompletionItemKind.Value, Detail = "Sostenuto pedal up", SortText = "5sostenutoOff" },
                new CompletionItem { Label = "unaCorda", Kind = CompletionItemKind.Value, Detail = "Una corda (soft pedal down)", SortText = "5unaCorda" },
                new CompletionItem { Label = "treCorde", Kind = CompletionItemKind.Value, Detail = "Tre corde — the una corda release", SortText = "5treCorde" },

                // Notation marks
                new CompletionItem { Label = "glissando", Kind = CompletionItemKind.Value, Detail = "Glissando to next note", SortText = "6glissando" },
                new CompletionItem { Label = "arpeggio", Kind = CompletionItemKind.Value, Detail = "Arpeggiate chord", SortText = "6arpeggio" },
                new CompletionItem { Label = "courtesy", Kind = CompletionItemKind.Value, Detail = "Force courtesy accidental", SortText = "6courtesy" },
                new CompletionItem { Label = "editorial", Kind = CompletionItemKind.Value, Detail = "Editorial (suggestion) accidental above the note", SortText = "6editorial" },
                new CompletionItem { Label = "cross", Kind = CompletionItemKind.Value, Detail = "Cross-staff note (moves to the other staff of the pair)", SortText = "6cross" },
                new CompletionItem { Label = "laissezVibrer", Kind = CompletionItemKind.Value, Detail = "Laissez vibrer tie (hanging, no destination)", SortText = "6laissezVibrer" },
                new CompletionItem { Label = "repeatTie", Kind = CompletionItemKind.Value, Detail = "Repeat tie (hanging tie into a repeat)", SortText = "6repeatTie" },
                new CompletionItem { Label = "rest", Kind = CompletionItemKind.Value, Detail = "Print this note as a rest at its own pitch (a4@rest)", SortText = "6rest" },
                new CompletionItem { Label = "stemUp", Kind = CompletionItemKind.Value, Detail = "Force the stem up", SortText = "6stemUp" },
                new CompletionItem { Label = "stemDown", Kind = CompletionItemKind.Value, Detail = "Force the stem down", SortText = "6stemDown" },

                ArgumentStub("notehead", "Notehead shape — offers x, cross, diamond, triangle, slash, xcircle", "6notehead"),

                ArgumentStub("finger", "Left-hand fingering — offers 0-5", "6finger"),
                ArgumentStub("pluck", "Right-hand (plucking) finger — offers p, i, m, a", "6pluck"),
                ArgumentStub("bend", "String bend — offers half, full", "6bend"),

                // Guitar chord frame: one character per string, low to high —
                // x = muted, o = open, digit = fret.
                new CompletionItem { Label = "frame(x32010)", Kind = CompletionItemKind.Value, Detail = "Chord frame (x = muted, o = open, digit = fret)", SortText = "6frame" },

                // Figured bass — parenthesised, figures space-separated: @fig(6 4).
                ArgumentStub("fig", "Figured bass — offers 6, 6 4, 7, 6 5, 4 3, … (space-separated)", "7fig"),

                // Chord name — on a note the '(…)' form (offers the key's diatonic
                // chords); on a chord the bare auto-derive form. Built above.
                chordItem
            ]
        });
    }

    /// <summary>
    /// An '@' entry whose argument comes from a second list: it inserts
    /// <c>name()</c> with the caret between the parens and asks the editor to
    /// suggest again, so a family's members (six notehead shapes, six
    /// fingerings, …) never crowd the annotation list itself.
    /// </summary>
    private static CompletionItem ArgumentStub(string name, string detail, string sortText) => new()
    {
        Label = name,
        Kind = CompletionItemKind.Value,
        Detail = detail,
        InsertText = $"{name}($0)",
        InsertTextFormat = InsertTextFormat.Snippet,
        SortText = sortText,
        Command = new Command
        {
            Title = $"Suggest {name} arguments",
            CommandIdentifier = "editor.action.triggerSuggest",
        },
    };

    /// <summary>
    /// The argument vocabulary of an <c>@name(…)</c> annotation, or null when the
    /// annotation takes free-form text (<c>@text</c>, <c>@mark</c>, <c>@frame</c>)
    /// or has its own key-dependent list (<c>@chord</c>, handled separately).
    /// This is the second half of the two-step completion: the '@' list offers the
    /// bare name, and the argument is picked from here.
    /// </summary>
    internal static CompletionList? GetAnnotationArgumentCompletions(string annotation) =>
        annotation.ToLowerInvariant() switch
        {
            "notehead" => GetNoteheadCompletions(),
            "finger" => GetFingerCompletions(),
            "pluck" => GetPluckCompletions(),
            "bend" => GetBendCompletions(),
            "feather" => GetFeatherCompletions(),
            "fig" => GetFiguredBassCompletions(),
            _ => null
        };

    /// <summary>One argument item; the list order is the order given.</summary>
    private static CompletionItem Argument(string label, string detail, int rank) => new()
    {
        Label = label,
        Kind = CompletionItemKind.Value,
        Detail = detail,
        SortText = $"{rank}{label}",
    };

    /// <summary>
    /// The notehead shapes, offered inside <c>@notehead(…)</c>. Sorted with the
    /// two percussion/rhythm shapes first, since those are what a user reaches
    /// for most; the rest follow in the order the collector documents them.
    /// </summary>
    internal static CompletionList GetNoteheadCompletions() => new()
    {
        Items =
        [
            Argument("x", "× notehead (dead/muted, percussion)", 0),
            Argument("cross", "Cross notehead", 1),
            Argument("diamond", "Diamond notehead ◇ (harmonic)", 2),
            Argument("triangle", "Triangle notehead", 3),
            Argument("slash", "Slash notehead (rhythm notation)", 4),
            Argument("xcircle", "Circled-× notehead", 5),
        ]
    };

    /// <summary>
    /// Left-hand fingering, inside <c>@finger(…)</c>. Any non-negative number
    /// parses; 0-5 (open string / thumb through little finger) is the range a
    /// score actually uses, so it is what the list offers.
    /// </summary>
    internal static CompletionList GetFingerCompletions() => new()
    {
        Items =
        [
            Argument("0", "Open string (or no finger)", 0),
            Argument("1", "Index finger (piano: thumb)", 1),
            Argument("2", "Middle finger", 2),
            Argument("3", "Ring finger", 3),
            Argument("4", "Little finger", 4),
            Argument("5", "Fifth finger (piano)", 5),
        ]
    };

    /// <summary>
    /// Right-hand (plucking) fingering, inside <c>@pluck(…)</c> — the Spanish
    /// guitar names.
    /// </summary>
    internal static CompletionList GetPluckCompletions() => new()
    {
        Items =
        [
            Argument("p", "Thumb (pulgar)", 0),
            Argument("i", "Index (índice)", 1),
            Argument("m", "Middle (medio)", 2),
            Argument("a", "Ring (anular)", 3),
        ]
    };

    /// <summary>String bend amounts, inside <c>@bend(…)</c>.</summary>
    internal static CompletionList GetBendCompletions() => new()
    {
        Items =
        [
            Argument("half", "Bend up a semitone", 0),
            Argument("full", "Bend up a whole tone", 1),
        ]
    };

    /// <summary>
    /// Figured bass, inside <c>@fig(…)</c>. The figures are space-separated and
    /// stack top to bottom, so the vocabulary is not a fixed set — what is
    /// offered is the continuo shorthand a score actually uses, most frequent
    /// first, plus the two non-numeric atoms (bare accidental, held line).
    /// Alterations are written after their figure: 6 s = 6♯, 4 f = 4♭, 7 n = 7♮.
    /// </summary>
    internal static CompletionList GetFiguredBassCompletions() => new()
    {
        Items =
        [
            Argument("6", "First inversion (6/3)", 0),
            Argument("6 4", "Second inversion", 1),
            Argument("7", "Seventh chord", 2),
            Argument("6 5", "Seventh, first inversion", 3),
            Argument("4 3", "Seventh, second inversion", 4),
            Argument("4 2", "Seventh, third inversion", 5),
            Argument("5 3", "Root position, written out", 6),
            Argument("9", "Ninth (9-8 suspension)", 7),
            Argument("4", "Fourth (4-3 suspension)", 8),
            Argument("2", "Second", 9),
            Argument("6 s", "6♯ — an alteration follows its figure (s/f/n)", 10),
            Argument("#", "Bare sharp — raises the third above the bass", 11),
            Argument("_", "Held figure — continuation line from the previous bass note", 12),
        ]
    };

    /// <summary>
    /// Feathered-beam directions, inside <c>@feather(…)</c>. The beam opens
    /// toward the side named, so right = getting faster.
    /// </summary>
    internal static CompletionList GetFeatherCompletions() => new()
    {
        Items =
        [
            Argument("right", "Opening right — accelerando", 0),
            Argument("left", "Opening left — ritardando", 1),
        ]
    };

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
