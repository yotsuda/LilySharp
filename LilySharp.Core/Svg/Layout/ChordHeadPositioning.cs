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

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Within-chord notehead X displacement for seconds and unisons: walking the
/// heads along the stem direction, every other head of a cluster closer than
/// a second is "reversed" to the far side of the stem.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stem.cc:606-760 Stem::calc_positioning_done —
/// reversed heads shift by <c>ell − thickness·reverse_overlap</c> in the stem
/// direction (UP: right of the stem, DOWN: left), where <c>ell</c> is the
/// head's right ink extent. reverse_overlap is 0.5 normally (reversed heads
/// overlap the stem by 50%), 1.1 for the first pair when it is a unison
/// (head slant would otherwise leave a gap, LP issue 346), and 2 for
/// stemless semibreve chords (positioned nearer to read as one chord).
/// </remarks>
public static class ChordHeadPositioning
{
    /// <summary>
    /// Per-note X offsets (staff spaces, relative to the chord column) in the
    /// same order as <paramref name="notes"/>. All zero when the chord has no
    /// seconds/unisons.
    /// </summary>
    /// <param name="notes">The chord's notes (any order).</param>
    /// <param name="stemUp">Stem direction (drives the shift side).</param>
    /// <param name="noteValue">1=whole, 2=half, 4=quarter, … (head glyph and
    /// the stemless-semibreve rule).</param>
    /// <param name="headFont">The font the HEADS read — the design their <c>font-size</c>
    /// selects, already magnified (<see cref="GlyphMetrics.AtFontSize"/>). Null is the plain
    /// 20, which is what a head with no font-size reads.</param>
    /// <remarks>
    /// ⚠️ A FONT, NOT A SCALE, and the difference is MEASURED rather than argued. Emmentaler is
    /// optically sized, so a reduced head is a DIFFERENT design's outline and not the 20's
    /// multiplied down, and the reversal shift follows whichever box is opened. Canonical
    /// LilyPond 2.26.0, three regimes in one dump (scratch/p316/sec-dump.ly, each number the
    /// second head's left minus the first's):
    /// <list type="bullet">
    /// <item><c>&lt;c' d'&gt;4</c> — 1.239200 = 1.304200 − 0.065, the twenty's box.</item>
    /// <item><c>\grace { &lt;c' d'&gt;16 }</c> — 0.852939 = 1.298161·magstep(−3) − 0.065, the
    /// FOURTEEN's. Reading the twenty scaled would say 0.857209, off by 0.004270.</item>
    /// <item><c>\new CueVoice { &lt;c' d'&gt;4 }</c> — 0.750349 = 1.294282·magstep(−4) − 0.065,
    /// the THIRTEEN's. The twenty scaled would say 0.756597, off by 0.006248.</item>
    /// </list>
    /// Both reduced answers were Lily#'s wrong ones until session 316; the control with no
    /// second (<c>\grace { &lt;c' e'&gt;16 }</c>, both heads at 16.120827) is zero either way,
    /// which is why no book without a second could have caught it.
    /// </remarks>
    // Internal because a FONT is: MusicFontDesign is the internal table, the same
    // reason AccidentalPlacement.CalculatePositions takes one internally.
    internal static double[] CalculateOffsets(
        IReadOnlyList<ChordNoteInfo> notes, bool stemUp, int noteValue,
        MusicFontDesign? headFont = null)
    {
        // LILYPOND-REF: stem.cc:684 — ell = head right ink extent.
        bool mixed = HasMixedHeads(notes);
        var offsets = OffsetsForEll(notes, stemUp, noteValue,
            GlyphMetrics.GetNoteheadBBox(headFont ?? MusicFont.Current.DesignAt(0), noteValue).Right,
            mixed ? headFont ?? GlyphMetrics.StaffHeadFont : null);
        if (mixed)
        {
            double support = LayoutUtilities.StemAttachX(stemUp, noteValue, SupportStyle(notes, stemUp, noteValue, headFont), headFont);
            var supportStyle = SupportStyle(notes, stemUp, noteValue, headFont);
            var font = headFont ?? GlyphMetrics.StaffHeadFont;
            for (int i = 0; i < notes.Count; i++)
            {
                // A harmonic head of a STEMLESS chord (a whole note's, a breve's) is centred on
                // the ordinary heads instead of standing at the stem's side: the touched note of
                // `<c' f'@harmonic>1` (owner-approved 2026-11-13, grammar evaluation 3).
                // LILYPOND-REF: lily/stem.cc:643-653 calc_positioning_done — `i_am_invisible` and
                //   style harmonic: sup_extent.center () − h_extent.center ().
                // MEASURED, 2.26.0 (Lab sessions/p1226/md/eval3/featH/lp3.ly): on `<c' f'\harmonic>1`
                //   the whole head spans 29.415 … 31.377 and the diamond 29.746 … 31.046, 0.331 in.
                if (noteValue <= 1 && notes[i].Notehead == NoteheadStyle.Harmonic && supportStyle != NoteheadStyle.Harmonic)
                {
                    var own = GlyphMetrics.GetNoteheadBBox(font, NoteheadStyle.Harmonic, noteValue);
                    var sup = GlyphMetrics.GetNoteheadBBox(font, supportStyle, noteValue);
                    offsets[i] += (sup.Left + sup.Right) / 2 - (own.Left + own.Right) / 2;
                    continue;
                }
                offsets[i] += support - LayoutUtilities.StemAttachX(stemUp, noteValue, notes[i].Notehead, headFont);
            }
        }
        return offsets;
    }

    /// <summary>Whether a chord's members draw different head shapes, as a drum chord's may.</summary>
    internal static bool HasMixedHeads(IReadOnlyList<ChordNoteInfo> notes)
    {
        for (int i = 1; i < notes.Count; i++)
            if (notes[i].Notehead != notes[0].Notehead)
                return true;
        return false;
    }

    /// <summary>
    /// The shape of the head a mixed chord's stem and other heads align to: the one with the most
    /// of itself inside the stem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:181-204 Stem::support_head — "the head with the widest part
    ///   inside the stem"; :627-664 calc_positioning_done — every other head moves by the support
    ///   head's attachment less its own, so all of them attach to one stem.
    /// MEASURED, 2.26.0 (Lab sessions/p1105/probe/chord.ps1): `&lt;cb sn&gt;4`, up, stands the triangle
    ///   0.22 left of the plain head; `&lt;bd tomfl&gt;4` the seconds' 1.24 either side.
    /// </remarks>
    private static NoteheadStyle SupportStyle(IReadOnlyList<ChordNoteInfo> notes, bool stemUp, int noteValue, MusicFontDesign? headFont)
    {
        var font = headFont ?? GlyphMetrics.StaffHeadFont;
        var best = notes[0].Notehead;
        double widest = double.NegativeInfinity;
        foreach (var note in notes)
        {
            var box = GlyphMetrics.GetNoteheadBBox(font, note.Notehead, noteValue);
            double attach = LayoutUtilities.StemAttachX(stemUp, noteValue, note.Notehead, headFont);
            double inside = Math.Abs((stemUp ? box.Left : box.Right) - attach);
            if (inside > widest)
                (widest, best) = (inside, note.Notehead);
        }
        return best;
    }

    /// <summary>The head shape a chord's stem attaches to: its own, or a mixed chord's support head's.</summary>
    internal static NoteheadStyle StemStyle(ChordItem chord, bool stemUp, int noteValue, MusicFontDesign? headFont = null)
        => HasMixedHeads(chord.Notes)
            ? SupportStyle(chord.Notes, stemUp, noteValue, headFont)
            : chord.Notehead;

    /// <summary>The right edge of a chord's rightmost head in its own frame: what a dot column is
    /// floored on (lily/dot-column.cc:82-84 calc_positioning_done).</summary>
    internal static double InkRight(IReadOnlyList<ChordNoteInfo> notes, double[] offsets, int noteValue, MusicFontDesign? headFont)
    {
        var font = headFont ?? GlyphMetrics.StaffHeadFont;
        double max = 0;
        if (HasMixedHeads(notes))
        {
            max = double.NegativeInfinity;
            for (int i = 0; i < notes.Count; i++)
                max = Math.Max(max, offsets[i] + GlyphMetrics.GetNoteheadBBox(font, notes[i].Notehead, noteValue).Right);
            return max;
        }
        return GlyphMetrics.GetNoteheadBBox(font, noteValue).Right + Math.Max(0, offsets.Max());
    }

    /// <summary>The right edge of a mixed chord's first head, the one at the stem's far end, in
    /// the chord's frame: what the next column's ideal is measured from.</summary>
    /// <remarks>LILYPOND-REF: lily/note-spacing.cc:46-77 Note_spacing::get_spacing — first_head's
    /// extent; lily/stem.cc:214-221 Stem::first_head.</remarks>
    internal static double FirstHeadRight(ChordItem chord, int noteValue, MusicFontDesign? headFont)
    {
        var offsets = CalculateOffsets(chord.Notes, chord.StemUp, noteValue, headFont);
        // On one row the head the walk leaves where it is, as measured: `&lt;cb cymc&gt;4`, down, steps
        // 3.00 from the circled cross, the later written; up, 3.08 from the triangle, the earlier.
        int first = 0;
        for (int i = 1; i < chord.Notes.Length; i++)
            if (chord.StemUp ? chord.Notes[i].StaffPosition < chord.Notes[first].StaffPosition
                             : chord.Notes[i].StaffPosition >= chord.Notes[first].StaffPosition)
                first = i;
        return offsets[first] + GlyphMetrics.GetNoteheadBBox(headFont ?? GlyphMetrics.StaffHeadFont, chord.Notes[first].Notehead, noteValue).Right;
    }

    /// <summary>
    /// Sorts the head indices by staff position in the stem direction.
    /// </summary>
    /// <remarks>
    /// Its own method because a lambda capturing a parameter builds its environment at the
    /// ENTRY of the method that holds it: in <see cref="OffsetsForEll"/> every single-note
    /// call paid it before the <c>Count &lt; 2</c> return (session 470's allocation-tick price).
    /// </remarks>
    private static void SortInStemDirection(int[] order, IReadOnlyList<ChordNoteInfo> notes, int dir)
    {
        // Keyed, not compared (session 535): the comparison's delegate and environment were
        // still built on every chord. The key is dir × position, the very number the
        // comparison compared; same introspective sort on the same keys, so the order is
        // unchanged (see Tunings.SortHighestFirst).
        int n = order.Length;
        Span<int> keys = n <= 16 ? stackalloc int[16] : new int[n];
        keys = keys[..n];
        for (int i = 0; i < n; i++)
            keys[i] = dir * notes[order[i]].StaffPosition;
        keys.Sort(order.AsSpan());
    }

    /// <summary>
    /// One pitch twice in a chord with a note a second from it: drawn as one head (owner's decision
    /// 2026-11-03, LILYSHARP-OWN). The parity walk only sees neighbours, so <c>&lt;a b b&gt;</c> goes
    /// left, right, left and lays the second b on the a, in LilyPond 2.26.0 too (checked by the
    /// twin). A unison with no second stays LilyPond's two heads, one a player can mean. B and
    /// B flat on one line are two notes, and are not this. Whatever reads the offsets, ties and
    /// dots and spacing, sees the two heads on one spot.
    /// </summary>
    private static bool IsOneHeadBesideASecond(IReadOnlyList<ChordNoteInfo> notes, int a, int b)
    {
        var (x, y) = (notes[a], notes[b]);
        // By the pitch and not the accidental, which only the first of them prints. Heads asked
        // about by position alone carry no pitch (NoteCollision's), and then the accidental is all
        // there is to tell.
        if (x.StaffPosition != y.StaffPosition || x.Midi != y.Midi
            || (x.Midi == 0 && x.Accidental != y.Accidental))
            return false;
        for (int i = 0; i < notes.Count; i++)
            if (Math.Abs(notes[i].StaffPosition - x.StaffPosition) == 1)
                return true;
        return false;
    }

    /// <summary>The rule itself, over the head right extent its caller resolved.</summary>
    /// <param name="mixedFont">Set for a chord of mixed heads: each reversed head moves by its own
    /// right edge (stem.cc:684, <c>heads[i]</c>).</param>
    private static double[] OffsetsForEll(
        IReadOnlyList<ChordNoteInfo> notes, bool stemUp, int noteValue, double ell, MusicFontDesign? mixedFont = null)
    {
        var offsets = new double[notes.Count];
        if (notes.Count < 2)
            return offsets;

        double thick = EngravingDefaults.StemThickness;
        int dir = stemUp ? 1 : -1;

        // LILYPOND-REF: stem.cc:619-623 — sort by position, reversed for DOWN,
        // so the walk always runs in the stem direction from the support head.
        var order = new int[notes.Count];
        for (int i = 0; i < notes.Count; i++) order[i] = i;
        SortInStemDirection(order, notes, dir);
        // Two mixed heads on one row keep the written order, turned over for a down stem (stem.cc:
        // 619-623 reverses the sorted heads): which one the walk reverses shows.
        // MEASURED, 2.26.0 (Lab sessions/p1105/probe/chord.ps1): `&lt;cymc cymr&gt;4`, down, moves
        // the circled cross, 1.41.
        if (mixedFont is not null)
            for (int i = 1; i < order.Length; i++)
                for (int j = i; j > 0 && notes[order[j - 1]].StaffPosition == notes[order[j]].StaffPosition
                        && dir * order[j - 1] > dir * order[j]; j--)
                    (order[j - 1], order[j]) = (order[j], order[j - 1]);

        // LILYPOND-REF: stem.cc:667-760 — parity walk over adjacent intervals.
        bool parity = true;
        double lastPos = notes[order[0]].StaffPosition;
        const int threshold = 1; // note-collision-threshold (staff positions)
        for (int k = 1; k < order.Length; k++)
        {
            if (IsOneHeadBesideASecond(notes, order[k - 1], order[k]))
            {
                offsets[order[k]] = offsets[order[k - 1]];
                continue;
            }
            double p = notes[order[k]].StaffPosition;
            double dy = Math.Abs(lastPos - p);

            if (dy < 0.1 + threshold)
            {
                if (parity)
                {
                    // LILYPOND-REF: stem.cc:697-740 reverse_overlap selection.
                    double reverseOverlap = 0.5;
                    if (k == 1 && dy < 0.1)
                        reverseOverlap = 1.1;
                    if (noteValue == 1)
                        reverseOverlap = 2; // stemless semibreve chord

                    double headEll = mixedFont is null
                        ? ell
                        : GlyphMetrics.GetNoteheadBBox(mixedFont, notes[order[k]].Notehead, noteValue).Right;
                    offsets[order[k]] = (headEll - thick * reverseOverlap) * dir;
                }
                parity = !parity;
            }
            else
            {
                parity = true;
            }
            lastPos = p;
        }

        return offsets;
    }
}
