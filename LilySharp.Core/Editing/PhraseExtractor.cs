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
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The "Extract phrase" refactoring: lifts a section's music (the whole body, or
/// the WHOLE MEASURES a selection touches) into a top-level
/// <c>phrase NAME { … }</c> and replaces it with the reference — so a melody
/// written directly in sections can be reused by other parts
/// (<c>harmony { section A { NAME'(3) } }</c>) without hand-refactoring.
/// </summary>
/// <remarks>
/// Extraction is SEMANTICS-PRESERVING or refused: a phrase body evaluates in a
/// fresh frame (default octave, quarter default duration), so the extractor
/// stamps the running duration onto the chunk's first undurated note and
/// corrects octave marks by MEASUREMENT — it compares the MIDI of the document
/// before and after, nudges marks by the observed octave delta, and refuses
/// outright (no changes) if the results still differ. There are TWO independent
/// mark knobs: the body's first pitch (fixes the phrase's own register) and the
/// first pitch AFTER the reference (fixes the relative chain downstream — the
/// reference hands off its ANCHOR, the first note's BARE letter, so body marks
/// never leak out). The measure is the extraction unit: barlines are the only
/// boundaries where the frame hand-off is fully compensatable.
/// </remarks>
public static class PhraseExtractor
{
    public sealed record Result(string? NewText, string? Error);

    public static Result Extract(string text, int selStart, int selEnd, string name)
    {
        var tree = SyntaxTree.Parse(text);
        if (tree.HasErrors)
            return new(null, "Fix the syntax errors before extracting - no changes made.");

        if (string.IsNullOrWhiteSpace(name) || !NameParsesAsPhrase(name))
            return new(null, $"'{name}' is not usable as a phrase name (it reads as a note or keyword).");
        if (DeclaredNames(tree).Contains(name))
            return new(null, $"'{name}' is already declared - pick another name.");

        // The music container under the caret: a part-major section cell, a
        // section-major part block, or a standalone section's own music.
        var root = tree.GetRoot();
        var container = root.DescendantNodes()
            .Where(n => n is PartBlockSyntax or SectionDeclarationSyntax)
            .Where(n => n.Span.Start <= selStart && selEnd <= n.Span.End)
            .OrderByDescending(n => n.Span.Start)
            .FirstOrDefault();
        if (container is null)
            return new(null, "Place the caret inside a section's music.");

        var (bodyStart, bodyEnd) = BraceBody(container);
        if (bodyStart < 0 || bodyEnd <= bodyStart)
            return new(null, "The section has no music to extract.");

        // The chunk: the whole body (bare caret), or the whole measures the
        // selection touches — snapped outward to the surrounding barlines.
        int chunkStart = bodyStart, chunkEnd = bodyEnd;
        if (selEnd > selStart)
        {
            foreach (var bar in container.DescendantNodes().OfType<BarlineSyntax>())
            {
                int end = InkEnd(bar);
                if (end <= selStart && end > chunkStart) chunkStart = end;
                if (end >= selEnd && end < chunkEnd) chunkEnd = end;
            }
        }

        var timed = container.DescendantNodes()
            .Where(n => n is NoteSyntax or ChordSyntax or ChordRepetitionSyntax or RestSyntax or ArpeggioSyntax)
            .Where(n => n.Span.Start >= chunkStart && n.Span.Start < chunkEnd)
            .OrderBy(n => n.Span.Start)
            .ToList();
        if (timed.Count == 0)
            return new(null, "The selected measures hold no music to extract.");

        // Compensation 1: a phrase resets the default duration to a quarter, so
        // the chunk's first undurated note must carry the running duration.
        string? durationStamp = null;
        int durationInsertAt = -1;
        if (FirstDurationless(timed[0]) is { } insertAt)
        {
            var runningAt = container.DescendantNodes()
                .Where(n => n is NoteSyntax or ChordSyntax or ChordRepetitionSyntax or RestSyntax)
                .Where(n => n.Span.Start < chunkStart && n.Span.Start >= bodyStart)
                .Select(DurationText(text))
                .LastOrDefault(t => t != null);
            if (runningAt != null)
            {
                durationStamp = runningAt;
                durationInsertAt = insertAt;
            }
        }

        // Compensation 2: the first pitch's octave marks, corrected by
        // measurement below (the fresh frame may place the letter octaves away).
        var firstPitch = container.DescendantNodes().OfType<PitchSyntax>()
            .Where(p => p.Span.Start >= chunkStart && p.Span.Start < chunkEnd)
            .OrderBy(p => p.Span.Start)
            .FirstOrDefault();

        // Compensation 3: the first pitch AFTER the reference. The reference
        // hands its ANCHOR off (the body's first BARE letter), so the downstream
        // chain no longer sees the inline chunk's last note — and since marks on
        // the body's first pitch never leak out, the two knobs are INDEPENDENT.
        var tailPitch = container.DescendantNodes().OfType<PitchSyntax>()
            .Where(p => p.Span.Start >= chunkEnd)
            .OrderBy(p => p.Span.Start)
            .FirstOrDefault();

        var baseline = MidiSignature(tree);
        int bodyDelta = 0, tailDelta = 0;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var candidate = Build(text, name, container, chunkStart, chunkEnd,
                durationStamp, durationInsertAt, firstPitch, bodyDelta, tailPitch, tailDelta);
            var newTree = SyntaxTree.Parse(candidate);
            if (newTree.HasErrors)
            {
                var first = newTree.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
                return new(null, "Extraction would not parse cleanly - no changes made. "
                    + $"({first.Code}: {first.Message})");
            }
            var sig = MidiSignature(newTree);
            int diff = FirstDifference(baseline, sig);
            if (diff < 0)
                return new(candidate, null); // provably identical music
            if (diff >= baseline.Count || diff >= sig.Count)
                break;
            int gap = baseline[diff].Pitch - sig[diff].Pitch;
            if (gap == 0 || gap % 12 != 0)
                break; // not a whole-octave miss — nothing these knobs can fix
            int step = gap / 12;
            // Body knob first; keep the turn only when it moves the first diff
            // LATER (the differing note was in the body). Knob independence
            // makes each accepted turn strict progress, so this terminates.
            if (firstPitch != null)
            {
                bodyDelta += step;
                var probe = SyntaxTree.Parse(Build(text, name, container, chunkStart, chunkEnd,
                    durationStamp, durationInsertAt, firstPitch, bodyDelta, tailPitch, tailDelta));
                if (!probe.HasErrors)
                {
                    int probeDiff = FirstDifference(baseline, MidiSignature(probe));
                    if (probeDiff < 0 || probeDiff > diff)
                        continue;
                }
                bodyDelta -= step;
            }
            // The diff sits past the reference — turn the tail knob.
            if (tailPitch is null)
                break;
            tailDelta += step;
        }
        return new(null, "Extraction here would change the music (a construct this "
            + "refactoring cannot compensate spans the boundary) - no changes made.");
    }

    private static string Build(string text, string name, SyntaxNode container,
        int chunkStart, int chunkEnd, string? durationStamp, int durationInsertAt,
        PitchSyntax? firstPitch, int bodyDelta, PitchSyntax? tailPitch, int tailDelta)
    {
        // Patch the chunk text (offsets descending so they don't shift).
        var chunk = text[chunkStart..chunkEnd];
        var patches = new List<(int Start, int End, string Text)>();
        if (durationStamp != null && durationInsertAt >= 0)
            patches.Add((durationInsertAt, durationInsertAt, durationStamp));
        if (firstPitch != null && bodyDelta != 0)
        {
            var (mStart, mEnd, net) = PitchMarks(firstPitch);
            int n = net + bodyDelta;
            patches.Add((mStart, mEnd, n >= 0 ? new string('\'', n) : new string(',', -n)));
        }
        foreach (var (s, e, t) in patches.OrderByDescending(p => p.Start))
            chunk = chunk[..(s - chunkStart)] + t + chunk[(e - chunkStart)..];

        // The tail patch lands AFTER the chunk, so its offsets are relative to
        // chunkEnd and survive the reference substitution below.
        string tail = text[chunkEnd..];
        if (tailPitch != null && tailDelta != 0)
        {
            var (mStart, mEnd, net) = PitchMarks(tailPitch);
            int n = net + tailDelta;
            tail = tail[..(mStart - chunkEnd)]
                 + (n >= 0 ? new string('\'', n) : new string(',', -n))
                 + tail[(mEnd - chunkEnd)..];
        }

        // The declaration goes at line start before the container's top-level
        // ancestor (the enclosing `part …` / `section …`).
        var topLevel = container;
        while (topLevel.Parent is { } p && p.Parent != null)
            topLevel = p;
        int insertPos = topLevel.Span.Start;
        while (insertPos > 0 && text[insertPos - 1] != '\n')
            insertPos--;

        string decl = $"phrase {name} {{{chunk}}}\n\n";
        string result = text[..chunkStart] + " " + name + " " + tail;
        return result[..insertPos] + decl + result[insertPos..];
    }

    /// <summary>Insertion offset for a duration on the chunk's first timed item
    /// when it carries none (after the pitch+marks / the closing bracket+marks /
    /// the rest letter), or null when it already has one.</summary>
    private static int? FirstDurationless(SyntaxNode first) => first switch
    {
        NoteSyntax n => n.Duration is null ? InkEnd(n.Pitch) : null,
        RestSyntax r => r.Duration is null ? ((SyntaxTokenNode)r.GetChild(0)!).Span.End : null,
        ChordSyntax c => c.Duration is null ? ChordDurationSlot(c) : null,
        ChordRepetitionSyntax q => q.Duration is null ? q.QToken.Span.End : null,
        ArpeggioSyntax => null, // a group inherits deliberately; the guard decides
        _ => null,
    };

    /// <summary>A node's INK end: the last descendant token's span end. Composite
    /// node spans include their children's trailing trivia; token spans do not, so
    /// precise text surgery must anchor on tokens.</summary>
    private static int InkEnd(SyntaxNode node)
    {
        int end = node.Span.Start;
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child is SyntaxTokenNode t)
                end = Math.Max(end, t.Span.End);
            else if (child is SyntaxNode inner)
                end = Math.Max(end, InkEnd(inner));
        }
        return end;
    }

    private static int ChordDurationSlot(ChordSyntax chord)
    {
        int at = chord.Span.End;
        for (int i = 0; i < chord.SlotCount; i++)
            if (chord.GetChild(i) is SyntaxTokenNode t
                && t.Kind is SyntaxKind.CloseAngle or SyntaxKind.Apostrophe or SyntaxKind.Comma)
                at = t.Span.End;
        return at;
    }

    /// <summary>The written duration text ("4", "2.", …) of a timed node, or null.</summary>
    private static Func<SyntaxNode, string?> DurationText(string text) => node =>
    {
        var d = node switch
        {
            NoteSyntax n => n.Duration,
            RestSyntax r => r.Duration,
            ChordSyntax c => c.Duration,
            ChordRepetitionSyntax q => q.Duration,
            _ => null,
        };
        return d is null ? null : text.Substring(d.Span.Start, d.Span.Length).TrimEnd();
    };

    /// <summary>The first pitch's octave-mark text range and net value.</summary>
    private static (int Start, int End, int Net) PitchMarks(PitchSyntax pitch)
    {
        int start = ((SyntaxTokenNode)pitch.GetChild(0)!).Span.End;
        int end = start, net = 0;
        for (int i = 1; i < pitch.SlotCount; i++)
        {
            if (pitch.GetChild(i) is not SyntaxTokenNode t)
                continue;
            if (t.Kind == SyntaxKind.Apostrophe) { net++; end = t.Span.End; }
            else if (t.Kind == SyntaxKind.Comma) { net--; end = t.Span.End; }
        }
        return (start, end, net);
    }

    /// <summary>The text range between a container's braces (its music body). A
    /// section-major part block holds its braces on a child music block, so fall
    /// through to it when the container has no direct brace tokens.</summary>
    private static (int Start, int End) BraceBody(SyntaxNode container)
    {
        int start = -1, end = -1;
        for (int i = 0; i < container.SlotCount; i++)
        {
            if (container.GetChild(i) is not SyntaxTokenNode t)
                continue;
            if (t.Kind == SyntaxKind.OpenBrace && start < 0)
                start = t.Span.End;
            else if (t.Kind == SyntaxKind.CloseBrace)
                end = t.Span.Start;
        }
        if (start < 0)
            for (int i = 0; i < container.SlotCount; i++)
                if (container.GetChild(i) is MusicBlockSyntax mb)
                    return BraceBody(mb);
        return (start, end);
    }

    private static HashSet<string> DeclaredNames(SyntaxTree tree)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in tree.GetRoot().DescendantNodes())
        {
            switch (n)
            {
                case PhraseDeclarationSyntax p: names.Add(p.Name.Text); break;
                case VariableDeclarationSyntax v: names.Add(v.Name.Text); break;
                case PartDeclarationSyntax pd: names.Add(pd.Name.Text); break;
                case PartBlockSyntax pb: names.Add(pb.Name); break;
                case SectionDeclarationSyntax s: names.Add(s.SectionName); break;
            }
        }
        return names;
    }

    /// <summary>A usable phrase name both DECLARES as a phrase and READS BACK as a
    /// reference — this rejects pitch spellings (c, ees), keywords and operators
    /// without duplicating the lexer's vocabulary.</summary>
    private static bool NameParsesAsPhrase(string name)
    {
        var decl = SyntaxTree.Parse($"phrase {name} {{ }}");
        if (decl.HasErrors || decl.GetRoot().DescendantNodes().OfType<PhraseDeclarationSyntax>()
                .SingleOrDefault()?.Name.Text != name)
            return false;
        var reference = SyntaxTree.Parse($"{{ {name} }}");
        return !reference.HasErrors && reference.GetRoot().DescendantNodes()
            .OfType<VariableReferenceSyntax>().SingleOrDefault()?.Name.Text == name;
    }

    private readonly record struct MidiEvent(int Channel, int Pitch, int Tick, int Duration);

    /// <summary>The document's sounding content, position-independent — what the
    /// refactoring must preserve exactly.</summary>
    private static List<MidiEvent> MidiSignature(SyntaxTree tree)
    {
        var midi = new MidiExporter().Export(tree);
        var events = new List<MidiEvent>();
        foreach (var track in midi.Tracks)
            foreach (var note in track.Notes)
                events.Add(new MidiEvent(note.Channel, note.Pitch, note.StartTick, note.DurationTicks));
        return events;
    }

    private static int FirstDifference(List<MidiEvent> a, List<MidiEvent> b)
    {
        int n = Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i])
                return i;
        return a.Count == b.Count ? -1 : n;
    }
}
