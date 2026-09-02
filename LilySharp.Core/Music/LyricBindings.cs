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
/// binding has two sites, and they are NOT the same property (user decision,
/// 2026-09-02 — it replaces the 2026-08-19 "one track property, two spellings"
/// rule):
/// <list type="bullet">
/// <item>the DEFINITION states the track's DEFAULT melody —
/// <c>lyrics ja sings vocal { … }</c> on any one block binds every block spelled
/// <c>lyrics ja</c>; later definition blocks may repeat the target identically
/// or omit it, and a different target is a conflict (LYS7005, reported by the
/// validator from <see cref="Conflicts"/>);</item>
/// <item>a SCORE ROW binds ITS OWN placement — <c>lyrics ja sings alt</c> puts
/// this row's syllables at <c>alt</c>'s rhythm whatever the definition says,
/// which is how ONE verse serves every staff of a chorale
/// (<c>staff sop  lyrics verse sings sop  staff alt  lyrics verse sings alt …</c>).
/// A row without <c>sings</c> takes the definition's default
/// (<see cref="TargetOfRow"/>).</item>
/// </list>
/// ONE walk per tree, read by the collector, the exporters and the validators —
/// never a per-walker re-derivation.
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

    /// <summary>The track's DEFAULT melody: the part the first definition block
    /// of that name says it sings, or null when no definition block of that name
    /// declares a binding. A score row's own <c>sings</c> is not consulted here —
    /// that is the row's placement, read by <see cref="TargetOfRow"/>.</summary>
    public static string? TargetOf(SyntaxNode root, string trackName)
    {
        while (root.Parent != null)
            root = root.Parent;
        var map = Maps.GetValue(root, BuildMap);
        return map.TryGetValue(trackName, out var target) ? target : null;
    }

    /// <summary>The part ONE placed row sings: the row's own <c>sings</c> when
    /// it writes one, else the track's default (<see cref="TargetOf"/>), else
    /// null (unbound — the even-spread lead-sheet row). The one reader of a
    /// row's target: the fold in RenderSpecParser, the group validator and the
    /// collector all ask this, so a row cannot fold under one staff and sing
    /// another.</summary>
    public static string? TargetOfRow(SyntaxNode root, string trackName, string? rowSings)
        => rowSings ?? TargetOf(root, trackName);

    /// <summary>The track name and stated target of a DEFINITION block
    /// (<c>lyrics ja sings vocal { … }</c>) — the site that states the track's
    /// default. A score row's <c>sings</c> is deliberately NOT read here: it
    /// binds only its own placement, so it neither sets the default nor
    /// conflicts with it.</summary>
    private static (string? Name, string? Target) DefinitionBindingOf(SyntaxNode node) => node switch
    {
        LyricsBlockSyntax b => (b.VoiceName, b.SingsTarget),
        _ => (null, null),
    };

    /// <summary>Every DEFINITION block that states a target DIFFERENT from its
    /// track's first-declared one — the validator's input for LYS7005. The node
    /// is a <see cref="LyricsBlockSyntax"/>. Score rows never conflict: each
    /// binds its own placement.</summary>
    public static IEnumerable<(SyntaxNode Node, string Target, string First)> Conflicts(SyntaxNode root)
    {
        while (root.Parent != null)
            root = root.Parent;
        var first = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
        {
            if (DefinitionBindingOf(node) is not ({ } name, { } target))
                continue;
            if (first.TryGetValue(name, out var t0))
            {
                if (!string.Equals(t0, target, StringComparison.Ordinal))
                    yield return (node, target, t0);
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
        foreach (var node in root.DescendantNodes())
            if (DefinitionBindingOf(node) is ({ } name, { } target) && !map.ContainsKey(name))
                map[name] = target;
        return map;
    }

    private static readonly ConditionalWeakTable<SyntaxNode, Dictionary<string, HashSet<string>>> VoiceMaps = new();

    /// <summary>
    /// The named voices written inside the named part's music (section cells and
    /// part-major inner sections alike) — the other half of the binding rule: a
    /// track binds to a part by <c>sings</c>, or by NAME to the part or one of
    /// these voices (<c>voice sop { } + lyrics sop { }</c>). One walk per tree,
    /// shared by the validator and the score-row folding in RenderSpecParser.
    /// </summary>
    public static IReadOnlySet<string> VoicesOfPart(SyntaxNode root, string partName)
    {
        while (root.Parent != null)
            root = root.Parent;
        var map = VoiceMaps.GetValue(root, BuildVoiceMap);
        return map.TryGetValue(partName, out var voices) ? voices : EmptyVoices;
    }

    private static readonly HashSet<string> EmptyVoices = new(StringComparer.Ordinal);

    private static Dictionary<string, HashSet<string>> BuildVoiceMap(SyntaxNode root)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var n in root.DescendantNodes())
        {
            string? part = n switch
            {
                PartBlockSyntax pb => pb.PartName.Text,
                PartDeclarationSyntax pd => pd.Name.Text,
                _ => null,
            };
            if (part == null) continue;
            foreach (var par in n.DescendantNodes().OfType<ParallelExpressionSyntax>())
                foreach (var (vn, _) in par.NamedVoices)
                    if (vn is { Length: > 0 })
                    {
                        if (!map.TryGetValue(part, out var set))
                            map[part] = set = new HashSet<string>(StringComparer.Ordinal);
                        set.Add(vn);
                    }
        }
        return map;
    }
}
