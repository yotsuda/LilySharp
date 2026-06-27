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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Specification for a single staff in a render block.
/// </summary>
public sealed record StaffSpec(
    ClefType Clef,
    string VoiceName,
    string? InstrumentName = null
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
/// Tablature staff render item.
/// </summary>
public sealed record TabStaffSpec(StaffSpec Staff, TuningType Tuning) : RenderItemSpec;

/// <summary>
/// Ossia staff render item (small alternative passage above/below main staff).
/// LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize and magnifyStaff
/// </summary>
public sealed record OssiaStaffSpec(StaffSpec Staff) : RenderItemSpec;

/// <summary>
/// Complete render specification parsed from a render block.
/// </summary>
public sealed record RenderSpec(
    string Name,
    string OutputFile,
    ImmutableArray<RenderItemSpec> Items,
    // A per-score `transpose <pitch>` (e.g. a Bb part-score): the c->target diatonic
    // interval, composed on top of each part's own transpose. Null = concert pitch.
    (int step, int alt, int oct)? ScoreTranspose = null
)
{
    /// <summary>Whether this render contains a grand staff.</summary>
    public bool HasGrandStaff => Items.Any(i => i is GrandStaffRenderSpec);

    /// <summary>Whether this render contains a tablature staff.</summary>
    public bool HasTab => Items.Any(i => i is TabStaffSpec);

    /// <summary>
    /// Whether this render needs the multi-staff pipeline. A lone <c>tab</c> still
    /// does: the single-staff path renders plain notation and has no tab support,
    /// so a tab-only score would otherwise fall back to a notation staff.
    /// </summary>
    public bool IsMultiStaff => Items.Length > 1 || HasGrandStaff || HasTab;

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
                case TabStaffSpec tab:
                    yield return tab.Staff.VoiceName;
                    break;
                case OssiaStaffSpec ossia:
                    yield return ossia.Staff.VoiceName;
                    break;
            }
        }
    }

    /// <summary>Gets all staff groups for layout.</summary>
    public IEnumerable<StaffGroup> ToStaffGroups(Func<string, ImmutableArray<Voice>> getVoices)
    {
        foreach (var item in Items)
        {
            switch (item)
            {
                case SingleStaffSpec single:
                    var singleStaff = Staff.Create(
                        single.Staff.Clef,
                        getVoices(single.Staff.VoiceName),
                        single.Staff.InstrumentName);
                    yield return StaffGroup.CreateSingle(singleStaff);
                    break;

                case GrandStaffRenderSpec grand:
                    var staves = grand.GrandStaff.Staves
                        .Select(s => Staff.Create(
                            s.Clef,
                            getVoices(s.VoiceName),
                            s.InstrumentName))
                        .ToArray();
                    yield return StaffGroup.CreateGrandStaff(staves);
                    break;

                // Tab / ossia staves don't support intra-staff polyphony; they
                // take the primary voice only.
                case TabStaffSpec tab:
                    var tabStaff = Staff.CreateTab(tab.Tuning, getVoices(tab.Staff.VoiceName)[0]);
                    yield return StaffGroup.CreateSingle(tabStaff);
                    break;

                case OssiaStaffSpec ossia:
                    var ossiaStaff = Staff.CreateOssia(
                        ossia.Staff.Clef,
                        getVoices(ossia.Staff.VoiceName)[0],
                        ossia.Staff.InstrumentName);
                    yield return StaffGroup.CreateSingle(ossiaStaff);
                    break;
            }
        }
    }
}