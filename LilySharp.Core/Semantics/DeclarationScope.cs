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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Where a directive (<c>time</c>, <c>key</c>, <c>clef</c>, <c>tempo</c>, <c>octave</c>,
/// <c>partial</c>) stands: at the FILE's top level, or inside music content — a phrase or
/// variable body, a section, a part's cell — where it is a change within the piece and not the
/// score's own setting.
/// </summary>
/// <remarks>
/// ONE spelling of the question (HANDOFF §2 R15 ⒠, session 1123): until then the collector
/// (<c>MeasureCollector.CollectDefinitions</c>), the MusicXML exporter's metadata pass,
/// <see cref="ScoreHomeKey"/> and <see cref="ScoreHomeMeter"/> each walked the parents with their
/// own list, two of them counting a PART HEADER (<c>part p { key bes major }</c>) as music and
/// two not — a per-part default is not the file's setting for the collector and for the key's
/// <c>Read</c>, but IS the file's last word for <c>ScoreHomeKey.Sharps</c> and for the MusicXML's
/// metadata (which applies part headers again per part). The flag states which reading a caller
/// takes; the container list is stated here and nowhere else.
/// </remarks>
internal static class DeclarationScope
{
    /// <summary>
    /// True when <paramref name="node"/> is nested inside a phrase or variable body, a section
    /// or a part cell — and, when <paramref name="partHeaderIsMusic"/>, inside a <c>part</c>
    /// declaration's header too.
    /// </summary>
    public static bool IsInsideMusicContent(SyntaxNode node, bool partHeaderIsMusic)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PhraseDeclarationSyntax or SectionDeclarationSyntax
                    or VariableDeclarationSyntax or PartBlockSyntax
                || (partHeaderIsMusic && p is PartDeclarationSyntax))
                return true;
        return false;
    }
}
