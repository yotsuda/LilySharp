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
            var result = NoteStepper.Step(doc.Text, doc.Tree,
                @params.Selections.Select(s => (s.Start, s.End)).ToList(), @params.Direction,
                text => ExpandUsings(text, SyntaxTree.Parse(text), basePath,
                    p => System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : null).Tree,
                @params.IncludeStretch, @params.Semitone, FacesForStep);
            return new StepResponse
            {
                Fallback = result.Fallback,
                Edits = result.Edits.Select(e => new StepTextEdit { Start = e.Start, End = e.End, NewText = e.NewText }).ToArray(),
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
