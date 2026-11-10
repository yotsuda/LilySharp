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

using LilySharp.Core.Editing;
using LilySharp.Core.Music;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The lyric-binding rules (docs/lyrics-singer-design.md): a score row places a track's words,
/// under the staff nearest above it or, with its own <c>on</c>, under any other part's. See
/// <see cref="LyricBindings"/>.
/// <list type="bullet">
/// <item>LYS7004 — <c>on T</c> where T names no part or voice in the file.</item>
/// <item>LYS6012 — a row inside a staff group that does not follow the staff
/// directly above it (a group has no independent band to fall back to).</item>
/// </list>
/// (LYS6009/LYS6010 guarded the <c>with lyrics</c> clause against unbound and
/// wrong-staff tracks; both RETIRED with the clause — LYS0031. At top level a
/// bound row folds only onto the staff it sings, a row for another part is a
/// legal independent band, and an unbound row is the lead-sheet row.)
/// </summary>
internal sealed class LyricSingsValidator : ISemanticValidator
{
    private static readonly HashSet<string> ReservedTrackNames = new(StringComparer.Ordinal) { "on", "hum", "solfege" };

    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();

        // Every name that can be sung: declared parts, section part blocks
        // (implicit parts), and named voices anywhere in music.
        var partNames = new HashSet<string>(StringComparer.Ordinal);
        var voiceNames = new HashSet<string>(StringComparer.Ordinal);
        // ⚠️ Both sets are read through PartReferenceFinder, which the language server's
        // semantic tokens also ask: a `sings` target the editor paints as resolved while
        // this validator calls it unknown would be the editor contradicting itself inside
        // one line. Two callers, one answer — and the wider set (parts AND voices) is the
        // half a caller is most likely to get wrong on its own.
        // Each predicate is asked of the kinds it can answer on — its list beside it in
        // PartReferenceFinder, pinned to the predicate by SymbolKindsTests (the declaring
        // kinds) and LyricSingsWalkTests (the other two) — rather than of every node: the
        // sets below name a handful of nodes in a book of hundreds of thousands, and this
        // pass runs after every settled keystroke.
        foreach (var n in root.DescendantNodesOfKinds(PartReferenceFinder.DeclaringKinds))
            if (PartReferenceFinder.DeclaredName(n) is { } part)
                partNames.Add(part.Text);
        foreach (var n in root.DescendantNodesOfKinds(PartReferenceFinder.VoiceIntroducingKinds))
            PartReferenceFinder.CollectVoiceNames(n, voiceNames);

        // Both sites that name a melody — a score row's `on` and a singer line's — go through the
        // unknown-target net. The singer line reports its own (CheckSingerLines), so only the row
        // is read here; PartReferenceFinder.SingsTargetToken answers for both.
        static (string? Name, string? Target, TextSpan Span)? SingsOf(SyntaxNode node) => node switch
        {
            LyricsRowRenderSyntax r => (r.PartName, r.SingsTarget, (r.SingsKeyword ?? r.LyricsKeyword).Span),
            _ => null,
        };

        foreach (var node in root.DescendantNodesOfKinds(PartReferenceFinder.SingsKinds))
        {
            if (SingsOf(node) is not ({ } name, { } target, var span))
                continue;
            if (!partNames.Contains(target) && !voiceNames.Contains(target))
                _diagnostics.Error(
                    span,
                    DiagnosticCodes.SingsTargetUnknown,
                    $"'{name}' is set on '{target}', but no part or voice of "
                    + "that name exists in this file.");
        }

        // A track's name only names it (owner's decision 2026-11-10): the
        // same name reads as a binding, and is none when the part is renamed. Said once a track.
        var namedTracks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in root.DescendantNodes<LyricsBlockSyntax>())
        {
            if (block.NameToken is not { } token || !namedTracks.Add(token.Text))
                continue;
            string? kind = partNames.Contains(token.Text) ? "part" : voiceNames.Contains(token.Text) ? "voice" : null;
            if (kind != null)
                _diagnostics.Error(token.Span, DiagnosticCodes.LyricsNamedAsSinger,
                    $"lyrics '{token.Text}' has the name of a {kind}. Give the words a name of their own and say who sings them on its own line: "
                    + $"'lyrics words {{ … }}' and 'singer sings words on {token.Text}'.");
        }

        // (The `with lyrics` attachment checks — LYS6009/LYS6010 — died with the
        // clause, LYS0031: a bound row folds only onto the staff it sings, so a
        // wrong-staff or unbound attachment can no longer be SPELLED at top level;
        // what remains checkable is the group case below.)

        CheckLyricsBlocks(root);
        CheckSingerLines(root, partNames, voiceNames);
        CheckStaffSounds(root);

        foreach (var row in root.DescendantNodes<LyricsRowRenderSyntax>())
        {
            if (row.SingsTarget == null && LyricBindings.TargetOf(root, row.PartName) == null)
            {
                RenderSpecParser.NearestStaffAbove(row, out bool severalParts);
                if (severalParts)
                    _diagnostics.Error(row.LyricsKeyword.Span, DiagnosticCodes.LyricsRowNeedsOn,
                        $"The staff above lyrics '{row.PartName}' carries several parts: say whose notes the words follow, 'lyrics {row.PartName} on PART'.");
            }
        }

        // Rows inside a staff group: inside the braces a row IS the staff above's
        // attached verse (score = a vertical stack of bands), so a row that sings
        // no adjacent staff has no place a group can give it. The fold itself is
        // RenderSpecParser.ParseGrandStaff; this is its refusal half.
        foreach (var group in root.DescendantNodes<GrandStaffRenderSyntax>())
        {
            string? partAbove = null;
            bool sharedStaffAbove = false;
            bool nestedGroupAbove = false;
            foreach (var member in group.ChildNodes())
            {
                switch (member)
                {
                    case StaffRenderSyntax st:
                        partAbove = RenderSpecParser.ParseStaffSpec(st)?.VoiceName;
                        sharedStaffAbove = false;
                        nestedGroupAbove = false;
                        break;
                    // A nested group: its own rows are checked when this loop reaches it as a
                    // group, and a row standing AFTER it, outside its braces, has no staff of
                    // this group directly above it (ParseGrandStaff folds under plain staves only).
                    case GrandStaffRenderSyntax:
                        partAbove = null;
                        sharedStaffAbove = false;
                        nestedGroupAbove = true;
                        break;
                    // A condensed or combined staff carries several parts, so no row can be
                    // ITS verse — RenderSpecParser.ParseGrandStaff folds only under a plain
                    // staff, and this is the refusal half of that.
                    case CondensedStaffRenderSyntax or CombinedStaffRenderSyntax:
                        partAbove = null;
                        sharedStaffAbove = true;
                        nestedGroupAbove = false;
                        break;
                    // A row with its own `on` stands anywhere in the group, on its own band
                    // (RenderSpecParser.ParseGrandStaff). Only a row that says nothing can
                    // lack a staff to belong to.
                    case LyricsRowRenderSyntax row
                        when row.SingsTarget == null
                          && (partAbove == null
                              || !RenderSpecParser.RowBindsToPart(root, row.PartName, RenderSpecParser.RowTarget(row), partAbove)):
                        _diagnostics.Error(
                            row.LyricsKeyword.Span,
                            DiagnosticCodes.GroupRowNotBoundToStaffAbove,
                            nestedGroupAbove
                                ? $"lyrics '{row.PartName}' stands under a nested group - inside a group "
                                  + "a row is the verse of the plain staff directly above it; write the row "
                                  + "inside that group, under the staff it sings."
                            : sharedStaffAbove
                                ? $"lyrics '{row.PartName}' stands under a condensed or combined staff - "
                                  + "inside a group a row is the verse of the ONE part on the staff above it, "
                                  + "and that staff carries several; move the row outside the braces."
                            : partAbove == null
                                ? $"lyrics '{row.PartName}' stands before any staff in this group - "
                                  + "inside a group a row is the verse of the staff directly above it."
                                : $"lyrics '{row.PartName}' does not sing '{partAbove}', the staff "
                                  + "directly above it - inside a group a row is that staff's verse; "
                                  + $"write 'lyrics {row.PartName} on PART' to say whose notes the words follow.");
                        break;
                }
            }
        }

        foreach (var group in root.DescendantNodes<GrandStaffRenderSyntax>())
            if (!group.IsInside<GrandStaffRenderSyntax>())
                WarnRowsPlacedTwice(group);
    }

    /// <summary>The same words on the same part, written twice in one group: two rows saying
    /// one thing (docs/lyrics-singer-design.md §9.3). Said at the second and any later row.</summary>
    private void WarnRowsPlacedTwice(GrandStaffRenderSyntax group)
    {
        var placed = new HashSet<(string Track, string Part)>();
        foreach (var row in group.DescendantNodes<LyricsRowRenderSyntax>())
            if (RenderSpecParser.RowTarget(row) is { } part && !placed.Add((row.PartName, part)))
                _diagnostics.Warning(row.LyricsKeyword.Span, DiagnosticCodes.GroupRowPlacedTwice,
                    $"lyrics '{row.PartName}' is already placed on '{part}' in this group; the same words print twice.");
    }

    private void CheckLyricsBlocks(SyntaxNode root)
    {
        foreach (var block in root.DescendantNodes<LyricsBlockSyntax>())
        {
            string name = block.NameToken?.Text ?? "words";
            if (block.NameToken is { } token && ReservedTrackNames.Contains(token.Text))
                _diagnostics.Error(token.Span, DiagnosticCodes.LyricsNameReserved,
                    $"'{token.Text}' is a reserved word and cannot name lyrics.");
            if (block.IsInside<PartDeclarationSyntax>() || block.IsInside<PartBlockSyntax>())
                _diagnostics.Error(block.LyricsKeyword.Span, DiagnosticCodes.LyricsInsidePart,
                    "Lyrics cannot be written inside a part: words are not a part's. Write them at the top level or directly in a section, 'lyrics words { section A { … } }'.");
        }
    }

    private void CheckSingerLines(SyntaxNode root, HashSet<string> partNames, HashSet<string> voiceNames)
    {
        var lines = LyricSinger.LinesOf(root);
        if (lines.Count == 0)
            return;
        var tracks = root.DescendantNodes<LyricsBlockSyntax>().Select(b => b.VoiceName).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var sungParts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var node = line.Node;
            if (node.SingsKeyword == null)
                continue;   // the parser said so
            var at = node.TrackToken ?? node.SingsKeyword;
            if (line.Track is "hum" or "solfege")
                _diagnostics.Error(at.Span, DiagnosticCodes.SingerGeneratedWordsNotYet, $"Singing '{line.Track}' is not built yet.");
            else if (!tracks.Contains(line.Track))
                _diagnostics.Error(at.Span, DiagnosticCodes.SingerWordsUnknown,
                    node.TrackToken == null ? "This file has no unnamed lyrics to sing." : $"No lyrics are named '{line.Track}'.");
            if (node.OnToken is { } on)
            {
                if (!partNames.Contains(on.Text) && !voiceNames.Contains(on.Text))
                    _diagnostics.Error(on.Span, DiagnosticCodes.SingsTargetUnknown, $"No part or voice is named '{on.Text}'.");
                else if (!partNames.Contains(on.Text) && partNames.Count(p => LyricBindings.VoicesOfPart(root, p).Contains(on.Text)) > 1)
                    _diagnostics.Error(on.Span, DiagnosticCodes.SingerVoiceAmbiguous, $"Several parts have a voice named '{on.Text}'.");
            }
            else if (line.Part == null)
                _diagnostics.Error(at.Span, DiagnosticCodes.SingerPartUnresolved,
                    "Nothing says whose notes these words are sung on: add 'on PART'.");
            if (line.Part != null && !sungParts.Add(line.Part))
                _diagnostics.Error(node.Keyword.Span, DiagnosticCodes.SingerDuplicatePart,
                    $"'{line.Part}' already has a singer. Several voices on one part are written with commas: singer \"a/b\",\"c/d\" sings ….");
        }
    }

    private void CheckStaffSounds(SyntaxNode root)
    {
        var singers = LyricSinger.LinesOf(root).Select(l => l.Part).OfType<string>().ToHashSet(StringComparer.Ordinal);
        void Check(SyntaxTokenNode? word, string? part)
        {
            if (word == null)
                return;
            if (word.Text is not ("voice" or "instrument" or "both"))
                _diagnostics.Error(word.Span, DiagnosticCodes.StaffSoundUnknown, $"'sounds' takes voice, instrument or both, not '{word.Text}'.");
            else if (word.Text != "instrument" && (part == null || !singers.Contains(part)))
                _diagnostics.Error(word.Span, DiagnosticCodes.StaffSoundNeedsSinger,
                    $"No singer sings '{part}'. Add a line at the top level: 'singer S sings WORDS on {part}'.");
        }
        foreach (var render in root.DescendantNodes<RenderDeclarationSyntax>())
            if (RenderSpecParser.Parse(render) is { Items.IsEmpty: true, HiddenStaves.IsDefaultOrEmpty: false })
                _diagnostics.Error(render.RenderKeyword.Span, DiagnosticCodes.ScoreAllStavesHidden,
                    "Every staff of this score is hidden, so it has nothing to print. Show at least one staff.");
        foreach (var node in root.DescendantNodes())
            if (node is StaffRenderSyntax staff)
                Check(staff.SoundsToken, staff.PartToken?.Text ?? SyntaxFacts.UnnamedPartName);
            else if (node is TabRenderSyntax tab)
                Check(tab.SoundsToken, tab.PartToken?.Text ?? SyntaxFacts.UnnamedPartName);
    }
}
