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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Type of staff grouping.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/engraver-init.ly — context definitions for each staff group type
/// </remarks>
public enum StaffGroupType
{
    /// <summary>Single staff (no grouping).</summary>
    Single,

    /// <summary>Grand staff with brace (piano, harp, organ).</summary>
    GrandStaff,

    /// <summary>Staff group with bracket (orchestral sections). Barlines span all staves.</summary>
    StaffGroup,

    /// <summary>Choir staff with bracket (vocal sections). Barlines are separate per staff.</summary>
    ChoirStaff
}

/// <summary>
/// A group over a run of leaf <see cref="StaffGroup"/>s — a <c>grandStaff</c>,
/// <c>staffGroup</c> or <c>choirStaff</c> that holds another group — itself standing in the
/// group <see cref="Outer"/>, as the user wrote it (session 376: any group in any group, at
/// any depth).
/// </summary>
/// <remarks>
/// A CLASS, compared by reference: two adjacent groups of the same type are two groups, and
/// the leaves say which one they belong to by holding the same instance in their chain.
/// <para>
/// MEASURED on LilyPond 2.26.0 (scratch/p377/nest/deep.ly, sd*.ly):
/// <list type="bullet">
/// <item>every delimiter is side-positioned against its PARENT's ink — a bracket 0.8 left of
/// it, a brace 0.3 — and a top-level one against the SystemStartBar; siblings at the same depth
/// are not aligned in a column;</item>
/// <item>a staff sits 9 below the staff above when both stand in the same innermost group,
/// 10.5 when they do not;</item>
/// <item>bar lines are drawn through the gap between two staves when ANY group holding both is
/// a staffGroup or grandStaff — a choirStaff inside a grandStaff has its gaps spanned by the
/// grandStaff.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class OuterStaffGroup(StaffGroupType type, OuterStaffGroup? outer = null)
{
    /// <summary><see cref="StaffGroupType.GrandStaff"/>, <see cref="StaffGroupType.StaffGroup"/> or
    /// <see cref="StaffGroupType.ChoirStaff"/>.</summary>
    public StaffGroupType Type { get; } = type;

    /// <summary>The group this one stands in, or null at the top of the score.</summary>
    public OuterStaffGroup? Outer { get; } = outer;

    /// <summary>How many groups stand around this one (0 at the top).</summary>
    public int Depth => Outer is null ? 0 : Outer.Depth + 1;

    /// <summary>This group, then every group around it, innermost first.</summary>
    public IEnumerable<OuterStaffGroup> SelfAndOuters()
    {
        for (var g = this; g != null; g = g.Outer)
            yield return g;
    }
}

/// <summary>
/// A group of staves rendered together.
/// </summary>
/// <remarks>
/// StaffGroup represents:
/// - A single staff (StaffGroupType.Single)
/// - A grand staff with brace (StaffGroupType.GrandStaff) - piano, harp
/// - A bracketed group (StaffGroupType.StaffGroup) - orchestral sections
///
/// Grand staff characteristics:
/// - Connected by a brace on the left
/// - Barlines extend through all staves
/// - Typically 2 staves (treble + bass), but can have more (organ)
/// </remarks>
public sealed record StaffGroup(
    StaffGroupType Type,
    ImmutableArray<Staff> Staves
)
{
    // Identity, not value equality: see ModelIdentity.
    public bool Equals(StaffGroup? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);

    /// <summary>Number of staves in this group.</summary>
    public int StaffCount => Staves.Length;

    /// <summary>
    /// The innermost group this leaf stands in, or null — set on every leaf of a group that
    /// holds another group (its chain of <see cref="OuterStaffGroup.Outer"/> reaches the top).
    /// </summary>
    /// <remarks>
    /// Leaves whose chains share one instance are that group's run (consecutive by
    /// construction, RenderSpec.BuildStaffGroups). The group is drawn, spans its bar lines and
    /// chooses its boundary spacing from this, so no reader has to reconstruct a tree.
    /// </remarks>
    public OuterStaffGroup? Outer { get; init; }

    /// <summary>Whether this is a grand staff (brace-connected).</summary>
    public bool IsGrandStaff => Type == StaffGroupType.GrandStaff;

    /// <summary>Whether this is a choir staff (bracket-connected, separate barlines).</summary>
    public bool IsChoirStaff => Type == StaffGroupType.ChoirStaff;

    /// <summary>Whether this is a staff group (bracket-connected, spanning barlines).</summary>
    public bool IsBracketGroup => Type == StaffGroupType.StaffGroup;

    /// <summary>Whether this is a single staff.</summary>
    public bool IsSingle => Type == StaffGroupType.Single;

    /// <summary>Whether this group has a delimiter (brace/bracket) at the left.</summary>
    public bool HasDelimiter => Type is StaffGroupType.GrandStaff
        or StaffGroupType.StaffGroup or StaffGroupType.ChoirStaff;

    /// <summary>The first (or only) staff.</summary>
    public Staff PrimaryStaff => Staves[0];

    /// <summary>
    /// Creates a single staff group (no brace/bracket).
    /// </summary>
    public static StaffGroup CreateSingle(Staff staff)
        => new(StaffGroupType.Single, ImmutableArray.Create(staff));

    /// <summary>
    /// Creates a grand staff (brace-connected, typically piano).
    /// </summary>
    public static StaffGroup CreateGrandStaff(params Staff[] staves)
    {
        if (staves.Length < 2)
            throw new ArgumentException("Grand staff requires at least 2 staves", nameof(staves));
        return new(StaffGroupType.GrandStaff, [.. staves]);
    }

    /// <summary>
    /// Creates a grand staff from an immutable array.
    /// </summary>
    public static StaffGroup CreateGrandStaff(ImmutableArray<Staff> staves)
    {
        if (staves.Length < 2)
            throw new ArgumentException("Grand staff requires at least 2 staves", nameof(staves));
        return new(StaffGroupType.GrandStaff, staves);
    }

    /// <summary>
    /// Creates a choir staff (bracket-connected, separate barlines).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly — ChoirStaff uses SystemStartBracket
    /// with disconnected barlines (each staff has its own barlines).
    /// </remarks>
    public static StaffGroup CreateChoirStaff(params Staff[] staves)
    {
        if (staves.Length < 2)
            throw new ArgumentException("Choir staff requires at least 2 staves", nameof(staves));
        return new(StaffGroupType.ChoirStaff, [.. staves]);
    }

    /// <summary>
    /// Creates a bracket group (orchestral sections with spanning barlines).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly — StaffGroup uses SystemStartBracket
    /// with connected barlines (barlines span all staves in the group).
    /// </remarks>
    public static StaffGroup CreateBracketGroup(params Staff[] staves)
    {
        if (staves.Length < 2)
            throw new ArgumentException("Bracket group requires at least 2 staves", nameof(staves));
        return new(StaffGroupType.StaffGroup, [.. staves]);
    }
}