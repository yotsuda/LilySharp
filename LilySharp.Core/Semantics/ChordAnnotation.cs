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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// What an <c>@chord(…)</c> argument says, word by word: the chord symbol, and the SHAPES
/// written for its chord diagram.
/// </summary>
/// <remarks>
/// <para>
/// Owner's design, 2026-09-28 (HANDOFF §2 K1), replacing the voicing index and the
/// <c>mute</c> words of 2026-09-27, which never shipped. The argument is read as WORDS — the
/// runs <see cref="MarkArgument"/> separates at whitespace and ',':
/// <code>
/// @chord(Cm7)                          name only
/// @chord(Cm7 x3x546)                   name + the shape its diagram draws
/// @chord(F guitar 133211 ukulele 2010) shapes for two tunings, named (ChordShapes)
/// @chord(x32010)                       a shape alone; the name is derived from its notes
/// @chord(Cm xx-10-12-13-11)            frets 10-15: a '-' each side of a two-digit fret (2026-09-28)
/// @chord                               bare: the name is derived from the notes it is on
/// </code>
/// Word 1 is a chord symbol (<see cref="ChordStructure.TryParseChordEntry"/>) or a shape (it
/// starts with <c>x</c>, <c>o</c> or a digit). The words after a symbol are shapes, each
/// optionally preceded by a tuning word — the same grammar a <c>chords</c> row's
/// <c>F(133211 2010)</c> has (<see cref="ChordShapes.ParseWords"/>).
/// </para>
/// <para>
/// A diagram draws only where a shape is written (owner's decision 2026-09-28): <c>@chord(Cm7)</c>
/// names and draws none — save in a <c>chordDiagrams … all</c> score, where every name (a bare
/// <c>@chord</c>'s derived one too) draws its written shape, else the default. The TUNING it draws on is the score's and the part's
/// (<see cref="ChordDiagramsKey.Resolve"/>: <c>layout { chordDiagrams T }</c>, else the part's
/// fretted instrument, else the guitar; <c>none</c> draws nothing, so one source makes a piano
/// score and a guitar score).
/// </para>
/// <para>
/// ⚠️ <b>A BREAKING CHANGE, accepted by the owner (2026-09-27)</b>: until then the runs were
/// concatenated, so <c>@chord(C 7)</c> named C7. It now names C with a one-character shape,
/// which no tuning has (a warning). No book wrote a spaced symbol (measured: 0 over the repo's
/// <c>.lys</c> and the Lab corpora).
/// </para>
/// LILYSHARP-OWN: LilyPond names a chord in a ChordNames context and draws a diagram in a
/// FretBoards context; nothing in it binds a shape to a chord symbol.
/// </remarks>
public sealed record ChordAnnotation
{
    /// <summary>Word 1 when it is a chord symbol (as written), else null.</summary>
    public string? Symbol { get; init; }

    /// <summary>The chord <see cref="Symbol"/> names, or null (no symbol, or one that does
    /// not parse — the validator's unknown-annotation message covers that).</summary>
    public ChordStructure? Structure { get; init; }

    /// <summary>Word 1 when it is quoted free text (<c>@chord("N.C.")</c>), quotes removed.</summary>
    public string? QuotedText { get; init; }

    /// <summary>The shapes written, in source order (those with a problem left out).</summary>
    public ImmutableArray<WrittenShape> Shapes { get; init; } = [];

    /// <summary>What is wrong with the words, by word index into the argument (LYS1038).
    /// A shape with a problem is not used; the name still draws.</summary>
    public ImmutableArray<ChordShapes.Problem> Problems { get; init; } = [];

    /// <summary>No argument at all: the bare <c>@chord</c>, named from its notes.</summary>
    public bool IsBare { get; init; }

    /// <summary>A shape with no symbol: the name comes from the shape's notes.</summary>
    public bool NamesFromDiagram => Symbol == null && QuotedText == null && !Shapes.IsEmpty;

    /// <summary>The words came from the <c>chord(…)</c> item a bare <c>@chord</c> is attached
    /// to, not from the mark (<see cref="Of"/>): there is nothing of the mark's own to edit or
    /// to point a diagnostic at.</summary>
    public bool FromShapeItem { get; init; }

    /// <summary>Reads the words of <paramref name="mark"/>, or null when it is not an
    /// <c>@chord</c>.</summary>
    /// <remarks>
    /// ⚠️ A BARE <c>@chord</c> ON A <c>chord(…)</c> ITEM reads THE ITEM'S words (owner's decision
    /// 2026-09-28, HANDOFF §2 K5): <c>chord(Cm7 x3x546)2@chord</c> names Cm7 and draws x3x546,
    /// exactly as <c>@chord(Cm7 x3x546)</c> would — so every reader of an <c>@chord</c> (the page,
    /// the MusicXML harmony, the twin, the hover) treats it alike with no second path. A symbol-
    /// less item (<c>chord(x32010)</c>) names from its shape's notes, which are the item's notes.
    /// The words' problems are left off (<see cref="FromShapeItem"/>): the item's own validator
    /// reports them at the item's words, once.
    /// </remarks>
    public static ChordAnnotation? Of(MusicMarkSyntax mark)
    {
        if (!string.Equals(mark.Name, "chord", System.StringComparison.Ordinal))
            return null;
        var words = mark.Arguments;
        if (words.IsEmpty && mark.Parent is ChordSyntax { IsShapeChord: true } item)
        {
            var itemWords = ShapeChords.Words(item);
            if (!itemWords.IsBare)
                return itemWords with { Problems = [], FromShapeItem = true };
        }
        return Parse(words.Select(a => a.Text).ToList());
    }

    /// <summary>The source span of each argument word — the runs <see cref="MarkArgument"/>
    /// reads: adjacent tokens form one word, whitespace and ',' separate them. Index i is the
    /// word <see cref="ChordShapes.Problem.WordIndex"/> i names.</summary>
    public static List<(int Start, int End)> WordSpans(MusicMarkSyntax mark)
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

    /// <summary>Reads a list of words (the runs of the argument).</summary>
    public static ChordAnnotation Parse(IReadOnlyList<string> words)
    {
        if (words.Count == 0)
            return new ChordAnnotation { IsBare = true };

        string first = words[0];
        // Quoted free text prints verbatim, as it always has; nothing may follow it.
        if (StringLiteral.IsQuoted(first))
        {
            return new ChordAnnotation
            {
                QuotedText = StringLiteral.IsClosed(first) ? StringLiteral.Value(first) : null,
                Problems = words.Count > 1
                    ? [new ChordShapes.Problem(1, ExtraWords(words[1]))]
                    : [],
            };
        }

        // A shape first: shapes alone, the name derived from their notes. A shape written in
        // upper case (X32010, X-3-5-5-4-3 — no chord root is X or O) reads the same way, so
        // ParseWords names the case fix rather than the symbol reader calling it unknown.
        if (ChordShapes.StartsShape(first) || ChordShapes.CaseCorrected(first) != null)
        {
            var problems = ChordShapes.ParseWords(words, null, out var alone);
            return new ChordAnnotation { Shapes = alone, Problems = [.. problems] };
        }

        // A chord symbol, then its shapes.
        var after = ChordShapes.ParseWords([.. words.Skip(1)], first, out var shapes);
        return new ChordAnnotation
        {
            Symbol = first,
            Structure = ChordStructure.TryParseChordEntry(first, out var parsed) ? parsed : null,
            Shapes = shapes,
            Problems = [.. after.Select(p => p with { WordIndex = p.WordIndex + 1 })],
        };
    }

    /// <summary>The shape this annotation's diagram draws on <paramref name="diagramTuning"/> —
    /// the one written for it; else the one the score's layout table lists for its chord
    /// (<paramref name="table"/>, in <paramref name="section"/>); in a <c>chordDiagrams … all</c>
    /// score (<paramref name="all"/>) else the default of its chord — or null: elsewhere a name
    /// alone draws no diagram (<see cref="ChordShapes.Drawn"/>, owner's decisions 2026-09-28).</summary>
    /// <param name="diagramTuning">The resolved tuning.</param>
    /// <param name="all">The score draws every chord name.</param>
    /// <param name="derived">The chord a name-less form names — a bare <c>@chord</c>'s, from its
    /// notes; a symbol-less shape's, from its frets — which the page knows and the words do not.
    /// Ignored when the annotation writes a symbol (<see cref="Structure"/> is its chord).</param>
    /// <param name="table">The score's layout shape table, or null.</param>
    /// <param name="section">The section the mark is written in, or null.</param>
    /// <param name="capo">The fret the score's capo is on, 0 for none.</param>
    public ChosenShape? Drawn(Syntax.TuningType diagramTuning, bool all = false, ChordStructure? derived = null,
        ChordShapeTable? table = null, string? section = null, int capo = 0)
        => ChordShapes.Drawn(diagramTuning, Shapes, all, Symbol != null ? Structure : derived, table, section, capo);

    /// <summary>
    /// The tuning a symbol-less shape (<c>@chord(x32010)</c>) is NAMED on — the score's
    /// diagram tuning when the shape fits it, else the part's (<paramref name="partTuning"/>)
    /// — and the shape; null when the shape fits neither.
    /// </summary>
    public (IReadOnlyList<int> Tuning, ImmutableArray<int> Frets)? NamingShape(
        Syntax.TuningType? diagramTuning, IReadOnlyList<int> partTuning)
    {
        if (!NamesFromDiagram)
            return null;
        if (diagramTuning is { } d && ChordShapes.WrittenFor(d, Shapes) is { } onDiagram)
            return (Tablature.Tunings.GetTuning(d), ChordShapes.Frets(onDiagram));
        foreach (var s in Shapes)
            if (s.TuningName == null && ChordShapes.StringCount(s.Shape) == partTuning.Count)
                return (partTuning, ChordShapes.Frets(s.Shape));
        return null;
    }

    /// <summary>
    /// The tuning of the part <paramref name="node"/> is written in (its <c>tuning</c>, else
    /// its preset's, else the guitar — <see cref="PartHeaderDefaults.Tuning"/>), found from the
    /// SYNTAX: the enclosing <c>part</c> declaration, or the part a section's block names. Null
    /// when the node is outside every part (a phrase, a top-level block) and the parts of the
    /// file are not all tuned alike — then only the page, which knows which part plays it,
    /// can tell.
    /// </summary>
    /// <remarks>
    /// What a symbol-less shape (<c>@chord(x32010)</c>) is NAMED on when the score draws no
    /// diagrams (<see cref="NamingShape"/>). The page does not ask this: it knows the part it
    /// is collecting. The validator, the twin and the editor's hover, which read the tree, do.
    /// </remarks>
    public static IReadOnlyList<int>? PartTuningOf(SyntaxNode node)
    {
        SyntaxNode root = node;
        while (root.Parent != null)
            root = root.Parent;
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            if (p is PartDeclarationSyntax declared)
                return Tablature.Tunings.GetTuning(PartHeaderDefaults.Read(declared).Tuning);
            if (p is PartBlockSyntax block)
                return Tablature.Tunings.GetTuning(
                    PartHeaderDefaults.Read(ConcertPitch.FindPart(root, block.Name)).Tuning);
        }
        var tunings = root.ChildNodes().OfType<PartDeclarationSyntax>()
            .Select(pd => PartHeaderDefaults.Read(pd).Tuning).Distinct().ToList();
        return tunings.Count switch
        {
            0 => Tablature.Tunings.Guitar,
            1 => Tablature.Tunings.GetTuning(tunings[0]),
            _ => null,
        };
    }

    /// <summary>
    /// The chord a written-out diagram sounds, named by the recognizer a bare <c>@chord</c>
    /// uses on notes (<see cref="ChordStructure.TryRecognize(IReadOnlyList{ValueTuple{int, int}}, out ChordStructure?)"/>),
    /// its notes spelled in the key of <paramref name="keySharps"/>; null when they name none.
    /// </summary>
    /// <remarks>
    /// ⚠️ The SPELLING is Lily#'s choice (the frets say pitch, not letter): a pitch class the key
    /// has on one of its seven letters takes that letter, any other a sharp in a sharp key or
    /// C major and a flat in a flat key. It decides only how the name PRINTS (C♯ or D♭); which
    /// chord is named depends on the pitch classes alone.
    /// </remarks>
    public static ChordStructure? NameFromFrets(
        IReadOnlyList<int> frets, IReadOnlyList<int> tuning, int keySharps)
    {
        var pitches = new List<int>();
        for (int i = 0; i < frets.Count && i < tuning.Count; i++)
            if (frets[i] >= 0)
                pitches.Add(tuning[i] + frets[i]);
        if (pitches.Count == 0)
            return null;
        pitches.Sort();
        var members = pitches.Select(p => SpellPitchClass(((p % 12) + 12) % 12, keySharps)).ToList();
        return ChordStructure.TryRecognize(members, out var structure) ? structure : null;
    }

    /// <summary>A pitch class as (letter step, alteration) in the key of <paramref name="keySharps"/>.</summary>
    internal static (int Step, int Alter) SpellPitchClass(int pc, int keySharps)
    {
        for (int step = 0; step < 7; step++)
        {
            int alter = KeySpelling.Alteration(step, keySharps);
            if ((((RelativeOctave.StepSemitoneOf(step) + alter) % 12) + 12) % 12 == pc)
                return (step, alter);
        }
        bool flats = keySharps < 0;
        for (int step = 0; step < 7; step++)
        {
            int natural = RelativeOctave.StepSemitoneOf(step);
            if (!flats && (natural + 1) % 12 == pc)
                return (step, 1);
            if (flats && ((natural - 1) % 12 + 12) % 12 == pc)
                return (step, -1);
        }
        return (0, 0);   // unreachable: every pitch class is a natural or a neighbour's sharp/flat
    }

    // ---------------------------------------------------------------- the messages
    // Each names the fix. Shared by the validator (which reports them) and the tests.

    internal static string ExtraWords(string w)
        => $"'{w}' is not understood after quoted text - a quoted @chord prints its text and "
           + "takes no shape.";

    // Owner (2026-09-28): the fix is either a name or the nameless diagram — the old
    // "@chord(C …)" put a made-up C in front of any shape.
    internal static string NoDerivedName(string written)
        => $"the notes of '{written}' name no chord Lily# knows, so no chord name is drawn - "
           + $"write the chord name first (@chord(NAME {written})), or use @diagram({written}) "
           + "for a diagram with no name.";
}
