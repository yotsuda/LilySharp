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
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The step keys on a <c>key</c> (owner's decision 2026-10-16): Ctrl+Shift+Up / Down — the
/// semitone pair — move it one fifth round the circle, one sharp more or one flat more
/// (<c>key c major</c> → <c>key g major</c> → <c>key d major</c>; Down → <c>key f major</c>).
/// </summary>
/// <remarks>
/// Lily#'s choices: the mode stays (<c>a minor</c> → <c>e minor</c>, <c>d dorian</c> →
/// <c>a dorian</c>); the walk stops at seven sharps or seven flats; only the signature's text
/// changes, never the notes after it. The octave pair (PageUp / PageDown) is not a key step —
/// on a key it keeps VS Code's own command. A <c>key custom …</c> steps nothing, and says so.
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The most sharps or flats the step walks to.</summary>
    private const int MostAccidentalsInAKey = 7;

    /// <summary>The <c>key</c> the caret at <paramref name="offset"/> is on — from its keyword
    /// to its last word — or null.</summary>
    private static KeySignatureSyntax? KeyAt(SyntaxTree tree, int offset)
    {
        foreach (var key in tree.GetNodes<KeySignatureSyntax>())
            if (key.SourceStart <= offset && offset <= key.Span.End)
                return key;
        return null;
    }

    /// <summary>The edit one step of a key makes (null when it cannot move) and the
    /// status-bar message.</summary>
    private static (Edit? Edit, string Message) KeyEdit(KeySignatureSyntax key, int dir, bool longFlats)
    {
        if (key.IsCustom)
            return (null, "key custom: no circle of fifths to step - write a key such as c major");
        if (key.GetChild(2) is not PitchSyntax tonic || tonic.BaseName == '\0'
            || key.GetChild(3) is not SyntaxTokenNode { Text.Length: > 0 } modeToken)
            return (null, "key: write its tonic and mode first (key c major)");
        string mode = modeToken.Text.ToLowerInvariant();
        if (KeySpelling.SharpsFor(tonic.PitchName, mode) is not int sharps)
            return (null, $"key {tonic.PitchName} {mode}: not a key the step knows");
        int next = sharps + dir;
        if (Math.Abs(next) > MostAccidentalsInAKey)
            return (null, $"key {tonic.PitchName} {mode}: already {MostAccidentalsInAKey} "
                + (dir > 0 ? "sharps" : "flats"));
        // The tonic a fifth up (or down) whose signature has `next` sharps: one spelling only.
        for (int step = 0; step < 7; step++)
            for (int alter = -1; alter <= 1; alter++)
                if (KeySpelling.SharpsFor(NameOf(step, alter, longFlats: true), mode) == next)
                {
                    string name = NameOf(step, alter, longFlats);
                    var token = tonic.PitchToken;
                    return (new Edit(token.Span.Start, token.Span.End, name),
                        $"key {name} {mode}: {Accidentals(next)}");
                }
        return (null, $"key {tonic.PitchName} {mode}: not a key the step knows");
    }

    /// <summary>"2 sharps", "1 flat", "no sharps or flats".</summary>
    private static string Accidentals(int sharps) => sharps switch
    {
        0 => "no sharps or flats",
        1 => "1 sharp",
        -1 => "1 flat",
        > 0 => $"{sharps} sharps",
        _ => $"{-sharps} flats",
    };
}
