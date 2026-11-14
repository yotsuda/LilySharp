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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The rules of a repeat, an ending and a navigation mark written in a section's MUSIC
/// (docs/inline-repeat-design.md §3.3, §3.5, §3.6) — the form's rules, asked of the score's sign
/// column (<see cref="RepeatStream"/>), where a sign of the music and a sign of the form are one
/// set whichever voice or section writes them.
/// </summary>
/// <remarks>
/// <para>
/// On the column, per form: a <c>|:</c> that opens while the one before it is still open
/// (<see cref="DiagnosticCodes.NestedRepeat"/> — repeats do not nest); a repeat with endings
/// whose passes are not each named once, that writes <c>:|*N</c> beside the numbers, or that
/// has no body (LYS1043, LYS1042, LYS1041 — the form's own rules and words); an ending no
/// repeat opens (LYS6008). Each is reported at the sign the MUSIC writes; a sign of the form
/// alone is <see cref="FormDeclarationValidator"/>'s.
/// </para>
/// <para>
/// In the text, per voice: an ending or a navigation mark written INSIDE a bar
/// (<see cref="DiagnosticCodes.SignInsideABar"/>) — a sign stands on a bar line. The bar a
/// sign cuts is written with a line there (<c>g a | [1. b c :|</c>), and read as one bar
/// (<see cref="SignCutBars"/>).
/// </para>
/// <para>
/// LILYSHARP-OWN: the language's rules about its own signs. Departs from: LilyPond's
/// <c>\repeat volta</c> nests and stands at any moment. Goes away: never on its own.
/// Observed by: <c>MusicRepeatValidatorTests</c>.
/// </para>
/// </remarks>
internal sealed class MusicRepeatValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly HashSet<(int Start, string Code)> _said = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        if (!RepeatStream.InTheMusic(root))
            return;
        var phrases = SectionBarCounts.PhraseBodies(root);
        var voices = SectionBarCounts.SemanticVoices(root, phrases);

        foreach (var voice in voices)
            if (!voice.IsChords && !voice.IsLyrics)
                ReportSignsInsideABar(voice.Container);

        var forms = new List<FormDeclarationSyntax?>();
        foreach (var form in TopLevelNodes.OfRoot<FormDeclarationSyntax>(root))
            forms.Add(form);
        if (forms.Count == 0)
            forms.Add(null);
        var signs = new Dictionary<string, SectionSigns>(StringComparer.Ordinal);
        foreach (var form in forms)
            ReportOnTheColumn(RepeatStream.For(root, form), form, voices, phrases, signs);
    }

    private void Error(TextSpan span, string code, string message)
    {
        if (_said.Add((span.Start, code)))
            _diagnostics.Error(span, code, message);
    }

    /// <summary>The ink of an ending's opening: <c>[1.</c>.</summary>
    private static TextSpan InkOf(InlineVoltaSyntax ending)
        => new(ending.Span.Start, Math.Min(ending.Span.Length, 1 + ending.VoltaText.Length));

    // ───────────────────────── in the text ─────────────────────────

    private void ReportSignsInsideABar(SyntaxNode container)
    {
        foreach (var ending in container.DescendantNodes<InlineVoltaSyntax>())
            if (MusicInTheBar(ending, before: true))
                Error(InkOf(ending), DiagnosticCodes.SignInsideABar,
                    $"The ending '[{ending.VoltaText}' opens inside a bar: an ending opens on a bar line. Write '|' before it "
                    + "- a bar the ending cuts is still read as one bar ('g a | [1. b c :| [2. b g ] |').");
        foreach (var mark in container.DescendantNodes<NavigationMarkSyntax>())
            if (MusicInTheBar(mark, before: true) && MusicInTheBar(mark, before: false))
                Error(mark.Span, DiagnosticCodes.SignInsideABar,
                    "This mark stands inside a bar, with music before and after it: a navigation mark stands on a bar line "
                    + "- 'segno' and 'coda' at the head of the bar after it, 'fine', 'to coda', 'dc' and 'ds' at the end of "
                    + "the bar before it. Write '|' where it stands; a bar the mark cuts is still read as one bar.");
    }

    /// <summary>Whether timed music stands beside <paramref name="sign"/> in its own bar —
    /// between it and the nearest bar line (or ending, or the scope's edge) on that side.</summary>
    private static bool MusicInTheBar(SyntaxNode sign, bool before)
    {
        if (sign.Parent is not { } parent)
            return false;
        var siblings = parent.ChildNodes().ToList();
        int at = siblings.FindIndex(n => ReferenceEquals(n, sign));
        for (int i = before ? at - 1 : at + 1; i >= 0 && i < siblings.Count; i += before ? -1 : 1)
        {
            if (siblings[i] is BarlineSyntax or InlineVoltaSyntax)
                return false;
            if (siblings[i] is not SyntaxTokenNode && SectionSigns.TakesTime(siblings[i]))
                return true;
        }
        return false;
    }

    // ───────────────────────── on the column ─────────────────────────

    private void ReportOnTheColumn(RepeatStream stream, FormDeclarationSyntax? form,
        List<SectionBarCounts.SemanticVoice> voices, IReadOnlyDictionary<string, SyntaxNode> phrases,
        Dictionary<string, SectionSigns> signs)
    {
        if (stream.NestedAt.Count == 0 && stream.LoneEndingsAt.Count == 0 && !stream.Runs.Any(r => r.Endings.Count > 0))
            return;

        // The signs the MUSIC writes, by the bar line of the column they stand on.
        var opens = new Dictionary<int, BarlineSyntax>();
        var closes = new Dictionary<int, List<BarlineSyntax>>();
        var endings = new Dictionary<int, InlineVoltaSyntax>();
        foreach (var play in stream.Plays)
        {
            if (!signs.TryGetValue(play.Section, out var written))
                signs[play.Section] = written = SectionSigns.Of(play.Section, voices, phrases);
            foreach (var (place, sign) in written.Lines)
            {
                var kind = sign.Node.BarToken.Kind;
                if (kind is SyntaxKind.RepeatStartBar or SyntaxKind.RepeatBothBar)
                    opens.TryAdd(play.FirstBar + place, sign.Node);
                if (kind is SyntaxKind.RepeatEndBar or SyntaxKind.RepeatBothBar)
                {
                    if (!closes.TryGetValue(play.FirstBar + place, out var list))
                        closes[play.FirstBar + place] = list = new();
                    list.Add(sign.Node);
                }
            }
            foreach (var ending in written.Endings)
                endings.TryAdd(play.FirstBar + ending.From, ending.Node);
        }
        string where = form?.Name is { } name ? $" (in form '{name.Text}')" : "";

        foreach (int at in stream.NestedAt)
            if (opens.TryGetValue(at, out var inner))
                Error(inner.BarToken.Span, DiagnosticCodes.NestedRepeat,
                    $"This '|:' opens a repeat while the '|:' before it is still open{where}: repeats do not nest. "
                    + "Close the first with ':|' before this one, or write ':|:' where one repeat ends and the next begins.");

        foreach (int at in stream.LoneEndingsAt)
            if (endings.TryGetValue(at, out var lone))
            {
                if (_said.Add((lone.Span.Start, DiagnosticCodes.VoltaEndingWithoutRepeat)))
                    _diagnostics.Warning(InkOf(lone), DiagnosticCodes.VoltaEndingWithoutRepeat,
                        $"The ending '[{lone.VoltaText}' stands in no repeat{where}, so its music is played once like any other: "
                        + "an ending belongs to a repeat - open one with '|:' before it and close it with ':|'.");
            }

        foreach (var run in stream.Runs)
        {
            if (run.Endings.Count == 0)
                continue;
            var written = run.Endings.Select(e => endings.GetValueOrDefault(e.From)).ToList();
            if (written.All(e => e == null))
                continue; // the form's own endings: FormDeclarationValidator's
            if (run.BodyEnd <= run.Start && opens.TryGetValue(run.Start, out var open))
                Error(open.BarToken.Span, DiagnosticCodes.EmptyRepeatBody,
                    "This repeat has no body: its first ending opens right at the '|:'. The body is what every pass "
                    + "plays before its ending - write the music the passes share before '[1.'.");
            if (run.Count is { } count)
                foreach (var (at, lines) in closes)
                    if (at > run.Start && at <= run.End)
                        foreach (var line in lines.Where(l => l.HasExplicitRepeatCount))
                            Error(line.Span, DiagnosticCodes.RepeatCountWithEndings,
                                $"A repeat with endings takes its passes from the endings' numbers: remove '*{count}' and number "
                                + "every pass instead - three passes, the first two on one ending, are '[1-2. … :| [3. … ]'.");

            var namedBy = new Dictionary<int, int>();
            bool unread = false;
            for (int k = 0; k < run.Endings.Count; k++)
            {
                var passes = run.Endings[k].Passes;
                unread |= passes.IsEmpty;
                for (int pass = 1; pass <= passes.Max; pass++)
                {
                    if (!passes.Contains(pass))
                        continue;
                    if (namedBy.TryGetValue(pass, out int first))
                    {
                        if (written[k] is { } twice)
                            Error(InkOf(twice), DiagnosticCodes.EndingPassNotNamedOnce,
                                $"Pass {pass} is already an earlier ending's{(written[first] is { } f ? $" ('[{f.VoltaText}')" : "")}: "
                                + $"each pass plays one ending - take {pass} out of one of the two numbers.");
                        break;
                    }
                    namedBy[pass] = k;
                }
            }
            if (unread)
                continue;
            int highest = namedBy.Keys.DefaultIfEmpty(0).Max();
            var missing = Enumerable.Range(1, highest).Where(p => !namedBy.ContainsKey(p)).ToList();
            if (missing.Count == 0)
                continue;
            // At the ending after the first gap (FormDeclarationValidator.ReportEndingPasses' choice).
            int after = Enumerable.Range(0, run.Endings.Count).Where(k => MinOf(run.Endings[k].Passes) > missing[0]).DefaultIfEmpty(-1).First();
            if (after < 0)
                after = Enumerable.Range(0, run.Endings.Count).First(k => run.Endings[k].Passes.Max > missing[0]);
            if (written[after] is not { } gap)
                continue;
            string named = missing.Count == 1 ? $"pass {missing[0]}" : $"passes {string.Join(", ", missing)}";
            Error(InkOf(gap), DiagnosticCodes.EndingPassNotNamedOnce,
                $"No ending plays {named} of this repeat: every pass up to the highest number ({highest}) plays one ending "
                + $"- give {named} to an ending's number ('[{missing[0]}. …]').");
        }

        static int MinOf(PassSet passes)
        {
            for (int p = 1; p <= passes.Max; p++)
                if (passes.Contains(p))
                    return p;
            return 0;
        }
    }

}
