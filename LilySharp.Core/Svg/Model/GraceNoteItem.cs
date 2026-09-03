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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Type of grace note.
/// </summary>
public enum GraceNoteType
{
    /// <summary>Regular grace note (no slash).</summary>
    Grace,
    /// <summary>Acciaccatura (slashed grace note, very short).</summary>
    Acciaccatura,
    /// <summary>Appoggiatura (unslashed grace note, takes time from main note).</summary>
    Appoggiatura
}

/// <summary>
/// One NOTEHEAD of a grace column — the part of a grace that a pitch owns.
/// </summary>
/// <remarks>
/// Everything here is per-HEAD; everything a whole column shares (the written duration, the
/// dots, the flag and the beam count) lives on <see cref="GraceColumnInfo"/>. The split is
/// the same one <see cref="ChordNoteInfo"/> makes against <see cref="ChordItem"/>, and for
/// the same reason: a chord is one column that sounds N pitches, not N columns.
/// </remarks>
public readonly record struct GraceHeadInfo(
    int StaffPosition,      // Staff position (-6 = middle C in treble clef)
    string? Accidental,     // "sharp", "flat", "natural", "doubleSharp", "doubleFlat", or null
    bool NeedsLedger,       // Whether ledger lines are needed
    int Midi = 0,           // Absolute MIDI pitch (for tab fret resolution)
    // The '\N' written on this grace note, or null for automatic string selection.
    // ⚠️ THIS IS NOT A GROB, which is why a grace note can carry it while it carries no
    // @staccato and no @text: a string number draws NOTHING on a notation staff (MEASURED
    // 2026-08-30 — `c'4\2` and `c'4` render byte-identical) and is only an input to
    // Tunings.CalculateFret, so it asks for no column of its own. It was silently ignored
    // until session 298, and that cost the reader's own `Real Gone.lys` two grace notes
    // drawn on whatever string the resolver picked. See Semantics.GraceBodySupport.
    int? StringNumber = null,
    // This head's accidental ink-left X in staff spaces from the COLUMN's origin, once the
    // column's accidentals have been packed together — null where the head stands alone on
    // its column and the per-head solve is the same answer. Same frame and same reason as
    // ChordNoteInfo.AccidentalX.
    // MEASURED on LilyPond 2.27.3 (scratch/p308/lp, book y3_gacc `\grace { <cis' dis'>16 }`
    // against y4_nacc `<cis' dis'>4`): a grace chord's accidentals ARE stacked, at the
    // grace's own -4 font — 0.8623 apart where a full-size chord's are 1.3000 apart.
    double? AccidentalX = null,
    // How far RIGHT of the column's origin this head is drawn, in staff spaces — the SECONDS
    // shift, which is a property of the head and not of the column.
    // MEASURED (scratch/p308/lp, y1_gsecond `\grace { <c' d'>16 }` against y2_nsecond):
    // LilyPond shifts the upper head of a second inside a GRACE chord by 0.8530 where it
    // shifts a full-size one by 1.2392 — the ordinary chord rule at the grace's scale.
    double HeadXOffset = 0
);

/// <summary>
/// One COLUMN of a grace group: the noteheads that sound together at one written duration.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/grace-spacing-engraver.cc — each grace COLUMN has its own duration
/// for spring-based spacing calculation.
/// <para>
/// ⚠️ THIS WAS ONE NOTE UNTIL SESSION 308, AND THAT IS WHAT KEPT A CHORD OUT OF A GRACE
/// BODY. The flat <c>ImmutableArray&lt;GraceNoteInfo&gt;</c> it replaced could not say
/// "these two sound together", so <c>MeasureCollector.CollectGraceNotes</c> read bare notes
/// and reported a chord as a <c>Semantics.GraceDropKind.Element</c> drop. The difficulty was
/// never the ADDRESS a grace note cannot name — HANDOFF §2 U8 ⒜ measured that
/// <c>ChordHeadPositioning.CalculateOffsets</c> takes none and is already called with a
/// scale — it was this one word.
/// </para>
/// </remarks>
public readonly record struct GraceColumnInfo(
    // The heads of this column, LOW TO HIGH. One head is an ordinary grace note; more than
    // one is a chord.
    ImmutableArray<GraceHeadInfo> Heads,
    Fraction BaseDuration,  // Written duration of this column (for spacing calculation)
    // Augmentation dots on THIS column's duration. SEPARATE from BaseDuration for the same
    // reason NoteItem.Dots is separate from NoteItem.BaseDuration: the note VALUE picks the
    // head, the flag and the beam count, and a dotted eighth is an eighth to all three.
    // Folding the dot into the fraction would make `grace { d'8. }` a sixteenth and give it
    // two beams. Read and thrown away until session 299, which is what LYS4020 reported.
    // LILYPOND-REF: scm/music-functions.scm:635-648 general-grace-settings —
    //   (Voice Dots font-size -3): a grace's dot comes out of the SAME font as its head
    //   (GraceNoteItem.Font), unlike its accidental, which states -4.
    int Dots = 0,
    // An invisible time-filler (`s`), as RestItem.IsSpacer is: it holds a column open and
    // draws nothing. Only meaningful on a column with no head.
    bool IsSpacer = false
)
{
    /// <summary>
    /// The column a single pitch makes — the shape every grace was until session 308, kept
    /// because a one-head column IS what an ordinary grace note is, and spelling the array
    /// out at each of those call sites would say nothing extra.
    /// </summary>
    public GraceColumnInfo(
        int staffPosition, string? accidental, bool needsLedger, Fraction baseDuration,
        int midi = 0, int? stringNumber = null, int dots = 0)
        : this(
            ImmutableArray.Create(
                new GraceHeadInfo(staffPosition, accidental, needsLedger, midi, stringNumber)),
            baseDuration, dots)
    {
    }

    /// <summary>
    /// How LONG this grace is — the note value with its dots applied. This is what SPACING
    /// asks for, while <see cref="BaseDuration"/> is what the head, the flag and the beam
    /// count ask for.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:163-180 <c>Spacing_spanner::note_spacing</c> —
    /// the grace branch reads <c>delta_t.grace_part_</c>, a MOMENT, so a dotted grace is
    /// three sixteenths there and an eighth to its glyphs. Same split as
    /// <c>MusicItem.Duration</c> against <c>MusicItem.BaseDuration</c>.
    /// </remarks>
    public Fraction Length => Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration;

    /// <summary>
    /// Value equality over the HEADS, not over the array they came in.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SYNTHESIZED PAIR IS WRONG FOR THIS TYPE, and it is wrong SILENTLY.
    /// <c>ImmutableArray&lt;T&gt;</c> answers <c>Equals</c> and <c>GetHashCode</c> off the
    /// underlying array's REFERENCE, so a record struct holding one gets a value type whose
    /// value nobody can compute: two columns built from the same pitches compare unequal and
    /// hash differently. A grace column is a VALUE by
    /// <c>LilySharp.Tests.ModelEqualityKindTests</c>' axes — a description, not an occurrence
    /// — and <c>MeasureContentKey</c> leans on exactly that to decide a system may be reused.
    /// <para>
    /// MEASURED rather than argued: with the synthesized pair,
    /// <c>SystemLayoutCacheTests.MultiStaff_ReusesSystems_AndStaysByteIdentical</c> went red
    /// on a WIDTH-PRESERVING edit — 10 cache entries where 9 were reused — because one
    /// system's key moved without its content moving. The page was never wrong; the reuse was
    /// declined, which is the shape a content key fails in.
    /// </para>
    /// ⚠️ <see cref="GraceHeadInfo"/> NEEDS NO SUCH OVERRIDE: every one of its fields is a
    /// primitive, so the compiler's pair already answers by value.
    /// </remarks>
    public bool Equals(GraceColumnInfo other)
    {
        if (BaseDuration != other.BaseDuration || Dots != other.Dots)
            return false;
        if (Heads.IsDefaultOrEmpty || other.Heads.IsDefaultOrEmpty)
            return Heads.IsDefaultOrEmpty && other.Heads.IsDefaultOrEmpty;
        if (Heads.Length != other.Heads.Length)
            return false;
        for (int i = 0; i < Heads.Length; i++)
            if (!Heads[i].Equals(other.Heads[i]))
                return false;
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(BaseDuration);
        hc.Add(Dots);
        if (!Heads.IsDefaultOrEmpty)
            foreach (var head in Heads)
                hc.Add(head);
        return hc.ToHashCode();
    }

    /// <summary>The LOWEST head of this column.</summary>
    /// <remarks>
    /// ⚠️ THERE IS NO <c>StaffPosition</c> ON A COLUMN, on purpose. A chord has several, and
    /// picking one of them behind the caller's back is exactly the shape this type was
    /// changed to remove: every reader has to say whether it wants the lowest, the highest,
    /// or all of them.
    /// </remarks>
    public GraceHeadInfo Lowest => Heads[0];

    /// <summary>The HIGHEST head of this column.</summary>
    public GraceHeadInfo Highest => Heads[Heads.Length - 1];

    /// <summary>True when more than one pitch sounds on this column.</summary>
    public bool IsChord => Heads.Length > 1;

    /// <summary>
    /// True when this column sounds NOTHING — a rest. A rest is a column with no head, which
    /// is the whole of what a rest is to a layout.
    /// </summary>
    /// <remarks>
    /// ⚠️ A REST IS NOT A SMALL REST. Everything else a grace owns carries a
    /// <c>font-size</c> out of <c>general-grace-settings</c>; that list names Stem, Flag,
    /// NoteHead, TabNoteHead, Dots, Accidental, Script, Fingering and StringNumber, and it
    /// does NOT name Rest — so a grace rest reads the STAFF's own size and comes out FULL
    /// SIZE. MEASURED, in one book, side by side (scratch/p308/lp2/s2_gracerestchord,
    /// <c>\grace { r16 d'16 }</c>): the rest's glyph is drawn at 0.0040 and the head beside
    /// it at 0.0028 = magstep(−3), and the rest's path data is byte-identical to a
    /// main-stream rest's.
    /// LILYPOND-REF: scm/music-functions.scm:636-650 <c>general-grace-settings</c> (v2.26.0).
    /// <para>
    /// ⇒ That is also why the column AFTER a rest is wider than the column after a head
    /// (1.7000 against 1.4180, same book set): a full-size glyph reaches further right.
    /// </para>
    /// </remarks>
    public bool IsRest => Heads.IsDefaultOrEmpty;
}

/// <summary>
/// A group of grace notes attached to a main note.
/// </summary>
/// <remarks>
/// LILYPOND-REF: grace-engraver.cc:36-80 Grace_engraver class
/// LILYPOND-REF: define-grobs.scm:1358-1402 GraceSpacing grob definition
///
/// Grace notes are rendered smaller (typically 65% of normal size) and
/// placed before their main note. Acciaccaturas have a diagonal slash
/// through the stem.
/// </remarks>
public sealed record GraceNoteItem
{
    // Identity, not value equality: see ModelIdentity.
    public bool Equals(GraceNoteItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);

    /// <summary>The type of grace note.</summary>
    public GraceNoteType Type { get; }

    /// <summary>The columns in this grace group, in written order.</summary>
    /// <remarks>
    /// ⚠️ NOT "the notes": a column holds a whole chord (session 308). The name was
    /// <c>Notes</c> while the two were the same thing, and leaving it would have made every
    /// <c>Notes.Length</c> in the layout read as a note count, which it no longer is — the
    /// beam count, the column offsets and the reserved width are all per COLUMN.
    /// </remarks>
    public ImmutableArray<GraceColumnInfo> Columns { get; }

    /// <summary>The measure index where this grace note appears.</summary>
    public int MeasureIndex { get; }

    /// <summary>The item index of the main note this grace is attached to.</summary>
    /// <remarks>
    /// ⚠️ AN INDEX INTO <see cref="VoiceIndex"/>'S OWN ITEM LIST, which is why that field
    /// had to exist. Every voice numbers its items from zero, so resolving this against the
    /// staff's PRIMARY voice returns whatever note happens to share the number — the very
    /// defect <c>LayoutUtilities.VoiceItemAt</c> was written for on the annotation side. It
    /// was invisible while a grace body was not walked: the index then counted the same
    /// items in both voices up to the grace, so the two answers coincided by accident.
    /// Walking the body (session 310) makes a lower voice's grace shift ITS OWN indices and
    /// not the primary's, and the accident ends.
    /// </remarks>
    public int MainNoteItemIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; init; }

    /// <summary>
    /// True when the writer slurred the LAST grace column to the main note by hand —
    /// <c>grace { g16( } a8)</c> — so the group draws the same bow an
    /// <c>appoggiatura</c> draws on its own.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/grace-init.ly startGraceSlur / stopGraceSlur — an appoggiatura IS a
    /// grace with a slur event on its last note and the matching end on the main note; a
    /// hand-written <c>(</c> … <c>)</c> on a plain <c>\grace</c> is the same two events, and
    /// LilyPond draws the same Slur for both. Lily# draws that bow from the group
    /// (<c>SharedRenderer.DrawGraceSlur</c>) rather than through the ordinary slur engraver,
    /// so the hand-written pair has to reach the group the way the keyword does: the
    /// collector reads the <c>(</c> off the last column and takes the <c>)</c> off the main
    /// note (<c>MeasureCollector.ProcessGraceRegion</c> and the walk's main-note arm). A
    /// <c>(</c> on an EARLIER grace column, or one whose <c>)</c> lands past the main note,
    /// is not this bow and is still reported dropped (LYS4020) — that is the island HANDOFF
    /// §2 U8 ⒝2 names: grace marks through the ordinary Slur engraver at the grace font.
    /// </remarks>
    public bool ExplicitSlur { get; init; }

    /// <summary>Global staff index this grace group belongs to (multi-staff
    /// routing; see <c>DynamicItem.StaffIndex</c>). 0 for single-staff.</summary>
    public int StaffIndex { get; }

    /// <summary>
    /// Which voice OF THAT STAFF wrote this grace — the list
    /// <see cref="MainNoteItemIndex"/> counts. 0 for the primary voice, which is every
    /// score that writes no <c>voice { }</c> span.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:368 — <c>Grace_engraver</c> is consisted by
    /// <c>\name Voice</c>, so a grace belongs to one voice exactly as a script or a
    /// dynamic does; Lily# records it for the same reason those do (see
    /// <c>LayoutUtilities.VoiceItemAt</c>, whose remarks carry the measurement).
    /// </remarks>
    public int VoiceIndex { get; }

    /// <summary>
    /// Where each of <see cref="Columns"/> STANDS in <see cref="VoiceIndex"/>'s own item list
    /// — entry for entry, the same frame <see cref="MainNoteItemIndex"/> counts in.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE ADDRESS THE GRACE DID NOT HAVE, and it exists because the body is walked
    /// now: since session 310 a grace column IS a measure item
    /// (<c>MeasureCollector.ProcessGraceRegion</c>), so it has an item index like every other,
    /// and the ordinary engravers reach a grob by that index. Recording it is what lets a
    /// LAYOUT fact be published per column — see <c>ScoreLayout.GraceColumnXs</c>, which the
    /// ordinary note pass reads to find out where a grace column stands.
    /// <para>
    /// ⚠️ NOT DERIVABLE BY ARITHMETIC, which is why it is carried rather than recomputed as
    /// <c>MainNoteItemIndex - Columns.Length + i</c>: a tuplet inside a grace body adds a
    /// BRACKET item that is not a column (<c>MeasureCollector.CanStandInGraceTime</c> lets
    /// <c>TupletExpressionSyntax</c> through), so the columns are not guaranteed to be the
    /// contiguous run immediately before the main note.
    /// </para>
    /// ⚠️ EMPTY IS LEGAL: a hand-built group (tests, and the two exporters' fixtures) states
    /// its columns without stating where they stand, and every reader of this treats an
    /// absent entry as "this column publishes no address" rather than as index 0.
    /// </remarks>
    public ImmutableArray<int> ColumnItemIndices { get; }

    /// <summary>Creates a grace note group attached to a main note.</summary>
    public GraceNoteItem(
        GraceNoteType type,
        ImmutableArray<GraceColumnInfo> columns,
        int measureIndex,
        int mainNoteItemIndex,
        int sourcePosition,
        int staffIndex = 0,
        int voiceIndex = 0,
        ImmutableArray<int> columnItemIndices = default)
    {
        Type = type;
        Columns = columns;
        MeasureIndex = measureIndex;
        MainNoteItemIndex = mainNoteItemIndex;
        SourcePosition = sourcePosition;
        StaffIndex = staffIndex;
        VoiceIndex = voiceIndex;
        ColumnItemIndices = columnItemIndices.IsDefault
            ? ImmutableArray<int>.Empty : columnItemIndices;
    }

    /// <summary>
    /// The <c>font-size</c> a grace note's voice carries, in LilyPond's sixths of an octave.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:635-648 <c>general-grace-settings</c> —
    /// <c>(Voice NoteHead font-size -3)</c>, and the same −3 for Stem, Flag, Dots and Script.
    /// This is the number a grace grob STATES; everything else about its size follows from
    /// it — which Emmentaler design its glyphs come out of
    /// (<see cref="Svg.Layout.EmmentalerDesignSize.ForFontSizeStep"/>) and the magnification
    /// that design is then read at (<see cref="ScaleFactor"/>).
    /// <para>
    /// ⚠️ NOT EVERY GRACE GROB IS AT THIS SIZE, and the recipe is per-grob rather than a voice
    /// <c>fontSize</c>: the same list gives the Accidental −4
    /// (<see cref="AccidentalFontSizeStep"/>), Fingering and StringNumber −8, TabNoteHead −4.
    /// This doc said "ly/grace-init.ly graceSettings — Voice.fontSize = #-3" until 2026-08-02;
    /// grace-init.ly holds the slurs and the acciaccatura slash and nothing about size.
    /// </para>
    /// </remarks>
    public const double FontSizeStep = -3.0;

    /// <summary>
    /// The <c>font-size</c> a grace note's ACCIDENTAL carries — one step below the head's.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:635-648 <c>general-grace-settings</c> —
    /// <c>(Voice Accidental font-size -4)</c> and <c>(Voice AccidentalCautionary font-size -4)</c>,
    /// against <c>(Voice NoteHead font-size -3)</c> two lines above.
    /// <para>
    /// MEASURED, not read off alone: LilyPond prints a grace sharp 0.692957 wide =
    /// 1.100000 × magstep(−4), where the head of the same grace is 0.917939 = the 14 design's
    /// 1.298161 × magstep(−3) (audit/lp-geometry/probes/grace-column-width.ly book GCWA, and
    /// scratch acc-size.ly asks the Accidental grob its own <c>font-size</c> and gets −4).
    /// So a grace's accidental reads the THIRTEEN design and its head the FOURTEEN — the two
    /// grobs of one note are two faces, which is why the placement takes two fonts.
    /// </para>
    /// </remarks>
    public const double AccidentalFontSizeStep = -4.0;

    /// <summary>
    /// Scale factor for grace notes relative to normal notes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/lily-library.scm <c>magstep</c> = <c>2^(s/6)</c>, over
    ///   <see cref="FontSizeStep"/>, so the scale is <c>magstep(-3)</c> = <c>2^(-1/2)</c>.
    /// <para>
    /// This was 0.65 until 2026-08-01, with a comment that said "font size -3 corresponds to
    /// approximately 0.65" — an evaluation, and a wrong one (magstep(-3) = 0.707107). It is
    /// not only the drawn size: the grace COLUMN's width reads the head's right edge
    /// (lily/note-spacing.cc:77 <c>left_head_end</c>), so the rounding sat inside the spacing
    /// law as well.
    /// </para>
    /// <para>
    /// ⚠️ THIS IS THE MAGNIFICATION, NOT THE WHOLE SIZE. A grace glyph is the FOURTEEN
    /// design's glyph at this magnification, not the twenty's: Emmentaler is optically sized
    /// and LilyPond picks the file before it scales anything. Ask
    /// <see cref="Svg.Layout.GlyphMetrics.AtFontSize"/> for a grace metric rather than
    /// multiplying a full-size one by this — the difference is 0.004270 on the head's right
    /// edge, which is what the <c>grace.column.*</c> ledger island carried until 2026-08-02.
    /// This factor stays because it is still one of the two terms: it is what
    /// <c>AtFontSize</c> multiplies the chosen design's table by.
    /// </para>
    /// </remarks>
    public static readonly double ScaleFactor =
        Svg.Layout.EmmentalerDesignSize.Magstep(FontSizeStep);

    /// <summary>
    /// The FONT a grace grob reads its glyph dimensions from: the design
    /// <see cref="FontSizeStep"/> selects, already magnified into the page's staff spaces.
    /// Nothing read out of it is multiplied again.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:115-186 select_font — one call answers both halves,
    ///   WHICH file and at WHAT magnification, and hands back a font that has applied the
    ///   second (lily/modified-font-metric.cc:62-68 get_indexed_char_dimensions).
    /// </remarks>
    internal static Svg.Layout.GlyphMetrics.DesignMetrics Font
        => Svg.Layout.GlyphMetrics.AtFontSize(FontSizeStep);

    /// <summary>
    /// The FONT a grace's ACCIDENTAL reads — <see cref="AccidentalFontSizeStep"/>'s design at
    /// its magstep, which is not <see cref="Font"/>.
    /// </summary>
    internal static Svg.Layout.GlyphMetrics.DesignMetrics AccidentalFont
        => Svg.Layout.GlyphMetrics.AtFontSize(AccidentalFontSizeStep);

    /// <summary>
    /// The Emmentaler design a grace's ACCIDENTAL is DRAWN from — the number a drawing
    /// context's music-face scope takes, paired with <see cref="AccidentalFont"/> the way
    /// <see cref="DesignSize"/> is paired with <see cref="Font"/>.
    /// </summary>
    internal static int AccidentalDesignSize
        => Svg.Layout.EmmentalerDesignSize.ForFontSizeStep(AccidentalFontSizeStep).Rounded;

    /// <summary>
    /// The Emmentaler design a grace is DRAWN from — the rounded size in the file name, the
    /// number a drawing context's music-face scope takes.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE DECISION, TWO READERS: this and <see cref="Font"/> must come from the same
    /// <see cref="FontSizeStep"/>, or the box a grace column reserves stops being the box its
    /// glyph fills. See <see cref="Svg.Layout.EmmentalerDesignSize"/>'s remarks.
    /// </remarks>
    internal static int DesignSize
        => Svg.Layout.EmmentalerDesignSize.ForFontSizeStep(FontSizeStep).Rounded;
}