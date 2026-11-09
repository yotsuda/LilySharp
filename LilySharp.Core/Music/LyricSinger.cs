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
    /// owner's decisions 2026-11-09, sessions 1173 and 1175): which voices the editor offers for it, and,
    /// written with no name, the singer itself (carried as the string <c>male</c>, for the engine to pick a
    /// voice of that gender). Lower-case, closed.</summary>
    public static readonly IReadOnlyList<string> Genders = ["male", "female"];

    /// <summary>The track name of the file's unnamed lyrics, for the language server's reading of a head.</summary>
    public const string UnnamedTrack = SyntaxFacts.UnnamedLyricsName;

    /// <summary>What joins the voices of one <c>singer "a","b"</c> (owner's request 2026-11-09, sessions
    /// 1197 and 1198: several voices on one part) in the string the engine is given.</summary>
    public const char Joiner = '+';

    /// <summary>The singer written from the token at <paramref name="index"/>: the name, and every
    /// <c>,"…"</c> after it, as one string for the engine.</summary>
    internal static string Joined(SyntaxTokenNode first, int index, Func<int, SyntaxNode?> child, int end)
    {
        var names = new List<string> { ValueOf(first.Text) };
        for (int i = index + 1; i + 1 < end && child(i) is SyntaxTokenNode { Kind: SyntaxKind.Comma } && child(i + 1) is SyntaxTokenNode { Kind: SyntaxKind.StringLiteral } next; i += 2)
            names.Add(ValueOf(next.Text));
        return string.Join(Joiner, names);
    }

    /// <summary>The text between a string token's quotes.</summary>
    public static string ValueOf(string token)
        => StringLiteral.IsClosed(token) ? StringLiteral.Value(token) : token.Trim('"');

    /// <summary>
    /// A reader of whether a part is sung by a male or a female voice when no voice is named, for the
    /// engine's default voice of that gender (owner's decision 2026-11-09, session 1187): what the
    /// singer's word says (<c>male</c>, <c>soprano</c>), else the part's preset, else its clef, a
    /// clef for male voices being bass, baritone, tenor or the treble an octave down.
    /// </summary>
    public static Func<string?, string?, string> GenderReaderOf(SyntaxNode root)
    {
        var parts = new Dictionary<string, Semantics.PartHeaderDefaults>(StringComparer.Ordinal);
        foreach (var part in root.DescendantNodes<PartDeclarationSyntax>())
            parts.TryAdd(part.Name.Text, Semantics.PartHeaderDefaults.Read(part));
        return (singer, part) => GenderOfWord(singer)
            ?? (part != null && parts.TryGetValue(part, out var header) ? GenderOfPart(header.Preset, header.ClefWord) : Female);
    }

    private const string Female = "female", Male = "male";

    private static string? GenderOfWord(string? word) => word switch
    {
        "female" or "soprano" or "mezzosoprano" or "alto" or "contralto" => Female,
        "male" or "tenor" or "baritone" or "bass" => Male,
        _ => null,
    };

    private static string GenderOfPart(string? preset, string? clef)
        => GenderOfWord(preset?.StartsWith("voice-", StringComparison.Ordinal) == true ? preset["voice-".Length..] : preset)
            ?? (clef is "bass" or "bass_8" or "baritone" or "tenor" or "treble_8" ? Male : Female);

    /// <summary>
    /// A reader of whether a track's words on a part are SUNG (owner's decision 2026-11-10, session
    /// 1217): a voice has been asked for, by a <c>singer</c> reaching them (<see cref="ReaderOf"/>) or
    /// by the part's <c>instrument</c> being a voice (<see cref="Svg.Model.InstrumentDefaults.IsVoice"/>).
    /// Words on any other part are printed only: the part keeps its instrument and no voice is
    /// rendered. A clef says nothing here (a viola reads the alto clef).
    /// </summary>
    public static Func<string?, string?, bool> SungReaderOf(SyntaxNode root, RenderSpec? spec)
    {
        var singerOf = ReaderOf(root, spec);
        var voices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in root.DescendantNodes<PartDeclarationSyntax>())
            if (Svg.Model.InstrumentDefaults.IsVoice(Semantics.PartHeaderDefaults.Read(part).Preset))
                voices.Add(part.Name.Text);
        return (track, part) => singerOf(track, part) is { Length: > 0 } || (part != null && voices.Contains(part));
    }

    /// <summary>The key a score row's singer is kept under: the track and the part the row says it
    /// sings, as one track may stand under several staves (owner's report 2026-11-09, session 1176:
    /// two rows of one track, each with its own singer, both sang the first's); the track alone for a
    /// row that names no part.</summary>
    public static string RowKey(string track, string? part) => part == null ? track : $"{track}\0{part}";

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
        // A row that names a voice of an engraved part (`sings alt`) is kept under that voice, and
        // its words stand on the part: looked up by the track.
        var declared = root.DescendantNodes<PartDeclarationSyntax>().Select(p => p.Name.Text).ToHashSet(StringComparer.Ordinal);
        var onAVoice = new Dictionary<string, string>(StringComparer.Ordinal);
        if (rows != null)
            foreach (var (key, singer) in rows)
                if (key.IndexOf('\0') is var cut and >= 0 && !declared.Contains(key[(cut + 1)..]))
                    onAVoice.TryAdd(key[..cut], singer);
        return (track, part) =>
            track != null && rows != null && (rows.TryGetValue(RowKey(track, part), out var row) || rows.TryGetValue(track, out row)) ? row
            : track != null && onAVoice.TryGetValue(track, out var voiced) ? voiced
            : track != null && tracks.TryGetValue(track, out var own) ? own
            : part != null && parts.TryGetValue(part, out var theirs) ? theirs
            : null;
    }
}
