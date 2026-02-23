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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A tremolo marking on a note.
/// </summary>
/// <remarks>
/// LILYPOND-REF: stem-tremolo.cc:36-80 Stem_tremolo class
/// LILYPOND-REF: define-grobs.scm:2760-2810 StemTremolo grob definition
///
/// Tremolos are indicated by diagonal beams across the stem:
/// - 1 beam = 8th note tremolo
/// - 2 beams = 16th note tremolo
/// - 3 beams = 32nd note tremolo
/// </remarks>
public sealed record TremoloItem
{
    /// <summary>Number of tremolo beams (1-3).</summary>
    public int BeamCount { get; }

    /// <summary>The measure index where this tremolo appears.</summary>
    public int MeasureIndex { get; }

    /// <summary>The item index within the measure.</summary>
    public int ItemIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }

    public TremoloItem(int beamCount, int measureIndex, int itemIndex, int sourcePosition)
    {
        BeamCount = Math.Clamp(beamCount, 1, 3);
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        SourcePosition = sourcePosition;
    }
}