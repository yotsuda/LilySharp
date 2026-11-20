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

namespace LilySharp.Core.Semantics;

/// <summary>
/// An <c>@or</c> mark on a chords-row symbol: the chords it may be instead —
/// <c>F@or(Bm Dm)</c>, the candidates space-separated (Orecchiante request 17, owner's decisions
/// 2026-11-17).
/// </summary>
/// <remarks>
/// <para>
/// A transcription (Orecchiante's, a writer's) that is unsure of a chord writes the one it
/// thinks likeliest and the others after it. Like <see cref="TodoAnnotation"/> it changes
/// neither the paper nor the sound: the symbol written is the chord, the candidates are a
/// note to oneself. The preview shows it as a grey <c>@todo</c> whose memo is the FIRST
/// candidate (<c>or Bm?</c>), the editor's Ctrl+Shift+Up / Down rotates the candidates
/// into the symbol's place (<c>F@or(Bm Dm)</c> → <c>Bm@or(Dm F)</c>) and sounds the chord
/// that came forward, and a quick fix keeps one and deletes the mark — as does deleting it
/// by hand. It may stand beside a <c>@todo</c> (<c>F@or(Bm)@todo("…")</c>).
/// </para>
/// </remarks>
/// <param name="Candidates">The candidates as written, in order; empty when <paramref name="Problem"/> is set.</param>
/// <param name="Problem">What is wrong with the arguments, or null.</param>
public sealed record OrAnnotation(IReadOnlyList<string> Candidates, string? Problem)
{
    /// <summary>The annotation's name.</summary>
    public const string Name = "or";

    /// <summary>The <c>@or</c> an annotation node writes, or null when it is not one. A bare
    /// <c>@or</c> is one with no candidates (a problem).</summary>
    public static OrAnnotation? Of(SyntaxNode annotation) => annotation switch
    {
        ArticulationSyntax art when string.Equals(art.NameToken.Text, Name, StringComparison.Ordinal)
            => new([], Shape),
        MusicMarkSyntax mark when !mark.IsSpanEnd && string.Equals(mark.Name, Name, StringComparison.Ordinal)
            => FromArguments(mark.Arguments),
        _ => null,
    };

    /// <summary>The <c>@or</c> among a host's child nodes (a chords-row entry's), or null.</summary>
    public static OrAnnotation? OfHost(SyntaxNode host)
    {
        foreach (var child in host.ChildNodes())
            if (Of(child) is { } or)
                return or;
        return null;
    }

    /// <summary>The preview's memo: the first candidate, asked — <c>or Bm?</c>.</summary>
    public string? Memo => Candidates.Count > 0 ? $"or {Candidates[0]}?" : null;

    private const string Shape = "@or takes the chords this one may be instead, space-separated: F@or(Bm Dm)";

    private static OrAnnotation FromArguments(IReadOnlyList<MarkArgument> arguments)
    {
        if (arguments.Count == 0)
            return new([], Shape + ".");
        var texts = arguments.Select(a => a.Value is LysValue.Str s ? s.V : a.Text).ToList();
        foreach (var t in texts)
            if (t.Length == 0 || Svg.Collector.ChordNameCollector.StructureOf(t, 0, 0) is null)
                return new([], $"'{t}' is not a chord symbol; " + Shape + ".");
        return new(texts, null);
    }
}
