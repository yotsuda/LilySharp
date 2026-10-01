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
using System.Collections.Generic;
using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Shared per-voice item traversal used by the span detectors (tie / slur /
/// glissando). Each voice runs its own engraver, so every detector scans all
/// voices and keys its output by voice index; a single-voice score iterates once
/// with voice 0 (byte-identical).
/// LILYPOND-REF: ly/engraver-init.ly — Tie_/Slur_/Glissando_engraver per Voice.
/// </summary>
internal static class VoiceScan
{
    /// <summary>
    /// Enumerates every item of every voice in order, yielding the voice index,
    /// that voice's measures (for follow-on lookups), and the item's measure/item
    /// indices. A detector with per-voice state resets it when
    /// <c>VoiceIndex</c> changes.
    /// </summary>
    /// <remarks>
    /// A struct walk (<see cref="VoiceItemWalk"/>), not a <c>yield</c> iterator: the three
    /// detectors each built a 104 B state machine per collect (session 446's census, 181 B a
    /// keystroke apiece over the reader's corpus), and <c>foreach</c> over the struct builds
    /// nothing. The order — voice, measure, item — and the grace skip are the iterator's
    /// (RULES §5.4: the safety of this rewrite is order identity).
    /// </remarks>
    public static VoiceItemWalk WalkVoiceItems(Score score) => new(score, includeGraceTime: false);

    /// <summary>
    /// <see cref="WalkVoiceItems"/> with grace time IN the stream — for the slur detector,
    /// which pairs an opening mark with its closing mark, never with "the next item", so the
    /// reason the walk skips grace time does not bind it (session 725: `grace { d'16( e') }`
    /// and `grace { g16( } a8)` are ordinary slurs, as in LilyPond).
    /// </summary>
    public static VoiceItemWalk WalkVoiceItemsWithGraceTime(Score score) => new(score, includeGraceTime: true);

    /// <summary>The walk <see cref="WalkVoiceItems"/> hands out; <c>foreach</c> binds to
    /// <see cref="GetEnumerator"/> and allocates nothing.</summary>
    public readonly struct VoiceItemWalk
        : IEnumerable<(int VoiceIndex, ImmutableArray<Measure> Measures, int MeasureIndex, int ItemIndex, MusicItem Item)>
    {
        private readonly Score _score;
        private readonly bool _includeGraceTime;

        internal VoiceItemWalk(Score score, bool includeGraceTime)
        {
            _score = score;
            _includeGraceTime = includeGraceTime;
        }

        public Enumerator GetEnumerator() => new(_score.Voices, _includeGraceTime);

        IEnumerator<(int VoiceIndex, ImmutableArray<Measure> Measures, int MeasureIndex, int ItemIndex, MusicItem Item)>
            IEnumerable<(int VoiceIndex, ImmutableArray<Measure> Measures, int MeasureIndex, int ItemIndex, MusicItem Item)>.GetEnumerator()
            => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Voice by voice, measure by measure, item by item — grace time skipped.</summary>
        public struct Enumerator
            : IEnumerator<(int VoiceIndex, ImmutableArray<Measure> Measures, int MeasureIndex, int ItemIndex, MusicItem Item)>
        {
            private readonly ImmutableArray<Voice> _voices;
            private ImmutableArray<Measure> _measures;
            private ImmutableArray<MusicItem> _items;
            private int _v, _m, _i;
            private readonly bool _includeGraceTime;

            internal Enumerator(ImmutableArray<Voice> voices, bool includeGraceTime)
            {
                _voices = voices;
                _includeGraceTime = includeGraceTime;
                _measures = ImmutableArray<Measure>.Empty;
                _items = ImmutableArray<MusicItem>.Empty;
                _v = -1;
                _m = 0;
                _i = -1;
                Current = default;
            }

            public (int VoiceIndex, ImmutableArray<Measure> Measures, int MeasureIndex, int ItemIndex, MusicItem Item) Current
            { get; private set; }

            readonly object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                while (true)
                {
                    _i++;
                    while (_i >= _items.Length)
                    {
                        // The measure is spent: the next one, or the next voice's first.
                        _m++;
                        while (_m >= _measures.Length)
                        {
                            _v++;
                            if (_v >= _voices.Length)
                                return false;
                            _measures = _voices[_v].Measures;
                            _m = 0;
                        }
                        _items = _measures[_m].Items;
                        _i = 0;
                    }
                    // GRACE TIME IS NOT YET IN THE SPAN DETECTORS' STREAM. All three pair an
                    // opening flag with THE NEXT item, and a grace takes no measure time, so
                    // it stands between a note and the note that note reaches to — MEASURED,
                    // `d4@glissando grace { d8 } c` drew its glissando to the GRACE (a
                    // horizontal line, both heads being d) instead of to the c.
                    // ⚠️ SCAFFOLDING, and the one whose removal is the PRIZE: this skip is
                    // the whole of "a grace note cannot carry a tie" (LYS4020). Deleting it is
                    // what HANDOFF §2 U8 ⒞ means, and it can only go once ⒝2 lets the ordinary
                    // engravers draw grace time — until then the detectors would pair spans
                    // that nothing would draw. The SLUR detector already walks grace time
                    // (WalkVoiceItemsWithGraceTime, session 725).
                    if (!_includeGraceTime && _items[_i].GraceTime)
                        continue;
                    Current = (_v, _measures, _m, _i, _items[_i]);
                    return true;
                }
            }

            public void Reset()
            {
                _measures = ImmutableArray<Measure>.Empty;
                _items = ImmutableArray<MusicItem>.Empty;
                _v = -1;
                _m = 0;
                _i = -1;
                Current = default;
            }

            public readonly void Dispose() { }
        }
    }

    /// <summary>
    /// Curve direction for a tie/slur span. Where the span STARTS inside polyphony the voice
    /// fixes it — the upper voice (index 0, 2, …) curves UP, the lower voice (1, 3, …) DOWN —
    /// so the voices' spans stay clear of each other; elsewhere the given stem-based
    /// <paramref name="singleVoiceFallback"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly \voiceOne/\voiceTwo set Tie/Slur.direction = UP/DOWN
    /// — and they hold only inside the <c>&lt;&lt; \\ &gt;&gt;</c> span (scm/music-functions.scm
    /// voicify-sublist), where the grob is created with them. The question is therefore
    /// <see cref="VoiceDefaults.GetDefaultStemUpAt"/>'s, at the span's START ITEM (per item
    /// since session 652 — a voice span can open mid-bar), the one stems and beams also ask. It was <c>voiceCount &gt; 1</c> — PART-wide — until session 650, so one
    /// two-voice passage pinned every slur and tie of the part up: showcase/grammar-tour
    /// drew `b4( c d e)` and `g2~ g4` over the staff in bars 13-15, forty bars before its
    /// only `&lt;&lt; \\ &gt;&gt;`, where LilyPond draws them under (the same fold VoiceColumn's
    /// remarks record for stems on 2026-08-01).
    /// </remarks>
    public static bool SpanCurvesUp(
        ImmutableArray<Voice> voices, int voiceIndex, int startMeasureIndex, int startItemIndex,
        bool singleVoiceFallback)
        => ForcedCurveUpAt(voices, voiceIndex, startMeasureIndex, startItemIndex) ?? singleVoiceFallback;

    /// <summary>The direction polyphony forces on a tie/slur of voice
    /// <paramref name="voiceIndex"/> starting at item <paramref name="startItemIndex"/> of
    /// measure <paramref name="startMeasureIndex"/>, or null where the span does not cover it
    /// (see <see cref="SpanCurvesUp"/>; since session 652 per item, not per measure — the
    /// remarks of <see cref="VoiceDefaults.GetDefaultStemUpAt(ImmutableArray{Voice}, int, int, int)"/>).</summary>
    public static bool? ForcedCurveUpAt(
        ImmutableArray<Voice> voices, int voiceIndex, int startMeasureIndex, int startItemIndex)
        => VoiceDefaults.GetDefaultStemUpAt(voices, voiceIndex, startMeasureIndex, startItemIndex);
}
