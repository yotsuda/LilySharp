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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns when a top-level single-value global setting (tempo / time / key / octave /
/// title / composer / font / paper / layout) is written more than once. Each sets ONE value, so
/// the collector keeps only the LAST occurrence — every earlier one is silently
/// overwritten and has no effect. A directive inside music (a section / part / phrase /
/// voice) is a mid-piece change, not a global default, so it is excluded.
/// </summary>
internal sealed class DuplicateGlobalSettingValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var groups = new Dictionary<string, List<SyntaxNode>>();
        // Asked of the kinds the switch can answer on rather than of every node: the switch
        // was cheap and the parent-chain test below was not, but asked of a whole book BOTH
        // were the validator (MEASURED, session 401: 6.4 of 6.4 ms on perf-fingbeam1k with
        // the chain first, 234,030 nodes for a handful of declarations — and session 409:
        // that book holds NONE of these kinds at all). Both are pure reads.
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(GlobalSettingKinds))
        {
            string? kind = SettingKindOf(node);
            if (kind == null || IsInMusic(node))
                continue;
            if (!groups.TryGetValue(kind, out var list))
                groups[kind] = list = new List<SyntaxNode>();
            list.Add(node);
        }

        foreach (var (kind, list) in groups)
            // In source order the LAST wins, so every earlier one is overwritten.
            for (int i = 0; i < list.Count - 1; i++)
                _diagnostics.Warning(list[i].Span, DiagnosticCodes.DuplicateGlobalSetting,
                    $"This '{kind}' is overwritten by a later '{kind}'; only the last one takes effect.");
    }

    /// <summary>The kinds <see cref="SettingKindOf"/> answers on, so the walk asks the
    /// tree's descendant index for those nodes instead of offering it every node of the
    /// book (this pass runs after every settled keystroke).</summary>
    /// <remarks>
    /// ⚠️ A SECOND SPELLING OF THE SWITCH, kept beside it on purpose (the shape
    /// <see cref="Editing.PartReferenceFinder.ReferenceKinds"/> has): a ninth global
    /// setting must be added to BOTH, or its duplicate goes unreported. Pinned by
    /// <c>TailValidatorKindsTests</c> over every node of the net books.
    /// </remarks>
    internal static readonly SyntaxKind[] GlobalSettingKinds =
    [
        SyntaxKind.TempoDeclaration, SyntaxKind.TimeSignature, SyntaxKind.KeySignature,
        SyntaxKind.OctaveDirective, SyntaxKind.FontDeclaration, SyntaxKind.PaperDeclaration,
        SyntaxKind.LayoutDeclaration, SyntaxKind.MetadataDeclaration,
    ];

    /// <summary>Which global setting this node states, or null. The singleton file
    /// defaults — the ones a later spelling of the same thing overwrites.</summary>
    internal static string? SettingKindOf(SyntaxNode node) => node switch
    {
        TempoDeclarationSyntax => "tempo",
        TimeSignatureSyntax => "time",
        KeySignatureSyntax => "key",
        OctaveDirectiveSyntax => "octave",
        // A NAMED fonts/paper block is a declaration, not the singleton file
        // default — several may coexist (name collisions are the
        // FontBinding/Paper validators' job), so only the unnamed form groups.
        FontDeclarationSyntax { NameToken: null } => "font",
        PaperDeclarationSyntax { NameToken: null } => "paper",
        LayoutDeclarationSyntax { NameToken: null } => "layout",
        MetadataDeclarationSyntax m => m.Keyword.ToLowerInvariant(), // title / composer
        _ => null,
    };

    private static bool IsInMusic(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PhraseDeclarationSyntax or SectionDeclarationSyntax
                or VariableDeclarationSyntax or PartBlockSyntax
                // A part-header directive (e.g. `part p { key bes major … }`) is a
                // PER-PART default, not a global one — a part that sets no key of its
                // own still inherits the top-level key. So it must not group with the
                // global settings, or it would falsely flag the global as overwritten.
                or PartDeclarationSyntax
                // Likewise a score's own header (`score sub { title "…" }`): it is a
                // PER-SCORE restatement, and scores that state none keep the file's.
                or RenderDeclarationSyntax)
                return true;
        return false;
    }
}
