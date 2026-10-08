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

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace LilySharp.Core.Svg;

/// <summary>
/// A music glyph by WHAT IT IS, independent of the font that draws it — the engine's common
/// vocabulary for music fonts, named after SMuFL (docs/smufl-design.md §0 2).
/// </summary>
/// <remarks>
/// A member is the SMuFL glyph name in PascalCase (<c>noteheadBlack</c> →
/// <see cref="NoteheadBlack"/>; a name that opens on a digit is spelled out,
/// <c>6stringTabClef</c> → <see cref="SixStringTabClef"/>). A glyph Emmentaler draws and SMuFL
/// has no name for is a <c>Feta…</c> member whose name is <c>feta.</c> + its feta name
/// (§4 of the design: SMuFL's optional-glyph convention leaves such names to the font).
/// <para>
/// The member → name / code point / feta name table is <see cref="MusicGlyphs"/>. ⚠️ The
/// order of the members is not part of anything — nothing may persist the numeric value.
/// </para>
/// </remarks>
internal enum MusicGlyph : ushort
{
    // ---- clefs ----
    GClef, GClefChange, FClef, FClefChange, CClef, CClefChange,
    UnpitchedPercussionClef1, FetaClefsPercussionChange,
    SixStringTabClef, FetaClefsTabChange,

    // ---- noteheads ----
    NoteheadDoubleWhole, NoteheadWhole, NoteheadHalf, NoteheadBlack,
    NoteheadDiamondWhole, NoteheadDiamondHalf, NoteheadDiamondBlack,
    NoteheadTriangleUpWhole, NoteheadTriangleUpHalf, NoteheadTriangleUpBlack,
    NoteheadSlashWhiteWhole, NoteheadSlashWhiteHalf, NoteheadSlashHorizontalEnds,
    NoteheadXWhole, NoteheadXHalf, NoteheadXBlack,
    NoteheadCircleX,

    // ---- rests ----
    RestMaxima, RestLonga, RestDoubleWhole, RestDoubleWholeLegerLine,
    RestWhole, RestWholeLegerLine, RestHalf, RestHalfLegerLine,
    RestQuarter, Rest8th, Rest16th, Rest32nd, Rest64th, Rest128th,

    // ---- accidentals ----
    AccidentalFlat, AccidentalNatural, AccidentalSharp, AccidentalDoubleSharp, AccidentalDoubleFlat,
    AccidentalQuarterToneSharpStein, AccidentalThreeQuarterTonesSharpStein,
    FetaAccidentalsFlatSlash, FetaAccidentalsFlatFlatSlash,
    AccidentalParensLeft, AccidentalParensRight,

    // ---- flags ----
    Flag8thUp, Flag8thDown, Flag16thUp, Flag16thDown, Flag32ndUp, Flag32ndDown,
    Flag64thUp, Flag64thDown, Flag128thUp, Flag128thDown,

    // ---- dots ----
    AugmentationDot, RepeatDot,

    // ---- time signatures ----
    TimeSig0, TimeSig1, TimeSig2, TimeSig3, TimeSig4,
    TimeSig5, TimeSig6, TimeSig7, TimeSig8, TimeSig9,
    TimeSigCommon, TimeSigCutCommon,

    // ---- fingering and figured bass ----
    Fingering0, Fingering1, Fingering2, Fingering3, Fingering4,
    Fingering5, Fingering6, Fingering7, Fingering8, Fingering9,
    Figbass0, Figbass1, Figbass2, Figbass3, Figbass4,
    Figbass5, Figbass6, Figbass7, Figbass8, Figbass9,
    FigbassFlat, FigbassNatural, FigbassSharp,

    // ---- articulations and other scripts ----
    FermataAbove, FermataBelow, FermataShortAbove, FermataShortBelow, FermataLongAbove, FermataLongBelow,
    ArticAccentAbove, ArticStaccatoAbove, ArticTenutoAbove,
    ArticTenutoStaccatoAbove, ArticTenutoStaccatoBelow,
    ArticStaccatissimoAbove, ArticStaccatissimoBelow,
    ArticMarcatoAbove, ArticMarcatoBelow,
    StringsUpBow, StringsUpBowTurned, StringsDownBow, StringsDownBowTurned,
    StringsHarmonic, StringsThumbPosition, BrassMuteClosed, PluckedSnapPizzicatoAbove,
    KeyboardPedalHeel1, KeyboardPedalHeel2, KeyboardPedalToe1, KeyboardPedalToe2,
    KeyboardPedalPed, KeyboardPedalDot, KeyboardPedalUp,

    // ---- ornaments, repeats, breaths ----
    OrnamentTrill, WiggleTrill, OrnamentTurn, OrnamentTurnInverted,
    OrnamentShortTrill, OrnamentMordent, OrnamentTremblement,
    WiggleArpeggiatoUp, Segno, Coda, BreathMarkComma, Caesura,

    // ---- system-start bracket ----
    BracketTop, BracketBottom,

    // ---- dynamics ----
    DynamicPiano, DynamicMezzo, DynamicForte, DynamicRinforzando, DynamicSforzando, DynamicZ, DynamicNiente,
}

/// <summary>
/// One row of the <see cref="MusicGlyph"/> vocabulary: its SMuFL name and code point, and
/// where Emmentaler keeps it.
/// </summary>
/// <param name="Glyph">The member.</param>
/// <param name="SmuflName">The SMuFL glyph name, or <c>feta.</c> + the feta name for a glyph
/// SMuFL has no name for.</param>
/// <param name="SmuflCodepoint">The SMuFL code point, or 0 for a <c>feta.</c> glyph (a SMuFL font
/// does not have it, and the glyph falls back to Emmentaler).</param>
/// <param name="FetaName">The glyph's name in Emmentaler — the name LilyPond asks for, and the
/// one the generators resolve (audit/scripts/Extract-Emmentaler*.py).</param>
/// <param name="EmmentalerCode">The character Emmentaler draws it with — today's private-use slot,
/// or ASCII for the fetaText digits and dynamic letters (<see cref="EmmentalerGlyphs"/>).</param>
/// <param name="Unverified">True while the SMuFL name is a reading of the descriptions and not yet
/// a drawing compared by eye (docs/smufl-design.md §4). A SMuFL font is not drawn through such a
/// row until it is settled.</param>
internal readonly record struct MusicGlyphEntry(
    MusicGlyph Glyph, string SmuflName, int SmuflCodepoint, string FetaName, char EmmentalerCode,
    bool Unverified = false);

/// <summary>The <see cref="MusicGlyph"/> table — the one home of the feta ↔ SMuFL mapping.</summary>
/// <remarks>
/// SMuFL names and code points checked against the SMuFL <c>glyphnames.json</c>
/// (w3c-cg/smufl gh-pages, read 2026-10-08) by Lab <c>sessions/p857/check-glyphnames.ps1</c>.
/// The Emmentaler column REFERS to <see cref="EmmentalerGlyphs"/>, so the generated code points
/// stay the single source of today's slots.
/// <para>
/// ⚠️ TWO ROWS MAY SHARE AN EMMENTALER GLYPH: <see cref="MusicGlyph.RepeatDot"/> is
/// <c>dots.dot</c>, the augmentation dot, because LilyPond draws both with it; SMuFL has two.
/// </para>
/// </remarks>
internal static class MusicGlyphs
{
    private const bool U = true; // Unverified — see MusicGlyphEntry.Unverified

    /// <summary>Every row, one per <see cref="MusicGlyph"/> member.</summary>
    public static readonly ImmutableArray<MusicGlyphEntry> Table =
    [
        // ---- clefs ----
        new(MusicGlyph.GClef, "gClef", 0xE050, "clefs.G", EmmentalerGlyphs.GClef),
        new(MusicGlyph.GClefChange, "gClefChange", 0xE07A, "clefs.G_change", EmmentalerGlyphs.GClefChange),
        new(MusicGlyph.FClef, "fClef", 0xE062, "clefs.F", EmmentalerGlyphs.FClef),
        new(MusicGlyph.FClefChange, "fClefChange", 0xE07C, "clefs.F_change", EmmentalerGlyphs.FClefChange),
        new(MusicGlyph.CClef, "cClef", 0xE05C, "clefs.C", EmmentalerGlyphs.CClef),
        new(MusicGlyph.CClefChange, "cClefChange", 0xE07B, "clefs.C_change", EmmentalerGlyphs.CClefChange),
        new(MusicGlyph.UnpitchedPercussionClef1, "unpitchedPercussionClef1", 0xE069, "clefs.percussion", EmmentalerGlyphs.PercussionClef),
        new(MusicGlyph.FetaClefsPercussionChange, "feta.clefs.percussion_change", 0, "clefs.percussion_change", EmmentalerGlyphs.PercussionClefChange),
        new(MusicGlyph.SixStringTabClef, "6stringTabClef", 0xE06D, "clefs.tab", EmmentalerGlyphs.TabClef, U),
        new(MusicGlyph.FetaClefsTabChange, "feta.clefs.tab_change", 0, "clefs.tab_change", EmmentalerGlyphs.TabClefChange),

        // ---- noteheads ----
        new(MusicGlyph.NoteheadDoubleWhole, "noteheadDoubleWhole", 0xE0A0, "noteheads.sM1", EmmentalerGlyphs.NoteheadDoubleWhole),
        new(MusicGlyph.NoteheadWhole, "noteheadWhole", 0xE0A2, "noteheads.s0", EmmentalerGlyphs.NoteheadWhole),
        new(MusicGlyph.NoteheadHalf, "noteheadHalf", 0xE0A3, "noteheads.s1", EmmentalerGlyphs.NoteheadHalf),
        new(MusicGlyph.NoteheadBlack, "noteheadBlack", 0xE0A4, "noteheads.s2", EmmentalerGlyphs.NoteheadBlack),
        new(MusicGlyph.NoteheadDiamondWhole, "noteheadDiamondWhole", 0xE0D8, "noteheads.s0diamond", EmmentalerGlyphs.NoteheadDiamondWhole, U),
        new(MusicGlyph.NoteheadDiamondHalf, "noteheadDiamondHalf", 0xE0D9, "noteheads.s1diamond", EmmentalerGlyphs.NoteheadDiamondHalf, U),
        new(MusicGlyph.NoteheadDiamondBlack, "noteheadDiamondBlack", 0xE0DB, "noteheads.s2diamond", EmmentalerGlyphs.NoteheadDiamondBlack, U),
        new(MusicGlyph.NoteheadTriangleUpWhole, "noteheadTriangleUpWhole", 0xE0BB, "noteheads.s0triangle", EmmentalerGlyphs.NoteheadTriangleWhole, U),
        new(MusicGlyph.NoteheadTriangleUpHalf, "noteheadTriangleUpHalf", 0xE0BC, "noteheads.s1triangle", EmmentalerGlyphs.NoteheadTriangleHalf, U),
        new(MusicGlyph.NoteheadTriangleUpBlack, "noteheadTriangleUpBlack", 0xE0BE, "noteheads.s2triangle", EmmentalerGlyphs.NoteheadTriangleBlack, U),
        new(MusicGlyph.NoteheadSlashWhiteWhole, "noteheadSlashWhiteWhole", 0xE102, "noteheads.s0slash", EmmentalerGlyphs.NoteheadSlashWhole, U),
        new(MusicGlyph.NoteheadSlashWhiteHalf, "noteheadSlashWhiteHalf", 0xE103, "noteheads.s1slash", EmmentalerGlyphs.NoteheadSlashHalf, U),
        new(MusicGlyph.NoteheadSlashHorizontalEnds, "noteheadSlashHorizontalEnds", 0xE101, "noteheads.s2slash", EmmentalerGlyphs.NoteheadSlashBlack, U),
        new(MusicGlyph.NoteheadXWhole, "noteheadXWhole", 0xE0A7, "noteheads.s0cross", EmmentalerGlyphs.NoteheadCrossWhole),
        new(MusicGlyph.NoteheadXHalf, "noteheadXHalf", 0xE0A8, "noteheads.s1cross", EmmentalerGlyphs.NoteheadCrossHalf),
        new(MusicGlyph.NoteheadXBlack, "noteheadXBlack", 0xE0A9, "noteheads.s2cross", EmmentalerGlyphs.NoteheadCrossBlack),
        new(MusicGlyph.NoteheadCircleX, "noteheadCircleX", 0xE0B3, "noteheads.s2xcircle", EmmentalerGlyphs.NoteheadXCircle),

        // ---- rests ----
        new(MusicGlyph.RestMaxima, "restMaxima", 0xE4E0, "rests.M3", EmmentalerGlyphs.RestMaxima),
        new(MusicGlyph.RestLonga, "restLonga", 0xE4E1, "rests.M2", EmmentalerGlyphs.RestLonga),
        new(MusicGlyph.RestDoubleWhole, "restDoubleWhole", 0xE4E2, "rests.M1", EmmentalerGlyphs.RestDoubleWhole),
        new(MusicGlyph.RestDoubleWholeLegerLine, "restDoubleWholeLegerLine", 0xE4F3, "rests.M1o", EmmentalerGlyphs.RestDoubleWholeLedgered),
        new(MusicGlyph.RestWhole, "restWhole", 0xE4E3, "rests.0", EmmentalerGlyphs.RestWhole),
        new(MusicGlyph.RestWholeLegerLine, "restWholeLegerLine", 0xE4F4, "rests.0o", EmmentalerGlyphs.RestWholeLedgered),
        new(MusicGlyph.RestHalf, "restHalf", 0xE4E4, "rests.1", EmmentalerGlyphs.RestHalf),
        new(MusicGlyph.RestHalfLegerLine, "restHalfLegerLine", 0xE4F5, "rests.1o", EmmentalerGlyphs.RestHalfLedgered),
        new(MusicGlyph.RestQuarter, "restQuarter", 0xE4E5, "rests.2", EmmentalerGlyphs.RestQuarter),
        new(MusicGlyph.Rest8th, "rest8th", 0xE4E6, "rests.3", EmmentalerGlyphs.Rest8th),
        new(MusicGlyph.Rest16th, "rest16th", 0xE4E7, "rests.4", EmmentalerGlyphs.Rest16th),
        new(MusicGlyph.Rest32nd, "rest32nd", 0xE4E8, "rests.5", EmmentalerGlyphs.Rest32nd),
        new(MusicGlyph.Rest64th, "rest64th", 0xE4E9, "rests.6", EmmentalerGlyphs.Rest64th),
        new(MusicGlyph.Rest128th, "rest128th", 0xE4EA, "rests.7", EmmentalerGlyphs.Rest128th),

        // ---- accidentals ----
        new(MusicGlyph.AccidentalFlat, "accidentalFlat", 0xE260, "accidentals.flat", EmmentalerGlyphs.AccidentalFlat),
        new(MusicGlyph.AccidentalNatural, "accidentalNatural", 0xE261, "accidentals.natural", EmmentalerGlyphs.AccidentalNatural),
        new(MusicGlyph.AccidentalSharp, "accidentalSharp", 0xE262, "accidentals.sharp", EmmentalerGlyphs.AccidentalSharp),
        new(MusicGlyph.AccidentalDoubleSharp, "accidentalDoubleSharp", 0xE263, "accidentals.doublesharp", EmmentalerGlyphs.AccidentalDoubleSharp),
        new(MusicGlyph.AccidentalDoubleFlat, "accidentalDoubleFlat", 0xE264, "accidentals.flatflat", EmmentalerGlyphs.AccidentalDoubleFlat),
        new(MusicGlyph.AccidentalQuarterToneSharpStein, "accidentalQuarterToneSharpStein", 0xE282, "accidentals.sharp.slashslash.stem", EmmentalerGlyphs.AccidentalQuarterSharp),
        new(MusicGlyph.AccidentalThreeQuarterTonesSharpStein, "accidentalThreeQuarterTonesSharpStein", 0xE283, "accidentals.sharp.slashslashslash.stemstem", EmmentalerGlyphs.AccidentalThreeQuarterSharp, U),
        new(MusicGlyph.FetaAccidentalsFlatSlash, "feta.accidentals.flat.slash", 0, "accidentals.flat.slash", EmmentalerGlyphs.AccidentalQuarterFlat),
        new(MusicGlyph.FetaAccidentalsFlatFlatSlash, "feta.accidentals.flatflat.slash", 0, "accidentals.flatflat.slash", EmmentalerGlyphs.AccidentalThreeQuarterFlat),
        new(MusicGlyph.AccidentalParensLeft, "accidentalParensLeft", 0xE26A, "accidentals.leftparen", EmmentalerGlyphs.AccidentalLeftParen),
        new(MusicGlyph.AccidentalParensRight, "accidentalParensRight", 0xE26B, "accidentals.rightparen", EmmentalerGlyphs.AccidentalRightParen),

        // ---- flags ----
        new(MusicGlyph.Flag8thUp, "flag8thUp", 0xE240, "flags.u3", EmmentalerGlyphs.Flag8thUp),
        new(MusicGlyph.Flag8thDown, "flag8thDown", 0xE241, "flags.d3", EmmentalerGlyphs.Flag8thDown),
        new(MusicGlyph.Flag16thUp, "flag16thUp", 0xE242, "flags.u4", EmmentalerGlyphs.Flag16thUp),
        new(MusicGlyph.Flag16thDown, "flag16thDown", 0xE243, "flags.d4", EmmentalerGlyphs.Flag16thDown),
        new(MusicGlyph.Flag32ndUp, "flag32ndUp", 0xE244, "flags.u5", EmmentalerGlyphs.Flag32ndUp),
        new(MusicGlyph.Flag32ndDown, "flag32ndDown", 0xE245, "flags.d5", EmmentalerGlyphs.Flag32ndDown),
        new(MusicGlyph.Flag64thUp, "flag64thUp", 0xE246, "flags.u6", EmmentalerGlyphs.Flag64thUp),
        new(MusicGlyph.Flag64thDown, "flag64thDown", 0xE247, "flags.d6", EmmentalerGlyphs.Flag64thDown),
        new(MusicGlyph.Flag128thUp, "flag128thUp", 0xE248, "flags.u7", EmmentalerGlyphs.Flag128thUp),
        new(MusicGlyph.Flag128thDown, "flag128thDown", 0xE249, "flags.d7", EmmentalerGlyphs.Flag128thDown),

        // ---- dots ----
        new(MusicGlyph.AugmentationDot, "augmentationDot", 0xE1E7, "dots.dot", EmmentalerGlyphs.AugmentationDot),
        new(MusicGlyph.RepeatDot, "repeatDot", 0xE044, "dots.dot", EmmentalerGlyphs.RepeatDots),

        // ---- time signatures (Emmentaler: the plain fetaText digits, drawn by ASCII) ----
        new(MusicGlyph.TimeSig0, "timeSig0", 0xE080, "zero", EmmentalerGlyphs.TimeSig0),
        new(MusicGlyph.TimeSig1, "timeSig1", 0xE081, "one", EmmentalerGlyphs.TimeSig1),
        new(MusicGlyph.TimeSig2, "timeSig2", 0xE082, "two", EmmentalerGlyphs.TimeSig2),
        new(MusicGlyph.TimeSig3, "timeSig3", 0xE083, "three", EmmentalerGlyphs.TimeSig3),
        new(MusicGlyph.TimeSig4, "timeSig4", 0xE084, "four", EmmentalerGlyphs.TimeSig4),
        new(MusicGlyph.TimeSig5, "timeSig5", 0xE085, "five", EmmentalerGlyphs.TimeSig5),
        new(MusicGlyph.TimeSig6, "timeSig6", 0xE086, "six", EmmentalerGlyphs.TimeSig6),
        new(MusicGlyph.TimeSig7, "timeSig7", 0xE087, "seven", EmmentalerGlyphs.TimeSig7),
        new(MusicGlyph.TimeSig8, "timeSig8", 0xE088, "eight", EmmentalerGlyphs.TimeSig8),
        new(MusicGlyph.TimeSig9, "timeSig9", 0xE089, "nine", EmmentalerGlyphs.TimeSig9),
        new(MusicGlyph.TimeSigCommon, "timeSigCommon", 0xE08A, "timesig.C44", EmmentalerGlyphs.TimeSigCommon),
        new(MusicGlyph.TimeSigCutCommon, "timeSigCutCommon", 0xE08B, "timesig.C22", EmmentalerGlyphs.TimeSigCutCommon),

        // ---- fingering (Emmentaler: the fattened cut, cv47 picking .alt for 4 and 7) ----
        new(MusicGlyph.Fingering0, "fingering0", 0xED10, "fattened.zero", EmmentalerGlyphs.FingeringDigit0),
        new(MusicGlyph.Fingering1, "fingering1", 0xED11, "fattened.one", EmmentalerGlyphs.FingeringDigit1),
        new(MusicGlyph.Fingering2, "fingering2", 0xED12, "fattened.two", EmmentalerGlyphs.FingeringDigit2),
        new(MusicGlyph.Fingering3, "fingering3", 0xED13, "fattened.three", EmmentalerGlyphs.FingeringDigit3),
        new(MusicGlyph.Fingering4, "fingering4", 0xED14, "fattened.four.alt", EmmentalerGlyphs.FingeringDigit4),
        new(MusicGlyph.Fingering5, "fingering5", 0xED15, "fattened.five", EmmentalerGlyphs.FingeringDigit5),
        new(MusicGlyph.Fingering6, "fingering6", 0xED24, "fattened.six", EmmentalerGlyphs.FingeringDigit6),
        new(MusicGlyph.Fingering7, "fingering7", 0xED25, "fattened.seven.alt", EmmentalerGlyphs.FingeringDigit7),
        new(MusicGlyph.Fingering8, "fingering8", 0xED26, "fattened.eight", EmmentalerGlyphs.FingeringDigit8),
        new(MusicGlyph.Fingering9, "fingering9", 0xED27, "fattened.nine", EmmentalerGlyphs.FingeringDigit9),

        // ---- figured bass (Emmentaler: the fixed-width fattened cut, tnum + cv47) ----
        new(MusicGlyph.Figbass0, "figbass0", 0xEA50, "fattened.fixedwidth.zero", EmmentalerGlyphs.FigBassDigit0),
        new(MusicGlyph.Figbass1, "figbass1", 0xEA51, "fattened.fixedwidth.one", EmmentalerGlyphs.FigBassDigit1),
        new(MusicGlyph.Figbass2, "figbass2", 0xEA52, "fattened.fixedwidth.two", EmmentalerGlyphs.FigBassDigit2),
        new(MusicGlyph.Figbass3, "figbass3", 0xEA54, "fattened.fixedwidth.three", EmmentalerGlyphs.FigBassDigit3),
        new(MusicGlyph.Figbass4, "figbass4", 0xEA55, "fattened.fixedwidth.four.alt", EmmentalerGlyphs.FigBassDigit4),
        new(MusicGlyph.Figbass5, "figbass5", 0xEA57, "fattened.fixedwidth.five", EmmentalerGlyphs.FigBassDigit5),
        new(MusicGlyph.Figbass6, "figbass6", 0xEA5B, "fattened.fixedwidth.six", EmmentalerGlyphs.FigBassDigit6),
        new(MusicGlyph.Figbass7, "figbass7", 0xEA5D, "fattened.fixedwidth.seven.alt", EmmentalerGlyphs.FigBassDigit7),
        new(MusicGlyph.Figbass8, "figbass8", 0xEA60, "fattened.fixedwidth.eight", EmmentalerGlyphs.FigBassDigit8),
        new(MusicGlyph.Figbass9, "figbass9", 0xEA61, "fattened.fixedwidth.nine", EmmentalerGlyphs.FigBassDigit9),
        new(MusicGlyph.FigbassFlat, "figbassFlat", 0xEA64, "accidentals.flat.figbass", EmmentalerGlyphs.FigBassFlat),
        new(MusicGlyph.FigbassNatural, "figbassNatural", 0xEA65, "accidentals.natural.figbass", EmmentalerGlyphs.FigBassNatural),
        new(MusicGlyph.FigbassSharp, "figbassSharp", 0xEA66, "accidentals.sharp.figbass", EmmentalerGlyphs.FigBassSharp),

        // ---- articulations and other scripts ----
        new(MusicGlyph.FermataAbove, "fermataAbove", 0xE4C0, "scripts.ufermata", EmmentalerGlyphs.FermataAbove),
        new(MusicGlyph.FermataBelow, "fermataBelow", 0xE4C1, "scripts.dfermata", EmmentalerGlyphs.FermataBelow),
        new(MusicGlyph.FermataShortAbove, "fermataShortAbove", 0xE4C4, "scripts.ushortfermata", EmmentalerGlyphs.FermataShortAbove),
        new(MusicGlyph.FermataShortBelow, "fermataShortBelow", 0xE4C5, "scripts.dshortfermata", EmmentalerGlyphs.FermataShortBelow),
        new(MusicGlyph.FermataLongAbove, "fermataLongAbove", 0xE4C6, "scripts.ulongfermata", EmmentalerGlyphs.FermataLongAbove),
        new(MusicGlyph.FermataLongBelow, "fermataLongBelow", 0xE4C7, "scripts.dlongfermata", EmmentalerGlyphs.FermataLongBelow),
        // LilyPond draws sforzato / staccato / tenuto with ONE glyph in both directions; SMuFL
        // has an Above/Below pair. The Above row is the Emmentaler glyph.
        new(MusicGlyph.ArticAccentAbove, "articAccentAbove", 0xE4A0, "scripts.sforzato", EmmentalerGlyphs.ArticAccentAbove),
        new(MusicGlyph.ArticStaccatoAbove, "articStaccatoAbove", 0xE4A2, "scripts.staccato", EmmentalerGlyphs.ArticStaccatoAbove),
        new(MusicGlyph.ArticTenutoAbove, "articTenutoAbove", 0xE4A4, "scripts.tenuto", EmmentalerGlyphs.ArticTenutoAbove),
        new(MusicGlyph.ArticTenutoStaccatoAbove, "articTenutoStaccatoAbove", 0xE4B2, "scripts.uportato", EmmentalerGlyphs.ArticPortatoAbove, U),
        new(MusicGlyph.ArticTenutoStaccatoBelow, "articTenutoStaccatoBelow", 0xE4B3, "scripts.dportato", EmmentalerGlyphs.ArticPortatoBelow, U),
        new(MusicGlyph.ArticStaccatissimoAbove, "articStaccatissimoAbove", 0xE4A6, "scripts.ustaccatissimo", EmmentalerGlyphs.ArticStaccatissimoAbove, U),
        new(MusicGlyph.ArticStaccatissimoBelow, "articStaccatissimoBelow", 0xE4A7, "scripts.dstaccatissimo", EmmentalerGlyphs.ArticStaccatissimoBelow, U),
        new(MusicGlyph.ArticMarcatoAbove, "articMarcatoAbove", 0xE4AC, "scripts.umarcato", EmmentalerGlyphs.ArticMarcatoAbove),
        new(MusicGlyph.ArticMarcatoBelow, "articMarcatoBelow", 0xE4AD, "scripts.dmarcato", EmmentalerGlyphs.ArticMarcatoBelow),
        new(MusicGlyph.StringsUpBow, "stringsUpBow", 0xE612, "scripts.uupbow", EmmentalerGlyphs.ArticUpBowAbove),
        new(MusicGlyph.StringsUpBowTurned, "stringsUpBowTurned", 0xE613, "scripts.dupbow", EmmentalerGlyphs.ArticUpBowBelow, U),
        new(MusicGlyph.StringsDownBow, "stringsDownBow", 0xE610, "scripts.udownbow", EmmentalerGlyphs.ArticDownBowAbove),
        new(MusicGlyph.StringsDownBowTurned, "stringsDownBowTurned", 0xE611, "scripts.ddownbow", EmmentalerGlyphs.ArticDownBowBelow, U),
        new(MusicGlyph.StringsHarmonic, "stringsHarmonic", 0xE614, "scripts.flageolet", EmmentalerGlyphs.ArticFlageolet),
        new(MusicGlyph.StringsThumbPosition, "stringsThumbPosition", 0xE624, "scripts.thumb", EmmentalerGlyphs.ArticThumb),
        new(MusicGlyph.BrassMuteClosed, "brassMuteClosed", 0xE5E5, "scripts.stopped", EmmentalerGlyphs.ArticStopped, U),
        new(MusicGlyph.PluckedSnapPizzicatoAbove, "pluckedSnapPizzicatoAbove", 0xE631, "scripts.snappizzicato", EmmentalerGlyphs.ScriptSnappizzicato, U),
        new(MusicGlyph.KeyboardPedalHeel1, "keyboardPedalHeel1", 0xE661, "scripts.upedalheel", EmmentalerGlyphs.PedalHeelUp, U),
        new(MusicGlyph.KeyboardPedalHeel2, "keyboardPedalHeel2", 0xE662, "scripts.dpedalheel", EmmentalerGlyphs.PedalHeelDown, U),
        new(MusicGlyph.KeyboardPedalToe1, "keyboardPedalToe1", 0xE664, "scripts.upedaltoe", EmmentalerGlyphs.PedalToeUp, U),
        new(MusicGlyph.KeyboardPedalToe2, "keyboardPedalToe2", 0xE665, "scripts.dpedaltoe", EmmentalerGlyphs.PedalToeDown, U),
        new(MusicGlyph.KeyboardPedalPed, "keyboardPedalPed", 0xE650, "pedal.Ped", EmmentalerGlyphs.PedalPed),
        new(MusicGlyph.KeyboardPedalDot, "keyboardPedalDot", 0xE654, "pedal..", EmmentalerGlyphs.PedalDot),
        new(MusicGlyph.KeyboardPedalUp, "keyboardPedalUp", 0xE655, "pedal.*", EmmentalerGlyphs.PedalStar),

        // ---- ornaments, repeats, breaths ----
        new(MusicGlyph.OrnamentTrill, "ornamentTrill", 0xE566, "scripts.trill", EmmentalerGlyphs.OrnTrill),
        new(MusicGlyph.WiggleTrill, "wiggleTrill", 0xEAA4, "scripts.trill_element", EmmentalerGlyphs.OrnTrillElement),
        new(MusicGlyph.OrnamentTurn, "ornamentTurn", 0xE567, "scripts.turn", EmmentalerGlyphs.OrnTurn),
        new(MusicGlyph.OrnamentTurnInverted, "ornamentTurnInverted", 0xE568, "scripts.reverseturn", EmmentalerGlyphs.OrnReverseTurn),
        new(MusicGlyph.OrnamentShortTrill, "ornamentShortTrill", 0xE56C, "scripts.prall", EmmentalerGlyphs.OrnPrall),
        new(MusicGlyph.OrnamentMordent, "ornamentMordent", 0xE56D, "scripts.mordent", EmmentalerGlyphs.OrnMordent),
        new(MusicGlyph.OrnamentTremblement, "ornamentTremblement", 0xE56E, "scripts.prallprall", EmmentalerGlyphs.OrnPrallPrall, U),
        new(MusicGlyph.WiggleArpeggiatoUp, "wiggleArpeggiatoUp", 0xEAA9, "scripts.arpeggio", EmmentalerGlyphs.Arpeggio),
        new(MusicGlyph.Segno, "segno", 0xE047, "scripts.segno", EmmentalerGlyphs.MarkSegno),
        new(MusicGlyph.Coda, "coda", 0xE048, "scripts.coda", EmmentalerGlyphs.MarkCoda),
        new(MusicGlyph.BreathMarkComma, "breathMarkComma", 0xE4CE, "scripts.rcomma", EmmentalerGlyphs.BreathComma),
        new(MusicGlyph.Caesura, "caesura", 0xE4D1, "scripts.caesura.straight", EmmentalerGlyphs.CaesuraStraight),

        // ---- system-start bracket ----
        new(MusicGlyph.BracketTop, "bracketTop", 0xE003, "brackettips.up", EmmentalerGlyphs.BracketTipUp),
        new(MusicGlyph.BracketBottom, "bracketBottom", 0xE004, "brackettips.down", EmmentalerGlyphs.BracketTipDown),

        // ---- dynamics (Emmentaler: fetaText letters, drawn by ASCII) ----
        new(MusicGlyph.DynamicPiano, "dynamicPiano", 0xE520, "p", EmmentalerGlyphs.DynamicPiano),
        new(MusicGlyph.DynamicMezzo, "dynamicMezzo", 0xE521, "m", EmmentalerGlyphs.DynamicMezzo),
        new(MusicGlyph.DynamicForte, "dynamicForte", 0xE522, "f", EmmentalerGlyphs.DynamicForte),
        new(MusicGlyph.DynamicRinforzando, "dynamicRinforzando", 0xE523, "r", EmmentalerGlyphs.DynamicRinforzando),
        new(MusicGlyph.DynamicSforzando, "dynamicSforzando", 0xE524, "s", EmmentalerGlyphs.DynamicSforzando),
        new(MusicGlyph.DynamicZ, "dynamicZ", 0xE525, "z", EmmentalerGlyphs.DynamicZ),
        new(MusicGlyph.DynamicNiente, "dynamicNiente", 0xE526, "n", 'n'),
    ];

    private static readonly MusicGlyphEntry[] ByGlyph = BuildByGlyph();
    private static readonly FrozenDictionary<string, MusicGlyph> BySmuflName =
        Table.ToFrozenDictionary(e => e.SmuflName, e => e.Glyph, StringComparer.Ordinal);

    private static MusicGlyphEntry[] BuildByGlyph()
    {
        var byGlyph = new MusicGlyphEntry[Enum.GetValues<MusicGlyph>().Length];
        foreach (var e in Table)
            byGlyph[(int) e.Glyph] = e;
        return byGlyph;
    }

    /// <summary>The row of <paramref name="glyph"/>.</summary>
    public static MusicGlyphEntry Of(MusicGlyph glyph) => ByGlyph[(int) glyph];

    /// <summary>The SMuFL name of <paramref name="glyph"/> (<c>feta.…</c> for an Emmentaler-only glyph).</summary>
    public static string SmuflName(MusicGlyph glyph) => ByGlyph[(int) glyph].SmuflName;

    /// <summary>The glyph a SMuFL name names, when this vocabulary has it.</summary>
    public static bool TryFromSmuflName(string name, out MusicGlyph glyph)
        => BySmuflName.TryGetValue(name, out glyph);

    // ===== Which glyph a grob draws — the dispatch, font-independent =====
    // Moved here from EmmentalerGlyphs (第858), whose Get* now answer Emmentaler's character
    // for the same choice.

    /// <summary>The accidental for a resolved accidental kind ("sharp", "flat",
    /// "doubleSharp", "doubleFlat", the four quarter-tone kinds); anything else (incl.
    /// "natural") is the natural sign. Single source for the name-to-glyph switch.</summary>
    public static MusicGlyph Accidental(string? kind) => kind switch
    {
        "doubleSharp" => MusicGlyph.AccidentalDoubleSharp,
        "sharp" => MusicGlyph.AccidentalSharp,
        "flat" => MusicGlyph.AccidentalFlat,
        "doubleFlat" => MusicGlyph.AccidentalDoubleFlat,
        "quarterSharp" => MusicGlyph.AccidentalQuarterToneSharpStein,
        "threeQuarterSharp" => MusicGlyph.AccidentalThreeQuarterTonesSharpStein,
        "quarterFlat" => MusicGlyph.FetaAccidentalsFlatSlash,
        "threeQuarterFlat" => MusicGlyph.FetaAccidentalsFlatFlatSlash,
        _ => MusicGlyph.AccidentalNatural,
    };

    /// <summary>The time signature digit <paramref name="digit"/> (0 for anything outside 0..9).</summary>
    public static MusicGlyph TimeSigDigit(int digit)
        => digit is >= 0 and <= 9 ? TimeSigDigits[digit] : MusicGlyph.TimeSig0;

    private static readonly MusicGlyph[] TimeSigDigits =
    [
        MusicGlyph.TimeSig0, MusicGlyph.TimeSig1, MusicGlyph.TimeSig2, MusicGlyph.TimeSig3, MusicGlyph.TimeSig4,
        MusicGlyph.TimeSig5, MusicGlyph.TimeSig6, MusicGlyph.TimeSig7, MusicGlyph.TimeSig8, MusicGlyph.TimeSig9,
    ];

    /// <summary>The rest for a note value at a staff position.</summary>
    /// <param name="noteValue">1 = whole, 2 = half, 0 = breve, 4/8/… shorter.</param>
    /// <param name="staffPosition">Where the rest's ORIGIN was drawn, in staff
    /// positions about the middle line (LilyPond's <c>get_position</c> — the whole
    /// rest's +2 already applied, since it is the origin that hangs from that line).</param>
    /// <param name="staffLines">The staff's line count — the lines the ledger question is
    /// asked of (<see cref="EngravingDefaults.StaffLinePositions"/>); five by default.</param>
    /// <remarks>
    /// LILYPOND-REF: lily/rest.cc:166-227 Rest::glyph_name — "rests." + duration-log,
    /// plus an "o" suffix for the LEDGERED cut of the glyph. A breve, whole or half
    /// rest OFF a staff line carries its own ledger line inside the glyph (there is no
    /// LedgerLineSpanner for rests), so the half rest LilyPond pushes to an odd position
    /// out of the staff prints as <c>rests.1o</c>, not <c>rests.1</c>.
    /// LILYPOND-REF: lily/staff-symbol.cc:372-396 Staff_symbol::on_line — with
    /// <c>allow_ledger</c> false (that is what <c>on_staff_line</c> passes), only the
    /// REAL lines count, so every position outside the staff is off-line and ledgers.
    /// <para>⚠️ The ledger changes the INK only. LilyPond keeps it out of the X extent
    /// on purpose (rest.cc:281-289 asks for the unledgered stencil there, because the
    /// Y position that decides it is not known until after line breaking), and the Y
    /// extent it reports is the bare bar's either way (measured: an <c>rests.1o</c> at
    /// position −11 reports <c>(0 . 0.625)</c>, the same as <c>rests.1</c>). So spacing,
    /// skylines and the dot column all keep reading the unledgered box.</para>
    /// </remarks>
    public static MusicGlyph Rest(int noteValue, double staffPosition, int staffLines = 5)
    {
        // LILYPOND-REF: lily/rest.cc:173-174 — int (get_position (me) + offset).
        // C++ truncates toward zero; so does this cast.
        int pos = (int) staffPosition;
        return noteValue switch
        {
            0 => IsLedgered(0, pos, staffLines) ? MusicGlyph.RestDoubleWholeLegerLine : MusicGlyph.RestDoubleWhole,
            1 => IsLedgered(1, pos, staffLines) ? MusicGlyph.RestWholeLegerLine : MusicGlyph.RestWhole,
            2 => IsLedgered(2, pos, staffLines) ? MusicGlyph.RestHalfLegerLine : MusicGlyph.RestHalf,
            4 => MusicGlyph.RestQuarter, 8 => MusicGlyph.Rest8th,
            16 => MusicGlyph.Rest16th, 32 => MusicGlyph.Rest32nd, 64 => MusicGlyph.Rest64th,
            128 => MusicGlyph.Rest128th,
            _ => MusicGlyph.RestQuarter,
        };
    }

    /// <summary>
    /// Whether a rest of this note value at this staff position prints the cut of its
    /// glyph that carries a ledger line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/rest.cc:170-185 Rest::glyph_name is_ledgered — a half rest
    /// needs a ledger if it is not LYING on a staff line, a whole rest if it is not
    /// HANGING from one, a breve if neither (its own line, or the one two positions
    /// above it, being a staff line spares it).
    /// <para>The staff's <c>line-positions</c> are the ones it DRAWS
    /// (<see cref="EngravingDefaults.StaffLinePositions"/>): {−4, −2, 0, 2, 4} on the
    /// five-line staff, the single middle line on <c>as lines 1</c>, the pair ±2 on
    /// <c>as lines 2</c>. LILYPOND-REF: scm/define-grobs.scm StaffSymbol — line-count 5.</para>
    /// </remarks>
    private static bool IsLedgered(int noteValue, int pos, int staffLines) =>
        !OnStaffLine(pos, staffLines)
        && !(noteValue == 0 && OnStaffLine(pos + 2, staffLines));

    /// <summary>Whether a staff position is one of the staff's drawn lines.</summary>
    /// <remarks>LILYPOND-REF: lily/staff-symbol.cc:372-382 Staff_symbol::on_line —
    /// the position equals one of <c>line-positions</c>.</remarks>
    private static bool OnStaffLine(int pos, int staffLines)
        => EngravingDefaults.OnDrawnStaffLine(pos, staffLines);

    /// <summary>The notehead for a style and note value; the whole-note variants
    /// serve the breve too (styled breves are not in the font).</summary>
    public static MusicGlyph Notehead(Model.NoteheadStyle style, int noteValue) => style switch
    {
        Model.NoteheadStyle.Cross => noteValue switch
        {
            0 or 1 => MusicGlyph.NoteheadXWhole, 2 => MusicGlyph.NoteheadXHalf, _ => MusicGlyph.NoteheadXBlack
        },
        Model.NoteheadStyle.Diamond => noteValue switch
        {
            0 or 1 => MusicGlyph.NoteheadDiamondWhole, 2 => MusicGlyph.NoteheadDiamondHalf, _ => MusicGlyph.NoteheadDiamondBlack
        },
        Model.NoteheadStyle.Triangle => noteValue switch
        {
            0 or 1 => MusicGlyph.NoteheadTriangleUpWhole, 2 => MusicGlyph.NoteheadTriangleUpHalf, _ => MusicGlyph.NoteheadTriangleUpBlack
        },
        Model.NoteheadStyle.Slash => noteValue switch
        {
            0 or 1 => MusicGlyph.NoteheadSlashWhiteWhole, 2 => MusicGlyph.NoteheadSlashWhiteHalf, _ => MusicGlyph.NoteheadSlashHorizontalEnds
        },
        Model.NoteheadStyle.XCircle => MusicGlyph.NoteheadCircleX,
        _ => Notehead(noteValue),
    };

    // LILYPOND-REF: lily/note-head.cc internal_print — glyph = "noteheads.s" +
    // min(duration-log, 2) (so quarter and shorter all share the s2 filled head).
    /// <summary>The plain notehead for a note value.</summary>
    public static MusicGlyph Notehead(int noteValue) => noteValue switch
    {
        0 => MusicGlyph.NoteheadDoubleWhole, 1 => MusicGlyph.NoteheadWhole, 2 => MusicGlyph.NoteheadHalf,
        _ => MusicGlyph.NoteheadBlack,
    };

    // LILYPOND-REF: lily/flag.cc Flag::glyph_name — "flags." + (up ? 'u' : 'd') + duration-log.
    /// <summary>The flag for a note value and stem direction; null for a note without one.</summary>
    public static MusicGlyph? Flag(int noteValue, bool stemUp) => noteValue switch
    {
        8 => stemUp ? MusicGlyph.Flag8thUp : MusicGlyph.Flag8thDown,
        16 => stemUp ? MusicGlyph.Flag16thUp : MusicGlyph.Flag16thDown,
        32 => stemUp ? MusicGlyph.Flag32ndUp : MusicGlyph.Flag32ndDown,
        64 => stemUp ? MusicGlyph.Flag64thUp : MusicGlyph.Flag64thDown,
        128 => stemUp ? MusicGlyph.Flag128thUp : MusicGlyph.Flag128thDown,
        _ => null,
    };
}
