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
/// LILYPOND-REF: scm/define-grobs.scm:2428-2442 NoteSpacing
/// LILYPOND-REF: lily/note-spacing.cc:119-199 stem_dir_correction
/// </remarks>
public sealed record NoteSpacingParameters
{
    public static NoteSpacingParameters Default { get; } = new();

    /// <summary>
    /// Correction factor for knee spacing (stem direction changes at beam).
    /// LILYPOND-REF: define-grobs.scm:2428 (knee-spacing-correction . 1.0)
    /// </summary>
    public double KneeSpacingCorrection { get; init; } = 1.0;

    /// <summary>
    /// Correction factor for notes with same stem direction.
    /// Tightens spacing when stems face the same way.
    /// LILYPOND-REF: define-grobs.scm:2429 (same-direction-correction . 0.25)
    /// </summary>
    public double SameDirectionCorrection { get; init; } = 0.25;

    /// <summary>
    /// Correction factor for notes with opposite stem directions.
    /// Increases spacing when stems face opposite ways to avoid collisions.
    /// LILYPOND-REF: define-grobs.scm:2431 (stem-spacing-correction . 0.5)
    /// </summary>
    public double StemSpacingCorrection { get; init; } = 0.5;

    /// <summary>
    /// Whether to measure distance to the barline (or next note).
    /// LILYPOND-REF: define-grobs.scm:2430 (space-to-barline . #t)
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
}
