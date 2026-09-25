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
using System.IO;
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The drift net for <see cref="ChordRepetitions"/>' GREEN walk (session 603), the twin of
/// <see cref="BareDurationsGreenWalkTests"/>: every <c>q</c> must resolve to the SAME red
/// chord instance and the same displacement the former RED walk gave — kept here verbatim as
/// the oracle — on every net book and on a book that spells every arm (plain and displaced
/// q chains, an empty chord, and each scope boundary). ⚠️ The net must bite: it asserts the
/// books it walked held repetitions, resolved ones among them.
/// </summary>
public class ChordRepetitionsGreenWalkTests
{
    private const string EveryArm = """
        part bass { clef bass }
        part other { clef treble }
        phrase lick { <d' f'>8 q q' q | }
        section A {
          bass { <c e g>4 q q' q | <>4 q <e g>4 q | }
          other { q4 <d f a>8 q q' 8 | }
        }
        section B { bass { q4 <f a>4 q q, | } other { lick | } }
        score main { staff bass staff other }
        """;

    [Fact]
    public void TheGreenWalk_ResolvesEveryRepetition_AsTheRedWalkDid()
    {
        var failures = new List<string>();
        int books = 0, repetitions = 0, resolved = 0;

        var sources = CollectResumeTests.NetBooks()
            .Select(p => (Label: Path.GetFileName(p), Text: TryRead(p)))
            .Where(s => s.Text is not null)
            .Append(("EveryArm", EveryArm));

        foreach (var (label, text) in sources)
        {
            SyntaxTree tree;
            try { tree = SyntaxTree.Parse(text!); }
            catch { continue; }
            books++;
            var root = tree.GetRoot();

            var reference = new Dictionary<ChordRepetitionSyntax, (ChordSyntax Chord, int Octave)>();
            ChordSyntax? chord = null;
            int octave = 0;
            RedThread(root, reference, ref chord, ref octave);

            foreach (var q in root.DescendantNodes<ChordRepetitionSyntax>())
            {
                repetitions++;
                var got = (ChordRepetitions.OriginalOf(q), ChordRepetitions.DisplacementOf(q));
                var want = reference.TryGetValue(q, out var r) ? (r.Chord, r.Octave) : ((ChordSyntax?)null, 0);
                if (want.Item1 != null)
                    resolved++;
                if (!ReferenceEquals(got.Item1, want.Item1) || got.Item2 != want.Item2)
                    failures.Add($"{label} @{q.Position}: green ({Describe(got.Item1)}, {got.Item2})"
                        + $" != red ({Describe(want.Item1)}, {want.Item2})");
            }
            var originals = ChordRepetitions.OriginalsOf(root);
            var wantOriginals = reference.Values.Select(v => v.Chord).ToHashSet();
            if (!originals.SetEquals(wantOriginals))
                failures.Add($"{label}: originals {originals.Count} != red {wantOriginals.Count}");
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        Assert.True(books >= 50 && repetitions >= 10 && resolved >= 8,
            $"the net did not bite: {books} books, {repetitions} repetitions, {resolved} resolved");
    }

    private static string? TryRead(string path)
    {
        try { return File.ReadAllText(path); } catch { return null; }
    }

    private static string Describe(SyntaxNode? n) => n is null ? "null" : $"{n.Kind}@{n.Position}";

    // The RED walk ChordRepetitions.Thread was until session 603 — verbatim, the oracle.
    private static void RedThread(SyntaxNode node,
        Dictionary<ChordRepetitionSyntax, (ChordSyntax Chord, int Octave)> map,
        ref ChordSyntax? chord, ref int octave)
    {
        switch (node)
        {
            case ChordSyntax c:
                chord = c;
                octave = 0;
                return;
            case ChordRepetitionSyntax q:
                if (chord != null)
                {
                    octave += q.OctaveOffset;
                    map[q] = (chord, octave);
                }
                return;
        }
        for (int i = 0; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is not { } child || child is SyntaxTokenNode)
                continue;
            if (child is PartDeclarationSyntax or SectionDeclarationSyntax or PhraseDeclarationSyntax
                or PartBlockSyntax or ChordPartBlockSyntax)
            {
                ChordSyntax? inner = null;
                int innerOctave = 0;
                RedThread(child, map, ref inner, ref innerOctave);
            }
            else
                RedThread(child, map, ref chord, ref octave);
        }
    }
}
