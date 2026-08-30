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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The grob families a tablature staff prints nothing for. A tab line carries the fret
/// digits and the gestures bound to them; the markup a reader takes from the notation
/// staff standing above it — the dynamics, the scripts, the rit. — belongs to that staff
/// alone, and a tab line repeating it prints the same annotation twice.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: ly/engraver-init.ly:1277-1285 Tab_staff_symbol_engraver — the last block
/// of the <c>TabStaff</c> context that engraver opens, transcribed here whole:
/// <code>
///   %% ... and all kinds of markups, spanners etc.
///   \override TupletBracket.stencil = ##f
///   \override TupletNumber.stencil = ##f
///   \override DynamicText.stencil = ##f
///   \override DynamicTextSpanner.stencil = ##f
///   \override TextSpanner.stencil = ##f
///   \override Hairpin.stencil = ##f
///   \override Script.stencil = ##f
///   \override TextScript.stencil = ##f
/// </code>
/// </para>
/// <para>
/// ⚠️⚠️ <b>A BLANKED STENCIL IS NOT MERELY INVISIBLE.</b> A grob whose stencil is
/// <c>##f</c> has an EMPTY EXTENT, so it joins no skyline and reserves no outside-staff
/// space (LILYPOND-REF: lily/grob.cc Grob::extent — an unset stencil yields an empty
/// interval; lily/axis-group-interface.cc:864-989 skyline_spacing walks only grobs that
/// answer with one). That is the whole defect this table closes, and it is why the
/// blanking runs at LAYOUT rather than at draw: what pushed a tab staff's <c>rit.</c> up
/// through the chord row and into the notation staff above it was the RESERVED BAND, not
/// the ink. Suppressing only the draw would leave the gap the ink used to sit in.
/// </para>
/// <para>
/// ⚠️ <b>AT LAYOUT, NOT AT COLLECTION.</b> LilyPond's engravers still RUN on a
/// <c>TabStaff</c> — only the stencils are blank — and Lily# must keep the items for the
/// same reason: a <see cref="DynamicItem"/> drives MIDI velocity, and the <c>.ly</c> and
/// MusicXML exporters write from the score model. Dropping these at collection would
/// silence a tab-only score's dynamics in three outputs the reader never sees the staff in.
/// </para>
/// <para>
/// ⚠️ <b>WHY IT IS CONSULTED IN SEVERAL PLACES AND NOT FILTERED IN ONE.</b> LilyPond has
/// no single filter either: <c>stencil = ##f</c> is a context property read wherever a
/// grob is asked for its stencil or its extent. Lily# asks in two passes — the INK half
/// (<c>LayoutEngine.Annotations</c>, which feeds the renderer and the outside-staff
/// stacker) and the RESERVATION half (<c>MultiStaffLayouter.BuildAllStaffSkylines</c>, via
/// the staff-keyed buckets in <see cref="ScoreSideTables"/>) — so both ask HERE. The table
/// is the one home (HANDOFF §5.2.1②); the asking is not.
/// </para>
/// <para>
/// ★ <b>NOT IN THIS TABLE, ON PURPOSE — AND THE LINE BETWEEN THE TWO KINDS.</b> What this
/// file blanks is markup a tab line REPEATS from the notation staff above it, which no
/// Lily# fixture ever asked for. What it leaves alone is markup Lily# has DECIDED a tab
/// line shows, and each of those decisions is written down in tracked snapshots:
/// <list type="bullet">
/// <item><c>TupletBracket</c>/<c>TupletNumber</c> are in LilyPond's block and were already
/// blank on a Lily# tab staff before this file existed (<c>TupletBracketEngraver</c>), so
/// nothing here re-states them.</item>
/// <item><c>DynamicTextSpanner</c> has no separate Lily# spelling — <c>@cresc</c> becomes a
/// Hairpin — so the Hairpin arm covers it.</item>
/// <item><b><c>Script</c> IS NOT BLANKED</b>, though LilyPond blanks it at :1284. Lily#
/// engraves scripts on a tab line deliberately, with placement of its own (the fret-digit
/// centring in <c>ArticulationEngraver</c>) and SEVEN tracked fixtures plus a named
/// clearance test — <c>test/tab-articulations</c>, <c>-multistaff</c>,
/// <c>test/tab-beam-script</c>, <c>test/tab-staccato-beam-side</c>,
/// <c>test/tab-forced-script-side</c>, <c>test/tab-beam-slope</c>,
/// <c>test/tab-technique-letters</c>, <c>TabScriptStemClearanceTests</c>. MEASURED: adding
/// the <c>Script</c> arm turns all eight red.</item>
/// <item>The four lines ABOVE this block in the same LilyPond context (<c>Tie</c>,
/// <c>RepeatTie</c>, <c>LaissezVibrerTie</c>, <c>PhrasingSlur</c>) are likewise not ported:
/// Lily# draws ties on a tab staff, with <c>test/tab-tie</c> and
/// <c>test/tab-chord-tie</c>.</item>
/// </list>
/// ⇒ Blanking either of those last two is a PRODUCT DECISION about what a Lily# tab line
/// shows, not a defect. It moves ink, and it belongs in a change that says so — not in
/// this one, which moves none.
/// </para>
/// <para>
/// ⚠️⚠️ ★ <b>THE ONE PLACE THIS COSTS THE READER SOMETHING, NAMED RATHER THAN HIDDEN.</b>
/// LilyPond's blanking is a CONTEXT property, so it applies whether or not a notation staff
/// stands beside the tab — and a score whose only line is a tab therefore prints no
/// <c>@text</c>, no dynamic and no <c>rit.</c> AT ALL, where before it printed them once.
/// MEASURED (2026-08-30, 1645 books): the whole corpus reach of this file is TWO books,
/// both the user's own — <c>scratch/ベースタブLy/Untitled-6.lys</c> (the reported one) and
/// <c>scratch/ベースタブLy/奏（かなで）.lys</c>, whose three score blocks show both faces:
/// its <c>"both"</c> score drew <c>@text("人差し指で")</c> TWICE and now draws it once (the
/// defect), while its <c>"tab"</c> score drew it once and now draws it not at all (this
/// paragraph). The corpus sweep sees only the first book because it renders each file's
/// DEFAULT score, and the second file's default score has no tab.
/// ⇒ ★ If a tab-only score should keep its markup, the change is to gate
/// <see cref="Blanks(MultiStaffScore, int)"/> on the part ALSO being shown on a notation
/// staff in the same score — one condition, in this one place. That is a LilyPond
/// DIVERGENCE (LilyPond has no such rule; a LilyPond user reverts the stencil per score),
/// so it is the writer's decision to take, not this file's to assume.
/// <c>TabStaffStencilTests.ATabOnlyScorePrintsNoBlankedMarkupEither</c> pins the current
/// answer either way, so changing it is a test edit and not a silent drift.
/// </para>
/// </remarks>
internal static class TabStaffStencils
{
    private static readonly ConditionalWeakTable<MultiStaffScore, TabStaffSet> _byScore = new();

    /// <summary>The global staff indices of a score's tab staves, cut once per score.</summary>
    /// <remarks>
    /// Memoised for the same reason <see cref="ScoreSideTables"/>'s staff-keyed buckets
    /// are: the reservation pass asks per (system, staff), so a walk of the staff list on
    /// every ask would be paid S×systems times on every keystroke.
    /// </remarks>
    private sealed class TabStaffSet
    {
        internal readonly HashSet<int> Indices = new();
    }

    private static TabStaffSet SetOf(MultiStaffScore score)
        => _byScore.GetValue(score, static s =>
        {
            var set = new TabStaffSet();
            foreach (var (_, staff, index) in s.EnumerateStaves())
                if (staff.IsTab)
                    set.Indices.Add(index);
            return set;
        });

    /// <summary>
    /// True when the staff at <paramref name="staffIndex"/> blanks the markup families of
    /// the table above — i.e. it is a tab staff.
    /// </summary>
    internal static bool Blanks(MultiStaffScore score, int staffIndex)
    {
        var set = SetOf(score);
        return set.Indices.Count > 0 && set.Indices.Contains(staffIndex);
    }

    /// <summary>True when <paramref name="staff"/> blanks those families.</summary>
    internal static bool Blanks(Staff? staff) => staff is { IsTab: true };

    /// <summary>
    /// <paramref name="items"/> without the ones a tab staff blanks — the DynamicText /
    /// TextScript / TextSpanner / Hairpin arms of the table, whichever the caller holds.
    /// Returns the input untouched when the score has no tab staff, so the ordinary score
    /// pays one dictionary hit and allocates nothing.
    /// </summary>
    /// <remarks>
    /// ⚠️ Filters what the caller holds — a LAYOUT list where the engraver keyed its
    /// <c>SourceIndex</c> off the input's POSITION (dynamics), an ITEM list where the item
    /// carries its own <c>SourceIndex</c> (hairpins, text spanners). Never filter a score
    /// side table itself: <c>SharedRenderer.ResolveDataPos</c> re-derives every data-pos by
    /// indexing those, and a shifted index hands the editor a click that jumps to the wrong
    /// character.
    /// </remarks>
    internal static ImmutableArray<T> Blank<T>(
        MultiStaffScore? score, ImmutableArray<T> items, Func<T, int> staffOf)
    {
        if (score == null || items.IsDefaultOrEmpty)
            return items;
        var set = SetOf(score);
        if (set.Indices.Count == 0)
            return items;

        // Two passes so the common answer — a tab staff that carries none of this
        // markup — returns the input array itself rather than a copy of it.
        bool any = false;
        foreach (var item in items)
            if (set.Indices.Contains(staffOf(item))) { any = true; break; }
        if (!any)
            return items;

        var kept = ImmutableArray.CreateBuilder<T>(items.Length);
        foreach (var item in items)
            if (!set.Indices.Contains(staffOf(item)))
                kept.Add(item);
        return kept.ToImmutable();
    }

}
