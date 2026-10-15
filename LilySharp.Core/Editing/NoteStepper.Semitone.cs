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
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The scale step (Ctrl+Shift+Up / Ctrl+Shift+Down): a note, a chord member, or every member of
/// a chord moves to the next tone of the key in force, up or down. Until 2026-10-16 it moved a
/// semitone (owner's decision 2026-10-15); the owner then asked for the key's tones only
/// ("半音で動くのではなく、現在のkeyの上にある音だけにして") — the file and its names
/// (<c>semitone</c>, <c>stepSemitoneUp</c>) keep the old word.
/// </summary>
/// <remarks>
/// <para>
/// THE SCALE is the signature's (Lily#'s choice: a minor key steps through its natural minor,
/// the leading tone being an accidental the signature does not hold; a church mode through its
/// own signature; a <c>key custom …</c> through the steps it writes). Each tone is spelled as
/// the signature spells it — in F major a steps up to <c>bes</c>, in G major e up to
/// <c>fis</c>. A note off the scale steps to the nearest tone of it in the direction asked
/// (<c>cis</c> in C major: Up <c>d</c>, Down <c>c</c>).
/// </para>
/// <para>
/// THE KEY IN FORCE is read from the source, not the collector (Lily#'s choice: the spelling
/// is the writer's, before any transpose): the last <c>key</c> written before the note in the
/// music around it, else its section's, else its part declaration's, else the file's; none
/// reads as C major.
/// </para>
/// <para>
/// THE PITCH IS KEPT (owner's decision 2026-10-15): the stepped note sounds exactly the scale
/// tone next to where it was. A new letter can change the octave a note resolves to —
/// <c>b'</c> → <c>c''</c> in absolute octaves; <c>f b</c> → <c>f c'</c> in relative ones, where
/// the nearest c is below the f — so the octave marks are corrected until the compiler's
/// resolved pitch (<see cref="ResolvedPitches.ForFile"/>) is the one stepped to. The notes after
/// it follow as they will, as after an octave step. A note no score resolves keeps its marks.
/// </para>
/// <para>
/// Not stepped (handled, no edit, a status-bar word): a scale-degree member and a
/// quarter-tone pitch. An <c>@chord</c> or a <c>chords</c> row entry steps its shape as with
/// the octave keys.
/// </para>
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The status-bar word for a target the semitone step leaves alone.</summary>
    private const string SemitoneRefusal =
        "a scale degree or a quarter tone moves by octave only (Ctrl+Shift+PageUp / PageDown)";

    /// <summary>The octave-mark correction is tried at most this many times (one re-collection
    /// each); one round settles every case met so far.</summary>
    private const int MaxOctaveRounds = 4;

    /// <summary>The pitches a semitone step of <paramref name="t"/> moves: a note's pitch, a
    /// member, or every pitch member of a chord or an arpeggio — never a scale degree or a
    /// quarter tone.</summary>
    private static List<PitchSyntax> SemitonePitches(Target t)
    {
        IEnumerable<SyntaxNode> nodes = t.Kind switch
        {
            TargetKind.Note => [((NoteSyntax)t.Node).Pitch],
            TargetKind.Member => [t.Node],
            _ => MembersOf(t.Node),
        };
        return [.. nodes.OfType<PitchSyntax>().Where(p => p.BaseName != '\0' && p.QuarterOffset == 0)];
    }

    /// <summary>
    /// The edits of a semitone step of <paramref name="pitches"/>, merged with
    /// <paramref name="others"/> (sorted, not overlapping them): each pitch respelled
    /// (<see cref="Respell"/>), its octave marks corrected until it sounds a semitone from
    /// where it was (the type's remarks). <paramref name="expand"/> is the step's.
    /// </summary>
    private static List<Edit> SemitoneEdits(string text, SyntaxTree tree, IReadOnlyList<Edit> others,
        IReadOnlyList<PitchSyntax> pitches, int dir, Func<string, SyntaxTree>? expand)
    {
        bool longFlats = WritesLongFlats(tree);
        var respelled = pitches.Select(p => Respell(p, dir, longFlats)).ToArray();
        var names = respelled.Select(r => r.Name).ToArray();
        var extra = new int[pitches.Count];

        List<Edit> Build()
        {
            var list = new List<Edit>(others);
            for (int i = 0; i < pitches.Count; i++)
            {
                var p = pitches[i];
                int nameEnd = NameEnd(p);
                list.Add(extra[i] == 0
                    ? new Edit(p.SourceStart, nameEnd, names[i])
                    : new Edit(p.SourceStart, MarksEnd(text, nameEnd), names[i] + Marks(p.OctaveOffset + extra[i])));
            }
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            return list;
        }

        var edits = Build();
        var before = WrittenMidi(text, tree, expand);
        if (before.Count == 0)
            return edits;
        for (int round = 0; round < MaxOctaveRounds; round++)
        {
            var after = WrittenMidi(Apply(text, edits), null, expand);
            bool corrected = false;
            for (int i = 0; i < pitches.Count; i++)
            {
                int at = pitches[i].SourceStart;
                if (!before.TryGetValue(at, out int was) || !after.TryGetValue(MapThrough(edits, at), out int now))
                    continue;
                int off = now - (was + respelled[i].Delta);
                if (off == 0 || off % 12 != 0)
                    continue;
                extra[i] -= off / 12;
                corrected = true;
            }
            if (!corrected)
                break;
            edits = Build();
        }
        return edits;
    }

    /// <summary>The written pitch the compiler resolves at each pitch's first character
    /// (MIDI numbers, the trace's spelling — a transpose shifts both sides of a comparison
    /// alike); empty when the text does not collect.</summary>
    private static Dictionary<int, int> WrittenMidi(string text, SyntaxTree? tree, Func<string, SyntaxTree>? expand)
    {
        var map = new Dictionary<int, int>();
        try
        {
            var resolved = expand?.Invoke(text) ?? tree ?? SyntaxTree.Parse(text);
            string source = resolved.Text;
            foreach (var e in ResolvedPitches.ForFile(resolved) ?? [])
            {
                int p = e.Position;
                while (p < source.Length && char.IsWhiteSpace(source[p]))
                    p++;
                if (SoundIndex.MidiOf(e.Pitch) is int midi)
                    map.TryAdd(p, midi);
            }
        }
        catch (Exception)
        {
            // A text the step breaks keeps the marks it has.
        }
        return map;
    }

    /// <summary><paramref name="net"/> octave marks: that many <c>'</c>, or <c>,</c> below zero.</summary>
    private static string Marks(int net) => net >= 0 ? new string('\'', net) : new string(',', -net);

    /// <summary>Whether the file spells e-flat and a-flat <c>ees</c> / <c>aes</c> rather than
    /// LilyPond's contractions <c>es</c> / <c>as</c>: the step writes the file's way, the
    /// language's canonical <c>ees</c> / <c>aes</c> unless only the contractions are written.</summary>
    private static bool WritesLongFlats(SyntaxTree tree)
    {
        bool contracted = false;
        foreach (var p in tree.GetNodes<PitchSyntax>())
            switch (p.PitchToken.Text)
            {
                case "ees" or "aes" or "eeses" or "aeses":
                    return true;
                case "es" or "as" or "eses" or "ases":
                    contracted = true;
                    break;
            }
        return !contracted;
    }

    /// <summary>The scale tone <paramref name="p"/> steps to, up (<paramref name="dir"/> &gt; 0)
    /// or down, in the key in force (the type's remarks): its name, as the signature spells it,
    /// and how many semitones it lies from <paramref name="p"/>.</summary>
    private static (string Name, int Delta) Respell(PitchSyntax p, int dir, bool longFlats)
    {
        var signature = KeyInForce(p);
        // In semitones above the letter's C, so ces is -1 and bis 12.
        int was = RelativeOctave.StepSemitoneOf(KeySpelling.StepOf(p.BaseName)) + p.AccidentalOffset;
        (int Step, int Alter, int Value)? best = null;
        for (int octave = -1; octave <= 1; octave++)
            for (int s = 0; s < 7; s++)
            {
                int a = signature[s];
                int value = RelativeOctave.StepSemitoneOf(s) + a + 12 * octave;
                if (dir > 0 ? value <= was : value >= was)
                    continue;
                if (best is not { } b || (dir > 0 ? value < b.Value : value > b.Value))
                    best = (s, a, value);
            }
        var (step, alter, to) = best!.Value;
        return (NameOf(step, alter, longFlats), to - was);
    }

    /// <summary>A pitch name in the language's Dutch names: <c>fis</c>, <c>bes</c>, <c>ees</c>
    /// (or <c>es</c> when <paramref name="longFlats"/> is false), <c>cisis</c>.</summary>
    private static string NameOf(int step, int alter, bool longFlats)
    {
        char letter = "cdefgab"[step];
        if (alter < 0 && !longFlats && letter is 'e' or 'a')
            return letter + (alter == -1 ? "s" : "ses");
        return letter + alter switch
        {
            2 => "isis",
            1 => "is",
            -1 => "es",
            -2 => "eses",
            _ => "",
        };
    }

    /// <summary>The signature in force at <paramref name="node"/> (the type's remarks): each
    /// step's alteration, C to B — a custom key's as written, the others' from their sharps.</summary>
    private static int[] KeyInForce(SyntaxNode node)
    {
        var alterations = new int[7];
        var key = WrittenKeyAt(node);
        if (key is { IsCustom: true })
        {
            foreach (var (step, alter) in key.CustomAlterations)
                if (step >= 0)
                    alterations[step] = alter;
            return alterations;
        }
        if (key == null || key.GetChild(2) is not PitchSyntax tonic || tonic.BaseName == '\0')
            return alterations;
        string mode = key.GetChild(3) is SyntaxTokenNode m && m.Text.Length > 0 ? m.Text.ToLowerInvariant() : "major";
        if (KeySpelling.SharpsFor(tonic.PitchName, mode) is not int sharps)
            return alterations;
        for (int s = 0; s < 7; s++)
            alterations[s] = KeySpelling.Alteration(s, sharps);
        return alterations;
    }

    /// <summary>The <c>key</c> written for <paramref name="node"/>: the last one before it among
    /// the children of each block around it, nearest block first; at the top, its part
    /// declaration's, then the file's.</summary>
    private static KeySignatureSyntax? WrittenKeyAt(SyntaxNode node)
    {
        var child = node;
        while (child.Parent is { } parent)
        {
            if (parent.Parent == null && PartOf(node) is { } part)
                foreach (var declaration in parent.DescendantNodes<PartDeclarationSyntax>())
                    if (declaration.Name.Text == part
                        && declaration.DescendantNodes<KeySignatureSyntax>().FirstOrDefault() is { } partKey)
                        return partKey;
            KeySignatureSyntax? last = null;
            foreach (var c in parent.ChildNodes())
            {
                if (c.SourceStart >= child.SourceStart)
                    break;
                if (c is KeySignatureSyntax k)
                    last = k;
            }
            if (last != null)
                return last;
            child = parent;
        }
        return null;
    }
}
