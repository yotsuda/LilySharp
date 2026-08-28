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
    public BarlineType StartBarline { get; init; }

    /// <summary>Barline at the end of this measure.</summary>
    public BarlineType EndBarline { get; init; }

    /// <summary>Optional section label (e.g., "A", "B", "Coda").</summary>
    public string? SectionLabel { get; init; }

    /// <summary>If true, force a line break after this measure. Derived from
    /// <see cref="LineBreakPermission"/> so it can never drift out of sync with it.</summary>
    public bool HasBreakAfter => LineBreakPermission == BreakPermission.Force;

    /// <summary>
    /// Line break permission after this measure.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/constrained-breaking.hh:74 break_permission_
    /// LILYPOND-REF: scm/define-grob-properties.scm — line-break-permission
    /// Allow = normal break point, Forbid = cannot break here, Force = must break here.
    /// When Force, HasBreakAfter is also true for backward compatibility.
    /// </remarks>
    public BreakPermission LineBreakPermission { get; init; }

    /// <summary>
    /// Page break permission after this measure (raw, before <c>min_permission</c> propagation).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/constrained-breaking.hh:75 page_permission_
    /// LILYPOND-REF: scm/define-grob-properties.scm — page-break-permission
    /// Use <see cref="EffectivePagePermission"/> to obtain the value after LP's
    /// <c>min_permission(line, page)</c> chain.
    /// </remarks>
    public BreakPermission PageBreakPermission { get; init; }

    /// <summary>
    /// Page turn permission after this measure (raw, before <c>min_permission</c> propagation).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/constrained-breaking.hh:76 turn_permission_
    /// LILYPOND-REF: scm/define-grob-properties.scm — page-turn-permission
    /// Use <see cref="EffectiveTurnPermission"/> to obtain the value after LP's
    /// <c>min_permission(page, turn)</c> chain (which itself derives from line).
    /// </remarks>
    public BreakPermission PageTurnPermission { get; init; }

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
    public double BreakPenalty { get; init; }

    /// <summary>Source start position for caching and incremental updates.</summary>
    public int SourceStart { get; init; }

    /// <summary>Source end position for caching and incremental updates.</summary>
    public int SourceEnd { get; init; }

    /// <summary>
    /// EXTRA source offsets a caret can be on to highlight this measure's END barline,
    /// beyond <see cref="SourceEnd"/> (the click target). A drawn barline can collapse
    /// several written ones — a phrase's trailing <c>:|</c>, the section <c>|</c>/<c>:|:</c>
    /// that confirms it, and a merged <c>|:</c> — and a caret on ANY of them should light
    /// it. So the renderer emits <c>data-pos</c> = <see cref="SourceEnd"/> (the click
    /// target — the outermost/section bar) plus <c>data-alt</c> = these aliases (the
    /// phrase bars that also live here). Empty for an ordinary single-source barline.
    /// </summary>
    public ImmutableArray<int> EndHighlightAliases { get; init; } = ImmutableArray<int>.Empty;

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
    public bool IsPickup { get; init; }

    /// <summary>
    /// True when this measure is an empty placeholder written as a bare barline gap —
    /// a leading <c>|</c>, a <c>| |</c> gap, or a trailing <c>| |</c> — with no music.
    /// It occupies a measure slot (so parts stay aligned) and renders as an empty bar.
    /// ⚠️ IT IS NOT ITEM-LESS: <c>MeasureBuilder.EmitEmptyMeasure</c> fills it with one
    /// full-measure SPACER of the meter in force, which is what makes it the same music
    /// as the <c>s1</c> an author would write (owner's decision 2026-08-28; it used to
    /// hold nothing, be worth zero, and be reported underfull). The flag survives the
    /// fill so a consumer can still tell an authored GAP from a bar of rests. Distinct
    /// from an intentionally empty track-fill measure (chords/lyrics alignment), which
    /// carries no flag and is silent.
    /// </summary>
    public bool IsEmptyPlaceholder { get; init; }

    /// <summary>
    /// True for the zero-width column a clef change written AFTER the last note leaves
    /// behind (clef-change-at-end.ly). LilyPond engraves that clef on the SAME
    /// break-align column as the piece's closing bar (the unbroken order is
    /// <c>… clef, staff-bar …</c>), so there is ONE bar moment: the collector moves the
    /// end barline onto the PREVIOUS measure and this one keeps only the clef, takes no
    /// width, and draws no bar — the clef hangs back into the closing gap the previous
    /// measure's spring reserved (SpacingRules.BoundaryClefAllowance).
    /// LILYPOND-REF: scm/define-grobs.scm:650-664 break-align-orders
    /// <para>
    /// ⚠️ LILYSHARP-OWN: THE FLAGGED ZERO-WIDTH MEASURE IS A TRANSLATION DEVICE. LilyPond
    /// has no trailing measure here at all — the clef and the bar are two grobs of one
    /// break-align column, and nothing else exists to suppress.
    ///   departs from: nothing line-for-line — the measure-based model has no column to
    ///     put the pair on, so the pair is spelled as "previous measure's bar + this
    ///     flagged remnant" instead.
    ///   goes away when: break-align columns become first-class and a measure stops being
    ///     the unit non-musical grobs hang on.
    ///   observed by: ClefChangeTests.TrailingClefChange_SharesTheClosingBarMoment (the
    ///     LP-measured page: one bar moment, clef 2.85 before it).
    /// </para>
    /// </summary>
    public bool IsTrailingClefColumn { get; init; }

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
        // Derive permission: hasBreakAfter implies Force for backward
        // compatibility. HasBreakAfter is a computed property off this value.
        LineBreakPermission = hasBreakAfter ? BreakPermission.Force : lineBreakPermission;
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