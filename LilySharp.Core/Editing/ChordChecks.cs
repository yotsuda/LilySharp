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
using LilySharp.Core.Midi;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The chords a piece's symbols name, as the form plays them, and what each does in its key (owner's
/// points 2026-10-30, docs/review-tool-design.md §4): a chord outside the scale is told by its role, so
/// that the AI can explain it, and a finding is made only of one that fits no role, or whose role is not
/// fulfilled. Chords inferred from the notes wait for the harmony analysis: only what a chords row or an
/// <c>@chord(…)</c> writes is read.
/// </summary>
public static class ChordChecks
{
    private const int MostBarsNamed = 6;

    private static readonly int[] MajorScale = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] NaturalMinorScale = [0, 2, 3, 5, 7, 8, 10];
    // A minor key's own: its natural notes, and the raised sixth and seventh.
    private static readonly int[] MinorScale = [0, 2, 3, 5, 7, 8, 9, 10, 11];
    // The degrees a secondary function tonicizes: the key's major and minor triads, not its tonic.
    private static readonly int[] MajorTargets = [2, 4, 5, 7, 9];
    private static readonly int[] MinorTargets = [3, 5, 7, 8, 10];

    /// <summary>One chord where the form plays it.</summary>
    /// <param name="Bar">The bar it starts in, as the form plays them, first bar 1.</param>
    /// <param name="Position">Where it is written.</param>
    /// <param name="Symbol">As it is written.</param>
    /// <param name="Chord">What it sounds.</param>
    internal sealed record PlayedChord(int Bar, int Position, string Symbol, ChordStructure Chord);

    /// <summary>The key a chord is read in.</summary>
    internal readonly record struct ChordKey(int TonicPc, int TonicStep, int Sharps, bool Minor)
    {
        /// <summary>The key of a <c>key</c> line's tonic and mode; null for a mode other than major or minor.</summary>
        public static ChordKey? Of(string tonic, string mode)
            => mode is "major" or "minor" && KeySpelling.SharpsFor(tonic, mode) is int sharps
                ? new ChordKey(CompositionChecks.PitchClass(tonic), KeySpelling.StepOf(tonic[0]), sharps, mode == "minor")
                : null;
    }

    /// <summary>What a chord does in its key: how it is named, and the root it leads to, if it leads to one.</summary>
    internal readonly record struct ChordRole(string Name, int? Expects = null);

    /// <summary>The chords as the form plays them. Each is read by the notes it sounds, so a chord no form plays
    /// is not here, and a chord a row strums comes once for each stroke.</summary>
    internal static List<PlayedChord> Played(SyntaxNode root, MidiFile midi, ChordKey? key)
    {
        int tonicStep = key?.TonicStep ?? 0, sharps = key?.Sharps ?? 0;
        var written = new Dictionary<int, (string Symbol, ChordStructure Chord)>();
        foreach (var entry in root.DescendantNodes<ChordEntrySyntax>())
            if (ChordNameCollector.StructureOf(entry.SymbolText, tonicStep, sharps) is { } typed)
                written[entry.SourceStart] = (entry.SymbolText, ChordTrackPitch.Sounding(entry, typed, sharps));
        foreach (var mark in root.DescendantNodes<MusicMarkSyntax>())
            if (mark.Parent is { } site && ChordAnnotation.Of(mark) is { Symbol: { } symbol, Structure: { } structure })
                written[site.Span.Start] = (symbol, structure);
        if (written.Count == 0)
            return [];
        var bars = midi.BarTicks.Where(b => b > 0).Distinct().OrderBy(b => b).ToList();
        var played = new List<PlayedChord>();
        var onsets = midi.Tracks.SelectMany(t => t.Notes).Where(n => written.ContainsKey(n.SourcePos))
            .Select(n => (n.StartTick, n.SourcePos)).Distinct().OrderBy(n => n.StartTick).ThenBy(n => n.SourcePos);
        foreach (var (tick, position) in onsets)
        {
            var (symbol, chord) = written[position];
            played.Add(new PlayedChord(1 + bars.Count(b => b <= tick), position, symbol, chord));
        }
        return played;
    }

    private static int RootPc(ChordStructure chord) => (RelativeOctave.StepSemitoneOf(chord.RootStep) + chord.RootAlter + 12) % 12;

    /// <summary>The role of a chord outside the scale, by what comes after it; a default role (empty name) for a
    /// chord inside the scale, and null for one outside that fits none. A chord whose tones are not known is
    /// inside: nothing can be said of it. The extensions colour a chord and do not place it, so a chord
    /// is read to its seventh.</summary>
    internal static ChordRole? RoleOf(ChordStructure chord, ChordKey key, ChordStructure? next)
    {
        if (chord.ToneSpecs.Count == 0)
            return new ChordRole("");
        int degree = (RootPc(chord) - key.TonicPc + 12) % 12;
        var tones = chord.ToneSpecs.Where(t => t.DiatonicStep <= 6).Select(t => (degree + t.Semitone) % 12).Append(degree).ToHashSet();
        if (tones.IsSubsetOf(key.Minor ? MinorScale : MajorScale))
            return new ChordRole("");

        bool majorThird = tones.Contains((degree + 4) % 12) && !tones.Contains((degree + 3) % 12);
        bool minorSeventh = tones.Contains((degree + 10) % 12);
        bool majorSeventh = tones.Contains((degree + 11) % 12);
        bool dominant = majorThird && !majorSeventh;
        bool diminished = tones.Contains((degree + 3) % 12) && tones.Contains((degree + 6) % 12);
        int[] targets = key.Minor ? MinorTargets : MajorTargets;
        string Numeral(int d) => DegreeNames[d];
        int? nextDegree = next is { } n ? (RootPc(n) - key.TonicPc + 12) % 12 : null;

        // Before its target, or written as one (`V7/V`): the chord says what it is for.
        int above = (degree + 5) % 12, below = (degree + 1) % 12;
        if (dominant && targets.Contains(above) && (nextDegree == above || chord.SecondaryTarget != null || !IsBlues(degree)))
            return new ChordRole($"V/{Numeral(above)}, a secondary dominant", above);
        if (diminished && targets.Contains(below))
            return new ChordRole($"vii°/{Numeral(below)}, a secondary leading-tone chord", below);
        if (dominant && minorSeventh && IsBlues(degree))
            return new ChordRole("a dominant seventh on the blues's own degree");
        if (degree == 1 && majorThird && !minorSeventh)
            return new ChordRole("♭II, the Neapolitan");
        if (dominant && minorSeventh && nextDegree == (degree + 11) % 12)
            return new ChordRole("a tritone substitute");
        if (tones.IsSubsetOf(key.Minor ? MajorScale : NaturalMinorScale))
            return new ChordRole($"borrowed from the parallel {(key.Minor ? "major" : "minor")}");
        return null;
    }

    private static bool IsBlues(int degree) => degree is 0 or 5;

    private static readonly string[] DegreeNames = ["I", "♭II", "II", "♭III", "III", "IV", "♯IV", "V", "♭VI", "VI", "♭VII", "VII"];

    /// <summary>The chord after <paramref name="index"/> that moves away from it; null at the end.</summary>
    private static ChordStructure? NextOf(List<PlayedChord> chords, int index)
    {
        for (int i = index + 1; i < chords.Count; i++)
            if (RootPc(chords[i].Chord) != RootPc(chords[index].Chord))
                return chords[i].Chord;
        return null;
    }

    /// <summary>The chords the facts list: each once, where it is first played, with its degree and, when it
    /// has one, its role. With no key to read them in, the symbols alone.</summary>
    internal static IEnumerable<(int Bar, string Symbol, string? Roman, string? Role)> Facts(SyntaxNode root, MidiFile midi, ChordKey? key)
    {
        var chords = Played(root, midi, key);
        var seen = new HashSet<int>();
        for (int i = 0; i < chords.Count; i++)
        {
            if (!seen.Add(chords[i].Position))
                continue;
            var c = chords[i];
            if (key is not { } k)
            {
                yield return (c.Bar, c.Symbol, null, null);
                continue;
            }
            string? role = RoleOf(c.Chord, k, NextOf(chords, i)) is { Name.Length: > 0 } named ? named.Name : null;
            yield return (c.Bar, c.Symbol, c.Chord.ToRomanNumeral(k.TonicStep, k.Sharps), role);
        }
    }

    /// <summary>The chords that fit no role, and those whose role is not fulfilled: put as a question, since a
    /// deceptive cadence is written just so. One finding for each symbol and what is asked of it.</summary>
    internal static IEnumerable<CompositionFinding> Findings(string source, SyntaxNode root, MidiFile midi, ChordKey key)
    {
        var chords = Played(root, midi, key);
        var bySymbol = new Dictionary<(string Symbol, int? Expects, int? Went), (List<int> Bars, int Position)>();
        for (int i = 0; i < chords.Count; i++)
        {
            var next = NextOf(chords, i);
            var role = RoleOf(chords[i].Chord, key, next);
            if (role is { Expects: null })
                continue;
            int? expects = role?.Expects is int d ? (d + key.TonicPc) % 12 : null;
            // The last chord has not gone anywhere yet.
            if (role != null && (next == null || RootPc(next) == expects))
                continue;
            var at = (chords[i].Symbol, expects, role == null || next == null ? null : (int?)RootPc(next));
            if (!bySymbol.TryGetValue(at, out var found))
                bySymbol[at] = found = ([], chords[i].Position);
            if (!found.Bars.Contains(chords[i].Bar))
                found.Bars.Add(chords[i].Bar);
        }
        foreach (var ((symbol, expects, went), (bars, position)) in bySymbol)
        {
            string Name(int pc) => PartRanges.SpelledFromPitch(pc + 60, key.Sharps)[..^1];
            string where = (bars.Count == 1 ? $"bar {bars[0]}" : $"bars {string.Join(", ", bars.Take(MostBarsNamed))}{(bars.Count > MostBarsNamed ? ", …" : "")}")
                + ", as the form plays them";
            var chord = chords.First(c => c.Position == position).Chord;
            string roman = chord.ToRomanNumeral(key.TonicStep, key.Sharps);
            yield return expects is int target
                ? new("chord.outside", $"{symbol} ({roman}, {where}) leads to a chord on {Name(target)}, and goes to {Name(went!.Value)}.")
                {
                    Bars = bars, Line = LineOf(source, position), Written = symbol,
                    Ask = $"Is the {symbol} in bar {bars[0]} meant to lead to {Name(target)}? Going to {Name(went!.Value)} instead, it may be a deceptive cadence.",
                }
                : new("chord.outside", $"{symbol} ({roman}, {where}) is outside the key, and none of the usual roles fits it.")
                {
                    Bars = bars, Line = LineOf(source, position), Written = symbol,
                    Ask = $"Is {symbol} in bar {bars[0]} the chord you meant?",
                };
        }
    }

    private static int LineOf(string source, int position) => 1 + source.AsSpan(0, position).Count('\n');
}
