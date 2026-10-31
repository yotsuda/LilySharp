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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The octave pair (Ctrl+Shift+PageUp / PageDown) on a <c>chords</c> row entry (owner's request
/// 2026-10-31): the entry's symbol walks the SEVENTH chords of the key in force — in C major
/// Cmaj7, Dm7, Em7, Fmaj7, G7, Am7, Bm7-5 — from the degree whose root the symbol stands on
/// (Cmaj7, C and C7 all stand on I), stopping at I and at VII (owner's decision the same day:
/// no going round). The semitone pair (Up / Down) keeps stepping the entry's shape, as it did.
/// </summary>
/// <remarks>
/// The key is the one written for the entry (<see cref="WrittenKeyAt"/>: the section's, the
/// file's), C major with none; a minor key walks its natural-minor sevenths (Am7 Bm7-5 Cmaj7 …,
/// <see cref="DiatonicChords.ForKey"/>). A shape the entry wrote goes with the old chord — it
/// was that chord's. A Roman degree (<c>IIm7</c>), a symbol whose root is not a tone of the
/// key, or a custom key, steps nothing and says why.
/// </remarks>
public static partial class NoteStepper
{
    private static (Edit? Edit, string Message) DiatonicChordEdit(ChordEntrySyntax entry, int dir)
    {
        string symbol = entry.SymbolText;
        if (!ChordStructure.TryParseChordEntry(symbol, out var parsed))
            return (null, $"{symbol}: the keys walk a key's seventh chords from a chord NAME, not a degree - its chord depends on the key.");

        var key = WrittenKeyAt(entry);
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
        // The whole entry — its shapes included: they held the old chord — becomes the new symbol;
        // a `@todo` after it stays (EntryEnd stops at the symbol or its ')').
        return (new Edit(entry.SourceStart, EntryEnd(entry), chord.SeventhSymbol),
            $"{chord.SeventhSymbol}: {chord.RomanSeventhSymbol} of {keyName} ({next + 1} of 7)");
    }
}
