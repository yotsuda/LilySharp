using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Specification for a single staff in a render block.
/// </summary>
public sealed record StaffSpec(
    ClefType Clef,
    string VoiceName
);

/// <summary>
/// Specification for a grand staff (brace-connected staves).
/// </summary>
public sealed record GrandStaffSpec(
    ImmutableArray<StaffSpec> Staves
)
{
    public int StaffCount => Staves.Length;
}

/// <summary>
/// A render item - either a single staff or a grand staff.
/// </summary>
public abstract record RenderItemSpec;

/// <summary>
/// Single staff render item.
/// </summary>
public sealed record SingleStaffSpec(StaffSpec Staff) : RenderItemSpec;

/// <summary>
/// Grand staff render item.
/// </summary>
public sealed record GrandStaffRenderSpec(GrandStaffSpec GrandStaff) : RenderItemSpec;

/// <summary>
/// Complete render specification parsed from a render block.
/// </summary>
public sealed record RenderSpec(
    string Name,
    string OutputFile,
    ImmutableArray<RenderItemSpec> Items
)
{
    /// <summary>Whether this render contains a grand staff.</summary>
    public bool HasGrandStaff => Items.Any(i => i is GrandStaffRenderSpec);

    /// <summary>Whether this is a multi-staff render.</summary>
    public bool IsMultiStaff => Items.Length > 1 || HasGrandStaff;

    /// <summary>Gets all voice names referenced in this render.</summary>
    public IEnumerable<string> GetVoiceNames()
    {
        foreach (var item in Items)
        {
            switch (item)
            {
                case SingleStaffSpec single:
                    yield return single.Staff.VoiceName;
                    break;
                case GrandStaffRenderSpec grand:
                    foreach (var staff in grand.GrandStaff.Staves)
                        yield return staff.VoiceName;
                    break;
            }
        }
    }

    /// <summary>Gets all staff groups for layout.</summary>
    public IEnumerable<StaffGroup> ToStaffGroups(Func<string, Voice> getVoice)
    {
        foreach (var item in Items)
        {
            switch (item)
            {
                case SingleStaffSpec single:
                    var singleStaff = Staff.Create(single.Staff.Clef, getVoice(single.Staff.VoiceName));
                    yield return StaffGroup.CreateSingle(singleStaff);
                    break;

                case GrandStaffRenderSpec grand:
                    var staves = grand.GrandStaff.Staves
                        .Select(s => Staff.Create(s.Clef, getVoice(s.VoiceName)))
                        .ToArray();
                    yield return StaffGroup.CreateGrandStaff(staves);
                    break;
            }
        }
    }
}