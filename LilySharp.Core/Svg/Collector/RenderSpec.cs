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
    // The range a written `lines N` may take is MinLines..MaxLines below —
    // RenderSpecParser reads against it and SymbolCaseValidator refuses against
    // it, so the bound is stated once and neither can drift from the other.
    int Lines = 5,
    // A named chord part whose symbols align above this staff
    // (staff NAME with chords CHORDPART); the same part can also feed
    // a lead-sheet row, so a progression is written once.
    string? WithChords = null,
    // staff ~flute: the writer opted OUT of the default
    // instrument-name label for this staff.
    bool NameSuppressed = false,
    // How the attached chords are shown (`... as roman | both | names`).
    ChordDisplayMode ChordDisplay = ChordDisplayMode.Names,
    // Named lyrics parts aligned note-by-note BELOW this staff
    // (staff NAME with lyrics L [with lyrics L2 ...]); multiple stack as verses.
    ImmutableArray<string> WithLyrics = default,
    // How piano pedal marks render (part property `pedal`: bracket | text | mixed).
    PedalStyle PedalStyle = PedalStyle.Bracket
)
{
    /// <summary>Fewest staff lines a written <c>lines N</c> may ask for.</summary>
    public const int MinLines = 1;

    /// <summary>Most staff lines a written <c>lines N</c> may ask for.</summary>
    /// <remarks>
    /// The bound has ONE home because it has two readers that must agree:
    /// <c>RenderSpecParser</c> falls back to five for anything outside it, and
    /// <c>SymbolCaseValidator</c> refuses to compile a book that writes something
    /// outside it. Until 2026-08-19 only the first existed, so <c>lines 9</c>
    /// compiled and silently drew five lines.
    /// LILYSHARP-OWN: the DEFAULT five is LilyPond's (scm/define-grobs.scm:3396,
    /// StaffSymbol's line-count property), but the RANGE is not. LilyPond bounds
    /// line-count at neither end — Staff_symbol::calc_line_positions just walks
    /// line_count values (lily/staff-symbol.cc:111-114) — so a six-line staff is a
    /// grob property away there and a compile error here. It departs from LilyPond
    /// the moment a book needs one; nothing but a tab staff has ever asked, and a
    /// tab staff takes its count from its tuning rather than from this property.
    /// Observed by LinesTheValidatorAccepts_AreExactlyTheOnesTheRendererUses, which
    /// compares the two readers over 0..7 rather than against this number.
    /// </remarks>
    public const int MaxLines = 5;
}

/// <summary>
/// Specification for a bracketed/braced staff group. The group Type distinguishes
/// grandStaff (brace, spanning barlines), staffGroup (bracket, spanning barlines)
/// and choirStaff (bracket, disconnected barlines).
/// </summary>
public sealed record GrandStaffSpec(
    ImmutableArray<StaffSpec> Staves,
    StaffGroupType Type = StaffGroupType.GrandStaff
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
/// Condensed-staff render item: <c>condensedStaff { partA partB … }</c> puts N parts onto
/// ONE staff, one voice each, in source order.
/// </summary>
/// <remarks>
/// This is the plain condensation — the parts keep their own notes and get the ordinary
/// polyphony treatment (voice 1 up, voice 2 down, collision resolution). It does NOT merge
/// unisons or print a2/Solo; that is the part combiner, which is a separate item.
/// </remarks>
public sealed record CondensedStaffSpec(
    ClefType Clef,
    ImmutableArray<string> PartNames,
    string? InstrumentName = null) : RenderItemSpec;

/// <summary>
/// Combined-staff render item: <c>combinedStaff { partA partB }</c> puts TWO parts onto one
/// staff and merges them where they agree — one notehead for a unison, one voice for a solo
/// with the other part's rests gone, and "a2"/"Solo"/"Solo II" to say which is which.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="CondensedStaffSpec"/>, which keeps both parts whole. See
/// <see cref="PartCombiner"/> for what "agree" means at each moment.
/// </remarks>
public sealed record CombinedStaffSpec(
    ClefType Clef,
    ImmutableArray<string> PartNames,
    string? InstrumentName = null) : RenderItemSpec;

/// <summary>
/// Tablature staff render item.
/// </summary>
public sealed record TabStaffSpec(StaffSpec Staff, TuningType Tuning, int Transposition = 0,
    // `tab part as numbers`: show fret digits only — no stems, beams, flags, dots,
    // rests or tuplet brackets (LilyPond's default TabStaff; `\tabFullNotation` is
    // the opposite, and stays this renderer's default). Ties/slurs still print.
    bool NumbersOnly = false,
    // A named chord part whose symbols align above this tab staff
    // (`tab part with chords CHORDPART [as roman|both|names]`) — exactly like the
    // notation-staff `staff … with chords …` attachment.
    string? WithChords = null,
    ChordDisplayMode ChordDisplay = ChordDisplayMode.Names) : RenderItemSpec;

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
    FormDeclarationSyntax? Form = null,
    // `title` / `composer` written INSIDE this score block, which restate the file
    // header for this score alone (`score sub { title "Violin I" staff vln }`).
    // Empty when the score states none, and applied over the file's own values by
    // MeasureCollector.CollectDefinitions — so a score that restates nothing keeps
    // them and no score can leak its header to another.
    ImmutableArray<MetadataDeclarationSyntax> HeaderOverrides = default,
    // `fonts NAME [{ … }]` written inside this score block — a reference to a named
    // top-level fonts block, with an optional override block of its own. Null when
    // the score references none (it then keeps the file's unnamed default). Resolved
    // by MeasureCollector.CollectDefinitions, the same road as HeaderOverrides.
    FontDeclarationSyntax? FontsRef = null,
    // `paper NAME [{ … }]`, same contract as FontsRef.
    PaperDeclarationSyntax? PaperRef = null
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

    /// <summary>
    /// The parts this score ENGRAVES, in score order, without repeats — the answer to
    /// "whose music is this score showing".
    /// </summary>
    /// <remarks>
    /// ⚠️ Staff-like items only. A chord row and a lyrics row name a part too, but they
    /// draw text beside the music rather than the music, so they are not one of the
    /// registers a piece of unattributed music could be read in.
    /// <para>
    /// ⚠️ DISTINCT, and that is the point of the property rather than an optimisation:
    /// <c>score main { staff bl  tab bl }</c> shows ONE part on two staves, and a caller
    /// asking "does this score name a single part" has to get yes. The first caller is
    /// <c>MidiExporter</c>, which uses it to attribute a bare <c>section</c> — music no
    /// <c>part { }</c> block claims — to the part the score gives it to.
    /// </para>
    /// </remarks>
    public ImmutableArray<string> EngravedPartNames
    {
        get
        {
            var names = ImmutableArray.CreateBuilder<string>();
            void Add(string? n)
            {
                if (!string.IsNullOrEmpty(n) && !names.Contains(n!)) names.Add(n!);
            }
            foreach (var item in Items)
                switch (item)
                {
                    case SingleStaffSpec s: Add(s.Staff.VoiceName); break;
                    case TabStaffSpec t: Add(t.Staff.VoiceName); break;
                    case OssiaStaffSpec o: Add(o.Staff.VoiceName); break;
                    case GrandStaffRenderSpec g:
                        foreach (var s in g.GrandStaff.Staves) Add(s.VoiceName);
                        break;
                    case CondensedStaffSpec c:
                        foreach (var n in c.PartNames) Add(n);
                        break;
                    case CombinedStaffSpec cb:
                        foreach (var n in cb.PartNames) Add(n);
                        break;
                }
            return names.ToImmutable();
        }
    }

    /// <summary>Whether this render contains a grand staff.</summary>
    public bool HasGrandStaff => Items.Any(i => i is GrandStaffRenderSpec);

    /// <summary>Whether this render contains a tablature staff.</summary>
    public bool HasTab => Items.Any(i => i is TabStaffSpec);

    /// <summary>
    /// Whether this render needs the multi-staff pipeline. A lone <c>tab</c> still
    /// does: the single-staff path renders plain notation and has no tab support,
    /// so a tab-only score would otherwise fall back to a notation staff.
    /// </summary>
    public bool IsMultiStaff => Items.Length > 1 || HasGrandStaff || HasTab || HasChordRow
        || HasLyricsRow || HasCondensedStaff || HasCombinedStaff;

    /// <summary>Whether this render contains a condensed staff. A lone one still needs the
    /// multi-staff pipeline: the single-staff path takes ONE part's voices, which is exactly
    /// what a condensed staff is not.</summary>
    public bool HasCondensedStaff => Items.Any(i => i is CondensedStaffSpec);

    /// <summary>Whether this render contains a combined staff. A lone one needs the
    /// multi-staff pipeline for the same reason a condensed one does.</summary>
    public bool HasCombinedStaff => Items.Any(i => i is CombinedStaffSpec);

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
    /// <remarks>
    /// ⚠️ <c>SharesStaffWithPrevious</c> breaks the one-binding-one-staff assumption on
    /// purpose: a <c>condensedStaff</c> yields ONE binding per part but builds ONE staff, so
    /// a caller that increments blindly would shift every later staff index by N-1. Those
    /// parts genuinely are on the same staff, so they take the same index.
    /// </remarks>
    public IEnumerable<(string VoiceName, string? WithChords, ChordDisplayMode ChordDisplay, ImmutableArray<string> WithLyrics, bool SharesStaffWithPrevious)> GetVoiceBindings()
    {
        static ImmutableArray<string> Ly(ImmutableArray<string> a) => a.IsDefault ? ImmutableArray<string>.Empty : a;
        foreach (var item in OrderedItems())
        {
            switch (item)
            {
                case SingleStaffSpec single:
                    yield return (single.Staff.VoiceName, single.Staff.WithChords, single.Staff.ChordDisplay, Ly(single.Staff.WithLyrics), false);
                    break;
                case GrandStaffRenderSpec grand:
                    foreach (var staff in grand.GrandStaff.Staves)
                        yield return (staff.VoiceName, staff.WithChords, staff.ChordDisplay, Ly(staff.WithLyrics), false);
                    break;
                // Every condensed part is COLLECTED even though they share one staff — the
                // binding list is what tells the collector whose music to gather — but only
                // the first opens a new staff index.
                case CondensedStaffSpec condensed:
                    for (int i = 0; i < condensed.PartNames.Length; i++)
                        yield return (condensed.PartNames[i], null, ChordDisplayMode.Names,
                            ImmutableArray<string>.Empty, i > 0);
                    break;
                // Same bookkeeping as a condensed staff: both parts are collected, one
                // staff index is opened. Whether the two end up as one voice or two is
                // decided later, by the music.
                case CombinedStaffSpec combined:
                    for (int i = 0; i < combined.PartNames.Length; i++)
                        yield return (combined.PartNames[i], null, ChordDisplayMode.Names,
                            ImmutableArray<string>.Empty, i > 0);
                    break;
                case TabStaffSpec tab:
                    yield return (tab.Staff.VoiceName, tab.WithChords, tab.ChordDisplay, Ly(tab.Staff.WithLyrics), false);
                    break;
                case OssiaStaffSpec ossia:
                    yield return (ossia.Staff.VoiceName, null, ChordDisplayMode.Names, ImmutableArray<string>.Empty, false);
                    break;
                case ChordRowSpec chordRow:
                    yield return (chordRow.PartName, null, chordRow.DisplayMode, ImmutableArray<string>.Empty, false);
                    break;
                case LyricsRowSpec lyricsRow:
                    yield return (lyricsRow.PartName, null, ChordDisplayMode.Names, ImmutableArray<string>.Empty, false);
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
                        PedalStyle = single.Staff.PedalStyle,
                    };
                    yield return StaffGroup.CreateSingle(singleStaff);
                    break;

                // N parts -> ONE staff. A Staff already holds N voices and the whole
                // polyphony path (stem directions by voice order, collision resolution,
                // rest displacement) reads staff.Voices, so condensing is a matter of
                // handing it the concatenated voice list. A part that is itself polyphonic
                // contributes all of its voices, in its own order.
                //
                // ⚠️ …with ONE step that is not free: the voice props. LilyPond's
                // \voiceOne/\voiceTwo belong to the STAFF's voices, and
                // MeasureCollector.ResolveVoiceStemDirections runs per PART, so nothing
                // applied them here — each part is monophonic on its own and returns at
                // `voices.Length <= 1`. The stems still came out right, because the renderer
                // re-derives those from the voice index, but a RESTS reads the stamp
                // (ElementCoordinator, ItemSkylineFactory): both parts' rests kept direction
                // 0 and were drawn on the centre line ON TOP OF EACH OTHER. Measured against
                // LilyPond's own \voiceOne/\voiceTwo control, which puts them at ±4:
                // audit/lpreg/pcsil-a-cond.lys vs pcsil-ctl.ly.
                // LILYPOND-REF: scm/music-functions.scm:666-674 make-voice-props-set — the
                // direction the voice number sets goes to every direction-polyphonic grob,
                // and Rest is one of them, which is why the rests and not the stems broke.
                case CondensedStaffSpec condensed:
                    var condensedVoices = MeasureCollector.ResolveVoiceStemDirections(
                        condensed.PartNames
                            .SelectMany(name => (IEnumerable<Voice>)getVoices(name))
                            .ToImmutableArray());
                    yield return StaffGroup.CreateSingle(
                        Staff.Create(condensed.Clef, condensedVoices, condensed.InstrumentName));
                    break;

                // TWO parts -> ONE staff, MERGED. Unlike the condensed staff above, the
                // voices that come out are not the voices that went in: the combiner
                // rewrites the music (PartCombiner.Combine), which is what LilyPond's
                // \partCombine does too.
                case CombinedStaffSpec combined:
                    var combineParts = combined.PartNames
                        // A part that rests with ITSELF — LilyPond's << R1 s1 s4 >>, a
                        // voice { } { } span here — settles which of those silences is the
                        // part's before the two parts are compared, because a skip is only a
                        // silence when there is no rest beside it.
                        .Select(name => PartCombiner.ChooseSilenceWithinPart(getVoices(name)))
                        .ToArray();
                    // A part with no music of its own still has to take part in the
                    // analysis — that is how the OTHER part's passage becomes a solo.
                    Voice PartVoice(int i) => i < combineParts.Length && combineParts[i].Length > 0
                        ? combineParts[i][0]
                        : new Voice(combined.PartNames.Length > i ? combined.PartNames[i] : "",
                            ImmutableArray<Measure>.Empty);
                    var result = PartCombiner.Combine(PartVoice(0), PartVoice(1));
                    // ⚠️ A part that is itself polyphonic contributes its FIRST voice to the
                    // combination and its remaining voices to the staff untouched — the
                    // combiner compares two streams, and a voice { } { } span is already two.
                    var extraVoices = combineParts
                        .SelectMany(vs => vs.Skip(1))
                        .ToImmutableArray();
                    yield return StaffGroup.CreateSingle(
                        Staff.Create(combined.Clef,
                            result.Voices.AddRange(extraVoices),
                            combined.InstrumentName)
                        with
                        { PartCombineMarks = result.Marks });
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
                            PedalStyle = s.PedalStyle,
                        })
                        .ToArray();
                    yield return grand.GrandStaff.Type switch
                    {
                        StaffGroupType.StaffGroup => StaffGroup.CreateBracketGroup(staves),
                        StaffGroupType.ChoirStaff => StaffGroup.CreateChoirStaff(staves),
                        _ => StaffGroup.CreateGrandStaff(staves),
                    };
                    break;

                // A tab staff carries EVERY voice of its part, like the notation staff:
                // LilyPond's TabStaff accepts TabVoices and \\ spans work in it. (Until
                // 2026-08-09 it took the primary voice only and a polyphonic part's tab
                // dropped voice two — automatic-polyphony-tabstaff.ly.) The tab draw
                // walk already iterates staff.Voices and places items by TIMING, so the
                // second voice's digits land on the shared columns.
                case TabStaffSpec tab:
                    var tabVoices = getVoices(tab.Staff.VoiceName);
                    if (tabVoices.Length == 0)
                        tabVoices = ImmutableArray.Create(
                            new Voice(tab.Staff.VoiceName, ImmutableArray<Measure>.Empty));
                    var tabStaff = Staff.CreateTab(tab.Tuning, tabVoices, tab.Staff.Clef, tab.Transposition, tab.NumbersOnly);
                    yield return StaffGroup.CreateSingle(tabStaff);
                    break;

                // Ossia staves don't support intra-staff polyphony; they take the
                // primary voice only.

                case OssiaStaffSpec ossia:
                    var ossiaStaff = Staff.CreateOssia(
                        ossia.Staff.Clef,
                        FirstVoiceOrEmpty(ossia.Staff.VoiceName),
                        ossia.Staff.InstrumentName) with
                    {
                        Lines = ossia.Staff.Lines,
                    };
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