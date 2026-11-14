// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/accidental-placement.cc
//     Copyright (C) 2002--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
//   lily/accidental.cc
//     Copyright (C) 2001--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Information about a positioned accidental within a chord.
/// </summary>
public readonly record struct AccidentalLayout(
    // Staff position of the note.
    int StaffPosition,
    // The accidental type (sharp, flat, natural, etc.).
    string Accidental,
    // X offset from the note column in staff spaces (negative = left of note).
    double XOffset,
    // Whether this is a courtesy (cautionary) accidental.
    bool IsCourtesy = false
);

/// <summary>
/// A stem as the accidental placement sees it: one more box in the heads' reference skyline,
/// in the same frame as the heads (staff spaces, Y up from the middle line, X from the note
/// column). <see cref="Exists"/> is false for a stemless head (a whole note, a breve) and for
/// "no stem given".
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc:348-351 extract_heads_and_stems — "Now that we
///   have all of the heads, grab all of the stems"; :375-385 build_heads_skyline boxes each at
///   its X extent and its pure Y extent. An invisible stem's X extent is empty
///   (lily/stem.cc Stem::width), so a stemless head adds nothing.
/// <para>
/// ⚠️ A DOWN STEM IS NOT INSIDE THE HEAD'S BOX. Its X extent starts at the head's left edge,
/// but it reaches below the head, and the skyline distance is taken over Y: a natural's
/// lower-right stroke, which the head's own box does not meet, meets the stem. MEASURED
/// (LilyPond 2.26.0, Lab sessions/p582/stemy.ly): a natural on a stem-down quarter is
/// 1.0460 left of the column, on a stem-up one 1.0343; a flat, sharp, double flat and double
/// sharp stand at the same X either way.
/// </para>
/// <para>
/// NOT PORTED: the stem's real end (its pure height). The far end is taken at
/// <see cref="EngravingDefaults.MinStemLength"/> from the outermost head instead. Only the stretch within an accidental's reach
/// of the heads (its half height plus the 0.1 horizon padding) can enter the distance, and
/// every stem reaches that far; the stem's near end is inside the head's box.
/// </para>
/// </remarks>
internal readonly record struct AccidentalStem(
    bool Exists, double YBottom, double YTop, double XLeft, double XRight)
{
    /// <summary>
    /// The stem of a head column whose heads run from <paramref name="lowestPosition"/> to
    /// <paramref name="highestPosition"/> (staff positions, half spaces), standing
    /// <paramref name="xOffset"/> from the column, read from <paramref name="headFont"/>
    /// (null is the plain 20).
    /// </summary>
    public static AccidentalStem Of(
        bool up, int noteValue, NoteheadStyle style, int lowestPosition, int highestPosition,
        double xOffset = 0, MusicFontDesign? headFont = null)
    {
        if (noteValue < 2)
            return default;
        double x = xOffset + LayoutUtilities.StemAttachX(up, noteValue, style, headFont);
        double half = EngravingDefaults.StemThickness / 2;
        double length = EngravingDefaults.MinStemLength * (headFont?.Magnification ?? 1.0);
        return up
            ? new(true, lowestPosition / 2.0, highestPosition / 2.0 + length, x - half, x + half)
            : new(true, lowestPosition / 2.0 - length, highestPosition / 2.0, x - half, x + half);
    }

    /// <summary>The stem of a chord whose heads are <paramref name="notes"/>.</summary>
    public static AccidentalStem Of(
        bool up, int noteValue, NoteheadStyle style, IReadOnlyList<ChordNoteInfo> notes,
        double xOffset = 0, MusicFontDesign? headFont = null)
    {
        if (notes.Count == 0)
            return default;
        int low = int.MaxValue, high = int.MinValue;
        for (int i = 0; i < notes.Count; i++)
        {
            low = Math.Min(low, notes[i].StaffPosition);
            high = Math.Max(high, notes[i].StaffPosition);
        }
        return Of(up, noteValue, style, low, high, xOffset, headFont);
    }

    /// <summary>A chord's own stem, in the direction <paramref name="up"/> it is drawn.</summary>
    public static AccidentalStem Of(
        ChordItem chord, bool up, MusicFontDesign? headFont = null) =>
        Of(up, GlyphMetrics.NoteValueOf(chord.BaseDuration), ChordHeadPositioning.StemStyle(chord, up, GlyphMetrics.NoteValueOf(chord.BaseDuration)), chord.Notes,
            0, headFont);
}

/// <summary>
/// Parameters for accidental placement. All dimensions in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc:393-439 position_apes
/// LILYPOND-REF: scm/define-grobs.scm:85 AccidentalPlacement right-padding
/// </remarks>
/// <remarks>
/// ⚠️ NONE OF THESE SCALE WITH THE FONT. They are grob properties in the STAFF's spaces, and
/// <c>position_apes</c> reads all three raw — <c>from_scm&lt;double&gt; (get_property (me,
/// "padding"), 0.2)</c>, the <c>right-padding</c> raise, and the literal <c>0.1</c> horizon —
/// with no magstep anywhere (lily/accidental-placement.cc:391-416). MEASURED: a GRACE sharp,
/// whose glyph is 0.692957 wide, still ends exactly 0.35 = 0.15 + 0.2 left of its head
/// (audit/lp-geometry/probes/grace-column-width.ly, book GCWA: the accidental's extent in its
/// column is (-1.042957 . -0.350000)). Multiplying these by the grace's magstep was half of
/// the ledger residual grace.column.accidental.step carried until 2026-08-02.
/// </remarks>
internal sealed record AccidentalPlacementParameters
{
    public static AccidentalPlacementParameters Default { get; } = new();

    /// <summary>Padding between accidental columns in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: accidental-placement.cc:398,505 (hardcoded 0.2)</remarks>
    public double Padding { get; init; } = 0.2;

    /// <summary>Extra padding from note head in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: define-grobs.scm:85 AccidentalPlacement.right-padding</remarks>
    public double RightPadding { get; init; } = 0.15;

    /// <summary>Y-axis padding for overlap detection in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: accidental-placement.cc:413 horizon_padding</remarks>
    public double HorizonPadding { get; init; } = 0.1;

}

/// <summary>
/// Calculates accidental positions for chords following LilyPond's algorithm.
/// Uses skyline-based collision detection for precise placement.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc
///
/// Algorithm:
/// 1. Build entries with glyph Y-extents at each note's staff position
/// 2. Sort by alteration priority (naturals rightmost, flats leftmost)
///    LILYPOND-REF: accidental-placement.cc:164-184 acc_less
/// 3. Build reference LeftSkyline from noteheads
///    LILYPOND-REF: accidental-placement.cc:375-385
/// 4. Position right-to-left: each accidental placed as close to notes
///    as possible without colliding with the reference skyline
///    LILYPOND-REF: accidental-placement.cc:393-439 position_apes
///
/// IMPLEMENTED — skyline-based collision (accidental-placement.cc:338-390)
/// IMPLEMENTED — octave-first priority sorting (accidental-placement.cc:164-184)
/// IMPLEMENTED — stagger_apes (accidental-placement.cc:192-235): apes by note name, sorted
///   by size then top, zigzag within each size run (see StaggerEntries)
/// IMPLEMENTED — the seats inside one ape (accidental-placement.cc:253-301
///   set_ape_skylines, see SetApeSeats): every octave of a note name in one column,
///   the same alteration in the same octave overstruck, a different one stepped left
/// NOTE — editorial (AccidentalSuggestion) accidentals do NOT pass through this
///   class: per LilyPond they are placed ABOVE the note (define-grobs.scm:96-123,
///   direction UP), handled by the collector + ArticulationEngraver.
/// </remarks>
internal sealed class AccidentalPlacement
{
    private readonly AccidentalPlacementParameters? _stated;

    /// <param name="parameters">The placement's parameters; null reads the running style's
    /// right-padding (<c>Accidental.rightPadding</c>) at each placement — several callers keep
    /// one instance in a static field, which outlives every style.</param>
    public AccidentalPlacement(AccidentalPlacementParameters? parameters = null)
    {
        _stated = parameters;
    }

    private AccidentalPlacementParameters _params =>
        _stated ?? (EngravingStyle.Current.AccidentalRightPadding is var rp
                && rp != AccidentalPlacementParameters.Default.RightPadding
            ? AccidentalPlacementParameters.Default with { RightPadding = rp }
            : AccidentalPlacementParameters.Default);

    /// <summary>Internal entry for positioning calculations.</summary>
    private readonly record struct PlacementEntry(
        int StaffPosition,
        string Accidental,
        double YBottom,     // Lower bound in staff spaces
        double YTop,        // Upper bound in staff spaces
        double Width,       // Glyph width in staff spaces (includes paren width if courtesy)
        int Priority,       // Sorting priority: lower = rightmost
        bool IsCourtesy     // Whether this is a courtesy accidental
    );

    /// <summary>
    /// Calculates accidental positions for a chord.
    /// </summary>
    /// <param name="notes">The chord's notes.</param>
    /// <param name="headOffsets">Optional per-note head X displacement
    /// (parallel to <paramref name="notes"/>) from
    /// <see cref="ChordHeadPositioning"/> — reversed heads of seconds shift
    /// the note-column ink the accidentals must clear.
    /// LILYPOND-REF: lily/accidental-placement.cc:375-385 — the reference
    /// skyline is built from the heads' real (shifted) extents.</param>
    /// <param name="accidentalFont">The font the ACCIDENTAL grobs read — the design their
    /// font-size selects, already magnified (<see cref="GlyphMetrics.AtFontSize"/>). Null is
    /// the plain 20, which is what a grob with no font-size reads.</param>
    /// <param name="headFont">The font the NOTE HEADS read, which is not always the same one:
    /// a grace's head is font-size −3 and its accidental −4
    /// (scm/music-functions.scm:635-648 general-grace-settings), so the two grobs come out of
    /// two designs. Only the heads' Y-extent and left edge enter here, as the reference
    /// skyline. Null is the plain 20.</param>
    /// <remarks>⚠️ THE PADDINGS ARE NOT PART OF EITHER FONT — see
    /// <see cref="AccidentalPlacementParameters"/>. A smaller accidental sits at the same 0.35
    /// from its head as a full-size one.</remarks>
    /// <param name="stem">The column's stem (<see cref="AccidentalStem"/>), which joins the
    /// heads in the reference skyline. Default is no stem.</param>
    /// <param name="stems">More stems — a staff column holding several voices has one each.</param>
    public ImmutableArray<AccidentalLayout> CalculatePositions(
        IReadOnlyList<ChordNoteInfo> notes, IReadOnlyList<double>? headOffsets = null,
        MusicFontDesign? accidentalFont = null,
        MusicFontDesign? headFont = null,
        AccidentalStem stem = default,
        IReadOnlyList<AccidentalStem>? stems = null)
    {
        // Counted before it is filled: the list's size is the number of accidental-
        // carrying notes, which one pass over the chord already knows. The count also
        // answers the empty case, so a chord with no accidental leaves with nothing
        // built at all.
        int accidentalCount = 0;
        for (int i = 0; i < notes.Count; i++)
            if (!string.IsNullOrEmpty(notes[i].Accidental))
                accidentalCount++;

        if (accidentalCount == 0)
            return ImmutableArray<AccidentalLayout>.Empty;

        // Everything — a single accidental included — goes through the skyline packer:
        // LilyPond runs position_apes even for one accidental, so a lone accidental clears
        // the note by right-padding (0.15) PLUS padding (0.2) = 0.35, not 0.15 alone.
        // ⚠️ THERE USED TO BE A LIST HERE of (note, head offset) for each accidental-carrying
        // note, and the packer read only the note half of it: the head offsets reach the
        // reference skyline through `headOffsets` itself. It cost 5,782 B a keystroke
        // (session 465's census, the first to see a container whose type argument is a
        // tuple); the packer now walks `notes` and skips the bare ones, in the same order.
        return CalculateMultipleAccidentals(accidentalCount, notes, headOffsets,
            accidentalFont ?? MusicFont.Current.DesignAt(0), headFont ?? MusicFont.Current.DesignAt(0),
            stem, stems);
    }

    /// <summary>
    /// Calculates position for a single note's accidental.
    /// </summary>
    public AccidentalLayout? CalculateSinglePosition(
        NoteItem note, MusicFontDesign? accidentalFont = null,
        MusicFontDesign? headFont = null)
        => CalculateSinglePosition(note.StaffPosition, note.Accidental, note.IsCourtesy,
            accidentalFont, headFont,
            AccidentalStem.Of(note.StemUp, GlyphMetrics.NoteValueOf(note.BaseDuration),
                note.Notehead, note.StaffPosition, note.StaffPosition, 0, headFont));

    /// <summary>
    /// The placement of ONE note's accidental (a full <see cref="NoteItem"/> or a grace
    /// <see cref="GraceColumnInfo"/>, reached through its primitives). LilyPond runs the SAME
    /// position_apes over every accidental, single or chord, so this is
    /// <see cref="CalculatePositions"/> over a ONE-element list — not a separate single-ape
    /// algorithm. Grace / cue notes pass their own fonts; passing none reads the plain 20, so
    /// the full-size callers stay byte-identical. Returns null when there is no accidental.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/accidental-placement.cc:391-438 position_apes.</remarks>
    public AccidentalLayout? CalculateSinglePosition(
        int staffPosition, string? accidental, bool isCourtesy,
        MusicFontDesign? accidentalFont = null,
        MusicFontDesign? headFont = null,
        AccidentalStem stem = default)
    {
        if (string.IsNullOrEmpty(accidental))
            return null;
        var note = new ChordNoteInfo(
            staffPosition, accidental, IsCourtesy: isCourtesy);
        var layouts = CalculatePositions(
            new[] { note }, headOffsets: null, accidentalFont, headFont, stem);
        return layouts.Length > 0 ? layouts[0] : (AccidentalLayout?)null;
    }

    /// <summary>
    /// The accidental's ink-left X (the value <see cref="AccidentalLayout.XOffset"/> carries):
    /// the glyph origin sits at <paramref name="offset"/>, so the LILC left edge is
    /// offset + bbox.Left; a courtesy accidental adds its left-parenthesis width, which draws
    /// (and is packed) that much further left again.
    /// </summary>
    private static double InkLeft(
        double offset, double bboxLeft, bool isCourtesy, MusicFontDesign font)
    {
        double inkLeft = offset + bboxLeft;
        if (isCourtesy)
            inkLeft -= font.Box(MusicGlyph.AccidentalParensLeft).Width;
        return inkLeft;
    }

    /// <summary>
    /// The accidental glyph's (LEFT, RIGHT) outline skyline pair, freshly cloned so the
    /// caller may mutate it, read from <paramref name="font"/>'s design and magnified by the
    /// same font's magnification — the box
    /// (<see cref="GlyphMetrics.GetAccidentalBBox(MusicFontDesign, string?)"/>) and
    /// this outline are two readings of ONE face and must never come from two. A courtesy
    /// accidental's stencil
    /// embeds the real leftparen/rightparen glyphs at its LILC edges (padding 0), and the
    /// skyline is built over that combined stencil — so the parens' baked outline skylines
    /// are composed in here. A bare flat/double-flat instead takes the 0.375 right-side
    /// fattening, which LilyPond skips when parenthesized.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental.cc:45-84 horizontal_skylines — skylines_from_stencil
    /// over the printed stencil; :33-43 parenthesize (add_at_edge X LEFT/RIGHT, padding 0);
    /// :65-82 the flat 0.375 right-skyline merge, guarded on !parenthesized.
    /// </remarks>
    internal static (HorizontalSkyline Left, HorizontalSkyline Right) GlyphSkylinePair(
        string accidental, bool isCourtesy, MusicFontDesign font)
    {
        // The design's UNMAGNIFIED table: this whole composition happens in the design's own
        // staff spaces, so the boxes it butts the parens against must be in them too.
        var design = MusicFont.Current.Design(font.Rounded);
        // The baked outlines are in the DESIGN's own staff spaces, like the design's metric
        // table; the magnification is applied at the end, exactly once, as it is to the boxes
        // (lily/modified-font-metric.cc:62-68).
        HorizontalSkyline left, right;
        if (GlyphMetrics.RestoreMainOf(accidental) is { } restoreMain)
        {
            // A RESTORE-FIRST composite (♮♯ / ♮♭): the printed stencil is
            // natural + 0.1 + main (lily/accidental.cc:131-142), and LilyPond's skyline
            // is built over that COMPOSED stencil (:54-58 skylines_from_stencil of the
            // grob's own stencil) — so its baked outlines are composed here the same way
            // the paren glyphs are composed below, in the same frame GetAccidentalBBox
            // uses (origin at the natural's).
            var (natLeft, natRight) = design.HorizontalSkylinePair(MusicGlyph.AccidentalNatural);
            left = natLeft.Clone();
            right = natRight.Clone();
            double dx = GlyphMetrics.RestoreMainOffset(design, restoreMain);
            var (mainLeft, mainRight) = design.HorizontalSkylinePair(MusicGlyphs.Accidental(restoreMain));
            var ml = mainLeft.Clone();
            ml.Raise(dx);
            left.Merge(ml);
            var mr = mainRight.Clone();
            mr.Raise(dx);
            right.Merge(mr);
        }
        else
        {
            var (bakedLeft, bakedRight) = design.HorizontalSkylinePair(MusicGlyphs.Accidental(accidental));
            left = bakedLeft.Clone();
            right = bakedRight.Clone();
        }
        var bbox = GlyphMetrics.GetAccidentalBBox(design, accidental);
        if (isCourtesy)
        {
            // parenthesize(): each paren's stencil extent butts against the accidental's
            // LILC extent with 0 padding — open's RIGHT at the accidental's LEFT, close's
            // LEFT at its RIGHT. Raise() is the X translation of a horizontal skyline.
            MergeParen(left, right, leftParen: true,
                bbox.Left - design.Box(MusicGlyph.AccidentalParensLeft).Right, design);
            MergeParen(left, right, leftParen: false,
                bbox.Right - design.Box(MusicGlyph.AccidentalParensRight).Left, design);
        }
        else if (MusicFont.Current.StemSidePaddingFraction(
                     MusicGlyphs.Accidental(GlyphMetrics.RestoreMainOf(accidental) ?? accidental))
                 is > 0.0 and var fraction)
        {
            // The fattening keys on the grob's GLYPH-NAME, which stays the MAIN glyph
            // under restore-first — so ♮♭ takes it too, over the COMPOSED extent
            // (lily/accidental.cc:65-67: the guard reads glyph_name, the box reads
            // my_stencil's extents, and the stencil already carries the natural).
            // "a bit more padding for the right of the stem" — one box on the RIGHT
            // skyline at x = stencil-right * the font's fraction (0.375 for Emmentaler's
            // flat family, §3 #17) over the stencil's Y-extent, NOT applied to a
            // parenthesized accidental.
            var fattening = HorizontalSkyline.RentBox(
                bbox.Bottom, bbox.Top, bbox.Left, bbox.Right * fraction,
                HorizontalDirection.Right);
            right.Merge(fattening); // copies the building, so the box goes straight back
            HorizontalSkyline.GiveBox(fattening);
        }
        if (font.Magnification != 1.0)
        {
            left.Scale(font.Magnification);
            right.Scale(font.Magnification);
        }
        return (left, right);
    }

    /// <summary>
    /// <see cref="GlyphSkylinePair"/>'s answer, built once per (glyph, courtesy, design,
    /// magnification) on this thread and shared — callers must NOT mutate it.
    /// </summary>
    /// <remarks>
    /// The pair is a function of those four alone (the design's baked outlines, the paren and
    /// fattening composition and the one magnification), and it was being rebuilt for every
    /// accidental placed: 193,426 builds over the reader's corpus, 104 a keystroke at
    /// 2,183 B each — 227 KB a keystroke, 8.2% of the render (measured session 491).
    /// </remarks>
    internal static (HorizontalSkyline Left, HorizontalSkyline Right) SharedGlyphSkylinePair(
        string accidental, bool isCourtesy, MusicFontDesign font)
    {
        var pairs = t_glyphPairs ??= new();
        // The music font is in the key (§6 ② ⒞): the pair is its outline's.
        var key = (MusicFont.Current, accidental, isCourtesy, font.Rounded, font.Magnification);
        if (!pairs.TryGetValue(key, out var pair))
            pairs[key] = pair = GlyphSkylinePair(accidental, isCourtesy, font);
        return pair;
    }

    [ThreadStatic]
    private static Dictionary<(MusicFont Font, string Accidental, bool IsCourtesy, int Design, double Magnification),
        (HorizontalSkyline Left, HorizontalSkyline Right)>? t_glyphPairs;

    /// <summary>Merges one paren glyph's baked outline skylines, translated to
    /// <paramref name="dx"/> in the accidental's frame, into the accidental's pair.</summary>
    private static void MergeParen(
        HorizontalSkyline left, HorizontalSkyline right, bool leftParen, double dx, MusicFontDesign design)
    {
        var (parenLeft, parenRight) = design.HorizontalSkylinePair(
            leftParen ? MusicGlyph.AccidentalParensLeft : MusicGlyph.AccidentalParensRight);
        var pl = parenLeft.Clone();
        pl.Raise(dx);
        left.Merge(pl);
        var pr = parenRight.Clone();
        pr.Raise(dx);
        right.Merge(pr);
    }

    private ImmutableArray<AccidentalLayout> CalculateMultipleAccidentals(
        int accidentalCount,
        IReadOnlyList<ChordNoteInfo> allNotes,
        IReadOnlyList<double>? headOffsets,
        MusicFontDesign accidentalFont,
        MusicFontDesign headFont,
        AccidentalStem stem,
        IReadOnlyList<AccidentalStem>? stems)
    {
        // Lent, and given back cleared before the return (see t_placementScratch).
        var scratch = t_placementScratch ?? new PlacementScratch();
        t_placementScratch = null;
        // Build entries with glyph Y-extents. A grace / cue accidental's glyph is smaller —
        // it comes out of a smaller design AND is read at a magstep, both of which
        // `accidentalFont` has already applied — but each stays centered on the note's real
        // (unscaled) staff position.
        var entries = scratch.Entries;
        entries.EnsureCapacity(accidentalCount);
        // Indexed, not foreach: `allNotes` is an interface (RULES §5.3).
        for (int ni = 0; ni < allNotes.Count; ni++)
        {
            var n = allNotes[ni];
            if (string.IsNullOrEmpty(n.Accidental))
                continue;
            var bbox = GlyphMetrics.GetAccidentalBBox(accidentalFont, n.Accidental!);
            // Staff position is in half-spaces; convert to staff spaces
            double yCenterSS = n.StaffPosition / 2.0;

            double yBottom = yCenterSS + bbox.Bottom;
            double yTop = yCenterSS + bbox.Top;

            int priority = GetAlterationPriority(n.Accidental!);
            // LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize() adds a paren glyph each side.
            double width = n.IsCourtesy
                ? bbox.Width + GlyphMetrics.GetAccidentalParensInkWidth(accidentalFont)
                : bbox.Width;
            entries.Add(new PlacementEntry(
                n.StaffPosition, n.Accidental!, yBottom, yTop, width, priority,
                n.IsCourtesy));
        }

        // ⚠️ THIS SORT DECIDES NOTHING LilyPond decides. The order ACROSS apes is
        // StaggerEntries' (ape_less + stagger_apes) and the order WITHIN one is WalkApes'
        // (acc_less); what is left to this one is the order the apes are first MET in, which
        // breaks ape_less' ties (two apes of one size whose tops are level — LilyPond's
        // std::sort leaves those to chance). It is kept as it was so those ties fall as they
        // always have. Until 2026-09-24 it ran across apes (session 569) and until 2026-11-14
        // within them (session 1233): a sharp stood nearer the notes than a double sharp on
        // the same line, where acc_less puts the higher alteration.
        entries.Sort((a, b) =>
        {
            if (a.Priority != b.Priority)
                return a.Priority.CompareTo(b.Priority);
            return b.StaffPosition.CompareTo(a.StaffPosition);
        });

        // LILYPOND-REF: accidental-placement.cc:192-235 stagger_apes — the placing order is
        // LilyPond's: apes by note name, the larger first, each size run zigzagged from its
        // highest (StaggerEntries). Each ape leaves it as one unbroken run.
        if (entries.Count > 1)
        {
            entries = StaggerEntries(entries);
            WalkApes(entries);
        }

        // LILYPOND-REF: accidental-placement.cc:375-385 build_heads_skyline — the reference
        // LEFT skyline is built from ALL noteheads of the column (not only the
        // accidental-carrying ones), at their real X extents; heads reversed to the LEFT of a
        // down-stem (seconds) shift their box. The stems join them (AccidentalStem): a down
        // stem reaches below the heads, where a natural's lower-right stroke meets it.
        // One box per note of the column, then one per stem.
        var headBoxes = scratch.HeadBoxes;
        headBoxes.EnsureCapacity(allNotes.Count + 1 + (stems?.Count ?? 0));
        if (stem.Exists)
            headBoxes.Add((stem.YBottom, stem.YTop, stem.XLeft, stem.XRight));
        if (stems is not null)
            for (int i = 0; i < stems.Count; i++)
                if (stems[i].Exists)
                    headBoxes.Add((stems[i].YBottom, stems[i].YTop, stems[i].XLeft, stems[i].XRight));
        for (int i = 0; i < allNotes.Count; i++)
        {
            double headOffset = headOffsets != null && i < headOffsets.Count ? headOffsets[i] : 0;
            double yCenterSS = allNotes[i].StaffPosition / 2.0;
            // The HEADS' own font, which is not the accidentals' when the two grobs carry
            // different font-sizes (a grace: −3 against −4).
            var nhBBox = headFont.Box(MusicGlyph.NoteheadBlack);
            // headOffset arrives already scaled (ChordHeadPositioning).
            headBoxes.Add((
                yCenterSS + nhBBox.Bottom,
                yCenterSS + nhBBox.Top,
                headOffset,
                headOffset + nhBBox.Width));
        }
        // LILYPOND-REF: accidental-placement.cc:398-400 — left_skyline = heads; raise by
        // -right-padding (0.15) so accidentals keep that much clear of the notes.
        // The running reference lives in the scratch's pair of skylines (see PlacementScratch).
        var reference = HorizontalSkyline.FromBoxesInto(scratch.ReferenceA, headBoxes);
        reference.Raise(-_params.RightPadding);

        // Position right-to-left with skyline-to-skyline nesting.
        // LILYPOND-REF: accidental-placement.cc:391-438 position_apes.
        // One layout per entry, written straight into the result's array (a list told this
        // exact size and then copied out was 3,211 B a keystroke — session 463).
        var layouts = new AccidentalLayout[entries.Count];
        int placed = 0;

        // LILYPOND-REF: accidental-placement.cc:55-81 add_accidental — the accidentals of ONE
        // note name are one APE, whatever the octave and whatever the alteration, and
        // position_apes moves an ape as one thing. Each ape is one unbroken run of `entries`
        // (StaggerEntries), already in the order set_ape_skylines walks it (WalkApes).
        // 40.11 columns a keystroke over the reader's corpus, and 99.9% of them hold ONE
        // note name (session 448) — almost always one accidental, whose seat is 0.
        double lastOffset = 0.0;
        Span<double> seatBuffer = stackalloc double[8];

        for (int start = 0; start < entries.Count;)
        {
            int noteName = NoteNameOf(entries[start].StaffPosition);
            int end = start + 1;
            while (end < entries.Count && NoteNameOf(entries[end].StaffPosition) == noteName)
                end++;

            // LILYPOND-REF: accidental-placement.cc:253-301 set_ape_skylines — first the
            // accidentals are seated against EACH OTHER …
            Span<double> seats = end - start <= seatBuffer.Length
                ? seatBuffer[..(end - start)]
                : new double[end - start];
            SetApeSeats(entries, start, end, accidentalFont, seats);

            // … and the ape's skyline is all of theirs merged where they were seated
            // (:292-296). LILYPOND-REF: accidental-placement.cc:411-416 position_apes — nest
            // the ape's RIGHT skyline against the accumulated LEFT skyline (horizon padding
            // 0.1), then back off by the inter-column padding (0.2).
            // ⚠️ THE MERGED SKYLINE IS NOT BUILT. The distance of a merge is the greatest of
            // its members' distances — the merge is the pointwise furthest of them, and the
            // horizon padding widens each building on its own (lily/skyline.cc:558-615
            // padded) — and a member seated `seat` to the left stands that much further off.
            // So each glyph's SHARED right skyline is read where it is, as it was for one
            // accidental, and nothing is built for the ape.
            double distance = double.NegativeInfinity;
            for (int k = start; k < end; k++)
            {
                var member = entries[k];
                // The glyph's own LEFT/RIGHT outline skylines, out of the same font as its
                // box and lifted onto the note's staff position (the Y centre does not scale
                // — a grace's head sits on the real staff lines).
                // ⚠️ THE PAIR IS SHARED (SharedGlyphSkylinePair) AND NEVER MUTATED: the right
                // one is shifted into a scratch copy only the distance reads, and the left one
                // is shifted, raised and merged into the new reference in one step below.
                var (_, memberRight) =
                    SharedGlyphSkylinePair(member.Accidental, member.IsCourtesy, accidentalFont);
                double d = HorizontalSkyline.ShiftedScratch(memberRight, member.StaffPosition / 2.0)
                    .Distance(reference, _params.HorizonPadding) + seats[k - start];
                if (d > distance)
                    distance = d;
            }
            double offset = -distance;
            if (double.IsInfinity(offset))
                offset = lastOffset;
            else
                offset -= _params.Padding;
            lastOffset = offset;

            for (int k = start; k < end; k++)
            {
                var entry = entries[k];
                // LILYPOND-REF: accidental-placement.cc:423-425 position_apes — every grob of
                // the ape is translated by the ape's offset, on top of its seat.
                double origin = offset + seats[k - start];
                double yCenterSS = entry.StaffPosition / 2.0;

                // LILYPOND-REF: accidental-placement.cc:418-421 — the new LEFT skyline is the
                // ape's LEFT skyline shifted into place, merged over the old one: one member
                // at a time here. After the LAST accidental nothing reads it, so it is not
                // built.
                if (placed + 1 < entries.Count)
                {
                    var (glyphLeft, _) =
                        SharedGlyphSkylinePair(entry.Accidental, entry.IsCourtesy, accidentalFont);
                    reference = HorizontalSkyline.ShiftedRaisedOverInto(
                        ReferenceEquals(reference, scratch.ReferenceA) ? scratch.ReferenceB : scratch.ReferenceA,
                        glyphLeft, yCenterSS, origin, reference);
                }

                // XOffset is the whole accidental's ink-left (what DrawAccidentalAtInkLeft and
                // the reservation boxes anchor to): the glyph origin lands at `origin`, so its
                // LILC left edge is origin + bbox.Left. A courtesy accidental's left
                // parenthesis draws that much further left again (DrawAccidentalAtInkLeft:
                // accInkLeft = inkLeftX + leftParen.Width) and its box is already packed
                // there, so the anchor must be the group left, not the bare glyph's. All out
                // of the accidental's own font.
                var bbox = GlyphMetrics.GetAccidentalBBox(accidentalFont, entry.Accidental);
                layouts[placed++] = new AccidentalLayout(
                    entry.StaffPosition, entry.Accidental,
                    InkLeft(origin, bbox.Left, entry.IsCourtesy, accidentalFont), entry.IsCourtesy);
            }

            start = end;
        }

        scratch.Entries.Clear();
        scratch.HeadBoxes.Clear();
        scratch.ReferenceA.Clear();
        scratch.ReferenceB.Clear();
        t_placementScratch = scratch;
        return System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(layouts);
    }

    /// <summary>The note name of a staff position: what an ape is keyed on.</summary>
    private static int NoteNameOf(int staffPosition) => ((staffPosition % 7) + 7) % 7;

    /// <summary>
    /// Puts each ape's accidentals — each unbroken run of one note name — in the order
    /// <c>set_ape_skylines</c> walks them: the higher octave first, and within an octave the
    /// natural first, then the higher alteration.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental-placement.cc:160-181 acc_less — by octave, then "naturals
    ///   are the largest (so that they don't get confused with cancellation naturals); apart
    ///   from that, we order according to the alteration";
    ///   lily/accidental-placement.cc:256-268 set_ape_skylines sorts by it and walks from the
    ///   END, so the largest is seated first and stands nearest the notes.
    /// A run is one accidental in all but a few columns, so this is an insertion sort over
    /// the list itself: nothing is built, and two accidentals acc_less calls equal keep the
    /// order they came in.
    /// </remarks>
    private static void WalkApes(List<PlacementEntry> entries)
    {
        for (int start = 0; start < entries.Count;)
        {
            int noteName = NoteNameOf(entries[start].StaffPosition);
            int end = start + 1;
            while (end < entries.Count && NoteNameOf(entries[end].StaffPosition) == noteName)
                end++;
            for (int i = start + 1; i < end; i++)
            {
                var moving = entries[i];
                int j = i;
                while (j > start && AccLess(entries[j - 1], moving))
                {
                    entries[j] = entries[j - 1];
                    j--;
                }
                entries[j] = moving;
            }
            start = end;
        }
    }

    /// <summary><c>acc_less</c>: whether <paramref name="p"/> is seated to the left of
    /// <paramref name="q"/> in their ape (the two have one note name).</summary>
    /// <remarks>LILYPOND-REF: lily/accidental-placement.cc:160-181 acc_less.</remarks>
    private static bool AccLess(PlacementEntry p, PlacementEntry q)
    {
        int pOctave = OctaveOf(p.StaffPosition), qOctave = OctaveOf(q.StaffPosition);
        if (pOctave != qOctave)
            return pOctave < qOctave;

        int pAlteration = GlyphMetrics.AlterationQuartersOf(p.Accidental);
        int qAlteration = GlyphMetrics.AlterationQuartersOf(q.Accidental);
        if (pAlteration == 0)
            return false;
        if (qAlteration == 0)
            return true;

        return pAlteration < qAlteration;
    }

    /// <summary>The octave a staff position stands in, counted in sevens from the middle
    /// line — only ever compared between two positions of one note name.</summary>
    private static int OctaveOf(int staffPosition) => (staffPosition - NoteNameOf(staffPosition)) / 7;

    /// <summary>
    /// Seats the accidentals of one ape — <paramref name="entries"/> from
    /// <paramref name="start"/> to <paramref name="end"/>, in the order <see cref="WalkApes"/>
    /// left them — against each other: <paramref name="seats"/> receives each one's X from the
    /// ape's own origin (0 or left of it).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental-placement.cc:259-290 set_ape_skylines — "we assume that
    ///   accidentals in different octaves won't collide. If two or more accidentals are in the
    ///   same octave: 1) if they are the same accidental, print them in overstrike 2) otherwise,
    ///   shift one to the left so they don't overlap." The first of an octave is not moved; a
    ///   different alteration in the same octave is put with its extent's RIGHT
    ///   <c>padding</c> left of the extent's LEFT of the one before.
    /// The extents are the grobs' own (<c>a->extent (a, X_AXIS)</c>): BOXES, a courtesy
    /// accidental's with its parentheses — not the outlines the ape then meets the column with.
    /// <para>
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1233/ap, extents from the paper column):
    /// <c>cisis'</c> and <c>cis'</c> on one beat in two voices, 𝄪 (−1.39 . −0.39) and ♯
    /// (−2.69 . −1.59); <c>&lt;cis' ces''&gt;</c>, ♯ (−1.45 . −0.35) and ♭ (−1.57 . −0.65) —
    /// one origin, though the flat alone would stand at −0.35.
    /// </para>
    /// <para>
    /// ⚠️ UNTIL 2026-11-14 (session 1233) an ape was (note name, GLYPH), the first of each
    /// met the column alone and the rest snapped to it: a second alteration on the same line
    /// was nested outline against outline (0.039–0.045 nearer than the boxes allow), one in
    /// another octave took a column of its own, and an octave that needed more room than the
    /// first one placed did not get it.
    /// </para>
    /// </remarks>
    private void SetApeSeats(
        List<PlacementEntry> entries, int start, int end, MusicFontDesign accidentalFont,
        Span<double> seats)
    {
        double padding = _params.Padding;
        int lastOctave = 0;
        double offset = 0;
        double lastOffset = 0;
        int lastAlteration = 0;
        for (int k = start; k < end; k++)
        {
            var a = entries[k];
            int octave = OctaveOf(a.StaffPosition);
            int alteration = GlyphMetrics.AlterationQuartersOf(a.Accidental);
            var (left, right) = ExtentOf(a, accidentalFont);

            double seat = 0;
            if (k == start || octave != lastOctave)
            {
                lastOffset = 0;
                offset = left - padding;
            }
            else if (alteration == lastAlteration)
                seat = lastOffset;
            else // Our alteration is different from the last one
            {
                double thisOffset = offset - right;
                seat = thisOffset;

                lastOffset = thisOffset;
                offset -= (right - left) + padding;
            }
            seats[k - start] = seat;

            lastOctave = octave;
            lastAlteration = alteration;
        }
    }

    /// <summary>The X extent of an accidental's stencil from its glyph origin: the glyph's
    /// box, and around a courtesy accidental its two parentheses.</summary>
    /// <remarks>LILYPOND-REF: lily/accidental.cc:33-43 parenthesize's two add_at_edge — each
    /// parenthesis is added at the stencil's edge with no padding.</remarks>
    private static (double Left, double Right) ExtentOf(PlacementEntry entry, MusicFontDesign font)
    {
        var bbox = GlyphMetrics.GetAccidentalBBox(font, entry.Accidental);
        return entry.IsCourtesy
            ? (bbox.Left - font.Box(MusicGlyph.AccidentalParensLeft).Width,
               bbox.Right + font.Box(MusicGlyph.AccidentalParensRight).Width)
            : (bbox.Left, bbox.Right);
    }

    /// <summary>
    /// The entry list and the head boxes <see cref="CalculateMultipleAccidentals"/> places a
    /// column's accidentals from, lent from one pair the thread keeps between columns.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 464's census, Release, the reader's corpus, eight forward keystrokes
    /// a book): 40.11 columns a keystroke, the entry list told its exact size and dropped at
    /// the return — 4,175 B a keystroke, every one unreachable when the render returned. The
    /// head boxes beside it are a tuple list, which no census spelling sees;
    /// <see cref="HorizontalSkyline.FromBoxes"/> builds its own buildings from them.
    /// <para>
    /// Renting takes the pair out of the drawer (session 421's idiom) and the clearing is on
    /// give. A stagger of three or more hands back a NEW entry list, which is the one read
    /// below it; the lent one is still the pair's. The method has no exit between the rent
    /// and the give. WHAT IT RETAINS is two emptied lists at the thread's widest column.
    /// </para>
    /// <para>
    /// The running reference skyline is the scratch's too (session 498): the heads' skyline is
    /// built into <see cref="PlacementScratch.ReferenceA"/>, and each accidental's merge is
    /// written into whichever of the pair the reference it reads is not. MEASURED (Release, the
    /// reader's corpus, eight forward keystrokes a book): 33.18 columns and 33.24 merges a
    /// keystroke, 38,098 B between them, every skyline dropped at the return — a column
    /// almost always carries ONE accidental, so almost every merge was the one after the
    /// last accidental, which nothing reads and which is therefore no longer built. The pair
    /// is cleared on give with the lists, and retained as two emptied skylines beside them.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static PlacementScratch? t_placementScratch;

    private sealed class PlacementScratch
    {
        public readonly List<PlacementEntry> Entries = new();
        public readonly List<(double YBottom, double YTop, double XLeft, double XRight)> HeadBoxes = new();
        public readonly HorizontalSkyline ReferenceA = new(HorizontalDirection.Left);
        public readonly HorizontalSkyline ReferenceB = new(HorizontalDirection.Left);
    }

    /// <summary>
    /// Puts the accidentals in the order <c>position_apes</c> places them — nearest the notes
    /// first: grouped into APES by note name, the apes sorted and staggered as LilyPond does.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental-placement.cc:55-81 add_accidental — an ape is keyed on the
    ///   pitch's NOTENAME (the default accidentalGrouping), so every octave of one letter is one
    ///   ape, placed as one column.
    /// LILYPOND-REF: lily/accidental-placement.cc:129-146 ape_priority / ape_less — the ape with
    ///   MORE accidentals first, then the one whose ink TOP is lower ("right is up because
    ///   we're horizontal").
    /// LILYPOND-REF: lily/accidental-placement.cc:192-235 stagger_apes — within each run of
    ///   one size, taken alternately from the back (the highest) and the front (the lowest),
    ///   then the whole list reversed; lily/accidental-placement.cc:407 position_apes iterates
    ///   it from the END, so the placing order is the zigzag itself: highest, lowest, next
    ///   highest …
    /// <para>
    /// ⚠️ IT WAS AN INVENTION until 2026-09-24 (session 569, ledger
    /// <c>chord.accidental.flat-stagger-span</c>): accidentals were grouped by VERTICAL
    /// PROXIMITY (within one staff space), which accidental-placement.cc does not contain, so a
    /// chord whose accidentals all stood within reach of each other — <c>ees'' ges'' bes''</c> —
    /// was one group, kept in pitch order, and stepped every flat a full column left of the one
    /// before. LilyPond places bes'', then ees'' (which tucks under it), then ges''.
    /// </para>
    /// <para>
    /// The members of an ape keep their incoming order (the caller's sort: octave-descending
    /// within an alteration), and <see cref="CalculateMultipleAccidentals"/>' column sharing
    /// seats them — the same seat LilyPond's <c>set_ape_skylines</c> gives octaves of one ape.
    /// </para>
    /// </remarks>
    private static List<PlacementEntry> StaggerEntries(List<PlacementEntry> entries)
    {
        var apes = new List<(int NoteName, List<PlacementEntry> Members, double Top)>();
        foreach (var entry in entries)
        {
            int noteName = ((entry.StaffPosition % 7) + 7) % 7;
            int k = apes.FindIndex(a => a.NoteName == noteName);
            if (k < 0)
                apes.Add((noteName, new List<PlacementEntry> { entry }, entry.YTop));
            else
            {
                var ape = apes[k];
                ape.Members.Add(entry);
                apes[k] = (ape.NoteName, ape.Members, Math.Max(ape.Top, entry.YTop));
            }
        }
        if (apes.Count < 2)
            return entries;

        // ape_less: more grobs first, then the lower top first.
        var sorted = apes
            .OrderByDescending(a => a.Members.Count)
            .ThenBy(a => a.Top)
            .ToList();

        var result = new List<PlacementEntry>(entries.Count);
        for (int i = 0; i < sorted.Count;)
        {
            int size = sorted[i].Members.Count;
            int end = i;
            while (end < sorted.Count && sorted[end].Members.Count == size)
                end++;
            // stagger_apes' zigzag over this run: back (highest), front (lowest), back, …
            int front = i, back = end - 1;
            bool parity = true;
            while (front <= back)
            {
                var ape = parity ? sorted[back--] : sorted[front++];
                result.AddRange(ape.Members);
                parity = !parity;
            }
            i = end;
        }
        return result;
    }

    /// <summary>
    /// Gets alteration sorting priority.
    /// Lower values are placed first (rightmost, closest to notes).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: accidental-placement.cc acc_less() — naturals sort largest so they are
    /// never confused with cancellation naturals; they are placed rightmost (priority 0 here).
    /// </remarks>
    private static int GetAlterationPriority(string accidental) => accidental switch
    {
        "natural" => 0,
        // A restore-first composite sorts as its MAIN glyph: the grob's alteration is
        // the pitch's (±1/2), the prepended natural is only stencil.
        "sharp" or "naturalSharp" => 1,
        "doubleSharp" => 2,
        "flat" or "naturalFlat" => 3,
        "doubleFlat" => 4,
        _ => 2
    };
}
