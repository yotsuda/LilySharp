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
/// (owner's decision 2026-10-16, on Lily#'s proposal), and on an <c>octave absolute</c> /
/// <c>octave relative</c> (owner's request 2026-10-17). Up is louder, more, higher; only the
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
/// below 1); the octave pair walks the common meters by the length of their bar,
/// <see cref="CommonMeters"/> — owner's decision 2026-10-16, replacing the first day's doubling or
/// halving of the beat unit (<c>6/8</c> ⇄ <c>6/4</c>), a change music seldom wants. A meter off
/// the list steps to the next one longer or shorter. An additive meter (<c>3+2/8</c>) and
/// <c>time none</c> step nothing.</item>
/// <item>A part's <c>octave N</c>: either pair steps N by 1, 0 to 8 (Lily#'s bounds) — every
/// bare note of the part moves with it.</item>
/// <item>A <c>transpose</c> (a part's, or the top-level default): the semitone pair moves the
/// target a semitone, spelled so the transposed key has the fewest sharps or flats (from the
/// file's first <c>key</c>; C major without one), black keys as flats on a tie
/// (<see cref="PitchTransposer.IntervalFromSemitones"/>'s spelling); the octave pair adds or
/// takes a <c>'</c> / <c>,</c> mark. The owner asked whether a NUMBER of semitones should be
/// writable instead; the language keeps the pitch (a number leaves the spelling open — +1 from
/// C major is D♭ or C♯), and the step gives the number's convenience.</item>
/// <item>An <c>octave absolute</c> / <c>octave relative</c> — the file's or one inside music (in
/// a part header the mode is an error — a part's <c>octave</c> takes its base number): Up
/// converts the file to absolute octaves, Down to relative, on either pair
/// (a direction each, not a toggle — owner's decision 2026-10-17). The step makes no edit of
/// its own: the editor runs its Convert Octaves to Absolute / Relative command
/// (<see cref="OctaveModeConverter"/>), which re-marks every note to keep its pitch — the
/// owner's decision 2026-10-17, over a swap of the word alone: the pitches are wanted far more
/// often, and a word alone is typed by hand.</item>
/// </list>
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The dynamic levels, softest first.</summary>
    private static readonly string[] DynamicLadder =
        [.. new[] { "ppppp", "pppp", "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "ffff", "fffff" }
            .Where(SyntaxFacts.IsDynamicText)];

    /// <summary>The meters the octave pair walks, shortest bar first; the bars of equal length
    /// stand side by side (<c>6/8</c> ⇄ <c>3/4</c>, <c>2/2</c> ⇄ <c>4/4</c>), so one press compares
    /// the two ways of counting it. Lily#'s list, the owner's decision to walk one.</summary>
    private static readonly (int Beats, int Unit)[] CommonMeters =
        [(3, 8), (2, 4), (6, 8), (3, 4), (2, 2), (4, 4), (9, 8), (12, 8)];

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

    /// <summary>A header's <c>octave N</c> / <c>octave absolute</c> or <c>transpose X</c> the caret
    /// is on, or null.</summary>
    private static PropertyAssignmentSyntax? ValuePropertyAt(SyntaxTree tree, int offset)
    {
        foreach (var p in tree.GetNodes<PropertyAssignmentSyntax>())
            if (p.NameToken.Text is "octave" or "transpose" or "shift"
                && p.SourceStart <= offset && offset <= p.Span.End)
                return p;
        return null;
    }

    /// <summary>An <c>octave absolute</c> / <c>octave relative</c> directive the caret is on — the
    /// file's, or one inside music (a part header's is a property, <see cref="ValuePropertyAt"/>) —
    /// from its keyword to its mode, or null.</summary>
    private static OctaveDirectiveSyntax? OctaveModeAt(SyntaxTree tree, int offset)
    {
        foreach (var d in tree.GetNodes<OctaveDirectiveSyntax>())
            if (d.SourceStart <= offset && offset <= d.Span.End)
                return d;
        return null;
    }

    /// <summary>Whether the keys have a step for a dynamic of this name: a level on the ladder, or
    /// a hairpin word (cresc / decresc / dim). <c>@sfz</c>, <c>@fp</c> and the like have none, so
    /// the keys step the note they are written on (<see cref="CarrierOf"/>).</summary>
    private static bool DynamicSteps(string name)
        => SyntaxFacts.IsDynamicSpannerName(name) || Array.IndexOf(DynamicLadder, name) >= 0;

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
        // The octave pair: the next common meter. On the list, its neighbour; off it, the first
        // whose bar is longer (Up) or the last whose bar is shorter (Down).
        int at = Array.IndexOf(CommonMeters, (beats, unit));
        double length = (double)beats / unit;
        int to = at >= 0 ? at + dir
            : dir > 0 ? Array.FindIndex(CommonMeters, m => (double)m.Beats / m.Unit > length)
            : Array.FindLastIndex(CommonMeters, m => (double)m.Beats / m.Unit < length);
        if (to < 0 || to >= CommonMeters.Length)
            return (null, $"time {beats}/{unit}: no common meter {(dir > 0 ? "longer" : "shorter")} "
                + "(3/8 2/4 6/8 3/4 2/2 4/4 9/8 12/8) - Up / Down change the beats");
        var (nb, nu) = CommonMeters[to];
        return (new Edit(time.Numerator.Span.Start, unitToken.Span.End, $"{nb}/{nu}"), $"time {nb}/{nu}");
    }

    /// <summary>One step of a header's <c>octave N</c> or <c>transpose X</c>.</summary>
    private static (Edit? Edit, string Message) ValuePropertyEdit(string text, SyntaxTree tree,
        PropertyAssignmentSyntax property, int dir, bool semitone)
    {
        var values = property.Values.OfType<SyntaxTokenNode>().ToList();
        if (property.NameToken.Text == "shift")
            return ShiftEdit(tree, property, values, dir);
        if (property.NameToken.Text == "octave")
        {
            // `octave absolute` in a part header is a mode where a number belongs (the
            // validator says so): nothing to step, and the message says where the mode goes.
            if (values is not [var only] || !int.TryParse(only.Text, out int octave))
                return (null, $"octave {Written(values)}: the step changes a number (octave 3) - a mode (octave absolute) is written at the top of the file or in the music");
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

    /// <summary>One step of the file's <c>shift N</c> (<see cref="FileShift"/>, 2026-10-17): either
    /// pair moves it a semitone, −11 to +11; the status bar names the key it lands in.</summary>
    private static (Edit? Edit, string Message) ShiftEdit(SyntaxTree tree, PropertyAssignmentSyntax property,
        System.Collections.Generic.List<SyntaxTokenNode> values, int dir)
    {
        if (values.Count == 0 || FileShift.Parse(property) is not int was)
            return (null, $"shift {string.Concat(values.Select(t => t.Text))}: the step changes a number of semitones (shift +1)");
        int next = was + dir;
        if (next < -FileShift.Max || next > FileShift.Max)
            return (null, $"shift {Signed(was)}: already {FileShift.Max} semitones {(dir > 0 ? "up" : "down")} - an octave is the part's octave N");
        var (sharps, keyName) = FirstKey(tree);
        string said = keyName is { } k
            && TonicFor(FileShift.KeySharpsAfter(sharps, next), k.Mode, longFlats: true) is { } tonic
            ? $"shift {Signed(next)}: key {k.Tonic} {k.Mode} -> {tonic} {k.Mode}"
            : $"shift {Signed(next)}: {(next == 0 ? "as written" : $"{Math.Abs(next)} semitone{(Math.Abs(next) == 1 ? "" : "s")} {(next > 0 ? "up" : "down")}")}";
        return (new Edit(values[0].Span.Start, values[^1].Span.End, Signed(next)), said);

        static string Signed(int n) => n > 0 ? "+" + n : n.ToString();
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
