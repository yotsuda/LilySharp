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
/// Type of barline.
/// </summary>
public enum BarlineType
{
    None,
    Single,       // |
    Double,       // ||
    Final,        // |.
    RepeatStart,  // |:
    RepeatEnd,    // :|
    RepeatBoth    // :|:
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
    public ImmutableArray<MusicItem> Items { get; }

    /// <summary>Barline at the start of this measure (for repeat starts).</summary>
    public BarlineType StartBarline { get; }

    /// <summary>Barline at the end of this measure.</summary>
    public BarlineType EndBarline { get; }

    /// <summary>Optional section label (e.g., "A", "B", "Coda").</summary>
    public string? SectionLabel { get; }

    /// <summary>If true, force a line break after this measure.</summary>
    public bool HasBreakAfter { get; }

    /// <summary>Source start position for caching and incremental updates.</summary>
    public int SourceStart { get; }

    /// <summary>Source end position for caching and incremental updates.</summary>
    public int SourceEnd { get; }

    public Measure(
        ImmutableArray<MusicItem> items,
        BarlineType startBarline,
        BarlineType endBarline,
        string? sectionLabel,
        int sourceStart,
        int sourceEnd,
        bool hasBreakAfter = false)
    {
        Items = items;
        StartBarline = startBarline;
        EndBarline = endBarline;
        SectionLabel = sectionLabel;
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
        HasBreakAfter = hasBreakAfter;
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