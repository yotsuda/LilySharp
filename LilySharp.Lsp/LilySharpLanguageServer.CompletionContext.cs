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
    // ========== Completion context scanning ==========

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
        => InnermostOpenBlock(new BlockContextScan(text, offset).Stack);

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>fonts { … }</c> block.
    /// </summary>
    /// <remarks>
    /// The block is UNNAMED, so its introducing keyword is the frame's
    /// <see cref="BlockFrame.Name"/> — the word immediately before the <c>{</c>. A font
    /// block never nests, so the innermost frame is the whole test.
    /// </remarks>
    internal static bool IsInsideFontBlock(string text, int offset)
        // Unnamed: the word before `{` is `fonts` (the frame's Name). NAMED —
        // `fonts house {` — the word before `{` is the NAME and `fonts` is one
        // word further back (the frame's Prefix), the same shape as a part block.
        => IsInsideFontBlock(new BlockContextScan(text, offset).Stack);

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>paper { … }</c> block;
    /// <paramref name="inSpacingBlock"/> is true when it sits one level deeper, in a
    /// nested spacing block (<c>systemSystemSpacing { | }</c>) — the innermost frame is
    /// then the spacing key and <c>paper</c> is the frame outside it.
    /// </summary>
    internal static bool IsInsidePaperBlock(string text, int offset, out bool inSpacingBlock)
        // A frame is a paper block when `paper` is the word before its `{` (unnamed)
        // or one word further back (named — `paper wide {`, the part-block shape).
        => IsInsidePaperBlock(new BlockContextScan(text, offset).Stack, out inSpacingBlock);

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>part &lt;name&gt; { … }</c>
    /// body. <see cref="InnermostOpenBlock"/> cannot answer this: the word before a
    /// part's <c>{</c> is the part NAME, so the introducing <c>part</c> keyword is one
    /// word further back — which is also what tells a declaration (<c>part rh {</c>)
    /// apart from a section-body part reference (<c>rh {</c>).
    /// </summary>
    internal static bool IsInsidePartBlock(string text, int offset)
        => IsInsidePartBlock(new BlockContextScan(text, offset).Stack);

    /// <summary>
    /// True when <paramref name="offset"/> sits directly inside a container that holds
    /// part-major inner sections — a <c>part</c> or <c>lyrics</c> block. (A chords track
    /// has the same shape, but its body completes the chord vocabulary, intercepted
    /// earlier.) Used to offer the document's not-yet-used section names after
    /// <c>section</c>.
    /// </summary>
    internal static bool IsInsideSectionContainer(string text, int offset)
        // The introducing keyword is the Prefix for a NAMED block (`lyrics words {`) but
        // the Name for an UNNAMED one (`lyrics {`, whose name is optional): FrameKeyword
        // picks whichever holds the keyword, as IsInsideChordsBlock does.
        => IsInsideSectionContainer(new BlockContextScan(text, offset).Stack);

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
        => IsInsideTopLevelLyricsBlock(new BlockContextScan(text, offset).Stack);

    /// <summary>
    /// True when <paramref name="offset"/> sits DIRECTLY inside a top-level
    /// <c>section [name] { }</c> — the innermost (and only) open block is that section. A
    /// section nested in a <c>part</c> is a part-major cell (its body is that part's music,
    /// not part blocks), so it is excluded.
    /// </summary>
    internal static bool IsInsideTopLevelSectionBody(string text, int offset)
        => IsInsideTopLevelSectionBody(new BlockContextScan(text, offset).Stack);

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

    /// <summary>One still-open block for <see cref="BlockContextScan"/>: the two-word
    /// <see cref="BlockFrame"/> AND the score-opener judgement, both read at the same
    /// <c>{</c> so one pass serves every consumer.</summary>
    private readonly record struct OpenBlock(BlockFrame Frame, bool IsScoreOpener);

    /// <summary>
    /// The one block-context scan of a completion request. <see cref="GetCompletionContext"/>
    /// used to call the <c>IsInside*</c> helpers independently, and each ran its own
    /// <see cref="ScanOpenBlocks"/> pass over <c>[0, offset)</c> — 10-15 full-document
    /// scans per keystroke on the completion hot path (completion triggers on the space
    /// that ends every note, so this IS a keystroke cost; 2026-08-26 review, finding 2-2).
    /// This scans ONCE, lazily — the early returns (an <c>@</c> or <c>\</c> trigger, a
    /// value keyword) still pay nothing — and every context predicate reads the shared
    /// stack. <see cref="RawBraceDepth"/> preserves the final fallthrough's exact
    /// arithmetic: a depth that went negative on a stray <c>}</c> is NOT the same as an
    /// empty stack, and the fallthrough's answer must not change shape on such input.
    /// </summary>
    private sealed class BlockContextScan
    {
        private readonly string _text;
        private readonly int _offset;
        private List<OpenBlock>? _stack;
        private int _rawBraceDepth;

        public BlockContextScan(string text, int offset)
        {
            _text = text;
            _offset = offset;
        }

        public List<OpenBlock> Stack
        {
            get { EnsureScanned(); return _stack!; }
        }

        public int RawBraceDepth
        {
            get { EnsureScanned(); return _rawBraceDepth; }
        }

        private void EnsureScanned()
        {
            if (_stack != null)
                return;
            var stack = new List<OpenBlock>();
            int depth = 0;
            bool inStr = false, inLine = false, inBlock = false;
            int limit = Math.Min(_offset, _text.Length);
            for (int i = 0; i < limit; i++)
            {
                if (!IsCodeChar(_text, i, ref inStr, ref inLine, ref inBlock)) continue;
                if (_text[i] == '{')
                {
                    depth++;
                    stack.Add(new OpenBlock(ReadFrame(_text, i), IsScoreBlockOpener(_text, i)));
                }
                else if (_text[i] == '}')
                {
                    depth--;
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                }
            }
            _stack = stack;
            _rawBraceDepth = depth;
        }
    }

    private static string? InnermostOpenBlock(List<OpenBlock> stack)
        => stack.Count > 0 ? stack[^1].Frame.Name : null;

    private static bool IsInsideFontBlock(List<OpenBlock> stack)
        => stack.Count > 0
            && (stack[^1].Frame.Name == "fonts" || stack[^1].Frame.Prefix == "fonts");

    private static bool IsInsidePaperBlock(List<OpenBlock> stack, out bool inSpacingBlock)
    {
        static bool IsPaperFrame(BlockFrame f) => f.Name == "paper" || f.Prefix == "paper";
        inSpacingBlock = false;
        if (stack.Count == 0)
            return false;
        if (IsPaperFrame(stack[^1].Frame))
            return true;
        if (stack.Count >= 2 && IsPaperFrame(stack[^2].Frame))
        {
            inSpacingBlock = true;
            return true;
        }
        return false;
    }

    private static bool IsInsidePartBlock(List<OpenBlock> stack)
        => stack.Count > 0 && stack[^1].Frame.Prefix == "part";

    private static bool IsInsideSectionContainer(List<OpenBlock> stack)
        => stack.Count > 0 && FrameKeyword(stack[^1].Frame) is "part" or "lyrics";

    private static bool IsInsideTopLevelLyricsBlock(List<OpenBlock> stack)
        => stack.Count == 1 && FrameKeyword(stack[0].Frame) == "lyrics";

    private static bool IsInsideTopLevelSectionBody(List<OpenBlock> stack)
        => stack.Count == 1 && FrameKeyword(stack[0].Frame) == "section";

    private static bool IsInsideScoreBlock(List<OpenBlock> stack)
        => stack.Count > 0 && stack[^1].IsScoreOpener;

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
        AfterPaperSizeName,
        AfterPaperSizeNameQuoted,
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

        // The ONE block-context scan of this request (lazy — see BlockContextScan).
        // Every IsInside* judgement below reads this shared stack instead of
        // re-scanning [0, offset) on its own.
        var scan = new BlockContextScan(text, offset);

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
        if (prevWord == "octave" && !IsInsidePartBlock(scan.Stack))
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
                    return IsInsideScoreBlock(scan.Stack)
                        ? CompletionContext.AfterFontsBlockRef
                        : CompletionContext.AfterFontKeyword;
                // `paper |` with no block yet: same motion.
                case "paper":
                    return IsInsideScoreBlock(scan.Stack)
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
        if (IsInsideFontBlock(scan.Stack))
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

        // Inside `paper { … }` a KEY is what belongs (a length value is a number, which
        // no list serves); inside a nested spacing block, its four sub-keys; inside the
        // one quoted value — `size "…"` — the paper-size names. Intercepted before the
        // fallthroughs for the reason the fonts block is: without this the popup offers
        // pitches and articulations at every caret inside the block.
        if (IsInsidePaperBlock(scan.Stack, out bool inSpacingBlock))
        {
            // `size |` — the size names, bare (the canonical spelling); and inside
            // `size "…"` — the same names, for the quoted escape that carries a space.
            if (IsInsideStringLiteral(text, offset))
            {
                if (KeywordBeforeCurrentString(text, offset) == "size")
                    return CompletionContext.AfterPaperSizeNameQuoted;
            }
            else if (prevWord == "size")
            {
                return CompletionContext.AfterPaperSizeName;
            }
            return inSpacingBlock ? CompletionContext.PaperSpecBlock : CompletionContext.PaperBlock;
        }

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
            && IsInsidePartBlock(scan.Stack)
            && !IsInsideStringLiteral(text, offset))
            return CompletionContext.AfterInstrument;

        // Right after the `removeEmpty` part property only its values are valid
        // (true / all / false — LP RemoveEmptyStaves / RemoveAllEmptyStaves).
        if (string.Equals(prevWord, "removeEmpty", StringComparison.OrdinalIgnoreCase)
            && IsInsidePartBlock(scan.Stack)
            && !IsInsideStringLiteral(text, offset))
            return CompletionContext.AfterRemoveEmpty;

        // Right after `section `: offer the section names known to the piece but not yet
        // declared in this scope, not the property list. Two scopes carry a fill-in:
        // inside a part { } / lyrics { } container (part-major inner section) and at the
        // top level (section-major declaration, filled from the form's references).
        // `section` inside a music body is not a declaration site, so it is skipped.
        if (prevWord == "section"
            && !IsInsideStringLiteral(text, offset)
            && (IsInsideSectionContainer(scan.Stack) || InnermostOpenBlock(scan.Stack) == null))
            return CompletionContext.AfterSection;

        // Directly inside a part { } header (and not after one of the
        // value-taking keywords above): offer the part property names — a part
        // body holds properties (and inner sections), never notes.
        if (IsInsidePartBlock(scan.Stack) && !IsInsideStringLiteral(text, offset))
            return CompletionContext.PartBlock;

        // Inside form <name> { … } the body is a playback order (section names and
        // navigation marks), not music — so it gets its own completions, never note
        // names. A form is always named, so the `form` keyword is the frame Prefix.
        if (scan.Stack.Count > 0 && scan.Stack[^1].Frame.Prefix == "form")
            return CompletionContext.FormBlock;

        // Inside score "name" { } / grandStaff { }: the body is a render spec.
        // After its reference keywords only the declared part names fit.
        if (IsInsideScoreBlock(scan.Stack))
        {
            // `… as |` → a display selector, but which one depends on what the `as`
            // governs: `tab … as` takes numbers|full, `chords … as` takes
            // roman|names.
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
            if (scan.Stack.Count > 0)
            {
                string block = scan.Stack[^1].Frame.Name;
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
            // `as roman|names` display selector (plus the normal
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
        if (IsInsideTopLevelLyricsBlock(scan.Stack) && !IsInsideStringLiteral(text, offset))
            return CompletionContext.LyricsBlock;

        // Directly inside a top-level `section { }` in a doc WITH parts: the body holds PART
        // BLOCKS (`melody { … }`), not notes — so offer the declared parts as cell scaffolds,
        // not the pitch letters. (A section in a NO-parts doc is a single voice and keeps its
        // note completions; a section nested in a part is a part-major cell, likewise music.)
        if (IsInsideTopLevelSectionBody(scan.Stack) && HasDeclaredParts(text)
            && !IsInsideStringLiteral(text, offset))
            return CompletionContext.SectionBlock;

        if (i >= 0 && text[i] == '{')
            return CompletionContext.MusicBlock;

        // Check if inside braces (ignoring braces in strings/comments). RawBraceDepth
        // keeps this exact arithmetic — a depth driven negative by a stray `}` is not
        // the same as an empty open-block stack.
        return scan.RawBraceDepth > 0 ? CompletionContext.MusicBlock : CompletionContext.TopLevel;
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
    /// <c>roman | names</c>. The word right before <c>as</c> is the target NAME
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
        => IsInsideScoreBlock(new BlockContextScan(text, offset).Stack);

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
    /// Finds the key signature in force at <paramref name="offset"/> by scanning back
    /// for the nearest preceding <c>key &lt;tonic&gt; &lt;mode&gt;</c> declaration, and
    /// returns its sharp(+)/flat(-) count (0 = C major / no key found).
    /// </summary>
    private static int CurrentKeySharps(string text, int offset) => CurrentKey(text, offset).Sharps;

    /// <summary>
    /// The key in force at <paramref name="offset"/>: its tonic LETTER and its signature —
    /// the LAST <c>key</c> declaration before the caret, C major when there is none. ONE
    /// spelling for every reader that asks (the pitch rows, the chord lists in music, in
    /// <c>@chord(…)</c> and in <c>chords { }</c>), so the key the pitches are spelled for
    /// and the key the chords are built on cannot be two different keys.
    /// </summary>
    internal static (char Tonic, int Sharps) CurrentKey(string text, int offset)
    {
        if (offset > text.Length) offset = text.Length;
        var prefix = text.Substring(0, offset);
        // tonic carries its own accidental suffix (fis, bes, …); mode is a word.
        var matches = KeyDeclRegex().Matches(prefix);
        if (matches.Count == 0) return ('c', 0);
        var last = matches[matches.Count - 1];
        return (char.ToLowerInvariant(last.Groups[1].Value[0]),
            LilySharp.Core.Music.KeySpelling.SharpsFor(
                last.Groups[1].Value, last.Groups[2].Value) ?? 0);
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

}
