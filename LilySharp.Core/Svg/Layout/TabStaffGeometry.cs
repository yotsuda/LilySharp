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
    /// enough to be hard to read. A single digit here is 0.625 × 2.9 = 1.8125 wide and
    /// 0.6875 × 2.9 = 1.99375 tall, against the 0.990155 LilyPond's TabNoteHead measures
    /// (audit/lp-geometry/probes/line-start-mindist.ly, score TKC) — about 2.0×. The
    /// collisions that follow from bigger digits are solved rather than avoided: chords
    /// stagger their digits into two columns (the zigzag), and the columns reserve that
    /// real extent (SpacingRules.ApplyTabChordSpacing).
    /// <para>
    /// Raised 2.6 → 2.9 on request, together with dropping the opaque background the digits
    /// used to sit on: with the string line now running THROUGH the digit rather than being
    /// painted out behind it, the digit has to carry the contrast on its own.
    /// </para>
    /// <para>
    /// ⚠️ This is a RATIFIED deviation (docs/HANDOFF.md §3), not an un-ported LilyPond
    /// quantity. Do not "fix" it toward LilyPond. The tab STRING SPACING is a separate
    /// question and does follow LilyPond — see <c>EngravingDefaults.TabStringSpace</c>.
    /// </para>
    /// </remarks>
    public const double FretFontSize = 3.3;

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
/// Quants a TAB beam through LilyPond's ported beam quanter
/// (<see cref="BeamScoringProblem"/>) by feeding the notes' STRING lines as the
/// stem positions — so tab beams get the same least-squares → damping →
/// concaveness → quanting treatment as notation beams (a chord/outlier flattens,
/// a monotonic run slopes), instead of an ad-hoc slope. Returns the beam line in
/// DEVICE Y (evaluate with <see cref="TabBeamMath.At"/>).
/// </summary>
internal static class TabBeamQuant
{
    public static (double Slope, double InterceptY, double FirstX) Compute(
        BeamGroup group, double[] memberStemXs, TabStaffGeometry geom, bool stemUp)
    {
        int n = group.Members.Length;
        double leftX = memberStemXs[0], rightX = memberStemXs[n - 1];
        double span = rightX - leftX;

        // Slope follows the STRING CONTOUR (first→last string), NOT the notation pitch:
        // the ported notation quanter tilts a run by pitch, so a same-string group of
        // different frets (all on one line) came out sloped and its stems inverted. A
        // tab beam is parallel to the strings; same string ⇒ flat.
        double firstStr = geom.StringY(geom.StemHeadString(group.Members[0].Item, stemUp));
        double lastStr = geom.StringY(geom.StemHeadString(group.Members[n - 1].Item, stemUp));
        double rawSlope = span > 0.001 ? (lastStr - firstStr) / span : 0.0;
        // Damp the tilt exactly as LilyPond does — a raw string jump makes far too
        // steep a beam. LILYPOND-REF: beam-quanting.cc:766
        //   slope = 0.6 * tanh(slope) / (damping + concaveness),
        // with the default damping = 1 and concaveness = 0 for a monotonic run.
        double slope = 0.6 * System.Math.Tanh(rawSlope);

        // Anchor the beam a fixed length from the digit HEADS (string line ± the digit
        // clearance), measured along the slope. The FARTHEST member (longest stem) gets
        // stemLen; a floor keeps the CLOSEST member's stem from collapsing to nothing
        // when the run crosses strings — LilyPond's tab stems sit ~2–2.5 string gaps
        // from the head, shorter than a notation stem.
        // LILYPOND-REF: measured from LilyPond's own tab SVG (\tabFullNotation).
        double stemLen = 2.4 * geom.StringSpace;
        double minStem = 1.4 * geom.StringSpace;
        double clearance = TabConstants.StemClearance(geom.StringSpace);
        double farAnchor = 0, nearAnchor = 0;
        for (int i = 0; i < n; i++)
        {
            int str = geom.StemHeadString(group.Members[i].Item, stemUp);
            double headY = geom.StringY(str) + (stemUp ? -clearance : clearance);
            double v = headY - slope * memberStemXs[i]; // beam intercept giving a zero stem here
            if (i == 0) { farAnchor = nearAnchor = v; }
            else if (stemUp)
            {
                farAnchor = System.Math.Max(farAnchor, v);   // lowest head = longest up-stem
                nearAnchor = System.Math.Min(nearAnchor, v); // highest head = shortest
            }
            else
            {
                farAnchor = System.Math.Min(farAnchor, v);   // highest head = longest down-stem
                nearAnchor = System.Math.Max(nearAnchor, v); // lowest head = shortest
            }
        }
        double beamFar = farAnchor + (stemUp ? -stemLen : stemLen);
        double beamFloor = nearAnchor + (stemUp ? -minStem : minStem);
        double intercept = stemUp
            ? System.Math.Min(beamFar, beamFloor)   // above the notes: the higher (smaller Y) wins
            : System.Math.Max(beamFar, beamFloor);  // below the notes: the lower (larger Y) wins
        double leftY = intercept + slope * leftX;
        double rightY = intercept + slope * rightX;

        // Keep the beam from hanging far past the staff. LilyPond's tab stems shorten
        // as the note sits lower on the fretboard so the beam clears the outer string
        // by only ~1.6 gaps, not a full stem below it — otherwise a mid-string run (a
        // 9 on string 3) drew a beam two gaps under the bottom line. Pull the beam back
        // toward the staff, preserving the slope.
        double edge = stemUp ? geom.StringY(1) : geom.StringY(geom.StringCount);
        double maxOverhang = 1.6 * geom.StringSpace;
        if (stemUp)
        {
            double top = System.Math.Min(leftY, rightY);
            double shift = (edge - maxOverhang) - top;
            if (shift > 0) { leftY += shift; rightY += shift; }
        }
        else
        {
            double bot = System.Math.Max(leftY, rightY);
            double shift = bot - (edge + maxOverhang);
            if (shift > 0) { leftY -= shift; rightY -= shift; }
        }
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
