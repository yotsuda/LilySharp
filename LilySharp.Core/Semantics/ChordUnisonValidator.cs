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
/// LYS1064: one pitch written twice in a <c>&lt;…&gt;</c> chord (owner's decision 2026-11-03: a
/// note of a triad dragged onto another makes <c>&lt;b' a, b&gt;</c>, where the marks hide that the
/// two b's are one pitch). Two members on two named strings are a unison a player means, and pass.
/// </summary>
/// <remarks>
/// Marks do not say it, a chord's members hanging on its first (GRAMMAR, the anchor), so the
/// compiler's resolved pitches do. They cost a collect and this runs after every settled keystroke:
/// only a chord with one spelling twice asks.
/// </remarks>
internal sealed class ChordUnisonValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var candidates = new List<List<(PitchSyntax Pitch, int? String)>>();
        foreach (var chord in tree.GetNodes<ChordSyntax>())
        {
            var members = MembersOf(chord);
            if (members.GroupBy(m => m.Pitch.PitchToken.Text).Any(g => g.Count() > 1))
                candidates.Add(members);
        }
        if (candidates.Count == 0)
            return;

        var resolved = new Dictionary<int, string>();
        string source = tree.Text;
        foreach (var e in ResolvedPitches.ForFile(tree) ?? [])
        {
            int p = e.Position;
            while (p < source.Length && char.IsWhiteSpace(source[p]))
                p++;
            resolved.TryAdd(p, e.Pitch);
        }

        foreach (var members in candidates)
        {
            var seen = new Dictionary<string, int?>();
            foreach (var (pitch, onString) in members)
            {
                if (!resolved.TryGetValue(pitch.SourceStart, out var heard))
                    continue;
                if (!seen.TryGetValue(heard, out var first))
                {
                    seen[heard] = onString;
                    continue;
                }
                if (first != null && onString != null && first != onString)
                    continue;
                // ASCII only: these strings reach legacy-codepage consoles through the CLI.
                _diagnostics.Warning(new TextSpan(pitch.SourceStart, pitch.Width),
                    DiagnosticCodes.ChordSamePitchTwice,
                    $"this chord has the same note twice ({heard}), which sounds as one - remove one "
                    + "of them, or give each its own string (\\N) if it is a unison on two strings");
            }
        }
    }

    /// <summary>A chord's pitches in written order, each with the <c>\N</c> written after it and
    /// before the next pitch.</summary>
    private static List<(PitchSyntax Pitch, int? String)> MembersOf(ChordSyntax chord)
    {
        var pitches = chord.DescendantNodes<PitchSyntax>().Where(p => p.BaseName != '\0').OrderBy(p => p.SourceStart).ToList();
        var strings = chord.DescendantNodes<StringNumberAnnotationSyntax>().ToList();
        var members = new List<(PitchSyntax, int?)>(pitches.Count);
        for (int i = 0; i < pitches.Count; i++)
        {
            int from = pitches[i].SourceStart, to = i + 1 < pitches.Count ? pitches[i + 1].SourceStart : int.MaxValue;
            members.Add((pitches[i], strings.FirstOrDefault(s => s.SourceStart > from && s.SourceStart < to)?.StringNumber));
        }
        return members;
    }
}
