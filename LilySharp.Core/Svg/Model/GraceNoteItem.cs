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
/// Information about a single note within a grace note group.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/grace-spacing-engraver.cc — each grace note has its own duration
/// for spring-based spacing calculation.
/// </remarks>
public readonly record struct GraceNoteInfo(
    int StaffPosition,      // Staff position (-6 = middle C in treble clef)
    string? Accidental,     // "sharp", "flat", "natural", "doubleSharp", "doubleFlat", or null
    bool NeedsLedger,       // Whether ledger lines are needed
    Fraction BaseDuration,  // Duration of this grace note (for spacing calculation)
    int Midi = 0            // Absolute MIDI pitch (for tab fret resolution)
);

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
    /// <summary>The type of grace note.</summary>
    public GraceNoteType Type { get; }

    /// <summary>The notes in this grace group.</summary>
    public ImmutableArray<GraceNoteInfo> Notes { get; }

    /// <summary>The measure index where this grace note appears.</summary>
    public int MeasureIndex { get; }

    /// <summary>The item index of the main note this grace is attached to.</summary>
    public int MainNoteItemIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; init; }

    /// <summary>Global staff index this grace group belongs to (multi-staff
    /// routing; see <c>DynamicItem.StaffIndex</c>). 0 for single-staff.</summary>
    public int StaffIndex { get; }

    /// <summary>Creates a grace note group attached to a main note.</summary>
    public GraceNoteItem(
        GraceNoteType type,
        ImmutableArray<GraceNoteInfo> notes,
        int measureIndex,
        int mainNoteItemIndex,
        int sourcePosition,
        int staffIndex = 0)
    {
        Type = type;
        Notes = notes;
        MeasureIndex = measureIndex;
        MainNoteItemIndex = mainNoteItemIndex;
        SourcePosition = sourcePosition;
        StaffIndex = staffIndex;
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