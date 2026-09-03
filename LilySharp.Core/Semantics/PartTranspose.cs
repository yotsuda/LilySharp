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

using System;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reads the <c>transpose</c> target (with its octave marks) for a part:
/// the part's own option if present, otherwise a top-level <c>transpose</c>
/// default that applies to every part. Shared by the renderer's collector and
/// the MIDI / MusicXML exporters so the single transpose grammar has one reader.
/// </summary>
public static class PartTranspose
{
    /// <summary>
    /// The effective transpose for <paramref name="partName"/>, or null: the part's own
    /// option, else the file default — and, in a file written at concert pitch
    /// (<see cref="ConcertPitch"/>), the instrument's own written-side shift composed
    /// inside it, so an alto saxophone's part prints a major sixth above the letters.
    /// </summary>
    /// <remarks>
    /// ⚠️ The concert-pitch shift lives HERE, in the one reader every consumer of a part's
    /// transpose already asks (the page's collector, MIDI, MusicXML, the LilyPond twin),
    /// and not beside any of them: the alternative — each consumer adding the instrument's
    /// shift for itself — is a second spelling of "shift this part", and the MIDI exporter
    /// would then pass the preset's −9 twice (once here, once as the sounding shift).
    /// </remarks>
    public static (int step, int alt, int oct)? Read(SyntaxNode root, string partName)
    {
        // Part declarations are top-level only (Parser.ParseTopLevelItem), so the
        // root's direct children are the whole search space (SyntaxNode.ChildNodes).
        // ReadScoreDefault stays on the descendant walk, but no longer counts a
        // render block's own transpose as the file's default — see there.
        var partDecl = ConcertPitch.FindPart(root, partName);
        // a part's own transpose overrides the default
        var written = (partDecl != null ? Read(partDecl) : null) ?? ReadScoreDefault(root);
        return PitchTransposer.NullIfIdentity(PitchTransposer.Compose(
            ConcertPitch.InputShift(ConcertPitch.FileIsConcert(root), partDecl), written));
    }

    /// <summary>
    /// Parses a <c>transpose &lt;pitch&gt;</c> property node (the shared shape used by a
    /// part header and a per-score transpose) into a c-&gt;target interval, or null.
    /// </summary>
    public static (int step, int alt, int oct)? ReadProperty(PropertyAssignmentSyntax prop)
        => IsTranspose(prop) ? Parse(prop) : null;

    /// <summary>Reads the transpose option from a part declaration, or null.</summary>
    public static (int step, int alt, int oct)? Read(PartDeclarationSyntax partDecl)
    {
        foreach (var prop in partDecl.Properties)
            if (IsTranspose(prop))
                return Parse(prop);
        return null;
    }

    /// <summary>
    /// A free-standing top-level <c>transpose d</c> (not a part-header attribute):
    /// the score-wide default. Public so callers that already hold each part's
    /// declaration can compute the default once and combine it themselves
    /// (own ?? default) rather than re-scanning the tree per part.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <c>score … transpose d { … }</c> is NOT this. It looks like it to a walk
    /// filtered only on "not inside a part", and being counted here made one construct
    /// give three answers (measured 2026-08-16, 第182):
    /// <list type="bullet">
    /// <item>the score that declares it got the interval TWICE — once from here as the
    /// file default, once from its own <c>RenderSpec.ScoreTranspose</c>, which the
    /// collector composes. `transpose d` moved c to E4, a major third, where the
    /// part-header spelling moves it to D4;</item>
    /// <item>and every OTHER score in the file got it once, unasked: in a book whose
    /// first score declares no transpose at all, that score engraved in D major.</item>
    /// </list>
    /// Both fall out of one line, so one guard removes both. With it, the three
    /// spellings of <c>transpose</c> — part header, top level, per score — finally
    /// agree that <c>d</c> means a major second, which is what
    /// <c>test/transpose-score.lys</c> and <c>test/transpose-down.lys</c> already
    /// document against LilyPond's <c>\transpose c d</c>.
    /// </remarks>
    public static (int step, int alt, int oct)? ReadScoreDefault(SyntaxNode root)
    {
        // Green finder, not DescendantNodes().OfType<…>(): this reader runs per
        // part per keystroke, and the red walk materialized a red wrapper for
        // EVERY descendant just to type-test it — measured as the whole-tree
        // red-creation cost of the edit keystroke once the collector's own walks
        // went green (HANDOFF §1 session 153). Same node set, same pre-order;
        // the matched red carries its Parent chain, so the IsInsidePart guard
        // is unchanged.
        foreach (var prop in root.GreenSites(
                     static g => (g.Kind == SyntaxKind.PropertyAssignment, Descend: true)))
            if (prop is PropertyAssignmentSyntax pa && IsTranspose(pa)
                && !IsInsidePart(pa) && !IsInsideRender(pa))
                return Parse(pa);
        return null;
    }

    private static bool IsTranspose(PropertyAssignmentSyntax prop)
        => string.Equals(prop.NameToken.Text, "transpose", StringComparison.OrdinalIgnoreCase);

    private static bool IsInsidePart(SyntaxNode node) => node.IsInside<PartDeclarationSyntax>();

    /// <summary>A per-score <c>transpose</c> belongs to that score, not to the file.</summary>
    private static bool IsInsideRender(SyntaxNode node) => node.IsInside<RenderDeclarationSyntax>();

    // Children are: name, [optional colon], value, [octave marks...]. The colon
    // is now optional, so locate the value/marks by skipping the name and colon
    // rather than by a fixed slot index.
    private static (int step, int alt, int oct)? Parse(PropertyAssignmentSyntax prop)
    {
        SyntaxTokenNode? valueToken = null;
        int oct = 0;
        for (int ci = 1; ci < prop.SlotCount; ci++)
        {
            if (prop.GetChild(ci) is not SyntaxTokenNode tok || tok.Kind == SyntaxKind.Colon)
                continue;
            if (valueToken == null)
            {
                valueToken = tok;
            }
            else if (tok.Kind == SyntaxKind.Apostrophe)
            {
                oct++;
            }
            else if (tok.Kind == SyntaxKind.Comma)
            {
                oct--;
            }
        }

        if (valueToken == null || !PitchTransposer.TryParseTarget(valueToken.Text, out int step, out int alt))
            return null;
        return (step, alt, oct);
    }
}
