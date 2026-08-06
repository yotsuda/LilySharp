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

    /// <summary>Grace fret digits relative to the main fret size (just slightly smaller).</summary>
    public const double GraceFretScale = 0.8;

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
    /// ⚠️ A dead note's <c>×</c> is a DIFFERENT shape (ink 0.436 of the size, centred lower),
    /// which is why the renderer asks the face per glyph for the BASELINE
    /// (<see cref="FretBaselineDrop"/>) instead of sharing one number. This height stays a
    /// digit's, because it is what the layout reserves and every reader of it is asking
    /// "how tall is a fret number".
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
    /// How far a tab stem's NEAR end sits from the string line its digit is centred on —
    /// midway between the digit's ink edge and the neighbouring string line.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, and it has to be: LilyPond's TabStaff draws no stems at all by default,
    /// so there is no quantity to port. The stem may only begin in the window between the
    /// digit it leaves (<see cref="FretDigitHeight"/> / 2) and the next string line
    /// (<paramref name="stringSpace"/>), and this centres it in that window.
    /// <para>
    /// ⚠️ IT IS A WINDOW, NOT A PADDING, and that is the whole point. This used to be
    /// <c>FretDigitHeight / 2 + 0.3</c> — a fixed gap past the digit — which walks toward the
    /// next line as the font grows because the digit grows and the line does not. MEASURED on
    /// test/tab-with-chords at font 3.3: stems ran 13.78..18.28 against string lines at 13.85
    /// and 18.35, i.e. both ends stopping 0.07 short of a line and reading as flush with it.
    /// Centring instead keeps the clearance proportional at any size.
    /// </para>
    /// <para>
    /// ★ At the size the old constant was tuned for (2.6) the two agree to 0.003 —
    /// <c>1.197</c> against <c>1.194</c> — so this is a re-derivation of the same intent, not
    /// a new number. What changes is that it no longer decays as the digits grow.
    /// </para>
    /// </remarks>
    public static double StemClearance(double stringSpace)
        => (FretDigitHeight / 2 + stringSpace) / 2;

    /// <summary>
    /// How far an UNBEAMED tab stem runs from its near end to its tip.
    /// </summary>
    /// <remarks>
    /// <c>\tabFullNotation</c> reverts the TabStaff's stem overrides, so a tab stem is bought
    /// with the ORDINARY stem lengths — and on a tab the staff space IS the string gap, so
    /// whatever that length is, it scales with it.
    /// LILYPOND-REF: ly/property-init.ly:828-832 — the reverts of Stem.length, no-stem-extend,
    ///   details and stencil that <c>\tabFullNotation</c> performs.
    /// LILYPOND-REF: ly/engraver-init.ly:1250-1258 no-stem-extend — what those reverts undo:
    ///   TabStaff sets <c>details</c> to <c>lengths 0 0 0 0 0 0</c> and the stencil to
    ///   <c>##f</c>, precisely so that no stem is drawn at all.
    /// LILYPOND-REF: scm/define-grobs.scm Stem details lengths.
    /// <para>
    /// ⚠️ 3.0 IS NOT THE CITED NUMBER, AND THE CITED LINE IS NOT A LENGTH. What
    /// <c>details.lengths</c> holds is <c>(3.5 3.5 3.5 4.25 5.0 …)</c>, and LilyPond does not
    /// use it directly: <c>length</c> is the callback <c>ly:stem::calc-length</c>, which picks
    /// the entry by duration-log and then applies the shortening, the middle-line pull and the
    /// minimum — the machinery <see cref="StemCalculator"/> already ports for NOTATION stems.
    /// So this is case ⒝ of HANDOFF §7.6: derived from LilyPond, not copied from it. What it
    /// would take to make it literal is to run a tab column through
    /// <see cref="StemCalculator"/> in the string-gap frame instead of writing one flat
    /// number here. ⚠️ DO NOT SWAP THE NUMBER ON ITS OWN: 3.0 → 3.5 moves every tab stem and
    /// every script now clearing one, and NO ledger point observes a tab stem (a tab book is
    /// not comparable to LilyPond until its strings are pinned). Open the book first.
    /// ⚠️ The wording this replaced said "the default 3 staff spaces measured FROM THE NOTE
    /// HEAD" beside a citation that reads 3.5, which is the shape §5.2 warns about — a
    /// LILYPOND-REF whose neighbouring formula is a different number.
    /// </para>
    /// <para>
    /// ⚠️ IT LIVES HERE BECAUSE TWO LAYERS NEED IT, and for three sessions only one had it.
    /// The renderer drew the stem; <see cref="ArticulationEngraver"/> has to place scripts
    /// clear of it, and its non-beamed branch simply had no stem term — while the BEAMED
    /// branch beside it had always cleared the beam's outer edge. MEASURED on
    /// test/tab-articulations-multistaff before the fix: the stems ran up to 17.960000 (2.85
    /// past the top string line) and both the flageolet and the fermata were pinned at
    /// 19.810000 — one number for two glyphs of different heights, which is the signature of
    /// a clamp rather than a placement, and the fermata's right arm crossed the stem.
    /// </para>
    /// </remarks>
    public static double UnbeamedStemLength(double stringSpace) => 3.0 * stringSpace;

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
/// the staff's space, not in the positions. An earlier attempt at this route (<c>26e553d9</c>)
/// spelled the strings three half-spaces apart and left the notation staff's thicknesses in
/// place — a stretched notation staff rather than a tab one — and was replaced by hand-fitted
/// arithmetic (<c>88f98480</c>) whose flat groups sat 0.297 past LilyPond's.
/// <para>
/// ⚠️ Directions come from the STRINGS (<paramref name="stemUp"/>), not the notated pitch, so
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
    {
        int shift = _octaveShift;
        var alloc = Tunings.CalculateChordFrets(
            chord.Notes.Select(n => (n.Midi + shift, n.StringNumber)).ToList(), _tuning);
        for (int i = 0; i < chord.Notes.Length; i++)
            if (chord.Notes[i].StaffPosition == staffPosition && alloc[i].stringNum >= 1)
                return StringY(alloc[i].stringNum);
        return StaffY;
    }

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

    /// <summary>
    /// Device-Y of an UNBEAMED tab stem's TIP, measured from the digit line the stem leaves
    /// (<paramref name="headY"/>, which the caller already has from
    /// <see cref="StemHeadString"/>) — or null when the item carries no stem at all, a whole
    /// note or a breve, which is the same gate the renderer takes before drawing one.
    /// </summary>
    /// <remarks>
    /// Composed from the parts that already exist rather than re-derived:
    /// <see cref="TabConstants.StemClearance"/> is the gap the stem starts after and
    /// <see cref="TabConstants.UnbeamedStemLength"/> is how far it runs — that last one moved
    /// out of the renderer so this could read it rather than spell it a second time.
    /// <para>
    /// ⚠️ <paramref name="headY"/> IS A PARAMETER RATHER THAN A LOOKUP ON PURPOSE. Resolving
    /// the head string again would run <c>Tunings.CalculateChordFrets</c> a second time for
    /// every chord that carries a script — the caller has just done it — and it would also
    /// let the two answers drift. Passing it keeps one resolution per script and makes the
    /// clearance provably the same line the caller measured its digit from.
    /// </para>
    /// </remarks>
    public double? UnbeamedStemTipY(MusicItem item, bool stemUp, double headY)
    {
        if (NoteColumnLayout.Of(item) is not { HasStem: true })
            return null;
        double reach = TabConstants.StemClearance(StringSpace)
                     + TabConstants.UnbeamedStemLength(StringSpace);
        return stemUp ? headY - reach : headY + reach;
    }

    /// <summary>The mean tab-head string of a note/chord (a chord averages its notes),
    /// for the string-based stem-direction decision.</summary>
    public double MeanString(MusicItem item)
    {
        switch (item)
        {
            case NoteItem n:
                return Fret(n.Midi, n.StringNumber).stringNum;
            case ChordItem c when c.Notes.Length > 0:
                double sum = 0;
                foreach (var x in c.Notes) sum += Fret(x.Midi, x.StringNumber).stringNum;
                return sum / c.Notes.Length;
            default:
                return 1.0;
        }
    }

    /// <summary>
    /// The tab stem direction for a mean string position: UP for the LOWER half of the
    /// fretboard (a low note, like a low notated pitch), DOWN for the upper. LilyPond
    /// decides a tab stem from the STRING (the tab head), not the notated pitch — so a
    /// bass run on the bottom strings points its stems up, not down.
    /// </summary>
    /// <remarks>LILYPOND-REF: the TabStaff's stems follow the tab note-column positions
    /// (Stem::calc_direction over the fret heads), not the sounding pitch.</remarks>
    public bool StringStemUp(double meanString) => meanString > (StringCount + 1) / 2.0;

    /// <summary>The string-based stem direction for a whole beam group (its members'
    /// mean tab-head string).</summary>
    public bool GroupStemUp(System.Collections.Generic.IEnumerable<MusicItem> items)
    {
        double sum = 0;
        int count = 0;
        foreach (var it in items) { sum += MeanString(it); count++; }
        return StringStemUp(count > 0 ? sum / count : 1.0);
    }
}
