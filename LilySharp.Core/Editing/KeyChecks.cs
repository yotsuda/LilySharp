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
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// What a person's piece says of its key (owner's points 2026-10-30, docs/review-tool-design.md §4): the
/// key heard against the key written, notes outside the key that lead nowhere, and a chromatic passing
/// note spelled against its direction. The review's alone: a composing AI spells by the guide.
/// </summary>
public static class KeyChecks
{
    private static readonly int[] MajorSteps = [0, 2, 4, 5, 7, 9, 11];

    // key.signature: this many notes at least, to hear a key in.
    private const int HeardFewestNotes = 16;
    // …the signature heard holding nearly all the sounding time, and this much more of it than the one written.
    private const double HeardInScale = 0.9, HeardBetterBy = 0.1;
    // key.outside: this share of a line's notes and more is the music's own language, not a slip.
    private const double OutsideMostShare = 0.1;
    // …and a line whose middle note lies under C3 is a bass, whatever plays it.
    private const int BassRegisterTop = 48;

    /// <summary>A key as the notes suggest it, with how well it fits (a correlation, at most 1).</summary>
    public readonly record struct HeardKey(int Tonic, bool Minor, double Fit)
    {
        /// <summary>The signature of this key, in fifths (sharps positive).</summary>
        public int Fifths
        {
            get
            {
                int fifths = FifthsOf(Minor ? Tonic + 3 : Tonic);
                // Six flats and six sharps name the same notes: E♭ minor is the key a piece is written in, D♯ minor is not.
                return Minor && fifths == 6 ? -6 : fifths;
            }
        }

        /// <summary>As a person says it: E minor, B♭ major.</summary>
        public string Name => $"{PartRanges.SpelledFromPitch(Tonic + 60, Fifths)[..^1]} {(Minor ? "minor" : "major")}";
    }

    private static int FifthsOf(int majorTonic)
    {
        int f = ((majorTonic % 12 + 12) % 12) * 7 % 12;
        return f > 6 ? f - 12 : f;
    }

    private static HashSet<int> ScaleOf(int fifths) => MajorSteps.Select(s => ((s + fifths * 7) % 12 + 12) % 12).ToHashSet();

    /// <summary>The keys the whole piece's notes fit, best first (<see cref="Harmony.KeyEstimator.ProfileFit"/>;
    /// the estimator itself reads one section, and leans on its last bass note). Empty with too few notes to
    /// tell. The drums and a chords row's voicings are left out.</summary>
    public static IReadOnlyList<HeardKey> Heard(MidiFile midi)
    {
        var notes = Pitched(midi);
        if (notes.Count < HeardFewestNotes)
            return [];
        var weights = Weights(notes);
        return Enumerable.Range(0, 12)
            .SelectMany(t => new[] { new HeardKey(t, false, Harmony.KeyEstimator.ProfileFit(weights, true, t)), new HeardKey(t, true, Harmony.KeyEstimator.ProfileFit(weights, false, t)) })
            .OrderByDescending(k => k.Fit).ToList();
    }

    private static List<MidiNote> Pitched(MidiFile midi) => midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9
        && !(n.Part ?? t.Name).EndsWith(" (chords)", System.StringComparison.Ordinal))).ToList();

    private static double[] Weights(IEnumerable<MidiNote> notes)
    {
        var weights = new double[12];
        foreach (var n in notes)
            weights[(n.Pitch % 12 + 12) % 12] += System.Math.Max(1, n.DurationTicks);
        return weights;
    }

    /// <summary>The findings for a piece that compiles. A piece that changes key, or whose notes do not
    /// sound as written (a transposing part, its file not in concert pitch), is not read by the two checks
    /// that weigh the sounding notes against one key.</summary>
    public static IEnumerable<CompositionFinding> Of(string source, SyntaxNode root, MidiFile midi)
    {
        var parts = PartsOf(root);
        bool concert = IsConcert(source);
        var findings = new List<CompositionFinding>();
        foreach (var finding in AgainstTheirDirection(source, root, parts, concert))
            findings.Add(finding);
        // A mode has its own notes, which this major-or-minor reading does not know.
        if (TheOneKey(source, root) is not { } keys || keys.Mode is not ("major" or "minor"))
            return findings;
        int fifths = WrittenFifths(root, keys);
        if (Signature(midi, keys, fifths) is { } signature)
            findings.Add(signature);
        findings.AddRange(Outside(source, midi, keys.Mode == "minor", fifths, CompositionChecks.SwungBlueNotes(root)));
        if (ChordChecks.ChordKey.Of(keys.Tonic, keys.Mode) is { } key)
            findings.AddRange(ChordChecks.Findings(source, root, midi, key));
        return findings;
    }

    /// <summary>The written key's signature. A key past seven accidentals (`des minor`, eight flats, the notes of
    /// C♯ minor; Senko-no-Michi) is not in <see cref="PartRanges.KeyFifths"/>' table, which reads it as C major:
    /// such a key is read by its notes instead.</summary>
    private static int WrittenFifths(SyntaxNode root, (string Tonic, string Mode) key)
    {
        int tonic = CompositionChecks.PitchClass(key.Tonic);
        int byNotes = FifthsOf(key.Mode == "major" ? tonic : tonic + 3), table = PartRanges.KeyFifths(root);
        return ScaleOf(table).SetEquals(ScaleOf(byNotes)) ? table : byNotes;
    }

    private static List<PartDeclarationSyntax> PartsOf(SyntaxNode root)
        => root.DescendantNodes<PartDeclarationSyntax>().Where(p => !p.IsStrokes).GroupBy(p => p.Name.Text).Select(g => g.First()).ToList();

    private static bool IsConcert(string source) => System.Text.RegularExpressions.Regex.IsMatch(source, @"(?m)^\s*pitch\s+concert\b");

    /// <summary>The one key a piece's sounding notes can be weighed against; null when it changes key, or when its
    /// notes do not sound as written (a transposing part, the file not in concert pitch).</summary>
    internal static (string Tonic, string Mode)? TheOneKey(string source, SyntaxNode root)
    {
        bool sounding = IsConcert(source) || PartsOf(root).All(p => Svg.Model.InstrumentDefaults.GetTransposition(CompositionChecks.InstrumentOf(p)) % 12 == 0);
        return sounding && CompositionChecks.PlayedKeys(root) is { } keys
            && root.DescendantNodes<KeySignatureSyntax>().Select(k => k.ToFullString().Trim()).Distinct().Count() <= 1
            ? keys.Home : null;
    }

    /// <summary>The signature the notes are in, where it is not the one written: the forgotten key signature,
    /// every F♯ and C♯ written as an accidental. The relative key shares its signature, so E minor heard under
    /// a written G major is nothing. Minor keys' raised sixths and sevenths lie outside their signature, which
    /// is why the signature heard must hold more of the time by a margin as well as nearly all of it.</summary>
    private static CompositionFinding? Signature(MidiFile midi, (string Tonic, string Mode) written, int fifths)
    {
        if (Heard(midi) is not { Count: > 0 } heard)
            return null;
        var weights = Weights(Pitched(midi));
        double Share(int f) => ScaleOf(f).Sum(pc => weights[pc]) / weights.Sum();
        double there = Share(fifths), best = Share(heard[0].Fifths);
        if (best < HeardInScale || best - there < HeardBetterBy)
            return null;
        var outside = Enumerable.Range(0, 12).Where(pc => !ScaleOf(fifths).Contains(pc) && ScaleOf(heard[0].Fifths).Contains(pc))
            .OrderByDescending(pc => weights[pc]).Select(pc => PartRanges.SpelledFromPitch(pc + 60, heard[0].Fifths)[..^1]).ToList();
        return new("key.signature", $"The key says {CompositionChecks.TonicName(written.Tonic)} {written.Mode}, and the notes are heard in {heard[0].Name}: "
            + $"{(int)System.Math.Round((1 - there) * 100)}% of the sounding time is on notes outside the key written ({string.Join(", ", outside)}). "
            + $"If it is in {heard[0].Name}, write that as the `key`, and those notes need no accidentals.");
    }

    /// <summary>A line's notes outside the key that lead nowhere: neither a semitone from a note of the key
    /// before or after them (a leading tone, a passing or neighbour note), nor one of a chromatic run, nor
    /// sounding in the harmony around them, from another part or from the line's own part's other voices (a
    /// chord from outside the key is a chord, not a slip: a solo guitar's tune on the E of the C7 its own thumb
    /// holds, Cafe-no-Midori), nor
    /// the bottom of the harmony: a line in a bass's register, or the lowest note sounding, plays the root of
    /// whatever chord is there, and an out-of-key root is that chord's, not a slip (the census: nearly all
    /// of 168 such notes were in the bass lines of the owner's books, under chords no part wrote out).
    /// In a piece that swings, its blue notes (<paramref name="blueNotes"/>,
    /// <see cref="CompositionChecks.SwungBlueNotes"/>) are the key's own, as a minor key's raised sixth
    /// and seventh are (owner's trial 2026-11-07: a swinging blues's D and G in E were told slips).
    /// A note that reaches a semitone neighbour one note later leads there too: a leading tone that steps
    /// onto its chord's next note first, `fis a g` (owner's trial 2026-11-07: a duo's F♯ over D, the
    /// third of a secondary dominant going to G minor, was told a slip). The note is named as written.</summary>
    private static IEnumerable<CompositionFinding> Outside(string? source, MidiFile midi, bool minor, int fifths, int[]? blueNotes = null)
    {
        var scale = ScaleOf(fifths);
        if (blueNotes != null)
            scale.UnionWith(blueNotes);
        if (minor)
        {
            // The raised sixth and seventh are the minor key's own (in A minor, F♯ and G♯): from its
            // relative major's tonic (C), a tritone and a minor sixth up.
            int relative = ((fifths * 7) % 12 + 12) % 12;
            scale.Add((relative + 6) % 12);
            scale.Add((relative + 8) % 12);
        }
        static int Pc(int pitch) => (pitch % 12 + 12) % 12;
        var everything = midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9).Select(n => (Part: n.Part ?? t.Name, n.Voice, n.StartTick, n.DurationTicks, n.Pitch))).ToList();
        var lineVoices = CompositionChecks.LineVoices(midi);
        var bars = midi.BarTicks.Where(b => b > 0).Distinct().OrderBy(b => b).ToList();
        foreach (var line in CompositionChecks.LineNotes(midi).Where(n => !n.Part.EndsWith(" (chords)", System.StringComparison.Ordinal)).GroupBy(n => n.Part))
        {
            if (line.Select(n => n.Pitch).Order().ElementAt(line.Count() / 2) < BassRegisterTop)
                continue;
            var notes = line.GroupBy(n => n.StartTick).Where(g => g.Count() == 1).Select(g => g.First()).OrderBy(n => n.StartTick).ToList();
            var strays = new List<int>();
            for (int i = 0; i < notes.Count; i++)
            {
                int pc = Pc(notes[i].Pitch);
                if (scale.Contains(pc))
                    continue;
                bool Beside(int j) => j >= 0 && j < notes.Count && System.Math.Abs(notes[j].Pitch - notes[i].Pitch) == 1;
                if (Beside(i - 1) || Beside(i + 1) || Beside(i + 2))
                    continue;
                var note = notes[i];
                bool Other((string Part, int Voice, int StartTick, int DurationTicks, int Pitch) o) => o.Part != line.Key || o.Voice != lineVoices[line.Key];
                var around = everything.Where(o => Other(o)
                    && o.StartTick < note.StartTick + note.DurationTicks && note.StartTick < o.StartTick + o.DurationTicks).ToList();
                // The harmony of its beat, as well: an accompaniment that breaks its chord sounds the note's pitch
                // in the beat, not at its instant (owner's trial 2026-11-07: a song's B♯ in a G♯7 the piano spread
                // over the dotted quarter, `bis8 dis fis`, was told a slip). The bottom of the harmony is still read
                // at its instant.
                var (beatStart, beatEnd) = BeatOf(midi, bars, note.StartTick);
                // …and of the bass it stands over, for as long as that bass holds: a broken chord that comes round
                // twice in the bar over one held bass is one chord (the same song's second B♯, a beat after the voice's).
                var bass = around.Where(o => o.Pitch < note.Pitch).OrderBy(o => o.Pitch).Select(o => ((int, int)?)(o.StartTick, o.StartTick + o.DurationTicks)).FirstOrDefault();
                bool InSpan(int from, int to, (string Part, int Voice, int StartTick, int DurationTicks, int Pitch) o) => o.StartTick < to && from < o.StartTick + o.DurationTicks;
                bool inBeat = everything.Any(o => Other(o) && Pc(o.Pitch) == pc
                    && (InSpan(beatStart, beatEnd, o) || (bass is var (bassFrom, bassTo) && InSpan(bassFrom, bassTo, o))));
                if (inBeat || around.Any(o => Pc(o.Pitch) == pc) || (around.Count > 0 && around.All(o => o.Pitch > note.Pitch)))
                    continue;
                strays.Add(i);
            }
            if (strays.Count == 0 || strays.Count > notes.Count * OutsideMostShare)
                continue;
            var at = strays.Select(i => 1 + bars.Count(b => b <= notes[i].StartTick)).Distinct().ToList();
            // Named as the page writes it (owner's trial 2026-11-07: a `fis` in D minor was told "G♭5"), else as the key spells it.
            var first = notes[strays[0]];
            int pos = midi.Tracks.SelectMany(t => t.Notes.Where(n => (n.Part ?? t.Name) == line.Key && n.StartTick == first.StartTick && n.Pitch == first.Pitch))
                .Select(n => n.SourcePos).FirstOrDefault(-1);
            string name = PartRanges.WrittenName(source, pos, first.Pitch) ?? PartRanges.SpelledFromPitch(first.Pitch, fifths);
            yield return new("key.outside", $"{line.Key}: {strays.Count} {(strays.Count == 1 ? "note" : "notes")} outside the key that "
                + $"{(strays.Count == 1 ? "leads" : "lead")} nowhere ({name} in bar {at[0]}"
                + (at.Count > 1 ? $" first; bars {string.Join(", ", at.Take(6))}{(at.Count > 6 ? ", …" : "")}" : "")
                + ", as the form plays them): no semitone into a note of the key, and no chord around it has that note. Is it the note you meant?") { Parts = [line.Key], Bars = at };
        }
    }

    /// <summary>The beat <paramref name="tick"/> falls in, as ticks from its start to its end: a beat of the
    /// meter in force, counted from the bar line — the denominator's note, or three of them in a compound
    /// meter (6/8, 9/8, 12/8: a dotted quarter). <paramref name="bars"/> are the bar lines after the first.</summary>
    private static (int Start, int End) BeatOf(MidiFile midi, IReadOnlyList<int> bars, int tick)
    {
        int numerator = 4, denominator = 4, at = -1;
        foreach (var m in midi.Tracks.SelectMany(t => t.TimeSignatures))
            if (m.Tick <= tick && m.Tick >= at)
                (numerator, denominator, at) = (m.Numerator, m.Denominator, m.Tick);
        int unit = System.Math.Max(1, midi.TicksPerQuarterNote * 4 / System.Math.Max(1, denominator));
        int beat = numerator > 3 && numerator % 3 == 0 && denominator >= 8 ? unit * 3 : unit;
        int bar = bars.LastOrDefault(b => b <= tick);
        int start = bar + (tick - bar) / beat * beat;
        return (start, start + beat);
    }

    /// <summary>A chromatic passing note, a semitone on each side and moving on the same way, written against
    /// its direction: rising C–D♭–D, falling D–C♯–C. A note of the key is left alone.</summary>
    private static IEnumerable<CompositionFinding> AgainstTheirDirection(string source, SyntaxNode root, IReadOnlyList<PartDeclarationSyntax> parts, bool concert)
    {
        foreach (var byPart in CompositionChecks.MusicBlocks(root, parts, concert).GroupBy(b => b.Part))
        {
            var found = new List<(string Written, string Right, bool Rising, int Start)>();
            foreach (var (_, music, key) in byPart)
            {
                if (key is not var (tonic, mode) || mode is not ("major" or "minor"))
                    continue;
                int tonicPc = CompositionChecks.PitchClass(tonic);
                var scale = ScaleOf(FifthsOf(mode == "major" ? tonicPc : tonicPc + 3));
                var line = music.DescendantNodes<PitchSyntax>().Where(p => p.Parent is NoteSyntax)
                    .Select(p => p.GetChild(0) is SyntaxTokenNode t && t.Text.Length > 0 ? (Name: t.Text.ToLowerInvariant(), Pitch: p) : (Name: "", Pitch: p))
                    .Where(p => p.Name.Length > 0).ToList();
                for (int i = 1; i + 1 < line.Count; i++)
                {
                    int here = CompositionChecks.PitchClass(line[i].Name);
                    if (scale.Contains(here))
                        continue;
                    int from = (here - CompositionChecks.PitchClass(line[i - 1].Name) + 12) % 12, to = (CompositionChecks.PitchClass(line[i + 1].Name) - here + 12) % 12;
                    bool flat = line[i].Name.Length > 1 && line[i].Name.EndsWith('s') && !line[i].Name.EndsWith("is", System.StringComparison.Ordinal);
                    bool sharp = line[i].Name.EndsWith("is", System.StringComparison.Ordinal);
                    // Named from the plain note it leaves: the lower raised, the upper lowered.
                    if (line[i - 1].Name.Length > 1)
                        continue;
                    if (from == 1 && to == 1 && flat)
                        found.Add((line[i].Name, line[i - 1].Name + "is", true, line[i].Pitch.Span.Start));
                    else if (from == 11 && to == 11 && sharp)
                        found.Add((line[i].Name, line[i - 1].Name + "es", false, line[i].Pitch.Span.Start));
                }
            }
            if (found.Count == 0)
                continue;
            var first = found[0];
            var finding = new CompositionFinding("spelling.direction", $"{byPart.Key}: {found.Count} chromatic passing {(found.Count == 1 ? "note" : "notes")} spelled against "
                + $"{(found.Count == 1 ? "its" : "their")} direction — `{first.Written}` on the way {(first.Rising ? "up" : "down")}: write `{first.Right}`.")
            {
                Parts = [byPart.Key],
                Line = 1 + source.AsSpan(0, first.Start).Count('\n'),
                Written = first.Written,
            };
            yield return finding;
        }
    }
}
