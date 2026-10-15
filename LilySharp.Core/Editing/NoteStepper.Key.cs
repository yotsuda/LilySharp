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
/// changes, never the notes after it. The octave pair (PageUp / PageDown) switches to the
/// parallel / relative key (<see cref="ModeEdits"/>). A <c>key custom …</c> steps nothing, and
/// says so.
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

    /// <summary>The edits one step of a key makes (none when it cannot move) and the status-bar
    /// message: the semitone pair walks the circle (<see cref="KeyEdit"/>), the octave pair
    /// switches the mode (<see cref="ModeEdits"/>).</summary>
    private static (Edit[] Edits, string Message) KeyStep(KeySignatureSyntax key, int dir, bool semitone,
        bool longFlats)
    {
        if (!semitone)
            return ModeEdits(key, dir, longFlats);
        var (edit, said) = KeyEdit(key, dir, longFlats);
        return (edit is { } e ? [e] : [], said);
    }

    /// <summary>
    /// The octave pair on a key (owner's decision 2026-10-16): PageUp the PARALLEL key — the same
    /// tonic, the other mode (<c>c major</c> ⇄ <c>c minor</c>) — PageDown the RELATIVE key — the
    /// same signature, the other mode (<c>c major</c> ⇄ <c>a minor</c>). Lily#'s choices: which key
    /// does which (the order the owner named them), and only <c>major</c> / <c>minor</c> switch;
    /// a church mode says so.
    /// </summary>
    private static (Edit[] Edits, string Message) ModeEdits(KeySignatureSyntax key, int dir, bool longFlats)
    {
        if (key.IsCustom)
            return ([], "key custom: no parallel or relative key - write a key such as c major");
        if (key.GetChild(2) is not PitchSyntax tonic || tonic.BaseName == '\0'
            || key.GetChild(3) is not SyntaxTokenNode { Text.Length: > 0 } modeToken)
            return ([], "key: write its tonic and mode first (key c major)");
        string mode = modeToken.Text.ToLowerInvariant();
        if (mode is not ("major" or "minor"))
            return ([], $"key {tonic.PitchName} {mode}: the parallel and relative keys switch major and minor only");
        string other = mode == "major" ? "minor" : "major";
        var modeEdit = new Edit(modeToken.Span.Start, modeToken.Span.End, other);
        if (dir > 0)
            return ([modeEdit], $"key {tonic.PitchName} {other}: the parallel {other} key");
        if (KeySpelling.SharpsFor(tonic.PitchName, mode) is not int sharps)
            return ([], $"key {tonic.PitchName} {mode}: not a key the step knows");
        if (TonicFor(sharps, other, longFlats) is not { } name)
            return ([], $"key {tonic.PitchName} {mode}: no relative {other} key to spell");
        var token = tonic.PitchToken;
        return ([new Edit(token.Span.Start, token.Span.End, name), modeEdit],
            $"key {name} {other}: the relative {other} key ({Accidentals(sharps)})");
    }

    /// <summary>The tonic, in the file's flats, whose <paramref name="mode"/> signature has
    /// <paramref name="sharps"/> sharps; null when none does.</summary>
    private static string? TonicFor(int sharps, string mode, bool longFlats)
    {
        for (int step = 0; step < 7; step++)
            for (int alter = -1; alter <= 1; alter++)
                if (KeySpelling.SharpsFor(NameOf(step, alter, longFlats: true), mode) == sharps)
                    return NameOf(step, alter, longFlats);
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
        if (TonicFor(next, mode, longFlats) is not { } name)
            return (null, $"key {tonic.PitchName} {mode}: not a key the step knows");
        var token = tonic.PitchToken;
        return (new Edit(token.Span.Start, token.Span.End, name), $"key {name} {mode}: {Accidentals(next)}");
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
