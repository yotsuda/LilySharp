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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>One layer of a section: a part, a chord row or a lyrics track writing it.</summary>
/// <param name="Label">How the layer is named — <c>part 'flute'</c>, <c>chords 'harmony'</c>,
/// <c>lyrics 'words'</c>.</param>
/// <param name="Bars">The bars it writes, counted as the bar checker counts them.</param>
/// <param name="Anchor">Its name token — where LYS2007 would stand on it.</param>
/// <param name="IsChords">A chord row.</param>
/// <param name="IsLyrics">A lyrics track (it never sets the section's length).</param>
public sealed record SectionLayer(string Label, int Bars, TextSpan Anchor, bool IsChords, bool IsLyrics);

/// <summary>One <c>section NAME { … }</c> declaration of a section.</summary>
/// <param name="Name">Its name token.</param>
/// <param name="Whole">The whole declaration — the layers whose anchors fall inside it are the
/// ones it writes.</param>
public readonly record struct SectionDeclaration(TextSpan Name, TextSpan Whole)
{
    /// <summary>True when <paramref name="layer"/> is written in this declaration.</summary>
    public bool Writes(SectionLayer layer)
        => layer.Anchor.Start >= Whole.Start && layer.Anchor.Start < Whole.Start + Whole.Length;
}

/// <summary>
/// One section as a whole — the shared span of time every layer that names it writes.
/// </summary>
/// <param name="Name">The section's name.</param>
/// <param name="Layers">Every layer that writes it, in document order.</param>
/// <param name="Declarations">Every <c>section NAME { … }</c> declaration of it, in document
/// order (one per layer in a part-major book, one per section in a section-major book, plus a
/// header-only <c>section A { partial 2 }</c>).</param>
/// <param name="FormReferences">How many times each form names it, by form name, in document
/// order — written references (<c>A</c>, <c>~A</c>, <c>[1. A]</c>); a <c>|: A :|</c> is one.</param>
public sealed record SectionSummary(
    string Name,
    IReadOnlyList<SectionLayer> Layers,
    IReadOnlyList<SectionDeclaration> Declarations,
    IReadOnlyList<(string Form, int Count)> FormReferences)
{
    /// <summary>The length the page lays the section out at: its longest part or chord row
    /// (a lyrics track never lengthens it). 0 when nothing but lyrics writes it.</summary>
    public int Bars => Layers.Where(l => !l.IsLyrics).Select(l => l.Bars).DefaultIfEmpty(0).Max();

    /// <summary>True when some layer is shorter than <see cref="Bars"/> — the case LYS2007
    /// reports. A lyrics track LONGER than the music is a stacked verse, not a mismatch.</summary>
    public bool IsInconsistent => Layers.Any(l => l.Bars < Bars);

    /// <summary>The length most of its parts and chord rows write — the one a writer that
    /// differs is measured against. Ten parts at 10 bars and one at 11 make the ONE the odd one
    /// (most likely a bar written twice), though the page lays the section out at 11
    /// (<see cref="Bars"/>). On a tie there is no telling, and the longer is taken, so the
    /// shorter — the one the page pads — is the odd one, as LYS2007's anchor picks it. 0 when
    /// nothing but lyrics writes it.</summary>
    public int CommonBars => Layers.Where(l => !l.IsLyrics)
        .GroupBy(l => l.Bars)
        .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
        .Select(g => g.Key)
        .FirstOrDefault();

    /// <summary>True when <paramref name="layer"/> differs from <see cref="CommonBars"/> — a
    /// part or chord row either way, a lyrics track only when shorter (a longer one is a
    /// stacked verse).</summary>
    public bool IsOdd(SectionLayer layer)
        => layer.IsLyrics ? layer.Bars < CommonBars : layer.Bars != CommonBars;
}

/// <summary>
/// The book's sections as the editor shows them — each section's layers with their lengths,
/// and where the forms name it (the CodeLens over every <c>section</c> declaration).
/// </summary>
/// <remarks>
/// The layers and their bar counts are the cross-part validator's own
/// (<see cref="SectionBarCounts"/> — the same house the exporters pad by), so what the lens says
/// is short is what LYS2007 says is short.
/// </remarks>
public static class SectionOverview
{
    /// <summary>Every section of the book that some layer writes or some form names, in the
    /// order its first declaration appears.</summary>
    public static IReadOnlyList<SectionSummary> Build(SyntaxNode root)
    {
        var layers = new Dictionary<string, List<SectionLayer>>(System.StringComparer.Ordinal);
        void Add(string section, SectionLayer layer)
        {
            if (!layers.TryGetValue(section, out var list))
                layers[section] = list = new();
            list.Add(layer);
        }

        foreach (var v in SectionBarCounts.SemanticVoices(root))
            Add(v.SectionName, new SectionLayer(v.Label, v.Bars, v.Anchor, v.IsChords, false));
        foreach (var v in SectionBarCounts.LyricsCells(root))
            Add(v.SectionName, new SectionLayer(v.Label, v.Bars, v.Anchor, false, true));

        var declarations = new Dictionary<string, List<SectionDeclaration>>(System.StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var node in root.DescendantNodes<SectionDeclarationSyntax>())
        {
            if (node is not SectionDeclarationSyntax sec || sec.Name.Text.Length == 0)
                continue;
            string name = sec.SectionName;
            if (!declarations.TryGetValue(name, out var spans))
            {
                declarations[name] = spans = new();
                order.Add(name);
            }
            spans.Add(new SectionDeclaration(sec.Name.Span, sec.Span));
            // A section-major lyrics block (`section A { lyrics w { … } }`) is a layer the
            // voices above do not list.
            foreach (var child in sec.ChildNodes())
                if (child is LyricsBlockSyntax lb && !lb.HasSections)
                {
                    var cell = SectionBarCounts.LyricsCell(name, lb, lb, SectionBarCounts.LyricsAnchor(lb), partMajor: false);
                    Add(name, new SectionLayer(cell.Label, cell.Bars, cell.Anchor, false, true));
                }
        }

        var references = new Dictionary<string, List<(string Form, int Count)>>(System.StringComparer.Ordinal);
        foreach (var token in SectionReferenceFinder.AllSectionNameTokens(root))
        {
            var form = FormOf(token);
            if (form == null)
                continue; // a declaration's own name
            if (!references.TryGetValue(token.Text, out var perForm))
                references[token.Text] = perForm = new();
            int at = perForm.FindIndex(f => f.Form == form);
            if (at < 0)
                perForm.Add((form, 1));
            else
                perForm[at] = (form, perForm[at].Count + 1);
        }

        return order.Select(name => new SectionSummary(
                name,
                layers.TryGetValue(name, out var l) ? l.OrderBy(x => x.Anchor.Start).ToList() : [],
                declarations[name],
                references.TryGetValue(name, out var r) ? r : []))
            .ToList();
    }

    /// <summary>The name of the form a section-name token stands in, or null when it is not
    /// inside a form (a declaration's name).</summary>
    private static string? FormOf(SyntaxTokenNode token)
    {
        for (var p = token.Parent; p != null; p = p.Parent)
            if (p is FormDeclarationSyntax form)
                return form.NameText;
        return null;
    }
}
