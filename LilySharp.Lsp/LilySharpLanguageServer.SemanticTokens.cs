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

    private record SemanticToken(int Line, int Character, int Length, int TokenType);

    private IEnumerable<SemanticToken> CollectSemanticTokens(SyntaxNode root, string text)
    {
        var tokens = new List<SemanticToken>();
        CollectTokensRecursive(root, text, tokens);
        return tokens.OrderBy(t => t.Line).ThenBy(t => t.Character);
    }

    private void CollectTokensRecursive(SyntaxNode node, string text, List<SemanticToken> tokens)
    {
        // Token types: 0=keyword, 1=variable, 2=number, 3=string, 4=comment, 5=operator, 6=pitch, 7=articulation, 8=dynamic

        if (node is SyntaxTokenNode tokenNode)
        {
            var kind = tokenNode.Kind;
            int? tokenType = kind switch
            {
                // Keywords
                SyntaxKind.RepeatKeyword or
                SyntaxKind.AlternativeKeyword or
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
                CollectTokensRecursive(child, text, tokens);
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

    private static int[] LineStartsOf(string text) =>
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
