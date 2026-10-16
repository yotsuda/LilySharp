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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// <c>R1*5/4</c> — LilyPond's rest run with a FRACTION for a multiplier — is two things in
/// Lily#: the run <c>R1*5</c> and the slash note <c>/4</c> (rhythm notation), and the page drew
/// the two without a word until the slash overflowed its bar (LYS2002, which does not name the
/// cause). This names it (<see cref="DiagnosticCodes.RestRunFraction"/>) and says the spelling
/// Lily# has for the same silence — one duration and a count, <c>R4*5</c> for one 5/4 bar —
/// which the editor offers to write (LilySharpLanguageServer.Editing, RestRunFractionAction).
/// </summary>
/// <remarks>
/// <para>
/// Owner's decision 2026-10-17: the fraction is NOT added to the grammar — one spelling per
/// instruction (the rule that retired <c>$</c>, 2026-09-02; "fix the example, not the
/// grammar", 2026-10-14), and <c>/</c> is the slash note's. The signal is adjacency: a slash
/// note glued to a run's count with no space between (<c>R1*5 /4</c> is a run and a slash,
/// as written).
/// </para>
/// <para>
/// The suggested spelling is the run's total length — dur × N / M in whole notes — written in
/// the unit of the file's first meter when it divides evenly (<c>R4*5</c>, <c>R4*10</c>), else
/// as the reduced fraction (<c>R8*3</c>); a length no power-of-two unit writes (<c>R1*2/3</c>)
/// gets no suggestion, only the reading. LILYSHARP-OWN: LilyPond reads <c>*5/4</c> as a
/// duration scale.
/// </para>
/// </remarks>
internal sealed class RestRunFractionValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        int meterUnit = root.DescendantNodes<TimeSignatureSyntax>()
            .Select(t => t.Denominator is { } d && int.TryParse(d.Text, out int u) ? u : 0)
            .FirstOrDefault(u => u > 0);

        foreach (var rest in root.DescendantNodes<RestSyntax>())
        {
            if (rest.GetChild(3) is not SyntaxTokenNode count || !int.TryParse(count.Text, out int n) || n < 1)
                continue;
            if (NextSibling(rest) is not SlashNoteSyntax slash || slash.SourceStart != count.Span.End
                || slash.Duration is not { } fraction || fraction.DotCount != 0 || fraction.Value < 1)
                continue;

            string written = rest.RestText + (rest.Duration is { } dur ? DurationText(dur) : "") + "*" + count.Text;
            string slashText = "/" + fraction.Value;
            string? suggested = rest.Duration is { } d ? Suggest(d.Value, d.DotCount, n, fraction.Value, meterUnit) : null;
            string how = suggested != null
                ? $"Lily# writes a run as one duration and a count: write '{suggested}'."
                : "Lily# writes a run as one duration and a count (R4*5 is one 5/4 bar).";
            _diagnostics.Error(new TextSpan(rest.SourceStart, fraction.Span.End - rest.SourceStart),
                DiagnosticCodes.RestRunFraction,
                $"'{written}{slashText}' is read as two things: the rest run '{written}' and the slash note "
                + $"'{slashText}' (a run takes no fraction). {how}");
        }
    }

    private static string DurationText(DurationSyntax duration)
        => duration.Value + new string('.', duration.DotCount);

    /// <summary>The run <c>R&lt;unit&gt;*&lt;count&gt;</c> of the same length as
    /// <c>R&lt;value&gt;&lt;dots&gt;*n/m</c>: in the meter's unit when that divides it, else the
    /// reduced fraction's power-of-two unit; null when no unit writes it.</summary>
    internal static string? Suggest(int value, int dots, int n, int m, int meterUnit)
    {
        // dur with dots = (2^(dots+1) − 1) / (value · 2^dots) whole notes.
        long num = ((1L << (dots + 1)) - 1) * n;
        long den = (long)value * (1L << dots) * m;
        long g = Gcd(num, den);
        num /= g;
        den /= g;
        if (meterUnit > 0 && (num * meterUnit) % den == 0)
            return $"R{meterUnit}*{num * meterUnit / den}";
        if (den <= 128 && (den & (den - 1)) == 0)
            return $"R{den}*{num}";
        return null;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a == 0 ? 1 : a;
    }

    /// <summary>The node written right after <paramref name="node"/> under the same parent, or null.</summary>
    private static SyntaxNode? NextSibling(SyntaxNode node)
    {
        if (node.Parent is not { } parent)
            return null;
        bool seen = false;
        for (int i = 0; i < parent.SlotCount; i++)
        {
            if (parent.GetChild(i) is not { } child)
                continue;
            if (seen)
                return child;
            if (child.SourceStart == node.SourceStart && child.GetType() == node.GetType())
                seen = true;
        }
        return null;
    }
}
