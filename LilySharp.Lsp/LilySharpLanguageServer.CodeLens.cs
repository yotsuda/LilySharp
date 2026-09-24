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

using LilySharp.Core.Editing;
using LilySharp.Lsp.Protocol;
using StreamJsonRpc;
using LspRange = LilySharp.Lsp.Protocol.Range;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    // ========== CodeLens: a section's length, layers and form references ==========

    /// <summary>The command a section lens runs — registered by the extension, which turns the
    /// plain JSON arguments into VS Code's own types and opens the references peek on every
    /// layer of the section.</summary>
    internal const string ShowSectionLayersCommand = "lilysharp.showSectionLayers";

    [JsonRpcMethod(Methods.TextDocumentCodeLensName, UseSingleObjectParameterDeserialization = true)]
    public Task<CodeLens[]?> GetCodeLensAsync(CodeLensParams @params, CancellationToken token)
        => OffDispatch(() => GetCodeLens(@params), token);

    /// <summary>
    /// One lens over a section's first declaration: the section as a whole — the
    /// length it is laid out at and who writes it (the parts, chord rows and lyrics tracks),
    /// or, when they disagree, each length with who writes it — and how many times each form
    /// names it. A click lists everything that writes it. A later declaration gets a lens only
    /// when it writes a different length from most of the section's writers; in a
    /// section-major book the block that does (<c>chords prog { … }</c>) gets it on its own line.
    /// </summary>
    /// <remarks>
    /// The section is a shared span of time, not a block (the point an AI author's feedback on
    /// writing Lily# made first): the same name in three parts is one section written three
    /// times. The first cut repeated the whole line over every declaration — four identical
    /// lines in a four-declaration book, which the owner found noisy — so the line now stands
    /// once and the later declarations speak only when they are the odd ones out. It does not
    /// say which length is right when the layers disagree — the LYS2007 warning (one per
    /// section, grouped the same way) says what the page does about it.
    /// </remarks>
    public CodeLens[]? GetCodeLens(CodeLensParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null)
            return null;

        var lenses = new List<CodeLens>();
        foreach (var section in SectionOverview.Build(doc.Tree.GetRoot()))
        {
            var locations = section.Layers
                .Select(l => new Location { Uri = uri, Range = RangeOfSpan(doc.Text, l.Anchor) })
                .ToArray();
            void AddLens(Core.Syntax.TextSpan at, string title)
            {
                var range = RangeOfSpan(doc.Text, at);
                lenses.Add(new CodeLens
                {
                    Range = range,
                    Command = new Command
                    {
                        Title = title,
                        // With no layer to list (a header-only declaration of a section no
                        // part writes) the extension's command does nothing — an empty
                        // command id would make VS Code report an unknown command on click.
                        CommandIdentifier = ShowSectionLayersCommand,
                        Arguments = [uri.ToString(), range.Start, locations],
                    },
                });
            }

            for (int i = 0; i < section.Declarations.Count; i++)
            {
                var declaration = section.Declarations[i];
                // The whole section's line stands once, over its first declaration; a later
                // declaration speaks only when it is the odd one out.
                string? title = i == 0
                    ? SectionLensTitle(section)
                    : ShortDeclarationTitle(section, section.Declarations[i]);
                if (title != null)
                    AddLens(declaration.Name, title);
                // A section-major block (`melody { … }` inside `section A { … }`) is its own
                // line, so the odd one speaks there — over the first declaration too, whose
                // own line speaks for the whole section.
                foreach (var layer in section.Layers)
                    if (declaration.Writes(layer) && !IsAnchoredOn(declaration, layer)
                        && OddLayersTitle(section, [layer]) is { } own)
                        AddLens(layer.Anchor, own);
            }
        }
        return lenses.ToArray();
    }

    /// <summary>True when <paramref name="layer"/> stands on the declaration's own name — a
    /// part-major cell (<c>part melody { section A { … } }</c>) or the single-part shorthand —
    /// rather than on a block of its own inside a section-major declaration.</summary>
    private static bool IsAnchoredOn(SectionDeclaration declaration, SectionLayer layer)
        => layer.Anchor.Start == declaration.Name.Start;

    /// <summary>The lens's line: <c>Section A · 2 bars · melody, chords 'harmony' · 2× in form
    /// main</c> — WHO writes the section, by name while they are few and counted by kind when
    /// they are many (<c>11 parts, 1 chord row</c>); when they disagree, each length with who
    /// writes it (<c>⚠ 9 bars in flute, 8 bars in the other 11</c>).</summary>
    /// <remarks>
    /// ⚠️ NO "LAYER": the first cut said <c>1 layer</c>, a word from an AI author's feedback
    /// that Lily#'s own documents never use in that sense, and the owner asked what it meant.
    /// The names and kinds the book itself writes are what the line says instead.
    /// </remarks>
    internal static string SectionLensTitle(SectionSummary section)
    {
        static string Bars(int n) => n == 1 ? "1 bar" : $"{n} bars";

        var parts = new List<string> { $"Section {section.Name}" };
        if (section.Layers.Count == 0)
            parts.Add("no part writes it");
        else if (!section.IsInconsistent)
            parts.Add($"{Bars(section.Bars)} · {Writers(section.Layers)}");
        else
        {
            var groups = section.Layers
                .GroupBy(l => l.Bars)
                .OrderByDescending(g => g.Key)
                .Select(g => g.ToList())
                .ToList();
            var largest = groups.OrderByDescending(g => g.Count).First();
            parts.Add("⚠ " + string.Join(", ", groups.Select(g =>
                // The majority, when it is many, is "the other N" beside the named few.
                g.Count > MaxNamed && ReferenceEquals(g, largest)
                    ? $"{Bars(g[0].Bars)} in the other {g.Count}"
                    : $"{Bars(g[0].Bars)} in {Writers(g)}")));
        }

        parts.Add(section.FormReferences.Count == 0
            ? "in no form"
            : string.Join(", ", section.FormReferences.Select(f =>
                $"{f.Count}× in form {(f.Form.Length == 0 ? "(unnamed)" : f.Form)}")));
        return string.Join(" · ", parts);
    }

    /// <summary>The line over a later declaration of a section, or null when it has nothing of
    /// its own to say: <c>⚠ Section A · 11 bars here (1 bar longer) · 10 bars in 10 parts</c>
    /// when what it writes differs from what most of the section's writers write
    /// (<see cref="SectionSummary.CommonBars"/>). Only the layers standing on its name speak
    /// here; a section-major block speaks over its own line (<see cref="GetCodeLens"/>).</summary>
    /// <remarks>
    /// Measured against the MAJORITY, not the longest: ten parts at 10 bars and one at 11 is
    /// most likely one bar written twice, and a line on each of the ten would point at every
    /// place but the one to fix. The page still lays the section out at 11 — the first
    /// declaration's line says so.
    /// </remarks>
    internal static string? ShortDeclarationTitle(SectionSummary section, SectionDeclaration declaration)
        => OddLayersTitle(section, section.Layers
            .Where(l => declaration.Writes(l) && IsAnchoredOn(declaration, l)).ToList());

    /// <summary>The line over <paramref name="mine"/> — one declaration's cells or one
    /// section-major block — or null when none of them is odd. Several odd ones are named:
    /// <c>2 bars in oboe and horn (1 bar shorter)</c>; a lone one is <c>here</c>.</summary>
    internal static string? OddLayersTitle(SectionSummary section, IReadOnlyList<SectionLayer> mine)
    {
        static string Bars(int n) => n == 1 ? "1 bar" : $"{n} bars";

        int common = section.CommonBars;
        var oddGroups = mine.Where(section.IsOdd)
            .GroupBy(l => l.Bars)
            .OrderByDescending(g => g.Key)
            .Select(g => g.ToList())
            .ToList();
        if (oddGroups.Count == 0)
            return null;
        string Difference(int bars) => bars > common
            ? $"{Bars(bars - common)} longer"
            : $"{Bars(common - bars)} shorter";
        string oddText = string.Join(", ", oddGroups.Select(g =>
            $"{Bars(g[0].Bars)} {(mine.Count == 1 ? "here" : $"in {Writers(g)}")} ({Difference(g[0].Bars)})"));
        var majority = section.Layers.Where(l => !l.IsLyrics && l.Bars == common).ToList();
        return $"⚠ Section {section.Name} · {oddText} · {Bars(common)} in {Writers(majority)}";
    }

    /// <summary>How many writers a lens names before it counts them by kind instead.</summary>
    private const int MaxNamed = 3;

    /// <summary>Who writes a section: <c>melody</c>, <c>melody, chords 'harmony' and lyrics
    /// 'words'</c>; past <see cref="MaxNamed"/>, counted by kind — <c>11 parts, 1 chord row</c>.</summary>
    internal static string Writers(IReadOnlyList<SectionLayer> layers)
    {
        if (layers.Count <= MaxNamed)
        {
            var names = layers.Select(WriterName).ToList();
            return names.Count == 1 ? names[0]
                : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
        }
        static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
        var kinds = new List<string>();
        int partCount = layers.Count(l => !l.IsChords && !l.IsLyrics);
        int chordCount = layers.Count(l => l.IsChords);
        int lyricsCount = layers.Count(l => l.IsLyrics);
        if (partCount > 0) kinds.Add(Count(partCount, "part", "parts"));
        if (chordCount > 0) kinds.Add(Count(chordCount, "chord row", "chord rows"));
        if (lyricsCount > 0) kinds.Add(Count(lyricsCount, "lyrics track", "lyrics tracks"));
        return string.Join(", ", kinds);
    }

    /// <summary>A writer as the book names it: a part by its bare name (<c>melody</c>), a chord
    /// row or a lyrics track by kind and name (<c>chords 'harmony'</c>, <c>lyrics 'words'</c>),
    /// and the single-part shorthand (music written straight into a section) as what it is.</summary>
    private static string WriterName(SectionLayer layer)
    {
        const string part = "part '";
        if (layer.Label.StartsWith(part) && layer.Label.EndsWith('\''))
            return layer.Label.Substring(part.Length, layer.Label.Length - part.Length - 1);
        if (layer.Label.StartsWith("section '"))
            return "its music";
        return layer.Label;
    }

    private static LspRange RangeOfSpan(string text, Core.Syntax.TextSpan span)
    {
        var (startLine, startCol) = GetLineAndColumn(text, span.Start);
        var (endLine, endCol) = GetLineAndColumn(text, span.Start + span.Length);
        return new LspRange { Start = new Position(startLine, startCol), End = new Position(endLine, endCol) };
    }
}
