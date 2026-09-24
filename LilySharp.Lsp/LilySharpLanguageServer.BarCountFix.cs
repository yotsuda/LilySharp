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
    // ========== Quick fix: pad a section's short layers with empty bars (LYS2007) ==========

    /// <summary>One layer the padding edit writes to: where its bars end, the bars it adds
    /// (counted in BAR LINES — over an open last bar the first only closes it), and its label
    /// for the action's title.</summary>
    internal readonly record struct BarPad(int Offset, string Text, int Bars, string Voice);

    /// <summary>
    /// The edit that brings a section's SHORT layers up to the length it is laid out at, for
    /// one LYS2007 (<see cref="DiagnosticCodes.SectionBarCountMismatch"/>): every layer the
    /// warning lists (its related locations) with fewer bars than that — a part-major
    /// <c>section</c> of a part / chords / lyrics track, or a section-major part block / named
    /// chords block / lyrics block — gets bare <c>|</c> appended after its last item. An empty
    /// <c>| |</c> bar is a full measure to every counter (the bare-barline rule; MEASURED
    /// 2026-09-10 on scratch/p363/pad-probe.lys: no LYS2001 either; a bare <c>|</c> in a lyrics
    /// body is an empty lyric measure the row skips).
    /// </summary>
    /// <remarks>
    /// ⚠️ OFFERED, NEVER APPLIED BY ITSELF: which length is right is the author's to say (the
    /// warning deliberately does not), so this is one action among the author's choices — the
    /// other being to delete the extra bar in the longer layer, which no tool can pick for them.
    /// <para>
    /// The target and each layer's count are read off the warning ("laid out at N bar(s)", and
    /// "X bar(s)" in each related entry), but no edit is trusted on that arithmetic: each
    /// layer's candidate is re-parsed and re-validated, and accepted only when that layer then
    /// reaches the target. That settles the case the count cannot see — a scope whose last bar
    /// is still open (<c>{ g2 g }</c>, <c>{ Dm7 | G7 }</c>), where the first added <c>|</c>
    /// only CLOSES it, so one more is tried. The layers are padded from the END of the document
    /// backwards, so an insertion never moves an anchor still to be read. The re-validation runs
    /// only when the user asks for code actions on the squiggle, never per keystroke.
    /// </para>
    /// </remarks>
    /// <returns>The layers' edits, or null when the diagnostic is not an LYS2007, it names no
    /// short layer, or some layer resolves to no scope or cannot be brought to length.</returns>
    internal static IReadOnlyList<BarPad>? BarCountPadding(
        SyntaxTree tree, string text, CoreDiagnostic diagnostic)
    {
        if (diagnostic.Code != DiagnosticCodes.SectionBarCountMismatch)
            return null;
        var target = LaidOutAt.Match(diagnostic.Message);
        if (!target.Success)
            return null;
        int want = int.Parse(target.Groups[1].Value);

        var shortLayers = new List<(TextSpan Anchor, int Have)>();
        foreach (var r in diagnostic.Related)
        {
            var m = LayerBars.Match(r.Message);
            if (m.Success && int.Parse(m.Groups[1].Value) is var have && have < want)
                shortLayers.Add((r.Span, have));
        }
        if (shortLayers.Count == 0)
            return null;

        var pads = new List<BarPad>();
        string current = text;
        foreach (var (anchor, have) in shortLayers.OrderByDescending(l => l.Anchor.Start))
        {
            var scope = ScopeAnchoredAt(tree, anchor);
            if (scope == null)
                return null;
            var (offset, voice) = scope.Value;
            BarPad? found = null;
            // The arithmetic answer first, then one more for an unclosed last bar.
            for (int bars = want - have; bars <= want - have + 1 && found == null; bars++)
            {
                string insert = string.Concat(Enumerable.Repeat(" |", bars));
                string candidate = current.Substring(0, offset) + insert + current.Substring(offset);
                if (LayerReaches(candidate, anchor.Start, want))
                {
                    found = new BarPad(offset, insert, bars, voice);
                    current = candidate;
                }
            }
            if (found is not { } pad)
                return null;
            pads.Add(pad);
        }
        return pads;
    }

    private static readonly Regex LaidOutAt =
        new(@"laid out at (\d+) bar\(s\)", RegexOptions.Compiled);

    private static readonly Regex LayerBars =
        new(@": (\d+) bar\(s\)", RegexOptions.Compiled);

    /// <summary>The scope a LYS2007 is anchored on (its NAME token span), and where its
    /// bars end: after the last item of its body, or right after its <c>{</c> when empty.
    /// Returns the insertion offset and the voice's label for the action title.</summary>
    private static (int Offset, string Voice)? ScopeAnchoredAt(SyntaxTree tree, TextSpan anchor)
    {
        // Part-major: `part p { section A { … } }` / `chords t { section A { … } }` /
        // `lyrics w { section A { … } }` — anchored on the section name. Section-major:
        // `section A { p { … } chords t { … } lyrics w { … } }` — anchored on the part block's
        // name / the chords block's name / the lyrics block's name (its keyword when nameless,
        // SectionBarCounts.LyricsAnchor).
        foreach (var sec in tree.GetNodes<SectionDeclarationSyntax>())
            if (sec.Name.Span == anchor)
            {
                // WHOSE section A: every part-major writer's cell is `section A`, so a title
                // that pads several of them has to say which (`section A of melody`).
                string owner = sec.Parent switch
                {
                    PartDeclarationSyntax p => $" of {p.Name.Text}",
                    ChordPartBlockSyntax { PartName: { } c } => $" of chords {c}",
                    LyricsBlockSyntax { VoiceName: { } l } => $" of lyrics {l}",
                    _ => "",
                };
                return (EndOfBody(sec), $"section {sec.SectionName}{owner}");
            }
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
        foreach (var lb in tree.GetNodes<LyricsBlockSyntax>())
            if ((lb.NameToken ?? lb.LyricsKeyword).Span == anchor)
                return (EndOfBody(lb), lb.VoiceName is { } n ? $"lyrics {n}" : "lyrics");
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

    /// <summary>True when, in the candidate text, the layer anchored at
    /// <paramref name="anchorStart"/> is no longer short of <paramref name="want"/> bars: no
    /// LYS2007 lists it any more (its section agrees), or the one that does counts it at the
    /// target. The anchor precedes every insertion made for it or after it, so its offset is
    /// stable across candidates.</summary>
    private static bool LayerReaches(string candidate, int anchorStart, int want)
    {
        var tree = SyntaxTree.Parse(candidate);
        foreach (var d in SemanticValidation.Run(tree))
        {
            if (d.Code != DiagnosticCodes.SectionBarCountMismatch)
                continue;
            foreach (var r in d.Related)
                if (r.Span.Start == anchorStart
                    && LayerBars.Match(r.Message) is { Success: true } m
                    && int.Parse(m.Groups[1].Value) < want)
                    return false;
        }
        return true;
    }
}
