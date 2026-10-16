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
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The step keys on a dynamic, a <c>time</c>, a part's <c>octave N</c> and a <c>transpose</c>
/// (owner's decision 2026-10-16, on Lily#'s proposal). Up is louder, more, higher; only the
/// written value changes.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A dynamic (<c>@p</c>, <c>@mf.up</c>, <c>@p.subito</c>): either pair steps the level
/// one mark, <c>ppppp</c> … <c>fffff</c>. On <c>@cresc</c> / <c>@decresc</c> / <c>@dim</c>
/// Up writes <c>@cresc</c> and Down <c>@decresc</c> — the owner: one pair of keys to remember,
/// not another. Lily#'s choices: the pair sets the direction rather than toggling, so a second
/// press stays; <c>@sfz</c>, <c>@fp</c> and the like have no place on the ladder and say so.</item>
/// <item>A <c>time</c>: the semitone pair steps the beats by 1 (<c>3/4</c> → <c>4/4</c>, never
/// below 1), the octave pair doubles or halves the beat unit (<c>6/8</c> ⇄ <c>6/4</c>, 1 to 64).
/// An additive meter (<c>3+2/8</c>) and <c>time none</c> step nothing.</item>
/// <item>A part's <c>octave N</c>: either pair steps N by 1, 0 to 8 (Lily#'s bounds) — every
/// bare note of the part moves with it.</item>
/// <item>A <c>transpose</c> (a part's, or the top-level default): the semitone pair moves the
/// target a semitone, spelled so the transposed key has the fewest sharps or flats (from the
/// file's first <c>key</c>; C major without one), black keys as flats on a tie
/// (<see cref="PitchTransposer.IntervalFromSemitones"/>'s spelling); the octave pair adds or
/// takes a <c>'</c> / <c>,</c> mark. The owner asked whether a NUMBER of semitones should be
/// writable instead; the language keeps the pitch (a number leaves the spelling open — +1 from
/// C major is D♭ or C♯), and the step gives the number's convenience.</item>
/// </list>
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The dynamic levels, softest first.</summary>
    private static readonly string[] DynamicLadder =
        [.. new[] { "ppppp", "pppp", "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "ffff", "fffff" }
            .Where(SyntaxFacts.IsDynamicText)];

    /// <summary>The beat units a <c>time</c> steps between.</summary>
    private const int LargestBeatUnit = 64;

    /// <summary>The bounds of a part's <c>octave N</c> (Lily#'s choice: C0 to C8).</summary>
    private const int LowestOctave = 0, HighestOctave = 8;

    /// <summary>A dynamic the caret at <paramref name="offset"/> is on — strictly after its
    /// <c>@</c>, which also touches the note before it — or null.</summary>
    private static DynamicSyntax? DynamicAt(SyntaxTree tree, int offset)
    {
        foreach (var d in tree.GetNodes<DynamicSyntax>())
            if (d.SourceStart < offset && offset <= d.Span.End)
                return d;
        return null;
    }

    /// <summary>A <c>time</c> the caret is on, from its keyword to its last token, or null.</summary>
    private static TimeSignatureSyntax? TimeAt(SyntaxTree tree, int offset)
    {
        foreach (var t in tree.GetNodes<TimeSignatureSyntax>())
            if (t.SourceStart <= offset && offset <= t.Span.End)
                return t;
        return null;
    }

    /// <summary>A header's <c>octave N</c> or <c>transpose X</c> the caret is on, or null.</summary>
    private static PropertyAssignmentSyntax? ValuePropertyAt(SyntaxTree tree, int offset)
    {
        foreach (var p in tree.GetNodes<PropertyAssignmentSyntax>())
            if (p.NameToken.Text is "octave" or "transpose"
                && p.SourceStart <= offset && offset <= p.Span.End)
                return p;
        return null;
    }

    /// <summary>One step of a dynamic: the edit (null when it cannot move) and the message.</summary>
    private static (Edit? Edit, string Message) DynamicEdit(DynamicSyntax dynamic, int dir)
    {
        var token = dynamic.DynamicToken;
        string name = token.Text;
        string? next;
        if (SyntaxFacts.IsDynamicSpannerName(name))
        {
            next = dir > 0 ? "cresc" : "decresc";
            if (next == name)
                return (null, $"@{name}: already {(dir > 0 ? "a crescendo - Down writes @decresc" : "a decrescendo - Up writes @cresc")}");
        }
        else
        {
            int at = Array.IndexOf(DynamicLadder, name);
            if (at < 0)
                return (null, $"@{name}: not on the ladder pp - p - mp - mf - f - ff");
            int to = at + dir;
            if (to < 0 || to >= DynamicLadder.Length)
                return (null, $"@{name}: already the {(dir > 0 ? "loudest" : "softest")}");
            next = DynamicLadder[to];
        }
        return (new Edit(token.Span.Start, token.Span.End, next), $"@{next}");
    }

    /// <summary>One step of a <c>time</c>: the semitone pair the beats, the octave pair the unit.</summary>
    private static (Edit? Edit, string Message) TimeEdit(TimeSignatureSyntax time, int dir, bool semitone)
    {
        if (time.IsSenzaMisura)
            return (null, "time none: no meter to step");
        if (time.BeatsText != null)
            return (null, $"time {time.BeatsText}/…: an additive meter steps nothing - edit its terms");
        if (time.Denominator is not { } unitToken || !int.TryParse(time.Numerator.Text, out int beats)
            || !int.TryParse(unitToken.Text, out int unit))
            return (null, "time: write its meter first (time 4/4)");
        if (semitone)
        {
            int next = beats + dir;
            if (next < 1)
                return (null, $"time {beats}/{unit}: already one beat");
            var n = time.Numerator;
            return (new Edit(n.Span.Start, n.Span.End, next.ToString()), $"time {next}/{unit}");
        }
        int nextUnit = dir > 0 ? unit * 2 : unit / 2;
        if (nextUnit < 1 || nextUnit > LargestBeatUnit || (dir < 0 && unit % 2 != 0))
            return (null, $"time {beats}/{unit}: no {(dir > 0 ? "shorter" : "longer")} beat unit");
        return (new Edit(unitToken.Span.Start, unitToken.Span.End, nextUnit.ToString()), $"time {beats}/{nextUnit}");
    }

    /// <summary>One step of a header's <c>octave N</c> or <c>transpose X</c>.</summary>
    private static (Edit? Edit, string Message) ValuePropertyEdit(string text, SyntaxTree tree,
        PropertyAssignmentSyntax property, int dir, bool semitone)
    {
        var values = property.Values.OfType<SyntaxTokenNode>().ToList();
        if (property.NameToken.Text == "octave")
        {
            if (values is not [var only] || !int.TryParse(only.Text, out int octave))
                return (null, $"octave {Written(values)}: the step changes a number (octave 3)");
            int next = octave + dir;
            if (next < LowestOctave || next > HighestOctave)
                return (null, $"octave {octave}: already the {(dir > 0 ? "highest" : "lowest")} ({LowestOctave} to {HighestOctave})");
            return (new Edit(only.Span.Start, only.Span.End, next.ToString()),
                $"octave {next}: a bare c is C{next}");
        }
        if (values.Count == 0 || !PitchTransposer.TryParseTarget(values[0].Text, out int step, out int alter))
            return (null, $"transpose {Written(values)}: the step moves a pitch (transpose d)");
        var head = values[0];
        int nameEnd = head.Span.End;
        if (!semitone)
        {
            // The octave pair: a mark, as on a note.
            var mark = MarkEdit(text, nameEnd, dir);
            int marks = Marks(text, nameEnd) + (dir > 0 ? 1 : -1);
            return (mark, $"transpose {head.Text}{MarksText(marks)}: "
                + Said(PitchTransposer.IntervalSemitones(step, alter, marks)));
        }
        int was = PitchTransposer.IntervalSemitones(step, alter, Marks(text, nameEnd));
        int target = was + dir;
        var (keySharps, keyName) = FirstKey(tree);
        (int Step, int Alter, int Octave, int Cost)? best = null;
        for (int s = 0; s < 7; s++)
            for (int a = -1; a <= 1; a++)
            {
                int rest = target - RelativeOctave.StepSemitoneOf(s) - a;
                if (((rest % 12) + 12) % 12 != 0)
                    continue;
                // Fewest sharps or flats in the transposed key; a tie goes to the flat spelling.
                int cost = Math.Abs(keySharps + PitchTransposer.KeySignatureFifthsShift(s, a)) * 4
                    + (PitchTransposer.IntervalFromSemitones(target) is var (fs, fa, _) && fs == s && fa == a ? 0 : 1);
                if (best is not { } b || cost < b.Cost)
                    best = (s, a, (int)Math.Floor(rest / 12.0), cost);
            }
        var (ns, na, no, _) = best!.Value;
        string name = NameOf(ns, na, longFlats: true) + MarksText(no);
        int end = MarksEnd(text, nameEnd);
        string key = keyName is { } k
            && TonicFor(keySharps + PitchTransposer.KeySignatureFifthsShift(ns, na), k.Mode, longFlats: true) is { } tonic
            ? $", key {k.Tonic} {k.Mode} -> {tonic} {k.Mode}" : "";
        return (new Edit(head.Span.Start, end, name), $"transpose {name}: {Said(target)}{key}");

        static string Written(System.Collections.Generic.List<SyntaxTokenNode> v)
            => string.Concat(v.Select(t => t.Text));
        static string Said(int semis) => semis == 0 ? "no shift"
            : $"{(semis > 0 ? "up" : "down")} {Math.Abs(semis)} semitone{(Math.Abs(semis) == 1 ? "" : "s")}";
    }

    /// <summary>The net octave of the <c>'</c> / <c>,</c> marks written at <paramref name="at"/>.</summary>
    private static int Marks(string text, int at)
    {
        int n = 0;
        for (int i = at; i < MarksEnd(text, at); i++)
            n += text[i] == '\'' ? 1 : -1;
        return n;
    }

    private static string MarksText(int octave) => octave >= 0 ? new string('\'', octave) : new string(',', -octave);

    /// <summary>The file's first <c>key</c>: its sharps, and its tonic and mode (null with none).</summary>
    private static (int Sharps, (string Tonic, string Mode)? Name) FirstKey(SyntaxTree tree)
    {
        foreach (var key in tree.GetNodes<KeySignatureSyntax>())
            if (!key.IsCustom && key.GetChild(2) is PitchSyntax tonic
                && key.GetChild(3) is SyntaxTokenNode { Text.Length: > 0 } mode
                && KeySpelling.SharpsFor(tonic.PitchName, mode.Text.ToLowerInvariant()) is int sharps)
                return (sharps, (tonic.PitchName, mode.Text.ToLowerInvariant()));
        return (0, null);
    }
}
