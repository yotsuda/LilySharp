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

using System.Collections.Immutable;
using System.Text;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Writes a <c>chord(…)</c> or <c>diagram(…)</c> item out as the <c>&lt;…&gt;</c> chord it plays
/// (owner's request 2026-10-31: right-click → "Convert to chord notes"):
/// <c>diagram(133211)4</c> → <c>&lt;f,\6 c\5 f'\4 a'\3 c'\2 f''\1&gt;4</c> on a guitar.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The notes are the ones the walk gives the item (<see cref="Svg.Collector.MeasureCollector.ShapeNotesObserver"/>),
/// lowest first — so the frame the chord hands on, its first note, is the item's lowest note,
/// the one the item handed on — each with its string number when it has one, in the key's
/// spelling the item used.</item>
/// <item>Their octave marks are read off the same walk the octave-mode converter makes
/// (<see cref="OctaveModeConverter.Walk"/>): each new pitch is forced to the octave the item
/// played it at, and the marks are what that took from the frame — relative or absolute alike.</item>
/// <item>The NAME a <c>chord(…)</c> item printed stays, as a bare <c>&lt;…&gt;@chord</c> — named from
/// the notes, which is what the item was (owner's decision 2026-10-31: not <c>@chord(F)</c>); only
/// when the bare mark would print another name than the item did is the symbol kept in it,
/// <c>@chord(F)</c>. The diagram does not stay (owner's decision the same day: the notes are what
/// was asked for, and a diagram left over them would not follow an edit); "Add Guitar Diagram"
/// goes back.</item>
/// <item>The file must sound the same afterwards (<see cref="Semantics.PlayedNotes.Signature"/>),
/// or nothing changes.</item>
/// </list>
/// </remarks>
public static class ShapeChordConverter
{
    /// <summary>The replacement of <c>[Start, End)</c>, or why there is none.</summary>
    public sealed record Result(int Start, int End, string? Replacement, string? Error);

    public static Result ToNotes(string source, int offset)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors - no changes made.");
        var item = tree.GetNodes<ChordSyntax>().FirstOrDefault(c => c.IsShapeChord
            && c.Span.Start <= offset && offset <= c.Span.End);
        if (item == null)
            return Fail("Put the caret on a chord(...) or diagram(...) item.");
        if (InGroup(item))
            return Fail("A chord(...) or diagram(...) item inside << >> plays its strings in turn - write them out by hand.");
        int close = item.ShapeCloseEnd;
        if (close < 0)
            return Fail("The item's parentheses are not closed.");

        // The notes the item plays, every time the walk reads it.
        ImmutableArray<ShapeNote>? played = null;
        bool differs = false;
        if (!OctaveModeConverter.Walk(tree, (_, octave) => octave, (pos, notes) =>
            {
                if (pos != item.SourceStart)
                    return;
                if (played is { } before && !before.SequenceEqual(notes))
                    differs = true;
                played ??= notes;
            }))
            return Fail("The file could not be compiled - no changes made.");
        if (played is not { IsEmpty: false } written)
            return Fail("This item plays no notes here (its shape fits no string of the part) - nothing to convert.");
        if (differs)
            return Fail("This item plays different notes in different scores - convert it by hand.");

        // The chord, its pitches without marks; where each pitch's marks go.
        bool contracted = KeyRespeller.UsesContractedFlats(tree.GetRoot());
        var notes = ShapeChords.Ascending(written).ToList();
        var sb = new StringBuilder("<");
        var pitchAt = new List<(int Start, int MarksAt)>();   // relative to the replacement's start
        foreach (var n in notes)
        {
            if (sb.Length > 1)
                sb.Append(' ');
            int start = sb.Length;
            sb.Append(KeyRespeller.Name(n.Step, n.Alter, contracted));
            pitchAt.Add((start, sb.Length));
            if (n.StringNumber > 0)
                sb.Append('\\').Append(n.StringNumber);
        }
        sb.Append('>');

        // The tail stays as written; what was seen comes after its duration, in one word.
        int end = item.Duration is { } duration ? duration.Span.End : close;
        string? symbol = ShownSymbol(source, item, out bool showsName);
        string mark = showsName ? "@chord" : "";
        string tailBeforeMark = source[close..end];
        string chordText = sb.ToString();
        string Replacement(IReadOnlyList<int> marks, string nameMark)
        {
            var r = new StringBuilder(chordText);
            for (int k = pitchAt.Count - 1; k >= 0; k--)
                r.Insert(pitchAt[k].MarksAt, Marks(marks[k]));
            return r + tailBeforeMark + nameMark;
        }

        // Each pitch's marks: the octave the item played it at, less what the frame gives it.
        string candidate = source[..item.SourceStart] + Replacement(new int[notes.Count], mark) + source[end..];
        var at = new Dictionary<int, int>();
        for (int k = 0; k < pitchAt.Count; k++)
            at[item.SourceStart + pitchAt[k].Start] = k;
        var marks = new int?[notes.Count];
        bool conflict = false;
        if (!OctaveModeConverter.Walk(SyntaxTree.Parse(candidate), (pos, octave) =>
            {
                if (!at.TryGetValue(pos, out int k))
                    return octave;
                int wanted = notes[k].Octave - octave;
                if (marks[k] is { } earlier && earlier != wanted)
                    conflict = true;
                marks[k] = wanted;
                return notes[k].Octave;
            }))
            return Fail("The converted file could not be compiled - no changes made.");
        if (conflict || marks.Any(m => m is null))
            return Fail("The item is played where its notes would need different octave marks - convert it by hand.");

        int[] octaveMarks = [.. marks.Select(m => m!.Value)];
        string replacement = Replacement(octaveMarks, mark);
        string result = source[..item.SourceStart] + replacement + source[end..];
        // What the file plays — the MIDI, else the page (a file whose scores name their own forms).
        string sound = Semantics.PlayedNotes.Signature(tree);
        if (Semantics.PlayedNotes.Signature(SyntaxTree.Parse(result)) != sound)
            return Fail("Written as notes, the item would not sound the same - no changes made.");
        // The bare @chord names the chord from its notes. When that is not the name the item
        // printed, the symbol goes into the mark, so the page reads the same.
        if (symbol != null && PrintedNames(SyntaxTree.Parse(result)) != PrintedNames(tree))
        {
            replacement = Replacement(octaveMarks, "@chord(" + symbol + ")");
            result = source[..item.SourceStart] + replacement + source[end..];
            if (Semantics.PlayedNotes.Signature(SyntaxTree.Parse(result)) != sound)
                return Fail("Written as notes, the item would not sound the same - no changes made.");
        }
        return new Result(item.SourceStart, end, replacement, null);
    }

    /// <summary>The chord names the page prints, score by score in source order — compared as a
    /// whole before and after the conversion, since a note-attached mark's name stands at the
    /// MARK's source position and the item's at the item's.</summary>
    private static string PrintedNames(SyntaxTree tree)
    {
        var specs = Svg.Collector.RenderSpecParser.FindAll(tree);
        IEnumerable<Svg.Collector.RenderSpec?> passes = specs.Count == 0
            ? new Svg.Collector.RenderSpec?[] { null } : (IEnumerable<Svg.Collector.RenderSpec?>)specs;
        var sb = new StringBuilder();
        foreach (var spec in passes)
        {
            Svg.Model.MultiStaffScore score;
            try { score = Svg.SvgGenerator.CollectScore(tree, spec); }
            catch { continue; }
            foreach (var name in score.ChordNames.OrderBy(c => c.SourcePosition))
                sb.Append(name.ChordText).Append('\n');
            sb.Append('\f');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Right-click → "Add Guitar Diagram" on a <c>&lt;…&gt;</c> chord (owner's decision
    /// 2026-10-31): the chord stays as written and gains <c>@diagram(SHAPE)</c> — the shape a bare
    /// <c>@diagram</c> would draw (<see cref="ChordFingering"/>, on the part's fretted strings, else
    /// the guitar's), written out so it shows in the source and the step keys can walk it. A bare
    /// <c>@diagram</c> already there is the one written out. Nothing sounds different: the edit is
    /// a mark. LYS1057 warns when the notes are edited away from the shape later.
    /// <para>
    /// On a single note too (review 2026-10-31, C1): <c>c'1</c> → <c>c'1@diagram(xxxx1x)</c>, where
    /// the note is held — what a bare <c>@diagram</c> on it draws, and what its step walks.
    /// </para>
    /// </summary>
    public static Result AddDiagram(string source, int offset)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors - no changes made.");
        if (DiagramTarget(tree, offset) is not { } target)
            return Fail("Put the caret on a <...> chord or a note.");
        var marks = Articulations(target).OfType<MusicMarkSyntax>().Where(m => m.Name == "diagram").ToList();
        if (marks.Any(m => !m.Arguments.IsEmpty))
            return Fail(target is NoteSyntax ? "This note already has a diagram." : "This chord already has a diagram.");

        if (PlayedPitches(tree, target) is not { } pitches)
            return Fail(target is NoteSyntax ? "This note is not played in any score - nothing to finger."
                : "This chord is not played in any score - nothing to finger.");
        if (ChordFingering.Fingering(pitches, StringsOf(target)) is not { } frets || frets.Max() > 15)
            return Fail(target is NoteSyntax ? "No string of the instrument plays this note - nothing to draw."
                : "No hand can hold this chord (LYS1055) - nothing to draw.");

        string mark = "@diagram(" + ChordVoicings.Spell(frets) + ")";
        if (marks.FirstOrDefault() is { } bare)
            return new Result(bare.Span.Start, bare.Span.End, mark, null);
        int at = target switch
        {
            NoteSyntax note => note.Tremolo?.Span.End ?? note.Duration?.Span.End ?? note.Pitch.Span.End,
            ChordSyntax chord => chord.Duration is { } duration ? duration.Span.End : CloseAngleEnd(chord),
            _ => -1,
        };
        return at < 0 ? Fail("The chord's brackets are not closed.") : new Result(at, at, mark, null);
    }

    /// <summary>
    /// Right-click → "Convert to chord(…)" on a <c>&lt;…&gt;</c> chord (review 2026-10-31, C2) — the
    /// way back from "Convert to Chord Notes": <c>&lt;f a c'&gt;1</c> → <c>chord(F)1</c>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The name is an <c>@chord(NAME)</c> written on the chord, else the one a bare
    /// <c>@chord</c> names from its notes (spelled as they are written); notes that name no chord
    /// refuse.</item>
    /// <item>A <c>chord(NAME)</c> plays its close voicing: when that is not the notes, the shape a
    /// hand holds them with goes in too — <c>chord(F 133211)</c> — in a part with a fretted
    /// instrument, or when the chord already draws a diagram (its <c>@diagram(SHAPE)</c>, or the
    /// first fingering a bare <c>@diagram</c> draws). A drawn diagram always carries its shape over,
    /// so the page keeps it.</item>
    /// <item>The <c>@chord</c> and <c>@diagram</c> marks go — the item now says both; every other
    /// mark and the duration stay.</item>
    /// <item>The file must sound the same afterwards (<see cref="Semantics.PlayedNotes.Signature"/>),
    /// or nothing changes — notes whose octave hangs on the chord's first note in relative mode
    /// included.</item>
    /// </list>
    /// </remarks>
    public static Result ToChordItem(string source, int offset)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors - no changes made.");
        var chord = tree.GetNodes<ChordSyntax>().FirstOrDefault(c => !c.IsShapeChord && !c.IsEmpty
            && c.Span.Start <= offset && offset <= c.Span.End);
        if (chord == null || chord.DrumNames.Any())
            return Fail("Put the caret on a <...> chord.");
        if (InGroup(chord))
            return Fail("Inside << >> a chord(...) item plays its strings in turn - convert it by hand.");
        int closeEnd = CloseAngleEnd(chord);
        if (closeEnd < 0)
            return Fail("The chord's brackets are not closed.");

        var seen = chord.Articulations.OfType<MusicMarkSyntax>().Where(m => m.Name is "chord" or "diagram").ToList();
        var nameMark = seen.FirstOrDefault(m => m.Name == "chord");
        var diagramMark = seen.FirstOrDefault(m => m.Name == "diagram");
        if (PlayedPitches(tree, chord) is not { } pitches)
            return Fail("This chord is not played in any score - nothing to convert.");

        // The name: written, else read off the notes.
        string? name;
        bool nameHasShape = false;
        if (nameMark is { Arguments.IsEmpty: false })
        {
            var words = nameMark.Arguments.Select(a => a.Text).ToList();
            nameHasShape = words.Any(w => ChordShapes.TryRead(w, out _, out _));
            name = string.Join(" ", words);
        }
        else
            name = NamedFromNotes(tree, chord) is { } structure ? ChordShapes.SourceSymbol(structure) : null;
        if (name == null)
            return Fail("These notes name no chord - write @chord(NAME) on it first, or convert it by hand.");

        // The shape: the one drawn, else the first a hand holds the notes with.
        var strings = StringsOf(chord);
        string? fingered = ChordFingering.Fingering(pitches, strings) is { } frets && frets.Max() <= 15
            ? ChordVoicings.Spell(frets) : null;
        string? drawn = diagramMark == null ? null
            : diagramMark.Arguments.IsEmpty ? fingered : string.Join(" ", diagramMark.Arguments.Select(a => a.Text));
        var candidates = new List<string>();
        if (nameHasShape || diagramMark == null)
            candidates.Add(name);
        if (!nameHasShape)
        {
            if (drawn != null)
                candidates.Add(name + " " + drawn);
            else if (diagramMark == null && fingered != null && PartOf(chord)?.FrettedTuning != null)
                candidates.Add(name + " " + fingered);
        }

        // The tail stays as written, less the marks the item now says.
        int end = Math.Max(closeEnd, seen.Count == 0 ? closeEnd : seen.Max(m => m.Span.End));
        var tail = new StringBuilder();
        int from = closeEnd;
        foreach (var m in seen.OrderBy(m => m.Span.Start))
        {
            tail.Append(source, from, m.Span.Start - from);
            from = m.Span.End;
        }
        tail.Append(source, from, end - from);

        string sound = Semantics.PlayedNotes.Signature(tree);
        foreach (var words in candidates)
        {
            string replacement = "chord(" + words + ")" + tail;
            var after = SyntaxTree.Parse(source[..chord.Span.Start] + replacement + source[end..]);
            if (!after.HasErrors && Semantics.PlayedNotes.Signature(after) == sound)
                return new Result(chord.Span.Start, end, replacement, null);
        }
        return Fail(candidates.Count == 1
            ? $"chord({candidates[0]}) would not sound the same as these notes - no changes made."
            : $"Neither chord({candidates[0]}) nor chord({candidates[1]}) would sound the same as these notes - no changes made.");
    }

    /// <summary>The chord a bare <c>@chord</c> on <paramref name="chord"/> names from its notes
    /// (the editor's hover reads the same), in the part it is written in.</summary>
    private static ChordStructure? NamedFromNotes(SyntaxTree tree, ChordSyntax chord)
    {
        string? part = null;
        for (var n = chord.Parent; n != null && part == null; n = n.Parent)
            part = n switch
            {
                PartDeclarationSyntax p => p.Name.Text,
                PartBlockSyntax b => b.Name,
                _ => null,
            };
        try
        {
            var collector = new Svg.Collector.MeasureCollector { RecordsChordFacts = true };
            collector.Collect(tree, part);
            return collector.ChordFacts.TryGetValue(chord.Span.Start, out var facts) ? facts.Structure : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>What "Add Guitar Diagram" works on at <paramref name="offset"/>: a pitched
    /// <c>&lt;…&gt;</c> chord, else a single pitched note (not a rest or a drum).</summary>
    private static SyntaxNode? DiagramTarget(SyntaxTree tree, int offset)
    {
        var chord = tree.GetNodes<ChordSyntax>().FirstOrDefault(c => !c.IsShapeChord && !c.IsEmpty
            && c.Span.Start <= offset && offset <= c.Span.End);
        if (chord != null)
            return chord.DrumNames.Any() ? null : chord;
        return tree.GetNodes<NoteSyntax>().FirstOrDefault(n => n.Span.Start <= offset && offset <= n.Span.End);
    }

    private static ChildNodeList Articulations(SyntaxNode node)
        => node is NoteSyntax note ? note.Articulations : ((ChordSyntax)node).Articulations;

    /// <summary>
    /// Whether "Add Guitar Diagram" is offered at <paramref name="offset"/>: on a <c>&lt;…&gt;</c>
    /// chord with no written diagram, anywhere; on a single note with none, only in a part that
    /// plays a fretted instrument (review 2026-10-31, C1) — a flute's melody note asks for no
    /// fingering chart, and the light bulb stays quiet there.
    /// </summary>
    public static bool OffersDiagram(SyntaxTree tree, int offset)
    {
        if (DiagramTarget(tree, offset) is not { } target
            || Articulations(target).Any(a => a is MusicMarkSyntax { Name: "diagram", Arguments.IsEmpty: false }))
            return false;
        return target is ChordSyntax || PartOf(target)?.FrettedTuning != null;
    }

    private static int CloseAngleEnd(ChordSyntax chord)
    {
        for (int i = 0; i < chord.SlotCount; i++)
            if (chord.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.CloseAngle } close)
                return close.Span.End;
        return -1;
    }

    /// <summary>The sounding pitches a <c>&lt;…&gt;</c> chord or a single note writes, lowest first, as
    /// the page draws it in the first score that renders it, or null when no score does.</summary>
    /// <remarks>Read off the page, not the MIDI export (owner's report 2026-10-31): a file whose
    /// scores each name their own form plays nothing as MIDI, and the diagram's step and "Add Guitar
    /// Diagram" refused there ("not played in any score").</remarks>
    internal static int[]? PlayedPitches(SyntaxTree tree, SyntaxNode chord)
    {
        var specs = Svg.Collector.RenderSpecParser.FindAll(tree);
        IEnumerable<Svg.Collector.RenderSpec?> passes = specs.Count == 0
            ? new Svg.Collector.RenderSpec?[] { null } : (IEnumerable<Svg.Collector.RenderSpec?>)specs;
        foreach (var spec in passes)
        {
            Svg.Model.MultiStaffScore score;
            try { score = Svg.SvgGenerator.CollectScore(tree, spec); }
            catch { continue; }
            foreach (var (_, staff, _) in score.EnumerateStaves())
                foreach (var voice in staff.Voices)
                    foreach (var measure in voice.Measures)
                        foreach (var item in measure.Items)
                        {
                            if (item.SourcePosition != chord.SourceStart)
                                continue;
                            if (item is Svg.Model.ChordItem c && !c.Notes.IsDefaultOrEmpty)
                                return [.. c.Notes.Select(n => n.Midi).Distinct().Order()];
                            if (item is Svg.Model.NoteItem n && n.Midi > 0)
                                return [n.Midi];
                        }
        }
        return null;
    }

    /// <summary>The open strings a chord's diagram is fretted on: its part's fretted instrument's,
    /// else the guitar's (the strings a bare <c>@diagram</c> draws on).</summary>
    internal static int[] StringsOf(SyntaxNode node)
        => Tablature.Tunings.GetTuning(PartOf(node)?.FrettedTuning ?? TuningType.Guitar);

    /// <summary>The header of the part <paramref name="node"/> is written in, or null (a phrase body).</summary>
    private static Semantics.PartHeaderDefaults? PartOf(SyntaxNode node)
    {
        SyntaxNode root = node;
        while (root.Parent != null)
            root = root.Parent;
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            if (p is PartDeclarationSyntax declared)
                return Semantics.PartHeaderDefaults.Read(declared);
            if (p is PartBlockSyntax block)
                return Semantics.ConcertPitch.FindPart(root, block.Name) is { } part ? Semantics.PartHeaderDefaults.Read(part) : null;
        }
        return null;
    }

    /// <summary>The symbol a <c>chord(…)</c> item printed — <c>F</c> of <c>chord(F 133211)</c>, null
    /// for a symbol-less item (named from its notes) — and whether the item showed a name at all
    /// (<paramref name="showsName"/>): not a <c>diagram(…)</c>, whose diagram is not carried over
    /// (owner's decision 2026-10-31), and not an item an <c>@chord</c> or <c>@diagram</c> already
    /// draws instead of, which stays where it is.</summary>
    private static string? ShownSymbol(string source, ChordSyntax item, out bool showsName)
    {
        showsName = item.ShowsName && !item.Articulations.Any(a => a is MusicMarkSyntax { Name: "chord" or "diagram" });
        if (!showsName)
            return null;
        var spans = item.ShapeWordSpans;
        return spans.Count > 0 && source[spans[0].Start..spans[0].End] is var first
            && !ChordShapes.TryRead(first, out _, out _) && !Tablature.Tunings.Names.Contains(first)
            ? first : null;
    }

    private static bool InGroup(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is ArpeggioSyntax)
                return true;
        return false;
    }

    private static string Marks(int marks) => marks >= 0 ? new string('\'', marks) : new string(',', -marks);

    private static Result Fail(string error) => new(-1, -1, null, error);
}
