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

using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Every annotation name the language reads, each in its one canonical spelling — the
/// union of the tables that consume them (<see cref="ArticulationRegistry"/>,
/// <see cref="MusicMarkItem.ParseMarkName"/>, the validator's plain feature names, the
/// argument-taking names and the dynamics).
/// </summary>
/// <remarks>
/// Names are CASE-SENSITIVE (owner's decision 2026-09-27): every table above matches
/// Ordinal, so a wrong-case name is unknown everywhere downstream and draws nothing.
/// This is the one place that knows all of them, so the diagnostic can name the right
/// spelling (<see cref="CaseOnlyMatch"/>) and a test can pin the tables against the
/// editor's '@' completion.
/// </remarks>
public static class AnnotationNames
{
    private static readonly Lazy<HashSet<string>> AllNames = new(() =>
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        all.UnionWith(ArticulationRegistry.Names);
        all.UnionWith(MusicMarkItem.MarkNames);
        all.UnionWith(AnnotationNameValidator.PlainFeatureNames);
        all.UnionWith(SyntaxFacts.ArgumentTakingAnnotationNames);
        all.UnionWith(SyntaxFacts.DynamicTextLevels.Keys);
        return all;
    });

    /// <summary>All canonical names, dotted compound names (<c>ds.al.fine</c>) included.</summary>
    public static IReadOnlyCollection<string> All => AllNames.Value;

    /// <summary>
    /// The canonical name <paramref name="written"/> differs from ONLY IN CASE, or null
    /// when it is canonical already or matches nothing case-insensitively.
    /// </summary>
    public static string? CaseOnlyMatch(string written)
    {
        if (AllNames.Value.Contains(written))
            return null;
        foreach (var name in AllNames.Value)
            if (string.Equals(name, written, StringComparison.OrdinalIgnoreCase))
                return name;
        return null;
    }
}
