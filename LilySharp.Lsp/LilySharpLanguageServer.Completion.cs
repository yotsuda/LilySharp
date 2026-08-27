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
        // ⚠️ The ROMAN degrees are offered only in the BLOCK. The annotation reads
        // TryParseChordEntry alone, so `@chord(V7)` is refused — measured, and the reason
        // this is one call with a flag rather than one list for both contexts.
        if (IsInsideChordAnnotation(doc.Text, offset))
            return GetDiatonicChordCompletions(doc.Text, offset);
        if (IsInsideChordsBlock(doc.Text, offset))
            return GetDiatonicChordCompletions(doc.Text, offset, degreesToo: true);

        return context switch
        {
            // The position goes with the text: the `template-…` items need a RANGE (not just
            // an offset) to re-type the word being completed without changing it.
            CompletionContext.TopLevel => GetTopLevelCompletions(doc.Text, offset, position),
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
            // `size |` / `size "…"` — the paper-size table, spelled for its position.
            CompletionContext.AfterPaperSizeName => GetPaperSizeNameCompletions(insideString: false),
            CompletionContext.AfterPaperSizeNameQuoted => GetPaperSizeNameCompletions(insideString: true),
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

}
