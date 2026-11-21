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
using System.Linq;
using System.Threading;
using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns at a note <c>@cross</c> moved to the other staff where, on the laid-out page, it runs
/// into that staff's own notes (<see cref="DiagnosticCodes.CrossStaffRunsIntoOtherStaff"/>).
/// </summary>
/// <remarks>
/// Owner's decision 2026-11-13, on the owner's own bar: a left hand writing
/// <c>d8 a d' fis'@cross a'@cross fis'@cross d' a</c> under a right hand's
/// <c>&lt;fis' a' fis''&gt;4 e''8 d'' a'2</c> draws the moved fis' through the right hand's eighth
/// beam and the moved a' against its half note. MEASURED, LilyPond 2.26.0 (Lab
/// sessions/p1226/md/readme/x): the same picture — a cross-staff note's collisions are the
/// writer's to avoid, and nothing is moved for one. So the page keeps drawing what is written,
/// and this says so.
/// LILYSHARP-OWN: LilyPond says nothing here.
/// <para>
/// WHERE IT IS KNOWN: only on the page. The score is collected and laid out on the road every
/// picture takes (<see cref="SvgGenerator.CollectAndLayout"/>, the file's own paper) and drawn
/// into the boxes backend, which paints nothing and records each symbol's ink box
/// (<see cref="BoxesDocumentContext"/>) — so what is compared is the ink the SVG and the PNG
/// show, not a second opinion of it. A <c>--set</c> of the command line is not known here: a
/// page laid out under other settings can differ.
/// </para>
/// <para>
/// ⚠️ A PAGE CHECK, NOT ONE OF <see cref="SemanticValidation.CreateAll"/>: it lays the score
/// out, and <see cref="SemanticValidation.Run(SyntaxTree)"/> is the pass the language server
/// runs after every settled keystroke. MEASURED 2026-11-13 (Debug <c>lysc check</c>, a
/// four-page song with 129 moved notes): 2449 ms with this in that pass against 739 ms
/// without. It runs from <see cref="SemanticValidation.RunPageChecks"/> — once a
/// <c>lysc check</c>, and in the editor after the text has rested. A file with no
/// <c>@cross</c> is not collected at all, and a score of it that moves no note (a part sheet,
/// one staff) is collected and not laid out. <see cref="Layouts"/> counts the layouts.
/// </para>
/// <para>
/// WHAT COUNTS. Of the moved note: its heads, its stem, its accidentals. Of the staff it moved
/// TO: the noteheads, stems, beams, flags, rests, dots and accidentals the renderer drew for
/// that staff's own voices (<see cref="BoxSymbol.Staff"/> is the VOICE's staff, so the moved
/// note's own voice — its stem, the cross-staff beam it hangs on, its neighbours — is never the
/// other staff's ink, and neither is a note the other staff sent away with an <c>@cross</c> of
/// its own). Staff lines, ledger lines, slurs, ties and every mark outside the staves are not
/// asked; a grace note that moved is not judged.
/// </para>
/// <para>
/// THE TOLERANCE (<see cref="Depth"/>, <see cref="Touch"/>): two inks collide when their boxes
/// share more than 0.1 staff space of height and stand side by side with less than 0.02 between
/// them. The height keeps a hairline that merely touches silent (a stem ending 0.05 from a
/// beam, two heads a third apart in one column, 0.09). The width is not asked for depth: a
/// moved head set against another voice's head or stem (the a' above, 0.0014 from the half
/// note) reads as one chord with it, which is the collision the owner named. A beam is asked by
/// its parallelogram, not its box (<see cref="BoxesDocumentContext.QuadOf"/>).
/// </para>
/// <para>
/// FOLDED (owner-side review 2026-11-13: a song whose hands interleave on one staff throughout
/// gave 105 warnings for 129 moved notes, which buries the Problems panel). ONE WARNING A BAR
/// OF A PART, at the bar's first colliding note, saying how many of the bar's moved notes
/// collide and with what kinds of ink (a single note keeps its own sentence); and a part with
/// more than <see cref="BarsBeforeOne"/> such bars in a score gets ONE, at the first, that
/// counts the others. A bar a repeat plays twice is one bar.
/// </para>
/// </remarks>
internal sealed class CrossStaffCollisionValidator : ISemanticValidator
{
    /// <summary>The height two ink boxes must share before they are said to collide, in staff
    /// spaces.</summary>
    internal const double Depth = 0.1;

    /// <summary>How far apart side by side two inks may stand and still be said to touch.</summary>
    internal const double Touch = 0.02;

    /// <summary>How many bars of one part may each carry a warning; with more, the part has
    /// one warning, at the first of them.</summary>
    internal const int BarsBeforeOne = 5;

    /// <summary>How many scores this check has laid out on this thread — for the test that
    /// holds the keystroke pass to none. (Per thread: counted per process, a test that ran a page
    /// check beside it was counted too, and the test failed now and then in a full run.)</summary>
    [ThreadStatic]
    internal static int Layouts;

    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    // The kinds asked of the other staff, in the order the message names them.
    private static readonly (string Kind, string One, string Many)[] Kinds =
    [
        ("notehead", "a notehead", "noteheads"), ("rest", "a rest", "rests"), ("beam", "a beam", "beams"),
        ("stem", "a stem", "stems"), ("flag", "a flag", "flags"),
        ("accidental", "an accidental", "accidentals"), ("dot", "a dot", "dots"),
    ];

    // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
    private const string WaysOut =
        " - a cross-staff note is drawn where it is written and nothing is moved for it (LilyPond does the same). "
        + "Write it on its own staff (drop '@cross'), turn the other hand's stems away ('@stemUp' / '@stemDown'), "
        + "or change the clef for the passage.";

    /// <summary>Whether <paramref name="tree"/> writes an <c>@cross</c> at all — what the
    /// check costs a file that does not.</summary>
    internal static bool Concerns(SyntaxTree tree) => tree.Text.Contains("@cross", StringComparison.Ordinal);

    public void Validate(SyntaxTree tree) => Validate(tree, CancellationToken.None);

    /// <summary><see cref="Validate(SyntaxTree)"/>, given up between a score's collect, its
    /// layout and its drawing when <paramref name="token"/> is cancelled (a layout once begun
    /// runs to its end). Throws <see cref="OperationCanceledException"/>.</summary>
    public void Validate(SyntaxTree tree, CancellationToken token)
    {
        if (!Concerns(tree))
            return;
        // The written @cross notes, by the offset their item carries: the span to underline
        // and the note as written.
        Dictionary<int, (TextSpan Span, string Written)>? written = null;
        foreach (var mark in tree.GetRoot().DescendantNodes<ArticulationSyntax>())
        {
            if (mark.Name != "cross" || mark.Parent is not { } owner)
                continue;
            (written ??= new())[owner.SourceStart] = (owner.Span, WrittenOf(tree.Text, owner));
        }
        if (written is null)
            return;

        var reported = new HashSet<int>();
        foreach (var spec in RenderSpecParser.FindAll(tree))
        {
            token.ThrowIfCancellationRequested();
            if (!spec.IsMultiStaff)
                continue;
            try
            {
                Judge(tree, spec, written, reported, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A malformed score surfaces its real error elsewhere; add nothing.
            }
        }
    }

    private void Judge(SyntaxTree tree, RenderSpec spec,
        Dictionary<int, (TextSpan Span, string Written)> written, HashSet<int> reported, CancellationToken token)
    {
        var score = SvgGenerator.CollectScore(tree, spec);

        // Every head's offset -> its item's offset and the staff it moved to; every moved item's
        // part (its voice's staff) and bar, the first time the form plays it; and every note
        // offset of the score, moved or not (an accidental drawn under one is a note's, where a
        // key signature's is drawn under its `key`).
        var moved = new Dictionary<int, (int Item, int Target)>();
        var barOf = new Dictionary<int, (int Staff, int Bar)>();
        var notes = new HashSet<int>();
        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
            foreach (var voice in staff.Voices)
                for (int bar = 0; bar < voice.Measures.Length; bar++)
                    foreach (var item in voice.Measures[bar].Items)
                    {
                        if (item.GraceTime || item is not (NoteItem or ChordItem))
                            continue;
                        notes.Add(item.SourcePosition);
                        if (item.CrossStaffIndex >= 0)
                        {
                            moved[item.SourcePosition] = (item.SourcePosition, item.CrossStaffIndex);
                            barOf.TryAdd(item.SourcePosition, (staffIndex, bar));
                        }
                        if (item is not ChordItem chord)
                            continue;
                        foreach (var member in chord.Notes)
                        {
                            if (member.SourcePosition < 0)
                                continue;
                            notes.Add(member.SourcePosition);
                            if (item.CrossStaffIndex >= 0)
                                moved[member.SourcePosition] = (item.SourcePosition, item.CrossStaffIndex);
                        }
                    }
        if (barOf.Count == 0)
            return;

        token.ThrowIfCancellationRequested();
        Layouts++;
        var layout = new Svg.Layout.LayoutEngine(score.Paper).Layout(score);
        token.ThrowIfCancellationRequested();
        using var doc = new BoxesDocumentContext(FontLocator.Find());
        SharedRenderer.RenderTo(score, layout, doc);
        token.ThrowIfCancellationRequested();

        // Per moved note, the kinds of the other staff's ink it meets (a bit a kind).
        var met = new Dictionary<int, int>();
        var pages = doc.Pages;
        for (int p = 0; p < pages.Count; p++)
        {
            var symbols = pages[p].Symbols;
            // The moved notes' heads first: a stem is known as a moved note's by the head it
            // leaves (a stem carries no offset of its own).
            var heads = new List<(BoxSymbol Head, int Item, int Target)>();
            foreach (var s in symbols)
                if (s.Kind == "notehead" && moved.TryGetValue(s.Pos, out var m))
                    heads.Add((s, m.Item, m.Target));
            if (heads.Count == 0)
                continue;

            // What each moved note inked, and the stems that are some moved note's.
            var ink = new List<(double[] Box, int Item, int Target)>();
            var movedStems = new HashSet<int>();
            foreach (var (head, item, target) in heads)
                ink.Add((head.Box, item, target));
            for (int i = 0; i < symbols.Count; i++)
            {
                var s = symbols[i];
                if (s.Kind == "accidental" && moved.TryGetValue(s.Pos, out var m))
                    ink.Add((s.Box, m.Item, m.Target));
                else if (s.Kind == "stem")
                    foreach (var (head, item, target) in heads)
                        if (head.Staff == s.Staff && LeavesHead(s.Box, head.Box))
                        {
                            ink.Add((s.Box, item, target));
                            movedStems.Add(i);
                            break;
                        }
            }

            for (int i = 0; i < symbols.Count; i++)
            {
                var s = symbols[i];
                int rank = Array.FindIndex(Kinds, k => k.Kind == s.Kind);
                if (rank < 0 || s.Staff < 0 || movedStems.Contains(i) || moved.ContainsKey(s.Pos))
                    continue;
                if (s.Kind == "accidental" && !notes.Contains(s.Pos))
                    continue;
                double[]? quad = s.Kind == "beam" ? doc.QuadOf(p, i) : null;
                foreach (var (box, item, target) in ink)
                {
                    int bit = 1 << rank;
                    if (target != s.Staff || (met.GetValueOrDefault(item) & bit) != 0)
                        continue;
                    if (quad is not null ? MeetsQuad(box, quad) : Meets(box, s.Box))
                        met[item] = met.GetValueOrDefault(item) | bit;
                }
            }
        }
        if (met.Count == 0)
            return;

        // Folded: the colliding notes by part and bar, a part's bars in the order played.
        foreach (var part in met.Keys.GroupBy(item => barOf[item].Staff).OrderBy(g => g.Key))
        {
            var bars = part.GroupBy(item => barOf[item].Bar).OrderBy(g => g.Key)
                .Select(g => g.OrderBy(item => item).ToList()).ToList();
            bool one = bars.Count > BarsBeforeOne;
            foreach (var bar in one ? bars.Take(1) : bars)
            {
                int first = bar[0];
                if (!reported.Add(first))
                    continue;
                var (span, text) = written.TryGetValue(first, out var w) ? w : (new TextSpan(first, 1), "this note");
                var (staff, at) = barOf[first];
                int kinds = bar.Aggregate(0, (k, item) => k | met[item]);
                string head = bar.Count == 1
                    ? $"{text} is drawn on the other staff ('@cross') and runs into {Named(kinds, many: false)} of that staff's own notes"
                    : $"{bar.Count} of this bar's {barOf.Values.Count(b => b == (staff, at))} notes drawn on the other staff ('@cross') "
                      + $"run into that staff's own notes ({Named(kinds, many: true)}), {text} first";
                if (one)
                {
                    int others = bars.Skip(1).SelectMany(b => b).Aggregate(0, (k, item) => k | met[item]);
                    head += $", and so do notes in {bars.Count - 1} more bars of this part ({Named(others, many: true)})";
                }
                _diagnostics.Warning(span, DiagnosticCodes.CrossStaffRunsIntoOtherStaff, head + WaysOut);
            }
        }
    }

    /// <summary>The kinds in <paramref name="kinds"/> (a bit a kind) in words: all of them in
    /// the plural, or the first alone with its article.</summary>
    private static string Named(int kinds, bool many)
    {
        var names = new List<string>();
        for (int k = 0; k < Kinds.Length; k++)
            if ((kinds & (1 << k)) != 0)
                names.Add(many ? Kinds[k].Many : Kinds[k].One);
        return many ? string.Join(", ", names) : names[0];
    }

    /// <summary>The note as written, for the message: a note's pitch (<c>fis'</c>), anything
    /// else up to its first annotation (<c>&lt;c' e'&gt;4</c>).</summary>
    private static string WrittenOf(string text, SyntaxNode owner)
    {
        var span = owner is NoteSyntax note ? note.Pitch.Span : owner.Span;
        string s = text.Substring(span.Start, span.Length);
        int at = s.IndexOf('@');
        return (at > 0 ? s[..at] : s).Trim();
    }

    /// <summary>Whether the stem <paramref name="stem"/> is the one that leaves
    /// <paramref name="head"/>: within the head's width, one end inside its height.</summary>
    private static bool LeavesHead(double[] stem, double[] head)
        => stem[0] >= head[0] - 0.05 && stem[2] <= head[2] + 0.05
           && ((stem[1] >= head[1] && stem[1] <= head[3]) || (stem[3] >= head[1] && stem[3] <= head[3]));

    /// <summary>Whether two ink boxes (x0, y0, x1, y1) collide: see <see cref="Depth"/> and
    /// <see cref="Touch"/>.</summary>
    internal static bool Meets(double[] a, double[] b)
        => Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]) > -Touch
           && Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]) > Depth;

    /// <summary>Whether the ink box <paramref name="a"/> collides with a beam's parallelogram
    /// (<paramref name="quad"/>: one long edge left to right, then the other right to left, its
    /// two ends vertical) — the beam's height read where the box stands, not over its whole
    /// length.</summary>
    internal static bool MeetsQuad(double[] a, double[] quad)
    {
        double xl = Math.Min(quad[0], quad[2]), xr = Math.Max(quad[0], quad[2]);
        if (xr - xl <= 0 || Math.Min(a[2], xr) - Math.Max(a[0], xl) <= -Touch)
            return false;
        double from = Math.Clamp(a[0], xl, xr), to = Math.Clamp(a[2], xl, xr);
        // Edge one runs (0,1)->(2,3), edge two (6,7)->(4,5): both over the same x.
        double Y(int x0, int y0, int x1, int y1, double x)
            => quad[y0] + (quad[y1] - quad[y0]) * (x - quad[x0]) / (quad[x1] - quad[x0]);
        double[] ys = [Y(0, 1, 2, 3, from), Y(0, 1, 2, 3, to), Y(6, 7, 4, 5, from), Y(6, 7, 4, 5, to)];
        return Math.Min(a[3], ys.Max()) - Math.Max(a[1], ys.Min()) > Depth;
    }
}
