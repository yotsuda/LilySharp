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
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
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
    /// <summary>One section's generated chord track: the melody part-block it was
    /// read from (a chords part is added right after it) and the chords block text.</summary>
    public readonly record struct SectionChordTrack(PartBlockSyntax MelodyBlock, string ChordsBlock);

    /// <summary>
    /// Adds a harmonized chords part to <paramref name="source"/> and returns the new
    /// document text (plus an optional note for the user), or null when there is no
    /// section melody to harmonize. The chords track RESPECTS the document's layout: a
    /// part-major file gets a top-level <c>chords harmony { section A { … } … }</c>
    /// track (no reshaping), a section-major file gets a <c>chords harmony { … }</c>
    /// spliced into each section. Powers the "Lily#: Add Chord Track" editor command.
    /// </summary>
    public static (string Text, string? Info)? AddChordTracks(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var root = tree.GetRoot();

        // Part-major: add a top-level chord track with matching inner sections, so the
        // current layout is preserved (previously the file was converted to
        // section-major first, which moved the sections above the parts).
        if (PartSectionLayoutConverter.Detect(root) == LayoutForm.PartMajor)
        {
            var voice = DetectVoice(root);
            var melodyPart = voice == null ? null
                : root.DescendantNodes<PartDeclarationSyntax>().FirstOrDefault(p => p.Name.Text == voice);
            var block = melodyPart == null ? null : HarmonizePartMajor(tree, voice!, melodyPart);
            if (block == null)
                return null;

            // Drop the track in just after the melody part, then wire the staff to it.
            var text = source.Insert(melodyPart!.Span.End, "\n\n" + block + "\n");
            return (AttachWithChords(text, voice!), null);
        }

        var tracks = HarmonizeBySections(tree);
        if (tracks.Count == 0)
            return null;

        // Splice a chords part after each section's melody part-block (deepest offset
        // first, so earlier insertions do not shift later positions); indent it to sit
        // as a sibling inside the section, on its own lines.
        var sb = new StringBuilder(source);
        foreach (var track in tracks.OrderByDescending(t => t.MelodyBlock.Span.End))
            sb.Insert(track.MelodyBlock.Span.End, "\n  " + track.ChordsBlock.Replace("\n", "\n  ") + "\n");

        return (AttachWithChords(sb.ToString(), tracks[0].MelodyBlock.PartName.Text), null);
    }

    // Wire the melody staff to the chords once: `staff X` -> `staff X with chords harmony`.
    private static string AttachWithChords(string text, string melodyName)
    {
        var staff = Regex.Match(text,
            @"\bstaff\s+" + Regex.Escape(melodyName) + @"\b(?!\s+with\b)");
        return staff.Success
            ? text.Insert(staff.Index + staff.Length, " with chords harmony")
            : text;
    }

    /// <summary>
    /// Harmonizes a part-major melody INTO A SINGLE part-major chord track:
    /// <c>chords harmony { section A { … } section B { … } }</c>, one inner section per
    /// the melody part's own sections. Returns null when nothing harmonizes.
    /// </summary>
    private static string? HarmonizePartMajor(SyntaxTree tree, string voice, PartDeclarationSyntax melodyPart)
    {
        var (tonic, sharps) = ReadKey(tree.ToFullString());
        var chords = DiatonicChords.ForKey(tonic, sharps);
        var pcByPos = PitchClassesByPosition(tree.GetRoot());

        var sb = new StringBuilder("chords harmony {\n");
        bool any = false;
        foreach (var section in melodyPart.DescendantNodes<SectionDeclarationSyntax>())
        {
            // Collect just this section's melody (a local structure naming only it) and
            // harmonize it, exactly as the section-major path does per section.
            var measures = new MeasureCollector()
                .Collect(tree, voice, SectionStructure(section.Name.Text)).Voice.Measures;
            var entries = HarmonizeMeasures(measures, pcByPos, chords);
            if (entries.Count == 0)
                continue;
            sb.Append("  section ").Append(section.SectionName)
              .Append(" { ").Append(string.Join(" | ", entries)).Append(" | }\n");
            any = true;
        }
        sb.Append('}');
        return any ? sb.ToString() : null;
    }

    /// <summary>
    /// Harmonizes each section's melody INDEPENDENTLY — without consulting the
    /// <c>structure { }</c> block — and returns one <c>chords harmony { … }</c> track
    /// per section, so each track lines up with its section as written. Robust to
    /// documents with or without a structure block. <paramref name="voice"/> selects
    /// which part to read (default: the first part-block's name, from the tree).
    /// </summary>
    public static IReadOnlyList<SectionChordTrack> HarmonizeBySections(SyntaxTree tree, string? voice = null)
    {
        var root = tree.GetRoot();
        voice ??= DetectVoice(root);
        var (tonic, sharps) = ReadKey(tree.ToFullString());
        var chords = DiatonicChords.ForKey(tonic, sharps);
        var pcByPos = PitchClassesByPosition(root);

        var tracks = new List<SectionChordTrack>();
        foreach (var section in root.DescendantNodes<SectionDeclarationSyntax>())
        {
            var melodyBlock = section.DescendantNodes<PartBlockSyntax>()
                .FirstOrDefault(pb => voice == null || pb.PartName.Text == voice);
            if (melodyBlock == null)
                continue;

            // Collect only THIS section for the voice, via a structure that names
            // just it (resolved against the whole tree's section definitions) — so
            // the track follows the written section, not the structure's ordering.
            var local = SectionStructure(section.Name.Text);
            var measures = new MeasureCollector()
                .Collect(tree, melodyBlock.PartName.Text, local).Voice.Measures;

            var entries = HarmonizeMeasures(measures, pcByPos, chords);
            if (entries.Count == 0)
                continue;
            tracks.Add(new SectionChordTrack(melodyBlock, ChordsBlock(entries)));
        }
        return tracks;
    }

    /// <summary>
    /// Harmonizes the whole melody and returns a single <c>chords harmony { … }</c>
    /// block, or null when there is no melody. Kept for the CLI and simple/inline
    /// documents; the editor command uses <see cref="HarmonizeBySections"/> so each
    /// section gets its own aligned track.
    /// </summary>
    public static string? Harmonize(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var (tonic, sharps) = ReadKey(tree.ToFullString());
        var chords = DiatonicChords.ForKey(tonic, sharps);
        var measures = new MeasureCollector().Collect(tree, DetectVoice(root)).Voice.Measures;
        var entries = HarmonizeMeasures(measures, PitchClassesByPosition(root), chords);
        return entries.Count == 0 ? null : ChordsBlock(entries);
    }

    // One chord per measure, each capped with a barline (as chord parts are written)
    // so the row's measures line up with the melody's.
    private static string ChordsBlock(List<string> entries)
        => "chords harmony {\n  " + string.Join(" | ", entries) + " |\n}";

    // Pitch classes of the melody, keyed by each note/chord's source position —
    // NoteItem/ChordItem carry the same position, so the collected measures (which
    // have the rhythm + measure boundaries) can be joined to the actual pitches.
    private static Dictionary<int, List<int>> PitchClassesByPosition(SyntaxNode root)
    {
        var pcByPos = new Dictionary<int, List<int>>();
        foreach (var n in root.DescendantNodes<NoteSyntax>())
            pcByPos[n.Position] = new List<int> { PitchClass(n.Pitch) };
        foreach (var c in root.DescendantNodes<ChordSyntax>())
            pcByPos[c.Position] = c.DescendantNodes<PitchSyntax>().Select(PitchClass).ToList();
        return pcByPos;
    }

    // One diatonic chord per measure: weight pitch classes by note duration (downbeat
    // doubled), score each triad by chord-tone coverage; a rest-only measure holds
    // the previous chord; the major-key V is voiced as V7.
    private static List<string> HarmonizeMeasures(
        IReadOnlyList<Measure> measures,
        Dictionary<int, List<int>> pcByPos,
        ImmutableArray<DiatonicChord> chords)
    {
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
            // The dominant (V) reads stronger as a dominant seventh (V7) — but only
            // when it is a MAJOR triad (a major-key V); a minor-key v is left alone.
            string quality = degree == 4 && chord.Quality.Length == 0
                ? ":7"
                : chord.LilyQualitySuffix;
            entries.Add(chord.LilyRoot + ToNoteValue(MeasureDuration(measure)) + quality);
        }
        return entries;
    }

    // The part to harmonize: the first part-block's name (section-major), else the
    // first part declaration's name (part-major). From the tree, never a regex over
    // the source — so a comment containing the word "part" cannot mislead it.
    private static string? DetectVoice(SyntaxNode root)
    {
        var block = root.DescendantNodes<PartBlockSyntax>().FirstOrDefault();
        if (block != null)
            return block.PartName.Text;
        return root.DescendantNodes<PartDeclarationSyntax>().FirstOrDefault()?.Name.Text;
    }

    // A structure that names one section, parsed standalone; its reference resolves
    // against the main tree's section definitions when passed to Collect.
    private static StructureDeclarationSyntax? SectionStructure(string sectionName)
        => SyntaxTree.Parse("structure { " + sectionName + " }").GetRoot()
            .DescendantNodes<StructureDeclarationSyntax>().FirstOrDefault();

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
}
