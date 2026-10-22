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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns at an <c>@cross</c> whose note has no other staff to move to
/// (<see cref="DiagnosticCodes.CrossStaffWithoutPartner"/>): the part it is written in is
/// drawn by scores that give it one staff and no neighbour, so the page draws the note where
/// it stands and <c>CrossStaffEngraver.Calculate</c> answers nothing (<c>staffCount &lt; 2</c>).
/// </summary>
/// <remarks>
/// Owner's decision 2026-10-23 (session 966, choice (a) of three): until then a single-staff
/// <c>@cross</c> was silent, and the owner's own <c>corpora/dogfood/blues.lys</c> had written
/// <c>b4@cross</c> on a lead sheet meaning an x head — which is <c>@dead</c> or
/// <c>@notehead(cross)</c>, so the message names those.
/// <para>
/// WHAT COUNTS AS A PARTNER is what the engraver counts: any other NOTATION staff of the
/// score (LayoutEngine hands it the score's whole staff count, not the grand staff's), so a
/// part standing beside an unrelated staff is not reported — the page does move its note.
/// The staves are counted off the score's items (<see cref="NotationStaves"/>): a staff, tab,
/// ossia, condensed or combined staff one each, a group its members' — and a chords or
/// lyrics ROW none, although it is a voice binding of its own
/// (<see cref="RenderSpec.GetVoiceBindings"/>). ⒝ The engraver counts rows among the staves
/// (<c>score.StaffGroups.Sum(g =&gt; g.StaffCount)</c>), so under a lead sheet's chord row it
/// files the note for "staff 1" — the row — and the page draws the note where it stands
/// (MEASURED, Lab sessions/p966/row.lys: nothing moves). The warning is right there too.
/// </para>
/// <para>
/// ⚠️ ONLY A NOTE WHOSE PART IS KNOWN FROM THE SYNTAX (<see cref="ChordDiagramScores.PartNameOf"/>):
/// an <c>@cross</c> in a phrase can be played by any part and is not judged; a part no score
/// draws is not judged either (its note is drawn nowhere, which other diagnostics own).
/// </para>
/// </remarks>
internal sealed class CrossStaffValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        List<(ArticulationSyntax Mark, string Part)>? crosses = null;
        foreach (var node in tree.GetRoot().DescendantNodes<ArticulationSyntax>())
        {
            if (node.Name != "cross")
                continue;
            if (ChordDiagramScores.PartNameOf(node) is { } part)
                (crosses ??= new()).Add((node, part));
        }
        if (crosses is null)
            return;

        // Per part: whether any score draws it, and whether one of those scores has a staff
        // beside it. Read once for the file — a book writes few @cross and fewer scores.
        var scores = RenderSpecParser.FindAll(tree);
        var verdict = new Dictionary<string, (bool Drawn, bool HasPartner)>(System.StringComparer.Ordinal);
        foreach (var (_, part) in crosses)
        {
            if (verdict.ContainsKey(part))
                continue;
            bool drawn = false, partner = false;
            foreach (var score in scores)
            {
                bool here = false;
                foreach (var (voiceName, _, _, _, _) in score.GetVoiceBindings())
                    if (voiceName == part)
                        here = true;
                if (!here)
                    continue;
                drawn = true;
                if (NotationStaves(score.Items) >= 2)
                    partner = true;
            }
            verdict[part] = (drawn, partner);
        }

        foreach (var (mark, part) in crosses)
        {
            var (drawn, partner) = verdict[part];
            if (!drawn || partner)
                continue;
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(mark.Span, DiagnosticCodes.CrossStaffWithoutPartner,
                $"'@cross' moves the note to the other staff of a grand staff, and every score that draws '{part}' "
                + "gives it one staff and no neighbour - the note stays where it is. For an x notehead write "
                + "'@dead' or '@notehead(cross)'; to move the note, put the part in a grandStaff { } beside another staff.");
        }
    }

    /// <summary>The NOTATION staves a score's items build — a staff, tab, ossia, condensed or
    /// combined staff one each, a group its members' — rows excluded: a chords or lyrics row
    /// is a voice binding of its own (<see cref="RenderSpec.GetVoiceBindings"/>) but no staff
    /// a note can move to.</summary>
    private static int NotationStaves(System.Collections.Immutable.ImmutableArray<RenderItemSpec> items)
    {
        int count = 0;
        foreach (var item in items)
            count += item switch
            {
                GrandStaffRenderSpec group => NotationStaves(group.GrandStaff.Members),
                SingleStaffSpec or TabStaffSpec or OssiaStaffSpec
                    or CondensedStaffSpec or CombinedStaffSpec => 1,
                _ => 0,
            };
        return count;
    }
}
