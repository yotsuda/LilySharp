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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The last step of the collect phase: marks a score's mid-piece meter changes BLANKED when
/// no staff in it engraves a time signature stencil.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: ly/engraver-init.ly:1214-1220 — the TabStaff block that \remove Key_engraver opens; a bare TabStaff carries
/// <c>\override TimeSignature.stencil = ##f</c>. LilyPond does NOT remove the
/// Time_signature_engraver there; the grob is made and then blanked, so it stands in the
/// non-musical column with an empty X extent and every walk that reads an extent steps over
/// it. This class is the port of that <c>##f</c>, and
/// <see cref="SpacingRules.ChangeItemHasInk"/> is the port of the two skips.
/// </para>
/// <para>
/// ⚠️ WHY THIS IS A PASS AND NOT A FLAG SET AT ITEM CREATION. The question is score-level —
/// <see cref="SpacingRules.AnyStaffEngravesTime"/>, the OR over every staff, because a paper
/// column aggregates all of them — and the staves do not exist until the collect phase has
/// built the voices they are made of (<c>RenderSpec.ToStaffGroups</c> takes the collected
/// voices). No point inside the walk can answer it. The alternative was to thread the answer
/// as a parameter through the nine spacing entry points that price a change column and their
/// callers in MeasureLayouter / MultiStaffLayouter / SystemBreaker / KnuthPlassBreaker /
/// SkylineBuilder / SharedRenderer; putting it on the item instead leaves ONE predicate and no
/// signature able to be forgotten at a call site (HANDOFF 5.2.1②).
/// </para>
/// <para>
/// ⚠️ VOICES ARE SHARED BETWEEN STAVES. <c>RenderSpec.ToStaffGroups</c> hands the SAME
/// <see cref="Voice"/> objects — hence the same <see cref="Measure"/> and the same
/// <see cref="MusicItem"/> — to a notation staff and to the tab staff of the same part. The
/// rewrite therefore memoises on REFERENCE identity, not on value: rewriting per staff would
/// hand two staves two copies of one measure, doubling the model and breaking the
/// <c>ReferenceEquals</c> the change-column walk uses to find an item's place in its column
/// (<see cref="SpacingRules.MidMeasureChangeOffsetWithin"/>).
/// </para>
/// <para>
/// Untouched objects are returned as they stand, so a score with no mid-piece meter — every
/// book in the corpus that reaches this at all — allocates nothing but the walk itself.
/// </para>
/// </remarks>
internal static class MeterStencil
{
    /// <summary>
    /// Returns <paramref name="score"/> with every mid-piece meter change marked
    /// <see cref="TimeSignatureChangeItem.Blanked"/> when no staff engraves one, and
    /// <paramref name="score"/> itself otherwise.
    /// </summary>
    public static MultiStaffScore Blank(MultiStaffScore score)
    {
        // The one question, asked in the one place that already owns it. TRUE is the ordinary
        // answer — any notation staff at all gives the column its width — so the common path
        // is this comparison and nothing else.
        if (SpacingRules.AnyStaffEngravesTime(score))
            return score;

        var voices = new Dictionary<Voice, Voice>(ReferenceEqualityComparer.Instance);
        var measures = new Dictionary<Measure, Measure>(ReferenceEqualityComparer.Instance);

        var groups = ImmutableArray.CreateBuilder<StaffGroup>(score.StaffGroups.Length);
        bool anyGroupChanged = false;
        foreach (var group in score.StaffGroups)
        {
            var staves = ImmutableArray.CreateBuilder<Staff>(group.Staves.Length);
            bool anyStaffChanged = false;
            foreach (var staff in group.Staves)
            {
                var rewritten = BlankVoices(staff.Voices, voices, measures);
                anyStaffChanged |= !rewritten.IsDefault;
                staves.Add(rewritten.IsDefault ? staff : staff with { Voices = rewritten });
            }
            anyGroupChanged |= anyStaffChanged;
            groups.Add(anyStaffChanged ? group with { Staves = staves.ToImmutable() } : group);
        }

        return anyGroupChanged ? score with { StaffGroups = groups.ToImmutable() } : score;
    }

    /// <summary>
    /// The rewritten voices, or <c>default</c> when none of them carries a meter change.
    /// </summary>
    private static ImmutableArray<Voice> BlankVoices(
        ImmutableArray<Voice> source,
        Dictionary<Voice, Voice> voiceMemo,
        Dictionary<Measure, Measure> measureMemo)
    {
        ImmutableArray<Voice>.Builder? builder = null;
        for (int i = 0; i < source.Length; i++)
        {
            var voice = source[i];
            if (!voiceMemo.TryGetValue(voice, out var rewritten))
            {
                rewritten = BlankVoice(voice, measureMemo);
                voiceMemo[voice] = rewritten;
            }
            if (ReferenceEquals(rewritten, voice))
            {
                builder?.Add(voice);
                continue;
            }
            if (builder == null)
            {
                builder = ImmutableArray.CreateBuilder<Voice>(source.Length);
                for (int j = 0; j < i; j++)
                    builder.Add(source[j]);
            }
            builder.Add(rewritten);
        }
        return builder?.ToImmutable() ?? default;
    }

    private static Voice BlankVoice(Voice voice, Dictionary<Measure, Measure> measureMemo)
    {
        ImmutableArray<Measure>.Builder? builder = null;
        for (int i = 0; i < voice.Measures.Length; i++)
        {
            var measure = voice.Measures[i];
            if (!measureMemo.TryGetValue(measure, out var rewritten))
            {
                rewritten = BlankMeasure(measure);
                measureMemo[measure] = rewritten;
            }
            if (ReferenceEquals(rewritten, measure))
            {
                builder?.Add(measure);
                continue;
            }
            if (builder == null)
            {
                builder = ImmutableArray.CreateBuilder<Measure>(voice.Measures.Length);
                for (int j = 0; j < i; j++)
                    builder.Add(voice.Measures[j]);
            }
            builder.Add(rewritten);
        }
        return builder == null ? voice : voice with { Measures = builder.ToImmutable() };
    }

    private static Measure BlankMeasure(Measure measure)
    {
        ImmutableArray<MusicItem>.Builder? builder = null;
        for (int i = 0; i < measure.Items.Length; i++)
        {
            if (measure.Items[i] is not TimeSignatureChangeItem { Blanked: false } time)
            {
                builder?.Add(measure.Items[i]);
                continue;
            }
            if (builder == null)
            {
                builder = ImmutableArray.CreateBuilder<MusicItem>(measure.Items.Length);
                for (int j = 0; j < i; j++)
                    builder.Add(measure.Items[j]);
            }
            builder.Add(time with { Blanked = true });
        }
        return builder == null ? measure : measure with { Items = builder.ToImmutable() };
    }
}
