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
using LilySharp.Core.Semantics;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Type of grace note.
/// </summary>
public enum GraceNoteType
{
    /// <summary>Regular grace note (no slash).</summary>
    Grace,
    /// <summary>Acciaccatura (slashed grace note, very short).</summary>
    Acciaccatura,
    /// <summary>Appoggiatura (unslashed grace note, takes time from main note).</summary>
    Appoggiatura
}

/// <summary>
/// Information about a single note within a grace note group.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/grace-spacing-engraver.cc — each grace note has its own duration
/// for spring-based spacing calculation.
/// </remarks>
public readonly record struct GraceNoteInfo(
    int StaffPosition,      // Staff position (-6 = middle C in treble clef)
    string? Accidental,     // "sharp", "flat", "natural", "doubleSharp", "doubleFlat", or null
    bool NeedsLedger,       // Whether ledger lines are needed
    Fraction BaseDuration   // Duration of this grace note (for spacing calculation)
);

/// <summary>
/// A group of grace notes attached to a main note.
/// </summary>
/// <remarks>
/// LILYPOND-REF: grace-engraver.cc:36-80 Grace_engraver class
/// LILYPOND-REF: define-grobs.scm:1358-1402 GraceSpacing grob definition
///
/// Grace notes are rendered smaller (typically 65% of normal size) and
/// placed before their main note. Acciaccaturas have a diagonal slash
/// through the stem.
/// </remarks>
public sealed record GraceNoteItem
{
    /// <summary>The type of grace note.</summary>
    public GraceNoteType Type { get; }

    /// <summary>The notes in this grace group.</summary>
    public ImmutableArray<GraceNoteInfo> Notes { get; }

    /// <summary>The measure index where this grace note appears.</summary>
    public int MeasureIndex { get; }

    /// <summary>The item index of the main note this grace is attached to.</summary>
    public int MainNoteItemIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }

    /// <summary>Global staff index this grace group belongs to (multi-staff
    /// routing; see <c>DynamicItem.StaffIndex</c>). 0 for single-staff.</summary>
    public int StaffIndex { get; }

    public GraceNoteItem(
        GraceNoteType type,
        ImmutableArray<GraceNoteInfo> notes,
        int measureIndex,
        int mainNoteItemIndex,
        int sourcePosition,
        int staffIndex = 0)
    {
        Type = type;
        Notes = notes;
        MeasureIndex = measureIndex;
        MainNoteItemIndex = mainNoteItemIndex;
        SourcePosition = sourcePosition;
        StaffIndex = staffIndex;
    }

    /// <summary>
    /// Scale factor for grace notes relative to normal notes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: define-grobs.scm:1389 font-size = -3
    /// Font size -3 corresponds to approximately 0.65 scaling.
    /// </remarks>
    public const double ScaleFactor = 0.65;
}