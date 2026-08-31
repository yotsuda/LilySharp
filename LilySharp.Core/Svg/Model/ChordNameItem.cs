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
/// <summary>How a chord row / attachment wants its symbols shown.</summary>
/// <remarks>
/// ⚠️ <c>Both</c> — the degree stacked above the name as ONE symbol — was retired
/// 2026-08-23 (user decision, LYS2012 carries the message). To show a track both ways,
/// place it twice: <c>chords prog as roman</c> above <c>chords prog as names</c>. That is
/// two rows the writer can see, order and space, rather than a third mode with its own
/// stacking distance. ⚠️ The two were never identical, and the difference is worth
/// remembering if this is ever revisited: a symbol with NO degree (an <c>r</c> slot's
/// "N.C.") printed ONCE under <c>Both</c>, and prints once PER ROW when stacked, because a
/// roman row falls back to the name.
/// </remarks>
public enum ChordDisplayMode
{
    /// <summary>Absolute chord names (C, Am7, G7). The default.</summary>
    Names,
    /// <summary>Roman-numeral scale degrees for the key (I, IIm7, V7).</summary>
    Roman,
}

/// <summary>A resolved chord symbol to be engraved above the staff (or as an
/// independent chord row): its display text, the moment/index it aligns to, and
/// how it should be shown (see <see cref="ChordDisplayMode"/>).</summary>
public sealed record ChordNameItem
{
    // Identity, not value equality: see ModelIdentity.
    public bool Equals(ChordNameItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);

    /// <summary>The chord symbol text for display (e.g., "Cm7", "B♭maj7").</summary>
    public string ChordText { get; }

    /// <summary>The Roman-numeral degree for the current key (e.g. "IIm7", "V7"), or
    /// null when the chord has no resolved structure. Computed at collection time (the
    /// key is known there); shown when <see cref="DisplayMode"/> is Roman or Both.</summary>
    public string? RomanText { get; init; }

    /// <summary>How this symbol should be shown: absolute name (default), Roman-numeral
    /// degree, or both stacked. Set from the placement's <c>as roman|both|names</c>.</summary>
    public ChordDisplayMode DisplayMode { get; init; } = ChordDisplayMode.Names;

    /// <summary>Measure index containing this chord name.</summary>
    public int MeasureIndex { get; }

    /// <summary>Item index of the note within the measure.</summary>
    public int ItemIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; init; }

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

    // The @chord ANNOTATION is read by AnnotationValues.Chord, from its argument.
    // It used to be read here, from the dotted MarkName, by splitting on '.' and
    // rejoining with "" — this file's own remark said it was "rejoining to the
    // written text", which is what an argument's Text already is
    // (docs/VALUE_SITE_AUDIT.md §9.3, §9.5.3 ⑴).
}
