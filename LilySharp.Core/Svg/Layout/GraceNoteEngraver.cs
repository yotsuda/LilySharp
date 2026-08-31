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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a grace note group.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:1721-1732 GraceSpacing grob
/// LILYPOND-REF: lily/grace-spacing-engraver.cc:46 Grace_spacing_engraver::process_music
/// </remarks>
public readonly record struct GraceNoteLayout(
    int MeasureIndex,                    // Measure containing this grace
    int MainNoteItemIndex,               // Item index of the main note
    double X,                            // X position (left edge of grace group)
    ImmutableArray<GraceColumnInfo> Columns, // Columns in the grace group (a column is a chord)
    GraceNoteType Type,                  // Grace type (for slash rendering)
    double Scale,                        // GraceNoteItem.ScaleFactor (magstep of fontSize -3)
    int SourcePosition,                  // For click-to-source mapping
    // Main-note anchor for the grace slur (acciaccatura/appoggiatura).
    // LILYPOND-REF: ly/grace-init.ly startGraceSlur/stopGraceSlur
    double MainNoteX = 0,                // Absolute X of the main notehead
    int MainNoteStaffPosition = 0,       // Staff position of the main notehead
    // Multi-staff: vertical offset of this grace's OWN staff within the system.
    // The renderer recomputes note Y from staff positions, so the offset must
    // travel to it rather than being baked into Y here.
    double StaffYOffset = 0,
    // Non-null when this grace sits on a TAB staff: the renderer then draws each
    // grace as a small fret number (resolved from GraceHeadInfo.Midi) instead of
    // a notehead. null for ordinary notation staves.
    TuningType? Tuning = null,
    // The tab staff's clef (treble_8 shifts written→sounding an octave down).
    ClefType TabClef = ClefType.Treble,
    // The tab staff's resolved transposition (bass = −12); combines with TabClef for
    // the written→sounding shift, matching the main tab digits.
    int TabTransposition = 0,
    int SourceIndex = -1,                // F3/B: index into score.GraceNotes (data-pos resolved at render)
    int StaffIndex = -1,                 // owning staff (ossia shrink); -1 = unknown/test construction
    // The quanted beam, when this group has one: LilyPond's Beam.positions, i.e. the beam
    // centre's height at each DRAWN end (half a stem thickness outside the outer stems), in
    // staff positions up from the middle line. Null when the group draws flags instead.
    // LILYPOND-REF: lily/beam-quanting.cc — a grace beam is quanted by the same machine as
    //   any other, with beam-thickness 0.384 and length-fraction 0.8 (scm/music-functions.scm:635-648).
    double? BeamLeftY = null,
    double? BeamRightY = null,
    // Where each grace column sits, relative to <see cref="X"/> — the SAME numbers the
    // reservation and the beam quanter used, so the drawn heads cannot land anywhere else.
    // Before 2026-08-01 the renderer stepped by its own literal (1.2 + 0.3) and the group's
    // reserved width was computed a third way; see SpacingRules.GraceColumns.
    ImmutableArray<double> ColumnOffsets = default
);

/// <summary>
/// Calculates positions for grace notes.
/// Implements LilyPond's grace note positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/grace-engraver.cc:81 Grace_engraver::process_music
/// LILYPOND-REF: lily/grace-spacing-engraver.cc:46 Grace_spacing_engraver::process_music
///   (there is no grace-spacing.cc / Grace_spacing::calc_springs; the spring logic
///   lives in Grace_spacing_engraver)
///
/// Grace notes are placed immediately before their main note with:
/// - Smaller size (65% of normal)
/// - Tighter spacing between grace notes
/// - Acciaccatura slash through stem
/// </remarks>
internal static class GraceNoteEngraver
{
    // LILYPOND-REF: scm/music-functions.scm:635-648 general-grace-settings — NoteHead/Stem/
    //   Flag font-size -3 (the list is PER-GROB: the Accidental is -4), i.e.
    //   scm/lily-library.scm magstep(-3) = 2^(-3/6). See GraceNoteItem.ScaleFactor.
    private static readonly double GraceScale = GraceNoteItem.ScaleFactor;

    // The grace column step, the space between graces and the grace-to-main junction all
    // used to be constants here (1.2 / 0.3 / 0.4). LilyPond has none of them: the run is a
    // chain of springs with a skyline floor, and it is computed once in
    // SpacingRules.GraceColumns (ledger grace.column.*).

    /// <summary>
    /// Calculates layout for all grace notes in a score.
    /// </summary>
    public static ImmutableArray<GraceNoteLayout> Calculate(
        Score score,
        ImmutableArray<GraceNoteItem> graceNotes,
        ImmutableArray<MeasureLayout> measureLayouts,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Dictionary<int, double>? staffYByIndex = null,
        Dictionary<int, Staff>? staffByIndex = null,
        ImmutableArray<ArticulationItem> articulations = default)
    {
        if (graceNotes.IsDefaultOrEmpty)
            return ImmutableArray<GraceNoteLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<GraceNoteLayout>(graceNotes.Length);

        for (int gi = 0; gi < graceNotes.Length; gi++)
        {
            var grace = graceNotes[gi];
            // Find the measure layout
            if (grace.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[grace.MeasureIndex];

            // Bounds guard (single-staff layouts only; multi-staff layouts
            // resolve through timing-aligned columns).
            if (measureLayout.Columns.IsDefaultOrEmpty
                && grace.MainNoteItemIndex >= measureLayout.Items.Length)
                continue;

            // Resolve this grace's OWN staff (multi-staff): its measures (for the
            // main-note X / accidental) and the staff's vertical offset (carried
            // to the renderer via StaffYOffset, since it recomputes note Y).
            var graceMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, grace.StaffIndex, score.Voice.Measures);
            double staffOffset = staffYByIndex != null
                && staffYByIndex.TryGetValue(grace.StaffIndex, out var so) ? so : 0;
            // Tab staves render grace notes as small fret numbers, not noteheads.
            TuningType? tabTuning = null;
            var tabClef = ClefType.Treble;
            int tabTransposition = 0;
            if (staffByIndex != null
                && staffByIndex.TryGetValue(grace.StaffIndex, out var gst) && gst.IsTab)
            {
                tabTuning = gst.Tuning ?? TuningType.Guitar;
                tabClef = gst.TabSourceClef;
                tabTransposition = gst.Transposition;
            }
            if (grace.MeasureIndex >= graceMeasures.Length)
                continue;

            double mainNoteX = LayoutUtilities.GetItemXOffset(
                graceMeasures, grace.MeasureIndex, grace.MainNoteItemIndex, measureLayout);

            // Where the run's columns sit — ONE computation, read here for the placement,
            // below for the quanter's x frame, and by the renderer for the drawn heads.
            // LILYPOND-REF: lily/spacing-basic.cc:163-180 Spacing_spanner::note_spacing, its
            //   grace branch; see SpacingRules.GraceColumns for the whole law.
            var measure = graceMeasures[grace.MeasureIndex];
            var mainItem = grace.MainNoteItemIndex < measure.Items.Length
                ? measure.Items[grace.MainNoteItemIndex]
                : null;
            var columns = SpacingRules.GraceColumns(grace.Columns, mainItem);
            // The main note's own leftward accidental reach is already inside the run's LAST
            // gap (it is that column's left separation skyline), so it is no longer added
            // here — doing both reserved it twice.
            double graceGroupWidth = columns.Span;

            // A wide above-script on the main note (fermata / ornament) overhangs
            // its notehead to the LEFT; a leading grace's flag collides with it
            // unless the grace is pushed further left. Reserve that overhang (plus
            // the grace's own flag reach) so the grace clears the script, the way
            // LilyPond keeps a grace and a fermata apart. Y-gated to a grace sitting
            // at/above the main note, whose flag actually reaches the script's band.
            // LILYPOND-REF: lily/grace-spacing-engraver.cc + the Script joining the
            //   main column's outside-staff skyline.
            double scriptOverhang = ScriptOverhangForGrace(
                articulations, grace, measure, graceGroupWidth);

            // Position the run's FIRST column that far in front of the main note's.
            double x = measureLayout.X + mainNoteX - graceGroupWidth - scriptOverhang;

            // Main-note anchor for the grace slur (acciaccatura/appoggiatura).
            int mainStaffPosition = grace.MainNoteItemIndex < measure.Items.Length
                ? measure.Items[grace.MainNoteItemIndex] switch
                {
                    NoteItem n => n.StaffPosition,
                    ChordItem { Notes.Length: > 0 } c => c.Notes.Min(cn => cn.StaffPosition),
                    _ => 0
                }
                : 0;

            var (beamLeftY, beamRightY) = tabTuning is null
                ? QuantGraceBeam(grace, columns.Offsets)
                : (null, null);   // a TAB grace is a bare fret number: no stem, no beam

            layouts.Add(new GraceNoteLayout(
                grace.MeasureIndex,
                grace.MainNoteItemIndex,
                x,
                grace.Columns,
                grace.Type,
                GraceScale,
                grace.SourcePosition,
                MainNoteX: measureLayout.X + mainNoteX,
                MainNoteStaffPosition: mainStaffPosition,
                StaffYOffset: staffOffset,
                Tuning: tabTuning,
                TabClef: tabClef,
                TabTransposition: tabTransposition,
                SourceIndex: gi,
                StaffIndex: grace.StaffIndex,
                BeamLeftY: beamLeftY,
                BeamRightY: beamRightY,
                ColumnOffsets: columns.Offsets
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Quants a grace group's beam with the ORDINARY beam quanter, at the grace scale —
    /// LilyPond's <c>Beam.positions</c> for that beam, in staff positions from the middle
    /// line at each drawn end. Null when the group has no beam (a lone grace, a
    /// quarter-or-longer one, or a mixed group, all of which draw flags).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:635-648 <c>general-grace-settings</c> — a grace
    ///   beam is an ordinary Beam with
    ///   <c>beam-thickness 0.384</c> and <c>length-fraction 0.8</c> on it and on its Stems,
    ///   so it goes through lily/beam-quanting.cc like any other. There is no second
    ///   algorithm, and until 2026-08-01 Lily# had one: the renderer drew the beam parallel
    ///   to the head contour at a fixed stem length, which is off LilyPond's quant grid
    ///   entirely (ledger beam.quant.grace.*).
    /// <para>
    /// The x frame is the run's own column chain — <see cref="SpacingRules.GraceColumns"/>,
    /// the same offsets the reservation and the renderer use. Only the SPAN reaches the
    /// quanter, and an ossia scales x and y alike, so the unscaled staff-space frame here is
    /// the right one for both.
    /// </para>
    /// </remarks>
    internal static (double?, double?) QuantGraceBeam(
        GraceNoteItem grace, ImmutableArray<double> columnOffsets)
    {
        var notes = grace.Columns;
        // ⚠️ THE GATE IS ASKED, NOT RE-DERIVED. This block used to spell IsBeamedRun out
        // again - `notes.Length < 2`, then `BeamCountForDuration(...) < 1` for every head -
        // under a comment that said "the same gate the renderer draws by". Both spellings
        // computed the same predicate, so nothing was wrong on the page; what was wrong is
        // that there were two of them, which is the defect checklist 7.7 calls this
        // repository's most repeated, and which the remarks on IsBeamedRun itself argue
        // against three lines below ("a run that is beamed for one of them and flagged for
        // another reserves a box it does not fill").
        // ⚠️ THE VALUE IS PROSPECTIVE, AND THE MEASUREMENT SAYS SO RATHER THAN THE OTHER
        // WAY ROUND. This comment first claimed the fold was load-bearing - "poison
        // IsBeamedRun and the quanter's tests go green before it and red after" - and the
        // experiment REFUTED that: poisoning IsBeamedRun to answer "not beamed" reddens
        // exactly 29 tests, the SAME 29, folded or unfolded (session 302, scratch/p302/
        // foldproof.py). Of course it does: with nothing beamed, the renderer and the
        // reservation move the page on their own and this method's answer is never read.
        // ⇒ The two spellings were INDISTINGUISHABLE, which is what makes the fold safe, not
        // what makes it pointless: no book was ever drawn wrong and none changes now. What it
        // buys is the FUTURE - the open half of docs/HANDOFF.md §2 U8 (a REST in a grace body)
        // has to teach this predicate what a member with NO HEAD does to a run, and with two
        // spellings only one of them would have been taught.
        // ⚠️ THE CHORD LEFT THAT LIST (session 308) WITHOUT TOUCHING THIS PREDICATE, which is
        // the measurement that split the ticket: LilyPond beams a grace run the same way
        // whether or not one of its columns is a chord (session 302's member table - polygon 2
        // wherever the chord stands). A rest is the one that changes the answer.
        // ⚠️ NOTHING GUARDS THE FOLD, and that follows from the same experiment: if someone
        // re-inlines the gate, no test goes red. There is nothing to guard once there is one
        // house - the drift this prevents cannot happen while the second spelling is absent.
        // ⚠️ THE PREFIX, NOT THE RUN. The beam covers the leading run of head-columns and
        // stops at the first rest; MEASURED that what follows does not enter it at all —
        // `{ d'16 e'16 r16 f'16 }` is quanted to the same four digits as `{ d'16 e'16 }`
        // (see BeamedPrefix). So the whole of the "partial beam group" is: hand the quanter
        // the prefix and nothing else.
        int beamed = BeamedPrefix(notes);
        if (beamed == 0) return (null, null);
        if (columnOffsets.IsDefault || columnOffsets.Length != notes.Length)
            return (null, null);

        var counts = new int[beamed];
        for (int i = 0; i < beamed; i++)
            counts[i] = BeamCountForDuration(notes[i].BaseDuration.Denominator);

        var members = ImmutableArray.CreateBuilder<BeamMember>(beamed);
        var xs = new double[beamed];
        for (int i = 0; i < beamed; i++)
        {
            xs[i] = columnOffsets[i];
            // A beam line reaches a neighbour only if BOTH stems carry it — and the LAST
            // column of the prefix has no neighbour inside the beam, whatever stands after it.
            int left = i > 0 ? Math.Min(counts[i], counts[i - 1]) : 0;
            int right = i < beamed - 1 ? Math.Min(counts[i], counts[i + 1]) : 0;
            // A CHORD hands the quanter its head RANGE, not one of its heads: the beam's view
            // of a member is Stem::head_positions(me)[my_dir] (lily/stem.cc:1214), which for a
            // stem-up chord is the TOP head, and BeamMember says so through
            // HeadPositionMin/Max. StaffPosition itself is documented as the heads' mean for a
            // chord — see BeamMember's remarks, which name it as the quanter's NON-input.
            var heads = notes[i].Heads;
            int lo = notes[i].Lowest.StaffPosition;
            int hi = notes[i].Highest.StaffPosition;
            int sum = 0;
            foreach (var h in heads) sum += h.StaffPosition;
            members.Add(new BeamMember(
                GraceColumnHeads.StandIn(notes[i], grace.SourcePosition),
                counts[i], left, right, sum / heads.Length, i,
                // LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings —
                //   ((Voice Stem direction ,UP)): a grace stem is forced up whatever the pitch.
                memberStemUp: true,
                headPositionMin: lo,
                headPositionMax: hi));
        }

        var group = new BeamGroup(members.ToImmutable(), grace.MeasureIndex, 0, stemUp: true);
        var (leftY, rightY) = new BeamScoringProblem(
            group, xs,
            lengthFraction: EngravingDefaults.GraceBeamLengthFraction,
            beamThickness: EngravingDefaults.GraceBeamThickness,
            headFont: GraceNoteItem.Font).Solve();
        return (leftY, rightY);
    }

    /// <summary>
    /// Whether a grace run draws ONE BEAM rather than a flag per head — the one house for
    /// that gate.
    /// </summary>
    /// <remarks>
    /// A run is beamed only when every head carries at least one beam; a lone grace, a
    /// quarter-or-longer one, or a mixed group draws flags instead.
    /// <para>
    /// ⚠️ FOUR READERS, ONE SENTENCE, and it was three sentences until session 299 wrote a
    /// fourth and noticed. <c>SpacingRules.GraceColumns</c> reserves by it (a flag is ink in
    /// its column's right skyline), <see cref="QuantGraceBeam"/> quants by it,
    /// <see cref="Dots"/> asks it because a flag can stand in the dots' way, and
    /// <c>SharedRenderer.DrawGraceStemsAndBeam</c> draws by it. A run that is beamed for one
    /// of them and flagged for another reserves a box it does not fill — the exact drift
    /// docs/RULES.md §7.7 lists as "the second spelling of one quantity".
    /// <para>
    /// ⚠️⚠️ AND ONE OF THE FOUR WAS NOT ASKING — IT WAS RE-DERIVING (session 302, found by
    /// counting the readers of a "one sentence, N readers" comment the way session 301's rule
    /// says to: grep the callers, and count the SPELLINGS as well as the call sites).
    /// <see cref="QuantGraceBeam"/> spelled this predicate out again inside its own loop, so
    /// the grep for <c>IsBeamedRun</c> answered TWO where the comment said four. Both
    /// spellings agreed, so no book was ever drawn wrong; the cost was all in the future,
    /// because the open half of docs/HANDOFF.md §2 U8 has to change exactly this predicate.
    /// </para>
    /// </para>
    /// </remarks>
    internal static bool IsBeamedRun(ImmutableArray<GraceColumnInfo> notes)
        => BeamedPrefix(notes) > 0;

    /// <summary>
    /// How many columns of a grace run the ONE beam covers, counted from the FRONT: the
    /// leading run of head-columns when it holds two or more beamable ones, and 0 when the
    /// run draws flags instead. Every column at or after this index draws a flag.
    /// </summary>
    /// <remarks>
    /// MEASURED on LilyPond (scratch/p308/lp2, twelve books, read as COORDINATES rather than
    /// as the polygon counts session 302 read):
    /// <list type="bullet">
    /// <item><c>{ d'16 e'16 r16 f'16 }</c> — beam span 1.4679, y 11.0386..11.7006, and
    /// <c>f'</c> takes a FLAG. Those are the SAME four digits <c>{ d'16 e'16 }</c> gets on
    /// its own, so this is not "a partial beam" needing its own quanter: it is the ORDINARY
    /// beam of the prefix, and what follows the prefix does not enter it.</item>
    /// <item><c>{ d'16 r16 e'16 f'16 }</c> — NO beam at all, although <c>e' f'</c> are two
    /// adjacent beamable heads. So the rule is not "beam each maximal run"; it is the FIRST
    /// run or nothing. Session 302 filed this book as an "outlier" and corrected its own rule
    /// twenty minutes after committing it — the coordinates say what it concluded.</item>
    /// <item><c>{ d'16 e'16 r8 f'16 }</c> — the same 1.4679: the rest's own DURATION does not
    /// enter, only the fact that it holds no head.</item>
    /// <item><c>{ d'16[ e'16] f'16 }</c> — also 1.4679. A manual <c>[ ]</c> prefix and a
    /// rest-terminated one produce the same geometry, which is why one rule serves the rest
    /// half of docs/HANDOFF.md §2 U8 ⒜ and the beam half of ⒞.</item>
    /// </list>
    /// ⚠️ MEASURED ON WSL's 2.27.3, AND SAID RATHER THAN HIDDEN — but for this quantity the
    /// version does not matter, and that too is measured rather than hoped: session 300 took
    /// two of these books on CANONICAL 2.26.0 and got spans of 1.4679 and 2.8859, the same
    /// four digits (scratch/p308/lp2/measurements.md carries the table).
    /// <para>
    /// ⚠️ THIS RETURNS A COUNT, NOT A BOOL, and <see cref="IsBeamedRun"/> is derived from it.
    /// The four readers that predicate served — the reservation, the quanter, the dot column
    /// and the renderer — each need to know WHICH columns are beamed, not only whether any
    /// are: a column outside the prefix reserves a flag and draws one.
    /// </para>
    /// </remarks>
    internal static int BeamedPrefix(ImmutableArray<GraceColumnInfo> notes)
    {
        if (notes.IsDefaultOrEmpty || notes.Length < 2)
            return 0;
        int n = 0;
        foreach (var note in notes)
        {
            // A rest ends the prefix, and so does a column whose value carries no beam.
            // ⚠️ A SPACER ENDS IT TOO, and that is MEASURED rather than inherited from the
            // fact that IsRest happens to cover it: `\grace { d'16 s16 e'16 }` draws two
            // FLAGGED heads and no beam on LilyPond, exactly as the same book with a written
            // rest does (scratch/p308/lp2/t1_spacermid). An invisible column still breaks the
            // run — what ends the beam is the absence of a HEAD, not the presence of ink.
            if (note.IsRest || BeamCountForDuration(note.BaseDuration.Denominator) < 1)
                break;
            n++;
        }
        return n >= 2 ? n : 0;
    }

    /// <summary>
    /// How long a grace stem is, in staff spaces — the ONE house for that length, read by
    /// the renderer that draws it and by <see cref="Dots"/>, which needs to know where the
    /// flag hangs before it can say whether the flag is in the dots' way.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:635-648 general-grace-settings —
    ///   <c>(Voice Stem font-size -3)</c>, so an ordinary
    ///   <see cref="EngravingDefaults.DefaultStemLength"/> read at the grace's own scale.
    /// </remarks>
    internal static double StemLength(double scale)
        => EngravingDefaults.DefaultStemLength * scale;

    /// <summary>
    /// Where one grace note's augmentation dots stand: the X of the FIRST dot in the grace
    /// column's own frame, and the staff positions the dots ended up on. Empty when the
    /// grace carries no dot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The X is <see cref="DotColumn.OffsetX"/> — the one statement of LilyPond's dot-column
    /// rule — asked with the supports a grace column actually has: its stem, and its flag
    /// when the run draws flags rather than a beam (the same gate
    /// <see cref="QuantGraceBeam"/> and <c>SpacingRules.GraceColumns</c> use, so a group
    /// cannot be beamed for one of the three and flagged for another).
    /// </para>
    /// <para>
    /// ⚠️ THE FLAG IS THE REASON THIS IS NOT A CONSTANT. A grace stem is short, so its flag's
    /// lowest ink can reach a row a dot is on — and then LilyPond puts the dot right of the
    /// FLAG instead of right of the head. MEASURED on LilyPond 2.26.0 (scratch/p299): the
    /// same grace one step apart answers differently, 1.226600 for a head in a space and
    /// 1.747300 for a head on a line (whose dot is lifted into the flag). A full-size note's
    /// flag never reaches, which is why <c>DrawNote</c>'s answer has always been the plain
    /// one and still is. See <see cref="DotColumn"/> for the whole pair.
    /// </para>
    /// <para>
    /// Everything here is in the grace's OWN staff spaces (its font, its stem length); an
    /// ossia multiplies the finished offset, exactly as it does the column offsets.
    /// </para>
    /// </remarks>
    internal static (double X, ImmutableArray<int> Positions) Dots(
        GraceColumnInfo note, bool beamed)
    {
        if (note.Dots <= 0)
            return (0, ImmutableArray<int>.Empty);

        var font = GraceNoteItem.Font;
        // The QUARTER's attachment, because that is the head the renderer actually draws for
        // every grace duration — the standing approximation recorded on graceHeadNoteValue in
        // SharedRenderer.DrawGraceStemsAndBeam, read from here rather than restated. The dots
        // have to stand relative to the ink that is on the page, not to the ink a
        // per-duration head would have made.
        const int graceHeadNoteValue = 4;
        // LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings —
        //   ((Voice Stem direction ,UP)): a grace stem is forced up, so its Dots prefer UP
        //   too (scm/music-functions.scm:616-631 direction-polyphonic-grobs pairs them).
        // EVERY head of the column gets a dot, and the whole set is resolved together so two
        // heads a second apart do not both claim the same row — the same DotConfiguration a
        // full-size chord goes through.
        // LILYPOND-REF: lily/dot-column.cc:194-227 Dot_column::calc_positioning_done — one
        //   Dot_configuration is built per DotColumn and EVERY dot of the column is placed
        //   into it (`cfg[p] = dp` in the loop over `dots`), so a chord's heads share one
        //   solve and `remove_collision` is what keeps two of them off the same row.
        var heads = note.Heads;
        var headPositions = new int[heads.Length];
        var dotCounts = new int[heads.Length];
        for (int i = 0; i < heads.Length; i++)
        {
            headPositions[i] = heads[i].StaffPosition;
            dotCounts[i] = 1;
        }
        var positions = ImmutableArray.Create(
            DotConfiguration.Resolve(headPositions, dotCounts));

        var supports = new List<DotColumn.Support>();
        double stemX = LayoutUtilities.StemAttachX(
            up: true, graceHeadNoteValue, NoteheadStyle.Default, font);
        // ⚠️ THE STEM IS FAITHFUL AND INERT, and saying so is the point. LilyPond puts it in
        // the support list, so it is here; but a grace stem is forced UP, which stands it at
        // the head's own attachment less half its thickness — always LEFT of the ink right
        // this skyline is floored on, so the max never picks it. MEASURED as an
        // indistinguishability (session 299): poisoning the WHOLE skyline off and poisoning
        // only the FLAG off turn the same assert of
        // AGraceDotClearsTheFlagOnlyWhenTheFlagIsOnItsRow red, because the flag is the only
        // support that ever binds. It becomes observable the day a grace head is drawn per
        // duration (the standing note on graceHeadNoteValue) and a wider head moves the edge.
        // ⚠️ THE LOWEST HEAD, because a stem-up stem stands on it. For a single head the two
        // readings are the same number, so the one-note books stay byte-identical.
        supports.Add(DotColumn.StemSupport(
            note.Lowest.StaffPosition, up: true, stemX + EngravingDefaults.StemThickness / 2));

        if (!beamed && note.BaseDuration.Numerator == 1 && note.BaseDuration.Denominator >= 8)
        {
            var flag = GlyphMetrics.GetFlagBBox(font, note.BaseDuration.Denominator, stemUp: true);
            if (flag != default)
            {
                // The flag hangs at the stem's end, and the bbox is relative to that anchor.
                // Position → staff spaces is the house's `* 0.5` (ElementCoordinator does the
                // same to build a head's own box), then the stem on top of it.
                // ⚠️ THE HIGHEST HEAD: a stem-up stem is measured from the TOP of the head
                // column, which is Stem::chord_start_y for this direction (lily/stem.cc:114-122).
                double stemEnd = note.Highest.StaffPosition * 0.5
                                 + StemLength(GraceNoteItem.ScaleFactor);
                supports.Add(DotColumn.FlagSupport(
                    stemEnd + flag.Bottom, stemEnd + flag.Top, stemX + flag.Right));
            }
        }

        return (DotColumn.OffsetX(
                    font.NoteheadBlack.Width, supports, positions, font.AugmentationDot.Width),
                positions);
    }

    /// <summary>Number of beam lines for a duration denominator (8th = 1, 16th = 2, …);
    /// 0 for a quarter or longer, which carries no beam.</summary>
    private static int BeamCountForDuration(int denominator)
    {
        int beams = 0;
        for (int d = denominator; d >= 8; d /= 2) beams++;
        return beams;
    }

    /// <summary>
    /// Extra leftward shift for a grace group so its flag clears a wide above-script
    /// (fermata / ornament) on the main note. Zero unless the main note carries such
    /// a script AND the grace sits at or above it (only then does the grace's flag
    /// rise into the script's band). The amount is the script's left overhang past
    /// where the grace would otherwise reach, plus the grace's own flag reach — every
    /// term glyph-derived, no hand-tuned constant.
    /// </summary>
    private static double ScriptOverhangForGrace(
        ImmutableArray<ArticulationItem> articulations, GraceNoteItem grace, Measure measure,
        double graceGroupWidth)
    {
        if (articulations.IsDefaultOrEmpty || grace.Columns.IsDefaultOrEmpty
            || grace.MainNoteItemIndex >= measure.Items.Length)
            return 0;
        var mainItem = measure.Items[grace.MainNoteItemIndex];

        int mainPos = mainItem switch
        {
            NoteItem n => n.StaffPosition,
            ChordItem { Notes.Length: > 0 } c => c.Notes.Max(cn => cn.StaffPosition),
            _ => 0
        };
        // Y-gate proxy: a grace below the main note keeps its flag out of the
        // above-script's band, so it needs no extra room.
        // The HIGHEST head of the highest column: the gate asks whether any grace ink
        // rises into the script's band, so a chord answers with its top head.
        if (grace.Columns.Max(n => n.Highest.StaffPosition) < mainPos)
            return 0;

        foreach (var art in articulations)
        {
            if (art.StaffIndex != grace.StaffIndex || art.MeasureIndex != grace.MeasureIndex
                || art.ItemIndex != grace.MainNoteItemIndex)
                continue;
            if (ArticulationEngraver.SpacingInkBox(art, mainItem, staffY: 0) is not { } box)
                continue;
            // Clear the grace's rightmost ink from the script's left edge by the SAME
            // gap LilyPond keeps between two note-column grobs: each grob's separation
            // box grows by extra-spacing-width (default 0.1) on the facing side, so a
            // script and a grace clear by 0.1 + 0.1. Everything else is a real glyph
            // extent, not a tuned constant.
            // LILYPOND-REF: lily/separation-item.cc extra-spacing-width default
            //   (-0.1 . 0.1); scm/define-grobs.scm Script inherits it.
            double leftFromCenter = -box.XLeft;                 // script left edge
            int noteValue = mainItem.Duration.Denominator <= 1 ? 1
                : mainItem.Duration.Denominator <= 2 ? 2 : 4;
            double centerX = GlyphMetrics.GetNoteheadBBox(noteValue).CenterX;
            // The grace's rightmost ink, measured LEFTWARD from the main centre.
            double graceInkRightFromCenter = centerX + graceGroupWidth
                - GraceInkRight(grace, graceGroupWidth);
            double overhang = leftFromCenter + 2 * ArticulationSpacing.ScriptExtraSpacingWidth
                            - graceInkRightFromCenter;
            return Math.Max(0, overhang);
        }
        return 0;
    }

    /// <summary>The grace group's rightmost ink, measured from the group's LEFT edge.
    /// A single flagged grace protrudes past its head by the flag (placed at the stem
    /// like a normal note's, then scaled); any other group keeps its ink within the
    /// reserved spring width, so the junction (width) is used.</summary>
    private static double GraceInkRight(GraceNoteItem grace, double graceGroupWidth)
    {
        if (grace.Columns.Length == 1)
        {
            var d = grace.Columns[0].BaseDuration;
            if (d.Numerator == 1 && d.Denominator >= 8)
            {
                // Read from the grace's own font, exactly as SpacingRules.GraceColumnRightReach
                // does — this is the same ink, measured for a different caller.
                var font = GraceNoteItem.Font;
                var flag = GlyphMetrics.GetFlagBBox(font, d.Denominator, stemUp: true);
                if (flag != default)
                    return LayoutUtilities.StemAttachX(
                        up: true, GlyphMetrics.NoteValueOf(d),
                        NoteheadStyle.Default, font) + flag.Width;
            }
        }
        return graceGroupWidth;
    }

    /// <summary>
    /// The width a group of <paramref name="noteCount"/> graces needs, for the one caller
    /// that has a count and no durations.
    /// </summary>
    /// <remarks>
    /// The same law as <see cref="SpacingRules.GraceColumns"/>, over a uniform run — and a
    /// uniform run's gaps do not depend on the note value at all, because
    /// <c>grace-spacing::calc-shortest-duration</c> normalises by the run's own minimum
    /// (scm/output-lib.scm:1403-1422, and MEASURED: ledger grace.column.two-eighths.step and
    /// grace.column.two-thirtyseconds.step both read what the sixteenths do). So this is not
    /// an approximation of the duration-aware answer for uniform input; it IS it.
    /// </remarks>
    public static double GetGraceGroupWidth(int noteCount)
    {
        if (noteCount <= 0) return 0;
        var uniform = ImmutableArray.CreateBuilder<GraceColumnInfo>(noteCount);
        for (int i = 0; i < noteCount; i++)
            uniform.Add(new GraceColumnInfo(0, null, false, Fraction.Eighth));
        return SpacingRules.CalculateGraceGroupSpringWidth(uniform.ToImmutable());
    }

    /// <summary>
    /// Gets the total width required for a grace note group using spring-based calculation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:46 Grace_spacing_engraver::process_music
    /// Uses per-group common shortest duration for LP-compliant spacing.
    /// </remarks>
    public static double GetGraceGroupWidth(ImmutableArray<GraceColumnInfo> notes)
    {
        return SpacingRules.CalculateGraceGroupSpringWidth(notes);
    }
}