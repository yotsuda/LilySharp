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
/// Represents a tie connecting two notes of the same pitch.
/// A tie is a curved line that connects two notes, indicating
/// that the second note is a continuation of the first.
/// </summary>
public sealed record TieItem
{
    // Identity, not value equality: see ModelIdentity.
    public bool Equals(TieItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);

    /// <summary>The starting note of the tie.</summary>
    public NoteItem StartNote { get; }

    /// <summary>The ending note of the tie.</summary>
    public NoteItem EndNote { get; }

    /// <summary>Staff position of the tie (same as notes).</summary>
    public int StaffPosition { get; }

    /// <summary>
    /// A direction imposed on the tie before it is placed — <c>true</c> up, <c>false</c>
    /// down — or <c>null</c> to let <see cref="Layout.TieFormattingProblem"/> decide.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-specification.cc:41-51 <c>Tie_specification::from_grob</c> —
    /// LilyPond takes a direction from the grob only when <c>direction</c> holds a NUMBER
    /// (<c>has_manual_dir_</c>), i.e. when something set it: <c>\voiceOne</c>/<c>\voiceTwo</c>
    /// (ly/engraver-init.ly), <c>\tieUp</c>, or the Tie_column's own distribution over a
    /// chord's ties. The default is the callback <c>ly:tie::calc-direction</c>, which is not
    /// a number, so an ordinary tie arrives with NO direction and the scored search decides
    /// (lily/tie-formatting-problem.cc:1004-1023 generate_optimal_configuration).
    /// <para>
    /// ⚠️ THIS USED TO BE A PLAIN <c>bool</c> SET AT COLLECTION TIME from the FIRST note's
    /// stem, and that rule is not LilyPond's — measured, it gives the same answer to two bars
    /// LilyPond answers oppositely (audit/lp-geometry <c>tie.direction.beam-opposes-stem</c>
    /// and <c>tie.direction.beam-agrees-with-stem</c>, the same music with the second note's
    /// beam reversed). <c>Tie::get_default_dir</c> reads like that rule and is not it either:
    /// lily/tie.cc:203-208 only calls it for a BROKEN piece.
    /// </para>
    /// </remarks>
    public bool? ForcedCurveUp { get; }

    /// <summary>Measure index where the tie starts.</summary>
    public int StartMeasureIndex { get; }

    /// <summary>Measure index where the tie ends.</summary>
    public int EndMeasureIndex { get; }

    /// <summary>Index of the start note within its measure.</summary>
    public int StartItemIndex { get; }

    /// <summary>Index of the end note within its measure.</summary>
    public int EndItemIndex { get; }

    /// <summary>Index of the voice this tie belongs to (0 = the primary/only
    /// voice). On a multi-voice staff the layout resolves the tie's endpoint X
    /// and head displacement against THIS voice's measures.</summary>
    public int VoiceIndex { get; }

    /// <summary>Source position of the <c>~</c> that wrote this tie, or
    /// <see cref="MusicItem.NoSourcePosition"/>. The drawn bow's <c>data-pos</c>.</summary>
    /// <remarks>
    /// ⚠️ ONE CHARACTER, SO NO ALIAS — a tie is written once, unlike a slur, whose
    /// <c>(</c> and <c>)</c> make <see cref="SlurItem.StartSourcePosition"/> a pair.
    /// A chord's ties all carry the ONE <c>~</c> written after the chord.
    /// <para>
    /// ⚠️ <c>init</c>, and re-derived at render time by
    /// <c>SharedRenderer.ResolveDataPos</c>: <c>SystemLayoutCache</c> serves a cached
    /// <c>TieLayout</c> whenever the measure CONTENT is unchanged, and content keys exclude
    /// source offsets by design — so a baked offset here is the offset of the edit that
    /// COMPUTED the layout, not of the edit being rendered (the session-190 defect, whose
    /// net is <c>IncrementalCompilerTests.ChainedEditsOnABowedTwoVoiceScore_AlwaysMatchFull</c>).
    /// </para>
    /// </remarks>
    public int SourcePosition { get; init; } = MusicItem.NoSourcePosition;

    /// <summary>Creates a tie between two notes of the same pitch.</summary>
    public TieItem(
        NoteItem startNote,
        NoteItem endNote,
        int staffPosition,
        bool? forcedCurveUp,
        int startMeasureIndex,
        int endMeasureIndex,
        int startItemIndex,
        int endItemIndex,
        int voiceIndex = 0)
    {
        StartNote = startNote;
        EndNote = endNote;
        StaffPosition = staffPosition;
        ForcedCurveUp = forcedCurveUp;
        StartMeasureIndex = startMeasureIndex;
        EndMeasureIndex = endMeasureIndex;
        StartItemIndex = startItemIndex;
        EndItemIndex = endItemIndex;
        VoiceIndex = voiceIndex;
    }

    /// <summary>The same tie with both measure numbers moved by <paramref name="delta"/> —
    /// what a per-system memo hands back when it serves a laid-out tie found under other
    /// measure numbers (<c>SystemLayoutCache</c>). The notes, the item indices and the
    /// source offset are carried as they are; the offset is re-derived at render time
    /// (see <see cref="SourcePosition"/>). A NEW identity, like every copy of this record.</summary>
    internal TieItem WithMeasureIndicesShifted(int delta)
        => new(StartNote, EndNote, StaffPosition, ForcedCurveUp,
            StartMeasureIndex + delta, EndMeasureIndex + delta, StartItemIndex, EndItemIndex,
            VoiceIndex)
        { SourcePosition = SourcePosition };
}