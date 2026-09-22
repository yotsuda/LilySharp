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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The above- and below-staff stack memos' KEY, field by field: every program field a
/// <c>SystemEntry</c> keeps is compared by <c>TryMatch</c>, and every field a <c>Probe</c>
/// gathers reaches the entry <c>ToEntry</c> stores.
/// </summary>
/// <remarks>
/// ⚠️ WHY BY REFLECTION. The behavioural nets (<c>OutsideStaffStackMemoTests</c>) drive the
/// pass with texts, bar numbers and tuplets; the key holds eleven families above and four
/// below. POISONED (session 512): dropping the articulations from the above comparison, or
/// taking any line-group structure below for the stored one, left the whole suite and the
/// owner's corpus green — a false hit replays the stored outputs, so both are soundness
/// holes that nothing observed. Walking the fields makes a family added later part of this
/// net without anyone remembering to write it.
/// </remarks>
public sealed class StackMemoKeyTests
{
    public static IEnumerable<object[]> Memos() =>
    [
        [typeof(AboveStackMemo)],
        [typeof(BelowStackMemo)],
    ];

    private const BindingFlags Fields = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>The program fields: everything an entry keeps except its outputs.</summary>
    private static FieldInfo[] ProgramFields(Type memo)
        => memo.GetNestedType("SystemEntry", BindingFlags.NonPublic | BindingFlags.Public)!
            .GetFields(Fields).Where(f => !f.Name.StartsWith("Out", StringComparison.Ordinal)).ToArray();

    [Theory]
    [MemberData(nameof(Memos))]
    public void EveryProgramField_TakesPartInTheMatch(Type memoType)
    {
        var fields = ProgramFields(memoType);
        Assert.True(fields.Length >= 8, $"{memoType.Name}: found only {fields.Length} program fields");

        // Liveness: an empty probe matches an empty entry, so a decline below is the field's.
        {
            var (memo, probe) = Fresh(memoType);
            Store(memo, NewEntry(memoType));
            Assert.True(TryMatch(memo, probe), $"{memoType.Name}: an empty probe must match an empty entry");
        }

        foreach (var field in fields)
        {
            var (memo, probe) = Fresh(memoType);
            var entry = NewEntry(memoType);
            field.SetValue(entry, OneValue(field.FieldType));
            Store(memo, entry);
            Assert.False(TryMatch(memo, probe),
                $"{memoType.Name}.SystemEntry.{field.Name} differs from the probe and the memo still hit");
        }
    }

    [Theory]
    [MemberData(nameof(Memos))]
    public void EveryProbeField_ReachesTheStoredEntry(Type memoType)
    {
        var (memo, probe) = Fresh(memoType);
        var probeType = probe.GetType();
        foreach (var field in ProgramFields(memoType))
        {
            if (field.FieldType.IsArray && field.FieldType.GetElementType()!.IsArray)
            {
                // A group list: one group of one ordinal, held flat with its run length.
                var flat = (IList)probeType.GetField(field.Name, Fields)!.GetValue(probe)!;
                var lengths = (IList)probeType.GetField(
                    field.Name.TrimEnd('s') + "Lengths", Fields)!.GetValue(probe)!;
                flat.Add(0);
                lengths.Add(1);
            }
            else if (field.FieldType.IsArray)
            {
                var list = (IList)probeType.GetField(field.Name, Fields)!.GetValue(probe)!;
                list.Add(Element(field.FieldType.GetElementType()!));
            }
            else
            {
                probeType.GetField(field.Name, Fields)!.SetValue(probe, OneScalar(field.FieldType));
            }
        }

        var entry = probeType.GetMethod("ToEntry")!.Invoke(probe, null)!;
        foreach (var field in ProgramFields(memoType))
            if (field.FieldType.IsArray)
                Assert.True(((Array)field.GetValue(entry)!).Length == 1,
                    $"{memoType.Name}.Probe.ToEntry dropped {field.Name}");
        Store(memo, entry);
        Assert.True(TryMatch(memo, probe), $"{memoType.Name}: the probe does not match its own entry");
    }

    // ---------- helpers ----------

    private static (object Memo, object Probe) Fresh(Type memoType)
    {
        var probeType = memoType.GetNestedType("Probe", BindingFlags.NonPublic | BindingFlags.Public)!;
        return (Activator.CreateInstance(memoType, nonPublic: true)!, Activator.CreateInstance(probeType)!);
    }

    private static object NewEntry(Type memoType)
        => Activator.CreateInstance(memoType.GetNestedType("SystemEntry",
            BindingFlags.NonPublic | BindingFlags.Public)!)!;

    private static void Store(object memo, object entry)
        => memo.GetType().GetMethod("Store")!.Invoke(memo, [0, entry]);

    private static bool TryMatch(object memo, object probe)
        => (bool)memo.GetType().GetMethod("TryMatch", [typeof(int), probe.GetType()])!
            .Invoke(memo, [0, probe])!;

    /// <summary>A value of <paramref name="type"/> that differs from what an empty probe
    /// holds: a one-element array, or a scalar other than the default.</summary>
    private static object OneValue(Type type)
    {
        if (type.IsArray)
        {
            var arr = Array.CreateInstance(type.GetElementType()!, 1);
            arr.SetValue(Element(type.GetElementType()!), 0);
            return arr;
        }
        return OneScalar(type);
    }

    private static object OneScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(double)) return 1.0;
        if (t == typeof(int)) return 1;
        if (t == typeof(bool)) return true;
        if (t == typeof(object)) return new object();
        throw new InvalidOperationException($"no non-default value for program field type {type}");
    }

    private static object? Element(Type type)
    {
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
        if (type == typeof(object)) return new object();
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
