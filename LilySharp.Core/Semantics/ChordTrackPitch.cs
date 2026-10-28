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

using System.Runtime.CompilerServices;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Whose letters a chord track is typed in, and whose reading a placed row prints (owner's
/// decision 2026-10-28). The two sites mirror the lyrics' <c>sings</c>
/// (<see cref="Music.LyricBindings"/>):
/// <list type="bullet">
/// <item>the DEFINITION — <c>chords tpc for tp { G6 | C7 }</c> — types the track in tp's
/// letters: under tp's <c>pitch</c> convention, the player's when tp's notes are the player's.
/// It is also the track's default reader. Any one block of the name says it; another block
/// may repeat it or omit it.</item>
/// <item>a SCORE ROW — <c>chords prog for tp</c> — prints THIS placement as tp's player reads
/// it on this page (<see cref="ConcertPitch.ChordNameDrop"/>), whatever the definition says.</item>
/// </list>
/// A track with no <c>for</c> is sounding pitch and printed as written: no <c>pitch</c> moves
/// it, and neither does the staff it stands above (until 2026-10-28 a row took the reading of
/// the staff right below it, a link no text stated).
/// </summary>
public static class ChordTrackPitch
{
    /// <summary>The contextual word, claimed only between a chord track's name and its brace,
    /// and after a score row's track name.</summary>
    public const string ForWord = "for";

    private static readonly ConditionalWeakTable<SyntaxNode, Dictionary<string, string>> Maps = new();

    /// <summary>The part whose letters the track <paramref name="trackName"/> is typed in: the
    /// first block of that name that writes a <c>for</c>, else null (sounding pitch).</summary>
    public static string? TargetOf(SyntaxNode root, string trackName)
    {
        while (root.Parent != null)
            root = root.Parent;
        return Maps.GetValue(root, BuildMap).TryGetValue(trackName, out var target) ? target : null;
    }

    /// <summary>The part whose player's reading one placed row prints: the row's own
    /// <c>for</c>, else the track's (<see cref="TargetOf"/>), else null (as written).</summary>
    public static string? ReaderOfRow(SyntaxNode root, string trackName, string? rowFor)
        => rowFor ?? TargetOf(root, trackName);

    /// <summary>
    /// How many semitones DOWN (0..11) the letters of <paramref name="trackName"/> are from
    /// what sounds: a B♭ trumpet's track typed as its player reads it is 2 (G6 sounds F6);
    /// 0 for a sounding track, or one whose part is typed at concert pitch.
    /// </summary>
    public static int TypedDrop(SyntaxNode root, string trackName, bool fileIsConcert)
    {
        if (TargetOf(root, trackName) is not { } target)
            return 0;
        var part = ConcertPitch.FindPart(root, target);
        if (part == null || (ConcertPitch.PartMode(part) ?? fileIsConcert))
            return 0;
        int shift = PartHeaderDefaults.Read(part).ConcertShiftSemitones;
        return ((-shift % 12) + 12) % 12;
    }

    /// <summary>
    /// How many semitones DOWN (0..11) one placed row prints what its track SOUNDS: to the
    /// reading of the row's reader on this page (<see cref="ConcertPitch.ChordNameDrop"/>;
    /// <paramref name="pageModeOf"/> is how this score prints a part, null for its letters'
    /// convention), 0 for a row with no reader. The letters reach what sounds first
    /// (<see cref="TypedDrop"/>).
    /// </summary>
    public static int ReaderDrop(SyntaxNode root, string trackName, string? rowFor, bool fileIsConcert,
        Func<string, bool?> pageModeOf)
        => ReaderOfRow(root, trackName, rowFor) is { } reader
            ? ConcertPitch.ChordNameDrop(pageModeOf(reader), fileIsConcert, ConcertPitch.FindPart(root, reader))
            : 0;

    /// <summary>
    /// What a chord symbol of a track typed <paramref name="typedDrop"/> semitones above what
    /// sounds (<see cref="TypedDrop"/>) sounds: the chord that many semitones down, spelled in
    /// <paramref name="soundingSharps"/> (the key in force, which is the sounding one). A Roman
    /// degree is no letter: it is read against the sounding key already and is not moved.
    /// </summary>
    public static Music.ChordStructure Sounding(Music.ChordStructure typed, string symbol, int typedDrop, int soundingSharps)
    {
        if (typedDrop == 0 || !IsLetterRoot(symbol))
            return typed;
        int typedSharps = Music.ChordStructure.PressedKeySharps(soundingSharps, (12 - typedDrop) % 12);
        return typed.Pressed(typedDrop, typedSharps);
    }

    /// <summary>What a track's <paramref name="entry"/> sounds (<see cref="Sounding(Music.ChordStructure, string, int, int)"/>),
    /// its track and the file's convention read off the tree — for a reader that walks the
    /// entries themselves (the key estimate, the capo advice).</summary>
    public static Music.ChordStructure Sounding(ChordEntrySyntax entry, Music.ChordStructure typed, int soundingSharps)
    {
        ChordPartBlockSyntax? block = null;
        SyntaxNode root = entry;
        while (root.Parent != null)
        {
            root = root.Parent;
            block ??= root as ChordPartBlockSyntax;
        }
        if (block?.PartName is not { } track)
            return typed;
        return Sounding(typed, entry.SymbolText, TypedDrop(root, track, ConcertPitch.FileIsConcert(root)), soundingSharps);
    }

    private static bool IsLetterRoot(string symbol) => symbol.Length > 0 && symbol[0] is >= 'A' and <= 'G';

    private static Dictionary<string, string> BuildMap(SyntaxNode root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in root.DescendantNodes().OfType<ChordPartBlockSyntax>())
            if (block.PartName is { } name && block.ForTarget is { } target)
                map.TryAdd(name, target);
        return map;
    }
}
