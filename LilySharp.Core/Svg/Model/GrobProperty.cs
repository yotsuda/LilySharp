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
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A single grob property override collected from the source.
/// LILYPOND-REF: lily/context-property.cc - push (override), pop (revert)
/// </summary>
/// <param name="GrobType">Grob type name (e.g., "Stem", "Beam", "NoteHead")</param>
/// <param name="PropertyName">Property name (e.g., "length", "thickness", "direction")</param>
/// <param name="Value">
/// The overriding value, TYPED at collection. It used to be the token's text, reparsed
/// at each use site; docs/VALUE_SITE_AUDIT.md §2 counted five such sites, one of which
/// (StaffSpacingParameters) parsed it without going through this resolver at all.
/// </param>
/// <param name="MeasureIndex">Measure where the override appears</param>
/// <param name="ItemIndex">Item index within the measure</param>
/// <param name="IsOnce">Whether this is a \once override (applies to next item only)</param>
public sealed record GrobOverride(
    string GrobType,
    string PropertyName,
    LysValue Value,
    int MeasureIndex,
    int ItemIndex,
    bool IsOnce = false,
    // Scope tags: null = broadest (all staves / all voices). A staff-scoped override
    // (part body, section/voice music) carries StaffIndex; a voice-scoped one (a
    // `voice {}` block) also carries VoiceIndex. A global override leaves both null.
    int? StaffIndex = null,
    int? VoiceIndex = null);

/// <summary>
/// A grob property revert collected from the source.
/// </summary>
public sealed record GrobRevert(
    string GrobType,
    string PropertyName,
    int MeasureIndex,
    int ItemIndex,
    int? StaffIndex = null,
    int? VoiceIndex = null);

/// <summary>
/// The grob properties an <c>override</c> / <c>revert</c> actually reaches — the whole
/// vocabulary the engine reads, and the single home of that list.
/// </summary>
/// <remarks>
/// <para>
/// The GRAMMAR accepts any <c>Grob.property = value</c>: the value is typed at collection
/// (<see cref="LysValue"/>, docs/VALUE_SITE_AUDIT.md §2) but the NAME is not checked
/// anywhere. Until this list existed, writing a property the engine does not read —
/// <c>Beam.thickness</c>, <c>Stem.direction</c>, or the mis-cased <c>stem.direction</c> —
/// engraved byte-for-byte identically to writing nothing at all, and said nothing.
/// MEASURED before this list was added: eight spellings, all "No errors found", all
/// producing an SVG identical to the no-override control.
/// </para>
/// <para>
/// ⚠️ This is a list of what is IMPLEMENTED, not of what is spellable. It is expected to
/// GROW, and growing it only ever turns a diagnostic OFF: a file rejected today because
/// its property is unimplemented compiles unchanged the day it lands. That asymmetry is
/// why the unimplemented case is an error rather than silence — silence could never be
/// tightened afterwards, while an error can always be relaxed.
/// </para>
/// <para>
/// The call sites that read these (SharedRenderer.Noteheads.cs ×4) pass the pair as
/// their lookup KEY, so they are readings of this list rather than second spellings of
/// it. Adding a property means adding its reader AND its row here, in one commit — and
/// the converse holds too: a reader that is disabled has no row (see the force-hshift
/// note below), or the row is a silent no-op the diagnostic claims cannot exist.
/// </para>
/// </remarks>
public static class SupportedGrobOverrides
{
    private static readonly HashSet<(string Grob, string Property)> Pairs = new()
    {
        // \hideNotes' two inked members — the pair audit/lp-regression/lys/complex-once.lys
        // mirrors (ly/property-init.ly). Read by SharedRenderer.Noteheads.
        ("NoteHead", "transparent"),
        ("Stem", "transparent"),
        // Colour, named or #rgb/#rrggbb. Read by SharedRenderer.Noteheads.ResolveColor
        // (four call sites). These readers were live for as long as the list existed, yet
        // the rows were missing — LYS1029 called a working feature unsupported while the
        // score still drew in colour (best-effort output). GRAMMAR_AUDIT §4.2; the
        // validator's own MEASURED note had simply never tried `color`.
        ("NoteHead", "color"),
        ("Stem", "color"),
        // NoteColumn.force-hshift is deliberately ABSENT (removed 2026-08-23, user-approved
        // pair decision with §4.2 above): its reader exists but sits behind
        // ElementCoordinator.ForceHshiftEnabled = false — the current implementation
        // cannot do what the property is for — so the spelling was the silent no-op this
        // list exists to prevent. A row here means IMPLEMENTED; the row returns in the
        // same commit that lands the per-voice implementation and flips that flag.
    };

    /// <summary>True if the engine reads this grob property. Case-SENSITIVE, like every
    /// other name in the language (grob names are PascalCase, properties are lisp-case).</summary>
    public static bool Contains(string grobType, string propertyName)
        => Pairs.Contains((grobType, propertyName));

    /// <summary>The supported pairs as <c>Grob.property</c>, ordered, for diagnostics.</summary>
    public static IEnumerable<string> Spellings
        => Pairs.Select(p => $"{p.Grob}.{p.Property}").OrderBy(s => s, StringComparer.Ordinal);
}

/// <summary>
/// Resolves grob property values at a given point in the score,
/// combining default values with user overrides.
/// LILYPOND-REF: lily/grob-property.cc - property resolution chain
/// </summary>
/// <remarks>
/// The resolver is a replayable timeline, not a one-shot cursor:
/// <list type="bullet">
/// <item>Advancing FORWARD applies every override/revert in the range
/// <c>(previous position, target]</c> — a consumer that skips item indices
/// (e.g. the collision columns) still sees everything written before its
/// position, matching LilyPond's "an override takes effect from its timewise
/// position onward" (lily/context-property.cc).</item>
/// <item>Advancing BACKWARD (each new voice/staff/render pass restarts at its
/// first measure) resets and replays from the top, so state from a previous
/// pass can never leak into an EARLIER position of the next one.</item>
/// <item>A <c>\once</c> override is a push/pop: moving past its position
/// restores the value it displaced (LILYPOND-REF: lily/context-property.cc
/// execute_general_pushpop_property) — it must not erase an outer persistent
/// <c>\override</c> of the same property.</item>
/// </list>
/// </remarks>
public sealed class GrobPropertyResolver
{
    // Active overrides: GrobType → PropertyName → value
    private readonly Dictionary<string, Dictionary<string, LysValue>> _activeOverrides = new();

    // What each \once at the CURRENT position displaced; restored (popped) as
    // soon as the timeline moves past that position. Previous == null means
    // the property was not set before the \once.
    private readonly List<(string GrobType, string PropertyName, LysValue? Previous)> _oncePops = new();

    private readonly ImmutableArray<GrobOverride> _overrides;

    // All overrides and reverts merged, ordered by (measure, item); at the
    // same position overrides apply before reverts (the pre-existing order).
    private readonly List<(int Measure, int Item, GrobOverride? Override, GrobRevert? Revert)> _events;
    private int _nextEvent;
    private int _measure = -1, _item = -1;

    /// <summary>Creates a resolver over the given override and revert timelines.</summary>
    public GrobPropertyResolver(
        ImmutableArray<GrobOverride> overrides,
        ImmutableArray<GrobRevert> reverts)
    {
        _overrides = overrides;
        _events = new List<(int, int, GrobOverride?, GrobRevert?)>(
            (overrides.IsDefaultOrEmpty ? 0 : overrides.Length)
            + (reverts.IsDefaultOrEmpty ? 0 : reverts.Length));
        if (!overrides.IsDefaultOrEmpty)
            foreach (var ov in overrides)
                _events.Add((ov.MeasureIndex, ov.ItemIndex, ov, null));
        if (!reverts.IsDefaultOrEmpty)
            foreach (var rv in reverts)
                _events.Add((rv.MeasureIndex, rv.ItemIndex, null, rv));
        // Stable sort keeps source order within a position and per kind;
        // Override (null Revert) sorts before Revert at the same position.
        _events = _events
            .OrderBy(e => e.Measure).ThenBy(e => e.Item).ThenBy(e => e.Override == null ? 1 : 0)
            .ToList();
    }

    /// <summary>
    /// Returns true if there are any overrides at all (optimization).
    /// </summary>
    public bool HasOverrides => !_overrides.IsDefaultOrEmpty;

    /// <summary>
    /// Moves the resolver to the given position: forward applies everything in
    /// <c>(previous, target]</c>; backward (a new pass) replays from the top;
    /// the same position is a no-op (a second staff/voice re-visiting the item
    /// keeps a pending <c>\once</c> visible).
    /// </summary>
    public void AdvanceTo(int measureIndex, int itemIndex)
    {
        int cmp = ComparePositions(measureIndex, itemIndex, _measure, _item);
        if (cmp == 0)
            return;
        if (cmp < 0)
        {
            // Rewind: replay the timeline from the top.
            _activeOverrides.Clear();
            _oncePops.Clear();
            _nextEvent = 0;
            _measure = -1;
            _item = -1;
        }

        // Leaving the current position pops what any \once there displaced.
        PopOnce();

        while (_nextEvent < _events.Count)
        {
            var e = _events[_nextEvent];
            if (ComparePositions(e.Measure, e.Item, measureIndex, itemIndex) > 0)
                break;
            _nextEvent++;

            if (e.Override is { } ov)
            {
                if (!_activeOverrides.TryGetValue(ov.GrobType, out var dict))
                {
                    dict = new Dictionary<string, LysValue>();
                    _activeOverrides[ov.GrobType] = dict;
                }
                if (ov.IsOnce)
                {
                    // A \once BEFORE the target position has already expired —
                    // apply-and-pop would be a no-op, so only a \once AT the
                    // target survives into the current state.
                    if (ComparePositions(e.Measure, e.Item, measureIndex, itemIndex) == 0)
                    {
                        _oncePops.Add((ov.GrobType, ov.PropertyName,
                            dict.TryGetValue(ov.PropertyName, out var prev) ? prev : null));
                        dict[ov.PropertyName] = ov.Value;
                    }
                }
                else
                {
                    dict[ov.PropertyName] = ov.Value;
                }
            }
            else if (e.Revert is { } rv)
            {
                if (_activeOverrides.TryGetValue(rv.GrobType, out var dict))
                    dict.Remove(rv.PropertyName);
            }
        }

        _measure = measureIndex;
        _item = itemIndex;
    }

    private void PopOnce()
    {
        if (_oncePops.Count == 0)
            return;
        // Restore in reverse so stacked onces on the same property unwind
        // to the value before the first of them.
        for (int i = _oncePops.Count - 1; i >= 0; i--)
        {
            var (grobType, propName, previous) = _oncePops[i];
            if (!_activeOverrides.TryGetValue(grobType, out var dict))
                continue;
            if (previous == null)
                dict.Remove(propName);
            else
                dict[propName] = previous;
        }
        _oncePops.Clear();
    }

    private static int ComparePositions(int m1, int i1, int m2, int i2)
        => m1 != m2 ? m1.CompareTo(m2) : i1.CompareTo(i2);

    /// <summary>
    /// Gets the overriding VALUE for a grob property, or null if not overridden.
    /// </summary>
    /// <remarks>
    /// The typed accessors below are readings OF this value, not four separate parses
    /// of a string. A consumer that needs a case this class does not name (a spacing
    /// sub-property, say) asks for the value and matches on it, rather than growing a
    /// sixth reader that reparses the text — which is what
    /// <c>StaffSpacingParameters.ApplyOverrides</c> had grown into.
    /// </remarks>
    public LysValue? GetValue(string grobType, string propertyName)
        => _activeOverrides.TryGetValue(grobType, out var dict)
           && dict.TryGetValue(propertyName, out var value)
            ? value
            : null;

    /// <summary>
    /// Gets an overridden double value for a grob property, or null if not overridden
    /// (or overridden with something that is not a number).
    /// </summary>
    public double? GetDouble(string grobType, string propertyName)
        => GetValue(grobType, propertyName)?.AsDouble;

    /// <summary>
    /// Gets an overridden integer value for a grob property, or null if not overridden
    /// (or overridden with something that is not an integer).
    /// </summary>
    public int? GetInt(string grobType, string propertyName)
        => GetValue(grobType, propertyName)?.AsInt;

    /// <summary>
    /// Gets an overridden value as the word it was written as, or null if not overridden.
    /// </summary>
    public string? GetString(string grobType, string propertyName)
        => GetValue(grobType, propertyName)?.AsText;

    /// <summary>
    /// Gets an overridden boolean value for a grob property, or null if not overridden.
    /// Recognizes: "true"/"1"/"yes" = true, "false"/"0"/"no" = false.
    /// </summary>
    public bool? GetBool(string grobType, string propertyName)
        => GetValue(grobType, propertyName)?.AsBool;

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

    /// <summary>
    /// A resolver seeing only the overrides/reverts in scope for the given staff and
    /// voice: a directive with a null <c>StaffIndex</c> applies to every staff; one tagged
    /// with a StaffIndex applies only to that staff; a non-null <c>VoiceIndex</c> narrows
    /// it further to that one voice. Lets each render pass (one staff-voice) see exactly
    /// the overrides that scope to it, so an override written in one staff/voice never
    /// bleeds into another.
    /// </summary>
    public static GrobPropertyResolver ForStaffVoice(
        ImmutableArray<GrobOverride> overrides, ImmutableArray<GrobRevert> reverts,
        int staffIndex, int voiceIndex)
    {
        bool InScope(int? s, int? v)
            => (s is null || s == staffIndex) && (v is null || v == voiceIndex);
        var o = overrides.IsDefaultOrEmpty ? overrides
            : overrides.Where(x => InScope(x.StaffIndex, x.VoiceIndex)).ToImmutableArray();
        var r = reverts.IsDefaultOrEmpty ? reverts
            : reverts.Where(x => InScope(x.StaffIndex, x.VoiceIndex)).ToImmutableArray();
        return new GrobPropertyResolver(o, r);
    }
}
