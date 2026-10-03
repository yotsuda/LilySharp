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
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Folds the clef changes a voice writes at ONE moment — two or more with none of its notes,
/// chords or rests between them, a bar line or not — into what LilyPond prints there: the
/// LAST clef set, and nothing at all when that is the clef already in force.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: lily/clef-engraver.cc:139-165 Clef_engraver::inspect_clef_properties —
///   asked once per timestep (process_music, :122), it compares clefGlyph, clefPosition and
///   clefTransposition with prev_glyph_, prev_cpos_ and prev_transposition_ and creates a
///   clef only when one differs; several <c>\clef</c> (or a <c>\cueClefUnset</c> followed by
///   a <c>\cueClef</c>) at one moment leave only the last value to compare, and an
///   unchanged one prints nothing.
/// </para>
/// <para>
/// Found by eye (session 767, the collision studies): a <c>cue treble { … }</c> closing at
/// a bar line and another opening right after it wrote the alto RESTORE and the next
/// region's treble cue clef at the same column, drawn on top of each other, where
/// LilyPond's twin printed neither. A single clef change is left alone — a written
/// <c>clef!</c> forces its glyph, and whether a lone change that changes nothing is drawn
/// is the walk's own question, not this fold's.
/// </para>
/// </remarks>
internal static class ClefChangeCollapse
{
    /// <summary>Applies the fold in place; <paramref name="initial"/> is the clef in force
    /// before the voice's first item.</summary>
    public static void Apply(List<Measure> measures, ClefType initial)
    {
        var inForce = initial;
        var run = new List<(int M, int I)>();
        List<(int M, int I)>? drop = null;

        void Close()
        {
            if (run.Count == 0)
                return;
            var last = (ClefChangeItem)measures[run[^1].M].Items[run[^1].I];
            if (run.Count >= 2)
            {
                // Every change but the last is unseen by LilyPond; the last too when it
                // restores what was in force before the run began.
                int dropped = last.NewClef == inForce ? run.Count : run.Count - 1;
                for (int k = 0; k < dropped; k++)
                    (drop ??= new()).Add(run[k]);
            }
            inForce = last.NewClef;
            run.Clear();
        }

        for (int m = 0; m < measures.Count; m++)
        {
            var items = measures[m].Items;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] is ClefChangeItem)
                    run.Add((m, i));
                else if (items[i] is NoteItem or ChordItem or RestItem)
                    Close();
            }
        }
        Close();

        if (drop == null)
            return;
        foreach (var group in drop.GroupBy(d => d.M))
        {
            var gone = group.Select(g => g.I).ToHashSet();
            var kept = measures[group.Key].Items.Where((_, idx) => !gone.Contains(idx)).ToImmutableArray();
            measures[group.Key] = measures[group.Key] with { Items = kept };
        }
    }
}
