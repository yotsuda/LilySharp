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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The drift net of the suffix splice's position shifter
/// (<see cref="CollectTailShifter"/>): the shifter re-homes a HAND-LISTED set
/// of position fields per adopted type, so a NEW position field added to any
/// adopted type would silently ship stale data-pos in spliced output. This net
/// reflects over every adopted type, flags every position-suspicious property
/// (named *Position / Source*), and fails unless it is either in the shifter's
/// known-shifted inventory or in the known-NOT-a-position allowlist — forcing
/// the author of a new field to decide consciously, in both places.
/// </summary>
public class CollectTailShifterTests
{
    // Properties that LOOK positional but are not source offsets:
    // staff-vertical positions and table indices (SourceIndex is an index into
    // another side table — shifting it would corrupt the reference). Name-wide
    // entries use a null type; oddballs are type-qualified so a future genuine
    // source offset with the same name on another type still trips the net.
    private static readonly HashSet<(Type? Type, string Name)> NotAPosition = new()
    {
        (null, "StaffPosition"),
        (null, "SourceIndex"),
        (typeof(ArpeggioItem), "MinStaffPosition"),
        (typeof(ArpeggioItem), "MaxStaffPosition"),
    };

    // The shifter's inventory, type by type (keep in step with
    // CollectTailShifter — this net exists so the two cannot drift apart
    // silently). MusicItem subtypes inherit the base's SourcePosition.
    private static readonly Dictionary<Type, string[]> Shifted = new()
    {
        [typeof(Measure)] = new[]
        {
            "SourceStart", "SourceEnd", "EndHighlightAliases", "SectionLabelPosition",
        },
        [typeof(ChordNoteInfo)] = new[] { "SourcePosition" },
        [typeof(GraceNoteInfo)] = Array.Empty<string>(),
        [typeof(DynamicItem)] = new[] { "SourcePosition" },
        [typeof(ArticulationItem)] = new[] { "SourcePosition" },
        [typeof(GraceNoteItem)] = new[] { "SourcePosition" },
        [typeof(MusicMarkItem)] = new[] { "SourcePosition" },
        [typeof(CustomTextItem)] = new[] { "SourcePosition" },
        [typeof(VoltaBracketItem)] = new[] { "SourcePosition" },
        [typeof(TupletBracketItem)] = new[] { "SourcePosition" },
        [typeof(ArpeggioItem)] = new[] { "SourcePosition" },
        [typeof(FiguredBassItem)] = new[] { "SourcePosition" },
        [typeof(PercentRepeatItem)] = new[] { "SourcePosition" },
        [typeof(CrossStaffItem)] = new[] { "SourcePosition" },
        [typeof(GrobOverride)] = Array.Empty<string>(),
        [typeof(GrobRevert)] = Array.Empty<string>(),
        [typeof(MeasureCollector.PitchTraceEntry)] = new[] { "Position" },
        [typeof(NavigationMarkPlacementWarning)] = new[] { "SourcePosition" },
        [typeof(TieTargetWarning)] = new[] { "SourcePosition" },
        [typeof(UnpairedSlurWarning)] = new[] { "SourcePosition" },
        [typeof(UnpairedBeamWarning)] = new[] { "SourcePosition" },
        [typeof(CueSpanBoundaryWarning)] = new[] { "SourcePosition" },
        [typeof(ChordNameItem)] = new[] { "SourcePosition" },
        // The trill-spanner event tuple is covered by hand in ShiftSideEntry
        // (ValueTuple fields are not reflectable by name); its shape change
        // would break the tuple pattern there at compile time.
    };

    /// <summary>
    /// Every element type of <c>CumulativeSideTables()</c> must be accounted for by the
    /// shifter — either as a <see cref="MusicItem"/> (which rides the base's
    /// SourcePosition) or by name in <see cref="Shifted"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ The inventory above is HAND-WRITTEN, so it can only validate the types it already
    /// lists: it is structurally blind to a table that was added and never wired. That is
    /// not hypothetical — <c>UnpairedBeamWarning</c> was added to CumulativeSideTables and
    /// NOT to ShiftSideEntry, whose <c>default:</c> throws, so any checkpoint/resume
    /// carrying one would have died. The whole suite stayed green because nothing drives an
    /// unpaired beam through an incremental edit.
    ///
    /// This test closes that hole from the other side: the list of types comes from the
    /// COLLECTOR at runtime (an empty <c>List&lt;T&gt;</c> still reports its T), so a new
    /// side table fails here the moment it is added, whatever it holds.
    /// </remarks>
    [Fact]
    public void EveryCumulativeSideTableTypeIsAccountedFor()
    {
        var elementTypes = new MeasureCollector().CumulativeSideTables()
            .Select(t => t.GetType())
            .Where(t => t.IsGenericType)
            .Select(t => t.GetGenericArguments()[0])
            .Distinct()
            .ToList();

        Assert.NotEmpty(elementTypes);

        var missing = elementTypes
            .Where(t => !typeof(MusicItem).IsAssignableFrom(t))
            .Where(t => !Shifted.ContainsKey(t))
            // The trill-spanner event is a ValueTuple, shifted by hand in ShiftSideEntry
            // (Item4 = the source position). It cannot join the inventory above, which
            // reflects fields BY NAME, so it is named here instead of silently skipped.
            .Where(t => !(t.IsGenericType
                          && t.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,,,>)))
            .Select(t => t.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            "these cumulative side-table element types are not accounted for by "
            + "CollectTailShifter.ShiftSideEntry (whose default: throws) nor listed in the "
            + "inventory above: " + string.Join(", ", missing));
    }

    [Fact]
    public void ShifterInventory_CoversEveryPositionField()
    {
        var failures = new List<string>();

        // Every MusicItem subtype rides the base's SourcePosition (plus the
        // per-type nesting the shifter special-cases: ChordItem.Notes).
        var itemTypes = typeof(MusicItem).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(MusicItem).IsAssignableFrom(t));
        foreach (var type in itemTypes)
            Check(type, new[] { "SourcePosition" }, failures);

        foreach (var (type, shifted) in Shifted)
            Check(type, shifted, failures);

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void Check(Type type, string[] shifted, List<string> failures)
    {
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // A source offset is an int (or a collection of ints); an enum or a
            // double with a positional-sounding name is something else.
            bool intish = prop.PropertyType == typeof(int)
                || prop.PropertyType == typeof(int?)
                || prop.PropertyType == typeof(System.Collections.Immutable.ImmutableArray<int>);
            bool suspicious = intish
                && (prop.Name.EndsWith("Position", StringComparison.Ordinal)
                    || prop.Name.StartsWith("Source", StringComparison.Ordinal)
                    || prop.Name == "EndHighlightAliases");
            if (!suspicious
                || NotAPosition.Contains((null, prop.Name))
                || NotAPosition.Contains((type, prop.Name)))
                continue;
            if (!shifted.Contains(prop.Name))
                failures.Add(
                    $"{type.Name}.{prop.Name} looks position-bearing but is not in the shifter's " +
                    "inventory — wire it into CollectTailShifter (or allowlist it here as not-a-position).");
        }
    }

    [Fact]
    public void Window_ShiftsSuffixKeepsPrefixDeclinesWindow()
    {
        var w = new CollectTailShifter.Window(Prefix: 100, SuffixStart: 120, Delta: 3);

        Assert.True(w.TryShift(0, out int p0));      // sentinel "none"
        Assert.Equal(0, p0);
        Assert.True(w.TryShift(-1, out int pm1));    // sentinel "fall back"
        Assert.Equal(-1, pm1);
        Assert.True(w.TryShift(99, out int pre));    // last stable prefix char
        Assert.Equal(99, pre);
        Assert.False(w.TryShift(100, out _));        // first changed char = window
        Assert.False(w.TryShift(119, out _));        // last window char
        Assert.True(w.TryShift(120, out int suf));   // first suffix char
        Assert.Equal(123, suf);
    }
}
