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
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;
using LspDiagnosticSeverity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity;
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
        if (FormNameReferenceAt(root, offset) is { } formTok)
            return FindFormDefinition(root, formTok.Text);

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

    /// <summary>The form-name reference token of a <c>score NAME …</c> header whose
    /// span contains <paramref name="offset"/> (end inclusive), or null. This is the
    /// bare identifier right after <c>score</c>, never the quoted basename.</summary>
    private static SyntaxTokenNode? FormNameReferenceAt(SyntaxNode root, int offset)
    {
        foreach (var render in root.DescendantNodes<RenderDeclarationSyntax>())
            if (render.FormName is { } fn && offset >= fn.Span.Start && offset <= fn.Span.End)
                return fn;
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
        _ => node.Parent switch
        {
            VariableReferenceSyntax r => r.Name.Text,
            VariableDeclarationSyntax d => d.Name.Text,
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

        var position = @params.Position;
        var offset = GetOffset(doc.Text, position.Line, position.Character);
        var node = doc.Tree.FindNode(offset);

        if (node == null) return null;

        string? name = null;

        // Find variable name from reference or declaration
        var varRef = FindAncestor<VariableReferenceSyntax>(node);
        if (varRef != null)
        {
            name = varRef.Name.Text;
        }
        else
        {
            var varDecl = FindAncestor<VariableDeclarationSyntax>(node);
            if (varDecl != null)
            {
                name = varDecl.Name.Text;
            }
        }

        if (name == null) return null;

        var locations = new List<Location>();

        // Include declaration if requested
        if (@params.Context.IncludeDeclaration)
        {
            var declName = FindVariableDefinition(doc.Tree.GetRoot(), name);
            if (declName != null)
            {
                locations.Add(CreateLocation(uri, doc.Text, declName));
            }
        }

        // Find all references
        foreach (var reference in doc.Tree.GetRoot().DescendantNodes<VariableReferenceSyntax>())
        {
            if (reference.Name.Text == name)
            {
                locations.Add(CreateLocation(uri, doc.Text, reference.Name));
            }
        }

        return locations.ToArray();
    }

}
