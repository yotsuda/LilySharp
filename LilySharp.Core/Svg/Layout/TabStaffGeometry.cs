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

using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Sizes shared by every tab-staff site. Previously the fret font size lived as a
/// private const in SharedRenderer AND a hard-coded literal in ElementCoordinator
/// (a silent desync if either was tuned); this is now the single source.
/// </summary>
internal static class TabConstants
{
    /// <summary>Font size of a tab fret number, in staff spaces.</summary>
    /// <remarks>
    /// LILYSHARP-OWN: deliberately LARGER than LilyPond's, whose tab digits are small
    /// enough to be hard to read (a digit's ink here is roughly double the 0.990155
    /// LilyPond's TabNoteHead measures — audit/lp-geometry/probes/line-start-mindist.ly,
    /// score TKC). The collisions that follow from bigger digits are solved rather than
    /// avoided: chords stagger their digits into two columns (the zigzag), and the columns
    /// reserve that real extent (SpacingRules.ApplyTabChordSpacing).
    /// <para>
    /// TUNED BY EYE, on request, and the history is the spec: 2.6 → 2.9 (with the opaque
    /// background dropped — the string line breaks around the digit, so the digit carries
    /// the contrast on its own) → 3.3 → 3.0 (2026-08-06, "3.3 は少し大きすぎる" on a bass
    /// tab). Every consumer — width reservation, string-line gap, skyline, stem clearance,
    /// articulation clearing — derives from this one constant through the face metrics
    /// below, so tuning it is a one-line change.
    /// </para>
    /// <para>
    /// ⚠️ This is a RATIFIED deviation (docs/HANDOFF.md §3), not an un-ported LilyPond
    /// quantity. Do not "fix" it toward LilyPond. The tab STRING SPACING is a separate
    /// question and does follow LilyPond — see <c>EngravingDefaults.TabStringSpace</c>.
    /// </para>
    /// </remarks>
    public const double FretFontSize = 3.0;

    /// <summary>Grace fret digits relative to the main fret size.</summary>
    /// <remarks>
    /// LilyPond's ratio, applied to Lily#'s own (larger) base size: a normal
    /// TabNoteHead is font-size −2 and a grace one −4, two size steps = 2^(−2/6)
    /// ≈ 0.7937 (measured on 2.26.0: whiteout heights 0.9366/1.1800 — the
    /// tablature-grace-notes twin). Was 0.8, a Lily#-own round number.
    /// LILYPOND-REF: scm/music-functions.scm:636-650 general-grace-settings —
    ///   (Voice TabNoteHead font-size -4).
    /// LILYPOND-REF: scm/define-grobs.scm:3717-3745 tab-note-head::print — the
    ///   TabNoteHead block, (font-size . -2).
    /// </remarks>
    public const double GraceFretScale = 0.7937005259840998; // 2^(-2/6)

    /// <summary>Height (staff spaces) of a fret digit's occluding box — the digit's
    /// visual extent, centred on its string line. Shared by the renderer (the
    /// page-coloured background) and the articulation engraver (clearing a digit that
    /// protrudes past the outer staff line).</summary>
    /// <remarks>
    /// MEASURED from the face itself rather than declared: the bundled serif's bold digits
    /// ink 2.3826 tall at font size 3.3, i.e. 0.7220 of the size — the round ones overshoot
    /// both the cap line and the baseline, which a cap-height figure misses. This used to be
    /// a flat <c>0.6875</c>, so the box was 0.11 SHORTER than the ink it was meant to occlude
    /// and the digit poked out of its own background at top and bottom.
    /// <para>
    /// ⚠️ This is what puts a CEILING on <see cref="FretFontSize"/>: the box is measured
    /// against a string gap of 1.5 (EngravingDefaults.TabStringSpace), so it exceeds one gap
    /// at any size above about 2.08 and eats into the NEIGHBOURING string lines — visibly so
    /// past about 3.0. Enlarging the digits further means occluding less, not more: breaking
    /// the string line around the digit instead of painting a box over it.
    /// </para>
    /// <para>
    /// ⚠️⚠️ AND THIS IS WHY LILYPOND'S <c>TabNoteHead (whiteout . #t)</c> IS NOT PORTED, which
    /// is worth writing down here because a reader who finds that property will come looking
    /// at this constant. MEASURED on 2.26.0 (the test/tab-chord-tie twin, measuring
    /// <c>tab-note-head::print</c>'s stencil): LilyPond's digit is Y −0.590 … +0.590, i.e.
    /// 1.180 TALL — it fits inside the same 1.5 gap with 0.16 to spare either side, so its
    /// white box never reaches a neighbouring line. Lily#'s is 2.166, <b>1.44 times the
    /// gap</b>: the identical device would blank 0.333 of each neighbouring string line.
    /// LilyPond can afford an occluder because its digits are small; that premise is exactly
    /// what Lily# gave up (ratified, docs/HANDOFF.md §3).
    /// <para>
    /// The second fault is the one that removed the box here in the first place: an occluder
    /// SPENDS A COLOUR. It was the only opaque light element in the document, so a viewer
    /// that themes a page by inverting it — VS Code's dark mode — turned the box black and
    /// sat every fret number in a hole. A gap spends none.
    /// </para>
    /// ⇒ On a tab, overlaps are resolved by REMOVING ink, never by covering it. If the tie
    /// that crosses a neighbouring digit is ever worth fixing, the fix is to cut the TIE the
    /// way the string line is cut — not to paint a box.
    /// </para>
    /// <para>
    /// ⚠️ A dead note's <c>×</c> is a DIFFERENT shape (ink 0.436 of the size, centred lower),
    /// which is why the renderer asks the face per glyph for the BASELINE
    /// (<see cref="FretBaselineDrop"/>) instead of sharing one number. This height stays a
    /// digit's, because it is what the layout reserves and every reader of it is asking
    /// "how tall is a fret number".
    /// </para>
    /// </remarks>
    /// <remarks>
    /// ⚠️ THE BUNDLED FACE, NOT THE SCORE'S — and here that is nearly always the same
    /// thing. <see cref="Rendering.TextRole.TabFret"/> is NOTATION
    /// (<c>TextRoles.IsNotation</c>): a broad <c>font "Georgia"</c> or <c>fonts { serif … }</c>
    /// does not reach it, by decision, because a fret number is not prose. Only a score
    /// that names <c>notation</c> or <c>tabFret</c> outright binds it, and this and its two
    /// neighbours below still take the bundled face when it does.
    /// <para>
    /// The reason is this member: it is a <c>static readonly</c> initialised by the TYPE,
    /// which cannot be handed a score. Closing it means turning three shared quantities
    /// into per-score calls, and it is left named and counted rather than half-done.
    /// </para>
    /// </remarks>
    public static readonly double FretDigitHeight =
        Rendering.TextFontMetrics.InkHeight("0", FretFontSize, sans: false,
            style: Rendering.FontStyle.Bold);

    /// <summary>
    /// How far BELOW its string line a fret glyph's baseline sits, so the glyph's INK is
    /// centred on the line.
    /// </summary>
    /// <remarks>
    /// Asked of the face per glyph, because the answer is not one number: the bundled serif's
    /// bold digits centre 0.3470 of the font size above their baseline, and a dead note's
    /// <c>×</c> centres 0.2500 above its own. Both used to be drawn at a hard-coded
    /// <c>0.32 × fontSize</c>, which put a digit 0.089 too HIGH and an <c>×</c> 0.231 too LOW
    /// at font size 3.3 — the digit's gap to the line above it read visibly narrower than the
    /// gap below, which is how it was spotted.
    /// <para>
    /// The same principle the noteheads already follow: a grob's extent comes from its
    /// stencil, not from a nominal fraction (see SkylineBuilder's notehead seed, which takes
    /// the LILC ink for exactly this reason).
    /// </para>
    /// </remarks>
    public static double FretBaselineDrop(string glyph, double fontSize)
    {
        var (bottom, top) = Rendering.TextFontMetrics.Ink(
            glyph, fontSize, sans: false, style: Rendering.FontStyle.Bold);
        return (top + bottom) / 2;
    }

    /// <summary>The drawn width of a fret glyph — its advance in the face it is set in.</summary>
    /// <remarks>
    /// One house for the width, read by the spacing reservation (so two digits are given room
    /// for two digits), by the string-line gap (so the hole is the glyph's own width) and by
    /// the skyline (so the chord row above knows how wide the number under it is).
    /// <para>
    /// ⚠️ IT USED TO BE A DIGIT COUNT, and it was wrong in BOTH directions. One digit was
    /// declared 0.625 of the font size and two digits 1.0, against a measured advance of
    /// 0.5740 each — so a single digit was over-reserved by 0.17 and a PAIR under-reserved by
    /// 0.49, which is why frets from 10 up crowded their neighbours at every size while
    /// single digits sat slightly too far apart. The face sets digits tabular, so two of them
    /// are exactly twice one; nothing about that was derivable from the count alone.
    /// </para>
    /// </remarks>
    public static double FretGlyphWidth(string glyph, double fontSize)
        => Rendering.TextFontMetrics.Advance(
            glyph, fontSize, sans: false, style: Rendering.FontStyle.Bold);

    /// <summary>
    /// Clear air the spacing engine keeps BETWEEN one column's fret digits and the next
    /// column's.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN. Reserving the glyph WIDTH alone only promises the digits will not
    /// overprint, and "not overprinting" is not the same as legible: MEASURED on a chromatic
    /// run at font size 3.3, single digits fell 0.606–0.866 apart because the musical spacing
    /// was already wider than they needed, while two-digit frets — where the reservation is
    /// what binds — closed to exactly this gap. The same rhythm of numbers read at two
    /// different densities depending on the fret. Raised 0.2 → 0.6 so the binding case reads
    /// like the loose one; measured after, the run comes out 0.796–0.866 throughout.
    /// <para>
    /// ⚠️ IT BELONGS BETWEEN COLUMNS, NOT IN A COLUMN'S EXTENT. Folding it into
    /// <c>TabItemHalfExtent</c> also pushed the FIRST note of a line, which is placed from
    /// that extent with no neighbour to clear, and moved the measured point
    /// <c>line-start.time-to-first-note.tab-*</c> 0.023550 off LilyPond. A gap is a relation
    /// between two things; an extent is a property of one.
    /// </para>
    /// </remarks>
    public const double FretColumnGap = 0.6;

    /// <summary>
    /// How far from its string line a tab stem's NEAR end is drawn — where LilyPond's
    /// <c>stem-begin-position</c> puts it: on the far side of the digit, 1.35 of the digit's
    /// half-height from the line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-head.cc:224-234 Note_head::calc_tab_stem_attachment — a
    ///   TabNoteHead's <c>stem-attachment</c> is <c>(0, dir * 1.35)</c>, in the head's own
    ///   half-extents.
    /// LILYPOND-REF: lily/stem.cc:945-960 internal_calc_stem_begin_position —
    ///   <c>pos += y_attach * 2 / ss</c> with <c>y_attach = head_height.linear_combination
    ///   (attachment)</c>, so the stem begins <c>1.35 × (digit height / 2)</c> from the string
    ///   line, past the digit's edge by 0.35 of its half-height.
    /// LILYPOND-REF: scm/define-grobs.scm:3741-3743 TabNoteHead stem-attachment, whiteout —
    ///   the digit is whited out, so the stem is drawn from its far edge and never through it.
    /// <para>
    /// MEASURED on 2.26.0 (scratch/p337/sugar3, 488 stems of the owner's Sugar.ly with
    /// <c>TabNoteHead.font-size = 2</c>, digit half-height 1.0): every
    /// <c>stem-begin-position</c> sat ±1.8 half-tab-spaces = ±1.35 ss from its string.
    /// </para>
    /// <para>
    /// ⚠️ This is NOT where the stem ENDS. LilyPond's stem end is a function of the head
    /// position and the duration alone (<see cref="StemCalculator.CalculateStemEndPosition"/>);
    /// the begin only shortens the visible line. Before 2026-09-05 Lily# added a fixed
    /// length to a "clearance" that centred the near end between digit and next line —
    /// so the tip moved with the digit size, and every tab system stood 1.45 ss taller
    /// than LilyPond's (HANDOFF §1 第337 ⑺).
    /// </para>
    /// </remarks>
    public static double StemBeginOffset() => 1.35 * FretDigitHeight / 2;

    /// <summary>
    /// A tab beam's <c>length-fraction</c>: 0.62, the one number LilyPond states rather than
    /// derives when it re-tunes beams for the wider tab staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:1234-1246 — beam-thickness, length-fraction, staff-symbol-staff-space
    ///   in the TabStaff context's beam block,
    ///   under the comment "TabStaff increase
    ///   the staff-space, which in turn increases beam thickness and spacing; beams are too
    ///   big. We have to adjust the beam settings", then
    ///   <c>\override Beam.beam-thickness = #0.32</c> and
    ///   <c>\override Beam.length-fraction = #0.62</c>.
    /// It buys two things and neither is a stem length: the beam translation
    /// (lily/beam.cc:142-144 Beam::get_beam_translation, giving 0.480667 against a notation
    /// staff's 0.81) and the weight of the forbidden-quant scorer, which LilyPond scales by
    /// <c>exp(−8·|1 − length-fraction|)</c> because "for stems that are non-standard, the
    /// forbidden beam quanting doesn't really work" (lily/beam-quanting.cc:80-87
    /// Beam_quant_parameters::fill) — 0.0478 of the full charge here.
    /// <para>
    /// ⚠️ It is the BEAM's, not the STEM's: <c>\tabFullNotation</c> reverts
    /// <c>Stem.details</c> and <c>Stem.no-stem-extend</c> and leaves these two alone, so a tab
    /// stem is bought with ordinary beamed-lengths.
    /// </para>
    /// </remarks>
    public const double BeamLengthFraction = 0.62;
}

/// <summary>
/// A straight tab beam line in device Y, produced by <see cref="TabBeamQuant"/>.
/// <see cref="At"/> evaluates it at any x. Shared by the renderer's beam pass and
/// the articulation engraver (a forced-above script must clear the same beam).
/// </summary>
internal static class TabBeamMath
{
    public static double At((double Slope, double InterceptY, double FirstX) line, double x)
        => line.Slope * (x - line.FirstX) + line.InterceptY;
}

/// <summary>
/// Quants a TAB beam through the ported LilyPond quanter
/// (<see cref="BeamScoringProblem"/>), fed the notes' STRING lines as stem positions and
/// the TAB staff's own constants. Returns the beam line in DEVICE Y (evaluate with
/// <see cref="TabBeamMath.At"/>).
/// </summary>
/// <remarks>
/// LilyPond runs a tab beam through the same quanter as a notation beam. Everything the
/// quanter reads is expressed in the staff's OWN spaces, and on a TabStaff that space is
/// 1.5 — so the lengths (beamed-lengths and friends) come through unchanged, while the two
/// thicknesses, which LilyPond builds from absolute quantities and then divides by the
/// staff space (lily/beam-quanting.cc:232-234), arrive scaled:
/// <code>
///                    notation   tab (space 1.5)
///   beam-thickness     0.48        0.32   = 0.48/1.5   ← MEASURED off LilyPond's grob
///   line thickness     0.10        0.0667 = 0.10/1.5
///   ⇒ sit/hang quant   0.19        0.12667                (thickness − line)/2
///   ⇒ beam translation 0.81        0.87333
/// </code>
/// ⚠️ THE STRINGS ARE ONE STAFF SPACE APART IN THAT FRAME, not 1.5. A four-string tab is
/// positions (3, 1, −1, −3), exactly what LilyPond's TabNoteHead reports; the 1.5 lives in
/// the staff's space, not in the positions. An earlier attempt at this route (<c>0251bb0f</c>)
/// spelled the strings three half-spaces apart and left the notation staff's thicknesses in
/// place — a stretched notation staff rather than a tab one — and was replaced by hand-fitted
/// arithmetic (<c>d06686ee</c>) whose flat groups sat 0.297 past LilyPond's.
/// <para>
/// ⚠️ Directions come from the STRINGS (<c>Compute</c>'s <c>stemUp</c>), not the notated pitch, so
/// the group handed to the quanter is re-stemmed. A tab beam is never kneed.
/// </para>
/// </remarks>
internal static class TabBeamQuant
{
    public static (double Slope, double InterceptY, double FirstX) Compute(
        BeamGroup group, double[] memberStemXs, TabStaffGeometry geom, bool stemUp)
    {
        int n = group.Members.Length;
        double leftX = memberStemXs[0], rightX = memberStemXs[n - 1];

        // String s (1 = the top line) sits at staff position StringCount + 1 − 2s: the lines
        // of an N-line staff are one staff SPACE apart in that staff's own frame, whatever
        // the space measures on the page. A four-string tab is (3, 1, −1, −3).
        var stemPos = new int[n];
        int maxIdx = 0;
        for (int i = 0; i < n; i++)
        {
            int str = geom.StemHeadString(group.Members[i].Item, stemUp);
            stemPos[i] = geom.StringCount + 1 - 2 * str;
            if (group.Members[i].ItemIndex > maxIdx) maxIdx = group.Members[i].ItemIndex;
        }

        // The quanter reads x by ItemIndex and re-applies the notehead-edge offset itself.
        // Feeding it the stem x already offset only adds a CONSTANT to every member, which
        // cancels: the problem keeps x relative to its own left edge and the span is a
        // difference. It matters that the offsets are the same for all of them, and they
        // are — a tab beam is never kneed, so every stem takes the same side.
        var xById = new double[maxIdx + 1];
        for (int i = 0; i < n; i++)
            xById[group.Members[i].ItemIndex] = memberStemXs[i];

        // Re-stem the group from the STRINGS. A tab stem's direction is the string's, not
        // the notated pitch's — a bass run on the low strings beams UP where the notation
        // staff beams DOWN — and the quanter asks the group, not the caller.
        var members = System.Collections.Immutable.ImmutableArray.CreateBuilder<BeamMember>(n);
        foreach (var m in group.Members)
            members.Add(new BeamMember(
                m.Item, m.BeamCount, m.BeamCountLeft, m.BeamCountRight, m.StaffPosition,
                m.ItemIndex, memberStemUp: stemUp, targetStaffIndex: m.TargetStaffIndex,
                measureIndex: m.MeasureIndex,
                headPositionMin: m.HeadPositionMin, headPositionMax: m.HeadPositionMax));
        var tabGroup = new BeamGroup(members.ToImmutable(), group.MeasureIndex,
                                     group.StartIndex, stemUp, group.GrowDirection, group.VoiceIndex);

        // What a TabStaff changes, in LilyPond's own words (ly/engraver-init.ly:1234, the
        // comment above the two overrides at :1237 and :1238): "TabStaff increase the
        // staff-space, which in turn increases beam thickness and spacing; beams are too
        // big. We have to adjust the beam settings":
        //   \override Beam.beam-thickness  = #0.32   (= 0.48/1.5, the absolute thickness kept)
        //   \override Beam.length-fraction = #0.62
        // The line thickness follows the same division the quanter applies to both
        // (lily/beam-quanting.cc:232-234). Everything else is already in the staff's own
        // spaces on both sides and comes through untouched — in particular the STEM's
        // length-fraction stays 1, because \tabFullNotation reverts Stem.details and
        // Stem.no-stem-extend but leaves the two Beam overrides standing.
        double space = geom.StringSpace;
        var (leftPos, rightPos) = new BeamScoringProblem(
            tabGroup, xById,
            stemPositions: stemPos,
            beamThickness: EngravingDefaults.BeamThickness / space,
            lineThickness: EngravingDefaults.StaffLineThickness / space,
            staffLineCount: geom.StringCount,
            beamLengthFraction: TabConstants.BeamLengthFraction).Solve();

        // Quanter Y is in staff POSITIONS (half-spaces) above the staff's MIDDLE; the tab
        // staff's middle is halfway down its strings, and one position is half a string gap.
        double middleY = geom.StringY(1) + (geom.StringCount - 1) * space / 2;
        double leftY = middleY - leftPos * space / 2;
        double rightY = middleY - rightPos * space / 2;
        double slope = rightX - leftX > 0.001 ? (rightY - leftY) / (rightX - leftX) : 0.0;
        return (slope, leftY, leftX);
    }
}

/// <summary>
/// The geometry of one tab staff: its tuning-derived string spacing / octave shift,
/// and the string→Y and midi→fret conversions used by the renderer, the tie/grace
/// layout, and the articulation engraver. Consolidates the chain
/// (<c>OctaveShift + GetTuning + CalculateFret + TabStringSpace(GetStringCount)</c>)
/// and the <c>Y = staffY + (stringNum-1)*stringSpace</c> formula that were inlined
/// at half a dozen sites.
/// </summary>
internal readonly struct TabStaffGeometry
{
    private readonly int[] _tuning;
    private readonly int _octaveShift;

    /// <summary>Device-Y of the top tab line (string 1).</summary>
    public double StaffY { get; }
    /// <summary>Vertical distance between adjacent string lines, in staff spaces.</summary>
    public double StringSpace { get; }
    /// <summary>Number of strings for this tuning.</summary>
    public int StringCount { get; }

    public TabStaffGeometry(TuningType tuning, double staffY, ClefType clef = ClefType.Treble,
        int transposition = 0)
    {
        StaffY = staffY;
        _tuning = Tunings.GetTuning(tuning);
        _octaveShift = Tunings.SoundingShift(clef, transposition);
        StringCount = Tunings.GetStringCount(tuning);
        StringSpace = EngravingDefaults.TabStringSpace(StringCount);
    }

    /// <summary>Device-Y of a string's line (string 1 = top line).</summary>
    public double StringY(int stringNum) => StaffY + (stringNum - 1) * StringSpace;

    /// <summary>
    /// Resolves the (string, fret) for a written MIDI pitch, honouring a preferred
    /// string (0 = automatic). The tuning's 8vb octave shift is applied here.
    /// </summary>
    public (int stringNum, int fret) Fret(int writtenMidi, int? preferredString = null)
        => Tunings.CalculateFret(writtenMidi + _octaveShift, _tuning, preferredString ?? 0);

    /// <summary>Device-Y of the fret digit row for a written MIDI pitch.</summary>
    public double DigitY(int writtenMidi, int? preferredString = null)
        => StringY(Fret(writtenMidi, preferredString).stringNum);

    /// <summary>
    /// Device-Y of ONE chord note's fret digit, resolved through the chord's
    /// EXCLUSIVE string allocation — the same <c>CalculateChordFrets</c> the
    /// digits are drawn with — rather than a per-note auto-fret. A per-note
    /// <see cref="DigitY"/> ignores the other notes and can hand several chord
    /// notes the same low string, which is why a chord's per-string ties used to
    /// pile up at the bottom of the staff instead of hugging their own digits.
    /// Matches the note by STAFF POSITION — a chord tie's synthesized start note
    /// carries no MIDI, only its staff position — and returns that string's line.
    /// </summary>
    public double ChordNoteDigitY(ChordItem chord, int staffPosition)
        => StringY(ChordNoteDigitColumn(chord, staffPosition).StringNum);

    /// <summary>
    /// Everything about WHERE one chord note's fret digit is drawn: the string it was
    /// allocated, the zigzag column offset from the note column's digit axis, and half the
    /// digit's drawn width. The three answers come from ONE run of the exclusive allocation,
    /// so a reader of the x cannot disagree with a reader of the y about which note it is.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ORDER MATTERS AND IT IS THE RENDERER'S. <see cref="TabChordColumns.Offsets"/>
    /// is indexed by the notes sorted TOP STRING FIRST, which is how <c>DrawTabChord</c>
    /// calls it; resolving the offset against any other order hands a digit its neighbour's
    /// column. Matched by STAFF POSITION, like <see cref="ChordNoteDigitY"/> — a chord tie's
    /// synthesized start note carries no MIDI, only its staff position.
    /// <para>
    /// A note whose staff position is not in the chord, or whose allocation failed, answers
    /// string 1 with a zero-width digit at the axis — the same fallback
    /// <see cref="ChordNoteDigitY"/> has always had (it returned <see cref="StaffY"/>, which
    /// is string 1's line).
    /// </para>
    /// </remarks>
    public (int StringNum, double Dx, double HalfWidth) ChordNoteDigitColumn(
        ChordItem chord, int staffPosition)
    {
        int shift = _octaveShift;
        var alloc = Tunings.CalculateChordFrets(
            chord.Notes.Select(n => (n.Midi + shift, n.StringNumber)).ToList(), _tuning);
        var ordered = alloc
            .Select(p => (str: p.stringNum, fret: p.fret))
            .OrderBy(p => p.str)
            .ToList();
        for (int i = 0; i < chord.Notes.Length; i++)
        {
            if (chord.Notes[i].StaffPosition != staffPosition || alloc[i].stringNum < 1)
                continue;
            int k = ordered.FindIndex(p => p.str == alloc[i].stringNum);
            if (k < 0)
                break;
            return (alloc[i].stringNum,
                    TabChordColumns.Offsets(ordered)[k],
                    TabChordColumns.FretWidth(ordered[k].fret) / 2);
        }
        return (1, 0, 0);
    }

    /// <summary>
    /// Where the digit on ONE SIDE of an item is drawn — the TOP string's for
    /// <paramref name="top"/>, the BOTTOM string's otherwise. This is the tab's answer to
    /// LilyPond's <c>extremes_[d].slur_head_</c>: the single head a slur attaches to and
    /// measures its width from, which on a chord is the one on the slur's own side.
    /// </summary>
    /// <remarks>
    /// The allocation and the zigzag ordering are <see cref="ChordNoteDigitColumn"/>'s, so
    /// an item's slur end and its drawn digit cannot disagree about which column they mean.
    /// A note that resolved no string answers string 1 with a zero-width digit at the axis —
    /// the same fallback its neighbours have.
    /// </remarks>
    public (int StringNum, double Dx, double HalfWidth) EdgeDigitColumn(MusicItem item, bool top)
    {
        switch (item)
        {
            case NoteItem n:
                return NoteDigitColumn(n.Midi, n.StringNumber);
            case ChordItem c when c.Notes.Length > 0:
            {
                int shift = _octaveShift;
                var alloc = Tunings.CalculateChordFrets(
                    c.Notes.Select(x => (x.Midi + shift, x.StringNumber)).ToList(), _tuning);
                var ordered = alloc
                    .Select(p => (str: p.stringNum, fret: p.fret))
                    .OrderBy(p => p.str)
                    .ToList();
                int k = top ? 0 : ordered.Count - 1;
                if (k < 0 || ordered[k].str < 1)
                    return (1, 0, 0);
                return (ordered[k].str,
                        TabChordColumns.Offsets(ordered)[k],
                        TabChordColumns.FretWidth(ordered[k].fret) / 2);
            }
            default:
                return (1, 0, 0);
        }
    }

    /// <summary>
    /// Where ONE note's (not a chord's) fret digit is drawn — the same three answers
    /// <see cref="ChordNoteDigitColumn"/> gives, for an item that has no zigzag to be in.
    /// </summary>
    public (int StringNum, double Dx, double HalfWidth) NoteDigitColumn(
        int writtenMidi, int? preferredString)
    {
        var (stringNum, fret) = Fret(writtenMidi, preferredString);
        return (stringNum, 0, TabChordColumns.FretWidth(fret) / 2);
    }

    /// <summary>
    /// The staff position LilyPond gives a fret digit on this string — the quantity its own
    /// tie column is ordered and directed by, and the tab's answer to "how high is this".
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/translation-functions.scm:971-982 tablature-position-on-lines —
    ///   <c>(- (* 2 string-nr) string-count 1)</c>, negated because <c>stringOneTopmost</c>
    ///   defaults to true (scm/define-context-properties.scm), which is also Lily#'s
    ///   convention (string 1 is <see cref="StaffY"/>, the top line).
    /// LILYPOND-REF: lily/tab-note-heads-engraver.cc:99-122 Tab_note_heads_engraver::process_music
    ///   — the engraver calls that through <c>tabStaffLineLayoutFunction</c> and writes the
    ///   answer to the TabNoteHead's <c>staff-position</c>.
    /// <para>
    /// MEASURED on 2.26.0 with the test/tab-chord-tie twin: <c>&lt;c' e' g'&gt;</c> on a
    /// six-string guitar reports TabNoteHead staff-positions 5, 3, 1 for strings 1, 2, 3.
    /// </para>
    /// </remarks>
    public int StaffPositionOfString(int stringNum) => StringCount + 1 - 2 * stringNum;

    /// <summary>
    /// The string a stem meets on this item: the TOP digit (smallest string number)
    /// for an up-stem, the BOTTOM for a down-stem. Chords use the same exclusive
    /// allocation the drawn chord does. Mirrors <c>TabStemHeadY</c> in the renderer.
    /// </summary>
    public int StemHeadString(MusicItem item, bool stemUp)
    {
        switch (item)
        {
            case NoteItem n:
                return Fret(n.Midi, n.StringNumber).stringNum;
            case ChordItem c when c.Notes.Length > 0:
                int shift = _octaveShift;
                var alloc = Tunings.CalculateChordFrets(
                    c.Notes.Select(x => (x.Midi + shift, x.StringNumber)).ToList(), _tuning);
                int head = alloc[0].stringNum;
                foreach (var a in alloc)
                    head = stemUp ? System.Math.Min(head, a.stringNum) : System.Math.Max(head, a.stringNum);
                return head;
            default:
                return 1;
        }
    }

    /// <summary>Half the string span in the tab's OWN spaces — LilyPond's
    /// <c>Staff_symbol_referencer::staff_radius</c> for this staff symbol.</summary>
    public double StaffRadius => (StringCount - 1) / 2.0;

    /// <summary>Device-Y of the tab's middle — staff position 0, the refpoint every LilyPond
    /// position on this staff is measured from (a gap between two strings when the count is
    /// even).</summary>
    public double MiddleY => StaffY + (StringCount - 1) * StringSpace / 2.0;

    /// <summary>
    /// Device-Y of an UNBEAMED tab stem's TIP — or null when the item carries no stem at
    /// all, a whole note or a breve, which is the same gate the renderer takes before drawing
    /// one.
    /// </summary>
    /// <remarks>
    /// The ordinary stem rule run in the tab's frame: <c>\tabFullNotation</c> reverts the
    /// TabStaff's stem overrides, so a tab stem is bought with the ordinary
    /// <c>details.lengths</c> by duration, the ordinary unnatural-direction shortening, and the
    /// ordinary pull to the middle line — everything in half-spaces of THIS staff, whose space
    /// is the string gap. <see cref="StemCalculator.CalculateStemEndPosition"/> is that rule;
    /// this converts its answer to device Y.
    /// LILYPOND-REF: ly/property-init.ly:828-832 tabFullNotation — the reverts of
    ///   Stem.length, no-stem-extend, details and stencil.
    /// LILYPOND-REF: lily/stem.cc:481-596 internal_calc_stem_end_position — the rule itself;
    ///   :505 staff_rad (<see cref="StaffRadius"/>), :588 <c>hp[dir] + dir * length</c>.
    /// <para>
    /// MEASURED on 2.26.0 (scratch/p337/sugar3, the owner's Sugar.ly, five-string bass):
    /// an eighth on the E string (position −2) stems UP to 3.750000 above the middle
    /// (= (−2 + 7) × 0.75, no shortening), an eighth on the A string (0) stems DOWN to
    /// −5.062500 (= −(7 − 0.25) × 0.75, the middle-line head is shortened by one step). Both
    /// fall out of the rule to the digit. Before 2026-09-05 this was a flat
    /// <c>clearance + 3.0 × string space</c> from the digit — 5.55 ss from the string, which
    /// put an A-string up-stem 2.6 ss above the top string where LilyPond's reaches 0.75, and
    /// made every full-notation tab system 1.45 ss taller than LilyPond's (HANDOFF §1 第337).
    /// </para>
    /// <para>
    /// ⚠️ <paramref name="headString"/> IS A PARAMETER RATHER THAN A LOOKUP ON PURPOSE.
    /// Resolving the head string again would run <c>Tunings.CalculateChordFrets</c> a second
    /// time for every chord that carries a script — the caller has just done it via
    /// <see cref="StemHeadString"/> — and it would also let the two answers drift.
    /// </para>
    /// </remarks>
    public double? UnbeamedStemTipY(MusicItem item, bool stemUp, int headString)
    {
        if (NoteColumnLayout.Of(item) is not { HasStem: true })
            return null;
        int durationLog = StemCalculator.GetDurationLog(GlyphMetrics.NoteValueOf(item));
        double endPosition = StemCalculator.CalculateStemEndPosition(
            stemUp, durationLog, StaffPositionOfString(headString), null, StaffRadius);
        // Positions are half-spaces of THIS staff above its middle; device Y grows downward.
        return MiddleY - endPosition * StringSpace / 2.0;
    }

    /// <summary>The staff positions of an item's OUTERMOST fret digits — LilyPond's
    /// <c>head_positions</c> on a TabVoice stem, (lowest, highest). A chord's strings come
    /// from the same exclusive allocation the drawn chord uses.</summary>
    private (int Low, int High) HeadPositions(MusicItem item)
    {
        switch (item)
        {
            case NoteItem n:
            {
                int p = StaffPositionOfString(Fret(n.Midi, n.StringNumber).stringNum);
                return (p, p);
            }
            case ChordItem c when c.Notes.Length > 0:
            {
                int shift = _octaveShift;
                var alloc = Tunings.CalculateChordFrets(
                    c.Notes.Select(x => (x.Midi + shift, x.StringNumber)).ToList(), _tuning);
                int lo = int.MaxValue, hi = int.MinValue;
                foreach (var a in alloc)
                {
                    int p = StaffPositionOfString(a.stringNum);
                    lo = System.Math.Min(lo, p);
                    hi = System.Math.Max(hi, p);
                }
                return (lo, hi);
            }
            default:
                return (0, 0);
        }
    }

    private static bool? ForcedStemUpOf(MusicItem item) => item switch
    {
        NoteItem n => n.ForcedStemUp,
        ChordItem c => c.ForcedStemUp,
        _ => null,
    };

    /// <summary>
    /// The direction of an UNBEAMED tab stem: LilyPond's default-direction rule run on the
    /// fret digits' staff positions — the head FARTHER from the middle decides, a tie (one
    /// digit ON the middle string, or a chord symmetric about it) falls to the neutral
    /// direction, DOWN — unless the writer turned the stem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:793-809 Stem::calc_default_direction —
    ///   <c>udistance = hp[UP]</c>, <c>ddistance = -hp[DOWN]</c>,
    ///   <c>dir = sign (ddistance - udistance)</c>.
    /// LILYPOND-REF: lily/stem.cc:769-788 Stem::calc_direction — a CENTER default falls to
    ///   <c>neutral-direction</c>, which is DOWN (scm/define-grobs.scm Stem).
    /// LILYPOND-REF: lily/tab-note-heads-engraver.cc:99-122 — the digit's staff-position is
    ///   its string's (<see cref="StaffPositionOfString"/>), so on a TabVoice the ordinary rule
    ///   reads strings, not sounding pitch: a bass run on the bottom strings stems UP.
    /// <para>
    /// MEASURED on 2.26.0 (scratch/p337/sugar3, five-string bass): 44 lone eighths on the E
    /// string (−2) UP, 72 on the A string (0, the middle) DOWN, 8 on the B string (−4) UP.
    /// For a SINGLE digit this is the "lower half of the fretboard stems up" rule Lily# had
    /// since 2026-07 (string &gt; (count+1)/2); what changed is the chord — the farther
    /// extreme rather than the mean string — and that the rule is now the cited one.
    /// </para>
    /// </remarks>
    public bool TabStemUp(MusicItem item)
    {
        if (ForcedStemUpOf(item) is { } forced)
            return forced;
        var (lo, hi) = HeadPositions(item);
        int udistance = hi;
        int ddistance = -lo;
        return ddistance - udistance > 0;   // sign > 0 → UP; 0 → neutral DOWN; < 0 → DOWN
    }

    /// <summary>
    /// The direction of a whole tab BEAM: LilyPond's beam default-direction rule run on the
    /// members' fret-digit positions — the farthest extreme decides, then a per-stem vote,
    /// then the sides' reach, then the neutral direction (DOWN).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:876-940 Beam::get_default_dir — :880-889 the extremes,
    ///   :894-916 the per-stem tally (a forced stem votes its own way and raises force_dir),
    ///   :918-924 the extremes check (skipped when any stem is forced), :928-937 the vote,
    ///   the averages, the totals, then <c>neutral-direction</c>.
    /// LILYPOND-REF: lily/beam.cc:182-246 Beam::calc_direction — the group's answer is set on
    ///   every unforced stem (:243 set_stem_directions), which is why a tab beam is never kneed
    ///   here: the members take one side.
    /// <para>
    /// The same rule <c>BeamDetector.DefaultBeamStemUp</c> runs for a notation beam on
    /// notated pitch, spelled here over strings because that is what a TabVoice stem's
    /// heads are. MEASURED on 2.26.0 (Sugar.ly): the pair E string (−2) + D string (+2) ties
    /// at every step and beams DOWN, the neutral direction; 36 such pairs, all DOWN. Before
    /// 2026-09-05 this was the MEAN string of the group, which agrees on that pair and
    /// disagrees wherever an outlier outweighs the majority (the notation side found the
    /// same defect in its mean rule, BeamDetector.cs).
    /// </para>
    /// </remarks>
    public bool GroupStemUp(System.Collections.Generic.IEnumerable<MusicItem> items)
    {
        int extremeUp = 0, extremeDown = 0;
        bool forceDir = false;
        int upVotes = 0, downVotes = 0, totalUp = 0, totalDown = 0;
        int count = 0;
        foreach (var item in items)
        {
            if (item is not (NoteItem or ChordItem))
                continue;
            count++;
            var (lo, hi) = HeadPositions(item);
            // LILYPOND-REF: beam.cc:883-888 — extremes[d] over head_positions, on each side.
            if (hi > 0) extremeUp = System.Math.Max(extremeUp, hi);
            if (lo < 0) extremeDown = System.Math.Min(extremeDown, lo);

            // LILYPOND-REF: beam.cc:897-915 — a stem with a set direction votes it (force_dir);
            //   otherwise its default-direction, falling to neutral-direction (DOWN) when
            //   that is CENTER; total[dir] += max (-dir * hp[-dir], 0).
            bool? forced = ForcedStemUpOf(item);
            if (forced is not null) forceDir = true;
            bool voteUp = forced ?? (-lo - hi > 0);
            if (voteUp)
            {
                upVotes++;
                totalUp += System.Math.Max(-lo, 0);
            }
            else
            {
                downVotes++;
                totalDown += System.Math.Max(hi, 0);
            }
        }
        if (count == 0)
            return false;

        // LILYPOND-REF: beam.cc:918-924 — the farther extreme wins, unless a stem is forced.
        if (!forceDir)
        {
            if (System.Math.Abs(extremeUp) > -extremeDown) return false;
            if (extremeUp < -extremeDown) return true;
        }
        // LILYPOND-REF: beam.cc:928-937 — the vote, then the sides' average reach (INTEGER
        //   division: Drul_array<int>), then the totals, then neutral-direction = DOWN.
        if (upVotes != downVotes) return upVotes > downVotes;
        if (upVotes > 0 && downVotes > 0)
        {
            int avgDiff = totalUp / upVotes - totalDown / downVotes;
            if (avgDiff != 0) return avgDiff > 0;
        }
        if (totalUp != totalDown) return totalUp > totalDown;
        return false;
    }
}
