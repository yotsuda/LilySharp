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
using System.Linq;
using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;
using StreamJsonRpc;

namespace LilySharp.Lsp;

/// <summary>
/// The step keys and the audition (owner's decision, 2026-09-28): <c>lilysharp/step</c>
/// computes what Ctrl+Shift+Up / Ctrl+Shift+Down do at every selection, <c>lilysharp/auditionAt</c>
/// what the caret is on and what it sounds. Both are <see cref="NoteStepper"/>'s — the server
/// has the tree, the resolved voicings and the pitches the compiler plays, so the extension
/// only applies the edits and forwards the pitches to the preview's synth.
/// </summary>
public sealed partial class LilySharpLanguageServer
{
    [JsonRpcMethod("lilysharp/step", UseSingleObjectParameterDeserialization = true)]
    public Task<StepResponse> StepAsync(StepParams @params, CancellationToken token)
        => OffDispatch(() => Step(@params), token);

    /// <summary>One step at every selection of the document; see <see cref="NoteStepper.Step"/>.
    /// The stepped text sounds through the same <c>using</c> expansion the preview plays.</summary>
    public StepResponse Step(StepParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null)
            return new StepResponse { Fallback = true, Error = "Document not found" };
        try
        {
            string basePath = uri.IsFile ? uri.LocalPath : string.Empty;
            var selections = @params.Selections.Select(s => (s.Start, s.End)).ToList();
            SyntaxTree Expand(string text) => ExpandUsings(text, SyntaxTree.Parse(text), basePath,
                p => System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : null).Tree;
            if (@params.Chromatic)
            {
                var chromatic = ChromaticStep(@params, doc, selections, Expand);
                if (@params.WantAccidentals && chromatic.Edits.Length == 1)
                    chromatic.Accidentals = AccidentalsOf(uri, doc.Text[..chromatic.Edits[0].Start] + chromatic.Edits[0].NewText + doc.Text[chromatic.Edits[0].End..], selections[0].Start, @params.RenderName, Expand);
                return chromatic;
            }
            // A note dragged and let go, or held still: all the way at once, the others held.
            if (@params.HoldOthers && selections.Count == 1)
            {
                bool positions = @params.StaffPositions > 0;
                string? moved = NoteStepper.DragNote(doc.Text, doc.Tree, selections[0].Start, @params.Direction >= 0 ? 1 : -1,
                    System.Math.Min(positions ? @params.StaffPositions : @params.Count, MaxStepsAtOnce),
                    positions ? NoteStepper.NoteDrag.StaffPositions : NoteStepper.NoteDrag.Octaves, Expand);
                if (moved != null)
                    return new StepResponse
                    {
                        Edits = [TheDifference(doc.Text, moved)],
                        Accidentals = @params.WantAccidentals ? AccidentalsOf(uri, moved, selections[0].Start, @params.RenderName, Expand) : [],
                        Version = doc.Version,
                    };
            }
            NoteStepper.StepResult StepOn(string on, SyntaxTree tree) => NoteStepper.Step(on, tree, selections, @params.Direction,
                Expand, @params.IncludeStretch, @params.Semitone, FacesForStep);
            var result = StepOn(doc.Text, doc.Tree);
            var edits = result.Edits.Select(e => new StepTextEdit { Start = e.Start, End = e.End, NewText = e.NewText }).ToArray();
            // A drag: each step read off the text the last one left, one edit in the end. Staff
            // positions are counted by the letter under the selection, where a step starts and
            // stays; anything else by the steps.
            bool byLetter = @params.StaffPositions > 0;
            int want = System.Math.Min(byLetter ? @params.StaffPositions : @params.Count, MaxStepsAtOnce);
            bool oneStepped = selections.Count == 1 && !result.Fallback && result.Edits.Any() && result.ConvertOctaves == null;
            if ((byLetter || want > 1 || @params.HoldOthers) && oneStepped)
            {
                int at = selections[0].Start;
                string now = Applied(doc.Text, result.Edits);
                int moved = !byLetter || now[at] != doc.Text[at] ? 1 : 0;
                // Twice as many tries as positions: one same-letter step at most, and a key of few
                // steps cannot hold the walk here for ever.
                for (int tries = 0; moved < want && tries < 2 * want; tries++)
                {
                    var next = StepOn(now, SyntaxTree.Parse(now));
                    if (next.Fallback || !next.Edits.Any())
                        break;
                    string stepped = Applied(now, next.Edits);
                    if (!byLetter || stepped[at] != now[at])
                        moved++;
                    now = stepped;
                    result = next;
                }
                if (@params.HoldOthers)
                    now = NoteStepper.HoldOtherPitches(doc.Text, now, at, Expand);
                edits = [TheDifference(doc.Text, now)];
            }

            return new StepResponse
            {
                Fallback = result.Fallback,
                Edits = edits,
                Message = result.Message,
                Pitches = [.. result.Pitches],
                Timbre = result.Timbre,
                Program = result.Program,
                Version = doc.Version,
                ConvertOctaves = result.ConvertOctaves?.ToString().ToLowerInvariant(),
                Strum = result.Strum,
                Apart = result.Apart,
            };
        }
        catch (Exception ex)
        {
            // A step the server cannot compute must not leave the key dead: the editor
            // falls back to the key's own command.
            return new StepResponse { Fallback = true, Error = ex.Message, Version = doc.Version };
        }
    }

    /// <summary>A note some semitones away, one edit, and what it then sounds.</summary>
    private static StepResponse ChromaticStep(StepParams @params, Document doc, List<(int Start, int End)> selections, System.Func<string, SyntaxTree> expand)
    {
        int count = System.Math.Min(@params.Count, MaxStepsAtOnce);
        if (selections.Count != 1 || count <= 0)
            return new StepResponse { Version = doc.Version };
        int at = selections[0].Start;
        string? stepped = NoteStepper.ChromaticStep(doc.Text, doc.Tree, at, (@params.Direction >= 0 ? 1 : -1) * count, expand, @params.HoldOthers);
        if (stepped == null)
            return new StepResponse { Version = doc.Version };
        // What it sounds is asked for by the hand on the way, not by what is written at the end.
        var heard = @params.HoldOthers ? null : NoteStepper.AuditionAt(stepped, SyntaxTree.Parse(stepped), at, expand(stepped));
        return new StepResponse
        {
            Edits = [TheDifference(doc.Text, stepped)],
            Pitches = heard != null ? [.. heard.Pitches] : [],
            Timbre = heard?.Timbre ?? 0,
            Program = heard?.Program ?? -1,
            Version = doc.Version,
        };
    }

    // A session of its own, for a note's accidental in a drag: it draws texts the document never
    // was, and the preview's session has a picture to keep a delta against. Only the last score asked.
    private readonly object _dragDrawGate = new();
    private (System.Uri Uri, string? Render, LilySharp.Core.Svg.IncrementalCompiler Session)? _dragDraw;

    /// <summary>The accidental the note at <paramref name="at"/> is drawn with in
    /// <paramref name="text"/> (owner's request 2026-11-03: as it goes, on and off by where it is).
    /// Read off the score itself, not worked out again: whether a note needs one hangs on the key,
    /// the bar before it and the staff's other voices, its place on the rest of a chord, and what
    /// it looks like on a natural that restores or a courtesy's brackets.</summary>
    private DraggedGlyph[] AccidentalsOf(System.Uri uri, string text, int at, string? render, System.Func<string, SyntaxTree> expand)
    {
        try
        {
            var tree = expand(text);
            string svg;
            lock (_dragDrawGate)
            {
                if (_dragDraw is not { } held || held.Uri != uri || held.Render != render)
                    _dragDraw = held = (uri, render, new LilySharp.Core.Svg.IncrementalCompiler(tree,
                        LilySharp.Core.Svg.Renderer.SvgRenderOptions.Preview(), render));
                svg = held.Session.RenderIncremental(tree);
            }
            return AccidentalsDrawnAt(svg, at);
        }
        catch (System.Exception)
        {
            // The note goes on without one; the score comes back drawn when it is let go.
            return [];
        }
    }

    /// <summary>The glyphs left of the first head with that source position, on its line.</summary>
    internal static DraggedGlyph[] AccidentalsDrawnAt(string svg, int at)
    {
        string pos = "data-pos=\"" + at.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"";
        static double Attr(string tag, string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(tag, "\\b" + name + "=\"([-0-9.]+)\"");
            return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
        int box = -1;
        for (int i = svg.IndexOf(pos, System.StringComparison.Ordinal); i >= 0; i = svg.IndexOf(pos, i + 1, System.StringComparison.Ordinal))
        {
            int open = svg.LastIndexOf('<', i);
            if (string.CompareOrdinal(svg, open, "<rect class=\"nh-hit\"", 0, 20) == 0) { box = open; break; }
        }
        if (box < 0)
            return [];
        string rect = svg[box..svg.IndexOf('>', box)];
        double x = Attr(rect, "x"), y = Attr(rect, "y") + Attr(rect, "height") / 2;
        var found = new List<DraggedGlyph>();
        for (int i = svg.IndexOf(pos, System.StringComparison.Ordinal); i >= 0; i = svg.IndexOf(pos, i + 1, System.StringComparison.Ordinal))
        {
            int open = svg.LastIndexOf('<', i);
            if (string.CompareOrdinal(svg, open, "<text", 0, 5) != 0)
                continue;
            int close = svg.IndexOf('>', i);
            string tag = svg[open..close];
            double gx = Attr(tag, "x");
            if (gx >= x - 0.001 || System.Math.Abs(Attr(tag, "y") - y) > 0.5)
                continue;
            int end = svg.IndexOf("</text>", close, System.StringComparison.Ordinal);
            found.Add(new DraggedGlyph { Glyph = svg[(close + 1)..end], Dx = gx - x, FontSize = Attr(tag, "font-size") });
        }
        return [.. found];
    }

    /// <summary>More than a drag has room for.</summary>
    private const int MaxStepsAtOnce = 64;

    private static string Applied(string text, IEnumerable<NoteStepper.Edit> edits)
    {
        foreach (var e in edits.OrderByDescending(e => e.Start))
            text = text[..e.Start] + e.NewText + text[e.End..];
        return text;
    }

    /// <summary>The one span in which <paramref name="after"/> differs from <paramref name="before"/>.</summary>
    private static StepTextEdit TheDifference(string before, string after)
    {
        int head = 0;
        while (head < before.Length && head < after.Length && before[head] == after[head])
            head++;
        int tail = 0;
        while (tail < before.Length - head && tail < after.Length - head
            && before[before.Length - 1 - tail] == after[after.Length - 1 - tail])
            tail++;
        return new StepTextEdit { Start = head, End = before.Length - tail, NewText = after[head..(after.Length - tail)] };
    }

    [JsonRpcMethod("lilysharp/noteHeads", UseSingleObjectParameterDeserialization = true)]
    public Task<NoteHeadsResponse> NoteHeadsAsync(NoteHeadsParams @params, CancellationToken token)
        => OffDispatch(() => NoteHeads(@params), token);

    /// <summary>What the preview may take hold of and drag (owner's request 2026-11-02): each
    /// pitch's start, the <c>data-pos</c> its head and accidental are drawn with. A clef, a key, a
    /// rest, a slash and a drum carry a data-pos too and are no note's pitch (a key's tonic is a
    /// pitch of no note); the glyphs cannot tell them apart, a music font being free to number
    /// its own.</summary>
    public NoteHeadsResponse NoteHeads(NoteHeadsParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new NoteHeadsResponse();
        var ledger = NoteStepper.LedgerLineOf(doc.Tree);
        return new NoteHeadsResponse
        {
            Positions = [.. doc.Tree.GetRoot().DescendantNodes<PitchSyntax>()
                .Where(p => p.BaseName != '\0' && p.QuarterOffset == 0
                    && p.Parent is NoteSyntax or ChordSyntax or ArpeggioSyntax)
                .Select(p => p.SourceStart)],
            Marks = [.. NoteStepper.SteppingMarks(doc.Text, doc.Tree)],
            FineMarks = [.. NoteStepper.FineSteppingMarks(doc.Tree)],
            KeyMarks = [.. NoteStepper.KeySteppingMarks(doc.Tree)],
            LedgerThickness = ledger.Thickness,
            LedgerLengthFraction = ledger.LengthFraction,
        };
    }

    /// <summary>The faces a <c>fonts</c> entry's text key steps through: the ones its completion
    /// offers, in the popup's order (<see cref="GetFontNameCompletions"/>), each once.</summary>
    private static IReadOnlyList<string> FacesForStep(string key)
        => [.. GetFontNameCompletions(key).Items
            .OrderBy(i => i.SortText ?? i.Label, StringComparer.Ordinal)
            .Select(i => i.Label!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    [JsonRpcMethod("lilysharp/auditionAt", UseSingleObjectParameterDeserialization = true)]
    public Task<AuditionAtResponse> AuditionAtAsync(AuditionAtParams @params, CancellationToken token)
        => OffDispatch(() => AuditionAt(@params), token);

    /// <summary>What the caret is on and the pitches it sounds, read off the tree the preview
    /// plays (the expanded one); Key -1 when it is on nothing that sounds.</summary>
    public AuditionAtResponse AuditionAt(AuditionAtParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new AuditionAtResponse();
        try
        {
            var (sounding, _) = ExpandUsings(doc, @params.TextDocument.Uri);
            if (NoteStepper.AuditionAt(doc.Text, doc.Tree, @params.Offset, sounding,
                    @params.IncludeStretch) is not { } audition)
                return new AuditionAtResponse { Version = doc.Version };
            return new AuditionAtResponse
            {
                Key = audition.Target.Key,
                Kind = audition.Target.Kind.ToString().ToLowerInvariant(),
                Pitches = [.. audition.Pitches],
                Timbre = audition.Timbre,
                Program = audition.Program,
                Strum = audition.Strum,
                Apart = audition.Apart,
                Version = doc.Version,
            };
        }
        catch (Exception)
        {
            return new AuditionAtResponse { Version = doc.Version };
        }
    }

    [JsonRpcMethod("lilysharp/caretInfo", UseSingleObjectParameterDeserialization = true)]
    public Task<CaretInfoResponse> CaretInfoAsync(AuditionAtParams @params, CancellationToken token)
        => OffDispatch(() => CaretInfo(@params), token);

    /// <summary>The status line of the setting under the caret (<see cref="NoteStepper.Describe"/>,
    /// owner's request 2026-10-24: in the status bar, where a hover would cover the text); a null
    /// Text when it is on nothing that has one.</summary>
    public CaretInfoResponse CaretInfo(AuditionAtParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new CaretInfoResponse();
        try
        {
            string? text = NoteStepper.Describe(doc.Text, doc.Tree, @params.Offset);
            // A note, chord or member: its bar, beat and pitches, written and sounding — the
            // sound off the tree the preview plays (owner's request 2026-10-24).
            if (text == null)
            {
                var (sounding, _) = ExpandUsings(doc, @params.TextDocument.Uri);
                var heard = NoteStepper.AuditionAt(doc.Text, doc.Tree, @params.Offset, sounding)?.Pitches ?? [];
                text = NoteStepper.DescribeNote(doc.Text, doc.Tree, @params.Offset,
                    NotePlacesOf(@params.TextDocument.Uri, doc), heard);
            }
            return new CaretInfoResponse { Text = text, Version = doc.Version };
        }
        catch (Exception)
        {
            return new CaretInfoResponse { Version = doc.Version };
        }
    }

    // The last document's note places, by version: the caret asks on every move, and a collect
    // per move would be the preview's whole cost again (the PlacesOf cache's reason).
    private readonly object _notePlacesLock = new();
    private (Uri Uri, int Version, NotePlaces Places)? _notePlaces;

    private NotePlaces NotePlacesOf(Uri uri, Document doc)
    {
        lock (_notePlacesLock)
            if (_notePlaces is { } cached && cached.Uri == uri && cached.Version == doc.Version)
                return cached.Places;
        var places = NotePlaces.Of(doc.Tree);
        lock (_notePlacesLock)
            _notePlaces = (uri, doc.Version, places);
        return places;
    }
}
