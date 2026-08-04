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

using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// An articulation mark attached to a music item.
/// </summary>
/// <remarks>
/// LILYPOND-REF: script-engraver.cc:36-61 Script_engraver class
/// LILYPOND-REF: define-grobs.scm:2268-2310 Script grob definition
/// </remarks>
public sealed record ArticulationItem
{
    /// <summary>The articulation type.</summary>
    public ArticulationType Type { get; }

    /// <summary>The measure index where this articulation appears.</summary>
    public int MeasureIndex { get; }

    /// <summary>The item index within the measure.</summary>
    public int ItemIndex { get; }

    /// <summary>Whether this articulation should be placed above the note. Init so
    /// the engraver can flip it to the beam-resolved side (a beamed note's stem, and
    /// thus a stem-coupled script's side, follows the beam, not the note's own pitch).</summary>
    public bool IsAbove { get; init; }

    /// <summary>
    /// True when the side came from an explicit <c>.up</c>/<c>.down</c> qualifier,
    /// so the engraver must honour <see cref="IsAbove"/> as-is instead of forcing a
    /// fermata/ornament/bow to its default UP side.
    /// </summary>
    public bool DirectionForced { get; init; }

    /// <summary>Guitar bend amount in SEMITONES (2 = full step); only for
    /// <see cref="ArticulationType.Bend"/>.</summary>
    public int BendSemitones { get; init; }

    /// <summary>Fret-frame spec, low string first ("x32010": x = muted,
    /// 0 = open, digit = fret); only for <see cref="ArticulationType.FretFrame"/>.</summary>
    public string? FrameSpec { get; init; }

    /// <summary>Right-hand pluck letter (p/i/m/a); only for
    /// <see cref="ArticulationType.Pluck"/>.</summary>
    public string? PluckLetter { get; init; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }

    /// <summary>Global staff index this articulation belongs to (multi-staff
    /// routing; see <c>DynamicItem.StaffIndex</c>). 0 for single-staff.</summary>
    public int StaffIndex { get; }

    /// <summary>
    /// Zero-based voice index within the staff this articulation was written in.
    /// <see cref="ItemIndex"/> counts items of THAT voice, so the note a script hangs off
    /// can only be found by going through it: a script resolved over the staff's primary
    /// voice lands on whatever note happens to share the index — in a two-voice staff, the
    /// upper voice's note, at the upper voice's pitch and (when the rhythms differ) at the
    /// upper voice's column. The sibling on the same note has carried this since the
    /// dynamics island: see <c>DynamicItem.VoiceIndex</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:359,414-416 Script_engraver — the <c>\name Voice</c>
    ///   context consists it, so a script is a grob of its own voice's context, and
    ///   lily/script-engraver.cc:234-250 acknowledge_rhythmic_head takes the heads of THAT
    ///   context (:410 is where the same file consists Dynamic_align_engraver).
    /// Measured: audit/lp-geometry <c>script.{staccato,marcato}-below.staff-to-ink-top</c>
    /// (LilyPond reads one number for both glyphs; a primary-voice anchor spread them 0.9).
    /// </remarks>
    public int VoiceIndex { get; init; }

    /// <summary>Creates an articulation mark of the given type.</summary>
    public ArticulationItem(ArticulationType type, int measureIndex, int itemIndex,
        bool isAbove, int sourcePosition, int staffIndex = 0)
    {
        Type = type;
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        IsAbove = isAbove;
        SourcePosition = sourcePosition;
        StaffIndex = staffIndex;
    }

    /// <summary>
    /// Gets the Emmentaler font glyph for this articulation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: feta-scripts.mf - articulation glyph definitions
    /// Codepoints verified against emmentaler-20.woff2 cmap table.
    /// </remarks>
    public string GetGlyph() => Type switch
    {
        ArticulationType.Staccato => EmmentalerGlyphs.ArticStaccatoAbove.ToString(),
        ArticulationType.Accent => EmmentalerGlyphs.ArticAccentAbove.ToString(),
        ArticulationType.Tenuto => EmmentalerGlyphs.ArticTenutoAbove.ToString(),
        ArticulationType.Marcato => (IsAbove ? EmmentalerGlyphs.ArticMarcatoAbove : EmmentalerGlyphs.ArticMarcatoBelow).ToString(),
        ArticulationType.Fermata => (IsAbove ? EmmentalerGlyphs.FermataAbove : EmmentalerGlyphs.FermataBelow).ToString(),
        ArticulationType.FermataShort => (IsAbove ? EmmentalerGlyphs.FermataShortAbove : EmmentalerGlyphs.FermataShortBelow).ToString(),
        ArticulationType.FermataLong => (IsAbove ? EmmentalerGlyphs.FermataLongAbove : EmmentalerGlyphs.FermataLongBelow).ToString(),
        // The staccato DOT sits adjacent to the notehead, the tenuto LINE outside it
        // (LILYPOND-REF: LilyPond portato output — dot nearest the head). Emmentaler's
        // uportato (E04E) has the dot on the baseline-far side and dportato (E04F) the
        // near side, so to get a dot-adjacent mark the ABOVE case wants dportato and the
        // BELOW case uportato — the opposite of the naive name. Paired with the flipped
        // Portato box in ArticulationEngraver.GetGlyphBBox.
        ArticulationType.Portato => (IsAbove ? EmmentalerGlyphs.ArticPortatoBelow : EmmentalerGlyphs.ArticPortatoAbove).ToString(),
        ArticulationType.Staccatissimo => (IsAbove ? EmmentalerGlyphs.ArticStaccatissimoAbove : EmmentalerGlyphs.ArticStaccatissimoBelow).ToString(),
        // LilyPond 2.26.0 gives the bowing marks a direction pair — scm/script.scm:453
        // (dupbow . uupbow) and :88 (ddownbow . udownbow) — where 2.24.4 drew the one
        // glyph both ways.
        ArticulationType.UpBow => (IsAbove ? EmmentalerGlyphs.ArticUpBowAbove : EmmentalerGlyphs.ArticUpBowBelow).ToString(),
        ArticulationType.DownBow => (IsAbove ? EmmentalerGlyphs.ArticDownBowAbove : EmmentalerGlyphs.ArticDownBowBelow).ToString(),
        ArticulationType.Flageolet => EmmentalerGlyphs.ArticFlageolet.ToString(),
        // Fall/Doit carry no font glyph — the layout emits a bend sentinel that the
        // renderer draws as a trailing curve (see ArticulationEngraver), so GetGlyph
        // is never consulted for them.
        ArticulationType.Fall => "",
        ArticulationType.Bend => "",
        // TAB technique letters + Bartók pizz: layout sentinels, custom-drawn.
        ArticulationType.HammerOn => "tabtech:H",
        ArticulationType.PullOff => "tabtech:P",
        ArticulationType.Tap => "tabtech:T",
        ArticulationType.Stopped => EmmentalerGlyphs.ArticStopped.ToString(),
        ArticulationType.Thumb => EmmentalerGlyphs.ArticThumb.ToString(),
        ArticulationType.Heel => (IsAbove ? EmmentalerGlyphs.PedalHeelUp : EmmentalerGlyphs.PedalHeelDown).ToString(),
        ArticulationType.Toe => (IsAbove ? EmmentalerGlyphs.PedalToeUp : EmmentalerGlyphs.PedalToeDown).ToString(),
        ArticulationType.Pluck => $"tabtech:{PluckLetter}",
        ArticulationType.Scoop => "",
        ArticulationType.Plop => "",
        ArticulationType.SnapPizz => "snappizz",
        ArticulationType.FretFrame => $"frame:{FrameSpec}",
        ArticulationType.Doit => "",
        ArticulationType.Trill => EmmentalerGlyphs.OrnTrill.ToString(),
        ArticulationType.Mordent => EmmentalerGlyphs.OrnMordent.ToString(),
        ArticulationType.Prall => EmmentalerGlyphs.OrnPrall.ToString(),
        ArticulationType.Turn => EmmentalerGlyphs.OrnTurn.ToString(),
        ArticulationType.InvertedTurn => EmmentalerGlyphs.OrnReverseTurn.ToString(),
        ArticulationType.PrallTriller => EmmentalerGlyphs.OrnPrallPrall.ToString(),
        ArticulationType.Breath => EmmentalerGlyphs.BreathComma.ToString(),
        ArticulationType.Caesura => EmmentalerGlyphs.CaesuraStraight.ToString(),
        ArticulationType.EditorialSharp => EmmentalerGlyphs.AccidentalSharp.ToString(),
        ArticulationType.EditorialFlat => EmmentalerGlyphs.AccidentalFlat.ToString(),
        ArticulationType.EditorialNatural => EmmentalerGlyphs.AccidentalNatural.ToString(),
        ArticulationType.EditorialDoubleSharp => EmmentalerGlyphs.AccidentalDoubleSharp.ToString(),
        ArticulationType.EditorialDoubleFlat => EmmentalerGlyphs.AccidentalDoubleFlat.ToString(),
        _ => ""
    };

    /// <summary>
    /// Whether this is an ornament (always placed above the note).
    /// </summary>
    public bool IsOrnament => Type is ArticulationType.Trill or ArticulationType.Mordent or
        ArticulationType.Prall or ArticulationType.Turn or ArticulationType.InvertedTurn or
        ArticulationType.PrallTriller;

    /// <summary>
    /// Whether this is an editorial (suggestion) accidental — always above the
    /// note, drawn at the reduced AccidentalSuggestion size.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:96-123 AccidentalSuggestion —
    /// (direction . UP), (font-size . -2).
    /// </remarks>
    public bool IsEditorialAccidental => Type is ArticulationType.EditorialSharp
        or ArticulationType.EditorialFlat or ArticulationType.EditorialNatural
        or ArticulationType.EditorialDoubleSharp or ArticulationType.EditorialDoubleFlat;

    /// <summary>
    /// Maps a resolved accidental kind ("sharp", "flat", ...) to its editorial
    /// articulation type.
    /// </summary>
    public static ArticulationType EditorialTypeFor(string accidentalKind) => accidentalKind switch
    {
        "sharp" => ArticulationType.EditorialSharp,
        "flat" => ArticulationType.EditorialFlat,
        "doubleSharp" => ArticulationType.EditorialDoubleSharp,
        "doubleFlat" => ArticulationType.EditorialDoubleFlat,
        _ => ArticulationType.EditorialNatural
    };

    /// <summary>
    /// The accidental kind string for an editorial type (for BBox lookups).
    /// </summary>
    public static string AccidentalKindFor(ArticulationType type) => type switch
    {
        ArticulationType.EditorialSharp => "sharp",
        ArticulationType.EditorialFlat => "flat",
        ArticulationType.EditorialDoubleSharp => "doubleSharp",
        ArticulationType.EditorialDoubleFlat => "doubleFlat",
        _ => "natural"
    };
}
