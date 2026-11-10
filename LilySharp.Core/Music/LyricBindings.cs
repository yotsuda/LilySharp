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
/// The shared lyric-binding resolver: which part a lyrics TRACK follows. A score row places
/// the words, with its own <c>on</c> or under the staff nearest above it
/// (docs/lyrics-singer-design.md); a row with neither stays the even-spread lead-sheet row.
/// ONE walk per tree, read by the collector, the exporters and the validators —
/// never a per-walker re-derivation.
/// </summary>
/// <remarks>
/// A track's NAME binds nothing, and the name of a part or a named voice is refused for one
/// (LYS7008, owner's decision 2026-11-10).
/// </remarks>
public static class LyricBindings
{
    private static readonly ConditionalWeakTable<SyntaxNode, Dictionary<string, string>> Maps = new();

    /// <summary>The track's melody: the part the first score row places it on, or null. A
    /// score's own <c>on</c> is that row's placement, read by <see cref="TargetOfRow"/>.</summary>
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

    private static Dictionary<string, string> BuildMap(SyntaxNode root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        bool unnamedTrack = false;
        foreach (var node in root.SitesOfKinds(SyntaxKind.LyricsBlock).OfType<LyricsBlockSyntax>())
            if (node.NameToken == null)
                unnamedTrack = true;
        // Where the score places it (docs/lyrics-singer-design.md): the first row's melody, what
        // the validators and the composing checks mean by a track's part. A track can be
        // placed on several parts; each row knows its own (RenderSpecParser.RowTarget).
        foreach (var row in root.SitesOfKinds(SyntaxKind.LyricsRowRender).OfType<LyricsRowRenderSyntax>())
            if ((row.SingsTarget ?? Svg.Collector.RenderSpecParser.NearestStaffAbove(row, out _)) is { } placed)
                map.TryAdd(row.PartName, placed);
        // The UNNAMED track that states no `sings` (2026-10-09) sings the file's one part: the
        // unnamed part, the only declared one, or — a file that declares none and writes its
        // music straight into its sections — the unnamed part a bare `staff` renders. With
        // several parts it stays unbound, like any track that names no melody.
        if (unnamedTrack && !map.ContainsKey(SyntaxFacts.UnnamedLyricsName))
        {
            var parts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in root.SitesOfKinds(Editing.PartReferenceFinder.DeclaringKinds))
                if (Editing.PartReferenceFinder.DeclaredName(n) is { } declared)
                    parts.Add(declared.Text);
            if (parts.Count <= 1)
                map[SyntaxFacts.UnnamedLyricsName] = parts.Count == 1 ? parts.First() : SyntaxFacts.UnnamedPartName;
        }
        return map;
    }

    private static readonly ConditionalWeakTable<SyntaxNode, Dictionary<string, Dictionary<string, int>>> VoiceMaps = new();

    /// <summary>
    /// The named voices written inside the named part's music (section cells and
    /// by-part inner sections alike) — the other half of the binding rule: a
    /// track binds to a part by <c>sings</c>, or by NAME to the part or one of
    /// these voices (<c>voice sop { } + lyrics sop { }</c>). One walk per tree,
    /// shared by the validator and the score-row folding in RenderSpecParser.
    /// </summary>
    public static IReadOnlyCollection<string> VoicesOfPart(SyntaxNode root, string partName)
        => SlotsOf(root, partName).Keys;

    /// <summary>
    /// The same named voices with the SLOT each takes on its part's staff: its position
    /// among the voices of the simultaneous span that names it, unnamed ones counted
    /// (<c>voice sop { } { }  voice alt { }</c> → sop 0, alt 2) — the index of the voice's
    /// measure track in the collector (the part's own stream, then the extra tracks in
    /// order). The first span to name a voice decides. It is what lets a collector that
    /// holds a staff's tracks find the one a verse sings, on the multi-staff road as on
    /// the single-staff one.
    /// </summary>
    public static IReadOnlyDictionary<string, int> VoiceSlotsOfPart(SyntaxNode root, string partName)
        => SlotsOf(root, partName);

    private static Dictionary<string, int> SlotsOf(SyntaxNode root, string partName)
    {
        while (root.Parent != null)
            root = root.Parent;
        var map = VoiceMaps.GetValue(root, BuildVoiceMap);
        return map.TryGetValue(partName, out var voices) ? voices : EmptyVoices;
    }

    private static readonly Dictionary<string, int> EmptyVoices = new(StringComparer.Ordinal);

    private static Dictionary<string, Dictionary<string, int>> BuildVoiceMap(SyntaxNode root)
    {
        var map = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        // The two spellings that declare a part, asked of the index by the kind list that
        // stands beside PartReferenceFinder.DeclaredName (the same switch as below, pinned
        // to that list by SymbolKindsTests). The walk INSIDE each part stays a subtree walk.
        foreach (var n in root.SitesOfKinds(Editing.PartReferenceFinder.DeclaringKinds))
        {
            string? part = n switch
            {
                PartBlockSyntax pb => pb.PartName.Text,
                PartDeclarationSyntax pd => pd.Name.Text,
                _ => null,
            };
            if (part == null) continue;
            foreach (var par in n.DescendantNodes().OfType<ParallelExpressionSyntax>())
            {
                int slot = 0;
                foreach (var (vn, _) in par.NamedVoices)
                {
                    if (vn is { Length: > 0 })
                    {
                        if (!map.TryGetValue(part, out var slots))
                            map[part] = slots = new Dictionary<string, int>(StringComparer.Ordinal);
                        slots.TryAdd(vn, slot);
                    }
                    slot++;
                }
            }
        }
        return map;
    }
}
