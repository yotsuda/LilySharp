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

using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// Puts a MUSIC FRAGMENT into the smallest complete document, for the many tests whose
/// subject is the music grammar rather than the file structure. Music at the top level is
/// a parse error (LYS0020), so a fragment cannot be its own file.
/// </summary>
/// <remarks>
/// ★ The wrapper is GEOMETRY-NEUTRAL — measured, not assumed: rendering <c>c4 d e f |</c>
/// bare and wrapped gives byte-identical coordinates (every staff line, glyph X/Y, stem and
/// barline rect), the only diff being <c>data-pos</c>, which is a source offset and moves
/// because the source text did. Two things earn that:
/// <list type="bullet">
/// <item><c>form main { ~A }</c> — the <c>~</c> suppresses the section label, whose box
/// otherwise sits above the staff and pushes everything down 2.66.</item>
/// <item>No display name and no <c>instrument</c>, so no staff name and no indent: X is
/// untouched.</item>
/// </list>
/// ⚠️ Leading <c>clef</c> / <c>key</c> / <c>time</c> / <c>tempo</c> belong OUTSIDE the part
/// (pass them in <paramref name="fileDefaults"/>), not in the part header. The relative
/// octave anchor comes from the FILE-level clef, so moving a <c>clef bass</c> into the part
/// header silently transposes the music up an octave (c → C4 instead of C3).
/// </remarks>
internal static class MusicSource
{
    /// <summary>The music fragment as the body of a lone part's lone section.</summary>
    /// <param name="music">A note stream — what would once have been written bare.</param>
    /// <param name="fileDefaults">Optional top-level directives (<c>clef bass</c>,
    /// <c>key g major</c>, <c>time 3/4</c>, <c>octave absolute</c>, …), one per line.</param>
    /// <remarks>
    /// The music gets its OWN lines. Folding it inline (<c>melody { … } }</c> on one line)
    /// works until the fragment ends in a <c>//</c> comment, which then eats the closing
    /// braces — every commented example in the language spec failed exactly that way.
    /// </remarks>
    public static string Wrap(string music, string fileDefaults = "")
        => (fileDefaults.Length > 0 ? fileDefaults.TrimEnd() + "\n" : "")
            + "part melody\n"
            + "section A { melody {\n"
            + music + "\n"
            + "} }\n"
            + "form main { ~A }\n"
            + "score main { staff melody }\n";

    /// <summary>Parses a music fragment in the minimal document. Use
    /// <c>tree.GetNodes&lt;T&gt;()</c> to reach the music — it is no longer at a root slot.</summary>
    public static SyntaxTree Parse(string music, string fileDefaults = "")
        => SyntaxTree.Parse(Wrap(music, fileDefaults));
}
