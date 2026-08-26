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
using LspRange = LilySharp.Lsp.Protocol.Range;
using LspDiagnosticSeverity = LilySharp.Lsp.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    // ========== Semantic Tokens ==========

    [JsonRpcMethod(Methods.TextDocumentSemanticTokensFullName, UseSingleObjectParameterDeserialization = true)]
    public SemanticTokens? GetSemanticTokensFull(SemanticTokensParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var tokens = new List<int>(); // [deltaLine, deltaStart, length, tokenType, tokenModifiers]
        int prevLine = 0;
        int prevChar = 0;

        foreach (var token in CollectSemanticTokens(doc.Tree.GetRoot(), doc.Text))
        {
            int deltaLine = token.Line - prevLine;
            int deltaChar = deltaLine == 0 ? token.Character - prevChar : token.Character;

            tokens.Add(deltaLine);
            tokens.Add(deltaChar);
            tokens.Add(token.Length);
            tokens.Add(token.TokenType);
            tokens.Add(0); // No modifiers

            prevLine = token.Line;
            prevChar = token.Character;
        }

        return new SemanticTokens { Data = tokens.ToArray() };
    }

    internal record SemanticToken(int Line, int Character, int Length, int TokenType);

    /// <summary>Token-type indices, matching the legend order in
    /// <see cref="Initialize"/>. Written down because what goes on the wire is a NUMBER: the
    /// three name types were appended after the six standard and three music ones, and an
    /// index that disagrees with the legend paints the WRONG colour rather than none — the
    /// harder of the two to notice.</summary>
    private const int TokenTypePart = 9;
    private const int TokenTypeSection = 10;
    private const int TokenTypePhrase = 11;

    internal static IEnumerable<SemanticToken> CollectSemanticTokens(SyntaxNode root, string text)
    {
        var tokens = new List<SemanticToken>();

        // A name that REFERS to something — a section in a `form { }`, a part in a
        // `score { }` — is coloured only when it resolves, and what it refers to may be
        // declared further down the file, so the answer is not knowable at the moment the
        // reference is walked past. The references are set aside and filtered once the walk
        // has seen every declaration.
        //
        // ⚠️ One walk, not three. Semantic tokens are re-requested on every edit (RULES §7.9:
        // the diagnostics path IS the perf path, and this sits beside it), so collecting the
        // declared names in separate DescendantNodes() passes would multiply the cost of
        // every keystroke to save two lists holding the few names a file mentions.
        var names = new DeclaredNames();
        CollectTokensRecursive(root, text, tokens, names);

        // ⚠️ An unresolved name gets NO token (the user's call, 2026-08-23), and this is the
        // one place the colour says something a grammar could not: a form naming a section
        // that does not exist, or a staff naming a part that does not, stays plain beside
        // the squiggle LYS1005 / LYS1007 puts under it. Colouring it would have the editor
        // assert the name means something at the same moment the diagnostics say it does not.
        Resolve(names.SectionReferences, names.Sections, TokenTypeSection);
        Resolve(names.PartReferences, names.Parts, TokenTypePart);
        Resolve(names.ChordTrackReferences, names.ChordTracks, TokenTypePart);
        Resolve(names.LyricTrackReferences, names.LyricTracks, TokenTypePart);
        // ⚠️ The wider set: a `sings` target may name a part OR a named voice, which is what
        // LYS6011 checks it against. Resolving it against the parts alone would leave plain
        // a name the diagnostics accept — a quieter disagreement than colouring a bad one,
        // and the reason PartReferenceFinder.SingsTargetToken carries the warning it does.
        Resolve(names.SingsTargets, [.. names.Parts, .. names.Voices], TokenTypePart);

        return tokens.OrderBy(t => t.Line).ThenBy(t => t.Character);

        void Resolve(List<SyntaxTokenNode> references, HashSet<string> declared, int tokenType)
        {
            foreach (var reference in references)
            {
                if (!declared.Contains(reference.Text))
                    continue;
                var (line, character) = GetLineAndCharacter(text, reference.Span.Start);
                tokens.Add(new SemanticToken(line, character, reference.Width, tokenType));
            }
        }
    }

    /// <summary>What the walk learns about names as it goes: which are declared, and which
    /// referred to a declaration it may not have reached yet.</summary>
    private sealed class DeclaredNames
    {
        public readonly HashSet<string> Sections = new(StringComparer.Ordinal);
        public readonly HashSet<string> Parts = new(StringComparer.Ordinal);
        // Chord and lyric tracks are SEPARATE namespaces — `staff prog` on a chord track is
        // the empty staff LYS1007 exists to catch — so a row is resolved against its own.
        public readonly HashSet<string> ChordTracks = new(StringComparer.Ordinal);
        public readonly HashSet<string> LyricTracks = new(StringComparer.Ordinal);
        // What a `sings` target may name: a part OR a named voice of a `<< … >>`. A wider
        // set than the score's part references get, and LyricSingsValidator's (LYS6011).
        public readonly HashSet<string> Voices = new(StringComparer.Ordinal);

        public readonly List<SyntaxTokenNode> SectionReferences = [];
        public readonly List<SyntaxTokenNode> PartReferences = [];
        public readonly List<SyntaxTokenNode> ChordTrackReferences = [];
        public readonly List<SyntaxTokenNode> LyricTrackReferences = [];
        public readonly List<SyntaxTokenNode> SingsTargets = [];
    }

    private static void CollectTokensRecursive(
        SyntaxNode node, string text, List<SemanticToken> tokens, DeclaredNames names)
    {
        // Token types: 0=keyword, 1=variable, 2=number, 3=string, 4=comment, 5=operator,
        // 6=pitch, 7=articulation, 8=dynamic, 9=part, 10=section, 11=phrase

        // ⚠️ Every question here goes through the SAME predicate SymbolReferenceValidator
        // asks — SectionSymbols for a section, PartReferenceFinder for a part — so a name
        // squiggled as undefined can never also come out painted as resolved. Neither side
        // keeps its own list of the spellings that count.
        if (SectionSymbols.DeclaredName(node) is { } sectionName)
        {
            names.Sections.Add(sectionName.Text);
            Paint(sectionName, TokenTypeSection);
        }
        else if (SectionSymbols.ReferencedName(node) is { } sectionReference)
        {
            names.SectionReferences.Add(sectionReference);
        }
        else if (PartReferenceFinder.DeclaredName(node) is { } partName)
        {
            // Both declaring spellings: the header `part NAME { … }` and the section-body
            // block `NAME { … }`. A declaration is always coloured — it is what the
            // references are checked against, so it cannot itself fail to resolve.
            names.Parts.Add(partName.Text);
            Paint(partName, TokenTypePart);
        }
        else if (node is PhraseDeclarationSyntax phraseDeclaration)
        {
            Paint(phraseDeclaration.Name, TokenTypePhrase);
        }
        else if (PartReferenceFinder.DeclaredTrackName(node) is { } track)
        {
            // `lyrics NAME { … }` / `chords NAME { … }`. A track is a part-shaped thing —
            // a named strand a score places as a row — so it takes the part's colour (the
            // user's call, 2026-08-23) and keeps its own namespace for resolving.
            (track.IsChord ? names.ChordTracks : names.LyricTracks).Add(track.Token.Text);
            Paint(track.Token, TokenTypePart);
        }
        else
        {
            // `staff NAME`, `ossia NAME`, `tab NAME`, the members of a condensed or combined
            // staff, and a bare part name — every place a score names a part. Deferred: the
            // part may be declared below the score.
            PartReferenceFinder.CollectReferenceTokens(node, names.PartReferences);

            // The score's track rows, deferred and resolved against their own namespace.
            if (PartReferenceFinder.ReferencedTrackName(node) is { } row)
                (row.IsChord ? names.ChordTrackReferences : names.LyricTrackReferences)
                    .Add(row.Token);
        }

        // ⚠️ NOT in the chain above, and that was a real defect for one build: a `sings`
        // target sits on the SAME node as the track name it belongs to
        // (`lyrics verse sings melody { … }`), so an else-branch that had already claimed
        // the node as a declaration never looked for it — `verse` came out blue and the
        // `melody` beside it plain, which is the half-coloured line this whole run has been
        // closing. Asked of every node, like the voices below.
        if (PartReferenceFinder.SingsTargetToken(node) is { } sings)
            names.SingsTargets.Add(sings);

        PartReferenceFinder.CollectVoiceNames(node, names.Voices);

        void Paint(SyntaxTokenNode name, int tokenType)
        {
            var (line, character) = GetLineAndCharacter(text, name.Span.Start);
            tokens.Add(new SemanticToken(line, character, name.Width, tokenType));
        }

        if (node is SyntaxTokenNode tokenNode)
        {
            var kind = tokenNode.Kind;
            int? tokenType = kind switch
            {
                // Keywords
                //
                // ⚠️ VoltaKeyword sits beside AlternativeKeyword because they are one pair in
                // the parser, and until 2026-08-18 only one of them was here — so the two
                // colourers said different things about the same word. The TextMate grammar
                // painted `volta` and `alternative` as errors while this list painted
                // `alternative` a keyword, and a semantic token LAYERS OVER the grammar: the
                // word with the entry came out a keyword, the word without it came out red.
                // The grammar no longer says anything is wrong (see its `keywords` comment),
                // and `volta` is a live spelling — `fonts { volta "TeX Gyre Schola" }` binds
                // the volta-bracket face — so both belong here, saying the same thing.
                SyntaxKind.RepeatKeyword or
                SyntaxKind.VoltaKeyword or SyntaxKind.AlternativeKeyword or
                SyntaxKind.ScoreKeyword or SyntaxKind.PartKeyword or SyntaxKind.StaffKeyword or
                SyntaxKind.VoiceKeyword or SyntaxKind.TitleKeyword or SyntaxKind.ComposerKeyword or
                SyntaxKind.TempoKeyword or SyntaxKind.TimeKeyword or SyntaxKind.KeyKeyword or
                SyntaxKind.ClefKeyword or SyntaxKind.TupletKeyword or SyntaxKind.GraceKeyword or
                SyntaxKind.MajorKeyword or SyntaxKind.MinorKeyword or SyntaxKind.LyricsKeyword or
                SyntaxKind.OverrideKeyword or SyntaxKind.RevertKeyword or SyntaxKind.OnceKeyword or
                SyntaxKind.PhraseKeyword or SyntaxKind.SectionKeyword or SyntaxKind.FormKeyword => 0,

                // Numbers
                SyntaxKind.IntegerLiteral => 2,

                // Strings
                SyntaxKind.StringLiteral => 3,

                // Pitches
                SyntaxKind.PitchC or SyntaxKind.PitchD or SyntaxKind.PitchE or SyntaxKind.PitchF or
                SyntaxKind.PitchG or SyntaxKind.PitchA or SyntaxKind.PitchB => 6,

                // Rest
                SyntaxKind.RestR or SyntaxKind.RestS or SyntaxKind.RestR_Full => 6,

                // Articulation/ornament names are now '@name' identifiers resolved by
                // ArticulationRegistry, not distinct keyword tokens — no special case here.

                // Dynamic names
                SyntaxKind.DynamicPPP or SyntaxKind.DynamicPP or SyntaxKind.DynamicP or
                SyntaxKind.DynamicMP or SyntaxKind.DynamicMF or SyntaxKind.DynamicF or
                SyntaxKind.DynamicFF or SyntaxKind.DynamicFFF => 8,

                _ => null
            };

            if (tokenType.HasValue)
            {
                var (line, character) = GetLineAndCharacter(text, node.Span.Start);
                tokens.Add(new SemanticToken(line, character, node.Width, tokenType.Value));
            }
        }
        else if (node is VariableReferenceSyntax varRef)
        {
            // Variable reference (after $ or use)
            var nameNode = varRef.Name;
            var (line, character) = GetLineAndCharacter(text, nameNode.Span.Start);
            tokens.Add(new SemanticToken(line, character, nameNode.Width, 1));
        }
        else if (node is VariableDeclarationSyntax varDecl)
        {
            // Variable declaration name
            var nameNode = varDecl.Name;
            var (line, character) = GetLineAndCharacter(text, nameNode.Span.Start);
            tokens.Add(new SemanticToken(line, character, nameNode.Width, 1));
        }
        else if (node is PropertyAssignmentSyntax propAssign)
        {
            // Property VALUE tokens (instrument bass-guitar, name Foo, …):
            // color the whole value uniformly. Without this only value words
            // that happened to be keywords ("bass" = the clef name) lit up,
            // leaving "-guitar" plain. One span over first→last value token.
            // Restricted to word-valued properties — pitch/number values
            // (transpose d, channel 1) keep their own token colors and must
            // not sit inside an overlapping span.
            string propName = propAssign.NameToken.Text.ToLowerInvariant();
            if (propName is not ("instrument" or "name" or "tuning"))
                goto recurse;
            SyntaxTokenNode? firstVal = null, lastVal = null;
            for (int vi = 2; vi < propAssign.SlotCount; vi++)
            {
                if (propAssign.GetChild(vi) is SyntaxTokenNode vt)
                {
                    firstVal ??= vt;
                    lastVal = vt;
                }
            }
            if (firstVal != null && lastVal != null
                && firstVal.Kind != SyntaxKind.StringLiteral)
            {
                var (line, character) = GetLineAndCharacter(text, firstVal.Span.Start);
                int width = lastVal.Span.End - firstVal.Span.Start;
                tokens.Add(new SemanticToken(line, character, width, 3));
            }
            // fall through to recursion: the property NAME keyword still
            // gets its keyword color from the token pass.
            recurse: ;
        }
        else if (node is ArticulationSyntax artNode)
        {
            // '@' + name as ONE articulation-colored span. @cue/@feather/…
            // only lit up when a TextMate regex happened to list them.
            var (line, character) = GetLineAndCharacter(text, artNode.Span.Start);
            tokens.Add(new SemanticToken(line, character,
                artNode.NameToken.Span.End - artNode.Span.Start, 7));
        }
        else if (node is MusicMarkSyntax markNode)
        {
            // '@name' prefix only — parenthesised args keep their own colors
            // (numbers in @fig(6 4), the string in @text("…")).
            if (markNode.GetChild(1) is SyntaxTokenNode markName)
            {
                var (line, character) = GetLineAndCharacter(text, markNode.Span.Start);
                tokens.Add(new SemanticToken(line, character,
                    markName.Span.End - markNode.Span.Start, 7));
            }
        }
        else if (node is TempoDeclarationSyntax tempoNode)
        {
            // ★ `swing` / `shuffle`, the feel words, for the same reason `tremolo` / `unfold`
            // are handled below: they lex as identifiers, so `tempo` was coloured and the word
            // that changes what it MEANS was not. They are deliberately not reserved —
            // TempoValue says so — because a part and a marking may still be called that.
            //
            // ⚠️ Which is exactly why the editor's TextMate grammar cannot hold them, and the
            // three ways tried on 2026-08-18 each failed differently: a bare alternation paints
            // `part swing { … }`; a rule spanning the whole value run swallows the colours of
            // the marking and the numbers inside it; a begin/end to end of line eats the music
            // in `section A { tempo 120  m { c'4 } }`. Position is the missing information, and
            // here the tree has it.
            //
            // ⚠️ The reading is TempoValue's own, not a second copy of it: the feel-word arm
            // of that switch precedes the marking arm, so a bare `tempo swing` is the feel word
            // and never a marking spelled that way. Asking IsFeelWord the same question keeps
            // the colour and the meaning from parting company.
            foreach (var value in tempoNode.Values.OfType<SyntaxTokenNode>())
            {
                if (value.Kind != SyntaxKind.Identifier || !TempoValue.IsFeelWord(value.Text))
                    continue;
                var (fline, fchar) = GetLineAndCharacter(text, value.Span.Start);
                tokens.Add(new SemanticToken(fline, fchar, value.Width, 0));
            }
        }
        else if (node is RepeatExpressionSyntax repNode)
        {
            // 'tremolo' / 'percent' / 'unfold' lex as identifiers, not
            // keyword kinds — `repeat` colored, its type word did not.
            var rt = repNode.RepeatType;
            var (rline, rchar) = GetLineAndCharacter(text, rt.Span.Start);
            tokens.Add(new SemanticToken(rline, rchar, rt.Width, 0));
        }

        // Recurse into children
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null)
                CollectTokensRecursive(child, text, tokens, names);
        }
    }

    /// <summary>
    /// Line start offsets of a document, built once per text instance.
    /// </summary>
    /// <remarks>
    /// <see cref="GetLineAndCharacter"/> used to walk from offset 0 on every call,
    /// and its callers ask once PER TOKEN — so semantic tokens, the outline and
    /// folding all cost O(text × tokens). MEASURED on a generated score, one bar
    /// per line: textDocument/semanticTokens/full took 18 ms at 50 bars, 73 ms at
    /// 200, 498 ms at 500 and 1747 ms at 1000 — the shape of a quadratic, and
    /// enough to make a long score feel stuck after every edit.
    ///
    /// The table is keyed by the text INSTANCE, which the document manager
    /// replaces on every change, so a stale index cannot be handed out and the
    /// entry dies with the version that owned it.
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string, int[]> LineStartsCache = new();

    internal static int[] LineStartsOf(string text) =>
        LineStartsCache.GetValue(text, static t =>
        {
            var starts = new List<int> { 0 };
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '\n')
                {
                    starts.Add(i + 1);
                }
                else if (t[i] == '\r')
                {
                    // CRLF is ONE break; a lone '\r' breaks too.
                    if (i + 1 < t.Length && t[i + 1] == '\n') i++;
                    starts.Add(i + 1);
                }
            }
            return [.. starts];
        });

    /// <summary>Index of the line containing <paramref name="position"/>: the last
    /// line start at or before it.</summary>
    private static int LineOf(int[] lineStarts, int position)
    {
        int lo = 0, hi = lineStarts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (lineStarts[mid] <= position) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    internal static (int line, int character) GetLineAndCharacter(string text, int position)
    {
        // A malformed/synthetic node can report a position outside the document; clamp it
        // so the character is never negative (VS Code rejects a Position with a negative
        // character, and one bad symbol range aborts the WHOLE documentSymbol response).
        position = System.Math.Clamp(position, 0, text.Length);

        var lineStarts = LineStartsOf(text);

        // A position sitting exactly ON the '\n' of a CRLF is INSIDE the break.
        // The scan-from-zero version had already counted the line at the '\r' and
        // reported column 0 — the line the break OPENS, which starts one char on.
        if (position > 0 && position < text.Length
            && text[position - 1] == '\r' && text[position] == '\n')
            return (LineOf(lineStarts, position + 1), 0);

        int line = LineOf(lineStarts, position);
        return (line, System.Math.Max(0, position - lineStarts[line]));
    }

    /// <summary>The scan-from-zero original, kept as the reference the fast path
    /// is tested against (see GetLineAndCharacterTests).</summary>
    internal static (int line, int character) GetLineAndCharacterByScan(string text, int position)
    {
        position = System.Math.Clamp(position, 0, text.Length);

        int line = 0;
        int lastLineStart = 0;

        for (int i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lastLineStart = i + 1;
            }
            else if (text[i] == '\r')
            {
                line++;
                // Treat CRLF as ONE line break — but only swallow the '\n' when it lies
                // STRICTLY BEFORE position. If position sits exactly on the '\n' (a node
                // boundary inside a CRLF — common in a CRLF file), swallowing it pushes
                // lastLineStart past position, so `position - lastLineStart` goes NEGATIVE.
                // VS Code then rejects the whole documentSymbol response with "Illegal
                // argument: character must be non-negative". Guarding on `< position`
                // keeps that '\n' uncounted, mapping the boundary to column 0.
                if (i + 1 < position && text[i + 1] == '\n')
                    i++;
                lastLineStart = i + 1;
            }
        }

        // Belt-and-suspenders: the character can never be negative regardless of any
        // line-ending edge above (a negative Position aborts the entire outline).
        return (line, System.Math.Max(0, position - lastLineStart));
    }

    // ========== Folding Ranges ==========

    [JsonRpcMethod(Methods.TextDocumentFoldingRangeName, UseSingleObjectParameterDeserialization = true)]
    public FoldingRange[]? GetFoldingRanges(FoldingRangeParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var ranges = new List<FoldingRange>();
        CollectFoldingRanges(doc.Tree.GetRoot(), doc.Text, ranges);
        return ranges.ToArray();
    }

    private void CollectFoldingRanges(SyntaxNode node, string text, List<FoldingRange> ranges)
    {
        // Foldable node types: MusicBlock, PartDeclaration, etc.
        bool isFoldable = node is MusicBlockSyntax or
                          PartDeclarationSyntax or
                          RepeatExpressionSyntax or ParallelExpressionSyntax or
                          TupletExpressionSyntax or GraceExpressionSyntax or
                          LyricsBlockSyntax or AlternativeClauseSyntax or
                          SectionDeclarationSyntax or PhraseDeclarationSyntax or
                          FormDeclarationSyntax or RenderDeclarationSyntax;

        if (isFoldable && node.FullWidth > 0)
        {
            var startPos = node.Position;
            var endPos = node.Position + node.FullWidth - 1;

            var (startLine, _) = GetLineAndCharacter(text, startPos);
            var (endLine, endChar) = GetLineAndCharacter(text, endPos);

            // Only create fold if it spans multiple lines
            if (endLine > startLine)
            {
                ranges.Add(new FoldingRange
                {
                    StartLine = startLine,
                    EndLine = endLine,
                    Kind = FoldingRangeKind.Region
                });
            }
        }

        // Recurse into children
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null)
                CollectFoldingRanges(child, text, ranges);
        }
    }

}
