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
using System.IO;
using System.Text.Json;

namespace LilySharp.Lsp;

/// <summary>
/// The side file an OMR reader (LilySharp-Omr) writes beside the <c>x.lys</c> it read:
/// <c>x.omr.json</c>, the agreement of its proposal of 2026-10-04 (section D). Lily# only reads
/// it — this is the one reader — and every field it does not use is left alone.
/// </summary>
/// <remarks>
/// <c>{ "version": 1, "todos": [ { "key": "o1203", "candidates": [ { "label": "F♯",
/// "text": "fis'8" } ] } ], … }</c> — a todo's <c>key</c> is the <c>@todo(key …)</c> in the
/// <c>.lys</c>, and a candidate's <c>text</c> is the marked item as it would be written without
/// the mark. A file that is missing, not JSON, or of a <c>version</c> this does not know reads
/// as having no candidates: the side file is a help, never a reason for an error.
/// </remarks>
internal static class OmrSideFile
{
    /// <summary>The <c>version</c>s of the side file this reads.</summary>
    private const int MaxVersion = 1;

    /// <summary>One candidate for a marked item.</summary>
    internal readonly record struct Candidate(string? Label, string Text);

    /// <summary>The candidates the side file at <paramref name="path"/> lists for <paramref name="key"/>,
    /// in its order; empty when it lists none or cannot be read.</summary>
    internal static IReadOnlyList<Candidate> Candidates(string path, string key)
    {
        if (!File.Exists(path))
            return [];
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out int v)
                || v < 1 || v > MaxVersion
                || !root.TryGetProperty("todos", out var todos) || todos.ValueKind != JsonValueKind.Array)
                return [];
            foreach (var todo in todos.EnumerateArray())
            {
                if (todo.ValueKind != JsonValueKind.Object
                    || !todo.TryGetProperty("key", out var k) || k.ValueKind != JsonValueKind.String
                    || !string.Equals(k.GetString(), key, StringComparison.Ordinal))
                    continue;
                var result = new List<Candidate>();
                if (todo.TryGetProperty("candidates", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var c in list.EnumerateArray())
                        if (c.ValueKind == JsonValueKind.Object
                            && c.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                            && text.GetString() is { Length: > 0 } t)
                            result.Add(new Candidate(
                                c.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String
                                    ? label.GetString() : null,
                                t));
                return result;
            }
            return [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
