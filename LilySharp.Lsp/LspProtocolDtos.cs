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

using Microsoft.VisualStudio.LanguageServer.Protocol;

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
