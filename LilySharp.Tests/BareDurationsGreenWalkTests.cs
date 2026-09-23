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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The drift net for <see cref="BareDurations"/>' GREEN walk (session 519): the map it
/// builds must resolve every bare duration to the SAME red instance, the same barline
/// crossing and the same displacement that the former RED walk did — that walk is kept
/// here, verbatim, as the reference oracle — on every net book and on a book that spells
/// every arm of the fold. The same net holds <see cref="PitchedRest"/>'s two spellings
/// (red and green) to one answer on every note.
/// </summary>
/// <remarks>
/// Reference identity, not value equality: the green walk recovers its reds by position
/// descent through the parent-cached <c>GetChild</c>, so it must hand back the very
/// instances the red walk touched. ⚠️ The net must bite: it asserts that the books it
/// walked held bare durations, pitched rests, chord repetitions and scope boundaries.
/// </remarks>
public class BareDurationsGreenWalkTests
{
    /// <summary>Every arm of the fold in one book: notes, chords, q chains (displaced),
    /// rests and pitched rests (transparent), an arpeggio (breaks the run), barlines
    /// (crossing), and the scope boundaries (part, section, phrase, part block).</summary>
    private const string EveryArm = """
        part bass { clef bass }
        part other { clef treble }
        phrase lick { d'8 8 e' 8 | 8 8 }
        section A {
          bass { c'4 4 8 | 4 r4 4 | <c' e g>4 4 q 4 q' 4 q' q 4 | a'4@rest 4 | <c' e>4 <>4 4 | }
          other { e'4 4 | <d f a>8 q q' 8 | 4 8 | }
        }
        section B { bass { f'4 4 | 8 | } other { g'4 4 | } }
        section C { bass { 4 g'4 | } other { <a c'>4 q 4 | } }
        score main { staff bass staff other }
        """;

    [Fact]
    public void TheGreenWalk_ResolvesEveryBareDuration_AsTheRedWalkDid()
    {
        var failures = new List<string>();
        int books = 0, bares = 0, pitchedRests = 0, repetitions = 0;

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

            var reference = new Dictionary<BareDurationSyntax, (SyntaxNode Original, bool Crossed, int Octave)>();
            SyntaxNode? last = null;
            bool crossed = false;
            int octave = 0;
            RedThread(root, reference, ref last, ref crossed, ref octave);

            foreach (var bare in root.DescendantNodes<BareDurationSyntax>())
            {
                bares++;
                var got = (BareDurations.OriginalOf(bare), BareDurations.CrossesBarline(bare),
                    BareDurations.DisplacementOf(bare));
                var want = reference.TryGetValue(bare, out var r)
                    ? (r.Original, r.Crossed, r.Octave)
                    : ((SyntaxNode?)null, false, 0);
                if (!ReferenceEquals(got.Item1, want.Item1) || got.Item2 != want.Item2 || got.Item3 != want.Item3)
                    failures.Add($"{label} @{bare.Position}: green ({Describe(got.Item1)}, {got.Item2}, {got.Item3})"
                        + $" != red ({Describe(want.Item1)}, {want.Item2}, {want.Item3})");
            }
            foreach (var note in root.DescendantNodes<NoteSyntax>())
            {
                bool red = PitchedRest.Is(note);
                if (red) pitchedRests++;
                if (red != PitchedRest.Is(note.Green))
                    failures.Add($"{label} @{note.Position}: PitchedRest red {red} != green {!red}");
            }
            repetitions += root.DescendantNodes<ChordRepetitionSyntax>().Count();
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        Assert.True(books >= 50 && bares >= 20 && pitchedRests >= 1 && repetitions >= 5,
            $"the net did not bite: {books} books, {bares} bare durations, {pitchedRests} pitched rests, {repetitions} repetitions");
    }

    private static string? TryRead(string path)
    {
        try { return File.ReadAllText(path); } catch { return null; }
    }

    private static string Describe(SyntaxNode? n) => n is null ? "null" : $"{n.Kind}@{n.Position}";

    // The RED walk BareDurations.Thread was until session 519 — verbatim, the oracle.
    private static void RedThread(SyntaxNode node,
        Dictionary<BareDurationSyntax, (SyntaxNode Original, bool Crossed, int Octave)> map,
        ref SyntaxNode? last, ref bool crossed, ref int octave)
    {
        switch (node)
        {
            case BarlineSyntax:
                crossed = true;
                return;
            case NoteSyntax n:
                if (!PitchedRest.Is(n))
                {
                    last = n;
                    crossed = false;
                    octave = 0;
                }
                return;
            case ChordSyntax c:
                if (!c.IsEmpty)
                {
                    last = c;
                    crossed = false;
                    octave = 0;
                }
                return;
            case SlashNoteSyntax or DrumNoteSyntax:
                last = node;
                crossed = false;
                octave = 0;
                return;
            case ChordRepetitionSyntax q:
                if (ChordRepetitions.OriginalOf(q) is { } chord)
                {
                    last = chord;
                    octave = ChordRepetitions.DisplacementOf(q);
                }
                crossed = false;
                return;
            case ArpeggioSyntax:
                last = null;
                crossed = false;
                octave = 0;
                return;
            case BareDurationSyntax bare:
                if (last != null)
                    map[bare] = (last, crossed, octave);
                crossed = false;
                return;
        }
        for (int i = 0; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is not { } child || child is SyntaxTokenNode)
                continue;
            if (child is PartDeclarationSyntax or SectionDeclarationSyntax or PhraseDeclarationSyntax
                or PartBlockSyntax or ChordPartBlockSyntax)
            {
                SyntaxNode? inner = null;
                bool innerCrossed = false;
                int innerOctave = 0;
                RedThread(child, map, ref inner, ref innerCrossed, ref innerOctave);
            }
            else
            {
                RedThread(child, map, ref last, ref crossed, ref octave);
            }
        }
    }
}
