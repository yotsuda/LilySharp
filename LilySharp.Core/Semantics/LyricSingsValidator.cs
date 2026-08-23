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
/// The lyric-binding rules (user decision, 2026-08-19 — closed before the first
/// tag): lyrics bind to their OWN melody at the definition
/// (<c>lyrics ja sings vocal { … }</c>), and the score only PLACES them.
/// <list type="bullet">
/// <item>LYS7004 — <c>sings T</c> where T names no part or voice in the file.</item>
/// <item>LYS7005 — two blocks of one track name different targets.</item>
/// <item>LYS6012 — a row inside a staff group that does not sing the staff
/// directly above it (a group has no independent band to fall back to).</item>
/// </list>
/// (LYS6009/LYS6010 guarded the <c>with lyrics</c> clause against unbound and
/// wrong-staff tracks; both RETIRED with the clause — LYS0031. At top level a
/// bound row folds only onto the staff it sings, a row for another part is a
/// legal independent band, and an unbound row is the lead-sheet row.)
/// </summary>
internal sealed class LyricSingsValidator : ISemanticValidator
{
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
        foreach (var n in root.DescendantNodes())
        {
            if (PartReferenceFinder.DeclaredName(n) is { } part)
                partNames.Add(part.Text);
            PartReferenceFinder.CollectVoiceNames(n, voiceNames);
        }

        // Both spellings of the declaration — the definition block and the score
        // row (`lyrics verse sings melody`) — state the same track property, so
        // both go through the same nets.
        static (string? Name, string? Target, TextSpan Span)? SingsOf(SyntaxNode node) => node switch
        {
            LyricsBlockSyntax b => (b.VoiceName, b.SingsTarget, (b.SingsKeyword ?? b.LyricsKeyword).Span),
            LyricsRowRenderSyntax r => (r.PartName, r.SingsTarget, (r.SingsKeyword ?? r.LyricsKeyword).Span),
            _ => null,
        };

        foreach (var node in root.DescendantNodes())
        {
            if (SingsOf(node) is not ({ } name, { } target, var span))
                continue;
            if (!partNames.Contains(target) && !voiceNames.Contains(target))
                _diagnostics.Error(
                    span,
                    DiagnosticCodes.SingsTargetUnknown,
                    $"'{name}' sings '{target}', but no part or voice of "
                    + "that name exists in this file.");
        }

        foreach (var (node, target, first) in LyricBindings.Conflicts(root))
        {
            if (SingsOf(node) is not ({ } name, _, var span))
                continue;
            _diagnostics.Error(
                span,
                DiagnosticCodes.SingsConflict,
                $"'{name}' already sings '{first}' - a track sings ONE part; "
                + "state the binding once (later blocks may repeat it identically or omit it).");
        }

        // (The `with lyrics` attachment checks — LYS6009/LYS6010 — died with the
        // clause, LYS0031: a bound row folds only onto the staff it sings, so a
        // wrong-staff or unbound attachment can no longer be SPELLED at top level;
        // what remains checkable is the group case below.)

        // Rows inside a staff group: inside the braces a row IS the staff above's
        // attached verse (score = a vertical stack of bands), so a row that sings
        // no adjacent staff has no place a group can give it. The fold itself is
        // RenderSpecParser.ParseGrandStaff; this is its refusal half.
        foreach (var group in root.DescendantNodes().OfType<GrandStaffRenderSyntax>())
        {
            string? partAbove = null;
            foreach (var member in group.ChildNodes())
            {
                switch (member)
                {
                    case StaffRenderSyntax st:
                        partAbove = RenderSpecParser.ParseStaffSpec(st)?.VoiceName;
                        break;
                    case LyricsRowRenderSyntax row
                        when partAbove == null
                          || !RenderSpecParser.RowBindsToPart(root, row.PartName, partAbove):
                        _diagnostics.Error(
                            row.LyricsKeyword.Span,
                            DiagnosticCodes.GroupRowNotBoundToStaffAbove,
                            partAbove == null
                                ? $"lyrics '{row.PartName}' stands before any staff in this group - "
                                  + "inside a group a row is the verse of the staff directly above it."
                                : $"lyrics '{row.PartName}' does not sing '{partAbove}', the staff "
                                  + "directly above it - inside a group a row is that staff's verse; "
                                  + "a row for another part goes outside the braces.");
                        break;
                }
            }
        }
    }
}
