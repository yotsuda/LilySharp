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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// A span is closed by the NAME it was opened with: <c>@8va … @!8va</c>, <c>@rit … @!rit</c>,
/// <c>@ottava(bassa) … @!ottava</c> (owner's decision 2026-10-28, "C": the reader sees which span
/// ends, and the writer need not know a span's family). Until then any name of the family closed
/// any start — <c>@8va … @!15ma</c> — as LilyPond's one stop per family does; the drawing still
/// pairs by family (one stop), and this is the rule on the writing.
/// </summary>
/// <remarks>
/// <para>The families where two names can meet: the ottava (8va, 8vb, 15ma, 15mb, ottava,
/// quindicesima) and the text spanner (rit, accel, rall, textSpan). A pedal is closed only by
/// its own name already, so the rule cannot fail on one, but it is read here too, for the end
/// that follows its start when the step keys turn a sustain into a sostenuto (owner's request
/// 2026-10-28). A hairpin also ends at the next dynamic, so the mark before an <c>@!cresc</c>
/// need not be the one it closes, and it is left alone.</para>
/// <para>Read on the syntax, in the order written within one voice line (a part's cell, a
/// phrase, one branch of <c>voice { } { }</c>): the end's partner is the nearest mark of its
/// family before it. A start that runs into another start (an ottava's change) is closed by
/// that start, so the end answers to the last one.</para>
/// <para>LILYSHARP-OWN: LilyPond's <c>\ottava #0</c> and <c>\stopTextSpan</c> carry no name.</para>
/// </remarks>
public static class SpanNames
{
    /// <summary>A span mark: the node, the name written, the token that spells it, whether it
    /// ends a span, and its family (the end the family shares).</summary>
    public readonly record struct Mark(SyntaxNode Node, string Name, SyntaxTokenNode NameToken, bool IsEnd, MusicMarkType Family);

    private static readonly HashSet<MusicMarkType> Families =
    [
        MusicMarkType.OttavaStop, MusicMarkType.TextSpanStop,
        MusicMarkType.SustainOff, MusicMarkType.SostenutoOff, MusicMarkType.UnaCordaOff,
    ];

    /// <summary>The node as a span mark of a family this rule reads, or null.</summary>
    public static Mark? Of(SyntaxNode node)
    {
        (string? name, SyntaxTokenNode? token, bool end) = node switch
        {
            ArticulationSyntax a => (a.Name, a.NameToken, false),
            MusicMarkSyntax m => (m.Name, NameTokenOf(m), m.IsSpanEnd),
            _ => (null, null, false),
        };
        if (name == null || token == null || MusicMarkItem.ParseSpanEndName(name) is not { } family || !Families.Contains(family))
            return null;
        return new Mark(node, name, token, end, family);
    }

    private static SyntaxTokenNode? NameTokenOf(MusicMarkSyntax mark)
    {
        for (int i = 0; i < mark.SlotCount; i++)
            if (mark.GetChild(i) is SyntaxTokenNode t && t.Kind is not (SyntaxKind.At or SyntaxKind.DashedBar))
                return t;
        return null;
    }

    /// <summary>The whole mark written at <paramref name="position"/> — <c>@accel</c>,
    /// <c>@textSpan("poco rit.")</c>, <c>@!8va</c>, the <c>@</c> to the end of its name or its
    /// argument — for a squiggle under the mark rather than under its first character (owner's
    /// request 2026-10-28: an unclosed <c>@accel</c> underlined only its <c>@</c>). One character
    /// where no mark stands there (a slur's <c>(</c>, a tie's <c>~</c>) or with no tree.</summary>
    public static TextSpan MarkSpan(SyntaxTree? tree, int position)
    {
        SyntaxNode? best = null;
        if (tree != null)
            foreach (var node in tree.GetRoot().DescendantNodes())
                if (node is ArticulationSyntax or MusicMarkSyntax or DynamicSyntax
                    && node.Span.Start <= position && position < node.Span.End
                    && (best == null || node.Span.Length < best.Span.Length))
                    best = node;
        return best?.Span ?? new TextSpan(position, 1);
    }

    /// <summary>The voice line a node is written in: the nearest branch of a <c>voice { } { }</c>,
    /// a section's part cell, a phrase, a section, else the file.</summary>
    private static SyntaxNode Line(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is MusicBlockSyntax { Parent: ParallelExpressionSyntax } or PartBlockSyntax or PhraseDeclarationSyntax
                or SectionDeclarationSyntax)
                return p;
        var top = node;
        while (top.Parent != null)
            top = top.Parent;
        return top;
    }

    /// <summary>The span marks of the node's voice line, in the order written.</summary>
    private static List<Mark> MarksBeside(SyntaxNode node)
    {
        var line = Line(node);
        var marks = new List<Mark>();
        foreach (var d in line.DescendantNodes())
            if (Of(d) is { } m && ReferenceEquals(Line(d), line))
                marks.Add(m);
        return marks;
    }

    /// <summary>The <c>@!</c> that ends a hairpin — the next <c>@!cresc</c> / <c>@!decresc</c> /
    /// <c>@!dim</c> in its line, when no dynamic (a level, which ends it, or a new hairpin) comes
    /// first — or null. For the step that turns a hairpin and its end together (owner's request
    /// 2026-10-28).</summary>
    public static SyntaxTokenNode? HairpinEndOf(DynamicSyntax start)
    {
        var line = Line(start);
        foreach (var d in line.DescendantNodes())
        {
            if (d.Span.Start <= start.Span.Start || !ReferenceEquals(Line(d), line))
                continue;
            if (d is DynamicSyntax)
                return null;
            if (d is MusicMarkSyntax { IsSpanEnd: true } end && SyntaxFacts.IsDynamicSpannerName(end.Name))
                return NameTokenOf(end);
        }
        return null;
    }

    /// <summary>The end that closes <paramref name="start"/> — the next mark of its family in its
    /// line, when that one is an end — or null.</summary>
    public static Mark? EndOf(SyntaxNode start)
    {
        if (Of(start) is not { IsEnd: false } open)
            return null;
        var marks = MarksBeside(start);
        int at = marks.FindIndex(m => ReferenceEquals(m.Node, start));
        for (int i = at + 1; i < marks.Count; i++)
            if (marks[i].Family == open.Family)
                return marks[i].IsEnd ? marks[i] : null;
        return null;
    }

    /// <summary>The start <paramref name="end"/> closes — the nearest mark of its family before
    /// it in its line, when that one is a start — or null.</summary>
    public static Mark? StartOf(SyntaxNode end)
    {
        if (Of(end) is not { IsEnd: true } close)
            return null;
        var marks = MarksBeside(end);
        int at = marks.FindIndex(m => ReferenceEquals(m.Node, end));
        for (int i = at - 1; i >= 0; i--)
            if (marks[i].Family == close.Family)
                return marks[i].IsEnd ? null : marks[i];
        return null;
    }
}

/// <summary>LYS4032: an end whose name is not the one its span was opened with
/// (<see cref="SpanNames"/>).</summary>
internal sealed class SpanEndNameValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var mark in tree.GetNodes<MusicMarkSyntax>())
        {
            if (!mark.IsSpanEnd || SpanNames.Of(mark) is not { } end || SpanNames.StartOf(mark) is not { } start
                || start.Name == end.Name)
                continue;
            _diagnostics.Error(end.NameToken.Span, DiagnosticCodes.SpanEndName,
                $"'@!{end.Name}' closes the '@{start.Name}' before it: a span is closed by the name it was "
                + $"opened with - write '@!{start.Name}'.");
        }
    }
}
