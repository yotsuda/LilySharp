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
    ImmutableArray<GraceNoteInfo> Notes, // Notes in the grace group
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
    // grace as a small fret number (resolved from GraceNoteInfo.Midi) instead of
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
            var columns = SpacingRules.GraceColumns(grace.Notes, mainItem);
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
                grace.Notes,
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
        var notes = grace.Notes;
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
        // buys is the FUTURE - the open half of docs/HANDOFF.md §2 U8 (a chord or a rest in a
        // grace body) has to teach this predicate what a non-head member does to a run, and
        // with two spellings only one of them would have been taught.
        // ⚠️ NOTHING GUARDS THE FOLD, and that follows from the same experiment: if someone
        // re-inlines the gate, no test goes red. There is nothing to guard once there is one
        // house - the drift this prevents cannot happen while the second spelling is absent.
        if (!IsBeamedRun(notes)) return (null, null);
        if (columnOffsets.IsDefault || columnOffsets.Length != notes.Length)
            return (null, null);

        var counts = new int[notes.Length];
        for (int i = 0; i < notes.Length; i++)
            counts[i] = BeamCountForDuration(notes[i].BaseDuration.Denominator);

        var members = ImmutableArray.CreateBuilder<BeamMember>(notes.Length);
        var xs = new double[notes.Length];
        for (int i = 0; i < notes.Length; i++)
        {
            xs[i] = columnOffsets[i];
            // A beam line reaches a neighbour only if BOTH stems carry it.
            int left = i > 0 ? Math.Min(counts[i], counts[i - 1]) : 0;
            int right = i < notes.Length - 1 ? Math.Min(counts[i], counts[i + 1]) : 0;
            members.Add(new BeamMember(
                new NoteItem(notes[i].StaffPosition, notes[i].BaseDuration, 0,
                             notes[i].Accidental, notes[i].NeedsLedger, grace.SourcePosition),
                counts[i], left, right, notes[i].StaffPosition, i,
                // LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings —
                //   ((Voice Stem direction ,UP)): a grace stem is forced up whatever the pitch.
                memberStemUp: true));
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
    internal static bool IsBeamedRun(ImmutableArray<GraceNoteInfo> notes)
    {
        if (notes.IsDefaultOrEmpty || notes.Length < 2)
            return false;
        foreach (var n in notes)
            if (BeamCountForDuration(n.BaseDuration.Denominator) < 1)
                return false;
        return true;
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
        GraceNoteInfo note, bool beamed)
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
        var positions = ImmutableArray.Create(
            DotConfiguration.Resolve(new[] { note.StaffPosition }, new[] { 1 })[0]);

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
        supports.Add(DotColumn.StemSupport(
            note.StaffPosition, up: true, stemX + EngravingDefaults.StemThickness / 2));

        if (!beamed && note.BaseDuration.Numerator == 1 && note.BaseDuration.Denominator >= 8)
        {
            var flag = GlyphMetrics.GetFlagBBox(font, note.BaseDuration.Denominator, stemUp: true);
            if (flag != default)
            {
                // The flag hangs at the stem's end, and the bbox is relative to that anchor.
                // Position → staff spaces is the house's `* 0.5` (ElementCoordinator does the
                // same to build a head's own box), then the stem on top of it.
                double stemEnd = note.StaffPosition * 0.5 + StemLength(GraceNoteItem.ScaleFactor);
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
        if (articulations.IsDefaultOrEmpty || grace.Notes.IsDefaultOrEmpty
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
        if (grace.Notes.Max(n => n.StaffPosition) < mainPos)
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
        if (grace.Notes.Length == 1)
        {
            var d = grace.Notes[0].BaseDuration;
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
        var uniform = ImmutableArray.CreateBuilder<GraceNoteInfo>(noteCount);
        for (int i = 0; i < noteCount; i++)
            uniform.Add(new GraceNoteInfo(0, null, false, Fraction.Eighth));
        return SpacingRules.CalculateGraceGroupSpringWidth(uniform.ToImmutable());
    }

    /// <summary>
    /// Gets the total width required for a grace note group using spring-based calculation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:46 Grace_spacing_engraver::process_music
    /// Uses per-group common shortest duration for LP-compliant spacing.
    /// </remarks>
    public static double GetGraceGroupWidth(ImmutableArray<GraceNoteInfo> notes)
    {
        return SpacingRules.CalculateGraceGroupSpringWidth(notes);
    }
}