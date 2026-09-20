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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The declarations the parser only ever places at the TOP of the tree, read from the
/// root's children instead of from a walk over every node.
/// </summary>
/// <remarks>
/// ⚠️ STRUCTURE, NOT STATISTICS. A validator that asked
/// <c>root.DescendantNodes().OfType&lt;PartDeclarationSyntax&gt;()</c> visited every note
/// of every part to find the parts, on every settled keystroke (RULES §7 item 9: one such
/// walk visited 234,030 nodes where the root has 5 children). These readers are safe only
/// because the PARSER places the kinds they name at a fixed depth:
/// <list type="bullet">
/// <item><c>part</c>, <c>form</c>, <c>score</c>, <c>drummap</c> are compilation-unit
/// items and nothing else — <c>Parser.ParseTopLevelItem</c> (Parser.cs) is the only
/// caller of their parse functions, and it is called only from
/// <c>ParseCompilationUnit</c>. A part inside a section is a <c>PartBlockSyntax</c>,
/// a different kind.</item>
/// <item><c>fonts</c>, <c>paper</c>, <c>layout</c> are compilation-unit items OR a
/// score's direct items — <c>Parser.ParseRenderItem</c> (Parser.Form.cs) parses them
/// with <c>inScore: true</c> straight into the render declaration's own slots.</item>
/// <item>A file is STRUCTURED (parts, sections, forms) exactly when one of those stands at
/// the root: a section may nest inside a part, but that part is a root child.</item>
/// </list>
/// Add a kind here only after reading where the parser creates it; a kind that can nest
/// deeper (a phrase, a section, a key signature) must keep its full walk or a green finder
/// (<c>SyntaxNode.KindSites</c>).
/// </remarks>
internal static class TopLevelNodes
{
    /// <summary>Root-level declarations of type <typeparamref name="T"/>, in document order.</summary>
    /// <remarks>
    /// The walk is a struct: <c>OfType&lt;T&gt;()</c> would box the child walk and build a
    /// filter iterator over the box, two objects for every ask (RULES §5.3, session 446).
    /// </remarks>
    public static TypedChildNodeList<T> OfRoot<T>(SyntaxNode root) where T : SyntaxNode
        => root.ChildNodesOfKind<T>();

    /// <summary>
    /// Root-level declarations of type <typeparamref name="T"/> and those written as a
    /// score's own items, in document (pre-order) order — the placement of
    /// <c>fonts</c> / <c>paper</c> / <c>layout</c>.
    /// </summary>
    public static IEnumerable<T> OfRootOrScore<T>(SyntaxNode root) where T : SyntaxNode
    {
        foreach (var child in root.ChildNodes())
        {
            if (child is T t)
                yield return t;
            else if (child is RenderDeclarationSyntax render)
                foreach (var item in render.ChildNodes())
                    if (item is T st)
                        yield return st;
        }
    }

    /// <summary>
    /// Whether the file has any structure (a part, a section or a form) — bare music is a
    /// plain note stream and several placement rules do not apply to it.
    /// </summary>
    public static bool IsStructured(SyntaxNode root)
    {
        // Written out rather than asked of Any(…): the predicate would box the struct walk.
        foreach (var n in root.ChildNodes())
            if (n is PartDeclarationSyntax or SectionDeclarationSyntax or FormDeclarationSyntax)
                return true;
        return false;
    }
}
