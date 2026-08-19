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
/// <item>LYS6009 — <c>staff X with lyrics N</c> where N sings nothing and its
/// name matches neither X nor one of X's voices (the voice-binding rule
/// <c>voice sop { } + lyrics sop { }</c> is a binding, kept).</item>
/// <item>LYS6010 — <c>staff X with lyrics N</c> where N sings a DIFFERENT part:
/// placement cannot re-decide the association.</item>
/// </list>
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
        foreach (var n in root.DescendantNodes())
        {
            switch (n)
            {
                case PartDeclarationSyntax pd:
                    partNames.Add(pd.Name.Text);
                    break;
                case PartBlockSyntax pb:
                    partNames.Add(pb.PartName.Text);
                    break;
                case ParallelExpressionSyntax par:
                    foreach (var (vn, _) in par.NamedVoices)
                        if (vn is { Length: > 0 })
                            voiceNames.Add(vn);
                    break;
            }
        }

        foreach (var block in root.DescendantNodes().OfType<LyricsBlockSyntax>())
        {
            if (block.SingsTarget is not { } target)
                continue;
            if (!partNames.Contains(target) && !voiceNames.Contains(target))
                _diagnostics.Error(
                    (block.SingsKeyword ?? block.LyricsKeyword).Span,
                    DiagnosticCodes.SingsTargetUnknown,
                    $"'{block.VoiceName}' sings '{target}', but no part or voice of "
                    + "that name exists in this file.");
        }

        foreach (var (block, target, first) in LyricBindings.Conflicts(root))
            _diagnostics.Error(
                (block.SingsKeyword ?? block.LyricsKeyword).Span,
                DiagnosticCodes.SingsConflict,
                $"'{block.VoiceName}' already sings '{first}' - a track sings ONE part; "
                + "state the binding once (later blocks may repeat it identically or omit it).");

        // Attachment checks: `staff X with lyrics N`.
        foreach (var staff in root.DescendantNodes().OfType<StaffRenderSyntax>())
        {
            if (RenderSpecParser.ParseStaffSpec(staff) is not { } spec
                || spec.WithLyrics.IsDefaultOrEmpty)
                continue;
            var staffVoices = VoicesOfPart(root, spec.VoiceName);
            foreach (var attached in spec.WithLyrics)
            {
                string? sings = LyricBindings.TargetOf(root, attached);
                if (sings != null)
                {
                    if (!string.Equals(sings, spec.VoiceName, StringComparison.Ordinal)
                        && !staffVoices.Contains(sings))
                        _diagnostics.Error(
                            staff.StaffKeyword.Span,
                            DiagnosticCodes.LyricsAttachmentWrongStaff,
                            $"lyrics '{attached}' sings '{sings}', not '{spec.VoiceName}' - "
                            + "the binding is the track's; place it under the part it sings.");
                    continue;
                }
                // Unbound: the name itself must be the binding (the part, or one
                // of its voices). Anything else used to silently align to
                // whatever staff it was attached to; that door is closed.
                if (string.Equals(attached, spec.VoiceName, StringComparison.Ordinal)
                    || staffVoices.Contains(attached))
                    continue;
                _diagnostics.Error(
                    staff.StaffKeyword.Span,
                    DiagnosticCodes.LyricsAttachmentUnbound,
                    $"lyrics '{attached}' does not sing any part - write "
                    + $"'lyrics {attached} sings {spec.VoiceName} {{ ... }}' so the words "
                    + "and their melody are bound where the track is defined.");
            }
        }
    }

    /// <summary>The named voices written inside part <paramref name="partName"/>'s
    /// music (section cells and part-major inner sections alike).</summary>
    private static HashSet<string> VoicesOfPart(SyntaxNode root, string partName)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in root.DescendantNodes())
        {
            SyntaxNode? body = n switch
            {
                PartBlockSyntax pb when pb.PartName.Text == partName => pb,
                PartDeclarationSyntax pd when pd.Name.Text == partName => pd,
                _ => null,
            };
            if (body == null) continue;
            foreach (var par in body.DescendantNodes().OfType<ParallelExpressionSyntax>())
                foreach (var (vn, _) in par.NamedVoices)
                    if (vn is { Length: > 0 })
                        result.Add(vn);
        }
        return result;
    }
}
