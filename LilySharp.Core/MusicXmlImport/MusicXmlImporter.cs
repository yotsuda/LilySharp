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

namespace LilySharp.Core.MusicXmlImport;

/// <summary>
/// Imports MusicXML (<c>.xml</c> / <c>.musicxml</c> / <c>.mxl</c>) into Lily#
/// source. Import is an OPINIONATED, non-unique mapping — the result is a faithful,
/// idiomatic STARTING POINT that renders the same music (pitches, rhythm, measures,
/// key, chords for the covered tier), not a byte round-trip of a hand-written
/// <c>.lys</c>. Whatever cannot be represented is surfaced in the returned
/// <see cref="ImportReport"/>, never emitted wrong.
/// </summary>
public sealed class MusicXmlImporter
{
    /// <summary>Imports a MusicXML document string, returning the Lily# source and
    /// a report of everything dropped or approximated. <paramref name="relativeOctave"/>
    /// asks for compact relative-octave output (default: explicit absolute).</summary>
    public (string Lys, ImportReport Report) Import(string xmlText, bool relativeOctave = false)
    {
        var report = new ImportReport();
        var doc = MusicXmlReader.Read(xmlText, report);
        return (LysWriter.Write(doc, report, relativeOctave), report);
    }

    /// <summary>Imports raw bytes — an <c>.mxl</c> zip or a plain XML file —
    /// returning the Lily# source and its import report.</summary>
    public (string Lys, ImportReport Report) ImportBytes(byte[] bytes, bool relativeOctave = false)
    {
        var report = new ImportReport();
        if (MidiReader.IsMidi(bytes))
        {
            // A Standard MIDI File through the same model and writer (owner's request 2026-11-12).
            var midi = MidiReader.Read(bytes, report, out var singers);
            var sung = midi.Parts.Where(p => p.Measures.Any(m => m.PrimaryItems.OfType<ImportNote>().Any(n => n.Lyrics.Count > 0))).ToList();
            var text = new System.Text.StringBuilder(LysWriter.Write(midi, report, relativeOctave));
            // Who sings a track, where the file says (Lily#'s own .mid does, Midi.MidiForFile).
            foreach (var (part, voice) in singers)
                text.Append("singer ").Append(Music.LyricSinger.Genders.Contains(voice.ToLowerInvariant()) ? voice.ToLowerInvariant() : '"' + LysWriter.EscapeString(voice) + '"')
                    .Append(" on ").Append(part).Append(" sings words").Append(sung.Count > 0 && sung[0].SafeName == part ? "" : "_" + part).Append('\n');
            return (text.ToString(), report);
        }
        if (AbcReader.IsAbc(bytes))
        {
            // ABC notation through the same model and writer (owner's decision 2026-11-18,
            // docs/abc-import-design.md). One piece is one tune: the first, here; ImportPieces
            // gives every tune of a book.
            var tunes = AbcReader.SplitTunes(AbcReader.Decode(bytes));
            if (tunes.Count > 1)
                report.Warn($"the file holds {tunes.Count} tunes: the first was read.");
            var abc = AbcReader.Read(tunes[0].Text, report);
            return (LysWriter.Write(abc, report, relativeOctave), report);
        }
        var doc = MusicXmlReader.ReadBytes(bytes, report);
        return (LysWriter.Write(doc, report, relativeOctave), report);
    }

    /// <summary>
    /// Imports a file as the pieces it holds, each a <c>.lys</c> of its own: an ABC tune book gives
    /// one per tune (a Lily# file is one piece — its title, key, meter and form are the file's), named
    /// <c>001-speed-the-plough</c> after its <c>X:</c> number and title (owner's decision 2026-11-18).
    /// Any other file is one piece with no name. A tune that cannot be read is a piece with an
    /// <see cref="ImportedPiece.Error"/> and no source; the others still come back.
    /// </summary>
    public List<ImportedPiece> ImportPieces(byte[] bytes, bool relativeOctave = false)
    {
        if (!AbcReader.IsAbc(bytes))
        {
            var (lys, report) = ImportBytes(bytes, relativeOctave);
            return [new ImportedPiece(null, null, lys, report, null)];
        }
        var tunes = AbcReader.SplitTunes(AbcReader.Decode(bytes));
        var names = tunes.Count > 1 ? AbcReader.FileNames(tunes) : null;
        var pieces = new List<ImportedPiece>();
        for (int k = 0; k < tunes.Count; k++)
        {
            var report = new ImportReport();
            try
            {
                var doc = AbcReader.Read(tunes[k].Text, report);
                pieces.Add(new ImportedPiece(names?[k], tunes[k].Title, LysWriter.Write(doc, report, relativeOctave), report, null));
            }
            catch (Exception ex) when (tunes.Count > 1)
            {
                pieces.Add(new ImportedPiece(names![k], tunes[k].Title, null, report, ex.Message));
            }
        }
        return pieces;
    }
}

/// <summary>One piece of an imported file (<see cref="MusicXmlImporter.ImportPieces"/>).</summary>
/// <param name="Name">The file name it is written as, without <c>.lys</c>, when the file held several
/// pieces; null for a file that is one piece (written as the source's own name).</param>
/// <param name="Title">The piece's title as the source has it, or null.</param>
/// <param name="Lys">The Lily# source, or null when the piece could not be read.</param>
/// <param name="Report">What was dropped or approximated.</param>
/// <param name="Error">Why the piece could not be read, or null.</param>
public sealed record ImportedPiece(string? Name, string? Title, string? Lys, ImportReport Report, string? Error);
