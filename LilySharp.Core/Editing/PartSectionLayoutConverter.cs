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
using System.Text;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>Which way a <c>.lys</c> file nests its part × section music cells.</summary>
public enum LayoutForm
{
    /// <summary>Could not tell (no part blocks / inner sections found).</summary>
    Unknown,
    /// <summary>section-major: <c>section A { melody { … } bass { … } }</c>.</summary>
    SectionMajor,
    /// <summary>part-major: <c>part melody { … section A { … } section B { … } }</c>.</summary>
    PartMajor,
}

/// <summary>
/// Converts a <c>.lys</c> document between the two equivalent authoring layouts:
/// section-major (a section holds per-part music blocks) and part-major (a part
/// holds its own inner sections). The two are duals over the part × section music
/// cells; only the nesting is transposed. Music-cell text is preserved verbatim;
/// the part/section scaffolding is regenerated to a canonical shape, and every
/// other top-level item (title, structure, score, phrases, …) is kept verbatim.
/// </summary>
public static class PartSectionLayoutConverter
{
    /// <summary>Detects the current layout of <paramref name="source"/>.</summary>
    public static LayoutForm Detect(string source) => Detect(SyntaxTree.Parse(source).GetRoot());

    /// <summary>Detects the layout from a parsed root.</summary>
    public static LayoutForm Detect(CompilationUnitSyntax root)
    {
        var parts = TopLevel(root).OfType<PartDeclarationSyntax>().ToList();
        // part-major iff any part declaration carries inner sections.
        if (parts.Any(p => DirectChildrenOfType<SectionDeclarationSyntax>(p).Any()))
            return LayoutForm.PartMajor;
        // section-major iff any top-level section carries part blocks.
        if (TopLevel(root).OfType<SectionDeclarationSyntax>()
                .Any(s => DirectChildrenOfType<PartBlockSyntax>(s).Any()))
            return LayoutForm.SectionMajor;
        return LayoutForm.Unknown;
    }

    /// <summary>
    /// Converts <paramref name="source"/> to the OTHER layout. Returns null when the
    /// layout can't be determined (nothing to transpose).
    /// </summary>
    public static string? Convert(string source)
    {
        var tree = SyntaxTree.Parse(source);
        // Never transpose a malformed file: the cell extraction relies on a clean,
        // balanced tree, and the caller overwrites the whole document with the
        // result. A file with syntax errors is left untouched.
        if (tree.HasErrors)
            return null;

        var root = tree.GetRoot();
        var form = Detect(root);
        if (form == LayoutForm.Unknown)
            return null;
        var target = form == LayoutForm.PartMajor ? LayoutForm.SectionMajor : LayoutForm.PartMajor;

        var result = Emit(source, root, target);
        // Safety net: only return a result that round-trips to a clean parse, so a
        // surprising cell (e.g. one with embedded braces) can never corrupt the
        // document in place. If it wouldn't parse, report "no change" instead.
        return SyntaxTree.Parse(result).HasErrors ? null : result;
    }

    // --- model extraction -----------------------------------------------------

    private static string Emit(string source, CompilationUnitSyntax root, LayoutForm target)
    {
        // Part order + attributes (the part body items that are NOT inner sections).
        var parts = new List<(string Name, string Attrs)>();
        // (part, section) -> music cell text (verbatim, between the braces).
        var cells = new Dictionary<(string Part, string Section), string>();
        var sectionOrder = new List<string>();
        void AddSection(string name)
        {
            if (!sectionOrder.Contains(name)) sectionOrder.Add(name);
        }

        foreach (var member in TopLevel(root))
        {
            switch (member)
            {
                case PartDeclarationSyntax part:
                    var attrs = new List<string>();
                    foreach (var child in DirectChildren(part))
                    {
                        if (child is SectionDeclarationSyntax inner) // part-major cell
                        {
                            AddSection(inner.SectionName);
                            cells[(part.Name.Text, inner.SectionName)] = BetweenBraces(inner.ToFullString());
                        }
                        else
                        {
                            attrs.Add(child.ToFullString().Trim());
                        }
                    }
                    parts.Add((part.Name.Text, string.Join(" ", attrs.Where(a => a.Length > 0))));
                    break;

                case SectionDeclarationSyntax section
                        when DirectChildrenOfType<PartBlockSyntax>(section).Any(): // section-major
                    AddSection(section.SectionName);
                    foreach (var pb in DirectChildrenOfType<PartBlockSyntax>(section))
                        cells[(pb.Name, section.SectionName)] = BetweenBraces(pb.ToFullString());
                    break;
            }
        }

        // Ensure every (part) appears even if it only shows up as a cell owner.
        foreach (var (p, _) in cells.Keys.ToList())
            if (!parts.Any(pt => pt.Name == p))
                parts.Add((p, ""));

        var body = target == LayoutForm.PartMajor
            ? EmitPartMajor(parts, sectionOrder, cells)
            : EmitSectionMajor(parts, sectionOrder, cells);

        return Reassemble(source, root, body);
    }

    private static string EmitPartMajor(
        List<(string Name, string Attrs)> parts, List<string> sectionOrder,
        Dictionary<(string, string), string> cells)
    {
        var sb = new StringBuilder();
        foreach (var (name, attrs) in parts)
        {
            sb.Append("part ").Append(name).Append(" {\n");
            if (attrs.Length > 0)
                sb.Append("  ").Append(attrs).Append('\n');
            foreach (var section in sectionOrder)
                if (cells.TryGetValue((name, section), out var music))
                    sb.Append("  section ").Append(section).Append(' ').Append(Braced(music)).Append('\n');
            sb.Append("}\n");
        }
        return sb.ToString();
    }

    private static string EmitSectionMajor(
        List<(string Name, string Attrs)> parts, List<string> sectionOrder,
        Dictionary<(string, string), string> cells)
    {
        var sb = new StringBuilder();
        foreach (var (name, attrs) in parts)
        {
            sb.Append("part ").Append(name);
            sb.Append(attrs.Length > 0 ? " { " + attrs + " }\n" : " { }\n");
        }
        sb.Append('\n');
        foreach (var section in sectionOrder)
        {
            sb.Append("section ").Append(section).Append(" {\n");
            foreach (var (name, _) in parts)
                if (cells.TryGetValue((name, section), out var music))
                    sb.Append("  ").Append(name).Append(' ').Append(Braced(music)).Append('\n');
            sb.Append("}\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Rebuilds the document: every NON-structural top-level item (title, structure,
    /// score, phrases, global key/time/tempo, …) is kept verbatim in source order,
    /// and the regenerated part/section block replaces the structural region (parts
    /// + part-bearing sections) at the position of its first member.
    /// </summary>
    private static string Reassemble(string source, CompilationUnitSyntax root, string structuralBody)
    {
        var sb = new StringBuilder();
        bool emitted = false;
        foreach (var member in TopLevel(root))
        {
            bool structural = member is PartDeclarationSyntax
                || (member is SectionDeclarationSyntax s && DirectChildrenOfType<PartBlockSyntax>(s).Any());
            if (structural)
            {
                if (!emitted)
                {
                    // Keep the blank line / comment the user placed above the first
                    // part-or-section block — it is the leading trivia of that block's
                    // keyword token (the node-level trivia width is 0 for composites,
                    // so read it from the keyword token's span).
                    if (member.GetChild(0) is SyntaxTokenNode kw && kw.Span.Start > kw.FullSpan.Start)
                        sb.Append(source[kw.FullSpan.Start..kw.Span.Start]);
                    sb.Append(structuralBody);
                    emitted = true;
                }
                continue; // absorbed into the regenerated block
            }
            sb.Append(member.ToFullString());
        }
        if (!emitted) sb.Append(structuralBody);
        return sb.ToString();
    }

    // --- helpers --------------------------------------------------------------

    /// <summary>The top-level member nodes (skips the trailing EOF token).</summary>
    private static IEnumerable<SyntaxNode> TopLevel(CompilationUnitSyntax root) => DirectChildren(root);

    /// <summary>A node's direct child NODES (skips tokens such as keywords/braces).</summary>
    private static IEnumerable<SyntaxNode> DirectChildren(SyntaxNode node)
    {
        for (int i = 0; i < node.SlotCount; i++)
            if (node.GetChild(i) is SyntaxNode n and not SyntaxTokenNode)
                yield return n;
    }

    private static IEnumerable<T> DirectChildrenOfType<T>(SyntaxNode node) where T : SyntaxNode
        => DirectChildren(node).OfType<T>();

    /// <summary>The inner content between a node's first <c>{</c> and last <c>}</c>,
    /// trimmed — preserves the music verbatim (including any inner comments).</summary>
    private static string BetweenBraces(string text)
    {
        int open = text.IndexOf('{');
        int close = text.LastIndexOf('}');
        if (open < 0 || close < 0 || close <= open)
            return text.Trim();
        return text.Substring(open + 1, close - open - 1).Trim();
    }

    /// <summary>Wraps a music cell in braces. A cell that spans lines or ends in a
    /// <c>//</c> line comment puts the closing brace on its OWN line, so the comment
    /// can't swallow the brace (which would unbalance and corrupt the document).</summary>
    private static string Braced(string music)
        => music.Contains('\n') || music.Contains("//")
            ? "{ " + music + "\n}"
            : "{ " + music + " }";
}
