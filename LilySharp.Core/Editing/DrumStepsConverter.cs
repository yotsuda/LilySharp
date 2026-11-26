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

using System.Globalization;
using System.Text;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Rewrites a drum chart between its two spellings (Orecchiante's request 24 §4,
/// docs/drum-steps-design.md §3): a <c>drums { }</c> chart of lines as a <c>steps { }</c> chart
/// of rows — a row a drum, a character a step — and a steps chart as the <c>drums { }</c> lines it
/// stands for. The editor offers it on a chart (Ctrl+.).
/// </summary>
/// <remarks>
/// <para><b>To steps.</b> The hits of every line (a phrase line's body too) are gathered at
/// their times, a row for each drum in the order the drums first appear (<c>hho</c> on the
/// <c>hh</c> row as <c>o</c>; <c>@accent</c> is <c>X</c>, <c>@parenthesize</c> <c>g</c>, an
/// <c>acciaccatura</c> of the same drum before its hit <c>f</c>). The bars are the first line's.
/// The beat is the meter's — read where the chart stands: its section's <c>time</c> before it,
/// else the file's — a dotted one (<c>steps 4.</c>) in a compound meter. Each beat is split as
/// finely as its hits need, the same in every row so the rows line up, and as one grid for the
/// whole chart when one grid fits every beat. A hit's length is not kept: in steps it lasts to the
/// next hit of its beat (drums do not ring on; only the page's spelling changes).</para>
/// <para>A chart that steps cannot write is not offered: a line with <c>up</c>/<c>down</c>, a
/// mark other than an accent or a ghost (a dynamic, a tremolo…), a pitched note, a hit off every
/// grid a beat splits into, lines whose bar lines disagree.</para>
/// <para><b>To lines.</b> Each row's notes as the parser read them (Parser.Steps), written out —
/// so the page and the sound are the same.</para>
/// </remarks>
public static class DrumStepsConverter
{
    /// <summary>A rewrite of the chart <c>[Start, End)</c> to <see cref="NewText"/>.</summary>
    public sealed record Rewrite(int Start, int End, string NewText, string Title);

    /// <summary>The title of the rewrite to steps.</summary>
    public const string ToStepsTitle = "Convert to Drum Steps steps { }";

    /// <summary>The title of the rewrite to lines.</summary>
    public const string ToLinesTitle = "Convert to Drum Lines drums { }";

    /// <summary>The rewrite of the drum chart at <paramref name="offset"/>, or null when there is
    /// none there, or it cannot be written the other way.</summary>
    public static Rewrite? At(SyntaxTree tree, int offset)
    {
        var chart = tree.GetRoot().DescendantNodes().OfType<ParallelExpressionSyntax>()
            .Where(p => p.IsDrums && p.Span.Start <= offset && offset <= p.Span.End)
            .OrderBy(p => p.Span.Length).FirstOrDefault();
        if (chart == null)
            return null;
        string source = tree.GetRoot().ToFullString();
        string indent = IndentOf(source, chart.Span.Start);
        bool multiline = source.AsSpan(chart.Span.Start, chart.Span.Length).Contains('\n');
        if (chart.IsSteps)
            return new Rewrite(chart.Span.Start, chart.Span.End, ToLines(chart, multiline, indent), ToLinesTitle);
        return ToSteps(chart, tree.GetRoot(), multiline, indent) is { } steps
            ? new Rewrite(chart.Span.Start, chart.Span.End, steps, ToStepsTitle)
            : null;
    }

    private static string IndentOf(string source, int at)
    {
        int lineStart = source.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        int i = lineStart;
        while (i < source.Length && source[i] is ' ' or '\t')
            i++;
        return source[lineStart..i];
    }

    // ---- steps → lines ------------------------------------------------------------------------

    private static string ToLines(ParallelExpressionSyntax chart, bool multiline, string indent)
    {
        var lines = chart.DrumLines.Select(l => "{ " + Items(l.Line.Items) + " }").ToList();
        return multiline
            ? "drums {\n" + string.Concat(lines.Select(l => $"{indent}  {l}\n")) + indent + "}"
            : "drums { " + string.Join(" ", lines) + " }";
    }

    private static string Items(IEnumerable<SyntaxNode> items)
        => string.Join(" ", items.Select(Item).Where(s => s.Length > 0));

    private static string Item(SyntaxNode item) => item switch
    {
        DrumNoteSyntax n => n.DrumName + Value(n.Duration) + Marks(n.Articulations),
        RestSyntax r => r.RestText + Value(r.Duration),
        TupletExpressionSyntax t => $"tuplet {t.TupletRatio}/{t.BaseDivision} {{ {Items(t.Body.Items)} }}",
        GraceExpressionSyntax g => $"{g.GraceKeyword.Text} {{ {Items(g.Body.Items)} }}",
        BarlineSyntax b => b.BarText,
        _ => "",
    };

    private static string Value(DurationSyntax? d)
        => d == null ? "" : d.Value.ToString(CultureInfo.InvariantCulture) + new string('.', d.DotCount);

    private static string Marks(ChildNodeList marks)
        => string.Concat(marks.OfType<ArticulationSyntax>().Select(a => "@" + a.Name));

    // ---- lines → steps ------------------------------------------------------------------------

    private readonly record struct Hit(Fraction At, string Drum, char Symbol);

    /// <summary>Why a chart is not written as steps: thrown inside the gather, caught once.</summary>
    private sealed class NotSteps : Exception;

    private static string? ToSteps(ParallelExpressionSyntax chart, SyntaxNode root, bool multiline, string indent)
    {
        var phrases = root.ChildNodes().OfType<PhraseDeclarationSyntax>()
            .GroupBy(p => p.Name.Text).ToDictionary(g => g.Key, g => g.First().Body);
        var hits = new List<Hit>();
        List<Fraction>? barLines = null;
        var end = Fraction.Zero;
        try
        {
            foreach (var (line, down) in chart.DrumLines)
            {
                if (down != null)
                    throw new NotSteps();
                var items = LineItems(line, phrases);
                var t = Fraction.Zero;
                var running = Fraction.Quarter;
                var bars = new List<Fraction>();
                string[]? last = null;
                char? flam = null;
                Gather(items, ref t, ref running, new Fraction(1, 1), hits, bars, ref last, ref flam);
                if (flam != null)
                    throw new NotSteps();
                if (bars.Count > 0 && bars[^1] == t)
                    bars.RemoveAt(bars.Count - 1);     // a closing bar line
                if (barLines == null)
                    barLines = bars;
                else if (!bars.SequenceEqual(barLines))
                    throw new NotSteps();
                if (t > end)
                    end = t;
            }
        }
        catch (NotSteps)
        {
            return null;
        }
        if (hits.Count == 0 || end == Fraction.Zero)
            return null;

        // The bars, and the beat the meter counts.
        var starts = new List<Fraction> { Fraction.Zero };
        starts.AddRange(barLines ?? []);
        var (beatWord, beat) = BeatAt(chart, root);
        var barBeats = new List<int>();
        for (int b = 0; b < starts.Count; b++)
        {
            var length = (b + 1 < starts.Count ? starts[b + 1] : end) - starts[b];
            var n = length / beat;
            if (n.Denominator != 1 || n.Numerator < 1)
                return null;
            barBeats.Add(n.Numerator);
        }

        // Every beat's grid: one for the whole chart when one fits, else each beat its own.
        bool dotted = beatWord.EndsWith('.');
        int[] grids = dotted ? [1, 3, 6] : [1, 2, 3, 4, 6, 8];
        var beatsAt = new List<Fraction>();
        for (int b = 0; b < starts.Count; b++)
            for (int k = 0; k < barBeats[b]; k++)
                beatsAt.Add(starts[b] + beat * new Fraction(k, 1));
        int? Fits(int grid, Fraction from) => hits.Where(h => h.At >= from && h.At < from + beat)
            .All(h => ((h.At - from) / beat * new Fraction(grid, 1)).Denominator == 1) ? grid : null;
        int? whole = grids.FirstOrDefault(g => beatsAt.All(at => Fits(g, at) != null));
        var gridOf = new List<int>();
        foreach (var at in beatsAt)
        {
            int grid = whole is > 0 ? whole.Value : grids.FirstOrDefault(g => Fits(g, at) != null);
            if (grid == 0)
                return null;
            gridOf.Add(grid);
        }

        var rows = new List<string>();
        foreach (var h in hits)
        {
            string row = RowOf(h.Drum);
            if (!rows.Contains(row))
                rows.Add(row);
        }
        int pad = rows.Max(r => r.Length);
        var rowTexts = new List<string>();
        foreach (var row in rows)
        {
            var text = new StringBuilder();
            int beatIndex = 0;
            for (int b = 0; b < starts.Count; b++)
            {
                if (b > 0)
                    text.Append(" | ");
                for (int k = 0; k < barBeats[b]; k++, beatIndex++)
                {
                    if (k > 0)
                        text.Append(' ');
                    var from = beatsAt[beatIndex];
                    int grid = gridOf[beatIndex];
                    var steps = new char[grid];
                    Array.Fill(steps, '.');
                    foreach (var h in hits.Where(h => RowOf(h.Drum) == row && h.At >= from && h.At < from + beat))
                    {
                        int i = ((h.At - from) / beat * new Fraction(grid, 1)).Numerator;
                        steps[i] = Stronger(steps[i], h.Symbol);
                    }
                    text.Append(steps);
                }
            }
            rowTexts.Add($"{row.PadRight(pad)} \"{text}\"");
        }

        string head = beatWord.Length == 0 ? "steps" : "steps " + beatWord;
        return multiline
            ? head + " {\n" + string.Concat(rowTexts.Select(r => $"{indent}  {r}\n")) + indent + "}"
            : head + " { " + string.Join(" ", rowTexts) + " }";
    }

    private static string RowOf(string drum) => drum == "hho" ? "hh" : drum;

    /// <summary>Two hits of one drum at one step (two lines, or a chord and a line): the one
    /// that says more.</summary>
    private static char Stronger(char a, char b)
    {
        const string order = ".xgofX";
        return order.IndexOf(b) > order.IndexOf(a) ? b : a;
    }

    private static IEnumerable<SyntaxNode> LineItems(MusicBlockSyntax line, Dictionary<string, MusicBlockSyntax> phrases)
    {
        var items = line.Items.ToList();
        // A phrase named as a line stands in a block of its own (Parser.Drums).
        if (items is [VariableReferenceSyntax reference])
            return phrases.TryGetValue(reference.Name.Text, out var body) ? body.Items : throw new NotSteps();
        return items;
    }

    private static void Gather(IEnumerable<SyntaxNode> items, ref Fraction t, ref Fraction running, Fraction scale,
        List<Hit> hits, List<Fraction> bars, ref string[]? last, ref char? flam)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case BarlineSyntax:
                    bars.Add(t);
                    break;
                case DrumNoteSyntax n:
                {
                    char symbol = SymbolOf(n.Articulations, n.DrumName);
                    if (n.Tremolo != null)
                        throw new NotSteps();
                    hits.Add(new Hit(t, n.DrumName, flam ?? symbol));
                    flam = null;
                    last = [n.DrumName];
                    running = n.Duration?.ToFraction() ?? running;
                    t += running * scale;
                    break;
                }
                case ChordSyntax c when !c.Pitches.Any() && c.DrumNames.Any():
                {
                    // A mark on the chord is every member's; a member's own mark is its own.
                    char chordMark = SymbolOf(c.Articulations, null);
                    foreach (var member in c.DrumNames)
                    {
                        char own = SymbolOf(member.Articulations, member.DrumName);
                        hits.Add(new Hit(t, member.DrumName, own != 'x' ? own : chordMark));
                    }
                    last = [.. c.DrumNames.Select(m => m.DrumName)];
                    running = c.Duration?.ToFraction() ?? running;
                    t += running * scale;
                    break;
                }
                case BareDurationSyntax bare when last != null:
                    if (bare.Tremolo != null || bare.Articulations.Any())
                        throw new NotSteps();
                    foreach (var drum in last)
                        hits.Add(new Hit(t, drum, drum == "hho" ? 'o' : 'x'));
                    running = bare.Duration.ToFraction();
                    t += running * scale;
                    break;
                case RestSyntax { IsMultiMeasure: false } r when !r.Articulations.Any():
                    running = r.Duration?.ToFraction() ?? running;
                    t += running * scale;
                    break;
                case TupletExpressionSyntax tuplet:
                    var inner = scale * new Fraction(tuplet.BaseDivision, tuplet.TupletRatio);
                    Gather(tuplet.Body.Items, ref t, ref running, inner, hits, bars, ref last, ref flam);
                    break;
                case GraceExpressionSyntax { IsAcciaccatura: true } grace
                    when grace.Body.Items.ToList() is [DrumNoteSyntax g] && !g.Articulations.Any() && flam == null:
                    flam = 'f';
                    break;
                default:
                    throw new NotSteps();
            }
        }
    }

    /// <summary>The step a hit is: <c>X</c> accented, <c>g</c> a ghost, <c>o</c> the open
    /// hi-hat, else <c>x</c>. Any other mark is not a step.</summary>
    private static char SymbolOf(ChildNodeList marks, string? drum)
    {
        char symbol = drum == "hho" ? 'o' : 'x';
        foreach (var mark in marks)
        {
            symbol = mark switch
            {
                ArticulationSyntax { Name: "accent" } when symbol == 'x' => 'X',
                ArticulationSyntax { Name: "parenthesize" } when symbol == 'x' => 'g',
                _ => throw new NotSteps(),
            };
        }
        return symbol;
    }

    /// <summary>The beat the meter counts where the chart stands, as the word after
    /// <c>steps</c> ("" for a quarter) and its length.</summary>
    private static (string Word, Fraction Length) BeatAt(ParallelExpressionSyntax chart, SyntaxNode root)
    {
        TimeSignatureSyntax? time = null;
        SectionDeclarationSyntax? section = null;
        for (var up = chart.Parent; up != null && section == null; up = up.Parent)
            section = up as SectionDeclarationSyntax;
        if (section != null)
            time = section.DescendantNodes().OfType<TimeSignatureSyntax>()
                .LastOrDefault(t => t.Span.End <= chart.Span.Start);
        time ??= root.ChildNodes().OfType<TimeSignatureSyntax>().LastOrDefault(t => t.Span.End <= chart.Span.Start)
              ?? root.ChildNodes().OfType<TimeSignatureSyntax>().FirstOrDefault();
        int beats = 4, beatType = 4;
        if (time is { IsSenzaMisura: false, Denominator: { } d } && int.TryParse(d.Text, out int dt))
            (beats, beatType) = (time.Beats, dt);
        if (beats % 3 == 0 && beats > 3 && beatType >= 4)
            return ($"{beatType / 2}.", new Fraction(3, beatType));
        return (beatType == 4 ? "" : beatType.ToString(CultureInfo.InvariantCulture), new Fraction(1, beatType));
    }
}
