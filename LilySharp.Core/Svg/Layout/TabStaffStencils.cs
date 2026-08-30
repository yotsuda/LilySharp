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
/// The grob families a NUMBERS-ONLY tablature staff prints nothing for. A `tab … as
/// numbers` line carries the fret digits and the gestures bound to them; the markup a
/// reader takes from the notation staff standing above it — the dynamics, the scripts, the
/// rit. — belongs to that staff alone, and a tab line repeating it prints the same
/// annotation twice.
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
/// ⚠️⚠️ ★ <b>THE CRITERION IS <see cref="Staff.TabNumbersOnly"/>, NOT "IS A TAB"</b>
/// (reader decision, 2026-08-30). LilyPond blanks these on EVERY <c>TabStaff</c>, which
/// costs a tab-only score its markup entirely — no <c>@text</c>, no dynamic, no
/// <c>rit.</c> — where it used to print each once. Lily# already has the distinction
/// LilyPond lacks, and it already means exactly the right thing:
/// <c>RenderSpecParser.StaffRenderedParts</c>'s own rule (reader decision, 2026-08-29) is
/// "a tab paired with a notation staff needs fret digits only, because the staff above
/// carries the meter, the rests, the dots, the stems and the ties; a tab standing alone has
/// to carry all of it itself". The markup families are more of that same list, so they
/// belong to that same switch — and the writer can already override it per tab with
/// <c>as numbers</c> / <c>as full</c>.
/// ⇒ <c>staff m</c> + <c>tab m</c> (numbers by default) blanks and the duplicate is gone;
/// <c>tab m</c> alone (full by default) keeps everything.
/// ⚠️ <b>THE ONE COMBINATION THAT STILL PRINTS TWICE</b> is an EXPLICIT
/// <c>tab m as full</c> beside <c>staff m</c> — the writer asked for a complete tab, and a
/// complete tab carries its own markup. That is the writer's choice showing through, not a
/// defect; it is the same reading under which the technique letters below stay.
/// </para>
/// <para>
/// ⚠️ <b>AT LAYOUT, NOT AT COLLECTION.</b> LilyPond's engravers still RUN on a
/// <c>TabStaff</c> — only the stencils are blank — and Lily# must keep the items for the
/// same reason: a <see cref="DynamicItem"/> drives MIDI velocity, and the <c>.ly</c> and
/// MusicXML exporters write from the score model. Dropping these at collection would
/// silence a score's dynamics in three outputs the reader never sees the staff in.
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
/// <item><b><c>Script</c> IS BLANKED ON A NUMBERS-ONLY TAB ONLY</b> (reader report,
/// 2026-08-30: "an @accent is showing on an `as numbers` tab; it should not"). LilyPond
/// blanks it on every TabStaff at :1284; Lily# engraves scripts on a FULL tab deliberately,
/// with placement of its own (the fret-digit centring in <c>ArticulationEngraver</c>) and
/// six tracked fixtures plus <c>TabScriptStemClearanceTests</c> — all of them tab-only or
/// explicitly <c>as full</c>, so all of them keep their scripts.
/// ⚠️⚠️ <b>EXCEPT THE TAB TECHNIQUE LETTERS</b>, which are the reason this is asked per
/// ITEM and not per type. <c>@tap</c>, <c>@hammeron</c>, <c>@pulloff</c> and
/// <c>@pluck</c>'s finger letter are TABLATURE ink — a guitarist reads T/H/P/p-i-m-a off
/// the tab, not off the staff above — and <c>test/tab-technique-letters</c> is a
/// NUMBERS-ONLY tab written for exactly them, after a reader reported one drawn into its
/// own notehead (2026-08-28). Blanking Scripts wholesale would have deleted that fixture's
/// entire subject. <see cref="ArticulationEngraver.TabTechniqueLetterOf"/> is already the
/// one home for "is this a technique letter", so this reads it rather than spelling the
/// four types again.</item>
/// <item>The four lines ABOVE this block in the same LilyPond context (<c>Tie</c>,
/// <c>RepeatTie</c>, <c>LaissezVibrerTie</c>, <c>PhrasingSlur</c>) are likewise not ported:
/// Lily# draws ties on a tab staff, with <c>test/tab-tie</c> and
/// <c>test/tab-chord-tie</c>.</item>
/// </list>
/// ⇒ Blanking the tie families is a PRODUCT DECISION about what a Lily# tab line shows,
/// not a defect. It moves ink, and it belongs in a change that says so.
/// </para>
/// <para>
/// ★ <b>WHAT THE NUMBERS-ONLY CRITERION BOUGHT, MEASURED.</b> The first cut of this file
/// blanked on every tab, and the price was named rather than hidden: a tab-only score lost
/// its markup entirely. <c>scratch/ベースタブLy/奏（かなで）.lys</c> showed both faces at
/// once — its <c>"both"</c> score drew <c>@text("人差し指で")</c> TWICE and now draws it
/// once (the defect), while its <c>"tab"</c> score drew it once and would have drawn it not
/// at all. The reader took the decision the same day (HANDOFF §2 U12): keep it. Gating on
/// <see cref="Staff.TabNumbersOnly"/> keeps it AND fixes the duplicate, because that flag
/// already defaults to precisely "this part is also on a notation staff".
/// ⇒ Both answers are pinned by <c>TabStaffStencilTests</c>, so either one changing is a
/// test edit and not a silent drift.
/// </para>
/// </remarks>
internal static class TabStaffStencils
{
    private static readonly ConditionalWeakTable<MultiStaffScore, TabStaffSet> _byScore = new();

    /// <summary>The global staff indices of a score's NUMBERS-ONLY tab staves, cut once per
    /// score.</summary>
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
                if (Blanks(staff))
                    set.Indices.Add(index);
            return set;
        });

    /// <summary>
    /// True when the staff at <paramref name="staffIndex"/> blanks the markup families of
    /// the table above — i.e. it is a NUMBERS-ONLY tab staff.
    /// </summary>
    internal static bool Blanks(MultiStaffScore score, int staffIndex)
    {
        var set = SetOf(score);
        return set.Indices.Count > 0 && set.Indices.Contains(staffIndex);
    }

    /// <summary>True when <paramref name="staff"/> blanks those families.</summary>
    /// <remarks>
    /// ⚠️ <c>TabNumbersOnly</c>, not <c>IsTab</c> — see the class remarks. A FULL tab has to
    /// carry its own markup because no notation staff is carrying it for the reader.
    /// </remarks>
    internal static bool Blanks(Staff? staff) => staff is { IsTab: true, TabNumbersOnly: true };

    /// <summary>
    /// True when this articulation prints nothing because it is an ordinary
    /// <c>Script</c> on a numbers-only tab — the <c>\override Script.stencil = ##f</c> arm.
    /// </summary>
    /// <remarks>
    /// ⚠️ THREE KINDS OF ARTICULATION, AND ONLY THE MIDDLE ONE GOES:
    /// <list type="bullet">
    /// <item>The BEND-AFTER gestures (<c>Fall</c>/<c>Doit</c>/<c>Bend</c>/<c>Scoop</c>/
    /// <c>Plop</c>) and the breath marks are a <c>BendAfter</c> / <c>BreathingSign</c> grob,
    /// NEITHER of which LilyPond's TabStaff block touches — and a bend is a tablature
    /// gesture above all. <see cref="ArticulationEngraver.IsSidePositionedScript"/> is
    /// already exactly that partition, so this reads it.</item>
    /// <item>The TAB TECHNIQUE LETTERS (T/H/P and <c>@pluck</c>'s finger) are what the tab
    /// is FOR: <c>test/tab-technique-letters</c> is a numbers-only tab written for them
    /// after a reader reported one drawn into its own notehead. Asked per ITEM because
    /// <c>@pluck</c> carries its letter on the item.</item>
    /// <item>Everything else — accent, staccato, tenuto, marcato, fermata, trill — is the
    /// markup the staff above already carries, and is what the reader asked to stop seeing
    /// twice.</item>
    /// </list>
    /// </remarks>
    internal static bool BlanksScript(Staff? staff, ArticulationItem articulation)
        => Blanks(staff)
           && ArticulationEngraver.IsSidePositionedScript(articulation.Type)
           && ArticulationEngraver.TabTechniqueLetterOf(articulation) is null;

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
