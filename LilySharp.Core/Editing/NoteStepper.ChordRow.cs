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

using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The octave pair (Ctrl+Shift+PageUp / PageDown) on a <c>chords</c> row entry (owner's request
/// 2026-10-31) or a named <c>@chord(…)</c> (owner's request 2026-11-03): its symbol walks the SEVENTH chords of the key in force — in C major
/// Cmaj7, Dm7, Em7, Fmaj7, G7, Am7, Bm7-5 — from the degree whose root the symbol stands on
/// (Cmaj7, C and C7 all stand on I), stopping at I and at VII (owner's decision the same day:
/// no going round). The semitone pair (Up / Down) keeps stepping the entry's shape, as it did.
/// So does a named <c>@chord(…)</c> in a part (owner's request 2026-11-03): an @chord with no name
/// has nothing to walk from, and keeps its steps.
/// </summary>
/// <remarks>
/// The key is the one written for it (<see cref="WrittenKeyAt"/>: the music's, the section's, the
/// part's, the file's), C major with none; a minor key walks its natural-minor sevenths (Am7 Bm7-5 Cmaj7 …,
/// <see cref="DiatonicChords.ForKey"/>). A shape written on it goes with the old chord — it
/// was that chord's. A Roman degree (<c>IIm7</c>), a symbol whose root is not a tone of the
/// key, or a custom key, steps nothing and says why.
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The whole entry, its shapes included (they held the old chord), becomes the new
    /// symbol; a <c>@todo</c> after it stays (<see cref="EntryEnd"/> stops at the symbol or its
    /// <c>)</c>).</summary>
    private static (Edit? Edit, string Message) DiatonicChordEdit(ChordEntrySyntax entry, int dir)
        => DiatonicChordEdit(entry, entry.SymbolText, entry.SourceStart, EntryEnd(entry), dir);

    /// <summary>All an @chord's words, its shapes included, become the new symbol.</summary>
    private static (Edit? Edit, string Message) DiatonicChordEdit(MusicMarkSyntax mark, string symbol, int dir)
    {
        var words = ChordAnnotation.WordSpans(mark);
        return DiatonicChordEdit(mark, symbol, words[0].Start, words[^1].End, dir);
    }

    private static (Edit? Edit, string Message) DiatonicChordEdit(SyntaxNode site, string symbol, int start, int end, int dir)
    {
        if (!ChordStructure.TryParseChordEntry(symbol, out var parsed))
            return (null, $"{symbol}: the keys walk a key's seventh chords from a chord NAME, not a degree - its chord depends on the key.");

        var key = WrittenKeyAt(site);
        if (key is { IsCustom: true })
            return (null, $"{symbol}: a custom key has no seventh chords to walk.");
        char tonic = 'c';
        int sharps = 0;
        string keyName = "C major";
        if (key != null && key.GetChild(2) is PitchSyntax { BaseName: not '\0' } tonicPitch)
        {
            string mode = key.GetChild(3) is SyntaxTokenNode m && m.Text.Length > 0 ? m.Text.ToLowerInvariant() : "major";
            if (KeySpelling.SharpsFor(tonicPitch.PitchName, mode) is int s)
            {
                tonic = tonicPitch.PitchName[0];
                sharps = s;
                keyName = $"{tonicPitch.PitchName} {mode}";
            }
        }
        var chords = DiatonicChords.ForKey(tonic, sharps);
        int at = -1;
        for (int i = 0; i < chords.Length; i++)
            if ("cdefgab".IndexOf(chords[i].RootLetter) == parsed.RootStep && chords[i].RootAlter == parsed.RootAlter)
            {
                at = i;
                break;
            }
        if (at < 0)
            return (null, $"{symbol}: its root is not a tone of {keyName} - the keys walk the key's seventh chords "
                + $"({string.Join(" ", chords.Select(c => c.SeventhSymbol))}).");

        // The walk stops at the ends (owner's decision 2026-10-31: I at the bottom, VII at the
        // top — no going round), and says where it stands.
        int next = at + dir;
        if (next < 0 || next >= chords.Length)
            return (null, $"{symbol}: {chords[at].RomanSeventhSymbol} is the {(next < 0 ? "first" : "last")} chord of {keyName} "
                + $"({string.Join(" ", chords.Select(c => c.SeventhSymbol))}).");
        var chord = chords[next];
        return (new Edit(start, end, chord.SeventhSymbol),
            $"{chord.SeventhSymbol}: {chord.RomanSeventhSymbol} of {keyName} ({next + 1} of 7)");
    }
}
