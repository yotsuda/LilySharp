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
    /// a report of everything dropped or approximated.</summary>
    public (string Lys, ImportReport Report) Import(string xmlText)
    {
        var report = new ImportReport();
        var doc = MusicXmlReader.Read(xmlText, report);
        return (LysWriter.Write(doc, report), report);
    }

    /// <summary>Imports raw bytes — an <c>.mxl</c> zip or a plain XML file —
    /// returning the Lily# source and its import report.</summary>
    public (string Lys, ImportReport Report) ImportBytes(byte[] bytes)
    {
        var report = new ImportReport();
        var doc = MusicXmlReader.ReadBytes(bytes, report);
        return (LysWriter.Write(doc, report), report);
    }
}
