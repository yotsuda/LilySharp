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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// A script on an empty chord whose next item is a spacer — <c>c''2 &lt;&gt;@accent s4 d''4</c>
/// (<see cref="DiagnosticCodes.ScriptBeforeASpacer"/>; owner's decision 2026-11-20): an accent,
/// a staccato, a fermata is a note's mark and a spacer is no note, so it goes on to the next
/// note in every output (the page, MIDI, MusicXML), and this says so where it is written.
/// </summary>
/// <remarks>
/// Scripts only. A level, a hairpin, words, a pedal or a line standing at a spacer's moment is
/// the ordinary way to place one between notes (LilyPond's own <c>s4\p</c>), and is left alone.
/// LilyPond hangs such a script on the spacer's moment with no support (Lab sessions/p1329/g sp1).
/// </remarks>
internal sealed class SpacerScriptValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var chord in tree.GetRoot().DescendantNodes<ChordSyntax>())
        {
            if (!chord.IsEmpty || chord.Parent is not { } parent || NextItem(parent, chord) is not RestSyntax { RestText: "s" })
                continue;
            foreach (var mark in chord.Articulations)
                if (mark is ArticulationSyntax { Type: not ArticulationType.None } script)
                    _diagnostics.Warning(script.Span, DiagnosticCodes.ScriptBeforeASpacer,
                        $"'@{script.Name}' on the empty chord stands at a spacer, which is no note: a script marks a note, "
                        + "so it goes on to the next note in this voice. Write it on that note, or on a note or a rest here.");
        }
    }

    /// <summary>The item after <paramref name="chord"/> in its sequence, past bar lines.</summary>
    private static SyntaxNode? NextItem(SyntaxNode parent, ChordSyntax chord)
    {
        bool after = false;
        foreach (var child in parent.ChildNodes())
        {
            if (ReferenceEquals(child, chord))
                after = true;
            else if (after && child is not BarlineSyntax)
                return child;
        }
        return null;
    }
}
