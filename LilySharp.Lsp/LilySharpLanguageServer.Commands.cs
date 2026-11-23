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
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using LilySharp.Lsp.Protocol;
using StreamJsonRpc;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LspRange = LilySharp.Lsp.Protocol.Range;
using LspDiagnosticSeverity = LilySharp.Lsp.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    /// <summary>
    /// Returns the server version for debugging deployment issues. Deliberately NOT
    /// through OffDispatch: it reads one static field, and staying on the dispatch
    /// loop makes its round-trip a fence — when it answers, everything sent before
    /// it has been dispatched (the async-dispatch tests lean on this).
    /// </summary>
    [JsonRpcMethod("lilysharp/version")]
    public string GetVersion()
    {
        return Version;
    }

    /// <summary>
    /// Reads the chords the preview's score plays and returns the edits that add them as a
    /// chords track (<see cref="LilySharp.Core.Harmony.ChordHarmonizer.AddReadChordTracks"/>):
    /// read from that score's parts beat by beat, a melody alone harmonized as rich as
    /// <see cref="AddChordTrackParams.Richness"/> asks — written beside the music as the file is
    /// laid out, and placed in that score above its top staff, a row of its own.
    /// Powers the "Lily#: Add Chord Track" editor command (owner's decisions 2026-10-26).
    /// </summary>
    [JsonRpcMethod("lilysharp/addChordTrack", UseSingleObjectParameterDeserialization = true)]
    public Task<AddChordTrackResponse> AddChordTrackAsync(AddChordTrackParams @params, CancellationToken token)
        => OffDispatch(() => AddChordTrack(@params), token);

    public AddChordTrackResponse AddChordTrack(AddChordTrackParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new AddChordTrackResponse { Error = DocumentNotFound };

        if (doc.Tree.HasErrors)     // (the document's own tree: no second parse of the text)
            return new AddChordTrackResponse { Error = "Fix the errors in the score first." };

        var richness = @params.Richness?.ToLowerInvariant() switch
        {
            "plain" => LilySharp.Core.Harmony.ChordRichness.Plain,
            "rich" => LilySharp.Core.Harmony.ChordRichness.Rich,
            _ => LilySharp.Core.Harmony.ChordRichness.Standard,
        };
        var result = LilySharp.Core.Harmony.ChordHarmonizer.AddReadChordTracks(doc.Text, @params.RenderName, melodyOnly: false, richness);
        if (result == null)
            return new AddChordTrackResponse { Error = "Nothing in the score shown sounds a note to read chords from." };

        // One full-document replace: the track and the score's rows are inserted at once.
        var (endLine, endCol) = GetLineAndColumn(doc.Text, doc.Text.Length);
        return new AddChordTrackResponse
        {
            Edits = new[]
            {
                new ChordTrackEdit
                {
                    StartLine = 0, StartChar = 0, EndLine = endLine, EndChar = endCol, NewText = result.Value.Text,
                },
            },
            Info = result.Value.Info,
        };
    }

    /// <summary>
    /// Imports a MusicXML file (<c>.xml</c> / <c>.musicxml</c> / <c>.mxl</c>) into
    /// Lily# source. A file PATH is preferred so a binary <c>.mxl</c> zip reads
    /// directly; raw <c>XmlText</c> is the fallback. Import is opinionated and
    /// non-unique: the result is a faithful STARTING POINT, and every dropped or
    /// approximated construct comes back in <c>Warnings</c> (the import report).
    /// Powers the "Lily#: Import MusicXML, MIDI or ABC…" editor command (a MIDI file and ABC
    /// notation are told by what they hold, <c>MusicXmlImporter.ImportBytes</c>).
    /// </summary>
    [JsonRpcMethod("lilysharp/importMusicXml", UseSingleObjectParameterDeserialization = true)]
    public Task<ImportMusicXmlResponse> ImportMusicXmlAsync(ImportMusicXmlParams @params, CancellationToken token)
        => OffDispatch(() => ImportMusicXml(@params), token);

    public ImportMusicXmlResponse ImportMusicXml(ImportMusicXmlParams @params)
    {
        try
        {
            var importer = new LilySharp.Core.MusicXmlImport.MusicXmlImporter();
            (string Lys, LilySharp.Core.MusicXmlImport.ImportReport Report) result;
            if (!string.IsNullOrEmpty(@params.FilePath))
            {
                if (!System.IO.File.Exists(@params.FilePath))
                    return new ImportMusicXmlResponse { Error = $"File not found: {@params.FilePath}" };
                var pieces = importer.ImportPieces(System.IO.File.ReadAllBytes(@params.FilePath), @params.RelativeOctave);
                if (pieces.Count > 1)
                {
                    // An ABC tune book: the extension writes a .lys per tune (docs/abc-import-design.md §3).
                    var first = pieces.FirstOrDefault(p => p.Lys != null);
                    return new ImportMusicXmlResponse
                    {
                        Lys = first?.Lys,
                        Warnings = first?.Report.Warnings.ToArray() ?? [],
                        Error = first == null ? "No tune of the file could be read." : null,
                        Pieces = pieces.Select(p => new ImportedPieceDto
                        {
                            Name = p.Name ?? "",
                            Title = p.Title,
                            Lys = p.Lys,
                            Warnings = p.Report.Warnings.ToArray(),
                            Error = p.Error,
                        }).ToArray(),
                    };
                }
                result = (pieces[0].Lys!, pieces[0].Report);
            }
            else if (!string.IsNullOrEmpty(@params.XmlText))
            {
                result = importer.Import(@params.XmlText, @params.RelativeOctave);
            }
            else
            {
                return new ImportMusicXmlResponse { Error = "No MusicXML file path or text provided." };
            }

            return new ImportMusicXmlResponse
            {
                Lys = result.Lys,
                Warnings = result.Report.Warnings.ToArray(),
            };
        }
        catch (Exception ex)
        {
            return new ImportMusicXmlResponse { Error = ex.Message };
        }
    }

    /// <summary>
    /// The "Extract phrase" refactoring: lifts the section music at the caret (or
    /// the whole measures a selection touches) into a top-level phrase and replaces
    /// it with the reference. Verified semantics-preserving (the MIDI of the old
    /// and new documents must match) or refused with no changes — see
    /// <see cref="LilySharp.Core.Editing.PhraseExtractor"/>.
    /// </summary>
    [JsonRpcMethod("lilysharp/extractPhrase", UseSingleObjectParameterDeserialization = true)]
    public Task<ExtractPhraseResponse> ExtractPhraseAsync(ExtractPhraseParams @params, CancellationToken token)
        => OffDispatch(() => ExtractPhrase(@params), token);

    public ExtractPhraseResponse ExtractPhrase(ExtractPhraseParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new ExtractPhraseResponse { Success = false, Error = DocumentNotFound };

        var result = LilySharp.Core.Editing.PhraseExtractor.Extract(
            doc.Text, @params.SelectionStart, @params.SelectionEnd, @params.Name);
        return result.NewText != null
            ? new ExtractPhraseResponse { Success = true, NewText = result.NewText }
            : new ExtractPhraseResponse { Success = false, Error = result.Error };
    }

    /// <summary>
    /// "Guess Section Key": the ranked keys of every section the caret or selection touches
    /// (<see cref="LilySharp.Core.Harmony.KeyEstimator"/>).
    /// </summary>
    [JsonRpcMethod("lilysharp/guessSectionKey", UseSingleObjectParameterDeserialization = true)]
    public Task<GuessSectionKeyResponse> GuessSectionKeyAsync(GuessSectionKeyParams @params, CancellationToken token)
        => OffDispatch(() => GuessSectionKey(@params), token);

    public GuessSectionKeyResponse GuessSectionKey(GuessSectionKeyParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new GuessSectionKeyResponse { Error = DocumentNotFound };

        var tree = doc.Tree;
        if (tree.HasErrors)
            return new GuessSectionKeyResponse { Error = "Fix the errors in the score first." };

        int start = Math.Min(@params.SelectionStart, @params.SelectionEnd);
        int end = Math.Max(@params.SelectionStart, @params.SelectionEnd);
        var names = LilySharp.Core.Harmony.KeyEstimator.SectionsAt(tree.GetRoot(), start, end);
        if (names.Count == 0)
            return new GuessSectionKeyResponse { Error = "Put the caret in a section (or select sections) to guess its key." };

        var sections = new List<SectionKeyGuess>();
        foreach (var name in names)
        {
            if (LilySharp.Core.Harmony.KeyEstimator.Estimate(tree, name) is not { } estimate)
                continue;
            sections.Add(new SectionKeyGuess
            {
                Section = name,
                CurrentKey = estimate.CurrentKey,
                Candidates = estimate.Likely
                    .Select(c => new KeyGuess { Key = c.Text, Score = c.Score }).ToArray(),
            });
        }
        return sections.Count == 0
            ? new GuessSectionKeyResponse { Error = $"No notes or chords to read in section {string.Join(", ", names)}." }
            : new GuessSectionKeyResponse { Sections = sections.ToArray() };
    }

    /// <summary>Writes the chosen key as the key a section starts in
    /// (<see cref="LilySharp.Core.Harmony.KeyEstimator.SetSectionKey"/>).</summary>
    [JsonRpcMethod("lilysharp/setSectionKey", UseSingleObjectParameterDeserialization = true)]
    public Task<SetSectionKeyResponse> SetSectionKeyAsync(SetSectionKeyParams @params, CancellationToken token)
        => OffDispatch(() => SetSectionKey(@params), token);

    public SetSectionKeyResponse SetSectionKey(SetSectionKeyParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new SetSectionKeyResponse { Error = DocumentNotFound };

        var text = LilySharp.Core.Harmony.KeyEstimator.SetSectionKey(doc.Text, @params.Section, @params.Key);
        return text != null
            ? new SetSectionKeyResponse { NewText = text }
            : new SetSectionKeyResponse { Error = $"Could not write key {@params.Key} into section {@params.Section}." };
    }

    /// <summary>What "Write a Harmony Part…" asks before it writes: the file's parts, the one
    /// the caret is in, its sections, and the names a new part cannot take.</summary>
    [JsonRpcMethod("lilysharp/harmonyPartChoices", UseSingleObjectParameterDeserialization = true)]
    public Task<HarmonyPartChoicesResponse> HarmonyPartChoicesAsync(HarmonyPartChoicesParams @params, CancellationToken token)
        => OffDispatch(() => HarmonyPartChoices(@params), token);   // (it walks the tree: off the dispatch thread, as every other request)

    public HarmonyPartChoicesResponse HarmonyPartChoices(HarmonyPartChoicesParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new HarmonyPartChoicesResponse();
        var root = doc.Tree.GetRoot();
        var parts = root.ChildNodes().OfType<PartDeclarationSyntax>().Where(p => !p.IsUnnamed && !p.IsStrokes)
            .Select(p => p.Name.Text).Distinct(StringComparer.Ordinal).ToArray();
        string? at = null;
        for (var n = doc.Tree.FindNode(@params.Offset); n != null && at == null; n = n.Parent)
            at = n switch { PartBlockSyntax block => block.Name, PartDeclarationSyntax part => part.Name.Text, _ => null };
        return new HarmonyPartChoicesResponse
        {
            Parts = parts,
            Part = at != null && parts.Contains(at) ? at : null,
            Sections = root.DescendantNodes<SectionDeclarationSyntax>().Select(s => s.SectionName)
                .Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToArray(),
            Taken = [.. parts, .. LilySharp.Core.Music.LyricSinger.LinesOf(root).Select(l => l.Person).OfType<string>().Distinct(StringComparer.Ordinal)],
        };
    }

    /// <summary>Writes a harmony out as a part: a singer's notes, or a part's an interval away
    /// (<see cref="LilySharp.Core.Editing.HarmonyPartWriter"/>).</summary>
    [JsonRpcMethod("lilysharp/harmonyPart", UseSingleObjectParameterDeserialization = true)]
    public Task<HarmonyPartResponse> HarmonyPartAsync(HarmonyPartParams @params, CancellationToken token)
        => OffDispatch(() => HarmonyPart(@params), token);

    public HarmonyPartResponse HarmonyPart(HarmonyPartParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new HarmonyPartResponse { Error = DocumentNotFound };
        var result = @params.Singer is { Length: > 0 } singer
            ? LilySharp.Core.Editing.HarmonyPartWriter.WriteSinger(doc.Text, singer)
            : LilySharp.Core.Editing.HarmonyPartWriter.WritePart(doc.Text, @params.From ?? "", @params.Name ?? "",
                @params.Interval, @params.Octaves, @params.Sections);
        return new HarmonyPartResponse { NewText = result.Text, Error = result.Error };
    }

    /// <summary>The respellings "Respell Notes to Key" would make in a section
    /// (<see cref="LilySharp.Core.Editing.KeyRespeller.Plan"/>).</summary>
    [JsonRpcMethod("lilysharp/planRespell", UseSingleObjectParameterDeserialization = true)]
    public Task<PlanRespellResponse> PlanRespellAsync(RespellParams @params, CancellationToken token)
        => OffDispatch(() => PlanRespell(@params), token);

    public PlanRespellResponse PlanRespell(RespellParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new PlanRespellResponse();
        return new PlanRespellResponse
        {
            Changes = LilySharp.Core.Editing.KeyRespeller.Plan(doc.Text, @params.Section, @params.Key)
                .Select(c => new RespellChange { Id = c.Id, From = c.From, To = c.To, Count = c.Count })
                .ToArray(),
        };
    }

    /// <summary>Respells a section's notes to a key, every note keeping its pitch
    /// (<see cref="LilySharp.Core.Editing.KeyRespeller.Respell"/>).</summary>
    [JsonRpcMethod("lilysharp/respellToKey", UseSingleObjectParameterDeserialization = true)]
    public Task<RespellToKeyResponse> RespellToKeyAsync(RespellParams @params, CancellationToken token)
        => OffDispatch(() => RespellToKey(@params), token);

    public RespellToKeyResponse RespellToKey(RespellParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new RespellToKeyResponse { Error = DocumentNotFound };
        var result = LilySharp.Core.Editing.KeyRespeller.Respell(
            doc.Text, @params.Section, @params.Key, @params.Accepted);
        return new RespellToKeyResponse { NewText = result.NewText, Error = result.Error, Changed = result.ChangedPitches };
    }

    [JsonRpcMethod("lilysharp/convertOctaves", UseSingleObjectParameterDeserialization = true)]
    public Task<ConvertOctavesResponse> ConvertOctavesAsync(ConvertOctavesParams @params, CancellationToken token)
        => OffDispatch(() => ConvertOctaves(@params), token);

    /// <summary>The whole document rewritten into the other octave mode
    /// (<see cref="LilySharp.Core.Editing.OctaveModeConverter"/>, which compiles the result and
    /// compares every note before it answers).</summary>
    public ConvertOctavesResponse ConvertOctaves(ConvertOctavesParams @params)
    {
        string? text = @params.Text
            ?? (@params.TextDocument is { } id ? _documentManager.GetDocument(id.Uri)?.Text : null);
        if (text == null)
            return new ConvertOctavesResponse { Success = false, Error = DocumentNotFound };

        if (@params.DropPartOctaves)
        {
            var plain = LilySharp.Core.Editing.OctaveModeConverter.WithoutPartOctaves(text);
            if (plain.NewText == null)
                return new ConvertOctavesResponse { Success = false, Error = plain.Error };
            text = plain.NewText;
        }
        string? notes = null;
        if (@params.DropConcertPitch)
        {
            var written = LilySharp.Core.Editing.OctaveModeConverter.WithoutConcertPitch(text);
            if (written.NewText != null)
                text = written.NewText;
            else
                notes = written.Error;
        }
        var result = LilySharp.Core.Editing.OctaveModeConverter.Convert(text,
            @params.Absolute ? LilySharp.Core.Editing.OctaveMode.Absolute : LilySharp.Core.Editing.OctaveMode.Relative);
        return result.NewText != null
            ? new ConvertOctavesResponse { Success = true, NewText = result.NewText, ChangedNotes = result.ChangedPitches, Notes = notes }
            : new ConvertOctavesResponse { Success = false, Error = result.Error, Notes = notes };
    }

    /// <summary>The voicings of a chord a player can hold on a string instrument, in a register
    /// (<see cref="LilySharp.Core.Editing.ChordVoicingFinder"/>; owner's request 2026-10-30): the
    /// composing chat's tool, asked before it writes a chord.</summary>
    [JsonRpcMethod("lilysharp/chordVoicings", UseSingleObjectParameterDeserialization = true)]
    public Task<ChordVoicingsResponse> ChordVoicingsAsync(ChordVoicingsParams @params, CancellationToken token)
        => OffDispatch(() => new ChordVoicingsResponse
        {
            Text = @params.Chords is { Length: > 0 } chords
                ? LilySharp.Core.Editing.ChordVoicingFinder.DescribeProgression(chords, @params.Instrument ?? "guitar",
                    @params.Low, @params.High, @params.Sound, @params.Strings, @params.OmitRoot == true,
                    @params.FromFret is int from and > 0 ? from : 0)
                : LilySharp.Core.Editing.ChordVoicingFinder.Describe(@params.Chord ?? "", @params.Instrument ?? "guitar",
                    @params.Low, @params.High, @params.Count is int n and > 0 ? n : 6, @params.Sound, @params.Strings,
                    @params.OmitRoot == true, @params.FromFret is int fret and > 0 ? fret : 0),
        }, token);

    [JsonRpcMethod("lilysharp/aiOctaves", UseSingleObjectParameterDeserialization = true)]
    public Task<AiOctavesResponse> AiOctavesAsync(AiOctavesParams @params, CancellationToken token)
        => OffDispatch(() => AiOctaves(@params), token);

    /// <summary>The AI transform's octave round trip
    /// (<see cref="LilySharp.Core.Editing.OctaveModeConverter.ToAbsoluteForAi"/> and
    /// <see cref="LilySharp.Core.Editing.OctaveModeConverter.FromAbsoluteForAi"/>): a text, not
    /// the document — the candidate is never in the editor.</summary>
    public AiOctavesResponse AiOctaves(AiOctavesParams @params)
    {
        var text = @params.Text ?? "";
        if (@params.ToAbsolute)
        {
            var abs = LilySharp.Core.Editing.OctaveModeConverter.ToAbsoluteForAi(text);
            return new AiOctavesResponse
            {
                NewText = abs.NewText,
                Error = abs.Error,
                Changes = abs.Changes.Select(c => new TextChangeDto { Start = c.Start, End = c.End, Text = c.Text }).ToArray(),
            };
        }
        var back = LilySharp.Core.Editing.OctaveModeConverter.FromAbsoluteForAi(text, @params.Original ?? "");
        return new AiOctavesResponse { NewText = back.NewText, Error = back.Error };
    }

    [JsonRpcMethod("lilysharp/removeStringNumbers", UseSingleObjectParameterDeserialization = true)]
    public Task<RemoveStringNumbersResponse> RemoveStringNumbersAsync(RemoveStringNumbersParams @params, CancellationToken token)
        => OffDispatch(() => RemoveStringNumbers(@params), token);

    /// <summary>The whole document without its written string numbers
    /// (<see cref="LilySharp.Core.Editing.StringNumberRemover"/>).</summary>
    public RemoveStringNumbersResponse RemoveStringNumbers(RemoveStringNumbersParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new RemoveStringNumbersResponse { Error = DocumentNotFound };
        var result = LilySharp.Core.Editing.StringNumberRemover.Remove(doc.Text);
        return new RemoveStringNumbersResponse { NewText = result.NewText, Removed = result.Removed };
    }

    [JsonRpcMethod("lilysharp/shapeToNotes", UseSingleObjectParameterDeserialization = true)]
    public Task<ShapeToNotesResponse> ShapeToNotesAsync(ShapeToNotesParams @params, CancellationToken token)
        => OffDispatch(() => ShapeToNotes(@params), token);

    /// <summary>The <c>chord(…)</c> / <c>diagram(…)</c> item at the offset written out as the
    /// <c>&lt;…&gt;</c> chord it plays (<see cref="LilySharp.Core.Editing.ShapeChordConverter"/>):
    /// the span to replace and its text, or why not.</summary>
    public ShapeToNotesResponse ShapeToNotes(ShapeToNotesParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new ShapeToNotesResponse { Error = DocumentNotFound };
        var r = LilySharp.Core.Editing.ShapeChordConverter.ToNotes(doc.Text, @params.Offset);
        return new ShapeToNotesResponse { Start = r.Start, End = r.End, Replacement = r.Replacement, Error = r.Error };
    }

    [JsonRpcMethod("lilysharp/addChordDiagram", UseSingleObjectParameterDeserialization = true)]
    public Task<ShapeToNotesResponse> AddChordDiagramAsync(ShapeToNotesParams @params, CancellationToken token)
        => OffDispatch(() => AddChordDiagram(@params), token);

    /// <summary>The <c>&lt;…&gt;</c> chord at the offset with its <c>@diagram(SHAPE)</c> written
    /// (<see cref="LilySharp.Core.Editing.ShapeChordConverter.AddDiagram"/>): the span to replace
    /// (empty for an insertion) and its text, or why not. The same shapes as lilysharp/shapeToNotes.</summary>
    public ShapeToNotesResponse AddChordDiagram(ShapeToNotesParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new ShapeToNotesResponse { Error = DocumentNotFound };
        var r = LilySharp.Core.Editing.ShapeChordConverter.AddDiagram(doc.Text, @params.Offset);
        return new ShapeToNotesResponse { Start = r.Start, End = r.End, Replacement = r.Replacement, Error = r.Error };
    }

    [JsonRpcMethod("lilysharp/notesToChord", UseSingleObjectParameterDeserialization = true)]
    public Task<ShapeToNotesResponse> NotesToChordAsync(ShapeToNotesParams @params, CancellationToken token)
        => OffDispatch(() => NotesToChord(@params), token);

    /// <summary>The <c>&lt;…&gt;</c> chord at the offset written as the <c>chord(…)</c> item that
    /// sounds it (<see cref="LilySharp.Core.Editing.ShapeChordConverter.ToChordItem"/>): the span
    /// to replace and its text, or why not. The same shapes as lilysharp/shapeToNotes.</summary>
    public ShapeToNotesResponse NotesToChord(ShapeToNotesParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new ShapeToNotesResponse { Error = DocumentNotFound };
        var r = LilySharp.Core.Editing.ShapeChordConverter.ToChordItem(doc.Text, @params.Offset);
        return new ShapeToNotesResponse { Start = r.Start, End = r.End, Replacement = r.Replacement, Error = r.Error };
    }

    /// <summary>"Join Sections" (<see cref="LilySharp.Core.Editing.SectionJoiner"/>, which checks
    /// the piece sounds as it did before it answers): the sections named, else the ones the
    /// selection touches (one: it and the next); the rewritten document and its account, or the
    /// refusal.</summary>
    [JsonRpcMethod("lilysharp/joinSections", UseSingleObjectParameterDeserialization = true)]
    public Task<JoinSectionsResponse> JoinSectionsAsync(JoinSectionsParams @params, CancellationToken token)
        => OffDispatch(() =>
        {
            var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
            if (doc == null)
                return new JoinSectionsResponse { Error = DocumentNotFound };
            var names = @params.Sections is { Length: >= 2 } given ? given
                : LilySharp.Core.Editing.SectionJoiner.SectionsAt(doc.Tree, @params.Start, @params.End);
            if (names == null)
                return new JoinSectionsResponse { Error = "Put the caret on a section with one after it, or select the sections to join." };
            var r = LilySharp.Core.Editing.SectionJoiner.Join(doc.Text, names);
            return new JoinSectionsResponse { Success = r.NewText != null, NewText = r.NewText, Plan = r.Plan, Error = r.Error, Version = doc.Version };
        }, token);

    /// <summary>"Join Bars in Pairs" (<see cref="LilySharp.Core.Editing.BarJoiner"/>, which checks
    /// every note plays where it did, the joined bars' at half their place): the bars of a chords row
    /// the selection touches; the rewritten document, its account and the bars played that were
    /// joined (for the file beside a chart), or the refusal.</summary>
    [JsonRpcMethod("lilysharp/joinBars", UseSingleObjectParameterDeserialization = true)]
    public Task<JoinBarsResponse> JoinBarsAsync(JoinBarsParams @params, CancellationToken token)
        => OffDispatch(() =>
        {
            var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
            if (doc == null)
                return new JoinBarsResponse { Error = DocumentNotFound };
            var r = LilySharp.Core.Editing.BarJoiner.Join(doc.Text, @params.Start, @params.End, @params.Score);
            return new JoinBarsResponse
            {
                Success = r.NewText != null, NewText = r.NewText, Plan = r.Plan, Error = r.Error,
                Played = r.Played?.ToArray(), BarsPlayed = r.BarsPlayed, Version = doc.Version,
            };
        }, token);

    /// <summary>"Split Section Here" (<see cref="LilySharp.Core.Editing.SectionCutter"/>, which checks the
    /// piece sounds as it did): with no name, where the caret cuts and a free name to offer; with one,
    /// the rewrite or the refusal.</summary>
    [JsonRpcMethod("lilysharp/cutSection", UseSingleObjectParameterDeserialization = true)]
    public Task<CutSectionResponse> CutSectionAsync(CutSectionParams @params, CancellationToken token)
        => OffDispatch(() =>
        {
            var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
            if (doc == null)
                return new CutSectionResponse { Error = DocumentNotFound };
            string? section = @params.Section;
            int bars = @params.Bars;
            if (section == null || bars <= 0)
            {
                int offset = Math.Clamp(@params.Offset, 0, doc.Text.Length);
                if (@params.Snap)
                {
                    // On to the bar line after the bar's text, and past it.
                    while (offset < doc.Text.Length && char.IsWhiteSpace(doc.Text[offset])) offset++;
                    while (offset < doc.Text.Length && doc.Text[offset] is '|' or '.' or ':') offset++;
                }
                if (LilySharp.Core.Editing.SectionCutter.PlaceAt(doc.Tree, offset) is not { } place)
                    return new CutSectionResponse
                    {
                        Error = LilySharp.Core.Editing.SectionCutter.SectionAt(doc.Tree, offset) is { } inSection
                            ? $"Section {inSection} has no bar after this point (or none before it) — there is nothing to make a new section of here. Put the caret on a bar line between two of its bars."
                            : "Put the caret on a bar line inside a section — the section is cut there.",
                    };
                (section, bars) = place;
            }
            if (string.IsNullOrWhiteSpace(@params.Name))
                return new CutSectionResponse { Section = section, Bars = bars, Version = doc.Version,
                    Suggested = LilySharp.Core.Editing.SectionCutter.SuggestName(doc.Tree, section) };
            var r = LilySharp.Core.Editing.SectionCutter.Cut(doc.Text, section, bars, @params.Name.Trim());
            return new CutSectionResponse { Success = r.NewText != null, NewText = r.NewText, Plan = r.Plan, Error = r.Error,
                Section = section, Bars = bars, Version = doc.Version };
        }, token);

    [JsonRpcMethod("lilysharp/splitSections", UseSingleObjectParameterDeserialization = true)]
    public Task<SplitSectionsResponse> SplitSectionsAsync(SplitSectionsParams @params, CancellationToken token)
        => OffDispatch(() => SplitSections(@params), token);

    /// <summary>"Split Sections to Match a Part" (<see cref="LilySharp.Core.Editing.SectionSplitter"/>,
    /// which compiles the result and compares every cut part's sound before it answers): the
    /// rewritten document and the plan the editor shows for confirmation, or the choice of part
    /// to follow when the candidates disagree (grouped: the parts that subdivide alike are one
    /// choice), or the refusal.</summary>
    public SplitSectionsResponse SplitSections(SplitSectionsParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new SplitSectionsResponse { Success = false, Error = DocumentNotFound };

        var result = LilySharp.Core.Editing.SectionSplitter.Split(doc.Text, @params.Section, @params.Reference);
        if (result.NewText != null)
            return new SplitSectionsResponse { Success = true, NewText = result.NewText, Plan = result.Plan };
        if (result.Offer is { } offer)
            return new SplitSectionsResponse
            {
                Success = false,
                Section = offer.Section,
                Choices = offer.Candidates
                    .GroupBy(c => c.Describe())
                    .Select(g => new SplitSectionsChoice
                    {
                        Part = g.First().Part,
                        Parts = string.Join(", ", g.Select(c => c.Part)),
                        Description = g.Key,
                    })
                    .ToArray(),
            };
        return new SplitSectionsResponse { Success = false, Error = result.Error };
    }

    /// <summary>
    /// Converts the document between the by-section and by-part authoring
    /// layouts (the editor command toggles whichever the file currently uses) and
    /// returns the rewritten source; the extension applies it as a full-document edit.
    /// </summary>
    [JsonRpcMethod("lilysharp/regroup", UseSingleObjectParameterDeserialization = true)]
    public Task<RegroupResponse> RegroupAsync(RegroupParams @params, CancellationToken token)
        => OffDispatch(() => Regroup(@params), token);

    public RegroupResponse Regroup(RegroupParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new RegroupResponse { Success = false, Error = DocumentNotFound };

        // Refuse to convert a file with syntax errors: cell extraction needs a
        // clean, balanced tree, and the client overwrites the whole document with
        // the result — so a malformed file would be mangled. Leave it untouched.
        if (doc.Tree.HasErrors)
            return new RegroupResponse
            {
                Success = false,
                Error = "Fix the syntax errors before regrouping — no changes made."
            };

        var from = LilySharp.Core.Editing.PartSectionRegrouper.Detect(doc.Text);
        if (from == LilySharp.Core.Editing.Grouping.Unknown)
            return new RegroupResponse
            {
                Success = false,
                Error = "Nothing to regroup — the file needs parts with sections."
            };

        // Chord/lyric blocks only exist in the by-section grouping; converting to
        // by-part would drop them. Explain and keep the file unchanged.
        if (from == LilySharp.Core.Editing.Grouping.BySection
            && LilySharp.Core.Editing.PartSectionRegrouper.HasUntransposableSectionContent(doc.Text))
            return new RegroupResponse
            {
                Success = false,
                Error = "This file has chords/lyrics blocks, which exist only in a file grouped "
                    + "by section. Regrouping by part would drop them, so it was left unchanged."
            };

        // Convert self-guards: it returns null unless the result round-trips to a
        // clean parse, so this can never produce a corrupt document.
        var newText = LilySharp.Core.Editing.PartSectionRegrouper.Convert(doc.Text, out var collision);
        // A cell written twice: the other layout has room for one, so converting would
        // silently keep the later and drop the earlier.
        if (collision != null)
            return new RegroupResponse
            {
                Success = false,
                Error = $"{char.ToUpperInvariant(collision[0])}{collision[1..]} is written twice. "
                    + "Regrouping would keep only the later one, so the file was left unchanged."
            };
        if (newText == null)
            return new RegroupResponse
            {
                Success = false,
                Error = "Regrouping would not produce a clean result — no changes made."
            };

        var to = from == LilySharp.Core.Editing.Grouping.ByPart
            ? LilySharp.Core.Editing.Grouping.BySection
            : LilySharp.Core.Editing.Grouping.ByPart;
        // The words the editor shows ("regrouped by section"), not the enum names.
        static string Words(LilySharp.Core.Editing.Grouping form)
            => form == LilySharp.Core.Editing.Grouping.ByPart ? "by part" : "by section";
        return new RegroupResponse
        {
            Success = true,
            NewText = newText,
            FromGrouping = Words(from),
            ToGrouping = Words(to),
        };
    }

    /// <summary>Every <c>@or</c> mark of the document (its span, '@' to ')'), for the editor's Remove
    /// All @or (owner's request 2026-11-17; <see cref="NoteStepper.OrMarks"/>).</summary>
    [JsonRpcMethod("lilysharp/orMarks", UseSingleObjectParameterDeserialization = true)]
    public Task<OrMarksResponse> OrMarksAsync(TodosParams @params, CancellationToken token)
        => OffDispatch(() =>
        {
            var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
            if (doc == null)
                return new OrMarksResponse { Error = DocumentNotFound };
            return new OrMarksResponse
            {
                Marks = [.. NoteStepper.OrMarks(doc.Tree).Select(m => new StepSelection { Start = m.Start, End = m.End })],
                Version = doc.Version,
            };
        }, token);

    /// <summary>
    /// The document's <c>@todo</c> marks with the bar and the part the score draws each in
    /// (LilySharp-Omr proposal C1): the editor's list of marks, and the way from a mark — or
    /// the caret on one — to the bar of the scanned page. See <see cref="TodoIndex"/>.
    /// </summary>
    [JsonRpcMethod("lilysharp/todos", UseSingleObjectParameterDeserialization = true)]
    public Task<TodosResponse> TodosAsync(TodosParams @params, CancellationToken token)
        => OffDispatch(() => Todos(@params), token);

    public TodosResponse Todos(TodosParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new TodosResponse { Error = DocumentNotFound };
        try
        {
            return new TodosResponse
            {
                // The places only once a mark is found (TodoIndex.Find): the preview asks with
                // every refresh, and a file with no mark must not pay a collect of every score.
                Todos = TodoIndex.Find(doc.Tree, () => PlacesOf(@params.TextDocument.Uri, doc)).Select(t => new TodoInfo
                {
                    Key = t.Key, Memo = t.Memo, Start = t.Start, End = t.End,
                    HostStart = t.HostStart, HostEnd = t.HostEnd, Measure = t.Measure, Part = t.Part,
                }).ToArray(),
                Version = doc.Version,
            };
        }
        catch (Exception ex)
        {
            return new TodosResponse { Error = ex.Message, Version = doc.Version };
        }
    }

    /// <summary>
    /// The printed bar and the part of the item at a caret (LilySharp-Omr proposal C1's second
    /// request). See <see cref="TodoIndex.PlaceAt"/>. ⚠️ The PRINTED bar: after a bar written
    /// too long it is one more than the bar a reader counts in the text — the scan view goes by
    /// the line its reader wrote a bar on instead (editors/vscode omrScanCore.anchorsOf).
    /// </summary>
    [JsonRpcMethod("lilysharp/placeAt", UseSingleObjectParameterDeserialization = true)]
    public Task<PlaceAtResponse> PlaceAtAsync(PlaceAtParams @params, CancellationToken token)
        => OffDispatch(() => PlaceAt(@params), token);

    public PlaceAtResponse PlaceAt(PlaceAtParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new PlaceAtResponse();
        try
        {
            var places = PlacesOf(@params.TextDocument.Uri, doc);
            var place = TodoIndex.PlaceAt(places, doc.Text, @params.Offset);
            return new PlaceAtResponse
            {
                Measure = place?.Measure, Part = place?.Part, Version = doc.Version,
                Places = @params.Offsets?.Select(o => TodoIndex.PlaceAt(places, doc.Text, o) is { } p
                    ? new PlaceInfo { Measure = p.Measure, Part = p.Part } : new PlaceInfo()).ToArray(),
            };
        }
        catch (Exception)
        {
            return new PlaceAtResponse { Version = doc.Version };
        }
    }

    /// <summary>
    /// The bar under a caret, in the other parts and chords rows that write its section — the
    /// editor's "go to the same bar in another part" (owner's request 2026-11-14).
    /// See <see cref="LilySharp.Core.Editing.SameBarFinder"/>.
    /// </summary>
    [JsonRpcMethod("lilysharp/sameBar", UseSingleObjectParameterDeserialization = true)]
    public Task<SameBarResponse> SameBarAsync(SameBarParams @params, CancellationToken token)
        => OffDispatch(() => SameBar(@params), token);

    public SameBarResponse SameBar(SameBarParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new SameBarResponse();
        try
        {
            var answer = LilySharp.Core.Editing.SameBarFinder.At(doc.Tree, @params.Offset);
            return answer == null
                ? new SameBarResponse { Version = doc.Version }
                : new SameBarResponse
                {
                    Section = answer.Section, Bar = answer.Bar, Voice = answer.Voice, Version = doc.Version,
                    Places = [.. answer.Others.Select(o => new SameBarPlace { Voice = o.Voice, Offset = o.Offset })],
                };
        }
        catch (Exception)
        {
            return new SameBarResponse { Version = doc.Version };
        }
    }

    /// <summary>
    /// Every voice of every section with its bars' text spans — the editor's "Report to
    /// Orecchiante" (Orecchiante's request 23, docs/orecchiante-report-design.md). See
    /// <see cref="LilySharp.Core.Editing.SameBarFinder.AllBars"/>.
    /// </summary>
    [JsonRpcMethod("lilysharp/voiceBars", UseSingleObjectParameterDeserialization = true)]
    public Task<VoiceBarsResponse> VoiceBarsAsync(VoiceBarsParams @params, CancellationToken token)
        => OffDispatch(() => VoiceBars(@params), token);

    public VoiceBarsResponse VoiceBars(VoiceBarsParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new VoiceBarsResponse { Version = -1 };
        try
        {
            var voices = LilySharp.Core.Editing.SameBarFinder.AllBars(doc.Tree);
            return new VoiceBarsResponse
            {
                Version = doc.Version,
                Voices = [.. voices.Select(v => new VoiceBarsInfo
                {
                    Section = v.Section, Voice = v.Voice,
                    Bars = [.. v.Bars.Select(b => new BarSpan { Start = b.Start, End = b.End })],
                })],
            };
        }
        catch (Exception)
        {
            return new VoiceBarsResponse { Version = doc.Version };
        }
    }

    /// <summary>
    /// A section's repeat signs written in every part that writes the section, or taken back to
    /// one — the editor's two commands (owner's design 2026-11-14, docs/inline-repeat-design.md
    /// §8). See <see cref="LilySharp.Core.Editing.RepeatSignSpreader"/>.
    /// </summary>
    [JsonRpcMethod("lilysharp/spreadRepeatSigns", UseSingleObjectParameterDeserialization = true)]
    public Task<SpreadRepeatSignsResponse> SpreadRepeatSignsAsync(SpreadRepeatSignsParams @params, CancellationToken token)
        => OffDispatch(() => SpreadRepeatSigns(@params), token);

    public SpreadRepeatSignsResponse SpreadRepeatSigns(SpreadRepeatSignsParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new SpreadRepeatSignsResponse();
        try
        {
            var result = @params.Reverse
                ? LilySharp.Core.Editing.RepeatSignSpreader.KeepInOnePart(doc.Tree)
                : LilySharp.Core.Editing.RepeatSignSpreader.WriteInEveryPart(doc.Tree);
            return new SpreadRepeatSignsResponse
            {
                Edits = [.. result.Edits.Select(e => new SpreadRepeatSignsEdit { Start = e.Start, End = e.End, NewText = e.NewText })],
                Count = result.Count, Skipped = [.. result.Skipped], Version = doc.Version,
            };
        }
        catch (Exception)
        {
            return new SpreadRepeatSignsResponse { Version = doc.Version };
        }
    }

    // The places of the last tree asked of: the caret asks on every move, and a collect per
    // move would be the preview's whole cost again. The tree is the one the piece is drawn
    // from (ExpandUsings: the same instance while the text and what its `using`s read stand;
    // the document's text is its prefix, so the document's positions are its own). Until
    // session 1296 it was the document's own tree, kept by version: in a book with `using`s
    // the bars of a text the page never draws (a mark after an included section stood in
    // bar 1), and an include saved meanwhile changed nothing.
    private readonly object _placesLock = new();
    private (SyntaxTree Tree, IReadOnlyList<ItemPlace> Places)? _places;

    /// <summary>How many times the places were collected (<see cref="TodoIndex.Places"/>) — for tests.</summary>
    internal int PlacesCollects => System.Threading.Volatile.Read(ref _placesCollects);

    private int _placesCollects;

    private IReadOnlyList<ItemPlace> PlacesOf(Uri uri, Document doc)
    {
        var (tree, _) = ExpandUsings(doc, uri);
        lock (_placesLock)
            if (_places is { } cached && ReferenceEquals(cached.Tree, tree))
                return cached.Places;
        System.Threading.Interlocked.Increment(ref _placesCollects);
        var places = TodoIndex.Places(tree);
        lock (_placesLock)
            _places = (tree, places);
        return places;
    }
}
