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

using LilySharp.Core.Midi;
using LilySharp.Core.Music;

namespace LilySharp.Core.Harmony;

/// <summary>A note as it sounds: when (ticks) and how high (MIDI pitch).</summary>
public readonly record struct SoundingNote(int StartTick, int DurationTicks, int Pitch);

/// <summary>
/// One chord read from the music: the ticks it lasts, its root and quality, the lowest note's
/// pitch class (the bass — a slash chord when it is not the root), how well the notes fit it (the
/// share of their weight on its tones), and whether one note at a time sounded there (a melody
/// alone: a reading, not a hearing). <see cref="Root"/> is -1 where nothing sounds.
/// </summary>
public sealed record ChordReading(int StartTick, int EndTick, int Root, ChordQuality Quality, int Bass,
    double Fit, bool Monophonic)
{
    /// <summary>The symbol as a chords row writes it (<c>Am7</c>, <c>G7/B</c>, <c>Bbmaj7</c>),
    /// the roots spelled for a key of <paramref name="keySharps"/> sharps (negative: flats).</summary>
    public string Symbol(int keySharps)
    {
        if (Root < 0)
            return "r";
        string s = HarmonyReader.PitchName(Root, keySharps) + HarmonyReader.SuffixOf(Quality);
        return Bass >= 0 && Bass != Root ? s + "/" + HarmonyReader.PitchName(Bass, keySharps) : s;
    }
}

/// <summary>
/// Reads the chords the notes sound (owner's decisions 2026-10-26, docs/harmony-analysis-design.md):
/// beat by beat, each pitch class weighed by how long it sounds and where it starts (a note struck
/// on the beat counts fully, one struck inside it — a passing or neighbour tone — less), then the
/// quality and root that fit the pooled weight best over stretches of beats, the stretches chosen
/// so that the fit gained by a change outweighs a cost per change. Chords may change on any beat.
/// </summary>
public static class HarmonyReader
{
    private const int DrumChannel = 9;
    private const string ChordRowSuffix = " (chords)";

    /// <summary>The qualities read, plainest first, and how much less likely each is than a triad
    /// when the notes fit both (a share of the weight).</summary>
    private static readonly (ChordQuality Quality, double Prior)[] Qualities =
    [
        (ChordQuality.Major, 0), (ChordQuality.Minor, 0),
        (ChordQuality.Dominant7, -0.12), (ChordQuality.Minor7, -0.12), (ChordQuality.Major7, -0.15),
        (ChordQuality.HalfDiminished7, -0.15), (ChordQuality.Diminished, -0.15),
        (ChordQuality.Diminished7, -0.2), (ChordQuality.Augmented, -0.2),
        (ChordQuality.Sus4, -0.2), (ChordQuality.Dominant7Sus4, -0.25),
    ];

    /// <summary>The longest stretch one chord is read over, in beats (two bars of 4/4).</summary>
    private const int MaxBeats = 8;

    /// <summary>
    /// The pitched notes of <paramref name="midi"/>: the drums left out, and either the chords
    /// rows alone (<paramref name="chordRows"/> true — the chords a file writes, sounded) or every
    /// track but them (false — what the parts play).
    /// </summary>
    public static IReadOnlyList<SoundingNote> NotesOf(MidiFile midi, bool chordRows)
    {
        var notes = new List<SoundingNote>();
        foreach (var track in midi.Tracks)
            foreach (var n in track.Notes)
            {
                if (n.Channel == DrumChannel)
                    continue;
                bool row = (n.Part ?? track.Name).EndsWith(ChordRowSuffix, StringComparison.Ordinal);
                if (row == chordRows)
                    notes.Add(new SoundingNote(n.StartTick, Math.Max(1, n.DurationTicks), n.Pitch));
            }
        return notes;
    }

    /// <summary>
    /// The chords <paramref name="notes"/> sound, over <paramref name="beats"/> (ticks, each
    /// beat to the next, the last to <paramref name="endTick"/>), adjacent readings of one chord
    /// joined. <paramref name="changeCost"/> is what a change must gain, as a share of a beat's
    /// usual weight: higher reads fewer, longer chords.
    /// </summary>
    public static IReadOnlyList<ChordReading> Read(IReadOnlyList<SoundingNote> notes,
        IReadOnlyList<(int Tick, bool Accent)> beats, int endTick, double changeCost = 0.6)
    {
        int count = beats.Count;
        if (count == 0)
            return [];
        var spans = new (int Start, int End)[count];
        for (int i = 0; i < count; i++)
            spans[i] = (beats[i].Tick, i + 1 < count ? beats[i + 1].Tick : Math.Max(beats[i].Tick + 1, endTick));

        // Each beat's weight per pitch class, its lowest note, and whether it ever sounds two at once.
        var weight = new double[count, 12];
        var total = new double[count];
        var lowest = new int[count];
        var mono = new bool[count];
        var sorted = notes.OrderBy(n => n.StartTick).ToList();
        for (int i = 0; i < count; i++)
        {
            var (s, e) = spans[i];
            double length = e - s;
            int low = int.MaxValue, lowAtStart = int.MaxValue;
            var sounding = new List<SoundingNote>();
            foreach (var n in sorted)
            {
                if (n.StartTick >= e)
                    break;
                int ne = n.StartTick + n.DurationTicks;
                if (ne <= s)
                    continue;
                double overlap = Math.Min(ne, e) - Math.Max(n.StartTick, s);
                if (overlap <= 0)
                    continue;
                // Struck inside the beat: a passing or neighbour tone, more often than not.
                double strike = n.StartTick > s ? 0.6 : 1.0;
                double w = overlap / length * strike * (beats[i].Accent ? 1.2 : 1.0);
                weight[i, ((n.Pitch % 12) + 12) % 12] += w;
                total[i] += w;
                sounding.Add(n);
                low = Math.Min(low, n.Pitch);
                if (n.StartTick <= s)
                    lowAtStart = Math.Min(lowAtStart, n.Pitch);
            }
            lowest[i] = lowAtStart != int.MaxValue ? lowAtStart : low;
            mono[i] = !Overlapping(sounding);
        }

        // What a change costs: a share of the median weight of a beat that sounds — once at a
        // bar line, half again at the middle of a bar, more on any other beat: chords change
        // with the bar more often than not, and a walking bass is not a chord a note.
        var weights = total.Where(t => t > 0).OrderBy(t => t).ToList();
        double typical = weights.Count == 0 ? 1 : weights[weights.Count / 2];
        double cost = changeCost * typical;
        var place = new double[count];
        for (int i = 0, bar = 0; i < count; i++)
        {
            if (beats[i].Accent)
                bar = i;
            int next = i + 1;
            while (next < count && !beats[next].Accent)
                next++;
            int length = next - bar, k = i - bar;
            place[i] = k == 0 ? 1.0 : length % 2 == 0 && k == length / 2 ? 1.5 : 2.5;
        }

        // best[j]: the best reading of beats [0, j); from[j] where its last stretch starts.
        var best = new double[count + 1];
        var from = new int[count + 1];
        var chosen = new (int Root, ChordQuality Quality, double Fit)[count + 1];
        for (int j = 1; j <= count; j++)
        {
            best[j] = double.NegativeInfinity;
            var pooled = new double[12];
            double sum = 0;
            for (int i = j - 1; i >= 0 && j - i <= MaxBeats; i--)
            {
                for (int pc = 0; pc < 12; pc++)
                    pooled[pc] += weight[i, pc];
                sum += total[i];
                var (root, quality, score, fit) = BestChord(pooled, sum, lowest[i]);
                double value = best[i] + score - cost * place[i];
                if (value > best[j])
                {
                    best[j] = value;
                    from[j] = i;
                    chosen[j] = (root, quality, fit);
                }
            }
        }

        var stretches = new List<(int I, int J, int Root, ChordQuality Quality, double Fit)>();
        for (int j = count; j > 0; j = from[j])
            stretches.Add((from[j], j, chosen[j].Root, chosen[j].Quality, chosen[j].Fit));
        stretches.Reverse();

        // A rest the chord holds through is part of it; a whole bar where nothing sounds is no
        // chord (a chords row's N.C.), cut out of the stretch it fell in.
        bool WholeBarSilent(int k, out int end)
        {
            end = k;
            if (!beats[k].Accent || total[k] > 0)
                return false;
            int e = k + 1;
            while (e < count && !beats[e].Accent)
                e++;
            for (int m = k; m < e; m++)
                if (total[m] > 0)
                    return false;
            end = e;
            return true;
        }
        var readings = new List<ChordReading>();
        void Add(int i, int j, int root, ChordQuality quality, double fit)
        {
            if (i >= j)
                return;
            int bass = root < 0 ? -1 : ((lowest[i] % 12) + 12) % 12;
            if (root >= 0 && lowest[i] == int.MaxValue)
                bass = root;
            bool alone = Enumerable.Range(i, j - i).All(k => mono[k]);
            readings.Add(new ChordReading(spans[i].Start, spans[j - 1].End, root, quality, bass, fit, alone));
        }
        foreach (var (i, j, root, quality, fit) in stretches)
        {
            int at = i;
            for (int k = i; k < j; k++)
            {
                if (!WholeBarSilent(k, out int end))
                    continue;
                end = Math.Min(end, j);
                Add(at, k, root, quality, fit);
                Add(k, end, -1, ChordQuality.Major, 1);
                at = end;
                k = end - 1;
            }
            Add(at, j, root, quality, fit);
        }

        // One chord read over several stretches is one reading (its bass the first's).
        var joined = new List<ChordReading>();
        foreach (var r in readings)
        {
            if (joined.Count > 0 && joined[^1].Root == r.Root && joined[^1].Quality == r.Quality)
            {
                var last = joined[^1];
                joined[^1] = last with { EndTick = r.EndTick, Fit = Math.Min(last.Fit, r.Fit), Monophonic = last.Monophonic && r.Monophonic };
            }
            else
                joined.Add(r);
        }
        return joined;
    }

    /// <summary>The chord that fits a stretch's pooled weight best: its root (-1 where nothing
    /// sounds), quality, score (in weight) and fit (the share of the weight on its tones).</summary>
    private static (int Root, ChordQuality Quality, double Score, double Fit) BestChord(double[] pooled, double sum, int lowest)
    {
        if (sum <= 0)
            return (-1, ChordQuality.Major, 0, 1);
        int bassPc = lowest == int.MaxValue ? -1 : ((lowest % 12) + 12) % 12;
        (int, ChordQuality, double, double) best = (-1, ChordQuality.Major, double.NegativeInfinity, 0);
        foreach (var (quality, prior) in Qualities)
        {
            var tones = ChordQualityRegistry.GetTones(quality);
            for (int root = 0; root < 12; root++)
            {
                double matched = 0, missing = 0;
                foreach (var tone in tones)
                {
                    double w = pooled[(root + tone.Semitone) % 12];
                    matched += w;
                    // A missing fifth is common; a missing root, third or seventh is not.
                    if (w < 0.05 * sum)
                        missing += tone.DiatonicStep == 4 && tone.Semitone == 7 ? 0.3 : 1.0;
                }
                double score = matched - 0.7 * (sum - matched)
                    - 0.25 * sum * missing / tones.Count
                    + prior * sum
                    + (bassPc == root ? 0.35 * sum : 0);
                if (score > best.Item3)
                    best = (root, quality, score, matched / sum);
            }
        }
        return best;
    }

    private static bool Overlapping(List<SoundingNote> notes)
    {
        for (int a = 0; a < notes.Count; a++)
            for (int b = a + 1; b < notes.Count; b++)
                if (notes[a].StartTick < notes[b].StartTick + notes[b].DurationTicks
                    && notes[b].StartTick < notes[a].StartTick + notes[a].DurationTicks
                    && notes[a].Pitch != notes[b].Pitch)
                    return true;
        return false;
    }

    private static readonly string[] Sharps = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] Flats = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
    private static readonly string[] Plain = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    /// <summary>A pitch class as a chord symbol's root: sharps in a sharp key, flats in a flat
    /// one, the common spelling of each in C.</summary>
    internal static string PitchName(int pc, int keySharps)
        => (keySharps > 0 ? Sharps : keySharps < 0 ? Flats : Plain)[((pc % 12) + 12) % 12];

    /// <summary>The quality as a chords row spells it.</summary>
    internal static string SuffixOf(ChordQuality quality) => quality switch
    {
        ChordQuality.Major => "",
        ChordQuality.Minor => "m",
        ChordQuality.Diminished => "dim",
        ChordQuality.Augmented => "aug",
        ChordQuality.Dominant7 => "7",
        ChordQuality.Major7 => "maj7",
        ChordQuality.Minor7 => "m7",
        ChordQuality.HalfDiminished7 => "m7-5",
        ChordQuality.Diminished7 => "dim7",
        ChordQuality.Sus4 => "sus4",
        ChordQuality.Dominant7Sus4 => "7sus4",
        _ => "",
    };
}
