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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// Who sings a lyrics track (owner's decisions 2026-11-09, sessions 1166 and 1168): the string a
/// file writes after <c>singer</c> — a voice's own name (<c>"neutrino/MERROW"</c>, it has a
/// <c>/</c>) or a role a listener's settings cast (<c>"soprano"</c>). On the part, on the lyrics
/// track and on the score's row, the nearest winning, as <c>sings</c>. Lily# only carries the
/// string: what sings is the engine's to resolve (docs/singing-roadmap.md §4), the listener's
/// settings having the last say.
/// </summary>
public static class LyricSinger
{
    /// <summary>The word that names a singer. Contextual: a part or a track may be called it.</summary>
    public const string Keyword = "singer";

    /// <summary>The words that may stand between <c>singer</c> and its name (<c>singer male "neutrino/SOMA"</c>,
    /// owner's decision 2026-11-09, session 1173): which voices the editor offers for it. Lower-case, closed.</summary>
    public static readonly IReadOnlyList<string> Genders = ["male", "female"];

    /// <summary>The track name of the file's unnamed lyrics, for the language server's reading of a head.</summary>
    public const string UnnamedTrack = SyntaxFacts.UnnamedLyricsName;

    /// <summary>The text between a string token's quotes.</summary>
    public static string ValueOf(string token)
        => StringLiteral.IsClosed(token) ? StringLiteral.Value(token) : token.Trim('"');

    /// <summary>
    /// A reader of the file under <paramref name="root"/> for the score <paramref name="spec"/>:
    /// the singer of a track on a part — the score row's, else the first block of the track that
    /// names one, else the part's, else null (the engine's default voice).
    /// </summary>
    public static Func<string?, string?, string?> ReaderOf(SyntaxNode root, RenderSpec? spec)
    {
        var tracks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in root.DescendantNodes<LyricsBlockSyntax>())
            if (block.VoiceName is { } track && block.Singer is { Length: > 0 } singer)
                tracks.TryAdd(track, singer);
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in root.DescendantNodes<PartDeclarationSyntax>())
            if (Semantics.PartHeaderDefaults.Read(part).Singer is { Length: > 0 } singer)
                parts.TryAdd(part.Name.Text, singer);
        var rows = spec?.RowSingers;
        return (track, part) =>
            track != null && rows != null && rows.TryGetValue(track, out var row) ? row
            : track != null && tracks.TryGetValue(track, out var own) ? own
            : part != null && parts.TryGetValue(part, out var theirs) ? theirs
            : null;
    }
}
