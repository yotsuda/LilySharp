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

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reports the verses of a lyric section that do not fit the repeat its section's own music
/// writes (<see cref="DiagnosticCodes.VerseAgainstRepeat"/>).
/// </summary>
/// <remarks>
/// <para>
/// A section whose music repeats counts ONE thing by its verses: <c>verse N</c> is the words
/// of the repeat's Nth pass (owner's rules 2026-11-14, docs/inline-repeat-design.md §6). So a
/// section that writes verses writes one for every pass, each pass once:
/// a pass NO verse names sings the last verse written before it; a verse for a pass the repeat
/// does not have is printed and never sung; of two verses naming one pass the first is sung.
/// Each is a WARNING — the owner's instruction is that a book must not stop playing over
/// words — and each has a quick fix (<c>Editing.VerseRepeatFixes</c>).
/// </para>
/// <para>
/// The passes of a section are the most its music asks for: a written <c>:|*N</c>, the highest
/// number a volta ending names, and two for a repeat that says neither. Words written with no
/// <c>verse</c> are sung on every pass and are told nothing. A section whose music writes no
/// repeat of its own is not read here: its verses are the form's plays of it.
/// </para>
/// <para>
/// ⚠️ Not here yet: words written in a bar the pass does not sing (a <c>verse 2</c> under a
/// first ending, or outside the repeat) — that needs each bar's passes, not the section's.
/// </para>
/// <para>
/// LILYSHARP-OWN: the language's rule about its own verses. Departs from: nothing in LilyPond
/// asks. Goes away: never on its own. Observed by: <c>VerseRepeatValidatorTests</c>.
/// </para>
/// </remarks>
internal sealed class VerseRepeatValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>What is wrong with a lyric section's verses.</summary>
    internal enum Fault
    {
        /// <summary>A pass of the repeat no verse names.</summary>
        MissingPass,
        /// <summary>A verse all of whose passes lie past the repeat's.</summary>
        NeverSung,
        /// <summary>A verse naming a pass an earlier verse names.</summary>
        PassNamedTwice,
    }

    /// <summary>One finding: the verse it is reported at (for a missing pass, the last verse
    /// written), the section, the passes its repeat has, and the pass in question.</summary>
    internal readonly record struct Finding(Fault Fault, SyntaxNode Verse, string Section, int Passes, int Pass);

    public void Validate(SyntaxTree tree)
    {
        foreach (var f in Findings(tree.GetRoot()))
        {
            var at = HeaderSpan(f.Verse);
            string message = f.Fault switch
            {
                Fault.MissingPass =>
                    $"Section '{f.Section}' repeats: its music plays it {f.Passes} times, and no verse here is for pass {f.Pass} - "
                    + $"it sings the last verse written before it. Write 'verse {f.Pass} {{ … }}' with that pass's words "
                    + "(words that are the same every pass need no 'verse' at all).",
                Fault.NeverSung =>
                    $"'{HeaderText(f.Verse)}' is never sung: section '{f.Section}' repeats, and its music plays it {f.Passes} times. "
                    + $"The words are printed all the same. Delete the verse, or repeat more times (':|*{f.Pass}').",
                _ =>
                    $"Pass {f.Pass} of section '{f.Section}' has two verses: the first written is sung, and "
                    + $"'{HeaderText(f.Verse)}' is printed and not sung on it. Give each pass one verse.",
            };
            _diagnostics.Warning(at, DiagnosticCodes.VerseAgainstRepeat, message);
        }
    }

    /// <summary>The findings of every lyric section whose section's music repeats, in document order.</summary>
    internal static List<Finding> Findings(SyntaxNode root)
    {
        var findings = new List<Finding>();
        var passesOf = PassesOfRepeatingSections(root);
        if (passesOf.Count == 0)
            return findings;
        foreach (var cell in root.DescendantNodesOfKinds(SyntaxKind.LyricVerse).GroupBy(v => v.Parent))
        {
            if (cell.Key == null || SectionOf(cell.Key) is not { } section || !passesOf.TryGetValue(section, out int passes))
                continue;
            var verses = cell.OrderBy(v => v.Span.Start).ToList();
            var named = new HashSet<int>();
            foreach (var verse in verses)
            {
                var plays = LyricsCollector.PlaysOfVerseAt(root, verse.Span.Start);
                if (plays.Count > 0 && plays.All(p => p > passes))
                    findings.Add(new Finding(Fault.NeverSung, verse, section, passes, plays.Max()));
                else if (plays.FirstOrDefault(named.Contains) is int twice and > 0)
                    findings.Add(new Finding(Fault.PassNamedTwice, verse, section, passes, twice));
                named.UnionWith(plays);
            }
            for (int pass = 1; pass <= passes; pass++)
                if (!named.Contains(pass))
                    findings.Add(new Finding(Fault.MissingPass, verses[^1], section, passes, pass));
        }
        return findings;
    }

    /// <summary>The passes of each section whose own music writes a repeat sign or an ending: a
    /// written <c>:|*N</c>, the highest pass an ending names, and at least two.</summary>
    internal static Dictionary<string, int> PassesOfRepeatingSections(SyntaxNode root)
    {
        var passes = new Dictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var sign in root.DescendantNodesOfKinds(SyntaxKind.Barline, SyntaxKind.InlineVolta))
        {
            int asks = sign switch
            {
                BarlineSyntax line when !SyntaxFacts.IsRepeatBarlineKind(line.BarToken.Kind) => 0,
                BarlineSyntax { HasExplicitRepeatCount: true } line => line.RepeatCount,
                InlineVoltaSyntax ending => ending.Numbers.DefaultIfEmpty(0).Max(),
                _ => 2,
            };
            if (asks == 0 && sign is BarlineSyntax)
                continue;
            if (SectionOf(sign) is { } section)
                passes[section] = System.Math.Max(System.Math.Max(2, asks), passes.GetValueOrDefault(section));
        }
        return passes;
    }

    private static string? SectionOf(SyntaxNode node)
    {
        for (var up = node; up != null; up = up.Parent)
            if (up is SectionDeclarationSyntax section)
                return section.SectionName;
        return null;
    }

    /// <summary>The verse's header — <c>verse 2</c>, up to its brace.</summary>
    internal static TextSpan HeaderSpan(SyntaxNode verse)
        => new(verse.Span.Start, System.Math.Max(1, HeaderText(verse).Length));

    private static string HeaderText(SyntaxNode verse)
    {
        var words = new System.Text.StringBuilder();
        for (int i = 0; i < verse.SlotCount; i++)
        {
            if (verse.GetChild(i) is not SyntaxTokenNode token || token.Kind == SyntaxKind.OpenBrace)
                break;
            words.Append(token.ToFullString());
        }
        return words.ToString().Trim();
    }
}
