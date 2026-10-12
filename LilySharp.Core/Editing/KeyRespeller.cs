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
using LilySharp.Core.Harmony;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Respells a section's notes to its key: <c>fis</c> written in G♭ major becomes <c>ges</c>,
/// every note keeping the pitch it sounds. Powers "Lily#: Respell Notes to Key" — a writer
/// transcribing by ear before knowing the key spells the black keys one way, and the key
/// found later wants the other.
/// </summary>
/// <remarks>
/// <para>
/// LILYSHARP-OWN: an editor aid. The rule:
/// ⑴ a pitch class IN the key's scale is spelled as the scale spells it (in G♭ major the
/// pitch F♯/G♭ is <c>ges</c>, B/C♭ is <c>ces</c>);
/// ⑵ a pitch class outside it is left alone when the section spells it ONE way (a written
/// <c>aes</c> in C major is the writer's choice); when the section spells it SEVERAL ways it
/// is unified to the spelling written most, a tie going to the key's way — the minor key's
/// raised sixth and seventh as raised degrees (<c>gis</c> in A minor), otherwise a raised
/// scale note in a sharp key (or C) and a lowered one in a flat key.
/// </para>
/// <para>
/// Only the section's own text is respelled — its cells, or its music inside each part —
/// and in each stream only the notes before a key change written in the middle of it.
/// Octave marks are recomputed by <see cref="OctaveModeConverter.Repin"/> so every note
/// sounds where it did (a respelled letter moves the frame the next relative note reads,
/// and <c>b</c>⇄<c>ces</c> crosses the octave), or the rewrite is refused.
/// </para>
/// </remarks>
public static class KeyRespeller
{
    /// <summary>One respelling and how many notes it applies to; <see cref="Id"/> names it
    /// for <see cref="Respell"/>'s <c>accepted</c> list.</summary>
    public readonly record struct Change(string From, string To, int Count)
    {
        public string Id => From + ">" + To;
    }

    private readonly record struct Proposal(PitchSyntax Pitch, string From, string To, int OctaveShift);

    private static readonly int[] StepSemitones = { 0, 2, 4, 5, 7, 9, 11 };

    /// <summary>The respellings <see cref="Respell"/> would make, most notes first. Empty
    /// when the section is already spelled to the key (or the key cannot be read).</summary>
    public static IReadOnlyList<Change> Plan(string source, string sectionName, string keyText)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return [];
        return Proposals(tree.GetRoot(), sectionName, keyText)
            .GroupBy(p => (p.From, p.To))
            .Select(g => new Change(g.Key.From, g.Key.To, g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.From, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Respells the section's notes to <paramref name="keyText"/> (<c>ges major</c>): all of
    /// <see cref="Plan"/>'s changes, or only those whose <see cref="Change.Id"/> is in
    /// <paramref name="accepted"/>. The result keeps every note at its pitch, or carries an
    /// error and no text.
    /// </summary>
    public static OctaveModeConverter.Result Respell(
        string source, string sectionName, string keyText, IReadOnlyCollection<string>? accepted = null)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return new(null, "Fix the errors in the score first — no changes made.", 0);
        var proposals = Proposals(tree.GetRoot(), sectionName, keyText)
            .Where(p => accepted == null || accepted.Contains(p.From + ">" + p.To))
            .ToList();
        if (proposals.Count == 0)
            return new(null, $"Section {sectionName} is already spelled for {keyText}.", 0);

        // The names change; the marks are Repin's (later edits first, so offsets hold).
        var text = source;
        foreach (var p in proposals.OrderByDescending(p => p.Pitch.SourceStart))
        {
            var name = p.Pitch.PitchToken.Span;
            text = text[..name.Start] + p.To + text[name.End..];
        }
        var shifts = proposals.Where(p => p.OctaveShift != 0)
            .ToDictionary(p => p.Pitch.SourceStart, p => p.OctaveShift);
        var result = OctaveModeConverter.Repin(source, text, shifts);
        return result.NewText == null ? result : result with { ChangedPitches = proposals.Count };
    }

    private static List<Proposal> Proposals(SyntaxNode root, string sectionName, string keyText)
    {
        var words = keyText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 2 || KeySpelling.SharpsFor(words[0], words[1]) is not int sharps)
            return [];
        bool minor = words[1].Equals("minor", StringComparison.OrdinalIgnoreCase);
        int tonicStep = KeySpelling.StepOf(words[0][0]);
        int tonicPc = Mod12(StepSemitones[tonicStep] + KeySpelling.Alteration(tonicStep, sharps));
        bool contracted = UsesContractedFlats(root);

        // The section's written pitches, as (step, alteration).
        var written = new List<(PitchSyntax Pitch, int Step, int Alter)>();
        var starting = new HashSet<KeySignatureSyntax>(KeyEstimator.StartingKeys(root, sectionName));
        foreach (var section in root.DescendantNodes<SectionDeclarationSyntax>())
        {
            if (section.SectionName != sectionName)
                continue;
            var midKeys = section.DescendantNodes<KeySignatureSyntax>().Where(k => !starting.Contains(k)).ToList();
            foreach (var p in section.DescendantNodes<PitchSyntax>())
            {
                if (p.Parent is not (NoteSyntax or ChordSyntax or ArpeggioSyntax) || p.QuarterOffset != 0)
                    continue;
                int step = KeySpelling.StepOf(p.BaseName);
                if (step < 0)
                    continue;
                var stream = Stream(p);
                if (midKeys.Any(k => k.SourceStart < p.SourceStart && Stream(k) == stream))
                    continue;
                written.Add((p, step, p.AccidentalOffset));
            }
        }

        // ⑴ the scale's spelling of each pitch class; ⑵ the unification of the others.
        var scale = new Dictionary<int, (int Step, int Alter)>();
        for (int s = 0; s < 7; s++)
            scale[Mod12(StepSemitones[s] + KeySpelling.Alteration(s, sharps))] = (s, KeySpelling.Alteration(s, sharps));
        var target = new Dictionary<int, (int Step, int Alter)>(scale);
        foreach (var byPc in written.GroupBy(w => Mod12(StepSemitones[w.Step] + w.Alter)))
        {
            if (scale.ContainsKey(byPc.Key))
                continue;
            var spellings = byPc.GroupBy(w => (w.Step, w.Alter)).ToList();
            if (spellings.Count < 2)
                continue;
            var keyWay = Chromatic(byPc.Key, sharps, minor, tonicStep, tonicPc);
            target[byPc.Key] = spellings
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key == keyWay)
                .First().Key;
        }

        var proposals = new List<Proposal>();
        foreach (var (pitch, step, alter) in written)
        {
            int pc = Mod12(StepSemitones[step] + alter);
            if (!target.TryGetValue(pc, out var to) || (to.Step == step && to.Alter == alter))
                continue;
            int shift = (StepSemitones[step] + alter - StepSemitones[to.Step] - to.Alter) / 12;
            proposals.Add(new Proposal(pitch, pitch.PitchName, Name(to.Step, to.Alter, contracted), shift));
        }
        return proposals;
    }

    // The key's spelling of a pitch class outside its scale.
    private static (int Step, int Alter) Chromatic(int pc, int sharps, bool minor, int tonicStep, int tonicPc)
    {
        // A minor key raises its sixth and seventh degrees.
        if (minor && (pc == Mod12(tonicPc + 9) || pc == Mod12(tonicPc + 11)))
        {
            int step = (tonicStep + (pc == Mod12(tonicPc + 9) ? 5 : 6)) % 7;
            return (step, KeySpelling.Alteration(step, sharps) + 1);
        }
        int bend = sharps >= 0 ? 1 : -1;
        for (int pass = 0; pass < 2; pass++, bend = -bend)
            for (int s = 0; s < 7; s++)
            {
                int a = KeySpelling.Alteration(s, sharps) + bend;
                if (Math.Abs(a) <= 2 && Mod12(StepSemitones[s] + a) == pc)
                    return (s, a);
            }
        return (0, pc); // unreachable: every pitch class is a raised or lowered scale note
    }

    // The stream a node's pitch is read in: the cell, else the section.
    private static SyntaxNode? Stream(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PartBlockSyntax or SectionDeclarationSyntax)
                return p;
        return null;
    }

    // `es`/`as` rather than `ees`/`aes` when the file already writes them so.
    private static bool UsesContractedFlats(SyntaxNode root)
        => root.DescendantNodes<PitchSyntax>().Any(p => p.PitchToken.Text is "es" or "as" or "eses" or "ases");

    private static string Name(int step, int alter, bool contracted)
    {
        char letter = "cdefgab"[step];
        if (contracted && letter is 'e' or 'a' && alter < 0)
            return alter == -1 ? letter + "s" : letter + "ses";
        return letter + alter switch { 2 => "isis", 1 => "is", -1 => "es", -2 => "eses", _ => "" };
    }

    private static int Mod12(int x) => ((x % 12) + 12) % 12;
}
