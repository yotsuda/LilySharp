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
/// <item>The NAME a <c>chord(…)</c> item printed stays: <c>&lt;…&gt;@chord(F)</c>. The diagram does
/// not (owner's decision 2026-10-31: the notes are what was asked for, and a diagram left over
/// them would not follow an edit); "Convert to Guitar Diagram" goes back.</item>
/// <item>The file must sound the same afterwards (<see cref="OctaveModeConverter.SoundSignature"/>),
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
        string mark = NameMark(source, item);
        string tailBeforeMark = source[close..end];
        string chordText = sb.ToString();
        string Replacement(IReadOnlyList<int> marks)
        {
            var r = new StringBuilder(chordText);
            for (int k = pitchAt.Count - 1; k >= 0; k--)
                r.Insert(pitchAt[k].MarksAt, Marks(marks[k]));
            return r + tailBeforeMark + mark;
        }

        // Each pitch's marks: the octave the item played it at, less what the frame gives it.
        string candidate = source[..item.SourceStart] + Replacement(new int[notes.Count]) + source[end..];
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

        string replacement = Replacement([.. marks.Select(m => m!.Value)]);
        string result = source[..item.SourceStart] + replacement + source[end..];
        if (OctaveModeConverter.SoundSignature(result) != OctaveModeConverter.SoundSignature(source))
            return Fail("Written as notes, the item would not sound the same - no changes made.");
        return new Result(item.SourceStart, end, replacement, null);
    }

    /// <summary>
    /// Right-click → "Convert to Guitar Diagram" on a <c>&lt;…&gt;</c> chord (owner's decision
    /// 2026-10-31): the chord stays as written and gains <c>@diagram(SHAPE)</c> — the shape a bare
    /// <c>@diagram</c> would draw (<see cref="ChordFingering"/>, on the part's fretted strings, else
    /// the guitar's), written out so it shows in the source and the step keys can walk it. A bare
    /// <c>@diagram</c> already there is the one written out. Nothing sounds different: the edit is
    /// a mark. LYS1057 warns when the notes are edited away from the shape later.
    /// </summary>
    public static Result AddDiagram(string source, int offset)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors - no changes made.");
        var chord = tree.GetNodes<ChordSyntax>().FirstOrDefault(c => !c.IsShapeChord
            && c.Span.Start <= offset && offset <= c.Span.End);
        if (chord == null || chord.DrumNames.Any())
            return Fail("Put the caret on a <...> chord.");
        var marks = chord.Articulations.OfType<MusicMarkSyntax>().Where(m => m.Name == "diagram").ToList();
        if (marks.Any(m => !m.Arguments.IsEmpty))
            return Fail("This chord already has a diagram.");

        if (PlayedPitches(tree, chord) is not { } pitches)
            return Fail("This chord is not played in any score - nothing to finger.");
        if (ChordFingering.Fingering(pitches, StringsOf(chord)) is not { } frets || frets.Max() > 15)
            return Fail("No hand can hold this chord (LYS1055) - nothing to draw.");

        string mark = "@diagram(" + ChordVoicings.Spell(frets) + ")";
        if (marks.FirstOrDefault() is { } bare)
            return new Result(bare.Span.Start, bare.Span.End, mark, null);
        int at = chord.Duration is { } duration ? duration.Span.End : CloseAngleEnd(chord);
        return at < 0 ? Fail("The chord's brackets are not closed.") : new Result(at, at, mark, null);
    }

    private static int CloseAngleEnd(ChordSyntax chord)
    {
        for (int i = 0; i < chord.SlotCount; i++)
            if (chord.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.CloseAngle } close)
                return close.Span.End;
        return -1;
    }

    /// <summary>The pitches a <c>&lt;…&gt;</c> chord plays, lowest first, as the MIDI export starts
    /// them (one moment, one part: the first time it is played), or null when it is not played.</summary>
    internal static int[]? PlayedPitches(SyntaxTree tree, ChordSyntax chord)
    {
        var span = chord.Span;
        var played = new Midi.MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes)
            .Where(n => n.SourcePos >= span.Start && n.SourcePos < span.End)
            .GroupBy(n => (n.Part, n.StartTick)).FirstOrDefault();
        return played?.Select(n => n.Pitch).Distinct().OrderBy(p => p).ToArray();
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

    /// <summary>The NAME a <c>chord(…)</c> item printed, as the mark that prints it on a
    /// <c>&lt;…&gt;</c> chord — <c>@chord(F)</c>, or a bare <c>@chord</c> for a symbol-less item
    /// (named from the notes, as the item was) — and nothing for a <c>diagram(…)</c>: the
    /// diagram is not carried over (owner's decision 2026-10-31), so a shape never follows the
    /// symbol into the mark.</summary>
    private static string NameMark(string source, ChordSyntax item)
    {
        // An @chord or @diagram on the item draws instead of it, and stays where it is.
        if (!item.ShowsName || item.Articulations.Any(a => a is MusicMarkSyntax { Name: "chord" or "diagram" }))
            return "";
        var spans = item.ShapeWordSpans;
        string? symbol = spans.Count > 0 && source[spans[0].Start..spans[0].End] is var first
            && !ChordShapes.TryRead(first, out _, out _) && !Tablature.Tunings.Names.Contains(first)
            ? first : null;
        return symbol is null ? "@chord" : "@chord(" + symbol + ")";
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
