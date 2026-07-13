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
    string? InstrumentName = null,
    // Hara-kiri: hide this staff in systems where it only rests
    // (part property removeEmpty true — LP RemoveEmptyStaves).
    bool RemoveEmpty = false,
    // Hara-kiri including the FIRST system (part property
    // removeEmpty all — LP RemoveAllEmptyStaves).
    bool RemoveFirst = false,
    // Staff line count (part property lines N; 5 default).
    int Lines = 5,
    // A named chord part whose symbols align above this staff
    // (staff NAME with chords CHORDPART); the same part can also feed
    // a lead-sheet row, so a progression is written once.
    string? WithChords = null,
    // staff ~flute: the writer opted OUT of the default
    // instrument-name label for this staff.
    bool NameSuppressed = false,
    // How the attached chords are shown (`... as roman | both | names`).
    ChordDisplayMode ChordDisplay = ChordDisplayMode.Names
);

/// <summary>
/// Specification for a grand staff (brace-connected staves).
/// </summary>
public sealed record GrandStaffSpec(
    ImmutableArray<StaffSpec> Staves
)
{
    /// <summary>Number of staves in this grand staff.</summary>
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
public sealed record TabStaffSpec(StaffSpec Staff, TuningType Tuning, int Transposition = 0,
    // `tab part as numbers`: show fret digits only — no stems, beams, flags, dots,
    // rests or tuplet brackets (LilyPond's default TabStaff; `\tabFullNotation` is
    // the opposite, and stays this renderer's default). Ties/slurs still print.
    bool NumbersOnly = false) : RenderItemSpec;

/// <summary>
/// Ossia staff render item (small alternative passage above/below main staff).
/// LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize and magnifyStaff
/// </summary>
public sealed record OssiaStaffSpec(StaffSpec Staff) : RenderItemSpec;

/// <summary>
/// Independent chord-row render item: <c>chords name</c> places a chord part
/// (<c>chords name { … }</c>) as its own row in the score's staff order.
/// </summary>
public sealed record ChordRowSpec(
    string PartName,
    ChordDisplayMode DisplayMode = ChordDisplayMode.Names) : RenderItemSpec;

/// <summary>
/// Independent lyrics-row render item: <c>lyrics name</c> places a lyrics part
/// (<c>lyrics name { … }</c>) as its own row in the score's staff order.
/// </summary>
public sealed record LyricsRowSpec(string PartName) : RenderItemSpec;

/// <summary>
/// Complete render specification parsed from a render block.
/// </summary>
public sealed record RenderSpec(
    string Name,
    string OutputFile,
    ImmutableArray<RenderItemSpec> Items,
    // A per-score `transpose <pitch>` (e.g. a Bb part-score): the c->target diatonic
    // interval, composed on top of each part's own transpose. Null = concert pitch.
    (int step, int alt, int oct)? ScoreTranspose = null,
    // The form this score renders, resolved from its `score <FormName>` header
    // against the file's `form <Name> { ... }` declarations. Null when the name is
    // missing or unknown (a validator error) — the score then renders nothing.
    FormDeclarationSyntax? Form = null
)
{
    /// <summary>
    /// The output file stem for this score, given the input file's stem: the
    /// <c>main</c> score (empty OutputFile) uses the input stem as-is; every other
    /// score appends its name — its form name, or an explicit basename — to the
    /// input stem (<c>foo</c> + <c>score sub</c> → <c>foo-sub</c>; <c>foo</c> +
    /// <c>score sub "custom"</c> → <c>foo-custom</c>).
    /// </summary>
    public string ResolveOutputStem(string inputStem) =>
        string.IsNullOrEmpty(OutputFile) ? inputStem : $"{inputStem}-{OutputFile}";

    /// <summary>Whether this render contains a grand staff.</summary>
    public bool HasGrandStaff => Items.Any(i => i is GrandStaffRenderSpec);

    /// <summary>Whether this render contains a tablature staff.</summary>
    public bool HasTab => Items.Any(i => i is TabStaffSpec);

    /// <summary>
    /// Whether this render needs the multi-staff pipeline. A lone <c>tab</c> still
    /// does: the single-staff path renders plain notation and has no tab support,
    /// so a tab-only score would otherwise fall back to a notation staff.
    /// </summary>
    public bool IsMultiStaff => Items.Length > 1 || HasGrandStaff || HasTab || HasChordRow || HasLyricsRow;

    /// <summary>Whether this render contains an independent chord row. A chord-only
    /// score (just <c>chords name</c>) still needs the multi-staff pipeline — the
    /// single-staff path renders a notation staff and has no chord-row support.</summary>
    public bool HasChordRow => Items.Any(i => i is ChordRowSpec);

    /// <summary>Whether this render contains an independent lyrics row (same
    /// multi-staff-pipeline requirement as <see cref="HasChordRow"/>).</summary>
    public bool HasLyricsRow => Items.Any(i => i is LyricsRowSpec);

    /// <summary>Gets all voice names referenced in this render.</summary>
    public IEnumerable<string> GetVoiceNames()
        => GetVoiceBindings().Select(b => b.VoiceName);

    /// <summary>
    /// The per-staff voice bindings in the SAME order <see cref="ToStaffGroups"/>
    /// builds staves (so the caller's counter equals the global staff index):
    /// the voice name plus the staff's attached chord part, if any
    /// (<c>staff NAME with chords CHORDPART</c>).
    /// </summary>
    public IEnumerable<(string VoiceName, string? WithChords, ChordDisplayMode ChordDisplay)> GetVoiceBindings()
    {
        foreach (var item in OrderedItems())
        {
            switch (item)
            {
                case SingleStaffSpec single:
                    yield return (single.Staff.VoiceName, single.Staff.WithChords, single.Staff.ChordDisplay);
                    break;
                case GrandStaffRenderSpec grand:
                    foreach (var staff in grand.GrandStaff.Staves)
                        yield return (staff.VoiceName, staff.WithChords, staff.ChordDisplay);
                    break;
                case TabStaffSpec tab:
                    yield return (tab.Staff.VoiceName, null, ChordDisplayMode.Names);
                    break;
                case OssiaStaffSpec ossia:
                    yield return (ossia.Staff.VoiceName, null, ChordDisplayMode.Names);
                    break;
                case ChordRowSpec chordRow:
                    yield return (chordRow.PartName, null, chordRow.DisplayMode);
                    break;
                case LyricsRowSpec lyricsRow:
                    yield return (lyricsRow.PartName, null, ChordDisplayMode.Names);
                    break;
            }
        }
    }

    /// <summary>
    /// Render items in STACKING (top-to-bottom) order: an ossia written AFTER a
    /// staff moves directly ABOVE the nearest preceding main item, LP-style —
    /// an ossia decorates the staff below it (LILYPOND-REF: Notation Reference
    /// "Ossia staves", alignAboveContext). An ossia written before any staff
    /// already stacks above and keeps its place; several ossias keep their
    /// source order (the first written ends up highest).
    /// <see cref="GetVoiceNames"/> and <see cref="ToStaffGroups"/> both iterate
    /// THIS order, so the collector's global staff indices stay in lockstep
    /// with the layout's.
    /// </summary>
    private List<RenderItemSpec> OrderedItems()
    {
        var ordered = new List<RenderItemSpec>(Items.Length);
        foreach (var item in Items)
        {
            if (item is OssiaStaffSpec)
            {
                int mainIdx = ordered.FindLastIndex(
                    i => i is SingleStaffSpec or GrandStaffRenderSpec or TabStaffSpec);
                if (mainIdx >= 0)
                {
                    ordered.Insert(mainIdx, item);
                    continue;
                }
            }
            ordered.Add(item);
        }
        return ordered;
    }

    /// <summary>Gets all staff groups for layout.</summary>
    public IEnumerable<StaffGroup> ToStaffGroups(Func<string, ImmutableArray<Voice>> getVoices)
    {
        // The single-voice rows (tab/ossia/chord/lyrics) take the part's primary
        // voice; a spec naming an undefined/empty part falls back to an empty voice
        // rather than throwing IndexOutOfRange.
        Voice FirstVoiceOrEmpty(string name)
        {
            var vs = getVoices(name);
            return vs.Length > 0 ? vs[0] : new Voice(name, ImmutableArray<Measure>.Empty);
        }

        foreach (var item in OrderedItems())
        {
            switch (item)
            {
                case SingleStaffSpec single:
                    var singleStaff = Staff.Create(
                        single.Staff.Clef,
                        getVoices(single.Staff.VoiceName),
                        single.Staff.InstrumentName) with
                    {
                        RemoveEmpty = single.Staff.RemoveEmpty,
                        RemoveFirst = single.Staff.RemoveFirst,
                        Lines = single.Staff.Lines,
                    };
                    yield return StaffGroup.CreateSingle(singleStaff);
                    break;

                case GrandStaffRenderSpec grand:
                    var staves = grand.GrandStaff.Staves
                        .Select(s => Staff.Create(
                            s.Clef,
                            getVoices(s.VoiceName),
                            s.InstrumentName) with
                        {
                            RemoveEmpty = s.RemoveEmpty,
                            RemoveFirst = s.RemoveFirst,
                            Lines = s.Lines,
                        })
                        .ToArray();
                    yield return StaffGroup.CreateGrandStaff(staves);
                    break;

                // Tab / ossia staves don't support intra-staff polyphony; they
                // take the primary voice only.
                case TabStaffSpec tab:
                    var tabStaff = Staff.CreateTab(tab.Tuning, FirstVoiceOrEmpty(tab.Staff.VoiceName), tab.Staff.Clef, tab.Transposition, tab.NumbersOnly);
                    yield return StaffGroup.CreateSingle(tabStaff);
                    break;

                case OssiaStaffSpec ossia:
                    var ossiaStaff = Staff.CreateOssia(
                        ossia.Staff.Clef,
                        FirstVoiceOrEmpty(ossia.Staff.VoiceName),
                        ossia.Staff.InstrumentName);
                    yield return StaffGroup.CreateSingle(ossiaStaff);
                    break;

                case ChordRowSpec chordRow:
                    yield return StaffGroup.CreateSingle(Staff.CreateTextRow(FirstVoiceOrEmpty(chordRow.PartName)));
                    break;

                case LyricsRowSpec lyricsRow:
                    yield return StaffGroup.CreateSingle(Staff.CreateTextRow(FirstVoiceOrEmpty(lyricsRow.PartName)));
                    break;
            }
        }
    }
}