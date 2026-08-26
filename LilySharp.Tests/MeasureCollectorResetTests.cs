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
using System.Runtime.CompilerServices;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A MeasureCollector may be reused for more than one Collect call (Reset runs at
/// the start of each). Reset must clear per-run state so a later call does not carry
/// the previous one's data — PitchTrace in particular used to accumulate unbounded.
/// </summary>
[Trait("Category", "Unit")]
public class MeasureCollectorResetTests
{
    [Fact]
    public void Reuse_PitchTraceReflectsOnlyLatestCollect()
    {
        var collector = new MeasureCollector();

        collector.Collect(SyntaxTree.Parse("c4 d e f"));   // 4 pitches
        Assert.Equal(4, collector.PitchTrace.Count);

        collector.Collect(SyntaxTree.Parse("g4 a"));       // 2 pitches
        // Without Reset clearing _pitchTrace this would be 6 (accumulated).
        Assert.Equal(2, collector.PitchTrace.Count);
    }

    [Fact]
    public void StickyDuration_CarriesDots_ForNotesAndRests()
    {
        // An undurated note/rest takes the WHOLE previous duration — dots included:
        // `c8. c` and `r8. r` are each two dotted eighths. Until 2026-08-07 only the
        // value stuck and the inherited dots reset to 0 (the second r8. of
        // dot-rest-beam-trigger.ly lost its dot), while the semantic walk and the
        // MIDI/MusicXML exporters already carried the dots.
        // LILYPOND-REF: lily/parser.yy:3505-3514 optional_notemode_duration — default_duration_
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("time 12/16\nc8. c r8. r |"));
        var items = score.Voices[0].Measures[0].Items;
        var notes = items.OfType<NoteItem>().ToList();
        var rests = items.OfType<RestItem>().ToList();
        Assert.Equal(2, notes.Count);
        Assert.Equal(2, rests.Count);
        Assert.All(notes, n => Assert.Equal(1, n.Dots));
        Assert.All(rests, r => Assert.Equal(1, r.Dots));
    }

    [Fact]
    public void StickyDuration_AWrittenDurationDropsTheInheritedDots()
    {
        // Writing a NEW plain duration replaces the whole default: after `c4. c8 c4`
        // a bare `c` is an undotted quarter, not a dotted anything.
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("time 4/4\nc4. c8 c4 c |"));
        var notes = score.Voices[0].Measures[0].Items.OfType<NoteItem>().ToList();
        Assert.Equal(new[] { 1, 0, 0, 0 }, notes.Select(n => n.Dots).ToArray());
    }

    [Fact]
    public void StickyDuration_AGroupInheritsTheDots()
    {
        // `c4. << c e g >>` — an equal-subdivision group without a trailing `>>N`
        // spans the inherited DOTTED quarter, so its three members subdivide 3/8
        // into plain eighths. With the dots dropped it spanned 1/4 and the members
        // came out a third of that. Found by the self-audit, not by a corpus book.
        // LILYPOND-REF: lily/parser.yy:3505-3514 optional_notemode_duration — default_duration_
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("time 12/8\nc4. << c e g >> c c |"));
        var notes = score.Voices[0].Measures[0].Items.OfType<NoteItem>().ToList();
        Assert.Equal(6, notes.Count);
        Assert.Equal(LilySharp.Core.Semantics.Fraction.FromNoteValue(8),
            notes[1].BaseDuration);   // a group member
        Assert.Equal(1, notes[4].Dots); // the bare c AFTER the group keeps the dotted default
    }

    // ===== The Reset drift net =====
    //
    // The collector's output lists used to be enumerated BY HAND in three places
    // (Reset / CumulativeSideTables / CaptureScoreContent), and the Reset copy had
    // drifted: _musicMarks, _customTexts, _voltaBrackets, _tupletBrackets and
    // _navPlacementWarnings were missing, so a reused instance carried the previous
    // collect's marks and warnings (2026-08-26 review, §4a-⑪). Reset now clears the
    // cumulative set FROM the CumulativeSideTables registry; this net holds the
    // other direction: every collection-typed field of the collector (and of its
    // output-sink collaborators LyricsCollector / ChordNameCollector) must be empty
    // after Reset, whatever list it lives in. A new field forgotten by both the
    // registry and Reset's explicit tail fails here the moment it exists.

    /// <summary>
    /// Fills EVERY collection field with one dummy element via reflection (no
    /// single walk could reach them all), calls the private <c>Reset()</c>, and
    /// requires every one of them empty. The populated-count floor proves the
    /// injector actually injected — a filter bug that matched zero fields would
    /// otherwise report success (§0's "right number for the wrong reason").
    /// </summary>
    [Fact]
    public void Reset_EmptiesEveryCollectionField()
    {
        var collector = new MeasureCollector();
        var fields = CollectionFields(collector).ToList();

        int populated = 0;
        var unfillable = new List<string>();
        foreach (var (label, value) in fields)
        {
            if (TryAddDummy(value))
                populated++;
            else
                unfillable.Add(label);
        }

        // Every collection field must accept the dummy — an unfillable one is a
        // hole in the net, not a pass (list it so the fix is a conscious change
        // here, not a silent skip).
        Assert.True(unfillable.Count == 0,
            "these collection fields could not be populated by the net's injector "
            + "(extend TryAddDummy or exempt them explicitly): "
            + string.Join(", ", unfillable));

        // 2026-08-26: 40 fields (the collector's own plus LyricsCollector's 3 and
        // ChordNameCollector's 2). A shrink means the reflection filter broke, not
        // that the class lost fields — investigate before touching the floor.
        Assert.True(populated >= 40,
            $"the injector only populated {populated} collection fields — "
            + "the reflection filter is no longer seeing the class's fields");

        typeof(MeasureCollector)
            .GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(collector, null);

        // Re-read the fields AFTER Reset: a field is also legitimately emptied by
        // being replaced with a fresh instance (_lyricsRowNames does), so the
        // pre-Reset references cannot be the thing judged.
        var stale = CollectionFields(collector)
            .Where(f => CountOf(f.Value) > 0)
            .Select(f => f.Label)
            .ToList();

        Assert.True(stale.Count == 0,
            "MeasureCollector.Reset left these collection fields non-empty "
            + "(add them to CumulativeSideTables or to Reset's explicit tail): "
            + string.Join(", ", stale));
    }

    /// <summary>
    /// The collector's own collection-typed instance fields, plus one level into
    /// the two output-sink collaborators whose contents Reset owns via their
    /// <c>Clear()</c> (LyricsCollector, ChordNameCollector). The state collaborators
    /// with their own Reset contracts (MetadataState, SectionState, OctaveContext)
    /// are deliberately outside this net's charter.
    /// </summary>
    private static IEnumerable<(string Label, object Value)> CollectionFields(object owner)
    {
        foreach (var field in owner.GetType()
                     .GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var value = field.GetValue(owner);
            if (value == null)
                continue; // a nullable table not yet built has nothing to leak
            if (IsCollection(value.GetType()))
                yield return ($"{owner.GetType().Name}.{field.Name}", value);
            else if (value is LyricsCollector or ChordNameCollector)
                foreach (var nested in CollectionFields(value))
                    yield return nested;
        }
    }

    private static bool IsCollection(Type type)
    {
        if (!type.IsGenericType)
            return false;
        var def = type.GetGenericTypeDefinition();
        return def == typeof(List<>) || def == typeof(Dictionary<,>)
            || def == typeof(HashSet<>) || def == typeof(SortedDictionary<,>)
            || def == typeof(Stack<>) || def == typeof(Queue<>);
    }

    /// <summary>Puts one dummy element into the collection (Reset never reads
    /// elements, so an uninitialized instance is enough). Returns false for a
    /// shape the injector does not know how to fill.</summary>
    private static bool TryAddDummy(object collection)
    {
        var type = collection.GetType();
        var args = type.GetGenericArguments();
        var def = type.GetGenericTypeDefinition();

        if (def == typeof(List<>))
        {
            ((IList)collection).Add(Dummy(args[0]));
            return true;
        }
        if (def == typeof(Dictionary<,>) || def == typeof(SortedDictionary<,>))
        {
            ((IDictionary)collection).Add(Dummy(args[0])!, Dummy(args[1]));
            return true;
        }
        if (def == typeof(HashSet<>))
        {
            type.GetMethod("Add")!.Invoke(collection, new[] { Dummy(args[0]) });
            return true;
        }
        if (def == typeof(Stack<>))
        {
            type.GetMethod("Push")!.Invoke(collection, new[] { Dummy(args[0]) });
            return true;
        }
        if (def == typeof(Queue<>))
        {
            type.GetMethod("Enqueue")!.Invoke(collection, new[] { Dummy(args[0]) });
            return true;
        }
        return false;
    }

    /// <summary>A value assignable to <paramref name="type"/> without running any
    /// constructor: dictionary keys only ever need hashing (strings and value
    /// tuples hash fine; syntax-node keys hash by reference), and no element is
    /// ever read back.</summary>
    private static object? Dummy(Type type)
    {
        if (type == typeof(string))
            return "reset-net-dummy";
        if (type.IsValueType)
            return Activator.CreateInstance(type); // Nullable<T> yields null: fine to add
        if (type.IsAbstract) // an abstract element type: any concrete subtype will do
            type = type.Assembly.GetTypes().First(t =>
                !t.IsAbstract && !t.ContainsGenericParameters && type.IsAssignableFrom(t));
        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private static int CountOf(object collection)
        => ((IEnumerable)collection).Cast<object?>().Count();
}
