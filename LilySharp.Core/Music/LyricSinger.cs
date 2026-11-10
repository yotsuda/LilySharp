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
/// Who sings what: the file's <c>singer [S] sings [NAME] [on PART]</c> lines
/// (docs/lyrics-singer-design.md). Lily# only carries the singer's string: what sings is the
/// engine's to resolve (docs/singing-roadmap.md §4), the listener's settings having the last say.
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

    private static string? GenderOfWord(string? word) => word switch { Female => Female, Male => Male, _ => null };

    private static string? GenderOfPreset(string? preset) => preset switch
    {
        "soprano" or "mezzosoprano" or "alto" or "contralto" => Female,
        "tenor" or "baritone" or "bass" => Male,
        _ => null,
    };

    private static string GenderOfPart(string? preset, string? clef)
        => GenderOfPreset(preset?.StartsWith("voice-", StringComparison.Ordinal) == true ? preset["voice-".Length..] : preset)
            ?? (clef is "bass" or "bass_8" or "baritone" or "tenor" or "treble_8" ? Male : Female);

    /// <summary>One <c>singer</c> line, resolved: who (null for the part's default), which track,
    /// the part whose notes it sings (null when nothing says: an error), and the voice of that
    /// part it names (null when it names the part).</summary>
    public sealed record Line(string? Singer, string Track, string? Part, string? Voice, SingerDeclarationSyntax Node);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SyntaxNode, IReadOnlyList<Line>> Lines = new();

    /// <summary>The file's <c>singer</c> lines, each resolved (§4 of the design: its <c>on</c>, the
    /// file's only part, or the one part every score prints the words under).</summary>
    public static IReadOnlyList<Line> LinesOf(SyntaxNode root)
    {
        while (root.Parent != null)
            root = root.Parent;
        return Lines.GetValue(root, BuildLines);
    }

    private static IReadOnlyList<Line> BuildLines(SyntaxNode root)
    {
        var declarations = root.SitesOfKinds(SyntaxKind.SingerDeclaration).OfType<SingerDeclarationSyntax>().ToList();
        if (declarations.Count == 0)
            return [];
        var parts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in root.SitesOfKinds(Editing.PartReferenceFinder.DeclaringKinds))
            if (Editing.PartReferenceFinder.DeclaredName(n) is { } declared)
                parts.Add(declared.Text);
        var specs = RenderSpecParser.FindAll(root);
        var lines = new List<Line>(declarations.Count);
        foreach (var declaration in declarations)
        {
            string track = declaration.TrackName;
            string? target = declaration.OnToken?.Text
                ?? (parts.Count == 1 ? parts.First()
                    : parts.Count == 0 ? SyntaxFacts.UnnamedPartName
                    : PrintedOn(specs, track));
            string? part = target, voice = null;
            if (target != null && parts.Count > 0 && !parts.Contains(target))
            {
                voice = target;
                part = parts.FirstOrDefault(p => LyricBindings.VoicesOfPart(root, p).Contains(target));
            }
            lines.Add(new Line(declaration.Singer, track, part, voice, declaration));
        }
        return lines;
    }

    /// <summary>The one part every score prints <paramref name="track"/> under, or null.</summary>
    private static string? PrintedOn(IReadOnlyList<RenderSpec> specs, string track)
    {
        var on = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            foreach (var binding in spec.GetVoiceBindings())
                if (!binding.WithLyrics.IsDefaultOrEmpty && binding.WithLyrics.Contains(track))
                    on.Add(binding.VoiceName);
            foreach (var row in spec.AllLyricsRows)
                if (row.PartName == track && row.Sings is { } sings)
                    on.Add(sings);
        }
        return on.Count == 1 ? on.First() : null;
    }

    /// <summary>
    /// A reader of whether a track's words on a part are SUNG in the score <paramref name="spec"/>:
    /// a <c>singer</c> line names them on that part and the part's staff sounds a voice.
    /// </summary>
    public static Func<string?, string?, bool> SungReaderOf(SyntaxNode root, RenderSpec? spec)
    {
        var lines = LinesOf(root);
        if (lines.Count == 0 || spec == null)
            return static (_, _) => false;
        var sounds = SungStaves.Of(root, spec);
        return (track, part) => part != null && sounds.Sung.Contains(part, StringComparer.Ordinal)
            && lines.Any(l => l.Part == part && l.Track == track);
    }

    /// <summary><paramref name="spec"/> with a row for every <c>singer</c> line whose words it does
    /// not print, so what is sung is read the way what is printed is. The words are the line's to
    /// sing whether the page shows them or not (docs/lyrics-singer-design.md §2).</summary>
    public static RenderSpec WithSungWords(SyntaxNode root, RenderSpec spec)
    {
        var lines = LinesOf(root);
        if (lines.Count == 0)
            return spec;
        var placed = new HashSet<(string Track, string On)>();
        foreach (var binding in spec.GetVoiceBindings())
            foreach (var track in binding.WithLyrics)
                placed.Add((track, binding.VoiceName));
        foreach (var row in spec.AllLyricsRows)
            if (row.Sings is { } on)
                placed.Add((row.PartName, on));
        var added = new List<RenderItemSpec>();
        foreach (var line in lines)
            if (line.Part is { } part && !placed.Contains((line.Track, part)) && !placed.Contains((line.Track, line.Voice ?? part)))
                added.Add(new LyricsRowSpec(line.Track, line.Voice ?? part));
        return added.Count == 0 ? spec : spec with { Items = spec.Items.AddRange(added) };
    }

    /// <summary>A reader of who sings a track on a part: the <c>singer</c> line's string, or null
    /// (the part's default voice).</summary>
    public static Func<string?, string?, string?> ReaderOf(SyntaxNode root, RenderSpec? spec)
    {
        var lines = LinesOf(root);
        return (track, part) => lines.FirstOrDefault(l => l.Part == part && l.Track == track)?.Singer;
    }
}
