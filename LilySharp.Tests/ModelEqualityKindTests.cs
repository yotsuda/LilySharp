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

using System.Reflection;
using System.Runtime.CompilerServices;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Pins which model types answer equality by IDENTITY and which by VALUE.
/// </summary>
/// <remarks>
/// <para>
/// The rule and its measurements live in <c>LilySharp.Core/Svg/Model/ModelIdentity.cs</c>;
/// the triage that produced these two lists is HANDOFF §2 C⑶. This is the net: a model
/// type cannot be added, or quietly flipped, without landing in one of the buckets ON
/// PURPOSE. Both directions are checked, so deleting an <c>Equals</c> override is as loud
/// as adding one.
/// </para>
/// <para>
/// The kind is read BEHAVIOURALLY rather than from the declaration, because for the
/// <see cref="MusicItem"/> hierarchy the override lives on the abstract base and each
/// derived record's synthesized <c>Equals</c> inherits it through <c>base.Equals</c> —
/// a declaration-shaped test would call the six derived records value types and be wrong.
/// Two field-identical instances are built with
/// <see cref="RuntimeHelpers.GetUninitializedObject"/> (every field at its default, so the
/// two are as content-identical as two instances get); a value type calls them equal and
/// an entity does not.
/// </para>
/// </remarks>
public class ModelEqualityKindTests
{
    /// <summary>
    /// One occurrence in one score. Two occurrences carrying the same content are still two
    /// things, so <c>IndexOf</c>/<c>Contains</c>/a dictionary key must not fuse them.
    /// </summary>
    private static readonly string[] Entities =
    [
        // the MusicItem hierarchy — identity comes from the abstract base
        "ChordItem", "ClefChangeItem", "KeySignatureChangeItem", "NoteItem", "RestItem",
        "TimeSignatureChangeItem",
        // one written mark each
        "ArticulationItem", "ChordNameItem", "CrossStaffItem", "CustomTextItem",
        "DynamicItem", "FiguredBassItem", "GraceNoteItem", "HairpinItem", "LyricItem",
        "MusicMarkItem", "OttavaBracketItem", "PercentRepeatItem", "SlurItem",
        "TextSpannerItem", "TieItem", "TrillSpannerItem", "TupletBracketItem",
        "VoltaBracketItem",
        // one node of one score tree
        "Measure", "MultiStaffScore", "Score", "Staff", "StaffGroup", "Voice",
    ];

    /// <summary>
    /// A description, not an occurrence — two identical ones ARE the same one. The
    /// incremental compiler leans on this: "is what I just recomputed the same VALUE as
    /// what I stored?" is how it decides a cached layout may be reused.
    /// </summary>
    private static readonly string[] Values =
    [
        "BeamGroup", "BeamLayout", "BeamMember", "BeamRestStem",
        "GrobOverride", "GrobRevert", "VoiceColumn", "VoiceEntry",
    ];

    [Fact]
    public void EveryReferenceTypeModelRecordIsClassifiedOnPurpose()
    {
        var declared = Entities.Concat(Values).ToHashSet(StringComparer.Ordinal);
        var found = ReferenceTypeModelRecords().Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unclassified = found.Except(declared).OrderBy(x => x, StringComparer.Ordinal);
        Assert.True(!unclassified.Any(),
            "a model record answers equality but no one decided how: "
            + string.Join(", ", unclassified)
            + " — put it in Entities or Values (the axes are in HANDOFF §2 C⑶) rather than "
            + "letting the compiler's default decide.");

        var gone = declared.Except(found).OrderBy(x => x, StringComparer.Ordinal);
        Assert.True(!gone.Any(),
            "this list names model records that no longer exist: " + string.Join(", ", gone));
    }

    [Fact]
    public void EntitiesAnswerByIdentity()
    {
        var wrong = new List<string>();
        foreach (var t in ReferenceTypeModelRecords().Where(t => Entities.Contains(t.Name)))
        {
            if (AreFieldIdenticalInstancesEqual(t) is true)
                wrong.Add(t.Name);
        }
        Assert.True(wrong.Count == 0,
            "these are entities but two content-identical instances compare EQUAL, so a "
            + "collection search silently returns the first twin (fixed #18): "
            + string.Join(", ", wrong.OrderBy(x => x, StringComparer.Ordinal))
            + " — the repair is the Equals/GetHashCode pair in ModelIdentity's remarks.");
    }

    [Fact]
    public void ValuesAnswerByValue()
    {
        var wrong = new List<string>();
        foreach (var t in ReferenceTypeModelRecords().Where(t => Values.Contains(t.Name)))
        {
            if (AreFieldIdenticalInstancesEqual(t) is false)
                wrong.Add(t.Name);
        }
        Assert.True(wrong.Count == 0,
            "these are compared BY VALUE to decide whether a cached result may be reused, "
            + "but they now answer by identity — every such comparison silently says "
            + "'changed' and the incremental fast paths go dead: "
            + string.Join(", ", wrong.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Two instances with every field at its default — as content-identical as two separate
    /// instances can be. <c>true</c> = value equality, <c>false</c> = identity,
    /// <c>null</c> = the type would not answer (not a verdict either way).
    /// </summary>
    private static bool? AreFieldIdenticalInstancesEqual(Type t)
    {
        try
        {
            object a = RuntimeHelpers.GetUninitializedObject(t);
            object b = RuntimeHelpers.GetUninitializedObject(t);
            return a.Equals(b);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<Type> ReferenceTypeModelRecords()
        => typeof(TieItem).Assembly.GetTypes()
            .Where(t => t.Namespace == "LilySharp.Core.Svg.Model"
                        && t.IsClass && !t.IsAbstract && IsRecord(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    /// <summary>A record is the class the compiler gave a clone method and an
    /// <c>EqualityContract</c> to; a plain class has neither.</summary>
    private static bool IsRecord(Type t)
        => t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance) is not null;
}
