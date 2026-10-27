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
using System.IO;
using System.Linq;
using System.Text;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The instrument ranges the AI is told (docs/GRAMMAR_FOR_LLM.md, the system prompt of Ask AI and
/// Compose with AI): generated from <see cref="InstrumentDefaults.SoundingRange"/> — the table
/// LYS1052 warns by — so the two cannot drift. Owner's report 2026-10-27: asked to add guitar
/// diagrams, the AI wrote a guitar part down to C2 and the candidate's head filled with "below the
/// range of the Guitar (E2-E6)". Regenerate with <c>LILYSHARP_UPDATE_DOCS=1</c>.
/// </summary>
/// <remarks>
/// The written ends are FOUND by the compiler, not worked out: in a part of the instrument, in
/// absolute octaves, the octave at which the two ends are in range and a semitone past each is
/// warned. Worked out from the preset's octave and transposition alone, the guitar came out an
/// octave off (measured: its notation sounds an octave under it).
/// </remarks>
[Trait("Category", "Unit")]
public class InstrumentRangePromptTests
{
    private const string Relative = "docs/GRAMMAR_FOR_LLM.md";
    private const string Begin = "<!-- BEGIN instrument ranges: generated from InstrumentDefaults.SoundingRange by InstrumentRangePromptTests (LILYSHARP_UPDATE_DOCS=1) -->";
    private const string End = "<!-- END instrument ranges -->";

    private static readonly string[] Names = ["c", "cis", "d", "ees", "e", "f", "fis", "g", "aes", "a", "bes", "b"];
    private static readonly string[] Letters = ["C", "C♯", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B"];

    private static string Sounding(int midi) => Letters[midi % 12] + (midi / 12 - 1);

    /// <summary>MIDI pitch <paramref name="midi"/> as an absolute-octave token, bare letters in
    /// octave <paramref name="baseOctave"/>.</summary>
    private static string Token(int midi, int baseOctave)
    {
        int octave = midi / 12 - 1 - baseOctave;
        return Names[midi % 12] + (octave > 0 ? new string('\'', octave) : new string(',', -octave));
    }

    private static int Warnings(string preset, string low, string high)
        => SemanticValidation.Run(SyntaxTree.Parse(
                $"octave absolute\ntime 2/4\npart p {{ instrument {preset} }}\nsection A {{\n  p {{ {low}4 {high}4 | }}\n}}\nform {{ A }}\nscore {{ staff p }}\n"))
            .Count(d => d.Code == DiagnosticCodes.NoteOutsideInstrumentRange);

    /// <summary>The range's ends as written in a part of <paramref name="preset"/>, in absolute
    /// octaves: the reading of the compiler (see the remarks).</summary>
    internal static (string Low, string High) WrittenEnds(string preset)
    {
        var (low, high) = InstrumentDefaults.SoundingRange(preset)!.Value;
        int shift = InstrumentDefaults.GetTransposition(preset);
        for (int baseOctave = 0; baseOctave <= 8; baseOctave++)
        {
            string lo = Token(low - shift, baseOctave), hi = Token(high - shift, baseOctave);
            if (Warnings(preset, lo, hi) == 0
                && Warnings(preset, Token(low - shift - 1, baseOctave), Token(high - shift + 1, baseOctave)) == 2)
                return (lo, hi);
        }
        throw new InvalidOperationException($"no octave writes the range of {preset}");
    }

    internal static string Block(string newline)
    {
        var sb = new StringBuilder();
        sb.Append(Begin).Append(newline);
        sb.Append("| instrument | sounds | write (absolute octaves) |").Append(newline);
        sb.Append("|---|---|---|").Append(newline);
        foreach (var preset in InstrumentDefaults.KnownInstruments)
        {
            if (InstrumentDefaults.SoundingRange(preset) is not { } range)
                continue;
            var (lo, hi) = WrittenEnds(preset);
            sb.Append($"| `{preset}` | {Sounding(range.Low)}–{Sounding(range.High)} | `{lo}` – `{hi}` |").Append(newline);
        }
        sb.Append(End);
        return sb.ToString();
    }

    [Fact]
    public void TheGrammarForTheAi_ListsEveryRangedInstrument_AsTheCompilerReadsIt()
    {
        var path = Path.Combine(CollectResumeTests.FindRepoRoot(), Relative.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(path);
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        int from = text.IndexOf(Begin, StringComparison.Ordinal), to = text.IndexOf(End, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, $"{Relative} has no generated instrument ranges block");
        string now = text[from..(to + End.Length)];
        string want = Block(newline);
        if (now != want && Environment.GetEnvironmentVariable("LILYSHARP_UPDATE_DOCS") == "1")
        {
            File.WriteAllText(path, text[..from] + want + text[(to + End.Length)..], new UTF8Encoding(false));
            return;
        }
        Assert.Equal(want, now);
    }

    [Fact]
    public void TheGuitarsLowE_IsWrittenWhereTheCompilerReadsE2()
    {
        // The case of the report: a guitar part's lowest note.
        var (lo, _) = WrittenEnds("guitar");
        Assert.StartsWith("e", lo);
        Assert.Equal(0, Warnings("guitar", lo, "e"));
        Assert.Equal(1, Warnings("guitar", lo.Replace("e", "ees"), "e"));
    }
}
