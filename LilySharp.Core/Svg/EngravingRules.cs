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

namespace LilySharp.Core.Svg;

/// <summary>
/// Music engraving rules based on standard practices.
/// Reference: "Behind Bars" by Elaine Gould.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm Stem / SpacingSpanner / NonMusicalPaperColumn
/// LP origin for individual constants is annotated per-member below.
/// </remarks>
public static class EngravingRules
{
    // === Stem lengths (in staff spaces) ===

    /// <summary>Standard stem length is 3.5 staff spaces (one octave).</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm Stem.length default 3.5.</remarks>
    public const double StandardStemLength = 3.5;

    /// <summary>Minimum stem length is 2.5 staff spaces.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm Stem property defaults; LP also uses
    /// stem-length-tuning callbacks via lily/stem.cc.
    /// </remarks>
    public const double MinimumStemLength = 2.5;

    // === Note Spacing ===
    // LILYPOND-REF: scm/define-grobs.scm:3239-3247 SpacingSpanner defaults
    //   shortest-duration-space = 2.0
    //   spacing-increment = 1.2
    //   base-shortest-duration = 3/16  (NOTE: Lily# currently uses 1/8 — see H-1
    //   in LAYOUT_ROADMAP_V3.md for the multi-voice tracking work that supersedes
    //   this static reference)
    // The duration→space formula itself follows lily/spacing-basic.cc:109-183.

    private const double ShortestDurationSpace = 2.0;
    private const double SpacingIncrement = 1.2;
    private const int ReferenceDuration = 8;

    /// <summary>
    /// Calculate note spacing using logarithmic spacing algorithm.
    /// </summary>
    /// <param name="noteValue">Note value (1=whole, 2=half, 4=quarter, 8=eighth, etc.)</param>
    /// <returns>Spacing in staff spaces</returns>
    public static double GetNoteSpacing(int noteValue)
    {
        double ratio = (double)ReferenceDuration / noteValue;

        if (ratio < 1.0)
        {
            // Short notes: linear spacing
            return (ShortestDurationSpace + ratio - 1) * SpacingIncrement;
        }
        else
        {
            // Longer notes: logarithmic spacing
            return (ShortestDurationSpace + Math.Log2(ratio)) * SpacingIncrement;
        }
    }

    /// <summary>Minimum spacing between notes (in staff spaces).</summary>
    /// <remarks>LILYPOND-REF: lily/note-spacing.cc minimum-distance derivation.</remarks>
    public const double MinimumNoteSpacing = 1.5;

    // === Break-aligned spacing ===
    // LILYPOND-REF: scm/define-grobs.scm space-alist entries on
    //   NonMusicalPaperColumn / Clef / KeySignature / TimeSignature / BarLine.
    // The values below are LilyPond's space-alist defaults; concrete entries
    // live near grob definitions in define-grobs.scm.

    /// <summary>Space after barline to first note.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm BarLine.space-alist (first-note ~1.3 ss).</remarks>
    public const double SpaceAfterBarline = 1.3;

    /// <summary>Space after clef to next element.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm Clef.space-alist (first-note 1.0 ss).</remarks>
    public const double SpaceAfterClef = 1.0;

    /// <summary>Space after time signature to first note.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm TimeSignature.space-alist (first-note 2.0 ss).</remarks>
    public const double SpaceAfterTimeSignature = 2.0;

    /// <summary>Space after key signature to first note.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm KeySignature.space-alist (first-note 2.5 ss).</remarks>
    public const double SpaceAfterKeySignature = 2.5;
}