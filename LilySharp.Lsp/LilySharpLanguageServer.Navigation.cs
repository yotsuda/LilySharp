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
    // ========== Go to Definition ==========

    [JsonRpcMethod(Methods.TextDocumentDefinitionName, UseSingleObjectParameterDeserialization = true)]
    public Location? Definition(TextDocumentPositionParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var position = @params.Position;
        var offset = GetOffset(doc.Text, position.Line, position.Character);

        var target = ResolveDefinitionTarget(doc, offset);
        return target == null ? null : CreateLocation(uri, doc.Text, target);
    }

    /// <summary>
    /// The declaration NAME token the caret at <paramref name="offset"/> refers to,
    /// across every symbol namespace, or null when the caret is not on a resolvable
    /// reference. The reference-token selection mirrors Rename
    /// (<see cref="PartReferenceFinder"/> / <see cref="SectionReferenceFinder"/>) and
    /// <c>SymbolReferenceValidator</c>, so go-to-definition, rename, and the
    /// undefined-symbol diagnostics can never disagree about what a bare name means
    /// in a given position:
    /// <list type="bullet">
    /// <item>a score render target (<c>staff melody</c>, <c>tab</c>, <c>ossia</c>, a
    /// midi part) or a section-body part block → the <c>part</c> definition;</item>
    /// <item>a form/structure section reference (<c>form m { Main }</c>, <c>~Main</c>,
    /// <c>[1. Main]</c>) → the <c>section Main { … }</c> declaration;</item>
    /// <item>a score's form name (<c>score main …</c>) → the <c>form main { … }</c>
    /// declaration;</item>
    /// <item>a bare music-block reference (<c>intro</c>) → the <c>phrase</c> (or the
    /// legacy <c>name = …</c> variable) declaration.</item>
    /// </list>
    /// A reference whose name matches no declaration (an undefined-symbol error)
    /// resolves to null rather than falling through to another namespace.
    /// </summary>
    private static SyntaxNode? ResolveDefinitionTarget(Document doc, int offset)
    {
        var root = doc.Tree.GetRoot();

        // Part reference / declaration (offset-precise, mirrors Rename).
        if (PartReferenceFinder.PartNameTokenAt(root, offset) is { } partTok)
            return FindPartDefinition(root, partTok.Text);

        // Section reference / declaration (form body, silent ref, volta alternative).
        if (SectionReferenceFinder.SectionNameTokenAt(root, offset) is { } sectionTok)
            return FindSectionDefinition(root, sectionTok.Text);

        // A score's form name: `score main …` → `form main { … }`.
        if (TokenAt(FormNameTokens(root), offset) is { } formTok)
            return FindFormDefinition(root, formTok.Text);

        // A `with lyrics NAME` attachment / `lyrics NAME` row → the `lyrics NAME { … }`
        // block; the chord analog → the `chords NAME { … }` block.
        if (TokenAt(LyricsNameTokens(root), offset) is { } lyrTok)
            return FindLyricsDefinition(root, lyrTok.Text);
        if (TokenAt(ChordNameTokens(root), offset) is { } chordTok)
            return FindChordPartDefinition(root, chordTok.Text);

        // Bare music-block reference: a phrase (or legacy `name = …` variable).
        var node = doc.Tree.FindNode(offset);
        if (node != null && FindAncestor<VariableReferenceSyntax>(node) is { } varRef)
            return FindVariableDefinition(root, varRef.Name.Text);

        return null;
    }

    private static T? FindAncestor<T>(SyntaxNode node) where T : SyntaxNode
    {
        var current = node;
        while (current != null)
        {
            if (current is T t)
                return t;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>The declaration NAME token for a bare music reference
    /// <paramref name="name"/> — a <c>phrase NAME { … }</c> or a legacy
    /// <c>NAME = …</c> variable — or null. Phrases are checked first: a
    /// VariableReferenceSyntax in a music block is a phrase reference in every
    /// current sample, and phrases are the reason go-to-definition was extended
    /// here. Mirrors <c>SymbolReferenceValidator</c>, which treats both a phrase and
    /// a legacy variable as defining the name such a reference can resolve to.</summary>
    private static SyntaxTokenNode? FindVariableDefinition(SyntaxNode root, string name)
    {
        foreach (var phrase in root.DescendantNodes<PhraseDeclarationSyntax>())
            if (phrase.Name.Text == name) return phrase.Name;
        foreach (var decl in root.DescendantNodes<VariableDeclarationSyntax>())
            if (decl.Name.Text == name) return decl.Name;
        return null;
    }

    /// <summary>The <c>part</c> definition NAME token for <paramref name="name"/>:
    /// the <c>part NAME { … }</c> header if present, else the first section-body part
    /// block <c>NAME { … }</c> (which also defines the part's music). Null when no
    /// part of that name is defined. Matches the two forms
    /// <c>SymbolReferenceValidator</c> counts as defining a part.</summary>
    private static SyntaxTokenNode? FindPartDefinition(SyntaxNode root, string name)
    {
        foreach (var part in root.DescendantNodes<PartDeclarationSyntax>())
            if (part.Name.Text == name) return part.Name;
        foreach (var block in root.DescendantNodes<PartBlockSyntax>())
            if (block.PartName.Text == name) return block.PartName;
        return null;
    }

    /// <summary>The <c>section NAME { … }</c> declaration NAME token for
    /// <paramref name="name"/>, or null.</summary>
    private static SyntaxTokenNode? FindSectionDefinition(SyntaxNode root, string name)
    {
        foreach (var section in root.DescendantNodes<SectionDeclarationSyntax>())
            if (section.SectionName == name) return section.Name;
        return null;
    }

    /// <summary>The <c>form NAME { … }</c> declaration NAME token for
    /// <paramref name="name"/>, or null. A malformed form that omitted its name is
    /// skipped.</summary>
    private static SyntaxTokenNode? FindFormDefinition(SyntaxNode root, string name)
    {
        foreach (var form in root.DescendantNodes<FormDeclarationSyntax>())
            if (form.Name is { } n && form.NameText == name) return n;
        return null;
    }

    // ── name-token streams: the declaration NAME token plus every reference token,
    // in document order. Shared by Go to Definition (TokenAt → declaration) and
    // Rename (Occurrences → rewrite all), so the two agree on what a name means at a
    // position. Part and section names use the Core *ReferenceFinder classes.

    /// <summary>The first token in <paramref name="tokens"/> whose span contains
    /// <paramref name="offset"/> (end inclusive), or null.</summary>
    private static SyntaxTokenNode? TokenAt(IEnumerable<SyntaxTokenNode> tokens, int offset)
    {
        foreach (var t in tokens)
            if (offset >= t.Span.Start && offset <= t.Span.End)
                return t;
        return null;
    }

    /// <summary>True when an occurrence NAME token is a DECLARATION (rather than a
    /// reference), judged by its parent node — the node that introduces the name.
    /// Distinguishes Write vs Read for Document Highlight and lets Find All References
    /// honor <c>includeDeclaration=false</c>. A section-body part block
    /// (<c>NAME { … }</c>) counts as a declaration: it defines the part's music.</summary>
    private static bool IsDeclarationToken(SyntaxNode token) => token.Parent is
        PartDeclarationSyntax or PartBlockSyntax
        or SectionDeclarationSyntax
        or FormDeclarationSyntax
        or LyricsBlockSyntax or ChordPartBlockSyntax
        or PhraseDeclarationSyntax or VariableDeclarationSyntax;

    /// <summary>Every <c>lyrics NAME</c> name token — the block declaration plus each
    /// reference (<c>staff … with lyrics NAME</c> clauses and independent
    /// <c>lyrics NAME</c> rows) — in document order.</summary>
    private static IEnumerable<SyntaxTokenNode> LyricsNameTokens(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case LyricsBlockSyntax block when block.VoiceName is { Length: > 0 }
                    && block.GetChild(1) is SyntaxTokenNode dt:
                    yield return dt;
                    break;
                case LyricsRowRenderSyntax when node.GetChild(1) is SyntaxTokenNode rt && rt.Text.Length > 0:
                    yield return rt;
                    break;
                case StaffRenderSyntax staff:
                    foreach (var t in WithClauseNameTokens(staff, SyntaxKind.LyricsKeyword))
                        yield return t;
                    break;
            }
        }
    }

    /// <summary>Every <c>chords NAME</c> name token — the named chord-part block
    /// declaration plus each reference (<c>with chords NAME</c> clauses and
    /// <c>chords NAME</c> rows). An unnamed <c>chords { … }</c> (the chord-symbols
    /// form that aligns above a co-written staff) has no name and is skipped.</summary>
    private static IEnumerable<SyntaxTokenNode> ChordNameTokens(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case ChordPartBlockSyntax block when block.PartName is { Length: > 0 }
                    && block.GetChild(1) is SyntaxTokenNode dt:
                    yield return dt;
                    break;
                case ChordRowRenderSyntax when node.GetChild(1) is SyntaxTokenNode rt && rt.Text.Length > 0:
                    yield return rt;
                    break;
                case StaffRenderSyntax staff:
                    foreach (var t in WithClauseNameTokens(staff, SyntaxKind.ChordsKeyword))
                        yield return t;
                    break;
            }
        }
    }

    /// <summary>Every <c>form NAME</c> name token — the <c>form NAME { … }</c>
    /// declaration plus each <c>score NAME …</c> reference.</summary>
    private static IEnumerable<SyntaxTokenNode> FormNameTokens(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case FormDeclarationSyntax form when form.Name is { } n && n.Text.Length > 0:
                    yield return n;
                    break;
                case RenderDeclarationSyntax render when render.FormName is { } fn && fn.Text.Length > 0:
                    yield return fn;
                    break;
            }
        }
    }

    /// <summary>The NAME tokens of every <c>with lyrics</c> / <c>with chords</c> clause
    /// (per <paramref name="attachKind"/>) in a staff render item. Mirrors
    /// <c>RenderSpecParser.ParseStaff</c>: the name is two slots past <c>with</c>; a
    /// trailing <c>as roman|both|names</c> selector sits after it and is untouched.</summary>
    private static IEnumerable<SyntaxTokenNode> WithClauseNameTokens(StaffRenderSyntax staff, SyntaxKind attachKind)
    {
        for (int i = 0; i + 2 < staff.SlotCount; i++)
            if (staff.GetChild(i) is SyntaxTokenNode w && w.Kind == SyntaxKind.WithKeyword
                && staff.GetChild(i + 1) is SyntaxTokenNode k && k.Kind == attachKind
                && staff.GetChild(i + 2) is SyntaxTokenNode n && n.Text.Length > 0)
                yield return n;
    }

    /// <summary>The <c>lyrics NAME { … }</c> block's NAME token for
    /// <paramref name="name"/>, or null. A block's name is its voice-binding name.</summary>
    private static SyntaxTokenNode? FindLyricsDefinition(SyntaxNode root, string name)
    {
        foreach (var block in root.DescendantNodes<LyricsBlockSyntax>())
            if (block.VoiceName == name && block.GetChild(1) is SyntaxTokenNode n)
                return n;
        return null;
    }

    /// <summary>The <c>chords NAME { … }</c> block's NAME token for
    /// <paramref name="name"/>, or null.</summary>
    private static SyntaxTokenNode? FindChordPartDefinition(SyntaxNode root, string name)
    {
        foreach (var block in root.DescendantNodes<ChordPartBlockSyntax>())
            if (block.PartName == name && block.GetChild(1) is SyntaxTokenNode n)
                return n;
        return null;
    }

    /// <summary>The variable name at <paramref name="node"/> — from the node itself
    /// or its immediate parent being a variable reference/declaration — or null.
    /// Shared by Rename and DocumentHighlight; References/Definition use the
    /// full-ancestor <see cref="FindAncestor{T}"/> walk instead.</summary>
    private static string? FindVariableNameAt(SyntaxNode node) => node switch
    {
        VariableReferenceSyntax r => r.Name.Text,
        VariableDeclarationSyntax d => d.Name.Text,
        PhraseDeclarationSyntax p => p.Name.Text,
        _ => node.Parent switch
        {
            VariableReferenceSyntax r => r.Name.Text,
            VariableDeclarationSyntax d => d.Name.Text,
            PhraseDeclarationSyntax p => p.Name.Text,
            _ => null,
        },
    };

    /// <summary>Invokes <paramref name="onOccurrence"/> for every declaration
    /// (isDeclaration=true) then every reference of <paramref name="name"/>, passing
    /// each occurrence's NAME node. Declaration-first order matches what Rename and
    /// DocumentHighlight emit.</summary>
    private static void ForEachOccurrence(SyntaxNode root, string name, Action<SyntaxNode, bool> onOccurrence)
    {
        foreach (var decl in root.DescendantNodes<VariableDeclarationSyntax>())
            if (decl.Name.Text == name) onOccurrence(decl.Name, true);
        // A phrase is declared with `phrase NAME { … }` (not `NAME = …`), and a bare
        // reference to it parses as a VariableReferenceSyntax — so its declaration must
        // be included here too, else rename/highlight would miss the `phrase NAME` site.
        foreach (var phrase in root.DescendantNodes<PhraseDeclarationSyntax>())
            if (phrase.Name.Text == name) onOccurrence(phrase.Name, true);
        foreach (var reference in root.DescendantNodes<VariableReferenceSyntax>())
            if (reference.Name.Text == name) onOccurrence(reference.Name, false);
    }

    private Location CreateLocation(Uri uri, string text, SyntaxNode node)
    {
        // Use the node's Span (the bare token, excluding leading/trailing trivia)
        // rather than Position/FullWidth (which include trivia): an LSP range must
        // cover the identifier itself, not the surrounding whitespace/newlines.
        var (startLine, startCol) = GetLineAndColumn(text, node.Span.Start);
        var (endLine, endCol) = GetLineAndColumn(text, node.Span.End);

        return new Location
        {
            Uri = uri,
            Range = new LspRange
            {
                Start = new Position(startLine, startCol),
                End = new Position(endLine, endCol)
            }
        };
    }

    // ========== Find References ==========

    [JsonRpcMethod(Methods.TextDocumentReferencesName, UseSingleObjectParameterDeserialization = true)]
    public Location[]? References(ReferenceParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var offset = GetOffset(doc.Text, @params.Position.Line, @params.Position.Character);

        // Every occurrence of the symbol at the caret, across all named namespaces
        // (the same model Rename and Document Highlight use). Filter out declarations
        // when the client asked to exclude them.
        var occurrences = SymbolOccurrences(doc, offset);
        if (occurrences.Count == 0) return null;

        var include = @params.Context?.IncludeDeclaration ?? true;
        return occurrences
            .Where(t => include || !IsDeclarationToken(t))
            .Select(t => CreateLocation(uri, doc.Text, t))
            .ToArray();
    }

}
