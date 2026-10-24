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

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The step keys on the language's value WORDS (owner's request 2026-10-24: "全部入れて"): a word
/// written right after its keyword and one of that keyword's values steps along the values, down
/// the list as the completion shows it (Down the next, Up the previous) — the values read from
/// <see cref="LanguageVocabulary"/>, the completion's own lists, so the two cannot disagree. A
/// part's <c>instrument</c> preset steps the presets and sounds the new one; <c>barNumbers every
/// N</c> steps its number; <c>paper { size … }</c> its page.
/// </summary>
/// <remarks>
/// Lily#'s choices: a word stops at either end; a value the list lacks is not stepped (the key's
/// own command runs — the word is a typo to fix, not a place in the list). <c>pitch</c> reads two
/// lists: a score's (how the page prints — <c>transposed</c> / <c>concert</c>) inside a
/// <c>score</c>, and how notes are typed (<c>written</c> / <c>concert</c>) at the top of the file
/// and in a part header.
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The values a keyword's word steps through, or null when the keyword has none (or
    /// <paramref name="value"/> is not one of them). <paramref name="inScore"/>: the word stands in
    /// a <c>score</c> (a page's <c>pitch</c>).</summary>
    private static IReadOnlyList<string>? ValuesOfKeyword(string keyword, string value, bool inScore)
    {
        IReadOnlyCollection<string>? values = keyword switch
        {
            "transposition" => LanguageVocabulary.TranspositionMarkers,
            "pedal" => LanguageVocabulary.PedalStyles,
            "pitch" => inScore ? LanguageVocabulary.PagePitchModes : LanguageVocabulary.PitchModes,
            "markTempo" => LanguageVocabulary.MarkArrangements,
            "barNumbers" => LanguageVocabulary.BarNumberPolicies,
            "accidentals" => LanguageVocabulary.AccidentalStyleWords,
            "sectionLabels" => LanguageVocabulary.SectionLabelStyles,
            "partCombineText" => LanguageVocabulary.PartCombineTextWords,
            "chordQualities" => LanguageVocabulary.ChordQualityStyleWords,
            "minorChords" => LanguageVocabulary.MinorChordWords,
            "chordNames" => LanguageVocabulary.ChordNameWords,
            "chordList" => LanguageVocabulary.ChordListWords,
            "compressRests" => LanguageVocabulary.CompressRestsWords,
            // `as` governs three different words: a chord row's display, a tab's style, and a
            // fonts entry's family — the one whose list holds the word.
            "as" => new[] { ChordAsWords, (IReadOnlyCollection<string>)LanguageVocabulary.TabStyles, FontFamilyWords }
                .FirstOrDefault(l => l.Contains(value, StringComparer.Ordinal)),
            _ => null,
        };
        return values != null && values.Contains(value, StringComparer.Ordinal) ? [.. values] : null;
    }

    /// <summary>A chord row's <c>as</c> words (ChordDisplayModeValidator's), in the completion's order.</summary>
    private static readonly string[] ChordAsWords = ["roman", "names"];

    /// <summary>A fonts entry's <c>as</c> words: the two generic families.</summary>
    private static readonly string[] FontFamilyWords = ["serif", "sans"];

    /// <summary>A keyword's value word the caret is on — the word right after the keyword among the
    /// same node's tokens, and one of its values (<see cref="ValuesOfKeyword"/>) — with its keyword,
    /// or null.</summary>
    private static (SyntaxTokenNode Keyword, SyntaxTokenNode Value)? KeywordValueAt(SyntaxTree tree, int offset)
    {
        foreach (var node in tree.GetNodes<SyntaxNode>())
        {
            SyntaxTokenNode? previous = null;
            for (int i = 0; i < node.SlotCount; i++)
            {
                var child = node.GetChild(i);
                // A part header's unwritten `:` is an empty slot (or a zero-width token): it stands
                // between nothing.
                if (child == null || child is SyntaxTokenNode { Text.Length: 0 })
                    continue;
                if (child is not SyntaxTokenNode token)
                {
                    previous = null;
                    continue;
                }
                if (previous != null && token.Span.Start <= offset && offset <= token.Span.End
                    && ValuesOfKeyword(previous.Text, token.Text, InScore(node)) != null)
                    return (previous, token);
                previous = token;
            }
        }
        return null;
    }

    private static bool InScore(SyntaxNode node)
    {
        for (SyntaxNode? n = node; n != null; n = n.Parent)
            if (n is RenderDeclarationSyntax)
                return true;
        return false;
    }

    /// <summary>One step of a keyword's value word: the next (Down) or previous (Up) value, stopping
    /// at either end. <c>barNumbers every N</c> keeps its number while it stays <c>every</c>, loses
    /// it to another value, and gains <c>4</c> when it becomes <c>every</c> (Lily#'s choice: a bar
    /// number every four bars, the phrase length most charts count in).</summary>
    private static (Edit? Edit, string Message) KeywordValueEdit(SyntaxTree tree, SyntaxTokenNode keyword,
        SyntaxTokenNode value, int dir)
    {
        var values = ValuesOfKeyword(keyword.Text, value.Text, InScore(keyword.Parent!))!;
        int at = values.ToList().IndexOf(value.Text), to = at - dir;
        if (to < 0 || to >= values.Count)
            return (null, $"{keyword.Text} {value.Text}: already the {(to < 0 ? "first" : "last")} ({string.Join(", ", values)})");
        string next = values[to];
        int end = value.Span.End;
        string written = next;
        if (keyword.Text == "barNumbers")
        {
            var number = NextToken(value);
            if (value.Text == "every" && number is { Kind: SyntaxKind.IntegerLiteral })
                end = number.Span.End;          // leaving `every N`: the number goes with it
            else if (next == "every")
                written = "every 4";            // arriving: a number to count by
        }
        return (new Edit(value.Span.Start, end, written), $"{keyword.Text} {written}");
    }

    /// <summary>The token after <paramref name="token"/> among its parent's children, or null.</summary>
    private static SyntaxTokenNode? NextToken(SyntaxTokenNode token)
    {
        var parent = token.Parent;
        if (parent == null)
            return null;
        for (int i = 0; i + 1 < parent.SlotCount; i++)
            if (ReferenceEquals(parent.GetChild(i), token) || parent.GetChild(i) is SyntaxTokenNode t && t.Span.Start == token.Span.Start)
                return parent.GetChild(i + 1) as SyntaxTokenNode;
        return null;
    }

    // ------------------------------------------------------------------ barNumbers every N

    /// <summary>The number of a <c>barNumbers every N</c> the caret is on, or null.</summary>
    private static SyntaxTokenNode? BarNumberEveryAt(SyntaxTree tree, int offset)
    {
        foreach (var node in tree.GetNodes<LayoutDeclarationSyntax>())
            for (int i = 0; i + 2 < node.SlotCount; i++)
                if (node.GetChild(i) is SyntaxTokenNode { Text: "barNumbers" }
                    && node.GetChild(i + 1) is SyntaxTokenNode { Text: "every" }
                    && node.GetChild(i + 2) is SyntaxTokenNode { Kind: SyntaxKind.IntegerLiteral } number
                    && number.Span.Start <= offset && offset <= number.Span.End)
                    return number;
        return null;
    }

    /// <summary>One step of <c>barNumbers every N</c>: a bar more (Up) or fewer (Down), from 1.</summary>
    private static (Edit? Edit, string Message) CountEdit(SyntaxTokenNode number, int dir, string what, int max = 999)
    {
        if (!int.TryParse(number.Text, out int n))
            return (null, $"{what}: write a whole number");
        int next = n + dir;
        if (next < 1 || next > max)
            return (null, $"{what} {n}: already the {(dir > 0 ? "most" : "fewest")} (1 to {max})");
        return (new Edit(number.Span.Start, number.Span.End, next.ToString()), $"{what} {next}");
    }

    // ------------------------------------------------------------------ numbers in the music

    /// <summary>The tuplet ratios the keys walk, fewest notes first — each the usual way to write
    /// that many notes (Lily#'s list: the denominator the note value they replace counts in).</summary>
    private static readonly (int Notes, int InTimeOf)[] TupletRatios =
        [(2, 3), (3, 2), (4, 3), (5, 4), (6, 4), (7, 4), (9, 8)];

    /// <summary>A number in the music the caret is on — a tuplet's ratio, a tremolo's count, a
    /// rest's <c>*N</c>, a <c>partial</c>'s length — as the node that holds it, or null.</summary>
    private static (TargetKind Kind, SyntaxNode Node)? MusicNumberAt(SyntaxTree tree, int offset)
    {
        static bool On(SyntaxNode? n, int at) => n is { } x && x.Span.Start <= at && at <= x.Span.End;
        foreach (var node in tree.GetNodes<SyntaxNode>())
            switch (node)
            {
                case TupletExpressionSyntax tuplet when On(tuplet.Numerator, offset) || On(tuplet.Denominator, offset):
                    return (TargetKind.TupletRatio, tuplet);
                case RepeatExpressionSyntax repeat when On(repeat.Count, offset):
                    return (TargetKind.MusicCount, repeat.Count);
                case RestSyntax rest when rest.GetChild(3) is SyntaxTokenNode { Text.Length: > 0 } count && On(count, offset):
                    return (TargetKind.MusicCount, count);
                case PartialDeclarationSyntax pickup when On(pickup.Duration, offset):
                    return (TargetKind.PartialLength, pickup.Duration);
            }
        return null;
    }

    /// <summary>One step of a tuplet's ratio: the next (Up, more notes) or previous (Down) of
    /// <see cref="TupletRatios"/>; a ratio off the list steps from where its notes sort.</summary>
    private static (Edit? Edit, string Message) TupletRatioEdit(TupletExpressionSyntax tuplet, int dir)
    {
        if (!int.TryParse(tuplet.Numerator.Text, out int notes) || !int.TryParse(tuplet.Denominator.Text, out int inTimeOf))
            return (null, "tuplet: write its ratio (tuplet 3/2)");
        int at = Array.IndexOf(TupletRatios, (notes, inTimeOf));
        int to = at >= 0 ? at + dir
            : dir > 0 ? Array.FindIndex(TupletRatios, r => r.Notes > notes)
            : Array.FindLastIndex(TupletRatios, r => r.Notes < notes);
        if (to < 0 || to >= TupletRatios.Length)
            return (null, $"tuplet {notes}/{inTimeOf}: already the {(dir > 0 ? "most" : "fewest")} notes "
                + $"({string.Join(" ", TupletRatios.Select(r => $"{r.Notes}/{r.InTimeOf}"))})");
        var (n, d) = TupletRatios[to];
        return (new Edit(tuplet.Numerator.Span.Start, tuplet.Denominator.Span.End, $"{n}/{d}"),
            $"tuplet {n}/{d}: {n} notes in the time of {d}");
    }

    /// <summary>The note values a <c>partial</c> walks, longest first.</summary>
    private static readonly int[] NoteValues = [1, 2, 4, 8, 16, 32, 64];

    /// <summary>One step of a <c>partial</c>'s length: a longer pickup (Up) or a shorter one (Down),
    /// the dots kept.</summary>
    private static (Edit? Edit, string Message) PartialLengthEdit(SyntaxNode duration, int dir)
    {
        if (duration.GetChild(0) is not SyntaxTokenNode value || !int.TryParse(value.Text, out int n))
            return (null, "partial: write its length (partial 4)");
        int at = Array.IndexOf(NoteValues, n);
        int to = at < 0 ? -1 : at - dir;
        if (to < 0 || to >= NoteValues.Length)
            return (null, $"partial {n}: already the {(dir > 0 ? "longest" : "shortest")} ({string.Join(" ", NoteValues)})");
        return (new Edit(value.Span.Start, value.Span.End, NoteValues[to].ToString()), $"partial {NoteValues[to]}");
    }

    // ------------------------------------------------------------------ paper size

    /// <summary>The page name of a <c>paper { size … }</c> the caret is on — the glued run of
    /// tokens after <c>size</c> (<c>a4</c> lexes as a pitch and a duration; GRAMMAR.md: SizeName) —
    /// as its first and last token and the name they spell, or null. A quoted size is not a name.</summary>
    private static (SyntaxTokenNode First, SyntaxTokenNode Last, string Name)? PaperSizeAt(SyntaxTree tree, int offset)
    {
        foreach (var paper in tree.GetNodes<PaperDeclarationSyntax>())
            for (int i = 0; i + 1 < paper.SlotCount; i++)
            {
                if (paper.GetChild(i) is not SyntaxTokenNode { Text: "size" }
                    || paper.GetChild(i + 1) is not SyntaxTokenNode { Kind: not SyntaxKind.StringLiteral } first)
                    continue;
                var last = first;
                for (int j = i + 2; j < paper.SlotCount && paper.GetChild(j) is SyntaxTokenNode glued
                     && glued.Span.Start == last.Span.End; j++)
                    last = glued;
                if (first.Span.Start <= offset && offset <= last.Span.End)
                {
                    string name = string.Concat(Enumerable.Range(i + 1, paper.SlotCount - i - 1)
                        .Select(paper.GetChild).OfType<SyntaxTokenNode>()
                        .TakeWhile(t => t.Span.Start <= last.Span.Start).Select(t => t.Text));
                    return (first, last, name);
                }
            }
        return null;
    }

    /// <summary>One step of a page name: the next (Down) or previous (Up) of the sizes the language
    /// knows, in the completion's order; a name off the list is not stepped.</summary>
    private static (Edit? Edit, string Message) PaperSizeEdit(SyntaxTokenNode first, SyntaxTokenNode last, string name, int dir)
    {
        var sizes = LanguageVocabulary.PaperSizeNames.ToList();
        int at = sizes.FindIndex(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
            return (null, $"size {name}: not a page the language knows ({string.Join(", ", sizes.Take(6))} …)");
        int to = at - dir;
        if (to < 0 || to >= sizes.Count)
            return (null, $"size {name}: already the {(to < 0 ? "first" : "last")} of the {sizes.Count} pages");
        return (new Edit(first.Span.Start, last.Span.End, sizes[to]), $"size {sizes[to]}");
    }

    // ------------------------------------------------------------------ instrument presets

    /// <summary>A part's <c>instrument</c> preset the caret is on — the glued run of tokens after the
    /// word (<c>alto-sax</c>), not the display name after it — as the property, its first and last
    /// token and the preset it spells, or null.</summary>
    private static (PropertyAssignmentSyntax Property, SyntaxTokenNode First, SyntaxTokenNode Last, string Preset)?
        InstrumentPresetAt(SyntaxTree tree, int offset)
    {
        foreach (var p in tree.GetNodes<PropertyAssignmentSyntax>())
            if (PresetOf(p) is { } preset && p.NameToken.Span.Start <= offset && offset <= preset.Last.Span.End)
                return (p, preset.First, preset.Last, preset.Name);
        return null;
    }

    /// <summary>The preset an <c>instrument</c> property writes: the glued run of tokens after the
    /// word, up to a display name — or null (another property, or no preset written).</summary>
    private static (SyntaxTokenNode First, SyntaxTokenNode Last, string Name)? PresetOf(PropertyAssignmentSyntax p)
    {
        if (p.NameToken.Text != "instrument")
            return null;
        var run = new List<SyntaxTokenNode>();
        for (int i = 1; i < p.SlotCount; i++)
        {
            if (p.GetChild(i) is not SyntaxTokenNode t || t.Span.Start == p.NameToken.Span.Start || t.Text.Length == 0)
                continue;
            if (t.Kind == SyntaxKind.StringLiteral || (run.Count > 0 && t.Span.Start != run[^1].Span.End))
                break;
            run.Add(t);
        }
        return run.Count > 0 ? (run[0], run[^1], string.Concat(run.Select(t => t.Text))) : null;
    }

    /// <summary>One step of an <c>instrument</c> preset: the next (Down) or previous (Up) of the
    /// presets in the completion's order (<see cref="LanguageVocabulary.InstrumentPresets"/>),
    /// stopping at either end; the new preset sounds (<see cref="PresetVoice"/>).</summary>
    private static (Edit? Edit, string Message) InstrumentPresetEdit(SyntaxTokenNode first, SyntaxTokenNode last,
        string preset, int dir)
    {
        var presets = LanguageVocabulary.InstrumentPresets.ToList();
        int at = presets.FindIndex(n => string.Equals(n, preset, StringComparison.OrdinalIgnoreCase));
        int to = at < 0 ? (dir < 0 ? 0 : presets.Count - 1) : at - dir;
        if (to < 0 || to >= presets.Count)
            return (null, $"instrument {preset}: already the {(to < 0 ? "first" : "last")} of the {presets.Count} presets");
        return (new Edit(first.Span.Start, last.Span.End, presets[to]), $"instrument {presets[to]}: {to + 1} of {presets.Count}");
    }

    /// <summary>What a preset sounds: one note in its General MIDI program (the preset's own,
    /// <see cref="Svg.Model.InstrumentDefaults.GetMidiProgram"/>), where its family sounds like
    /// itself (<see cref="SampleVoice"/>); silent for a word that is no preset.</summary>
    private static Voice PresetVoice(PropertyAssignmentSyntax property)
    {
        if (PresetOf(property) is not { } preset
            || Svg.Model.InstrumentDefaults.GetMidiProgram(preset.Name.ToLowerInvariant()) is not int program)
            return Voice.Silent;
        return SampleVoice(program);
    }
}
