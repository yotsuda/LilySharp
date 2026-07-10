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
        var node = doc.Tree.FindNode(offset);

        if (node == null) return null;

        // Find variable reference
        var varRef = FindAncestor<VariableReferenceSyntax>(node);
        if (varRef != null)
        {
            var name = varRef.Name.Text;
            var definition = FindVariableDefinition(doc.Tree.GetRoot(), name);
            if (definition != null)
            {
                return CreateLocation(uri, doc.Text, definition);
            }
        }

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

    private static VariableDeclarationSyntax? FindVariableDefinition(SyntaxNode root, string name)
    {
        foreach (var decl in root.DescendantNodes<VariableDeclarationSyntax>())
        {
            if (decl.Name.Text == name)
                return decl;
        }
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
            var decl = FindVariableDefinition(doc.Tree.GetRoot(), name);
            if (decl != null)
            {
                locations.Add(CreateLocation(uri, doc.Text, decl.Name));
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
