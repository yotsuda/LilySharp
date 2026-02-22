using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A single grob property override collected from the source.
/// LILYPOND-REF: lily/context-property.cc - push (override), pop (revert)
/// </summary>
/// <param name="GrobType">Grob type name (e.g., "Stem", "Beam", "NoteHead")</param>
/// <param name="PropertyName">Property name (e.g., "length", "thickness", "direction")</param>
/// <param name="Value">Override value as string (parsed at usage site)</param>
/// <param name="MeasureIndex">Measure where the override appears</param>
/// <param name="ItemIndex">Item index within the measure</param>
/// <param name="IsOnce">Whether this is a \once override (applies to next item only)</param>
public sealed record GrobOverride(
    string GrobType,
    string PropertyName,
    string Value,
    int MeasureIndex,
    int ItemIndex,
    bool IsOnce = false);

/// <summary>
/// A grob property revert collected from the source.
/// </summary>
public sealed record GrobRevert(
    string GrobType,
    string PropertyName,
    int MeasureIndex,
    int ItemIndex);

/// <summary>
/// Resolves grob property values at a given point in the score,
/// combining default values with user overrides.
/// LILYPOND-REF: lily/grob-property.cc - property resolution chain
/// </summary>
public sealed class GrobPropertyResolver
{
    // Active overrides: GrobType → PropertyName → value
    private readonly Dictionary<string, Dictionary<string, string>> _activeOverrides = new();

    // Once overrides pending application: cleared after one item
    private readonly Dictionary<string, Dictionary<string, string>> _onceOverrides = new();

    private readonly ImmutableArray<GrobOverride> _overrides;
    private readonly ImmutableArray<GrobRevert> _reverts;

    public GrobPropertyResolver(
        ImmutableArray<GrobOverride> overrides,
        ImmutableArray<GrobRevert> reverts)
    {
        _overrides = overrides;
        _reverts = reverts;
    }

    /// <summary>
    /// Returns true if there are any overrides at all (optimization).
    /// </summary>
    public bool HasOverrides => !_overrides.IsDefaultOrEmpty;

    /// <summary>
    /// Advances the resolver to the given position, applying/reverting as needed.
    /// Must be called in order (measure, item) for correct state.
    /// </summary>
    public void AdvanceTo(int measureIndex, int itemIndex)
    {
        // Clear any pending once overrides from previous item
        if (_onceOverrides.Count > 0)
        {
            foreach (var (grobType, props) in _onceOverrides)
            {
                if (_activeOverrides.TryGetValue(grobType, out var active))
                {
                    foreach (var propName in props.Keys)
                        active.Remove(propName);
                }
            }
            _onceOverrides.Clear();
        }

        // Apply overrides at this position
        foreach (var ov in _overrides)
        {
            if (ov.MeasureIndex == measureIndex && ov.ItemIndex == itemIndex)
            {
                if (!_activeOverrides.TryGetValue(ov.GrobType, out var dict))
                {
                    dict = new Dictionary<string, string>();
                    _activeOverrides[ov.GrobType] = dict;
                }
                dict[ov.PropertyName] = ov.Value;

                if (ov.IsOnce)
                {
                    if (!_onceOverrides.TryGetValue(ov.GrobType, out var onceDict))
                    {
                        onceDict = new Dictionary<string, string>();
                        _onceOverrides[ov.GrobType] = onceDict;
                    }
                    onceDict[ov.PropertyName] = ov.Value;
                }
            }
        }

        // Apply reverts at this position
        foreach (var rv in _reverts)
        {
            if (rv.MeasureIndex == measureIndex && rv.ItemIndex == itemIndex)
            {
                if (_activeOverrides.TryGetValue(rv.GrobType, out var dict))
                    dict.Remove(rv.PropertyName);
            }
        }
    }

    /// <summary>
    /// Gets an overridden double value for a grob property, or null if not overridden.
    /// </summary>
    public double? GetDouble(string grobType, string propertyName)
    {
        if (_activeOverrides.TryGetValue(grobType, out var dict) &&
            dict.TryGetValue(propertyName, out var value) &&
            double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
        {
            return result;
        }
        return null;
    }

    /// <summary>
    /// Gets an overridden integer value for a grob property, or null if not overridden.
    /// </summary>
    public int? GetInt(string grobType, string propertyName)
    {
        if (_activeOverrides.TryGetValue(grobType, out var dict) &&
            dict.TryGetValue(propertyName, out var value) &&
            int.TryParse(value, out int result))
        {
            return result;
        }
        return null;
    }

    /// <summary>
    /// Gets an overridden string value for a grob property, or null if not overridden.
    /// </summary>
    public string? GetString(string grobType, string propertyName)
    {
        if (_activeOverrides.TryGetValue(grobType, out var dict) &&
            dict.TryGetValue(propertyName, out var value))
        {
            return value;
        }
        return null;
    }

    /// <summary>
    /// Gets an overridden boolean value for a grob property, or null if not overridden.
    /// Recognizes: "true"/"1"/"yes" = true, "false"/"0"/"no" = false.
    /// </summary>
    public bool? GetBool(string grobType, string propertyName)
    {
        var s = GetString(grobType, propertyName);
        if (s == null) return null;
        return s.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => true,
            "false" or "0" or "no" => false,
            _ => null
        };
    }

    /// <summary>
    /// Checks if a specific grob property is currently overridden.
    /// </summary>
    public bool IsOverridden(string grobType, string propertyName)
    {
        return _activeOverrides.TryGetValue(grobType, out var dict) &&
               dict.ContainsKey(propertyName);
    }

    /// <summary>
    /// Creates an empty resolver (no overrides).
    /// </summary>
    public static GrobPropertyResolver Empty { get; } =
        new(ImmutableArray<GrobOverride>.Empty, ImmutableArray<GrobRevert>.Empty);
}
