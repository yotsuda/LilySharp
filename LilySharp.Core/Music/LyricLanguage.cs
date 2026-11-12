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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// The language lyrics are written in (owner's decisions 2026-11-09, session 1167): a property of
/// the words, not of who sings them. A closed vocabulary of lower-case names — the editor
/// completes them, and a misspelling is an error (LYS4038) — on the file
/// (<c>language japanese</c>) and on a lyrics track (<c>lyrics en language english { … }</c>),
/// the track's winning. Nothing written: Japanese, as every file before this was read. The
/// engine's language tag (<c>ja</c>) never appears in a score; this table is the one place it is
/// made.
/// </summary>
public static class LyricLanguage
{
    /// <summary>The word that names a language. Contextual: a part or a track may be called it.</summary>
    public const string Keyword = "language";

    /// <summary>The tag of the language of a file that names none.</summary>
    public const string DefaultCode = "ja";

    private static readonly (string Name, string Code)[] Table =
    [
        ("japanese", "ja"),
        ("english", "en"),
        ("chinese", "zh"),
        ("korean", "ko"),
        // The commonest language of sacred choral music (asked for by an anthem written to
        // try the grammar, 2026-11-12). No engine Cantoria drives sings it: a voice asked to
        // is told so, as for any language no installed voice sings.
        ("latin", "la"),
    ];

    /// <summary>The names a score may write, in the order the editor offers them.</summary>
    public static IReadOnlyList<string> Names { get; } = Table.Select(t => t.Name).ToArray();

    /// <summary>The engine's tag for <paramref name="name"/>, or null when it is not a language name.</summary>
    public static string? CodeOf(string? name)
    {
        foreach (var (n, code) in Table)
            if (string.Equals(n, name, StringComparison.Ordinal))
                return code;
        return null;
    }

    /// <summary>
    /// A reader of the file under <paramref name="root"/>: the tag of a track's words — the first
    /// block of that name that writes a language (the rule <c>sings</c> has), else the file's last
    /// <c>language</c> line (as every repeated single-value setting), else <see cref="DefaultCode"/>.
    /// A name that is not a language (LYS4038) counts as unwritten.
    /// </summary>
    public static Func<string?, string> ReaderOf(SyntaxNode root)
    {
        string file = DefaultCode;
        foreach (var line in root.DescendantNodes<LanguageDeclarationSyntax>())
            if (CodeOf(line.Language) is { } code)
                file = code;
        var tracks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in root.DescendantNodes<LyricsBlockSyntax>())
            if (block.VoiceName is { } track && CodeOf(block.Language) is { } code)
                tracks.TryAdd(track, code);
        return track => track == GeneratedLyrics.Hum ? GeneratedLyrics.HumLanguage
            : track != null && tracks.TryGetValue(track, out var code) ? code : file;
    }
}
