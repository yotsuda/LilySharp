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
                @params.IncludeStretch);
            return new StepResponse
            {
                Fallback = result.Fallback,
                Edits = result.Edits.Select(e => new StepTextEdit { Start = e.Start, End = e.End, NewText = e.NewText }).ToArray(),
                Message = result.Message,
                Pitches = [.. result.Pitches],
                Timbre = result.Timbre,
                Version = doc.Version,
            };
        }
        catch (Exception ex)
        {
            // A step the server cannot compute must not leave the key dead: the editor
            // falls back to the key's own command.
            return new StepResponse { Fallback = true, Error = ex.Message, Version = doc.Version };
        }
    }

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
                Version = doc.Version,
            };
        }
        catch (Exception)
        {
            return new AuditionAtResponse { Version = doc.Version };
        }
    }
}
