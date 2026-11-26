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

using System.Text;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Parser;

/// <summary>
/// One row of a drum steps chart, <c>hh "x.x. x.x. | xxxx x.o."</c>, read into the tokens the
/// same row would be written as in a <c>drums { }</c> line (docs/drum-steps-design.md §2).
/// </summary>
/// <remarks>
/// <para>The pattern: one character a step, a run of steps between spaces a BEAT, <c>|</c>
/// between bars. A beat's length is the chart's (a quarter unless <c>steps 4. { }</c> says
/// otherwise), so how many steps it has says how finely it is split: 1 2 4 8 evenly, 3 and 6
/// as a triplet — a dotted beat splits into 1, 3 or 6 with no triplet.</para>
/// <para>A hit lasts to the next hit of its beat, or to the beat's end (drums do not ring on,
/// so this is only how the page writes it); the steps before a beat's first hit are a rest.
/// A beat whose hits all fall on a coarser grid is written on that grid (<c>x..x..</c> is two
/// eighths, not a triplet). Empty beats are one rest each, a bar of nothing one rest, and in
/// a bar of four beats the empty halves are half rests.</para>
/// <para>Every token carries the characters it stands on as its SOURCE (or none) and what it
/// reads as as its spelling — <see cref="Syntax.InternalSyntax.SyntaxToken.Spelled"/>. The steps a
/// note or rest lasts over and the spaces are trivia of the token before them, so the tokens'
/// widths add up to the pattern's and each note sits at its own step.</para>
/// </remarks>
internal static class StepsPattern
{
    /// <summary>The time unit: a whole note is 1536 (2^9 × 3), so a 128th, a sixteenth
    /// triplet's eighth of a dotted sixteenth and every step any beat splits into are whole.</summary>
    internal const int Whole = 1536;

    /// <summary>The symbols a step may be.</summary>
    internal const string Symbols = ". x X g o f";

    internal readonly record struct Problem(int Offset, int Length, string Message, bool IsWarning = false);

    /// <summary>A token to put in the stream: its kind, what it reads as, what stands in the
    /// file for it, and the trivia after it.</summary>
    internal sealed class Piece(SyntaxKind kind, string spelling, string source)
    {
        public SyntaxKind Kind { get; } = kind;
        public string Spelling { get; } = spelling;
        public string Source { get; } = source;
        public List<(SyntaxKind Kind, StringBuilder Text)> Trailing { get; } = [];

        public void Trail(SyntaxKind kind, char c)
        {
            if (Trailing.Count > 0 && Trailing[^1].Kind == kind)
                Trailing[^1].Text.Append(c);
            else
                Trailing.Add((kind, new StringBuilder().Append(c)));
        }
    }

    internal sealed class Row
    {
        public List<Piece> Pieces { get; } = [];
        /// <summary>The beats of each bar, in order.</summary>
        public List<int> BeatsPerBar { get; } = [];
        /// <summary>Where each bar starts in the literal (for a message about a bar).</summary>
        public List<int> BarOffsets { get; } = [];
        public List<Problem> Problems { get; } = [];
    }

    private sealed record Step(char C, int Offset);

    private sealed class Group
    {
        public List<Step> Steps { get; } = [];
        public bool Bad;                    // a length no beat splits into: written as a rest
        public bool Empty => Bad || Steps.TrueForAll(s => s.C == '.');
        public bool MergedAway;             // an empty beat the rest before it already covers
        public int RestLength;              // the rest an empty beat (or its run) is written as
        public bool Tuplet;
        public int Unit;                    // a step's WRITTEN length on the reduced grid
        public int Reduce = 1;              // steps of the written grid per written step
    }

    /// <summary>Reads one row. <paramref name="literal"/> is the string token's text, quotes
    /// included; offsets in the result are into it.</summary>
    internal static Row Lower(string drum, string literal, int beat, bool dottedBeat)
    {
        var row = new Row();
        bool closed = literal.Length >= 2 && literal[^1] == '"';
        int end = closed ? literal.Length - 1 : literal.Length;

        // Bars of beats of steps.
        var bars = new List<List<Group>> { new() };
        row.BarOffsets.Add(1);
        Group? open = null;
        for (int i = 1; i < end; i++)
        {
            char c = literal[i];
            if (c == ' ' || c == '\t')
            {
                open = null;
                continue;
            }
            if (c == '|')
            {
                open = null;
                bars.Add([]);
                row.BarOffsets.Add(i + 1);
                continue;
            }
            if (open == null)
                bars[^1].Add(open = new Group());
            if (!Symbols.Contains(c))
            {
                row.Problems.Add(new Problem(i, 1,
                    $"'{c}' is not a step ({Where(drum, bars.Count, bars[^1].Count, open.Steps.Count + 1)}): "
                    + $"a step is one of {Symbols} — . strikes nothing, x strikes, X with an accent, "
                    + "g a ghost note, o the open hi-hat, f a flam"));
                c = '.';
            }
            if (c == 'o' && drum != "hh" && drum != "hho")
                row.Problems.Add(new Problem(i, 1,
                    $"'o' opens the hi-hat ({Where(drum, bars.Count, bars[^1].Count, open.Steps.Count + 1)}), "
                    + $"and this is the {drum} row: it is played as x. Write the open hi-hat on the hh row",
                    IsWarning: true));
            open.Steps.Add(new Step(c, i));
        }

        // A bar line closing the last bar leaves an empty bar after it; any other empty bar
        // is a mistake.
        if (bars.Count > 1 && bars[^1].Count == 0)
        {
            bars.RemoveAt(bars.Count - 1);
            row.BarOffsets.RemoveAt(row.BarOffsets.Count - 1);
        }
        for (int b = 0; b < bars.Count; b++)
        {
            if (bars[b].Count == 0)
                row.Problems.Add(new Problem(Math.Min(row.BarOffsets[b], literal.Length - 1), 1,
                    bars.Count == 1
                        ? $"the {drum} row has no steps: write a beat a run of steps, the beats between spaces — {drum} \"x.x. x.x. x.x. x.x.\""
                        : $"bar {b + 1} of the {drum} row has no beats: write its steps between the bar lines"));
            row.BeatsPerBar.Add(bars[b].Count);
        }

        foreach (var bar in bars)
            foreach (var g in bar)
                Plan(g, drum, bars.IndexOf(bar) + 1, bar.IndexOf(g) + 1, beat, dottedBeat, row);
        foreach (var bar in bars)
            MergeRests(bar, beat, dottedBeat);

        // The stream, in the literal's order.
        var head = new Piece(SyntaxKind.OpenBrace, "{", literal.Length > 0 ? literal[..1] : "");
        row.Pieces.Add(head);
        var stepAt = new Dictionary<int, (Group G, int Index)>();
        foreach (var bar in bars)
            foreach (var g in bar)
                for (int k = 0; k < g.Steps.Count; k++)
                    stepAt[g.Steps[k].Offset] = (g, k);

        for (int i = 1; i < end; i++)
        {
            char c = literal[i];
            if (stepAt.TryGetValue(i, out var at))
            {
                EmitStep(at.G, at.Index, c, drum, row.Pieces);
                continue;
            }
            if (c == '|')
            {
                row.Pieces.Add(new Piece(SyntaxKind.Bar, "|", "|"));
                continue;
            }
            row.Pieces[^1].Trail(SyntaxKind.WhitespaceTrivia, c);
        }
        row.Pieces.Add(new Piece(SyntaxKind.CloseBrace, "}", closed ? "\"" : ""));
        return row;
    }

    private static string Where(string drum, int bar, int beat, int step)
        => $"step {step} of beat {beat} of bar {bar} in the {drum} row";

    private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    /// <summary>How the beat is written: its grid (reduced to the coarsest one its hits sit
    /// on) and whether that grid is a triplet.</summary>
    private static void Plan(Group g, string drum, int bar, int beatNo, int beat, bool dotted, Row row)
    {
        int n = g.Steps.Count;
        bool fits = dotted ? n is 1 or 3 or 6 : n is 1 or 2 or 3 or 4 or 6 or 8;
        if (!fits)
        {
            g.Bad = true;
            g.RestLength = beat;
            row.Problems.Add(new Problem(g.Steps[0].Offset, n,
                $"beat {beatNo} of bar {bar} in the {drum} row has {n} steps: "
                + (dotted
                    ? "a dotted beat splits into 1, 3 or 6 (eighths are 3, sixteenths 6)"
                    : "a beat splits into 1, 2, 4 or 8 steps, or 3 or 6 for a triplet (sixteenths are 4)")));
            return;
        }
        if (g.Empty)
        {
            g.RestLength = beat;
            return;
        }

        int grid = n;
        for (int k = 0; k < n; k++)
            if (g.Steps[k].C != '.')
                grid = Gcd(grid, k);
        if (grid == 0)
            grid = n;
        g.Reduce = grid;
        int m = n / grid;                       // steps on the coarsest grid the hits sit on
        int step = beat / m;
        g.Tuplet = !dotted && !IsPowerOfTwo(m);
        g.Unit = g.Tuplet ? step * 3 / 2 : step;
    }

    /// <summary>Empty beats in a row become longer rests: a bar of nothing one rest, and in a
    /// bar of four beats an empty half (beats 1–2 or 3–4) a half-bar rest; three beats' run of
    /// two empty ones one rest.</summary>
    private static void MergeRests(List<Group> bar, int beat, bool dotted)
    {
        if (bar.Count == 0)
            return;
        if (bar.TrueForAll(g => g.Empty) && Representable(beat * bar.Count) is { } whole)
        {
            bar[0].RestLength = whole;
            for (int i = 1; i < bar.Count; i++)
                bar[i].MergedAway = true;
            return;
        }
        if (dotted)
            return;
        if (bar.Count == 4)
        {
            for (int i = 0; i < 4; i += 2)
                if (bar[i].Empty && bar[i + 1].Empty)
                {
                    bar[i].RestLength = beat * 2;
                    bar[i + 1].MergedAway = true;
                }
        }
        else if (bar.Count == 3)
        {
            for (int i = 0; i < 2; i++)
                if (bar[i].Empty && bar[i + 1].Empty && !bar[i].MergedAway)
                {
                    bar[i].RestLength = beat * 2;
                    bar[i + 1].MergedAway = true;
                    break;
                }
        }
    }

    /// <summary>The length as one written value (plain or dotted), or null.</summary>
    private static int? Representable(int length)
    {
        for (int d = 1; d <= 128; d *= 2)
        {
            int b = Whole / d;
            if (b == length || b * 3 / 2 == length)
                return length;
        }
        return null;
    }

    /// <summary>A length split into written values, longest first (plain or dotted).</summary>
    private static List<(int Denominator, int Dots)> Split(int length)
    {
        var values = new List<(int, int)>();
        while (length > 0)
        {
            (int D, int Dots, int V) best = (0, 0, 0);
            for (int d = 1; d <= 128; d *= 2)
            {
                int b = Whole / d;
                if (b * 3 / 2 <= length && b * 3 / 2 > best.V && b % 2 == 0)
                    best = (d, 1, b * 3 / 2);
                if (b <= length && b > best.V)
                    best = (d, 0, b);
            }
            if (best.V == 0)
                break;
            values.Add((best.D, best.Dots));
            length -= best.V;
        }
        return values;
    }

    private static void AddValue(List<Piece> pieces, (int Denominator, int Dots) value)
    {
        pieces.Add(new Piece(SyntaxKind.IntegerLiteral, value.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture), ""));
        for (int i = 0; i < value.Dots; i++)
            pieces.Add(new Piece(SyntaxKind.Dot, ".", ""));
    }

    /// <summary>The tokens a step starts, or its character as trivia of the token before.</summary>
    private static void EmitStep(Group g, int index, char written, string drum, List<Piece> pieces)
    {
        string source = written.ToString();
        if (g.Empty)
        {
            if (index == 0 && !g.MergedAway)
            {
                pieces.Add(new Piece(SyntaxKind.RestR, "r", source));
                var values = Split(g.RestLength);
                AddValue(pieces, values[0]);
                for (int i = 1; i < values.Count; i++)
                {
                    pieces.Add(new Piece(SyntaxKind.RestR, "r", ""));
                    AddValue(pieces, values[i]);
                }
            }
            else
                pieces[^1].Trail(SyntaxKind.StepsHoldTrivia, written);
            return;
        }

        if (index == 0 && g.Tuplet)
        {
            pieces.Add(new Piece(SyntaxKind.TupletKeyword, "tuplet", ""));
            pieces.Add(new Piece(SyntaxKind.IntegerLiteral, "3", ""));
            pieces.Add(new Piece(SyntaxKind.Slash, "/", ""));
            pieces.Add(new Piece(SyntaxKind.IntegerLiteral, "2", ""));
            pieces.Add(new Piece(SyntaxKind.OpenBrace, "{", ""));
        }

        char c = g.Steps[index].C;
        int firstHit = g.Steps.FindIndex(s => s.C != '.');
        if (index < firstHit)
        {
            // The steps before the beat's first hit: one rest from the first of them.
            if (index == 0)
            {
                pieces.Add(new Piece(SyntaxKind.RestR, "r", source));
                var values = Split(firstHit / g.Reduce * g.Unit);
                AddValue(pieces, values[0]);
                for (int i = 1; i < values.Count; i++)
                {
                    pieces.Add(new Piece(SyntaxKind.RestR, "r", ""));
                    AddValue(pieces, values[i]);
                }
            }
            else
                pieces[^1].Trail(SyntaxKind.StepsHoldTrivia, written);
        }
        else if (c == '.')
            pieces[^1].Trail(SyntaxKind.StepsHoldTrivia, written);
        else
        {
            int next = g.Steps.FindIndex(index + 1, s => s.C != '.');
            if (next < 0)
                next = g.Steps.Count;
            int length = (next - index) / g.Reduce * g.Unit;
            string name = c == 'o' && drum == "hh" ? "hho" : drum;
            if (c == 'f')
            {
                pieces.Add(new Piece(SyntaxKind.AcciaccaturaKeyword, "acciaccatura", ""));
                pieces.Add(new Piece(SyntaxKind.OpenBrace, "{", ""));
                pieces.Add(new Piece(SyntaxKind.Identifier, name, ""));
                pieces.Add(new Piece(SyntaxKind.IntegerLiteral, "16", ""));
                pieces.Add(new Piece(SyntaxKind.CloseBrace, "}", ""));
            }
            pieces.Add(new Piece(SyntaxKind.Identifier, name, source));
            var values = Split(length);
            AddValue(pieces, values[0]);
            if (c is 'X' or 'g')
            {
                pieces.Add(new Piece(SyntaxKind.At, "@", ""));
                pieces.Add(new Piece(SyntaxKind.Identifier, c == 'X' ? "accent" : "parenthesize", ""));
            }
            for (int i = 1; i < values.Count; i++)
            {
                pieces.Add(new Piece(SyntaxKind.RestR, "r", ""));
                AddValue(pieces, values[i]);
            }
        }

        if (index == g.Steps.Count - 1 && g.Tuplet)
            pieces.Add(new Piece(SyntaxKind.CloseBrace, "}", ""));
    }
}
