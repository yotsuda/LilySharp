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

using System.Runtime.CompilerServices;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// The shared lyric-binding resolver: which part a lyrics TRACK sings. The
/// binding is a property of the track NAME — <c>lyrics ja sings vocal</c> on any
/// one block binds every block spelled <c>lyrics ja</c>; later blocks may repeat
/// the target identically or omit it, and a different target is a conflict
/// (LYS7005, reported by the validator from <see cref="Conflicts"/>). ONE walk
/// per tree, read by the collector, the exporters and the validators — never a
/// per-walker re-derivation.
/// </summary>
/// <remarks>
/// A track with no <c>sings</c> anywhere is UNBOUND: as a score row it stays the
/// even-spread lead-sheet row; attached via <c>with lyrics</c> it is an error
/// (LYS6009) unless its NAME matches the staff's part or one of that part's
/// voices — the pre-<c>sings</c> name-equality rule, kept because the name IS
/// the voice binding (<c>voice sop { } + lyrics sop { }</c>).
/// </remarks>
public static class LyricBindings
{
    private static readonly ConditionalWeakTable<SyntaxNode, Dictionary<string, string>> Maps = new();

    /// <summary>The part the named track sings, or null when no block of that
    /// name declares a binding.</summary>
    public static string? TargetOf(SyntaxNode root, string trackName)
    {
        while (root.Parent != null)
            root = root.Parent;
        var map = Maps.GetValue(root, BuildMap);
        return map.TryGetValue(trackName, out var target) ? target : null;
    }

    /// <summary>Every block that declares a target DIFFERENT from its track's
    /// first-declared one — the validator's input for LYS7005.</summary>
    public static IEnumerable<(LyricsBlockSyntax Block, string Target, string First)> Conflicts(SyntaxNode root)
    {
        while (root.Parent != null)
            root = root.Parent;
        var first = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in root.DescendantNodes().OfType<LyricsBlockSyntax>())
        {
            if (block.VoiceName is not { } name || block.SingsTarget is not { } target)
                continue;
            if (first.TryGetValue(name, out var t0))
            {
                if (!string.Equals(t0, target, StringComparison.Ordinal))
                    yield return (block, target, t0);
            }
            else
            {
                first[name] = target;
            }
        }
    }

    private static Dictionary<string, string> BuildMap(SyntaxNode root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in root.DescendantNodes().OfType<LyricsBlockSyntax>())
        {
            if (block.VoiceName is { } name && block.SingsTarget is { } target
                && !map.ContainsKey(name))
                map[name] = target;
        }
        return map;
    }
}
