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
using System.Runtime.CompilerServices;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The editor's "step" keys (Ctrl+Alt+Up / Ctrl+Alt+Down in a <c>.lys</c>) and the
/// audition that sounds what the caret is on: which written thing a caret or a selection
/// is on, the text edit one step up or down makes of it, and the pitches it sounds.
/// </summary>
/// <remarks>
/// <para>
/// Owner's decision, 2026-09-28. What a step does depends on what the caret is on:
/// </para>
/// <list type="bullet">
/// <item>an <c>@chord(…)</c> with a voicing index (<c>@chord(Cm7 2)</c>,
/// <c>@chord(Cm7 2 mute 1)</c>): the index ±1. Up at the last index changes nothing and
/// says the range (<c>Cm7: voicings 0–51</c>); Down at 0 goes back to the name alone —
/// <c>@chord(Cm7 0 mute 1)</c> and <c>@chord(D mute 5)</c> (whose index is an implicit 0)
/// both become the bare name, the <c>mute</c> words going with the index they overlay.</item>
/// <item>an <c>@chord(Cm7)</c> naming a chord and nothing more: Up writes <c> 0</c> (voicing
/// 0 appears); Down does nothing.</item>
/// <item>an <c>@chord(D mute 5)</c>: Up writes the implicit index out as its successor,
/// <c>@chord(D 1 mute 5)</c>.</item>
/// <item>a NOTE — a pitch with its accidental, octave marks, duration and dots; a chord
/// member inside <c>&lt; &gt;</c> / <c>&lt;&lt; &gt;&gt;</c>; a scale-degree member: Up
/// removes one <c>,</c> if the pitch has any, else adds one <c>'</c>; Down mirrors it. A
/// PLAIN TEXTUAL MARK EDIT, deliberately not pitch-preserving for the notes after it (the
/// owner: "単に ' を挿入するだけの方が使いやすい") — inside a chord only the member moves.</item>
/// <item>a chord or an arpeggio as a whole (the caret on its <c>&lt;</c>, its <c>&gt;</c>
/// or its duration, not on a member): the same edit on the marks after the closing
/// bracket, which move the whole chord (<c>&lt;c e g&gt;'</c>). Lily#'s choice: the one
/// mark that already means "this chord, an octave over".</item>
/// </list>
/// <para>
/// Anything else — a rest, a barline, a written-out diagram <c>@chord(x32010)</c>, a
/// bare <c>@chord</c>, the space between notes — is not steppable, and the editor runs the
/// key's own command instead (add a cursor above/below): <see cref="StepResult.Fallback"/>.
/// </para>
/// <para>
/// ⚠️ THE PITCHES ARE THE COMPILER'S. A note sounds what the MIDI export plays at its
/// source position — the same numbers the preview's Play button schedules — and a chord
/// member, which the export files under its chord's position, is the collector's resolved
/// pitch moved by whatever the export moved the chord by (a transposing part). A voicing
/// sounds its diagram on the part's tuning: the open string plus the fret, muted strings
/// silent. Nothing here re-derives a pitch from the letters.
/// </para>
/// </remarks>
public static class NoteStepper
{
    /// <summary>One replacement of <c>[Start, End)</c> in the text the request was made against.</summary>
    public readonly record struct Edit(int Start, int End, string NewText);

    /// <summary>What a caret is on.</summary>
    public enum TargetKind
    {
        /// <summary>A note (its pitch, marks, duration and dots).</summary>
        Note,
        /// <summary>A member of a chord or an arpeggio.</summary>
        Member,
        /// <summary>A chord or an arpeggio as a whole.</summary>
        Chord,
        /// <summary>An <c>@chord(…)</c> annotation.</summary>
        Voicing,
    }

    /// <summary>A written thing the caret is on: its kind and its node (a
    /// <see cref="NoteSyntax"/>, a <see cref="PitchSyntax"/> or <see cref="ScaleDegreeSyntax"/>
    /// member, a <see cref="ChordSyntax"/> or <see cref="ArpeggioSyntax"/>, or a
    /// <see cref="MusicMarkSyntax"/>).</summary>
    public readonly record struct Target(TargetKind Kind, SyntaxNode Node)
    {
        /// <summary>Where it starts — what tells one target from another.</summary>
        public int Key => Node.SourceStart;
    }

    /// <summary>
    /// What a step comes to: the edits (against the text asked about; empty when the step
    /// changed nothing), a status-bar message, the pitches to sound afterwards (MIDI, lowest
    /// first) with the timbre to sound them in, and <see cref="Fallback"/> when no selection
    /// was on anything steppable — the editor then runs the key's own command.
    /// </summary>
    public sealed record StepResult(
        bool Fallback, ImmutableArray<Edit> Edits, string? Message,
        ImmutableArray<int> Pitches, int Timbre);

    /// <summary>What the caret is on and what it sounds (MIDI, lowest first).</summary>
    public sealed record Audition(Target Target, ImmutableArray<int> Pitches, int Timbre);

    /// <summary>The most pitches one audition sounds (a selection of a whole passage would
    /// otherwise strike every note of it at once).</summary>
    public const int MaxAuditionPitches = 12;

    /// <summary>The timbre a voicing sounds in when its host note plays nothing to copy one
    /// from: the guitar family (MidiNote.Timbre 4).</summary>
    private const int GuitarTimbre = 4;

    // ------------------------------------------------------------------ the step

    /// <summary>
    /// One step of every selection: <paramref name="direction"/> &gt; 0 is Up. A caret
    /// (<c>Start == End</c>) steps what it is on; a selection steps every note and member
    /// whose pitch starts inside it. <paramref name="expand"/> turns the stepped text into the
    /// tree that SOUNDS (the server's <c>using</c> expansion); null parses it as it stands.
    /// </summary>
    public static StepResult Step(
        string text, SyntaxTree tree, IReadOnlyList<(int Start, int End)> selections, int direction,
        Func<string, SyntaxTree>? expand = null)
    {
        int dir = direction >= 0 ? 1 : -1;
        var targets = new List<Target>();
        var seen = new HashSet<(TargetKind, int)>();
        void Add(Target t)
        {
            if (seen.Add((t.Kind, t.Key)))
                targets.Add(t);
        }

        foreach (var (rawStart, rawEnd) in selections)
        {
            int start = Math.Min(rawStart, rawEnd), end = Math.Max(rawStart, rawEnd);
            if (start == end)
            {
                if (TargetAt(text, tree, start) is { } t)
                    Add(t);
                continue;
            }
            foreach (var pitch in PitchLikeNodes(tree))
                if (pitch.SourceStart >= start && pitch.SourceStart < end)
                    Add(pitch.Parent is NoteSyntax note
                        ? new Target(TargetKind.Note, note)
                        : new Target(TargetKind.Member, pitch));
        }

        if (targets.Count == 0)
            return new StepResult(true, [], null, [], 0);

        var edits = new List<Edit>();
        string? message = null;
        var stepped = new List<Target>();
        foreach (var t in targets)
        {
            var (edit, said, handled) = t.Kind == TargetKind.Voicing
                ? VoicingEdit((MusicMarkSyntax)t.Node, dir)
                : (MarkEdit(text, MarksStart(t), dir), null, true);
            if (!handled)
                continue;
            stepped.Add(t);
            message ??= said;
            if (edit is { } e)
                edits.Add(e);
        }
        if (stepped.Count == 0)
            return new StepResult(true, [], null, [], 0);

        edits.Sort((a, b) => a.Start.CompareTo(b.Start));
        string newText = Apply(text, edits);

        // The audition: each stepped thing, found again in the stepped text (an edit moves
        // only what stands after it), sounded as the compiler now reads it.
        var pitches = new SortedSet<int>();
        int timbre = -1;
        try
        {
            var newTree = SyntaxTree.Parse(newText);
            var sounding = expand?.Invoke(newText) ?? newTree;
            foreach (var t in stepped)
            {
                int at = MapThrough(edits, t.Key);
                if (FindTarget(newTree, t.Kind, at) is not { } moved)
                    continue;
                var (p, i) = Sound(moved, sounding);
                foreach (int pitch in p)
                    pitches.Add(pitch);
                if (timbre < 0 && p.Length > 0)
                    timbre = i;
            }
        }
        catch (Exception)
        {
            // A text the stepped mark breaks still steps; it only stays silent.
        }

        return new StepResult(false, [.. edits], message,
            [.. pitches.Take(MaxAuditionPitches)], Math.Max(timbre, 0));
    }

    // ------------------------------------------------------------------ the audition

    /// <summary>
    /// What the caret at <paramref name="offset"/> is on and the pitches it sounds, or null
    /// when it is on nothing that sounds (a rest, a barline, an <c>@chord</c> with no
    /// diagram). <paramref name="sounding"/> is the tree the piece plays from (the
    /// <c>using</c>-expanded one); null means <paramref name="tree"/>.
    /// </summary>
    public static Audition? AuditionAt(string text, SyntaxTree tree, int offset, SyntaxTree? sounding = null)
    {
        if (TargetAt(text, tree, offset) is not { } target)
            return null;
        if (target.Kind == TargetKind.Voicing
            && ChordAnnotation.Of((MusicMarkSyntax)target.Node) is not { WantsDiagram: true })
            return null;
        var (pitches, timbre) = Sound(target, sounding ?? tree);
        return pitches.IsEmpty ? null : new Audition(target, pitches, timbre);
    }

    // ------------------------------------------------------------------ what the caret is on

    /// <summary>
    /// The written thing the caret at <paramref name="offset"/> is on, or null. A caret
    /// touching a thing's first or last character counts (<c>c'4|</c> is on the note), and
    /// the more specific thing wins: an <c>@chord</c> over the note it is written on, a
    /// member over its chord.
    /// </summary>
    public static Target? TargetAt(string text, SyntaxTree tree, int offset)
    {
        // An @chord: strictly after its '@' — a caret just before the '@' is also just after
        // the note's duration, and belongs to the note.
        foreach (var mark in tree.GetNodes<MusicMarkSyntax>())
            if (mark.Name == "chord" && mark.Span.Start < offset && offset <= mark.Span.End)
                return new Target(TargetKind.Voicing, mark);

        Target? note = null;
        foreach (var pitch in PitchLikeNodes(tree))
        {
            int start = pitch.SourceStart;
            if (offset < start)
                continue;
            if (pitch.Parent is NoteSyntax n)
            {
                if (offset <= NoteHeadEnd(text, n))
                    note ??= new Target(TargetKind.Note, n);
            }
            else if (offset <= MarksEnd(text, NameEnd(pitch)))
            {
                return new Target(TargetKind.Member, pitch);
            }
        }
        if (note != null)
            return note;

        foreach (var node in tree.GetNodes<SyntaxNode>())
        {
            if (node is not (ChordSyntax or ArpeggioSyntax) || offset < node.SourceStart)
                continue;
            if (CloseBracket(node) is { } close && offset <= ChordHeadEnd(text, node, close))
                return new Target(TargetKind.Chord, node);
        }
        return null;
    }

    /// <summary>The written pitches a step moves: a note's pitch, a chord's or an arpeggio's
    /// pitch and degree members — never a pitch written as an argument (<c>transpose c d</c>)
    /// or in a <c>chords { }</c> block.</summary>
    private static IEnumerable<SyntaxNode> PitchLikeNodes(SyntaxTree tree)
    {
        foreach (var node in tree.GetNodes<SyntaxNode>())
            if (node is PitchSyntax or ScaleDegreeSyntax
                && node.Parent is NoteSyntax or ChordSyntax or ArpeggioSyntax)
                yield return node;
    }

    /// <summary>The end of a pitch's or a degree's name token (the letter and its
    /// accidental, or the degree number and its accidental).</summary>
    private static int NameEnd(SyntaxNode pitchLike) => pitchLike.GetChild(0)!.Span.End;

    /// <summary>Just past the <c>'</c>/<c>,</c> marks written at <paramref name="at"/>.</summary>
    private static int MarksEnd(string text, int at)
    {
        int e = at;
        while (e < text.Length && text[e] is '\'' or ',')
            e++;
        return e;
    }

    /// <summary>The end of a note's head: its pitch and marks, its duration and dots, its
    /// tremolo — not the articulations and annotations after them.</summary>
    private static int NoteHeadEnd(string text, NoteSyntax note)
    {
        int end = MarksEnd(text, NameEnd(note.Pitch));
        if (note.Duration is { } d)
            end = Math.Max(end, d.Span.End);
        if (note.Tremolo is { } t)
            end = Math.Max(end, t.Span.End);
        return end;
    }

    /// <summary>A chord's <c>&gt;</c> or an arpeggio's <c>&gt;&gt;</c>, or null when it
    /// never closed.</summary>
    private static SyntaxNode? CloseBracket(SyntaxNode chord)
    {
        var kind = chord is ArpeggioSyntax ? SyntaxKind.DoubleCloseAngle : SyntaxKind.CloseAngle;
        for (int i = 0; i < chord.SlotCount; i++)
            if (chord.GetChild(i) is SyntaxTokenNode token && token.Kind == kind)
                return token;
        return null;
    }

    /// <summary>The end of a chord's head: its closing bracket, the marks after it, its
    /// duration and its tremolo.</summary>
    private static int ChordHeadEnd(string text, SyntaxNode chord, SyntaxNode close)
    {
        int end = MarksEnd(text, close.Span.End);
        SyntaxNode? duration = chord is ChordSyntax c ? c.Duration : (chord as ArpeggioSyntax)?.TotalDuration;
        SyntaxNode? tremolo = (chord as ChordSyntax)?.Tremolo;
        if (duration != null)
            end = Math.Max(end, duration.Span.End);
        if (tremolo != null)
            end = Math.Max(end, tremolo.Span.End);
        return end;
    }

    /// <summary>Where a target's octave marks are (or would be) written.</summary>
    private static int MarksStart(Target t) => t.Kind switch
    {
        TargetKind.Note => NameEnd(((NoteSyntax)t.Node).Pitch),
        TargetKind.Member => NameEnd(t.Node),
        _ => CloseBracket(t.Node)!.Span.End,
    };

    /// <summary>Finds a target of <paramref name="kind"/> starting at <paramref name="at"/>
    /// in a re-parsed text.</summary>
    private static Target? FindTarget(SyntaxTree tree, TargetKind kind, int at)
    {
        foreach (var node in tree.GetNodes<SyntaxNode>())
        {
            if (node.SourceStart != at)
                continue;
            bool fits = kind switch
            {
                TargetKind.Note => node is NoteSyntax,
                TargetKind.Member => node is PitchSyntax or ScaleDegreeSyntax
                    && node.Parent is ChordSyntax or ArpeggioSyntax,
                TargetKind.Chord => node is ChordSyntax or ArpeggioSyntax,
                _ => node is MusicMarkSyntax { Name: "chord" },
            };
            if (fits)
                return new Target(kind, node);
        }
        return null;
    }

    // ------------------------------------------------------------------ the edits

    /// <summary>
    /// The octave-mark edit at <paramref name="at"/> (the first character after the name or
    /// the closing bracket): Up removes one <c>,</c> when there is one, else adds a
    /// <c>'</c> after the marks; Down mirrors it.
    /// </summary>
    internal static Edit MarkEdit(string text, int at, int dir)
    {
        int end = MarksEnd(text, at);
        char remove = dir > 0 ? ',' : '\'';
        for (int i = end - 1; i >= at; i--)
            if (text[i] == remove)
                return new Edit(i, i + 1, "");
        return new Edit(end, end, dir > 0 ? "'" : ",");
    }

    /// <summary>
    /// The voicing-index edit of an <c>@chord</c>, a status-bar message, and whether the
    /// annotation is steppable at all (a symbol, optionally an index and <c>mute</c> — not
    /// a written-out diagram, quoted text or the bare <c>@chord</c>). A steppable one that
    /// cannot move (Up at the last index, Down with no index) answers no edit.
    /// </summary>
    private static (Edit? Edit, string? Message, bool Handled) VoicingEdit(MusicMarkSyntax mark, int dir)
    {
        if (ChordAnnotation.Of(mark) is not { Symbol: { } symbol, Structure: not null, Written: null } words)
            return (null, null, false);
        var spans = WordSpans(mark);
        if (spans.Count != mark.Arguments.Length || spans.Count == 0)
            return (null, null, false);
        if (words.Problem != null)
            return (null, words.Problem, true);

        // How many voicings there are, when the part's tuning is known (a phrase the parts
        // play on different tunings has no one count; it steps unbounded, as the page and
        // the validator will judge it per part).
        int? count = null;
        string? unavailable = null;
        if (ChordAnnotation.PartTuningOf(mark) is { } tuning)
        {
            var r = (words with { Index = 0, HasMute = false, Mutes = [] }).Resolve(tuning);
            if (r.Problem != null)
                unavailable = r.Problem;
            else
                count = r.Count;
        }

        int symbolEnd = spans[0].End;
        if (!words.IsIndexForm)
        {
            if (dir < 0)
                return (null, null, true);
            if (unavailable != null)
                return (null, unavailable, true);
            return (new Edit(symbolEnd, symbolEnd, " 0"), null, true);
        }

        int index = words.Index ?? 0;
        if (dir > 0)
        {
            if (count is int n && index >= n - 1)
                return (null, $"{symbol}: voicings 0–{n - 1}", true);
            return words.Index == null
                ? (new Edit(symbolEnd, symbolEnd, " 1"), null, true)
                : (new Edit(spans[1].Start, spans[1].End, Invariant(index + 1)), null, true);
        }
        if (index == 0)
            // Back to the name alone. The mute words go too: without an index they would
            // be `@chord(D mute 5)`, which IS index 0 — the step would not have moved.
            return (new Edit(symbolEnd, spans[^1].End, ""), null, true);
        int down = count is int total && index > total - 1 ? total - 1 : index - 1;
        return (new Edit(spans[1].Start, spans[1].End, Invariant(down)), null, true);
    }

    private static string Invariant(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The source span of each argument word — the runs <see cref="MarkArgument"/>
    /// reads: adjacent tokens form one word, whitespace and ',' separate them.</summary>
    private static List<(int Start, int End)> WordSpans(MusicMarkSyntax mark)
    {
        var spans = new List<(int Start, int End)>();
        int start = -1, end = -1;
        foreach (var token in mark.ArgumentTokens)
        {
            if (token.Kind == SyntaxKind.Comma)
            {
                if (start >= 0)
                    spans.Add((start, end));
                start = -1;
                continue;
            }
            if (start >= 0 && token.Span.Start == end)
            {
                end = token.Span.End;
                continue;
            }
            if (start >= 0)
                spans.Add((start, end));
            start = token.Span.Start;
            end = token.Span.End;
        }
        if (start >= 0)
            spans.Add((start, end));
        return spans;
    }

    /// <summary>The text with <paramref name="sorted"/> (by start, not overlapping) applied.</summary>
    internal static string Apply(string text, IReadOnlyList<Edit> sorted)
    {
        var sb = new System.Text.StringBuilder(text.Length + sorted.Count);
        int at = 0;
        foreach (var e in sorted)
        {
            sb.Append(text, at, e.Start - at).Append(e.NewText);
            at = e.End;
        }
        return sb.Append(text, at, text.Length - at).ToString();
    }

    /// <summary>Where <paramref name="position"/> stands once <paramref name="sorted"/> is
    /// applied (every edit before it moves it by its growth).</summary>
    private static int MapThrough(IReadOnlyList<Edit> sorted, int position)
    {
        int shift = 0;
        foreach (var e in sorted)
            if (e.End <= position && e.Start < position)
                shift += e.NewText.Length - (e.End - e.Start);
        return position + shift;
    }

    // ------------------------------------------------------------------ the sound

    /// <summary>The pitches a target sounds (MIDI, lowest first) and the timbre to sound
    /// them in, read off <paramref name="sounding"/>.</summary>
    private static (ImmutableArray<int> Pitches, int Timbre) Sound(Target target, SyntaxTree sounding)
    {
        var index = SoundIndex.Of(sounding);
        switch (target.Kind)
        {
            case TargetKind.Note:
            {
                int at = target.Key;
                if (index.Midi.TryGetValue(at, out var played))
                    return ([played[0]], index.Timbre[at]);
                return index.Trace.TryGetValue(((NoteSyntax)target.Node).Pitch.SourceStart, out int written)
                    ? ([written], 0) : ([], 0);
            }
            case TargetKind.Member:
            {
                int at = target.Key;
                // An arpeggio's members play at their own positions.
                if (index.Midi.TryGetValue(at, out var played))
                    return ([played[0]], index.Timbre[at]);
                if (!index.Trace.TryGetValue(at, out int written))
                    return ([], 0);
                // A chord's are filed under the chord: the member's resolved pitch, moved
                // by what the export moved the chord by.
                if (target.Node.Parent is ChordSyntax chord
                    && index.Midi.TryGetValue(chord.SourceStart, out var chordPlayed))
                {
                    var members = MembersOf(chord)
                        .Select(m => index.Trace.TryGetValue(m.SourceStart, out int p) ? p : int.MaxValue)
                        .Where(p => p != int.MaxValue).ToList();
                    int shift = members.Count > 0 ? chordPlayed.Min() - members.Min() : 0;
                    return ([written + shift], index.Timbre[chord.SourceStart]);
                }
                return ([written], 0);
            }
            case TargetKind.Chord:
            {
                var set = new SortedSet<int>();
                int timbre = 0;
                IEnumerable<int> positions = target.Node is ArpeggioSyntax arpeggio
                    ? arpeggio.Members.Select(m => m.SourceStart)
                    : [target.Key];
                foreach (int at in positions)
                    if (index.Midi.TryGetValue(at, out var played))
                    {
                        set.UnionWith(played);
                        timbre = index.Timbre[at];
                    }
                if (set.Count == 0)
                    foreach (var m in MembersOf(target.Node))
                        if (index.Trace.TryGetValue(m.SourceStart, out int p))
                            set.Add(p);
                return ([.. set.Take(MaxAuditionPitches)], timbre);
            }
            default:
            {
                var mark = (MusicMarkSyntax)target.Node;
                if (ChordAnnotation.Of(mark) is not { WantsDiagram: true } words
                    || ChordAnnotation.PartTuningOf(mark) is not { } tuning)
                    return ([], 0);
                var resolved = words.Resolve(tuning);
                if (!resolved.HasDiagram)
                    return ([], 0);
                var pitches = new SortedSet<int>();
                for (int s = 0; s < resolved.Frets.Length && s < tuning.Count; s++)
                    if (resolved.Frets[s] >= 0)
                        pitches.Add(tuning[s] + resolved.Frets[s]);
                int host = mark.Parent?.SourceStart ?? -1;
                return ([.. pitches], index.Timbre.TryGetValue(host, out int t) ? t : GuitarTimbre);
            }
        }
    }

    /// <summary>A chord's pitch and degree members (an arpeggio's, nested chords' included).</summary>
    private static IEnumerable<SyntaxNode> MembersOf(SyntaxNode chord)
    {
        foreach (var node in chord.DescendantNodes<SyntaxNode>())
            if (node is PitchSyntax or ScaleDegreeSyntax
                && node.Parent is ChordSyntax or ArpeggioSyntax)
                yield return node;
    }

    /// <summary>
    /// What a tree sounds, by source position: the MIDI export's pitches (and timbre) filed
    /// under each note's position, and the collector's resolved pitch of each written pitch
    /// (<see cref="ResolvedPitches.ForFile"/>, as MIDI numbers). Built once per tree — the
    /// caret moving over an unchanged document asks the same tree again and again.
    /// </summary>
    private sealed class SoundIndex
    {
        public Dictionary<int, List<int>> Midi { get; } = [];
        public Dictionary<int, int> Timbre { get; } = [];
        public Dictionary<int, int> Trace { get; } = [];

        private static readonly ConditionalWeakTable<SyntaxTree, SoundIndex> Cache = new();

        public static SoundIndex Of(SyntaxTree tree) => Cache.GetValue(tree, Build);

        private static SoundIndex Build(SyntaxTree tree)
        {
            var index = new SoundIndex();
            try
            {
                var midi = new Midi.MidiExporter().Export(tree);
                foreach (var note in midi.Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick))
                {
                    if (note.SourcePos < 0 || note.Channel == 9)
                        continue;
                    if (!index.Midi.TryGetValue(note.SourcePos, out var list))
                    {
                        index.Midi[note.SourcePos] = list = [];
                        index.Timbre[note.SourcePos] = note.Timbre;
                    }
                    if (!list.Contains(note.Pitch))
                        list.Add(note.Pitch);
                }
            }
            catch (Exception)
            {
                // A tree the export cannot walk sounds from the trace alone.
            }
            try
            {
                string text = tree.Text;
                foreach (var e in ResolvedPitches.ForFile(tree) ?? [])
                {
                    int p = e.Position;
                    while (p < text.Length && char.IsWhiteSpace(text[p]))
                        p++;
                    if (MidiOf(e.Pitch) is int midi)
                        index.Trace.TryAdd(p, midi);
                }
            }
            catch (Exception)
            {
                // Nor can it always be collected; what the export gave stands.
            }
            return index;
        }

        /// <summary>A resolved spelling (<c>C#4</c>, <c>Bb3</c>, <c>Fx5</c>) as a MIDI number
        /// (C4 = 60).</summary>
        internal static int? MidiOf(string spelled)
        {
            if (spelled.Length < 2)
                return null;
            int step = "CDEFGAB".IndexOf(spelled[0]);
            if (step < 0)
                return null;
            int i = 1, alter = 0;
            while (i < spelled.Length && spelled[i] is '#' or 'x' or 'b')
            {
                alter += spelled[i] switch { '#' => 1, 'x' => 2, _ => -1 };
                i++;
            }
            if (!int.TryParse(spelled.AsSpan(i), System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out int octave))
                return null;
            int[] semis = [0, 2, 4, 5, 7, 9, 11];
            return (octave + 1) * 12 + semis[step] + alter;
        }
    }
}
