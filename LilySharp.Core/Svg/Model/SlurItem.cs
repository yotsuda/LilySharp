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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents a slur connecting multiple notes for phrasing.
/// A slur is a curved line that groups notes together,
/// indicating they should be played legato.
/// </summary>
public sealed record SlurItem
{
    // Identity, not value equality: see ModelIdentity.
    public bool Equals(SlurItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);

    /// <summary>Staff position at start.</summary>
    public int StartStaffPosition { get; }

    /// <summary>Staff position at end.</summary>
    public int EndStaffPosition { get; }

    /// <summary>Direction of the slur curve (up or down).</summary>
    public bool CurveUp { get; }

    /// <summary>Measure index where the slur starts.</summary>
    public int StartMeasureIndex { get; }

    /// <summary>Measure index where the slur ends.</summary>
    public int EndMeasureIndex { get; }

    /// <summary>Index of the start note within its measure.</summary>
    public int StartItemIndex { get; }

    /// <summary>Index of the end note within its measure.</summary>
    public int EndItemIndex { get; }

    /// <summary>Index of the voice this slur belongs to (0 = the primary/only
    /// voice). On a multi-voice staff the layout resolves the slur's endpoint X,
    /// head displacement and obstacles against THIS voice's measures.</summary>
    public int VoiceIndex { get; }

    /// <summary>Source position of the <c>(</c> that opened this slur, or
    /// <see cref="MusicItem.NoSourcePosition"/> when nothing wrote one (a grace slur is
    /// implied by <c>grace { }</c>). The drawn bow's <c>data-pos</c> — its click target.</summary>
    /// <remarks>
    /// ⚠️ TWO POSITIONS, ONE BOW: a slur is written at two places and the reader may put
    /// the caret on either, so the bow carries the open as its primary and the close as a
    /// <c>data-alt</c> alias (<c>IDrawingContext.Source(int, IReadOnlyList&lt;int&gt;)</c>).
    /// A tie needs no such pair — <c>~</c> is one character — so <c>TieItem</c> has no
    /// field of its own and reads <c>StartNote.TieStartSourcePosition</c> instead.
    /// <para>
    /// ⚠️ NOT IN <c>MeasureContentKey</c>: slurs are not one of its bucketed side tables
    /// (they are detected downstream of the collect), so unlike the offsets on
    /// <c>MusicItem</c> these two need no exclusion — they never reach a content hash.
    /// </para>
    /// </remarks>
    public int StartSourcePosition { get; init; } = MusicItem.NoSourcePosition;

    /// <summary>Source position of the <c>)</c> that closed this slur, or
    /// <see cref="MusicItem.NoSourcePosition"/>. See <see cref="StartSourcePosition"/>.</summary>
    public int EndSourcePosition { get; init; } = MusicItem.NoSourcePosition;

    /// <summary>Creates a slur spanning from a start note to an end note.</summary>
    public SlurItem(
        int startStaffPosition,
        int endStaffPosition,
        bool curveUp,
        int startMeasureIndex,
        int endMeasureIndex,
        int startItemIndex,
        int endItemIndex,
        int voiceIndex = 0)
    {
        StartStaffPosition = startStaffPosition;
        EndStaffPosition = endStaffPosition;
        CurveUp = curveUp;
        StartMeasureIndex = startMeasureIndex;
        EndMeasureIndex = endMeasureIndex;
        StartItemIndex = startItemIndex;
        EndItemIndex = endItemIndex;
        VoiceIndex = voiceIndex;
    }

    /// <summary>The same slur with both measure numbers moved by <paramref name="delta"/> —
    /// see <see cref="TieItem.WithMeasureIndicesShifted"/>; the two source offsets ride
    /// along and are re-derived at render time.</summary>
    internal SlurItem WithMeasureIndicesShifted(int delta)
        => new(StartStaffPosition, EndStaffPosition, CurveUp,
            StartMeasureIndex + delta, EndMeasureIndex + delta, StartItemIndex, EndItemIndex,
            VoiceIndex)
        { StartSourcePosition = StartSourcePosition, EndSourcePosition = EndSourcePosition };
}