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
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tablature context has no Accidental grob at all, so a tab note neither draws an
/// accidental nor RESERVES room for one.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/engraver-init.ly:1189 (TabVoice) and :1213 (TabStaff) —
/// <c>\remove Accidental_engraver</c>, "No accidental in tablature !".
/// <para>
/// The defect this pins: Lily# decides accidentals once per PART, before the score spec
/// binds that part to a staff, so a part written in F# major and shown as tablature
/// carried naturals nothing drew — and they were NOT inert, because
/// <see cref="SpacingRules.MusicalColumnLeftReach"/> read them. Ledger key
/// <c>line-start.time-to-first-note.tab-keyed</c>, whose LilyPond side is an identity with
/// tab-concert and therefore measures this and nothing else.
/// </para>
/// <para>
/// ⚠️ The boundary is the STAFF, not the score: the same part on a notation staff beside
/// the tab keeps its accidentals, which is what removing an engraver from one context
/// means. Both halves are asserted below, so neither a missing removal nor a blanket one
/// passes.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TabAccidentalRemovalTests
{
    // The tab part opens in F# major, so every written c/d/e/f is spelled with a natural;
    // the notation part beside it stays in C major and spells none. Same pitches — a key
    // never transposes — which is why LilyPond's two staves here are geometrically twins.
    private const string Src = """
        octave absolute
        time 4/4
        key c major

        part gt { clef treble }
        part pn { clef treble }

        section Main {
          gt { key fis major  c,4 d, e, f, | g,2 e, }
          pn { c4 d e f | g2 e }
        }

        form main { Main }

        score main { staff pn  tab gt }
        """;

    private static MultiStaffScore Collect()
    {
        var tree = SyntaxTree.Parse(Src);
        Assert.False(tree.HasErrors,
            string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
    }

    private static List<NoteItem> NotesOf(Staff staff) =>
        staff.PrimaryVoice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();

    private static Staff TheTabStaff(MultiStaffScore score) =>
        score.StaffGroups.SelectMany(g => g.Staves).Single(s => s.IsTab);

    /// <summary>
    /// The tab staff's own copy of the voice carries no accidental — this is the removal
    /// itself, and it fails without it (the tab part is in F# major).
    /// </summary>
    [Fact]
    public void TabStaff_HasNoAccidentalOnAnyNote()
    {
        var notes = NotesOf(TheTabStaff(Collect()));
        Assert.NotEmpty(notes);
        Assert.All(notes, n => Assert.Null(n.Accidental));
        Assert.All(notes, n => Assert.Null(n.EditorialAccidental));
    }

    /// <summary>
    /// …and a tab note therefore reaches left by the plain <c>extra-spacing-width</c> 0.1,
    /// not by an accidental's ~1.23. This is the quantity the ledger point moved on, so it
    /// is asserted rather than left to the snapshot.
    /// </summary>
    [Fact]
    public void TabNote_ReachesLeftByTheBareExtraSpacingWidth()
    {
        foreach (var note in NotesOf(TheTabStaff(Collect())))
            Assert.Equal(SpacingRules.DefaultExtraSpacingWidth,
                SpacingRules.MusicalColumnLeftReach(note), 9);
    }

    /// <summary>
    /// The CONTROL, and the half that stops the removal being applied score-wide: the
    /// notation staff beside it still spells its accidentals. Removing an engraver empties
    /// ONE context. (Its own part is in C major and spells none of its own, so what is
    /// asserted is that its notes are untouched objects — the tab rewrite must not reach
    /// them through the shared voice both staves start from.)
    /// </summary>
    [Fact]
    public void NotationStaffBesideIt_IsUntouched()
    {
        var score = Collect();
        var notation = score.StaffGroups.SelectMany(g => g.Staves).Single(s => !s.IsTab);
        var notes = NotesOf(notation);

        Assert.NotEmpty(notes);
        Assert.All(notes, n => Assert.Null(n.Accidental));   // pn is in C major

        // …and the tab staff is a DIFFERENT voice object, which is what makes the rewrite
        // per-staff rather than per-part.
        Assert.NotSame(notation.PrimaryVoice, TheTabStaff(score).PrimaryVoice);
    }

    /// <summary>
    /// A tab part that spells NO accidental is not rebuilt at all — the removal returns the
    /// same measure instances, so it cannot perturb a score it has nothing to do.
    /// </summary>
    [Fact]
    public void AVoiceWithNoAccidentals_IsReturnedUnchanged()
    {
        var score = Collect();
        var voice = TheTabStaff(score).PrimaryVoice;
        Assert.Same(voice, TabResolver.RemoveAccidentals(voice));
    }
}
