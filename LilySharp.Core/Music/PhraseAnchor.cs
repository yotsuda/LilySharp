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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// The written step a phrase reference hands back to the relative-octave chain.
/// A reference is ONE item, interpreted exactly like a chord: it propagates its
/// ANCHOR — the body's first pitched element's bare letter, or the key tonic
/// for a degree-opened group — never its interior, so the note after a
/// reference does not depend on how the phrase body happens to end, and
/// editing the tail of a phrase can never move the music that follows a
/// reference. Shared by the SVG collector and the MIDI / MusicXML exporters so
/// the three walkers cannot drift.
/// </summary>
internal static class PhraseAnchor
{
    /// <summary>Sentinel step for a degree-opened anchor: the caller substitutes
    /// the AMBIENT tonic at the reference site (it may differ per call site).</summary>
    public const int Tonic = -1;

    /// <summary>Sentinel step for a body that opens with a chord from a shape
    /// (<c>chord(C x32010)</c>): the anchor is that item's LOWEST sounding note as written —
    /// the note the item itself hands on (owner's decision 2026-09-28) — which only the
    /// caller can compute (the part's tuning, the capo, the key). It is absolute: the
    /// reference's own marks do not move it, as they do not move the item (owner's decision
    /// 2026-09-30). The item is <see cref="Anchor"/>'s <c>shape</c>.</summary>
    /// <remarks>LILYSHARP-OWN: LilyPond has neither the item nor a phrase anchor — the twin's
    /// nested <c>\relative</c> reads the note after it from the pitch BEFORE the reference (it warns there).</remarks>
    public const int Shape = -2;

    /// <summary>
    /// The written step (0 = c … 6 = b) of <paramref name="body"/>'s anchor,
    /// <see cref="Tonic"/> when the first pitched element is degree-anchored
    /// (<c>&lt;1 3 5&gt;</c>), <see cref="Shape"/> when it is a chord from a shape, or null
    /// for a pitchless body (rests / drums only).
    /// <paramref name="resolveReference"/> maps a nested reference's name to its
    /// body (null when unknown); recursion is depth-capped.
    /// </summary>
    public static int? AnchorStep(SyntaxNode body, Func<string, SyntaxNode?> resolveReference)
        => Anchor(body, resolveReference, out _);

    /// <summary><see cref="AnchorStep"/>, with the chord from a shape the anchor is read from
    /// when it is <see cref="Shape"/> (null otherwise).</summary>
    public static int? Anchor(SyntaxNode body, Func<string, SyntaxNode?> resolveReference, out ChordSyntax? shape)
    {
        shape = null;
        return Walk(body, resolveReference, depth: 0, ref shape);
    }

    private static int? Walk(SyntaxNode node, Func<string, SyntaxNode?> resolve, int depth, ref ChordSyntax? shape)
    {
        switch (node)
        {
            case NoteSyntax n:
                return StepOf(n.Pitch);

            case PitchSyntax p:
                return StepOf(p);

            // A chord from a shape anchors on its lowest sounding note (see Shape). Until
            // 2026-09-30 it fell to the arm below, which found no root and anchored nothing,
            // so the scan went on to the NEXT note of the body — the interior leaked.
            case ChordSyntax { IsShapeChord: true } sc:
                shape = sc;
                return Shape;

            // A chord anchors on its root letter — or the tonic when it opens
            // with degrees (an all-degree chord; `<1 3 g>` is rejected upstream).
            case ChordSyntax c:
                return c.Root is { } root ? StepOf(root)
                    : c.Degrees.Any() ? Tonic : null;

            // A `q` anchors like the chord it repeats (an unresolvable one —
            // no chord before it — anchors nothing and the scan continues).
            case ChordRepetitionSyntax q:
                if (ChordRepetitions.OriginalOf(q) is { IsShapeChord: true } shapeOrig)
                {
                    shape = shapeOrig;
                    return Shape;
                }
                return ChordRepetitions.OriginalOf(q) is { } orig
                    ? (orig.Root is { } qr ? StepOf(qr) : orig.Degrees.Any() ? Tonic : null)
                    : null;

            // An arpeggio anchors on its first PITCHED member (leading rests
            // just advance time) — the same rule its own processing applies.
            case ArpeggioSyntax a:
                foreach (var member in a.Members)
                {
                    // A chord(…) member first: the group's root is its lowest note (ArpeggioSpread).
                    if (member is ChordSyntax { IsShapeChord: true } spread)
                    {
                        shape = spread;
                        return Shape;
                    }
                    int? m = member switch
                    {
                        ScaleDegreeSyntax => Tonic,
                        PitchSyntax mp => StepOf(mp),
                        ChordSyntax mc => mc.Root is { } mr ? StepOf(mr)
                            : mc.Degrees.Any() ? Tonic : null,
                        _ => null,
                    };
                    if (m is not null)
                        return m;
                }
                return null;

            // A grace body runs in a nested frame and never advances the
            // chain — the anchor is the first MAIN note, so skip it.
            case GraceExpressionSyntax:
                return null;

            // A body that opens with a nested reference anchors on THAT
            // phrase's anchor.
            case VariableReferenceSyntax v when depth < 16:
                return resolve(v.Name.Text) is { } nested
                    ? Walk(nested, resolve, depth + 1, ref shape)
                    : null;

            default:
                for (int i = 0; i < node.SlotCount; i++)
                {
                    if (node.GetChild(i) is { } child && child is not SyntaxTokenNode
                        && Walk(child, resolve, depth, ref shape) is { } found)
                        return found;
                }
                return null;
        }
    }

    private static int StepOf(PitchSyntax p)
        => "cdefgab".IndexOf(p.BaseName);
}
