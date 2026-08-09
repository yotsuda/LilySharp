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
/// Represents a trill spanner (tr symbol with wavy line extension).
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/scheme-engravers.scm Trill_spanner_engraver class
/// LILYPOND-REF: scm/define-grobs.scm:2175-2230 TrillSpanner grob definition
///
/// Trill spanners display "tr" at the start point with a wavy line
/// extending to the end point. Used for sustained trills:
///   tr~~~~~~~~~~~~
/// </remarks>
public sealed record TrillSpannerItem(
    // Measure index of the start point.
    int StartMeasureIndex,
    // Item index within the start measure.
    int StartItemIndex,
    // Measure index of the end point.
    int EndMeasureIndex,
    // Item index within the end measure.
    int EndItemIndex,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // Global staff index this spanner belongs to (multi-staff routing;
    // see DynamicItem.StaffIndex). 0 for single-staff.
    int StaffIndex = 0,
    // Which VOICE of that staff engraved it — the voice whose note columns are its
    // side-support and whose column the left bound attaches to. LilyPond consists
    // Trill_spanner_engraver in the Voice context (ly/engraver-init.ly:376), so a trill
    // sides off ITS OWN voice's grobs; another voice's ink reaches it through the
    // outside-staff collision pass instead. Same field, same reason, as
    // DynamicItem.VoiceIndex.
    int VoiceIndex = 0,
    // The direction the WRITER forced with @startTrillSpan.up/.down (LilyPond's
    // ^\startTrillSpan / _\startTrillSpan): +1 / −1, or 0 when unforced — the engraver
    // then falls to the voice default (TrillSpanner is a direction-polyphonic grob, so
    // an even voice's trills sit BELOW) and lastly the grob's own UP.
    // LILYPOND-REF: scm/scheme-engravers.scm:1818-1820 Trill_spanner_engraver — the
    //   start event's direction is set on the grob when the writer gave one;
    // LILYPOND-REF: scm/music-functions.scm:617-634 direction-polyphonic-grobs —
    //   TrillSpanner is in the list; scm/define-grobs.scm:4076 — (direction . UP).
    int Direction = 0
);
