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

using LilySharp.Core.Svg;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for note-to-note spacing optical corrections.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:2649-2663 NoteSpacing
/// LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction
/// </remarks>
internal sealed record NoteSpacingParameters
{
    public static NoteSpacingParameters Default { get; } = new();

    /// <summary>
    /// Correction factor for knee spacing (stem direction changes at beam).
    /// LILYPOND-REF: define-grobs.scm:2653 (knee-spacing-correction . 1.0)
    /// </summary>
    public double KneeSpacingCorrection { get; init; } = 1.0;

    /// <summary>
    /// Correction factor for notes with same stem direction.
    /// Tightens spacing when stems face the same way.
    /// LILYPOND-REF: define-grobs.scm:2654 (same-direction-correction . 0.25)
    /// </summary>
    public double SameDirectionCorrection { get; init; } = 0.25;

    /// <summary>
    /// Correction factor for notes with opposite stem directions.
    /// Increases spacing when stems face opposite ways to avoid collisions.
    /// LILYPOND-REF: define-grobs.scm:2656 (stem-spacing-correction . 0.5)
    /// </summary>
    public double StemSpacingCorrection { get; init; } = 0.5;

    /// <summary>
    /// Whether to measure distance to the barline (or next note).
    /// LILYPOND-REF: define-grobs.scm:2655 (space-to-barline . #t)
    /// </summary>
    public bool SpaceToBarline { get; init; } = true;

    /// <summary>
    /// When true, enforces strictly proportional spacing based on duration.
    /// In strict mode, the minimum distance equals the duration-based ideal distance,
    /// preventing compression below proportional spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:229-264 strict_note_spacing
    /// Used with \set SpacingSpanner.strict-note-spacing = ##t
    /// Commonly used in multi-voice scores for uniform column alignment.
    /// </remarks>
    public bool StrictNoteSpacing { get; init; } = false;

    /// <summary>
    /// Base note space: the minimum space for the shortest note in strict mode.
    /// Equals ShortestDurationSpace * SpacingIncrement in LilyPond.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:229-264
    /// LILYPOND-REF: scm/define-grobs.scm SpacingSpanner.base-shortest-duration
    /// </remarks>
    public double BaseNoteSpace { get; init; } = EngravingDefaults.ShortestDurationSpace
                                                  * EngravingDefaults.SpacingIncrement;

    /// <summary>
    /// LILYSHARP-OWN: ⚠️ this knob no longer reaches note-to-note spacing at all, and it is
    /// a candidate for removal rather than for use.
    /// </summary>
    /// <remarks>
    /// It used to be added to a raw-ink skyline distance, and its old REFs claimed
    /// <c>skyline-horizontal-padding</c> as the source. That was wrong twice over: the 0.2
    /// between two columns comes from each grob's <c>extra-spacing-width</c>, not from a
    /// per-pair padding, and LilyPond's note-to-note spring minimum takes NO padding at all.
    /// Both are now ported and the note-to-note path does not read this property — see
    /// <c>SeparatingPaddingTests.NoteToNoteDistance_DoesNotDependOnMinItemGap</c>, which
    /// asserts that nothing it is set to moves that distance.
    /// LILYPOND-REF: lily/separation-item.cc:166-179; lily/note-spacing.cc:78-83;
    ///   lily/separation-item.cc:47-68 with lily/spacing-spanner.cc:315-316.
    /// <para>
    /// The one place it is still READ is
    /// <see cref="SpacingRules.CalculateSkylineDistance"/>'s both-items-null guard, which is
    /// a "cannot happen" branch. <see cref="GlyphMetrics.MinItemGap"/> itself is still live
    /// for LYRIC extents, which are their own unported quantity.
    /// </para>
    /// </remarks>
    public double MinItemGap { get; init; } = GlyphMetrics.MinItemGap;
}
