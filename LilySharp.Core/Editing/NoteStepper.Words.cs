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
using System.Collections.Immutable;
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

    /// <summary>One step of <c>barNumbers every N</c>: a bar more (Up) or fewer (Down), from
    /// <paramref name="min"/>.</summary>
    private static (Edit? Edit, string Message) CountEdit(SyntaxTokenNode number, int dir, string what, int max = 999,
        int min = 1)
    {
        if (!int.TryParse(number.Text, out int n))
            return (null, $"{what}: write a whole number");
        int next = n + dir;
        if (next < min || next > max)
            return (null, $"{what} {n}: already the {(dir > 0 ? "most" : "fewest")} ({min} to {max})");
        return (new Edit(number.Span.Start, number.Span.End, next.ToString()), $"{what} {next}");
    }

    /// <summary>The fewest times a <c>:|*N</c> plays: a repeat plays at least twice, which is what
    /// a <c>:|</c> without a count already says.</summary>
    private const int FewestRepeatPlays = 2;

    /// <summary>The count of a form repeat block's <c>:|*N</c> (the token after its <c>*</c>), or null.</summary>
    private static SyntaxTokenNode? BlockRepeatCount(FormRepeatBlockSyntax block)
    {
        for (int i = 0; i + 1 < block.SlotCount; i++)
            if (block.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.Asterisk }
                && block.GetChild(i + 1) is SyntaxTokenNode { Kind: SyntaxKind.IntegerLiteral, Text.Length: > 0 } count)
                return count;
        return null;
    }

    /// <summary>One step of a <c>:|*N</c>'s count (owner's request 2026-10-24): once more (Up) or
    /// once fewer (Down), from <see cref="FewestRepeatPlays"/>.</summary>
    private static (Edit? Edit, string Message) RepeatPlaysEdit(SyntaxTokenNode count, int dir)
    {
        var (edit, said) = CountEdit(count, dir, ":|*", min: FewestRepeatPlays);
        return edit is { } e ? (e, $":|*{e.NewText}: plays it {e.NewText} times") : (null, said);
    }

    // ------------------------------------------------------------------ numbers in the music

    /// <summary>The tuplet ratios the keys walk, fewest notes first — each the usual way to write
    /// that many notes (Lily#'s list: the denominator the note value they replace counts in).</summary>
    private static readonly (int Notes, int InTimeOf)[] TupletRatios =
        [(2, 3), (3, 2), (4, 3), (5, 4), (6, 4), (7, 4), (9, 8)];

    /// <summary>A number in the music the caret is on — a tuplet's ratio, a tremolo's count, a
    /// rest's <c>*N</c>, a repeat's <c>:|*N</c> (owner's request 2026-10-24), a <c>partial</c>'s
    /// length — as the node that holds it, or null.</summary>
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
                case BarlineSyntax { HasExplicitRepeatCount: true } bar
                    when bar.GetChild(2) is SyntaxTokenNode { Text.Length: > 0 } plays && On(plays, offset):
                    return (TargetKind.MusicCount, plays);
                // A form's `|: … :|*N` keeps its `*` and count on the block itself.
                case FormRepeatBlockSyntax block when BlockRepeatCount(block) is { } plays && On(plays, offset):
                    return (TargetKind.MusicCount, plays);
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

    // ------------------------------------------------------------------ names

    /// <summary>A name that refers to a declaration the caret is on — a section in a <c>form</c>
    /// (<c>A</c>, <c>~A</c>), the part or voice a lyrics track <c>sings</c> — or null.</summary>
    private static (TargetKind Kind, SyntaxTokenNode Name)? NameReferenceAt(SyntaxTree tree, int offset)
    {
        var root = tree.GetRoot();
        foreach (var node in root.DescendantNodesOfKinds(SectionSymbols.ReferencingKinds))
            if (SectionSymbols.ReferencedName(node) is { Text.Length: > 0 } name && name.Span.Start <= offset && offset <= name.Span.End)
                return (TargetKind.FormSection, name);
        foreach (var node in root.DescendantNodesOfKinds(PartReferenceFinder.SingsKinds))
            if (PartReferenceFinder.SingsTargetToken(node) is { Text.Length: > 0 } name && name.Span.Start <= offset && offset <= name.Span.End)
                return (TargetKind.SingsTarget, name);
        return null;
    }

    /// <summary>The names a reference walks: the sections the file declares, in the order written;
    /// or, for a <c>sings</c>, the parts in that order and then the named voices.</summary>
    private static List<string> NamesFor(SyntaxTree tree, TargetKind kind)
    {
        var root = tree.GetRoot();
        if (kind == TargetKind.FormSection)
        {
            var sections = new List<string>();
            foreach (var node in root.DescendantNodesOfKinds(SectionSymbols.DeclaringKinds))
                if (SectionSymbols.DeclaredName(node) is { Text.Length: > 0 } name && !sections.Contains(name.Text))
                    sections.Add(name.Text);
            return sections;
        }
        var names = DeclaredPartsInOrder(tree);
        var voices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodesOfKinds(PartReferenceFinder.VoiceIntroducingKinds))
            PartReferenceFinder.CollectVoiceNames(node, voices);
        names.AddRange(voices.Where(v => !names.Contains(v)).OrderBy(v => v, StringComparer.Ordinal));
        return names;
    }

    /// <summary>One step of a name reference (owner's request 2026-10-24): the next (Down) or
    /// previous (Up) of <see cref="NamesFor"/>, stopping at either end; a name the file does not
    /// declare steps from the ends. Only the name changes (a <c>~</c> stays).</summary>
    private static (Edit? Edit, string Message) NameReferenceEdit(SyntaxTree tree, TargetKind kind, SyntaxTokenNode name, int dir)
    {
        var names = NamesFor(tree, kind);
        string what = kind == TargetKind.FormSection ? "section" : "part";
        if (names.Count == 0)
            return (null, $"{name.Text}: the file declares no {what} to switch to");
        int at = names.IndexOf(name.Text);
        int to = at < 0 ? (dir < 0 ? 0 : names.Count - 1) : at - dir;
        string list = names.Count <= 8 ? $" ({string.Join(", ", names)})" : "";
        if (to < 0 || to >= names.Count)
            return (null, $"{name.Text}: already the {(to < 0 ? "first" : "last")} {what}{list}");
        return (new Edit(name.Span.Start, name.Span.End, names[to]), $"{names[to]}: {what} {to + 1} of {names.Count}{list}");
    }

    // ------------------------------------------------------------------ selections: a duration, a chord's quality

    /// <summary>
    /// The duration a selection covers exactly — the digits of a note's, a rest's or a chord's
    /// length (<c>c'4</c> with the <c>4</c> selected) — or null. Owner's request 2026-10-24: a
    /// caret on a note steps its pitch, as it always has, so the length steps only when it alone
    /// is selected.
    /// </summary>
    private static SyntaxNode? SelectedDuration(SyntaxTree tree, int start, int end)
    {
        foreach (var node in tree.GetNodes<SyntaxNode>())
        {
            var duration = node switch
            {
                NoteSyntax note => note.Duration,
                RestSyntax rest => rest.Duration,
                ChordSyntax { IsShapeChord: false } chord => chord.Duration,
                _ => null,
            };
            if (duration?.GetChild(0) is SyntaxTokenNode { Kind: SyntaxKind.IntegerLiteral } value
                && start <= value.Span.Start && value.Span.End <= end
                && duration.Span.Start <= start && end <= duration.Span.End)
                return duration;
        }
        return null;
    }

    /// <summary>One step of a selected duration: a longer note (Up) or a shorter one (Down) of
    /// <see cref="NoteValues"/>, the dots kept.</summary>
    private static (Edit? Edit, string Message) DurationEdit(SyntaxNode duration, int dir)
    {
        if (duration.GetChild(0) is not SyntaxTokenNode value || !int.TryParse(value.Text, out int n))
            return (null, "write a note value (c'4)");
        int at = Array.IndexOf(NoteValues, n);
        int to = at < 0 ? -1 : at - dir;
        if (to < 0 || to >= NoteValues.Length)
            return (null, $"{n}: already the {(dir > 0 ? "longest" : "shortest")} ({string.Join(" ", NoteValues)})");
        return (new Edit(value.Span.Start, value.Span.End, NoteValues[to].ToString()), $"a {NoteValues[to]} note");
    }

    /// <summary>The qualities the keys walk, in a chart's everyday order (Lily#'s list); a
    /// candidate the chord grammar refuses is passed by.</summary>
    private static readonly string[] ChordQualities =
        ["", "m", "7", "maj7", "m7", "6", "m6", "9", "m9", "sus2", "sus4", "7sus4", "dim", "dim7", "m7b5", "aug"];

    /// <summary>A chord symbol a selection covers — a chords-row entry's, or an <c>@chord(…)</c>'s
    /// first word — written as one glued run with no alteration list and no shape: its span, or
    /// null.</summary>
    private static (SyntaxNode Node, int Start, int End)? SelectedChordSymbol(string text, SyntaxTree tree, int start, int end)
    {
        foreach (var entry in tree.GetNodes<ChordEntrySyntax>())
        {
            if (entry.ShapesOpenParen != null || entry.SymbolText.Contains('('))
                continue;
            int s = -1, e = -1;
            for (int i = 0; i < entry.SlotCount; i++)
                if (entry.GetChild(i) is SyntaxTokenNode { Text.Length: > 0 } t)
                {
                    if (s < 0) s = t.Span.Start;
                    e = t.Span.End;
                }
            if (s >= 0 && start <= s && e <= end && end - start <= e - s + 1
                && text.Substring(s, e - s) == entry.SymbolText)
                return (entry, s, e);
        }
        foreach (var mark in tree.GetNodes<MusicMarkSyntax>())
        {
            if (mark.Name != "chord" || ChordAnnotation.WordSpans(mark) is not { Count: > 0 } spans)
                continue;
            var (s, e) = (spans[0].Start, spans[0].End);
            string word = text.Substring(s, e - s);
            if (!word.Contains('(') && start <= s && e <= end && end - start <= e - s + 1
                && Music.ChordStructure.TryParseChordEntry(word, out _))
                return (mark, s, e);
        }
        return null;
    }

    /// <summary>One step of a selected chord symbol's quality (owner's request 2026-10-24): the next
    /// (Down) or previous (Up) of <see cref="ChordQualities"/> that the chord grammar reads, the root
    /// and a <c>/bass</c> kept; a quality off the list steps from the ends.</summary>
    private static (Edit? Edit, string Message) ChordQualityEdit(string text, int start, int end, int dir)
    {
        string symbol = text.Substring(start, end - start);
        var m = System.Text.RegularExpressions.Regex.Match(symbol, @"^([A-G](?:is|es|s|#|b)?)([^/]*)(/.*)?$");
        if (!m.Success)
            return (null, $"{symbol}: not a chord name the step reads");
        string root = m.Groups[1].Value, quality = m.Groups[2].Value, bass = m.Groups[3].Value;
        var readable = ChordQualities.Where(q => Music.ChordStructure.TryParseChordEntry(root + q + bass, out _)).ToList();
        int at = readable.IndexOf(quality);
        int to = at < 0 ? (dir < 0 ? 0 : readable.Count - 1) : at - dir;
        if (to < 0 || to >= readable.Count)
            return (null, $"{symbol}: already the {(to < 0 ? "first" : "last")} ({string.Join(" ", readable.Select(q => root + q))})");
        string next = root + readable[to] + bass;
        return (new Edit(start, end, next), $"{next}");
    }

    // ------------------------------------------------------------------ bar lines

    /// <summary>The bar lines the keys walk (GRAMMAR.md: Barline), in the completion's order: the
    /// single, the double, the final, the dotted. The repeat bar lines (<c>|:</c>, <c>:|</c> …) are
    /// not here: they change the playing order, a different thing from a bar line's look.</summary>
    private static readonly string[] BarlineWalk = ["|", "||", "|.", "!"];

    /// <summary>The bar lines the keys walk in a <c>form</c>, in the owner's order (2026-10-24): the
    /// plain divider, the double, the repeat's <c>|:</c>, <c>:|</c> and <c>:|:</c>, the final, the
    /// dotted — the order the form's completion lists them in. A <c>|:</c> wants a <c>:|</c> after
    /// it (the form says so until it is written); a <c>:|</c> alone repeats from the top. A music
    /// body takes no repeat sign (LYS1034), so the music walks <see cref="BarlineWalk"/>.</summary>
    public static IReadOnlyList<string> FormBarlineWalk { get; } = ["|", "||", "|:", ":|", ":|:", "|.", "!"];

    /// <summary>A bar line of <see cref="BarlineWalk"/> the caret is on, or null — in the music, or
    /// in a <c>form</c>, where the engraved ones are bar-line nodes and the plain <c>|</c> is a bare
    /// divider token of the form itself (owner's request 2026-10-24: the form's too).</summary>
    private static SyntaxTokenNode? BarlineAt(SyntaxTree tree, int offset)
    {
        // A bar line node in a form walks the form's list (a `:|:` there is one).
        static bool On(SyntaxTokenNode t, int at) => (InForm(t) ? FormBarlineWalk : BarlineWalk).Contains(t.Text)
            && t.Span.Start <= at && at <= t.Span.End;
        foreach (var bar in tree.GetNodes<BarlineSyntax>())
            if (bar.GetChild(0) is SyntaxTokenNode token && On(token, offset))
                return token;
        foreach (var form in tree.GetNodes<SyntaxNode>())
        {
            if (form is not (FormDeclarationSyntax or FormRepeatBlockSyntax))
                continue;
            for (int i = 0; i < form.SlotCount; i++)
                if (form.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.Bar or SyntaxKind.RepeatStartBar
                        or SyntaxKind.RepeatBothBar or SyntaxKind.RepeatEndBar } divider
                    && FormBarlineWalk.Contains(divider.Text)
                    && divider.Span.Start <= offset && offset <= divider.Span.End)
                    return divider;
        }
        return null;
    }

    /// <summary>Whether a bar-line token stands in a form, where a plain <c>|</c> divides and draws nothing.</summary>
    private static bool InForm(SyntaxNode node)
    {
        for (SyntaxNode? n = node; n != null; n = n.Parent)
            if (n is FormDeclarationSyntax)
                return true;
        return false;
    }

    /// <summary>One step of a bar line (owner's request 2026-10-24): the next (Down) or previous (Up)
    /// of <see cref="BarlineWalk"/>, stopping at either end. Every one of them ends the bar
    /// (Semantics.WrittenBar), so only the look changes.</summary>
    private static (Edit? Edit, string Message) BarlineEdit(SyntaxTokenNode bar, int dir)
    {
        bool inForm = InForm(bar);
        var walk = inForm ? FormBarlineWalk : BarlineWalk;
        int at = walk.ToList().IndexOf(bar.Text), to = at - dir;
        if (at < 0 || to < 0 || to >= walk.Count)
            return (null, $"{bar.Text}: already the {(to < 0 ? "first" : "last")} ({string.Join(" ", walk)})");
        string next = walk[to];
        string said = BarlineSays(next, inForm);
        // A `:|*N` leaving the repeat's close takes its count with it: no other sign takes one.
        int end = bar.Span.End;
        if (bar.Text == ":|" && NextToken(bar) is { Text: "*" } star && NextToken(star) is { Kind: SyntaxKind.IntegerLiteral } count)
            end = count.Span.End;
        return (new Edit(bar.Span.Start, end, next), said);
    }

    /// <summary>What a bar line of <see cref="FormBarlineWalk"/> is, in a line — the step's message
    /// and the caret's status line (<see cref="Describe"/>) read this one wording.</summary>
    private static string BarlineSays(string bar, bool inForm) => bar switch
    {
        // In a form a plain `|` only divides the sections: no bar line is drawn there.
        "|" when inForm => "|: a plain divider - a form draws no bar line here",
        "|" => "|: the single bar line",
        "||" => "||: the double bar line",
        "|." => "|.: the final bar line",
        "!" => "!: the dotted bar line",
        "|:" => "|: opens a repeat - close it with :| in the form",
        ":|" => ":| closes the repeat (alone, it repeats from the top)",
        _ => ":|: closes the repeat before it and opens the next - close that with :|",
    };

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

    /// <summary>What a preset sounds, in its General MIDI program (the preset's own,
    /// <see cref="Svg.Model.InstrumentDefaults.GetMidiProgram"/>): a preset with strings strums its
    /// open strings as a <c>tuning</c> does (owner's request 2026-10-24) — string after string from
    /// the highest-numbered, a ukulele's high G first; one without (a flute, a voice) one note where
    /// its family sounds like itself (<see cref="SampleVoice"/>). Silent for a word that is no
    /// preset.</summary>
    private static Voice PresetVoice(PropertyAssignmentSyntax property)
    {
        if (PresetOf(property) is not { } written
            || Svg.Model.InstrumentDefaults.GetMidiProgram(written.Name.ToLowerInvariant()) is not int program)
            return Voice.Silent;
        if (Svg.Model.InstrumentDefaults.GetTuning(written.Name.ToLowerInvariant()) is { } tuning)
            return OpenStringsVoice(Tablature.Tunings.Parse(tuning), program);
        if (PresetChords.TryGetValue(written.Name.ToLowerInvariant(), out var chord))
            return new Voice(chord, Midi.GeneralMidi.PreviewTimbreFamily(program), program);
        return SampleVoice(program);
    }

    /// <summary>
    /// The presets that sound a chord, struck together (owner's request 2026-10-24: a piano hand's
    /// one note was a little lonely — "Cmaj7 or so"). Lily#'s choice of the voicings: the right
    /// hand close around middle C (C4 E4 G4 B4), the left hand open and low (C2 G2 B2 E3), since a
    /// close chord down there is muddy.
    /// </summary>
    private static readonly Dictionary<string, ImmutableArray<int>> PresetChords = new(StringComparer.Ordinal)
    {
        ["piano-right"] = [60, 64, 67, 71],
        ["piano-left"] = [36, 43, 47, 52],
    };
}
