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
using System.Globalization;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The step keys on a <c>tempo</c> (owner's decision 2026-10-16): Ctrl+Shift+Up / Down — the
/// semitone pair — change the metronome number by 1, Ctrl+Shift+PageUp / PageDown — the
/// octave pair — by 10. The small step on the small key, the big step on the big one, as
/// with notes.
/// </summary>
/// <remarks>
/// The number stepped is the one <see cref="TempoValue"/> reads as the bpm
/// (<see cref="TempoDeclarationSyntax.ValuePositions"/>: <c>tempo 4 = |120</c>,
/// <c>tempo |120 swing</c>) — never the beat unit or the swing value. A tempo with no
/// number (<c>tempo "Allegro"</c>, <c>tempo swing</c>) is on the key but steps nothing,
/// and the status bar says why. Lily#'s choice: the number stops at 1.
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>What the octave pair adds to or takes from a metronome number.</summary>
    public const int TempoBigStep = 10;

    /// <summary>The <c>tempo</c> the caret at <paramref name="offset"/> is on — from its
    /// keyword to its last value — or null.</summary>
    private static TempoDeclarationSyntax? TempoAt(SyntaxTree tree, int offset)
    {
        foreach (var tempo in tree.GetNodes<TempoDeclarationSyntax>())
            if (tempo.SourceStart <= offset && offset <= TempoEnd(tempo))
                return tempo;
        return null;
    }

    /// <summary>Just past a tempo's last written token.</summary>
    private static int TempoEnd(TempoDeclarationSyntax tempo)
    {
        int end = tempo.TempoKeyword.Span.End;
        for (int i = 0; i < tempo.SlotCount; i++)
            if (tempo.GetChild(i) is SyntaxTokenNode t && t.Text.Length > 0)
                end = Math.Max(end, t.SourceStart + t.Text.Length);
        return end;
    }

    /// <summary>The edit one step of a tempo makes (null when it cannot move) and the
    /// status-bar message.</summary>
    private static (Edit? Edit, string Message) TempoEdit(TempoDeclarationSyntax tempo, int dir, bool small)
    {
        int at = tempo.ValuePositions.Count;
        var token = at > 0
            ? tempo.Values.OfType<SyntaxTokenNode>().FirstOrDefault(t => t.Span.Start == at)
            : null;
        if (token == null || !int.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int bpm))
            return (null, "tempo: no metronome number to step - write one (tempo 120)");
        int next = Math.Max(1, bpm + dir * (small ? 1 : TempoBigStep));
        if (next == bpm)
            return (null, $"tempo: {bpm} is the lowest");
        return (new Edit(token.Span.Start, token.Span.Start + token.Text.Length,
            next.ToString(CultureInfo.InvariantCulture)), $"tempo: {bpm} -> {next}");
    }
}
