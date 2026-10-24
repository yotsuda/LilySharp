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
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// <item>A part's <c>midiInstrument "…"</c> (owner's request 2026-10-24): the semitone pair walks
/// the 128 General MIDI names, the octave pair their families of eight
/// (<see cref="MidiInstrumentEdit"/>).</item>
/// <item>A part's <c>tuning</c> (owner's request 2026-10-24): the semitone pair walks the tunings,
/// the octave pair their families (<see cref="TuningEdit"/>); the new one sounds its open strings.</item>
/// <item>A percent mark (owner's request 2026-10-24): either pair the number of bars it repeats,
/// <c>%</c> ⇄ <c>%2</c> ⇄ <c>%3</c> (<see cref="PercentEdit"/>).</item>
/// <item>A score's <c>grandStaff</c> / <c>staffGroup</c> / <c>choirStaff</c> / <c>condensedStaff</c>
/// (owner's requests 2026-10-24): either pair the group's keyword (<see cref="StaffGroupEdit"/>),
/// and a <c>staff</c> / <c>tab</c> row the other word (<see cref="StaffOrTabEdit"/>); the part a
/// score row names, the file's next or previous part (<see cref="ScorePartNameEdit"/>).</item>
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

    /// <summary>One step of a part's <c>midiInstrument "…"</c> (owner's request 2026-10-24): the
    /// semitone pair walks the 128 General MIDI names one program at a time, the octave pair a
    /// family of eight at a time (piano, chromatic percussion, organ …: the first of the next or
    /// the previous family). It stops at either end; a name off the list steps from the ends, and
    /// a part with no name written reads as program 1, LilyPond's default.
    /// <para>⚠️ UP GOES TO THE LOWER PROGRAM (owner's decision 2026-10-24): the names are read as
    /// a list, program 1 at the top — the completion's order — so Up moves up it, towards
    /// <c>"acoustic grand"</c>, and Down towards <c>"gunshot"</c>.</para></summary>
    private static (Edit? Edit, string Message) MidiInstrumentEdit(PropertyAssignmentSyntax property, int dir, bool semitone)
    {
        var names = Midi.GeneralMidi.InstrumentNames;
        var token = InstrumentNameToken(property);
        string current = token != null ? StringLiteral.Value(token.Text) : names[0];
        int at = Midi.GeneralMidi.ProgramOf(current) ?? -1;
        int toward = -dir;   // +1: the next program, down the list
        int to = at < 0 ? (toward > 0 ? 0 : names.Count - 1)
            : semitone ? at + toward
            : toward > 0 ? (at / 8 + 1) * 8
            : at % 8 != 0 ? at / 8 * 8 : at - 8;
        if (to < 0 || to >= names.Count)
            return (null, $"midiInstrument \"{current}\": already the {(toward > 0 ? "last" : "first")} of the {names.Count} (program {at + 1})");
        string next = names[to];
        string said = $"midiInstrument \"{next}\": program {to + 1} of {names.Count}";
        return token != null
            ? (new Edit(token.Span.Start, token.Span.End, $"\"{next}\""), said)
            : (new Edit(property.NameToken.Span.End, property.NameToken.Span.End, $" \"{next}\""), said);
    }

    /// <summary>A part's <c>midiInstrument</c> the caret at <paramref name="offset"/> is on, from
    /// the word to its name, or null.</summary>
    private static PropertyAssignmentSyntax? InstrumentAt(SyntaxTree tree, int offset)
    {
        foreach (var p in tree.GetNodes<PropertyAssignmentSyntax>())
            if (p.NameToken.Text == "midiInstrument" && p.SourceStart <= offset && offset <= p.Span.End)
                return p;
        return null;
    }

    private static SyntaxTokenNode? InstrumentNameToken(PropertyAssignmentSyntax property)
        => property.Values.OfType<SyntaxTokenNode>().FirstOrDefault(v => v.Kind == SyntaxKind.StringLiteral);

    /// <summary>
    /// What a <c>midiInstrument</c> sounds (owner's request 2026-10-24: hear the instrument when it
    /// changes, by the keys or by typing): one note in its program, silent for a name that is not
    /// one of the 128. Lily#'s choice of the note: C4; G2 for the bass instruments, C3 for the low
    /// ones, C5 for the high ones — where each one sounds like itself.
    /// </summary>
    private static Voice InstrumentVoice(PropertyAssignmentSyntax property)
    {
        if (InstrumentNameToken(property) is not { } token
            || Midi.GeneralMidi.ProgramOf(StringLiteral.Value(token.Text)) is not int program)
            return Voice.Silent;
        return SampleVoice(program);
    }

    /// <summary>One note in <paramref name="program"/>, where its family sounds like itself — what a
    /// <c>midiInstrument</c> and an <c>instrument</c> preset sound when they change.</summary>
    private static Voice SampleVoice(int program)
    {
        int pitch = program switch
        {
            >= 32 and <= 39 or 43 or 58 or 70 => 43,   // bass, contrabass, tuba, bassoon
            42 or 47 or 67 => 48,                      // cello, timpani, baritone sax
            >= 8 and <= 15 or >= 72 and <= 79 => 72,   // chromatic percussion, pipe
            _ => 60,
        };
        return new Voice([pitch], Midi.GeneralMidi.PreviewTimbreFamily(program), program);
    }

    /// <summary>A percent mark (<c>%</c>, <c>%N</c>) the caret is on, from its <c>%</c> to its
    /// last digit, or null.</summary>
    private static PercentMarkSyntax? PercentAt(SyntaxTree tree, int offset)
    {
        foreach (var p in tree.GetNodes<PercentMarkSyntax>())
            if (p.Mark.Span.Start <= offset && offset <= p.Mark.Span.End)
                return p;
        return null;
    }

    /// <summary>One step of a percent mark (owner's request 2026-10-24): either pair repeats one
    /// bar more (Up) or fewer (Down) — <c>%</c> → <c>%2</c> → <c>%3</c>, and back to <c>%</c>,
    /// the spelling of one bar; 1 to <see cref="PercentMarkSyntax.MaxBars"/>. A count that is no
    /// number of bars (<c>%0</c>) steps from 1.</summary>
    private static (Edit? Edit, string Message) PercentEdit(PercentMarkSyntax percent, int dir)
    {
        int bars = Math.Max(percent.Bars, 1);
        int next = percent.Bars == 0 ? 1 : bars + dir;
        if (next < 1 || next > PercentMarkSyntax.MaxBars)
            return (null, $"{percent.Mark.Text}: already {(next < 1 ? "one bar" : $"{PercentMarkSyntax.MaxBars} bars, the most a mark repeats")}");
        string spelled = next == 1 ? "%" : "%" + next;
        return (new Edit(percent.Mark.Span.Start, percent.Mark.Span.End, spelled),
            $"{spelled}: repeats the {(next == 1 ? "bar" : $"{next} bars")} before it");
    }

    /// <summary>The staff-group keywords the step walks, in the order the score body's completion
    /// lists them: the brace, the bracket with bar lines through, the bracket with bar lines per
    /// staff, and the parts on ONE staff (owner's decision 2026-10-24: the list is short, so
    /// <c>condensedStaff</c> walks with the groups).</summary>
    private static readonly string[] StaffGroupWalk =
        ["grandStaff", "staffGroup", "choirStaff", "condensedStaff", "combinedStaff"];

    /// <summary>The two families PageUp / PageDown jump between, each with its usual kind first:
    /// the bracketed groups of several staves, and the parts on one staff.</summary>
    private static readonly string[][] StaffGroupFamilies =
    [
        ["grandStaff", "staffGroup", "choirStaff"],
        ["condensedStaff", "combinedStaff"],
    ];

    /// <summary>The members a <c>combinedStaff</c> takes: exactly two (GRAMMAR.md: CombinedStaff).
    /// The walk offers it only to a group or a staff of two parts (owner's decision 2026-10-24).</summary>
    private const int CombinedStaffParts = 2;

    /// <summary>A score's staff group, condensed or combined staff whose keyword the caret is on
    /// (not its body), or null.</summary>
    private static SyntaxNode? StaffGroupAt(SyntaxTree tree, int offset)
    {
        foreach (var node in tree.GetNodes<SyntaxNode>())
            if (node is GrandStaffRenderSyntax or CondensedStaffRenderSyntax or CombinedStaffRenderSyntax
                && node.GetChild(0) is SyntaxTokenNode keyword
                && keyword.Span.Start <= offset && offset <= keyword.Span.End)
                return node;
        return null;
    }

    /// <summary>Whether a keyword holds bare part names (<c>condensedStaff</c>, <c>combinedStaff</c>)
    /// rather than <c>staff</c> rows.</summary>
    private static bool HoldsBareNames(string keyword) => keyword is "condensedStaff" or "combinedStaff";

    /// <summary>
    /// One step of a staff group's keyword (owner's requests 2026-10-24): Ctrl+Shift+Down / Up
    /// write the next / previous of <see cref="StaffGroupWalk"/> — down the list as the completion
    /// shows it, as the clefs and <c>midiInstrument</c> step — and stop at either end; PageDown /
    /// PageUp jump between the families (<see cref="StaffGroupFamilies"/>).
    /// Between the groups only the word changes. Into or out of <c>condensedStaff</c> the body is
    /// rewritten too, since it holds bare part names where a group holds <c>staff</c> rows:
    /// <c>grandStaff { staff rh staff lh }</c> ⇄ <c>condensedStaff { rh lh }</c>, every space and
    /// line break between them kept; a condensed staff's clef goes onto each of its staves
    /// (<c>condensedStaff bass { vla vc }</c> → <c>grandStaff { staff bass vla staff bass vc }</c>).
    /// Into one, the rows' common clef goes after the keyword (<c>staff treble melody staff treble
    /// p2</c> → <c>condensedStaff treble { melody p2 }</c>, owner's report 2026-10-24). A group
    /// holding anything but <c>staff [CLEF] NAME</c> rows (a nested group, a lyrics row), or staves
    /// of different clefs, does not go onto one staff, and the status bar says so.
    /// </summary>
    private static (Edit? Edit, string Message) StaffGroupEdit(string text, SyntaxNode group, int dir, bool semitone = true)
    {
        var keyword = (SyntaxTokenNode)group.GetChild(0)!;
        bool fromBare = HoldsBareNames(keyword.Text);
        // What the body holds: bare names (a condensed / combined staff), or the plain staff rows
        // of a group — null when a group holds anything else (it cannot become bare names).
        var names = group switch
        {
            CondensedStaffRenderSyntax condensed => condensed.PartNameTokens.ToList(),
            CombinedStaffRenderSyntax combined => combined.PartNameTokens.ToList(),
            _ => null,
        };
        List<StaffRenderSyntax>? rows = fromBare ? null : PlainStaffRows(group);
        int parts = names?.Count ?? rows?.Count ?? -1;

        int at = Array.IndexOf(StaffGroupWalk, keyword.Text);
        int to;
        if (!semitone)
        {
            // PageDown / PageUp jump a family to its usual kind (owner's decision 2026-10-24):
            // the brackets (grandStaff) ⇄ one staff (condensedStaff), as the clefs' groups jump.
            int family = Array.FindIndex(StaffGroupFamilies, f => f.Contains(keyword.Text));
            int target = family - dir;
            if (target < 0 || target >= StaffGroupFamilies.Length)
                return (null, $"{keyword.Text}: already the {(target < 0 ? "first" : "last")} family "
                    + "(grandStaff staffGroup choirStaff | condensedStaff combinedStaff)");
            to = Array.IndexOf(StaffGroupWalk, StaffGroupFamilies[target][0]);
        }
        else
        {
            // The next word, skipping combinedStaff unless the body has exactly two parts.
            to = at < 0 ? 0 : at - dir;
            while (to >= 0 && to < StaffGroupWalk.Length && StaffGroupWalk[to] == "combinedStaff"
                   && parts != CombinedStaffParts)
                to -= dir;
            if (to < 0 || to >= StaffGroupWalk.Length)
                return (null, $"{keyword.Text}: already the {(to < 0 ? "first" : "last")} ({string.Join(", ", StaffGroupWalk)}"
                    + (parts != CombinedStaffParts ? "; combinedStaff takes exactly two parts)" : ")"));
        }
        string next = StaffGroupWalk[to];
        string said = next switch
        {
            "grandStaff" => "grandStaff: a brace, bar lines through the staves",
            "staffGroup" => "staffGroup: a bracket, bar lines through the staves",
            "choirStaff" => "choirStaff: a bracket, bar lines per staff",
            "condensedStaff" => "condensedStaff: the parts on one staff, a voice each",
            _ => "combinedStaff: the two parts on one staff, merged where they agree (a2 / Solo)",
        };
        bool toBare = HoldsBareNames(next);
        // Between two groups, or between the two bare-name staves: only the word changes.
        if (fromBare == toBare)
            return (new Edit(keyword.Span.Start, keyword.Span.End, next), said);
        if (fromBare)
        {
            // Out of a condensed / combined staff: each bare name becomes a staff row, with the clef.
            var clefToken = CondensedStaffRenderSyntax.ClefBeforeBrace(group);
            string clef = clefToken is { } c ? c.Text + " " : "";
            int end = names!.Count > 0 ? names[^1].Span.End : keyword.Span.End;
            var spliced = new System.Text.StringBuilder(next);
            int from = clefToken?.Span.End ?? keyword.Span.End;
            foreach (var name in names)
            {
                spliced.Append(text, from, name.Span.Start - from).Append("staff ").Append(clef).Append(name.Text);
                from = name.Span.End;
            }
            return (new Edit(keyword.Span.Start, end, spliced.ToString()), said);
        }
        // Into a condensed / combined staff: every member must be a `staff [CLEF] NAME` row.
        if (rows == null)
            return (null, $"{keyword.Text}: only staff rows go onto one staff (staff [clef] NAME) - "
                + "a nested group or a lyrics row stays a group");
        if (rows.Count < 2)
            return (null, $"{keyword.Text}: {next} takes two parts or more");
        // One staff has one clef: the rows' clef, written once after the keyword
        // (`staff treble melody  staff treble p2` → `condensedStaff treble { melody p2 }`, the
        // reverse of putting a condensed staff's clef onto each staff). Different clefs, or a clef
        // on some rows only, do not go onto one staff.
        var clefs = rows.Select(r => r.ClefToken?.Text).Distinct().ToList();
        if (clefs.Count > 1)
            return (null, $"{keyword.Text}: the staves have different clefs ({string.Join(", ", clefs.Select(c => c ?? "none"))}) "
                + "- one staff takes one clef");
        var joining = new System.Text.StringBuilder(next);
        if (clefs[0] is { } sharedClef)
            joining.Append(' ').Append(sharedClef);
        int cursor = keyword.Span.End;
        foreach (var row in rows)
        {
            joining.Append(text, cursor, row.Span.Start - cursor).Append(row.PartToken!.Text);
            cursor = row.Span.End;
        }
        return (new Edit(keyword.Span.Start, rows[^1].Span.End, joining.ToString()), said);
    }

    /// <summary>
    /// The completion's half of the same rewrite (owner's request 2026-10-24): the word
    /// <c>[wordStart, wordEnd)</c> is being completed to <paramref name="keyword"/>
    /// (<c>condensedStaff</c> / <c>combinedStaff</c>) in front of a written group body
    /// (<c>▮ { staff treble melody  staff treble p2 }</c>). Returns the head to write in the word's
    /// place — the keyword and the staves' common clef (<c>condensedStaff treble</c>) — and the
    /// edits, against <paramref name="text"/>, that take each row's <c>staff [CLEF]</c> away;
    /// null when the body does not go onto one staff (as <see cref="StaffGroupEdit"/> refuses it),
    /// and the completion writes the word alone.
    /// </summary>
    public static (string Head, ImmutableArray<Edit> BodyEdits)? OneStaffHeadBeforeABody(
        string text, int wordStart, int wordEnd, string keyword)
    {
        if (!HoldsBareNames(keyword) || wordStart < 0 || wordEnd < wordStart || wordEnd > text.Length)
            return null;
        // Read the body as the group it would be with a group keyword in the word's place.
        const string Probe = "grandStaff";
        string probed = string.Concat(text.AsSpan(0, wordStart), Probe, text.AsSpan(wordEnd));
        int shift = Probe.Length - (wordEnd - wordStart);
        var group = SyntaxTree.Parse(probed).GetNodes<GrandStaffRenderSyntax>()
            .FirstOrDefault(g => g.GrandStaffKeyword.Span.Start == wordStart);
        if (group == null || PlainStaffRows(group) is not { Count: >= 2 } rows
            || (keyword == "combinedStaff" && rows.Count != CombinedStaffParts))
            return null;
        var clefs = rows.Select(r => r.ClefToken?.Text).Distinct().ToList();
        if (clefs.Count > 1)
            return null;
        var edits = rows.Select(r => new Edit(r.Span.Start - shift, r.PartToken!.Span.Start - shift, "")).ToImmutableArray();
        return (clefs[0] is { } clef ? keyword + " " + clef : keyword, edits);
    }

    /// <summary>A group's members when every one is a <c>staff [CLEF] NAME</c> row (no MIDI-only
    /// part, nothing after the name), else null. The clefs are the caller's to reconcile.</summary>
    private static List<StaffRenderSyntax>? PlainStaffRows(SyntaxNode group)
    {
        var rows = new List<StaffRenderSyntax>();
        for (int i = 0; i < group.SlotCount; i++)
            switch (group.GetChild(i))
            {
                case SyntaxTokenNode:
                    continue;
                case StaffRenderSyntax row when row.PartToken is { } part
                        && row.MidiOnlyPartToken is null && part.Span.End == row.Span.End:
                    rows.Add(row);
                    continue;
                default:
                    return null;
            }
        return rows;
    }

    /// <summary>The part name a score row names that the caret is on — a <c>staff</c>'s or a
    /// <c>tab</c>'s part, a member of a <c>condensedStaff</c> / <c>combinedStaff</c> — or null.</summary>
    private static SyntaxTokenNode? ScorePartNameAt(SyntaxTree tree, int offset)
    {
        foreach (var node in tree.GetNodes<SyntaxNode>())
        {
            var names = new List<SyntaxTokenNode>();
            if (node is StaffRenderSyntax { PartToken: { } staffPart })
                names.Add(staffPart);
            else if (node is TabRenderSyntax { PartToken: { } tabPart })
                names.Add(tabPart);
            else if (node is CondensedStaffRenderSyntax condensed)
                names.AddRange(condensed.PartNameTokens);
            else if (node is CombinedStaffRenderSyntax combined)
                names.AddRange(combined.PartNameTokens);
            foreach (var name in names)
                if (name.Text.Length > 0 && name.Span.Start <= offset && offset <= name.Span.End)
                    return name;
        }
        return null;
    }

    /// <summary>The parts this file declares, in the order written — a <c>part NAME</c> header or
    /// a section's <c>NAME { … }</c> cell, each once, the unnamed part left out (as
    /// <see cref="ScoreSkeleton"/> lists them).</summary>
    private static List<string> DeclaredPartsInOrder(SyntaxTree tree)
    {
        var parts = new List<string>();
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(PartReferenceFinder.DeclaringKinds))
            if (PartReferenceFinder.DeclaredName(node) is { Text.Length: > 0 } name
                && name.Text != SyntaxFacts.UnnamedPartName && !parts.Contains(name.Text))
                parts.Add(name.Text);
        return parts;
    }

    /// <summary>One step of the part a score row names (owner's request 2026-10-24): either pair
    /// writes the next (Down) or previous (Up) of the file's parts in the order they are declared,
    /// and stops at either end — so a row can be pointed at each part in turn. A name the file
    /// does not declare steps from the ends.</summary>
    private static (Edit? Edit, string Message) ScorePartNameEdit(SyntaxTree tree, SyntaxTokenNode name, int dir)
    {
        var parts = DeclaredPartsInOrder(tree);
        if (parts.Count == 0)
            return (null, $"{name.Text}: the file declares no part to switch to");
        int at = parts.IndexOf(name.Text);
        int to = at < 0 ? (dir < 0 ? 0 : parts.Count - 1) : at - dir;
        string list = parts.Count <= 8 ? $" ({string.Join(", ", parts)})" : "";
        if (to < 0 || to >= parts.Count)
            return (null, $"{name.Text}: already the {(to < 0 ? "first" : "last")} of the {parts.Count} parts{list}");
        string next = parts[to];
        return (new Edit(name.Span.Start, name.Span.End, next), $"{next}: part {to + 1} of {parts.Count}{list}");
    }

    /// <summary>The count of a staff row's <c>as lines N</c> the caret is on (the number; the word
    /// <c>lines</c> steps the selector, <see cref="StaffSelectorAt"/>), or null.</summary>
    private static SyntaxTokenNode? StaffLinesAt(SyntaxTree tree, int offset)
    {
        foreach (var row in tree.GetNodes<StaffRenderSyntax>())
            for (int i = 0; i + 1 < row.SlotCount; i++)
                if (row.GetChild(i) is SyntaxTokenNode { Text: "lines" }
                    && row.GetChild(i + 1) is SyntaxTokenNode { Kind: SyntaxKind.IntegerLiteral } count
                    && count.Span.Start <= offset && offset <= count.Span.End)
                    return count;
        return null;
    }

    /// <summary>The selectors a staff row's <c>as</c> takes, in the order the completion lists
    /// them, each with the value it is written with when the step switches to it — the first of
    /// its values as the completion lists them (Lily#'s choice: <c>lines 1</c>, the one-line staff
    /// the selector is mostly written for).</summary>
    private static readonly (string Word, string Value)[] StaffSelectorWalk =
        [("lines", "1"), ("removeEmpty", "true"), ("pitch", ConcertPitch.PageModes[0])];

    /// <summary>The values of <c>as removeEmpty</c> in the order the completion lists them: the two
    /// hiding modes, then the default. The completion reads this list.</summary>
    public static IReadOnlyList<string> RemoveEmptyOrder { get; } = ["true", "all", "false"];

    /// <summary>A selector of a staff or ossia row's <c>as</c> clause the caret is on: the row, the
    /// selector's word and its value token (null when none is written), and whether the caret is
    /// on the value (a <c>removeEmpty</c> / <c>pitch</c> word; a <c>lines</c> number is
    /// <see cref="StaffLinesAt"/>'s) — or null.</summary>
    private static (SyntaxNode Row, SyntaxTokenNode Word, SyntaxTokenNode? Value, bool OnValue)? StaffSelectorAt(
        SyntaxTree tree, int offset)
    {
        foreach (var row in tree.GetNodes<SyntaxNode>())
        {
            if (row is not (StaffRenderSyntax or OssiaRenderSyntax))
                continue;
            bool afterAs = false;
            for (int i = 1; i < row.SlotCount; i++)
            {
                if (row.GetChild(i) is not SyntaxTokenNode t)
                    continue;
                if (t.Text == "as")
                {
                    afterAs = true;
                    continue;
                }
                if (!afterAs || !StaffSelectorWalk.Any(s => s.Word == t.Text))
                    continue;
                var value = i + 1 < row.SlotCount && row.GetChild(i + 1) is SyntaxTokenNode v
                    && v.Text.Length > 0 && !StaffSelectorWalk.Any(s => s.Word == v.Text) ? v : null;
                if (t.Span.Start <= offset && offset <= t.Span.End)
                    return (row, t, value, false);
                if (value != null && t.Text != "lines" && value.Span.Start <= offset && offset <= value.Span.End)
                    return (row, t, value, true);
            }
        }
        return null;
    }

    /// <summary>
    /// One step of a staff row's <c>as</c> selector (owner's request 2026-10-24), either pair, down
    /// the list as the completion shows it (Down the next, Up the previous), stopping at either end.
    /// On the selector's word: the next of <c>lines</c>, <c>removeEmpty</c>, <c>pitch</c> that the row
    /// does not already write (an ossia takes no <c>pitch</c>), written with its first value
    /// (<c>as lines 1</c> → <c>as removeEmpty true</c>). On a <c>removeEmpty</c> value: <c>true</c>,
    /// <c>all</c>, <c>false</c>; on a <c>pitch</c> value: <c>transposed</c>, <c>concert</c>.
    /// </summary>
    private static (Edit? Edit, string Message) StaffSelectorEdit(SyntaxNode row, SyntaxTokenNode word,
        SyntaxTokenNode? value, bool onValue, int dir)
    {
        int toward = -dir;   // +1: down the list
        if (onValue && value != null)
        {
            IReadOnlyList<string> values = word.Text == "pitch" ? ConcertPitch.PageModes : RemoveEmptyOrder;
            int at = values.ToList().IndexOf(value.Text);
            int to = at < 0 ? (toward > 0 ? 0 : values.Count - 1) : at + toward;
            if (to < 0 || to >= values.Count)
                return (null, $"{word.Text} {value.Text}: already the {(toward > 0 ? "last" : "first")} ({string.Join(", ", values)})");
            return (new Edit(value.Span.Start, value.Span.End, values[to]), $"{word.Text} {values[to]}");
        }
        // The selectors this row may switch to: not pitch on an ossia, not one it already writes.
        var written = new HashSet<string>();
        for (int i = 0; i < row.SlotCount; i++)
            if (row.GetChild(i) is SyntaxTokenNode t && t != word && StaffSelectorWalk.Any(s => s.Word == t.Text))
                written.Add(t.Text);
        var walk = StaffSelectorWalk.Where(s => !(row is OssiaRenderSyntax && s.Word == "pitch")).ToList();
        int here = walk.FindIndex(s => s.Word == word.Text);
        int next = here + toward;
        while (next >= 0 && next < walk.Count && written.Contains(walk[next].Word))
            next += toward;
        if (here < 0 || next < 0 || next >= walk.Count)
            return (null, $"as {word.Text}: already the {(toward > 0 ? "last" : "first")} selector "
                + $"({string.Join(", ", walk.Select(s => s.Word))}{(written.Count > 0 ? "; the row writes " + string.Join(", ", written) : "")})");
        var (nextWord, nextValue) = walk[next];
        int end = value?.Span.End ?? word.Span.End;
        return (new Edit(word.Span.Start, end, nextWord + " " + nextValue), $"as {nextWord} {nextValue}");
    }

    /// <summary>One step of a staff row's <c>as lines N</c> (owner's request 2026-10-24): either pair
    /// one line more (Up) or fewer (Down), within the counts the language takes
    /// (<see cref="LanguageVocabulary.MinStaffLines"/> to <see cref="LanguageVocabulary.MaxStaffLines"/>).</summary>
    private static (Edit? Edit, string Message) StaffLinesEdit(SyntaxTokenNode count, int dir)
    {
        if (!int.TryParse(count.Text, out int lines))
            return (null, "lines: write a whole number (as lines 1)");
        int next = lines + dir;
        if (next < LanguageVocabulary.MinStaffLines || next > LanguageVocabulary.MaxStaffLines)
            return (null, $"lines {lines}: already the {(dir > 0 ? "most" : "fewest")} "
                + $"({LanguageVocabulary.MinStaffLines} to {LanguageVocabulary.MaxStaffLines})");
        string said = next == 1 ? "lines 1: a one-line rhythm or percussion staff"
            : next == LanguageVocabulary.MaxStaffLines ? $"lines {next}: the default" : $"lines {next}";
        return (new Edit(count.Span.Start, count.Span.End, next.ToString()), said);
    }

    /// <summary>A score's <c>staff</c> or <c>tab</c> row whose keyword the caret is on, or null.</summary>
    private static SyntaxNode? StaffOrTabAt(SyntaxTree tree, int offset)
    {
        foreach (var node in tree.GetNodes<SyntaxNode>())
            if (node is StaffRenderSyntax or TabRenderSyntax
                && node.GetChild(0) is SyntaxTokenNode keyword
                && keyword.Span.Start <= offset && offset <= keyword.Span.End)
                return node;
        return null;
    }

    /// <summary>One step of a <c>staff</c> / <c>tab</c> row (owner's request 2026-10-24): either
    /// pair writes the other word — <c>staff gt</c> ⇄ <c>tab gt</c>. The word that only one of them
    /// reads after its keyword goes: a staff's clef (<c>staff bass gt</c> → <c>tab gt</c>, where
    /// <c>bass</c> would name a tuning) and a tab's tuning (<c>tab bass4 gt</c> → <c>staff gt</c>);
    /// the rest of the row stays.</summary>
    private static (Edit? Edit, string Message) StaffOrTabEdit(SyntaxNode row)
    {
        var keyword = (SyntaxTokenNode)row.GetChild(0)!;
        // A group holds staves, never a tab (GRAMMAR.md: StaffGroupBody).
        if (row is StaffRenderSyntax && row.Parent is GrandStaffRenderSyntax { GrandStaffKeyword.Text: var groupWord })
            return (null, $"staff: a {groupWord} holds staves, not a tab - put the tab outside the group");
        var (next, dropped, said) = row switch
        {
            StaffRenderSyntax staff => ("tab", staff.ClefToken, "tab: tablature of the part"),
            TabRenderSyntax tab => ("staff", tab.TuningToken, "staff: notation of the part"),
            _ => ("", null, ""),
        };
        // A dropped word stands right after the keyword: the edit runs to its end.
        int end = dropped is { } d && d.Span.Start > keyword.Span.End ? d.Span.End : keyword.Span.End;
        return (new Edit(keyword.Span.Start, end, next), said);
    }

    /// <summary>The tuning words the step walks: one spelling a tuning (the shortest of
    /// LilyPond's doubles — <c>bass</c>, not <c>bass4</c> or <c>doublebass</c>; <c>violin</c>, not
    /// <c>mandolin</c>), in the order of <see cref="TuningType"/>, which groups the families.</summary>
    private static readonly string[] TuningWalk =
    [
        .. Enum.GetValues<TuningType>()
            .Select(type => Tablature.Tunings.Names.Where(n => Tablature.Tunings.Parse(n) == type)
                .OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).FirstOrDefault())
            .OfType<string>(),
    ];

    /// <summary>A tuning's family, for the octave pair: guitars, basses, the orchestral strings,
    /// banjos, ukuleles.</summary>
    private static int TuningFamily(TuningType type) => type switch
    {
        < TuningType.Bass => 0,
        < TuningType.Violin => 1,
        < TuningType.BanjoOpenG => 2,
        < TuningType.Ukulele => 3,
        _ => 4,
    };

    /// <summary>A part's <c>tuning</c> the caret at <paramref name="offset"/> is on, or null.</summary>
    private static PropertyAssignmentSyntax? TuningAt(SyntaxTree tree, int offset)
    {
        foreach (var p in tree.GetNodes<PropertyAssignmentSyntax>())
            if (p.NameToken.Text == "tuning" && p.SourceStart <= offset && offset <= p.Span.End)
                return p;
        return null;
    }

    /// <summary>The tuning word a <c>tuning</c> names, or null.</summary>
    private static SyntaxTokenNode? TuningWordToken(PropertyAssignmentSyntax property)
        => property.Values.OfType<SyntaxTokenNode>().FirstOrDefault(v => v.Text.Length > 0);

    /// <summary>One step of a part's <c>tuning</c> (owner's request 2026-10-24, as
    /// <c>midiInstrument</c>): the semitone pair walks <see cref="TuningWalk"/> one tuning at a
    /// time, the octave pair a family at a time (the first of the next or the previous one). A
    /// double (<c>bass4</c>) steps from its tuning's place; it stops at either end, and a word off
    /// the list steps from the ends.
    /// <para>⚠️ DOWN GOES DOWN THE LIST (owner's decision 2026-10-24, the reverse of the first
    /// day's): the order the completion after <c>tuning</c> shows, <c>guitar</c> at the top, as
    /// the clefs, the fonts and <c>midiInstrument</c> step.</para></summary>
    private static (Edit? Edit, string Message) TuningEdit(PropertyAssignmentSyntax property, int dir, bool semitone)
    {
        var token = TuningWordToken(property);
        string current = token?.Text ?? "";
        int at = Tablature.Tunings.Names.Contains(current)
            ? Array.FindIndex(TuningWalk, w => Tablature.Tunings.Parse(w) == Tablature.Tunings.Parse(current))
            : -1;
        int FamilyOf(int i) => TuningFamily(Tablature.Tunings.Parse(TuningWalk[i]));
        int FirstOf(int family) => Array.FindIndex(TuningWalk, w => TuningFamily(Tablature.Tunings.Parse(w)) == family);
        int toward = -dir;   // +1: down the list
        int to;
        if (at < 0)
            to = toward > 0 ? 0 : TuningWalk.Length - 1;
        else if (semitone)
            to = at + toward;
        else if (toward > 0)
            to = FirstOf(FamilyOf(at) + 1);
        else
            // Up inside a family goes to its first tuning; from its first, to the previous family's.
            to = FirstOf(FamilyOf(at)) < at ? FirstOf(FamilyOf(at)) : FirstOf(FamilyOf(at) - 1);
        if (to < 0 || to >= TuningWalk.Length)
            return (null, $"tuning {current}: already the {(toward > 0 ? "last" : "first")} of the {TuningWalk.Length}");
        string next = TuningWalk[to];
        string said = $"tuning {next}: {OpenStrings(next)}";
        return token != null
            ? (new Edit(token.Span.Start, token.Span.End, next), said)
            : (new Edit(property.NameToken.Span.End, property.NameToken.Span.End, " " + next), said);
    }

    /// <summary>A tuning's open strings in LilyPond's order (the highest string number first) —
    /// "E1 A1 D2 G2".</summary>
    private static string OpenStrings(string word)
        => string.Join(" ", Tablature.Tunings.GetTuning(Tablature.Tunings.Parse(word)).Select(SpellMidi));

    /// <summary>What a <c>tuning</c> sounds when it changes: its open strings one after another,
    /// in string order from the highest-numbered string (<see cref="StepResult.Strum"/>), in the General MIDI program of its instrument (Lily#'s choice: the fingered bass,
    /// the violin, viola and cello, the banjo, the nylon guitar for the guitars and the ukuleles).
    /// Silent for a word that is no tuning.</summary>
    private static Voice TuningVoice(PropertyAssignmentSyntax property)
    {
        if (TuningWordToken(property) is not { } token || !Tablature.Tunings.Names.Contains(token.Text))
            return Voice.Silent;
        var type = Tablature.Tunings.Parse(token.Text);
        string name = type switch
        {
            TuningType.Violin => "violin",
            TuningType.Viola => "viola",
            TuningType.Cello => "cello",
            _ => TuningFamily(type) switch
            {
                1 => "electric bass (finger)",
                3 => "banjo",
                _ => "acoustic guitar (nylon)",
            },
        };
        int program = Midi.GeneralMidi.ProgramOf(name)!.Value;
        // Struck string after string, the highest-numbered string first — the table's own order
        // (owner's request 2026-10-24) — NOT sorted by pitch: a re-entrant ukulele's 4th string is
        // its high G, struck before the 3rd string's C (owner's report, same day). On a guitar the
        // two orders agree.
        return new Voice([.. Tablature.Tunings.GetTuning(type)],
            Midi.GeneralMidi.PreviewTimbreFamily(program), program, Strum: true);
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
