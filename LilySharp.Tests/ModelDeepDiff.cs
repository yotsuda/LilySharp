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

namespace LilySharp.Tests;

/// <summary>
/// Structural equality over a whole object graph, positions included — unlike
/// <c>MeasureContentKey</c>, which deliberately excludes them. Record
/// <c>Equals</c> cannot be trusted here (ImmutableArray members compare by
/// reference), so everything is walked: primitives by value, enumerables
/// element-wise, everything else property-by-property. Returns a describing
/// path for the FIRST difference, or null when the graphs match. Shared by the
/// collect-resume completeness nets (CollectResumeTests, CollectEditResumeTests).
/// </summary>
internal static class ModelDeepDiff
{
    private static readonly Dictionary<Type, PropertyInfo[]> Props = new();

    public static string? FirstDifference(object? a, object? b, string path)
    {
        if (ReferenceEquals(a, b))
            return null;
        if (a is null || b is null)
            return $"{path}: {Describe(a)} vs {Describe(b)}";
        var type = a.GetType();
        if (type != b.GetType())
            return $"{path}: type {a.GetType().Name} vs {b.GetType().Name}";

        if (type.IsPrimitive || type.IsEnum || a is string || a is decimal)
            return a.Equals(b) ? null : $"{path}: {a} vs {b}";
        if (a is double da && b is double db)
            return da.Equals(db) ? null : $"{path}: {da:R} vs {db:R}";

        if (a is IEnumerable ea && b is IEnumerable eb)
            return EnumerableDifference(ea, eb, path);

        var props = GetProps(type);
        if (props.Length == 0)
            return a.Equals(b) ? null : $"{path}: {a} vs {b}";
        foreach (var p in props)
        {
            object? va, vb;
            try { va = p.GetValue(a); }
            catch (Exception ex) { va = $"<threw {ex.GetBaseException().GetType().Name}>"; }
            try { vb = p.GetValue(b); }
            catch (Exception ex) { vb = $"<threw {ex.GetBaseException().GetType().Name}>"; }
            var diff = FirstDifference(va, vb, $"{path}.{p.Name}");
            if (diff != null)
                return diff;
        }
        return null;
    }

    private static string? EnumerableDifference(IEnumerable a, IEnumerable b, string path)
    {
        List<object?> la, lb;
        try { la = a.Cast<object?>().ToList(); }
        catch (InvalidOperationException) { la = new(); } // default ImmutableArray
        try { lb = b.Cast<object?>().ToList(); }
        catch (InvalidOperationException) { lb = new(); }
        if (la.Count != lb.Count)
            return $"{path}: count {la.Count} vs {lb.Count}";
        for (int i = 0; i < la.Count; i++)
        {
            var diff = FirstDifference(la[i], lb[i], $"{path}[{i}]");
            if (diff != null)
                return diff;
        }
        return null;
    }

    private static PropertyInfo[] GetProps(Type type)
    {
        if (Props.TryGetValue(type, out var cached))
            return cached;
        var props = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();
        Props[type] = props;
        return props;
    }

    private static string Describe(object? v) => v?.ToString() ?? "null";
}
