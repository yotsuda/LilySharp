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

using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The score's home METER — the top-level <c>time</c> declaration (not one written
/// inside a section / phrase / part block). A section that states no meter of its own
/// REVERTS to it at its boundary, exactly as it reverts to
/// <see cref="ScoreHomeKey"/>'s key: a mid-section <c>time</c> change cannot leak into
/// the next section, nor into the same section played again elsewhere by the form.
/// </summary>
/// <remarks>
/// ⚠️ THE RULE IS THE COLLECTOR'S AND THIS IS ITS TWIN.
/// <c>MeasureCollector.ProcessSectionPrologue</c> owns it for the page (against the
/// per-voice snapshot <c>_sectionResetTimeBeats</c>, taken before any section music is
/// walked); <c>MeasureValidator</c> agrees through the per-block scoping in
/// <c>ValidateItemsScoped</c>. The three EXPORTERS did not: measured 2026-08-31 on
/// <c>section A { c'4 d e f | time 3/4 key g major g a b | } section B { c'4 d e f | }</c>,
/// the page draws bar 3 in 4/4 with a natural, and
/// <list type="bullet">
/// <item><c>lysc ly</c> restored the KEY and not the meter — so the twin handed LilyPond
/// a 3/4 bar holding four quarters;</item>
/// <item><c>lysc xml</c> restored neither;</item>
/// <item><c>lysc midi</c> restored neither.</item>
/// </list>
/// The fixture named for the rule (<c>test/section-meter-resets-to-global</c>) could not
/// catch it: its section A ENDS in the score meter, so the revert changes nothing there.
/// A section that ends in a meter the score does not have is the observable case.
/// <para>
/// The home meter is read the way <see cref="ScoreHomeKey"/> reads the home key — the
/// LAST top-level declaration wins, and a file that writes none is in 4/4.
/// </para>
/// </remarks>
public static class ScoreHomeMeter
{
    /// <summary>Beats and beat type of the score's home meter (4/4 when none is written).</summary>
    public static (int Beats, int BeatType) Read(SyntaxNode root)
    {
        var node = Declaration(root);
        return node is null || node.IsSenzaMisura ? (4, 4) : (node.Beats, node.BeatType);
    }

    /// <summary>
    /// The home meter's DECLARATION node, or null when the file writes none. The
    /// LilyPond exporter re-emits this node verbatim when a section boundary restores
    /// the score meter, so the written spelling (<c>4/4</c> vs <c>C</c>) comes from the
    /// source rather than from a re-derived pair of numbers.
    /// </summary>
    public static TimeSignatureSyntax? Declaration(SyntaxNode root)
    {
        TimeSignatureSyntax? home = null;
        foreach (var time in root.DescendantNodes().OfType<TimeSignatureSyntax>())
            if (!IsInsideMusicContent(time))
                home = time;
        return home;
    }

    // A `time` inside a section/phrase/part is a change, not the score home.
    // Mirrors ScoreHomeKey.IsInsideMusicContent, which owns the same question for the key.
    private static bool IsInsideMusicContent(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PhraseDeclarationSyntax or SectionDeclarationSyntax
                or VariableDeclarationSyntax or PartBlockSyntax)
                return true;
        return false;
    }
}
