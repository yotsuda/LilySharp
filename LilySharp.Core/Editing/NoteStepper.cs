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
/// The editor's "step" keys (Ctrl+Shift+Up / Ctrl+Shift+Down in a <c>.lys</c>; Ctrl+Alt until
/// the owner moved them, 2026-09-28) and the audition that sounds what the caret is on: which
/// written thing a caret or a selection is on, the text edit one step up or down makes of it,
/// and the pitches it sounds.
/// </summary>
/// <remarks>
/// <para>
/// Owner's decisions, 2026-09-28. What a step does depends on what the caret is on:
/// </para>
/// <list type="bullet">
/// <item>an <c>@chord(Cm7)</c> / <c>@chord(Cm7 x35343)</c>, or a <c>chords</c> row entry
/// <c>Cm7</c> / <c>Cm7(x35343)</c> (HANDOFF §2 K): the shape is REWRITTEN to the next /
/// previous shape of the editor's order (<see cref="ShapeOrder"/>: the default first —
/// LilyPond's predefined shape, else Lily#'s first — then Lily#'s maximal shapes) on
/// <see cref="StepTuning"/>. A diagram draws only where a shape is written, so on a name alone
/// Up WRITES the default and the diagram appears; Down at the default removes the shape and
/// the diagram goes; Down on a name alone does nothing. A shape with muted strings steps from
/// the shape it mutes and loses its <c>x</c>. The status bar says
/// <c>Cm7: shape 4 of 19 (x3x546)</c>. Stretch shapes are walked only on request (owner's
/// decision 2026-09-28, the <c>includeStretch</c> argument).</item>
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
/// bare <c>@chord</c>, the space between notes — is not steppable: <see cref="StepResult.Fallback"/>,
/// and the editor runs Add Cursor Above / Below on Linux (where Ctrl+Shift+Up/Down is that
/// command's second binding) and nothing on Windows and macOS.
/// </para>
/// <para>
/// ⚠️ THE PITCHES ARE THE COMPILER'S. A note sounds what the MIDI export plays at its
/// source position — the same numbers the preview's Play button schedules — and a chord
/// member, which the export files under its chord's position, is the collector's resolved
/// pitch moved by whatever the export moved the chord by (a transposing part). An
/// <c>@chord</c> sounds its shape (<see cref="SoundedShape"/>) on <see cref="StepTuning"/>:
/// the open string plus the fret, muted strings silent. Nothing here re-derives a pitch from
/// the letters.
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
        /// <summary>An <c>@chord(…)</c> annotation, or a <c>chords</c> row entry
        /// (<see cref="ChordEntrySyntax"/>) — a chord whose shape the step rewrites.</summary>
        Voicing,
    }

    /// <summary>A written thing the caret is on: its kind and its node (a
    /// <see cref="NoteSyntax"/>, a <see cref="PitchSyntax"/> or <see cref="ScaleDegreeSyntax"/>
    /// member, a <see cref="ChordSyntax"/> or <see cref="ArpeggioSyntax"/>, a
    /// <see cref="MusicMarkSyntax"/> or a <see cref="ChordEntrySyntax"/>).</summary>
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
    /// <paramref name="includeStretch"/> (<c>lilysharp.chordShapes.includeStretch</c>) lets an
    /// <c>@chord</c> step through stretch shapes too (<see cref="ShapeOrder"/>).
    /// </summary>
    public static StepResult Step(
        string text, SyntaxTree tree, IReadOnlyList<(int Start, int End)> selections, int direction,
        Func<string, SyntaxTree>? expand = null, bool includeStretch = false)
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
                ? VoicingEdit(t.Node, dir, includeStretch)
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
                var (p, i) = Sound(moved, sounding, includeStretch);
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
    /// <c>using</c>-expanded one); null means <paramref name="tree"/>. <paramref name="includeStretch"/>
    /// is the step's setting; an <c>@chord</c> with no shape written sounds the drawn default
    /// either way (the compiler's pick, which prefers a shape without a stretch).
    /// </summary>
    public static Audition? AuditionAt(string text, SyntaxTree tree, int offset, SyntaxTree? sounding = null,
        bool includeStretch = false)
    {
        if (TargetAt(text, tree, offset) is not { } target)
            return null;
        // A chords-row entry sounds when STEPPED, not when the caret lands on it: the caret
        // audition stays what it was before row entries became steppable (Lily#'s choice,
        // 2026-09-28 — a caret crossing a chord row would otherwise strike every chord).
        if (target.Node is ChordEntrySyntax)
            return null;
        if (target.Kind == TargetKind.Voicing
            && SoundedShape(target.Node, includeStretch) is null)
            return null;
        var (pitches, timbre) = Sound(target, sounding ?? tree, includeStretch);
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
        // A bare one on a chord(…) item names the item's words (ChordAnnotation.Of), so it
        // steps the item's shape.
        foreach (var mark in tree.GetNodes<MusicMarkSyntax>())
            if (mark.Name == "chord" && mark.Span.Start < offset && offset <= mark.Span.End)
                return ChordAnnotation.Of(mark) is { FromShapeItem: true }
                    ? new Target(TargetKind.Voicing, mark.Parent!)
                    : new Target(TargetKind.Voicing, mark);
        // A chord(…) item (owner's decision 2026-09-28): from its word 'chord' to its ')' —
        // the step rewrites its shape as an @chord's, on the part's tuning (StepTuning).
        foreach (var item in tree.GetNodes<ChordSyntax>())
            if (item.IsShapeChord && item.SourceStart <= offset && offset <= ShapeItemHeadEnd(item))
                return new Target(TargetKind.Voicing, item);
        // A chords-row entry: from its symbol's first character to its ')' (or its symbol's end).
        foreach (var entry in tree.GetNodes<ChordEntrySyntax>())
            if (entry.SourceStart <= offset && offset <= EntryEnd(entry))
                return new Target(TargetKind.Voicing, entry);

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

    /// <summary>Just past a <c>chord(…)</c> item's <c>)</c> (or its last word, unclosed).</summary>
    private static int ShapeItemHeadEnd(ChordSyntax item)
    {
        int end = item.SourceStart + Parser.Parser.ShapeChordWord.Length;
        for (int i = 0; i < item.SlotCount; i++)
            if (item.GetChild(i) is SyntaxTokenNode t && t.Text.Length > 0)
            {
                end = Math.Max(end, t.SourceStart + t.Text.Length);
                if (t.Kind == SyntaxKind.CloseParen)
                    break;
            }
            else if (item.GetChild(i) is not null and not SyntaxTokenNode)
                break;
        return end;
    }

    /// <summary>Just past a row entry's last token (its <c>)</c>, or its symbol).</summary>
    private static int EntryEnd(ChordEntrySyntax entry)
    {
        int end = entry.SourceStart;
        for (int i = 0; i < entry.SlotCount; i++)
            if (entry.GetChild(i) is SyntaxTokenNode t && t.Text.Length > 0)
                end = Math.Max(end, t.SourceStart + t.Text.Length);
        return end;
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
                _ => node is MusicMarkSyntax { Name: "chord" } or ChordEntrySyntax
                    or ChordSyntax { IsShapeChord: true },
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
    /// The shape edit of an <c>@chord</c> or a <c>chords</c> row entry (HANDOFF §2 K, owner's
    /// decisions 2026-09-28), a status-bar message, and whether it is steppable at all (a chord
    /// symbol — not a symbol-less shape, quoted text or the bare <c>@chord</c>). The step walks
    /// <see cref="ShapeOrder"/> on <see cref="StepTuning"/>, stepping the shape written for that
    /// tuning (the others in the group stay): with none written, Up writes the order's first —
    /// the default, so the diagram APPEARS (<c>G</c> → <c>G(320003)</c>) — and Down does
    /// nothing; from a written shape Up / Down write its neighbour — a shape with muted strings
    /// steps from the order's shape it mutes, and loses its <c>x</c> (the owner: mutes are put
    /// back once the shape is chosen); Down AT the default REMOVES the shape, and the diagram
    /// goes (a row's group left empty goes whole). A steppable one that cannot move answers no
    /// edit.
    /// <para>
    /// ★ IN A <c>chordDiagrams … all</c> SCORE (the first rendering the chord,
    /// <see cref="DrawsEveryChord"/>) a name alone already draws the default, so the step
    /// counts from there (owner's decision 2026-09-28): Up from the name writes the shape AFTER
    /// the default; Down on the name alone does nothing, and Down at a WRITTEN default removes it
    /// like in any score (the page keeps showing it); the status bar says why.
    /// </para>
    /// </summary>
    /// <remarks>
    /// A written shape the order does not hold — a STRETCH shape while
    /// <paramref name="includeStretch"/> is off (owner's decision 2026-09-28), or a shape no rule
    /// allows — steps by where it would SORT (<see cref="Music.ChordVoicings.CompareInOrder"/>;
    /// a muted copy sorts as the stretch shape it mutes): Up to the first shape of the order
    /// after it, Down to the last before it, or back to the default.
    /// </remarks>
    private static (Edit? Edit, string? Message, bool Handled) VoicingEdit(SyntaxNode node, int dir,
        bool includeStretch)
    {
        var (site, refusal) = ShapeSiteOf(node);
        if (site == null)
            return (null, refusal, refusal != null);
        if (site.Problems.Count > 0)
            return (null, site.Problems[0], true);

        var (word, note) = StepTuning(node);
        var tuningType = Tablature.Tunings.Parse(word);
        var tuning = Tablature.Tunings.GetTuning(tuningType);
        var (_, order) = ShapeOrder(node, site.Chord, includeStretch);
        // Under a capo the shapes are the PRESSED chord's (ChordStructure.Pressed), as the order is.
        var pressedChord = site.Chord.Pressed(CapoOf(node), 0);
        string symbol = site.Symbol;
        if (order.Count == 0)
            return (null, $"{symbol}: no shape to step through on {word}{note}.", true);

        // The shape written for the step's tuning, if any: its word, and its tuning word's.
        int shapeAt = -1, namedAt = -1;
        for (int i = 0; i + 1 < site.Words.Count && shapeAt < 0; i++)
            if (site.Words[i].Text is var t && Tablature.Tunings.Names.Contains(t)
                && Tablature.Tunings.Parse(t) == tuningType
                && Music.ChordShapes.StringCount(site.Words[i + 1].Text) == tuning.Length)
                (namedAt, shapeAt) = (i, i + 1);
        // Counted in strings, not characters: a dash-separated shape (8-x-x-8-8-11) is six.
        for (int i = 0; i < site.Words.Count && shapeAt < 0; i++)
            if (Music.ChordShapes.StartsShape(site.Words[i].Text)
                && (i == 0 || !Tablature.Tunings.Names.Contains(site.Words[i - 1].Text))
                && Music.ChordShapes.StringCount(site.Words[i].Text) == tuning.Length)
                shapeAt = i;

        // In a `chordDiagrams … all` score (the first rendering this chord) a name alone already
        // DRAWS the default (Music.ChordShapes.Drawn), so the default is where an unwritten name
        // stands — Lily#'s reading of the owner's decision 2026-09-28: Up from the name writes the
        // shape AFTER the default (writing the default itself would change nothing on the page);
        // Down at a written default removes it (below), the name alone drawing it all the same.
        // The same where the score's layout TABLE lists the chord (2026-09-29): the name alone
        // draws the table's shape, and the step counts from it.
        var nameAlone = ShapeOfNameAlone(node, site.Chord);
        bool drawsAll = nameAlone != null && DrawsEveryChord(node);
        string shows = drawsAll ? "this score draws every chord, so it shows the default"
            : "this score's layout table lists it, so it shows";
        int defaultAt = nameAlone != null ? PlaceInOrder(nameAlone.Frets, order) : -1;

        if (shapeAt < 0)
        {
            if (defaultAt >= 0)
            {
                string shown = Music.ChordVoicings.Spell(order[defaultAt]);
                if (dir < 0)
                    return (null, $"{symbol}: no shape written - {shows} "
                        + $"({word}: {shown}); Down does nothing, Up writes the next shape{note}", true);
                if (defaultAt + 1 >= order.Count)
                    return (null, $"{symbol}: the shape shown ({word}: {shown}) is the last - this score "
                        + $"already draws it{note}", true);
                return (Insert(site, Music.ChordVoicings.Spell(order[defaultAt + 1])),
                    ShapeMessage(symbol, defaultAt + 1, order) + note, true);
            }
            // No shape for this tuning: Up writes the drawn default, so the diagram appears;
            // Down has nothing to take away.
            if (dir < 0)
                return (null, $"{symbol}: no shape written, no diagram - Up adds one ({word}: "
                    + $"{Music.ChordVoicings.Spell(order[0])}){note}", true);
            return (Insert(site, Music.ChordVoicings.Spell(order[0])), ShapeMessage(symbol, 0, order) + note, true);
        }

        var written = site.Words[shapeAt];
        var frets = Music.ChordShapes.Frets(written.Text);
        int at = PlaceInOrder(frets, order), next;
        if (at < 0)
        {
            next = NextBySortKey(SortKeyOf(frets, tuning, pressedChord), order, dir);
            if (next < 0)
                return (null, $"{symbol}: '{written.Text}' sorts after the last of the {order.Count} shapes{note}.", true);
        }
        else if (dir < 0 && at == defaultAt)
        {
            // Down at the default of an `all` score REMOVES the written shape, as in any score
            // (owner, 2026-09-28: "@chord(Cm7 x35343) + Down should give @chord(Cm7)"). The page
            // does not change — the name alone draws the same default — but the source loses a
            // shape that says nothing, which is what Down at the default means everywhere else.
            return (Remove(site, namedAt >= 0 ? namedAt : shapeAt, shapeAt),
                $"{symbol}: shape removed - {(drawsAll
                    ? "this score draws every chord, so the name alone still shows the default"
                    : "this score's layout table lists it, so the name alone still shows it")} "
                + $"({Music.ChordVoicings.Spell(order[at])}){note}", true);
        }
        else if (dir < 0 && at == 0 && site.IsItem)
        {
            // A chord(…) item's shape IS its notes (required, owner's decision 2026-09-28):
            // Down stops at the default instead of leaving a spacer.
            return (null, ShapeMessage(symbol, 0, order) + " - the first; a chord(...) item keeps its shape" + note, true);
        }
        else if (dir < 0 && at == 0)
        {
            // Down at the default: the shape goes, and with it the diagram.
            return (Remove(site, namedAt >= 0 ? namedAt : shapeAt, shapeAt),
                $"{symbol}: shape removed, no diagram (Up adds {Music.ChordVoicings.Spell(order[0])}){note}", true);
        }
        else
        {
            next = at + dir;
            if (next >= order.Count)
                return (null, ShapeMessage(symbol, at, order) + note, true);
        }
        return (new Edit(written.Start, written.End, Music.ChordVoicings.Spell(order[next])),
            ShapeMessage(symbol, next, order) + note, true);
    }

    /// <summary>
    /// Where a step rewrites a chord's shapes: an <c>@chord(…)</c> with a symbol, or a
    /// <c>chords</c> row entry (<c>G</c>, <c>G(320003)</c>, <c>F(133211 2010)</c>) — the symbol,
    /// its chord, the words after it with their spans, the first problem of those words, where
    /// the symbol ends and, for a row entry, its parenthesised group.
    /// </summary>
    private sealed record ShapeSite(string Symbol, Music.ChordStructure Chord,
        List<(string Text, int Start, int End)> Words, List<string> Problems, int SymbolEnd,
        (int Open, int OpenEnd, int Close)? Group, bool IsRow, bool IsItem = false);

    /// <summary>The site a step at <paramref name="node"/> rewrites, or null with the refusal
    /// to show (null refusal: not steppable at all — the key's own command runs).</summary>
    private static (ShapeSite? Site, string? Refusal) ShapeSiteOf(SyntaxNode node)
    {
        // A chord(…) item: its words are an @chord's (Music.ShapeChords.Words).
        if (node is ChordSyntax { IsShapeChord: true } item)
        {
            var itemWords = Music.ShapeChords.Words(item);
            if (itemWords is not { Symbol: { } itemSymbol, Structure: { } itemChord })
                return (null, null);
            var itemSpans = item.ShapeWordSpans;
            var itemArgs = item.ShapeArguments;
            if (itemSpans.Count != itemArgs.Length || itemSpans.Count == 0)
                return (null, null);
            var itemList = new List<(string, int, int)>();
            for (int i = 1; i < itemSpans.Count; i++)
                itemList.Add((itemArgs[i].Text, itemSpans[i].Start, itemSpans[i].End));
            return (new ShapeSite(itemSymbol, itemChord, itemList, [.. itemWords.Problems.Select(p => p.Message)],
                itemSpans[0].End, null, IsRow: false, IsItem: true), null);
        }
        if (node is MusicMarkSyntax mark)
        {
            if (ChordAnnotation.Of(mark) is not { Symbol: { } symbol, Structure: { } chord } words)
                return (null, null);
            var spans = ChordAnnotation.WordSpans(mark);
            if (spans.Count != mark.Arguments.Length || spans.Count == 0)
                return (null, null);
            var list = new List<(string, int, int)>();
            for (int i = 1; i < spans.Count; i++)
                list.Add((mark.Arguments[i].Text, spans[i].Start, spans[i].End));
            return (new ShapeSite(symbol, chord, list, [.. words.Problems.Select(p => p.Message)],
                spans[0].End, null, IsRow: false), null);
        }
        if (node is not ChordEntrySyntax entry)
            return (null, null);
        string sym = entry.SymbolText;
        if (!Music.ChordStructure.TryParseChordEntry(sym, out var parsed))
            return (null, $"{sym}: the step writes shapes for a chord name, not a degree - its chord "
                + "depends on the key.");
        int symbolEnd = entry.SourceStart;
        SyntaxTokenNode? open = null, close = null;
        for (int i = 0; i < entry.SlotCount; i++)
        {
            if (entry.GetChild(i) is not SyntaxTokenNode tk)
                continue;
            if (tk.Kind == SyntaxKind.OpenParen)
                open ??= tk;
            else if (tk.Kind == SyntaxKind.CloseParen && open != null)
                close ??= tk;
            else if (open == null && tk.Text.Length > 0)
                symbolEnd = tk.SourceStart + tk.Text.Length;
        }
        if (open != null && close == null)
            return (null, $"{sym}: close the shape's ')' first.");
        var shapeWords = entry.ShapeWords;
        var problems = ChordDiagramScores.ShapesOf(entry).Problems;
        return (new ShapeSite(sym, parsed,
            [.. shapeWords.Select(w => (w.Text, w.Span.Start, w.Span.End))],
            [.. problems.Select(p => p.Message)], symbolEnd,
            open != null ? (open.SourceStart, open.SourceStart + 1, close!.SourceStart + 1) : null,
            IsRow: true), null);
    }

    /// <summary>The edit that adds <paramref name="spelled"/> to a site: after its last word
    /// (a row entry with no group gains one: <c>G</c> → <c>G(320003)</c>).</summary>
    private static Edit Insert(ShapeSite site, string spelled)
    {
        if (site.Words.Count > 0)
            return new Edit(site.Words[^1].End, site.Words[^1].End, " " + spelled);
        if (site.Group is { } g)
            return new Edit(g.OpenEnd, g.OpenEnd, spelled);
        return new Edit(site.SymbolEnd, site.SymbolEnd, site.IsRow ? "(" + spelled + ")" : " " + spelled);
    }

    /// <summary>The edit that takes words <paramref name="from"/>..<paramref name="to"/> (a
    /// shape and its tuning word) out of a site, with the space before them — or, first in a
    /// row's group, after them; a row's group left empty goes whole (<c>G(320003)</c> → <c>G</c>).</summary>
    private static Edit Remove(ShapeSite site, int from, int to)
    {
        if (site.Group is { } g && site.Words.Count == to - from + 1)
            return new Edit(site.SymbolEnd, g.Close, "");
        if (from > 0)
            return new Edit(site.Words[from - 1].End, site.Words[to].End, "");
        if (site.Group != null)
            return new Edit(site.Words[from].Start,
                to + 1 < site.Words.Count ? site.Words[to + 1].Start : site.Words[to].End, "");
        return new Edit(site.SymbolEnd, site.Words[to].End, "");
    }

    /// <summary>"Cm7: shape 3 of 21 (x35343)" — the position counted from 1, the first being
    /// the default the step writes first.</summary>
    private static string ShapeMessage(string symbol, int at, IReadOnlyList<ImmutableArray<int>> order)
        => $"{symbol}: shape {at + 1} of {order.Count} ({Music.ChordVoicings.Spell(order[at])})";

    /// <summary>What a written shape the order lacks sorts as: the stretch-inclusive base it
    /// is (or is a muted copy of), else itself.</summary>
    private static IReadOnlyList<int> SortKeyOf(ImmutableArray<int> written, IReadOnlyList<int> tuning,
        Music.ChordStructure chord)
    {
        var bases = Music.ChordVoicings.For(tuning, chord, includeStretch: true).Bases;
        int i = PlaceInOrder(written, bases);
        return i >= 0 ? bases[i] : written;
    }

    /// <summary>Up: the first shape of the order (past the default) sorting after
    /// <paramref name="key"/>, −1 when none does; Down: the last sorting before it, else the
    /// default (0).</summary>
    private static int NextBySortKey(IReadOnlyList<int> key, IReadOnlyList<ImmutableArray<int>> order, int dir)
    {
        if (dir > 0)
        {
            for (int i = 1; i < order.Count; i++)
                if (Music.ChordVoicings.CompareInOrder(order[i], key) > 0)
                    return i;
            return -1;
        }
        for (int i = order.Count - 1; i >= 1; i--)
            if (Music.ChordVoicings.CompareInOrder(order[i], key) < 0)
                return i;
        return 0;
    }

    /// <summary>
    /// The order the step walks (K3 — the editor's own, NOT frozen in the language): the
    /// default first (<see cref="Music.ChordShapes.Default"/>: LilyPond's predefined shape, else
    /// the compiler's fallback), then Lily#'s maximal shapes in their order
    /// (<see cref="Music.ChordVoicings"/>) — the normal rule's, or with
    /// <paramref name="includeStretch"/> the stretch-inclusive rule's (owner's decision
    /// 2026-09-28, <c>lilysharp.chordShapes.includeStretch</c>) — each once, on
    /// <see cref="StepTuning"/>. <paramref name="site"/> is an <c>@chord</c> or a row entry.
    /// </summary>
    /// <remarks>
    /// Shapes at frets 10–15 are IN the order since 2026-09-28 (owner's decision): the shape
    /// grammar writes them dash-separated (<see cref="Music.ChordShapes.TryRead"/>), and the
    /// step spells each with <see cref="Music.ChordVoicings.Spell"/> — one character per string
    /// when every fret is 9 or less, else the compact form, a '-' on each side of each two-digit
    /// fret (owner's decision, same day) — so Cm walks on past <c>8xx888</c> to
    /// <c>8xx88-11</c>. (Until then they were left out: one character per string could not
    /// write them back.)
    /// </remarks>
    public static (IReadOnlyList<int> Tuning, List<ImmutableArray<int>> Order) ShapeOrder(
        SyntaxNode site, Music.ChordStructure chord, bool includeStretch)
    {
        var tuningType = Tablature.Tunings.Parse(StepTuning(site).Word);
        IReadOnlyList<int> tuning = Tablature.Tunings.GetTuning(tuningType);
        // Under a capo (the first score's, like the tuning) the shapes are the PRESSED chord's:
        // the sounding chord that many semitones down (ChordStructure.Pressed; 2026-09-29).
        var pressed = chord.Pressed(CapoOf(site), 0);
        var order = new List<ImmutableArray<int>>();
        void Add(ImmutableArray<int> shape)
        {
            if (!order.Any(o => o.SequenceEqual(shape)))
                order.Add(shape);
        }
        if (Music.ChordShapes.Default(tuningType, pressed) is { } first)
            Add(first.Frets);
        // On every tuning since 2026-09-29 (the ukulele's too: the rules less V4, K5 ⑥).
        foreach (var b in Music.ChordVoicings.For(tuning, pressed, includeStretch).Bases)
            Add(b);
        return (tuning, order);
    }

    /// <summary>The fret the capo is on in the first score rendering the chord at
    /// <paramref name="site"/> (<see cref="StepTuning"/>'s score), 0 for none.</summary>
    public static int CapoOf(SyntaxNode site)
        => ScoresOf(site, out _) is { Count: > 0 } scores ? scores[0].Score.Capo
            // A chord(…) item, or a mark no score places: the first score drawing its part.
            : ChordDiagramScores.CapoOfNode(site);

    /// <summary>
    /// Where <paramref name="frets"/> stands among a chord's shapes, for the hover:
    /// <c>shape 3 of 21 (34 with stretch)</c> — the normal order's count, and the
    /// stretch-inclusive one's when it differs (a hover carries no setting); a stretch shape
    /// says <c>stretch shape 25 of 34</c>. Null when neither order holds it.
    /// </summary>
    public static string? ShapePlace(SyntaxNode site, Music.ChordStructure chord, ImmutableArray<int> frets)
    {
        var (_, normal) = ShapeOrder(site, chord, includeStretch: false);
        var (_, stretch) = ShapeOrder(site, chord, includeStretch: true);
        string withStretch = stretch.Count != normal.Count ? $" ({stretch.Count} with stretch)" : "";
        int at = PlaceInOrder(frets, normal);
        if (at >= 0)
            return $"shape {at + 1} of {normal.Count}{withStretch}";
        int s = PlaceInOrder(frets, stretch);
        return s >= 0 ? $"stretch shape {s + 1} of {stretch.Count}" : null;
    }

    /// <summary>
    /// The tuning word the editor steps, sounds and offers a chord's shapes on, and a note for
    /// the status bar (or null): the owner's rule for which tuning a diagram draws on
    /// (<see cref="ChordDiagramsKey.Resolve"/>: the layout's <c>chordDiagrams</c>, else the
    /// part's fretted instrument, else the guitar), read in the FIRST score that renders the
    /// chord — for an <c>@chord</c>, the first score drawing its part
    /// (<see cref="ChordDiagramScores.TuningsOfMark"/>); for a row entry, the first score placing
    /// its row, on the staff it is first placed over (<see cref="ChordDiagramScores.TuningsOfRow"/>).
    /// </summary>
    /// <remarks>
    /// Lily#'s choices: when later scores draw it on another tuning the note says so
    /// (" - on guitar, the first score's; another draws ukulele"); when the first score writes
    /// <c>chordDiagrams none</c> the step still works, on the part's instrument or the guitar,
    /// and the note says that score draws no diagram.
    /// </remarks>
    public static (string Word, string? Note) StepTuning(SyntaxNode site)
    {
        var scores = ScoresOf(site, out string? partWord);
        if (scores.Count == 0)
            return (partWord ?? "guitar", null);
        if (scores[0].Word is not { } word)
            return (partWord ?? "guitar", " (the first score writes chordDiagrams none: no diagram there)");
        var other = scores.Select(s => s.Word).OfType<string>().FirstOrDefault(w => w != word);
        return (word, other == null ? null : $" - on {word}, the first score's; another draws {other}");
    }

    /// <summary>
    /// Whether the FIRST score rendering the chord (<see cref="StepTuning"/>'s) writes
    /// <c>chordDiagrams … all</c> — so a name with no shape already SHOWS the default there
    /// (owner's decision 2026-09-28), and the step treats the default as what the name draws.
    /// </summary>
    public static bool DrawsEveryChord(SyntaxNode site)
        => ScoresOf(site, out _) is { Count: > 0 } scores && scores[0].Score.All && scores[0].Word != null;

    /// <summary>
    /// The shape a NAME ALONE draws for <paramref name="chord"/> at <paramref name="site"/> in the
    /// first score rendering it (<see cref="StepTuning"/>'s): the default under
    /// <c>chordDiagrams … all</c>, the table's shape — or the default, for a name listed alone —
    /// where the score's layout table lists the chord (<see cref="Music.ChordShapeTable"/>,
    /// 2026-09-29); null where a name alone draws nothing.
    /// </summary>
    public static Music.ChosenShape? ShapeOfNameAlone(SyntaxNode site, Music.ChordStructure chord)
    {
        var scores = ScoresOf(site, out _);
        if (scores.Count == 0 || scores[0].Word is not { } word || !scores[0].Score.DrawsNamesAlone)
            return null;
        var score = scores[0].Score;
        return Music.ChordShapes.Drawn(Tablature.Tunings.Parse(word), [], score.All, chord, score.Table,
            score.Table != null ? ChordDiagramScores.SectionNameOf(site) : null, score.Capo);
    }

    /// <summary>The scores rendering the chord at <paramref name="site"/>, in document order, with
    /// the tuning word each draws it on; <paramref name="partWord"/> is an <c>@chord</c>'s part's
    /// fretted tuning (null for a row entry).</summary>
    private static IReadOnlyList<(ChordDiagramScores.Score Score, string? Word)> ScoresOf(SyntaxNode site,
        out string? partWord)
    {
        partWord = null;
        if (site is ChordEntrySyntax entry)
            return ChordDiagramScores.BlockOf(entry)?.PartName is { } row
                ? ChordDiagramScores.TuningsOfRow(RootOf(site), row) : [];
        partWord = ChordDiagramScores.FrettedWordOfNode(site);
        return site is MusicMarkSyntax mark ? ChordDiagramScores.TuningsOfMark(mark) : [];
    }

    private static SyntaxNode RootOf(SyntaxNode node)
    {
        while (node.Parent != null)
            node = node.Parent;
        return node;
    }

    /// <summary>Where a written shape stands in the order: its own place, else the first shape
    /// it is a muted copy of (equal on every string it sounds), else −1.</summary>
    private static int PlaceInOrder(ImmutableArray<int> written, IReadOnlyList<ImmutableArray<int>> order)
    {
        for (int i = 0; i < order.Count; i++)
            if (order[i].SequenceEqual(written))
                return i;
        for (int i = 0; i < order.Count; i++)
        {
            bool fits = order[i].Length == written.Length;
            for (int s = 0; s < written.Length && fits; s++)
                fits = written[s] < 0 || written[s] == order[i][s];
            if (fits)
                return i;
        }
        return -1;
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
    private static (ImmutableArray<int> Pitches, int Timbre) Sound(Target target, SyntaxTree sounding,
        bool includeStretch)
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
                var mark = target.Node;
                if (SoundedShape(mark, includeStretch) is not { } sounded)
                    return ([], 0);
                var (tuning, frets) = sounded;
                var pitches = new SortedSet<int>();
                for (int s = 0; s < frets.Length && s < tuning.Count; s++)
                    if (frets[s] >= 0)
                        pitches.Add(tuning[s] + frets[s]);
                int host = mark is MusicMarkSyntax ? mark.Parent?.SourceStart ?? -1
                    : mark is ChordSyntax ? mark.SourceStart : -1;
                return ([.. pitches], index.Timbre.TryGetValue(host, out int t) ? t : GuitarTimbre);
            }
        }
    }

    /// <summary>
    /// The shape an <c>@chord</c> or a row entry sounds in the editor, on
    /// <see cref="StepTuning"/>: the shape written for that tuning, else the first of
    /// <see cref="ShapeOrder"/> (the default the step would write — it sounds though no diagram
    /// draws, so a name can be tried by ear); null for one with no symbol it can read and no
    /// shape, or none on the tuning.
    /// </summary>
    private static (IReadOnlyList<int> Tuning, ImmutableArray<int> Frets)? SoundedShape(SyntaxNode site,
        bool includeStretch)
    {
        ImmutableArray<Music.WrittenShape> shapes;
        Music.ChordStructure? structure;
        if (site is ChordEntrySyntax entry)
        {
            shapes = ChordDiagramScores.ShapesOf(entry).Shapes;
            structure = Music.ChordStructure.TryParseChordEntry(entry.SymbolText, out var parsed) ? parsed : null;
        }
        else if (site is MusicMarkSyntax mark
                 && ChordAnnotation.Of(mark) is { IsBare: false, QuotedText: null } words)
            (shapes, structure) = (words.Shapes, words.Structure);
        else if (site is ChordSyntax { IsShapeChord: true } item
                 && Music.ShapeChords.Words(item) is { IsBare: false, QuotedText: null } itemWords)
            (shapes, structure) = (itemWords.Shapes, itemWords.Structure);
        else
            return null;
        var tuningType = Tablature.Tunings.Parse(StepTuning(site).Word);
        if (Music.ChordShapes.WrittenFor(tuningType, shapes) is { } written)
            return (Tablature.Tunings.GetTuning(tuningType), Music.ChordShapes.Frets(written));
        if (structure is not { } chord)
            return null;
        var (t, order) = ShapeOrder(site, chord, includeStretch);
        return order.Count > 0 ? (t, order[0]) : null;
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
