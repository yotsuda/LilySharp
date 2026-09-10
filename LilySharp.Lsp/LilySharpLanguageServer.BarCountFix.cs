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

using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    // ========== Quick fix: pad a short section voice with empty bars (LYS2007) ==========

    /// <summary>
    /// The edit that silences one LYS2007 (<see cref="DiagnosticCodes.SectionBarCountMismatch"/>):
    /// the voice the warning is anchored on — a part-major <c>section</c> (of a part or a
    /// chords track), or a section-major part block / named chords block — gets as many
    /// bare <c>|</c> appended after its last item as it is short of the section's longest
    /// voice. An empty <c>| |</c> bar is a full measure to every counter (the bare-barline
    /// rule; MEASURED 2026-09-10 on scratch/p363/pad-probe.lys: no LYS2001 either), so the
    /// section aligns and the warning goes.
    /// </summary>
    /// <remarks>
    /// The number of bars is read off the message ("spans N bar(s) in … but M in …"), but the
    /// edit is never trusted on that arithmetic alone: the candidate text is re-parsed and
    /// re-validated, and only an edit under which the warning at this anchor is gone is
    /// offered. That is what settles the one case the count cannot see — a scope whose last
    /// bar is still open (<c>{ g2 g }</c>, <c>{ Dm7 | G7 }</c>): its first added <c>|</c> only
    /// CLOSES that bar, so one more is tried. The re-validation runs only when the user asks
    /// for code actions on the squiggle, never per keystroke.
    /// </remarks>
    /// <returns>The insertion offset, the text to insert, and the bar count added — or null
    /// when the diagnostic is not an LYS2007, its anchor resolves to no scope, or no candidate
    /// clears the warning.</returns>
    internal static (int Offset, string Text, int Bars, string Voice)? BarCountPadding(
        SyntaxTree tree, string text, CoreDiagnostic diagnostic)
    {
        if (diagnostic.Code != DiagnosticCodes.SectionBarCountMismatch)
            return null;
        var m = BarCountMessage.Match(diagnostic.Message);
        if (!m.Success)
            return null;
        int have = int.Parse(m.Groups[1].Value);
        int want = int.Parse(m.Groups[2].Value);
        if (want <= have)
            return null;

        var scope = ScopeAnchoredAt(tree, diagnostic.Span);
        if (scope == null)
            return null;
        var (offset, voice) = scope.Value;

        // Try the arithmetic answer first, then one more for an unclosed last bar.
        for (int bars = want - have; bars <= want - have + 1; bars++)
        {
            string insert = string.Concat(Enumerable.Repeat(" |", bars));
            string candidate = text.Substring(0, offset) + insert + text.Substring(offset);
            if (!StillWarnsAt(candidate, diagnostic.Span.Start))
                return (offset, insert, bars, voice);
        }
        return null;
    }

    private static readonly Regex BarCountMessage =
        new(@"spans (\d+) bar\(s\) in .+ but (\d+) in ", RegexOptions.Compiled);

    /// <summary>The scope a LYS2007 is anchored on (its NAME token span), and where its
    /// bars end: after the last item of its body, or right after its <c>{</c> when empty.
    /// Returns the insertion offset and the voice's label for the action title.</summary>
    private static (int Offset, string Voice)? ScopeAnchoredAt(SyntaxTree tree, TextSpan anchor)
    {
        // Part-major: `part p { section A { … } }` / `chords t { section A { … } }` — anchored
        // on the section name. Section-major: `section A { p { … } chords t { … } }` — anchored
        // on the part block's name / the chords block's name.
        foreach (var sec in tree.GetNodes<SectionDeclarationSyntax>())
            if (sec.Name.Span == anchor)
                return (EndOfBody(sec), $"section {sec.SectionName}");
        foreach (var pb in tree.GetNodes<PartBlockSyntax>())
            if (pb.PartName.Span == anchor)
            {
                // A part block's braces belong to its body node (name, options…, body).
                var body = pb.ChildNodes().OfType<MusicBlockSyntax>().LastOrDefault();
                return body == null ? null : (EndOfBody(body), $"part {pb.Name}");
            }
        foreach (var cb in tree.GetNodes<ChordPartBlockSyntax>())
            if (cb.NameToken is { } name && name.Span == anchor)
                return (EndOfBody(cb), $"chords {cb.PartName}");
        return null;
    }

    /// <summary>The offset just after the last item between the scope's own braces (the
    /// items are the node's direct children; nested braces belong to child nodes), or just
    /// after the opening brace when the body is empty.</summary>
    private static int EndOfBody(SyntaxNode scope)
    {
        int after = -1;
        bool inBody = false;
        for (int i = 0; i < scope.SlotCount; i++)
        {
            var child = scope.GetChild(i);
            if (child == null)
                continue;
            if (child is SyntaxTokenNode tok)
            {
                if (!inBody && tok.Kind == SyntaxKind.OpenBrace) { inBody = true; after = tok.Span.End; }
                else if (inBody && tok.Kind == SyntaxKind.CloseBrace) break;
                else if (inBody) after = tok.Span.End;
                continue;
            }
            if (inBody)
                after = child.Span.End;
        }
        return after;
    }

    /// <summary>True when the candidate text still reports a LYS2007 anchored at
    /// <paramref name="anchorStart"/> (the anchor precedes every insertion, so its offset
    /// is stable across candidates).</summary>
    private static bool StillWarnsAt(string candidate, int anchorStart)
    {
        var tree = SyntaxTree.Parse(candidate);
        foreach (var d in SemanticValidation.Run(tree))
            if (d.Code == DiagnosticCodes.SectionBarCountMismatch && d.Span.Start == anchorStart)
                return true;
        return false;
    }
}
