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
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Type of barline.
/// </summary>
public enum BarlineType
{
    /// <summary>No barline.</summary>
    None,
    /// <summary>Single thin barline (<c>|</c>).</summary>
    Single,
    /// <summary>Double thin barline (<c>||</c>).</summary>
    Double,
    /// <summary>Final barline: thin then thick (<c>|.</c>).</summary>
    Final,
    /// <summary>Repeat-start barline (<c>|:</c>).</summary>
    RepeatStart,
    /// <summary>Repeat-end barline (<c>:|</c>).</summary>
    RepeatEnd,
    /// <summary>Back-to-back repeat barline: end then start (<c>:|:</c>).</summary>
    RepeatBoth,
    /// <summary>Dashed barline (LilyPond <c>\bar "!"</c>).</summary>
    Dashed
}

/// <summary>
/// Represents a single measure (bar) containing music items.
/// </summary>
/// <remarks>
/// A measure is the fundamental unit for:
/// - Duration validation (total should match time signature)
/// - Layout calculation (measures are not split across lines)
/// - Caching (measures can be cached by source position)
/// </remarks>
public sealed record Measure
{
    /// <summary>The music items in this measure.</summary>
    public ImmutableArray<MusicItem> Items { get; init; }

    /// <summary>Barline at the start of this measure (for repeat starts).</summary>
    public BarlineType StartBarline { get; }

    /// <summary>Barline at the end of this measure.</summary>
    public BarlineType EndBarline { get; }

    /// <summary>Optional section label (e.g., "A", "B", "Coda").</summary>
    public string? SectionLabel { get; init; }

    /// <summary>If true, force a line break after this measure.</summary>
    public bool HasBreakAfter { get; }

    /// <summary>
    /// Line break permission after this measure.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/constrained-breaking.hh:74 break_permission_
    /// LILYPOND-REF: scm/define-grob-properties.scm — line-break-permission
    /// Allow = normal break point, Forbid = cannot break here, Force = must break here.
    /// When Force, HasBreakAfter is also true for backward compatibility.
    /// </remarks>
    public BreakPermission LineBreakPermission { get; }

    /// <summary>
    /// Page break permission after this measure (raw, before <c>min_permission</c> propagation).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/constrained-breaking.hh:75 page_permission_
    /// LILYPOND-REF: scm/define-grob-properties.scm — page-break-permission
    /// Use <see cref="EffectivePagePermission"/> to obtain the value after LP's
    /// <c>min_permission(line, page)</c> chain.
    /// </remarks>
    public BreakPermission PageBreakPermission { get; }

    /// <summary>
    /// Page turn permission after this measure (raw, before <c>min_permission</c> propagation).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/constrained-breaking.hh:76 turn_permission_
    /// LILYPOND-REF: scm/define-grob-properties.scm — page-turn-permission
    /// Use <see cref="EffectiveTurnPermission"/> to obtain the value after LP's
    /// <c>min_permission(page, turn)</c> chain (which itself derives from line).
    /// </remarks>
    public BreakPermission PageTurnPermission { get; }

    /// <summary>
    /// Page break permission after applying LP's chained <c>min_permission(line, page)</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:530-535 — page perm constrained by line perm.
    /// </remarks>
    public BreakPermission EffectivePagePermission =>
        Layout.BreakPermissionExtensions.MinPermission(LineBreakPermission, PageBreakPermission);

    /// <summary>
    /// Page turn permission after applying LP's chained <c>min_permission</c>:
    /// turn perm is constrained by the (already chained) page perm, which is constrained by line perm.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:534-535 — turn perm constrained by chained page perm.
    /// </remarks>
    public BreakPermission EffectiveTurnPermission =>
        Layout.BreakPermissionExtensions.MinPermission(EffectivePagePermission, PageTurnPermission);

    /// <summary>
    /// Penalty for breaking the line after this measure.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:112-113 break_penalty_
    /// Added to demerits in the DP when a break occurs after this measure.
    /// </remarks>
    public double BreakPenalty { get; }

    /// <summary>Source start position for caching and incremental updates.</summary>
    public int SourceStart { get; }

    /// <summary>Source end position for caching and incremental updates.</summary>
    public int SourceEnd { get; }

    /// <summary>Source offset of this measure's section-label declaration (0 = none),
    /// so the section mark can carry a data-pos that jumps to <c>section X</c>
    /// instead of the measure's music. Falls back to SourceStart when unset.</summary>
    public int SectionLabelPosition { get; init; }

    /// <summary>
    /// True when this measure is an anacrusis (pickup) declared with <c>partial</c>:
    /// it is shorter than the meter on purpose. A LEADING pickup (index 0) is bar 0
    /// for numbering, so the first full bar is numbered 1, not 2.
    /// LILYPOND-REF: lily/bar-number-engraver.cc — \partial leaves the pickup
    /// uncounted (currentBarNumber reaches 1 only at the first full measure).
    /// </summary>
    public bool IsPickup { get; }

    /// <summary>Creates a measure from its items, barlines, and break/pickup metadata.</summary>
    public Measure(
        ImmutableArray<MusicItem> items,
        BarlineType startBarline,
        BarlineType endBarline,
        string? sectionLabel,
        int sourceStart,
        int sourceEnd,
        bool hasBreakAfter = false,
        BreakPermission lineBreakPermission = BreakPermission.Allow,
        double breakPenalty = 0,
        BreakPermission pageBreakPermission = BreakPermission.Allow,
        BreakPermission pageTurnPermission = BreakPermission.Allow,
        int sectionLabelPosition = 0,
        bool isPickup = false)
    {
        Items = items;
        StartBarline = startBarline;
        EndBarline = endBarline;
        SectionLabel = sectionLabel;
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
        SectionLabelPosition = sectionLabelPosition;
        IsPickup = isPickup;
        // Derive permission: hasBreakAfter implies Force for backward compatibility
        LineBreakPermission = hasBreakAfter ? BreakPermission.Force : lineBreakPermission;
        HasBreakAfter = hasBreakAfter || LineBreakPermission == BreakPermission.Force;
        BreakPenalty = breakPenalty;
        PageBreakPermission = pageBreakPermission;
        PageTurnPermission = pageTurnPermission;
    }

    /// <summary>
    /// Total duration of all items in this measure.
    /// </summary>
    public Fraction TotalDuration
    {
        get
        {
            var total = Fraction.Zero;
            foreach (var item in Items)
                total = total + item.Duration;
            return total;
        }
    }

    /// <summary>
    /// Validates that the measure duration matches the expected time signature.
    /// </summary>
    public bool ValidateDuration(Fraction expectedDuration)
        => TotalDuration == expectedDuration;
}