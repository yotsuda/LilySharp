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

using LilySharp.Lsp.Protocol;

namespace LilySharp.Lsp;

// Request/response DTOs for the custom lilysharp/* LSP methods, moved out of
// LilySharpLanguageServer.cs (which also had ~190 dead trailing blank lines).

/// <summary>
/// Parameters for lilysharp/svg request.
/// </summary>
public class SvgParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
    /// <summary>
    /// Optional render name to select which render block to use.
    /// If null, returns the first score render or default preview.
    /// </summary>
    public string? RenderName { get; set; }
}

/// <summary>
/// Response for lilysharp/svg request.
/// </summary>
public class SvgResponse
{
    public string? Svg { get; set; }
    public string? Error { get; set; }
    /// <summary>
    /// List of available render definitions in the document.
    /// </summary>
    public RenderInfo[]? Renders { get; set; }
    /// <summary>
    /// True when a newer lilysharp/svg request for the same (document, render name)
    /// arrived while this one was queued: no render was performed, Svg and Error are
    /// both null, and the newer request's response carries the picture. The client
    /// drops this response instead of painting it.
    /// </summary>
    public bool Superseded { get; set; }
}

/// <summary>
/// Information about a render definition.
/// </summary>
public class RenderInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";  // "score" or "audio"
    public string Filename { get; set; } = "";
}

/// <summary>Result of lilysharp/addChordTrack: the edits that add the chords part
/// (empty/absent when <see cref="Error"/> explains why none were produced).</summary>
public class AddChordTrackResponse
{
    public ChordTrackEdit[]? Edits { get; set; }
    public string? Error { get; set; }
    /// <summary>An optional note shown to the user (e.g. that the layout was converted).</summary>
    public string? Info { get; set; }
}

/// <summary>A single insertion for the add-chord-track edit (0-based line/char).</summary>
public class ChordTrackEdit
{
    public int StartLine { get; set; }
    public int StartChar { get; set; }
    public int EndLine { get; set; }
    public int EndChar { get; set; }
    public string NewText { get; set; } = "";
}

/// <summary>
/// Parameters for the lilysharp/export request.
/// </summary>
public class ExportParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
    /// <summary>Output format: svg, png, pdf, midi, or musicxml.</summary>
    public string? Format { get; set; }
    /// <summary>Absolute path to write the exported file to.</summary>
    public string OutputPath { get; set; } = "";
    /// <summary>Score to export (visual formats); null = first/default score.</summary>
    public string? RenderName { get; set; }
}

/// <summary>
/// Response for the lilysharp/export request.
/// </summary>
public class ExportResponse
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
}

/// <summary>Parameters for the lilysharp/playback request.</summary>
public class PlaybackParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
}

/// <summary>One playable note, in SECONDS (tempo map already applied).</summary>
public class PlaybackNote
{
    public double T { get; set; }   // onset (s)
    public double D { get; set; }   // duration (s)
    public double P { get; set; }   // MIDI pitch (fractional for quarter tones)
    public int V { get; set; }      // velocity 0-127
    public int S { get; set; }      // source offset (-1 = none) for follow-along highlight
    public int O { get; set; }      // printed-copy ordinal of this onset among same-S copies
    public int I { get; set; }      // timbre family for the preview synth
}

/// <summary>Response for the lilysharp/playback request.</summary>
public class PlaybackResponse
{
    public PlaybackNote[]? Notes { get; set; }
    public string? Error { get; set; }
}

/// <summary>Parameters for the lilysharp/convertLayout request.</summary>
public class ConvertLayoutParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
}

/// <summary>Response for the lilysharp/convertLayout request: the rewritten source
/// plus which layout it went from / to (for a status message).</summary>
public class ConvertLayoutResponse
{
    public bool Success { get; set; }
    public string? NewText { get; set; }
    public string? FromLayout { get; set; }
    public string? ToLayout { get; set; }
    public string? Error { get; set; }
}

/// <summary>Parameters for lilysharp/extractPhrase: the caret (or a selection —
/// snapped outward to whole measures) and the name for the extracted phrase.
/// Offsets are 0-based character offsets into the document text.</summary>
public class ExtractPhraseParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
    public int SelectionStart { get; set; }
    public int SelectionEnd { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Response for lilysharp/extractPhrase: the rewritten source (the
/// refactoring is verified semantics-preserving server-side, or refused).</summary>
public class ExtractPhraseResponse
{
    public bool Success { get; set; }
    public string? NewText { get; set; }
    public string? Error { get; set; }
}

/// <summary>Parameters for lilysharp/importMusicXml. A <see cref="FilePath"/> is
/// preferred (handles a binary <c>.mxl</c>); <see cref="XmlText"/> is the fallback
/// when only raw XML is on hand.</summary>
public class ImportMusicXmlParams
{
    public string? FilePath { get; set; }
    public string? XmlText { get; set; }
    /// <summary>Emit compact relative-octave notes instead of explicit absolute.</summary>
    public bool RelativeOctave { get; set; }
}

/// <summary>Response for lilysharp/importMusicXml: the generated Lily# source, the
/// import-report warnings (dropped/approximated constructs), and an error if the
/// file could not be read or parsed.</summary>
public class ImportMusicXmlResponse
{
    public string? Lys { get; set; }
    public string[] Warnings { get; set; } = System.Array.Empty<string>();
    public string? Error { get; set; }
}

// ============================================================
// AI collaborative editing (docs/ai-collab-design): the extension asks the
// server to validate a candidate BEFORE showing it, render it non-destructively,
// and describe the resolved musical facts of a selection so the model isn't blind.
// None of these mutate the open document.
// ============================================================

/// <summary>Parameters for lilysharp/checkCandidate: an arbitrary candidate source
/// string (typically the open document with the selection replaced by the model's
/// output). The candidate is parsed and validated in isolation — it does NOT touch
/// document state.</summary>
public class CheckCandidateParams
{
    /// <summary>The full candidate source text to validate.</summary>
    public string Text { get; set; } = "";
}

/// <summary>One diagnostic from validating a candidate: 0-based line/char (for the
/// editor) plus the absolute source offset/length (so the client can attribute an
/// error to the candidate's replaced span rather than a pre-existing one).</summary>
public class CandidateDiagnostic
{
    public int Line { get; set; }
    public int Char { get; set; }
    public int Offset { get; set; }
    public int Length { get; set; }
    public string Severity { get; set; } = "error";  // error | warning | info | hint
    public string Message { get; set; } = "";
    public string? Code { get; set; }
}

/// <summary>Response for lilysharp/checkCandidate: every parser + semantic
/// diagnostic, and <see cref="HasErrors"/> as a quick gate. The validate-and-
/// self-repair loop feeds <see cref="Diagnostics"/> back to the model on failure so
/// a broken candidate is never shown to the user.</summary>
public class CheckCandidateResponse
{
    public bool HasErrors { get; set; }
    public CandidateDiagnostic[] Diagnostics { get; set; } = System.Array.Empty<CandidateDiagnostic>();
}

/// <summary>Parameters for lilysharp/renderText: render an arbitrary candidate
/// source string to preview SVG without touching the open document (the "decide on
/// the score" preview of §3/§7 — an offscreen compile of the candidate).</summary>
public class RenderTextParams
{
    public string Text { get; set; } = "";
    /// <summary>Optional render/score name; null = first score / default preview.</summary>
    public string? RenderName { get; set; }
}

/// <summary>Parameters for lilysharp/factsForRange: the resolved musical facts of a
/// selection, so the model sees what the compiler sees (§5). The range is given as
/// absolute source character offsets into the open document.</summary>
public class FactsForRangeParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
    /// <summary>Inclusive start offset (absolute character position) of the selection.</summary>
    public int Start { get; set; }
    /// <summary>Exclusive end offset (absolute character position) of the selection.</summary>
    public int End { get; set; }
}

/// <summary>One note's resolved absolute pitch within the selection: the written
/// token (e.g. <c>c''</c>), its resolved absolute pitch (e.g. <c>C6</c>), and its
/// source offset. Mirrors <c>check --pitches</c>.</summary>
public class ResolvedPitchFact
{
    public int Offset { get; set; }
    public string Written { get; set; } = "";
    public string Resolved { get; set; } = "";
}

/// <summary>Response for lilysharp/factsForRange: the resolved absolute pitches
/// inside the selection (the highest-value "un-blindfold" fact), plus an error if the
/// document could not be resolved.</summary>
public class FactsForRangeResponse
{
    public ResolvedPitchFact[] Pitches { get; set; } = System.Array.Empty<ResolvedPitchFact>();
    public string? Error { get; set; }
}
