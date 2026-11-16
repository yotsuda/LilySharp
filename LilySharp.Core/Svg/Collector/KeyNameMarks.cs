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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The key as words over a score whose staves show no signature — every staff a ONE-LINE staff
/// (owner's request 2026-11-16, the chord chart's one-line staff; staves of two to four lines draw
/// the signature themselves and take no words — owner's decision the same day): "Key: D" over the
/// first bar, and again over a bar the key changes at (<see cref="MusicMarkType.KeyName"/>).
/// </summary>
/// <remarks>
/// LILYSHARP-OWN. A one-line staff is LilyPond's RhythmicStaff, which removes the Key_engraver
/// (ly/engraver-init.ly), and a staff of two to four lines draws the signature at positions its
/// lines do not have. The words follow the chord chart: the tonic spelled as a chord symbol's root
/// ("D", "Bb", "F#"), a minor key with "m" ("Bm"). A tab or drum staff in the score, or any staff
/// of five lines or more, and the score gets none; so does a key with no tonic (<c>key custom</c>,
/// <c>key none</c>) and a file that writes no key at all.
/// </remarks>
internal static class KeyNameMarks
{
    /// <summary>The prefix the words carry.</summary>
    internal const string Prefix = "Key: ";

    /// <summary>Whether the score's staves (rows aside) are all one-line staves, none a tab or
    /// drum staff, and there is one at least.</summary>
    internal static bool Wanted(ImmutableArray<StaffGroup> staffGroups)
    {
        bool any = false;
        foreach (var group in staffGroups)
            foreach (var staff in group.Staves)
            {
                if (staff.IsTextRow)
                    continue;
                if (staff.IsTab || staff.Clef == ClefType.Percussion || staff.Lines != 1)
                    return false;
                any = true;
            }
        return any;
    }

    /// <summary>
    /// <paramref name="marks"/> with the key's words added after them (so the indices the other
    /// marks are read by stay). The opening key over bar 0: the header <c>key</c> of a section the
    /// piece opens with, else the file's first <c>key</c> outside any section. The changes: where
    /// the first staff draws one (<see cref="KeySignatureChangeItem"/> opening a bar — named from
    /// the <c>key</c> written at its place, or the opening key when it is a section's return to
    /// it); a staff with none (the automatic one-line staff takes its bars from the rows, which
    /// carry no key) from the sections — at each start the form plays, the section's header key
    /// or the opening one, where it differs from the key before.
    /// </summary>
    internal static ImmutableArray<MusicMarkItem> Add(ImmutableArray<MusicMarkItem> marks,
        ImmutableArray<StaffGroup> staffGroups, SyntaxNode root, Semantics.SectionHeaders headers,
        IReadOnlyDictionary<string, List<int>> sectionStarts)
    {
        var staff = staffGroups.SelectMany(g => g.Staves).FirstOrDefault(s => !s.IsTextRow);
        if (staff is null)
            return marks;
        KeySignatureSyntax? opening = null;
        foreach (var (name, starts) in sectionStarts)
            if (starts.Contains(0) && headers.Keys.TryGetValue(name, out var k))
                opening = k;
        opening ??= root.KindSites(SyntaxKind.KeySignature).OfType<KeySignatureSyntax>()
            .FirstOrDefault(k => !InSection(k));
        if (opening is null || NameOf(opening) is not { } home)
            return marks;

        var builder = marks.IsDefaultOrEmpty ? ImmutableArray.CreateBuilder<MusicMarkItem>() : marks.ToBuilder();
        void Mark(int measure, string name, int position) =>
            // At the bar's first moment: the X placement reads the timing column of it (a
            // default 0/0 timing put the words over the NEXT bar's start).
            builder.Add(new MusicMarkItem(MusicMarkType.KeyName, Prefix + name, measure, position,
                anchorTiming: Semantics.Fraction.Zero));
        Mark(0, home, opening.Pitch.Position);

        var keysAt = new Dictionary<int, KeySignatureSyntax>();
        // (green sites: a keystroke must not build the whole book's red index — KeystrokeStaysRedFreeTests)
        foreach (var k in root.KindSites(SyntaxKind.KeySignature).OfType<KeySignatureSyntax>())
            if (!k.IsCustom)
                keysAt[k.Pitch.Position] = k;
        var measures = staff.PrimaryVoice.Measures;
        bool drawn = false;
        for (int m = 1; m < measures.Length; m++)
            foreach (var item in measures[m].Items)
            {
                if (item is KeySignatureChangeItem change)
                {
                    drawn = true;
                    string? name = keysAt.TryGetValue(change.SourcePosition, out var written) ? NameOf(written)
                        : change.NewKey.Custom == null && change.NewKey.Sharps == SharpsOf(opening) ? home
                        : null;
                    if (name != null)
                        Mark(m, name, change.SourcePosition);
                    break;
                }
                if (item.Duration > Semantics.Fraction.Zero)
                    break;      // a change later in the bar is not the bar's opening key
            }
        if (drawn)
            return builder.ToImmutable();

        // The sections, in the order the form plays them.
        var plays = new List<(int Measure, string Section)>();
        foreach (var (name, starts) in sectionStarts)
            foreach (int s in starts)
                plays.Add((s, name));
        string current = home;
        foreach (var (measure, section) in plays.OrderBy(p => p.Measure))
        {
            var key = headers.Keys.TryGetValue(section, out var k) ? k : null;
            string? name = key is null ? home : NameOf(key);
            if (name is null || name == current || measure >= measures.Length)
                continue;
            current = name;
            if (measure > 0)
                Mark(measure, name, key?.Pitch.Position ?? 0);
        }
        return builder.ToImmutable();
    }

    /// <summary>The key a <c>key</c> writes, as a chord symbol's root: the tonic's letter and
    /// accidental, then its mode — nothing for major (and <c>ionian</c>, the same scale), "m" for
    /// minor (and <c>aeolian</c>), the church mode's name for the other five ("D Dorian"; owner's
    /// report 2026-11-16: <c>key d ionian</c> printed "Dm", every mode but <c>major</c> read as
    /// minor). Null for a key with no tonic.</summary>
    internal static string? NameOf(KeySignatureSyntax key)
    {
        if (key.IsCustom || key.GetChild(2) is not PitchSyntax pitch || pitch.BaseName is not (>= 'a' and <= 'g')
            || key.GetChild(3) is not SyntaxTokenNode mode)
            return null;
        int alter = pitch.AccidentalOffset;
        string accidental = alter switch { > 0 => new string('#', alter), < 0 => new string('b', -alter), _ => "" };
        string suffix = mode.Kind switch
        {
            SyntaxKind.MajorKeyword or SyntaxKind.IonianKeyword => "",
            SyntaxKind.MinorKeyword or SyntaxKind.AeolianKeyword => "m",
            SyntaxKind.DorianKeyword => " Dorian",
            SyntaxKind.PhrygianKeyword => " Phrygian",
            SyntaxKind.LydianKeyword => " Lydian",
            SyntaxKind.MixolydianKeyword => " Mixolydian",
            SyntaxKind.LocrianKeyword => " Locrian",
            _ => "",
        };
        return char.ToUpperInvariant(pitch.BaseName) + accidental + suffix;
    }

    /// <summary>A mode's signature against the major scale on the same tonic, in fifths (Dorian
    /// is two flats from major: D Dorian has C major's signature).</summary>
    private static int ModeFifths(SyntaxKind mode) => mode switch
    {
        SyntaxKind.LydianKeyword => 1,
        SyntaxKind.MixolydianKeyword => -1,
        SyntaxKind.DorianKeyword => -2,
        SyntaxKind.MinorKeyword or SyntaxKind.AeolianKeyword => -3,
        SyntaxKind.PhrygianKeyword => -4,
        SyntaxKind.LocrianKeyword => -5,
        _ => 0,
    };

    private static bool InSection(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is SectionDeclarationSyntax)
                return true;
        return false;
    }

    /// <summary>The sharps (positive) or flats (negative) of a traditional key.</summary>
    private static int SharpsOf(KeySignatureSyntax key)
    {
        if (key.GetChild(2) is not PitchSyntax pitch)
            return 0;
        int[] majorFifths = [0, 2, 4, -1, 1, 3, 5];    // c d e f g a b
        int step = "cdefgab".IndexOf(pitch.BaseName);
        if (step < 0)
            return 0;
        return majorFifths[step] + 7 * pitch.AccidentalOffset
            + (key.GetChild(3) is SyntaxTokenNode mode ? ModeFifths(mode.Kind) : 0);
    }
}
