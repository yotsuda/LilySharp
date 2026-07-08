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
using System.Text.RegularExpressions;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Harmony;

/// <summary>
/// A first-pass automatic harmonizer: reads an existing melody and its key and
/// proposes one diatonic chord per measure, returned as a <c>chords { }</c> part.
/// </summary>
/// <remarks>
/// MVP heuristic (a starting point to edit, not a "correct" answer — harmonization
/// is inherently ambiguous): one chord per written measure, chosen from the key's
/// seven diatonic triads by how much of the melody (weighted by note duration, the
/// downbeat counted double) lands on each candidate's chord tones. Ties nudge toward
/// the common chords (I, IV, V…). A rest-only measure holds the previous chord.
/// Future work: harmonic rhythm inference, a progression/cadence model, secondary
/// dominants and sevenths.
/// </remarks>
public static class ChordHarmonizer
{
    /// <summary>
    /// Harmonizes the melody in <paramref name="tree"/> and returns a
    /// <c>chords harmony { … }</c> block (one chord per measure), or null when there
    /// is no melody to read.
    /// </summary>
    public static string? Harmonize(SyntaxTree tree)
    {
        string source = tree.ToFullString();
        var (tonic, sharps) = ReadKey(source);
        var chords = DiatonicChords.ForKey(tonic, sharps);

        // Pitch classes of the melody, keyed by each note/chord's source position —
        // NoteItem/ChordItem carry the same position, so we can join the collected
        // measures (which already have the correct rhythm + measure boundaries) to
        // the actual pitches (which the collected model does not carry).
        var pcByPos = new Dictionary<int, List<int>>();
        var root = tree.GetRoot();
        foreach (var n in root.DescendantNodes<NoteSyntax>())
            pcByPos[n.Position] = new List<int> { PitchClass(n.Pitch) };
        foreach (var c in root.DescendantNodes<ChordSyntax>())
            pcByPos[c.Position] = c.DescendantNodes<PitchSyntax>().Select(PitchClass).ToList();

        var measures = new MeasureCollector().Collect(tree, FirstPartName(source)).Voice.Measures;
        if (measures.Length == 0)
            return null;

        var entries = new List<string>();
        int prevDegree = 0; // hold over rest-only measures; tonic to start
        foreach (var measure in measures)
        {
            var weight = new double[12];
            bool hasPitch = false, downbeat = true;
            foreach (var item in measure.Items)
            {
                if (!pcByPos.TryGetValue(item.SourcePosition, out var pcs))
                    continue;
                double w = item.Duration.ToDouble() * (downbeat ? 2.0 : 1.0);
                downbeat = false;
                foreach (int pc in pcs) { weight[pc] += w; hasPitch = true; }
            }

            int degree = prevDegree;
            if (hasPitch)
            {
                double best = double.NegativeInfinity;
                foreach (var ch in chords)
                {
                    double s = ch.PitchClasses.Sum(pc => weight[pc]) + 0.001 * DegreePreference(ch.Degree);
                    if (s > best) { best = s; degree = ch.Degree; }
                }
            }
            prevDegree = degree;

            var chord = chords[degree];
            // The dominant (V) reads stronger as a dominant seventh (V7). Only when
            // it is a MAJOR triad — i.e. a major-key V; a minor-key v (natural minor)
            // is left alone rather than emitting a weak v7.
            string quality = degree == 4 && chord.Quality.Length == 0
                ? ":7"
                : chord.LilyQualitySuffix;
            entries.Add(chord.LilyRoot + ToNoteValue(MeasureDuration(measure)) + quality);
        }

        // One chord per measure, each capped with a barline (as chord parts are
        // written) so the row's measures line up with the melody's.
        return "chords harmony {\n  " + string.Join(" | ", entries) + " |\n}";
    }

    // Common chords first when the pitch score ties (I / IV / V over iii / vii°).
    private static int DegreePreference(int degree) => degree switch
    { 0 => 6, 3 => 5, 4 => 5, 5 => 4, 1 => 3, 2 => 2, _ => 1 };

    private static int PitchClass(PitchSyntax p)
    {
        int baseSemi = p.BaseName switch
        { 'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5, 'g' => 7, 'a' => 9, 'b' => 11, _ => 0 };
        return ((baseSemi + p.AccidentalOffset) % 12 + 12) % 12;
    }

    private static Fraction MeasureDuration(Measure measure)
    {
        var total = Fraction.Zero;
        foreach (var item in measure.Items)
            total += item.Duration;
        return total;
    }

    /// <summary>A duration Fraction as a Lily# note value ("1", "2.", "4"); "1" as a
    /// fallback for an irregular measure the MVP can't express as one dotted value.</summary>
    private static string ToNoteValue(Fraction f)
    {
        foreach (int baseVal in new[] { 1, 2, 4, 8, 16 })
            foreach (int dots in new[] { 0, 1, 2 })
                if (Fraction.FromNoteValue(baseVal).Dotted(dots) == f)
                    return baseVal + new string('.', dots);
        return "1";
    }

    private static (char Tonic, int Sharps) ReadKey(string source)
    {
        var m = Regex.Match(source, @"\bkey\s+([a-gA-G](?:is|es|isis|eses)?)\s+([A-Za-z]+)");
        if (!m.Success)
            return ('c', 0);
        char tonic = char.ToLowerInvariant(m.Groups[1].Value[0]);
        return (tonic, KeySpelling.SharpsFor(m.Groups[1].Value, m.Groups[2].Value) ?? 0);
    }

    private static string? FirstPartName(string source)
    {
        var m = Regex.Match(source, @"\bpart\s+(\w+)");
        return m.Success ? m.Groups[1].Value : null;
    }
}
