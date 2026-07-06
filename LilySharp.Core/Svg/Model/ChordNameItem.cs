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

using System.Collections.Immutable;
using System.Text;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A chord name symbol to display above the staff (e.g., "Cm7", "B♭maj7").
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/chord-name.cc - ChordName grob
/// LILYPOND-REF: scm/chord-ignatzek-names.scm - Ignatzek naming algorithm
/// LILYPOND-REF: scm/define-grobs.scm - ChordName: font-family=sans, font-size=1.5
///
/// LilySharp uses annotation-based chord names (@chord.TEXT) rather than
/// LilyPond's separate ChordNames context with pitch-set analysis.
/// The chord symbol text is specified directly and displayed above the staff.
/// </remarks>
public sealed record ChordNameItem
{
    /// <summary>The chord symbol text for display (e.g., "Cm7", "B♭maj7").</summary>
    public string ChordText { get; }

    /// <summary>Measure index containing this chord name.</summary>
    public int MeasureIndex { get; }

    /// <summary>Item index of the note within the measure.</summary>
    public int ItemIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }

    /// <summary>Global staff index this chord name belongs to (multi-staff
    /// routing; see <c>DynamicItem.StaffIndex</c>). 0 for single-staff.</summary>
    public int StaffIndex { get; }

    /// <summary>
    /// When true the chord is placed by its musical moment (<see cref="Timing"/>)
    /// against the shared column grid, not by <see cref="ItemIndex"/>. Set for
    /// <c>chordnames { }</c> entries, which carry their own rhythm independent of
    /// the melody. The note-attached <c>@chord.TEXT</c> path leaves it false.
    /// </summary>
    public bool UseTiming { get; }

    /// <summary>The chord's start time from its measure's start (used when <see cref="UseTiming"/>).</summary>
    public Fraction Timing { get; }

    /// <summary>
    /// The resolved chord structure (root, quality interval set, bass) when entered
    /// symbolically via <c>chordnames</c>. Null for the literal <c>@chord.TEXT</c>
    /// path. The foundation for future staff-note expansion and fret diagrams;
    /// Phase 1 renders only <see cref="ChordText"/>.
    /// </summary>
    public ChordStructure? Structure { get; }

    /// <summary>
    /// True when this symbol belongs to an independent chord ROW (a <c>chords name
    /// { }</c> part placed via <c>chords name</c> in a score). The engraver then
    /// places it WITHIN its row's band (by <see cref="StaffIndex"/>) rather than
    /// floating above an associated staff.
    /// </summary>
    public bool IsChordRow { get; }

    /// <summary>Creates a chord name symbol for display above the staff.</summary>
    public ChordNameItem(string chordText, int measureIndex, int itemIndex,
        int sourcePosition, int staffIndex = 0,
        bool useTiming = false, Fraction timing = default, ChordStructure? structure = null,
        bool isChordRow = false)
    {
        ChordText = chordText;
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        SourcePosition = sourcePosition;
        StaffIndex = staffIndex;
        UseTiming = useTiming;
        Timing = timing;
        Structure = structure;
        IsChordRow = isChordRow;
    }

    /// <summary>
    /// Parses a chord name annotation string (e.g., "chord.Cm7", "chord.Bb7").
    /// Returns the display text, or null if not a chord annotation.
    /// </summary>
    /// <remarks>
    /// Syntax: "chord.TEXT" where TEXT is the chord symbol.
    /// Post-processing: Root flats (Bb→B♭, Eb→E♭, etc.) are automatically converted.
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm - root naming with accidentals
    /// </remarks>
    public static string? ParseChordName(string markName)
    {
        if (!markName.StartsWith("chord.", StringComparison.Ordinal))
            return null;

        // Join all parts after "chord." (remove dots used as token separators)
        var rest = markName.Substring(6);
        if (string.IsNullOrEmpty(rest))
            return null;

        // Parts were joined with dots by MusicMarkSyntax.MarkName.
        // For chord names, we join them without dots to form the display text.
        // e.g., "chord.C.m7" → parts "C" + "m7" → "Cm7"
        var parts = rest.Split('.');
        var rawText = string.Join("", parts);

        if (string.IsNullOrEmpty(rawText))
            return null;

        return FormatChordText(rawText);
    }

    /// <summary>
    /// Formats raw chord text by converting root accidentals to Unicode symbols.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-name.scm:58-85 - alteration→text-accidental-markup
    /// Recognizes root flats: Bb→B♭, Eb→E♭, Ab→A♭, Db→D♭, Gb→G♭, Cb→C♭, Fb→F♭
    /// </remarks>
    private static string FormatChordText(string raw)
    {
        if (raw.Length < 2)
            return raw;

        // Check for root flat: uppercase letter (A-G) followed by lowercase 'b'
        // but only if it's a valid flat root (not "B" alone which is B natural)
        char root = raw[0];
        if (root is >= 'A' and <= 'G' && raw[1] == 'b')
        {
            // Verify this is a root flat, not part of a word like "Baug"
            // Root flat: the 'b' must be at position 1 and the root must be a valid note
            return root + "\u266D" + raw.Substring(2);  // ♭
        }

        return raw;
    }
}
