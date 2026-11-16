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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>Which of the two keys a numeric <c>transpose</c> lands in where the key has a twin
/// (B / C♭, F♯ / G♭, D♭ / C♯ and their minors): <c>transpose +6 flat</c>.</summary>
public enum TransposeSpelling
{
    /// <summary>The key with fewer accidentals, sharps at the tritone.</summary>
    Auto,
    /// <summary>The sharp key of a twin pair.</summary>
    Sharp,
    /// <summary>The flat key of a twin pair.</summary>
    Flat,
}

/// <summary>
/// The file's top-level <c>transpose</c> (owner's decision 2026-11-16, which retired <c>shift N</c>
/// into it): the whole piece moved — every pitched part with its key signatures, its own
/// <c>transpose</c> and a transposing instrument's shift included, and every chord row — on the
/// page, in the MIDI, in the MusicXML and in the LilyPond twin. It composes OUTSIDE everything a
/// part says for itself (the <c>fileShift</c> argument of <see cref="PartTranspose"/>'s
/// <c>Read</c>). Drum notes are not pitches and stay where they are.
/// </summary>
/// <remarks>
/// <para>
/// TWO FORMS, one interval. <c>transpose d</c> (a pitch, with octave marks: LilyPond's
/// <c>\transpose c d</c>) is the interval as written. <c>transpose +2</c> / <c>transpose -1</c> (a
/// number of semitones, −11…+11) leaves the spelling open (+1 from C major is D♭ or C♯), so it is
/// turned into ONE c→target interval (<see cref="Interval"/>): the target key is the file's first
/// <c>key</c> (C major without one, as a staff draws it) moved N semitones and brought into five
/// flats … six sharps (<see cref="ChordStructure.PressedKeySharps"/>, sharps at the tritone) —
/// C major +1 is D♭ major, −1 B major, +6 F♯ major. Where that key has a twin, <c>sharp</c> /
/// <c>flat</c> after the number takes the one asked for (<c>transpose +6 flat</c>: G♭); anywhere
/// else the other spelling would be a key of eight or more accidentals, and the word changes
/// nothing (<see cref="HasTwin"/>, which the validator warns on).
/// </para>
/// <para>
/// The chord rows move by the same interval as the notes (MeasureCollector.SetRowMove, with the
/// score's own <c>transpose</c>), so they spell as the notes do. A WRITTEN chord shape is a shape of
/// the written chord: under a transpose the row's diagrams draw each chord's usual shape on its new
/// root instead (ChordNameCollector.DiagramOf).
/// </para>
/// <para>Until 2026-11-16 a top-level <c>transpose</c> was only the default of the parts that
/// wrote none, and moved no chord row; <c>shift N</c> did what this does.</para>
/// <para>LILYSHARP-OWN: LilyPond has <c>\transpose FROM TO</c> and no number of semitones.</para>
/// </remarks>
public static class FileTranspose
{
    /// <summary>The most a numeric transpose moves either way: eleven semitones (an octave is the
    /// pitch form's marks, <c>transpose c'</c>, or a part's <c>octave N</c>).</summary>
    public const int Max = 11;

    /// <summary>Whether <paramref name="prop"/> is THE site the file's transpose is read from — a
    /// top-level <c>transpose</c> outside every part header and score block. One predicate for the
    /// whole-tree reader and for the collector's definitions walk.</summary>
    public static bool IsSite(PropertyAssignmentSyntax prop)
        => TransposeValue.IsTranspose(prop) && !prop.IsInside<PartDeclarationSyntax>() && !prop.IsInside<RenderDeclarationSyntax>();

    /// <summary>The file's transpose site, or null: the first in the pre-order the collector's
    /// definitions walk meets it.</summary>
    public static PropertyAssignmentSyntax? Site(SyntaxNode root)
    {
        foreach (var prop in root.GreenSites(
                     static g => (g.Kind == SyntaxKind.PropertyAssignment, Descend: true)))
            if (prop is PropertyAssignmentSyntax pa && IsSite(pa))
                return pa;
        return null;
    }

    /// <summary>The file's transpose as the c→target interval every reader composes, or null for
    /// none (or a value the parser refused).</summary>
    public static (int step, int alt, int oct)? Read(SyntaxNode root)
        => Site(root) is { } site ? IntervalOf(site, () => FirstKeySharps(root)) : null;

    /// <summary>The file's transpose in semitones, 0 for none.</summary>
    public static int ReadSemitones(SyntaxNode root)
        => Read(root) is { } t ? PitchTransposer.IntervalSemitones(t.step, t.alt, t.oct) : 0;

    /// <summary>The interval a <c>transpose</c> property says, a numeric one spelled from the key
    /// <paramref name="fromSharps"/> answers (the key the music is in before it — asked only for a
    /// number); null for a value the parser refused. Shared by the file's, a part's and a score's
    /// <c>transpose</c>.</summary>
    public static (int step, int alt, int oct)? IntervalOf(PropertyAssignmentSyntax prop, System.Func<int> fromSharps)
        => TransposeValue.Parse(prop) switch
        {
            TransposeValue.Pitch p => (p.Step, p.Alt, p.Oct),
            TransposeValue.Amount { Semitones: 0 } => null,
            TransposeValue.Amount a => Interval(a.Semitones, fromSharps(), a.Spelling),
            _ => null,
        };

    /// <summary>
    /// The c→target interval that moves music by <paramref name="semitones"/>, spelled from the
    /// key of <paramref name="keySharps"/> (the remarks). Null for 0.
    /// </summary>
    public static (int step, int alt, int oct)? Interval(int semitones, int keySharps,
        TransposeSpelling spelling = TransposeSpelling.Auto)
    {
        if (semitones == 0)
            return null;
        int fifths = KeySharpsAfter(keySharps, semitones, spelling) - keySharps;
        var (step, alt) = PitchTransposer.IntervalFromFifths(fifths);
        // Seven fifths are a semitone (mod 12), so the interval's size agrees with the shift
        // mod 12, and the octave marks make up the difference exactly.
        int within = RelativeOctave.StepSemitoneOf(step) + alt;
        return (step, alt, FloorDiv(semitones - within, 12));
    }

    /// <summary>The key signature after a numeric transpose: <paramref name="keySharps"/> moved
    /// <paramref name="semitones"/>, brought into five flats … six sharps
    /// (<see cref="ChordStructure.PressedKeySharps"/>), then the twin <paramref name="spelling"/>
    /// asks for; unmoved for 0.</summary>
    public static int KeySharpsAfter(int keySharps, int semitones, TransposeSpelling spelling = TransposeSpelling.Auto)
    {
        if (semitones == 0)
            return keySharps;
        int auto = ChordStructure.PressedKeySharps(keySharps, -semitones);
        return spelling switch
        {
            TransposeSpelling.Flat when auto >= 5 => auto - 12,      // B → C♭, F♯ → G♭
            TransposeSpelling.Sharp when auto <= -5 => auto + 12,    // D♭ → C♯
            _ => auto,
        };
    }

    /// <summary>Whether the key a numeric transpose lands in (automatically) has a twin that
    /// <c>sharp</c> / <c>flat</c> can take.</summary>
    public static bool HasTwin(int keySharps, int semitones)
        => semitones != 0 && ChordStructure.PressedKeySharps(keySharps, -semitones) is >= 5 or <= -5;

    /// <summary>A chord row's drop with a transpose of <paramref name="semitones"/> folded in —
    /// semitones DOWN, 0..11, the number <see cref="ChordStructure.Pressed"/> takes.</summary>
    public static int RowDrop(int drop, int semitones) => (((drop - semitones) % 12) + 12) % 12;

    /// <summary>The sharps of the file's first <c>key</c> in document order (a part header's
    /// counts); 0 — C major — with none, or a <c>key custom</c>.</summary>
    public static int FirstKeySharps(SyntaxNode root)
    {
        foreach (var node in root.GreenSites(
                     static g => (g.Kind == SyntaxKind.KeySignature, Descend: true)))
            if (node is KeySignatureSyntax key && !key.IsCustom && key.GetChild(2) is PitchSyntax tonic
                && key.GetChild(3) is SyntaxTokenNode { Text.Length: > 0 } mode
                && KeySpelling.SharpsFor(tonic.PitchName, mode.Text.ToLowerInvariant()) is int sharps)
                return sharps;
        return 0;
    }

    private static int FloorDiv(int a, int b) => (int)System.Math.Floor((double)a / b);
}

/// <summary>What a <c>transpose</c> property writes: a pitch with octave marks
/// (<c>transpose bes,</c>) or a number of semitones with an optional spelling
/// (<c>transpose -2</c>, <c>transpose +6 flat</c>). One reader for the parser's three sites —
/// the top level, a part header, a score header.</summary>
public abstract record TransposeValue
{
    /// <summary><c>transpose d'</c>: the c→target interval as written.</summary>
    public sealed record Pitch(int Step, int Alt, int Oct) : TransposeValue;

    /// <summary><c>transpose +3 flat</c>: semitones, spelled by <see cref="FileTranspose.Interval"/>.</summary>
    public sealed record Amount(int Semitones, TransposeSpelling Spelling) : TransposeValue;

    /// <summary>The two words that may follow a number.</summary>
    public const string SharpWord = "sharp", FlatWord = "flat";

    /// <summary>Whether <paramref name="prop"/> is a <c>transpose</c> (any site).</summary>
    public static bool IsTranspose(PropertyAssignmentSyntax prop)
        => string.Equals(prop.NameToken.Text, "transpose", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>The value written, or null when it is neither form (the validator says why).</summary>
    public static TransposeValue? Parse(PropertyAssignmentSyntax prop)
    {
        // Children: name, [optional colon], then the value tokens.
        var tokens = new System.Collections.Generic.List<SyntaxTokenNode>();
        for (int ci = 1; ci < prop.SlotCount; ci++)
            if (prop.GetChild(ci) is SyntaxTokenNode tok && tok.Kind != SyntaxKind.Colon && tok.Text.Length > 0)
                tokens.Add(tok);
        if (tokens.Count == 0)
            return null;

        int i = 0, sign = 1;
        if (tokens[0].Kind is SyntaxKind.Plus or SyntaxKind.Minus)
        {
            sign = tokens[0].Kind == SyntaxKind.Minus ? -1 : 1;
            i = 1;
        }
        if (i < tokens.Count && tokens[i].Kind == SyntaxKind.IntegerLiteral)
        {
            if (!int.TryParse(tokens[i].Text, out int n) || n > FileTranspose.Max)
                return null;
            var spelling = TransposeSpelling.Auto;
            if (i + 1 < tokens.Count)
            {
                if (i + 2 < tokens.Count)
                    return null;
                switch (tokens[i + 1].Text)
                {
                    case SharpWord: spelling = TransposeSpelling.Sharp; break;
                    case FlatWord: spelling = TransposeSpelling.Flat; break;
                    default: return null;
                }
            }
            return new Amount(sign * n, spelling);
        }
        if (i != 0 || !PitchTransposer.TryParseTarget(tokens[0].Text, out int step, out int alt))
            return null;
        int oct = 0;
        for (int k = 1; k < tokens.Count; k++)
        {
            if (tokens[k].Kind == SyntaxKind.Apostrophe) oct++;
            else if (tokens[k].Kind == SyntaxKind.Comma) oct--;
            else return null;
        }
        return new Pitch(step, alt, oct);
    }
}
