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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The <c>marks</c> display option — how a boxed section label and the metronome mark
/// standing at the same bar are arranged — read from its two positions and answered as
/// the one bit the layout wants.
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
/// Two positions, the shape <c>fonts</c> and <c>paper</c> take: written at the top level it
/// is the file's default; written as a score item (<c>score main { marks beside … }</c>) it
/// is that score's own and replaces the default for that score alone. There is no part or
/// section position — the arrangement is a property of the page, and a page prints one
/// way. The words are a closed vocabulary and bare, like <c>octave absolute</c>; a third
/// word is refused where it stands (Parser.ParseMarksDirective).
/// </para>
/// <para>
/// ⚠️ NOT an <c>override</c> and NOT a <c>paper</c> key, deliberately (user decision
/// 2026-09-02): <c>override</c> reads a <c>once</c> / section scope that a whole-score
/// quantity would silently ignore, and <c>paper { }</c> is the page's DIMENSIONS and says
/// so ("the line/page-breaking algorithm switches" are excluded there for the same reason).
/// </para>
/// </remarks>
public static class MarkArrangement
{
    /// <summary>The keyword, in both positions.</summary>
    public const string Property = "marks";

    /// <summary>LilyPond's arrangement — the label stacked over the tempo — the default.</summary>
    public const string Stacked = "stacked";

    /// <summary>The chart's — the label at the line start with the tempo to its right.</summary>
    public const string Beside = "beside";

    /// <summary>The two words <c>marks</c> takes, the default first, beside the parser
    /// that refuses a third.</summary>
    public static readonly IReadOnlyList<string> Modes = [Stacked, Beside];

    /// <summary>
    /// Reads a <c>marks</c> property: true for <c>beside</c>, false for <c>stacked</c>, null
    /// when the node is not a marks property or carries a word outside the two (the parser
    /// has reported that one already).
    /// </summary>
    public static bool? ReadProperty(PropertyAssignmentSyntax prop)
    {
        if (!string.Equals(prop.NameToken.Text, Property, StringComparison.Ordinal))
            return null;
        return prop.ValueText switch
        {
            Beside => true,
            Stacked => false,
            _ => null,
        };
    }

    /// <summary>
    /// Whether the file's default arrangement is <c>beside</c>: the LAST top-level
    /// <c>marks</c> directive says so (the last wins, like every repeated global; the
    /// duplicate is warned about). A directive inside a score body is that score's own
    /// (<see cref="ScoreArrangement"/>) and is not the file's.
    /// </summary>
    /// <remarks>
    /// Green finder rather than a red walk, the reason <see cref="ConcertPitch.FileIsConcert"/>
    /// gives: this is asked per collect, per keystroke.
    /// </remarks>
    public static bool FileIsBeside(SyntaxNode root)
    {
        bool? last = null;
        foreach (var prop in root.GreenSites(
                     static g => (g.Kind == SyntaxKind.PropertyAssignment, Descend: true)))
            if (prop is PropertyAssignmentSyntax pa
                && !pa.IsInside<PartDeclarationSyntax>() && !pa.IsInside<RenderDeclarationSyntax>()
                && ReadProperty(pa) is { } mode)
                last = mode;
        return last ?? false;
    }

    /// <summary>
    /// The arrangement <paramref name="render"/> asks for itself — true for <c>beside</c>,
    /// false for <c>stacked</c>, null when the score body writes no <c>marks</c> item and the
    /// file's default applies. The LAST item wins, like every repeated single-value setting.
    /// </summary>
    public static bool? ScoreArrangement(RenderDeclarationSyntax? render)
    {
        if (render == null) return null;
        bool? last = null;
        foreach (var node in render.DescendantNodes())
            if (node is PropertyAssignmentSyntax pa && ReadProperty(pa) is { } mode)
                last = mode;
        return last;
    }
}
