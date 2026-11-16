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

/// <summary>Which of the two spellings a view transpose takes where the key it lands in has
/// two (B / C♭, F♯ / G♭, C♯ / D♭ and their minors).</summary>
public enum ViewSpelling
{
    /// <summary>The key with fewer accidentals, sharps at the tritone (<see cref="FileShift"/>'s rule).</summary>
    Auto,
    /// <summary>The sharp key of the pair.</summary>
    Sharp,
    /// <summary>The flat key of the pair.</summary>
    Flat,
}

/// <summary>
/// The preview's view-only transpose (owner's request 2026-11-16): the recording panel's
/// semitones move the score's picture and its playback with the recording, and the file is not
/// edited. It composes OUTSIDE the chosen score's own <c>transpose</c> (<see cref="RenderSpec.ScoreTranspose"/>),
/// and so reaches whatever that interval reaches — notes, key signatures, chord rows, <c>@chord</c>s,
/// what is played.
/// </summary>
/// <remarks>
/// THE SPELLING is <see cref="FileShift"/>'s: the key the music is in — the file's first
/// <c>key</c> (C major without one, as a staff draws it), moved by the file's <c>shift</c> and the
/// score's <c>transpose</c> — moved <see cref="Semitones"/> and brought into five flats … six
/// sharps. Where that key has a twin (B / C♭, F♯ / G♭, D♭ / C♯) <see cref="Spelling"/> may take
/// the other (owner's decision 2026-11-16: the toggle works only there; anywhere else the other
/// spelling would be a key of eight or more accidentals).
/// LILYSHARP-OWN: an editor's view, with no LilyPond counterpart.
/// </remarks>
public sealed record ViewTranspose(int Semitones, ViewSpelling Spelling = ViewSpelling.Auto)
{
    /// <summary>The interval a collector moves the chosen score by: the score's own
    /// <c>transpose</c> with the view composed outside it — or the score's own alone when there
    /// is no view to apply.</summary>
    public static (int step, int alt, int oct)? ScoreInterval(
        SyntaxNode root, RenderSpec? spec, ViewTranspose? view)
        => view is { Semitones: not 0 } v
            ? PitchTransposer.Compose(spec?.ScoreTranspose, v.Interval(root, spec))
            : spec?.ScoreTranspose;

    /// <summary>The view's own c→target interval, spelled as the remarks say.</summary>
    public (int step, int alt, int oct) Interval(SyntaxNode root, RenderSpec? spec)
    {
        var (from, to, _) = Keys(root, spec);
        var (step, alt) = PitchTransposer.IntervalFromFifths(to - from);
        int within = RelativeOctave.StepSemitoneOf(step) + alt;
        return (step, alt, FloorDiv(Semitones - within, 12));
    }

    /// <summary>The key signature the music is in before the view (<paramref name="spec"/>'s
    /// score), the one it lands in, and whether that one has a twin the toggle can take.</summary>
    public (int From, int To, bool HasTwin) Keys(SyntaxNode root, RenderSpec? spec)
    {
        int from = FileShift.KeySharpsAfter(FileShift.FirstKeySharps(root), FileShift.ReadSemitones(root));
        if (spec?.ScoreTranspose is { } t)
            from += PitchTransposer.KeySignatureFifthsShift(t.step, t.alt);
        int auto = FileShift.KeySharpsAfter(from, Semitones);   // five flats … six sharps
        bool twin = auto is >= 5 or <= -5;
        int to = Spelling switch
        {
            ViewSpelling.Flat when auto >= 5 => auto - 12,       // B → C♭, F♯ → G♭
            ViewSpelling.Sharp when auto <= -5 => auto + 12,     // D♭ → C♯
            _ => auto,
        };
        return (from, to, twin);
    }

    /// <summary>The name of the key the view lands in, as a chart writes it — <c>G♭</c>,
    /// <c>E♭m</c>, <c>D dorian</c> — in the file's first key's mode (major without one).</summary>
    public string KeyName(SyntaxNode root, RenderSpec? spec)
    {
        string mode = FirstMode(root);
        int to = Keys(root, spec).To;
        for (int step = 0; step < 7; step++)
            for (int alter = -1; alter <= 1; alter++)
                if (KeySpelling.SharpsFor(LilyName(step, alter), mode) == to)
                {
                    string tonic = char.ToUpperInvariant("cdefgab"[step]) + (alter switch { 1 => "♯", -1 => "♭", _ => "" });
                    return mode switch { "major" => tonic, "minor" => tonic + "m", _ => tonic + " " + mode };
                }
        return "";
    }

    private static string LilyName(int step, int alter)
        => "cdefgab"[step] + (alter switch { 1 => "is", -1 => "es", _ => "" });

    private static string FirstMode(SyntaxNode root)
    {
        foreach (var node in root.GreenSites(static g => (g.Kind == SyntaxKind.KeySignature, Descend: true)))
            if (node is KeySignatureSyntax key && !key.IsCustom && key.GetChild(2) is PitchSyntax tonic
                && key.GetChild(3) is SyntaxTokenNode { Text.Length: > 0 } mode
                && KeySpelling.SharpsFor(tonic.PitchName, mode.Text.ToLowerInvariant()) is int)
                return mode.Text.ToLowerInvariant();
        return "major";
    }

    private static int FloorDiv(int a, int b) => (int)System.Math.Floor((double)a / b);
}
