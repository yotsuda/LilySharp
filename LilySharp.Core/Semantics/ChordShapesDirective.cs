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
using System.Globalization;
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The top-level <c>capo N</c> line: the fret the file's capo is on, 1 to <see cref="MaxFret"/>
/// — what every PRESSED shape in the file is relative to (the shapes written in the music, the
/// <see cref="ChordShapesDirective">chordShapes</see> table), what the chord names print under
/// (<see cref="ChordNamesKey"/>), and what a <c>diagram(Eb x32010)</c> item sounds.
/// </summary>
/// <remarks>
/// <para>
/// Owner's grammar 2026-10-31 (docs/ROADMAP.md "Chord diagrams: one key doing four jobs" §4,
/// Q2): the piece's DATA, a line beside <c>time</c> and <c>key</c>, written once — not a
/// <c>layout</c> key, because the capo changes what an item SOUNDS and a sound must not change
/// with the edition; not a part property, because the shape table's shapes are pressed shapes
/// (the capo must live at the table's level) and the commonest capo chart is a chords row, which
/// belongs to no part. A per-part capo may be added LATER as an override of the file's. Until
/// 2026-10-31 the capo was the layout's <c>chordDiagrams … capo N</c> (built 2026-09-29).
/// </para>
/// <para>
/// The value reaches every reader through <see cref="ChordSpelling.Capo"/> on the resolved
/// <see cref="LayoutPlan"/> (<see cref="ChordShapesDirective.Apply"/>): the one value every
/// chord namer is handed, as before — so the readers of the capo (the page, the twin, MusicXML,
/// the MIDI, the editor) did not move; only its home in the SOURCE did. The "Capo N" line at the
/// score's head prints only where the score prints diagrams or pressed names
/// (<see cref="LayoutPlan.PrintsCapoLine"/>).
/// </para>
/// <para>
/// LILYSHARP-OWN: LilyPond 2.26 has no capo property; its <c>\fret-diagram-verbose</c> knows a
/// <c>capo</c> bar inside one diagram only.
/// </para>
/// </remarks>
public static class CapoDirective
{
    /// <summary>The word that opens the line — at the top level only, and not a keyword (as
    /// <c>check</c>): a part may still be named <c>capo</c>.</summary>
    public const string Keyword = "capo";

    /// <summary>The highest fret a capo is written on (a guitar's capo range); 0 is no capo
    /// and is refused — leave the line out.</summary>
    public const int MaxFret = 11;

    /// <summary>The file's <c>capo</c> lines, in document order.</summary>
    public static IEnumerable<PropertyAssignmentSyntax> Lines(SyntaxNode root)
    {
        foreach (var node in root.ChildNodes())
            if (node is PropertyAssignmentSyntax { NameToken.Text: Keyword } line)
                yield return line;
    }

    /// <summary>The fret the file's capo is on — the LAST <c>capo</c> line's, as every repeated
    /// single-value setting — or 0 for none (or a line the reader refuses).</summary>
    public static int CapoOf(SyntaxNode root)
    {
        int capo = 0;
        foreach (var line in Lines(root))
            if (Read(line, out _) is { } fret)
                capo = fret;
        return capo;
    }

    /// <summary>
    /// Reads one <c>capo</c> line: its fret (1 to <see cref="MaxFret"/>), or null with
    /// <paramref name="problem"/> saying what is wrong (LYS1058) — nothing after the word, a word
    /// or a sign, 0, a fret above the range, or a second value.
    /// </summary>
    internal static int? Read(PropertyAssignmentSyntax line, out LayoutPlanReader.Problem? problem)
    {
        problem = null;
        var values = line.Values.OfType<SyntaxTokenNode>().ToList();
        string takes = $"'{Keyword}' takes the fret the capo is on, 1 to {MaxFret}: '{Keyword} 3'";
        if (values.Count == 0)
        {
            problem = new LayoutPlanReader.Problem(line.NameToken.Span, DiagnosticCodes.CapoDirectiveBadValue,
                takes + ".", IsError: true);
            return null;
        }
        var first = values[0];
        if (first.Kind != SyntaxKind.IntegerLiteral
            || !int.TryParse(first.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int fret)
            || fret > MaxFret)
        {
            problem = new LayoutPlanReader.Problem(first.Span, DiagnosticCodes.CapoDirectiveBadValue,
                $"'{first.Text}' is not a capo fret. " + takes + ".", IsError: true);
            return null;
        }
        if (fret == 0)
        {
            problem = new LayoutPlanReader.Problem(first.Span, DiagnosticCodes.CapoDirectiveBadValue,
                $"'{Keyword} 0' is no capo - leave the line out.", IsError: true);
            return null;
        }
        if (values.Count > 1)
        {
            problem = new LayoutPlanReader.Problem(values[1].Span, DiagnosticCodes.CapoDirectiveBadValue,
                takes + $" - one fret; '{values[1].Text}' is extra.", IsError: true);
            return null;
        }
        return fret;
    }
}

/// <summary>
/// The top-level <c>chordShapes { … }</c> block — the file's chord-shape table
/// (<see cref="ChordShapeTable"/>): <c>chordShapes { Cm7 x35343  G  section Chorus { C x35553 } }</c>,
/// the chords that draw a diagram wherever they are named, with the shape each draws; written
/// once, the piece's DATA (owner's grammar 2026-10-31, docs/ROADMAP.md "Chord diagrams: one key
/// doing four jobs" §4, Q1). Until then the table followed the layout's <c>chordDiagrams</c>
/// words (built 2026-09-29, HANDOFF §2 K5 ③) — which shape a chord takes belongs to the piece,
/// not to one edition of it, and a named <c>layout lp { }</c> had to copy the table.
/// </summary>
/// <remarks>
/// <para>
/// The words are the block's tokens re-joined by ADJACENCY (a chords row's rule): <c>C#m7-5/G</c>
/// and <c>8xx88-11</c> are one word each; a brace is always its own. A word is a chord symbol
/// unless it starts a shape (<see cref="ChordShapes.StartsShape"/>) or names a tuning — the
/// words after a symbol are its shapes, read by <see cref="ChordShapes.ParseWords"/>. The section
/// names are wrapped in the keyword so a section named <c>A</c> or <c>C</c> cannot be read as a
/// chord. A section no <c>section NAME { }</c> declares warns (its entries apply nowhere); a chord
/// listed twice in one scope warns, and the last one wins (a key's rule). Each shape is checked
/// against its chord (LYS1039) on the tunings a diagram of this file can draw on: the guitar,
/// every fretted instrument the parts play, and every <c>chordTuning</c> word a layout block
/// writes. A table shape is the PRESSED shape: under the file's <see cref="CapoDirective">capo</see>
/// it is raised by the capo and checked against the sounding chord the entry names.
/// </para>
/// <para>
/// ONE HOME FOR THE READING, the layout block's contract: the collector wants the table and no
/// diagnostics, the validator (<see cref="ChordShapesValidator"/>) the diagnostics and no table,
/// and the twin, MusicXML and the editor the table the page resolved to — all through
/// <see cref="Apply"/>, which lays the file's capo and table on a <see cref="LayoutPlan"/> the way
/// the page reads them (<see cref="LayoutPlanReader.Resolve"/>).
/// </para>
/// </remarks>
public static class ChordShapesDirective
{
    /// <summary>The word that opens the block — at the top level only, and not a keyword.</summary>
    public const string Keyword = "chordShapes";

    /// <summary>The word that opens a section's entries inside the table.</summary>
    public const string SectionWord = "section";

    /// <summary>The file's <c>chordShapes</c> declarations, in document order.</summary>
    public static IEnumerable<ChordShapesDeclarationSyntax> Declarations(SyntaxNode root)
        => TopLevelNodes.OfRoot<ChordShapesDeclarationSyntax>(root);

    /// <summary>The file's shape table — the LAST <c>chordShapes</c> block's, as every repeated
    /// single-value setting — or null for none (or a block the reader refuses as a whole).</summary>
    public static ChordShapeTable? TableOf(SyntaxNode root)
    {
        ChordShapeTable? table = null;
        foreach (var declaration in Declarations(root))
            if (declaration.IsBlock && Read(declaration, out _) is { } read)
                table = read;
        return table;
    }

    /// <summary>
    /// <paramref name="plan"/> with the FILE's capo and shape table laid on it — the two values
    /// the plan carries for every reader (<see cref="ChordSpelling.Capo"/>,
    /// <see cref="LayoutPlan.ChordDiagramTable"/>), which no <c>layout { }</c> entry sets since
    /// 2026-10-31. Every resolution of a score's plan ends here
    /// (<see cref="LayoutPlanReader.Resolve"/>, the collector, MusicXML).
    /// </summary>
    internal static LayoutPlan Apply(SyntaxNode root, LayoutPlan plan)
    {
        int capo = CapoDirective.CapoOf(root);
        var table = TableOf(root);
        if (capo == plan.Chords.Capo && Equals(table, plan.ChordDiagramTable))
            return plan;
        return plan with { ChordDiagramTable = table, Chords = plan.Chords with { Capo = capo } };
    }

    /// <summary>
    /// Reads a <c>chordShapes</c> block into its <see cref="ChordShapeTable"/>: entries
    /// <c>SYMBOL [TUNINGWORD] SHAPE …</c> (a name alone: the usual shape), and
    /// <c>section NAME { entries }</c> blocks. Null when the table is refused as a whole (a
    /// structural error, LYS1060: a stray brace, a <c>section</c> with no name or block, tokens
    /// after the closer); an entry with a problem is left out with a warning and the rest stand,
    /// the way a row's written shapes do (LYS1038).
    /// </summary>
    /// <param name="declaration">The block.</param>
    /// <param name="problems">Everything wrong with it, in source order.</param>
    internal static ChordShapeTable? Read(ChordShapesDeclarationSyntax declaration,
        out IReadOnlyList<LayoutPlanReader.Problem> problems)
    {
        var found = new List<LayoutPlanReader.Problem>();
        problems = found;
        if (!declaration.IsBlock)
            return null;   // refused by the parser (LYS1059), kept so no position slides
        SyntaxNode root = declaration;
        while (root.Parent != null)
            root = root.Parent;
        return ReadTable(declaration.Tokens, 0, root, CapoDirective.CapoOf(root), found);
    }

    /// <summary>
    /// The table reader proper, over the tokens from the <c>{</c> at <paramref name="open"/> to
    /// its closer. Shared with the OLD <c>chordDiagrams … { }</c> form's reader only for its
    /// problems (the old form is refused whole, LYS1061).
    /// </summary>
    internal static ChordShapeTable? ReadTable(IReadOnlyList<SyntaxTokenNode> values, int open,
        SyntaxNode root, int capo, List<LayoutPlanReader.Problem> found)
    {
        bool refused = false;
        void Error(TextSpan span, string message)
        {
            found.Add(new LayoutPlanReader.Problem(span, DiagnosticCodes.ChordShapesTableBroken, message, IsError: true));
            refused = true;
        }
        void Warn(TextSpan span, string code, string message)
            => found.Add(new LayoutPlanReader.Problem(span, code, message, IsError: false));

        var words = TableWords(values, open + 1);
        var song = ImmutableArray.CreateBuilder<ChordShapeEntry>();
        var sections = ImmutableArray.CreateBuilder<(string Section, ImmutableArray<ChordShapeEntry> Entries)>();
        var sectionSpans = new List<(string Name, TextSpan Span)>();
        var scope = song;
        string? sectionName = null;

        // The entry being read: its symbol (null between entries, or while the words of a
        // symbol that did not parse are skipped), and the words after it.
        string? symbol = null;
        TextSpan symbolSpan = default;
        ChordStructure? chord = null;
        bool skipping = false;
        var entryWords = new List<(string Text, TextSpan Span)>();
        List<TuningType>? checkTunings = null;

        void Flush()
        {
            if (symbol == null || chord == null)
            {
                entryWords.Clear();
                return;
            }
            var entryProblems = ChordShapes.ParseWords([.. entryWords.Select(w => w.Text)], symbol, out var shapes);
            foreach (var p in entryProblems)
                Warn(entryWords[p.WordIndex].Span, DiagnosticCodes.ChordDiagramNotDrawn, p.Message);
            checkTunings ??= CheckTunings(root);
            // A table shape is the PRESSED shape: under a capo it is raised by the capo
            // (ChordShapes.AtCapo) and checked against the sounding chord the entry names, so the
            // message speaks sounding names; the pressed chord (ChordStructure.Pressed) only
            // names the fix that keeps the symbol.
            var pressed = chord.Pressed(capo, 0);
            foreach (var tuning in checkTunings)
            {
                if (ChordShapes.WrittenFor(tuning, shapes) is not { } shape
                    || ChordShapes.Mismatch(ChordShapes.AtCapo(ChordShapes.Frets(shape), capo),
                        Tablature.Tunings.GetTuning(tuning), chord) is not { } mismatch)
                    continue;
                string? tuningName = shapes.First(s => s.Shape == shape).TuningName;
                string? pressedShape = capo > 0 ? ChordShapes.Default(tuning, pressed)?.Spelled : null;
                string message = ChordShapes.MismatchMessage(mismatch, shape, tuningName, symbol, inRow: false, inTable: true,
                    capo: capo, pressedShape: pressedShape);
                var at = entryWords.First(w => w.Text == shape).Span;
                if (!found.Any(f => f.Span.Start == at.Start && f.Message == message))
                    Warn(at, DiagnosticCodes.ChordShapeMismatch, message);
            }
            if (scope.Any(e => ChordShapeTable.SameChord(e.Chord, chord)))
                Warn(symbolSpan, DiagnosticCodes.ChordShapesTableBroken,
                    $"'{symbol}' is listed twice in this table; the last one wins.");
            scope.Add(new ChordShapeEntry(symbol, chord, shapes));
            symbol = null;
            chord = null;
            entryWords.Clear();
        }

        int i = 0, depth = 1;
        while (i < words.Count && depth > 0)
        {
            var w = words[i];
            if (w.Text == "}")
            {
                Flush();
                skipping = false;
                depth--;
                if (depth == 1 && sectionName != null)
                {
                    sections.Add((sectionName, scope.ToImmutable()));
                    sectionName = null;
                    scope = song;
                }
                i++;
                continue;
            }
            if (w.Text == "{")
            {
                Error(w.Span, "'{' here opens nothing - a shape table lists chord names with their shapes "
                    + "(Cm7 x35343), and only 'section NAME { ... }' opens a block inside it.");
                return null;
            }
            if (w.Text == SectionWord)
            {
                if (depth > 1)
                {
                    Error(w.Span, "a section's table holds no 'section' of its own - close this one "
                        + "with '}' first.");
                    return null;
                }
                Flush();
                skipping = false;
                if (i + 2 >= words.Count || words[i + 1].Text is "{" or "}" || words[i + 2].Text != "{")
                {
                    Error(w.Span, "'section' takes the section's name and a block of entries: "
                        + "section Chorus { C x35553 }.");
                    return null;
                }
                sectionName = words[i + 1].Text;
                sectionSpans.Add((sectionName, words[i + 1].Span));
                scope = ImmutableArray.CreateBuilder<ChordShapeEntry>();
                depth = 2;
                i += 3;
                continue;
            }
            bool shapeOrTuning = ChordShapes.StartsShape(w.Text)
                || Tablature.Tunings.Names.Contains(w.Text)
                || ChordShapes.CaseCorrected(w.Text) != null;
            if (shapeOrTuning)
            {
                if (symbol != null)
                    entryWords.Add((w.Text, w.Span));
                else if (!skipping)
                    Warn(w.Span, DiagnosticCodes.ChordDiagramNotDrawn,
                        $"'{w.Text}' comes before any chord name - a table entry is the chord's name, "
                        + "then its shapes: Cm7 x35343. It is not used.");
                i++;
                continue;
            }
            // A chord symbol: the entry before it is complete.
            Flush();
            if (ChordStructure.TryParseChordEntry(w.Text, out var parsed))
            {
                symbol = w.Text;
                symbolSpan = w.Span;
                chord = parsed;
                skipping = false;
            }
            else
            {
                Warn(w.Span, DiagnosticCodes.ChordDiagramNotDrawn,
                    $"'{w.Text}' is not a chord symbol (Cm7, F#m7-5/A) - a table entry is the chord's "
                    + "name, then its shapes: Cm7 x35343. It is not used.");
                skipping = true;
            }
            i++;
        }
        Flush();
        if (depth > 0)
        {
            Error(values[open].Span, $"This '{Keyword}' table has no closing '}}'.");
            return null;
        }
        if (i < words.Count)
        {
            Error(words[i].Span, $"'{words[i].Text}' comes after the shape table's closing '}}' - the table "
                + $"ends the '{Keyword}' block.");
            return null;
        }

        // A section no `section NAME { }` declares: its entries apply nowhere.
        if (sectionSpans.Count > 0)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in root.KindSites(SyntaxKind.SectionDeclaration))
                if (node is SectionDeclarationSyntax declared)
                    known.Add(declared.SectionName);
            foreach (var (name, span) in sectionSpans)
                if (!known.Contains(name))
                    Warn(span, DiagnosticCodes.ChordShapesTableBroken,
                        $"No section is named '{name}', so its entries apply nowhere."
                        + (known.Count > 0 ? " Sections: " + string.Join(", ", known.OrderBy(k => k, StringComparer.Ordinal)) + "." : ""));
        }
        return refused ? null : new ChordShapeTable(song.ToImmutable(), sections.ToImmutable());
    }

    /// <summary>The tokens from <paramref name="from"/> on, re-joined by ADJACENCY into words
    /// (a chords row's rule: <c>C#m7-5/G</c>, <c>8xx88-11</c> are one word each); a brace is
    /// always a word of its own.</summary>
    internal static List<(string Text, TextSpan Span)> TableWords(IReadOnlyList<SyntaxTokenNode> values, int from)
    {
        var words = new List<(string Text, TextSpan Span)>();
        var sb = new System.Text.StringBuilder();
        int start = -1, end = -1;
        void Flush()
        {
            if (sb.Length > 0)
                words.Add((sb.ToString(), new TextSpan(start, end - start)));
            sb.Clear();
            start = -1;
        }
        for (int i = from; i < values.Count; i++)
        {
            var t = values[i];
            if (t.Kind is SyntaxKind.OpenBrace or SyntaxKind.CloseBrace)
            {
                Flush();
                words.Add((t.Text, t.Span));
                continue;
            }
            if (t.Text.Length == 0)
                continue;
            if (sb.Length > 0 && t.Span.Start != end)
                Flush();
            if (start < 0)
                start = t.Span.Start;
            sb.Append(t.Text);
            end = t.Span.Start + t.Text.Length;
        }
        Flush();
        return words;
    }

    /// <summary>The tunings a table's shapes are checked on (LYS1039): the guitar, every fretted
    /// instrument the file's parts play, and every <c>chordTuning</c> word a layout block of the
    /// file writes — the tunings a diagram of this file can draw on
    /// (<see cref="ChordDiagramsKey.Resolve"/>).</summary>
    private static List<TuningType> CheckTunings(SyntaxNode root)
    {
        var tunings = new List<TuningType> { TuningType.Guitar };
        foreach (var part in TopLevelNodes.OfRoot<PartDeclarationSyntax>(root))
            if (PartHeaderDefaults.Read(part).FrettedTuning is { } fretted && !tunings.Contains(fretted))
                tunings.Add(fretted);
        foreach (var layout in TopLevelNodes.OfRootOrScore<LayoutDeclarationSyntax>(root))
            if (LayoutPlanReader.Read(layout, out _).ChordTuning is { } word
                && Tablature.Tunings.Parse(word) is var t && !tunings.Contains(t))
                tunings.Add(t);
        return tunings;
    }
}

/// <summary>
/// Reports what is wrong with the file's <c>capo</c> lines (LYS1058; a second one LYS4005) and
/// its <c>chordShapes</c> blocks (LYS1059 / LYS1060 for the block, LYS1038 / LYS1039 for an
/// entry; a second block LYS4005). The reading itself lives in <see cref="CapoDirective"/> and
/// <see cref="ChordShapesDirective"/>, shared with the collector, so the two cannot disagree
/// about what is legal — the <see cref="LayoutValidator"/>'s contract.
/// </summary>
internal sealed class ChordShapesValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var capos = CapoDirective.Lines(root).ToList();
        foreach (var line in capos)
            if (CapoDirective.Read(line, out var problem) == null && problem is { } p)
                _diagnostics.Error(p.Span, p.Code, p.Message);
        // In source order the LAST wins (DuplicateGlobalSettingValidator's rule and sentence).
        for (int i = 0; i < capos.Count - 1; i++)
            _diagnostics.Warning(capos[i].Span, DiagnosticCodes.DuplicateGlobalSetting,
                $"This '{CapoDirective.Keyword}' is overwritten by a later '{CapoDirective.Keyword}'; only the last one takes effect.");

        var tables = ChordShapesDirective.Declarations(root).ToList();
        foreach (var table in tables)
        {
            ChordShapesDirective.Read(table, out var problems);
            foreach (var p in problems)
            {
                if (p.IsError)
                    _diagnostics.Error(p.Span, p.Code, p.Message);
                else
                    _diagnostics.Warning(p.Span, p.Code, p.Message);
            }
        }
        for (int i = 0; i < tables.Count - 1; i++)
            _diagnostics.Warning(tables[i].KeywordToken.Span, DiagnosticCodes.DuplicateGlobalSetting,
                $"This '{ChordShapesDirective.Keyword}' is overwritten by a later '{ChordShapesDirective.Keyword}'; only the last one takes effect.");
    }
}
