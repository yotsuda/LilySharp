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

using System.Collections.Generic;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// An <c>@struck</c> with no chord held under it
/// (<see cref="DiagnosticCodes.StruckWithNothingHeld"/>; <see cref="StruckNotes"/>): the mark is
/// one member's of a <c>&lt;…&gt;</c> chord, the note struck with the chord and let go, so a single
/// note, a whole chord, a member of a written-out arpeggio and a chord marked on every member
/// have nothing to be struck with.
/// </summary>
internal sealed class StruckNoteValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(AnnotationNameValidator.AnnotationKinds))
        {
            if (node is not ArticulationSyntax { Name: StruckNotes.Name } struck)
                continue;
            if (struck.Parent is PitchSyntax pitch && StruckNotes.IsStruck(pitch))
                continue;
            string why = struck.Parent is PitchSyntax { Parent: ChordSyntax }
                ? "every member of this chord is marked, so none of it is held"
                : "this is not a member of a <...> chord";
            _diagnostics.Error(struck.Span, DiagnosticCodes.StruckWithNothingHeld,
                $"'@struck' is a chord member's - the note struck with the chord and let go while the rest is held, "
                + $"drawn as a small black head on the chord's stem - and {why}: the note is drawn and played as "
                + "written. Write it on one member, inside the brackets (<c e g@struck>2).");
        }

        // A tie from a struck member (owner's decision 2026-11-20): the page draws it, as LilyPond
        // does (Lab sessions/p1330/s2 bar 5), but the member is let go — the tie does not sound
        // and MusicXML does not tie it.
        foreach (var chord in tree.GetRoot().DescendantNodes<ChordSyntax>())
        {
            if (chord.IsEmpty)
                continue;
            // Ties written inside the brackets tie those members alone; else the chord's tie, all.
            bool memberTies = chord.HasTiedMembers;
            foreach (var pitch in chord.Pitches)
            {
                if (!StruckNotes.IsStruck(pitch))
                    continue;
                var tie = memberTies ? MemberTie(chord, pitch) : FirstTie(chord.Articulations) ?? TieAfter(chord);
                if (tie != null)
                    _diagnostics.Warning(tie.Span, DiagnosticCodes.TieFromAStruckNote,
                        $"The tie reaches the struck member {pitch.PitchName}, which is let go after it is struck: "
                        + "the page draws the tie, as LilyPond does, but it does not sound and MusicXML does not tie it. "
                        + "Tie the held members only (<c~ e~ g@struck>).");
            }
        }
    }

    /// <summary>The tie written inside the brackets after <paramref name="member"/>, before the next member.</summary>
    private static TieSyntax? MemberTie(ChordSyntax chord, PitchSyntax member)
    {
        bool after = false;
        foreach (var child in chord.ChildNodes())
        {
            if (ReferenceEquals(child, member))
                after = true;
            else if (after && child is PitchSyntax or ScaleDegreeSyntax or DrumNoteSyntax)
                return null;
            else if (after && child is TieSyntax tie)
                return tie;
            else if (child is SyntaxTokenNode { Kind: SyntaxKind.CloseAngle })
                return null;
        }
        return null;
    }

    /// <summary>The chord's own tie, <c>&lt;c e g&gt;2~</c>: the item written right after it in its sequence.</summary>
    private static TieSyntax? TieAfter(ChordSyntax chord)
    {
        if (chord.Parent is not { } parent)
            return null;
        bool after = false;
        foreach (var child in parent.ChildNodes())
        {
            if (ReferenceEquals(child, chord))
                after = true;
            else if (after)
                return child as TieSyntax;
        }
        return null;
    }

    private static TieSyntax? FirstTie(IEnumerable<SyntaxNode> marks)
    {
        foreach (var mark in marks)
            if (mark is TieSyntax tie)
                return tie;
        return null;
    }
}
