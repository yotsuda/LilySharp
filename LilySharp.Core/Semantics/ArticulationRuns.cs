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
using System.Runtime.CompilerServices;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// A mark over a run of notes: <c>c'8@staccato d e f@!staccato</c> (owner's decision 2026-11-12,
/// grammar evaluation 2). Written with an end <c>@!NAME</c> on the LAST note of the run, every
/// note from the opening one to that one, both included, carries the mark — drawn and played on
/// each exactly as if each had been written <c>@NAME</c>. A mark with no <c>@!NAME</c> after it
/// is one note's, as it always was, so no file written before this changes meaning.
/// </summary>
/// <remarks>
/// <para>THE RULE, read on the syntax (as <see cref="SpanNames"/> reads a span's name): an end
/// closes the NEAREST <c>@NAME</c> written before it in its own voice line
/// (<see cref="SpanNames.Line"/>: a part's music in one section, a phrase, one branch of
/// <c>voice { } { }</c>) that no earlier end has closed. Nearest, not first: a part may hold any
/// number of single marks before a run, and none of them may be made the opening of one by an
/// end written forty bars on. So a run CLOSES IN THE SECTION IT OPENS IN — an end with no
/// opening before it in its line is an error (<see cref="DiagnosticCodes.UnpairedSpan"/>), which
/// is the rule <c>@!8va</c> is held to within a section.</para>
/// <para>WHAT THE RUN COVERS: the notes and chords written between its two ends in that line,
/// the ones inside a tuplet or a repeat among them. Rests carry nothing. Not covered, each
/// because the run is read where it is written: grace notes (ornaments of the run, not members
/// of it), another voice of a <c>voice { } { }</c>, and the notes of a phrase called between
/// the ends (a phrase's notes are the same at every call — the call inside a run is a
/// warning).</para>
/// <para>ONE HOME: the page (MeasureCollector.CollectArticulations), the sound (MidiExporter)
/// and the MusicXML and LilyPond exporters each ask <see cref="Covering"/> for the note they
/// stand on and treat what comes back as marks written on it. Nothing downstream of them knows
/// a run from four marks.</para>
/// <para>LILYSHARP-OWN: LilyPond has no such spanner; its twin writes the script on each note.</para>
/// </remarks>
public static class ArticulationRuns
{
    /// <summary>The marks that may run: the ones a passage is marked with note after note.</summary>
    public static IReadOnlyList<string> Names { get; } = ["staccato", "staccatissimo", "tenuto", "accent", "marcato"];

    /// <summary>Whether <paramref name="name"/> is a mark that may run (case-sensitive, as every
    /// annotation name is).</summary>
    public static bool IsRunName(string? name) => name is "staccato" or "staccatissimo" or "tenuto" or "accent" or "marcato";

    /// <summary>One run: the mark that opens it, the end that closes it, and the notes it covers
    /// beyond the opening one (which carries its own written mark).</summary>
    public sealed record Run(ArticulationSyntax Opener, MusicMarkSyntax End, IReadOnlyList<SyntaxNode> Covered,
        IReadOnlyList<VariableReferenceSyntax> PhraseCalls);

    /// <summary>What a tree's runs come to: the runs, the ends that close nothing, and — the
    /// lookup the four readers make — the openers covering each note, by the note's start.</summary>
    public sealed class Reading
    {
        internal static readonly Reading None = new();
        /// <summary>The runs, in the order their ends are written.</summary>
        public List<Run> Runs { get; } = [];
        /// <summary>The <c>@!NAME</c> with no <c>@NAME</c> open before it in its line.</summary>
        public List<MusicMarkSyntax> StrayEnds { get; } = [];
        internal Dictionary<int, List<ArticulationSyntax>> ByItemStart { get; } = [];
    }

    private static readonly ConditionalWeakTable<SyntaxNode, Reading> Cache = new();

    /// <summary>The runs written in the tree <paramref name="node"/> belongs to. Read once per
    /// tree; a tree with no <c>@!NAME</c> of a running mark costs one scan and holds nothing.</summary>
    public static Reading Of(SyntaxNode node)
    {
        var root = node;
        while (root.Parent != null)
            root = root.Parent;
        return Cache.GetValue(root, Read);
    }

    /// <summary>Whether the tree writes any run — what the collect resume asks before it adopts
    /// a prefix: a covered note's marks are a function of the text AFTER it (CollectResumePlanner).</summary>
    public static bool Any(SyntaxNode root) => Of(root).Runs.Count > 0;

    /// <summary>The marks <paramref name="item"/> (a note, a chord, a <c>q</c>, a slash, a bare
    /// duration, a drum note, a member of a <c>&lt;&lt; &gt;&gt;</c> group) carries from runs it stands in and
    /// did not open — the opening marks themselves, to be read as if written on it. Empty for
    /// every note of a file with no run.</summary>
    public static IReadOnlyList<ArticulationSyntax> Covering(SyntaxNode item)
    {
        var reading = Of(item);
        return reading.ByItemStart.Count > 0 && IsCoverable(item) && reading.ByItemStart.TryGetValue(item.Span.Start, out var openers)
            ? openers
            : [];
    }

    /// <summary><paramref name="written"/> — the marks written on <paramref name="item"/> — and
    /// after them the marks it carries from runs (<see cref="Covering"/>): what an exporter that
    /// reads a note's marks as one list reads. The list itself where no run covers the note.</summary>
    public static IEnumerable<SyntaxNode> WithCovering(SyntaxNode item, IEnumerable<SyntaxNode> written)
    {
        var covering = Covering(item);
        return covering.Count == 0 ? written : System.Linq.Enumerable.Concat(written, covering);
    }

    /// <summary>The note-like item a mark is written on: the mark's parent, or the chord when
    /// that parent is a chord's member.</summary>
    private static SyntaxNode? HostOf(SyntaxNode mark)
        => mark.Parent is { Parent: ChordSyntax chord } ? chord : mark.Parent;

    /// <summary>An item a run can cover, as each reader meets it: one column of the music.</summary>
    private static bool IsCoverable(SyntaxNode node) => node switch
    {
        NoteSyntax or ChordSyntax or ChordRepetitionSyntax or SlashNoteSyntax or BareDurationSyntax => true,
        DrumNoteSyntax => node.Parent is not ChordSyntax,
        PitchSyntax => node.Parent is ArpeggioSyntax,
        _ => false,
    };

    private static bool InGrace(SyntaxNode node, SyntaxNode line)
    {
        for (var p = node.Parent; p != null && !ReferenceEquals(p, line); p = p.Parent)
            if (p is GraceExpressionSyntax)
                return true;
        return false;
    }

    private static Reading Read(SyntaxNode root)
    {
        // The common case first: no end of a running mark anywhere, nothing to hold.
        // ⚠️ Through the GREEN finder: this is asked on every keystroke's collect, and a walk of
        // the root's descendants would build every red of the book there
        // (KeystrokeStaysRedFreeTests). Only a file that writes a run pays for more.
        List<MusicMarkSyntax>? ends = null;
        foreach (var node in root.KindSites(SyntaxKind.MusicMark))
            if (node is MusicMarkSyntax { IsSpanEnd: true } end && IsRunName(end.Name))
                (ends ??= []).Add(end);
        if (ends == null)
            return Reading.None;

        var reading = new Reading();
        var closed = new HashSet<ArticulationSyntax>(ReferenceEqualityComparer.Instance);
        foreach (var end in ends)
        {
            var line = SpanNames.Line(end);
            var endHost = HostOf(end);
            // The nearest opening of that name before the end's note, in the end's own line,
            // not already closed. (One on the end's own note is that note's mark, not a run.)
            ArticulationSyntax? opener = null;
            if (endHost != null)
                foreach (var d in line.DescendantNodes())
                {
                    if (d.Span.Start >= endHost.Span.Start)
                        break;
                    if (d is ArticulationSyntax art && art.Name == end.Name && !closed.Contains(art)
                        && ReferenceEquals(SpanNames.Line(art), line) && !InGrace(art, line))
                        opener = art;
                }
            if (opener == null || HostOf(opener) is not { } openHost)
            {
                reading.StrayEnds.Add(end);
                continue;
            }
            closed.Add(opener);

            var covered = new List<SyntaxNode>();
            var calls = new List<VariableReferenceSyntax>();
            foreach (var d in line.DescendantNodes())
            {
                if (d.Span.Start <= openHost.Span.Start)
                    continue;
                if (d.Span.Start > endHost!.Span.Start)
                    break;
                if (!ReferenceEquals(SpanNames.Line(d), line))
                    continue;
                if (d is VariableReferenceSyntax call)
                    calls.Add(call);
                else if (IsCoverable(d) && !InGrace(d, line))
                {
                    covered.Add(d);
                    if (!reading.ByItemStart.TryGetValue(d.Span.Start, out var openers))
                        reading.ByItemStart[d.Span.Start] = openers = [];
                    openers.Add(opener);
                }
            }
            reading.Runs.Add(new Run(opener, end, covered, calls));
        }
        // A note in two runs reads their marks in the order the openings were written, as it
        // would read them written on itself — not in the order the ends happen to close.
        foreach (var openers in reading.ByItemStart.Values)
            if (openers.Count > 1)
                openers.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return reading;
    }
}

/// <summary>What a run of marks (<see cref="ArticulationRuns"/>) can be told at the place it is
/// written: an end that closes nothing (an error — nothing says which notes it meant), and a
/// phrase called inside a run, whose notes the run does not reach (a warning).</summary>
internal sealed class ArticulationRunValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var reading = ArticulationRuns.Of(tree.GetRoot());
        foreach (var end in reading.StrayEnds)
            _diagnostics.Error(end.Span, DiagnosticCodes.UnpairedSpan,
                $"this '@!{end.Name}' ends nothing: no '@{end.Name}' is written before it in this part's music of "
                + $"this section (or this phrase, or this voice). A run of marks opens and closes in one of them - "
                + $"write '@{end.Name}' on the first note of the run and '@!{end.Name}' on its last "
                + $"(c8@{end.Name} d e f@!{end.Name}); one note's mark is '@{end.Name}' alone.");
        foreach (var run in reading.Runs)
            foreach (var call in run.PhraseCalls)
                _diagnostics.Warning(call.Span, DiagnosticCodes.UnpairedSpan,
                    $"the '@{run.Opener.Name}' run that '@!{run.End.Name}' closes does not reach into '{call.Name.Text}': "
                    + "a run marks the notes written between its two ends, and a phrase's notes are the same at "
                    + $"every call. Write the run inside the phrase, or close it before the call and open it again after.");
    }
}
