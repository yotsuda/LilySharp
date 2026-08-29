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

using System.Collections;
using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Reports a lyrics line that has MORE syllables than the notes it binds to, so
/// the trailing syllables found no note and were silently dropped from the
/// engraving. <see cref="Span"/> points at the FIRST dropped syllable, and
/// <see cref="FirstSyllable"/>/<see cref="FirstBar"/> name it and its 1-based
/// bar within the lyric line, so the author lands on the exact word where the
/// miscount starts.
/// </summary>
public record LyricSyllableWarning(
    LilySharp.Core.Syntax.TextSpan Span,
    int UnplacedSyllables,
    string FirstSyllable,
    int FirstBar
);

/// <summary>
/// A section's plain (unbracketed) lyric verse that is fully shadowed by the section's
/// <c>[N. …]</c> verses: every written-out occurrence already has a numbered verse, so
/// the plain line — which only fills an occurrence NO bracket covers — never renders.
/// <see cref="Span"/> anchors the first shadowed plain syllable.
/// </summary>
public record ShadowedPlainLyricWarning(
    LilySharp.Core.Syntax.TextSpan Span,
    string SectionName
);

/// <summary>
/// A tied pair whose two notes carry DIFFERENT explicit tab string numbers
/// (<c>\N</c>). A tie holds one string, so the held note can't change strings;
/// <see cref="SourcePosition"/> points at the destination note's <c>\N</c>.
/// </summary>
public record TabTieStringWarning(
    int SourcePosition,
    int PreviousString,
    int FollowingString
);

/// <summary>
/// A note whose sounding pitch is outside the tab's playable range — below the
/// lowest open string (silently CLAMPED to fret 0, i.e. shown as a wrong open
/// string) or above the 24th fret of every string. Almost always an octave slip.
/// <see cref="SourcePosition"/> points at the note.
/// </summary>
public record TabRangeWarning(
    int SourcePosition,
    bool BelowRange   // true = below the lowest string; false = above the top fret
);

/// <summary>A navigation mark (segno/coda/D.S./…) written mid-measure rather than at a
/// barline boundary — an unusual placement worth flagging.</summary>
public record NavigationMarkPlacementWarning(int SourcePosition, string MarkText);

/// <summary>
/// A tie (<c>~</c>) whose immediately following timed item cannot receive it — a
/// note/chord repeating none of the tied pitches, or an audible rest. A tie joins
/// two notes of the SAME pitch, so this is almost always an authoring slip (a slur
/// was meant, or the target note was mistyped). <see cref="SourcePosition"/> points
/// at the following item (the one that fails to match).
/// </summary>
public record TieTargetWarning(
    int SourcePosition,
    TieTargetProblem Problem
);

/// <summary>
/// Why a tie could not bind. Three states, not two booleans: a bool pair would have a
/// fourth combination that cannot happen.
/// </summary>
public enum TieTargetProblem
{
    /// <summary>The following note/chord repeats none of the tied pitches.</summary>
    PitchMismatch,
    /// <summary>The following timed item is an audible rest.</summary>
    IntoRest,
    /// <summary>Nothing follows at all — the tie is the last thing in its voice, so the
    /// renderer draws nothing. MEASURED: `c4 d4 e4 f4~ |` engraves byte-for-byte as the
    /// same bar without the tie, while `f4@laissezVibrer` draws the hanging tie the
    /// writer probably meant.</summary>
    NoTarget,
}

/// <summary>
/// A slur mark that pairs with nothing, so no slur is drawn: a <c>(</c> that is never
/// closed (including one left open when its voice ends — a slur does not cross voices)
/// or a <c>)</c> read with none open. <see cref="SourcePosition"/> points at the NOTE the
/// mark is written on, because that is where the mark binds: a slur mark annotates the
/// note BEFORE it (MeasureCollector.MusicWalk PeekMarkers), which is also why a <c>(</c>
/// with no note before it never becomes a mark at all and is seen here only through the
/// <c>)</c> that then has nothing to pair with.
/// </summary>
public record UnpairedSlurWarning(
    int SourcePosition,
    bool IsOpen       // true = an unclosed '('; false = a ')' with nothing open
);

/// <summary>Which span family a pairing complaint is about. One enum rather than one per
/// family, because the LANGUAGE now has one answer for all of them: a span must be closed,
/// and one nobody closed draws nothing. What differs is only the words the diagnostic
/// uses.</summary>
public enum SpanKind
{
    /// <summary>A text spanner (<c>@rit</c>, <c>@textSpan("…")</c> … <c>@!</c>).</summary>
    TextSpanner,
    /// <summary>An ottava bracket (<c>@ottava</c>, <c>@quindicesima</c> … <c>@!</c>).</summary>
    Ottava,
    /// <summary>A piano pedal (<c>@sustainOn</c> … <c>@sustainOff</c>, and its two
    /// siblings). ⚠️ ONLY TWO OF THE THREE FAULTS APPLY: a second <c>@sustainOn</c> while
    /// one is down is RE-PEDALLING, which is real notation ("Ped. … Ped."), so it opens a
    /// new bracket rather than being refused the way a nested span is.</summary>
    Pedal,
}

/// <summary>Why a span mark drew nothing — the three situations a start/stop pairing can
/// fail in, which are also the three an engraver holding ONE open span can meet.</summary>
/// <remarks>
/// ⚠️ THE THREE ARE LILYPOND'S, COUNTED FROM ITS TEXT SPANNER ENGRAVER rather than invented:
/// they are the three places <c>Text_spanner_engraver</c> can complain. Reading a family's
/// engraver for how many warnings it can emit gives the classification for free.
/// LILYPOND-REF: lily/text-spanner-engraver.cc:59-88 Text_spanner_engraver::process_music,
/// :117-127 Text_spanner_engraver::finalize.
/// </remarks>
public enum SpanPairingFault
{
    /// <summary>A START that no <c>@!</c> ever closed.</summary>
    Unterminated,
    /// <summary>A terminator with no span open in its voice.</summary>
    StopWithNoStart,
    /// <summary>A START written while one is already open in the same voice. The OPEN one
    /// keeps the span; spans do not nest.
    /// ⚠️ ONLY THE TEXT SPANNER REACHES THIS. Its engraver refuses the second start
    /// ("already have a text spanner"), but the other two families read the same writing as
    /// a CHANGE and close-then-open: an ottava because any ottava event finishes the open
    /// span first (lily/ottava-engraver.cc:122-136), a pedal because that is re-pedalling.
    /// Session 289 gave the ottava this fault by analogy and audit/lpreg/ottcons.lys — the
    /// twin of LilyPond's ottava-consecutive.ly — refused it.</summary>
    StartWhileOpen,
}

/// <summary>
/// A span mark that pairs with nothing, so nothing is drawn for it.
/// <see cref="SourcePosition"/> points at the mark itself.
/// </summary>
/// <remarks>
/// Recorded by the SAME call that decides which spans are drawn (each family's pairing
/// walk) and surfaced by <c>SpanPairingValidator</c>, for the reason
/// <see cref="UnpairedSlurWarning"/> gives: a warning that re-derives the pairing can
/// disagree with the page.
/// <para>
/// ⚠️ ONE PER (SOURCE POSITION, KIND, FAULT). The marks being paired are the PLAYED piece's,
/// so a mark written inside a repeated section arrives once per playing; it is unterminated
/// once.
/// </para>
/// </remarks>
public record UnpairedSpanWarning(
    int SourcePosition,
    SpanKind Kind,
    SpanPairingFault Fault
);

/// <summary>Which kind of span crossed a cue boundary — the two whose engravers LilyPond
/// keeps in the Voice context, so a cue region (a Voice of its own) cuts both.</summary>
public enum CueSpanKind
{
    /// <summary>A slur, paired by <see cref="SlurPairingScanner"/>'s stack.</summary>
    Slur,
    /// <summary>A tie, bound to the next timed item by <see cref="TieTargetScanner"/>.</summary>
    Tie,
}

/// <summary>
/// A slur or tie with one end inside a <c>cue { … }</c> region and the other outside it.
/// LilyPond cannot engrave such a span at all, so what Lily# draws for it is ink LilyPond
/// will never make. <see cref="SourcePosition"/> points at the item the span STARTS on,
/// which is the end the writer usually has to move.
/// </summary>
/// <remarks>
/// Recorded by the same two scanners that already decide the pairing — the crossing test is one
/// comparison on a pair those scanners have in hand — so this can never disagree with what gets
/// drawn, for the reason <see cref="UnpairedSlurWarning"/> gives.
/// <para>
/// ⚠️ THE COMPARISON IS OF REGIONS, NOT OF A FLAG, and it has to be: <c>cue { … } cue { … }</c>
/// is TWO CueVoice contexts (probe cue-span.ly C-TWO), so a span running from one into the next
/// crosses a voice boundary and LilyPond refuses it exactly as it refuses one leaving a cue —
/// MEASURED 2026-08-15, the slur form warns "unterminated slur" / "cannot end slur" and the tie
/// form is dropped in silence (byte-identical SVG). Both ends answer <c>IsCue</c> true, so until
/// the collector stamped the region's EDGE (<see cref="MusicItem.BeginsCueRegion"/>) that
/// crossing was invisible here and Lily# drew the curve without a word.
///   observed by: <c>CueRegionTests.SlurBetweenTwoAdjacentCuesIsRejected</c> and its tie twin.
///     No fixture, sample or corpus book writes two cue blocks back to back (grep 2026-08-03,
///     re-checked 2026-08-10 and twice on 2026-08-15 — of 888 <c>.lys</c> on disk only 10 write
///     a cue region at all, 4 of them outside scratch/), so those two tests ARE the observer.
/// </para>
/// <para>
/// A span that passes OVER a whole cue region (<c>c4( cue { e f } g4)</c>) is NOT a crossing
/// and is not reported: both ends are in the enclosing Voice, and MEASURED, LilyPond pairs it
/// without a word.
/// </para>
/// </remarks>
public record CueSpanBoundaryWarning(
    int SourcePosition,
    CueSpanKind Kind,
    CueSpanCrossing Crossing
);

/// <summary>Which voice boundary the span crossed — the three a cue region can put between
/// two notes. All three are one condition to LilyPond (the ends are in different Voice
/// contexts); they are told apart only so the diagnostic can name what was written.</summary>
public enum CueSpanCrossing
{
    /// <summary>Begins outside a <c>cue { … }</c> and ends inside it.</summary>
    IntoCue,
    /// <summary>Begins inside a <c>cue { … }</c> and ends outside it.</summary>
    OutOfCue,
    /// <summary>Begins in one <c>cue { … }</c> and ends in the NEXT one — the crossing a
    /// per-note cue flag cannot see, since both ends are cued.</summary>
    BetweenCues,
}

/// <summary>
/// A manual beam bracket that pairs with nothing: a <c>[</c> never closed (including one
/// left open when its voice ends — <see cref="BeamDetector"/> matches per voice) or a
/// <c>]</c> read with none open. Unlike an unpaired slur, which loses its curve outright,
/// an unpaired bracket loses only the GROUPING the writer asked for: the bracket is
/// discarded and the notes fall back to automatic beaming. MEASURED — <c>c8[ d8 e8 f8 g8</c>
/// engraves byte-for-byte as the same notes with no bracket at all, while the closed
/// <c>c8[ d8 e8 f8 g8]</c> engraves a five-note beam that automatic beaming never produces.
/// So the beam that appears is not the one that was written, and nothing said so.
/// </summary>
public record UnpairedBeamWarning(
    int SourcePosition,
    bool IsOpen       // true = an unclosed '['; false = a ']' with nothing open
);

/// <summary>
/// A <c>|:</c> that no <c>:|</c> ever closes — a repeat whose end nobody wrote.
/// </summary>
/// <remarks>
/// There is no <c>IsOpen</c> half here, unlike the slur and beam warnings: the OTHER
/// one-sided case has a meaning rather than a defect. A <c>:|</c> with nothing open repeats
/// from the beginning of the piece, which is the ordinary reading of the sign.
/// <para>
/// ⚠️ The position is a MEASURE boundary offset, not the offset of a token in the written
/// text — and it cannot be otherwise. The pairing is only decidable after score expansion
/// (a section's <c>|:</c> may be closed by a <c>:|</c> the form writes), so what is scanned
/// is the expanded measure stream, where the two layers have already become siblings.
/// </para>
/// </remarks>
public record UnpairedRepeatWarning(
    int SourcePosition
);

