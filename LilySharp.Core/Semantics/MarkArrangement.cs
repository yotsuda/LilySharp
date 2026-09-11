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

using System.Collections.Generic;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The <c>marks</c> key of a <c>layout { }</c> block — how a boxed section label and the
/// metronome mark standing at the same bar are arranged — and its two words. The reading
/// is <see cref="LayoutPlanReader"/>'s; the bit it produces is <see cref="LayoutPlan.MarksBeside"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>marks stacked</c> (the default) is LilyPond's arrangement: the label break-aligns to
/// the key/clef column and the tempo to the meter column, each on its own anchor, and the
/// outside-staff pass stacks the label over the tempo wherever their inks meet
/// (MusicMarkEngraver, OutsideStaffStacker.PlaceMusicMarks). <c>marks beside</c> is the
/// chart's one line: the label's box stands at the line-start edge (after the drawn
/// <c>|:</c> when the line opens on one) and the tempo sits to its right, baselines aligned
/// — "[Chorus] ♩ = 132". LilyPond has no such construction;
/// it is a Lily#-own arrangement the owner asked for as an OPTION (2026-09-02, HANDOFF §3),
/// after the label's default placement was brought to LilyPond's (session 324).
/// </para>
/// <para>
/// It was a bare top-level directive (and a bare score item) from 2026-09-09 to 2026-09-11,
/// when the owner moved it into <c>layout { }</c>: a bare word was the only display-only
/// global among the music settings, and the same word names a FONT GROUP in
/// <c>fonts { marks "Georgia" }</c> — inside a block the block says which aspect of the
/// marks is meant (their face, their arrangement), which the bare form could not.
/// </para>
/// </remarks>
public static class MarkArrangement
{
    /// <summary>The key, as written in the block.</summary>
    public const string Property = "marks";

    /// <summary>LilyPond's arrangement — the label stacked over the tempo — the default.</summary>
    public const string Stacked = "stacked";

    /// <summary>The chart's — the label at the line start with the tempo to its right.</summary>
    public const string Beside = "beside";

    /// <summary>The two words <c>marks</c> takes, the default first.</summary>
    public static readonly IReadOnlyList<string> Modes = [Stacked, Beside];
}
