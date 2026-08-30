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

using LilySharp.Core.Syntax;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Zero-width sentinel injected into the flattened music-node stream at each
/// phrase-reference expansion: the collector resets its relative pitch and
/// duration state when it encounters one, so every phrase body evaluates in
/// the default frame regardless of call site. A reference's trailing octave
/// marks (<c>Chorus'</c> / <c>Chorus,</c>) travel on <see cref="OctaveOffset"/>,
/// shifting the fresh frame up or down before the phrase body runs.
/// </summary>
internal sealed class RelativeResetMarker : SyntaxNode
{
    /// <summary>The anchorless offset-free reset (a parallel span's fresh frame).</summary>
    public static readonly RelativeResetMarker Instance = new(0, null);

    /// <summary>Net octave shift applied to the reset frame (' = +1, , = -1).</summary>
    public int OctaveOffset { get; }

    /// <summary>The phrase's anchor step (see <see cref="Music.PhraseAnchor"/>):
    /// the written step the paired <see cref="PhraseEndMarker"/> hands back to
    /// the relative chain — the chord rule, so a reference propagates its first
    /// note's bare letter, never its interior. <see cref="Music.PhraseAnchor.Tonic"/>
    /// marks a degree-opened body (resolved to the AMBIENT tonic at the
    /// reference); null = pitchless body, nothing to hand off.</summary>
    public int? AnchorStep { get; }

    /// <summary>Reuses <see cref="Instance"/> for the (common) bare anchorless case.</summary>
    public static RelativeResetMarker For(int octaveOffset, int? anchorStep = null)
        => octaveOffset == 0 && anchorStep == null
            ? Instance
            : new RelativeResetMarker(octaveOffset, anchorStep);

    private RelativeResetMarker(int octaveOffset, int? anchorStep)
        : base(MarkerGreen.Shared, parent: null, position: 0)
    {
        OctaveOffset = octaveOffset;
        AnchorStep = anchorStep;
    }

    private sealed class MarkerGreen : GreenNode
    {
        public static readonly MarkerGreen Shared = new();
        private MarkerGreen() : base(SyntaxKind.None, fullWidth: 0) { }
    }
}

/// <summary>
/// Zero-width sentinel closing a phrase-reference expansion: paired with the
/// leading <see cref="RelativeResetMarker"/>, it lets the collector restore the
/// pitch transpose it armed for the movable phrase, so inline notes that follow
/// the reference stay at their written (absolute) pitch.
/// </summary>
internal sealed class PhraseEndMarker : SyntaxNode
{
    public static readonly PhraseEndMarker Instance = new();

    private PhraseEndMarker()
        : base(MarkerGreen.Shared, parent: null, position: 0)
    {
    }

    private sealed class MarkerGreen : GreenNode
    {
        public static readonly MarkerGreen Shared = new();
        private MarkerGreen() : base(SyntaxKind.None, fullWidth: 0) { }
    }
}

/// <summary>
/// Zero-width sentinel opening a TUPLET written inside a <c>grace { }</c> body: the
/// expander (<see cref="Semantics.GraceBodySupport"/>) replaces the tuplet by its body
/// bracketed with this and <see cref="GraceTupletEndMarker"/>, so each reader borrows the
/// part of the ratio IT reads and gives it back at the close.
/// </summary>
/// <remarks>
/// ⚠️ A TUPLET IN A GRACE BODY IS A CONTAINER, NOT A GROB, which is why it is expanded
/// rather than engraved. MEASURED on LilyPond 2.26.0 (session 301, scratch/p301/lp,
/// <c>data-pos</c> masked): <c>\grace { \tuplet 3/2 { d'16 e' f' } }</c> puts its three
/// notes at coordinates BYTE-IDENTICAL to <c>\grace { d'16 e' f' }</c>, and all that is
/// added is the italic serif <c>3</c> (with the four bracket <c>&lt;line&gt;</c>s too, once
/// the durations are long enough that no beam stands in for them). So the PAGE reads nothing
/// off the ratio: it engraves the written durations and loses the bracket and the number,
/// which stay in the drop list.
/// <para>
/// ⚠️ THE SOUND DOES READ IT, and the two halves are ONE mechanism in LilyPond rather than
/// two decisions. LILYPOND-REF: lily/duration-scheme.cc:190-200 ly_duration_compress — the
/// entry <c>\tuplet</c> reaches (it makes a <c>TimeScaledMusic</c> whose element is the music
/// compressed by <c>normal/actual</c>, before any engraver or performer sees it). What that
/// call does is multiply the duration's FACTOR and touch nothing else: the log and the dots,
/// which decide the notehead, the flag and the number of beams, come through unchanged, while
/// the duration's value as a moment is the log-and-dots value TIMES the factor. ⇒ One
/// compression, two answers: the played length shrinks and the drawn note does not. That is
/// why the page's arm below is a no-op and the two exporters' are not.
/// <para>
/// MEASURED 2026-08-30 (session 302, scratch/p302/lp) on LilyPond's own <c>\midi</c>,
/// division 384: <c>\grace { d'16 e' f' } c'4</c> puts its three grace notes at ticks
/// 0 / 21 / 43 and hands the main note over at 64, while
/// <c>\grace { \tuplet 3/2 { … } } c'4</c> puts them at 0 / 14 / 29 and hands over at 43 —
/// 64 × 2/3 = 42.67. So <see cref="Midi.MidiExporter"/> and
/// <see cref="MusicXml.MusicXmlExporter"/> push the ratio on the tuplet stack their main
/// streams already keep, and nothing new is spelled: the arithmetic that reads it
/// (<c>FractionToTicks</c>, <c>CurrentTupletRatio</c>) is the house that was already there.
/// </para>
/// </para>
/// </remarks>
internal sealed class GraceTupletStartMarker : SyntaxNode
{
    /// <summary>How many notes are played (LilyPond's <c>\tuplet ACTUAL/NORMAL</c>
    /// numerator; a triplet's 3).</summary>
    public int Actual { get; }

    /// <summary>In the time of how many (a triplet's 2).</summary>
    public int Normal { get; }

    /// <summary>The tuplet as written, kept for the SPAN the dropped bracket is reported
    /// at — the marker itself is zero-width and stands at position 0, so it can name no
    /// place in the source on its own.</summary>
    public TupletExpressionSyntax Written { get; }

    public GraceTupletStartMarker(TupletExpressionSyntax written)
        : base(MarkerGreen.Shared, parent: null, position: 0)
    {
        Written = written;
        Actual = written.TupletRatio;
        Normal = written.BaseDivision;
    }

    private sealed class MarkerGreen : GreenNode
    {
        public static readonly MarkerGreen Shared = new();
        private MarkerGreen() : base(SyntaxKind.None, fullWidth: 0) { }
    }
}

/// <summary>
/// Zero-width sentinel closing a <see cref="GraceTupletStartMarker"/>. The pair is emitted
/// or omitted together (the expander pays for a whole entry or none of it), so a reader that
/// pushed on the open can pop on the close without a balance check of its own.
/// </summary>
internal sealed class GraceTupletEndMarker : SyntaxNode
{
    public static readonly GraceTupletEndMarker Instance = new();

    private GraceTupletEndMarker()
        : base(MarkerGreen.Shared, parent: null, position: 0)
    {
    }

    private sealed class MarkerGreen : GreenNode
    {
        public static readonly MarkerGreen Shared = new();
        private MarkerGreen() : base(SyntaxKind.None, fullWidth: 0) { }
    }
}
