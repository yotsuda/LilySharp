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
using System.Runtime.CompilerServices;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// What the caret's status line reads off a tree's collects — the resolved pitch of every
/// written pitch (<see cref="ResolvedPitches.ForFile(SyntaxTree)"/>) and the place of every
/// note (<see cref="NotePlaces"/>) — from ONE collect of each score, kept by the tree.
/// </summary>
/// <remarks>
/// The two were read by two walks of every score: the sound's index collected each score for
/// the pitches, and the places collected each again — 2S full collects a keystroke with the
/// caret on a note, beside the preview's one resumed collect (session 1291's review, P4:
/// caretInfo was 84% of the side work left on a keystroke). A score that transposes
/// (<c>transpose</c>, <c>concert</c>, a staff's pitch) is still collected a second time for
/// its places, which read the pitch as the part writes it; nearly no score does.
/// ⚠️ One tree, one entry: with <c>using</c>s or a printed singer the sound reads the
/// expanded tree and the places the document's own, and each pays its own collects as before.
/// </remarks>
internal sealed class FileReadings
{
    /// <summary>The resolved pitches in written order; null when no score collected.</summary>
    public IReadOnlyList<MeasureCollector.PitchTraceEntry>? Trace { get; }

    public NotePlaces Places { get; }

    /// <summary>How many full collects the build ran — for tests.</summary>
    public int Collects { get; }

    private FileReadings(IReadOnlyList<MeasureCollector.PitchTraceEntry>? trace, NotePlaces places, int collects)
        => (Trace, Places, Collects) = (trace, places, collects);

    private static readonly ConditionalWeakTable<SyntaxTree, FileReadings> Cache = new();

    public static FileReadings Of(SyntaxTree tree) => Cache.GetValue(tree, Build);

    private static FileReadings Build(SyntaxTree tree)
    {
        var places = new NotePlaces.Builder();
        int collects = 0;
        IReadOnlyList<MeasureCollector.PitchTraceEntry>? trace = null;
        try
        {
            trace = ResolvedPitches.ForFile(tree, (spec, collector) =>
            {
                collects++;
                bool transposes = spec is { ScoreTranspose: not null } or { ScoreConcert: not null }
                    or { StaffPitch: not null };
                if (!transposes && collector.Collected is { } score)
                    places.Add(score, collector.PitchTrace);
                else
                {
                    collects++;
                    places.AddUntransposed(tree, spec);
                }
            });
        }
        catch (System.Exception)
        {
            // A tree the collector cannot walk has no pitches and the places gathered so far.
        }
        return new FileReadings(trace, places.Build(), collects);
    }
}
