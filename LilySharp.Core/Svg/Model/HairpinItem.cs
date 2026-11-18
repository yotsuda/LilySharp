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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Direction of a hairpin (crescendo or decrescendo).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:140-142 grow_dir
/// </remarks>
public enum HairpinDirection
{
    /// <summary>Crescendo: opening wedge (grows louder).</summary>
    Crescendo,
    /// <summary>Decrescendo: closing wedge (grows softer).</summary>
    Decrescendo
}

/// <summary>
/// Represents a hairpin (crescendo/decrescendo wedge) spanning multiple notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:110-358
/// LILYPOND-REF: scm/define-grobs.scm:1641-1666 Hairpin grob
/// </remarks>
public sealed record HairpinItem(
    // Crescendo or decrescendo.
    HairpinDirection Direction,
    // Measure index of the start note.
    int StartMeasureIndex,
    // Item index within the start measure.
    int StartItemIndex,
    // Measure index of the end note.
    int EndMeasureIndex,
    // Item index within the end measure.
    int EndItemIndex,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: index of the originating cresc/decresc mark in score.MusicMarks,
    // so a reused layout re-derives data-pos from the live score. -1 = unresolved.
    int SourceIndex = -1,
    // The staff this hairpin belongs to (0 = first/only staff).
    int StaffIndex = 0
)
{
    /// <summary>
    /// The hairpin stands above its staff: its start's <see cref="MusicMarkItem.HairpinAbove"/>.
    /// </summary>
    public bool IsAbove { get; init; }

    /// <summary>
    /// The voice of its staff the hairpin was written in (its start's
    /// <see cref="MusicMarkItem.VoiceIndex"/>): <see cref="StartItemIndex"/> counts that voice's
    /// items, the wedge's bounds are that voice's columns and it sides off that voice's heads
    /// and stems.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:359-410 Dynamic_engraver and Dynamic_align_engraver are consisted into the Voice context,
    ///   so a hairpin's bounds and its line's supports are its own voice's:
    /// LILYPOND-REF: lily/dynamic-engraver.cc:229-250 Dynamic_engraver::acknowledge_note_column — the spanner's bounds
    /// LILYPOND-REF: lily/dynamic-align-engraver.cc:107-118 Dynamic_align_engraver::acknowledge_rhythmic_head and acknowledge_stem — the supports
    /// </remarks>
    public int VoiceIndex { get; init; }

    /// <summary>
    /// The voice <see cref="EndItemIndex"/> counts in: <see cref="VoiceIndex"/>, unless the
    /// hairpin found no end in its own voice and was ended by another voice of its staff
    /// (<c>HairpinEngraver.DetectHairpins</c>, the whole-staff search).
    /// </summary>
    public int EndVoiceIndex { get; init; }

    /// <summary>The <c>@niente</c> at the hairpin's start moment (<c>c@niente@cresc</c>), or null.</summary>
    public DynamicItem? NienteAtStart { get; init; }

    /// <summary>The <c>@niente</c> that ends the hairpin (<c>… g@niente</c>), or null.</summary>
    public DynamicItem? NienteAtEnd { get; init; }

    /// <summary>The hairpin ends at an explicit stop (<c>@!cresc</c>, LilyPond's <c>\!</c>) on
    /// the note at its end: the bound is that note's column, as for a niente.</summary>
    public bool EndsAtStop { get; init; }

    /// <summary>
    /// The hairpin ends where ANOTHER voice's hairpin begins — its own voice writes nothing
    /// after it, so the search went over the staff (<c>HairpinEngraver.DetectHairpins</c>). It
    /// is drawn as a hairpin stopped at that moment (<see cref="EndsAtStop"/>'s picture, on its
    /// own voice's note there when it has one), and the two are NOT back to back
    /// (<see cref="AdjacentAtEnd"/>): each is on its own voice's line.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: the pairing (LilyPond pairs a hairpin in its own voice and leaves this one
    ///   unterminated, drawing none); the picture is LilyPond's for the same hairpin with
    ///   <c>\!</c> written at that moment.
    ///   departs from: lily/dynamic-engraver.cc:197-211 Dynamic_engraver::finalize.
    ///   observed by: HairpinVoiceTests (a hairpin met in another voice).
    /// LILYPOND-REF: lily/dynamic-engraver.cc:155-165 process_music — adjacent-spanners are the
    ///   hairpin ending and the one starting in ONE Dynamic_engraver, which stands in a Voice.
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1306/hp, the twin with <c>\!</c> by hand):
    ///   t4 <c>&lt;&lt; { c''8 c'' c''\&lt; c'' c''2\! } \\ { c'2 c'2\&gt; } &gt;&gt;</c> — the crescendo
    ///   runs 22.079..28.565, to the right edge of its c''2 (27.138..28.515), and the
    ///   decrescendo opens at 27.088, its own column's left, one line lower (−8.28 under −6.54).
    ///   Until session 1306 the two were drawn back to back, 27.49 and 28.16, as one voice's
    ///   pair is (s4: 27.543 and 28.110) — on two lines of different heights. A condensed
    ///   staff's two parts were too, the one's wedge ABOVE the staff and the other's below.
    /// </remarks>
    public bool MetInAnotherVoice { get; init; }

    /// <summary>
    /// The source position of what the hairpin was paired with as its end — the level, the next
    /// hairpin's start or the stop (<c>@!cresc</c>) — or -1 for none: a hairpin nothing ends
    /// runs to its bar's line, and one the carry rule cuts (<see cref="CutAtSectionEnd"/>) to
    /// its section's end. What a writer of the same pairs closes the hairpin at (MusicXmlExporter).
    /// </summary>
    public int EndSourcePosition { get; init; } = -1;

    /// <summary>The hairpin's end was found past its section's end and refused by the carry
    /// rule (<c>Collector.SectionPlayCursor</c>): it is cut where its own section's play ends.</summary>
    public bool CutAtSectionEnd { get; init; }

    /// <summary>
    /// The hairpin's THIN end touches a niente — a crescendo from it (dal niente) or a
    /// decrescendo to it (al niente) — so that end is drawn as a circle and the niente is not
    /// printed as a word. A niente at the THICK end is a contradiction (LYS4029): the wedge is
    /// drawn plain and the word is printed.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:153 Hairpin::print reads circled_tip (the circled-tip of scm/define-grob-properties.scm) — "Put a circle at
    ///   start/end of hairpins (al/del niente)", the end being the tip.
    /// </remarks>
    public bool CircledTip => Direction == HairpinDirection.Crescendo
        ? NienteAtStart != null
        : NienteAtEnd != null;

    /// <summary>The niente the circle stands for (and that is therefore not printed), or null.</summary>
    public DynamicItem? CircledNiente => Direction == HairpinDirection.Crescendo
        ? NienteAtStart
        : NienteAtEnd;

    /// <summary>The niente at the THICK end, or null — the one LYS4029 reports.</summary>
    public DynamicItem? NienteAtThickEnd => Direction == HairpinDirection.Crescendo
        ? NienteAtEnd
        : NienteAtStart;

    /// <summary>
    /// The circled tip is shared: a decrescendo al niente and a crescendo dal niente meet on one
    /// note (<c>… g@niente@cresc …</c>), so both tips hang on the note's centre and their two
    /// circles fall on one place.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:243-252 Hairpin::print, x_points — "Handle back-to-back hairpins with
    ///   a circle in the middle": with an adjacent hairpin on the same column and the circled
    ///   tip on this side, <c>x_points[d] = e.center () + d * (rad - thick / 2.0)</c>.
    /// </remarks>
    public bool TipAdjacent { get; init; }

    /// <summary>
    /// Another hairpin starts on the note this one ends on (<c>… d''8@!cresc@decresc …</c>), the
    /// two back to back: each stops a third of the bound padding short of the note column's
    /// centre, so the wedges meet with a gap of two thirds and never cross (owner's report
    /// 2026-11-01: the crescendo ran to the note's right edge, through the decrescendo's open
    /// end). Not at a bar's head, where the ending hairpin's bound is the bar line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:241-260 Hairpin::print — neighbor_found, not circled:
    ///   <c>x_points[d] = e.center () - d * padding / 3</c>; lily/dynamic-engraver.cc:155-165 —
    ///   the hairpin ending and the one starting at one timestep are adjacent-spanners.
    ///   Measured (2.26.0, Lab sessions/p1036/chop/adj.svg): <c>c'8\&lt; d'8 e'8\!\&gt; f'8 g'8\!</c>, the
    ///   crescendo ends at 22.448 and the decrescendo starts at 23.115, 0.667 apart.
    /// </remarks>
    public bool AdjacentAtEnd { get; init; }

    /// <summary>This hairpin starts on the note another ends on — the other side of
    /// <see cref="AdjacentAtEnd"/>.</summary>
    public bool AdjacentAtStart { get; init; }

    // Identity, not value equality: see ModelIdentity.
    public bool Equals(HairpinItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);
}
