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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The capo suggestion (owner's design 2026-09-28, HANDOFF §2 K2; built 2026-09-29): for each
/// capo fret 0 to <see cref="MaxSuggested"/>, how many of the chords a file names would be
/// played with a BARRE on the usual shape of their pressed chord — ranked, fewest barres
/// first. The editor lists the ranking after <c>capo</c> in a layout's <c>chordDiagrams</c>
/// entry and on hovering the word; the writer picks a fret and writes it (<c>capo auto</c> is
/// not a spelling: the choice is the writer's).
/// </summary>
/// <remarks>
/// The chords are every <c>chords</c> row entry that names an absolute chord and every
/// <c>@chord(SYMBOL …)</c> in the file, each chord once (a Roman degree, a bare <c>@chord</c>
/// and a quoted text name no chord the tree can read). A chord is played with a barre when its
/// usual shape on the tuning (<see cref="ChordShapes.Default"/>) has one — a predefined shape's
/// own barre, else a shape whose finger count (<see cref="ChordVoicings.Fingers"/>) is below
/// its fretted strings. A chord with no usual shape at all counts apart (<c>Unshaped</c>), after
/// the barres and before the fret itself in the ranking. LILYSHARP-OWN: LilyPond has nothing
/// of the kind.
/// </remarks>
public static class CapoAdvisor
{
    /// <summary>The highest capo fret the ranking considers (a capo higher up leaves too
    /// little neck; the owner's prototype ranked 0–7).</summary>
    public const int MaxSuggested = 7;

    /// <summary>One capo fret's tally.</summary>
    /// <param name="Capo">The fret, 0 for no capo.</param>
    /// <param name="Barres">How many of the chords take a barre there.</param>
    /// <param name="Unshaped">How many have no usual shape there.</param>
    /// <param name="Chords">How many chords the file names.</param>
    /// <param name="BarreChords">The barre chords: the symbol as the music writes it (sounding)
    /// and the pressed shape, in the file's order.</param>
    public sealed record Choice(int Capo, int Barres, int Unshaped, int Chords,
        ImmutableArray<(string Symbol, string Shape)> BarreChords);

    /// <summary>The chords the file names, each once, in the order they first appear.</summary>
    public static IReadOnlyList<(string Symbol, ChordStructure Chord)> ChordsOf(SyntaxNode root)
    {
        var found = new List<(string, ChordStructure)>();
        void Add(string symbol, ChordStructure chord)
        {
            if (chord.RawSuffix == null && !found.Any(f => ChordShapeTable.SameChord(f.Item2, chord)))
                found.Add((symbol, chord));
        }
        var sites = root.KindSites(SyntaxKind.ChordEntry)
            .Concat(root.KindSites(SyntaxKind.MusicMark))
            .OrderBy(n => n.SourceStart);
        foreach (var site in sites)
        {
            if (site is ChordEntrySyntax entry)
            {
                if (ChordStructure.TryParseChordEntry(entry.SymbolText, out var chord))
                    Add(entry.SymbolText, chord);
            }
            else if (site is MusicMarkSyntax mark
                     && ChordAnnotation.Of(mark) is { Symbol: { } symbol, Structure: { } structure })
                Add(symbol, structure);
        }
        return found;
    }

    /// <summary>The ranking for <paramref name="root"/>'s chords on <paramref name="tuning"/>:
    /// capo 0 to <see cref="MaxSuggested"/>, fewest barres first, then fewest chords with no
    /// shape, then the lower fret. Empty when the file names no chord.</summary>
    public static IReadOnlyList<Choice> Rank(SyntaxNode root, TuningType tuning)
    {
        var chords = ChordsOf(root);
        if (chords.Count == 0)
            return [];
        var choices = new List<Choice>();
        for (int capo = 0; capo <= MaxSuggested; capo++)
        {
            int unshaped = 0;
            var barres = ImmutableArray.CreateBuilder<(string, string)>();
            foreach (var (symbol, chord) in chords)
            {
                var shape = ChordShapes.Default(tuning, chord.Pressed(capo, 0));
                if (shape == null)
                    unshaped++;
                else if (HasBarre(shape))
                    barres.Add((symbol, shape.Spelled));
            }
            choices.Add(new Choice(capo, barres.Count, unshaped, chords.Count, barres.ToImmutable()));
        }
        return [.. choices.OrderBy(c => c.Barres).ThenBy(c => c.Unshaped).ThenBy(c => c.Capo)];
    }

    /// <summary>Whether a shape is played with a barre: a predefined shape's own barre
    /// (LilyPond's fingering says — D's <c>xx0232</c> and A's <c>x02220</c> have none, F's
    /// <c>133211</c> and B♭'s <c>x13331</c> one); for a shape of Lily#'s order, one finger
    /// holding the lowest fret across three strings or more (<see cref="ChordVoicings.Fingers"/>'
    /// barre, which two strings alone would also satisfy).</summary>
    public static bool HasBarre(ChosenShape shape)
    {
        if (shape.Predefined is { } p)
            return p.Barres.Length > 0;
        int fretted = shape.Frets.Count(f => f > 0);
        int low = ChordVoicings.Position(shape.Frets);
        return fretted > 1 && low > 0 && shape.Frets.Count(f => f == low) >= 3
               && ChordVoicings.Fingers(shape.Frets) < fretted;
    }

    /// <summary>One line per choice, the ranking's order — <c>capo 3: 1 barre chord of 7 (F 133211)</c>.</summary>
    public static string Describe(Choice c)
    {
        string barres = c.Barres == 1 ? "1 barre chord" : $"{c.Barres} barre chords";
        string list = c.BarreChords.IsEmpty ? ""
            : " (" + string.Join(", ", c.BarreChords.Select(b => $"{b.Symbol} {b.Shape}")) + ")";
        string unshaped = c.Unshaped > 0 ? $", {c.Unshaped} with no shape" : "";
        return $"capo {c.Capo}: {barres} of {c.Chords}{list}{unshaped}";
    }
}
